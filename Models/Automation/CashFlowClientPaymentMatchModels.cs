namespace CotizadorInterno.Web.Models.Automation;

public sealed class CashFlowClientPaymentMatchResultDto
{
    public bool DryRun { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public int ReviewedMovements { get; set; }
    public int CandidateMovements { get; set; }
    public int Suggested { get; set; }
    public int PendingReview { get; set; }
    public int NoInvoiceToken { get; set; }
    public int NoInvoiceMatch { get; set; }
    public int AmbiguousInvoice { get; set; }
    public int DifferenceOutOfTolerance { get; set; }
    public int Created { get; set; }
    public int Updated { get; set; }
    public int Unchanged { get; set; }
    public int Skipped { get; set; }
    public decimal TotalEntries { get; set; }
    public decimal SuggestedEntries { get; set; }
    public decimal PendingReviewEntries { get; set; }
    public IReadOnlyList<CashFlowClientPaymentMatchRowDto> Rows { get; set; } = Array.Empty<CashFlowClientPaymentMatchRowDto>();
}

public sealed class CashFlowClientPaymentMatchRowDto
{
    public string MovementId { get; set; } = "";
    public string MovementExternalKey { get; set; } = "";
    public DateOnly? MovementDate { get; set; }
    public string SourceFlow { get; set; } = "";
    public string BankAccountCode { get; set; } = "";
    public string BankAccountName { get; set; } = "";
    public string Description { get; set; } = "";
    public decimal EntryValue { get; set; }
    public IReadOnlyList<string> InvoiceTokens { get; set; } = Array.Empty<string>();
    public string InvoiceRecordIds { get; set; } = "";
    public string InvoiceNumbers { get; set; } = "";
    public string ClientNames { get; set; } = "";
    public decimal InvoiceTotal { get; set; }
    public decimal ReteFteValue { get; set; }
    public decimal ReteIcaValue { get; set; }
    public decimal RteIvaValue { get; set; }
    public decimal RetentionsTotal { get; set; }
    public decimal DifferenceValue { get; set; }
    public int Confidence { get; set; }
    public string Status { get; set; } = "";
    public string Reason { get; set; } = "";
    public string ExternalKey { get; set; } = "";
    public string SourceHash { get; set; } = "";
    public string SiigoDraftJson { get; set; } = "";
}
