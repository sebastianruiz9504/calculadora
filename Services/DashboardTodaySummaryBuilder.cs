using System.Globalization;
using System.Text;
using CotizadorInterno.Web.Models.Dashboard;
using CotizadorInterno.Web.Models.SoporteCloud;

namespace CotizadorInterno.Web.Services;

internal static class DashboardTodaySummaryBuilder
{
    private const int CloudVerticalOption = 645250000;
    private const int CopiersVerticalOption = 645250001;
    private static readonly CultureInfo ColombianCulture = CultureInfo.GetCultureInfo("es-CO");

    public static TodayDashboardDto Build(
        DateOnly today,
        PortfolioDashboardDto portfolio,
        YtdDashboardDto currentYearYtd,
        YtdDashboardDto? previousYearYtd,
        SoporteCloudBoardDto currentSupport,
        SoporteCloudBoardDto previousSupport,
        CopiersEquipmentDashboardDto copiersEquipment,
        decimal cloudProductsTotalBusinessUsd)
    {
        var currentStart = new DateOnly(today.Year, today.Month, 1);
        var previousStart = currentStart.AddMonths(-1);
        var previousDay = Math.Min(today.Day, DateTime.DaysInMonth(previousStart.Year, previousStart.Month));
        var previousEnd = previousStart.AddDays(previousDay - 1);

        var invoices = portfolio.Invoices ?? Array.Empty<BillingInvoiceRowDto>();
        var currentInvoices = invoices.Where(row => IsDateInRange(row.EmissionDateValue, currentStart, today)).ToList();
        var previousInvoices = invoices.Where(row => IsDateInRange(row.EmissionDateValue, previousStart, previousEnd)).ToList();
        var currentExpenses = GetExpenseRecords(currentYearYtd, currentStart, today);
        var previousExpenseSource = previousStart.Year == currentYearYtd.Year
            ? currentYearYtd
            : previousYearYtd;
        var previousExpenses = GetExpenseRecords(previousExpenseSource, previousStart, previousEnd);
        var currentMaintenance = (copiersEquipment.MaintenanceRows ?? Array.Empty<CopiersMaintenanceRowDto>())
            .Where(row => IsDateInRange(row.DateValue, currentStart, today))
            .ToList();
        var pendingInvoices = invoices.Where(static row => row.IsPortfolioPending).ToList();
        var overdueInvoices = pendingInvoices.Where(static row => row.IsOverdue).ToList();

        var currentBillingTotal = RoundCurrency(currentInvoices.Sum(static row => row.NetTotalInvoice));
        var previousBillingTotal = RoundCurrency(previousInvoices.Sum(static row => row.NetTotalInvoice));
        var currentExpenseTotal = RoundCurrency(currentExpenses.Sum(static row => row.Value));
        var previousExpenseTotal = RoundCurrency(previousExpenses.Sum(static row => row.Value));
        var currentSupportTotal = currentSupport.TotalTickets;
        var previousSupportTotal = previousSupport.TotalTickets;
        var portfolioTotal = RoundCurrency(pendingInvoices.Sum(static row => row.NetTotalInvoice));
        var overduePortfolioTotal = RoundCurrency(overdueInvoices.Sum(static row => row.NetTotalInvoice));

        return new TodayDashboardDto
        {
            AsOfDateValue = today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            AsOfDateLabel = today.ToString("dd 'de' MMMM 'de' yyyy", ColombianCulture),
            CurrentPeriodLabel = BuildRangeLabel(currentStart, today),
            ComparisonPeriodLabel = BuildRangeLabel(previousStart, previousEnd),
            Cards = new[]
            {
                BuildCurrentCard(
                    "total-businesses",
                    "Productos Cloud",
                    "Total negocios",
                    "Cantidad por valor de venta unitario de todas las filas. El valor se actualiza una vez por mes.",
                    "usd",
                    cloudProductsTotalBusinessUsd,
                    "billing",
                    "current-month",
                    Array.Empty<TodayDashboardItemDto>()),
                BuildComparativeCard(
                    "billing",
                    "Facturación",
                    "Facturación a la fecha del mes",
                    "Valor neto emitido por vertical.",
                    "currency",
                    currentBillingTotal,
                    previousBillingTotal,
                    "billing",
                    "overview",
                    BuildInvoiceVerticalItems(currentInvoices, previousInvoices, static row => row.NetTotalInvoice)),
                BuildComparativeCard(
                    "invoice-count",
                    "Facturación",
                    "Facturas emitidas a la fecha del mes",
                    "Cantidad emitida en el mismo tramo de cada mes.",
                    "number",
                    currentInvoices.Count,
                    previousInvoices.Count,
                    "billing",
                    "overview"),
                BuildComparativeCard(
                    "expenses",
                    "Gastos",
                    "Gastos a la fecha del mes",
                    "Gasto consolidado por vertical.",
                    "currency",
                    currentExpenseTotal,
                    previousExpenseTotal,
                    "ytd",
                    "",
                    BuildExpenseVerticalItems(currentExpenses, previousExpenses)),
                BuildComparativeCard(
                    "support-cloud",
                    "Soporte Cloud",
                    "Tickets de soporte Cloud del mes",
                    "Tickets creados por propietario.",
                    "number",
                    currentSupportTotal,
                    previousSupportTotal,
                    "support-cloud",
                    "",
                    BuildSupportOwnerItems(currentSupport, previousSupport)),
                BuildCurrentCard(
                    "copiers-maintenance",
                    "Soporte Copiers",
                    "Mantenimientos de soporte Copiers",
                    "Mantenimientos registrados por propietario.",
                    "number",
                    currentMaintenance.Count,
                    "copiers",
                    "maintenance",
                    BuildMaintenanceOwnerItems(currentMaintenance)),
                BuildCurrentCard(
                    "portfolio",
                    "Cartera",
                    "Cartera a la fecha",
                    "Facturas pendientes por vertical.",
                    "currency",
                    portfolioTotal,
                    "portfolio",
                    "detail",
                    BuildInvoiceVerticalItems(pendingInvoices, Array.Empty<BillingInvoiceRowDto>(), static row => row.NetTotalInvoice, showsGrowth: false)),
                BuildCurrentCard(
                    "overdue-portfolio",
                    "Cartera",
                    "Cartera vencida a la fecha",
                    "Facturas vencidas por vertical.",
                    "currency",
                    overduePortfolioTotal,
                    "portfolio",
                    "detail",
                    BuildInvoiceVerticalItems(overdueInvoices, Array.Empty<BillingInvoiceRowDto>(), static row => row.NetTotalInvoice, showsGrowth: false))
            }
        };
    }

