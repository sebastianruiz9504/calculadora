using CotizadorInterno.Web.Models;

namespace CotizadorInterno.Web.Services;

public interface IProvisioningRequestStore
{
    Task SavePendingAsync(ProvisioningStoredRequest request, CancellationToken ct = default);
    Task<ProvisioningStoredRequest?> GetAsync(string requestId, CancellationToken ct = default);
    Task<IReadOnlyList<ProvisioningStoredRequest>> GetApprovedPendingHardwareSyncAsync(CancellationToken ct = default);
    Task MarkFlowDispatchFailedAsync(string requestId, string message, CancellationToken ct = default);
    Task<ProvisioningStoredRequest> ApplyApprovalAsync(ProvisioningApprovalCallbackInput input, CancellationToken ct = default);
    Task MarkHardwareSyncResultAsync(string requestId, ProvisioningHardwareSyncStatus status, int importedCount, string message, CancellationToken ct = default);
}
