using CotizadorInterno.Web.Models.Permissions;

namespace CotizadorInterno.Web.Models.Conciliacion;

public sealed class ConciliacionPageViewModel
{
    public CurrentUserInfo CurrentUser { get; set; } = new();
    public ConciliacionBoardDto Board { get; set; } = new();
}

public sealed class ConciliacionBoardDto
{
    public int Year { get; set; }
    public int Month { get; set; }
    public string PeriodLabel { get; set; } = "";
    public string StatusLabel { get; set; } = "";
    public string StatusTone { get; set; } = "";
    public int TotalPendingReview { get; set; }
    public int TotalSuggested { get; set; }
    public int TotalApproved { get; set; }
    public decimal ClientPaymentEntries { get; set; }
    public IReadOnlyList<ConciliacionPhaseDto> Phases { get; set; } = Array.Empty<ConciliacionPhaseDto>();
    public ConciliacionCashFlowSummaryDto CashFlow { get; set; } = new();
    public ConciliacionClientPaymentSummaryDto ClientPayments { get; set; } = new();
    public ConciliacionDianSupplierInvoiceSummaryDto DianSupplierInvoices { get; set; } = new();
    public IReadOnlyList<DeduccionesIvaImportHistoryEntryDto> DeduccionesIvaImports { get; set; } =
        Array.Empty<DeduccionesIvaImportHistoryEntryDto>();
    public string DeduccionesIvaHistoryError { get; set; } = "";
    public ConciliacionCuentaCobroSummaryDto CuentasCobro { get; set; } = new();
    public IReadOnlyList<ConciliacionOptionDto> DianCategoryOptions { get; set; } = Array.Empty<ConciliacionOptionDto>();
    public IReadOnlyList<ConciliacionOptionDto> DianExpenseAccountOptions { get; set; } = Array.Empty<ConciliacionOptionDto>();
    public IReadOnlyList<ConciliacionOptionDto> AccountingAccountOptions { get; set; } = Array.Empty<ConciliacionOptionDto>();
}

public sealed class ConciliacionOptionDto
{
    public string Value { get; set; } = "";
    public string Label { get; set; } = "";
}

