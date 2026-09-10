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
    ILoggerFactory loggers)
{
    public ICopiersMaintenanceV2Service CreateService()
    {
        if (!options.Value.ActivitiesEnabled)
            throw new CopiersMaintenanceV2ValidationException("activities_disabled", "Las nuevas actas todavía no están habilitadas.");
        var mapped = Options.Create(CopiersActivityV2Bindings.Create(bindings.Value));
        var repository = new CopiersMaintenanceV2DataverseRepository(client, accessor, mapped, clock,
            loggers.CreateLogger<CopiersMaintenanceV2DataverseRepository>());
        return new CopiersMaintenanceV2Service(repository, pdf, options, mapped, clock,
            loggers.CreateLogger<CopiersMaintenanceV2Service>(), business: business);
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

    public async Task<object> StatusAsync(string recordId, string activityKind, string technicianId, CancellationToken ct)
    {
        var id = CopiersMaintenanceV2Validation.RequiredGuid(recordId, "record_invalid", "El registro");
        var o = activityKind == "maintenance" ? bindings.Value : CopiersActivityV2Bindings.Create(bindings.Value);
        if (activityKind is not ("maintenance" or "movement" or "toner")
            || (activityKind != "maintenance" && !options.Value.ActivitiesEnabled))
            throw new CopiersMaintenanceV2ValidationException("activity_invalid", "El tipo de atención no es válido.");
        using var response = await client.SendAsync($"/api/data/v9.2/{o.MainEntitySetName}({id})?$select={o.TechnicianUserIdField},{o.WorkflowStateField},{o.EmailStateField},{o.ServiceReferenceField}",
            HttpMethod.Get, null, null, ct);
        if (response.StatusCode == HttpStatusCode.NotFound) throw new KeyNotFoundException();
        if (!response.IsSuccessStatusCode) throw new CopiersMaintenanceV2PersistenceException("No fue posible confirmar el estado del envío.");
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var row = doc.RootElement;
        if (!string.Equals(row.GetProperty(o.TechnicianUserIdField).GetString(), technicianId, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException();
        var state = row.GetProperty(o.WorkflowStateField).GetInt32();
        var email = row.GetProperty(o.EmailStateField).GetInt32();
        return new {
            recordId = id,
            serviceReference = row.TryGetProperty(o.ServiceReferenceField, out var reference) ? reference.GetString() : "",
            state = state == o.ReadyToSendStateValue ? 2 : state == o.FailedStateValue ? 3 : state == o.FinalizingStateValue ? 1 : 0,
            emailState = email == o.EmailSentStateValue ? 3 : email == o.EmailFailedStateValue ? 4 : email == o.EmailProcessingStateValue ? 2 : email == o.EmailPendingStateValue ? 1 : 0
        };
    }
}
