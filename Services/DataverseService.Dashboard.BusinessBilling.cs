using System.Globalization;
using System.Text;
using CotizadorInterno.Web.Models.Dashboard;

namespace CotizadorInterno.Web.Services;

public sealed partial class DataverseService
{
    private const string BusinessBillingGranularityMonth = "month";
    private const string BusinessBillingGranularityQuarter = "quarter";
    private const string BusinessBillingGranularitySemester = "semester";
    private const string BusinessBillingGranularityYear = "year";
    private const string BusinessBillingGranularityAll = "all";
    private const string BusinessBillingContractMonthly = "monthly";
    private const string BusinessBillingContractPrepaid = "prepaid";

    public async Task<BusinessBillingDashboardDto> GetBusinessBillingDashboardAsync(
        DateOnly? startDate,
        DateOnly? endDate,
        string? granularity,
        CancellationToken ct = default)
    {
        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No HttpContext available.");

        var today = GetBogotaToday();
        var requestedStart = startDate.HasValue
            ? new DateOnly(startDate.Value.Year, startDate.Value.Month, 1)
            : new DateOnly(today.Year, 1, 1);
        var requestedEnd = endDate.HasValue
            ? new DateOnly(endDate.Value.Year, endDate.Value.Month, 1)
            : new DateOnly(today.Year, today.Month, 1);

        if (requestedEnd < requestedStart)
        {
            (requestedStart, requestedEnd) = (requestedEnd, requestedStart);
        }

        var endExclusive = requestedEnd.AddMonths(1);
        var resolvedGranularity = NormalizeBusinessBillingGranularity(granularity);
        var periods = BuildBusinessBillingPeriods(requestedStart, endExclusive, resolvedGranularity);
        var queryStart = periods
            .SelectMany(static period => new[]
            {
                period.Start,
                period.PreviousStart,
                period.PreviousYearStart
            })
            .Min();

        var metadata = await ResolveRhEntityMetadataAsync(
            _dashboardBillingTableLogicalName,
            _dashboardBillingTableSetName,
            _dashboardBillingIdField,
            _dashboardBillingPrimaryNameField,
            httpContext.User,
            ct);

        var billingRows = await GetSiigoRevenueLedgerRowsAsync(
            metadata,
            queryStart,
            endExclusive,
            httpContext.User,
            ct);

        var cloud = BuildBusinessBillingSection(
            "cloud",
            "Cloud",
            DashboardVerticalCloudOption,
            periods,
            billingRows);
        var copiers = BuildBusinessBillingSection(
            "copiers",
            "Copiers",
            DashboardVerticalCopiersOption,
            periods,
            billingRows);
        var totalSales = RoundCurrency(cloud.TotalSales + copiers.TotalSales);
        var recordsCount = cloud.RecordsCount + copiers.RecordsCount;

        return new BusinessBillingDashboardDto
        {
            StartDateValue = requestedStart.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            EndDateValue = requestedEnd.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            StartMonthValue = requestedStart.ToString("yyyy-MM", CultureInfo.InvariantCulture),
            EndMonthValue = requestedEnd.ToString("yyyy-MM", CultureInfo.InvariantCulture),
            Granularity = resolvedGranularity,
            GranularityLabel = ResolveBusinessBillingGranularityLabel(resolvedGranularity),
            PeriodLabel = BuildBusinessBillingPeriodLabel(requestedStart, endExclusive, resolvedGranularity),
            DateRangeLabel = BuildDateRangeLabel(requestedStart, endExclusive),
            FocusLabel = "Ventas por vertical y tipo de contrato",
            HasData = recordsCount > 0 || Math.Abs(totalSales) >= 0.01m,
            RecordsCount = recordsCount,
            TotalSales = totalSales,
            EmptyStateMessage = "No hay facturacion Cloud o Copiers para el rango seleccionado.",
            Cloud = cloud,
            Copiers = copiers
        };
    }

