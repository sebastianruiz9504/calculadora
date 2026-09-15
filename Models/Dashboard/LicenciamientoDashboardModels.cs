namespace CotizadorInterno.Web.Models.Dashboard;

public sealed class LicenciamientoDashboardDto
{
    public int Year { get; set; }
    public int Month { get; set; }
    public string YearLabel { get; set; } = "";
    public string MonthLabel { get; set; } = "";
    public string DateRangeLabel { get; set; } = "";
    public string FocusLabel { get; set; } = "";
    public bool HasData { get; set; }
    public int RecordsCount { get; set; }
    public decimal TotalSales { get; set; }
    public decimal TotalCost { get; set; }
    public decimal TotalUtility { get; set; }
    public decimal? TotalUtilityPercent { get; set; }
    public LicenciamientoDashboardSegmentDto Monthly { get; set; } = new();
    public LicenciamientoDashboardSegmentDto Prepaid { get; set; } = new();
    public LicenciamientoDashboardCostCardDto MonthlyCostCard { get; set; } = new();
    public LicenciamientoDashboardCostCardDto PrepaidCostCard { get; set; } = new();
    public IReadOnlyList<LicenciamientoDashboardMonthOptionDto> MonthOptions { get; set; } = Array.Empty<LicenciamientoDashboardMonthOptionDto>();
    public string EmptyStateMessage { get; set; } = "";
}

public sealed class LicenciamientoDashboardSegmentDto
{
    public string Key { get; set; } = "";
    public string Label { get; set; } = "";
    public decimal TotalSales { get; set; }
    public decimal SalesSharePercent { get; set; }
    public decimal TotalCost { get; set; }
    public decimal TotalUtility { get; set; }
    public decimal? UtilityPercent { get; set; }
    public int RecordsCount { get; set; }
    public IReadOnlyList<LicenciamientoDashboardMonthlyPointDto> Months { get; set; } = Array.Empty<LicenciamientoDashboardMonthlyPointDto>();
}

public sealed class LicenciamientoDashboardMonthlyPointDto
{
    public int Year { get; set; }
    public int Month { get; set; }
    public string Key { get; set; } = "";
    public string Label { get; set; } = "";
    public decimal Sales { get; set; }
    public decimal SalesSharePercent { get; set; }
    public decimal Cost { get; set; }
    public decimal Utility { get; set; }
    public decimal? UtilityPercent { get; set; }
    public int RecordsCount { get; set; }
}

public sealed class LicenciamientoDashboardCostCardDto
{
    public string Key { get; set; } = "";
    public string Label { get; set; } = "";
    public int Year { get; set; }
    public int Month { get; set; }
    public string MonthLabel { get; set; } = "";
    public decimal TotalCost { get; set; }
    public decimal TotalSales { get; set; }
    public decimal Utility { get; set; }
    public decimal? UtilityPercent { get; set; }
    public int RecordsCount { get; set; }
    public IReadOnlyList<LicenciamientoDashboardClientCostDto> Breakdown { get; set; } = Array.Empty<LicenciamientoDashboardClientCostDto>();
}

public sealed class LicenciamientoDashboardClientCostDto
{
    public string ClientKey { get; set; } = "";
    public string ClientName { get; set; } = "";
    public string BusinessGroupId { get; set; } = "";
    public string BusinessGroupName { get; set; } = "";
    public decimal Cost { get; set; }
    public decimal Sales { get; set; }
    public decimal Utility { get; set; }
    public decimal SharePercent { get; set; }
    public int RecordsCount { get; set; }
    public IReadOnlyList<LicenciamientoDashboardLineDto> Lines { get; set; } = Array.Empty<LicenciamientoDashboardLineDto>();
}

public sealed class LicenciamientoDashboardLineDto
{
    public string Source { get; set; } = "";
    public string RecordId { get; set; } = "";
    public string Description { get; set; } = "";
    public string ClientName { get; set; } = "";
    public string Reference { get; set; } = "";
    public string Date { get; set; } = "";
    public decimal Amount { get; set; }
    public int? ContractTypeValue { get; set; }
    public string ContractTypeLabel { get; set; } = "";
}

public sealed class LicenciamientoDashboardContractChangeRequest
{
    public int Year { get; set; }
    public int Month { get; set; }
    public string ClientKey { get; set; } = "";
    public string SourceContractKey { get; set; } = "";
    public string TargetContractKey { get; set; } = "";
    public List<LicenciamientoDashboardLineSelection> Lines { get; set; } = new();
}

public sealed class LicenciamientoDashboardLineSelection
{
    public string Source { get; set; } = "";
    public string RecordId { get; set; } = "";
    public int? ExpectedContractTypeValue { get; set; }
}

public sealed class LicenciamientoDashboardContractChangeResult
{
    public int UpdatedCount { get; set; }
    public int AlreadyUpdatedCount { get; set; }
    public string Message { get; set; } = "";
}

public sealed class LicenciamientoDashboardMonthOptionDto
{
    public int Value { get; set; }
    public string Label { get; set; } = "";
    public bool HasData { get; set; }
}
