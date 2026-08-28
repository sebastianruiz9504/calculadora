using CotizadorInterno.Web.Models;

namespace CotizadorInterno.Web.Models.Dashboard;

public enum BillingPeriodKind
{
    Month = 0,
    Quarter = 1,
    Semester = 2,
    Year = 3,
    Bimonthly = 4
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
            "bimonthly" or "bimensual" or "bimestre" => BillingPeriodKind.Bimonthly,
            "quarter" or "trimestre" => BillingPeriodKind.Quarter,
            "semester" or "semestre" => BillingPeriodKind.Semester,
            "year" or "ano" or "año" or "anual" => BillingPeriodKind.Year,
            _ => BillingPeriodKind.Month
        };
    }

    public static string ToKey(this BillingPeriodKind value) => value switch
    {
        BillingPeriodKind.Bimonthly => "bimonthly",
        BillingPeriodKind.Quarter => "quarter",
        BillingPeriodKind.Semester => "semester",
        BillingPeriodKind.Year => "year",
        _ => "month"
    };

    public static string ToLabel(this BillingPeriodKind value) => value switch
    {
        BillingPeriodKind.Bimonthly => "Bimensual",
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
    public string InitialSupportStartDate { get; set; } = "";
    public string InitialSupportEndDate { get; set; } = "";
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
    public int CreditNotesCount { get; set; }
    public int CompareCreditNotesCount { get; set; }
    public IReadOnlyList<BillingKpiDto> Kpis { get; set; } = Array.Empty<BillingKpiDto>();
    public IReadOnlyList<BillingTrendPointDto> Trend { get; set; } = Array.Empty<BillingTrendPointDto>();
    public IReadOnlyList<BillingVerticalSummaryDto> Verticals { get; set; } = Array.Empty<BillingVerticalSummaryDto>();
    public IReadOnlyList<BillingClientSummaryDto> TopClients { get; set; } = Array.Empty<BillingClientSummaryDto>();
    public IReadOnlyList<BillingRetentionItemDto> Retentions { get; set; } = Array.Empty<BillingRetentionItemDto>();
    public IReadOnlyList<BillingUnpaidInvoiceDto> UnpaidInvoices { get; set; } = Array.Empty<BillingUnpaidInvoiceDto>();
    public IReadOnlyList<BillingDifferenceInvoiceDto> DifferenceInvoices { get; set; } = Array.Empty<BillingDifferenceInvoiceDto>();
}

public sealed class PortfolioDashboardDto
{
    public string AsOfDateLabel { get; set; } = "";
    public string FocusLabel { get; set; } = "";
    public bool HasData { get; set; }
    public int RecordsCount { get; set; }
    public string EmptyStateTitle { get; set; } = "";
    public string EmptyStateMessage { get; set; } = "";
    public IReadOnlyList<PortfolioKpiDto> Kpis { get; set; } = Array.Empty<PortfolioKpiDto>();
    public IReadOnlyList<BillingUnpaidInvoiceDto> OverdueInvoices { get; set; } = Array.Empty<BillingUnpaidInvoiceDto>();
    public IReadOnlyList<BillingInvoiceRowDto> Invoices { get; set; } = Array.Empty<BillingInvoiceRowDto>();
}

public sealed class AccountStatementDto
{
    public string ClientId { get; set; } = "";
    public string ClientName { get; set; } = "";
    public string AsOfDateLabel { get; set; } = "";
    public bool HasData { get; set; }
    public int RecordsCount { get; set; }
    public decimal TotalAmount { get; set; }
    public string EmptyStateTitle { get; set; } = "";
    public string EmptyStateMessage { get; set; } = "";
    public IReadOnlyList<AccountStatementInvoiceDto> Invoices { get; set; } = Array.Empty<AccountStatementInvoiceDto>();
}

public sealed class AccountStatementInvoiceDto
{
    public string RecordId { get; set; } = "";
    public string InvoiceNumber { get; set; } = "";
    public decimal TotalInvoice { get; set; }
    public decimal CreditNoteTotal { get; set; }
    public decimal NetTotalInvoice { get; set; }
    public string EmissionDateValue { get; set; } = "";
    public string EmissionDateDisplay { get; set; } = "";
    public string DueDateValue { get; set; } = "";
    public string DueDateDisplay { get; set; } = "";
    public string StateKey { get; set; } = "";
    public string StateLabel { get; set; } = "";
    public int DaysValue { get; set; }
    public string DaysDisplay { get; set; } = "";
    public string PublicUrl { get; set; } = "";
}

public sealed class BusinessDashboardDto
{
    public string AsOfDateLabel { get; set; } = "";
    public string FocusLabel { get; set; } = "";
    public bool HasData { get; set; }
    public int RecordsCount { get; set; }
    public int ClientsCount { get; set; }
    public int ProductsCount { get; set; }
    public decimal TotalAnnualValueUsd { get; set; }
    public decimal MonthlyBillingUsd { get; set; }
    public decimal AverageContractValueUsd { get; set; }
    public string EmptyStateTitle { get; set; } = "";
    public string EmptyStateMessage { get; set; } = "";
    public IReadOnlyList<BusinessKpiDto> Kpis { get; set; } = Array.Empty<BusinessKpiDto>();
    public BusinessProjectionDto Projection { get; set; } = new();
    public IReadOnlyList<BusinessContractSummaryDto> TopContracts { get; set; } = Array.Empty<BusinessContractSummaryDto>();
    public IReadOnlyList<BusinessLineSummaryDto> LineSummaries { get; set; } = Array.Empty<BusinessLineSummaryDto>();
    public IReadOnlyList<BusinessProductSummaryDto> TopProducts { get; set; } = Array.Empty<BusinessProductSummaryDto>();
    public IReadOnlyList<BusinessContractTypeSummaryDto> ContractTypes { get; set; } = Array.Empty<BusinessContractTypeSummaryDto>();
}

