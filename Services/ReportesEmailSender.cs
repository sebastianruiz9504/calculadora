using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using CotizadorInterno.Web.Models.Reportes;
using Microsoft.Extensions.Options;
using Microsoft.Identity.Client;
using Microsoft.Identity.Web;

namespace CotizadorInterno.Web.Services;

public sealed class ReportesEmailMessage
{
    public IReadOnlyList<string> To { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> Cc { get; set; } = Array.Empty<string>();
    public string Subject { get; set; } = "";
    public string HtmlBody { get; set; } = "";
    public IReadOnlyList<ReporteEmailAttachment> Attachments { get; set; } = Array.Empty<ReporteEmailAttachment>();
}

public sealed class ReportesEmailSendResult
{
    public string Sender { get; set; } = "";
    public string Mode { get; set; } = "";
}

public interface IReportesEmailSender
{
    Task<ReportesEmailSendResult> SendAsync(ReportesEmailMessage message, ClaimsPrincipal user, CancellationToken ct = default);
}

public sealed class ReportesEmailSender : IReportesEmailSender
{
    public const string MailSendScope = "Mail.Send";
    private const string GraphDefaultScope = "https://graph.microsoft.com/.default";
    private static readonly string[] DelegatedGraphScopes = { MailSendScope };
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ReportesOptions _reportesOptions;
    private readonly M365Options _m365Options;
    private readonly IConfiguration _configuration;
    private readonly ITokenAcquisition _tokenAcquisition;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<ReportesEmailSender> _logger;

    public ReportesEmailSender(
        IOptions<ReportesOptions> reportesOptions,
        IOptions<M365Options> m365Options,
        IConfiguration configuration,
        ITokenAcquisition tokenAcquisition,
        IHttpClientFactory httpClientFactory,
        ILogger<ReportesEmailSender> logger)
    {
        _reportesOptions = reportesOptions.Value;
        _m365Options = m365Options.Value;
        _configuration = configuration;
        _tokenAcquisition = tokenAcquisition;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<ReportesEmailSendResult> SendAsync(
        ReportesEmailMessage message,
        ClaimsPrincipal user,
        CancellationToken ct = default)
    {
        ValidateMessage(message);
        if (_reportesOptions.Email.UseSignedInUserSender && user.Identity?.IsAuthenticated == true)
            return await SendWithDelegatedGraphAsync(message, user, ct);

        return await SendWithAppGraphAsync(message, ct);
    }

    private async Task<ReportesEmailSendResult> SendWithDelegatedGraphAsync(
        ReportesEmailMessage message,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var token = await _tokenAcquisition.GetAccessTokenForUserAsync(DelegatedGraphScopes, user: user);
        var client = _httpClientFactory.CreateClient();
        var url = BuildGraphUri("/me/sendMail");
        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = new StringContent(JsonSerializer.Serialize(BuildGraphSendMailPayload(message), JsonOptions), Encoding.UTF8, "application/json");

        using var response = await client.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "Graph sendMail delegado de reportes fallo para {Sender}. Status {StatusCode}. Body: {Body}",
                ResolveUserSender(user),
                (int)response.StatusCode,
                body);
            throw new InvalidOperationException($"Microsoft Graph no pudo enviar el correo del reporte con el usuario autenticado: {(int)response.StatusCode} {response.ReasonPhrase}. {Truncate(body, 1200)}");
        }

        return new ReportesEmailSendResult
        {
            Sender = ResolveUserSender(user),
            Mode = "delegated"
        };
    }

    private async Task<ReportesEmailSendResult> SendWithAppGraphAsync(
        ReportesEmailMessage message,
        CancellationToken ct)
    {
        var sender = FirstNonEmpty(_reportesOptions.Email.SenderUserPrincipalName, _configuration["FinancialReconciliation:SenderUserPrincipalName"]);
        if (string.IsNullOrWhiteSpace(sender))
            throw new InvalidOperationException("Configura Reportes:Email:SenderUserPrincipalName para enviar el correo por Microsoft Graph.");

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
                "Graph sendMail de reportes fallo para {Sender}. Status {StatusCode}. Body: {Body}",
                sender,
                (int)response.StatusCode,
                body);
            throw new InvalidOperationException($"Microsoft Graph no pudo enviar el correo del reporte: {(int)response.StatusCode} {response.ReasonPhrase}. {Truncate(body, 1200)}");
        }

        return new ReportesEmailSendResult
        {
            Sender = sender,
            Mode = "app-only"
        };
    }

    private object BuildGraphSendMailPayload(ReportesEmailMessage message)
    {
        var graphMessage = new Dictionary<string, object?>
        {
            ["subject"] = message.Subject,
            ["body"] = new
            {
                contentType = "HTML",
                content = message.HtmlBody
            },
            ["toRecipients"] = BuildGraphRecipients(message.To)
        };

        var ccRecipients = BuildGraphRecipients(message.Cc);
        if (ccRecipients.Length > 0)
            graphMessage["ccRecipients"] = ccRecipients;

        var attachments = message.Attachments
            .Where(static attachment => attachment.Content.Length > 0)
            .Select(static attachment => new Dictionary<string, object?>
            {
                ["@odata.type"] = "#microsoft.graph.fileAttachment",
                ["name"] = attachment.FileName,
                ["contentType"] = FirstNonEmpty(attachment.ContentType, "application/octet-stream"),
                ["contentBytes"] = Convert.ToBase64String(attachment.Content)
            })
            .ToArray();

        if (attachments.Length > 0)
            graphMessage["attachments"] = attachments;

        return new
        {
            message = graphMessage,
            saveToSentItems = true
        };
    }

    private static object[] BuildGraphRecipients(IEnumerable<string> emails) =>
        emails
            .Where(static email => !string.IsNullOrWhiteSpace(email))
            .Select(static email => new
            {
                emailAddress = new
                {
                    address = email
                }
            })
            .ToArray<object>();

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

    private static void ValidateMessage(ReportesEmailMessage message)
    {
        if (message.To.Count == 0 || message.To.All(static value => string.IsNullOrWhiteSpace(value)))
            throw new InvalidOperationException("El correo de destino del reporte esta vacio.");

        if (string.IsNullOrWhiteSpace(message.Subject))
            throw new InvalidOperationException("El asunto del correo del reporte esta vacio.");

        foreach (var attachment in message.Attachments.Where(static item => item.Content.Length > 0))
        {
            if (string.IsNullOrWhiteSpace(attachment.FileName))
                throw new InvalidOperationException("Uno de los adjuntos del reporte no tiene nombre.");
        }
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

    private static string ResolveUserSender(ClaimsPrincipal user) =>
        FirstNonEmpty(
            user.FindFirst("preferred_username")?.Value,
            user.FindFirst(ClaimTypes.Upn)?.Value,
            user.FindFirst(ClaimTypes.Email)?.Value,
            user.Identity?.Name,
            "usuario autenticado");

    private static string Truncate(string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
            return value;

        return value[..maxLength];
    }
}
