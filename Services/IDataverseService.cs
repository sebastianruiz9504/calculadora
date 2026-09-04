using CotizadorInterno.Web.Models;
using CotizadorInterno.Web.Models.Automation;
using CotizadorInterno.Web.Models.Calculator;
using CotizadorInterno.Web.Models.Conciliacion;
using CotizadorInterno.Web.Models.Contracts;
using CotizadorInterno.Web.Models.CuentasCobro;
using CotizadorInterno.Web.Models.Copiers;
using CotizadorInterno.Web.Models.Dashboard;
using CotizadorInterno.Web.Models.Envios;
using CotizadorInterno.Web.Models.Hardware;
using CotizadorInterno.Web.Models.Licenciamiento;
using CotizadorInterno.Web.Models.MesaAyuda;
using CotizadorInterno.Web.Models.Metricas;
using CotizadorInterno.Web.Models.Nomina;
using CotizadorInterno.Web.Models.Permissions;
using CotizadorInterno.Web.Models.PlanRio;
using CotizadorInterno.Web.Models.PortalProveedores;
using CotizadorInterno.Web.Models.PublicDataExport;
using CotizadorInterno.Web.Models.Puntajes;
using CotizadorInterno.Web.Models.Reconciliation;
using CotizadorInterno.Web.Models.RebatesInversiones;
using CotizadorInterno.Web.Models.RegistroPagosClientes;
using CotizadorInterno.Web.Models.RH;
using CotizadorInterno.Web.Models.Renovaciones;
using CotizadorInterno.Web.Models.SoporteCloud;
using CotizadorInterno.Web.Models.Tasks;

namespace CotizadorInterno.Web.Services;

public interface IDataverseService
{
    Task<IReadOnlyList<ProductLookupItem>> SearchProductsAsync(string query, int top = 12, CancellationToken ct = default);
    Task<ProductLookupItem> EnsureCalculatorProductAsync(ProductCreateInput input, CancellationToken ct = default);
    Task<IReadOnlyList<ClientLookupItem>> SearchClientsAsync(string query, int top = 12, CancellationToken ct = default);
    Task<IReadOnlyList<SystemUserLookupItem>> SearchSystemUsersAsync(string query, int top = 12, CancellationToken ct = default, bool includeAllWhenEmpty = false);
    Task<SystemUserLookupItem?> GetSystemUserAsync(string systemUserId, CancellationToken ct = default);
    Task<IReadOnlyList<RenewalDateLookupItem>> SearchRenewalDatesByClientAsync(string clientId, int top = 250, CancellationToken ct = default);
    Task<CurrentUserInfo?> GetCurrentUserAsync(CancellationToken ct = default);
    Task<RenewalBoardDto> GetRenewalBoardAsync(RenewalPeriodFilter filter, CancellationToken ct = default);
    Task<int> UpdateRenewalRecordsAsync(IReadOnlyList<RenewalRecordUpdateItem> items, CancellationToken ct = default);
    Task<RenewalScenarioCreateResultDto> CreateRenewalScenarioAsync(IReadOnlyList<RenewalRecordUpdateItem> items, CancellationToken ct = default);
    Task<ScoreBoardDto> GetScoreBoardAsync(ScorePeriodFilter filter, CancellationToken ct = default, ScoreBusinessFilter businessFilter = ScoreBusinessFilter.NewBusiness);
    Task<ScoreVerificationDetailDto> GetScoreVerificationDetailAsync(string recordId, ScorePeriodFilter filter, CancellationToken ct = default);
    Task<ScoreVerificationComputedResultDto> RecalculateScoreRecordAsync(ScoreVerificationRequest request, CancellationToken ct = default);
    Task<ScoreVerificationSaveResultDto> VerifyScoreRecordAsync(ScoreVerificationRequest request, CancellationToken ct = default);
    Task<ScoreRecordDeleteResultDto> DeleteScoreRecordAsync(string recordId, CancellationToken ct = default);
    Task<ScoreMoveToRenewalResultDto> MoveScoreBusinessToRenewalAsync(ScoreMoveToRenewalRequest request, CancellationToken ct = default);
    Task<ScoreMonthClosePreviewResultDto> PreviewScoreMonthCloseAsync(ScorePeriodFilter filter, CancellationToken ct = default);
    Task<ScoreMonthCloseResultDto> CloseScoreMonthAsync(ScoreMonthCloseRequest request, CancellationToken ct = default);
    Task<ScoreMonthUndoResultDto> UndoScoreMonthCloseAsync(ScorePeriodFilter filter, CancellationToken ct = default);
    Task<ScoreOfferDownloadResult?> DownloadScoreOfferAsync(string recordId, CancellationToken ct = default);
    Task<NominaPreviewResultDto> PreviewNominaAsync(NominaPreviewRequest request, CancellationToken ct = default);
    Task<NominaConfirmResultDto> ConfirmNominaAsync(NominaConfirmRequest request, CancellationToken ct = default);
    Task<NominaClosedPeriodDto> GetNominaClosedPeriodAsync(string periodKey, CancellationToken ct = default);
    Task<NominaClosedVerticalsSaveResultDto> SaveNominaClosedVerticalsAsync(NominaClosedVerticalsSaveRequest request, CancellationToken ct = default);
    Task<NominaPaymentProofUploadResultDto> UploadNominaPaymentProofAsync(string recordId, string fileName, string contentType, byte[] content, string paymentType = "nomina", CancellationToken ct = default);
    Task<RhFileDownloadResult?> DownloadNominaPaymentProofAsync(string recordId, string paymentType = "nomina", CancellationToken ct = default);
    Task<NominaPaymentHistoryDto> GetNominaPaymentHistoryAsync(int year, CancellationToken ct = default);
    Task<PrimaLegalBoardDto> GetPrimaLegalBoardAsync(int year, int semester, CancellationToken ct = default);
    Task<PrimaLegalLiquidationSaveResultDto> SavePrimaLegalLiquidationAsync(PrimaLegalLiquidationRequest request, CancellationToken ct = default);
    Task<RhTableDataResultDto> GetRhTableDataAsync(string tableKey, CancellationToken ct = default);
    Task<RhFileDownloadResult> ExportRhTableAsync(string tableKey, string? employeeId = null, CancellationToken ct = default);
    Task<RhSaveResultDto> SaveRhRecordAsync(RhSaveRequest request, CancellationToken ct = default);
    Task<RhFileUploadResultDto> UploadRhFieldFileAsync(string tableKey, string recordId, string fieldName, string fileName, string contentType, byte[] content, CancellationToken ct = default);
    Task<RhFileDownloadResult?> DownloadRhFieldFileAsync(string tableKey, string recordId, string fieldName, CancellationToken ct = default);
    Task<VacationRequestContextDto> GetVacationRequestContextAsync(CancellationToken ct = default);
    Task<VacationRequestSubmitResultDto> SubmitVacationRequestAsync(VacationRequestSubmitInput input, CancellationToken ct = default);
    Task<string> GetVacationRequestDocumentHtmlAsync(string recordId, bool autoPrint = false, CancellationToken ct = default);
    Task<MetricsDashboardDto> GetMetricsDashboardAsync(MetricsRangeFilter filter, MetricsViewMode view, MetricsPeriodGranularity period, string? sellerKey = null, CancellationToken ct = default);
    Task<BillingDashboardDto> GetBillingDashboardAsync(int year, BillingPeriodKind periodKind, int? periodValue = null, CancellationToken ct = default);
    Task<IReadOnlyList<ReconciliationDataverseBillingRow>> GetFinancialReconciliationBillingRowsAsync(DateOnly startInclusive, DateOnly endExclusive, CancellationToken ct = default);
    Task<IReadOnlyList<ReconciliationDataverseCreditNoteRow>> GetFinancialReconciliationCreditNoteRowsAsync(DateOnly startInclusive, DateOnly endExclusive, CancellationToken ct = default);
    Task<IReadOnlyList<ReconciliationDataverseExpenseRow>> GetFinancialReconciliationExpenseRowsAsync(DateOnly startInclusive, DateOnly endExclusive, CancellationToken ct = default);
    Task<FinancialReconciliationCorrectionResult> ApplyFinancialReconciliationBillingCorrectionsAsync(
        DateOnly startInclusive,
        DateOnly endExclusive,
        IReadOnlyList<ReconciliationDataverseBillingRow> dataverseBilling,
        IReadOnlyList<ReconciliationDataverseCreditNoteRow> dataverseCreditNotes,
        SiigoFinancialReconciliationData siigo,
        CancellationToken ct = default);
    Task<FinancialReconciliationCorrectionResult> CreateFinancialReconciliationMissingBillingInvoicesAsync(
        DateOnly startInclusive,
        DateOnly endExclusive,
        IReadOnlyList<ReconciliationDataverseBillingRow> dataverseBilling,
        IReadOnlyList<ReconciliationDataverseCreditNoteRow> dataverseCreditNotes,
        SiigoFinancialReconciliationData siigo,
        IReadOnlyList<string> invoiceKeys,
        CancellationToken ct = default);
    Task<FinancialReconciliationCorrectionResult> DeleteFinancialReconciliationBillingRowsAsync(
        IReadOnlyList<string> recordIds,
        CancellationToken ct = default);
    Task<AccountCatalogSyncResultDto> UpsertSiigoAccountCatalogAsync(
        DateOnly startDate,
        DateOnly endDate,
        IReadOnlyList<SiigoObservedAccountDto> accounts,
        CancellationToken ct = default);
    Task<ExpenseAccountingRuleApplyResultDto> ApplyExpenseAccountingRulesAsync(
        DateOnly startDate,
        DateOnly endDate,
        string movementType = "Compra",
        bool overwrite = false,
        CancellationToken ct = default,
        IReadOnlySet<string>? externalKeys = null);
    Task<ExpenseAccountingTemplateApplyResultDto> ApplyExpenseAccountingTemplatesAsync(
        DateOnly startDate,
        DateOnly endDate,
        string movementType = "Compra",
        bool overwrite = false,
        bool dryRun = false,
        CancellationToken ct = default);

