using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CotizadorInterno.Web.Models.M365;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using Microsoft.Identity.Client;

namespace CotizadorInterno.Web.Services;

public sealed class M365TenantConnectionService : IM365TenantConnectionService
{
    private const string GraphDefaultScope = "https://graph.microsoft.com/.default";
    private const string ClientsEntitySetName = "cr07a_clientes";
    private const string FormattedValueAnnotationSuffix = "@OData.Community.Display.V1.FormattedValue";
    private readonly M365Options _options;
    private readonly IDataProtector _stateProtector;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<M365TenantConnectionService> _logger;
    private readonly string _dataverseBaseUrl;
    private readonly string _azureAuthorityInstance;
    private readonly string _azureTenantId;
    private readonly string _azureClientId;
    private readonly string _dataverseClientSecret;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    public M365TenantConnectionService(
        IOptions<M365Options> options,
        IDataProtectionProvider dataProtectionProvider,
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<M365TenantConnectionService> logger)
    {
        _options = options.Value;
        _stateProtector = dataProtectionProvider.CreateProtector("CotizadorInterno.M365.AdminConsentState.v1");
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _dataverseBaseUrl = (configuration["Dataverse:BaseUrl"] ?? "").TrimEnd('/');
        _azureAuthorityInstance = configuration["AzureAd:Instance"] ?? "https://login.microsoftonline.com/";
        _azureTenantId = configuration["AzureAd:TenantId"] ?? "";
        _azureClientId = configuration["AzureAd:ClientId"] ?? "";
        _dataverseClientSecret = configuration["Dataverse:ClientSecret"]
            ?? configuration["AzureAd:ClientSecret"]
            ?? "";
    }

