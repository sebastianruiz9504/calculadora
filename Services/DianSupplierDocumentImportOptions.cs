namespace CotizadorInterno.Web.Services;

public sealed class DianSupplierDocumentImportOptions
{
    public bool Enabled { get; set; } = true;
    public bool DryRun { get; set; }
    public string LocalFilePath { get; set; } = "";
    public string FileName { get; set; } = "Reporte DIAN.xlsx";
}