    private static TodayDashboardCardDto BuildComparativeCard(
        string key,
        string eyebrow,
        string title,
        string description,
        string valueFormat,
        decimal value,
        decimal previousValue,
        string destinationTab,
        string destinationSubtab,
        IReadOnlyList<TodayDashboardItemDto>? items = null) =>
        new()
        {
            Key = key,
            Eyebrow = eyebrow,
            Title = title,
            Description = description,
            ValueFormat = valueFormat,
            Value = value,
            PreviousValue = previousValue,
            ShowsGrowth = true,
            GrowthPercent = CalculateGrowth(value, previousValue),
            DestinationTab = destinationTab,
            DestinationSubtab = destinationSubtab,
            Items = items ?? Array.Empty<TodayDashboardItemDto>()
        };

    private static TodayDashboardCardDto BuildCurrentCard(
        string key,
        string eyebrow,
        string title,
        string description,
        string valueFormat,
        decimal value,
        string destinationTab,
        string destinationSubtab,
        IReadOnlyList<TodayDashboardItemDto> items) =>
        new()
        {
            Key = key,
            Eyebrow = eyebrow,
            Title = title,
            Description = description,
            ValueFormat = valueFormat,
            Value = value,
            DestinationTab = destinationTab,
            DestinationSubtab = destinationSubtab,
            Items = items
        };

    private static IReadOnlyList<TodayDashboardItemDto> BuildInvoiceVerticalItems(
        IReadOnlyList<BillingInvoiceRowDto> currentRows,
        IReadOnlyList<BillingInvoiceRowDto> previousRows,
        Func<BillingInvoiceRowDto, decimal> valueSelector,
        bool showsGrowth = true)
    {
        var current = AggregateInvoiceVerticals(currentRows, valueSelector);
        var previous = AggregateInvoiceVerticals(previousRows, valueSelector);
        return BuildItems(current, previous, showsGrowth);
    }

