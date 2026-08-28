namespace CotizadorInterno.Web.Models.Dashboard;

public sealed class YtdDashboardDto
{
    public int Year { get; set; }
    public int MonthCutoff { get; set; }
    public string PeriodLabel { get; set; } = "";
    public string DateRangeLabel { get; set; } = "";
    public string FocusLabel { get; set; } = "";
    public bool HasData { get; set; }
    public int RecordsCount { get; set; }
    public string EmptyStateMessage { get; set; } = "";
    public string SourceWarning { get; set; } = "";
    public YtdChartDto Chart { get; set; } = new();
    public IReadOnlyList<YtdChartDto> Charts { get; set; } = Array.Empty<YtdChartDto>();
    public YtdFilterSetDto RevenueFilters { get; set; } = new();
    public YtdFilterSetDto ExpenseFilters { get; set; } = new();
    public YtdEditorOptionsDto EditorOptions { get; set; } = new();
    public YtdLicensingReconciliationDto LicensingReconciliation { get; set; } = new();
}

public sealed class YtdChartDto
{
    public string Key { get; set; } = "";
    public string Title { get; set; } = "";
    public string Subtitle { get; set; } = "";
    public bool HasData { get; set; }
    public decimal TotalSales { get; set; }
    public decimal TotalExpenses { get; set; }
    public decimal TotalUtility { get; set; }
    public IReadOnlyList<YtdChartPointDto> Points { get; set; } = Array.Empty<YtdChartPointDto>();
}

public sealed class YtdChartPointDto
{
    public string Key { get; set; } = "";
    public string Label { get; set; } = "";
    public int Month { get; set; }
    public decimal Sales { get; set; }
    public decimal Expenses { get; set; }
    public decimal Utility { get; set; }
    public IReadOnlyList<YtdBreakdownSegmentDto> RevenueSegments { get; set; } = Array.Empty<YtdBreakdownSegmentDto>();
    public IReadOnlyList<YtdBreakdownSegmentDto> ExpenseSegments { get; set; } = Array.Empty<YtdBreakdownSegmentDto>();
}

public sealed class YtdBreakdownSegmentDto
{
    public string Key { get; set; } = "";
    public string Label { get; set; } = "";
    public string Kind { get; set; } = "";
    public string ClientKey { get; set; } = "";
    public string ClientLabel { get; set; } = "";
    public string CategoryKey { get; set; } = "";
    public string CategoryLabel { get; set; } = "";
    public string VerticalKey { get; set; } = "";
    public string VerticalLabel { get; set; } = "";
    public string ContractTypeKey { get; set; } = "";
    public string ContractTypeLabel { get; set; } = "";
    public decimal Value { get; set; }
    public int RecordsCount { get; set; }
    public IReadOnlyList<YtdBreakdownRecordDto> Records { get; set; } = Array.Empty<YtdBreakdownRecordDto>();
}

public sealed class YtdBreakdownRecordDto
{
    public string SourceType { get; set; } = "";
    public string SourceLabel { get; set; } = "";
    public string RecordId { get; set; } = "";
    public string DocumentNumber { get; set; } = "";
    public string DateDisplay { get; set; } = "";
    public string Counterparty { get; set; } = "";
    public string RecipientLabel { get; set; } = "";
    public string ClientKey { get; set; } = "";
    public string ClientLabel { get; set; } = "";
    public int? CategoryOptionValue { get; set; }
    public string CategoryKey { get; set; } = "";
    public string CategoryLabel { get; set; } = "";
    public string VerticalKey { get; set; } = "";
    public string VerticalLabel { get; set; } = "";
    public int? VerticalOptionValue { get; set; }
    public string ContractTypeKey { get; set; } = "";
    public string ContractTypeLabel { get; set; } = "";
    public int? ContractTypeOptionValue { get; set; }
    public string Description { get; set; } = "";
    public decimal TotalInvoice { get; set; }
    public decimal VatValue { get; set; }
    public decimal TotalBeforeVatValue { get; set; }
    public decimal PaymentValue { get; set; }
    public decimal CloudValue { get; set; }
    public decimal CopiersValue { get; set; }
    public decimal Value { get; set; }
    public bool CanEditCategory { get; set; }
    public bool CanEditVertical { get; set; }
    public bool CanEditAllocation { get; set; }
    public bool CanEditContractType { get; set; }
    public IReadOnlyList<string> LicensingCostRecordIds { get; set; } = Array.Empty<string>();
}

