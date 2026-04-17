namespace CotizadorInterno.Web.Models.Dashboard;

public sealed class PnlDashboardDto
{
    public int Year { get; set; }
    public string VerticalKey { get; set; } = "all";
    public string VerticalLabel { get; set; } = "";
    public int LatestMonthAvailable { get; set; }
    public string LatestMonthAvailableLabel { get; set; } = "";
    public int MonthCutoff { get; set; }
    public string MonthCutoffLabel { get; set; } = "";
    public string DateRangeLabel { get; set; } = "";
    public string FocusLabel { get; set; } = "";
    public string Description { get; set; } = "";
    public bool HasData { get; set; }
    public int RecordsCount { get; set; }
    public string EmptyStateTitle { get; set; } = "";
    public string EmptyStateMessage { get; set; } = "";
    public IReadOnlyList<PnlMonthColumnDto> Months { get; set; } = Array.Empty<PnlMonthColumnDto>();
    public IReadOnlyList<PnlKpiDto> Kpis { get; set; } = Array.Empty<PnlKpiDto>();
    public IReadOnlyList<PnlRowDto> Rows { get; set; } = Array.Empty<PnlRowDto>();
}

public sealed class PnlMonthColumnDto
{
    public int Month { get; set; }
    public string Key { get; set; } = "";
    public string Label { get; set; } = "";
}

public sealed class PnlKpiDto
{
    public string Key { get; set; } = "";
    public string Label { get; set; } = "";
    public string Hint { get; set; } = "";
    public decimal Value { get; set; }
    public string ValueFormat { get; set; } = "currency";
    public string Tone { get; set; } = "neutral";
}

public sealed class PnlRowDto
{
    public string Key { get; set; } = "";
    public string Label { get; set; } = "";
    public string RowType { get; set; } = "detail";
    public int Level { get; set; }
    public string ValueFormat { get; set; } = "currency";
    public IReadOnlyList<decimal> Values { get; set; } = Array.Empty<decimal>();
    public decimal Total { get; set; }
}