public sealed class BusinessProjectionDto
{
    public string PeriodLabel { get; set; } = "";
    public string DateRangeLabel { get; set; } = "";
    public string HistoryPeriodLabel { get; set; } = "";
    public decimal StandardTrm { get; set; }
    public decimal RecurringBillingUsd { get; set; }
    public decimal RecurringBillingCop { get; set; }
    public int RecurringRecordsCount { get; set; }
    public decimal CurrentCostsCop { get; set; }
    public int CostRecordsCount { get; set; }
    public decimal ProjectedMonthlyUtilityCop { get; set; }
    public decimal? ProjectedMonthlyUtilityPercent { get; set; }
    public decimal CurrentPayrollCop { get; set; }
    public int PayrollRecordsCount { get; set; }
    public decimal ProjectedMonthlyUtilityAfterPayrollCop { get; set; }
    public decimal? ProjectedMonthlyUtilityAfterPayrollPercent { get; set; }
    public IReadOnlyList<BusinessKpiDto> Kpis { get; set; } = Array.Empty<BusinessKpiDto>();
    public IReadOnlyList<BusinessProjectionMonthRowDto> MonthlyRows { get; set; } = Array.Empty<BusinessProjectionMonthRowDto>();
}

public sealed class BusinessProjectionMonthRowDto
{
    public string Key { get; set; } = "";
    public string MonthYearLabel { get; set; } = "";
    public decimal RealMonthlyBillingCop { get; set; }
    public int BillingRecordsCount { get; set; }
    public decimal CurrentCostsCop { get; set; }
    public int CostRecordsCount { get; set; }
    public decimal ProjectedMonthlyUtilityCop { get; set; }
    public decimal? ProjectedMonthlyUtilityPercent { get; set; }
    public decimal PayrollCop { get; set; }
    public int PayrollRecordsCount { get; set; }
    public decimal ProjectedNetUtilityCop { get; set; }
    public decimal? ProjectedNetUtilityPercent { get; set; }
}

public sealed class BusinessKpiDto
{
    public string Key { get; set; } = "";
    public string Label { get; set; } = "";
    public string Hint { get; set; } = "";
    public decimal Value { get; set; }
    public string ValueFormat { get; set; } = "usd";
    public string SecondaryLabel { get; set; } = "";
    public string SecondaryValue { get; set; } = "";
}

public sealed class BusinessContractSummaryDto
{
    public string Key { get; set; } = "";
    public string ClientId { get; set; } = "";
    public string ClientName { get; set; } = "";
    public decimal AnnualValueUsd { get; set; }
    public decimal MonthlyBillingUsd { get; set; }
    public int RecordsCount { get; set; }
    public int ProductsCount { get; set; }
    public string TopProductName { get; set; } = "";
    public decimal SharePercent { get; set; }
}

public sealed class BusinessLineSummaryDto
{
    public string Key { get; set; } = "";
    public string Label { get; set; } = "";
    public decimal AnnualValueUsd { get; set; }
    public decimal MonthlyBillingUsd { get; set; }
    public int RecordsCount { get; set; }
    public int ClientsCount { get; set; }
    public int Quantity { get; set; }
    public decimal SharePercent { get; set; }
}

public sealed class BusinessProductSummaryDto
{
    public string Key { get; set; } = "";
    public string ProductId { get; set; } = "";
    public string ProductName { get; set; } = "";
    public decimal AnnualValueUsd { get; set; }
    public decimal MonthlyBillingUsd { get; set; }
    public int Quantity { get; set; }
    public int ClientsCount { get; set; }
    public int RecordsCount { get; set; }
    public decimal SharePercent { get; set; }
}

public sealed class BusinessContractTypeSummaryDto
{
    public string Key { get; set; } = "";
    public string Label { get; set; } = "";
    public decimal AnnualValueUsd { get; set; }
    public decimal MonthlyBillingUsd { get; set; }
    public int RecordsCount { get; set; }
    public decimal SharePercent { get; set; }
}

public sealed class CloudBillingCurrentMonthDashboardDto
{
    public string AsOfDateLabel { get; set; } = "";
    public string PeriodLabel { get; set; } = "";
    public string DateRangeLabel { get; set; } = "";
    public bool HasData { get; set; }
    public int RecordsCount { get; set; }
    public int BilledCount { get; set; }
    public int PendingCount { get; set; }
    public int DueTodayCount { get; set; }
    public int OverdueCount { get; set; }
    public int ErrorCount { get; set; }
    public int DianAcceptedCount { get; set; }
    public int DianPendingCount { get; set; }
    public int EmailSentCount { get; set; }
    public int EmailPendingCount { get; set; }
    public int SiigoInvoicesCheckedCount { get; set; }
    public int SiigoMatchedInvoiceCount { get; set; }
    public string SiigoValidationError { get; set; } = "";
    public decimal TotalMonthlyUsd { get; set; }
    public decimal BilledMonthlyUsd { get; set; }
    public decimal PendingMonthlyUsd { get; set; }
    public decimal DueTodayMonthlyUsd { get; set; }
    public decimal OverdueMonthlyUsd { get; set; }
    public string EmptyStateTitle { get; set; } = "";
    public string EmptyStateMessage { get; set; } = "";
    public IReadOnlyList<PortfolioKpiDto> Kpis { get; set; } = Array.Empty<PortfolioKpiDto>();
    public IReadOnlyList<CloudBillingCurrentMonthRowDto> Rows { get; set; } = Array.Empty<CloudBillingCurrentMonthRowDto>();
}

