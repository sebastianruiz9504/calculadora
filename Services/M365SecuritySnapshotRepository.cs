using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CotizadorInterno.Web.Models.M365;
using Microsoft.Extensions.Options;
using Microsoft.Identity.Client;

namespace CotizadorInterno.Web.Services;

public sealed class M365SecuritySnapshotRepository : IM365SecuritySnapshotRepository
{
    private const string ClientsEntitySetName = "cr07a_clientes";
    private const string FormattedValueAnnotationSuffix = "@OData.Community.Display.V1.FormattedValue";
    private readonly M365Options _options;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<M365SecuritySnapshotRepository> _logger;
    private readonly string _dataverseBaseUrl;
    private readonly string _azureAuthorityInstance;
    private readonly string _dataverseTenantId;
    private readonly string _dataverseClientId;
    private readonly string _dataverseClientSecret;
    private readonly string _dataverseCredentialSource;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    public M365SecuritySnapshotRepository(
        IOptions<M365Options> options,
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<M365SecuritySnapshotRepository> logger)
    {
        _options = options.Value;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _dataverseBaseUrl = (configuration["Dataverse:BaseUrl"] ?? "").TrimEnd('/');
        _azureAuthorityInstance = configuration["AzureAd:Instance"] ?? "https://login.microsoftonline.com/";
        _dataverseTenantId = FirstNonEmpty(configuration["Dataverse:TenantId"], configuration["AzureAd:TenantId"]);

        var credential = ResolveDataverseAppCredential(configuration);
        _dataverseClientId = credential.ClientId;
        _dataverseClientSecret = credential.ClientSecret;
        _dataverseCredentialSource = credential.Source;
    }

    public async Task<M365TenantConnectionRecord?> FindConnectionForSnapshotAsync(
        string clienteId,
        string tenantIdOrHint,
        CancellationToken ct = default)
    {
        var table = _options.Dataverse;
        var normalizedClienteId = NormalizeOptionalGuid(clienteId);
        var normalizedTenant = tenantIdOrHint?.Trim() ?? "";

        if (!string.IsNullOrWhiteSpace(normalizedClienteId)
            && !string.IsNullOrWhiteSpace(normalizedTenant))
        {
            var match = await FindConnectionByFilterAsync(
                $"{table.InternalClientIdField} eq '{EscapeOdataLiteral(normalizedClienteId)}' and ({BuildTenantIdOrHintFilter(table, normalizedTenant)})",
                ct);
            if (match is not null)
                return match;
        }

        if (!string.IsNullOrWhiteSpace(normalizedClienteId))
        {
            var match = await FindConnectionByFilterAsync(
                $"{table.InternalClientIdField} eq '{EscapeOdataLiteral(normalizedClienteId)}'",
                ct);
            if (match is not null)
                return match;
        }

        if (!string.IsNullOrWhiteSpace(normalizedTenant))
        {
            return await FindConnectionByFilterAsync(BuildTenantIdOrHintFilter(table, normalizedTenant), ct);
        }

        return null;
    }

    public async Task<M365SecuritySnapshotRecord> UpsertSnapshotAsync(
        M365SecuritySnapshotRecord snapshot,
        CancellationToken ct = default)
    {
        if (snapshot is null)
            throw new ArgumentNullException(nameof(snapshot));

        var clienteId = NormalizeGuid(snapshot.ClienteId, nameof(snapshot.ClienteId));
        var tenantId = NormalizeRequiredText(snapshot.TenantId, nameof(snapshot.TenantId));
        var periodo = NormalizeRequiredText(snapshot.Periodo, nameof(snapshot.Periodo));
        var existing = await FindSnapshotAsync(clienteId, tenantId, periodo, ct);
        var table = _options.Dataverse.SecuritySnapshot;
        var payload = BuildSnapshotPayload(snapshot, clienteId, tenantId, periodo);

        if (!string.IsNullOrWhiteSpace(clienteId))
        {
            var navigationProperty = await ResolveLookupNavigationPropertyAsync(
                table.TableLogicalName,
                table.ClientLookupField,
                table.ClientNavigationProperty,
                ct);
            if (!string.IsNullOrWhiteSpace(navigationProperty))
                payload[$"{navigationProperty}@odata.bind"] = $"/{ClientsEntitySetName}({clienteId})";
        }

        var relativeUrl = string.IsNullOrWhiteSpace(existing?.RecordId)
            ? $"/api/data/v9.2/{table.TableSetName}"
            : $"/api/data/v9.2/{table.TableSetName}({existing.RecordId})";
        var method = string.IsNullOrWhiteSpace(existing?.RecordId) ? "POST" : "PATCH";
        var body = await CallDataverseAppSendAsync(relativeUrl, method, payload, ct, AddReturnRepresentationHeaders);
        if (!string.IsNullOrWhiteSpace(body))
        {
            using var doc = JsonDocument.Parse(body);
            var record = BuildSnapshotRecord(doc.RootElement);
            if (!string.IsNullOrWhiteSpace(record.RecordId))
                return record;
        }

        return await FindSnapshotAsync(clienteId, tenantId, periodo, ct)
            ?? new M365SecuritySnapshotRecord
            {
                RecordId = existing?.RecordId ?? "",
                ClienteId = clienteId,
                TenantId = tenantId,
                Periodo = periodo,
                SecureScoreActual = snapshot.SecureScoreActual,
                SecureScoreMaximo = snapshot.SecureScoreMaximo,
                AlertasHigh = snapshot.AlertasHigh,
                AlertasMedium = snapshot.AlertasMedium,
                AlertasLow = snapshot.AlertasLow,
                IncidentesActivos = snapshot.IncidentesActivos,
                IncidentesResueltos = snapshot.IncidentesResueltos,
                FechaConsulta = snapshot.FechaConsulta,
                EstadoConsulta = snapshot.EstadoConsulta,
                ErrorConsulta = snapshot.ErrorConsulta
            };
    }

