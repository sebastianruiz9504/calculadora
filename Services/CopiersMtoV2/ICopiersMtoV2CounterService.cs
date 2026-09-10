using CotizadorInterno.Web.Models.CopiersMtoV2;

namespace CotizadorInterno.Web.Services.CopiersMtoV2;

public interface ICopiersMtoV2CounterService
{
    Task<CopiersMtoV2CounterReadingDto> GetLatestAsync(string clientId, string equipmentId, CancellationToken ct = default);
    Task<bool> ValidateForMaintenanceAsync(CopiersMtoV2CounterSaveCommand command, CancellationToken ct = default);
    Task<CopiersMtoV2CounterSaveResult> SaveForMaintenanceAsync(CopiersMtoV2CounterSaveCommand command, CancellationToken ct = default);
}
