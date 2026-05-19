(() => {
    const app = document.getElementById("billingDashboardApp");
    if (!app) {
        return;
    }

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

    const billingKpisContainer = document.getElementById("billingKpisContainer");
    const trendsContainer = document.getElementById("billingTrendsContainer");
    const billingReportToggleButton = document.getElementById("billingReportToggleBtn");
    const siigoToggleButton = document.getElementById("siigoToggleBtn");
    const billingReportSection = document.getElementById("billingReportSection");
    const siigoApiSection = document.getElementById("siigoApiSection");
    const billingInvoicesSearch = document.getElementById("billingInvoicesSearch");
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
    const copiersMaintenanceBody = document.getElementById("copiersMaintenanceBody");
    const copiersMaintenancePagination = document.getElementById("copiersMaintenancePagination");
    const copiersMaintenancePrevBtn = document.getElementById("copiersMaintenancePrevBtn");
    const copiersMaintenanceNextBtn = document.getElementById("copiersMaintenanceNextBtn");
    const copiersMaintenancePageSummary = document.getElementById("copiersMaintenancePageSummary");
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
    const portfolioInvoicesSearch = document.getElementById("portfolioInvoicesSearch");
    const portfolioInvoicesClearFiltersButton = document.getElementById("portfolioInvoicesClearFiltersBtn");
    const portfolioInvoicesResultsCount = document.getElementById("portfolioInvoicesResultsCount");
    const portfolioInvoicesHead = document.getElementById("portfolioInvoicesHead");
    const portfolioInvoicesBody = document.getElementById("portfolioInvoicesBody");

    const businessRefreshButton = document.getElementById("businessRefreshBtn");
    const businessStatusBanner = document.getElementById("businessStatusBanner");
    const businessAsOfLabel = document.getElementById("businessAsOfLabel");
    const businessFocusLabel = document.getElementById("businessFocusLabel");
    const businessKpisContainer = document.getElementById("businessKpisContainer");
    const businessLinesChart = document.getElementById("businessLinesChart");
    const businessLineMeta = document.getElementById("businessLineMeta");
    const businessContractTypesChart = document.getElementById("businessContractTypesChart");
    const businessContractsList = document.getElementById("businessContractsList");
    const businessContractsCount = document.getElementById("businessContractsCount");
    const businessProductsChart = document.getElementById("businessProductsChart");

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
    const utilityBreakdownTitle = document.getElementById("utilityBreakdownTitle");
    const utilityBreakdownSubtitle = document.getElementById("utilityBreakdownSubtitle");
    const utilityBreakdownSummary = document.getElementById("utilityBreakdownSummary");
    const utilityBreakdownBody = document.getElementById("utilityBreakdownBody");
    const utilityBreakdownFooter = document.getElementById("utilityBreakdownFooter");

    const tabButtons = Array.from(document.querySelectorAll("[data-dashboard-tab]"));
    const tabPanels = Array.from(document.querySelectorAll("[data-dashboard-panel]"));

    const currentYear = Number(app.dataset.initialYear || new Date().getFullYear());
    const currentPeriod = app.dataset.initialPeriod || "month";
    const currentValue = Number(app.dataset.initialValue || 1);
    const currentMonth = new Date().getMonth() + 1;
    const currentBimonthly = Math.floor((currentMonth - 1) / 2) + 1;
    const currentFourMonthly = Math.floor((currentMonth - 1) / 4) + 1;
    const licenciamientoDefaultYear = Math.max(currentYear - 1, 2000);
    const licenciamientoDefaultMonth = 12;
    const taxesReteFuenteExportUrl = app.dataset.taxesRetefuenteExportUrl || app.dataset.taxesReteFuenteExportUrl || "";
    const taxesVatExportUrl = app.dataset.taxesVatExportUrl || "";
    const billingClientReportExportUrl = app.dataset.billingClientReportExportUrl || "";
    const copiersCountersPdfUrl = app.dataset.copiersCountersPdfUrl || "";

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
        activeTab: "billing",
        year: currentYear,
        period: currentPeriod,
        value: currentValue,
        billingDashboard: null,
        billingInvoicesDetail: null,
        billingInvoicesLoading: false,
        billingInvoicesSaving: false,
        billingInvoicesDeleting: false,
        billingInvoicesContractSaving: false,
        billingInvoicesSearchTerm: "",
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
        copiersMaintenancePage: 1,
        copiersCountersYear: currentYear,
        copiersCountersMonth: currentValue,
        copiersCountersClientId: "",
        copiersCountersClientName: "",
        copiersCountersClientSuggestions: [],
        copiersCountersHasAppliedFilters: false,
        portfolioSearchTerm: "",
        portfolioInvoicesSearchTerm: "",
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
        licenciamientoDashboard: null,
        licenciamientoSignature: "",
        licenciamientoYear: licenciamientoDefaultYear,
        licenciamientoMonth: licenciamientoDefaultMonth,
        utilityDashboard: null,
        utilitySignature: "",
        utilityLoading: false,
        utilityAssigningRecordId: "",
        pnlDetail: null,
        pnlDetailContext: null,
        pnlDetailLoading: false,
        pnlDetailSavingRecordId: "",
        periodLoading: false,
        portfolioLoading: false,
        businessLoading: false,
        pnlLoading: false,
        licenciamientoLoading: false
    };

    function escapeHtml(value) {
        return (value ?? "").toString()
            .replace(/&/g, "&amp;")
            .replace(/</g, "&lt;")
            .replace(/>/g, "&gt;")
            .replace(/"/g, "&quot;")
            .replace(/'/g, "&#39;");
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

    function getLicenciamientoSignature() {
        return `${state.licenciamientoYear}|${state.licenciamientoMonth}`;
    }

    function getUtilitySignature() {
        return "2025-to-date";
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
            billingInvoicesRefreshButton,
            billingInvoicesDuplicatesButton,
            billingInvoicesClearFiltersButton
        ].forEach(element => {
            if (element) {
                element.disabled = loading || state.billingInvoicesDeleting || state.billingInvoicesContractSaving;
            }
        });

        syncBillingInvoicesSelectionSummary();
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
            portfolioInvoicesClearFiltersButton
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
    }

    function isPnlDetailOpen() {
        return Boolean(pnlDetailModal && !pnlDetailModal.hidden);
    }

    function isUtilityBreakdownOpen() {
        return Boolean(utilityBreakdownModal && !utilityBreakdownModal.hidden);
    }

    function closeUtilityBreakdownModal() {
        if (!utilityBreakdownModal) {
            return;
        }

        utilityBreakdownModal.hidden = true;
        document.body.classList.remove("dashboard-modal-open");

        if (utilityBreakdownTitle) {
            utilityBreakdownTitle.textContent = "Detalle de utilidad teorica";
        }

        if (utilityBreakdownSubtitle) {
            utilityBreakdownSubtitle.textContent = "Filas incluidas en el calculo seleccionado.";
        }

        if (utilityBreakdownSummary) {
            utilityBreakdownSummary.innerHTML = "";
        }

        if (utilityBreakdownBody) {
            utilityBreakdownBody.innerHTML = '<tr><td colspan="9" class="dashboard-table__empty">Selecciona una tarjeta de utilidad para ver el desglose.</td></tr>';
        }

        if (utilityBreakdownFooter) {
            utilityBreakdownFooter.innerHTML = "";
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
                    <td class="text-end">${escapeHtml(currencyFormatter.format(Number(row.totalInvoice || 0)))}</td>
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

    function getFilteredCopiersMaintenanceRows() {
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
                    <td>${renderCopiersMaintenanceDateCell(row)}</td>
                    <td>${escapeHtml(row.equipmentSerial || "Sin equipo")}</td>
                    <td>${escapeHtml(row.clientName || "Sin cliente")}</td>
                    <td>${escapeHtml(row.technicianName || "Sin owner")}</td>
                    <td>${renderCopiersMaintenanceStatusBadge(row)}</td>
                    <td>${renderCopiersMaintenanceDetailCell(row)}</td>
                    <td class="text-center">${renderCopiersMaintenanceAttachmentCell(row)}</td>
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

        state.pnlDetailContext = {
            rowKey,
            rowLabel: rowLabel || "Detalle de la celda",
            cellMonth: Number.isFinite(Number(cellMonth)) ? Number(cellMonth) : null
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

    function buildBillingInvoicesUrl() {
        return app.dataset.billingInvoicesUrl || "";
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

    function buildBusinessUrl() {
        return app.dataset.businessUrl || "";
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

        if (Number.isFinite(Number(cellMonth))) {
            params.set("cellMonth", String(Number(cellMonth)));
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
            body: options.body
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
            const totalInvoice = Number(row.totalInvoice || 0);
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
                    <td class="text-end">${escapeHtml(currencyFormatter.format(Number(row.vatValue || 0)))}</td>
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
                               data-total="${escapeHtml(formatEditableDecimalValue(Number(row.total || 0)))}" />
                    </td>
                    <td>${escapeHtml(row.name || row.prefix || "-")}</td>
                    <td>${escapeHtml(row.dateDisplay || row.dateValue || "Sin fecha")}</td>
                    <td>${escapeHtml(row.customerIdentification || detail?.customerIdentification || "-")}</td>
                    <td><span class="${statusClass}">${escapeHtml(status)}</span></td>
                    <td><span class="${mailStatusClass}">${escapeHtml(mailStatus)}</span></td>
                    <td class="text-end">${escapeHtml(currencyFormatter.format(Number(row.total || 0)))}</td>
                    <td class="text-end">${escapeHtml(currencyFormatter.format(Number(row.balance || 0)))}</td>
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
        renderBusinessLinesChart(dashboard);
        renderBusinessContractTypes(dashboard);
        renderBusinessContracts(dashboard);
        renderBusinessProducts(dashboard);
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

    function renderUtilityBreakdownModal(card) {
        const rows = Array.isArray(card?.breakdown) ? card.breakdown : [];
        const sales = Number(card?.sales || 0);
        const cost = Number(card?.cost || 0);
        const utility = Number(card?.utility || 0);

        if (utilityBreakdownTitle) {
            utilityBreakdownTitle.textContent = card?.label || "Detalle de utilidad teorica";
        }

        if (utilityBreakdownSubtitle) {
            utilityBreakdownSubtitle.textContent = `${state.utilityDashboard?.periodLabel || "Periodo activo"} · ${numberFormatter.format(rows.length)} fila(s)`;
        }

        if (utilityBreakdownSummary) {
            utilityBreakdownSummary.innerHTML = `
                <div class="utility-breakdown-summary__item">
                    <span>Venta</span>
                    <strong>${escapeHtml(currencyFormatter.format(sales))}</strong>
                </div>
                <div class="utility-breakdown-summary__item">
                    <span>Costo</span>
                    <strong>${escapeHtml(currencyFormatter.format(cost))}</strong>
                </div>
                <div class="utility-breakdown-summary__item">
                    <span>Total tarjeta</span>
                    <strong>${escapeHtml(formatSignedCurrency(utility))}</strong>
                </div>
                <div class="utility-breakdown-summary__item">
                    <span>Margen</span>
                    <strong>${escapeHtml(formatUtilityPercent(card?.utilityPercent))}</strong>
                </div>
            `;
        }

        if (utilityBreakdownBody) {
            utilityBreakdownBody.innerHTML = rows.length
                ? rows.map(row => {
                    const hasCost = row?.hasCost !== false;
                    const costValue = Number(row?.cost || 0);
                    const lineUtility = Number(row?.utility || 0);
                    const lineTone = lineUtility >= 0 ? "positive" : "negative";
                    const billingDay = Number(row?.billingDay || 0);
                    return `
                        <tr>
                            <td>
                                <div class="utility-row-main">${escapeHtml(row?.clientName || "Sin cliente")}</div>
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
                }).join("")
                : '<tr><td colspan="9" class="dashboard-table__empty">Esta tarjeta no tiene filas para desglosar.</td></tr>';
        }

        if (utilityBreakdownFooter) {
            utilityBreakdownFooter.innerHTML = `
                <tr class="dashboard-table__total">
                    <td colspan="6">Total tarjeta</td>
                    <td class="text-end">${escapeHtml(currencyFormatter.format(sales))}</td>
                    <td class="text-end">${escapeHtml(currencyFormatter.format(cost))}</td>
                    <td class="text-end">${escapeHtml(formatSignedCurrency(utility))}</td>
                </tr>
            `;
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

        renderUtilityBreakdownModal(card);
        utilityBreakdownModal.hidden = false;
        document.body.classList.add("dashboard-modal-open");
        window.setTimeout(() => utilityBreakdownCloseBtn?.focus(), 30);
    }

    function renderUtilitySummaryCard(card, accentClass) {
        const sales = Number(card?.sales || 0);
        const cost = Number(card?.cost || 0);
        const utility = Number(card?.utility || 0);
        const tone = utility >= 0 ? "positive" : "negative";
        const cardKey = card?.key || "";
        const rows = Array.isArray(card?.breakdown) ? card.breakdown.length : Number(card?.recordsCount || 0);
        return `
            <article class="utility-summary-card ${accentClass} is-${tone}">
                <div class="utility-summary-card__header">
                    <span class="utility-summary-card__label">${escapeHtml(card?.label || "")}</span>
                    <button type="button"
                            class="btn btn-sm btn-outline-secondary utility-summary-card__breakdown-btn"
                            data-utility-breakdown="${escapeHtml(cardKey)}"
                            ${rows > 0 ? "" : "disabled"}>Desglose</button>
                </div>
                <strong class="utility-summary-card__value">${escapeHtml(formatSignedCurrency(utility))}</strong>
                <div class="utility-summary-card__meta">
                    <span>Venta ${escapeHtml(currencyFormatter.format(sales))}</span>
                    <span>Costo ${escapeHtml(currencyFormatter.format(cost))}</span>
                </div>
                <div class="utility-summary-card__footer">
                    <span>${escapeHtml(numberFormatter.format(Number(card?.recordsCount || 0)))} filas</span>
                    <span>${escapeHtml(formatUtilityPercent(card?.utilityPercent))}</span>
                    ${Number(card?.missingCostCount || 0) > 0 ? `<span>${escapeHtml(numberFormatter.format(Number(card?.missingCostCount || 0)))} sin costo</span>` : ""}
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
            return `
                <g>
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
            { title: "Facturacion emitida", currentKey: "billingCurrent", previousKey: "billingPrevious", growthKey: "billingGrowthPercent", color: "#0f766e" },
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

        return `
            <div class="dashboard-tax-filters">
                <label class="dashboard-filter dashboard-filter--compact">
                    <span class="dashboard-filter__label">Año</span>
                    <select class="form-select dashboard-select" data-taxes-filter="year" data-taxes-section="${escapeHtml(section.key || "")}">
                        ${yearOptions.map(option => `<option value="${escapeHtml(String(option.value))}" ${Number(option.value) === Number(filter.year) ? "selected" : ""}>${escapeHtml(option.label || String(option.value))}</option>`).join("")}
                    </select>
                </label>
                ${valueControl}
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
        const monthAttribute = Number.isFinite(Number(cellMonth))
            ? `data-pnl-cell-month="${Number(cellMonth)}"`
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
            : status === "Pendiente"
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
            key: "totalInvoice",
            label: "Valor",
            type: "number",
            align: "end",
            displayValue: row => currencyFormatter.format(Number(row.totalInvoice || 0)),
            render: row => renderPortfolioCurrency(row.totalInvoice)
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
            const thClass = column.align === "end" ? "text-end" : "";
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
                        <td class="${column.align === "end" ? "text-end" : ""}">
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
        gridState.sortKey = tableKey === "invoices" || tableKey === "billingInvoices" ? "emissionDateValue" : "ageDays";
        gridState.sortDirection = "desc";

        if (tableKey === "billingInvoices") {
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

    function updateHeroForCopiers(dashboard) {
        const fallbackFocus = state.copiersSubtab === "equipment"
            ? "Equipos asignados, stock y disponibilidad"
            : state.copiersSubtab === "inventory"
                ? "Valores comerciales y referencias pendientes"
            : state.copiersSubtab === "counters"
                ? "Consumo mensual de copias y escaneos"
            : state.copiersSubtab === "maintenance"
                ? "Mantenimientos, owners y actas"
                : "Ordenado por dia de facturacion";
        const focusLabel = state.copiersSubtab === "billing"
            || state.copiersSubtab === "counters"
            || state.copiersSubtab === "inventory"
            ? (dashboard?.focusLabel || fallbackFocus)
            : fallbackFocus;
        const activeRecordCount = state.copiersSubtab === "maintenance"
            ? getFilteredCopiersMaintenanceRows().length
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

    function updateBillingContext(dashboard) {
        state.billingDashboard = dashboard;
        state.billingSignature = getPeriodSignature();
        periodLabel && (periodLabel.textContent = dashboard?.periodLabel || "Sin periodo");
        dateRangeLabel && (dateRangeLabel.textContent = dashboard?.dateRangeLabel || "-");

        if (state.activeTab === "billing") {
            updateHeroForBilling(dashboard);
        }
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

    function updateBusinessContext(dashboard) {
        state.businessDashboard = dashboard;
        businessAsOfLabel && (businessAsOfLabel.textContent = dashboard?.asOfDateLabel || "Sin corte");
        businessFocusLabel && (businessFocusLabel.textContent = dashboard?.focusLabel || "Productos Cloud agrupados por cliente");

        if (state.activeTab === "business") {
            updateHeroForBusiness(dashboard);
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
        utilityPeriodLabel && (utilityPeriodLabel.textContent = dashboard?.periodLabel || "Ene 2025 - hoy");
        utilityDateRangeLabel && (utilityDateRangeLabel.textContent = dashboard?.dateRangeLabel || "-");

        if (state.activeTab === "utility") {
            updateHeroForUtility(dashboard);
        }
    }

    function syncPeriodScopeVisibility() {
        if (dashboardPeriodScope) {
            dashboardPeriodScope.hidden = state.activeTab === "portfolio"
                || state.activeTab === "business"
                || state.activeTab === "copiers"
                || state.activeTab === "pnl"
                || state.activeTab === "licenciamiento"
                || state.activeTab === "utility"
                || state.activeTab === "taxes"
                || state.activeTab === "support-cloud";
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

        if (tabKey !== "billing" && isBillingInvoiceEditorOpen()) {
            closeBillingInvoiceEditorModal();
        }

        if (tabKey !== "billing" && isBillingContractTypeModalOpen()) {
            closeBillingContractTypeModal();
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
            const isActive = button.dataset.dashboardTab === tabKey;
            button.classList.toggle("is-active", isActive);
            button.setAttribute("aria-selected", isActive ? "true" : "false");
        });

        tabPanels.forEach(panel => {
            const isActive = panel.dataset.dashboardPanel === tabKey;
            panel.classList.toggle("is-active", isActive);
            panel.hidden = !isActive;
        });

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
            if (state.portfolioDashboard) {
                updateHeroForPortfolio(state.portfolioDashboard);
            } else {
                loadPortfolio();
            }
            return;
        }

        if (tabKey === "business") {
            if (state.businessDashboard) {
                updateHeroForBusiness(state.businessDashboard);
            } else {
                loadBusiness();
            }
            return;
        }

        if (tabKey === "support-cloud") {
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

        if (state.billingDashboard && state.billingSignature === getPeriodSignature()) {
            updateHeroForBilling(state.billingDashboard);
        } else {
            loadBilling();
        }
    }

    function loadActivePeriodTab() {
        if (state.activeTab === "taxes") {
            return;
        }

        loadBilling();
    }

    async function loadBillingInvoices(options = {}) {
        const url = buildBillingInvoicesUrl();
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
            state.billingInvoiceDuplicateNumbers = getBillingInvoiceDuplicateNumbers(rows);
            pruneBillingInvoiceSelections();
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
        const loaded = await loadBillingInvoices({ silent: true });
        if (!loaded) {
            return;
        }

        const duplicateCount = Array.isArray(state.billingInvoicesDetail?.invoices)
            ? state.billingInvoicesDetail.invoices.filter(isBillingInvoiceDuplicate).length
            : 0;
        const duplicateNumberCount = state.billingInvoiceDuplicateNumbers.size;
        showBillingDuplicateRows();
        setStatus(
            billingInvoicesStatus,
            duplicateCount ? "success" : "info",
            duplicateCount
                ? `Encontramos ${numberFormatter.format(duplicateCount)} registros en ${numberFormatter.format(duplicateNumberCount)} numero(s) de factura duplicados. Selecciona manualmente los que quieras eliminar.`
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

        try {
            const dashboard = await fetchJson(buildBillingUrl());
            updateBillingContext(dashboard);
            renderComparativeKpis(billingKpisContainer, dashboard?.kpis, dashboard?.compareYear);
            renderTrends(dashboard);
            if (!state.billingInvoicesDetail) {
                loadBillingInvoices().catch(() => {});
            }
            setStatus(billingStatusBanner, "", "");
        } catch (error) {
            setStatus(billingStatusBanner, "error", error instanceof Error ? error.message : "No fue posible cargar el dashboard.");
        } finally {
            setPeriodLoading(false);
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
        setPortfolioLoading(true);
        setStatus(portfolioStatusBanner, "info", "Actualizando tablero de cartera...");

        try {
            const dashboard = await fetchJson(buildPortfolioUrl());
            updatePortfolioContext(dashboard);
            renderPortfolioKpis(dashboard);
            renderPortfolioTable();
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
            setStatus(pnlStatusBanner, "", "");
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

    async function assignUtilityRow(button) {
        const url = buildUtilityAssignmentUrl();
        if (!url) {
            setStatus(utilityStatusBanner, "error", "No hay una URL configurada para asignar filas.");
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
        setStatus(utilityStatusBanner, "info", "Guardando asignacion...");

        try {
            const result = await fetchJson(url, {
                method: "POST",
                body: JSON.stringify(payload)
            });
            setStatus(utilityStatusBanner, "success", result?.message || "Fila asignada correctamente.");
            await loadUtility();
        } catch (error) {
            setStatus(utilityStatusBanner, "error", error instanceof Error ? error.message : "No fue posible asignar la fila.");
        } finally {
            state.utilityAssigningRecordId = "";
            setUtilityLoading(false);
            renderUtilityUnresolvedRows(state.utilityDashboard);
        }
    }

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
    billingReportToggleButton?.addEventListener("click", () => {
        toggleBillingSection(billingReportSection, billingReportToggleButton, "Ocultar reportes", "Mostrar reportes");
    });
    siigoToggleButton?.addEventListener("click", () => {
        toggleBillingSection(siigoApiSection, siigoToggleButton, "Ocultar Siigo API", "Mostrar Siigo API");
    });
    billingInvoicesRefreshButton?.addEventListener("click", () => {
        state.billingInvoicesGrid.duplicatesOnly = false;
        loadBillingInvoices();
    });
    billingInvoicesDuplicatesButton?.addEventListener("click", () => {
        findDuplicateBillingInvoices().catch(() => {});
    });
    billingInvoicesClearFiltersButton?.addEventListener("click", () => resetPortfolioGrid("billingInvoices"));
    billingInvoicesContractButton?.addEventListener("click", openBillingContractTypeModal);
    billingInvoicesDeleteButton?.addEventListener("click", () => {
        deleteSelectedBillingInvoices().catch(() => {});
    });
    billingInvoicesSearch?.addEventListener("input", () => {
        state.billingInvoicesSearchTerm = billingInvoicesSearch.value || "";
        renderBillingInvoicesTable();
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
    portfolioRefreshButton?.addEventListener("click", loadPortfolio);
    businessRefreshButton?.addEventListener("click", loadBusiness);
    copiersRefreshButton?.addEventListener("click", loadCopiers);
    copiersEquipmentRefreshButton?.addEventListener("click", () => {
        loadCopiersEquipment();
    });
    copiersInventoryExportButton?.addEventListener("click", exportCopiersInventory);
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

        const blockers = Array.isArray(state.copiersCountersDashboard?.exportBlockers)
            ? state.copiersCountersDashboard.exportBlockers
            : [];
        if (blockers.length || state.copiersCountersDashboard?.canExport === false) {
            const message = blockers.length
                ? blockers.map(item => item.message || "Pendiente sin detalle.").filter(Boolean).join("\n")
                : "Hay pendientes por solucionar antes de exportar.";
            window.alert(`Antes de exportar debes corregir:\n\n${message}`);
            setStatus(copiersStatusBanner, "warning", "Hay pendientes por solucionar antes de exportar el PDF de contadores.");
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
    utilitySummaryCards?.addEventListener("click", event => {
        const button = event.target.closest("[data-utility-breakdown]");
        if (!button) {
            return;
        }

        openUtilityBreakdownModal(button.dataset.utilityBreakdown || "monthly");
    });
    utilityUnresolvedBody?.addEventListener("click", event => {
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
    pnlDetailCloseBtn?.addEventListener("click", closePnlDetailModal);
    pnlDetailModal?.querySelectorAll("[data-pnl-detail-close]").forEach(element => {
        element.addEventListener("click", closePnlDetailModal);
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

        if (event.key === "Escape" && isUtilityBreakdownOpen()) {
            closeUtilityBreakdownModal();
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
    renderSiigoCustomerSelect();
    syncSiigoDateRangeWithActivePeriod();
    buildPnlMonthOptions(12);
    buildLicenciamientoMonthOptions();
    buildCopiersMaintenanceFilterOptions();
    buildCopiersCountersPeriodOptions();
    renderCopiersCountersPending();
    renderCopiersMaintenanceTable();
    syncCopiersInventoryButtons();
    setBillingSectionExpanded(billingReportSection, billingReportToggleButton, false, "Ocultar reportes", "Mostrar reportes");
    setBillingSectionExpanded(siigoApiSection, siigoToggleButton, false, "Ocultar Siigo API", "Mostrar Siigo API");
    resetBillingReportReference();
    syncBillingReportSelectionSummary();
    syncBillingInvoicesSelectionSummary();
    syncPeriodScopeVisibility();
    syncCopiersSubtabVisibility();
    loadBilling();
})();
