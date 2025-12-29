namespace CotizadorInterno.Web.Models.Calculator;

public sealed class QuoteScenarioInput
{
    public string ScenarioName { get; set; } = "Escenario 1";

    public DealType DealType { get; set; } = DealType.ClienteNuevo;

    public bool RequiresProration { get; set; } = false;
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }

    public List<QuoteLineInput> Lines { get; set; } = new();
}