public sealed class CloudBillingCurrentMonthRowDto
{
    public string RecordId { get; set; } = "";
    public string ClientId { get; set; } = "";
    public string ClientName { get; set; } = "";
    public string ProductId { get; set; } = "";
    public string ProductName { get; set; } = "";
    public string ProductLineLabel { get; set; } = "";
    public string ContractTypeLabel { get; set; } = "";
    public decimal Quantity { get; set; }
    public decimal UnitSaleUsd { get; set; }
    public decimal MonthlyBillingUsd { get; set; }
    public bool IsAutomaticBilling { get; set; }
    public bool ProductBilledFlag { get; set; }
    public int BillingDay { get; set; }
    public string BillingDayDisplay { get; set; } = "";
    public string ExpectedBillingDateValue { get; set; } = "";
    public string ExpectedBillingDateDisplay { get; set; } = "";
    public string LastInvoiceDateValue { get; set; } = "";
    public string LastInvoiceDateDisplay { get; set; } = "";
    public string LastSiigoInvoiceId { get; set; } = "";
    public string BillingError { get; set; } = "";
    public string MonthInvoiceNumbers { get; set; } = "";
    public int MonthInvoiceCount { get; set; }
    public IReadOnlyList<CloudBillingInvoiceReferenceDto> MonthInvoices { get; set; } = Array.Empty<CloudBillingInvoiceReferenceDto>();
    public bool MatchedByInvoiceTable { get; set; }
    public bool HasSiigoInvoice { get; set; }
    public string MatchedSiigoInvoiceId { get; set; } = "";
    public string MatchedSiigoInvoiceName { get; set; } = "";
    public bool IsSiigoInvoiceAnnulled { get; set; }
    public string DianStatus { get; set; } = "";
    public string DianStatusLabel { get; set; } = "Sin validar";
    public string DianStatusTone { get; set; } = "neutral";
    public string DianObservations { get; set; } = "";
    public string DianErrors { get; set; } = "";
    public bool IsDianAccepted { get; set; }
    public bool IsDianRejected { get; set; }
    public string MailStatus { get; set; } = "";
    public string MailStatusLabel { get; set; } = "Sin validar";
    public string MailStatusTone { get; set; } = "neutral";
    public string MailObservations { get; set; } = "";
    public bool IsEmailSent { get; set; }
    public bool IsBillingComplete { get; set; }
    public bool IsBilled { get; set; }
    public bool IsPending { get; set; }
    public bool IsDueToday { get; set; }
    public bool IsOverdue { get; set; }
    public bool HasBillingError { get; set; }
    public string StatusKey { get; set; } = "";
    public string StatusLabel { get; set; } = "";
    public string StatusTone { get; set; } = "neutral";
    public string EvidenceLabel { get; set; } = "";
}

public sealed class CloudBillingInvoiceReferenceDto
{
    public string RecordId { get; set; } = "";
    public string InvoiceNumber { get; set; } = "";
    public string InvoiceCode { get; set; } = "";
    public string InvoicePrefix { get; set; } = "";
    public string SiigoInvoiceId { get; set; } = "";
    public string SiigoInvoiceName { get; set; } = "";
}

public sealed class CopiersDashboardDto
{
    public string AsOfDateLabel { get; set; } = "";
    public string FocusLabel { get; set; } = "";
    public string CounterPeriodValue { get; set; } = "";
    public string CounterPeriodLabel { get; set; } = "";
    public bool HasData { get; set; }
    public int RecordsCount { get; set; }
    public string EmptyStateTitle { get; set; } = "";
    public string EmptyStateMessage { get; set; } = "";
    public IReadOnlyList<PortfolioKpiDto> Kpis { get; set; } = Array.Empty<PortfolioKpiDto>();
    public IReadOnlyList<CopiersBillingGroupDto> Groups { get; set; } = Array.Empty<CopiersBillingGroupDto>();
    public IReadOnlyList<CopiersBillingRowDto> Rows { get; set; } = Array.Empty<CopiersBillingRowDto>();
}

public sealed class CopiersBillingGroupDto
{
    public string GroupId { get; set; } = "";
    public string ClientId { get; set; } = "";
    public string ClientName { get; set; } = "";
    public int BillingDay { get; set; }
    public string BillingDayDisplay { get; set; } = "";
    public int ProductLinesCount { get; set; }
    public int EquipmentCount { get; set; }
    public int CountersRegisteredCount { get; set; }
    public int PendingCountersCount { get; set; }
    public decimal Quantity { get; set; }
    public decimal IncludedOperations { get; set; }
    public bool GroupIncludedOperations { get; set; } = true;
    public decimal AdditionalOperation { get; set; }
    public decimal TotalWithVat { get; set; }
    public string CounterSummary { get; set; } = "";
    public int EquipmentAssignedToLinesCount { get; set; }
    public int EquipmentAvailableForLinesCount { get; set; }
    public string EquipmentAssignmentSummary { get; set; } = "";
    public IReadOnlyList<CopiersBillingRowDto> Lines { get; set; } = Array.Empty<CopiersBillingRowDto>();
    public IReadOnlyList<CopiersBillingEquipmentDto> Equipment { get; set; } = Array.Empty<CopiersBillingEquipmentDto>();
}

public sealed class CopiersBillingEquipmentDto
{
    public string RecordId { get; set; } = "";
    public string Serial { get; set; } = "";
    public string ClientId { get; set; } = "";
    public string ClientName { get; set; } = "";
    public string CategoryLabel { get; set; } = "";
    public string Reference { get; set; } = "";
    public string Area { get; set; } = "";
    public string Site { get; set; } = "";
    public bool HasCurrentCounter { get; set; }
    public string CounterDateValue { get; set; } = "";
    public string CounterDateDisplay { get; set; } = "";
    public long? CounterCopies { get; set; }
    public long? CounterScans { get; set; }
    public string CounterStatusLabel { get; set; } = "";
    public string CounterStatusTone { get; set; } = "";
}

public sealed class CopiersBillingRowDto
{
    public string RecordId { get; set; } = "";
    public string ClientId { get; set; } = "";
    public string ProductId { get; set; } = "";
    public string ClientName { get; set; } = "";
    public string ProductName { get; set; } = "";
    public decimal Quantity { get; set; }
    public decimal IncludedOperations { get; set; }
    public bool GroupIncludedOperations { get; set; } = true;
    public decimal AdditionalOperation { get; set; }
    public decimal UnitValueBeforeVat { get; set; }
    public decimal UnitValueWithVat { get; set; }
    public decimal TotalWithVat { get; set; }
    public int BillingDay { get; set; }
    public string BillingDayDisplay { get; set; } = "";
    public int EquipmentAssignmentCapacity { get; set; }
    public int AssignedEquipmentCount { get; set; }
    public int AvailableEquipmentCount { get; set; }
    public string EquipmentAssignmentSummary { get; set; } = "";
    public bool HasAssignmentOverflow { get; set; }
}