    private async Task<M365TenantConnectionRecord?> FindConnectionByFilterAsync(string filter, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(filter))
            return null;

        var table = _options.Dataverse;
        var relativeUrl =
            $"/api/data/v9.2/{table.ConnectionTableSetName}" +
            $"?$select={BuildConnectionSelectClause()}" +
            $"&$filter={Uri.EscapeDataString(filter)}" +
            "&$orderby=modifiedon desc&$top=1";
        var items = await GetDataverseAppEntitiesAsync(relativeUrl, ct, AddFormattedValueHeaders);
        return items.Select(BuildConnectionRecord).FirstOrDefault(item => !string.IsNullOrWhiteSpace(item.RecordId));
    }

    private async Task<M365SecuritySnapshotRecord?> FindSnapshotAsync(
        string clienteId,
        string tenantId,
        string periodo,
        CancellationToken ct)
    {
        var table = _options.Dataverse.SecuritySnapshot;
        var filter =
            $"{table.InternalClientIdField} eq '{EscapeOdataLiteral(clienteId)}' and " +
            $"{table.TenantIdField} eq '{EscapeOdataLiteral(tenantId)}' and " +
            $"{table.PeriodoField} eq '{EscapeOdataLiteral(periodo)}'";
        var relativeUrl =
            $"/api/data/v9.2/{table.TableSetName}" +
            $"?$select={BuildSnapshotSelectClause()}" +
            $"&$filter={Uri.EscapeDataString(filter)}" +
            "&$orderby=modifiedon desc&$top=1";
        var items = await GetDataverseAppEntitiesAsync(relativeUrl, ct, AddFormattedValueHeaders);
        return items.Select(BuildSnapshotRecord).FirstOrDefault(item => !string.IsNullOrWhiteSpace(item.RecordId));
    }

    private Dictionary<string, object?> BuildSnapshotPayload(
        M365SecuritySnapshotRecord snapshot,
        string clienteId,
        string tenantId,
        string periodo)
    {
        var table = _options.Dataverse.SecuritySnapshot;
        return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            [table.PrimaryNameField] = BuildSnapshotName(clienteId, tenantId, periodo),
            [table.InternalClientIdField] = clienteId,
            [table.TenantIdField] = tenantId,
            [table.PeriodoField] = periodo,
            [table.SecureScoreActualField] = snapshot.SecureScoreActual,
            [table.SecureScoreMaximoField] = snapshot.SecureScoreMaximo,
            [table.AlertasHighField] = snapshot.AlertasHigh,
            [table.AlertasMediumField] = snapshot.AlertasMedium,
            [table.AlertasLowField] = snapshot.AlertasLow,
            [table.IncidentesActivosField] = snapshot.IncidentesActivos,
            [table.IncidentesResueltosField] = snapshot.IncidentesResueltos,
            [table.RecomendacionesTopJsonField] = snapshot.RecomendacionesTopJson,
            [table.AlertasJsonField] = snapshot.AlertasJson,
            [table.IncidentesJsonField] = snapshot.IncidentesJson,
            [table.FechaConsultaField] = snapshot.FechaConsulta,
            [table.EstadoConsultaField] = snapshot.EstadoConsulta,
            [table.ErrorConsultaField] = snapshot.ErrorConsulta
        };
    }

    private async Task<string> ResolveLookupNavigationPropertyAsync(
        string entityLogicalName,
        string lookupField,
        string configuredNavigationProperty,
        CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(configuredNavigationProperty)
            && !string.Equals(configuredNavigationProperty, lookupField, StringComparison.OrdinalIgnoreCase))
        {
            return configuredNavigationProperty.Trim();
        }

        if (string.IsNullOrWhiteSpace(entityLogicalName) || string.IsNullOrWhiteSpace(lookupField))
            return lookupField;

        try
        {
            var relativeUrl =
                $"/api/data/v9.2/EntityDefinitions(LogicalName='{EscapeOdataLiteral(entityLogicalName)}')" +
                "?$select=LogicalName" +
                "&$expand=ManyToOneRelationships($select=ReferencingAttribute,ReferencingEntityNavigationPropertyName)";
            var json = await CallDataverseAppGetJsonAsync(relativeUrl, ct);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("ManyToOneRelationships", out var relationships)
                && relationships.ValueKind == JsonValueKind.Array)
            {
                var navigationProperty = relationships
                    .EnumerateArray()
                    .Where(relationship => string.Equals(
                        ReadString(relationship, "ReferencingAttribute"),
                        lookupField,
                        StringComparison.OrdinalIgnoreCase))
                    .Select(relationship => ReadString(relationship, "ReferencingEntityNavigationPropertyName"))
                    .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

                if (!string.IsNullOrWhiteSpace(navigationProperty))
                    return navigationProperty.Trim();
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or JsonException)
        {
            _logger.LogWarning(
                ex,
                "No fue posible resolver la propiedad de navegacion {LookupField} para {EntityLogicalName}. Se usara {Fallback}.",
                lookupField,
                entityLogicalName,
                lookupField);
        }

        return lookupField;
    }

    private async Task<string> GetDataverseAppAccessTokenAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_dataverseBaseUrl)
            || string.IsNullOrWhiteSpace(_dataverseTenantId)
            || string.IsNullOrWhiteSpace(_dataverseClientId)
            || string.IsNullOrWhiteSpace(_dataverseClientSecret))
        {
            throw new M365PersistenceConfigurationException(
                "La persistencia M365 requiere credenciales app-only para Dataverse. Configura Dataverse:BaseUrl, Dataverse:TenantId o AzureAd:TenantId, y una credencial valida: Dataverse:ClientSecret con Dataverse:ClientId o AzureAd:ClientId, AzureAd:ClientSecret con AzureAd:ClientId, o M365:ClientSecret con M365:ClientId.");
        }

        var authority = $"{_azureAuthorityInstance.TrimEnd('/')}/{_dataverseTenantId.Trim()}";
        var app = ConfidentialClientApplicationBuilder
            .Create(_dataverseClientId.Trim())
            .WithClientSecret(_dataverseClientSecret)
            .WithAuthority(authority)
            .Build();
        var result = await app
            .AcquireTokenForClient(new[] { $"{_dataverseBaseUrl}/.default" })
            .ExecuteAsync(ct);

        _logger.LogDebug(
            "Token app-only de Dataverse obtenido usando credencial {CredentialSource}.",
            string.IsNullOrWhiteSpace(_dataverseCredentialSource) ? "sin origen" : _dataverseCredentialSource);

        return result.AccessToken;
    }

    private async Task<string> CallDataverseAppGetJsonAsync(
        string relativeUrl,
        CancellationToken ct,
        Action<HttpRequestMessage>? customizeRequest = null)
    {
        using var response = await CallDataverseAppResponseAsync(relativeUrl, "GET", ct, customizeRequest: customizeRequest);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw BuildDataverseAppException(response, body);

        return body;
    }

    private async Task<string> CallDataverseAppSendAsync(
        string relativeUrl,
        string method,
        object payload,
        CancellationToken ct,
        Action<HttpRequestMessage>? customizeRequest = null)
    {
        using var content = new StringContent(JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json");
        using var response = await CallDataverseAppResponseAsync(relativeUrl, method, ct, content, customizeRequest);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw BuildDataverseAppException(response, body);

        return body;
    }

    private InvalidOperationException BuildDataverseAppException(HttpResponseMessage response, string body)
    {
        var baseMessage = $"Dataverse app error {(int)response.StatusCode} {response.ReasonPhrase}. Body: {body}";
        if (response.StatusCode == System.Net.HttpStatusCode.Forbidden
            && (body.Contains("0x80072560", StringComparison.OrdinalIgnoreCase)
                || body.Contains("not a member of the organization", StringComparison.OrdinalIgnoreCase)))
        {
            return new M365PersistenceConfigurationException(
                "La app configurada para persistir M365 no es miembro del entorno Dataverse. " +
                "Crea o activa un Application User en el entorno de Dataverse para la App Registration indicada en Dataverse:ClientId, AzureAd:ClientId o M365:ClientId y asignale un rol con permisos sobre las tablas M365.",
                new InvalidOperationException(baseMessage));
        }

        return new InvalidOperationException(baseMessage);
    }

    private async Task<HttpResponseMessage> CallDataverseAppResponseAsync(
        string relativeUrl,
        string method,
        CancellationToken ct,
        HttpContent? content = null,
        Action<HttpRequestMessage>? customizeRequest = null)
    {
        var token = await GetDataverseAppAccessTokenAsync(ct);
        var client = _httpClientFactory.CreateClient();
        using var request = new HttpRequestMessage(new HttpMethod(method), BuildDataverseAppUri(relativeUrl));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.TryAddWithoutValidation("OData-MaxVersion", "4.0");
        request.Headers.TryAddWithoutValidation("OData-Version", "4.0");
        if (content is not null)
            request.Content = content;

        customizeRequest?.Invoke(request);
        return await client.SendAsync(request, ct);
    }

    private async Task<List<JsonElement>> GetDataverseAppEntitiesAsync(
        string relativeUrl,
        CancellationToken ct,
        Action<HttpRequestMessage>? customizeRequest = null)
    {
        const int maxPages = 50;
        var pageCount = 0;
        var items = new List<JsonElement>();
        string? nextRelativeUrl = relativeUrl;

        while (!string.IsNullOrWhiteSpace(nextRelativeUrl))
        {
            pageCount++;
            if (pageCount > maxPages)
                throw new InvalidOperationException("Se alcanzo el limite de paginas consultando registros M365 en Dataverse.");

            var json = await CallDataverseAppGetJsonAsync(nextRelativeUrl, ct, customizeRequest);
            using var doc = JsonDocument.Parse(json);
            var value = doc.RootElement.GetProperty("value");
            foreach (var item in value.EnumerateArray())
            {
                items.Add(item.Clone());
            }

            nextRelativeUrl = doc.RootElement.TryGetProperty("@odata.nextLink", out var nextLinkProp)
                ? GetRelativeDataverseUrl(nextLinkProp.GetString())
                : null;
        }

        return items;
    }

    private Uri BuildDataverseAppUri(string relativeUrl)
    {
        if (Uri.TryCreate(relativeUrl, UriKind.Absolute, out var absoluteUri))
            return absoluteUri;

        var normalizedRelativeUrl = relativeUrl.StartsWith("/", StringComparison.Ordinal)
            ? relativeUrl
            : $"/{relativeUrl}";

        return new Uri($"{_dataverseBaseUrl}{normalizedRelativeUrl}", UriKind.Absolute);
    }

    private M365TenantConnectionRecord BuildConnectionRecord(JsonElement item)
    {
        var table = _options.Dataverse;
        var clientLookup = BuildLookupValuePropertyName(table.ClientLookupField);
        return new M365TenantConnectionRecord
        {
            RecordId = ReadString(item, table.ConnectionIdField).Trim(),
            ClienteId = FirstNonEmpty(ReadString(item, table.InternalClientIdField), ReadString(item, clientLookup)),
            ClienteNombre = FirstNonEmpty(ReadLookupFormattedValue(item, clientLookup), ReadString(item, $"{table.ClientLookupField}{FormattedValueAnnotationSuffix}")),
            TenantId = ReadString(item, table.TenantIdField).Trim(),
            TenantHint = ReadString(item, table.TenantHintField).Trim(),
            EstadoConexion = ReadString(item, table.EstadoConexionField).Trim(),
            FechaConexion = ReadString(item, table.FechaConexionField).Trim(),
            AdminConsent = ReadBool(item, table.AdminConsentField),
            PermisosSolicitados = ReadString(item, table.PermisosSolicitadosField).Trim(),
            ResultadoConsentimiento = ReadString(item, table.ResultadoConsentimientoField).Trim()
        };
    }

    private M365SecuritySnapshotRecord BuildSnapshotRecord(JsonElement item)
    {
        var table = _options.Dataverse.SecuritySnapshot;
        var clientLookup = BuildLookupValuePropertyName(table.ClientLookupField);
        return new M365SecuritySnapshotRecord
        {
            RecordId = ReadString(item, table.IdField).Trim(),
            ClienteId = FirstNonEmpty(ReadString(item, table.InternalClientIdField), ReadString(item, clientLookup)),
            TenantId = ReadString(item, table.TenantIdField).Trim(),
            Periodo = ReadString(item, table.PeriodoField).Trim(),
            SecureScoreActual = ReadDecimal(item, table.SecureScoreActualField) ?? 0m,
            SecureScoreMaximo = ReadDecimal(item, table.SecureScoreMaximoField) ?? 0m,
            AlertasHigh = ReadInt(item, table.AlertasHighField) ?? 0,
            AlertasMedium = ReadInt(item, table.AlertasMediumField) ?? 0,
            AlertasLow = ReadInt(item, table.AlertasLowField) ?? 0,
            IncidentesActivos = ReadInt(item, table.IncidentesActivosField) ?? 0,
            IncidentesResueltos = ReadInt(item, table.IncidentesResueltosField) ?? 0,
            RecomendacionesTopJson = ReadString(item, table.RecomendacionesTopJsonField).Trim(),
            AlertasJson = ReadString(item, table.AlertasJsonField).Trim(),
            IncidentesJson = ReadString(item, table.IncidentesJsonField).Trim(),
            FechaConsulta = ReadString(item, table.FechaConsultaField).Trim(),
            EstadoConsulta = ReadString(item, table.EstadoConsultaField).Trim(),
            ErrorConsulta = ReadString(item, table.ErrorConsultaField).Trim()
        };
    }

    private string BuildConnectionSelectClause()
    {
        var table = _options.Dataverse;
        return string.Join(",",
            new[]
            {
                table.ConnectionIdField,
                table.PrimaryNameField,
                table.InternalClientIdField,
                BuildLookupValuePropertyName(table.ClientLookupField),
                table.TenantIdField,
                table.TenantHintField,
                table.EstadoConexionField,
                table.FechaConexionField,
                table.AdminConsentField,
                table.PermisosSolicitadosField,
                table.ResultadoConsentimientoField
            }
            .Where(field => !string.IsNullOrWhiteSpace(field))
            .Distinct(StringComparer.OrdinalIgnoreCase));
    }

    private string BuildSnapshotSelectClause()
    {
        var table = _options.Dataverse.SecuritySnapshot;
        return string.Join(",",
            new[]
            {
                table.IdField,
                table.PrimaryNameField,
                table.InternalClientIdField,
                BuildLookupValuePropertyName(table.ClientLookupField),
                table.TenantIdField,
                table.PeriodoField,
                table.SecureScoreActualField,
                table.SecureScoreMaximoField,
                table.AlertasHighField,
                table.AlertasMediumField,
                table.AlertasLowField,
                table.IncidentesActivosField,
                table.IncidentesResueltosField,
                table.RecomendacionesTopJsonField,
                table.AlertasJsonField,
                table.IncidentesJsonField,
                table.FechaConsultaField,
                table.EstadoConsultaField,
                table.ErrorConsultaField
            }
            .Where(field => !string.IsNullOrWhiteSpace(field))
            .Distinct(StringComparer.OrdinalIgnoreCase));
    }

    private static string BuildSnapshotName(string clienteId, string tenantId, string periodo)
    {
        var tenant = string.IsNullOrWhiteSpace(tenantId) ? "sin tenant" : tenantId.Trim();
        return $"M365 seguridad - {clienteId} - {tenant} - {periodo}";
    }

    private static string NormalizeGuid(string? raw, string paramName)
    {
        if (!Guid.TryParse(raw, out var parsed))
            throw new InvalidOperationException($"El valor de {paramName} no es valido.");

        return parsed.ToString("D");
    }

    private static string NormalizeOptionalGuid(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return "";

        return Guid.TryParse(raw, out var parsed) ? parsed.ToString("D") : "";
    }

    private static string NormalizeRequiredText(string? value, string paramName)
    {
        var normalized = value?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(normalized))
            throw new InvalidOperationException($"El valor de {paramName} es obligatorio.");

        return normalized;
    }

    private static string EscapeOdataLiteral(string value) =>
        (value ?? string.Empty).Replace("'", "''");

    private static string BuildTenantIdOrHintFilter(M365DataverseOptions table, string tenantIdOrHint)
    {
        var value = EscapeOdataLiteral(tenantIdOrHint.Trim());
        return $"{table.TenantIdField} eq '{value}' or {table.TenantHintField} eq '{value}'";
    }

    private static DataverseAppCredential ResolveDataverseAppCredential(IConfiguration configuration)
    {
        var dataverseClientId = FirstNonEmpty(configuration["Dataverse:ClientId"], configuration["AzureAd:ClientId"]);
        var dataverseClientSecret = FirstNonEmpty(configuration["Dataverse:ClientSecret"]);
        if (!string.IsNullOrWhiteSpace(dataverseClientId)
            && !string.IsNullOrWhiteSpace(dataverseClientSecret))
        {
            return new DataverseAppCredential(dataverseClientId, dataverseClientSecret, "Dataverse");
        }

        var azureClientId = FirstNonEmpty(configuration["AzureAd:ClientId"]);
        var azureClientSecret = FirstNonEmpty(configuration["AzureAd:ClientSecret"]);
        if (!string.IsNullOrWhiteSpace(azureClientId)
            && !string.IsNullOrWhiteSpace(azureClientSecret))
        {
            return new DataverseAppCredential(azureClientId, azureClientSecret, "AzureAd");
        }

        var m365ClientId = FirstNonEmpty(configuration["M365:ClientId"]);
        var m365ClientSecret = FirstNonEmpty(configuration["M365:ClientSecret"]);
        if (!string.IsNullOrWhiteSpace(m365ClientId)
            && !string.IsNullOrWhiteSpace(m365ClientSecret))
        {
            return new DataverseAppCredential(m365ClientId, m365ClientSecret, "M365");
        }

        return new DataverseAppCredential(dataverseClientId, "", "");
    }

    private static string? GetRelativeDataverseUrl(string? nextLink)
    {
        if (string.IsNullOrWhiteSpace(nextLink))
            return null;

        if (Uri.TryCreate(nextLink, UriKind.Absolute, out var absoluteUri))
            return $"{absoluteUri.AbsolutePath}{absoluteUri.Query}";

        return nextLink;
    }

    private static string BuildLookupValuePropertyName(string lookupField) =>
        $"_{lookupField}_value";

    private static string? ReadLookupFormattedValue(JsonElement item, string? lookupValuePropertyName)
    {
        if (string.IsNullOrWhiteSpace(lookupValuePropertyName))
            return null;

        return ReadString(item, $"{lookupValuePropertyName}{FormattedValueAnnotationSuffix}");
    }

    private static string ReadString(JsonElement item, string? propertyName)
    {
        if (string.IsNullOrWhiteSpace(propertyName))
            return "";

        if (!item.TryGetProperty(propertyName, out var property))
            return "";

        return property.ValueKind switch
        {
            JsonValueKind.String => property.GetString() ?? "",
            JsonValueKind.Number => property.ToString(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => ""
        };
    }

    private static decimal? ReadDecimal(JsonElement item, string propertyName)
    {
        if (!item.TryGetProperty(propertyName, out var property))
            return null;

        if (property.ValueKind == JsonValueKind.Number && property.TryGetDecimal(out var number))
            return number;

        if (property.ValueKind == JsonValueKind.String
            && decimal.TryParse(property.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private static int? ReadInt(JsonElement item, string propertyName)
    {
        if (!item.TryGetProperty(propertyName, out var property))
            return null;

        if (property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out var number))
            return number;

        if (property.ValueKind == JsonValueKind.String
            && int.TryParse(property.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private static bool ReadBool(JsonElement item, string propertyName)
    {
        if (!item.TryGetProperty(propertyName, out var property))
            return false;

        return property.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String => bool.TryParse(property.GetString(), out var parsed) && parsed,
            JsonValueKind.Number => property.TryGetInt32(out var value) && value != 0,
            _ => false
        };
    }

    private static void AddReturnRepresentationHeaders(HttpRequestMessage request)
    {
        request.Headers.TryAddWithoutValidation(
            "Prefer",
            $"return=representation, odata.include-annotations=\"{FormattedValueAnnotationSuffix.TrimStart('@')}\"");
    }

    private static void AddFormattedValueHeaders(HttpRequestMessage request)
    {
        request.Headers.TryAddWithoutValidation("Prefer", $"odata.include-annotations=\"{FormattedValueAnnotationSuffix.TrimStart('@')}\"");
    }

    private static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? "";

    private sealed record DataverseAppCredential(string ClientId, string ClientSecret, string Source);
}
