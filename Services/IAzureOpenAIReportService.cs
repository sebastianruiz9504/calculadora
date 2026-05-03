using CotizadorInterno.Web.Models.Reportes;

namespace CotizadorInterno.Web.Services;

public interface IAzureOpenAIReportService
{
    Task<ReporteGenerarResult> GenerateReportAsync(
        ReporteGenerarRequest request,
        CancellationToken ct = default);
}
