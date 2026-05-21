using CotizadorInterno.Web.Models.Automation;

namespace CotizadorInterno.Web.Services;

public interface ICashFlowMatchingService
{
    Task<CashFlowClientPaymentMatchResultDto> MatchClientPaymentsAsync(
        DateOnly? startDate = null,
        DateOnly? endDate = null,
        bool dryRun = false,
        CancellationToken ct = default);
}
