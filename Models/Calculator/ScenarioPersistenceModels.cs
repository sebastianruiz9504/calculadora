namespace CotizadorInterno.Web.Models.Calculator;

public sealed class ScenarioLineInput
{
    public int BusinessType { get; set; }
    public string ProductId { get; set; } = "";
    public string ProductDescription { get; set; } = "";
    public decimal CostUnit { get; set; }
    public decimal MarginPercent { get; set; }
    public int ContractMonths { get; set; } = 12;
    public int Quantity { get; set; } = 1;
    public decimal SuggestedRetailPrice { get; set; }
    public decimal Acelerador { get; set; }
}

public sealed class ScenarioResultSnapshot
{
    public decimal Points { get; set; }
    public decimal Commission { get; set; }
    public string? Segment { get; set; }
    public string? ProrationText { get; set; }
    public decimal TotalMonthlySale { get; set; }
    public decimal TotalSale { get; set; }
}

public sealed class ScenarioSaveRequest
{
    public string ScenarioId { get; set; } = "";
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
    public string ScenarioName { get; set; } = "";
    public int DealType { get; set; }
    public bool RequiresProration { get; set; }
    public string? StartDate { get; set; }
    public string? EndDate { get; set; }
    public List<ScenarioLineInput> Lines { get; set; } = new();
    public ScenarioResultSnapshot? LastResult { get; set; }
}