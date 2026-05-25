using CotizadorInterno.Web.Models.Automation;

namespace CotizadorInterno.Web.Services;

public interface IDianSupplierDocumentImportService
{
    Task<DianSupplierDocumentImportResultDto> ImportAsync(
        string? localFilePath = null,
        bool dryRun = false,
        CancellationToken ct = default);
}
