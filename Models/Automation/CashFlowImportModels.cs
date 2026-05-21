namespace CotizadorInterno.Web.Models.Automation;

public sealed class CashFlowImportResultDto
{
    public bool DryRun { get; set; }
    public int RowsRead { get; set; }
    public int MovementsRead { get; set; }
    public int TransfersRead { get; set; }
    public int Skipped { get; set; }
    public int FutureRowsSkipped { get; set; }
    public int Created { get; set; }
    public int Updated { get; set; }
    public int Unchanged { get; set; }
    public decimal TotalEntries { get; set; }
    public decimal TotalExits { get; set; }
    public decimal TransferValue { get; set; }
    public IReadOnlyList<CashFlowImportFlowSummaryDto> FlowSummaries { get; set; } = Array.Empty<CashFlowImportFlowSummaryDto>();
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
