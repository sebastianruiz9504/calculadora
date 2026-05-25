using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CotizadorInterno.Web.Models.Automation;
using CotizadorInterno.Web.Models.Conciliacion;
using CotizadorInterno.Web.Models.Dashboard;
using CotizadorInterno.Web.Models.Reconciliation;
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

    public async Task<IReadOnlyList<SiigoCustomerLookupItemDto>> GetCustomersAsync(CancellationToken ct = default)
    {
        var pageSize = Math.Clamp(_options.PageSize, 25, 100);
        var maxPages = Math.Clamp(_options.MaxCustomerPages, 1, 200);
        var results = new Dictionary<string, SiigoCustomerLookupItemDto>(StringComparer.OrdinalIgnoreCase);
        var customerTypes = new[] { "Customer", "Supplier", "Other" };
        var activeStates = new[] { true, false };

        foreach (var customerType in customerTypes)
        {
            foreach (var active in activeStates)
            {
                for (var page = 1; page <= maxPages; page++)
                {
                    var response = await GetPagedAsync<SiigoCustomerApiDto>(
                        "v1/customers",
                        new[]
                        {
                            Pair("type", customerType),
                            Pair("active", active ? "true" : "false"),
                            Pair("page", page.ToString(CultureInfo.InvariantCulture)),
                            Pair("page_size", pageSize.ToString(CultureInfo.InvariantCulture))
                        },
                        ct);

                    AddCustomerResults(results, response.Results.Select(MapCustomer), int.MaxValue);

                    if (ShouldStopPaging(response.Pagination, page, pageSize, response.Results.Count))
                        break;
                }
            }
        }

        return SortCustomers(results.Values).ToList();
    }

    public async Task<IReadOnlyList<SiigoCustomerLookupItemDto>> SearchCustomersAsync(string query, int top = 12, CancellationToken ct = default)
    {
        var search = (query ?? "").Trim();
        if (search.Length < 2)
            return Array.Empty<SiigoCustomerLookupItemDto>();

        var requestedTop = Math.Clamp(top, 1, 50);
        var digits = ExtractDigits(search);
        var results = new Dictionary<string, SiigoCustomerLookupItemDto>(StringComparer.OrdinalIgnoreCase);
        var customerTypes = new[] { "Customer", "Supplier", "Other" };
        var activeStates = new[] { true, false };

        foreach (var identification in BuildIdentificationCandidates(digits))
        {
            var unfilteredPage = await GetPagedAsync<SiigoCustomerApiDto>(
                "v1/customers",
                new[]
                {
                    Pair("identification", identification),
                    Pair("page", "1"),
                    Pair("page_size", requestedTop.ToString(CultureInfo.InvariantCulture))
                },
                ct);

            AddCustomerResults(results, unfilteredPage.Results.Select(MapCustomer), requestedTop);
            if (results.Count >= requestedTop)
                return SortCustomers(results.Values).Take(requestedTop).ToList();

            var inactivePage = await GetPagedAsync<SiigoCustomerApiDto>(
                "v1/customers",
                new[]
                {
                    Pair("identification", identification),
                    Pair("active", "false"),
                    Pair("page", "1"),
                    Pair("page_size", requestedTop.ToString(CultureInfo.InvariantCulture))
                },
                ct);

            AddCustomerResults(results, inactivePage.Results.Select(MapCustomer), requestedTop);
            if (results.Count >= requestedTop)
                return SortCustomers(results.Values).Take(requestedTop).ToList();

            foreach (var customerType in customerTypes)
            {
                foreach (var active in activeStates)
                {
                    var exactPage = await GetPagedAsync<SiigoCustomerApiDto>(
                        "v1/customers",
                        new[]
                        {
                            Pair("identification", identification),
                            Pair("active", active ? "true" : "false"),
                            Pair("type", customerType),
                            Pair("page", "1"),
                            Pair("page_size", requestedTop.ToString(CultureInfo.InvariantCulture))
                        },
                        ct);

                    AddCustomerResults(results, exactPage.Results.Select(MapCustomer), requestedTop);
                    if (results.Count >= requestedTop)
                        return SortCustomers(results.Values).Take(requestedTop).ToList();
                }
            }
        }

        return SortCustomers(results.Values).Take(requestedTop).ToList();
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

    public async Task<SiigoFinancialReconciliationData> GetFinancialReconciliationDocumentsAsync(
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken ct = default)
    {
        if (startDate > endDate)
            throw new InvalidOperationException("La fecha inicial no puede ser mayor que la fecha final.");

        var pageSize = Math.Clamp(_options.PageSize, 25, 100);
        var maxPages = Math.Clamp(_options.MaxReconciliationPages, 1, 500);
        var dateParameters = new[]
        {
            Pair("date_start", FormatSiigoDate(startDate)),
            Pair("date_end", FormatSiigoDate(endDate))
        };

        var invoicesTask = GetAllPagedAsync<SiigoInvoiceApiDto>("v1/invoices", dateParameters, pageSize, maxPages, ct);
        var creditNotesTask = GetAllPagedAsync<SiigoCreditNoteApiDto>("v1/credit-notes", dateParameters, pageSize, maxPages, ct);
        var purchasesTask = GetAllPagedAsync<SiigoPurchaseApiDto>("v1/purchases", dateParameters, pageSize, maxPages, ct);

        await Task.WhenAll(invoicesTask, creditNotesTask, purchasesTask);

        return new SiigoFinancialReconciliationData
        {
            Invoices = invoicesTask.Result
                .Select(MapReconciliationInvoice)
                .Where(row => IsInsidePeriod(row.Date, startDate, endDate))
                .ToList(),
            CreditNotes = creditNotesTask.Result
                .Select(MapReconciliationCreditNote)
                .Where(row => IsInsidePeriod(row.Date, startDate, endDate))
                .ToList(),
            Purchases = purchasesTask.Result
                .Select(MapReconciliationPurchase)
                .Where(row => IsInsidePeriod(row.Date, startDate, endDate))
                .ToList()
        };
    }

    public async Task<IReadOnlyList<SiigoObservedAccountDto>> GetObservedAccountCatalogAsync(
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken ct = default)
    {
        if (startDate > endDate)
            throw new InvalidOperationException("La fecha inicial no puede ser mayor que la fecha final.");

        var pageSize = Math.Clamp(_options.PageSize, 25, 100);
        var maxPages = Math.Clamp(_options.MaxReconciliationPages, 1, 500);
        var dateParameters = new[]
        {
            Pair("date_start", FormatSiigoDate(startDate)),
            Pair("date_end", FormatSiigoDate(endDate))
        };

        var journalsTask = GetAllPagedAsync<SiigoAccountingDocumentApiDto>("v1/journals", dateParameters, pageSize, maxPages, ct);
        var paymentReceiptsTask = GetAllPagedAsync<SiigoAccountingDocumentApiDto>("v1/payment-receipts", dateParameters, pageSize, maxPages, ct);
        var vouchersTask = GetAllPagedAsync<SiigoAccountingDocumentApiDto>("v1/vouchers", dateParameters, pageSize, maxPages, ct);
        var purchasesTask = GetAllPagedAsync<SiigoAccountingDocumentApiDto>("v1/purchases", dateParameters, pageSize, maxPages, ct);

        await Task.WhenAll(journalsTask, paymentReceiptsTask, vouchersTask, purchasesTask);

        var accounts = new Dictionary<string, SiigoObservedAccountDto>(StringComparer.OrdinalIgnoreCase);
        AddObservedAccounts(accounts, "Journals", journalsTask.Result, startDate, endDate);
        AddObservedAccounts(accounts, "Payment receipts", paymentReceiptsTask.Result, startDate, endDate);
        AddObservedAccounts(accounts, "Vouchers", vouchersTask.Result, startDate, endDate);
        AddObservedAccounts(accounts, "Purchases", purchasesTask.Result, startDate, endDate);

        return accounts.Values
            .OrderBy(account => account.Code, StringComparer.OrdinalIgnoreCase)
            .ToList();
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

    public async Task<IReadOnlyList<SiigoTaxLookupDto>> GetTaxesAsync(CancellationToken ct = default)
    {
        var taxes = await GetAuthorizedJsonAsync<List<SiigoTaxApiDto>>("v1/taxes", ct);
        return taxes
            .Select(static tax => new SiigoTaxLookupDto
            {
                Id = tax.Id,
                Name = tax.Name?.Trim() ?? "",
                Type = tax.Type?.Trim() ?? "",
                Percentage = tax.Percentage,
                Active = tax.Active
            })
            .Where(static tax => tax.Id > 0)
            .OrderBy(static tax => tax.Type, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static tax => tax.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task<IReadOnlyList<SiigoDocumentTypeLookupDto>> GetDocumentTypesAsync(
        string type,
        CancellationToken ct = default)
    {
        var normalizedType = (type ?? "").Trim();
        if (string.IsNullOrWhiteSpace(normalizedType))
            throw new InvalidOperationException("Indica el tipo de documento Siigo a consultar.");

        var documentTypes = await GetAuthorizedJsonAsync<List<SiigoDocumentTypeApiDto>>(
            BuildRelativeUrl("v1/document-types", new[] { Pair("type", normalizedType) }),
            ct);

        return documentTypes
            .Select(static documentType => new SiigoDocumentTypeLookupDto
            {
                Id = documentType.Id,
                Code = documentType.Code?.Trim() ?? "",
                Name = documentType.Name?.Trim() ?? "",
                Description = documentType.Description?.Trim() ?? "",
                Type = documentType.Type?.Trim() ?? "",
                Active = documentType.Active,
                AutomaticNumber = documentType.AutomaticNumber,
                Consecutive = documentType.Consecutive
            })
            .Where(static documentType => documentType.Id > 0)
            .OrderBy(static documentType => documentType.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task<SiigoVoucherCreateResultDto> CreateVoucherAsync(
        object payload,
        string? idempotencyKey = null,
        CancellationToken ct = default)
    {
        if (payload is null)
            throw new ArgumentNullException(nameof(payload));

        var rawBody = await SendAuthorizedJsonAsync(HttpMethod.Post, "v1/vouchers", payload, idempotencyKey, ct);
        return ParseCreatedAccountingDocument(rawBody, "Siigo creo el recibo, pero no fue posible interpretar la respuesta.");
    }

    public async Task<SiigoVoucherCreateResultDto> CreateJournalAsync(
        object payload,
        string? idempotencyKey = null,
        CancellationToken ct = default)
    {
        if (payload is null)
            throw new ArgumentNullException(nameof(payload));

        var rawBody = await SendAuthorizedJsonAsync(HttpMethod.Post, "v1/journals", payload, idempotencyKey, ct);
        return ParseCreatedAccountingDocument(rawBody, "Siigo creo el comprobante de ingreso, pero no fue posible interpretar la respuesta.");
    }

    private static SiigoVoucherCreateResultDto ParseCreatedAccountingDocument(string rawBody, string parseErrorMessage)
    {
        try
        {
            using var document = JsonDocument.Parse(rawBody);
            var root = document.RootElement;
            return new SiigoVoucherCreateResultDto
            {
                Id = ReadJsonString(root, "id"),
                Name = ReadJsonString(root, "name"),
                Number = ReadJsonString(root, "number"),
                Date = ReadJsonString(root, "date"),
                RawJson = JsonSerializer.Serialize(root, new JsonSerializerOptions(JsonOptions) { WriteIndented = true })
            };
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(parseErrorMessage, ex);
        }
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
            throw new InvalidOperationException("Ingresa el NIT del cliente para consultar Siigo.");

        var digits = ExtractDigits(query);
        if (digits.Length < 3)
            throw new InvalidOperationException("Ingresa el NIT del cliente, sin depender del nombre.");

        var suggestions = await SearchCustomersAsync(query, top: 5, ct);
        if (suggestions.Count == 0)
        {
            return new SiigoCustomerLookupItemDto
            {
                Id = "",
                DisplayName = $"NIT {digits}",
                Name = $"NIT {digits}",
                Identification = digits,
                Type = "Direct",
                BranchOffice = 0,
                Active = true
            };
        }

        var exact = suggestions.FirstOrDefault(customer =>
            string.Equals(customer.Identification, digits, StringComparison.OrdinalIgnoreCase)
            || BuildIdentificationCandidates(digits).Any(candidate =>
                string.Equals(customer.Identification, candidate, StringComparison.OrdinalIgnoreCase)));

        if (exact is not null)
            return exact;

        if (suggestions.Count == 1)
            return suggestions[0];

        throw new InvalidOperationException("Selecciona una coincidencia exacta del NIT antes de consultar facturas.");
    }

    private async Task<SiigoPagedResponse<T>> GetPagedAsync<T>(
        string path,
        IEnumerable<KeyValuePair<string, string?>> parameters,
        CancellationToken ct)
    {
        return await GetAuthorizedJsonAsync<SiigoPagedResponse<T>>(BuildRelativeUrl(path, parameters), ct);
    }

    private async Task<IReadOnlyList<T>> GetAllPagedAsync<T>(
        string path,
        IEnumerable<KeyValuePair<string, string?>> parameters,
        int pageSize,
        int maxPages,
        CancellationToken ct)
    {
        var results = new List<T>();
        var baseParameters = parameters.ToList();

        for (var page = 1; page <= maxPages; page++)
        {
            var pageParameters = baseParameters
                .Concat(new[]
                {
                    Pair("page", page.ToString(CultureInfo.InvariantCulture)),
                    Pair("page_size", pageSize.ToString(CultureInfo.InvariantCulture))
                });
            var response = await GetPagedAsync<T>(path, pageParameters, ct);
            results.AddRange(response.Results);

            if (ShouldStopPaging(response.Pagination, page, pageSize, response.Results.Count))
                break;
        }

        return results;
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

    private async Task<string> SendAuthorizedJsonAsync(
        HttpMethod method,
        string relativeUrl,
        object payload,
        string? idempotencyKey,
        CancellationToken ct)
    {
        using var response = await SendAuthorizedWithJsonAsync(method, relativeUrl, payload, idempotencyKey, ct);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            InvalidateToken();
            using var retryResponse = await SendAuthorizedWithJsonAsync(method, relativeUrl, payload, idempotencyKey, ct);
            return await ReadRawJsonResponseAsync(retryResponse, ct);
        }

        return await ReadRawJsonResponseAsync(response, ct);
    }

    private async Task<HttpResponseMessage> SendAuthorizedWithJsonAsync(
        HttpMethod method,
        string relativeUrl,
        object payload,
        string? idempotencyKey,
        CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(payload, JsonOptions);
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        return await SendAuthorizedAsync(method, relativeUrl, ct, content, idempotencyKey);
    }

    private async Task<HttpResponseMessage> SendAuthorizedAsync(
        HttpMethod method,
        string relativeUrl,
        CancellationToken ct,
        HttpContent? content = null,
        string? idempotencyKey = null)
    {
        var token = await GetAccessTokenAsync(ct);
        var request = new HttpRequestMessage(method, relativeUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue(_tokenType, token);
        request.Headers.TryAddWithoutValidation("Partner-Id", ResolvePartnerId());
        if (!string.IsNullOrWhiteSpace(idempotencyKey))
            request.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey.Trim());
        if (content is not null)
            request.Content = content;
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
        var rawBody = await ReadRawJsonResponseAsync(response, ct);
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

    private async Task<string> ReadRawJsonResponseAsync(HttpResponseMessage response, CancellationToken ct)
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

        return rawBody;
    }

    private static string ResolveSiigoErrorMessage(string rawBody)
    {
        if (string.IsNullOrWhiteSpace(rawBody))
            return "respuesta vacia";

        try
        {
            using var document = JsonDocument.Parse(rawBody);
            var root = document.RootElement;

            if (TryGetStringProperty(root, "message", out var message)
                || TryGetStringProperty(root, "Message", out message))
                return message;

            if (TryGetStringProperty(root, "error", out var error)
                || TryGetStringProperty(root, "Error", out error))
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

    private static string ReadJsonString(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var property))
            return "";

        return property.ValueKind switch
        {
            JsonValueKind.String => property.GetString() ?? "",
            JsonValueKind.Number => property.ToString(),
            _ => ""
        };
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
            Type = customer.Type?.Trim() ?? "",
            BranchOffice = customer.BranchOffice,
            Active = customer.Active
        };
    }

    private static int ResolveCustomerTypeOrder(string? type) =>
        (type ?? "").Trim() switch
        {
            "Customer" => 0,
            "Supplier" => 1,
            "Other" => 2,
            _ => 3
        };

    private static IOrderedEnumerable<SiigoCustomerLookupItemDto> SortCustomers(IEnumerable<SiigoCustomerLookupItemDto> customers) =>
        customers
            .OrderBy(static customer => ResolveCustomerTypeOrder(customer.Type))
            .ThenBy(static customer => customer.Active ? 0 : 1)
            .ThenBy(static customer => string.IsNullOrWhiteSpace(customer.DisplayName) ? customer.Identification : customer.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static customer => customer.Identification, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static customer => customer.BranchOffice);

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
            StampObservations = invoice.Stamp?.Observations?.Trim() ?? "",
            StampErrors = invoice.Stamp?.Errors?.Trim() ?? "",
            MailStatus = invoice.Mail?.Status?.Trim() ?? "",
            MailObservations = invoice.Mail?.Observations?.Trim() ?? "",
            Annulled = invoice.Annulled
        };
    }

    private static SiigoReconciliationInvoice MapReconciliationInvoice(SiigoInvoiceApiDto invoice)
    {
        return new SiigoReconciliationInvoice
        {
            Id = invoice.Id?.Trim() ?? "",
            Name = invoice.Name?.Trim() ?? "",
            Prefix = invoice.Prefix?.Trim() ?? "",
            Number = invoice.Number,
            Date = ParseSiigoDate(invoice.Date),
            CustomerId = invoice.Customer?.Id?.Trim() ?? "",
            CustomerIdentification = invoice.Customer?.Identification?.Trim() ?? "",
            Total = RoundCurrency(invoice.Total),
            Vat = SumVat(invoice.Items),
            Annulled = invoice.Annulled,
            RawJson = JsonSerializer.Serialize(invoice, JsonOptions)
        };
    }

    private static SiigoReconciliationCreditNote MapReconciliationCreditNote(SiigoCreditNoteApiDto creditNote)
    {
        return new SiigoReconciliationCreditNote
        {
            Id = creditNote.Id?.Trim() ?? "",
            Name = creditNote.Name?.Trim() ?? "",
            Number = creditNote.Number,
            Date = ParseSiigoDate(creditNote.Date),
            CreatedAt = ParseSiigoDateTimeOffset(creditNote.Metadata?.Created),
            InvoiceId = creditNote.Invoice?.Id?.Trim() ?? "",
            InvoiceName = creditNote.Invoice?.Name?.Trim() ?? "",
            InvoicePrefix = creditNote.InvoiceData?.Prefix?.Trim() ?? "",
            InvoiceNumber = creditNote.InvoiceData?.Number,
            CustomerId = creditNote.Customer?.Id?.Trim() ?? "",
            CustomerIdentification = creditNote.Customer?.Identification?.Trim() ?? "",
            StampStatus = creditNote.Stamp?.Status?.Trim() ?? "",
            Cude = creditNote.Stamp?.Cude?.Trim() ?? "",
            Total = RoundCurrency(creditNote.Total),
            Vat = SumVat(creditNote.Items),
            RawJson = JsonSerializer.Serialize(creditNote, JsonOptions)
        };
    }

    private static SiigoReconciliationPurchase MapReconciliationPurchase(SiigoPurchaseApiDto purchase)
    {
        var providerPrefix = purchase.ProviderInvoice?.Prefix?.Trim() ?? "";
        var providerNumber = purchase.ProviderInvoice?.Number?.Trim() ?? "";

        return new SiigoReconciliationPurchase
        {
            Id = purchase.Id?.Trim() ?? "",
            Name = purchase.Name?.Trim() ?? "",
            Date = ParseSiigoDate(purchase.Date),
            SupplierIdentification = purchase.Supplier?.Identification?.Trim() ?? "",
            ProviderInvoicePrefix = providerPrefix,
            ProviderInvoiceNumber = providerNumber,
            ProviderInvoiceFullNumber = BuildProviderInvoiceFullNumber(providerPrefix, providerNumber),
            Total = RoundCurrency(purchase.Total),
            Vat = SumVat(purchase.Items),
            Balance = RoundCurrency(purchase.Balance)
        };
    }

    private static void AddObservedAccounts(
        IDictionary<string, SiigoObservedAccountDto> target,
        string source,
        IEnumerable<SiigoAccountingDocumentApiDto> documents,
        DateOnly startDate,
        DateOnly endDate)
    {
        foreach (var document in documents)
        {
            var documentDate = ParseSiigoDate(document.Date);
            if (!IsInsidePeriod(documentDate, startDate, endDate))
                continue;

            foreach (var item in document.Items ?? Array.Empty<SiigoDocumentItemApiDto>())
            {
                var code = FirstNonEmpty(item.Account?.Code, item.Code);
                if (string.IsNullOrWhiteSpace(code))
                    continue;

                var name = NormalizeObservedAccountName(FirstNonEmpty(
                    item.Account?.Name,
                    item.Description,
                    code));

                if (!target.TryGetValue(code, out var existing))
                {
                    target[code] = new SiigoObservedAccountDto
                    {
                        Code = code,
                        Name = name,
                        Type = ResolveObservedAccountType(code, name),
                        Source = source,
                        Uses = 1,
                        LastSeenDate = documentDate
                    };
                    continue;
                }

                existing.Uses++;
                existing.Source = MergeObservedAccountSources(existing.Source, source);
                if (documentDate.HasValue
                    && (!existing.LastSeenDate.HasValue || documentDate.Value > existing.LastSeenDate.Value))
                {
                    existing.LastSeenDate = documentDate;
                }

                if (ShouldReplaceObservedAccountName(existing.Name, name, code))
                {
                    existing.Name = name;
                    existing.Type = ResolveObservedAccountType(code, name);
                }
            }
        }
    }

    private static bool ShouldReplaceObservedAccountName(string current, string candidate, string code)
    {
        if (string.IsNullOrWhiteSpace(candidate))
            return false;

        if (string.IsNullOrWhiteSpace(current) || string.Equals(current, code, StringComparison.OrdinalIgnoreCase))
            return true;

        return current.Contains(" Base:", StringComparison.OrdinalIgnoreCase)
            && !candidate.Contains(" Base:", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeObservedAccountName(string value)
    {
        var normalized = (value ?? "").Trim();
        var baseIndex = normalized.IndexOf(" Base:", StringComparison.OrdinalIgnoreCase);
        if (baseIndex > 0)
            normalized = normalized[..baseIndex].Trim();

        return Truncate(normalized, 120);
    }

    private static string MergeObservedAccountSources(string current, string source)
    {
        var values = (current ?? "")
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Concat(new[] { source })
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase);

        return string.Join("; ", values);
    }

    private static string ResolveObservedAccountType(string code, string name)
    {
        var normalizedName = (name ?? "").ToLowerInvariant();
        if (code.StartsWith("1110", StringComparison.OrdinalIgnoreCase)
            || code.StartsWith("1105", StringComparison.OrdinalIgnoreCase)
            || normalizedName.Contains("bancolombia", StringComparison.OrdinalIgnoreCase)
            || normalizedName.Contains("caja", StringComparison.OrdinalIgnoreCase))
        {
            return "Banco/Caja";
        }

        if (code.StartsWith("1305", StringComparison.OrdinalIgnoreCase))
            return "Cliente";

        if (code.StartsWith("2205", StringComparison.OrdinalIgnoreCase)
            || code.StartsWith("2335", StringComparison.OrdinalIgnoreCase))
        {
            return "Proveedor/CxP";
        }

        if (code.StartsWith("1355", StringComparison.OrdinalIgnoreCase)
            || code.StartsWith("1351", StringComparison.OrdinalIgnoreCase)
            || code.StartsWith("2365", StringComparison.OrdinalIgnoreCase)
            || code.StartsWith("2368", StringComparison.OrdinalIgnoreCase))
        {
            return "Retencion";
        }

        if (code.StartsWith("2408", StringComparison.OrdinalIgnoreCase)
            || normalizedName.Contains("iva", StringComparison.OrdinalIgnoreCase))
        {
            return "IVA";
        }

        if (string.Equals(code, "42958101", StringComparison.OrdinalIgnoreCase)
            || normalizedName.Contains("ajuste", StringComparison.OrdinalIgnoreCase))
        {
            return "Ajuste";
        }

        if (code.StartsWith("4", StringComparison.OrdinalIgnoreCase))
            return "Ingreso";

        if (code.StartsWith("5", StringComparison.OrdinalIgnoreCase)
            || code.StartsWith("6", StringComparison.OrdinalIgnoreCase)
            || code.StartsWith("7", StringComparison.OrdinalIgnoreCase))
        {
            return "Gasto/Costo";
        }

        if (code.StartsWith("2", StringComparison.OrdinalIgnoreCase))
            return "Pasivo";

        if (code.StartsWith("1", StringComparison.OrdinalIgnoreCase))
            return "Activo";

        return "Otro";
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

    private static IReadOnlyList<string> BuildIdentificationCandidates(string digits)
    {
        if (digits.Length < 3)
            return Array.Empty<string>();

        var candidates = new List<string> { digits };
        if (digits.Length > 6)
            candidates.Add(digits[..^1]);

        if (digits.Length is >= 7 and <= 10)
        {
            for (var checkDigit = 0; checkDigit <= 9; checkDigit++)
            {
                candidates.Add($"{digits}{checkDigit}");
            }
        }

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

    private static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? "";

    private static string Truncate(string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
            return value;

        return value[..maxLength];
    }

    private static string FormatSiigoDate(DateOnly value) =>
        value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static DateOnly? ParseSiigoDate(string? value) =>
        DateOnly.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? parsed
            : null;

    private static bool IsInsidePeriod(DateOnly? date, DateOnly startDate, DateOnly endDate) =>
        date.HasValue && date.Value >= startDate && date.Value <= endDate;

    private static DateTimeOffset? ParseSiigoDateTimeOffset(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed
            : null;

    private static string ExtractDigits(string? value) =>
        new((value ?? "").Where(char.IsDigit).ToArray());

    private static decimal SumVat(IReadOnlyList<SiigoDocumentItemApiDto>? items)
    {
        if (items is null || items.Count == 0)
            return 0m;

        var total = items
            .SelectMany(static item => item.Taxes ?? Array.Empty<SiigoDocumentTaxApiDto>())
            .Where(static tax => IsVatTax(tax.Type) || IsVatTax(tax.Name))
            .Sum(static tax => tax.Value);

        return RoundCurrency(total);
    }

    private static bool IsVatTax(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var normalized = value.Trim();
        return normalized.Equals("IVA", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("IVA", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("VAT", StringComparison.OrdinalIgnoreCase);
    }

    private static decimal RoundCurrency(decimal value) =>
        Math.Round(value, 2, MidpointRounding.AwayFromZero);

    private static string BuildProviderInvoiceFullNumber(string prefix, string number)
    {
        if (string.IsNullOrWhiteSpace(prefix))
            return number.Trim();

        if (string.IsNullOrWhiteSpace(number))
            return prefix.Trim();

        return $"{prefix.Trim()}-{number.Trim()}";
    }

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

        [JsonPropertyName("type")]
        public string Type { get; set; } = "";

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

        [JsonPropertyName("mail")]
        public SiigoInvoiceMailApiDto? Mail { get; set; }

        [JsonPropertyName("items")]
        public IReadOnlyList<SiigoDocumentItemApiDto> Items { get; set; } = Array.Empty<SiigoDocumentItemApiDto>();

        [JsonPropertyName("annulled")]
        public bool Annulled { get; set; }
    }

    private sealed class SiigoCreditNoteApiDto
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = "";

        [JsonPropertyName("number")]
        public long? Number { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("date")]
        public string Date { get; set; } = "";

        [JsonPropertyName("invoice")]
        public SiigoDocumentReferenceApiDto? Invoice { get; set; }

        [JsonPropertyName("invoice_data")]
        public SiigoInvoiceDataApiDto? InvoiceData { get; set; }

        [JsonPropertyName("customer")]
        public SiigoInvoiceCustomerApiDto? Customer { get; set; }

        [JsonPropertyName("metadata")]
        public SiigoMetadataApiDto? Metadata { get; set; }

        [JsonPropertyName("stamp")]
        public SiigoCreditNoteStampApiDto? Stamp { get; set; }

        [JsonPropertyName("total")]
        public decimal Total { get; set; }

        [JsonPropertyName("items")]
        public IReadOnlyList<SiigoDocumentItemApiDto> Items { get; set; } = Array.Empty<SiigoDocumentItemApiDto>();
    }

    private sealed class SiigoPurchaseApiDto
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = "";

        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("date")]
        public string Date { get; set; } = "";

        [JsonPropertyName("supplier")]
        public SiigoPurchaseSupplierApiDto? Supplier { get; set; }

        [JsonPropertyName("provider_invoice")]
        public SiigoProviderInvoiceApiDto? ProviderInvoice { get; set; }

        [JsonPropertyName("total")]
        public decimal Total { get; set; }

        [JsonPropertyName("balance")]
        public decimal Balance { get; set; }

        [JsonPropertyName("items")]
        public IReadOnlyList<SiigoDocumentItemApiDto> Items { get; set; } = Array.Empty<SiigoDocumentItemApiDto>();
    }

    private sealed class SiigoAccountingDocumentApiDto
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = "";

        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("date")]
        public string Date { get; set; } = "";

        [JsonPropertyName("items")]
        public IReadOnlyList<SiigoDocumentItemApiDto> Items { get; set; } = Array.Empty<SiigoDocumentItemApiDto>();
    }

    private sealed class SiigoDocumentReferenceApiDto
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = "";

        [JsonPropertyName("name")]
        public string Name { get; set; } = "";
    }

    private sealed class SiigoPurchaseSupplierApiDto
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = "";

        [JsonPropertyName("identification")]
        public string Identification { get; set; } = "";

        [JsonPropertyName("branch_office")]
        public int BranchOffice { get; set; }
    }

    private sealed class SiigoProviderInvoiceApiDto
    {
        [JsonPropertyName("prefix")]
        public string Prefix { get; set; } = "";

        [JsonPropertyName("number")]
        public string Number { get; set; } = "";
    }

    private sealed class SiigoInvoiceDataApiDto
    {
        [JsonPropertyName("prefix")]
        public string Prefix { get; set; } = "";

        [JsonPropertyName("number")]
        public long? Number { get; set; }
    }

    private sealed class SiigoMetadataApiDto
    {
        [JsonPropertyName("created")]
        public string Created { get; set; } = "";
    }

    private sealed class SiigoDocumentItemApiDto
    {
        [JsonPropertyName("account")]
        public SiigoDocumentAccountApiDto? Account { get; set; }

        [JsonPropertyName("code")]
        public string Code { get; set; } = "";

        [JsonPropertyName("type")]
        public string Type { get; set; } = "";

        [JsonPropertyName("description")]
        public string Description { get; set; } = "";

        [JsonPropertyName("value")]
        public decimal Value { get; set; }

        [JsonPropertyName("taxes")]
        public IReadOnlyList<SiigoDocumentTaxApiDto> Taxes { get; set; } = Array.Empty<SiigoDocumentTaxApiDto>();
    }

    private sealed class SiigoDocumentAccountApiDto
    {
        [JsonPropertyName("code")]
        public string Code { get; set; } = "";

        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("movement")]
        public string Movement { get; set; } = "";
    }

    private sealed class SiigoDocumentTaxApiDto
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("type")]
        public string Type { get; set; } = "";

        [JsonPropertyName("value")]
        public decimal Value { get; set; }
    }

    private sealed class SiigoTaxApiDto
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("type")]
        public string? Type { get; set; }

        [JsonPropertyName("percentage")]
        public decimal Percentage { get; set; }

        [JsonPropertyName("active")]
        public bool Active { get; set; }
    }

    private sealed class SiigoDocumentTypeApiDto
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("code")]
        public string? Code { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("description")]
        public string? Description { get; set; }

        [JsonPropertyName("type")]
        public string? Type { get; set; }

        [JsonPropertyName("active")]
        public bool Active { get; set; }

        [JsonPropertyName("automatic_number")]
        public bool AutomaticNumber { get; set; }

        [JsonPropertyName("consecutive")]
        public int Consecutive { get; set; }
    }

    private sealed class SiigoInvoiceCustomerApiDto
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = "";

        [JsonPropertyName("identification")]
        public string Identification { get; set; } = "";

        [JsonPropertyName("branch_office")]
        public int BranchOffice { get; set; }
    }

    private sealed class SiigoInvoiceStampApiDto
    {
        [JsonPropertyName("status")]
        public string Status { get; set; } = "";

        [JsonPropertyName("observations")]
        public string Observations { get; set; } = "";

        [JsonPropertyName("errors")]
        public string Errors { get; set; } = "";
    }

    private sealed class SiigoCreditNoteStampApiDto
    {
        [JsonPropertyName("status")]
        public string Status { get; set; } = "";

        [JsonPropertyName("cude")]
        public string Cude { get; set; } = "";
    }

    private sealed class SiigoInvoiceMailApiDto
    {
        [JsonPropertyName("status")]
        public string Status { get; set; } = "";

        [JsonPropertyName("observations")]
        public string Observations { get; set; } = "";
    }

    private sealed class SiigoPdfResponseApiDto
    {
        [JsonPropertyName("base64")]
        public string Base64 { get; set; } = "";
    }
}
