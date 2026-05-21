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
    public ConciliacionClientPaymentSummaryDto ClientPayments { get; set; } = new();
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
    public IReadOnlyList<ConciliacionFlowStepDto> Steps { get; set; } = Array.Empty<ConciliacionFlowStepDto>();
}

public sealed class ConciliacionFlowStepDto
{
    public string Label { get; set; } = "";
    public string StatusLabel { get; set; } = "";
    public string StatusTone { get; set; } = "";
    public string Summary { get; set; } = "";
}

public sealed class ConciliacionClientPaymentSummaryDto
{
    public int TotalRows { get; set; }
    public int Suggested { get; set; }
    public int Approved { get; set; }
    public int Rejected { get; set; }
    public int PendingReview { get; set; }
    public int DifferenceOutOfTolerance { get; set; }
    public int NoInvoiceToken { get; set; }
    public int NoInvoiceMatch { get; set; }
    public int AmbiguousInvoice { get; set; }
    public decimal TotalEntries { get; set; }
    public decimal SuggestedEntries { get; set; }
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
    public string InvoiceNumbers { get; set; } = "";
    public string ClientNames { get; set; } = "";
    public decimal EntryValue { get; set; }
    public decimal InvoiceTotal { get; set; }
    public decimal RetentionsTotal { get; set; }
    public decimal DifferenceValue { get; set; }
    public string DraftJson { get; set; } = "";
    public string ModifiedOnDisplay { get; set; } = "";
}

public sealed class ConciliacionClientPaymentStatusRequest
{
    public string RecordId { get; set; } = "";
    public string Status { get; set; } = "";
    public string Reason { get; set; } = "";
}

public sealed class ConciliacionActionResultDto
{
    public string Message { get; set; } = "";
    public ConciliacionClientPaymentRowDto? Row { get; set; }
}
