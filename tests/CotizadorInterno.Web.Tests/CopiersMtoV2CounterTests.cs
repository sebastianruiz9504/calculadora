using System.Net;
using System.Reflection;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using CotizadorInterno.Web.Models.CopiersMtoV2;
using CotizadorInterno.Web.Services;
using CotizadorInterno.Web.Services.CopiersMtoV2;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Identity.Abstractions;
using Xunit;

namespace CotizadorInterno.Web.Tests;

public sealed class CopiersMtoV2CounterTests
{
    private const string EquipmentId = "58e07029-6d84-425c-aee0-17b5d8f14a32";
    private const string ClientId = "81522aa1-96b9-45e8-937a-b5f0d920dc46";
    private const string MaintenanceId = "194c5a47-aec6-48bd-a1be-7a3f8eb141b4";
    private const string PreviousId = "142ca1f2-ae40-4cfa-9f67-17ed4db85c37";
    private const string Fingerprint = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    [Fact]
    public async Task LatestReturnsNullReadingsWhenThereIsNoHistory()
    {
        using var fixture = new Fixture();
        var result = await fixture.Service.GetLatestAsync(ClientId, EquipmentId);
        Assert.Equal(EquipmentId, result.EquipmentId);
        Assert.Equal("", result.RecordId);
        Assert.Equal("", result.DateValue);
        Assert.Null(result.RecordedAtUtc);
        Assert.Null(result.CopiesCounter);
        Assert.Null(result.ScansCounter);
        Assert.All(fixture.Transport.Requests, request => Assert.Equal("GET", request.Method));
    }

    [Fact]
    public async Task LatestUsesDeterministicOrderAndBogotaDateWithDelegatedIdentity()
    {
        using var fixture = new Fixture();
        fixture.Transport.Records[PreviousId] = Counter(PreviousId, 500, null, "2026-09-10T02:30:00Z");
        var result = await fixture.Service.GetLatestAsync(ClientId, EquipmentId);
        Assert.Equal(500L, result.CopiesCounter);
        Assert.Null(result.ScansCounter);
        Assert.Equal("2026-09-09", result.DateValue);
        Assert.Equal("09/09/2026 21:30", result.DateDisplay);
        Assert.Equal(DateTimeOffset.Parse("2026-09-10T02:30:00Z"), result.RecordedAtUtc);
        var query = Assert.Single(fixture.Transport.Requests, x => x.Path.Contains("cr07a_contadoreses?", StringComparison.Ordinal));
        Assert.Contains("$orderby=cr07a_fechadetomadecontador desc,createdon desc,cr07a_contadoresid desc", Uri.UnescapeDataString(query.Path));
        Assert.Contains($"_cr07a_maquina_value eq {EquipmentId}", Uri.UnescapeDataString(query.Path));
        Assert.EndsWith("&$top=1", query.Path);
    }

