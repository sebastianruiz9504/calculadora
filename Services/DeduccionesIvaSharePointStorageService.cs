using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using CotizadorInterno.Web.Models.Conciliacion;
using Microsoft.Extensions.Options;
using Microsoft.Identity.Client;

namespace CotizadorInterno.Web.Services;

public sealed class DeduccionesIvaSharePointStorageService : IDeduccionesIvaSharePointStorageService
{
    private const string GraphDefaultScope = "https://graph.microsoft.com/.default";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly DeduccionesIvaImportOptions _options;
    private readonly M365Options _m365Options;
    private readonly IConfiguration _configuration;
    private readonly IHttpClientFactory _httpClientFactory;

    public DeduccionesIvaSharePointStorageService(
        IOptions<DeduccionesIvaImportOptions> options,
        IOptions<M365Options> m365Options,
        IConfiguration configuration,
        IHttpClientFactory httpClientFactory)
    {
        _options = options.Value;
        _m365Options = m365Options.Value;
        _configuration = configuration;
        _httpClientFactory = httpClientFactory;
    }

    public async Task<DeduccionesIvaSharePointUploadResult> UploadAsync(
        string originalFileName,
        string? contentType,
        Stream content,
        CancellationToken ct = default)
    {
        var storedFileName = BuildStoredFileName(originalFileName);
        if (!_options.UploadToSharePoint)
        {
            return new DeduccionesIvaSharePointUploadResult(
                false,
                storedFileName,
                _options.FolderPath,
                BuildFallbackWebUrl(storedFileName));
        }

        if (string.IsNullOrWhiteSpace(_options.DriveId))
            throw new InvalidOperationException("Configura DeduccionesIvaImport:DriveId para guardar los exportables en SharePoint.");

        var token = await GetGraphAccessTokenAsync(ct);
        var client = _httpClientFactory.CreateClient();
        await EnsureFolderPathAsync(client, token, _options.FolderPath, ct);

        if (content.CanSeek)
            content.Position = 0;
        using var uploadBuffer = new MemoryStream();
        await content.CopyToAsync(uploadBuffer, ct);
        uploadBuffer.Position = 0;

        var itemPath = BuildDrivePath(_options.FolderPath, storedFileName);
        using var request = new HttpRequestMessage(
            HttpMethod.Put,
            BuildGraphUri($"/drives/{Uri.EscapeDataString(_options.DriveId)}/root:/{itemPath}:/content"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = new StreamContent(uploadBuffer);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue(ResolveContentType(contentType, storedFileName));

        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Microsoft Graph no pudo guardar el Excel de deducciones IVA: {(int)response.StatusCode} {response.ReasonPhrase}. {Truncate(body, 1200)}");

        var webUrl = ReadStringProperty(body, "webUrl");
        return new DeduccionesIvaSharePointUploadResult(
            true,
            storedFileName,
            _options.FolderPath,
            FirstNonEmpty(webUrl, BuildFallbackWebUrl(storedFileName)));
    }

    public async Task SaveImportHistoryAsync(
        DeduccionesIvaImportHistoryManifestDto manifest,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        if (!_options.UploadToSharePoint)
            return;
        if (string.IsNullOrWhiteSpace(_options.DriveId))
            throw new InvalidOperationException("Configura DeduccionesIvaImport:DriveId para guardar el historico de importaciones.");
        if (string.IsNullOrWhiteSpace(manifest.ImportId))
            throw new InvalidOperationException("El historico de importacion requiere un identificador durable.");

        var token = await GetGraphAccessTokenAsync(ct);
        var client = _httpClientFactory.CreateClient();
        var historyFolderPath = BuildHistoryFolderPath();
        await EnsureFolderPathAsync(client, token, historyFolderPath, ct);

        var stamp = manifest.ImportedAtUtc.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var historyFileName = $"import-{stamp}-{SanitizeFileName(manifest.ImportId)}.json";
        var itemPath = BuildDrivePath(historyFolderPath, historyFileName);
        using var request = new HttpRequestMessage(
            HttpMethod.Put,
            BuildGraphUri($"/drives/{Uri.EscapeDataString(_options.DriveId)}/root:/{itemPath}:/content"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = new StringContent(
            JsonSerializer.Serialize(manifest, JsonOptions),
            Encoding.UTF8,
            "application/json");

        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException(
                $"Microsoft Graph no pudo guardar el historico de deducciones IVA: {(int)response.StatusCode} {response.ReasonPhrase}. {Truncate(body, 1200)}");
        }
    }

    public async Task<DeduccionesIvaStoredFile> DownloadAsync(
        string storedFileName,
        CancellationToken ct = default)
    {
        var normalizedName = Path.GetFileName(storedFileName ?? "");
        if (string.IsNullOrWhiteSpace(normalizedName))
            throw new InvalidOperationException("El historico no tiene un nombre de archivo DIAN valido.");
        if (!_options.UploadToSharePoint || string.IsNullOrWhiteSpace(_options.DriveId))
            throw new InvalidOperationException("El almacenamiento SharePoint de deducciones IVA no esta habilitado.");

        var token = await GetGraphAccessTokenAsync(ct);
        var client = _httpClientFactory.CreateClient();
        var itemPath = BuildDrivePath(_options.FolderPath, normalizedName);
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            BuildGraphUri($"/drives/{Uri.EscapeDataString(_options.DriveId)}/root:/{itemPath}:/content"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException(
                $"Microsoft Graph no pudo descargar el Excel historico de deducciones IVA: "
                + $"{(int)response.StatusCode} {response.ReasonPhrase}. {Truncate(body, 1200)}");
        }

        return new DeduccionesIvaStoredFile(
            normalizedName,
            await response.Content.ReadAsByteArrayAsync(ct));
    }

    public async Task<IReadOnlyList<DeduccionesIvaImportHistoryManifestDto>> GetImportHistoryAsync(
        int top = 25,
        CancellationToken ct = default)
    {
        if (!_options.UploadToSharePoint || string.IsNullOrWhiteSpace(_options.DriveId))
            return Array.Empty<DeduccionesIvaImportHistoryManifestDto>();

        var requestedTop = Math.Clamp(top, 1, 100);
        var token = await GetGraphAccessTokenAsync(ct);
        var client = _httpClientFactory.CreateClient();
        var historyFolderPath = BuildDrivePath(BuildHistoryFolderPath());
        var relativePath =
            $"/drives/{Uri.EscapeDataString(_options.DriveId)}/root:/{historyFolderPath}:/children"
            + $"?$select=id,name,lastModifiedDateTime&$top={requestedTop.ToString(CultureInfo.InvariantCulture)}";
        using var request = new HttpRequestMessage(HttpMethod.Get, BuildGraphUri(relativePath));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return Array.Empty<DeduccionesIvaImportHistoryManifestDto>();

        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Microsoft Graph no pudo consultar el historico de deducciones IVA: {(int)response.StatusCode} {response.ReasonPhrase}. {Truncate(body, 1200)}");
        }

        var items = ReadHistoryDriveItems(body)
            .Where(static item => item.Name.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(static item => item.LastModifiedDateTime)
            .Take(requestedTop)
            .ToArray();
        var manifests = new List<DeduccionesIvaImportHistoryManifestDto>(items.Length);
        foreach (var item in items)
        {
            var manifest = await ReadImportHistoryManifestAsync(client, token, item.Id, ct);
            if (manifest is not null)
                manifests.Add(manifest);
        }

        return manifests
            .OrderByDescending(static item => item.ImportedAtUtc)
            .Take(requestedTop)
            .ToArray();
    }

    private async Task EnsureFolderPathAsync(
        HttpClient client,
        string token,
        string folderPath,
        CancellationToken ct)
    {
        var segments = SplitDrivePath(folderPath);
        if (segments.Count == 0)
            return;

        var parentPath = "";
        foreach (var segment in segments)
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                string.IsNullOrWhiteSpace(parentPath)
                    ? BuildGraphUri($"/drives/{Uri.EscapeDataString(_options.DriveId)}/root/children")
                    : BuildGraphUri($"/drives/{Uri.EscapeDataString(_options.DriveId)}/root:/{BuildDrivePath(parentPath)}:/children"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Content = new StringContent(
                JsonSerializer.Serialize(new Dictionary<string, object?>
                {
                    ["name"] = segment,
                    ["folder"] = new { },
                    ["@microsoft.graph.conflictBehavior"] = "fail"
                }, JsonOptions),
                Encoding.UTF8,
                "application/json");

            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            if (response.StatusCode != HttpStatusCode.Conflict && !response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(ct);
                throw new InvalidOperationException($"Microsoft Graph no pudo crear la carpeta de deducciones IVA: {(int)response.StatusCode} {response.ReasonPhrase}. {Truncate(body, 1200)}");
            }

            parentPath = string.IsNullOrWhiteSpace(parentPath)
                ? segment
                : $"{parentPath}/{segment}";
        }
    }

    private async Task<DeduccionesIvaImportHistoryManifestDto?> ReadImportHistoryManifestAsync(
        HttpClient client,
        string token,
        string itemId,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(itemId))
            return null;

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            BuildGraphUri($"/drives/{Uri.EscapeDataString(_options.DriveId)}/items/{Uri.EscapeDataString(itemId)}/content"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode)
            return null;

        await using var content = await response.Content.ReadAsStreamAsync(ct);
        try
        {
            return await JsonSerializer.DeserializeAsync<DeduccionesIvaImportHistoryManifestDto>(
                content,
                JsonOptions,
                ct);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static IReadOnlyList<HistoryDriveItem> ReadHistoryDriveItems(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return Array.Empty<HistoryDriveItem>();

        try
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("value", out var values)
                || values.ValueKind != JsonValueKind.Array)
            {
                return Array.Empty<HistoryDriveItem>();
            }

            return values.EnumerateArray()
                .Select(item => new HistoryDriveItem(
                    item.TryGetProperty("id", out var id) ? id.GetString() ?? "" : "",
                    item.TryGetProperty("name", out var name) ? name.GetString() ?? "" : "",
                    item.TryGetProperty("lastModifiedDateTime", out var modified)
                    && DateTimeOffset.TryParse(
                        modified.GetString(),
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.AssumeUniversal,
                        out var parsed)
                        ? parsed
                        : DateTimeOffset.MinValue))
                .Where(static item => !string.IsNullOrWhiteSpace(item.Id))
                .ToArray();
        }
        catch (JsonException)
        {
            return Array.Empty<HistoryDriveItem>();
        }
    }

    private async Task<string> GetGraphAccessTokenAsync(CancellationToken ct)
    {
        var tenantId = FirstNonEmpty(_configuration["M365:TenantId"], _configuration["AzureAd:TenantId"]);
        var clientId = FirstNonEmpty(_m365Options.ClientId, _configuration["AzureAd:ClientId"]);
        var clientSecret = FirstNonEmpty(_m365Options.ClientSecret, _configuration["AzureAd:ClientSecret"]);
        if (string.IsNullOrWhiteSpace(tenantId) || string.IsNullOrWhiteSpace(clientId))
            throw new InvalidOperationException("Configura M365:ClientId y AzureAd:TenantId para guardar deducciones IVA en SharePoint.");

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
            throw new InvalidOperationException("Configura M365:ClientSecret, AzureAd:ClientSecret o un certificado M365 para guardar deducciones IVA en SharePoint.");
        }

        try
        {
            var app = builder.Build();
            var result = await app.AcquireTokenForClient(ResolveGraphTokenScopes()).ExecuteAsync(ct);
            return result.AccessToken;
        }
        catch (MsalException ex)
        {
            throw new InvalidOperationException("No fue posible obtener token app-only de Microsoft Graph para SharePoint.", ex);
        }
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

    private string[] ResolveGraphTokenScopes()
    {
        var scopes = _m365Options.Scopes?
            .Where(static scope => !string.IsNullOrWhiteSpace(scope))
            .Select(static scope => scope.Trim())
            .ToArray();

        return scopes is { Length: > 0 } ? scopes : new[] { GraphDefaultScope };
    }

    private string BuildFallbackWebUrl(string storedFileName)
    {
        if (string.IsNullOrWhiteSpace(_options.WebBaseUrl))
            return "";

        return $"{_options.WebBaseUrl.TrimEnd('/')}/{Uri.EscapeDataString(storedFileName)}";
    }

    private string BuildHistoryFolderPath() =>
        string.Join("/", SplitDrivePath(_options.FolderPath).Concat(new[] { "_history" }));

    private string BuildStoredFileName(string originalFileName)
    {
        var sourceName = Path.GetFileName(FirstNonEmpty(originalFileName, "Reporte DIAN.xlsx"));
        var extension = Path.GetExtension(sourceName);
        if (string.IsNullOrWhiteSpace(extension))
            extension = ".xlsx";

        var nameWithoutExtension = Path.GetFileNameWithoutExtension(sourceName);
        var safeName = SanitizeFileName(nameWithoutExtension);
        if (string.IsNullOrWhiteSpace(safeName))
            safeName = "exportable-dian";

        var prefix = SanitizeFileName(FirstNonEmpty(_options.FileNamePrefix, "deducciones-iva"));
        var stamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        return $"{prefix}_{stamp}_{safeName}{extension.ToLowerInvariant()}";
    }

    private static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars().Concat(new[] { '#', '%', '*', ':', '<', '>', '?', '/', '\\', '|', '"' }).ToHashSet();
        var builder = new StringBuilder(value.Length);
        foreach (var ch in value.Trim())
        {
            builder.Append(invalid.Contains(ch) || char.IsControl(ch) ? '-' : ch);
        }

        return Regex.Replace(builder.ToString(), "-{2,}", "-").Trim(' ', '.', '-');
    }

    private static string BuildDrivePath(params string?[] paths) =>
        string.Join("/", paths
            .SelectMany(path => SplitDrivePath(path))
            .Select(Uri.EscapeDataString));

    private static IReadOnlyList<string> SplitDrivePath(string? path) =>
        (path ?? "")
            .Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(static segment => !string.IsNullOrWhiteSpace(segment))
            .ToArray();

    private static string ResolveContentType(string? contentType, string storedFileName)
    {
        if (!string.IsNullOrWhiteSpace(contentType))
            return contentType.Trim();

        return Path.GetExtension(storedFileName).Equals(".xlsm", StringComparison.OrdinalIgnoreCase)
            ? "application/vnd.ms-excel.sheet.macroEnabled.12"
            : "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    }

    private static string ReadStringProperty(string json, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(json))
            return "";

        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.TryGetProperty(propertyName, out var property)
                ? property.GetString() ?? ""
                : "";
        }
        catch (JsonException)
        {
            return "";
        }
    }

    private static string NormalizeAuthorityHost(string? value)
    {
        var authority = string.IsNullOrWhiteSpace(value)
            ? "https://login.microsoftonline.com"
            : value.Trim();

        return authority.TrimEnd('/');
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
                return value.Trim();
        }

        return "";
    }

    private static string Truncate(string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
            return value;

        return value[..maxLength];
    }

    private sealed record HistoryDriveItem(
        string Id,
        string Name,
        DateTimeOffset LastModifiedDateTime);
}
