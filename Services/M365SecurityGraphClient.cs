using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using CotizadorInterno.Web.Models.M365;
using Microsoft.Extensions.Options;
using Microsoft.Identity.Client;

namespace CotizadorInterno.Web.Services;

public sealed class M365SecurityGraphClient : IM365SecurityGraphClient
{
    private const string GraphDefaultScope = "https://graph.microsoft.com/.default";
    private readonly M365Options _options;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<M365SecurityGraphClient> _logger;

    public M365SecurityGraphClient(
        IOptions<M365Options> options,
        IHttpClientFactory httpClientFactory,
        ILogger<M365SecurityGraphClient> logger)
    {
        _options = options.Value;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<M365SecurityGraphData> CollectSecurityDataAsync(
        string tenantId,
        DateTimeOffset periodStartUtc,
        DateTimeOffset periodEndExclusiveUtc,
        CancellationToken ct = default)
    {
        EnsureGraphCredentialConfiguration();
        var normalizedTenantId = NormalizeRequiredText(tenantId, nameof(tenantId));
        var token = await GetGraphAccessTokenAsync(normalizedTenantId, ct);

        var secureScoresTask = GetGraphCollectionAsync(token, "/security/secureScores?$top=50", ct);
        var controlProfilesTask = GetGraphCollectionAsync(token, "/security/secureScoreControlProfiles?$top=100", ct);
        var alertsTask = GetGraphCollectionAsync(token, BuildMonthlyFilterPath("/security/alerts_v2", periodStartUtc, periodEndExclusiveUtc), ct);
        var incidentsTask = GetGraphCollectionAsync(token, BuildMonthlyFilterPath("/security/incidents", periodStartUtc, periodEndExclusiveUtc), ct);

        await Task.WhenAll(secureScoresTask, controlProfilesTask, alertsTask, incidentsTask);

        var secureScores = secureScoresTask.Result;
        var latestSecureScore = secureScores
            .Select(ParseSecureScore)
            .OrderByDescending(score => ParseDateOrMin(score.CreatedDateTime))
            .FirstOrDefault() ?? new M365SecureScoreSnapshot();

        var controlProfiles = controlProfilesTask.Result;
        var alerts = alertsTask.Result.Select(ParseAlertSummary).ToList();
        var incidents = incidentsTask.Result.Select(ParseIncidentSummary).ToList();

        _logger.LogInformation(
            "Datos de seguridad M365 recolectados para tenant {TenantId}: {AlertCount} alertas, {IncidentCount} incidentes.",
            normalizedTenantId,
            alerts.Count,
            incidents.Count);

        return new M365SecurityGraphData
        {
            SecureScore = latestSecureScore,
            TopRecommendations = BuildTopRecommendations(latestSecureScore, controlProfiles),
            Alerts = alerts,
            Incidents = incidents,
            RawAlerts = alertsTask.Result,
            RawIncidents = incidentsTask.Result
        };
    }

    private async Task<IReadOnlyList<JsonElement>> GetGraphCollectionAsync(
        string token,
        string relativePath,
        CancellationToken ct)
    {
        const int maxPages = 30;
        var pageCount = 0;
        var items = new List<JsonElement>();
        string? nextUrl = BuildGraphUri(relativePath).ToString();
        var client = _httpClientFactory.CreateClient();

        while (!string.IsNullOrWhiteSpace(nextUrl))
        {
            pageCount++;
            if (pageCount > maxPages)
                throw new InvalidOperationException("Se alcanzo el limite de paginas consultando Microsoft Graph.");

            using var request = new HttpRequestMessage(HttpMethod.Get, nextUrl);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            using var response = await client.SendAsync(request, ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException(
                    $"Graph devolvio {(int)response.StatusCode} {response.ReasonPhrase} en {nextUrl}. {Truncate(body, 1200)}".Trim());
            }

            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("value", out var value)
                && value.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in value.EnumerateArray())
                {
                    items.Add(item.Clone());
                }
            }

            nextUrl = doc.RootElement.TryGetProperty("@odata.nextLink", out var nextLinkProperty)
                ? nextLinkProperty.GetString()
                : null;
        }

        return items;
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

    private M365SecureScoreSnapshot ParseSecureScore(JsonElement item)
    {
        return new M365SecureScoreSnapshot
        {
            Id = ReadString(item, "id").Trim(),
            CreatedDateTime = ReadString(item, "createdDateTime").Trim(),
            CurrentScore = ReadDecimal(item, "currentScore") ?? 0m,
            MaxScore = ReadDecimal(item, "maxScore") ?? 0m,
            ControlScores = ReadArray(item, "controlScores")
                .Select(control => new M365SecureScoreControlScore
                {
                    ControlName = FirstNonEmpty(
                        ReadString(control, "controlName"),
                        ReadString(control, "id")).Trim(),
                    Score = ReadDecimal(control, "score") ?? 0m,
                    MaxScore = ReadDecimal(control, "maxScore") ?? 0m
                })
                .Where(control => !string.IsNullOrWhiteSpace(control.ControlName))
                .ToList()
        };
    }

    private static M365SecurityAlertSummary ParseAlertSummary(JsonElement item)
    {
        return new M365SecurityAlertSummary
        {
            Id = ReadString(item, "id").Trim(),
            CreatedDateTime = ReadString(item, "createdDateTime").Trim(),
            Title = FirstNonEmpty(ReadString(item, "title"), ReadString(item, "displayName")).Trim(),
            Severity = ReadString(item, "severity").Trim(),
            Status = ReadString(item, "status").Trim(),
            ServiceSource = ReadString(item, "serviceSource").Trim()
        };
    }

