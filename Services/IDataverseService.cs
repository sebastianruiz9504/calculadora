using CotizadorInterno.Web.Models;
using CotizadorInterno.Web.Models.Calculator;
using CotizadorInterno.Web.Models.Metricas;
using CotizadorInterno.Web.Models.PortalProveedores;
using CotizadorInterno.Web.Models.Puntajes;
using CotizadorInterno.Web.Models.Renovaciones;

namespace CotizadorInterno.Web.Services;

public interface IDataverseService
{
    Task<IReadOnlyList<ProductLookupItem>> SearchProductsAsync(string query, int top = 12, CancellationToken ct = default);
    Task<IReadOnlyList<ClientLookupItem>> SearchClientsAsync(string query, int top = 12, CancellationToken ct = default);
    Task<IReadOnlyList<RenewalDateLookupItem>> SearchRenewalDatesByClientAsync(string clientId, int top = 250, CancellationToken ct = default);
    Task<CurrentUserInfo?> GetCurrentUserAsync(CancellationToken ct = default);
    Task<RenewalBoardDto> GetRenewalBoardAsync(RenewalPeriodFilter filter, CancellationToken ct = default);
    Task<int> UpdateRenewalRecordsAsync(IReadOnlyList<RenewalRecordUpdateItem> items, CancellationToken ct = default);
    Task<ScoreBoardDto> GetScoreBoardAsync(ScorePeriodFilter filter, CancellationToken ct = default);
    Task VerifyScoreRecordAsync(ScoreVerificationRequest request, CancellationToken ct = default);
    Task<ScoreOfferDownloadResult?> DownloadScoreOfferAsync(string recordId, CancellationToken ct = default);
    Task<MetricsDashboardDto> GetMetricsDashboardAsync(MetricsRangeFilter filter, string? sellerKey = null, CancellationToken ct = default);
    Task<IReadOnlyList<SupplierProviderLookupItem>> GetSupplierCertificateProvidersAsync(DateOnly startDate, DateOnly endDate, string? searchTerm = null, CancellationToken ct = default);
    Task<SupplierCertificateSummaryDto> GetSupplierCertificateSummaryAsync(SupplierCertificateQuery query, CancellationToken ct = default);
    Task<IReadOnlyList<ScenarioStoredDto>> GetScenariosForUserAsync(CancellationToken ct = default);
    Task UpsertScenarioAsync(ScenarioSaveRequest request, CancellationToken ct = default);
      Task DeleteScenarioAsync(string scenarioId, CancellationToken ct = default);

}
