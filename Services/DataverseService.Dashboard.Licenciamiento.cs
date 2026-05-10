using System.Globalization;
using CotizadorInterno.Web.Models.Dashboard;
using CotizadorInterno.Web.Models.Licenciamiento;

namespace CotizadorInterno.Web.Services;

public sealed partial class DataverseService
{
    public async Task<LicenciamientoDashboardDto> GetLicenciamientoDashboardAsync(
        int year,
        int? month = null,
        CancellationToken ct = default)
    {
        var today = GetBogotaToday();
        var resolvedYear = year is >= 2000 and <= 2100 ? year : Math.Max(today.Year - 1, 2000);
        var selectedMonth = Math.Clamp(month ?? ResolveLicenciamientoDashboardDefaultMonth(resolvedYear, today), 1, 12);
        var yearStart = new DateOnly(resolvedYear, 1, 1);
        var yearEndExclusive = yearStart.AddYears(1);

        var cruce = await GetLicenciamientoCruceDashboardAsync(resolvedYear, 12, "ytd", ct);
        var rows = (cruce.Rows ?? Array.Empty<LicenciamientoCruceRowDto>())
            .Where(row => IsLicenciamientoDashboardRowInYear(row, resolvedYear))
            .ToList();
        var totalSales = RoundCurrency(rows.Sum(static row => row.FacturacionSinIva));
        var totalCost = RoundCurrency(rows.Sum(static row => row.CostoLicenciamiento));
        var totalUtility = RoundCurrency(rows.Sum(static row => row.MargenBruto));

        return new LicenciamientoDashboardDto
        {
            Year = resolvedYear,
            Month = selectedMonth,
            YearLabel = resolvedYear.ToString(CultureInfo.InvariantCulture),
            MonthLabel = ResolveLicenciamientoDashboardMonthLabel(resolvedYear, selectedMonth),
            DateRangeLabel = BuildDateRangeLabel(yearStart, yearEndExclusive),
            FocusLabel = "Licenciamiento por utilidad y tipo de contrato",
            HasData = rows.Count > 0,
            RecordsCount = rows.Count,
            TotalSales = totalSales,
            TotalCost = totalCost,
            TotalUtility = totalUtility,
            TotalUtilityPercent = CalculateLicenciamientoCruceMarginPercent(totalUtility, totalSales),
            Monthly = BuildLicenciamientoDashboardSegment(
                rows,
                resolvedYear,
                LicenciamientoCruceMonthlyKey,
                LicenciamientoCruceMonthlyLabel,
                totalSales),
            Prepaid = BuildLicenciamientoDashboardSegment(
                rows,
                resolvedYear,
                LicenciamientoCruceOneTimeKey,
                LicenciamientoCruceOneTimeLabel,
                totalSales),
            MonthlyCostCard = BuildLicenciamientoDashboardCostCard(
                rows,
                resolvedYear,
                selectedMonth,
                LicenciamientoCruceMonthlyKey,
                LicenciamientoCruceMonthlyLabel),
            PrepaidCostCard = BuildLicenciamientoDashboardCostCard(
                rows,
                resolvedYear,
                selectedMonth,
                LicenciamientoCruceOneTimeKey,
                LicenciamientoCruceOneTimeLabel),
            MonthOptions = BuildLicenciamientoDashboardMonthOptions(rows, resolvedYear),
            EmptyStateMessage = rows.Count == 0
                ? $"No hay datos de licenciamiento cruzados para {resolvedYear}."
                : ""
        };
    }

    private static int ResolveLicenciamientoDashboardDefaultMonth(int year, DateOnly today) =>
        year >= today.Year ? today.Month : 12;

    private static bool IsLicenciamientoDashboardRowInYear(LicenciamientoCruceRowDto row, int year)
    {
        if (DateOnly.TryParseExact(
                $"{row.MesCierre}-01",
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var month))
        {
            return month.Year == year;
        }

        return false;
    }

    private static string BuildLicenciamientoDashboardMonthKey(int year, int month) =>
        $"{year:D4}-{Math.Clamp(month, 1, 12):D2}";

    private static string ResolveLicenciamientoDashboardMonthLabel(int year, int month) =>
        ToTitleCase(new DateOnly(year, Math.Clamp(month, 1, 12), 1).ToString("MMMM yyyy", DashboardCulture));

    private static IReadOnlyList<LicenciamientoDashboardMonthOptionDto> BuildLicenciamientoDashboardMonthOptions(
        IReadOnlyList<LicenciamientoCruceRowDto> rows,
        int year)
    {
        var monthsWithData = rows
            .Select(static row => row.MesCierre)
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return Enumerable.Range(1, 12)
            .Select(month => new LicenciamientoDashboardMonthOptionDto
            {
                Value = month,
                Label = ResolveLicenciamientoDashboardMonthLabel(year, month),
                HasData = monthsWithData.Contains(BuildLicenciamientoDashboardMonthKey(year, month))
            })
            .ToList();
    }