public sealed class CopiersLineEquipmentAssignmentDetailDto
{
    public string LineId { get; set; } = "";
    public string ClientId { get; set; } = "";
    public string ClientName { get; set; } = "";
    public string ProductName { get; set; } = "";
    public decimal Quantity { get; set; }
    public decimal IncludedOperations { get; set; }
    public int AssignmentCapacity { get; set; }
    public int AssignedCount { get; set; }
    public int AvailableCount { get; set; }
    public string Summary { get; set; } = "";
    public IReadOnlyList<CopiersLineEquipmentAssignmentItemDto> AssignedEquipment { get; set; } = Array.Empty<CopiersLineEquipmentAssignmentItemDto>();
    public IReadOnlyList<CopiersLineEquipmentAssignmentItemDto> AvailableEquipment { get; set; } = Array.Empty<CopiersLineEquipmentAssignmentItemDto>();
}

public sealed class CopiersLineEquipmentAssignmentItemDto
{
    public string AssignmentId { get; set; } = "";
    public string EquipmentId { get; set; } = "";
    public string Serial { get; set; } = "";
    public string CategoryLabel { get; set; } = "";
    public string Reference { get; set; } = "";
    public string Area { get; set; } = "";
    public string Site { get; set; } = "";
    public string AssignedLineId { get; set; } = "";
    public string AssignedLineName { get; set; } = "";
}

public sealed class CopiersLineEquipmentAssignmentSaveRequestDto
{
    public string LineId { get; set; } = "";
    public string ClientId { get; set; } = "";
    public List<string> EquipmentIds { get; set; } = new();
}

public sealed class CopiersLineEquipmentAssignmentSaveResultDto
{
    public string Message { get; set; } = "";
    public CopiersLineEquipmentAssignmentDetailDto Detail { get; set; } = new();
}

public sealed class CopiersRecordSaveRequestDto
{
    public string RecordId { get; set; } = "";
    public string ClientId { get; set; } = "";
    public string ClientName { get; set; } = "";
    public string ProductId { get; set; } = "";
    public string ProductName { get; set; } = "";
    public decimal Quantity { get; set; }
    public decimal IncludedOperations { get; set; }
    public decimal AdditionalOperation { get; set; }
    public decimal UnitValueBeforeVat { get; set; }
    public int? BillingDay { get; set; }
    public decimal UnitValueWithVat { get; set; }
    public decimal TotalWithVat { get; set; }
}

public sealed class CopiersRecordSaveResultDto
{
    public string RecordId { get; set; } = "";
    public bool IsCreated { get; set; }
    public string Message { get; set; } = "";
}

public sealed class CopiersClientInvoicesDetailDto
{
    public string ClientId { get; set; } = "";
    public string ClientName { get; set; } = "";
    public bool HasData { get; set; }
    public int RecordsCount { get; set; }
    public string EmptyStateTitle { get; set; } = "";
    public string EmptyStateMessage { get; set; } = "";
    public IReadOnlyList<CopiersClientInvoiceRowDto> Invoices { get; set; } = Array.Empty<CopiersClientInvoiceRowDto>();
}

public sealed class CopiersClientInvoiceRowDto
{
    public string RecordId { get; set; } = "";
    public string InvoiceNumber { get; set; } = "";
    public string PublicUrl { get; set; } = "";
    public decimal TotalInvoice { get; set; }
    public decimal CreditNoteTotal { get; set; }
    public decimal NetTotalInvoice { get; set; }
    public string EmissionDateValue { get; set; } = "";
    public string EmissionDateDisplay { get; set; } = "";
    public string PaymentDateValue { get; set; } = "";
    public string PaymentDateDisplay { get; set; } = "";
    public decimal PaymentValue { get; set; }
    public bool IsPaymentOverdue { get; set; }
}

public sealed class BillingClientReportDto
{
    public string ClientId { get; set; } = "";
    public string ClientName { get; set; } = "";
    public bool HasData { get; set; }
    public int RecordsCount { get; set; }
    public string EmptyStateTitle { get; set; } = "";
    public string EmptyStateMessage { get; set; } = "";
    public IReadOnlyList<BillingClientReportInvoiceDto> Invoices { get; set; } = Array.Empty<BillingClientReportInvoiceDto>();
}

public sealed class BillingClientReportInvoiceDto
{
    public string RecordId { get; set; } = "";
    public string InvoiceNumber { get; set; } = "";
    public string ClientId { get; set; } = "";
    public string ClientName { get; set; } = "";
    public string CompanyTaxId { get; set; } = "";
    public decimal VatPercent { get; set; }
    public decimal VatValue { get; set; }
    public decimal CreditNoteVat { get; set; }
    public decimal NetVatValue { get; set; }
    public decimal TotalInvoice { get; set; }
    public decimal CreditNoteTotal { get; set; }
    public decimal NetTotalInvoice { get; set; }
    public string PublicUrl { get; set; } = "";
    public string EmissionDateValue { get; set; } = "";
    public string EmissionDateDisplay { get; set; } = "";
    public string VerticalLabel { get; set; } = "";
    public string ContractTypeLabel { get; set; } = "";
}

public sealed class BillingClientReportExportRequestDto
{
    public string ClientId { get; set; } = "";
    public string ClientName { get; set; } = "";
    public IReadOnlyList<BillingClientReportExportItemDto> Items { get; set; } = Array.Empty<BillingClientReportExportItemDto>();
}

public sealed class BillingClientReportExportItemDto
{
    public string RecordId { get; set; } = "";
    public decimal? ExportAmount { get; set; }
}

public sealed class CopiersEquipmentDashboardDto
{
    public string AsOfDateLabel { get; set; } = "";
    public string FocusLabel { get; set; } = "";
    public bool HasData { get; set; }
    public int RecordsCount { get; set; }
    public string EmptyStateTitle { get; set; } = "";
    public string EmptyStateMessage { get; set; } = "";
    public IReadOnlyList<PortfolioKpiDto> Kpis { get; set; } = Array.Empty<PortfolioKpiDto>();
    public IReadOnlyList<CopiersEquipmentClientSummaryDto> ClientSummaries { get; set; } = Array.Empty<CopiersEquipmentClientSummaryDto>();
    public IReadOnlyList<CopiersEquipmentRowDto> EquipmentRows { get; set; } = Array.Empty<CopiersEquipmentRowDto>();
    public IReadOnlyList<CopiersEquipmentRowDto> StockRows { get; set; } = Array.Empty<CopiersEquipmentRowDto>();
    public IReadOnlyList<CopiersMaintenanceRowDto> MaintenanceRows { get; set; } = Array.Empty<CopiersMaintenanceRowDto>();
    public IReadOnlyList<CopiersEquipmentOptionDto> CategoryOptions { get; set; } = Array.Empty<CopiersEquipmentOptionDto>();
    public CopiersMaintenanceChartDto MaintenanceChart { get; set; } = new();
}