public sealed class ConciliacionPhaseDto
{
    public string Key { get; set; } = "";
    public string Label { get; set; } = "";
    public string StatusLabel { get; set; } = "";
    public string StatusTone { get; set; } = "";
    public string CadenceLabel { get; set; } = "";
    public string LastRunLabel { get; set; } = "";
    public string RunSummary { get; set; } = "";
    public string NextStep { get; set; } = "";
    public IReadOnlyList<string> ReadyItems { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> MissingItems { get; set; } = Array.Empty<string>();
    public IReadOnlyList<ConciliacionFlowStepDto> Steps { get; set; } = Array.Empty<ConciliacionFlowStepDto>();
}

public sealed class ConciliacionFlowStepDto
{
    public string Label { get; set; } = "";
    public string StatusLabel { get; set; } = "";
    public string StatusTone { get; set; } = "";
    public string Summary { get; set; } = "";
}

public sealed class ConciliacionCashFlowSummaryDto
{
    public int TotalRows { get; set; }
    public int MovementRows { get; set; }
    public int TransferRows { get; set; }
    public int EntryRows { get; set; }
    public int ExitRows { get; set; }
    public int OutgoingInvoiceRows { get; set; }
    public int IncomingInvoiceRows { get; set; }
    public int CollectionAccountRows { get; set; }
    public int AccountingVoucherRows { get; set; }
    public int OrphanRows { get; set; }
    public int PendingValidationRows { get; set; }
    public int PendingSiigoRows { get; set; }
    public decimal TotalEntries { get; set; }
    public decimal TotalExits { get; set; }
    public decimal TotalTransfers { get; set; }
    public string LastRunLabel { get; set; } = "";
    public IReadOnlyList<ConciliacionCashFlowBankSummaryDto> BankSummaries { get; set; } = Array.Empty<ConciliacionCashFlowBankSummaryDto>();
    public IReadOnlyList<ConciliacionBankBalanceDto> BankBalances { get; set; } = Array.Empty<ConciliacionBankBalanceDto>();
    public IReadOnlyList<ConciliacionCashFlowComparisonRowDto> MonthCloseComparisons { get; set; } = Array.Empty<ConciliacionCashFlowComparisonRowDto>();
    public IReadOnlyList<string> MonthCloseIssues { get; set; } = Array.Empty<string>();
    public bool CanValidateMonth { get; set; }
    public bool MonthValidated { get; set; }
    public string MonthValidationLabel { get; set; } = "";
    public string MonthValidationTone { get; set; } = "";
    public string MonthValidationDetail { get; set; } = "";
    public IReadOnlyList<ConciliacionAccountingVoucherGroupDto> AccountingVoucherGroups { get; set; } = Array.Empty<ConciliacionAccountingVoucherGroupDto>();
    public IReadOnlyList<ConciliacionCashFlowRowDto> Rows { get; set; } = Array.Empty<ConciliacionCashFlowRowDto>();
}

public sealed class ConciliacionCashFlowComparisonRowDto
{
    public string Concept { get; set; } = "";
    public bool ShowSiigo { get; set; }
    public decimal SiigoValue { get; set; }
    public bool ShowDataverse { get; set; }
    public decimal DataverseValue { get; set; }
    public bool ShowCashFlow { get; set; }
    public decimal CashFlowValue { get; set; }
    public decimal DifferenceValue { get; set; }
    public string DifferenceLabel { get; set; } = "";
    public string StatusLabel { get; set; } = "";
    public string StatusTone { get; set; } = "";
    public string Detail { get; set; } = "";
}

public sealed class ConciliacionCashFlowBankSummaryDto
{
    public string BankKey { get; set; } = "";
    public string BankLabel { get; set; } = "";
    public int RowsFound { get; set; }
    public int ReportedToSiigo { get; set; }
    public int PendingConciliation { get; set; }
    public decimal TotalEntries { get; set; }
    public decimal TotalExits { get; set; }
}

public sealed class ConciliacionBankBalanceDto
{
    public string BankKey { get; set; } = "";
    public string BankLabel { get; set; } = "";
    public string SourceFlow { get; set; } = "";
    public string BankAccountCode { get; set; } = "";
    public string BankAccountName { get; set; } = "";
    public int Year { get; set; }
    public int Month { get; set; }
    public string PeriodKey { get; set; } = "";
    public bool HasOpeningBalance { get; set; }
    public decimal OpeningBalance { get; set; }
    public decimal TotalEntries { get; set; }
    public decimal TotalExits { get; set; }
    public decimal CurrentBalance { get; set; }
}

public sealed class ConciliacionCashFlowRowDto
{
    public string RecordId { get; set; } = "";
    public string SourceKind { get; set; } = "";
    public string SourceKindLabel { get; set; } = "";
    public string MovementDateValue { get; set; } = "";
    public string MovementDateDisplay { get; set; } = "";
    public string SourceFlow { get; set; } = "";
    public string BankAccountCode { get; set; } = "";
    public string BankAccountName { get; set; } = "";
    public string Direction { get; set; } = "";
    public string DirectionTone { get; set; } = "";
    public decimal EntryValue { get; set; }
    public decimal ExitValue { get; set; }
    public decimal Amount { get; set; }
    public string Description { get; set; } = "";
    public string Recipient { get; set; } = "";
    public string DestinationBank { get; set; } = "";
    public string DocumentType { get; set; } = "";
    public string Observations { get; set; } = "";
    public string ExcelMovementType { get; set; } = "";
    public int SourceRowNumber { get; set; }
    public string DetectedTypeKey { get; set; } = "";
    public string DetectedTypeLabel { get; set; } = "";
    public string DetectedTypeTone { get; set; } = "";
    public string ValidationStatus { get; set; } = "";
    public string ValidationTone { get; set; } = "";
    public string RegistrationStatus { get; set; } = "";
    public string RegistrationTone { get; set; } = "";
    public string DataverseStatus { get; set; } = "";
    public string ReviewReason { get; set; } = "";
    public string SiigoSupplierId { get; set; } = "";
    public string SiigoSupplierName { get; set; } = "";
    public string SiigoStatus { get; set; } = "";
    public string SiigoDocumentId { get; set; } = "";
    public string SiigoDocumentName { get; set; } = "";
    public string AccountCode { get; set; } = "";
    public string AccountName { get; set; } = "";
    public string ThirdPartyId { get; set; } = "";
    public string ThirdPartyIdentification { get; set; } = "";
    public string ThirdPartyName { get; set; } = "";
    public int ThirdPartyBranchOffice { get; set; }
    public string InvoiceStatus { get; set; } = "";
    public string InvoiceStatusTone { get; set; } = "";
    public string SiigoDocumentStatus { get; set; } = "";
    public string SiigoDocumentTone { get; set; } = "";
    public string SiigoPaymentStatus { get; set; } = "";
    public string SiigoPaymentTone { get; set; } = "";
    public string InvoiceBalanceStatus { get; set; } = "";
    public string DataversePaymentStatus { get; set; } = "";
    public string DataversePaymentTone { get; set; } = "";
    public string ExternalKey { get; set; } = "";
    public string DetailUrl { get; set; } = "";
    public string MatchRecordId { get; set; } = "";
    public string MatchStatus { get; set; } = "";
    public string ModifiedOnValue { get; set; } = "";
    public bool CanValidate { get; set; }
    public string ActionTargetKey { get; set; } = "";
}

public sealed class ConciliacionAccountingVoucherGroupDto
{
    public string GroupKey { get; set; } = "";
    public string GroupKind { get; set; } = "";
    public string GroupLabel { get; set; } = "";
    public string GroupDetail { get; set; } = "";
    public string MovementDateDisplay { get; set; } = "";
    public string SourceFlow { get; set; } = "";
    public string BankAccountCode { get; set; } = "";
    public string BankAccountName { get; set; } = "";
    public string Direction { get; set; } = "";
    public string DirectionTone { get; set; } = "";
    public decimal EntryValue { get; set; }
    public decimal ExitValue { get; set; }
    public decimal Amount { get; set; }
    public bool IsMonthlyCloseGroup { get; set; }
    public bool IsGrouped { get; set; }
    public int RowCount { get; set; }
    public bool HasMissingAccounts { get; set; }
    public IReadOnlyList<string> RecordIds { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> MovementExternalKeys { get; set; } = Array.Empty<string>();
    public IReadOnlyList<ConciliacionAccountingVoucherAccountLineDto> AccountLines { get; set; } = Array.Empty<ConciliacionAccountingVoucherAccountLineDto>();
    public IReadOnlyList<ConciliacionCashFlowRowDto> Rows { get; set; } = Array.Empty<ConciliacionCashFlowRowDto>();
}

public sealed class ConciliacionAccountingVoucherAccountLineDto
{
    public string ConceptKey { get; set; } = "";
    public string ConceptLabel { get; set; } = "";
    public string AccountCode { get; set; } = "";
    public string AccountName { get; set; } = "";
    public decimal Amount { get; set; }
    public int RowCount { get; set; }
    public bool HasAccount { get; set; }
}

public sealed class ConciliacionClientPaymentSummaryDto
{
    public int TotalRows { get; set; }
    public int Suggested { get; set; }
    public int Approved { get; set; }
    public int ReadyForSiigo { get; set; }
    public int PreflightOk { get; set; }
    public int PreflightBlocked { get; set; }
    public int Rejected { get; set; }
    public int PendingReview { get; set; }
    public int DifferenceOutOfTolerance { get; set; }
    public int NoInvoiceToken { get; set; }
    public int NoInvoiceMatch { get; set; }
    public int AmbiguousInvoice { get; set; }
    public decimal TotalEntries { get; set; }
    public decimal SuggestedEntries { get; set; }
    public decimal ReadyForSiigoEntries { get; set; }
    public decimal PendingReviewEntries { get; set; }
    public string LastRunLabel { get; set; } = "";
    public IReadOnlyList<ConciliacionClientPaymentRowDto> Rows { get; set; } = Array.Empty<ConciliacionClientPaymentRowDto>();
}

public sealed class ConciliacionClientPaymentRowDto
{
    public string RecordId { get; set; } = "";
    public string Status { get; set; } = "";
    public string StatusLabel { get; set; } = "";
    public string StatusTone { get; set; } = "";
    public int Confidence { get; set; }
    public string Reason { get; set; } = "";
    public string MovementId { get; set; } = "";
    public string MovementExternalKey { get; set; } = "";
    public int SourceRowNumber { get; set; }
    public string MovementDateValue { get; set; } = "";
    public string MovementDateDisplay { get; set; } = "";
    public string SourceFlow { get; set; } = "";
    public string BankAccountCode { get; set; } = "";
    public string BankAccountName { get; set; } = "";
    public string Description { get; set; } = "";
    public string InvoiceRecordIds { get; set; } = "";
    public string InvoiceNumbers { get; set; } = "";
    public string ClientNames { get; set; } = "";
    public decimal EntryValue { get; set; }
    public decimal InvoiceTotal { get; set; }
    public decimal PaymentValue { get; set; }
    public decimal ReteFuenteValue { get; set; }
    public decimal ReteIcaValue { get; set; }
    public decimal RteIvaValue { get; set; }
    public decimal RetentionsTotal { get; set; }
    public decimal DifferenceValue { get; set; }
    public string DraftJson { get; set; } = "";
    public string PreflightStatus { get; set; } = "";
    public string PreflightStatusLabel { get; set; } = "";
    public string PreflightStatusTone { get; set; } = "";
    public string PreflightMessage { get; set; } = "";
    public decimal PreflightDebitTotal { get; set; }
    public decimal PreflightCreditTotal { get; set; }
    public string PreflightValidatedOnDisplay { get; set; } = "";
    public string ModifiedOnDisplay { get; set; } = "";
}

public sealed class ConciliacionDianSupplierInvoiceSummaryDto
{
    public int TotalRows { get; set; }
    public int ProviderPending { get; set; }
    public int ClassificationPending { get; set; }
    public int ReadyForPurchase { get; set; }
    public int SentToSiigo { get; set; }
    public int WithErrors { get; set; }
    public decimal TotalValue { get; set; }
    public string LastRunLabel { get; set; } = "";
    public IReadOnlyList<ConciliacionDianSupplierInvoiceRowDto> Rows { get; set; } = Array.Empty<ConciliacionDianSupplierInvoiceRowDto>();
}

public sealed class ConciliacionDianSupplierInvoiceRowDto
{
    public string RecordId { get; set; } = "";
    public string Stage { get; set; } = "";
    public string StageLabel { get; set; } = "";
    public string StageTone { get; set; } = "";
    public string DocumentType { get; set; } = "";
    public string Prefix { get; set; } = "";
    public string Folio { get; set; } = "";
    public string InvoiceNumber { get; set; } = "";
    public string Cufe { get; set; } = "";
    public string EmissionDateValue { get; set; } = "";
    public string EmissionDateDisplay { get; set; } = "";
    public string ReceptionDateValue { get; set; } = "";
    public string ReceptionDateDisplay { get; set; } = "";
    public string DianStatus { get; set; } = "";
    public string DianGroup { get; set; } = "";
    public string PaymentForm { get; set; } = "";
    public string PaymentMethod { get; set; } = "";
    public string Currency { get; set; } = "";
    public string SupplierNit { get; set; } = "";
    public string SupplierName { get; set; } = "";
    public string RecipientNit { get; set; } = "";
    public string RecipientName { get; set; } = "";
    public decimal BaseAmount { get; set; }
    public decimal VatValue { get; set; }
    public decimal ReteFuenteValue { get; set; }
    public decimal ReteIcaValue { get; set; }
    public decimal ReteIvaValue { get; set; }
    public decimal TotalValue { get; set; }
    public decimal PaymentValue { get; set; }
    public decimal CloudValue { get; set; }
    public decimal CopiersValue { get; set; }
    public string VerticalLabel { get; set; } = "";
    public string CategoryValue { get; set; } = "";
    public string CategoryLabel { get; set; } = "";
    public string AccountCode { get; set; } = "";
    public string AccountName { get; set; } = "";
    public string AutomationState { get; set; } = "";
    public string ReviewReason { get; set; } = "";
    public string SiigoSupplierId { get; set; } = "";
    public string SiigoSupplierName { get; set; } = "";
    public string SiigoDocumentId { get; set; } = "";
    public string SiigoDocumentName { get; set; } = "";
    public string ProviderStatusLabel { get; set; } = "";
    public string ProviderStatusTone { get; set; } = "";
    public string ClassificationStatusLabel { get; set; } = "";
    public string ClassificationStatusTone { get; set; } = "";
    public string SiigoStatusLabel { get; set; } = "";
    public string SiigoStatusTone { get; set; } = "";
    public string AutomationSource { get; set; } = "";
    public string ExcelKey { get; set; } = "";
    public string SiigoBusinessKey { get; set; } = "";
    public string SourceLabel { get; set; } = "";
    public int SourceRowNumber { get; set; }
    public string ConcurrencyToken { get; set; } = "";
    public DateTimeOffset? CreatedAt { get; set; }
    public DateTimeOffset? ModifiedAt { get; set; }
    public string ModifiedOnDisplay { get; set; } = "";
}

public sealed class ConciliacionCuentaCobroSummaryDto
{
    public int TotalRows { get; set; }
    public int DetectedCashFlowRows { get; set; }
    public int MatchedRows { get; set; }
    public int PendingRows { get; set; }
    public int ReadyForSiigo { get; set; }
    public int SentToSiigo { get; set; }
    public int WithErrors { get; set; }
    public decimal TotalPaidValue { get; set; }
    public decimal TotalGrossValue { get; set; }
    public decimal TotalReteFuenteValue { get; set; }
    public decimal TotalRetentionsValue { get; set; }
    public string LastRunLabel { get; set; } = "";
    public IReadOnlyList<ConciliacionCuentaCobroRowDto> Rows { get; set; } = Array.Empty<ConciliacionCuentaCobroRowDto>();
}

public sealed class ConciliacionCuentaCobroRowDto
{
    public string RecordId { get; set; } = "";
    public string RecordSource { get; set; } = "";
    public string ConcurrencyToken { get; set; } = "";
    public string CashFlowRecordId { get; set; } = "";
    public string CashFlowExternalKey { get; set; } = "";
    public int SourceRowNumber { get; set; }
    public string Stage { get; set; } = "";
    public string StageLabel { get; set; } = "";
    public string StageTone { get; set; } = "";
    public string SourceFlow { get; set; } = "";
    public string BankAccountCode { get; set; } = "";
    public string BankAccountName { get; set; } = "";
    public string MovementDateValue { get; set; } = "";
    public string MovementDateDisplay { get; set; } = "";
    public string CashFlowDescription { get; set; } = "";
    public string CashFlowRecipient { get; set; } = "";
    public string CashFlowDocumentType { get; set; } = "";
    public string CashFlowObservations { get; set; } = "";
    public decimal CashFlowExitValue { get; set; }
    public string Receptor { get; set; } = "";
    public string NitOCedula { get; set; } = "";
    public string Observaciones { get; set; } = "";
    public string FechaEmisionValue { get; set; } = "";
    public string FechaEmisionDisplay { get; set; } = "";
    public string FechaPagoValue { get; set; } = "";
    public string FechaPagoDisplay { get; set; } = "";
    public decimal ValorTotal { get; set; }
    public decimal ValorIva { get; set; }
    public decimal ValorPago { get; set; }
    public decimal CloudValue { get; set; }
    public decimal CopiersValue { get; set; }
    public string CategoryValue { get; set; } = "";
    public string CategoryLabel { get; set; } = "";
    public decimal ReteFuentePorcentaje { get; set; }
    public decimal ReteFuenteValor { get; set; }
    public IReadOnlyList<ConciliacionCuentaCobroRetentionDto> Retentions { get; set; } = Array.Empty<ConciliacionCuentaCobroRetentionDto>();
    public decimal DifferenceValue { get; set; }
    public int MatchScore { get; set; }
    public string MatchLabel { get; set; } = "";
    public string MatchTone { get; set; } = "";
    public bool TotalesCuadran { get; set; }
    public string AccountCode { get; set; } = "";
    public string AccountName { get; set; } = "";
    public string AutomationState { get; set; } = "";
    public string ReviewReason { get; set; } = "";
    public string SiigoSupplierId { get; set; } = "";
    public string SiigoSupplierName { get; set; } = "";
    public string SiigoDocumentId { get; set; } = "";
    public string SiigoDocumentName { get; set; } = "";
    public string SiigoPaymentId { get; set; } = "";
    public string SiigoPaymentName { get; set; } = "";
    public string SiigoStatusLabel { get; set; } = "";
    public string SiigoStatusTone { get; set; } = "";
    public string SiigoPaymentStatusLabel { get; set; } = "";
    public string SiigoPaymentStatusTone { get; set; } = "";
    public string ModifiedOnDisplay { get; set; } = "";
}

public sealed class ConciliacionCuentaCobroRetentionDto
{
    public string Kind { get; set; } = "";
    public string Label { get; set; } = "";
    public int TaxId { get; set; }
    public string AccountCode { get; set; } = "";
    public decimal BaseValue { get; set; }
    public decimal Rate { get; set; }
    public decimal Value { get; set; }
}

public sealed class ConciliacionClientPaymentStatusRequest
{
    public string RecordId { get; set; } = "";
    public string Status { get; set; } = "";
    public string Reason { get; set; } = "";
}

public sealed class ConciliacionCashFlowCategoryRequest
{
    public string RecordId { get; set; } = "";
    public string SourceKind { get; set; } = "";
    public string MovementExternalKey { get; set; } = "";
    public string ClientPaymentRecordId { get; set; } = "";
    public string CategoryValue { get; set; } = "";
    public string Reason { get; set; } = "";
}

public sealed class ConciliacionBankOpeningBalanceRequest
{
    public int Year { get; set; }
    public int Month { get; set; }
    public string BankKey { get; set; } = "";
    public decimal OpeningBalance { get; set; }
}

public sealed class ConciliacionBankOpeningBalanceResultDto
{
    public string Message { get; set; } = "";
    public ConciliacionBankBalanceDto Balance { get; set; } = new();
}

public sealed class ConciliacionCashFlowCategoryResultDto
{
    public string Message { get; set; } = "";
    public string CategoryValue { get; set; } = "";
    public string CategoryLabel { get; set; } = "";
    public string CategoryTone { get; set; } = "";
}

public sealed class ConciliacionCashFlowDescriptionRequest
{
    public string RecordId { get; set; } = "";
    public string SourceKind { get; set; } = "";
    public string MovementExternalKey { get; set; } = "";
    public string Description { get; set; } = "";
}

public sealed class ConciliacionCashFlowPendingRequest
{
    public string RecordId { get; set; } = "";
    public string SourceKind { get; set; } = "";
    public string MovementExternalKey { get; set; } = "";
    public string Reason { get; set; } = "";
}

public sealed class ConciliacionCashFlowDescriptionResultDto
{
    public string Message { get; set; } = "";
    public string Description { get; set; } = "";
    public ConciliacionCashFlowRowDto? Row { get; set; }
}

public sealed class ConciliacionCashFlowManualRequest
{
    public string RecordId { get; set; } = "";
    public string SourceKind { get; set; } = "";
    public string MovementExternalKey { get; set; } = "";
    public string ClientPaymentRecordId { get; set; } = "";
    public string Reason { get; set; } = "";
}

public sealed class ConciliacionCashFlowAccountingAccountRequest
{
    public string RecordId { get; set; } = "";
    public List<string> RecordIds { get; set; } = new();
    public string SourceKind { get; set; } = "";
    public string MovementExternalKey { get; set; } = "";
    public List<string> MovementExternalKeys { get; set; } = new();
    public string AccountCode { get; set; } = "";
    public string ThirdPartyId { get; set; } = "";
    public string ThirdPartyIdentification { get; set; } = "";
    public string ThirdPartyName { get; set; } = "";
    public int ThirdPartyBranchOffice { get; set; }
}

public sealed class ConciliacionCashFlowAccountingVoucherRequest
{
    public string RecordId { get; set; } = "";
    public List<string> RecordIds { get; set; } = new();
    public string SourceKind { get; set; } = "";
    public string MovementExternalKey { get; set; } = "";
    public List<string> MovementExternalKeys { get; set; } = new();
    public string GroupKey { get; set; } = "";
    public string GroupLabel { get; set; } = "";
    public string ThirdPartyId { get; set; } = "";
    public string ThirdPartyIdentification { get; set; } = "";
    public string ThirdPartyName { get; set; } = "";
    public int ThirdPartyBranchOffice { get; set; }
}

public sealed class ConciliacionCashFlowActionResultDto
{
    public string Message { get; set; } = "";
    public bool IsSuccess { get; set; }
    public bool IsReadyForSiigo { get; set; }
    public bool DataverseChangesSucceeded { get; set; }
    public bool SiigoSucceeded { get; set; }
    public bool DataverseReconciliationSucceeded { get; set; }
    public string TargetEndpoint { get; set; } = "";
    public string PayloadJson { get; set; } = "";
    public string ResponseJson { get; set; } = "";
    public string SiigoId { get; set; } = "";
    public string SiigoName { get; set; } = "";
    public IReadOnlyList<string> Issues { get; set; } = Array.Empty<string>();
    public ConciliacionCashFlowRowDto? Row { get; set; }
}

public sealed class ConciliacionSiigoSupplierLookupDto
{
    public string Id { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Name { get; set; } = "";
    public string CommercialName { get; set; } = "";
    public string Identification { get; set; } = "";
    public string Type { get; set; } = "";
    public int BranchOffice { get; set; }
    public bool Active { get; set; }
}

public sealed class ConciliacionSiigoOpenPurchaseDto
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string DateValue { get; set; } = "";
    public string DateDisplay { get; set; } = "";
    public string SupplierIdentification { get; set; } = "";
    public int SupplierBranchOffice { get; set; }
    public string ProviderInvoicePrefix { get; set; } = "";
    public string ProviderInvoiceNumber { get; set; } = "";
    public string ProviderInvoiceFullNumber { get; set; } = "";
    public decimal Total { get; set; }
    public decimal Balance { get; set; }
    public string DataverseRecordId { get; set; } = "";
    public string DataverseInvoiceNumber { get; set; } = "";
    public string DataverseSupplierName { get; set; } = "";
    public string DataverseSupplierNit { get; set; } = "";
    public decimal DataverseTotal { get; set; }
    public decimal DataversePaymentValue { get; set; }
    public decimal DataverseReteFuenteValue { get; set; }
    public decimal DataverseReteIcaValue { get; set; }
    public string DataverseCufeCude { get; set; } = "";
    public decimal DataverseBaseAmount { get; set; }
    public decimal DataverseCloudValue { get; set; }
    public decimal DataverseCopiersValue { get; set; }
    public string DataverseCategoryValue { get; set; } = "";
    public string DataverseCategoryLabel { get; set; } = "";
    public string DataverseMatchLabel { get; set; } = "";
    public string DataverseMatchTone { get; set; } = "";
    public int MatchScore { get; set; }
}

public sealed class ConciliacionSiigoOpenPurchaseSearchResultDto
{
    public string Message { get; set; } = "";
    public ConciliacionSiigoSupplierLookupDto? Supplier { get; set; }
    public IReadOnlyList<ConciliacionSiigoSupplierLookupDto> SupplierCandidates { get; set; } = Array.Empty<ConciliacionSiigoSupplierLookupDto>();
    public IReadOnlyList<ConciliacionSiigoOpenPurchaseDto> Purchases { get; set; } = Array.Empty<ConciliacionSiigoOpenPurchaseDto>();
    public IReadOnlyList<ConciliacionSiigoRetentionOptionDto> ReteFuenteOptions { get; set; } = Array.Empty<ConciliacionSiigoRetentionOptionDto>();
    public IReadOnlyList<ConciliacionSiigoRetentionOptionDto> ReteIcaOptions { get; set; } = Array.Empty<ConciliacionSiigoRetentionOptionDto>();
}

public sealed class ConciliacionSiigoOpenInvoiceDto
{
    public string Id { get; set; } = "";
    public string DataverseRecordId { get; set; } = "";
    public string Name { get; set; } = "";
    public string Prefix { get; set; } = "";
    public long? Number { get; set; }
    public string DateValue { get; set; } = "";
    public string DateDisplay { get; set; } = "";
    public string CustomerId { get; set; } = "";
    public string CustomerName { get; set; } = "";
    public string CustomerIdentification { get; set; } = "";
    public int CustomerBranchOffice { get; set; }
    public decimal Total { get; set; }
    public decimal Vat { get; set; }
    public decimal TaxBase { get; set; }
    public decimal Balance { get; set; }
    public decimal SiigoBalance { get; set; }
    public string DuePrefix { get; set; } = "";
    public int DueConsecutive { get; set; }
    public int DueQuote { get; set; }
    public string DueDateValue { get; set; } = "";
    public string DueDateDisplay { get; set; } = "";
    public bool HasExactDueReference { get; set; }
    public string DueReferenceIssue { get; set; } = "";
}

public sealed class ConciliacionSiigoPaidInvoiceDto
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string InvoiceDateDisplay { get; set; } = "";
    public string PaymentDateValue { get; set; } = "";
    public string PaymentDateDisplay { get; set; } = "";
    public decimal Total { get; set; }
    public decimal PaymentValue { get; set; }
    public decimal ReteFuenteRate { get; set; }
    public decimal ReteFuenteValue { get; set; }
    public decimal ReteIcaRate { get; set; }
    public decimal ReteIcaValue { get; set; }
    public decimal RteIvaRate { get; set; }
    public decimal RteIvaValue { get; set; }
}

public sealed class ConciliacionSiigoRetentionOptionDto
{
    public int TaxId { get; set; }
    public string Kind { get; set; } = "";
    public string Name { get; set; } = "";
    public decimal Rate { get; set; }
    public string RateLabel { get; set; } = "";
}

public sealed class ConciliacionSiigoOpenInvoiceSearchResultDto
{
    public string Message { get; set; } = "";
    public ConciliacionSiigoSupplierLookupDto? Customer { get; set; }
    public IReadOnlyList<ConciliacionSiigoOpenInvoiceDto> Invoices { get; set; } = Array.Empty<ConciliacionSiigoOpenInvoiceDto>();
    public IReadOnlyList<ConciliacionSiigoPaidInvoiceDto> PaidInvoices { get; set; } = Array.Empty<ConciliacionSiigoPaidInvoiceDto>();
    public IReadOnlyList<ConciliacionSiigoRetentionOptionDto> ReteFuenteOptions { get; set; } = Array.Empty<ConciliacionSiigoRetentionOptionDto>();
    public IReadOnlyList<ConciliacionSiigoRetentionOptionDto> ReteIcaOptions { get; set; } = Array.Empty<ConciliacionSiigoRetentionOptionDto>();
    public IReadOnlyList<ConciliacionSiigoRetentionOptionDto> RteIvaOptions { get; set; } = Array.Empty<ConciliacionSiigoRetentionOptionDto>();
}

public sealed class ConciliacionSiigoSupplierSearchRequest
{
    public string Query { get; set; } = "";
    public int Top { get; set; } = 12;
}

public sealed class ConciliacionSupplierPaymentPurchaseSearchRequest
{
    public string RecordId { get; set; } = "";
    public string MovementExternalKey { get; set; } = "";
    public string SupplierId { get; set; } = "";
    public string SupplierQuery { get; set; } = "";
    public int LookbackMonths { get; set; } = 36;
}

public sealed class ConciliacionClientPaymentInvoiceSearchRequest
{
    public string RecordId { get; set; } = "";
    public string MovementExternalKey { get; set; } = "";
    public string CustomerId { get; set; } = "";
    public string CustomerQuery { get; set; } = "";
    public int LookbackMonths { get; set; } = 60;
}

public sealed class ConciliacionPaymentAllocationRequest
{
    public string DocumentId { get; set; } = "";
    public string DocumentName { get; set; } = "";
    public string DataverseRecordId { get; set; } = "";
    public string CustomerId { get; set; } = "";
    public string CustomerIdentification { get; set; } = "";
    public string CustomerName { get; set; } = "";
    public int CustomerBranchOffice { get; set; }
    public decimal AppliedValue { get; set; }
    public int ReteFuenteTaxId { get; set; }
    public int ReteIcaTaxId { get; set; }
    public int RteIvaTaxId { get; set; }
}

public sealed class ConciliacionSupplierPaymentAllocationRequest
{
    public string DocumentId { get; set; } = "";
    public string DocumentName { get; set; } = "";
    public string DataverseRecordId { get; set; } = "";
    public string DataverseInvoiceNumber { get; set; } = "";
    public string CufeCude { get; set; } = "";
    public decimal AppliedValue { get; set; }
    public decimal CloudValue { get; set; }
    public decimal CopiersValue { get; set; }
    public string CategoryValue { get; set; } = "";
    public int ReteFuenteTaxId { get; set; }
    public int ReteIcaTaxId { get; set; }
}

public sealed class ConciliacionSupplierExpenseAllocationRequest
{
    public string RecordId { get; set; } = "";
    public string InvoiceNumber { get; set; } = "";
    public string CufeCude { get; set; } = "";
    public decimal PaymentValue { get; set; }
    public decimal CloudValue { get; set; }
    public decimal CopiersValue { get; set; }
    public string CategoryValue { get; set; } = "";
}

public sealed class ConciliacionClientInvoicePaymentApplyRequest
{
    public string MatchRecordId { get; set; } = "";
    public string RecordId { get; set; } = "";
    public string MovementExternalKey { get; set; } = "";
    public string CustomerId { get; set; } = "";
    public string CustomerIdentification { get; set; } = "";
    public string CustomerName { get; set; } = "";
    public ConciliacionPaymentAllocationRequest? Allocation { get; set; }
    public List<ConciliacionPaymentAllocationRequest> Allocations { get; set; } = new();
}

public sealed class ConciliacionClientPaymentDataverseSnapshotRequest
{
    public string MatchRecordId { get; set; } = "";
    public string MovementRecordId { get; set; } = "";
    public string MovementExternalKey { get; set; } = "";
    public string InvoiceRecordIds { get; set; } = "";
    public string InvoiceNumbers { get; set; } = "";
    public string ClientNames { get; set; } = "";
    public decimal InvoiceTotal { get; set; }
    public decimal PaymentValue { get; set; }
    public decimal ReteFuenteValue { get; set; }
    public decimal ReteIcaValue { get; set; }
    public decimal RteIvaValue { get; set; }
    public decimal DifferenceValue { get; set; }
    public string SnapshotJson { get; set; } = "";
}

public sealed class ConciliacionClientInvoicePaymentApplyResultDto
{
    public string Message { get; set; } = "";
    public bool IsSuccess { get; set; }
    public int SavedCount { get; set; }
    public string MatchRecordId { get; set; } = "";
    public IReadOnlyList<ConciliacionClientInvoicePaymentApplyItemDto> Items { get; set; } =
        Array.Empty<ConciliacionClientInvoicePaymentApplyItemDto>();
    public string DataverseRecordId { get; set; } = "";
    public string InvoiceNumber { get; set; } = "";
    public string PaymentDateValue { get; set; } = "";
    public decimal InvoiceTotal { get; set; }
    public decimal TaxBase { get; set; }
    public decimal PaymentValue { get; set; }
    public decimal ReteFuenteRate { get; set; }
    public decimal ReteFuenteValue { get; set; }
    public decimal ReteIcaRate { get; set; }
    public decimal ReteIcaValue { get; set; }
    public decimal RteIvaRate { get; set; }
    public decimal RteIvaValue { get; set; }
    public decimal AdjustmentValue { get; set; }
    public decimal FinalBalance { get; set; }
}

public sealed class ConciliacionClientInvoicePaymentApplyItemDto
{
    public string DocumentId { get; set; } = "";
    public string DataverseRecordId { get; set; } = "";
    public string InvoiceNumber { get; set; } = "";
    public string CustomerId { get; set; } = "";
    public string CustomerIdentification { get; set; } = "";
    public string CustomerName { get; set; } = "";
    public decimal PaymentValue { get; set; }
}

public sealed class ConciliacionClientInvoicePaymentSendRequest
{
    public string MatchRecordId { get; set; } = "";
    public string RecordId { get; set; } = "";
    public string MovementExternalKey { get; set; } = "";
    public string CustomerId { get; set; } = "";
    public string CustomerIdentification { get; set; } = "";
    public string CustomerName { get; set; } = "";
    public List<ConciliacionPaymentAllocationRequest> Allocations { get; set; } = new();
}

public sealed class ConciliacionSupplierPaymentSendRequest
{
    public string RecordId { get; set; } = "";
    public string MovementExternalKey { get; set; } = "";
    public string SupplierId { get; set; } = "";
    public string SupplierIdentification { get; set; } = "";
    public string SupplierName { get; set; } = "";
    public string PurchaseId { get; set; } = "";
    public string PurchaseName { get; set; } = "";
    public decimal ReteFuenteValue { get; set; }
    public decimal ReteFuenteRate { get; set; }
    public decimal ReteIcaValue { get; set; }
    public decimal ReteIcaRate { get; set; }
    public List<ConciliacionSupplierPaymentAllocationRequest> Allocations { get; set; } = new();
}

public sealed class ConciliacionMonthValidationRequest
{
    public int Year { get; set; }
    public int Month { get; set; }
    public string Comments { get; set; } = "";
}

public sealed class ConciliacionMonthValidationStateDto
{
    public bool IsValidated { get; set; }
    public string TaskId { get; set; } = "";
    public string ValidatedOnDisplay { get; set; } = "";
    public string ValidatedBy { get; set; } = "";
    public string Comments { get; set; } = "";
}

public sealed class ConciliacionMonthValidationResultDto
{
    public string Message { get; set; } = "";
    public ConciliacionMonthValidationStateDto State { get; set; } = new();
}

public sealed class ConciliacionInvoiceSearchRequest
{
    public string Query { get; set; } = "";
    public decimal? Value { get; set; }
    public int Top { get; set; } = 20;
}

public sealed class ConciliacionInvoiceSearchResultDto
{
    public string Message { get; set; } = "";
    public IReadOnlyList<ConciliacionInvoiceLookupDto> Items { get; set; } = Array.Empty<ConciliacionInvoiceLookupDto>();
}

public sealed class ConciliacionInvoiceLookupDto
{
    public string RecordId { get; set; } = "";
    public string InvoiceNumber { get; set; } = "";
    public string ClientName { get; set; } = "";
    public string EmissionDateDisplay { get; set; } = "";
    public decimal TotalInvoice { get; set; }
    public decimal PaymentValue { get; set; }
    public decimal ReteFteValue { get; set; }
    public decimal ReteIcaValue { get; set; }
    public decimal RteIvaValue { get; set; }
    public decimal DifferenceWithEntry { get; set; }
}

public sealed class ConciliacionAssignInvoiceRequest
{
    public string RecordId { get; set; } = "";
    public string InvoiceRecordId { get; set; } = "";
    public List<string> InvoiceRecordIds { get; set; } = new();
}

public sealed class ConciliacionDianClassificationRequest
{
    public string RecordId { get; set; } = "";
    public int? CategoryValue { get; set; }
    public string AccountCode { get; set; } = "";
}

public sealed class ConciliacionDianSupplierDocumentRequest
{
    public string RecordId { get; set; } = "";
    public int Year { get; set; }
    public int Month { get; set; }
    public string SupplierName { get; set; } = "";
    public string SupplierNit { get; set; } = "";
    public string PersonType { get; set; } = "";
    public string IdType { get; set; } = "";
    public string CheckDigit { get; set; } = "";
    public bool? VatResponsible { get; set; }
    public string FiscalResponsibilityCode { get; set; } = "";
    public string Address { get; set; } = "";
    public string CountryCode { get; set; } = "";
    public string StateCode { get; set; } = "";
    public string CityCode { get; set; } = "";
}

public sealed class ConciliacionDianPeriodRequest
{
    public int Year { get; set; }
    public int Month { get; set; }
    public IReadOnlyList<string> Periods { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> ExternalKeys { get; set; } = Array.Empty<string>();
}

public sealed class ConciliacionCuentaCobroClassificationRequest
{
    public string RecordId { get; set; } = "";
    public string RecordSource { get; set; } = "";
    public string ConcurrencyToken { get; set; } = "";
    public string AccountCode { get; set; } = "";
}

public sealed class ConciliacionCuentaCobroDocumentRequest
{
    public string RecordId { get; set; } = "";
    public string RecordSource { get; set; } = "";
    public string ConcurrencyToken { get; set; } = "";
    public string CashFlowRecordId { get; set; } = "";
    public string CashFlowExternalKey { get; set; } = "";
}

public sealed class ConciliacionCuentaCobroExpenseSaveRequest
{
    public string RecordId { get; set; } = "";
    public string RecordSource { get; set; } = "";
    public string ConcurrencyToken { get; set; } = "";
    public string CashFlowRecordId { get; set; } = "";
    public string CashFlowExternalKey { get; set; } = "";
    public string Receptor { get; set; } = "";
    public string NitOCedula { get; set; } = "";
    public string Observaciones { get; set; } = "";
    public string FechaEmisionValue { get; set; } = "";
    public string FechaPagoValue { get; set; } = "";
    public decimal ValorTotal { get; set; }
    public decimal ValorIva { get; set; }
    public decimal ValorPago { get; set; }
    public decimal CloudValue { get; set; }
    public decimal CopiersValue { get; set; }
    public string CategoryValue { get; set; } = "";
    public string AccountCode { get; set; } = "";
    public string SiigoSupplierId { get; set; } = "";
    public string SiigoSupplierName { get; set; } = "";
    public string SiigoSupplierIdentification { get; set; } = "";
    public int SiigoSupplierBranchOffice { get; set; }
    public IReadOnlyList<ConciliacionCuentaCobroRetentionDto> Retentions { get; set; } =
        Array.Empty<ConciliacionCuentaCobroRetentionDto>();
}

public sealed class ConciliacionActionResultDto
{
    public string Message { get; set; } = "";
    public ConciliacionClientPaymentRowDto? Row { get; set; }
}

public sealed class ConciliacionPreflightResultDto
{
    public string Message { get; set; } = "";
    public bool IsReadyForSiigo { get; set; }
    public IReadOnlyList<string> Issues { get; set; } = Array.Empty<string>();
    public ConciliacionClientPaymentRowDto? Row { get; set; }
}

public sealed class ConciliacionSiigoDryRunResultDto
{
    public string Message { get; set; } = "";
    public bool IsReadyForSiigo { get; set; }
    public string TargetEndpoint { get; set; } = "";
    public string PayloadJson { get; set; } = "";
    public int LineCount { get; set; }
    public decimal DebitTotal { get; set; }
    public decimal CreditTotal { get; set; }
    public IReadOnlyList<string> Issues { get; set; } = Array.Empty<string>();
    public ConciliacionClientPaymentRowDto? Row { get; set; }
}

public sealed class ConciliacionSiigoSendPreparedDto
{
    public string Message { get; set; } = "";
    public bool CanSend { get; set; }
    public string TargetEndpoint { get; set; } = "";
    public string CustomerIdentification { get; set; } = "";
    public IReadOnlyList<string> InvoiceNumbers { get; set; } = Array.Empty<string>();
    public object? Payload { get; set; }
    public string PayloadJson { get; set; } = "";
    public IReadOnlyList<string> Issues { get; set; } = Array.Empty<string>();
    public ConciliacionClientPaymentRowDto? Row { get; set; }
}

public sealed class ConciliacionSiigoSendResultDto
{
    public string Message { get; set; } = "";
    public bool IsSuccess { get; set; }
    public bool DataversePaymentsSucceeded { get; set; }
    public bool SiigoSucceeded { get; set; }
    public bool DataverseReconciliationSucceeded { get; set; }
    public string SiigoId { get; set; } = "";
    public string SiigoName { get; set; } = "";
    public string TargetEndpoint { get; set; } = "";
    public string PayloadJson { get; set; } = "";
    public string ResponseJson { get; set; } = "";
    public IReadOnlyList<string> Issues { get; set; } = Array.Empty<string>();
    public ConciliacionClientPaymentRowDto? Row { get; set; }
}

public sealed class ConciliacionDianActionResultDto
{
    public string Message { get; set; } = "";
    public bool IsSuccess { get; set; }
    public bool IsReadyForSiigo { get; set; }
    public string TargetEndpoint { get; set; } = "";
    public string PayloadJson { get; set; } = "";
    public string ResponseJson { get; set; } = "";
    public string SiigoId { get; set; } = "";
    public string SiigoName { get; set; } = "";
    public IReadOnlyList<string> Issues { get; set; } = Array.Empty<string>();
    public ConciliacionDianSupplierInvoiceRowDto? Row { get; set; }
}

public sealed class ConciliacionCuentaCobroActionResultDto
{
    public string Message { get; set; } = "";
    public bool IsSuccess { get; set; }
    public bool IsReadyForSiigo { get; set; }
    public string TargetEndpoint { get; set; } = "";
    public string PayloadJson { get; set; } = "";
    public string ResponseJson { get; set; } = "";
    public string SiigoId { get; set; } = "";
    public string SiigoName { get; set; } = "";
    public IReadOnlyList<string> Issues { get; set; } = Array.Empty<string>();
    public ConciliacionCuentaCobroRowDto? Row { get; set; }
}

public sealed class ConciliacionCuentaCobroEditorDto
{
    public ConciliacionCuentaCobroRowDto Row { get; set; } = new();
    public IReadOnlyList<ConciliacionSiigoRetentionOptionDto> ReteFuenteOptions { get; set; } =
        Array.Empty<ConciliacionSiigoRetentionOptionDto>();
    public IReadOnlyList<ConciliacionSiigoRetentionOptionDto> ReteIcaOptions { get; set; } =
        Array.Empty<ConciliacionSiigoRetentionOptionDto>();
    public IReadOnlyList<ConciliacionSiigoRetentionOptionDto> RteIvaOptions { get; set; } =
        Array.Empty<ConciliacionSiigoRetentionOptionDto>();
}

public sealed class SiigoVoucherCreateResultDto
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Number { get; set; } = "";
    public string Date { get; set; } = "";
    public string RawJson { get; set; } = "";
}

public sealed class SiigoDocumentTypeLookupDto
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Type { get; set; } = "";
    public bool Active { get; set; }
    public bool AutomaticNumber { get; set; }
    public int Consecutive { get; set; }
}