    public M365ConnectUrlResult BuildConnectUrl(M365ConnectUrlRequest request)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));

        EnsureConsentConfiguration();
        var clienteId = NormalizeGuid(request.ClienteId, nameof(request.ClienteId));
        var tenantHint = NormalizeTenantHint(request.TenantIdOrDomain);
        var scopes = ResolveConsentScopes();
        var requestedPermissions = ResolveRequestedPermissions(scopes);
        var state = new M365ConsentState
        {
            ClienteId = clienteId,
            TenantHint = tenantHint,
            RequestedScopes = scopes.ToList(),
            RequestedPermissions = requestedPermissions.ToList(),
            IssuedAtUtc = DateTimeOffset.UtcNow,
            Nonce = Guid.NewGuid().ToString("N")
        };

        var protectedState = _stateProtector.Protect(JsonSerializer.Serialize(state, JsonOptions));
        var authority = NormalizeAuthorityHost(_options.AuthorityHost);
        var endpoint = $"{authority}/{Uri.EscapeDataString(tenantHint)}/v2.0/adminconsent";
        var query = BuildQueryString(new Dictionary<string, string>
        {
            ["client_id"] = _options.ClientId.Trim(),
            ["scope"] = string.Join(' ', scopes),
            ["redirect_uri"] = _options.RedirectUri.Trim(),
            ["state"] = protectedState
        });

        return new M365ConnectUrlResult
        {
            Url = $"{endpoint}?{query}",
            ClienteId = clienteId,
            TenantHint = tenantHint,
            RedirectUri = _options.RedirectUri.Trim(),
            Scopes = scopes,
            RequestedPermissions = requestedPermissions
        };
    }

    public async Task<M365ConsentCallbackResult> HandleConsentCallbackAsync(
        M365ConsentCallbackRequest request,
        CancellationToken ct = default)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));

        var state = UnprotectState(request.State);
        EnsureStateIsFresh(state);

        var tenantId = FirstNonEmpty(request.Tenant, state.TenantHint);
        var adminConsent = IsAdminConsentGranted(request.AdminConsent);
        var hasProviderError = !string.IsNullOrWhiteSpace(request.Error);
        var success = adminConsent && !hasProviderError && !string.IsNullOrWhiteSpace(request.Tenant);
        var estado = success ? "Conectado" : "Consentimiento no completado";
        var requestedPermissions = state.RequestedPermissions.Count > 0
            ? state.RequestedPermissions
            : ResolveRequestedPermissions(state.RequestedScopes);
        var resultPayload = JsonSerializer.Serialize(new
        {
            tenant = request.Tenant,
            admin_consent = request.AdminConsent,
            scope = request.Scope,
            error = request.Error,
            error_description = request.ErrorDescription,
            receivedAtUtc = DateTimeOffset.UtcNow
        }, JsonOptions);

        try
        {
            var record = await UpsertConnectionAsync(
                state.ClienteId,
                tenantId,
                state.TenantHint,
                estado,
                adminConsent,
                state.RequestedScopes,
                requestedPermissions,
                request.Scope,
                resultPayload,
                request.Error,
                request.ErrorDescription,
                ct);

            _logger.LogInformation(
                "Consentimiento M365 recibido para cliente {ClienteId}, tenant {TenantId}, resultado {EstadoConexion}.",
                state.ClienteId,
                tenantId,
                estado);

            return new M365ConsentCallbackResult
            {
                Success = success,
                ClienteId = state.ClienteId,
                TenantId = tenantId,
                EstadoConexion = estado,
                RecordId = record.RecordId,
                Message = success
                    ? "Consentimiento de Microsoft 365 guardado correctamente."
                    : "Microsoft no completo el consentimiento. El resultado fue guardado para revision."
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "No fue posible guardar el consentimiento M365 para cliente {ClienteId} y tenant {TenantId}.",
                state.ClienteId,
                tenantId);
            throw;
        }
    }

    public async Task<M365TestConnectionResult> TestConnectionAsync(
        M365TestConnectionRequest request,
        CancellationToken ct = default)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));

        EnsureGraphCredentialConfiguration();
        var connection = await FindConnectionAsync(request.ClienteId, request.TenantId, ct)
            ?? throw new InvalidOperationException("No hay una conexion Microsoft 365 guardada para el cliente o tenant indicado.");
        if (string.IsNullOrWhiteSpace(connection.TenantId))
            throw new InvalidOperationException("La conexion guardada no tiene tenantId.");

        var testedAt = DateTimeOffset.UtcNow;
        var endpoint = BuildGraphUri("/organization?$select=id,displayName&$top=1");
        try
        {
            var token = await GetGraphAccessTokenAsync(connection.TenantId, ct);
            using var httpRequest = new HttpRequestMessage(HttpMethod.Get, endpoint);
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            httpRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            var client = _httpClientFactory.CreateClient();
            using var response = await client.SendAsync(httpRequest, ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
            {
                var failureMessage = $"Graph devolvio {(int)response.StatusCode} {response.ReasonPhrase}. {Truncate(body, 900)}".Trim();
                await UpdateConnectionTestResultAsync(connection.RecordId, false, failureMessage, "Error prueba Graph", testedAt, ct);
                _logger.LogWarning(
                    "Prueba Graph fallida para cliente {ClienteId}, tenant {TenantId}. Status {StatusCode}.",
                    connection.ClienteId,
                    connection.TenantId,
                    (int)response.StatusCode);

                return new M365TestConnectionResult
                {
                    Success = false,
                    ClienteId = connection.ClienteId,
                    TenantId = connection.TenantId,
                    GraphEndpoint = endpoint.ToString(),
                    EstadoConexion = "Error prueba Graph",
                    TestedAt = testedAt.ToString("O", CultureInfo.InvariantCulture),
                    Message = failureMessage
                };
            }

            var tenantDisplayName = ReadOrganizationDisplayName(body);
            var successMessage = string.IsNullOrWhiteSpace(tenantDisplayName)
                ? "Conexion Microsoft Graph validada correctamente."
                : $"Conexion Microsoft Graph validada para {tenantDisplayName}.";
            await UpdateConnectionTestResultAsync(connection.RecordId, true, successMessage, "Conexion probada", testedAt, ct);

            return new M365TestConnectionResult
            {
                Success = true,
                ClienteId = connection.ClienteId,
                TenantId = connection.TenantId,
                TenantDisplayName = tenantDisplayName,
                GraphEndpoint = endpoint.ToString(),
                EstadoConexion = "Conexion probada",
                TestedAt = testedAt.ToString("O", CultureInfo.InvariantCulture),
                Message = successMessage
            };
        }
        catch (Exception ex) when (ex is MsalException or HttpRequestException or TaskCanceledException)
        {
            var failureMessage = $"No fue posible validar Microsoft Graph: {ex.Message}";
            await UpdateConnectionTestResultAsync(connection.RecordId, false, failureMessage, "Error prueba Graph", testedAt, ct);
            _logger.LogWarning(
                ex,
                "Prueba M365 fallida para cliente {ClienteId}, tenant {TenantId}.",
                connection.ClienteId,
                connection.TenantId);

            return new M365TestConnectionResult
            {
                Success = false,
                ClienteId = connection.ClienteId,
                TenantId = connection.TenantId,
                GraphEndpoint = endpoint.ToString(),
                EstadoConexion = "Error prueba Graph",
                TestedAt = testedAt.ToString("O", CultureInfo.InvariantCulture),
                Message = failureMessage
            };
        }
    }

    private async Task<M365TenantConnectionRecord> UpsertConnectionAsync(
        string clienteId,
        string tenantId,
        string tenantHint,
        string estadoConexion,
        bool adminConsent,
        IReadOnlyList<string> requestedScopes,
        IReadOnlyList<string> requestedPermissions,
        string consentedScope,
        string consentResult,
        string error,
        string errorDescription,
        CancellationToken ct)
    {
        var existing = await FindConnectionAsync(clienteId, tenantId, ct);
        var table = _options.Dataverse;
        var now = DateTimeOffset.UtcNow;
        var payload = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            [table.PrimaryNameField] = BuildConnectionName(clienteId, tenantId),
            [table.InternalClientIdField] = clienteId,
            [table.TenantIdField] = tenantId,
            [table.TenantHintField] = tenantHint,
            [table.EstadoConexionField] = estadoConexion,
            [table.FechaConexionField] = now.ToString("O", CultureInfo.InvariantCulture),
            [table.PermisosSolicitadosField] = BuildRequestedPermissionsText(requestedScopes, requestedPermissions),
            [table.ResultadoConsentimientoField] = consentResult,
            [table.AdminConsentField] = adminConsent,
            [table.ScopeConsentidoField] = consentedScope,
            [table.ErrorField] = error,
            [table.ErrorDescriptionField] = errorDescription
        };

        var navigationProperty = await ResolveClientNavigationPropertyAsync(ct);
        if (!string.IsNullOrWhiteSpace(clienteId))
        {
            payload[$"{navigationProperty}@odata.bind"] = $"/{ClientsEntitySetName}({clienteId})";
        }

        var relativeUrl = string.IsNullOrWhiteSpace(existing?.RecordId)
            ? $"/api/data/v9.2/{table.ConnectionTableSetName}"
            : $"/api/data/v9.2/{table.ConnectionTableSetName}({existing.RecordId})";

        var method = string.IsNullOrWhiteSpace(existing?.RecordId) ? "POST" : "PATCH";
        var body = await CallDataverseAppSendAsync(relativeUrl, method, payload, ct, AddReturnRepresentationHeaders);
        if (!string.IsNullOrWhiteSpace(body))
        {
            using var doc = JsonDocument.Parse(body);
            var record = BuildConnectionRecord(doc.RootElement);
            if (!string.IsNullOrWhiteSpace(record.RecordId))
                return record;
        }

        return await FindConnectionAsync(clienteId, tenantId, ct)
            ?? new M365TenantConnectionRecord
            {
                RecordId = existing?.RecordId ?? "",
                ClienteId = clienteId,
                TenantId = tenantId,
                TenantHint = tenantHint,
                EstadoConexion = estadoConexion,
                AdminConsent = adminConsent,
                PermisosSolicitados = BuildRequestedPermissionsText(requestedScopes, requestedPermissions),
                ResultadoConsentimiento = consentResult
            };
    }

    private async Task<M365TenantConnectionRecord?> FindConnectionAsync(string clienteId, string tenantId, CancellationToken ct)
    {
        var table = _options.Dataverse;
        var filters = new List<string>();
        var normalizedClienteId = NormalizeOptionalGuid(clienteId);
        if (!string.IsNullOrWhiteSpace(normalizedClienteId))
            filters.Add($"{table.InternalClientIdField} eq '{EscapeOdataLiteral(normalizedClienteId)}'");

        if (!string.IsNullOrWhiteSpace(tenantId))
            filters.Add($"{table.TenantIdField} eq '{EscapeOdataLiteral(tenantId.Trim())}'");

        if (filters.Count == 0)
            return null;

        var filter = filters.Count == 1
            ? filters[0]
            : $"({string.Join(" or ", filters)})";
        var relativeUrl =
            $"/api/data/v9.2/{table.ConnectionTableSetName}" +
            $"?$select={BuildConnectionSelectClause()}" +
            $"&$filter={Uri.EscapeDataString(filter)}" +
            $"&$orderby=modifiedon desc&$top=1";
        var items = await GetDataverseAppEntitiesAsync(relativeUrl, ct, AddFormattedValueHeaders);
        return items.Select(BuildConnectionRecord).FirstOrDefault(item => !string.IsNullOrWhiteSpace(item.RecordId));
    }

    private async Task UpdateConnectionTestResultAsync(
        string recordId,
        bool success,
        string message,
        string estadoConexion,
        DateTimeOffset testedAt,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(recordId))
            return;

        var table = _options.Dataverse;
        var payload = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            [table.EstadoConexionField] = estadoConexion,
            [table.LastTestDateField] = testedAt.ToString("O", CultureInfo.InvariantCulture),
            [table.LastTestSuccessField] = success,
            [table.LastTestResultField] = Truncate(message, 1800)
        };
        var relativeUrl = $"/api/data/v9.2/{table.ConnectionTableSetName}({NormalizeGuid(recordId, nameof(recordId))})";
        await CallDataverseAppSendAsync(relativeUrl, "PATCH", payload, ct);
    }

    private async Task<string> ResolveClientNavigationPropertyAsync(CancellationToken ct)
    {
        var table = _options.Dataverse;
        if (!string.IsNullOrWhiteSpace(table.ClientNavigationProperty)
            && !string.Equals(table.ClientNavigationProperty, table.ClientLookupField, StringComparison.OrdinalIgnoreCase))
        {
            return table.ClientNavigationProperty.Trim();
        }

        try
        {
            var relativeUrl =
                $"/api/data/v9.2/EntityDefinitions(LogicalName='{EscapeOdataLiteral(table.ConnectionTableLogicalName)}')" +
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
                        table.ClientLookupField,
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
                "No fue posible resolver la propiedad de navegacion cliente para la tabla M365. Se usara {Fallback}.",
                table.ClientNavigationProperty);
        }

        return FirstNonEmpty(table.ClientNavigationProperty, table.ClientLookupField);
    }

    private async Task<string> GetGraphAccessTokenAsync(string tenantId, CancellationToken ct)
    {
        var authority = $"{NormalizeAuthorityHost(_options.AuthorityHost)}/{tenantId.Trim()}";
        var builder = ConfidentialClientApplicationBuilder
            .Create(_options.ClientId.Trim())
            .WithAuthority(authority);

        var certificate = LoadCertificateOrDefault();
        IConfidentialClientApplication app = certificate is not null
            ? builder.WithCertificate(certificate).Build()
            : builder.WithClientSecret(_options.ClientSecret).Build();

        var result = await app
            .AcquireTokenForClient(ResolveGraphTokenScopes())
            .ExecuteAsync(ct);
        return result.AccessToken;
    }

    private async Task<string> GetDataverseAppAccessTokenAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_dataverseBaseUrl)
            || string.IsNullOrWhiteSpace(_azureTenantId)
            || string.IsNullOrWhiteSpace(_azureClientId)
            || string.IsNullOrWhiteSpace(_dataverseClientSecret))
        {
            throw new InvalidOperationException(
                "La persistencia M365 requiere configurar Dataverse:BaseUrl, AzureAd:TenantId, AzureAd:ClientId y Dataverse:ClientSecret o AzureAd:ClientSecret.");
        }

        var authorityBase = _azureAuthorityInstance.EndsWith("/", StringComparison.Ordinal)
            ? _azureAuthorityInstance
            : $"{_azureAuthorityInstance}/";
        var app = ConfidentialClientApplicationBuilder
            .Create(_azureClientId)
            .WithClientSecret(_dataverseClientSecret)
            .WithAuthority($"{authorityBase}{_azureTenantId}")
            .Build();

        var result = await app
            .AcquireTokenForClient(new[] { $"{_dataverseBaseUrl}/.default" })
            .ExecuteAsync(ct);

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
            throw new InvalidOperationException($"Dataverse app error {(int)response.StatusCode} {response.ReasonPhrase}. Body: {body}");

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
            throw new InvalidOperationException($"Dataverse app error {(int)response.StatusCode} {response.ReasonPhrase}. Body: {body}");

        return body;
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
        const int maxPages = 20;
        var pageCount = 0;
        var items = new List<JsonElement>();
        string? nextRelativeUrl = relativeUrl;

        while (!string.IsNullOrWhiteSpace(nextRelativeUrl))
        {
            pageCount++;
            if (pageCount > maxPages)
                throw new InvalidOperationException("Se alcanzo el limite de paginas consultando conexiones M365 en Dataverse.");

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

    private Uri BuildGraphUri(string relativePath)
    {
        var baseUrl = string.IsNullOrWhiteSpace(_options.GraphBaseUrl)
            ? "https://graph.microsoft.com/v1.0"
            : _options.GraphBaseUrl.TrimEnd('/');
        var normalizedPath = relativePath.StartsWith("/", StringComparison.Ordinal)
            ? relativePath
            : $"/{relativePath}";

        return new Uri($"{baseUrl}{normalizedPath}", UriKind.Absolute);
    }

    private X509Certificate2? LoadCertificateOrDefault()
    {
        if (!string.IsNullOrWhiteSpace(_options.CertificatePath))
        {
            var password = string.IsNullOrEmpty(_options.CertificatePassword)
                ? null
                : _options.CertificatePassword;
            return X509CertificateLoader.LoadPkcs12FromFile(_options.CertificatePath, password);
        }

        if (string.IsNullOrWhiteSpace(_options.CertificateThumbprint))
            return null;

        var thumbprint = _options.CertificateThumbprint.Replace(" ", "", StringComparison.Ordinal).Trim();
        var storeName = Enum.TryParse<StoreName>(_options.CertificateStoreName, ignoreCase: true, out var parsedStoreName)
            ? parsedStoreName
            : StoreName.My;
        var storeLocation = Enum.TryParse<StoreLocation>(_options.CertificateStoreLocation, ignoreCase: true, out var parsedStoreLocation)
            ? parsedStoreLocation
            : StoreLocation.CurrentUser;

        using var store = new X509Store(storeName, storeLocation);
        store.Open(OpenFlags.ReadOnly);
        return store.Certificates
            .Find(X509FindType.FindByThumbprint, thumbprint, validOnly: false)
            .OfType<X509Certificate2>()
            .OrderByDescending(certificate => certificate.NotAfter)
            .FirstOrDefault();
    }

    private M365ConsentState UnprotectState(string protectedState)
    {
        if (string.IsNullOrWhiteSpace(protectedState))
            throw new InvalidOperationException("La respuesta de Microsoft no incluyo state.");

        try
        {
            var json = _stateProtector.Unprotect(protectedState);
            return JsonSerializer.Deserialize<M365ConsentState>(json, JsonOptions)
                ?? throw new InvalidOperationException("El state de Microsoft no es valido.");
        }
        catch (Exception ex) when (ex is System.Security.Cryptography.CryptographicException or JsonException)
        {
            _logger.LogWarning(ex, "No fue posible validar el state del consentimiento M365.");
            throw new InvalidOperationException("El state de Microsoft no es valido o expiro.", ex);
        }
    }

    private void EnsureStateIsFresh(M365ConsentState state)
    {
        var maxAge = TimeSpan.FromMinutes(Math.Clamp(_options.StateLifetimeMinutes, 5, 1440));
        if (DateTimeOffset.UtcNow - state.IssuedAtUtc > maxAge)
            throw new InvalidOperationException("El enlace de consentimiento expiro. Genera uno nuevo.");

        if (string.IsNullOrWhiteSpace(state.ClienteId))
            throw new InvalidOperationException("El state no contiene clienteId.");
    }

    private void EnsureConsentConfiguration()
    {
        if (string.IsNullOrWhiteSpace(_options.ClientId))
            throw new InvalidOperationException("M365:ClientId no esta configurado.");

        if (string.IsNullOrWhiteSpace(_options.RedirectUri))
            throw new InvalidOperationException("M365:RedirectUri no esta configurado.");
    }

    private void EnsureGraphCredentialConfiguration()
    {
        EnsureConsentConfiguration();
        if (!string.IsNullOrWhiteSpace(_options.ClientSecret)
            || !string.IsNullOrWhiteSpace(_options.CertificatePath)
            || !string.IsNullOrWhiteSpace(_options.CertificateThumbprint))
        {
            return;
        }

        throw new InvalidOperationException("M365 requiere configurar ClientSecret o certificado para probar la conexion.");
    }

    private IReadOnlyList<string> ResolveConsentScopes()
    {
        var scopes = (_options.Scopes ?? Array.Empty<string>())
            .Select(scope => scope?.Trim())
            .Where(scope => !string.IsNullOrWhiteSpace(scope))
            .Select(scope => scope!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (scopes.Count == 0)
            scopes.Add(GraphDefaultScope);

        return scopes;
    }

    private string[] ResolveGraphTokenScopes()
    {
        var defaultScope = ResolveConsentScopes()
            .FirstOrDefault(scope => scope.EndsWith("/.default", StringComparison.OrdinalIgnoreCase));
        return new[] { defaultScope ?? GraphDefaultScope };
    }

    private IReadOnlyList<string> ResolveRequestedPermissions(IReadOnlyList<string> scopes)
    {
        var configured = (_options.RequestedPermissions ?? Array.Empty<string>())
            .Select(permission => permission?.Trim())
            .Where(permission => !string.IsNullOrWhiteSpace(permission))
            .Select(permission => permission!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return configured.Count > 0 ? configured : scopes;
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

    private static string BuildRequestedPermissionsText(
        IReadOnlyList<string> requestedScopes,
        IReadOnlyList<string> requestedPermissions)
    {
        var parts = new List<string>();
        if (requestedScopes.Count > 0)
            parts.Add($"Scopes: {string.Join(", ", requestedScopes)}");
        if (requestedPermissions.Count > 0)
            parts.Add($"Permisos: {string.Join(", ", requestedPermissions)}");

        return string.Join(" | ", parts);
    }

    private static string BuildConnectionName(string clienteId, string tenantId)
    {
        var tenant = string.IsNullOrWhiteSpace(tenantId) ? "sin tenant" : tenantId.Trim();
        return $"M365 - {clienteId} - {tenant}";
    }

    private static string ReadOrganizationDisplayName(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return "";

        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("value", out var value)
            || value.ValueKind != JsonValueKind.Array
            || value.GetArrayLength() == 0)
        {
            return "";
        }

        return ReadString(value[0], "displayName").Trim();
    }

    private static string NormalizeTenantHint(string? raw)
    {
        var value = raw?.Trim() ?? "";
        return string.IsNullOrWhiteSpace(value) ? "organizations" : value;
    }

    private static bool IsAdminConsentGranted(string? raw) =>
        string.Equals(raw?.Trim(), "true", StringComparison.OrdinalIgnoreCase);

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

    private static string NormalizeAuthorityHost(string? raw)
    {
        var authority = string.IsNullOrWhiteSpace(raw)
            ? "https://login.microsoftonline.com"
            : raw.Trim();

        return authority.TrimEnd('/');
    }

    private static string BuildQueryString(IReadOnlyDictionary<string, string> values) =>
        string.Join("&", values.Select(pair =>
            $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"));

    private static string EscapeOdataLiteral(string value) =>
        (value ?? string.Empty).Replace("'", "''");

    private static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? "";

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

    private static string Truncate(string value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";

        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }

    private sealed class M365ConsentState
    {
        public string ClienteId { get; set; } = "";
        public string TenantHint { get; set; } = "";
        public List<string> RequestedScopes { get; set; } = new();
        public List<string> RequestedPermissions { get; set; } = new();
        public DateTimeOffset IssuedAtUtc { get; set; }
        public string Nonce { get; set; } = "";
    }
}
