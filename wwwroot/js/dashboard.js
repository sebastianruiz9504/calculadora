(() => {
    const app = document.getElementById("billingDashboardApp");
    if (!app) {
        return;
    }

    function ensureTodayDashboardMarkup() {
        const tabs = app.querySelector(".dashboard-tabs");
        if (tabs && !tabs.querySelector('[data-dashboard-tab="today"]')) {
            tabs.querySelectorAll("[data-dashboard-tab]").forEach(button => {
                button.classList.remove("is-active");
                button.setAttribute("aria-selected", "false");
            });

            const todayButton = document.createElement("button");
            todayButton.type = "button";
            todayButton.className = "dashboard-tab is-active dashboard-tab--today";
            todayButton.dataset.dashboardTab = "today";
            todayButton.setAttribute("aria-selected", "true");
            todayButton.textContent = ". HOY";
            tabs.prepend(todayButton);
        }

        if (!app.querySelector('[data-dashboard-panel="today"]')) {
            app.querySelectorAll("[data-dashboard-panel]").forEach(panel => {
                panel.classList.remove("is-active");
                panel.hidden = true;
            });

            const todayPanel = document.createElement("div");
            todayPanel.className = "dashboard-tab-panel is-active";
            todayPanel.dataset.dashboardPanel = "today";
            todayPanel.innerHTML = `
                <section class="dashboard-today" aria-labelledby="dashboardTodayTitle">
                    <header class="dashboard-today__header">
                        <div>
                            <div class="dashboard-panel__kicker">Resumen ejecutivo</div>
                            <h2 class="dashboard-today__title" id="dashboardTodayTitle">Lo esencial de hoy</h2>
                            <p class="dashboard-today__subtitle">
                                Corte <strong id="dashboardTodayAsOf">de hoy</strong>. Las variaciones comparan
                                <span id="dashboardTodayCurrentPeriod">el mes a la fecha</span> contra
                                <span id="dashboardTodayComparisonPeriod">los mismos d&iacute;as del mes anterior</span>.
                            </p>
                        </div>
                        <button type="button" class="btn btn-outline-primary dashboard-today__refresh" id="dashboardTodayRefreshBtn">Actualizar</button>
                    </header>
                    <div class="dashboard-status" id="dashboardTodayStatus" role="status" aria-live="polite"></div>
                    <div class="dashboard-today-grid" id="dashboardTodayCards" aria-live="polite" aria-busy="true">
                        ${Array.from({ length: 8 }, () => '<article class="dashboard-today-card dashboard-today-card--skeleton" aria-hidden="true"><span></span><strong></strong><small></small></article>').join("")}
                    </div>
                </section>`;

            const firstPanel = app.querySelector("[data-dashboard-panel]");
            if (firstPanel) {
                firstPanel.before(todayPanel);
            } else {
                app.append(todayPanel);
            }
        }
    }

    ensureTodayDashboardMarkup();

    const utilityTheoreticalExclusionsStorageKey = "cotizador-interno.dashboard.utility.theoretical-exclusions.v1";

    const dashboardPeriodScope = document.getElementById("dashboardPeriodScope");
    const yearFilter = document.getElementById("dashboardYearFilter");
    const periodFilter = document.getElementById("dashboardPeriodFilter");
    const valueFilter = document.getElementById("dashboardValueFilter");
    const refreshButton = document.getElementById("dashboardRefreshBtn");
    const portfolioRefreshButton = document.getElementById("portfolioRefreshBtn");
    const billingStatusBanner = document.getElementById("dashboardStatusBanner");
    const taxesStatusBanner = document.getElementById("taxesStatusBanner");
    const portfolioStatusBanner = document.getElementById("portfolioStatusBanner");
    const periodLabel = document.getElementById("dashboardPeriodLabel");
    const dateRangeLabel = document.getElementById("dashboardDateRangeLabel");
    const compareLabel = document.getElementById("dashboardCompareLabel");
    const granularityLabel = document.getElementById("dashboardGranularityLabel");
    const recordCount = document.getElementById("dashboardRecordCount");
    const dashboardTodayCards = document.getElementById("dashboardTodayCards");
    const dashboardTodayStatus = document.getElementById("dashboardTodayStatus");
    const dashboardTodayAsOf = document.getElementById("dashboardTodayAsOf");
    const dashboardTodayCurrentPeriod = document.getElementById("dashboardTodayCurrentPeriod");
    const dashboardTodayComparisonPeriod = document.getElementById("dashboardTodayComparisonPeriod");
    const dashboardTodayRefreshButton = document.getElementById("dashboardTodayRefreshBtn");

    const billingKpisContainer = document.getElementById("billingKpisContainer");
    const trendsContainer = document.getElementById("billingTrendsContainer");
    const billingSubtabButtons = Array.from(document.querySelectorAll("[data-billing-subtab]"));
    const billingSubpanels = Array.from(document.querySelectorAll("[data-billing-subpanel]"));
    const billingReportToggleButton = document.getElementById("billingReportToggleBtn");
    const siigoToggleButton = document.getElementById("siigoToggleBtn");
    const billingDataverseRepairButton = document.getElementById("billingDataverseRepairBtn");
    const billingReportSection = document.getElementById("billingReportSection");
    const siigoApiSection = document.getElementById("siigoApiSection");
    const cloudBillingRefreshButton = document.getElementById("cloudBillingRefreshBtn");
    const cloudBillingStatusBanner = document.getElementById("cloudBillingStatusBanner");
    const cloudBillingPeriodLabel = document.getElementById("cloudBillingPeriodLabel");
    const cloudBillingDateRangeLabel = document.getElementById("cloudBillingDateRangeLabel");
    const cloudBillingKpisContainer = document.getElementById("cloudBillingKpisContainer");
    const cloudBillingSearch = document.getElementById("cloudBillingSearch");
    const cloudBillingStatusFilter = document.getElementById("cloudBillingStatusFilter");
    const cloudBillingResultsCount = document.getElementById("cloudBillingResultsCount");
    const cloudBillingSummaryText = document.getElementById("cloudBillingSummaryText");
    const cloudBillingBody = document.getElementById("cloudBillingBody");
    const cloudBillingDetailModal = document.getElementById("cloudBillingDetailModal");
    const cloudBillingDetailCloseBtn = document.getElementById("cloudBillingDetailCloseBtn");
    const cloudBillingDetailTitle = document.getElementById("cloudBillingDetailTitle");
    const cloudBillingDetailSubtitle = document.getElementById("cloudBillingDetailSubtitle");
    const cloudBillingDetailSummary = document.getElementById("cloudBillingDetailSummary");
    const cloudBillingDetailErrors = document.getElementById("cloudBillingDetailErrors");
    const cloudBillingDetailBody = document.getElementById("cloudBillingDetailBody");
    const billingCreditNotesSearch = document.getElementById("billingCreditNotesSearch");
    const billingCreditNotesStatusFilter = document.getElementById("billingCreditNotesStatusFilter");
    const billingCreditNotesRefreshButton = document.getElementById("billingCreditNotesRefreshBtn");
    const billingCreditNotesStatus = document.getElementById("billingCreditNotesStatus");
    const billingCreditNotesTotalCount = document.getElementById("billingCreditNotesTotalCount");
    const billingCreditNotesTotalAmount = document.getElementById("billingCreditNotesTotalAmount");
    const billingCreditNotesMatchedAmount = document.getElementById("billingCreditNotesMatchedAmount");
    const billingCreditNotesMatchedCount = document.getElementById("billingCreditNotesMatchedCount");
    const billingCreditNotesUnmatchedAmount = document.getElementById("billingCreditNotesUnmatchedAmount");
    const billingCreditNotesUnmatchedCount = document.getElementById("billingCreditNotesUnmatchedCount");
    const billingCreditNotesResultsCount = document.getElementById("billingCreditNotesResultsCount");
    const billingCreditNotesBody = document.getElementById("billingCreditNotesBody");
    const billingInvoicesSearch = document.getElementById("billingInvoicesSearch");
    const billingInvoicesMonth = document.getElementById("billingInvoicesMonth");
    const billingInvoicesPageSize = document.getElementById("billingInvoicesPageSize");
    const billingInvoicesPreviousPageButton = document.getElementById("billingInvoicesPreviousPageBtn");
    const billingInvoicesNextPageButton = document.getElementById("billingInvoicesNextPageBtn");
    const billingInvoicesPageLabel = document.getElementById("billingInvoicesPageLabel");
    const billingInvoicesRefreshButton = document.getElementById("billingInvoicesRefreshBtn");
    const billingInvoicesDuplicatesButton = document.getElementById("billingInvoicesDuplicatesBtn");
    const billingInvoicesClearFiltersButton = document.getElementById("billingInvoicesClearFiltersBtn");
    const billingInvoicesContractButton = document.getElementById("billingInvoicesContractBtn");
    const billingInvoicesDeleteButton = document.getElementById("billingInvoicesDeleteBtn");
    const billingInvoicesStatus = document.getElementById("billingInvoicesStatus");
    const billingInvoicesResultsCount = document.getElementById("billingInvoicesResultsCount");
    const billingInvoicesSelectedCount = document.getElementById("billingInvoicesSelectedCount");
    const billingInvoicesHead = document.getElementById("billingInvoicesHead");
    const billingInvoicesBody = document.getElementById("billingInvoicesBody");
    const billingInvoiceEditorModal = document.getElementById("billingInvoiceEditorModal");
    const billingInvoiceEditorCloseButton = document.getElementById("billingInvoiceEditorCloseBtn");
    const billingInvoiceEditorCancelButton = document.getElementById("billingInvoiceEditorCancelBtn");
    const billingInvoiceEditorForm = document.getElementById("billingInvoiceEditorForm");
    const billingInvoiceEditorTitle = document.getElementById("billingInvoiceEditorTitle");
    const billingInvoiceEditorSubtitle = document.getElementById("billingInvoiceEditorSubtitle");
    const billingInvoiceEditorStatus = document.getElementById("billingInvoiceEditorStatus");
    const billingInvoiceEditorSaveButton = document.getElementById("billingInvoiceEditorSaveBtn");
    const billingInvoiceRecordIdInput = document.getElementById("billingInvoiceRecordIdInput");
    const billingInvoiceNumberInput = document.getElementById("billingInvoiceNumberInput");
    const billingInvoiceClientIdInput = document.getElementById("billingInvoiceClientIdInput");
    const billingInvoiceClientNameInput = document.getElementById("billingInvoiceClientNameInput");
    const billingInvoiceClientOptions = document.getElementById("billingInvoiceClientOptions");
    const billingInvoiceCompanyTaxIdInput = document.getElementById("billingInvoiceCompanyTaxIdInput");
    const billingInvoiceVerticalInput = document.getElementById("billingInvoiceVerticalInput");
    const billingInvoiceContractTypeInput = document.getElementById("billingInvoiceContractTypeInput");
    const billingInvoiceEmissionDateInput = document.getElementById("billingInvoiceEmissionDateInput");
    const billingInvoiceDueDateInput = document.getElementById("billingInvoiceDueDateInput");
    const billingInvoicePaymentDateInput = document.getElementById("billingInvoicePaymentDateInput");
    const billingInvoiceTotalInput = document.getElementById("billingInvoiceTotalInput");
    const billingInvoiceVatPercentInput = document.getElementById("billingInvoiceVatPercentInput");
    const billingInvoiceVatValueInput = document.getElementById("billingInvoiceVatValueInput");
    const billingInvoicePaymentValueInput = document.getElementById("billingInvoicePaymentValueInput");
    const billingInvoiceReteIcaInput = document.getElementById("billingInvoiceReteIcaInput");
    const billingInvoiceRteIvaInput = document.getElementById("billingInvoiceRteIvaInput");
    const billingInvoiceRteFteInput = document.getElementById("billingInvoiceRteFteInput");
    const billingInvoiceDifferenceInput = document.getElementById("billingInvoiceDifferenceInput");
    const billingInvoicePublicUrlInput = document.getElementById("billingInvoicePublicUrlInput");
    const billingContractTypeModal = document.getElementById("billingContractTypeModal");
    const billingContractTypeCloseButton = document.getElementById("billingContractTypeCloseBtn");
    const billingContractTypeCancelButton = document.getElementById("billingContractTypeCancelBtn");
    const billingContractTypeForm = document.getElementById("billingContractTypeForm");
    const billingContractTypeStatus = document.getElementById("billingContractTypeStatus");
    const billingContractTypeSelectedCount = document.getElementById("billingContractTypeSelectedCount");
    const billingContractTypeBulkInput = document.getElementById("billingContractTypeBulkInput");
    const billingContractTypeSaveButton = document.getElementById("billingContractTypeSaveBtn");
    const billingReportClientSearch = document.getElementById("billingReportClientSearch");
    const billingReportClientIdInput = document.getElementById("billingReportClientIdInput");
    const billingReportClientOptions = document.getElementById("billingReportClientOptions");
    const billingReportLoadButton = document.getElementById("billingReportLoadBtn");
    const billingReportExportButton = document.getElementById("billingReportExportBtn");
    const billingReportStatus = document.getElementById("billingReportStatus");
    const billingReportClientReference = document.getElementById("billingReportClientReference");
    const billingReportNitReference = document.getElementById("billingReportNitReference");
    const billingReportResultsCount = document.getElementById("billingReportResultsCount");
    const billingReportSelectedCount = document.getElementById("billingReportSelectedCount");
    const billingReportSelectedTotal = document.getElementById("billingReportSelectedTotal");
    const billingReportBody = document.getElementById("billingReportBody");
    const billingReportPreview = document.getElementById("billingReportPreview");
    const billingReportPreviewTitle = document.getElementById("billingReportPreviewTitle");
    const billingReportPreviewLink = document.getElementById("billingReportPreviewLink");
    const billingReportPreviewFrame = document.getElementById("billingReportPreviewFrame");
    const siigoCustomerSelect = document.getElementById("siigoCustomerSelect");
    const siigoCustomerIdInput = document.getElementById("siigoCustomerIdInput");
    const siigoCustomersLoadButton = document.getElementById("siigoCustomersLoadBtn");
    const siigoCustomerNitSearch = document.getElementById("siigoCustomerNitSearch");
    const siigoCustomerNitSearchButton = document.getElementById("siigoCustomerNitSearchBtn");
    const siigoStartDateInput = document.getElementById("siigoStartDateInput");
    const siigoEndDateInput = document.getElementById("siigoEndDateInput");
    const siigoUseActivePeriodButton = document.getElementById("siigoUseActivePeriodBtn");
    const siigoInvoicesLoadButton = document.getElementById("siigoInvoicesLoadBtn");
    const siigoInvoicesDownloadButton = document.getElementById("siigoInvoicesDownloadBtn");
    const siigoInvoicesStatus = document.getElementById("siigoInvoicesStatus");
    const siigoCustomerReference = document.getElementById("siigoCustomerReference");
    const siigoNitReference = document.getElementById("siigoNitReference");
    const siigoPeriodReference = document.getElementById("siigoPeriodReference");
    const siigoInvoicesResultsCount = document.getElementById("siigoInvoicesResultsCount");
    const siigoInvoicesSelectedCount = document.getElementById("siigoInvoicesSelectedCount");
    const siigoInvoicesTotalAmount = document.getElementById("siigoInvoicesTotalAmount");
    const siigoInvoicesSelectAll = document.getElementById("siigoInvoicesSelectAll");
    const siigoInvoicesBody = document.getElementById("siigoInvoicesBody");

    const copiersRefreshButton = document.getElementById("copiersRefreshBtn");
    const copiersStatusBanner = document.getElementById("copiersStatusBanner");
    const copiersAsOfLabel = document.getElementById("copiersAsOfLabel");
    const copiersFocusLabel = document.getElementById("copiersFocusLabel");
    const copiersSubtabButtons = Array.from(document.querySelectorAll("[data-copiers-subtab]"));
    const copiersSubpanels = Array.from(document.querySelectorAll("[data-copiers-subpanel]"));
    const copiersResultsCount = document.getElementById("copiersResultsCount");
    const copiersKpisContainer = document.getElementById("copiersKpisContainer");
    const copiersBillingBody = document.getElementById("copiersBillingBody");
    const copiersNewRecordButton = document.getElementById("copiersNewRecordBtn");
    const copiersEquipmentRefreshButton = document.getElementById("copiersEquipmentRefreshBtn");
    const copiersInventoryExportButton = document.getElementById("copiersInventoryExportBtn");
    const copiersMaintenanceRefreshButton = document.getElementById("copiersMaintenanceRefreshBtn");
    const copiersCountersRefreshButton = document.getElementById("copiersCountersRefreshBtn");
    const copiersCountersPdfButton = document.getElementById("copiersCountersPdfBtn");
    const copiersEquipmentResultsCount = document.getElementById("copiersEquipmentResultsCount");
    const copiersEquipmentKpisContainer = document.getElementById("copiersEquipmentKpisContainer");
    const copiersInventoryKpisContainer = document.getElementById("copiersInventoryKpisContainer");
    const copiersInventoryResultsCount = document.getElementById("copiersInventoryResultsCount");
    const copiersInventoryPendingCount = document.getElementById("copiersInventoryPendingCount");
    const copiersInventoryBody = document.getElementById("copiersInventoryBody");
    const copiersInventoryPendingBody = document.getElementById("copiersInventoryPendingBody");
    const copiersMaintenanceKpisContainer = document.getElementById("copiersMaintenanceKpisContainer");
    const copiersCountersKpisContainer = document.getElementById("copiersCountersKpisContainer");
    const copiersClientSummaryBody = document.getElementById("copiersClientSummaryBody");
    const copiersStockBody = document.getElementById("copiersStockBody");
    const copiersEquipmentBody = document.getElementById("copiersEquipmentBody");
    const copiersMaintenanceChart = document.getElementById("copiersMaintenanceChart");
    const copiersMaintenanceLegend = document.getElementById("copiersMaintenanceLegend");
    const copiersMaintenanceResultsCount = document.getElementById("copiersMaintenanceResultsCount");
    const copiersMaintenanceYearFilter = document.getElementById("copiersMaintenanceYearFilter");
    const copiersMaintenanceMonthFilter = document.getElementById("copiersMaintenanceMonthFilter");
    const copiersMaintenanceOwnerFilter = document.getElementById("copiersMaintenanceOwnerFilter");
    const copiersMaintenanceHead = document.getElementById("copiersMaintenanceHead");
    const copiersMaintenanceBody = document.getElementById("copiersMaintenanceBody");
    const copiersMaintenancePagination = document.getElementById("copiersMaintenancePagination");
    const copiersMaintenancePrevBtn = document.getElementById("copiersMaintenancePrevBtn");
    const copiersMaintenanceNextBtn = document.getElementById("copiersMaintenanceNextBtn");
    const copiersMaintenancePageSummary = document.getElementById("copiersMaintenancePageSummary");
    const copiersMovementsRefreshButton = document.getElementById("copiersMovementsRefreshBtn");
    const copiersMovementsResultsCount = document.getElementById("copiersMovementsResultsCount");
    const copiersMovementsHead = document.getElementById("copiersMovementsHead");
    const copiersMovementsBody = document.getElementById("copiersMovementsBody");
    const copiersCountersMonthFilter = document.getElementById("copiersCountersMonthFilter");
    const copiersCountersYearFilter = document.getElementById("copiersCountersYearFilter");
    const copiersCountersClientNameFilter = document.getElementById("copiersCountersClientNameFilter");
    const copiersCountersClientIdFilter = document.getElementById("copiersCountersClientIdFilter");
    const copiersCountersClientOptions = document.getElementById("copiersCountersClientOptions");
    const copiersCountersClearButton = document.getElementById("copiersCountersClearBtn");
    const copiersCountersPeriodLabel = document.getElementById("copiersCountersPeriodLabel");
    const copiersCountersEmptyState = document.getElementById("copiersCountersEmptyState");
    const copiersCountersResultsShell = document.getElementById("copiersCountersResultsShell");
    const copiersCountersClientResultsCount = document.getElementById("copiersCountersClientResultsCount");
    const copiersCountersEquipmentResultsCount = document.getElementById("copiersCountersEquipmentResultsCount");
    const copiersCountersClientBody = document.getElementById("copiersCountersClientBody");
    const copiersCountersEquipmentBody = document.getElementById("copiersCountersEquipmentBody");
    const copiersEditorModal = document.getElementById("copiersEditorModal");
    const copiersEditorCloseBtn = document.getElementById("copiersEditorCloseBtn");
    const copiersEditorCancelBtn = document.getElementById("copiersEditorCancelBtn");
    const copiersEditorForm = document.getElementById("copiersEditorForm");
    const copiersEditorTitle = document.getElementById("copiersEditorTitle");
    const copiersEditorSubtitle = document.getElementById("copiersEditorSubtitle");
    const copiersEditorStatus = document.getElementById("copiersEditorStatus");
    const copiersEditorSaveBtn = document.getElementById("copiersEditorSaveBtn");
    const copiersRecordIdInput = document.getElementById("copiersRecordIdInput");
    const copiersBillingDayInput = document.getElementById("copiersBillingDayInput");
    const copiersClientIdInput = document.getElementById("copiersClientIdInput");
    const copiersClientNameInput = document.getElementById("copiersClientNameInput");
    const copiersClientOptions = document.getElementById("copiersClientOptions");
    const copiersProductIdInput = document.getElementById("copiersProductIdInput");
    const copiersProductNameInput = document.getElementById("copiersProductNameInput");
    const copiersProductOptions = document.getElementById("copiersProductOptions");
    const copiersQuantityInput = document.getElementById("copiersQuantityInput");
    const copiersIncludedOperationsInput = document.getElementById("copiersIncludedOperationsInput");
    const copiersAdditionalOperationInput = document.getElementById("copiersAdditionalOperationInput");
    const copiersUnitValueBeforeVatInput = document.getElementById("copiersUnitValueBeforeVatInput");
    const copiersUnitValueWithVatInput = document.getElementById("copiersUnitValueWithVatInput");
    const copiersTotalWithVatInput = document.getElementById("copiersTotalWithVatInput");
    const copiersClientInvoicesModal = document.getElementById("copiersClientInvoicesModal");
    const copiersClientInvoicesCloseBtn = document.getElementById("copiersClientInvoicesCloseBtn");
    const copiersClientInvoicesStatus = document.getElementById("copiersClientInvoicesStatus");
    const copiersClientInvoicesTitle = document.getElementById("copiersClientInvoicesTitle");
    const copiersClientInvoicesSubtitle = document.getElementById("copiersClientInvoicesSubtitle");
    const copiersClientInvoicesResultsCount = document.getElementById("copiersClientInvoicesResultsCount");
    const copiersClientInvoicesBody = document.getElementById("copiersClientInvoicesBody");
    const copiersLineEquipmentModal = document.getElementById("copiersLineEquipmentModal");
    const copiersLineEquipmentCloseBtn = document.getElementById("copiersLineEquipmentCloseBtn");
    const copiersLineEquipmentCancelBtn = document.getElementById("copiersLineEquipmentCancelBtn");
    const copiersLineEquipmentSaveBtn = document.getElementById("copiersLineEquipmentSaveBtn");
    const copiersLineEquipmentTitle = document.getElementById("copiersLineEquipmentTitle");
    const copiersLineEquipmentSubtitle = document.getElementById("copiersLineEquipmentSubtitle");
    const copiersLineEquipmentStatus = document.getElementById("copiersLineEquipmentStatus");
    const copiersLineEquipmentSummary = document.getElementById("copiersLineEquipmentSummary");
    const copiersLineEquipmentAssignedCount = document.getElementById("copiersLineEquipmentAssignedCount");
    const copiersLineEquipmentAvailableCount = document.getElementById("copiersLineEquipmentAvailableCount");
    const copiersLineEquipmentAssignedBody = document.getElementById("copiersLineEquipmentAssignedBody");
    const copiersLineEquipmentAvailableBody = document.getElementById("copiersLineEquipmentAvailableBody");
    const copiersBillingCountersModal = document.getElementById("copiersBillingCountersModal");
    const copiersBillingCountersCloseBtn = document.getElementById("copiersBillingCountersCloseBtn");
    const copiersBillingCountersTitle = document.getElementById("copiersBillingCountersTitle");
    const copiersBillingCountersSubtitle = document.getElementById("copiersBillingCountersSubtitle");
    const copiersBillingCountersResultsCount = document.getElementById("copiersBillingCountersResultsCount");
    const copiersBillingCountersBody = document.getElementById("copiersBillingCountersBody");
    const copiersEquipmentDetailModal = document.getElementById("copiersEquipmentDetailModal");
    const copiersEquipmentDetailCloseBtn = document.getElementById("copiersEquipmentDetailCloseBtn");
    const copiersEquipmentDetailCancelBtn = document.getElementById("copiersEquipmentDetailCancelBtn");
    const copiersEquipmentDetailStatus = document.getElementById("copiersEquipmentDetailStatus");
    const copiersEquipmentDetailTitle = document.getElementById("copiersEquipmentDetailTitle");
    const copiersEquipmentDetailSubtitle = document.getElementById("copiersEquipmentDetailSubtitle");
    const copiersEquipmentDetailSerial = document.getElementById("copiersEquipmentDetailSerial");
    const copiersEquipmentDetailCurrentClient = document.getElementById("copiersEquipmentDetailCurrentClient");
    const copiersEquipmentDetailCategory = document.getElementById("copiersEquipmentDetailCategory");
    const copiersEquipmentDetailReference = document.getElementById("copiersEquipmentDetailReference");
    const copiersEquipmentDetailObservations = document.getElementById("copiersEquipmentDetailObservations");
    const copiersEquipmentAssignmentForm = document.getElementById("copiersEquipmentAssignmentForm");
    const copiersEquipmentRecordIdInput = document.getElementById("copiersEquipmentRecordIdInput");
    const copiersEquipmentClientIdInput = document.getElementById("copiersEquipmentClientIdInput");
    const copiersEquipmentClientNameInput = document.getElementById("copiersEquipmentClientNameInput");
    const copiersEquipmentClientOptions = document.getElementById("copiersEquipmentClientOptions");
    const copiersEquipmentMoveToStockInput = document.getElementById("copiersEquipmentMoveToStockInput");
    const copiersEquipmentSaveBtn = document.getElementById("copiersEquipmentSaveBtn");
    const copiersEquipmentMaintenanceBody = document.getElementById("copiersEquipmentMaintenanceBody");

    const taxesRecurringCards = document.getElementById("taxesRecurringCards");
    const taxesOtherCards = document.getElementById("taxesOtherCards");
    const taxesRecurringDetail = document.getElementById("taxesRecurringDetail");
    const taxesOtherDetail = document.getElementById("taxesOtherDetail");

    const portfolioAsOfLabel = document.getElementById("portfolioAsOfLabel");
    const portfolioFocusLabel = document.getElementById("portfolioFocusLabel");
    const portfolioClientSearch = document.getElementById("portfolioClientSearch");
    const portfolioOverdueClearFiltersButton = document.getElementById("portfolioOverdueClearFiltersBtn");
    const portfolioResultsCount = document.getElementById("portfolioResultsCount");
    const portfolioKpisContainer = document.getElementById("portfolioKpisContainer");
    const portfolioOverdueHead = document.getElementById("portfolioOverdueHead");
    const portfolioUnpaidBody = document.getElementById("portfolioUnpaidBody");
    const portfolioSubtabButtons = Array.from(document.querySelectorAll("[data-portfolio-subtab]"));
    const portfolioSubpanels = Array.from(document.querySelectorAll("[data-portfolio-subpanel]"));
    const portfolioMonthlyRangeFilter = document.getElementById("portfolioMonthlyRangeFilter");
    const portfolioMonthlyYearFilter = document.getElementById("portfolioMonthlyYearFilter");
    const portfolioMonthlyMonthFilter = document.getElementById("portfolioMonthlyMonthFilter");
    const portfolioMonthlyStartFilter = document.getElementById("portfolioMonthlyStartFilter");
    const portfolioMonthlyEndFilter = document.getElementById("portfolioMonthlyEndFilter");
    const portfolioMonthlyCloudSummary = document.getElementById("portfolioMonthlyCloudSummary");
    const portfolioMonthlyCloudLegend = document.getElementById("portfolioMonthlyCloudLegend");
    const portfolioMonthlyCloudChart = document.getElementById("portfolioMonthlyCloudChart");
    const portfolioMonthlyCopiersSummary = document.getElementById("portfolioMonthlyCopiersSummary");
    const portfolioMonthlyCopiersLegend = document.getElementById("portfolioMonthlyCopiersLegend");
    const portfolioMonthlyCopiersChart = document.getElementById("portfolioMonthlyCopiersChart");
    const portfolioMonthlyFilterFields = Array.from(document.querySelectorAll("[data-portfolio-monthly-filter-field]"));
    const portfolioMonthlyDetailModal = document.getElementById("portfolioMonthlyDetailModal");
    const portfolioMonthlyDetailCloseButton = document.getElementById("portfolioMonthlyDetailCloseBtn");
    const portfolioMonthlyDetailTitle = document.getElementById("portfolioMonthlyDetailTitle");
    const portfolioMonthlyDetailSubtitle = document.getElementById("portfolioMonthlyDetailSubtitle");
    const portfolioMonthlyDetailSummary = document.getElementById("portfolioMonthlyDetailSummary");
    const portfolioMonthlyDetailTotal = document.getElementById("portfolioMonthlyDetailTotal");
    const portfolioMonthlyDetailBody = document.getElementById("portfolioMonthlyDetailBody");
    const portfolioMonthlyChartConfigs = [
        {
            key: "cloud",
            label: "Cloud",
            summary: portfolioMonthlyCloudSummary,
            legend: portfolioMonthlyCloudLegend,
            chart: portfolioMonthlyCloudChart
        },
        {
            key: "copiers",
            label: "Copiers",
            summary: portfolioMonthlyCopiersSummary,
            legend: portfolioMonthlyCopiersLegend,
            chart: portfolioMonthlyCopiersChart
        }
    ];
    const portfolioInvoicesSearch = document.getElementById("portfolioInvoicesSearch");
    const portfolioInvoicesClearFiltersButton = document.getElementById("portfolioInvoicesClearFiltersBtn");
    const portfolioInvoicesResultsCount = document.getElementById("portfolioInvoicesResultsCount");
    const portfolioInvoicesHead = document.getElementById("portfolioInvoicesHead");
    const portfolioInvoicesBody = document.getElementById("portfolioInvoicesBody");
    const accountStatementClientSearch = document.getElementById("accountStatementClientSearch");
    const accountStatementClientIdInput = document.getElementById("accountStatementClientIdInput");
    const accountStatementClientOptions = document.getElementById("accountStatementClientOptions");
    const accountStatementMatches = document.getElementById("accountStatementMatches");
    const accountStatementGenerateButton = document.getElementById("accountStatementGenerateBtn");
    const accountStatementPdfButton = document.getElementById("accountStatementPdfBtn");
    const accountStatementStatus = document.getElementById("accountStatementStatus");
    const accountStatementClientReference = document.getElementById("accountStatementClientReference");
    const accountStatementCount = document.getElementById("accountStatementCount");
    const accountStatementTotal = document.getElementById("accountStatementTotal");
    const accountStatementAsOf = document.getElementById("accountStatementAsOf");
    const accountStatementBody = document.getElementById("accountStatementBody");

    const businessRefreshButton = document.getElementById("businessRefreshBtn");
    const businessStatusBanner = document.getElementById("businessStatusBanner");
    const businessSubtabButtons = Array.from(document.querySelectorAll("[data-business-subtab]"));
    const businessSubpanels = Array.from(document.querySelectorAll("[data-business-subpanel]"));
    const businessAsOfLabel = document.getElementById("businessAsOfLabel");
    const businessFocusLabel = document.getElementById("businessFocusLabel");
    const businessKpisContainer = document.getElementById("businessKpisContainer");
    const businessProjectionKpisContainer = document.getElementById("businessProjectionKpisContainer");
    const businessProjectionHistoryMeta = document.getElementById("businessProjectionHistoryMeta");
    const businessProjectionHistoryBody = document.getElementById("businessProjectionHistoryBody");
    const businessLinesChart = document.getElementById("businessLinesChart");
    const businessLineMeta = document.getElementById("businessLineMeta");
    const businessContractTypesChart = document.getElementById("businessContractTypesChart");
    const businessContractsList = document.getElementById("businessContractsList");
    const businessContractsCount = document.getElementById("businessContractsCount");
    const businessProductsChart = document.getElementById("businessProductsChart");
    const businessBillingStartFilter = document.getElementById("businessBillingStartFilter");
    const businessBillingEndFilter = document.getElementById("businessBillingEndFilter");
    const businessBillingGranularityFilter = document.getElementById("businessBillingGranularityFilter");
    const businessBillingRefreshButton = document.getElementById("businessBillingRefreshBtn");
    const businessBillingStatusBanner = document.getElementById("businessBillingStatusBanner");
    const businessBillingPeriodLabel = document.getElementById("businessBillingPeriodLabel");
    const businessBillingDateRangeLabel = document.getElementById("businessBillingDateRangeLabel");
    const businessBillingTotalSales = document.getElementById("businessBillingTotalSales");
    const businessBillingRecordCount = document.getElementById("businessBillingRecordCount");
    const businessBillingGranularityLabel = document.getElementById("businessBillingGranularityLabel");
    const businessBillingCloudTotal = document.getElementById("businessBillingCloudTotal");
    const businessBillingCopiersTotal = document.getElementById("businessBillingCopiersTotal");
    const businessBillingCloudMonthlyChart = document.getElementById("businessBillingCloudMonthlyChart");
    const businessBillingCloudPrepaidChart = document.getElementById("businessBillingCloudPrepaidChart");
    const businessBillingCopiersMonthlyChart = document.getElementById("businessBillingCopiersMonthlyChart");
    const businessBillingCopiersPrepaidChart = document.getElementById("businessBillingCopiersPrepaidChart");

    const pnlYearFilter = document.getElementById("pnlYearFilter");
    const pnlMonthFilter = document.getElementById("pnlMonthFilter");
    const pnlVerticalFilter = document.getElementById("pnlVerticalFilter");
    const pnlRefreshButton = document.getElementById("pnlRefreshBtn");
    const pnlStatusBanner = document.getElementById("pnlStatusBanner");
    const pnlPeriodLabel = document.getElementById("pnlPeriodLabel");
    const pnlDateRangeLabel = document.getElementById("pnlDateRangeLabel");
    const pnlKpisContainer = document.getElementById("pnlKpisContainer");
    const pnlDescription = document.getElementById("pnlDescription");
    const pnlTableContainer = document.getElementById("pnlTableContainer");
    const pnlOrphanDescription = document.getElementById("pnlOrphanDescription");
    const pnlOrphanTableContainer = document.getElementById("pnlOrphanTableContainer");
    const pnlDetailModal = document.getElementById("pnlDetailModal");
    const pnlDetailCloseBtn = document.getElementById("pnlDetailCloseBtn");
    const pnlDetailTitle = document.getElementById("pnlDetailTitle");
    const pnlDetailSubtitle = document.getElementById("pnlDetailSubtitle");
    const pnlDetailStatus = document.getElementById("pnlDetailStatus");
    const pnlDetailBody = document.getElementById("pnlDetailBody");

    const licenciamientoYearFilter = document.getElementById("licenciamientoYearFilter");
    const licenciamientoMonthFilter = document.getElementById("licenciamientoMonthFilter");
    const licenciamientoRefreshButton = document.getElementById("licenciamientoRefreshBtn");
    const licenciamientoStatusBanner = document.getElementById("licenciamientoStatusBanner");
    const licenciamientoPeriodLabel = document.getElementById("licenciamientoPeriodLabel");
    const licenciamientoDateRangeLabel = document.getElementById("licenciamientoDateRangeLabel");
    const licenciamientoSummaryCards = document.getElementById("licenciamientoSummaryCards");
    const licenciamientoMonthlyChart = document.getElementById("licenciamientoMonthlyChart");
    const licenciamientoPrepaidChart = document.getElementById("licenciamientoPrepaidChart");
    const licenciamientoCostPeriodLabel = document.getElementById("licenciamientoCostPeriodLabel");
    const licenciamientoCostCards = document.getElementById("licenciamientoCostCards");

    const utilityRefreshButton = document.getElementById("utilityRefreshBtn");
    const utilityStatusBanner = document.getElementById("utilityStatusBanner");
    const utilityPeriodLabel = document.getElementById("utilityPeriodLabel");
    const utilityDateRangeLabel = document.getElementById("utilityDateRangeLabel");
    const utilitySummaryCards = document.getElementById("utilitySummaryCards");
    const utilityMonthlyChart = document.getElementById("utilityMonthlyChart");
    const utilityPrepaidChart = document.getElementById("utilityPrepaidChart");
    const utilityUnresolvedResultsCount = document.getElementById("utilityUnresolvedResultsCount");
    const utilityUnresolvedBody = document.getElementById("utilityUnresolvedBody");
    const utilityBreakdownModal = document.getElementById("utilityBreakdownModal");
    const utilityBreakdownCloseBtn = document.getElementById("utilityBreakdownCloseBtn");
    const utilityBreakdownSaveBtn = document.getElementById("utilityBreakdownSaveBtn");
    const utilityBreakdownTitle = document.getElementById("utilityBreakdownTitle");
    const utilityBreakdownSubtitle = document.getElementById("utilityBreakdownSubtitle");
    const utilityBreakdownStatus = document.getElementById("utilityBreakdownStatus");
    const utilityBreakdownSummary = document.getElementById("utilityBreakdownSummary");
    const utilityBreakdownBody = document.getElementById("utilityBreakdownBody");
    const utilityBreakdownFooter = document.getElementById("utilityBreakdownFooter");
    const utilityRealDetailModal = document.getElementById("utilityRealDetailModal");
    const utilityRealDetailCloseBtn = document.getElementById("utilityRealDetailCloseBtn");
    const utilityRealDetailTitle = document.getElementById("utilityRealDetailTitle");
    const utilityRealDetailSubtitle = document.getElementById("utilityRealDetailSubtitle");
    const utilityRealDetailSummary = document.getElementById("utilityRealDetailSummary");
    const utilityRealSalesTotal = document.getElementById("utilityRealSalesTotal");
    const utilityRealCostsTotal = document.getElementById("utilityRealCostsTotal");
    const utilityRealSalesBody = document.getElementById("utilityRealSalesBody");
    const utilityRealCostsBody = document.getElementById("utilityRealCostsBody");
    const utilityOrphansModal = document.getElementById("utilityOrphansModal");
    const utilityOrphansCloseBtn = document.getElementById("utilityOrphansCloseBtn");
    const utilityOrphansTitle = document.getElementById("utilityOrphansTitle");
    const utilityOrphansSubtitle = document.getElementById("utilityOrphansSubtitle");
    const utilityOrphansStatus = document.getElementById("utilityOrphansStatus");
    const utilityOrphansBody = document.getElementById("utilityOrphansBody");

    const ytdRefreshButton = document.getElementById("ytdRefreshBtn");
    const ytdStatusBanner = document.getElementById("ytdStatusBanner");
    const ytdPeriodLabel = document.getElementById("ytdPeriodLabel");
    const ytdDateRangeLabel = document.getElementById("ytdDateRangeLabel");
    const ytdTotalChart = document.getElementById("ytdTotalChart");
    const ytdRevenueBreakdown = document.getElementById("ytdRevenueBreakdown");
    const ytdExpenseBreakdown = document.getElementById("ytdExpenseBreakdown");
    const ytdRevenueCategoryFilters = document.getElementById("ytdRevenueCategoryFilters");
    const ytdRevenueClientFilters = document.getElementById("ytdRevenueClientFilters");
    const ytdRevenueVerticalFilters = document.getElementById("ytdRevenueVerticalFilters");
    const ytdRevenueContractTypeFilters = document.getElementById("ytdRevenueContractTypeFilters");
    const ytdExpenseCategoryFilters = document.getElementById("ytdExpenseCategoryFilters");
    const ytdExpenseClientFilters = document.getElementById("ytdExpenseClientFilters");
    const ytdExpenseVerticalFilters = document.getElementById("ytdExpenseVerticalFilters");
    const ytdExpenseContractTypeFilters = document.getElementById("ytdExpenseContractTypeFilters");
    const ytdReconciliationDisclaimer = document.getElementById("ytdReconciliationDisclaimer");
    const ytdDetailModal = document.getElementById("ytdDetailModal");
    const ytdDetailCloseBtn = document.getElementById("ytdDetailCloseBtn");
    const ytdDetailTitle = document.getElementById("ytdDetailTitle");
    const ytdDetailSubtitle = document.getElementById("ytdDetailSubtitle");
    const ytdDetailSummary = document.getElementById("ytdDetailSummary");
    const ytdDetailStatus = document.getElementById("ytdDetailStatus");
    const ytdDetailSelectedCount = document.getElementById("ytdDetailSelectedCount");
    const ytdDetailDirtyCount = document.getElementById("ytdDetailDirtyCount");
    const ytdDetailSelectAll = document.getElementById("ytdDetailSelectAll");
    const ytdBulkCategorySelect = document.getElementById("ytdBulkCategorySelect");
    const ytdBulkBillingVerticalSelect = document.getElementById("ytdBulkBillingVerticalSelect");
    const ytdBulkBillingContractSelect = document.getElementById("ytdBulkBillingContractSelect");
    const ytdBulkExpenseContractSelect = document.getElementById("ytdBulkExpenseContractSelect");
    const ytdBulkCloudInput = document.getElementById("ytdBulkCloudInput");
    const ytdBulkCopiersInput = document.getElementById("ytdBulkCopiersInput");
    const ytdBulkApplyButton = document.getElementById("ytdBulkApplyBtn");
    const ytdBulkSaveButton = document.getElementById("ytdBulkSaveBtn");
    const ytdDetailBody = document.getElementById("ytdDetailBody");
    const ytdDetailFoot = document.getElementById("ytdDetailFoot");
    const ytdDetailTotals = document.getElementById("ytdDetailTotals");

    const dashboardAgentForm = document.getElementById("dashboardAgentForm");
    const dashboardAgentInput = document.getElementById("dashboardAgentInput");
    const dashboardAgentSendButton = document.getElementById("dashboardAgentSendBtn");
    const dashboardAgentMessages = document.getElementById("dashboardAgentMessages");
    const dashboardAgentStatus = document.getElementById("dashboardAgentStatus");
    const dashboardAgentSources = document.getElementById("dashboardAgentSources");
    const dashboardAgentPromptButtons = Array.from(document.querySelectorAll("[data-agent-prompt]"));
    const dashboardAgentFeedbackModal = document.getElementById("dashboardAgentFeedbackModal");
    const dashboardAgentFeedbackForm = document.getElementById("dashboardAgentFeedbackForm");
    const dashboardAgentFeedbackMessageId = document.getElementById("dashboardAgentFeedbackMessageId");
    const dashboardAgentFeedbackCategory = document.getElementById("dashboardAgentFeedbackCategory");
    const dashboardAgentFeedbackExpected = document.getElementById("dashboardAgentFeedbackExpected");
    const dashboardAgentFeedbackNotes = document.getElementById("dashboardAgentFeedbackNotes");
    const dashboardAgentFeedbackStatus = document.getElementById("dashboardAgentFeedbackStatus");
    const dashboardAgentFeedbackSubmitButton = document.getElementById("dashboardAgentFeedbackSubmitBtn");
    const dashboardAgentFeedbackCloseButtons = Array.from(document.querySelectorAll("[data-agent-feedback-close]"));
    const dashboardAgentLearningPanel = document.getElementById("dashboardAgentLearningPanel");
    const dashboardAgentLearningSummary = document.getElementById("dashboardAgentLearningSummary");
    const dashboardAgentLearningStatus = document.getElementById("dashboardAgentLearningStatus");
    const dashboardAgentLearningList = document.getElementById("dashboardAgentLearningList");
    const dashboardAgentLearningRefreshButton = document.getElementById("dashboardAgentLearningRefreshBtn");

    const tabButtons = Array.from(document.querySelectorAll("[data-dashboard-tab]"));
    const tabPanels = Array.from(document.querySelectorAll("[data-dashboard-panel]"));
    const dashboardGroupPanels = Array.from(document.querySelectorAll("[data-dashboard-group-panel]"));
    const dashboardGroupButtons = Array.from(document.querySelectorAll("[data-dashboard-group-target]"));

    const currentYear = Number(app.dataset.initialYear || new Date().getFullYear());
    const currentPeriod = app.dataset.initialPeriod || "month";
    const currentValue = Number(app.dataset.initialValue || 1);
    const currentMonth = new Date().getMonth() + 1;
    const currentBimonthly = Math.floor((currentMonth - 1) / 2) + 1;
    const currentFourMonthly = Math.floor((currentMonth - 1) / 4) + 1;
    const licenciamientoDefaultYear = Math.max(currentYear - 1, 2000);
    const licenciamientoDefaultMonth = 12;
    const taxesReteFuenteExportUrl = app.dataset.taxesRetefuenteExportUrl || app.dataset.taxesReteFuenteExportUrl || "";
    const taxesReteIcaExportUrl = app.dataset.taxesReteicaExportUrl || app.dataset.taxesReteIcaExportUrl || "";
    const taxesVatExportUrl = app.dataset.taxesVatExportUrl || "";
    const billingClientReportExportUrl = app.dataset.billingClientReportExportUrl || "";
    const copiersCountersPdfUrl = app.dataset.copiersCountersPdfUrl || "";
    const accountStatementPdfUrl = app.dataset.accountStatementPdfUrl || "";
    const todayUrl = app.dataset.todayUrl || "";

    const currencyFormatter = new Intl.NumberFormat("es-CO", {
        style: "currency",
        currency: "COP",
        minimumFractionDigits: 0,
        maximumFractionDigits: 0
    });

    const usdCurrencyFormatter = new Intl.NumberFormat("es-CO", {
        style: "currency",
        currency: "USD",
        minimumFractionDigits: 0,
        maximumFractionDigits: 0
    });

    const usdUnitFormatter = new Intl.NumberFormat("es-CO", {
        style: "currency",
        currency: "USD",
        minimumFractionDigits: 2,
        maximumFractionDigits: 2
    });

    const numberFormatter = new Intl.NumberFormat("es-CO", {
        minimumFractionDigits: 0,
        maximumFractionDigits: 2
    });

    const monthLabels = ["Enero", "Febrero", "Marzo", "Abril", "Mayo", "Junio", "Julio", "Agosto", "Septiembre", "Octubre", "Noviembre", "Diciembre"];
    const weekdayLabels = ["Domingo", "Lunes", "Martes", "Miércoles", "Jueves", "Viernes", "Sábado"];
    const copiersMaintenancePageSize = 100;
    const copiersMaintenanceStatusCompleted = 645250000;
    const copiersMaintenanceStatusPending = 645250001;

    const state = {
        activeTab: "today",
        todayDashboard: null,
        todayLoading: false,
        year: currentYear,
        period: currentPeriod,
        value: currentValue,
        billingDashboard: null,
        billingSubtab: "overview",
        billingCreditNotesDetail: null,
        billingCreditNotesLoading: false,
        billingCreditNotesSearchTerm: "",
        billingCreditNotesStatusFilter: "all",
        cloudBillingDashboard: null,
        cloudBillingGroups: [],
        cloudBillingActiveGroup: null,
        cloudBillingLoading: false,
        cloudBillingSearchTerm: "",
        cloudBillingStatusFilter: "all",
        billingInvoicesDetail: null,
        billingInvoicesLoading: false,
        billingInvoicesSaving: false,
        billingInvoicesDeleting: false,
        billingInvoicesContractSaving: false,
        billingInvoicesSearchTerm: "",
        billingInvoicesYear: currentYear,
        billingInvoicesMonth: currentValue || currentMonth,
        billingInvoicesPage: 1,
        billingInvoicesPageSize: 50,
        billingInvoicesTotalPages: 1,
        billingInvoicesDuplicatesOnly: false,
        billingInvoicesGrid: {
            sortKey: "emissionDateValue",
            sortDirection: "desc",
            filters: {},
            duplicatesOnly: false
        },
        billingInvoiceDuplicateNumbers: new Set(),
        billingInvoiceSelectedIds: new Set(),
        billingInvoiceClientSuggestions: [],
        billingInvoiceEditorOriginal: null,
        billingReportDetail: null,
        billingReportLoading: false,
        billingReportExporting: false,
        billingReportClientSuggestions: [],
        siigoInvoicesDetail: null,
        siigoInvoicesLoading: false,
        siigoInvoicesDownloading: false,
        siigoCustomers: [],
        siigoCustomersLoading: false,
        siigoCustomerNitSearching: false,
        copiersDashboard: null,
        copiersEquipmentDashboard: null,
        copiersInventoryDashboard: null,
        copiersMovementsDashboard: null,
        copiersCountersDashboard: null,
        copiersCountersSignature: "",
        taxesDashboard: null,
        taxesActiveRecurringKey: "retefuente",
        taxesActiveOtherKey: "income-tax",
        taxesReteFuenteTableKey: "autofuente",
        taxesVatTableKey: "generated",
        taxesVatVerticalKey: "all",
        taxesFilters: {
            reteFuenteYear: currentYear,
            reteFuenteMonth: currentMonth,
            reteIcaYear: Math.max(currentYear, 2026),
            reteIcaPeriod: currentBimonthly,
            ivaYear: Math.max(currentYear, 2026),
            ivaPeriod: currentFourMonthly,
            incomeTaxYear: Math.max(currentYear, 2025)
        },
        portfolioDashboard: null,
        businessDashboard: null,
        businessSubtab: "all",
        pnlDashboard: null,
        billingSignature: "",
        taxesSignature: "",
        pnlSignature: "",
        copiersSubtab: "billing",
        copiersLoading: false,
        copiersExpandedGroups: new Set(),
        copiersEquipmentLoading: false,
        copiersInventoryLoading: false,
        copiersInventoryExporting: false,
        copiersMovementsLoading: false,
        copiersCountersLoading: false,
        copiersEditorSaving: false,
        copiersEditorOriginal: null,
        copiersClientSuggestions: [],
        copiersProductSuggestions: [],
        copiersClientInvoicesLoading: false,
        copiersClientInvoicesRequestSequence: 0,
        copiersLineEquipmentDetail: null,
        copiersLineEquipmentDraftIds: new Set(),
        copiersLineEquipmentLoading: false,
        copiersLineEquipmentSaving: false,
        copiersEquipmentDetail: null,
        copiersEquipmentDetailLoading: false,
        copiersEquipmentAssignmentSaving: false,
        copiersEquipmentClientSuggestions: [],
        copiersMaintenanceYear: "all",
        copiersMaintenanceMonth: "all",
        copiersMaintenanceOwner: "all",
        copiersMaintenanceGrid: {
            sortKey: "dateValue",
            sortDirection: "desc",
            filters: {}
        },
        copiersMaintenancePage: 1,
        copiersMovementsGrid: {
            sortKey: "dateValue",
            sortDirection: "desc",
            filters: {}
        },
        copiersCountersYear: currentYear,
        copiersCountersMonth: currentValue,
        copiersCountersClientId: "",
        copiersCountersClientName: "",
        copiersCountersClientSuggestions: [],
        copiersCountersHasAppliedFilters: false,
        portfolioSubtab: "monthly",
        portfolioMonthlyRange: "year",
        portfolioMonthlyYear: currentYear,
        portfolioMonthlyMonth: `${currentYear}-${String(currentValue || currentMonth).padStart(2, "0")}`,
        portfolioMonthlyStart: `${currentYear}-01`,
        portfolioMonthlyEnd: `${currentYear}-12`,
        portfolioSearchTerm: "",
        portfolioInvoicesSearchTerm: "",
        accountStatementDetail: null,
        accountStatementClientSuggestions: [],
        accountStatementLoading: false,
        accountStatementPdfLoading: false,
        accountStatementRequestSequence: 0,
        portfolioGrids: {
            overdue: {
                sortKey: "ageDays",
                sortDirection: "desc",
                filters: {}
            },
            invoices: {
                sortKey: "emissionDateValue",
                sortDirection: "desc",
                filters: {}
            }
        },
        pnlYear: currentYear,
        pnlMonth: new Date().getMonth() + 1,
        pnlVertical: "all",
        businessBillingDashboard: null,
        businessBillingSignature: "",
        businessBillingStart: `${currentYear}-01`,
        businessBillingEnd: `${currentYear}-${String(currentMonth).padStart(2, "0")}`,
        businessBillingGranularity: "month",
        businessBillingLoading: false,
        licenciamientoDashboard: null,
        licenciamientoSignature: "",
        licenciamientoYear: licenciamientoDefaultYear,
        licenciamientoMonth: licenciamientoDefaultMonth,
        utilityDashboard: null,
        utilitySignature: "",
        utilityLoading: false,
        utilityAssigningRecordId: "",
        utilityBreakdownCardKey: "",
        utilityExcludedTheoreticalRowIds: new Set(),
        utilitySavedExcludedTheoreticalRowIds: new Set(),
        utilityBreakdownDirty: false,
        utilityRealDetailContext: null,
        ytdDashboard: null,
        ytdLoading: false,
        ytdYear: currentYear,
        ytdRevenueBreakdown: "global",
        ytdExpenseBreakdown: "global",
        ytdRevenueCategoryKeys: new Set(),
        ytdRevenueClientKeys: new Set(),
        ytdRevenueVerticalKeys: new Set(),
        ytdRevenueContractTypeKeys: new Set(),
        ytdExpenseCategoryKeys: new Set(),
        ytdExpenseClientKeys: new Set(),
        ytdExpenseVerticalKeys: new Set(),
        ytdExpenseContractTypeKeys: new Set(),
        ytdSegmentDetails: {},
        ytdDetailRecords: {},
        ytdBulkSaving: false,
        ytdSegmentDetailCounter: 0,
        pnlDetail: null,
        pnlDetailContext: null,
        pnlDetailLoading: false,
        pnlDetailSavingRecordId: "",
        periodLoading: false,
        portfolioLoading: false,
        businessLoading: false,
        pnlLoading: false,
        licenciamientoLoading: false,
        agentMessages: [],
        agentLoading: false,
        agentFeedbackById: {},
        agentFeedbackActiveId: "",
        agentLearningLoaded: false,
        agentLearningLoading: false
    };

    let accountStatementSearchTimer = 0;

    function escapeHtml(value) {
        return (value ?? "").toString()
            .replace(/&/g, "&amp;")
            .replace(/</g, "&lt;")
            .replace(/>/g, "&gt;")
            .replace(/"/g, "&quot;")
            .replace(/'/g, "&#39;");
    }

    function setAgentLoading(isLoading) {
        state.agentLoading = Boolean(isLoading);
        if (dashboardAgentSendButton) {
            dashboardAgentSendButton.disabled = state.agentLoading;
            dashboardAgentSendButton.textContent = state.agentLoading ? "Consultando..." : "Enviar";
        }

        if (dashboardAgentInput) {
            dashboardAgentInput.disabled = state.agentLoading;
        }

        if (dashboardAgentStatus) {
            dashboardAgentStatus.textContent = state.agentLoading ? "Consultando Dataverse..." : "";
            dashboardAgentStatus.classList.toggle("is-loading", state.agentLoading);
        }
    }

    function splitMarkdownTableRow(line) {
        return (line || "")
            .trim()
            .replace(/^\|/, "")
            .replace(/\|$/, "")
            .split("|")
            .map(cell => cell.trim());
    }

    function isMarkdownTableSeparator(line) {
        return /^\s*\|?\s*:?-{3,}:?\s*(\|\s*:?-{3,}:?\s*)+\|?\s*$/.test(line || "");
    }

    function renderMarkdownTable(lines, startIndex) {
        const headers = splitMarkdownTableRow(lines[startIndex]);
        let cursor = startIndex + 2;
        const rows = [];
        while (cursor < lines.length && (lines[cursor] || "").includes("|")) {
            rows.push(splitMarkdownTableRow(lines[cursor]));
            cursor += 1;
        }

        const tableHtml = `
            <div class="dashboard-agent-table-scroll">
                <table class="dashboard-agent-table">
                    <thead><tr>${headers.map(header => `<th>${escapeHtml(header)}</th>`).join("")}</tr></thead>
                    <tbody>
                        ${rows.map(row => `<tr>${headers.map((_, index) => `<td>${escapeHtml(row[index] || "")}</td>`).join("")}</tr>`).join("")}
                    </tbody>
                </table>
            </div>`;

        return { html: tableHtml, nextIndex: cursor };
    }

    function renderAgentAnswerHtml(content) {
        const lines = (content || "").toString().split(/\r?\n/);
        const chunks = [];
        let paragraph = [];

        const flushParagraph = () => {
            if (paragraph.length === 0) {
                return;
            }
            chunks.push(`<p>${paragraph.map(line => escapeHtml(line)).join("<br />")}</p>`);
            paragraph = [];
        };

        for (let index = 0; index < lines.length;) {
            if (index + 1 < lines.length && (lines[index] || "").includes("|") && isMarkdownTableSeparator(lines[index + 1])) {
                flushParagraph();
                const rendered = renderMarkdownTable(lines, index);
                chunks.push(rendered.html);
                index = rendered.nextIndex;
                continue;
            }

            if (!lines[index].trim()) {
                flushParagraph();
                index += 1;
                continue;
            }

            paragraph.push(lines[index]);
            index += 1;
        }

        flushParagraph();
        return chunks.join("");
    }

    function renderAgentStructuredTables(tables) {
        const validTables = Array.isArray(tables)
            ? tables.filter(table => table && Array.isArray(table.columns) && Array.isArray(table.rows) && table.rows.length > 0)
            : [];
        if (validTables.length === 0) {
            return "";
        }

        return validTables.map(table => {
            const columns = table.columns || [];
            const hiddenRows = Math.max(0, (table.totalRows || table.rows.length) - table.rows.length);
            return `
                <section class="dashboard-agent-result-table">
                    <header class="dashboard-agent-result-table__header">
                        <div>
                            <strong>${escapeHtml(table.title || "Resultados")}</strong>
                            ${table.description ? `<small>${escapeHtml(table.description)}</small>` : ""}
                        </div>
                        <span>${escapeHtml(`${table.totalRows || table.rows.length} fila(s)`)}</span>
                    </header>
                    <div class="dashboard-agent-table-scroll">
                        <table class="dashboard-agent-table">
                            <thead>
                                <tr>${columns.map(column => `<th>${escapeHtml(column.label || column.key)}</th>`).join("")}</tr>
                            </thead>
                            <tbody>
                                ${table.rows.map(row => `
                                    <tr>${columns.map(column => `<td>${escapeHtml(row?.[column.key] ?? "")}</td>`).join("")}</tr>
                                `).join("")}
                            </tbody>
                        </table>
                    </div>
                    ${hiddenRows > 0 ? `<div class="dashboard-agent-result-table__note">Mostrando ${table.rows.length} de ${table.totalRows} filas. Descarga Excel para ver todo.</div>` : ""}
                </section>`;
        }).join("");
    }

    function renderAgentExport(exportInfo) {
        if (!exportInfo?.exportId || !app.dataset.agentExportUrl) {
            return "";
        }

        const href = `${app.dataset.agentExportUrl}?id=${encodeURIComponent(exportInfo.exportId)}`;
        return `
            <div class="dashboard-agent-export">
                <a class="btn btn-outline-primary dashboard-agent-export__button" href="${escapeHtml(href)}" download="${escapeHtml(exportInfo.fileName || "dashboard-agent.xlsx")}">Descargar Excel</a>
                <small>${escapeHtml(exportInfo.label || "Resultados")} · ${escapeHtml(`${exportInfo.recordsCount || 0} fila(s)`)}</small>
            </div>`;
    }

    function createAgentFeedbackId() {
        return `agent-feedback-${Date.now()}-${Math.random().toString(36).slice(2, 9)}`;
    }

    function renderAgentFeedbackActions(feedbackId) {
        if (!feedbackId || !app.dataset.agentFeedbackUrl) {
            return "";
        }

        return `
            <div class="dashboard-agent-feedback-actions" aria-label="Retroalimentacion de respuesta">
                <button type="button" class="dashboard-agent-feedback-actions__button" data-agent-feedback-type="incorrect">Respuesta incorrecta</button>
                <button type="button" class="dashboard-agent-feedback-actions__button" data-agent-feedback-type="missing-data">Faltan datos</button>
                <button type="button" class="dashboard-agent-feedback-actions__button" data-agent-feedback-type="learning">Enviar para aprendizaje</button>
            </div>`;
    }

    function setAgentInlineStatus(target, baseClass, type, message) {
        if (!target) {
            return;
        }

        target.className = `${baseClass}${type ? ` is-${type}` : ""}`;
        target.textContent = message || "";
    }

    function setAgentFeedbackStatus(type, message) {
        setAgentInlineStatus(dashboardAgentFeedbackStatus, "dashboard-agent-feedback-form__status", type, message);
    }

    function setAgentLearningStatus(type, message) {
        setAgentInlineStatus(dashboardAgentLearningStatus, "dashboard-agent-learning__status", type, message);
    }

    function closeAgentFeedbackModal() {
        if (!dashboardAgentFeedbackModal || dashboardAgentFeedbackModal.hidden) {
            return;
        }

        dashboardAgentFeedbackModal.hidden = true;
        document.body.classList.remove("dashboard-modal-open");
        state.agentFeedbackActiveId = "";
        if (dashboardAgentFeedbackForm) {
            dashboardAgentFeedbackForm.reset();
        }
        setAgentFeedbackStatus("", "");
        if (dashboardAgentFeedbackSubmitButton) {
            dashboardAgentFeedbackSubmitButton.disabled = false;
            dashboardAgentFeedbackSubmitButton.textContent = "Guardar";
        }
    }

    function openAgentFeedbackModal(feedbackId, category) {
        const payload = state.agentFeedbackById[feedbackId];
        if (!payload || !dashboardAgentFeedbackModal) {
            return;
        }

        state.agentFeedbackActiveId = feedbackId;
        if (dashboardAgentFeedbackMessageId) {
            dashboardAgentFeedbackMessageId.value = feedbackId;
        }
        if (dashboardAgentFeedbackCategory) {
            dashboardAgentFeedbackCategory.value = category || "learning";
        }
        if (dashboardAgentFeedbackExpected) {
            dashboardAgentFeedbackExpected.value = "";
        }
        if (dashboardAgentFeedbackNotes) {
            dashboardAgentFeedbackNotes.value = "";
        }

        setAgentFeedbackStatus("", "");
        dashboardAgentFeedbackModal.hidden = false;
        document.body.classList.add("dashboard-modal-open");
        dashboardAgentFeedbackExpected?.focus();
    }

    function markAgentFeedbackSent(feedbackId, message) {
        if (!dashboardAgentMessages || !feedbackId) {
            return;
        }

        const container = dashboardAgentMessages.querySelector(`[data-agent-feedback-id="${feedbackId}"] .dashboard-agent-feedback-actions`);
        if (container) {
            container.innerHTML = `<span class="dashboard-agent-feedback-actions__sent">${escapeHtml(message || "Enviado a aprendizaje")}</span>`;
        }
    }

    async function submitAgentFeedback(event) {
        event?.preventDefault();
        const feedbackId = dashboardAgentFeedbackMessageId?.value || state.agentFeedbackActiveId;
        const payload = state.agentFeedbackById[feedbackId];
        if (!payload || !app.dataset.agentFeedbackUrl) {
            setAgentFeedbackStatus("error", "No encontre la respuesta para enviar a aprendizaje.");
            return;
        }

        const request = {
            question: payload.question || "",
            answer: payload.answer || "",
            category: dashboardAgentFeedbackCategory?.value || "learning",
            expectedAnswer: dashboardAgentFeedbackExpected?.value || "",
            notes: dashboardAgentFeedbackNotes?.value || "",
            sources: payload.sources || [],
            contextSummary: payload.contextSummary || null
        };

        if (dashboardAgentFeedbackSubmitButton) {
            dashboardAgentFeedbackSubmitButton.disabled = true;
            dashboardAgentFeedbackSubmitButton.textContent = "Guardando...";
        }
        setAgentFeedbackStatus("info", "Guardando solicitud de aprendizaje...");

        try {
            const result = await fetchJson(app.dataset.agentFeedbackUrl, {
                method: "POST",
                body: JSON.stringify(request)
            });
            const message = result?.message || "Solicitud enviada a aprendizaje.";
            setAgentFeedbackStatus("success", message);
            markAgentFeedbackSent(feedbackId, "Enviado a aprendizaje");
            state.agentLearningLoaded = false;
            if (dashboardAgentLearningPanel?.open) {
                await loadAgentLearning(true);
            }
            window.setTimeout(closeAgentFeedbackModal, 650);
        } catch (error) {
            setAgentFeedbackStatus("error", error instanceof Error ? error.message : "No fue posible enviar la solicitud.");
        } finally {
            if (dashboardAgentFeedbackSubmitButton && !dashboardAgentFeedbackModal?.hidden) {
                dashboardAgentFeedbackSubmitButton.disabled = false;
                dashboardAgentFeedbackSubmitButton.textContent = "Guardar";
            }
        }
    }

    function getAgentLearningCategoryLabel(category) {
        const normalized = (category || "").toString().toLowerCase();
        if (normalized === "incorrect") {
            return "Respuesta incorrecta";
        }
        if (normalized === "missing-data") {
            return "Faltan datos";
        }
        if (normalized === "other") {
            return "Otro";
        }
        return "Aprendizaje";
    }

    function getAgentLearningStatusLabel(status) {
        const normalized = (status || "").toString().toLowerCase();
        if (normalized === "reviewed") {
            return "Revisado";
        }
        if (normalized === "implemented") {
            return "Implementado";
        }
        if (normalized === "discarded") {
            return "Descartado";
        }
        return "Pendiente";
    }

    function renderAgentLearningStatusOptions(currentStatus) {
        const statuses = [
            ["pending", "Pendiente"],
            ["reviewed", "Revisado"],
            ["implemented", "Implementado"],
            ["discarded", "Descartado"]
        ];
        const normalized = (currentStatus || "pending").toString().toLowerCase();
        return statuses
            .map(([value, label]) => `<option value="${value}"${value === normalized ? " selected" : ""}>${label}</option>`)
            .join("");
    }

    function truncateAgentLearningText(value, maxLength = 420) {
        const text = (value || "").toString().trim();
        return text.length <= maxLength
            ? text
            : `${text.slice(0, maxLength)}...`;
    }

    function renderAgentLearningRows(board) {
        const rows = Array.isArray(board?.rows) ? board.rows : [];
        const storageLabel = board?.storage === "dataverse" ? "Dataverse" : "respaldo local";
        if (dashboardAgentLearningSummary) {
            dashboardAgentLearningSummary.textContent = `${rows.length} solicitud(es) - ${storageLabel}`;
        }
        if (!dashboardAgentLearningList) {
            return;
        }

        if (rows.length === 0) {
            dashboardAgentLearningList.innerHTML = '<div class="dashboard-agent-learning__empty">No hay solicitudes de aprendizaje registradas.</div>';
            return;
        }

        dashboardAgentLearningList.innerHTML = rows.map(row => {
            const status = (row.status || "pending").toString().toLowerCase();
            return `
                <article class="dashboard-agent-learning-row" data-agent-learning-id="${escapeHtml(row.feedbackId || "")}">
                    <header class="dashboard-agent-learning-row__header">
                        <div>
                            <span class="dashboard-agent-learning-row__badge">${escapeHtml(getAgentLearningCategoryLabel(row.category))}</span>
                            <strong>${escapeHtml(truncateAgentLearningText(row.question, 180))}</strong>
                        </div>
                        <span class="dashboard-agent-learning-row__status dashboard-agent-learning-row__status--${escapeHtml(status)}">${escapeHtml(getAgentLearningStatusLabel(status))}</span>
                    </header>
                    <div class="dashboard-agent-learning-row__meta">
                        <span>${escapeHtml(row.createdOnDisplay || row.createdOnValue || "Sin fecha")}</span>
                        <span>${escapeHtml(row.createdByName || row.createdByEmail || "Usuario")}</span>
                    </div>
                    ${row.expectedAnswer ? `<div class="dashboard-agent-learning-row__block"><strong>Esperado</strong><p>${escapeHtml(truncateAgentLearningText(row.expectedAnswer, 520))}</p></div>` : ""}
                    ${row.notes ? `<div class="dashboard-agent-learning-row__block"><strong>Notas</strong><p>${escapeHtml(truncateAgentLearningText(row.notes, 520))}</p></div>` : ""}
                    <details class="dashboard-agent-learning-row__details">
                        <summary>Ver respuesta y contexto</summary>
                        <div class="dashboard-agent-learning-row__block">
                            <strong>Respuesta del agente</strong>
                            <p>${escapeHtml(truncateAgentLearningText(row.answer, 900))}</p>
                        </div>
                        <div class="dashboard-agent-learning-row__json">
                            <strong>Fuentes</strong>
                            <pre>${escapeHtml(row.sourcesJson || "[]")}</pre>
                        </div>
                        <div class="dashboard-agent-learning-row__json">
                            <strong>Contexto</strong>
                            <pre>${escapeHtml(row.contextSummaryJson || "{}")}</pre>
                        </div>
                    </details>
                    <div class="dashboard-agent-learning-row__review">
                        <select class="form-select" data-agent-learning-status>
                            ${renderAgentLearningStatusOptions(status)}
                        </select>
                        <input type="text" class="form-control" data-agent-learning-review-notes maxlength="3900" placeholder="Nota de revision" value="${escapeHtml(row.reviewNotes || "")}" />
                        <button type="button" class="btn btn-outline-primary" data-agent-learning-save>Guardar estado</button>
                    </div>
                </article>`;
        }).join("");
    }

    async function loadAgentLearning(force = false) {
        if (!app.dataset.agentLearningUrl || state.agentLearningLoading || (state.agentLearningLoaded && !force)) {
            return;
        }

        state.agentLearningLoading = true;
        if (dashboardAgentLearningRefreshButton) {
            dashboardAgentLearningRefreshButton.disabled = true;
        }
        setAgentLearningStatus("info", "Cargando solicitudes...");

        try {
            const board = await fetchJson(app.dataset.agentLearningUrl);
            state.agentLearningLoaded = true;
            renderAgentLearningRows(board);
            setAgentLearningStatus("", "");
        } catch (error) {
            setAgentLearningStatus("error", error instanceof Error ? error.message : "No fue posible cargar aprendizaje.");
        } finally {
            state.agentLearningLoading = false;
            if (dashboardAgentLearningRefreshButton) {
                dashboardAgentLearningRefreshButton.disabled = false;
            }
        }
    }

    async function saveAgentLearningStatus(button) {
        const row = button?.closest("[data-agent-learning-id]");
        const feedbackId = row?.dataset.agentLearningId || "";
        if (!feedbackId || !app.dataset.agentLearningStatusUrl) {
            setAgentLearningStatus("error", "No encontre el registro para actualizar.");
            return;
        }

        const status = row.querySelector("[data-agent-learning-status]")?.value || "pending";
        const reviewNotes = row.querySelector("[data-agent-learning-review-notes]")?.value || "";
        button.disabled = true;
        setAgentLearningStatus("info", "Actualizando estado...");
        try {
            const result = await fetchJson(app.dataset.agentLearningStatusUrl, {
                method: "POST",
                body: JSON.stringify({
                    feedbackId,
                    status,
                    reviewNotes
                })
            });
            setAgentLearningStatus("success", result?.message || "Estado actualizado.");
            state.agentLearningLoaded = false;
            await loadAgentLearning(true);
        } catch (error) {
            setAgentLearningStatus("error", error instanceof Error ? error.message : "No fue posible actualizar el estado.");
        } finally {
            button.disabled = false;
        }
    }

    function appendAgentMessage(role, content, options = {}) {
        if (!dashboardAgentMessages) {
            return;
        }

        const message = document.createElement("div");
        const normalizedRole = role === "user" ? "user" : "assistant";
        message.className = `dashboard-agent-message dashboard-agent-message--${normalizedRole}`;
        if (options.feedbackId) {
            message.dataset.agentFeedbackId = options.feedbackId;
        }
        const bubble = document.createElement("div");
        bubble.className = "dashboard-agent-message__bubble";
        bubble.innerHTML = normalizedRole === "assistant"
            ? `${renderAgentAnswerHtml(content || "")}${renderAgentStructuredTables(options.tables)}${renderAgentExport(options.export)}${renderAgentFeedbackActions(options.feedbackId)}`
            : escapeHtml(content || "").replace(/\n/g, "<br />");
        message.appendChild(bubble);
        dashboardAgentMessages.appendChild(message);
        dashboardAgentMessages.scrollTop = dashboardAgentMessages.scrollHeight;
    }

    function renderAgentSources(sources, contextSummary) {
        if (!dashboardAgentSources) {
            return;
        }

        const items = Array.isArray(sources) ? sources.filter(source => source && (source.label || source.table)) : [];
        const candidateTables = Array.isArray(contextSummary?.candidateTables) ? contextSummary.candidateTables : [];
        const dataSections = Array.isArray(contextSummary?.dataSections) ? contextSummary.dataSections : [];
        const missingResolvers = Array.isArray(contextSummary?.missingResolvers) ? contextSummary.missingResolvers : [];
        const hasContextSummary = candidateTables.length > 0 || dataSections.length > 0 || missingResolvers.length > 0 || contextSummary?.learningReviewReason;
        dashboardAgentSources.hidden = items.length === 0 && !hasContextSummary;
        if (items.length === 0 && !hasContextSummary) {
            dashboardAgentSources.innerHTML = "";
            return;
        }

        dashboardAgentSources.innerHTML = `
            ${items.length > 0 ? `<span class="dashboard-agent__sources-label">Fuentes</span>
            <div class="dashboard-agent__source-list">
                ${items.map(source => `
                    <span class="dashboard-agent__source">
                        <strong>${escapeHtml(source.label || source.table || "Dataverse")}</strong>
                        <small>${escapeHtml(source.detail || source.table || "")}</small>
                    </span>
                `).join("")}
            </div>` : ""}
            ${hasContextSummary ? `
                <details class="dashboard-agent__context">
                    <summary>Contexto consultado</summary>
                    <div class="dashboard-agent__context-grid">
                        <div>
                            <strong>Tablas candidatas</strong>
                            <small>${candidateTables.length ? escapeHtml(candidateTables.join(", ")) : "Sin candidatas"}</small>
                        </div>
                        <div>
                            <strong>Secciones cargadas</strong>
                            <small>${dataSections.length ? escapeHtml(dataSections.join(", ")) : "Sin datos cargados"}</small>
                        </div>
                        <div>
                            <strong>Pendientes</strong>
                            <small>${missingResolvers.length ? escapeHtml(missingResolvers.join(", ")) : "Sin pendientes"}</small>
                        </div>
                        ${contextSummary?.learningReviewReason ? `
                            <div>
                                <strong>Aprendizaje</strong>
                                <small>${escapeHtml(contextSummary.learningReviewReason)}</small>
                            </div>` : ""}
                    </div>
                </details>` : ""}`;
    }

    async function submitAgentQuestion(rawQuestion) {
        const question = (rawQuestion || "").trim();
        if (!question || state.agentLoading || !app.dataset.agentUrl) {
            return;
        }

        const history = state.agentMessages.slice(-8);
        state.agentMessages.push({ role: "user", content: question });
        appendAgentMessage("user", question);
        renderAgentSources([]);
        if (dashboardAgentInput) {
            dashboardAgentInput.value = "";
        }

        setAgentLoading(true);
        try {
            const response = await fetchJson(app.dataset.agentUrl, {
                method: "POST",
                body: JSON.stringify({
                    message: question,
                    history
                })
            });
            const answer = response?.answer || "No encontre una respuesta para esa pregunta.";
            state.agentMessages.push({ role: "assistant", content: answer });
            const feedbackId = createAgentFeedbackId();
            state.agentFeedbackById[feedbackId] = {
                question,
                answer,
                sources: response?.sources || [],
                contextSummary: response?.contextSummary || null
            };
            appendAgentMessage("assistant", answer, {
                tables: response?.tables || [],
                export: response?.export || null,
                feedbackId
            });
            renderAgentSources(response?.sources || [], response?.contextSummary || null);
        } catch (error) {
            const message = error instanceof Error ? error.message : "No fue posible responder con el agente.";
            state.agentMessages.push({ role: "assistant", content: message });
            const feedbackId = createAgentFeedbackId();
            state.agentFeedbackById[feedbackId] = {
                question,
                answer: message,
                sources: [],
                contextSummary: null
            };
            appendAgentMessage("assistant", message, { feedbackId });
            renderAgentSources([]);
        } finally {
            setAgentLoading(false);
            dashboardAgentInput?.focus();
        }
    }

    function normalizeText(value) {
        return (value ?? "")
            .toString()
            .normalize("NFD")
            .replace(/[\u0300-\u036f]/g, "")
            .toLowerCase()
            .trim();
    }

    function getPeriodSignature() {
        return `${state.year}|${state.period}|${state.value}`;
    }

    function getTaxesSignature() {
        const filters = state.taxesFilters || {};
        return [
            filters.reteFuenteYear,
            filters.reteFuenteMonth,
            filters.reteIcaYear,
            filters.reteIcaPeriod,
            filters.ivaYear,
            filters.ivaPeriod,
            filters.incomeTaxYear
        ].join("|");
    }

    function getPnlSignature() {
        return `${state.pnlYear}|${state.pnlMonth}|${state.pnlVertical}`;
    }

    function getBusinessBillingSignature() {
        return `${normalizeBusinessBillingMonthKey(state.businessBillingStart)}|${normalizeBusinessBillingMonthKey(state.businessBillingEnd)}|${state.businessBillingGranularity || "month"}`;
    }

    function syncBusinessBillingFilterControls() {
        if (businessBillingStartFilter) {
            businessBillingStartFilter.value = normalizeBusinessBillingMonthKey(state.businessBillingStart) || `${currentYear}-01`;
        }

        if (businessBillingEndFilter) {
            businessBillingEndFilter.value = normalizeBusinessBillingMonthKey(state.businessBillingEnd) || `${currentYear}-${String(currentMonth).padStart(2, "0")}`;
        }

        if (businessBillingGranularityFilter) {
            businessBillingGranularityFilter.value = state.businessBillingGranularity || "month";
        }
    }

    function updateBusinessBillingFiltersFromControls() {
        let start = normalizeBusinessBillingMonthKey(businessBillingStartFilter?.value) || state.businessBillingStart || `${currentYear}-01`;
        let end = normalizeBusinessBillingMonthKey(businessBillingEndFilter?.value) || state.businessBillingEnd || start;
        if (getPortfolioMonthOrder(start) > getPortfolioMonthOrder(end)) {
            [start, end] = [end, start];
        }

        state.businessBillingStart = start;
        state.businessBillingEnd = end;
        state.businessBillingGranularity = businessBillingGranularityFilter?.value || state.businessBillingGranularity || "month";
        syncBusinessBillingFilterControls();
    }

    function getLicenciamientoSignature() {
        return `${state.licenciamientoYear}|${state.licenciamientoMonth}`;
    }

    function getUtilitySignature() {
        return "2025-to-date";
    }

    function readUtilityStoredTheoreticalExclusions() {
        try {
            const raw = window.localStorage?.getItem(utilityTheoreticalExclusionsStorageKey) || "";
            if (!raw) {
                return new Set();
            }

            const parsed = JSON.parse(raw);
            const ids = Array.isArray(parsed)
                ? parsed
                : Array.isArray(parsed?.excludedRowIds) ? parsed.excludedRowIds : [];
            return new Set(ids.map(item => String(item || "").trim()).filter(Boolean));
        } catch {
            return new Set();
        }
    }

    function writeUtilityStoredTheoreticalExclusions(ids) {
        const payload = {
            excludedRowIds: Array.from(ids || []).filter(Boolean),
            savedAt: new Date().toISOString()
        };
        window.localStorage?.setItem(utilityTheoreticalExclusionsStorageKey, JSON.stringify(payload));
    }

    function getCopiersCountersSignature() {
        return `${state.copiersCountersYear}|${state.copiersCountersMonth}|${state.copiersCountersClientId || ""}|${normalizeText(state.copiersCountersClientName || "")}`;
    }

    function formatMetric(value, format) {
        const numericValue = Number(value || 0);
        if (format === "number") {
            return numberFormatter.format(numericValue);
        }

        if (format === "days") {
            return `${numberFormatter.format(numericValue)} dias`;
        }

        if (format === "percent") {
            return `${numberFormatter.format(numericValue)}%`;
        }

        return currencyFormatter.format(numericValue);
    }

    function formatUsd(value) {
        return usdCurrencyFormatter.format(Number(value || 0));
    }

    function formatBusinessMetric(value, format) {
        if (format === "number") {
            return numberFormatter.format(Number(value || 0));
        }

        if (format === "currency") {
            return currencyFormatter.format(Number(value || 0));
        }

        if (format === "percent") {
            return formatPercent(value);
        }

        return formatUsd(value);
    }

    function formatNullableNumber(value) {
        if (value === null || value === undefined || value === "") {
            return "—";
        }

        return numberFormatter.format(Number(value || 0));
    }

    function formatGrowth(value) {
        if (value === null || value === undefined) {
            return "Nuevo";
        }

        const numericValue = Number(value || 0);
        const prefix = numericValue > 0 ? "+" : "";
        return `${prefix}${numberFormatter.format(numericValue)}%`;
    }

    function formatPercent(value) {
        return `${numberFormatter.format(Number(value || 0))}%`;
    }

    function formatCompactMillions(value) {
        const numericValue = Number(value || 0);
        const sign = numericValue < 0 ? "-" : "";
        const millions = Math.abs(numericValue) / 1000000;
        const roundedTenths = Math.round(millions * 10) / 10;
        const hasDecimal = Math.abs(roundedTenths - Math.round(roundedTenths)) >= 0.05;
        return `${sign}${roundedTenths.toLocaleString("es-CO", {
            minimumFractionDigits: hasDecimal ? 1 : 0,
            maximumFractionDigits: hasDecimal ? 1 : 0
        })}M`;
    }

    function setStatus(target, type, message) {
        if (!target) {
            return;
        }

        if (!message) {
            target.className = "dashboard-status";
            target.textContent = "";
            return;
        }

        target.className = `dashboard-status show ${type}`;
        target.textContent = message;
    }

    function setTodayLoading(loading) {
        state.todayLoading = Boolean(loading);
        if (dashboardTodayRefreshButton) {
            dashboardTodayRefreshButton.disabled = state.todayLoading;
            dashboardTodayRefreshButton.textContent = state.todayLoading ? "Actualizando..." : "Actualizar";
        }

        dashboardTodayCards?.setAttribute("aria-busy", state.todayLoading ? "true" : "false");
    }

    function formatTodayValue(value, format) {
        if (format === "currency") {
            return currencyFormatter.format(Number(value || 0));
        }
        if (format === "usd") {
            return usdUnitFormatter.format(Number(value || 0));
        }
        return numberFormatter.format(Number(value || 0));
    }

    function resolveTodayGrowth(value, previousValue, growthPercent, showsGrowth) {
        if (!showsGrowth) {
            return null;
        }

        const current = Number(value || 0);
        const previous = Number(previousValue || 0);
        const growth = growthPercent === null || growthPercent === undefined
            ? null
            : Number(growthPercent);

        if (growth === null || !Number.isFinite(growth)) {
            return {
                tone: current > 0 && previous === 0 ? "new" : "neutral",
                icon: current > 0 && previous === 0 ? "●" : "=",
                label: current > 0 && previous === 0 ? "Nuevo vs mes pasado" : "Sin base comparable"
            };
        }

        if (growth > 0) {
            return { tone: "positive", icon: "↑", label: `${numberFormatter.format(growth)}% vs mes pasado` };
        }
        if (growth < 0) {
            return { tone: "negative", icon: "↓", label: `${numberFormatter.format(Math.abs(growth))}% vs mes pasado` };
        }

        return { tone: "neutral", icon: "=", label: "0% vs mes pasado" };
    }

    function renderTodayGrowth(value, previousValue, growthPercent, showsGrowth, compact = false) {
        const growth = resolveTodayGrowth(value, previousValue, growthPercent, showsGrowth);
        if (!growth) {
            return "";
        }

        return `<span class="dashboard-today-growth dashboard-today-growth--${growth.tone}${compact ? " dashboard-today-growth--compact" : ""}"><span aria-hidden="true">${growth.icon}</span>${escapeHtml(growth.label)}</span>`;
    }

    function renderTodayItems(card) {
        const items = Array.isArray(card?.items) ? card.items : [];
        if (!items.length) {
            return "";
        }

        return `<div class="dashboard-today-card__items">${items.map(item => `
            <div class="dashboard-today-card__item">
                <span class="dashboard-today-card__item-label">${escapeHtml(item?.label || "Sin clasificar")}</span>
                <strong>${formatTodayValue(item?.value, card?.valueFormat)}</strong>
                ${item?.showsGrowth
                    ? renderTodayGrowth(item?.value, item?.previousValue, item?.growthPercent, true, true)
                    : ""}
            </div>`).join("")}</div>`;
    }

    function renderTodayDashboard(dashboard) {
        state.todayDashboard = dashboard || null;
        if (dashboardTodayAsOf) {
            dashboardTodayAsOf.textContent = dashboard?.asOfDateLabel || "de hoy";
        }
        if (dashboardTodayCurrentPeriod) {
            dashboardTodayCurrentPeriod.textContent = dashboard?.currentPeriodLabel || "el mes a la fecha";
        }
        if (dashboardTodayComparisonPeriod) {
            dashboardTodayComparisonPeriod.textContent = dashboard?.comparisonPeriodLabel || "los mismos dias del mes anterior";
        }

        if (!dashboardTodayCards) {
            return;
        }

        const cards = Array.isArray(dashboard?.cards) ? dashboard.cards : [];
        if (!cards.length) {
            dashboardTodayCards.innerHTML = '<div class="dashboard-empty-state"><strong>Sin resumen disponible</strong><span>No encontramos datos para construir las tarjetas de hoy.</span></div>';
            return;
        }

        dashboardTodayCards.innerHTML = cards.map(card => {
            const format = card?.valueFormat || "number";
            const previous = card?.showsGrowth
                ? `<div class="dashboard-today-card__previous">Mes anterior: <strong>${formatTodayValue(card?.previousValue, format)}</strong></div>`
                : '<div class="dashboard-today-card__previous">Corte actual</div>';

            return `
                <button type="button"
                        class="dashboard-today-card dashboard-today-card--${escapeHtml(card?.key || "summary")}" 
                        data-today-card
                        data-today-destination="${escapeHtml(card?.destinationTab || "")}" 
                        data-today-subtab="${escapeHtml(card?.destinationSubtab || "")}" 
                        aria-label="Abrir detalle de ${escapeHtml(card?.title || "esta métrica")}">
                    <span class="dashboard-today-card__eyebrow">${escapeHtml(card?.eyebrow || "Resumen")}</span>
                    <span class="dashboard-today-card__title">${escapeHtml(card?.title || "Métrica")}</span>
                    <strong class="dashboard-today-card__value">${formatTodayValue(card?.value, format)}</strong>
                    ${renderTodayGrowth(card?.value, card?.previousValue, card?.growthPercent, card?.showsGrowth)}
                    ${previous}
                    ${renderTodayItems(card)}
                    <span class="dashboard-today-card__description">${escapeHtml(card?.description || "")}</span>
                    <span class="dashboard-today-card__link">Ver detalle <span aria-hidden="true">→</span></span>
                </button>`;
        }).join("");
    }

    function getBogotaTodayParts() {
        const parts = new Intl.DateTimeFormat("en-US", {
            timeZone: "America/Bogota",
            year: "numeric",
            month: "2-digit",
            day: "2-digit"
        }).formatToParts(new Date());
        const values = Object.fromEntries(parts.map(part => [part.type, part.value]));
        return {
            year: Number(values.year),
            month: Number(values.month),
            day: Number(values.day)
        };
    }

    function buildTodayDateValue(year, month, day) {
        return `${String(year).padStart(4, "0")}-${String(month).padStart(2, "0")}-${String(day).padStart(2, "0")}`;
    }

    function getTodayComparisonScope() {
        const current = getBogotaTodayParts();
        const previousAnchor = new Date(Date.UTC(current.year, current.month - 2, 1));
        const previousYear = previousAnchor.getUTCFullYear();
        const previousMonth = previousAnchor.getUTCMonth() + 1;
        const previousLastDay = new Date(Date.UTC(previousYear, previousMonth, 0)).getUTCDate();
        const previousDay = Math.min(current.day, previousLastDay);
        return {
            today: buildTodayDateValue(current.year, current.month, current.day),
            currentStart: buildTodayDateValue(current.year, current.month, 1),
            previousStart: buildTodayDateValue(previousYear, previousMonth, 1),
            previousEnd: buildTodayDateValue(previousYear, previousMonth, previousDay),
            currentYear: current.year,
            currentMonth: current.month,
            currentDay: current.day,
            previousYear,
            previousMonth,
            previousDay
        };
    }

    function isTodayDateInRange(value, start, end) {
        const normalized = (value || "").trim();
        return /^\d{4}-\d{2}-\d{2}$/.test(normalized)
            && normalized >= start
            && normalized <= end;
    }

    function calculateTodayGrowth(value, previousValue) {
        const current = Number(value || 0);
        const previous = Number(previousValue || 0);
        if (previous === 0) {
            return null;
        }

        return Math.round((((current - previous) / Math.abs(previous)) * 100) * 10) / 10;
    }

    function normalizeTodayKey(value, fallback) {
        const normalized = normalizeText(value)
            .replace(/[^a-z0-9]+/g, "-")
            .replace(/^-+|-+$/g, "");
        return normalized || fallback;
    }

    function resolveTodayInvoiceVertical(row) {
        const label = (row?.verticalLabel || "").trim();
        if (Number(row?.verticalOptionValue) === 645250000 || normalizeText(label).includes("cloud")) {
            return { key: "cloud", label: "Cloud" };
        }
        if (Number(row?.verticalOptionValue) === 645250001 || normalizeText(label).includes("copier")) {
            return { key: "copiers", label: "Copiers" };
        }

        const fallbackLabel = label || "Sin vertical";
        return { key: normalizeTodayKey(fallbackLabel, "sin-vertical"), label: fallbackLabel };
    }

    function resolveTodayExpenseVertical(row) {
        const key = (row?.verticalKey || "").trim();
        const label = (row?.verticalLabel || "").trim();
        if (normalizeText(key) === "cloud" || normalizeText(label).includes("cloud")) {
            return { key: "cloud", label: "Cloud" };
        }
        if (normalizeText(key) === "copiers" || normalizeText(label).includes("copier")) {
            return { key: "copiers", label: "Copiers" };
        }

        const fallbackLabel = label || "Sin vertical";
        return { key: normalizeTodayKey(key || fallbackLabel, "sin-vertical"), label: fallbackLabel };
    }

    function aggregateTodayRows(rows, dimensionResolver, valueSelector) {
        const buckets = new Map();
        (rows || []).forEach(row => {
            const dimension = dimensionResolver(row);
            const existing = buckets.get(dimension.key) || { label: dimension.label, value: 0 };
            existing.value += Number(valueSelector(row) || 0);
            buckets.set(dimension.key, existing);
        });
        return buckets;
    }

    function aggregateTodayOwners(rows, idSelector, nameSelector, valueSelector) {
        const buckets = new Map();
        (rows || []).forEach(row => {
            const id = (idSelector(row) || "").trim();
            const name = (nameSelector(row) || "").trim() || "Sin propietario";
            const key = id ? `id:${id.toLowerCase()}` : `name:${normalizeTodayKey(name, "sin-propietario")}`;
            const existing = buckets.get(key) || { label: name, value: 0 };
            existing.value += Number(valueSelector(row) || 0);
            buckets.set(key, existing);
        });
        return buckets;
    }

    function buildTodayItems(current, previous, showsGrowth) {
        const keys = Array.from(new Set([...current.keys(), ...previous.keys()]));
        const order = key => key === "cloud" ? 0 : key === "copiers" ? 1 : key === "sin-vertical" ? 3 : 2;
        return keys.map(key => {
            const actual = current.get(key) || { label: previous.get(key)?.label || "Sin clasificar", value: 0 };
            const prior = previous.get(key) || { label: actual.label, value: 0 };
            return {
                key,
                label: actual.label || prior.label,
                value: actual.value,
                previousValue: prior.value,
                showsGrowth,
                growthPercent: showsGrowth ? calculateTodayGrowth(actual.value, prior.value) : null
            };
        }).sort((left, right) => order(left.key) - order(right.key)
            || Math.abs(right.value) - Math.abs(left.value)
            || left.label.localeCompare(right.label, "es"));
    }

    function getTodayExpenseRecords(dashboard, year, month, start, end) {
        const monthKey = `${String(year).padStart(4, "0")}-${String(month).padStart(2, "0")}`;
        const points = Array.isArray(dashboard?.chart?.points) ? dashboard.chart.points : [];
        return points
            .filter(point => point?.key === monthKey)
            .flatMap(point => Array.isArray(point?.expenseSegments) ? point.expenseSegments : [])
            .flatMap(segment => Array.isArray(segment?.records) ? segment.records : [])
            .filter(row => isTodayDateInRange(row?.dateDisplay, start, end));
    }

    function buildTodayCard(key, eyebrow, title, description, valueFormat, value, previousValue, showsGrowth, destinationTab, destinationSubtab, items = []) {
        return {
            key,
            eyebrow,
            title,
            description,
            valueFormat,
            value,
            previousValue,
            showsGrowth,
            growthPercent: showsGrowth ? calculateTodayGrowth(value, previousValue) : null,
            destinationTab,
            destinationSubtab,
            items
        };
    }

    function buildTodayDashboardFromExistingData(scope, portfolio, currentYtd, previousYtd, currentSupport, previousSupport, copiersEquipment) {
        const invoices = Array.isArray(portfolio?.invoices) ? portfolio.invoices : [];
        const currentInvoices = invoices.filter(row => isTodayDateInRange(row?.emissionDateValue, scope.currentStart, scope.today));
        const previousInvoices = invoices.filter(row => isTodayDateInRange(row?.emissionDateValue, scope.previousStart, scope.previousEnd));
        const previousExpenseSource = scope.previousYear === scope.currentYear ? currentYtd : previousYtd;
        const currentExpenses = getTodayExpenseRecords(currentYtd, scope.currentYear, scope.currentMonth, scope.currentStart, scope.today);
        const previousExpenses = getTodayExpenseRecords(previousExpenseSource, scope.previousYear, scope.previousMonth, scope.previousStart, scope.previousEnd);
        const maintenanceRows = (Array.isArray(copiersEquipment?.maintenanceRows) ? copiersEquipment.maintenanceRows : [])
            .filter(row => isTodayDateInRange(row?.dateValue, scope.currentStart, scope.today));
        const pendingInvoices = invoices.filter(row => Boolean(row?.isPortfolioPending));
        const overdueInvoices = pendingInvoices.filter(row => Boolean(row?.isOverdue));

        const currentBilling = currentInvoices.reduce((total, row) => total + Number(row?.netTotalInvoice || 0), 0);
        const previousBilling = previousInvoices.reduce((total, row) => total + Number(row?.netTotalInvoice || 0), 0);
        const currentExpenseTotal = currentExpenses.reduce((total, row) => total + Number(row?.value || 0), 0);
        const previousExpenseTotal = previousExpenses.reduce((total, row) => total + Number(row?.value || 0), 0);
        const portfolioTotal = pendingInvoices.reduce((total, row) => total + Number(row?.netTotalInvoice || 0), 0);
        const overduePortfolioTotal = overdueInvoices.reduce((total, row) => total + Number(row?.netTotalInvoice || 0), 0);

        const invoiceItems = (currentRows, previousRows, showsGrowth) => buildTodayItems(
            aggregateTodayRows(currentRows, resolveTodayInvoiceVertical, row => row?.netTotalInvoice),
            aggregateTodayRows(previousRows, resolveTodayInvoiceVertical, row => row?.netTotalInvoice),
            showsGrowth);
        const expenseItems = buildTodayItems(
            aggregateTodayRows(currentExpenses, resolveTodayExpenseVertical, row => row?.value),
            aggregateTodayRows(previousExpenses, resolveTodayExpenseVertical, row => row?.value),
            true);
        const supportItems = buildTodayItems(
            aggregateTodayOwners(currentSupport?.creatorSummaries, row => row?.creatorId, row => row?.creatorName, row => row?.totalTickets),
            aggregateTodayOwners(previousSupport?.creatorSummaries, row => row?.creatorId, row => row?.creatorName, row => row?.totalTickets),
            true);
        const maintenanceItems = buildTodayItems(
            aggregateTodayOwners(maintenanceRows, row => row?.technicianId, row => row?.technicianName, () => 1),
            new Map(),
            false);

        const currentLabel = `1-${scope.currentDay} de ${monthLabels[scope.currentMonth - 1].toLowerCase()} ${scope.currentYear}`;
        const previousLabel = `1-${scope.previousDay} de ${monthLabels[scope.previousMonth - 1].toLowerCase()} ${scope.previousYear}`;
        return {
            asOfDateValue: scope.today,
            asOfDateLabel: `${scope.currentDay} de ${monthLabels[scope.currentMonth - 1].toLowerCase()} de ${scope.currentYear}`,
            currentPeriodLabel: currentLabel,
            comparisonPeriodLabel: previousLabel,
            cards: [
                buildTodayCard("billing", "Facturación", "Facturación a la fecha del mes", "Valor neto emitido por vertical.", "currency", currentBilling, previousBilling, true, "billing", "overview", invoiceItems(currentInvoices, previousInvoices, true)),
                buildTodayCard("invoice-count", "Facturación", "Facturas emitidas a la fecha del mes", "Cantidad emitida en el mismo tramo de cada mes.", "number", currentInvoices.length, previousInvoices.length, true, "billing", "overview"),
                buildTodayCard("expenses", "Gastos", "Gastos a la fecha del mes", "Gasto consolidado por vertical.", "currency", currentExpenseTotal, previousExpenseTotal, true, "ytd", "", expenseItems),
                buildTodayCard("support-cloud", "Soporte Cloud", "Tickets de soporte Cloud del mes", "Tickets creados por propietario.", "number", Number(currentSupport?.totalTickets || 0), Number(previousSupport?.totalTickets || 0), true, "support-cloud", "", supportItems),
                buildTodayCard("copiers-maintenance", "Soporte Copiers", "Mantenimientos de soporte Copiers", "Mantenimientos registrados por propietario.", "number", maintenanceRows.length, 0, false, "copiers", "maintenance", maintenanceItems),
                buildTodayCard("portfolio", "Cartera", "Cartera a la fecha", "Facturas pendientes por vertical.", "currency", portfolioTotal, 0, false, "portfolio", "detail", invoiceItems(pendingInvoices, [], false)),
                buildTodayCard("overdue-portfolio", "Cartera", "Cartera vencida a la fecha", "Facturas vencidas por vertical.", "currency", overduePortfolioTotal, 0, false, "portfolio", "detail", invoiceItems(overdueInvoices, [], false))
            ]
        };
    }

    function buildTodayDataUrl(baseUrl, params) {
        const url = new URL(baseUrl, window.location.origin);
        Object.entries(params).forEach(([key, value]) => url.searchParams.set(key, String(value)));
        return url.toString();
    }

    async function loadTodayFromExistingEndpoints() {
        const scope = getTodayComparisonScope();
        const supportApp = document.getElementById("dashboardSupportCloudApp");
        const supportUrl = supportApp?.dataset.loadUrl || "";
        const portfolioUrl = app.dataset.portfolioUrl || "";
        const ytdUrl = app.dataset.ytdUrl || "";
        const copiersEquipmentUrl = app.dataset.copiersEquipmentUrl || "";
        if (!supportUrl || !portfolioUrl || !ytdUrl || !copiersEquipmentUrl) {
            throw new Error("El Dashboard no tiene configuradas todas las fuentes del resumen de hoy.");
        }

        const previousYtdPromise = scope.previousYear === scope.currentYear
            ? Promise.resolve(null)
            : fetchJson(buildTodayDataUrl(ytdUrl, { year: scope.previousYear }), { cache: "no-store" });
        const [portfolio, currentYtd, previousYtd, currentSupport, previousSupport, copiersEquipment] = await Promise.all([
            fetchJson(portfolioUrl, { cache: "no-store" }),
            fetchJson(buildTodayDataUrl(ytdUrl, { year: scope.currentYear }), { cache: "no-store" }),
            previousYtdPromise,
            fetchJson(buildTodayDataUrl(supportUrl, { startDate: scope.currentStart, endDate: scope.today }), { cache: "no-store" }),
            fetchJson(buildTodayDataUrl(supportUrl, { startDate: scope.previousStart, endDate: scope.previousEnd }), { cache: "no-store" }),
            fetchJson(copiersEquipmentUrl, { cache: "no-store" })
        ]);

        return buildTodayDashboardFromExistingData(
            scope,
            portfolio,
            currentYtd,
            previousYtd,
            currentSupport,
            previousSupport,
            copiersEquipment);
    }

    async function loadToday() {
        if (state.todayLoading) {
            return;
        }

        setTodayLoading(true);
        setStatus(dashboardTodayStatus, "info", "Construyendo el corte ejecutivo de hoy...");
        try {
            const dashboard = todayUrl
                ? await fetchJson(todayUrl, { cache: "no-store" })
                : await loadTodayFromExistingEndpoints();
            renderTodayDashboard(dashboard);
            setStatus(dashboardTodayStatus, "", "");
        } catch (error) {
            setStatus(dashboardTodayStatus, "error", error instanceof Error ? error.message : "No fue posible cargar el resumen de hoy.");
        } finally {
            setTodayLoading(false);
        }
    }

    function openTodayDestination(card) {
        const destination = card?.dataset.todayDestination || "";
        const subtab = card?.dataset.todaySubtab || "";
        if (!destination) {
            return;
        }

        if (destination === "billing") {
            setBillingSubtab(subtab || "overview");
        } else if (destination === "portfolio") {
            setPortfolioSubtab(subtab || "detail");
        } else if (destination === "copiers") {
            setCopiersSubtab(subtab || "maintenance");
        }

        setActiveTab(destination);
        document.querySelector(".dashboard-tabs")?.scrollIntoView({ behavior: "smooth", block: "start" });
    }

    function setPeriodLoading(loading) {
        state.periodLoading = loading;
        [yearFilter, periodFilter, valueFilter, refreshButton].forEach(element => {
            if (element) {
                element.disabled = loading;
            }
        });
    }

    function setBillingReportLoading(loading) {
        state.billingReportLoading = loading;
        [billingReportClientSearch, billingReportLoadButton].forEach(element => {
            if (element) {
                element.disabled = loading || state.billingReportExporting;
            }
        });

        syncBillingReportSelectionSummary();
    }

    function setBillingReportExporting(exporting) {
        state.billingReportExporting = exporting;
        [billingReportClientSearch, billingReportLoadButton].forEach(element => {
            if (element) {
                element.disabled = exporting || state.billingReportLoading;
            }
        });

        syncBillingReportSelectionSummary();
    }

    function setBillingInvoicesLoading(loading) {
        state.billingInvoicesLoading = loading;
        [
            billingInvoicesSearch,
            billingInvoicesMonth,
            billingInvoicesPageSize,
            billingInvoicesRefreshButton,
            billingInvoicesDuplicatesButton,
            billingInvoicesClearFiltersButton
        ].forEach(element => {
            if (element) {
                element.disabled = loading || state.billingInvoicesDeleting || state.billingInvoicesContractSaving;
            }
        });

        syncBillingInvoicesSelectionSummary();
        syncBillingInvoicesPagination();
    }

    function syncBillingInvoicesPagination(detail = state.billingInvoicesDetail) {
        state.billingInvoicesPage = Math.max(1, Number(detail?.page || state.billingInvoicesPage || 1));
        state.billingInvoicesPageSize = Math.max(25, Number(detail?.pageSize || state.billingInvoicesPageSize || 50));
        state.billingInvoicesTotalPages = Math.max(1, Number(detail?.totalPages || 1));
        state.billingInvoicesDuplicatesOnly = Boolean(detail?.duplicatesOnly);

        if (billingInvoicesMonth && !state.billingInvoicesDuplicatesOnly) {
            billingInvoicesMonth.value = `${String(state.billingInvoicesYear).padStart(4, "0")}-${String(state.billingInvoicesMonth).padStart(2, "0")}`;
        }
        if (billingInvoicesPageSize) {
            billingInvoicesPageSize.value = String(state.billingInvoicesPageSize);
        }
        if (billingInvoicesPageLabel) {
            const total = Number(detail?.totalRecordsCount || 0);
            billingInvoicesPageLabel.textContent = `${detail?.periodLabel || "Periodo"} · Página ${numberFormatter.format(state.billingInvoicesPage)} de ${numberFormatter.format(state.billingInvoicesTotalPages)} · ${numberFormatter.format(total)} registro(s)`;
        }
        if (billingInvoicesPreviousPageButton) {
            billingInvoicesPreviousPageButton.disabled = state.billingInvoicesLoading || !detail?.hasPreviousPage;
        }
        if (billingInvoicesNextPageButton) {
            billingInvoicesNextPageButton.disabled = state.billingInvoicesLoading || !detail?.hasNextPage;
        }
    }

    function setBillingCreditNotesLoading(loading) {
        state.billingCreditNotesLoading = loading;
        [billingCreditNotesSearch, billingCreditNotesStatusFilter, billingCreditNotesRefreshButton].forEach(element => {
            if (element) {
                element.disabled = loading;
            }
        });

        if (billingCreditNotesRefreshButton) {
            billingCreditNotesRefreshButton.textContent = loading ? "Actualizando..." : "Actualizar";
        }
    }

    function setCloudBillingLoading(loading) {
        state.cloudBillingLoading = loading;
        [cloudBillingRefreshButton, cloudBillingSearch, cloudBillingStatusFilter].forEach(element => {
            if (element) {
                element.disabled = loading;
            }
        });

        if (cloudBillingRefreshButton) {
            cloudBillingRefreshButton.textContent = loading ? "Actualizando..." : "Actualizar";
        }
    }

    function setBillingInvoiceSaving(saving) {
        state.billingInvoicesSaving = saving;
        [
            billingInvoiceEditorSaveButton,
            billingInvoiceEditorCancelButton,
            billingInvoiceEditorCloseButton
        ].forEach(element => {
            if (element) {
                element.disabled = saving;
            }
        });
    }

    function setBillingInvoicesDeleting(deleting) {
        state.billingInvoicesDeleting = deleting;
        setBillingInvoicesLoading(state.billingInvoicesLoading);
        syncBillingInvoicesSelectionSummary();
    }

    function setBillingInvoicesContractSaving(saving) {
        state.billingInvoicesContractSaving = saving;
        [
            billingContractTypeSaveButton,
            billingContractTypeCancelButton,
            billingContractTypeCloseButton,
            billingContractTypeBulkInput
        ].forEach(element => {
            if (element) {
                element.disabled = saving;
            }
        });

        setBillingInvoicesLoading(state.billingInvoicesLoading);
        syncBillingInvoicesSelectionSummary();
    }

    function setSiigoInvoicesLoading(loading) {
        state.siigoInvoicesLoading = loading;
        [siigoStartDateInput, siigoEndDateInput, siigoUseActivePeriodButton, siigoInvoicesLoadButton].forEach(element => {
            if (element) {
                element.disabled = loading || state.siigoInvoicesDownloading;
            }
        });

        syncSiigoCustomerControls();
        syncSiigoInvoicesSelectionSummary();
    }

    function setSiigoInvoicesDownloading(downloading) {
        state.siigoInvoicesDownloading = downloading;
        [siigoStartDateInput, siigoEndDateInput, siigoUseActivePeriodButton, siigoInvoicesLoadButton].forEach(element => {
            if (element) {
                element.disabled = downloading || state.siigoInvoicesLoading;
            }
        });

        syncSiigoCustomerControls();
        syncSiigoInvoicesSelectionSummary();
    }

    function setSiigoCustomersLoading(loading) {
        state.siigoCustomersLoading = loading;
        syncSiigoCustomerControls();
    }

    function setSiigoCustomerNitSearching(searching) {
        state.siigoCustomerNitSearching = searching;
        syncSiigoCustomerControls();
    }

    function syncSiigoCustomerControls() {
        const busy = state.siigoCustomersLoading || state.siigoCustomerNitSearching || state.siigoInvoicesLoading || state.siigoInvoicesDownloading;
        const hasCustomers = Array.isArray(state.siigoCustomers) && state.siigoCustomers.length > 0;

        if (siigoCustomerSelect) {
            siigoCustomerSelect.disabled = busy || !hasCustomers;
        }

        if (siigoCustomersLoadButton) {
            siigoCustomersLoadButton.disabled = busy;
            siigoCustomersLoadButton.textContent = state.siigoCustomersLoading
                ? "Cargando clientes..."
                : hasCustomers
                    ? "Actualizar clientes"
                    : "Consultar clientes";
        }

        if (siigoCustomerNitSearch) {
            siigoCustomerNitSearch.disabled = busy;
        }

        if (siigoCustomerNitSearchButton) {
            siigoCustomerNitSearchButton.disabled = busy;
            siigoCustomerNitSearchButton.textContent = state.siigoCustomerNitSearching ? "Buscando..." : "Buscar NIT";
        }
    }

    function setPortfolioLoading(loading) {
        state.portfolioLoading = loading;
        [
            portfolioRefreshButton,
            portfolioClientSearch,
            portfolioInvoicesSearch,
            portfolioOverdueClearFiltersButton,
            portfolioInvoicesClearFiltersButton,
            portfolioMonthlyRangeFilter,
            portfolioMonthlyYearFilter,
            portfolioMonthlyMonthFilter,
            portfolioMonthlyStartFilter,
            portfolioMonthlyEndFilter
        ].forEach(element => {
            if (element) {
                element.disabled = loading;
            }
        });
    }

    function setBusinessLoading(loading) {
        state.businessLoading = loading;
        if (businessRefreshButton) {
            businessRefreshButton.disabled = loading;
            businessRefreshButton.textContent = loading ? "Actualizando..." : "Actualizar";
        }
    }

    function setBusinessBillingLoading(loading) {
        state.businessBillingLoading = loading;
        [
            businessBillingStartFilter,
            businessBillingEndFilter,
            businessBillingGranularityFilter,
            businessBillingRefreshButton
        ].forEach(element => {
            if (element) {
                element.disabled = loading;
            }
        });

        if (businessBillingRefreshButton) {
            businessBillingRefreshButton.textContent = loading ? "Actualizando..." : "Actualizar";
        }
    }

    function setCopiersLoading(loading) {
        state.copiersLoading = loading;
        [copiersRefreshButton, copiersNewRecordButton].forEach(element => {
            if (element) {
                element.disabled = loading;
            }
        });
    }

    function setCopiersEquipmentLoading(loading) {
        state.copiersEquipmentLoading = loading;
        [
            copiersEquipmentRefreshButton,
            copiersMaintenanceRefreshButton,
            copiersMaintenanceYearFilter,
            copiersMaintenanceMonthFilter,
            copiersMaintenanceOwnerFilter,
            ...copiersSubtabButtons
        ].forEach(element => {
            if (element) {
                element.disabled = loading;
            }
        });
    }

    function syncCopiersInventoryButtons() {
        const busy = state.copiersInventoryLoading || state.copiersInventoryExporting;

        if (copiersInventoryExportButton) {
            copiersInventoryExportButton.disabled = busy;
            copiersInventoryExportButton.textContent = state.copiersInventoryExporting
                ? "Exportando..."
                : "Exportar Excel";
        }
    }

    function setCopiersInventoryLoading(loading) {
        state.copiersInventoryLoading = loading;
        copiersSubtabButtons.forEach(element => {
            if (element) {
                element.disabled = loading;
            }
        });
        syncCopiersInventoryButtons();
    }

    function setCopiersInventoryExporting(exporting) {
        state.copiersInventoryExporting = exporting;
        syncCopiersInventoryButtons();
    }

    function setCopiersMovementsLoading(loading) {
        state.copiersMovementsLoading = loading;
        [
            copiersMovementsRefreshButton,
            ...copiersSubtabButtons
        ].forEach(element => {
            if (element) {
                element.disabled = loading;
            }
        });

        if (copiersMovementsRefreshButton) {
            copiersMovementsRefreshButton.textContent = loading ? "Actualizando..." : "Actualizar";
        }
    }

    function setCopiersCountersLoading(loading) {
        state.copiersCountersLoading = loading;
        [
            copiersCountersRefreshButton,
            copiersCountersPdfButton,
            copiersCountersClearButton,
            copiersCountersMonthFilter,
            copiersCountersYearFilter,
            copiersCountersClientNameFilter,
            ...copiersSubtabButtons
        ].forEach(element => {
            if (element) {
                element.disabled = loading;
            }
        });
        updateCopiersCountersPdfButton();
    }

    function setCopiersEditorSaving(saving) {
        state.copiersEditorSaving = saving;
        [copiersEditorSaveBtn, copiersEditorCloseBtn, copiersEditorCancelBtn].forEach(element => {
            if (element) {
                element.disabled = saving;
            }
        });
    }

    function setCopiersEquipmentAssignmentSaving(saving) {
        state.copiersEquipmentAssignmentSaving = saving;
        [copiersEquipmentSaveBtn, copiersEquipmentDetailCloseBtn, copiersEquipmentDetailCancelBtn, copiersEquipmentClientNameInput, copiersEquipmentMoveToStockInput].forEach(element => {
            if (element) {
                element.disabled = saving;
            }
        });
    }

    function setCopiersLineEquipmentBusy(busy) {
        state.copiersLineEquipmentLoading = busy && !state.copiersLineEquipmentSaving;
        [
            copiersLineEquipmentCloseBtn,
            copiersLineEquipmentCancelBtn
        ].forEach(element => {
            if (element) {
                element.disabled = busy;
            }
        });

        if (copiersLineEquipmentSaveBtn) {
            copiersLineEquipmentSaveBtn.disabled = busy || !state.copiersLineEquipmentDetail;
        }
    }

    function setCopiersLineEquipmentSaving(saving) {
        state.copiersLineEquipmentSaving = saving;
        [
            copiersLineEquipmentCloseBtn,
            copiersLineEquipmentCancelBtn,
            copiersLineEquipmentSaveBtn
        ].forEach(element => {
            if (element) {
                element.disabled = saving;
            }
        });
    }

    function setPnlLoading(loading) {
        state.pnlLoading = loading;
        [pnlYearFilter, pnlMonthFilter, pnlVerticalFilter, pnlRefreshButton].forEach(element => {
            if (element) {
                element.disabled = loading;
            }
        });
    }

    function setLicenciamientoLoading(loading) {
        state.licenciamientoLoading = loading;
        [licenciamientoYearFilter, licenciamientoMonthFilter, licenciamientoRefreshButton].forEach(element => {
            if (element) {
                element.disabled = loading;
            }
        });
    }

    function setUtilityLoading(loading) {
        state.utilityLoading = loading;
        [utilityRefreshButton].forEach(element => {
            if (element) {
                element.disabled = loading || Boolean(state.utilityAssigningRecordId);
            }
        });
        if (utilityBreakdownSaveBtn) {
            utilityBreakdownSaveBtn.disabled = loading || !state.utilityBreakdownDirty;
        }
    }

    function setYtdLoading(loading) {
        state.ytdLoading = loading;
        if (ytdRefreshButton) {
            ytdRefreshButton.disabled = loading;
            ytdRefreshButton.textContent = loading ? "Actualizando..." : "Actualizar";
        }
    }

    function isPnlDetailOpen() {
        return Boolean(pnlDetailModal && !pnlDetailModal.hidden);
    }

    function isUtilityBreakdownOpen() {
        return Boolean(utilityBreakdownModal && !utilityBreakdownModal.hidden);
    }

    function isUtilityRealDetailOpen() {
        return Boolean(utilityRealDetailModal && !utilityRealDetailModal.hidden);
    }

    function isUtilityOrphansOpen() {
        return Boolean(utilityOrphansModal && !utilityOrphansModal.hidden);
    }

    function closeUtilityBreakdownModal() {
        if (!utilityBreakdownModal) {
            return;
        }

        utilityBreakdownModal.hidden = true;
        document.body.classList.remove("dashboard-modal-open");
        state.utilityBreakdownCardKey = "";

        if (utilityBreakdownTitle) {
            utilityBreakdownTitle.textContent = "Detalle de utilidad teorica";
        }

        if (utilityBreakdownSubtitle) {
            utilityBreakdownSubtitle.textContent = "Filas incluidas en el calculo seleccionado.";
        }

        setStatus(utilityBreakdownStatus, "", "");

        if (utilityBreakdownSummary) {
            utilityBreakdownSummary.innerHTML = "";
        }

        if (utilityBreakdownBody) {
            utilityBreakdownBody.innerHTML = '<tr><td colspan="10" class="dashboard-table__empty">Selecciona una tarjeta de utilidad para ver el desglose.</td></tr>';
        }

        if (utilityBreakdownFooter) {
            utilityBreakdownFooter.innerHTML = "";
        }
    }

    function closeUtilityRealDetailModal() {
        if (!utilityRealDetailModal) {
            return;
        }

        utilityRealDetailModal.hidden = true;
        document.body.classList.remove("dashboard-modal-open");
        state.utilityRealDetailContext = null;

        if (utilityRealDetailTitle) {
            utilityRealDetailTitle.textContent = "Detalle mensual";
        }

        if (utilityRealDetailSubtitle) {
            utilityRealDetailSubtitle.textContent = "Ventas y costos del mes seleccionado.";
        }

        if (utilityRealDetailSummary) {
            utilityRealDetailSummary.innerHTML = "";
        }

        if (utilityRealSalesTotal) {
            utilityRealSalesTotal.textContent = "$0";
        }

        if (utilityRealCostsTotal) {
            utilityRealCostsTotal.textContent = "$0";
        }

        if (utilityRealSalesBody) {
            utilityRealSalesBody.innerHTML = '<tr><td colspan="5" class="dashboard-table__empty">Haz click en una barra para ver sus facturas.</td></tr>';
        }

        if (utilityRealCostsBody) {
            utilityRealCostsBody.innerHTML = '<tr><td colspan="5" class="dashboard-table__empty">Haz click en una barra para ver sus costos.</td></tr>';
        }
    }

    function closeUtilityOrphansModal() {
        if (!utilityOrphansModal) {
            return;
        }

        utilityOrphansModal.hidden = true;
        document.body.classList.remove("dashboard-modal-open");
        setStatus(utilityOrphansStatus, "", "");

        if (utilityOrphansTitle) {
            utilityOrphansTitle.textContent = "Filas huerfanas";
        }

        if (utilityOrphansSubtitle) {
            utilityOrphansSubtitle.textContent = "Filas que no entraron en Monthly ni Annual por no tener tipo de contrato reconocible.";
        }

        if (utilityOrphansBody) {
            utilityOrphansBody.innerHTML = '<tr><td colspan="8" class="dashboard-table__empty">No hay filas huerfanas.</td></tr>';
        }
    }

    function setBillingSectionExpanded(section, button, expanded, expandedLabel, collapsedLabel) {
        if (section) {
            section.hidden = !expanded;
        }

        if (button) {
            button.setAttribute("aria-expanded", expanded ? "true" : "false");
            button.textContent = expanded ? expandedLabel : collapsedLabel;
        }
    }

    function toggleBillingSection(section, button, expandedLabel, collapsedLabel) {
        const expanded = Boolean(section?.hidden);
        setBillingSectionExpanded(section, button, expanded, expandedLabel, collapsedLabel);
    }

    function isBillingInvoiceEditorOpen() {
        return Boolean(billingInvoiceEditorModal && !billingInvoiceEditorModal.hidden);
    }

    function isBillingContractTypeModalOpen() {
        return Boolean(billingContractTypeModal && !billingContractTypeModal.hidden);
    }

    function isCloudBillingDetailOpen() {
        return Boolean(cloudBillingDetailModal && !cloudBillingDetailModal.hidden);
    }

    function closeBillingInvoiceEditorModal() {
        if (!billingInvoiceEditorModal) {
            return;
        }

        billingInvoiceEditorModal.hidden = true;
        document.body.classList.remove("dashboard-modal-open");
        state.billingInvoiceEditorOriginal = null;
        setBillingInvoiceSaving(false);
        setStatus(billingInvoiceEditorStatus, "", "");
    }

    function closeBillingContractTypeModal() {
        if (!billingContractTypeModal) {
            return;
        }

        billingContractTypeModal.hidden = true;
        document.body.classList.remove("dashboard-modal-open");
        setBillingInvoicesContractSaving(false);
        setStatus(billingContractTypeStatus, "", "");
    }

    function closeCloudBillingDetailModal() {
        if (!cloudBillingDetailModal) {
            return;
        }

        cloudBillingDetailModal.hidden = true;
        document.body.classList.remove("dashboard-modal-open");
        state.cloudBillingActiveGroup = null;

        if (cloudBillingDetailTitle) {
            cloudBillingDetailTitle.textContent = "Detalle de facturacion";
        }

        if (cloudBillingDetailSubtitle) {
            cloudBillingDetailSubtitle.textContent = "Productos y validacion de la fila seleccionada.";
        }

        if (cloudBillingDetailSummary) {
            cloudBillingDetailSummary.innerHTML = "";
        }

        if (cloudBillingDetailErrors) {
            cloudBillingDetailErrors.textContent = "Sin errores registrados.";
        }

        if (cloudBillingDetailBody) {
            cloudBillingDetailBody.innerHTML = '<tr><td colspan="10" class="dashboard-table__empty">Selecciona una fila para ver el detalle.</td></tr>';
        }
    }

    function openPnlDetailModal() {
        if (!pnlDetailModal) {
            return;
        }

        pnlDetailModal.hidden = false;
        document.body.classList.add("dashboard-modal-open");
    }

    function closePnlDetailModal() {
        if (!pnlDetailModal) {
            return;
        }

        pnlDetailModal.hidden = true;
        document.body.classList.remove("dashboard-modal-open");
        state.pnlDetail = null;
        state.pnlDetailContext = null;
        state.pnlDetailLoading = false;
        state.pnlDetailSavingRecordId = "";
        setStatus(pnlDetailStatus, "", "");

        if (pnlDetailTitle) {
            pnlDetailTitle.textContent = "Detalle de la celda";
        }

        if (pnlDetailSubtitle) {
            pnlDetailSubtitle.textContent = "Selecciona una celda del P&L para ver su composición.";
        }

        if (pnlDetailBody) {
            pnlDetailBody.innerHTML = '<tr><td colspan="15" class="dashboard-table__empty">Selecciona una celda del P&L para ver el detalle.</td></tr>';
        }
    }

    function isCopiersEditorOpen() {
        return Boolean(copiersEditorModal && !copiersEditorModal.hidden);
    }

    function resetCopiersEditorForm() {
        state.copiersEditorOriginal = null;
        setStatus(copiersEditorStatus, "", "");
        copiersRecordIdInput && (copiersRecordIdInput.value = "");
        copiersBillingDayInput && (copiersBillingDayInput.value = "");
        copiersClientIdInput && (copiersClientIdInput.value = "");
        copiersClientNameInput && (copiersClientNameInput.value = "");
        copiersProductIdInput && (copiersProductIdInput.value = "");
        copiersProductNameInput && (copiersProductNameInput.value = "");
        copiersQuantityInput && (copiersQuantityInput.value = "");
        copiersIncludedOperationsInput && (copiersIncludedOperationsInput.value = "");
        copiersAdditionalOperationInput && (copiersAdditionalOperationInput.value = "");
        copiersUnitValueBeforeVatInput && (copiersUnitValueBeforeVatInput.value = "");
        copiersUnitValueWithVatInput && (copiersUnitValueWithVatInput.value = "");
        copiersTotalWithVatInput && (copiersTotalWithVatInput.value = "");
        state.copiersClientSuggestions = [];
        state.copiersProductSuggestions = [];
        if (copiersClientOptions) {
            copiersClientOptions.innerHTML = "";
        }

        if (copiersProductOptions) {
            copiersProductOptions.innerHTML = "";
        }
    }

    function closeCopiersEditorModal() {
        if (!copiersEditorModal) {
            return;
        }

        copiersEditorModal.hidden = true;
        document.body.classList.remove("dashboard-modal-open");
        resetCopiersEditorForm();
        setCopiersEditorSaving(false);

        if (copiersEditorTitle) {
            copiersEditorTitle.textContent = "Editar registro";
        }

        if (copiersEditorSubtitle) {
            copiersEditorSubtitle.textContent = "Actualiza los campos del registro seleccionado.";
        }
    }

    function fillCopiersEditorForm(row) {
        if (!row) {
            return;
        }

        copiersRecordIdInput && (copiersRecordIdInput.value = row.recordId || "");
        copiersBillingDayInput && (copiersBillingDayInput.value = Number(row.billingDay || 0) > 0 ? String(Number(row.billingDay || 0)) : "");
        copiersClientIdInput && (copiersClientIdInput.value = row.clientId || "");
        copiersClientNameInput && (copiersClientNameInput.value = row.clientName || "");
        copiersProductIdInput && (copiersProductIdInput.value = row.productId || "");
        copiersProductNameInput && (copiersProductNameInput.value = row.productName || "");
        copiersQuantityInput && (copiersQuantityInput.value = formatEditableDecimalValue(row.quantity));
        copiersIncludedOperationsInput && (copiersIncludedOperationsInput.value = formatEditableDecimalValue(row.includedOperations));
        copiersAdditionalOperationInput && (copiersAdditionalOperationInput.value = formatEditableDecimalValue(row.additionalOperation));
        copiersUnitValueBeforeVatInput && (copiersUnitValueBeforeVatInput.value = formatEditableDecimalValue(row.unitValueBeforeVat));
        copiersUnitValueWithVatInput && (copiersUnitValueWithVatInput.value = formatEditableDecimalValue(row.unitValueWithVat));
        copiersTotalWithVatInput && (copiersTotalWithVatInput.value = formatEditableDecimalValue(row.totalWithVat));
    }

    function focusCopiersField(fieldKey) {
        if (!fieldKey) {
            return;
        }

        const target = copiersEditorForm?.querySelector(`[data-copiers-input-field="${fieldKey}"]`);
        if (!target) {
            return;
        }

        window.setTimeout(() => {
            target.focus();
            if (typeof target.select === "function") {
                target.select();
            }
        }, 30);
    }

    function openCopiersEditorModal(mode, row, focusField) {
        if (!copiersEditorModal) {
            return;
        }

        resetCopiersEditorForm();
        document.body.classList.add("dashboard-modal-open");
        copiersEditorModal.hidden = false;
        setCopiersEditorSaving(false);

        if (mode === "create") {
            if (copiersEditorTitle) {
                copiersEditorTitle.textContent = "Nuevo registro Copiers";
            }

            if (copiersEditorSubtitle) {
                copiersEditorSubtitle.textContent = "Completa todas las columnas para crear un nuevo registro en el popup.";
            }
        } else {
            state.copiersEditorOriginal = row ? { ...row } : null;
            fillCopiersEditorForm(row);

            if (copiersEditorTitle) {
                copiersEditorTitle.textContent = "Editar registro Copiers";
            }

            if (copiersEditorSubtitle) {
                copiersEditorSubtitle.textContent = focusField
                    ? `Campo seleccionado: ${getCopiersFieldLabel(focusField)}. Puedes ajustar el registro completo antes de guardar.`
                    : "Actualiza los campos del registro seleccionado.";
            }
        }

        focusCopiersField(mode === "create" ? "clientName" : focusField);
    }

    function isCopiersClientInvoicesOpen() {
        return Boolean(copiersClientInvoicesModal && !copiersClientInvoicesModal.hidden);
    }

    function resetCopiersClientInvoicesModal() {
        state.copiersClientInvoicesLoading = false;
        setStatus(copiersClientInvoicesStatus, "", "");

        if (copiersClientInvoicesTitle) {
            copiersClientInvoicesTitle.textContent = "Facturas del cliente";
        }

        if (copiersClientInvoicesSubtitle) {
            copiersClientInvoicesSubtitle.textContent = "Consulta las facturas emitidas con vertical Copiers en cr07a_facturacion para el cliente seleccionado.";
        }

        if (copiersClientInvoicesResultsCount) {
            copiersClientInvoicesResultsCount.textContent = "Mostrando 0 facturas";
        }

        if (copiersClientInvoicesBody) {
            copiersClientInvoicesBody.innerHTML = '<tr><td colspan="6" class="dashboard-table__empty">Selecciona un cliente para ver sus facturas emitidas.</td></tr>';
        }
    }

    function closeCopiersClientInvoicesModal() {
        if (!copiersClientInvoicesModal) {
            return;
        }

        copiersClientInvoicesModal.hidden = true;
        document.body.classList.remove("dashboard-modal-open");
        resetCopiersClientInvoicesModal();
    }

    function renderCopiersClientInvoicesLoading(row) {
        if (!copiersClientInvoicesModal) {
            return;
        }

        resetCopiersClientInvoicesModal();
        document.body.classList.add("dashboard-modal-open");
        copiersClientInvoicesModal.hidden = false;
        state.copiersClientInvoicesLoading = true;

        if (copiersClientInvoicesTitle) {
            copiersClientInvoicesTitle.textContent = row?.clientName || "Facturas del cliente";
        }

        if (copiersClientInvoicesSubtitle) {
            copiersClientInvoicesSubtitle.textContent = "Consultando facturas emitidas con vertical Copiers en cr07a_facturacion para este cliente...";
        }

        if (copiersClientInvoicesBody) {
            copiersClientInvoicesBody.innerHTML = '<tr><td colspan="6" class="dashboard-table__empty">Cargando facturas emitidas...</td></tr>';
        }

        setStatus(copiersClientInvoicesStatus, "info", "Consultando facturas del cliente...");

        window.setTimeout(() => {
            copiersClientInvoicesCloseBtn?.focus();
        }, 30);
    }

    function renderCopiersClientInvoicesDetail(detail) {
        if (copiersClientInvoicesTitle) {
            copiersClientInvoicesTitle.textContent = detail?.clientName || "Facturas del cliente";
        }

        if (copiersClientInvoicesSubtitle) {
            copiersClientInvoicesSubtitle.textContent = detail?.hasData
                ? "Facturas emitidas con vertical Copiers encontradas en cr07a_facturacion para este cliente."
                : (detail?.emptyStateMessage || "No encontramos facturas Copiers para este cliente.");
        }

        if (copiersClientInvoicesResultsCount) {
            copiersClientInvoicesResultsCount.textContent = `Mostrando ${numberFormatter.format(Number(detail?.recordsCount || 0))} facturas`;
        }

        if (!copiersClientInvoicesBody) {
            return;
        }

        const rows = Array.isArray(detail?.invoices) ? detail.invoices : [];
        copiersClientInvoicesBody.innerHTML = rows.length
            ? rows.map(row => `
                <tr>
                    <td>${escapeHtml(row.invoiceNumber || "-")}</td>
                    <td class="text-end">${escapeHtml(currencyFormatter.format(getNetInvoiceTotal(row)))}</td>
                    <td>${escapeHtml(row.emissionDateDisplay || "Sin fecha")}</td>
                    <td>${row.isPaymentOverdue
                        ? '<span class="dashboard-badge dashboard-badge--overdue" title="Sin pago y en mora">Vencida</span>'
                        : escapeHtml(row.paymentDateDisplay || "Sin fecha")}</td>
                    <td class="text-end">${escapeHtml(currencyFormatter.format(Number(row.paymentValue || 0)))}</td>
                    <td class="text-center">${(() => {
                        const publicUrl = (row.publicUrl || "").trim();
                        return isHttpUrl(publicUrl)
                            ? `<a class="dashboard-icon-link" href="${escapeHtml(publicUrl)}" target="_blank" rel="noopener noreferrer" aria-label="Descargar factura ${escapeHtml(row.invoiceNumber || "")}" title="Descargar factura"><span aria-hidden="true">⬇</span></a>`
                            : '<span class="dashboard-muted-text">-</span>';
                    })()}</td>
                </tr>
            `).join("")
            : `<tr><td colspan="6" class="dashboard-table__empty">${escapeHtml(detail?.emptyStateTitle || "No encontramos facturas Copiers para este cliente.")}</td></tr>`;
    }

    function isCopiersLineEquipmentOpen() {
        return Boolean(copiersLineEquipmentModal && !copiersLineEquipmentModal.hidden);
    }

    function resetCopiersLineEquipmentModal() {
        state.copiersLineEquipmentDetail = null;
        state.copiersLineEquipmentDraftIds = new Set();
        state.copiersLineEquipmentLoading = false;
        state.copiersLineEquipmentSaving = false;
        setStatus(copiersLineEquipmentStatus, "", "");
        setCopiersLineEquipmentSaving(false);

        if (copiersLineEquipmentTitle) {
            copiersLineEquipmentTitle.textContent = "Equipos de la linea";
        }

        if (copiersLineEquipmentSubtitle) {
            copiersLineEquipmentSubtitle.textContent = "Asigna equipos del cliente a esta linea de producto Copiers.";
        }

        if (copiersLineEquipmentSummary) {
            copiersLineEquipmentSummary.innerHTML = "";
        }

        if (copiersLineEquipmentAssignedCount) {
            copiersLineEquipmentAssignedCount.textContent = "0 equipos";
        }

        if (copiersLineEquipmentAvailableCount) {
            copiersLineEquipmentAvailableCount.textContent = "0 equipos";
        }

        if (copiersLineEquipmentAssignedBody) {
            copiersLineEquipmentAssignedBody.innerHTML = '<tr><td colspan="3" class="dashboard-table__empty">No hay equipos asignados a esta linea.</td></tr>';
        }

        if (copiersLineEquipmentAvailableBody) {
            copiersLineEquipmentAvailableBody.innerHTML = '<tr><td colspan="3" class="dashboard-table__empty">No hay equipos disponibles para asignar.</td></tr>';
        }
    }

    function closeCopiersLineEquipmentModal() {
        if (!copiersLineEquipmentModal) {
            return;
        }

        copiersLineEquipmentModal.hidden = true;
        document.body.classList.remove("dashboard-modal-open");
        resetCopiersLineEquipmentModal();
    }

    function renderCopiersLineEquipmentLoading(row) {
        if (!copiersLineEquipmentModal) {
            return;
        }

        resetCopiersLineEquipmentModal();
        document.body.classList.add("dashboard-modal-open");
        copiersLineEquipmentModal.hidden = false;
        state.copiersLineEquipmentLoading = true;
        setCopiersLineEquipmentBusy(true);

        if (copiersLineEquipmentTitle) {
            copiersLineEquipmentTitle.textContent = row?.productName || "Equipos de la linea";
        }

        if (copiersLineEquipmentSubtitle) {
            copiersLineEquipmentSubtitle.textContent = "Cargando equipos asignados y disponibles del cliente...";
        }

        setStatus(copiersLineEquipmentStatus, "info", "Consultando asignacion de equipos...");
        window.setTimeout(() => copiersLineEquipmentCloseBtn?.focus(), 30);
    }

    function getCopiersLineEquipmentPool(detail) {
        const byId = new Map();
        [...(detail?.assignedEquipment || []), ...(detail?.availableEquipment || [])].forEach(item => {
            const id = item?.equipmentId || "";
            if (id && !byId.has(id)) {
                byId.set(id, item);
            }
        });

        return Array.from(byId.values()).sort((left, right) =>
            String(left?.serial || "").localeCompare(String(right?.serial || ""), "es", { numeric: true, sensitivity: "base" }));
    }

    function renderCopiersLineEquipmentSummary(detail, assignedCount, availableCount) {
        if (!copiersLineEquipmentSummary) {
            return;
        }

        const capacity = Number(detail?.assignmentCapacity || 0);
        const overflow = assignedCount > capacity;
        copiersLineEquipmentSummary.innerHTML = `
            <div class="dashboard-line-assignment-summary__item ${overflow ? "is-warning" : ""}">
                <span>Cupos de la linea</span>
                <strong>${escapeHtml(numberFormatter.format(capacity))}</strong>
            </div>
            <div class="dashboard-line-assignment-summary__item">
                <span>Asignados</span>
                <strong>${escapeHtml(numberFormatter.format(assignedCount))}</strong>
            </div>
            <div class="dashboard-line-assignment-summary__item">
                <span>Disponibles del cliente</span>
                <strong>${escapeHtml(numberFormatter.format(availableCount))}</strong>
            </div>
            <div class="dashboard-line-assignment-summary__item">
                <span>Oper. incluidas</span>
                <strong>${escapeHtml(numberFormatter.format(Number(detail?.includedOperations || 0)))}</strong>
            </div>
        `;
    }

    function buildCopiersLineEquipmentDetailText(item) {
        return [item?.categoryLabel, item?.reference, item?.site, item?.area]
            .filter(value => value && String(value).trim())
            .join(" · ") || "Sin detalle";
    }

    function renderCopiersLineEquipmentRow(item, action) {
        const isAssign = action === "assign";
        return `
            <tr>
                <td><strong>${escapeHtml(item?.serial || "Equipo sin serial")}</strong></td>
                <td>${escapeHtml(buildCopiersLineEquipmentDetailText(item))}</td>
                <td class="text-end">
                    <button type="button"
                            class="btn btn-sm ${isAssign ? "btn-outline-primary" : "btn-outline-secondary"}"
                            data-copiers-line-equipment-${isAssign ? "assign" : "remove"}="${escapeHtml(item?.equipmentId || "")}">
                        ${isAssign ? "Asignar" : "Quitar"}
                    </button>
                </td>
            </tr>
        `;
    }

    function renderCopiersLineEquipmentDetail(detail) {
        state.copiersLineEquipmentDetail = detail || null;
        state.copiersLineEquipmentDraftIds = new Set(
            (detail?.assignedEquipment || [])
                .map(item => item?.equipmentId || "")
                .filter(Boolean)
        );
        renderCopiersLineEquipmentDraft();
    }

    function renderCopiersLineEquipmentDraft() {
        const detail = state.copiersLineEquipmentDetail;
        const pool = getCopiersLineEquipmentPool(detail);
        const assignedIds = state.copiersLineEquipmentDraftIds || new Set();
        const assigned = pool.filter(item => assignedIds.has(item?.equipmentId || ""));
        const available = pool.filter(item => !assignedIds.has(item?.equipmentId || ""));
        const capacity = Number(detail?.assignmentCapacity || 0);

        if (copiersLineEquipmentTitle) {
            copiersLineEquipmentTitle.textContent = detail?.productName || "Equipos de la linea";
        }

        if (copiersLineEquipmentSubtitle) {
            copiersLineEquipmentSubtitle.textContent = [
                detail?.clientName || "",
                `${numberFormatter.format(assigned.length)}/${numberFormatter.format(capacity)} asignados`
            ].filter(Boolean).join(" · ");
        }

        if (copiersLineEquipmentAssignedCount) {
            copiersLineEquipmentAssignedCount.textContent = `${numberFormatter.format(assigned.length)} equipo(s)`;
        }

        if (copiersLineEquipmentAvailableCount) {
            copiersLineEquipmentAvailableCount.textContent = `${numberFormatter.format(available.length)} equipo(s)`;
        }

        renderCopiersLineEquipmentSummary(detail, assigned.length, available.length);

        if (copiersLineEquipmentAssignedBody) {
            copiersLineEquipmentAssignedBody.innerHTML = assigned.length
                ? assigned.map(item => renderCopiersLineEquipmentRow(item, "remove")).join("")
                : '<tr><td colspan="3" class="dashboard-table__empty">No hay equipos asignados a esta linea.</td></tr>';
        }

        if (copiersLineEquipmentAvailableBody) {
            copiersLineEquipmentAvailableBody.innerHTML = available.length
                ? available.map(item => renderCopiersLineEquipmentRow(item, "assign")).join("")
                : '<tr><td colspan="3" class="dashboard-table__empty">No hay equipos disponibles para asignar.</td></tr>';
        }

        if (copiersLineEquipmentSaveBtn) {
            copiersLineEquipmentSaveBtn.disabled = state.copiersLineEquipmentSaving || assigned.length > capacity;
        }

        if (assigned.length > capacity) {
            setStatus(copiersLineEquipmentStatus, "error", `Esta linea permite maximo ${numberFormatter.format(capacity)} equipo(s).`);
        } else if (!state.copiersLineEquipmentSaving && !copiersLineEquipmentStatus?.classList.contains("success")) {
            setStatus(copiersLineEquipmentStatus, "", "");
        }
    }

    function assignCopiersLineEquipment(equipmentId) {
        const detail = state.copiersLineEquipmentDetail;
        const capacity = Number(detail?.assignmentCapacity || 0);
        const normalizedId = equipmentId || "";
        if (!normalizedId) {
            return;
        }

        if (state.copiersLineEquipmentDraftIds.size >= capacity) {
            setStatus(copiersLineEquipmentStatus, "error", `Esta linea permite maximo ${numberFormatter.format(capacity)} equipo(s).`);
            return;
        }

        state.copiersLineEquipmentDraftIds.add(normalizedId);
        setStatus(copiersLineEquipmentStatus, "", "");
        renderCopiersLineEquipmentDraft();
    }

    function removeCopiersLineEquipment(equipmentId) {
        const normalizedId = equipmentId || "";
        if (!normalizedId) {
            return;
        }

        state.copiersLineEquipmentDraftIds.delete(normalizedId);
        setStatus(copiersLineEquipmentStatus, "", "");
        renderCopiersLineEquipmentDraft();
    }

    function isCopiersBillingCountersOpen() {
        return Boolean(copiersBillingCountersModal && !copiersBillingCountersModal.hidden);
    }

    function resetCopiersBillingCountersModal() {
        if (copiersBillingCountersTitle) {
            copiersBillingCountersTitle.textContent = "Equipos y contador reciente";
        }

        if (copiersBillingCountersSubtitle) {
            copiersBillingCountersSubtitle.textContent = "Selecciona un grupo de facturacion Copiers para consultar sus equipos asignados.";
        }

        if (copiersBillingCountersResultsCount) {
            copiersBillingCountersResultsCount.textContent = "Mostrando 0 equipos";
        }

        if (copiersBillingCountersBody) {
            copiersBillingCountersBody.innerHTML = '<tr><td colspan="7" class="dashboard-table__empty">Selecciona un grupo para consultar sus equipos.</td></tr>';
        }
    }

    function closeCopiersBillingCountersModal() {
        if (!copiersBillingCountersModal) {
            return;
        }

        copiersBillingCountersModal.hidden = true;
        document.body.classList.remove("dashboard-modal-open");
        resetCopiersBillingCountersModal();
    }

    function renderCopiersBillingCountersRows(group) {
        const items = Array.isArray(group?.equipment) ? group.equipment : [];
        if (!items.length) {
            return '<tr><td colspan="7" class="dashboard-table__empty">Este cliente no tiene equipos asignados en la tabla de equipos.</td></tr>';
        }

        return items.map(row => {
            const hasCounter = Boolean(row.hasCurrentCounter);
            const statusClass = hasCounter ? "dashboard-counter-chip--ok" : "dashboard-counter-chip--pending";
            const statusLabel = row.counterStatusLabel || (hasCounter ? "Contador registrado" : "Pendiente de contador");
            const detail = [row.categoryLabel, row.reference]
                .filter(value => value && String(value).trim())
                .join(" · ");
            const location = [row.site, row.area]
                .filter(value => value && String(value).trim())
                .join(" · ");

            return `
                <tr>
                    <td><strong>${escapeHtml(row.serial || "Equipo sin serial")}</strong></td>
                    <td>${escapeHtml(detail || "Sin detalle")}</td>
                    <td>${escapeHtml(location || "Sin ubicacion")}</td>
                    <td>
                        <span class="dashboard-counter-chip ${statusClass}">
                            <strong>${escapeHtml(statusLabel)}</strong>
                            <small>${escapeHtml(row.counterDateDisplay || "Ultimos 35 dias")}</small>
                        </span>
                    </td>
                    <td>${escapeHtml(row.counterDateDisplay || "Sin fecha")}</td>
                    <td class="text-end">${escapeHtml(formatNullableNumber(row.counterCopies))}</td>
                    <td class="text-end">${escapeHtml(formatNullableNumber(row.counterScans))}</td>
                </tr>
            `;
        }).join("");
    }

    function openCopiersBillingCountersModal(group) {
        if (!copiersBillingCountersModal) {
            return;
        }

        const equipment = Array.isArray(group?.equipment) ? group.equipment : [];
        resetCopiersBillingCountersModal();

        if (copiersBillingCountersTitle) {
            copiersBillingCountersTitle.textContent = group?.clientName || "Equipos y contador reciente";
        }

        if (copiersBillingCountersSubtitle) {
            const parts = [
                group?.billingDayDisplay || "",
                group?.counterSummary || "",
                `${numberFormatter.format(Number(group?.countersRegisteredCount || 0))}/${numberFormatter.format(Number(group?.equipmentCount || 0))} con contador`
            ].filter(value => value && String(value).trim());
            copiersBillingCountersSubtitle.textContent = parts.join(" · ");
        }

        if (copiersBillingCountersResultsCount) {
            copiersBillingCountersResultsCount.textContent = `Mostrando ${numberFormatter.format(equipment.length)} equipos`;
        }

        if (copiersBillingCountersBody) {
            copiersBillingCountersBody.innerHTML = renderCopiersBillingCountersRows(group);
        }

        document.body.classList.add("dashboard-modal-open");
        copiersBillingCountersModal.hidden = false;
        window.setTimeout(() => copiersBillingCountersCloseBtn?.focus(), 30);
    }

    function isCopiersEquipmentDetailOpen() {
        return Boolean(copiersEquipmentDetailModal && !copiersEquipmentDetailModal.hidden);
    }

    function resetCopiersEquipmentDetail() {
        state.copiersEquipmentDetail = null;
        state.copiersEquipmentDetailLoading = false;
        state.copiersEquipmentAssignmentSaving = false;
        state.copiersEquipmentClientSuggestions = [];
        setStatus(copiersEquipmentDetailStatus, "", "");
        setCopiersEquipmentAssignmentSaving(false);

        copiersEquipmentRecordIdInput && (copiersEquipmentRecordIdInput.value = "");
        copiersEquipmentClientIdInput && (copiersEquipmentClientIdInput.value = "");
        copiersEquipmentClientNameInput && (copiersEquipmentClientNameInput.value = "");
        copiersEquipmentMoveToStockInput && (copiersEquipmentMoveToStockInput.checked = false);
        copiersEquipmentDetailSerial && (copiersEquipmentDetailSerial.textContent = "-");
        copiersEquipmentDetailCurrentClient && (copiersEquipmentDetailCurrentClient.textContent = "-");
        copiersEquipmentDetailCategory && (copiersEquipmentDetailCategory.textContent = "-");
        copiersEquipmentDetailReference && (copiersEquipmentDetailReference.textContent = "-");
        copiersEquipmentDetailObservations && (copiersEquipmentDetailObservations.textContent = "-");

        if (copiersEquipmentDetailTitle) {
            copiersEquipmentDetailTitle.textContent = "Detalle del equipo";
        }

        if (copiersEquipmentDetailSubtitle) {
            copiersEquipmentDetailSubtitle.textContent = "Consulta la información del equipo, reasigna su cliente o envíalo a stock y revisa sus mantenimientos.";
        }

        if (copiersEquipmentClientOptions) {
            copiersEquipmentClientOptions.innerHTML = "";
        }

        if (copiersEquipmentMaintenanceBody) {
            copiersEquipmentMaintenanceBody.innerHTML = '<tr><td colspan="9" class="dashboard-table__empty">Selecciona un equipo para ver su historial de mantenimientos.</td></tr>';
        }
    }

    function closeCopiersEquipmentDetailModal() {
        if (!copiersEquipmentDetailModal) {
            return;
        }

        copiersEquipmentDetailModal.hidden = true;
        document.body.classList.remove("dashboard-modal-open");
        resetCopiersEquipmentDetail();
    }

    function renderCopiersEquipmentMaintenanceTable(rows) {
        if (!copiersEquipmentMaintenanceBody) {
            return;
        }

        const items = Array.isArray(rows) ? rows : [];
        copiersEquipmentMaintenanceBody.innerHTML = items.length
            ? items.map(row => `
                <tr>
                    <td>${escapeHtml(row.dateDisplay || "-")}</td>
                    <td>${escapeHtml(row.title || "-")}</td>
                    <td>${escapeHtml(row.maintenanceTypeLabel || "-")}</td>
                    <td>${renderCopiersMaintenanceStatusBadge(row)}</td>
                    <td>${escapeHtml(row.clientName || "Sin cliente")}</td>
                    <td>${escapeHtml(row.technicianName || "Sin tecnico")}</td>
                    <td>${escapeHtml(row.description || "Sin descripcion")}</td>
                    <td>${escapeHtml(row.internalId || "-")}</td>
                    <td>
                        ${row.hasAttachment ? `
                            <a class="dashboard-link-btn" href="${escapeHtml(buildCopiersMaintenanceFileUrl(row.recordId || ""))}" target="_blank" rel="noopener noreferrer">
                                ${escapeHtml(row.attachmentFileName || "Descargar")}
                            </a>
                        ` : '<span class="dashboard-muted-text">Sin acta</span>'}
                    </td>
                </tr>
            `).join("")
            : '<tr><td colspan="9" class="dashboard-table__empty">Este equipo todavia no tiene mantenimientos registrados.</td></tr>';
    }

    function renderCopiersMaintenanceKpis(dashboard) {
        const items = Array.isArray(dashboard?.kpis)
            ? dashboard.kpis.filter(kpi => (kpi?.key || "") === "equipment-maintenance")
            : [];

        renderSimpleKpis(copiersMaintenanceKpisContainer, items);
    }

    function parseCopiersMaintenanceDateParts(row) {
        const rawValue = (row?.dateValue || "").trim();
        const isoMatch = rawValue.match(/^(\d{4})-(\d{2})-(\d{2})$/);
        if (isoMatch) {
            return {
                year: Number(isoMatch[1]),
                month: Number(isoMatch[2]),
                day: Number(isoMatch[3])
            };
        }

        const displayValue = (row?.dateDisplay || "").trim();
        const displayMatch = displayValue.match(/^(\d{1,2})\/(\d{1,2})\/(\d{4})$/);
        if (displayMatch) {
            return {
                year: Number(displayMatch[3]),
                month: Number(displayMatch[2]),
                day: Number(displayMatch[1])
            };
        }

        return { year: null, month: null, day: null };
    }

    function renderCopiersMaintenanceDateCell(row) {
        const dateParts = parseCopiersMaintenanceDateParts(row);
        const formattedDate = (row?.dateDisplay || "").trim() || "-";
        if (!dateParts.year || !dateParts.month || !dateParts.day) {
            return `
                <div class="dashboard-maintenance-date">
                    <strong>${escapeHtml(formattedDate)}</strong>
                </div>
            `;
        }

        const weekdayIndex = new Date(dateParts.year, dateParts.month - 1, dateParts.day).getDay();
        const weekdayLabel = weekdayLabels[weekdayIndex] || "";
        return `
            <div class="dashboard-maintenance-date">
                <strong>${escapeHtml(formattedDate)}</strong>
                <span>${escapeHtml(weekdayLabel)}</span>
            </div>
        `;
    }

    function renderCopiersMaintenanceTypeBadge(row) {
        const label = (row?.maintenanceTypeLabel || "").trim();
        const normalizedLabel = normalizeText(label);
        if (normalizedLabel === "correctivo") {
            return '<span class="dashboard-badge is-danger">Correctivo</span>';
        }

        if (normalizedLabel === "preventivo") {
            return '<span class="dashboard-badge is-success">Preventivo</span>';
        }

        return label
            ? `<span class="dashboard-pill dashboard-pill--soft">${escapeHtml(label)}</span>`
            : '<span class="dashboard-muted-text">Sin tipo</span>';
    }

    function renderCopiersMaintenanceStatusBadge(row) {
        const value = Number(row?.maintenanceStatusValue || copiersMaintenanceStatusPending);
        const label = (row?.maintenanceStatusLabel || "").trim() || "Pendiente";
        const completed = value === copiersMaintenanceStatusCompleted;
        return `<span class="dashboard-badge ${completed ? "is-success" : "is-warning"}">${escapeHtml(label)}</span>`;
    }

    function renderCopiersMaintenanceDetailCell(row) {
        const title = (row?.title || "").trim();
        const internalId = (row?.internalId || "").trim();
        const description = (row?.description || "").trim();

        return `
            <div class="dashboard-maintenance-detail">
                ${title ? `<strong class="dashboard-maintenance-detail__title">${escapeHtml(title)}</strong>` : ""}
                <div class="dashboard-maintenance-detail__meta">
                    <span class="dashboard-maintenance-detail__id">${escapeHtml(internalId || "Sin ID interno")}</span>
                    ${renderCopiersMaintenanceTypeBadge(row)}
                </div>
                <span class="dashboard-maintenance-detail__description">${escapeHtml(description || "Sin descripcion")}</span>
            </div>
        `;
    }

    function getCopiersMaintenanceDetailDisplay(row) {
        const title = (row?.title || "").trim();
        const type = (row?.maintenanceTypeLabel || "").trim();
        const internalId = (row?.internalId || "").trim();
        const description = (row?.description || "").trim();
        const parts = [title, type, internalId, description].filter(Boolean);
        return parts.length ? parts.join(" - ") : "Sin detalle";
    }

    function getCopiersMaintenanceAttachmentDisplay(row) {
        return row?.hasAttachment ? "Con acta" : "Sin acta";
    }

    function renderCopiersMaintenanceAttachmentCell(row) {
        if (!row?.hasAttachment) {
            return '<span class="dashboard-muted-text">-</span>';
        }

        return `
            <a class="dashboard-icon-link" href="${escapeHtml(buildCopiersMaintenanceFileUrl(row.recordId || ""))}" target="_blank" rel="noopener noreferrer" aria-label="Descargar acta de entrega">
                <span aria-hidden="true">&#8681;</span>
                <span class="visually-hidden">Descargar acta</span>
            </a>
        `;
    }

    function getCopiersMaintenanceOwnerFilterValue(row) {
        const technicianId = (row?.technicianId || "").trim();
        if (technicianId) {
            return `id:${technicianId.toLowerCase()}`;
        }

        const technicianName = normalizeText(row?.technicianName || "");
        if (technicianName) {
            return `name:${technicianName}`;
        }

        return "empty";
    }

    function renderDashboardSelectOptions(select, options, selectedValue) {
        if (!select) {
            return;
        }

        const items = Array.isArray(options) ? options : [];
        select.innerHTML = items.map(option => `
            <option value="${escapeHtml(option.value)}"${option.value === selectedValue ? " selected" : ""}>
                ${escapeHtml(option.label)}
            </option>
        `).join("");
    }

    function buildCopiersMaintenanceFilterOptions() {
        const rows = Array.isArray(state.copiersEquipmentDashboard?.maintenanceRows)
            ? state.copiersEquipmentDashboard.maintenanceRows
            : [];
        const allOption = { value: "all", label: "Todos" };

        const years = rows
            .map(row => parseCopiersMaintenanceDateParts(row).year)
            .filter(year => Number.isInteger(year) && year > 0)
            .filter((year, index, list) => list.indexOf(year) === index)
            .sort((left, right) => right - left);

        if (state.copiersMaintenanceYear !== "all" && !years.includes(Number(state.copiersMaintenanceYear))) {
            state.copiersMaintenanceYear = "all";
        }

        const yearOptions = [allOption, ...years.map(year => ({ value: String(year), label: String(year) }))];
        renderDashboardSelectOptions(copiersMaintenanceYearFilter, yearOptions, state.copiersMaintenanceYear);

        const monthRows = rows.filter(row => {
            if (state.copiersMaintenanceYear === "all") {
                return true;
            }

            return parseCopiersMaintenanceDateParts(row).year === Number(state.copiersMaintenanceYear);
        });

        const months = monthRows
            .map(row => parseCopiersMaintenanceDateParts(row).month)
            .filter(month => Number.isInteger(month) && month >= 1 && month <= 12)
            .filter((month, index, list) => list.indexOf(month) === index)
            .sort((left, right) => left - right);

        if (state.copiersMaintenanceMonth !== "all" && !months.includes(Number(state.copiersMaintenanceMonth))) {
            state.copiersMaintenanceMonth = "all";
        }

        const monthOptions = [allOption, ...months.map(month => ({
            value: String(month),
            label: monthLabels[Math.max(month - 1, 0)] || `Mes ${month}`
        }))];
        renderDashboardSelectOptions(copiersMaintenanceMonthFilter, monthOptions, state.copiersMaintenanceMonth);

        const ownerRows = rows.filter(row => {
            const dateParts = parseCopiersMaintenanceDateParts(row);
            if (state.copiersMaintenanceYear !== "all" && dateParts.year !== Number(state.copiersMaintenanceYear)) {
                return false;
            }

            if (state.copiersMaintenanceMonth !== "all" && dateParts.month !== Number(state.copiersMaintenanceMonth)) {
                return false;
            }

            return true;
        });

        const ownerMap = new Map();
        ownerRows.forEach(row => {
            const ownerValue = getCopiersMaintenanceOwnerFilterValue(row);
            const ownerLabel = (row?.technicianName || "").trim() || "Sin owner";
            if (!ownerMap.has(ownerValue)) {
                ownerMap.set(ownerValue, ownerLabel);
            }
        });

        const ownerOptions = [allOption, ...Array.from(ownerMap.entries())
            .sort((left, right) => left[1].localeCompare(right[1], "es-CO", { sensitivity: "base" }))
            .map(([value, label]) => ({ value, label }))];

        if (state.copiersMaintenanceOwner !== "all" && !ownerMap.has(state.copiersMaintenanceOwner)) {
            state.copiersMaintenanceOwner = "all";
        }

        renderDashboardSelectOptions(copiersMaintenanceOwnerFilter, ownerOptions, state.copiersMaintenanceOwner);
    }

    function getBaseFilteredCopiersMaintenanceRows() {
        const rows = Array.isArray(state.copiersEquipmentDashboard?.maintenanceRows)
            ? state.copiersEquipmentDashboard.maintenanceRows
            : [];

        return rows.filter(row => {
            const dateParts = parseCopiersMaintenanceDateParts(row);
            if (state.copiersMaintenanceYear !== "all" && dateParts.year !== Number(state.copiersMaintenanceYear)) {
                return false;
            }

            if (state.copiersMaintenanceMonth !== "all" && dateParts.month !== Number(state.copiersMaintenanceMonth)) {
                return false;
            }

            if (state.copiersMaintenanceOwner !== "all"
                && getCopiersMaintenanceOwnerFilterValue(row) !== state.copiersMaintenanceOwner) {
                return false;
            }

            return true;
        });
    }

    function getFilteredCopiersMaintenanceRows() {
        return getFilteredPortfolioGridRows("copiersMaintenance");
    }

    function getCopiersMaintenancePagination(rows) {
        const totalRows = Array.isArray(rows) ? rows.length : 0;
        const totalPages = Math.max(1, Math.ceil(totalRows / copiersMaintenancePageSize));
        const page = Math.min(Math.max(state.copiersMaintenancePage, 1), totalPages);
        const startIndex = totalRows ? (page - 1) * copiersMaintenancePageSize : 0;
        const endIndex = Math.min(startIndex + copiersMaintenancePageSize, totalRows);

        if (page !== state.copiersMaintenancePage) {
            state.copiersMaintenancePage = page;
        }

        return {
            page,
            totalRows,
            totalPages,
            startIndex,
            endIndex,
            rows: rows.slice(startIndex, endIndex)
        };
    }

    function renderCopiersMaintenancePagination(pagination) {
        const hasRows = pagination.totalRows > 0;

        if (copiersMaintenancePagination) {
            copiersMaintenancePagination.classList.toggle("is-hidden", !hasRows);
        }

        if (copiersMaintenancePageSummary) {
            copiersMaintenancePageSummary.textContent = hasRows
                ? `Página ${numberFormatter.format(pagination.page)} de ${numberFormatter.format(pagination.totalPages)} - Registros ${numberFormatter.format(pagination.startIndex + 1)}-${numberFormatter.format(pagination.endIndex)}`
                : "Página 0 de 0";
        }

        if (copiersMaintenancePrevBtn) {
            copiersMaintenancePrevBtn.disabled = !hasRows || pagination.page <= 1;
        }

        if (copiersMaintenanceNextBtn) {
            copiersMaintenanceNextBtn.disabled = !hasRows || pagination.page >= pagination.totalPages;
        }
    }

    function setCopiersMaintenancePage(page) {
        const totalRows = getFilteredCopiersMaintenanceRows().length;
        const totalPages = Math.max(1, Math.ceil(totalRows / copiersMaintenancePageSize));
        const nextPage = Math.min(Math.max(page, 1), totalPages);

        if (nextPage === state.copiersMaintenancePage) {
            return;
        }

        state.copiersMaintenancePage = nextPage;
        renderCopiersMaintenanceTable();
    }

    function renderCopiersMaintenanceTable() {
        const allRows = Array.isArray(state.copiersEquipmentDashboard?.maintenanceRows)
            ? state.copiersEquipmentDashboard.maintenanceRows
            : [];
        const filteredRows = getFilteredCopiersMaintenanceRows();
        const pagination = getCopiersMaintenancePagination(filteredRows);
        const rows = pagination.rows;

        renderPortfolioGridHeader("copiersMaintenance");

        if (copiersMaintenanceResultsCount) {
            copiersMaintenanceResultsCount.textContent = filteredRows.length
                ? `Mostrando ${numberFormatter.format(pagination.startIndex + 1)}-${numberFormatter.format(pagination.endIndex)} de ${numberFormatter.format(filteredRows.length)} registros${filteredRows.length !== allRows.length ? ` filtrados (${numberFormatter.format(allRows.length)} totales)` : ""}`
                : `Mostrando 0 de ${numberFormatter.format(allRows.length)} registros`;
        }

        if (state.activeTab === "copiers" && state.copiersSubtab === "maintenance" && recordCount) {
            recordCount.textContent = numberFormatter.format(filteredRows.length);
        }

        if (!copiersMaintenanceBody) {
            return;
        }

        copiersMaintenanceBody.innerHTML = rows.length
            ? rows.map(row => `
                <tr>
                    ${copiersMaintenanceColumns.map(column => `
                        <td class="${getPortfolioColumnAlignClass(column)}">
                            ${getPortfolioColumnCell(row, column)}
                        </td>
                    `).join("")}
                </tr>
            `).join("")
            : '<tr><td colspan="7" class="dashboard-table__empty">No hay mantenimientos para los filtros seleccionados.</td></tr>';

        renderCopiersMaintenancePagination(pagination);
    }

    function renderCopiersMaintenanceDashboard(dashboard) {
        renderCopiersMaintenanceKpis(dashboard);
        renderCopiersMaintenanceChart(dashboard?.maintenanceChart);
        buildCopiersMaintenanceFilterOptions();
        state.copiersMaintenancePage = 1;
        renderCopiersMaintenanceTable();
    }

    function renderCopiersMovementAttachmentCell(row) {
        if (!row?.hasAttachment) {
            return '<span class="dashboard-muted-text">Sin acta</span>';
        }

        return `
            <a class="dashboard-link-btn" href="${escapeHtml(buildCopiersEquipmentMovementFileUrl(row.recordId || ""))}" target="_blank" rel="noopener noreferrer">
                ${escapeHtml(row.attachmentFileName || "Descargar")}
            </a>
        `;
    }

    function getCopiersMovementAttachmentDisplay(row) {
        return row?.hasAttachment ? "Con acta" : "Sin acta";
    }

    function renderCopiersMovementsDashboard(dashboard) {
        renderPortfolioGrid("copiersMovements");
        const rows = getFilteredPortfolioGridRows("copiersMovements");

        if (state.activeTab === "copiers" && state.copiersSubtab === "movements" && recordCount) {
            recordCount.textContent = numberFormatter.format(rows.length);
        }
    }

    function fillCopiersEquipmentDetail(detail) {
        if (!detail?.equipment) {
            return;
        }

        const equipment = detail.equipment;
        state.copiersEquipmentDetail = detail;
        copiersEquipmentRecordIdInput && (copiersEquipmentRecordIdInput.value = equipment.recordId || "");
        copiersEquipmentClientIdInput && (copiersEquipmentClientIdInput.value = equipment.clientId || "");
        copiersEquipmentClientNameInput && (copiersEquipmentClientNameInput.value = equipment.inStock ? "" : (equipment.clientName || ""));
        copiersEquipmentMoveToStockInput && (copiersEquipmentMoveToStockInput.checked = Boolean(equipment.inStock));
        copiersEquipmentDetailSerial && (copiersEquipmentDetailSerial.textContent = equipment.serial || "Sin serial");
        copiersEquipmentDetailCurrentClient && (copiersEquipmentDetailCurrentClient.textContent = equipment.inStock ? "Stock" : (equipment.clientName || "Sin cliente"));
        copiersEquipmentDetailCategory && (copiersEquipmentDetailCategory.textContent = equipment.categoryLabel || "Sin categoria");
        copiersEquipmentDetailReference && (copiersEquipmentDetailReference.textContent = equipment.reference || "Sin referencia");
        copiersEquipmentDetailObservations && (copiersEquipmentDetailObservations.textContent = equipment.observations || "Sin observaciones");

        if (copiersEquipmentDetailTitle) {
            copiersEquipmentDetailTitle.textContent = equipment.serial
                ? `Equipo ${equipment.serial}`
                : "Detalle del equipo";
        }

        if (copiersEquipmentDetailSubtitle) {
            copiersEquipmentDetailSubtitle.textContent = equipment.inStock
                ? "Este equipo está actualmente en stock. Puedes asignarlo a un cliente desde este popup."
                : "Revisa la informacion del equipo y actualiza su cliente actual cuando lo necesites.";
        }

        renderCopiersEquipmentMaintenanceTable(detail.maintenanceRows);
    }

    function openCopiersEquipmentDetailModal(detail) {
        if (!copiersEquipmentDetailModal) {
            return;
        }

        resetCopiersEquipmentDetail();
        document.body.classList.add("dashboard-modal-open");
        copiersEquipmentDetailModal.hidden = false;
        fillCopiersEquipmentDetail(detail);

        window.setTimeout(() => {
            copiersEquipmentClientNameInput?.focus();
        }, 30);
    }

    function renderPnlDetailLoading(rowLabel, cellMonth) {
        if (pnlDetailTitle) {
            pnlDetailTitle.textContent = rowLabel || "Detalle de la celda";
        }

        if (pnlDetailSubtitle) {
            pnlDetailSubtitle.textContent = cellMonth
                ? `Cargando composición de ${rowLabel || "la celda"} para ${monthLabels[Math.max(Number(cellMonth) - 1, 0)] || "el mes seleccionado"}...`
                : `Cargando composición acumulada de ${rowLabel || "la celda"}...`;
        }

        if (pnlDetailBody) {
            pnlDetailBody.innerHTML = '<tr><td colspan="15" class="dashboard-table__empty">Cargando detalle...</td></tr>';
        }
    }

    function buildPnlDetailSubtitle(detail) {
        const parts = [];
        if (detail?.cellLabel) {
            parts.push(detail.cellLabel);
        }

        if (detail?.verticalLabel) {
            parts.push(detail.verticalLabel);
        }

        if ((detail?.valueFormat || "currency") === "number") {
            parts.push(`${numberFormatter.format(Number(detail?.recordsCount || 0))} registros`);
            return parts.join(" · ");
        }

        parts.push(formatMetric(detail?.total || 0, detail?.valueFormat || "currency"));
        parts.push(`${numberFormatter.format(Number(detail?.recordsCount || 0))} registros`);
        return parts.join(" · ");
    }

    function buildPnlDetailSelectOptions(options, currentKey, currentLabel, currentValue, useNumericValue) {
        const normalizedOptions = Array.isArray(options) ? [...options] : [];
        const hasCurrent = normalizedOptions.some(option => {
            if (useNumericValue) {
                return Number(option?.value) === Number(currentValue);
            }

            return (option?.key || "") === (currentKey || "");
        });

        if (!hasCurrent && ((useNumericValue && Number.isFinite(Number(currentValue))) || (!useNumericValue && currentKey))) {
            normalizedOptions.unshift({
                key: currentKey || String(currentValue || ""),
                label: currentLabel || "Actual",
                value: useNumericValue ? Number(currentValue) : currentValue
            });
        }

        return normalizedOptions;
    }

    function renderPnlVerticalEditor(record, verticalOptions) {
        if (!record?.canEditVertical) {
            return `<span class="dashboard-pnl-detail__static">${escapeHtml(record?.verticalLabel || "No aplica")}</span>`;
        }

        const options = buildPnlDetailSelectOptions(
            verticalOptions,
            record?.verticalKey || "",
            record?.verticalLabel || "",
            record?.verticalKey || "",
            false
        );

        return `
            <select class="form-select form-select-sm dashboard-select dashboard-select--detail" data-pnl-edit-field="vertical">
                ${options.map(option => `
                    <option value="${escapeHtml(option?.key || "")}" ${(option?.key || "") === (record?.verticalKey || "") ? "selected" : ""}>
                        ${escapeHtml(option?.label || option?.key || "")}
                    </option>
                `).join("")}
            </select>
        `;
    }

    function renderPnlCategoryEditor(record, categoryOptions) {
        if (!record?.canEditCategory) {
            return `<span class="dashboard-pnl-detail__static">${escapeHtml(record?.categoryLabel || "No aplica")}</span>`;
        }

        const options = buildPnlDetailSelectOptions(
            categoryOptions,
            String(record?.categoryOptionValue || ""),
            record?.categoryLabel || "",
            record?.categoryOptionValue,
            true
        );

        return `
            <select class="form-select form-select-sm dashboard-select dashboard-select--detail" data-pnl-edit-field="category">
                ${options.map(option => `
                    <option value="${escapeHtml(String(option?.value ?? ""))}" ${Number(option?.value) === Number(record?.categoryOptionValue) ? "selected" : ""}>
                        ${escapeHtml(option?.label || "")}
                    </option>
                `).join("")}
            </select>
        `;
    }

    function formatEditableDecimalValue(value) {
        const numericValue = Number(value ?? 0);
        return Number.isFinite(numericValue) ? numericValue.toFixed(2) : "0.00";
    }

    function parseEditableDecimalValue(value) {
        const normalizedValue = (value ?? "").toString().trim().replace(",", ".");
        if (!normalizedValue) {
            return NaN;
        }

        const numericValue = Number(normalizedValue);
        return Number.isFinite(numericValue) ? numericValue : NaN;
    }

    function parseCopiersDecimalInputValue(input, label) {
        const rawValue = input?.value ?? "";
        if (!rawValue.toString().trim()) {
            return 0;
        }

        const numericValue = parseEditableDecimalValue(rawValue);
        if (Number.isNaN(numericValue) || numericValue < 0) {
            throw new Error(`El valor de ${label} debe ser numerico y no puede ser negativo.`);
        }

        return numericValue;
    }

    function parseCopiersBillingDayValue() {
        const rawValue = (copiersBillingDayInput?.value || "").trim();
        if (!rawValue) {
            return null;
        }

        const numericValue = Number(rawValue);
        if (!Number.isInteger(numericValue) || numericValue < 1 || numericValue > 31) {
            throw new Error("El dia de facturacion debe estar entre 1 y 31.");
        }

        return numericValue;
    }

    function buildCopiersSavePayload() {
        const clientName = (copiersClientNameInput?.value || "").trim();
        const productName = (copiersProductNameInput?.value || "").trim();
        if (!clientName) {
            throw new Error("Debes indicar el cliente del registro.");
        }

        if (!productName) {
            throw new Error("Debes indicar el producto del registro.");
        }

        return {
            recordId: copiersRecordIdInput?.value || "",
            billingDay: parseCopiersBillingDayValue(),
            clientId: copiersClientIdInput?.value || "",
            clientName,
            productId: copiersProductIdInput?.value || "",
            productName,
            quantity: parseCopiersDecimalInputValue(copiersQuantityInput, "Cantidad"),
            includedOperations: parseCopiersDecimalInputValue(copiersIncludedOperationsInput, "Operaciones incluidas"),
            additionalOperation: parseCopiersDecimalInputValue(copiersAdditionalOperationInput, "cr07a_operacionadicional"),
            unitValueBeforeVat: parseCopiersDecimalInputValue(copiersUnitValueBeforeVatInput, "Valor unitario antes de IVA"),
            unitValueWithVat: parseCopiersDecimalInputValue(copiersUnitValueWithVatInput, "Valor unitario con IVA"),
            totalWithVat: parseCopiersDecimalInputValue(copiersTotalWithVatInput, "Total con IVA")
        };
    }

    function syncCopiersLookupSelection(input, hiddenInput, suggestions, labelKey) {
        const typedValue = (input?.value || "").trim();
        if (!typedValue) {
            if (hiddenInput) {
                hiddenInput.value = "";
            }
            return;
        }

        const match = (Array.isArray(suggestions) ? suggestions : []).find(item => {
            return (item?.[labelKey] || "").trim().toLowerCase() === typedValue.toLowerCase();
        });

        if (hiddenInput) {
            hiddenInput.value = match?.id || "";
        }
    }

    function renderCopiersLookupOptions(target, items, labelKey) {
        if (!target) {
            return;
        }

        const options = Array.isArray(items) ? items : [];
        target.innerHTML = options.map(item => `
            <option value="${escapeHtml(item?.[labelKey] || "")}"></option>
        `).join("");
    }

    function buildCopiersClientSearchUrl(query) {
        const baseUrl = app.dataset.copiersClientSearchUrl || "";
        return `${baseUrl}?q=${encodeURIComponent(query || "")}`;
    }

    function buildCopiersProductSearchUrl(query) {
        const baseUrl = app.dataset.copiersProductSearchUrl || "";
        return `${baseUrl}?q=${encodeURIComponent(query || "")}`;
    }

    function wireCopiersLookupInput(input, hiddenInput, datalist, stateKey, labelKey, urlBuilder) {
        if (!input || !hiddenInput) {
            return;
        }

        let timer = 0;
        let requestSequence = 0;

        const applySuggestions = items => {
            state[stateKey] = Array.isArray(items) ? items : [];
            renderCopiersLookupOptions(datalist, state[stateKey], labelKey);
            syncCopiersLookupSelection(input, hiddenInput, state[stateKey], labelKey);
        };

        input.addEventListener("input", () => {
            hiddenInput.value = "";
            const query = (input.value || "").trim();
            window.clearTimeout(timer);

            if (query.length < 2) {
                applySuggestions([]);
                return;
            }

            const currentSequence = ++requestSequence;
            timer = window.setTimeout(async () => {
                try {
                    const items = await fetchJson(urlBuilder(query));
                    if (currentSequence !== requestSequence) {
                        return;
                    }

                    applySuggestions(items);
                } catch {
                    if (currentSequence !== requestSequence) {
                        return;
                    }

                    applySuggestions([]);
                }
            }, 220);
        });

        input.addEventListener("change", () => {
            syncCopiersLookupSelection(input, hiddenInput, state[stateKey], labelKey);
        });

        input.addEventListener("blur", () => {
            syncCopiersLookupSelection(input, hiddenInput, state[stateKey], labelKey);
        });
    }

    function getCopiersFieldLabel(fieldKey) {
        return ({
            billingDay: "Dia facturacion",
            clientName: "Cliente",
            productName: "Producto",
            quantity: "Cantidad",
            includedOperations: "Operaciones incluidas",
            additionalOperation: "cr07a_operacionadicional",
            unitValueBeforeVat: "Valor unitario antes IVA",
            unitValueWithVat: "Valor unitario con IVA",
            totalWithVat: "Total con IVA"
        })[fieldKey] || "Registro";
    }

    async function saveCopiersEditor() {
        if (state.copiersEditorSaving) {
            return;
        }

        let payload;
        try {
            payload = buildCopiersSavePayload();
        } catch (error) {
            setStatus(copiersEditorStatus, "error", error instanceof Error ? error.message : "Revisa los datos del registro.");
            return;
        }

        setCopiersEditorSaving(true);
        setStatus(copiersEditorStatus, "info", payload.recordId ? "Guardando cambios en Dataverse..." : "Creando registro en Dataverse...");

        try {
            const result = await fetchJson(app.dataset.copiersSaveUrl || "", {
                method: "POST",
                body: JSON.stringify(payload)
            });

            closeCopiersEditorModal();
            await loadCopiers();
            setStatus(copiersStatusBanner, "info", result?.message || "Registro guardado correctamente.");
        } catch (error) {
            setStatus(copiersEditorStatus, "error", error instanceof Error ? error.message : "No fue posible guardar el registro.");
        } finally {
            setCopiersEditorSaving(false);
        }
    }

    function renderPnlAllocationEditor(record, fieldKey) {
        const numericValue = Number(record?.[fieldKey] || 0);
        if (!record?.canEditAllocation) {
            return `<span class="dashboard-pnl-detail__static">${escapeHtml(currencyFormatter.format(numericValue))}</span>`;
        }

        return `
            <input
                type="number"
                step="0.01"
                class="form-control form-control-sm dashboard-detail-number-input"
                data-pnl-edit-field="${escapeHtml(fieldKey)}"
                value="${escapeHtml(formatEditableDecimalValue(numericValue))}" />
        `;
    }

    function renderPnlDetail(detail) {
        state.pnlDetail = detail || null;

        if (pnlDetailTitle) {
            pnlDetailTitle.textContent = detail?.rowLabel || "Detalle de la celda";
        }

        if (pnlDetailSubtitle) {
            pnlDetailSubtitle.textContent = buildPnlDetailSubtitle(detail);
        }

        if (!pnlDetailBody) {
            return;
        }

        const records = Array.isArray(detail?.records) ? detail.records : [];
        if (!records.length) {
            pnlDetailBody.innerHTML = `
                <tr>
                    <td colspan="15" class="dashboard-table__empty">${escapeHtml(detail?.emptyMessage || "No encontramos registros para esta celda.")}</td>
                </tr>
            `;
            return;
        }

        const cellValueFormat = detail?.valueFormat || "currency";
        pnlDetailBody.innerHTML = records.map(record => {
            const isSaving = state.pnlDetailSavingRecordId && state.pnlDetailSavingRecordId === record.recordId;
            const canEditRecord = Boolean(record.canEditVertical || record.canEditCategory || record.canEditAllocation);
            return `
                <tr
                    data-record-id="${escapeHtml(record.recordId || "")}"
                    data-source-type="${escapeHtml(record.sourceType || "")}"
                    data-original-vertical="${escapeHtml(record.verticalKey || "")}"
                    data-original-category="${escapeHtml(String(record.categoryOptionValue ?? ""))}"
                    data-original-cloud="${escapeHtml(String(Number(record.cloudValue || 0)))}"
                    data-original-copiers="${escapeHtml(String(Number(record.copiersValue || 0)))}">
                    <td>${escapeHtml(record.sourceLabel || record.sourceType || "-")}</td>
                    <td>${escapeHtml(record.documentNumber || "-")}</td>
                    <td>${escapeHtml(record.dateDisplay || "-")}</td>
                    <td>${escapeHtml(record.description || "-")}</td>
                    <td>${renderPnlCategoryEditor(record, detail?.categoryOptions)}</td>
                    <td class="text-end">${escapeHtml(currencyFormatter.format(Number(record.totalInvoice || 0)))}</td>
                    <td class="text-end">${escapeHtml(currencyFormatter.format(Number(record.vatValue || 0)))}</td>
                    <td class="text-end">${escapeHtml(currencyFormatter.format(Number(record.totalBeforeVatValue || 0)))}</td>
                    <td class="text-end">${escapeHtml(currencyFormatter.format(Number(record.paymentValue || 0)))}</td>
                    <td>${renderPnlVerticalEditor(record, detail?.verticalOptions)}</td>
                    <td>${escapeHtml(record.assignedMonthDisplay || "-")}</td>
                    <td class="text-end">${renderPnlAllocationEditor(record, "cloudValue")}</td>
                    <td class="text-end">${renderPnlAllocationEditor(record, "copiersValue")}</td>
                    <td class="text-end"><strong>${escapeHtml(formatMetric(record.cellValue || 0, cellValueFormat))}</strong></td>
                    <td>
                        ${canEditRecord
                            ? `<button type="button" class="btn btn-sm btn-outline-primary" data-pnl-detail-save ${isSaving ? "disabled" : ""}>${isSaving ? "Guardando..." : "Guardar"}</button>`
                            : '<span class="dashboard-pnl-detail__static">Solo lectura</span>'}
                    </td>
                </tr>
            `;
        }).join("");
    }

    async function loadPnlDetail(rowKey, rowLabel, cellMonth) {
        if (!rowKey) {
            return;
        }

        const resolvedCellMonth = Number(cellMonth);
        state.pnlDetailContext = {
            rowKey,
            rowLabel: rowLabel || "Detalle de la celda",
            cellMonth: Number.isInteger(resolvedCellMonth) && resolvedCellMonth >= 1 && resolvedCellMonth <= 12
                ? resolvedCellMonth
                : null
        };

        state.pnlDetailLoading = true;
        openPnlDetailModal();
        renderPnlDetailLoading(rowLabel, state.pnlDetailContext.cellMonth);
        setStatus(pnlDetailStatus, "info", "Cargando detalle de la celda...");

        try {
            const detail = await fetchJson(buildPnlDetailUrl(rowKey, state.pnlDetailContext.cellMonth));
            renderPnlDetail(detail);
            setStatus(pnlDetailStatus, "", "");
        } catch (error) {
            setStatus(pnlDetailStatus, "error", error instanceof Error ? error.message : "No fue posible cargar el detalle de la celda.");
            if (pnlDetailBody) {
                pnlDetailBody.innerHTML = '<tr><td colspan="15" class="dashboard-table__empty">No fue posible cargar el detalle.</td></tr>';
            }
            throw error;
        } finally {
            state.pnlDetailLoading = false;
        }
    }

    async function refreshCurrentPnlDetail() {
        if (!state.pnlDetailContext) {
            return;
        }

        await loadPnlDetail(
            state.pnlDetailContext.rowKey,
            state.pnlDetailContext.rowLabel,
            state.pnlDetailContext.cellMonth
        );
    }

    async function savePnlDetailRecord(saveButton) {
        if (!saveButton) {
            return;
        }

        const row = saveButton.closest("tr[data-record-id]");
        if (!row) {
            return;
        }

        const payload = {
            sourceType: row.dataset.sourceType || "",
            recordId: row.dataset.recordId || ""
        };

        const verticalSelect = row.querySelector("[data-pnl-edit-field='vertical']");
        const categorySelect = row.querySelector("[data-pnl-edit-field='category']");
        const cloudInput = row.querySelector("[data-pnl-edit-field='cloudValue']");
        const copiersInput = row.querySelector("[data-pnl-edit-field='copiersValue']");
        const originalVertical = (row.dataset.originalVertical || "").toLowerCase();
        const originalCategory = Number(row.dataset.originalCategory || 0);
        const originalCloud = Number(row.dataset.originalCloud || 0);
        const originalCopiers = Number(row.dataset.originalCopiers || 0);
        const verticalValue = verticalSelect && !verticalSelect.disabled ? (verticalSelect.value || "").toLowerCase() : "";
        if ((verticalValue === "cloud" || verticalValue === "copiers") && verticalValue !== originalVertical) {
            payload.verticalKey = verticalValue;
        }

        const categoryValue = categorySelect && !categorySelect.disabled ? Number(categorySelect.value || 0) : NaN;
        if (!Number.isNaN(categoryValue) && categoryValue > 0 && categoryValue !== originalCategory) {
            payload.categoryOptionValue = categoryValue;
        }

        if (cloudInput) {
            const cloudValue = parseEditableDecimalValue(cloudInput.value);
            if (Number.isNaN(cloudValue) || cloudValue < 0) {
                setStatus(pnlDetailStatus, "error", "El valor Cloud debe ser numerico y no puede ser negativo.");
                return;
            }

            if (Math.abs(cloudValue - originalCloud) >= 0.01) {
                payload.cloudValue = cloudValue;
            }
        }

        if (copiersInput) {
            const copiersValue = parseEditableDecimalValue(copiersInput.value);
            if (Number.isNaN(copiersValue) || copiersValue < 0) {
                setStatus(pnlDetailStatus, "error", "El valor Copiers debe ser numerico y no puede ser negativo.");
                return;
            }

            if (Math.abs(copiersValue - originalCopiers) >= 0.01) {
                payload.copiersValue = copiersValue;
            }
        }

        if (!payload.verticalKey && !payload.categoryOptionValue && payload.cloudValue === undefined && payload.copiersValue === undefined) {
            setStatus(pnlDetailStatus, "info", "No hay cambios pendientes en este registro.");
            return;
        }

        state.pnlDetailSavingRecordId = payload.recordId;
        renderPnlDetail(state.pnlDetail);
        setStatus(pnlDetailStatus, "info", "Guardando cambios en Dataverse...");

        try {
            const result = await fetchJson(app.dataset.pnlDetailUpdateUrl, {
                method: "POST",
                body: JSON.stringify(payload)
            });

            await loadPnl();
            await refreshCurrentPnlDetail();
            setStatus(pnlDetailStatus, "info", result?.message || "Registro actualizado correctamente.");
        } catch (error) {
            state.pnlDetailSavingRecordId = "";
            renderPnlDetail(state.pnlDetail);
            setStatus(pnlDetailStatus, "error", error instanceof Error ? error.message : "No fue posible guardar el registro.");
            return;
        }

        state.pnlDetailSavingRecordId = "";
        renderPnlDetail(state.pnlDetail);
    }

    function buildYearOptions() {
        if (!yearFilter) {
            return;
        }

        const options = [];
        for (let year = currentYear + 1; year >= currentYear - 5; year -= 1) {
            options.push(`<option value="${year}">${year}</option>`);
        }

        yearFilter.innerHTML = options.join("");
        yearFilter.value = String(state.year);
    }

    function buildPnlYearOptions() {
        if (!pnlYearFilter) {
            return;
        }

        const options = [];
        for (let year = currentYear + 1; year >= currentYear - 5; year -= 1) {
            options.push(`<option value="${year}">${year}</option>`);
        }

        pnlYearFilter.innerHTML = options.join("");
        pnlYearFilter.value = String(state.pnlYear);
    }

    function buildLicenciamientoYearOptions() {
        if (!licenciamientoYearFilter) {
            return;
        }

        const options = [];
        for (let year = currentYear + 1; year >= currentYear - 6; year -= 1) {
            options.push(`<option value="${year}">${year}</option>`);
        }

        licenciamientoYearFilter.innerHTML = options.join("");
        licenciamientoYearFilter.value = String(state.licenciamientoYear);
    }

    function buildPnlMonthOptions(maxMonth = 12) {
        if (!pnlMonthFilter) {
            return;
        }

        const safeMaxMonth = Math.min(Math.max(Number(maxMonth || 1), 1), 12);
        if (state.pnlMonth > safeMaxMonth) {
            state.pnlMonth = safeMaxMonth;
        }

        pnlMonthFilter.innerHTML = monthLabels
            .slice(0, safeMaxMonth)
            .map((label, index) => `<option value="${index + 1}">${escapeHtml(label)}</option>`)
            .join("");
        pnlMonthFilter.value = String(state.pnlMonth);
    }

    function buildLicenciamientoMonthOptions(monthOptions) {
        if (!licenciamientoMonthFilter) {
            return;
        }

        const options = Array.isArray(monthOptions) && monthOptions.length
            ? monthOptions
            : monthLabels.map((label, index) => ({ value: index + 1, label, hasData: false }));

        licenciamientoMonthFilter.innerHTML = options.map(option => {
            const value = Number(option?.value || 1);
            const label = option?.label || monthLabels[Math.max(value - 1, 0)] || "Mes";
            const suffix = option?.hasData ? "" : " · sin datos";
            return `<option value="${value}">${escapeHtml(`${label}${suffix}`)}</option>`;
        }).join("");
        licenciamientoMonthFilter.value = String(state.licenciamientoMonth);
    }

    function getDefaultValue(period, year) {
        if (year !== currentYear) {
            return 1;
        }

        const today = new Date();
        switch (period) {
            case "bimonthly":
                return Math.floor(today.getMonth() / 2) + 1;
            case "quarter":
                return Math.floor(today.getMonth() / 3) + 1;
            case "semester":
                return today.getMonth() < 6 ? 1 : 2;
            case "year":
                return 1;
            default:
                return today.getMonth() + 1;
        }
    }

    function formatLocalDateValue(date) {
        const year = date.getFullYear();
        const month = String(date.getMonth() + 1).padStart(2, "0");
        const day = String(date.getDate()).padStart(2, "0");
        return `${year}-${month}-${day}`;
    }

    function getActivePeriodDateRange() {
        const year = Number(state.year || currentYear);
        const value = Math.max(Number(state.value || 1), 1);
        let startMonth = 0;
        let endMonth = 0;

        switch (state.period) {
            case "bimonthly":
                startMonth = Math.min((value - 1) * 2, 10);
                endMonth = startMonth + 1;
                break;
            case "quarter":
                startMonth = Math.min((value - 1) * 3, 9);
                endMonth = startMonth + 2;
                break;
            case "semester":
                startMonth = value <= 1 ? 0 : 6;
                endMonth = startMonth + 5;
                break;
            case "year":
                startMonth = 0;
                endMonth = 11;
                break;
            default:
                startMonth = Math.min(Math.max(value - 1, 0), 11);
                endMonth = startMonth;
                break;
        }

        return {
            start: formatLocalDateValue(new Date(year, startMonth, 1)),
            end: formatLocalDateValue(new Date(year, endMonth + 1, 0))
        };
    }

    function syncSiigoDateRangeWithActivePeriod() {
        const range = getActivePeriodDateRange();
        if (siigoStartDateInput) {
            siigoStartDateInput.value = range.start;
        }

        if (siigoEndDateInput) {
            siigoEndDateInput.value = range.end;
        }

        if (siigoPeriodReference) {
            siigoPeriodReference.textContent = `${range.start} - ${range.end}`;
        }
    }

    function buildValueOptions() {
        if (!valueFilter) {
            return;
        }

        const options = [];
        switch (state.period) {
            case "bimonthly":
                ["B1 Ene-Feb", "B2 Mar-Abr", "B3 May-Jun", "B4 Jul-Ago", "B5 Sep-Oct", "B6 Nov-Dic"].forEach((label, index) => {
                    options.push({ value: index + 1, label });
                });
                break;
            case "quarter":
                ["T1", "T2", "T3", "T4"].forEach((label, index) => {
                    options.push({ value: index + 1, label });
                });
                break;
            case "semester":
                ["S1", "S2"].forEach((label, index) => {
                    options.push({ value: index + 1, label });
                });
                break;
            case "year":
                options.push({ value: 1, label: "Ano completo" });
                break;
            default:
                ["Enero", "Febrero", "Marzo", "Abril", "Mayo", "Junio", "Julio", "Agosto", "Septiembre", "Octubre", "Noviembre", "Diciembre"]
                    .forEach((label, index) => {
                        options.push({ value: index + 1, label });
                    });
                break;
        }

        if (!options.some(option => option.value === state.value)) {
            state.value = getDefaultValue(state.period, state.year);
        }

        valueFilter.innerHTML = options
            .map(option => `<option value="${option.value}">${escapeHtml(option.label)}</option>`)
            .join("");
        valueFilter.value = String(state.value);
    }

    function buildBillingUrl() {
        const params = new URLSearchParams({
            year: String(state.year),
            period: state.period,
            value: String(state.value)
        });

        return `${app.dataset.billingUrl}?${params.toString()}`;
    }

    function buildBillingInvoicesUrl(options = {}) {
        const baseUrl = app.dataset.billingInvoicesUrl || "";
        if (!baseUrl) {
            return "";
        }

        const url = new URL(baseUrl, window.location.origin);
        url.searchParams.set("year", String(options.year ?? state.billingInvoicesYear));
        url.searchParams.set("month", String(options.month ?? state.billingInvoicesMonth));
        url.searchParams.set("page", String(options.page ?? state.billingInvoicesPage));
        url.searchParams.set("pageSize", String(options.pageSize ?? state.billingInvoicesPageSize));
        if (options.duplicatesOnly ?? state.billingInvoicesDuplicatesOnly) {
            url.searchParams.set("duplicatesOnly", "true");
        }
        return `${url.pathname}${url.search}`;
    }

    function buildBillingCreditNotesUrl() {
        return app.dataset.billingCreditNotesUrl || "";
    }

    function buildCloudBillingCurrentMonthUrl() {
        return app.dataset.billingCurrentMonthUrl || "";
    }

    function buildBillingInvoiceSaveUrl() {
        return app.dataset.billingInvoiceSaveUrl || "";
    }

    function buildBillingInvoicesDeleteUrl() {
        return app.dataset.billingInvoicesDeleteUrl || "";
    }

    function buildBillingInvoicesContractUrl() {
        return app.dataset.billingInvoicesContractUrl || "";
    }

    function buildTaxesUrl() {
        const params = new URLSearchParams({
            reteFuenteYear: String(state.taxesFilters.reteFuenteYear),
            reteFuenteMonth: String(state.taxesFilters.reteFuenteMonth),
            reteIcaYear: String(state.taxesFilters.reteIcaYear),
            reteIcaPeriod: String(state.taxesFilters.reteIcaPeriod),
            ivaYear: String(state.taxesFilters.ivaYear),
            ivaPeriod: String(state.taxesFilters.ivaPeriod),
            incomeTaxYear: String(state.taxesFilters.incomeTaxYear)
        });

        return `${app.dataset.taxesUrl}?${params.toString()}`;
    }

    function buildBillingClientReportUrl(clientId, clientName) {
        const baseUrl = app.dataset.billingClientReportUrl || "";
        const params = new URLSearchParams({
            clientId: clientId || "",
            clientName: clientName || ""
        });

        return `${baseUrl}?${params.toString()}`;
    }

    function buildBillingClientReportExportUrl() {
        return billingClientReportExportUrl || "";
    }

    function buildSiigoCustomersUrl() {
        return app.dataset.siigoCustomersUrl || "";
    }

    function buildSiigoCustomerSearchUrl(query) {
        const baseUrl = app.dataset.siigoCustomerSearchUrl || "";
        return `${baseUrl}?q=${encodeURIComponent(normalizeNitValue(query || ""))}`;
    }

    function buildSiigoInvoicesUrl() {
        const baseUrl = app.dataset.siigoInvoicesUrl || "";
        const selectedCustomer = getSelectedSiigoCustomer();
        const selectedId = siigoCustomerSelect?.value || siigoCustomerIdInput?.value || "";
        const isDirectLookup = Boolean(selectedCustomer?.directLookup) || selectedId.startsWith("nit:");
        const params = new URLSearchParams({
            customerId: isDirectLookup ? "" : selectedId,
            customerQuery: selectedCustomer?.identification || "",
            startDate: siigoStartDateInput?.value || "",
            endDate: siigoEndDateInput?.value || ""
        });

        return `${baseUrl}?${params.toString()}`;
    }

    function buildSiigoInvoicesDownloadUrl() {
        return app.dataset.siigoInvoicesDownloadUrl || "";
    }

    function normalizeNitValue(value) {
        return (value || "").toString().replace(/\D/g, "");
    }

    function getSelectedSiigoCustomer() {
        const selectedId = siigoCustomerSelect?.value || siigoCustomerIdInput?.value || "";
        if (!selectedId) {
            return null;
        }

        return state.siigoCustomers.find(customer =>
            String(customer?.id || "") === selectedId) || null;
    }

    function buildSiigoCustomerKey(customer) {
        return (customer?.id || `${customer?.identification || ""}:${customer?.branchOffice || 0}:${customer?.type || ""}`).toString();
    }

    function upsertSiigoCustomers(items) {
        const incoming = Array.isArray(items) ? items : [];
        const byKey = new Map();

        state.siigoCustomers.forEach(customer => {
            const key = buildSiigoCustomerKey(customer);
            if (key) {
                byKey.set(key, customer);
            }
        });

        incoming.forEach(customer => {
            const key = buildSiigoCustomerKey(customer);
            if (key) {
                byKey.set(key, customer);
            }
        });

        state.siigoCustomers = Array.from(byKey.values())
            .sort((left, right) => buildSiigoCustomerOptionLabel(left).localeCompare(
                buildSiigoCustomerOptionLabel(right),
                "es",
                { numeric: true, sensitivity: "base" }));
    }

    function buildSiigoCustomerOptionLabel(customer) {
        const name = customer?.commercialName || customer?.name || customer?.displayName || "Cliente sin nombre";
        const nit = customer?.identification ? `NIT ${customer.identification}` : "NIT sin dato";
        const branch = Number(customer?.branchOffice || 0) > 0 ? `Sucursal ${customer.branchOffice}` : "";
        const typeValue = (customer?.type || "").toString().trim();
        const type = typeValue ? `Tipo ${typeValue}` : "";
        const status = customer?.active === false ? "Inactivo" : "";

        return [name, nit, branch, type, status]
            .filter(Boolean)
            .join(" - ");
    }

    function buildSiigoCustomersSummary(customers) {
        const items = Array.isArray(customers) ? customers : [];
        const counts = items.reduce((summary, customer) => {
            const type = (customer?.type || "Sin tipo").toString().trim() || "Sin tipo";
            summary.byType[type] = (summary.byType[type] || 0) + 1;
            if (customer?.active === false) {
                summary.inactive += 1;
            }

            return summary;
        }, { byType: {}, inactive: 0 });

        const typeSummary = Object.entries(counts.byType)
            .sort(([left], [right]) => left.localeCompare(right, "es", { sensitivity: "base" }))
            .map(([type, count]) => `${type}: ${numberFormatter.format(count)}`)
            .join(", ");
        const inactiveSummary = counts.inactive > 0
            ? `, inactivos: ${numberFormatter.format(counts.inactive)}`
            : "";

        return `${numberFormatter.format(items.length)} (${typeSummary || "sin desglose"}${inactiveSummary})`;
    }

    function renderSiigoCustomerSelect(message) {
        if (!siigoCustomerSelect) {
            return;
        }

        const previousId = siigoCustomerSelect.value || siigoCustomerIdInput?.value || "";
        const customers = Array.isArray(state.siigoCustomers) ? state.siigoCustomers : [];
        if (!customers.length) {
            siigoCustomerSelect.innerHTML = `<option value="">${escapeHtml(message || "Consulta clientes en Siigo...")}</option>`;
            siigoCustomerSelect.value = "";
            if (siigoCustomerIdInput) {
                siigoCustomerIdInput.value = "";
            }
            syncSiigoCustomerControls();
            return;
        }

        siigoCustomerSelect.innerHTML = [
            '<option value="">Selecciona un cliente...</option>',
            ...customers.map(customer => `<option value="${escapeHtml(customer?.id || "")}">${escapeHtml(buildSiigoCustomerOptionLabel(customer))}</option>`)
        ].join("");

        const nextId = customers.some(customer => String(customer?.id || "") === previousId)
            ? previousId
            : "";
        siigoCustomerSelect.value = nextId;
        if (siigoCustomerIdInput) {
            siigoCustomerIdInput.value = nextId;
        }

        syncSiigoCustomerControls();
    }

    function syncSiigoCustomerSelection() {
        const selectedCustomer = getSelectedSiigoCustomer();

        if (siigoCustomerIdInput) {
            siigoCustomerIdInput.value = selectedCustomer?.id || "";
        }

        if (siigoCustomerReference) {
            siigoCustomerReference.textContent = selectedCustomer?.displayName || selectedCustomer?.name || "-";
        }

        if (siigoNitReference) {
            siigoNitReference.textContent = selectedCustomer?.identification || "-";
        }
    }

    function formatSiigoUiError(action, error, url) {
        const message = error instanceof Error
            ? error.message
            : "No fue posible completar la solicitud.";
        const status = error?.status
            ? `HTTP ${error.status}${error.statusText ? ` ${error.statusText}` : ""}. `
            : "";
        let route = "";

        if (url) {
            try {
                route = ` Ruta: ${new URL(url, window.location.origin).pathname}.`;
            } catch {
                route = "";
            }
        }

        return `Error al ${action}. ${status}Detalle: ${message}.${route}`;
    }

    function buildTaxesReteFuenteExportUrl() {
        const params = new URLSearchParams({
            reteFuenteYear: String(state.taxesFilters.reteFuenteYear),
            reteFuenteMonth: String(state.taxesFilters.reteFuenteMonth)
        });

        return `${taxesReteFuenteExportUrl}?${params.toString()}`;
    }

    function buildTaxesReteIcaExportUrl() {
        const params = new URLSearchParams({
            reteIcaYear: String(state.taxesFilters.reteIcaYear),
            reteIcaPeriod: String(state.taxesFilters.reteIcaPeriod)
        });

        return `${taxesReteIcaExportUrl}?${params.toString()}`;
    }

    function buildTaxesVatExportUrl() {
        const params = new URLSearchParams({
            ivaYear: String(state.taxesFilters.ivaYear),
            ivaPeriod: String(state.taxesFilters.ivaPeriod)
        });

        return `${taxesVatExportUrl}?${params.toString()}`;
    }

    function buildPortfolioUrl() {
        return app.dataset.portfolioUrl || "";
    }

    function buildAccountStatementClientSearchUrl(query) {
        const baseUrl = app.dataset.accountStatementClientSearchUrl || "";
        return `${baseUrl}?q=${encodeURIComponent(query || "")}`;
    }

    function buildAccountStatementUrl(clientId, clientName) {
        const baseUrl = app.dataset.accountStatementUrl || "";
        const params = new URLSearchParams({
            clientId: clientId || "",
            clientName: clientName || ""
        });

        return `${baseUrl}?${params.toString()}`;
    }

    function buildAccountStatementPdfUrl() {
        const baseUrl = accountStatementPdfUrl || "";
        if (!baseUrl) {
            return "";
        }

        const params = new URLSearchParams({
            clientId: state.accountStatementDetail?.clientId || accountStatementClientIdInput?.value || "",
            clientName: state.accountStatementDetail?.clientName || accountStatementClientSearch?.value || ""
        });

        return `${baseUrl}?${params.toString()}`;
    }

    function buildBusinessUrl() {
        return app.dataset.businessUrl || "";
    }

    function buildBusinessBillingUrl() {
        const baseUrl = app.dataset.businessBillingUrl || "";
        if (!baseUrl) {
            return "";
        }

        const params = new URLSearchParams({
            start: `${normalizeBusinessBillingMonthKey(state.businessBillingStart) || `${currentYear}-01`}-01`,
            end: `${normalizeBusinessBillingMonthKey(state.businessBillingEnd) || `${currentYear}-${String(currentMonth).padStart(2, "0")}`}-01`,
            granularity: state.businessBillingGranularity || "month"
        });

        return `${baseUrl}?${params.toString()}`;
    }

    function buildCopiersUrl() {
        return app.dataset.copiersUrl || "";
    }

    function buildCopiersClientInvoicesUrl(clientId, clientName) {
        const baseUrl = app.dataset.copiersClientInvoicesUrl || "";
        const params = new URLSearchParams({
            clientId: clientId || "",
            clientName: clientName || ""
        });

        return `${baseUrl}?${params.toString()}`;
    }

    function buildCopiersEquipmentUrl() {
        return app.dataset.copiersEquipmentUrl || "";
    }

    function buildCopiersInventoryUrl() {
        return app.dataset.copiersInventoryUrl || "";
    }

    function buildCopiersInventoryExportUrl() {
        return app.dataset.copiersInventoryExportUrl || "";
    }

    function buildCopiersEquipmentMovementsUrl() {
        return app.dataset.copiersEquipmentMovementsUrl || "";
    }

    function buildCopiersCountersUrl() {
        const baseUrl = app.dataset.copiersCountersUrl || "";
        const params = new URLSearchParams({
            year: String(state.copiersCountersYear),
            month: String(state.copiersCountersMonth),
            clientId: state.copiersCountersClientId || "",
            clientName: state.copiersCountersClientName || ""
        });

        return `${baseUrl}?${params.toString()}`;
    }

    function buildCopiersCountersPdfUrl() {
        const baseUrl = copiersCountersPdfUrl || "";
        if (!baseUrl) {
            return "";
        }

        const params = new URLSearchParams({
            year: String(state.copiersCountersYear),
            month: String(state.copiersCountersMonth),
            clientId: state.copiersCountersClientId || "",
            clientName: state.copiersCountersClientName || ""
        });

        return `${baseUrl}?${params.toString()}`;
    }

    function buildCopiersEquipmentDetailUrl(equipmentId) {
        const baseUrl = app.dataset.copiersEquipmentDetailUrl || "";
        const params = new URLSearchParams({
            equipmentId: equipmentId || ""
        });

        return `${baseUrl}?${params.toString()}`;
    }

    function buildCopiersEquipmentAssignmentUrl() {
        return app.dataset.copiersEquipmentAssignmentUrl || "";
    }

    function buildCopiersLineEquipmentAssignmentUrl(lineId, clientId) {
        const baseUrl = app.dataset.copiersLineEquipmentAssignmentUrl || "";
        const params = new URLSearchParams({
            lineId: lineId || "",
            clientId: clientId || ""
        });

        return `${baseUrl}?${params.toString()}`;
    }

    function buildCopiersLineEquipmentAssignmentSaveUrl() {
        return app.dataset.copiersLineEquipmentAssignmentSaveUrl || "";
    }

    function buildCopiersMaintenanceFileUrl(maintenanceId) {
        const baseUrl = app.dataset.copiersMaintenanceFileUrl || "";
        const params = new URLSearchParams({
            maintenanceId: maintenanceId || ""
        });

        return `${baseUrl}?${params.toString()}`;
    }

    function buildCopiersEquipmentMovementFileUrl(movementId) {
        const baseUrl = app.dataset.copiersEquipmentMovementFileUrl || "";
        const params = new URLSearchParams({
            movementId: movementId || ""
        });

        return `${baseUrl}?${params.toString()}`;
    }

    function buildPnlUrl() {
        const params = new URLSearchParams({
            year: String(state.pnlYear),
            month: String(state.pnlMonth),
            vertical: state.pnlVertical
        });

        return `${app.dataset.pnlUrl}?${params.toString()}`;
    }

    function buildLicenciamientoUrl() {
        const params = new URLSearchParams({
            year: String(state.licenciamientoYear),
            month: String(state.licenciamientoMonth)
        });

        return `${app.dataset.licenciamientoUrl || ""}?${params.toString()}`;
    }

    function buildUtilityUrl() {
        return app.dataset.utilityUrl || "";
    }

    function buildYtdUrl() {
        const baseUrl = app.dataset.ytdUrl || "";
        const params = new URLSearchParams({
            year: String(state.ytdYear || currentYear)
        });

        return `${baseUrl}?${params.toString()}`;
    }

    function buildUtilityAssignmentUrl() {
        return app.dataset.utilityAssignmentUrl || "";
    }

    function buildPnlDetailUrl(rowKey, cellMonth) {
        const params = new URLSearchParams({
            year: String(state.pnlYear),
            cutoffMonth: String(state.pnlMonth),
            vertical: state.pnlVertical,
            rowKey: rowKey || ""
        });

        const resolvedCellMonth = Number(cellMonth);
        if (Number.isInteger(resolvedCellMonth) && resolvedCellMonth >= 1 && resolvedCellMonth <= 12) {
            params.set("cellMonth", String(resolvedCellMonth));
        }

        return `${app.dataset.pnlDetailUrl}?${params.toString()}`;
    }

    async function fetchJson(url, options = {}) {
        const headers = {
            Accept: "application/json",
            ...(options.headers || {})
        };
        if (options.body && !headers["Content-Type"]) {
            headers["Content-Type"] = "application/json";
        }

        const response = await fetch(url, {
            method: options.method || "GET",
            headers,
            body: options.body,
            cache: options.cache || "default"
        });

        const contentType = response.headers.get("content-type") || "";
        if (!response.ok) {
            const rawBody = await response.text();
            let message = rawBody;

            if (contentType.includes("application/json")) {
                try {
                    const payload = rawBody ? JSON.parse(rawBody) : null;
                    message = typeof payload === "string"
                        ? payload
                        : payload?.message || payload?.title || payload?.error?.message || rawBody;
                } catch {
                    message = rawBody;
                }
            }

            const error = new Error(message || "No fue posible completar la solicitud.");
            error.status = response.status;
            error.statusText = response.statusText || "";
            error.url = response.url || url;
            throw error;
        }

        if (!contentType.includes("application/json")) {
            const message = await response.text();
            throw new Error(message || "La respuesta del servidor no fue valida.");
        }

        return response.json();
    }

    function resetBillingReportPreview() {
        if (billingReportPreview) {
            billingReportPreview.hidden = true;
        }

        if (billingReportPreviewFrame) {
            billingReportPreviewFrame.removeAttribute("src");
        }

        if (billingReportPreviewLink) {
            billingReportPreviewLink.href = "#";
        }
    }

    function resetBillingReportReference() {
        if (billingReportClientReference) {
            billingReportClientReference.textContent = "-";
        }

        if (billingReportNitReference) {
            billingReportNitReference.textContent = "-";
        }
    }

    function isHttpUrl(value) {
        return /^https?:\/\//i.test((value || "").trim());
    }

    function openBillingReportPreview(row) {
        const publicUrl = (row?.publicUrl || "").trim();
        if (!isHttpUrl(publicUrl) || !billingReportPreview) {
            resetBillingReportPreview();
            return;
        }

        billingReportPreview.hidden = false;

        if (billingReportPreviewTitle) {
            billingReportPreviewTitle.textContent = row?.invoiceNumber
                ? `Factura ${row.invoiceNumber}`
                : "Factura";
        }

        if (billingReportPreviewLink) {
            billingReportPreviewLink.href = publicUrl;
        }

        if (billingReportPreviewFrame) {
            billingReportPreviewFrame.src = publicUrl;
        }
    }

    function renderBillingReportTable(detail) {
        state.billingReportDetail = detail || null;
        resetBillingReportPreview();

        const rows = Array.isArray(detail?.invoices) ? detail.invoices : [];
        const clientName = detail?.clientName || rows.find(row => row?.clientName)?.clientName || "";
        const companyTaxId = rows.find(row => (row?.companyTaxId || "").trim())?.companyTaxId || "";

        if (billingReportClientReference) {
            billingReportClientReference.textContent = clientName || "-";
        }

        if (billingReportNitReference) {
            billingReportNitReference.textContent = companyTaxId || "-";
        }

        if (billingReportResultsCount) {
            billingReportResultsCount.textContent = numberFormatter.format(Number(detail?.recordsCount || rows.length || 0));
        }

        if (!billingReportBody) {
            return;
        }

        if (!rows.length) {
            billingReportBody.innerHTML = `<tr><td colspan="8" class="dashboard-table__empty">${escapeHtml(detail?.emptyStateTitle || "No encontramos facturas para este cliente.")}</td></tr>`;
            syncBillingReportSelectionSummary();
            return;
        }

        billingReportBody.innerHTML = rows.map(row => {
            const totalInvoice = getNetInvoiceTotal(row);
            const publicUrl = (row.publicUrl || "").trim();
            const hasUrl = isHttpUrl(publicUrl);
            return `
                <tr data-billing-report-row-id="${escapeHtml(row.recordId || "")}">
                    <td>
                        <input type="checkbox"
                               class="form-check-input"
                               data-billing-report-select
                               data-record-id="${escapeHtml(row.recordId || "")}"
                               data-total="${escapeHtml(formatEditableDecimalValue(totalInvoice))}" />
                    </td>
                    <td>${escapeHtml(row.invoiceNumber || "-")}</td>
                    <td>${escapeHtml(row.emissionDateDisplay || "Sin fecha")}</td>
                    <td class="text-end">${escapeHtml(numberFormatter.format(Number(row.vatPercent || 0)))}%</td>
                    <td class="text-end">${escapeHtml(currencyFormatter.format(getNetInvoiceVat(row)))}</td>
                    <td class="text-end">${escapeHtml(currencyFormatter.format(totalInvoice))}</td>
                    <td class="text-end">
                        <input type="number"
                               min="0"
                               max="${escapeHtml(formatEditableDecimalValue(totalInvoice))}"
                               step="0.01"
                               class="form-control dashboard-report-amount-input"
                               value="${escapeHtml(formatEditableDecimalValue(totalInvoice))}"
                               data-billing-report-amount
                               data-total="${escapeHtml(formatEditableDecimalValue(totalInvoice))}"
                               disabled />
                    </td>
                    <td>
                        ${hasUrl
                            ? `<div class="dashboard-report-url-actions">
                                <a href="${escapeHtml(publicUrl)}" target="_blank" rel="noopener noreferrer" class="dashboard-link-btn">Abrir</a>
                                <button type="button" class="btn btn-sm btn-outline-secondary" data-billing-report-preview="${escapeHtml(row.recordId || "")}">Vista</button>
                            </div>`
                            : '<span class="dashboard-muted-text">Sin URL</span>'}
                    </td>
                </tr>
            `;
        }).join("");

        syncBillingReportSelectionSummary();
    }

    function syncBillingReportSelectionSummary() {
        const selected = billingReportBody
            ? Array.from(billingReportBody.querySelectorAll("[data-billing-report-select]:checked"))
            : [];
        let total = 0;

        selected.forEach(checkbox => {
            const row = checkbox.closest("tr");
            const amountInput = row?.querySelector("[data-billing-report-amount]");
            const amount = parseEditableDecimalValue(amountInput?.value);
            if (Number.isFinite(amount)) {
                total += amount;
            }
        });

        if (billingReportSelectedCount) {
            billingReportSelectedCount.textContent = numberFormatter.format(selected.length);
        }

        if (billingReportSelectedTotal) {
            billingReportSelectedTotal.textContent = currencyFormatter.format(total);
        }

        if (billingReportExportButton) {
            billingReportExportButton.disabled = state.billingReportLoading
                || state.billingReportExporting
                || selected.length === 0
                || !buildBillingClientReportExportUrl();
        }
    }

    function getBillingReportInvoiceById(recordId) {
        const rows = Array.isArray(state.billingReportDetail?.invoices)
            ? state.billingReportDetail.invoices
            : [];

        return rows.find(row => (row?.recordId || "") === recordId) || null;
    }

    function buildBillingReportExportPayload() {
        const selected = billingReportBody
            ? Array.from(billingReportBody.querySelectorAll("[data-billing-report-select]:checked"))
            : [];

        if (!selected.length) {
            throw new Error("Selecciona al menos una factura para exportar.");
        }

        const items = selected.map(checkbox => {
            const recordId = checkbox.dataset.recordId || "";
            const row = checkbox.closest("tr");
            const amountInput = row?.querySelector("[data-billing-report-amount]");
            const total = parseEditableDecimalValue(amountInput?.dataset.total);
            const amount = parseEditableDecimalValue(amountInput?.value);

            if (!Number.isFinite(amount) || amount < 0) {
                throw new Error("El valor a reportar debe ser numerico y no puede ser negativo.");
            }

            if (Number.isFinite(total) && amount > total) {
                throw new Error("El valor a reportar no puede superar el total de la factura.");
            }

            return {
                recordId,
                exportAmount: amount
            };
        });

        return {
            clientId: state.billingReportDetail?.clientId || billingReportClientIdInput?.value || "",
            clientName: state.billingReportDetail?.clientName || billingReportClientSearch?.value || "",
            items
        };
    }

    function resolveDownloadFileName(contentDisposition) {
        const header = contentDisposition || "";
        const encodedMatch = /filename\*=UTF-8''([^;]+)/i.exec(header);
        if (encodedMatch?.[1]) {
            return decodeURIComponent(encodedMatch[1].replace(/"/g, ""));
        }

        const regularMatch = /filename="?([^";]+)"?/i.exec(header);
        return regularMatch?.[1] || "";
    }

    async function exportBillingReport() {
        const url = buildBillingClientReportExportUrl();
        if (!url) {
            setStatus(billingReportStatus, "error", "No hay una URL configurada para exportar el reporte.");
            return;
        }

        let payload;
        try {
            payload = buildBillingReportExportPayload();
        } catch (error) {
            setStatus(billingReportStatus, "error", error instanceof Error ? error.message : "Revisa las facturas seleccionadas.");
            return;
        }

        setBillingReportExporting(true);
        setStatus(billingReportStatus, "info", "Preparando Excel...");

        try {
            const response = await fetch(url, {
                method: "POST",
                headers: {
                    Accept: "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                    "Content-Type": "application/json"
                },
                body: JSON.stringify(payload)
            });

            if (!response.ok) {
                const message = await response.text();
                throw new Error(message || "No fue posible exportar el reporte.");
            }

            const blob = await response.blob();
            const fileName = resolveDownloadFileName(response.headers.get("content-disposition"))
                || "reporte-facturas-cliente.xlsx";
            const objectUrl = URL.createObjectURL(blob);
            const link = document.createElement("a");
            link.href = objectUrl;
            link.download = fileName;
            document.body.appendChild(link);
            link.click();
            link.remove();
            window.setTimeout(() => URL.revokeObjectURL(objectUrl), 1000);
            setStatus(billingReportStatus, "success", "Excel generado correctamente.");
        } catch (error) {
            setStatus(billingReportStatus, "error", error instanceof Error ? error.message : "No fue posible exportar el reporte.");
        } finally {
            setBillingReportExporting(false);
        }
    }

    function setAccountStatementLoading(isLoading) {
        state.accountStatementLoading = Boolean(isLoading);
        if (accountStatementGenerateButton) {
            accountStatementGenerateButton.disabled = state.accountStatementLoading;
            accountStatementGenerateButton.textContent = state.accountStatementLoading ? "Generando..." : "Generar";
        }

        if (accountStatementClientSearch) {
            accountStatementClientSearch.disabled = state.accountStatementLoading;
        }

        syncAccountStatementPdfButton();
    }

    function setAccountStatementPdfLoading(isLoading) {
        state.accountStatementPdfLoading = Boolean(isLoading);
        if (accountStatementPdfButton) {
            accountStatementPdfButton.textContent = state.accountStatementPdfLoading ? "Generando..." : "Generar PDF";
        }

        syncAccountStatementPdfButton();
    }

    function syncAccountStatementPdfButton() {
        if (!accountStatementPdfButton) {
            return;
        }

        const rows = Array.isArray(state.accountStatementDetail?.invoices)
            ? state.accountStatementDetail.invoices
            : [];
        accountStatementPdfButton.disabled = state.accountStatementLoading
            || state.accountStatementPdfLoading
            || rows.length === 0
            || !buildAccountStatementPdfUrl();
    }

    function resetAccountStatementTable(message) {
        state.accountStatementDetail = null;
        if (accountStatementClientReference) {
            accountStatementClientReference.textContent = "-";
        }

        if (accountStatementCount) {
            accountStatementCount.textContent = "0";
        }

        if (accountStatementTotal) {
            accountStatementTotal.textContent = currencyFormatter.format(0);
        }

        if (accountStatementAsOf) {
            accountStatementAsOf.textContent = "-";
        }

        if (accountStatementBody) {
            accountStatementBody.innerHTML = `<tr><td colspan="6" class="dashboard-table__empty">${escapeHtml(message || "Busca un cliente para generar el estado de cuenta.")}</td></tr>`;
        }

        syncAccountStatementPdfButton();
    }

    function syncAccountStatementClientSelection() {
        const typedValue = (accountStatementClientSearch?.value || "").trim();
        const match = state.accountStatementClientSuggestions.find(item =>
            (item?.name || "").trim().toLowerCase() === typedValue.toLowerCase());

        if (accountStatementClientIdInput) {
            accountStatementClientIdInput.value = match?.id || "";
        }

        renderAccountStatementMatches(state.accountStatementClientSuggestions);
    }

    function renderAccountStatementMatches(items) {
        const options = Array.isArray(items) ? items : [];
        renderCopiersLookupOptions(accountStatementClientOptions, options, "name");

        if (!accountStatementMatches) {
            return;
        }

        const query = (accountStatementClientSearch?.value || "").trim();
        if (!query || query.length < 2) {
            accountStatementMatches.innerHTML = "";
            return;
        }

        if (!options.length) {
            accountStatementMatches.innerHTML = '<span class="dashboard-account-statement-matches__empty">Sin coincidencias.</span>';
            return;
        }

        const selectedId = accountStatementClientIdInput?.value || "";
        accountStatementMatches.innerHTML = options.map(item => {
            const itemId = item?.id || "";
            const isActive = itemId && itemId === selectedId;
            return `
                <button type="button"
                        class="dashboard-account-statement-match${isActive ? " is-active" : ""}"
                        data-account-statement-match-id="${escapeHtml(itemId)}"
                        data-account-statement-match-name="${escapeHtml(item?.name || "")}">
                    ${escapeHtml(item?.name || "Cliente sin nombre")}
                </button>
            `;
        }).join("");
    }

    async function searchAccountStatementClients() {
        const query = (accountStatementClientSearch?.value || "").trim();
        state.accountStatementRequestSequence += 1;
        const sequence = state.accountStatementRequestSequence;

        if (accountStatementClientIdInput) {
            accountStatementClientIdInput.value = "";
        }

        if (query.length < 2) {
            state.accountStatementClientSuggestions = [];
            renderAccountStatementMatches([]);
            return;
        }

        const url = buildAccountStatementClientSearchUrl(query);
        if (!url) {
            setStatus(accountStatementStatus, "error", "No hay una URL configurada para buscar clientes.");
            return;
        }

        try {
            const items = await fetchJson(url);
            if (sequence !== state.accountStatementRequestSequence) {
                return;
            }

            state.accountStatementClientSuggestions = Array.isArray(items) ? items : [];
            renderAccountStatementMatches(state.accountStatementClientSuggestions);
            syncAccountStatementClientSelection();
        } catch (error) {
            if (sequence !== state.accountStatementRequestSequence) {
                return;
            }

            state.accountStatementClientSuggestions = [];
            renderAccountStatementMatches([]);
            setStatus(accountStatementStatus, "error", error instanceof Error ? error.message : "No fue posible buscar clientes.");
        }
    }

    function renderAccountStatementTable(detail) {
        state.accountStatementDetail = detail || null;
        const rows = Array.isArray(detail?.invoices) ? detail.invoices : [];

        if (accountStatementClientReference) {
            accountStatementClientReference.textContent = detail?.clientName || "-";
        }

        if (accountStatementCount) {
            accountStatementCount.textContent = numberFormatter.format(Number(detail?.recordsCount || rows.length || 0));
        }

        if (accountStatementTotal) {
            accountStatementTotal.textContent = currencyFormatter.format(Number(detail?.totalAmount || 0));
        }

        if (accountStatementAsOf) {
            accountStatementAsOf.textContent = detail?.asOfDateLabel || "-";
        }

        if (!accountStatementBody) {
            syncAccountStatementPdfButton();
            return;
        }

        if (!rows.length) {
            accountStatementBody.innerHTML = `<tr><td colspan="6" class="dashboard-table__empty">${escapeHtml(detail?.emptyStateTitle || "No encontramos facturas pendientes para este cliente.")}</td></tr>`;
            syncAccountStatementPdfButton();
            return;
        }

        accountStatementBody.innerHTML = rows.map(row => {
            const isOverdue = (row.stateKey || "").toLowerCase() === "overdue";
            const badgeClass = isOverdue
                ? "dashboard-badge dashboard-badge--overdue"
                : "dashboard-badge is-warning";

            return `
                <tr>
                    <td>${escapeHtml(row.invoiceNumber || "-")}</td>
                    <td class="text-end">${escapeHtml(currencyFormatter.format(getNetInvoiceTotal(row)))}</td>
                    <td>${escapeHtml(row.emissionDateDisplay || "Sin fecha")}</td>
                    <td>${escapeHtml(row.dueDateDisplay || "Sin fecha")}</td>
                    <td><span class="${badgeClass}">${escapeHtml(row.stateLabel || "-")}</span></td>
                    <td>${escapeHtml(row.daysDisplay || "-")}</td>
                </tr>
            `;
        }).join("");

        syncAccountStatementPdfButton();
    }

    async function loadAccountStatement() {
        const clientId = accountStatementClientIdInput?.value || "";
        const clientName = (accountStatementClientSearch?.value || "").trim();
        if (!clientId && !clientName) {
            setStatus(accountStatementStatus, "error", "Busca un cliente para generar el estado de cuenta.");
            return;
        }

        const url = buildAccountStatementUrl(clientId, clientName);
        if (!url) {
            setStatus(accountStatementStatus, "error", "No hay una URL configurada para generar el estado de cuenta.");
            return;
        }

        setAccountStatementLoading(true);
        setStatus(accountStatementStatus, "info", "Generando estado de cuenta...");

        try {
            const detail = await fetchJson(url);
            renderAccountStatementTable(detail);
            setStatus(
                accountStatementStatus,
                detail?.hasData ? "success" : "info",
                detail?.hasData
                    ? "Estado de cuenta generado correctamente."
                    : (detail?.emptyStateMessage || "No encontramos facturas pendientes para este cliente."));
        } catch (error) {
            resetAccountStatementTable("No pudimos generar el estado de cuenta.");
            setStatus(accountStatementStatus, "error", error instanceof Error ? error.message : "No fue posible generar el estado de cuenta.");
        } finally {
            setAccountStatementLoading(false);
        }
    }

    async function downloadAccountStatementPdf() {
        const url = buildAccountStatementPdfUrl();
        if (!url) {
            setStatus(accountStatementStatus, "error", "No hay una URL configurada para generar el PDF.");
            return;
        }

        setAccountStatementPdfLoading(true);
        setStatus(accountStatementStatus, "info", "Preparando PDF...");

        try {
            const response = await fetch(url, {
                method: "GET",
                headers: {
                    Accept: "application/pdf"
                }
            });

            if (!response.ok) {
                const message = await response.text();
                throw new Error(message || "No fue posible generar el PDF.");
            }

            const blob = await response.blob();
            const fileName = resolveDownloadFileName(response.headers.get("content-disposition"))
                || "estado-de-cuenta.pdf";
            const objectUrl = URL.createObjectURL(blob);
            const link = document.createElement("a");
            link.href = objectUrl;
            link.download = fileName;
            document.body.appendChild(link);
            link.click();
            link.remove();
            window.setTimeout(() => URL.revokeObjectURL(objectUrl), 1000);
            setStatus(accountStatementStatus, "success", "PDF generado correctamente.");
        } catch (error) {
            setStatus(accountStatementStatus, "error", error instanceof Error ? error.message : "No fue posible generar el PDF.");
        } finally {
            setAccountStatementPdfLoading(false);
        }
    }

    function resetSiigoInvoicesTable(message) {
        state.siigoInvoicesDetail = null;
        const selectedCustomer = getSelectedSiigoCustomer();
        if (siigoCustomerReference) {
            siigoCustomerReference.textContent = selectedCustomer?.displayName || selectedCustomer?.name || "-";
        }

        if (siigoNitReference) {
            siigoNitReference.textContent = selectedCustomer?.identification || "-";
        }

        if (siigoInvoicesResultsCount) {
            siigoInvoicesResultsCount.textContent = "0";
        }

        if (siigoInvoicesTotalAmount) {
            siigoInvoicesTotalAmount.textContent = currencyFormatter.format(0);
        }

        if (siigoInvoicesSelectAll) {
            siigoInvoicesSelectAll.checked = false;
            siigoInvoicesSelectAll.indeterminate = false;
        }

        if (siigoInvoicesBody) {
            siigoInvoicesBody.innerHTML = `<tr><td colspan="8" class="dashboard-table__empty">${escapeHtml(message || "Busca un cliente y consulta sus facturas de Siigo.")}</td></tr>`;
        }

        syncSiigoInvoicesSelectionSummary();
    }

    function renderSiigoInvoicesTable(detail) {
        state.siigoInvoicesDetail = detail || null;
        const rows = Array.isArray(detail?.invoices) ? detail.invoices : [];

        if (siigoCustomerReference) {
            siigoCustomerReference.textContent = detail?.customerDisplayName || "-";
        }

        if (siigoNitReference) {
            siigoNitReference.textContent = detail?.customerIdentification || "-";
        }

        if (siigoPeriodReference) {
            siigoPeriodReference.textContent = detail?.periodLabel || "-";
        }

        if (siigoInvoicesResultsCount) {
            siigoInvoicesResultsCount.textContent = numberFormatter.format(Number(detail?.recordsCount || rows.length || 0));
        }

        if (siigoInvoicesTotalAmount) {
            siigoInvoicesTotalAmount.textContent = currencyFormatter.format(Number(detail?.totalAmount || 0));
        }

        if (siigoInvoicesSelectAll) {
            siigoInvoicesSelectAll.checked = false;
            siigoInvoicesSelectAll.indeterminate = false;
        }

        if (!siigoInvoicesBody) {
            return;
        }

        if (!rows.length) {
            siigoInvoicesBody.innerHTML = `<tr><td colspan="8" class="dashboard-table__empty">${escapeHtml(detail?.emptyStateTitle || "No encontramos facturas en Siigo.")}</td></tr>`;
            syncSiigoInvoicesSelectionSummary();
            return;
        }

        siigoInvoicesBody.innerHTML = rows.map(row => {
            const grossTotal = Number(row.grossTotal ?? row.total ?? 0);
            const grossBalance = Number(row.grossBalance ?? row.balance ?? 0);
            const status = row.annulled
                ? "Anulada"
                : (row.stampStatus || "Sin estado");
            const statusClass = row.annulled
                ? "dashboard-badge"
                : "dashboard-badge is-success";
            const mailStatus = row.mailStatus || "Sin estado";
            const mailStatusClass = String(row.mailStatus || "").toLowerCase() === "sent"
                ? "dashboard-badge is-success"
                : "dashboard-badge";

            return `
                <tr data-siigo-invoice-id="${escapeHtml(row.id || "")}">
                    <td>
                        <input type="checkbox"
                               class="form-check-input"
                               data-siigo-invoice-select
                               data-id="${escapeHtml(row.id || "")}"
                               data-name="${escapeHtml(row.name || "")}"
                               data-total="${escapeHtml(formatEditableDecimalValue(grossTotal))}" />
                    </td>
                    <td>${escapeHtml(row.name || row.prefix || "-")}</td>
                    <td>${escapeHtml(row.dateDisplay || row.dateValue || "Sin fecha")}</td>
                    <td>${escapeHtml(row.customerIdentification || detail?.customerIdentification || "-")}</td>
                    <td><span class="${statusClass}">${escapeHtml(status)}</span></td>
                    <td><span class="${mailStatusClass}">${escapeHtml(mailStatus)}</span></td>
                    <td class="text-end">${escapeHtml(currencyFormatter.format(grossTotal))}</td>
                    <td class="text-end">${escapeHtml(currencyFormatter.format(grossBalance))}</td>
                </tr>
            `;
        }).join("");

        syncSiigoInvoicesSelectionSummary();
    }

    function syncSiigoInvoicesSelectionSummary() {
        const checkboxes = siigoInvoicesBody
            ? Array.from(siigoInvoicesBody.querySelectorAll("[data-siigo-invoice-select]"))
            : [];
        const selected = checkboxes.filter(checkbox => checkbox.checked);

        if (siigoInvoicesSelectedCount) {
            siigoInvoicesSelectedCount.textContent = numberFormatter.format(selected.length);
        }

        if (siigoInvoicesSelectAll) {
            siigoInvoicesSelectAll.checked = checkboxes.length > 0 && selected.length === checkboxes.length;
            siigoInvoicesSelectAll.indeterminate = selected.length > 0 && selected.length < checkboxes.length;
        }

        if (siigoInvoicesDownloadButton) {
            siigoInvoicesDownloadButton.disabled = state.siigoInvoicesLoading
                || state.siigoInvoicesDownloading
                || selected.length === 0
                || !buildSiigoInvoicesDownloadUrl();
        }
    }

    function buildSiigoInvoicesDownloadPayload() {
        const selected = siigoInvoicesBody
            ? Array.from(siigoInvoicesBody.querySelectorAll("[data-siigo-invoice-select]:checked"))
            : [];

        if (!selected.length) {
            throw new Error("Selecciona al menos una factura de Siigo para descargar.");
        }

        return {
            invoices: selected.map(checkbox => ({
                id: checkbox.dataset.id || "",
                name: checkbox.dataset.name || ""
            }))
        };
    }

    async function downloadSiigoInvoices() {
        const url = buildSiigoInvoicesDownloadUrl();
        if (!url) {
            setStatus(siigoInvoicesStatus, "error", "No hay una URL configurada para descargar facturas desde Siigo.");
            return;
        }

        let payload;
        try {
            payload = buildSiigoInvoicesDownloadPayload();
        } catch (error) {
            setStatus(siigoInvoicesStatus, "error", error instanceof Error ? error.message : "Selecciona las facturas de Siigo.");
            return;
        }

        setSiigoInvoicesDownloading(true);
        setStatus(siigoInvoicesStatus, "info", "Preparando descarga desde Siigo...");

        try {
            const response = await fetch(url, {
                method: "POST",
                headers: {
                    Accept: "application/pdf, application/zip, application/octet-stream",
                    "Content-Type": "application/json"
                },
                body: JSON.stringify(payload)
            });

            if (!response.ok) {
                const message = await response.text();
                throw new Error(message || "No fue posible descargar las facturas desde Siigo.");
            }

            const blob = await response.blob();
            const fileName = resolveDownloadFileName(response.headers.get("content-disposition"))
                || "facturas-siigo.zip";
            const objectUrl = URL.createObjectURL(blob);
            const link = document.createElement("a");
            link.href = objectUrl;
            link.download = fileName;
            document.body.appendChild(link);
            link.click();
            link.remove();
            window.setTimeout(() => URL.revokeObjectURL(objectUrl), 1000);
            setStatus(siigoInvoicesStatus, "success", "Descarga generada correctamente.");
        } catch (error) {
            setStatus(siigoInvoicesStatus, "error", error instanceof Error ? error.message : "No fue posible descargar las facturas desde Siigo.");
        } finally {
            setSiigoInvoicesDownloading(false);
        }
    }

    function renderComparativeKpis(container, kpis, compareYear) {
        if (!container) {
            return;
        }

        const items = Array.isArray(kpis) ? kpis : [];
        const renderBreakdowns = breakdowns => {
            if (!Array.isArray(breakdowns) || !breakdowns.length) {
                return "";
            }

            return `
                <div class="dashboard-kpi__breakdowns">
                    ${breakdowns.map(item => `
                        <div class="dashboard-kpi__breakdown">
                            <div class="dashboard-kpi__breakdown-head">
                                <span class="dashboard-kpi__breakdown-label">${escapeHtml(item.label)}</span>
                                <span class="dashboard-kpi__breakdown-value">${escapeHtml(currencyFormatter.format(Number(item.value || 0)))} · ${escapeHtml(formatPercent(item.sharePercent || 0))}</span>
                            </div>
                            <div class="dashboard-kpi__breakdown-track">
                                <span class="dashboard-kpi__breakdown-fill" style="width:${Math.min(Number(item.sharePercent || 0), 100)}%"></span>
                            </div>
                        </div>
                    `).join("")}
                </div>
            `;
        };

        container.innerHTML = items.map(kpi => `
            <article class="dashboard-kpi dashboard-kpi--${escapeHtml(kpi.tone || "neutral")}">
                <div class="dashboard-kpi__header">
                    <span class="dashboard-kpi__label">${escapeHtml(kpi.label)}</span>
                    ${kpi.showComparison === false ? "" : `<span class="dashboard-kpi__delta">${escapeHtml(formatGrowth(kpi.growthPercent))}</span>`}
                </div>
                <strong class="dashboard-kpi__value">${escapeHtml(formatMetric(kpi.value, kpi.valueFormat))}</strong>
                <span class="dashboard-kpi__hint">${escapeHtml(kpi.hint)}</span>
                ${kpi.showComparison === false ? "" : `
                    <div class="dashboard-kpi__footer">
                        <span>${escapeHtml(String(compareYear || ""))}</span>
                        <strong>${escapeHtml(formatMetric(kpi.previousValue, kpi.valueFormat))}</strong>
                    </div>
                `}
                ${kpi.secondaryLabel || kpi.secondaryValue ? `
                    <div class="dashboard-kpi__secondary">
                        <span>${escapeHtml(kpi.secondaryLabel || "")}</span>
                        <strong>${escapeHtml(kpi.secondaryValue || "")}</strong>
                    </div>
                ` : ""}
                ${renderBreakdowns(kpi.breakdowns)}
            </article>
        `).join("");
    }

    function renderPortfolioKpis(dashboard) {
        renderSimpleKpis(portfolioKpisContainer, dashboard?.kpis);
    }

    function formatPortfolioCompactMoney(value) {
        const numericValue = Number(value || 0);
        const sign = numericValue < 0 ? "-" : "";
        const absoluteValue = Math.abs(numericValue);
        const formatUnit = scaledValue => {
            const rounded = Math.round(scaledValue * 10) / 10;
            const hasDecimal = Math.abs(rounded - Math.round(rounded)) > 0.001;
            return `${sign}${hasDecimal ? rounded.toFixed(1) : Math.round(rounded).toString()}`;
        };

        if (absoluteValue >= 1000000000) {
            return `${formatUnit(absoluteValue / 1000000000)}B`;
        }

        if (absoluteValue >= 1000000) {
            return `${formatUnit(absoluteValue / 1000000)}M`;
        }

        if (absoluteValue >= 1000) {
            return `${formatUnit(absoluteValue / 1000)}K`;
        }

        return `${sign}${numberFormatter.format(absoluteValue)}`;
    }

    function normalizePortfolioMonthKey(value) {
        const text = (value || "").toString().trim();
        let match = /^(\d{4})-(\d{2})/.exec(text);
        if (!match) {
            match = /^(\d{2})\/(\d{2})\/(\d{4})$/.exec(text);
            if (match) {
                return `${match[3]}-${match[2]}`;
            }
            return "";
        }

        const month = Number(match[2]);
        return month >= 1 && month <= 12 ? `${match[1]}-${match[2]}` : "";
    }

    function normalizeBusinessBillingMonthKey(value) {
        return normalizePortfolioMonthKey(value);
    }

    function getPortfolioInvoiceMonthKey(row) {
        return normalizePortfolioMonthKey(row?.emissionDateValue || row?.emissionDateDisplay || "");
    }

    function getPortfolioMonthOrder(monthKey) {
        const normalized = normalizePortfolioMonthKey(monthKey);
        if (!normalized) {
            return 0;
        }

        return Number(normalized.slice(0, 4)) * 12 + Number(normalized.slice(5, 7));
    }

    function addPortfolioMonths(monthKey, amount) {
        const normalized = normalizePortfolioMonthKey(monthKey);
        if (!normalized) {
            return "";
        }

        const year = Number(normalized.slice(0, 4));
        const monthIndex = Number(normalized.slice(5, 7)) - 1 + Number(amount || 0);
        const date = new Date(Date.UTC(year, monthIndex, 1));
        return `${date.getUTCFullYear()}-${String(date.getUTCMonth() + 1).padStart(2, "0")}`;
    }

    function formatPortfolioMonthLabel(monthKey, shortLabel = false) {
        const normalized = normalizePortfolioMonthKey(monthKey);
        if (!normalized) {
            return "Sin mes";
        }

        const year = normalized.slice(0, 4);
        const monthIndex = Number(normalized.slice(5, 7)) - 1;
        const label = monthLabels[monthIndex] || normalized;
        return shortLabel ? `${label.slice(0, 3)} ${year}` : `${label} ${year}`;
    }

    function isPortfolioPendingInvoice(row) {
        if (typeof row?.isPortfolioPending === "boolean") {
            return row.isPortfolioPending;
        }

        if (row?.isFullyCredited) {
            return false;
        }

        if ((row?.paymentDateValue || "").trim()) {
            return false;
        }

        return Number(row?.paymentValue || 0) <= 0;
    }

    function isPortfolioInvoiceOverdue(row) {
        if (typeof row?.isOverdue === "boolean") {
            return row.isOverdue;
        }

        return Number(row?.ageDays || 0) > 0 || normalizeText(row?.paymentStatusLabel || "").includes("vencid");
    }

    function getPortfolioInvoiceVerticalKey(row) {
        const optionValue = Number(row?.verticalOptionValue || 0);
        if (optionValue === 645250000) {
            return "cloud";
        }

        if (optionValue === 645250001) {
            return "copiers";
        }

        const label = normalizeText(row?.verticalLabel || "");
        if (label.includes("cloud")) {
            return "cloud";
        }

        if (label.includes("copiers")) {
            return "copiers";
        }

        return "other";
    }

    function getPortfolioMonthlyPendingRows(verticalKey = "") {
        return (Array.isArray(state.portfolioDashboard?.invoices) ? state.portfolioDashboard.invoices : [])
            .filter(isPortfolioPendingInvoice)
            .filter(row => !verticalKey || getPortfolioInvoiceVerticalKey(row) === verticalKey);
    }

    function buildPortfolioMonthlyYearOptions() {
        if (!portfolioMonthlyYearFilter) {
            return;
        }

        const years = new Set([currentYear, state.portfolioMonthlyYear, 2026]);
        getPortfolioMonthlyPendingRows().forEach(row => {
            const monthKey = getPortfolioInvoiceMonthKey(row);
            if (monthKey) {
                years.add(Number(monthKey.slice(0, 4)));
            }
        });

        const options = Array.from(years)
            .filter(year => Number.isFinite(year) && year >= 2000 && year <= 2100)
            .sort((left, right) => right - left);

        portfolioMonthlyYearFilter.innerHTML = options
            .map(year => `<option value="${year}">${year}</option>`)
            .join("");
        portfolioMonthlyYearFilter.value = String(state.portfolioMonthlyYear || currentYear);
    }

    function syncPortfolioMonthlyFilterControls() {
        const range = ["year", "all", "month", "custom"].includes(state.portfolioMonthlyRange)
            ? state.portfolioMonthlyRange
            : "year";

        state.portfolioMonthlyRange = range;

        if (portfolioMonthlyRangeFilter) {
            portfolioMonthlyRangeFilter.value = range;
        }

        if (portfolioMonthlyMonthFilter) {
            portfolioMonthlyMonthFilter.value = normalizePortfolioMonthKey(state.portfolioMonthlyMonth) || `${currentYear}-${String(currentMonth).padStart(2, "0")}`;
        }

        if (portfolioMonthlyStartFilter) {
            portfolioMonthlyStartFilter.value = normalizePortfolioMonthKey(state.portfolioMonthlyStart) || `${currentYear}-01`;
        }

        if (portfolioMonthlyEndFilter) {
            portfolioMonthlyEndFilter.value = normalizePortfolioMonthKey(state.portfolioMonthlyEnd) || `${currentYear}-12`;
        }

        portfolioMonthlyFilterFields.forEach(field => {
            const fieldKey = field.dataset.portfolioMonthlyFilterField || "";
            field.hidden = (range === "all")
                || (range === "year" && fieldKey !== "year")
                || (range === "month" && fieldKey !== "month")
                || (range === "custom" && fieldKey !== "custom");
        });
    }

    function getPortfolioMonthlyBounds() {
        const range = state.portfolioMonthlyRange || "year";

        if (range === "all") {
            return { kind: "all", start: "", end: "" };
        }

        if (range === "month") {
            const monthKey = normalizePortfolioMonthKey(state.portfolioMonthlyMonth) || `${currentYear}-${String(currentMonth).padStart(2, "0")}`;
            return { kind: "month", start: monthKey, end: monthKey };
        }

        if (range === "custom") {
            let start = normalizePortfolioMonthKey(state.portfolioMonthlyStart) || `${currentYear}-01`;
            let end = normalizePortfolioMonthKey(state.portfolioMonthlyEnd) || start;
            if (getPortfolioMonthOrder(start) > getPortfolioMonthOrder(end)) {
                [start, end] = [end, start];
            }

            return { kind: "custom", start, end };
        }

        const year = Number(state.portfolioMonthlyYear || currentYear);
        return { kind: "year", start: `${year}-01`, end: `${year}-12` };
    }

    function isPortfolioMonthInsideBounds(monthKey, bounds) {
        if (!monthKey) {
            return false;
        }

        if (!bounds?.start || !bounds?.end) {
            return true;
        }

        const order = getPortfolioMonthOrder(monthKey);
        return order >= getPortfolioMonthOrder(bounds.start) && order <= getPortfolioMonthOrder(bounds.end);
    }

    function enumeratePortfolioMonthRange(bounds) {
        if (!bounds?.start || !bounds?.end || bounds.kind === "all") {
            return [];
        }

        const months = [];
        let cursor = bounds.start;
        while (cursor && getPortfolioMonthOrder(cursor) <= getPortfolioMonthOrder(bounds.end) && months.length <= 72) {
            months.push(cursor);
            cursor = addPortfolioMonths(cursor, 1);
        }

        return months.length <= 72 ? months : [];
    }

    function getPortfolioMonthlyFilterLabel(bounds) {
        if (bounds?.kind === "all") {
            return "toda la data";
        }

        if (bounds?.kind === "month") {
            return formatPortfolioMonthLabel(bounds.start).toLowerCase();
        }

        if (bounds?.kind === "custom") {
            return `${formatPortfolioMonthLabel(bounds.start)} - ${formatPortfolioMonthLabel(bounds.end)}`.toLowerCase();
        }

        return `año ${Number(state.portfolioMonthlyYear || currentYear)}`;
    }

    function buildPortfolioMonthlyChartData(verticalKey = "") {
        const bounds = getPortfolioMonthlyBounds();
        const pendingRows = getPortfolioMonthlyPendingRows(verticalKey);
        const monthMap = new Map();
        let noEmissionDateCount = 0;

        pendingRows.forEach(row => {
            const monthKey = getPortfolioInvoiceMonthKey(row);
            if (!monthKey) {
                noEmissionDateCount++;
                return;
            }

            if (!isPortfolioMonthInsideBounds(monthKey, bounds)) {
                return;
            }

            if (!monthMap.has(monthKey)) {
                monthMap.set(monthKey, {
                    key: monthKey,
                    label: formatPortfolioMonthLabel(monthKey, true),
                    total: 0,
                    overdueTotal: 0,
                    currentTotal: 0,
                    count: 0,
                    overdueCount: 0,
                    currentCount: 0,
                    invoices: [],
                    overdueInvoices: [],
                    currentInvoices: []
                });
            }

            const bucket = monthMap.get(monthKey);
            const invoiceValue = getNetInvoiceTotal(row);
            bucket.total += invoiceValue;
            bucket.count += 1;
            bucket.invoices.push(row);

            if (isPortfolioInvoiceOverdue(row)) {
                bucket.overdueTotal += invoiceValue;
                bucket.overdueCount += 1;
                bucket.overdueInvoices.push(row);
            } else {
                bucket.currentTotal += invoiceValue;
                bucket.currentCount += 1;
                bucket.currentInvoices.push(row);
            }
        });

        const rangeMonths = enumeratePortfolioMonthRange(bounds);
        const monthKeys = rangeMonths.length
            ? rangeMonths
            : Array.from(monthMap.keys()).sort((left, right) => getPortfolioMonthOrder(left) - getPortfolioMonthOrder(right));

        const rows = monthKeys.map(monthKey => monthMap.get(monthKey) || {
            key: monthKey,
            label: formatPortfolioMonthLabel(monthKey, true),
            total: 0,
            overdueTotal: 0,
            currentTotal: 0,
            count: 0,
            overdueCount: 0,
            currentCount: 0,
            invoices: [],
            overdueInvoices: [],
            currentInvoices: []
        });

        const total = rows.reduce((sum, row) => sum + row.total, 0);
        const overdueTotal = rows.reduce((sum, row) => sum + row.overdueTotal, 0);
        const currentTotal = rows.reduce((sum, row) => sum + row.currentTotal, 0);
        const count = rows.reduce((sum, row) => sum + row.count, 0);
        const overdueCount = rows.reduce((sum, row) => sum + row.overdueCount, 0);
        const currentCount = rows.reduce((sum, row) => sum + row.currentCount, 0);

        return {
            bounds,
            rows,
            rangeLabel: getPortfolioMonthlyFilterLabel(bounds),
            total,
            overdueTotal,
            currentTotal,
            count,
            overdueCount,
            currentCount,
            noEmissionDateCount
        };
    }

    function renderPortfolioMonthlyLegendItem(tone, label, value, count) {
        return `
            <span class="dashboard-portfolio-monthly-legend__item">
                <i class="dashboard-portfolio-monthly-legend__swatch dashboard-portfolio-monthly-legend__swatch--${escapeHtml(tone)}"></i>
                <span>${escapeHtml(label)}</span>
                <strong>${escapeHtml(formatPortfolioCompactMoney(value))}</strong>
                <small>${escapeHtml(numberFormatter.format(count))} fact.</small>
            </span>
        `;
    }

    function getPortfolioMonthlyRowsForMonth(monthKey, verticalKey = "") {
        const normalizedMonthKey = normalizePortfolioMonthKey(monthKey);
        if (!normalizedMonthKey) {
            return [];
        }

        return getPortfolioMonthlyPendingRows(verticalKey)
            .filter(row => getPortfolioInvoiceMonthKey(row) === normalizedMonthKey)
            .sort((left, right) => {
                const leftOverdue = isPortfolioInvoiceOverdue(left);
                const rightOverdue = isPortfolioInvoiceOverdue(right);
                if (leftOverdue !== rightOverdue) {
                    return leftOverdue ? -1 : 1;
                }

                if (leftOverdue && rightOverdue) {
                    const ageComparison = Number(right.ageDays || 0) - Number(left.ageDays || 0);
                    if (ageComparison !== 0) {
                        return ageComparison;
                    }
                }

                const leftDue = left.dueDateValue || "9999-12-31";
                const rightDue = right.dueDateValue || "9999-12-31";
                const dueComparison = leftDue.localeCompare(rightDue, "es", { numeric: true, sensitivity: "base" });
                if (dueComparison !== 0) {
                    return dueComparison;
                }

                return (left.clientName || "").localeCompare(right.clientName || "", "es", { numeric: true, sensitivity: "base" });
            });
    }

    function renderPortfolioMonthlyDetailRows(rows, emptyMessage) {
        const items = Array.isArray(rows) ? rows : [];
        if (!items.length) {
            return `<tr><td colspan="8" class="dashboard-table__empty">${escapeHtml(emptyMessage)}</td></tr>`;
        }

        return items.map(row => {
            const invoiceUrl = (row.publicUrl || "").trim();
            const link = invoiceUrl
                ? `<a href="${escapeHtml(invoiceUrl)}" target="_blank" rel="noopener noreferrer" class="dashboard-table-link">Abrir</a>`
                : "-";
            const isOverdue = isPortfolioInvoiceOverdue(row);
            const statusLabel = isOverdue ? "Vencida" : "Sin vencer";
            const statusClass = isOverdue
                ? "dashboard-badge dashboard-badge--overdue"
                : "dashboard-badge is-info";
            const daysLabel = isOverdue
                ? `${numberFormatter.format(Number(row.ageDays || 0))} dias vencida`
                : "Sin vencer";

            return `
                <tr>
                    <td>${renderPortfolioText(row.invoiceNumber)}</td>
                    <td>${renderPortfolioText(row.clientName)}</td>
                    <td>${renderPortfolioText(row.emissionDateDisplay || "Sin fecha")}</td>
                    <td>${renderPortfolioText(row.dueDateDisplay || "Sin fecha")}</td>
                    <td><span class="${statusClass}">${escapeHtml(statusLabel)}</span></td>
                    <td class="text-end">${escapeHtml(daysLabel)}</td>
                    <td class="text-end">${renderPortfolioCurrency(getNetInvoiceTotal(row))}</td>
                    <td>${link}</td>
                </tr>
            `;
        }).join("");
    }

    function isPortfolioMonthlyDetailOpen() {
        return Boolean(portfolioMonthlyDetailModal && !portfolioMonthlyDetailModal.hidden);
    }

    function closePortfolioMonthlyDetailModal() {
        if (!portfolioMonthlyDetailModal) {
            return;
        }

        portfolioMonthlyDetailModal.hidden = true;
        document.body.classList.remove("dashboard-modal-open");
    }

    function openPortfolioMonthlyDetailModal(monthKey, verticalKey = "") {
        const normalizedMonthKey = normalizePortfolioMonthKey(monthKey);
        const rows = getPortfolioMonthlyRowsForMonth(normalizedMonthKey, verticalKey);
        if (!portfolioMonthlyDetailModal || !normalizedMonthKey || !rows.length) {
            return;
        }

        const overdueRows = rows.filter(isPortfolioInvoiceOverdue);
        const currentRows = rows.filter(row => !isPortfolioInvoiceOverdue(row));
        const overdueTotal = overdueRows.reduce((sum, row) => sum + getNetInvoiceTotal(row), 0);
        const currentTotal = currentRows.reduce((sum, row) => sum + getNetInvoiceTotal(row), 0);
        const total = overdueTotal + currentTotal;
        const monthLabel = formatPortfolioMonthLabel(normalizedMonthKey);
        const verticalLabel = portfolioMonthlyChartConfigs.find(item => item.key === verticalKey)?.label || "Cartera";

        portfolioMonthlyDetailTitle && (portfolioMonthlyDetailTitle.textContent = `Facturas ${verticalLabel} de ${monthLabel}`);
        portfolioMonthlyDetailSubtitle && (portfolioMonthlyDetailSubtitle.textContent = "Facturas emitidas sin pago ordenadas de la mas vencida a la menos vencida.");
        portfolioMonthlyDetailTotal && (portfolioMonthlyDetailTotal.textContent = currencyFormatter.format(total));

        if (portfolioMonthlyDetailSummary) {
            portfolioMonthlyDetailSummary.innerHTML = `
                <span>
                    <strong>${escapeHtml(formatPortfolioCompactMoney(total))}</strong>
                    <small>Total pendiente</small>
                </span>
                <span>
                    <strong>${escapeHtml(numberFormatter.format(rows.length))}</strong>
                    <small>Facturas</small>
                </span>
                <span>
                    <strong>${escapeHtml(formatPortfolioCompactMoney(overdueTotal))}</strong>
                    <small>${escapeHtml(numberFormatter.format(overdueRows.length))} vencidas</small>
                </span>
                <span>
                    <strong>${escapeHtml(formatPortfolioCompactMoney(currentTotal))}</strong>
                    <small>${escapeHtml(numberFormatter.format(currentRows.length))} sin vencer</small>
                </span>
            `;
        }

        if (portfolioMonthlyDetailBody) {
            portfolioMonthlyDetailBody.innerHTML = renderPortfolioMonthlyDetailRows(rows, "No hay facturas pendientes en este mes.");
        }

        document.body.classList.add("dashboard-modal-open");
        portfolioMonthlyDetailModal.hidden = false;
        window.setTimeout(() => portfolioMonthlyDetailCloseButton?.focus(), 30);
    }

    function renderPortfolioMonthlyChart(config) {
        const data = buildPortfolioMonthlyChartData(config.key);
        if (config.summary) {
            const noDateNote = data.noEmissionDateCount > 0
                ? ` ${numberFormatter.format(data.noEmissionDateCount)} factura(s) ${config.label} pendiente(s) sin fecha de emision no se grafican.`
                : "";
            config.summary.innerHTML = data.count > 0
                ? `
                    <span>${escapeHtml(formatPortfolioCompactMoney(data.total))} ${escapeHtml(config.label)} pendiente por ingresar en ${escapeHtml(data.rangeLabel)}.</span>
                    <strong>${escapeHtml(currencyFormatter.format(data.total))}</strong>
                    <small>${escapeHtml(numberFormatter.format(data.count))} facturas sin pago: ${escapeHtml(numberFormatter.format(data.overdueCount))} vencidas y ${escapeHtml(numberFormatter.format(data.currentCount))} sin vencer.${escapeHtml(noDateNote)}</small>
                `
                : `<span>No hay facturas ${escapeHtml(config.label)} emitidas sin pago para ${escapeHtml(data.rangeLabel)}.${escapeHtml(noDateNote)}</span>`;
        }

        if (config.legend) {
            config.legend.innerHTML = [
                renderPortfolioMonthlyLegendItem("overdue", "Vencidas", data.overdueTotal, data.overdueCount),
                renderPortfolioMonthlyLegendItem("current", "Sin vencer", data.currentTotal, data.currentCount)
            ].join("");
        }

        if (!config.chart) {
            return;
        }

        if (data.total <= 0 || data.rows.length === 0) {
            config.chart.innerHTML = `<div class="dashboard-portfolio-monthly-empty">No hay cartera ${escapeHtml(config.label)} pendiente para este filtro.</div>`;
            return;
        }

        const maxTotal = Math.max(...data.rows.map(row => row.total), 1);
        config.chart.innerHTML = `
            <div class="dashboard-portfolio-monthly-chart__plot" style="--portfolio-month-count:${data.rows.length}">
                ${data.rows.map(row => {
                    const barHeight = row.total > 0 ? Math.max(7, (row.total / maxTotal) * 100) : 0;
                    const overdueShare = row.total > 0 ? (row.overdueTotal / row.total) * 100 : 0;
                    const currentShare = row.total > 0 ? (row.currentTotal / row.total) * 100 : 0;
                    const overdueSegment = row.overdueTotal > 0
                        ? `<span class="dashboard-portfolio-monthly-chart__segment dashboard-portfolio-monthly-chart__segment--overdue" style="height:${overdueShare}%"></span>`
                        : "";
                    const currentSegment = row.currentTotal > 0
                        ? `<span class="dashboard-portfolio-monthly-chart__segment dashboard-portfolio-monthly-chart__segment--current" style="height:${currentShare}%"></span>`
                        : "";
                    const tooltipTitle = formatPortfolioMonthLabel(row.key);
                    const barClass = row.count > 0
                        ? "dashboard-portfolio-monthly-chart__bar is-clickable"
                        : "dashboard-portfolio-monthly-chart__bar is-empty";

                    return `
                        <article class="${barClass}"
                                 tabindex="${row.count > 0 ? "0" : "-1"}"
                                 role="${row.count > 0 ? "button" : "img"}"
                                 data-portfolio-month-key="${escapeHtml(row.key)}"
                                 data-portfolio-vertical-key="${escapeHtml(config.key)}"
                                 aria-label="${escapeHtml(`${config.label} ${tooltipTitle}: ${currencyFormatter.format(row.total)} pendiente por ingresar. Click para ver facturas.`)}">
                            <strong class="dashboard-portfolio-monthly-chart__total">${escapeHtml(formatPortfolioCompactMoney(row.total))}</strong>
                            <div class="dashboard-portfolio-monthly-chart__scale">
                                <div class="dashboard-portfolio-monthly-chart__stack" style="height:${barHeight}%">
                                    ${overdueSegment}
                                    ${currentSegment}
                                </div>
                            </div>
                            <span class="dashboard-portfolio-monthly-chart__label">${escapeHtml(row.label)}</span>
                            <small>${escapeHtml(numberFormatter.format(row.count))} fact.</small>
                            <div class="dashboard-portfolio-monthly-tooltip" role="tooltip">
                                <strong>${escapeHtml(config.label)} - ${escapeHtml(tooltipTitle)}</strong>
                                <span>Total: ${escapeHtml(currencyFormatter.format(row.total))}</span>
                                <span>Vencidas: ${escapeHtml(currencyFormatter.format(row.overdueTotal))} (${escapeHtml(numberFormatter.format(row.overdueCount))})</span>
                                <span>Sin vencer: ${escapeHtml(currencyFormatter.format(row.currentTotal))} (${escapeHtml(numberFormatter.format(row.currentCount))})</span>
                            </div>
                        </article>
                    `;
                }).join("")}
            </div>
        `;
    }

    function renderPortfolioMonthlyDashboard() {
        buildPortfolioMonthlyYearOptions();
        syncPortfolioMonthlyFilterControls();
        portfolioMonthlyChartConfigs.forEach(renderPortfolioMonthlyChart);
    }

    function renderSimpleKpis(container, kpis) {
        const items = Array.isArray(kpis) ? kpis : [];
        if (!container) {
            return;
        }

        container.innerHTML = items.map(kpi => `
            <article class="dashboard-kpi dashboard-kpi--neutral">
                <div class="dashboard-kpi__header">
                    <span class="dashboard-kpi__label">${escapeHtml(kpi.label)}</span>
                </div>
                <strong class="dashboard-kpi__value">${escapeHtml(formatMetric(kpi.value, kpi.valueFormat))}</strong>
                <span class="dashboard-kpi__hint">${escapeHtml(kpi.hint)}</span>
                <div class="dashboard-kpi__alert">
                    <span class="dashboard-kpi__alert-label">${escapeHtml(kpi.secondaryLabel || "")}</span>
                    <strong class="dashboard-kpi__alert-value">${escapeHtml(kpi.secondaryValue || "")}</strong>
                </div>
            </article>
        `).join("");
    }

    function renderBusinessKpis(dashboard) {
        const kpis = Array.isArray(dashboard?.kpis) ? dashboard.kpis : [];
        if (!businessKpisContainer) {
            return;
        }

        businessKpisContainer.innerHTML = kpis.length
            ? kpis.map(kpi => `
                <article class="business-summary-card">
                    <span class="business-summary-card__label">${escapeHtml(kpi.label || "")}</span>
                    <strong class="business-summary-card__value">${escapeHtml(formatBusinessMetric(kpi.value, kpi.valueFormat))}</strong>
                    <span class="business-summary-card__hint">${escapeHtml(kpi.hint || "")}</span>
                    ${kpi.secondaryLabel || kpi.secondaryValue ? `
                        <span class="business-summary-card__secondary">
                            <span>${escapeHtml(kpi.secondaryLabel || "")}</span>
                            <strong>${escapeHtml(kpi.secondaryValue || "")}</strong>
                        </span>
                    ` : ""}
                </article>
            `).join("")
            : '<div class="business-empty">Sin indicadores disponibles.</div>';
    }

    function renderBusinessProjectionKpis(dashboard) {
        const projection = dashboard?.projection || {};
        const kpis = Array.isArray(projection?.kpis) ? projection.kpis : [];
        if (!businessProjectionKpisContainer) {
            return;
        }

        businessProjectionKpisContainer.innerHTML = kpis.length
            ? kpis.map(kpi => `
                <article class="business-summary-card business-summary-card--projection business-summary-card--${escapeHtml(kpi.key || "default")}">
                    <span class="business-summary-card__label">${escapeHtml(kpi.label || "")}</span>
                    <strong class="business-summary-card__value">${escapeHtml(formatBusinessMetric(kpi.value, kpi.valueFormat))}</strong>
                    <span class="business-summary-card__hint">${escapeHtml(kpi.hint || projection?.dateRangeLabel || "")}</span>
                    ${kpi.secondaryLabel || kpi.secondaryValue ? `
                        <span class="business-summary-card__secondary">
                            <span>${escapeHtml(kpi.secondaryLabel || "")}</span>
                            <strong>${escapeHtml(kpi.secondaryValue || "")}</strong>
                        </span>
                    ` : ""}
                </article>
            `).join("")
            : '<div class="business-empty">Sin indicadores de proyeccion disponibles.</div>';
    }

    function renderBusinessProjectionHistory(dashboard) {
        const projection = dashboard?.projection || {};
        const rows = Array.isArray(projection?.monthlyRows) ? projection.monthlyRows : [];
        if (businessProjectionHistoryMeta) {
            businessProjectionHistoryMeta.textContent = rows.length
                ? `${projection.historyPeriodLabel || "Desde 2026"} · ${numberFormatter.format(rows.length)} mes(es)`
                : projection.historyPeriodLabel || "Desde 2026";
        }

        if (!businessProjectionHistoryBody) {
            return;
        }

        if (!rows.length) {
            businessProjectionHistoryBody.innerHTML = '<tr><td colspan="6" class="dashboard-table__empty">Sin datos mensuales desde 2026.</td></tr>';
            return;
        }

        const formatProjectionPercent = value => value === null || value === undefined
            ? "Sin margen"
            : formatPercent(value);
        const formatCount = (label, value) => `${label} ${numberFormatter.format(Number(value || 0))}`;

        businessProjectionHistoryBody.innerHTML = rows.map(row => `
            <tr>
                <td>
                    <strong>${escapeHtml(row.monthYearLabel || row.key || "")}</strong>
                </td>
                <td class="text-end">
                    <strong>${escapeHtml(formatBusinessMetric(row.realMonthlyBillingCop, "currency"))}</strong>
                    <small>${escapeHtml(formatCount("Facturas", row.billingRecordsCount))}</small>
                </td>
                <td class="text-end">
                    <strong>${escapeHtml(formatBusinessMetric(row.currentCostsCop, "currency"))}</strong>
                    <small>${escapeHtml(formatCount("Cruces", row.costRecordsCount))}</small>
                </td>
                <td class="text-end ${Number(row.projectedMonthlyUtilityCop || 0) < 0 ? "is-negative" : "is-positive"}">
                    <strong>${escapeHtml(formatBusinessMetric(row.projectedMonthlyUtilityCop, "currency"))}</strong>
                    <small>${escapeHtml(formatProjectionPercent(row.projectedMonthlyUtilityPercent))}</small>
                </td>
                <td class="text-end">
                    <strong>${escapeHtml(formatBusinessMetric(row.payrollCop, "currency"))}</strong>
                    <small>${escapeHtml(formatCount("Registros", row.payrollRecordsCount))}</small>
                </td>
                <td class="text-end ${Number(row.projectedNetUtilityCop || 0) < 0 ? "is-negative" : "is-positive"}">
                    <strong>${escapeHtml(formatBusinessMetric(row.projectedNetUtilityCop, "currency"))}</strong>
                    <small>${escapeHtml(formatProjectionPercent(row.projectedNetUtilityPercent))}</small>
                </td>
            </tr>
        `).join("");
    }

    function renderBusinessLinesChart(dashboard) {
        const rows = Array.isArray(dashboard?.lineSummaries) ? dashboard.lineSummaries : [];
        const maxValue = Math.max(1, ...rows.map(row => Number(row.annualValueUsd || 0)));

        if (businessLineMeta) {
            businessLineMeta.textContent = `${numberFormatter.format(rows.length)} linea(s)`;
        }

        if (!businessLinesChart) {
            return;
        }

        businessLinesChart.innerHTML = rows.length
            ? rows.map((row, index) => {
                const annual = Number(row.annualValueUsd || 0);
                const width = Math.max(3, (annual / maxValue) * 100);
                const colorClass = `business-line-chart__bar--${(index % 5) + 1}`;

                return `
                    <div class="business-line-chart__row">
                        <div class="business-line-chart__label">
                            <strong>${escapeHtml(row.label || "Sin linea")}</strong>
                            <span>${escapeHtml(numberFormatter.format(Number(row.recordsCount || 0)))} filas · ${escapeHtml(numberFormatter.format(Number(row.clientsCount || 0)))} clientes</span>
                        </div>
                        <div class="business-line-chart__track" aria-hidden="true">
                            <span class="business-line-chart__bar ${colorClass}" style="width:${width}%"></span>
                        </div>
                        <div class="business-line-chart__value">
                            <strong>${escapeHtml(formatUsd(annual))}</strong>
                            <span>${escapeHtml(formatPercent(row.sharePercent || 0))}</span>
                        </div>
                    </div>
                `;
            }).join("")
            : '<div class="business-empty">No hay lineas para graficar.</div>';
    }

    function renderBusinessContractTypes(dashboard) {
        const rows = Array.isArray(dashboard?.contractTypes) ? dashboard.contractTypes : [];
        if (!businessContractTypesChart) {
            return;
        }

        if (!rows.length) {
            businessContractTypesChart.innerHTML = '<div class="business-empty">Sin tipos de contrato.</div>';
            return;
        }

        const colors = ["#0f766e", "#2563eb", "#b45309", "#7c3aed", "#db2777"];
        let cursor = 0;
        const totalShare = rows.reduce((sum, row) => sum + Math.max(Number(row.sharePercent || 0), 0), 0);
        const segments = totalShare <= 0
            ? "#e2e8f0 0% 100%"
            : rows.map((row, index) => {
                const start = cursor;
                const share = Math.max(Number(row.sharePercent || 0), 0);
                cursor = Math.min(100, cursor + share);
                return `${colors[index % colors.length]} ${start}% ${cursor}%`;
            }).join(", ");
        const dominant = rows[0];

        businessContractTypesChart.innerHTML = `
            <div class="business-contract-mix__donut" style="background: conic-gradient(${segments});">
                <span>
                    <strong>${escapeHtml(formatPercent(dominant?.sharePercent || 0))}</strong>
                    <small>${escapeHtml(dominant?.label || "")}</small>
                </span>
            </div>
            <div class="business-contract-mix__legend">
                ${rows.map((row, index) => `
                    <div class="business-contract-mix__item">
                        <span class="business-contract-mix__swatch" style="background:${colors[index % colors.length]}"></span>
                        <span>${escapeHtml(row.label || "Sin contrato")}</span>
                        <strong>${escapeHtml(formatUsd(row.annualValueUsd || 0))}</strong>
                    </div>
                `).join("")}
            </div>
        `;
    }

    function renderBusinessContracts(dashboard) {
        const rows = Array.isArray(dashboard?.topContracts) ? dashboard.topContracts : [];
        const maxValue = Math.max(1, ...rows.map(row => Number(row.annualValueUsd || 0)));

        if (businessContractsCount) {
            businessContractsCount.textContent = `${numberFormatter.format(rows.length)} contrato(s)`;
        }

        if (!businessContractsList) {
            return;
        }

        businessContractsList.innerHTML = rows.length
            ? rows.map((row, index) => {
                const annual = Number(row.annualValueUsd || 0);
                const width = Math.max(3, (annual / maxValue) * 100);

                return `
                    <div class="business-contract-row">
                        <span class="business-contract-row__rank">${String(index + 1).padStart(2, "0")}</span>
                        <div class="business-contract-row__main">
                            <div class="business-contract-row__title">
                                <strong>${escapeHtml(row.clientName || "Sin cliente")}</strong>
                                <span>${escapeHtml(formatPercent(row.sharePercent || 0))}</span>
                            </div>
                            <small>${escapeHtml(numberFormatter.format(Number(row.recordsCount || 0)))} filas · ${escapeHtml(numberFormatter.format(Number(row.productsCount || 0)))} productos · ${escapeHtml(row.topProductName || "Sin producto dominante")}</small>
                            <div class="business-contract-row__meter" aria-hidden="true">
                                <span style="width:${width}%"></span>
                            </div>
                        </div>
                        <div class="business-contract-row__amount">
                            <strong>${escapeHtml(formatUsd(annual))}</strong>
                            <span>${escapeHtml(formatUsd(row.monthlyBillingUsd || 0))}/mes</span>
                        </div>
                    </div>
                `;
            }).join("")
            : '<div class="business-empty">No hay contratos para mostrar.</div>';
    }

    function renderBusinessProducts(dashboard) {
        const rows = Array.isArray(dashboard?.topProducts) ? dashboard.topProducts : [];
        const maxQuantity = Math.max(1, ...rows.map(row => Number(row.quantity || 0)));

        if (!businessProductsChart) {
            return;
        }

        businessProductsChart.innerHTML = rows.length
            ? rows.map((row, index) => {
                const quantity = Number(row.quantity || 0);
                const width = Math.max(3, (quantity / maxQuantity) * 100);

                return `
                    <div class="business-product-row">
                        <div class="business-product-row__head">
                            <span>${escapeHtml(String(index + 1).padStart(2, "0"))}</span>
                            <strong>${escapeHtml(row.productName || "Producto sin nombre")}</strong>
                        </div>
                        <div class="business-product-row__bar" aria-hidden="true">
                            <span style="width:${width}%"></span>
                        </div>
                        <div class="business-product-row__meta">
                            <span>${escapeHtml(numberFormatter.format(quantity))} unidades</span>
                            <strong>${escapeHtml(formatUsd(row.annualValueUsd || 0))}</strong>
                        </div>
                    </div>
                `;
            }).join("")
            : '<div class="business-empty">No hay productos para mostrar.</div>';
    }

    function renderBusinessDashboard(dashboard) {
        renderBusinessKpis(dashboard);
        renderBusinessProjectionKpis(dashboard);
        renderBusinessProjectionHistory(dashboard);
        renderBusinessLinesChart(dashboard);
        renderBusinessContractTypes(dashboard);
        renderBusinessContracts(dashboard);
        renderBusinessProducts(dashboard);
    }

    function formatBusinessBillingMillions(value) {
        const millions = Number(value || 0) / 1000000;
        return `${millions.toLocaleString("es-CO", { minimumFractionDigits: 1, maximumFractionDigits: 1 })}M`;
    }

    function formatBusinessBillingGrowth(value) {
        return value === null || value === undefined || value === ""
            ? "N/A"
            : `${Number(value || 0) > 0 ? "+" : ""}${formatPercent(value)}`;
    }

    function resolveBusinessBillingGrowthTone(value) {
        if (value === null || value === undefined || value === "") {
            return "neutral";
        }

        const numericValue = Number(value || 0);
        return numericValue > 0 ? "positive" : numericValue < 0 ? "negative" : "neutral";
    }

    function resolveBusinessBillingPreviousGrowthLabel() {
        switch (state.businessBillingGranularity || "month") {
            case "quarter":
                return "Trim. ant.";
            case "semester":
                return "Sem. ant.";
            case "year":
                return "Año ant.";
            case "all":
                return "Periodo ant.";
            default:
                return "Mes ant.";
        }
    }

    function renderBusinessBillingChart(container, chart, accentClass) {
        if (!container) {
            return;
        }

        const points = Array.isArray(chart?.points) ? chart.points : [];
        if (!points.length) {
            container.innerHTML = '<div class="business-billing-empty">Sin ventas para este filtro.</div>';
            return;
        }

        const width = Math.max(760, points.length * 96);
        const height = 360;
        const padding = { top: 82, right: 24, bottom: 48, left: 62 };
        const plotWidth = width - padding.left - padding.right;
        const plotHeight = height - padding.top - padding.bottom;
        const values = points.map(point => Number(point?.sales || 0));
        const minValue = Math.min(0, ...values);
        const maxValue = Math.max(0, ...values);
        const valueRange = Math.max(1, maxValue - minValue);
        const step = plotWidth / Math.max(points.length, 1);
        const barWidth = Math.min(48, Math.max(24, step * 0.58));
        const yForValue = value => padding.top + (((maxValue - Number(value || 0)) / valueRange) * plotHeight);
        const baselineY = yForValue(0);
        const gridValues = Array.from(new Set([minValue, 0, maxValue]))
            .sort((left, right) => left - right);
        const previousGrowthLabel = resolveBusinessBillingPreviousGrowthLabel();
        const previousYearGrowthLabel = "Año ant.";

        const gridLines = gridValues.map(value => {
            const y = yForValue(value);
            return `
                <line class="business-billing-chart__grid" x1="${padding.left}" y1="${y.toFixed(2)}" x2="${width - padding.right}" y2="${y.toFixed(2)}"></line>
                <text class="business-billing-chart__axis" x="${padding.left - 10}" y="${(y + 4).toFixed(2)}" text-anchor="end">${escapeHtml(formatBusinessBillingMillions(value))}</text>
            `;
        }).join("");

        const bars = points.map((point, index) => {
            const sales = Number(point?.sales || 0);
            const x = padding.left + (index * step) + ((step - barWidth) / 2);
            const valueY = yForValue(sales);
            const y = sales >= 0 ? valueY : baselineY;
            const barHeight = Math.max(2, Math.abs(baselineY - valueY));
            const labelX = x + (barWidth / 2);
            const labelBaseY = sales >= 0
                ? Math.max(22, y - 44)
                : Math.min(height - padding.bottom - 34, y + barHeight + 14);
            const previousGrowth = point?.previousPeriodGrowthPercent;
            const previousYearGrowth = point?.samePeriodPreviousYearGrowthPercent;
            const previousTone = resolveBusinessBillingGrowthTone(previousGrowth);
            const previousYearTone = resolveBusinessBillingGrowthTone(previousYearGrowth);
            const title = `${point?.label || ""}: ${currencyFormatter.format(sales)} · ${previousGrowthLabel} ${formatBusinessBillingGrowth(previousGrowth)} · ${previousYearGrowthLabel} ${formatBusinessBillingGrowth(previousYearGrowth)}`;

            return `
                <g class="business-billing-chart__bar-group">
                    <title>${escapeHtml(title)}</title>
                    <rect class="business-billing-chart__bar ${escapeHtml(accentClass || "")} ${sales < 0 ? "is-negative" : ""}" x="${x.toFixed(2)}" y="${y.toFixed(2)}" width="${barWidth.toFixed(2)}" height="${barHeight.toFixed(2)}" rx="5"></rect>
                    <text class="business-billing-chart__total-label" x="${labelX.toFixed(2)}" y="${labelBaseY.toFixed(2)}" text-anchor="middle">${escapeHtml(formatBusinessBillingMillions(sales))}</text>
                    <text class="business-billing-chart__growth-label is-${previousTone}" x="${labelX.toFixed(2)}" y="${(labelBaseY + 15).toFixed(2)}" text-anchor="middle">${escapeHtml(previousGrowthLabel)} ${escapeHtml(formatBusinessBillingGrowth(previousGrowth))}</text>
                    <text class="business-billing-chart__growth-label is-${previousYearTone}" x="${labelX.toFixed(2)}" y="${(labelBaseY + 30).toFixed(2)}" text-anchor="middle">${escapeHtml(previousYearGrowthLabel)} ${escapeHtml(formatBusinessBillingGrowth(previousYearGrowth))}</text>
                    <text class="business-billing-chart__month" x="${labelX.toFixed(2)}" y="${height - 18}" text-anchor="middle">${escapeHtml(point?.shortLabel || point?.label || "")}</text>
                </g>
            `;
        }).join("");

        container.innerHTML = `
            <div class="business-billing-chart__meta">
                <span><strong>${escapeHtml(currencyFormatter.format(Number(chart?.totalSales || 0)))}</strong> ventas</span>
                <span><strong>${escapeHtml(numberFormatter.format(Number(chart?.recordsCount || 0)))}</strong> facturas</span>
            </div>
            <div class="business-billing-chart__scroll">
                <svg class="business-billing-chart__svg" viewBox="0 0 ${width} ${height}" role="img" aria-label="${escapeHtml(chart?.label || "Ventas")}">
                    ${gridLines}
                    <line class="business-billing-chart__baseline" x1="${padding.left}" y1="${baselineY}" x2="${width - padding.right}" y2="${baselineY}"></line>
                    ${bars}
                </svg>
            </div>
        `;
    }

    function renderBusinessBillingDashboard(dashboard) {
        renderBusinessBillingChart(businessBillingCloudMonthlyChart, dashboard?.cloud?.monthly, "is-cloud-monthly");
        renderBusinessBillingChart(businessBillingCloudPrepaidChart, dashboard?.cloud?.prepaid, "is-cloud-prepaid");
        renderBusinessBillingChart(businessBillingCopiersMonthlyChart, dashboard?.copiers?.monthly, "is-copiers-monthly");
        renderBusinessBillingChart(businessBillingCopiersPrepaidChart, dashboard?.copiers?.prepaid, "is-copiers-prepaid");
    }

    function renderCloudBillingKpis(dashboard) {
        if (!cloudBillingKpisContainer) {
            return;
        }

        cloudBillingKpisContainer.innerHTML = "";
    }

    function renderCloudBillingStatus(row) {
        const tone = normalizeText(row?.statusTone || "neutral").replace(/[^a-z0-9]+/g, "-") || "neutral";
        return `<span class="dashboard-status-pill dashboard-status-pill--${escapeHtml(tone)}">${escapeHtml(row?.statusLabel || "Sin estado")}</span>`;
    }

    function renderCloudBillingAuditPill(label, tone, detail) {
        const normalizedTone = normalizeText(tone || "neutral").replace(/[^a-z0-9]+/g, "-") || "neutral";
        const cleanDetail = (detail || "").trim();
        return `
            <span class="dashboard-status-pill dashboard-status-pill--${escapeHtml(normalizedTone)}">${escapeHtml(label || "Sin validar")}</span>
            ${cleanDetail ? `<small>${escapeHtml(cleanDetail)}</small>` : ""}
        `;
    }

    function rowMatchesCloudBillingFilter(row) {
        const filter = state.cloudBillingStatusFilter || "all";
        if (filter === "overdue" && !row?.isOverdue) {
            return false;
        }

        if (filter === "today" && !row?.isDueToday) {
            return false;
        }

        if (filter === "pending" && !row?.isPending) {
            return false;
        }

        if (filter === "billed" && !row?.isBillingComplete) {
            return false;
        }

        if (filter === "dian-pending" && !(row?.isBilled && !row?.isDianAccepted)) {
            return false;
        }

        if (filter === "mail-pending" && !(row?.isDianAccepted && !row?.isEmailSent)) {
            return false;
        }

        if (filter === "siigo-missing" && row?.statusKey !== "siigo-missing") {
            return false;
        }

        if (filter === "error" && !row?.hasBillingError) {
            return false;
        }

        if (filter === "manual" && row?.statusKey !== "manual" && row?.statusKey !== "no-day") {
            return false;
        }

        const term = normalizeText(state.cloudBillingSearchTerm);
        if (!term) {
            return true;
        }

        const haystack = normalizeText([
            row?.statusLabel,
            row?.clientName,
            row?.productName,
            row?.productLineLabel,
            row?.contractTypeLabel,
            row?.billingDayDisplay,
            row?.expectedBillingDateDisplay,
            row?.lastInvoiceDateDisplay,
            row?.lastSiigoInvoiceId,
            row?.matchedSiigoInvoiceId,
            row?.matchedSiigoInvoiceName,
            row?.monthInvoiceNumbers,
            row?.dianStatus,
            row?.dianStatusLabel,
            row?.dianObservations,
            row?.dianErrors,
            row?.mailStatus,
            row?.mailStatusLabel,
            row?.mailObservations,
            row?.billingError,
            row?.evidenceLabel
        ].join(" "));

        return haystack.includes(term);
    }

    function getCloudBillingStatusPriority(row) {
        if (row?.isOverdue || row?.statusKey === "overdue") {
            return 0;
        }

        if (row?.hasBillingError || row?.isDianRejected || row?.isSiigoInvoiceAnnulled || row?.statusTone === "danger") {
            return 1;
        }

        if (row?.isDueToday) {
            return 2;
        }

        if (row?.statusKey === "siigo-missing" || row?.statusKey === "dian-pending" || row?.statusKey === "mail-pending") {
            return 3;
        }

        if (row?.isPending) {
            return 4;
        }

        if (row?.statusKey === "manual" || row?.statusKey === "no-day") {
            return 5;
        }

        if (row?.isBillingComplete || row?.isBilled) {
            return 6;
        }

        return 7;
    }

    function buildCloudBillingGroupKey(row) {
        const clientKey = (row?.clientId || normalizeText(row?.clientName || "") || "sin-cliente").toString();
        const dayKey = Number(row?.billingDay || 0) > 0
            ? String(Number(row.billingDay))
            : (row?.billingDayDisplay || row?.expectedBillingDateValue || "sin-dia").toString();

        return `${clientKey}::${dayKey}`;
    }

    function getCloudBillingRowSiigoReferences(row) {
        return [
            row?.matchedSiigoInvoiceName,
            row?.matchedSiigoInvoiceId,
            row?.lastSiigoInvoiceId,
            row?.monthInvoiceNumbers
        ]
            .map(value => (value || "").toString().trim())
            .filter(Boolean);
    }

    function summarizeCloudBillingReferences(rows) {
        const references = [];
        (rows || []).forEach(row => {
            getCloudBillingRowSiigoReferences(row).forEach(reference => {
                if (!references.some(item => normalizeText(item) === normalizeText(reference))) {
                    references.push(reference);
                }
            });
        });

        if (!references.length) {
            return "-";
        }

        return references.length > 1
            ? `${references[0]} +${references.length - 1}`
            : references[0];
    }

    function buildCloudBillingGroups(rows) {
        const map = new Map();
        (rows || []).forEach(row => {
            const key = buildCloudBillingGroupKey(row);
            if (!map.has(key)) {
                map.set(key, {
                    key,
                    rows: []
                });
            }

            map.get(key).rows.push(row);
        });

        return Array.from(map.values()).map(group => {
            const sortedRows = group.rows.slice().sort((left, right) =>
                getCloudBillingStatusPriority(left) - getCloudBillingStatusPriority(right)
                || String(left?.expectedBillingDateValue || "").localeCompare(String(right?.expectedBillingDateValue || ""), "es", { numeric: true })
                || String(left?.productName || "").localeCompare(String(right?.productName || ""), "es", { sensitivity: "base" }));
            const statusRow = sortedRows[0] || {};
            const firstRow = group.rows[0] || {};
            const statuses = new Set(group.rows.map(row => row?.statusLabel || "Sin estado").filter(Boolean));

            return {
                key: group.key,
                rows: sortedRows,
                clientId: firstRow.clientId || "",
                clientName: firstRow.clientName || "Sin cliente",
                billingDay: Number(firstRow.billingDay || 0),
                billingDayDisplay: firstRow.billingDayDisplay || (firstRow.billingDay ? String(firstRow.billingDay) : "Sin dia"),
                dayDisplay: firstRow.expectedBillingDateDisplay || firstRow.billingDayDisplay || "Sin fecha",
                statusKey: statusRow.statusKey || "default",
                statusLabel: statusRow.statusLabel || "Sin estado",
                statusTone: statusRow.statusTone || "neutral",
                statusCount: statuses.size,
                productsCount: group.rows.length,
                monthlyBillingUsd: group.rows.reduce((total, row) => total + Number(row?.monthlyBillingUsd || 0), 0),
                siigoReference: summarizeCloudBillingReferences(group.rows)
            };
        }).sort((left, right) =>
            getCloudBillingStatusPriority(left) - getCloudBillingStatusPriority(right)
            || Number(left.billingDay || 0) - Number(right.billingDay || 0)
            || String(left.clientName || "").localeCompare(String(right.clientName || ""), "es", { sensitivity: "base" }));
    }

    function getFilteredCloudBillingRows() {
        const rows = Array.isArray(state.cloudBillingDashboard?.rows)
            ? state.cloudBillingDashboard.rows
            : [];

        return rows.filter(rowMatchesCloudBillingFilter);
    }

    function renderCloudBillingTable() {
        const rows = getFilteredCloudBillingRows();
        const groups = buildCloudBillingGroups(rows);
        const totalRows = Array.isArray(state.cloudBillingDashboard?.rows)
            ? state.cloudBillingDashboard.rows.length
            : 0;
        const totalGroups = buildCloudBillingGroups(Array.isArray(state.cloudBillingDashboard?.rows)
            ? state.cloudBillingDashboard.rows
            : []).length;

        state.cloudBillingGroups = groups;

        if (cloudBillingResultsCount) {
            cloudBillingResultsCount.textContent = `Mostrando ${numberFormatter.format(groups.length)} de ${numberFormatter.format(totalGroups)} grupos`;
        }

        if (cloudBillingSummaryText) {
            cloudBillingSummaryText.textContent = state.cloudBillingDashboard
                ? `${numberFormatter.format(rows.length)} de ${numberFormatter.format(totalRows)} productos en los filtros`
                : "-";
        }

        if (!cloudBillingBody) {
            return;
        }

        if (!groups.length) {
            const emptyMessage = state.cloudBillingDashboard?.hasData
                ? "No hay grupos que coincidan con los filtros."
                : (state.cloudBillingDashboard?.emptyStateMessage || "No encontramos productos Cloud para revisar.");
            cloudBillingBody.innerHTML = `<tr><td colspan="5" class="dashboard-table__empty">${escapeHtml(emptyMessage)}</td></tr>`;
            return;
        }

        cloudBillingBody.innerHTML = groups.map((group, index) => {
            return `
                <tr class="dashboard-cloud-billing-row dashboard-cloud-billing-row--${escapeHtml(group.statusKey || "default")}"
                    tabindex="0"
                    data-cloud-billing-group-index="${index}"
                    aria-label="Ver detalle de ${escapeHtml(group.clientName || "cliente")}">
                    <td>${renderCloudBillingStatus(group)}</td>
                    <td>${escapeHtml(group.dayDisplay || "Sin fecha")}</td>
                    <td>${escapeHtml(group.clientName || "Sin cliente")}</td>
                    <td>${escapeHtml(group.siigoReference || "-")}</td>
                    <td>${escapeHtml(group.billingDayDisplay || "Sin dia")}</td>
                </tr>
            `;
        }).join("");
    }

    function renderCloudBillingDetailErrors(group) {
        const items = [];
        (group?.rows || []).forEach(row => {
            const details = [
                row?.billingError,
                row?.dianErrors,
                row?.isDianRejected ? row?.dianObservations : "",
                row?.isSiigoInvoiceAnnulled ? "Factura anulada en Siigo." : "",
                row?.mailStatusTone === "danger" ? row?.mailObservations : ""
            ]
                .map(value => (value || "").toString().trim())
                .filter(Boolean);

            if (!details.length) {
                return;
            }

            items.push(`
                <div class="cloud-billing-detail-error">
                    <strong>${escapeHtml(row?.productName || "Producto sin asignar")}</strong>
                    <span>${escapeHtml(details.join(" | "))}</span>
                </div>
            `);
        });

        return items.length
            ? items.join("")
            : "Sin errores registrados.";
    }

    function openCloudBillingDetailModal(group) {
        if (!cloudBillingDetailModal || !group) {
            return;
        }

        state.cloudBillingActiveGroup = group;

        if (cloudBillingDetailTitle) {
            cloudBillingDetailTitle.textContent = group.clientName || "Detalle de facturacion";
        }

        if (cloudBillingDetailSubtitle) {
            cloudBillingDetailSubtitle.textContent = `${group.dayDisplay || "Sin fecha"} | Dia de facturacion ${group.billingDayDisplay || "Sin dia"}`;
        }

        if (cloudBillingDetailSummary) {
            cloudBillingDetailSummary.innerHTML = `
                <span>${renderCloudBillingStatus(group)}</span>
                <span><strong>${escapeHtml(numberFormatter.format(group.productsCount || 0))}</strong> productos</span>
                <span><strong>${escapeHtml(formatUsd(group.monthlyBillingUsd || 0))}</strong> mensual</span>
                <span>Siigo ID: <strong>${escapeHtml(group.siigoReference || "-")}</strong></span>
            `;
        }

        if (cloudBillingDetailErrors) {
            cloudBillingDetailErrors.innerHTML = renderCloudBillingDetailErrors(group);
        }

        if (cloudBillingDetailBody) {
            cloudBillingDetailBody.innerHTML = (group.rows || []).map(row => {
                const invoiceReference = row?.monthInvoiceNumbers
                    ? `${row.monthInvoiceNumbers}${row.matchedByInvoiceTable ? " | match" : " | cliente"}`
                    : "-";
                const errorText = (row?.billingError || "").trim();
                const dianDetail = (row?.dianErrors || row?.dianStatus || row?.dianObservations || "").trim();
                const mailDetail = (row?.mailStatus || row?.mailObservations || "").trim();

                return `
                    <tr>
                        <td>${renderCloudBillingStatus(row)}</td>
                        <td>
                            <strong>${escapeHtml(row?.productName || "Producto sin asignar")}</strong>
                            <small>${escapeHtml(row?.productLineLabel || "")}</small>
                        </td>
                        <td>${escapeHtml(row?.contractTypeLabel || "Sin contrato")}</td>
                        <td class="text-end">${escapeHtml(numberFormatter.format(Number(row?.quantity || 0)))}</td>
                        <td class="text-end"><strong>${escapeHtml(formatUsd(row?.monthlyBillingUsd || 0))}</strong></td>
                        <td>${escapeHtml(row?.lastInvoiceDateDisplay || "Sin factura")}</td>
                        <td>${escapeHtml(invoiceReference)}</td>
                        <td>${renderCloudBillingAuditPill(row?.dianStatusLabel, row?.dianStatusTone, dianDetail)}</td>
                        <td>${renderCloudBillingAuditPill(row?.mailStatusLabel, row?.mailStatusTone, mailDetail)}</td>
                        <td class="dashboard-cloud-billing-error">${errorText ? escapeHtml(errorText) : "-"}</td>
                    </tr>
                `;
            }).join("");
        }

        document.body.classList.add("dashboard-modal-open");
        cloudBillingDetailModal.hidden = false;
        window.setTimeout(() => cloudBillingDetailCloseBtn?.focus(), 30);
    }

    function renderCloudBillingDashboard(dashboard) {
        updateCloudBillingContext(dashboard);
        renderCloudBillingKpis(dashboard);
        renderCloudBillingTable();
    }

    function renderCopiersKpis(dashboard) {
        renderSimpleKpis(copiersKpisContainer, dashboard?.kpis);
    }

    function renderCopiersEquipmentKpis(dashboard) {
        renderSimpleKpis(copiersEquipmentKpisContainer, dashboard?.kpis);
    }

    function renderCopiersInventoryKpis(dashboard) {
        renderSimpleKpis(copiersInventoryKpisContainer, dashboard?.kpis);
    }

    function renderPnlKpis(dashboard) {
        const kpis = Array.isArray(dashboard?.kpis) ? dashboard.kpis : [];
        if (!pnlKpisContainer) {
            return;
        }

        pnlKpisContainer.innerHTML = kpis.map(kpi => `
            <article class="dashboard-kpi dashboard-kpi--${escapeHtml(kpi.tone || "neutral")}">
                <div class="dashboard-kpi__header">
                    <span class="dashboard-kpi__label">${escapeHtml(kpi.label)}</span>
                </div>
                <strong class="dashboard-kpi__value">${escapeHtml(formatMetric(kpi.value, kpi.valueFormat))}</strong>
                <span class="dashboard-kpi__hint">${escapeHtml(kpi.hint)}</span>
            </article>
        `).join("");
    }

    function formatLicenciamientoPercent(value) {
        if (value === null || value === undefined || value === "") {
            return "Sin margen";
        }

        return formatPercent(value);
    }

    function formatSignedCurrency(value) {
        const numericValue = Number(value || 0);
        const prefix = numericValue > 0 ? "+" : "";
        return `${prefix}${currencyFormatter.format(numericValue)}`;
    }

    function renderLicenciamientoSummary(dashboard) {
        if (!licenciamientoSummaryCards) {
            return;
        }

        const utility = Number(dashboard?.totalUtility || 0);
        const utilityTone = utility >= 0 ? "positive" : "negative";
        const cards = [
            {
                label: "Ventas totales",
                value: currencyFormatter.format(Number(dashboard?.totalSales || 0)),
                hint: dashboard?.dateRangeLabel || ""
            },
            {
                label: "Costos totales",
                value: currencyFormatter.format(Number(dashboard?.totalCost || 0)),
                hint: `${numberFormatter.format(Number(dashboard?.recordsCount || 0))} cruces`
            },
            {
                label: "Utilidad total",
                value: formatSignedCurrency(utility),
                hint: `Margen ${formatLicenciamientoPercent(dashboard?.totalUtilityPercent)}`,
                tone: utilityTone
            },
            {
                label: "Ventas Monthly",
                value: currencyFormatter.format(Number(dashboard?.monthly?.totalSales || 0)),
                hint: `${formatPercent(dashboard?.monthly?.salesSharePercent || 0)} de ventas`
            },
            {
                label: "Ventas Prepaid",
                value: currencyFormatter.format(Number(dashboard?.prepaid?.totalSales || 0)),
                hint: `${formatPercent(dashboard?.prepaid?.salesSharePercent || 0)} de ventas`
            }
        ];

        licenciamientoSummaryCards.innerHTML = cards.map(card => `
            <article class="lic-summary-card ${card.tone ? `is-${card.tone}` : ""}">
                <span class="lic-summary-card__label">${escapeHtml(card.label)}</span>
                <strong class="lic-summary-card__value">${escapeHtml(card.value)}</strong>
                <span class="lic-summary-card__hint">${escapeHtml(card.hint)}</span>
            </article>
        `).join("");
    }

    function renderLicenciamientoChart(container, segment, accent) {
        if (!container) {
            return;
        }

        const points = Array.isArray(segment?.months) ? segment.months : [];
        const hasData = points.some(point => Number(point.recordsCount || 0) > 0);
        const values = points.map(point => Number(point.utility || 0));
        const minValue = Math.min(0, ...values);
        const maxValue = Math.max(0, ...values);
        const range = maxValue === minValue ? 1 : maxValue - minValue;
        const width = 720;
        const height = 286;
        const padding = { top: 24, right: 22, bottom: 42, left: 64 };
        const plotWidth = width - padding.left - padding.right;
        const plotHeight = height - padding.top - padding.bottom;
        const barSlot = plotWidth / Math.max(points.length, 1);
        const barWidth = Math.min(28, Math.max(12, barSlot * 0.46));
        const yForValue = value => padding.top + ((maxValue - value) / range) * plotHeight;
        const baselineY = yForValue(0);
        const gridValues = [maxValue, (maxValue + minValue) / 2, minValue]
            .filter((value, index, all) => index === all.findIndex(item => Math.abs(item - value) < 0.01));

        const grid = gridValues.map(value => {
            const y = yForValue(value);
            return `
                <line class="lic-chart__grid" x1="${padding.left}" y1="${y}" x2="${width - padding.right}" y2="${y}"></line>
                <text class="lic-chart__axis" x="${padding.left - 10}" y="${y + 4}" text-anchor="end">${escapeHtml(numberFormatter.format(value))}</text>
            `;
        }).join("");

        const bars = points.map((point, index) => {
            const utility = Number(point.utility || 0);
            const sales = Number(point.sales || 0);
            const x = padding.left + (barSlot * index) + ((barSlot - barWidth) / 2);
            const y = utility >= 0 ? yForValue(utility) : baselineY;
            const barHeight = Math.max(2, Math.abs(yForValue(utility) - baselineY));
            const labelX = padding.left + (barSlot * index) + (barSlot / 2);
            const tone = utility >= 0 ? "positive" : "negative";
            return `
                <g>
                    <title>${escapeHtml(`${point.label || ""}: utilidad ${currencyFormatter.format(utility)} - ventas ${currencyFormatter.format(sales)}`)}</title>
                    <rect class="lic-chart__bar is-${tone}" x="${x.toFixed(2)}" y="${y.toFixed(2)}" width="${barWidth.toFixed(2)}" height="${barHeight.toFixed(2)}" rx="5"></rect>
                    <text class="lic-chart__month" x="${labelX.toFixed(2)}" y="${height - 14}" text-anchor="middle">${escapeHtml(point.label || "")}</text>
                </g>
            `;
        }).join("");

        container.innerHTML = `
            <div class="lic-chart-card__header">
                <div>
                    <span class="lic-chart-card__eyebrow">${escapeHtml(segment?.label || "Licenciamiento")}</span>
                    <h2 class="lic-chart-card__title">Utilidad mensual</h2>
                </div>
                <span class="lic-chart-card__share">${escapeHtml(formatPercent(segment?.salesSharePercent || 0))} ventas</span>
            </div>
            <div class="lic-chart-card__metrics">
                <span><strong>${escapeHtml(currencyFormatter.format(Number(segment?.totalSales || 0)))}</strong> ventas</span>
                <span><strong>${escapeHtml(formatSignedCurrency(segment?.totalUtility || 0))}</strong> utilidad</span>
                <span><strong>${escapeHtml(formatLicenciamientoPercent(segment?.utilityPercent))}</strong> margen</span>
            </div>
            <div class="lic-chart-card__canvas ${hasData ? "" : "is-empty"}">
                ${hasData ? `
                    <svg class="lic-chart" viewBox="0 0 ${width} ${height}" role="img" aria-label="Utilidad mensual ${escapeHtml(segment?.label || "")}">
                        <defs>
                            <linearGradient id="lic-chart-gradient-${escapeHtml(segment?.key || "segment")}" x1="0" x2="0" y1="0" y2="1">
                                <stop offset="0%" stop-color="${accent}" stop-opacity=".34"></stop>
                                <stop offset="100%" stop-color="${accent}" stop-opacity=".08"></stop>
                            </linearGradient>
                        </defs>
                        ${grid}
                        <line class="lic-chart__baseline" x1="${padding.left}" y1="${baselineY}" x2="${width - padding.right}" y2="${baselineY}"></line>
                        ${bars}
                    </svg>
                ` : '<div class="lic-empty">Sin utilidad para el periodo seleccionado.</div>'}
            </div>
        `;
    }

    function renderLicenciamientoCostCard(card, accentClass) {
        const rows = Array.isArray(card?.breakdown) ? card.breakdown : [];
        const totalCost = Number(card?.totalCost || 0);
        const totalSales = Number(card?.totalSales || 0);
        const utility = Number(card?.utility || 0);
        const rowsHtml = rows.length
            ? rows.map(item => {
                const share = Math.min(Math.max(Number(item.sharePercent || 0), 0), 100);
                const groupLabel = item.businessGroupName ? "Grupo empresarial" : "Cliente";
                return `
                    <div class="lic-cost-breakdown__row">
                        <div class="lic-cost-breakdown__main">
                            <strong>${escapeHtml(item.clientName || "Sin cliente")}</strong>
                            <span>${escapeHtml(groupLabel)} · ${escapeHtml(numberFormatter.format(Number(item.recordsCount || 0)))} cruce(s)</span>
                        </div>
                        <div class="lic-cost-breakdown__amount">
                            <strong>${escapeHtml(currencyFormatter.format(Number(item.cost || 0)))}</strong>
                            <span>${escapeHtml(formatPercent(item.sharePercent || 0))}</span>
                        </div>
                        <div class="lic-cost-breakdown__bar" aria-hidden="true">
                            <span style="width:${share}%"></span>
                        </div>
                    </div>
                `;
            }).join("")
            : '<div class="lic-empty">Sin costos para este mes.</div>';

        return `
            <article class="lic-cost-card ${accentClass}">
                <div class="lic-cost-card__header">
                    <div>
                        <span class="lic-cost-card__eyebrow">${escapeHtml(card?.label || "")}</span>
                        <h3 class="lic-cost-card__title">${escapeHtml(card?.monthLabel || "")}</h3>
                    </div>
                    <span class="lic-cost-card__badge">${escapeHtml(numberFormatter.format(Number(card?.recordsCount || 0)))} cruces</span>
                </div>
                <strong class="lic-cost-card__value">${escapeHtml(currencyFormatter.format(totalCost))}</strong>
                <div class="lic-cost-card__stats">
                    <span>Ventas <strong>${escapeHtml(currencyFormatter.format(totalSales))}</strong></span>
                    <span>Utilidad <strong>${escapeHtml(formatSignedCurrency(utility))}</strong></span>
                    <span>Margen <strong>${escapeHtml(formatLicenciamientoPercent(card?.utilityPercent))}</strong></span>
                </div>
                <div class="lic-cost-breakdown">
                    <div class="lic-cost-breakdown__header">
                        <span>Desglose por cliente</span>
                        <span>Costo</span>
                    </div>
                    ${rowsHtml}
                </div>
            </article>
        `;
    }

    function renderLicenciamientoCostCards(dashboard) {
        if (!licenciamientoCostCards) {
            return;
        }

        licenciamientoCostCards.innerHTML = [
            renderLicenciamientoCostCard(dashboard?.monthlyCostCard, "lic-cost-card--monthly"),
            renderLicenciamientoCostCard(dashboard?.prepaidCostCard, "lic-cost-card--prepaid")
        ].join("");
    }

    function renderLicenciamientoDashboard(dashboard) {
        renderLicenciamientoSummary(dashboard);
        renderLicenciamientoChart(licenciamientoMonthlyChart, dashboard?.monthly, "#0f766e");
        renderLicenciamientoChart(licenciamientoPrepaidChart, dashboard?.prepaid, "#b45309");
        renderLicenciamientoCostCards(dashboard);
    }

    function formatUtilityPercent(value) {
        if (value === null || value === undefined || value === "") {
            return "Sin margen";
        }

        return formatPercent(value);
    }

    function getUtilityTheoreticalCard(cardKey) {
        const dashboard = state.utilityDashboard || {};
        return cardKey === "prepaid"
            ? dashboard.theoreticalPrepaid
            : dashboard.theoreticalMonthly;
    }

    function getUtilityRowId(row) {
        return row?.recordId || `${row?.clientName || ""}|${row?.productName || ""}|${row?.contractTypeLabel || ""}`;
    }

    function getUtilityAllTheoreticalRows(dashboard = state.utilityDashboard) {
        return [
            ...(Array.isArray(dashboard?.theoreticalMonthly?.breakdown) ? dashboard.theoreticalMonthly.breakdown : []),
            ...(Array.isArray(dashboard?.theoreticalPrepaid?.breakdown) ? dashboard.theoreticalPrepaid.breakdown : [])
        ];
    }

    function getUtilityAvailableTheoreticalRowIds(dashboard = state.utilityDashboard) {
        return new Set(getUtilityAllTheoreticalRows(dashboard)
            .map(getUtilityRowId)
            .filter(Boolean));
    }

    function setsHaveSameValues(left, right) {
        if ((left?.size || 0) !== (right?.size || 0)) {
            return false;
        }

        for (const value of left || []) {
            if (!right?.has(value)) {
                return false;
            }
        }

        return true;
    }

    function applySavedUtilityTheoreticalExclusions(dashboard) {
        const stored = readUtilityStoredTheoreticalExclusions();
        const available = getUtilityAvailableTheoreticalRowIds(dashboard);
        const resolved = new Set(Array.from(stored).filter(rowId => available.has(rowId)));
        state.utilityExcludedTheoreticalRowIds = resolved;
        state.utilitySavedExcludedTheoreticalRowIds = new Set(resolved);
        state.utilityBreakdownDirty = false;
        setStatus(utilityBreakdownStatus, "", "");
        updateUtilityBreakdownSaveState();
    }

    function updateUtilityBreakdownSaveState() {
        state.utilityBreakdownDirty = !setsHaveSameValues(
            state.utilityExcludedTheoreticalRowIds,
            state.utilitySavedExcludedTheoreticalRowIds);

        if (utilityBreakdownSaveBtn) {
            utilityBreakdownSaveBtn.disabled = state.utilityLoading || !state.utilityBreakdownDirty;
            utilityBreakdownSaveBtn.textContent = state.utilityBreakdownDirty ? "Guardar desglose" : "Desglose guardado";
        }
    }

    function saveUtilityTheoreticalBreakdown() {
        const available = getUtilityAvailableTheoreticalRowIds();
        const idsToSave = new Set(Array.from(state.utilityExcludedTheoreticalRowIds)
            .filter(rowId => available.has(rowId)));

        try {
            writeUtilityStoredTheoreticalExclusions(idsToSave);
            state.utilityExcludedTheoreticalRowIds = new Set(idsToSave);
            state.utilitySavedExcludedTheoreticalRowIds = new Set(idsToSave);
            updateUtilityBreakdownSaveState();
            renderUtilitySummary(state.utilityDashboard);
            if (state.utilityBreakdownCardKey) {
                renderUtilityBreakdownModal(getUtilityTheoreticalCard(state.utilityBreakdownCardKey));
            }
            setStatus(utilityBreakdownStatus, "success", "Desglose guardado. Al actualizar el panel se aplicara esta seleccion.");
        } catch {
            setStatus(utilityBreakdownStatus, "error", "No fue posible guardar el desglose en este navegador.");
        }
    }

    function isUtilityRowIncluded(row) {
        const rowId = getUtilityRowId(row);
        return !rowId || !state.utilityExcludedTheoreticalRowIds.has(rowId);
    }

    function calculateUtilityPercentValue(utility, sales) {
        return Math.abs(Number(sales || 0)) < 0.01
            ? null
            : Math.round((Number(utility || 0) / Number(sales || 0)) * 10000) / 100;
    }

    function getUtilityCardRows(card) {
        return Array.isArray(card?.breakdown) ? card.breakdown : [];
    }

    function getUtilityEffectiveCard(card) {
        const rows = getUtilityCardRows(card);
        if (!rows.length) {
            return {
                ...(card || {}),
                includedRecordsCount: Number(card?.recordsCount || 0),
                totalRecordsCount: Number(card?.recordsCount || 0)
            };
        }

        const includedRows = rows.filter(isUtilityRowIncluded);
        const sales = includedRows.reduce((sum, row) => sum + Number(row?.sales || 0), 0);
        const cost = includedRows.reduce((sum, row) => sum + Number(row?.cost || 0), 0);
        const utility = sales - cost;

        return {
            ...(card || {}),
            sales,
            cost,
            utility,
            utilityPercent: calculateUtilityPercentValue(utility, sales),
            recordsCount: includedRows.length,
            includedRecordsCount: includedRows.length,
            totalRecordsCount: rows.length,
            missingCostCount: includedRows.filter(row => row?.hasCost === false).length
        };
    }

    function groupUtilityRowsByClient(rows) {
        const groups = new Map();
        rows.forEach(row => {
            const clientName = row?.clientName || "Sin cliente";
            const key = normalizeText(clientName) || clientName;
            if (!groups.has(key)) {
                groups.set(key, {
                    key,
                    clientName,
                    rows: []
                });
            }

            groups.get(key).rows.push(row);
        });

        return Array.from(groups.values())
            .sort((left, right) => left.clientName.localeCompare(right.clientName, "es", { sensitivity: "base" }));
    }

    function renderUtilityBreakdownSummary(card, rows) {
        const includedRows = rows.filter(isUtilityRowIncluded);
        const sales = includedRows.reduce((sum, row) => sum + Number(row?.sales || 0), 0);
        const cost = includedRows.reduce((sum, row) => sum + Number(row?.cost || 0), 0);
        const utility = sales - cost;
        const utilityPercent = calculateUtilityPercentValue(utility, sales);

        if (utilityBreakdownSubtitle) {
            utilityBreakdownSubtitle.textContent = `${state.utilityDashboard?.periodLabel || "Periodo activo"} · ${numberFormatter.format(includedRows.length)} de ${numberFormatter.format(rows.length)} fila(s) incluidas`;
        }

        if (utilityBreakdownSummary) {
            utilityBreakdownSummary.innerHTML = `
                <div class="utility-breakdown-summary__item">
                    <span>Venta incluida</span>
                    <strong>${escapeHtml(currencyFormatter.format(sales))}</strong>
                </div>
                <div class="utility-breakdown-summary__item">
                    <span>Costo incluido</span>
                    <strong>${escapeHtml(currencyFormatter.format(cost))}</strong>
                </div>
                <div class="utility-breakdown-summary__item">
                    <span>Total teorico</span>
                    <strong>${escapeHtml(formatSignedCurrency(utility))}</strong>
                </div>
                <div class="utility-breakdown-summary__item">
                    <span>Margen</span>
                    <strong>${escapeHtml(formatUtilityPercent(utilityPercent))}</strong>
                </div>
            `;
        }

        if (utilityBreakdownFooter) {
            utilityBreakdownFooter.innerHTML = `
                <tr class="dashboard-table__total">
                    <td colspan="7">Total incluido</td>
                    <td class="text-end">${escapeHtml(currencyFormatter.format(sales))}</td>
                    <td class="text-end">${escapeHtml(currencyFormatter.format(cost))}</td>
                    <td class="text-end">${escapeHtml(formatSignedCurrency(utility))}</td>
                </tr>
            `;
        }
    }

    function syncUtilityBreakdownGroupCheckboxes() {
        utilityBreakdownBody?.querySelectorAll("[data-utility-client-count]").forEach(checkbox => {
            checkbox.indeterminate = checkbox.dataset.indeterminate === "true";
        });
    }

    function renderUtilityBreakdownModal(card) {
        const rows = Array.isArray(card?.breakdown) ? card.breakdown : [];

        if (utilityBreakdownTitle) {
            utilityBreakdownTitle.textContent = card?.label || "Detalle de utilidad teorica";
        }

        renderUtilityBreakdownSummary(card, rows);

        if (utilityBreakdownBody) {
            utilityBreakdownBody.innerHTML = rows.length
                ? groupUtilityRowsByClient(rows).map(group => {
                    const groupRows = group.rows || [];
                    const includedRows = groupRows.filter(isUtilityRowIncluded);
                    const allIncluded = includedRows.length === groupRows.length;
                    const noneIncluded = includedRows.length === 0;
                    const groupSales = includedRows.reduce((sum, row) => sum + Number(row?.sales || 0), 0);
                    const groupCost = includedRows.reduce((sum, row) => sum + Number(row?.cost || 0), 0);
                    const groupUtility = groupSales - groupCost;
                    const groupTone = groupUtility >= 0 ? "positive" : "negative";
                    const groupRow = `
                        <tr class="utility-breakdown-group-row">
                            <td>
                                <input type="checkbox"
                                       class="form-check-input"
                                       data-utility-client-count="${escapeHtml(group.key)}"
                                       data-indeterminate="${!allIncluded && !noneIncluded ? "true" : "false"}"
                                       ${allIncluded ? "checked" : ""}
                                       aria-label="Contar cliente ${escapeHtml(group.clientName)}">
                            </td>
                            <td colspan="6">
                                <div class="utility-row-main">${escapeHtml(group.clientName)}</div>
                                <div class="utility-row-muted">${escapeHtml(numberFormatter.format(includedRows.length))} de ${escapeHtml(numberFormatter.format(groupRows.length))} fila(s) incluidas</div>
                            </td>
                            <td class="text-end">${escapeHtml(currencyFormatter.format(groupSales))}</td>
                            <td class="text-end">${escapeHtml(currencyFormatter.format(groupCost))}</td>
                            <td class="text-end utility-breakdown__line-total is-${groupTone}">${escapeHtml(formatSignedCurrency(groupUtility))}</td>
                        </tr>
                    `;

                    const detailRows = groupRows.map(row => {
                    const hasCost = row?.hasCost !== false;
                    const costValue = Number(row?.cost || 0);
                    const lineUtility = Number(row?.utility || 0);
                    const lineTone = lineUtility >= 0 ? "positive" : "negative";
                    const billingDay = Number(row?.billingDay || 0);
                    const rowId = getUtilityRowId(row);
                    const included = isUtilityRowIncluded(row);
                    return `
                        <tr class="${included ? "" : "is-excluded"}">
                            <td>
                                <input type="checkbox"
                                       class="form-check-input"
                                       data-utility-row-count="${escapeHtml(rowId)}"
                                       ${included ? "checked" : ""}
                                       aria-label="Contar fila ${escapeHtml(row?.productName || "")}">
                            </td>
                            <td>
                                <div class="utility-row-muted">${billingDay > 0 ? `Dia ${escapeHtml(numberFormatter.format(billingDay))}` : ""}</div>
                            </td>
                            <td>
                                <div class="utility-row-main">${escapeHtml(row?.productName || "Sin producto")}</div>
                                <div class="utility-row-muted">${escapeHtml(row?.productLineLabel || "")}</div>
                            </td>
                            <td>${escapeHtml(row?.contractTypeLabel || "-")}</td>
                            <td class="text-end">${escapeHtml(numberFormatter.format(Number(row?.quantity || 0)))}</td>
                            <td class="text-end">${escapeHtml(usdUnitFormatter.format(Number(row?.unitSaleUsd || 0)))}</td>
                            <td class="text-end">${hasCost
                                ? escapeHtml(usdUnitFormatter.format(Number(row?.unitCostUsd || 0)))
                                : '<span class="utility-breakdown__missing-cost">Sin costo</span>'}</td>
                            <td class="text-end">${escapeHtml(currencyFormatter.format(Number(row?.sales || 0)))}</td>
                            <td class="text-end">${escapeHtml(currencyFormatter.format(costValue))}</td>
                            <td class="text-end utility-breakdown__line-total is-${lineTone}">${escapeHtml(formatSignedCurrency(lineUtility))}</td>
                        </tr>
                    `;
                    }).join("");

                    return groupRow + detailRows;
                }).join("")
                : '<tr><td colspan="10" class="dashboard-table__empty">Esta tarjeta no tiene filas para desglosar.</td></tr>';

            syncUtilityBreakdownGroupCheckboxes();
        }
    }

    function openUtilityBreakdownModal(cardKey) {
        if (!utilityBreakdownModal) {
            return;
        }

        const card = getUtilityTheoreticalCard(cardKey);
        if (!card) {
            return;
        }

        state.utilityBreakdownCardKey = cardKey === "prepaid" ? "prepaid" : "monthly";
        renderUtilityBreakdownModal(card);
        updateUtilityBreakdownSaveState();
        setStatus(utilityBreakdownStatus, "", "");
        utilityBreakdownModal.hidden = false;
        document.body.classList.add("dashboard-modal-open");
        window.setTimeout(() => utilityBreakdownCloseBtn?.focus(), 30);
    }

    function renderUtilityOrphansModal() {
        const rows = Array.isArray(state.utilityDashboard?.theoreticalOrphanRows)
            ? state.utilityDashboard.theoreticalOrphanRows
            : [];

        if (utilityOrphansTitle) {
            utilityOrphansTitle.textContent = `Filas huerfanas (${numberFormatter.format(rows.length)})`;
        }

        if (utilityOrphansSubtitle) {
            utilityOrphansSubtitle.textContent = rows.length
                ? "Estas filas no entraron en Monthly ni Annual porque el tipo de contrato esta vacio o no se reconoce. Asignarlas actualiza la columna de tipo de contrato en Productos Cloud."
                : "No hay filas huerfanas: todas las filas de Productos Cloud tienen tipo Monthly o Annual reconocible.";
        }

        if (!utilityOrphansBody) {
            return;
        }

        utilityOrphansBody.innerHTML = rows.length
            ? rows.map(row => {
                const rowUtility = Number(row?.utility || 0);
                const rowTone = rowUtility >= 0 ? "positive" : "negative";
                const isAssigning = state.utilityAssigningRecordId && state.utilityAssigningRecordId === row.recordId;
                return `
                    <tr>
                        <td>
                            <div class="utility-row-main">${escapeHtml(row?.clientName || "Sin cliente")}</div>
                            <div class="utility-row-muted">${Number(row?.billingDay || 0) > 0 ? `Dia ${escapeHtml(numberFormatter.format(Number(row?.billingDay || 0)))}` : "Sin dia"}</div>
                        </td>
                        <td>
                            <div class="utility-row-main">${escapeHtml(row?.productName || "Sin producto")}</div>
                            <div class="utility-row-muted">${escapeHtml(row?.productLineLabel || "")}</div>
                        </td>
                        <td>${escapeHtml(row?.contractTypeLabel || "Sin contrato")}</td>
                        <td>${escapeHtml(row?.reason || "")}</td>
                        <td class="text-end">${escapeHtml(currencyFormatter.format(Number(row?.sales || 0)))}</td>
                        <td class="text-end">${escapeHtml(currencyFormatter.format(Number(row?.cost || 0)))}</td>
                        <td class="text-end utility-breakdown__line-total is-${rowTone}">${escapeHtml(formatSignedCurrency(rowUtility))}</td>
                        <td>
                            <div class="utility-assign-control">
                                <select class="form-select form-select-sm dashboard-select" data-utility-target>
                                    <option value="monthly">Monthly</option>
                                    <option value="prepaid">Annual</option>
                                </select>
                                <button type="button"
                                        class="btn btn-sm btn-outline-primary"
                                        data-utility-assign
                                        data-source-type="sales-performance"
                                        data-record-id="${escapeHtml(row?.recordId || "")}"
                                        ${isAssigning ? "disabled" : ""}>${isAssigning ? "Guardando..." : "Asignar"}</button>
                            </div>
                        </td>
                    </tr>
                `;
            }).join("")
            : '<tr><td colspan="8" class="dashboard-table__empty">No hay filas huerfanas.</td></tr>';
    }

    function openUtilityOrphansModal() {
        if (!utilityOrphansModal) {
            return;
        }

        renderUtilityOrphansModal();
        utilityOrphansModal.hidden = false;
        document.body.classList.add("dashboard-modal-open");
        window.setTimeout(() => utilityOrphansCloseBtn?.focus(), 30);
    }

    function handleUtilityBreakdownCountChange(input) {
        const card = getUtilityTheoreticalCard(state.utilityBreakdownCardKey);
        if (!card) {
            return;
        }

        if (input.matches("[data-utility-row-count]")) {
            const rowId = input.dataset.utilityRowCount || "";
            if (rowId) {
                if (input.checked) {
                    state.utilityExcludedTheoreticalRowIds.delete(rowId);
                } else {
                    state.utilityExcludedTheoreticalRowIds.add(rowId);
                }
            }
        }

        if (input.matches("[data-utility-client-count]")) {
            const clientKey = input.dataset.utilityClientCount || "";
            const group = groupUtilityRowsByClient(getUtilityCardRows(card))
                .find(item => item.key === clientKey);
            (group?.rows || []).forEach(row => {
                const rowId = getUtilityRowId(row);
                if (!rowId) {
                    return;
                }

                if (input.checked) {
                    state.utilityExcludedTheoreticalRowIds.delete(rowId);
                } else {
                    state.utilityExcludedTheoreticalRowIds.add(rowId);
                }
            });
        }

        renderUtilitySummary(state.utilityDashboard);
        renderUtilityBreakdownModal(card);
        updateUtilityBreakdownSaveState();
        if (state.utilityBreakdownDirty) {
            setStatus(utilityBreakdownStatus, "warning", "Tienes cambios sin guardar en este desglose.");
        }
    }

    function renderUtilitySummaryCard(card, accentClass) {
        const effectiveCard = getUtilityEffectiveCard(card);
        const sales = Number(effectiveCard?.sales || 0);
        const cost = Number(effectiveCard?.cost || 0);
        const utility = Number(effectiveCard?.utility || 0);
        const tone = utility >= 0 ? "positive" : "negative";
        const cardKey = card?.key || "";
        const rows = getUtilityCardRows(card);
        const totalRows = rows.length || Number(card?.recordsCount || 0);
        const includedRows = Number(effectiveCard?.includedRecordsCount ?? effectiveCard?.recordsCount ?? 0);
        const orphanRows = Array.isArray(state.utilityDashboard?.theoreticalOrphanRows)
            ? state.utilityDashboard.theoreticalOrphanRows.length
            : 0;
        return `
            <article class="utility-summary-card ${accentClass} is-${tone}">
                <div class="utility-summary-card__header">
                    <span class="utility-summary-card__label">${escapeHtml(card?.label || "")}</span>
                    <div class="utility-summary-card__actions">
                        <button type="button"
                                class="btn btn-sm btn-outline-secondary utility-summary-card__breakdown-btn"
                                data-utility-breakdown="${escapeHtml(cardKey)}"
                                ${totalRows > 0 ? "" : "disabled"}>Desglose</button>
                        <button type="button"
                                class="btn btn-sm btn-outline-secondary utility-summary-card__breakdown-btn"
                                data-utility-orphans>Filas huerfanas${orphanRows > 0 ? ` (${escapeHtml(numberFormatter.format(orphanRows))})` : ""}</button>
                    </div>
                </div>
                <strong class="utility-summary-card__value">${escapeHtml(formatSignedCurrency(utility))}</strong>
                <div class="utility-summary-card__meta">
                    <span>Venta ${escapeHtml(currencyFormatter.format(sales))}</span>
                    <span>Costo ${escapeHtml(currencyFormatter.format(cost))}</span>
                </div>
                <div class="utility-summary-card__footer">
                    <span>${escapeHtml(numberFormatter.format(includedRows))} de ${escapeHtml(numberFormatter.format(totalRows))} filas</span>
                    <span>${escapeHtml(formatUtilityPercent(effectiveCard?.utilityPercent))}</span>
                    ${Number(effectiveCard?.missingCostCount || 0) > 0 ? `<span>${escapeHtml(numberFormatter.format(Number(effectiveCard?.missingCostCount || 0)))} sin costo</span>` : ""}
                </div>
            </article>
        `;
    }

    function renderUtilitySummary(dashboard) {
        if (!utilitySummaryCards) {
            return;
        }

        utilitySummaryCards.innerHTML = [
            renderUtilitySummaryCard(dashboard?.theoreticalMonthly, "utility-summary-card--monthly"),
            renderUtilitySummaryCard(dashboard?.theoreticalPrepaid, "utility-summary-card--prepaid")
        ].join("");
    }

    function getUtilityRealSegment(segmentKey) {
        const dashboard = state.utilityDashboard || {};
        return segmentKey === "prepaid"
            ? dashboard.realPrepaid
            : dashboard.realMonthly;
    }

    function getUtilityRealPoint(segmentKey, monthKey) {
        const segment = getUtilityRealSegment(segmentKey);
        const point = (Array.isArray(segment?.months) ? segment.months : [])
            .find(item => String(item?.key || "") === String(monthKey || ""));
        return { segment, point };
    }

    function renderUtilityRealDetailModal(segment, point) {
        const billingRows = Array.isArray(point?.billingRows) ? point.billingRows : [];
        const costRows = Array.isArray(point?.costRows) ? point.costRows : [];
        const sales = Number(point?.sales || 0);
        const cost = Number(point?.cost || 0);
        const utility = Number(point?.utility || 0);
        const utilityPercent = calculateUtilityPercentValue(utility, sales);

        if (utilityRealDetailTitle) {
            utilityRealDetailTitle.textContent = `${segment?.label || "Utilidad real"} - ${point?.label || "Mes"}`;
        }

        if (utilityRealDetailSubtitle) {
            utilityRealDetailSubtitle.textContent = `${numberFormatter.format(billingRows.length)} factura(s), ${numberFormatter.format(costRows.length)} costo(s)`;
        }

        if (utilityRealDetailSummary) {
            utilityRealDetailSummary.innerHTML = `
                <div class="utility-breakdown-summary__item">
                    <span>Ventas</span>
                    <strong>${escapeHtml(currencyFormatter.format(sales))}</strong>
                </div>
                <div class="utility-breakdown-summary__item">
                    <span>Costos</span>
                    <strong>${escapeHtml(currencyFormatter.format(cost))}</strong>
                </div>
                <div class="utility-breakdown-summary__item">
                    <span>Utilidad</span>
                    <strong>${escapeHtml(formatSignedCurrency(utility))}</strong>
                </div>
                <div class="utility-breakdown-summary__item">
                    <span>Margen</span>
                    <strong>${escapeHtml(formatUtilityPercent(utilityPercent))}</strong>
                </div>
            `;
        }

        if (utilityRealSalesTotal) {
            utilityRealSalesTotal.textContent = currencyFormatter.format(sales);
        }

        if (utilityRealCostsTotal) {
            utilityRealCostsTotal.textContent = currencyFormatter.format(cost);
        }

        if (utilityRealSalesBody) {
            utilityRealSalesBody.innerHTML = billingRows.length
                ? billingRows.map(row => {
                    const invoiceNumber = row?.invoiceNumber || row?.recordId || "Sin factura";
                    const invoiceHtml = row?.publicUrl
                        ? `<a class="dashboard-link" href="${escapeHtml(row.publicUrl)}" target="_blank" rel="noopener noreferrer">${escapeHtml(invoiceNumber)}</a>`
                        : escapeHtml(invoiceNumber);
                    return `
                        <tr>
                            <td>
                                <div class="utility-row-main">${invoiceHtml}</div>
                                <div class="utility-row-muted">${escapeHtml(row?.companyTaxId || "")}</div>
                            </td>
                            <td>${escapeHtml(row?.clientName || "Sin cliente")}</td>
                            <td>${escapeHtml(row?.emissionDateDisplay || "-")}</td>
                            <td>${escapeHtml(row?.contractTypeLabel || "-")}</td>
                            <td class="text-end">${escapeHtml(currencyFormatter.format(getNetInvoiceTotal(row)))}</td>
                        </tr>
                    `;
                }).join("")
                : '<tr><td colspan="5" class="dashboard-table__empty">Este mes no tiene facturas de venta en el calculo.</td></tr>';
        }

        if (utilityRealCostsBody) {
            utilityRealCostsBody.innerHTML = costRows.length
                ? costRows.map(row => {
                    const totalUsd = Number(row?.totalUsd || 0);
                    const quantity = Number(row?.quantity || 0);
                    const unitUsd = Number(row?.unitUsd || 0);
                    const usdLabel = Math.abs(totalUsd) >= 0.01
                        ? usdUnitFormatter.format(totalUsd)
                        : `${escapeHtml(numberFormatter.format(quantity))} x ${escapeHtml(usdUnitFormatter.format(unitUsd))}`;
                    return `
                        <tr>
                            <td>
                                <div class="utility-row-main">${escapeHtml(row?.reference || row?.recordId || "Sin referencia")}</div>
                                <div class="utility-row-muted">${escapeHtml(row?.vendor || "")}</div>
                            </td>
                            <td>
                                <div class="utility-row-main">${escapeHtml(row?.clientName || "Sin cliente")}</div>
                                <div class="utility-row-muted">${escapeHtml(row?.productName || "Sin producto")}</div>
                            </td>
                            <td>
                                <div>${escapeHtml(row?.dateDisplay || "-")}</div>
                                <div class="utility-row-muted">${escapeHtml(row?.contractTypeLabel || "")}</div>
                            </td>
                            <td class="text-end">
                                <div>${usdLabel}</div>
                                ${Number(row?.trm || 0) > 0 ? `<div class="utility-row-muted">TRM ${escapeHtml(numberFormatter.format(Number(row.trm || 0)))}</div>` : ""}
                            </td>
                            <td class="text-end">${escapeHtml(currencyFormatter.format(Number(row?.cost || 0)))}</td>
                        </tr>
                    `;
                }).join("")
                : '<tr><td colspan="5" class="dashboard-table__empty">Este mes no tiene costos en el calculo.</td></tr>';
        }
    }

    function openUtilityRealDetailModal(segmentKey, monthKey) {
        if (!utilityRealDetailModal) {
            return;
        }

        const { segment, point } = getUtilityRealPoint(segmentKey, monthKey);
        if (!point) {
            return;
        }

        state.utilityRealDetailContext = { segmentKey, monthKey };
        renderUtilityRealDetailModal(segment, point);
        utilityRealDetailModal.hidden = false;
        document.body.classList.add("dashboard-modal-open");
        window.setTimeout(() => utilityRealDetailCloseBtn?.focus(), 30);
    }

    function handleUtilityChartActivation(target) {
        const group = target?.closest?.("[data-utility-real-segment][data-utility-real-month]");
        if (!group || !group.classList.contains("is-clickable")) {
            return false;
        }

        openUtilityRealDetailModal(
            group.dataset.utilityRealSegment || "monthly",
            group.dataset.utilityRealMonth || "");
        return true;
    }

    function renderUtilityChart(container, segment, accent) {
        if (!container) {
            return;
        }

        const points = Array.isArray(segment?.months) ? segment.months : [];
        const hasData = points.some(point => Number(point.billingRecordsCount || 0) > 0 || Number(point.costRecordsCount || 0) > 0);
        const values = points.map(point => Number(point.utility || 0));
        let minValue = Math.min(0, ...values);
        let maxValue = Math.max(0, ...values);
        if (minValue === 0 && maxValue === 0) {
            minValue = -1;
            maxValue = 1;
        }
        const range = maxValue === minValue ? 1 : maxValue - minValue;
        const width = Math.max(900, points.length * 54);
        const height = 320;
        const padding = { top: 36, right: 24, bottom: 46, left: 68 };
        const plotWidth = width - padding.left - padding.right;
        const plotHeight = height - padding.top - padding.bottom;
        const barSlot = plotWidth / Math.max(points.length, 1);
        const barWidth = Math.min(30, Math.max(14, barSlot * 0.44));
        const yForValue = value => padding.top + ((maxValue - value) / range) * plotHeight;
        const baselineY = yForValue(0);
        const gridValues = [maxValue, (maxValue + minValue) / 2, minValue]
            .filter((value, index, all) => index === all.findIndex(item => Math.abs(item - value) < 0.01));
        const gradientId = `utility-chart-gradient-${segment?.key || "segment"}`;

        const grid = gridValues.map(value => {
            const y = yForValue(value);
            return `
                <line class="utility-chart__grid" x1="${padding.left}" y1="${y}" x2="${width - padding.right}" y2="${y}"></line>
                <text class="utility-chart__axis" x="${padding.left - 10}" y="${y + 4}" text-anchor="end">${escapeHtml(formatCompactMillions(value))}</text>
            `;
        }).join("");

        const bars = points.map((point, index) => {
            const utility = Number(point.utility || 0);
            const sales = Number(point.sales || 0);
            const cost = Number(point.cost || 0);
            const x = padding.left + (barSlot * index) + ((barSlot - barWidth) / 2);
            const y = utility >= 0 ? yForValue(utility) : baselineY;
            const barHeight = Math.max(2, Math.abs(yForValue(utility) - baselineY));
            const labelX = padding.left + (barSlot * index) + (barSlot / 2);
            const labelY = utility >= 0
                ? Math.max(14, y - 7)
                : Math.min(height - padding.bottom - 4, y + barHeight + 14);
            const tone = utility >= 0 ? "positive" : "negative";
            const isClickable = Number(point.billingRecordsCount || 0) > 0 || Number(point.costRecordsCount || 0) > 0;
            return `
                <g class="utility-chart__bar-hit ${isClickable ? "is-clickable" : ""}"
                   ${isClickable ? `tabindex="0" role="button" data-utility-real-segment="${escapeHtml(segment?.key || "monthly")}" data-utility-real-month="${escapeHtml(point.key || "")}" aria-label="${escapeHtml(`Ver detalle ${segment?.label || "utilidad real"} ${point.label || ""}`)}"` : ""}>
                    <title>${escapeHtml(`${point.label || ""}: utilidad ${currencyFormatter.format(utility)} - venta ${currencyFormatter.format(sales)} - costo ${currencyFormatter.format(cost)}`)}</title>
                    <rect class="utility-chart__bar is-${tone}" x="${x.toFixed(2)}" y="${y.toFixed(2)}" width="${barWidth.toFixed(2)}" height="${barHeight.toFixed(2)}" rx="4"></rect>
                    <text class="utility-chart__bar-label is-${tone}" x="${labelX.toFixed(2)}" y="${labelY.toFixed(2)}" text-anchor="middle">${escapeHtml(formatCompactMillions(utility))}</text>
                    <text class="utility-chart__month" x="${labelX.toFixed(2)}" y="${height - 16}" text-anchor="middle">${escapeHtml(point.label || "")}</text>
                </g>
            `;
        }).join("");

        container.innerHTML = `
            <div class="utility-chart-card__header">
                <div>
                    <span class="utility-chart-card__eyebrow">Cloud</span>
                    <h2 class="utility-chart-card__title">${escapeHtml(segment?.label || "Utilidad real")}</h2>
                </div>
                <span class="utility-chart-card__badge">${escapeHtml(formatUtilityPercent(segment?.utilityPercent))}</span>
            </div>
            <div class="utility-chart-card__metrics">
                <span><strong>${escapeHtml(currencyFormatter.format(Number(segment?.sales || 0)))}</strong> ventas</span>
                <span><strong>${escapeHtml(currencyFormatter.format(Number(segment?.cost || 0)))}</strong> costos</span>
                <span><strong>${escapeHtml(formatSignedCurrency(segment?.utility || 0))}</strong> utilidad</span>
            </div>
            <div class="utility-chart-card__canvas ${hasData ? "" : "is-empty"}">
                ${hasData ? `
                    <svg class="utility-chart" viewBox="0 0 ${width} ${height}" role="img" aria-label="${escapeHtml(segment?.label || "Utilidad real")}">
                        <defs>
                            <linearGradient id="${escapeHtml(gradientId)}" x1="0" x2="0" y1="0" y2="1">
                                <stop offset="0%" stop-color="${accent}" stop-opacity=".24"></stop>
                                <stop offset="100%" stop-color="${accent}" stop-opacity=".06"></stop>
                            </linearGradient>
                        </defs>
                        <rect class="utility-chart__plot" x="${padding.left}" y="${padding.top}" width="${plotWidth}" height="${plotHeight}" fill="url(#${escapeHtml(gradientId)})"></rect>
                        ${grid}
                        <line class="utility-chart__baseline" x1="${padding.left}" y1="${baselineY}" x2="${width - padding.right}" y2="${baselineY}"></line>
                        ${bars}
                    </svg>
                ` : '<div class="utility-empty">Sin ventas o costos para este periodo.</div>'}
            </div>
        `;
    }

    function renderUtilityUnresolvedRows(dashboard) {
        if (!utilityUnresolvedBody) {
            return;
        }

        const rows = Array.isArray(dashboard?.unresolvedRows) ? dashboard.unresolvedRows : [];
        if (utilityUnresolvedResultsCount) {
            utilityUnresolvedResultsCount.textContent = `Mostrando ${numberFormatter.format(rows.length)} fila(s)`;
        }

        if (!rows.length) {
            utilityUnresolvedBody.innerHTML = '<tr><td colspan="8" class="dashboard-table__empty">No hay filas pendientes por asignar.</td></tr>';
            return;
        }

        utilityUnresolvedBody.innerHTML = rows.map(row => {
            const canAssign = Boolean(row?.canAssign) && row?.sourceType !== "price";
            const isAssigning = state.utilityAssigningRecordId && state.utilityAssigningRecordId === row.recordId;
            const selectedBucket = row?.suggestedBucket === "prepaid" ? "prepaid" : "monthly";
            const classification = [
                row?.currentVertical,
                row?.currentContractType
            ].filter(Boolean).join(" / ") || "Sin clasificar";
            const targetControl = canAssign
                ? `
                    <div class="utility-assign-control">
                        <select class="form-select form-select-sm dashboard-select" data-utility-target>
                            <option value="monthly" ${selectedBucket === "monthly" ? "selected" : ""}>Monthly</option>
                            <option value="prepaid" ${selectedBucket === "prepaid" ? "selected" : ""}>Prepaid</option>
                        </select>
                        <button type="button"
                                class="btn btn-sm btn-outline-primary"
                                data-utility-assign
                                data-source-type="${escapeHtml(row?.sourceType || "")}"
                                data-record-id="${escapeHtml(row?.recordId || "")}"
                                ${isAssigning ? "disabled" : ""}>${isAssigning ? "Guardando..." : "Asignar"}</button>
                    </div>
                `
                : '<span class="dashboard-pnl-detail__static">Revisar origen</span>';

            return `
                <tr>
                    <td>${escapeHtml(row?.sourceLabel || "")}</td>
                    <td>${escapeHtml(row?.reference || row?.recordId || "")}</td>
                    <td>
                        <div class="utility-row-main">${escapeHtml(row?.clientName || "Sin cliente")}</div>
                        <div class="utility-row-muted">${escapeHtml(row?.productName || "")}</div>
                    </td>
                    <td>${escapeHtml(row?.dateDisplay || "-")}</td>
                    <td>${escapeHtml(classification)}</td>
                    <td>${escapeHtml(row?.reason || "")}</td>
                    <td class="text-end">${escapeHtml(currencyFormatter.format(Number(row?.amount || 0)))}</td>
                    <td>${targetControl}</td>
                </tr>
            `;
        }).join("");
    }

    function renderUtilityDashboard(dashboard) {
        renderUtilitySummary(dashboard);
        renderUtilityChart(utilityMonthlyChart, dashboard?.realMonthly, "#0f766e");
        renderUtilityChart(utilityPrepaidChart, dashboard?.realPrepaid, "#b45309");
        renderUtilityUnresolvedRows(dashboard);

        if (isUtilityBreakdownOpen() && state.utilityBreakdownCardKey) {
            renderUtilityBreakdownModal(getUtilityTheoreticalCard(state.utilityBreakdownCardKey));
            updateUtilityBreakdownSaveState();
        }

        if (isUtilityRealDetailOpen() && state.utilityRealDetailContext) {
            const { segment, point } = getUtilityRealPoint(
                state.utilityRealDetailContext.segmentKey,
                state.utilityRealDetailContext.monthKey);
            if (point) {
                renderUtilityRealDetailModal(segment, point);
            } else {
                closeUtilityRealDetailModal();
            }
        }
    }

    function initializeYtdFilterState(dashboard) {
        const revenueCategories = Array.isArray(dashboard?.revenueFilters?.categories) ? dashboard.revenueFilters.categories : [];
        const revenueClients = Array.isArray(dashboard?.revenueFilters?.clients) ? dashboard.revenueFilters.clients : [];
        const revenueVerticals = Array.isArray(dashboard?.revenueFilters?.verticals) ? dashboard.revenueFilters.verticals : [];
        const revenueContractTypes = Array.isArray(dashboard?.revenueFilters?.contractTypes) ? dashboard.revenueFilters.contractTypes : [];
        const expenseCategories = Array.isArray(dashboard?.expenseFilters?.categories) ? dashboard.expenseFilters.categories : [];
        const expenseClients = Array.isArray(dashboard?.expenseFilters?.clients) ? dashboard.expenseFilters.clients : [];
        const expenseVerticals = Array.isArray(dashboard?.expenseFilters?.verticals) ? dashboard.expenseFilters.verticals : [];
        const expenseContractTypes = Array.isArray(dashboard?.expenseFilters?.contractTypes) ? dashboard.expenseFilters.contractTypes : [];
        state.ytdRevenueCategoryKeys = new Set(revenueCategories.map(item => item?.key).filter(Boolean));
        state.ytdRevenueClientKeys = new Set(revenueClients.map(item => item?.key).filter(Boolean));
        state.ytdRevenueVerticalKeys = new Set(revenueVerticals.map(item => item?.key).filter(Boolean));
        state.ytdRevenueContractTypeKeys = new Set(revenueContractTypes.map(item => item?.key).filter(Boolean));
        state.ytdExpenseCategoryKeys = new Set(expenseCategories.map(item => item?.key).filter(Boolean));
        state.ytdExpenseClientKeys = new Set(expenseClients.map(item => item?.key).filter(Boolean));
        state.ytdExpenseVerticalKeys = new Set(expenseVerticals.map(item => item?.key).filter(Boolean));
        state.ytdExpenseContractTypeKeys = new Set(expenseContractTypes.map(item => item?.key).filter(Boolean));
        state.ytdRevenueBreakdown = "global";
        state.ytdExpenseBreakdown = "global";
        syncYtdBreakdownControls();
    }

    function renderYtdDashboard(dashboard) {
        const charts = Array.isArray(dashboard?.charts) ? dashboard.charts : [];
        const chart = dashboard?.chart || charts.find(item => item?.key === "total");
        renderYtdFilters(dashboard);
        renderYtdChart(ytdTotalChart, chart);
        renderYtdReconciliation(dashboard);
    }

    function renderYtdFilters(dashboard) {
        renderYtdDropdownFilter(
            ytdRevenueCategoryFilters,
            "Categorias de ingresos",
            dashboard?.revenueFilters?.categories,
            state.ytdRevenueCategoryKeys,
            "revenue-category");
        renderYtdDropdownFilter(
            ytdRevenueClientFilters,
            "Clientes de ingresos",
            dashboard?.revenueFilters?.clients,
            state.ytdRevenueClientKeys,
            "revenue-client");
        renderYtdDropdownFilter(
            ytdRevenueVerticalFilters,
            "Vertical de ingresos",
            dashboard?.revenueFilters?.verticals,
            state.ytdRevenueVerticalKeys,
            "revenue-vertical");
        renderYtdDropdownFilter(
            ytdRevenueContractTypeFilters,
            "Tipo contrato de ingresos",
            dashboard?.revenueFilters?.contractTypes,
            state.ytdRevenueContractTypeKeys,
            "revenue-contract-type");
        renderYtdDropdownFilter(
            ytdExpenseCategoryFilters,
            "Categorias de gastos",
            dashboard?.expenseFilters?.categories,
            state.ytdExpenseCategoryKeys,
            "expense-category");
        renderYtdDropdownFilter(
            ytdExpenseClientFilters,
            "Clientes en gastos",
            dashboard?.expenseFilters?.clients,
            state.ytdExpenseClientKeys,
            "expense-client");
        renderYtdDropdownFilter(
            ytdExpenseVerticalFilters,
            "Vertical gastos",
            dashboard?.expenseFilters?.verticals,
            state.ytdExpenseVerticalKeys,
            "expense-vertical");
        renderYtdDropdownFilter(
            ytdExpenseContractTypeFilters,
            "Tipo contrato gastos",
            dashboard?.expenseFilters?.contractTypes,
            state.ytdExpenseContractTypeKeys,
            "expense-contract-type");
    }

    function renderYtdDropdownFilter(container, title, options, selectedKeys, filterKey) {
        if (!container) {
            return;
        }

        const items = Array.isArray(options) ? options.filter(item => item?.key) : [];
        if (!items.length) {
            container.innerHTML = `
                <div class="ytd-filter-dropdown ytd-filter-dropdown--empty">
                    <button type="button" class="ytd-filter-dropdown__toggle" disabled>
                        <span>${escapeHtml(title)}</span>
                        <strong>Sin opciones</strong>
                    </button>
                </div>
            `;
            return;
        }

        const selectedCount = items.filter(item => selectedKeys?.has(String(item.key || ""))).length;
        const summary = selectedCount === items.length
            ? "Todos"
            : selectedCount === 0
                ? "Ninguno"
                : `${selectedCount}/${items.length}`;
        const listHtml = items.map(item => {
            const key = String(item.key || "");
            const checked = selectedKeys?.has(key) ? "checked" : "";
            const total = Number(item.total || 0);
            return `
                <label class="ytd-filter-option" title="${escapeHtml(item.label || key)}" data-ytd-filter-option data-ytd-search-text="${escapeHtml(`${item.label || key} ${currencyFormatter.format(total)}`.toLowerCase())}">
                    <input type="checkbox" data-ytd-filter="${escapeHtml(filterKey)}" data-ytd-key="${escapeHtml(key)}" ${checked}>
                    <span>${escapeHtml(item.label || key)}</span>
                    <small>${escapeHtml(currencyFormatter.format(total))}</small>
                </label>
            `;
        }).join("");

        container.innerHTML = `
            <div class="ytd-filter-dropdown" data-ytd-dropdown data-ytd-filter-key="${escapeHtml(filterKey)}">
                <button type="button" class="ytd-filter-dropdown__toggle" data-ytd-dropdown-toggle>
                    <span>${escapeHtml(title)}</span>
                    <strong data-ytd-dropdown-summary>${escapeHtml(summary)}</strong>
                </button>
                <div class="ytd-filter-dropdown__panel" data-ytd-dropdown-panel hidden>
                    <input type="search" class="ytd-filter-dropdown__search" data-ytd-filter-search placeholder="Buscar..." autocomplete="off">
                    <div class="ytd-filter-dropdown__actions">
                        <button type="button" data-ytd-filter-action="all">Todos</button>
                        <button type="button" data-ytd-filter-action="none">Ninguno</button>
                    </div>
                    <div class="ytd-filter-dropdown__list">
                        ${listHtml}
                    </div>
                </div>
            </div>
        `;
    }

    function syncYtdBreakdownControls() {
        ytdRevenueBreakdown?.querySelectorAll('input[name="ytdRevenueBreakdown"]').forEach(input => {
            input.checked = input.value === state.ytdRevenueBreakdown;
        });
        ytdExpenseBreakdown?.querySelectorAll('input[name="ytdExpenseBreakdown"]').forEach(input => {
            input.checked = input.value === state.ytdExpenseBreakdown;
        });
    }

    function getYtdFilterOptions(filterKey) {
        const dashboard = state.ytdDashboard;
        if (filterKey === "revenue-category") {
            return Array.isArray(dashboard?.revenueFilters?.categories) ? dashboard.revenueFilters.categories : [];
        }
        if (filterKey === "revenue-client") {
            return Array.isArray(dashboard?.revenueFilters?.clients) ? dashboard.revenueFilters.clients : [];
        }
        if (filterKey === "revenue-vertical") {
            return Array.isArray(dashboard?.revenueFilters?.verticals) ? dashboard.revenueFilters.verticals : [];
        }
        if (filterKey === "revenue-contract-type") {
            return Array.isArray(dashboard?.revenueFilters?.contractTypes) ? dashboard.revenueFilters.contractTypes : [];
        }
        if (filterKey === "expense-category") {
            return Array.isArray(dashboard?.expenseFilters?.categories) ? dashboard.expenseFilters.categories : [];
        }
        if (filterKey === "expense-client") {
            return Array.isArray(dashboard?.expenseFilters?.clients) ? dashboard.expenseFilters.clients : [];
        }
        if (filterKey === "expense-vertical") {
            return Array.isArray(dashboard?.expenseFilters?.verticals) ? dashboard.expenseFilters.verticals : [];
        }
        if (filterKey === "expense-contract-type") {
            return Array.isArray(dashboard?.expenseFilters?.contractTypes) ? dashboard.expenseFilters.contractTypes : [];
        }

        return [];
    }

    function getYtdSelectedSet(filterKey) {
        if (filterKey === "revenue-category") {
            return state.ytdRevenueCategoryKeys;
        }
        if (filterKey === "revenue-client") {
            return state.ytdRevenueClientKeys;
        }
        if (filterKey === "revenue-vertical") {
            return state.ytdRevenueVerticalKeys;
        }
        if (filterKey === "revenue-contract-type") {
            return state.ytdRevenueContractTypeKeys;
        }
        if (filterKey === "expense-category") {
            return state.ytdExpenseCategoryKeys;
        }
        if (filterKey === "expense-client") {
            return state.ytdExpenseClientKeys;
        }
        if (filterKey === "expense-vertical") {
            return state.ytdExpenseVerticalKeys;
        }
        if (filterKey === "expense-contract-type") {
            return state.ytdExpenseContractTypeKeys;
        }

        return null;
    }

    function isYtdFilterNarrowed(filterKey) {
        const selectedSet = getYtdSelectedSet(filterKey);
        const optionKeys = getYtdFilterOptions(filterKey)
            .map(item => String(item?.key || ""))
            .filter(Boolean);
        if (!selectedSet || !optionKeys.length) {
            return false;
        }

        return optionKeys.some(key => !selectedSet.has(key));
    }

    function isYtdDimensionSelected(key, filterKey) {
        const normalizedKey = String(key || "");
        const selectedSet = getYtdSelectedSet(filterKey);
        if (!selectedSet) {
            return true;
        }

        if (normalizedKey) {
            return selectedSet.has(normalizedKey);
        }

        return !isYtdFilterNarrowed(filterKey);
    }

    function syncYtdDropdownSummary(dropdown) {
        if (!dropdown) {
            return;
        }

        const filterKey = dropdown.dataset.ytdFilterKey || "";
        const selectedSet = getYtdSelectedSet(filterKey);
        const options = getYtdFilterOptions(filterKey).filter(item => item?.key);
        const summaryTarget = dropdown.querySelector("[data-ytd-dropdown-summary]");
        if (!selectedSet || !summaryTarget || !options.length) {
            return;
        }

        const selectedCount = options.filter(item => selectedSet.has(String(item.key || ""))).length;
        summaryTarget.textContent = selectedCount === options.length
            ? "Todos"
            : selectedCount === 0
                ? "Ninguno"
                : `${selectedCount}/${options.length}`;
    }

    function setYtdDropdownOpen(dropdown, open) {
        if (!dropdown) {
            return;
        }

        dropdown.classList.toggle("is-open", open);
        const panel = dropdown.querySelector("[data-ytd-dropdown-panel]");
        if (panel) {
            panel.hidden = !open;
        }
    }

    function closeYtdDropdowns(exceptDropdown = null) {
        document.querySelectorAll("[data-ytd-dropdown].is-open").forEach(dropdown => {
            if (dropdown !== exceptDropdown) {
                setYtdDropdownOpen(dropdown, false);
            }
        });
    }

    function applyYtdDropdownSearch(input) {
        const dropdown = input?.closest("[data-ytd-dropdown]");
        if (!dropdown) {
            return;
        }

        const query = (input.value || "").trim().toLowerCase();
        dropdown.querySelectorAll("[data-ytd-filter-option]").forEach(option => {
            const haystack = option.dataset.ytdSearchText || "";
            option.hidden = Boolean(query) && !haystack.includes(query);
        });
    }

    function updateYtdFilterSelection(filterKey, key, checked) {
        const targetSet = getYtdSelectedSet(filterKey);
        if (!targetSet || !key) {
            return;
        }

        if (checked) {
            targetSet.add(key);
        } else {
            targetSet.delete(key);
        }

        renderYtdChart(ytdTotalChart, state.ytdDashboard?.chart || state.ytdDashboard?.charts?.find(chart => chart?.key === "total"));
    }

    function setYtdFilterSelection(filterKey, mode) {
        const targetSet = getYtdSelectedSet(filterKey);
        if (!targetSet) {
            return;
        }

        targetSet.clear();
        if (mode === "all") {
            getYtdFilterOptions(filterKey).forEach(item => {
                if (item?.key) {
                    targetSet.add(String(item.key));
                }
            });
        }

        renderYtdFilters(state.ytdDashboard);
        renderYtdChart(ytdTotalChart, state.ytdDashboard?.chart || state.ytdDashboard?.charts?.find(chart => chart?.key === "total"));
    }

    function renderYtdReconciliation(dashboard) {
        if (!ytdReconciliationDisclaimer) {
            return;
        }

        const reconciliation = dashboard?.licensingReconciliation;
        if (!reconciliation?.disclaimer) {
            ytdReconciliationDisclaimer.hidden = true;
            ytdReconciliationDisclaimer.innerHTML = "";
            return;
        }

        const months = Array.isArray(reconciliation.months) ? reconciliation.months : [];
        const monthHtml = months.length
            ? `<div class="ytd-disclaimer__months">${months.map(month => `
                <span>
                    ${escapeHtml(month.label || "")}
                    <strong>${escapeHtml(currencyFormatter.format(Number(month.difference || 0)))}</strong>
                    <small>${escapeHtml(formatPercent(month.differencePercent || 0))}</small>
                </span>
            `).join("")}</div>`
            : "";

        ytdReconciliationDisclaimer.hidden = false;
        ytdReconciliationDisclaimer.innerHTML = `
            <p>${escapeHtml(reconciliation.disclaimer)}</p>
            ${monthHtml}
        `;
    }

    function renderYtdChart(container, chart) {
        if (!container) {
            return;
        }

        if (!chart) {
            container.innerHTML = '<div class="ytd-empty">No hay datos disponibles.</div>';
            return;
        }

        const view = buildYtdChartView(chart);
        state.ytdSegmentDetails = {};
        state.ytdSegmentDetailCounter = 0;
        container.innerHTML = `
            <div class="ytd-chart-card__header">
                <div>
                    <div class="ytd-chart-card__eyebrow">YTD</div>
                    <h2 class="ytd-chart-card__title">${escapeHtml(chart.title || "")}</h2>
                    <p class="ytd-chart-card__subtitle">${escapeHtml(chart.subtitle || "")}</p>
                </div>
                <div class="ytd-chart-card__totals">
                    <span><strong>${escapeHtml(currencyFormatter.format(view.totalSales))}</strong> ingresos</span>
                    <span><strong>${escapeHtml(currencyFormatter.format(view.totalExpenses))}</strong> gastos</span>
                    <span><strong>${escapeHtml(currencyFormatter.format(view.totalUtility))}</strong> utilidad</span>
                    <span class="ytd-chart-card__accumulated"><strong>${escapeHtml(currencyFormatter.format(view.totalUtility))}</strong> Acumulado</span>
                </div>
            </div>
            <div class="ytd-chart-card__legend" aria-hidden="true">
                <span><i class="ytd-chart-card__swatch ytd-chart-card__swatch--sales"></i>Ingresos totales</span>
                <span><i class="ytd-chart-card__swatch ytd-chart-card__swatch--expenses"></i>Gastos totales</span>
                <span><i class="ytd-chart-card__swatch ytd-chart-card__swatch--utility"></i>Utilidad</span>
            </div>
            <div class="ytd-chart-card__plot">
                ${buildYtdChartSvg(view)}
                <div class="ytd-chart-tooltip" role="tooltip" hidden></div>
            </div>
        `;

        wireYtdChartTooltip(container);
    }

    function buildYtdChartView(chart) {
        const points = Array.isArray(chart?.points) ? chart.points : [];
        const viewPoints = points.map(point => {
            const revenueSegments = (Array.isArray(point?.revenueSegments) ? point.revenueSegments : [])
                .filter(segment => {
                    const categoryKey = String(segment?.categoryKey || "");
                    const clientKey = String(segment?.clientKey || "");
                    const verticalKey = String(segment?.verticalKey || "");
                    const contractTypeKey = String(segment?.contractTypeKey || "");
                    const categorySelected = isYtdDimensionSelected(categoryKey, "revenue-category");
                    const clientSelected = isYtdDimensionSelected(clientKey, "revenue-client");
                    const verticalSelected = isYtdDimensionSelected(verticalKey, "revenue-vertical");
                    const contractTypeSelected = isYtdDimensionSelected(contractTypeKey, "revenue-contract-type");
                    return categorySelected && clientSelected && verticalSelected && contractTypeSelected;
                });
            const expenseSegments = (Array.isArray(point?.expenseSegments) ? point.expenseSegments : [])
                .filter(segment => {
                    return isYtdExpenseSegmentVisible(segment);
                });
            const sales = roundYtdValue(revenueSegments.reduce((sum, segment) => sum + Number(segment?.value || 0), 0));
            const expenses = roundYtdValue(expenseSegments.reduce((sum, segment) => sum + Number(segment?.value || 0), 0));

            return {
                key: point?.key || "",
                label: point?.label || "",
                month: Number(point?.month || 0),
                sales,
                expenses,
                utility: roundYtdValue(sales - expenses),
                revenueStacks: groupYtdSegments(revenueSegments, state.ytdRevenueBreakdown, "revenue"),
                expenseStacks: groupYtdSegments(expenseSegments, state.ytdExpenseBreakdown, "expense")
            };
        });

        return {
            key: chart?.key || "total",
            title: chart?.title || "YTD",
            hasData: viewPoints.some(point =>
                Math.abs(point.sales) >= 0.01
                || Math.abs(point.expenses) >= 0.01
                || Math.abs(point.utility) >= 0.01),
            totalSales: roundYtdValue(viewPoints.reduce((sum, point) => sum + point.sales, 0)),
            totalExpenses: roundYtdValue(viewPoints.reduce((sum, point) => sum + point.expenses, 0)),
            totalUtility: roundYtdValue(viewPoints.reduce((sum, point) => sum + point.utility, 0)),
            points: viewPoints
        };
    }

    function groupYtdSegments(segments, mode, segmentType) {
        if (mode === "global") {
            const value = roundYtdValue(segments.reduce((sum, segment) => sum + Number(segment?.value || 0), 0));
            const records = segments.flatMap(segment => Array.isArray(segment?.records) ? segment.records : []);
            return Math.abs(value) < 0.01
                ? []
                : [{
                    key: `${segmentType}:global`,
                    label: segmentType === "revenue" ? "Ingresos totales" : "Gastos totales",
                    value,
                    recordsCount: segments.reduce((sum, segment) => sum + Number(segment?.recordsCount || 0), 0),
                    records
                }];
        }

        const grouped = new Map();
        segments.forEach(segment => {
            const key = resolveYtdSegmentGroupKey(segment, mode);
            const label = resolveYtdSegmentGroupLabel(segment, mode, segmentType);
            const current = grouped.get(key) || {
                key,
                label,
                value: 0,
                recordsCount: 0,
                records: []
            };
            current.value += Number(segment?.value || 0);
            current.recordsCount += Number(segment?.recordsCount || 0);
            current.records.push(...(Array.isArray(segment?.records) ? segment.records : []));
            grouped.set(key, current);
        });

        return Array.from(grouped.values())
            .map(item => ({
                ...item,
                value: roundYtdValue(item.value)
            }))
            .filter(item => Math.abs(item.value) >= 0.01)
            .sort((left, right) => Math.abs(right.value) - Math.abs(left.value) || left.label.localeCompare(right.label, "es"));
    }

    function resolveYtdSegmentGroupKey(segment, mode) {
        if (mode === "vertical") {
            return String(segment?.verticalKey || "sin-vertical");
        }
        if (mode === "category") {
            return String(segment?.categoryKey || "sin-categoria");
        }
        if (mode === "contractType") {
            return String(segment?.contractTypeKey || "sin-tipo-contrato");
        }
        return String(segment?.clientKey || "sin-cliente");
    }

    function resolveYtdSegmentGroupLabel(segment, mode, segmentType) {
        if (mode === "vertical") {
            return segment?.verticalLabel || "Sin vertical";
        }
        if (mode === "category") {
            return segment?.categoryLabel || "Sin categoria";
        }
        if (mode === "contractType") {
            return segment?.contractTypeLabel || (segmentType === "expense" ? "Otros gastos" : "Sin contrato");
        }
        if (segmentType === "expense" && !segment?.clientKey) {
            return "Otros gastos";
        }
        return segment?.clientLabel || "Sin cliente";
    }

    function roundYtdValue(value) {
        return Math.round(Number(value || 0) * 100) / 100;
    }

    function buildYtdChartSvg(chart) {
        const points = Array.isArray(chart?.points) ? chart.points : [];
        if (!points.length || !chart?.hasData) {
            return '<div class="ytd-empty">No hay movimientos para este corte.</div>';
        }

        const width = 1180;
        const height = 430;
        const margin = { left: 86, right: 34, top: 46, bottom: 62 };
        const plotWidth = width - margin.left - margin.right;
        const plotHeight = height - margin.top - margin.bottom;
        const numericPoints = points;
        const values = numericPoints.flatMap(point => [point.sales, point.expenses, point.utility, 0]);
        let minValue = Math.min(...values);
        let maxValue = Math.max(...values);
        if (Math.abs(maxValue - minValue) < 0.01) {
            maxValue += 1;
            minValue -= 1;
        }

        const padding = Math.max((maxValue - minValue) * 0.08, 1);
        maxValue += padding;
        minValue -= padding;

        const yFor = value => margin.top + ((maxValue - value) / (maxValue - minValue)) * plotHeight;
        const zeroY = yFor(0);
        const xStep = plotWidth / numericPoints.length;
        const barWidth = Math.min(30, Math.max(12, xStep * 0.22));
        const monthLabelEvery = numericPoints.length > 9 ? 2 : 1;
        const revenuePalette = ["#2563eb", "#0891b2", "#2b88d8", "#0078d4", "#60a5fa", "#0f6cbd", "#4f6bed", "#38bdf8"];
        const expensePalette = ["#64748b", "#8a8886", "#a4262c", "#ca5010", "#7a7574", "#605e5c", "#b45309", "#475569"];
        const revenueStackKind = buildYtdStackKindLabel("Ingresos", state.ytdRevenueBreakdown);
        const expenseStackKind = buildYtdStackKindLabel("Gastos", state.ytdExpenseBreakdown);

        const gridLines = Array.from({ length: 5 }, (_, index) => {
            const ratio = index / 4;
            const value = maxValue - ((maxValue - minValue) * ratio);
            const y = yFor(value);
            return `
                <line class="ytd-chart__grid" x1="${margin.left}" y1="${y.toFixed(2)}" x2="${width - margin.right}" y2="${y.toFixed(2)}"></line>
                <text class="ytd-chart__axis" x="${margin.left - 12}" y="${(y + 4).toFixed(2)}" text-anchor="end">${escapeHtml(formatCompactMillions(value))}</text>
            `;
        }).join("");

        const renderStackedBar = (point, index, stacks, offset, palette, stackKind) => {
            const centerX = margin.left + (index * xStep) + (xStep / 2);
            const x = centerX + offset;
            let running = 0;
            return stacks.map(segment => {
                const start = running;
                running += Number(segment.value || 0);
                const yStart = yFor(start);
                const yEnd = yFor(running);
                const y = Math.min(yStart, yEnd);
                const h = Math.max(1, Math.abs(yEnd - yStart));
                const color = getYtdSegmentColor(segment.key, palette);
                return `<rect class="ytd-chart__bar" x="${x.toFixed(2)}" y="${y.toFixed(2)}" width="${barWidth.toFixed(2)}" height="${h.toFixed(2)}" rx="4" fill="${color}" ${buildYtdTooltipAttrs(chart, point, segment, stackKind)}></rect>`;
            }).join("");
        };

        const bars = numericPoints.map((point, index) => [
            renderStackedBar(point, index, point.revenueStacks, -barWidth - 3, revenuePalette, revenueStackKind),
            renderStackedBar(point, index, point.expenseStacks, 3, expensePalette, expenseStackKind)
        ].join("")).join("");

        const renderBarTotalLabel = (point, index, value, offset, tone) => {
            if (Math.abs(Number(value || 0)) < 0.01) {
                return "";
            }

            const centerX = margin.left + (index * xStep) + (xStep / 2);
            const x = centerX + offset + (barWidth / 2);
            const yValue = yFor(value);
            const y = value >= 0
                ? Math.max(16, yValue - 8)
                : Math.min(height - margin.bottom - 4, yValue + 16);
            return `<text class="ytd-chart__bar-total ytd-chart__bar-total--${escapeHtml(tone)}" x="${x.toFixed(2)}" y="${y.toFixed(2)}" text-anchor="middle">${escapeHtml(formatCompactMillions(value))}</text>`;
        };

        const barTotalLabels = numericPoints.map((point, index) => [
            renderBarTotalLabel(point, index, point.sales, -barWidth - 3, "sales"),
            renderBarTotalLabel(point, index, point.expenses, 3, "expenses")
        ].join("")).join("");

        const utilityPath = numericPoints.map((point, index) => {
            const centerX = margin.left + (index * xStep) + (xStep / 2);
            const y = yFor(point.utility);
            return `${index === 0 ? "M" : "L"} ${centerX.toFixed(2)} ${y.toFixed(2)}`;
        }).join(" ");
        const utilityPoints = numericPoints.map((point, index) => {
            const centerX = margin.left + (index * xStep) + (xStep / 2);
            const y = yFor(point.utility);
            return `<circle class="ytd-chart__point" cx="${centerX.toFixed(2)}" cy="${y.toFixed(2)}" r="5" ${buildYtdTooltipAttrs(chart, point)}></circle>`;
        }).join("");

        const labels = numericPoints.map((point, index) => {
            const centerX = margin.left + (index * xStep) + (xStep / 2);
            const label = (point.label || "").split(" ")[0].slice(0, 3);
            const visible = index % monthLabelEvery === 0 || index === numericPoints.length - 1;
            return visible
                ? `<text class="ytd-chart__month" x="${centerX.toFixed(2)}" y="${height - 24}" text-anchor="middle">${escapeHtml(label)}</text>`
                : "";
        }).join("");

        const hitboxes = numericPoints.map((point, index) => {
            const x = margin.left + (index * xStep);
            return `<rect class="ytd-chart__hitbox" x="${x.toFixed(2)}" y="${margin.top}" width="${xStep.toFixed(2)}" height="${plotHeight}" ${buildYtdTooltipAttrs(chart, point)}></rect>`;
        }).join("");

        return `
            <svg class="ytd-chart" viewBox="0 0 ${width} ${height}" role="img" aria-label="${escapeHtml(chart.title || "YTD")}">
                <rect class="ytd-chart__plot-bg" x="${margin.left}" y="${margin.top}" width="${plotWidth}" height="${plotHeight}"></rect>
                ${gridLines}
                <line class="ytd-chart__zero" x1="${margin.left}" y1="${zeroY.toFixed(2)}" x2="${width - margin.right}" y2="${zeroY.toFixed(2)}"></line>
                ${hitboxes}
                ${bars}
                ${barTotalLabels}
                <path class="ytd-chart__line" d="${utilityPath}"></path>
                ${utilityPoints}
                ${labels}
            </svg>
        `;
    }

    function buildYtdStackKindLabel(prefix, mode) {
        if (mode === "vertical") {
            return `${prefix} por vertical`;
        }
        if (mode === "category") {
            return `${prefix} por categoria`;
        }
        if (mode === "client") {
            return `${prefix} por cliente`;
        }
        if (mode === "contractType") {
            return `${prefix} por tipo de contrato`;
        }

        return `${prefix} global`;
    }

    function getYtdSegmentColor(key, palette) {
        const source = String(key || "");
        let hash = 0;
        for (let index = 0; index < source.length; index += 1) {
            hash = ((hash << 5) - hash) + source.charCodeAt(index);
            hash |= 0;
        }

        return palette[Math.abs(hash) % palette.length];
    }

    function buildYtdTooltipAttrs(chart, point, segment, stackKind) {
        let detailKey = "";
        if (segment) {
            detailKey = `ytd-detail-${state.ytdSegmentDetailCounter += 1}`;
            const segmentType = String(segment?.key || "").startsWith("revenue")
                ? "revenue"
                : String(segment?.key || "").startsWith("expense")
                    ? "expense"
                    : "";
            state.ytdSegmentDetails[detailKey] = {
                title: `${stackKind || "Segmento"} - ${segment.label || ""}`,
                subtitle: `${point?.label || ""} · ${currencyFormatter.format(Number(segment.value || 0))}`,
                segmentType,
                segmentKind: stackKind || "",
                segmentLabel: segment.label || "",
                pointLabel: point?.label || "",
                value: Number(segment.value || 0),
                records: Array.isArray(segment.records) ? segment.records : []
            };
        }

        return [
            `data-ytd-title="${escapeHtml(`${chart?.title || ""} - ${point?.label || ""}`)}"`,
            `data-ytd-sales="${Number(point?.sales || 0)}"`,
            `data-ytd-expenses="${Number(point?.expenses || 0)}"`,
            `data-ytd-utility="${Number(point?.utility || 0)}"`,
            segment ? `data-ytd-segment-kind="${escapeHtml(stackKind || "")}"` : "",
            segment ? `data-ytd-segment-label="${escapeHtml(segment.label || "")}"` : "",
            segment ? `data-ytd-segment-value="${Number(segment.value || 0)}"` : "",
            detailKey ? `data-ytd-detail-key="${escapeHtml(detailKey)}"` : ""
        ].join(" ");
    }

    function isYtdDetailOpen() {
        return Boolean(ytdDetailModal && !ytdDetailModal.hidden);
    }

    function closeYtdDetailModal() {
        if (!ytdDetailModal) {
            return;
        }

        ytdDetailModal.hidden = true;
        document.body.classList.remove("dashboard-modal-open");
        if (ytdDetailTitle) {
            ytdDetailTitle.textContent = "Detalle del segmento";
        }
        if (ytdDetailSubtitle) {
            ytdDetailSubtitle.textContent = "Registros que componen la seccion seleccionada.";
        }
        if (ytdDetailSummary) {
            ytdDetailSummary.innerHTML = "";
        }
        setStatus(ytdDetailStatus, "", "");
        if (ytdDetailSelectAll) {
            ytdDetailSelectAll.checked = false;
            ytdDetailSelectAll.indeterminate = false;
        }
        if (ytdDetailBody) {
            ytdDetailBody.innerHTML = '<tr><td colspan="15" class="dashboard-table__empty">Haz click sobre una seccion de una barra YTD para ver sus registros.</td></tr>';
        }
        if (ytdDetailFoot) {
            ytdDetailFoot.innerHTML = "";
            ytdDetailFoot.dataset.segmentValue = "";
        }
        if (ytdDetailTotals) {
            ytdDetailTotals.innerHTML = "";
        }
        updateYtdDetailToolbarState();
    }

    function getYtdEditorOptions(groupKey) {
        const options = state.ytdDashboard?.editorOptions || {};
        const values = options?.[groupKey];
        return Array.isArray(values) ? values : [];
    }

    function renderYtdStaticCell(value, fallback = "-") {
        return `<span class="dashboard-pnl-detail__static">${escapeHtml(value || fallback)}</span>`;
    }

    function renderYtdOptionEditor(record, options, fieldKey, currentValue, currentLabel, editable) {
        if (!editable) {
            return renderYtdStaticCell(currentLabel);
        }

        const normalizedOptions = buildPnlDetailSelectOptions(
            options,
            String(currentValue ?? ""),
            currentLabel || "",
            currentValue,
            true
        );

        return `
            <select class="form-select form-select-sm dashboard-select dashboard-select--detail" data-ytd-edit-field="${escapeHtml(fieldKey)}">
                ${normalizedOptions.map(option => {
                    const value = option?.value ?? "";
                    return `
                        <option value="${escapeHtml(String(value))}" ${Number(value) === Number(currentValue) ? "selected" : ""}>
                            ${escapeHtml(option?.label || option?.key || "")}
                        </option>
                    `;
                }).join("")}
            </select>
        `;
    }

    function renderYtdAllocationEditor(record, fieldKey) {
        const numericValue = Number(record?.[fieldKey] || 0);
        if (!record?.canEditAllocation) {
            return renderYtdStaticCell(numericValue ? currencyFormatter.format(numericValue) : "-");
        }

        return `
            <input
                type="number"
                step="0.01"
                class="form-control form-control-sm dashboard-detail-number-input"
                data-ytd-edit-field="${escapeHtml(fieldKey)}"
                value="${escapeHtml(formatEditableDecimalValue(numericValue))}" />
        `;
    }

    function renderYtdCategoryEditor(record) {
        if (record?.sourceType !== "expense") {
            return renderYtdStaticCell(record?.categoryLabel || "Facturacion");
        }

        return renderYtdOptionEditor(
            record,
            getYtdEditorOptions("expenseCategories"),
            "category",
            record?.categoryOptionValue,
            record?.categoryLabel || "",
            Boolean(record?.canEditCategory)
        );
    }

    function renderYtdVerticalEditor(record) {
        if (record?.sourceType !== "billing") {
            return renderYtdStaticCell(record?.verticalLabel || "Sin vertical");
        }

        return renderYtdOptionEditor(
            record,
            getYtdEditorOptions("billingVerticals"),
            "vertical",
            record?.verticalOptionValue,
            record?.verticalLabel || "",
            Boolean(record?.canEditVertical)
        );
    }

    function renderYtdContractTypeEditor(record) {
        if (record?.sourceType === "billing") {
            return renderYtdOptionEditor(
                record,
                getYtdEditorOptions("billingContractTypes"),
                "contractType",
                record?.contractTypeOptionValue,
                record?.contractTypeLabel || "",
                Boolean(record?.canEditContractType)
            );
        }

        if (record?.sourceType === "expense") {
            return renderYtdOptionEditor(
                record,
                getYtdEditorOptions("expenseContractTypes"),
                "contractType",
                record?.contractTypeOptionValue,
                record?.contractTypeLabel || "",
                Boolean(record?.canEditContractType)
            );
        }

        return renderYtdStaticCell(record?.contractTypeLabel || "No aplica");
    }

    function canEditYtdRecord(record) {
        return Boolean(record?.canEditCategory
            || record?.canEditVertical
            || record?.canEditAllocation
            || record?.canEditContractType);
    }

    function renderYtdBulkSelect(select, options, placeholder) {
        if (!select) {
            return;
        }

        const items = Array.isArray(options) ? options : [];
        select.innerHTML = `
            <option value="">${escapeHtml(placeholder)}</option>
            ${items.map(option => `
                <option value="${escapeHtml(String(option?.value ?? ""))}">${escapeHtml(option?.label || option?.key || "")}</option>
            `).join("")}
        `;
    }

    function renderYtdBulkControls() {
        renderYtdBulkSelect(ytdBulkCategorySelect, getYtdEditorOptions("expenseCategories"), "Categoria gastos");
        renderYtdBulkSelect(ytdBulkBillingVerticalSelect, getYtdEditorOptions("billingVerticals"), "Vertical facturacion");
        renderYtdBulkSelect(ytdBulkBillingContractSelect, getYtdEditorOptions("billingContractTypes"), "Contrato facturacion");
        renderYtdBulkSelect(ytdBulkExpenseContractSelect, getYtdEditorOptions("expenseContractTypes"), "Contrato gastos XCB");
        if (ytdBulkCloudInput) {
            ytdBulkCloudInput.value = "";
        }
        if (ytdBulkCopiersInput) {
            ytdBulkCopiersInput.value = "";
        }
    }

    function getYtdDetailRows() {
        return Array.from(ytdDetailBody?.querySelectorAll("[data-ytd-record-key]") || []);
    }

    function getSelectedYtdDetailRows() {
        return getYtdDetailRows().filter(row => row.querySelector("[data-ytd-row-select]")?.checked);
    }

    function updateYtdDetailToolbarState() {
        const rows = getYtdDetailRows();
        const selectedRows = getSelectedYtdDetailRows();
        let dirtyCount = 0;

        rows.forEach(row => {
            const patch = buildYtdRowPatch(row, { silent: true });
            const isDirty = Boolean(patch);
            row.classList.toggle("is-dirty", isDirty);
            if (isDirty) {
                dirtyCount += 1;
            }
        });

        if (ytdDetailSelectedCount) {
            ytdDetailSelectedCount.textContent = `${numberFormatter.format(selectedRows.length)} seleccionados`;
        }
        if (ytdDetailDirtyCount) {
            ytdDetailDirtyCount.textContent = `${numberFormatter.format(dirtyCount)} cambios pendientes`;
        }
        if (ytdBulkSaveButton) {
            ytdBulkSaveButton.disabled = dirtyCount === 0 || state.ytdBulkSaving;
        }
        if (ytdDetailSelectAll) {
            ytdDetailSelectAll.checked = rows.length > 0 && selectedRows.length === rows.length;
            ytdDetailSelectAll.indeterminate = selectedRows.length > 0 && selectedRows.length < rows.length;
        }
        updateYtdDetailFooterFromRows();
    }

    function isYtdDetailRecordVisible(record, detail) {
        const segmentType = detail?.segmentType || (record?.sourceType === "billing" ? "revenue" : "expense");
        if (segmentType === "revenue") {
            return isYtdDimensionSelected(record?.clientKey, "revenue-client")
                && isYtdDimensionSelected(record?.verticalKey, "revenue-vertical")
                && isYtdDimensionSelected(record?.contractTypeKey, "revenue-contract-type");
        }

        return isYtdExpenseRecordVisible(record);
    }

    function isYtdLicensingExpenseCategory(categoryKey, categoryLabel) {
        const key = normalizeText(categoryKey);
        const label = normalizeText(categoryLabel);
        return key === "licensing" || label.includes("licenciamiento");
    }

    function isYtdExpenseSegmentVisible(segment) {
        const categoryKey = String(segment?.categoryKey || "");
        const categoryLabel = String(segment?.categoryLabel || "");
        const verticalKey = String(segment?.verticalKey || "");
        const categorySelected = isYtdDimensionSelected(categoryKey, "expense-category");
        const verticalSelected = isYtdDimensionSelected(verticalKey, "expense-vertical");
        if (!categorySelected || !verticalSelected) {
            return false;
        }

        if (!isYtdLicensingExpenseCategory(categoryKey, categoryLabel)) {
            return true;
        }

        return isYtdDimensionSelected(segment?.clientKey, "expense-client")
            && isYtdDimensionSelected(segment?.contractTypeKey, "expense-contract-type");
    }

    function isYtdExpenseRecordVisible(record) {
        const categoryKey = String(record?.categoryKey || "");
        const categoryLabel = String(record?.categoryLabel || "");
        return isYtdDimensionSelected(record?.categoryKey, "expense-category")
            && isYtdDimensionSelected(record?.verticalKey, "expense-vertical")
            && (!isYtdLicensingExpenseCategory(categoryKey, categoryLabel)
                || (isYtdDimensionSelected(record?.clientKey, "expense-client")
                    && isYtdDimensionSelected(record?.contractTypeKey, "expense-contract-type")));
    }

    function readYtdDetailRowNumber(row, record, fieldKey) {
        const input = row?.querySelector(`[data-ytd-edit-field='${fieldKey}']`);
        if (input) {
            const value = parseEditableDecimalValue(input.value);
            return Number.isFinite(value) ? value : 0;
        }

        return Number(record?.[fieldKey] || 0);
    }

    function updateYtdDetailFooterFromRows() {
        if (!ytdDetailFoot) {
            return;
        }

        const rows = getYtdDetailRows();
        if (!rows.length) {
            ytdDetailFoot.innerHTML = "";
            return;
        }

        let valueTotal = 0;
        let cloudTotal = 0;
        let copiersTotal = 0;
        rows.forEach(row => {
            const record = state.ytdDetailRecords?.[row.dataset.ytdRecordKey || ""];
            valueTotal += Number(record?.value || 0);
            cloudTotal += readYtdDetailRowNumber(row, record, "cloudValue");
            copiersTotal += readYtdDetailRowNumber(row, record, "copiersValue");
        });

        const segmentValue = Number(ytdDetailFoot.dataset.segmentValue || valueTotal);
        const difference = roundYtdValue(segmentValue - valueTotal);
        const differenceText = Math.abs(difference) >= 0.01
            ? ` · diferencia vs barra ${currencyFormatter.format(difference)}`
            : "";
        ytdDetailFoot.innerHTML = `
            <tr>
                <td colspan="10">
                    <strong>Total visible</strong>
                    <span>${escapeHtml(numberFormatter.format(rows.length))} registro(s)${escapeHtml(differenceText)}</span>
                </td>
                <td class="text-end">${escapeHtml(currencyFormatter.format(cloudTotal))}</td>
                <td class="text-end">${escapeHtml(currencyFormatter.format(copiersTotal))}</td>
                <td>Valor</td>
                <td class="text-end">${escapeHtml(currencyFormatter.format(valueTotal))}</td>
                <td></td>
            </tr>
        `;
    }

    function aggregateYtdDetailTotals(records, resolveLabel) {
        const groups = new Map();
        records.forEach(record => {
            const label = resolveLabel(record) || "Sin dato";
            const current = groups.get(label) || 0;
            groups.set(label, current + Number(record?.value || 0));
        });

        return Array.from(groups.entries())
            .map(([label, value]) => ({ label, value: roundYtdValue(value) }))
            .filter(item => Math.abs(item.value) >= 0.01)
            .sort((left, right) => Math.abs(right.value) - Math.abs(left.value) || left.label.localeCompare(right.label, "es"))
            .slice(0, 8);
    }

    function renderYtdDetailTotalGroup(title, items) {
        if (!items.length) {
            return "";
        }

        return `
            <div class="ytd-detail-totals__group">
                <span>${escapeHtml(title)}</span>
                ${items.map(item => `
                    <div>
                        <small title="${escapeHtml(item.label)}">${escapeHtml(item.label)}</small>
                        <strong>${escapeHtml(currencyFormatter.format(item.value))}</strong>
                    </div>
                `).join("")}
            </div>
        `;
    }

    function renderYtdDetailTotals(records, segmentValue) {
        if (ytdDetailFoot) {
            ytdDetailFoot.dataset.segmentValue = String(Number(segmentValue || 0));
        }
        updateYtdDetailFooterFromRows();

        if (!ytdDetailTotals) {
            return;
        }

        if (!records.length) {
            ytdDetailTotals.innerHTML = "";
            return;
        }

        const visibleTotal = roundYtdValue(records.reduce((sum, record) => sum + Number(record?.value || 0), 0));
        const sourceTotals = aggregateYtdDetailTotals(records, record => record?.sourceLabel || record?.sourceType || "Fuente");
        const clientTotals = aggregateYtdDetailTotals(records, record => record?.clientLabel || "Sin cliente");
        const contractTotals = aggregateYtdDetailTotals(records, record => record?.contractTypeLabel || "Sin tipo");
        const difference = roundYtdValue(Number(segmentValue || 0) - visibleTotal);
        ytdDetailTotals.innerHTML = `
            <div class="ytd-detail-totals__headline">
                <span>Total barra</span>
                <strong>${escapeHtml(currencyFormatter.format(Number(segmentValue || visibleTotal || 0)))}</strong>
                <small>Visible: ${escapeHtml(currencyFormatter.format(visibleTotal))}${Math.abs(difference) >= 0.01 ? ` · diferencia ${escapeHtml(currencyFormatter.format(difference))}` : ""}</small>
            </div>
            ${renderYtdDetailTotalGroup("Fuente", sourceTotals)}
            ${renderYtdDetailTotalGroup("Cliente", clientTotals)}
            ${renderYtdDetailTotalGroup("Tipo contrato", contractTotals)}
        `;
    }

    function openYtdDetailModal(detailKey) {
        const detail = state.ytdSegmentDetails?.[detailKey];
        if (!detail || !ytdDetailModal) {
            return;
        }

        const rawRecords = Array.isArray(detail.records) ? detail.records : [];
        const records = rawRecords
            .filter(record => isYtdDetailRecordVisible(record, detail))
            .slice()
            .sort((left, right) => Math.abs(Number(right?.value || 0)) - Math.abs(Number(left?.value || 0)));
        const total = records.reduce((sum, record) => sum + Number(record?.value || 0), 0);
        const segmentValue = Number(detail.value || total || 0);
        state.ytdDetailRecords = {};
        setStatus(ytdDetailStatus, "", "");
        renderYtdBulkControls();

        ytdDetailTitle && (ytdDetailTitle.textContent = detail.title || "Detalle del segmento");
        ytdDetailSubtitle && (ytdDetailSubtitle.textContent = detail.subtitle || "");
        if (ytdDetailSummary) {
            ytdDetailSummary.innerHTML = `
                <span><strong>${escapeHtml(currencyFormatter.format(segmentValue))}</strong> valor del segmento</span>
                <span><strong>${escapeHtml(numberFormatter.format(records.length))}</strong> registro(s) visibles</span>
                ${rawRecords.length !== records.length ? `<span>${escapeHtml(numberFormatter.format(rawRecords.length))} registro(s) antes del filtro activo</span>` : ""}
                <span>${escapeHtml(detail.pointLabel || "")}</span>
            `;
        }

        if (ytdDetailBody) {
            ytdDetailBody.innerHTML = records.length
                ? records.map((record, index) => {
                    const recordKey = `ytd-record-${index}`;
                    state.ytdDetailRecords[recordKey] = record;
                    const editable = canEditYtdRecord(record);
                    return `
                    <tr data-ytd-record-key="${escapeHtml(recordKey)}">
                        <td>
                            <input type="checkbox" class="form-check-input" data-ytd-row-select aria-label="Seleccionar registro YTD" ${editable ? "" : "disabled"}>
                        </td>
                        <td>${escapeHtml(record?.sourceLabel || record?.sourceType || "-")}</td>
                        <td>${escapeHtml(record?.documentNumber || record?.recordId || "-")}</td>
                        <td>${escapeHtml(record?.dateDisplay || "-")}</td>
                        <td>${escapeHtml(record?.counterparty || "-")}</td>
                        <td>${escapeHtml(record?.recipientLabel || "-")}</td>
                        <td>${escapeHtml(record?.clientLabel || "-")}</td>
                        <td>${renderYtdCategoryEditor(record)}</td>
                        <td>${renderYtdVerticalEditor(record)}</td>
                        <td>${renderYtdContractTypeEditor(record)}</td>
                        <td class="text-end">${renderYtdAllocationEditor(record, "cloudValue")}</td>
                        <td class="text-end">${renderYtdAllocationEditor(record, "copiersValue")}</td>
                        <td>${escapeHtml(record?.description || "-")}</td>
                        <td class="text-end">${escapeHtml(currencyFormatter.format(Number(record?.value || 0)))}</td>
                        <td>
                            ${editable
                                ? '<button type="button" class="btn btn-sm btn-outline-primary" data-ytd-detail-save>Guardar fila</button>'
                                : '<span class="dashboard-pnl-detail__static">Solo lectura</span>'}
                        </td>
                    </tr>
                `;
                }).join("")
                : '<tr><td colspan="15" class="dashboard-table__empty">No hay registros detallados para este segmento.</td></tr>';
        }
        renderYtdDetailTotals(records, segmentValue);

        ytdDetailModal.hidden = false;
        document.body.classList.add("dashboard-modal-open");
        updateYtdDetailToolbarState();
    }

    function readYtdNumericSelect(row, fieldKey) {
        const select = row?.querySelector(`[data-ytd-edit-field='${fieldKey}']`);
        if (!select) {
            return null;
        }

        const value = Number(select.value || 0);
        return Number.isFinite(value) && value > 0 ? value : null;
    }

    function readYtdAllocationValue(row, fieldKey, label) {
        const input = row?.querySelector(`[data-ytd-edit-field='${fieldKey}']`);
        if (!input) {
            return null;
        }

        const value = parseEditableDecimalValue(input.value);
        if (Number.isNaN(value) || value < 0) {
            throw new Error(`El valor ${label} debe ser numerico y no puede ser negativo.`);
        }

        return value;
    }

    function buildYtdRowPatch(row, options = {}) {
        const record = state.ytdDetailRecords?.[row?.dataset.ytdRecordKey || ""];
        if (!row || !record) {
            return null;
        }

        const sourceType = record?.sourceType || "";
        const patch = {
            sourceType,
            recordId: record.recordId || ""
        };

        try {
            if (sourceType === "billing") {
                const verticalValue = readYtdNumericSelect(row, "vertical");
                const contractTypeValue = readYtdNumericSelect(row, "contractType");
                if (verticalValue && verticalValue !== Number(record.verticalOptionValue || 0)) {
                    patch.verticalOptionValue = verticalValue;
                }
                if (contractTypeValue && contractTypeValue !== Number(record.contractTypeOptionValue || 0)) {
                    patch.contractTypeOptionValue = contractTypeValue;
                }
            } else if (sourceType === "expense") {
                const categoryValue = readYtdNumericSelect(row, "category");
                const contractTypeValue = readYtdNumericSelect(row, "contractType");
                const cloudValue = readYtdAllocationValue(row, "cloudValue", "Cloud");
                const copiersValue = readYtdAllocationValue(row, "copiersValue", "Copiers");
                const licensingRecordIds = Array.isArray(record.licensingCostRecordIds)
                    ? record.licensingCostRecordIds.filter(Boolean)
                    : [];

                if (categoryValue && categoryValue !== Number(record.categoryOptionValue || 0)) {
                    patch.categoryOptionValue = categoryValue;
                }
                if (cloudValue !== null && Math.abs(cloudValue - Number(record.cloudValue || 0)) >= 0.01) {
                    patch.cloudValue = cloudValue;
                }
                if (copiersValue !== null && Math.abs(copiersValue - Number(record.copiersValue || 0)) >= 0.01) {
                    patch.copiersValue = copiersValue;
                }
                if (contractTypeValue
                    && contractTypeValue !== Number(record.contractTypeOptionValue || 0)
                    && licensingRecordIds.length) {
                    patch.contractTypeOptionValue = contractTypeValue;
                    patch.licensingCostRecordIds = licensingRecordIds;
                }
            } else {
                return null;
            }
        } catch (error) {
            if (!options.silent) {
                throw error;
            }

            return null;
        }

        const hasChanges = patch.verticalOptionValue !== undefined
            || patch.contractTypeOptionValue !== undefined
            || patch.categoryOptionValue !== undefined
            || patch.cloudValue !== undefined
            || patch.copiersValue !== undefined;
        return hasChanges ? patch : null;
    }

    function collectYtdRowPatches(rows) {
        return rows
            .map(row => buildYtdRowPatch(row))
            .filter(Boolean);
    }

    function applyYtdBulkChanges() {
        const rows = getSelectedYtdDetailRows();
        if (!rows.length) {
            setStatus(ytdDetailStatus, "info", "Selecciona al menos un registro para aplicar cambios masivos.");
            return;
        }

        const categoryValue = Number(ytdBulkCategorySelect?.value || 0);
        const billingVerticalValue = Number(ytdBulkBillingVerticalSelect?.value || 0);
        const billingContractValue = Number(ytdBulkBillingContractSelect?.value || 0);
        const expenseContractValue = Number(ytdBulkExpenseContractSelect?.value || 0);
        const cloudValue = (ytdBulkCloudInput?.value || "").trim();
        const copiersValue = (ytdBulkCopiersInput?.value || "").trim();
        let applied = 0;

        rows.forEach(row => {
            const record = state.ytdDetailRecords?.[row.dataset.ytdRecordKey || ""];
            if (!record) {
                return;
            }

            if (record.sourceType === "expense" && categoryValue > 0) {
                const select = row.querySelector("[data-ytd-edit-field='category']");
                if (select && !select.disabled) {
                    select.value = String(categoryValue);
                    applied += 1;
                }
            }

            if (record.sourceType === "billing" && billingVerticalValue > 0) {
                const select = row.querySelector("[data-ytd-edit-field='vertical']");
                if (select && !select.disabled) {
                    select.value = String(billingVerticalValue);
                    applied += 1;
                }
            }

            if (record.sourceType === "billing" && billingContractValue > 0) {
                const select = row.querySelector("[data-ytd-edit-field='contractType']");
                if (select && !select.disabled) {
                    select.value = String(billingContractValue);
                    applied += 1;
                }
            }

            if (record.sourceType === "expense" && expenseContractValue > 0) {
                const select = row.querySelector("[data-ytd-edit-field='contractType']");
                if (select && !select.disabled) {
                    select.value = String(expenseContractValue);
                    applied += 1;
                }
            }

            if (record.sourceType === "expense" && cloudValue) {
                const input = row.querySelector("[data-ytd-edit-field='cloudValue']");
                if (input && !input.disabled) {
                    input.value = cloudValue;
                    applied += 1;
                }
            }

            if (record.sourceType === "expense" && copiersValue) {
                const input = row.querySelector("[data-ytd-edit-field='copiersValue']");
                if (input && !input.disabled) {
                    input.value = copiersValue;
                    applied += 1;
                }
            }
        });

        updateYtdDetailToolbarState();
        setStatus(
            ytdDetailStatus,
            applied > 0 ? "info" : "warning",
            applied > 0
                ? `Cambios aplicados en ${numberFormatter.format(applied)} campo(s). Revisa y usa Guardar cambios.`
                : "No hubo campos compatibles con la seleccion actual.");
    }

    function updateYtdRecordOriginalFromPatch(row, patch) {
        const record = state.ytdDetailRecords?.[row?.dataset.ytdRecordKey || ""];
        if (!record || !patch) {
            return;
        }

        if (patch.verticalOptionValue !== undefined) {
            record.verticalOptionValue = patch.verticalOptionValue;
            record.verticalLabel = row.querySelector("[data-ytd-edit-field='vertical']")?.selectedOptions?.[0]?.textContent || record.verticalLabel;
        }
        if (patch.contractTypeOptionValue !== undefined) {
            record.contractTypeOptionValue = patch.contractTypeOptionValue;
            record.contractTypeLabel = row.querySelector("[data-ytd-edit-field='contractType']")?.selectedOptions?.[0]?.textContent || record.contractTypeLabel;
        }
        if (patch.categoryOptionValue !== undefined) {
            record.categoryOptionValue = patch.categoryOptionValue;
            record.categoryLabel = row.querySelector("[data-ytd-edit-field='category']")?.selectedOptions?.[0]?.textContent || record.categoryLabel;
        }
        if (patch.cloudValue !== undefined) {
            record.cloudValue = patch.cloudValue;
        }
        if (patch.copiersValue !== undefined) {
            record.copiersValue = patch.copiersValue;
        }
    }

    async function saveYtdRows(rows, options = {}) {
        const patches = collectYtdRowPatches(rows);
        if (!patches.length) {
            setStatus(ytdDetailStatus, "info", "No hay cambios pendientes para guardar.");
            updateYtdDetailToolbarState();
            return;
        }

        state.ytdBulkSaving = true;
        if (ytdBulkSaveButton) {
            ytdBulkSaveButton.disabled = true;
        }
        rows.forEach(row => {
            row.querySelectorAll("[data-ytd-detail-save]").forEach(button => {
                button.disabled = true;
                button.textContent = "Guardando...";
            });
        });
        setStatus(ytdDetailStatus, "info", `Guardando ${numberFormatter.format(patches.length)} cambio(s) en Dataverse...`);

        try {
            const result = await fetchJson(app.dataset.ytdRecordsUpdateUrl || "", {
                method: "POST",
                body: JSON.stringify({ records: patches })
            });

            rows.forEach(row => {
                const patch = buildYtdRowPatch(row, { silent: true });
                updateYtdRecordOriginalFromPatch(row, patch);
                row.querySelectorAll("[data-ytd-detail-save]").forEach(button => {
                    button.disabled = false;
                    button.textContent = "Guardar fila";
                });
            });

            setStatus(ytdDetailStatus, "info", result?.message || "Cambios YTD guardados correctamente.");
            updateYtdDetailToolbarState();
            await loadYtd();
            if (options.closeAfterSave) {
                closeYtdDetailModal();
            }
        } catch (error) {
            setStatus(ytdDetailStatus, "error", error instanceof Error ? error.message : "No fue posible guardar los cambios.");
            rows.forEach(row => {
                row.querySelectorAll("[data-ytd-detail-save]").forEach(button => {
                    button.disabled = false;
                    button.textContent = "Guardar fila";
                });
            });
        } finally {
            state.ytdBulkSaving = false;
            updateYtdDetailToolbarState();
        }
    }

    async function saveYtdDetailRecord(button) {
        const row = button?.closest("[data-ytd-record-key]");
        if (!row) {
            return;
        }

        await saveYtdRows([row], { closeAfterSave: false });
    }

    function wireYtdChartTooltip(container) {
        const plot = container.querySelector(".ytd-chart-card__plot");
        const tooltip = container.querySelector(".ytd-chart-tooltip");
        if (!plot || !tooltip) {
            return;
        }

        plot.addEventListener("mousemove", event => {
            const target = event.target.closest("[data-ytd-title]");
            if (!target || !plot.contains(target)) {
                tooltip.hidden = true;
                return;
            }

            tooltip.innerHTML = `
                <strong>${escapeHtml(target.dataset.ytdTitle || "")}</strong>
                ${target.dataset.ytdSegmentLabel ? `<span>${escapeHtml(target.dataset.ytdSegmentKind || "")}: ${escapeHtml(target.dataset.ytdSegmentLabel || "")} · ${escapeHtml(currencyFormatter.format(Number(target.dataset.ytdSegmentValue || 0)))}</span>` : ""}
                <span>Ventas totales: ${escapeHtml(currencyFormatter.format(Number(target.dataset.ytdSales || 0)))}</span>
                <span>Gastos totales: ${escapeHtml(currencyFormatter.format(Number(target.dataset.ytdExpenses || 0)))}</span>
                <span>Utilidad: ${escapeHtml(currencyFormatter.format(Number(target.dataset.ytdUtility || 0)))}</span>
            `;

            const bounds = plot.getBoundingClientRect();
            const tooltipWidth = 248;
            const left = Math.min(Math.max(event.clientX - bounds.left + 14, 8), Math.max(8, bounds.width - tooltipWidth));
            const top = Math.max(8, event.clientY - bounds.top - 96);
            tooltip.style.left = `${left}px`;
            tooltip.style.top = `${top}px`;
            tooltip.hidden = false;
        });

        plot.addEventListener("mouseleave", () => {
            tooltip.hidden = true;
        });

        plot.addEventListener("click", event => {
            const target = event.target.closest("[data-ytd-detail-key]");
            if (!target || !plot.contains(target)) {
                return;
            }

            openYtdDetailModal(target.dataset.ytdDetailKey || "");
        });
    }

    function getNiceMaxValue(value) {
        const numericValue = Number(value || 0);
        if (numericValue <= 0) {
            return 1;
        }

        const exponent = Math.floor(Math.log10(numericValue));
        const base = 10 ** exponent;
        const fraction = numericValue / base;

        if (fraction <= 1) {
            return base;
        }

        if (fraction <= 2) {
            return 2 * base;
        }

        if (fraction <= 5) {
            return 5 * base;
        }

        return 10 * base;
    }

    function buildLinePath(points) {
        if (!points.length) {
            return "";
        }

        return points
            .map((point, index) => `${index === 0 ? "M" : "L"} ${point.x.toFixed(2)} ${point.y.toFixed(2)}`)
            .join(" ");
    }

    function buildAreaPath(points, baselineY) {
        if (!points.length) {
            return "";
        }

        const start = `M ${points[0].x.toFixed(2)} ${baselineY.toFixed(2)}`;
        const line = points.map(point => `L ${point.x.toFixed(2)} ${point.y.toFixed(2)}`).join(" ");
        const end = `L ${points[points.length - 1].x.toFixed(2)} ${baselineY.toFixed(2)} Z`;
        return `${start} ${line} ${end}`;
    }

    function buildSampleLabelIndexes(length) {
        if (length <= 8) {
            return new Set(Array.from({ length }, (_, index) => index));
        }

        const indexes = new Set([0, length - 1]);
        const step = Math.ceil(length / 5);
        for (let index = 0; index < length; index += step) {
            indexes.add(index);
        }

        return indexes;
    }

    function renderTrendChart(trend, currentKey, previousKey, accentColor) {
        const labels = trend.map(item => item.label || "");
        const currentValues = trend.map(item => Number(item[currentKey] || 0));
        const previousValues = trend.map(item => Number(item[previousKey] || 0));
        const maxValue = getNiceMaxValue(Math.max(1, ...currentValues, ...previousValues));
        const width = 620;
        const height = 220;
        const padding = { top: 18, right: 20, bottom: 36, left: 52 };
        const plotWidth = width - padding.left - padding.right;
        const plotHeight = height - padding.top - padding.bottom;
        const denominator = Math.max(labels.length - 1, 1);
        const labelIndexes = buildSampleLabelIndexes(labels.length);

        const gridLines = Array.from({ length: 4 }, (_, index) => {
            const ratio = index / 3;
            const y = padding.top + plotHeight - (plotHeight * ratio);
            const value = maxValue * ratio;
            return `
                <line class="dashboard-chart__grid" x1="${padding.left}" y1="${y}" x2="${width - padding.right}" y2="${y}"></line>
                <text class="dashboard-chart__axis-label" x="${padding.left - 8}" y="${y + 4}" text-anchor="end">${escapeHtml(numberFormatter.format(value))}</text>
            `;
        }).join("");

        const xLabels = labels.map((label, index) => {
            if (!labelIndexes.has(index)) {
                return "";
            }

            const x = padding.left + (plotWidth * (index / denominator));
            return `<text class="dashboard-chart__axis-label" x="${x}" y="${height - 10}" text-anchor="middle">${escapeHtml(label)}</text>`;
        }).join("");

        const buildPoints = values => values.map((value, index) => ({
            x: padding.left + (plotWidth * (index / denominator)),
            y: padding.top + plotHeight - ((Number(value || 0) / maxValue) * plotHeight),
            value: Number(value || 0),
            label: labels[index] || ""
        }));

        const currentPoints = buildPoints(currentValues);
        const previousPoints = buildPoints(previousValues);
        const currentPath = buildLinePath(currentPoints);
        const previousPath = buildLinePath(previousPoints);
        const areaPath = buildAreaPath(currentPoints, padding.top + plotHeight);

        const currentDots = currentPoints.map(point => `
            <g>
                <title>${escapeHtml(`${point.label}: ${currencyFormatter.format(point.value)}`)}</title>
                <circle class="dashboard-chart__dot" cx="${point.x}" cy="${point.y}" r="4.2" fill="${accentColor}" stroke="#ffffff"></circle>
            </g>
        `).join("");

        const previousDots = previousPoints.map(point => `
            <g>
                <title>${escapeHtml(`${point.label}: ${currencyFormatter.format(point.value)}`)}</title>
                <circle class="dashboard-chart__dot" cx="${point.x}" cy="${point.y}" r="3.4" fill="#ffffff" stroke="#94a3b8"></circle>
            </g>
        `).join("");

        return `
            <svg class="dashboard-chart" viewBox="0 0 ${width} ${height}" role="img" aria-label="Tendencia">
                <defs>
                    <linearGradient id="dashboardGradient-${currentKey}" x1="0" x2="0" y1="0" y2="1">
                        <stop offset="0%" stop-color="${accentColor}" stop-opacity=".28"></stop>
                        <stop offset="100%" stop-color="${accentColor}" stop-opacity="0"></stop>
                    </linearGradient>
                </defs>
                ${gridLines}
                ${xLabels}
                <path class="dashboard-chart__area" d="${areaPath}" fill="url(#dashboardGradient-${currentKey})"></path>
                <path class="dashboard-chart__line dashboard-chart__line--reference" d="${previousPath}" stroke="#94a3b8"></path>
                <path class="dashboard-chart__line" d="${currentPath}" stroke="${accentColor}"></path>
                ${previousDots}
                ${currentDots}
            </svg>
        `;
    }

    function renderTrendBars(trend, currentKey, previousKey, growthKey, accentColor) {
        const currentValues = trend.map(item => Number(item[currentKey] || 0));
        const previousValues = trend.map(item => Number(item[previousKey] || 0));
        const maxValue = Math.max(1, ...currentValues, ...previousValues);

        return `
            <div class="dashboard-trend-bars">
                ${trend.map(point => {
                    const currentValue = Number(point[currentKey] || 0);
                    const previousValue = Number(point[previousKey] || 0);
                    const currentHeight = Math.max(4, (currentValue / maxValue) * 100);
                    const previousHeight = Math.max(4, (previousValue / maxValue) * 100);
                    const growth = point[growthKey];
                    const growthClass = growth === null || growth === undefined || Number(growth || 0) >= 0
                        ? "is-positive"
                        : "is-negative";

                    return `
                        <div class="dashboard-trend-bar" title="${escapeHtml(`${point.label}: ${currencyFormatter.format(currentValue)} vs ${currencyFormatter.format(previousValue)}`)}">
                            <span class="dashboard-growth ${growthClass}">${escapeHtml(formatGrowth(growth))}</span>
                            <div class="dashboard-trend-bar__plot" aria-hidden="true">
                                <span class="dashboard-trend-bar__bar dashboard-trend-bar__bar--previous" style="height:${previousHeight}%"></span>
                                <span class="dashboard-trend-bar__bar" style="height:${currentHeight}%; background:${accentColor}"></span>
                            </div>
                            <strong>${escapeHtml(point.label || "")}</strong>
                            <small>${escapeHtml(currencyFormatter.format(currentValue))}</small>
                        </div>
                    `;
                }).join("")}
            </div>
        `;
    }

    function renderTrends(dashboard) {
        const trend = Array.isArray(dashboard?.trend) ? dashboard.trend : [];
        if (!trendsContainer) {
            return;
        }

        if (!trend.length) {
            trendsContainer.innerHTML = '<div class="dashboard-table__empty">Todavia no hay puntos suficientes para dibujar la tendencia.</div>';
            return;
        }

        const cards = [
            { title: "Facturacion neta emitida", currentKey: "billingCurrent", previousKey: "billingPrevious", growthKey: "billingGrowthPercent", color: "#0f766e" },
            { title: "Recaudo", currentKey: "collectionsCurrent", previousKey: "collectionsPrevious", growthKey: "collectionsGrowthPercent", color: "#1d4ed8" },
            { title: "Retenciones", currentKey: "retentionsCurrent", previousKey: "retentionsPrevious", growthKey: "retentionsGrowthPercent", color: "#f97316" }
        ];

        trendsContainer.innerHTML = cards.map(card => {
            const currentTotal = trend.reduce((sum, point) => sum + Number(point[card.currentKey] || 0), 0);
            const previousTotal = trend.reduce((sum, point) => sum + Number(point[card.previousKey] || 0), 0);
            return `
                <article class="dashboard-trend-card">
                    <div class="dashboard-trend-card__header">
                        <strong>${escapeHtml(card.title)}</strong>
                        <span class="dashboard-growth ${currentTotal >= previousTotal ? "is-positive" : "is-negative"}">${escapeHtml(formatGrowth(previousTotal === 0 && currentTotal > 0 ? null : ((currentTotal - previousTotal) / (previousTotal || 1)) * 100))}</span>
                    </div>
                    <div class="dashboard-trend-card__value">${escapeHtml(currencyFormatter.format(currentTotal))}</div>
                    ${renderTrendBars(trend, card.currentKey, card.previousKey, card.growthKey, card.color)}
                    <div class="dashboard-trend-legend">
                        <span class="dashboard-legend-chip" style="color:${card.color}">Actual</span>
                        <span class="dashboard-legend-chip dashboard-legend-chip--muted">Ano anterior</span>
                    </div>
                </article>
            `;
        }).join("");
    }

    function renderTaxCalculationDetails(container, details) {
        if (!container) {
            return;
        }

        const items = Array.isArray(details) ? details : [];
        if (!items.length) {
            container.innerHTML = "";
            return;
        }

        container.innerHTML = items.map(detail => {
            const lines = Array.isArray(detail.lines) ? detail.lines : [];
            const invoiceCount = numberFormatter.format(Number(detail.invoiceCount || 0));

            return `
                <article class="dashboard-tax-calculation">
                    <div class="dashboard-tax-calculation__header">
                        <div>
                            <span class="dashboard-tax-calculation__eyebrow">Detalle del calculo</span>
                            <strong>${escapeHtml(detail.label || "Calculo")}</strong>
                        </div>
                        <span>${escapeHtml(detail.formula || "")}</span>
                    </div>
                    <div class="dashboard-tax-calculation__stats">
                        <div>
                            <span>${escapeHtml(detail.baseLabel || "Base total")}</span>
                            <strong>${escapeHtml(formatMetric(detail.baseTotal, "currency"))}</strong>
                        </div>
                        <div>
                            <span>${escapeHtml(detail.invoiceTotalLabel || "Total facturas")}</span>
                            <strong>${escapeHtml(formatMetric(detail.invoiceTotal, "currency"))}</strong>
                            <small>${escapeHtml(invoiceCount)} facturas</small>
                        </div>
                        <div>
                            <span>${escapeHtml(detail.resultLabel || "Resultado")}</span>
                            <strong>${escapeHtml(formatMetric(detail.resultValue, "currency"))}</strong>
                        </div>
                    </div>
                    ${lines.length ? `
                        <div class="dashboard-tax-calculation__lines">
                            ${lines.map(line => `
                                <div>
                                    <span>${escapeHtml(line.label || "")}</span>
                                    <strong>${escapeHtml(formatMetric(line.value, line.valueFormat))}</strong>
                                </div>
                            `).join("")}
                        </div>
                    ` : ""}
                </article>
            `;
        }).join("");
    }

    function renderTaxVerticalSummaries(container, summaries, compareYear) {
        if (!container) {
            return;
        }

        const items = Array.isArray(summaries) ? summaries : [];
        if (!items.length) {
            container.innerHTML = '<div class="dashboard-table__empty">No hay valores por vertical para este periodo.</div>';
            return;
        }

        container.innerHTML = items.map(item => {
            const components = Array.isArray(item.components) ? item.components : [];
            const growthClass = Number(item.growthPercent || 0) > 0
                ? "is-positive"
                : Number(item.growthPercent || 0) < 0
                    ? "is-negative"
                    : "";

            return `
                <article class="dashboard-tax-vertical-card dashboard-tax-vertical-card--${escapeHtml(item.tone || "neutral")} dashboard-tax-vertical-card--${escapeHtml(item.key || "vertical")}">
                    <div class="dashboard-tax-vertical-card__header">
                        <span class="dashboard-tax-vertical-card__label">${escapeHtml(item.label)}</span>
                        ${item.showComparison === false ? "" : `<span class="dashboard-growth ${growthClass}">${escapeHtml(formatGrowth(item.growthPercent))}</span>`}
                    </div>
                    <span class="dashboard-tax-vertical-card__primary-label">${escapeHtml(item.primaryLabel || "Total")}</span>
                    <strong class="dashboard-tax-vertical-card__value">${escapeHtml(formatMetric(item.primaryValue, "currency"))}</strong>
                    <div class="dashboard-tax-vertical-card__components">
                        ${components.map(component => `
                            <div class="dashboard-tax-vertical-card__component">
                                <span>${escapeHtml(component.label)}</span>
                                <strong>${escapeHtml(formatMetric(component.value, "currency"))}</strong>
                            </div>
                        `).join("")}
                    </div>
                    ${item.showComparison === false ? "" : `
                        <div class="dashboard-tax-vertical-card__footer">
                            <span>${escapeHtml(String(compareYear || ""))}</span>
                            <strong>${escapeHtml(formatMetric(item.previousPrimaryValue, "currency"))}</strong>
                        </div>
                    `}
                </article>
            `;
        }).join("");
    }

    function getTaxesRecurringSections(dashboard) {
        return [dashboard?.reteFuente, dashboard?.reteIca, dashboard?.reteIva]
            .filter(section => section && section.key);
    }

    function getTaxesOtherSections(dashboard) {
        return [dashboard?.incomeTax]
            .filter(section => section && section.key);
    }

    function getTaxesFilterMap(sectionKey) {
        const map = {
            retefuente: { year: "reteFuenteYear", value: "reteFuenteMonth" },
            reteica: { year: "reteIcaYear", value: "reteIcaPeriod" },
            reteiva: { year: "ivaYear", value: "ivaPeriod" },
            "income-tax": { year: "incomeTaxYear" }
        };

        return map[sectionKey] || null;
    }

    function syncTaxesFiltersFromDashboard(dashboard) {
        [...getTaxesRecurringSections(dashboard), ...getTaxesOtherSections(dashboard)].forEach(section => {
            const mapping = getTaxesFilterMap(section.key);
            const filter = section?.filter || {};
            if (!mapping || !filter) {
                return;
            }

            if (mapping.year && Number.isFinite(Number(filter.year))) {
                state.taxesFilters[mapping.year] = Number(filter.year);
            }

            if (mapping.value && Number.isFinite(Number(filter.value))) {
                state.taxesFilters[mapping.value] = Number(filter.value);
            }
        });
    }

    function renderTaxesDashboard(dashboard) {
        const recurringSections = getTaxesRecurringSections(dashboard);
        const otherSections = getTaxesOtherSections(dashboard);

        if (!recurringSections.some(section => section.key === state.taxesActiveRecurringKey)) {
            state.taxesActiveRecurringKey = recurringSections[0]?.key || "retefuente";
        }

        if (!otherSections.some(section => section.key === state.taxesActiveOtherKey)) {
            state.taxesActiveOtherKey = otherSections[0]?.key || "income-tax";
        }

        renderTaxesCards(taxesRecurringCards, recurringSections, state.taxesActiveRecurringKey, "recurring");
        renderTaxesCards(taxesOtherCards, otherSections, state.taxesActiveOtherKey, "other");
        renderTaxesDetail(taxesRecurringDetail, recurringSections.find(section => section.key === state.taxesActiveRecurringKey), dashboard);
        renderTaxesDetail(taxesOtherDetail, otherSections.find(section => section.key === state.taxesActiveOtherKey), dashboard);
    }

    function renderTaxesCards(container, sections, activeKey, groupKey) {
        if (!container) {
            return;
        }

        container.innerHTML = sections.length
            ? sections.map(section => renderTaxesCard(section, section.key === activeKey, groupKey)).join("")
            : '<div class="dashboard-table__empty">No hay tarjetas de impuestos para mostrar.</div>';
    }

    function renderTaxesCard(section, isActive, groupKey) {
        const cloud = getTaxesVerticalValue(section, "cloud");
        const copiers = getTaxesVerticalValue(section, "copiers");
        const total = Number(section?.totalValue || 0);
        const activeClass = isActive ? " is-active" : "";
        const period = [section?.periodLabel, section?.dateRangeLabel].filter(Boolean).join(" | ");

        return `
            <button type="button"
                    class="dashboard-tax-card${activeClass}"
                    data-taxes-card="${escapeHtml(section.key || "")}"
                    data-taxes-card-group="${escapeHtml(groupKey || "")}"
                    aria-pressed="${isActive ? "true" : "false"}">
                <span class="dashboard-tax-card__kicker">${escapeHtml(section?.filter?.valueLabel || section?.periodLabel || "Periodo")}</span>
                <strong class="dashboard-tax-card__title">${escapeHtml(section?.label || "Impuesto")}</strong>
                <span class="dashboard-tax-card__total-label">${escapeHtml(section?.totalLabel || "Total")}</span>
                <span class="dashboard-tax-card__value">${escapeHtml(formatMetric(total, "currency"))}</span>
                <span class="dashboard-tax-card__period">${escapeHtml(period || "-")}</span>
                <span class="dashboard-tax-card__verticals">
                    <span class="dashboard-tax-card__vertical dashboard-tax-card__vertical--cloud">
                        <span>Cloud</span>
                        <strong>${escapeHtml(formatMetric(cloud, "currency"))}</strong>
                    </span>
                    <span class="dashboard-tax-card__vertical dashboard-tax-card__vertical--copiers">
                        <span>Copiers</span>
                        <strong>${escapeHtml(formatMetric(copiers, "currency"))}</strong>
                    </span>
                </span>
            </button>
        `;
    }

    function getTaxesVerticalValue(section, verticalKey) {
        const summaries = Array.isArray(section?.verticalSummaries) ? section.verticalSummaries : [];
        return Number(summaries.find(item => item.key === verticalKey)?.primaryValue || 0);
    }

    function renderTaxesDetail(container, section, dashboard) {
        if (!container) {
            return;
        }

        if (!section) {
            container.innerHTML = '<div class="dashboard-table__empty">Selecciona una tarjeta para ver el detalle.</div>';
            return;
        }

        const filterMarkup = renderTaxesFilterControls(section);
        const isVatSection = section.key === "reteiva";
        const isReteFuenteSection = section.key === "retefuente";
        const detailContent = isVatSection
            ? `
                ${renderTaxesVatSummary(section)}
                <div class="dashboard-tax-verticals">
                    <div class="dashboard-tax-verticals__header">
                        <span>Por vertical</span>
                        <strong>${escapeHtml(section.totalLabel || "Total")}</strong>
                    </div>
                    <div class="dashboard-tax-vertical-grid" data-taxes-detail-verticals></div>
                </div>
                ${renderTaxesVatTable(section)}
            `
            : isReteFuenteSection
                ? `
                    ${renderTaxesReteFuenteSummary(section)}
                    <div class="dashboard-tax-verticals">
                        <div class="dashboard-tax-verticals__header">
                            <span>Por vertical</span>
                            <strong>${escapeHtml(section.totalLabel || "Total")}</strong>
                        </div>
                        <div class="dashboard-tax-vertical-grid" data-taxes-detail-verticals></div>
                    </div>
                    ${renderTaxesReportTable(section, "retefuente")}
                `
            : `
                <div class="dashboard-metric-grid" data-taxes-detail-metrics></div>
                <div class="dashboard-tax-calculation-grid" data-taxes-detail-calculation></div>
                <div class="dashboard-tax-verticals">
                    <div class="dashboard-tax-verticals__header">
                        <span>Por vertical</span>
                        <strong>${escapeHtml(section.totalLabel || "Total")}</strong>
                    </div>
                    <div class="dashboard-tax-vertical-grid" data-taxes-detail-verticals></div>
                </div>
            `;

        container.innerHTML = `
            <div class="dashboard-tax-detail-panel__header">
                <div>
                    <div class="dashboard-panel__kicker">${escapeHtml(section.periodLabel || "Periodo")}</div>
                    <h3>${escapeHtml(section.label || "Impuesto")}</h3>
                    <p>${escapeHtml(section.description || "")}</p>
                </div>
                <div class="dashboard-tax-detail-panel__actions">
                    <strong>${escapeHtml(formatMetric(section.totalValue, "currency"))}</strong>
                    ${isVatSection ? '<button type="button" class="btn btn-primary" data-taxes-vat-export>Generar reporte</button>' : ""}
                    ${isReteFuenteSection ? '<button type="button" class="btn btn-primary" data-taxes-retefuente-export>Generar reporte</button>' : ""}
                </div>
            </div>
            ${filterMarkup}
            ${renderTaxesCalculationBase(section)}
            ${detailContent}
        `;

        if (!isVatSection && !isReteFuenteSection) {
            renderComparativeKpis(
                container.querySelector("[data-taxes-detail-metrics]"),
                Array.isArray(section.metrics) ? section.metrics : [],
                dashboard?.compareYear);
            renderTaxCalculationDetails(
                container.querySelector("[data-taxes-detail-calculation]"),
                Array.isArray(section.calculationDetails) ? section.calculationDetails : []);
        }

        renderTaxVerticalSummaries(
            container.querySelector("[data-taxes-detail-verticals]"),
            Array.isArray(section.verticalSummaries) ? section.verticalSummaries : [],
            dashboard?.compareYear);
    }

    function renderTaxesCalculationBase(section) {
        const label = section?.calculationBaseLabel || "";
        if (!label) {
            return "";
        }

        return `
            <article class="dashboard-tax-base-card">
                <div>
                    <span>Base del calculo</span>
                    <strong>${escapeHtml(label)}</strong>
                </div>
                <strong>${escapeHtml(formatMetric(section?.calculationBaseValue || 0, "currency"))}</strong>
            </article>
        `;
    }

    function renderTaxesFilterControls(section) {
        const filter = section?.filter || {};
        const yearOptions = Array.isArray(filter.yearOptions) ? filter.yearOptions : [];
        const valueOptions = Array.isArray(filter.valueOptions) ? filter.valueOptions : [];
        const valueControl = valueOptions.length
            ? `
                <label class="dashboard-filter dashboard-filter--compact">
                    <span class="dashboard-filter__label">Periodo</span>
                    <select class="form-select dashboard-select" data-taxes-filter="value" data-taxes-section="${escapeHtml(section.key || "")}">
                        ${valueOptions.map(option => `<option value="${escapeHtml(String(option.value))}" ${Number(option.value) === Number(filter.value) ? "selected" : ""}>${escapeHtml(option.label || String(option.value))}</option>`).join("")}
                    </select>
                </label>
            `
            : "";
        const exportControl = section?.key === "reteica"
            ? '<button type="button" class="btn btn-primary" data-taxes-reteica-export>Exportar reporte</button>'
            : "";

        return `
            <div class="dashboard-tax-filters">
                <label class="dashboard-filter dashboard-filter--compact">
                    <span class="dashboard-filter__label">Año</span>
                    <select class="form-select dashboard-select" data-taxes-filter="year" data-taxes-section="${escapeHtml(section.key || "")}">
                        ${yearOptions.map(option => `<option value="${escapeHtml(String(option.value))}" ${Number(option.value) === Number(filter.year) ? "selected" : ""}>${escapeHtml(option.label || String(option.value))}</option>`).join("")}
                    </select>
                </label>
                ${valueControl}
                ${exportControl}
            </div>
        `;
    }

    function renderTaxesRetentionDetailTable(section) {
        const rows = Array.isArray(section?.retentionDetails) ? section.retentionDetails : [];
        const totalPayment = rows.reduce((sum, row) => sum + Number(row.paymentValue || 0), 0);
        const totalReteFuente = rows.reduce((sum, row) => sum + Number(row.reteFuenteValue || 0), 0);
        const totalCloud = rows.reduce((sum, row) => sum + Number(row.cloudValue || 0), 0);
        const totalCopiers = rows.reduce((sum, row) => sum + Number(row.copiersValue || 0), 0);

        return `
            <div class="dashboard-table-meta">
                <span>${escapeHtml(`Mostrando ${numberFormatter.format(rows.length)} registros de retefuente en gastos`)}</span>
            </div>
            <div class="dashboard-table-wrap dashboard-table-wrap--tall">
                <table class="table dashboard-table">
                    <thead>
                        <tr>
                            <th>Fecha pago</th>
                            <th class="text-end">Valor pago</th>
                            <th class="text-end">Retefuente</th>
                            <th>Tipo persona</th>
                            <th>Receptor</th>
                            <th>NIT receptor</th>
                            <th class="text-end">Cloud</th>
                            <th class="text-end">Copiers</th>
                        </tr>
                    </thead>
                    <tbody>
                        ${rows.length ? rows.map(row => `
                            <tr>
                                <td>${escapeHtml(row.paymentDateDisplay || "")}</td>
                                <td class="text-end">${escapeHtml(currencyFormatter.format(Number(row.paymentValue || 0)))}</td>
                                <td class="text-end">${escapeHtml(currencyFormatter.format(Number(row.reteFuenteValue || 0)))}</td>
                                <td>${escapeHtml(row.personTypeLabel || "Sin clasificar")}</td>
                                <td>${escapeHtml(row.recipientName || "")}</td>
                                <td>${escapeHtml(row.recipientNit || "")}</td>
                                <td class="text-end">${escapeHtml(currencyFormatter.format(Number(row.cloudValue || 0)))}</td>
                                <td class="text-end">${escapeHtml(currencyFormatter.format(Number(row.copiersValue || 0)))}</td>
                            </tr>
                        `).join("") : '<tr><td colspan="8" class="dashboard-table__empty">No hay retenciones de retefuente para este mes.</td></tr>'}
                        ${rows.length ? `
                            <tr class="dashboard-table__total">
                                <td>Total</td>
                                <td class="text-end">${escapeHtml(currencyFormatter.format(totalPayment))}</td>
                                <td class="text-end">${escapeHtml(currencyFormatter.format(totalReteFuente))}</td>
                                <td colspan="3">${escapeHtml(numberFormatter.format(rows.length))} registros</td>
                                <td class="text-end">${escapeHtml(currencyFormatter.format(totalCloud))}</td>
                                <td class="text-end">${escapeHtml(currencyFormatter.format(totalCopiers))}</td>
                            </tr>
                        ` : ""}
                    </tbody>
                </table>
            </div>
        `;
    }

    function getTaxesVatTables(section) {
        return Array.isArray(section?.vatDetails?.tables) ? section.vatDetails.tables : [];
    }

    function getTaxesVatTable(section, tableKey = state.taxesVatTableKey) {
        const tables = getTaxesVatTables(section);
        return tables.find(table => table.key === tableKey) || tables[0] || null;
    }

    function getTaxesVatTableTotal(section, tableKey) {
        return Number(getTaxesVatTable(section, tableKey)?.totalValue || 0);
    }

    function renderTaxesVatSummary(section) {
        const generated = getTaxesVatTableTotal(section, "generated");
        const spent = getTaxesVatTableTotal(section, "spent");
        const reteiva = getTaxesVatTableTotal(section, "reteiva");
        const payable = Number(section?.totalValue || 0);

        return `
            <article class="dashboard-tax-vat-summary">
                <div class="dashboard-tax-vat-summary__main">
                    <span>${escapeHtml(section?.totalLabel || "IVA total a pagar")}</span>
                    <strong>${escapeHtml(formatMetric(payable, "currency"))}</strong>
                </div>
                <div class="dashboard-tax-vat-summary__formula" aria-label="Formula IVA">
                    <div class="dashboard-tax-vat-summary__component dashboard-tax-vat-summary__component--debit">
                        <span>IVA generado</span>
                        <strong>${escapeHtml(formatMetric(generated, "currency"))}</strong>
                    </div>
                    <span class="dashboard-tax-vat-summary__operator">-</span>
                    <div class="dashboard-tax-vat-summary__group">
                        <div class="dashboard-tax-vat-summary__component dashboard-tax-vat-summary__component--credit">
                            <span>IVA gastado</span>
                            <strong>${escapeHtml(formatMetric(spent, "currency"))}</strong>
                        </div>
                        <div class="dashboard-tax-vat-summary__component dashboard-tax-vat-summary__component--credit">
                            <span>ReteIVA a favor</span>
                            <strong>${escapeHtml(formatMetric(reteiva, "currency"))}</strong>
                        </div>
                    </div>
                </div>
            </article>
        `;
    }

    function getTaxesVatVerticalOptions(table) {
        const rows = Array.isArray(table?.rows) ? table.rows : [];
        const hasUnassigned = rows.some(row => Number(row.unassignedTaxValue || 0) > 0);
        const options = [
            { key: "all", label: "Todas" },
            { key: "cloud", label: "Cloud" },
            { key: "copiers", label: "Copiers" }
        ];

        if (hasUnassigned) {
            options.push({ key: "unassigned", label: "Sin vertical" });
        }

        return options;
    }

    function getTaxesVatRowAmount(row, verticalKey, amountKey) {
        if (verticalKey === "cloud") {
            return Number(row[`cloud${amountKey}`] || 0);
        }

        if (verticalKey === "copiers") {
            return Number(row[`copiers${amountKey}`] || 0);
        }

        if (verticalKey === "unassigned") {
            return Number(row[`unassigned${amountKey}`] || 0);
        }

        return Number(row[amountKey.charAt(0).toLowerCase() + amountKey.slice(1)] || 0);
    }

    function renderTaxesVatTable(section) {
        const tables = getTaxesVatTables(section);
        const table = getTaxesVatTable(section);
        if (!table) {
            return '<div class="dashboard-table__empty">No hay detalle de IVA para este periodo.</div>';
        }

        const verticalOptions = getTaxesVatVerticalOptions(table);
        if (!verticalOptions.some(option => option.key === state.taxesVatVerticalKey)) {
            state.taxesVatVerticalKey = "all";
        }

        const verticalKey = state.taxesVatVerticalKey;
        const rows = (Array.isArray(table.rows) ? table.rows : [])
            .map(row => ({
                ...row,
                displayTotal: getTaxesVatRowAmount(row, verticalKey, "TotalValue"),
                displayTax: getTaxesVatRowAmount(row, verticalKey, "TaxValue")
            }))
            .filter(row => Number(row.displayTax || 0) > 0);
        const totalInvoice = rows.reduce((sum, row) => sum + Number(row.displayTotal || 0), 0);
        const totalTax = rows.reduce((sum, row) => sum + Number(row.displayTax || 0), 0);
        const showRetentionRateColumns = Boolean(table.showRetentionRateColumns);
        const retentionRateHeaders = showRetentionRateColumns
            ? '<th class="text-end">% rte fuente</th><th class="text-end">% rte ica</th>'
            : "";
        const retentionRateTotalCells = showRetentionRateColumns ? "<td></td><td></td>" : "";
        const totalColumns = 5 + (showRetentionRateColumns ? 2 : 0);

        return `
            <section class="dashboard-tax-vat-table">
                <div class="dashboard-tax-vat-table__toolbar">
                    <label class="dashboard-filter dashboard-filter--compact">
                        <span class="dashboard-filter__label">Tabla</span>
                        <select class="form-select dashboard-select" data-taxes-vat-table>
                            ${tables.map(option => `<option value="${escapeHtml(option.key || "")}" ${option.key === table.key ? "selected" : ""}>${escapeHtml(option.label || "")}</option>`).join("")}
                        </select>
                    </label>
                    <label class="dashboard-filter dashboard-filter--compact">
                        <span class="dashboard-filter__label">Vertical</span>
                        <select class="form-select dashboard-select" data-taxes-vat-vertical>
                            ${verticalOptions.map(option => `<option value="${escapeHtml(option.key)}" ${option.key === verticalKey ? "selected" : ""}>${escapeHtml(option.label)}</option>`).join("")}
                        </select>
                    </label>
                    <div class="dashboard-tax-vat-table__total">
                        <span>${escapeHtml(table.valueLabel || "Total")}</span>
                        <strong>${escapeHtml(formatMetric(totalTax, "currency"))}</strong>
                    </div>
                </div>
                <div class="dashboard-table-wrap dashboard-table-wrap--tall">
                    <table class="table dashboard-table">
                        <thead>
                            <tr>
                                <th>${escapeHtml(table.dateColumnLabel || "Fecha")}</th>
                                <th>Numero factura</th>
                                <th>${escapeHtml(table.nameColumnLabel || "Nombre")}</th>
                                <th class="text-end">Total factura</th>
                                <th class="text-end">${escapeHtml(table.valueLabel || "Valor")}</th>
                                ${retentionRateHeaders}
                            </tr>
                        </thead>
                        <tbody>
                            ${rows.length ? rows.map(row => `
                                <tr>
                                    <td>${escapeHtml(row.dateDisplay || "Sin fecha")}</td>
                                    <td>${escapeHtml(row.invoiceNumber || "-")}</td>
                                    <td>${escapeHtml(row.name || "")}</td>
                                    <td class="text-end">${escapeHtml(currencyFormatter.format(Number(row.displayTotal || 0)))}</td>
                                    <td class="text-end">${escapeHtml(currencyFormatter.format(Number(row.displayTax || 0)))}</td>
                                    ${showRetentionRateColumns ? `<td class="text-end">${escapeHtml(formatPercent(row.reteFuentePercent || 0))}</td><td class="text-end">${escapeHtml(formatPercent(row.reteIcaPercent || 0))}</td>` : ""}
                                </tr>
                            `).join("") : `<tr><td colspan="${escapeHtml(String(totalColumns))}" class="dashboard-table__empty">No hay filas para esta combinacion de tabla y vertical.</td></tr>`}
                            ${rows.length ? `
                                <tr class="dashboard-table__total">
                                    <td colspan="3">${escapeHtml(`${numberFormatter.format(rows.length)} registros`)}</td>
                                    <td class="text-end">${escapeHtml(currencyFormatter.format(totalInvoice))}</td>
                                    <td class="text-end">${escapeHtml(currencyFormatter.format(totalTax))}</td>
                                    ${retentionRateTotalCells}
                                </tr>
                            ` : ""}
                        </tbody>
                    </table>
                </div>
            </section>
        `;
    }

    function getTaxesReportTables(section) {
        return Array.isArray(section?.reportDetails?.tables) ? section.reportDetails.tables : [];
    }

    function getTaxesReportTable(section, tableKey) {
        const tables = getTaxesReportTables(section);
        return tables.find(table => table.key === tableKey) || tables[0] || null;
    }

    function getTaxesReportTableTotal(section, tableKey) {
        return Number(getTaxesReportTable(section, tableKey)?.totalAmountValue || 0);
    }

    function renderTaxesReteFuenteSummary(section) {
        const autofuente = getTaxesReportTableTotal(section, "autofuente");
        const expenses = getTaxesReportTableTotal(section, "retefuente-gastos");
        const payable = Number(section?.totalValue || 0);

        return `
            <article class="dashboard-tax-vat-summary">
                <div class="dashboard-tax-vat-summary__main">
                    <span>${escapeHtml(section?.totalLabel || "Total retefuente a pagar")}</span>
                    <strong>${escapeHtml(formatMetric(payable, "currency"))}</strong>
                </div>
                <div class="dashboard-tax-vat-summary__formula" aria-label="Formula Retefuente">
                    <div class="dashboard-tax-vat-summary__component dashboard-tax-vat-summary__component--debit">
                        <span>Autofuente</span>
                        <strong>${escapeHtml(formatMetric(autofuente, "currency"))}</strong>
                    </div>
                    <span class="dashboard-tax-vat-summary__operator">+</span>
                    <div class="dashboard-tax-vat-summary__component dashboard-tax-vat-summary__component--debit">
                        <span>ReteFuente gastos</span>
                        <strong>${escapeHtml(formatMetric(expenses, "currency"))}</strong>
                    </div>
                </div>
            </article>
        `;
    }

    function renderTaxesReportTable(section, kind) {
        const tableKey = kind === "retefuente" ? state.taxesReteFuenteTableKey : "";
        const tables = getTaxesReportTables(section);
        const table = getTaxesReportTable(section, tableKey);
        if (!table) {
            return '<div class="dashboard-table__empty">No hay detalle para este periodo.</div>';
        }

        if (kind === "retefuente" && !tables.some(option => option.key === state.taxesReteFuenteTableKey)) {
            state.taxesReteFuenteTableKey = table.key || "autofuente";
        }

        const rows = Array.isArray(table.rows) ? table.rows : [];
        const showReteFuentePercentColumn = Boolean(table.showReteFuentePercentColumn);
        const showReteIcaPercentColumn = Boolean(table.showReteIcaPercentColumn);
        const percentColumnsCount = (showReteFuentePercentColumn ? 1 : 0) + (showReteIcaPercentColumn ? 1 : 0);
        const totalColumns = 5 + (table.showCategoryColumn ? 1 : 0) + (table.showBaseColumn ? 1 : 0) + percentColumnsCount;
        const categoryHeader = table.showCategoryColumn ? `<th>${escapeHtml(table.categoryColumnLabel || "Categoria")}</th>` : "";
        const baseHeader = table.showBaseColumn ? `<th class="text-end">${escapeHtml(table.baseColumnLabel || "Base")}</th>` : "";
        const reteFuentePercentHeader = showReteFuentePercentColumn ? '<th class="text-end">% rte fuente</th>' : "";
        const reteIcaPercentHeader = showReteIcaPercentColumn ? '<th class="text-end">% rte ica</th>' : "";
        const categoryTotalCell = table.showCategoryColumn ? "<td></td>" : "";
        const baseTotalCell = table.showBaseColumn ? `<td class="text-end">${escapeHtml(currencyFormatter.format(Number(table.totalBaseValue || 0)))}</td>` : "";
        const percentTotalCells = `${showReteFuentePercentColumn ? "<td></td>" : ""}${showReteIcaPercentColumn ? "<td></td>" : ""}`;

        return `
            <section class="dashboard-tax-vat-table">
                <div class="dashboard-tax-vat-table__toolbar">
                    <label class="dashboard-filter dashboard-filter--compact">
                        <span class="dashboard-filter__label">Tabla</span>
                        <select class="form-select dashboard-select" data-taxes-report-table="${escapeHtml(kind || "")}">
                            ${tables.map(option => `<option value="${escapeHtml(option.key || "")}" ${option.key === table.key ? "selected" : ""}>${escapeHtml(option.label || "")}</option>`).join("")}
                        </select>
                    </label>
                    <div class="dashboard-tax-vat-table__total">
                        <span>${escapeHtml(table.amountColumnLabel || "Total")}</span>
                        <strong>${escapeHtml(formatMetric(table.totalAmountValue, "currency"))}</strong>
                    </div>
                </div>
                <div class="dashboard-table-wrap dashboard-table-wrap--tall">
                    <table class="table dashboard-table">
                        <thead>
                            <tr>
                                <th>${escapeHtml(table.dateColumnLabel || "Fecha")}</th>
                                <th>Numero factura</th>
                                <th>${escapeHtml(table.nameColumnLabel || "Nombre")}</th>
                                ${categoryHeader}
                                <th class="text-end">${escapeHtml(table.totalColumnLabel || "Total")}</th>
                                ${baseHeader}
                                <th class="text-end">${escapeHtml(table.amountColumnLabel || "Valor")}</th>
                                ${reteFuentePercentHeader}
                                ${reteIcaPercentHeader}
                            </tr>
                        </thead>
                        <tbody>
                            ${rows.length ? rows.map(row => `
                                <tr>
                                    <td>${escapeHtml(row.dateDisplay || "Sin fecha")}</td>
                                    <td>${escapeHtml(row.invoiceNumber || "-")}</td>
                                    <td>${escapeHtml(row.name || "")}</td>
                                    ${table.showCategoryColumn ? `<td>${escapeHtml(row.category || "")}</td>` : ""}
                                    <td class="text-end">${escapeHtml(currencyFormatter.format(Number(row.totalValue || 0)))}</td>
                                    ${table.showBaseColumn ? `<td class="text-end">${escapeHtml(currencyFormatter.format(Number(row.baseValue || 0)))}</td>` : ""}
                                    <td class="text-end">${escapeHtml(currencyFormatter.format(Number(row.amountValue || 0)))}</td>
                                    ${showReteFuentePercentColumn ? `<td class="text-end">${escapeHtml(formatPercent(row.reteFuentePercent || 0))}</td>` : ""}
                                    ${showReteIcaPercentColumn ? `<td class="text-end">${escapeHtml(formatPercent(row.reteIcaPercent || 0))}</td>` : ""}
                                </tr>
                            `).join("") : `<tr><td colspan="${escapeHtml(String(totalColumns))}" class="dashboard-table__empty">No hay filas para esta tabla.</td></tr>`}
                            ${rows.length ? `
                                <tr class="dashboard-table__total">
                                    <td colspan="3">${escapeHtml(`${numberFormatter.format(rows.length)} registros`)}</td>
                                    ${categoryTotalCell}
                                    <td class="text-end">${escapeHtml(currencyFormatter.format(Number(table.totalValue || 0)))}</td>
                                    ${baseTotalCell}
                                    <td class="text-end">${escapeHtml(currencyFormatter.format(Number(table.totalAmountValue || 0)))}</td>
                                    ${percentTotalCells}
                                </tr>
                            ` : ""}
                        </tbody>
                    </table>
                </div>
            </section>
        `;
    }

    function handleTaxesFilterChange(select) {
        const sectionKey = select?.dataset?.taxesSection || "";
        const filterKind = select?.dataset?.taxesFilter || "";
        const mapping = getTaxesFilterMap(sectionKey);
        if (!mapping) {
            return;
        }

        const value = Number(select.value || 0);
        if (!Number.isFinite(value) || value <= 0) {
            return;
        }

        if (filterKind === "year" && mapping.year) {
            state.taxesFilters[mapping.year] = value;
        }

        if (filterKind === "value" && mapping.value) {
            state.taxesFilters[mapping.value] = value;
        }

        loadTaxes();
    }

    function buildFallbackCopiersGroups(rows) {
        const groups = new Map();
        rows.forEach(row => {
            const billingDay = Number(row.billingDay || 0);
            const clientKey = row.clientId || normalizeText(row.clientName || "sin-cliente");
            const groupId = `${clientKey}|day:${billingDay}`;
            if (!groups.has(groupId)) {
                groups.set(groupId, {
                    groupId,
                    clientId: row.clientId || "",
                    clientName: row.clientName || "Sin cliente",
                    billingDay,
                    billingDayDisplay: row.billingDayDisplay || (billingDay > 0 ? `Dia ${billingDay}` : "Sin dia"),
                    productLinesCount: 0,
                    equipmentCount: 0,
                    countersRegisteredCount: 0,
                    pendingCountersCount: 0,
                    quantity: 0,
                    includedOperations: 0,
                    additionalOperation: 0,
                    totalWithVat: 0,
                    counterSummary: "Sin equipos asignados",
                    lines: [],
                    equipment: []
                });
            }

            const group = groups.get(groupId);
            group.lines.push(row);
            group.productLinesCount += 1;
            group.quantity += Number(row.quantity || 0);
            group.includedOperations += Number(row.includedOperations || 0);
            group.additionalOperation += Number(row.additionalOperation || 0);
            group.totalWithVat += Number(row.totalWithVat || 0);
        });

        return Array.from(groups.values());
    }

    function getCopiersGroups(dashboard) {
        const groups = Array.isArray(dashboard?.groups) && dashboard.groups.length
            ? [...dashboard.groups]
            : buildFallbackCopiersGroups(Array.isArray(dashboard?.rows) ? dashboard.rows : []);

        return groups.sort((left, right) => {
            const leftDay = Number(left.billingDay || 0) > 0 ? Number(left.billingDay || 0) : Number.MAX_SAFE_INTEGER;
            const rightDay = Number(right.billingDay || 0) > 0 ? Number(right.billingDay || 0) : Number.MAX_SAFE_INTEGER;
            if (leftDay !== rightDay) {
                return leftDay - rightDay;
            }

            return normalizeText(left.clientName).localeCompare(normalizeText(right.clientName), "es");
        });
    }

    function getCopiersGroupById(groupId) {
        const groups = getCopiersGroups(state.copiersDashboard);
        return groups.find(group => (group?.groupId || "") === (groupId || "")) || null;
    }

    function renderCopiersCounterSummary(group) {
        const equipmentCount = Number(group?.equipmentCount || 0);
        const registered = Number(group?.countersRegisteredCount || 0);
        const pending = Number(group?.pendingCountersCount || 0);
        const groupId = group?.groupId || "";

        if (!equipmentCount) {
            return `
                <button type="button" class="dashboard-counter-chip dashboard-counter-chip--neutral dashboard-counter-chip--button" data-copiers-counter-summary="${escapeHtml(groupId)}" title="Ver equipos y contador reciente">
                    <strong>Sin equipos</strong>
                    <small>0 asociados</small>
                </button>
            `;
        }

        const tone = pending > 0 ? "pending" : "ok";
        const label = pending > 0
            ? `${numberFormatter.format(pending)} pendiente(s)`
            : "Al dia";

        return `
            <button type="button" class="dashboard-counter-chip dashboard-counter-chip--${tone} dashboard-counter-chip--button" data-copiers-counter-summary="${escapeHtml(groupId)}" title="Ver equipos y contador reciente">
                <strong>${escapeHtml(label)}</strong>
                <small>${escapeHtml(`${registered}/${equipmentCount} con contador`)}</small>
            </button>
        `;
    }

    function renderCopiersProductLines(lines) {
        const items = Array.isArray(lines) ? lines : [];
        if (!items.length) {
            return '<div class="dashboard-table__empty">No hay lineas de productos para este grupo.</div>';
        }

        return `
            <div class="dashboard-copiers-lines">
                <div class="dashboard-copiers-line dashboard-copiers-line--header">
                    <span>Producto</span>
                    <span>Cant.</span>
                    <span>Equipos</span>
                    <span>Oper. incl.</span>
                    <span>Oper. adic.</span>
                    <span>Unit. antes IVA</span>
                    <span>Unit. con IVA</span>
                    <span>Total con IVA</span>
                </div>
                ${items.map(row => `
                    <div class="dashboard-copiers-line">
                        <button type="button" class="dashboard-copiers-cell-btn dashboard-copiers-cell-btn--link" data-copiers-row-id="${escapeHtml(row.recordId || "")}" data-copiers-field="productName">
                            ${escapeHtml(row.productName || "Producto sin nombre")}
                        </button>
                        <button type="button" class="dashboard-copiers-cell-btn text-end" data-copiers-row-id="${escapeHtml(row.recordId || "")}" data-copiers-field="quantity" title="Cantidad">
                            ${escapeHtml(numberFormatter.format(Number(row.quantity || 0)))}
                        </button>
                        <button type="button" class="dashboard-copiers-assignment-btn ${row.hasAssignmentOverflow ? "is-warning" : ""}" data-copiers-line-assignment="${escapeHtml(row.recordId || "")}" title="Asignar equipos a esta linea">
                            <strong>${escapeHtml(`${numberFormatter.format(Number(row.assignedEquipmentCount || 0))}/${numberFormatter.format(Number(row.equipmentAssignmentCapacity || 0))}`)}</strong>
                            <small>${escapeHtml(row.equipmentAssignmentSummary || "Sin asignacion")}</small>
                        </button>
                        <button type="button" class="dashboard-copiers-cell-btn text-end" data-copiers-row-id="${escapeHtml(row.recordId || "")}" data-copiers-field="includedOperations" title="Operaciones incluidas">
                            ${escapeHtml(numberFormatter.format(Number(row.includedOperations || 0)))}
                        </button>
                        <button type="button" class="dashboard-copiers-cell-btn text-end" data-copiers-row-id="${escapeHtml(row.recordId || "")}" data-copiers-field="additionalOperation" title="Operacion adicional">
                            ${escapeHtml(numberFormatter.format(Number(row.additionalOperation || 0)))}
                        </button>
                        <button type="button" class="dashboard-copiers-cell-btn text-end" data-copiers-row-id="${escapeHtml(row.recordId || "")}" data-copiers-field="unitValueBeforeVat" title="Valor unitario antes IVA">
                            ${escapeHtml(currencyFormatter.format(Number(row.unitValueBeforeVat || 0)))}
                        </button>
                        <button type="button" class="dashboard-copiers-cell-btn text-end" data-copiers-row-id="${escapeHtml(row.recordId || "")}" data-copiers-field="unitValueWithVat" title="Valor unitario con IVA">
                            ${escapeHtml(currencyFormatter.format(Number(row.unitValueWithVat || 0)))}
                        </button>
                        <button type="button" class="dashboard-copiers-cell-btn text-end" data-copiers-row-id="${escapeHtml(row.recordId || "")}" data-copiers-field="totalWithVat">
                            ${escapeHtml(currencyFormatter.format(Number(row.totalWithVat || 0)))}
                        </button>
                    </div>
                `).join("")}
            </div>
        `;
    }

    function renderCopiersEquipmentDetails(equipment) {
        const items = Array.isArray(equipment) ? equipment : [];
        if (!items.length) {
            return '<div class="dashboard-table__empty">Este cliente no tiene equipos asignados en la tabla de equipos.</div>';
        }

        return `
            <div class="dashboard-copiers-equipment-list">
                ${items.map(row => {
                    const hasCounter = Boolean(row.hasCurrentCounter);
                    const statusClass = hasCounter ? "dashboard-counter-chip--ok" : "dashboard-counter-chip--pending";
                    const statusLabel = row.counterStatusLabel || (hasCounter ? "Contador registrado" : "Pendiente de contador");
                    const meta = [row.categoryLabel, row.reference, row.site, row.area]
                        .filter(value => value && String(value).trim())
                        .join(" · ");

                    return `
                        <button type="button" class="dashboard-copiers-equipment-item" data-copiers-equipment-id="${escapeHtml(row.recordId || "")}">
                            <span class="dashboard-copiers-equipment-item__main">
                                <strong>${escapeHtml(row.serial || "Equipo sin serial")}</strong>
                                <small>${escapeHtml(meta || "Sin detalle adicional")}</small>
                            </span>
                            <span class="dashboard-counter-chip ${statusClass}">
                                <strong>${escapeHtml(statusLabel)}</strong>
                                <small>${escapeHtml(row.counterDateDisplay || "Ultimos 35 dias")}</small>
                            </span>
                        </button>
                    `;
                }).join("")}
            </div>
        `;
    }

    function renderCopiersGroupDetail(group) {
        const lines = Array.isArray(group?.lines) ? group.lines : [];

        return `
            <tr class="dashboard-copiers-detail-row">
                <td colspan="7">
                    <div class="dashboard-copiers-detail">
                        <section class="dashboard-copiers-detail__section">
                            <div class="dashboard-copiers-detail__header">
                                <strong>Lineas de productos Copiers</strong>
                                <span>${escapeHtml(group?.equipmentAssignmentSummary || `${numberFormatter.format(lines.length)} linea(s)`)}</span>
                            </div>
                            ${renderCopiersProductLines(lines)}
                        </section>
                    </div>
                </td>
            </tr>
        `;
    }

    function renderCopiersTable(dashboard) {
        const groups = getCopiersGroups(dashboard);
        const rowCount = Array.isArray(dashboard?.rows) ? dashboard.rows.length : groups.reduce((sum, group) => sum + Number(group.productLinesCount || 0), 0);

        if (copiersResultsCount) {
            copiersResultsCount.textContent = `Mostrando ${numberFormatter.format(groups.length)} grupo(s) · ${numberFormatter.format(rowCount)} linea(s)`;
        }

        if (!copiersBillingBody) {
            return;
        }

        copiersBillingBody.innerHTML = groups.length
            ? groups.map(group => {
                const groupId = group.groupId || "";
                const expanded = state.copiersExpandedGroups.has(groupId);
                return `
                    <tr class="dashboard-copiers-group-row ${expanded ? "is-expanded" : ""}">
                        <td>
                            <button type="button" class="dashboard-copiers-group-toggle" data-copiers-group-toggle="${escapeHtml(groupId)}" aria-expanded="${expanded ? "true" : "false"}">
                                <span>${expanded ? "-" : "+"}</span>
                                ${escapeHtml(group.billingDayDisplay || "Sin dia")}
                            </button>
                        </td>
                        <td>
                            <button type="button" class="dashboard-copiers-cell-btn dashboard-copiers-cell-btn--link" data-copiers-group-client="${escapeHtml(groupId)}">
                                ${escapeHtml(group.clientName || "Sin cliente")}
                            </button>
                        </td>
                        <td>${escapeHtml(numberFormatter.format(Number(group.productLinesCount || 0)))} linea(s)</td>
                        <td class="text-end">
                            <span class="dashboard-equipment-assignment-inline">
                                <strong>${escapeHtml(numberFormatter.format(Number(group.equipmentCount || 0)))}</strong>
                                <small>${escapeHtml(group.equipmentAssignmentSummary || "Sin asignacion")}</small>
                            </span>
                        </td>
                        <td>${renderCopiersCounterSummary(group)}</td>
                        <td class="text-end">${escapeHtml(currencyFormatter.format(Number(group.totalWithVat || 0)))}</td>
                        <td class="text-end">
                            <button type="button" class="btn btn-sm btn-outline-secondary dashboard-copiers-detail-btn" data-copiers-group-toggle="${escapeHtml(groupId)}">
                                ${expanded ? "Ocultar" : "Desglosar"}
                            </button>
                        </td>
                    </tr>
                    ${expanded ? renderCopiersGroupDetail(group) : ""}
                `;
            }).join("")
            : '<tr><td colspan="7" class="dashboard-table__empty">No hay registros de facturacion copiers disponibles.</td></tr>';
    }

    function getCopiersRowById(recordId) {
        const rows = Array.isArray(state.copiersDashboard?.rows) ? state.copiersDashboard.rows : [];
        return rows.find(row => (row?.recordId || "") === (recordId || "")) || null;
    }

    function renderCopiersClientSummaries(dashboard) {
        if (!copiersClientSummaryBody) {
            return;
        }

        const rows = Array.isArray(dashboard?.clientSummaries) ? dashboard.clientSummaries : [];
        copiersClientSummaryBody.innerHTML = rows.length
            ? rows.map(row => `
                <tr>
                    <td>${escapeHtml(row.clientName || "Sin cliente")}</td>
                    <td class="text-end">${escapeHtml(numberFormatter.format(Number(row.equipmentCount || 0)))}</td>
                    <td>${escapeHtml(row.categoryBreakdown || "Sin detalle")}</td>
                </tr>
            `).join("")
            : '<tr><td colspan="3" class="dashboard-table__empty">No hay clientes con equipos asignados.</td></tr>';
    }

    function renderCopiersStockTable(dashboard) {
        if (!copiersStockBody) {
            return;
        }

        const rows = Array.isArray(dashboard?.stockRows) ? dashboard.stockRows : [];
        copiersStockBody.innerHTML = rows.length
            ? rows.map(row => `
                <tr>
                    <td>${escapeHtml(row.serial || "Sin serial")}</td>
                    <td>${escapeHtml(row.categoryLabel || "Sin categoria")}</td>
                    <td>${escapeHtml(row.reference || "Sin referencia")}</td>
                </tr>
            `).join("")
            : '<tr><td colspan="3" class="dashboard-table__empty">No hay equipos en stock en este momento.</td></tr>';
    }

    function renderCopiersEquipmentTable(dashboard) {
        const rows = Array.isArray(dashboard?.equipmentRows) ? dashboard.equipmentRows : [];

        if (copiersEquipmentResultsCount) {
            copiersEquipmentResultsCount.textContent = `Mostrando ${numberFormatter.format(rows.length)} registros`;
        }

        if (!copiersEquipmentBody) {
            return;
        }

        copiersEquipmentBody.innerHTML = rows.length
            ? rows.map(row => `
                <tr>
                    <td>
                        <button type="button" class="dashboard-copiers-cell-btn" data-copiers-equipment-id="${escapeHtml(row.recordId || "")}">
                            ${escapeHtml(row.serial || "Sin serial")}
                        </button>
                    </td>
                    <td>
                        <button type="button" class="dashboard-copiers-cell-btn" data-copiers-equipment-id="${escapeHtml(row.recordId || "")}">
                            ${row.inStock
                                ? '<span class="dashboard-pill dashboard-pill--stock">Stock</span>'
                                : escapeHtml(row.clientName || "Sin cliente")}
                        </button>
                    </td>
                    <td>
                        <button type="button" class="dashboard-copiers-cell-btn" data-copiers-equipment-id="${escapeHtml(row.recordId || "")}">
                            ${escapeHtml(row.categoryLabel || "Sin categoria")}
                        </button>
                    </td>
                    <td>
                        <button type="button" class="dashboard-copiers-cell-btn" data-copiers-equipment-id="${escapeHtml(row.recordId || "")}">
                            ${escapeHtml(row.reference || "Sin referencia")}
                        </button>
                    </td>
                    <td>
                        <button type="button" class="dashboard-copiers-cell-btn" data-copiers-equipment-id="${escapeHtml(row.recordId || "")}">
                            ${escapeHtml(row.observations || "Sin observaciones")}
                        </button>
                    </td>
                    <td class="text-end">
                        <button type="button" class="dashboard-copiers-cell-btn text-end" data-copiers-equipment-id="${escapeHtml(row.recordId || "")}">
                            ${escapeHtml(numberFormatter.format(Number(row.maintenanceCount || 0)))}
                        </button>
                    </td>
                    <td>
                        <button type="button" class="dashboard-copiers-cell-btn" data-copiers-equipment-id="${escapeHtml(row.recordId || "")}">
                            ${escapeHtml(row.lastMaintenanceDateDisplay || "Sin mantenimientos")}
                        </button>
                    </td>
                </tr>
            `).join("")
            : '<tr><td colspan="7" class="dashboard-table__empty">No hay equipos cargados en este momento.</td></tr>';
    }

    function renderCopiersInventoryValue(row) {
        const effectiveValue = row?.effectiveCommercialValue;
        if (effectiveValue === null || effectiveValue === undefined || effectiveValue === "") {
            return '<span class="dashboard-pill dashboard-pill--warning">Pendiente</span>';
        }

        const value = currencyFormatter.format(Number(effectiveValue || 0));
        const isSuggested = !row?.commercialValue && row?.suggestedCommercialValue;
        return isSuggested
            ? `${escapeHtml(value)} <span class="dashboard-muted-inline">sugerido</span>`
            : escapeHtml(value);
    }

    function renderCopiersInventorySource(row) {
        const source = row?.commercialValueSource || "Pendiente";
        const tone = source === "Dataverse"
            ? "success"
            : source === "Sugerido"
                ? "info"
                : "warning";
        return `<span class="dashboard-pill dashboard-pill--${tone}">${escapeHtml(source)}</span>`;
    }

    function renderCopiersInventoryTable(dashboard) {
        const rows = Array.isArray(dashboard?.records) ? dashboard.records : [];

        if (copiersInventoryResultsCount) {
            copiersInventoryResultsCount.textContent = `Mostrando ${numberFormatter.format(rows.length)} equipos`;
        }

        if (!copiersInventoryBody) {
            return;
        }

        copiersInventoryBody.innerHTML = rows.length
            ? rows.map(row => `
                <tr>
                    <td>${escapeHtml(row.serial || "Sin serial")}</td>
                    <td>${escapeHtml(row.reference || "Sin referencia")}</td>
                    <td class="text-end">${renderCopiersInventoryValue(row)}</td>
                    <td>${renderCopiersInventorySource(row)}</td>
                    <td>${row.inStock
                        ? '<span class="dashboard-pill dashboard-pill--stock">Stock</span>'
                        : escapeHtml(row.clientName || "Sin cliente")}</td>
                    <td>${escapeHtml(row.categoryLabel || "Sin categoria")}</td>
                </tr>
            `).join("")
            : '<tr><td colspan="6" class="dashboard-table__empty">No hay equipos cargados en este momento.</td></tr>';
    }

    function renderCopiersInventoryPendingGroups(dashboard) {
        const groups = Array.isArray(dashboard?.pendingReferenceGroups)
            ? dashboard.pendingReferenceGroups
            : [];
        const pendingCount = Number(dashboard?.pendingRecordsCount || 0);

        if (copiersInventoryPendingCount) {
            copiersInventoryPendingCount.textContent = `${numberFormatter.format(pendingCount)} pendientes`;
        }

        if (!copiersInventoryPendingBody) {
            return;
        }

        copiersInventoryPendingBody.innerHTML = groups.length
            ? groups.map(group => `
                <tr>
                    <td>
                        <strong>${escapeHtml(group.reference || group.key || "Sin referencia")}</strong>
                        <span class="dashboard-muted-inline">${escapeHtml(group.key || "")}</span>
                    </td>
                    <td class="text-end">${escapeHtml(numberFormatter.format(Number(group.equipmentCount || 0)))}</td>
                    <td>${escapeHtml((Array.isArray(group.examples) ? group.examples : []).join(", ") || "Sin ejemplos")}</td>
                </tr>
            `).join("")
            : '<tr><td colspan="3" class="dashboard-table__empty">No quedan referencias pendientes sin valor comercial.</td></tr>';
    }

    function renderCopiersInventoryDashboard(dashboard) {
        renderCopiersInventoryKpis(dashboard);
        renderCopiersInventoryTable(dashboard);
        renderCopiersInventoryPendingGroups(dashboard);
        syncCopiersInventoryButtons();
    }

    function renderCopiersMaintenanceChart(chart) {
        if (!copiersMaintenanceChart || !copiersMaintenanceLegend) {
            return;
        }

        const labels = Array.isArray(chart?.labels) ? chart.labels : [];
        const series = Array.isArray(chart?.series) ? chart.series : [];
        if (!labels.length || !series.length) {
            copiersMaintenanceChart.innerHTML = `
                <div class="dashboard-table__empty">
                    <strong>Sin soportes para graficar.</strong><br />
                    <span>Cuando existan mantenimientos con tecnico y fecha apareceran aqui.</span>
                </div>
            `;
            copiersMaintenanceLegend.innerHTML = "";
            return;
        }

        const palette = ["#0f6cbd", "#198754", "#f59e0b", "#c2410c", "#8b5cf6", "#ef4444", "#0891b2", "#475569"];
        const chartHeight = 320;
        const chartWidth = 1080;
        const padding = { top: 20, right: 24, bottom: 50, left: 56 };
        const values = series.flatMap(item => Array.isArray(item.values) ? item.values : []);
        const maxValue = getNiceMaxValue(Math.max(1, ...values));
        const plotWidth = chartWidth - padding.left - padding.right;
        const plotHeight = chartHeight - padding.top - padding.bottom;
        const axisSteps = 4;

        const gridLines = Array.from({ length: axisSteps + 1 }, (_, index) => {
            const y = padding.top + ((plotHeight / axisSteps) * index);
            const axisValue = maxValue - ((maxValue / axisSteps) * index);
            return `
                <line x1="${padding.left}" y1="${y}" x2="${chartWidth - padding.right}" y2="${y}" class="dashboard-maintenance-chart__grid" />
                <text x="${padding.left - 10}" y="${y + 4}" class="dashboard-maintenance-chart__axis">${escapeHtml(numberFormatter.format(axisValue))}</text>
            `;
        }).join("");

        const labelNodes = labels.map((label, index) => {
            const x = labels.length === 1
                ? padding.left + (plotWidth / 2)
                : padding.left + ((plotWidth / Math.max(labels.length - 1, 1)) * index);
            return `<text x="${x}" y="${chartHeight - 18}" text-anchor="middle" class="dashboard-maintenance-chart__axis">${escapeHtml(label)}</text>`;
        }).join("");

        const seriesNodes = series.map((item, index) => {
            const valuesList = Array.isArray(item.values) ? item.values : [];
            const color = palette[index % palette.length];
            const points = valuesList.map((value, valueIndex) => {
                const x = labels.length === 1
                    ? padding.left + (plotWidth / 2)
                    : padding.left + ((plotWidth / Math.max(labels.length - 1, 1)) * valueIndex);
                const y = padding.top + plotHeight - ((Number(value || 0) / maxValue) * plotHeight);
                return { x, y, value: Number(value || 0) };
            });
            const circles = points.map(point => `
                <circle cx="${point.x}" cy="${point.y}" r="4" fill="${color}">
                    <title>${escapeHtml(item.technicianName || "Tecnico")}: ${escapeHtml(numberFormatter.format(point.value))}</title>
                </circle>
            `).join("");

            return `
                <path d="${buildLinePath(points)}" fill="none" stroke="${color}" stroke-width="3" stroke-linecap="round" stroke-linejoin="round"></path>
                ${circles}
            `;
        }).join("");

        copiersMaintenanceChart.innerHTML = `
            <svg viewBox="0 0 ${chartWidth} ${chartHeight}" class="dashboard-maintenance-chart__svg" role="img" aria-label="Soportes realizados por mes y tecnico">
                ${gridLines}
                <line x1="${padding.left}" y1="${padding.top + plotHeight}" x2="${chartWidth - padding.right}" y2="${padding.top + plotHeight}" class="dashboard-maintenance-chart__baseline"></line>
                ${labelNodes}
                ${seriesNodes}
            </svg>
        `;

        copiersMaintenanceLegend.innerHTML = series.map((item, index) => `
            <div class="dashboard-maintenance-legend__item">
                <span class="dashboard-maintenance-legend__swatch" style="--legend-color: ${palette[index % palette.length]};"></span>
                <span>${escapeHtml(item.technicianName || "Sin tecnico")}</span>
                <strong>${escapeHtml(numberFormatter.format(Number(item.total || 0)))}</strong>
            </div>
        `).join("");
    }

    function buildCopiersCountersPeriodOptions() {
        if (copiersCountersMonthFilter && !copiersCountersMonthFilter.options.length) {
            copiersCountersMonthFilter.innerHTML = monthLabels
                .map((label, index) => `<option value="${index + 1}">${escapeHtml(label)}</option>`)
                .join("");
        }

        if (copiersCountersYearFilter && !copiersCountersYearFilter.options.length) {
            const startYear = currentYear + 1;
            const endYear = currentYear - 5;
            copiersCountersYearFilter.innerHTML = Array.from({ length: startYear - endYear + 1 }, (_, index) => startYear - index)
                .map(year => `<option value="${year}">${year}</option>`)
                .join("");
        }

        if (copiersCountersMonthFilter) {
            copiersCountersMonthFilter.value = String(state.copiersCountersMonth);
        }

        if (copiersCountersYearFilter) {
            copiersCountersYearFilter.value = String(state.copiersCountersYear);
        }

        if (copiersCountersClientNameFilter) {
            copiersCountersClientNameFilter.value = state.copiersCountersClientName || "";
        }

        if (copiersCountersClientIdFilter) {
            copiersCountersClientIdFilter.value = state.copiersCountersClientId || "";
        }
    }

    function syncCopiersCountersFiltersFromControls() {
        state.copiersCountersMonth = Number(copiersCountersMonthFilter?.value || currentValue);
        state.copiersCountersYear = Number(copiersCountersYearFilter?.value || currentYear);
        state.copiersCountersClientId = copiersCountersClientIdFilter?.value || "";
        state.copiersCountersClientName = (copiersCountersClientNameFilter?.value || "").trim();
    }

    function renderCopiersCountersPending(message) {
        buildCopiersCountersPeriodOptions();

        if (copiersCountersKpisContainer) {
            copiersCountersKpisContainer.innerHTML = "";
            copiersCountersKpisContainer.hidden = true;
        }

        if (copiersCountersResultsShell) {
            copiersCountersResultsShell.hidden = true;
        }

        if (copiersCountersEmptyState) {
            copiersCountersEmptyState.hidden = false;
            copiersCountersEmptyState.innerHTML = `
                <strong>Define los filtros para consultar contadores.</strong>
                <span>${escapeHtml(message || "Al aplicar el filtro se cargan los KPIs, el consumo por cliente y el detalle por equipo.")}</span>
            `;
        }

        if (copiersCountersClientResultsCount) {
            copiersCountersClientResultsCount.textContent = "Sin consulta aplicada";
        }

        if (copiersCountersEquipmentResultsCount) {
            copiersCountersEquipmentResultsCount.textContent = "Sin consulta aplicada";
        }

        if (copiersCountersClientBody) {
            copiersCountersClientBody.innerHTML = '<tr><td colspan="11" class="dashboard-table__empty">Aplica filtros para consultar Dataverse.</td></tr>';
        }

        if (copiersCountersEquipmentBody) {
            copiersCountersEquipmentBody.innerHTML = '<tr><td colspan="20" class="dashboard-table__empty">Aplica filtros para consultar Dataverse.</td></tr>';
        }

        if (copiersCountersPeriodLabel) {
            copiersCountersPeriodLabel.textContent = "Selecciona mes, año y cliente opcional. La consulta a Dataverse se ejecuta al aplicar el filtro.";
        }

        if (state.activeTab === "copiers" && state.copiersSubtab === "counters") {
            updateHeroForCopiers(null);
        }

        updateCopiersCountersPdfButton();
    }

    function markCopiersCountersFiltersPending(message) {
        state.copiersCountersDashboard = null;
        state.copiersCountersSignature = "";
        state.copiersCountersHasAppliedFilters = false;
        renderCopiersCountersPending(message || "Tienes filtros listos. Aplicalos para consultar Dataverse.");
    }

    function handleCopiersCountersFilterChanged(message) {
        syncCopiersCountersFiltersFromControls();
        markCopiersCountersFiltersPending(message);
    }

    function updateCopiersCountersPdfButton() {
        if (!copiersCountersPdfButton) {
            return;
        }

        const filtersMatchLoadedData = state.copiersCountersHasAppliedFilters
            && state.copiersCountersDashboard
            && state.copiersCountersSignature === getCopiersCountersSignature();
        copiersCountersPdfButton.disabled = state.copiersCountersLoading
            || !copiersCountersPdfUrl
            || !filtersMatchLoadedData;
    }

    function renderCopiersCountersClientOptions(dashboard) {
        if (!copiersCountersClientOptions) {
            return;
        }

        const clients = Array.isArray(dashboard?.clients) ? dashboard.clients : [];
        state.copiersCountersClientSuggestions = clients;
        renderCopiersLookupOptions(copiersCountersClientOptions, clients, "name");

        const selectedClientId = dashboard?.selectedClientId || state.copiersCountersClientId || "";
        const selectedClientName = dashboard?.selectedClientName
            || clients.find(client => String(client.id || "").toLowerCase() === selectedClientId.toLowerCase())?.name
            || state.copiersCountersClientName
            || "";

        state.copiersCountersClientId = selectedClientId;
        state.copiersCountersClientName = selectedClientName;

        if (copiersCountersClientIdFilter) {
            copiersCountersClientIdFilter.value = selectedClientId;
        }

        if (copiersCountersClientNameFilter) {
            copiersCountersClientNameFilter.value = selectedClientName;
        }
    }

    function renderCopiersCountersClientTable(dashboard) {
        const rows = Array.isArray(dashboard?.clientSummaries) ? dashboard.clientSummaries : [];
        if (copiersCountersClientResultsCount) {
            copiersCountersClientResultsCount.textContent = `Mostrando ${numberFormatter.format(rows.length)} clientes`;
        }

        if (!copiersCountersClientBody) {
            return;
        }

        copiersCountersClientBody.innerHTML = rows.length
            ? rows.map(row => `
                <tr>
                    <td>${escapeHtml(row.clientName || "Sin cliente")}</td>
                    <td>${escapeHtml(row.billingDayDisplay || "Sin dia")}</td>
                    <td class="text-end">${escapeHtml(formatNullableNumber(row.totalCopies))}</td>
                    <td class="text-end">${escapeHtml(formatNullableNumber(row.totalScans))}</td>
                    <td class="text-end"><strong>${escapeHtml(formatNullableNumber(row.totalConsumption))}</strong></td>
                    <td class="text-end">${escapeHtml(formatNullableNumber(row.includedOperations))}</td>
                    <td class="text-end"><strong>${escapeHtml(formatNullableNumber(row.excessQuantity))}</strong></td>
                    <td class="text-end">${escapeHtml(formatMetric(row.unitExcessCost || 0, "currency"))}</td>
                    <td class="text-end"><strong>${escapeHtml(formatMetric(row.excessTotal || 0, "currency"))}</strong></td>
                    <td class="text-end">${escapeHtml(formatNullableNumber(row.equipmentWithConsumption))}</td>
                    <td>${escapeHtml(row.assignmentModeLabel || row.validationSummary || "")}</td>
                </tr>
            `).join("")
            : '<tr><td colspan="11" class="dashboard-table__empty">No hay datos para el periodo seleccionado.</td></tr>';
    }

    function renderCopiersCountersEquipmentTable(dashboard) {
        const rows = Array.isArray(dashboard?.equipmentRows) ? dashboard.equipmentRows : [];
        if (copiersCountersEquipmentResultsCount) {
            copiersCountersEquipmentResultsCount.textContent = `Mostrando ${numberFormatter.format(rows.length)} equipos`;
        }

        if (!copiersCountersEquipmentBody) {
            return;
        }

        copiersCountersEquipmentBody.innerHTML = rows.length
            ? rows.map(row => `
                <tr>
                    <td>${escapeHtml(row.clientName || "Sin cliente")}</td>
                    <td>${escapeHtml(row.equipmentName || "Sin equipo")}</td>
                    <td>${renderCopiersCounterAssignmentBadge(row)}</td>
                    <td>${escapeHtml(row.isBackup ? "Backup" : (row.productLineName || ""))}</td>
                    <td>${escapeHtml(row.site || "")}</td>
                    <td>${escapeHtml(row.area || "")}</td>
                    <td>${escapeHtml(row.previousDateDisplay || "—")}</td>
                    <td>${escapeHtml(row.currentDateDisplay || "—")}</td>
                    <td class="text-end">${escapeHtml(formatNullableNumber(row.currentCopiesCounter))}</td>
                    <td class="text-end">${escapeHtml(formatNullableNumber(row.previousCopiesCounter))}</td>
                    <td class="text-end"><strong>${escapeHtml(formatNullableNumber(row.copiesConsumption))}</strong></td>
                    <td class="text-end">${escapeHtml(formatNullableNumber(row.currentScansCounter))}</td>
                    <td class="text-end">${escapeHtml(formatNullableNumber(row.previousScansCounter))}</td>
                    <td class="text-end"><strong>${escapeHtml(formatNullableNumber(row.scansConsumption))}</strong></td>
                    <td class="text-end">${escapeHtml(formatNullableNumber(row.daysBetweenReadings))}</td>
                    <td class="text-end"><strong>${escapeHtml(formatNullableNumber(row.totalConsumption))}</strong></td>
                    <td class="text-end">${escapeHtml(formatNullableNumber(row.includedOperations))}</td>
                    <td class="text-end"><strong>${escapeHtml(formatNullableNumber(row.excessQuantity))}</strong></td>
                    <td class="text-end">${escapeHtml(formatMetric(row.unitExcessCost || 0, "currency"))}</td>
                    <td class="text-end"><strong>${escapeHtml(formatMetric(row.excessTotal || 0, "currency"))}</strong></td>
                </tr>
            `).join("")
            : '<tr><td colspan="20" class="dashboard-table__empty">No hay equipos con lecturas para el periodo seleccionado.</td></tr>';
    }

    function renderCopiersCounterAssignmentBadge(row) {
        const status = row?.assignmentStatus || "Sin clasificar";
        const tone = row?.isBackup
            ? "info"
            : (status.toLowerCase().includes("sin") ? "warning" : "success");
        return `<span class="dashboard-status-pill dashboard-status-pill--${tone}">${escapeHtml(status)}</span>`;
    }

    function renderCopiersCountersDashboard(dashboard) {
        buildCopiersCountersPeriodOptions();
        renderCopiersCountersClientOptions(dashboard);
        if (copiersCountersEmptyState) {
            copiersCountersEmptyState.hidden = true;
        }

        if (copiersCountersKpisContainer) {
            copiersCountersKpisContainer.hidden = false;
        }

        if (copiersCountersResultsShell) {
            copiersCountersResultsShell.hidden = false;
        }

        renderSimpleKpis(copiersCountersKpisContainer, dashboard?.kpis);
        renderCopiersCountersClientTable(dashboard);
        renderCopiersCountersEquipmentTable(dashboard);

        if (copiersCountersPeriodLabel) {
            copiersCountersPeriodLabel.textContent = dashboard?.dateRangeLabel
                ? `${dashboard.periodLabel || "Periodo"} · ${dashboard.dateRangeLabel}`
                : (dashboard?.periodLabel || "Copias y escaneos consolidados por periodo.");
        }

        updateCopiersCountersPdfButton();
    }

    function renderCopiersEquipmentDashboard(dashboard) {
        renderCopiersEquipmentKpis(dashboard);
        renderCopiersClientSummaries(dashboard);
        renderCopiersStockTable(dashboard);
        renderCopiersEquipmentTable(dashboard);
        renderCopiersMaintenanceDashboard(dashboard);
    }

    function getCopiersEquipmentRowById(recordId) {
        const rows = Array.isArray(state.copiersEquipmentDashboard?.equipmentRows) ? state.copiersEquipmentDashboard.equipmentRows : [];
        return rows.find(row => (row?.recordId || "") === (recordId || "")) || null;
    }

    function getPnlRowGroup(rowKey) {
        const key = rowKey || "";
        if (key.includes("income") && !key.includes("before-taxes")) {
            return "income";
        }

        if (key.includes("cogs") || key.includes("supplies") || key.includes("machines") || key.includes("technical-service")) {
            return "cogs";
        }

        if (key.includes("personal") || key.includes("admin") || key.includes("commercial")) {
            return "expenses";
        }

        if (key.includes("gross-profit") || key.includes("ebitda") || key.includes("net-income") || key.includes("before-taxes")) {
            return "profit";
        }

        if (key.includes("other") || key.includes("financial")) {
            return "other";
        }

        return "neutral";
    }

    function getPnlCellTone(value) {
        const numericValue = Number(value || 0);
        if (numericValue > 0) {
            return "positive";
        }

        if (numericValue < 0) {
            return "negative";
        }

        return "zero";
    }

    function renderPnlCellButton(row, value, percentage, cellMonth) {
        const resolvedCellMonth = Number(cellMonth);
        const monthAttribute = Number.isInteger(resolvedCellMonth) && resolvedCellMonth >= 1 && resolvedCellMonth <= 12
            ? `data-pnl-cell-month="${resolvedCellMonth}"`
            : "";
        const tone = getPnlCellTone(value);
        const percentValue = Number(percentage || 0);
        const percentLabel = row.valueFormat === "currency"
            ? `<span class="dashboard-pnl-cell-percent">${escapeHtml(formatMetric(percentValue, "percent"))}</span>`
            : "";

        return `
            <button
                type="button"
                class="dashboard-pnl-cell-btn dashboard-pnl-cell-btn--${tone}"
                data-pnl-row-key="${escapeHtml(row.key || "")}"
                data-pnl-row-label="${escapeHtml(row.label || "")}"
                ${monthAttribute}>
                <span class="dashboard-pnl-cell-value">${escapeHtml(formatMetric(value, row.valueFormat))}</span>
                ${percentLabel}
            </button>
        `;
    }

    function renderPnlTable(dashboard) {
        if (!pnlTableContainer) {
            return;
        }

        const months = Array.isArray(dashboard?.months) ? dashboard.months : [];
        const rows = Array.isArray(dashboard?.rows) ? dashboard.rows : [];

        if (!months.length || !rows.length) {
            pnlTableContainer.innerHTML = `
                <div class="dashboard-table__empty">
                    <strong>${escapeHtml(dashboard?.emptyStateTitle || "No hay datos para el P&L.")}</strong><br />
                    <span>${escapeHtml(dashboard?.emptyStateMessage || "Sin movimientos en este corte.")}</span>
                </div>
            `;
            return;
        }

        const headerCells = months
            .map(month => `<th class="text-end">${escapeHtml(month.label)}</th>`)
            .join("");

        const bodyRows = rows.map(row => {
            const rowGroup = getPnlRowGroup(row.key || "");
            if ((row.rowType || "").toLowerCase() === "section") {
                return `
                    <tr class="dashboard-pnl-row dashboard-pnl-row--section dashboard-pnl-row--group-${escapeHtml(rowGroup)}">
                        <td colspan="${months.length + 2}">${escapeHtml(row.label)}</td>
                    </tr>
                `;
            }

            const valueCells = (Array.isArray(row.values) ? row.values : [])
                .map((value, index) => {
                    const month = Number(months[index]?.month || index + 1);
                    const percentage = Array.isArray(row.percentages) ? row.percentages[index] : 0;
                    return `
                        <td class="text-end">
                            ${renderPnlCellButton(row, value, percentage, month)}
                        </td>
                    `;
                })
                .join("");

            return `
                <tr class="dashboard-pnl-row dashboard-pnl-row--${escapeHtml(row.rowType || "detail")} dashboard-pnl-row--group-${escapeHtml(rowGroup)}">
                    <td class="dashboard-pnl-row__label dashboard-pnl-row__label--level-${Number(row.level || 0)}">${escapeHtml(row.label)}</td>
                    ${valueCells}
                    <td class="text-end dashboard-pnl-row__total">
                        ${renderPnlCellButton(row, row.total, row.totalPercentage, null)}
                    </td>
                </tr>
            `;
        }).join("");

        pnlTableContainer.innerHTML = `
            <table class="table dashboard-table dashboard-pnl-table">
                <thead>
                    <tr>
                        <th>Cuenta</th>
                        ${headerCells}
                        <th class="text-end">Total YTD</th>
                    </tr>
                </thead>
                <tbody>${bodyRows}</tbody>
            </table>
        `;

        pnlTableContainer.querySelectorAll("[data-pnl-row-key]").forEach(button => {
            button.addEventListener("click", () => {
                loadPnlDetail(
                    button.dataset.pnlRowKey || "",
                    button.dataset.pnlRowLabel || "",
                    button.dataset.pnlCellMonth || null
                ).catch(() => {});
            });
        });
    }

    function renderPnlOrphanCell(row, month, value, isTotal = false) {
        const count = Number(value || 0);
        if (count <= 0) {
            return `<span class="dashboard-pnl-orphan-zero">${escapeHtml(numberFormatter.format(count))}</span>`;
        }

        const monthAttribute = isTotal ? "" : `data-pnl-cell-month="${Number(month || 0)}"`;
        return `
            <button
                type="button"
                class="dashboard-pnl-cell-btn dashboard-pnl-cell-btn--count"
                data-pnl-row-key="${escapeHtml(row.key || "")}"
                data-pnl-row-label="${escapeHtml(row.label || "")}"
                ${monthAttribute}>
                ${escapeHtml(numberFormatter.format(count))}
            </button>
        `;
    }

    function renderPnlOrphanTable(dashboard) {
        if (!pnlOrphanTableContainer) {
            return;
        }

        pnlOrphanDescription && (pnlOrphanDescription.textContent = dashboard?.orphanDescription || "");

        const months = Array.isArray(dashboard?.months) ? dashboard.months : [];
        const rows = Array.isArray(dashboard?.orphanRows) ? dashboard.orphanRows : [];
        if (!months.length || !rows.length) {
            pnlOrphanTableContainer.innerHTML = `
                <div class="dashboard-table__empty">
                    <strong>Sin controles de registros huerfanos.</strong><br />
                    <span>Cuando el P&L tenga meses visibles mostraremos aqui los registros pendientes de clasificacion.</span>
                </div>
            `;
            return;
        }

        const headerCells = months
            .map(month => `<th class="text-end">${escapeHtml(month.label)}</th>`)
            .join("");

        const bodyRows = rows.map(row => {
            const valueCells = (Array.isArray(row.values) ? row.values : [])
                .map((value, index) => `
                    <td class="text-end">
                        ${renderPnlOrphanCell(row, months[index]?.month || index + 1, value)}
                    </td>
                `)
                .join("");

            return `
                <tr class="dashboard-pnl-row dashboard-pnl-row--orphan">
                    <td class="dashboard-pnl-row__label dashboard-pnl-orphan-row__label">
                        <strong>${escapeHtml(row.label || "")}</strong>
                        <span class="dashboard-pnl-orphan-row__hint">${escapeHtml(row.hint || "")}</span>
                    </td>
                    ${valueCells}
                    <td class="text-end dashboard-pnl-row__total">
                        ${renderPnlOrphanCell(row, null, row.total, true)}
                    </td>
                </tr>
            `;
        }).join("");

        pnlOrphanTableContainer.innerHTML = `
            <table class="table dashboard-table dashboard-pnl-table dashboard-pnl-orphan-table">
                <thead>
                    <tr>
                        <th>Control</th>
                        ${headerCells}
                        <th class="text-end">Total YTD</th>
                    </tr>
                </thead>
                <tbody>${bodyRows}</tbody>
            </table>
        `;

        pnlOrphanTableContainer.querySelectorAll("[data-pnl-row-key]").forEach(button => {
            button.addEventListener("click", () => {
                loadPnlDetail(
                    button.dataset.pnlRowKey || "",
                    button.dataset.pnlRowLabel || "",
                    button.dataset.pnlCellMonth || null
                ).catch(() => {});
            });
        });
    }

    function parsePortfolioDisplayDate(value) {
        const match = /^(\d{2})\/(\d{2})\/(\d{4})$/.exec((value || "").trim());
        return match ? `${match[3]}-${match[2]}-${match[1]}` : "";
    }

    function renderPortfolioCurrency(value) {
        return escapeHtml(currencyFormatter.format(Number(value || 0)));
    }

    function getNetInvoiceTotal(row) {
        if (row && Object.prototype.hasOwnProperty.call(row, "netTotalInvoice")) {
            return Math.max(0, Number(row.netTotalInvoice || 0));
        }

        return Math.max(
            0,
            Number(row?.totalInvoice || 0) - Number(row?.creditNoteTotal || 0));
    }

    function getNetInvoiceVat(row) {
        if (row && Object.prototype.hasOwnProperty.call(row, "netVatValue")) {
            return Math.max(0, Number(row.netVatValue || 0));
        }

        return Math.max(
            0,
            Number(row?.vatValue || 0) - Number(row?.creditNoteVat || 0));
    }

    function renderPortfolioNumber(value) {
        return escapeHtml(numberFormatter.format(Number(value || 0)));
    }

    function renderPortfolioPercent(value) {
        return `${renderPortfolioNumber(value)}%`;
    }

    function renderPortfolioText(value) {
        const text = (value ?? "").toString().trim();
        return escapeHtml(text || "-");
    }

    function renderPortfolioStatusBadge(row) {
        const status = (row.paymentStatusLabel || "").trim() || "Sin estado";
        const tone = status === "Con pago"
            ? "is-success"
            : status === "NC completa"
                ? "is-info"
            : status.startsWith("Pendiente")
                ? "is-warning"
                : "";

        return `<span class="dashboard-badge ${tone}">${escapeHtml(status)}</span>`;
    }

    function renderBillingCategoryChip(value, category) {
        const label = (value || "").toString().trim() || "Sin dato";
        const normalized = normalizeText(label).replace(/[^a-z0-9]+/g, "-").replace(/^-|-$/g, "");
        const toneClass = normalized ? `dashboard-billing-chip--${category}-${normalized}` : "";
        return `<span class="dashboard-billing-chip ${toneClass}">${escapeHtml(label)}</span>`;
    }

    function renderBillingInvoiceDownloadLink(row) {
        const url = (row?.publicUrl || "").trim();
        const invoiceNumber = (row?.invoiceNumber || "").trim();
        const label = invoiceNumber
            ? `Abrir factura ${invoiceNumber}`
            : "Abrir factura";
        const icon = `
            <svg viewBox="0 0 24 24" aria-hidden="true" focusable="false">
                <path d="M12 3v11m0 0 4-4m-4 4-4-4M5 17v2a2 2 0 0 0 2 2h10a2 2 0 0 0 2-2v-2" />
            </svg>
        `;

        if (!url) {
            return `<span class="dashboard-icon-link is-disabled" title="Sin link de factura">${icon}</span>`;
        }

        return `
            <a href="${escapeHtml(url)}"
               target="_blank"
               rel="noopener noreferrer"
               class="dashboard-icon-link"
               title="${escapeHtml(label)}"
               aria-label="${escapeHtml(label)}"
               data-billing-invoice-ignore-click>
                ${icon}
            </a>
        `;
    }

    function normalizeInvoiceNumber(value) {
        return normalizeText(value).replace(/[^a-z0-9]/g, "");
    }

    function getBillingInvoiceDuplicateNumbers(rows) {
        const counts = new Map();
        (Array.isArray(rows) ? rows : []).forEach(row => {
            const key = normalizeInvoiceNumber(row?.invoiceNumber || "");
            if (key) {
                counts.set(key, (counts.get(key) || 0) + 1);
            }
        });

        return new Set(Array.from(counts.entries())
            .filter(([, count]) => count > 1)
            .map(([key]) => key));
    }

    function isBillingInvoiceDuplicate(row) {
        return state.billingInvoiceDuplicateNumbers.has(normalizeInvoiceNumber(row?.invoiceNumber || ""));
    }

    function getBillingInvoiceContractTypeOptions() {
        return Array.isArray(state.billingInvoicesDetail?.contractTypeOptions)
            ? state.billingInvoicesDetail.contractTypeOptions
            : [];
    }

    function getBillingInvoiceVerticalOptions() {
        return Array.isArray(state.billingInvoicesDetail?.verticalOptions)
            ? state.billingInvoicesDetail.verticalOptions
            : [];
    }

    const billingInvoiceColumns = [
        { key: "invoiceNumber", label: "Factura" },
        { key: "clientName", label: "Cliente" },
        {
            key: "verticalLabel",
            label: "Vertical",
            render: row => renderBillingCategoryChip(row.verticalLabel, "vertical")
        },
        {
            key: "contractTypeLabel",
            label: "Contrato",
            render: row => renderBillingCategoryChip(row.contractTypeLabel, "contract")
        },
        {
            key: "emissionDateValue",
            label: "Fecha de emision",
            type: "date",
            displayValue: row => row.emissionDateDisplay || "Sin fecha",
            sortValue: row => row.emissionDateValue || ""
        },
        {
            key: "totalInvoice",
            label: "Total factura",
            type: "number",
            align: "end",
            displayValue: row => currencyFormatter.format(Number(row.totalInvoice || 0)),
            render: row => renderPortfolioCurrency(row.totalInvoice)
        },
        {
            key: "creditNoteTotal",
            label: "Notas credito",
            type: "number",
            align: "end",
            displayValue: row => currencyFormatter.format(Number(row.creditNoteTotal || 0)),
            render: row => renderPortfolioCurrency(row.creditNoteTotal)
        },
        {
            key: "netTotalInvoice",
            label: "Total neto",
            type: "number",
            align: "end",
            displayValue: row => currencyFormatter.format(getNetInvoiceTotal(row)),
            render: row => renderPortfolioCurrency(getNetInvoiceTotal(row))
        },
        {
            key: "paymentStatusLabel",
            label: "Estado",
            render: renderPortfolioStatusBadge
        },
        {
            key: "publicUrl",
            label: "Link",
            displayValue: row => row.publicUrl ? "Con link" : "Sin link",
            sortValue: row => row.publicUrl || "",
            render: renderBillingInvoiceDownloadLink
        }
    ];

    const copiersMaintenanceColumns = [
        {
            key: "dateValue",
            label: "Fecha",
            type: "date",
            displayValue: row => row.dateDisplay || "Sin fecha",
            sortValue: row => row.dateValue || row.dateDisplay || "",
            render: renderCopiersMaintenanceDateCell
        },
        {
            key: "equipmentSerial",
            label: "Equipo",
            displayValue: row => row.equipmentSerial || "Equipo externo"
        },
        {
            key: "clientName",
            label: "Cliente",
            displayValue: row => row.clientName || "Sin cliente"
        },
        {
            key: "technicianName",
            label: "Owner",
            displayValue: row => row.technicianName || "Sin owner"
        },
        {
            key: "maintenanceStatusLabel",
            label: "Estado",
            displayValue: row => row.maintenanceStatusLabel || "Pendiente",
            render: renderCopiersMaintenanceStatusBadge
        },
        {
            key: "detail",
            label: "Detalle",
            displayValue: getCopiersMaintenanceDetailDisplay,
            render: renderCopiersMaintenanceDetailCell
        },
        {
            key: "attachment",
            label: "Acta",
            align: "center",
            displayValue: getCopiersMaintenanceAttachmentDisplay,
            sortValue: row => row.hasAttachment ? 1 : 0,
            render: renderCopiersMaintenanceAttachmentCell
        }
    ];

    const copiersMovementsColumns = [
        {
            key: "dateValue",
            label: "Fecha movimiento",
            type: "date",
            displayValue: row => row.dateDisplay || "Sin fecha",
            sortValue: row => `${row.dateValue || row.dateDisplay || ""}|${row.createdOnValue || ""}`
        },
        {
            key: "equipmentSerial",
            label: "Equipo",
            displayValue: row => row.equipmentSerial || "Sin equipo"
        },
        {
            key: "clientName",
            label: "Cliente nuevo",
            displayValue: row => row.clientName || "Sin cliente"
        },
        {
            key: "reason",
            label: "Motivo movimiento",
            displayValue: row => row.reason || "Sin motivo"
        },
        {
            key: "attachment",
            label: "Acta de entrega",
            displayValue: getCopiersMovementAttachmentDisplay,
            sortValue: row => row.hasAttachment ? 1 : 0,
            render: renderCopiersMovementAttachmentCell
        }
    ];

    const portfolioOverdueColumns = [
        { key: "invoiceNumber", label: "Factura" },
        { key: "clientName", label: "Cliente" },
        { key: "verticalLabel", label: "Vertical" },
        { key: "contractTypeLabel", label: "Contrato" },
        {
            key: "dueDateDisplay",
            label: "Vencimiento",
            type: "date",
            sortValue: row => parsePortfolioDisplayDate(row.dueDateDisplay)
        },
        {
            key: "ageDays",
            label: "Dias vencida",
            type: "number",
            align: "end",
            displayValue: row => `${numberFormatter.format(Number(row.ageDays || 0))} dias`,
            render: row => `<span class="dashboard-badge">${escapeHtml(numberFormatter.format(Number(row.ageDays || 0)))} dias</span>`
        },
        {
            key: "netTotalInvoice",
            label: "Valor neto",
            type: "number",
            align: "end",
            displayValue: row => currencyFormatter.format(getNetInvoiceTotal(row)),
            render: row => renderPortfolioCurrency(getNetInvoiceTotal(row))
        }
    ];

    const portfolioInvoiceColumns = [
        { key: "recordId", label: "ID registro" },
        { key: "invoiceNumber", label: "Factura" },
        { key: "clientName", label: "Cliente" },
        { key: "clientId", label: "ID cliente" },
        { key: "companyTaxId", label: "NIT empresa" },
        { key: "verticalLabel", label: "Vertical" },
        { key: "contractTypeLabel", label: "Contrato" },
        {
            key: "emissionDateValue",
            label: "Emision",
            type: "date",
            displayValue: row => row.emissionDateDisplay || "Sin fecha",
            sortValue: row => row.emissionDateValue || ""
        },
        {
            key: "dueDateValue",
            label: "Vencimiento",
            type: "date",
            displayValue: row => row.dueDateDisplay || "Sin fecha",
            sortValue: row => row.dueDateValue || ""
        },
        {
            key: "paymentDateValue",
            label: "Pago",
            type: "date",
            displayValue: row => row.paymentDateDisplay || "Sin pago",
            sortValue: row => row.paymentDateValue || ""
        },
        {
            key: "paymentStatusLabel",
            label: "Estado",
            render: renderPortfolioStatusBadge
        },
        {
            key: "ageDays",
            label: "Dias vencida",
            type: "number",
            align: "end",
            displayValue: row => `${numberFormatter.format(Number(row.ageDays || 0))} dias`,
            render: row => `${renderPortfolioNumber(row.ageDays)} dias`
        },
        {
            key: "totalInvoice",
            label: "Total factura",
            type: "number",
            align: "end",
            displayValue: row => currencyFormatter.format(Number(row.totalInvoice || 0)),
            render: row => renderPortfolioCurrency(row.totalInvoice)
        },
        {
            key: "creditNoteTotal",
            label: "Notas credito",
            type: "number",
            align: "end",
            displayValue: row => currencyFormatter.format(Number(row.creditNoteTotal || 0)),
            render: row => renderPortfolioCurrency(row.creditNoteTotal)
        },
        {
            key: "netTotalInvoice",
            label: "Total neto",
            type: "number",
            align: "end",
            displayValue: row => currencyFormatter.format(getNetInvoiceTotal(row)),
            render: row => renderPortfolioCurrency(getNetInvoiceTotal(row))
        },
        {
            key: "vatPercent",
            label: "IVA %",
            type: "number",
            align: "end",
            displayValue: row => `${numberFormatter.format(Number(row.vatPercent || 0))}%`,
            render: row => renderPortfolioPercent(row.vatPercent)
        },
        {
            key: "vatValue",
            label: "IVA valor",
            type: "number",
            align: "end",
            displayValue: row => currencyFormatter.format(Number(row.vatValue || 0)),
            render: row => renderPortfolioCurrency(row.vatValue)
        },
        {
            key: "paymentValue",
            label: "Valor pago",
            type: "number",
            align: "end",
            displayValue: row => currencyFormatter.format(Number(row.paymentValue || 0)),
            render: row => renderPortfolioCurrency(row.paymentValue)
        },
        {
            key: "reteIcaValue",
            label: "ReteICA",
            type: "number",
            align: "end",
            displayValue: row => currencyFormatter.format(Number(row.reteIcaValue || 0)),
            render: row => renderPortfolioCurrency(row.reteIcaValue)
        },
        {
            key: "rteIvaValue",
            label: "RteIVA",
            type: "number",
            align: "end",
            displayValue: row => currencyFormatter.format(Number(row.rteIvaValue || 0)),
            render: row => renderPortfolioCurrency(row.rteIvaValue)
        },
        {
            key: "rteFteValue",
            label: "RteFte",
            type: "number",
            align: "end",
            displayValue: row => currencyFormatter.format(Number(row.rteFteValue || 0)),
            render: row => renderPortfolioCurrency(row.rteFteValue)
        },
        {
            key: "retentionsTotal",
            label: "Retenciones",
            type: "number",
            align: "end",
            displayValue: row => currencyFormatter.format(Number(row.retentionsTotal || 0)),
            render: row => renderPortfolioCurrency(row.retentionsTotal)
        },
        {
            key: "differenceValue",
            label: "Diferencia",
            type: "number",
            align: "end",
            displayValue: row => currencyFormatter.format(Number(row.differenceValue || 0)),
            render: row => renderPortfolioCurrency(row.differenceValue)
        },
        {
            key: "publicUrl",
            label: "URL factura",
            displayValue: row => row.publicUrl ? "Con URL" : "Sin URL",
            sortValue: row => row.publicUrl || "",
            render: row => row.publicUrl
                ? `<a href="${escapeHtml(row.publicUrl)}" target="_blank" rel="noopener noreferrer" class="dashboard-table-link">Abrir</a>`
                : "-"
        }
    ];

    function getPortfolioGridConfig(tableKey) {
        if (tableKey === "copiersMaintenance") {
            return {
                key: "copiersMaintenance",
                columns: copiersMaintenanceColumns,
                rows: getBaseFilteredCopiersMaintenanceRows(),
                head: copiersMaintenanceHead,
                body: copiersMaintenanceBody,
                counter: copiersMaintenanceResultsCount,
                searchTerm: "",
                emptyMessage: "No hay mantenimientos para los filtros seleccionados."
            };
        }

        if (tableKey === "copiersMovements") {
            return {
                key: "copiersMovements",
                columns: copiersMovementsColumns,
                rows: Array.isArray(state.copiersMovementsDashboard?.records) ? state.copiersMovementsDashboard.records : [],
                head: copiersMovementsHead,
                body: copiersMovementsBody,
                counter: copiersMovementsResultsCount,
                searchTerm: "",
                emptyMessage: state.copiersMovementsDashboard?.emptyStateMessage || "No hay movimientos de equipos registrados."
            };
        }

        if (tableKey === "billingInvoices") {
            return {
                key: "billingInvoices",
                columns: billingInvoiceColumns,
                rows: Array.isArray(state.billingInvoicesDetail?.invoices) ? state.billingInvoicesDetail.invoices : [],
                head: billingInvoicesHead,
                body: billingInvoicesBody,
                counter: billingInvoicesResultsCount,
                searchTerm: state.billingInvoicesSearchTerm,
                emptyMessage: state.billingInvoicesGrid.duplicatesOnly
                    ? "No encontramos facturas duplicadas por numero."
                    : "No hay facturas registradas en facturacion.",
                includeSelection: true,
                rowId: row => row.recordId || "",
                rowClass: row => isBillingInvoiceDuplicate(row) ? "dashboard-billing-invoice-row is-duplicate" : "dashboard-billing-invoice-row"
            };
        }

        if (tableKey === "invoices") {
            return {
                key: "invoices",
                columns: portfolioInvoiceColumns,
                rows: Array.isArray(state.portfolioDashboard?.invoices) ? state.portfolioDashboard.invoices : [],
                head: portfolioInvoicesHead,
                body: portfolioInvoicesBody,
                counter: portfolioInvoicesResultsCount,
                searchTerm: state.portfolioInvoicesSearchTerm,
                emptyMessage: "No hay facturas registradas en facturacion."
            };
        }

        return {
            key: "overdue",
            columns: portfolioOverdueColumns,
            rows: Array.isArray(state.portfolioDashboard?.overdueInvoices) ? state.portfolioDashboard.overdueInvoices : [],
            head: portfolioOverdueHead,
            body: portfolioUnpaidBody,
            counter: portfolioResultsCount,
            searchTerm: state.portfolioSearchTerm,
            emptyMessage: "No hay facturas vencidas sin pago en este momento."
        };
    }

    function getPortfolioGridState(tableKey) {
        if (tableKey === "copiersMaintenance") {
            return state.copiersMaintenanceGrid;
        }

        if (tableKey === "copiersMovements") {
            return state.copiersMovementsGrid;
        }

        if (tableKey === "billingInvoices") {
            return state.billingInvoicesGrid;
        }

        if (!state.portfolioGrids[tableKey]) {
            state.portfolioGrids[tableKey] = {
                sortKey: "",
                sortDirection: "asc",
                filters: {}
            };
        }

        return state.portfolioGrids[tableKey];
    }

    function getPortfolioColumnAlignClass(column) {
        if (column?.align === "end") {
            return "text-end";
        }

        if (column?.align === "center") {
            return "text-center";
        }

        return "";
    }

    function getPortfolioColumnDisplay(row, column) {
        if (typeof column.displayValue === "function") {
            return (column.displayValue(row) ?? "").toString();
        }

        return (row?.[column.key] ?? "").toString().trim() || "-";
    }

    function getPortfolioColumnSortValue(row, column) {
        if (typeof column.sortValue === "function") {
            return column.sortValue(row);
        }

        return row?.[column.key] ?? "";
    }

    function getPortfolioColumnCell(row, column) {
        if (typeof column.render === "function") {
            return column.render(row);
        }

        return renderPortfolioText(getPortfolioColumnDisplay(row, column));
    }

    function isPortfolioColumnFiltered(tableKey, columnKey) {
        const filter = getPortfolioGridState(tableKey).filters[columnKey];
        return Boolean(filter && (((filter.query || "").trim()) || Array.isArray(filter.selected)));
    }

    function rowMatchesPortfolioFilters(row, config) {
        const gridState = getPortfolioGridState(config.key);
        const globalTerm = normalizeText(config.searchTerm);

        if (config.key === "billingInvoices"
            && state.billingInvoicesGrid.duplicatesOnly
            && !isBillingInvoiceDuplicate(row)) {
            return false;
        }

        if (globalTerm) {
            const rowSearchText = normalizeText(config.columns
                .map(column => getPortfolioColumnDisplay(row, column))
                .join(" "));
            if (!rowSearchText.includes(globalTerm)) {
                return false;
            }
        }

        return config.columns.every(column => {
            const filter = gridState.filters[column.key];
            if (!filter) {
                return true;
            }

            const displayValue = getPortfolioColumnDisplay(row, column);
            if ((filter.query || "").trim() && !normalizeText(displayValue).includes(normalizeText(filter.query))) {
                return false;
            }

            if (Array.isArray(filter.selected) && !filter.selected.includes(displayValue)) {
                return false;
            }

            return true;
        });
    }

    function comparePortfolioValues(left, right, column) {
        const leftIsEmpty = left === null || left === undefined || left === "";
        const rightIsEmpty = right === null || right === undefined || right === "";
        if (leftIsEmpty && rightIsEmpty) {
            return 0;
        }

        if (leftIsEmpty) {
            return 1;
        }

        if (rightIsEmpty) {
            return -1;
        }

        if (column?.type === "number") {
            return Number(left || 0) - Number(right || 0);
        }

        return left.toString().localeCompare(right.toString(), "es", { numeric: true, sensitivity: "base" });
    }

    function getFilteredPortfolioGridRows(tableKey) {
        const config = getPortfolioGridConfig(tableKey);
        const gridState = getPortfolioGridState(tableKey);
        const sortColumn = config.columns.find(column => column.key === gridState.sortKey) || config.columns[0];
        const sortDirection = gridState.sortDirection === "desc" ? -1 : 1;

        return config.rows
            .filter(row => rowMatchesPortfolioFilters(row, config))
            .sort((left, right) => {
                const comparison = comparePortfolioValues(
                    getPortfolioColumnSortValue(left, sortColumn),
                    getPortfolioColumnSortValue(right, sortColumn),
                    sortColumn);

                if (comparison !== 0) {
                    return comparison * sortDirection;
                }

                return (left.invoiceNumber || "").localeCompare(right.invoiceNumber || "", "es", { numeric: true, sensitivity: "base" });
            });
    }

    function renderPortfolioGridHeader(tableKey) {
        const config = getPortfolioGridConfig(tableKey);
        const gridState = getPortfolioGridState(tableKey);
        if (!config.head) {
            return;
        }

        const selectionHeader = config.includeSelection
            ? `
                <th class="dashboard-selection-cell">
                    <input type="checkbox" class="form-check-input" data-billing-invoices-select-all aria-label="Seleccionar facturas visibles" />
                </th>
            `
            : "";

        config.head.innerHTML = selectionHeader + config.columns.map(column => {
            const isSorted = gridState.sortKey === column.key;
            const sortLabel = isSorted ? (gridState.sortDirection === "desc" ? "Desc" : "Asc") : "";
            const thClass = getPortfolioColumnAlignClass(column);
            const buttonClass = isPortfolioColumnFiltered(tableKey, column.key)
                ? "dashboard-column-filter__button is-filtered"
                : "dashboard-column-filter__button";

            return `
                <th class="${thClass}">
                    <div class="dashboard-column-filter">
                        <button type="button"
                                class="${buttonClass}"
                                data-portfolio-grid="${escapeHtml(tableKey)}"
                                data-portfolio-column="${escapeHtml(column.key)}"
                                aria-expanded="false"
                                title="Ordenar y filtrar ${escapeHtml(column.label)}">
                            <span>${escapeHtml(column.label)}</span>
                            <span class="dashboard-column-filter__state">${escapeHtml(sortLabel)}</span>
                            <span class="dashboard-column-filter__glyph">v</span>
                        </button>
                    </div>
                </th>
            `;
        }).join("");
    }

    function renderPortfolioGrid(tableKey) {
        const config = getPortfolioGridConfig(tableKey);
        const filteredRows = getFilteredPortfolioGridRows(tableKey);

        renderPortfolioGridHeader(tableKey);

        if (config.counter) {
            config.counter.textContent = `Mostrando ${numberFormatter.format(filteredRows.length)} de ${numberFormatter.format(config.rows.length)} registros`;
        }

        if (!config.body) {
            return;
        }

        config.body.innerHTML = filteredRows.length
            ? filteredRows.map(row => {
                const rowId = typeof config.rowId === "function" ? config.rowId(row) : "";
                const rowClass = typeof config.rowClass === "function" ? config.rowClass(row) : "";
                const selectionCell = config.includeSelection
                    ? `
                        <td class="dashboard-selection-cell">
                            <input type="checkbox"
                                   class="form-check-input"
                                   data-billing-invoice-select
                                   data-record-id="${escapeHtml(rowId)}"
                                   aria-label="Seleccionar factura ${escapeHtml(row.invoiceNumber || "")}"
                                   ${state.billingInvoiceSelectedIds.has(rowId) ? "checked" : ""} />
                        </td>
                    `
                    : "";

                return `
                <tr ${rowId ? `data-billing-invoice-id="${escapeHtml(rowId)}"` : ""} class="${escapeHtml(rowClass)}">
                    ${selectionCell}
                    ${config.columns.map(column => `
                        <td class="${getPortfolioColumnAlignClass(column)}">
                            ${getPortfolioColumnCell(row, column)}
                        </td>
                    `).join("")}
                </tr>
            `;
            }).join("")
            : `<tr><td colspan="${config.columns.length + (config.includeSelection ? 1 : 0)}" class="dashboard-table__empty">${escapeHtml(config.emptyMessage)}</td></tr>`;

        if (config.includeSelection) {
            syncBillingInvoicesSelectionSummary();
        }
    }

    function buildPortfolioColumnFilterValues(tableKey, columnKey) {
        const config = getPortfolioGridConfig(tableKey);
        const column = config.columns.find(item => item.key === columnKey);
        const counts = new Map();

        if (!column) {
            return [];
        }

        config.rows.forEach(row => {
            const displayValue = getPortfolioColumnDisplay(row, column);
            counts.set(displayValue, (counts.get(displayValue) || 0) + 1);
        });

        return Array.from(counts.entries())
            .map(([value, count]) => ({ value, count }))
            .sort((left, right) => left.value.localeCompare(right.value, "es", { numeric: true, sensitivity: "base" }));
    }

    function closePortfolioColumnMenu() {
        document.querySelectorAll(".dashboard-column-menu").forEach(menu => menu.remove());
        document.querySelectorAll("[data-portfolio-column][aria-expanded='true']").forEach(button => {
            button.setAttribute("aria-expanded", "false");
        });
    }

    function renderGridByKey(tableKey) {
        if (tableKey === "copiersMaintenance") {
            state.copiersMaintenancePage = 1;
            renderCopiersMaintenanceTable();
            return;
        }

        if (tableKey === "copiersMovements") {
            renderCopiersMovementsDashboard(state.copiersMovementsDashboard);
            return;
        }

        if (tableKey === "billingInvoices") {
            renderBillingInvoicesTable();
            return;
        }

        renderPortfolioTable();
    }

    function openPortfolioColumnMenu(tableKey, columnKey, anchorButton) {
        const config = getPortfolioGridConfig(tableKey);
        const column = config.columns.find(item => item.key === columnKey);
        const container = anchorButton?.closest(".dashboard-column-filter");
        if (!column || !container) {
            return;
        }

        const existingOpen = anchorButton.getAttribute("aria-expanded") === "true";
        closePortfolioColumnMenu();
        if (existingOpen) {
            return;
        }

        const gridState = getPortfolioGridState(tableKey);
        const filter = gridState.filters[columnKey] || {};
        const values = buildPortfolioColumnFilterValues(tableKey, columnKey);
        const selected = Array.isArray(filter.selected) ? new Set(filter.selected) : null;
        const isNumeric = column.type === "number";
        const sortAscLabel = isNumeric ? "Menor a mayor" : "A-Z";
        const sortDescLabel = isNumeric ? "Mayor a menor" : "Z-A";
        const valueOptions = values.length
            ? values.map(item => `
                <label class="dashboard-column-menu__option">
                    <input type="checkbox"
                           data-portfolio-menu-value
                           value="${escapeHtml(item.value)}"
                           ${!selected || selected.has(item.value) ? "checked" : ""} />
                    <span>${escapeHtml(item.value)}</span>
                    <small>${escapeHtml(numberFormatter.format(item.count))}</small>
                </label>
            `).join("")
            : '<div class="dashboard-column-menu__empty">Sin valores</div>';

        const menu = document.createElement("div");
        menu.className = "dashboard-column-menu";
        menu.dataset.portfolioMenuTable = tableKey;
        menu.dataset.portfolioMenuColumn = columnKey;
        menu.innerHTML = `
            <div class="dashboard-column-menu__sort">
                <button type="button" data-portfolio-menu-action="sort-asc">${escapeHtml(sortAscLabel)}</button>
                <button type="button" data-portfolio-menu-action="sort-desc">${escapeHtml(sortDescLabel)}</button>
            </div>
            <label class="dashboard-column-menu__search">
                <span>Buscar</span>
                <input type="search" value="${escapeHtml(filter.query || "")}" data-portfolio-menu-query />
            </label>
            <div class="dashboard-column-menu__quick">
                <button type="button" data-portfolio-menu-action="select-all">Todo</button>
                <button type="button" data-portfolio-menu-action="select-none">Nada</button>
            </div>
            <div class="dashboard-column-menu__values">
                ${valueOptions}
            </div>
            <div class="dashboard-column-menu__footer">
                <button type="button" data-portfolio-menu-action="clear">Limpiar</button>
                <button type="button" data-portfolio-menu-action="apply" class="dashboard-column-menu__apply">Aplicar</button>
            </div>
        `;

        container.appendChild(menu);
        anchorButton.setAttribute("aria-expanded", "true");
        menu.querySelector("[data-portfolio-menu-query]")?.focus({ preventScroll: true });
    }

    function setPortfolioColumnFilter(tableKey, columnKey, filter) {
        const gridState = getPortfolioGridState(tableKey);
        const hasQuery = Boolean((filter.query || "").trim());
        const hasSelectedFilter = Array.isArray(filter.selected);

        if (!hasQuery && !hasSelectedFilter) {
            delete gridState.filters[columnKey];
            return;
        }

        gridState.filters[columnKey] = filter;
    }

    function handlePortfolioColumnMenuAction(actionButton) {
        const menu = actionButton.closest(".dashboard-column-menu");
        if (!menu) {
            return;
        }

        const tableKey = menu.dataset.portfolioMenuTable || "overdue";
        const columnKey = menu.dataset.portfolioMenuColumn || "";
        const gridState = getPortfolioGridState(tableKey);
        const action = actionButton.dataset.portfolioMenuAction || "";

        if (action === "sort-asc" || action === "sort-desc") {
            gridState.sortKey = columnKey;
            gridState.sortDirection = action === "sort-desc" ? "desc" : "asc";
            closePortfolioColumnMenu();
            renderGridByKey(tableKey);
            return;
        }

        if (action === "select-all" || action === "select-none") {
            menu.querySelectorAll("[data-portfolio-menu-value]").forEach(input => {
                input.checked = action === "select-all";
            });
            return;
        }

        if (action === "clear") {
            delete gridState.filters[columnKey];
            closePortfolioColumnMenu();
            renderGridByKey(tableKey);
            return;
        }

        if (action === "apply") {
            const query = (menu.querySelector("[data-portfolio-menu-query]")?.value || "").trim();
            const inputs = Array.from(menu.querySelectorAll("[data-portfolio-menu-value]"));
            const checkedValues = inputs
                .filter(input => input.checked)
                .map(input => input.value);
            const selected = inputs.length > 0 && checkedValues.length !== inputs.length
                ? checkedValues
                : null;

            setPortfolioColumnFilter(tableKey, columnKey, { query, selected });
            closePortfolioColumnMenu();
            renderGridByKey(tableKey);
        }
    }

    function resetPortfolioGrid(tableKey) {
        const gridState = getPortfolioGridState(tableKey);
        gridState.filters = {};
        gridState.sortKey = tableKey === "copiersMaintenance"
            ? "dateValue"
            : tableKey === "copiersMovements"
                ? "dateValue"
            : tableKey === "invoices" || tableKey === "billingInvoices" ? "emissionDateValue" : "ageDays";
        gridState.sortDirection = "desc";

        if (tableKey === "copiersMaintenance") {
            state.copiersMaintenancePage = 1;
        } else if (tableKey === "billingInvoices") {
            state.billingInvoicesSearchTerm = "";
            state.billingInvoicesGrid.duplicatesOnly = false;
            billingInvoicesSearch && (billingInvoicesSearch.value = "");
        } else if (tableKey === "invoices") {
            state.portfolioInvoicesSearchTerm = "";
            portfolioInvoicesSearch && (portfolioInvoicesSearch.value = "");
        } else {
            state.portfolioSearchTerm = "";
            portfolioClientSearch && (portfolioClientSearch.value = "");
        }

        closePortfolioColumnMenu();
        renderGridByKey(tableKey);
    }

    function showBillingDuplicateRows() {
        state.billingInvoicesSearchTerm = "";
        state.billingInvoicesGrid.filters = {};
        state.billingInvoicesGrid.sortKey = "invoiceNumber";
        state.billingInvoicesGrid.sortDirection = "asc";
        state.billingInvoicesGrid.duplicatesOnly = true;
        state.billingInvoiceSelectedIds.clear();
        billingInvoicesSearch && (billingInvoicesSearch.value = "");
        closePortfolioColumnMenu();
        renderBillingInvoicesTable();
    }

    function renderBillingInvoicesTable() {
        renderPortfolioGrid("billingInvoices");
        syncBillingInvoicesSelectionSummary();
    }

    function getFilteredBillingInvoiceRows() {
        return getFilteredPortfolioGridRows("billingInvoices");
    }

    function pruneBillingInvoiceSelections() {
        const rows = Array.isArray(state.billingInvoicesDetail?.invoices)
            ? state.billingInvoicesDetail.invoices
            : [];
        const availableIds = new Set(rows.map(row => row?.recordId || "").filter(Boolean));
        state.billingInvoiceSelectedIds = new Set(
            Array.from(state.billingInvoiceSelectedIds).filter(id => availableIds.has(id))
        );
    }

    function getSelectedBillingInvoiceIds() {
        return Array.from(state.billingInvoiceSelectedIds).filter(Boolean);
    }

    function syncBillingInvoicesSelectionSummary() {
        const selectedIds = getSelectedBillingInvoiceIds();
        const visibleRows = getFilteredBillingInvoiceRows();
        const visibleIds = visibleRows.map(row => row?.recordId || "").filter(Boolean);
        const visibleSelectedCount = visibleIds.filter(id => state.billingInvoiceSelectedIds.has(id)).length;
        const selectAll = billingInvoicesHead?.querySelector("[data-billing-invoices-select-all]");

        if (billingInvoicesSelectedCount) {
            billingInvoicesSelectedCount.textContent = numberFormatter.format(selectedIds.length);
        }

        if (selectAll) {
            selectAll.checked = visibleIds.length > 0 && visibleSelectedCount === visibleIds.length;
            selectAll.indeterminate = visibleSelectedCount > 0 && visibleSelectedCount < visibleIds.length;
        }

        if (billingInvoicesDeleteButton) {
            billingInvoicesDeleteButton.disabled = state.billingInvoicesLoading
                || state.billingInvoicesDeleting
                || selectedIds.length === 0
                || !buildBillingInvoicesDeleteUrl();
        }

        if (billingInvoicesContractButton) {
            billingInvoicesContractButton.disabled = state.billingInvoicesLoading
                || state.billingInvoicesContractSaving
                || selectedIds.length === 0
                || !buildBillingInvoicesContractUrl();
        }
    }

    function getBillingInvoiceById(recordId) {
        const rows = Array.isArray(state.billingInvoicesDetail?.invoices)
            ? state.billingInvoicesDetail.invoices
            : [];

        return rows.find(row => (row?.recordId || "") === recordId) || null;
    }

    function renderBillingOptionSelect(select, options, selectedValue, includeEmpty = true) {
        if (!select) {
            return;
        }

        const items = Array.isArray(options) ? options : [];
        select.innerHTML = [
            includeEmpty ? '<option value="">Sin seleccionar</option>' : "",
            ...items.map(option => `
                <option value="${escapeHtml(String(option?.value ?? ""))}" ${Number(option?.value) === Number(selectedValue) ? "selected" : ""}>
                    ${escapeHtml(option?.label || "")}
                </option>
            `)
        ].join("");
    }

    function fillBillingInvoiceEditor(row) {
        state.billingInvoiceEditorOriginal = row ? { ...row } : null;
        billingInvoiceRecordIdInput && (billingInvoiceRecordIdInput.value = row?.recordId || "");
        billingInvoiceNumberInput && (billingInvoiceNumberInput.value = row?.invoiceNumber || "");
        billingInvoiceClientIdInput && (billingInvoiceClientIdInput.value = row?.clientId || "");
        billingInvoiceClientNameInput && (billingInvoiceClientNameInput.value = row?.clientName || "");
        billingInvoiceCompanyTaxIdInput && (billingInvoiceCompanyTaxIdInput.value = row?.companyTaxId || "");
        renderBillingOptionSelect(billingInvoiceVerticalInput, getBillingInvoiceVerticalOptions(), row?.verticalOptionValue);
        renderBillingOptionSelect(billingInvoiceContractTypeInput, getBillingInvoiceContractTypeOptions(), row?.contractTypeOptionValue);
        billingInvoiceEmissionDateInput && (billingInvoiceEmissionDateInput.value = row?.emissionDateValue || "");
        billingInvoiceDueDateInput && (billingInvoiceDueDateInput.value = row?.dueDateValue || "");
        billingInvoicePaymentDateInput && (billingInvoicePaymentDateInput.value = row?.paymentDateValue || "");
        billingInvoiceTotalInput && (billingInvoiceTotalInput.value = formatEditableDecimalValue(row?.totalInvoice || 0));
        billingInvoiceVatPercentInput && (billingInvoiceVatPercentInput.value = formatEditableDecimalValue(row?.vatPercent || 0));
        billingInvoiceVatValueInput && (billingInvoiceVatValueInput.value = formatEditableDecimalValue(row?.vatValue || 0));
        billingInvoicePaymentValueInput && (billingInvoicePaymentValueInput.value = formatEditableDecimalValue(row?.paymentValue || 0));
        billingInvoiceReteIcaInput && (billingInvoiceReteIcaInput.value = formatEditableDecimalValue(row?.reteIcaValue || 0));
        billingInvoiceRteIvaInput && (billingInvoiceRteIvaInput.value = formatEditableDecimalValue(row?.rteIvaValue || 0));
        billingInvoiceRteFteInput && (billingInvoiceRteFteInput.value = formatEditableDecimalValue(row?.rteFteValue || 0));
        billingInvoiceDifferenceInput && (billingInvoiceDifferenceInput.value = formatEditableDecimalValue(row?.differenceValue || 0));
        billingInvoicePublicUrlInput && (billingInvoicePublicUrlInput.value = row?.publicUrl || "");
    }

    function openBillingInvoiceEditorModal(row) {
        if (!billingInvoiceEditorModal || !row) {
            return;
        }

        fillBillingInvoiceEditor(row);
        setBillingInvoiceSaving(false);
        setStatus(billingInvoiceEditorStatus, "", "");
        document.body.classList.add("dashboard-modal-open");
        billingInvoiceEditorModal.hidden = false;

        if (billingInvoiceEditorTitle) {
            billingInvoiceEditorTitle.textContent = row.invoiceNumber
                ? `Factura ${row.invoiceNumber}`
                : "Editar factura";
        }

        if (billingInvoiceEditorSubtitle) {
            billingInvoiceEditorSubtitle.textContent = row.clientName
                ? row.clientName
                : "Actualiza los campos de la factura seleccionada.";
        }

        window.setTimeout(() => billingInvoiceNumberInput?.focus(), 30);
    }

    function parseBillingInvoiceDecimalInput(input, label, allowNegative = false) {
        const rawValue = input?.value ?? "";
        if (!rawValue.toString().trim()) {
            return 0;
        }

        const numericValue = parseEditableDecimalValue(rawValue);
        if (Number.isNaN(numericValue) || (!allowNegative && numericValue < 0)) {
            throw new Error(`El valor de ${label} debe ser numerico${allowNegative ? "" : " y no puede ser negativo"}.`);
        }

        return numericValue;
    }

    function readBillingOptionValue(select) {
        const rawValue = (select?.value || "").trim();
        if (!rawValue) {
            return null;
        }

        const numericValue = Number(rawValue);
        return Number.isFinite(numericValue) ? numericValue : null;
    }

    function buildBillingInvoiceSavePayload() {
        const invoiceNumber = (billingInvoiceNumberInput?.value || "").trim();
        if (!invoiceNumber) {
            throw new Error("El numero de factura es obligatorio.");
        }

        return {
            recordId: billingInvoiceRecordIdInput?.value || "",
            invoiceNumber,
            clientId: billingInvoiceClientIdInput?.value || "",
            clientName: (billingInvoiceClientNameInput?.value || "").trim(),
            companyTaxId: (billingInvoiceCompanyTaxIdInput?.value || "").trim(),
            verticalOptionValue: readBillingOptionValue(billingInvoiceVerticalInput),
            contractTypeOptionValue: readBillingOptionValue(billingInvoiceContractTypeInput),
            emissionDateValue: billingInvoiceEmissionDateInput?.value || "",
            dueDateValue: billingInvoiceDueDateInput?.value || "",
            paymentDateValue: billingInvoicePaymentDateInput?.value || "",
            totalInvoice: parseBillingInvoiceDecimalInput(billingInvoiceTotalInput, "Total factura"),
            vatPercent: parseBillingInvoiceDecimalInput(billingInvoiceVatPercentInput, "% IVA"),
            vatValue: parseBillingInvoiceDecimalInput(billingInvoiceVatValueInput, "IVA valor"),
            paymentValue: parseBillingInvoiceDecimalInput(billingInvoicePaymentValueInput, "Valor pago"),
            reteIcaValue: parseBillingInvoiceDecimalInput(billingInvoiceReteIcaInput, "ReteICA"),
            rteIvaValue: parseBillingInvoiceDecimalInput(billingInvoiceRteIvaInput, "RteIVA"),
            rteFteValue: parseBillingInvoiceDecimalInput(billingInvoiceRteFteInput, "RteFte"),
            differenceValue: parseBillingInvoiceDecimalInput(billingInvoiceDifferenceInput, "Diferencia", true),
            publicUrl: (billingInvoicePublicUrlInput?.value || "").trim()
        };
    }

    async function saveBillingInvoiceEditor() {
        if (state.billingInvoicesSaving) {
            return;
        }

        let payload;
        try {
            payload = buildBillingInvoiceSavePayload();
        } catch (error) {
            setStatus(billingInvoiceEditorStatus, "error", error instanceof Error ? error.message : "Revisa los datos de la factura.");
            return;
        }

        const url = buildBillingInvoiceSaveUrl();
        if (!url) {
            setStatus(billingInvoiceEditorStatus, "error", "No hay una URL configurada para guardar facturas.");
            return;
        }

        setBillingInvoiceSaving(true);
        setStatus(billingInvoiceEditorStatus, "info", "Guardando factura en Dataverse...");

        try {
            const result = await fetchJson(url, {
                method: "POST",
                body: JSON.stringify(payload)
            });
            closeBillingInvoiceEditorModal();
            await loadBillingInvoices({ silent: true });
            setStatus(billingInvoicesStatus, "success", result?.message || "Factura actualizada correctamente.");
        } catch (error) {
            setStatus(billingInvoiceEditorStatus, "error", error instanceof Error ? error.message : "No fue posible actualizar la factura.");
        } finally {
            setBillingInvoiceSaving(false);
        }
    }

    function openBillingContractTypeModal() {
        const selectedIds = getSelectedBillingInvoiceIds();
        if (!selectedIds.length || !billingContractTypeModal) {
            setStatus(billingInvoicesStatus, "error", "Selecciona al menos una factura.");
            return;
        }

        renderBillingOptionSelect(billingContractTypeBulkInput, getBillingInvoiceContractTypeOptions(), null, false);
        if (billingContractTypeSelectedCount) {
            billingContractTypeSelectedCount.textContent = numberFormatter.format(selectedIds.length);
        }

        setStatus(billingContractTypeStatus, "", "");
        setBillingInvoicesContractSaving(false);
        document.body.classList.add("dashboard-modal-open");
        billingContractTypeModal.hidden = false;
        window.setTimeout(() => billingContractTypeBulkInput?.focus(), 30);
    }

    async function saveBillingContractTypeChange() {
        if (state.billingInvoicesContractSaving) {
            return;
        }

        const selectedIds = getSelectedBillingInvoiceIds();
        const contractTypeOptionValue = readBillingOptionValue(billingContractTypeBulkInput);
        if (!selectedIds.length) {
            setStatus(billingContractTypeStatus, "error", "Selecciona al menos una factura.");
            return;
        }

        if (!contractTypeOptionValue) {
            setStatus(billingContractTypeStatus, "error", "Selecciona el nuevo tipo de contrato.");
            return;
        }

        setBillingInvoicesContractSaving(true);
        setStatus(billingContractTypeStatus, "info", "Aplicando cambio masivo en Dataverse...");

        try {
            const result = await fetchJson(buildBillingInvoicesContractUrl(), {
                method: "POST",
                body: JSON.stringify({
                    recordIds: selectedIds,
                    contractTypeOptionValue
                })
            });
            closeBillingContractTypeModal();
            state.billingInvoiceSelectedIds.clear();
            await loadBillingInvoices({ silent: true });
            setStatus(billingInvoicesStatus, "success", result?.message || "Tipo de contrato actualizado.");
        } catch (error) {
            setStatus(billingContractTypeStatus, "error", error instanceof Error ? error.message : "No fue posible cambiar el tipo de contrato.");
        } finally {
            setBillingInvoicesContractSaving(false);
        }
    }

    async function deleteSelectedBillingInvoices() {
        if (state.billingInvoicesDeleting) {
            return;
        }

        const selectedIds = getSelectedBillingInvoiceIds();
        if (!selectedIds.length) {
            setStatus(billingInvoicesStatus, "error", "Selecciona al menos una factura para eliminar.");
            return;
        }

        const confirmed = window.confirm(`Vas a eliminar ${selectedIds.length} factura(s) de Dataverse. Esta accion no se puede deshacer.`);
        if (!confirmed) {
            return;
        }

        setBillingInvoicesDeleting(true);
        setStatus(billingInvoicesStatus, "info", "Eliminando facturas seleccionadas...");

        try {
            const result = await fetchJson(buildBillingInvoicesDeleteUrl(), {
                method: "POST",
                body: JSON.stringify({ recordIds: selectedIds })
            });
            state.billingInvoiceSelectedIds.clear();
            await loadBillingInvoices({ silent: true });
            setStatus(billingInvoicesStatus, "success", result?.message || "Facturas eliminadas correctamente.");
        } catch (error) {
            setStatus(billingInvoicesStatus, "error", error instanceof Error ? error.message : "No fue posible eliminar las facturas.");
        } finally {
            setBillingInvoicesDeleting(false);
        }
    }

    function renderPortfolioTable() {
        renderPortfolioGrid("overdue");
        renderPortfolioGrid("invoices");
    }

    function updateHeroForBilling(dashboard) {
        compareLabel && (compareLabel.textContent = dashboard?.compareLabel || "Mismo periodo del ano anterior");
        granularityLabel && (granularityLabel.textContent = dashboard?.granularityLabel || "Mensual");
        recordCount && (recordCount.textContent = numberFormatter.format(Number(dashboard?.recordsCount || 0)));
    }

    function updateHeroForCloudBilling(dashboard) {
        compareLabel && (compareLabel.textContent = dashboard?.asOfDateLabel ? `Corte al ${dashboard.asOfDateLabel}` : "Mes actual");
        granularityLabel && (granularityLabel.textContent = "Productos Cloud por dia de facturacion");
        recordCount && (recordCount.textContent = numberFormatter.format(Number(dashboard?.recordsCount || 0)));
    }

    function updateHeroForTaxes(dashboard) {
        compareLabel && (compareLabel.textContent = dashboard?.compareLabel || "Mismo periodo del ano anterior");
        granularityLabel && (granularityLabel.textContent = dashboard?.granularityLabel || "Mensual");
        recordCount && (recordCount.textContent = numberFormatter.format(Number(dashboard?.recordsCount || 0)));
    }

    function updateHeroForPortfolio(dashboard) {
        compareLabel && (compareLabel.textContent = dashboard?.asOfDateLabel ? `Corte al ${dashboard.asOfDateLabel}` : "Corte actual");
        granularityLabel && (granularityLabel.textContent = dashboard?.focusLabel || "Facturas vencidas sin pago");
        recordCount && (recordCount.textContent = numberFormatter.format(Number(dashboard?.recordsCount || 0)));
    }

    function updateHeroForBusiness(dashboard) {
        compareLabel && (compareLabel.textContent = dashboard?.asOfDateLabel ? `Corte al ${dashboard.asOfDateLabel}` : "Corte actual");
        granularityLabel && (granularityLabel.textContent = dashboard?.focusLabel || "Productos Cloud agrupados por cliente");
        recordCount && (recordCount.textContent = numberFormatter.format(Number(dashboard?.recordsCount || 0)));
    }

    function updateHeroForBusinessBilling(dashboard) {
        compareLabel && (compareLabel.textContent = dashboard?.periodLabel || "Facturacion de negocio");
        granularityLabel && (granularityLabel.textContent = dashboard?.focusLabel || "Ventas por vertical y tipo de contrato");
        recordCount && (recordCount.textContent = numberFormatter.format(Number(dashboard?.recordsCount || 0)));
    }

    function updateHeroForCopiers(dashboard) {
        const fallbackFocus = state.copiersSubtab === "equipment"
            ? "Equipos asignados, stock y disponibilidad"
            : state.copiersSubtab === "inventory"
                ? "Valores comerciales y referencias pendientes"
            : state.copiersSubtab === "movements"
                ? "Historial de cambios de cliente por equipo"
            : state.copiersSubtab === "counters"
                ? "Consumo mensual de copias y escaneos"
            : state.copiersSubtab === "maintenance"
                ? "Mantenimientos, owners y actas"
                : "Ordenado por dia de facturacion";
        const focusLabel = state.copiersSubtab === "billing"
            || state.copiersSubtab === "counters"
            || state.copiersSubtab === "inventory"
            || state.copiersSubtab === "movements"
            ? (dashboard?.focusLabel || fallbackFocus)
            : fallbackFocus;
        const activeRecordCount = state.copiersSubtab === "maintenance"
            ? getFilteredCopiersMaintenanceRows().length
            : state.copiersSubtab === "movements"
                ? Number(dashboard?.recordsCount || 0)
            : state.copiersSubtab === "counters"
                ? Number(dashboard?.recordsCount || 0)
            : Number(dashboard?.recordsCount || 0);
        compareLabel && (compareLabel.textContent = dashboard?.asOfDateLabel ? `Corte al ${dashboard.asOfDateLabel}` : "Corte actual");
        granularityLabel && (granularityLabel.textContent = focusLabel);
        recordCount && (recordCount.textContent = numberFormatter.format(activeRecordCount));
    }

    function updateHeroForPnl(dashboard) {
        compareLabel && (compareLabel.textContent = dashboard?.monthCutoffLabel ? `Corte a ${dashboard.monthCutoffLabel} ${dashboard.year || ""}` : "Corte P&L");
        granularityLabel && (granularityLabel.textContent = dashboard?.focusLabel || "P&L mensual");
        recordCount && (recordCount.textContent = numberFormatter.format(Number(dashboard?.recordsCount || 0)));
    }

    function updateHeroForLicenciamiento(dashboard) {
        compareLabel && (compareLabel.textContent = dashboard?.yearLabel ? `Ano calendario ${dashboard.yearLabel}` : "Ano calendario");
        granularityLabel && (granularityLabel.textContent = dashboard?.focusLabel || "Licenciamiento");
        recordCount && (recordCount.textContent = numberFormatter.format(Number(dashboard?.recordsCount || 0)));
    }

    function updateHeroForUtility(dashboard) {
        compareLabel && (compareLabel.textContent = dashboard?.periodLabel ? `Utilidad ${dashboard.periodLabel}` : "Utilidad Cloud");
        granularityLabel && (granularityLabel.textContent = dashboard?.focusLabel || "Cloud Monthly y Prepaid");
        recordCount && (recordCount.textContent = numberFormatter.format(Number(dashboard?.recordsCount || 0)));
    }

    function updateHeroForYtd(dashboard) {
        compareLabel && (compareLabel.textContent = dashboard?.periodLabel || "YTD");
        granularityLabel && (granularityLabel.textContent = dashboard?.focusLabel || "Ventas, gastos y utilidad por mes");
        recordCount && (recordCount.textContent = numberFormatter.format(Number(dashboard?.recordsCount || 0)));
    }

    function updateBillingContext(dashboard) {
        state.billingDashboard = dashboard;
        state.billingSignature = getPeriodSignature();
        periodLabel && (periodLabel.textContent = dashboard?.periodLabel || "Sin periodo");
        dateRangeLabel && (dateRangeLabel.textContent = dashboard?.dateRangeLabel || "-");

        if (state.activeTab === "billing" && state.billingSubtab === "overview") {
            updateHeroForBilling(dashboard);
        }
    }

    function updateCloudBillingContext(dashboard) {
        state.cloudBillingDashboard = dashboard;
        cloudBillingPeriodLabel && (cloudBillingPeriodLabel.textContent = dashboard?.periodLabel || "Mes actual");
        cloudBillingDateRangeLabel && (cloudBillingDateRangeLabel.textContent = dashboard?.dateRangeLabel || "-");

        if (state.activeTab === "billing" && state.billingSubtab === "current-month") {
            updateHeroForCloudBilling(dashboard);
        }
    }

    function updateHeroForBillingCreditNotes(detail) {
        compareLabel && (compareLabel.textContent = "Consulta histórica");
        granularityLabel && (granularityLabel.textContent = "Notas crédito y cruce seguro");
        recordCount && (recordCount.textContent = numberFormatter.format(Number(detail?.recordsCount || 0)));
    }

    function updateTaxesContext(dashboard) {
        syncTaxesFiltersFromDashboard(dashboard);
        state.taxesDashboard = dashboard;
        state.taxesSignature = getTaxesSignature();
        periodLabel && (periodLabel.textContent = dashboard?.periodLabel || "Sin periodo");
        dateRangeLabel && (dateRangeLabel.textContent = dashboard?.dateRangeLabel || "-");

        if (state.activeTab === "taxes") {
            updateHeroForTaxes(dashboard);
        }
    }

    function updatePortfolioContext(dashboard) {
        state.portfolioDashboard = dashboard;
        portfolioAsOfLabel && (portfolioAsOfLabel.textContent = dashboard?.asOfDateLabel || "Sin corte");
        portfolioFocusLabel && (portfolioFocusLabel.textContent = dashboard?.focusLabel || "Facturas vencidas sin pago");

        if (state.activeTab === "portfolio") {
            updateHeroForPortfolio(dashboard);
        }
    }

    function resolveBusinessFocusLabel(dashboard) {
        if (state.businessSubtab === "projection") {
            return `Proyeccion ${dashboard?.projection?.periodLabel || ""}`.trim();
        }

        return dashboard?.focusLabel || "Productos Cloud agrupados por cliente";
    }

    function updateBusinessContext(dashboard) {
        state.businessDashboard = dashboard;
        businessAsOfLabel && (businessAsOfLabel.textContent = dashboard?.asOfDateLabel || "Sin corte");
        businessFocusLabel && (businessFocusLabel.textContent = resolveBusinessFocusLabel(dashboard));

        if (state.activeTab === "business") {
            updateHeroForBusiness(dashboard);
        }
    }

    function updateBusinessBillingContext(dashboard) {
        state.businessBillingDashboard = dashboard;
        state.businessBillingStart = normalizeBusinessBillingMonthKey(dashboard?.startMonthValue) || state.businessBillingStart;
        state.businessBillingEnd = normalizeBusinessBillingMonthKey(dashboard?.endMonthValue) || state.businessBillingEnd;
        state.businessBillingGranularity = dashboard?.granularity || state.businessBillingGranularity || "month";
        state.businessBillingSignature = getBusinessBillingSignature();

        businessBillingPeriodLabel && (businessBillingPeriodLabel.textContent = dashboard?.periodLabel || "Sin periodo");
        businessBillingDateRangeLabel && (businessBillingDateRangeLabel.textContent = dashboard?.dateRangeLabel || "-");
        businessBillingTotalSales && (businessBillingTotalSales.textContent = currencyFormatter.format(Number(dashboard?.totalSales || 0)));
        businessBillingRecordCount && (businessBillingRecordCount.textContent = numberFormatter.format(Number(dashboard?.recordsCount || 0)));
        businessBillingGranularityLabel && (businessBillingGranularityLabel.textContent = dashboard?.granularityLabel || "Mes");
        businessBillingCloudTotal && (businessBillingCloudTotal.textContent = currencyFormatter.format(Number(dashboard?.cloud?.totalSales || 0)));
        businessBillingCopiersTotal && (businessBillingCopiersTotal.textContent = currencyFormatter.format(Number(dashboard?.copiers?.totalSales || 0)));

        if (businessBillingStartFilter) {
            businessBillingStartFilter.value = state.businessBillingStart;
        }

        if (businessBillingEndFilter) {
            businessBillingEndFilter.value = state.businessBillingEnd;
        }

        if (businessBillingGranularityFilter) {
            businessBillingGranularityFilter.value = state.businessBillingGranularity;
        }

        if (state.activeTab === "business-billing") {
            updateHeroForBusinessBilling(dashboard);
        }
    }

    function updateCopiersContext(dashboard) {
        state.copiersDashboard = dashboard;
        copiersAsOfLabel && (copiersAsOfLabel.textContent = dashboard?.asOfDateLabel || "Sin corte");
        copiersFocusLabel && (copiersFocusLabel.textContent = dashboard?.focusLabel || "Ordenado por dia de facturacion");

        if (state.activeTab === "copiers" && state.copiersSubtab === "billing") {
            updateHeroForCopiers(dashboard);
        }
    }

    function updateCopiersEquipmentContext(dashboard) {
        state.copiersEquipmentDashboard = dashboard;

        if (state.activeTab === "copiers" && state.copiersSubtab === "equipment") {
            updateHeroForCopiers(dashboard);
        }
    }

    function updateCopiersInventoryContext(dashboard) {
        state.copiersInventoryDashboard = dashboard;

        if (state.activeTab === "copiers" && state.copiersSubtab === "inventory") {
            updateHeroForCopiers(dashboard);
        }
    }

    function updateCopiersMovementsContext(dashboard) {
        state.copiersMovementsDashboard = dashboard;

        if (state.activeTab === "copiers" && state.copiersSubtab === "movements") {
            updateHeroForCopiers(dashboard);
        }
    }

    function updateCopiersCountersContext(dashboard) {
        state.copiersCountersDashboard = dashboard;
        state.copiersCountersYear = Number(dashboard?.year || state.copiersCountersYear);
        state.copiersCountersMonth = Number(dashboard?.month || state.copiersCountersMonth);
        state.copiersCountersClientId = dashboard?.selectedClientId ?? state.copiersCountersClientId ?? "";
        state.copiersCountersClientName = dashboard?.selectedClientName ?? state.copiersCountersClientName ?? "";
        state.copiersCountersSignature = getCopiersCountersSignature();
        state.copiersCountersHasAppliedFilters = true;

        if (state.activeTab === "copiers" && state.copiersSubtab === "counters") {
            updateHeroForCopiers(dashboard);
        }
    }

    function updatePnlContext(dashboard) {
        state.pnlDashboard = dashboard;
        state.pnlYear = Number(dashboard?.year || state.pnlYear);
        state.pnlMonth = Number(dashboard?.monthCutoff || state.pnlMonth || 1);
        state.pnlVertical = dashboard?.verticalKey || state.pnlVertical || "all";
        state.pnlSignature = getPnlSignature();
        pnlPeriodLabel && (pnlPeriodLabel.textContent = dashboard?.monthCutoffLabel ? `${dashboard.monthCutoffLabel} ${dashboard.year || ""}` : "Sin corte");
        pnlDateRangeLabel && (pnlDateRangeLabel.textContent = dashboard?.dateRangeLabel || "-");
        pnlDescription && (pnlDescription.textContent = dashboard?.description || "");

        if (pnlYearFilter) {
            pnlYearFilter.value = String(state.pnlYear);
        }

        if (pnlVerticalFilter) {
            pnlVerticalFilter.value = state.pnlVertical;
        }

        buildPnlMonthOptions(dashboard?.latestMonthAvailable || 12);

        if (state.activeTab === "pnl") {
            updateHeroForPnl(dashboard);
        }
    }

    function updateLicenciamientoContext(dashboard) {
        state.licenciamientoDashboard = dashboard;
        state.licenciamientoYear = Number(dashboard?.year || state.licenciamientoYear);
        state.licenciamientoMonth = Number(dashboard?.month || state.licenciamientoMonth || licenciamientoDefaultMonth);
        state.licenciamientoSignature = getLicenciamientoSignature();
        licenciamientoPeriodLabel && (licenciamientoPeriodLabel.textContent = dashboard?.yearLabel ? `${dashboard.yearLabel} - ${dashboard.monthLabel || ""}` : "Sin periodo");
        licenciamientoDateRangeLabel && (licenciamientoDateRangeLabel.textContent = dashboard?.dateRangeLabel || "-");
        licenciamientoCostPeriodLabel && (licenciamientoCostPeriodLabel.textContent = dashboard?.monthLabel || "-");

        if (licenciamientoYearFilter) {
            licenciamientoYearFilter.value = String(state.licenciamientoYear);
        }

        buildLicenciamientoMonthOptions(dashboard?.monthOptions);

        if (state.activeTab === "licenciamiento") {
            updateHeroForLicenciamiento(dashboard);
        }
    }

    function updateUtilityContext(dashboard) {
        state.utilityDashboard = dashboard;
        state.utilitySignature = getUtilitySignature();
        applySavedUtilityTheoreticalExclusions(dashboard);
        utilityPeriodLabel && (utilityPeriodLabel.textContent = dashboard?.periodLabel || "Ene 2025 - hoy");
        utilityDateRangeLabel && (utilityDateRangeLabel.textContent = dashboard?.dateRangeLabel || "-");

        if (state.activeTab === "utility") {
            updateHeroForUtility(dashboard);
        }
    }

    function updateYtdContext(dashboard) {
        state.ytdDashboard = dashboard;
        state.ytdYear = Number(dashboard?.year || state.ytdYear || currentYear);
        initializeYtdFilterState(dashboard);
        ytdPeriodLabel && (ytdPeriodLabel.textContent = dashboard?.periodLabel || `YTD ${state.ytdYear}`);
        ytdDateRangeLabel && (ytdDateRangeLabel.textContent = dashboard?.dateRangeLabel || "-");

        if (state.activeTab === "ytd") {
            updateHeroForYtd(dashboard);
        }
    }

    function syncPeriodScopeVisibility() {
        if (dashboardPeriodScope) {
            dashboardPeriodScope.hidden = state.activeTab === "today"
                || state.activeTab === "agent"
                || (state.activeTab === "billing" && state.billingSubtab !== "overview")
                || state.activeTab === "portfolio"
                || state.activeTab === "business"
                || state.activeTab === "business-billing"
                || state.activeTab === "hardware"
                || state.activeTab === "copiers"
                || state.activeTab === "pnl"
                || state.activeTab === "licenciamiento"
                || state.activeTab === "utility"
                || state.activeTab === "ytd"
                || state.activeTab === "taxes"
                || state.activeTab === "support-cloud";
        }
    }

    function splitDashboardGroupTabs(value) {
        return (value || "")
            .split(/\s+/)
            .map(item => item.trim())
            .filter(Boolean);
    }

    function syncDashboardGroupTabs() {
        dashboardGroupPanels.forEach(panel => {
            const groupTabs = splitDashboardGroupTabs(panel.dataset.dashboardGroupTabs);
            const isVisible = groupTabs.includes(state.activeTab);
            panel.hidden = !isVisible;
            panel.classList.toggle("is-active", isVisible);
        });

        dashboardGroupButtons.forEach(button => {
            const isActive = button.dataset.dashboardGroupTarget === state.activeTab;
            button.classList.toggle("is-active", isActive);
            button.setAttribute("aria-selected", isActive ? "true" : "false");
        });
    }

    function syncBillingSubtabVisibility() {
        billingSubtabButtons.forEach(button => {
            const isActive = button.dataset.billingSubtab === state.billingSubtab;
            button.classList.toggle("is-active", isActive);
            button.setAttribute("aria-selected", isActive ? "true" : "false");
        });

        billingSubpanels.forEach(panel => {
            const isActive = panel.dataset.billingSubpanel === state.billingSubtab;
            panel.classList.toggle("is-active", isActive);
            panel.hidden = !isActive;
        });

        syncPeriodScopeVisibility();
    }

    function setBillingSubtab(subtabKey) {
        const allowedSubtabs = new Set(["overview", "current-month", "credit-notes"]);
        state.billingSubtab = allowedSubtabs.has(subtabKey) ? subtabKey : "overview";
        syncBillingSubtabVisibility();

        if (state.activeTab !== "billing") {
            return;
        }

        if (state.billingSubtab === "current-month") {
            if (state.cloudBillingDashboard) {
                updateHeroForCloudBilling(state.cloudBillingDashboard);
                renderCloudBillingDashboard(state.cloudBillingDashboard);
            } else {
                loadCloudBillingCurrentMonth();
            }
            return;
        }

        if (state.billingSubtab === "credit-notes") {
            if (state.billingCreditNotesDetail) {
                updateHeroForBillingCreditNotes(state.billingCreditNotesDetail);
                renderBillingCreditNotesTable();
            } else {
                loadBillingCreditNotes();
            }
            return;
        }

        if (state.billingDashboard && state.billingSignature === getPeriodSignature()) {
            updateHeroForBilling(state.billingDashboard);
        } else {
            loadBilling();
        }
    }

    function syncPortfolioSubtabVisibility() {
        portfolioSubtabButtons.forEach(button => {
            const isActive = button.dataset.portfolioSubtab === state.portfolioSubtab;
            button.classList.toggle("is-active", isActive);
            button.setAttribute("aria-selected", isActive ? "true" : "false");
        });

        portfolioSubpanels.forEach(panel => {
            const isActive = panel.dataset.portfolioSubpanel === state.portfolioSubtab;
            panel.classList.toggle("is-active", isActive);
            panel.hidden = !isActive;
        });
    }

    function setPortfolioSubtab(subtabKey, options = {}) {
        state.portfolioSubtab = ["detail", "statement"].includes(subtabKey) ? subtabKey : "monthly";
        syncPortfolioSubtabVisibility();

        if (state.portfolioSubtab !== "monthly" && isPortfolioMonthlyDetailOpen()) {
            closePortfolioMonthlyDetailModal();
        }

        if (state.activeTab !== "portfolio") {
            return;
        }

        if (options.refresh || !state.portfolioDashboard) {
            loadPortfolio();
            return;
        }

        updateHeroForPortfolio(state.portfolioDashboard);
        if (state.portfolioSubtab === "monthly") {
            renderPortfolioMonthlyDashboard();
        } else if (state.portfolioSubtab === "detail") {
            renderPortfolioTable();
        } else {
            syncAccountStatementPdfButton();
        }
    }

    function syncBusinessSubtabVisibility() {
        businessSubtabButtons.forEach(button => {
            const isActive = button.dataset.businessSubtab === state.businessSubtab;
            button.classList.toggle("is-active", isActive);
            button.setAttribute("aria-selected", isActive ? "true" : "false");
        });

        businessSubpanels.forEach(panel => {
            const isActive = panel.dataset.businessSubpanel === state.businessSubtab;
            panel.classList.toggle("is-active", isActive);
            panel.hidden = !isActive;
        });
    }

    function setBusinessSubtab(subtabKey) {
        state.businessSubtab = subtabKey === "projection" ? "projection" : "all";
        syncBusinessSubtabVisibility();

        if (state.activeTab !== "business") {
            return;
        }

        if (state.businessDashboard) {
            businessFocusLabel && (businessFocusLabel.textContent = resolveBusinessFocusLabel(state.businessDashboard));
            updateHeroForBusiness(state.businessDashboard);
            renderBusinessDashboard(state.businessDashboard);
        } else {
            loadBusiness();
        }
    }

    function syncCopiersSubtabVisibility() {
        copiersSubtabButtons.forEach(button => {
            const isActive = button.dataset.copiersSubtab === state.copiersSubtab;
            button.classList.toggle("is-active", isActive);
            button.setAttribute("aria-selected", isActive ? "true" : "false");
        });

        copiersSubpanels.forEach(panel => {
            const isActive = panel.dataset.copiersSubpanel === state.copiersSubtab;
            panel.classList.toggle("is-active", isActive);
            panel.hidden = !isActive;
        });
    }

    function setCopiersSubtab(subtabKey) {
        state.copiersSubtab = subtabKey === "equipment"
            ? "equipment"
            : subtabKey === "inventory"
                ? "inventory"
            : subtabKey === "movements"
                ? "movements"
            : subtabKey === "counters"
                ? "counters"
            : subtabKey === "maintenance"
                ? "maintenance"
                : "billing";
        syncCopiersSubtabVisibility();

        if (state.copiersSubtab !== "billing" && isCopiersClientInvoicesOpen()) {
            closeCopiersClientInvoicesModal();
        }

        if (state.copiersSubtab !== "billing" && isCopiersLineEquipmentOpen()) {
            closeCopiersLineEquipmentModal();
        }

        if (state.copiersSubtab !== "billing" && isCopiersBillingCountersOpen()) {
            closeCopiersBillingCountersModal();
        }

        if (state.copiersSubtab !== "billing" && isCopiersEditorOpen()) {
            closeCopiersEditorModal();
        }

        if (state.copiersSubtab !== "equipment" && isCopiersEquipmentDetailOpen()) {
            closeCopiersEquipmentDetailModal();
        }

        if (state.activeTab !== "copiers") {
            return;
        }

        if (state.copiersSubtab === "equipment" || state.copiersSubtab === "maintenance") {
            if (state.copiersEquipmentDashboard) {
                updateHeroForCopiers(state.copiersEquipmentDashboard);
            } else {
                loadCopiersEquipment();
            }
            return;
        }

        if (state.copiersSubtab === "inventory") {
            updateHeroForCopiers(state.copiersInventoryDashboard || {});
            syncCopiersInventoryButtons();
            return;
        }

        if (state.copiersSubtab === "movements") {
            if (state.copiersMovementsDashboard) {
                updateHeroForCopiers(state.copiersMovementsDashboard);
                renderCopiersMovementsDashboard(state.copiersMovementsDashboard);
            } else {
                loadCopiersMovements();
            }
            return;
        }

        if (state.copiersSubtab === "counters") {
            buildCopiersCountersPeriodOptions();
            if (state.copiersCountersHasAppliedFilters
                && state.copiersCountersDashboard
                && state.copiersCountersSignature === getCopiersCountersSignature()) {
                updateHeroForCopiers(state.copiersCountersDashboard);
            } else {
                renderCopiersCountersPending();
            }
            return;
        }

        if (state.copiersDashboard) {
            updateHeroForCopiers(state.copiersDashboard);
        } else {
            loadCopiers();
        }
    }

    function setActiveTab(tabKey) {
        state.activeTab = tabKey;
        syncPeriodScopeVisibility();

        if (tabKey !== "pnl" && isPnlDetailOpen()) {
            closePnlDetailModal();
        }

        if (tabKey !== "utility" && isUtilityBreakdownOpen()) {
            closeUtilityBreakdownModal();
        }

        if (tabKey !== "utility" && isUtilityRealDetailOpen()) {
            closeUtilityRealDetailModal();
        }

        if (tabKey !== "utility" && isUtilityOrphansOpen()) {
            closeUtilityOrphansModal();
        }

        if (tabKey !== "billing" && isBillingInvoiceEditorOpen()) {
            closeBillingInvoiceEditorModal();
        }

        if (tabKey !== "billing" && isBillingContractTypeModalOpen()) {
            closeBillingContractTypeModal();
        }

        if (tabKey !== "portfolio" && isPortfolioMonthlyDetailOpen()) {
            closePortfolioMonthlyDetailModal();
        }

        if (tabKey !== "copiers" && isCopiersEditorOpen()) {
            closeCopiersEditorModal();
        }

        if (tabKey !== "copiers" && isCopiersClientInvoicesOpen()) {
            closeCopiersClientInvoicesModal();
        }

        if (tabKey !== "copiers" && isCopiersLineEquipmentOpen()) {
            closeCopiersLineEquipmentModal();
        }

        if (tabKey !== "copiers" && isCopiersBillingCountersOpen()) {
            closeCopiersBillingCountersModal();
        }

        if (tabKey !== "copiers" && isCopiersEquipmentDetailOpen()) {
            closeCopiersEquipmentDetailModal();
        }

        tabButtons.forEach(button => {
            const groupTabs = splitDashboardGroupTabs(button.dataset.dashboardGroupTabs);
            const isActive = button.dataset.dashboardTab === tabKey || groupTabs.includes(tabKey);
            button.classList.toggle("is-active", isActive);
            button.setAttribute("aria-selected", isActive ? "true" : "false");
        });

        tabPanels.forEach(panel => {
            const isActive = panel.dataset.dashboardPanel === tabKey;
            panel.classList.toggle("is-active", isActive);
            panel.hidden = !isActive;
        });

        syncDashboardGroupTabs();

        if (tabKey === "today") {
            if (state.todayDashboard) {
                renderTodayDashboard(state.todayDashboard);
            } else {
                loadToday();
            }
            return;
        }

        if (tabKey === "ytd") {
            if (state.ytdDashboard) {
                updateHeroForYtd(state.ytdDashboard);
            } else {
                loadYtd();
            }
            return;
        }

        if (tabKey === "pnl") {
            if (state.pnlDashboard && state.pnlSignature === getPnlSignature()) {
                updateHeroForPnl(state.pnlDashboard);
            } else {
                loadPnl();
            }
            return;
        }

        if (tabKey === "licenciamiento") {
            if (state.licenciamientoDashboard && state.licenciamientoSignature === getLicenciamientoSignature()) {
                updateHeroForLicenciamiento(state.licenciamientoDashboard);
            } else {
                loadLicenciamiento();
            }
            return;
        }

        if (tabKey === "utility") {
            if (state.utilityDashboard && state.utilitySignature === getUtilitySignature()) {
                updateHeroForUtility(state.utilityDashboard);
            } else {
                loadUtility();
            }
            return;
        }

        if (tabKey === "copiers") {
            setCopiersSubtab(state.copiersSubtab);
            return;
        }

        if (tabKey === "portfolio") {
            setPortfolioSubtab(state.portfolioSubtab);
            return;
        }

        if (tabKey === "business") {
            setBusinessSubtab(state.businessSubtab);
            return;
        }

        if (tabKey === "business-billing") {
            if (state.businessBillingDashboard && state.businessBillingSignature === getBusinessBillingSignature()) {
                updateHeroForBusinessBilling(state.businessBillingDashboard);
                renderBusinessBillingDashboard(state.businessBillingDashboard);
            } else {
                loadBusinessBilling();
            }
            return;
        }

        if (tabKey === "support-cloud") {
            return;
        }

        if (tabKey === "agent") {
            return;
        }

        if (tabKey === "taxes") {
            if (state.taxesDashboard && state.taxesSignature === getTaxesSignature()) {
                updateHeroForTaxes(state.taxesDashboard);
                renderTaxesDashboard(state.taxesDashboard);
            } else {
                loadTaxes();
            }
            return;
        }

        if (tabKey === "billing") {
            setBillingSubtab(state.billingSubtab);
            return;
        }

        if (state.billingDashboard && state.billingSignature === getPeriodSignature()) {
            updateHeroForBilling(state.billingDashboard);
        } else {
            loadBilling();
        }
    }

    function loadActivePeriodTab() {
        if (state.activeTab !== "billing") {
            return;
        }

        loadBilling();
    }

    function getFilteredBillingCreditNotes() {
        const rows = Array.isArray(state.billingCreditNotesDetail?.creditNotes)
            ? state.billingCreditNotesDetail.creditNotes
            : [];
        const query = normalizeText(state.billingCreditNotesSearchTerm);
        const statusFilter = state.billingCreditNotesStatusFilter || "all";

        return rows.filter(row => {
            if (statusFilter === "matched" && !row?.isMatched) {
                return false;
            }
            if (statusFilter === "unmatched" && row?.isMatched) {
                return false;
            }
            if (!query) {
                return true;
            }

            return [
                row?.creditNoteName,
                row?.creditNoteId,
                row?.invoiceReference,
                row?.matchedInvoiceNumber,
                row?.clientName,
                row?.customerIdentification,
                row?.matchBy,
                row?.dateDisplay
            ].some(value => normalizeText(value).includes(query));
        });
    }

    function renderBillingCreditNotesTable() {
        const detail = state.billingCreditNotesDetail;
        const allRows = Array.isArray(detail?.creditNotes) ? detail.creditNotes : [];
        const rows = getFilteredBillingCreditNotes();

        billingCreditNotesTotalCount && (billingCreditNotesTotalCount.textContent = numberFormatter.format(Number(detail?.recordsCount || 0)));
        billingCreditNotesTotalAmount && (billingCreditNotesTotalAmount.textContent = currencyFormatter.format(Number(detail?.totalAmount || 0)));
        billingCreditNotesMatchedAmount && (billingCreditNotesMatchedAmount.textContent = currencyFormatter.format(Number(detail?.matchedAmount || 0)));
        billingCreditNotesMatchedCount && (billingCreditNotesMatchedCount.textContent = `${numberFormatter.format(Number(detail?.matchedCount || 0))} notas`);
        billingCreditNotesUnmatchedAmount && (billingCreditNotesUnmatchedAmount.textContent = currencyFormatter.format(Number(detail?.unmatchedAmount || 0)));
        billingCreditNotesUnmatchedCount && (billingCreditNotesUnmatchedCount.textContent = `${numberFormatter.format(Number(detail?.unmatchedCount || 0))} notas`);
        billingCreditNotesResultsCount && (billingCreditNotesResultsCount.textContent = `Mostrando ${numberFormatter.format(rows.length)} de ${numberFormatter.format(allRows.length)} notas crédito`);

        if (!billingCreditNotesBody) {
            return;
        }

        if (!rows.length) {
            const message = detail?.hasData
                ? "No hay notas crédito que coincidan con los filtros."
                : (detail?.emptyStateMessage || "No encontramos notas crédito para mostrar.");
            billingCreditNotesBody.innerHTML = `<tr><td colspan="9" class="dashboard-table__empty">${escapeHtml(message)}</td></tr>`;
            return;
        }

        billingCreditNotesBody.innerHTML = rows.map(row => {
            const creditNoteName = row?.creditNoteName || "Nota crédito";
            const creditNoteId = row?.creditNoteId || "";
            const invoiceReference = row?.invoiceReference || "Sin referencia";
            const matchedInvoice = row?.matchedInvoiceNumber || "";
            const invoicePrimary = matchedInvoice || invoiceReference;
            const showOriginalReference = matchedInvoice
                && normalizeText(matchedInvoice) !== normalizeText(invoiceReference);
            const clientName = row?.clientName || "Cliente sin identificar";
            const customerIdentification = row?.customerIdentification || "Sin NIT";
            const statusLabel = row?.isMatched ? "Restada en facturación" : "Sin cruce seguro";
            const statusTone = row?.isMatched ? "success" : "warning";

            return `
                <tr class="${row?.isMatched ? "" : "dashboard-credit-note-row--unmatched"}">
                    <td class="dashboard-credit-note-date">${escapeHtml(row?.dateDisplay || "Sin fecha")}</td>
                    <td>
                        <div class="dashboard-credit-note-reference">
                            <strong>${escapeHtml(creditNoteName)}</strong>
                            ${creditNoteId && normalizeText(creditNoteId) !== normalizeText(creditNoteName)
                                ? `<small title="${escapeHtml(creditNoteId)}">ID Siigo: ${escapeHtml(creditNoteId)}</small>`
                                : ""}
                        </div>
                    </td>
                    <td>
                        <div class="dashboard-credit-note-reference">
                            <strong>${escapeHtml(invoicePrimary)}</strong>
                            ${showOriginalReference ? `<small>Referencia NC: ${escapeHtml(invoiceReference)}</small>` : ""}
                        </div>
                    </td>
                    <td>
                        <div class="dashboard-credit-note-reference">
                            <strong>${escapeHtml(clientName)}</strong>
                            <small>${escapeHtml(customerIdentification)}</small>
                        </div>
                    </td>
                    <td><span class="dashboard-status-pill dashboard-status-pill--${statusTone}">${escapeHtml(statusLabel)}</span></td>
                    <td>${escapeHtml(row?.matchBy || "-")}</td>
                    <td class="text-end dashboard-credit-note-amount">${currencyFormatter.format(Number(row?.baseValue || 0))}</td>
                    <td class="text-end dashboard-credit-note-amount">${currencyFormatter.format(Number(row?.vat || 0))}</td>
                    <td class="text-end dashboard-credit-note-amount dashboard-credit-note-amount--total">${currencyFormatter.format(Number(row?.total || 0))}</td>
                </tr>`;
        }).join("");
    }

    async function loadBillingCreditNotes() {
        const url = buildBillingCreditNotesUrl();
        if (!url || !billingCreditNotesBody) {
            return false;
        }

        setBillingCreditNotesLoading(true);
        setStatus(billingCreditNotesStatus, "info", "Consultando notas crédito y validando su cruce con facturación...");

        try {
            const detail = await fetchJson(url);
            state.billingCreditNotesDetail = detail || null;
            renderBillingCreditNotesTable();
            updateHeroForBillingCreditNotes(detail);
            setStatus(
                billingCreditNotesStatus,
                detail?.hasData ? "" : "info",
                detail?.hasData ? "" : (detail?.emptyStateMessage || "No encontramos notas crédito registradas."));
            return true;
        } catch (error) {
            state.billingCreditNotesDetail = null;
            billingCreditNotesBody.innerHTML = '<tr><td colspan="9" class="dashboard-table__empty">No pudimos consultar las notas crédito.</td></tr>';
            setStatus(billingCreditNotesStatus, "error", error instanceof Error ? error.message : "No fue posible cargar la tabla de notas crédito.");
            return false;
        } finally {
            setBillingCreditNotesLoading(false);
        }
    }

    async function loadBillingInvoices(options = {}) {
        if (Number.isFinite(Number(options.page))) {
            state.billingInvoicesPage = Math.max(1, Number(options.page));
        }
        if (Number.isFinite(Number(options.pageSize))) {
            state.billingInvoicesPageSize = Math.max(25, Number(options.pageSize));
        }
        if (typeof options.duplicatesOnly === "boolean") {
            state.billingInvoicesDuplicatesOnly = options.duplicatesOnly;
        }
        const url = buildBillingInvoicesUrl(options);
        if (!url || !billingInvoicesBody) {
            return false;
        }

        setBillingInvoicesLoading(true);
        if (!options.silent) {
            setStatus(billingInvoicesStatus, "info", "Consultando facturas en Dataverse...");
        }

        try {
            const detail = await fetchJson(url);
            const rows = Array.isArray(detail?.invoices) ? detail.invoices : [];
            state.billingInvoicesDetail = detail || null;
            state.billingInvoicesYear = Number(detail?.year || state.billingInvoicesYear);
            state.billingInvoicesMonth = Number(detail?.month || state.billingInvoicesMonth);
            state.billingInvoiceDuplicateNumbers = getBillingInvoiceDuplicateNumbers(rows);
            pruneBillingInvoiceSelections();
            syncBillingInvoicesPagination(detail);
            renderBillingInvoicesTable();
            setStatus(
                billingInvoicesStatus,
                detail?.hasData ? "" : "info",
                detail?.hasData ? "" : (detail?.emptyStateMessage || "No encontramos facturas registradas."));
            return true;
        } catch (error) {
            state.billingInvoicesDetail = null;
            state.billingInvoiceDuplicateNumbers = new Set();
            state.billingInvoiceSelectedIds.clear();
            syncBillingInvoicesPagination(null);
            if (billingInvoicesBody) {
                billingInvoicesBody.innerHTML = '<tr><td colspan="9" class="dashboard-table__empty">No pudimos consultar la tabla de facturacion.</td></tr>';
            }
            syncBillingInvoicesSelectionSummary();
            setStatus(billingInvoicesStatus, "error", error instanceof Error ? error.message : "No fue posible cargar la tabla de facturacion.");
            return false;
        } finally {
            setBillingInvoicesLoading(false);
        }
    }

    async function findDuplicateBillingInvoices() {
        setStatus(billingInvoicesStatus, "info", "Buscando números repetidos en el histórico. Esta consulta puede tardar un poco...");
        const loaded = await loadBillingInvoices({ silent: true, duplicatesOnly: true, page: 1, pageSize: 100 });
        if (!loaded) {
            return;
        }

        const duplicateCount = Number(state.billingInvoicesDetail?.totalRecordsCount || 0);
        state.billingInvoicesGrid.duplicatesOnly = false;
        renderBillingInvoicesTable();
        setStatus(
            billingInvoicesStatus,
            duplicateCount ? "success" : "info",
            duplicateCount
                ? `Encontramos ${numberFormatter.format(duplicateCount)} registros con número de factura duplicado. Selecciona manualmente los que quieras eliminar.`
                : "No encontramos numeros de factura duplicados.");
    }

    async function loadBillingReportInvoices() {
        const clientId = billingReportClientIdInput?.value || "";
        const clientName = (billingReportClientSearch?.value || "").trim();
        if (!clientId && !clientName) {
            setStatus(billingReportStatus, "error", "Busca un cliente para consultar sus facturas.");
            return;
        }

        setBillingReportLoading(true);
        setStatus(billingReportStatus, "info", "Consultando facturas del cliente...");

        try {
            const detail = await fetchJson(buildBillingClientReportUrl(clientId, clientName));
            renderBillingReportTable(detail);
            setStatus(billingReportStatus, detail?.hasData ? "" : "info", detail?.hasData ? "" : (detail?.emptyStateMessage || "No encontramos facturas para este cliente."));
        } catch (error) {
            state.billingReportDetail = null;
            resetBillingReportPreview();
            if (billingReportBody) {
                billingReportBody.innerHTML = '<tr><td colspan="8" class="dashboard-table__empty">No pudimos consultar las facturas del cliente.</td></tr>';
            }
            resetBillingReportReference();
            syncBillingReportSelectionSummary();
            setStatus(billingReportStatus, "error", error instanceof Error ? error.message : "No fue posible cargar las facturas del cliente.");
        } finally {
            setBillingReportLoading(false);
        }
    }

    async function loadSiigoCustomers() {
        const url = buildSiigoCustomersUrl();
        if (!url) {
            setStatus(siigoInvoicesStatus, "error", "No hay una URL configurada para cargar clientes desde Siigo.");
            return;
        }

        setSiigoCustomersLoading(true);
        setStatus(siigoInvoicesStatus, "info", "Cargando clientes desde Siigo...");
        resetSiigoInvoicesTable("Cargando clientes desde Siigo.");
        renderSiigoCustomerSelect("Cargando clientes...");

        try {
            const items = await fetchJson(url);
            state.siigoCustomers = Array.isArray(items) ? items : [];
            renderSiigoCustomerSelect(state.siigoCustomers.length ? "" : "Siigo no devolvio clientes.");

            if (state.siigoCustomers.length === 0) {
                setStatus(siigoInvoicesStatus, "error", "Siigo respondio correctamente, pero no devolvio clientes para mostrar.");
                resetSiigoInvoicesTable("Siigo no devolvio clientes para seleccionar.");
                return;
            }

            setStatus(siigoInvoicesStatus, "success", `Terceros cargados desde Siigo: ${buildSiigoCustomersSummary(state.siigoCustomers)}.`);
            resetSiigoInvoicesTable("Selecciona un cliente y consulta sus facturas de Siigo.");
        } catch (error) {
            state.siigoCustomers = [];
            renderSiigoCustomerSelect("No se pudieron cargar clientes.");
            resetSiigoInvoicesTable("Fallo la carga de clientes desde Siigo. Revisa el error de arriba.");
            setStatus(siigoInvoicesStatus, "error", formatSiigoUiError("cargar clientes desde Siigo", error, url));
        } finally {
            setSiigoCustomersLoading(false);
        }
    }

    async function searchSiigoCustomerByNit() {
        const query = normalizeNitValue(siigoCustomerNitSearch?.value || "");
        if (query.length < 3) {
            setStatus(siigoInvoicesStatus, "error", "Ingresa al menos 3 digitos del NIT para buscar en Siigo.");
            return;
        }

        const url = buildSiigoCustomerSearchUrl(query);
        if (!url) {
            setStatus(siigoInvoicesStatus, "error", "No hay una URL configurada para buscar clientes por NIT en Siigo.");
            return;
        }

        setSiigoCustomerNitSearching(true);
        setStatus(siigoInvoicesStatus, "info", `Buscando NIT ${query} en Siigo...`);

        try {
            const items = await fetchJson(url);
            const customers = Array.isArray(items) ? items : [];
            if (!customers.length) {
                const directCustomer = {
                    id: `nit:${query}`,
                    displayName: `NIT ${query} (consulta directa)`,
                    name: `NIT ${query}`,
                    commercialName: "",
                    identification: query,
                    type: "Consulta directa",
                    branchOffice: 0,
                    active: true,
                    directLookup: true
                };

                upsertSiigoCustomers([directCustomer]);
                renderSiigoCustomerSelect();

                if (siigoCustomerSelect) {
                    siigoCustomerSelect.value = directCustomer.id;
                }

                syncSiigoCustomerSelection();
                resetSiigoInvoicesTable("Consulta facturas directamente por este NIT.");
                setStatus(siigoInvoicesStatus, "info", `Siigo no devolvio terceros para el NIT ${query}, pero lo deje seleccionado para consultar facturas directamente por identificacion.`);
                return;
            }

            upsertSiigoCustomers(customers);
            renderSiigoCustomerSelect();

            const selected = customers[0];
            if (siigoCustomerSelect) {
                siigoCustomerSelect.value = selected?.id || "";
            }

            syncSiigoCustomerSelection();
            resetSiigoInvoicesTable("Consulta Siigo para el cliente encontrado.");
            setStatus(siigoInvoicesStatus, "success", `Cliente encontrado y agregado al dropdown: ${buildSiigoCustomerOptionLabel(selected)}.`);
        } catch (error) {
            setStatus(siigoInvoicesStatus, "error", formatSiigoUiError(`buscar el NIT ${query} en Siigo`, error, url));
        } finally {
            setSiigoCustomerNitSearching(false);
        }
    }

    async function loadSiigoInvoices() {
        const customerId = siigoCustomerSelect?.value || siigoCustomerIdInput?.value || "";
        const startDate = siigoStartDateInput?.value || "";
        const endDate = siigoEndDateInput?.value || "";

        if (!customerId) {
            setStatus(siigoInvoicesStatus, "error", "Selecciona un cliente de Siigo antes de consultar facturas.");
            return;
        }

        if (!startDate || !endDate) {
            setStatus(siigoInvoicesStatus, "error", "Selecciona la fecha inicial y final.");
            return;
        }

        if (startDate > endDate) {
            setStatus(siigoInvoicesStatus, "error", "La fecha inicial no puede ser mayor que la fecha final.");
            return;
        }

        setSiigoInvoicesLoading(true);
        setStatus(siigoInvoicesStatus, "info", "Consultando facturas en Siigo...");

        try {
            const detail = await fetchJson(buildSiigoInvoicesUrl());
            renderSiigoInvoicesTable(detail);
            setStatus(siigoInvoicesStatus, detail?.hasData ? "" : "info", detail?.hasData ? "" : (detail?.emptyStateMessage || "No encontramos facturas en Siigo para este periodo."));
        } catch (error) {
            resetSiigoInvoicesTable("No pudimos consultar las facturas de Siigo.");
            setStatus(siigoInvoicesStatus, "error", formatSiigoUiError("consultar facturas en Siigo", error, buildSiigoInvoicesUrl()));
        } finally {
            setSiigoInvoicesLoading(false);
        }
    }

    async function loadBilling() {
        setPeriodLoading(true);
        setStatus(billingStatusBanner, "info", "Actualizando tablero de facturacion...");
        if (!state.billingInvoicesDetail && !state.billingInvoicesLoading) {
            loadBillingInvoices().catch(() => {});
        }

        try {
            const dashboard = await fetchJson(buildBillingUrl());
            updateBillingContext(dashboard);
            renderComparativeKpis(billingKpisContainer, dashboard?.kpis, dashboard?.compareYear);
            renderTrends(dashboard);
            setStatus(billingStatusBanner, "", "");
        } catch (error) {
            setStatus(billingStatusBanner, "error", error instanceof Error ? error.message : "No fue posible cargar el dashboard.");
        } finally {
            setPeriodLoading(false);
        }
    }

    async function loadCloudBillingCurrentMonth() {
        const url = buildCloudBillingCurrentMonthUrl();
        if (!url || !cloudBillingBody) {
            return;
        }

        setCloudBillingLoading(true);
        setStatus(cloudBillingStatusBanner, "info", "Revisando Productos Cloud y facturas del mes actual...");

        try {
            const dashboard = await fetchJson(url);
            renderCloudBillingDashboard(dashboard);
            if (dashboard?.siigoValidationError) {
                setStatus(cloudBillingStatusBanner, "warning", dashboard.siigoValidationError);
            } else {
                setStatus(
                    cloudBillingStatusBanner,
                    dashboard?.hasData ? "" : "info",
                    dashboard?.hasData ? "" : (dashboard?.emptyStateMessage || "No encontramos productos Cloud para revisar."));
            }
        } catch (error) {
            state.cloudBillingDashboard = null;
            if (cloudBillingBody) {
                cloudBillingBody.innerHTML = '<tr><td colspan="5" class="dashboard-table__empty">No pudimos cargar la auditoria del mes actual.</td></tr>';
            }
            setStatus(cloudBillingStatusBanner, "error", error instanceof Error ? error.message : "No fue posible cargar MES ACTUAL.");
        } finally {
            setCloudBillingLoading(false);
        }
    }

    async function loadTaxes() {
        setPeriodLoading(true);
        setStatus(taxesStatusBanner, "info", "Actualizando tablero de impuestos...");

        try {
            const dashboard = await fetchJson(buildTaxesUrl());
            updateTaxesContext(dashboard);
            renderTaxesDashboard(dashboard);
            setStatus(taxesStatusBanner, "", "");
        } catch (error) {
            setStatus(taxesStatusBanner, "error", error instanceof Error ? error.message : "No fue posible cargar el dashboard de impuestos.");
        } finally {
            setPeriodLoading(false);
        }
    }

    async function loadPortfolio() {
        if (state.portfolioLoading) {
            return;
        }

        setPortfolioLoading(true);
        setStatus(portfolioStatusBanner, "info", "Actualizando tablero de cartera...");

        try {
            const dashboard = await fetchJson(buildPortfolioUrl(), { cache: "no-store" });
            updatePortfolioContext(dashboard);
            renderPortfolioKpis(dashboard);
            if (state.portfolioSubtab === "monthly") {
                renderPortfolioMonthlyDashboard();
            } else if (state.portfolioSubtab === "detail") {
                renderPortfolioTable();
            } else {
                syncAccountStatementPdfButton();
            }
            setStatus(portfolioStatusBanner, "", "");
        } catch (error) {
            setStatus(portfolioStatusBanner, "error", error instanceof Error ? error.message : "No fue posible cargar la cartera.");
        } finally {
            setPortfolioLoading(false);
        }
    }

    async function loadBusiness() {
        if (!buildBusinessUrl()) {
            return;
        }

        setBusinessLoading(true);
        setStatus(businessStatusBanner, "info", "Actualizando tablero de negocios...");

        try {
            const dashboard = await fetchJson(buildBusinessUrl());
            updateBusinessContext(dashboard);
            renderBusinessDashboard(dashboard);
            setStatus(
                businessStatusBanner,
                dashboard?.hasData ? "" : "info",
                dashboard?.hasData ? "" : (dashboard?.emptyStateMessage || "No hay negocios cerrados para mostrar."));
        } catch (error) {
            setStatus(businessStatusBanner, "error", error instanceof Error ? error.message : "No fue posible cargar el dashboard de negocios.");
        } finally {
            setBusinessLoading(false);
        }
    }

    async function repairBillingDataverse() {
        const baseUrl = billingDataverseRepairButton?.dataset.repairUrl || "";
        if (!baseUrl) {
            setStatus(billingStatusBanner, "error", "No esta configurada la sincronizacion con Dataverse.");
            return;
        }

        if (state.period !== "month") {
            setStatus(billingStatusBanner, "warning", "Selecciona un mes para sincronizar Siigo con Dataverse.");
            return;
        }

        const url = new URL(baseUrl, window.location.origin);
        url.searchParams.set("year", String(state.year));
        url.searchParams.set("month", String(state.value));
        billingDataverseRepairButton.disabled = true;
        setStatus(billingStatusBanner, "info", "Sincronizando facturas y notas credito aceptadas desde Siigo...");

        try {
            const response = await fetch(url.toString(), {
                method: "POST",
                headers: { "Accept": "application/json" }
            });
            const payload = await response.json().catch(() => ({}));
            if (!response.ok) {
                throw new Error(payload.message || payload.detail || "No fue posible sincronizar Dataverse.");
            }

            const applied = Number(payload.applied || 0);
            const errors = Number(payload.errors || 0);
            const remaining = Number(payload.billingDifferences || 0);
            setStatus(
                billingStatusBanner,
                errors > 0 || remaining > 0 ? "warning" : "success",
                `${payload.message || "Sincronizacion terminada."} ${numberFormatter.format(applied)} cambio(s), ${numberFormatter.format(errors)} error(es), ${numberFormatter.format(remaining)} diferencia(s) pendiente(s).`);
        } catch (error) {
            setStatus(
                billingStatusBanner,
                "error",
                error instanceof Error ? error.message : "No fue posible sincronizar Dataverse.");
        } finally {
            billingDataverseRepairButton.disabled = false;
        }
    }

    async function loadBusinessBilling() {
        if (!buildBusinessBillingUrl()) {
            return;
        }

        setBusinessBillingLoading(true);
        setStatus(businessBillingStatusBanner, "info", "Actualizando facturacion de negocio...");

        try {
            const dashboard = await fetchJson(buildBusinessBillingUrl());
            updateBusinessBillingContext(dashboard);
            renderBusinessBillingDashboard(dashboard);
            setStatus(
                businessBillingStatusBanner,
                dashboard?.hasData ? "" : "info",
                dashboard?.hasData ? "" : (dashboard?.emptyStateMessage || "No hay facturacion para el rango seleccionado."));
        } catch (error) {
            setStatus(businessBillingStatusBanner, "error", error instanceof Error ? error.message : "No fue posible cargar la facturacion de negocio.");
        } finally {
            setBusinessBillingLoading(false);
        }
    }

    async function loadCopiers() {
        setCopiersLoading(true);
        setStatus(copiersStatusBanner, "info", "Actualizando facturacion copiers...");

        try {
            const dashboard = await fetchJson(buildCopiersUrl());
            updateCopiersContext(dashboard);
            renderCopiersTable(dashboard);
            setStatus(copiersStatusBanner, "", "");
        } catch (error) {
            setStatus(copiersStatusBanner, "error", error instanceof Error ? error.message : "No fue posible cargar la facturacion copiers.");
        } finally {
            setCopiersLoading(false);
        }
    }

    async function loadCopiersClientInvoices(row) {
        if (!row) {
            return;
        }

        const requestSequence = ++state.copiersClientInvoicesRequestSequence;
        renderCopiersClientInvoicesLoading(row);

        try {
            const detail = await fetchJson(buildCopiersClientInvoicesUrl(row.clientId || "", row.clientName || ""));
            if (requestSequence !== state.copiersClientInvoicesRequestSequence) {
                return;
            }

            renderCopiersClientInvoicesDetail(detail);
            setStatus(copiersClientInvoicesStatus, "", "");
        } catch (error) {
            if (requestSequence !== state.copiersClientInvoicesRequestSequence) {
                return;
            }

            if (copiersClientInvoicesSubtitle) {
                copiersClientInvoicesSubtitle.textContent = "No fue posible cargar las facturas emitidas del cliente seleccionado.";
            }

            if (copiersClientInvoicesBody) {
                copiersClientInvoicesBody.innerHTML = '<tr><td colspan="6" class="dashboard-table__empty">No pudimos consultar las facturas emitidas de este cliente.</td></tr>';
            }

            setStatus(copiersClientInvoicesStatus, "error", error instanceof Error ? error.message : "No fue posible cargar las facturas emitidas.");
        } finally {
            if (requestSequence === state.copiersClientInvoicesRequestSequence) {
                state.copiersClientInvoicesLoading = false;
            }
        }
    }

    async function loadCopiersLineEquipmentAssignment(row) {
        if (!row) {
            return;
        }

        renderCopiersLineEquipmentLoading(row);

        try {
            const detail = await fetchJson(buildCopiersLineEquipmentAssignmentUrl(row.recordId || "", row.clientId || ""));
            renderCopiersLineEquipmentDetail(detail);
            setStatus(copiersLineEquipmentStatus, "", "");
        } catch (error) {
            if (copiersLineEquipmentSubtitle) {
                copiersLineEquipmentSubtitle.textContent = "No fue posible cargar la asignacion de equipos de esta linea.";
            }

            setStatus(copiersLineEquipmentStatus, "error", error instanceof Error ? error.message : "No fue posible cargar la asignacion.");
        } finally {
            setCopiersLineEquipmentBusy(false);
            state.copiersLineEquipmentLoading = false;
        }
    }

    async function saveCopiersLineEquipmentAssignment() {
        const detail = state.copiersLineEquipmentDetail;
        if (!detail || state.copiersLineEquipmentSaving) {
            return;
        }

        const url = buildCopiersLineEquipmentAssignmentSaveUrl();
        if (!url) {
            setStatus(copiersLineEquipmentStatus, "error", "No hay una URL configurada para guardar la asignacion.");
            return;
        }

        setCopiersLineEquipmentSaving(true);
        setStatus(copiersLineEquipmentStatus, "info", "Guardando asignacion...");

        try {
            const result = await fetchJson(url, {
                method: "POST",
                body: JSON.stringify({
                    lineId: detail.lineId || "",
                    clientId: detail.clientId || "",
                    equipmentIds: Array.from(state.copiersLineEquipmentDraftIds || [])
                })
            });

            renderCopiersLineEquipmentDetail(result?.detail || detail);
            setStatus(copiersLineEquipmentStatus, "success", result?.message || "Asignacion actualizada correctamente.");
            await loadCopiers();
        } catch (error) {
            setStatus(copiersLineEquipmentStatus, "error", error instanceof Error ? error.message : "No fue posible guardar la asignacion.");
        } finally {
            setCopiersLineEquipmentSaving(false);
            renderCopiersLineEquipmentDraft();
        }
    }

    function renderCopiersEquipmentDetailLoading(row) {
        if (!copiersEquipmentDetailModal) {
            return;
        }

        resetCopiersEquipmentDetail();
        document.body.classList.add("dashboard-modal-open");
        copiersEquipmentDetailModal.hidden = false;
        state.copiersEquipmentDetailLoading = true;

        if (copiersEquipmentDetailTitle) {
            copiersEquipmentDetailTitle.textContent = row?.serial
                ? `Equipo ${row.serial}`
                : "Detalle del equipo";
        }

        if (copiersEquipmentDetailSubtitle) {
            copiersEquipmentDetailSubtitle.textContent = "Cargando informacion del equipo y su historial de mantenimientos...";
        }

        if (copiersEquipmentMaintenanceBody) {
            copiersEquipmentMaintenanceBody.innerHTML = '<tr><td colspan="9" class="dashboard-table__empty">Cargando historial del equipo...</td></tr>';
        }

        setStatus(copiersEquipmentDetailStatus, "info", "Consultando detalle del equipo...");
    }

    function buildCopiersEquipmentAssignmentPayload() {
        const moveToStock = Boolean(copiersEquipmentMoveToStockInput?.checked);
        const clientName = (copiersEquipmentClientNameInput?.value || "").trim();
        if (!moveToStock && !clientName) {
            throw new Error("Debes indicar el cliente al que quieres reasignar el equipo o marcarlo como stock.");
        }

        return {
            recordId: copiersEquipmentRecordIdInput?.value || "",
            clientId: moveToStock ? "" : (copiersEquipmentClientIdInput?.value || ""),
            clientName: moveToStock ? "" : clientName,
            moveToStock
        };
    }

    async function loadCopiersEquipment(options = {}) {
        const quiet = Boolean(options.quiet);
        setCopiersEquipmentLoading(true);
        if (!quiet) {
            const loadingMessage = state.copiersSubtab === "maintenance"
                ? "Actualizando mantenimientos copiers..."
                : "Actualizando inventario de equipos copiers...";
            setStatus(copiersStatusBanner, "info", loadingMessage);
        }

        try {
            const dashboard = await fetchJson(buildCopiersEquipmentUrl());
            updateCopiersEquipmentContext(dashboard);
            renderCopiersEquipmentDashboard(dashboard);
            if (!quiet) {
                setStatus(copiersStatusBanner, "", "");
            }
        } catch (error) {
            setStatus(copiersStatusBanner, "error", error instanceof Error ? error.message : "No fue posible cargar los equipos copiers.");
        } finally {
            setCopiersEquipmentLoading(false);
        }
    }

    async function loadCopiersInventory(options = {}) {
        const quiet = Boolean(options.quiet);
        setCopiersInventoryLoading(true);
        if (!quiet) {
            setStatus(copiersStatusBanner, "info", "Actualizando inventario comercial copiers...");
        }

        try {
            const dashboard = await fetchJson(buildCopiersInventoryUrl());
            updateCopiersInventoryContext(dashboard);
            renderCopiersInventoryDashboard(dashboard);
            if (!quiet) {
                setStatus(copiersStatusBanner, "", "");
            }
        } catch (error) {
            setStatus(copiersStatusBanner, "error", error instanceof Error ? error.message : "No fue posible cargar el inventario comercial.");
        } finally {
            setCopiersInventoryLoading(false);
        }
    }

    async function loadCopiersMovements(options = {}) {
        const quiet = Boolean(options.quiet);
        setCopiersMovementsLoading(true);
        if (!quiet) {
            setStatus(copiersStatusBanner, "info", "Actualizando movimientos de equipos copiers...");
        }

        try {
            const dashboard = await fetchJson(buildCopiersEquipmentMovementsUrl());
            updateCopiersMovementsContext(dashboard);
            renderCopiersMovementsDashboard(dashboard);
            if (!quiet) {
                setStatus(copiersStatusBanner, "", "");
            }
        } catch (error) {
            setStatus(copiersStatusBanner, "error", error instanceof Error ? error.message : "No fue posible cargar los movimientos de equipos.");
        } finally {
            setCopiersMovementsLoading(false);
        }
    }

    async function exportCopiersInventory() {
        const url = buildCopiersInventoryExportUrl();
        if (!url) {
            setStatus(copiersStatusBanner, "error", "No hay una URL configurada para exportar el inventario.");
            return;
        }

        setCopiersInventoryExporting(true);
        setStatus(copiersStatusBanner, "info", "Preparando Excel de inventario...");

        try {
            const response = await fetch(url, {
                method: "GET",
                headers: {
                    Accept: "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
                }
            });

            if (!response.ok) {
                const message = await response.text();
                throw new Error(message || "No fue posible exportar el inventario.");
            }

            const blob = await response.blob();
            const fileName = resolveDownloadFileName(response.headers.get("content-disposition"))
                || "inventario-comercial-copiers.xlsx";
            const objectUrl = URL.createObjectURL(blob);
            const link = document.createElement("a");
            link.href = objectUrl;
            link.download = fileName;
            document.body.appendChild(link);
            link.click();
            link.remove();
            window.setTimeout(() => URL.revokeObjectURL(objectUrl), 1000);
            setStatus(copiersStatusBanner, "success", "Excel generado correctamente.");
        } catch (error) {
            setStatus(copiersStatusBanner, "error", error instanceof Error ? error.message : "No fue posible exportar el inventario.");
        } finally {
            setCopiersInventoryExporting(false);
        }
    }

    async function loadCopiersCounters(options = {}) {
        const quiet = Boolean(options.quiet);
        syncCopiersCountersFiltersFromControls();
        setCopiersCountersLoading(true);
        if (!quiet) {
            setStatus(copiersStatusBanner, "info", "Aplicando filtros y consultando contadores copiers...");
        }

        try {
            const dashboard = await fetchJson(buildCopiersCountersUrl());
            updateCopiersCountersContext(dashboard);
            renderCopiersCountersDashboard(dashboard);
            if (!quiet) {
                setStatus(copiersStatusBanner, "", "");
            }
        } catch (error) {
            setStatus(copiersStatusBanner, "error", error instanceof Error ? error.message : "No fue posible cargar el consumo de contadores.");
        } finally {
            setCopiersCountersLoading(false);
        }
    }

    async function loadCopiersEquipmentDetail(recordId) {
        const row = getCopiersEquipmentRowById(recordId);
        renderCopiersEquipmentDetailLoading(row);

        try {
            const detail = await fetchJson(buildCopiersEquipmentDetailUrl(recordId));
            fillCopiersEquipmentDetail(detail);
            setStatus(copiersEquipmentDetailStatus, "", "");
        } catch (error) {
            setStatus(copiersEquipmentDetailStatus, "error", error instanceof Error ? error.message : "No fue posible cargar el detalle del equipo.");
        } finally {
            state.copiersEquipmentDetailLoading = false;
        }
    }

    async function saveCopiersEquipmentAssignment() {
        if (state.copiersEquipmentAssignmentSaving) {
            return;
        }

        try {
            const payload = buildCopiersEquipmentAssignmentPayload();
            setCopiersEquipmentAssignmentSaving(true);
            setStatus(copiersEquipmentDetailStatus, "info", "Guardando asignacion del equipo...");

            const result = await fetchJson(buildCopiersEquipmentAssignmentUrl(), {
                method: "POST",
                body: JSON.stringify(payload)
            });
            const dashboard = await fetchJson(buildCopiersEquipmentUrl());
            updateCopiersEquipmentContext(dashboard);
            renderCopiersEquipmentDashboard(dashboard);
            const detail = await fetchJson(buildCopiersEquipmentDetailUrl(result?.recordId || payload.recordId));
            fillCopiersEquipmentDetail(detail);
            setStatus(copiersEquipmentDetailStatus, "success", result?.message || "Equipo actualizado correctamente.");
            setStatus(copiersStatusBanner, "success", result?.message || "Equipo actualizado correctamente.");
        } catch (error) {
            setStatus(copiersEquipmentDetailStatus, "error", error instanceof Error ? error.message : "No fue posible guardar la reasignacion del equipo.");
        } finally {
            setCopiersEquipmentAssignmentSaving(false);
        }
    }

    async function loadPnl() {
        setPnlLoading(true);
        setStatus(pnlStatusBanner, "info", "Actualizando tablero P&L...");

        try {
            const dashboard = await fetchJson(buildPnlUrl());
            updatePnlContext(dashboard);
            renderPnlKpis(dashboard);
            renderPnlTable(dashboard);
            renderPnlOrphanTable(dashboard);
            setStatus(pnlStatusBanner, dashboard?.sourceWarning ? "info" : "", dashboard?.sourceWarning || "");
        } catch (error) {
            setStatus(pnlStatusBanner, "error", error instanceof Error ? error.message : "No fue posible cargar el dashboard P&L.");
        } finally {
            setPnlLoading(false);
        }
    }

    async function loadLicenciamiento() {
        if (!app.dataset.licenciamientoUrl) {
            return;
        }

        setLicenciamientoLoading(true);
        setStatus(licenciamientoStatusBanner, "info", "Actualizando tablero de licenciamiento...");

        try {
            const dashboard = await fetchJson(buildLicenciamientoUrl());
            updateLicenciamientoContext(dashboard);
            renderLicenciamientoDashboard(dashboard);
            setStatus(
                licenciamientoStatusBanner,
                dashboard?.hasData ? "" : "info",
                dashboard?.hasData ? "" : (dashboard?.emptyStateMessage || "No hay datos de licenciamiento para el periodo seleccionado."));
        } catch (error) {
            setStatus(licenciamientoStatusBanner, "error", error instanceof Error ? error.message : "No fue posible cargar el dashboard de licenciamiento.");
        } finally {
            setLicenciamientoLoading(false);
        }
    }

    async function loadUtility() {
        if (!app.dataset.utilityUrl) {
            return;
        }

        setUtilityLoading(true);
        setStatus(utilityStatusBanner, "info", "Actualizando tablero de utilidad...");

        try {
            const dashboard = await fetchJson(buildUtilityUrl());
            updateUtilityContext(dashboard);
            renderUtilityDashboard(dashboard);
            setStatus(
                utilityStatusBanner,
                dashboard?.hasData ? "" : "info",
                dashboard?.hasData ? "" : (dashboard?.emptyStateMessage || "No hay datos de utilidad desde enero de 2025."));
        } catch (error) {
            setStatus(utilityStatusBanner, "error", error instanceof Error ? error.message : "No fue posible cargar el dashboard de utilidad.");
        } finally {
            setUtilityLoading(false);
        }
    }

    async function loadYtd() {
        if (!app.dataset.ytdUrl) {
            return;
        }

        setYtdLoading(true);
        setStatus(ytdStatusBanner, "info", "Actualizando graficos YTD...");

        try {
            const dashboard = await fetchJson(buildYtdUrl());
            updateYtdContext(dashboard);
            renderYtdDashboard(dashboard);
            setStatus(
                ytdStatusBanner,
                dashboard?.sourceWarning ? "info" : (dashboard?.hasData ? "" : "info"),
                dashboard?.sourceWarning || (dashboard?.hasData ? "" : (dashboard?.emptyStateMessage || "No hay datos YTD para mostrar.")));
        } catch (error) {
            setStatus(ytdStatusBanner, "error", error instanceof Error ? error.message : "No fue posible cargar el dashboard YTD.");
        } finally {
            setYtdLoading(false);
        }
    }

    async function assignUtilityRow(button) {
        const url = buildUtilityAssignmentUrl();
        const statusTarget = button.closest("#utilityOrphansModal")
            ? utilityOrphansStatus
            : utilityStatusBanner;
        const wasOrphansOpen = isUtilityOrphansOpen();
        if (!url) {
            setStatus(statusTarget, "error", "No hay una URL configurada para asignar filas.");
            return;
        }

        const row = button.closest("tr");
        const targetSelect = row?.querySelector("[data-utility-target]");
        const payload = {
            sourceType: button.dataset.sourceType || "",
            recordId: button.dataset.recordId || "",
            targetBucket: targetSelect?.value || "monthly"
        };

        state.utilityAssigningRecordId = payload.recordId;
        setUtilityLoading(true);
        renderUtilityUnresolvedRows(state.utilityDashboard);
        if (wasOrphansOpen) {
            renderUtilityOrphansModal();
        }
        setStatus(statusTarget, "info", "Guardando asignacion...");

        try {
            const result = await fetchJson(url, {
                method: "POST",
                body: JSON.stringify(payload)
            });
            setStatus(statusTarget, "success", result?.message || "Fila asignada correctamente.");
            await loadUtility();
            if (wasOrphansOpen) {
                renderUtilityOrphansModal();
            }
        } catch (error) {
            setStatus(statusTarget, "error", error instanceof Error ? error.message : "No fue posible asignar la fila.");
        } finally {
            state.utilityAssigningRecordId = "";
            setUtilityLoading(false);
            renderUtilityUnresolvedRows(state.utilityDashboard);
            if (wasOrphansOpen) {
                renderUtilityOrphansModal();
            }
        }
    }

    dashboardAgentForm?.addEventListener("submit", event => {
        event.preventDefault();
        submitAgentQuestion(dashboardAgentInput?.value || "");
    });

    dashboardAgentInput?.addEventListener("keydown", event => {
        if (event.key === "Enter" && !event.shiftKey) {
            event.preventDefault();
            dashboardAgentForm?.requestSubmit();
        }
    });

    dashboardAgentPromptButtons.forEach(button => {
        button.addEventListener("click", () => {
            const prompt = button.dataset.agentPrompt || "";
            if (dashboardAgentInput) {
                dashboardAgentInput.value = prompt;
            }

            submitAgentQuestion(prompt);
        });
    });

    dashboardAgentMessages?.addEventListener("click", event => {
        const button = event.target.closest("[data-agent-feedback-type]");
        if (!button) {
            return;
        }

        const message = button.closest("[data-agent-feedback-id]");
        openAgentFeedbackModal(message?.dataset.agentFeedbackId || "", button.dataset.agentFeedbackType || "learning");
    });

    dashboardAgentFeedbackForm?.addEventListener("submit", submitAgentFeedback);
    dashboardAgentFeedbackCloseButtons.forEach(button => {
        button.addEventListener("click", closeAgentFeedbackModal);
    });

    dashboardAgentLearningPanel?.addEventListener("toggle", () => {
        if (dashboardAgentLearningPanel.open) {
            loadAgentLearning();
        }
    });

    dashboardAgentLearningRefreshButton?.addEventListener("click", () => {
        loadAgentLearning(true);
    });

    dashboardAgentLearningList?.addEventListener("click", event => {
        const button = event.target.closest("[data-agent-learning-save]");
        if (button) {
            saveAgentLearningStatus(button);
        }
    });

    yearFilter?.addEventListener("change", () => {
        state.year = Number(yearFilter.value || currentYear);
        state.value = getDefaultValue(state.period, state.year);
        buildValueOptions();
        syncSiigoDateRangeWithActivePeriod();
        resetSiigoInvoicesTable("Consulta Siigo para el nuevo periodo activo.");
        loadActivePeriodTab();
    });

    periodFilter?.addEventListener("change", () => {
        state.period = periodFilter.value || "month";
        state.value = getDefaultValue(state.period, state.year);
        buildValueOptions();
        syncSiigoDateRangeWithActivePeriod();
        resetSiigoInvoicesTable("Consulta Siigo para el nuevo periodo activo.");
        loadActivePeriodTab();
    });

    valueFilter?.addEventListener("change", () => {
        state.value = Number(valueFilter.value || 1);
        syncSiigoDateRangeWithActivePeriod();
        resetSiigoInvoicesTable("Consulta Siigo para el nuevo periodo activo.");
        loadActivePeriodTab();
    });

    refreshButton?.addEventListener("click", loadActivePeriodTab);
    billingDataverseRepairButton?.addEventListener("click", repairBillingDataverse);
    billingReportToggleButton?.addEventListener("click", () => {
        toggleBillingSection(billingReportSection, billingReportToggleButton, "Ocultar reportes", "Mostrar reportes");
    });
    siigoToggleButton?.addEventListener("click", () => {
        toggleBillingSection(siigoApiSection, siigoToggleButton, "Ocultar Siigo API", "Mostrar Siigo API");
    });
    billingInvoicesRefreshButton?.addEventListener("click", () => {
        state.billingInvoicesGrid.duplicatesOnly = false;
        loadBillingInvoices({ duplicatesOnly: false, page: 1 });
    });
    billingInvoicesDuplicatesButton?.addEventListener("click", () => {
        findDuplicateBillingInvoices().catch(() => {});
    });
    billingInvoicesClearFiltersButton?.addEventListener("click", () => resetPortfolioGrid("billingInvoices"));
    billingInvoicesContractButton?.addEventListener("click", openBillingContractTypeModal);
    billingInvoicesDeleteButton?.addEventListener("click", () => {
        deleteSelectedBillingInvoices().catch(() => {});
    });
    billingInvoicesMonth?.addEventListener("change", () => {
        const match = /^(\d{4})-(\d{2})$/.exec(billingInvoicesMonth.value || "");
        if (!match) {
            return;
        }
        state.billingInvoicesYear = Number(match[1]);
        state.billingInvoicesMonth = Number(match[2]);
        state.billingInvoicesGrid.duplicatesOnly = false;
        state.billingInvoiceSelectedIds.clear();
        loadBillingInvoices({ duplicatesOnly: false, page: 1 });
    });
    billingInvoicesPageSize?.addEventListener("change", () => {
        loadBillingInvoices({ page: 1, pageSize: Number(billingInvoicesPageSize.value || 50) });
    });
    billingInvoicesPreviousPageButton?.addEventListener("click", () => {
        loadBillingInvoices({ page: Math.max(1, state.billingInvoicesPage - 1) });
    });
    billingInvoicesNextPageButton?.addEventListener("click", () => {
        loadBillingInvoices({ page: Math.min(state.billingInvoicesTotalPages, state.billingInvoicesPage + 1) });
    });
    let billingInvoicesSearchTimer = 0;
    billingInvoicesSearch?.addEventListener("input", () => {
        window.clearTimeout(billingInvoicesSearchTimer);
        billingInvoicesSearchTimer = window.setTimeout(() => {
            state.billingInvoicesSearchTerm = billingInvoicesSearch.value || "";
            renderBillingInvoicesTable();
        }, 180);
    });
    billingCreditNotesRefreshButton?.addEventListener("click", loadBillingCreditNotes);
    billingCreditNotesSearch?.addEventListener("input", () => {
        state.billingCreditNotesSearchTerm = billingCreditNotesSearch.value || "";
        renderBillingCreditNotesTable();
    });
    billingCreditNotesStatusFilter?.addEventListener("change", () => {
        state.billingCreditNotesStatusFilter = billingCreditNotesStatusFilter.value || "all";
        renderBillingCreditNotesTable();
    });
    cloudBillingRefreshButton?.addEventListener("click", loadCloudBillingCurrentMonth);
    cloudBillingSearch?.addEventListener("input", () => {
        state.cloudBillingSearchTerm = cloudBillingSearch.value || "";
        renderCloudBillingTable();
    });
    cloudBillingStatusFilter?.addEventListener("change", () => {
        state.cloudBillingStatusFilter = cloudBillingStatusFilter.value || "all";
        renderCloudBillingTable();
    });
    cloudBillingBody?.addEventListener("click", event => {
        const row = event.target.closest("[data-cloud-billing-group-index]");
        if (!row) {
            return;
        }

        const index = Number(row.dataset.cloudBillingGroupIndex || -1);
        openCloudBillingDetailModal(state.cloudBillingGroups[index]);
    });
    cloudBillingBody?.addEventListener("keydown", event => {
        if (event.key !== "Enter" && event.key !== " ") {
            return;
        }

        const row = event.target.closest("[data-cloud-billing-group-index]");
        if (!row) {
            return;
        }

        event.preventDefault();
        const index = Number(row.dataset.cloudBillingGroupIndex || -1);
        openCloudBillingDetailModal(state.cloudBillingGroups[index]);
    });
    billingInvoicesHead?.addEventListener("change", event => {
        const checkbox = event.target.closest("[data-billing-invoices-select-all]");
        if (!checkbox) {
            return;
        }

        const checked = Boolean(checkbox.checked);
        getFilteredBillingInvoiceRows().forEach(row => {
            const recordId = row?.recordId || "";
            if (!recordId) {
                return;
            }

            if (checked) {
                state.billingInvoiceSelectedIds.add(recordId);
            } else {
                state.billingInvoiceSelectedIds.delete(recordId);
            }
        });

        renderBillingInvoicesTable();
    });
    billingInvoicesBody?.addEventListener("change", event => {
        const checkbox = event.target.closest("[data-billing-invoice-select]");
        if (!checkbox) {
            return;
        }

        const recordId = checkbox.dataset.recordId || "";
        if (!recordId) {
            return;
        }

        if (checkbox.checked) {
            state.billingInvoiceSelectedIds.add(recordId);
        } else {
            state.billingInvoiceSelectedIds.delete(recordId);
        }

        syncBillingInvoicesSelectionSummary();
    });
    billingInvoicesBody?.addEventListener("click", event => {
        if (event.target.closest("[data-billing-invoice-select], [data-billing-invoice-ignore-click], a, button, input, select, textarea")) {
            return;
        }

        const row = event.target.closest("[data-billing-invoice-id]");
        const invoice = getBillingInvoiceById(row?.dataset.billingInvoiceId || "");
        if (invoice) {
            openBillingInvoiceEditorModal(invoice);
        }
    });
    billingInvoiceEditorForm?.addEventListener("submit", event => {
        event.preventDefault();
        saveBillingInvoiceEditor();
    });
    billingInvoiceEditorCloseButton?.addEventListener("click", closeBillingInvoiceEditorModal);
    billingInvoiceEditorCancelButton?.addEventListener("click", closeBillingInvoiceEditorModal);
    billingInvoiceEditorModal?.querySelectorAll("[data-billing-invoice-editor-close]").forEach(element => {
        element.addEventListener("click", closeBillingInvoiceEditorModal);
    });
    billingContractTypeForm?.addEventListener("submit", event => {
        event.preventDefault();
        saveBillingContractTypeChange();
    });
    billingContractTypeCloseButton?.addEventListener("click", closeBillingContractTypeModal);
    billingContractTypeCancelButton?.addEventListener("click", closeBillingContractTypeModal);
    billingContractTypeModal?.querySelectorAll("[data-billing-contract-close]").forEach(element => {
        element.addEventListener("click", closeBillingContractTypeModal);
    });
    billingReportLoadButton?.addEventListener("click", loadBillingReportInvoices);
    billingReportExportButton?.addEventListener("click", exportBillingReport);
    billingReportClientSearch?.addEventListener("keydown", event => {
        if (event.key === "Enter") {
            event.preventDefault();
            loadBillingReportInvoices();
        }
    });
    billingReportClientSearch?.addEventListener("input", () => {
        state.billingReportDetail = null;
        resetBillingReportPreview();
        if (billingReportResultsCount) {
            billingReportResultsCount.textContent = "0";
        }
        if (billingReportBody) {
            billingReportBody.innerHTML = '<tr><td colspan="8" class="dashboard-table__empty">Busca un cliente para ver sus facturas.</td></tr>';
        }
        resetBillingReportReference();
        syncBillingReportSelectionSummary();
    });
    billingReportBody?.addEventListener("change", event => {
        const checkbox = event.target.closest("[data-billing-report-select]");
        if (!checkbox) {
            return;
        }

        const row = checkbox.closest("tr");
        const amountInput = row?.querySelector("[data-billing-report-amount]");
        if (amountInput) {
            amountInput.disabled = !checkbox.checked;
            if (checkbox.checked && !amountInput.value) {
                amountInput.value = amountInput.dataset.total || "0.00";
            }
        }

        syncBillingReportSelectionSummary();
    });
    billingReportBody?.addEventListener("input", event => {
        if (event.target.closest("[data-billing-report-amount]")) {
            syncBillingReportSelectionSummary();
        }
    });
    billingReportBody?.addEventListener("click", event => {
        const previewButton = event.target.closest("[data-billing-report-preview]");
        if (!previewButton) {
            return;
        }

        const invoice = getBillingReportInvoiceById(previewButton.dataset.billingReportPreview || "");
        openBillingReportPreview(invoice);
    });
    siigoUseActivePeriodButton?.addEventListener("click", () => {
        syncSiigoDateRangeWithActivePeriod();
        resetSiigoInvoicesTable("Consulta Siigo para el periodo activo.");
        setStatus(siigoInvoicesStatus, "", "");
    });
    siigoInvoicesLoadButton?.addEventListener("click", loadSiigoInvoices);
    siigoCustomersLoadButton?.addEventListener("click", loadSiigoCustomers);
    siigoCustomerNitSearchButton?.addEventListener("click", searchSiigoCustomerByNit);
    siigoCustomerNitSearch?.addEventListener("keydown", event => {
        if (event.key === "Enter") {
            event.preventDefault();
            searchSiigoCustomerByNit();
        }
    });
    siigoInvoicesDownloadButton?.addEventListener("click", downloadSiigoInvoices);
    siigoCustomerSelect?.addEventListener("change", () => {
        syncSiigoCustomerSelection();
        resetSiigoInvoicesTable("Consulta Siigo para el cliente seleccionado.");
        setStatus(siigoInvoicesStatus, "", "");
    });
    [siigoStartDateInput, siigoEndDateInput].forEach(input => {
        input?.addEventListener("change", () => {
            if (siigoPeriodReference) {
                const startDate = siigoStartDateInput?.value || "";
                const endDate = siigoEndDateInput?.value || "";
                siigoPeriodReference.textContent = startDate && endDate ? `${startDate} - ${endDate}` : "-";
            }
            resetSiigoInvoicesTable("Consulta Siigo para el nuevo rango de fechas.");
            setStatus(siigoInvoicesStatus, "", "");
        });
    });
    siigoInvoicesSelectAll?.addEventListener("change", () => {
        const checked = Boolean(siigoInvoicesSelectAll.checked);
        siigoInvoicesBody?.querySelectorAll("[data-siigo-invoice-select]").forEach(checkbox => {
            checkbox.checked = checked;
        });
        syncSiigoInvoicesSelectionSummary();
    });
    siigoInvoicesBody?.addEventListener("change", event => {
        if (event.target.closest("[data-siigo-invoice-select]")) {
            syncSiigoInvoicesSelectionSummary();
        }
    });
    [taxesRecurringCards, taxesOtherCards].forEach(container => {
        container?.addEventListener("click", event => {
            const card = event.target.closest("[data-taxes-card]");
            if (!card) {
                return;
            }

            const group = card.dataset.taxesCardGroup || "recurring";
            if (group === "other") {
                state.taxesActiveOtherKey = card.dataset.taxesCard || "income-tax";
            } else {
                state.taxesActiveRecurringKey = card.dataset.taxesCard || "retefuente";
            }

            renderTaxesDashboard(state.taxesDashboard);
        });
    });
    [taxesRecurringDetail, taxesOtherDetail].forEach(container => {
        container?.addEventListener("click", event => {
            const reteFuenteExportButton = event.target.closest("[data-taxes-retefuente-export]");
            if (reteFuenteExportButton) {
                if (!taxesReteFuenteExportUrl) {
                    setStatus(taxesStatusBanner, "error", "No hay una URL configurada para generar el reporte de retefuente.");
                    return;
                }

                window.location.href = buildTaxesReteFuenteExportUrl();
                return;
            }

            const reteIcaExportButton = event.target.closest("[data-taxes-reteica-export]");
            if (reteIcaExportButton) {
                if (!taxesReteIcaExportUrl) {
                    setStatus(taxesStatusBanner, "error", "No hay una URL configurada para generar el reporte de Rete ICA.");
                    return;
                }

                window.location.href = buildTaxesReteIcaExportUrl();
                return;
            }

            const exportButton = event.target.closest("[data-taxes-vat-export]");
            if (!exportButton) {
                return;
            }

            if (!taxesVatExportUrl) {
                setStatus(taxesStatusBanner, "error", "No hay una URL configurada para generar el reporte de IVA.");
                return;
            }

            window.location.href = buildTaxesVatExportUrl();
        });

        container?.addEventListener("change", event => {
            const reportTableSelect = event.target.closest("[data-taxes-report-table]");
            if (reportTableSelect) {
                const reportKind = reportTableSelect.dataset.taxesReportTable || "";
                if (reportKind === "retefuente") {
                    state.taxesReteFuenteTableKey = reportTableSelect.value || "autofuente";
                }

                renderTaxesDashboard(state.taxesDashboard);
                return;
            }

            const vatTableSelect = event.target.closest("[data-taxes-vat-table]");
            if (vatTableSelect) {
                state.taxesVatTableKey = vatTableSelect.value || "generated";
                state.taxesVatVerticalKey = "all";
                renderTaxesDashboard(state.taxesDashboard);
                return;
            }

            const vatVerticalSelect = event.target.closest("[data-taxes-vat-vertical]");
            if (vatVerticalSelect) {
                state.taxesVatVerticalKey = vatVerticalSelect.value || "all";
                renderTaxesDashboard(state.taxesDashboard);
                return;
            }

            const select = event.target.closest("[data-taxes-filter]");
            if (select) {
                handleTaxesFilterChange(select);
            }
        });
    });
    portfolioSubtabButtons.forEach(button => {
        button.addEventListener("click", () => setPortfolioSubtab(button.dataset.portfolioSubtab || "monthly"));
    });
    portfolioMonthlyRangeFilter?.addEventListener("change", () => {
        state.portfolioMonthlyRange = portfolioMonthlyRangeFilter.value || "year";
        renderPortfolioMonthlyDashboard();
    });
    portfolioMonthlyYearFilter?.addEventListener("change", () => {
        state.portfolioMonthlyYear = Number(portfolioMonthlyYearFilter.value || currentYear);
        renderPortfolioMonthlyDashboard();
    });
    portfolioMonthlyMonthFilter?.addEventListener("change", () => {
        state.portfolioMonthlyMonth = normalizePortfolioMonthKey(portfolioMonthlyMonthFilter.value) || state.portfolioMonthlyMonth;
        renderPortfolioMonthlyDashboard();
    });
    portfolioMonthlyStartFilter?.addEventListener("change", () => {
        state.portfolioMonthlyStart = normalizePortfolioMonthKey(portfolioMonthlyStartFilter.value) || state.portfolioMonthlyStart;
        renderPortfolioMonthlyDashboard();
    });
    portfolioMonthlyEndFilter?.addEventListener("change", () => {
        state.portfolioMonthlyEnd = normalizePortfolioMonthKey(portfolioMonthlyEndFilter.value) || state.portfolioMonthlyEnd;
        renderPortfolioMonthlyDashboard();
    });
    portfolioMonthlyChartConfigs.forEach(config => {
        config.chart?.addEventListener("click", event => {
            const bar = event.target.closest("[data-portfolio-month-key]");
            if (!bar || !bar.classList.contains("is-clickable")) {
                return;
            }

            openPortfolioMonthlyDetailModal(
                bar.dataset.portfolioMonthKey || "",
                bar.dataset.portfolioVerticalKey || config.key);
        });
        config.chart?.addEventListener("keydown", event => {
            if (event.key !== "Enter" && event.key !== " ") {
                return;
            }

            const bar = event.target.closest("[data-portfolio-month-key]");
            if (!bar || !bar.classList.contains("is-clickable")) {
                return;
            }

            event.preventDefault();
            openPortfolioMonthlyDetailModal(
                bar.dataset.portfolioMonthKey || "",
                bar.dataset.portfolioVerticalKey || config.key);
        });
    });
    portfolioMonthlyDetailCloseButton?.addEventListener("click", closePortfolioMonthlyDetailModal);
    portfolioMonthlyDetailModal?.querySelectorAll("[data-portfolio-monthly-detail-close]").forEach(element => {
        element.addEventListener("click", closePortfolioMonthlyDetailModal);
    });
    portfolioRefreshButton?.addEventListener("click", loadPortfolio);
    businessRefreshButton?.addEventListener("click", loadBusiness);
    businessBillingRefreshButton?.addEventListener("click", () => {
        updateBusinessBillingFiltersFromControls();
        loadBusinessBilling();
    });
    [businessBillingStartFilter, businessBillingEndFilter, businessBillingGranularityFilter].forEach(element => {
        element?.addEventListener("change", () => {
            updateBusinessBillingFiltersFromControls();
            if (state.activeTab === "business-billing") {
                loadBusinessBilling();
            }
        });
    });
    copiersRefreshButton?.addEventListener("click", loadCopiers);
    copiersEquipmentRefreshButton?.addEventListener("click", () => {
        loadCopiersEquipment();
    });
    copiersInventoryExportButton?.addEventListener("click", exportCopiersInventory);
    copiersMovementsRefreshButton?.addEventListener("click", () => {
        loadCopiersMovements();
    });
    copiersMaintenanceRefreshButton?.addEventListener("click", () => {
        loadCopiersEquipment();
    });
    copiersCountersRefreshButton?.addEventListener("click", () => {
        loadCopiersCounters();
    });
    copiersCountersPdfButton?.addEventListener("click", () => {
        syncCopiersCountersFiltersFromControls();
        if (!state.copiersCountersHasAppliedFilters || state.copiersCountersSignature !== getCopiersCountersSignature()) {
            setStatus(copiersStatusBanner, "warning", "Aplica los filtros antes de exportar el PDF.");
            updateCopiersCountersPdfButton();
            return;
        }

        const url = buildCopiersCountersPdfUrl();
        if (!url) {
            setStatus(copiersStatusBanner, "error", "No hay una URL configurada para exportar el PDF de contadores.");
            return;
        }

        window.location.href = url;
    });
    copiersCountersClearButton?.addEventListener("click", () => {
        state.copiersCountersYear = currentYear;
        state.copiersCountersMonth = currentValue;
        state.copiersCountersClientId = "";
        state.copiersCountersClientName = "";
        buildCopiersCountersPeriodOptions();
        markCopiersCountersFiltersPending("Filtros restablecidos. Aplica el filtro cuando quieras consultar Dataverse.");
        setStatus(copiersStatusBanner, "", "");
    });
    copiersNewRecordButton?.addEventListener("click", () => {
        openCopiersEditorModal("create");
    });
    pnlRefreshButton?.addEventListener("click", loadPnl);
    licenciamientoRefreshButton?.addEventListener("click", loadLicenciamiento);
    utilityRefreshButton?.addEventListener("click", loadUtility);
    utilityBreakdownSaveBtn?.addEventListener("click", saveUtilityTheoreticalBreakdown);
    ytdRefreshButton?.addEventListener("click", loadYtd);
    ytdRevenueBreakdown?.addEventListener("change", event => {
        const input = event.target.closest('input[name="ytdRevenueBreakdown"]');
        if (!input) {
            return;
        }

        state.ytdRevenueBreakdown = input.value || "global";
        renderYtdChart(ytdTotalChart, state.ytdDashboard?.chart || state.ytdDashboard?.charts?.find(chart => chart?.key === "total"));
    });
    ytdExpenseBreakdown?.addEventListener("change", event => {
        const input = event.target.closest('input[name="ytdExpenseBreakdown"]');
        if (!input) {
            return;
        }

        state.ytdExpenseBreakdown = input.value || "global";
        renderYtdChart(ytdTotalChart, state.ytdDashboard?.chart || state.ytdDashboard?.charts?.find(chart => chart?.key === "total"));
    });
    [
        ytdRevenueCategoryFilters,
        ytdRevenueClientFilters,
        ytdRevenueVerticalFilters,
        ytdRevenueContractTypeFilters,
        ytdExpenseCategoryFilters,
        ytdExpenseClientFilters,
        ytdExpenseVerticalFilters,
        ytdExpenseContractTypeFilters
    ].forEach(container => {
        container?.addEventListener("click", event => {
            const toggle = event.target.closest("[data-ytd-dropdown-toggle]");
            if (toggle) {
                const dropdown = toggle.closest("[data-ytd-dropdown]");
                const shouldOpen = !dropdown?.classList.contains("is-open");
                closeYtdDropdowns(dropdown);
                setYtdDropdownOpen(dropdown, shouldOpen);
                if (shouldOpen) {
                    dropdown?.querySelector("[data-ytd-filter-search]")?.focus();
                }
                return;
            }

            const action = event.target.closest("[data-ytd-filter-action]");
            if (action) {
                const dropdown = action.closest("[data-ytd-dropdown]");
                setYtdFilterSelection(
                    dropdown?.dataset.ytdFilterKey || "",
                    action.dataset.ytdFilterAction || "all");
            }
        });
        container?.addEventListener("input", event => {
            const input = event.target.closest("[data-ytd-filter-search]");
            if (input) {
                applyYtdDropdownSearch(input);
            }
        });
        container?.addEventListener("change", event => {
            const input = event.target.closest("[data-ytd-filter]");
            if (!input) {
                return;
            }

            updateYtdFilterSelection(
                input.dataset.ytdFilter || "",
                input.dataset.ytdKey || "",
                Boolean(input.checked));
            syncYtdDropdownSummary(input.closest("[data-ytd-dropdown]"));
        });
    });
    document.addEventListener("click", event => {
        if (!event.target.closest("[data-ytd-dropdown]")) {
            closeYtdDropdowns();
        }
    });
    ytdDetailCloseBtn?.addEventListener("click", closeYtdDetailModal);
    ytdDetailModal?.querySelectorAll("[data-ytd-detail-close]").forEach(element => {
        element.addEventListener("click", closeYtdDetailModal);
    });
    ytdDetailBody?.addEventListener("click", event => {
        const button = event.target.closest("[data-ytd-detail-save]");
        if (button) {
            saveYtdDetailRecord(button);
        }
    });
    ytdDetailBody?.addEventListener("change", event => {
        if (event.target.closest("[data-ytd-row-select], [data-ytd-edit-field]")) {
            updateYtdDetailToolbarState();
        }
    });
    ytdDetailBody?.addEventListener("input", event => {
        if (event.target.closest("[data-ytd-edit-field]")) {
            updateYtdDetailToolbarState();
        }
    });
    ytdDetailSelectAll?.addEventListener("change", () => {
        const checked = Boolean(ytdDetailSelectAll.checked);
        getYtdDetailRows().forEach(row => {
            const checkbox = row.querySelector("[data-ytd-row-select]");
            if (checkbox && !checkbox.disabled) {
                checkbox.checked = checked;
            }
        });
        updateYtdDetailToolbarState();
    });
    ytdBulkApplyButton?.addEventListener("click", applyYtdBulkChanges);
    ytdBulkSaveButton?.addEventListener("click", () => {
        saveYtdRows(getYtdDetailRows(), { closeAfterSave: true });
    });
    utilitySummaryCards?.addEventListener("click", event => {
        const orphansButton = event.target.closest("[data-utility-orphans]");
        if (orphansButton) {
            openUtilityOrphansModal();
            return;
        }

        const button = event.target.closest("[data-utility-breakdown]");
        if (!button) {
            return;
        }

        openUtilityBreakdownModal(button.dataset.utilityBreakdown || "monthly");
    });
    [utilityMonthlyChart, utilityPrepaidChart].forEach(chart => {
        chart?.addEventListener("click", event => {
            handleUtilityChartActivation(event.target);
        });

        chart?.addEventListener("keydown", event => {
            if (event.key !== "Enter" && event.key !== " ") {
                return;
            }

            if (handleUtilityChartActivation(event.target)) {
                event.preventDefault();
            }
        });
    });
    utilityBreakdownBody?.addEventListener("change", event => {
        const input = event.target.closest("[data-utility-row-count], [data-utility-client-count]");
        if (!input) {
            return;
        }

        handleUtilityBreakdownCountChange(input);
    });
    utilityUnresolvedBody?.addEventListener("click", event => {
        const button = event.target.closest("[data-utility-assign]");
        if (!button) {
            return;
        }

        assignUtilityRow(button);
    });
    utilityOrphansBody?.addEventListener("click", event => {
        const button = event.target.closest("[data-utility-assign]");
        if (!button) {
            return;
        }

        assignUtilityRow(button);
    });
    portfolioClientSearch?.addEventListener("input", () => {
        state.portfolioSearchTerm = portfolioClientSearch.value || "";
        renderPortfolioTable();
    });
    portfolioInvoicesSearch?.addEventListener("input", () => {
        state.portfolioInvoicesSearchTerm = portfolioInvoicesSearch.value || "";
        renderPortfolioTable();
    });
    accountStatementClientSearch?.addEventListener("input", () => {
        if (accountStatementClientIdInput) {
            accountStatementClientIdInput.value = "";
        }

        resetAccountStatementTable("Busca un cliente para generar el estado de cuenta.");
        setStatus(accountStatementStatus, "", "");
        window.clearTimeout(accountStatementSearchTimer);
        accountStatementSearchTimer = window.setTimeout(() => {
            searchAccountStatementClients().catch(() => {});
        }, 220);
    });
    accountStatementClientSearch?.addEventListener("keydown", event => {
        if (event.key === "Enter") {
            event.preventDefault();
            syncAccountStatementClientSelection();
            loadAccountStatement().catch(() => {});
        }
    });
    accountStatementMatches?.addEventListener("click", event => {
        const button = event.target.closest("[data-account-statement-match-id]");
        if (!button) {
            return;
        }

        if (accountStatementClientIdInput) {
            accountStatementClientIdInput.value = button.dataset.accountStatementMatchId || "";
        }

        if (accountStatementClientSearch) {
            accountStatementClientSearch.value = button.dataset.accountStatementMatchName || "";
        }

        renderAccountStatementMatches(state.accountStatementClientSuggestions);
        resetAccountStatementTable("Genera el estado de cuenta para el cliente seleccionado.");
        setStatus(accountStatementStatus, "", "");
    });
    accountStatementGenerateButton?.addEventListener("click", () => {
        syncAccountStatementClientSelection();
        loadAccountStatement().catch(() => {});
    });
    accountStatementPdfButton?.addEventListener("click", () => {
        downloadAccountStatementPdf().catch(() => {});
    });
    portfolioOverdueClearFiltersButton?.addEventListener("click", () => resetPortfolioGrid("overdue"));
    portfolioInvoicesClearFiltersButton?.addEventListener("click", () => resetPortfolioGrid("invoices"));
    document.addEventListener("click", event => {
        const columnButton = event.target.closest("[data-portfolio-column]");
        if (columnButton) {
            event.preventDefault();
            openPortfolioColumnMenu(
                columnButton.dataset.portfolioGrid || "overdue",
                columnButton.dataset.portfolioColumn || "",
                columnButton);
            return;
        }

        const menuActionButton = event.target.closest("[data-portfolio-menu-action]");
        if (menuActionButton) {
            event.preventDefault();
            handlePortfolioColumnMenuAction(menuActionButton);
            return;
        }

        if (!event.target.closest(".dashboard-column-menu")) {
            closePortfolioColumnMenu();
        }
    });
    document.addEventListener("keydown", event => {
        if (event.key === "Escape") {
            closePortfolioColumnMenu();
            closeAgentFeedbackModal();
        }
    });
    pnlYearFilter?.addEventListener("change", () => {
        closePnlDetailModal();
        state.pnlYear = Number(pnlYearFilter.value || currentYear);
        state.pnlMonth = Math.min(state.pnlMonth || 1, 12);
        buildPnlMonthOptions(12);
        loadPnl();
    });
    pnlMonthFilter?.addEventListener("change", () => {
        closePnlDetailModal();
        state.pnlMonth = Number(pnlMonthFilter.value || 1);
        loadPnl();
    });
    pnlVerticalFilter?.addEventListener("change", () => {
        closePnlDetailModal();
        state.pnlVertical = pnlVerticalFilter.value || "all";
        loadPnl();
    });
    licenciamientoYearFilter?.addEventListener("change", () => {
        state.licenciamientoYear = Number(licenciamientoYearFilter.value || licenciamientoDefaultYear);
        state.licenciamientoMonth = state.licenciamientoYear >= currentYear
            ? new Date().getMonth() + 1
            : licenciamientoDefaultMonth;
        buildLicenciamientoMonthOptions();
        loadLicenciamiento();
    });
    licenciamientoMonthFilter?.addEventListener("change", () => {
        state.licenciamientoMonth = Number(licenciamientoMonthFilter.value || licenciamientoDefaultMonth);
        loadLicenciamiento();
    });
    utilityBreakdownCloseBtn?.addEventListener("click", closeUtilityBreakdownModal);
    utilityBreakdownModal?.querySelectorAll("[data-utility-breakdown-close]").forEach(element => {
        element.addEventListener("click", closeUtilityBreakdownModal);
    });
    utilityRealDetailCloseBtn?.addEventListener("click", closeUtilityRealDetailModal);
    utilityRealDetailModal?.querySelectorAll("[data-utility-real-detail-close]").forEach(element => {
        element.addEventListener("click", closeUtilityRealDetailModal);
    });
    utilityOrphansCloseBtn?.addEventListener("click", closeUtilityOrphansModal);
    utilityOrphansModal?.querySelectorAll("[data-utility-orphans-close]").forEach(element => {
        element.addEventListener("click", closeUtilityOrphansModal);
    });
    pnlDetailCloseBtn?.addEventListener("click", closePnlDetailModal);
    pnlDetailModal?.querySelectorAll("[data-pnl-detail-close]").forEach(element => {
        element.addEventListener("click", closePnlDetailModal);
    });
    cloudBillingDetailCloseBtn?.addEventListener("click", closeCloudBillingDetailModal);
    cloudBillingDetailModal?.querySelectorAll("[data-cloud-billing-detail-close]").forEach(element => {
        element.addEventListener("click", closeCloudBillingDetailModal);
    });
    copiersEditorCloseBtn?.addEventListener("click", closeCopiersEditorModal);
    copiersEditorCancelBtn?.addEventListener("click", closeCopiersEditorModal);
    copiersEditorModal?.querySelectorAll("[data-copiers-editor-close]").forEach(element => {
        element.addEventListener("click", closeCopiersEditorModal);
    });
    copiersClientInvoicesCloseBtn?.addEventListener("click", closeCopiersClientInvoicesModal);
    copiersClientInvoicesModal?.querySelectorAll("[data-copiers-client-invoices-close]").forEach(element => {
        element.addEventListener("click", closeCopiersClientInvoicesModal);
    });
    copiersLineEquipmentCloseBtn?.addEventListener("click", closeCopiersLineEquipmentModal);
    copiersLineEquipmentCancelBtn?.addEventListener("click", closeCopiersLineEquipmentModal);
    copiersLineEquipmentSaveBtn?.addEventListener("click", saveCopiersLineEquipmentAssignment);
    copiersLineEquipmentModal?.querySelectorAll("[data-copiers-line-equipment-close]").forEach(element => {
        element.addEventListener("click", closeCopiersLineEquipmentModal);
    });
    copiersLineEquipmentAssignedBody?.addEventListener("click", event => {
        const button = event.target.closest("[data-copiers-line-equipment-remove]");
        if (button) {
            removeCopiersLineEquipment(button.dataset.copiersLineEquipmentRemove || "");
        }
    });
    copiersLineEquipmentAvailableBody?.addEventListener("click", event => {
        const button = event.target.closest("[data-copiers-line-equipment-assign]");
        if (button) {
            assignCopiersLineEquipment(button.dataset.copiersLineEquipmentAssign || "");
        }
    });
    copiersBillingCountersCloseBtn?.addEventListener("click", closeCopiersBillingCountersModal);
    copiersBillingCountersModal?.querySelectorAll("[data-copiers-billing-counters-close]").forEach(element => {
        element.addEventListener("click", closeCopiersBillingCountersModal);
    });
    copiersEquipmentDetailCloseBtn?.addEventListener("click", closeCopiersEquipmentDetailModal);
    copiersEquipmentDetailCancelBtn?.addEventListener("click", closeCopiersEquipmentDetailModal);
    copiersEquipmentDetailModal?.querySelectorAll("[data-copiers-equipment-close]").forEach(element => {
        element.addEventListener("click", closeCopiersEquipmentDetailModal);
    });
    copiersEditorForm?.addEventListener("submit", event => {
        event.preventDefault();
        saveCopiersEditor();
    });
    copiersEquipmentAssignmentForm?.addEventListener("submit", event => {
        event.preventDefault();
        saveCopiersEquipmentAssignment();
    });
    copiersBillingBody?.addEventListener("click", event => {
        const toggleButton = event.target.closest("[data-copiers-group-toggle]");
        if (toggleButton) {
            const groupId = toggleButton.dataset.copiersGroupToggle || "";
            if (state.copiersExpandedGroups.has(groupId)) {
                state.copiersExpandedGroups.delete(groupId);
            } else {
                state.copiersExpandedGroups.add(groupId);
            }

            renderCopiersTable(state.copiersDashboard);
            return;
        }

        const clientButton = event.target.closest("[data-copiers-group-client]");
        if (clientButton) {
            const group = getCopiersGroupById(clientButton.dataset.copiersGroupClient || "");
            if (group) {
                loadCopiersClientInvoices(group);
            }
            return;
        }

        const counterButton = event.target.closest("[data-copiers-counter-summary]");
        if (counterButton) {
            const group = getCopiersGroupById(counterButton.dataset.copiersCounterSummary || "");
            if (group) {
                openCopiersBillingCountersModal(group);
            }
            return;
        }

        const lineAssignmentButton = event.target.closest("[data-copiers-line-assignment]");
        if (lineAssignmentButton) {
            const row = getCopiersRowById(lineAssignmentButton.dataset.copiersLineAssignment || "");
            if (row) {
                loadCopiersLineEquipmentAssignment(row);
            }
            return;
        }

        const equipmentButton = event.target.closest("[data-copiers-equipment-id]");
        if (equipmentButton) {
            loadCopiersEquipmentDetail(equipmentButton.dataset.copiersEquipmentId || "");
            return;
        }

        const button = event.target.closest("[data-copiers-row-id]");
        if (!button) {
            return;
        }

        const row = getCopiersRowById(button.dataset.copiersRowId || "");
        if (!row) {
            return;
        }

        const fieldKey = button.dataset.copiersField || "";
        if (fieldKey === "clientName") {
            loadCopiersClientInvoices(row);
            return;
        }

        openCopiersEditorModal("edit", row, fieldKey);
    });
    copiersEquipmentBody?.addEventListener("click", event => {
        const button = event.target.closest("[data-copiers-equipment-id]");
        if (!button) {
            return;
        }

        loadCopiersEquipmentDetail(button.dataset.copiersEquipmentId || "");
    });
    copiersEquipmentMoveToStockInput?.addEventListener("change", () => {
        if (copiersEquipmentMoveToStockInput.checked) {
            copiersEquipmentClientIdInput && (copiersEquipmentClientIdInput.value = "");
            copiersEquipmentClientNameInput && (copiersEquipmentClientNameInput.value = "");
            return;
        }

        copiersEquipmentClientNameInput?.focus();
    });
    copiersEquipmentClientNameInput?.addEventListener("input", () => {
        if ((copiersEquipmentClientNameInput.value || "").trim()) {
            copiersEquipmentMoveToStockInput && (copiersEquipmentMoveToStockInput.checked = false);
        }
    });
    copiersMaintenancePrevBtn?.addEventListener("click", () => {
        setCopiersMaintenancePage(state.copiersMaintenancePage - 1);
    });
    copiersMaintenanceNextBtn?.addEventListener("click", () => {
        setCopiersMaintenancePage(state.copiersMaintenancePage + 1);
    });
    copiersMaintenanceYearFilter?.addEventListener("change", () => {
        state.copiersMaintenanceYear = copiersMaintenanceYearFilter.value || "all";
        state.copiersMaintenancePage = 1;
        buildCopiersMaintenanceFilterOptions();
        renderCopiersMaintenanceTable();
        if (state.activeTab === "copiers" && state.copiersSubtab === "maintenance" && state.copiersEquipmentDashboard) {
            updateHeroForCopiers(state.copiersEquipmentDashboard);
        }
    });
    copiersMaintenanceMonthFilter?.addEventListener("change", () => {
        state.copiersMaintenanceMonth = copiersMaintenanceMonthFilter.value || "all";
        state.copiersMaintenancePage = 1;
        buildCopiersMaintenanceFilterOptions();
        renderCopiersMaintenanceTable();
        if (state.activeTab === "copiers" && state.copiersSubtab === "maintenance" && state.copiersEquipmentDashboard) {
            updateHeroForCopiers(state.copiersEquipmentDashboard);
        }
    });
    copiersMaintenanceOwnerFilter?.addEventListener("change", () => {
        state.copiersMaintenanceOwner = copiersMaintenanceOwnerFilter.value || "all";
        state.copiersMaintenancePage = 1;
        renderCopiersMaintenanceTable();
        if (state.activeTab === "copiers" && state.copiersSubtab === "maintenance" && state.copiersEquipmentDashboard) {
            updateHeroForCopiers(state.copiersEquipmentDashboard);
        }
    });
    wireCopiersLookupInput(copiersCountersClientNameFilter, copiersCountersClientIdFilter, copiersCountersClientOptions, "copiersCountersClientSuggestions", "name", buildCopiersClientSearchUrl);
    copiersCountersMonthFilter?.addEventListener("change", () => {
        handleCopiersCountersFilterChanged("Mes actualizado. Aplica el filtro para consultar Dataverse.");
    });
    copiersCountersYearFilter?.addEventListener("change", () => {
        handleCopiersCountersFilterChanged("Año actualizado. Aplica el filtro para consultar Dataverse.");
    });
    copiersCountersClientNameFilter?.addEventListener("input", () => {
        if (copiersCountersClientIdFilter) {
            copiersCountersClientIdFilter.value = "";
        }

        handleCopiersCountersFilterChanged("Cliente actualizado. Aplica el filtro para consultar Dataverse.");
    });
    copiersCountersClientNameFilter?.addEventListener("change", () => {
        syncCopiersLookupSelection(
            copiersCountersClientNameFilter,
            copiersCountersClientIdFilter,
            state.copiersCountersClientSuggestions,
            "name");
        handleCopiersCountersFilterChanged("Cliente actualizado. Aplica el filtro para consultar Dataverse.");
    });
    copiersCountersClientNameFilter?.addEventListener("blur", () => {
        syncCopiersLookupSelection(
            copiersCountersClientNameFilter,
            copiersCountersClientIdFilter,
            state.copiersCountersClientSuggestions,
            "name");
        syncCopiersCountersFiltersFromControls();
    });
    pnlDetailBody?.addEventListener("click", event => {
        const saveButton = event.target.closest("[data-pnl-detail-save]");
        if (!saveButton) {
            return;
        }

        savePnlDetailRecord(saveButton);
    });
    document.addEventListener("keydown", event => {
        if (event.key === "Escape" && isPortfolioMonthlyDetailOpen()) {
            closePortfolioMonthlyDetailModal();
            return;
        }

        if (event.key === "Escape" && isCloudBillingDetailOpen()) {
            closeCloudBillingDetailModal();
            return;
        }

        if (event.key === "Escape" && isBillingContractTypeModalOpen()) {
            closeBillingContractTypeModal();
            return;
        }

        if (event.key === "Escape" && isBillingInvoiceEditorOpen()) {
            closeBillingInvoiceEditorModal();
            return;
        }

        if (event.key === "Escape" && isCopiersClientInvoicesOpen()) {
            closeCopiersClientInvoicesModal();
            return;
        }

        if (event.key === "Escape" && isCopiersLineEquipmentOpen()) {
            closeCopiersLineEquipmentModal();
            return;
        }

        if (event.key === "Escape" && isCopiersBillingCountersOpen()) {
            closeCopiersBillingCountersModal();
            return;
        }

        if (event.key === "Escape" && isCopiersEquipmentDetailOpen()) {
            closeCopiersEquipmentDetailModal();
            return;
        }

        if (event.key === "Escape" && isCopiersEditorOpen()) {
            closeCopiersEditorModal();
            return;
        }

        if (event.key === "Escape" && isYtdDetailOpen()) {
            closeYtdDetailModal();
            return;
        }

        if (event.key === "Escape" && isUtilityBreakdownOpen()) {
            closeUtilityBreakdownModal();
            return;
        }

        if (event.key === "Escape" && isUtilityRealDetailOpen()) {
            closeUtilityRealDetailModal();
            return;
        }

        if (event.key === "Escape" && isUtilityOrphansOpen()) {
            closeUtilityOrphansModal();
            return;
        }

        if (event.key === "Escape" && isPnlDetailOpen()) {
            closePnlDetailModal();
        }
    });

    tabButtons.forEach(button => {
        button.addEventListener("click", () => {
            const tabKey = button.dataset.dashboardTab || "billing";
            if (tabKey !== state.activeTab) {
                setActiveTab(tabKey);
            }
        });
    });
    dashboardTodayRefreshButton?.addEventListener("click", () => loadToday());
    dashboardTodayCards?.addEventListener("click", event => {
        const card = event.target.closest("[data-today-card]");
        if (card) {
            openTodayDestination(card);
        }
    });
    dashboardGroupButtons.forEach(button => {
        button.addEventListener("click", () => {
            const tabKey = button.dataset.dashboardGroupTarget || "";
            if (tabKey && tabKey !== state.activeTab) {
                setActiveTab(tabKey);
            }
        });
    });
    billingSubtabButtons.forEach(button => {
        button.addEventListener("click", () => {
            const subtabKey = button.dataset.billingSubtab || "overview";
            if (subtabKey !== state.billingSubtab) {
                setBillingSubtab(subtabKey);
            }
        });
    });
    businessSubtabButtons.forEach(button => {
        button.addEventListener("click", () => {
            const subtabKey = button.dataset.businessSubtab || "all";
            if (subtabKey !== state.businessSubtab) {
                setBusinessSubtab(subtabKey);
            }
        });
    });
    copiersSubtabButtons.forEach(button => {
        button.addEventListener("click", () => {
            const subtabKey = button.dataset.copiersSubtab || "billing";
            if (subtabKey !== state.copiersSubtab) {
                setCopiersSubtab(subtabKey);
            }
        });
    });

    buildYearOptions();
    buildPnlYearOptions();
    buildLicenciamientoYearOptions();
    periodFilter && (periodFilter.value = state.period);
    pnlVerticalFilter && (pnlVerticalFilter.value = state.pnlVertical);
    wireCopiersLookupInput(billingInvoiceClientNameInput, billingInvoiceClientIdInput, billingInvoiceClientOptions, "billingInvoiceClientSuggestions", "name", buildCopiersClientSearchUrl);
    wireCopiersLookupInput(billingReportClientSearch, billingReportClientIdInput, billingReportClientOptions, "billingReportClientSuggestions", "name", buildCopiersClientSearchUrl);
    wireCopiersLookupInput(copiersClientNameInput, copiersClientIdInput, copiersClientOptions, "copiersClientSuggestions", "name", buildCopiersClientSearchUrl);
    wireCopiersLookupInput(copiersProductNameInput, copiersProductIdInput, copiersProductOptions, "copiersProductSuggestions", "description", buildCopiersProductSearchUrl);
    wireCopiersLookupInput(copiersEquipmentClientNameInput, copiersEquipmentClientIdInput, copiersEquipmentClientOptions, "copiersEquipmentClientSuggestions", "name", buildCopiersClientSearchUrl);
    buildValueOptions();
    cloudBillingStatusFilter && (cloudBillingStatusFilter.value = state.cloudBillingStatusFilter);
    renderSiigoCustomerSelect();
    syncSiigoDateRangeWithActivePeriod();
    buildPnlMonthOptions(12);
    buildLicenciamientoMonthOptions();
    buildCopiersMaintenanceFilterOptions();
    buildCopiersCountersPeriodOptions();
    renderCopiersCountersPending();
    renderCopiersMaintenanceTable();
    syncCopiersInventoryButtons();
    syncBusinessBillingFilterControls();
    setBillingSectionExpanded(billingReportSection, billingReportToggleButton, false, "Ocultar reportes", "Mostrar reportes");
    setBillingSectionExpanded(siigoApiSection, siigoToggleButton, false, "Ocultar Siigo API", "Mostrar Siigo API");
    resetBillingReportReference();
    syncBillingReportSelectionSummary();
    syncBillingInvoicesSelectionSummary();
    syncBillingSubtabVisibility();
    syncBusinessSubtabVisibility();
    buildPortfolioMonthlyYearOptions();
    syncPortfolioMonthlyFilterControls();
    syncPortfolioSubtabVisibility();
    syncAccountStatementPdfButton();
    syncPeriodScopeVisibility();
    syncDashboardGroupTabs();
    syncCopiersSubtabVisibility();
    loadToday();
})();
