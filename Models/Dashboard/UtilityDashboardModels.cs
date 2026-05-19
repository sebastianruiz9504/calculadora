namespace CotizadorInterno.Web.Models.Dashboard;

public sealed class UtilityDashboardDto
{
    public int StartYear { get; set; }
    public int EndYear { get; set; }
    public int EndMonth { get; set; }
    public string PeriodLabel { get; set; } = "";
    public string DateRangeLabel { get; set; } = "";
    public string FocusLabel { get; set; } = "";
    public bool HasData { get; set; }
    public int RecordsCount { get; set; }
    public decimal StandardTrm { get; set; }
    public UtilityTheoreticalCardDto TheoreticalMonthly { get; set; } = new();
    public UtilityTheoreticalCardDto TheoreticalPrepaid { get; set; } = new();
    public UtilityRealSegmentDto RealMonthly { get; set; } = new();
    public UtilityRealSegmentDto RealPrepaid { get; set; } = new();
    public IReadOnlyList<UtilityUnresolvedRowDto> UnresolvedRows { get; set; } = Array.Empty<UtilityUnresolvedRowDto>();
    public string EmptyStateMessage { get; set; } = "";
}

public sealed class UtilityTheoreticalCardDto
{
    public string Key { get; set; } = "";
    public string Label { get; set; } = "";
    public decimal Sales { get; set; }
    public decimal Cost { get; set; }
    public decimal Utility { get; set; }
    public decimal? UtilityPercent { get; set; }
    public int RecordsCount { get; set; }
    public int MissingCostCount { get; set; }
    public IReadOnlyList<UtilityTheoreticalBreakdownRowDto> Breakdown { get; set; } = Array.Empty<UtilityTheoreticalBreakdownRowDto>();
}

public sealed class UtilityTheoreticalBreakdownRowDto
{
    public string RecordId { get; set; } = "";
    public string ClientName { get; set; } = "";
    public string ProductName { get; set; } = "";
    public string ProductLineLabel { get; set; } = "";
    public string ContractTypeLabel { get; set; } = "";
    public int Quantity { get; set; }
    public int BillingDay { get; set; }
    public decimal UnitSaleUsd { get; set; }
    public decimal UnitCostUsd { get; set; }
    public decimal Sales { get; set; }
    public decimal Cost { get; set; }
    public decimal Utility { get; set; }
    public bool HasCost { get; set; }
}

public sealed class UtilityRealSegmentDto
{
    public string Key { get; set; } = "";
    public string Label { get; set; } = "";
    public decimal Sales { get; set; }
    public decimal Cost { get; set; }
    public decimal Utility { get; set; }
    public decimal? UtilityPercent { get; set; }
    public int BillingRecordsCount { get; set; }
    public int CostRecordsCount { get; set; }
    public IReadOnlyList<UtilityMonthlyPointDto> Months { get; set; } = Array.Empty<UtilityMonthlyPointDto>();
}

public sealed class UtilityMonthlyPointDto
{
    public string Key { get; set; } = "";
    public string Label { get; set; } = "";
    public int Year { get; set; }
    public int Month { get; set; }
    public decimal Sales { get; set; }
    public decimal Cost { get; set; }
    public decimal Utility { get; set; }
    public decimal? UtilityPercent { get; set; }
    public int BillingRecordsCount { get; set; }
    public int CostRecordsCount { get; set; }
}

public sealed class UtilityUnresolvedRowDto
{
    public string SourceType { get; set; } = "";
    public string SourceLabel { get; set; } = "";
    public string RecordId { get; set; } = "";
    public string Reference { get; set; } = "";
    public string ClientName { get; set; } = "";
    public string ProductName { get; set; } = "";
    public string DateDisplay { get; set; } = "";
    public string CurrentVertical { get; set; } = "";
    public string CurrentContractType { get; set; } = "";
    public string Reason { get; set; } = "";
    public string SuggestedBucket { get; set; } = "";
    public decimal Amount { get; set; }
    public bool CanAssign { get; set; }
}

public sealed class UtilityAssignmentRequestDto
{
    public string SourceType { get; set; } = "";
    public string RecordId { get; set; } = "";
    public string TargetBucket { get; set; } = "";
}

public sealed class UtilityAssignmentResultDto
{
    public string Message { get; set; } = "";
    public string SourceType { get; set; } = "";
    public string RecordId { get; set; } = "";
    public string TargetBucket { get; set; } = "";
}