public sealed class SiigoTaxLookupDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
    public decimal Percentage { get; set; }
    public bool Active { get; set; }
}

public sealed class SiigoPaymentTypeLookupDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
    public bool Active { get; set; }
    public bool DueDate { get; set; }
}

public sealed class ConciliacionSyncHealthDto
{
    public int Year { get; set; }
    public int Month { get; set; }
    public string PeriodLabel { get; set; } = "";
    public string GeneratedAtDisplay { get; set; } = "";
    public string StatusLabel { get; set; } = "";
    public string StatusTone { get; set; } = "";
    public int TotalDifferenceRows { get; set; }
    public IReadOnlyList<ConciliacionSyncHealthItemDto> Items { get; set; } = Array.Empty<ConciliacionSyncHealthItemDto>();
}

public sealed class ConciliacionSyncHealthItemDto
{
    public string Key { get; set; } = "";
    public string Label { get; set; } = "";
    public string Description { get; set; } = "";
    public string StatusLabel { get; set; } = "";
    public string StatusTone { get; set; } = "";
    public string DataverseLabel { get; set; } = "Dataverse";
    public string SiigoLabel { get; set; } = "Siigo";
    public decimal DataverseTotal { get; set; }
    public decimal SiigoTotal { get; set; }
    public decimal DifferenceTotal { get; set; }
    public decimal DataverseVat { get; set; }
    public decimal SiigoVat { get; set; }
    public decimal VatDifference { get; set; }
    public int DataverseCount { get; set; }
    public int SiigoCount { get; set; }
    public int CountDifference { get; set; }
    public int DifferenceRows { get; set; }
    public string Notes { get; set; } = "";
}

