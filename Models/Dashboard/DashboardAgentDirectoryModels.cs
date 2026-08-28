namespace CotizadorInterno.Web.Models.Dashboard;

public sealed class DashboardAgentTableDirectoryDto
{
    public string Version { get; set; } = "";
    public string ColumnMode { get; set; } = "";
    public IReadOnlyList<string> ScopeRules { get; set; } = Array.Empty<string>();
    public IReadOnlyList<DashboardAgentTableDirectoryItemDto> Tables { get; set; } = Array.Empty<DashboardAgentTableDirectoryItemDto>();
    public IReadOnlyList<DashboardAgentSemanticRelationshipDto> Relationships { get; set; } = Array.Empty<DashboardAgentSemanticRelationshipDto>();
}

public sealed class DashboardAgentTableDirectoryItemDto
{
    public string Module { get; set; } = "";
    public string Feature { get; set; } = "";
    public string Label { get; set; } = "";
    public string LogicalName { get; set; } = "";
    public string EntitySetName { get; set; } = "";
    public string ResolverKey { get; set; } = "";
    public string Description { get; set; } = "";
    public IReadOnlyList<string> BusinessTerms { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> UsedColumns { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> WritableColumns { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> KeyColumns { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> DateColumns { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> MoneyColumns { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> TextColumns { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> RelatedTables { get; set; } = Array.Empty<string>();
}

public sealed class DashboardAgentSemanticRelationshipDto
{
    public string Topic { get; set; } = "";
    public string Description { get; set; } = "";
    public IReadOnlyList<string> Tables { get; set; } = Array.Empty<string>();
}

public sealed class DashboardAgentQueryPlanDto
{
    public bool InScope { get; set; }
    public string ScopeReason { get; set; } = "";
    public string Intent { get; set; } = "";
    public IReadOnlyList<string> ExtractedTokens { get; set; } = Array.Empty<string>();
    public IReadOnlyList<DashboardAgentCandidateTableDto> CandidateTables { get; set; } = Array.Empty<DashboardAgentCandidateTableDto>();
    public IReadOnlyList<string> DataResolvers { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> MissingResolvers { get; set; } = Array.Empty<string>();
}

public sealed class DashboardAgentCandidateTableDto
{
    public string LogicalName { get; set; } = "";
    public string EntitySetName { get; set; } = "";
    public string Label { get; set; } = "";
    public string Module { get; set; } = "";
    public string ResolverKey { get; set; } = "";
    public string Reason { get; set; } = "";
    public int Score { get; set; }
    public bool HasDataResolver { get; set; }
}

public sealed class DashboardAgentContextSummaryDto
{
    public string Scope { get; set; } = "";
    public int DirectoryTablesCount { get; set; }
    public IReadOnlyList<string> CandidateTables { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> DataSections { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> MissingResolvers { get; set; } = Array.Empty<string>();
    public string LearningReviewReason { get; set; } = "";
}