    private static M365SecurityIncidentSummary ParseIncidentSummary(JsonElement item)
    {
        return new M365SecurityIncidentSummary
        {
            Id = ReadString(item, "id").Trim(),
            CreatedDateTime = ReadString(item, "createdDateTime").Trim(),
            DisplayName = FirstNonEmpty(ReadString(item, "displayName"), ReadString(item, "title")).Trim(),
            Severity = ReadString(item, "severity").Trim(),
            Status = ReadString(item, "status").Trim()
        };
    }

    private static IReadOnlyList<M365SecurityRecommendation> BuildTopRecommendations(
        M365SecureScoreSnapshot secureScore,
        IReadOnlyList<JsonElement> controlProfiles)
    {
        var controlScores = secureScore.ControlScores
            .Where(control => !string.IsNullOrWhiteSpace(control.ControlName))
            .GroupBy(control => control.ControlName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        var recommendations = controlProfiles
            .Select(profile =>
            {
                var id = FirstNonEmpty(ReadString(profile, "id"), ReadString(profile, "controlName")).Trim();
                controlScores.TryGetValue(id, out var controlScore);
                var profileMaxScore = ReadDecimal(profile, "maxScore") ?? 0m;
                var maxScore = Math.Max(profileMaxScore, controlScore?.MaxScore ?? 0m);
                var currentScore = controlScore?.Score ?? 0m;

                return new M365SecurityRecommendation
                {
                    Id = id,
                    Title = FirstNonEmpty(ReadString(profile, "title"), id).Trim(),
                    Category = FirstNonEmpty(ReadString(profile, "category"), ReadString(profile, "controlCategory")).Trim(),
                    ActionType = ReadString(profile, "actionType").Trim(),
                    Remediation = ReadString(profile, "remediation").Trim(),
                    UserImpact = ReadString(profile, "userImpact").Trim(),
                    ImplementationCost = ReadString(profile, "implementationCost").Trim(),
                    CurrentScore = currentScore,
                    MaxScore = maxScore,
                    ScoreGap = Math.Max(0m, maxScore - currentScore),
                    Rank = ReadInt(profile, "rank") ?? 0
                };
            })
            .Where(item => !string.IsNullOrWhiteSpace(item.Id) || !string.IsNullOrWhiteSpace(item.Title))
            .ToList();

        if (recommendations.Count == 0)
        {
            recommendations = secureScore.ControlScores
                .Select(control => new M365SecurityRecommendation
                {
                    Id = control.ControlName,
                    Title = control.ControlName,
                    CurrentScore = control.Score,
                    MaxScore = control.MaxScore,
                    ScoreGap = Math.Max(0m, control.MaxScore - control.Score)
                })
                .ToList();
        }

        return recommendations
            .OrderByDescending(item => item.ScoreGap)
            .ThenByDescending(item => item.MaxScore)
            .ThenBy(item => item.Rank <= 0 ? int.MaxValue : item.Rank)
            .ThenBy(item => item.Title, StringComparer.OrdinalIgnoreCase)
            .Take(10)
            .ToList();
    }

    private Uri BuildGraphUri(string relativePath)
    {
        if (Uri.TryCreate(relativePath, UriKind.Absolute, out var absoluteUri))
            return absoluteUri;

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

    private void EnsureGraphCredentialConfiguration()
    {
        if (string.IsNullOrWhiteSpace(_options.ClientId))
            throw new InvalidOperationException("M365:ClientId no esta configurado.");

        if (!string.IsNullOrWhiteSpace(_options.ClientSecret)
            || !string.IsNullOrWhiteSpace(_options.CertificatePath)
            || !string.IsNullOrWhiteSpace(_options.CertificateThumbprint))
        {
            return;
        }

        throw new InvalidOperationException("M365 requiere configurar ClientSecret o certificado para consultar Microsoft Graph.");
    }

    private string[] ResolveGraphTokenScopes()
    {
        var defaultScope = (_options.Scopes ?? Array.Empty<string>())
            .Select(scope => scope?.Trim())
            .Where(scope => !string.IsNullOrWhiteSpace(scope))
            .Select(scope => scope!)
            .FirstOrDefault(scope => scope.EndsWith("/.default", StringComparison.OrdinalIgnoreCase));

        return new[] { defaultScope ?? GraphDefaultScope };
    }

    private static string BuildMonthlyFilterPath(
        string path,
        DateTimeOffset periodStartUtc,
        DateTimeOffset periodEndExclusiveUtc)
    {
        var filter =
            $"createdDateTime ge {FormatGraphUtc(periodStartUtc)} and createdDateTime lt {FormatGraphUtc(periodEndExclusiveUtc)}";
        return $"{path}?$filter={Uri.EscapeDataString(filter)}&$top=100";
    }

    private static string FormatGraphUtc(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    private static IReadOnlyList<JsonElement> ReadArray(JsonElement item, string propertyName)
    {
        if (!item.TryGetProperty(propertyName, out var property)
            || property.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<JsonElement>();
        }

        return property.EnumerateArray().Select(element => element.Clone()).ToList();
    }

    private static string ReadString(JsonElement item, string propertyName)
    {
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

    private static DateTimeOffset ParseDateOrMin(string value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed.ToUniversalTime()
            : DateTimeOffset.MinValue;

    private static string NormalizeRequiredText(string? value, string paramName)
    {
        var normalized = value?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(normalized))
            throw new InvalidOperationException($"El valor de {paramName} es obligatorio.");

        return normalized;
    }

    private static string NormalizeAuthorityHost(string? raw)
    {
        var authority = string.IsNullOrWhiteSpace(raw)
            ? "https://login.microsoftonline.com"
            : raw.Trim();

        return authority.TrimEnd('/');
    }

    private static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? "";

    private static string Truncate(string value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";

        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }
}
