using CotizadorInterno.Web.Models.Reportes;

namespace CotizadorInterno.Web.Services;

public interface IReportesDataverseRepository
{
    Task<ReporteMonthlyInput> LoadMonthlyInputAsync(
        string clienteId,
        string periodo,
        DateOnly startDate,
        DateOnly endExclusiveDate,
        CancellationToken ct = default);

    Task<ReporteHtmlGeneradoRecord> UpsertGeneratedReportAsync(
        ReporteHtmlGeneradoRecord report,
        CancellationToken ct = default);

    Task<IReadOnlyList<ReporteHtmlGeneradoRecord>> ListGeneratedReportsAsync(
        string periodo,
        CancellationToken ct = default);

    Task<ReporteHtmlGeneradoRecord?> GetGeneratedReportAsync(
        string reportId,
        CancellationToken ct = default);
}
