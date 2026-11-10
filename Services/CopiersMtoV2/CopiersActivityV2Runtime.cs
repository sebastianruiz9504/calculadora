using System.Net;
using System.Text.Json;
using CotizadorInterno.Web.Models.CopiersMtoV2;
using Microsoft.Extensions.Options;

namespace CotizadorInterno.Web.Services.CopiersMtoV2;

public sealed class CopiersActivityV2Runtime(
    ICopiersMtoV2ApplicationDataverseClient client,
    IHttpContextAccessor accessor,
    IOptions<CopiersMaintenanceV2Options> options,
    IOptions<CopiersMaintenanceV2DataverseOptions> bindings,
    ICopiersMtoV2PdfBuilder pdf,
    ICopiersActivityV2BusinessService business,
    TimeProvider clock,
    ILoggerFactory loggers,
    ICopiersMtoV2CounterService? counters = null)
{
    internal CopiersMaintenanceV2DataverseRepository CreateRepository() => new(client, accessor,
        Options.Create(CopiersActivityV2Bindings.Create(bindings.Value)), clock, loggers.CreateLogger<CopiersMaintenanceV2DataverseRepository>());
    public ICopiersMaintenanceV2Service CreateService(ICopiersMtoV2CounterService? counterService = null)
    {
        if (!options.Value.ActivitiesEnabled)
            throw new CopiersMaintenanceV2ValidationException("activities_disabled", "Las nuevas actas todavía no están habilitadas.");
        var mapped = Options.Create(CopiersActivityV2Bindings.Create(bindings.Value));
        var repository = new CopiersMaintenanceV2DataverseRepository(client, accessor, mapped, clock,
            loggers.CreateLogger<CopiersMaintenanceV2DataverseRepository>());
        return new CopiersMaintenanceV2Service(repository, pdf, options, mapped, clock,
            loggers.CreateLogger<CopiersMaintenanceV2Service>(), counters: counterService ?? counters, business: business);
    }

    // This existence probe uses organization-wide application reads. A delegated
    // catalog missing equipment due to row permissions must not enable free entry.
    public async Task<bool> ClientHasNoEquipmentAsync(string clientId, CancellationToken ct)
    {
        var id = CopiersMaintenanceV2Validation.RequiredGuid(clientId, "client_invalid", "El cliente");
        using var response = await client.SendAsync($"/api/data/v9.2/cr07a_equipos?$select=cr07a_equipoid&$filter=_cr07a_cliente_value eq {id}&$top=1",
            HttpMethod.Get, null, null, ct);
        if (!response.IsSuccessStatusCode)
            throw new CopiersMaintenanceV2PersistenceException("No fue posible verificar si el cliente tiene equipos registrados.");
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        return document.RootElement.GetProperty("value").GetArrayLength() == 0;
    }

    public async Task VerifyDiscardableDraftAsync(CopiersSubmission item, CancellationToken ct)
    {
        var payload = item.Payload ?? throw new InvalidOperationException("Falta el contenido del envío.");
        var request = JsonSerializer.Deserialize<CopiersMaintenanceV2FinalizeMultipartRequestDto>(payload.RequestJson)!;
        // activity_snapshot_invalid is raised before PDF, counters, inventory or
        // publication. Existing receipts do not persist exception codes, so read
        // the authoritative row instead of trusting a browser flag or message.
        if (item.Result is not null || !item.NeedsReview || item.Attempts != 1
            || request.ActivityKind is not ("movement" or "toner" or "counter" or "other")
            || request.SubmissionKey != item.SubmissionKey || payload.Draft.SubmissionKey != item.SubmissionKey)
            throw new CopiersMaintenanceV2ConcurrencyException("Este envío requiere revisión interna antes de poder descartarlo.");
        var o = CopiersActivityV2Bindings.Create(bindings.Value);
        var filter = $"{o.OperationKeyField} eq '{item.SubmissionKey.Replace("'", "''", StringComparison.Ordinal)}'";
        var fields = new[] { o.TechnicianUserIdField, o.WorkflowStateField, o.EmailStateField, o.LastErrorCodeField,
            o.FinalizationFingerprintField, o.SignatureEvidenceKeyField, o.SignedReportEvidenceKeyField,
            o.SignedReportSha256Field, o.ReadyAtUtcField, o.ProviderDraftIdField, o.InternetMessageIdField };
        using var response = await client.SendAsync($"/api/data/v9.2/{o.MainEntitySetName}?$select={string.Join(',', fields)}&$filter={Uri.EscapeDataString(filter)}&$top=2",
            HttpMethod.Get, null, null, ct);
        if (!response.IsSuccessStatusCode)
            throw new CopiersMaintenanceV2PersistenceException("No fue posible verificar el envío. Conservamos el borrador; vuelve a intentarlo.");
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var rows = document.RootElement.GetProperty("value");
        if (rows.GetArrayLength() != 1)
            throw new CopiersMaintenanceV2ConcurrencyException("No se pudo identificar un único borrador rechazado. El envío requiere revisión interna.");
        var row = rows[0];
        string Text(string field) => row.TryGetProperty(field, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
        if (!string.Equals(Text(o.TechnicianUserIdField), payload.Actor.SystemUserId, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException();
        if (row.GetProperty(o.WorkflowStateField).GetInt32() != o.FailedStateValue
            || row.GetProperty(o.EmailStateField).GetInt32() != o.EmailNotReadyStateValue
            || Text(o.LastErrorCodeField) != "activity_snapshot_invalid"
            || fields.Skip(4).Any(field => !row.TryGetProperty(field, out var value)
                || value.ValueKind != JsonValueKind.Null && (value.ValueKind != JsonValueKind.String || !string.IsNullOrEmpty(value.GetString()))))
            throw new CopiersMaintenanceV2ConcurrencyException("El envío puede tener un reporte o una operación registrada. Conservamos el borrador para evitar duplicados.");
    }

    public async Task<object> StatusAsync(string recordId, string activityKind, string technicianId, CancellationToken ct)
    {
        var id = CopiersMaintenanceV2Validation.RequiredGuid(recordId, "record_invalid", "El registro");
        var o = activityKind == "maintenance" ? bindings.Value : CopiersActivityV2Bindings.Create(bindings.Value);
        if (activityKind is not ("maintenance" or "movement" or "toner" or "counter" or "other")
            || (activityKind != "maintenance" && !options.Value.ActivitiesEnabled))
            throw new CopiersMaintenanceV2ValidationException("activity_invalid", "El tipo de atención no es válido.");
        using var response = await client.SendAsync($"/api/data/v9.2/{o.MainEntitySetName}({id})?$select={o.TechnicianUserIdField},{o.WorkflowStateField},{o.EmailStateField},{o.ServiceReferenceField},{o.LastErrorCodeField}",
            HttpMethod.Get, null, null, ct);
        if (response.StatusCode == HttpStatusCode.NotFound) throw new KeyNotFoundException();
        if (!response.IsSuccessStatusCode) throw new CopiersMaintenanceV2PersistenceException("No fue posible confirmar el estado del envío.");
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var row = doc.RootElement;
        if (!string.Equals(row.GetProperty(o.TechnicianUserIdField).GetString(), technicianId, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException();
        var state = row.GetProperty(o.WorkflowStateField).GetInt32();
        var email = row.GetProperty(o.EmailStateField).GetInt32();
        var requiresReview = email != o.EmailSentStateValue
            && row.TryGetProperty(o.LastErrorCodeField, out var error)
            && error.GetString() == "EMAIL_REVIEW_REQUIRED";
        return new {
            recordId = id,
            serviceReference = row.TryGetProperty(o.ServiceReferenceField, out var reference) ? reference.GetString() : "",
            state = state == o.ReadyToSendStateValue ? 2 : state == o.FailedStateValue ? 3 : state == o.FinalizingStateValue ? 1 : 0,
            emailState = email == o.EmailSentStateValue ? 3 : email == o.EmailFailedStateValue ? 4 : email == o.EmailProcessingStateValue ? 2 : email == o.EmailPendingStateValue ? 1 : 0,
            requiresReview,
            message = requiresReview
                ? "El reporte está guardado. El proveedor no confirmó el envío del correo y requiere revisión interna para evitar duplicados. Puedes crear otro registro."
                : ""
        };
    }
}
