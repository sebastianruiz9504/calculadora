namespace CotizadorInterno.Web.Services;

public sealed class SiigoOptions
{
    public const string DefaultBaseUrl = "https://api.siigo.com";

    public string BaseUrl { get; set; } = DefaultBaseUrl;
    public string Username { get; set; } = "";
    public string AccessKey { get; set; } = "";
    public string PartnerId { get; set; } = "CotizadorInterno";
    public int PageSize { get; set; } = 100;
    public int MaxCustomerPages { get; set; } = 100;
    public int MaxInvoicePages { get; set; } = 20;
    public int MaxReconciliationPages { get; set; } = 200;
    public int TokenRefreshSkewMinutes { get; set; } = 5;
}
