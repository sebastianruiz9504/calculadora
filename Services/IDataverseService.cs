using CotizadorInterno.Web.Models;
using CotizadorInterno.Web.Models.Calculator;
using CotizadorInterno.Web.Models.Dashboard;
using CotizadorInterno.Web.Models.Metricas;
using CotizadorInterno.Web.Models.Nomina;
using CotizadorInterno.Web.Models.Permissions;
using CotizadorInterno.Web.Models.PortalProveedores;
using CotizadorInterno.Web.Models.Puntajes;
using CotizadorInterno.Web.Models.RH;
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
    Task<ScoreVerificationDetailDto> GetScoreVerificationDetailAsync(string recordId, ScorePeriodFilter filter, CancellationToken ct = default);
    Task<ScoreVerificationComputedResultDto> RecalculateScoreRecordAsync(ScoreVerificationRequest request, CancellationToken ct = default);
    Task<ScoreVerificationSaveResultDto> VerifyScoreRecordAsync(ScoreVerificationRequest request, CancellationToken ct = default);
    Task<ScoreMonthClosePreviewResultDto> PreviewScoreMonthCloseAsync(ScorePeriodFilter filter, CancellationToken ct = default);
    Task<ScoreMonthCloseResultDto> CloseScoreMonthAsync(ScoreMonthCloseRequest request, CancellationToken ct = default);
    Task<ScoreMonthUndoResultDto> UndoScoreMonthCloseAsync(ScorePeriodFilter filter, CancellationToken ct = default);
    Task<ScoreOfferDownloadResult?> DownloadScoreOfferAsync(string recordId, CancellationToken ct = default);
    Task<NominaPreviewResultDto> PreviewNominaAsync(NominaPreviewRequest request, CancellationToken ct = default);
    Task<NominaConfirmResultDto> ConfirmNominaAsync(NominaConfirmRequest request, CancellationToken ct = default);
    Task<RhTableDataResultDto> GetRhTableDataAsync(string tableKey, CancellationToken ct = default);
    Task<RhSaveResultDto> SaveRhRecordAsync(RhSaveRequest request, CancellationToken ct = default);
    Task<RhFileUploadResultDto> UploadRhFieldFileAsync(string tableKey, string recordId, string fieldName, string fileName, string contentType, byte[] content, CancellationToken ct = default);
    Task<RhFileDownloadResult?> DownloadRhFieldFileAsync(string tableKey, string recordId, string fieldName, CancellationToken ct = default);
    Task<VacationRequestContextDto> GetVacationRequestContextAsync(CancellationToken ct = default);
    Task<VacationRequestSubmitResultDto> SubmitVacationRequestAsync(VacationRequestSubmitInput input, CancellationToken ct = default);
    Task<string> GetVacationRequestDocumentHtmlAsync(string recordId, bool autoPrint = false, CancellationToken ct = default);
    Task<MetricsDashboardDto> GetMetricsDashboardAsync(MetricsRangeFilter filter, MetricsViewMode view, string? sellerKey = null, CancellationToken ct = default);
    Task<BillingDashboardDto> GetBillingDashboardAsync(int year, BillingPeriodKind periodKind, int? periodValue = null, CancellationToken ct = default);
    Task<TaxesDashboardDto> GetTaxesDashboardAsync(int year, BillingPeriodKind periodKind, int? periodValue = null, CancellationToken ct = default);
    Task<PnlDashboardDto> GetPnlDashboardAsync(int year, int? monthCutoff = null, string? vertical = null, CancellationToken ct = default);
    Task<PnlCellDetailDto> GetPnlCellDetailAsync(int year, int? monthCutoff, string? vertical, string rowKey, int? cellMonth = null, CancellationToken ct = default);
    Task<PnlDetailRecordUpdateResultDto> UpdatePnlDetailRecordAsync(PnlDetailRecordUpdateRequestDto request, CancellationToken ct = default);
    Task<PortfolioDashboardDto> GetPortfolioDashboardAsync(CancellationToken ct = default);
    Task<CopiersDashboardDto> GetCopiersDashboardAsync(CancellationToken ct = default);
    Task<IReadOnlyList<SupplierProviderLookupItem>> GetSupplierCertificateProvidersAsync(DateOnly startDate, DateOnly endDate, string? searchTerm = null, CancellationToken ct = default);
    Task<SupplierCertificateSummaryDto> GetSupplierCertificateSummaryAsync(SupplierCertificateQuery query, CancellationToken ct = default);
    Task<IReadOnlyList<ScenarioStoredDto>> GetScenariosForUserAsync(CancellationToken ct = default);
    Task UpsertScenarioAsync(ScenarioSaveRequest request, CancellationToken ct = default);
    Task DeleteScenarioAsync(string scenarioId, CancellationToken ct = default);
    Task<IReadOnlyList<EmployeeModulePermissionRowDto>> GetEmployeeModulePermissionsAsync(CancellationToken ct = default);
    Task<EmployeeModulePermissionSaveResult> SaveEmployeeModulePermissionsAsync(EmployeeModulePermissionSaveRequest request, CancellationToken ct = default);
}
