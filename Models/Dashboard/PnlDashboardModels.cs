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
    public string OrphanDescription { get; set; } = "";
    public IReadOnlyList<PnlMonthColumnDto> Months { get; set; } = Array.Empty<PnlMonthColumnDto>();
    public IReadOnlyList<PnlKpiDto> Kpis { get; set; } = Array.Empty<PnlKpiDto>();
    public IReadOnlyList<PnlRowDto> Rows { get; set; } = Array.Empty<PnlRowDto>();
    public IReadOnlyList<PnlOrphanRowDto> OrphanRows { get; set; } = Array.Empty<PnlOrphanRowDto>();
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

public sealed class PnlOrphanRowDto
{
    public string Key { get; set; } = "";
    public string Label { get; set; } = "";
    public string Hint { get; set; } = "";
    public IReadOnlyList<int> Values { get; set; } = Array.Empty<int>();
    public int Total { get; set; }
}

public sealed class PnlCellDetailDto
{
    public int Year { get; set; }
    public int MonthCutoff { get; set; }
    public int? CellMonth { get; set; }
    public string RowKey { get; set; } = "";
    public string RowLabel { get; set; } = "";
    public string CellLabel { get; set; } = "";
    public string VerticalKey { get; set; } = "all";
    public string VerticalLabel { get; set; } = "";
    public string ValueFormat { get; set; } = "currency";
    public decimal Total { get; set; }
    public int RecordsCount { get; set; }
    public string EmptyMessage { get; set; } = "";
    public IReadOnlyList<PnlOptionDto> VerticalOptions { get; set; } = Array.Empty<PnlOptionDto>();
    public IReadOnlyList<PnlOptionDto> CategoryOptions { get; set; } = Array.Empty<PnlOptionDto>();
    public IReadOnlyList<PnlCellDetailRecordDto> Records { get; set; } = Array.Empty<PnlCellDetailRecordDto>();
}

public sealed class PnlCellDetailRecordDto
{
    public string SourceType { get; set; } = "";
    public string SourceLabel { get; set; } = "";
    public string RecordId { get; set; } = "";
    public string DocumentNumber { get; set; } = "";
    public string Description { get; set; } = "";
    public string DateDisplay { get; set; } = "";
    public string AssignedMonthDisplay { get; set; } = "";
    public string VerticalKey { get; set; } = "";
    public string VerticalLabel { get; set; } = "";
    public int? CategoryOptionValue { get; set; }
    public string CategoryLabel { get; set; } = "";
    public decimal TotalInvoice { get; set; }
    public decimal VatValue { get; set; }
    public decimal TotalBeforeVatValue { get; set; }
    public decimal PaymentValue { get; set; }
    public decimal CloudValue { get; set; }
    public decimal CopiersValue { get; set; }
    public decimal CellValue { get; set; }
    public bool CanEditVertical { get; set; }
    public bool CanEditCategory { get; set; }
    public bool CanEditAllocation { get; set; }
}

public sealed class PnlOptionDto
{
    public string Key { get; set; } = "";
    public string Label { get; set; } = "";
    public int? Value { get; set; }
}

public sealed class PnlDetailRecordUpdateRequestDto
{
    public string SourceType { get; set; } = "";
    public string RecordId { get; set; } = "";
    public string? VerticalKey { get; set; }
    public int? CategoryOptionValue { get; set; }
    public decimal? CloudValue { get; set; }
    public decimal? CopiersValue { get; set; }
}

public sealed class PnlDetailRecordUpdateResultDto
{
    public string RecordId { get; set; } = "";
    public string Message { get; set; } = "";
}
