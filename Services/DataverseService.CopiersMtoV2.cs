using System.Net.Http.Json;
using System.Text.Json;
using CotizadorInterno.Web.Models.CopiersMtoV2;
using CotizadorInterno.Web.Models.Dashboard;
using CotizadorInterno.Web.Models.Copiers;
using System.Security.Claims;
using CotizadorInterno.Web.Services.CopiersMtoV2;

namespace CotizadorInterno.Web.Services;

public sealed partial class DataverseService
{
    internal const string CopiersContactEmailField = "dtc_personaencargadacopiers";

    public async Task<IReadOnlyList<CopiersLookupItemDto>> GetCopiersMtoV2TonerSuppliesAsync(CancellationToken ct = default)
    {
        var user = _httpContextAccessor.HttpContext?.User ?? throw new UnauthorizedAccessException();
        var rows = new List<CopiersLookupItemDto>();
        string? path = "/api/data/v9.2/cr07a_suministros?$select=cr07a_suministroid,cr07a_nombredelsuministro,cr07a_cantidad&$filter=statecode eq 0 and cr07a_categoria eq 645250000 and cr07a_cantidad gt 0&$orderby=cr07a_nombredelsuministro";
        while (!string.IsNullOrEmpty(path))
        {
            using var doc = JsonDocument.Parse(await CallDataverseGetJsonAsync(path, user, ct));
            foreach (var row in doc.RootElement.GetProperty("value").EnumerateArray())
                rows.Add(new() { Id=ReadString(row,"cr07a_suministroid"), Label=ReadString(row,"cr07a_nombredelsuministro"),
                    Quantity=row.GetProperty("cr07a_cantidad").GetInt32() });
            path = doc.RootElement.TryGetProperty("@odata.nextLink", out var next) ? next.GetString() : null;
            if (path is not null && Uri.TryCreate(path, UriKind.Absolute, out var absolute))
            {
                if (!string.Equals(absolute.Host, new Uri(_dataverseBaseUrl).Host, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("La página de suministros no pertenece al entorno configurado.");
                path = absolute.PathAndQuery;
            }
        }
        return rows;
    }

    // Signed operational rows remain immutable even through the older edit/upload endpoints.
    private async Task EnsureCopiersUnsignedBusinessRowAsync(string tableSet, string id, ClaimsPrincipal user, CancellationToken ct)
    {
        if (tableSet is not ("cr07a_entregas" or "cr07a_movimientosequiposes")) throw new InvalidOperationException("Tabla no válida.");
        var normalized = CopiersMaintenanceV2Validation.RequiredGuid(id,"record_invalid","El registro");
        using var doc = JsonDocument.Parse(await CallDataverseGetJsonAsync($"/api/data/v9.2/{tableSet}({normalized})?$select=dtc_signedreportkey", user, ct));
        if (!string.IsNullOrWhiteSpace(ReadString(doc.RootElement,"dtc_signedreportkey")))
            throw new InvalidOperationException("Esta operación tiene un acta firmada V2 y no puede editarse ni reemplazar su comprobante desde el formulario anterior.");
    }

    // The capture catalog needs equipment assignments, not the historical dashboard.
    // Reuse its equipment reader so serial, lookup and stock semantics stay identical.
    public async Task<IReadOnlyList<CopiersEquipmentRowDto>> GetCopiersMtoV2EquipmentAsync(CancellationToken ct = default)
    {
        var user = _httpContextAccessor.HttpContext?.User
            ?? throw new InvalidOperationException("No hay un usuario autenticado.");
        var metadata = await ResolveRhEntityMetadataAsync(
            DashboardEquipmentTableLogicalName,
            DashboardEquipmentTableSetName,
            DashboardEquipmentIdField,
            DashboardEquipmentPrimaryNameField,
            user,
            ct);
        var equipment = await GetEquipmentRecordsAsync(metadata, user, ct);
        return BuildEquipmentRows(equipment, []);
    }

    // Use the signed-in technician's Dataverse permissions, not the PDF worker identity.
    public async Task<IReadOnlyList<CopiersMtoV2ClientOptionDto>> GetCopiersMtoV2ClientsAsync(CancellationToken ct = default)
    {
        var user = _httpContextAccessor.HttpContext?.User
            ?? throw new InvalidOperationException("No hay un usuario autenticado.");
        var rows = new List<CopiersMtoV2ClientOptionDto>();
        var path = $"/api/data/v9.2/cr07a_clientes?$select=cr07a_clienteid,cr07a_nombre,cr07a_nombrepersonaacargo,{CopiersContactEmailField}&$filter=statecode eq 0&$orderby=cr07a_nombre";
        while (!string.IsNullOrEmpty(path))
        {
            using var doc = JsonDocument.Parse(await CallDataverseGetJsonAsync(path, user, ct));
            foreach (var row in doc.RootElement.GetProperty("value").EnumerateArray())
                rows.Add(MapCopiersMtoV2Client(row));
            path = doc.RootElement.TryGetProperty("@odata.nextLink", out var next) ? next.GetString() : null;
            if (!string.IsNullOrEmpty(path) && Uri.TryCreate(path, UriKind.Absolute, out var absolute))
            {
                if (!string.Equals(absolute.Host, new Uri(_dataverseBaseUrl).Host, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Dataverse devolvió una página fuera del entorno configurado.");
                path = absolute.PathAndQuery;
            }
        }
        return rows.Where(row => !string.IsNullOrWhiteSpace(row.Name)).ToArray();
    }

    public async Task<CopiersMtoV2ClientOptionDto> SaveCopiersMtoV2ClientEmailAsync(string clientId, string email, CancellationToken ct = default)
    {
        var id = CopiersMaintenanceV2Validation.RequiredGuid(clientId, "client_invalid", "El cliente");
        CopiersMaintenanceV2Validation.ValidateCustomerEmail(email);
        var user = _httpContextAccessor.HttpContext?.User
            ?? throw new InvalidOperationException("No hay un usuario autenticado.");
        var path = $"/api/data/v9.2/cr07a_clientes({id})";
        using var current = JsonDocument.Parse(await CallDataverseGetJsonAsync(path + "?$select=cr07a_clienteid,statecode", user, ct));
        if (current.RootElement.GetProperty("statecode").GetInt32() != 0)
            throw new CopiersMaintenanceV2ValidationException("client_inactive", "El cliente está inactivo.");
        var version = current.RootElement.GetProperty("@odata.etag").GetString();
        using var content = JsonContent.Create(new Dictionary<string, string> { [CopiersContactEmailField] = email.Trim() });
        using var response = await _downstreamApi.CallApiForUserAsync("Dataverse", options =>
        {
            options.RelativePath = path;
            options.HttpMethod = "PATCH";
            options.CustomizeHttpRequestMessage = request => request.Headers.TryAddWithoutValidation("If-Match", version);
        }, content: content, user: user, cancellationToken: ct);
        if (response.StatusCode == System.Net.HttpStatusCode.PreconditionFailed)
            throw new CopiersMaintenanceV2ConcurrencyException("El cliente cambió mientras guardabas. Recarga e inténtalo nuevamente.");
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Dataverse rechazó el cambio de correo (HTTP {(int)response.StatusCode}).");
        using var saved = JsonDocument.Parse(await CallDataverseGetJsonAsync(path + $"?$select=cr07a_clienteid,cr07a_nombre,cr07a_nombrepersonaacargo,{CopiersContactEmailField}", user, ct));
        var result = MapCopiersMtoV2Client(saved.RootElement);
        if (!string.Equals(result.Email, email.Trim(), StringComparison.Ordinal))
            throw new CopiersMaintenanceV2ConcurrencyException("El correo cambió durante la verificación. Recarga el cliente.");
        return result;
    }

    private static CopiersMtoV2ClientOptionDto MapCopiersMtoV2Client(JsonElement row) => new()
    {
        Id = ReadString(row, "cr07a_clienteid"),
        Name = ReadString(row, "cr07a_nombre"),
        ContactName = ReadString(row, "cr07a_nombrepersonaacargo"),
        Email = ReadString(row, CopiersContactEmailField)
    };
}
