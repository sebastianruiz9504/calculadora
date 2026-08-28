using System.Net.Http.Headers;
using Azure.Core;
using Azure.Identity;

namespace CotizadorInterno.Web.Services.CopiersMtoV2;

/// <summary>
/// Uses a dedicated application identity for V2 persistence. Production can
/// select an explicit user-assigned managed identity; a client secret remains
/// available only for controlled non-Azure environments.
/// </summary>
public sealed class CopiersMtoV2ApplicationDataverseClient : ICopiersMtoV2ApplicationDataverseClient
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<CopiersMtoV2ApplicationDataverseClient> _logger;
    private readonly string _baseUrl;
    private readonly TokenCredential? _credential;

    public CopiersMtoV2ApplicationDataverseClient(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<CopiersMtoV2ApplicationDataverseClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _baseUrl = (configuration["CopiersMtoV2:DataverseApp:BaseUrl"] ?? "").TrimEnd('/');
        // V2 must use its dedicated Dataverse application identity. Reusing the
        // interactive AzureAd registration would silently widen the trust boundary.
        var managedIdentityClientId =
            (configuration["CopiersMtoV2:DataverseApp:ManagedIdentityClientId"] ?? "").Trim();
        var tenantId = (configuration["CopiersMtoV2:DataverseApp:TenantId"] ?? "").Trim();
        var clientId = (configuration["CopiersMtoV2:DataverseApp:ClientId"] ?? "").Trim();
        var clientSecret = (configuration["CopiersMtoV2:DataverseApp:ClientSecret"] ?? "").Trim();
        if (Guid.TryParse(managedIdentityClientId, out var parsedManagedIdentityClientId)
            && parsedManagedIdentityClientId != Guid.Empty)
        {
            _credential = new ManagedIdentityCredential(
                ManagedIdentityId.FromUserAssignedClientId(parsedManagedIdentityClientId.ToString("D")));
        }
        else if (!string.IsNullOrWhiteSpace(tenantId)
            && !string.IsNullOrWhiteSpace(clientId)
            && !string.IsNullOrWhiteSpace(clientSecret))
        {
            _credential = new ClientSecretCredential(tenantId, clientId, clientSecret);
        }
    }

    public async Task<HttpResponseMessage> SendAsync(
        string relativeUrl,
        HttpMethod method,
        HttpContent? content,
        Action<HttpRequestMessage>? customizeRequest,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_baseUrl) || _credential is null)
        {
            throw new CopiersMaintenanceV2PersistenceException(
                "La identidad de aplicación para Dataverse V2 no está configurada. " +
                "Configura BaseUrl y ManagedIdentityClientId, o las credenciales app-only completas, en el entorno.");
        }

        AccessToken token;
        try
        {
            token = await _credential.GetTokenAsync(
                new TokenRequestContext(new[] { $"{_baseUrl}/.default" }),
                ct);
        }
        catch (AuthenticationFailedException ex)
        {
            _logger.LogError(ex, "Falló la autenticación app-only de MTO Firmado V2.");
            throw new CopiersMaintenanceV2PersistenceException(
                "No fue posible autenticar la identidad de aplicación de MTO Firmado V2.");
        }

        var target = Uri.TryCreate(relativeUrl, UriKind.Absolute, out var absolute)
            ? absolute
            : new Uri($"{_baseUrl}/{relativeUrl.TrimStart('/')}", UriKind.Absolute);
        using var request = new HttpRequestMessage(method, target)
        {
            Content = content
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.TryAddWithoutValidation("OData-MaxVersion", "4.0");
        request.Headers.TryAddWithoutValidation("OData-Version", "4.0");
        customizeRequest?.Invoke(request);

        try
        {
            return await _httpClientFactory.CreateClient().SendAsync(request, ct);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Falló el transporte app-only de MTO Firmado V2.");
            throw new CopiersMaintenanceV2PersistenceException(
                "No fue posible comunicarse con Dataverse para guardar el MTO Firmado V2.");
        }
    }
}

