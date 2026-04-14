using CotizadorInterno.Web.Models;

namespace CotizadorInterno.Web.Models.Dashboard;

public enum BillingPeriodKind
{
    Month = 0,
    Quarter = 1,
    Semester = 2,
    Year = 3
}

public static class BillingPeriodKindExtensions
{
    public static BillingPeriodKind ParseOrDefault(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return BillingPeriodKind.Month;

        return value.Trim().ToLowerInvariant() switch
        {
            "month" or "mes" or "mensual" => BillingPeriodKind.Month,
            "quarter" or "trimestre" => BillingPeriodKind.Quarter,
            "semester" or "semestre" => BillingPeriodKind.Semester,
            "year" or "ano" or "año" or "anual" => BillingPeriodKind.Year,
            _ => BillingPeriodKind.Month
        };
    }

    public static string ToKey(this BillingPeriodKind value) => value switch
    {
        BillingPeriodKind.Quarter => "quarter",
        BillingPeriodKind.Semester => "semester",
        BillingPeriodKind.Year => "year",
        _ => "month"
    };

    public static string ToLabel(this BillingPeriodKind value) => value switch
    {
        BillingPeriodKind.Quarter => "Trimestre",
        BillingPeriodKind.Semester => "Semestre",
        BillingPeriodKind.Year => "Anual",
        _ => "Mes"
    };
}

public sealed class DashboardPageViewModel
{
    public CurrentUserInfo CurrentUser { get; set; } = new();
    public int InitialYear { get; set; }
    public BillingPeriodKind InitialPeriodKind { get; set; } = BillingPeriodKind.Month;
    public int InitialPeriodValue { get; set; } = 1;
}

public sealed class BillingDashboardDto
{
    public int Year { get; set; }
    public int CompareYear { get; set; }
    public string PeriodKind { get; set; } = BillingPeriodKind.Month.ToKey();
    public string PeriodKindLabel { get; set; } = BillingPeriodKind.Month.ToLabel();
    public int PeriodValue { get; set; }
    public string PeriodLabel { get; set; } = "";
    public string DateRangeLabel { get; set; } = "";
    public string CompareLabel { get; set; } = "";
    public string GranularityLabel { get; set; } = "";
    public string EmptyStateTitle { get; set; } = "";
    public string EmptyStateMessage { get; set; } = "";
    public bool HasData { get; set; }
    public int RecordsCount { get; set; }
    public int CompareRecordsCount { get; set; }
    public IReadOnlyList<BillingKpiDto> Kpis { get; set; } = Array.Empty<BillingKpiDto>();
    public IReadOnlyList<BillingTrendPointDto> Trend { get; set; } = Array.Empty<BillingTrendPointDto>();
    public IReadOnlyList<BillingVerticalSummaryDto> Verticals { get; set; } = Array.Empty<BillingVerticalSummaryDto>();
    public IReadOnlyList<BillingClientSummaryDto> TopClients { get; set; } = Array.Empty<BillingClientSummaryDto>();
    public IReadOnlyList<BillingRetentionItemDto> Retentions { get; set; } = Array.Empty<BillingRetentionItemDto>();
    public IReadOnlyList<BillingUnpaidInvoiceDto> UnpaidInvoices { get; set; } = Array.Empty<BillingUnpaidInvoiceDto>();
    public IReadOnlyList<BillingDifferenceInvoiceDto> DifferenceInvoices { get; set; } = Array.Empty<BillingDifferenceInvoiceDto>();
}

public sealed class BillingKpiDto
{
    public string Key { get; set; } = "";
    public string Label { get; set; } = "";
    public string Hint { get; set; } = "";
    public decimal Value { get; set; }
    public decimal PreviousValue { get; set; }
    public decimal? GrowthPercent { get; set; }
    public string ValueFormat { get; set; } = "currency";
    public string Tone { get; set; } = "neutral";
    public string SecondaryLabel { get; set; } = "";
    public string SecondaryValue { get; set; } = "";
    public IReadOnlyList<BillingKpiBreakdownDto> Breakdowns { get; set; } = Array.Empty<BillingKpiBreakdownDto>();
}

public sealed class BillingKpiBreakdownDto
{
    public string Key { get; set; } = "";
    public string Label { get; set; } = "";
    public decimal Value { get; set; }
    public decimal SharePercent { get; set; }
}

public sealed class BillingTrendPointDto
{
    public string Key { get; set; } = "";
    public string Label { get; set; } = "";
    public decimal BillingCurrent { get; set; }
    public decimal BillingPrevious { get; set; }
    public decimal CollectionsCurrent { get; set; }
    public decimal CollectionsPrevious { get; set; }
    public decimal RetentionsCurrent { get; set; }
    public decimal RetentionsPrevious { get; set; }
}

public sealed class BillingVerticalSummaryDto
{
    public string Key { get; set; } = "";
    public string Label { get; set; } = "";
    public int InvoicesCount { get; set; }
    public int UnpaidInvoicesCount { get; set; }
    public decimal TotalBilling { get; set; }
    public decimal PreviousTotalBilling { get; set; }
    public decimal? GrowthPercent { get; set; }
    public decimal TotalVat { get; set; }
    public decimal PreviousTotalVat { get; set; }
    public decimal? VatGrowthPercent { get; set; }
    public decimal UnpaidAmount { get; set; }
    public IReadOnlyList<BillingContractTypeSummaryDto> ContractTypes { get; set; } = Array.Empty<BillingContractTypeSummaryDto>();
}

public sealed class BillingContractTypeSummaryDto
{
    public string Key { get; set; } = "";
    public string Label { get; set; } = "";
    public decimal TotalBilling { get; set; }
    public decimal PreviousTotalBilling { get; set; }
    public decimal? GrowthPercent { get; set; }
    public decimal SharePercent { get; set; }
}

public sealed class BillingClientSummaryDto
{
    public string Key { get; set; } = "";
    public string ClientName { get; set; } = "";
    public int InvoicesCount { get; set; }
    public decimal TotalBilling { get; set; }
    public decimal PreviousTotalBilling { get; set; }
    public decimal? GrowthPercent { get; set; }
    public decimal SharePercent { get; set; }
}

public sealed class BillingRetentionItemDto
{
    public string Key { get; set; } = "";
    public string Label { get; set; } = "";
    public decimal Total { get; set; }
    public decimal PreviousTotal { get; set; }
    public decimal? GrowthPercent { get; set; }
}

public sealed class BillingUnpaidInvoiceDto
{
    public string InvoiceNumber { get; set; } = "";
    public string ClientName { get; set; } = "";
    public string VerticalLabel { get; set; } = "";
    public string ContractTypeLabel { get; set; } = "";
    public string DueDateDisplay { get; set; } = "";
    public decimal TotalInvoice { get; set; }
    public int AgeDays { get; set; }
}

public sealed class BillingDifferenceInvoiceDto
{
    public string InvoiceNumber { get; set; } = "";
    public string ClientName { get; set; } = "";
    public string VerticalLabel { get; set; } = "";
    public string PaymentDateDisplay { get; set; } = "";
    public decimal TotalInvoice { get; set; }
    public decimal PaymentValue { get; set; }
    public decimal RetentionsTotal { get; set; }
    public decimal Difference { get; set; }
    public bool IsBalanced { get; set; }
}
