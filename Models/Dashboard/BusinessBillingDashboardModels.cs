namespace CotizadorInterno.Web.Models.Dashboard;

public sealed class BusinessBillingDashboardDto
{
    public string StartDateValue { get; set; } = "";
    public string EndDateValue { get; set; } = "";
    public string StartMonthValue { get; set; } = "";
    public string EndMonthValue { get; set; } = "";
    public string Granularity { get; set; } = "month";
    public string GranularityLabel { get; set; } = "";
    public string PeriodLabel { get; set; } = "";
    public string DateRangeLabel { get; set; } = "";
    public string FocusLabel { get; set; } = "";
    public bool HasData { get; set; }
    public int RecordsCount { get; set; }
    public decimal TotalSales { get; set; }
    public string EmptyStateMessage { get; set; } = "";
    public BusinessBillingSectionDto Cloud { get; set; } = new();
    public BusinessBillingSectionDto Copiers { get; set; } = new();
}

public sealed class BusinessBillingSectionDto
{
    public string Key { get; set; } = "";
    public string Label { get; set; } = "";
    public decimal TotalSales { get; set; }
    public int RecordsCount { get; set; }
    public BusinessBillingChartDto Monthly { get; set; } = new();
    public BusinessBillingChartDto Prepaid { get; set; } = new();
}

public sealed class BusinessBillingChartDto
{
    public string Key { get; set; } = "";
    public string Label { get; set; } = "";
    public string VerticalKey { get; set; } = "";
    public string ContractTypeKey { get; set; } = "";
    public decimal TotalSales { get; set; }
    public int RecordsCount { get; set; }
    public IReadOnlyList<BusinessBillingPointDto> Points { get; set; } = Array.Empty<BusinessBillingPointDto>();
}

public sealed class BusinessBillingPointDto
{
    public string Key { get; set; } = "";
    public string Label { get; set; } = "";
    public string ShortLabel { get; set; } = "";
    public string StartDateValue { get; set; } = "";
    public string EndDateValue { get; set; } = "";
    public decimal Sales { get; set; }
    public int RecordsCount { get; set; }
    public decimal PreviousPeriodSales { get; set; }
    public decimal SamePeriodPreviousYearSales { get; set; }
    public decimal? PreviousPeriodGrowthPercent { get; set; }
    public decimal? SamePeriodPreviousYearGrowthPercent { get; set; }
}
