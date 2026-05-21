using CotizadorInterno.Web.Models.Automation;

namespace CotizadorInterno.Web.Services;

public interface ISiigoAccountCatalogSyncService
{
    Task<AccountCatalogSyncResultDto> SyncAsync(
        DateOnly? startDate = null,
        DateOnly? endDate = null,
        CancellationToken ct = default);
}