    Task<CashFlowDataverseUpsertResultDto> UpsertCashFlowRowsAsync(
        IReadOnlyList<CashFlowImportRowDto> rows,
        bool dryRun = false,
        CancellationToken ct = default);
    Task<DianSupplierDocumentDataverseUpsertResultDto> UpsertDianSupplierDocumentRowsAsync(
        IReadOnlyList<DianSupplierDocumentImportRowDto> rows,
        bool dryRun = false,
        CancellationToken ct = default);
    Task<IReadOnlyList<DianSupplierDocumentImportRowDto>> GetDianSupplierDocumentRowsForSupplierLookupAsync(
        DateOnly startDate,
        DateOnly endDate,
        bool onlyPending = true,
        CancellationToken ct = default);
    Task<DianSupplierDocumentSiigoSupplierResolutionResultDto> ResolveDianSupplierDocumentSiigoSuppliersAsync(
        IReadOnlyList<DianSupplierDocumentImportRowDto> rows,
        IReadOnlyList<DianSupplierDocumentResolvedSupplierDto> suppliers,
        bool dryRun = false,
        CancellationToken ct = default);
    Task<CashFlowClientPaymentMatchResultDto> MatchCashFlowClientPaymentsAsync(
        DateOnly startDate,
        DateOnly endDate,
        bool dryRun = false,
        decimal differenceTolerance = 2000m,
        CancellationToken ct = default);
    Task<ConciliacionInvoiceSearchResultDto> SearchConciliacionDataverseInvoicesAsync(
        ConciliacionInvoiceSearchRequest request,
        CancellationToken ct = default);
    Task<ConciliacionActionResultDto> AssignConciliacionClientPaymentInvoiceAsync(
        ConciliacionAssignInvoiceRequest request,
        CancellationToken ct = default);
    Task<ConciliacionCashFlowRowDto> GetConciliacionCashFlowMovementAsync(
        ConciliacionCashFlowAccountingVoucherRequest request,
        CancellationToken ct = default);
    Task<IReadOnlyList<ConciliacionCashFlowRowDto>> GetConciliacionCashFlowMovementsAsync(
        ConciliacionCashFlowAccountingVoucherRequest request,
        CancellationToken ct = default);
    Task<ConciliacionCashFlowActionResultDto> UpdateConciliacionCashFlowAccountingAccountAsync(
        ConciliacionCashFlowAccountingAccountRequest request,
        CancellationToken ct = default);
    Task<ConciliacionCashFlowActionResultDto> MarkConciliacionCashFlowAccountingVoucherSiigoResultAsync(
        ConciliacionCashFlowAccountingVoucherRequest request,
        bool success,
        string message,
        string siigoId = "",
        string siigoName = "",
        string responseJson = "",
        string payloadJson = "",
        CancellationToken ct = default);
    Task<ConciliacionActionResultDto> UpdateConciliacionDianSupplierDocumentClassificationAsync(
        ConciliacionDianClassificationRequest request,
        CancellationToken ct = default);
    Task<ConciliacionDianSupplierInvoiceRowDto> GetConciliacionDianSupplierDocumentAsync(
        string recordId,
        CancellationToken ct = default);
    Task<IReadOnlyList<ConciliacionDianSupplierInvoiceRowDto>> GetConciliacionDianSupplierDocumentsForAutomationAsync(
        DateOnly startInclusive,
        DateOnly endExclusive,
        CancellationToken ct = default);
    Task<IReadOnlyList<ConciliacionDianSupplierInvoiceRowDto>> GetConciliacionDianSupplierDocumentsForHistoryAsync(
        DateOnly startInclusive,
        DateOnly endExclusive,
        CancellationToken ct = default) =>
        GetConciliacionDianSupplierDocumentsForAutomationAsync(startInclusive, endExclusive, ct);
    Task<bool> TryClaimConciliacionDianSupplierDocumentForSiigoAsync(
        string recordId,
        string concurrencyToken,
        CancellationToken ct = default);
    Task<bool> TryClaimConciliacionDianSupplierCreationAsync(
        string recordId,
        string concurrencyToken,
        CancellationToken ct = default);
    Task<ConciliacionDianActionResultDto> MarkConciliacionDianSupplierAsync(
        string recordId,
        string siigoSupplierId,
        string siigoSupplierName,
        string message,
        CancellationToken ct = default);
    Task<ConciliacionDianActionResultDto> ClearConciliacionDianSupplierAsync(
        string recordId,
        string message,
        CancellationToken ct = default);
    Task<ConciliacionDianActionResultDto> MarkConciliacionDianSupplierDocumentSiigoResultAsync(
        string recordId,
        bool success,
        string message,
        string siigoId = "",
        string siigoName = "",
        string responseJson = "",
        bool ownsProcessingClaim = false,
        bool releaseProcessingClaim = false,
        CancellationToken ct = default);
    Task<ConciliacionDianActionResultDto> ConfirmConciliacionDianSupplierDocumentAmbiguousWriteAsync(
        string recordId,
        string siigoId,
        string siigoName,
        string message,
        string responseJson = "",
        CancellationToken ct = default) =>
        throw new NotSupportedException("Esta implementacion de Dataverse no admite confirmar escrituras ambiguas.");
    Task<ConciliacionSiigoSendPreparedDto> PrepareConciliacionClientPaymentSiigoSendAsync(
        string recordId,
        CancellationToken ct = default,
        IReadOnlyList<SiigoTaxLookupDto>? siigoTaxes = null,
        SiigoDocumentTypeLookupDto? journalDocument = null,
        IReadOnlyList<SiigoInvoiceRowDto>? siigoInvoices = null);
    Task<ConciliacionActionResultDto> MarkConciliacionClientPaymentSiigoSendResultAsync(
        string recordId,
        bool success,
        string message,
        string siigoId = "",
        string siigoName = "",
        string responseJson = "",
        string statusOverride = "",
        CancellationToken ct = default);
    Task<BillingInvoicesTableDto> GetBillingInvoicesAsync(CancellationToken ct = default);
    Task<BillingInvoicesTableDto> GetBillingInvoicesPageAsync(int year, int month, int page = 1, int pageSize = 50, bool duplicatesOnly = false, CancellationToken ct = default);
    Task<BillingCreditNotesTableDto> GetBillingCreditNotesAsync(CancellationToken ct = default);
    Task<BillingInvoiceSaveResultDto> SaveBillingInvoiceAsync(BillingInvoiceSaveRequestDto request, CancellationToken ct = default);
    Task<BillingInvoicesDeleteResultDto> DeleteBillingInvoicesAsync(BillingInvoicesDeleteRequestDto request, CancellationToken ct = default);
    Task<BillingInvoicesContractTypeUpdateResultDto> UpdateBillingInvoicesContractTypeAsync(BillingInvoicesContractTypeUpdateRequestDto request, CancellationToken ct = default);
    Task<TaxesDashboardDto> GetTaxesDashboardAsync(TaxesDashboardRequestDto request, CancellationToken ct = default);
    Task<DashboardAgentExpensesDto> GetDashboardAgentExpensesAsync(DateOnly startInclusive, DateOnly endExclusive, CancellationToken ct = default);
    Task<DashboardAgentFeedbackResultDto> CreateDashboardAgentFeedbackAsync(DashboardAgentFeedbackRequestDto request, CancellationToken ct = default);
    Task<DashboardAgentLearningBoardDto> GetDashboardAgentLearningFeedbackAsync(CancellationToken ct = default);
    Task<DashboardAgentFeedbackResultDto> UpdateDashboardAgentLearningFeedbackStatusAsync(DashboardAgentLearningStatusUpdateRequestDto request, CancellationToken ct = default);
    Task<BusinessDashboardDto> GetBusinessDashboardAsync(CancellationToken ct = default);
    Task<BusinessBillingDashboardDto> GetBusinessBillingDashboardAsync(DateOnly? startDate, DateOnly? endDate, string? granularity, CancellationToken ct = default);
    Task<CloudBillingCurrentMonthDashboardDto> GetCloudBillingCurrentMonthDashboardAsync(CancellationToken ct = default);
    Task<decimal> GetCloudProductsTotalBusinessUsdAsync(CancellationToken ct = default);
    Task<YtdDashboardDto> GetYtdDashboardAsync(int year, CancellationToken ct = default);
    Task<YtdRecordUpdateResultDto> UpdateYtdBillingRecordAsync(YtdBillingRecordUpdateRequestDto request, CancellationToken ct = default);
    Task<YtdRecordsUpdateResultDto> UpdateYtdRecordsAsync(YtdRecordsUpdateRequestDto request, CancellationToken ct = default);
    Task<PnlDashboardDto> GetPnlDashboardAsync(int year, int? monthCutoff = null, string? vertical = null, CancellationToken ct = default);
    Task<PnlCellDetailDto> GetPnlCellDetailAsync(int year, int? monthCutoff, string? vertical, string rowKey, int? cellMonth = null, CancellationToken ct = default);
    Task<PnlDetailRecordUpdateResultDto> UpdatePnlDetailRecordAsync(PnlDetailRecordUpdateRequestDto request, CancellationToken ct = default);
    Task<LicenciamientoDashboardDto> GetLicenciamientoDashboardAsync(int year, int? month = null, CancellationToken ct = default);
    Task<UtilityDashboardDto> GetUtilityDashboardAsync(CancellationToken ct = default);
    Task<UtilityAssignmentResultDto> AssignUtilityRowAsync(UtilityAssignmentRequestDto request, CancellationToken ct = default);
    Task<PortfolioDashboardDto> GetPortfolioDashboardAsync(CancellationToken ct = default);
    Task<PortfolioDashboardDto> GetPortfolioDashboardSummaryAsync(CancellationToken ct = default);
    Task<AccountStatementDto> GetAccountStatementAsync(string clientId, string? clientName = null, CancellationToken ct = default);
    Task<CopiersDashboardDto> GetCopiersDashboardAsync(CancellationToken ct = default);
    Task<CopiersClientInvoicesDetailDto> GetCopiersClientInvoicesAsync(string clientId, string? clientName = null, CancellationToken ct = default);
    Task<CopiersLineEquipmentAssignmentDetailDto> GetCopiersLineEquipmentAssignmentAsync(string lineId, string? clientId = null, CancellationToken ct = default);
    Task<CopiersLineEquipmentAssignmentSaveResultDto> SaveCopiersLineEquipmentAssignmentAsync(CopiersLineEquipmentAssignmentSaveRequestDto request, CancellationToken ct = default);
    Task<BillingClientReportDto> GetBillingClientReportAsync(string clientId, string? clientName = null, CancellationToken ct = default);
    Task<CopiersRecordSaveResultDto> SaveCopiersRecordAsync(CopiersRecordSaveRequestDto request, CancellationToken ct = default);
    Task<CopiersEquipmentDashboardDto> GetCopiersEquipmentDashboardAsync(CancellationToken ct = default);
    Task<CopiersEquipmentDetailDto> GetCopiersEquipmentDetailAsync(string equipmentId, CancellationToken ct = default);
    Task<CopiersEquipmentMovementsDashboardDto> GetCopiersEquipmentMovementsDashboardAsync(CancellationToken ct = default);
    Task<CopiersCommercialInventoryDto> GetCopiersCommercialInventoryAsync(CancellationToken ct = default);
    Task<CopiersEquipmentInventoryDto> GetCopiersEquipmentInventoryAsync(string? clientId, string? clientName, CancellationToken ct = default);
    Task<CopiersCountersDashboardDto> GetCopiersCountersDashboardAsync(int year, int month, string? clientId = null, string? clientName = null, CancellationToken ct = default);
    Task<CopiersEquipmentAssignmentResultDto> SaveCopiersEquipmentAssignmentAsync(CopiersEquipmentAssignmentRequestDto request, CancellationToken ct = default);
    Task<CopiersEquipmentBackupAssignmentResultDto> SaveCopiersEquipmentBackupAssignmentAsync(CopiersEquipmentBackupAssignmentRequestDto request, CancellationToken ct = default);
    Task<CopiersEquipmentSaveResultDto> SaveCopiersEquipmentAsync(CopiersEquipmentSaveRequestDto request, CancellationToken ct = default);
    Task<CopiersEquipmentMovementSaveResultDto> RegisterCopiersEquipmentMovementAsync(CopiersEquipmentMovementSaveRequestDto request, CancellationToken ct = default);
    Task<CopiersEquipmentMovementSaveResultDto> UploadCopiersEquipmentMovementAttachmentAsync(string movementId, string fileName, string contentType, byte[] content, CancellationToken ct = default);
    Task<RhFileDownloadResult?> DownloadCopiersEquipmentMovementAttachmentAsync(string movementId, CancellationToken ct = default);
    Task<CopiersEquipmentClientSaveResultDto> SaveCopiersEquipmentClientAsync(CopiersEquipmentClientSaveRequestDto request, CancellationToken ct = default);
    Task<PlanRioPageViewModel> GetPlanRioPageAsync(CancellationToken ct = default);
    Task<PlanRioWorkoutSaveResultDto> SavePlanRioWorkoutAsync(PlanRioWorkoutSaveRequestDto request, CancellationToken ct = default);
    Task<RhFileDownloadResult?> DownloadCopiersMaintenanceAttachmentAsync(string maintenanceId, CancellationToken ct = default);
    Task<CotizadorInterno.Web.Models.Copiers.CopiersMaintenanceBoardDto> GetCopiersMaintenanceBoardAsync(CancellationToken ct = default);
    Task<CotizadorInterno.Web.Models.Copiers.CopiersMaintenanceSaveResultDto> SaveCopiersMaintenanceAsync(CotizadorInterno.Web.Models.Copiers.CopiersMaintenanceSaveRequestDto request, CancellationToken ct = default);
    Task<CotizadorInterno.Web.Models.Copiers.CopiersMaintenanceSaveResultDto> UploadCopiersMaintenanceAttachmentAsync(string maintenanceId, string fileName, string contentType, byte[] content, CancellationToken ct = default);
    Task<CopiersPreventiveMaintenanceBoardDto> GetCopiersPreventiveMaintenanceBoardAsync(string? period = null, CancellationToken ct = default);
    Task<CopiersPreventiveMaintenanceFrequencyUpdateResultDto> UpdateCopiersPreventiveMaintenanceFrequencyAsync(CopiersPreventiveMaintenanceFrequencyUpdateRequestDto request, CancellationToken ct = default);
    Task SaveCopiersPreventiveMaintenanceScheduleAsync(CopiersPreventiveMaintenanceScheduleRequestDto request, CopiersPreventiveMaintenanceScheduleResultDto calendarResult, CancellationToken ct = default);
    Task<CopiersCounterSaveResultDto> SaveCopiersCounterAsync(CopiersCounterSaveRequestDto request, CancellationToken ct = default);
    Task<CopiersCounterSaveResultDto> UploadCopiersCounterAttachmentAsync(string counterId, string fileName, string contentType, byte[] content, CancellationToken ct = default);
    Task<CopiersSupplyInventoryDto> GetCopiersSupplyInventoryAsync(CancellationToken ct = default);
    Task<CopiersSupplyQuantityUpdateResultDto> UpdateCopiersSupplyQuantityAsync(CopiersSupplyQuantityUpdateRequestDto request, CancellationToken ct = default);
    Task<IReadOnlyList<CopiersLookupItemDto>> GetCopiersSupplyLookupAsync(string? query = null, int top = 100, CancellationToken ct = default);
    Task<CopiersSupplierInvoiceBoardDto> GetCopiersPendingSupplierInvoicesAsync(CancellationToken ct = default);
    Task<CopiersApproveSupplierInvoiceResultDto> ApproveCopiersSupplierInvoiceAsync(string invoiceId, CancellationToken ct = default);
    Task<CopiersDeliveryBoardDto> GetCopiersDeliveriesAsync(CancellationToken ct = default);
    Task<CopiersDeliverySaveResultDto> SaveCopiersDeliveryAsync(CopiersDeliverySaveRequestDto request, CancellationToken ct = default);
    Task<CopiersDeliverySaveResultDto> UploadCopiersDeliveryAttachmentAsync(string deliveryId, string fileName, string contentType, byte[] content, CancellationToken ct = default);
    Task<RhFileDownloadResult?> DownloadCopiersDeliveryAttachmentAsync(string deliveryId, CancellationToken ct = default);
    Task<CopiersSupplierInvoiceBoardDto> GetCopiersSupplierInvoicesAsync(CancellationToken ct = default);
    Task<CopiersSupplierInvoiceBatchCreateResultDto> CreateCopiersSupplierInvoicesAsync(CopiersSupplierInvoiceBatchCreateRequestDto request, CancellationToken ct = default);
    Task<IReadOnlyList<SupplierProviderLookupItem>> GetSupplierCertificateProvidersAsync(DateOnly startDate, DateOnly endDate, string? searchTerm = null, CancellationToken ct = default);
    Task<SupplierCertificateSummaryDto> GetSupplierCertificateSummaryAsync(SupplierCertificateQuery query, CancellationToken ct = default);
    Task<IReadOnlyList<ScenarioStoredDto>> GetScenariosForUserAsync(CancellationToken ct = default);
    Task<ScenarioStoredDto?> GetScenarioByIdAsync(string scenarioId, CancellationToken ct = default);
    async Task<IReadOnlyList<ScenarioStoredDto>> GetScenariosByGroupIdAsync(string groupId, CancellationToken ct = default) =>
        (await GetScenariosForUserAsync(ct))
            .Where(item => string.Equals(
                string.IsNullOrWhiteSpace(item.GroupId) ? item.ScenarioId : item.GroupId,
                groupId,
                StringComparison.OrdinalIgnoreCase))
            .OrderBy(item => item.PossibilityOrder)
            .ToList();
    Task UpsertScenarioAsync(ScenarioSaveRequest request, CancellationToken ct = default);
    Task UpdateScenarioByIdAsync(ScenarioSaveRequest request, CancellationToken ct = default);
    async Task<ScenarioStoredDto?> UpdateScenarioByIdAuthorizedAsync(
        ScenarioSaveRequest request,
        string expectedOwnerSystemUserId,
        CancellationToken ct = default)
    {
        await UpdateScenarioByIdAsync(request, ct);
        return await GetScenarioByIdAsync(request.ScenarioId, ct);
    }
    Task<bool> DeleteScenarioAsync(string scenarioId, CancellationToken ct = default);
    Task<bool> RenameScenarioGroupAsync(
        string groupId,
        string groupName,
        CancellationToken ct = default) => Task.FromResult(false);
    async Task<bool> RecommendScenarioPossibilityAsync(
        string groupId,
        string scenarioId,
        CancellationToken ct = default)
    {
        var scenarios = await GetScenariosByGroupIdAsync(groupId, ct);
        if (!scenarios.Any(item => string.Equals(item.ScenarioId, scenarioId, StringComparison.OrdinalIgnoreCase)))
            return false;
        foreach (var scenario in scenarios)
        {
            var request = new ScenarioSaveRequest
            {
                ScenarioId = scenario.ScenarioId,
                GroupId = scenario.GroupId,
                GroupName = scenario.GroupName,
                PossibilityName = scenario.PossibilityName,
                PossibilityOrder = scenario.PossibilityOrder,
                IncludeInProposal = scenario.IncludeInProposal,
                IsRecommended = string.Equals(scenario.ScenarioId, scenarioId, StringComparison.OrdinalIgnoreCase),
                ExpectedRowVersion = scenario.RowVersion,
                CrmDealId = scenario.CrmDealId,
                ScenarioName = scenario.ScenarioName,
                DealType = scenario.DealType,
                RequiresProration = scenario.RequiresProration,
                StartDate = DateTime.TryParse(scenario.StartDate, out var startDate) ? startDate : null,
                EndDate = DateTime.TryParse(scenario.EndDate, out var endDate) ? endDate : null,
                Lines = scenario.Lines,
                LastResult = scenario.LastResult
            };
            _ = await SaveScenarioV2Async(request, updateOnly: true, ct);
        }
        return true;
    }
    async Task<ScenarioStoredDto?> SaveScenarioV2Async(ScenarioSaveRequest request, bool updateOnly = false, CancellationToken ct = default)
    {
        if (updateOnly)
            await UpdateScenarioByIdAsync(request, ct);
        else
            await UpsertScenarioAsync(request, ct);
        return await GetScenarioByIdAsync(request.ScenarioId, ct);
    }
    Task<IReadOnlyList<ProposalExportHistoryItemDto>> GetProposalHistoryAsync(string groupId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<ProposalExportHistoryItemDto>>([]);
    Task<IReadOnlyDictionary<string, IReadOnlyList<ProposalExportHistoryItemDto>>> GetProposalHistoryForUserAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyDictionary<string, IReadOnlyList<ProposalExportHistoryItemDto>>>(
            new Dictionary<string, IReadOnlyList<ProposalExportHistoryItemDto>>(StringComparer.OrdinalIgnoreCase));
    Task<ProposalConfigurationSnapshotDto?> GetLatestProposalConfigurationAsync(string groupId, CancellationToken ct = default) =>
        Task.FromResult<ProposalConfigurationSnapshotDto?>(null);
    Task<ProposalExportSaveResultDto> SaveProposalExportAsync(ProposalExportSaveRequest request, CancellationToken ct = default) =>
        throw new NotSupportedException("La persistencia de propuestas no esta configurada.");
    Task<ProposalExportDownloadDto?> DownloadProposalExportAsync(string exportId, CancellationToken ct = default) =>
        Task.FromResult<ProposalExportDownloadDto?>(null);
    Task<IReadOnlyList<EmployeeModulePermissionRowDto>> GetEmployeeModulePermissionsAsync(CancellationToken ct = default);
    Task<EmployeeModulePermissionSaveResult> SaveEmployeeModulePermissionsAsync(EmployeeModulePermissionSaveRequest request, CancellationToken ct = default);
    Task<RebatesInversionesBoardDto> GetRebatesInversionesBoardAsync(int year, CancellationToken ct = default);
    Task<RebatesInversionesSaveResultDto> SaveRebatesInversionesRecordAsync(RebatesInversionesSaveRequest request, CancellationToken ct = default);
    Task<RebatesInversionesDeleteResultDto> DeleteRebatesInversionesRecordAsync(RebatesInversionesDeleteRequest request, CancellationToken ct = default);
    Task<CuentaCobroBoardDto> GetCuentasCobroBoardAsync(int year, int month, CancellationToken ct = default);
    Task<CuentaCobroSaveResultDto> SaveCuentaCobroAsync(CuentaCobroSaveRequest request, CancellationToken ct = default);
    Task<CuentaCobroFileUploadResultDto> UploadCuentaCobroAttachmentAsync(string recordId, string fileName, string contentType, byte[] content, CancellationToken ct = default);
    Task<CuentaCobroFileDownloadResult?> DownloadCuentaCobroAttachmentAsync(string recordId, CancellationToken ct = default);
    Task<CuentaCobroPrintResultDto> MarkCuentaCobroAsPrintedAsync(string recordId, CancellationToken ct = default);
    Task<CuentaCobroRowDto> GetCuentaCobroByIdAsync(string recordId, CancellationToken ct = default);
    Task<RegistroPagosClientesBoardDto> GetRegistroPagosClientesBoardAsync(CancellationToken ct = default);
    Task<RegistroPagosClientesPaymentSaveResult> SaveRegistroPagosClientePaymentAsync(RegistroPagosClientesPaymentSaveRequest request, CancellationToken ct = default);
    Task<ConciliacionClientPaymentRowDto> SaveConciliacionClientPaymentDataverseSnapshotAsync(ConciliacionClientPaymentDataverseSnapshotRequest request, CancellationToken ct = default);
    Task<ConciliacionBoardDto> GetConciliacionBoardAsync(int year, int month, CancellationToken ct = default);
    Task<IReadOnlyList<ConciliacionBankBalanceDto>> GetConciliacionBankBalancesAsync(int year, int month, CancellationToken ct = default);
    Task<ConciliacionBankOpeningBalanceResultDto> SetConciliacionBankOpeningBalanceAsync(ConciliacionBankOpeningBalanceRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<ConciliacionOptionDto>> GetConciliacionAccountingAccountOptionsAsync(CancellationToken ct = default);
    Task<ConciliacionMonthValidationStateDto> GetConciliacionCashFlowMonthValidationAsync(int year, int month, CancellationToken ct = default);
    Task<ConciliacionMonthValidationResultDto> MarkConciliacionCashFlowMonthValidatedAsync(int year, int month, string periodLabel, string comments, CancellationToken ct = default);
    Task<ConciliacionActionResultDto> UpdateConciliacionClientPaymentStatusAsync(ConciliacionClientPaymentStatusRequest request, CancellationToken ct = default);
    Task<ConciliacionActionResultDto> MarkConciliacionClientPaymentManualSiigoAsync(string recordId, string reason = "", CancellationToken ct = default);
    Task<ConciliacionCashFlowCategoryResultDto> UpdateConciliacionCashFlowCategoryAsync(ConciliacionCashFlowCategoryRequest request, CancellationToken ct = default);
    Task<ConciliacionCashFlowDescriptionResultDto> UpdateConciliacionCashFlowDescriptionAsync(ConciliacionCashFlowDescriptionRequest request, CancellationToken ct = default);
    Task<ConciliacionCashFlowDescriptionResultDto> MarkConciliacionCashFlowPendingAsync(ConciliacionCashFlowPendingRequest request, CancellationToken ct = default);
    Task<ConciliacionCashFlowDescriptionResultDto> MarkConciliacionCashFlowOmittedAsync(ConciliacionCashFlowPendingRequest request, CancellationToken ct = default);
    Task<ConciliacionCashFlowActionResultDto> MarkConciliacionCashFlowManualSiigoAsync(ConciliacionCashFlowManualRequest request, CancellationToken ct = default);
    Task<ConciliacionCashFlowRowDto> GetConciliacionCashFlowMovementAsync(ConciliacionSupplierPaymentPurchaseSearchRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<ConciliacionDianSupplierInvoiceRowDto>> GetConciliacionDianSupplierDocumentsForPaymentAsync(string supplierIdentification, DateOnly startInclusive, DateOnly endExclusive, CancellationToken ct = default);
    Task<ConciliacionDianActionResultDto> UpdateConciliacionSupplierExpenseAllocationAsync(ConciliacionSupplierExpenseAllocationRequest request, CancellationToken ct = default);
    Task<ConciliacionCashFlowActionResultDto> MarkConciliacionSupplierPaymentSiigoResultAsync(ConciliacionSupplierPaymentSendRequest request, bool success, string message, string siigoId = "", string siigoName = "", string responseJson = "", string payloadJson = "", string statusOverride = "", string targetEndpoint = "/v1/journals", string messagePrefix = "Comprobante de egreso de proveedor enviado a Siigo", CancellationToken ct = default);
    Task<ConciliacionPreflightResultDto> ValidateConciliacionClientPaymentPreflightAsync(string recordId, CancellationToken ct = default);
    Task<ConciliacionSiigoDryRunResultDto> SimulateConciliacionClientPaymentSiigoSendAsync(string recordId, CancellationToken ct = default);
    Task<ConciliacionCuentaCobroRowDto> GetConciliacionCuentaCobroDocumentAsync(ConciliacionCuentaCobroDocumentRequest request, CancellationToken ct = default);
    Task<ConciliacionCuentaCobroActionResultDto> SaveConciliacionCuentaCobroExpenseAsync(ConciliacionCuentaCobroExpenseSaveRequest request, CancellationToken ct = default);
    Task<ConciliacionCuentaCobroActionResultDto> UpdateConciliacionCuentaCobroClassificationAsync(ConciliacionCuentaCobroClassificationRequest request, CancellationToken ct = default);
    Task<bool> TryClaimConciliacionCuentaCobroSupportDocumentForSiigoAsync(ConciliacionCuentaCobroDocumentRequest request, CancellationToken ct = default);
    Task<ConciliacionCuentaCobroActionResultDto> MarkConciliacionCuentaCobroPreflightAsync(ConciliacionCuentaCobroDocumentRequest request, bool ready, string message, IReadOnlyList<string> issues, string payloadJson = "", CancellationToken ct = default);
    Task<ConciliacionCuentaCobroActionResultDto> MarkConciliacionCuentaCobroSiigoResultAsync(ConciliacionCuentaCobroDocumentRequest request, bool success, string message, string siigoId = "", string siigoName = "", string siigoPaymentId = "", string siigoPaymentName = "", string responseJson = "", string payloadJson = "", string stateOverride = "", string targetEndpoint = "/v1/purchase-support-documents", CancellationToken ct = default);
    Task<LicenciamientoBoardDto> GetLicenciamientoBoardAsync(CancellationToken ct = default);
    Task<LicenciamientoPreviewResultDto> PreviewLicenciamientoUploadAsync(string fileName, byte[] content, CancellationToken ct = default);
    Task<LicenciamientoHistoricalPreviewResultDto> PreviewLicenciamientoHistoricalUploadAsync(IReadOnlyList<LicenciamientoHistoricalFileUploadDto> files, string trmText, string acronisBreakdownText, CancellationToken ct = default);
    Task<IReadOnlyList<LicenciamientoLookupItemDto>> SearchLicenciamientoAccountsAsync(string query, int top = 12, CancellationToken ct = default);
    Task<IReadOnlyList<LicenciamientoLookupItemDto>> SearchLicenciamientoProductsAsync(string query, int top = 12, CancellationToken ct = default);
    Task<LicenciamientoRegisterAccountIdResultDto> RegisterLicenciamientoAccountIdAsync(LicenciamientoRegisterAccountIdRequestDto request, CancellationToken ct = default);
    Task<LicenciamientoRegisterProductResultDto> RegisterLicenciamientoProductAsync(LicenciamientoRegisterProductRequestDto request, CancellationToken ct = default);
    Task<LicenciamientoImportResultDto> ImportLicenciamientoRowsAsync(LicenciamientoImportRequestDto request, CancellationToken ct = default);
    Task<LicenciamientoAdjustTrmResultDto> AdjustLicenciamientoTrmAsync(LicenciamientoAdjustTrmRequestDto request, CancellationToken ct = default);
    Task<LicenciamientoUpdateContractTypeResultDto> UpdateLicenciamientoContractTypeAsync(LicenciamientoUpdateContractTypeRequestDto request, CancellationToken ct = default);
    Task<LicenciamientoUpdateSalesPriceResultDto> UpdateLicenciamientoSalesPriceAsync(LicenciamientoUpdateSalesPriceRequestDto request, CancellationToken ct = default);
    Task<LicenciamientoCruceDashboardDto> GetLicenciamientoCruceDashboardAsync(int year, int month, string periodMode = "month", CancellationToken ct = default);
    Task<LicenciamientoCruceUpdateCostAccountResultDto> UpdateLicenciamientoCruceCostAccountAsync(LicenciamientoCruceUpdateCostAccountRequestDto request, CancellationToken ct = default);
    Task<LicenciamientoCruceUpdateBillingVerticalResultDto> UpdateLicenciamientoCruceBillingVerticalAsync(LicenciamientoCruceUpdateBillingVerticalRequestDto request, CancellationToken ct = default);
    Task<IReadOnlyList<LicenciamientoCruceAccountLookupDto>> SearchLicenciamientoCruceAccountsAsync(string query, int top = 12, CancellationToken ct = default);
    Task<LicenciamientoCruceSaveAccountMappingResultDto> SaveLicenciamientoCruceAccountMappingAsync(LicenciamientoCruceSaveAccountMappingRequestDto request, CancellationToken ct = default);
    Task<LicenciamientoCruceUpdateCostInvoiceDateResultDto> UpdateLicenciamientoCruceCostInvoiceDateAsync(LicenciamientoCruceUpdateCostInvoiceDateRequestDto request, CancellationToken ct = default);
    Task<HardwareCsvPreviewResultDto> PreviewHardwareCsvAsync(string fileName, byte[] content, CancellationToken ct = default);
    Task<HardwareProvisionResultDto> ProvisionHardwareCsvAsync(string fileName, byte[] content, CancellationToken ct = default);
    Task<HardwareBoardDto> GetHardwareBoardAsync(int? stateValue = null, DateOnly? startDate = null, DateOnly? endDate = null, CancellationToken ct = default, bool currentOwnerOnly = false, CurrentUserInfo? ownerOverride = null, bool filterByCreatedOn = false);
    Task<HardwareOrderCreateResultDto> CreateHardwareOrderDraftAsync(HardwareOrderCreateRequest request, CancellationToken ct = default, CurrentUserInfo? ownerOverride = null);
    Task<HardwareBulkEditResultDto> UpdateHardwareCommercialDraftAsync(HardwareOrderLineEditRequest request, CancellationToken ct = default, CurrentUserInfo? ownerOverride = null);
    Task<HardwareBulkEditResultDto> DeleteHardwareCommercialDraftAsync(string recordId, CancellationToken ct = default, CurrentUserInfo? ownerOverride = null);
    Task<HardwareSaveResultDto> SaveHardwareStageAsync(HardwareStageSaveRequest request, CancellationToken ct = default, bool requireCurrentOwner = false, CurrentUserInfo? ownerOverride = null);
    Task<HardwareBulkEditResultDto> SaveHardwareRecordsAsync(HardwareBulkEditRequest request, CancellationToken ct = default);
    Task<HardwareFileUploadResultDto> UploadHardwareFileAsync(string recordId, string fieldName, string fileName, string contentType, byte[] content, CancellationToken ct = default, bool requireCurrentOwner = false, CurrentUserInfo? ownerOverride = null, int? requiredStateValue = null, IReadOnlyCollection<int>? allowedStateValues = null);
    Task<HardwareFileDownloadResult?> DownloadHardwareFileAsync(string recordId, string fieldName, CancellationToken ct = default, bool requireCurrentOwner = false, CurrentUserInfo? ownerOverride = null, int? requiredStateValue = null);
    Task<IReadOnlyList<HardwareInvoiceLookupItemDto>> SearchHardwareInvoicesAsync(string query, int top = 12, CancellationToken ct = default);
    Task<SoporteCloudBoardDto> GetSoporteCloudBoardAsync(DateOnly? startDate = null, DateOnly? endDate = null, CancellationToken ct = default);
    Task<IReadOnlyList<MesaAyudaDataverseTicketDto>> GetMesaAyudaTicketsAsync(CancellationToken ct = default);
    Task<MesaAyudaDataverseTicketDto?> GetMesaAyudaTicketAsync(string ticketId, CancellationToken ct = default);
    Task<IReadOnlyList<MesaAyudaInteractionDto>> GetMesaAyudaInteractionsAsync(CancellationToken ct = default);
    Task<IReadOnlyList<MesaAyudaInteractionDto>> GetMesaAyudaInteractionsAsync(string ticketId, CancellationToken ct = default);
    Task<MesaAyudaInteractionDto?> GetMesaAyudaInteractionByIdempotencyKeyAsync(string idempotencyKey, CancellationToken ct = default);
    Task<MesaAyudaInteractionDto> CreateMesaAyudaInternalMessageAsync(MesaAyudaInternalMessageCreate request, CancellationToken ct = default);
    Task<MesaAyudaInteractionDto> SaveMesaAyudaInvestigationAsync(MesaAyudaInvestigationCreate request, CancellationToken ct = default);
    Task<SoporteCloudTrainingsBoardDto> GetSoporteCloudTrainingsBoardAsync(DateOnly? startDate = null, DateOnly? endDate = null, CancellationToken ct = default, bool includeAll = false);
    Task<SoporteCloudTrainingSaveResultDto> SaveSoporteCloudTrainingAsync(SoporteCloudTrainingSaveRequest request, CancellationToken ct = default);
    Task<SoporteCloudSaveResultDto> SaveSoporteCloudTicketAsync(SoporteCloudSaveRequest request, CancellationToken ct = default);
    Task<SoporteCloudDeleteResultDto> DeleteSoporteCloudTicketAsync(string recordId, CancellationToken ct = default);
    Task<SoporteCloudFileUploadResultDto> UploadSoporteCloudAttachmentAsync(string recordId, string fileName, string contentType, byte[] content, CancellationToken ct = default);
    Task<SoporteCloudFileDownloadResult?> DownloadSoporteCloudAttachmentAsync(string recordId, CancellationToken ct = default);
    Task<SoporteCloudSurveyBoardDto> GetSoporteCloudSurveyBoardAsync(CancellationToken ct = default);
    Task<SoporteCloudSurveySessionDetailDto> GetSoporteCloudSurveySessionDetailAsync(string sessionId, CancellationToken ct = default);
    Task<SoporteCloudSurveySaveResultDto> SaveSoporteCloudSurveyTopicAsync(SoporteCloudSurveyTopicSaveRequest request, CancellationToken ct = default);
    Task<SoporteCloudSurveySaveResultDto> SaveSoporteCloudSurveyQuestionAsync(SoporteCloudSurveyQuestionSaveRequest request, CancellationToken ct = default);
    Task<SoporteCloudSurveySaveResultDto> DeleteSoporteCloudSurveyQuestionAsync(string questionId, CancellationToken ct = default);
    Task<SoporteCloudSurveySaveResultDto> SaveSoporteCloudSurveySessionAsync(SoporteCloudSurveySessionSaveRequest request, CancellationToken ct = default);
    Task<SoporteCloudSurveySaveResultDto> CloseSoporteCloudSurveySessionAsync(string sessionId, decimal? durationMinutes = null, CancellationToken ct = default);
    Task<SoporteCloudPublicSurveyViewModel> GetSoporteCloudPublicSurveyAsync(string code, CancellationToken ct = default, bool trackScan = true);
    Task<SoporteCloudSurveySubmitResultDto> SubmitSoporteCloudPublicSurveyAsync(SoporteCloudSurveySubmitRequest request, CancellationToken ct = default);
    Task<int> SaveSoporteCloudLiveKnowledgeResultsAsync(string code, IReadOnlyList<SoporteCloudSurveySubmitRequest> submissions, CancellationToken ct = default);
    Task<SoporteCloudSurveySessionDetailDto> GetSoporteCloudPublicSurveyResultsAsync(string code, CancellationToken ct = default);
    Task<EnviosBoardDto> GetEnviosBoardAsync(int? year = null, int? month = null, CancellationToken ct = default);
    Task<EnviosBoardDto> GetEnviosTransportadorBoardAsync(int? year = null, int? month = null, CancellationToken ct = default);
    Task<EnvioSaveResultDto> CreateEnvioSolicitudAsync(EnvioCreateRequest request, CancellationToken ct = default);
    Task<EnvioSaveResultDto> ScheduleEnvioAsync(EnvioScheduleRequest request, CancellationToken ct = default);
    Task<EnvioSaveResultDto> ApproveEnvioPickupAsync(string recordId, CancellationToken ct = default);
    Task<EnvioSaveResultDto> ConfirmEnvioDeliveryAsync(string recordId, CancellationToken ct = default);
    Task<EnvioFileUploadResultDto> ApproveEnvioDeliverySatisfactionAsync(string recordId, string fileName, string contentType, byte[] content, CancellationToken ct = default);
    Task<EnvioFileDownloadResult?> DownloadEnvioDeliveryActAsync(string recordId, CancellationToken ct = default);
    Task<ContractsPageViewModel> GetContractsPageAsync(CancellationToken ct = default);
    Task<ContractCreateResultDto> CreateContractAsync(ContractCreateRequest request, string rutFileName, string rutContentType, byte[] rutContent, string offerFileName, string offerContentType, byte[] offerContent, CancellationToken ct = default);
    Task<ContractServiceOrderCreateResultDto> CreateContractServiceOrderAsync(ContractServiceOrderCreateRequest request, CancellationToken ct = default);
    Task<ContractUploadResultDto> UploadContractSignedFileAsync(string contractId, string fileName, string contentType, byte[] content, CancellationToken ct = default);
    Task<ContractUploadResultDto> UploadContractOrderSignedFileAsync(string orderId, string fileName, string contentType, byte[] content, CancellationToken ct = default);
    Task<ContractUploadResultDto> GenerateContractDeliveryActAsync(string contractId, string orderId, CancellationToken ct = default);
    Task<ContractFileDownloadResult?> DownloadContractFileAsync(string recordKind, string recordId, string fileKey, CancellationToken ct = default);
    Task<TaskSyncResultDto> SyncAutomaticTasksAsync(CancellationToken ct = default);
    Task<IReadOnlyList<TaskBoardItemDto>> GetPendingTasksForCurrentUserAsync(CancellationToken ct = default);
    Task<ManualTaskCreateResult> CreateManualTaskAsync(ManualTaskCreateRequest request, CancellationToken ct = default);
    Task<ManualTaskCloseResult> CloseManualTaskAsync(ManualTaskCloseRequest request, string? fileName = null, string? contentType = null, byte[]? content = null, CancellationToken ct = default);
    PublicDataExportCatalogDto GetPublicDataExportCatalog();
    Task<PublicDataExportTableDto> GetPublicDataExportTableAsync(string datasetKey, IReadOnlyList<string> columnKeys, int? top = null, CancellationToken ct = default);
}
