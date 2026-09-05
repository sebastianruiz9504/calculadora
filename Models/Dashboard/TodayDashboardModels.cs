namespace CotizadorInterno.Web.Models.Dashboard;

public sealed class TodayDashboardDto
{
    public string AsOfDateValue { get; set; } = "";
    public string AsOfDateLabel { get; set; } = "";
    public string CurrentPeriodLabel { get; set; } = "";
    public string ComparisonPeriodLabel { get; set; } = "";
    public IReadOnlyList<TodayDashboardCardDto> Cards { get; set; } = Array.Empty<TodayDashboardCardDto>();
}

public sealed class TodayFinancialDashboardDto
{
    public IReadOnlyList<BillingInvoiceRowDto> RecentInvoices { get; set; } = Array.Empty<BillingInvoiceRowDto>();
    public IReadOnlyList<BillingInvoiceRowDto> PendingInvoices { get; set; } = Array.Empty<BillingInvoiceRowDto>();
}

public sealed class TodayDashboardCardDto
{
    public string Key { get; set; } = "";
    public string Eyebrow { get; set; } = "";
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string ValueFormat { get; set; } = "number";
    public decimal Value { get; set; }
    public decimal PreviousValue { get; set; }
    public bool ShowsGrowth { get; set; }
    public decimal? GrowthPercent { get; set; }
    public string DestinationTab { get; set; } = "";
    public string DestinationSubtab { get; set; } = "";
    public IReadOnlyList<TodayDashboardItemDto> Items { get; set; } = Array.Empty<TodayDashboardItemDto>();
}

public sealed class TodayDashboardItemDto
{
    public string Key { get; set; } = "";
    public string Label { get; set; } = "";
    public decimal Value { get; set; }
    public decimal PreviousValue { get; set; }
    public bool ShowsGrowth { get; set; }
    public decimal? GrowthPercent { get; set; }
}
