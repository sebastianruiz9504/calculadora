using CotizadorInterno.Web.Models.Automation;

namespace CotizadorInterno.Web.Services;

public interface IDianSupplierDocumentImportService
{
    Task<DianSupplierDocumentImportResultDto> ImportAsync(
        string? localFilePath = null,
        bool dryRun = false,
        CancellationToken ct = default,
        DateOnly? periodStart = null);

    Task<DianSupplierDocumentImportResultDto> ImportAsync(
        Stream workbookStream,
        string sourceFileName,
        bool dryRun = false,
        CancellationToken ct = default,
        DateOnly? periodStart = null);

    Task<DianSupplierDocumentSupplierLookupRunResultDto> ResolvePendingSuppliersAsync(
        DateOnly startDate,
        DateOnly endDate,
        bool dryRun = false,
        CancellationToken ct = default);
}
