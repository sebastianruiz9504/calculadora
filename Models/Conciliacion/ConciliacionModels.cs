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
    public IReadOnlyList<ConciliacionOptionDto> DianCategoryOptions { get; set; } = Array.Empty<ConciliacionOptionDto>();
    public IReadOnlyList<ConciliacionOptionDto> DianExpenseAccountOptions { get; set; } = Array.Empty<ConciliacionOptionDto>();
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
    public IReadOnlyList<ConciliacionCashFlowRowDto> Rows { get; set; } = Array.Empty<ConciliacionCashFlowRowDto>();
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
    public string DetectedTypeKey { get; set; } = "";
    public string DetectedTypeLabel { get; set; } = "";
    public string DetectedTypeTone { get; set; } = "";
    public string ValidationStatus { get; set; } = "";
    public string ValidationTone { get; set; } = "";
    public string RegistrationStatus { get; set; } = "";
    public string RegistrationTone { get; set; } = "";
    public string DataverseStatus { get; set; } = "";
    public string SiigoStatus { get; set; } = "";
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
    public string SiigoDocumentId { get; set; } = "";
    public string SiigoDocumentName { get; set; } = "";
    public string SiigoSupplierId { get; set; } = "";
    public string SiigoSupplierName { get; set; } = "";
    public string ProviderStatusLabel { get; set; } = "";
    public string ProviderStatusTone { get; set; } = "";
    public string ClassificationStatusLabel { get; set; } = "";
    public string ClassificationStatusTone { get; set; } = "";
    public string SiigoStatusLabel { get; set; } = "";
    public string SiigoStatusTone { get; set; } = "";
    public string SourceLabel { get; set; } = "";
    public string ModifiedOnDisplay { get; set; } = "";
}

public sealed class ConciliacionClientPaymentStatusRequest
{
    public string RecordId { get; set; } = "";
    public string Status { get; set; } = "";
    public string Reason { get; set; } = "";
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
