using CotizadorInterno.Web.Models;

namespace CotizadorInterno.Web.Models.Metricas;

public enum MetricsRangeFilter
{
    ThisMonth = 0,
    ThisYear = 1,
    PreviousYear = 2,
    All = 3
}

public enum MetricsViewMode
{
    Global = 0,
    Individual = 1
}

public enum MetricsPeriodGranularity
{
    Month = 0,
    Quarter = 1,
    Semester = 2,
    Year = 3
}

public static class MetricsRangeFilterExtensions
{
    public static MetricsRangeFilter ParseOrDefault(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return MetricsRangeFilter.ThisYear;

        return value.Trim().ToLowerInvariant() switch
        {
            "thismonth" or "this-month" or "este-mes" => MetricsRangeFilter.ThisMonth,
            "thisyear" or "this-year" or "este-ano" or "este-a\u00f1o" => MetricsRangeFilter.ThisYear,
            "previousyear" or "previous-year" or "ano-pasado" or "a\u00f1o-pasado" => MetricsRangeFilter.PreviousYear,
            "all" or "todo" or "todos" => MetricsRangeFilter.All,
            _ => MetricsRangeFilter.ThisYear
        };
    }

    public static string ToKey(this MetricsRangeFilter value) => value switch
    {
        MetricsRangeFilter.ThisMonth => "this-month",
        MetricsRangeFilter.ThisYear => "this-year",
        MetricsRangeFilter.PreviousYear => "previous-year",
        MetricsRangeFilter.All => "all",
        _ => "this-year"
    };

    public static string ToLabel(this MetricsRangeFilter value) => value switch
    {
        MetricsRangeFilter.ThisMonth => "Este mes",
        MetricsRangeFilter.ThisYear => "Este a\u00f1o",
        MetricsRangeFilter.PreviousYear => "A\u00f1o pasado",
        MetricsRangeFilter.All => "Todo",
        _ => "Este a\u00f1o"
    };
}

public static class MetricsPeriodGranularityExtensions
{
    public static MetricsPeriodGranularity ParseOrDefault(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return MetricsPeriodGranularity.Month;

        return value.Trim().ToLowerInvariant() switch
        {
            "quarter" or "trimestre" or "trimestral" => MetricsPeriodGranularity.Quarter,
            "semester" or "semestre" or "semestral" => MetricsPeriodGranularity.Semester,
            "year" or "ano" or "a\u00f1o" or "anual" => MetricsPeriodGranularity.Year,
            _ => MetricsPeriodGranularity.Month
        };
    }

    public static string ToKey(this MetricsPeriodGranularity value) => value switch
    {
        MetricsPeriodGranularity.Quarter => "quarter",
        MetricsPeriodGranularity.Semester => "semester",
        MetricsPeriodGranularity.Year => "year",
        _ => "month"
    };

    public static string ToLabel(this MetricsPeriodGranularity value) => value switch
    {
        MetricsPeriodGranularity.Quarter => "Trimestral",
        MetricsPeriodGranularity.Semester => "Semestral",
        MetricsPeriodGranularity.Year => "Anual",
        _ => "Mensual"
    };

    public static int MonthsPerPeriod(this MetricsPeriodGranularity value) => value switch
    {
        MetricsPeriodGranularity.Quarter => 3,
        MetricsPeriodGranularity.Semester => 6,
        MetricsPeriodGranularity.Year => 12,
        _ => 1
    };
}

public static class MetricsViewModeExtensions
{
    public static MetricsViewMode ParseOrDefault(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return MetricsViewMode.Global;

        return value.Trim().ToLowerInvariant() switch
        {
            "individual" or "individuales" => MetricsViewMode.Individual,
            _ => MetricsViewMode.Global
        };
    }

    public static string ToKey(this MetricsViewMode value) => value switch
    {
        MetricsViewMode.Individual => "individual",
        _ => "global"
    };

    public static string ToLabel(this MetricsViewMode value) => value switch
    {
        MetricsViewMode.Individual => "Individuales",
        _ => "Globales"
    };
}

