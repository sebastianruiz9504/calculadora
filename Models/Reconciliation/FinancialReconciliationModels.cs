namespace CotizadorInterno.Web.Models.Reconciliation;

public sealed class ReconciliationDataverseBillingRow
{
    public string RecordId { get; set; } = "";
    public string InvoiceNumber { get; set; } = "";
    public string InvoicePrefix { get; set; } = "";
    public string InvoiceCode { get; set; } = "";
    public string SiigoInvoiceId { get; set; } = "";
    public string SiigoInvoiceName { get; set; } = "";
    public string ClientId { get; set; } = "";
    public string ClientName { get; set; } = "";
    public string CompanyTaxId { get; set; } = "";
    public DateOnly? EmissionDate { get; set; }
    public int? VerticalOptionValue { get; set; }
    public int? ContractTypeOptionValue { get; set; }
    public decimal Total { get; set; }
    public decimal Vat { get; set; }
    public decimal? VatPercent { get; set; }
}

public sealed class ReconciliationDataverseCreditNoteRow
{
    public string RecordId { get; set; } = "";
    public string CreditNoteId { get; set; } = "";
    public string CreditNoteName { get; set; } = "";
    public long? CreditNoteNumber { get; set; }
    public DateOnly? Date { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public string InvoiceId { get; set; } = "";
    public string InvoiceName { get; set; } = "";
    public string InvoicePrefix { get; set; } = "";
    public long? InvoiceNumber { get; set; }
    public string CustomerIdentification { get; set; } = "";
    public decimal Total { get; set; }
    public decimal Vat { get; set; }
    public string FacturacionDataverseId { get; set; } = "";
    public string MatchBy { get; set; } = "";
    public bool Processed { get; set; }
}

public sealed class ReconciliationDataverseExpenseRow
{
    public string RecordId { get; set; } = "";
    public string InvoiceNumber { get; set; } = "";
    public string IssuerName { get; set; } = "";
    public string IssuerNit { get; set; } = "";
    public string RecipientName { get; set; } = "";
    public string RecipientNit { get; set; } = "";
    public DateOnly? EmissionDate { get; set; }
    public DateOnly? PaymentDate { get; set; }
    public decimal Total { get; set; }
    public decimal Vat { get; set; }
    public decimal PaymentValue { get; set; }
}

public sealed class SiigoFinancialReconciliationData
{
    public IReadOnlyList<SiigoReconciliationInvoice> Invoices { get; set; } = Array.Empty<SiigoReconciliationInvoice>();
    public IReadOnlyList<SiigoReconciliationCreditNote> CreditNotes { get; set; } = Array.Empty<SiigoReconciliationCreditNote>();
    public IReadOnlyList<SiigoReconciliationPurchase> Purchases { get; set; } = Array.Empty<SiigoReconciliationPurchase>();
}

public sealed class SiigoReconciliationInvoice
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Prefix { get; set; } = "";
    public long? Number { get; set; }
    public DateOnly? Date { get; set; }
    public string CustomerId { get; set; } = "";
    public string CustomerIdentification { get; set; } = "";
    public decimal Total { get; set; }
    public decimal SuggestedWithholdingTotal { get; set; }
    public decimal GrossTotal { get; set; }
    public decimal Vat { get; set; }
    public bool Annulled { get; set; }
    public string StampStatus { get; set; } = "";
    public string RawJson { get; set; } = "";
}

public sealed class SiigoReconciliationCreditNote
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public long? Number { get; set; }
    public DateOnly? Date { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public string InvoiceId { get; set; } = "";
    public string InvoiceName { get; set; } = "";
    public string InvoicePrefix { get; set; } = "";
    public long? InvoiceNumber { get; set; }
    public string CustomerId { get; set; } = "";
    public string CustomerIdentification { get; set; } = "";
    public string StampStatus { get; set; } = "";
    public string Cude { get; set; } = "";
    public decimal Total { get; set; }
    public decimal SuggestedWithholdingTotal { get; set; }
    public decimal GrossTotal { get; set; }
    public decimal Vat { get; set; }
    public string RawJson { get; set; } = "";
}

public sealed class SiigoReconciliationPurchase
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public DateOnly? Date { get; set; }
    public string SupplierIdentification { get; set; } = "";
    public string ProviderInvoicePrefix { get; set; } = "";
    public string ProviderInvoiceNumber { get; set; } = "";
    public string ProviderInvoiceFullNumber { get; set; } = "";
    public DateOnly? PaymentDueDate { get; set; }
    public decimal Total { get; set; }
    public decimal Vat { get; set; }
    public decimal Balance { get; set; }
}

