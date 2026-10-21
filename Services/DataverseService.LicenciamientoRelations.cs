using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CotizadorInterno.Web.Models.Licenciamiento;

namespace CotizadorInterno.Web.Services;

public sealed partial class DataverseService
{
    // Record-scoped relations reuse the existing mapping table. These reserved keys
    // never participate in account/name fallback and take precedence over account rules.
    internal static string LicensingRecordRelationKey(string recordId) => $"consumption:{Guid.Parse(recordId):D}";

    internal static Guid LicensingRelationId(string key) =>
        new(SHA256.HashData(Encoding.UTF8.GetBytes("licensing-client-relation:" + key.ToLowerInvariant())).AsSpan(0, 16));

    public async Task<IReadOnlyList<LicenciamientoCruceClientOptionDto>> SearchLicenciamientoCruceClientsAsync(string query, CancellationToken ct = default)
    {
        var user = _httpContextAccessor.HttpContext?.User ?? throw new InvalidOperationException("No HttpContext available.");
        query = (query ?? "").Trim();
        if (query.Length < 2) return Array.Empty<LicenciamientoCruceClientOptionDto>();
        var filter = $"contains(cr07a_nombre,'{EscapeOdataLiteral(query)}')";
        var digits = new string(query.Where(char.IsDigit).ToArray());
        if (digits.Length > 0 && query.All(c => char.IsDigit(c) || c is '.' or '-' or ' ')
            && long.TryParse(digits, out var nit)) filter += $" or cr07a_nit eq {nit}";
        var items = await GetDataverseEntitiesAsync($"/api/data/v9.2/cr07a_clientes?$select=cr07a_clienteid,cr07a_nombre,cr07a_nit&$filter={Uri.EscapeDataString(filter)}&$orderby=cr07a_nombre asc&$top=25", user, ct);
        return items.Select(item => new LicenciamientoCruceClientOptionDto
        {
            ClientId = ReadString(item, "cr07a_clienteid"), ClientName = ReadString(item, "cr07a_nombre"), Nit = ReadString(item, "cr07a_nit")
        }).ToList();
    }