public sealed class ConciliacionBillingDifferencesDto
{
    public int Year { get; set; }
    public int Month { get; set; }
    public string PeriodLabel { get; set; } = "";
    public string GeneratedAtDisplay { get; set; } = "";
    public string StatusLabel { get; set; } = "";
    public string StatusTone { get; set; } = "";
    public int MissingInDataverseCount { get; set; }
    public int OnlyDataverseCount { get; set; }
    public int AmountDifferenceCount { get; set; }
    public IReadOnlyList<ConciliacionBillingDifferenceRowDto> MissingInDataverse { get; set; } = Array.Empty<ConciliacionBillingDifferenceRowDto>();
    public IReadOnlyList<ConciliacionBillingDifferenceRowDto> OnlyDataverse { get; set; } = Array.Empty<ConciliacionBillingDifferenceRowDto>();
    public IReadOnlyList<ConciliacionBillingDifferenceRowDto> AmountDifferences { get; set; } = Array.Empty<ConciliacionBillingDifferenceRowDto>();
}

public sealed class ConciliacionBillingDifferenceRowDto
{
    public string Key { get; set; } = "";
    public string Source { get; set; } = "";
    public string StatusLabel { get; set; } = "";
    public string StatusTone { get; set; } = "";
    public string RecordId { get; set; } = "";
    public string SiigoInvoiceId { get; set; } = "";
    public string InvoiceNumber { get; set; } = "";
    public string Prefix { get; set; } = "";
    public string Number { get; set; } = "";
    public string DateValue { get; set; } = "";
    public string DateDisplay { get; set; } = "";
    public string CustomerIdentification { get; set; } = "";
    public string ClientName { get; set; } = "";
    public decimal SiigoTotal { get; set; }
    public decimal DataverseTotal { get; set; }
    public decimal SiigoVat { get; set; }
    public decimal DataverseVat { get; set; }
    public decimal Difference { get; set; }
    public decimal VatDifference { get; set; }
    public bool CanCreateInDataverse { get; set; }
    public bool CanDeleteFromDataverse { get; set; }
}

public sealed class ConciliacionBillingCreateRequest
{
    public int? Year { get; set; }
    public int? Month { get; set; }
    public IReadOnlyList<string> InvoiceKeys { get; set; } = Array.Empty<string>();
}

public sealed class ConciliacionBillingDeleteRequest
{
    public int? Year { get; set; }
    public int? Month { get; set; }
    public IReadOnlyList<string> RecordIds { get; set; } = Array.Empty<string>();
}

public sealed class ConciliacionBillingDifferenceActionResultDto
{
    public string Message { get; set; } = "";
    public int Applied { get; set; }
    public int Errors { get; set; }
    public IReadOnlyList<ConciliacionBillingDifferenceActionDto> Actions { get; set; } = Array.Empty<ConciliacionBillingDifferenceActionDto>();
    public ConciliacionBillingDifferencesDto Differences { get; set; } = new();
}

public sealed class ConciliacionBillingDifferenceActionDto
{
    public string Entity { get; set; } = "";
    public string Action { get; set; } = "";
    public string Document { get; set; } = "";
    public string RecordId { get; set; } = "";
    public string Notes { get; set; } = "";
}
