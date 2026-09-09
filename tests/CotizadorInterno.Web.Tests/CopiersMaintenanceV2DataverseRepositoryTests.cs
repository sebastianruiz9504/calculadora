using System.Globalization;
using System.Net;
using System.Runtime.CompilerServices;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CotizadorInterno.Web.Models.CopiersMtoV2;
using CotizadorInterno.Web.Services.CopiersMtoV2;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace CotizadorInterno.Web.Tests;

public sealed class CopiersMaintenanceV2DataverseRepositoryTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CompleteFinalization_DataverseWholeSecondReadBack_PublishesWithAndWithoutLocation(bool includeLocation)
    {
        var fixture = new Fixture(includeLocation);
        var originalSignedAt = fixture.Command.DeviceSignedAtUtc;
        var originalFinalizedAt = fixture.Command.ServerFinalizedAtUtc;
        var originalLocationAt = fixture.Command.InternalLocation?.CapturedAtUtc;

        var result = await fixture.Repository.CompleteFinalizationAsync(fixture.Command);

        Assert.Equal(CopiersMaintenanceV2WorkflowState.ReadyToSend, result.State);
        Assert.Equal(CopiersMaintenanceV2EmailState.Pending, result.EmailState);
        Assert.Equal(1, fixture.Transport.ReadyPatches);
        Assert.Equal(4, fixture.Transport.EvidenceCreates);
        Assert.Equal(4, fixture.Transport.FileUploads);
        Assert.Equal(4, fixture.Transport.VerifiedFileReads);
        Assert.Equal(2, fixture.Transport.ConditionalMainPatches);
        Assert.Equal(originalSignedAt, fixture.Command.DeviceSignedAtUtc);
        Assert.Equal(originalFinalizedAt, fixture.Command.ServerFinalizedAtUtc);
        Assert.Equal(originalLocationAt, fixture.Command.InternalLocation?.CapturedAtUtc);
        Assert.Equal("fingerprint-with-original-device-precision", result.FinalizationFingerprint);
        Assert.All(fixture.Transport.WrittenInstants, instant =>
        {
            Assert.Equal(TimeSpan.Zero, instant.Offset);
            Assert.Equal(0, instant.Ticks % TimeSpan.TicksPerSecond);
        });
        Assert.NotEmpty(fixture.Transport.WrittenInstants);
        Assert.Equal("2026-09-08T20:30:02Z", fixture.Transport.Main[fixture.Options.ReadyAtUtcField]!.GetValue<string>());
    }

    [Theory]
    [InlineData("dtc_devicesignedatutc", "deviceSignedAt")]
    [InlineData("dtc_serverfinalizedatutc", "serverFinalizedAt")]
    [InlineData("dtc_locationcapturedatutc", "locationCapturedAt")]
    public async Task CompleteFinalization_RealOneSecondDateDrift_NeverPublishes(string field, string expectedFailure)
    {
        var fixture = new Fixture();
        fixture.Transport.StagingMutation = row => row[field] =
            DateTimeOffset.Parse(row[field]!.GetValue<string>(), CultureInfo.InvariantCulture)
                .AddSeconds(1).ToString("O", CultureInfo.InvariantCulture);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Repository.CompleteFinalizationAsync(fixture.Command));

        Assert.Contains(expectedFailure, exception.Message);
        Assert.Equal(0, fixture.Transport.ReadyPatches);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CompleteFinalization_CoordinateDriftOrMissingProtectedField_NeverPublishes(bool missing)
    {
        var fixture = new Fixture();
        fixture.Transport.StagingMutation = row => row[fixture.Options.LatitudeField] = missing
            ? null
            : fixture.Command.InternalLocation!.Latitude + 0.0000001d;

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Repository.CompleteFinalizationAsync(fixture.Command));

        Assert.Contains("latitude", exception.Message);
        Assert.Equal(0, fixture.Transport.ReadyPatches);
    }

    [Fact]
    public async Task CompleteFinalization_MissingProtectedTimestamp_NeverPublishes()
    {
        var fixture = new Fixture();
        fixture.Transport.StagingMutation = row => row[fixture.Options.LocationCapturedAtUtcField] = null;

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Repository.CompleteFinalizationAsync(fixture.Command));

        Assert.Contains("locationCapturedAt", exception.Message);
        Assert.Equal(0, fixture.Transport.ReadyPatches);
    }

    [Fact]
    public async Task CompleteFinalization_CorruptedEvidenceBinary_NeverStagesOrPublishes()
    {
        var fixture = new Fixture();
        fixture.Transport.CorruptFileReadBack = true;

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Repository.CompleteFinalizationAsync(fixture.Command));

        Assert.Contains("no coincide con el archivo recibido", exception.Message);
        Assert.Equal(0, fixture.Transport.StagingPatches);
        Assert.Equal(0, fixture.Transport.ReadyPatches);
    }

    [Fact]
    public async Task CompleteFinalization_EvidenceMetadataDrift_NeverStagesOrPublishes()
    {
        var fixture = new Fixture();
        fixture.Transport.EvidenceMutation = row => row[fixture.Options.EvidenceSizeField] = 123456;

        await Assert.ThrowsAsync<CopiersMaintenanceV2ConcurrencyException>(() =>
            fixture.Repository.CompleteFinalizationAsync(fixture.Command));

        Assert.Equal(0, fixture.Transport.FileUploads);
        Assert.Equal(0, fixture.Transport.StagingPatches);
        Assert.Equal(0, fixture.Transport.ReadyPatches);
    }

    [Fact]
    public async Task CompleteFinalization_StagingResponseLost_ReusesVerifiedEvidenceAfterFailedLeaseRetry()
    {
        var fixture = new Fixture();
        fixture.Transport.FailNextStagingResponse = true;
        await Assert.ThrowsAsync<CopiersMaintenanceV2PersistenceException>(() =>
            fixture.Repository.CompleteFinalizationAsync(fixture.Command));
        Assert.Equal(4, fixture.Transport.EvidenceCreates);
        Assert.Equal(0, fixture.Transport.ReadyPatches);
        var failed = await fixture.Repository.MarkFinalizationFailedAsync(new()
        {
            RecordId = fixture.Command.RecordId,
            SubmissionKey = fixture.Command.SubmissionKey,
            TechnicianSystemUserId = fixture.Command.TechnicianSystemUserId,
            FinalizationLeaseId = fixture.Command.FinalizationLeaseId,
            ErrorCode = "test-response-lost",
            ErrorMessage = "Simulated staging response lost"
        });
        var acquired = await fixture.Repository.TryBeginFinalizationAsync(new()
        {
            RecordId = failed.RecordId,
            SubmissionKey = failed.SubmissionKey,
            TechnicianSystemUserId = failed.TechnicianSystemUserId,
            ExpectedVersion = failed.Version,
            FinalizationLeaseId = "lease-second-attempt",
            StartedAtUtc = Fixture.NowUtc
        });
        Assert.Equal(CopiersMaintenanceV2BeginDisposition.Acquired, acquired.Disposition);
        fixture.Command.FinalizationLeaseId = acquired.FinalizationLeaseId;

        var completed = await fixture.Repository.CompleteFinalizationAsync(fixture.Command);

        Assert.Equal(CopiersMaintenanceV2WorkflowState.ReadyToSend, completed.State);
        Assert.Equal(4, fixture.Transport.EvidenceCreates);
        Assert.Equal(4, fixture.Transport.FileUploads);
        Assert.Equal(8, fixture.Transport.VerifiedFileReads);
        Assert.Equal(1, fixture.Transport.ReadyPatches);
    }

    [Fact]
    public async Task CompleteFinalization_ConcurrentParentWriteWhileUploading_NeverStagesOrPublishes()
    {
        var fixture = new Fixture();
        fixture.Transport.BumpParentVersionAfterFileUpload = true;

        await Assert.ThrowsAsync<CopiersMaintenanceV2ConcurrencyException>(() =>
            fixture.Repository.CompleteFinalizationAsync(fixture.Command));

        Assert.Equal(0, fixture.Transport.StagingPatches);
        Assert.Equal(0, fixture.Transport.ReadyPatches);
    }

    private sealed class Fixture
    {
        public static readonly DateTimeOffset NowUtc = DateTimeOffset.Parse("2026-09-08T20:30:02.6789012Z", CultureInfo.InvariantCulture);
        public CopiersMaintenanceV2DataverseOptions Options { get; }
        public CopiersMaintenanceV2CompleteFinalizationCommand Command { get; }
        public DataverseTransport Transport { get; }
        public CopiersMaintenanceV2DataverseRepository Repository { get; }

        public Fixture(bool includeLocation = true)
        {
            Options = ReadOptions();
            var record = new CopiersMaintenanceV2DraftRecord
            {
                RecordId = "eb5b1b62-3a3b-4aa6-9f10-715d13685df7",
                SubmissionKey = "repository-real-shape-regression",
                ServiceReference = "MTO-000123",
                TechnicianSystemUserId = "7ebf05a3-fcbb-480c-94f7-34207b1e7a6a",
                TechnicianName = "Técnico de prueba",
                TechnicianEmail = "technician@example.invalid",
                ClientId = "c129cc99-a4d1-4c7d-a768-a8c717e5ee35",
                ClientName = "Cliente de prueba",
                CustomerContactName = "Contacto de prueba",
                CustomerEmail = "client@example.invalid",
                EquipmentId = "18b7dad5-3e40-40a4-85de-31140c1f0f3d",
                EquipmentSerial = "TEST-SERIAL-123",
                Title = "Mantenimiento de prueba",
                ServiceDate = new DateOnly(2026, 9, 8),
                MaintenanceTypeValue = Options.MaintenanceTypePreventiveValue
            };
            Command = new()
            {
                RecordId = record.RecordId,
                SubmissionKey = record.SubmissionKey,
                TechnicianSystemUserId = record.TechnicianSystemUserId,
                FinalizationLeaseId = "lease-first-attempt",
                BaseSnapshot = CopiersMaintenanceV2BaseSnapshot.From(record),
                FinalizationFingerprint = "fingerprint-with-original-device-precision",
                FormVersion = "copiers-mto-v2-2026-08-27",
                Answers = [new() { Key = "service", Label = "Servicio", Value = "Prueba", SortOrder = 1 }],
                WorkPerformed = "Limpieza y revisión completa.",
                CustomerObservations = "",
                InternalNotes = "",
                ServiceAddressInternal = "Sede de prueba",
                SignerName = "Persona que firma",
                SignerRole = "Administrador",
                CustomerAccepted = true,
                SignaturePointCount = 32,
                DeviceSignedAtUtc = DateTimeOffset.Parse("2026-09-08T15:30:00.123-05:00", CultureInfo.InvariantCulture),
                ServerFinalizedAtUtc = DateTimeOffset.Parse("2026-09-08T20:30:01.9876543Z", CultureInfo.InvariantCulture),
                InternalLocation = includeLocation ? new()
                {
                    Latitude = 4.7110123d,
                    Longitude = -74.0721234d,
                    AccuracyMeters = 13.1234567d,
                    CapturedAtUtc = DateTimeOffset.Parse("2026-09-08T20:29:59.456Z", CultureInfo.InvariantCulture),
                    Source = "navigator.geolocation"
                } : null,
                Signature = File("firma.jpg", "image/jpeg", "signature binary"),
                SignedReport = File("MTO-000123.pdf", "application/pdf", "%PDF-1.4\nreport binary"),
                OriginalAttachments = [File("evidencia.jpg", "image/jpeg", "original evidence binary")],
                CustomerAttachments = [File("evidencia-cliente.jpg", "image/jpeg", "sanitized customer binary")],
                EmailOutbox = new()
                {
                    OutboxKey = "test-outbox",
                    To = [record.CustomerEmail],
                    Subject = "MTO-000123",
                    HtmlBody = "<p>Reporte de prueba</p>"
                }
            };
            Transport = new(Options, record, Command.FinalizationLeaseId);
            var context = new HttpContextAccessor
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, record.TechnicianSystemUserId)], "test"))
                }
            };
            Repository = new(Transport, context, Microsoft.Extensions.Options.Options.Create(Options),
                new FixedTimeProvider(), NullLogger<CopiersMaintenanceV2DataverseRepository>.Instance);
        }

        private static CopiersMaintenanceV2StoredFile File(string name, string type, string text)
        {
            var bytes = Encoding.UTF8.GetBytes(text);
            return new() { FileName = name, ContentType = type, Content = bytes, Size = bytes.Length, Sha256 = Convert.ToHexString(SHA256.HashData(bytes)) };
        }

        private static CopiersMaintenanceV2DataverseOptions ReadOptions([CallerFilePath] string source = "")
        {
            var root = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(source)!, "..", ".."));
            using var settings = JsonDocument.Parse(System.IO.File.ReadAllText(Path.Combine(root, "appsettings.json")));
            var options = settings.RootElement.GetProperty("CopiersMtoV2").GetProperty("Dataverse")
                .Deserialize<CopiersMaintenanceV2DataverseOptions>()!;
            Assert.Empty(options.FindMissingBindings());
            return options;
        }

        private sealed class FixedTimeProvider : TimeProvider
        {
            public override DateTimeOffset GetUtcNow() => NowUtc;
        }
    }

    /// <summary>
    /// Exercises the production repository, not a repository mock. Dataverse wire
    /// behavior includes second-precision UTC dates, decimal(7), null strings,
    /// OData lookup/ETag shapes, alternate-key evidence and independent file GETs.
    /// </summary>
    private sealed class DataverseTransport : ICopiersMtoV2ApplicationDataverseClient
    {
        private readonly CopiersMaintenanceV2DataverseOptions _options;
        private readonly Dictionary<string, JsonObject> _evidence = new(StringComparer.Ordinal);
        private readonly Dictionary<string, byte[]> _files = new(StringComparer.Ordinal);
        private readonly HashSet<string> _dateFields;
        private int _version = 100;
        public JsonObject Main { get; }
        public List<DateTimeOffset> WrittenInstants { get; } = [];
        public Action<JsonObject>? StagingMutation { get; set; }
        public Action<JsonObject>? EvidenceMutation { get; set; }
        public bool CorruptFileReadBack { get; set; }
        public bool FailNextStagingResponse { get; set; }
        public bool BumpParentVersionAfterFileUpload { get; set; }
        public int ReadyPatches { get; private set; }
        public int StagingPatches { get; private set; }
        public int EvidenceCreates { get; private set; }
        public int FileUploads { get; private set; }
        public int VerifiedFileReads { get; private set; }
        public int ConditionalMainPatches { get; private set; }

        public DataverseTransport(CopiersMaintenanceV2DataverseOptions options, CopiersMaintenanceV2DraftRecord record, string lease)
        {
            _options = options;
            _dateFields = [options.DeviceSignedAtUtcField, options.ServerFinalizedAtUtcField,
                options.LocationCapturedAtUtcField, options.ReadyAtUtcField, options.EvidenceSecurityCheckedAtUtcField];
            Main = new()
            {
                [options.MainIdField] = record.RecordId,
                [options.ServiceReferenceField] = record.ServiceReference,
                [options.OperationKeyField] = record.SubmissionKey,
                [options.WorkflowStateField] = options.FinalizingStateValue,
                [options.EmailStateField] = options.EmailNotReadyStateValue,
                [options.FinalizationLeaseIdField] = lease,
                [options.TechnicianUserIdField] = record.TechnicianSystemUserId,
                [options.TechnicianNameField] = record.TechnicianName,
                [options.TechnicianEmailField] = record.TechnicianEmail,
                [$"_{options.ClientLookupLogicalName}_value"] = record.ClientId,
                [options.ClientNameField] = record.ClientName,
                [options.ClientContactNameField] = record.CustomerContactName,
                [options.ClientEmailField] = record.CustomerEmail,
                [$"_{options.EquipmentLookupLogicalName}_value"] = record.EquipmentId,
                [options.EquipmentSerialField] = record.EquipmentSerial,
                [options.TitleField] = record.Title,
                [options.ServiceDateField] = record.ServiceDate.ToString("yyyy-MM-dd") + "T00:00:00Z",
                [options.MaintenanceTypeField] = record.MaintenanceTypeValue,
                ["modifiedon"] = "2026-09-08T20:30:01Z"
            };
        }

        public async Task<HttpResponseMessage> SendAsync(string relativeUrl, HttpMethod method, HttpContent? content,
            Action<HttpRequestMessage>? customizeRequest, CancellationToken ct = default)
        {
            var uri = new Uri("https://example.invalid" + relativeUrl);
            using var request = new HttpRequestMessage(method, uri);
            customizeRequest?.Invoke(request);
            var path = uri.AbsolutePath;
            var mainPath = $"/api/data/v9.2/{_options.MainEntitySetName}({Main[_options.MainIdField]!.GetValue<string>()})";
            if (path == mainPath)
            {
                if (method == HttpMethod.Get)
                {
                    var result = Select(Main, uri);
                    result["@odata.etag"] = $"W/\"{_version}\"";
                    return Json(result);
                }
                Assert.Equal(HttpMethod.Patch, method);
                Assert.True(request.Headers.TryGetValues("If-Match", out var values));
                ConditionalMainPatches++;
                if (values.Single() != $"W/\"{_version}\"")
                    return new(HttpStatusCode.PreconditionFailed);
                var payload = JsonNode.Parse(await content!.ReadAsStringAsync(ct))!.AsObject();
                var isStaging = payload.ContainsKey(_options.AnswersJsonField);
                var isReady = payload[_options.WorkflowStateField]?.GetValue<int>() == _options.ReadyToSendStateValue;
                Apply(Main, payload);
                _version++;
                if (isStaging)
                {
                    StagingPatches++;
                    StagingMutation?.Invoke(Main);
                    if (FailNextStagingResponse)
                    {
                        FailNextStagingResponse = false;
                        return new(HttpStatusCode.ServiceUnavailable) { Content = new StringContent("Simulated lost response after persisted staging") };
                    }
                }
                if (isReady) ReadyPatches++;
                return new(HttpStatusCode.NoContent);
            }

            var evidenceBase = $"/api/data/v9.2/{_options.EvidenceEntitySetName}";
            if (path == evidenceBase)
            {
                if (method == HttpMethod.Get)
                {
                    var filter = Query(uri, "$filter");
                    var key = filter[(filter.IndexOf('\'') + 1)..filter.LastIndexOf('\'')];
                    var rows = _evidence.Values.Where(item => item[_options.EvidenceKeyField]!.GetValue<string>() == key)
                        .Select(item => Select(item, uri)).ToArray();
                    return Json(new JsonObject { ["value"] = new JsonArray(rows.Cast<JsonNode>().ToArray()) });
                }
                Assert.Equal(HttpMethod.Post, method);
                var payload = JsonNode.Parse(await content!.ReadAsStringAsync(ct))!.AsObject();
                var id = Guid.NewGuid().ToString("D");
                var row = new JsonObject { [_options.EvidenceIdField] = id };
                Apply(row, payload);
                EvidenceMutation?.Invoke(row);
                _evidence.Add(id, row);
                EvidenceCreates++;
                return Json(row.DeepClone(), HttpStatusCode.Created);
            }

            Assert.StartsWith(evidenceBase + "(", path);
            var evidenceId = path[(path.IndexOf('(') + 1)..path.IndexOf(')')];
            Assert.True(_evidence.ContainsKey(evidenceId));
            if (method == HttpMethod.Patch)
            {
                Assert.EndsWith("/" + _options.EvidenceFileField, path);
                _files[evidenceId] = await content!.ReadAsByteArrayAsync(ct);
                FileUploads++;
                if (BumpParentVersionAfterFileUpload) _version++;
                return new(HttpStatusCode.NoContent);
            }
            Assert.Equal(HttpMethod.Get, method);
            Assert.EndsWith("/" + _options.EvidenceFileField + "/$value", path);
            if (!_files.TryGetValue(evidenceId, out var bytes)) return new(HttpStatusCode.NoContent);
            VerifiedFileReads++;
            var resultBytes = bytes.ToArray();
            if (CorruptFileReadBack) resultBytes[0] ^= 0xff;
            return new(HttpStatusCode.OK) { Content = new ByteArrayContent(resultBytes) };
        }

        private void Apply(JsonObject target, JsonObject payload)
        {
            foreach (var (field, value) in payload)
            {
                if (field == $"{_options.EvidenceParentNavigationProperty}@odata.bind")
                {
                    var bind = value!.GetValue<string>();
                    target[$"_{_options.EvidenceParentLookupLogicalName}_value"] = bind[(bind.IndexOf('(') + 1)..bind.IndexOf(')')];
                }
                else if (_dateFields.Contains(field) && value is not null)
                {
                    var instant = DateTimeOffset.Parse(value.GetValue<string>(), CultureInfo.InvariantCulture);
                    WrittenInstants.Add(instant);
                    target[field] = instant.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
                }
                else if ((field == _options.LatitudeField || field == _options.LongitudeField || field == _options.AccuracyMetersField) && value is not null)
                    target[field] = Math.Round(value.GetValue<double>(), 7, MidpointRounding.AwayFromZero);
                else if (value is JsonValue json && json.TryGetValue<string>(out var text) && text == "")
                    target[field] = null;
                else
                    target[field] = value?.DeepClone();
            }
        }

        private static JsonObject Select(JsonObject source, Uri uri)
        {
            var result = new JsonObject();
            foreach (var field in Query(uri, "$select").Split(',', StringSplitOptions.RemoveEmptyEntries))
                result[field] = source[field]?.DeepClone();
            return result;
        }

        private static string Query(Uri uri, string name) => uri.Query.TrimStart('?').Split('&')
            .Select(part => part.Split('=', 2)).Where(part => Uri.UnescapeDataString(part[0]) == name)
            .Select(part => Uri.UnescapeDataString(part[1])).Single();

        private static HttpResponseMessage Json(JsonNode? value, HttpStatusCode status = HttpStatusCode.OK) =>
            new(status) { Content = new StringContent(value!.ToJsonString(), Encoding.UTF8, "application/json") };
    }
}
