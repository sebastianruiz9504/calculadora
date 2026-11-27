using System.Text.Json.Serialization;

namespace CotizadorInterno.Web.Models.Conciliacion;

public sealed record DirectCashFlowConcept(string Key, string Code, string Label, string Direction, int Row, string Group);
public sealed record DirectCashFlowReportRow(int Row, string Code, string Label, string Kind, string? ConceptKey, IReadOnlyList<decimal?> Values);
public sealed class DirectCashFlowMonth
{
    public int Year { get; set; }
    public int Month { get; set; }
    public string Label { get; set; } = "";
    public decimal Entries { get; set; }
    public decimal Exits { get; set; }
    public decimal UnclassifiedEntries { get; set; }
    public decimal UnclassifiedExits { get; set; }
    public int UnclassifiedCount { get; set; }
    public int SuggestedCount { get; set; }
    public int ConfirmedCount { get; set; }
    public int ManualCount { get; set; }
    public int InternalTransferCount { get; set; }
    public decimal InternalTransferNet { get; set; }
    public decimal? OpeningBalance { get; set; }
    public decimal? ClosingBalance { get; set; }
    public decimal? CalculatedClosingBalance { get; set; }
    public string ClosingSource { get; set; } = "";
    public decimal? BankDifference { get; set; }
    public string OpeningSource { get; set; } = "";
    public bool HasActivity { get; set; }
    public DirectCashFlowBalances Balances { get; set; } = new();
}
public sealed class DirectCashFlowReport
{
    public int Year { get; set; }
    public int StartMonth { get; set; }
    public int EndMonth { get; set; }
    public int EndYear { get; set; }
    public DateTimeOffset CapturedAt { get; set; } = DateTimeOffset.UtcNow;
    public IReadOnlyList<DirectCashFlowMonth> Months { get; set; } = [];
    public IReadOnlyList<DirectCashFlowReportRow> Rows { get; set; } = [];
    public IReadOnlyList<DirectCashFlowBankCheck> BankChecks { get; set; } = [];
}
public sealed class DirectCashFlowMovement
{
    public string RecordId { get; set; } = "";
    public string SourceKind { get; set; } = "Movimiento";
    public string Date { get; set; } = "";
    public string Bank { get; set; } = "";
    public string Description { get; set; } = "";
    public string Reference { get; set; } = "";
    public string Direction { get; set; } = "";
    public decimal Amount { get; set; }
    public decimal Entry { get; set; }
    public decimal Exit { get; set; }
    public string ConceptKey { get; set; } = "";
    public string ConceptLabel { get; set; } = "";
    public string Status { get; set; } = "Pendiente";
    public string Reason { get; set; } = "";
    public string Version { get; set; } = "";
    public bool IsManual => SourceKind == "Manual";
    public bool IsStatement { get; set; }
    public bool IsInternalTransfer { get; set; }
}
public sealed record DirectCashFlowPage(IReadOnlyList<DirectCashFlowMovement> Items, int Total, int Page, int PageSize);
public sealed class DirectCashFlowAssignmentRequest
{
    public string RecordId { get; set; } = "";
    public string SourceKind { get; set; } = "";
    public string ConceptKey { get; set; } = "";
    public string Version { get; set; } = "";
}
public sealed class DirectCashFlowManualRequest
{
    public string RecordId { get; set; } = "";
    public string Date { get; set; } = "";
    public string Bank { get; set; } = "";
    public string Direction { get; set; } = "Salida";
    public decimal Amount { get; set; }
    public string Description { get; set; } = "";
    public string Reference { get; set; } = "";
    public string ConceptKey { get; set; } = "";
    public string Version { get; set; } = "";
}
public sealed class DirectCashFlowBalances
{
    public int Year { get; set; }
    public int Month { get; set; }
    public decimal? Opening { get; set; }
    public decimal? Closing { get; set; }
    [JsonIgnore]
    public decimal? Cloud { get; set; }
    [JsonIgnore]
    public decimal? Copiers { get; set; }
    [JsonIgnore]
    public decimal? Pockets { get; set; }
    [JsonIgnore]
    public decimal? Fiduciary { get; set; }
    public string Version { get; set; } = "";
}
public sealed class DirectCashFlowData
{
    public DateOnly? HistoryStart { get; init; }
    public DateOnly? HistoryEnd { get; init; }
    public DateTimeOffset CapturedAt { get; init; } = DateTimeOffset.UtcNow;
    public IReadOnlyList<DirectCashFlowMovement> Movements { get; init; } = [];
    public IReadOnlyList<DirectCashFlowBalances> Balances { get; init; } = [];
    public IReadOnlyDictionary<string, decimal> BankOpenings { get; init; } = new Dictionary<string, decimal>();
    public IReadOnlyList<DirectCashFlowStatement> Statements { get; init; } = [];
}

public sealed class DirectCashFlowStatement
{
    public string Period { get; set; } = "";
    public string Bank { get; set; } = "";
    public string Account { get; set; } = "";
    public string FileHash { get; set; } = "";
    public decimal Opening { get; set; }
    public decimal Credits { get; set; }
    public decimal Debits { get; set; }
    public decimal Closing { get; set; }
    public IReadOnlyList<DirectCashFlowStatementLine> Rows { get; set; } = [];
}
public sealed class DirectCashFlowStatementLine
{
    public string RecordId { get; set; } = "";
    public string SourceKind { get; set; } = "";
    public string Date { get; set; } = "";
    public decimal Amount { get; set; }
    public decimal Balance { get; set; }
    public int Page { get; set; }
    public int Line { get; set; }
}
public sealed class DirectCashFlowBankCheck
{
    public string Period { get; set; } = "";
    public string Bank { get; set; } = "";
    public string Account { get; set; } = "";
    public decimal Opening { get; set; }
    public decimal StatementCredits { get; set; }
    public decimal StatementDebits { get; set; }
    public decimal StatementClosing { get; set; }
    public decimal RegisteredCredits { get; set; }
    public decimal RegisteredDebits { get; set; }
    public decimal CalculatedClosing => Opening + RegisteredCredits - RegisteredDebits;
    public decimal Difference => CalculatedClosing - StatementClosing;
    public int StatementCount { get; set; }
    public int RegisteredCount { get; set; }
    public int MissingCount { get; set; }
    public int UnexpectedCount { get; set; }
    public int ChangedCount { get; set; }
    public int PendingCount { get; set; }
    public int ImportedCount { get; set; }
    public bool StatementValid { get; set; }
    public bool Matches => StatementValid && Difference == 0 && RegisteredCredits == StatementCredits && RegisteredDebits == StatementDebits && MissingCount == 0 && UnexpectedCount == 0 && ChangedCount == 0;
}
