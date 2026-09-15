using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using CotizadorInterno.Web.Models.Dashboard;

namespace CotizadorInterno.Web.Services;

public sealed partial class DataverseService
{
    public async Task<LicenciamientoDashboardContractChangeResult> ChangeLicenciamientoDashboardContractAsync(
        LicenciamientoDashboardContractChangeRequest request, CancellationToken ct = default)
    {
        LicenciamientoDashboardContractEditor.ValidateRequest(request);
        var user = _httpContextAccessor.HttpContext?.User ?? throw new InvalidOperationException("No hay sesión activa.");
        var dashboard = await GetLicenciamientoDashboardAsync(request.Year, request.Month, ct);
        // Validate the full client/month selection before preparing any Dataverse write.
        LicenciamientoDashboardContractEditor.SelectLines(request, dashboard);
        var licensing = await ResolveLicensingMetadataAsync(user, ct);
        var billing = await ResolveRhEntityMetadataAsync(_dashboardBillingTableLogicalName, _dashboardBillingTableSetName,
            _dashboardBillingIdField, _dashboardBillingPrimaryNameField, user, ct);

        return await LicenciamientoDashboardContractEditor.ApplyAsync(request, dashboard,
            async (line, token) =>
            {
                var isCost = line.Source == "cost";
                var set = isCost ? licensing.BaseMetadata.EntitySetName : billing.EntitySetName;
                var field = isCost ? LicensingContractTypeField : _dashboardBillingContractTypeField;
                var target = LicenciamientoDashboardContractEditor.TargetValue(line.Source, request.TargetContractKey);
                var payloadValue = isCost ? ConvertLicensingPayloadValue(licensing, field, target) : target;
                var path = $"/api/data/v9.2/{set}({Guid.Parse(line.RecordId):D})";
                var json = await CallDataverseGetJsonAsync($"{path}?$select={field}", user, token);
                using var document = JsonDocument.Parse(json);
                return LicenciamientoDashboardContractEditor.Prepare(path, field, payloadValue!, target,
                    line.ExpectedContractTypeValue, document.RootElement);
            },
            async (changes, token) =>
            {
                var batch = $"batch_{Guid.NewGuid():N}";
                var changeSet = $"changeset_{Guid.NewGuid():N}";
                using var content = new StringContent(LicenciamientoDashboardContractEditor.BuildBatch(changes, batch, changeSet), Encoding.UTF8);
                content.Headers.ContentType = new MediaTypeHeaderValue("multipart/mixed");
                content.Headers.ContentType.Parameters.Add(new NameValueHeaderValue("boundary", batch));
                using var response = await CallRhDataverseResponseAsync("/api/data/v9.2/$batch", "POST", user, token, content);
                LicenciamientoDashboardContractEditor.ValidateBatchResponse(response.IsSuccessStatusCode,
                    await response.Content.ReadAsStringAsync(token), changes.Count);
            },
            async (change, token) =>
            {
                var json = await CallDataverseGetJsonAsync($"{change.Path}?$select={change.Field}", user, token);
                using var document = JsonDocument.Parse(json);
                return document.RootElement.TryGetProperty(change.Field, out var value) && int.TryParse(value.ToString(), out var current) && current == change.TargetValue;
            }, ct);
    }
}
