namespace CotizadorInterno.Web.Models.Calculator;

public sealed class QuoteProposalGenerationInput
{
    public QuoteScenarioInput Scenario { get; set; } = new();
    public QuoteScenarioResult Result { get; set; } = new();
    public string PreparedByName { get; set; } = "";
    public string PreparedByEmail { get; set; } = "";
    public DateTimeOffset GeneratedAt { get; set; }
}
