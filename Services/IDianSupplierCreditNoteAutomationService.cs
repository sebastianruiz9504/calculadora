using CotizadorInterno.Web.Models.Automation;

namespace CotizadorInterno.Web.Services;

public interface IDianSupplierCreditNoteAutomationService
{
    Task<DianSupplierCreditNoteAutomationResultDto> ProcessPeriodAsync(
        DateOnly periodStart,
        bool dryRun = false,
        IReadOnlySet<string>? externalKeys = null,
        CancellationToken ct = default);
}
