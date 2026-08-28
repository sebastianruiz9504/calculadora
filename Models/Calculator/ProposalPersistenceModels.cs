namespace CotizadorInterno.Web.Models.Calculator;

public sealed class ProposalExportHistoryItemDto
{
    public string ExportId { get; set; } = "";
    public string GroupId { get; set; } = "";
    public int Version { get; set; }
    public string FileName { get; set; } = "";
    public DateTimeOffset ExportedAtUtc { get; set; }
    public string ExportedByName { get; set; } = "";
    public int PossibilityCount { get; set; }
}

public sealed class ProposalExportSaveRequest
{
    public string GroupId { get; set; } = "";
    public string OwnerSystemUserId { get; set; } = "";
    public string IdempotencyKey { get; set; } = "";
    public string EconomicHash { get; set; } = "";
    public string ConfigurationJson { get; set; } = "";
    public string FileName { get; set; } = "";
    public byte[] PdfContent { get; set; } = [];
    public int PossibilityCount { get; set; }
}

public sealed class ProposalExportSaveResultDto
{
    public ProposalExportHistoryItemDto Export { get; set; } = new();
    public bool AlreadyExisted { get; set; }
}

public sealed class ProposalExportDownloadDto
{
    public string FileName { get; set; } = "";
    public string ContentType { get; set; } = "application/pdf";
    public byte[] Content { get; set; } = [];
}

public sealed class ProposalConfigurationSnapshotDto
{
    public string ConfigurationJson { get; set; } = "";
    public ProposalExportHistoryItemDto? Export { get; set; }
}
