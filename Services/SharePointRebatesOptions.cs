namespace CotizadorInterno.Web.Services;

public sealed class SharePointRebatesOptions
{
    public const string SectionName = "SharePointRebates";

    public bool Enabled { get; set; } = true;
    public string DriveId { get; set; } = "";
    public string ItemId { get; set; } = "";
    public string FileName { get; set; } = "Facturacion DIGITAL TECH.xlsx";
    public string TableName { get; set; } = "Rebates";
    public string LocalFilePath { get; set; } = "";
}