public sealed class FinancialReconciliationReportResult
{
    public int Year { get; set; }
    public int Month { get; set; }
    public string PeriodLabel { get; set; } = "";
    public string FileName { get; set; } = "";
    public byte[] ExcelContent { get; set; } = Array.Empty<byte>();
    public FinancialReconciliationSummary BeforeSummary { get; set; } = new();
    public FinancialReconciliationSummary Summary { get; set; } = new();
    public FinancialReconciliationCorrectionResult Corrections { get; set; } = new();
}

public sealed class FinancialReconciliationRunResult
{
    public FinancialReconciliationReportResult Report { get; set; } = new();
    public bool EmailSent { get; set; }
    public string EmailStatus { get; set; } = "";
    public bool ReteFuenteEmailSent { get; set; }
    public string ReteFuenteEmailStatus { get; set; } = "";
}

public sealed class FinancialReconciliationSnapshotResult
{
    public int Year { get; set; }
    public int Month { get; set; }
    public string PeriodLabel { get; set; } = "";
    public DateTimeOffset GeneratedAt { get; set; }
    public FinancialReconciliationSummary Summary { get; set; } = new();
}

public sealed class FinancialReconciliationSummary
{
    public decimal SiigoBillingGross { get; set; }
    public decimal SiigoBillingCreditNotes { get; set; }
    public decimal SiigoBillingNet { get; set; }
    public int SiigoBillingInvoiceCount { get; set; }
    public int SiigoBillingCreditNoteCount { get; set; }
    public decimal DataverseBillingGross { get; set; }
    public decimal DataverseBillingCreditNotes { get; set; }
    public decimal DataverseBillingNet { get; set; }
    public decimal DataverseBilling { get; set; }
    public int DataverseBillingInvoiceCount { get; set; }
    public int DataverseBillingCreditNoteCount { get; set; }
    public decimal BillingDifference { get; set; }
    public decimal SiigoVatGross { get; set; }
    public decimal SiigoVatCreditNotes { get; set; }
    public decimal SiigoVatNet { get; set; }
    public decimal DataverseVatGross { get; set; }
    public decimal DataverseVatCreditNotes { get; set; }
    public decimal DataverseVatNet { get; set; }
    public decimal DataverseVat { get; set; }
    public decimal BillingVatDifference { get; set; }
    public decimal PowerAppsExpenses { get; set; }
    public decimal SiigoExpenses { get; set; }
    public decimal ExpenseDifference { get; set; }
    public int PowerAppsExpenseCount { get; set; }
    public int SiigoExpenseCount { get; set; }
    public decimal PowerAppsExpenseVat { get; set; }
    public decimal SiigoExpenseVat { get; set; }
    public decimal ExpenseVatDifference { get; set; }
    public int BillingDifferenceCount { get; set; }
    public int ExpenseDifferenceCount { get; set; }
}

public sealed class FinancialReconciliationCorrectionResult
{
    public IReadOnlyList<FinancialReconciliationCorrectionAction> Actions { get; set; } = Array.Empty<FinancialReconciliationCorrectionAction>();
    public int CreatedInvoices => Actions.Count(static action => string.Equals(action.Entity, "Factura", StringComparison.OrdinalIgnoreCase)
        && string.Equals(action.Action, "Creada", StringComparison.OrdinalIgnoreCase));
    public int UpdatedInvoices => Actions.Count(static action => string.Equals(action.Entity, "Factura", StringComparison.OrdinalIgnoreCase)
        && string.Equals(action.Action, "Actualizada", StringComparison.OrdinalIgnoreCase));
    public int CreatedCreditNotes => Actions.Count(static action => string.Equals(action.Entity, "NC", StringComparison.OrdinalIgnoreCase)
        && string.Equals(action.Action, "Creada", StringComparison.OrdinalIgnoreCase));
    public int UpdatedCreditNotes => Actions.Count(static action => string.Equals(action.Entity, "NC", StringComparison.OrdinalIgnoreCase)
        && string.Equals(action.Action, "Actualizada", StringComparison.OrdinalIgnoreCase));
    public int DeletedInvoices => Actions.Count(static action => string.Equals(action.Entity, "Factura", StringComparison.OrdinalIgnoreCase)
        && string.Equals(action.Action, "Eliminada", StringComparison.OrdinalIgnoreCase));
    public int Errors => Actions.Count(static action => string.Equals(action.Action, "Error", StringComparison.OrdinalIgnoreCase));
    public int Applied => CreatedInvoices + UpdatedInvoices + DeletedInvoices + CreatedCreditNotes + UpdatedCreditNotes;
}

public sealed class FinancialReconciliationCorrectionAction
{
    public string Entity { get; set; } = "";
    public string Action { get; set; } = "";
    public string Document { get; set; } = "";
    public string RecordId { get; set; } = "";
    public decimal PreviousTotal { get; set; }
    public decimal NewTotal { get; set; }
    public decimal PreviousVat { get; set; }
    public decimal NewVat { get; set; }
    public string Notes { get; set; } = "";
}
