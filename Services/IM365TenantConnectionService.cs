using CotizadorInterno.Web.Models.M365;

namespace CotizadorInterno.Web.Services;

public interface IM365TenantConnectionService
{
    M365ConnectUrlResult BuildConnectUrl(M365ConnectUrlRequest request);
    Task<M365ConsentCallbackResult> HandleConsentCallbackAsync(M365ConsentCallbackRequest request, CancellationToken ct = default);
    Task<M365TestConnectionResult> TestConnectionAsync(M365TestConnectionRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<M365ConnectedClientItem>> ListConnectedClientsAsync(CancellationToken ct = default);
}
