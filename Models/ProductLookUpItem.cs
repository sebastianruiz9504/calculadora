namespace CotizadorInterno.Web.Models;

public sealed class ProductLookupItem
{
    public string Id { get; set; } = "";
    public string Description { get; set; } = "";
    public decimal? PurchasePrice { get; set; }
    public decimal? SuggestedRetailPrice { get; set; }
    public decimal? Acelerador { get; set; } // interno (no mostrar)
}
