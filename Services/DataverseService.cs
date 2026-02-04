using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Identity.Abstractions;
using Microsoft.Identity.Web;
using CotizadorInterno.Web.Models;
using CotizadorInterno.Web.Models.Calculator;

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
    private const string DefaultScenariosTableSetName = "cr07a_negocioscomercialeses";
    private const string DefaultScenariosTableName = "cr07a_negocioscomerciales";
    private readonly string _scenariosTableSetName;
    private readonly string _scenariosTableName;

    public DataverseService(IDownstreamApi downstreamApi, IHttpContextAccessor httpContextAccessor, IConfiguration configuration)
    {
        _downstreamApi = downstreamApi;
        _httpContextAccessor = httpContextAccessor;
        _scenariosTableSetName = configuration["Dataverse:ScenariosTableSetName"]
            ?? DefaultScenariosTableSetName;
        _scenariosTableName = configuration["Dataverse:ScenariosTableName"]
            ?? DefaultScenariosTableName;
    }

    public async Task<UserSegment> GetCurrentUserSegmentAsync(CancellationToken ct = default)
    {
        var info = await GetCurrentUserAsync(ct);
        return info?.Segment ?? UserSegment.Unknown;
    }

    public async Task<IReadOnlyList<ScenarioStoredDto>> GetScenariosForUserAsync(CancellationToken ct = default)
    {
        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No HttpContext available.");

        var currentUser = await GetCurrentUserAsync(ct);
        if (currentUser is null || string.IsNullOrWhiteSpace(currentUser.SystemUserId))
            return Array.Empty<ScenarioStoredDto>();

        var select = string.Join(",", new[]
        {
            "cr07a_scenarioid",
            "cr07a_scenarioname",
            "cr07a_dealtype",
            "cr07a_requiresproration",
            "cr07a_startdate",
            "cr07a_enddate",
            "cr07a_linesjson",
            "cr07a_lastresultjson"
        });

        var filter = $"cr07a_systemuserid eq '{EscapeOdataLiteral(currentUser.SystemUserId)}'";
        var relativeUrl = $"/api/data/v9.2/{_scenariosTableSetName}?$select={select}&$filter={Uri.EscapeDataString(filter)}";

        var json = await CallDataverseGetJsonAsync(relativeUrl, httpContext.User, ct);

        using var doc = JsonDocument.Parse(json);
        var arr = doc.RootElement.GetProperty("value");

        var list = new List<ScenarioStoredDto>(arr.GetArrayLength());
        foreach (var item in arr.EnumerateArray())
        {
            var linesJson = item.TryGetProperty("cr07a_linesjson", out var linesProp)
                ? linesProp.GetString()
                : null;
            var resultJson = item.TryGetProperty("cr07a_lastresultjson", out var resultProp)
                ? resultProp.GetString()
                : null;

            list.Add(new ScenarioStoredDto
            {
                ScenarioId = item.TryGetProperty("cr07a_scenarioid", out var idProp) ? (idProp.GetString() ?? "") : "",
                ScenarioName = item.TryGetProperty("cr07a_scenarioname", out var nameProp) ? (nameProp.GetString() ?? "") : "",
                DealType = ReadInt(item, "cr07a_dealtype"),
                RequiresProration = ReadBool(item, "cr07a_requiresproration"),
                StartDate = item.TryGetProperty("cr07a_startdate", out var startProp) ? startProp.GetString() : null,
                EndDate = item.TryGetProperty("cr07a_enddate", out var endProp) ? endProp.GetString() : null,
                Lines = DeserializeJsonOrDefault<List<ScenarioLineInput>>(linesJson) ?? new List<ScenarioLineInput>(),
                LastResult = DeserializeJsonOrDefault<ScenarioResultSnapshot>(resultJson)
            });
        }

        return list;
    }

    public async Task UpsertScenarioAsync(ScenarioSaveRequest request, CancellationToken ct = default)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));

        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No HttpContext available.");

        var currentUser = await GetCurrentUserAsync(ct);
        if (currentUser is null || string.IsNullOrWhiteSpace(currentUser.SystemUserId))
            throw new InvalidOperationException("Usuario actual no disponible.");

        var recordId = await FindScenarioRecordIdAsync(request.ScenarioId, currentUser.SystemUserId, httpContext.User, ct);

        var payload = new Dictionary<string, object?>
        {
            ["cr07a_name"] = string.IsNullOrWhiteSpace(request.ScenarioName) ? "Escenario" : request.ScenarioName,
            ["cr07a_scenarioid"] = request.ScenarioId,
            ["cr07a_scenarioname"] = request.ScenarioName,
            ["cr07a_dealtype"] = request.DealType,
            ["cr07a_requiresproration"] = request.RequiresProration,
            ["cr07a_startdate"] = request.StartDate?.ToString("yyyy-MM-dd"),
            ["cr07a_enddate"] = request.EndDate?.ToString("yyyy-MM-dd"),
            ["cr07a_linesjson"] = JsonSerializer.Serialize(request.Lines ?? new List<ScenarioLineInput>()),
            ["cr07a_lastresultjson"] = request.LastResult is null ? null : JsonSerializer.Serialize(request.LastResult),
            ["cr07a_systemuserid"] = currentUser.SystemUserId,
            ["cr07a_displayname"] = currentUser.DisplayName,
            ["cr07a_email"] = currentUser.Email
        };

        if (string.IsNullOrWhiteSpace(recordId))
        {
            var relativeUrl = $"/api/data/v9.2/{_scenariosTableSetName}";
            await CallDataverseSendAsync(relativeUrl, "POST", payload, httpContext.User, ct);
            return;
        }

        var updateUrl = $"/api/data/v9.2/{_scenariosTableSetName}({recordId})";
        await CallDataverseSendAsync(updateUrl, "PATCH", payload, httpContext.User, ct);
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

    public async Task<IReadOnlyList<ClientLookupItem>> SearchClientsAsync(string query, int top = 12, CancellationToken ct = default)
    {
        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No HttpContext available.");

        query = (query ?? "").Trim();
        if (query.Length < 2)
            return Array.Empty<ClientLookupItem>();

        var safeQuery = query.Replace("'", "''");
        var select = "cr07a_clienteid,cr07a_nombre";
        var filter = $"contains(cr07a_nombre,'{safeQuery}')";
        var relativeUrl = $"/api/data/v9.2/cr07a_clientes?$select={select}&$filter={Uri.EscapeDataString(filter)}&$top={top}";

        var json = await CallDataverseGetJsonAsync(relativeUrl, httpContext.User, ct);

        using var doc = JsonDocument.Parse(json);
        var arr = doc.RootElement.GetProperty("value");

        var list = new List<ClientLookupItem>(Math.Min(arr.GetArrayLength(), top));
        foreach (var item in arr.EnumerateArray())
        {
            list.Add(new ClientLookupItem
            {
                Id = item.TryGetProperty("cr07a_clienteid", out var idProp) ? (idProp.GetString() ?? "") : "",
                Name = item.TryGetProperty("cr07a_nombre", out var nameProp) ? (nameProp.GetString() ?? "") : ""
            });
        }

        return list;
    }
    public async Task<CurrentUserInfo?> GetCurrentUserAsync(CancellationToken ct = default)
    {
        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No HttpContext available.");

       
 var userRecord = await GetCurrentUserRecordAsync(httpContext.User, ct);
        if (userRecord is null)
            return null;

        return new CurrentUserInfo
        {
            SystemUserId = userRecord.Value.TryGetProperty("systemuserid", out var idProp) ? (idProp.GetString() ?? "") : "",
            DisplayName = userRecord.Value.TryGetProperty("fullname", out var nameProp) ? (nameProp.GetString() ?? "") : "",
            Email = userRecord.Value.TryGetProperty("internalemailaddress", out var emailProp) ? (emailProp.GetString() ?? "") : "",
            Segment = ParseSegment(userRecord.Value)
        };
    }

    private async Task<JsonElement?> GetCurrentUserRecordAsync(System.Security.Claims.ClaimsPrincipal user, CancellationToken ct)
    {
        var objectId = user.GetObjectId();
        if (string.IsNullOrWhiteSpace(objectId))
            return null;

        var select = "systemuserid,fullname,internalemailaddress,cr07a_segmentocomercial";
        var filter = $"azureactivedirectoryobjectid eq {Guid.Parse(objectId):D}";
        var relativeUrl = $"/api/data/v9.2/systemusers?$select={select}&$filter={Uri.EscapeDataString(filter)}&$top=1";

        var json = await CallDataverseGetJsonAsync(relativeUrl, user, ct);

        using var doc = JsonDocument.Parse(json);
        var value = doc.RootElement.GetProperty("value");

        if (value.GetArrayLength() == 0)
            return null;

        return value[0].Clone();
    }

    private static UserSegment ParseSegment(JsonElement item)
    {
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

        if (segRaw.Equals("Corporate", StringComparison.OrdinalIgnoreCase))
            return UserSegment.Corporate;

        if (segRaw.Equals("SMB", StringComparison.OrdinalIgnoreCase))
            return UserSegment.SMB;

  if (segRaw.Equals("Super", StringComparison.OrdinalIgnoreCase))
            return UserSegment.Super;

        if (int.TryParse(segRaw, out var opt))
        {
            return opt switch
            {
                1 => UserSegment.SMB,
                2 => UserSegment.Corporate,
                                3 => UserSegment.Super,
                _ => UserSegment.Unknown
            };
        }

        return UserSegment.Unknown;
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
    private async Task<string> CallDataverseSendAsync(string relativeUrl, string method, object payload, System.Security.Claims.ClaimsPrincipal user, CancellationToken ct)
    {
        var jsonPayload = JsonSerializer.Serialize(payload, JsonOptions);
        using var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

        var result = await _downstreamApi.CallApiForUserAsync(
            serviceName: "Dataverse",
            options =>
            {
                options.RelativePath = relativeUrl;
                options.HttpMethod = method;
            },
            user: user,
            content: content,
            cancellationToken: ct);

        if (result is not System.Net.Http.HttpResponseMessage resp)
            throw new InvalidOperationException($"Unexpected downstream response type: {result?.GetType().FullName ?? "null"}");

        var body = await resp.Content.ReadAsStringAsync(ct);

        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"Dataverse error {(int)resp.StatusCode} {resp.ReasonPhrase}. Body: {body}");

        return body;
    }

    private async Task<string?> FindScenarioRecordIdAsync(string scenarioId, string systemUserId, System.Security.Claims.ClaimsPrincipal user, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(scenarioId) || string.IsNullOrWhiteSpace(systemUserId))
            return null;

        var select = $"{_scenariosTableName}id";
        var filter = $"cr07a_scenarioid eq '{EscapeOdataLiteral(scenarioId)}' and cr07a_systemuserid eq '{EscapeOdataLiteral(systemUserId)}'";
        var relativeUrl = $"/api/data/v9.2/{_scenariosTableSetName}?$select={select}&$filter={Uri.EscapeDataString(filter)}&$top=1";

        var json = await CallDataverseGetJsonAsync(relativeUrl, user, ct);
        using var doc = JsonDocument.Parse(json);
        var value = doc.RootElement.GetProperty("value");
        if (value.GetArrayLength() == 0)
            return null;

        var record = value[0];
        var idPropName = $"{_scenariosTableName}id";
        return record.TryGetProperty(idPropName, out var idProp) ? idProp.GetString() : null;
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
    private static int ReadInt(JsonElement el, string name)
    {
        if (!el.TryGetProperty(name, out var p))
            return 0;

        return p.ValueKind switch
        {
            JsonValueKind.Number => p.TryGetInt32(out var v) ? v : 0,
            JsonValueKind.String => int.TryParse(p.GetString(), out var v) ? v : 0,
            _ => 0
        };
    }

    private static bool ReadBool(JsonElement el, string name)
    {
        if (!el.TryGetProperty(name, out var p))
            return false;

        return p.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String => bool.TryParse(p.GetString(), out var v) && v,
            JsonValueKind.Number => p.TryGetInt32(out var v) && v != 0,
            _ => false
        };
    }

    private static T? DeserializeJsonOrDefault<T>(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return default;

        try
        {
            return JsonSerializer.Deserialize<T>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return default;
        }
    }

    private static string EscapeOdataLiteral(string value)
    {
        return (value ?? string.Empty).Replace("'", "''");
    }
}
