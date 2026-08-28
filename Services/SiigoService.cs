using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
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
    private const int TransientReadMaxRetries = 3;
    private const int TransientWriteMaxRetries = 3;

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
        var fetchTop = Math.Clamp(Math.Max(requestedTop, 10), 1, 50);
        var digits = ExtractDigits(search);
        var results = new Dictionary<string, SiigoCustomerLookupItemDto>(StringComparer.OrdinalIgnoreCase);

        foreach (var identification in BuildIdentificationCandidates(digits))
        {
            var page = await GetPagedAsync<SiigoCustomerApiDto>(
                "v1/customers",
                new[]
                {
                    Pair("identification", identification),
                    Pair("page", "1"),
                    Pair("page_size", fetchTop.ToString(CultureInfo.InvariantCulture))
                },
                ct);

            AddCustomerResults(results, page.Results.Select(MapCustomer), fetchTop);
            if (results.Values.Any(customer => customer.Active && IsSameIdentificationCandidate(digits, ExtractDigits(customer.Identification))))
                break;
        }

        if ((results.Count < requestedTop || digits.Length < 3) && search.Any(static value => char.IsLetter(value)))
        {
            var normalizedSearch = NormalizeSiigoLookupText(search);
            var nameMatches = (await GetCustomersAsync(ct))
                .Select(customer => new
                {
                    Customer = customer,
                    Score = ScoreCustomerNameMatch(customer, normalizedSearch)
                })
                .Where(static item => item.Score > 0)
                .OrderByDescending(static item => item.Score)
                .ThenBy(static item => item.Customer.Active ? 0 : 1)
                .ThenBy(static item => ResolveCustomerTypeOrder(item.Customer.Type))
                .ThenBy(static item => item.Customer.DisplayName, StringComparer.OrdinalIgnoreCase)
                .Take(requestedTop)
                .Select(static item => item.Customer);

            AddCustomerResults(results, nameMatches, requestedTop);
        }

        return SortCustomers(results.Values).Take(requestedTop).ToList();
    }

    public async Task<ConciliacionSiigoOpenPurchaseSearchResultDto> GetOpenPurchasesAsync(
        string? supplierId,
        string? supplierQuery,
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken ct = default)
    {
        if (startDate > endDate)
            throw new InvalidOperationException("La fecha inicial no puede ser mayor que la fecha final.");

        var supplier = await ResolveCustomerAsync(supplierId, supplierQuery, ct);
        var supplierDigits = ExtractDigits(supplier.Identification);
        if (supplierDigits.Length < 3)
            throw new InvalidOperationException("El proveedor seleccionado no tiene identificacion valida para buscar compras en Siigo.");

        var pageSize = Math.Clamp(_options.PageSize, 25, 100);
        var maxPages = Math.Clamp(_options.MaxReconciliationPages, 1, 500);
        var purchases = await GetAllPagedAsync<SiigoPurchaseApiDto>(
            "v1/purchases",
            new[]
            {
                Pair("date_start", FormatSiigoDate(startDate)),
                Pair("date_end", FormatSiigoDate(endDate.AddDays(1)))
            },
            pageSize,
            maxPages,
            ct);

        var openPurchases = purchases
            .Where(purchase => IsInsidePeriod(ParseSiigoDate(purchase.Date), startDate, endDate))
            .Where(purchase => IsSameIdentificationCandidate(supplierDigits, ExtractDigits(purchase.Supplier?.Identification)))
            .Where(static purchase => RoundCurrency(purchase.Balance) > 0m)
            .Select(MapOpenPurchase)
            .OrderBy(static purchase => purchase.DateValue, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static purchase => purchase.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new ConciliacionSiigoOpenPurchaseSearchResultDto
        {
            Message = openPurchases.Count == 0
                ? $"No encontramos facturas de compra con saldo pendiente para {supplier.DisplayName}."
                : $"Encontramos {openPurchases.Count:N0} factura{(openPurchases.Count == 1 ? "" : "s")} de compra con saldo pendiente.",
            Supplier = MapConciliacionSupplier(supplier),
            Purchases = openPurchases
        };
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
                    Pair("date_end", FormatSiigoDate(endDate.AddDays(1))),
                    Pair("page", page.ToString(CultureInfo.InvariantCulture)),
                    Pair("page_size", pageSize.ToString(CultureInfo.InvariantCulture))
                },
                ct);

            invoices.AddRange(response.Results.Select(MapInvoice));

            if (ShouldStopPaging(response.Pagination, page, pageSize, response.Results.Count))
                break;
        }

        var sortedInvoices = invoices
            .Where(invoice => IsInsidePeriod(ParseSiigoDate(invoice.DateValue), startDate, endDate))
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
            TotalAmount = sortedInvoices.Sum(static invoice => invoice.GrossTotal),
            TotalBalance = sortedInvoices.Sum(static invoice => invoice.GrossBalance),
            EmptyStateTitle = "Sin facturas en Siigo",
            EmptyStateMessage = $"No encontramos facturas de venta para {customer.DisplayName} entre {startDate:dd/MM/yyyy} y {endDate:dd/MM/yyyy}.",
            Invoices = sortedInvoices
        };
    }

    public async Task<IReadOnlyList<SiigoInvoiceRowDto>> GetInvoicesByDateRangeAsync(
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken ct = default)
    {
        if (startDate > endDate)
            throw new InvalidOperationException("La fecha inicial no puede ser mayor que la fecha final.");

        var pageSize = Math.Clamp(_options.PageSize, 25, 100);
        var maxPages = Math.Clamp(_options.MaxInvoicePages, 1, 100);
        var invoices = await GetAllPagedAsync<SiigoInvoiceApiDto>(
            "v1/invoices",
            new[]
            {
                Pair("date_start", FormatSiigoDate(startDate)),
                Pair("date_end", FormatSiigoDate(endDate.AddDays(1)))
            },
            pageSize,
            maxPages,
            ct);

        return invoices
            .Where(invoice => IsInsidePeriod(ParseSiigoDate(invoice.Date), startDate, endDate))
            .Select(MapInvoice)
            .OrderByDescending(invoice => invoice.DateValue)
            .ThenBy(invoice => invoice.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<SiigoInvoiceRowDto?> GetInvoiceByIdAsync(
        string invoiceId,
        CancellationToken ct = default)
    {
        var normalizedId = (invoiceId ?? "").Trim();
        if (string.IsNullOrWhiteSpace(normalizedId))
            throw new InvalidOperationException("Debes indicar el id de la factura Siigo.");

        var invoice = await GetAuthorizedJsonAsync<SiigoInvoiceApiDto>(
            $"v1/invoices/{Uri.EscapeDataString(normalizedId)}",
            ct);
        return invoice is null ? null : MapInvoice(invoice);
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
            Pair("date_end", FormatSiigoDate(endDate.AddDays(1)))
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

    public async Task<SiigoFinancialReconciliationData> GetBillingDocumentsAsync(
        DateOnly startInclusive,
        DateOnly endExclusive,
        CancellationToken ct = default)
    {
        if (startInclusive >= endExclusive)
            throw new InvalidOperationException("La fecha inicial debe ser menor que la fecha final exclusiva.");

        var pageSize = Math.Clamp(_options.PageSize, 25, 100);
        var maxPages = Math.Clamp(_options.MaxReconciliationPages, 1, 500);
        var dateParameters = new[]
        {
            Pair("date_start", FormatSiigoDate(startInclusive)),
            Pair("date_end", FormatSiigoDate(endExclusive))
        };

        var invoicesTask = GetAllPagedAsync<SiigoInvoiceApiDto>("v1/invoices", dateParameters, pageSize, maxPages, ct);
        var creditNotesTask = GetAllPagedAsync<SiigoCreditNoteApiDto>("v1/credit-notes", dateParameters, pageSize, maxPages, ct);

        await Task.WhenAll(invoicesTask, creditNotesTask);

        return new SiigoFinancialReconciliationData
        {
            Invoices = invoicesTask.Result
                .Select(MapReconciliationInvoice)
                .Where(row => IsInsideHalfOpenPeriod(row.Date, startInclusive, endExclusive))
                .ToList(),
            CreditNotes = creditNotesTask.Result
                .Select(MapReconciliationCreditNote)
                .Where(row => IsInsideHalfOpenPeriod(row.Date, startInclusive, endExclusive))
                .ToList()
        };
    }

    public async Task<IReadOnlyList<SiigoReconciliationPurchase>> GetPurchasesByDateRangeAsync(
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken ct = default)
    {
        if (startDate > endDate)
            throw new InvalidOperationException("La fecha inicial no puede ser mayor que la fecha final.");

        var pageSize = Math.Clamp(_options.PageSize, 25, 100);
        var maxPages = Math.Clamp(_options.MaxReconciliationPages, 1, 500);
        var purchases = await GetAllPagedAsync<SiigoPurchaseApiDto>(
            "v1/purchases",
            new[]
            {
                Pair("date_start", FormatSiigoDate(startDate)),
                Pair("date_end", FormatSiigoDate(endDate.AddDays(1)))
            },
            pageSize,
            maxPages,
            ct,
            failOnTruncation: true);

        return purchases
            .Select(MapReconciliationPurchase)
            .Where(row => IsInsidePeriod(row.Date, startDate, endDate))
            .OrderByDescending(static row => row.Date)
            .ThenBy(static row => row.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task<SiigoReconciliationPurchase?> GetPurchaseByIdAsync(
        string purchaseId,
        CancellationToken ct = default)
    {
        var normalizedId = (purchaseId ?? "").Trim();
        if (string.IsNullOrWhiteSpace(normalizedId))
            throw new InvalidOperationException("Debes indicar el id de la compra Siigo.");

        var purchase = await GetAuthorizedJsonAsync<SiigoPurchaseApiDto>(
            $"v1/purchases/{Uri.EscapeDataString(normalizedId)}",
            ct);
        return purchase is null ? null : MapReconciliationPurchase(purchase);
    }

    public async Task<decimal?> GetAccountsPayableBalanceAsync(
        string supplierIdentification,
        string duePrefix,
        int dueConsecutive,
        int dueQuote = 1,
        CancellationToken ct = default)
    {
        var identification = ExtractDigits(supplierIdentification);
        var prefix = (duePrefix ?? "").Trim();
        if (string.IsNullOrWhiteSpace(identification)
            || string.IsNullOrWhiteSpace(prefix)
            || dueConsecutive <= 0
            || dueQuote <= 0)
        {
            throw new InvalidOperationException("El vencimiento de cuenta por pagar no tiene una identidad valida.");
        }

        var pageSize = Math.Clamp(_options.PageSize, 25, 100);
        var maxPages = Math.Clamp(_options.MaxReconciliationPages, 1, 500);
        var rows = await GetAllPagedAsync<SiigoAccountsPayableApiDto>(
            "v1/accounts-payable",
            new[] { Pair("provider_identification", identification) },
            pageSize,
            maxPages,
            ct,
            failOnTruncation: true);
        var matches = rows
            .Where(row => row.Due is not null
                && row.Due.Prefix.Equals(prefix, StringComparison.OrdinalIgnoreCase)
                && row.Due.Consecutive == dueConsecutive
                && row.Due.Quote == dueQuote)
            .ToArray();
        if (matches.Length > 1)
        {
            throw new InvalidOperationException(
                $"Siigo devolvio {matches.Length:N0} saldos para el vencimiento "
                + $"{prefix}-{dueConsecutive}, cuota {dueQuote}.");
        }

        return matches.Length == 0 ? null : RoundCurrency(matches[0].Due!.Balance);
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
            Pair("date_end", FormatSiigoDate(endDate.AddDays(1)))
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

    public async Task<IReadOnlyList<SiigoPaymentTypeLookupDto>> GetPaymentTypesAsync(
        string documentType,
        CancellationToken ct = default)
    {
        var normalizedType = (documentType ?? "").Trim();
        if (string.IsNullOrWhiteSpace(normalizedType))
            throw new InvalidOperationException("Indica el tipo de documento Siigo para consultar formas de pago.");

        var paymentTypes = await GetAuthorizedJsonAsync<List<SiigoPaymentTypeApiDto>>(
            BuildRelativeUrl("v1/payment-types", new[] { Pair("document_type", normalizedType) }),
            ct);

        return paymentTypes
            .Select(static paymentType => new SiigoPaymentTypeLookupDto
            {
                Id = paymentType.Id,
                Name = paymentType.Name?.Trim() ?? "",
                Type = paymentType.Type?.Trim() ?? "",
                Active = paymentType.Active,
                DueDate = paymentType.DueDate
            })
            .Where(static paymentType => paymentType.Id > 0)
            .OrderBy(static paymentType => paymentType.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task<SiigoCustomerLookupItemDto> CreateCustomerAsync(
        object payload,
        string? idempotencyKey = null,
        CancellationToken ct = default)
    {
        if (payload is null)
            throw new ArgumentNullException(nameof(payload));

        var payloadJson = JsonSerializer.Serialize(payload, JsonOptions);
        var expectedIdentification = ReadPayloadString(payloadJson, "identification");
        if (string.IsNullOrWhiteSpace(expectedIdentification))
            throw new InvalidOperationException("El payload del tercero Siigo debe incluir una identificacion.");

        var rawBody = await SendAuthorizedJsonAsync(
            HttpMethod.Post,
            "v1/customers",
            payload,
            idempotencyKey,
            ct,
            allowEmptySuccessResponse: true);
        try
        {
            var customer = JsonSerializer.Deserialize<SiigoCustomerApiDto>(rawBody, JsonOptions)
                ?? throw new InvalidOperationException("Siigo creo el tercero, pero no devolvio datos.");
            var mapped = MapCustomer(customer);
            if (string.IsNullOrWhiteSpace(mapped.Id))
                throw new InvalidOperationException("La respuesta exitosa no incluyo el id del tercero.");
            if (string.IsNullOrWhiteSpace(mapped.Identification))
                throw new InvalidOperationException("La respuesta exitosa no incluyo la identificacion del tercero.");
            if (!AreEquivalentIdentifications(mapped.Identification, expectedIdentification))
            {
                throw new InvalidOperationException(
                    $"La respuesta exitosa devolvio la identificacion '{mapped.Identification}', distinta de la solicitada '{expectedIdentification}'.");
            }

            return mapped;
        }
        catch (Exception ex) when ((ex is JsonException or InvalidOperationException)
                                   && ex is not SiigoSupplierCreateException)
        {
            throw new SiigoSupplierCreateException(
                "Siigo creo el tercero, pero no devolvio una confirmacion durable y coherente. No se repetira el POST hasta verificar el proveedor.",
                payloadJson,
                ex,
                isAmbiguous: true);
        }
    }

    public async Task<SiigoVoucherCreateResultDto> CreatePurchaseAsync(
        object payload,
        string? idempotencyKey = null,
        CancellationToken ct = default)
    {
        if (payload is null)
            throw new ArgumentNullException(nameof(payload));

        var rawBody = await SendAuthorizedJsonAsync(HttpMethod.Post, "v1/purchases", payload, idempotencyKey, ct);
        return ParseCreatedAccountingDocument(rawBody, "Siigo creo la factura de compra, pero no fue posible interpretar la respuesta.");
    }

    public async Task<SiigoVoucherCreateResultDto> CreatePurchaseSupportDocumentAsync(
        object payload,
        string? idempotencyKey = null,
        CancellationToken ct = default)
    {
        if (payload is null)
            throw new ArgumentNullException(nameof(payload));

        var rawBody = await SendAuthorizedJsonAsync(HttpMethod.Post, "v1/purchase-support-documents", payload, idempotencyKey, ct);
        return ParseCreatedAccountingDocument(rawBody, "Siigo creo el documento soporte, pero no fue posible interpretar la respuesta.");
    }

    public async Task<SiigoVoucherCreateResultDto> CreatePaymentReceiptAsync(
        object payload,
        string? idempotencyKey = null,
        CancellationToken ct = default)
    {
        if (payload is null)
            throw new ArgumentNullException(nameof(payload));

        var rawBody = await SendAuthorizedJsonAsync(HttpMethod.Post, "v1/payment-receipts", payload, idempotencyKey, ct);
        return ParseCreatedAccountingDocument(rawBody, "Siigo creo el recibo de pago/egreso, pero no fue posible interpretar la respuesta.");
    }

    public async Task<SiigoVoucherCreateResultDto?> FindPaymentReceiptByObservationAsync(
        string uniqueObservation,
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken ct = default)
    {
        var marker = (uniqueObservation ?? "").Trim();
        if (string.IsNullOrWhiteSpace(marker))
            throw new InvalidOperationException("Debes indicar el identificador unico del recibo de pago/egreso.");
        if (endDate < startDate)
            throw new InvalidOperationException("El rango para buscar el recibo de pago/egreso no es valido.");

        var pageSize = Math.Clamp(_options.PageSize, 25, 100);
        var maxPages = Math.Clamp(_options.MaxReconciliationPages, 1, 500);
        var documents = await GetAllPagedAsync<SiigoAccountingDocumentApiDto>(
            "v1/payment-receipts",
            new[]
            {
                Pair("created_start", FormatSiigoDate(startDate)),
                Pair("created_end", FormatSiigoDate(endDate.AddDays(1)))
            },
            pageSize,
            maxPages,
            ct,
            failOnTruncation: true);
        var matches = documents
            .Where(document => document.Observations.Contains(marker, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (matches.Length > 1)
        {
            throw new InvalidOperationException(
                $"Siigo devolvio {matches.Length:N0} recibos de pago/egreso con el identificador unico {marker}; "
                + "se detuvo la conciliacion para evitar asociar el documento incorrecto.");
        }
        if (matches.Length == 0)
            return null;

        var match = matches[0];
        var detail = await GetAuthorizedJsonAsync<JsonElement>(
            $"v1/payment-receipts/{Uri.EscapeDataString(match.Id)}",
            ct);
        return new SiigoVoucherCreateResultDto
        {
            Id = match.Id,
            Name = match.Name,
            Date = match.Date,
            RawJson = JsonSerializer.Serialize(detail, new JsonSerializerOptions(JsonOptions) { WriteIndented = true })
        };
    }

    public async Task<SiigoVoucherCreateResultDto?> FindJournalByObservationAsync(
        string uniqueObservation,
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken ct = default)
    {
        var marker = (uniqueObservation ?? "").Trim();
        if (string.IsNullOrWhiteSpace(marker))
            throw new InvalidOperationException("Debes indicar el identificador unico del comprobante contable.");
        if (endDate < startDate)
            throw new InvalidOperationException("El rango para buscar el comprobante contable no es valido.");

        var pageSize = Math.Clamp(_options.PageSize, 25, 100);
        var maxPages = Math.Clamp(_options.MaxReconciliationPages, 1, 500);
        var documents = await GetAllPagedAsync<SiigoAccountingDocumentApiDto>(
            "v1/journals",
            new[]
            {
                Pair("created_start", FormatSiigoDate(startDate)),
                Pair("created_end", FormatSiigoDate(endDate.AddDays(1)))
            },
            pageSize,
            maxPages,
            ct,
            failOnTruncation: true);
        var matches = documents
            .Where(document => document.Observations.Contains(marker, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (matches.Length > 1)
        {
            throw new InvalidOperationException(
                $"Siigo devolvio {matches.Length:N0} comprobantes contables con el identificador unico {marker}; "
                + "se detuvo la conciliacion para evitar asociar el documento incorrecto.");
        }
        if (matches.Length == 0)
            return null;

        var match = matches[0];
        var detail = await GetAuthorizedJsonAsync<JsonElement>(
            $"v1/journals/{Uri.EscapeDataString(match.Id)}",
            ct);
        return new SiigoVoucherCreateResultDto
        {
            Id = match.Id,
            Name = match.Name,
            Date = match.Date,
            RawJson = JsonSerializer.Serialize(detail, new JsonSerializerOptions(JsonOptions) { WriteIndented = true })
        };
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
            var result = new SiigoVoucherCreateResultDto
            {
                Id = ReadJsonString(root, "id"),
                Name = ReadJsonString(root, "name"),
                Number = ReadJsonString(root, "number"),
                Date = ReadJsonString(root, "date"),
                RawJson = JsonSerializer.Serialize(root, new JsonSerializerOptions(JsonOptions) { WriteIndented = true })
            };
            if (string.IsNullOrWhiteSpace(result.Id) && string.IsNullOrWhiteSpace(result.Name))
                throw new InvalidOperationException($"{parseErrorMessage} La respuesta exitosa no incluyo id ni nombre del documento.");

            return result;
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
        CancellationToken ct,
        bool failOnTruncation = false)
    {
        var results = new List<T>();
        var baseParameters = parameters.ToList();
        var reachedKnownEnd = false;

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
            {
                reachedKnownEnd = true;
                break;
            }
        }

        if (failOnTruncation && !reachedKnownEnd)
        {
            throw new InvalidOperationException(
                $"La consulta de {path} alcanzo el limite configurado de {maxPages:N0} pagina(s) sin confirmar el final. "
                + "No es seguro crear documentos porque podria existir una compra fuera del resultado consultado.");
        }

        return results;
    }

    private async Task<T> GetAuthorizedJsonAsync<T>(string relativeUrl, CancellationToken ct)
    {
        var transientAttempt = 0;
        var authorizationRetried = false;
        while (true)
        {
            HttpResponseMessage response;
            try
            {
                response = await SendAuthorizedAsync(HttpMethod.Get, relativeUrl, ct);
            }
            catch (HttpRequestException ex) when (transientAttempt < TransientReadMaxRetries)
            {
                await DelayAfterTransientReadExceptionAsync(ex, transientAttempt, ct);
                transientAttempt++;
                continue;
            }
            catch (TaskCanceledException ex) when (!ct.IsCancellationRequested
                                                   && transientAttempt < TransientReadMaxRetries)
            {
                await DelayAfterTransientReadExceptionAsync(ex, transientAttempt, ct);
                transientAttempt++;
                continue;
            }

            using (response)
            {
                if (response.StatusCode == HttpStatusCode.Unauthorized && !authorizationRetried)
                {
                    authorizationRetried = true;
                    InvalidateToken();
                    continue;
                }

                if (await DelayIfTransientReadFailureAsync(response, transientAttempt, ct))
                {
                    transientAttempt++;
                    continue;
                }

                return await ReadJsonResponseAsync<T>(response, ct);
            }
        }
    }

    private async Task<string> SendAuthorizedJsonAsync(
        HttpMethod method,
        string relativeUrl,
        object payload,
        string? idempotencyKey,
        CancellationToken ct,
        bool allowEmptySuccessResponse = false)
    {
        var effectiveIdempotencyKey = SupportsSiigoIdempotency(relativeUrl)
            ? idempotencyKey
            : null;
        var transientAttempt = 0;
        var authorizationRetried = false;
        while (true)
        {
            HttpResponseMessage response;
            try
            {
                response = await SendAuthorizedWithJsonAsync(method, relativeUrl, payload, effectiveIdempotencyKey, ct);
            }
            catch (HttpRequestException ex) when (CanRetryTransientWrite(method, effectiveIdempotencyKey, transientAttempt))
            {
                await DelayAfterTransientWriteExceptionAsync(ex, transientAttempt, ct);
                transientAttempt++;
                continue;
            }
            catch (TaskCanceledException ex) when (!ct.IsCancellationRequested
                                                   && CanRetryTransientWrite(method, effectiveIdempotencyKey, transientAttempt))
            {
                await DelayAfterTransientWriteExceptionAsync(ex, transientAttempt, ct);
                transientAttempt++;
                continue;
            }

            using (response)
            {
                if (response.StatusCode == HttpStatusCode.Unauthorized && !authorizationRetried)
                {
                    authorizationRetried = true;
                    InvalidateToken();
                    continue;
                }

                if (await DelayIfTransientWriteFailureAsync(
                        response,
                        method,
                        effectiveIdempotencyKey,
                        transientAttempt,
                        ct))
                {
                    transientAttempt++;
                    continue;
                }

                return await ReadRawJsonResponseAsync(response, ct, allowEmptySuccessResponse);
            }
        }
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

    private async Task<string> ReadRawJsonResponseAsync(
        HttpResponseMessage response,
        CancellationToken ct,
        bool allowEmptySuccessResponse = false)
    {
        var rawBody = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            var message = ResolveSiigoErrorMessage(rawBody);
            _logger.LogWarning("Siigo API returned {StatusCode}: {Message}", (int)response.StatusCode, message);
            throw new InvalidOperationException($"Siigo respondio {(int)response.StatusCode}: {message}");
        }

        if (string.IsNullOrWhiteSpace(rawBody) && !allowEmptySuccessResponse)
            throw new InvalidOperationException("Siigo devolvio una respuesta vacia.");

        return rawBody;
    }

    private async Task<bool> DelayIfTransientReadFailureAsync(
        HttpResponseMessage response,
        int attempt,
        CancellationToken ct)
    {
        if (!IsTransientReadStatus(response.StatusCode) || attempt >= TransientReadMaxRetries)
            return false;

        var rawBody = await response.Content.ReadAsStringAsync(ct);
        var delay = response.StatusCode == HttpStatusCode.TooManyRequests
            ? ResolveRateLimitDelay(response, rawBody, attempt)
            : ResolveTransientWriteDelay(response, attempt);
        _logger.LogWarning(
            "Siigo no pudo completar temporalmente una consulta ({StatusCode}: {Message}). Reintentando en {DelaySeconds} segundos. Intento {Attempt}/{MaxAttempts}.",
            (int)response.StatusCode,
            ResolveSiigoErrorMessage(rawBody),
            delay.TotalSeconds.ToString("0", CultureInfo.InvariantCulture),
            attempt + 1,
            TransientReadMaxRetries + 1);
        await Task.Delay(delay, ct);
        return true;
    }

    private async Task DelayAfterTransientReadExceptionAsync(Exception ex, int attempt, CancellationToken ct)
    {
        var delay = ResolveTransientWriteDelay(null, attempt);
        _logger.LogWarning(
            ex,
            "La consulta a Siigo fallo temporalmente antes de recibir respuesta. Reintentando en {DelaySeconds} segundos. Intento {Attempt}/{MaxAttempts}.",
            delay.TotalSeconds.ToString("0", CultureInfo.InvariantCulture),
            attempt + 1,
            TransientReadMaxRetries + 1);
        await Task.Delay(delay, ct);
    }

    private async Task<bool> DelayIfTransientWriteFailureAsync(
        HttpResponseMessage response,
        HttpMethod method,
        string? idempotencyKey,
        int attempt,
        CancellationToken ct)
    {
        var canRetryWithIdempotency = CanRetryTransientWrite(method, idempotencyKey, attempt);
        var canRetryRejectedRateLimit = CanRetryRateLimitedWrite(method, response.StatusCode, attempt);
        if ((!canRetryWithIdempotency && !canRetryRejectedRateLimit)
            || !IsTransientWriteStatus(response.StatusCode))
        {
            return false;
        }

        var rawBody = await response.Content.ReadAsStringAsync(ct);
        var delay = response.StatusCode == HttpStatusCode.TooManyRequests
            ? ResolveRateLimitDelay(response, rawBody, attempt)
            : ResolveTransientWriteDelay(response, attempt);
        _logger.LogWarning(
            "Siigo no pudo procesar temporalmente una escritura ({StatusCode}: {Message}). Reintentando en {DelaySeconds} segundos. Intento {Attempt}/{MaxAttempts}.",
            (int)response.StatusCode,
            ResolveSiigoErrorMessage(rawBody),
            delay.TotalSeconds.ToString("0", CultureInfo.InvariantCulture),
            attempt + 1,
            TransientWriteMaxRetries + 1);
        await Task.Delay(delay, ct);
        return true;
    }

    private async Task DelayAfterTransientWriteExceptionAsync(Exception ex, int attempt, CancellationToken ct)
    {
        var delay = ResolveTransientWriteDelay(null, attempt);
        _logger.LogWarning(
            ex,
            "La escritura en Siigo fallo temporalmente antes de recibir respuesta. Reintentando con la misma clave de idempotencia en {DelaySeconds} segundos. Intento {Attempt}/{MaxAttempts}.",
            delay.TotalSeconds.ToString("0", CultureInfo.InvariantCulture),
            attempt + 1,
            TransientWriteMaxRetries + 1);
        await Task.Delay(delay, ct);
    }

    private static bool CanRetryTransientWrite(HttpMethod method, string? idempotencyKey, int attempt) =>
        method == HttpMethod.Post
        && !string.IsNullOrWhiteSpace(idempotencyKey)
        && attempt < TransientWriteMaxRetries;

    internal static bool CanRetryRateLimitedWrite(HttpMethod method, HttpStatusCode statusCode, int attempt) =>
        method == HttpMethod.Post
        && statusCode == HttpStatusCode.TooManyRequests
        && attempt < TransientWriteMaxRetries;

    internal static bool IsTransientReadStatus(HttpStatusCode statusCode) => statusCode is
        HttpStatusCode.RequestTimeout
        or HttpStatusCode.TooManyRequests
        or HttpStatusCode.InternalServerError
        or HttpStatusCode.BadGateway
        or HttpStatusCode.ServiceUnavailable
        or HttpStatusCode.GatewayTimeout;

    private static bool SupportsSiigoIdempotency(string relativeUrl)
    {
        var path = (relativeUrl ?? "").Split('?', 2)[0].Trim('/');
        return path.Equals("v1/invoices", StringComparison.OrdinalIgnoreCase)
            || path.Equals("v1/credit-notes", StringComparison.OrdinalIgnoreCase)
            || path.Equals("v1/journals", StringComparison.OrdinalIgnoreCase)
            || path.Equals("v1/vouchers", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsTransientWriteStatus(HttpStatusCode statusCode) => statusCode is
        HttpStatusCode.RequestTimeout
        or HttpStatusCode.TooManyRequests
        or HttpStatusCode.InternalServerError
        or HttpStatusCode.BadGateway
        or HttpStatusCode.ServiceUnavailable
        or HttpStatusCode.GatewayTimeout;

    private static TimeSpan ResolveTransientWriteDelay(HttpResponseMessage? response, int attempt)
    {
        if (response?.Headers.RetryAfter?.Delta is { } delta && delta > TimeSpan.Zero)
            return ClampRateLimitDelay(delta);

        if (response?.Headers.RetryAfter?.Date is { } date)
        {
            var until = date - DateTimeOffset.UtcNow;
            if (until > TimeSpan.Zero)
                return ClampRateLimitDelay(until);
        }

        var seconds = attempt switch
        {
            0 => 3,
            1 => 8,
            _ => 18
        };
        return TimeSpan.FromSeconds(seconds);
    }

    private static TimeSpan ResolveRateLimitDelay(HttpResponseMessage response, string rawBody, int attempt)
    {
        if (response.Headers.RetryAfter?.Delta is { } delta && delta > TimeSpan.Zero)
            return ClampRateLimitDelay(delta);

        if (response.Headers.RetryAfter?.Date is { } date)
        {
            var until = date - DateTimeOffset.UtcNow;
            if (until > TimeSpan.Zero)
                return ClampRateLimitDelay(until);
        }

        var match = Regex.Match(rawBody ?? "", @"try again in\s+(\d+)\s+seconds", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (match.Success && int.TryParse(match.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds))
            return ClampRateLimitDelay(TimeSpan.FromSeconds(seconds + 2));

        return ClampRateLimitDelay(TimeSpan.FromSeconds(8 * (attempt + 1)));
    }

    private static TimeSpan ClampRateLimitDelay(TimeSpan delay)
    {
        var seconds = Math.Clamp(delay.TotalSeconds, 2d, 90d);
        return TimeSpan.FromSeconds(seconds);
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

    private static string ReadPayloadString(string payloadJson, string propertyName)
    {
        using var document = JsonDocument.Parse(payloadJson);
        return document.RootElement.ValueKind == JsonValueKind.Object
            ? ReadJsonString(document.RootElement, propertyName).Trim()
            : "";
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

        var vat = SumVat(invoice.Items);
        var suggestedWithholdingTotal = SumSuggestedWithholdings(invoice.Items, invoice.Retentions);
        var grossTotal = RoundCurrency(invoice.Total + suggestedWithholdingTotal);
        var grossBalance = RoundCurrency(invoice.Balance);
        if (grossBalance > 0m)
            grossBalance = RoundCurrency(grossBalance + suggestedWithholdingTotal);

        var dueReference = ResolveInvoiceDueReference(invoice);

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
            GrossTotal = grossTotal,
            SuggestedWithholdingTotal = suggestedWithholdingTotal,
            Vat = vat,
            Balance = invoice.Balance,
            GrossBalance = grossBalance,
            DuePrefix = dueReference.Prefix,
            DueConsecutive = dueReference.Consecutive,
            DueQuote = dueReference.Quote,
            DueDateValue = dueReference.DateValue,
            DueDateDisplay = dueReference.DateDisplay,
            HasExactDueReference = dueReference.IsExact,
            DueReferenceIssue = dueReference.Issue,
            StampStatus = invoice.Stamp?.Status?.Trim() ?? "",
            StampObservations = invoice.Stamp?.Observations?.Trim() ?? "",
            StampErrors = invoice.Stamp?.Errors?.Trim() ?? "",
            MailStatus = invoice.Mail?.Status?.Trim() ?? "",
            MailObservations = invoice.Mail?.Observations?.Trim() ?? "",
            Annulled = invoice.Annulled
        };
    }

    private static SiigoInvoiceDueReference ResolveInvoiceDueReference(SiigoInvoiceApiDto invoice)
    {
        var dueDates = (invoice.Payments ?? Array.Empty<SiigoInvoicePaymentApiDto>())
            .Select(static payment => payment.DueDate)
            .ToArray();
        if (!TryResolveInvoiceDueReference(
                invoice.Name,
                invoice.Number,
                dueDates,
                out var prefix,
                out var number,
                out var quote,
                out var dateValue,
                out var issue))
        {
            return new SiigoInvoiceDueReference(prefix, number, quote, dateValue, "", false, issue);
        }

        var dueDate = DateOnly.ParseExact(dateValue, "yyyy-MM-dd", CultureInfo.InvariantCulture);

        return new SiigoInvoiceDueReference(
            prefix,
            number,
            quote,
            dateValue,
            dueDate.ToString("dd/MM/yyyy", ColombianCulture),
            true,
            "");
    }

    internal static bool TryResolveInvoiceDueReference(
        string? invoiceName,
        long? invoiceNumber,
        IReadOnlyList<string>? paymentDueDates,
        out string prefix,
        out int consecutive,
        out int quote,
        out string dateValue,
        out string issue)
    {
        prefix = "";
        consecutive = invoiceNumber is > 0 and <= int.MaxValue
            ? (int)invoiceNumber.Value
            : 0;
        quote = 0;
        dateValue = "";
        issue = "";

        if (!TryResolveInvoiceDuePrefix(invoiceName, consecutive, out prefix))
        {
            issue = $"Siigo no devolvio un nombre de cartera valido para {(invoiceName ?? "la factura").Trim()}.";
            return false;
        }

        var dueDates = (paymentDueDates ?? Array.Empty<string>())
            .Where(static dueDate => !string.IsNullOrWhiteSpace(dueDate))
            .Select(static dueDate => dueDate.Trim())
            .ToArray();
        if (dueDates.Length != 1)
        {
            issue = dueDates.Length == 0
                ? $"La factura {invoiceName} no devolvio la fecha de su vencimiento en Siigo."
                : $"La factura {invoiceName} devolvio {dueDates.Length:N0} vencimientos; selecciona una factura con un unico vencimiento.";
            return false;
        }

        quote = 1;
        if (!DateOnly.TryParseExact(
                dueDates[0],
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var dueDate))
        {
            issue = $"La fecha de vencimiento que Siigo devolvio para {invoiceName} no es valida.";
            return false;
        }

        dateValue = dueDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        return true;
    }

    private static bool TryResolveInvoiceDuePrefix(string? invoiceName, int consecutive, out string prefix)
    {
        prefix = "";
        if (consecutive <= 0)
            return false;

        var normalized = Regex.Replace((invoiceName ?? "").Trim().ToUpperInvariant(), @"\s+", "-", RegexOptions.CultureInvariant);
        normalized = Regex.Replace(normalized, @"-+", "-", RegexOptions.CultureInvariant).Trim('-');
        var match = Regex.Match(normalized, @"^(?<prefix>.+)-(?<consecutive>\d+)$", RegexOptions.CultureInvariant);
        if (!match.Success
            || !int.TryParse(match.Groups["consecutive"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedConsecutive)
            || parsedConsecutive != consecutive)
        {
            return false;
        }

        prefix = match.Groups["prefix"].Value.Trim('-');
        return !string.IsNullOrWhiteSpace(prefix);
    }

    private static ConciliacionSiigoOpenPurchaseDto MapOpenPurchase(SiigoPurchaseApiDto purchase)
    {
        var dateDisplay = "";
        if (DateOnly.TryParse(purchase.Date, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            dateDisplay = date.ToString("dd/MM/yyyy", ColombianCulture);

        var providerPrefix = purchase.ProviderInvoice?.Prefix?.Trim() ?? "";
        var providerNumber = purchase.ProviderInvoice?.Number?.Trim() ?? "";
        return new ConciliacionSiigoOpenPurchaseDto
        {
            Id = purchase.Id?.Trim() ?? "",
            Name = purchase.Name?.Trim() ?? "",
            DateValue = purchase.Date?.Trim() ?? "",
            DateDisplay = dateDisplay,
            SupplierIdentification = purchase.Supplier?.Identification?.Trim() ?? "",
            SupplierBranchOffice = purchase.Supplier?.BranchOffice ?? 0,
            ProviderInvoicePrefix = providerPrefix,
            ProviderInvoiceNumber = providerNumber,
            ProviderInvoiceFullNumber = BuildProviderInvoiceFullNumber(providerPrefix, providerNumber),
            Total = RoundCurrency(purchase.Total),
            Balance = RoundCurrency(purchase.Balance)
        };
    }

    private static ConciliacionSiigoSupplierLookupDto MapConciliacionSupplier(SiigoCustomerLookupItemDto customer) =>
        new()
        {
            Id = customer.Id,
            DisplayName = customer.DisplayName,
            Name = customer.Name,
            CommercialName = customer.CommercialName,
            Identification = customer.Identification,
            Type = customer.Type,
            BranchOffice = customer.BranchOffice,
            Active = customer.Active
        };

    private static SiigoReconciliationInvoice MapReconciliationInvoice(SiigoInvoiceApiDto invoice)
    {
        var suggestedWithholdingTotal = SumSuggestedWithholdings(invoice.Items, invoice.Retentions);

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
            SuggestedWithholdingTotal = suggestedWithholdingTotal,
            GrossTotal = RoundCurrency(invoice.Total + suggestedWithholdingTotal),
            Annulled = invoice.Annulled,
            StampStatus = invoice.Stamp?.Status?.Trim() ?? "",
            RawJson = JsonSerializer.Serialize(invoice, JsonOptions)
        };
    }

    private static SiigoReconciliationCreditNote MapReconciliationCreditNote(SiigoCreditNoteApiDto creditNote)
    {
        var suggestedWithholdingTotal = SumSuggestedWithholdings(creditNote.Items, creditNote.Retentions);

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
            SuggestedWithholdingTotal = suggestedWithholdingTotal,
            GrossTotal = RoundCurrency(creditNote.Total + suggestedWithholdingTotal),
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
            PaymentDueDate = purchase.Payments
                .Select(static payment => ParseSiigoDate(payment.DueDate))
                .FirstOrDefault(static dueDate => dueDate.HasValue),
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

        if (IsLikelyObservedAccountLineDescription(current)
            && !IsLikelyObservedAccountLineDescription(candidate))
        {
            return true;
        }

        return ScoreObservedAccountName(candidate, code) > ScoreObservedAccountName(current, code);
    }

    private static string NormalizeObservedAccountName(string value)
    {
        var normalized = (value ?? "").Trim();
        var baseIndex = normalized.IndexOf(" Base:", StringComparison.OrdinalIgnoreCase);
        if (baseIndex > 0)
            normalized = normalized[..baseIndex].Trim();

        return Truncate(normalized, 120);
    }

    private static int ScoreObservedAccountName(string name, string code)
    {
        var normalized = NormalizeObservedAccountName(name);
        if (string.IsNullOrWhiteSpace(normalized))
            return 0;

        if (string.Equals(normalized, code, StringComparison.OrdinalIgnoreCase))
            return 10;

        if (IsLikelyObservedAccountLineDescription(normalized))
            return 20;

        return 50;
    }

    private static bool IsLikelyObservedAccountLineDescription(string value)
    {
        var normalized = NormalizeSiigoLookupText(value);
        if (string.IsNullOrWhiteSpace(normalized))
            return false;

        if (normalized.Contains(" base:", StringComparison.OrdinalIgnoreCase))
            return true;

        var words = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (words.Length < 3)
            return false;

        return normalized.StartsWith("nomina ", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("pago nomina ", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("pago banco ", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("proveedor ", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("cuenta de cobro ", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("clientes nacionales ", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("ajuste al peso ", StringComparison.OrdinalIgnoreCase);
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

    private static int ScoreCustomerNameMatch(SiigoCustomerLookupItemDto customer, string normalizedSearch)
    {
        if (string.IsNullOrWhiteSpace(normalizedSearch))
            return 0;

        var display = NormalizeSiigoLookupText(customer.DisplayName);
        var name = NormalizeSiigoLookupText(customer.Name);
        var commercial = NormalizeSiigoLookupText(customer.CommercialName);
        var identification = NormalizeSiigoLookupText(customer.Identification);
        if (string.Equals(identification, normalizedSearch, StringComparison.OrdinalIgnoreCase))
            return 100;
        if (display.StartsWith(normalizedSearch, StringComparison.OrdinalIgnoreCase)
            || name.StartsWith(normalizedSearch, StringComparison.OrdinalIgnoreCase)
            || commercial.StartsWith(normalizedSearch, StringComparison.OrdinalIgnoreCase))
        {
            return 80;
        }
        if (display.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase)
            || name.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase)
            || commercial.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase))
        {
            return 60;
        }

        var tokens = normalizedSearch
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(static token => token.Length >= 2)
            .ToArray();
        if (tokens.Length > 1 && tokens.All(token => display.Contains(token, StringComparison.OrdinalIgnoreCase)
            || name.Contains(token, StringComparison.OrdinalIgnoreCase)
            || commercial.Contains(token, StringComparison.OrdinalIgnoreCase)))
        {
            return 40;
        }

        return 0;
    }

    private static string NormalizeSiigoLookupText(string? value)
    {
        var text = (value ?? "").Trim().ToUpperInvariant();
        text = text
            .Replace("Á", "A", StringComparison.Ordinal)
            .Replace("É", "E", StringComparison.Ordinal)
            .Replace("Í", "I", StringComparison.Ordinal)
            .Replace("Ó", "O", StringComparison.Ordinal)
            .Replace("Ú", "U", StringComparison.Ordinal)
            .Replace("Ü", "U", StringComparison.Ordinal)
            .Replace("Ñ", "N", StringComparison.Ordinal);
        return Regex.Replace(text, @"\s+", " ", RegexOptions.CultureInvariant).Trim();
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

    private static bool IsSameIdentificationCandidate(string leftDigits, string rightDigits)
    {
        if (string.IsNullOrWhiteSpace(leftDigits) || string.IsNullOrWhiteSpace(rightDigits))
            return false;

        if (string.Equals(leftDigits, rightDigits, StringComparison.OrdinalIgnoreCase))
            return true;

        return (leftDigits.Length >= 9 && leftDigits.Length == rightDigits.Length + 1 && leftDigits.StartsWith(rightDigits, StringComparison.OrdinalIgnoreCase))
            || (rightDigits.Length >= 9 && rightDigits.Length == leftDigits.Length + 1 && rightDigits.StartsWith(leftDigits, StringComparison.OrdinalIgnoreCase));
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

    private static bool IsInsideHalfOpenPeriod(DateOnly? date, DateOnly startInclusive, DateOnly endExclusive) =>
        date.HasValue && date.Value >= startInclusive && date.Value < endExclusive;

    private static DateTimeOffset? ParseSiigoDateTimeOffset(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed
            : null;

    private static string ExtractDigits(string? value) =>
        new((value ?? "").Where(char.IsDigit).ToArray());

    private static bool AreEquivalentIdentifications(string actual, string expected)
    {
        var actualDigits = ExtractDigits(actual);
        var expectedDigits = ExtractDigits(expected);
        return actualDigits.Length > 0
            && string.Equals(actualDigits, expectedDigits, StringComparison.Ordinal);
    }

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

    private static decimal SumSuggestedWithholdings(
        IReadOnlyList<SiigoDocumentItemApiDto>? items,
        IReadOnlyList<SiigoDocumentTaxApiDto>? retentions)
    {
        var itemWithholdings = (items ?? Array.Empty<SiigoDocumentItemApiDto>())
            .SelectMany(static item => item.Taxes ?? Array.Empty<SiigoDocumentTaxApiDto>());
        var invoiceWithholdings = retentions ?? Array.Empty<SiigoDocumentTaxApiDto>();

        return RoundCurrency(itemWithholdings
            .Concat(invoiceWithholdings)
            .Where(static tax => tax.Value > 0m
                && (IsWithholdingTax(tax.Type) || IsWithholdingTax(tax.Name)))
            .Sum(static tax => tax.Value));
    }

    private static bool IsWithholdingTax(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var normalized = value.Trim();
        return normalized.Contains("RETE", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("RETENCION", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("WITHHOLD", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsVatTax(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        if (IsWithholdingTax(value))
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

    private sealed class SiigoPaymentTypeApiDto
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("type")]
        public string Type { get; set; } = "";

        [JsonPropertyName("active")]
        public bool Active { get; set; }

        [JsonPropertyName("due_date")]
        public bool DueDate { get; set; }
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

        [JsonPropertyName("retentions")]
        public IReadOnlyList<SiigoDocumentTaxApiDto> Retentions { get; set; } = Array.Empty<SiigoDocumentTaxApiDto>();

        [JsonPropertyName("stamp")]
        public SiigoInvoiceStampApiDto? Stamp { get; set; }

        [JsonPropertyName("mail")]
        public SiigoInvoiceMailApiDto? Mail { get; set; }

        [JsonPropertyName("items")]
        public IReadOnlyList<SiigoDocumentItemApiDto> Items { get; set; } = Array.Empty<SiigoDocumentItemApiDto>();

        [JsonPropertyName("payments")]
        public IReadOnlyList<SiigoInvoicePaymentApiDto> Payments { get; set; } = Array.Empty<SiigoInvoicePaymentApiDto>();

        [JsonPropertyName("annulled")]
        public bool Annulled { get; set; }
    }

    private sealed class SiigoInvoicePaymentApiDto
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("value")]
        public decimal Value { get; set; }

        [JsonPropertyName("due_date")]
        public string DueDate { get; set; } = "";
    }

    private sealed record SiigoInvoiceDueReference(
        string Prefix,
        int Consecutive,
        int Quote,
        string DateValue,
        string DateDisplay,
        bool IsExact,
        string Issue);

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

        [JsonPropertyName("retentions")]
        public IReadOnlyList<SiigoDocumentTaxApiDto> Retentions { get; set; } = Array.Empty<SiigoDocumentTaxApiDto>();

        [JsonPropertyName("items")]
        public IReadOnlyList<SiigoDocumentItemApiDto> Items { get; set; } = Array.Empty<SiigoDocumentItemApiDto>();

    }

    private sealed class SiigoPurchasePaymentApiDto
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("value")]
        public decimal Value { get; set; }

        [JsonPropertyName("due_date")]
        public string DueDate { get; set; } = "";
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

        [JsonPropertyName("payments")]
        public IReadOnlyList<SiigoPurchasePaymentApiDto> Payments { get; set; } = Array.Empty<SiigoPurchasePaymentApiDto>();
    }

    private sealed class SiigoAccountingDocumentApiDto
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = "";

        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("date")]
        public string Date { get; set; } = "";

        [JsonPropertyName("observations")]
        public string Observations { get; set; } = "";

        [JsonPropertyName("items")]
        public IReadOnlyList<SiigoDocumentItemApiDto> Items { get; set; } = Array.Empty<SiigoDocumentItemApiDto>();
    }

    private sealed class SiigoAccountsPayableApiDto
    {
        [JsonPropertyName("due")]
        public SiigoAccountsPayableDueApiDto? Due { get; set; }
    }

    private sealed class SiigoAccountsPayableDueApiDto
    {
        [JsonPropertyName("prefix")]
        public string Prefix { get; set; } = "";

        [JsonPropertyName("consecutive")]
        [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
        public int Consecutive { get; set; }

        [JsonPropertyName("quote")]
        [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
        public int Quote { get; set; }

        [JsonPropertyName("date")]
        public string Date { get; set; } = "";

        [JsonPropertyName("balance")]
        [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
        public decimal Balance { get; set; }
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
