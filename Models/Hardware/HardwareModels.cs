namespace CotizadorInterno.Web.Models.Hardware;

public sealed class HardwarePageViewModel
{
    public CurrentUserInfo CurrentUser { get; set; } = new();
    public string TableLogicalName { get; set; } = "cr07a_hardware";
    public string TableDisplayName { get; set; } = "Hardware";
}

public sealed class HardwareCsvPreviewResultDto
{
    public string FileName { get; set; } = "";
    public string TableLogicalName { get; set; } = "";
    public string TableDisplayName { get; set; } = "";
    public string DetectedDelimiterLabel { get; set; } = "";
    public int TotalRows { get; set; }
    public int TotalColumns { get; set; }
    public int SystemColumnsCount { get; set; }
    public IReadOnlyList<string> SystemColumns { get; set; } = Array.Empty<string>();
    public IReadOnlyList<HardwareCsvColumnDto> Columns { get; set; } = Array.Empty<HardwareCsvColumnDto>();
    public string Message { get; set; } = "";
}

public sealed class HardwareCsvColumnDto
{
    public int Index { get; set; }
    public string SourceHeader { get; set; } = "";
    public string DisplayLabel { get; set; } = "";
    public string LogicalName { get; set; } = "";
    public string SchemaName { get; set; } = "";
    public string DataverseType { get; set; } = "";
    public string ExampleValue { get; set; } = "";
}

public sealed class HardwareProvisionResultDto
{
    public string Message { get; set; } = "";
    public string TableLogicalName { get; set; } = "";
    public string EntitySetName { get; set; } = "";
    public bool TableCreated { get; set; }
    public int CreatedColumnsCount { get; set; }
    public int ExistingColumnsCount { get; set; }
    public int ImportedCount { get; set; }
    public int SkippedDuplicatesCount { get; set; }
    public IReadOnlyList<string> CreatedColumns { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> ExistingColumns { get; set; } = Array.Empty<string>();
}
