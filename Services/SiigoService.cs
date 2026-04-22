using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CotizadorInterno.Web.Models.Dashboard;
using Microsoft.Extensions.Options;

namespace CotizadorInterno.Web.Services;

public sealed class SiigoService : ISiigoService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    private static readonly CultureInfo ColombianCulture = CultureInfo.GetCultureInfo("es-CO");

    private readonly HttpClient _httpClient;
    private readonly SiigoOptions _options;
    private readonly ILogger<SiigoService> _logger;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);

    private string _accessToken = "";
    private string _tokenType = "Bearer";
    private DateTimeOffset _tokenExpiresAt = DateTimeOffset.MinValue;

    public SiigoService(HttpClient httpClient, IOptions<SiigoOptions> options, ILogger<SiigoService> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SiigoCustomerLookupItemDto>> SearchCustomersAsync(string query, int top = 12, CancellationToken ct = default)
    {
        var search = (query ?? "").Trim();
        if (search.Length < 2)
            return Array.Empty<SiigoCustomerLookupItemDto>();

        var requestedTop = Math.Clamp(top, 1, 50);
        var normalizedSearch = NormalizeSearch(search);
        var digits = ExtractDigits(search);
        var results = new Dictionary<string, SiigoCustomerLookupItemDto>(StringComparer.OrdinalIgnoreCase);

        foreach (var identification in BuildIdentificationCandidates(digits))
        {
            var exactPage = await GetPagedAsync<SiigoCustomerApiDto>(
                "v1/customers",
                new[]
                {
                    Pair("identification", identification),
                    Pair("active", "true"),
                    Pair("type", "Customer"),
                    Pair("page", "1"),
                    Pair("page_size", requestedTop.ToString(CultureInfo.InvariantCulture))
                },
                ct);

            AddCustomerResults(results, exactPage.Results.Select(MapCustomer), requestedTop);
            if (results.Count >= requestedTop)
                return results.Values.Take(requestedTop).ToList();
        }

        var maxPages = Math.Clamp(_options.MaxCustomerSearchPages, 1, 25);
        var pageSize = Math.Clamp(_options.PageSize, 25, 100);

        for (var page = 1; page <= maxPages && results.Count < requestedTop; page++)
        {
            var listPage = await GetPagedAsync<SiigoCustomerApiDto>(
                "v1/customers",
                new[]
                {
                    Pair("active", "true"),
                    Pair("type", "Customer"),
                    Pair("page", page.ToString(CultureInfo.InvariantCulture)),
                    Pair("page_size", pageSize.ToString(CultureInfo.InvariantCulture))
                },
                ct);

            var matches = listPage.Results
                .Select(MapCustomer)
                .Where(customer => CustomerMatches(customer, normalizedSearch, digits));

            AddCustomerResults(results, matches, requestedTop);

            if (ShouldStopPaging(listPage.Pagination, page, pageSize, listPage.Results.Count))
                break;
        }

        return results.Values.Take(requestedTop).ToList();
    }

    public async Task<SiigoInvoiceSearchResultDto> GetInvoicesAsync(
        string? customerId,
        string? customerQuery,
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken ct = default)
    {
        if (startDate > endDate)
            throw new InvalidOperationException("La fecha inicial no puede ser mayor que la fecha final.");

        var customer = await ResolveCustomerAsync(customerId, customerQuery, ct);
        var pageSize = Math.Clamp(_options.PageSize, 25, 100);
        var maxPages = Math.Clamp(_options.MaxInvoicePages, 1, 100);
        var invoices = new List<SiigoInvoiceRowDto>();

        for (var page = 1; page <= maxPages; page++)
        {
            var response = await GetPagedAsync<SiigoInvoiceApiDto>(
                "v1/invoices",
                new[]
                {
                    Pair("customer_identification", customer.Identification),
                    Pair("customer_branch_office", customer.BranchOffice.ToString(CultureInfo.InvariantCulture)),
                    Pair("date_start", FormatSiigoDate(startDate)),
                    Pair("date_end", FormatSiigoDate(endDate)),
                    Pair("page", page.ToString(CultureInfo.InvariantCulture)),
                    Pair("page_size", pageSize.ToString(CultureInfo.InvariantCulture))
                },
                ct);

            invoices.AddRange(response.Results.Select(MapInvoice));

            if (ShouldStopPaging(response.Pagination, page, pageSize, response.Results.Count))
                break;
        }

        var sortedInvoices = invoices
            .OrderByDescending(invoice => invoice.DateValue)
            .ThenBy(invoice => invoice.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new SiigoInvoiceSearchResultDto
        {
            CustomerId = customer.Id,
            CustomerDisplayName = customer.DisplayName,
            CustomerIdentification = customer.Identification,
            CustomerBranchOffice = customer.BranchOffice,
            StartDateValue = FormatSiigoDate(startDate),
            EndDateValue = FormatSiigoDate(endDate),
            PeriodLabel = $"{startDate:dd/MM/yyyy} - {endDate:dd/MM/yyyy}",
            HasData = sortedInvoices.Count > 0,
            RecordsCount = sortedInvoices.Count,
            TotalAmount = sortedInvoices.Sum(static invoice => invoice.Total),
            TotalBalance = sortedInvoices.Sum(static invoice => invoice.Balance),
            EmptyStateTitle = "Sin facturas en Siigo",
            EmptyStateMessage = $"No encontramos facturas de venta para {customer.DisplayName} entre {startDate:dd/MM/yyyy} y {endDate:dd/MM/yyyy}.",
            Invoices = sortedInvoices
        };
    }

    public async Task<SiigoInvoiceDownloadResult> DownloadInvoicePdfsAsync(
        IReadOnlyList<SiigoInvoiceDownloadItemDto> invoices,
        CancellationToken ct = default)
    {
        var requestedInvoices = (invoices ?? Array.Empty<SiigoInvoiceDownloadItemDto>())
            .Where(static invoice => !string.IsNullOrWhiteSpace(invoice.Id))
            .GroupBy(static invoice => invoice.Id.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.First())
            .Take(100)
            .ToList();

        if (requestedInvoices.Count == 0)
            throw new InvalidOperationException("Selecciona al menos una factura para descargar.");

        var files = new List<(string FileName, byte[] Content)>();
        for (var index = 0; index < requestedInvoices.Count; index++)
        {
            var invoice = requestedInvoices[index];
            var pdf = await GetAuthorizedJsonAsync<SiigoPdfResponseApiDto>($"v1/invoices/{Uri.EscapeDataString(invoice.Id.Trim())}/pdf", ct);
            if (string.IsNullOrWhiteSpace(pdf.Base64))
                throw new InvalidOperationException($"Siigo no devolvio PDF para la factura {ResolveInvoiceLabel(invoice, index + 1)}.");

            byte[] content;
            try
            {
                content = Convert.FromBase64String(pdf.Base64);
            }
            catch (FormatException ex)
            {
                throw new InvalidOperationException($"Siigo devolvio un PDF invalido para la factura {ResolveInvoiceLabel(invoice, index + 1)}.", ex);
            }

            files.Add(($"{BuildSafeFileName(ResolveInvoiceLabel(invoice, index + 1))}.pdf", content));
        }

        if (files.Count == 1)
        {
            return new SiigoInvoiceDownloadResult
            {
                FileName = files[0].FileName,
                ContentType = "application/pdf",
                Content = files[0].Content
            };
        }

        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var usedNames = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in files)
            {
                var entryName = BuildUniqueFileName(file.FileName, usedNames);
                var entry = archive.CreateEntry(entryName, CompressionLevel.Fastest);
                await using var entryStream = entry.Open();
                await entryStream.WriteAsync(file.Content, ct);
            }
        }

        return new SiigoInvoiceDownloadResult
        {
            FileName = $"facturas-siigo-{DateTimeOffset.Now:yyyyMMddHHmm}.zip",
            ContentType = "application/zip",
            Content = stream.ToArray()
        };
    }

    private async Task<SiigoCustomerLookupItemDto> ResolveCustomerAsync(string? customerId, string? customerQuery, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(customerId))
        {
            var customer = await GetAuthorizedJsonAsync<SiigoCustomerApiDto>($"v1/customers/{Uri.EscapeDataString(customerId.Trim())}", ct);
            return MapCustomer(customer);
        }

        var query = (customerQuery ?? "").Trim();
        if (string.IsNullOrWhiteSpace(query))
            throw new InvalidOperationException("Busca y selecciona un cliente de Siigo.");

        var suggestions = await SearchCustomersAsync(query, top: 5, ct);
        if (suggestions.Count == 0)
            throw new InvalidOperationException("No encontramos clientes en Siigo con ese criterio.");

        var normalizedQuery = NormalizeSearch(query);
        var digits = ExtractDigits(query);
        var exact = suggestions.FirstOrDefault(customer =>
            string.Equals(NormalizeSearch(customer.DisplayName), normalizedQuery, StringComparison.OrdinalIgnoreCase)
            || string.Equals(NormalizeSearch(customer.Name), normalizedQuery, StringComparison.OrdinalIgnoreCase)
            || string.Equals(customer.Identification, digits, StringComparison.OrdinalIgnoreCase));

        if (exact is not null)
            return exact;

        if (suggestions.Count == 1)
            return suggestions[0];

        throw new InvalidOperationException("Selecciona una coincidencia exacta del cliente antes de consultar facturas.");
    }

    private async Task<SiigoPagedResponse<T>> GetPagedAsync<T>(
        string path,
        IEnumerable<KeyValuePair<string, string?>> parameters,
        CancellationToken ct)
    {
        return await GetAuthorizedJsonAsync<SiigoPagedResponse<T>>(BuildRelativeUrl(path, parameters), ct);
    }

    private async Task<T> GetAuthorizedJsonAsync<T>(string relativeUrl, CancellationToken ct)
    {
        using var response = await SendAuthorizedAsync(HttpMethod.Get, relativeUrl, ct);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            InvalidateToken();
            using var retryResponse = await SendAuthorizedAsync(HttpMethod.Get, relativeUrl, ct);
            return await ReadJsonResponseAsync<T>(retryResponse, ct);
        }

        return await ReadJsonResponseAsync<T>(response, ct);
    }

    private async Task<HttpResponseMessage> SendAuthorizedAsync(HttpMethod method, string relativeUrl, CancellationToken ct)
    {
        var token = await GetAccessTokenAsync(ct);
        var request = new HttpRequestMessage(method, relativeUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue(_tokenType, token);
        request.Headers.TryAddWithoutValidation("Partner-Id", ResolvePartnerId());
        return await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
    }

    private async Task<string> GetAccessTokenAsync(CancellationToken ct)
    {
        if (HasValidToken())
            return _accessToken;

        await _tokenLock.WaitAsync(ct);
        try
        {
            if (HasValidToken())
                return _accessToken;

            ValidateOptions();

            using var request = new HttpRequestMessage(HttpMethod.Post, "auth");
            request.Headers.TryAddWithoutValidation("Partner-Id", ResolvePartnerId());
            request.Content = new StringContent(
                JsonSerializer.Serialize(new
                {
                    username = _options.Username.Trim(),
                    access_key = _options.AccessKey.Trim()
                }, JsonOptions),
                Encoding.UTF8,
                "application/json");

            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            var auth = await ReadJsonResponseAsync<SiigoAuthResponseApiDto>(response, ct);

            if (string.IsNullOrWhiteSpace(auth.AccessToken))
                throw new InvalidOperationException("Siigo no devolvio un token de acceso.");

            _accessToken = auth.AccessToken.Trim();
            _tokenType = string.IsNullOrWhiteSpace(auth.TokenType) ? "Bearer" : auth.TokenType.Trim();

            var expiresInSeconds = auth.ExpiresIn > 0 ? auth.ExpiresIn : 3600;
            var skew = Math.Clamp(_options.TokenRefreshSkewMinutes, 1, 30);
            _tokenExpiresAt = DateTimeOffset.UtcNow
                .AddSeconds(expiresInSeconds)
                .AddMinutes(-skew);

            return _accessToken;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    private bool HasValidToken() =>
        !string.IsNullOrWhiteSpace(_accessToken)
        && _tokenExpiresAt > DateTimeOffset.UtcNow.AddMinutes(1);

    private void InvalidateToken()
    {
        _accessToken = "";
        _tokenExpiresAt = DateTimeOffset.MinValue;
    }

    private void ValidateOptions()
    {
        if (string.IsNullOrWhiteSpace(_options.Username) || string.IsNullOrWhiteSpace(_options.AccessKey))
            throw new InvalidOperationException("Configura Siigo:Username y Siigo:AccessKey en User Secrets o variables de entorno.");

        if (string.IsNullOrWhiteSpace(ResolvePartnerId()))
            throw new InvalidOperationException("Configura Siigo:PartnerId con un identificador alfanumerico de 3 a 100 caracteres.");
    }

    private string ResolvePartnerId()
    {
        var value = string.IsNullOrWhiteSpace(_options.PartnerId)
            ? "CotizadorInterno"
            : _options.PartnerId.Trim();

        return value;
    }

    private async Task<T> ReadJsonResponseAsync<T>(HttpResponseMessage response, CancellationToken ct)
    {
        var rawBody = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            var message = ResolveSiigoErrorMessage(rawBody);
            _logger.LogWarning("Siigo API returned {StatusCode}: {Message}", (int)response.StatusCode, message);
            throw new InvalidOperationException($"Siigo respondio {(int)response.StatusCode}: {message}");
        }

        if (string.IsNullOrWhiteSpace(rawBody))
            throw new InvalidOperationException("Siigo devolvio una respuesta vacia.");

        try
        {
            return JsonSerializer.Deserialize<T>(rawBody, JsonOptions)
                ?? throw new InvalidOperationException("Siigo devolvio una respuesta sin datos.");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("No fue posible interpretar la respuesta de Siigo.", ex);
        }
    }

    private static string ResolveSiigoErrorMessage(string rawBody)
    {
        if (string.IsNullOrWhiteSpace(rawBody))
            return "respuesta vacia";

        try
        {
            using var document = JsonDocument.Parse(rawBody);
            var root = document.RootElement;

            if (TryGetStringProperty(root, "message", out var message))
                return message;

            if (TryGetStringProperty(root, "error", out var error))
                return error;

            foreach (var propertyName in new[] { "errors", "Errors" })
            {
                if (!root.TryGetProperty(propertyName, out var errors))
                    continue;

                if (errors.ValueKind == JsonValueKind.Array)
                {
                    var values = errors.EnumerateArray()
                        .Select(errorItem =>
                        {
                            if (errorItem.ValueKind == JsonValueKind.String)
                                return errorItem.GetString();

                            return errorItem.ValueKind == JsonValueKind.Object
                                   && TryGetStringProperty(errorItem, "message", out var itemMessage)
                                ? itemMessage
                                : errorItem.ToString();
                        })
                        .Where(static value => !string.IsNullOrWhiteSpace(value));

                    var joined = string.Join("; ", values);
                    if (!string.IsNullOrWhiteSpace(joined))
                        return joined;
                }

                if (errors.ValueKind == JsonValueKind.String)
                    return errors.GetString() ?? "error de Siigo";
            }
        }
        catch (JsonException)
        {
        }

        return rawBody.Length <= 400 ? rawBody : rawBody[..400];
    }

    private static bool TryGetStringProperty(JsonElement root, string propertyName, out string value)
    {
        value = "";
        if (!root.TryGetProperty(propertyName, out var property))
            return false;

        if (property.ValueKind != JsonValueKind.String)
            return false;

        value = property.GetString() ?? "";
        return !string.IsNullOrWhiteSpace(value);
    }

    private static SiigoCustomerLookupItemDto MapCustomer(SiigoCustomerApiDto customer)
    {
        var name = ResolveName(customer.Name);
        var commercialName = customer.CommercialName?.Trim() ?? "";
        var identification = customer.Identification?.Trim() ?? "";
        var primaryName = !string.IsNullOrWhiteSpace(commercialName) ? commercialName : name;
        var displayName = string.IsNullOrWhiteSpace(identification)
            ? primaryName
            : $"{primaryName} - {identification}";

        return new SiigoCustomerLookupItemDto
        {
            Id = customer.Id?.Trim() ?? "",
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? identification : displayName,
            Name = name,
            CommercialName = commercialName,
            Identification = identification,
            BranchOffice = customer.BranchOffice,
            Active = customer.Active
        };
    }

    private static SiigoInvoiceRowDto MapInvoice(SiigoInvoiceApiDto invoice)
    {
        var dateDisplay = "";
        if (DateOnly.TryParse(invoice.Date, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            dateDisplay = date.ToString("dd/MM/yyyy", ColombianCulture);

        return new SiigoInvoiceRowDto
        {
            Id = invoice.Id?.Trim() ?? "",
            Name = invoice.Name?.Trim() ?? "",
            Prefix = invoice.Prefix?.Trim() ?? "",
            Number = invoice.Number,
            DateValue = invoice.Date?.Trim() ?? "",
            DateDisplay = dateDisplay,
            CustomerIdentification = invoice.Customer?.Identification?.Trim() ?? "",
            CustomerBranchOffice = invoice.Customer?.BranchOffice ?? 0,
            Total = invoice.Total,
            Balance = invoice.Balance,
            StampStatus = invoice.Stamp?.Status?.Trim() ?? "",
            Annulled = invoice.Annulled
        };
    }

    private static void AddCustomerResults(
        Dictionary<string, SiigoCustomerLookupItemDto> target,
        IEnumerable<SiigoCustomerLookupItemDto> source,
        int top)
    {
        foreach (var customer in source)
        {
            if (target.Count >= top)
                return;

            var key = !string.IsNullOrWhiteSpace(customer.Id)
                ? customer.Id
                : $"{customer.Identification}:{customer.BranchOffice}";

            target.TryAdd(key, customer);
        }
    }

    private static bool CustomerMatches(SiigoCustomerLookupItemDto customer, string normalizedSearch, string digits)
    {
        if (!string.IsNullOrWhiteSpace(digits)
            && customer.Identification.Contains(digits, StringComparison.OrdinalIgnoreCase))
            return true;

        return NormalizeSearch(customer.DisplayName).Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase)
            || NormalizeSearch(customer.Name).Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase)
            || NormalizeSearch(customer.CommercialName).Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<string> BuildIdentificationCandidates(string digits)
    {
        if (digits.Length < 3)
            return Array.Empty<string>();

        var candidates = new List<string> { digits };
        if (digits.Length > 6)
            candidates.Add(digits[..^1]);

        return candidates
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool ShouldStopPaging(SiigoPaginationApiDto? pagination, int page, int pageSize, int returnedCount)
    {
        if (returnedCount <= 0 || returnedCount < pageSize)
            return true;

        return pagination?.TotalResults > 0 && page * pageSize >= pagination.TotalResults;
    }

    private static string ResolveName(JsonElement name)
    {
        if (name.ValueKind == JsonValueKind.Array)
        {
            return string.Join(" ", name.EnumerateArray()
                    .Where(static item => item.ValueKind == JsonValueKind.String)
                    .Select(static item => item.GetString())
                    .Where(static item => !string.IsNullOrWhiteSpace(item)))
                .Trim();
        }

        if (name.ValueKind == JsonValueKind.String)
            return name.GetString()?.Trim() ?? "";

        return name.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null
            ? ""
            : name.ToString().Trim();
    }

    private static string FormatSiigoDate(DateOnly value) =>
        value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string NormalizeSearch(string? value)
    {
        var trimmed = (value ?? "").Trim();
        if (trimmed.Length == 0)
            return "";

        var normalized = trimmed.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);
        foreach (var character in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
                builder.Append(char.ToLowerInvariant(character));
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }

    private static string ExtractDigits(string? value) =>
        new((value ?? "").Where(char.IsDigit).ToArray());

    private static string BuildRelativeUrl(string path, IEnumerable<KeyValuePair<string, string?>> parameters)
    {
        var query = string.Join("&", parameters
            .Where(static item => !string.IsNullOrWhiteSpace(item.Value))
            .Select(static item => $"{Uri.EscapeDataString(item.Key)}={Uri.EscapeDataString(item.Value!)}"));

        return string.IsNullOrWhiteSpace(query)
            ? path
            : $"{path}?{query}";
    }

    private static KeyValuePair<string, string?> Pair(string key, string? value) => new(key, value);

    private static string ResolveInvoiceLabel(SiigoInvoiceDownloadItemDto invoice, int index) =>
        string.IsNullOrWhiteSpace(invoice.Name)
            ? $"factura-{index}"
            : invoice.Name.Trim();

    private static string BuildSafeFileName(string value)
    {
        var cleaned = string.Join("-", (value ?? "factura")
            .Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        cleaned = cleaned
            .Replace(" ", "-", StringComparison.OrdinalIgnoreCase)
            .Trim('-');

        return string.IsNullOrWhiteSpace(cleaned)
            ? "factura"
            : cleaned.ToLowerInvariant();
    }

    private static string BuildUniqueFileName(string fileName, IDictionary<string, int> usedNames)
    {
        if (!usedNames.TryGetValue(fileName, out var count))
        {
            usedNames[fileName] = 1;
            return fileName;
        }

        count++;
        usedNames[fileName] = count;

        var extension = Path.GetExtension(fileName);
        var nameWithoutExtension = Path.GetFileNameWithoutExtension(fileName);
        return $"{nameWithoutExtension}-{count}{extension}";
    }

    private sealed class SiigoAuthResponseApiDto
    {
        [JsonPropertyName("access_token")]
        public string AccessToken { get; set; } = "";

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; set; }

        [JsonPropertyName("token_type")]
        public string TokenType { get; set; } = "Bearer";
    }

    private sealed class SiigoPagedResponse<T>
    {
        [JsonPropertyName("pagination")]
        public SiigoPaginationApiDto? Pagination { get; set; }

        [JsonPropertyName("results")]
        public IReadOnlyList<T> Results { get; set; } = Array.Empty<T>();
    }

    private sealed class SiigoPaginationApiDto
    {
        [JsonPropertyName("page")]
        public int Page { get; set; }

        [JsonPropertyName("page_size")]
        public int PageSize { get; set; }

        [JsonPropertyName("total_results")]
        public int TotalResults { get; set; }
    }

    private sealed class SiigoCustomerApiDto
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = "";

        [JsonPropertyName("name")]
        public JsonElement Name { get; set; }

        [JsonPropertyName("commercial_name")]
        public string CommercialName { get; set; } = "";

        [JsonPropertyName("identification")]
        public string Identification { get; set; } = "";

        [JsonPropertyName("branch_office")]
        public int BranchOffice { get; set; }

        [JsonPropertyName("active")]
        public bool Active { get; set; }
    }

    private sealed class SiigoInvoiceApiDto
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = "";

        [JsonPropertyName("prefix")]
        public string Prefix { get; set; } = "";

        [JsonPropertyName("number")]
        public long? Number { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("date")]
        public string Date { get; set; } = "";

        [JsonPropertyName("customer")]
        public SiigoInvoiceCustomerApiDto? Customer { get; set; }

        [JsonPropertyName("total")]
        public decimal Total { get; set; }

        [JsonPropertyName("balance")]
        public decimal Balance { get; set; }

        [JsonPropertyName("stamp")]
        public SiigoInvoiceStampApiDto? Stamp { get; set; }

        [JsonPropertyName("annulled")]
        public bool Annulled { get; set; }
    }

    private sealed class SiigoInvoiceCustomerApiDto
    {
        [JsonPropertyName("identification")]
        public string Identification { get; set; } = "";

        [JsonPropertyName("branch_office")]
        public int BranchOffice { get; set; }
    }

    private sealed class SiigoInvoiceStampApiDto
    {
        [JsonPropertyName("status")]
        public string Status { get; set; } = "";
    }

    private sealed class SiigoPdfResponseApiDto
    {
        [JsonPropertyName("base64")]
        public string Base64 { get; set; } = "";
    }
}