    [Fact]
    public async Task LatestRejectsEquipmentFromAnotherClientBeforeReadingCounters()
    {
        using var fixture = new Fixture();
        await Assert.ThrowsAsync<CopiersMaintenanceV2ValidationException>(() => fixture.Service.GetLatestAsync(Guid.NewGuid().ToString(), EquipmentId));
        Assert.DoesNotContain(fixture.Transport.Requests, x => x.Path.Contains("cr07a_contadores", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SaveWritesRealUtcTimestampUsingExistingMetadataAndReadsBack()
    {
        using var fixture = new Fixture();
        var command = Command();
        var result = await fixture.Service.SaveForMaintenanceAsync(command);
        Assert.False(result.ReusedExisting);
        Assert.Equal(DataverseService.BuildCopiersMtoV2CounterRecordId(MaintenanceId), result.RecordId);
        var patch = Assert.Single(fixture.Transport.Requests, x => x.Method == "PATCH");
        Assert.Equal("*", patch.IfNoneMatch);
        Assert.Equal($"/api/data/v9.2/cr07a_contadoreses({result.RecordId})", patch.Path);
        var payload = Assert.IsType<JsonElement>(patch.Payload);
        Assert.Equal($"/cr07a_equipos({EquipmentId})", payload.GetProperty("cr07a_Maquina@odata.bind").GetString());
        Assert.StartsWith("MTO V2 194c5a47aec648bda1be7a3f8eb141b4 ", payload.GetProperty("cr07a_equipo").GetString());
        Assert.EndsWith(" " + Fingerprint, payload.GetProperty("cr07a_equipo").GetString());
        Assert.False(payload.TryGetProperty("cr07a_name", out _));
        Assert.Equal("2026-09-10T16:25:37Z", payload.GetProperty("cr07a_fechadetomadecontador").GetString());
        Assert.Equal(550L, payload.GetProperty("cr07a_contador").GetInt64());
        Assert.Equal(80L, payload.GetProperty("cr07a_contadorescaner").GetInt64());
        Assert.Equal("GET", fixture.Transport.Requests[^1].Method);
    }

    [Fact]
    public async Task RetryReusesDeterministicRowWithoutAnotherWrite()
    {
        using var fixture = new Fixture();
        var first = await fixture.Service.SaveForMaintenanceAsync(Command());
        var second = await fixture.Service.SaveForMaintenanceAsync(Command());
        Assert.Equal(first.RecordId, second.RecordId);
        Assert.True(second.ReusedExisting);
        Assert.Single(fixture.Transport.Requests, x => x.Method == "PATCH");
    }

    [Fact]
    public async Task ChangedRetryFailsWithoutOverwritingPersistedCounter()
    {
        using var fixture = new Fixture();
        await fixture.Service.SaveForMaintenanceAsync(Command());
        await Assert.ThrowsAsync<CopiersMaintenanceV2ConcurrencyException>(() => fixture.Service.SaveForMaintenanceAsync(Command(copies: 551)));
        Assert.Single(fixture.Transport.Requests, x => x.Method == "PATCH");
    }

    [Fact]
    public async Task DifferentSignedFingerprintCannotReuseCounterProof()
    {
        using var fixture = new Fixture();
        await fixture.Service.SaveForMaintenanceAsync(Command());
        var changed = Command(fingerprint: new string('a', 64));
        await Assert.ThrowsAsync<CopiersMaintenanceV2ConcurrencyException>(() => fixture.Service.ValidateForMaintenanceAsync(changed));
        await Assert.ThrowsAsync<CopiersMaintenanceV2ConcurrencyException>(() => fixture.Service.SaveForMaintenanceAsync(changed));
        Assert.Single(fixture.Transport.Requests, x => x.Method == "PATCH");
    }

    [Fact]
    public async Task ValidationReturnsTrueOnlyForPersistedExactSnapshot()
    {
        using var fixture = new Fixture();
        await fixture.Service.SaveForMaintenanceAsync(Command());
        Assert.True(await fixture.Service.ValidateForMaintenanceAsync(Command()));
        Assert.True(await fixture.Service.ValidateForMaintenanceAsync(Command(fingerprint: Fingerprint.ToUpperInvariant())));
        Assert.Single(fixture.Transport.Requests, x => x.Method == "PATCH");
    }

    [Fact]
    public async Task ValidationWithoutOwnPersistedCounterReturnsFalseAndDoesNotWrite()
    {
        using var fixture = new Fixture();
        Assert.False(await fixture.Service.ValidateForMaintenanceAsync(Command()));
        Assert.DoesNotContain(fixture.Transport.Requests, x => x.Method != "GET");
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("gggggggggggggggggggggggggggggggggggggggggggggggggggggggggggggggg")]
    public async Task InvalidSignedFingerprintIsRejectedBeforeDataverseReadsOrWrites(string fingerprint)
    {
        using var fixture = new Fixture();
        await Assert.ThrowsAsync<CopiersMaintenanceV2ValidationException>(() => fixture.Service.ValidateForMaintenanceAsync(Command(fingerprint: fingerprint)));
        await Assert.ThrowsAsync<CopiersMaintenanceV2ValidationException>(() => fixture.Service.SaveForMaintenanceAsync(Command(fingerprint: fingerprint)));
        Assert.Empty(fixture.Transport.Requests);
    }

    [Fact]
    public async Task ConcurrentIdenticalCreateReadsBackAfterPreconditionFailure()
    {
        using var fixture = new Fixture();
        fixture.Transport.SimulateConcurrentCreate = true;
        var result = await fixture.Service.SaveForMaintenanceAsync(Command());
        Assert.True(result.ReusedExisting);
        Assert.Single(fixture.Transport.Requests, x => x.Method == "PATCH");
        Assert.Equal("GET", fixture.Transport.Requests[^1].Method);
    }

    [Fact]
    public async Task FailedReadbackDoesNotReportSuccess()
    {
        using var fixture = new Fixture();
        fixture.Transport.DropWrittenRecord = true;
        await Assert.ThrowsAsync<CopiersMaintenanceV2PersistenceException>(() => fixture.Service.SaveForMaintenanceAsync(Command()));
        Assert.Single(fixture.Transport.Requests, x => x.Method == "PATCH");
    }

    [Fact]
    public async Task ValidateReadsPinnedHistoryWithoutReplacingItWithLaterReadingOrWriting()
    {
        using var fixture = new Fixture();
        fixture.Transport.Records[PreviousId] = Counter(PreviousId, 500, 70, "2026-09-08T16:00:00Z");
        var later = Guid.NewGuid().ToString();
        fixture.Transport.Records[later] = Counter(later, 900, 90, "2026-09-10T17:00:00Z");
        await fixture.Service.ValidateForMaintenanceAsync(Command(previous: true));
        Assert.DoesNotContain(fixture.Transport.Requests, x => x.Method != "GET");
        Assert.DoesNotContain(fixture.Transport.Requests, x => x.Path.Contains("cr07a_contadoreses?", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ChangedPreviousCounterIsRejectedBeforeWriting()
    {
        using var fixture = new Fixture();
        fixture.Transport.Records[PreviousId] = Counter(PreviousId, 499, 70, "2026-09-08T16:00:00Z");
        await Assert.ThrowsAsync<CopiersMaintenanceV2ValidationException>(() => fixture.Service.SaveForMaintenanceAsync(Command(previous: true)));
        Assert.DoesNotContain(fixture.Transport.Requests, x => x.Method == "PATCH");
    }

    [Fact]
    public async Task PreviousCounterFromDifferentEquipmentIsRejected()
    {
        using var fixture = new Fixture();
        fixture.Transport.Records[PreviousId] = Counter(PreviousId, 500, 70, "2026-09-08T16:00:00Z", equipment: Guid.NewGuid().ToString());
        await Assert.ThrowsAsync<CopiersMaintenanceV2ValidationException>(() => fixture.Service.ValidateForMaintenanceAsync(Command(previous: true)));
    }

    [Fact]
    public async Task CurrentCounterCannotDecreaseFromPinnedHistory()
    {
        using var fixture = new Fixture();
        fixture.Transport.Records[PreviousId] = Counter(PreviousId, 500, 70, "2026-09-08T16:00:00Z");
        await Assert.ThrowsAsync<CopiersMaintenanceV2ValidationException>(() => fixture.Service.ValidateForMaintenanceAsync(Command(copies: 499, previous: true)));
    }

    [Fact]
    public async Task FalseNoHistoryClaimIsRejectedWithoutWriting()
    {
        using var fixture = new Fixture();
        fixture.Transport.Records[PreviousId] = Counter(PreviousId, 500, 70, "2026-09-08T16:00:00Z");
        await Assert.ThrowsAsync<CopiersMaintenanceV2ValidationException>(() => fixture.Service.ValidateForMaintenanceAsync(Command()));
        var query = Assert.Single(fixture.Transport.Requests, x => x.Path.Contains("cr07a_contadoreses?", StringComparison.Ordinal));
        var decoded = Uri.UnescapeDataString(query.Path);
        Assert.Contains("$select=cr07a_contadoresid&", decoded);
        Assert.Contains("createdon le 2026-09-10T16:25:37Z", decoded);
        Assert.Contains($"cr07a_contadoresid ne {DataverseService.BuildCopiersMtoV2CounterRecordId(MaintenanceId)}", decoded);
        Assert.DoesNotContain(fixture.Transport.Requests, x => x.Method != "GET");
    }

    [Fact]
    public async Task NoHistoryAtVisitEndDoesNotConflictWithAReadingUploadedLater()
    {
        using var fixture = new Fixture();
        fixture.Transport.Records[PreviousId] = Counter(PreviousId, 500, 70, "2026-09-10T17:00:00Z");
        await fixture.Service.ValidateForMaintenanceAsync(Command());
        Assert.DoesNotContain(fixture.Transport.Requests, x => x.Method != "GET");
    }

    [Fact]
    public async Task UnauthenticatedCounterReadDoesNotCallDataverse()
    {
        using var fixture = new Fixture(authenticated: false);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => fixture.Service.GetLatestAsync(ClientId, EquipmentId));
        Assert.Empty(fixture.Transport.Requests);
    }

    [Theory]
    [InlineData(null, 0L)]
    [InlineData(0L, null)]
    [InlineData(-1L, 0L)]
    [InlineData(0L, -1L)]
    [InlineData(2147483648L, 0L)]
    [InlineData(0L, 2147483648L)]
    public async Task MissingNegativeOrOutOfRangeReadingsFailBeforeAnyRequest(long? copies, long? scans)
    {
        using var fixture = new Fixture();
        await Assert.ThrowsAsync<CopiersMaintenanceV2ValidationException>(() => fixture.Service.SaveForMaintenanceAsync(Command(copies, scans)));
        Assert.Empty(fixture.Transport.Requests);
    }

    [Fact]
    public async Task RealZeroReadingsAreAccepted()
    {
        using var fixture = new Fixture();
        await fixture.Service.SaveForMaintenanceAsync(Command(0, 0));
        var patch = Assert.Single(fixture.Transport.Requests, x => x.Method == "PATCH");
        var payload = Assert.IsType<JsonElement>(patch.Payload);
        Assert.Equal(0, payload.GetProperty("cr07a_contador").GetInt32());
        Assert.Equal(0, payload.GetProperty("cr07a_contadorescaner").GetInt32());
    }

    [Fact]
    public void DeterministicIdIsStableAcrossGuidFormattingAndChangesPerMaintenance()
    {
        Assert.Equal(DataverseService.BuildCopiersMtoV2CounterRecordId(MaintenanceId),
            DataverseService.BuildCopiersMtoV2CounterRecordId(Guid.Parse(MaintenanceId).ToString("B").ToUpperInvariant()));
        Assert.NotEqual(DataverseService.BuildCopiersMtoV2CounterRecordId(MaintenanceId),
            DataverseService.BuildCopiersMtoV2CounterRecordId(PreviousId));
    }

    private static CopiersMtoV2CounterSaveCommand Command(long? copies = 550, long? scans = 80, bool previous = false, string fingerprint = Fingerprint) => new()
    {
        MaintenanceRecordId = MaintenanceId, SubmissionKey = "mto-v2-counter-retry-12345678", ClientId = ClientId,
        FinalizationFingerprint = fingerprint,
        EquipmentId = EquipmentId, EquipmentSerial = "SERIAL-001", ReadingAtUtc = DateTimeOffset.Parse("2026-09-10T11:25:37.345-05:00"),
        CopiesCounter = copies, ScansCounter = scans, PreviousCounterRecordId = previous ? PreviousId : "",
        PreviousCopiesCounter = previous ? 500 : null, PreviousScansCounter = previous ? 70 : null,
        PreviousDateValue = previous ? "2026-09-08" : ""
    };

    private static Dictionary<string, object?> Counter(string id, long? copies, long? scans, string at, string? equipment = null) => new()
    {
        ["cr07a_contadoresid"] = id, ["cr07a_equipo"] = "Contador histórico",
        ["_cr07a_maquina_value"] = equipment ?? EquipmentId, ["cr07a_fechadetomadecontador"] = at,
        ["cr07a_contador"] = copies, ["cr07a_contadorescaner"] = scans, ["createdon"] = at
    };

    private sealed class Fixture : IDisposable
    {
        private readonly MemoryCache _cache = new(new MemoryCacheOptions());
        public DataverseService Service { get; }
        public CounterTransport Transport { get; }

        public Fixture(bool authenticated = true)
        {
            var downstream = DispatchProxy.Create<IDownstreamApi, CounterTransport>();
            Transport = (CounterTransport)downstream;
            var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, "technician@example.test")], authenticated ? "test" : null));
            Transport.ExpectedUser = user;
            Service = new DataverseService(downstream,
                new HttpContextAccessor { HttpContext = new DefaultHttpContext { User = user } },
                null!, null!, null!, _cache, null!, new ConfigurationBuilder().Build(), Options.Create(new RhOptions()),
                NullLogger<DataverseService>.Instance);
        }

        public void Dispose() => _cache.Dispose();
    }

    public sealed record Request(string Method, string Path, string? IfNoneMatch, JsonElement? Payload);

    public class CounterTransport : DispatchProxy
    {
        public ClaimsPrincipal ExpectedUser { get; set; } = null!;
        public Dictionary<string, Dictionary<string, object?>> Records { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<Request> Requests { get; } = [];
        public bool SimulateConcurrentCreate { get; set; }
        public bool DropWrittenRecord { get; set; }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            Assert.NotNull(targetMethod);
            Assert.Equal(nameof(IDownstreamApi.CallApiForUserAsync), targetMethod.Name);
            Assert.NotNull(args);
            Assert.Equal("Dataverse", args[0]);
            Assert.Contains(args, item => ReferenceEquals(item, ExpectedUser));
            var options = new DownstreamApiOptions();
            Assert.IsType<Action<DownstreamApiOptions>>(args[1])(options);
            using var request = new HttpRequestMessage();
            options.CustomizeHttpRequestMessage?.Invoke(request);
            JsonElement? payload = null;
            if (args.OfType<HttpContent>().FirstOrDefault() is { } content)
            {
                using var parsed = JsonDocument.Parse(content.ReadAsStringAsync().GetAwaiter().GetResult());
                payload = parsed.RootElement.Clone();
            }
            var path = options.RelativePath ?? "";
            Requests.Add(new(options.HttpMethod!, path, request.Headers.TryGetValues("If-None-Match", out var match) ? match.Single() : null, payload));
            object body;
            if (path.Contains("EntityDefinitions(LogicalName='cr07a_equipo')", StringComparison.Ordinal))
                body = new { LogicalName = "cr07a_equipo", EntitySetName = "cr07a_equipos", PrimaryIdAttribute = "cr07a_equipoid", PrimaryNameAttribute = "cr07a_nombredelequipo" };
            else if (path.Contains("EntityDefinitions(LogicalName='cr07a_contadores')", StringComparison.Ordinal))
                body = new { LogicalName = "cr07a_contadores", EntitySetName = "cr07a_contadoreses", PrimaryIdAttribute = "cr07a_contadoresid", PrimaryNameAttribute = "cr07a_equipo",
                    ManyToOneRelationships = new[] { new { ReferencingAttribute = "cr07a_maquina", ReferencingEntityNavigationPropertyName = "cr07a_Maquina" } } };
            else if (path.StartsWith($"/api/data/v9.2/cr07a_equipos({EquipmentId})?", StringComparison.Ordinal))
                body = new Dictionary<string, object?> { ["cr07a_equipoid"] = EquipmentId, ["cr07a_nombredelequipo"] = "SERIAL-001", ["_cr07a_cliente_value"] = ClientId };
            else if (path.StartsWith("/api/data/v9.2/cr07a_contadoreses?", StringComparison.Ordinal))
            {
                var decoded = Uri.UnescapeDataString(path);
                var records = Records.Values.Where(x => Text(x["_cr07a_maquina_value"]) == EquipmentId);
                if (decoded.Contains("createdon le ", StringComparison.Ordinal))
                {
                    var cutoff = decoded.Split("createdon le ")[1].Split(' ')[0];
                    var excluded = decoded.Split("cr07a_contadoresid ne ")[1].Split('&')[0];
                    records = records.Where(x => DateTimeOffset.Parse(Text(x["createdon"])) <= DateTimeOffset.Parse(cutoff)
                        && Text(x["cr07a_contadoresid"]) != excluded);
                }
                body = new { value = records.OrderByDescending(x => Text(x["cr07a_fechadetomadecontador"])).Take(1).ToArray() };
            }
            else if (path.StartsWith("/api/data/v9.2/cr07a_contadoreses(", StringComparison.Ordinal))
            {
                var id = path[(path.IndexOf('(') + 1)..path.IndexOf(')')];
                if (options.HttpMethod == "GET")
                    return Records.TryGetValue(id, out var record) ? Response(record) : Response(new { }, HttpStatusCode.NotFound);
                Assert.Equal("PATCH", options.HttpMethod);
                Assert.NotNull(payload);
                if (Records.ContainsKey(id)) return Response(new { }, HttpStatusCode.PreconditionFailed);
                if (!DropWrittenRecord)
                {
                    var saved = JsonSerializer.Deserialize<Dictionary<string, object?>>(payload.Value.GetRawText())!;
                    saved.Remove("cr07a_Maquina@odata.bind");
                    saved["cr07a_contadoresid"] = id;
                    saved["_cr07a_maquina_value"] = EquipmentId;
                    saved["createdon"] = "2026-09-10T16:25:40Z";
                    Records[id] = saved;
                }
                return Response(new { }, SimulateConcurrentCreate ? HttpStatusCode.PreconditionFailed : HttpStatusCode.NoContent);
            }
            else throw new NotSupportedException($"Unexpected request: {path}");
            return Response(body);
        }

        private static Task<HttpResponseMessage> Response(object body, HttpStatusCode status = HttpStatusCode.OK) => Task.FromResult(
            new HttpResponseMessage(status) { Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json") });

        private static string Text(object? value) => value is JsonElement element ? element.ToString() : value?.ToString() ?? "";
    }
}
