using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using CotizadorInterno.Web.Models.Dashboard;
using Microsoft.Extensions.Caching.Memory;

namespace CotizadorInterno.Web.Services;

public sealed partial class DataverseService
{
    private const string CloudProductsTotalBusinessCacheKeyPrefix = "dashboard:cloud-products:total-business";
    private static readonly SemaphoreSlim CloudProductsTotalBusinessCacheGate = new(1, 1);
    private const string CloudBillingBilledField = "cr07a_facturado";
    private const string CloudBillingLastInvoiceDateField = "cr07a_fechaultimafactura";
    private const string CloudBillingErrorField = "cr07a_error_facturacion";
    private const string CloudBillingSiigoInvoiceIdField = "cr07a_siigo_invoice_id";
    private const string CloudBillingProductNameField = "cr07a_productname";
    private const string CloudBillingMonthlyTotalField = "cr07a_valorventatotalmensual";

    public async Task<decimal> GetCloudProductsTotalBusinessUsdAsync(CancellationToken ct = default)
    {
        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No HttpContext available.");
        var monthKey = GetBogotaToday().ToString("yyyy-MM", CultureInfo.InvariantCulture);
        var cacheKey = $"{CloudProductsTotalBusinessCacheKeyPrefix}:{_dataverseBaseUrl}:{_salesPerformanceTableSetName}:{monthKey}";

        if (_memoryCache.TryGetValue(cacheKey, out decimal cachedTotal))
            return cachedTotal;

        await CloudProductsTotalBusinessCacheGate.WaitAsync(ct);
        try
        {
            if (_memoryCache.TryGetValue(cacheKey, out cachedTotal))
                return cachedTotal;

            var products = await GetCloudProductsBusinessValuesAsync(httpContext.User, ct);
            var total = CalculateCloudProductsTotalBusinessUsd(
                products);

            // La clave cambia con el mes; la expiración solo retira del proceso la entrada anterior.
            _memoryCache.Set(
                cacheKey,
                total,
                new MemoryCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromDays(40),
                    Size = null
                });

            return total;
        }
        finally
        {
            CloudProductsTotalBusinessCacheGate.Release();
        }
    }

    internal static decimal CalculateCloudProductsTotalBusinessUsd(
        IEnumerable<(int Quantity, decimal UnitSaleUsd)> products) =>
        RoundCurrency(products.Sum(static product => product.Quantity * product.UnitSaleUsd));

    private async Task<IReadOnlyList<(int Quantity, decimal UnitSaleUsd)>> GetCloudProductsBusinessValuesAsync(
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var select = string.Join(",", new[]
        {
            _salesPerformanceIdField,
            DefaultSalesPerformanceQuantityField,
            DefaultSalesPerformanceUnitSaleUsdField
        });
        var relativeUrl = $"/api/data/v9.2/{_salesPerformanceTableSetName}?$select={select}";
        var items = await GetDataverseEntitiesAsync(relativeUrl, user, ct);

        return items
            .Where(item => !string.IsNullOrWhiteSpace(ReadString(item, _salesPerformanceIdField)))
            .GroupBy(item => ReadString(item, _salesPerformanceIdField), StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.First())
            .Select(item => (
                ReadIntFlexible(item, DefaultSalesPerformanceQuantityField),
                ReadDecimal(item, DefaultSalesPerformanceUnitSaleUsdField) ?? 0m))
            .ToList();
    }

    public async Task<CloudBillingCurrentMonthDashboardDto> GetCloudBillingCurrentMonthDashboardAsync(CancellationToken ct = default)
    {
        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No HttpContext available.");

        var today = GetBogotaToday();
        var monthStart = new DateOnly(today.Year, today.Month, 1);
        var monthEnd = monthStart.AddMonths(1);
        var productRowsTask = GetCloudBillingProductRowsAsync(httpContext.User, ct);

        var billingMetadata = await ResolveRhEntityMetadataAsync(
            _dashboardBillingTableLogicalName,
            _dashboardBillingTableSetName,
            _dashboardBillingIdField,
            _dashboardBillingPrimaryNameField,
            httpContext.User,
            ct);

        var monthInvoices = await GetSiigoRevenueLedgerRowsAsync(
            billingMetadata,
            monthStart,
            monthEnd,
            httpContext.User,
            ct);

        var acceptedInvoices = monthInvoices
            .Where(static row => !row.IsCreditNoteLedgerEntry)
            .ToList();
        var cloudInvoices = acceptedInvoices
            .Where(IsCloudBillingInvoice)
            .ToList();
        var productRows = await productRowsTask;
        var rows = productRows
            .Where(static product => product.IsAutomaticBilling)
            .Select(product => BuildCloudBillingCurrentMonthRow(product, acceptedInvoices, cloudInvoices, monthStart, monthEnd, today))
            .OrderBy(GetCloudBillingStatusOrder)
            .ThenBy(static row => row.ExpectedBillingDateValue, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static row => row.ClientName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static row => row.ProductName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var billedRows = rows.Where(static row => row.IsBilled).ToList();
        var pendingRows = rows.Where(static row => row.IsPending).ToList();
        var dueTodayRows = rows.Where(static row => row.IsDueToday).ToList();
        var overdueRows = rows.Where(static row => row.IsOverdue).ToList();
        var errorRows = rows.Where(static row => row.HasBillingError).ToList();

        return new CloudBillingCurrentMonthDashboardDto
        {
            AsOfDateLabel = today.ToString("dd MMM yyyy", DashboardCulture),
            PeriodLabel = ToTitleCase(monthStart.ToString("MMMM yyyy", DashboardCulture)),
            DateRangeLabel = BuildDateRangeLabel(monthStart, monthEnd),
            HasData = rows.Count > 0,
            RecordsCount = rows.Count,
            BilledCount = billedRows.Count,
            PendingCount = pendingRows.Count,
            DueTodayCount = dueTodayRows.Count,
            OverdueCount = overdueRows.Count,
            ErrorCount = errorRows.Count,
            TotalMonthlyUsd = RoundCurrency(rows.Sum(static row => row.MonthlyBillingUsd)),
            BilledMonthlyUsd = RoundCurrency(billedRows.Sum(static row => row.MonthlyBillingUsd)),
            PendingMonthlyUsd = RoundCurrency(pendingRows.Sum(static row => row.MonthlyBillingUsd)),
            DueTodayMonthlyUsd = RoundCurrency(dueTodayRows.Sum(static row => row.MonthlyBillingUsd)),
            OverdueMonthlyUsd = RoundCurrency(overdueRows.Sum(static row => row.MonthlyBillingUsd)),
            EmptyStateTitle = "No encontramos productos Cloud para revisar.",
            EmptyStateMessage = "Cuando existan filas en Productos Cloud apareceran aqui con su estado de facturacion del mes actual.",
            Kpis = BuildCloudBillingKpis(billedRows, pendingRows, dueTodayRows, overdueRows, errorRows),
            Rows = rows
        };
    }

    private async Task<List<CloudBillingProductRecord>> GetCloudBillingProductRowsAsync(ClaimsPrincipal user, CancellationToken ct)
    {
        var relativeUrl = $"/api/data/v9.2/{_salesPerformanceTableSetName}";
        var items = await GetDataverseEntitiesAsync(relativeUrl, user, ct, AddFormattedValueHeaders);

        return items
            .Select(ParseCloudBillingProductRow)
            .Where(static row => row is not null)
            .Cast<CloudBillingProductRecord>()
            .GroupBy(static row => row.RecordId, StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.First())
            .ToList();
    }

    private CloudBillingProductRecord? ParseCloudBillingProductRow(JsonElement item)
    {
        var recordId = ReadString(item, _salesPerformanceIdField);
        if (string.IsNullOrWhiteSpace(recordId))
            return null;

        var clientLookupProperty = DetectLookupValueProperty(item, SalesPerformanceClientLookupFieldCandidates, "cliente");
        var productLookupProperty = DetectLookupValueProperty(item, SalesPerformanceProductLookupFieldCandidates, "producto");
        var productLineOptionValue = ReadOptionValue(item, _salesPerformanceProductLineField);
        var contractTypeOptionValue = ReadOptionValue(item, _salesPerformanceContractTypeField);
        var quantity = ReadIntFlexible(item, DefaultSalesPerformanceQuantityField);
        var unitSaleUsd = RoundCurrency(ReadDecimal(item, DefaultSalesPerformanceUnitSaleUsdField) ?? 0m);
        var monthlyTotal = RoundCurrency(ReadDecimal(item, CloudBillingMonthlyTotalField) ?? 0m);

        if (monthlyTotal == 0m && quantity != 0 && unitSaleUsd != 0m)
            monthlyTotal = RoundCurrency(quantity * unitSaleUsd);

        return new CloudBillingProductRecord
        {
            RecordId = recordId.Trim(),
            ClientId = ReadString(item, clientLookupProperty).Trim(),
            ClientName = FirstNonEmpty(
                ReadLookupFormattedValue(item, clientLookupProperty),
                ReadString(item, $"{_salesPerformanceClientLookupLogicalName}{FormattedValueAnnotationSuffix}"),
                ReadString(item, _salesPerformanceClientLookupLogicalName),
                "Cliente sin asignar").Trim(),
            ProductId = ReadString(item, productLookupProperty).Trim(),
            ProductName = FirstNonEmpty(
                ReadLookupFormattedValue(item, productLookupProperty),
                ReadString(item, CloudBillingProductNameField),
                ReadString(item, _salesPerformancePrimaryNameField),
                "Producto sin asignar").Trim(),
            ProductLineLabel = ResolveBusinessProductLineLabel(item, productLineOptionValue),
            ContractTypeLabel = ResolveBusinessContractTypeLabel(item, contractTypeOptionValue),
            Quantity = quantity,
            UnitSaleUsd = unitSaleUsd,
            MonthlyBillingUsd = monthlyTotal,
            IsAutomaticBilling = ReadYesNoOptionFlexible(item, _salesPerformanceAutoBillField),
            ProductBilledFlag = ReadYesNoOptionFlexible(item, CloudBillingBilledField) || ReadBool(item, CloudBillingBilledField),
            BillingDay = ReadIntFlexible(item, _salesPerformanceBillingDayField),
            LastInvoiceDate = ReadDateOnly(item, CloudBillingLastInvoiceDateField),
            LastSiigoInvoiceId = ReadString(item, CloudBillingSiigoInvoiceIdField).Trim(),
            BillingError = ReadString(item, CloudBillingErrorField).Trim()
        };
    }

    private static CloudBillingCurrentMonthRowDto BuildCloudBillingCurrentMonthRow(
        CloudBillingProductRecord product,
        IReadOnlyList<BillingRecordRow> acceptedInvoices,
        IReadOnlyList<BillingRecordRow> cloudInvoices,
        DateOnly monthStart,
        DateOnly monthEnd,
        DateOnly today)
    {
        var expectedDate = ResolveCloudBillingExpectedDate(monthStart.Year, monthStart.Month, product.BillingDay);
        var hasMonthlyLog = product.LastInvoiceDate is not null
            && product.LastInvoiceDate.Value >= monthStart
            && product.LastInvoiceDate.Value < monthEnd;
        // El ID Siigo es evidencia documental exacta y no debe depender de que
        // Dataverse haya logrado clasificar la factura en la vertical Cloud.
        var directInvoiceMatches = FindDirectCloudInvoiceMatches(product, acceptedInvoices);
        var clientInvoiceMatches = directInvoiceMatches.Count > 0
            ? directInvoiceMatches
            : FindClientCloudInvoiceMatches(product, cloudInvoices);
        var billedByInvoiceTable = directInvoiceMatches.Count > 0;
        var isBilled = hasMonthlyLog || billedByInvoiceTable;
        var billingError = ResolveCurrentMonthCloudBillingError(product.BillingError, product.LastInvoiceDate, monthStart, monthEnd);
        var hasBillingError = !string.IsNullOrWhiteSpace(billingError);
        var isPending = product.IsAutomaticBilling
            && product.BillingDay > 0
            && !isBilled
            && expectedDate is not null
            && expectedDate.Value > today;
        var isDueToday = product.IsAutomaticBilling
            && product.BillingDay > 0
            && !isBilled
            && expectedDate is not null
            && expectedDate.Value == today;
        var isOverdue = product.IsAutomaticBilling
            && product.BillingDay > 0
            && !isBilled
            && expectedDate is not null
            && expectedDate.Value < today;
        var status = ResolveCloudBillingStatus(product, isBilled, isPending, isDueToday, isOverdue, hasBillingError);

        return new CloudBillingCurrentMonthRowDto
        {
            RecordId = product.RecordId,
            ClientId = product.ClientId,
            ClientName = product.ClientName,
            ProductId = product.ProductId,
            ProductName = product.ProductName,
            ProductLineLabel = product.ProductLineLabel,
            ContractTypeLabel = product.ContractTypeLabel,
            Quantity = product.Quantity,
            UnitSaleUsd = product.UnitSaleUsd,
            MonthlyBillingUsd = product.MonthlyBillingUsd,
            IsAutomaticBilling = product.IsAutomaticBilling,
            ProductBilledFlag = product.ProductBilledFlag,
            BillingDay = product.BillingDay,
            BillingDayDisplay = product.BillingDay > 0
                ? product.BillingDay.ToString(CultureInfo.InvariantCulture)
                : "Sin dia",
            ExpectedBillingDateValue = expectedDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "",
            ExpectedBillingDateDisplay = expectedDate?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) ?? "Sin fecha",
            LastInvoiceDateValue = product.LastInvoiceDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "",
            LastInvoiceDateDisplay = product.LastInvoiceDate?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) ?? "Sin factura",
            LastSiigoInvoiceId = product.LastSiigoInvoiceId,
            BillingError = billingError,
            MonthInvoiceNumbers = BuildCloudInvoiceReferenceLabel(clientInvoiceMatches),
            MonthInvoiceCount = clientInvoiceMatches.Count,
            MonthInvoices = BuildCloudInvoiceReferences(clientInvoiceMatches),
            MatchedByInvoiceTable = billedByInvoiceTable,
            IsBilled = isBilled,
            IsPending = isPending,
            IsDueToday = isDueToday,
            IsOverdue = isOverdue,
            HasBillingError = hasBillingError,
            StatusKey = status.Key,
            StatusLabel = status.Label,
            StatusTone = status.Tone,
            EvidenceLabel = ResolveCloudBillingEvidenceLabel(isBilled, hasMonthlyLog, billedByInvoiceTable, billingError, clientInvoiceMatches)
        };
    }

    private static IReadOnlyList<PortfolioKpiDto> BuildCloudBillingKpis(
        IReadOnlyList<CloudBillingCurrentMonthRowDto> billedRows,
        IReadOnlyList<CloudBillingCurrentMonthRowDto> pendingRows,
        IReadOnlyList<CloudBillingCurrentMonthRowDto> dueTodayRows,
        IReadOnlyList<CloudBillingCurrentMonthRowDto> overdueRows,
        IReadOnlyList<CloudBillingCurrentMonthRowDto> errorRows)
    {
        return new[]
        {
            BuildCloudBillingKpi("billed", "Facturados", "Filas con fecha ultima factura en el mes o match directo con factura Siigo.", billedRows.Count, billedRows.Sum(static row => row.MonthlyBillingUsd)),
            BuildCloudBillingKpi("pending", "Por llegar", "No facturados cuyo dia de facturacion aun no llega.", pendingRows.Count, pendingRows.Sum(static row => row.MonthlyBillingUsd)),
            BuildCloudBillingKpi("today", "Hoy", "No facturados con dia de facturacion igual al corte.", dueTodayRows.Count, dueTodayRows.Sum(static row => row.MonthlyBillingUsd)),
            BuildCloudBillingKpi("overdue", "Vencidos", "No facturados cuyo dia de facturacion ya paso.", overdueRows.Count, overdueRows.Sum(static row => row.MonthlyBillingUsd)),
            BuildCloudBillingKpi("errors", "Errores", "Filas con error activo registrado por la automatizacion de Siigo.", errorRows.Count, errorRows.Sum(static row => row.MonthlyBillingUsd))
        };
    }

    private static PortfolioKpiDto BuildCloudBillingKpi(string key, string label, string hint, int count, decimal monthlyUsd) =>
        new()
        {
            Key = key,
            Label = label,
            Hint = hint,
            Value = count,
            ValueFormat = "number",
            SecondaryLabel = "Valor mensual",
            SecondaryValue = FormatUsdValue(RoundCurrency(monthlyUsd))
        };

    private static bool IsCloudBillingInvoice(BillingRecordRow row) =>
        row.VerticalOptionValue == DashboardVerticalCloudOption
        || string.Equals(row.VerticalLabel, "Cloud", StringComparison.OrdinalIgnoreCase);

    private static List<BillingRecordRow> FindDirectCloudInvoiceMatches(
        CloudBillingProductRecord product,
        IReadOnlyList<BillingRecordRow> cloudInvoices)
    {
        var productSiigoId = NormalizeCloudInvoiceReference(product.LastSiigoInvoiceId);
        if (string.IsNullOrWhiteSpace(productSiigoId))
            return new List<BillingRecordRow>();

        return cloudInvoices
            .Where(invoice =>
                string.Equals(NormalizeCloudInvoiceReference(invoice.SiigoInvoiceId), productSiigoId, StringComparison.OrdinalIgnoreCase)
                || string.Equals(NormalizeCloudInvoiceReference(invoice.SiigoInvoiceName), productSiigoId, StringComparison.OrdinalIgnoreCase)
                || string.Equals(NormalizeCloudInvoiceReference(invoice.InvoiceNumber), productSiigoId, StringComparison.OrdinalIgnoreCase)
                || string.Equals(NormalizeCloudInvoiceReference(invoice.InvoiceCode), productSiigoId, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    private static List<BillingRecordRow> FindClientCloudInvoiceMatches(
        CloudBillingProductRecord product,
        IReadOnlyList<BillingRecordRow> cloudInvoices)
    {
        if (string.IsNullOrWhiteSpace(product.ClientId))
            return new List<BillingRecordRow>();

        return cloudInvoices
            .Where(invoice => string.Equals(invoice.ClientId, product.ClientId, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    private static string BuildCloudInvoiceReferenceLabel(IReadOnlyList<BillingRecordRow> invoices)
    {
        if (invoices.Count == 0)
            return "";

        var labels = invoices
            .Select(static invoice => FirstNonEmpty(invoice.InvoiceNumber, invoice.SiigoInvoiceName, invoice.SiigoInvoiceId))
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(3)
            .ToList();

        if (labels.Count == 0)
            return "";

        return invoices.Count > labels.Count
            ? $"{string.Join(", ", labels)} +{invoices.Count - labels.Count}"
            : string.Join(", ", labels);
    }

    private static IReadOnlyList<CloudBillingInvoiceReferenceDto> BuildCloudInvoiceReferences(IReadOnlyList<BillingRecordRow> invoices)
    {
        if (invoices.Count == 0)
            return Array.Empty<CloudBillingInvoiceReferenceDto>();

        return invoices
            .Select(static invoice => new CloudBillingInvoiceReferenceDto
            {
                RecordId = invoice.RecordId,
                InvoiceNumber = invoice.InvoiceNumber,
                InvoiceCode = invoice.InvoiceCode,
                InvoicePrefix = invoice.InvoicePrefix,
                SiigoInvoiceId = invoice.SiigoInvoiceId,
                SiigoInvoiceName = invoice.SiigoInvoiceName
            })
            .ToArray();
    }

    private static DateOnly? ResolveCloudBillingExpectedDate(int year, int month, int billingDay)
    {
        if (billingDay <= 0)
            return null;

        var day = Math.Min(billingDay, DateTime.DaysInMonth(year, month));
        return new DateOnly(year, month, day);
    }

    private static (string Key, string Label, string Tone) ResolveCloudBillingStatus(
        CloudBillingProductRecord product,
        bool isBilled,
        bool isPending,
        bool isDueToday,
        bool isOverdue,
        bool hasBillingError)
    {
        if (isBilled)
            return ("billed", "Facturado", "success");

        if (!product.IsAutomaticBilling)
            return ("manual", "No automatico", "neutral");

        if (product.BillingDay <= 0)
            return ("no-day", "Sin dia", "warning");

        if (hasBillingError)
            return ("error", isOverdue ? "Error Siigo" : "Error previo", "danger");

        if (isOverdue)
            return ("overdue", "Vencido", "danger");

        if (isDueToday)
            return ("today", "Para hoy", "warning");

        if (isPending)
            return ("pending", "Pendiente", "info");

        return ("pending", "Pendiente", "info");
    }

    private static string ResolveCurrentMonthCloudBillingError(
        string? billingError,
        DateOnly? lastInvoiceDate,
        DateOnly monthStart,
        DateOnly monthEnd)
    {
        var cleanError = (billingError ?? "").Trim();
        if (string.IsNullOrWhiteSpace(cleanError))
            return "";

        if (lastInvoiceDate is not null
            && lastInvoiceDate.Value >= monthStart
            && lastInvoiceDate.Value < monthEnd)
        {
            return cleanError;
        }

        var dates = ExtractCloudBillingErrorDates(cleanError);
        if (dates.Count == 0)
            return cleanError;

        return dates.Any(date => date >= monthStart && date < monthEnd)
            ? cleanError
            : "";
    }

    private static IReadOnlyList<DateOnly> ExtractCloudBillingErrorDates(string value)
    {
        var dates = new List<DateOnly>();
        if (string.IsNullOrWhiteSpace(value))
            return dates;

        foreach (var token in value.Split(new[] { ' ', '|', ',', ';', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var cleanToken = token.Trim().Trim('.', ':', ')', ']', '}');
            if (DateOnly.TryParseExact(cleanToken, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var isoDate)
                || DateOnly.TryParseExact(cleanToken, "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out isoDate))
            {
                dates.Add(isoDate);
            }
        }

        return dates;
    }

    private static string ResolveCloudBillingEvidenceLabel(
        bool isBilled,
        bool hasMonthlyLog,
        bool billedByInvoiceTable,
        string billingError,
        IReadOnlyList<BillingRecordRow> monthInvoices)
    {
        if (hasMonthlyLog)
            return "Log producto";

        if (billedByInvoiceTable)
            return "Factura Siigo";

        if (isBilled)
            return "Facturado";

        if (!string.IsNullOrWhiteSpace(billingError))
            return "Error activo";

        if (monthInvoices.Count > 0)
            return "Factura del cliente";

        return "";
    }

    private static int GetCloudBillingStatusOrder(CloudBillingCurrentMonthRowDto row)
    {
        if (row.IsOverdue)
            return 0;

        if (row.HasBillingError && !row.IsBilled)
            return 1;

        if (row.IsDueToday)
            return 2;

        if (row.IsPending)
            return 3;

        if (row.IsBilled)
            return 4;

        return 5;
    }

    private static string NormalizeCloudInvoiceReference(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";

        var chars = value
            .Trim()
            .Where(char.IsLetterOrDigit)
            .Select(char.ToLowerInvariant)
            .ToArray();

        return new string(chars);
    }

    private sealed class CloudBillingProductRecord
    {
        public string RecordId { get; set; } = "";
        public string ClientId { get; set; } = "";
        public string ClientName { get; set; } = "";
        public string ProductId { get; set; } = "";
        public string ProductName { get; set; } = "";
        public string ProductLineLabel { get; set; } = "";
        public string ContractTypeLabel { get; set; } = "";
        public int Quantity { get; set; }
        public decimal UnitSaleUsd { get; set; }
        public decimal MonthlyBillingUsd { get; set; }
        public bool IsAutomaticBilling { get; set; }
        public bool ProductBilledFlag { get; set; }
        public int BillingDay { get; set; }
        public DateOnly? LastInvoiceDate { get; set; }
        public string LastSiigoInvoiceId { get; set; } = "";
        public string BillingError { get; set; } = "";
    }
}
