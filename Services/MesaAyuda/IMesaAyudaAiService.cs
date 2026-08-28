using CotizadorInterno.Web.Models.MesaAyuda;

namespace CotizadorInterno.Web.Services.MesaAyuda;

public interface IMesaAyudaAiService
{
    bool IsConfigured { get; }

    Task<MesaAyudaInvestigationResultDto> AnalyzeAsync(
        MesaAyudaAiRequest request,
        CancellationToken ct = default);
}
