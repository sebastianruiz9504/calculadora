using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Microsoft.Identity.Client;

namespace CotizadorInterno.Web.Services;

public sealed class ReconciliationEmailMessage
{
    public string To { get; set; } = "";
    public string Subject { get; set; } = "";
    public string HtmlBody { get; set; } = "";
    public string AttachmentFileName { get; set; } = "";
    public string AttachmentContentType { get; set; } = "application/octet-stream";
    public byte[] AttachmentContent { get; set; } = Array.Empty<byte>();
}

public interface IReconciliationReportSender
{
    Task SendAsync(ReconciliationEmailMessage message, CancellationToken ct = default);
}

public sealed class ReconciliationReportSender : IReconciliationReportSender
{
    private const string GraphDefaultScope = "https://graph.microsoft.com/.default";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly FinancialReconciliationOptions _reconciliationOptions;
    private readonly M365Options _m365Options;
    private readonly IConfiguration _configuration;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<ReconciliationReportSender> _logger;

    public ReconciliationReportSender(
        IOptions<FinancialReconciliationOptions> reconciliationOptions,
        IOptions<M365Options> m365Options,
        IConfiguration configuration,
        IHttpClientFactory httpClientFactory,
        ILogger<ReconciliationReportSender> logger)
    {
        _reconciliationOptions = reconciliationOptions.Value;
        _m365Options = m365Options.Value;
        _configuration = configuration;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task SendAsync(ReconciliationEmailMessage message, CancellationToken ct = default)
    {
        ValidateMessage(message);
        if (!string.IsNullOrWhiteSpace(_reconciliationOptions.EmailFlowUrl))
        {
            await SendWithFlowAsync(message, ct);
            return;
        }

        await SendWithGraphAsync(message, ct);
    }

    private async Task SendWithFlowAsync(ReconciliationEmailMessage message, CancellationToken ct)
    {
        var client = _httpClientFactory.CreateClient();
        var attachment = new
        {
            fileName = message.AttachmentFileName,
            contentType = message.AttachmentContentType,
            base64 = Convert.ToBase64String(message.AttachmentContent)
        };
        var payload = new
        {
            recipientEmail = message.To,
            subject = message.Subject,
            htmlBody = message.HtmlBody,
            attachmentName = message.AttachmentFileName,
            attachmentContentType = message.AttachmentContentType,
            attachmentContentBytes = Convert.ToBase64String(message.AttachmentContent),
            attachment,
            attachments = new[] { attachment }
        };

        using var response = await client.PostAsJsonAsync(_reconciliationOptions.EmailFlowUrl, payload, JsonOptions, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"El flujo de correo de conciliacion respondio {(int)response.StatusCode}: {Truncate(body, 1200)}");
    }

    private async Task SendWithGraphAsync(ReconciliationEmailMessage message, CancellationToken ct)
    {
        var sender = FirstNonEmpty(_reconciliationOptions.SenderUserPrincipalName, message.To);
        if (string.IsNullOrWhiteSpace(sender))
            throw new InvalidOperationException("Configura FinancialReconciliation:SenderUserPrincipalName para enviar el correo por Microsoft Graph.");

        var token = await GetGraphAccessTokenAsync(ct);
        var client = _httpClientFactory.CreateClient();
        var url = BuildGraphUri($"/users/{Uri.EscapeDataString(sender)}/sendMail");
        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = new StringContent(JsonSerializer.Serialize(BuildGraphSendMailPayload(message), JsonOptions), Encoding.UTF8, "application/json");

        using var response = await client.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "Graph sendMail fallo para {Sender}. Status {StatusCode}. Body: {Body}",
                sender,
                (int)response.StatusCode,
                body);
            throw new InvalidOperationException($"Microsoft Graph no pudo enviar el correo de conciliacion: {(int)response.StatusCode} {response.ReasonPhrase}. {Truncate(body, 1200)}");
        }
    }

    private object BuildGraphSendMailPayload(ReconciliationEmailMessage message)
    {
        var attachment = new Dictionary<string, object?>
        {
            ["@odata.type"] = "#microsoft.graph.fileAttachment",
            ["name"] = message.AttachmentFileName,
            ["contentType"] = message.AttachmentContentType,
            ["contentBytes"] = Convert.ToBase64String(message.AttachmentContent)
        };

        return new
        {
            message = new
            {
                subject = message.Subject,
                body = new
                {
                    contentType = "HTML",
                    content = message.HtmlBody
                },
                toRecipients = new[]
                {
                    new
                    {
                        emailAddress = new
                        {
                            address = message.To
                        }
                    }
                },
                attachments = new object[] { attachment }
            },
            saveToSentItems = true
        };
    }

    private async Task<string> GetGraphAccessTokenAsync(CancellationToken ct)
    {
        var tenantId = FirstNonEmpty(_configuration["M365:TenantId"], _configuration["AzureAd:TenantId"]);
        var clientId = FirstNonEmpty(_m365Options.ClientId, _configuration["AzureAd:ClientId"]);
        var clientSecret = FirstNonEmpty(_m365Options.ClientSecret, _configuration["AzureAd:ClientSecret"]);
        if (string.IsNullOrWhiteSpace(tenantId) || string.IsNullOrWhiteSpace(clientId))
            throw new InvalidOperationException("Configura M365:ClientId y AzureAd:TenantId para enviar el informe por Microsoft Graph.");

        var builder = ConfidentialClientApplicationBuilder
            .Create(clientId)
            .WithAuthority($"{NormalizeAuthorityHost(_m365Options.AuthorityHost)}/{tenantId}");
        var certificate = LoadCertificateOrDefault();
        if (certificate is not null)
        {
            builder.WithCertificate(certificate);
        }
        else if (!string.IsNullOrWhiteSpace(clientSecret))
        {
            builder.WithClientSecret(clientSecret);
        }
        else
        {
            throw new InvalidOperationException("Configura M365:ClientSecret, AzureAd:ClientSecret o un certificado M365 para enviar el informe por Microsoft Graph.");
        }

        try
        {
            var app = builder.Build();
            var result = await app.AcquireTokenForClient(ResolveGraphTokenScopes()).ExecuteAsync(ct);
            return result.AccessToken;
        }
        catch (MsalException ex)
        {
            throw new InvalidOperationException("No fue posible obtener token app-only de Microsoft Graph para enviar el informe.", ex);
        }
    }

    private Uri BuildGraphUri(string relativePath)
    {
        if (Uri.TryCreate(relativePath, UriKind.Absolute, out var absoluteUri))
            return absoluteUri;

        var baseUrl = string.IsNullOrWhiteSpace(_m365Options.GraphBaseUrl)
            ? "https://graph.microsoft.com/v1.0"
            : _m365Options.GraphBaseUrl.TrimEnd('/');
        var normalizedPath = relativePath.StartsWith("/", StringComparison.Ordinal)
            ? relativePath
            : $"/{relativePath}";

        return new Uri($"{baseUrl}{normalizedPath}", UriKind.Absolute);
    }

    private X509Certificate2? LoadCertificateOrDefault()
    {
        if (!string.IsNullOrWhiteSpace(_m365Options.CertificatePath))
        {
            return X509CertificateLoader.LoadPkcs12FromFile(
                _m365Options.CertificatePath,
                _m365Options.CertificatePassword,
                X509KeyStorageFlags.MachineKeySet | X509KeyStorageFlags.EphemeralKeySet);
        }

        if (string.IsNullOrWhiteSpace(_m365Options.CertificateThumbprint))
            return null;

        if (!Enum.TryParse<StoreName>(_m365Options.CertificateStoreName, ignoreCase: true, out var storeName))
            storeName = StoreName.My;

        if (!Enum.TryParse<StoreLocation>(_m365Options.CertificateStoreLocation, ignoreCase: true, out var storeLocation))
            storeLocation = StoreLocation.CurrentUser;

        using var store = new X509Store(storeName, storeLocation);
        store.Open(OpenFlags.ReadOnly);
        var certificates = store.Certificates.Find(
            X509FindType.FindByThumbprint,
            _m365Options.CertificateThumbprint.Trim(),
            validOnly: false);

        return certificates.Count == 0 ? null : certificates[0];
    }

    private string[] ResolveGraphTokenScopes()
    {
        var scopes = _m365Options.Scopes?
            .Where(static scope => !string.IsNullOrWhiteSpace(scope))
            .Select(static scope => scope.Trim())
            .ToArray();

        return scopes is { Length: > 0 } ? scopes : new[] { GraphDefaultScope };
    }

    private static void ValidateMessage(ReconciliationEmailMessage message)
    {
        if (string.IsNullOrWhiteSpace(message.To))
            throw new InvalidOperationException("El correo de destino de conciliacion esta vacio.");

        if (string.IsNullOrWhiteSpace(message.Subject))
            throw new InvalidOperationException("El asunto del correo de conciliacion esta vacio.");

        if (message.AttachmentContent.Length == 0)
            throw new InvalidOperationException("El informe de conciliacion no tiene contenido para adjuntar.");
    }

    private static string NormalizeAuthorityHost(string? value)
    {
        var authority = string.IsNullOrWhiteSpace(value)
            ? "https://login.microsoftonline.com"
            : value.Trim();

        return authority.TrimEnd('/');
    }

    private static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? "";

    private static string Truncate(string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
            return value;

        return value[..maxLength];
    }
}
