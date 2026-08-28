using System.Text.Json.Serialization;

namespace CotizadorInterno.Web.Models.Calculator;

public sealed class ScenarioLineInput
{
    public string LineId { get; set; } = "";
    public int LineOrder { get; set; }
    public int BusinessType { get; set; }
    public string ProductId { get; set; } = "";
    public string ProductDescription { get; set; } = "";
    public decimal CostUnit { get; set; }
    public decimal MarginPercent { get; set; }
    public int ContractMonths { get; set; } = 12;
    public int Quantity { get; set; } = 1;
    public decimal SuggestedRetailPrice { get; set; }
    public decimal Acelerador { get; set; }
    public bool HasVat { get; set; }
}

public sealed class ScenarioResultSnapshot
{
    public string InputHash { get; set; } = "";
    public decimal Points { get; set; }
    public decimal Commission { get; set; }
    public int ProrationDays { get; set; }
    public decimal ProrationFactor { get; set; }
    public string? ProrationText { get; set; }
    public decimal TotalMonthlySale { get; set; }
    public decimal TotalSale { get; set; }
}

public sealed class ScenarioSaveRequest
{
    public string ScenarioId { get; set; } = "";
    public string GroupId { get; set; } = "";
    public string GroupName { get; set; } = "";
    public string PossibilityName { get; set; } = "";
    public int PossibilityOrder { get; set; } = 1;
    public bool IncludeInProposal { get; set; } = true;
    public bool IsRecommended { get; set; }
    public string ExpectedRowVersion { get; set; } = "";
    public string CrmDealId { get; set; } = "";
    public string ScenarioName { get; set; } = "";
    public int DealType { get; set; }
    public bool RequiresProration { get; set; }
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public List<ScenarioLineInput> Lines { get; set; } = new();
    public ScenarioResultSnapshot? LastResult { get; set; }
}

public sealed class ScenarioStoredDto
{
    public string ScenarioId { get; set; } = "";
    public string GroupId { get; set; } = "";
    public string GroupName { get; set; } = "";
    public string PossibilityName { get; set; } = "";
    public int PossibilityOrder { get; set; } = 1;
    public bool IncludeInProposal { get; set; } = true;
    public bool IsRecommended { get; set; }
    public string RowVersion { get; set; } = "";
    public string CrmDealId { get; set; } = "";
    public bool IsCrmSharedAccess { get; set; }

    [JsonIgnore]
    public string OwnerSystemUserId { get; set; } = "";

    [JsonIgnore]
    public string OwnerDisplayName { get; set; } = "";

    [JsonIgnore]
    public string OwnerEmail { get; set; } = "";

    [JsonIgnore]
    public string StructuredLinesHash { get; set; } = "";

    public string ScenarioName { get; set; } = "";
    public int DealType { get; set; }
    public bool RequiresProration { get; set; }
    public string? StartDate { get; set; }
    public string? EndDate { get; set; }
    public List<ScenarioLineInput> Lines { get; set; } = new();
    public ScenarioResultSnapshot? LastResult { get; set; }
}

public sealed class ScenarioGroupStoredDto
{
    public string GroupId { get; set; } = "";
    public string GroupName { get; set; } = "";
    public string PrimaryScenarioId { get; set; } = "";
    public IReadOnlyList<ScenarioStoredDto> Possibilities { get; set; } = [];
    public IReadOnlyList<ProposalExportHistoryItemDto> ProposalHistory { get; set; } = [];
}

public sealed class ScenarioPossibilityCreateRequest
{
    public string GroupId { get; set; } = "";
    public string SourceScenarioId { get; set; } = "";
    public bool DuplicateSource { get; set; } = true;
    public string Name { get; set; } = "";
}

public sealed class ScenarioPossibilityRecommendationRequest
{
    public string GroupId { get; set; } = "";
    public string ScenarioId { get; set; } = "";
}

public sealed class ScenarioGroupRenameRequest
{
    public string GroupId { get; set; } = "";
    public string GroupName { get; set; } = "";
}

public sealed class ScenarioPersistenceConflictException : InvalidOperationException
{
    public ScenarioPersistenceConflictException(string message)
        : base(message)
    {
    }
}

public sealed class ScenarioPersistenceNotFoundException : InvalidOperationException
{
    public ScenarioPersistenceNotFoundException(string message)
        : base(message)
    {
    }
}

public sealed class ScenarioPersistenceConcurrencyException : InvalidOperationException
{
    public ScenarioPersistenceConcurrencyException(string message)
        : base(message)
    {
    }
}
