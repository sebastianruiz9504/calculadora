namespace CotizadorInterno.Web.Models.Calculator;

public sealed class QuoteScenarioResult
{
    public decimal TotalMonthlySale { get; set; }
    public decimal TotalSale { get; set; }

    // Oculto (no mostrar en UI)
    public decimal UtilityRaw { get; set; }
    public decimal UtilityAdjusted { get; set; }

    public decimal Points { get; set; }     // Visible
    public decimal Commission { get; set; } // Visible

    public int ProrationDays { get; set; }
    public decimal ProrationFactor { get; set; } // (days/365) o 1
}
