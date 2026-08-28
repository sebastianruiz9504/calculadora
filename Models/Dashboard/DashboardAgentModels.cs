namespace CotizadorInterno.Web.Models.Dashboard;

public sealed class DashboardAgentChatRequestDto
{
    public string Message { get; set; } = "";
    public List<DashboardAgentChatMessageDto> History { get; set; } = new();
}

public sealed class DashboardAgentChatMessageDto
{
    public string Role { get; set; } = "";
    public string Content { get; set; } = "";
}

public sealed class DashboardAgentChatResponseDto
{
    public string Answer { get; set; } = "";
    public IReadOnlyList<DashboardAgentSourceDto> Sources { get; set; } = Array.Empty<DashboardAgentSourceDto>();
    public IReadOnlyList<string> FollowUps { get; set; } = Array.Empty<string>();
    public string Confidence { get; set; } = "";
    public DashboardAgentContextSummaryDto? ContextSummary { get; set; }
    public IReadOnlyList<DashboardAgentTableResultDto> Tables { get; set; } = Array.Empty<DashboardAgentTableResultDto>();
    public DashboardAgentExportDto? Export { get; set; }
    public bool LearningReviewQueued { get; set; }
}

public sealed class DashboardAgentSourceDto
{
    public string Label { get; set; } = "";
    public string Table { get; set; } = "";
    public string Detail { get; set; } = "";
    public int RecordsCount { get; set; }
}

public sealed class DashboardAgentTableResultDto
{
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public int TotalRows { get; set; }
    public IReadOnlyList<DashboardAgentTableColumnDto> Columns { get; set; } = Array.Empty<DashboardAgentTableColumnDto>();
    public IReadOnlyList<IReadOnlyDictionary<string, string>> Rows { get; set; } = Array.Empty<IReadOnlyDictionary<string, string>>();
}

public sealed class DashboardAgentTableColumnDto
{
    public string Key { get; set; } = "";
    public string Label { get; set; } = "";
}

public sealed class DashboardAgentExportDto
{
    public string ExportId { get; set; } = "";
    public string FileName { get; set; } = "";
    public string Label { get; set; } = "";
    public int RecordsCount { get; set; }
}

public sealed class DashboardAgentFeedbackRequestDto
{
    public string Question { get; set; } = "";
    public string Answer { get; set; } = "";
    public string Category { get; set; } = "";
    public string ExpectedAnswer { get; set; } = "";
    public string Notes { get; set; } = "";
    public IReadOnlyList<DashboardAgentSourceDto> Sources { get; set; } = Array.Empty<DashboardAgentSourceDto>();
    public DashboardAgentContextSummaryDto? ContextSummary { get; set; }
}

public sealed class DashboardAgentFeedbackResultDto
{
    public string FeedbackId { get; set; } = "";
    public string Message { get; set; } = "";
    public string Storage { get; set; } = "";
}

public sealed class DashboardAgentLearningBoardDto
{
    public int RecordsCount { get; set; }
    public string Storage { get; set; } = "";
    public IReadOnlyList<DashboardAgentLearningFeedbackRowDto> Rows { get; set; } = Array.Empty<DashboardAgentLearningFeedbackRowDto>();
}

public sealed class DashboardAgentLearningFeedbackRowDto
{
    public string FeedbackId { get; set; } = "";
    public string CreatedOnValue { get; set; } = "";
    public string CreatedOnDisplay { get; set; } = "";
    public string Question { get; set; } = "";
    public string Answer { get; set; } = "";
    public string Category { get; set; } = "";
    public string ExpectedAnswer { get; set; } = "";
    public string Notes { get; set; } = "";
    public string Status { get; set; } = "";
    public string ReviewNotes { get; set; } = "";
    public string CreatedByName { get; set; } = "";
    public string CreatedByEmail { get; set; } = "";
    public string SourcesJson { get; set; } = "";
    public string ContextSummaryJson { get; set; } = "";
}

public sealed class DashboardAgentLearningStatusUpdateRequestDto
{
    public string FeedbackId { get; set; } = "";
    public string Status { get; set; } = "";
    public string ReviewNotes { get; set; } = "";
}

public sealed class DashboardAgentExpensesDto
{
    public string StartDateValue { get; set; } = "";
    public string EndDateValue { get; set; } = "";
    public string PeriodLabel { get; set; } = "";
    public int RecordsCount { get; set; }
    public IReadOnlyList<DashboardAgentExpenseRowDto> Rows { get; set; } = Array.Empty<DashboardAgentExpenseRowDto>();
}

public sealed class DashboardAgentExpenseRowDto
{
    public string RecordId { get; set; } = "";
    public string Name { get; set; } = "";
    public string InvoiceNumber { get; set; } = "";
    public string SupplierName { get; set; } = "";
    public string SupplierNit { get; set; } = "";
    public string RecipientName { get; set; } = "";
    public string RecipientNit { get; set; } = "";
    public string EmissionDateValue { get; set; } = "";
    public string PaymentDateValue { get; set; } = "";
    public decimal TotalValue { get; set; }
    public decimal TotalBeforeVatValue { get; set; }
    public decimal VatValue { get; set; }
    public decimal PaymentValue { get; set; }
    public decimal ReteFuenteValue { get; set; }
    public decimal ReteIcaValue { get; set; }
    public decimal CloudValue { get; set; }
    public decimal CopiersValue { get; set; }
    public string CategoryLabel { get; set; } = "";
    public string AccountCode { get; set; } = "";
    public string AccountName { get; set; } = "";
    public string AutomationState { get; set; } = "";
    public string ReviewReason { get; set; } = "";
    public string SourceLabel { get; set; } = "";
    public string Details { get; set; } = "";
    public string SearchText { get; set; } = "";
}
