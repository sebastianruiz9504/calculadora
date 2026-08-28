using CotizadorInterno.Web.Models.MesaAyuda;

namespace CotizadorInterno.Web.Services.MesaAyuda;

public interface IMesaAyudaWorkspaceService
{
    Task<MesaAyudaWorkspaceDto> GetWorkspaceAsync(CancellationToken ct = default);
    Task<MesaAyudaTicketDto?> GetTicketAsync(string ticketId, CancellationToken ct = default);
    Task<MesaAyudaTimelineEventDto> CreateInternalMessageAsync(
        MesaAyudaInternalMessageCreate request,
        CancellationToken ct = default);
    Task<MesaAyudaInvestigationResultDto?> GetPersistedInvestigationAsync(
        string idempotencyKey,
        CancellationToken ct = default);
    Task<MesaAyudaTimelineEventDto> SaveInvestigationAsync(
        MesaAyudaInvestigationCreate request,
        CancellationToken ct = default);
}
