using CotizadorInterno.Web.Models.Automation;
using CotizadorInterno.Web.Models.Conciliacion;
using CotizadorInterno.Web.Models.Dashboard;
using CotizadorInterno.Web.Models.Reconciliation;

namespace CotizadorInterno.Web.Services;

public interface ISiigoService
{
    Task<IReadOnlyList<SiigoCustomerLookupItemDto>> GetCustomersAsync(CancellationToken ct = default);

    Task<IReadOnlyList<SiigoCustomerLookupItemDto>> SearchCustomersAsync(string query, int top = 12, CancellationToken ct = default);

    Task<ConciliacionSiigoOpenPurchaseSearchResultDto> GetOpenPurchasesAsync(
        string? supplierId,
        string? supplierQuery,
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken ct = default);

    Task<SiigoInvoiceSearchResultDto> GetInvoicesAsync(
        string? customerId,
        string? customerQuery,
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken ct = default);

    Task<IReadOnlyList<SiigoInvoiceRowDto>> GetInvoicesByDateRangeAsync(
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken ct = default);

    Task<SiigoInvoiceRowDto?> GetInvoiceByIdAsync(
        string invoiceId,
        CancellationToken ct = default) =>
        Task.FromResult<SiigoInvoiceRowDto?>(null);

    Task<SiigoFinancialReconciliationData> GetFinancialReconciliationDocumentsAsync(
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken ct = default);

    Task<SiigoFinancialReconciliationData> GetBillingDocumentsAsync(
        DateOnly startInclusive,
        DateOnly endExclusive,
        CancellationToken ct = default);

    Task<IReadOnlyList<SiigoReconciliationPurchase>> GetPurchasesByDateRangeAsync(
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken ct = default);

    Task<SiigoReconciliationPurchase?> GetPurchaseByIdAsync(
        string purchaseId,
        CancellationToken ct = default) =>
        Task.FromResult<SiigoReconciliationPurchase?>(null);

    Task<decimal?> GetAccountsPayableBalanceAsync(
        string supplierIdentification,
        string duePrefix,
        int dueConsecutive,
        int dueQuote = 1,
        CancellationToken ct = default) =>
        Task.FromResult<decimal?>(null);

    Task<IReadOnlyList<SiigoObservedAccountDto>> GetObservedAccountCatalogAsync(
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken ct = default);

    Task<SiigoInvoiceDownloadResult> DownloadInvoicePdfsAsync(
        IReadOnlyList<SiigoInvoiceDownloadItemDto> invoices,
        CancellationToken ct = default);

    Task<IReadOnlyList<SiigoTaxLookupDto>> GetTaxesAsync(CancellationToken ct = default);

    Task<IReadOnlyList<SiigoDocumentTypeLookupDto>> GetDocumentTypesAsync(
        string type,
        CancellationToken ct = default);

    Task<IReadOnlyList<SiigoPaymentTypeLookupDto>> GetPaymentTypesAsync(
        string documentType,
        CancellationToken ct = default);

    Task<SiigoCustomerLookupItemDto> CreateCustomerAsync(
        object payload,
        string? idempotencyKey = null,
        CancellationToken ct = default);

    Task<SiigoVoucherCreateResultDto> CreatePurchaseAsync(
        object payload,
        string? idempotencyKey = null,
        CancellationToken ct = default);

    Task<SiigoVoucherCreateResultDto> CreatePurchaseSupportDocumentAsync(
        object payload,
        string? idempotencyKey = null,
        CancellationToken ct = default);

    Task<SiigoVoucherCreateResultDto> CreatePaymentReceiptAsync(
        object payload,
        string? idempotencyKey = null,
        CancellationToken ct = default);

    Task<SiigoVoucherCreateResultDto?> FindPaymentReceiptByObservationAsync(
        string uniqueObservation,
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken ct = default) =>
        Task.FromResult<SiigoVoucherCreateResultDto?>(null);

    Task<SiigoVoucherCreateResultDto?> FindJournalByObservationAsync(
        string uniqueObservation,
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken ct = default) =>
        Task.FromResult<SiigoVoucherCreateResultDto?>(null);

    Task<SiigoVoucherCreateResultDto> CreateVoucherAsync(
        object payload,
        string? idempotencyKey = null,
        CancellationToken ct = default);

    Task<SiigoVoucherCreateResultDto> CreateJournalAsync(
        object payload,
        string? idempotencyKey = null,
        CancellationToken ct = default);
}
