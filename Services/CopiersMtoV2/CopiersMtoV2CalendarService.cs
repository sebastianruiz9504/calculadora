using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using CotizadorInterno.Web.Models;
using CotizadorInterno.Web.Models.Dashboard;
using CotizadorInterno.Web.Models.Permissions;
using Microsoft.Extensions.Options;

namespace CotizadorInterno.Web.Services.CopiersMtoV2;

public interface ICopiersMtoV2CalendarService
{
    Task<CopiersMtoV2CalendarBootstrapDto> BootstrapAsync(CancellationToken ct = default);
    Task<CopiersMtoV2CalendarWeekDto> WeekAsync(string technicianId, string weekStart, CancellationToken ct = default);
    Task<CopiersMtoV2CalendarDetailDto> DetailAsync(string id, CancellationToken ct = default);
    Task<CopiersMtoV2CalendarFile> EvidenceAsync(string id, string evidenceKey, CancellationToken ct = default);
}

/// <summary>
/// Read-only internal reporting boundary. The app-only identity can read every technician's
/// secured snapshot, so authorization must precede every request, including file downloads.
/// </summary>
public sealed class CopiersMtoV2CalendarService(
    ICopiersMtoV2ApplicationDataverseClient client,
    IDataverseService dataverse,
    IHttpContextAccessor contextAccessor,
    IOptions<CopiersMaintenanceV2DataverseOptions> options,
    IConfiguration configuration) : ICopiersMtoV2CalendarService
{
    private const int MaxPages = 20;
    private const int MaxRows = 5000;
    private const int MaxFileBytes = 12 * 1024 * 1024;
    private const string AllTechnicians = "all";
    private static readonly TimeSpan BogotaOffset = TimeSpan.FromHours(-5);
    private static readonly CultureInfo Spanish = CultureInfo.GetCultureInfo("es-CO");
    private readonly CopiersMaintenanceV2DataverseOptions _o = options.Value;
    private readonly Uri _organization = new(configuration["CopiersMtoV2:DataverseApp:BaseUrl"]
        ?? "https://orgc79ca19c.crm2.dynamics.com");

    public async Task<CopiersMtoV2CalendarBootstrapDto> BootstrapAsync(CancellationToken ct = default)
    {
        var actor = await AuthorizeAsync(ct);
        var employees = await dataverse.GetEmployeeModulePermissionsAsync(ct);
        var technicians = employees
            .Where(x => x.IsActive && x.ModuleOptionValues.Contains(AppModuleCatalog.Copiers.OptionValue)
                && Guid.TryParse(x.SystemUserId, out var id) && id != Guid.Empty)
            .Select(x => new CopiersMtoV2CalendarTechnicianDto(NormalizeGuid(x.SystemUserId),
                First(x.EmployeeName, x.UserDisplayName, x.UserEmail), x.UserEmail))
            .ToList();
        // Include historical technicians even after their employee record is deactivated.
        // The server groups three small fields; no locations or answer snapshots are loaded.
        var groups = await QueryAsync($"{_o.MainEntitySetName}?$apply=" + Uri.EscapeDataString(
            $"groupby(({_o.TechnicianUserIdField},{_o.TechnicianNameField},{_o.TechnicianEmailField}))"), ct);
        technicians.AddRange(groups.Where(x => Guid.TryParse(Text(x, _o.TechnicianUserIdField), out var id) && id != Guid.Empty)
            .Select(x => new CopiersMtoV2CalendarTechnicianDto(NormalizeGuid(Text(x, _o.TechnicianUserIdField)),
                First(Text(x, _o.TechnicianNameField), Text(x, _o.TechnicianEmailField)), Text(x, _o.TechnicianEmailField))));
        if (Guid.TryParse(actor.SystemUserId, out var actorId) && actorId != Guid.Empty)
            technicians.Add(new(actorId.ToString("D"), First(actor.DisplayName, actor.Email), actor.Email));
        var result = technicians.GroupBy(x => x.Id, StringComparer.OrdinalIgnoreCase).Select(x => x.First())
            .OrderBy(x => x.Name, StringComparer.Create(Spanish, true)).ToArray();
        // Dashboard/Copiers authorization already allows consulting any technician.
        // Do not silently hide a successful visit under another technician by
        // defaulting this reporting view to the signed-in viewer.
        return new(result, result.Length > 0 ? AllTechnicians : "");
    }

    public async Task<CopiersMtoV2CalendarWeekDto> WeekAsync(string technicianId, string weekStart, CancellationToken ct = default)
    {
        await AuthorizeAsync(ct);
        var allTechnicians = string.Equals(technicianId, AllTechnicians, StringComparison.OrdinalIgnoreCase);
        var technician = allTechnicians ? AllTechnicians : NormalizeGuid(technicianId);
        if (!DateOnly.TryParseExact(weekStart, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            || date.Year < 2000 || date.Year > 2100)
            throw new ArgumentException("Selecciona una semana válida (AAAA-MM-DD).");
        var monday = date.AddDays(-(((int)date.DayOfWeek + 6) % 7));
        var until = monday.AddDays(7);
        // ServiceDate is the form's date-only value. Do not compare it to a server-local timezone.
        var filter = $"({_o.WorkflowStateField} eq {_o.ReadyToSendStateValue} or ({_o.WorkflowStateField} eq {_o.FailedStateValue} and {_o.SignedReportEvidenceKeyField} ne null))"
            + (allTechnicians ? "" : $" and {_o.TechnicianUserIdField} eq '{technician}'")
            + $" and {_o.ServiceDateField} ge {monday:yyyy-MM-dd}T00:00:00Z and {_o.ServiceDateField} lt {until:yyyy-MM-dd}T00:00:00Z";
        var rows = await QueryAsync($"{_o.MainEntitySetName}?$select={EventFields()}&$filter={Uri.EscapeDataString(filter)}"
            + $"&$orderby={_o.ServiceDateField} asc,{_o.MainIdField} asc", ct);
        var events = rows.Where(x => IsVisible(x)
                && (allTechnicians || string.Equals(Text(x, _o.TechnicianUserIdField), technician, StringComparison.OrdinalIgnoreCase))
                && ServiceDate(x) >= monday && ServiceDate(x) < until)
            .Select(MapEvent).OrderBy(x => x.StartAtUtc).ThenBy(x => x.Id).ToArray();
        return new(monday.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), events);
    }

    public async Task<CopiersMtoV2CalendarDetailDto> DetailAsync(string id, CancellationToken ct = default)
    {
        await AuthorizeAsync(ct);
        id = NormalizeGuid(id);
        var row = await ReadReadyAsync(id, ct);
        var summary = MapEvent(row);
        var evidenceRows = await EvidenceRowsAsync(id, ct);
        var evidences = evidenceRows.Where(x => IsEvidenceSafe(x, row, id)).Select(x => MapEvidence(x, id)).ToArray();
        return new()
        {
            Id = summary.Id, ServiceReference = summary.ServiceReference, ClientName = summary.ClientName,
            MaintenanceType = summary.MaintenanceType, TechnicianId = summary.TechnicianId,
            TechnicianName = summary.TechnicianName, StartAtUtc = summary.StartAtUtc, EndAtUtc = summary.EndAtUtc,
            DurationEstimated = summary.DurationEstimated, TimingNote = summary.TimingNote,
            WorkflowState = summary.WorkflowState, EmailState = summary.EmailState,
            ClientContactName = Text(row, _o.ClientContactNameField), ClientEmail = Text(row, _o.ClientEmailField),
            EquipmentSerial = Text(row, _o.EquipmentSerialField), Title = Text(row, _o.TitleField),
            TechnicianEmail = Text(row, _o.TechnicianEmailField), ServiceDate = ServiceDate(row).ToString("yyyy-MM-dd"),
            FormVersion = Text(row, _o.FormVersionField), DeviceSignedAtUtc = Instant(row, _o.DeviceSignedAtUtcField),
            ServerFinalizedAtUtc = Instant(row, _o.ServerFinalizedAtUtcField), WorkPerformed = Text(row, _o.WorkPerformedField),
            CustomerObservations = Text(row, _o.CustomerObservationsField), ServiceAddress = Text(row, _o.ServiceAddressInternalField),
            InternalNotes = Text(row, _o.InternalNotesField), SignerName = Text(row, _o.SignerNameField),
            SignerRole = Text(row, _o.SignerRoleField), CustomerAccepted = Boolean(row, _o.CustomerAcceptedField),
            Answers = Answers(row), Evidences = evidences, Location = Location(row),
            ReportUrl = evidences.FirstOrDefault(x => x.Purpose == "SignedReport")?.Url ?? "",
            SignatureUrl = evidences.FirstOrDefault(x => x.Purpose == "Signature")?.Url ?? ""
        };
    }

    public async Task<CopiersMtoV2CalendarFile> EvidenceAsync(string id, string evidenceKey, CancellationToken ct = default)
    {
        await AuthorizeAsync(ct);
        id = NormalizeGuid(id);
        if (!IsHash(evidenceKey)) throw new ArgumentException("La referencia del adjunto no es válida.");
        var parent = await ReadReadyAsync(id, ct);
        var rows = await EvidenceRowsAsync(id, ct);
        var matches = rows.Where(x => string.Equals(Text(x, _o.EvidenceKeyField), evidenceKey, StringComparison.OrdinalIgnoreCase)
            && IsEvidenceSafe(x, parent, id)).ToArray();
        if (matches.Length != 1) throw new KeyNotFoundException();
        var row = matches[0];
        var evidenceId = NormalizeGuid(Text(row, _o.EvidenceIdField));
        var expectedHash = Text(row, _o.EvidenceSha256Field);
        var contentType = Text(row, _o.EvidenceContentTypeField).ToLowerInvariant();
        using var response = await client.SendAsync($"/api/data/v9.2/{_o.EvidenceEntitySetName}({evidenceId})/{_o.EvidenceFileField}/$value",
            HttpMethod.Get, null, null, ct);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException("No fue posible leer el archivo de evidencia.");
        if (response.Content.Headers.ContentLength > MaxFileBytes) throw new InvalidOperationException("El adjunto supera el límite permitido.");
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var output = new MemoryStream();
        var buffer = new byte[64 * 1024];
        int read;
        while ((read = await stream.ReadAsync(buffer, ct)) > 0)
        {
            if (output.Length + read > MaxFileBytes) throw new InvalidOperationException("El adjunto supera el límite permitido.");
            output.Write(buffer, 0, read);
        }
        var bytes = output.ToArray();
        if (bytes.LongLength != Number(row, _o.EvidenceSizeField)
            || !CryptographicOperations.FixedTimeEquals(SHA256.HashData(bytes), Convert.FromHexString(expectedHash))
            || !MatchesContentType(bytes, contentType))
            throw new InvalidOperationException("La integridad del adjunto no coincide con el mantenimiento finalizado.");
        // Metadata must remain stable throughout download; do not serve a changed evidence row.
        var reread = await ReadAsync($"{_o.EvidenceEntitySetName}({evidenceId})?$select={EvidenceFields()}", ct);
        if (!IsEvidenceSafe(reread, parent, id) || Text(reread, "@odata.etag") != Text(row, "@odata.etag")
            || Text(reread, _o.EvidenceSha256Field) != expectedHash
            || Text(reread, _o.EvidenceKeyField) != Text(row, _o.EvidenceKeyField)
            || Text(reread, _o.EvidenceContentTypeField) != Text(row, _o.EvidenceContentTypeField)
            || Number(reread, _o.EvidenceSizeField) != bytes.LongLength)
            throw new InvalidOperationException("La evidencia cambió durante la consulta. Intenta nuevamente.");
        return new(bytes, contentType, SafeFileName(Text(row, _o.EvidenceOriginalFileNameField)));
    }

    private async Task<CurrentUserInfo> AuthorizeAsync(CancellationToken ct)
    {
        if (contextAccessor.HttpContext?.User.Identity?.IsAuthenticated != true) throw new UnauthorizedAccessException();
        var actor = await dataverse.GetCurrentUserAsync(ct);
        if (!AppModuleAccessPolicy.CanAccess(AppModule.Dashboard, actor)
            || !AppModuleAccessPolicy.CanAccess(AppModule.Copiers, actor)) throw new UnauthorizedAccessException();
        if (!_o.SchemaProvisioned || !Regex.IsMatch(_o.MainEntitySetName, "^[a-z0-9_]+$", RegexOptions.CultureInvariant))
            throw new InvalidOperationException("El calendario MTO V2 no está configurado.");
        return actor!;
    }

    private async Task<JsonElement> ReadReadyAsync(string id, CancellationToken ct)
    {
        var row = await ReadAsync($"{_o.MainEntitySetName}({id})?$select={DetailFields()}", ct);
        if (!IsVisible(row) || !string.Equals(Text(row, _o.MainIdField), id, StringComparison.OrdinalIgnoreCase))
            throw new KeyNotFoundException();
        return row;
    }

    private Task<List<JsonElement>> EvidenceRowsAsync(string id, CancellationToken ct) => QueryAsync(
        $"{_o.EvidenceEntitySetName}?$select={EvidenceFields()}&$filter="
        + Uri.EscapeDataString($"_{_o.EvidenceParentLookupLogicalName}_value eq {id}")
        + $"&$orderby={_o.EvidenceSequenceField} asc,{_o.EvidenceIdField} asc", ct);

    private async Task<List<JsonElement>> QueryAsync(string relative, CancellationToken ct)
    {
        var rows = new List<JsonElement>();
        var next = "/api/data/v9.2/" + relative;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var page = 0; page < MaxPages; page++)
        {
            if (!seen.Add(next)) throw new InvalidOperationException("La consulta repitió una página de resultados.");
            var result = await ReadAsync(next, ct);
            if (!result.TryGetProperty("value", out var values) || values.ValueKind != JsonValueKind.Array)
                throw new InvalidOperationException("Dataverse no devolvió una lista válida.");
            rows.AddRange(values.EnumerateArray().Select(x => x.Clone()));
            if (rows.Count > MaxRows) throw new InvalidOperationException("La consulta supera el límite del calendario.");
            var continuation = Text(result, "@odata.nextLink");
            if (string.IsNullOrEmpty(continuation)) return rows;
            next = SafeNextLink(continuation);
        }
        throw new InvalidOperationException("La consulta supera el límite de páginas del calendario.");
    }

    private string SafeNextLink(string link)
    {
        if (!Uri.TryCreate(_organization, link, out var target)
            || target.Scheme != Uri.UriSchemeHttps || target.Host != _organization.Host || target.Port != _organization.Port
            || !string.IsNullOrEmpty(target.UserInfo) || !string.IsNullOrEmpty(target.Fragment)
            || !target.AbsolutePath.StartsWith("/api/data/v9.2/", StringComparison.Ordinal))
            throw new InvalidOperationException("Dataverse devolvió una página fuera del entorno configurado.");
        return target.PathAndQuery;
    }

    private async Task<JsonElement> ReadAsync(string relative, CancellationToken ct)
    {
        if (!relative.StartsWith("/api/data/v9.2/", StringComparison.Ordinal)) relative = "/api/data/v9.2/" + relative;
        using var response = await client.SendAsync(relative, HttpMethod.Get, null,
            request => request.Headers.TryAddWithoutValidation("Prefer", "odata.maxpagesize=250"), ct);
        if (response.StatusCode == HttpStatusCode.NotFound) throw new KeyNotFoundException();
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"Dataverse rechazó la consulta del calendario ({(int)response.StatusCode}).");
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        return document.RootElement.Clone();
    }

    private CopiersMtoV2CalendarEventDto MapEvent(JsonElement row)
    {
        var answers = Answers(row);
        var recordedEnd = ParseRecordedInstant(answers.FirstOrDefault(x => x.Key == "service_ended_at_utc")?.Value);
        var end = recordedEnd ?? Instant(row, _o.DeviceSignedAtUtcField) ?? Instant(row, _o.ServerFinalizedAtUtcField);
        var start = ParseRecordedInstant(answers.FirstOrDefault(x => x.Key == "service_started_at_utc")?.Value)
            ?? ParseVisitStart(answers.FirstOrDefault(x => x.Key == "service_started_at")?.Value);
        var estimated = !start.HasValue || !end.HasValue || end <= start || end - start > TimeSpan.FromDays(1);
        if (estimated)
        {
            end ??= new DateTimeOffset(ServiceDate(row).ToDateTime(new TimeOnly(12, 0)), BogotaOffset).ToUniversalTime();
            start = end.Value.AddMinutes(-30);
        }
        return new()
        {
            Id = Text(row, _o.MainIdField), ServiceReference = Text(row, _o.ServiceReferenceField),
            ClientName = Text(row, _o.ClientNameField), TechnicianId = Text(row, _o.TechnicianUserIdField),
            TechnicianName = Text(row, _o.TechnicianNameField), StartAtUtc = start!.Value, EndAtUtc = end!.Value,
            MaintenanceType = Number(row, _o.MaintenanceTypeField) == _o.MaintenanceTypePreventiveValue ? "Preventivo"
                : Number(row, _o.MaintenanceTypeField) == _o.MaintenanceTypeCorrectiveValue ? "Correctivo" : "Sin clasificar",
            DurationEstimated = estimated,
            TimingNote = estimated ? "Franja visual de 30 minutos; duración real no disponible."
                : recordedEnd.HasValue ? "Desde la entrada hasta la salida registradas en el reporte firmado (hora de Bogotá)."
                : "Desde el inicio de visita registrado (hora de Bogotá) hasta la firma del cliente.",
            WorkflowState = Number(row, _o.WorkflowStateField) == _o.ReadyToSendStateValue ? "ReadyToSend" : "Failed",
            EmailState = EmailState(Number(row, _o.EmailStateField))
        };
    }

    private static DateTimeOffset? ParseRecordedInstant(string? text)
    {
        // Only offset-aware values are canonical. Historical localized display
        // strings keep their explicit Bogotá fallback below.
        if (string.IsNullOrWhiteSpace(text)
            || !Regex.IsMatch(text, @"(?:Z|[+-]\d{2}:\d{2})$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
            || !DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var instant)
            || instant.Year is < 2000 or > 2100) return null;
        return instant.ToUniversalTime();
    }

    private static DateTimeOffset? ParseVisitStart(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        // Existing signed forms store es-CO display text, not an offset-aware timestamp.
        // Do not let a server's machine timezone silently reinterpret that historical text.
        var normalized = Regex.Replace(text.Replace('\u00a0', ' ').Replace('\u202f', ' '), "\\s+", " ").Trim();
        normalized = Regex.Replace(normalized, "a\\.?\\s*m\\.?", "AM", RegexOptions.IgnoreCase);
        normalized = Regex.Replace(normalized, "p\\.?\\s*m\\.?", "PM", RegexOptions.IgnoreCase);
        normalized = Regex.Replace(normalized, "\\bsept\\b", "sep", RegexOptions.IgnoreCase);
        if (DateTime.TryParse(normalized, Spanish, DateTimeStyles.AllowWhiteSpaces, out var local)
            && local.Year is >= 2000 and <= 2100)
            return new DateTimeOffset(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), BogotaOffset).ToUniversalTime();
        return null;
    }

    private IReadOnlyList<CopiersMtoV2CalendarAnswerDto> Answers(JsonElement row)
    {
        var text = Text(row, _o.AnswersJsonField);
        if (string.IsNullOrWhiteSpace(text)) return [];
        using var answers = JsonDocument.Parse(text);
        if (answers.RootElement.ValueKind != JsonValueKind.Array || answers.RootElement.GetArrayLength() > 100)
            throw new InvalidOperationException("El formulario guardado no contiene respuestas válidas.");
        return answers.RootElement.EnumerateArray().Select(x => new CopiersMtoV2CalendarAnswerDto(
            Text(x, "key"), Text(x, "label"), Text(x, "value"))).ToArray();
    }

    private CopiersMtoV2CalendarLocationDto? Location(JsonElement row)
    {
        var latitude = Decimal(row, _o.LatitudeField);
        var longitude = Decimal(row, _o.LongitudeField);
        if (latitude is null or < -90 or > 90 || longitude is null or < -180 or > 180) return null;
        var accuracy = Decimal(row, _o.AccuracyMetersField);
        return new(latitude.Value, longitude.Value, accuracy >= 0 ? accuracy : null,
            Instant(row, _o.LocationCapturedAtUtcField), Text(row, _o.LocationSourceField));
    }

    private bool IsEvidenceSafe(JsonElement row, JsonElement parent, string id)
    {
        if (!string.Equals(Text(row, $"_{_o.EvidenceParentLookupLogicalName}_value"), id, StringComparison.OrdinalIgnoreCase)
            || !IsHash(Text(row, _o.EvidenceKeyField)) || !IsHash(Text(row, _o.EvidenceSha256Field))
            || Number(row, _o.EvidenceSizeField) is <= 0 or > MaxFileBytes
            || string.IsNullOrEmpty(Text(row, "@odata.etag"))) return false;
        var purpose = Number(row, _o.EvidencePurposeField);
        var type = Text(row, _o.EvidenceContentTypeField).ToLowerInvariant();
        if (purpose == _o.EvidenceSignedReportPurposeValue)
            return type == "application/pdf" && Text(row, _o.EvidenceKeyField) == Text(parent, _o.SignedReportEvidenceKeyField)
                && Text(row, _o.EvidenceSha256Field) == Text(parent, _o.SignedReportSha256Field);
        if (purpose == _o.EvidenceSignaturePurposeValue)
            return type is "image/png" or "image/jpeg" && Text(row, _o.EvidenceKeyField) == Text(parent, _o.SignatureEvidenceKeyField)
                && Text(row, _o.EvidenceSha256Field) == Text(parent, _o.SignatureSha256Field);
        return purpose == _o.EvidenceCustomerAttachmentPurposeValue
            && Number(row, _o.EvidenceSecurityStateField) == _o.EvidenceSecurityScanPassedValue
            && type is "image/png" or "image/jpeg" && MatchesManifest(row, parent);
    }

    private bool MatchesManifest(JsonElement evidence, JsonElement parent)
    {
        var raw = Text(parent, _o.AttachmentManifestJsonField);
        if (string.IsNullOrWhiteSpace(raw)) return false;
        using var manifest = JsonDocument.Parse(raw);
        return manifest.RootElement.ValueKind == JsonValueKind.Array && manifest.RootElement.EnumerateArray().Any(x =>
            Text(x, "evidenceKey") == Text(evidence, _o.EvidenceKeyField)
            && Text(x, "sha256") == Text(evidence, _o.EvidenceSha256Field)
            && Text(x, "contentType") == Text(evidence, _o.EvidenceContentTypeField)
            && Text(x, "fileName") == Text(evidence, _o.EvidenceOriginalFileNameField)
            && Number(x, "size") == Number(evidence, _o.EvidenceSizeField));
    }

    private CopiersMtoV2CalendarEvidenceDto MapEvidence(JsonElement row, string id)
    {
        var key = Text(row, _o.EvidenceKeyField);
        var purpose = Number(row, _o.EvidencePurposeField);
        return new(key, SafeFileName(Text(row, _o.EvidenceOriginalFileNameField)), Text(row, _o.EvidenceContentTypeField),
            Number(row, _o.EvidenceSizeField), purpose == _o.EvidenceSignedReportPurposeValue ? "SignedReport"
                : purpose == _o.EvidenceSignaturePurposeValue ? "Signature"
                : purpose == _o.EvidenceOriginalAttachmentPurposeValue ? "OriginalAttachment" : "CustomerAttachment",
            $"/CopiersMtoV2Calendar/Evidence?id={id}&evidenceKey={key}");
    }

    private string EventFields() => Join(_o.MainIdField, _o.ServiceReferenceField, _o.ClientNameField,
        _o.TechnicianUserIdField, _o.TechnicianNameField, _o.MaintenanceTypeField, _o.WorkflowStateField,
        _o.EmailStateField, _o.ServiceDateField, _o.AnswersJsonField, _o.DeviceSignedAtUtcField, _o.ServerFinalizedAtUtcField,
        _o.SignedReportEvidenceKeyField, _o.SignedReportSha256Field);
    private string DetailFields() => Join(EventFields(), _o.ClientContactNameField, _o.ClientEmailField, _o.EquipmentSerialField,
        _o.TitleField, _o.TechnicianEmailField, _o.FormVersionField, _o.WorkPerformedField, _o.CustomerObservationsField,
        _o.ServiceAddressInternalField, _o.InternalNotesField, _o.SignerNameField, _o.SignerRoleField, _o.CustomerAcceptedField,
        _o.LatitudeField, _o.LongitudeField, _o.AccuracyMetersField, _o.LocationCapturedAtUtcField, _o.LocationSourceField,
        _o.SignatureEvidenceKeyField, _o.SignatureSha256Field, _o.AttachmentManifestJsonField);
    private string EvidenceFields() => Join(_o.EvidenceIdField, _o.EvidenceKeyField, $"_{_o.EvidenceParentLookupLogicalName}_value",
        _o.EvidencePurposeField, _o.EvidenceSequenceField, _o.EvidenceOriginalFileNameField, _o.EvidenceContentTypeField,
        _o.EvidenceSizeField, _o.EvidenceSha256Field, _o.EvidenceSecurityStateField);
    private bool IsVisible(JsonElement row) => Number(row, _o.WorkflowStateField) == _o.ReadyToSendStateValue
        || Number(row, _o.WorkflowStateField) == _o.FailedStateValue
            && IsHash(Text(row, _o.SignedReportEvidenceKeyField)) && IsHash(Text(row, _o.SignedReportSha256Field));
    private string EmailState(long state) => state == _o.EmailSentStateValue ? "Sent" : state == _o.EmailPendingStateValue ? "Pending"
        : state == _o.EmailProcessingStateValue ? "Processing" : state == _o.EmailFailedStateValue ? "Failed" : "NotReady";
    private DateOnly ServiceDate(JsonElement row) => DateOnly.TryParse(Text(row, _o.ServiceDateField).Split('T')[0],
        CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : throw new InvalidOperationException("El mantenimiento no tiene fecha válida.");
    private static string NormalizeGuid(string value) => Guid.TryParse(value, out var guid) && guid != Guid.Empty
        ? guid.ToString("D") : throw new ArgumentException("La referencia del técnico o mantenimiento no es válida.");
    private static string Join(params string[] values) => string.Join(",", values.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct());
    private static string First(params string[] values) => values.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? "Sin nombre";
    private static string Text(JsonElement row, string field) => row.TryGetProperty(field, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
    private static long Number(JsonElement row, string field) => row.TryGetProperty(field, out var value)
        && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var result) ? result : -1;
    private static double? Decimal(JsonElement row, string field) => row.TryGetProperty(field, out var value)
        && value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var result) && double.IsFinite(result) ? result : null;
    private static bool Boolean(JsonElement row, string field) => row.TryGetProperty(field, out var value) && value.ValueKind == JsonValueKind.True;
    private static DateTimeOffset? Instant(JsonElement row, string field) => DateTimeOffset.TryParse(Text(row, field),
        CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var value) ? value.ToUniversalTime() : null;
    private static bool IsHash(string? value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);
    private static string SafeFileName(string name) => Path.GetFileName(name.Replace('\\', '/')).Replace("\r", "").Replace("\n", "");
    private static bool MatchesContentType(byte[] bytes, string type) => type switch
    {
        "application/pdf" => bytes.AsSpan().StartsWith("%PDF-"u8),
        "image/png" => bytes.AsSpan().StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }),
        "image/jpeg" => bytes.Length > 3 && bytes[0] == 255 && bytes[1] == 216 && bytes[2] == 255,
        _ => false
    };
}
