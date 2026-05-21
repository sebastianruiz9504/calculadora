using CotizadorInterno.Web.Models.Automation;

namespace CotizadorInterno.Web.Services;

public interface ICashFlowImportService
{
    Task<CashFlowImportResultDto> ImportAsync(bool dryRun = false, CancellationToken ct = default);
}
