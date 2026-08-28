namespace CotizadorInterno.Web.Services;

public sealed class DeduccionesIvaImportOptions
{
    public bool UploadToSharePoint { get; set; } = true;
    public string DriveId { get; set; } = "b!m_sba-unH0eJEfpMXYVviH-5vGe2fzxOg7QfAzDfHyfhn2vK3whlSqaNnVgVfGP0";
    public string FolderPath { get; set; } = "deducciones iva";
    public string FileNamePrefix { get; set; } = "deducciones-iva";
    public string WebBaseUrl { get; set; } = "https://digitaltechco.sharepoint.com/sites/2023/Shared%20Documents/deducciones%20iva";
    public long MaxFileBytes { get; set; } = 50 * 1024 * 1024;
}