    private static BusinessBillingSectionDto BuildBusinessBillingSection(
        string verticalKey,
        string verticalLabel,
        int verticalOption,
        IReadOnlyList<BusinessBillingPeriod> periods,
        IReadOnlyList<BillingRecordRow> billingRows)
    {
        var monthly = BuildBusinessBillingChart(
            verticalKey,
            verticalLabel,
            verticalOption,
            BusinessBillingContractMonthly,
            "Monthly",
            periods,
            billingRows);
        var prepaid = BuildBusinessBillingChart(
            verticalKey,
            verticalLabel,
            verticalOption,
            BusinessBillingContractPrepaid,
            "OneTime / Prepaid",
            periods,
            billingRows);

        return new BusinessBillingSectionDto
        {
            Key = verticalKey,
            Label = verticalLabel,
            TotalSales = RoundCurrency(monthly.TotalSales + prepaid.TotalSales),
            RecordsCount = monthly.RecordsCount + prepaid.RecordsCount,
            Monthly = monthly,
            Prepaid = prepaid
        };
    }

    private static BusinessBillingChartDto BuildBusinessBillingChart(
        string verticalKey,
        string verticalLabel,
        int verticalOption,
        string contractTypeKey,
        string contractTypeLabel,
        IReadOnlyList<BusinessBillingPeriod> periods,
        IReadOnlyList<BillingRecordRow> billingRows)
    {
        var rows = billingRows
            .Where(row => IsBusinessBillingVertical(row, verticalOption)
                && IsBusinessBillingContract(row, contractTypeKey))
            .ToList();
        var points = periods
            .Select(period =>
            {
                var currentRows = FilterBusinessBillingRows(rows, period.Start, period.EndExclusive);
                var previousRows = FilterBusinessBillingRows(rows, period.PreviousStart, period.PreviousEndExclusive);
                var previousYearRows = FilterBusinessBillingRows(rows, period.PreviousYearStart, period.PreviousYearEndExclusive);
                var sales = SumCurrency(currentRows, static row => row.NetBeforeVatValue);
                var previousSales = SumCurrency(previousRows, static row => row.NetBeforeVatValue);
                var previousYearSales = SumCurrency(previousYearRows, static row => row.NetBeforeVatValue);

                return new BusinessBillingPointDto
                {
                    Key = period.Key,
                    Label = period.Label,
                    ShortLabel = period.ShortLabel,
                    StartDateValue = period.Start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    EndDateValue = period.EndExclusive.AddDays(-1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    Sales = sales,
                    RecordsCount = currentRows.Count(static row => !row.IsCreditNoteLedgerEntry),
                    PreviousPeriodSales = previousSales,
                    SamePeriodPreviousYearSales = previousYearSales,
                    PreviousPeriodGrowthPercent = CalculateGrowthPercent(sales, previousSales),
                    SamePeriodPreviousYearGrowthPercent = CalculateGrowthPercent(sales, previousYearSales)
                };
            })
            .ToList();

        return new BusinessBillingChartDto
        {
            Key = $"{verticalKey}-{contractTypeKey}",
            Label = $"{verticalLabel} {contractTypeLabel}",
            VerticalKey = verticalKey,
            ContractTypeKey = contractTypeKey,
            TotalSales = RoundCurrency(points.Sum(static point => point.Sales)),
            RecordsCount = points.Sum(static point => point.RecordsCount),
            Points = points
        };
    }

    private static List<BillingRecordRow> FilterBusinessBillingRows(
        IEnumerable<BillingRecordRow> rows,
        DateOnly startInclusive,
        DateOnly endExclusive) =>
        rows
            .Where(row => row.EmissionDate is not null
                && row.EmissionDate.Value >= startInclusive
                && row.EmissionDate.Value < endExclusive)
            .ToList();

    private static bool IsBusinessBillingVertical(BillingRecordRow row, int verticalOption)
    {
        if (row.VerticalOptionValue == verticalOption)
            return true;

        var normalized = NormalizeBusinessBillingText(row.VerticalLabel);
        return verticalOption == DashboardVerticalCloudOption
            ? normalized.Contains("CLOUD", StringComparison.OrdinalIgnoreCase)
            : normalized.Contains("COPIERS", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsBusinessBillingContract(BillingRecordRow row, string contractTypeKey)
    {
        var normalized = NormalizeBusinessBillingText(row.ContractTypeLabel);
        if (string.Equals(contractTypeKey, BusinessBillingContractMonthly, StringComparison.OrdinalIgnoreCase))
        {
            return row.ContractTypeOptionValue == DashboardContractTypeMonthlyOption
                || normalized.Contains("MONTHLY", StringComparison.OrdinalIgnoreCase)
                || normalized.Contains("MONTLHY", StringComparison.OrdinalIgnoreCase)
                || normalized.Contains("MENSUAL", StringComparison.OrdinalIgnoreCase);
        }

        return row.ContractTypeOptionValue == DashboardContractTypeOneTimeOption
            || normalized.Contains("ONETIME", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("ONE TIME", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("PREPAID", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("PRE PAID", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("PREPAGO", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("ANNUAL", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("ANUAL", StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<BusinessBillingPeriod> BuildBusinessBillingPeriods(
        DateOnly startInclusive,
        DateOnly endExclusive,
        string granularity)
    {
        if (string.Equals(granularity, BusinessBillingGranularityAll, StringComparison.OrdinalIgnoreCase))
        {
            return new[]
            {
                BuildBusinessBillingPeriod(startInclusive, endExclusive, "Todo", "Todo")
            };
        }

        var periods = new List<BusinessBillingPeriod>();
        var cursor = ResolveBusinessBillingCalendarPeriodStart(startInclusive, granularity);
        var monthsPerPeriod = ResolveBusinessBillingPeriodMonths(granularity);

        while (cursor < endExclusive)
        {
            var rawEnd = cursor.AddMonths(monthsPerPeriod);
            var periodStart = MaxBusinessBillingDate(cursor, startInclusive);
            var periodEnd = MinBusinessBillingDate(rawEnd, endExclusive);
            if (periodStart < periodEnd)
            {
                periods.Add(BuildBusinessBillingPeriod(
                    periodStart,
                    periodEnd,
                    ResolveBusinessBillingPeriodLabel(cursor, granularity),
                    ResolveBusinessBillingShortPeriodLabel(cursor, granularity)));
            }

            cursor = rawEnd;
        }

        return periods.Count > 0
            ? periods
            : new[] { BuildBusinessBillingPeriod(startInclusive, endExclusive, "Sin periodo", "Sin periodo") };
    }

    private static BusinessBillingPeriod BuildBusinessBillingPeriod(
        DateOnly startInclusive,
        DateOnly endExclusive,
        string label,
        string shortLabel)
    {
        var periodMonths = Math.Max(1, CalculateBusinessBillingMonthSpan(startInclusive, endExclusive));
        return new BusinessBillingPeriod(
            $"{startInclusive:yyyy-MM}_{endExclusive.AddDays(-1):yyyy-MM-dd}",
            label,
            shortLabel,
            startInclusive,
            endExclusive,
            startInclusive.AddMonths(-periodMonths),
            endExclusive.AddMonths(-periodMonths),
            startInclusive.AddYears(-1),
            endExclusive.AddYears(-1));
    }

    private static DateOnly ResolveBusinessBillingCalendarPeriodStart(DateOnly value, string granularity)
    {
        return granularity switch
        {
            BusinessBillingGranularityQuarter => new DateOnly(value.Year, (((value.Month - 1) / 3) * 3) + 1, 1),
            BusinessBillingGranularitySemester => new DateOnly(value.Year, value.Month <= 6 ? 1 : 7, 1),
            BusinessBillingGranularityYear => new DateOnly(value.Year, 1, 1),
            _ => new DateOnly(value.Year, value.Month, 1)
        };
    }

    private static int ResolveBusinessBillingPeriodMonths(string granularity) => granularity switch
    {
        BusinessBillingGranularityQuarter => 3,
        BusinessBillingGranularitySemester => 6,
        BusinessBillingGranularityYear => 12,
        _ => 1
    };

    private static string ResolveBusinessBillingPeriodLabel(DateOnly periodStart, string granularity)
    {
        return granularity switch
        {
            BusinessBillingGranularityQuarter => $"T{((periodStart.Month - 1) / 3) + 1} {periodStart.Year}",
            BusinessBillingGranularitySemester => $"S{(periodStart.Month <= 6 ? 1 : 2)} {periodStart.Year}",
            BusinessBillingGranularityYear => periodStart.Year.ToString(CultureInfo.InvariantCulture),
            _ => ToTitleCase(periodStart.ToString("MMM yyyy", DashboardCulture))
        };
    }

    private static string ResolveBusinessBillingShortPeriodLabel(DateOnly periodStart, string granularity)
    {
        return granularity switch
        {
            BusinessBillingGranularityQuarter => $"T{((periodStart.Month - 1) / 3) + 1}",
            BusinessBillingGranularitySemester => $"S{(periodStart.Month <= 6 ? 1 : 2)}",
            BusinessBillingGranularityYear => periodStart.Year.ToString(CultureInfo.InvariantCulture),
            _ => ToTitleCase(periodStart.ToString("MMM", DashboardCulture))
        };
    }

    private static int CalculateBusinessBillingMonthSpan(DateOnly startInclusive, DateOnly endExclusive) =>
        ((endExclusive.Year - startInclusive.Year) * 12) + endExclusive.Month - startInclusive.Month;

    private static DateOnly MinBusinessBillingDate(DateOnly left, DateOnly right) =>
        left <= right ? left : right;

    private static DateOnly MaxBusinessBillingDate(DateOnly left, DateOnly right) =>
        left >= right ? left : right;

    private static string NormalizeBusinessBillingGranularity(string? value)
    {
        var normalized = NormalizeBusinessBillingText(value);
        return normalized switch
        {
            "QUARTER" or "TRIMESTRE" => BusinessBillingGranularityQuarter,
            "SEMESTER" or "SEMESTRE" => BusinessBillingGranularitySemester,
            "YEAR" or "ANO" or "ANIO" or "ANUAL" => BusinessBillingGranularityYear,
            "ALL" or "TODO" or "TOTAL" => BusinessBillingGranularityAll,
            _ => BusinessBillingGranularityMonth
        };
    }

    private static string ResolveBusinessBillingGranularityLabel(string granularity) => granularity switch
    {
        BusinessBillingGranularityQuarter => "Trimestre",
        BusinessBillingGranularitySemester => "Semestre",
        BusinessBillingGranularityYear => "Anual",
        BusinessBillingGranularityAll => "Todo",
        _ => "Mes"
    };

    private static string BuildBusinessBillingPeriodLabel(DateOnly startInclusive, DateOnly endExclusive, string granularity)
    {
        if (string.Equals(granularity, BusinessBillingGranularityAll, StringComparison.OrdinalIgnoreCase))
            return $"Todo: {BuildDateRangeLabel(startInclusive, endExclusive)}";

        return $"{ToTitleCase(startInclusive.ToString("MMM yyyy", DashboardCulture))} - {ToTitleCase(endExclusive.AddMonths(-1).ToString("MMM yyyy", DashboardCulture))}";
    }

    private static string NormalizeBusinessBillingText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";

        var normalized = value.Trim().Normalize(NormalizationForm.FormD);
        var builder = new System.Text.StringBuilder(normalized.Length);
        foreach (var ch in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
                builder.Append(char.ToUpperInvariant(ch));
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }

    private sealed record BusinessBillingPeriod(
        string Key,
        string Label,
        string ShortLabel,
        DateOnly Start,
        DateOnly EndExclusive,
        DateOnly PreviousStart,
        DateOnly PreviousEndExclusive,
        DateOnly PreviousYearStart,
        DateOnly PreviousYearEndExclusive);
}
