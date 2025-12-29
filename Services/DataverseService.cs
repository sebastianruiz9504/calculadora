using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Identity.Abstractions;
using Microsoft.Identity.Web;
using CotizadorInterno.Web.Models;

namespace CotizadorInterno.Web.Services;

public sealed class DataverseService : IDataverseService
{
    private readonly IDownstreamApi _downstreamApi;
    private readonly IHttpContextAccessor _httpContextAccessor;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    public DataverseService(IDownstreamApi downstreamApi, IHttpContextAccessor httpContextAccessor)
    {
        _downstreamApi = downstreamApi;
        _httpContextAccessor = httpContextAccessor;
    }

    public async Task<UserSegment> GetCurrentUserSegmentAsync(CancellationToken ct = default)
    {
        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No HttpContext available.");

        // OID Entra del usuario
        var objectId = httpContext.User.GetObjectId();
        if (string.IsNullOrWhiteSpace(objectId))
            return UserSegment.Unknown;

        var filter = $"azureactivedirectoryobjectid eq {Guid.Parse(objectId):D}";
        var relativeUrl = $"/api/data/v9.2/systemusers?$select=cr07a_segmentocomercial&$filter={Uri.EscapeDataString(filter)}&$top=1";

        var json = await CallDataverseGetJsonAsync(relativeUrl, httpContext.User, ct);

        using var doc = JsonDocument.Parse(json);
        var value = doc.RootElement.GetProperty("value");

        if (value.GetArrayLength() == 0)
            return UserSegment.Unknown;

        var item = value[0];

        if (!item.TryGetProperty("cr07a_segmentocomercial", out var segProp))
            return UserSegment.Unknown;

        var segRaw = segProp.ValueKind switch
        {
            JsonValueKind.Number => segProp.GetInt32().ToString(),
            JsonValueKind.String => segProp.GetString(),
            _ => null
        };

        if (string.IsNullOrWhiteSpace(segRaw))
            return UserSegment.Unknown;

        // Si tu campo guarda texto
        if (segRaw.Equals("Corporate", StringComparison.OrdinalIgnoreCase))
            return UserSegment.Corporate;

        if (segRaw.Equals("SMB", StringComparison.OrdinalIgnoreCase))
            return UserSegment.SMB;

        // Si es OptionSet numérico (ajustaremos mapping real cuando veamos el valor)
        if (int.TryParse(segRaw, out var opt))
        {
            return opt switch
            {
                1 => UserSegment.SMB,
                2 => UserSegment.Corporate,
                _ => UserSegment.Unknown
            };
        }

        return UserSegment.Unknown;
    }

    public async Task<IReadOnlyList<ProductLookupItem>> SearchProductsAsync(string query, int top = 12, CancellationToken ct = default)
    {
        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No HttpContext available.");

        query = (query ?? "").Trim();
        if (query.Length < 2)
            return Array.Empty<ProductLookupItem>();

        var safeQuery = query.Replace("'", "''");
        var select = "cr07a_priceableitemdescription,cr07a_purchaseprice,cr07a_suggestedretailprice,cr07a_acelerador,cr07a_precioscloudid";
        var filter = $"contains(cr07a_priceableitemdescription,'{safeQuery}')";
        var relativeUrl = $"/api/data/v9.2/cr07a_preciosclouds?$select={select}&$filter={Uri.EscapeDataString(filter)}&$top={top}";

        var json = await CallDataverseGetJsonAsync(relativeUrl, httpContext.User, ct);

        using var doc = JsonDocument.Parse(json);
        var arr = doc.RootElement.GetProperty("value");

        var list = new List<ProductLookupItem>(Math.Min(arr.GetArrayLength(), top));
        foreach (var item in arr.EnumerateArray())
        {
            list.Add(new ProductLookupItem
            {
                Id = item.TryGetProperty("cr07a_precioscloudid", out var idProp) ? (idProp.GetString() ?? "") : "",
                Description = item.TryGetProperty("cr07a_priceableitemdescription", out var d) ? (d.GetString() ?? "") : "",
                PurchasePrice = ReadDecimal(item, "cr07a_purchaseprice"),
                SuggestedRetailPrice = ReadDecimal(item, "cr07a_suggestedretailprice"),
                Acelerador = ReadDecimal(item, "cr07a_acelerador")
            });
        }

        return list;
    }

    private async Task<string> CallDataverseGetJsonAsync(string relativeUrl, System.Security.Claims.ClaimsPrincipal user, CancellationToken ct)
    {
        // En tu combinación de paquetes, esto devuelve HttpResponseMessage.
        var result = await _downstreamApi.CallApiForUserAsync(
            serviceName: "Dataverse",
            options =>
            {
                options.RelativePath = relativeUrl;
                options.HttpMethod = "GET";
            },
            user: user,
            cancellationToken: ct);

        if (result is not System.Net.Http.HttpResponseMessage resp)
            throw new InvalidOperationException($"Unexpected downstream response type: {result?.GetType().FullName ?? "null"}");

        var body = await resp.Content.ReadAsStringAsync(ct);

        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"Dataverse error {(int)resp.StatusCode} {resp.ReasonPhrase}. Body: {body}");

        return body;
    }

    private static decimal? ReadDecimal(JsonElement el, string name)
    {
        if (!el.TryGetProperty(name, out var p))
            return null;

        return p.ValueKind switch
        {
            JsonValueKind.Number => p.TryGetDecimal(out var d) ? d : null,
            JsonValueKind.String => decimal.TryParse(p.GetString(), out var d) ? d : null,
            _ => null
        };
    }
}