public sealed class CopiersCommercialInventoryDto
{
    public string AsOfDateLabel { get; set; } = "";
    public string FocusLabel { get; set; } = "";
    public bool HasData { get; set; }
    public int RecordsCount { get; set; }
    public bool CommercialValueColumnExists { get; set; }
    public int ValuedRecordsCount { get; set; }
    public int SuggestedRecordsCount { get; set; }
    public int PendingRecordsCount { get; set; }
    public decimal TotalCommercialValue { get; set; }
    public IReadOnlyList<PortfolioKpiDto> Kpis { get; set; } = Array.Empty<PortfolioKpiDto>();
    public IReadOnlyList<CopiersCommercialInventoryRowDto> Records { get; set; } = Array.Empty<CopiersCommercialInventoryRowDto>();
    public IReadOnlyList<CopiersCommercialInventoryReferenceGroupDto> PendingReferenceGroups { get; set; } = Array.Empty<CopiersCommercialInventoryReferenceGroupDto>();
}

public sealed class CopiersCommercialInventoryRowDto
{
    public string RecordId { get; set; } = "";
    public string Serial { get; set; } = "";
    public string Reference { get; set; } = "";
    public string ReferenceGroupKey { get; set; } = "";
    public string ClientName { get; set; } = "";
    public string CategoryLabel { get; set; } = "";
    public bool InStock { get; set; }
    public decimal? CommercialValue { get; set; }
    public decimal? SuggestedCommercialValue { get; set; }
    public decimal? EffectiveCommercialValue { get; set; }
    public string CommercialValueSource { get; set; } = "";
}

public sealed class CopiersCommercialInventoryReferenceGroupDto
{
    public string Key { get; set; } = "";
    public string Reference { get; set; } = "";
    public int EquipmentCount { get; set; }
    public IReadOnlyList<string> Examples { get; set; } = Array.Empty<string>();
}

public sealed class CopiersCommercialValueColumnEnsureResultDto
{
    public bool ColumnExists { get; set; }
    public bool ColumnCreated { get; set; }
    public string Message { get; set; } = "";
}

public sealed class CopiersCommercialValueSeedResultDto
{
    public int UpdatedCount { get; set; }
    public int SkippedCount { get; set; }
    public string Message { get; set; } = "";
}

public sealed class CopiersEquipmentClientSummaryDto
{
    public string ClientId { get; set; } = "";
    public string ClientName { get; set; } = "";
    public string ContactName { get; set; } = "";
    public string Email { get; set; } = "";
    public string Phone { get; set; } = "";
    public string Address { get; set; } = "";
    public int EquipmentCount { get; set; }
    public string CategoryBreakdown { get; set; } = "";
}

public sealed class CopiersEquipmentRowDto
{
    public string RecordId { get; set; } = "";
    public string Serial { get; set; } = "";
    public string ClientId { get; set; } = "";
    public string ClientName { get; set; } = "";
    public int? CategoryValue { get; set; }
    public string CategoryLabel { get; set; } = "";
    public string Reference { get; set; } = "";
    public string Area { get; set; } = "";
    public string Site { get; set; } = "";
    public string Observations { get; set; } = "";
    public bool InStock { get; set; }
    public int MaintenanceCount { get; set; }
    public string LastMaintenanceDateValue { get; set; } = "";
    public string LastMaintenanceDateDisplay { get; set; } = "";
}

public sealed class CopiersEquipmentDetailDto
{
    public CopiersEquipmentRowDto Equipment { get; set; } = new();
    public IReadOnlyList<CopiersMaintenanceRowDto> MaintenanceRows { get; set; } = Array.Empty<CopiersMaintenanceRowDto>();
    public IReadOnlyList<CopiersEquipmentMovementRowDto> MovementRows { get; set; } = Array.Empty<CopiersEquipmentMovementRowDto>();
    public IReadOnlyList<CopiersEquipmentOptionDto> CategoryOptions { get; set; } = Array.Empty<CopiersEquipmentOptionDto>();
}

public sealed class CopiersEquipmentOptionDto
{
    public int Value { get; set; }
    public string Label { get; set; } = "";
}

public sealed class CopiersMaintenanceRowDto
{
    public string RecordId { get; set; } = "";
    public string Title { get; set; } = "";
    public string InternalId { get; set; } = "";
    public string EquipmentId { get; set; } = "";
    public string EquipmentSerial { get; set; } = "";
    public string DateValue { get; set; } = "";
    public string DateDisplay { get; set; } = "";
    public string Description { get; set; } = "";
    public string ClientId { get; set; } = "";
    public string ClientName { get; set; } = "";
    public bool HasAttachment { get; set; }
    public string AttachmentFileName { get; set; } = "";
    public int? MaintenanceTypeValue { get; set; }
    public string MaintenanceTypeLabel { get; set; } = "";
    public int? MaintenanceStatusValue { get; set; }
    public string MaintenanceStatusLabel { get; set; } = "";
    public string TechnicianId { get; set; } = "";
    public string TechnicianName { get; set; } = "";
}

public sealed class CopiersEquipmentAssignmentRequestDto
{
    public string RecordId { get; set; } = "";
    public string ClientId { get; set; } = "";
    public string ClientName { get; set; } = "";
    public bool MoveToStock { get; set; }
}

public sealed class CopiersEquipmentAssignmentResultDto
{
    public string RecordId { get; set; } = "";
    public string Message { get; set; } = "";
    public CopiersEquipmentRowDto Equipment { get; set; } = new();
}

