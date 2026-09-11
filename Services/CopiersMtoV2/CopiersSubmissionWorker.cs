using System.Security.Claims;
using System.Text.Json;
using CotizadorInterno.Web.Models.CopiersMtoV2;
using Microsoft.Extensions.Options;

namespace CotizadorInterno.Web.Services.CopiersMtoV2;

public sealed class CopiersSubmissionWorker(CopiersSubmissionStore store, IServiceScopeFactory scopes,
    ILogger<CopiersSubmissionWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                foreach (var id in store.PendingIds())
                {
                    var snapshot = await store.ReadAsync(id, stoppingToken);
                    if (snapshot is null || snapshot.Result is not null || snapshot.NeedsReview || snapshot.NextAttemptUtc > DateTimeOffset.UtcNow) continue;
                    try { await ProcessAsync(id, stoppingToken); }
                    catch (IOException) { /* Another worker holds this receipt; next scan will reconcile it. */ }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogError(ex, "No fue posible revisar la recepción persistente de Copiers."); }
            try { await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }
    internal async Task ProcessAsync(string id, CancellationToken stoppingToken,
        Func<CopiersSubmission, CancellationToken, Task<CopiersMaintenanceV2FinalizeResultDto>>? processor = null)
    {
        await using var lease = await store.LockAsync(id, stoppingToken);
        var item = await store.ReadAsync(id, stoppingToken);
        if (item?.Payload is null || item.Result is not null || item.NeedsReview || item.NextAttemptUtc > DateTimeOffset.UtcNow) return;
        item.Attempts++;
        // Persist admission before doing any remote business write. A process exit
        // resumes this same immutable payload, never creates a replacement key.
        item.NextAttemptUtc = DateTimeOffset.UtcNow.AddMinutes(16);
        await store.WriteAsync(item, stoppingToken);
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            deadline.CancelAfter(TimeSpan.FromMinutes(5));
            item.Result = await (processor ?? FinalizeAsync)(item, deadline.Token);
            // Dataverse has independently verified files at this point. Retain the
            // receipt/fingerprint, not a second indefinite copy of photos/signature.
            item.Payload = null; item.Error = "";
        }
        catch (Exception ex)
        {
            item.NeedsReview = ex is CopiersMaintenanceV2ValidationException or UnauthorizedAccessException
                || item.Attempts >= 12;
            item.Error = item.NeedsReview
                ? ex is CopiersMaintenanceV2ValidationException validation ? validation.Message : "El envío quedó guardado y requiere revisión interna. No lo dupliques."
                : "El envío sigue guardado. Se reintentará automáticamente sin crear otro registro.";
            item.NextAttemptUtc = DateTimeOffset.UtcNow.AddMinutes(ex is CopiersMaintenanceV2ConcurrencyException ? 16 : Math.Min(16, item.Attempts * 2));
            logger.LogError(ex, "Recepción Copiers {ReceiptId}, intento {Attempt}, revisión {Review}.", id, item.Attempts, item.NeedsReview);
        }
        // Do not tie the durable transition to a disconnected HTTP request or an
        // already-cancelled work timeout. Interrupted writes keep the old receipt.
        using var commit = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await store.WriteAsync(item, commit.Token);
    }

    private async Task<CopiersMaintenanceV2FinalizeResultDto> FinalizeAsync(CopiersSubmission item, CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var services = scope.ServiceProvider;
        var accessor = services.GetRequiredService<IHttpContextAccessor>();
        var previous = accessor.HttpContext;
        var streams = new List<MemoryStream>();
        try
        {
            var payload = item.Payload!;
            accessor.HttpContext = new DefaultHttpContext { RequestServices = services,
                User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, payload.Actor.SystemUserId)], "CopiersDurableReceipt")) };
            var request = JsonSerializer.Deserialize<CopiersMaintenanceV2FinalizeMultipartRequestDto>(payload.RequestJson)!;
            request.DurableReceivedAtUtc = item.ReceivedAtUtc;
            foreach (var file in payload.Files)
            {
                var stream = new MemoryStream(file.Content, writable: false); streams.Add(stream);
                var formFile = new FormFile(stream, 0, stream.Length, file.Field, file.Name) { Headers = new HeaderDictionary(), ContentType = file.Type };
                if (file.Field == "Signature") request.Signature = formFile; else request.Attachments.Add(formFile);
            }
            if (CopiersEquipmentOperation.Read(request.MovementDetailsJson)?.Internal == true)
                return await services.GetRequiredService<CopiersEquipmentOperationsService>().CompleteInternalAsync(payload.Draft, request, payload.Actor, ct);
            var service = request.ActivityKind != "maintenance" ? services.GetRequiredService<CopiersActivityV2Runtime>().CreateService()
                : new CopiersMaintenanceV2Service(services.GetRequiredService<ICopiersMaintenanceV2DataverseRepository>(),
                    services.GetRequiredService<ICopiersMtoV2PdfBuilder>(), services.GetRequiredService<IOptions<CopiersMaintenanceV2Options>>(),
                    services.GetRequiredService<IOptions<CopiersMaintenanceV2DataverseOptions>>(), services.GetRequiredService<TimeProvider>(),
                    services.GetRequiredService<ILogger<CopiersMaintenanceV2Service>>(), services.GetRequiredService<CopiersMtoV2WorkerCounters>());
            var draft = await service.CreateOrGetDraftAsync(payload.Draft, payload.Actor, ct);
            request.RecordId = draft.RecordId; request.ExpectedVersion = draft.Version;
            return await service.FinalizeMultipartAsync(request, payload.Actor, ct);
        }
        finally { accessor.HttpContext = previous; foreach (var stream in streams) stream.Dispose(); }
    }
}
