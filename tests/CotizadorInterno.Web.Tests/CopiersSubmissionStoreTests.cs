using System.Security.Claims;
using System.Text;
using System.Text.Json;
using CotizadorInterno.Web.Models.CopiersMtoV2;
using CotizadorInterno.Web.Services.CopiersMtoV2;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CotizadorInterno.Web.Tests;

public sealed class CopiersSubmissionStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "CopiersDurableTests", Guid.NewGuid().ToString("N"));
    private const string Owner = "tenant/technician";
    private const string Key = "copiers-durable-test-0001";
    private CopiersSubmissionStore Store() => new(Path.Combine(_root, "receipts"), DataProtectionProvider.Create(new DirectoryInfo(Path.Combine(_root, "keys"))));
    private static CopiersMaintenanceV2DraftRequestDto Draft() => new() { SubmissionKey = Key, ClientId = Guid.Empty.ToString(), ClientName = "CUSTOMER PRIVATE", EquipmentSerial = "TEST" };
    private static CopiersMaintenanceV2ActorContext Actor() => new() { SystemUserId = "d21f6e36-7ca4-4f19-88c3-069c0218a5e8", DisplayName = "TECH PRIVATE", Email = "tech@example.test" };
    private static CopiersMaintenanceV2FinalizeMultipartRequestDto Request(string text = "SIGNED ORIGINAL") => new()
    {
        SubmissionKey = Key, WorkPerformed = text, CustomerAccepted = true,
        Signature = File("Signature", "signature.jpg", [1, 2, 3, 4]), Attachments = [File("Attachments", "photo.jpg", [5, 6, 7])]
    };
    private static IFormFile File(string field, string name, byte[] bytes) => new FormFile(new MemoryStream(bytes), 0, bytes.Length, field, name) { Headers = new HeaderDictionary(), ContentType = "image/jpeg" };

    [Fact] public async Task StatusBeforeFirstReceiptReturnsNotFoundWithoutCreatingStorage()
    {
        var store = Store();
        Assert.False(Directory.Exists(Path.Combine(_root, "receipts")));
        Assert.Null(await store.FindAsync(Owner, Key, default));
        Assert.Empty(store.PendingIds());
        Assert.False(Directory.Exists(Path.Combine(_root, "receipts")));
    }
    [Fact] public async Task FirstStatusThenReceiveAndRetryPreservesOneOriginalReceipt()
    {
        var store = Store();
        Assert.Null(await store.FindAsync(Owner, Key, default));
        var first = await store.ReceiveAsync(Owner, Draft(), Request(), Actor(), default);
        var recovered = await Store().FindAsync(Owner, Key, default);
        Assert.Equal(first.Fingerprint, recovered!.Fingerprint);
        var replay = await Store().ReceiveAsync(Owner, Draft(), Request(), Actor(), default);
        Assert.Equal(first.ReceivedAtUtc, replay.ReceivedAtUtc);
        Assert.Single(store.PendingIds());
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, recovered.Payload!.Files[0].Content);
    }
    [Fact] public async Task MissingReceiptInExistingDirectoryReturnsNotFound()
    {
        Directory.CreateDirectory(Path.Combine(_root, "receipts"));
        Assert.Null(await Store().FindAsync(Owner, Key, default));
    }
    [Fact] public async Task CorruptReceiptMustNotLookLikeAnUnreceivedSubmission()
    {
        var store = Store();
        var first = await store.ReceiveAsync(Owner, Draft(), Request(), Actor(), default);
        await System.IO.File.WriteAllBytesAsync(Path.Combine(_root, "receipts", first.Id + ".json"), [1, 2, 3]);
        await Assert.ThrowsAsync<System.Security.Cryptography.CryptographicException>(() => store.FindAsync(Owner, Key, default));
    }

    [Fact] public async Task RestartRestoresExactOriginalFilesAndNoRequestResources()
    {
        var receipt = await Store().ReceiveAsync(Owner, Draft(), Request(), Actor(), default);
        var restored = await Store().FindAsync(Owner, Key, default);
        Assert.Equal(receipt.Id, restored!.Id);
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, restored.Payload!.Files[0].Content);
        Assert.Equal(new byte[] { 5, 6, 7 }, restored.Payload.Files[1].Content);
        var decoded = JsonSerializer.Deserialize<CopiersMaintenanceV2FinalizeMultipartRequestDto>(restored.Payload.RequestJson)!;
        Assert.Null(decoded.Signature); Assert.Empty(decoded.Attachments);
        Assert.Equal("SIGNED ORIGINAL", decoded.WorkPerformed);
    }
    [Fact] public async Task ReceiptIsEncryptedAtRest()
    {
        var receipt = await Store().ReceiveAsync(Owner, Draft(), Request(), Actor(), default);
        var bytes = await System.IO.File.ReadAllBytesAsync(Path.Combine(_root, "receipts", receipt.Id + ".json"));
        Assert.DoesNotContain("SIGNED ORIGINAL", Encoding.UTF8.GetString(bytes));
        Assert.DoesNotContain("CUSTOMER PRIVATE", Encoding.UTF8.GetString(bytes));
    }
    [Fact] public async Task SameKeyDifferentOwnerCannotReadReceipt()
    {
        await Store().ReceiveAsync(Owner, Draft(), Request(), Actor(), default);
        Assert.Null(await Store().FindAsync("tenant/another-user", Key, default));
    }
    [Fact] public async Task ExactReplayReturnsOneReceipt()
    {
        var first = await Store().ReceiveAsync(Owner, Draft(), Request(), Actor(), default);
        var second = await Store().ReceiveAsync(Owner, Draft(), Request(), Actor(), default);
        Assert.Equal(first.Fingerprint, second.Fingerprint);
        Assert.Equal(first.ReceivedAtUtc, second.ReceivedAtUtc);
        Assert.Single(Store().PendingIds());
    }
    [Fact] public async Task ChangedSignedContentCannotOverwriteAcceptedReceipt()
    {
        await Store().ReceiveAsync(Owner, Draft(), Request(), Actor(), default);
        await Assert.ThrowsAsync<CopiersMaintenanceV2ConcurrencyException>(() => Store().ReceiveAsync(Owner, Draft(), Request("CHANGED"), Actor(), default));
        Assert.Contains("SIGNED ORIGINAL", (await Store().FindAsync(Owner, Key, default))!.Payload!.RequestJson);
    }
    [Fact] public async Task ChangedPhotoCannotReuseReceipt()
    {
        await Store().ReceiveAsync(Owner, Draft(), Request(), Actor(), default);
        var changed = Request(); changed.Attachments = [File("Attachments", "photo.jpg", [9])];
        await Assert.ThrowsAsync<CopiersMaintenanceV2ConcurrencyException>(() => Store().ReceiveAsync(Owner, Draft(), changed, Actor(), default));
    }
    [Fact] public async Task ParallelWorkersAdmitOnlyOneCopy()
    {
        var receipts = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Store().ReceiveAsync(Owner, Draft(), Request(), Actor(), default)));
        Assert.Single(receipts.Select(x => x.ReceivedAtUtc).Distinct()); Assert.Single(Store().PendingIds());
    }
    [Fact] public async Task CancellationBeforeAdmissionLeavesNoReceipt()
    {
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Store().ReceiveAsync(Owner, Draft(), Request(), Actor(), new CancellationToken(true)));
        Assert.Empty(Store().PendingIds());
    }
    [Fact] public async Task CompletedReceiptCanDropSensitivePayloadAndStillAnswerStatus()
    {
        var store = Store(); var item = await store.ReceiveAsync(Owner, Draft(), Request(), Actor(), default);
        item.Result = new() { RecordId = Guid.NewGuid().ToString(), State = CopiersMaintenanceV2WorkflowState.ReadyToSend };
        item.Payload = null; await store.WriteAsync(item, default);
        var recovered = await Store().FindAsync(Owner, Key, default);
        Assert.Null(recovered!.Payload); Assert.NotNull(recovered.Result);
        Assert.Contains("completed", JsonSerializer.Serialize(CopiersSubmissionStore.Status(recovered)));
        Assert.DoesNotContain("SIGNED ORIGINAL", JsonSerializer.Serialize(CopiersSubmissionStore.Status(recovered)));
    }
    [Fact] public async Task PendingLimitNeverDeletesAnExistingReceipt()
    {
        for (int i = 0; i < 10; i++) { var r = Request(); r.SubmissionKey = Key + i; await Store().ReceiveAsync(Owner, Draft(), r, Actor(), default); }
        await Assert.ThrowsAsync<CopiersMaintenanceV2ValidationException>(() => Store().ReceiveAsync(Owner, Draft(), Request(), Actor(), default));
        Assert.Equal(10, Store().PendingIds().Count());
    }
    [Fact] public void OwnerRequiresAuthenticatedTenantAndObjectId()
    {
        Assert.Throws<UnauthorizedAccessException>(() => CopiersSubmissionStore.Owner(new ClaimsPrincipal()));
        var tenant = Guid.NewGuid().ToString(); var id = Guid.NewGuid().ToString();
        Assert.Equal(tenant + "/" + id, CopiersSubmissionStore.Owner(new ClaimsPrincipal(new ClaimsIdentity([new Claim("tid", tenant), new Claim("oid", id)], "test"))));
    }
    [Fact] public void NoLocalCacheFallbackIsAllowedInProduction()
    {
        Assert.Throws<InvalidOperationException>(() => CopiersSubmissionStore.ResolveDirectory(_root, key => key switch { "WEBSITE_SITE_NAME" => "app", "WEBSITE_LOCAL_CACHE_OPTION" => "Always", _ => null }));
        Assert.Throws<InvalidOperationException>(() => CopiersSubmissionStore.ResolveDirectory(_root, key => key == "WEBSITE_SITE_NAME" ? "app" : null));
        Assert.StartsWith(_root, CopiersSubmissionStore.ResolveDirectory("ignored", key => key switch { "WEBSITE_SITE_NAME" => "app", "HOME" => _root, _ => null }));
    }
    [Fact] public void ClientCannotDeserializeTrustedWorkerTimestamp()
    {
        var request = JsonSerializer.Deserialize<CopiersMaintenanceV2FinalizeMultipartRequestDto>("{\"DurableReceivedAtUtc\":\"2099-01-01T00:00:00Z\"}");
        Assert.Null(request!.DurableReceivedAtUtc);
    }

    private CopiersSubmissionWorker Worker(CopiersSubmissionStore store) => new(store,
        new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(), NullLogger<CopiersSubmissionWorker>.Instance);
    [Fact] public async Task TwoWorkersProcessOneReceiptOnlyOnce()
    {
        var store = Store(); var item = await store.ReceiveAsync(Owner, Draft(), Request(), Actor(), default);
        var calls = 0;
        async Task<CopiersMaintenanceV2FinalizeResultDto> Run(CopiersSubmission work, CancellationToken ct) { Interlocked.Increment(ref calls); await Task.Delay(100, ct); return new() { State = CopiersMaintenanceV2WorkflowState.ReadyToSend }; }
        await Task.WhenAll(Worker(store).ProcessAsync(item.Id, default, Run), Worker(Store()).ProcessAsync(item.Id, default, Run));
        Assert.Equal(1, calls); Assert.NotNull((await Store().FindAsync(Owner, Key, default))!.Result);
    }
    [Fact] public async Task NetworkFailureKeepsPayloadAndRestartCanRetrySameKey()
    {
        var store = Store(); var item = await store.ReceiveAsync(Owner, Draft(), Request(), Actor(), default);
        await Worker(store).ProcessAsync(item.Id, default, (_, _) => throw new HttpRequestException("offline"));
        var failed = (await Store().FindAsync(Owner, Key, default))!;
        Assert.False(failed.NeedsReview); Assert.NotNull(failed.Payload); Assert.Null(failed.Result);
        Assert.True(failed.NextAttemptUtc > DateTimeOffset.UtcNow);
        failed.NextAttemptUtc = default; await store.WriteAsync(failed, default);
        await Worker(Store()).ProcessAsync(item.Id, default, (work, _) => {
            Assert.Equal(item.Fingerprint, work.Fingerprint); Assert.Equal(Key, work.SubmissionKey);
            return Task.FromResult(new CopiersMaintenanceV2FinalizeResultDto { State = CopiersMaintenanceV2WorkflowState.ReadyToSend }); });
        Assert.Null((await Store().FindAsync(Owner, Key, default))!.Payload);
    }
    [Fact] public async Task ValidationFailureStopsAutomaticRetryButPreservesOriginal()
    {
        var store = Store(); var item = await store.ReceiveAsync(Owner, Draft(), Request(), Actor(), default);
        await Worker(store).ProcessAsync(item.Id, default, (_, _) => throw new CopiersMaintenanceV2ValidationException("invalid", "Revisar lectura"));
        var failed = (await Store().FindAsync(Owner, Key, default))!;
        Assert.True(failed.NeedsReview); Assert.NotNull(failed.Payload); Assert.Equal("Revisar lectura", failed.Error);
        await Worker(Store()).ProcessAsync(item.Id, default, (_, _) => throw new Exception("Must not run"));
        Assert.Equal(1, (await Store().FindAsync(Owner, Key, default))!.Attempts);
    }
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