public sealed class CopiersEquipmentSaveRequestDto
{
    public string RecordId { get; set; } = "";
    public string Serial { get; set; } = "";
    public string ClientId { get; set; } = "";
    public string ClientName { get; set; } = "";
    public int? CategoryValue { get; set; }
    public string Reference { get; set; } = "";
    public string Area { get; set; } = "";
    public string Site { get; set; } = "";
    public string Observations { get; set; } = "";
}

public sealed class CopiersEquipmentSaveResultDto
{
    public string RecordId { get; set; } = "";
    public string Message { get; set; } = "";
    public CopiersEquipmentRowDto Equipment { get; set; } = new();
}

public sealed class CopiersEquipmentMovementSaveRequestDto
{
    public string EquipmentId { get; set; } = "";
    public string ClientId { get; set; } = "";
    public string ClientName { get; set; } = "";
    public string DateValue { get; set; } = "";
    public string Reason { get; set; } = "";
}

public sealed class CopiersEquipmentMovementSaveResultDto
{
    public string RecordId { get; set; } = "";
    public string Message { get; set; } = "";
    public CopiersEquipmentRowDto Equipment { get; set; } = new();
    public IReadOnlyList<CopiersEquipmentMovementRowDto> MovementRows { get; set; } = Array.Empty<CopiersEquipmentMovementRowDto>();
}

public sealed class CopiersEquipmentMovementsDashboardDto
{
    public IReadOnlyList<CopiersEquipmentMovementRowDto> Records { get; set; } = Array.Empty<CopiersEquipmentMovementRowDto>();
    public int RecordsCount { get; set; }
    public string FocusLabel { get; set; } = "";
    public string EmptyStateMessage { get; set; } = "";
}

public sealed class CopiersEquipmentMovementRowDto
{
    public string RecordId { get; set; } = "";
    public string EquipmentId { get; set; } = "";
    public string EquipmentSerial { get; set; } = "";
    public string ClientId { get; set; } = "";
    public string ClientName { get; set; } = "";
    public string DateValue { get; set; } = "";
    public string DateDisplay { get; set; } = "";
    public string CreatedOnValue { get; set; } = "";
    public string Reason { get; set; } = "";
    public bool HasAttachment { get; set; }
    public string AttachmentFileName { get; set; } = "";
}

public sealed class CopiersEquipmentClientSaveRequestDto
{
    public string ClientId { get; set; } = "";
    public string ClientName { get; set; } = "";
    public string ContactName { get; set; } = "";
    public string Email { get; set; } = "";
    public string Phone { get; set; } = "";
    public string Address { get; set; } = "";
}

public sealed class CopiersEquipmentClientSaveResultDto
{
    public string ClientId { get; set; } = "";
    public string Message { get; set; } = "";
    public CopiersEquipmentClientSummaryDto Client { get; set; } = new();
}

public sealed class CopiersMaintenanceChartDto
{
    public IReadOnlyList<string> Labels { get; set; } = Array.Empty<string>();
    public IReadOnlyList<CopiersMaintenanceSeriesDto> Series { get; set; } = Array.Empty<CopiersMaintenanceSeriesDto>();
}

public sealed class CopiersMaintenanceSeriesDto
{
    public string TechnicianId { get; set; } = "";
    public string TechnicianName { get; set; } = "";
    public IReadOnlyList<int> Values { get; set; } = Array.Empty<int>();
    public int Total { get; set; }
}

public sealed class TaxesDashboardDto
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
    public TaxesSectionDto ReteFuente { get; set; } = new();
    public TaxesSectionDto ReteIva { get; set; } = new();
    public TaxesSectionDto ReteIca { get; set; } = new();
    public TaxesSectionDto IncomeTax { get; set; } = new();
    public IReadOnlyList<TaxExpenseDetailDto> ExpenseDetails { get; set; } = Array.Empty<TaxExpenseDetailDto>();
}

public sealed class TaxesDashboardRequestDto
{
    public int? Year { get; set; }
    public string? Period { get; set; }
    public int? Value { get; set; }
    public int? ReteFuenteYear { get; set; }
    public int? ReteFuenteMonth { get; set; }
    public int? ReteIcaYear { get; set; }
    public int? ReteIcaPeriod { get; set; }
    public int? IvaYear { get; set; }
    public int? IvaPeriod { get; set; }
    public int? IncomeTaxYear { get; set; }
}

public sealed class TaxesSectionDto
{
    public string Key { get; set; } = "";
    public string Label { get; set; } = "";
    public string Description { get; set; } = "";
    public string PeriodLabel { get; set; } = "";
    public string DateRangeLabel { get; set; } = "";
    public string TotalLabel { get; set; } = "";
    public decimal TotalValue { get; set; }
    public string CalculationBaseLabel { get; set; } = "";
    public decimal CalculationBaseValue { get; set; }
    public TaxesSectionFilterDto Filter { get; set; } = new();
    public IReadOnlyList<BillingKpiDto> Metrics { get; set; } = Array.Empty<BillingKpiDto>();
    public IReadOnlyList<TaxCalculationDetailDto> CalculationDetails { get; set; } = Array.Empty<TaxCalculationDetailDto>();
    public IReadOnlyList<TaxVerticalSummaryDto> VerticalSummaries { get; set; } = Array.Empty<TaxVerticalSummaryDto>();
    public IReadOnlyList<TaxExpenseDetailDto> RetentionDetails { get; set; } = Array.Empty<TaxExpenseDetailDto>();
    public TaxVatDetailsDto VatDetails { get; set; } = new();
    public TaxReportDetailsDto ReportDetails { get; set; } = new();
}

public sealed class TaxesSectionFilterDto
{
    public string Kind { get; set; } = "";
    public int Year { get; set; }
    public int Value { get; set; }
    public string ValueLabel { get; set; } = "";
    public IReadOnlyList<TaxesFilterOptionDto> YearOptions { get; set; } = Array.Empty<TaxesFilterOptionDto>();
    public IReadOnlyList<TaxesFilterOptionDto> ValueOptions { get; set; } = Array.Empty<TaxesFilterOptionDto>();
}

public sealed class TaxesFilterOptionDto
{
    public int Value { get; set; }
    public string Label { get; set; } = "";
}

