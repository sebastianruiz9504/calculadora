using CotizadorInterno.Web.Models.M365;

namespace CotizadorInterno.Web.Services;

public interface IM365SecurityGraphClient
{
    Task<M365SecurityGraphData> CollectSecurityDataAsync(
        string tenantId,
        DateTimeOffset periodStartUtc,
        DateTimeOffset periodEndExclusiveUtc,
        CancellationToken ct = default);
}
