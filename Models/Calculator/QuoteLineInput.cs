using System.Text.Json.Serialization;
namespace CotizadorInterno.Web.Models.Calculator;

public sealed class QuoteLineInput
{
        [JsonConverter(typeof(JsonStringEnumConverter))]
    public BusinessType BusinessType { get; set; }
    public string ProductId { get; set; } = "";
    public string ProductDescription { get; set; } = "";

    public decimal CostUnit { get; set; }              // Costo UND
    public decimal MarginPercent { get; set; }         // Margen (%)
    public int ContractMonths { get; set; } = 12;      // Duración (meses)
    public int Quantity { get; set; } = 1;             // Cantidad

    public decimal SuggestedRetailPrice { get; set; }  // cr07a_suggestedretailprice (visible)
    public decimal Acelerador { get; set; }            // cr07a_acelerador (oculto)
}