public sealed class TaxCalculationDetailDto
{
    public string Key { get; set; } = "";
    public string Label { get; set; } = "";
    public string Formula { get; set; } = "";
    public string BaseLabel { get; set; } = "Base total";
    public decimal BaseTotal { get; set; }
    public string InvoiceTotalLabel { get; set; } = "Total facturas";
    public decimal InvoiceTotal { get; set; }
    public int InvoiceCount { get; set; }
    public string ResultLabel { get; set; } = "";
    public decimal ResultValue { get; set; }
    public IReadOnlyList<TaxCalculationDetailLineDto> Lines { get; set; } = Array.Empty<TaxCalculationDetailLineDto>();
}

public sealed class TaxCalculationDetailLineDto
{
    public string Label { get; set; } = "";
    public decimal Value { get; set; }
    public string ValueFormat { get; set; } = "currency";
}

public sealed class TaxVerticalSummaryDto
{
    public string Key { get; set; } = "";
    public string Label { get; set; } = "";
    public string PrimaryLabel { get; set; } = "";
    public decimal PrimaryValue { get; set; }
    public decimal PreviousPrimaryValue { get; set; }
    public decimal? GrowthPercent { get; set; }
    public string Tone { get; set; } = "neutral";
    public bool ShowComparison { get; set; } = true;
    public IReadOnlyList<TaxVerticalComponentDto> Components { get; set; } = Array.Empty<TaxVerticalComponentDto>();
}

public sealed class TaxVerticalComponentDto
{
    public string Key { get; set; } = "";
    public string Label { get; set; } = "";
    public decimal Value { get; set; }
    public decimal PreviousValue { get; set; }
}

public sealed class TaxExpenseDetailDto
{
    public string PaymentDateDisplay { get; set; } = "";
    public decimal PaymentValue { get; set; }
    public decimal ReteFuenteValue { get; set; }
    public string PersonTypeLabel { get; set; } = "";
    public string RecipientName { get; set; } = "";
    public string RecipientNit { get; set; } = "";
    public decimal CloudValue { get; set; }
    public decimal CopiersValue { get; set; }
}

public sealed class TaxVatDetailsDto
{
    public IReadOnlyList<TaxVatTableDto> Tables { get; set; } = Array.Empty<TaxVatTableDto>();
}

public sealed class TaxVatTableDto
{
    public string Key { get; set; } = "";
    public string Label { get; set; } = "";
    public string DateColumnLabel { get; set; } = "Fecha";
    public string NameColumnLabel { get; set; } = "";
    public string ValueLabel { get; set; } = "";
    public bool ShowRetentionRateColumns { get; set; }
    public decimal TotalValue { get; set; }
    public IReadOnlyList<TaxVatRowDto> Rows { get; set; } = Array.Empty<TaxVatRowDto>();
}

public sealed class TaxVatRowDto
{
    public string DateDisplay { get; set; } = "";
    public string InvoiceNumber { get; set; } = "";
    public string Name { get; set; } = "";
    public string VerticalKey { get; set; } = "";
    public string VerticalLabel { get; set; } = "";
    public decimal TotalValue { get; set; }
    public decimal TaxValue { get; set; }
    public decimal ReteFuentePercent { get; set; }
    public decimal ReteIcaPercent { get; set; }
    public decimal CloudTotalValue { get; set; }
    public decimal CloudTaxValue { get; set; }
    public decimal CopiersTotalValue { get; set; }
    public decimal CopiersTaxValue { get; set; }
    public decimal UnassignedTotalValue { get; set; }
    public decimal UnassignedTaxValue { get; set; }
}

public sealed class TaxReportDetailsDto
{
    public IReadOnlyList<TaxReportTableDto> Tables { get; set; } = Array.Empty<TaxReportTableDto>();
}

public sealed class TaxReportTableDto
{
    public string Key { get; set; } = "";
    public string Label { get; set; } = "";
    public string DateColumnLabel { get; set; } = "Fecha";
    public string DocumentColumnLabel { get; set; } = "Numero factura";
    public string NameColumnLabel { get; set; } = "Nombre";
    public string CustomerIdentificationColumnLabel { get; set; } = "";
    public string TotalColumnLabel { get; set; } = "Total";
    public string BaseColumnLabel { get; set; } = "";
    public string AmountColumnLabel { get; set; } = "Valor";
    public string CategoryColumnLabel { get; set; } = "";
    public bool ShowCustomerIdentificationColumn { get; set; }
    public bool ShowBaseColumn { get; set; }
    public bool ShowCategoryColumn { get; set; }
    public bool ShowReteFuentePercentColumn { get; set; }
    public bool ShowReteIcaPercentColumn { get; set; }
    public decimal TotalBaseValue { get; set; }
    public decimal TotalValue { get; set; }
    public decimal TotalAmountValue { get; set; }
    public IReadOnlyList<TaxReportRowDto> Rows { get; set; } = Array.Empty<TaxReportRowDto>();
}

public sealed class TaxReportRowDto
{
    public string DateDisplay { get; set; } = "";
    public string InvoiceNumber { get; set; } = "";
    public string Name { get; set; } = "";
    public string CustomerIdentification { get; set; } = "";
    public string Category { get; set; } = "";
    public decimal BaseValue { get; set; }
    public decimal TotalValue { get; set; }
    public decimal AmountValue { get; set; }
    public decimal ReteFuentePercent { get; set; }
    public decimal ReteIcaPercent { get; set; }
}