    private static IReadOnlyDictionary<string, SummaryBucket> AggregateInvoiceVerticals(
        IEnumerable<BillingInvoiceRowDto> rows,
        Func<BillingInvoiceRowDto, decimal> valueSelector) =>
        rows
            .GroupBy(row => ResolveInvoiceVertical(row).Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                static group => group.Key,
                group => new SummaryBucket(
                    ResolveInvoiceVertical(group.First()).Label,
                    RoundCurrency(group.Sum(valueSelector))),
                StringComparer.OrdinalIgnoreCase);

    private static IReadOnlyList<TodayDashboardItemDto> BuildExpenseVerticalItems(
        IReadOnlyList<YtdBreakdownRecordDto> currentRows,
        IReadOnlyList<YtdBreakdownRecordDto> previousRows)
    {
        var current = AggregateExpenseVerticals(currentRows);
        var previous = AggregateExpenseVerticals(previousRows);
        return BuildItems(current, previous, showsGrowth: true);
    }

    private static IReadOnlyDictionary<string, SummaryBucket> AggregateExpenseVerticals(IEnumerable<YtdBreakdownRecordDto> rows) =>
        rows
            .GroupBy(row => ResolveExpenseVertical(row).Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                static group => group.Key,
                group => new SummaryBucket(
                    ResolveExpenseVertical(group.First()).Label,
                    RoundCurrency(group.Sum(static row => row.Value))),
                StringComparer.OrdinalIgnoreCase);

    private static IReadOnlyList<TodayDashboardItemDto> BuildSupportOwnerItems(
        SoporteCloudBoardDto current,
        SoporteCloudBoardDto previous)
    {
        var currentOwners = AggregateSupportOwners(current.CreatorSummaries);
        var previousOwners = AggregateSupportOwners(previous.CreatorSummaries);
        return BuildItems(currentOwners, previousOwners, showsGrowth: true);
    }

    private static IReadOnlyDictionary<string, SummaryBucket> AggregateSupportOwners(
        IEnumerable<SoporteCloudCreatorSummaryDto>? rows) =>
        (rows ?? Array.Empty<SoporteCloudCreatorSummaryDto>())
            .GroupBy(row => BuildOwnerKey(row.CreatorId, row.CreatorName), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                static group => group.Key,
                group => new SummaryBucket(
                    FirstNonEmpty(group.Select(static row => row.CreatorName), "Sin propietario"),
                    group.Sum(static row => row.TotalTickets)),
                StringComparer.OrdinalIgnoreCase);

    private static IReadOnlyList<TodayDashboardItemDto> BuildMaintenanceOwnerItems(
        IEnumerable<CopiersMaintenanceRowDto> rows)
    {
        var current = rows
            .GroupBy(row => BuildOwnerKey(row.TechnicianId, row.TechnicianName), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                static group => group.Key,
                group => new SummaryBucket(
                    FirstNonEmpty(group.Select(static row => row.TechnicianName), "Sin propietario"),
                    group.Count()),
                StringComparer.OrdinalIgnoreCase);
        return BuildItems(current, new Dictionary<string, SummaryBucket>(StringComparer.OrdinalIgnoreCase), showsGrowth: false);
    }

