namespace CotizadorInterno.Web.Models.Automation;

public sealed class CashFlowImportResultDto
{
    public bool DryRun { get; set; }
    public int RowsRead { get; set; }
    public int MovementsRead { get; set; }
    public int TransfersRead { get; set; }
    public int Skipped { get; set; }
    public int BlankRowsSkipped { get; set; }
    public int FutureRowsSkipped { get; set; }
    public int DataverseRowsSkipped { get; set; }
    public int Created { get; set; }
    public int Updated { get; set; }
    public int Unchanged { get; set; }
    public decimal TotalEntries { get; set; }
    public decimal TotalExits { get; set; }
    public decimal TransferValue { get; set; }
    public IReadOnlyList<CashFlowImportFlowSummaryDto> FlowSummaries { get; set; } = Array.Empty<CashFlowImportFlowSummaryDto>();
    public IReadOnlyList<CashFlowImportSkippedRowDto> SkippedRows { get; set; } = Array.Empty<CashFlowImportSkippedRowDto>();
    public IReadOnlyList<CashFlowImportRowDto> SampleRows { get; set; } = Array.Empty<CashFlowImportRowDto>();
}

public sealed class CashFlowImportFlowSummaryDto
{
    public string SourceFlow { get; set; } = "";
    public int Rows { get; set; }
    public int Movements { get; set; }
    public int Transfers { get; set; }
    public decimal Entries { get; set; }
    public decimal Exits { get; set; }
    public decimal TransferValue { get; set; }
}

public sealed class CashFlowImportSkippedRowDto
{
    public string SourceFlow { get; set; } = "";
    public string TableName { get; set; } = "";
    public int RowNumber { get; set; }
    public DateOnly? Date { get; set; }
    public string Reason { get; set; } = "";
    public decimal Entry { get; set; }
    public decimal Exit { get; set; }
    public string Description { get; set; } = "";
}

public sealed class CashFlowImportRowDto
{
    public string SourceFileName { get; set; } = "";
    public string SourceFlow { get; set; } = "";
    public string TableName { get; set; } = "";
    public int RowNumber { get; set; }
    public DateOnly? Date { get; set; }
    public string MovementType { get; set; } = "";
    public string Category { get; set; } = "";
    public decimal Entry { get; set; }
    public decimal Exit { get; set; }
    public string Description { get; set; } = "";
    public string Recipient { get; set; } = "";
    public string DestinationBank { get; set; } = "";
    public string DocumentType { get; set; } = "";
    public string Observations { get; set; } = "";
    public string SiigoStatus { get; set; } = "";
    public string BankAccountCode { get; set; } = "";
    public string BankAccountName { get; set; } = "";
    public string ExternalKey { get; set; } = "";
    public string SourceHash { get; set; } = "";
    public bool IsTransfer { get; set; }
    public string TransferFrom { get; set; } = "";
    public string TransferTo { get; set; } = "";
}

public sealed class CashFlowDataverseUpsertResultDto
{
    public int Created { get; set; }
    public int Updated { get; set; }
    public int Unchanged { get; set; }
    public int Skipped { get; set; }
}

public sealed class DianSupplierDocumentImportResultDto
{
    public bool DryRun { get; set; }
    public string SourceFileName { get; set; } = "";
    public int RowsRead { get; set; }
    public int ImportableRows { get; set; }
    public int InvoiceRows { get; set; }
    public int SupportDocumentRows { get; set; }
    public int SkippedRows { get; set; }
    public int Created { get; set; }
    public int Updated { get; set; }
    public int Unchanged { get; set; }
    public int DataverseRowsSkipped { get; set; }
    public int SupplierLookupReviewed { get; set; }
    public int SupplierLookupFound { get; set; }
    public int SupplierLookupMissing { get; set; }
    public int SupplierLookupFailed { get; set; }
    public int SupplierLookupRowsUpdated { get; set; }
    public int AutoClassificationReviewed { get; set; }
    public int AutoClassificationUpdated { get; set; }
    public int AutoClassificationAlreadyAssigned { get; set; }
    public int AutoClassificationNoRule { get; set; }
    public int AutoClassificationInvalidRule { get; set; }
    public string AutoClassificationMessage { get; set; } = "";
    public decimal TotalValue { get; set; }
    public decimal VatValue { get; set; }
    public decimal ReteFuenteValue { get; set; }
    public decimal ReteIcaValue { get; set; }
    public decimal ReteIvaValue { get; set; }
    public IReadOnlyList<DianSupplierDocumentSkippedRowDto> Skipped { get; set; } = Array.Empty<DianSupplierDocumentSkippedRowDto>();
    public IReadOnlyList<DianSupplierDocumentImportRowDto> SampleRows { get; set; } = Array.Empty<DianSupplierDocumentImportRowDto>();
}

public sealed class DianSupplierDocumentSkippedRowDto
{
    public int RowNumber { get; set; }
    public string DocumentType { get; set; } = "";
    public string Group { get; set; } = "";
    public string Prefix { get; set; } = "";
    public string Folio { get; set; } = "";
    public string Reason { get; set; } = "";
}

public sealed class DianSupplierDocumentImportRowDto
{
    public string SourceFileName { get; set; } = "";
    public string SheetName { get; set; } = "";
    public int RowNumber { get; set; }
    public string ExternalKey { get; set; } = "";
    public string SourceHash { get; set; } = "";
    public string DocumentKind { get; set; } = "";
    public string DocumentType { get; set; } = "";
    public string DianGroup { get; set; } = "";
    public string DianStatus { get; set; } = "";
    public string CufeCude { get; set; } = "";
    public string Prefix { get; set; } = "";
    public string Folio { get; set; } = "";
    public string InvoiceNumber { get; set; } = "";
    public string Currency { get; set; } = "";
    public string PaymentForm { get; set; } = "";
    public string PaymentMethod { get; set; } = "";
    public DateOnly? EmissionDate { get; set; }
    public DateTimeOffset? ReceptionDate { get; set; }
    public string SupplierNit { get; set; } = "";
    public string SupplierName { get; set; } = "";
    public string CompanyNit { get; set; } = "";
    public string CompanyName { get; set; } = "";
    public decimal BaseAmount { get; set; }
    public decimal VatValue { get; set; }
    public decimal IcaValue { get; set; }
    public decimal ReteIvaValue { get; set; }
    public decimal ReteFuenteValue { get; set; }
    public decimal ReteIcaValue { get; set; }
    public decimal TotalValue { get; set; }
}

public sealed class DianSupplierDocumentDataverseUpsertResultDto
{
    public int Created { get; set; }
    public int Updated { get; set; }
    public int Unchanged { get; set; }
    public int Skipped { get; set; }
}

public sealed class DianSupplierDocumentResolvedSupplierDto
{
    public string SupplierNit { get; set; } = "";
    public string SiigoSupplierId { get; set; } = "";
    public string SiigoSupplierName { get; set; } = "";
}

public sealed class DianSupplierDocumentSiigoSupplierResolutionResultDto
{
    public int Reviewed { get; set; }
    public int Found { get; set; }
    public int Missing { get; set; }
    public int Failed { get; set; }
    public int MatchedRows { get; set; }
    public int Updated { get; set; }
}
