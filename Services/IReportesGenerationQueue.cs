using CotizadorInterno.Web.Models.Reportes;

namespace CotizadorInterno.Web.Services;

public interface IReportesGenerationQueue
{
    ValueTask QueueAsync(ReporteGenerarRequest request, CancellationToken ct = default);
}