public sealed class YtdFilterSetDto
{
    public IReadOnlyList<YtdFilterOptionDto> Clients { get; set; } = Array.Empty<YtdFilterOptionDto>();
    public IReadOnlyList<YtdFilterOptionDto> Categories { get; set; } = Array.Empty<YtdFilterOptionDto>();
    public IReadOnlyList<YtdFilterOptionDto> Verticals { get; set; } = Array.Empty<YtdFilterOptionDto>();
    public IReadOnlyList<YtdFilterOptionDto> ContractTypes { get; set; } = Array.Empty<YtdFilterOptionDto>();
    public IReadOnlyList<YtdBreakdownModeDto> BreakdownModes { get; set; } = Array.Empty<YtdBreakdownModeDto>();
}

public sealed class YtdFilterOptionDto
{
    public string Key { get; set; } = "";
    public string Label { get; set; } = "";
    public decimal Total { get; set; }
    public int RecordsCount { get; set; }
}

public sealed class YtdBreakdownModeDto
{
    public string Key { get; set; } = "";
    public string Label { get; set; } = "";
}

public sealed class YtdEditorOptionsDto
{
    public IReadOnlyList<YtdEditorOptionDto> BillingVerticals { get; set; } = Array.Empty<YtdEditorOptionDto>();
    public IReadOnlyList<YtdEditorOptionDto> BillingContractTypes { get; set; } = Array.Empty<YtdEditorOptionDto>();
    public IReadOnlyList<YtdEditorOptionDto> ExpenseVerticals { get; set; } = Array.Empty<YtdEditorOptionDto>();
    public IReadOnlyList<YtdEditorOptionDto> ExpenseCategories { get; set; } = Array.Empty<YtdEditorOptionDto>();
    public IReadOnlyList<YtdEditorOptionDto> ExpenseContractTypes { get; set; } = Array.Empty<YtdEditorOptionDto>();
}

public sealed class YtdEditorOptionDto
{
    public string Key { get; set; } = "";
    public string Label { get; set; } = "";
    public int? Value { get; set; }
}

public sealed class YtdBillingRecordUpdateRequestDto
{
    public string RecordId { get; set; } = "";
    public int? VerticalOptionValue { get; set; }
    public int? ContractTypeOptionValue { get; set; }
}

public sealed class YtdRecordsUpdateRequestDto
{
    public IReadOnlyList<YtdRecordUpdateRequestDto> Records { get; set; } = Array.Empty<YtdRecordUpdateRequestDto>();
}

public sealed class YtdRecordUpdateRequestDto
{
    public string SourceType { get; set; } = "";
    public string RecordId { get; set; } = "";
    public int? VerticalOptionValue { get; set; }
    public int? ContractTypeOptionValue { get; set; }
    public int? CategoryOptionValue { get; set; }
    public decimal? CloudValue { get; set; }
    public decimal? CopiersValue { get; set; }
    public IReadOnlyList<string> LicensingCostRecordIds { get; set; } = Array.Empty<string>();
}

public sealed class YtdRecordUpdateResultDto
{
    public string RecordId { get; set; } = "";
    public string Message { get; set; } = "";
}

public sealed class YtdRecordsUpdateResultDto
{
    public int UpdatedCount { get; set; }
    public int BillingUpdatedCount { get; set; }
    public int ExpenseUpdatedCount { get; set; }
    public int LicensingUpdatedCount { get; set; }
    public string Message { get; set; } = "";
}

public sealed class YtdLicensingReconciliationDto
{
    public decimal InvoiceTotal { get; set; }
    public decimal LicensingTotal { get; set; }
    public decimal Difference { get; set; }
    public decimal DifferencePercent { get; set; }
    public string Disclaimer { get; set; } = "";
    public IReadOnlyList<YtdLicensingReconciliationMonthDto> Months { get; set; } = Array.Empty<YtdLicensingReconciliationMonthDto>();
}

public sealed class YtdLicensingReconciliationMonthDto
{
    public string Key { get; set; } = "";
    public string Label { get; set; } = "";
    public int Month { get; set; }
    public decimal InvoiceValue { get; set; }
    public decimal LicensingValue { get; set; }
    public decimal Difference { get; set; }
    public decimal DifferencePercent { get; set; }
}
