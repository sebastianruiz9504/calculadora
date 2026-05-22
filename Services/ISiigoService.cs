using CotizadorInterno.Web.Models.Automation;
using CotizadorInterno.Web.Models.Conciliacion;
using CotizadorInterno.Web.Models.Dashboard;
using CotizadorInterno.Web.Models.Reconciliation;

namespace CotizadorInterno.Web.Services;

public interface ISiigoService
{
    Task<IReadOnlyList<SiigoCustomerLookupItemDto>> GetCustomersAsync(CancellationToken ct = default);

    Task<IReadOnlyList<SiigoCustomerLookupItemDto>> SearchCustomersAsync(string query, int top = 12, CancellationToken ct = default);

    Task<SiigoInvoiceSearchResultDto> GetInvoicesAsync(
        string? customerId,
        string? customerQuery,
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken ct = default);

    Task<SiigoFinancialReconciliationData> GetFinancialReconciliationDocumentsAsync(
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken ct = default);

    Task<IReadOnlyList<SiigoObservedAccountDto>> GetObservedAccountCatalogAsync(
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken ct = default);

    Task<SiigoInvoiceDownloadResult> DownloadInvoicePdfsAsync(
        IReadOnlyList<SiigoInvoiceDownloadItemDto> invoices,
        CancellationToken ct = default);

    Task<SiigoVoucherCreateResultDto> CreateVoucherAsync(
        object payload,
        string? idempotencyKey = null,
        CancellationToken ct = default);
}
