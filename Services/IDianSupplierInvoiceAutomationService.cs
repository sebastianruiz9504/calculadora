using CotizadorInterno.Web.Models.Automation;

namespace CotizadorInterno.Web.Services;

public interface IDianSupplierInvoiceAutomationService
{
    Task<DianSupplierInvoiceAutomationResultDto> ProcessPeriodAsync(
        DateOnly periodStart,
        bool dryRun = false,
        string? supplierKey = null,
        IReadOnlySet<string>? externalKeys = null,
        CancellationToken ct = default);
}
