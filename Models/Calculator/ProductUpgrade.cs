namespace CotizadorInterno.Web.Models.Calculator;

/// <summary>
/// Catalog values captured when an upgrade is selected. CostUnit on the line is
/// the incremental cost, so every downstream consumer uses the same economics.
/// These immutable values must not be refreshed when reopening a saved quote.
/// </summary>
public sealed record ProductUpgrade
{
    public string TargetProductId { get; init; } = "";
    public decimal TargetCostUnit { get; init; }
    public decimal TargetSuggestedRetailPrice { get; init; }
    public string BaseProductId { get; init; } = "";
    public string BaseProductDescription { get; init; } = "";
    public decimal BaseCostUnit { get; init; }
    public decimal BaseSuggestedRetailPrice { get; init; }
}
