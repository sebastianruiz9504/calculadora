using CotizadorInterno.Web.Models.Automation;
using CotizadorInterno.Web.Models.Conciliacion;

namespace CotizadorInterno.Web.Services;

public interface IDeduccionesIvaImportHistoryService
{
    Task RecordAsync(
        string originalFileName,
        DeduccionesIvaSharePointUploadResult upload,
        DateOnly periodStart,
        string importedBy,
        DianSupplierDocumentImportResultDto import,
        CancellationToken ct = default);

    Task<IReadOnlyList<DeduccionesIvaImportHistoryEntryDto>> GetHistoryAsync(
        int top = 25,
        CancellationToken ct = default);

    Task<DianSupplierDocumentImportResultDto> ReprocessLatestAsync(
        CancellationToken ct = default) =>
        throw new NotSupportedException("Este historico no implementa reproceso.");
}