public sealed class PortfolioKpiDto
{
    public string Key { get; set; } = "";
    public string Label { get; set; } = "";
    public string Hint { get; set; } = "";
    public decimal Value { get; set; }
    public string ValueFormat { get; set; } = "currency";
    public string SecondaryLabel { get; set; } = "";
    public string SecondaryValue { get; set; } = "";
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
    public bool ShowComparison { get; set; } = true;
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
    public decimal? BillingGrowthPercent { get; set; }
    public decimal CollectionsCurrent { get; set; }
    public decimal CollectionsPrevious { get; set; }
    public decimal? CollectionsGrowthPercent { get; set; }
    public decimal RetentionsCurrent { get; set; }
    public decimal RetentionsPrevious { get; set; }
    public decimal? RetentionsGrowthPercent { get; set; }
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

public sealed class BillingInvoiceRowDto
{
    public string RecordId { get; set; } = "";
    public string InvoiceNumber { get; set; } = "";
    public string ClientId { get; set; } = "";
    public string ClientName { get; set; } = "";
    public string CompanyTaxId { get; set; } = "";
    public int? VerticalOptionValue { get; set; }
    public string VerticalLabel { get; set; } = "";
    public int? ContractTypeOptionValue { get; set; }
    public string ContractTypeLabel { get; set; } = "";
    public string EmissionDateValue { get; set; } = "";
    public string EmissionDateDisplay { get; set; } = "";
    public string DueDateValue { get; set; } = "";
    public string DueDateDisplay { get; set; } = "";
    public string PaymentDateValue { get; set; } = "";
    public string PaymentDateDisplay { get; set; } = "";
    public decimal TotalInvoice { get; set; }
    public decimal NetTotalInvoice { get; set; }
    public decimal VatPercent { get; set; }
    public decimal VatValue { get; set; }
    public decimal CreditNoteVat { get; set; }
    public decimal NetVatValue { get; set; }
    public decimal NetBeforeVatValue { get; set; }
    public decimal PaymentValue { get; set; }
    public decimal ReteIcaValue { get; set; }
    public decimal RteIvaValue { get; set; }
    public decimal RteFteValue { get; set; }
    public decimal RetentionsTotal { get; set; }
    public decimal DifferenceValue { get; set; }
    public decimal CreditNoteTotal { get; set; }
    public int CreditNoteCount { get; set; }
    public bool IsFullyCredited { get; set; }
    public bool IsPartiallyCredited { get; set; }
    public bool IsPortfolioPending { get; set; }
    public bool IsOverdue { get; set; }
    public string PaymentStatusLabel { get; set; } = "";
    public int AgeDays { get; set; }
    public string PublicUrl { get; set; } = "";
}

public sealed class BillingInvoicesTableDto
{
    public bool HasData { get; set; }
    public int RecordsCount { get; set; }
    public int TotalRecordsCount { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
    public int TotalPages { get; set; } = 1;
    public int Year { get; set; }
    public int Month { get; set; }
    public bool HasPreviousPage { get; set; }
    public bool HasNextPage { get; set; }
    public bool DuplicatesOnly { get; set; }
    public string PeriodLabel { get; set; } = "";
    public string EmptyStateTitle { get; set; } = "";
    public string EmptyStateMessage { get; set; } = "";
    public IReadOnlyList<BillingOptionDto> VerticalOptions { get; set; } = Array.Empty<BillingOptionDto>();
    public IReadOnlyList<BillingOptionDto> ContractTypeOptions { get; set; } = Array.Empty<BillingOptionDto>();
    public IReadOnlyList<BillingInvoiceRowDto> Invoices { get; set; } = Array.Empty<BillingInvoiceRowDto>();
}

public sealed class BillingCreditNotesTableDto
{
    public bool HasData { get; set; }
    public int RecordsCount { get; set; }
    public int MatchedCount { get; set; }
    public int UnmatchedCount { get; set; }
    public decimal TotalAmount { get; set; }
    public decimal VatAmount { get; set; }
    public decimal MatchedAmount { get; set; }
    public decimal UnmatchedAmount { get; set; }
    public string EmptyStateTitle { get; set; } = "";
    public string EmptyStateMessage { get; set; } = "";
    public IReadOnlyList<BillingCreditNoteRowDto> CreditNotes { get; set; } = Array.Empty<BillingCreditNoteRowDto>();
}

public sealed class BillingCreditNoteRowDto
{
    public string RecordId { get; set; } = "";
    public string CreditNoteId { get; set; } = "";
    public string CreditNoteName { get; set; } = "";
    public string DateValue { get; set; } = "";
    public string DateDisplay { get; set; } = "";
    public string InvoiceReference { get; set; } = "";
    public string MatchedInvoiceNumber { get; set; } = "";
    public string ClientName { get; set; } = "";
    public string CustomerIdentification { get; set; } = "";
    public decimal BaseValue { get; set; }
    public decimal Vat { get; set; }
    public decimal Total { get; set; }
    public bool IsMatched { get; set; }
    public string MatchBy { get; set; } = "";
}

public sealed class BillingOptionDto
{
    public int Value { get; set; }
    public string Label { get; set; } = "";
}

public sealed class BillingInvoiceSaveRequestDto
{
    public string RecordId { get; set; } = "";
    public string InvoiceNumber { get; set; } = "";
    public string ClientId { get; set; } = "";
    public string ClientName { get; set; } = "";
    public string CompanyTaxId { get; set; } = "";
    public int? VerticalOptionValue { get; set; }
    public int? ContractTypeOptionValue { get; set; }
    public string EmissionDateValue { get; set; } = "";
    public string DueDateValue { get; set; } = "";
    public string PaymentDateValue { get; set; } = "";
    public decimal TotalInvoice { get; set; }
    public decimal VatPercent { get; set; }
    public decimal VatValue { get; set; }
    public decimal PaymentValue { get; set; }
    public decimal ReteIcaValue { get; set; }
    public decimal RteIvaValue { get; set; }
    public decimal RteFteValue { get; set; }
    public decimal DifferenceValue { get; set; }
    public string PublicUrl { get; set; } = "";
}

public sealed class BillingInvoiceSaveResultDto
{
    public string Message { get; set; } = "";
    public BillingInvoiceRowDto Invoice { get; set; } = new();
}

public sealed class BillingInvoicesDeleteRequestDto
{
    public IReadOnlyList<string> RecordIds { get; set; } = Array.Empty<string>();
}

public sealed class BillingInvoicesDeleteResultDto
{
    public int DeletedCount { get; set; }
    public string Message { get; set; } = "";
}

public sealed class BillingInvoicesContractTypeUpdateRequestDto
{
    public IReadOnlyList<string> RecordIds { get; set; } = Array.Empty<string>();
    public int? ContractTypeOptionValue { get; set; }
}

public sealed class BillingInvoicesContractTypeUpdateResultDto
{
    public int UpdatedCount { get; set; }
    public string Message { get; set; } = "";
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
