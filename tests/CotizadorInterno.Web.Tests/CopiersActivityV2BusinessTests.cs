using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using CotizadorInterno.Web.Models.CopiersMtoV2;
using CotizadorInterno.Web.Services.CopiersMtoV2;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace CotizadorInterno.Web.Tests;

public sealed class CopiersActivityV2BusinessTests
{
    private const string ReportId = "041882f0-9cc7-48ec-b3bb-cbffadf657cc";
    private const string EquipmentId = "a23a3e76-c73f-46f7-bf76-73bff9d7a4b4";
    private const string OriginId = "124019b9-0916-4269-8ad6-d1f197924afb";
    private const string ClientId = "031b8aa1-b719-46e3-b7ef-37f42e8601fc";
    private const string SupplyId = "7516b4ba-c9b7-4342-b834-8e9d0a398f90";
    private const string TechnicianId = "a67c2aec-e090-4d1b-9c9f-38a35206153c";
    private const string Fingerprint = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
    private const string Organization = "https://business-tests.example.test";

    [Fact]
    public async Task MovementCreatesRealRowAndAssignmentInOneConditionalChangeSet()
    {
        var fixture = new Fixture("movement");
        var command = Command("movement");
        Assert.False(await fixture.Service.ValidateAsync(command));
        Assert.All(fixture.Transport.Requests, request => Assert.Equal(HttpMethod.Get, request.Method));
        var result = await fixture.Service.CommitAsync(command, Pdf());
        Assert.False(result.ReusedExisting);
        Assert.Equal("ACT-001234", result.ServiceReference);
        Assert.Equal(CopiersActivityV2BusinessService.BuildBusinessRecordId(ReportId, "movement"), result.BusinessRecordId);
        Assert.Equal(ClientId, fixture.Transport.AssignedClient);
        Assert.Equal(1, fixture.Transport.AssetMutations);
        var batch = Assert.Single(fixture.Transport.Requests, request => request.Path.EndsWith("/$batch", StringComparison.Ordinal));
        Assert.Contains("multipart/mixed", batch.ContentType);
        Assert.Contains("If-None-Match: *", batch.Body);
        Assert.Contains("If-Match: W/\"10\"", batch.Body);
        Assert.Contains($"PATCH {Organization}/api/data/v9.2/cr07a_movimientosequiposes({result.BusinessRecordId}) HTTP/1.1", batch.Body);
        Assert.Contains($"PATCH {Organization}/api/data/v9.2/cr07a_equipos({EquipmentId}) HTTP/1.1", batch.Body);
        var row = Assert.IsType<JsonObject>(fixture.Transport.Business);
        Assert.Equal(TechnicianId, row["_ownerid_value"]!.GetValue<string>());
        Assert.Equal(OriginId, row["dtc_originclientkey"]!.GetValue<string>());
        Assert.Equal("Cliente origen", row["dtc_originclientname"]!.GetValue<string>());
        Assert.Equal("2026-09-10T23:30:42Z", row["cr07a_fecha"]!.GetValue<string>());
        Assert.Equal(Fingerprint, row["dtc_signedfingerprint"]!.GetValue<string>());
        Assert.Equal(ReportId, row["dtc_signedreportkey"]!.GetValue<string>());
        Assert.Equal(1, fixture.Transport.FileUploads);
        Assert.EndsWith("/cr07a_actadeentrega/$value", fixture.Transport.Requests[^1].Path);
        Assert.DoesNotContain(fixture.Transport.Requests, request => request.Path.Contains("mantenimiento", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task TonerCreatesDeliveryWithRealEquipmentAndDecrementsStockExactlyOnce()
    {
        var fixture = new Fixture("toner");
        var result = await fixture.Service.CommitAsync(Command("toner"), Pdf());
        Assert.Equal(7, fixture.Transport.Stock);
        Assert.Equal(1, fixture.Transport.AssetMutations);
        Assert.Contains("cr07a_entregas(", fixture.Transport.Requests.Single(request => request.Path.EndsWith("/$batch")).Body);
        Assert.Equal(ClientId, fixture.Transport.Business!["_cr07a_iddecliente_value"]!.GetValue<string>());
        Assert.Equal(EquipmentId, fixture.Transport.Business["_cr07a_iddeequipo_value"]!.GetValue<string>());
        Assert.Equal(SupplyId, fixture.Transport.Business["_cr07a_iddesuministro_value"]!.GetValue<string>());
        Assert.Equal(3, fixture.Transport.Business["cr07a_cantidadentregada"]!.GetValue<int>());
        Assert.Equal(645250000, fixture.Transport.Business["cr07a_estadodeentrega"]!.GetValue<int>());
        Assert.EndsWith("/cr07a_comprobantedeentrega/$value", fixture.Transport.Requests[^1].Path);
        Assert.False(result.ReusedExisting);
    }

    [Fact]
    public async Task ExactReplayDoesNotCareAboutLaterStockAssignmentOrNames()
    {
        var fixture = new Fixture("toner");
        var command = Command("toner");
        await fixture.Service.CommitAsync(command, Pdf());
        fixture.Transport.Stock = 2;
        fixture.Transport.AssignedClient = OriginId;
        fixture.Transport.SupplyName = "Nombre modificado posteriormente";
        fixture.Transport.Requests.Clear();
        Assert.True(await fixture.Service.ValidateAsync(command));
        var replay = await fixture.Service.CommitAsync(command, Pdf());
        Assert.True(replay.ReusedExisting);
        Assert.Equal(2, fixture.Transport.Stock);
        Assert.Equal(1, fixture.Transport.AssetMutations);
        Assert.Equal(1, fixture.Transport.FileUploads);
        Assert.All(fixture.Transport.Requests, request => Assert.Equal(HttpMethod.Get, request.Method));
        Assert.DoesNotContain(fixture.Transport.Requests, request => request.Path.Contains("cr07a_equipos(") || request.Path.Contains("cr07a_suministros("));
    }

    [Fact]
    public async Task MovementReplaySurvivesASecondLegitimateReassignment()
    {
        var fixture = new Fixture("movement");
        var command = Command("movement");
        await fixture.Service.CommitAsync(command, Pdf());
        fixture.Transport.AssignedClient = Guid.NewGuid().ToString();
        Assert.True(await fixture.Service.ValidateAsync(command));
        Assert.True((await fixture.Service.CommitAsync(command, Pdf())).ReusedExisting);
        Assert.Equal(1, fixture.Transport.AssetMutations);
    }

    [Fact]
    public async Task ChangedFingerprintOrOwnerCannotReuseBusinessProof()
    {
        var fixture = new Fixture("toner");
        var command = Command("toner");
        await fixture.Service.CommitAsync(command, Pdf());
        await Assert.ThrowsAsync<CopiersMaintenanceV2ConcurrencyException>(() => fixture.Service.ValidateAsync(command with { FinalizationFingerprint = new string('a', 64) }));
        await Assert.ThrowsAsync<CopiersMaintenanceV2ConcurrencyException>(() => fixture.Service.ValidateAsync(command with { TechnicianSystemUserId = Guid.NewGuid().ToString() }));
        Assert.Equal(1, fixture.Transport.AssetMutations);
    }

    [Fact]
    public async Task TamperedPersistedQuantityFailsReadBackWithoutAnotherDecrement()
    {
        var fixture = new Fixture("toner");
        await fixture.Service.CommitAsync(Command("toner"), Pdf());
        fixture.Transport.Business!["cr07a_cantidadentregada"] = 4;
        await Assert.ThrowsAsync<CopiersMaintenanceV2ConcurrencyException>(() => fixture.Service.CommitAsync(Command("toner"), Pdf()));
        Assert.Equal(1, fixture.Transport.AssetMutations);
    }

    [Fact]
    public async Task StaleAssetVersionRollsBackRecordAndStockTogether()
    {
        var fixture = new Fixture("toner");
        fixture.Transport.RaceAssetBeforeBatch = true;
        await Assert.ThrowsAsync<CopiersMaintenanceV2ConcurrencyException>(() => fixture.Service.CommitAsync(Command("toner"), Pdf()));
        Assert.Null(fixture.Transport.Business);
        Assert.Equal(9, fixture.Transport.Stock); // The other writer, not this delivery.
        Assert.Equal(0, fixture.Transport.AssetMutations);
        Assert.Equal(0, fixture.Transport.FileUploads);
    }

    [Fact]
    public async Task ConcurrentReassignmentDoesNotCreateAMovementFromAStaleOrigin()
    {
        var fixture = new Fixture("movement");
        fixture.Transport.RaceAssetBeforeBatch = true;
        await Assert.ThrowsAsync<CopiersMaintenanceV2ConcurrencyException>(() => fixture.Service.CommitAsync(Command("movement"), Pdf()));
        Assert.Null(fixture.Transport.Business);
        Assert.Equal(ClientId, fixture.Transport.AssignedClient); // The competing assignment wins.
        Assert.Equal(0, fixture.Transport.AssetMutations);
        Assert.Equal(0, fixture.Transport.FileUploads);
    }

    [Fact]
    public async Task ConcurrentIdenticalCommitUsesDurableRowInsteadOfSubtractingAgain()
    {
        var fixture = new Fixture("toner");
        fixture.Transport.ConcurrentIdenticalCommit = true;
        var result = await fixture.Service.CommitAsync(Command("toner"), Pdf());
        Assert.True(result.ReusedExisting);
        Assert.Equal(7, fixture.Transport.Stock);
        Assert.Equal(1, fixture.Transport.AssetMutations);
    }

    [Fact]
    public async Task LostBatchResponseIsReconciledByExactAtomicRecord()
    {
        var fixture = new Fixture("toner");
        fixture.Transport.LoseBatchResponse = true;
        Assert.True((await fixture.Service.CommitAsync(Command("toner"), Pdf())).ReusedExisting);
        Assert.Equal(1, fixture.Transport.AssetMutations);
        Assert.Equal(7, fixture.Transport.Stock);
    }

    [Fact]
    public async Task MissingInnerBatchStatusesNeverReportsSuccessfulPersistence()
    {
        var fixture = new Fixture("toner");
        fixture.Transport.MalformedBatchResponse = true;
        await Assert.ThrowsAsync<CopiersMaintenanceV2PersistenceException>(() => fixture.Service.CommitAsync(Command("toner"), Pdf()));
        Assert.Null(fixture.Transport.Business);
        Assert.Equal(10, fixture.Transport.Stock);
    }

    [Fact]
    public async Task FailedPdfUploadCanRetryWithoutRepeatingBusinessMutation()
    {
        var fixture = new Fixture("toner");
        fixture.Transport.FailNextFileUpload = true;
        await Assert.ThrowsAsync<CopiersMaintenanceV2PersistenceException>(() => fixture.Service.CommitAsync(Command("toner"), Pdf()));
        Assert.NotNull(fixture.Transport.Business);
        Assert.Null(fixture.Transport.FileBytes);
        Assert.True(await fixture.Service.ValidateAsync(Command("toner")));
        Assert.True((await fixture.Service.CommitAsync(Command("toner"), Pdf())).ReusedExisting);
        Assert.Equal(1, fixture.Transport.AssetMutations);
        Assert.Equal(7, fixture.Transport.Stock);
        Assert.Equal(Pdf().Content, fixture.Transport.FileBytes);
    }

    [Fact]
    public async Task ExistingDifferentPdfIsNeverOverwritten()
    {
        var fixture = new Fixture("toner");
        await fixture.Service.CommitAsync(Command("toner"), Pdf());
        fixture.Transport.FileBytes = "%PDF-different signed bytes"u8.ToArray();
        await Assert.ThrowsAsync<CopiersMaintenanceV2ConcurrencyException>(() => fixture.Service.CommitAsync(Command("toner"), Pdf()));
        Assert.Equal(1, fixture.Transport.FileUploads);
        Assert.Equal(1, fixture.Transport.AssetMutations);
    }

    [Fact]
    public async Task InvalidPdfFailsBeforeAnyBusinessReadOrMutation()
    {
        var fixture = new Fixture("toner");
        var pdf = Pdf();
        pdf.Sha256 = new string('0', 64);
        await Assert.ThrowsAsync<CopiersMaintenanceV2ValidationException>(() => fixture.Service.CommitAsync(Command("toner"), pdf));
        Assert.Empty(fixture.Transport.Requests);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task NonPositiveQuantityFailsBeforeDataverse(int quantity)
    {
        var fixture = new Fixture("toner");
        await Assert.ThrowsAsync<CopiersMaintenanceV2ValidationException>(() => fixture.Service.ValidateAsync(Command("toner") with { SupplyQuantity = quantity }));
        Assert.Empty(fixture.Transport.Requests);
    }

    [Fact]
    public void FractionalTransportQuantityCannotBindToCommandInteger()
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<CopiersActivityV2BusinessCommand>("{\"SupplyQuantity\":1.5}"));
    }

    [Fact]
    public async Task SupplyCategoryMustBeTonerRegardlessOfItsName()
    {
        var fixture = new Fixture("toner");
        fixture.Transport.Category = 645250003;
        await Assert.ThrowsAsync<CopiersMaintenanceV2ValidationException>(() => fixture.Service.ValidateAsync(Command("toner")));
        Assert.Equal(0, fixture.Transport.AssetMutations);
    }

    [Fact]
    public async Task InsufficientOrChangedSignedStockCannotCommit()
    {
        var fixture = new Fixture("toner");
        fixture.Transport.Stock = 2;
        await Assert.ThrowsAsync<CopiersMaintenanceV2ValidationException>(() => fixture.Service.CommitAsync(Command("toner") with { SupplyStockBefore = null }, Pdf()));
        fixture.Transport.Stock = 9;
        await Assert.ThrowsAsync<CopiersMaintenanceV2ConcurrencyException>(() => fixture.Service.ValidateAsync(Command("toner")));
        Assert.Equal(0, fixture.Transport.AssetMutations);
    }

    [Fact]
    public async Task SupplyAndOriginNamesAreVerifiedBeforeSigningBusinessMutation()
    {
        var fixture = new Fixture("toner");
        await Assert.ThrowsAsync<CopiersMaintenanceV2ConcurrencyException>(() => fixture.Service.ValidateAsync(Command("toner") with { SupplyName = "Producto falsificado" }));
        fixture = new Fixture("movement");
        await Assert.ThrowsAsync<CopiersMaintenanceV2ConcurrencyException>(() => fixture.Service.ValidateAsync(Command("movement") with { OriginClientName = "Origen falsificado" }));
        Assert.Equal(0, fixture.Transport.AssetMutations);
    }

    [Fact]
    public async Task ChangedOriginOrEquipmentClientStopsActivity()
    {
        var fixture = new Fixture("movement");
        fixture.Transport.AssignedClient = ClientId;
        await Assert.ThrowsAsync<CopiersMaintenanceV2ConcurrencyException>(() => fixture.Service.ValidateAsync(Command("movement")));
        fixture = new Fixture("toner");
        fixture.Transport.AssignedClient = OriginId;
        await Assert.ThrowsAsync<CopiersMaintenanceV2ValidationException>(() => fixture.Service.ValidateAsync(Command("toner")));
    }

    [Fact]
    public async Task MovementFromUnassignedStockUsesTheActualFormLabel()
    {
        var fixture = new Fixture("movement");
        fixture.Transport.AssignedClient = "";
        var command = Command("movement") with { OriginClientId = "", OriginClientName = "Stock" };
        Assert.False(await fixture.Service.ValidateAsync(command));
        await fixture.Service.CommitAsync(command, Pdf());
        Assert.Null(fixture.Transport.Business!["dtc_originclientkey"]);
        Assert.Equal("Stock", fixture.Transport.Business["dtc_originclientname"]!.GetValue<string>());
        Assert.Equal(ClientId, fixture.Transport.AssignedClient);
        Assert.True(await fixture.Service.ValidateAsync(command));
    }

    [Fact]
    public async Task MovementFromStockCannotForgeAClientName()
    {
        var fixture = new Fixture("movement");
        fixture.Transport.AssignedClient = "";
        await Assert.ThrowsAsync<CopiersMaintenanceV2ValidationException>(() => fixture.Service.ValidateAsync(
            Command("movement") with { OriginClientId = "", OriginClientName = "Cliente falsificado" }));
        Assert.Equal(0, fixture.Transport.AssetMutations);
    }

    [Fact]
    public async Task DepletionSetsExhaustedStatusAndPreservesPurchaseDate()
    {
        var fixture = new Fixture("toner");
        await fixture.Service.CommitAsync(Command("toner") with { SupplyQuantity = 10 }, Pdf());
        Assert.Equal(0, fixture.Transport.Stock);
        var batch = fixture.Transport.Requests.Single(x => x.Path.EndsWith("/$batch")).Body;
        Assert.Contains("\"cr07a_estadodelsuministro\":645250001", batch);
        Assert.DoesNotContain("cr07a_fechadecompra", batch);
    }

    [Fact]
    public async Task MalformedEntityEtagCannotBecomeABatchHeader()
    {
        var fixture = new Fixture("toner");
        fixture.Transport.InvalidVersion = true;
        await Assert.ThrowsAsync<CopiersMaintenanceV2PersistenceException>(() => fixture.Service.ValidateAsync(Command("toner")));
        Assert.DoesNotContain(fixture.Transport.Requests, x => x.Path.EndsWith("/$batch"));
    }

    [Fact]
    public void DeterministicBusinessIdBindsKindAndCanonicalReportGuid()
    {
        Assert.Equal(CopiersActivityV2BusinessService.BuildBusinessRecordId(ReportId, "toner"),
            CopiersActivityV2BusinessService.BuildBusinessRecordId(Guid.Parse(ReportId).ToString("B").ToUpperInvariant(), "TONER"));
        Assert.NotEqual(CopiersActivityV2BusinessService.BuildBusinessRecordId(ReportId, "toner"),
            CopiersActivityV2BusinessService.BuildBusinessRecordId(ReportId, "movement"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("http://business-tests.example.test")]
    [InlineData("https://business-tests.example.test/other/path")]
    [InlineData("https://business-tests.example.test/?forward=other")]
    public async Task InvalidActivityUrlDoesNotBreakLegacyDiButCannotEmitAMutation(string configuredUrl)
    {
        var transport = new FakeTransport("toner");
        var service = new CopiersActivityV2BusinessService(transport, new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            { ["CopiersMtoV2:DataverseApp:BaseUrl"] = configuredUrl }).Build());
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CommitAsync(Command("toner"), Pdf()));
        Assert.All(transport.Requests, request => Assert.Equal(HttpMethod.Get, request.Method));
        Assert.Equal(0, transport.AssetMutations);
    }

    private static CopiersActivityV2BusinessCommand Command(string kind) => new()
    {
        ReportRecordId = ReportId, SubmissionKey = "activity-test-operation-123456", FinalizationFingerprint = Fingerprint,
        ActivityKind = kind, ClientId = ClientId, EquipmentId = EquipmentId, EquipmentSerial = "SERIAL-001",
        TechnicianSystemUserId = TechnicianId, ServiceReference = "ACT-001234", ServiceDate = new(2026, 9, 10),
        OccurredAtUtc = DateTimeOffset.Parse("2026-09-10T18:30:42.500-05:00", CultureInfo.InvariantCulture),
        OriginClientId = kind == "movement" ? OriginId : "", OriginClientName = kind == "movement" ? "Cliente origen" : "",
        MovementReason = kind == "movement" ? "Traslado por renovación" : "", SupplyId = kind == "toner" ? SupplyId : "",
        SupplyName = kind == "toner" ? "Toner referencia 5500" : "", SupplyQuantity = kind == "toner" ? 3 : 0,
        SupplyStockBefore = kind == "toner" ? 10 : null
    };

    private static CopiersMaintenanceV2StoredFile Pdf()
    {
        var bytes = "%PDF-1.7\nDeterministic signed report test\n%%EOF"u8.ToArray();
        return new() { FileName = "ACT-001234-Firmado.pdf", ContentType = "application/pdf", Content = bytes,
            Size = bytes.Length, Sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant() };
    }

    private sealed class Fixture
    {
        public FakeTransport Transport { get; }
        public CopiersActivityV2BusinessService Service { get; }
        public Fixture(string kind)
        {
            Transport = new(kind);
            Service = new(Transport, new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            { ["CopiersMtoV2:DataverseApp:BaseUrl"] = Organization }).Build());
        }
    }

    private sealed record Request(HttpMethod Method, string Path, string Body, string ContentType);
    private sealed class FakeTransport(string kind) : ICopiersMtoV2ApplicationDataverseClient
    {
        public List<Request> Requests { get; } = [];
        public JsonObject? Business { get; set; }
        public byte[]? FileBytes { get; set; }
        public string AssignedClient { get; set; } = kind == "movement" ? OriginId : ClientId;
        public string SupplyName { get; set; } = "Toner referencia 5500";
        public int Category { get; set; } = 645250000;
        public int Stock { get; set; } = 10;
        public int AssetMutations { get; private set; }
        public int FileUploads { get; private set; }
        public bool RaceAssetBeforeBatch { get; set; }
        public bool ConcurrentIdenticalCommit { get; set; }
        public bool LoseBatchResponse { get; set; }
        public bool MalformedBatchResponse { get; set; }
        public bool FailNextFileUpload { get; set; }
        public bool InvalidVersion { get; set; }
        private int _equipmentVersion = 10;
        private int _supplyVersion = 20;

        public async Task<HttpResponseMessage> SendAsync(string relativeUrl, HttpMethod method, HttpContent? content,
            Action<HttpRequestMessage>? customizeRequest, CancellationToken ct = default)
        {
            using var request = new HttpRequestMessage();
            customizeRequest?.Invoke(request);
            var body = content is null || content.Headers.ContentType?.MediaType == "application/octet-stream" ? "" : await content.ReadAsStringAsync(ct);
            Requests.Add(new(method, relativeUrl, body, content?.Headers.ContentType?.ToString() ?? ""));
            var path = relativeUrl.Split('?')[0];
            if (path.EndsWith("/$batch", StringComparison.Ordinal)) return ApplyBatch(body);
            if (path.EndsWith("/$value", StringComparison.Ordinal))
                return FileBytes is null ? Json(new { }, HttpStatusCode.NotFound) : new(HttpStatusCode.OK) { Content = new ByteArrayContent(FileBytes) };
            if (method == HttpMethod.Patch)
            {
                Assert.Contains(kind == "movement" ? "cr07a_actadeentrega" : "cr07a_comprobantedeentrega", path);
                Assert.Equal("*", request.Headers.GetValues("If-Match").Single());
                Assert.Equal("ACT-001234-Firmado.pdf", request.Headers.GetValues("x-ms-file-name").Single());
                if (FailNextFileUpload) { FailNextFileUpload = false; return Json(new { }, HttpStatusCode.InternalServerError); }
                FileBytes = await content!.ReadAsByteArrayAsync(ct);
                FileUploads++;
                return Json(new { }, HttpStatusCode.NoContent);
            }
            Assert.Equal(HttpMethod.Get, method);
            if (path.Contains("cr07a_entregas(", StringComparison.Ordinal) || path.Contains("cr07a_movimientosequiposes(", StringComparison.Ordinal))
                return Business is null ? Json(new { }, HttpStatusCode.NotFound) : Json(Business);
            if (path.EndsWith($"cr07a_clientes({ClientId})", StringComparison.Ordinal))
                return Json(new { cr07a_clienteid = ClientId, cr07a_nombre = "Cliente destino", statecode = 0 });
            if (path.EndsWith($"cr07a_clientes({OriginId})", StringComparison.Ordinal))
                return Json(new { cr07a_clienteid = OriginId, cr07a_nombre = "Cliente origen", statecode = 0 });
            if (path.EndsWith($"cr07a_equipos({EquipmentId})", StringComparison.Ordinal))
                return Json(new Dictionary<string, object?> { ["cr07a_equipoid"] = EquipmentId, ["cr07a_nombredelequipo"] = "SERIAL-001",
                    ["_cr07a_cliente_value"] = AssignedClient, ["@odata.etag"] = $"W/\"{_equipmentVersion}\"" });
            if (path.EndsWith($"cr07a_suministros({SupplyId})", StringComparison.Ordinal))
                return Json(new Dictionary<string, object?> { ["cr07a_suministroid"] = SupplyId, ["cr07a_nombredelsuministro"] = SupplyName,
                    ["cr07a_cantidad"] = Stock, ["cr07a_categoria"] = Category, ["@odata.etag"] = InvalidVersion ? "W/\"20\"\r\nInjected: 1" : $"W/\"{_supplyVersion}\"" });
            throw new NotSupportedException(relativeUrl);
        }

        private HttpResponseMessage ApplyBatch(string body)
        {
            if (MalformedBatchResponse) return new(HttpStatusCode.OK) { Content = new StringContent("not-a-change-set") };
            var operations = Regex.Matches(body, @"PATCH (?<url>https://[^\s]+) HTTP/1\.1\r\nContent-Type: [^\r]+\r\n(?<header>If-(?:None-)?Match): (?<etag>[^\r]+)\r\n\r\n(?<json>[^\r\n]+)");
            Assert.Equal(2, operations.Count);
            Assert.All(operations.Cast<Match>(), operation => Assert.StartsWith(Organization + "/api/data/v9.2/", operation.Groups["url"].Value));
            Assert.Equal("If-None-Match", operations[0].Groups["header"].Value);
            Assert.Equal("*", operations[0].Groups["etag"].Value);
            Assert.Equal("If-Match", operations[1].Groups["header"].Value);
            if (RaceAssetBeforeBatch)
            {
                if (kind == "movement") { AssignedClient = ClientId; _equipmentVersion++; }
                else { Stock--; _supplyVersion++; }
                RaceAssetBeforeBatch = false;
            }
            var expected = kind == "movement" ? $"W/\"{_equipmentVersion}\"" : $"W/\"{_supplyVersion}\"";
            if (Business is not null || operations[1].Groups["etag"].Value != expected) return BatchResponse(412);
            var row = JsonNode.Parse(operations[0].Groups["json"].Value)!.AsObject();
            var patch = JsonNode.Parse(operations[1].Groups["json"].Value)!.AsObject();
            var primary = kind == "movement" ? "cr07a_movimientosequiposid" : "cr07a_entregaid";
            row[primary] = CopiersActivityV2BusinessService.BuildBusinessRecordId(ReportId, kind);
            foreach (var (navigation, lookup) in new[]
            {
                ("ownerid", "_ownerid_value"), ("cr07a_Cliente", "_cr07a_cliente_value"), ("cr07a_Equipo", "_cr07a_equipo_value"),
                ("cr07a_IDdecliente", "_cr07a_iddecliente_value"), ("cr07a_IDdeequipo", "_cr07a_iddeequipo_value"),
                ("cr07a_IDdesuministro", "_cr07a_iddesuministro_value")
            })
            {
                if (row[navigation + "@odata.bind"] is not { } binding) continue;
                row[lookup] = BindingId(binding.GetValue<string>());
                row.Remove(navigation + "@odata.bind");
            }
            Business = row;
            if (kind == "movement") { AssignedClient = BindingId(patch["cr07a_Cliente@odata.bind"]!.GetValue<string>()); _equipmentVersion++; }
            else { Stock = patch["cr07a_cantidad"]!.GetValue<int>(); _supplyVersion++; }
            AssetMutations++;
            if (LoseBatchResponse) { LoseBatchResponse = false; throw new CopiersMaintenanceV2PersistenceException("Simulated response loss after atomic commit"); }
            return ConcurrentIdenticalCommit ? BatchResponse(412) : BatchResponse(204, 204);
        }

        private static string BindingId(string binding) => binding[(binding.IndexOf('(') + 1)..binding.IndexOf(')')];
        private static HttpResponseMessage Json(object body, HttpStatusCode status = HttpStatusCode.OK) => new(status)
        { Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json") };
        private static HttpResponseMessage BatchResponse(params int[] statuses) => new(HttpStatusCode.OK)
        { Content = new StringContent(string.Join("\r\n", statuses.Select(status => $"HTTP/1.1 {status} Test\r\n"))) };
    }
}
