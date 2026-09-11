using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CotizadorInterno.Web.Models.CopiersMtoV2;
using CotizadorInterno.Web.Services.CopiersMtoV2;
using Xunit;

namespace CotizadorInterno.Web.Tests;

public sealed class CopiersWorkerCounterTests
{
    private const string Equipment = "22222222-2222-4222-8222-222222222222";
    private const string Customer = "11111111-1111-4111-8111-111111111111";
    private static CopiersMtoV2CounterSaveCommand Command(long copies = 100) => new() {
        MaintenanceRecordId = "33333333-3333-4333-8333-333333333333", SubmissionKey = "copiers-worker-test-0001",
        EquipmentId = Equipment, ClientId = Customer, EquipmentSerial = "TEST", CopiesCounter = copies, ScansCounter = 20,
        ReadingAtUtc = DateTimeOffset.Parse("2026-09-10T21:00:01.123Z"), FinalizationFingerprint = new string('a', 64)
    };
    [Fact] public async Task WorkerCreatesOnceUsingPostAndReadsBackWithoutWritePrivilege()
    {
        var transport = new Transport(); var service = new CopiersMtoV2WorkerCounters(transport);
        var first = await service.SaveForMaintenanceAsync(Command());
        var second = await service.SaveForMaintenanceAsync(Command());
        Assert.Equal(first.RecordId, second.RecordId); Assert.True(second.ReusedExisting);
        Assert.Equal(1, transport.Posts); Assert.NotNull(transport.Row);
    }
    [Fact] public async Task ExistingChangedCounterIsNeverOverwritten()
    {
        var transport = new Transport(); var service = new CopiersMtoV2WorkerCounters(transport);
        await service.SaveForMaintenanceAsync(Command());
        await Assert.ThrowsAsync<CopiersMaintenanceV2ConcurrencyException>(() => service.SaveForMaintenanceAsync(Command(101)));
        Assert.Equal(1, transport.Posts);
    }
    [Fact] public async Task EquipmentReassignmentPreventsCounterWrite()
    {
        var transport = new Transport { Client = Guid.NewGuid().ToString() };
        await Assert.ThrowsAsync<CopiersMaintenanceV2ValidationException>(() => new CopiersMtoV2WorkerCounters(transport).SaveForMaintenanceAsync(Command()));
        Assert.Equal(0, transport.Posts);
    }
    [Fact] public async Task MissingHistoryInSignedFormPreventsCounterWrite()
    {
        var transport = new Transport { HasPrevious = true };
        await Assert.ThrowsAsync<CopiersMaintenanceV2ValidationException>(() => new CopiersMtoV2WorkerCounters(transport).SaveForMaintenanceAsync(Command()));
        Assert.Equal(0, transport.Posts);
    }
    [Fact] public async Task ConcurrentCreateIsReconciledByExactReadBack()
    {
        var transport = new Transport { ConcurrentCreate = true };
        var result = await new CopiersMtoV2WorkerCounters(transport).SaveForMaintenanceAsync(Command());
        Assert.True(result.ReusedExisting); Assert.Equal(1, transport.Posts);
    }
    [Fact] public async Task MissingPersistenceNeverReportsSuccess()
    {
        var transport = new Transport { DropCreate = true };
        await Assert.ThrowsAsync<CopiersMaintenanceV2PersistenceException>(() => new CopiersMtoV2WorkerCounters(transport).SaveForMaintenanceAsync(Command()));
    }
    private sealed class Transport : ICopiersMtoV2ApplicationDataverseClient
    {
        public string Client { get; set; } = Customer;
        public int Posts { get; private set; }
        public bool HasPrevious { get; init; }
        public bool ConcurrentCreate { get; init; }
        public bool DropCreate { get; init; }
        public Dictionary<string, object?>? Row { get; private set; }
        public async Task<HttpResponseMessage> SendAsync(string path, HttpMethod method, HttpContent? content, Action<HttpRequestMessage>? customize, CancellationToken ct = default)
        {
            if (method == HttpMethod.Post)
            {
                Assert.Equal("/api/data/v9.2/cr07a_contadoreses", path); Posts++;
                var raw = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(await content!.ReadAsStringAsync(ct))!;
                Assert.True(raw.ContainsKey("cr07a_contadoresid")); Assert.False(raw.ContainsKey("ownerid@odata.bind"));
                if (!DropCreate) { Row = raw.Where(x => !x.Key.Contains('@')).ToDictionary(x => x.Key, x => (object?)x.Value); Row["_cr07a_maquina_value"] = Equipment; }
                return Response(new { }, ConcurrentCreate ? HttpStatusCode.BadRequest : HttpStatusCode.NoContent);
            }
            Assert.Equal(HttpMethod.Get, method);
            if (path.StartsWith("/api/data/v9.2/cr07a_equipos(")) return Response(new Dictionary<string, object?> { ["cr07a_nombredelequipo"] = "TEST", ["_cr07a_cliente_value"] = Client });
            if (path.StartsWith("/api/data/v9.2/cr07a_contadoreses?")) return Response(new { value = HasPrevious ? new[] { new { cr07a_contadoresid = Guid.NewGuid() } } : [] });
            if (path.StartsWith("/api/data/v9.2/cr07a_contadoreses(")) return Row is null ? Response(new { }, HttpStatusCode.NotFound) : Response(Row);
            throw new InvalidOperationException("Unexpected request: " + path);
        }
        private static HttpResponseMessage Response(object body, HttpStatusCode status = HttpStatusCode.OK) => new(status) { Content = JsonContent.Create(body) };
    }
}