    public async Task<LicenciamientoCruceSaveAccountMappingResultDto> SaveLicenciamientoCruceClientRelationAsync(
        LicenciamientoCruceClientRelationRequestDto request, CancellationToken ct = default)
    {
        var recordId = NormalizeGuid(request.CostRecordId, nameof(request.CostRecordId));
        var clientId = NormalizeGuid(request.ClientId, nameof(request.ClientId));
        if (request.Scope is not ("record" or "account")) throw new InvalidOperationException("Selecciona el alcance de la relacion.");
        var user = _httpContextAccessor.HttpContext?.User ?? throw new InvalidOperationException("No HttpContext available.");
        var metadata = await ResolveLicensingMetadataAsync(user, ct);
        using var costDoc = JsonDocument.Parse(await CallDataverseGetJsonAsync(
            $"/api/data/v9.2/{metadata.BaseMetadata.EntitySetName}({recordId})?$select={BuildLicensingSelectClause(metadata)}", user, ct, AddFormattedValueHeaders));
        var cost = costDoc.RootElement;
        if (string.IsNullOrWhiteSpace(request.ExpectedCostVersion) || ReadString(cost, "@odata.etag") != request.ExpectedCostVersion)
            throw new InvalidOperationException("El consumo cambio desde que abriste la ficha. Actualiza y revisa la relacion.");
        var original = BuildLicensingRecordDto(metadata, cost) ?? throw new InvalidOperationException("Consumo no disponible.");
        var accountId = NormalizeOptionalGuid(original.CompanyAccountId);
        if (request.Scope == "account" && string.IsNullOrWhiteSpace(accountId))
            throw new InvalidOperationException("Este consumo no tiene cuenta Intcomex identificada. Usa Solo este consumo.");
        using var clientDoc = JsonDocument.Parse(await CallDataverseGetJsonAsync(
            $"/api/data/v9.2/cr07a_clientes({clientId})?$select=cr07a_clienteid,cr07a_nombre", user, ct));
        var clientName = ReadString(clientDoc.RootElement, "cr07a_nombre");
        var mapMetadata = await TryResolveLicenciamientoCruceAccountMapMetadataAsync(user, ct)
            ?? throw new InvalidOperationException("La tabla de relaciones no esta disponible.");
        var key = request.Scope == "record" ? LicensingRecordRelationKey(recordId) : accountId;
        var field = request.Scope == "record" ? LicenciamientoCruceAccountMapSourceAccountNameField : LicenciamientoCruceAccountMapSourceAccountIdField;
        var existing = await GetDataverseEntitiesAsync($"/api/data/v9.2/{mapMetadata.EntitySetName}?$select={BuildLicenciamientoCruceMappingSelect(mapMetadata)}&$filter={Uri.EscapeDataString($"{field} eq '{EscapeOdataLiteral(key)}'")}&$top=2", user, ct);
        if (existing.Count > 1) throw new InvalidOperationException("Hay mas de una relacion para esta cuenta. Revisa las equivalencias antes de guardar.");
        var existingId = existing.Count == 1 ? ReadString(existing[0], mapMetadata.PrimaryIdField) : "";
        var existingVersion = existing.Count == 1 ? ReadString(existing[0], "@odata.etag") : "";
        if (existingId != (request.ExpectedMappingId ?? "") || existingVersion != (request.ExpectedMappingVersion ?? ""))
            throw new InvalidOperationException("La relacion o su alcance cambio. Vuelve a abrir la ficha antes de guardar.");
        var id = string.IsNullOrEmpty(existingId) ? LicensingRelationId(key).ToString("D") : existingId;
        var payload = new Dictionary<string, object?>
        {
            [LicenciamientoCruceAccountMapPrimaryNameField] = BuildLicenciamientoCruceMappingName(original.NombreCliente, clientName),
            [LicenciamientoCruceAccountMapSourceAccountIdField] = request.Scope == "account" ? accountId : "",
            [LicenciamientoCruceAccountMapSourceAccountNameField] = request.Scope == "record" ? key : "account:" + accountId,
            // New rules are keyed only by a real account or a specific consumption; never by a fuzzy name.
            [LicenciamientoCruceAccountMapSourceClientNameField] = "",
            [LicenciamientoCruceAccountMapTargetAccountIdField] = "",
            [LicenciamientoCruceAccountMapTargetAccountNameField] = "",
            [LicenciamientoCruceAccountMapTargetClientIdField] = clientId,
            [LicenciamientoCruceAccountMapTargetClientNameField] = clientName,
            [LicenciamientoCruceAccountMapActiveField] = true,
            [LicenciamientoCruceAccountMapNotesField] = request.Scope == "record" ? "Relacion solo para este consumo." : "Relacion para consumos actuales y futuros de esta cuenta; respeta excepciones por consumo."
        };
        var path = $"/api/data/v9.2/{mapMetadata.EntitySetName}({id})";
        using var content = new StringContent(JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json");
        using var response = await _downstreamApi.CallApiForUserAsync("Dataverse", options =>
        {
            options.RelativePath = path; options.HttpMethod = "PATCH";
            options.CustomizeHttpRequestMessage = message => message.Headers.TryAddWithoutValidation(
                existing.Count == 0 ? "If-None-Match" : "If-Match", existing.Count == 0 ? "*" : existingVersion);
        }, user: user, content: content, cancellationToken: ct);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException("No se guardo la relacion. Puede haber sido modificada por otro usuario; actualiza la ficha.");
        using var readback = JsonDocument.Parse(await CallDataverseGetJsonAsync(path + $"?$select={BuildLicenciamientoCruceMappingSelect(mapMetadata)}", user, ct));
        var saved = ParseLicenciamientoCruceAccountMapping(mapMetadata, readback.RootElement);
        if (saved is null || !saved.Active || saved.TargetClientId != clientId
            || (request.Scope == "record" ? saved.SourceAccountName != key : saved.SourceAccountId != key))
            throw new InvalidOperationException("No fue posible confirmar la relacion guardada. Actualiza antes de volver a intentar.");
        return new() { MappingId = id, TargetClientId = clientId, TargetClientName = clientName,
            Message = request.Scope == "record" ? $"Este consumo quedo relacionado con {clientName}." : $"La cuenta Intcomex quedo relacionada con {clientName} para todos los periodos." };
    }
}
