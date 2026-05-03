using CotizadorInterno.Web.Models.M365;

namespace CotizadorInterno.Web.Services;

public interface IM365SecuritySnapshotService
{
    Task<M365SecuritySnapshotResult> CollectMonthlySnapshotAsync(
        M365SecuritySnapshotRequest request,
        CancellationToken ct = default);
}