    private static LicenciamientoDashboardSegmentDto BuildLicenciamientoDashboardSegment(
        IReadOnlyList<LicenciamientoCruceRowDto> rows,
        int year,
        string contractKey,
        string contractLabel,
        decimal totalSales)
    {
        var segmentRows = rows
            .Where(row => string.Equals(row.TipoContratoKey, contractKey, StringComparison.OrdinalIgnoreCase))
            .ToList();
        var points = Enumerable.Range(1, 12)
            .Select(month =>
            {
                var monthKey = BuildLicenciamientoDashboardMonthKey(year, month);
                var monthRows = segmentRows
                    .Where(row => string.Equals(row.MesCierre, monthKey, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                var sales = RoundCurrency(monthRows.Sum(static row => row.FacturacionSinIva));
                var cost = RoundCurrency(monthRows.Sum(static row => row.CostoLicenciamiento));
                var utility = RoundCurrency(monthRows.Sum(static row => row.MargenBruto));

                return new LicenciamientoDashboardMonthlyPointDto
                {
                    Year = year,
                    Month = month,
                    Key = monthKey,
                    Label = ToTitleCase(new DateOnly(year, month, 1).ToString("MMM", DashboardCulture)),
                    Sales = sales,
                    SalesSharePercent = CalculateLicenciamientoDashboardSharePercent(sales, totalSales),
                    Cost = cost,
                    Utility = utility,
                    UtilityPercent = CalculateLicenciamientoCruceMarginPercent(utility, sales),
                    RecordsCount = monthRows.Count
                };
            })
            .ToList();

        var segmentSales = RoundCurrency(segmentRows.Sum(static row => row.FacturacionSinIva));
        var segmentCost = RoundCurrency(segmentRows.Sum(static row => row.CostoLicenciamiento));
        var segmentUtility = RoundCurrency(segmentRows.Sum(static row => row.MargenBruto));

        return new LicenciamientoDashboardSegmentDto
        {
            Key = contractKey,
            Label = contractLabel,
            TotalSales = segmentSales,
            SalesSharePercent = CalculateLicenciamientoDashboardSharePercent(segmentSales, totalSales),
            TotalCost = segmentCost,
            TotalUtility = segmentUtility,
            UtilityPercent = CalculateLicenciamientoCruceMarginPercent(segmentUtility, segmentSales),
            RecordsCount = segmentRows.Count,
            Months = points
        };
    }

    private static LicenciamientoDashboardCostCardDto BuildLicenciamientoDashboardCostCard(
        IReadOnlyList<LicenciamientoCruceRowDto> rows,
        int year,
        int month,
        string contractKey,
        string contractLabel)
    {
        var monthKey = BuildLicenciamientoDashboardMonthKey(year, month);
        var cardRows = rows
            .Where(row => string.Equals(row.TipoContratoKey, contractKey, StringComparison.OrdinalIgnoreCase)
                && string.Equals(row.MesCierre, monthKey, StringComparison.OrdinalIgnoreCase))
            .ToList();
        var totalCost = RoundCurrency(cardRows.Sum(static row => row.CostoLicenciamiento));
        var totalSales = RoundCurrency(cardRows.Sum(static row => row.FacturacionSinIva));
        var utility = RoundCurrency(cardRows.Sum(static row => row.MargenBruto));

        var breakdown = cardRows
            .GroupBy(ResolveLicenciamientoDashboardClientKey, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var items = group.ToList();
                var first = items.FirstOrDefault();
                var cost = RoundCurrency(items.Sum(static row => row.CostoLicenciamiento));
                var sales = RoundCurrency(items.Sum(static row => row.FacturacionSinIva));

                return new LicenciamientoDashboardClientCostDto
                {
                    ClientKey = group.Key,
                    ClientName = ResolveLicenciamientoDashboardClientLabel(first),
                    BusinessGroupId = first?.GrupoEmpresarialId ?? "",
                    BusinessGroupName = first?.GrupoEmpresarial ?? "",
                    Cost = cost,
                    Sales = sales,
                    Utility = RoundCurrency(items.Sum(static row => row.MargenBruto)),
                    SharePercent = CalculateLicenciamientoDashboardSharePercent(cost, totalCost),
                    RecordsCount = items.Count
                };
            })
            .OrderByDescending(item => item.Cost)
            .ThenBy(item => item.ClientName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new LicenciamientoDashboardCostCardDto
        {
            Key = contractKey,
            Label = contractLabel,
            Year = year,
            Month = month,
            MonthLabel = ResolveLicenciamientoDashboardMonthLabel(year, month),
            TotalCost = totalCost,
            TotalSales = totalSales,
            Utility = utility,
            UtilityPercent = CalculateLicenciamientoCruceMarginPercent(utility, totalSales),
            RecordsCount = cardRows.Count,
            Breakdown = breakdown
        };
    }

    private static string ResolveLicenciamientoDashboardClientKey(LicenciamientoCruceRowDto row)
    {
        if (!string.IsNullOrWhiteSpace(row.GrupoEmpresarialId))
            return $"group:{row.GrupoEmpresarialId.Trim()}";

        if (!string.IsNullOrWhiteSpace(row.GrupoEmpresarial))
            return $"group-name:{row.GrupoEmpresarial.Trim()}";

        return FirstNonEmpty(
            row.MatrixClientKey,
            row.Trace?.CostGroupKey,
            row.Trace?.BillingGroupKey,
            row.Cliente,
            row.NitCliente,
            row.RowKey,
            "sin-cliente");
    }

    private static string ResolveLicenciamientoDashboardClientLabel(LicenciamientoCruceRowDto? row)
    {
        if (row is null)
            return "Sin cliente";

        return FirstNonEmpty(
            row.GrupoEmpresarial,
            row.Cliente,
            row.NitCliente,
            "Sin cliente");
    }

    private static decimal CalculateLicenciamientoDashboardSharePercent(decimal value, decimal total)
    {
        if (Math.Abs(total) < 0.01m)
            return 0m;

        return RoundCurrency((value / total) * 100m);
    }
}