public sealed class MetricasPageViewModel
{
    public CurrentUserInfo CurrentUser { get; set; } = new();
    public MetricsRangeFilter InitialFilter { get; set; } = MetricsRangeFilter.ThisYear;
    public MetricsViewMode InitialView { get; set; } = MetricsViewMode.Global;
    public MetricsPeriodGranularity InitialPeriod { get; set; } = MetricsPeriodGranularity.Month;
}

public sealed class MetricsDashboardDto
{
    public string Filter { get; set; } = MetricsRangeFilter.ThisYear.ToKey();
    public string FilterLabel { get; set; } = MetricsRangeFilter.ThisYear.ToLabel();
    public string View { get; set; } = MetricsViewMode.Global.ToKey();
    public string ViewLabel { get; set; } = MetricsViewMode.Global.ToLabel();
    public string Period { get; set; } = MetricsPeriodGranularity.Month.ToKey();
    public string PeriodLabel { get; set; } = MetricsPeriodGranularity.Month.ToLabel();
    public string GranularityLabel { get; set; } = "Mensual";
    public string AppliedSellerKey { get; set; } = "";
    public string AppliedSellerName { get; set; } = "Todos los vendedores";
    public bool RequiresSellerSelection { get; set; }
    public string EmptyStateTitle { get; set; } = "";
    public string EmptyStateMessage { get; set; } = "";
    public int RecordsCount { get; set; }
    public int SellersCount { get; set; }
    public int VerticalsCount { get; set; }
    public decimal TotalScore { get; set; }
    public decimal TotalAnnualValue { get; set; }
    public IReadOnlyList<MetricsSellerOptionDto> Sellers { get; set; } = Array.Empty<MetricsSellerOptionDto>();
    public IReadOnlyList<MetricsChartDto> Charts { get; set; } = Array.Empty<MetricsChartDto>();
}

public sealed class MetricsSellerOptionDto
{
    public string Key { get; set; } = "";
    public string Name { get; set; } = "";
}

public sealed class MetricsChartDto
{
    public string Key { get; set; } = "";
    public string Title { get; set; } = "";
    public string Subtitle { get; set; } = "";
    public string EmptyMessage { get; set; } = "No hay datos para este grafico.";
    public string GoalLabel { get; set; } = "";
    public decimal TotalScore { get; set; }
    public decimal TotalAnnualValue { get; set; }
    public IReadOnlyList<string> Categories { get; set; } = Array.Empty<string>();
    public IReadOnlyList<MetricsGoalStatusDto> GoalStatuses { get; set; } = Array.Empty<MetricsGoalStatusDto>();
    public IReadOnlyList<MetricsSeriesDto> Series { get; set; } = Array.Empty<MetricsSeriesDto>();
}

public sealed class MetricsGoalStatusDto
{
    public string CategoryKey { get; set; } = "";
    public string Category { get; set; } = "";
    public decimal ActualValue { get; set; }
    public decimal TargetValue { get; set; }
    public decimal PreviousYearValue { get; set; }
    public decimal? GrowthPercent { get; set; }
    public bool HasTarget { get; set; }
    public bool IsMet { get; set; }
    public string StatusTone { get; set; } = "";
    public string StatusLabel { get; set; } = "";
    public IReadOnlyList<MetricsBusinessDetailDto> Details { get; set; } = Array.Empty<MetricsBusinessDetailDto>();
}

public sealed class MetricsBusinessDetailDto
{
    public string RecordId { get; set; } = "";
    public string ClientName { get; set; } = "";
    public decimal Score { get; set; }
    public decimal ContractValue { get; set; }
    public string ContractStartDateValue { get; set; } = "";
    public string ContractStartDateDisplay { get; set; } = "";
    public string Detail { get; set; } = "";
}

public sealed class MetricsSeriesDto
{
    public string Key { get; set; } = "";
    public string Name { get; set; } = "";
    public string Color { get; set; } = "";
    public bool IsReference { get; set; }
    public string StrokeDasharray { get; set; } = "";
    public string LegendNote { get; set; } = "";
    public decimal TotalScore { get; set; }
    public decimal TotalAnnualValue { get; set; }
    public IReadOnlyList<decimal> Values { get; set; } = Array.Empty<decimal>();
    public IReadOnlyList<decimal> AnnualValues { get; set; } = Array.Empty<decimal>();
}
