using CotizadorInterno.Web.Models.M365;

namespace CotizadorInterno.Web.Services;

public interface IM365SecuritySnapshotRepository
{
    Task<M365TenantConnectionRecord?> FindConnectionForSnapshotAsync(
        string clienteId,
        string tenantIdOrHint,
        CancellationToken ct = default);

    Task<M365SecuritySnapshotRecord> UpsertSnapshotAsync(
        M365SecuritySnapshotRecord snapshot,
        CancellationToken ct = default);
}