    private static IReadOnlyList<TodayDashboardItemDto> BuildItems(
        IReadOnlyDictionary<string, SummaryBucket> current,
        IReadOnlyDictionary<string, SummaryBucket> previous,
        bool showsGrowth)
    {
        return current.Keys
            .Concat(previous.Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(key =>
            {
                current.TryGetValue(key, out var currentValue);
                previous.TryGetValue(key, out var previousValue);
                var value = currentValue?.Value ?? 0m;
                var comparison = previousValue?.Value ?? 0m;
                return new TodayDashboardItemDto
                {
                    Key = key,
                    Label = currentValue?.Label ?? previousValue?.Label ?? "Sin clasificar",
                    Value = value,
                    PreviousValue = comparison,
                    ShowsGrowth = showsGrowth,
                    GrowthPercent = showsGrowth ? CalculateGrowth(value, comparison) : null
                };
            })
            .OrderBy(static item => ResolveBucketOrder(item.Key))
            .ThenByDescending(static item => Math.Abs(item.Value))
            .ThenBy(static item => item.Label, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static IReadOnlyList<YtdBreakdownRecordDto> GetExpenseRecords(
        YtdDashboardDto? dashboard,
        DateOnly start,
        DateOnly end)
    {
        if (dashboard is null)
            return Array.Empty<YtdBreakdownRecordDto>();

        var monthKey = start.ToString("yyyy-MM", CultureInfo.InvariantCulture);
        return (dashboard.Chart?.Points ?? Array.Empty<YtdChartPointDto>())
            .Where(point => string.Equals(point.Key, monthKey, StringComparison.OrdinalIgnoreCase))
            .SelectMany(static point => point.ExpenseSegments ?? Array.Empty<YtdBreakdownSegmentDto>())
            .SelectMany(static segment => segment.Records ?? Array.Empty<YtdBreakdownRecordDto>())
            .Where(row => IsDateInRange(row.DateDisplay, start, end))
            .ToList();
    }

    private static (string Key, string Label) ResolveInvoiceVertical(BillingInvoiceRowDto row)
    {
        if (row.VerticalOptionValue == CloudVerticalOption || Contains(row.VerticalLabel, "cloud"))
            return ("cloud", "Cloud");
        if (row.VerticalOptionValue == CopiersVerticalOption || Contains(row.VerticalLabel, "copier"))
            return ("copiers", "Copiers");

        var label = string.IsNullOrWhiteSpace(row.VerticalLabel) ? "Sin vertical" : row.VerticalLabel.Trim();
        return (NormalizeKey(label, "sin-vertical"), label);
    }

    private static (string Key, string Label) ResolveExpenseVertical(YtdBreakdownRecordDto row)
    {
        if (string.Equals(row.VerticalKey, "cloud", StringComparison.OrdinalIgnoreCase) || Contains(row.VerticalLabel, "cloud"))
            return ("cloud", "Cloud");
        if (string.Equals(row.VerticalKey, "copiers", StringComparison.OrdinalIgnoreCase) || Contains(row.VerticalLabel, "copier"))
            return ("copiers", "Copiers");

        var label = string.IsNullOrWhiteSpace(row.VerticalLabel) ? "Sin vertical" : row.VerticalLabel.Trim();
        return (NormalizeKey(row.VerticalKey, NormalizeKey(label, "sin-vertical")), label);
    }

    private static string BuildOwnerKey(string? id, string? name) =>
        !string.IsNullOrWhiteSpace(id)
            ? $"id:{id.Trim().ToLowerInvariant()}"
            : $"name:{NormalizeKey(name, "sin-propietario")}";

    private static bool IsDateInRange(string? value, DateOnly start, DateOnly end) =>
        DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
        && date >= start
        && date <= end;

    private static decimal? CalculateGrowth(decimal current, decimal previous)
    {
        if (previous == 0m)
            return null;

        return Math.Round(((current - previous) / Math.Abs(previous)) * 100m, 1, MidpointRounding.AwayFromZero);
    }

    private static string BuildRangeLabel(DateOnly start, DateOnly end) =>
        start.Year == end.Year && start.Month == end.Month
            ? $"1-{end.Day} de {end.ToString("MMMM yyyy", ColombianCulture)}"
            : $"{start:dd/MM/yyyy} - {end:dd/MM/yyyy}";

    private static int ResolveBucketOrder(string key) => key.ToLowerInvariant() switch
    {
        "cloud" => 0,
        "copiers" => 1,
        "sin-vertical" => 3,
        _ => 2
    };

    private static string NormalizeKey(string? value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
            return fallback;

        var normalized = value.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        return new string(normalized
                .Where(static ch => CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
                .Select(static ch => char.IsLetterOrDigit(ch) ? ch : '-')
                .ToArray())
            .Trim('-');
    }

    private static bool Contains(string? value, string token) =>
        value?.Contains(token, StringComparison.OrdinalIgnoreCase) == true;

    private static string FirstNonEmpty(IEnumerable<string?> values, string fallback) =>
        values.FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? fallback;

    private static decimal RoundCurrency(decimal value) =>
        Math.Round(value, 2, MidpointRounding.AwayFromZero);

    private sealed record SummaryBucket(string Label, decimal Value);
}
