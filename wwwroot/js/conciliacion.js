(() => {
    const app = document.getElementById("conciliacionApp");
    if (!app) {
        return;
    }

    const updatePaymentUrl = app.dataset.updatePaymentUrl || "";
    const manualPaymentUrl = app.dataset.manualPaymentUrl || "";
    const cashFlowCategoryUrl = app.dataset.cashflowCategoryUrl || "";
    const cashFlowStatementImportUrl = app.dataset.cashflowStatementImportUrl || "";
    const bankBalancesUrl = app.dataset.bankBalancesUrl || "";
    const bankOpeningBalanceUrl = app.dataset.bankOpeningBalanceUrl || "";
    const cashFlowDescriptionUrl = app.dataset.cashflowDescriptionUrl || "";
    const cashFlowPendingUrl = app.dataset.cashflowPendingUrl || "";
    const cashFlowOmittedUrl = app.dataset.cashflowOmittedUrl || "";
    const cashFlowManualUrl = app.dataset.cashflowManualUrl || "";
    const preflightPaymentUrl = app.dataset.preflightPaymentUrl || "";
    const dryRunPaymentUrl = app.dataset.dryRunPaymentUrl || "";
    const sendPaymentUrl = app.dataset.sendPaymentUrl || "";
    const invoiceSearchUrl = app.dataset.invoiceSearchUrl || "";
    const invoiceAssignUrl = app.dataset.invoiceAssignUrl || "";
    const siigoCustomerSearchUrl = app.dataset.siigoCustomerSearchUrl || "";
    const clientPaymentInvoicesUrl = app.dataset.clientPaymentInvoicesUrl || "";
    const clientPaymentDataverseApplyUrl = app.dataset.clientPaymentDataverseApplyUrl || "";
    const clientPaymentDirectSendUrl = app.dataset.clientPaymentDirectSendUrl || "";
    const dianClassificationUrl = app.dataset.dianClassificationUrl || "";
    const dianCreateSupplierUrl = app.dataset.dianCreateSupplierUrl || "";
    const dianAnalyzeRutUrl = app.dataset.dianAnalyzeRutUrl || "";
    const dianRetryPurchasesUrl = app.dataset.dianRetryPurchasesUrl || "";
    const dianSupplierLookupUrl = app.dataset.dianSupplierLookupUrl || "";
    const deduccionesIvaImportUrl = app.dataset.deduccionesIvaImportUrl || "";
    const dianDryRunUrl = app.dataset.dianDryRunUrl || "";
    const dianSendUrl = app.dataset.dianSendUrl || "";
    const cuentaCobroEditorUrl = app.dataset.cuentaCobroEditorUrl || "";
    const cuentaCobroExpenseSaveUrl = app.dataset.cuentaCobroExpenseSaveUrl || "";
    const cuentaCobroClassificationUrl = app.dataset.cuentaCobroClassificationUrl || "";
    const cuentaCobroPreflightUrl = app.dataset.cuentaCobroPreflightUrl || "";
    const cuentaCobroSendUrl = app.dataset.cuentaCobroSendUrl || "";
    const cuentaCobroPaymentUrl = app.dataset.cuentaCobroPaymentUrl || "";
    const cuentaCobroManualUrl = app.dataset.cuentaCobroManualUrl || "";
    const cashFlowAccountUrl = app.dataset.cashflowAccountUrl || "";
    const accountingVoucherSendUrl = app.dataset.accountingVoucherSendUrl || "";
    const siigoSupplierSearchUrl = app.dataset.siigoSupplierSearchUrl || "";
    const supplierPaymentPurchasesUrl = app.dataset.supplierPaymentPurchasesUrl || "";
    const supplierPaymentSendUrl = app.dataset.supplierPaymentSendUrl || "";
    const supplierPaymentManualUrl = app.dataset.supplierPaymentManualUrl || "";
    const syncHealthUrl = app.dataset.syncHealthUrl || "";
    const syncBillingDifferencesUrl = app.dataset.syncBillingDifferencesUrl || "";
    const syncBillingCreateUrl = app.dataset.syncBillingCreateUrl || "";
    const syncBillingDeleteUrl = app.dataset.syncBillingDeleteUrl || "";
    const cashFlowMonthValidateUrl = app.dataset.cashflowMonthValidateUrl || "";
    const periodYear = Number(app.dataset.periodYear || 0);
    const periodMonth = Number(app.dataset.periodMonth || 0);
    const antiforgeryToken = app.querySelector('input[name="__RequestVerificationToken"]')?.value || "";
    const clientPaymentDifferenceTolerance = 2000;
    const transientSiigoUserMessage = "Siigo no está disponible temporalmente. La conciliación no pudo finalizar. Espera unos minutos y vuelve a intentarlo.";
    const isTransientSiigoFailure = (...values) => values.some((value) => {
        if (value === true) {
            return true;
        }
        const normalized = String(value || "")
            .normalize("NFD")
            .replace(/[\u0300-\u036f]/g, "")
            .toLowerCase();
        return normalized.includes("document_query_service")
            || normalized.includes("currently unavailable")
            || normalized.includes("temporalmente no disponible")
            || ["408", "429", "500", "502", "503", "504"]
                .some((status) => normalized.includes(`respondio ${status}`));
    });
    const resolveSiigoFailureMessage = (payload, fallbackMessage) => {
        const issues = Array.isArray(payload?.issues) ? payload.issues.join(" ") : "";
        if (isTransientSiigoFailure(
            payload?.isTransientSiigoFailure,
            payload?.message,
            payload?.detail,
            issues)) {
            return transientSiigoUserMessage;
        }
        return payload?.detail || payload?.message || fallbackMessage;
    };
    const statusBox = document.getElementById("cncStatus");
    const tabButtons = Array.from(app.querySelectorAll("[data-cnc-tab]"));
    const panels = Array.from(app.querySelectorAll("[data-cnc-panel]"));
    const verticalBar = app.querySelector(".cnc-vertical-bar");
    const paymentSearch = document.getElementById("cncPaymentSearch");
    const paymentStatusFilter = document.getElementById("cncPaymentStatusFilter");
    const paymentRowsBody = document.getElementById("cncPaymentRows");
    const paymentCount = document.getElementById("cncPaymentCount");
    const genericTableSearches = Array.from(app.querySelectorAll("[data-cnc-table-search]"));
    const columnFilterInputs = Array.from(app.querySelectorAll("[data-cnc-column-filter]"));
    const v2FilterButtons = Array.from(app.querySelectorAll("[data-cnc-v2-filter]"));
    const verticalButtons = Array.from(app.querySelectorAll("[data-cnc-vertical]"));
    const verticalCount = document.getElementById("cncVerticalCount");
    const reassignModal = document.getElementById("cncReassignModal");
    const reassignDescription = document.getElementById("cncReassignDescription");
    const reassignCategory = document.getElementById("cncReassignCategory");
    const reassignApply = document.getElementById("cncReassignApply");
    const dianEditModal = document.getElementById("cncDianEditModal");
    const dianEditDescription = document.getElementById("cncDianEditDescription");
    const dianAccount = document.getElementById("cncDianAccount");
    const dianSave = document.getElementById("cncDianSave");
    const dianSkip = document.getElementById("cncDianSkip");
    const dianSupplierModal = document.getElementById("cncDianSupplierModal");
    const dianSupplierTitle = document.getElementById("cncDianSupplierTitle");
    const dianSupplierDescription = document.getElementById("cncDianSupplierDescription");
    let dianSupplierFeedback = document.getElementById("cncDianSupplierFeedback");
    if (!dianSupplierFeedback && dianSupplierDescription) {
        dianSupplierFeedback = document.createElement("div");
        dianSupplierFeedback.id = "cncDianSupplierFeedback";
        dianSupplierFeedback.className = "cnc-modal__feedback";
        dianSupplierFeedback.setAttribute("role", "alert");
        dianSupplierFeedback.setAttribute("aria-live", "assertive");
        dianSupplierFeedback.hidden = true;
        dianSupplierDescription.insertAdjacentElement("afterend", dianSupplierFeedback);
    }
    const dianSupplierName = document.getElementById("cncDianSupplierName");
    const dianSupplierNit = document.getElementById("cncDianSupplierNit");
    const dianSupplierPersonType = document.getElementById("cncDianSupplierPersonType");
    const dianSupplierIdType = document.getElementById("cncDianSupplierIdType");
    const dianSupplierCheckDigit = document.getElementById("cncDianSupplierCheckDigit");
    const dianSupplierVatResponsible = document.getElementById("cncDianSupplierVatResponsible");
    const dianSupplierFiscalResponsibility = document.getElementById("cncDianSupplierFiscalResponsibility");
    const dianSupplierAddress = document.getElementById("cncDianSupplierAddress");
    const dianSupplierCity = document.getElementById("cncDianSupplierCity");
    const dianSupplierRutFile = document.getElementById("cncDianSupplierRutFile");
    const dianSupplierRutAnalyze = document.getElementById("cncDianSupplierRutAnalyze");
    const dianSupplierRutStatus = document.getElementById("cncDianSupplierRutStatus");
    const dianSupplierRutSection = document.getElementById("cncDianSupplierRutSection");
    const dianSupplierSave = document.getElementById("cncDianSupplierSave");
    const cuentaCobroModal = document.getElementById("cncCuentaCobroModal");
    const cuentaCobroDescription = document.getElementById("cncCuentaCobroDescription");
    const cuentaCobroAccount = document.getElementById("cncCuentaCobroAccount");
    const cuentaCobroSave = document.getElementById("cncCuentaCobroSave");
    const accountingVoucherModal = document.getElementById("cncAccountingVoucherModal");
    const accountingVoucherDescription = document.getElementById("cncAccountingVoucherDescription");
    const accountingVoucherAccount = document.getElementById("cncAccountingVoucherAccount");
    const accountingVoucherSave = document.getElementById("cncAccountingVoucherSave");
    const accountingVoucherSend = document.getElementById("cncAccountingVoucherSend");
    const accountingVoucherThirdPartyField = document.getElementById("cncAccountingVoucherThirdPartyField");
    const accountingVoucherThirdPartyQuery = document.getElementById("cncAccountingVoucherThirdPartyQuery");
    const accountingVoucherThirdPartySearch = document.getElementById("cncAccountingVoucherThirdPartySearch");
    const accountingVoucherThirdPartyResults = document.getElementById("cncAccountingVoucherThirdPartyResults");
    const accountingVoucherThirdPartySelected = document.getElementById("cncAccountingVoucherThirdPartySelected");
    const supplierPaymentModal = document.getElementById("cncSupplierPaymentModal");
    const supplierPaymentDescription = document.getElementById("cncSupplierPaymentDescription");
    const supplierPaymentSummary = document.getElementById("cncSupplierPaymentSummary");
    const supplierPaymentSupplierQuery = document.getElementById("cncSupplierPaymentSupplierQuery");
    const supplierPaymentSupplierSearch = document.getElementById("cncSupplierPaymentSupplierSearch");
    const supplierPaymentSuppliers = document.getElementById("cncSupplierPaymentSuppliers");
    const supplierPaymentPurchases = document.getElementById("cncSupplierPaymentPurchases");
    const supplierPaymentReteFuenteValue = document.getElementById("cncSupplierPaymentReteFuenteValue");
    const supplierPaymentReteFuenteRate = document.getElementById("cncSupplierPaymentReteFuenteRate");
    const supplierPaymentReteIcaValue = document.getElementById("cncSupplierPaymentReteIcaValue");
    const supplierPaymentReteIcaRate = document.getElementById("cncSupplierPaymentReteIcaRate");
    const supplierPaymentIssues = document.getElementById("cncSupplierPaymentIssues");
    const supplierPaymentPreview = document.getElementById("cncSupplierPaymentPreview");
    const supplierPaymentPayload = document.getElementById("cncSupplierPaymentPayload");
    const supplierPaymentResponse = document.getElementById("cncSupplierPaymentResponse");
    const supplierPaymentSend = document.getElementById("cncSupplierPaymentSend");
    const supplierPaymentSkip = document.getElementById("cncSupplierPaymentSkip");
    const invoiceModal = document.getElementById("cncInvoiceModal");
    const invoiceDescription = document.getElementById("cncInvoiceDescription");
    const invoiceQuery = document.getElementById("cncInvoiceQuery");
    const invoiceValue = document.getElementById("cncInvoiceValue");
    const invoiceSearchButton = document.getElementById("cncInvoiceSearchButton");
    const invoiceResults = document.getElementById("cncInvoiceResults");
    const invoiceSelected = document.getElementById("cncInvoiceSelected");
    const invoiceSave = document.getElementById("cncInvoiceSave");
    const monthCloseModal = document.getElementById("cncMonthCloseModal");
    const monthCloseDescription = document.getElementById("cncMonthCloseDescription");
    const syncSummary = app.querySelector("[data-cnc-sync-summary]");
    const syncGrid = app.querySelector("[data-cnc-sync-grid]");
    const syncRefreshButton = app.querySelector("[data-cnc-sync-refresh]");
    const billingDiffBox = app.querySelector("[data-cnc-billing-diff]");
    const billingDiffRefreshButton = app.querySelector("[data-cnc-billing-diff-refresh]");
    const billingCreateSelectedButton = app.querySelector("[data-cnc-billing-create-selected]");
    const billingDeleteSelectedButton = app.querySelector("[data-cnc-billing-delete-selected]");
    const deduccionesForm = app.querySelector("[data-cnc-deducciones-form]");
    const deduccionesFile = app.querySelector("[data-cnc-deducciones-file]");
    const deduccionesSubmit = app.querySelector("[data-cnc-deducciones-submit]");
    const deduccionesResult = app.querySelector("[data-cnc-deducciones-result]");
    const deduccionesHistoryJson = app.querySelector("[data-cnc-deducciones-history-json]");
    const deduccionesHistoryOpeners = Array.from(app.querySelectorAll("[data-cnc-deducciones-history-open]"));
    const bankImportForm = app.querySelector("[data-cnc-bank-import-form]");
    const bankImportResult = app.querySelector("[data-cnc-bank-import-result]");
    const bankBalanceSelect = app.querySelector("[data-cnc-bank-balance-select]");
    const bankBalanceCurrent = app.querySelector("[data-cnc-bank-balance-current]");
    const bankBalanceOpening = app.querySelector("[data-cnc-bank-balance-opening]");
    const bankBalanceEntries = app.querySelector("[data-cnc-bank-balance-entries]");
    const bankBalanceExits = app.querySelector("[data-cnc-bank-balance-exits]");
    const bankBalanceOpenButton = app.querySelector("[data-cnc-bank-balance-open-opening]");
    const bankBalanceModal = app.querySelector("[data-cnc-bank-balance-modal]");
    const bankBalanceInput = app.querySelector("[data-cnc-bank-balance-input]");
    const bankBalanceDescription = app.querySelector("[data-cnc-bank-balance-description]");
    const bankBalanceStatus = app.querySelector("[data-cnc-bank-balance-status]");
    const bankBalanceSave = app.querySelector("[data-cnc-bank-balance-save]");
    let activeReassignRow = null;
    let activeDianRow = null;
    let dianAccountBatchRows = [];
    let dianAccountBatchIndex = -1;
    let dianAccountBatchDirty = false;
    let activeDianSupplierRow = null;
    let dianSupplierRutAnalyzed = false;
    let dianSupplierEntryMode = "rut";
    let lastDeduccionesPayload = null;
    let deduccionesHistoryItems = [];
    try {
        deduccionesHistoryItems = JSON.parse(deduccionesHistoryJson?.textContent || "[]");
    } catch {
        deduccionesHistoryItems = [];
    }
    let activeCuentaCobroRow = null;
    let activeAccountingVoucherRow = null;
    let selectedAccountingVoucherThirdParty = null;
    let persistedAccountingVoucherThirdParty = null;
    let accountingVoucherThirdPartySearchSequence = 0;
    let accountingVoucherThirdPartySearchTimer = 0;
    let activeSupplierPaymentRow = null;
    let supplierPaymentBatchRows = [];
    let supplierPaymentBatchIndex = -1;
    let supplierPaymentBatchDirty = false;
    let selectedSupplierPaymentSupplier = null;
    let selectedSupplierPaymentPurchase = null;
    let activeInvoiceRow = null;
    let bulkReassignRows = [];
    let selectedInvoiceIds = [];
    let selectedInvoices = [];
    let cashFlowWizardRows = [];
    let cashFlowWizardAccumulatedGroups = [];
    let cashFlowWizardIndex = 0;
    let cashFlowWizardMode = "rows";
    let cashFlowWizardSupplierPayment = null;
    let cashFlowWizardClientPayment = null;
    let cashFlowWizardAdditionalCustomer = null;
    let cashFlowWizardCuentaCobro = null;
    let cashFlowWizardAccountingVoucher = null;
    let cashFlowPendingRow = null;
    let cashFlowPendingMode = "pending";
    let activeVertical = app.dataset.activeVertical || "Cloud";
    let syncLoaded = false;
    let syncLoading = false;
    let billingDifferencesLoading = false;
    const isNavigableTab = (button) => !button.hidden && button.getAttribute("aria-hidden") !== "true";
    const validTabKeys = new Set(tabButtons
        .filter(isNavigableTab)
        .map((button) => button.dataset.cncTab || "")
        .filter(Boolean));
    const validVerticalKeys = new Set(verticalButtons.map((button) => button.dataset.cncVertical || "").filter(Boolean));
    const activeTabStorageKey = `conciliacion.activeTab:${window.location.pathname}:${window.location.search}`;
    const activeVerticalStorageKey = `conciliacion.activeVertical:${window.location.pathname}:${window.location.search}`;

    const excludedReconciliationOptions = [
        { value: "salida-fe", label: "Factura proveedor" },
        { value: "cuenta-cobro", label: "Documento soporte" },
        { value: "comprobante-contable", label: "Comprobante contable" },
        { value: "traslado-interno", label: "Traslado interno" },
        { value: "no-incluida-conciliacion", label: "No incluida" }
    ];
    const excludedEntryReconciliationOptions = [
        { value: "entrada-fe", label: "Factura cliente" },
        { value: "cuenta-cobro", label: "Documento soporte" },
        { value: "entrada-comprobante", label: "Comprobante contable" },
        { value: "no-incluida-conciliacion", label: "No incluida" }
    ];

    const categoryOptions = {
        Entrada: [
            { value: "entrada-fe", label: "Factura cliente" },
            { value: "entrada-comprobante", label: "Comprobante contable" },
            { value: "no-incluida-conciliacion", label: "No incluida" },
            { value: "traslado-interno", label: "Traslado interno" }
        ],
        Salida: excludedReconciliationOptions,
        Traslado: [
            { value: "salida-fe", label: "Factura proveedor" },
            { value: "cuenta-cobro", label: "Documento soporte" },
            { value: "comprobante-contable", label: "Comprobante contable" },
            { value: "no-incluida-conciliacion", label: "No incluida" },
            { value: "traslado-interno", label: "Traslado interno" }
        ],
        NoIncluida: excludedReconciliationOptions
    };

    const excludedReconciliationOptionsByDirection = {
        Entrada: excludedEntryReconciliationOptions,
        Salida: excludedReconciliationOptions,
        Traslado: excludedReconciliationOptions
    };

    const categoryTone = (value) => {
        switch (value) {
            case "entrada-fe":
            case "salida-fe":
                return "success";
            case "cuenta-cobro":
            case "comprobante-contable":
            case "entrada-comprobante":
                return "info";
            case "traslado-interno":
                return "info";
            case "no-incluida-conciliacion":
                return "neutral";
            default:
                return "warning";
        }
    };

    const categoryLabel = (value) => {
        const allOptions = Object.values(categoryOptions).flat();
        return allOptions.find((item) => item.value === value)?.label || "Sin clasificar";
    };

    const categoryTarget = (value) => {
        switch (value) {
            case "entrada-fe":
                return "entradas-fe";
            case "salida-fe":
                return "salidas-fe";
            case "cuenta-cobro":
                return "cuentas-cobro";
            case "comprobante-contable":
            case "entrada-comprobante":
                return "comprobantes";
            case "no-incluida-conciliacion":
                return "huerfanos";
            case "traslado-interno":
                return "flujo-caja";
            default:
                return "";
        }
    };

    const categoryFlowName = (value) => {
        switch (value) {
            case "entrada-fe":
                return "Entradas FV";
            case "salida-fe":
                return "Salidas FC";
            case "cuenta-cobro":
                return "Cuentas de cobro";
            case "comprobante-contable":
            case "entrada-comprobante":
                return "Comprobantes";
            case "no-incluida-conciliacion":
                return "No incluidas";
            case "traslado-interno":
                return "Traslados internos";
            default:
                return "Flujo de caja";
        }
    };

    const setStatus = (message, tone) => {
        if (!statusBox) {
            return;
        }

        statusBox.textContent = message || "";
        statusBox.className = "cnc-status";
        if (tone) {
            statusBox.classList.add(`is-${tone}`);
        }
        statusBox.classList.toggle("show", Boolean(message));
    };

    const resolveTabKey = (key) => {
        const candidate = String(key || "").trim();
        if (validTabKeys.has(candidate)) {
            return candidate;
        }

        return tabButtons.find((button) => button.classList.contains("is-active"))?.dataset.cncTab
            || tabButtons[0]?.dataset.cncTab
            || "";
    };

    const resolveCurrentTab = () => tabButtons.find((button) => button.classList.contains("is-active"))?.dataset.cncTab
        || tabButtons[0]?.dataset.cncTab
        || "";

    const resolveVerticalKey = (key) => {
        const candidate = String(key || "").trim();
        const exact = Array.from(validVerticalKeys).find((value) => value === candidate);
        if (exact) {
            return exact;
        }

        const normalized = normalizeText(candidate);
        return Array.from(validVerticalKeys).find((value) => normalizeText(value) === normalized)
            || app.dataset.activeVertical
            || "Cloud";
    };

    const parseViewHash = () => {
        const raw = decodeURIComponent((window.location.hash || "").replace(/^#/, ""));
        if (!raw) {
            return { tab: "", vertical: "" };
        }

        if (!raw.includes("=") && validTabKeys.has(raw)) {
            return { tab: raw, vertical: "" };
        }

        const params = new URLSearchParams(raw);
        return {
            tab: params.get("tab") || params.get("phase") || "",
            vertical: params.get("vertical") || params.get("flow") || ""
        };
    };

    const persistViewState = (tabKey, verticalKey) => {
        const resolvedTab = resolveTabKey(tabKey);
        const resolvedVertical = resolveVerticalKey(verticalKey);
        if (!resolvedTab) {
            return;
        }

        try {
            window.localStorage.setItem(activeTabStorageKey, resolvedTab);
            window.localStorage.setItem(activeVerticalStorageKey, resolvedVertical);
        } catch {
            // Local storage can be disabled; the URL hash still preserves the view.
        }

        const hash = new URLSearchParams({ tab: resolvedTab, vertical: resolvedVertical }).toString();
        const nextUrl = `${window.location.pathname}${window.location.search}#${hash}`;
        window.history.replaceState(null, "", nextUrl);
    };

    const reloadPreservingView = () => {
        persistViewState(resolveCurrentTab(), activeVertical);
        window.location.reload();
    };

    const persistActiveTab = (key) => {
        const resolved = resolveTabKey(key);
        if (!resolved) {
            return;
        }

        persistViewState(resolved, activeVertical);
    };

    const resolveInitialTab = () => {
        const hashTab = parseViewHash().tab;
        if (validTabKeys.has(hashTab)) {
            return hashTab;
        }

        try {
            const stored = window.localStorage.getItem(activeTabStorageKey) || "";
            if (validTabKeys.has(stored)) {
                return stored;
            }
        } catch {
            // Ignore storage errors and keep the server-rendered active tab.
        }

        return resolveTabKey("");
    };

    const resolveInitialVertical = () => {
        const hashVertical = parseViewHash().vertical;
        if (hashVertical) {
            return resolveVerticalKey(hashVertical);
        }

        try {
            const stored = window.localStorage.getItem(activeVerticalStorageKey) || "";
            if (stored) {
                return resolveVerticalKey(stored);
            }
        } catch {
            // Ignore storage errors and keep the server-rendered vertical.
        }

        return resolveVerticalKey(activeVertical);
    };

    const setActiveTab = (key, persist = true) => {
        const resolvedKey = resolveTabKey(key);
        tabButtons.forEach((button) => {
            const active = button.dataset.cncTab === resolvedKey;
            button.classList.toggle("is-active", active);
            button.setAttribute("aria-selected", active ? "true" : "false");
        });

        panels.forEach((panel) => {
            const active = panel.dataset.cncPanel === resolvedKey;
            panel.classList.toggle("is-active", active);
            panel.hidden = !active;
        });

        if (verticalBar) {
            verticalBar.hidden = resolvedKey === "sincronizacion" || resolvedKey === "conciliacion-2";
        }
        if (persist) {
            persistActiveTab(resolvedKey);
        }
        if (resolvedKey === "sincronizacion") {
            loadSyncHealth();
        }
    };

    const normalizeText = (value) => String(value || "")
        .normalize("NFD")
        .replace(/[\u0300-\u036f]/g, "")
        .trim()
        .toLowerCase();

    const rowCountLabel = (value) => `${value.toLocaleString("es-CO")} fila${value === 1 ? "" : "s"}`;

    const getConciliacion2FilterValue = (kind) => normalizeText(
        v2FilterButtons.find((button) =>
            button.dataset.cncV2Filter === kind
            && button.classList.contains("is-active"))?.dataset.cncV2FilterValue || "");

    const rowMatchesConciliacion2Segments = (key, row) => {
        if (key !== "conciliacion-2") {
            return true;
        }

        const vertical = getConciliacion2FilterValue("vertical");
        const direction = getConciliacion2FilterValue("direction");
        return (!vertical || normalizeText(row.dataset.filterVertical) === vertical)
            && (!direction || normalizeText(row.dataset.filterDirection) === direction);
    };

    const setConciliacion2Filter = (button) => {
        const kind = button.dataset.cncV2Filter || "";
        v2FilterButtons
            .filter((item) => item.dataset.cncV2Filter === kind)
            .forEach((item) => {
                const active = item === button;
                item.classList.toggle("is-active", active);
                item.setAttribute("aria-pressed", active ? "true" : "false");
            });
        applyGenericTableFilter("conciliacion-2");
        if (kind === "vertical") {
            selectBankBalanceForSource(button.dataset.cncV2FilterValue || "");
        }
    };

    const verticalMatches = (flow) => {
        const normalizedFlow = normalizeText(flow);
        if (!normalizedFlow) {
            return false;
        }

        return normalizedFlow === normalizeText(activeVertical);
    };

    const updateVerticalButtons = () => {
        verticalButtons.forEach((button) => {
            const active = button.dataset.cncVertical === activeVertical;
            button.classList.toggle("is-active", active);
            button.setAttribute("aria-pressed", active ? "true" : "false");
        });
        app.dataset.activeVertical = activeVertical;
    };

    const updateVerticalCount = () => {
        if (!verticalCount) {
            return;
        }

        const activePanel = panels.find((panel) => !panel.hidden);
        const visibleRows = activePanel
            ? Array.from(activePanel.querySelectorAll("tr[data-record-id]")).filter((row) => !row.hidden).length
            : 0;
        verticalCount.textContent = `${activeVertical}: ${rowCountLabel(visibleRows)}`;
    };

    const applyGenericTableFilter = (key) => {
        const input = app.querySelector(`[data-cnc-table-search="${CSS.escape(key)}"]`);
        const bodies = Array.from(app.querySelectorAll(`[data-cnc-table-body="${CSS.escape(key)}"]`));
        const count = app.querySelector(`[data-cnc-table-count="${CSS.escape(key)}"]`);
        const query = normalizeText(input?.value);
        const columnFilters = columnFilterInputs
            .filter((filter) => filter.dataset.cncColumnFilter === key)
            .map((filter) => ({
                field: filter.dataset.cncFilterField || "",
                value: normalizeText(filter.value)
            }))
            .filter((filter) => filter.field && filter.value);
        const stageCounters = bodies.flatMap((body) => Array.from(body.querySelectorAll("[data-cnc-stage-count]")));
        const stageCounts = new Map(stageCounters.map((counter) => [counter, 0]));
        let visible = 0;

        bodies.flatMap((body) => Array.from(body.querySelectorAll("tr[data-record-id]"))).forEach((row) => {
            const ignoreVertical = row.dataset.cncIgnoreVertical === "true";
            const matches = (ignoreVertical || verticalMatches(row.dataset.flow))
                && rowMatchesConciliacion2Segments(key, row)
                && (!query || normalizeText(row.dataset.search).includes(query))
                && columnFilters.every((filter) =>
                    normalizeText(row.dataset[`filter${filter.field.charAt(0).toUpperCase()}${filter.field.slice(1)}`])
                        .includes(filter.value));
            row.hidden = !matches;
            if (matches) {
                visible += 1;
                const counter = row.closest(".cnc-pipeline-stage")?.querySelector("[data-cnc-stage-count]");
                if (counter) {
                    stageCounts.set(counter, (stageCounts.get(counter) || 0) + 1);
                }
            }
        });

        if (count) {
            count.textContent = rowCountLabel(visible);
        }
        stageCounters.forEach((counter) => {
            counter.textContent = rowCountLabel(stageCounts.get(counter) || 0);
        });
        refreshBulkSections();
        updateVerticalCount();
    };

    const getPaymentRows = () => Array.from(paymentRowsBody?.querySelectorAll("tr[data-record-id]") || []);

    const getDetailRow = (recordId) => paymentRowsBody?.querySelector(`tr[data-detail-for="${CSS.escape(recordId)}"]`);

    const applyPaymentFilters = () => {
        const query = normalizeText(paymentSearch?.value);
        const status = String(paymentStatusFilter?.value || "").trim();
        let visible = 0;
        const stageCounters = Array.from(paymentRowsBody?.querySelectorAll("[data-cnc-stage-count]") || []);
        const stageCounts = new Map(stageCounters.map((counter) => [counter, 0]));

        getPaymentRows().forEach((row) => {
            const rowStatus = row.dataset.status || "";
            const rowFlow = row.dataset.flow || "";
            const rowSearch = normalizeText(row.dataset.search);
            const matches = (!query || rowSearch.includes(query))
                && (!status || rowStatus === status)
                && verticalMatches(rowFlow);
            row.hidden = !matches;
            const detail = getDetailRow(row.dataset.recordId || "");
            if (detail) {
                detail.hidden = !matches;
            }
            if (matches) {
                visible += 1;
                const counter = row.closest(".cnc-pipeline-stage")?.querySelector("[data-cnc-stage-count]");
                if (counter) {
                    stageCounts.set(counter, (stageCounts.get(counter) || 0) + 1);
                }
            }
        });

        if (paymentCount) {
            paymentCount.textContent = rowCountLabel(visible);
        }
        stageCounters.forEach((counter) => {
            counter.textContent = rowCountLabel(stageCounts.get(counter) || 0);
        });
        refreshBulkSections();
        updateVerticalCount();
    };

    const refreshAllFilters = () => {
        updateVerticalButtons();
        applyPaymentFilters();
        genericTableSearches.forEach((input) => applyGenericTableFilter(input.dataset.cncTableSearch || ""));
        updateVerticalCount();
    };

    const applyHashViewState = () => {
        const hashState = parseViewHash();
        if (hashState.vertical) {
            activeVertical = resolveVerticalKey(hashState.vertical);
        }
        if (validTabKeys.has(hashState.tab)) {
            setActiveTab(hashState.tab, false);
        }
        refreshAllFilters();
    };

    const setCollapsibleState = (section, collapsed) => {
        section.dataset.cncCollapsed = collapsed ? "true" : "false";
        const button = section.querySelector(":scope > .cnc-pipeline-stage__header [data-cnc-collapse-toggle], :scope > .cnc-table-toolbar [data-cnc-collapse-toggle]");
        if (button) {
            button.textContent = collapsed ? "Expandir" : "Contraer";
            button.setAttribute("aria-expanded", collapsed ? "false" : "true");
        }
    };

    const initializeCollapsibleTables = () => {
        const sections = Array.from(app.querySelectorAll(".cnc-pipeline-stage, .cnc-payment-panel"))
            .filter((section) => !section.matches(".cnc-v2-panel, [data-cnc-no-collapse]"))
            .filter((section) => section.querySelector(":scope > .cnc-table-wrap"));

        sections.forEach((section, index) => {
            section.dataset.cncCollapsible = "true";
            const tableWrap = section.querySelector(":scope > .cnc-table-wrap");
            const header = section.querySelector(":scope > .cnc-pipeline-stage__header")
                || section.querySelector(":scope > .cnc-table-toolbar");
            if (!tableWrap || !header || header.querySelector("[data-cnc-collapse-toggle]")) {
                setCollapsibleState(section, true);
                return;
            }

            const button = document.createElement("button");
            button.type = "button";
            button.className = "cnc-collapse-button";
            button.dataset.cncCollapseToggle = "";
            button.setAttribute("aria-controls", `cncTableSection${index}`);
            tableWrap.id = tableWrap.id || `cncTableSection${index}`;
            button.addEventListener("click", () => {
                setCollapsibleState(section, section.dataset.cncCollapsed !== "true");
            });
            header.appendChild(button);
            setCollapsibleState(section, true);
        });
    };

    const statusTone = (status) => {
        switch (status) {
            case "Aprobado":
            case "ListoSiigo":
            case "EnviadoSiigo":
            case "Conciliado":
                return "success";
            case "Rechazado":
            case "BloqueadoSiigo":
            case "ErrorSiigo":
                return "danger";
            case "RevisionManual":
            case "DiferenciaFueraRango":
            case "FacturaAmbigua":
                return "warning";
            case "Sugerido":
                return "info";
            case "ReasignadoCategoria":
                return "neutral";
            default:
                return "neutral";
        }
    };

    const statusLabel = (status) => {
        switch (status) {
            case "RevisionManual":
                return "Revision manual";
            case "ListoSiigo":
                return "Listo Siigo";
            case "EnviadoSiigo":
                return "Enviado Siigo";
            case "ErrorSiigo":
                return "Error Siigo";
            case "Conciliado":
                return "Conciliado";
            case "BloqueadoSiigo":
                return "Bloqueado pre-Siigo";
            case "ReasignadoCategoria":
                return "Reasignado a otra categoria";
            case "DiferenciaFueraRango":
                return "Diferencia fuera de rango";
            case "SinFacturaDescripcion":
                return "Sin factura en descripcion";
            case "FacturaNoEncontrada":
                return "Factura no encontrada";
            case "FacturaAmbigua":
                return "Factura ambigua";
            default:
                return status || "Sin estado";
        }
    };

    const canSendPaymentStatus = (status) => status === "ListoSiigo" || status === "ErrorSiigo";

    const money = (value) => Number(value || 0).toLocaleString("es-CO", {
        style: "currency",
        currency: "COP",
        maximumFractionDigits: 0
    });

    const clientPaymentMoney = (value) => Number(value || 0).toLocaleString("es-CO", {
        style: "currency",
        currency: "COP",
        minimumFractionDigits: 0,
        maximumFractionDigits: 2
    });

    const renderIssueList = (row, selector, issues) => {
        const list = row.querySelector(selector);
        if (!list) {
            return;
        }

        list.innerHTML = "";
        const values = Array.isArray(issues)
            ? issues.filter((issue) => String(issue || "").trim())
            : [];
        list.hidden = values.length === 0;
        values.forEach((issue) => {
            const item = document.createElement("li");
            item.textContent = issue;
            list.appendChild(item);
        });
    };

    const invoiceRetentionTotal = (invoice) =>
        Number(invoice?.reteFteValue || 0)
        + Number(invoice?.reteIcaValue || 0)
        + Number(invoice?.rteIvaValue || 0);

    const summarizeInvoiceSelection = (invoices, paymentValue) => {
        const selected = Array.isArray(invoices) ? invoices : [];
        const invoiceTotal = selected.reduce((sum, invoice) => sum + Number(invoice?.totalInvoice || 0), 0);
        const retentions = selected.reduce((sum, invoice) => sum + invoiceRetentionTotal(invoice), 0);
        const netToPayment = invoiceTotal - retentions;
        const difference = invoiceTotal - Number(paymentValue || 0) - retentions;
        const clients = Array.from(new Set(selected
            .map((invoice) => normalizeText(invoice?.clientName || ""))
            .filter(Boolean)));

        return {
            count: selected.length,
            invoiceTotal,
            retentions,
            netToPayment,
            difference,
            hasDifferentClients: clients.length > 1
        };
    };

    const renderInvoiceSelectionSummary = (container, invoices, paymentValue, emptyMessage, options = {}) => {
        if (!container) {
            return;
        }

        const summary = summarizeInvoiceSelection(invoices, paymentValue);
        container.hidden = false;
        if (summary.count === 0) {
            if (options.hideWhenEmpty) {
                container.hidden = true;
                container.innerHTML = "";
                return;
            }
            container.className = "cnc-invoice-selected";
            container.innerHTML = `<small>${escapeHtml(emptyMessage || "Selecciona una o varias facturas.")}</small>`;
            return;
        }

        const tone = Math.abs(summary.difference) <= 5
            ? "success"
            : Math.abs(summary.difference) <= clientPaymentDifferenceTolerance
                ? "warning"
                : "danger";
        const warning = summary.hasDifferentClients
            ? `<small class="cnc-tone-warning">Revisa la seleccion: hay nombres de cliente diferentes.</small>`
            : "";
        container.className = `cnc-invoice-selected cnc-invoice-selected--${tone}`;
        container.innerHTML = `
            <strong>${summary.count} factura${summary.count === 1 ? "" : "s"} seleccionada${summary.count === 1 ? "" : "s"}</strong>
            <div class="cnc-invoice-selected__grid">
                <span>Total facturas <b>${escapeHtml(money(summary.invoiceTotal))}</b></span>
                <span>Retenciones <b>${escapeHtml(money(summary.retentions))}</b></span>
                <span>Neto contra pago <b>${escapeHtml(money(summary.netToPayment))}</b></span>
                <span>Diferencia <b>${escapeHtml(money(summary.difference))}</b></span>
            </div>
            ${warning}`;
    };

    const moneyPrecise = (value) => Number(value || 0).toLocaleString("es-CO", {
        style: "currency",
        currency: "COP",
        maximumFractionDigits: 2
    });

    const selectedBankBalanceOption = () =>
        bankBalanceSelect?.selectedOptions?.[0] || null;

    const readBankBalanceOption = (option = selectedBankBalanceOption()) => {
        if (!option) {
            return null;
        }

        return {
            bankKey: option.value || "",
            bankLabel: option.dataset.bankLabel || option.textContent || "",
            sourceFlow: option.dataset.sourceFlow || "",
            bankAccountCode: option.dataset.bankCode || "",
            bankAccountName: option.dataset.bankName || "",
            hasOpeningBalance: option.dataset.hasOpeningBalance === "true",
            openingBalance: Number(option.dataset.openingBalance || 0),
            totalEntries: Number(option.dataset.totalEntries || 0),
            totalExits: Number(option.dataset.totalExits || 0),
            currentBalance: Number(option.dataset.currentBalance || 0)
        };
    };

    const writeBankBalanceOption = (option, balance) => {
        if (!option || !balance) {
            return;
        }

        option.value = balance.bankKey || "";
        option.textContent = balance.bankLabel || balance.bankKey || "Banco";
        option.dataset.bankLabel = balance.bankLabel || "";
        option.dataset.sourceFlow = balance.sourceFlow || "";
        option.dataset.bankCode = balance.bankAccountCode || "";
        option.dataset.bankName = balance.bankAccountName || "";
        option.dataset.hasOpeningBalance = balance.hasOpeningBalance ? "true" : "false";
        option.dataset.openingBalance = String(Number(balance.openingBalance || 0));
        option.dataset.totalEntries = String(Number(balance.totalEntries || 0));
        option.dataset.totalExits = String(Number(balance.totalExits || 0));
        option.dataset.currentBalance = String(Number(balance.currentBalance || 0));
    };

    const renderBankBalance = () => {
        const balance = readBankBalanceOption();
        const hasBalance = Boolean(balance?.bankKey);
        if (bankBalanceCurrent) {
            bankBalanceCurrent.textContent = moneyPrecise(balance?.currentBalance || 0);
            bankBalanceCurrent.dataset.tone = Number(balance?.currentBalance || 0) < 0
                ? "negative"
                : "neutral";
        }
        if (bankBalanceOpening) {
            bankBalanceOpening.textContent = balance?.hasOpeningBalance
                ? moneyPrecise(balance.openingBalance)
                : "Sin definir";
        }
        if (bankBalanceEntries) {
            bankBalanceEntries.textContent = moneyPrecise(balance?.totalEntries || 0);
        }
        if (bankBalanceExits) {
            bankBalanceExits.textContent = moneyPrecise(balance?.totalExits || 0);
        }
        if (bankBalanceOpenButton) {
            bankBalanceOpenButton.disabled = !hasBalance;
        }
    };

    const replaceBankBalanceOptions = (balances) => {
        if (!bankBalanceSelect || !Array.isArray(balances)) {
            return;
        }

        const selectedKey = bankBalanceSelect.value;
        const fragment = document.createDocumentFragment();
        balances.forEach((balance) => {
            const option = document.createElement("option");
            writeBankBalanceOption(option, balance);
            fragment.appendChild(option);
        });
        bankBalanceSelect.replaceChildren(fragment);
        const canPreserveSelection = Array.from(bankBalanceSelect.options)
            .some((option) => option.value === selectedKey);
        if (canPreserveSelection) {
            bankBalanceSelect.value = selectedKey;
        }
        renderBankBalance();
    };

    const selectBankBalanceForSource = (sourceFlow) => {
        if (!bankBalanceSelect) {
            return;
        }

        const normalizedSource = normalizeText(sourceFlow);
        const match = Array.from(bankBalanceSelect.options)
            .find((option) => normalizeText(option.dataset.sourceFlow) === normalizedSource);
        if (match) {
            bankBalanceSelect.value = match.value;
            renderBankBalance();
        }
    };

    const refreshBankBalances = async () => {
        if (!bankBalancesUrl || !periodYear || !periodMonth) {
            return;
        }

        const url = new URL(bankBalancesUrl, window.location.origin);
        url.searchParams.set("year", String(periodYear));
        url.searchParams.set("month", String(periodMonth));
        const response = await fetch(url, {
            headers: { "Accept": "application/json" }
        });
        const payload = await response.json().catch(() => null);
        if (!response.ok || !Array.isArray(payload)) {
            throw new Error(payload?.detail || payload?.message || "No fue posible actualizar el saldo bancario.");
        }
        replaceBankBalanceOptions(payload);
    };

    const setBankBalanceModalStatus = (message = "", tone = "neutral") => {
        if (!bankBalanceStatus) {
            return;
        }

        bankBalanceStatus.textContent = message;
        bankBalanceStatus.dataset.tone = tone;
    };

    const closeBankBalanceModal = () => {
        if (bankBalanceModal) {
            bankBalanceModal.hidden = true;
        }
        setBankBalanceModalStatus();
    };

    const openBankBalanceModal = () => {
        const balance = readBankBalanceOption();
        if (!bankBalanceModal || !bankBalanceInput || !balance?.bankKey) {
            setStatus("Selecciona un banco antes de poner el saldo inicial.", "info");
            return;
        }

        if (bankBalanceDescription) {
            bankBalanceDescription.textContent =
                `Define el saldo al inicio de ${periodYear}-${String(periodMonth).padStart(2, "0")} para ${balance.bankLabel}.`;
        }
        bankBalanceInput.value = balance.hasOpeningBalance
            ? String(balance.openingBalance)
            : "";
        setBankBalanceModalStatus();
        bankBalanceModal.hidden = false;
        window.setTimeout(() => {
            bankBalanceInput.focus();
            bankBalanceInput.select();
        }, 0);
    };

    const saveBankOpeningBalance = async () => {
        const balance = readBankBalanceOption();
        const rawValue = String(bankBalanceInput?.value || "").trim();
        const openingBalance = Number(rawValue);
        if (!balance?.bankKey) {
            setBankBalanceModalStatus("Selecciona el banco del saldo inicial.", "error");
            return;
        }
        if (!rawValue || !Number.isFinite(openingBalance)) {
            setBankBalanceModalStatus("Digita un saldo inicial valido.", "error");
            bankBalanceInput?.focus();
            return;
        }
        if (!bankOpeningBalanceUrl) {
            setBankBalanceModalStatus("No se encontro la ruta para guardar el saldo inicial.", "error");
            return;
        }
        if (!antiforgeryToken) {
            setBankBalanceModalStatus("No se pudo validar la sesion para guardar el saldo.", "error");
            return;
        }

        const previousText = bankBalanceSave?.textContent || "";
        if (bankBalanceSave) {
            bankBalanceSave.disabled = true;
            bankBalanceSave.textContent = "Guardando...";
        }
        if (bankBalanceInput) {
            bankBalanceInput.disabled = true;
        }
        setBankBalanceModalStatus("Guardando y verificando en Dataverse...", "neutral");

        try {
            const response = await fetch(bankOpeningBalanceUrl, {
                method: "POST",
                headers: {
                    "Accept": "application/json",
                    "Content-Type": "application/json",
                    "RequestVerificationToken": antiforgeryToken
                },
                body: JSON.stringify({
                    year: periodYear,
                    month: periodMonth,
                    bankKey: balance.bankKey,
                    openingBalance
                })
            });
            const payload = await response.json().catch(() => ({}));
            if (!response.ok || !payload.balance) {
                throw new Error(payload.detail || payload.message || "No fue posible guardar el saldo inicial.");
            }

            const option = Array.from(bankBalanceSelect?.options || [])
                .find((item) => item.value === payload.balance.bankKey);
            writeBankBalanceOption(option, payload.balance);
            if (option && bankBalanceSelect) {
                bankBalanceSelect.value = option.value;
            }
            renderBankBalance();
            setStatus(payload.message || "Saldo inicial guardado.", "success");
            closeBankBalanceModal();
        } catch (error) {
            setBankBalanceModalStatus(
                error instanceof Error ? error.message : "No fue posible guardar el saldo inicial.",
                "error");
        } finally {
            if (bankBalanceSave) {
                bankBalanceSave.disabled = false;
                bankBalanceSave.textContent = previousText;
            }
            if (bankBalanceInput) {
                bankBalanceInput.disabled = false;
            }
        }
    };

    const numberLabel = (value) => Number(value || 0).toLocaleString("es-CO");

    const positiveNumberFromInput = (input) => {
        const value = Number(input?.value || 0);
        return Number.isFinite(value) && value > 0 ? value : 0;
    };

    const parseDataList = (value) => String(value || "")
        .split("|")
        .map((item) => item.trim())
        .filter(Boolean);

    const parseJson = (value) => {
        try {
            return value ? JSON.parse(value) : null;
        } catch {
            return null;
        }
    };

    const getPreviewPayloadRoot = (payload) => {
        if (!payload || typeof payload !== "object") {
            return {};
        }

        return payload.purchase
            || payload.supportDocument
            || payload.payment
            || payload;
    };

    const getPreviewItems = (payload) => {
        const root = getPreviewPayloadRoot(payload);
        return Array.isArray(root.items) ? root.items : [];
    };

    const resolvePreviewAccount = (item) => {
        if (item?.account && typeof item.account === "object") {
            return [item.account.code, item.account.name, item.account.movement].filter(Boolean).join(" - ");
        }

        return [item?.code, item?.type].filter(Boolean).join(" - ");
    };

    const resolvePreviewItemValue = (item) => {
        if (Number.isFinite(Number(item?.value))) {
            return Number(item.value);
        }

        const price = Number(item?.price || 0);
        const quantity = Number(item?.quantity || 1);
        return price * quantity;
    };

    const renderJsonInto = (box, value) => {
        if (!box) {
            return;
        }

        if (typeof value === "string") {
            box.textContent = value;
            return;
        }

        box.textContent = JSON.stringify(value || {}, null, 2);
    };

    const resetAccountSearch = (selectId) => {
        const input = app.querySelector(`[data-cnc-account-search-for="${CSS.escape(selectId)}"]`);
        const select = document.getElementById(selectId);
        if (input) {
            input.value = "";
        }
        select?.querySelectorAll("option").forEach((option) => {
            option.hidden = false;
        });
    };

    const initializeAccountSearches = () => {
        app.querySelectorAll("[data-cnc-account-search-for]").forEach((input) => {
            const selectId = input.dataset.cncAccountSearchFor || "";
            const select = document.getElementById(selectId);
            if (!select) {
                return;
            }

            const applyFilter = () => {
                const query = normalizeText(input.value);
                let firstMatch = "";
                select.querySelectorAll("option").forEach((option) => {
                    if (!option.value) {
                        option.hidden = false;
                        return;
                    }

                    const matches = !query || normalizeText(`${option.value} ${option.textContent}`).includes(query);
                    option.hidden = !matches;
                    if (matches && !firstMatch) {
                        firstMatch = option.value;
                    }
                });
                if (query && (!select.value || select.selectedOptions[0]?.hidden) && firstMatch) {
                    select.value = firstMatch;
                }
            };

            input.addEventListener("input", applyFilter);
            input.addEventListener("search", applyFilter);
        });
    };

    const setSyncLoading = () => {
        if (syncSummary) {
            syncSummary.innerHTML = "";
            const badge = document.createElement("span");
            badge.className = "cnc-badge cnc-badge--info";
            badge.textContent = "Consultando";
            const text = document.createElement("strong");
            text.textContent = "Calculando totales de Dataverse y Siigo...";
            syncSummary.append(badge, text);
        }
        if (syncGrid) {
            syncGrid.innerHTML = "";
            const empty = document.createElement("div");
            empty.className = "cnc-sync-empty";
            empty.textContent = "Consultando fuentes del periodo.";
            syncGrid.appendChild(empty);
        }
    };

    const renderSyncMetric = (label, value) => {
        const item = document.createElement("div");
        item.className = "cnc-sync-metric";
        const title = document.createElement("span");
        title.textContent = label;
        const number = document.createElement("strong");
        number.textContent = value;
        item.append(title, number);
        return item;
    };

    const renderSyncSystem = (label, total, count, vat) => {
        const system = document.createElement("div");
        system.className = "cnc-sync-system";
        const title = document.createElement("span");
        title.textContent = label;
        const amount = document.createElement("strong");
        amount.textContent = moneyPrecise(total);
        const meta = document.createElement("small");
        meta.textContent = `${numberLabel(count)} registros | IVA ${moneyPrecise(vat)}`;
        system.append(title, amount, meta);
        return system;
    };

    const renderSyncHealth = (payload) => {
        syncLoaded = true;
        if (syncSummary) {
            syncSummary.innerHTML = "";
            const badge = document.createElement("span");
            badge.className = `cnc-badge cnc-badge--${payload.statusTone || "neutral"}`;
            badge.textContent = payload.statusLabel || "Sin estado";
            const text = document.createElement("strong");
            text.textContent = `${payload.periodLabel || "Periodo"} | ${numberLabel(payload.totalDifferenceRows)} filas con diferencia`;
            const time = document.createElement("small");
            time.textContent = `Ultima consulta: ${payload.generatedAtDisplay || "sin fecha"}`;
            syncSummary.append(badge, text, time);
        }

        if (!syncGrid) {
            return;
        }

        syncGrid.innerHTML = "";
        const items = Array.isArray(payload.items) ? payload.items : [];
        if (items.length === 0) {
            const empty = document.createElement("div");
            empty.className = "cnc-sync-empty";
            empty.textContent = "No hay cruces configurados para este periodo.";
            syncGrid.appendChild(empty);
            return;
        }

        items.forEach((item) => {
            const card = document.createElement("article");
            card.className = `cnc-sync-card cnc-sync-card--${item.statusTone || "neutral"}`;

            const header = document.createElement("header");
            const heading = document.createElement("div");
            const title = document.createElement("h3");
            title.textContent = item.label || "Cruce";
            const description = document.createElement("p");
            description.textContent = item.description || "";
            heading.append(title, description);
            const badge = document.createElement("span");
            badge.className = `cnc-badge cnc-badge--${item.statusTone || "neutral"}`;
            badge.textContent = item.statusLabel || "Sin estado";
            header.append(heading, badge);

            const systems = document.createElement("div");
            systems.className = "cnc-sync-systems";
            systems.append(
                renderSyncSystem(item.dataverseLabel || "Dataverse", item.dataverseTotal, item.dataverseCount, item.dataverseVat),
                renderSyncSystem(item.siigoLabel || "Siigo", item.siigoTotal, item.siigoCount, item.siigoVat)
            );

            const metrics = document.createElement("div");
            metrics.className = "cnc-sync-metrics";
            metrics.append(
                renderSyncMetric("Diferencia Dataverse - Siigo", moneyPrecise(item.differenceTotal)),
                renderSyncMetric("Diferencia registros", numberLabel(item.countDifference)),
                renderSyncMetric("Diferencia IVA", moneyPrecise(item.vatDifference)),
                renderSyncMetric("Filas por revisar", rowCountLabel(Number(item.differenceRows || 0)))
            );

            const notes = document.createElement("small");
            notes.className = "cnc-sync-notes";
            notes.textContent = item.notes || "";
            card.append(header, systems, metrics, notes);
            syncGrid.appendChild(card);
        });
    };

    const loadSyncHealth = async (force = false) => {
        if (!syncHealthUrl || syncLoading || (syncLoaded && !force)) {
            return;
        }

        syncLoading = true;
        if (syncRefreshButton) {
            syncRefreshButton.disabled = true;
        }
        setSyncLoading();

        try {
            const response = await fetch(syncHealthUrl, { headers: { "Accept": "application/json" } });
            const payload = await response.json().catch(() => ({}));
            if (!response.ok) {
                throw new Error(payload.detail || payload.message || "No fue posible consultar la salud de sincronizacion.");
            }
            renderSyncHealth(payload);
        } catch (error) {
            if (syncSummary) {
                syncSummary.innerHTML = "";
                const badge = document.createElement("span");
                badge.className = "cnc-badge cnc-badge--danger";
                badge.textContent = "Error";
                const text = document.createElement("strong");
                text.textContent = error instanceof Error ? error.message : "Ocurrio un error inesperado.";
                syncSummary.append(badge, text);
            }
            if (syncGrid) {
                syncGrid.innerHTML = "";
                const empty = document.createElement("div");
                empty.className = "cnc-sync-empty";
                empty.textContent = "No se pudieron cargar los cruces.";
                syncGrid.appendChild(empty);
            }
        } finally {
            syncLoading = false;
            if (syncRefreshButton) {
                syncRefreshButton.disabled = false;
            }
        }
    };

    const setBillingDifferencesLoading = (message) => {
        if (!billingDiffBox) {
            return;
        }

        billingDiffBox.innerHTML = "";
        const empty = document.createElement("div");
        empty.className = "cnc-sync-empty";
        empty.textContent = message || "Consultando diferencias de facturacion.";
        billingDiffBox.appendChild(empty);
    };

    const createBillingCell = (value, className = "") => {
        const cell = document.createElement("td");
        if (className) {
            cell.className = className;
        }
        cell.textContent = value || "";
        return cell;
    };

    const createBillingMoneyCell = (value) => {
        const cell = createBillingCell(moneyPrecise(value), "is-money");
        const numeric = Number(value || 0);
        if (Math.abs(numeric) > 1) {
            cell.classList.add(numeric > 0 ? "text-success" : "text-danger");
        }
        return cell;
    };

    const createBillingInvoiceCell = (row) => {
        const cell = document.createElement("td");
        const title = document.createElement("strong");
        title.textContent = row.invoiceNumber || row.key || "Sin numero";
        cell.appendChild(title);

        const detail = [row.siigoInvoiceId ? `Siigo ${row.siigoInvoiceId}` : "", row.recordId ? `Dataverse ${row.recordId}` : ""]
            .filter(Boolean)
            .join(" | ");
        if (detail) {
            const small = document.createElement("small");
            small.textContent = detail;
            cell.appendChild(small);
        }

        return cell;
    };

    const createBillingStatusCell = (row) => {
        const cell = document.createElement("td");
        const badge = document.createElement("span");
        badge.className = `cnc-badge cnc-badge--${row.statusTone || "neutral"}`;
        badge.textContent = row.statusLabel || "Diferencia";
        cell.appendChild(badge);
        return cell;
    };

    const getCheckedBillingDifferenceValues = (action) => Array.from(
        billingDiffBox?.querySelectorAll(`[data-cnc-billing-diff-check="${action}"]:checked`) || []
    )
        .map((input) => action === "create" ? input.dataset.key : input.dataset.recordId)
        .map((value) => String(value || "").trim())
        .filter(Boolean);

    const refreshBillingDifferenceActions = () => {
        const creating = getCheckedBillingDifferenceValues("create").length;
        const deleting = getCheckedBillingDifferenceValues("delete").length;
        if (billingCreateSelectedButton) {
            billingCreateSelectedButton.disabled = billingDifferencesLoading || creating === 0;
        }
        if (billingDeleteSelectedButton) {
            billingDeleteSelectedButton.disabled = billingDifferencesLoading || deleting === 0;
        }

        billingDiffBox?.querySelectorAll("[data-cnc-billing-select-all]").forEach((master) => {
            const action = master.dataset.cncBillingSelectAll || "";
            const checks = Array.from(billingDiffBox.querySelectorAll(`[data-cnc-billing-diff-check="${action}"]`));
            const checked = checks.filter((input) => input.checked).length;
            master.checked = checks.length > 0 && checked === checks.length;
            master.indeterminate = checked > 0 && checked < checks.length;
        });
    };

    const attachBillingDifferenceSelectionHandlers = () => {
        billingDiffBox?.querySelectorAll("[data-cnc-billing-diff-check]").forEach((input) => {
            input.addEventListener("change", refreshBillingDifferenceActions);
        });
        billingDiffBox?.querySelectorAll("[data-cnc-billing-select-all]").forEach((master) => {
            master.addEventListener("change", () => {
                const action = master.dataset.cncBillingSelectAll || "";
                billingDiffBox.querySelectorAll(`[data-cnc-billing-diff-check="${action}"]`).forEach((input) => {
                    input.checked = master.checked;
                });
                refreshBillingDifferenceActions();
            });
        });
        refreshBillingDifferenceActions();
    };

    const renderBillingDifferenceSection = (title, description, rows, action, emptyMessage) => {
        const section = document.createElement("section");
        section.className = "cnc-sync-diff-section";
        const safeRows = Array.isArray(rows) ? rows : [];

        const header = document.createElement("header");
        const copy = document.createElement("div");
        const heading = document.createElement("h3");
        heading.textContent = title;
        const text = document.createElement("p");
        text.textContent = description;
        copy.append(heading, text);
        const badge = document.createElement("span");
        badge.className = safeRows.length === 0 ? "cnc-badge cnc-badge--success" : "cnc-badge cnc-badge--warning";
        badge.textContent = rowCountLabel(safeRows.length);
        header.append(copy, badge);
        section.appendChild(header);

        if (safeRows.length === 0) {
            const empty = document.createElement("div");
            empty.className = "cnc-sync-empty";
            empty.textContent = emptyMessage;
            section.appendChild(empty);
            return section;
        }

        const actionable = action === "create" || action === "delete";
        const wrap = document.createElement("div");
        wrap.className = "cnc-sync-table-wrap";
        const table = document.createElement("table");
        table.className = "cnc-sync-table";
        const thead = document.createElement("thead");
        const headRow = document.createElement("tr");
        if (actionable) {
            const selectHead = document.createElement("th");
            selectHead.className = "is-select";
            const master = document.createElement("input");
            master.type = "checkbox";
            master.dataset.cncBillingSelectAll = action;
            master.setAttribute("aria-label", `Seleccionar ${title}`);
            selectHead.appendChild(master);
            headRow.appendChild(selectHead);
        }

        ["Factura", "Fecha", "NIT / cliente", "Siigo", "Dataverse", "Diferencia", "Estado"].forEach((label, index) => {
            const th = document.createElement("th");
            th.textContent = label;
            if (index >= 3 && index <= 5) {
                th.className = "is-money";
            }
            headRow.appendChild(th);
        });
        thead.appendChild(headRow);

        const tbody = document.createElement("tbody");
        safeRows.forEach((row) => {
            const tr = document.createElement("tr");
            if (actionable) {
                const selectCell = document.createElement("td");
                selectCell.className = "is-select";
                const input = document.createElement("input");
                input.type = "checkbox";
                input.dataset.cncBillingDiffCheck = action;
                input.dataset.key = row.key || row.siigoInvoiceId || row.invoiceNumber || "";
                input.dataset.recordId = row.recordId || "";
                input.setAttribute("aria-label", `Seleccionar ${row.invoiceNumber || row.key || "factura"}`);
                selectCell.appendChild(input);
                tr.appendChild(selectCell);
            }

            tr.appendChild(createBillingInvoiceCell(row));
            tr.appendChild(createBillingCell(row.dateDisplay || row.dateValue || ""));
            tr.appendChild(createBillingCell(row.clientName || row.customerIdentification || ""));
            tr.appendChild(createBillingMoneyCell(row.siigoTotal));
            tr.appendChild(createBillingMoneyCell(row.dataverseTotal));
            tr.appendChild(createBillingMoneyCell(row.difference));
            tr.appendChild(createBillingStatusCell(row));
            tbody.appendChild(tr);
        });

        table.append(thead, tbody);
        wrap.appendChild(table);
        section.appendChild(wrap);
        return section;
    };

    const renderBillingDifferences = (payload) => {
        if (!billingDiffBox) {
            return;
        }

        billingDiffBox.innerHTML = "";
        const summary = document.createElement("div");
        summary.className = "cnc-sync-diff-summary";
        const badge = document.createElement("span");
        badge.className = `cnc-badge cnc-badge--${payload.statusTone || "neutral"}`;
        badge.textContent = payload.statusLabel || "Sin estado";
        const text = document.createElement("strong");
        const missing = Number(payload.missingInDataverseCount || 0);
        const onlyDataverse = Number(payload.onlyDataverseCount || 0);
        const amount = Number(payload.amountDifferenceCount || 0);
        text.textContent = `${payload.periodLabel || "Periodo"} | faltan ${numberLabel(missing)} en Dataverse | sobran ${numberLabel(onlyDataverse)} en Dataverse | valores ${numberLabel(amount)}`;
        const time = document.createElement("small");
        time.textContent = `Ultima consulta: ${payload.generatedAtDisplay || "sin fecha"}`;
        summary.append(badge, text, time);
        billingDiffBox.appendChild(summary);

        billingDiffBox.appendChild(renderBillingDifferenceSection(
            "Faltan en Dataverse",
            "Existen en Siigo con estado Accepted y no tienen registro equivalente en Dataverse.",
            payload.missingInDataverse,
            "create",
            "No hay facturas aceptadas de Siigo faltantes en Dataverse."
        ));
        billingDiffBox.appendChild(renderBillingDifferenceSection(
            "Estan en Dataverse y no en Siigo",
            "Registros de Dataverse del periodo que no aparecen como factura aceptada en Siigo.",
            payload.onlyDataverse,
            "delete",
            "No hay facturas sobrantes en Dataverse."
        ));
        billingDiffBox.appendChild(renderBillingDifferenceSection(
            "Valores diferentes",
            "Facturas cruzadas por identificador o numero, pero con diferencia de total o IVA.",
            payload.amountDifferences,
            "",
            "No hay diferencias de valor en las facturas cruzadas."
        ));
        attachBillingDifferenceSelectionHandlers();
    };

    const loadBillingDifferences = async () => {
        if (!syncBillingDifferencesUrl || billingDifferencesLoading) {
            return;
        }

        billingDifferencesLoading = true;
        if (billingDiffRefreshButton) {
            billingDiffRefreshButton.disabled = true;
        }
        refreshBillingDifferenceActions();
        setBillingDifferencesLoading("Consultando diferencias de facturacion en este momento...");

        try {
            const response = await fetch(syncBillingDifferencesUrl, { headers: { "Accept": "application/json" } });
            const payload = await response.json().catch(() => ({}));
            if (!response.ok) {
                throw new Error(payload.detail || payload.message || "No fue posible consultar las diferencias de facturacion.");
            }
            renderBillingDifferences(payload);
        } catch (error) {
            setBillingDifferencesLoading(error instanceof Error ? error.message : "Ocurrio un error inesperado.");
            setStatus(error instanceof Error ? error.message : "Ocurrio un error inesperado.", "error");
        } finally {
            billingDifferencesLoading = false;
            if (billingDiffRefreshButton) {
                billingDiffRefreshButton.disabled = false;
            }
            refreshBillingDifferenceActions();
        }
    };

    const runBillingDifferenceAction = async (action) => {
        const isCreate = action === "create";
        const values = getCheckedBillingDifferenceValues(action);
        if (values.length === 0) {
            setStatus(isCreate ? "Selecciona al menos una factura para crear." : "Selecciona al menos una factura para eliminar.", "warning");
            return;
        }

        if (!isCreate && !window.confirm(`Se eliminaran ${values.length.toLocaleString("es-CO")} factura(s) solo de Dataverse. Siigo no se modifica.`)) {
            return;
        }

        const url = isCreate ? syncBillingCreateUrl : syncBillingDeleteUrl;
        const button = isCreate ? billingCreateSelectedButton : billingDeleteSelectedButton;
        if (!url || billingDifferencesLoading) {
            return;
        }

        billingDifferencesLoading = true;
        const previousText = button?.textContent || "";
        if (button) {
            button.disabled = true;
            button.textContent = isCreate ? "Creando..." : "Eliminando...";
        }
        if (billingDiffRefreshButton) {
            billingDiffRefreshButton.disabled = true;
        }
        refreshBillingDifferenceActions();

        const body = isCreate
            ? { year: periodYear, month: periodMonth, invoiceKeys: values }
            : { year: periodYear, month: periodMonth, recordIds: values };

        try {
            const response = await fetch(url, {
                method: "POST",
                headers: {
                    "Accept": "application/json",
                    "Content-Type": "application/json"
                },
                body: JSON.stringify(body)
            });
            const payload = await response.json().catch(() => ({}));
            if (!response.ok) {
                throw new Error(payload.detail || payload.message || "No fue posible aplicar la correccion en Dataverse.");
            }

            if (payload.differences) {
                renderBillingDifferences(payload.differences);
            } else {
                await loadBillingDifferences();
            }

            const errors = Number(payload.errors || 0);
            setStatus(payload.message || "Correccion aplicada en Dataverse.", errors > 0 ? "warning" : "success");
            syncLoaded = false;
            loadSyncHealth(true);
        } catch (error) {
            setStatus(error instanceof Error ? error.message : "Ocurrio un error inesperado.", "error");
        } finally {
            billingDifferencesLoading = false;
            if (button) {
                button.textContent = previousText;
            }
            if (billingDiffRefreshButton) {
                billingDiffRefreshButton.disabled = false;
            }
            refreshBillingDifferenceActions();
        }
    };

    const setDeduccionesResultLoading = (message) => {
        if (!deduccionesResult) {
            return;
        }

        deduccionesResult.innerHTML = "";
        const empty = document.createElement("div");
        empty.className = "cnc-sync-empty";
        empty.textContent = message || "Importando deducciones IVA.";
        deduccionesResult.appendChild(empty);
    };

    const ensureDeduccionesDetailModal = () => {
        let modal = document.getElementById("cncDeduccionesDetailModal");
        if (modal) {
            return modal;
        }

        modal = document.createElement("div");
        modal.id = "cncDeduccionesDetailModal";
        modal.className = "cnc-modal";
        modal.hidden = true;
        modal.setAttribute("role", "dialog");
        modal.setAttribute("aria-modal", "true");
        modal.innerHTML = `
            <div class="cnc-modal__panel cnc-modal__panel--preview">
                <div class="cnc-modal__header">
                    <div>
                        <div class="cnc-kicker">Importación DIAN</div>
                        <h2 data-cnc-deducciones-detail-title>Detalle</h2>
                    </div>
                    <button type="button" class="btn btn-sm btn-outline-secondary" data-cnc-deducciones-detail-close>Cerrar</button>
                </div>
                <p class="cnc-modal__description" data-cnc-deducciones-detail-description></p>
                <div data-cnc-deducciones-detail-content></div>
            </div>`;
        modal.querySelectorAll("[data-cnc-deducciones-detail-close]").forEach((button) => {
            button.addEventListener("click", () => { modal.hidden = true; });
        });
        modal.addEventListener("click", (event) => {
            if (event.target === modal) {
                modal.hidden = true;
            }
        });
        app.appendChild(modal);
        return modal;
    };

    const openDeduccionesDetail = (title, rows, columns, options = {}) => {
        const modal = ensureDeduccionesDetailModal();
        const resolvedRows = Array.isArray(rows) ? rows : [];
        const heading = modal.querySelector("[data-cnc-deducciones-detail-title]");
        const description = modal.querySelector("[data-cnc-deducciones-detail-description]");
        const content = modal.querySelector("[data-cnc-deducciones-detail-content]");
        if (heading) {
            heading.textContent = title;
        }
        if (description) {
            description.textContent = options.description
                || `${numberLabel(resolvedRows.length)} registro(s) en esta categoría.`;
        }
        if (!content) {
            return;
        }

        content.innerHTML = "";
        if (resolvedRows.length === 0) {
            const empty = document.createElement("div");
            empty.className = "cnc-sync-empty";
            empty.textContent = options.emptyMessage || "No hay registros en esta categoría.";
            content.appendChild(empty);
            modal.hidden = false;
            return;
        }

        const wrap = document.createElement("div");
        wrap.className = "cnc-sync-table-wrap";
        const table = document.createElement("table");
        table.className = "cnc-sync-table cnc-sync-table--compact";
        const head = document.createElement("thead");
        const headRow = document.createElement("tr");
        columns.forEach((column) => {
            const th = document.createElement("th");
            th.textContent = column.label;
            headRow.appendChild(th);
        });
        const rowActions = Array.isArray(options.actions)
            ? options.actions.filter((action) => typeof action?.action === "function")
            : typeof options.action === "function"
                ? [{
                    label: options.actionLabel || "Abrir",
                    visible: options.actionVisible,
                    action: options.action,
                    className: "btn btn-sm btn-primary"
                }]
                : [];
        if (rowActions.length > 0) {
            const th = document.createElement("th");
            th.textContent = "Acción";
            headRow.appendChild(th);
        }
        head.appendChild(headRow);

        const body = document.createElement("tbody");
        resolvedRows.slice(0, 500).forEach((row) => {
            const tr = document.createElement("tr");
            columns.forEach((column) => {
                const td = document.createElement("td");
                const value = typeof column.value === "function" ? column.value(row) : row?.[column.value];
                td.textContent = value === null || value === undefined || value === "" ? "—" : String(value);
                tr.appendChild(td);
            });
            if (rowActions.length > 0) {
                const td = document.createElement("td");
                const visibleActions = rowActions.filter((action) =>
                    typeof action.visible !== "function" || action.visible(row));
                if (visibleActions.length > 0) {
                    const actions = document.createElement("div");
                    actions.className = "cnc-row-actions";
                    visibleActions.forEach((action) => {
                        const button = document.createElement("button");
                        button.type = "button";
                        button.className = action.className || "btn btn-sm btn-primary";
                        button.textContent = typeof action.label === "function"
                            ? action.label(row)
                            : action.label || "Abrir";
                        button.addEventListener("click", () => {
                            modal.hidden = true;
                            action.action(row);
                        });
                        actions.appendChild(button);
                    });
                    td.appendChild(actions);
                } else {
                    td.textContent = "—";
                }
                tr.appendChild(td);
            }
            body.appendChild(tr);
        });
        table.append(head, body);
        wrap.appendChild(table);
        content.appendChild(wrap);
        if (options.linkUrl) {
            const link = document.createElement("a");
            link.className = "btn btn-sm btn-outline-primary cnc-deducciones-history-link";
            link.href = options.linkUrl;
            link.target = "_blank";
            link.rel = "noopener noreferrer";
            link.textContent = options.linkLabel || "Abrir archivo en SharePoint";
            content.appendChild(link);
        }
        modal.hidden = false;
    };

    const createPendingSupplierRow = (supplier) => {
        const row = document.createElement("div");
        row.dataset.recordId = supplier?.representativeRecordId || "";
        row.dataset.supplierName = supplier?.supplierName || "";
        row.dataset.supplierNit = supplier?.supplierNit || supplier?.supplierKey || "";
        row.dataset.supplierPersonType = "Company";
        row.dataset.supplierIdType = "31";
        row.dataset.supplierCheckDigit = calculateColombianCheckDigit(row.dataset.supplierNit);
        return row;
    };

    const openDeduccionesHistoryDetail = (history) => {
        const documents = Array.isArray(history?.documents) ? history.documents : [];
        const skipped = Array.isArray(history?.skipped) ? history.skipped : [];
        const detailRows = [
            ...documents.map((row) => ({ ...row, historyRowKind: "document" })),
            ...skipped.map((row) => ({ ...row, historyRowKind: "skipped" }))
        ];
        const pendingRutSuppliers = Number(history?.pendingRutSuppliers || 0);
        const sentToSiigo = Number(history?.sentToSiigo || 0);
        const siigoRows = Number(history?.siigoRows || 0);
        const supportDocumentRows = Number(history?.supportDocumentRows || 0);
        const payrollRows = Number(history?.payrollRows || 0);
        const creditNotes = Number(history?.supplierCreditNotes || 0);
        const creditNotesApplied = Number(history?.supplierCreditNotesApplied || 0);
        openDeduccionesDetail(
            `Importación ${history?.periodLabel || ""}`.trim(),
            detailRows,
            [
                { label: "Tipo", value: (row) => row?.historyRowKind === "skipped" ? "Omitida" : row?.isPayroll ? "Nómina" : row?.isSupportDocument ? "Documento soporte" : row?.isSupplierCreditNote ? "Nota crédito proveedor" : "Factura de compra" },
                { label: "Documento", value: (row) => row?.invoiceNumber || [row?.prefix, row?.folio].filter(Boolean).join("-") },
                { label: "Proveedor", value: (row) => row?.supplierName || "" },
                { label: "NIT", value: (row) => row?.supplierNit || "" },
                { label: "Fecha", value: (row) => row?.emissionDateDisplay || row?.emissionDate || row?.receptionDate || "" },
                { label: "Total", value: (row) => moneyPrecise(row?.totalValue || 0) },
                { label: "Cuenta", value: (row) => row?.accountCode || "" },
                { label: "Documento Siigo", value: (row) => row?.siigoDocumentName || "" },
                { label: "Estado", value: (row) => row?.historyRowKind === "skipped" ? row?.reason || "Omitida" : row?.statusLabel || "" },
                { label: "Detalle", value: (row) => row?.detail || row?.reason || "" }
            ],
            {
                description: `${history?.importedAtDisplay || ""} · ${sentToSiigo} de ${siigoRows} documentos Siigo · ${supportDocumentRows} documento(s) soporte solo en Dataverse · ${payrollRows} nómina(s) solo en Dataverse · ${creditNotesApplied} de ${creditNotes} notas aplicadas · ${pendingRutSuppliers} proveedor(es) pendientes de RUT · ${skipped.length} fila(s) omitidas.`,
                emptyMessage: "Esta importación no tiene documentos vigentes para mostrar.",
                actions: [
                    {
                        label: "Subir RUT",
                        visible: (row) => Boolean(row?.historyRowKind !== "skipped" && row?.needsRut && row?.recordId),
                        action: (row) => openDianSupplierModal(createPendingSupplierRow({
                            representativeRecordId: row?.recordId || "",
                            supplierName: row?.supplierName || "",
                            supplierNit: row?.supplierNit || ""
                        }), { mode: "rut" })
                    },
                    {
                        label: "Subir manualmente",
                        className: "btn btn-sm btn-outline-primary",
                        visible: (row) => Boolean(row?.historyRowKind !== "skipped" && row?.needsRut && row?.recordId),
                        action: (row) => openDianSupplierModal(createPendingSupplierRow({
                            representativeRecordId: row?.recordId || "",
                            supplierName: row?.supplierName || "",
                            supplierNit: row?.supplierNit || ""
                        }), { mode: "manual" })
                    }
                ],
                linkUrl: history?.sharePointWebUrl || "",
                linkLabel: "Abrir archivo original en SharePoint"
            });
    };

    const renderDeduccionesMetric = (label, value, onClick = null) => {
        const item = document.createElement(onClick ? "button" : "div");
        item.className = `cnc-sync-metric${onClick ? " cnc-sync-metric--clickable" : ""}`;
        if (onClick) {
            item.type = "button";
            item.addEventListener("click", onClick);
        }
        const title = document.createElement("span");
        title.textContent = label;
        const number = document.createElement("strong");
        number.textContent = value;
        item.append(title, number);
        return item;
    };

    const renderDeduccionesResult = (payload) => {
        if (!deduccionesResult) {
            return;
        }

        const importResult = payload.import || {};
        const sharePoint = payload.sharePoint || {};
        lastDeduccionesPayload = payload;
        const siigoAutomation = importResult.siigoAutomation && typeof importResult.siigoAutomation === "object"
            ? importResult.siigoAutomation
            : null;
        const importedRows = Array.isArray(importResult.sampleRows) ? importResult.sampleRows : [];
        const upsertRows = Array.isArray(importResult.upsertRows) ? importResult.upsertRows : [];
        const skippedRows = Array.isArray(importResult.skipped) ? importResult.skipped : [];
        const automationRows = Array.isArray(siigoAutomation?.rows) ? siigoAutomation.rows : [];
        const pendingSuppliers = Array.isArray(siigoAutomation?.pendingSuppliers) ? siigoAutomation.pendingSuppliers : [];
        const statusEquals = (row, value) => String(row?.status || "").toLowerCase() === value.toLowerCase();
        const successfulAutomationStatuses = new Set(["created", "existinglinked", "alreadyimported", "recoveredafterambiguouserror"]);
        const nonFailureAutomationStatuses = new Set([
            ...successfulAutomationStatuses,
            "readydryrun",
            "existingpurchasewouldlink",
            "pendingsupplier",
            "pendingclassification",
            "concurrentprocessing",
            "ambiguouswritepending"
        ]);
        const createdSiigoRows = automationRows.filter((row) => statusEquals(row, "Created"));
        const existingLinkedRows = automationRows.filter((row) => statusEquals(row, "ExistingLinked"));
        const alreadyImportedRows = automationRows.filter((row) => statusEquals(row, "AlreadyImported"));
        const pendingSupplierRows = automationRows.filter((row) => statusEquals(row, "PendingSupplier"));
        const pendingClassificationRows = automationRows.filter((row) => statusEquals(row, "PendingClassification"));
        const ambiguousRows = automationRows.filter((row) => statusEquals(row, "AmbiguousWritePending"));
        const failedRows = automationRows.filter((row) =>
            !nonFailureAutomationStatuses.has(String(row?.status || "").toLowerCase()));
        const pendingSupplierNits = new Set(
            pendingSuppliers.map((supplier) => extractDigits(supplier?.supplierNit || supplier?.supplierKey || "")));
        const foundSupplierRows = Array.from(importedRows.reduce((index, row) => {
            const nit = extractDigits(row?.supplierNit || "");
            if (nit && !pendingSupplierNits.has(nit) && !index.has(nit)) {
                index.set(nit, row);
            }
            return index;
        }, new Map()).values());
        const importColumns = [
            { label: "Fila", value: (row) => row?.rowNumber || "" },
            { label: "Tipo", value: (row) => String(row?.documentKind || "").toLowerCase() === "nominaindividual" ? "Nómina (solo Dataverse)" : row?.documentType || "" },
            { label: "Factura", value: (row) => row?.invoiceNumber || [row?.prefix, row?.folio].filter(Boolean).join("-") },
            { label: "Proveedor", value: (row) => row?.supplierName || "" },
            { label: "NIT", value: (row) => row?.supplierNit || "" },
            { label: "Fecha", value: (row) => row?.emissionDate || "" },
            { label: "Total", value: (row) => moneyPrecise(row?.totalValue || 0) }
        ];
        const upsertColumns = [
            { label: "Fila", value: (row) => row?.rowNumber || "" },
            { label: "Factura", value: (row) => row?.invoiceNumber || "" },
            { label: "Proveedor", value: (row) => row?.supplierName || "" },
            { label: "NIT", value: (row) => row?.supplierNit || "" },
            { label: "Total", value: (row) => moneyPrecise(row?.totalValue || 0) },
            { label: "Resultado", value: (row) => row?.outcome || "" }
        ];
        const automationColumns = [
            { label: "Factura", value: (row) => row?.invoiceNumber || "" },
            { label: "Proveedor", value: (row) => row?.supplierName || "" },
            { label: "NIT", value: (row) => row?.supplierNit || row?.supplierKey || "" },
            { label: "Estado", value: (row) => row?.status || "" },
            { label: "Documento Siigo", value: (row) => row?.siigoName || row?.siigoId || "" },
            { label: "Detalle", value: (row) => row?.message || (Array.isArray(row?.issues) ? row.issues.join(" ") : "") }
        ];
        const automationCount = (value) => {
            if (Array.isArray(value)) {
                return value.length;
            }

            const parsed = Number(value || 0);
            return Number.isFinite(parsed) ? parsed : 0;
        };
        const automationStatus = String(siigoAutomation?.status || "").trim();
        const normalizedAutomationStatus = automationStatus.toLowerCase();
        const automationFailed = automationCount(siigoAutomation?.failed);
        const automationPendingSupplierInvoices = automationCount(siigoAutomation?.pendingSupplierInvoices);
        const automationPendingClassification = automationCount(siigoAutomation?.pendingClassification);
        const automationAmbiguousWritePending = automationCount(siigoAutomation?.ambiguousWritePending);
        const automationPendingSuppliers = automationCount(siigoAutomation?.pendingSuppliers);
        const automationStatusIsPending = normalizedAutomationStatus.includes("pending")
            || normalizedAutomationStatus.includes("pendiente")
            || normalizedAutomationStatus.includes("requires")
            || normalizedAutomationStatus.includes("requiere")
            || normalizedAutomationStatus.includes("blocked")
            || normalizedAutomationStatus.includes("bloqueado");
        const automationHasErrors = automationFailed > 0
            || normalizedAutomationStatus.includes("error")
            || normalizedAutomationStatus.includes("fail");
        const automationHasPendingSuppliers = automationPendingSupplierInvoices > 0
            || automationPendingSuppliers > 0
            || (automationStatusIsPending
                && (normalizedAutomationStatus.includes("supplier")
                    || normalizedAutomationStatus.includes("proveedor")));
        const automationHasPendingClassification = automationPendingClassification > 0
            || (automationStatusIsPending
                && (normalizedAutomationStatus.includes("classification")
                    || normalizedAutomationStatus.includes("clasificacion")));
        const automationHasAmbiguousWrite = automationAmbiguousWritePending > 0;
        const automationCompleted = (Boolean(siigoAutomation?.completed)
            || normalizedAutomationStatus.includes("complete")
            || normalizedAutomationStatus.includes("completado"))
            && !automationHasErrors
            && !automationHasPendingSuppliers
            && !automationHasPendingClassification
            && !automationHasAmbiguousWrite;
        const automationStatusLabel = automationCompleted
            ? "Job completado"
            : automationHasErrors
                ? "Job con errores"
                : automationHasPendingSuppliers
                    ? "Pendiente de proveedores"
                    : automationHasPendingClassification
                        ? "Pendiente de clasificacion"
                        : automationHasAmbiguousWrite
                            ? "Pendiente de confirmar en Siigo"
                        : automationStatus
                            ? automationStatus.replace(/([a-z])([A-Z])/g, "$1 $2").replace(/[_-]+/g, " ")
                            : "Procesando Siigo";
        const automationTone = automationCompleted ? "success" : "warning";
        deduccionesResult.innerHTML = "";

        const summary = document.createElement("article");
        summary.className = siigoAutomation
            ? `cnc-sync-card cnc-sync-card--${automationTone}`
            : "cnc-sync-card cnc-sync-card--success";
        const header = document.createElement("header");
        const heading = document.createElement("div");
        const title = document.createElement("h3");
        title.textContent = importResult.sourceFileName || sharePoint.storedFileName || "Archivo DIAN";
        const detail = document.createElement("p");
        const sharePointDetail = sharePoint.folderPath ? `SharePoint: ${sharePoint.folderPath}` : "SharePoint archivado";
        const periodDetail = importResult.periodLabel ? ` Periodos detectados: ${importResult.periodLabel}.` : "";
        detail.textContent = siigoAutomation
            ? `${sharePointDetail}.${periodDetail} Estado Siigo: ${automationStatusLabel}.`
            : `${sharePointDetail}.${periodDetail}`;
        heading.append(title, detail);
        const badge = document.createElement("span");
        badge.className = `cnc-badge cnc-badge--${siigoAutomation ? automationTone : "success"}`;
        badge.textContent = importResult.dryRun
            ? "Simulacion"
            : siigoAutomation
                ? automationStatusLabel
                : "Importado";
        header.append(heading, badge);

        const metrics = document.createElement("div");
        metrics.className = "cnc-sync-metrics";
        metrics.append(
            renderDeduccionesMetric(
                "Filas importables",
                numberLabel(importResult.importableRows || 0),
                () => openDeduccionesDetail("Filas importables", importedRows, importColumns)),
            renderDeduccionesMetric(
                "Creadas",
                numberLabel(importResult.created || 0),
                () => openDeduccionesDetail(
                    "Registros creados en Dataverse",
                    upsertRows.filter((row) => String(row?.outcome || "").toLowerCase() === "created"),
                    upsertColumns)),
            renderDeduccionesMetric(
                "Actualizadas",
                numberLabel(importResult.updated || 0),
                () => openDeduccionesDetail(
                    "Registros actualizados en Dataverse",
                    upsertRows.filter((row) => String(row?.outcome || "").toLowerCase() === "updated"),
                    upsertColumns)),
            renderDeduccionesMetric(
                "IVA",
                moneyPrecise(importResult.vatValue || 0),
                () => openDeduccionesDetail(
                    "IVA de las filas importables",
                    importedRows.filter((row) => Number(row?.vatValue || 0) !== 0),
                    importColumns))
        );

        const secondary = document.createElement("div");
        secondary.className = "cnc-sync-metrics";
        secondary.append(
            renderDeduccionesMetric(
                "Sin cambios",
                numberLabel(importResult.unchanged || 0),
                () => openDeduccionesDetail(
                    "Registros sin cambios",
                    upsertRows.filter((row) => String(row?.outcome || "").toLowerCase() === "unchanged"),
                    upsertColumns)),
            renderDeduccionesMetric(
                "Omitidas",
                numberLabel(importResult.skippedRows || 0),
                () => openDeduccionesDetail(
                    "Filas omitidas",
                    skippedRows,
                    [
                        { label: "Fila", value: (row) => row?.rowNumber || "" },
                        { label: "Tipo", value: (row) => row?.documentType || "" },
                        { label: "Grupo", value: (row) => row?.group || "" },
                        { label: "Factura", value: (row) => [row?.prefix, row?.folio].filter(Boolean).join("-") },
                        { label: "Motivo", value: (row) => row?.reason || "" }
                    ])),
            renderDeduccionesMetric(
                "Proveedores encontrados",
                numberLabel(importResult.supplierLookupFound || 0),
                () => openDeduccionesDetail("Proveedores encontrados en Siigo", foundSupplierRows, importColumns)),
            renderDeduccionesMetric(
                "Nóminas solo Dataverse",
                numberLabel(importResult.payrollRows || 0),
                () => openDeduccionesDetail(
                    "Nóminas guardadas únicamente en Dataverse",
                    importedRows.filter((row) => String(row?.documentKind || "").toLowerCase() === "nominaindividual"),
                    importColumns)),
            renderDeduccionesMetric(
                "Documentos soporte solo Dataverse",
                numberLabel(importResult.supportDocumentRows || 0),
                () => openDeduccionesDetail(
                    "Documentos soporte guardados únicamente en Dataverse",
                    importedRows.filter((row) => String(row?.documentKind || "").toLowerCase() === "documentosoporte"),
                    importColumns)),
            renderDeduccionesMetric(
                "Total",
                moneyPrecise(importResult.totalValue || 0),
                () => openDeduccionesDetail("Totales importables", importedRows, importColumns))
        );

        summary.append(header, metrics, secondary);
        if (siigoAutomation) {
            const automationPrimary = document.createElement("div");
            automationPrimary.className = "cnc-sync-metrics";
            automationPrimary.append(
                renderDeduccionesMetric(
                    "Elegibles Siigo",
                    numberLabel(automationCount(siigoAutomation.eligible)),
                    () => openDeduccionesDetail("Facturas elegibles para Siigo", automationRows, automationColumns)),
                renderDeduccionesMetric(
                    "Creadas Siigo",
                    numberLabel(automationCount(siigoAutomation.created)),
                    () => openDeduccionesDetail("Facturas de compra creadas en Siigo", createdSiigoRows, automationColumns)),
                renderDeduccionesMetric(
                    "Compras existentes asociadas",
                    numberLabel(automationCount(siigoAutomation.existingLinked)),
                    () => openDeduccionesDetail("Compras Siigo existentes y asociadas", existingLinkedRows, automationColumns)),
                renderDeduccionesMetric(
                    "Ya importadas",
                    numberLabel(automationCount(siigoAutomation.alreadyImported)),
                    () => openDeduccionesDetail("Facturas ya importadas", alreadyImportedRows, automationColumns))
            );

            const automationPending = document.createElement("div");
            automationPending.className = "cnc-sync-metrics";
            automationPending.append(
                renderDeduccionesMetric(
                    "Facturas sin proveedor",
                    numberLabel(automationPendingSupplierInvoices),
                    () => openDeduccionesDetail("Facturas sin proveedor Siigo", pendingSupplierRows, automationColumns)),
                renderDeduccionesMetric(
                    "Pendientes clasificacion",
                    numberLabel(automationPendingClassification),
                    () => openDeduccionesDetail("Facturas pendientes de cuenta", pendingClassificationRows, automationColumns)),
                renderDeduccionesMetric(
                    "Confirmacion Siigo",
                    numberLabel(automationAmbiguousWritePending),
                    () => openDeduccionesDetail("Operaciones pendientes de confirmar", ambiguousRows, automationColumns)),
                renderDeduccionesMetric(
                    "Fallidas Siigo",
                    numberLabel(automationFailed),
                    () => openDeduccionesDetail("Errores de carga a Siigo", failedRows, automationColumns)),
                renderDeduccionesMetric(
                    "Proveedores pendientes",
                    numberLabel(automationPendingSuppliers),
                    () => openDeduccionesDetail(
                        "Proveedores pendientes de crear en Siigo",
                        pendingSuppliers,
                        [
                            { label: "Proveedor", value: (row) => row?.supplierName || "" },
                            { label: "NIT", value: (row) => row?.supplierNit || row?.supplierKey || "" },
                            { label: "Facturas", value: (row) => numberLabel(row?.pendingInvoiceCount || 0) },
                            { label: "Total", value: (row) => moneyPrecise(row?.totalValue || 0) }
                        ],
                        {
                            description: "Adjunta el RUT de cada proveedor. La IA extraerá los datos, se creará el tercero y se reintentará la carga de sus facturas.",
                            actions: [
                                {
                                    label: "Subir RUT",
                                    action: (supplier) => openDianSupplierModal(
                                        createPendingSupplierRow(supplier),
                                        { mode: "rut" })
                                },
                                {
                                    label: "Subir manualmente",
                                    className: "btn btn-sm btn-outline-primary",
                                    action: (supplier) => openDianSupplierModal(
                                        createPendingSupplierRow(supplier),
                                        { mode: "manual" })
                                }
                            ]
                        }))
            );
            summary.append(automationPrimary, automationPending);
        }
        if (sharePoint.webUrl) {
            const link = document.createElement("a");
            link.className = "btn btn-sm btn-outline-primary";
            link.href = sharePoint.webUrl;
            link.target = "_blank";
            link.rel = "noopener noreferrer";
            link.textContent = "Abrir en SharePoint";
            summary.appendChild(link);
        }
        if (siigoAutomation && !automationCompleted && dianRetryPurchasesUrl) {
            const retry = document.createElement("button");
            retry.type = "button";
            retry.className = "btn btn-sm btn-primary";
            retry.textContent = "Reintentar pendientes en Siigo";
            retry.addEventListener("click", async () => {
                retry.disabled = true;
                const previousText = retry.textContent;
                retry.textContent = "Procesando...";
                setStatus("Clasificando y reintentando las facturas pendientes en Siigo...", "info");
                try {
                    const externalKeys = importedRows
                        .map((row) => String(row?.externalKey || "").trim())
                        .filter(Boolean);
                    const response = await fetch(dianRetryPurchasesUrl, {
                        method: "POST",
                        headers: { "Content-Type": "application/json", "Accept": "application/json" },
                        body: JSON.stringify({
                            periods: Array.isArray(importResult.siigoPeriods) ? importResult.siigoPeriods : [],
                            externalKeys
                        })
                    });
                    const result = await response.json().catch(() => ({}));
                    if (!response.ok) {
                        throw new Error(result.detail || result.message || "No fue posible reintentar las facturas.");
                    }

                    const basePayload = lastDeduccionesPayload || payload;
                    renderDeduccionesResult({
                        ...basePayload,
                        import: {
                            ...(basePayload.import || {}),
                            siigoAutomation: result.automation || {}
                        }
                    });
                    const failed = Number(result.automation?.failed || 0);
                    const pending = Number(result.automation?.pendingSupplierInvoices || 0)
                        + Number(result.automation?.pendingClassification || 0)
                        + Number(result.automation?.ambiguousWritePending || 0);
                    setStatus(
                        result.message || "Reintento finalizado.",
                        failed > 0 ? "error" : pending > 0 ? "warning" : "success");
                } catch (error) {
                    retry.disabled = false;
                    retry.textContent = previousText;
                    setStatus(error instanceof Error ? error.message : "Ocurrio un error inesperado.", "error");
                }
            });
            summary.appendChild(retry);
        }

        deduccionesResult.appendChild(summary);
    };

    const importDeduccionesIva = async (event) => {
        event.preventDefault();
        if (!deduccionesIvaImportUrl) {
            setStatus("No se encontro la ruta para importar deducciones IVA.", "error");
            return;
        }

        const file = deduccionesFile?.files?.[0];
        if (!file) {
            setStatus("Adjunta el ZIP o Excel DIAN antes de importar.", "info");
            return;
        }

        const extension = file.name.split(".").pop()?.toLowerCase() || "";
        if (!["zip", "xlsx", "xlsm"].includes(extension)) {
            setStatus("El archivo debe ser .zip, .xlsx o .xlsm.", "warning");
            return;
        }

        const previousText = deduccionesSubmit?.textContent || "";
        if (deduccionesSubmit) {
            deduccionesSubmit.disabled = true;
            deduccionesSubmit.textContent = "Importando...";
        }
        setStatus("Guardando archivo en SharePoint e importando a Dataverse...", "info");
        setDeduccionesResultLoading("Procesando archivo DIAN.");

        try {
            const formData = new FormData();
            formData.append("file", file);
            const response = await fetch(deduccionesIvaImportUrl, {
                method: "POST",
                headers: { "Accept": "application/json" },
                body: formData
            });
            const payload = await response.json().catch(() => ({}));
            if (!response.ok) {
                throw new Error(payload.detail || payload.message || "No fue posible importar deducciones IVA.");
            }

            renderDeduccionesResult(payload);
            const siigoAutomation = payload.import?.siigoAutomation;
            const statusTone = Number(siigoAutomation?.failed || 0) > 0
                ? "error"
                : siigoAutomation && !siigoAutomation.completed
                    ? "warning"
                    : "success";
            setStatus(payload.message || "Deducciones IVA importadas.", statusTone);
            syncLoaded = false;
            if (Number(siigoAutomation?.pendingSupplierInvoices || 0) > 0
                || Number(siigoAutomation?.pendingClassification || 0) > 0
                || Number(siigoAutomation?.ambiguousWritePending || 0) > 0
                || Number(siigoAutomation?.failed || 0) > 0) {
                setStatus(
                    `${payload.message || "Importacion finalizada."} Haz clic en las tarjetas para revisar el detalle y resolver pendientes.`,
                    statusTone);
            }
            if (payload.historyRecorded) {
                window.setTimeout(reloadPreservingView, 1800);
            }
        } catch (error) {
            setDeduccionesResultLoading(error instanceof Error ? error.message : "Ocurrio un error inesperado.");
            setStatus(error instanceof Error ? error.message : "Ocurrio un error inesperado.", "error");
        } finally {
            if (deduccionesSubmit) {
                deduccionesSubmit.disabled = false;
                deduccionesSubmit.textContent = previousText;
            }
        }
    };

    const setBankImportResult = (message, tone = "neutral") => {
        if (!bankImportResult) {
            return;
        }

        bankImportResult.textContent = message || "";
        bankImportResult.dataset.tone = tone;
    };

    const buildBankImportSummary = (payload) => {
        const created = Number(payload?.created || 0);
        const updated = Number(payload?.updated || 0);
        const unchanged = Number(payload?.unchanged || 0);
        const skipped = Number(payload?.skipped || 0);
        return `Creadas ${numberLabel(created)} · Actualizadas ${numberLabel(updated)} · Ya existentes ${numberLabel(unchanged)} · Omitidas ${numberLabel(skipped)}`;
    };

    const importBancolombiaStatement = async (event) => {
        event.preventDefault();
        if (!cashFlowStatementImportUrl) {
            setStatus("No se encontro la ruta para importar Bancolombia.", "error");
            return;
        }

        const form = event.currentTarget;
        const fileInput = form.querySelector("[data-cnc-bank-import-file]");
        const account = form.querySelector("[data-cnc-bank-import-account]")?.value || "";
        const submit = form.querySelector("[data-cnc-bank-import-submit]");
        const file = fileInput?.files?.[0];
        if (!file) {
            setStatus("Selecciona el Excel de Bancolombia antes de importar.", "info");
            return;
        }

        const extension = file.name.split(".").pop()?.toLowerCase() || "";
        if (!["xlsx", "xlsm", "xls"].includes(extension)) {
            setStatus("El extracto debe ser un archivo Excel.", "warning");
            return;
        }

        const previousText = submit?.textContent || "";
        if (submit) {
            submit.disabled = true;
            submit.textContent = "Importando...";
        }
        if (fileInput) {
            fileInput.disabled = true;
        }
        setBankImportResult(`Procesando ${file.name}`, "info");
        setStatus("Importando extracto Bancolombia a Dataverse...", "info");

        try {
            const formData = new FormData();
            formData.append("file", file);
            formData.append("accountKey", account);
            formData.append("year", String(periodYear));
            formData.append("month", String(periodMonth));
            formData.append("dryRun", "false");
            const response = await fetch(cashFlowStatementImportUrl, {
                method: "POST",
                headers: { "Accept": "application/json" },
                body: formData
            });
            const payload = await response.json().catch(() => ({}));
            if (!response.ok) {
                throw new Error(payload.detail || payload.message || "No fue posible importar el extracto Bancolombia.");
            }

            const summary = buildBankImportSummary(payload);
            setBankImportResult(summary, "success");
            selectBankBalanceForSource(account);
            try {
                await refreshBankBalances();
            } catch {
                // La recarga inmediata siguiente vuelve a consultar los valores persistidos.
            }
            setStatus(`${summary}. Saldo actualizado; recargando bandeja...`, "success");
            window.setTimeout(reloadPreservingView, 900);
        } catch (error) {
            const message = error instanceof Error ? error.message : "Ocurrio un error inesperado.";
            setBankImportResult(message, "error");
            setStatus(message, "error");
        } finally {
            if (submit) {
                submit.disabled = false;
                submit.textContent = previousText;
            }
            if (fileInput) {
                fileInput.disabled = false;
            }
        }
    };

    const updateRowStatus = (row, payloadRow, fallbackStatus) => {
        const nextStatus = payloadRow?.status || fallbackStatus;
        row.dataset.status = nextStatus;
        const badge = row.querySelector("[data-status-label]");
        if (badge) {
            badge.textContent = payloadRow?.statusLabel || statusLabel(nextStatus);
            badge.className = `cnc-badge cnc-badge--${payloadRow?.statusTone || statusTone(nextStatus)}`;
        }

        if (payloadRow) {
            const preflightBadge = row.querySelector("[data-preflight-label]");
            if (preflightBadge) {
                preflightBadge.textContent = payloadRow.preflightStatusLabel || "Sin validar";
                preflightBadge.className = `cnc-badge cnc-badge--${payloadRow.preflightStatusTone || "neutral"}`;
            }

            const totals = row.querySelector("[data-preflight-totals]");
            if (totals) {
                const debit = Number(payloadRow.preflightDebitTotal || 0);
                const credit = Number(payloadRow.preflightCreditTotal || 0);
                totals.textContent = debit || credit
                    ? `Db ${money(debit)} / Cr ${money(credit)}`
                    : (payloadRow.preflightValidatedOnDisplay || "Sin log");
            }

            const detail = getDetailRow(row.dataset.recordId || "");
            const message = row.querySelector("[data-preflight-message]") || detail?.querySelector("[data-preflight-message]");
            if (message) {
                message.textContent = payloadRow.preflightMessage || "Sin validacion pre-Siigo.";
            }

            const sendButton = row.querySelector("[data-cnc-send-siigo]");
            if (sendButton) {
                sendButton.disabled = !canSendPaymentStatus(nextStatus);
            }
        }
    };

    const actionReason = (action) => {
        if (action === "Aprobado") {
            return "Aprobado desde modulo Conciliacion.";
        }

        const label = action === "Rechazado"
            ? "Motivo del rechazo"
            : "Nota de revision";
        return window.prompt(label, "") || "";
    };

    const updatePaymentStatus = async (button, options = {}) => {
        const row = button.closest("tr[data-record-id]");
        if (!row) {
            return { success: false, message: "No se encontro la fila." };
        }

        const action = button.dataset.cncAction || "";
        const recordId = row.dataset.recordId || "";
        if (!recordId || !action || !updatePaymentUrl) {
            setStatus("No se encontro la ruta o el registro para actualizar.", "error");
            return { success: false, message: "No se encontro la ruta o el registro para actualizar." };
        }

        const reason = options.reason ?? actionReason(action);
        if ((action === "Rechazado" || action === "RevisionManual") && !reason.trim()) {
            return { success: false, message: "Accion cancelada." };
        }

        const buttons = Array.from(row.querySelectorAll("[data-cnc-action]"));
        buttons.forEach((item) => { item.disabled = true; });
        setStatus("Actualizando cruce en Dataverse...", "info");

        try {
            const response = await fetch(updatePaymentUrl, {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify({ recordId, status: action, reason })
            });
            const payload = await response.json().catch(() => ({}));
            if (!response.ok) {
                throw new Error(payload.detail || payload.message || "No fue posible actualizar el cruce.");
            }

            updateRowStatus(row, payload.row, action);
            applyPaymentFilters();
            setStatus(payload.message || "Cruce actualizado.", "success");
            if (action === "Aprobado" && !options.suppressReload) {
                window.setTimeout(reloadPreservingView, 550);
            }
            return { success: true, message: payload.message || "Cruce actualizado.", payload };
        } catch (error) {
            const message = error instanceof Error ? error.message : "Ocurrio un error inesperado.";
            setStatus(message, "error");
            return { success: false, message };
        } finally {
            buttons.forEach((item) => { item.disabled = false; });
            const sendButton = row.querySelector("[data-cnc-send-siigo]");
            if (sendButton) {
                sendButton.disabled = !canSendPaymentStatus(row.dataset.status || "");
            }
        }
    };

    const markPaymentManualSiigo = async (button, options = {}) => {
        const row = button.closest("tr[data-record-id]");
        if (!row) {
            return { success: false, message: "No se encontro la fila." };
        }

        const recordId = row.dataset.recordId || "";
        if (!recordId || !manualPaymentUrl) {
            setStatus("No se encontro la ruta o el cruce para marcar el registro manual.", "error");
            return { success: false, message: "No se encontro la ruta o el cruce para marcar el registro manual." };
        }

        const confirmed = options.skipConfirm
            ? true
            : window.confirm("Esto marcara el pago como registrado manualmente en Siigo y lo movera a Enviados / errores Siigo. Continuar?");
        if (!confirmed) {
            return { success: false, message: "Accion cancelada." };
        }

        const reason = options.reason || "Registrada manualmente en Siigo desde Conciliacion. No se envio payload desde la app.";
        const buttons = Array.from(row.querySelectorAll("[data-cnc-action], [data-cnc-manual-siigo], [data-cnc-preflight], [data-cnc-send-siigo]"));
        buttons.forEach((item) => { item.disabled = true; });
        setStatus("Marcando registro manual en Siigo...", "info");

        try {
            const response = await fetch(manualPaymentUrl, {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify({ recordId, reason })
            });
            const payload = await response.json().catch(() => ({}));
            if (!response.ok) {
                throw new Error(payload.detail || payload.message || "No fue posible marcar el registro manual.");
            }

            updateRowStatus(row, payload.row, payload.row?.status || "EnviadoSiigo");
            applyPaymentFilters();
            setStatus(payload.message || "Registro marcado manualmente en Siigo.", "success");
            if (!options.suppressReload) {
                window.setTimeout(reloadPreservingView, 650);
            }
            return { success: true, message: payload.message || "Registro marcado manualmente en Siigo.", payload };
        } catch (error) {
            const message = error instanceof Error ? error.message : "Ocurrio un error inesperado.";
            setStatus(message, "error");
            return { success: false, message };
        } finally {
            buttons.forEach((item) => { item.disabled = false; });
        }
    };

    const validatePaymentPreflight = async (button, options = {}) => {
        const row = button.closest("tr[data-record-id]");
        if (!row) {
            return { success: false, message: "No se encontro la fila." };
        }

        const recordId = row.dataset.recordId || "";
        if (!recordId || !preflightPaymentUrl) {
            setStatus("No se encontro la ruta o el registro para validar.", "error");
            return { success: false, message: "No se encontro la ruta o el registro para validar." };
        }

        const buttons = Array.from(row.querySelectorAll("[data-cnc-action], [data-cnc-preflight], [data-cnc-dry-run], [data-cnc-send-siigo]"));
        buttons.forEach((item) => { item.disabled = true; });
        setStatus("Validando borrador pre-Siigo...", "info");

        try {
            const response = await fetch(preflightPaymentUrl, {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify({ recordId })
            });
            const payload = await response.json().catch(() => ({}));
            if (!response.ok) {
                throw new Error(payload.detail || payload.message || "No fue posible validar el borrador.");
            }

            updateRowStatus(row, payload.row, payload.row?.status || row.dataset.status || "");
            renderIssueList(row, "[data-preflight-issues]", payload.issues || []);
            applyPaymentFilters();
            setStatus(
                payload.isReadyForSiigo
                    ? (payload.message || "Validacion pre-Siigo finalizada.")
                    : `${payload.message || "Validacion pre-Siigo finalizada."} Revisa los pendientes visibles en la fila.`,
                payload.isReadyForSiigo ? "success" : "info");
            return {
                success: Boolean(payload.isReadyForSiigo),
                message: payload.message || "Validacion pre-Siigo finalizada.",
                payload
            };
        } catch (error) {
            const message = error instanceof Error ? error.message : "Ocurrio un error inesperado.";
            setStatus(message, "error");
            return { success: false, message };
        } finally {
            buttons.forEach((item) => { item.disabled = false; });
            const sendButton = row.querySelector("[data-cnc-send-siigo]");
            if (sendButton) {
                sendButton.disabled = !canSendPaymentStatus(row.dataset.status || "");
            }
        }
    };

    const simulatePaymentSiigoDryRun = async (button) => {
        const row = button.closest("tr[data-record-id]");
        if (!row) {
            return { success: false, message: "No se encontro la fila." };
        }

        const recordId = row.dataset.recordId || "";
        if (!recordId || !dryRunPaymentUrl) {
            setStatus("No se encontro la ruta o el registro para simular.", "error");
            return { success: false, message: "No se encontro la ruta o el registro para simular." };
        }

        const buttons = Array.from(row.querySelectorAll("[data-cnc-action], [data-cnc-preflight], [data-cnc-dry-run], [data-cnc-send-siigo]"));
        buttons.forEach((item) => { item.disabled = true; });
        setStatus("Simulando payload de envio a Siigo...", "info");

        try {
            const response = await fetch(dryRunPaymentUrl, {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify({ recordId })
            });
            const payload = await response.json().catch(() => ({}));
            if (!response.ok) {
                throw new Error(payload.detail || payload.message || "No fue posible simular el envio.");
            }

            const message = row.querySelector("[data-siigo-dryrun-message]");
            const preview = row.querySelector("[data-siigo-dryrun-preview]");
            const payloadBox = row.querySelector("[data-siigo-dryrun-payload]");
            const issues = Array.isArray(payload.issues) ? payload.issues : [];

            if (message) {
                message.textContent = issues.length
                    ? `${payload.message || "Simulacion finalizada."} Pendientes abajo.`
                    : (payload.message || "Simulacion finalizada.");
                message.className = payload.isReadyForSiigo ? "cnc-tone-success" : "cnc-tone-warning";
            }
            renderIssueList(row, "[data-siigo-dryrun-issues]", issues);
            if (payloadBox) {
                payloadBox.textContent = payload.payloadJson || "";
            }
            if (preview) {
                preview.hidden = !payload.payloadJson;
            }

            setStatus(payload.message || "Simulacion finalizada.", payload.isReadyForSiigo ? "success" : "info");
            return {
                success: Boolean(payload.isReadyForSiigo) && issues.length === 0,
                message: payload.message || "Simulacion finalizada.",
                payload
            };
        } catch (error) {
            const message = error instanceof Error ? error.message : "Ocurrio un error inesperado.";
            setStatus(message, "error");
            return { success: false, message };
        } finally {
            buttons.forEach((item) => { item.disabled = false; });
        }
    };

    const openPaymentSendPreview = (button) => openSiigoPreviewModal(button, {
        title: "Simulacion de comprobante contable",
        kicker: "Registro de entradas FV",
        kind: "Comprobante contable",
        documentLabel: "Comprobante de ingreso",
        loadingMessage: "Preparando el comprobante que se enviara a Siigo...",
        sendLabel: "Enviar comprobante a Siigo",
        sendingLabel: "Enviando comprobante...",
        preview: (trigger) => simulatePaymentSiigoDryRun(trigger),
        send: (trigger) => sendPaymentToSiigo(trigger, { skipConfirm: true })
    });

    const openDianInvoiceSendPreview = (button) => openSiigoPreviewModal(button, {
        title: "Simulacion de factura proveedor",
        kicker: "Documento proveedor",
        kind: "Factura proveedor",
        documentLabel: "FC proveedor",
        loadingMessage: "Preparando la factura de compra que se enviara a Siigo...",
        sendLabel: "Enviar factura a Siigo",
        sendingLabel: "Enviando factura...",
        preview: (trigger) => runDianAction(trigger, dianDryRunUrl, {
            loadingMessage: "Simulando factura de compra Siigo...",
            successMessage: "Simulacion finalizada.",
            errorMessage: "No fue posible simular la factura.",
            reloadOnSuccess: false
        }),
        send: (trigger) => runDianAction(trigger, dianSendUrl, {
            loadingMessage: "Enviando factura de compra real a Siigo...",
            successMessage: "Factura enviada a Siigo.",
            errorMessage: "No fue posible enviar la factura a Siigo.",
            skipConfirm: true,
            reloadOnSuccess: true
        })
    });

    const openCuentaCobroSendPreview = (button) => openSiigoPreviewModal(button, {
        title: "Simulacion de documento soporte",
        kicker: "Cuenta de cobro",
        kind: "Documento soporte",
        documentLabel: "DS + pago",
        loadingMessage: "Preparando el documento soporte que se enviara a Siigo...",
        sendLabel: "Enviar a Siigo",
        sendingLabel: "Enviando a Siigo...",
        preview: (trigger) => runCuentaCobroAction(trigger, cuentaCobroPreflightUrl, {
            loadingMessage: "Validando documento soporte pre-Siigo...",
            successMessage: "Prevalidacion finalizada.",
            errorMessage: "No fue posible validar el documento soporte.",
            reloadOnSuccess: false
        }),
        send: (trigger) => sendCuentaCobroToSiigo(trigger, {
            skipConfirm: true,
            reloadOnSuccess: true
        })
    });

    const sendPaymentToSiigo = async (button, options = {}) => {
        const row = button.closest("tr[data-record-id]");
        if (!row) {
            return { success: false, message: "No se encontro la fila." };
        }

        const recordId = row.dataset.recordId || "";
        if (!recordId || !sendPaymentUrl) {
            setStatus("No se encontro la ruta o el registro para enviar a Siigo.", "error");
            return { success: false, message: "No se encontro la ruta o el registro para enviar a Siigo." };
        }

        if (!canSendPaymentStatus(row.dataset.status || "")) {
            setStatus("El cruce debe estar Listo Siigo o Error Siigo antes del envio real.", "info");
            return { success: false, message: "El cruce debe estar Listo Siigo o Error Siigo antes del envio real." };
        }

        const confirmed = options.skipConfirm
            ? true
            : window.confirm("Esto creara un comprobante de ingreso real en Siigo. Revisa que la fila sea la correcta antes de continuar.");
        if (!confirmed) {
            return { success: false, message: "Envio cancelado." };
        }

        const buttons = Array.from(row.querySelectorAll("[data-cnc-action], [data-cnc-preflight], [data-cnc-dry-run], [data-cnc-send-siigo]"));
        buttons.forEach((item) => { item.disabled = true; });
        setStatus("Enviando pago real a Siigo...", "info");

        try {
            const response = await fetch(sendPaymentUrl, {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify({ recordId })
            });
            const payload = await response.json().catch(() => ({}));
            if (!response.ok) {
                throw new Error(payload.detail || payload.message || "No fue posible enviar a Siigo.");
            }

            updateRowStatus(row, payload.row, payload.row?.status || row.dataset.status || "");
            renderIssueList(row, "[data-siigo-send-issues]", payload.issues || []);

            const message = row.querySelector("[data-siigo-send-message]");
            const payloadPreview = row.querySelector("[data-siigo-send-payload-preview]");
            const payloadBox = row.querySelector("[data-siigo-send-payload]");
            const preview = row.querySelector("[data-siigo-send-preview]");
            const responseBox = row.querySelector("[data-siigo-send-response]");
            if (message) {
                message.textContent = payload.message || "Envio finalizado.";
                message.className = payload.isSuccess ? "cnc-tone-success" : "cnc-tone-warning";
            }
            if (payloadBox) {
                payloadBox.textContent = payload.payloadJson || "";
            }
            if (payloadPreview) {
                payloadPreview.hidden = !payload.payloadJson;
            }
            if (responseBox) {
                responseBox.textContent = payload.responseJson || "";
            }
            if (preview) {
                preview.hidden = !payload.responseJson;
            }

            setStatus(payload.message || "Envio finalizado.", payload.isSuccess ? "success" : "info");
            if (payload.isSuccess && !options.suppressReload) {
                window.setTimeout(reloadPreservingView, 900);
            } else {
                buttons.forEach((item) => { item.disabled = false; });
                button.disabled = !canSendPaymentStatus(row.dataset.status || "");
            }
            return {
                success: Boolean(payload.isSuccess),
                message: payload.message || "Envio finalizado.",
                payload
            };
        } catch (error) {
            const message = error instanceof Error ? error.message : "Ocurrio un error inesperado.";
            setStatus(message, "error");
            buttons.forEach((item) => { item.disabled = false; });
            button.disabled = !canSendPaymentStatus(row.dataset.status || "");
            return { success: false, message };
        }
    };

    const closeReassignModal = () => {
        if (reassignModal) {
            reassignModal.hidden = true;
        }
        activeReassignRow = null;
        bulkReassignRows = [];
    };

    const populateReassignOptions = (direction, currentType) => {
        const isExcludedFromReconciliation = String(currentType || "").trim().toLowerCase() === "no-incluida-conciliacion";
        const options = isExcludedFromReconciliation
            ? (excludedReconciliationOptionsByDirection[direction] || categoryOptions.NoIncluida)
            : categoryOptions[direction] || categoryOptions.Salida;

        if (reassignCategory) {
            reassignCategory.innerHTML = "";
            options.forEach((option) => {
                const item = document.createElement("option");
                item.value = option.value;
                item.textContent = option.label;
                item.selected = !isExcludedFromReconciliation && option.value === currentType;
                reassignCategory.appendChild(item);
            });

            if (isExcludedFromReconciliation && options.length > 0) {
                reassignCategory.value = options[0].value;
            }
        }
    };

    const openReassignModal = (row) => {
        activeReassignRow = row;
        bulkReassignRows = [];
        const direction = row.dataset.direction || "";

        if (reassignDescription) {
            reassignDescription.textContent = row.dataset.description || "Registro sin descripcion.";
        }

        populateReassignOptions(direction, row.dataset.currentType || "");

        if (reassignModal) {
            reassignModal.hidden = false;
        }
    };

    const openBulkCategoryModal = (section, rows) => {
        const selectedRows = rows.filter((row) => row.dataset.direction);
        if (selectedRows.length === 0) {
            setStatus("Selecciona registros con categoria modificable.", "info");
            return;
        }

        const directions = Array.from(new Set(selectedRows.map((row) => row.dataset.direction || "")));
        if (directions.length > 1) {
            setStatus("Selecciona registros de un solo tipo: entradas o salidas.", "info");
            return;
        }

        bulkReassignRows = selectedRows;
        activeReassignRow = selectedRows[0] || null;
        if (reassignDescription) {
            reassignDescription.textContent = `${selectedRows.length} registros seleccionados. Al aplicar, se guardara la categoria en Dataverse y dejaran de aparecer en esta tabla si ya no corresponden a entradas FE.`;
        }
        populateReassignOptions(directions[0], "");

        if (reassignModal) {
            reassignModal.hidden = false;
        }
    };

    const applyReassignCategory = async () => {
        const rowsToUpdate = bulkReassignRows.length > 0
            ? bulkReassignRows
            : (activeReassignRow ? [activeReassignRow] : []);
        if (rowsToUpdate.length === 0 || !reassignCategory) {
            return;
        }

        const nextValue = reassignCategory.value;
        const nextLabel = reassignCategory.options[reassignCategory.selectedIndex]?.textContent || categoryLabel(nextValue);
        const nextTone = categoryTone(nextValue);
        if (!cashFlowCategoryUrl) {
            setStatus("No se encontro la ruta o la fila del flujo de caja para guardar categoria.", "error");
            return;
        }

        if (reassignApply) {
            reassignApply.disabled = true;
        }
        const modal = ensureBulkProgressModal();
        const list = modal.querySelector("[data-cnc-bulk-list]");
        const reload = modal.querySelector("[data-cnc-bulk-reload]");
        const closeButtons = modal.querySelectorAll("[data-cnc-bulk-close]");
        if (list) {
            list.innerHTML = "";
        }
        if (reload) {
            reload.hidden = true;
        }
        closeButtons.forEach((button) => { button.disabled = true; });
        const progressItems = rowsToUpdate.map((row, index) => createBulkProgressItem(modal, row, index));
        setBulkProgress(modal, "Guardando categoria", 0, rowsToUpdate.length, `0 de ${rowsToUpdate.length} registros procesados.`);
        modal.hidden = false;
        setStatus(
            rowsToUpdate.length > 1
                ? `Guardando categoria en ${rowsToUpdate.length} registros...`
                : "Guardando categoria del flujo de caja en Dataverse...",
            "info");

        try {
            let lastPayload = null;
            for (let index = 0; index < rowsToUpdate.length; index += 1) {
                const rowToUpdate = rowsToUpdate[index];
                const progressItem = progressItems[index];
                updateBulkProgressItem(progressItem, "running", "Guardando en Dataverse...");
                const recordId = rowToUpdate.dataset.cashflowRecordId || "";
                const movementExternalKey = rowToUpdate.dataset.movementExternalKey || "";
                if (!recordId && !movementExternalKey) {
                    throw new Error("Una de las filas seleccionadas no tiene identificador de flujo de caja.");
                }

                const response = await fetch(cashFlowCategoryUrl, {
                    method: "POST",
                    headers: { "Content-Type": "application/json" },
                    body: JSON.stringify({
                        recordId,
                        sourceKind: rowToUpdate.dataset.sourceKind || "Movimiento",
                        movementExternalKey,
                        clientPaymentRecordId: rowToUpdate.dataset.clientPaymentRecordId || "",
                        categoryValue: nextValue,
                        reason: `Categoria reasignada manualmente a ${nextLabel} desde Conciliacion.`
                    })
                });
                const payload = await response.json().catch(() => ({}));
                if (!response.ok) {
                    throw new Error(payload.detail || payload.message || "No fue posible guardar la categoria.");
                }
                lastPayload = payload;
                updateBulkProgressItem(progressItem, "success", payload.message || "Categoria guardada.");

                const visibleRecordId = rowToUpdate.dataset.recordId || recordId;
                const rows = visibleRecordId
                    ? Array.from(app.querySelectorAll(`[data-record-id="${CSS.escape(visibleRecordId)}"]`))
                    : [rowToUpdate];

                rows.forEach((row) => {
                    row.dataset.currentType = nextValue;
                    const badge = row.querySelector("[data-cnc-type-label]");
                    if (badge) {
                        badge.textContent = payload.categoryLabel || nextLabel;
                        badge.className = `cnc-badge cnc-badge--${payload.categoryTone || nextTone}`;
                    }
                });
                setBulkProgress(
                    modal,
                    "Guardando categoria",
                    index + 1,
                    rowsToUpdate.length,
                    `${index + 1} de ${rowsToUpdate.length} registros procesados.`);
            }

            closeReassignModal();
            setStatus(lastPayload?.message || "Categoria guardada en Dataverse.", "success");
            closeButtons.forEach((button) => { button.disabled = false; });
            if (reload) {
                reload.hidden = false;
            }
            window.setTimeout(reloadPreservingView, 650);
        } catch (error) {
            const failedItem = progressItems.find((item) => item.dataset.state === "running");
            if (failedItem) {
                updateBulkProgressItem(failedItem, "error", error instanceof Error ? error.message : "No se pudo completar.");
            }
            closeButtons.forEach((button) => { button.disabled = false; });
            if (reload) {
                reload.hidden = false;
            }
            setStatus(error instanceof Error ? error.message : "Ocurrio un error inesperado.", "error");
        } finally {
            if (reassignApply) {
                reassignApply.disabled = false;
            }
        }
    };

    const getCategoryOptionsForRow = (row) => {
        const direction = row?.dataset?.direction || "";
        const currentType = String(row?.dataset?.currentType || "").trim().toLowerCase();
        const isExcludedFromReconciliation = currentType === "no-incluida-conciliacion";
        return isExcludedFromReconciliation
            ? (excludedReconciliationOptionsByDirection[direction] || categoryOptions.NoIncluida)
            : (categoryOptions[direction] || categoryOptions.Salida);
    };

    const resolveCashFlowRowLabel = (row) =>
        row?.dataset?.description
        || row?.dataset?.bankDetail
        || row?.querySelector("td:nth-child(3) strong")?.textContent?.trim()
        || row?.dataset?.recordId
        || "Movimiento";

    const updateCashFlowRowCategory = (row, value, label, tone) => {
        if (!row) {
            return;
        }

        row.dataset.currentType = value;
        row.dataset.typeLabel = label;
        row.dataset.typeTone = tone;
        row.dataset.actionTarget = categoryTarget(value);
        const badge = row.querySelector("[data-cnc-type-label]");
        if (badge) {
            badge.textContent = label;
            badge.className = `cnc-badge cnc-badge--${tone}`;
        }
    };

    const findCashFlowSiblingRows = (row) => {
        if (!row) {
            return [];
        }

        const selectors = [];
        const recordId = row.dataset.cashflowRecordId || row.dataset.recordId || "";
        const movementExternalKey = row.dataset.movementExternalKey || "";
        if (recordId) {
            selectors.push(`[data-record-id="${CSS.escape(recordId)}"]`);
            selectors.push(`[data-cashflow-record-id="${CSS.escape(recordId)}"]`);
        }
        if (movementExternalKey) {
            selectors.push(`[data-movement-external-key="${CSS.escape(movementExternalKey)}"]`);
        }

        const rows = selectors.flatMap((selector) => Array.from(app.querySelectorAll(selector)));
        rows.push(row);
        return Array.from(new Set(rows))
            .filter((item) => item?.matches?.("tr[data-record-id]"));
    };

    const updateConciliacion2CheckState = (row, conciliated, pendingKind = "pending") => {
        if (!row) {
            return;
        }

        const omitted = !conciliated && pendingKind === "omitted";
        row.dataset.filterConciliated = omitted ? "omitido" : conciliated ? "conciliado" : "pendiente";
        row.querySelectorAll("[data-cnc-v2-conciliated-label]").forEach((label) => {
            const reviewPending = !conciliated && pendingKind === "review";
            label.innerHTML = omitted ? "Omitido" : conciliated ? "&#10003;" : reviewPending ? "Revisar" : "Pendiente";
            label.setAttribute(
                "aria-label",
                omitted ? "Omitido" : conciliated ? "Conciliado" : reviewPending ? "Pendiente por verificar" : "Pendiente");
            label.className = `cnc-v2-state cnc-v2-state--${omitted ? "omitted" : conciliated ? "ok" : reviewPending ? "review" : "pending"}`;
        });
    };

    const appendSiigoDocumentToConciliacion2Description = (row, payloadRow = null) => {
        const siigoDocumentName = String(
            payloadRow?.siigoDocumentName
            || row?.dataset?.siigoDocumentName
            || "").trim();
        if (!row
            || !siigoDocumentName
            || normalizeText(siigoDocumentName) === "subida manualmente en siigo") {
            return;
        }

        row.dataset.siigoDocumentName = siigoDocumentName;
        const currentDescription = String(row.dataset.description || "").trim();
        if (normalizeText(currentDescription).includes(normalizeText(siigoDocumentName))) {
            return;
        }

        const displayDescription = currentDescription
            ? `${currentDescription} - ${siigoDocumentName}`
            : siigoDocumentName;
        syncConciliacion2DescriptionDisplay(row, displayDescription, payloadRow);
    };

    const markCashFlowRowConciliated = (row, payloadRow = null) => {
        const rows = findCashFlowSiblingRows(row);
        if (rows.length === 0) {
            return;
        }

        rows.forEach((targetRow) => {
            targetRow.dataset.cncCashflowPending = "false";
            targetRow.dataset.validationStatus = payloadRow?.validationStatus || "Validada";
            targetRow.dataset.registrationStatus = payloadRow?.registrationStatus || "Dataverse OK / Siigo OK";
            targetRow.dataset.dataverseStatus = payloadRow?.dataverseStatus || targetRow.dataset.dataverseStatus || "Conciliado";
            targetRow.classList.remove("is-siigo-pending", "is-review-pending");
            targetRow.classList.add("is-manual-conciliated");
            appendSiigoDocumentToConciliacion2Description(targetRow, payloadRow);

            const validation = targetRow.querySelector("[data-cnc-validation-label]");
            if (validation) {
                validation.textContent = targetRow.dataset.validationStatus;
                validation.className = "cnc-badge cnc-badge--success";
            }

            const registration = targetRow.querySelector("[data-cnc-registration-label]");
            if (registration) {
                registration.textContent = targetRow.dataset.registrationStatus;
                registration.className = "cnc-badge cnc-badge--success";
            }

            targetRow.querySelectorAll("[data-cnc-cashflow-manual]").forEach((button) => {
                button.disabled = true;
                button.textContent = "Conciliado";
            });
            updateConciliacion2CheckState(targetRow, true);
        });
    };

    const isNoSiigoCashFlowCategory = (value) =>
        value === "no-incluida-conciliacion";

    const isPendingSiigoCashFlowCategory = (value) =>
        value === "traslado-interno";

    const markCashFlowRowPendingSiigo = (row) => {
        const rows = findCashFlowSiblingRows(row);
        rows.forEach((targetRow) => {
            targetRow.dataset.cncCashflowPending = "true";
            targetRow.dataset.validationStatus = "Interno / fase Siigo pendiente";
            targetRow.dataset.registrationStatus = "Dataverse OK / Siigo pendiente";
            targetRow.classList.remove("is-manual-conciliated", "is-review-pending", "is-siigo-pending");

            const validation = targetRow.querySelector("[data-cnc-validation-label]");
            if (validation) {
                validation.textContent = targetRow.dataset.validationStatus;
                validation.className = "cnc-badge cnc-badge--info";
            }

            const registration = targetRow.querySelector("[data-cnc-registration-label]");
            if (registration) {
                registration.textContent = targetRow.dataset.registrationStatus;
                registration.className = "cnc-badge cnc-badge--info";
            }

            updateConciliacion2CheckState(targetRow, false);
        });
    };

    const markCashFlowRowPendingReview = (row, payloadRow = null) => {
        const rows = findCashFlowSiblingRows(row);
        rows.forEach((targetRow) => {
            targetRow.dataset.cncCashflowPending = "true";
            targetRow.dataset.validationStatus = payloadRow?.validationStatus || "Pendiente por verificar";
            targetRow.dataset.registrationStatus = payloadRow?.registrationStatus || "Dataverse OK / conciliacion pendiente";
            targetRow.dataset.dataverseStatus = payloadRow?.dataverseStatus || "PendienteRevision";
            if (payloadRow?.reviewReason) {
                targetRow.dataset.reviewReason = payloadRow.reviewReason;
            }
            targetRow.classList.remove("is-manual-conciliated", "is-siigo-pending");
            targetRow.classList.add("is-review-pending");

            const validation = targetRow.querySelector("[data-cnc-validation-label]");
            if (validation) {
                validation.textContent = targetRow.dataset.validationStatus;
                validation.className = "cnc-badge cnc-badge--warning";
            }

            const registration = targetRow.querySelector("[data-cnc-registration-label]");
            if (registration) {
                registration.textContent = targetRow.dataset.registrationStatus;
                registration.className = "cnc-badge cnc-badge--warning";
            }

            updateConciliacion2CheckState(targetRow, false, "review");
        });
    };

    const markCashFlowRowOmitted = (row, payloadRow = null, reason = "") => {
        const rows = findCashFlowSiblingRows(row);
        rows.forEach((targetRow) => {
            targetRow.dataset.cncCashflowPending = "false";
            targetRow.dataset.validationStatus = payloadRow?.validationStatus || "Omitido";
            targetRow.dataset.registrationStatus = payloadRow?.registrationStatus || "Dataverse OK / omitido";
            targetRow.dataset.dataverseStatus = "Omitido";
            targetRow.dataset.reviewReason = payloadRow?.reviewReason || reason || targetRow.dataset.reviewReason || "";
            targetRow.classList.remove("is-manual-conciliated", "is-review-pending", "is-siigo-pending");
            targetRow.classList.add("is-omitted");

            const validation = targetRow.querySelector("[data-cnc-validation-label]");
            if (validation) {
                validation.textContent = targetRow.dataset.validationStatus;
                validation.className = "cnc-badge cnc-badge--neutral";
            }

            const registration = targetRow.querySelector("[data-cnc-registration-label]");
            if (registration) {
                registration.textContent = targetRow.dataset.registrationStatus;
                registration.className = "cnc-badge cnc-badge--neutral";
            }

            updateConciliacion2CheckState(targetRow, false, "omitted");
        });
    };

    const markCashFlowRowNoSiigo = (row) => {
        markCashFlowRowConciliated(row, {
            validationStatus: "No incluida",
            registrationStatus: "Dataverse OK / no aplica Siigo"
        });
    };

    const markCashFlowManualSiigo = async (trigger, options = {}) => {
        const row = trigger?.matches?.("tr[data-record-id]")
            ? trigger
            : trigger?.closest?.("tr[data-record-id]");
        if (!row) {
            return { success: false, message: "No se encontro la fila del flujo de caja." };
        }

        if (!cashFlowManualUrl) {
            setStatus("No se encontro la ruta para marcar el flujo de caja como manual.", "error");
            return { success: false, message: "No se encontro la ruta para marcar el flujo de caja como manual." };
        }

        const recordId = row.dataset.cashflowRecordId || row.dataset.recordId || "";
        const movementExternalKey = row.dataset.movementExternalKey || "";
        if (!recordId && !movementExternalKey) {
            setStatus("No se encontro el identificador del flujo de caja.", "error");
            return { success: false, message: "No se encontro el identificador del flujo de caja." };
        }

        const confirmed = options.skipConfirm
            ? true
            : window.confirm("Esto marcara este movimiento como Conciliado manualmente en Siigo y conciliado. Continuar?");
        if (!confirmed) {
            return { success: false, message: "Accion cancelada." };
        }

        const buttons = Array.from(row.querySelectorAll("[data-cnc-cashflow-manual], [data-cnc-tab-target], [data-cnc-open-reassign]"));
        buttons.forEach((button) => { button.disabled = true; });
        setStatus("Marcando flujo de caja como Conciliado manualmente...", "info");

        try {
            const response = await fetch(cashFlowManualUrl, {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify({
                    recordId,
                    sourceKind: row.dataset.sourceKind || "Movimiento",
                    movementExternalKey,
                    clientPaymentRecordId: row.dataset.matchRecordId || row.dataset.clientPaymentRecordId || "",
                    reason: options.reason || "Movimiento Conciliado manualmente a Siigo desde Conciliacion. No se envio payload desde la app."
                })
            });
            const payload = await response.json().catch(() => ({}));
            if (!response.ok) {
                throw new Error(payload.detail || payload.message || "No fue posible marcar el movimiento como manual.");
            }

            markCashFlowRowConciliated(row, payload.row);
            setStatus(payload.message || "Flujo de caja marcado como Conciliado manualmente.", "success");
            refreshBulkSections();
            if (!options.suppressReload) {
                window.setTimeout(reloadPreservingView, 650);
            }
            return { success: true, message: payload.message || "Flujo de caja marcado como Conciliado manualmente.", payload };
        } catch (error) {
            const message = error instanceof Error ? error.message : "Ocurrio un error inesperado.";
            setStatus(message, "error");
            buttons.forEach((button) => { button.disabled = false; });
            return { success: false, message };
        }
    };

    const syncConciliacion2CategoryDisplay = (row, value, label, tone) => {
        updateCashFlowRowCategory(row, value, label, tone);
        row.dataset.filterCategory = `${label || ""} ${value || ""}`;
    };

    const syncConciliacion2DescriptionDisplay = (row, description, payloadRow = null) => {
        if (!row) {
            return;
        }

        const value = String(description || "").trim();
        row.dataset.description = value;
        row.dataset.filterDescription = value;
        row.dataset.search = `${row.dataset.searchBase || row.dataset.search || ""} ${value}`.trim();

        const input = row.querySelector("[data-cnc-v2-description-input]");
        if (input) {
            input.value = value;
        }

        if (payloadRow?.detectedTypeKey) {
            syncConciliacion2CategoryDisplay(
                row,
                payloadRow.detectedTypeKey,
                payloadRow.detectedTypeLabel || categoryLabel(payloadRow.detectedTypeKey),
                payloadRow.detectedTypeTone || categoryTone(payloadRow.detectedTypeKey));
        }
    };

    const saveConciliacion2Description = async (trigger) => {
        const row = trigger?.closest?.("tr[data-record-id]");
        const input = row?.querySelector("[data-cnc-v2-description-input]");
        if (!row || !input) {
            return;
        }

        if (!cashFlowDescriptionUrl) {
            setStatus("No se encontro la ruta para guardar la descripcion.", "error");
            return;
        }

        const previousValue = row.dataset.description || "";
        const nextValue = String(input.value || "").trim();
        if (nextValue === previousValue) {
            setStatus("La descripcion no tiene cambios.", "info");
            return;
        }

        input.disabled = true;
        const button = row.querySelector("[data-cnc-v2-description-save]");
        if (button) {
            button.disabled = true;
        }
        setStatus("Guardando descripcion del movimiento...", "info");

        try {
            const response = await fetch(cashFlowDescriptionUrl, {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify({
                    recordId: row.dataset.cashflowRecordId || row.dataset.recordId || "",
                    sourceKind: row.dataset.sourceKind || "Movimiento",
                    movementExternalKey: row.dataset.movementExternalKey || "",
                    description: nextValue
                })
            });
            const payload = await response.json().catch(() => ({}));
            if (!response.ok) {
                throw new Error(payload.detail || payload.message || "No fue posible guardar la descripcion.");
            }

            const savedValue = payload.description ?? payload.row?.description ?? nextValue;
            findCashFlowSiblingRows(row).forEach((targetRow) => {
                syncConciliacion2DescriptionDisplay(targetRow, savedValue, payload.row || null);
            });
            setStatus(payload.message || "Descripcion guardada.", "success");
            applyGenericTableFilter("conciliacion-2");
        } catch (error) {
            input.value = previousValue;
            setStatus(error instanceof Error ? error.message : "Ocurrio un error inesperado.", "error");
        } finally {
            input.disabled = false;
            if (button) {
                button.disabled = false;
            }
        }
    };

    const closeCashFlowPendingModal = () => {
        const modal = document.getElementById("cncCashFlowPendingModal");
        if (modal) {
            modal.hidden = true;
        }
        cashFlowPendingRow = null;
        cashFlowPendingMode = "pending";
    };

    const saveCashFlowPending = async () => {
        const modal = document.getElementById("cncCashFlowPendingModal");
        const row = cashFlowPendingRow;
        const omitted = cashFlowPendingMode === "omitted";
        const reasonInput = modal?.querySelector("[data-cnc-cashflow-pending-reason]");
        const status = modal?.querySelector("[data-cnc-cashflow-pending-status]");
        const saveButton = modal?.querySelector("[data-cnc-cashflow-pending-save]");
        const reason = String(reasonInput?.value || "").trim();
        if (!modal || !row || !reasonInput || !status || !saveButton) {
            return;
        }

        if (!reason) {
            status.textContent = omitted
                ? "Escribe la observación por la cual se omite el movimiento."
                : "Escribe por qué el movimiento debe quedar pendiente.";
            reasonInput.focus();
            return;
        }

        const targetUrl = omitted ? cashFlowOmittedUrl : cashFlowPendingUrl;
        if (!targetUrl) {
            status.textContent = omitted
                ? "No se encontró la ruta para guardar el movimiento omitido."
                : "No se encontró la ruta para guardar el pendiente.";
            return;
        }

        saveButton.disabled = true;
        reasonInput.disabled = true;
        status.textContent = omitted
            ? "Guardando observación y marcando como omitido en Dataverse..."
            : "Guardando motivo en Dataverse...";

        try {
            const response = await fetch(targetUrl, {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify({
                    recordId: row.dataset.cashflowRecordId || row.dataset.recordId || "",
                    sourceKind: row.dataset.sourceKind || "Movimiento",
                    movementExternalKey: row.dataset.movementExternalKey || "",
                    reason
                })
            });
            const payload = await response.json().catch(() => ({}));
            if (!response.ok) {
                throw new Error(payload.detail || payload.message || (omitted
                    ? "No fue posible omitir el movimiento."
                    : "No fue posible dejar el movimiento pendiente."));
            }

            const payloadRow = payload.row || null;
            const savedDescription = payload.description ?? payloadRow?.description ?? row.dataset.description ?? "";
            findCashFlowSiblingRows(row).forEach((targetRow) => {
                syncConciliacion2DescriptionDisplay(targetRow, savedDescription, payloadRow);
            });
            if (omitted) {
                markCashFlowRowOmitted(row, payloadRow, reason);
            } else {
                markCashFlowRowPendingReview(row, payloadRow);
            }
            refreshBulkSections();
            applyGenericTableFilter("conciliacion-2");
            setStatus(
                payload.message || (omitted ? "Movimiento marcado como omitido." : "Movimiento pendiente para verificación."),
                "success");
            closeCashFlowPendingModal();
            ensureCashFlowWizardModal().hidden = true;
            cashFlowWizardClientPayment = null;
            cashFlowWizardMode = "rows";
        } catch (error) {
            status.textContent = error instanceof Error ? error.message : "Ocurrio un error inesperado.";
        } finally {
            saveButton.disabled = false;
            reasonInput.disabled = false;
        }
    };

    const ensureCashFlowPendingModal = () => {
        let modal = document.getElementById("cncCashFlowPendingModal");
        if (modal) {
            return modal;
        }

        modal = document.createElement("div");
        modal.className = "cnc-modal";
        modal.id = "cncCashFlowPendingModal";
        modal.setAttribute("role", "dialog");
        modal.setAttribute("aria-modal", "true");
        modal.setAttribute("aria-labelledby", "cncCashFlowPendingTitle");
        modal.hidden = true;
        modal.innerHTML = `
            <div class="cnc-modal__panel cnc-cashflow-pending-panel">
                <div class="cnc-modal__header">
                    <div>
                        <div class="cnc-kicker">Conciliacion</div>
                        <h2 id="cncCashFlowPendingTitle" data-cnc-cashflow-pending-title>Dejar pendiente</h2>
                    </div>
                    <button type="button" class="cnc-cashflow-wizard__close" data-cnc-cashflow-pending-close aria-label="Cerrar" title="Cerrar">
                        <span aria-hidden="true">&times;</span>
                    </button>
                </div>
                <div data-cnc-cashflow-pending-existing-wrap hidden>
                    <label>Descripcion actual</label>
                    <p class="cnc-cashflow-pending-existing" data-cnc-cashflow-pending-existing></p>
                </div>
                <label class="cnc-modal__field">
                    <span data-cnc-cashflow-pending-label>¿Por qué queda pendiente?</span>
                    <textarea class="form-control" rows="5" maxlength="1000" required data-cnc-cashflow-pending-reason placeholder="Describe qué hace falta comprobar para conciliarlo después."></textarea>
                </label>
                <p class="cnc-cashflow-pending-status" data-cnc-cashflow-pending-status aria-live="polite"></p>
                <div class="cnc-modal__actions">
                    <button type="button" class="btn btn-outline-secondary" data-cnc-cashflow-pending-cancel>Cancelar</button>
                    <button type="button" class="btn btn-warning" data-cnc-cashflow-pending-save>Guardar pendiente</button>
                </div>
            </div>`;
        document.body.appendChild(modal);
        modal.querySelector("[data-cnc-cashflow-pending-close]")?.addEventListener("click", closeCashFlowPendingModal);
        modal.querySelector("[data-cnc-cashflow-pending-cancel]")?.addEventListener("click", closeCashFlowPendingModal);
        modal.querySelector("[data-cnc-cashflow-pending-save]")?.addEventListener("click", saveCashFlowPending);
        modal.querySelector("[data-cnc-cashflow-pending-reason]")?.addEventListener("keydown", (event) => {
            if (event.key === "Enter" && (event.ctrlKey || event.metaKey)) {
                event.preventDefault();
                saveCashFlowPending();
            }
        });
        modal.addEventListener("click", (event) => {
            if (event.target === modal) {
                closeCashFlowPendingModal();
            }
        });
        return modal;
    };

    const openCashFlowPendingModal = (row, mode = "pending") => {
        if (!row) {
            setStatus("No se encontró el movimiento seleccionado.", "error");
            return;
        }

        cashFlowPendingMode = mode === "omitted" ? "omitted" : "pending";
        const omitted = cashFlowPendingMode === "omitted";
        cashFlowPendingRow = row;
        const modal = ensureCashFlowPendingModal();
        const description = String(row.dataset.description || "").trim();
        const title = modal.querySelector("[data-cnc-cashflow-pending-title]");
        const existingWrap = modal.querySelector("[data-cnc-cashflow-pending-existing-wrap]");
        const existing = modal.querySelector("[data-cnc-cashflow-pending-existing]");
        const reasonLabel = modal.querySelector("[data-cnc-cashflow-pending-label]");
        const reasonInput = modal.querySelector("[data-cnc-cashflow-pending-reason]");
        const status = modal.querySelector("[data-cnc-cashflow-pending-status]");
        const saveButton = modal.querySelector("[data-cnc-cashflow-pending-save]");
        if (title) {
            title.textContent = omitted ? "Marcar movimiento como OMITIDO" : "Dejar pendiente";
        }
        if (existingWrap && existing) {
            existingWrap.hidden = !description;
            existing.textContent = description;
        }
        if (reasonLabel) {
            reasonLabel.textContent = omitted ? "Observación obligatoria" : "¿Por qué queda pendiente?";
        }
        if (reasonInput) {
            reasonInput.value = "";
            reasonInput.disabled = false;
            reasonInput.placeholder = omitted
                ? "Explica por qué este movimiento no debe incluirse en la conciliación."
                : "Describe qué hace falta comprobar para conciliarlo después.";
        }
        if (status) {
            status.textContent = "";
        }
        if (saveButton) {
            saveButton.disabled = false;
            saveButton.textContent = omitted ? "Guardar como OMITIDO" : "Guardar pendiente";
            saveButton.className = `btn ${omitted ? "btn-secondary" : "btn-warning"}`;
        }
        modal.hidden = false;
        window.setTimeout(() => reasonInput?.focus(), 0);
    };

    const markConciliacion2Check = async (checkbox) => {
        const row = checkbox?.closest?.("tr[data-record-id]");
        if (!row) {
            return;
        }

        if (row.dataset.cncCashflowPending !== "true") {
            updateConciliacion2CheckState(row, true);
            setStatus("Esta fila ya aparece conciliada.", "info");
            return;
        }

        const result = await markCashFlowManualSiigo(row, {
            suppressReload: true,
            reason: "Movimiento marcado como conciliado desde check de Conciliacion 2."
        });
        if (result?.success) {
            markCashFlowRowConciliated(row, result.payload?.row || null);
            setStatus(result.message || "Fila conciliada.", "success");
        } else {
            checkbox.checked = false;
        }
    };

    const isVisibleCashFlowWizardRow = (row) =>
        row?.dataset?.cncCashflowPending === "true"
        && !row.hidden
        && !row.closest("[hidden]")
        && verticalMatches(row.dataset.flow || "");

    const isAccumulatedCashFlowRow = (row) =>
        Boolean(String(row?.dataset?.cncAccumulatedGroupKey || "").trim());

    const getVisibleCashFlowWizardRows = () =>
        Array.from(app.querySelectorAll('[data-cnc-panel="flujo-caja"] tr[data-cnc-cashflow-pending="true"]'))
            .filter((row) => isVisibleCashFlowWizardRow(row) && !isAccumulatedCashFlowRow(row));

    const getVisibleCashFlowAccumulatedGroups = () => {
        const visibleRows = Array.from(app.querySelectorAll('[data-cnc-panel="flujo-caja"] tr[data-cnc-cashflow-pending="true"][data-cnc-accumulated-group-key]'))
            .filter((row) => isVisibleCashFlowWizardRow(row) && isAccumulatedCashFlowRow(row));
        const visibleKeys = new Set(visibleRows.map((row) => row.dataset.cncAccumulatedGroupKey || "").filter(Boolean));
        const rows = Array.from(app.querySelectorAll('[data-cnc-panel="flujo-caja"] tr[data-cnc-cashflow-pending="true"][data-cnc-accumulated-group-key]'))
            .filter((row) =>
                isAccumulatedCashFlowRow(row)
                && visibleKeys.has(row.dataset.cncAccumulatedGroupKey || "")
                && verticalMatches(row.dataset.flow || ""));
        const groups = new Map();

        rows.forEach((row) => {
            const key = row.dataset.cncAccumulatedGroupKey || "";
            if (!groups.has(key)) {
                groups.set(key, {
                    key,
                    label: row.dataset.cncAccumulatedGroupLabel || "Comprobante acumulado",
                    detail: row.dataset.cncAccumulatedGroupDetail || "",
                    amount: Number(row.dataset.cncAccumulatedGroupAmount || 0),
                    amountLabel: row.dataset.cncAccumulatedGroupAmountLabel || "",
                    count: Number(row.dataset.cncAccumulatedGroupCount || 0),
                    missingAccounts: row.dataset.cncAccumulatedGroupMissingAccounts === "true",
                    flow: row.dataset.flow || "",
                    bank: row.dataset.bankLabel || "",
                    target: row.dataset.actionTarget || "comprobantes",
                    rows: []
                });
            }

            groups.get(key).rows.push(row);
        });

        return Array.from(groups.values())
            .sort((left, right) => (left.label || "").localeCompare(right.label || "", "es-CO"));
    };

    const ensureCashFlowWizardModal = () => {
        let modal = document.getElementById("cncCashFlowWizardModal");
        if (modal) {
            return modal;
        }

        modal = document.createElement("div");
        modal.className = "cnc-modal cnc-modal--cashflow-wizard";
        modal.id = "cncCashFlowWizardModal";
        modal.setAttribute("role", "dialog");
        modal.setAttribute("aria-modal", "true");
        modal.hidden = true;
        modal.innerHTML = `
            <div class="cnc-modal__panel cnc-modal__panel--wide cnc-cashflow-wizard">
                <div class="cnc-modal__header cnc-cashflow-wizard__header">
                    <div>
                        <div class="cnc-kicker">Flujo de caja</div>
                        <h2 data-cnc-cashflow-wizard-title>Conciliar movimiento</h2>
                    </div>
                    <button type="button" class="cnc-cashflow-wizard__close" data-cnc-cashflow-wizard-close aria-label="Cerrar" title="Cerrar">
                        <span aria-hidden="true">&times;</span>
                    </button>
                </div>
                <div class="cnc-cashflow-wizard__progress">
                    <span data-cnc-cashflow-wizard-count></span>
                    <div class="cnc-progress" aria-hidden="true">
                        <div class="cnc-progress__bar" data-cnc-cashflow-wizard-bar></div>
                    </div>
                </div>
                <div class="cnc-cashflow-wizard__card" data-cnc-cashflow-wizard-card></div>
                <div class="cnc-cashflow-wizard__message is-empty" data-cnc-cashflow-wizard-message aria-live="polite"></div>
                <div class="cnc-modal__actions cnc-cashflow-wizard__actions">
                    <button type="button" class="btn btn-outline-secondary" data-cnc-cashflow-wizard-prev>Anterior</button>
                    <button type="button" class="btn btn-outline-secondary" data-cnc-cashflow-wizard-next>Saltar</button>
                    <button type="button" class="btn btn-outline-secondary" data-cnc-cashflow-wizard-pending>Dejar Pendiente</button>
                    <button type="button" class="btn btn-outline-secondary" data-cnc-cashflow-wizard-omitted>OMITIDO</button>
                    <button type="button" class="btn btn-primary" data-cnc-cashflow-wizard-process>Procesar</button>
                </div>
            </div>`;
        document.body.appendChild(modal);
        modal.querySelector("[data-cnc-cashflow-wizard-close]")?.addEventListener("click", () => {
            modal.hidden = true;
        });
        modal.addEventListener("click", (event) => {
            if (event.target === modal) {
                modal.hidden = true;
            }
        });
        modal.querySelector("[data-cnc-cashflow-wizard-prev]")?.addEventListener("click", () => moveCashFlowWizard(-1));
        modal.querySelector("[data-cnc-cashflow-wizard-next]")?.addEventListener("click", () => moveCashFlowWizard(1));
        modal.querySelector("[data-cnc-cashflow-wizard-pending]")?.addEventListener("click", leaveCurrentCashFlowWizardPending);
        modal.querySelector("[data-cnc-cashflow-wizard-omitted]")?.addEventListener("click", leaveCurrentCashFlowWizardOmitted);
        modal.querySelector("[data-cnc-cashflow-wizard-process]")?.addEventListener("click", processCurrentCashFlowWizardRow);
        return modal;
    };

    const setCashFlowWizardTitle = (title) => {
        const modal = ensureCashFlowWizardModal();
        const heading = modal.querySelector("[data-cnc-cashflow-wizard-title]");
        const panel = modal.querySelector(".cnc-cashflow-wizard");
        panel?.classList.toggle("is-client-payment", title === "Factura cliente");
        panel?.classList.toggle("is-supplier-payment", title === "Pago a proveedor");
        if (heading) {
            heading.textContent = title || "Conciliar movimiento";
        }
    };

    const refreshCashFlowWizardAccumulatedGroups = () => {
        const current = getVisibleCashFlowAccumulatedGroups();
        if (cashFlowWizardAccumulatedGroups.length === 0) {
            cashFlowWizardAccumulatedGroups = current;
            return;
        }

        const currentByKey = new Map(current.map((group) => [group.key, group]));
        cashFlowWizardAccumulatedGroups = cashFlowWizardAccumulatedGroups
            .map((group) => currentByKey.get(group.key))
            .filter(Boolean);
    };

    const renderCashFlowAccumulatedGroups = (modal, message = "", tone = "info") => {
        setCashFlowWizardTitle("Comprobantes acumulados");
        const card = modal.querySelector("[data-cnc-cashflow-wizard-card]");
        const count = modal.querySelector("[data-cnc-cashflow-wizard-count]");
        const bar = modal.querySelector("[data-cnc-cashflow-wizard-bar]");
        const messageBox = modal.querySelector("[data-cnc-cashflow-wizard-message]");
        const prev = modal.querySelector("[data-cnc-cashflow-wizard-prev]");
        const next = modal.querySelector("[data-cnc-cashflow-wizard-next]");
        const process = modal.querySelector("[data-cnc-cashflow-wizard-process]");
        const pending = modal.querySelector("[data-cnc-cashflow-wizard-pending]");
        const omitted = modal.querySelector("[data-cnc-cashflow-wizard-omitted]");
        const progress = modal.querySelector(".cnc-cashflow-wizard__progress");

        refreshCashFlowWizardAccumulatedGroups();
        const groups = cashFlowWizardAccumulatedGroups;
        if (count) {
            count.textContent = `${groups.length} acumulado${groups.length === 1 ? "" : "s"} al final`;
        }
        if (bar) {
            bar.style.width = "100%";
        }
        if (progress) {
            progress.hidden = false;
        }
        if (prev) {
            prev.disabled = cashFlowWizardRows.length === 0;
        }
        if (next) {
            next.disabled = true;
            next.textContent = "Saltar";
        }
        if (next) {
            next.hidden = true;
        }
        [process, pending, omitted].forEach((button) => {
            if (button) {
                button.hidden = true;
            }
        });

        if (groups.length === 0) {
            if (card) {
                card.innerHTML = `<div class="cnc-empty-state"><strong>No hay acumulados pendientes.</strong><small>Todos los items visibles quedaron conciliados o filtrados.</small></div>`;
            }
            if (messageBox) {
                messageBox.textContent = message || "No quedan items por recorrer.";
                messageBox.className = `cnc-cashflow-wizard__message is-${tone}`;
            }
            return;
        }

        if (card) {
            card.innerHTML = `
                <div class="cnc-cashflow-accumulated__intro">
                    <strong>Acumulados para comprobante unico</strong>
                    <small>Estos movimientos no entran al recorrido consecutivo. Se procesan como total unificado desde Comprobantes.</small>
                </div>
                <div class="cnc-cashflow-accumulated-list">
                    ${groups.map((group) => `
                        <article class="cnc-cashflow-accumulated-card" data-cnc-accumulated-card="${escapeHtml(group.key)}">
                            <header>
                                <div>
                                    <span class="cnc-badge cnc-badge--${group.missingAccounts ? "warning" : "info"}">${group.missingAccounts ? "Pendiente cuenta" : "Listo como acumulado"}</span>
                                    <h3>${escapeHtml(group.label)}</h3>
                                    <small>${escapeHtml(group.detail || group.bank || "Comprobante acumulado")}</small>
                                </div>
                                <strong>${escapeHtml(group.amountLabel || money(group.amount))}</strong>
                            </header>
                            <div class="cnc-cashflow-accumulated-card__meta">
                                <span>${numberLabel(group.count || group.rows.length)} movimiento${(group.count || group.rows.length) === 1 ? "" : "s"}</span>
                                <span>${escapeHtml(group.flow || "Sin vertical")}</span>
                                <span>${escapeHtml(group.bank || "Sin banco")}</span>
                            </div>
                            <details class="cnc-voucher-breakdown">
                                <summary>Ver desglose completo</summary>
                                <div class="cnc-voucher-breakdown__grid">
                                    ${group.rows.map((row) => `
                                        <div class="cnc-voucher-breakdown__line">
                                            <strong>${escapeHtml(row.dataset.dateDisplay || "Sin fecha")} - ${escapeHtml(row.dataset.amountLabel || "")}</strong>
                                            <small>${escapeHtml(resolveCashFlowRowLabel(row))}</small>
                                            <small>${escapeHtml(row.dataset.registrationStatus || "")}</small>
                                        </div>
                                    `).join("")}
                                </div>
                            </details>
                            <button type="button" class="btn btn-sm btn-primary" data-cnc-open-accumulated-group="${escapeHtml(group.key)}">
                                Abrir acumulado
                            </button>
                        </article>
                    `).join("")}
                </div>`;
            card.querySelectorAll("[data-cnc-open-accumulated-group]").forEach((button) => {
                button.addEventListener("click", () => openCashFlowAccumulatedGroup(button.dataset.cncOpenAccumulatedGroup || ""));
            });
        }
        if (messageBox) {
            messageBox.textContent = message || "Acumulados listos al final del recorrido.";
            messageBox.className = `cnc-cashflow-wizard__message is-${tone}`;
        }
    };

    const resetCashFlowWizardActionButtons = () => {
        const modal = ensureCashFlowWizardModal();
        const actions = modal.querySelector(".cnc-cashflow-wizard__actions");
        const prev = modal.querySelector("[data-cnc-cashflow-wizard-prev]");
        const next = modal.querySelector("[data-cnc-cashflow-wizard-next]");
        const process = modal.querySelector("[data-cnc-cashflow-wizard-process]");
        const pending = modal.querySelector("[data-cnc-cashflow-wizard-pending]");
        const omitted = modal.querySelector("[data-cnc-cashflow-wizard-omitted]");
        if (actions) {
            actions.hidden = false;
        }
        if (prev) {
            prev.hidden = false;
        }
        if (next) {
            next.hidden = false;
            next.textContent = "Saltar";
        }
        if (process) {
            process.hidden = false;
            process.textContent = "Procesar";
        }
        if (pending) {
            pending.hidden = false;
            pending.textContent = "Dejar Pendiente";
        }
        if (omitted) {
            omitted.hidden = false;
            omitted.textContent = "OMITIDO";
        }
    };

    const setCashFlowWizardProcessActions = () => {
        const modal = ensureCashFlowWizardModal();
        const actions = modal.querySelector(".cnc-cashflow-wizard__actions");
        const prev = modal.querySelector("[data-cnc-cashflow-wizard-prev]");
        const next = modal.querySelector("[data-cnc-cashflow-wizard-next]");
        const process = modal.querySelector("[data-cnc-cashflow-wizard-process]");
        const pending = modal.querySelector("[data-cnc-cashflow-wizard-pending]");
        const omitted = modal.querySelector("[data-cnc-cashflow-wizard-omitted]");
        if (actions) {
            actions.hidden = true;
        }
        if (prev) {
            prev.hidden = false;
            prev.disabled = false;
        }
        if (next) {
            next.hidden = true;
        }
        if (process) {
            process.hidden = true;
        }
        if (pending) {
            pending.hidden = true;
        }
        if (omitted) {
            omitted.hidden = true;
        }
    };

    const setCashFlowWizardSupplierMessage = (message, tone = "info") => {
        const modal = ensureCashFlowWizardModal();
        const messageBox = modal.querySelector("[data-cnc-cashflow-wizard-message]");
        if (messageBox) {
            const text = String(message || "").trim();
            messageBox.textContent = text;
            messageBox.className = text
                ? `cnc-cashflow-wizard__message is-${tone}`
                : "cnc-cashflow-wizard__message is-empty";
        }
    };

    const setCashFlowWizardMessage = setCashFlowWizardSupplierMessage;

    const resetCashFlowWizardProcessState = () => {
        cashFlowWizardSupplierPayment = null;
        cashFlowWizardClientPayment = null;
        cashFlowWizardCuentaCobro = null;
        cashFlowWizardAccountingVoucher = null;
    };

    const cashFlowWizardDataValues = (row) =>
        [
            row?.dataset?.cashflowRecordId,
            row?.dataset?.recordId,
            row?.dataset?.movementExternalKey,
            row?.dataset?.matchRecordId,
            row?.dataset?.clientPaymentRecordId
        ]
            .map((value) => String(value || "").trim())
            .filter(Boolean);

    const dataListContainsAny = (dataList, values) => {
        const candidates = parseDataList(dataList).map((value) => value.toLowerCase());
        return values.some((value) => candidates.includes(String(value || "").toLowerCase()));
    };

    const findClientPaymentRowForCashFlow = (row) => {
        const recordId = row?.dataset?.matchRecordId || row?.dataset?.clientPaymentRecordId || "";
        if (!recordId) {
            return null;
        }

        return Array.from(app.querySelectorAll("tr[data-record-id]"))
            .find((candidate) =>
                candidate.dataset.recordId === recordId
                && (candidate.hasAttribute("data-cnc-invoice-assign")
                    || candidate.querySelector("[data-cnc-action], [data-cnc-preflight], [data-cnc-send-siigo], [data-cnc-manual-siigo]")));
    };

    const findCuentaCobroRowForCashFlow = (row) => {
        const values = cashFlowWizardDataValues(row);
        if (values.length === 0) {
            return null;
        }

        return Array.from(app.querySelectorAll("tr[data-record-id]"))
            .find((candidate) => {
                const isCuentaCobro = candidate.hasAttribute("data-cnc-cuenta-cobro-edit")
                    || candidate.querySelector("[data-cnc-cuenta-cobro-preflight], [data-cnc-cuenta-cobro-send], [data-cnc-cuenta-cobro-send-payment], [data-cnc-cuenta-cobro-manual]");
                if (!isCuentaCobro) {
                    return false;
                }

                return values.includes(candidate.dataset.cashflowRecordId || "")
                    || values.includes(candidate.dataset.movementExternalKey || "")
                    || values.includes(candidate.dataset.recordId || "");
            });
    };

    const findAccountingVoucherRowForCashFlow = (row, groupKey = "") => {
        const values = cashFlowWizardDataValues(row);
        return Array.from(app.querySelectorAll("tr[data-record-id]"))
            .find((candidate) => {
                const isAccountingVoucher = candidate.hasAttribute("data-cnc-accounting-voucher-edit")
                    || candidate.querySelector("[data-cnc-accounting-voucher-send]");
                if (!isAccountingVoucher) {
                    return false;
                }

                if (groupKey && candidate.dataset.accountingVoucherGroupKey === groupKey) {
                    return true;
                }

                return values.includes(candidate.dataset.cashflowRecordId || "")
                    || values.includes(candidate.dataset.movementExternalKey || "")
                    || values.includes(candidate.dataset.recordId || "")
                    || dataListContainsAny(candidate.dataset.cashflowRecordIds || "", values)
                    || dataListContainsAny(candidate.dataset.movementExternalKeys || "", values);
            });
    };

    const renderCashFlowWizardIssues = (selector, issues) => {
        const modal = ensureCashFlowWizardModal();
        renderIssueList(modal, selector, issues);
    };

    const renderCashFlowWizardPreview = (payloadSelector, responseSelector, wrapSelector, payloadJson = "", responseJson = "") => {
        const modal = ensureCashFlowWizardModal();
        const payload = modal.querySelector(payloadSelector);
        const response = modal.querySelector(responseSelector);
        const wrap = modal.querySelector(wrapSelector);
        if (payload) {
            payload.textContent = payloadJson || "";
        }
        if (response) {
            response.textContent = responseJson || "";
        }
        if (wrap) {
            wrap.hidden = !payloadJson && !responseJson;
        }
    };

    const completeCashFlowWizardRow = (row, payloadRow, message) => {
        markCashFlowRowConciliated(row, payloadRow);
        const index = cashFlowWizardRows.indexOf(row);
        if (index >= 0) {
            cashFlowWizardRows.splice(index, 1);
        } else {
            cashFlowWizardRows.splice(cashFlowWizardIndex, 1);
        }
        if (cashFlowWizardIndex >= cashFlowWizardRows.length) {
            cashFlowWizardIndex = Math.max(0, cashFlowWizardRows.length - 1);
        }
        cashFlowWizardMode = cashFlowWizardRows.length > 0 ? "rows" : "accumulated";
        setStatus(message || "Item conciliado.", "success");
        resetCashFlowWizardProcessState();
        renderCashFlowWizard(message || "Item conciliado.", "success");
        refreshBulkSections();
    };

    const accountOptionsHtml = (select) =>
        Array.from(select?.options || [])
            .map((option) => `<option value="${escapeHtml(option.value)}"${option.disabled ? " disabled" : ""}>${escapeHtml(option.textContent || "")}</option>`)
            .join("");

    const getCashFlowWizardClientRecordId = () =>
        cashFlowWizardClientPayment?.recordId
        || cashFlowWizardClientPayment?.paymentRow?.dataset?.recordId
        || cashFlowWizardClientPayment?.row?.dataset?.matchRecordId
        || "";

    const hasCashFlowWizardClientInvoice = () => Boolean(
        cashFlowWizardClientPayment?.paymentRow?.dataset?.dataverseInvoice
        || cashFlowWizardClientPayment?.paymentRow?.dataset?.flowInvoice);

    const updateCashFlowWizardClientActions = () => {
        const modal = ensureCashFlowWizardModal();
        const recordId = getCashFlowWizardClientRecordId();
        const status = cashFlowWizardClientPayment?.status || cashFlowWizardClientPayment?.paymentRow?.dataset?.status || "";
        const hasSelection = getCashFlowWizardClientSelectedInvoiceIds().length > 0;
        const hasInvoice = hasCashFlowWizardClientInvoice();
        const finished = status === "EnviadoSiigo" || status === "Conciliado";
        const canSend = Boolean(recordId) && canSendPaymentStatus(status);
        const canValidate = Boolean(recordId)
            && hasInvoice
            && !hasSelection
            && !canSend
            && !finished
            && (status === "Aprobado" || status === "BloqueadoSiigo");

        const assign = modal.querySelector("[data-cnc-wizard-client-assign]");
        const approve = modal.querySelector("[data-cnc-wizard-client-approve]");
        const preflight = modal.querySelector("[data-cnc-wizard-client-preflight]");
        const send = modal.querySelector("[data-cnc-wizard-client-send]");
        if (assign) {
            assign.hidden = !recordId || !hasSelection;
            assign.disabled = !recordId || !hasSelection;
        }
        if (approve) {
            approve.hidden = !recordId || !hasInvoice || hasSelection || canValidate || canSend || finished;
            approve.disabled = !recordId;
        }
        if (preflight) {
            preflight.hidden = !canValidate;
            preflight.disabled = !canValidate;
        }
        if (send) {
            send.hidden = !canSend;
            send.disabled = !canSend;
        }
    };

    const renderCashFlowWizardClientIssues = () => {
        renderCashFlowWizardIssues("[data-cnc-wizard-client-issues]", cashFlowWizardClientPayment?.issues || []);
    };

    const renderCashFlowWizardClientPreview = () => {
        renderCashFlowWizardPreview(
            "[data-cnc-wizard-client-payload]",
            "[data-cnc-wizard-client-response]",
            "[data-cnc-wizard-client-preview]",
            cashFlowWizardClientPayment?.payloadJson || "",
            cashFlowWizardClientPayment?.responseJson || "");
    };

    const getCashFlowWizardClientSelectedInvoiceIds = () =>
        (cashFlowWizardClientPayment?.selectedInvoices || [])
            .map((invoice) => invoice?.recordId || "")
            .filter(Boolean);

    const renderCashFlowWizardClientSelection = () => {
        const modal = ensureCashFlowWizardModal();
        const summary = modal.querySelector("[data-cnc-wizard-client-selection]");
        renderInvoiceSelectionSummary(
            summary,
            cashFlowWizardClientPayment?.selectedInvoices || [],
            Number(cashFlowWizardClientPayment?.row?.dataset?.entryValue || 0),
            "",
            { hideWhenEmpty: true });
        updateCashFlowWizardClientActions();
    };

    const toggleCashFlowWizardClientInvoice = (invoice) => {
        if (!cashFlowWizardClientPayment || !invoice?.recordId) {
            return;
        }

        const selected = cashFlowWizardClientPayment.selectedInvoices || [];
        const exists = selected.some((item) => item.recordId === invoice.recordId);
        cashFlowWizardClientPayment.selectedInvoices = exists
            ? selected.filter((item) => item.recordId !== invoice.recordId)
            : [...selected, invoice];

        renderCashFlowWizardClientResults();
        renderCashFlowWizardClientSelection();
    };

    const renderCashFlowWizardClientResults = () => {
        const modal = ensureCashFlowWizardModal();
        const box = modal.querySelector("[data-cnc-wizard-client-results]");
        if (!box || !cashFlowWizardClientPayment) {
            return;
        }

        const items = cashFlowWizardClientPayment.invoices || [];
        const selectedIds = new Set(getCashFlowWizardClientSelectedInvoiceIds());
        box.innerHTML = "";
        if (items.length === 0) {
            box.hidden = !cashFlowWizardClientPayment.hasSearched;
            if (!cashFlowWizardClientPayment.hasSearched) {
                return;
            }
            const empty = document.createElement("small");
            empty.textContent = "Sin facturas encontradas.";
            box.appendChild(empty);
            return;
        }
        box.hidden = false;

        items.forEach((invoice) => {
            const button = document.createElement("button");
            const title = document.createElement("strong");
            const amount = document.createElement("span");
            const client = document.createElement("small");
            const detail = document.createElement("small");
            button.type = "button";
            button.className = "cnc-invoice-result";
            button.dataset.invoiceId = invoice.recordId || "";
            button.classList.toggle("is-selected", selectedIds.has(invoice.recordId || ""));
            title.textContent = invoice.invoiceNumber || "Sin factura";
            amount.textContent = money(invoice.totalInvoice);
            client.textContent = `${invoice.clientName || "Sin cliente"} - ${invoice.emissionDateDisplay || "Sin fecha"}`;
            detail.textContent = `Neto ${money(Number(invoice.totalInvoice || 0) - invoiceRetentionTotal(invoice))} | Diferencia ${money(invoice.differenceWithEntry || 0)}`;
            title.appendChild(amount);
            button.append(title, client, detail);
            button.addEventListener("click", () => toggleCashFlowWizardClientInvoice(invoice));
            box.appendChild(button);
        });
    };

    const renderCashFlowWizardClientPaymentLegacy = (row, message = "", tone = "info") => {
        const modal = ensureCashFlowWizardModal();
        setCashFlowWizardTitle("Pago de cliente");
        const card = modal.querySelector("[data-cnc-cashflow-wizard-card]");
        const count = modal.querySelector("[data-cnc-cashflow-wizard-count]");
        const bar = modal.querySelector("[data-cnc-cashflow-wizard-bar]");
        cashFlowWizardMode = "client-payment";
        setCashFlowWizardProcessActions();
        if (count) {
            count.textContent = `Entrada FV ${cashFlowWizardIndex + 1} de ${cashFlowWizardRows.length}`;
        }
        if (bar) {
            bar.style.width = `${Math.round((cashFlowWizardIndex + 1) / Math.max(cashFlowWizardRows.length, 1) * 100)}%`;
        }

        const existingState = cashFlowWizardClientPayment?.row === row
            ? cashFlowWizardClientPayment
            : null;
        const paymentRow = existingState?.paymentRow || findClientPaymentRowForCashFlow(row);
        cashFlowWizardClientPayment = existingState || {
            row,
            paymentRow,
            recordId: row.dataset.matchRecordId || paymentRow?.dataset.recordId || "",
            status: paymentRow?.dataset.status || "",
            invoices: [],
            selectedInvoices: [],
            hasSearched: false,
            issues: [],
            payloadJson: "",
            responseJson: ""
        };

        const recordId = getCashFlowWizardClientRecordId();
        const status = cashFlowWizardClientPayment.status || cashFlowWizardClientPayment.paymentRow?.dataset.status || "";
        const invoiceLabel = [
            cashFlowWizardClientPayment.paymentRow?.dataset?.dataverseInvoice,
            cashFlowWizardClientPayment.paymentRow?.dataset?.dataverseClient
        ].filter(Boolean).join(" - ");

        if (card) {
            card.innerHTML = `
                <div class="cnc-wizard-process">
                    <header class="cnc-wizard-process__summary">
                        <div>
                            <span>${escapeHtml(row.dataset.dateDisplay || "Sin fecha")}</span>
                            <p>${escapeHtml(resolveCashFlowRowLabel(row))}</p>
                        </div>
                        <strong>${escapeHtml(row.dataset.amountLabel || money(Number(row.dataset.entryValue || 0)))}</strong>
                    </header>
                    ${recordId ? `
                        <div class="cnc-wizard-process__state">
                            <span class="cnc-badge cnc-badge--${escapeHtml(statusTone(status))}">${escapeHtml(statusLabel(status))}</span>
                            ${invoiceLabel ? `<strong>${escapeHtml(invoiceLabel)}</strong>` : ""}
                        </div>` : ""}
                    ${recordId ? `
                        <div class="cnc-supplier-payment-search">
                            <label class="cnc-modal__field">
                                <span>Buscar factura</span>
                                <input class="form-control" type="search" data-cnc-wizard-client-query value="${escapeHtml(cashFlowWizardClientPayment.paymentRow?.dataset.flowInvoice || cashFlowWizardClientPayment.paymentRow?.dataset.dataverseInvoice || row.dataset.description || "")}" placeholder="Factura, cliente o descripcion" />
                            </label>
                            <button type="button" class="btn btn-outline-primary" data-cnc-wizard-client-search>Buscar</button>
                        </div>
                        <div class="cnc-invoice-selected" data-cnc-wizard-client-selection></div>
                        <div class="cnc-invoice-results" data-cnc-wizard-client-results></div>` : ""}
                    <ul class="cnc-issue-list" data-cnc-wizard-client-issues hidden></ul>
                    <div class="cnc-wizard-process__actions">
                        <button type="button" class="btn btn-outline-secondary" data-cnc-wizard-client-back>Volver</button>
                        <button type="button" class="btn btn-primary" data-cnc-wizard-client-assign hidden>Asignar</button>
                        <button type="button" class="btn btn-primary" data-cnc-wizard-client-approve hidden>Aprobar</button>
                        <button type="button" class="btn btn-primary" data-cnc-wizard-client-preflight hidden>Validar</button>
                        <button type="button" class="btn btn-primary" data-cnc-wizard-client-send hidden>Enviar a Siigo</button>
                    </div>
                </div>`;
        }

        modal.querySelector("[data-cnc-wizard-client-search]")?.addEventListener("click", searchCashFlowWizardClientInvoices);
        modal.querySelector("[data-cnc-wizard-client-query]")?.addEventListener("keydown", (event) => {
            if (event.key === "Enter") {
                event.preventDefault();
                searchCashFlowWizardClientInvoices();
            }
        });
        modal.querySelector("[data-cnc-wizard-client-back]")?.addEventListener("click", () => {
            cashFlowWizardMode = "rows";
            cashFlowWizardClientPayment = null;
            renderCashFlowWizard();
        });
        modal.querySelector("[data-cnc-wizard-client-assign]")?.addEventListener("click", assignCashFlowWizardClientInvoices);
        modal.querySelector("[data-cnc-wizard-client-approve]")?.addEventListener("click", approveCashFlowWizardClientPayment);
        modal.querySelector("[data-cnc-wizard-client-preflight]")?.addEventListener("click", preflightCashFlowWizardClientPayment);
        modal.querySelector("[data-cnc-wizard-client-send]")?.addEventListener("click", sendCashFlowWizardClientPaymentToSiigoLegacy);

        renderCashFlowWizardClientResults();
        renderCashFlowWizardClientSelection();
        renderCashFlowWizardClientIssues();
        renderCashFlowWizardClientPreview();
        updateCashFlowWizardClientActions();
        setCashFlowWizardMessage(message || (recordId ? "" : "No hay un cruce relacionado con este movimiento."), recordId ? tone : "error");
    };

    const searchCashFlowWizardClientInvoices = async () => {
        if (!cashFlowWizardClientPayment?.row || !invoiceSearchUrl) {
            setCashFlowWizardMessage("No se encontro la ruta para buscar facturas.", "error");
            return;
        }

        const modal = ensureCashFlowWizardModal();
        const queryInput = modal.querySelector("[data-cnc-wizard-client-query]");
        const valueInput = modal.querySelector("[data-cnc-wizard-client-value]");
        const button = modal.querySelector("[data-cnc-wizard-client-search]");
        const results = modal.querySelector("[data-cnc-wizard-client-results]");
        const query = String(queryInput?.value || "").trim();
        const rawValue = Number(valueInput?.value || cashFlowWizardClientPayment.row?.dataset.entryValue || 0);
        const value = Number.isFinite(rawValue) && rawValue > 0 ? rawValue : null;
        if (!query && !value) {
            setCashFlowWizardMessage("Busca por factura, cliente o valor.", "info");
            return;
        }

        if (button) {
            button.disabled = true;
        }
        if (results) {
            results.innerHTML = "<small>Buscando facturas en Dataverse...</small>";
        }
        cashFlowWizardClientPayment.selectedInvoices = [];
        cashFlowWizardClientPayment.hasSearched = true;
        renderCashFlowWizardClientSelection();
        setCashFlowWizardMessage("Buscando facturas en Dataverse...", "info");

        try {
            const response = await fetch(invoiceSearchUrl, {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify({ query, value, top: 12 })
            });
            const payload = await response.json().catch(() => ({}));
            if (!response.ok) {
                throw new Error(payload.detail || payload.message || "No fue posible buscar facturas.");
            }

            cashFlowWizardClientPayment.invoices = payload.items || [];
            renderCashFlowWizardClientResults();
            setCashFlowWizardMessage(
                cashFlowWizardClientPayment.invoices.length
                    ? `${cashFlowWizardClientPayment.invoices.length} factura${cashFlowWizardClientPayment.invoices.length === 1 ? "" : "s"} encontrada${cashFlowWizardClientPayment.invoices.length === 1 ? "" : "s"}.`
                    : "Sin facturas encontradas.",
                "info");
        } catch (error) {
            cashFlowWizardClientPayment.invoices = [];
            renderCashFlowWizardClientResults();
            setCashFlowWizardMessage(error instanceof Error ? error.message : "Ocurrio un error inesperado.", "error");
        } finally {
            if (button) {
                button.disabled = false;
            }
        }
    };

    const assignCashFlowWizardClientInvoices = async () => {
        if (!cashFlowWizardClientPayment?.row || !invoiceAssignUrl) {
            setCashFlowWizardMessage("No se encontro la ruta para asignar factura.", "error");
            return;
        }

        const recordId = getCashFlowWizardClientRecordId();
        if (!recordId) {
            setCashFlowWizardMessage("No hay un cruce para asignar la factura.", "error");
            return;
        }
        const invoiceRecordIds = getCashFlowWizardClientSelectedInvoiceIds();
        if (invoiceRecordIds.length === 0) {
            setCashFlowWizardMessage("Selecciona una o varias facturas antes de asignar.", "info");
            return;
        }

        setCashFlowWizardMessage("Asignando facturas al cruce Dataverse...", "info");
        try {
            const response = await fetch(invoiceAssignUrl, {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify({
                    recordId,
                    invoiceRecordId: invoiceRecordIds[0] || "",
                    invoiceRecordIds
                })
            });
            const payload = await response.json().catch(() => ({}));
            if (!response.ok) {
                throw new Error(payload.detail || payload.message || "No fue posible asignar las facturas.");
            }

            const paymentRow = cashFlowWizardClientPayment.paymentRow;
            if (paymentRow) {
                const selected = cashFlowWizardClientPayment.selectedInvoices || [];
                paymentRow.dataset.dataverseInvoice = selected.map((item) => item.invoiceNumber).filter(Boolean).join(", ");
                paymentRow.dataset.dataverseClient = Array.from(new Set(selected.map((item) => item.clientName).filter(Boolean))).join(", ");
                updateRowStatus(paymentRow, payload.row, payload.row?.status || paymentRow.dataset.status || "");
            }
            cashFlowWizardClientPayment.status = payload.row?.status || cashFlowWizardClientPayment.status;
            cashFlowWizardClientPayment.selectedInvoices = [];
            renderCashFlowWizardClientPaymentLegacy(cashFlowWizardClientPayment.row, payload.message || "Facturas asignadas.", "success");
        } catch (error) {
            setCashFlowWizardMessage(error instanceof Error ? error.message : "Ocurrio un error inesperado.", "error");
        }
    };

    const approveCashFlowWizardClientPayment = async () => {
        if (!cashFlowWizardClientPayment?.row || !updatePaymentUrl) {
            setCashFlowWizardMessage("No se encontro la ruta para aprobar el cruce.", "error");
            return;
        }
        const recordId = getCashFlowWizardClientRecordId();
        if (!recordId) {
            setCashFlowWizardMessage("No hay cruce de entrada FV para aprobar.", "error");
            return;
        }

        const modal = ensureCashFlowWizardModal();
        const button = modal.querySelector("[data-cnc-wizard-client-approve]");
        if (button) {
            button.disabled = true;
        }
        setCashFlowWizardMessage("Aprobando cruce de entrada FV...", "info");
        try {
            const response = await fetch(updatePaymentUrl, {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify({
                    recordId,
                    status: "Aprobado",
                    reason: "Aprobado desde asistente de flujo de caja."
                })
            });
            const payload = await response.json().catch(() => ({}));
            if (!response.ok) {
                throw new Error(payload.detail || payload.message || "No fue posible aprobar el cruce.");
            }

            if (cashFlowWizardClientPayment.paymentRow) {
                updateRowStatus(cashFlowWizardClientPayment.paymentRow, payload.row, "Aprobado");
            }
            cashFlowWizardClientPayment.status = payload.row?.status || "Aprobado";
            updateCashFlowWizardClientActions();
            setCashFlowWizardMessage(payload.message || "Cruce aprobado. Ahora valida pre-Siigo.", "success");
        } catch (error) {
            setCashFlowWizardMessage(error instanceof Error ? error.message : "Ocurrio un error inesperado.", "error");
        } finally {
            if (button) {
                button.disabled = false;
            }
            updateCashFlowWizardClientActions();
        }
    };

    const preflightCashFlowWizardClientPayment = async () => {
        if (!cashFlowWizardClientPayment?.row || !preflightPaymentUrl) {
            setCashFlowWizardMessage("No se encontro la ruta para validar pre-Siigo.", "error");
            return;
        }
        const recordId = getCashFlowWizardClientRecordId();
        if (!recordId) {
            setCashFlowWizardMessage("No hay cruce de entrada FV para validar.", "error");
            return;
        }

        const modal = ensureCashFlowWizardModal();
        const button = modal.querySelector("[data-cnc-wizard-client-preflight]");
        if (button) {
            button.disabled = true;
        }
        cashFlowWizardClientPayment.issues = [];
        renderCashFlowWizardClientIssues();
        setCashFlowWizardMessage("Validando borrador pre-Siigo...", "info");
        try {
            const response = await fetch(preflightPaymentUrl, {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify({ recordId })
            });
            const payload = await response.json().catch(() => ({}));
            if (!response.ok) {
                throw new Error(payload.detail || payload.message || "No fue posible validar el borrador.");
            }

            cashFlowWizardClientPayment.issues = payload.issues || [];
            cashFlowWizardClientPayment.status = payload.row?.status || cashFlowWizardClientPayment.status;
            if (cashFlowWizardClientPayment.paymentRow) {
                updateRowStatus(cashFlowWizardClientPayment.paymentRow, payload.row, payload.row?.status || cashFlowWizardClientPayment.status || "");
                renderIssueList(cashFlowWizardClientPayment.paymentRow, "[data-preflight-issues]", payload.issues || []);
            }
            renderCashFlowWizardClientIssues();
            updateCashFlowWizardClientActions();
            setCashFlowWizardMessage(
                payload.message || "Validacion pre-Siigo finalizada.",
                payload.isReadyForSiigo && (payload.issues || []).length === 0 ? "success" : "info");
        } catch (error) {
            setCashFlowWizardMessage(error instanceof Error ? error.message : "Ocurrio un error inesperado.", "error");
        } finally {
            if (button) {
                button.disabled = false;
            }
            updateCashFlowWizardClientActions();
        }
    };

    const sendCashFlowWizardClientPaymentToSiigoLegacy = async () => {
        if (!cashFlowWizardClientPayment?.row || !sendPaymentUrl) {
            setCashFlowWizardMessage("No se encontro la ruta para enviar el comprobante.", "error");
            return;
        }
        const recordId = getCashFlowWizardClientRecordId();
        if (!recordId) {
            setCashFlowWizardMessage("No hay cruce de entrada FV para enviar.", "error");
            return;
        }

        const modal = ensureCashFlowWizardModal();
        const button = modal.querySelector("[data-cnc-wizard-client-send]");
        if (button) {
            button.disabled = true;
        }
        cashFlowWizardClientPayment.issues = [];
        cashFlowWizardClientPayment.payloadJson = "";
        cashFlowWizardClientPayment.responseJson = "";
        renderCashFlowWizardClientIssues();
        renderCashFlowWizardClientPreview();
        setCashFlowWizardMessage("Enviando comprobante de ingreso a Siigo...", "info");
        try {
            const response = await fetch(sendPaymentUrl, {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify({ recordId })
            });
            const payload = await response.json().catch(() => ({}));
            cashFlowWizardClientPayment.issues = payload.issues || [];
            cashFlowWizardClientPayment.payloadJson = payload.payloadJson || "";
            cashFlowWizardClientPayment.responseJson = payload.responseJson || "";
            renderCashFlowWizardClientIssues();
            renderCashFlowWizardClientPreview();
            if (!response.ok) {
                throw new Error(payload.detail || payload.message || "No fue posible enviar a Siigo.");
            }

            cashFlowWizardClientPayment.status = payload.row?.status || cashFlowWizardClientPayment.status;
            if (cashFlowWizardClientPayment.paymentRow) {
                updateRowStatus(cashFlowWizardClientPayment.paymentRow, payload.row, payload.row?.status || cashFlowWizardClientPayment.status || "");
                renderIssueList(cashFlowWizardClientPayment.paymentRow, "[data-siigo-send-issues]", payload.issues || []);
            }
            if (!payload.isSuccess) {
                setCashFlowWizardMessage(payload.message || "Siigo rechazo el comprobante. Revisa el detalle.", "error");
                updateCashFlowWizardClientActions();
                return;
            }

            completeCashFlowWizardRow(cashFlowWizardClientPayment.row, payload.row, payload.message || "Comprobante enviado a Siigo.");
        } catch (error) {
            setCashFlowWizardMessage(error instanceof Error ? error.message : "Ocurrio un error inesperado.", "error");
            if (button) {
                button.disabled = false;
            }
        }
    };

    const clientPaymentAllocationKey = (invoice) => [
        invoice?.customerId || invoice?.customerIdentification || invoice?.customerName || "",
        invoice?.id || invoice?.name || ""
    ].join("|");

    const roundClientPaymentMoney = (value) => Math.round((Number(value || 0) + Number.EPSILON) * 100) / 100;

    const getCashFlowWizardClientAllocationDraft = (invoice) => {
        if (!cashFlowWizardClientPayment) {
            return {
                paymentValue: 0,
                reteFuenteTaxId: 0,
                reteIcaTaxId: 0,
                rteIvaTaxId: 0,
                selected: false,
                dataverseSavedSignature: "",
                saving: false,
                sending: false,
                sendFailed: false
            };
        }

        const key = clientPaymentAllocationKey(invoice);
        const current = cashFlowWizardClientPayment.allocations?.[key];
        const draft = current && typeof current === "object"
            ? current
            : { paymentValue: Number(current || 0), reteFuenteTaxId: 0, reteIcaTaxId: 0, rteIvaTaxId: 0 };
        draft.paymentValue = Math.max(0, Number(draft.paymentValue || 0));
        draft.reteFuenteTaxId = Number(draft.reteFuenteTaxId || 0);
        draft.reteIcaTaxId = Number(draft.reteIcaTaxId || 0);
        draft.rteIvaTaxId = Number(draft.rteIvaTaxId || 0);
        draft.selected = Boolean(draft.selected);
        draft.dataverseSavedSignature = String(draft.dataverseSavedSignature || "");
        draft.saving = Boolean(draft.saving);
        draft.sending = Boolean(draft.sending);
        draft.sendFailed = Boolean(draft.sendFailed);
        draft.saveMessage = String(draft.saveMessage || "");
        cashFlowWizardClientPayment.allocations[key] = draft;
        return draft;
    };

    const findCashFlowWizardClientRetentionOption = (kind, taxId) => {
        const options = kind === "reteIca"
            ? cashFlowWizardClientPayment?.reteIcaOptions
            : kind === "rteIva"
                ? cashFlowWizardClientPayment?.rteIvaOptions
                : cashFlowWizardClientPayment?.reteFuenteOptions;
        return (options || []).find((option) => Number(option.taxId || 0) === Number(taxId || 0)) || null;
    };

    const calculateCashFlowWizardClientAllocation = (invoice) => {
        const draft = getCashFlowWizardClientAllocationDraft(invoice);
        const taxBase = Math.max(0, Number(invoice?.taxBase || 0));
        const vatBase = Math.max(0, Number(invoice?.vat || 0));
        const reteFuente = findCashFlowWizardClientRetentionOption("reteFuente", draft.reteFuenteTaxId);
        const reteIca = findCashFlowWizardClientRetentionOption("reteIca", draft.reteIcaTaxId);
        const rteIva = findCashFlowWizardClientRetentionOption("rteIva", draft.rteIvaTaxId);
        const reteFuenteValue = roundClientPaymentMoney(taxBase * Number(reteFuente?.rate || 0) / 100);
        const reteIcaValue = roundClientPaymentMoney(taxBase * Number(reteIca?.rate || 0) / 1000);
        const rteIvaValue = roundClientPaymentMoney(vatBase * Number(rteIva?.rate || 0) / 100);
        const retentionValue = roundClientPaymentMoney(reteFuenteValue + reteIcaValue + rteIvaValue);
        const tenderedValue = roundClientPaymentMoney(draft.paymentValue + retentionValue);
        const invoiceBalance = roundClientPaymentMoney(Number(invoice?.balance || 0));
        const closesInvoice = Math.abs(invoiceBalance - tenderedValue) <= clientPaymentDifferenceTolerance;
        const grossValue = closesInvoice ? invoiceBalance : tenderedValue;
        const adjustmentValue = closesInvoice
            ? roundClientPaymentMoney(grossValue - tenderedValue)
            : 0;
        const remainingBalance = roundClientPaymentMoney(invoiceBalance - grossValue);
        return {
            draft,
            reteFuenteValue,
            reteIcaValue,
            rteIvaValue,
            retentionValue,
            grossValue,
            adjustmentValue,
            remainingBalance
        };
    };

    const clientPaymentRetentionOptionsHtml = (options, selectedTaxId, emptyLabel) => [
        `<option value="">${escapeHtml(emptyLabel)}</option>`,
        ...(options || []).map((option) => `
            <option value="${escapeHtml(String(option.taxId || ""))}" ${Number(option.taxId || 0) === Number(selectedTaxId || 0) ? "selected" : ""} title="${escapeHtml(option.name || option.rateLabel || "")}">
                ${escapeHtml(option.rateLabel || option.name || "")}
            </option>`)
    ].join("");

    const cashFlowWizardClientAllocationSignature = (invoice) => {
        const calculation = calculateCashFlowWizardClientAllocation(invoice);
        return [
            invoice?.id || "",
            invoice?.name || "",
            invoice?.dataverseRecordId || "",
            invoice?.customerId || "",
            invoice?.customerIdentification || "",
            Number(invoice?.customerBranchOffice || 0),
            roundClientPaymentMoney(calculation.draft.paymentValue).toFixed(2),
            Number(calculation.draft.reteFuenteTaxId || 0),
            Number(calculation.draft.reteIcaTaxId || 0),
            Number(calculation.draft.rteIvaTaxId || 0)
        ].join("|");
    };

    const cashFlowWizardClientAllocationIsSaved = (invoice) => {
        const draft = getCashFlowWizardClientAllocationDraft(invoice);
        return Boolean(draft.dataverseSavedSignature)
            && draft.dataverseSavedSignature === cashFlowWizardClientAllocationSignature(invoice);
    };

    const invalidateCashFlowWizardClientAllocation = (invoice) => {
        const draft = getCashFlowWizardClientAllocationDraft(invoice);
        draft.dataverseSavedSignature = "";
        draft.sendFailed = false;
        draft.saveMessage = "";
    };

    const buildCashFlowWizardClientAllocation = (invoice) => {
        const calculation = calculateCashFlowWizardClientAllocation(invoice);
        return {
            documentId: invoice.id || "",
            documentName: invoice.name || "",
            dataverseRecordId: invoice.dataverseRecordId || "",
            customerId: invoice.customerId || "",
            customerIdentification: invoice.customerIdentification || "",
            customerName: invoice.customerName || "",
            customerBranchOffice: Number(invoice.customerBranchOffice || 0),
            appliedValue: calculation.draft.paymentValue,
            reteFuenteTaxId: calculation.draft.reteFuenteTaxId,
            reteIcaTaxId: calculation.draft.reteIcaTaxId,
            rteIvaTaxId: calculation.draft.rteIvaTaxId
        };
    };

    const getCashFlowWizardSelectedClientInvoices = () =>
        (cashFlowWizardClientPayment?.invoices || [])
            .filter((invoice) => getCashFlowWizardClientAllocationDraft(invoice).selected);

    const updateCashFlowWizardClientSummary = () => {
        if (!cashFlowWizardClientPayment) {
            return;
        }

        const modal = ensureCashFlowWizardModal();
        const summary = modal.querySelector("[data-cnc-wizard-client-summary]");
        const target = Number(cashFlowWizardClientPayment.row?.dataset.entryValue || 0);
        const calculations = (cashFlowWizardClientPayment.invoices || [])
            .map((invoice) => ({ invoice, calculation: calculateCashFlowWizardClientAllocation(invoice) }))
            .filter(({ calculation }) => calculation.draft.selected);
        const applied = roundClientPaymentMoney(calculations
            .reduce((total, item) => total + item.calculation.draft.paymentValue, 0));
        const retentions = roundClientPaymentMoney(calculations
            .reduce((total, item) => total + item.calculation.retentionValue, 0));
        const rteIva = roundClientPaymentMoney(calculations
            .reduce((total, item) => total + item.calculation.rteIvaValue, 0));
        const grossApplied = roundClientPaymentMoney(calculations
            .reduce((total, item) => total + item.calculation.grossValue, 0));
        const adjustment = roundClientPaymentMoney(grossApplied - target - retentions);
        const pending = target - applied;
        const hasOverAppliedInvoice = calculations.some((item) =>
            item.calculation.remainingBalance < -clientPaymentDifferenceTolerance);
        const balanced = Math.abs(pending) <= clientPaymentDifferenceTolerance
            && applied > 0
            && !hasOverAppliedInvoice;

        if (summary) {
            summary.classList.toggle("is-balanced", balanced);
            summary.classList.toggle("is-over", pending < -1 || hasOverAppliedInvoice);
            summary.innerHTML = [
                ["Facturas seleccionadas", String(calculations.length)],
                ["Movimiento", clientPaymentMoney(target)],
                ["Pago aplicado", clientPaymentMoney(applied)],
                ["Retenciones", clientPaymentMoney(retentions)],
                ["RteIVA incluida", clientPaymentMoney(rteIva)],
                ["Ajuste al peso", clientPaymentMoney(adjustment)],
                ["Cartera aplicada", clientPaymentMoney(grossApplied)],
                [Math.abs(pending) <= clientPaymentDifferenceTolerance
                    ? "Diferencia banco"
                    : pending < 0 ? "Exceso banco" : "Pendiente banco", clientPaymentMoney(Math.abs(pending))]
            ].map(([label, value]) => `
                <div>
                    <span>${escapeHtml(label)}</span>
                    <strong>${escapeHtml(value)}</strong>
                </div>`).join("");
        }

        const applyButton = modal.querySelector("[data-cnc-wizard-client-apply-selected]");
        if (applyButton) {
            const busy = calculations.some(({ calculation }) =>
                calculation.draft.saving || calculation.draft.sending);
            applyButton.disabled = calculations.length === 0
                || busy
                || Boolean(cashFlowWizardClientPayment.siigoCreated);
            applyButton.textContent = busy ? "Aplicando..." : "Aplicar";
            const pendingButton = modal.querySelector("[data-cnc-wizard-client-leave-pending]");
            if (pendingButton) {
                pendingButton.disabled = busy || Boolean(cashFlowWizardClientPayment.siigoCreated);
            }
            const omittedButton = modal.querySelector("[data-cnc-wizard-client-omitted]");
            if (omittedButton) {
                omittedButton.disabled = busy || Boolean(cashFlowWizardClientPayment.siigoCreated);
            }
        }
    };

    const renderCashFlowWizardClientCandidates = () => {
        const modal = ensureCashFlowWizardModal();
        const results = modal.querySelector("[data-cnc-wizard-client-customers]");
        const selected = modal.querySelector("[data-cnc-wizard-client-customer]");
        if (!results || !selected || !cashFlowWizardClientPayment) {
            return;
        }

        const customer = cashFlowWizardClientPayment.customer;
        selected.innerHTML = "";
        selected.hidden = !customer;
        if (customer) {
            const label = document.createElement("strong");
            const change = document.createElement("button");
            label.textContent = supplierPaymentLabel(customer);
            change.type = "button";
            change.className = "btn btn-sm btn-outline-secondary";
            change.textContent = "Cambiar";
            change.addEventListener("click", () => {
                closeAdditionalClientInvoiceModal();
                cashFlowWizardClientPayment.customer = null;
                cashFlowWizardClientPayment.candidates = [];
                cashFlowWizardClientPayment.invoices = [];
                cashFlowWizardClientPayment.paidInvoices = [];
                cashFlowWizardClientPayment.reteFuenteOptions = [];
                cashFlowWizardClientPayment.reteIcaOptions = [];
                cashFlowWizardClientPayment.rteIvaOptions = [];
                cashFlowWizardClientPayment.allocations = {};
                const query = modal.querySelector("[data-cnc-wizard-client-customer-query]");
                if (query) {
                    query.value = "";
                    query.focus();
                }
                renderCashFlowWizardClientCandidates();
                renderCashFlowWizardClientInvoices();
            });
            selected.append(label, change);
        }

        results.innerHTML = "";
        const candidates = customer ? [] : (cashFlowWizardClientPayment.candidates || []);
        results.hidden = candidates.length === 0;
        candidates.forEach((candidate) => {
            const button = document.createElement("button");
            const title = document.createElement("strong");
            const detail = document.createElement("small");
            button.type = "button";
            button.className = "cnc-party-picker__option";
            title.textContent = candidate.commercialName || candidate.name || candidate.displayName || "Cliente Siigo";
            detail.textContent = [candidate.identification, Number(candidate.branchOffice || 0) > 0 ? `Sucursal ${candidate.branchOffice}` : ""]
                .filter(Boolean)
                .join(" - ");
            button.append(title, detail);
            button.addEventListener("click", () => selectCashFlowWizardClientCustomer(candidate));
            results.appendChild(button);
        });
    };

    const searchCashFlowWizardClientCustomers = async (query) => {
        if (!cashFlowWizardClientPayment || !siigoCustomerSearchUrl || query.length < 2) {
            return;
        }

        const sequence = Number(cashFlowWizardClientPayment.searchSequence || 0) + 1;
        cashFlowWizardClientPayment.searchSequence = sequence;
        try {
            const response = await fetch(siigoCustomerSearchUrl, {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify({ query, top: 10 })
            });
            const payload = await response.json().catch(() => ({}));
            if (!response.ok) {
                const errorMessage = resolveSiigoFailureMessage(payload, "No fue posible buscar clientes.");
                if (isTransientSiigoFailure(payload.isTransientSiigoFailure, errorMessage)) {
                    showTransientSiigoFailure();
                }
                throw new Error(errorMessage);
            }
            if (!cashFlowWizardClientPayment || cashFlowWizardClientPayment.searchSequence !== sequence) {
                return;
            }

            cashFlowWizardClientPayment.candidates = payload.items || [];
            renderCashFlowWizardClientCandidates();
        } catch (error) {
            if (cashFlowWizardClientPayment?.searchSequence === sequence) {
                cashFlowWizardClientPayment.candidates = [];
                renderCashFlowWizardClientCandidates();
                setCashFlowWizardMessage(error instanceof Error ? error.message : "No fue posible buscar clientes.", "error");
            }
        }
    };

    const scheduleCashFlowWizardClientSearch = (query) => {
        if (!cashFlowWizardClientPayment) {
            return;
        }
        window.clearTimeout(cashFlowWizardClientPayment.searchTimer || 0);
        if (query.length < 2) {
            cashFlowWizardClientPayment.candidates = [];
            renderCashFlowWizardClientCandidates();
            return;
        }
        cashFlowWizardClientPayment.searchTimer = window.setTimeout(
            () => searchCashFlowWizardClientCustomers(query),
            280);
    };

    const selectCashFlowWizardClientCustomer = (customer) => {
        if (!cashFlowWizardClientPayment) {
            return;
        }
        closeAdditionalClientInvoiceModal();
        const modal = ensureCashFlowWizardModal();
        cashFlowWizardClientPayment.searchSequence = Number(cashFlowWizardClientPayment.searchSequence || 0) + 1;
        cashFlowWizardClientPayment.customer = customer;
        cashFlowWizardClientPayment.candidates = [];
        cashFlowWizardClientPayment.invoices = [];
        cashFlowWizardClientPayment.paidInvoices = [];
        cashFlowWizardClientPayment.reteFuenteOptions = [];
        cashFlowWizardClientPayment.reteIcaOptions = [];
        cashFlowWizardClientPayment.rteIvaOptions = [];
        cashFlowWizardClientPayment.allocations = {};
        const query = modal.querySelector("[data-cnc-wizard-client-customer-query]");
        if (query) {
            query.value = supplierPaymentLabel(customer);
        }
        renderCashFlowWizardClientCandidates();
        loadCashFlowWizardClientInvoices();
    };

    const mergeClientPaymentRetentionOptions = (current, incoming) => {
        const byId = new Map((current || []).map((option) => [Number(option.taxId || 0), option]));
        (incoming || []).forEach((option) => {
            const key = Number(option.taxId || 0);
            if (key > 0 && !byId.has(key)) {
                byId.set(key, option);
            }
        });
        return Array.from(byId.values());
    };

    const cancelAdditionalClientCustomerSearch = (state) => {
        if (!state) {
            return;
        }
        window.clearTimeout(state.searchTimer || 0);
        state.searchTimer = 0;
        state.searchController?.abort();
        state.searchController = null;
        state.searchSequence = Number(state.searchSequence || 0) + 1;
        state.loadingCustomers = false;
    };

    const cancelAdditionalClientInvoiceSearch = (state) => {
        if (!state) {
            return;
        }
        state.invoiceSearchController?.abort();
        state.invoiceSearchController = null;
        state.invoiceSearchSequence = Number(state.invoiceSearchSequence || 0) + 1;
        state.loadingInvoices = false;
    };

    const closeAdditionalClientInvoiceModal = () => {
        const modal = document.getElementById("cncAdditionalClientInvoiceModal");
        if (modal) {
            modal.hidden = true;
        }
        if (cashFlowWizardAdditionalCustomer) {
            cancelAdditionalClientCustomerSearch(cashFlowWizardAdditionalCustomer);
            cancelAdditionalClientInvoiceSearch(cashFlowWizardAdditionalCustomer);
        }
        cashFlowWizardAdditionalCustomer = null;
    };

    const additionalClientInvoiceAlreadyAdded = (invoice) =>
        (cashFlowWizardClientPayment?.invoices || []).some((current) =>
            clientPaymentAllocationKey(current) === clientPaymentAllocationKey(invoice));

    const addAdditionalClientInvoice = (invoice) => {
        if (!cashFlowWizardClientPayment || !invoice || additionalClientInvoiceAlreadyAdded(invoice)) {
            return;
        }
        cashFlowWizardClientPayment.invoices.push({
            ...invoice,
            isAdditionalCustomer: true
        });
        renderCashFlowWizardClientInvoices();
        renderAdditionalClientInvoiceModal();
        setCashFlowWizardMessage(
            `${invoice.name || "La factura"} de ${invoice.customerName || "la otra razón social"} fue añadida al pago.`,
            "success");
    };

    const renderAdditionalClientInvoiceModal = () => {
        const modal = document.getElementById("cncAdditionalClientInvoiceModal");
        const state = cashFlowWizardAdditionalCustomer;
        if (!modal || !state) {
            return;
        }
        const candidates = modal.querySelector("[data-cnc-additional-client-candidates]");
        const selected = modal.querySelector("[data-cnc-additional-client-selected]");
        const invoices = modal.querySelector("[data-cnc-additional-client-invoices]");
        const message = modal.querySelector("[data-cnc-additional-client-message]");
        if (candidates) {
            candidates.innerHTML = "";
            candidates.hidden = state.loadingCustomers || state.candidates.length === 0 || Boolean(state.customer);
            state.candidates.forEach((candidate) => {
                const button = document.createElement("button");
                button.type = "button";
                button.className = "cnc-party-picker__option";
                button.innerHTML = `
                    <strong>${escapeHtml(candidate.commercialName || candidate.name || candidate.displayName || "Cliente Siigo")}</strong>
                    <small>${escapeHtml([candidate.identification, Number(candidate.branchOffice || 0) > 0 ? `Sucursal ${candidate.branchOffice}` : ""].filter(Boolean).join(" - "))}</small>`;
                button.addEventListener("click", () => selectAdditionalClientCustomer(candidate));
                candidates.appendChild(button);
            });
        }
        if (selected) {
            selected.hidden = !state.customer;
            selected.innerHTML = state.customer
                ? `<strong>${escapeHtml(supplierPaymentLabel(state.customer))}</strong><button type="button" class="btn btn-sm btn-outline-secondary" data-cnc-additional-client-change>Cambiar</button>`
                : "";
            selected.querySelector("[data-cnc-additional-client-change]")?.addEventListener("click", () => {
                cancelAdditionalClientCustomerSearch(state);
                cancelAdditionalClientInvoiceSearch(state);
                state.customer = null;
                state.invoices = [];
                state.candidates = [];
                state.message = "";
                state.messageTone = "";
                const query = modal.querySelector("[data-cnc-additional-client-query]");
                if (query) {
                    query.value = "";
                    query.focus();
                }
                renderAdditionalClientInvoiceModal();
            });
        }
        if (invoices) {
            invoices.innerHTML = "";
            if (state.loadingInvoices || (state.customer && state.invoices.length === 0)) {
                const empty = document.createElement("p");
                empty.className = "cnc-payment-empty";
                empty.textContent = state.loadingInvoices ? "Consultando facturas abiertas..." : "Esta razón social no tiene facturas abiertas.";
                invoices.appendChild(empty);
            }
            state.invoices.forEach((invoice) => {
                const card = document.createElement("div");
                const added = additionalClientInvoiceAlreadyAdded(invoice);
                card.className = "cnc-additional-client-invoice";
                card.innerHTML = `
                    <div>
                        <strong>${escapeHtml(invoice.name || "Factura")}</strong>
                        <small>${escapeHtml(invoice.dateDisplay || invoice.dateValue || "")} · saldo ${escapeHtml(clientPaymentMoney(invoice.balance || 0))}</small>
                    </div>
                    <button type="button" class="btn btn-sm ${added ? "btn-outline-secondary" : "btn-outline-primary"}" ${added ? "disabled" : ""}>
                        ${added ? "Añadida" : "Añadir"}
                    </button>`;
                card.querySelector("button")?.addEventListener("click", () => addAdditionalClientInvoice(invoice));
                invoices.appendChild(card);
            });
        }
        if (message) {
            message.textContent = state.message || "";
            message.hidden = !state.message;
            if (state.messageTone) {
                message.dataset.tone = state.messageTone;
            } else {
                message.removeAttribute("data-tone");
            }
        }
    };

    const searchAdditionalClientCustomers = async (query, sequence) => {
        const state = cashFlowWizardAdditionalCustomer;
        if (!state || query.length < 2 || state.searchSequence !== sequence) {
            return;
        }
        if (!siigoCustomerSearchUrl) {
            state.loadingCustomers = false;
            state.message = "No está disponible la búsqueda de clientes en Siigo.";
            state.messageTone = "error";
            renderAdditionalClientInvoiceModal();
            return;
        }

        const controller = new AbortController();
        state.searchController = controller;
        try {
            const response = await fetch(siigoCustomerSearchUrl, {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify({ query, top: 10 }),
                signal: controller.signal
            });
            const payload = await response.json().catch(() => ({}));
            if (!response.ok) {
                throw new Error(resolveSiigoFailureMessage(payload, "No fue posible buscar la otra razón social."));
            }
            if (cashFlowWizardAdditionalCustomer !== state || state.searchSequence !== sequence) {
                return;
            }
            const primaryId = String(cashFlowWizardClientPayment?.customer?.id || "");
            state.candidates = (payload.items || []).filter((candidate) => String(candidate.id || "") !== primaryId);
            state.message = state.candidates.length === 0 ? "No encontramos otra razón social con ese texto." : "";
            state.messageTone = "";
        } catch (error) {
            if (error?.name === "AbortError") {
                return;
            }
            if (cashFlowWizardAdditionalCustomer === state && state.searchSequence === sequence) {
                state.candidates = [];
                state.message = error instanceof Error ? error.message : "No fue posible buscar la otra razón social.";
                state.messageTone = "error";
            }
        } finally {
            if (cashFlowWizardAdditionalCustomer === state && state.searchSequence === sequence) {
                state.loadingCustomers = false;
                if (state.searchController === controller) {
                    state.searchController = null;
                }
                renderAdditionalClientInvoiceModal();
            }
        }
    };

    const scheduleAdditionalClientCustomerSearch = (query) => {
        const state = cashFlowWizardAdditionalCustomer;
        if (!state) {
            return;
        }
        cancelAdditionalClientCustomerSearch(state);
        cancelAdditionalClientInvoiceSearch(state);
        state.customer = null;
        state.invoices = [];
        state.candidates = [];
        state.messageTone = "";
        if (query.length < 2) {
            state.message = query.length > 0 ? "Escribe al menos dos caracteres para buscar." : "";
            renderAdditionalClientInvoiceModal();
            return;
        }
        const sequence = state.searchSequence;
        state.loadingCustomers = true;
        state.message = "Buscando clientes en Siigo...";
        renderAdditionalClientInvoiceModal();
        state.searchTimer = window.setTimeout(() => searchAdditionalClientCustomers(query, sequence), 280);
    };

    const selectAdditionalClientCustomer = async (customer) => {
        const state = cashFlowWizardAdditionalCustomer;
        if (!state || !clientPaymentInvoicesUrl || !cashFlowWizardClientPayment?.row) {
            return;
        }
        cancelAdditionalClientCustomerSearch(state);
        cancelAdditionalClientInvoiceSearch(state);
        const invoiceSearchSequence = state.invoiceSearchSequence;
        const invoiceSearchController = new AbortController();
        state.invoiceSearchController = invoiceSearchController;
        state.customer = customer;
        state.candidates = [];
        state.invoices = [];
        state.loadingInvoices = true;
        state.message = "";
        state.messageTone = "";
        const modal = document.getElementById("cncAdditionalClientInvoiceModal");
        const query = modal?.querySelector("[data-cnc-additional-client-query]");
        if (query) {
            query.value = supplierPaymentLabel(customer);
        }
        renderAdditionalClientInvoiceModal();
        try {
            const response = await fetch(clientPaymentInvoicesUrl, {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify({
                    recordId: cashFlowWizardClientPayment.row.dataset.recordId || "",
                    movementExternalKey: cashFlowWizardClientPayment.row.dataset.movementExternalKey || "",
                    customerId: customer.id || "",
                    customerQuery: customer.identification || customer.displayName || customer.name || "",
                    lookbackMonths: 60
                }),
                signal: invoiceSearchController.signal
            });
            const payload = await response.json().catch(() => ({}));
            if (!response.ok) {
                throw new Error(resolveSiigoFailureMessage(payload, "No fue posible consultar las facturas de la otra razón social."));
            }
            if (cashFlowWizardAdditionalCustomer !== state
                || state.invoiceSearchSequence !== invoiceSearchSequence) {
                return;
            }
            state.customer = payload.customer || customer;
            state.invoices = (payload.invoices || []).map((invoice) => ({
                ...invoice,
                customerId: invoice.customerId || state.customer.id || "",
                customerIdentification: invoice.customerIdentification || state.customer.identification || "",
                customerName: invoice.customerName || state.customer.displayName || state.customer.name || "",
                customerBranchOffice: Number(invoice.customerBranchOffice || state.customer.branchOffice || 0),
                isAdditionalCustomer: true
            }));
            cashFlowWizardClientPayment.reteFuenteOptions = mergeClientPaymentRetentionOptions(
                cashFlowWizardClientPayment.reteFuenteOptions,
                payload.reteFuenteOptions);
            cashFlowWizardClientPayment.reteIcaOptions = mergeClientPaymentRetentionOptions(
                cashFlowWizardClientPayment.reteIcaOptions,
                payload.reteIcaOptions);
            cashFlowWizardClientPayment.rteIvaOptions = mergeClientPaymentRetentionOptions(
                cashFlowWizardClientPayment.rteIvaOptions,
                payload.rteIvaOptions);
        } catch (error) {
            if (error?.name === "AbortError") {
                return;
            }
            if (cashFlowWizardAdditionalCustomer === state
                && state.invoiceSearchSequence === invoiceSearchSequence) {
                state.invoices = [];
                state.message = error instanceof Error ? error.message : "No fue posible consultar las facturas de la otra razón social.";
                state.messageTone = "error";
            }
        } finally {
            if (cashFlowWizardAdditionalCustomer === state
                && state.invoiceSearchSequence === invoiceSearchSequence) {
                state.loadingInvoices = false;
                if (state.invoiceSearchController === invoiceSearchController) {
                    state.invoiceSearchController = null;
                }
                renderAdditionalClientInvoiceModal();
            }
        }
    };

    const ensureAdditionalClientInvoiceModal = () => {
        let modal = document.getElementById("cncAdditionalClientInvoiceModal");
        if (modal) {
            return modal;
        }
        modal = document.createElement("div");
        modal.id = "cncAdditionalClientInvoiceModal";
        modal.className = "cnc-modal cnc-additional-client-modal";
        modal.hidden = true;
        modal.innerHTML = `
            <div class="cnc-modal__panel cnc-additional-client-modal__dialog" role="dialog" aria-modal="true" aria-labelledby="cncAdditionalClientTitle">
                <div class="cnc-modal__header">
                    <div>
                        <div class="cnc-kicker">Pago de cliente</div>
                        <h2 id="cncAdditionalClientTitle">Añadir factura de otra razón social</h2>
                    </div>
                    <button type="button" class="btn btn-sm btn-outline-secondary" data-cnc-additional-client-close aria-label="Cerrar">Cerrar</button>
                </div>
                <p class="cnc-modal__description">Busca el otro cliente en Siigo y añade únicamente las facturas que forman parte de este pago.</p>
                <div class="cnc-party-picker">
                    <label class="cnc-modal__field">
                        <span>Otra razón social</span>
                        <input class="form-control" type="search" autocomplete="off" data-cnc-additional-client-query placeholder="Nombre o NIT" />
                    </label>
                    <div class="cnc-party-picker__results" data-cnc-additional-client-candidates hidden></div>
                </div>
                <div class="cnc-party-picker__selected" data-cnc-additional-client-selected hidden></div>
                <div class="cnc-additional-client-invoices" data-cnc-additional-client-invoices></div>
                <p class="cnc-modal__feedback" data-cnc-additional-client-message role="status" aria-live="polite" hidden></p>
                <div class="cnc-modal__actions">
                    <button type="button" class="btn btn-outline-secondary" data-cnc-additional-client-close>Listo</button>
                </div>
            </div>`;
        document.body.appendChild(modal);
        modal.querySelectorAll("[data-cnc-additional-client-close]")
            .forEach((button) => button.addEventListener("click", closeAdditionalClientInvoiceModal));
        modal.addEventListener("click", (event) => {
            if (event.target === modal) {
                closeAdditionalClientInvoiceModal();
            }
        });
        const query = modal.querySelector("[data-cnc-additional-client-query]");
        query?.addEventListener("input", () => scheduleAdditionalClientCustomerSearch(String(query.value || "").trim()));
        return modal;
    };

    const openAdditionalClientInvoiceModal = () => {
        if (!cashFlowWizardClientPayment?.customer) {
            setCashFlowWizardMessage("Selecciona primero el cliente principal.", "info");
            return;
        }
        cashFlowWizardAdditionalCustomer = {
            customer: null,
            candidates: [],
            invoices: [],
            loadingCustomers: false,
            loadingInvoices: false,
            searchSequence: 0,
            searchTimer: 0,
            searchController: null,
            invoiceSearchSequence: 0,
            invoiceSearchController: null,
            message: "",
            messageTone: ""
        };
        const modal = ensureAdditionalClientInvoiceModal();
        const query = modal.querySelector("[data-cnc-additional-client-query]");
        if (query) {
            query.value = "";
        }
        modal.hidden = false;
        renderAdditionalClientInvoiceModal();
        window.setTimeout(() => query?.focus(), 0);
    };

    const renderCashFlowWizardClientInvoices = () => {
        const modal = ensureCashFlowWizardModal();
        const wrap = modal.querySelector("[data-cnc-wizard-client-invoice-wrap]");
        const body = modal.querySelector("[data-cnc-wizard-client-invoices]");
        const paidWrap = modal.querySelector("[data-cnc-wizard-client-paid-wrap]");
        const paidBody = modal.querySelector("[data-cnc-wizard-client-paid-invoices]");
        if (!wrap || !body || !cashFlowWizardClientPayment) {
            return;
        }

        const invoices = cashFlowWizardClientPayment.invoices || [];
        const paidInvoices = cashFlowWizardClientPayment.paidInvoices || [];
        wrap.hidden = !cashFlowWizardClientPayment.customer;
        body.innerHTML = "";
        if (cashFlowWizardClientPayment.loadingInvoices || invoices.length === 0) {
            const row = document.createElement("tr");
            const cell = document.createElement("td");
            cell.colSpan = 9;
            cell.className = "cnc-payment-empty";
            cell.textContent = cashFlowWizardClientPayment.loadingInvoices ? "Consultando saldos..." : "Sin facturas con saldo.";
            row.appendChild(cell);
            body.appendChild(row);
        }

        invoices.forEach((invoice) => {
            const calculation = calculateCashFlowWizardClientAllocation(invoice);
            const draft = calculation.draft;
            const dueReady = invoice.hasExactDueReference === true;
            const invoiceCustomerName = invoice.customerName
                || cashFlowWizardClientPayment.customer?.displayName
                || cashFlowWizardClientPayment.customer?.name
                || "Cliente Siigo";
            const additionalCustomer = Boolean(invoice.isAdditionalCustomer);
            const grossTotal = Number(invoice.total || invoice.balance || 0);
            const outstandingBalance = Number(invoice.balance || grossTotal);
            const partialBalance = Math.abs(grossTotal - outstandingBalance) > 1
                ? `<small>Pendiente ${escapeHtml(clientPaymentMoney(outstandingBalance))}</small>`
                : "";
            const row = document.createElement("tr");
            row.className = "cnc-payment-allocation-row";
            row.classList.toggle("is-applied", calculation.grossValue > 0);
            row.classList.toggle("is-saved", cashFlowWizardClientAllocationIsSaved(invoice));
            row.classList.toggle("is-selected", draft.selected);
            row.classList.toggle("is-blocked", !dueReady);
            const dueDetail = dueReady
                ? `<small>Vencimiento ${escapeHtml(invoice.duePrefix || "")} · cuota ${escapeHtml(String(invoice.dueQuote || 1))} · ${escapeHtml(invoice.dueDateDisplay || invoice.dueDateValue || "")}</small>`
                : `<small class="cnc-payment-due-warning">${escapeHtml(invoice.dueReferenceIssue || "No fue posible confirmar el vencimiento existente en Siigo.")}</small>`;
            row.innerHTML = `
                <td data-label="Factura">
                    <strong>${escapeHtml(invoice.name || "Sin numero")}</strong>
                    <span class="cnc-client-invoice-party ${additionalCustomer ? "is-additional" : ""}">${escapeHtml(invoiceCustomerName)}</span>
                    ${additionalCustomer ? '<button type="button" class="cnc-client-invoice-remove" data-cnc-client-remove-additional>Quitar</button>' : ""}
                    <small>Base ${escapeHtml(clientPaymentMoney(invoice.taxBase || 0))}</small>
                    <small>IVA ${escapeHtml(clientPaymentMoney(invoice.vat || 0))}</small>
                    ${dueDetail}
                </td>
                <td data-label="Fecha">${escapeHtml(invoice.dateDisplay || invoice.dateValue || "")}</td>
                <td class="text-end" data-label="Total bruto"><strong>${escapeHtml(clientPaymentMoney(grossTotal))}</strong>${partialBalance}</td>
                <td data-label="Pago">
                    <input class="form-control cnc-payment-value-input" type="number" min="0" max="${escapeHtml(String(invoice.balance || 0))}" step="0.01" value="${draft.paymentValue > 0 ? escapeHtml(String(draft.paymentValue)) : ""}" aria-label="Pago aplicado a ${escapeHtml(invoice.name || "factura")}" ${dueReady ? "" : "disabled"} data-cnc-client-payment-value />
                </td>
                <td data-label="ReteFuente">
                    <div class="cnc-retention-editor">
                        <select class="form-select" aria-label="ReteFuente de ${escapeHtml(invoice.name || "factura")}" ${dueReady ? "" : "disabled"} data-cnc-client-rete-fuente>
                            ${clientPaymentRetentionOptionsHtml(cashFlowWizardClientPayment.reteFuenteOptions, draft.reteFuenteTaxId, "Sin retefuente")}
                        </select>
                        <small data-cnc-client-rete-fuente-value>${escapeHtml(clientPaymentMoney(calculation.reteFuenteValue))}</small>
                    </div>
                </td>
                <td data-label="ReteICA">
                    <div class="cnc-retention-editor">
                        <select class="form-select" aria-label="ReteICA de ${escapeHtml(invoice.name || "factura")}" ${dueReady ? "" : "disabled"} data-cnc-client-rete-ica>
                            ${clientPaymentRetentionOptionsHtml(cashFlowWizardClientPayment.reteIcaOptions, draft.reteIcaTaxId, "Sin ReteICA")}
                        </select>
                        <small data-cnc-client-rete-ica-value>${escapeHtml(clientPaymentMoney(calculation.reteIcaValue))}</small>
                    </div>
                </td>
                <td data-label="RteIVA">
                    <div class="cnc-retention-editor">
                        <select class="form-select" aria-label="RteIVA de ${escapeHtml(invoice.name || "factura")}" ${dueReady ? "" : "disabled"} data-cnc-client-rete-iva>
                            ${clientPaymentRetentionOptionsHtml(cashFlowWizardClientPayment.rteIvaOptions, draft.rteIvaTaxId, "Sin RteIVA")}
                        </select>
                        <small data-cnc-client-rete-iva-value>${escapeHtml(clientPaymentMoney(calculation.rteIvaValue))}</small>
                    </div>
                </td>
                <td class="text-end cnc-payment-balance" data-label="Saldo final" data-cnc-client-remaining>${escapeHtml(clientPaymentMoney(calculation.remainingBalance))}</td>
                <td data-label="Seleccionar" class="cnc-payment-apply-cell">
                    <input class="form-check-input" type="checkbox" ${draft.selected ? "checked" : ""} ${dueReady ? "" : "disabled"} aria-label="Seleccionar ${escapeHtml(invoice.name || "factura")}" data-cnc-client-select />
                    <small data-cnc-client-save-status></small>
                </td>`;
            const input = row.querySelector("[data-cnc-client-payment-value]");
            const reteFuenteSelect = row.querySelector("[data-cnc-client-rete-fuente]");
            const reteIcaSelect = row.querySelector("[data-cnc-client-rete-ica]");
            const rteIvaSelect = row.querySelector("[data-cnc-client-rete-iva]");
            const select = row.querySelector("[data-cnc-client-select]");
            const removeAdditional = row.querySelector("[data-cnc-client-remove-additional]");
            const saveStatus = row.querySelector("[data-cnc-client-save-status]");
            const updateRow = () => {
                const current = calculateCashFlowWizardClientAllocation(invoice);
                const saved = cashFlowWizardClientAllocationIsSaved(invoice);
                row.classList.toggle("is-applied", current.grossValue > 0);
                row.classList.toggle("is-over", current.remainingBalance < -1);
                row.classList.toggle("is-saved", saved);
                row.classList.toggle("is-selected", current.draft.selected);
                const reteFuenteValue = row.querySelector("[data-cnc-client-rete-fuente-value]");
                const reteIcaValue = row.querySelector("[data-cnc-client-rete-ica-value]");
                const rteIvaValue = row.querySelector("[data-cnc-client-rete-iva-value]");
                const remaining = row.querySelector("[data-cnc-client-remaining]");
                reteFuenteValue && (reteFuenteValue.textContent = clientPaymentMoney(current.reteFuenteValue));
                reteIcaValue && (reteIcaValue.textContent = clientPaymentMoney(current.reteIcaValue));
                rteIvaValue && (rteIvaValue.textContent = clientPaymentMoney(current.rteIvaValue));
                if (remaining) {
                    remaining.textContent = clientPaymentMoney(current.remainingBalance);
                    remaining.classList.toggle("is-zero", Math.abs(current.remainingBalance) <= 1);
                    remaining.classList.toggle("is-over", current.remainingBalance < -1);
                }
                if (select) {
                    select.checked = current.draft.selected;
                    select.disabled = !dueReady
                        || current.draft.saving
                        || current.draft.sending
                        || Boolean(cashFlowWizardClientPayment?.siigoCreated);
                }
                if (saveStatus) {
                    saveStatus.textContent = current.draft.saving
                        ? "Dataverse"
                        : current.draft.sending
                            ? "Siigo"
                            : current.draft.sendFailed ? "Envio pendiente" : saved ? "Dataverse OK" : current.draft.saveMessage;
                }
                updateCashFlowWizardClientSummary();
            };
            input?.addEventListener("input", () => {
                const normalized = Math.max(0, Number(input.value || 0));
                draft.paymentValue = Number.isFinite(normalized) ? normalized : 0;
                invalidateCashFlowWizardClientAllocation(invoice);
                updateRow();
            });
            reteFuenteSelect?.addEventListener("change", () => {
                draft.reteFuenteTaxId = Number(reteFuenteSelect.value || 0);
                invalidateCashFlowWizardClientAllocation(invoice);
                updateRow();
            });
            reteIcaSelect?.addEventListener("change", () => {
                draft.reteIcaTaxId = Number(reteIcaSelect.value || 0);
                invalidateCashFlowWizardClientAllocation(invoice);
                updateRow();
            });
            rteIvaSelect?.addEventListener("change", () => {
                draft.rteIvaTaxId = Number(rteIvaSelect.value || 0);
                invalidateCashFlowWizardClientAllocation(invoice);
                updateRow();
            });
            select?.addEventListener("change", () => {
                draft.selected = Boolean(select.checked);
                draft.saveMessage = "";
                updateRow();
            });
            removeAdditional?.addEventListener("click", () => {
                const key = clientPaymentAllocationKey(invoice);
                cashFlowWizardClientPayment.invoices = cashFlowWizardClientPayment.invoices
                    .filter((candidate) => candidate !== invoice);
                delete cashFlowWizardClientPayment.allocations[key];
                renderCashFlowWizardClientInvoices();
            });
            updateRow();
            body.appendChild(row);
        });

        if (paidWrap && paidBody) {
            paidWrap.hidden = cashFlowWizardClientPayment.loadingInvoices || paidInvoices.length === 0;
            paidBody.innerHTML = paidInvoices.map((invoice) => {
                const reteFuente = Number(invoice.reteFuenteValue || 0) > 0
                    ? `<strong>${escapeHtml(`${Number(invoice.reteFuenteRate || 0).toLocaleString("es-CO", { maximumFractionDigits: 4 })}%`)}</strong><small>${escapeHtml(clientPaymentMoney(invoice.reteFuenteValue))}</small>`
                    : "<span>-</span>";
                const reteIca = Number(invoice.reteIcaValue || 0) > 0
                    ? `<strong>${escapeHtml(`${Number(invoice.reteIcaRate || 0).toLocaleString("es-CO", { maximumFractionDigits: 4 })} x mil`)}</strong><small>${escapeHtml(clientPaymentMoney(invoice.reteIcaValue))}</small>`
                    : "<span>-</span>";
                const rteIva = Number(invoice.rteIvaValue || 0) > 0
                    ? `<strong>${escapeHtml(`${Number(invoice.rteIvaRate || 0).toLocaleString("es-CO", { maximumFractionDigits: 4 })}%`)}</strong><small>${escapeHtml(clientPaymentMoney(invoice.rteIvaValue))}</small>`
                    : "<span>-</span>";
                return `
                    <tr>
                        <td data-label="Factura"><strong>${escapeHtml(invoice.name || "Sin numero")}</strong></td>
                        <td data-label="Pago">${escapeHtml(invoice.paymentDateDisplay || invoice.invoiceDateDisplay || "-")}</td>
                        <td class="text-end" data-label="Total bruto">${escapeHtml(clientPaymentMoney(invoice.total || 0))}</td>
                        <td class="cnc-paid-retention" data-label="ReteFuente">${reteFuente}</td>
                        <td class="cnc-paid-retention" data-label="ReteICA">${reteIca}</td>
                        <td class="cnc-paid-retention" data-label="RteIVA">${rteIva}</td>
                    </tr>`;
            }).join("");
        }
        updateCashFlowWizardClientSummary();
    };

    const loadCashFlowWizardClientInvoices = async () => {
        if (!cashFlowWizardClientPayment?.customer || !clientPaymentInvoicesUrl) {
            return;
        }

        const state = cashFlowWizardClientPayment;
        state.loadingInvoices = true;
        state.invoices = [];
        state.paidInvoices = [];
        state.reteFuenteOptions = [];
        state.reteIcaOptions = [];
        state.rteIvaOptions = [];
        state.allocations = {};
        renderCashFlowWizardClientInvoices();
        setCashFlowWizardMessage("", "info");
        try {
            const response = await fetch(clientPaymentInvoicesUrl, {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify({
                    recordId: state.row?.dataset.recordId || "",
                    movementExternalKey: state.row?.dataset.movementExternalKey || "",
                    customerId: state.customer.id || "",
                    customerQuery: state.customer.identification || state.customer.displayName || "",
                    lookbackMonths: 60
                })
            });
            const payload = await response.json().catch(() => ({}));
            if (!response.ok) {
                const errorMessage = resolveSiigoFailureMessage(payload, "No fue posible consultar las facturas.");
                if (isTransientSiigoFailure(payload.isTransientSiigoFailure, errorMessage)) {
                    showTransientSiigoFailure();
                }
                throw new Error(errorMessage);
            }
            if (cashFlowWizardClientPayment !== state) {
                return;
            }
            state.customer = payload.customer || state.customer;
            state.invoices = (payload.invoices || []).map((invoice) => ({
                ...invoice,
                customerId: invoice.customerId || state.customer?.id || "",
                customerIdentification: invoice.customerIdentification || state.customer?.identification || "",
                customerName: invoice.customerName || state.customer?.displayName || state.customer?.name || "",
                customerBranchOffice: Number(invoice.customerBranchOffice || state.customer?.branchOffice || 0),
                isAdditionalCustomer: false
            }));
            state.paidInvoices = payload.paidInvoices || [];
            state.reteFuenteOptions = payload.reteFuenteOptions || [];
            state.reteIcaOptions = payload.reteIcaOptions || [];
            state.rteIvaOptions = payload.rteIvaOptions || [];
        } catch (error) {
            state.invoices = [];
            state.paidInvoices = [];
            state.reteFuenteOptions = [];
            state.reteIcaOptions = [];
            state.rteIvaOptions = [];
            setCashFlowWizardMessage(error instanceof Error ? error.message : "No fue posible consultar las facturas.", "error");
        } finally {
            if (cashFlowWizardClientPayment === state) {
                state.loadingInvoices = false;
                renderCashFlowWizardClientCandidates();
                renderCashFlowWizardClientInvoices();
            }
        }
    };

    const applyCashFlowWizardSelectedClientPayments = async () => {
        if (!cashFlowWizardClientPayment?.customer
            || !clientPaymentDataverseApplyUrl
            || !clientPaymentDirectSendUrl) {
            setCashFlowWizardMessage("Selecciona el cliente y las facturas que deseas aplicar.", "info");
            return;
        }

        const wizardState = cashFlowWizardClientPayment;
        const selectedInvoices = getCashFlowWizardSelectedClientInvoices();
        if (selectedInvoices.length === 0) {
            setCashFlowWizardMessage("Selecciona al menos una factura.", "info");
            return;
        }

        let validationMessage = "";
        selectedInvoices.forEach((invoice) => {
            const calculation = calculateCashFlowWizardClientAllocation(invoice);
            const draft = calculation.draft;
            draft.saveMessage = "";
            const invalidRetention = [
                ["ReteFuente", "reteFuente", draft.reteFuenteTaxId],
                ["ReteICA", "reteIca", draft.reteIcaTaxId],
                ["RteIVA", "rteIva", draft.rteIvaTaxId]
            ].find(([, kind, taxId]) => taxId > 0 && !findCashFlowWizardClientRetentionOption(kind, taxId));
            if (invalidRetention) {
                draft.saveMessage = `${invalidRetention[0]} no valida`;
                validationMessage ||= `La tarifa de ${invalidRetention[0]} de ${invoice.name || "la factura"} ya no esta disponible.`;
            } else if (draft.rteIvaTaxId > 0 && Number(invoice?.vat || 0) <= 0) {
                draft.saveMessage = "Factura sin IVA";
                validationMessage ||= `No se puede calcular RteIVA para ${invoice.name || "la factura"} porque no tiene IVA.`;
            } else if (draft.paymentValue <= 0) {
                draft.saveMessage = "Indica el pago";
                validationMessage ||= `Indica el valor pagado de ${invoice.name || "la factura"}.`;
            } else if (calculation.remainingBalance < -clientPaymentDifferenceTolerance) {
                draft.saveMessage = "Supera el saldo";
                validationMessage ||= `El pago y las retenciones de ${invoice.name || "la factura"} superan su saldo.`;
            }
        });

        if (validationMessage) {
            renderCashFlowWizardClientInvoices();
            setCashFlowWizardMessage(validationMessage, "error");
            return;
        }

        const movementValue = roundClientPaymentMoney(Number(wizardState.row?.dataset.entryValue || 0));
        const selectedTotal = roundClientPaymentMoney(selectedInvoices.reduce(
            (total, invoice) => total + calculateCashFlowWizardClientAllocation(invoice).draft.paymentValue,
            0));
        if (Math.abs(selectedTotal - movementValue) > clientPaymentDifferenceTolerance) {
            setCashFlowWizardMessage(
                `La diferencia entre los pagos seleccionados (${clientPaymentMoney(selectedTotal)}) y el movimiento (${clientPaymentMoney(movementValue)}) supera la tolerancia permitida de +/- ${clientPaymentMoney(clientPaymentDifferenceTolerance)}.`,
                "info");
            return;
        }

        const allSaved = selectedInvoices.every(cashFlowWizardClientAllocationIsSaved);
        const progressModal = openClientPaymentProgress(selectedInvoices.length, allSaved);
        let dataverseReady = allSaved;
        if (!dataverseReady) {
            selectedInvoices.forEach((invoice) => {
                const draft = getCashFlowWizardClientAllocationDraft(invoice);
                draft.saving = true;
                draft.saveMessage = "";
            });
            renderCashFlowWizardClientInvoices();
            try {
                const customer = wizardState.customer;
                const response = await fetch(clientPaymentDataverseApplyUrl, {
                    method: "POST",
                    headers: { "Content-Type": "application/json" },
                    body: JSON.stringify({
                        matchRecordId: getCashFlowWizardClientRecordId(),
                        recordId: wizardState.row?.dataset.recordId || "",
                        movementExternalKey: wizardState.row?.dataset.movementExternalKey || "",
                        customerId: customer.id || "",
                        customerIdentification: customer.identification || "",
                        customerName: customer.displayName || customer.name || "",
                        allocations: selectedInvoices.map(buildCashFlowWizardClientAllocation)
                    })
                });
                const payload = await response.json().catch(() => ({}));
                if (!response.ok || !payload.isSuccess) {
                    throw new Error(resolveSiigoFailureMessage(payload, "Dataverse no pudo guardar las aplicaciones."));
                }

                const matchRecordId = String(payload.matchRecordId || "").trim();
                if (matchRecordId) {
                    wizardState.recordId = matchRecordId;
                    findCashFlowSiblingRows(wizardState.row).forEach((targetRow) => {
                        targetRow.dataset.matchRecordId = matchRecordId;
                        targetRow.dataset.clientPaymentRecordId = matchRecordId;
                    });
                    if (wizardState.paymentRow) {
                        wizardState.paymentRow.dataset.recordId = matchRecordId;
                    }
                }

                (payload.items || []).forEach((item) => {
                    const invoice = selectedInvoices.find((candidate) => {
                        const sameDocument = String(candidate.id || "") === String(item.documentId || "")
                            || String(candidate.name || "") === String(item.invoiceNumber || "");
                        const sameCustomer = !item.customerId
                            || String(candidate.customerId || "") === String(item.customerId || "")
                            || String(candidate.customerIdentification || "") === String(item.customerIdentification || "");
                        return sameDocument && sameCustomer;
                    });
                    if (invoice && item.dataverseRecordId) {
                        invoice.dataverseRecordId = item.dataverseRecordId;
                    }
                });
                selectedInvoices.forEach((invoice) => {
                    const draft = getCashFlowWizardClientAllocationDraft(invoice);
                    draft.dataverseSavedSignature = cashFlowWizardClientAllocationSignature(invoice);
                    draft.saveMessage = "";
                });
                dataverseReady = true;
                setClientPaymentProgressStep(
                    progressModal,
                    "payments",
                    "success",
                    `${selectedInvoices.length} factura${selectedInvoices.length === 1 ? "" : "s"} guardada${selectedInvoices.length === 1 ? "" : "s"}`);
                setClientPaymentProgressStep(progressModal, "siigo", "running", "Enviando comprobante");
                setCashFlowWizardMessage(payload.message || "Aplicaciones guardadas en Dataverse.", "success");
            } catch (error) {
                const errorMessage = error instanceof Error ? error.message : "No fue posible guardar en Dataverse.";
                const transientSiigoFailure = isTransientSiigoFailure(errorMessage);
                selectedInvoices.forEach((invoice) => {
                    const draft = getCashFlowWizardClientAllocationDraft(invoice);
                    draft.dataverseSavedSignature = "";
                    draft.saveMessage = "Error al guardar";
                });
                if (transientSiigoFailure) {
                    showTransientSiigoFailure(progressModal);
                    setCashFlowWizardMessage(transientSiigoUserMessage, "info");
                } else {
                    setClientPaymentProgressStep(progressModal, "payments", "error", "No guardado");
                    setClientPaymentProgressStep(progressModal, "siigo", "error", "No enviado");
                    setClientPaymentProgressStep(progressModal, "reconciliation", "error", "No conciliado");
                    finishClientPaymentProgress(progressModal, false, errorMessage);
                    setCashFlowWizardMessage(errorMessage, "error");
                }
            } finally {
                selectedInvoices.forEach((invoice) => {
                    getCashFlowWizardClientAllocationDraft(invoice).saving = false;
                });
                renderCashFlowWizardClientInvoices();
            }
        }

        if (!dataverseReady || cashFlowWizardClientPayment !== wizardState) {
            return;
        }

        selectedInvoices.forEach((invoice) => {
            const draft = getCashFlowWizardClientAllocationDraft(invoice);
            draft.sending = true;
            draft.sendFailed = false;
        });
        renderCashFlowWizardClientInvoices();
        const sent = await sendCashFlowWizardClientPaymentToSiigo(selectedInvoices, progressModal);
        if (!sent && cashFlowWizardClientPayment === wizardState) {
            selectedInvoices.forEach((invoice) => {
                getCashFlowWizardClientAllocationDraft(invoice).sending = false;
            });
            renderCashFlowWizardClientInvoices();
        }
    };

    const renderCashFlowWizardClientPayment = (row, message = "", tone = "info") => {
        const modal = ensureCashFlowWizardModal();
        setCashFlowWizardTitle("Factura cliente");
        const card = modal.querySelector("[data-cnc-cashflow-wizard-card]");
        const count = modal.querySelector("[data-cnc-cashflow-wizard-count]");
        const bar = modal.querySelector("[data-cnc-cashflow-wizard-bar]");
        cashFlowWizardMode = "client-payment";
        setCashFlowWizardProcessActions();
        if (count) {
            count.textContent = `Entrada ${cashFlowWizardIndex + 1} de ${cashFlowWizardRows.length}`;
        }
        if (bar) {
            bar.style.width = `${Math.round((cashFlowWizardIndex + 1) / Math.max(cashFlowWizardRows.length, 1) * 100)}%`;
        }

        const existingState = cashFlowWizardClientPayment?.row === row ? cashFlowWizardClientPayment : null;
        const paymentRow = existingState?.paymentRow || findClientPaymentRowForCashFlow(row);
        cashFlowWizardClientPayment = existingState || {
            row,
            paymentRow,
            recordId: row.dataset.matchRecordId || paymentRow?.dataset.recordId || "",
            query: "",
            customer: null,
            candidates: [],
            invoices: [],
            paidInvoices: [],
            reteFuenteOptions: [],
            reteIcaOptions: [],
            rteIvaOptions: [],
            allocations: {},
            issues: [],
            payloadJson: "",
            responseJson: "",
            loadingInvoices: false,
            searchSequence: 0,
            searchTimer: 0,
            siigoCreated: false
        };

        if (card) {
            card.innerHTML = `
                <div class="cnc-wizard-process">
                    <header class="cnc-wizard-process__summary">
                        <div>
                            <span>${escapeHtml(row.dataset.dateDisplay || "Sin fecha")}</span>
                            <p>${escapeHtml(resolveCashFlowRowLabel(row))}</p>
                        </div>
                        <strong>${escapeHtml(row.dataset.amountLabel || money(Number(row.dataset.entryValue || 0)))}</strong>
                    </header>
                    <div class="cnc-party-picker">
                        <label class="cnc-modal__field">
                            <span>Cliente</span>
                            <input class="form-control" type="search" autocomplete="off" data-cnc-wizard-client-customer-query placeholder="Escribe nombre o NIT" />
                        </label>
                        <div class="cnc-party-picker__results" data-cnc-wizard-client-customers hidden></div>
                    </div>
                    <div class="cnc-party-picker__selected" data-cnc-wizard-client-customer hidden></div>
                    <div class="cnc-supplier-payment-table-wrap" data-cnc-wizard-client-invoice-wrap hidden>
                        <table class="table align-middle cnc-table cnc-payment-allocation-table cnc-client-payment-table">
                            <thead>
                                <tr>
                                    <th>Factura</th>
                                    <th>Fecha</th>
                                    <th class="text-end">Total bruto</th>
                                    <th>Pago</th>
                                    <th>ReteFuente</th>
                                    <th>ReteICA</th>
                                    <th>RteIVA</th>
                                    <th class="text-end">Saldo final</th>
                                    <th>Seleccionar</th>
                                </tr>
                            </thead>
                            <tbody data-cnc-wizard-client-invoices></tbody>
                        </table>
                        <button type="button" class="btn btn-link cnc-client-add-company" data-cnc-wizard-client-add-company>
                            + Añadir factura de otra razon social
                        </button>
                    </div>
                    <div class="cnc-payment-allocation-summary cnc-client-payment-summary" data-cnc-wizard-client-summary></div>
                    <section class="cnc-client-payment-history" data-cnc-wizard-client-paid-wrap hidden>
                        <h3>Ultimas 5 pagadas</h3>
                        <div class="cnc-client-payment-history__table">
                            <table class="table align-middle cnc-table cnc-client-payment-history-table">
                                <thead><tr><th>Factura</th><th>Pago</th><th class="text-end">Total bruto</th><th>ReteFuente</th><th>ReteICA</th><th>RteIVA</th></tr></thead>
                                <tbody data-cnc-wizard-client-paid-invoices></tbody>
                            </table>
                        </div>
                    </section>
                    <ul class="cnc-issue-list" data-cnc-wizard-client-issues hidden></ul>
                    <div class="cnc-wizard-process__actions">
                        <button type="button" class="btn btn-outline-secondary" data-cnc-wizard-client-direct-back>Volver</button>
                        <button type="button" class="btn btn-outline-warning" data-cnc-wizard-client-leave-pending>Dejar pendiente</button>
                        <button type="button" class="btn btn-outline-secondary" data-cnc-wizard-client-omitted>OMITIDO</button>
                        <button type="button" class="btn btn-primary" data-cnc-wizard-client-apply-selected disabled>Aplicar</button>
                    </div>
                </div>`;
        }

        const query = modal.querySelector("[data-cnc-wizard-client-customer-query]");
        if (query && cashFlowWizardClientPayment.customer) {
            query.value = supplierPaymentLabel(cashFlowWizardClientPayment.customer);
        }
        query?.addEventListener("input", () => {
            const value = String(query.value || "").trim();
            cashFlowWizardClientPayment.query = value;
            if (cashFlowWizardClientPayment.customer
                && value !== supplierPaymentLabel(cashFlowWizardClientPayment.customer)) {
                cashFlowWizardClientPayment.customer = null;
                cashFlowWizardClientPayment.invoices = [];
                cashFlowWizardClientPayment.paidInvoices = [];
                cashFlowWizardClientPayment.reteFuenteOptions = [];
                cashFlowWizardClientPayment.reteIcaOptions = [];
                cashFlowWizardClientPayment.rteIvaOptions = [];
                cashFlowWizardClientPayment.allocations = {};
                renderCashFlowWizardClientInvoices();
            }
            scheduleCashFlowWizardClientSearch(value);
        });
        modal.querySelector("[data-cnc-wizard-client-direct-back]")?.addEventListener("click", () => {
            cashFlowWizardMode = "rows";
            cashFlowWizardClientPayment = null;
            renderCashFlowWizard();
        });
        modal.querySelector("[data-cnc-wizard-client-apply-selected]")?.addEventListener(
            "click",
            applyCashFlowWizardSelectedClientPayments);
        modal.querySelector("[data-cnc-wizard-client-add-company]")?.addEventListener(
            "click",
            openAdditionalClientInvoiceModal);
        modal.querySelector("[data-cnc-wizard-client-leave-pending]")?.addEventListener(
            "click",
            () => openCashFlowPendingModal(cashFlowWizardClientPayment?.row || row));
        modal.querySelector("[data-cnc-wizard-client-omitted]")?.addEventListener(
            "click",
            () => openCashFlowPendingModal(cashFlowWizardClientPayment?.row || row, "omitted"));
        renderCashFlowWizardClientCandidates();
        renderCashFlowWizardClientInvoices();
        renderCashFlowWizardClientIssues();
        updateCashFlowWizardClientSummary();
        setCashFlowWizardMessage(message, tone);
        window.setTimeout(() => query?.focus(), 0);
    };

    const ensureClientPaymentProgressModal = () => {
        let modal = document.getElementById("cncClientPaymentProgressModal");
        if (modal) {
            return modal;
        }

        modal = document.createElement("div");
        modal.className = "cnc-modal cnc-client-payment-progress-modal";
        modal.id = "cncClientPaymentProgressModal";
        modal.setAttribute("role", "dialog");
        modal.setAttribute("aria-modal", "true");
        modal.hidden = true;
        modal.innerHTML = `
            <div class="cnc-modal__panel cnc-client-payment-progress-panel">
                <div class="cnc-modal__header">
                    <div>
                        <div class="cnc-kicker" data-cnc-client-progress-kicker>Pago de cliente</div>
                        <h2 data-cnc-client-progress-title>Procesando conciliacion</h2>
                    </div>
                </div>
                <div class="cnc-client-payment-progress-list">
                    <div class="cnc-client-payment-progress-step" data-cnc-client-progress-step="payments">
                        <span>1</span><div><strong>Factura en Dataverse</strong><small>En espera</small></div>
                    </div>
                    <div class="cnc-client-payment-progress-step" data-cnc-client-progress-step="siigo">
                        <span>2</span><div><strong>Comprobante en Siigo</strong><small>En espera</small></div>
                    </div>
                    <div class="cnc-client-payment-progress-step" data-cnc-client-progress-step="reconciliation">
                        <span>3</span><div><strong>Conciliacion en Dataverse</strong><small>En espera</small></div>
                    </div>
                </div>
                <p class="cnc-client-payment-progress-summary" data-cnc-client-progress-summary></p>
                <div class="cnc-modal__actions" data-cnc-client-progress-actions hidden>
                    <button type="button" class="btn btn-outline-secondary" data-cnc-client-progress-close>Cerrar</button>
                </div>
            </div>`;
        document.body.appendChild(modal);
        modal.querySelector("[data-cnc-client-progress-close]")?.addEventListener("click", () => {
            modal.hidden = true;
        });
        return modal;
    };

    const setClientPaymentProgressStep = (modal, key, state, message) => {
        const step = modal.querySelector(`[data-cnc-client-progress-step="${key}"]`);
        if (!step) {
            return;
        }
        step.dataset.state = state;
        const status = step.querySelector("small");
        if (status) {
            status.textContent = message || state;
        }
    };

    const openClientPaymentProgress = (invoiceCount, paymentsReady = false) => {
        const modal = ensureClientPaymentProgressModal();
        const summary = modal.querySelector("[data-cnc-client-progress-summary]");
        const actions = modal.querySelector("[data-cnc-client-progress-actions]");
        const kicker = modal.querySelector("[data-cnc-client-progress-kicker]");
        const title = modal.querySelector("[data-cnc-client-progress-title]");
        modal.hidden = false;
        modal.dataset.state = "running";
        if (kicker) {
            kicker.textContent = "Pago de cliente";
        }
        if (title) {
            title.textContent = "Procesando conciliación";
        }
        if (summary) {
            summary.textContent = paymentsReady ? "Enviando pago..." : "Guardando aplicacion...";
        }
        if (actions) {
            actions.hidden = true;
        }
        setClientPaymentProgressStep(
            modal,
            "payments",
            paymentsReady ? "success" : "running",
            paymentsReady
                ? `${invoiceCount} factura${invoiceCount === 1 ? "" : "s"} confirmada${invoiceCount === 1 ? "" : "s"}`
                : "Guardando factura");
        setClientPaymentProgressStep(
            modal,
            "siigo",
            paymentsReady ? "running" : "waiting",
            paymentsReady ? "Enviando comprobante" : "En espera");
        setClientPaymentProgressStep(modal, "reconciliation", "waiting", "En espera");
        return modal;
    };

    const finishClientPaymentProgress = (modal, success, message) => {
        const summary = modal.querySelector("[data-cnc-client-progress-summary]");
        const actions = modal.querySelector("[data-cnc-client-progress-actions]");
        modal.dataset.state = success ? "success" : "error";
        if (summary) {
            summary.textContent = message || (success ? "Pago conciliado." : "No fue posible completar la conciliacion.");
        }
        if (actions) {
            actions.hidden = success;
        }
    };

    const showTransientSiigoFailure = (existingModal = null, paymentsSaved = false) => {
        const modal = existingModal || ensureClientPaymentProgressModal();
        const kicker = modal.querySelector("[data-cnc-client-progress-kicker]");
        const title = modal.querySelector("[data-cnc-client-progress-title]");
        const summary = modal.querySelector("[data-cnc-client-progress-summary]");
        const actions = modal.querySelector("[data-cnc-client-progress-actions]");
        modal.hidden = false;
        modal.dataset.state = "warning";
        if (kicker) {
            kicker.textContent = "Servicio de Siigo";
        }
        if (title) {
            title.textContent = "Siigo temporalmente no disponible";
        }
        if (summary) {
            summary.textContent = transientSiigoUserMessage;
        }
        if (actions) {
            actions.hidden = false;
        }
        setClientPaymentProgressStep(
            modal,
            "payments",
            paymentsSaved ? "success" : "waiting",
            paymentsSaved ? "Aplicaciones guardadas" : "Sin cambios");
        setClientPaymentProgressStep(modal, "siigo", "warning", "Falla temporal de Siigo");
        setClientPaymentProgressStep(modal, "reconciliation", "waiting", "Pendiente");
        return modal;
    };

    const sendCashFlowWizardClientPaymentToSiigo = async (selectedInvoices, existingProgressModal = null) => {
        if (!Array.isArray(selectedInvoices)
            || selectedInvoices.length === 0
            || !cashFlowWizardClientPayment?.row
            || !cashFlowWizardClientPayment.customer
            || !clientPaymentDirectSendUrl) {
            setCashFlowWizardMessage("Selecciona el cliente y las facturas que deseas aplicar.", "info");
            if (existingProgressModal) {
                setClientPaymentProgressStep(existingProgressModal, "siigo", "error", "No enviado");
                setClientPaymentProgressStep(existingProgressModal, "reconciliation", "error", "No conciliado");
                finishClientPaymentProgress(existingProgressModal, false, "No se encontraron las facturas que se iban a enviar.");
            }
            return false;
        }

        const allocations = selectedInvoices
            .map(buildCashFlowWizardClientAllocation)
            .filter((allocation) => allocation.appliedValue > 0
                || allocation.reteFuenteTaxId > 0
                || allocation.reteIcaTaxId > 0
                || allocation.rteIvaTaxId > 0);
        if (selectedInvoices.length === 0
            || selectedInvoices.some((invoice) => !cashFlowWizardClientAllocationIsSaved(invoice))) {
            setCashFlowWizardMessage("Todas las facturas deben quedar confirmadas en Dataverse antes del envio.", "info");
            return false;
        }

        const progressModal = existingProgressModal || openClientPaymentProgress(selectedInvoices.length, true);
        setClientPaymentProgressStep(
            progressModal,
            "payments",
            "success",
            `${selectedInvoices.length} factura${selectedInvoices.length === 1 ? "" : "s"} guardada${selectedInvoices.length === 1 ? "" : "s"}`);
        setClientPaymentProgressStep(progressModal, "siigo", "running", "Enviando comprobante");
        cashFlowWizardClientPayment.issues = [];
        renderCashFlowWizardClientIssues();
        setCashFlowWizardMessage("Enviando comprobante de ingreso a Siigo...", "info");
        let payload = {};
        try {
            const customer = cashFlowWizardClientPayment.customer;
            const response = await fetch(clientPaymentDirectSendUrl, {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify({
                    matchRecordId: getCashFlowWizardClientRecordId(),
                    recordId: cashFlowWizardClientPayment.row.dataset.recordId || "",
                    movementExternalKey: cashFlowWizardClientPayment.row.dataset.movementExternalKey || "",
                    customerId: customer.id || "",
                    customerIdentification: customer.identification || "",
                    customerName: customer.displayName || customer.name || "",
                    allocations
                })
            });
            payload = await response.json().catch(() => ({}));
            const transientSiigoFailure = isTransientSiigoFailure(
                payload.isTransientSiigoFailure,
                payload.message,
                payload.detail,
                ...(payload.issues || []));
            cashFlowWizardClientPayment.issues = transientSiigoFailure ? [] : (payload.issues || []);
            cashFlowWizardClientPayment.siigoCreated = Boolean(payload.siigoSucceeded);
            renderCashFlowWizardClientIssues();
            setClientPaymentProgressStep(
                progressModal,
                "payments",
                payload.dataversePaymentsSucceeded ? "success" : "error",
                payload.dataversePaymentsSucceeded ? "Aplicaciones guardadas" : "No confirmado");
            setClientPaymentProgressStep(
                progressModal,
                "siigo",
                payload.siigoSucceeded ? "success" : transientSiigoFailure ? "warning" : "error",
                payload.siigoSucceeded
                    ? (payload.siigoName || "Comprobante creado")
                    : transientSiigoFailure ? "Servicio no disponible" : "Envio fallido");
            setClientPaymentProgressStep(
                progressModal,
                "reconciliation",
                payload.dataverseReconciliationSucceeded ? "success" : transientSiigoFailure ? "waiting" : "error",
                payload.dataverseReconciliationSucceeded ? "Movimiento conciliado" : transientSiigoFailure ? "Pendiente" : "No conciliado");
            if (!response.ok) {
                throw new Error(resolveSiigoFailureMessage(payload, "No fue posible enviar el comprobante."));
            }
            if (!payload.isSuccess) {
                selectedInvoices.forEach((invoice) => {
                    const draft = getCashFlowWizardClientAllocationDraft(invoice);
                    draft.sending = false;
                    draft.sendFailed = true;
                });
                if (transientSiigoFailure) {
                    showTransientSiigoFailure(progressModal, Boolean(payload.dataversePaymentsSucceeded));
                    setCashFlowWizardMessage(transientSiigoUserMessage, "info");
                } else {
                    finishClientPaymentProgress(progressModal, false, payload.message || "El pago no fue conciliado.");
                    setCashFlowWizardMessage(payload.message || "El comprobante quedo bloqueado.", "error");
                }
                updateCashFlowWizardClientSummary();
                return false;
            }

            finishClientPaymentProgress(progressModal, true, payload.message || "Pago conciliado en Siigo y Dataverse.");
            const completedRow = cashFlowWizardClientPayment.row;
            await delay(650);
            progressModal.hidden = true;
            completeCashFlowWizardRow(completedRow, payload.row, payload.message || "Comprobante enviado a Siigo.");
            ensureCashFlowWizardModal().hidden = true;
            return true;
        } catch (error) {
            const errorMessage = resolveSiigoFailureMessage(
                payload,
                error instanceof Error ? error.message : "No fue posible enviar el comprobante.");
            const transientSiigoFailure = isTransientSiigoFailure(
                payload.isTransientSiigoFailure,
                errorMessage);
            selectedInvoices.forEach((invoice) => {
                const draft = getCashFlowWizardClientAllocationDraft(invoice);
                draft.sending = false;
                draft.sendFailed = true;
            });
            if (!payload.siigoSucceeded && !transientSiigoFailure) {
                setClientPaymentProgressStep(progressModal, "siigo", "error", "Envio fallido");
            }
            if (!payload.dataverseReconciliationSucceeded && !transientSiigoFailure) {
                setClientPaymentProgressStep(progressModal, "reconciliation", "error", "No conciliado");
            }
            if (transientSiigoFailure) {
                cashFlowWizardClientPayment.issues = [];
                renderCashFlowWizardClientIssues();
                showTransientSiigoFailure(progressModal, Boolean(payload.dataversePaymentsSucceeded));
                setCashFlowWizardMessage(transientSiigoUserMessage, "info");
            } else {
                finishClientPaymentProgress(progressModal, false, errorMessage);
                setCashFlowWizardMessage(errorMessage, "error");
            }
            updateCashFlowWizardClientSummary();
            return false;
        }
    };

    const normalizeCuentaCobroRetentionKind = (kind) => {
        const normalized = String(kind || "")
            .normalize("NFD")
            .replace(/[\u0300-\u036f]/g, "")
            .replace(/[^a-z]/gi, "")
            .toLowerCase();
        if (normalized === "retefuente" || normalized === "retefte") {
            return "ReteFuente";
        }
        if (normalized === "reteica") {
            return "ReteICA";
        }
        if (normalized === "rteiva" || normalized === "reteiva") {
            return "RteIVA";
        }
        return "";
    };

    const cuentaCobroWizardRequest = () => {
        const state = cashFlowWizardCuentaCobro;
        const processRow = state?.processRow;
        const cashFlowRow = state?.row;
        const editorRow = state?.editorRow || {};
        return {
            recordId: editorRow.recordId || processRow?.dataset.recordId || "",
            recordSource: editorRow.recordSource
                || processRow?.dataset.recordSource
                || cashFlowRow?.dataset.recordSource
                || "",
            concurrencyToken: editorRow.concurrencyToken || "",
            cashFlowRecordId: editorRow.cashFlowRecordId
                || processRow?.dataset.cashflowRecordId
                || cashFlowRow?.dataset.cashflowRecordId
                || cashFlowRow?.dataset.recordId
                || "",
            cashFlowExternalKey: editorRow.cashFlowExternalKey
                || processRow?.dataset.movementExternalKey
                || cashFlowRow?.dataset.movementExternalKey
                || ""
        };
    };

    const normalizeCuentaCobroRetentionOptions = (options, kind, existingRetentions) => {
        const normalizedKind = normalizeCuentaCobroRetentionKind(kind);
        const normalized = (Array.isArray(options) ? options : [])
            .map((option) => ({
                taxId: Number(option?.taxId || 0),
                kind: normalizeCuentaCobroRetentionKind(option?.kind) || normalizedKind,
                name: String(option?.name || ""),
                rate: Number(option?.rate || 0),
                rateLabel: String(option?.rateLabel || option?.name || "")
            }))
            .filter((option) => option.taxId > 0);

        (existingRetentions || [])
            .filter((retention) => normalizeCuentaCobroRetentionKind(retention?.kind) === normalizedKind)
            .forEach((retention) => {
                const taxId = Number(retention?.taxId || 0);
                if (taxId <= 0 || normalized.some((option) => option.taxId === taxId)) {
                    return;
                }
                const rate = Number(retention?.rate || 0);
                normalized.push({
                    taxId,
                    kind: normalizedKind,
                    name: String(retention?.label || normalizedKind),
                    rate,
                    rateLabel: rate > 0
                        ? `${rate.toLocaleString("es-CO", { maximumFractionDigits: 4 })}${normalizedKind === "ReteICA" ? " x mil" : "%"}`
                        : String(retention?.label || normalizedKind)
                });
            });

        return normalized;
    };

    const hydrateCuentaCobroWizardEditor = (payload) => {
        const state = cashFlowWizardCuentaCobro;
        if (!state) {
            return;
        }

        const row = {
            ...(state.editorRow || {}),
            ...(payload?.row || {})
        };
        const retentions = Array.isArray(row.retentions) ? row.retentions : [];
        const retentionFor = (kind) => retentions.find(
            (retention) => normalizeCuentaCobroRetentionKind(retention?.kind) === kind) || null;
        const reteFuente = retentionFor("ReteFuente");
        const reteIca = retentionFor("ReteICA");
        const rteIva = retentionFor("RteIVA");
        const cashFlowExitValue = Number(
            row.cashFlowExitValue
            || state.row?.dataset?.exitValue
            || 0);
         const movementDateValue = row.movementDateValue
             || state.row?.dataset?.dateValue
             || "";
         const automationState = String(row.automationState || "").trim().toLowerCase();
         const reviewReason = String(row.reviewReason || "");
         state.needsSiigoVerification = automationState === "verificaciondocumentosoportesiigopendiente"
             || reviewReason.toLowerCase().includes("[siigo_support_document_write_ambiguous]");
         state.siigoDocumentInProgress = automationState === "procesandodocumentosoportesiigo";

         state.editorRow = row;
         state.isLegacy = String(row.recordSource || "").trim().toLowerCase() === "cuenta-cobro";
         state.isLocked = Boolean(
             row.siigoDocumentId
             || row.siigoDocumentName
             || row.siigoPaymentId
             || row.siigoPaymentName
             || state.needsSiigoVerification
             || state.siigoDocumentInProgress);
        state.accountCode = row.accountCode || state.accountCode || "";
        state.reteFuenteOptions = normalizeCuentaCobroRetentionOptions(
            payload?.reteFuenteOptions ?? state.reteFuenteOptions,
            "ReteFuente",
            retentions);
        state.reteIcaOptions = normalizeCuentaCobroRetentionOptions(
            payload?.reteIcaOptions ?? state.reteIcaOptions,
            "ReteICA",
            retentions);
        state.rteIvaOptions = normalizeCuentaCobroRetentionOptions(
            payload?.rteIvaOptions ?? state.rteIvaOptions,
            "RteIVA",
            retentions);
        state.retentionAccountCodes = {
            ReteFuente: String(reteFuente?.accountCode || ""),
            ReteICA: String(reteIca?.accountCode || ""),
            RteIVA: String(rteIva?.accountCode || "")
        };
         state.form = {
            receptor: String(row.receptor || row.cashFlowRecipient || ""),
            nitOCedula: String(row.nitOCedula || ""),
            observaciones: String(row.observaciones || row.cashFlowObservations || row.cashFlowDescription || ""),
            fechaEmisionValue: String(row.fechaEmisionValue || movementDateValue),
            fechaPagoValue: String(row.fechaPagoValue || movementDateValue),
            valorTotal: Number(row.valorTotal) > 0 ? Number(row.valorTotal) : cashFlowExitValue,
            valorIva: Math.max(0, Number(row.valorIva || 0)),
            cloudValue: Math.max(0, Number(row.cloudValue || 0)),
            copiersValue: Math.max(0, Number(row.copiersValue || 0)),
            categoryValue: String(row.categoryValue || ""),
            accountCode: String(row.accountCode || state.accountCode || ""),
            reteFuenteTaxId: Number(reteFuente?.taxId || 0),
            reteIcaTaxId: Number(reteIca?.taxId || 0),
            rteIvaTaxId: Number(rteIva?.taxId || 0)
        };
        state.supplier = row.siigoSupplierId && row.siigoSupplierName && row.nitOCedula
            ? {
                id: String(row.siigoSupplierId),
                name: String(row.siigoSupplierName),
                commercialName: String(row.siigoSupplierName),
                identification: String(row.nitOCedula),
                branchOffice: 0,
                active: true
            }
            : null;
        state.supplierQuery = state.supplier ? supplierPaymentLabel(state.supplier) : String(row.receptor || "");
        state.supplierCandidates = [];
        state.supplierSearchMessage = state.supplier
            ? "Proveedor de Siigo asociado al gasto."
            : "Escribe al menos dos caracteres y selecciona un proveedor activo de Siigo.";
        state.loaded = true;
        state.loadFailed = false;
        state.dirty = !String(row.recordId || "").trim();
        state.isReadyForSiigo = String(row.automationState || "").toLowerCase() === "listosiigo"
            || String(row.stage || "").toLowerCase() === "prevalidacion";
    };

    const findCuentaCobroWizardRetentionOption = (kind, taxId) => {
        const normalizedKind = normalizeCuentaCobroRetentionKind(kind);
        const options = normalizedKind === "ReteICA"
            ? cashFlowWizardCuentaCobro?.reteIcaOptions
            : normalizedKind === "RteIVA"
                ? cashFlowWizardCuentaCobro?.rteIvaOptions
                : cashFlowWizardCuentaCobro?.reteFuenteOptions;
        return (options || []).find((option) => Number(option.taxId || 0) === Number(taxId || 0)) || null;
    };

    const calculateCuentaCobroWizardExpense = () => {
        const state = cashFlowWizardCuentaCobro;
        const form = state?.form || {};
        const totalValue = Math.max(0, roundClientPaymentMoney(form.valorTotal));
        const vatValue = Math.max(0, roundClientPaymentMoney(form.valorIva));
        const taxBaseValue = Math.max(0, roundClientPaymentMoney(totalValue - vatValue));
        const cashFlowExitValue = roundClientPaymentMoney(
            state?.editorRow?.cashFlowExitValue
            || state?.row?.dataset?.exitValue
            || 0);
        if (state?.isLegacy) {
            const retentions = (Array.isArray(state.editorRow?.retentions) ? state.editorRow.retentions : [])
                .map((retention) => ({
                    kind: normalizeCuentaCobroRetentionKind(retention?.kind) || String(retention?.kind || ""),
                    label: String(retention?.label || retention?.kind || "Retencion"),
                    taxId: Number(retention?.taxId || 0),
                    accountCode: String(retention?.accountCode || ""),
                    baseValue: Number(retention?.baseValue || totalValue),
                    rate: Number(retention?.rate || 0),
                    value: roundClientPaymentMoney(retention?.value)
                }))
                .filter((retention) => retention.value > 0);
            const retentionValue = roundClientPaymentMoney(
                retentions.reduce((total, retention) => total + retention.value, 0));
            const storedPaymentValue = Number(state.editorRow?.valorPago || 0);
            const paymentValue = roundClientPaymentMoney(
                storedPaymentValue > 0 ? storedPaymentValue : Math.max(0, totalValue - retentionValue));
            const balanceValue = roundClientPaymentMoney(totalValue - retentionValue - paymentValue);
            const cashFlowDifferenceValue = roundClientPaymentMoney(paymentValue - cashFlowExitValue);
            return {
                totalValue,
                vatValue,
                taxBaseValue,
                retentions,
                retentionValue,
                paymentValue,
                balanceValue,
                cashFlowExitValue,
                cashFlowDifferenceValue,
                isBalanced: totalValue > 0
                    && paymentValue > 0
                    && cashFlowExitValue > 0
                    && Math.abs(balanceValue) <= 0.01
                    && Math.abs(cashFlowDifferenceValue) <= 0.01
            };
        }

        const retentions = [
            { kind: "ReteFuente", label: "ReteFuente", taxId: form.reteFuenteTaxId },
            { kind: "ReteICA", label: "ReteICA", taxId: form.reteIcaTaxId },
            { kind: "RteIVA", label: "RteIVA", taxId: form.rteIvaTaxId }
        ].map((selection) => {
            const option = findCuentaCobroWizardRetentionOption(selection.kind, selection.taxId);
            if (!option) {
                return null;
            }
            const divisor = selection.kind === "ReteICA" ? 1000 : 100;
            const baseValue = selection.kind === "RteIVA" ? vatValue : taxBaseValue;
            return {
                kind: selection.kind,
                label: option.name || selection.label,
                taxId: Number(option.taxId || 0),
                accountCode: state?.retentionAccountCodes?.[selection.kind] || "",
                baseValue,
                rate: Number(option.rate || 0),
                value: roundClientPaymentMoney(baseValue * Number(option.rate || 0) / divisor)
            };
        }).filter(Boolean);
        const retentionValue = roundClientPaymentMoney(
            retentions.reduce((total, retention) => total + retention.value, 0));
        const paymentValue = Math.max(0, roundClientPaymentMoney(totalValue - retentionValue));
        const balanceValue = roundClientPaymentMoney(totalValue - retentionValue - paymentValue);
        const cashFlowDifferenceValue = roundClientPaymentMoney(paymentValue - cashFlowExitValue);
        return {
            totalValue,
            vatValue,
            taxBaseValue,
            retentions,
            retentionValue,
            paymentValue,
            balanceValue,
            cashFlowExitValue,
            cashFlowDifferenceValue,
            isBalanced: totalValue > 0
                && paymentValue > 0
                && cashFlowExitValue > 0
                && Math.abs(balanceValue) <= 0.01
                && Math.abs(cashFlowDifferenceValue) <= 0.01
        };
    };

    const validateCuentaCobroWizardExpense = (calculation = calculateCuentaCobroWizardExpense()) => {
        const state = cashFlowWizardCuentaCobro;
        const form = state?.form || {};
        if (state?.isLegacy) {
            return String(form.accountCode || "").trim()
                ? []
                : ["Selecciona la cuenta contable del registro historico."];
        }
        const issues = [];
        if (!String(form.receptor || "").trim()) {
            issues.push("Indica el nombre del receptor.");
        }
        if (!String(form.nitOCedula || "").trim()) {
            issues.push("Indica el NIT o la cedula.");
        }
        if (!state?.supplier?.id || !state?.supplier?.identification) {
            issues.push("Busca y selecciona el proveedor activo en Siigo.");
        }
        if (!String(form.fechaEmisionValue || "").trim()) {
            issues.push("Indica la fecha de emision.");
        }
         if (!String(form.fechaPagoValue || "").trim()) {
             issues.push("Indica la fecha de pago.");
         }
         if (String(form.fechaEmisionValue || "").trim()
             && String(form.fechaPagoValue || "").trim()
             && String(form.fechaEmisionValue) > String(form.fechaPagoValue)) {
             issues.push("La fecha de emision no puede ser posterior a la fecha de pago.");
         }
         if (!String(form.accountCode || "").trim()) {
            issues.push("Selecciona la cuenta contable del gasto.");
        }
        if (calculation.totalValue <= 0) {
            issues.push("El valor total debe ser mayor a cero.");
        }
        if (calculation.vatValue < 0 || calculation.vatValue > calculation.totalValue) {
            issues.push("El valor IVA debe estar entre cero y el total.");
        }
        const allocationBase = roundClientPaymentMoney(calculation.totalValue - calculation.vatValue);
        const allocatedValue = roundClientPaymentMoney(Number(form.cloudValue || 0) + Number(form.copiersValue || 0));
        if (Math.abs(allocatedValue - allocationBase) > 0.01) {
            issues.push(`Cloud y Copiers deben sumar la base sin IVA (${clientPaymentMoney(allocationBase)}).`);
        }
        if (!String(form.categoryValue || "").trim()) {
            issues.push("Selecciona la categoria del gasto.");
        }
        if (Number(form.rteIvaTaxId || 0) > 0 && calculation.vatValue <= 0) {
            issues.push("Indica el valor IVA para calcular RteIVA.");
        }
        if (calculation.paymentValue <= 0) {
            issues.push("Las retenciones no pueden ser iguales o superiores al total.");
        }
        if (calculation.cashFlowExitValue <= 0) {
            issues.push("La salida bancaria no tiene un valor valido para conciliar.");
        }
        if (Math.abs(calculation.balanceValue) > 0.01) {
            issues.push("El saldo de la cuenta de cobro debe quedar en cero.");
        }
        if (calculation.cashFlowExitValue > 0 && Math.abs(calculation.cashFlowDifferenceValue) > 0.01) {
            issues.push("El pago calculado debe coincidir con la salida bancaria.");
        }
        return issues;
    };

    const updateCuentaCobroWizardActions = () => {
        const modal = ensureCashFlowWizardModal();
        const state = cashFlowWizardCuentaCobro;
        if (!state) {
            return;
        }

        const request = cuentaCobroWizardRequest();
        const editorRow = state.editorRow || {};
        const processRow = state.processRow;
        const hasRecord = Boolean(request.recordId);
        const hasSavedAccount = Boolean(state.form?.accountCode) && !state.dirty;
        const hasDocument = Boolean(
            editorRow.siigoDocumentId
            || editorRow.siigoDocumentName
            || state.siigoDocumentId
            || state.siigoDocumentName);
        const hasPayment = Boolean(
            editorRow.siigoPaymentId
            || editorRow.siigoPaymentName
            || state.siigoPaymentId
            || state.siigoPaymentName);
        const historicalPreflight = Boolean(processRow?.querySelector("[data-cnc-cuenta-cobro-preflight]"));
        const historicalSend = Boolean(processRow?.querySelector("[data-cnc-cuenta-cobro-send]"));
        const historicalPayment = Boolean(processRow?.querySelector("[data-cnc-cuenta-cobro-send-payment]"));
        const hasHistoricalAction = historicalPreflight || historicalSend || historicalPayment;
        const automationState = String(editorRow.automationState || "").toLowerCase();
        const stage = String(editorRow.stage || "").toLowerCase();
         const isReadyForSiigo = state.isReadyForSiigo === true
             || automationState === "listosiigo"
             || stage === "prevalidacion";
         const hasDocumentWriteHold = Boolean(
             state.needsSiigoVerification
             || state.siigoDocumentInProgress);
         const calculation = state.loaded ? calculateCuentaCobroWizardExpense() : null;
        const canSave = state.loaded
            && state.dirty
            && !state.isLocked
            && validateCuentaCobroWizardExpense(calculation).length === 0
            && !state.loading
            && !state.saving
            && !state.processing;
        const saveButton = modal.querySelector("[data-cnc-wizard-cuenta-save]");
        if (saveButton) {
            saveButton.hidden = !state.loaded || (!state.dirty && hasRecord);
            saveButton.disabled = !canSave;
        }
        const completeButton = modal.querySelector("[data-cnc-wizard-cuenta-complete]");
        const canComplete = state.loaded
            && !state.isLegacy
            && !hasDocument
            && !hasDocumentWriteHold
            && validateCuentaCobroWizardExpense(calculation).length === 0
            && !state.loading
            && !state.saving
            && !state.processing;
        if (completeButton) {
            completeButton.hidden = !canComplete;
            completeButton.disabled = !canComplete;
        }

         const canUseSavedRecord = hasRecord
             && hasSavedAccount
             && !state.loading
             && !state.saving
             && !state.processing;
        const actions = [
            [
                 "[data-cnc-wizard-cuenta-payment]",
                 canUseSavedRecord
                     && !hasDocumentWriteHold
                     && (hasHistoricalAction ? historicalPayment : hasDocument && !hasPayment)
            ]
        ];
        actions.forEach(([selector, visible]) => {
            const button = modal.querySelector(selector);
            if (button) {
                button.hidden = !visible;
                button.disabled = !visible || state.processing;
            }
        });
    };

    const renderCuentaCobroWizardPayload = () => {
        renderCashFlowWizardIssues("[data-cnc-wizard-cuenta-issues]", cashFlowWizardCuentaCobro?.issues || []);
        renderCashFlowWizardPreview(
            "[data-cnc-wizard-cuenta-payload]",
            "[data-cnc-wizard-cuenta-response]",
            "[data-cnc-wizard-cuenta-preview]",
            cashFlowWizardCuentaCobro?.payloadJson || "",
            cashFlowWizardCuentaCobro?.responseJson || "");
    };

    const updateCuentaCobroWizardSummary = () => {
        const modal = ensureCashFlowWizardModal();
        const state = cashFlowWizardCuentaCobro;
        if (!state?.loaded) {
            updateCuentaCobroWizardActions();
            return;
        }

        const calculation = calculateCuentaCobroWizardExpense();
        const issues = validateCuentaCobroWizardExpense(calculation);
        state.form.valorPago = calculation.paymentValue;
        const setText = (selector, value) => {
            const target = modal.querySelector(selector);
            if (target) {
                target.textContent = value;
            }
        };
        const payment = modal.querySelector("[data-cnc-wizard-cuenta-payment-value]");
        if (payment) {
            payment.value = calculation.paymentValue.toFixed(2);
        }
        const rteIvaSelect = modal.querySelector("[data-cnc-wizard-cuenta-rte-iva]");
        if (rteIvaSelect) {
            rteIvaSelect.disabled = Boolean(state.isLegacy || state.isLocked || calculation.vatValue <= 0);
        }
        setText("[data-cnc-wizard-cuenta-rete-fuente-value]", clientPaymentMoney(
            calculation.retentions.find((retention) => retention.kind === "ReteFuente")?.value || 0));
        setText("[data-cnc-wizard-cuenta-rete-ica-value]", clientPaymentMoney(
            calculation.retentions.find((retention) => retention.kind === "ReteICA")?.value || 0));
        setText("[data-cnc-wizard-cuenta-rte-iva-value]", clientPaymentMoney(
            calculation.retentions.find((retention) => retention.kind === "RteIVA")?.value || 0));
        setText("[data-cnc-wizard-cuenta-tax-base]", clientPaymentMoney(calculation.taxBaseValue));
        setText("[data-cnc-wizard-cuenta-vat-base]", clientPaymentMoney(calculation.vatValue));
        setText("[data-cnc-wizard-cuenta-total-summary]", clientPaymentMoney(calculation.totalValue));
        setText("[data-cnc-wizard-cuenta-retentions-summary]", clientPaymentMoney(calculation.retentionValue));
        setText("[data-cnc-wizard-cuenta-payment-summary]", clientPaymentMoney(calculation.paymentValue));
        setText("[data-cnc-wizard-cuenta-balance-summary]", clientPaymentMoney(calculation.balanceValue));
        setText("[data-cnc-wizard-cuenta-difference-summary]", clientPaymentMoney(calculation.cashFlowDifferenceValue));
        const summary = modal.querySelector("[data-cnc-wizard-cuenta-summary]");
        if (summary) {
            summary.classList.toggle("is-balanced", calculation.isBalanced);
            summary.classList.toggle("is-over", !calculation.isBalanced);
        }
        const validation = modal.querySelector("[data-cnc-wizard-cuenta-validation]");
        if (validation) {
            validation.textContent = state.isLegacy
                ? (issues.length > 0
                    ? issues.join(" ")
                    : "Registro historico: los valores se muestran en solo lectura; aqui solo se actualiza la cuenta contable.")
                : issues.length > 0
                    ? issues.join(" ")
                    : `Cuadra con la salida bancaria de ${clientPaymentMoney(calculation.cashFlowExitValue)}.`;
            validation.classList.toggle("is-valid", issues.length === 0);
            validation.classList.toggle("is-invalid", issues.length > 0);
        }
        updateCuentaCobroWizardActions();
    };

    const markCuentaCobroWizardDirty = () => {
        if (!cashFlowWizardCuentaCobro) {
            return;
        }
        cashFlowWizardCuentaCobro.dirty = true;
        cashFlowWizardCuentaCobro.issues = [];
        renderCuentaCobroWizardPayload();
        updateCuentaCobroWizardSummary();
    };

    const cuentaCobroHasSiigoSupplier = (supplier) => Boolean(
        supplier
        && String(supplier.id || "").trim()
        && String(supplier.identification || "").trim()
        && (String(supplier.commercialName || "").trim()
            || String(supplier.name || "").trim()
            || String(supplier.displayName || "").trim()));

    const renderCuentaCobroWizardSupplier = () => {
        const modal = ensureCashFlowWizardModal();
        const state = cashFlowWizardCuentaCobro;
        const results = modal.querySelector("[data-cnc-wizard-cuenta-supplier-results]");
        const selected = modal.querySelector("[data-cnc-wizard-cuenta-supplier-selected]");
        const feedback = modal.querySelector("[data-cnc-wizard-cuenta-supplier-feedback]");
        if (!state || !results || !selected) {
            return;
        }

        const supplier = state.supplier;
        selected.innerHTML = "";
        selected.hidden = !supplier;
        if (supplier) {
            const label = document.createElement("strong");
            const change = document.createElement("button");
            label.textContent = supplierPaymentLabel(supplier);
            change.type = "button";
            change.className = "btn btn-sm btn-outline-secondary";
            change.textContent = "Cambiar";
            change.disabled = Boolean(state.isLocked);
            change.addEventListener("click", () => {
                state.supplier = null;
                state.supplierCandidates = [];
                state.supplierSearchMessage = "Busca el proveedor por nombre o NIT en Siigo.";
                state.form.receptor = "";
                state.form.nitOCedula = "";
                const query = modal.querySelector("[data-cnc-wizard-cuenta-supplier-query]");
                const receptor = modal.querySelector("[data-cnc-wizard-cuenta-receptor]");
                const nit = modal.querySelector("[data-cnc-wizard-cuenta-nit]");
                if (receptor) {
                    receptor.value = "";
                }
                if (nit) {
                    nit.value = "";
                }
                if (query) {
                    query.value = state.supplierQuery || state.form.receptor || "";
                    query.focus();
                }
                renderCuentaCobroWizardSupplier();
                markCuentaCobroWizardDirty();
            });
            selected.append(label, change);
        }

        results.innerHTML = "";
        const candidates = supplier
            ? []
            : (state.supplierCandidates || []).filter(cuentaCobroHasSiigoSupplier);
        results.hidden = candidates.length === 0;
        candidates.forEach((candidate) => {
            const button = document.createElement("button");
            const title = document.createElement("strong");
            const detail = document.createElement("small");
            button.type = "button";
            button.className = "cnc-party-picker__option";
            title.textContent = candidate.commercialName || candidate.name || candidate.displayName || "Proveedor Siigo";
            detail.textContent = [
                candidate.identification,
                Number(candidate.branchOffice || 0) > 0 ? `Sucursal ${candidate.branchOffice}` : ""
            ].filter(Boolean).join(" - ");
            button.append(title, detail);
            button.addEventListener("click", () => {
                state.supplierSearchSequence = Number(state.supplierSearchSequence || 0) + 1;
                state.supplier = candidate;
                state.supplierCandidates = [];
                state.supplierQuery = supplierPaymentLabel(candidate);
                state.supplierSearchMessage = "Proveedor seleccionado desde Siigo.";
                state.form.receptor = candidate.commercialName || candidate.name || candidate.displayName || "";
                state.form.nitOCedula = String(candidate.identification || "");
                const query = modal.querySelector("[data-cnc-wizard-cuenta-supplier-query]");
                const receptor = modal.querySelector("[data-cnc-wizard-cuenta-receptor]");
                const nit = modal.querySelector("[data-cnc-wizard-cuenta-nit]");
                if (query) {
                    query.value = state.supplierQuery;
                }
                if (receptor) {
                    receptor.value = state.form.receptor;
                }
                if (nit) {
                    nit.value = state.form.nitOCedula;
                }
                renderCuentaCobroWizardSupplier();
                markCuentaCobroWizardDirty();
            });
            results.appendChild(button);
        });
        if (feedback) {
            feedback.textContent = state.supplierSearchMessage || "";
        }
    };

    const searchCuentaCobroWizardSuppliers = async (query) => {
        const state = cashFlowWizardCuentaCobro;
        if (!state || !siigoSupplierSearchUrl || query.length < 2 || state.isLocked) {
            return;
        }
        const sequence = Number(state.supplierSearchSequence || 0) + 1;
        state.supplierSearchSequence = sequence;
        state.supplierSearchMessage = "Buscando proveedores en Siigo...";
        renderCuentaCobroWizardSupplier();
        try {
            const response = await fetch(siigoSupplierSearchUrl, {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify({ query, top: 10 })
            });
            const payload = await response.json().catch(() => ({}));
            if (!response.ok) {
                throw new Error(payload.detail || payload.message || "No fue posible buscar proveedores en Siigo.");
            }
            if (cashFlowWizardCuentaCobro !== state || state.supplierSearchSequence !== sequence) {
                return;
            }
            state.supplierCandidates = (payload.items || [])
                .filter((candidate) => candidate?.active !== false && cuentaCobroHasSiigoSupplier(candidate));
            state.supplierSearchMessage = state.supplierCandidates.length === 0
                ? "No se encontro el proveedor. Crealo o activalo primero en Siigo y vuelve a buscarlo aqui."
                : "Selecciona el proveedor correcto de la lista de Siigo.";
            renderCuentaCobroWizardSupplier();
        } catch (error) {
            if (cashFlowWizardCuentaCobro === state && state.supplierSearchSequence === sequence) {
                state.supplierCandidates = [];
                state.supplierSearchMessage = error instanceof Error ? error.message : "No fue posible buscar proveedores en Siigo.";
                renderCuentaCobroWizardSupplier();
            }
        }
    };

    const scheduleCuentaCobroWizardSupplierSearch = (query) => {
        const state = cashFlowWizardCuentaCobro;
        if (!state) {
            return;
        }
        window.clearTimeout(state.supplierSearchTimer || 0);
        if (query.length < 2) {
            state.supplierCandidates = [];
            state.supplierSearchMessage = "Escribe al menos dos caracteres para buscar en Siigo.";
            renderCuentaCobroWizardSupplier();
            return;
        }
        state.supplierSearchTimer = window.setTimeout(
            () => searchCuentaCobroWizardSuppliers(query),
            280);
    };

    const bindCuentaCobroWizardEditor = () => {
        const modal = ensureCashFlowWizardModal();
        const state = cashFlowWizardCuentaCobro;
        if (!state?.loaded) {
            return;
        }

        const bindText = (selector, key) => {
            modal.querySelector(selector)?.addEventListener("input", (event) => {
                state.form[key] = event.currentTarget.value;
                markCuentaCobroWizardDirty();
            });
        };
        if (!state.isLegacy && !state.isLocked) {
            const supplierQuery = modal.querySelector("[data-cnc-wizard-cuenta-supplier-query]");
            supplierQuery?.addEventListener("input", (event) => {
                state.supplierQuery = String(event.currentTarget.value || "");
                state.supplier = null;
                state.supplierCandidates = [];
                scheduleCuentaCobroWizardSupplierSearch(state.supplierQuery.trim());
                markCuentaCobroWizardDirty();
            });
            bindText("[data-cnc-wizard-cuenta-observaciones]", "observaciones");
            bindText("[data-cnc-wizard-cuenta-fecha-emision]", "fechaEmisionValue");
            const totalInput = modal.querySelector("[data-cnc-wizard-cuenta-total]");
            totalInput?.addEventListener("input", () => {
                const value = Number(totalInput.value || 0);
                state.form.valorTotal = Number.isFinite(value) ? Math.max(0, value) : 0;
                markCuentaCobroWizardDirty();
            });
            const vatInput = modal.querySelector("[data-cnc-wizard-cuenta-iva]");
            vatInput?.addEventListener("input", () => {
                const value = Number(vatInput.value || 0);
                state.form.valorIva = Number.isFinite(value) ? Math.max(0, value) : 0;
                markCuentaCobroWizardDirty();
            });
            [
                ["[data-cnc-wizard-cuenta-cloud]", "cloudValue"],
                ["[data-cnc-wizard-cuenta-copiers]", "copiersValue"]
            ].forEach(([selector, key]) => {
                const input = modal.querySelector(selector);
                input?.addEventListener("input", () => {
                    const value = Number(input.value || 0);
                    state.form[key] = Number.isFinite(value) ? Math.max(0, value) : 0;
                    markCuentaCobroWizardDirty();
                });
            });
            const categorySelect = modal.querySelector("[data-cnc-wizard-cuenta-category]");
            categorySelect?.addEventListener("change", () => {
                state.form.categoryValue = String(categorySelect.value || "");
                markCuentaCobroWizardDirty();
            });
        }
        const accountSelect = modal.querySelector("[data-cnc-wizard-cuenta-account]");
        if (!state.isLocked) {
            accountSelect?.addEventListener("change", () => {
                state.form.accountCode = accountSelect.value || "";
                markCuentaCobroWizardDirty();
            });
        }
        if (!state.isLegacy && !state.isLocked) {
            [
                ["[data-cnc-wizard-cuenta-rete-fuente]", "reteFuenteTaxId"],
                ["[data-cnc-wizard-cuenta-rete-ica]", "reteIcaTaxId"],
                ["[data-cnc-wizard-cuenta-rte-iva]", "rteIvaTaxId"]
            ].forEach(([selector, key]) => {
                modal.querySelector(selector)?.addEventListener("change", (event) => {
                    state.form[key] = Number(event.currentTarget.value || 0);
                    markCuentaCobroWizardDirty();
                });
            });
        }
        renderCuentaCobroWizardSupplier();
    };

    const renderCashFlowWizardCuentaCobro = (row, message = "", tone = "info") => {
        const modal = ensureCashFlowWizardModal();
        setCashFlowWizardTitle("Documento soporte");
        const card = modal.querySelector("[data-cnc-cashflow-wizard-card]");
        const count = modal.querySelector("[data-cnc-cashflow-wizard-count]");
        const bar = modal.querySelector("[data-cnc-cashflow-wizard-bar]");
        cashFlowWizardMode = "cuenta-cobro";
        setCashFlowWizardProcessActions();
        if (count) {
            count.textContent = `Cuenta de cobro ${cashFlowWizardIndex + 1} de ${cashFlowWizardRows.length}`;
        }
        if (bar) {
            bar.style.width = `${Math.round((cashFlowWizardIndex + 1) / Math.max(cashFlowWizardRows.length, 1) * 100)}%`;
        }

        const existingState = cashFlowWizardCuentaCobro?.row === row
            ? cashFlowWizardCuentaCobro
            : null;
        const processRow = existingState?.processRow || findCuentaCobroRowForCashFlow(row);
        cashFlowWizardCuentaCobro = existingState || {
            row,
            processRow,
            editorRow: null,
            accountCode: processRow?.dataset.accountCode || "",
            form: null,
            reteFuenteOptions: [],
            reteIcaOptions: [],
            rteIvaOptions: [],
            retentionAccountCodes: {},
            loaded: false,
            loading: false,
            loadFailed: false,
            saving: false,
            processing: false,
            dirty: false,
            issues: [],
            payloadJson: "",
            responseJson: ""
        };
        const state = cashFlowWizardCuentaCobro;
        const form = state.form || {};
        const accountHtml = accountOptionsHtml(cuentaCobroAccount);
        const editorLocked = state.isLegacy || state.isLocked;
        const legacyReadonly = editorLocked ? " readonly" : "";
        const legacyDisabled = editorLocked ? " disabled" : "";
        const editorContent = state.loaded
            ? `
                <div class="cnc-cuenta-expense-editor">
                    ${state.isLegacy
                        ? `<div class="cnc-cuenta-expense-editor__legacy">
                            <strong>Registro historico</strong>
                            <span>Los datos y retenciones permanecen en su fuente original. Solo puedes actualizar la cuenta contable; no se creara otro gasto.</span>
                        </div>`
                        : ""}
                     ${state.isLocked
                         ? `<div class="cnc-cuenta-expense-editor__legacy">
                             <strong>${state.needsSiigoVerification
                                 ? "Verificacion manual requerida"
                                 : state.siigoDocumentInProgress
                                     ? "Documento soporte en proceso"
                                     : "Documento ya enviado"}</strong>
                             <span>${state.needsSiigoVerification
                                 ? "No reintentes el envio. Verifica primero en Siigo si el documento soporte fue creado."
                                 : state.siigoDocumentInProgress
                                     ? "Otra ejecucion conserva la reserva de envio; los valores permanecen bloqueados."
                                     : "Los valores quedan congelados para conservar la trazabilidad con Siigo."}</span>
                         </div>`
                         : ""}
                    <div class="cnc-cuenta-expense-editor__grid">
                        <div class="cnc-modal__field cnc-cuenta-expense-editor__field--wide">
                            <span>Proveedor en Siigo</span>
                            <div class="cnc-party-picker">
                                <input class="form-control" type="search" autocomplete="off" placeholder="Escribe nombre o NIT para buscar en Siigo" value="${escapeHtml(state.supplierQuery || "")}"${legacyReadonly} data-cnc-wizard-cuenta-supplier-query />
                                <div class="cnc-party-picker__results" data-cnc-wizard-cuenta-supplier-results hidden></div>
                            </div>
                            <div class="cnc-party-picker__selected" data-cnc-wizard-cuenta-supplier-selected hidden></div>
                            <small data-cnc-wizard-cuenta-supplier-feedback></small>
                        </div>
                        <label class="cnc-modal__field">
                            <span>Nombre del receptor</span>
                            <input class="form-control" type="text" value="${escapeHtml(form.receptor || "")}" readonly data-cnc-wizard-cuenta-receptor />
                        </label>
                        <label class="cnc-modal__field">
                            <span>NIT o cedula</span>
                            <input class="form-control" type="text" value="${escapeHtml(form.nitOCedula || "")}" readonly data-cnc-wizard-cuenta-nit />
                        </label>
                        <label class="cnc-modal__field">
                            <span>Fecha de emision</span>
                            <input class="form-control" type="date" value="${escapeHtml(form.fechaEmisionValue || "")}"${legacyReadonly} data-cnc-wizard-cuenta-fecha-emision />
                        </label>
                        <label class="cnc-modal__field">
                            <span>Fecha de pago (flujo de caja)</span>
                            <input class="form-control" type="date" value="${escapeHtml(form.fechaPagoValue || "")}" readonly data-cnc-wizard-cuenta-fecha-pago />
                        </label>
                        <label class="cnc-modal__field">
                            <span>Valor total</span>
                            <input class="form-control" type="number" min="0" step="0.01" value="${escapeHtml(String(form.valorTotal || ""))}"${legacyReadonly} data-cnc-wizard-cuenta-total />
                        </label>
                        <label class="cnc-modal__field">
                            <span>Valor IVA incluido en el total</span>
                            <input class="form-control" type="number" min="0" step="0.01" value="${escapeHtml(String(form.valorIva || 0))}"${legacyReadonly} data-cnc-wizard-cuenta-iva />
                            <small>La RteIVA se calcula sobre este valor.</small>
                        </label>
                        <label class="cnc-modal__field">
                            <span>Pago calculado</span>
                            <input class="form-control" type="number" step="0.01" readonly data-cnc-wizard-cuenta-payment-value />
                        </label>
                        <label class="cnc-modal__field cnc-cuenta-expense-editor__field--wide">
                            <span>Cuenta contable del gasto</span>
                            <select class="form-select"${state.isLocked ? " disabled" : ""} data-cnc-wizard-cuenta-account>${accountHtml}</select>
                        </label>
                        <label class="cnc-modal__field">
                            <span>Cloud <small>(base sin IVA)</small></span>
                            <input class="form-control" type="number" min="0" step="0.01" value="${escapeHtml(String(form.cloudValue || 0))}"${legacyReadonly} data-cnc-wizard-cuenta-cloud />
                        </label>
                        <label class="cnc-modal__field">
                            <span>Copiers <small>(base sin IVA)</small></span>
                            <input class="form-control" type="number" min="0" step="0.01" value="${escapeHtml(String(form.copiersValue || 0))}"${legacyReadonly} data-cnc-wizard-cuenta-copiers />
                        </label>
                        <label class="cnc-modal__field cnc-cuenta-expense-editor__field--wide">
                            <span>Categoria del gasto</span>
                            <select class="form-select"${legacyDisabled} data-cnc-wizard-cuenta-category>${supplierExpenseCategoryOptionsHtml()}</select>
                        </label>
                        <label class="cnc-modal__field">
                            <span>ReteFuente <small>(base <span data-cnc-wizard-cuenta-tax-base></span>)</small></span>
                            <div class="cnc-retention-editor">
                                <select class="form-select"${legacyDisabled} data-cnc-wizard-cuenta-rete-fuente>
                                    ${clientPaymentRetentionOptionsHtml(state.reteFuenteOptions, form.reteFuenteTaxId, "Sin ReteFuente")}
                                </select>
                                <small data-cnc-wizard-cuenta-rete-fuente-value></small>
                            </div>
                        </label>
                        <label class="cnc-modal__field">
                            <span>ReteICA <small>(base sin IVA)</small></span>
                            <div class="cnc-retention-editor">
                                <select class="form-select"${legacyDisabled} data-cnc-wizard-cuenta-rete-ica>
                                    ${clientPaymentRetentionOptionsHtml(state.reteIcaOptions, form.reteIcaTaxId, "Sin ReteICA")}
                                </select>
                                <small data-cnc-wizard-cuenta-rete-ica-value></small>
                            </div>
                        </label>
                        <label class="cnc-modal__field">
                            <span>RteIVA <small>(base <span data-cnc-wizard-cuenta-vat-base></span>)</small></span>
                            <div class="cnc-retention-editor">
                                <select class="form-select"${legacyDisabled} data-cnc-wizard-cuenta-rte-iva>
                                    ${clientPaymentRetentionOptionsHtml(state.rteIvaOptions, form.rteIvaTaxId, "Sin RteIVA")}
                                </select>
                                <small data-cnc-wizard-cuenta-rte-iva-value></small>
                            </div>
                        </label>
                        <label class="cnc-modal__field cnc-cuenta-expense-editor__field--wide">
                            <span>Observaciones</span>
                            <textarea class="form-control" rows="2"${legacyReadonly} data-cnc-wizard-cuenta-observaciones>${escapeHtml(form.observaciones || "")}</textarea>
                        </label>
                    </div>
                    <div class="cnc-table-wrap cnc-cuenta-expense-editor__summary" data-cnc-wizard-cuenta-summary>
                        <table class="table align-middle">
                            <thead>
                                <tr>
                                    <th class="text-end">Total</th>
                                    <th class="text-end">Retenciones</th>
                                    <th class="text-end">Pago</th>
                                    <th class="text-end">Saldo</th>
                                    <th class="text-end">Diferencia vs salida</th>
                                </tr>
                            </thead>
                            <tbody>
                                <tr>
                                    <td class="text-end" data-cnc-wizard-cuenta-total-summary></td>
                                    <td class="text-end" data-cnc-wizard-cuenta-retentions-summary></td>
                                    <td class="text-end" data-cnc-wizard-cuenta-payment-summary></td>
                                    <td class="text-end" data-cnc-wizard-cuenta-balance-summary></td>
                                    <td class="text-end" data-cnc-wizard-cuenta-difference-summary></td>
                                </tr>
                            </tbody>
                        </table>
                    </div>
                    <p class="cnc-cuenta-expense-editor__validation" data-cnc-wizard-cuenta-validation></p>
                </div>`
            : `
                <div class="cnc-payment-empty cnc-cuenta-expense-editor__loading">
                    <p>${state.loadFailed ? "No fue posible cargar el formulario del documento soporte." : "Consultando movimiento, gasto y tarifas Siigo..."}</p>
                    ${state.loadFailed
                        ? `<button type="button" class="btn btn-outline-secondary" data-cnc-wizard-cuenta-retry>Cargar nuevamente</button>`
                        : ""}
                </div>`;

        if (card) {
            card.innerHTML = `
                <div class="cnc-wizard-process">
                    <header class="cnc-wizard-process__summary">
                        <div>
                            <span>${escapeHtml(state.editorRow?.movementDateDisplay || row.dataset.dateDisplay || "Sin fecha")}</span>
                            <p>${escapeHtml(resolveCashFlowRowLabel(row))}</p>
                        </div>
                        <strong>${escapeHtml(row.dataset.amountLabel || money(Number(state.editorRow?.cashFlowExitValue || row.dataset.exitValue || row.dataset.entryValue || 0)))}</strong>
                    </header>
                    ${editorContent}
                    <ul class="cnc-issue-list" data-cnc-wizard-cuenta-issues hidden></ul>
                    <details class="cnc-json-preview" hidden data-cnc-wizard-cuenta-preview>
                        <summary>Payload / respuesta</summary>
                        <pre data-cnc-wizard-cuenta-payload></pre>
                        <pre data-cnc-wizard-cuenta-response></pre>
                    </details>
                    <div class="cnc-wizard-process__actions">
                        <button type="button" class="btn btn-outline-secondary" data-cnc-wizard-cuenta-back>Volver</button>
                        <button type="button" class="btn btn-primary" data-cnc-wizard-cuenta-save hidden>${state.isLegacy ? "Guardar cuenta historica" : "Guardar en Dataverse"}</button>
                        <button type="button" class="btn btn-danger" data-cnc-wizard-cuenta-complete hidden>Registrar documento soporte y pago</button>
                        <button type="button" class="btn btn-primary" data-cnc-wizard-cuenta-payment hidden>Reintentar pago</button>
                    </div>
                </div>`;
        }

        const accountSelect = modal.querySelector("[data-cnc-wizard-cuenta-account]");
        if (accountSelect) {
            accountSelect.value = form.accountCode || "";
        }
        const categorySelect = modal.querySelector("[data-cnc-wizard-cuenta-category]");
        if (categorySelect) {
            categorySelect.value = form.categoryValue || "";
        }
        bindCuentaCobroWizardEditor();
        modal.querySelector("[data-cnc-wizard-cuenta-retry]")?.addEventListener("click", () => {
            state.loadFailed = false;
            loadCashFlowWizardCuentaCobroEditor();
        });
        modal.querySelector("[data-cnc-wizard-cuenta-back]")?.addEventListener("click", () => {
            cashFlowWizardMode = "rows";
            cashFlowWizardCuentaCobro = null;
            renderCashFlowWizard();
        });
        modal.querySelector("[data-cnc-wizard-cuenta-save]")?.addEventListener(
            "click",
            state.isLegacy ? saveCashFlowWizardLegacyCuentaCobroAccount : saveCashFlowWizardCuentaCobroExpense);
        modal.querySelector("[data-cnc-wizard-cuenta-complete]")?.addEventListener("click", registerCashFlowWizardCuentaCobroInSiigo);
        modal.querySelector("[data-cnc-wizard-cuenta-payment]")?.addEventListener("click", () => runCashFlowWizardCuentaCobroAction(cuentaCobroPaymentUrl, {
            loadingMessage: "Reintentando pago del documento soporte...",
            successMessage: "Pago del documento soporte enviado.",
            completeOnSuccess: true
        }));
        renderCuentaCobroWizardPayload();
        updateCuentaCobroWizardSummary();
        if (message) {
            setCashFlowWizardMessage(message, tone);
        }
        if (!state.loaded && !state.loading && !state.loadFailed) {
            loadCashFlowWizardCuentaCobroEditor();
        }
    };

    const loadCashFlowWizardCuentaCobroEditor = async () => {
        const state = cashFlowWizardCuentaCobro;
        if (!state?.row || state.loading) {
            return;
        }
        if (!cuentaCobroEditorUrl) {
            state.loadFailed = true;
            renderCashFlowWizardCuentaCobro(state.row, "No se encontro la ruta para cargar el documento soporte.", "error");
            return;
        }

        state.loading = true;
        state.loadFailed = false;
        renderCashFlowWizardCuentaCobro(state.row, "Cargando formulario del documento soporte...", "info");
        try {
            const response = await fetch(cuentaCobroEditorUrl, {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify(cuentaCobroWizardRequest())
            });
            const payload = await response.json().catch(() => ({}));
            if (!response.ok) {
                throw new Error(payload.detail || payload.message || "No fue posible cargar el formulario del documento soporte.");
            }
            if (cashFlowWizardCuentaCobro !== state) {
                return;
            }

            state.loading = false;
            hydrateCuentaCobroWizardEditor(payload);
            renderCashFlowWizardCuentaCobro(state.row, payload.message || "", "success");
        } catch (error) {
            if (cashFlowWizardCuentaCobro !== state) {
                return;
            }
            state.loading = false;
            state.loadFailed = true;
            renderCashFlowWizardCuentaCobro(
                state.row,
                error instanceof Error ? error.message : "Ocurrio un error inesperado.",
                "error");
        }
    };

    const saveCashFlowWizardLegacyCuentaCobroAccount = async () => {
        const state = cashFlowWizardCuentaCobro;
        const request = cuentaCobroWizardRequest();
        const accountCode = String(state?.form?.accountCode || "").trim();
        if (!state?.isLegacy || !request.recordId || !cuentaCobroClassificationUrl) {
            setCashFlowWizardMessage("No se encontro el registro historico o la ruta para guardar su cuenta.", "error");
            return;
        }
        if (!accountCode) {
            setCashFlowWizardMessage("Selecciona la cuenta contable del registro historico.", "info");
            return;
        }

        state.saving = true;
        state.issues = [];
        renderCuentaCobroWizardPayload();
        updateCuentaCobroWizardActions();
        setCashFlowWizardMessage("Guardando la cuenta contable en el registro historico...", "info");
        try {
            const response = await fetch(cuentaCobroClassificationUrl, {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                 body: JSON.stringify({
                     recordId: request.recordId,
                     recordSource: request.recordSource,
                     concurrencyToken: request.concurrencyToken,
                     accountCode
                 })
            });
            const payload = await response.json().catch(() => ({}));
            state.issues = payload.issues || [];
            if (!response.ok || payload.isSuccess === false) {
                throw new Error(payload.detail || payload.message || "No fue posible guardar la cuenta contable historica.");
            }

            state.saving = false;
            mergeCuentaCobroWizardActionRow(payload.row || {
                recordId: request.recordId,
                recordSource: request.recordSource,
                accountCode
            });
            state.form.accountCode = payload.row?.accountCode || accountCode;
            state.accountCode = state.form.accountCode;
            state.dirty = false;
            renderCashFlowWizardCuentaCobro(
                state.row,
                payload.message || "Cuenta contable del registro historico guardada.",
                "success");
        } catch (error) {
            state.saving = false;
            setCashFlowWizardMessage(error instanceof Error ? error.message : "Ocurrio un error inesperado.", "error");
            renderCuentaCobroWizardPayload();
            updateCuentaCobroWizardActions();
        }
    };

    const saveCashFlowWizardCuentaCobroExpense = async () => {
        const state = cashFlowWizardCuentaCobro;
        if (state?.isLegacy) {
            saveCashFlowWizardLegacyCuentaCobroAccount();
            return;
        }
        if (!state?.loaded || !cuentaCobroExpenseSaveUrl) {
            setCashFlowWizardMessage("No se encontro el formulario o la ruta para guardar en Dataverse.", "error");
            return;
        }

        const calculation = calculateCuentaCobroWizardExpense();
        const validationIssues = validateCuentaCobroWizardExpense(calculation);
        if (validationIssues.length > 0) {
            state.issues = validationIssues;
            renderCuentaCobroWizardPayload();
            setCashFlowWizardMessage("Corrige los datos antes de guardar en Dataverse.", "error");
            updateCuentaCobroWizardActions();
            return;
        }

        const request = cuentaCobroWizardRequest();
        const form = state.form;
        const body = {
            recordId: request.recordId,
            recordSource: request.recordSource,
            concurrencyToken: request.concurrencyToken,
            cashFlowRecordId: request.cashFlowRecordId,
            cashFlowExternalKey: request.cashFlowExternalKey,
            receptor: String(form.receptor || "").trim(),
            nitOCedula: String(form.nitOCedula || "").trim(),
            observaciones: String(form.observaciones || "").trim(),
            fechaEmisionValue: String(form.fechaEmisionValue || ""),
            fechaPagoValue: String(form.fechaPagoValue || ""),
            valorTotal: calculation.totalValue,
            valorIva: calculation.vatValue,
            valorPago: calculation.paymentValue,
            cloudValue: Number(form.cloudValue || 0),
            copiersValue: Number(form.copiersValue || 0),
            categoryValue: String(form.categoryValue || ""),
            accountCode: String(form.accountCode || ""),
            siigoSupplierId: String(state.supplier?.id || ""),
            siigoSupplierName: String(state.supplier?.commercialName || state.supplier?.name || state.supplier?.displayName || ""),
            siigoSupplierIdentification: String(state.supplier?.identification || ""),
            siigoSupplierBranchOffice: Number(state.supplier?.branchOffice || 0),
            retentions: calculation.retentions
        };

        state.saving = true;
        state.issues = [];
        renderCuentaCobroWizardPayload();
        updateCuentaCobroWizardActions();
        setCashFlowWizardMessage("Guardando gasto, distribucion y retenciones en Dataverse...", "info");
        try {
            const response = await fetch(cuentaCobroExpenseSaveUrl, {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify(body)
            });
            const payload = await response.json().catch(() => ({}));
            state.issues = payload.issues || [];
            if (!response.ok || payload.isSuccess === false) {
                throw new Error(payload.detail || payload.message || "No fue posible guardar la cuenta de cobro.");
            }
            if (!payload.row?.recordId) {
                throw new Error("Dataverse no devolvio el identificador del gasto guardado.");
            }

            state.saving = false;
            hydrateCuentaCobroWizardEditor({
                row: payload.row,
                reteFuenteOptions: state.reteFuenteOptions,
                reteIcaOptions: state.reteIcaOptions,
                rteIvaOptions: state.rteIvaOptions
            });
            state.dirty = false;
            if (state.processRow) {
                state.processRow.dataset.recordId = payload.row.recordId;
                state.processRow.dataset.recordSource = payload.row.recordSource || request.recordSource || "";
                state.processRow.dataset.accountCode = payload.row.accountCode || body.accountCode;
            }
            renderCashFlowWizardCuentaCobro(
                state.row,
                payload.message || "Gasto, distribucion y retenciones guardados en Dataverse.",
                "success");
            return true;
        } catch (error) {
            state.saving = false;
            setCashFlowWizardMessage(error instanceof Error ? error.message : "Ocurrio un error inesperado.", "error");
            renderCuentaCobroWizardPayload();
            updateCuentaCobroWizardActions();
            return false;
        }
    };

    const mergeCuentaCobroWizardActionRow = (row) => {
        const state = cashFlowWizardCuentaCobro;
        if (!state || !row) {
            return;
        }
         state.editorRow = {
             ...(state.editorRow || {}),
             ...row
         };
         const automationState = String(state.editorRow.automationState || "").trim().toLowerCase();
         const reviewReason = String(state.editorRow.reviewReason || "").toLowerCase();
         state.needsSiigoVerification = automationState === "verificaciondocumentosoportesiigopendiente"
             || reviewReason.includes("[siigo_support_document_write_ambiguous]");
         state.siigoDocumentInProgress = automationState === "procesandodocumentosoportesiigo";
         state.isLocked = Boolean(
             state.editorRow.siigoDocumentId
             || state.editorRow.siigoDocumentName
             || state.editorRow.siigoPaymentId
             || state.editorRow.siigoPaymentName
             || state.needsSiigoVerification
             || state.siigoDocumentInProgress);
         state.accountCode = row.accountCode || state.accountCode || "";
         if (state.form && row.accountCode) {
             state.form.accountCode = row.accountCode;
        }
        if (state.processRow) {
             state.processRow.dataset.recordId = row.recordId || state.processRow.dataset.recordId || "";
             state.processRow.dataset.recordSource = row.recordSource || state.processRow.dataset.recordSource || "";
             state.processRow.dataset.concurrencyToken = row.concurrencyToken || state.processRow.dataset.concurrencyToken || "";
             state.processRow.dataset.accountCode = row.accountCode || state.processRow.dataset.accountCode || "";
         }
    };

    const runCashFlowWizardCuentaCobroAction = async (url, options = {}) => {
        const state = cashFlowWizardCuentaCobro;
        if (!state?.row || !cuentaCobroWizardRequest().recordId || !url) {
            setCashFlowWizardMessage("No se encontro el gasto guardado o la ruta de proceso.", "error");
            return false;
        }

        state.processing = true;
        state.issues = [];
        state.payloadJson = "";
        state.responseJson = "";
        renderCuentaCobroWizardPayload();
        updateCuentaCobroWizardActions();
        setCashFlowWizardMessage(options.loadingMessage || "Procesando cuenta de cobro...", "info");

        try {
            const response = await fetch(url, {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify(cuentaCobroWizardRequest())
            });
            const payload = await response.json().catch(() => ({}));
            state.issues = payload.issues || [];
            state.payloadJson = payload.payloadJson || "";
            state.responseJson = payload.responseJson || "";
            state.isReadyForSiigo = payload.isReadyForSiigo === true;
            mergeCuentaCobroWizardActionRow(payload.row);
            renderCuentaCobroWizardPayload();
            const hostRow = state.processRow || state.row;
            renderCuentaCobroActionPayload(hostRow, payload);
            renderIssueList(hostRow, "[data-cnc-cuenta-cobro-issues]", payload.issues || []);
            if (!response.ok) {
                throw new Error(payload.detail || payload.message || options.errorMessage || "No fue posible procesar la cuenta de cobro.");
            }

            const success = payload.isSuccess !== false && !(payload.issues || []).length;
            const displayMessage = payload.message || options.successMessage || "Accion finalizada.";
            state.processing = false;
            if (success && options.completeOnSuccess) {
                completeCashFlowWizardRow(state.row, payload.row, displayMessage);
                return true;
            }

            setCashFlowWizardMessage(displayMessage, success ? "success" : "info");
            updateCuentaCobroWizardActions();
            return success;
        } catch (error) {
            state.processing = false;
            setCashFlowWizardMessage(error instanceof Error ? error.message : "Ocurrio un error inesperado.", "error");
            updateCuentaCobroWizardActions();
            return false;
        }
    };

    const registerCashFlowWizardCuentaCobroInSiigo = async () => {
        const state = cashFlowWizardCuentaCobro;
        if (!state || state.isLegacy || state.isLocked) {
            setCashFlowWizardMessage("Esta cuenta de cobro no esta disponible para registrar en Siigo.", "error");
            return;
        }

        if (state.dirty || !cuentaCobroWizardRequest().recordId) {
            const saved = await saveCashFlowWizardCuentaCobroExpense();
            if (!saved) {
                return;
            }
        }

        const preflightOk = await runCashFlowWizardCuentaCobroAction(cuentaCobroPreflightUrl, {
            loadingMessage: "Validando documento soporte, distribucion, retenciones y pago...",
            successMessage: "Validacion correcta. Registrando los documentos en Siigo..."
        });
        if (!preflightOk) {
            return;
        }

        await runCashFlowWizardCuentaCobroAction(cuentaCobroSendUrl, {
            loadingMessage: "Paso 1 de 2: creando documento soporte; despues se registrara el pago...",
            successMessage: "Documento soporte y comprobante de pago registrados en Siigo.",
            completeOnSuccess: true
        });
    };

    const accountingVoucherRowHost = (row) => row?.closest?.("tr[data-record-id]") || row || null;

    const accountingVoucherRequiresThirdParty = (row) => {
        const host = accountingVoucherRowHost(row);
        if (String(host?.dataset?.sourceKind || "").trim().toLowerCase() === "traslado"
            || String(host?.dataset?.currentType || "").trim().toLowerCase() === "traslado-interno") {
            return false;
        }
        const direction = String(host?.dataset?.direction || "").trim().toLowerCase();
        const currentType = String(host?.dataset?.currentType || "").trim().toLowerCase();
        return direction === "salida" || currentType === "comprobante-contable";
    };

    const accountingVoucherThirdPartyFields = (thirdParty) => ({
        thirdPartyId: thirdParty?.id || "",
        thirdPartyIdentification: thirdParty?.identification || "",
        thirdPartyName: thirdParty?.commercialName || thirdParty?.name || thirdParty?.displayName || "",
        thirdPartyBranchOffice: Number.isFinite(Number(thirdParty?.branchOffice || 0))
            ? Math.max(0, Number(thirdParty?.branchOffice || 0))
            : 0
    });

    const accountingVoucherThirdPartySignature = (thirdParty) => {
        if (!thirdParty) {
            return "";
        }
        const fields = accountingVoucherThirdPartyFields(thirdParty);
        return [
            String(fields.thirdPartyId || "").trim().toLowerCase(),
            String(fields.thirdPartyIdentification || "").replace(/\D+/g, ""),
            String(fields.thirdPartyName || "").trim(),
            String(fields.thirdPartyBranchOffice || 0)
        ].join("|");
    };

    const accountingVoucherHasThirdParty = (thirdParty) => Boolean(
        String(thirdParty?.id || "").trim()
        && String(thirdParty?.identification || "").trim()
        && String(thirdParty?.commercialName || thirdParty?.name || thirdParty?.displayName || "").trim());

    const accountingVoucherThirdPartyFromRow = (row) => {
        const host = accountingVoucherRowHost(row);
        const id = host?.dataset?.thirdPartyId || "";
        const identification = host?.dataset?.thirdPartyIdentification || "";
        const name = host?.dataset?.thirdPartyName || "";
        if (!id || !identification) {
            return null;
        }
        return {
            id,
            identification,
            displayName: name,
            name,
            branchOffice: Math.max(0, Number(host?.dataset?.thirdPartyBranchOffice || 0))
        };
    };

    const setAccountingVoucherThirdPartyOnRow = (row, thirdParty) => {
        const host = accountingVoucherRowHost(row);
        if (!host?.dataset) {
            return;
        }
        const fields = accountingVoucherThirdPartyFields(thirdParty);
        host.dataset.thirdPartyId = fields.thirdPartyId;
        host.dataset.thirdPartyIdentification = fields.thirdPartyIdentification;
        host.dataset.thirdPartyName = fields.thirdPartyName;
        host.dataset.thirdPartyBranchOffice = String(fields.thirdPartyBranchOffice);
    };

    const renderCashFlowWizardAccountingVoucherThirdParty = () => {
        const modal = ensureCashFlowWizardModal();
        const results = modal.querySelector("[data-cnc-wizard-voucher-third-parties]");
        const selected = modal.querySelector("[data-cnc-wizard-voucher-third-party]");
        if (!results || !selected || !cashFlowWizardAccountingVoucher) {
            return;
        }

        const thirdParty = cashFlowWizardAccountingVoucher.thirdParty;
        selected.innerHTML = "";
        selected.hidden = !thirdParty;
        if (thirdParty) {
            const label = document.createElement("strong");
            const change = document.createElement("button");
            label.textContent = supplierPaymentLabel(thirdParty);
            change.type = "button";
            change.className = "btn btn-sm btn-outline-secondary";
            change.textContent = "Cambiar";
            change.addEventListener("click", () => {
                cashFlowWizardAccountingVoucher.thirdParty = null;
                cashFlowWizardAccountingVoucher.thirdPartyCandidates = [];
                const query = modal.querySelector("[data-cnc-wizard-voucher-third-party-query]");
                if (query) {
                    query.value = "";
                    query.focus();
                }
                renderCashFlowWizardAccountingVoucherThirdParty();
                updateAccountingVoucherWizardActions();
            });
            selected.append(label, change);
        }

        results.innerHTML = "";
        const candidates = thirdParty
            ? []
            : (cashFlowWizardAccountingVoucher.thirdPartyCandidates || []).filter(accountingVoucherHasThirdParty);
        results.hidden = candidates.length === 0;
        candidates.forEach((candidate) => {
            const button = document.createElement("button");
            const title = document.createElement("strong");
            const detail = document.createElement("small");
            button.type = "button";
            button.className = "cnc-party-picker__option";
            title.textContent = candidate.commercialName || candidate.name || candidate.displayName || "Tercero Siigo";
            detail.textContent = [
                candidate.identification,
                Number(candidate.branchOffice || 0) > 0 ? `Sucursal ${candidate.branchOffice}` : ""
            ].filter(Boolean).join(" - ");
            button.append(title, detail);
            button.addEventListener("click", () => {
                cashFlowWizardAccountingVoucher.thirdPartySearchSequence = Number(cashFlowWizardAccountingVoucher.thirdPartySearchSequence || 0) + 1;
                cashFlowWizardAccountingVoucher.thirdParty = candidate;
                cashFlowWizardAccountingVoucher.thirdPartyCandidates = [];
                const query = modal.querySelector("[data-cnc-wizard-voucher-third-party-query]");
                if (query) {
                    query.value = supplierPaymentLabel(candidate);
                }
                renderCashFlowWizardAccountingVoucherThirdParty();
                updateAccountingVoucherWizardActions();
            });
            results.appendChild(button);
        });
    };

    const searchCashFlowWizardAccountingVoucherThirdParties = async (query) => {
        if (!cashFlowWizardAccountingVoucher || !siigoSupplierSearchUrl || query.length < 2) {
            return;
        }
        const state = cashFlowWizardAccountingVoucher;
        const sequence = Number(state.thirdPartySearchSequence || 0) + 1;
        state.thirdPartySearchSequence = sequence;
        try {
            const response = await fetch(siigoSupplierSearchUrl, {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify({ query, top: 10 })
            });
            const payload = await response.json().catch(() => ({}));
            if (!response.ok) {
                throw new Error(payload.detail || payload.message || "No fue posible buscar terceros en Siigo.");
            }
            if (cashFlowWizardAccountingVoucher !== state || state.thirdPartySearchSequence !== sequence) {
                return;
            }
            state.thirdPartyCandidates = (payload.items || [])
                .filter((candidate) => candidate?.active !== false && accountingVoucherHasThirdParty(candidate));
            renderCashFlowWizardAccountingVoucherThirdParty();
            if (state.thirdPartyCandidates.length === 0) {
                setCashFlowWizardMessage("No se encontraron terceros activos con ID y NIT en Siigo.", "info");
            }
        } catch (error) {
            if (cashFlowWizardAccountingVoucher === state && state.thirdPartySearchSequence === sequence) {
                state.thirdPartyCandidates = [];
                renderCashFlowWizardAccountingVoucherThirdParty();
                setCashFlowWizardMessage(error instanceof Error ? error.message : "No fue posible buscar terceros.", "error");
            }
        }
    };

    const scheduleCashFlowWizardAccountingVoucherThirdPartySearch = (query) => {
        if (!cashFlowWizardAccountingVoucher) {
            return;
        }
        window.clearTimeout(cashFlowWizardAccountingVoucher.thirdPartySearchTimer || 0);
        if (query.length < 2) {
            cashFlowWizardAccountingVoucher.thirdPartyCandidates = [];
            renderCashFlowWizardAccountingVoucherThirdParty();
            return;
        }
        cashFlowWizardAccountingVoucher.thirdPartySearchTimer = window.setTimeout(
            () => searchCashFlowWizardAccountingVoucherThirdParties(query),
            280);
    };

    const accountingVoucherWizardRequest = () => {
        const processRow = cashFlowWizardAccountingVoucher?.processRow;
        return {
            recordId: processRow?.dataset.cashflowRecordId || processRow?.dataset.recordId || "",
            recordIds: parseDataList(processRow?.dataset.cashflowRecordIds || ""),
            sourceKind: processRow?.dataset.sourceKind || "Movimiento",
            movementExternalKey: processRow?.dataset.movementExternalKey || "",
            movementExternalKeys: parseDataList(processRow?.dataset.movementExternalKeys || ""),
            groupKey: processRow?.dataset.accountingVoucherGroupKey || cashFlowWizardAccountingVoucher?.group?.key || "",
            groupLabel: processRow?.dataset.accountingVoucherGroupLabel || processRow?.dataset.description || cashFlowWizardAccountingVoucher?.group?.label || "",
            ...accountingVoucherThirdPartyFields(cashFlowWizardAccountingVoucher?.thirdParty)
        };
    };

    const updateAccountingVoucherWizardActions = () => {
        const modal = ensureCashFlowWizardModal();
        const request = accountingVoucherWizardRequest();
        const hasRecord = Boolean(request.recordId || request.recordIds.length || request.movementExternalKeys.length);
        const selectedAccount = String(modal.querySelector("[data-cnc-wizard-voucher-account]")?.value || "").trim();
        const savedAccount = String(cashFlowWizardAccountingVoucher?.accountCode || "").trim();
        const hasSelectedAccount = Boolean(selectedAccount);
        const hasSavedAccount = Boolean(savedAccount);
        const accountChanged = selectedAccount !== savedAccount;
        const thirdPartyChanged = accountingVoucherThirdPartySignature(cashFlowWizardAccountingVoucher?.thirdParty)
            !== accountingVoucherThirdPartySignature(cashFlowWizardAccountingVoucher?.persistedThirdParty);
        const hasChanges = accountChanged || thirdPartyChanged;
        const requiresThirdParty = accountingVoucherRequiresThirdParty(cashFlowWizardAccountingVoucher?.processRow);
        const hasThirdParty = !requiresThirdParty || accountingVoucherHasThirdParty(cashFlowWizardAccountingVoucher?.thirdParty);
        const save = modal.querySelector("[data-cnc-wizard-voucher-save]");
        const send = modal.querySelector("[data-cnc-wizard-voucher-send]");
        if (save) {
            save.hidden = !hasRecord || !hasChanges || !hasSelectedAccount;
            save.disabled = !hasRecord || !hasSelectedAccount || !hasThirdParty;
        }
        if (send) {
            send.hidden = !hasRecord || hasChanges || !hasSavedAccount;
            send.disabled = !hasRecord || !hasSavedAccount || !hasThirdParty;
        }
    };

    const renderAccountingVoucherWizardPayload = () => {
        renderCashFlowWizardIssues("[data-cnc-wizard-voucher-issues]", cashFlowWizardAccountingVoucher?.issues || []);
        renderCashFlowWizardPreview(
            "[data-cnc-wizard-voucher-payload]",
            "[data-cnc-wizard-voucher-response]",
            "[data-cnc-wizard-voucher-preview]",
            cashFlowWizardAccountingVoucher?.payloadJson || "",
            cashFlowWizardAccountingVoucher?.responseJson || "");
    };

    const renderCashFlowWizardAccountingVoucher = (row, message = "", tone = "info", options = {}) => {
        const modal = ensureCashFlowWizardModal();
        setCashFlowWizardTitle("Comprobante contable");
        const card = modal.querySelector("[data-cnc-cashflow-wizard-card]");
        const count = modal.querySelector("[data-cnc-cashflow-wizard-count]");
        const bar = modal.querySelector("[data-cnc-cashflow-wizard-bar]");
        const group = options.accumulatedGroup || null;
        const baseRow = row || group?.rows?.[0] || null;
        cashFlowWizardMode = "accounting-voucher";
        setCashFlowWizardProcessActions();
        if (count) {
            count.textContent = group ? "Comprobante acumulado" : `Comprobante ${cashFlowWizardIndex + 1} de ${cashFlowWizardRows.length}`;
        }
        if (bar) {
            bar.style.width = group ? "100%" : `${Math.round((cashFlowWizardIndex + 1) / Math.max(cashFlowWizardRows.length, 1) * 100)}%`;
        }

        const existingState = cashFlowWizardAccountingVoucher?.row === baseRow && cashFlowWizardAccountingVoucher?.group?.key === group?.key
            ? cashFlowWizardAccountingVoucher
            : null;
        const processRow = existingState?.processRow
            || findAccountingVoucherRowForCashFlow(baseRow, group?.key || "")
            || (String(baseRow?.dataset?.sourceKind || "").toLowerCase() === "traslado"
                || String(baseRow?.dataset?.currentType || "").toLowerCase() === "traslado-interno"
                ? baseRow
                : null);
        const isInternalTransfer = String(processRow?.dataset?.sourceKind || "").toLowerCase() === "traslado"
            || String(processRow?.dataset?.currentType || "").toLowerCase() === "traslado-interno";
        const persistedThirdParty = accountingVoucherThirdPartyFromRow(processRow);
        cashFlowWizardAccountingVoucher = existingState || {
            row: baseRow,
            group,
            processRow,
            accountCode: processRow?.dataset.accountCode || "",
            thirdParty: persistedThirdParty,
            persistedThirdParty,
            thirdPartyCandidates: [],
            thirdPartySearchSequence: 0,
            thirdPartySearchTimer: 0,
            issues: [],
            payloadJson: "",
            responseJson: ""
        };

        const accountHtml = accountOptionsHtml(accountingVoucherAccount);
        const request = accountingVoucherWizardRequest();
        const hasRecord = Boolean(request.recordId || request.recordIds.length || request.movementExternalKeys.length);
        const requiresThirdParty = accountingVoucherRequiresThirdParty(processRow);
        const amountLabel = group?.amountLabel || baseRow?.dataset.amountLabel || "";
        const description = group?.detail || group?.label || resolveCashFlowRowLabel(baseRow);
        const breakdown = group?.rows?.length
            ? `
                <details class="cnc-voucher-breakdown">
                    <summary>${numberLabel(group.rows.length)} movimientos</summary>
                    <div class="cnc-voucher-breakdown__grid">
                        ${group.rows.map((line) => `
                            <div class="cnc-voucher-breakdown__line">
                                <strong>${escapeHtml(line.dataset.dateDisplay || "Sin fecha")} - ${escapeHtml(line.dataset.amountLabel || "")}</strong>
                                <small>${escapeHtml(resolveCashFlowRowLabel(line))}</small>
                            </div>
                        `).join("")}
                    </div>
                </details>`
            : "";

        if (card) {
            card.innerHTML = `
                <div class="cnc-wizard-process">
                    <header class="cnc-wizard-process__summary">
                        <div>
                            <span>${group ? `${numberLabel(group.rows.length)} movimientos` : escapeHtml(baseRow?.dataset.dateDisplay || "Sin fecha")}</span>
                            <p>${escapeHtml(description)}</p>
                        </div>
                        <strong>${escapeHtml(amountLabel || money(Number(baseRow?.dataset.entryValue || baseRow?.dataset.exitValue || 0)))}</strong>
                    </header>
                    ${breakdown}
                    <label class="cnc-modal__field">
                        <span>${isInternalTransfer ? "Cuenta bancaria contraparte" : "Cuenta contable"}</span>
                        <select class="form-select" data-cnc-wizard-voucher-account>${accountHtml}</select>
                    </label>
                    ${isInternalTransfer ? "<small>Selecciona la cuenta bancaria contraparte. El sistema acredita la cuenta origen, debita la cuenta seleccionada y usa Bancolombia como tercero.</small>" : ""}
                    ${requiresThirdParty ? `
                        <div class="cnc-party-picker">
                            <label class="cnc-modal__field">
                                <span>Tercero Siigo</span>
                                <input class="form-control" type="search" autocomplete="off" data-cnc-wizard-voucher-third-party-query placeholder="Escribe nombre o NIT" />
                            </label>
                            <div class="cnc-party-picker__results" data-cnc-wizard-voucher-third-parties hidden></div>
                        </div>
                        <div class="cnc-party-picker__selected" data-cnc-wizard-voucher-third-party hidden></div>
                        <small>${group ? "El tercero seleccionado se aplicara a todas las lineas del acumulado." : "El tercero real es obligatorio y se aplicara a todas las lineas del comprobante."}</small>
                    ` : ""}
                    <ul class="cnc-issue-list" data-cnc-wizard-voucher-issues hidden></ul>
                    <div class="cnc-wizard-process__actions">
                        <button type="button" class="btn btn-outline-secondary" data-cnc-wizard-voucher-back>Volver</button>
                        <button type="button" class="btn btn-primary" data-cnc-wizard-voucher-save hidden>Guardar cuenta</button>
                        <button type="button" class="btn btn-primary" data-cnc-wizard-voucher-send hidden>Enviar a Siigo</button>
                    </div>
                </div>`;
        }

        const accountSelect = modal.querySelector("[data-cnc-wizard-voucher-account]");
        if (accountSelect) {
            accountSelect.value = cashFlowWizardAccountingVoucher.accountCode || "";
            accountSelect.addEventListener("change", updateAccountingVoucherWizardActions);
        }
        const thirdPartyQuery = modal.querySelector("[data-cnc-wizard-voucher-third-party-query]");
        if (thirdPartyQuery && cashFlowWizardAccountingVoucher.thirdParty) {
            thirdPartyQuery.value = supplierPaymentLabel(cashFlowWizardAccountingVoucher.thirdParty);
        }
        thirdPartyQuery?.addEventListener("input", () => {
            const value = String(thirdPartyQuery.value || "").trim();
            if (cashFlowWizardAccountingVoucher.thirdParty
                && value !== supplierPaymentLabel(cashFlowWizardAccountingVoucher.thirdParty)) {
                cashFlowWizardAccountingVoucher.thirdParty = null;
                cashFlowWizardAccountingVoucher.thirdPartyCandidates = [];
                renderCashFlowWizardAccountingVoucherThirdParty();
            }
            scheduleCashFlowWizardAccountingVoucherThirdPartySearch(value);
            updateAccountingVoucherWizardActions();
        });
        modal.querySelector("[data-cnc-wizard-voucher-back]")?.addEventListener("click", () => {
            cashFlowWizardMode = group ? "accumulated" : "rows";
            cashFlowWizardAccountingVoucher = null;
            renderCashFlowWizard();
        });
        modal.querySelector("[data-cnc-wizard-voucher-save]")?.addEventListener("click", saveCashFlowWizardAccountingVoucherAccount);
        modal.querySelector("[data-cnc-wizard-voucher-send]")?.addEventListener("click", sendCashFlowWizardAccountingVoucherToSiigo);
        renderCashFlowWizardAccountingVoucherThirdParty();
        renderAccountingVoucherWizardPayload();
        updateAccountingVoucherWizardActions();
        setCashFlowWizardMessage(
            message || (hasRecord ? "" : "No hay un comprobante relacionado con este movimiento."),
            hasRecord ? tone : "error");
    };

    const saveCashFlowWizardAccountingVoucherAccount = async () => {
        if (!cashFlowWizardAccountingVoucher?.processRow || !cashFlowAccountUrl) {
            setCashFlowWizardMessage("No se encontro el comprobante o la ruta para guardar cuenta.", "error");
            return;
        }

        const modal = ensureCashFlowWizardModal();
        const accountCode = modal.querySelector("[data-cnc-wizard-voucher-account]")?.value || "";
        if (!accountCode) {
            setCashFlowWizardMessage("Selecciona una cuenta contable antes de guardar.", "info");
            return;
        }
        if (accountingVoucherRequiresThirdParty(cashFlowWizardAccountingVoucher.processRow)
            && !accountingVoucherHasThirdParty(cashFlowWizardAccountingVoucher.thirdParty)) {
            setCashFlowWizardMessage("Selecciona el tercero real de Siigo antes de guardar.", "info");
            return;
        }

        const button = modal.querySelector("[data-cnc-wizard-voucher-save]");
        if (button) {
            button.disabled = true;
        }
        setCashFlowWizardMessage("Guardando cuenta contable del comprobante...", "info");
        const request = accountingVoucherWizardRequest();
        try {
            const response = await fetch(cashFlowAccountUrl, {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify({
                    recordId: request.recordId,
                    recordIds: request.recordIds,
                    sourceKind: request.sourceKind,
                    movementExternalKey: request.movementExternalKey,
                    movementExternalKeys: request.movementExternalKeys,
                    accountCode,
                    ...accountingVoucherThirdPartyFields(cashFlowWizardAccountingVoucher.thirdParty)
                })
            });
            const payload = await response.json().catch(() => ({}));
            if (!response.ok) {
                throw new Error(payload.detail || payload.message || "No fue posible guardar la cuenta contable.");
            }

            cashFlowWizardAccountingVoucher.accountCode = accountCode;
            setAccountingVoucherThirdPartyOnRow(
                cashFlowWizardAccountingVoucher.processRow,
                cashFlowWizardAccountingVoucher.thirdParty);
            cashFlowWizardAccountingVoucher.persistedThirdParty = cashFlowWizardAccountingVoucher.thirdParty;
            cashFlowWizardAccountingVoucher.processRow.dataset.accountCode = accountCode;
            renderCashFlowWizardAccountingVoucher(
                cashFlowWizardAccountingVoucher.row,
                payload.message || "Cuenta contable guardada.",
                "success",
                { accumulatedGroup: cashFlowWizardAccountingVoucher.group });
        } catch (error) {
            setCashFlowWizardMessage(error instanceof Error ? error.message : "Ocurrio un error inesperado.", "error");
            if (button) {
                button.disabled = false;
            }
        }
    };

    const sendCashFlowWizardAccountingVoucherToSiigo = async () => {
        if (!cashFlowWizardAccountingVoucher?.processRow || !accountingVoucherSendUrl) {
            setCashFlowWizardMessage("No se encontro el comprobante o la ruta de envio.", "error");
            return;
        }
        const modal = ensureCashFlowWizardModal();
        const savedAccount = String(cashFlowWizardAccountingVoucher.accountCode || "").trim();
        const selectedAccount = String(modal.querySelector("[data-cnc-wizard-voucher-account]")?.value || "").trim();
        const thirdPartyChanged = accountingVoucherThirdPartySignature(cashFlowWizardAccountingVoucher.thirdParty)
            !== accountingVoucherThirdPartySignature(cashFlowWizardAccountingVoucher.persistedThirdParty);
        if (!savedAccount || selectedAccount !== savedAccount || thirdPartyChanged) {
            setCashFlowWizardMessage("Guarda la cuenta contable y el tercero antes de enviar.", "info");
            updateAccountingVoucherWizardActions();
            return;
        }
        if (accountingVoucherRequiresThirdParty(cashFlowWizardAccountingVoucher.processRow)
            && !accountingVoucherHasThirdParty(cashFlowWizardAccountingVoucher.thirdParty)) {
            setCashFlowWizardMessage("Selecciona el tercero real de Siigo antes de enviar el comprobante.", "info");
            updateAccountingVoucherWizardActions();
            return;
        }

        const button = modal.querySelector("[data-cnc-wizard-voucher-send]");
        if (button) {
            button.disabled = true;
        }
        cashFlowWizardAccountingVoucher.issues = [];
        cashFlowWizardAccountingVoucher.payloadJson = "";
        cashFlowWizardAccountingVoucher.responseJson = "";
        renderAccountingVoucherWizardPayload();
        setCashFlowWizardMessage("Enviando comprobante contable a Siigo...", "info");
        try {
            const response = await fetch(accountingVoucherSendUrl, {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify(accountingVoucherWizardRequest())
            });
            const payload = await response.json().catch(() => ({}));
            cashFlowWizardAccountingVoucher.issues = payload.issues || [];
            cashFlowWizardAccountingVoucher.payloadJson = payload.payloadJson || "";
            cashFlowWizardAccountingVoucher.responseJson = payload.responseJson || "";
            renderAccountingVoucherWizardPayload();
            renderAccountingVoucherPayload(cashFlowWizardAccountingVoucher.processRow, payload);
            renderIssueList(cashFlowWizardAccountingVoucher.processRow, "[data-cnc-accounting-voucher-issues]", payload.issues || []);
            if (!response.ok) {
                throw new Error(payload.detail || payload.message || "No fue posible enviar el comprobante.");
            }

            const success = payload.isSuccess !== false && !(payload.issues || []).length;
            if (!success) {
                setCashFlowWizardMessage(payload.message || "Siigo rechazo el comprobante. Revisa el detalle.", "error");
                updateAccountingVoucherWizardActions();
                return;
            }

            const group = cashFlowWizardAccountingVoucher.group;
            if (group?.rows?.length) {
                group.rows.forEach((groupRow) => markCashFlowRowConciliated(groupRow, payload.row));
                cashFlowWizardAccumulatedGroups = cashFlowWizardAccumulatedGroups.filter((item) => item.key !== group.key);
                cashFlowWizardAccountingVoucher = null;
                cashFlowWizardMode = cashFlowWizardRows.length > 0 ? "rows" : "accumulated";
                setStatus(payload.message || "Comprobante acumulado enviado a Siigo.", "success");
                renderCashFlowWizard(payload.message || "Comprobante acumulado enviado a Siigo.", "success");
                refreshBulkSections();
                return;
            }

            completeCashFlowWizardRow(cashFlowWizardAccountingVoucher.row, payload.row, payload.message || "Comprobante contable enviado a Siigo.");
        } catch (error) {
            setCashFlowWizardMessage(error instanceof Error ? error.message : "Ocurrio un error inesperado.", "error");
            if (button) {
                button.disabled = false;
            }
        }
    };

    const getCashFlowWizardSupplierRetentions = () => {
        const modal = ensureCashFlowWizardModal();
        return {
            reteFuenteValue: positiveNumberFromInput(modal.querySelector("[data-cnc-wizard-supplier-retefuente]")),
            reteFuenteRate: positiveNumberFromInput(modal.querySelector("[data-cnc-wizard-supplier-retefuente-rate]")),
            reteIcaValue: positiveNumberFromInput(modal.querySelector("[data-cnc-wizard-supplier-reteica]")),
            reteIcaRate: positiveNumberFromInput(modal.querySelector("[data-cnc-wizard-supplier-reteica-rate]"))
        };
    };

    const updateCashFlowWizardSupplierSummary = () => {
        if (!cashFlowWizardSupplierPayment) {
            return;
        }

        const modal = ensureCashFlowWizardModal();
        const summary = modal.querySelector("[data-cnc-wizard-supplier-summary]");
        const send = modal.querySelector("[data-cnc-wizard-supplier-send]");
        if (!summary) {
            return;
        }

        const row = cashFlowWizardSupplierPayment.row;
        const bankPayment = Number(row?.dataset.exitValue || 0);
        const retentions = getCashFlowWizardSupplierRetentions();
        cashFlowWizardSupplierPayment.retentions = retentions;
        const appliedValue = bankPayment + retentions.reteFuenteValue + retentions.reteIcaValue;
        const balance = cashFlowWizardSupplierPayment.purchase ? Number(cashFlowWizardSupplierPayment.purchase.balance || 0) : 0;
        const difference = cashFlowWizardSupplierPayment.purchase ? balance - appliedValue : 0;
        const hasPurchase = Boolean(cashFlowWizardSupplierPayment.purchase);
        const items = hasPurchase
            ? [
                ["Valor aplicado", money(appliedValue)],
                ["Saldo factura", money(balance)],
                ["Diferencia", money(difference)]
            ]
            : [];

        summary.innerHTML = items.map(([label, value]) => `
            <div>
                <span>${escapeHtml(label)}</span>
                <strong>${escapeHtml(value)}</strong>
            </div>
        `).join("");
        summary.hidden = !hasPurchase;

        if (send) {
            send.hidden = !hasPurchase;
            send.disabled = !hasPurchase || !supplierPaymentSendUrl || bankPayment <= 0;
        }
    };

    const renderCashFlowWizardSupplierIssues = (issues) => {
        const modal = ensureCashFlowWizardModal();
        const list = modal.querySelector("[data-cnc-wizard-supplier-issues]");
        if (!list) {
            return;
        }

        const values = Array.isArray(issues)
            ? issues.filter((issue) => String(issue || "").trim())
            : [];
        list.innerHTML = "";
        list.hidden = values.length === 0;
        values.forEach((issue) => {
            const item = document.createElement("li");
            item.textContent = issue;
            list.appendChild(item);
        });
    };

    const renderCashFlowWizardSupplierPreview = (payloadJson = "", responseJson = "") => {
        const modal = ensureCashFlowWizardModal();
        const preview = modal.querySelector("[data-cnc-wizard-supplier-preview]");
        const payload = modal.querySelector("[data-cnc-wizard-supplier-payload]");
        const response = modal.querySelector("[data-cnc-wizard-supplier-response]");
        if (payload) {
            payload.textContent = payloadJson || "";
        }
        if (response) {
            response.textContent = responseJson || "";
        }
        if (preview) {
            preview.hidden = !payloadJson && !responseJson;
        }
    };

    const renderCashFlowWizardSupplierCandidates = () => {
        const modal = ensureCashFlowWizardModal();
        const box = modal.querySelector("[data-cnc-wizard-supplier-candidates]");
        if (!box || !cashFlowWizardSupplierPayment) {
            return;
        }

        const selected = cashFlowWizardSupplierPayment.supplier;
        const candidates = (cashFlowWizardSupplierPayment.candidates || [])
            .filter((candidate) => !selected?.id || candidate?.id !== selected.id);
        box.innerHTML = "";
        box.hidden = !selected && candidates.length === 0;
        if (selected) {
            const selectedBox = document.createElement("div");
            selectedBox.className = "cnc-supplier-payment-selected";
            selectedBox.textContent = supplierPaymentLabel(selected);
            box.appendChild(selectedBox);
        }

        if (candidates.length === 0) {
            return;
        }
        box.hidden = false;

        candidates.forEach((supplier) => {
            const button = document.createElement("button");
            const title = document.createElement("strong");
            const detail = document.createElement("small");
            button.type = "button";
            button.className = "cnc-supplier-payment-supplier";
            title.textContent = supplierPaymentLabel(supplier);
            detail.textContent = supplier.active === false ? "Inactivo" : "";
            button.append(title);
            if (detail.textContent) {
                button.append(detail);
            }
            button.addEventListener("click", () => {
                cashFlowWizardSupplierPayment.supplier = supplier;
                cashFlowWizardSupplierPayment.purchase = null;
                cashFlowWizardSupplierPayment.query = supplier.identification || supplier.displayName || supplier.name || cashFlowWizardSupplierPayment.query || "";
                loadCashFlowWizardSupplierPurchases({ supplier });
            });
            box.appendChild(button);
        });
    };

    const renderCashFlowWizardSupplierPurchases = () => {
        const modal = ensureCashFlowWizardModal();
        const body = modal.querySelector("[data-cnc-wizard-supplier-purchases]");
        if (!body || !cashFlowWizardSupplierPayment) {
            return;
        }

        const purchases = cashFlowWizardSupplierPayment.purchases || [];
        body.innerHTML = "";
        if (purchases.length === 0) {
            const row = document.createElement("tr");
            const cell = document.createElement("td");
            cell.colSpan = 3;
            cell.innerHTML = "<small>Sin facturas abiertas.</small>";
            row.appendChild(cell);
            body.appendChild(row);
            updateCashFlowWizardSupplierSummary();
            return;
        }

        purchases.forEach((purchase) => {
            const row = document.createElement("tr");
            row.className = "cnc-supplier-payment-invoice";
            row.tabIndex = 0;
            if (cashFlowWizardSupplierPayment.purchase?.id === purchase.id) {
                row.classList.add("is-selected");
            }
            row.innerHTML = `
                <td>
                    <strong>${escapeHtml(purchase.name || "Sin numero")}</strong>
                    <small>${escapeHtml(purchase.providerInvoiceFullNumber || purchase.providerInvoiceNumber || "")}</small>
                </td>
                <td>${escapeHtml(purchase.dateDisplay || purchase.dateValue || "Sin fecha")}</td>
                <td class="text-end"><strong>${money(purchase.balance)}</strong></td>`;
            const selectPurchase = () => {
                cashFlowWizardSupplierPayment.purchase = purchase;
                const retentions = getCashFlowWizardSupplierRetentions();
                if (retentions.reteFuenteValue <= 0 && retentions.reteIcaValue <= 0) {
                    const inferred = supplierPaymentRetentionsForRow(cashFlowWizardSupplierPayment.row, purchase);
                    const reteFuente = modal.querySelector("[data-cnc-wizard-supplier-retefuente]");
                    const reteIca = modal.querySelector("[data-cnc-wizard-supplier-reteica]");
                    if (reteFuente && inferred.reteFuenteValue > 0) {
                        reteFuente.value = String(inferred.reteFuenteValue);
                    }
                    if (reteIca && inferred.reteIcaValue > 0) {
                        reteIca.value = String(inferred.reteIcaValue);
                    }
                }
                renderCashFlowWizardSupplierPurchases();
                updateCashFlowWizardSupplierSummary();
            };
            row.addEventListener("click", selectPurchase);
            row.addEventListener("keydown", (event) => {
                if (event.key === "Enter" || event.key === " ") {
                    event.preventDefault();
                    selectPurchase();
                }
            });
            body.appendChild(row);
        });
        updateCashFlowWizardSupplierSummary();
    };

    const renderCashFlowWizardSupplierPaymentLegacy = (row, message = "", tone = "info") => {
        const modal = ensureCashFlowWizardModal();
        setCashFlowWizardTitle("Pago a proveedor");
        const card = modal.querySelector("[data-cnc-cashflow-wizard-card]");
        const count = modal.querySelector("[data-cnc-cashflow-wizard-count]");
        const bar = modal.querySelector("[data-cnc-cashflow-wizard-bar]");
        cashFlowWizardMode = "supplier-payment";
        setCashFlowWizardProcessActions();
        if (count) {
            count.textContent = `Salida FC ${cashFlowWizardIndex + 1} de ${cashFlowWizardRows.length}`;
        }
        if (bar) {
            bar.style.width = `${Math.round((cashFlowWizardIndex + 1) / Math.max(cashFlowWizardRows.length, 1) * 100)}%`;
        }

        const existingState = cashFlowWizardSupplierPayment?.row === row
            ? cashFlowWizardSupplierPayment
            : null;
        cashFlowWizardSupplierPayment = existingState || {
            row,
            query: row.dataset.supplierQuery || row.dataset.dataverseSupplier || row.dataset.description || "",
            supplier: null,
            candidates: [],
            purchases: [],
            purchase: null,
            retentions: supplierPaymentRetentionsForRow(row),
            issues: [],
            payloadJson: "",
            responseJson: ""
        };
        const currentRetentions = cashFlowWizardSupplierPayment.retentions || {};
        const hasRetentions = Number(currentRetentions.reteFuenteValue || 0) > 0
            || Number(currentRetentions.reteIcaValue || 0) > 0;

        if (card) {
            card.innerHTML = `
                <div class="cnc-wizard-process">
                    <header class="cnc-wizard-process__summary">
                        <div>
                            <span>${escapeHtml(row.dataset.dateDisplay || "Sin fecha")}</span>
                            <p>${escapeHtml(resolveCashFlowRowLabel(row))}</p>
                        </div>
                        <strong>${escapeHtml(row.dataset.amountLabel || money(Number(row.dataset.exitValue || 0)))}</strong>
                    </header>
                    <div class="cnc-supplier-payment-search">
                        <label class="cnc-modal__field">
                            <span>Buscar proveedor o factura</span>
                            <input class="form-control" type="search" data-cnc-wizard-supplier-query value="${escapeHtml(cashFlowWizardSupplierPayment.query || "")}" placeholder="Proveedor, NIT o texto de la factura" />
                        </label>
                        <button type="button" class="btn btn-outline-primary" data-cnc-wizard-supplier-search>Buscar</button>
                    </div>
                    <div class="cnc-supplier-payment-suppliers" data-cnc-wizard-supplier-candidates></div>
                    <div class="cnc-supplier-payment-table-wrap">
                        <table class="table align-middle cnc-table cnc-supplier-payment-table">
                            <thead>
                                <tr>
                                    <th>Factura</th>
                                    <th>Fecha</th>
                                    <th class="text-end">Saldo</th>
                                </tr>
                            </thead>
                            <tbody data-cnc-wizard-supplier-purchases></tbody>
                        </table>
                    </div>
                    <details class="cnc-wizard-optional"${hasRetentions ? " open" : ""}>
                        <summary>Retenciones</summary>
                        <div class="cnc-wizard-retentions">
                            <label class="cnc-modal__field">
                                <span>Retefuente</span>
                                <input class="form-control" type="number" min="0" step="1" data-cnc-wizard-supplier-retefuente value="${escapeHtml(String(cashFlowWizardSupplierPayment.retentions?.reteFuenteValue || 0))}" />
                            </label>
                            <label class="cnc-modal__field">
                                <span>Tarifa %</span>
                                <input class="form-control" type="number" min="0" step="0.01" data-cnc-wizard-supplier-retefuente-rate value="${escapeHtml(String(cashFlowWizardSupplierPayment.retentions?.reteFuenteRate || ""))}" />
                            </label>
                            <label class="cnc-modal__field">
                                <span>ReteICA</span>
                                <input class="form-control" type="number" min="0" step="1" data-cnc-wizard-supplier-reteica value="${escapeHtml(String(cashFlowWizardSupplierPayment.retentions?.reteIcaValue || 0))}" />
                            </label>
                            <label class="cnc-modal__field">
                                <span>Tarifa %</span>
                                <input class="form-control" type="number" min="0" step="0.01" data-cnc-wizard-supplier-reteica-rate value="${escapeHtml(String(cashFlowWizardSupplierPayment.retentions?.reteIcaRate || ""))}" />
                            </label>
                        </div>
                    </details>
                    <div class="cnc-supplier-payment-summary" data-cnc-wizard-supplier-summary hidden></div>
                    <ul class="cnc-issue-list" data-cnc-wizard-supplier-issues hidden></ul>
                    <div class="cnc-wizard-process__actions">
                        <button type="button" class="btn btn-outline-secondary" data-cnc-wizard-supplier-back>Volver</button>
                        <button type="button" class="btn btn-primary" data-cnc-wizard-supplier-send hidden disabled>Enviar a Siigo</button>
                    </div>
                </div>`;
        }

        modal.querySelector("[data-cnc-wizard-supplier-search]")?.addEventListener("click", () => loadCashFlowWizardSupplierPurchases());
        modal.querySelector("[data-cnc-wizard-supplier-query]")?.addEventListener("keydown", (event) => {
            if (event.key === "Enter") {
                event.preventDefault();
                loadCashFlowWizardSupplierPurchases();
            }
        });
        modal.querySelector("[data-cnc-wizard-supplier-back]")?.addEventListener("click", () => {
            cashFlowWizardMode = "rows";
            renderCashFlowWizard();
        });
        modal.querySelector("[data-cnc-wizard-supplier-send]")?.addEventListener("click", sendCashFlowWizardSupplierPaymentToSiigoLegacy);
        [
            "[data-cnc-wizard-supplier-retefuente]",
            "[data-cnc-wizard-supplier-retefuente-rate]",
            "[data-cnc-wizard-supplier-reteica]",
            "[data-cnc-wizard-supplier-reteica-rate]"
        ].forEach((selector) => {
            const input = modal.querySelector(selector);
            input?.addEventListener("input", updateCashFlowWizardSupplierSummary);
            input?.addEventListener("change", updateCashFlowWizardSupplierSummary);
        });

        renderCashFlowWizardSupplierCandidates();
        renderCashFlowWizardSupplierPurchases();
        renderCashFlowWizardSupplierIssues(cashFlowWizardSupplierPayment.issues || []);
        renderCashFlowWizardSupplierPreview(cashFlowWizardSupplierPayment.payloadJson || "", cashFlowWizardSupplierPayment.responseJson || "");
        setCashFlowWizardSupplierMessage(message, tone);

        if (!existingState && cashFlowWizardSupplierPayment.query) {
            loadCashFlowWizardSupplierPurchases({ silent: true });
        }
    };

    const loadCashFlowWizardSupplierPurchases = async (options = {}) => {
        if (!cashFlowWizardSupplierPayment?.row || !supplierPaymentPurchasesUrl) {
            setCashFlowWizardSupplierMessage("No se encontro la ruta para consultar facturas abiertas.", "error");
            return;
        }

        const modal = ensureCashFlowWizardModal();
        const queryInput = modal.querySelector("[data-cnc-wizard-supplier-query]");
        const searchButton = modal.querySelector("[data-cnc-wizard-supplier-search]");
        const body = modal.querySelector("[data-cnc-wizard-supplier-purchases]");
        const supplier = options.supplier || cashFlowWizardSupplierPayment.supplier;
        cashFlowWizardSupplierPayment.query = String(queryInput?.value || cashFlowWizardSupplierPayment.query || "").trim();
        cashFlowWizardSupplierPayment.purchase = null;
        cashFlowWizardSupplierPayment.payloadJson = "";
        cashFlowWizardSupplierPayment.responseJson = "";
        cashFlowWizardSupplierPayment.issues = [];
        renderCashFlowWizardSupplierIssues([]);
        renderCashFlowWizardSupplierPreview("", "");
        if (body) {
            body.innerHTML = '<tr><td colspan="3"><small>Consultando facturas...</small></td></tr>';
        }
        if (searchButton) {
            searchButton.disabled = true;
        }
        updateCashFlowWizardSupplierSummary();

        try {
            const request = supplierPaymentRequestForRow(cashFlowWizardSupplierPayment.row, supplier);
            request.supplierId = supplier?.id || request.supplierId;
            request.supplierQuery = cashFlowWizardSupplierPayment.query || supplier?.identification || supplier?.displayName || request.supplierQuery || "";
            const response = await fetch(supplierPaymentPurchasesUrl, {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify(request)
            });
            const payload = await response.json().catch(() => ({}));
            if (!response.ok) {
                throw new Error(payload.detail || payload.message || "No fue posible consultar facturas abiertas.");
            }

            cashFlowWizardSupplierPayment.supplier = payload.supplier || supplier || null;
            cashFlowWizardSupplierPayment.candidates = payload.supplierCandidates || [];
            cashFlowWizardSupplierPayment.purchases = payload.purchases || [];
            cashFlowWizardSupplierPayment.purchase = selectSupplierPaymentPurchaseForRow(
                cashFlowWizardSupplierPayment.row,
                cashFlowWizardSupplierPayment.purchases);
            if (cashFlowWizardSupplierPayment.supplier && queryInput) {
                queryInput.value = supplierPaymentLabel(cashFlowWizardSupplierPayment.supplier);
            }
            renderCashFlowWizardSupplierCandidates();
            renderCashFlowWizardSupplierPurchases();
            setCashFlowWizardSupplierMessage(
                options.silent
                    ? ""
                    : `${cashFlowWizardSupplierPayment.purchases.length} factura${cashFlowWizardSupplierPayment.purchases.length === 1 ? "" : "s"} abierta${cashFlowWizardSupplierPayment.purchases.length === 1 ? "" : "s"}.`,
                "info");
        } catch (error) {
            cashFlowWizardSupplierPayment.purchases = [];
            cashFlowWizardSupplierPayment.purchase = null;
            renderCashFlowWizardSupplierCandidates();
            renderCashFlowWizardSupplierPurchases();
            setCashFlowWizardSupplierMessage(error instanceof Error ? error.message : "Ocurrio un error inesperado.", "error");
        } finally {
            if (searchButton) {
                searchButton.disabled = false;
            }
        }
    };

    const sendCashFlowWizardSupplierPaymentToSiigoLegacy = async () => {
        if (!cashFlowWizardSupplierPayment?.row || !cashFlowWizardSupplierPayment.purchase || !supplierPaymentSendUrl) {
            setCashFlowWizardSupplierMessage("Selecciona una factura abierta antes de enviar el pago.", "info");
            return;
        }

        const modal = ensureCashFlowWizardModal();
        const send = modal.querySelector("[data-cnc-wizard-supplier-send]");
        if (send) {
            send.disabled = true;
        }
        setCashFlowWizardSupplierMessage("Enviando pago proveedor a Siigo...", "info");
        renderCashFlowWizardSupplierIssues([]);
        renderCashFlowWizardSupplierPreview("", "");

        try {
            const supplier = cashFlowWizardSupplierPayment.supplier;
            const purchase = cashFlowWizardSupplierPayment.purchase;
            const retentions = getCashFlowWizardSupplierRetentions();
            const request = supplierPaymentRequestForRow(cashFlowWizardSupplierPayment.row, supplier);
            request.supplierId = supplier?.id || request.supplierId;
            request.supplierIdentification = supplier?.identification || "";
            request.supplierName = supplierPaymentLabel(supplier);
            request.purchaseId = purchase.id || "";
            request.purchaseName = purchase.name || "";
            request.reteFuenteValue = retentions.reteFuenteValue;
            request.reteFuenteRate = retentions.reteFuenteRate;
            request.reteIcaValue = retentions.reteIcaValue;
            request.reteIcaRate = retentions.reteIcaRate;

            const response = await fetch(supplierPaymentSendUrl, {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify(request)
            });
            const payload = await response.json().catch(() => ({}));
            cashFlowWizardSupplierPayment.issues = payload.issues || [];
            cashFlowWizardSupplierPayment.payloadJson = payload.payloadJson || "";
            cashFlowWizardSupplierPayment.responseJson = payload.responseJson || "";
            renderCashFlowWizardSupplierIssues(cashFlowWizardSupplierPayment.issues);
            renderCashFlowWizardSupplierPreview(cashFlowWizardSupplierPayment.payloadJson, cashFlowWizardSupplierPayment.responseJson);

            if (!response.ok) {
                throw new Error(payload.detail || payload.message || "No fue posible enviar el pago a Siigo.");
            }
            if (!payload.isSuccess) {
                setCashFlowWizardSupplierMessage(payload.message || "El pago quedo bloqueado por validacion.", "error");
                updateCashFlowWizardSupplierSummary();
                return;
            }

            markCashFlowRowConciliated(cashFlowWizardSupplierPayment.row, payload.row);
            cashFlowWizardRows.splice(cashFlowWizardIndex, 1);
            if (cashFlowWizardIndex >= cashFlowWizardRows.length) {
                cashFlowWizardIndex = Math.max(0, cashFlowWizardRows.length - 1);
            }
            cashFlowWizardSupplierPayment = null;
            cashFlowWizardMode = cashFlowWizardRows.length > 0 ? "rows" : "accumulated";
            setStatus(payload.message || "Pago proveedor enviado a Siigo.", "success");
            renderCashFlowWizard(payload.message || "Pago proveedor enviado a Siigo.", "success");
            refreshBulkSections();
        } catch (error) {
            setCashFlowWizardSupplierMessage(error instanceof Error ? error.message : "Ocurrio un error inesperado.", "error");
            if (send) {
                send.disabled = false;
            }
        }
    };

    const supplierWizardAllocationKey = (purchase) => String(purchase?.id || purchase?.name || "");

    const supplierExpenseCategoryOptionsHtml = () => {
        const template = document.getElementById("cncSupplierExpenseCategoryOptions");
        return template?.innerHTML || '<option value="">Selecciona categoria</option>';
    };

    const getCashFlowWizardSupplierAllocationDraft = (purchase) => {
        if (!cashFlowWizardSupplierPayment) {
            return null;
        }

        const key = supplierWizardAllocationKey(purchase);
        const current = cashFlowWizardSupplierPayment.allocations?.[key];
        if (current && typeof current === "object") {
            return current;
        }

        const paymentValue = Math.max(0, Number(cashFlowWizardSupplierPayment.row?.dataset.exitValue || 0));
        let cloudValue = Math.max(0, Number(purchase?.dataverseCloudValue || 0));
        let copiersValue = Math.max(0, Number(purchase?.dataverseCopiersValue || 0));
        const storedAllocation = roundClientPaymentMoney(cloudValue + copiersValue);
        if (paymentValue > 0 && storedAllocation > 0 && Math.abs(storedAllocation - paymentValue) > 1) {
            cloudValue = roundClientPaymentMoney(paymentValue * cloudValue / storedAllocation);
            copiersValue = roundClientPaymentMoney(paymentValue - cloudValue);
        }

        const draft = {
            paymentValue,
            cloudValue,
            copiersValue,
            categoryValue: String(purchase?.dataverseCategoryValue || ""),
            reteFuenteTaxId: 0,
            reteIcaTaxId: 0,
            sending: false,
            sendFailed: false,
            siigoCreated: false
        };
        cashFlowWizardSupplierPayment.allocations[key] = draft;
        return draft;
    };

    const buildCashFlowWizardSupplierAllocation = (purchase) => {
        const draft = getCashFlowWizardSupplierAllocationDraft(purchase);
        return {
            documentId: purchase?.id || "",
            documentName: purchase?.name || "",
            dataverseRecordId: purchase?.dataverseRecordId || "",
            dataverseInvoiceNumber: purchase?.dataverseInvoiceNumber || "",
            cufeCude: purchase?.dataverseCufeCude || "",
            appliedValue: Math.max(0, Number(draft?.paymentValue || 0)),
            cloudValue: Math.max(0, Number(draft?.cloudValue || 0)),
            copiersValue: Math.max(0, Number(draft?.copiersValue || 0)),
            categoryValue: String(draft?.categoryValue || ""),
            reteFuenteTaxId: Number(draft?.reteFuenteTaxId || 0),
            reteIcaTaxId: Number(draft?.reteIcaTaxId || 0)
        };
    };

    const findCashFlowWizardSupplierRetentionOption = (kind, taxId) => {
        const options = kind === "reteIca"
            ? cashFlowWizardSupplierPayment?.reteIcaOptions
            : cashFlowWizardSupplierPayment?.reteFuenteOptions;
        return (options || []).find((option) => Number(option.taxId || 0) === Number(taxId || 0)) || null;
    };

    const calculateCashFlowWizardSupplierAllocation = (purchase) => {
        const draft = getCashFlowWizardSupplierAllocationDraft(purchase);
        const taxBase = Math.max(0, Number(purchase?.dataverseBaseAmount || 0));
        const reteFuente = findCashFlowWizardSupplierRetentionOption("reteFuente", draft?.reteFuenteTaxId);
        const reteIca = findCashFlowWizardSupplierRetentionOption("reteIca", draft?.reteIcaTaxId);
        const reteFuenteValue = roundClientPaymentMoney(taxBase * Number(reteFuente?.rate || 0) / 100);
        const reteIcaValue = roundClientPaymentMoney(taxBase * Number(reteIca?.rate || 0) / 1000);
        const retentionValue = roundClientPaymentMoney(reteFuenteValue + reteIcaValue);
        const grossValue = roundClientPaymentMoney(Number(draft?.paymentValue || 0) + retentionValue);
        const remainingBalance = roundClientPaymentMoney(Number(purchase?.balance || 0) - grossValue);
        return {
            draft,
            reteFuente,
            reteIca,
            reteFuenteValue,
            reteIcaValue,
            retentionValue,
            grossValue,
            remainingBalance
        };
    };

    const validateCashFlowWizardSupplierAllocation = (purchase, draft) => {
        const verified = String(purchase?.dataverseMatchTone || "").toLowerCase() === "success"
            && Boolean(purchase?.dataverseRecordId)
            && Boolean(purchase?.dataverseCufeCude || purchase?.dataverseInvoiceNumber);
        if (!verified) {
            return { valid: false, message: purchase?.dataverseMatchLabel || "Sin cruce DIAN verificable." };
        }
        if (draft?.siigoCreated) {
            return { valid: false, message: "El pago ya fue creado en Siigo; no lo reintentes." };
        }
        if (draft?.sending) {
            return { valid: false, message: "Procesando..." };
        }

        const bankPayment = roundClientPaymentMoney(Number(cashFlowWizardSupplierPayment?.row?.dataset.exitValue || 0));
        const calculation = calculateCashFlowWizardSupplierAllocation(purchase);
        const paymentValue = roundClientPaymentMoney(Number(draft?.paymentValue || 0));
        const purchaseBalance = Math.max(0, Number(purchase?.balance || 0));
        if (paymentValue <= 0) {
            return { valid: false, message: "Indica el valor pagado." };
        }
        if (Math.abs(paymentValue - bankPayment) > 0.01) {
            return { valid: false, message: `El valor pagado debe coincidir con el movimiento (${money(bankPayment)}).` };
        }
        if (calculation.remainingBalance < -1) {
            return { valid: false, message: `Pago y retenciones superan el saldo de ${money(purchaseBalance)}.` };
        }

        const distributed = Math.max(0, Number(draft?.cloudValue || 0)) + Math.max(0, Number(draft?.copiersValue || 0));
        if (Math.abs(distributed - paymentValue) > 1) {
            return { valid: false, message: `Distribuye el valor pagado de ${money(paymentValue)} entre Cloud y Copiers.` };
        }
        if (!String(draft?.categoryValue || "")) {
            return { valid: false, message: "Selecciona la categoria." };
        }

        return { valid: true, message: "" };
    };

    const getSupplierPaymentEditorPurchase = (modal) => {
        const key = String(modal?.dataset.purchaseKey || "");
        return (cashFlowWizardSupplierPayment?.purchases || [])
            .find((purchase) => supplierWizardAllocationKey(purchase) === key) || null;
    };

    const closeSupplierPaymentEditor = () => {
        const modal = document.getElementById("cncSupplierPaymentEditorModal");
        if (modal) {
            modal.hidden = true;
            modal.dataset.purchaseKey = "";
        }
    };

    const ensureSupplierPaymentEditorModal = () => {
        let modal = document.getElementById("cncSupplierPaymentEditorModal");
        if (modal) {
            return modal;
        }

        modal = document.createElement("div");
        modal.className = "cnc-modal cnc-supplier-payment-editor-modal";
        modal.id = "cncSupplierPaymentEditorModal";
        modal.setAttribute("role", "dialog");
        modal.setAttribute("aria-modal", "true");
        modal.setAttribute("aria-labelledby", "cncSupplierPaymentEditorTitle");
        modal.hidden = true;
        modal.innerHTML = `
            <div class="cnc-modal__panel cnc-supplier-payment-editor-panel">
                <div class="cnc-modal__header">
                    <div><div class="cnc-kicker">Pago a proveedor</div><h2 id="cncSupplierPaymentEditorTitle" data-cnc-supplier-editor-title>Factura</h2></div>
                    <button type="button" class="cnc-cashflow-wizard__close" aria-label="Cerrar" title="Cerrar" data-cnc-supplier-editor-close>&times;</button>
                </div>
                <div class="cnc-supplier-payment-editor-facts">
                    <div><span>Total</span><strong data-cnc-supplier-editor-total></strong></div>
                    <div><span>Saldo pendiente</span><strong data-cnc-supplier-editor-current-balance></strong></div>
                </div>
                <div class="cnc-supplier-payment-editor-grid">
                    <label class="cnc-modal__field"><span>Valor pago</span><input class="form-control" type="number" min="0" step="0.01" data-cnc-supplier-editor-payment /></label>
                    <label class="cnc-modal__field"><span>Copiers (valor pagado)</span><input class="form-control" type="number" min="0" step="0.01" data-cnc-supplier-editor-copiers /></label>
                    <label class="cnc-modal__field"><span>Cloud (valor pagado)</span><input class="form-control" type="number" min="0" step="0.01" data-cnc-supplier-editor-cloud /></label>
                    <label class="cnc-modal__field cnc-supplier-payment-editor-category"><span>Categoria</span><select class="form-select" data-cnc-supplier-editor-category></select></label>
                </div>
                <p class="form-text mb-3">Cloud y Copiers deben sumar el valor pagado. La base DIAN se usa solamente para calcular las retenciones.</p>
                <fieldset class="cnc-supplier-payment-editor-retentions">
                    <legend>Retenciones</legend>
                    <div class="cnc-supplier-payment-editor-retention">
                        <label class="cnc-modal__field"><span>ReteFuente %</span><select class="form-select" data-cnc-supplier-editor-retefuente></select></label>
                        <div><span>Valor ReteFuente</span><strong data-cnc-supplier-editor-retefuente-value></strong></div>
                    </div>
                    <div class="cnc-supplier-payment-editor-retention">
                        <label class="cnc-modal__field"><span>ReteICA %</span><select class="form-select" data-cnc-supplier-editor-reteica></select></label>
                        <div><span>Valor ReteICA</span><strong data-cnc-supplier-editor-reteica-value></strong></div>
                    </div>
                </fieldset>
                <div class="cnc-supplier-payment-editor-balance">
                    <span>Saldo</span><strong data-cnc-supplier-editor-balance></strong>
                </div>
                <p class="cnc-supplier-payment-editor-error" data-cnc-supplier-editor-error hidden></p>
                <div class="cnc-modal__actions">
                    <button type="button" class="btn btn-outline-secondary" data-cnc-supplier-editor-cancel>Cancelar</button>
                    <button type="button" class="btn btn-primary" data-cnc-supplier-editor-send>Enviar</button>
                </div>
            </div>`;
        document.body.appendChild(modal);

        modal.querySelectorAll("[data-cnc-supplier-editor-close], [data-cnc-supplier-editor-cancel]")
            .forEach((button) => button.addEventListener("click", closeSupplierPaymentEditor));
        modal.addEventListener("click", (event) => {
            if (event.target === modal) {
                closeSupplierPaymentEditor();
            }
        });
        [
            "[data-cnc-supplier-editor-payment]",
            "[data-cnc-supplier-editor-copiers]",
            "[data-cnc-supplier-editor-cloud]"
        ].forEach((selector) => {
            modal.querySelector(selector)?.addEventListener("input", () => updateSupplierPaymentEditor(modal, true));
        });
        [
            "[data-cnc-supplier-editor-category]",
            "[data-cnc-supplier-editor-retefuente]",
            "[data-cnc-supplier-editor-reteica]"
        ].forEach((selector) => {
            modal.querySelector(selector)?.addEventListener("change", () => updateSupplierPaymentEditor(modal, true));
        });
        modal.querySelector("[data-cnc-supplier-editor-send]")?.addEventListener("click", async () => {
            const purchase = getSupplierPaymentEditorPurchase(modal);
            if (purchase) {
                await sendCashFlowWizardSupplierPaymentToSiigo(purchase);
            }
        });
        return modal;
    };

    const updateSupplierPaymentEditor = (modal, readFields = false) => {
        const purchase = getSupplierPaymentEditorPurchase(modal);
        if (!purchase) {
            return;
        }
        const draft = getCashFlowWizardSupplierAllocationDraft(purchase);
        if (readFields) {
            draft.paymentValue = Math.max(0, Number(modal.querySelector("[data-cnc-supplier-editor-payment]")?.value || 0));
            draft.copiersValue = Math.max(0, Number(modal.querySelector("[data-cnc-supplier-editor-copiers]")?.value || 0));
            draft.cloudValue = Math.max(0, Number(modal.querySelector("[data-cnc-supplier-editor-cloud]")?.value || 0));
            draft.categoryValue = String(modal.querySelector("[data-cnc-supplier-editor-category]")?.value || "");
            draft.reteFuenteTaxId = Number(modal.querySelector("[data-cnc-supplier-editor-retefuente]")?.value || 0);
            draft.reteIcaTaxId = Number(modal.querySelector("[data-cnc-supplier-editor-reteica]")?.value || 0);
            draft.editorError = "";
            draft.sendFailed = false;
        }

        const calculation = calculateCashFlowWizardSupplierAllocation(purchase);
        const validation = validateCashFlowWizardSupplierAllocation(purchase, draft);
        const reteFuenteValue = modal.querySelector("[data-cnc-supplier-editor-retefuente-value]");
        const reteIcaValue = modal.querySelector("[data-cnc-supplier-editor-reteica-value]");
        const balance = modal.querySelector("[data-cnc-supplier-editor-balance]");
        const error = modal.querySelector("[data-cnc-supplier-editor-error]");
        const send = modal.querySelector("[data-cnc-supplier-editor-send]");
        reteFuenteValue && (reteFuenteValue.textContent = clientPaymentMoney(calculation.reteFuenteValue));
        reteIcaValue && (reteIcaValue.textContent = clientPaymentMoney(calculation.reteIcaValue));
        if (balance) {
            balance.textContent = clientPaymentMoney(calculation.remainingBalance);
            balance.classList.toggle("is-zero", Math.abs(calculation.remainingBalance) <= 1);
            balance.classList.toggle("is-over", calculation.remainingBalance < -1);
        }
        const errorMessage = String(draft.editorError || validation.message || "");
        if (error) {
            error.textContent = errorMessage;
            error.hidden = !errorMessage;
        }
        if (send) {
            send.disabled = !validation.valid || draft.sending || draft.siigoCreated || !supplierPaymentSendUrl;
            send.textContent = draft.sending ? "Enviando..." : draft.sendFailed ? "Reintentar" : "Enviar";
        }
    };

    const openSupplierPaymentEditor = (purchase) => {
        const verified = String(purchase?.dataverseMatchTone || "").toLowerCase() === "success"
            && Boolean(purchase?.dataverseRecordId)
            && Boolean(purchase?.dataverseCufeCude || purchase?.dataverseInvoiceNumber);
        if (!verified) {
            setCashFlowWizardSupplierMessage("La factura no tiene un cruce verificable en Dataverse.", "error");
            return;
        }

        const draft = getCashFlowWizardSupplierAllocationDraft(purchase);
        const modal = ensureSupplierPaymentEditorModal();
        const invoiceNumber = purchase.providerInvoiceFullNumber
            || purchase.providerInvoiceNumber
            || purchase.dataverseInvoiceNumber
            || purchase.name
            || "Factura";
        modal.dataset.purchaseKey = supplierWizardAllocationKey(purchase);
        modal.querySelector("[data-cnc-supplier-editor-title]").textContent = invoiceNumber;
        modal.querySelector("[data-cnc-supplier-editor-total]").textContent = clientPaymentMoney(purchase.dataverseTotal || purchase.total || 0);
        modal.querySelector("[data-cnc-supplier-editor-current-balance]").textContent = clientPaymentMoney(purchase.balance || 0);
        modal.querySelector("[data-cnc-supplier-editor-payment]").value = String(draft.paymentValue || "");
        modal.querySelector("[data-cnc-supplier-editor-copiers]").value = String(draft.copiersValue || "");
        modal.querySelector("[data-cnc-supplier-editor-cloud]").value = String(draft.cloudValue || "");
        const category = modal.querySelector("[data-cnc-supplier-editor-category]");
        category.innerHTML = supplierExpenseCategoryOptionsHtml();
        category.value = String(draft.categoryValue || "");
        const reteFuente = modal.querySelector("[data-cnc-supplier-editor-retefuente]");
        const reteIca = modal.querySelector("[data-cnc-supplier-editor-reteica]");
        reteFuente.innerHTML = clientPaymentRetentionOptionsHtml(
            cashFlowWizardSupplierPayment?.reteFuenteOptions,
            draft.reteFuenteTaxId,
            "Sin ReteFuente");
        reteIca.innerHTML = clientPaymentRetentionOptionsHtml(
            cashFlowWizardSupplierPayment?.reteIcaOptions,
            draft.reteIcaTaxId,
            "Sin ReteICA");
        draft.editorError = "";
        modal.hidden = false;
        updateSupplierPaymentEditor(modal, false);
        window.setTimeout(() => modal.querySelector("[data-cnc-supplier-editor-payment]")?.focus(), 0);
    };

    const renderCashFlowWizardSupplierPartyPicker = () => {
        const modal = ensureCashFlowWizardModal();
        const results = modal.querySelector("[data-cnc-wizard-supplier-parties]");
        const selected = modal.querySelector("[data-cnc-wizard-supplier-party]");
        if (!results || !selected || !cashFlowWizardSupplierPayment) {
            return;
        }

        const supplier = cashFlowWizardSupplierPayment.supplier;
        selected.innerHTML = "";
        selected.hidden = !supplier;
        if (supplier) {
            const label = document.createElement("strong");
            const change = document.createElement("button");
            label.textContent = supplierPaymentLabel(supplier);
            change.type = "button";
            change.className = "btn btn-sm btn-outline-secondary";
            change.textContent = "Cambiar";
            change.addEventListener("click", () => {
                cashFlowWizardSupplierPayment.supplier = null;
                cashFlowWizardSupplierPayment.candidates = [];
                cashFlowWizardSupplierPayment.purchases = [];
                cashFlowWizardSupplierPayment.reteFuenteOptions = [];
                cashFlowWizardSupplierPayment.reteIcaOptions = [];
                cashFlowWizardSupplierPayment.allocations = {};
                const query = modal.querySelector("[data-cnc-wizard-supplier-party-query]");
                if (query) {
                    query.value = "";
                    query.focus();
                }
                renderCashFlowWizardSupplierPartyPicker();
                renderCashFlowWizardSupplierAllocations();
            });
            selected.append(label, change);
        }

        results.innerHTML = "";
        const candidates = supplier ? [] : (cashFlowWizardSupplierPayment.candidates || []);
        results.hidden = candidates.length === 0;
        candidates.forEach((candidate) => {
            const button = document.createElement("button");
            const title = document.createElement("strong");
            const detail = document.createElement("small");
            button.type = "button";
            button.className = "cnc-party-picker__option";
            title.textContent = candidate.commercialName || candidate.name || candidate.displayName || "Proveedor Siigo";
            detail.textContent = [candidate.identification, Number(candidate.branchOffice || 0) > 0 ? `Sucursal ${candidate.branchOffice}` : ""]
                .filter(Boolean)
                .join(" - ");
            button.append(title, detail);
            button.addEventListener("click", () => selectCashFlowWizardSupplierParty(candidate));
            results.appendChild(button);
        });
    };

    const searchCashFlowWizardSupplierParties = async (query) => {
        if (!cashFlowWizardSupplierPayment || !siigoSupplierSearchUrl || query.length < 2) {
            return;
        }
        const sequence = Number(cashFlowWizardSupplierPayment.searchSequence || 0) + 1;
        cashFlowWizardSupplierPayment.searchSequence = sequence;
        try {
            const response = await fetch(siigoSupplierSearchUrl, {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify({ query, top: 10 })
            });
            const payload = await response.json().catch(() => ({}));
            if (!response.ok) {
                throw new Error(payload.detail || payload.message || "No fue posible buscar proveedores.");
            }
            if (!cashFlowWizardSupplierPayment || cashFlowWizardSupplierPayment.searchSequence !== sequence) {
                return;
            }
            cashFlowWizardSupplierPayment.candidates = payload.items || [];
            renderCashFlowWizardSupplierPartyPicker();
        } catch (error) {
            if (cashFlowWizardSupplierPayment?.searchSequence === sequence) {
                cashFlowWizardSupplierPayment.candidates = [];
                renderCashFlowWizardSupplierPartyPicker();
                setCashFlowWizardSupplierMessage(error instanceof Error ? error.message : "No fue posible buscar proveedores.", "error");
            }
        }
    };

    const scheduleCashFlowWizardSupplierSearch = (query) => {
        if (!cashFlowWizardSupplierPayment) {
            return;
        }
        window.clearTimeout(cashFlowWizardSupplierPayment.searchTimer || 0);
        if (query.length < 2) {
            cashFlowWizardSupplierPayment.candidates = [];
            renderCashFlowWizardSupplierPartyPicker();
            return;
        }
        cashFlowWizardSupplierPayment.searchTimer = window.setTimeout(
            () => searchCashFlowWizardSupplierParties(query),
            280);
    };

    const selectCashFlowWizardSupplierParty = (supplier) => {
        if (!cashFlowWizardSupplierPayment) {
            return;
        }
        const modal = ensureCashFlowWizardModal();
        cashFlowWizardSupplierPayment.searchSequence = Number(cashFlowWizardSupplierPayment.searchSequence || 0) + 1;
        cashFlowWizardSupplierPayment.supplier = supplier;
        cashFlowWizardSupplierPayment.candidates = [];
        cashFlowWizardSupplierPayment.purchases = [];
        cashFlowWizardSupplierPayment.reteFuenteOptions = [];
        cashFlowWizardSupplierPayment.reteIcaOptions = [];
        cashFlowWizardSupplierPayment.allocations = {};
        const query = modal.querySelector("[data-cnc-wizard-supplier-party-query]");
        if (query) {
            query.value = supplierPaymentLabel(supplier);
        }
        renderCashFlowWizardSupplierPartyPicker();
        loadCashFlowWizardSupplierOpenPurchases();
    };

    const renderCashFlowWizardSupplierAllocations = () => {
        const modal = ensureCashFlowWizardModal();
        const wrap = modal.querySelector("[data-cnc-wizard-supplier-purchase-wrap]");
        const body = modal.querySelector("[data-cnc-wizard-supplier-open-purchases]");
        if (!wrap || !body || !cashFlowWizardSupplierPayment) {
            return;
        }
        const purchases = cashFlowWizardSupplierPayment.purchases || [];
        wrap.hidden = !cashFlowWizardSupplierPayment.supplier;
        body.innerHTML = "";
        if (cashFlowWizardSupplierPayment.loadingPurchases || purchases.length === 0) {
            const row = document.createElement("tr");
            const cell = document.createElement("td");
            cell.colSpan = 5;
            cell.className = "cnc-payment-empty";
            cell.textContent = cashFlowWizardSupplierPayment.loadingPurchases ? "Consultando saldos..." : "Sin facturas con saldo.";
            row.appendChild(cell);
            body.appendChild(row);
            return;
        }

        purchases.forEach((purchase) => {
            const draft = getCashFlowWizardSupplierAllocationDraft(purchase);
            const row = document.createElement("tr");
            const verified = String(purchase.dataverseMatchTone || "").toLowerCase() === "success"
                && Boolean(purchase.dataverseRecordId)
                && Boolean(purchase.dataverseCufeCude || purchase.dataverseInvoiceNumber);
            const invoiceNumber = purchase.providerInvoiceFullNumber
                || purchase.providerInvoiceNumber
                || purchase.dataverseInvoiceNumber
                || purchase.name
                || "Sin numero";
            row.className = "cnc-payment-allocation-row cnc-supplier-payment-allocation-row";
            row.classList.toggle("is-sending", Boolean(draft?.sending));
            row.classList.toggle("is-error", Boolean(draft?.sendFailed));
            row.innerHTML = `
                <td data-label="Factura"><strong>${escapeHtml(invoiceNumber)}</strong></td>
                <td data-label="Fecha">${escapeHtml(purchase.dateDisplay || purchase.dateValue || "")}</td>
                <td data-label="Total" class="text-end"><strong>${escapeHtml(clientPaymentMoney(purchase.dataverseTotal || purchase.total || 0))}</strong></td>
                <td data-label="Saldo pendiente" class="text-end"><strong>${escapeHtml(clientPaymentMoney(purchase.balance || 0))}</strong></td>
                <td data-label="Accion" class="cnc-payment-apply-cell">
                    <button type="button" class="btn btn-sm btn-outline-primary" data-cnc-supplier-apply${verified ? "" : " disabled"} title="${verified ? "Abrir pago" : "Sin cruce verificable en Dataverse"}">Aplicar</button>
                </td>`;
            const apply = row.querySelector("[data-cnc-supplier-apply]");
            apply?.addEventListener("click", () => openSupplierPaymentEditor(purchase));
            body.appendChild(row);
        });
    };

    const loadCashFlowWizardSupplierOpenPurchases = async () => {
        if (!cashFlowWizardSupplierPayment?.supplier || !supplierPaymentPurchasesUrl) {
            return;
        }
        const state = cashFlowWizardSupplierPayment;
        state.loadingPurchases = true;
        state.purchases = [];
        state.reteFuenteOptions = [];
        state.reteIcaOptions = [];
        state.allocations = {};
        renderCashFlowWizardSupplierAllocations();
        setCashFlowWizardSupplierMessage("", "info");
        try {
            const request = supplierPaymentRequestForRow(state.row, state.supplier);
            request.supplierId = state.supplier.id || "";
            request.supplierQuery = state.supplier.identification || state.supplier.displayName || "";
            const response = await fetch(supplierPaymentPurchasesUrl, {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify(request)
            });
            const payload = await response.json().catch(() => ({}));
            if (!response.ok) {
                throw new Error(payload.detail || payload.message || "No fue posible consultar las facturas.");
            }
            if (cashFlowWizardSupplierPayment !== state) {
                return;
            }
            state.supplier = payload.supplier || state.supplier;
            state.purchases = payload.purchases || [];
            state.reteFuenteOptions = payload.reteFuenteOptions || [];
            state.reteIcaOptions = payload.reteIcaOptions || [];
        } catch (error) {
            state.purchases = [];
            setCashFlowWizardSupplierMessage(error instanceof Error ? error.message : "No fue posible consultar las facturas.", "error");
        } finally {
            if (cashFlowWizardSupplierPayment === state) {
                state.loadingPurchases = false;
                renderCashFlowWizardSupplierPartyPicker();
                renderCashFlowWizardSupplierAllocations();
            }
        }
    };

    const renderCashFlowWizardSupplierPayment = (row, message = "", tone = "info") => {
        const modal = ensureCashFlowWizardModal();
        setCashFlowWizardTitle("Pago a proveedor");
        const card = modal.querySelector("[data-cnc-cashflow-wizard-card]");
        const count = modal.querySelector("[data-cnc-cashflow-wizard-count]");
        const bar = modal.querySelector("[data-cnc-cashflow-wizard-bar]");
        cashFlowWizardMode = "supplier-payment";
        setCashFlowWizardProcessActions();
        if (count) {
            count.textContent = `Salida ${cashFlowWizardIndex + 1} de ${cashFlowWizardRows.length}`;
        }
        if (bar) {
            bar.style.width = `${Math.round((cashFlowWizardIndex + 1) / Math.max(cashFlowWizardRows.length, 1) * 100)}%`;
        }

        const existingState = cashFlowWizardSupplierPayment?.row === row ? cashFlowWizardSupplierPayment : null;
        cashFlowWizardSupplierPayment = existingState || {
            row,
            query: "",
            supplier: null,
            candidates: [],
            purchases: [],
            allocations: {},
            reteFuenteOptions: [],
            reteIcaOptions: [],
            issues: [],
            loadingPurchases: false,
            searchSequence: 0,
            searchTimer: 0
        };

        if (card) {
            card.innerHTML = `
                <div class="cnc-wizard-process">
                    <header class="cnc-wizard-process__summary">
                        <div>
                            <span>${escapeHtml(row.dataset.dateDisplay || "Sin fecha")}</span>
                            <p>${escapeHtml(resolveCashFlowRowLabel(row))}</p>
                        </div>
                        <strong>${escapeHtml(row.dataset.amountLabel || money(Number(row.dataset.exitValue || 0)))}</strong>
                    </header>
                    <div class="cnc-party-picker">
                        <label class="cnc-modal__field">
                            <span>Proveedor</span>
                            <input class="form-control" type="search" autocomplete="off" data-cnc-wizard-supplier-party-query placeholder="Escribe nombre o NIT" />
                        </label>
                        <div class="cnc-party-picker__results" data-cnc-wizard-supplier-parties hidden></div>
                    </div>
                    <div class="cnc-party-picker__selected" data-cnc-wizard-supplier-party hidden></div>
                    <div class="cnc-supplier-payment-table-wrap" data-cnc-wizard-supplier-purchase-wrap hidden>
                        <table class="table align-middle cnc-table cnc-supplier-payment-allocation-table">
                            <thead><tr><th>Factura</th><th>Fecha</th><th class="text-end">Total</th><th class="text-end">Saldo pendiente</th><th>Accion</th></tr></thead>
                            <tbody data-cnc-wizard-supplier-open-purchases></tbody>
                        </table>
                    </div>
                    <ul class="cnc-issue-list" data-cnc-wizard-supplier-issues hidden></ul>
                    <div class="cnc-wizard-process__actions">
                        <button type="button" class="btn btn-outline-secondary" data-cnc-wizard-supplier-direct-back>Volver</button>
                    </div>
                </div>`;
        }

        const query = modal.querySelector("[data-cnc-wizard-supplier-party-query]");
        if (query && cashFlowWizardSupplierPayment.supplier) {
            query.value = supplierPaymentLabel(cashFlowWizardSupplierPayment.supplier);
        }
        query?.addEventListener("input", () => {
            const value = String(query.value || "").trim();
            cashFlowWizardSupplierPayment.query = value;
            if (cashFlowWizardSupplierPayment.supplier
                && value !== supplierPaymentLabel(cashFlowWizardSupplierPayment.supplier)) {
                cashFlowWizardSupplierPayment.supplier = null;
                cashFlowWizardSupplierPayment.purchases = [];
                cashFlowWizardSupplierPayment.reteFuenteOptions = [];
                cashFlowWizardSupplierPayment.reteIcaOptions = [];
                cashFlowWizardSupplierPayment.allocations = {};
                renderCashFlowWizardSupplierAllocations();
            }
            scheduleCashFlowWizardSupplierSearch(value);
        });
        modal.querySelector("[data-cnc-wizard-supplier-direct-back]")?.addEventListener("click", () => {
            cashFlowWizardMode = "rows";
            cashFlowWizardSupplierPayment = null;
            renderCashFlowWizard();
        });
        renderCashFlowWizardSupplierPartyPicker();
        renderCashFlowWizardSupplierAllocations();
        renderCashFlowWizardSupplierIssues(cashFlowWizardSupplierPayment.issues || []);
        setCashFlowWizardSupplierMessage(message, tone);
        window.setTimeout(() => query?.focus(), 0);
    };

    const ensureSupplierPaymentProgressModal = () => {
        let modal = document.getElementById("cncSupplierPaymentProgressModal");
        if (modal) {
            return modal;
        }

        modal = document.createElement("div");
        modal.className = "cnc-modal cnc-client-payment-progress-modal";
        modal.id = "cncSupplierPaymentProgressModal";
        modal.setAttribute("role", "dialog");
        modal.setAttribute("aria-modal", "true");
        modal.hidden = true;
        modal.innerHTML = `
            <div class="cnc-modal__panel cnc-client-payment-progress-panel">
                <div class="cnc-modal__header">
                    <div><div class="cnc-kicker">Pago a proveedor</div><h2>Procesando conciliacion</h2></div>
                </div>
                <div class="cnc-client-payment-progress-list">
                    <div class="cnc-client-payment-progress-step" data-cnc-supplier-progress-step="allocation">
                        <span>1</span><div><strong>Distribucion en Dataverse</strong><small>En espera</small></div>
                    </div>
                    <div class="cnc-client-payment-progress-step" data-cnc-supplier-progress-step="siigo">
                        <span>2</span><div><strong>Pago en Siigo</strong><small>En espera</small></div>
                    </div>
                    <div class="cnc-client-payment-progress-step" data-cnc-supplier-progress-step="reconciliation">
                        <span>3</span><div><strong>Conciliacion en Dataverse</strong><small>En espera</small></div>
                    </div>
                </div>
                <p class="cnc-client-payment-progress-summary" data-cnc-supplier-progress-summary></p>
                <div class="cnc-modal__actions" data-cnc-supplier-progress-actions hidden>
                    <button type="button" class="btn btn-outline-secondary" data-cnc-supplier-progress-close>Cerrar</button>
                </div>
            </div>`;
        document.body.appendChild(modal);
        modal.querySelector("[data-cnc-supplier-progress-close]")?.addEventListener("click", () => {
            modal.hidden = true;
        });
        return modal;
    };

    const setSupplierPaymentProgressStep = (modal, key, state, message) => {
        const step = modal.querySelector(`[data-cnc-supplier-progress-step="${key}"]`);
        if (!step) {
            return;
        }
        step.dataset.state = state;
        const status = step.querySelector("small");
        if (status) {
            status.textContent = message || state;
        }
    };

    const openSupplierPaymentProgress = () => {
        const modal = ensureSupplierPaymentProgressModal();
        const summary = modal.querySelector("[data-cnc-supplier-progress-summary]");
        const actions = modal.querySelector("[data-cnc-supplier-progress-actions]");
        modal.hidden = false;
        modal.dataset.state = "running";
        if (summary) {
            summary.textContent = "Guardando Cloud, Copiers y categoria...";
        }
        if (actions) {
            actions.hidden = true;
        }
        setSupplierPaymentProgressStep(modal, "allocation", "running", "Guardando cambios");
        setSupplierPaymentProgressStep(modal, "siigo", "waiting", "En espera");
        setSupplierPaymentProgressStep(modal, "reconciliation", "waiting", "En espera");
        return modal;
    };

    const finishSupplierPaymentProgress = (modal, success, message) => {
        const summary = modal.querySelector("[data-cnc-supplier-progress-summary]");
        const actions = modal.querySelector("[data-cnc-supplier-progress-actions]");
        modal.dataset.state = success ? "success" : "error";
        if (summary) {
            summary.textContent = message || (success ? "Pago conciliado." : "No fue posible completar la conciliacion.");
        }
        if (actions) {
            actions.hidden = success;
        }
    };

    const sendCashFlowWizardSupplierPaymentToSiigo = async (purchase) => {
        if (!purchase || !cashFlowWizardSupplierPayment?.row || !cashFlowWizardSupplierPayment.supplier || !supplierPaymentSendUrl) {
            setCashFlowWizardSupplierMessage("Selecciona el proveedor y una factura valida.", "info");
            return false;
        }

        const editorModal = ensureSupplierPaymentEditorModal();
        updateSupplierPaymentEditor(editorModal, true);
        const draft = getCashFlowWizardSupplierAllocationDraft(purchase);
        const validation = validateCashFlowWizardSupplierAllocation(purchase, draft);
        if (!validation.valid) {
            draft.editorError = validation.message;
            updateSupplierPaymentEditor(editorModal, false);
            return false;
        }

        draft.sending = true;
        draft.sendFailed = false;
        draft.editorError = "";
        const progressModal = openSupplierPaymentProgress();
        renderCashFlowWizardSupplierIssues([]);
        renderCashFlowWizardSupplierAllocations();
        updateSupplierPaymentEditor(editorModal, false);
        setCashFlowWizardSupplierMessage("", "info");
        let payload = {};
        try {
            const supplier = cashFlowWizardSupplierPayment.supplier;
            const calculation = calculateCashFlowWizardSupplierAllocation(purchase);
            const allocation = buildCashFlowWizardSupplierAllocation(purchase);
            const request = supplierPaymentRequestForRow(cashFlowWizardSupplierPayment.row, supplier);
            request.supplierId = supplier.id || "";
            request.supplierIdentification = supplier.identification || "";
            request.supplierName = supplierPaymentLabel(supplier);
            request.purchaseId = allocation.documentId;
            request.purchaseName = allocation.documentName;
            request.allocations = [allocation];
            request.reteFuenteValue = calculation.reteFuenteValue;
            request.reteFuenteRate = Number(calculation.reteFuente?.rate || 0);
            request.reteIcaValue = calculation.reteIcaValue;
            request.reteIcaRate = Number(calculation.reteIca?.rate || 0);

            const response = await fetch(supplierPaymentSendUrl, {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify(request)
            });
            payload = await response.json().catch(() => ({}));
            cashFlowWizardSupplierPayment.issues = payload.issues || [];
            draft.siigoCreated = Boolean(payload.siigoSucceeded);
            renderCashFlowWizardSupplierIssues(cashFlowWizardSupplierPayment.issues);
            const transientSiigoFailure = !payload.siigoSucceeded
                && String(payload.message || "").toLowerCase().includes("temporalmente");
            setSupplierPaymentProgressStep(
                progressModal,
                "allocation",
                payload.dataverseChangesSucceeded ? "success" : "error",
                payload.dataverseChangesSucceeded ? "Cambios guardados" : "No confirmado");
            setSupplierPaymentProgressStep(
                progressModal,
                "siigo",
                payload.siigoSucceeded ? "success" : "error",
                payload.siigoSucceeded
                    ? (payload.siigoName || "Pago creado")
                    : transientSiigoFailure ? "Servicio no disponible" : "Envio fallido");
            setSupplierPaymentProgressStep(
                progressModal,
                "reconciliation",
                payload.dataverseReconciliationSucceeded ? "success" : "error",
                payload.dataverseReconciliationSucceeded ? "Movimiento conciliado" : "No conciliado");
            if (!response.ok) {
                throw new Error(payload.detail || payload.message || "No fue posible enviar el pago.");
            }
            if (!payload.isSuccess) {
                draft.sending = false;
                draft.sendFailed = !draft.siigoCreated;
                draft.editorError = payload.message || "El pago no fue conciliado.";
                finishSupplierPaymentProgress(progressModal, false, payload.message || "El pago no fue conciliado.");
                renderCashFlowWizardSupplierAllocations();
                updateSupplierPaymentEditor(editorModal, false);
                return false;
            }

            draft.sending = false;
            finishSupplierPaymentProgress(progressModal, true, payload.message || "Pago conciliado en Siigo y Dataverse.");
            const completedRow = cashFlowWizardSupplierPayment.row;
            await delay(650);
            progressModal.hidden = true;
            closeSupplierPaymentEditor();
            completeCashFlowWizardRow(completedRow, payload.row, payload.message || "Pago proveedor conciliado.");
            ensureCashFlowWizardModal().hidden = true;
            return true;
        } catch (error) {
            draft.sending = false;
            draft.sendFailed = !draft.siigoCreated;
            const errorMessage = error instanceof Error ? error.message : "No fue posible enviar el pago.";
            draft.editorError = errorMessage;
            if (!payload.dataverseChangesSucceeded) {
                setSupplierPaymentProgressStep(progressModal, "allocation", "error", "No confirmado");
            }
            if (!payload.siigoSucceeded) {
                setSupplierPaymentProgressStep(progressModal, "siigo", "error", "Envio fallido");
            }
            if (!payload.dataverseReconciliationSucceeded) {
                setSupplierPaymentProgressStep(progressModal, "reconciliation", "error", "No conciliado");
            }
            finishSupplierPaymentProgress(progressModal, false, errorMessage);
            renderCashFlowWizardSupplierAllocations();
            updateSupplierPaymentEditor(editorModal, false);
            return false;
        }
    };

    const renderCashFlowWizard = (message = "", tone = "info") => {
        const modal = ensureCashFlowWizardModal();
        setCashFlowWizardTitle("Conciliar movimiento");
        const card = modal.querySelector("[data-cnc-cashflow-wizard-card]");
        const count = modal.querySelector("[data-cnc-cashflow-wizard-count]");
        const bar = modal.querySelector("[data-cnc-cashflow-wizard-bar]");
        const messageBox = modal.querySelector("[data-cnc-cashflow-wizard-message]");
        const prev = modal.querySelector("[data-cnc-cashflow-wizard-prev]");
        const next = modal.querySelector("[data-cnc-cashflow-wizard-next]");
        const process = modal.querySelector("[data-cnc-cashflow-wizard-process]");
        const pending = modal.querySelector("[data-cnc-cashflow-wizard-pending]");
        const omitted = modal.querySelector("[data-cnc-cashflow-wizard-omitted]");
        const progress = modal.querySelector(".cnc-cashflow-wizard__progress");
        resetCashFlowWizardActionButtons();

        cashFlowWizardRows = cashFlowWizardRows.filter((row) =>
            row?.isConnected
            && row.dataset.cncCashflowPending === "true"
            && !isAccumulatedCashFlowRow(row));
        refreshCashFlowWizardAccumulatedGroups();
        if (cashFlowWizardMode === "accumulated" || (cashFlowWizardRows.length === 0 && cashFlowWizardAccumulatedGroups.length > 0)) {
            cashFlowWizardMode = "accumulated";
            renderCashFlowAccumulatedGroups(modal, message, tone);
            return;
        }

        if (cashFlowWizardRows.length === 0) {
            if (card) {
                card.innerHTML = `<div class="cnc-empty-state"><strong>No hay pendientes visibles.</strong><small>Todos los items visibles del flujo de caja quedaron conciliados o filtrados.</small></div>`;
            }
            if (count) {
                count.textContent = "0 de 0";
            }
            if (bar) {
                bar.style.width = "100%";
            }
            [prev, next, process, pending, omitted].forEach((button) => {
                if (button) {
                    button.disabled = true;
                }
            });
            if (progress) {
                progress.hidden = true;
            }
            if (messageBox) {
                messageBox.textContent = message || "No quedan items por recorrer.";
                messageBox.className = `cnc-cashflow-wizard__message is-${tone}`;
            }
            return;
        }

        cashFlowWizardIndex = Math.min(Math.max(cashFlowWizardIndex, 0), cashFlowWizardRows.length - 1);
        const row = cashFlowWizardRows[cashFlowWizardIndex];
        const currentType = row.dataset.currentType || "";
        const options = getCategoryOptionsForRow(row);
        const optionHtml = options.map((option) =>
            `<option value="${escapeHtml(option.value)}"${option.value === currentType ? " selected" : ""}>${escapeHtml(option.label)}</option>`
        ).join("");

        if (card) {
            card.innerHTML = `
                <section class="cnc-cashflow-wizard__movement">
                    <div class="cnc-cashflow-wizard__facts">
                        <div class="cnc-cashflow-wizard__fact">
                            <span>Fecha</span>
                            <strong>${escapeHtml(row.dataset.dateDisplay || "Sin fecha")}</strong>
                        </div>
                        <div class="cnc-cashflow-wizard__fact cnc-cashflow-wizard__fact--total">
                            <span>Total</span>
                            <strong>${escapeHtml(row.dataset.amountLabel || "")}</strong>
                        </div>
                    </div>
                    <div class="cnc-cashflow-wizard__description">
                        <span>Descripci&oacute;n</span>
                        <p>${escapeHtml(resolveCashFlowRowLabel(row))}</p>
                    </div>
                </section>
                <label class="cnc-modal__field cnc-cashflow-wizard__category">
                    <span>Categor&iacute;a</span>
                    <select class="form-select" data-cnc-cashflow-wizard-category aria-label="Categor&iacute;a del movimiento">${optionHtml}</select>
                </label>`;
        }

        if (count) {
            count.textContent = `${cashFlowWizardIndex + 1} de ${cashFlowWizardRows.length}`;
        }
        if (bar) {
            bar.style.width = `${Math.round((cashFlowWizardIndex + 1) / cashFlowWizardRows.length * 100)}%`;
        }
        if (progress) {
            progress.hidden = cashFlowWizardRows.length <= 1;
        }
        if (messageBox) {
            const showMessage = Boolean(message) && tone !== "info";
            messageBox.textContent = showMessage ? message : "";
            messageBox.className = showMessage
                ? `cnc-cashflow-wizard__message is-${tone}`
                : "cnc-cashflow-wizard__message is-empty";
        }
        if (prev) {
            prev.hidden = cashFlowWizardRows.length <= 1;
            prev.disabled = cashFlowWizardIndex === 0;
        }
        if (next) {
            next.hidden = true;
        }
        [process, pending, omitted].forEach((button) => {
            if (button) {
                button.hidden = false;
                button.disabled = false;
            }
        });
    };

    const openCashFlowWizard = () => {
        cashFlowWizardRows = getVisibleCashFlowWizardRows();
        cashFlowWizardAccumulatedGroups = getVisibleCashFlowAccumulatedGroups();
        cashFlowWizardIndex = 0;
        cashFlowWizardMode = cashFlowWizardRows.length > 0 ? "rows" : "accumulated";
        const modal = ensureCashFlowWizardModal();
        modal.hidden = false;
        renderCashFlowWizard(
            cashFlowWizardRows.length
                ? "Recorriendo pendientes visibles del flujo de caja. Los acumulados quedan al final."
                : cashFlowWizardAccumulatedGroups.length
                    ? "No hay pendientes individuales; mostrando acumulados al final."
                    : "No hay pendientes visibles para iniciar.",
            cashFlowWizardRows.length || cashFlowWizardAccumulatedGroups.length ? "info" : "success");
    };

    const openCashFlowWizardForRow = (row) => {
        if (!row) {
            return;
        }
        if (row.dataset.cncCashflowPending !== "true") {
            const omitted = String(row.dataset.dataverseStatus || "").trim().toLowerCase() === "omitido";
            setStatus(
                omitted
                    ? "Esta fila esta omitida y no se enviara a Siigo."
                    : "Esta fila ya aparece conciliada. El envio a Siigo no se abre para evitar duplicados.",
                "info");
            return;
        }

        cashFlowWizardRows = [row];
        cashFlowWizardAccumulatedGroups = [];
        cashFlowWizardIndex = 0;
        cashFlowWizardMode = "rows";
        const modal = ensureCashFlowWizardModal();
        modal.hidden = false;
        renderCashFlowWizard();
    };

    const moveCashFlowWizard = (offset) => {
        if (!["rows", "accumulated"].includes(cashFlowWizardMode)) {
            const returnToAccumulated = cashFlowWizardMode === "accounting-voucher"
                && cashFlowWizardAccountingVoucher?.group;
            cashFlowWizardMode = returnToAccumulated ? "accumulated" : "rows";
            resetCashFlowWizardProcessState();
            renderCashFlowWizard();
            return;
        }

        if (cashFlowWizardMode === "accumulated") {
            if (offset < 0 && cashFlowWizardRows.length > 0) {
                cashFlowWizardMode = "rows";
                cashFlowWizardIndex = cashFlowWizardRows.length - 1;
            }
            renderCashFlowWizard();
            return;
        }

        if (cashFlowWizardRows.length === 0) {
            cashFlowWizardMode = cashFlowWizardAccumulatedGroups.length > 0 ? "accumulated" : "rows";
            renderCashFlowWizard();
            return;
        }

        if (offset > 0 && cashFlowWizardIndex >= cashFlowWizardRows.length - 1 && cashFlowWizardAccumulatedGroups.length > 0) {
            cashFlowWizardMode = "accumulated";
            renderCashFlowWizard("Acumulados unificados al final del recorrido.", "info");
            return;
        }

        cashFlowWizardIndex = Math.min(
            Math.max(cashFlowWizardIndex + offset, 0),
            cashFlowWizardRows.length - 1);
        renderCashFlowWizard();
    };

    const saveCashFlowCategoryForRow = async (row, nextValue, nextLabel, nextTone, reason) => {
        if (!row || !cashFlowCategoryUrl) {
            throw new Error("No se encontro la ruta o la fila del flujo de caja para guardar categoria.");
        }

        const response = await fetch(cashFlowCategoryUrl, {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify({
                recordId: row.dataset.cashflowRecordId || row.dataset.recordId || "",
                sourceKind: row.dataset.sourceKind || "Movimiento",
                movementExternalKey: row.dataset.movementExternalKey || "",
                clientPaymentRecordId: row.dataset.matchRecordId || row.dataset.clientPaymentRecordId || "",
                categoryValue: nextValue,
                reason
            })
        });
        const payload = await response.json().catch(() => ({}));
        if (!response.ok) {
            throw new Error(payload.detail || payload.message || "No fue posible guardar la categoria.");
        }

        const savedValue = payload.categoryValue || nextValue;
        const savedLabel = payload.categoryLabel || nextLabel;
        const savedTone = payload.categoryTone || nextTone;
        findCashFlowSiblingRows(row).forEach((targetRow) => {
            syncConciliacion2CategoryDisplay(targetRow, savedValue, savedLabel, savedTone);
        });
        return payload;
    };

    const processCurrentCashFlowWizardRow = async () => {
        const row = cashFlowWizardRows[cashFlowWizardIndex];
        if (!row) {
            renderCashFlowWizard("No se encontro el item actual.", "error");
            return;
        }

        const modal = ensureCashFlowWizardModal();
        const select = modal.querySelector("[data-cnc-cashflow-wizard-category]");
        const process = modal.querySelector("[data-cnc-cashflow-wizard-process]");
        if (select && select.value && select.value !== row.dataset.currentType) {
            const nextValue = select.value;
            const nextLabel = select.options[select.selectedIndex]?.textContent || categoryLabel(nextValue);
            const nextTone = categoryTone(nextValue);
            select.disabled = true;
            if (process) {
                process.disabled = true;
                process.textContent = "Guardando...";
            }
            setStatus("Guardando categoria antes de procesar...", "info");
            try {
                const payload = await saveCashFlowCategoryForRow(
                    row,
                    nextValue,
                    nextLabel,
                    nextTone,
                    `Categoria seleccionada como ${nextLabel} desde Conciliacion 2 antes de procesar.`);
                setStatus(payload.message || "Categoria guardada. Abriendo proceso...", "success");
                if (isNoSiigoCashFlowCategory(row.dataset.currentType || nextValue)) {
                    markCashFlowRowNoSiigo(row);
                    cashFlowWizardRows.splice(cashFlowWizardIndex, 1);
                    if (cashFlowWizardIndex >= cashFlowWizardRows.length) {
                        cashFlowWizardIndex = Math.max(0, cashFlowWizardRows.length - 1);
                    }
                    refreshBulkSections();
                    renderCashFlowWizard(`${nextLabel} guardada. Este item no requiere envio a Siigo.`, "success");
                    return;
                }
            } catch (error) {
                const message = error instanceof Error ? error.message : "Ocurrio un error inesperado.";
                renderCashFlowWizard(message, "error");
                setStatus(message, "error");
                if (process) {
                    process.disabled = false;
                    process.textContent = "Procesar";
                }
                select.disabled = false;
                return;
            } finally {
                if (process) {
                    process.textContent = "Procesar";
                }
                select.disabled = false;
            }
        }

        if (isNoSiigoCashFlowCategory(row.dataset.currentType || "")) {
            markCashFlowRowNoSiigo(row);
            cashFlowWizardRows.splice(cashFlowWizardIndex, 1);
            if (cashFlowWizardIndex >= cashFlowWizardRows.length) {
                cashFlowWizardIndex = Math.max(0, cashFlowWizardRows.length - 1);
            }
            refreshBulkSections();
            renderCashFlowWizard("Este item no requiere envio a Siigo y quedo conciliado.", "success");
            return;
        }

        switch (row.dataset.currentType || "") {
            case "entrada-fe":
                renderCashFlowWizardClientPayment(row);
                return;
            case "salida-fe":
                renderCashFlowWizardSupplierPayment(row);
                return;
            case "cuenta-cobro":
                renderCashFlowWizardCuentaCobro(row);
                return;
            case "comprobante-contable":
            case "entrada-comprobante":
            case "traslado-interno":
                renderCashFlowWizardAccountingVoucher(row);
                return;
            default:
                renderCashFlowWizard("No encontre un flujo especifico para esta categoria. Selecciona otra categoria para continuar.", "warning");
        }
    };

    const openCashFlowAccumulatedGroup = (groupKey) => {
        const group = cashFlowWizardAccumulatedGroups.find((item) => item.key === groupKey);
        if (!group) {
            renderCashFlowAccumulatedGroups(ensureCashFlowWizardModal(), "No encontre el acumulado seleccionado.", "error");
            return;
        }

        renderCashFlowWizardAccountingVoucher(group.rows[0] || null, "Acumulado abierto con su desglose.", "info", {
            accumulatedGroup: group
        });
    };

    const leaveCurrentCashFlowWizardPending = () => {
        const row = cashFlowWizardRows[cashFlowWizardIndex];
        if (!row) {
            ensureCashFlowWizardModal().hidden = true;
            return;
        }

        openCashFlowPendingModal(row);
    };

    const leaveCurrentCashFlowWizardOmitted = () => {
        const row = cashFlowWizardRows[cashFlowWizardIndex];
        if (!row) {
            ensureCashFlowWizardModal().hidden = true;
            return;
        }

        openCashFlowPendingModal(row, "omitted");
    };

    const closeDianEditModal = () => {
        const shouldReload = dianAccountBatchDirty;
        dianAccountBatchRows = [];
        dianAccountBatchIndex = -1;
        dianAccountBatchDirty = false;
        if (dianEditModal) {
            dianEditModal.hidden = true;
        }
        if (dianSkip) {
            dianSkip.hidden = true;
        }
        if (dianSave) {
            dianSave.textContent = "Guardar";
            dianSave.disabled = false;
        }
        activeDianRow = null;
        if (shouldReload) {
            window.setTimeout(reloadPreservingView, 650);
        }
    };

    const isDianAccountBatchActive = () =>
        dianAccountBatchRows.length > 0 && dianAccountBatchIndex >= 0;

    const openDianEditModal = (row, options = {}) => {
        activeDianRow = row;
        if (dianEditDescription) {
            const base = row.dataset.description || "Documento DIAN sin descripcion.";
            dianEditDescription.textContent = options.prefix
                ? `${options.prefix} ${base}`
                : base;
        }
        if (dianAccount) {
            dianAccount.value = row.dataset.accountCode || "";
        }
        if (dianSkip) {
            dianSkip.hidden = !isDianAccountBatchActive();
        }
        if (dianSave) {
            dianSave.textContent = isDianAccountBatchActive() ? "Guardar y continuar" : "Guardar";
            dianSave.disabled = false;
        }
        resetAccountSearch("cncDianAccount");
        if (dianEditModal) {
            dianEditModal.hidden = false;
        }
    };

    const openNextDianAccountBatchRow = (message = "", tone = "info") => {
        dianAccountBatchIndex += 1;
        if (dianAccountBatchIndex >= dianAccountBatchRows.length) {
            const hadChanges = dianAccountBatchDirty;
            dianAccountBatchRows = [];
            dianAccountBatchIndex = -1;
            dianAccountBatchDirty = false;
            if (dianSkip) {
                dianSkip.hidden = true;
            }
            if (dianEditModal) {
                dianEditModal.hidden = true;
            }
            activeDianRow = null;
            if (dianSave) {
                dianSave.textContent = "Guardar";
                dianSave.disabled = false;
            }
            setStatus(message || "Ajuste masivo de cuentas finalizado.", hadChanges ? "success" : tone);
            if (hadChanges) {
                window.setTimeout(reloadPreservingView, 650);
            }
            return;
        }

        const current = dianAccountBatchRows[dianAccountBatchIndex];
        const prefix = `${dianAccountBatchIndex + 1} de ${dianAccountBatchRows.length}.`;
        if (message) {
            setStatus(message, tone);
        }
        openDianEditModal(current, { prefix });
    };

    const openBulkDianAccountModal = (_section, rows) => {
        dianAccountBatchRows = rows.filter((row) => row?.dataset?.recordId);
        dianAccountBatchIndex = -1;
        dianAccountBatchDirty = false;
        if (dianAccountBatchRows.length === 0) {
            setStatus("Selecciona documentos DIAN para ajustar cuenta gasto.", "info");
            return;
        }

        openNextDianAccountBatchRow(`Ajustando cuenta gasto para ${dianAccountBatchRows.length} documento${dianAccountBatchRows.length === 1 ? "" : "s"}.`, "info");
    };

    const skipDianAccountBatchRow = () => {
        if (!isDianAccountBatchActive()) {
            return;
        }

        openNextDianAccountBatchRow("Documento omitido. Continuando con el siguiente.", "info");
    };

    const saveDianClassification = async () => {
        if (!activeDianRow || !dianClassificationUrl) {
            setStatus("No se encontro la ruta o el documento DIAN para guardar.", "error");
            return;
        }

        const recordId = activeDianRow.dataset.recordId || "";
        const accountCode = dianAccount?.value || "";
        if (!recordId || !accountCode) {
            setStatus("Selecciona cuenta gasto antes de guardar.", "info");
            return;
        }

        if (dianSave) {
            dianSave.disabled = true;
        }
        setStatus("Guardando cuenta gasto en Dataverse...", "info");

        try {
            const response = await fetch(dianClassificationUrl, {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify({ recordId, accountCode })
            });
            const payload = await response.json().catch(() => ({}));
            if (!response.ok) {
                throw new Error(payload.detail || payload.message || "No fue posible guardar la cuenta gasto.");
            }

            activeDianRow.dataset.accountCode = accountCode;
            if (isDianAccountBatchActive()) {
                dianAccountBatchDirty = true;
                openNextDianAccountBatchRow(payload.message || "Cuenta gasto guardada.", "success");
                return;
            }

            closeDianEditModal();
            setStatus(payload.message || "Cuenta gasto guardada.", "success");
            window.setTimeout(reloadPreservingView, 650);
        } catch (error) {
            const message = error instanceof Error ? error.message : "Ocurrio un error inesperado.";
            if (isDianAccountBatchActive()) {
                openNextDianAccountBatchRow(message, "error");
                return;
            }

            setStatus(message, "error");
            if (dianSave) {
                dianSave.disabled = false;
            }
        }
    };

    const closeCuentaCobroModal = () => {
        if (cuentaCobroModal) {
            cuentaCobroModal.hidden = true;
        }
        activeCuentaCobroRow = null;
    };

    const openCuentaCobroModal = (row) => {
        activeCuentaCobroRow = row;
        if (cuentaCobroDescription) {
            cuentaCobroDescription.textContent = row.dataset.description || "Cuenta de cobro sin descripcion.";
        }
        if (cuentaCobroAccount) {
            cuentaCobroAccount.value = row.dataset.accountCode || "";
        }
        resetAccountSearch("cncCuentaCobroAccount");
        if (cuentaCobroModal) {
            cuentaCobroModal.hidden = false;
        }
    };

    const saveCuentaCobroClassification = async () => {
        if (!activeCuentaCobroRow || !cuentaCobroClassificationUrl) {
            setStatus("No se encontro la ruta o la cuenta de cobro para guardar.", "error");
            return;
        }

         const recordId = activeCuentaCobroRow.dataset.recordId || "";
         const recordSource = activeCuentaCobroRow.dataset.recordSource || "";
         const concurrencyToken = activeCuentaCobroRow.dataset.concurrencyToken || "";
         const accountCode = cuentaCobroAccount?.value || "";
        if (!recordId || !accountCode) {
            setStatus("Selecciona cuenta gasto antes de guardar.", "info");
            return;
        }

        if (cuentaCobroSave) {
            cuentaCobroSave.disabled = true;
        }
        setStatus("Guardando cuenta contable en Dataverse...", "info");

        try {
            const response = await fetch(cuentaCobroClassificationUrl, {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                 body: JSON.stringify({ recordId, recordSource, concurrencyToken, accountCode })
            });
            const payload = await response.json().catch(() => ({}));
            if (!response.ok) {
                throw new Error(payload.detail || payload.message || "No fue posible guardar la cuenta contable.");
            }

            closeCuentaCobroModal();
            setStatus(payload.message || "Cuenta contable guardada.", "success");
            window.setTimeout(reloadPreservingView, 650);
        } catch (error) {
            setStatus(error instanceof Error ? error.message : "Ocurrio un error inesperado.", "error");
            if (cuentaCobroSave) {
                cuentaCobroSave.disabled = false;
            }
        }
    };

    const accountingVoucherModalIsLineEdit = () =>
        Boolean(activeAccountingVoucherRow?.hasAttribute?.("data-cnc-accounting-voucher-line-edit"));

    const updateAccountingVoucherModalActions = () => {
        const row = activeAccountingVoucherRow;
        const host = accountingVoucherRowHost(row);
        const requiresThirdParty = accountingVoucherRequiresThirdParty(host);
        const lineEdit = accountingVoucherModalIsLineEdit();
        const selectedAccount = String(accountingVoucherAccount?.value || "").trim();
        const savedAccount = String(row?.dataset?.accountCode || "").trim();
        const accountChanged = selectedAccount !== savedAccount;
        const hasThirdParty = !requiresThirdParty || accountingVoucherHasThirdParty(selectedAccountingVoucherThirdParty);
        const thirdPartyChanged = accountingVoucherThirdPartySignature(selectedAccountingVoucherThirdParty)
            !== accountingVoucherThirdPartySignature(persistedAccountingVoucherThirdParty);
        const canSend = Boolean(host?.querySelector?.("[data-cnc-accounting-voucher-send]"));
        if (accountingVoucherThirdPartyField) {
            accountingVoucherThirdPartyField.hidden = lineEdit || !requiresThirdParty;
        }
        if (accountingVoucherSave) {
            accountingVoucherSave.disabled = !row
                || !selectedAccount
                || (!lineEdit && !hasThirdParty);
        }
        if (accountingVoucherSend) {
            accountingVoucherSend.hidden = lineEdit || !canSend;
            accountingVoucherSend.disabled = !canSend
                || !savedAccount
                || accountChanged
                || thirdPartyChanged
                || (requiresThirdParty && !accountingVoucherHasThirdParty(selectedAccountingVoucherThirdParty));
        }
    };

    const renderAccountingVoucherThirdPartyPicker = (message = "") => {
        if (!accountingVoucherThirdPartyResults || !accountingVoucherThirdPartySelected) {
            return;
        }

        accountingVoucherThirdPartySelected.innerHTML = "";
        accountingVoucherThirdPartySelected.hidden = !selectedAccountingVoucherThirdParty;
        if (selectedAccountingVoucherThirdParty) {
            const label = document.createElement("strong");
            const change = document.createElement("button");
            label.textContent = supplierPaymentLabel(selectedAccountingVoucherThirdParty);
            change.type = "button";
            change.className = "btn btn-sm btn-outline-secondary";
            change.textContent = "Cambiar";
            change.addEventListener("click", () => {
                selectedAccountingVoucherThirdParty = null;
                accountingVoucherThirdPartySearchSequence += 1;
                if (accountingVoucherThirdPartyQuery) {
                    accountingVoucherThirdPartyQuery.value = "";
                    accountingVoucherThirdPartyQuery.focus();
                }
                renderAccountingVoucherThirdPartyPicker();
                updateAccountingVoucherModalActions();
            });
            accountingVoucherThirdPartySelected.append(label, change);
        }

        accountingVoucherThirdPartyResults.innerHTML = "";
        accountingVoucherThirdPartyResults.hidden = !message;
        if (message) {
            const detail = document.createElement("small");
            detail.textContent = message;
            accountingVoucherThirdPartyResults.appendChild(detail);
        }
    };

    const selectAccountingVoucherThirdParty = (thirdParty) => {
        selectedAccountingVoucherThirdParty = thirdParty;
        accountingVoucherThirdPartySearchSequence += 1;
        if (accountingVoucherThirdPartyQuery) {
            accountingVoucherThirdPartyQuery.value = supplierPaymentLabel(thirdParty);
        }
        renderAccountingVoucherThirdPartyPicker();
        updateAccountingVoucherModalActions();
    };

    const searchAccountingVoucherThirdParties = async () => {
        const query = String(accountingVoucherThirdPartyQuery?.value || "").trim();
        if (query.length < 2) {
            renderAccountingVoucherThirdPartyPicker("Escribe al menos 2 caracteres para buscar.");
            return;
        }
        if (!siigoSupplierSearchUrl) {
            renderAccountingVoucherThirdPartyPicker("No se encontro la ruta de busqueda de terceros Siigo.");
            return;
        }

        const sequence = accountingVoucherThirdPartySearchSequence + 1;
        accountingVoucherThirdPartySearchSequence = sequence;
        if (accountingVoucherThirdPartySearch) {
            accountingVoucherThirdPartySearch.disabled = true;
        }
        if (accountingVoucherThirdPartyResults) {
            accountingVoucherThirdPartyResults.hidden = false;
            accountingVoucherThirdPartyResults.innerHTML = "<small>Buscando terceros en Siigo...</small>";
        }
        try {
            const response = await fetch(siigoSupplierSearchUrl, {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify({ query, top: 10 })
            });
            const payload = await response.json().catch(() => ({}));
            if (!response.ok) {
                throw new Error(payload.detail || payload.message || "No fue posible buscar terceros en Siigo.");
            }
            if (sequence !== accountingVoucherThirdPartySearchSequence || !activeAccountingVoucherRow) {
                return;
            }

            const items = (payload.items || [])
                .filter((candidate) => candidate?.active !== false && accountingVoucherHasThirdParty(candidate));
            accountingVoucherThirdPartyResults.innerHTML = "";
            accountingVoucherThirdPartyResults.hidden = false;
            if (items.length === 0) {
                accountingVoucherThirdPartyResults.innerHTML = "<small>No se encontraron terceros activos.</small>";
                return;
            }
            items.forEach((candidate) => {
                const button = document.createElement("button");
                const title = document.createElement("strong");
                const detail = document.createElement("small");
                button.type = "button";
                button.className = "cnc-party-picker__option";
                title.textContent = candidate.commercialName || candidate.name || candidate.displayName || "Tercero Siigo";
                detail.textContent = [
                    candidate.identification,
                    Number(candidate.branchOffice || 0) > 0 ? `Sucursal ${candidate.branchOffice}` : ""
                ].filter(Boolean).join(" - ");
                button.append(title, detail);
                button.addEventListener("click", () => selectAccountingVoucherThirdParty(candidate));
                accountingVoucherThirdPartyResults.appendChild(button);
            });
        } catch (error) {
            renderAccountingVoucherThirdPartyPicker(error instanceof Error ? error.message : "No fue posible buscar terceros.");
        } finally {
            if (accountingVoucherThirdPartySearch) {
                accountingVoucherThirdPartySearch.disabled = false;
            }
        }
    };

    const scheduleAccountingVoucherThirdPartySearch = () => {
        window.clearTimeout(accountingVoucherThirdPartySearchTimer);
        const query = String(accountingVoucherThirdPartyQuery?.value || "").trim();
        if (selectedAccountingVoucherThirdParty
            && query !== supplierPaymentLabel(selectedAccountingVoucherThirdParty)) {
            selectedAccountingVoucherThirdParty = null;
            renderAccountingVoucherThirdPartyPicker();
            updateAccountingVoucherModalActions();
        }
        if (query.length < 2) {
            return;
        }
        accountingVoucherThirdPartySearchTimer = window.setTimeout(searchAccountingVoucherThirdParties, 280);
    };

    const closeAccountingVoucherModal = () => {
        window.clearTimeout(accountingVoucherThirdPartySearchTimer);
        accountingVoucherThirdPartySearchSequence += 1;
        if (accountingVoucherModal) {
            accountingVoucherModal.hidden = true;
        }
        activeAccountingVoucherRow = null;
        selectedAccountingVoucherThirdParty = null;
        persistedAccountingVoucherThirdParty = null;
    };

    const openAccountingVoucherModal = (row) => {
        activeAccountingVoucherRow = row;
        accountingVoucherThirdPartySearchSequence += 1;
        persistedAccountingVoucherThirdParty = accountingVoucherThirdPartyFromRow(row);
        selectedAccountingVoucherThirdParty = persistedAccountingVoucherThirdParty;
        if (accountingVoucherDescription) {
            const host = accountingVoucherRowHost(row);
            accountingVoucherDescription.textContent = host?.dataset.description || row.dataset.description || "Comprobante contable sin descripcion.";
        }
        if (accountingVoucherAccount) {
            accountingVoucherAccount.value = row.dataset.accountCode || "";
        }
        resetAccountSearch("cncAccountingVoucherAccount");
        if (accountingVoucherThirdPartyQuery) {
            accountingVoucherThirdPartyQuery.value = selectedAccountingVoucherThirdParty
                ? supplierPaymentLabel(selectedAccountingVoucherThirdParty)
                : "";
        }
        renderAccountingVoucherThirdPartyPicker();
        updateAccountingVoucherModalActions();
        if (accountingVoucherModal) {
            accountingVoucherModal.hidden = false;
        }
    };

    const saveAccountingVoucherAccount = async () => {
        if (!activeAccountingVoucherRow || !cashFlowAccountUrl) {
            setStatus("No se encontro la ruta o el comprobante para guardar.", "error");
            return;
        }

        const row = activeAccountingVoucherRow;
        const accountCode = accountingVoucherAccount?.value || "";
        if (!accountCode) {
            setStatus("Selecciona cuenta contable antes de guardar.", "info");
            return;
        }
        if (!accountingVoucherModalIsLineEdit()
            && accountingVoucherRequiresThirdParty(row)
            && !accountingVoucherHasThirdParty(selectedAccountingVoucherThirdParty)) {
            setStatus("Selecciona el tercero real de Siigo antes de guardar.", "info");
            return;
        }

        if (accountingVoucherSave) {
            accountingVoucherSave.disabled = true;
        }
        setStatus("Guardando cuenta contable del comprobante en Dataverse...", "info");

        try {
            const response = await fetch(cashFlowAccountUrl, {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify({
                    recordId: row.dataset.cashflowRecordId || row.dataset.recordId || "",
                    recordIds: parseDataList(row.dataset.cashflowRecordIds || ""),
                    sourceKind: row.dataset.sourceKind || "Movimiento",
                    movementExternalKey: row.dataset.movementExternalKey || "",
                    movementExternalKeys: parseDataList(row.dataset.movementExternalKeys || ""),
                    accountCode,
                    ...accountingVoucherThirdPartyFields(selectedAccountingVoucherThirdParty)
                })
            });
            const payload = await response.json().catch(() => ({}));
            if (!response.ok) {
                throw new Error(payload.detail || payload.message || "No fue posible guardar la cuenta contable.");
            }

            setAccountingVoucherThirdPartyOnRow(row, selectedAccountingVoucherThirdParty);
            closeAccountingVoucherModal();
            setStatus(payload.message || "Cuenta contable guardada.", "success");
            window.setTimeout(reloadPreservingView, 650);
        } catch (error) {
            setStatus(error instanceof Error ? error.message : "Ocurrio un error inesperado.", "error");
            if (accountingVoucherSave) {
                accountingVoucherSave.disabled = false;
            }
        }
    };

    const sendAccountingVoucherFromModal = async () => {
        const row = activeAccountingVoucherRow;
        if (!row) {
            setStatus("No se encontro el comprobante contable.", "error");
            return;
        }
        const savedAccount = String(row.dataset.accountCode || "").trim();
        const selectedAccount = String(accountingVoucherAccount?.value || "").trim();
        const thirdPartyChanged = accountingVoucherThirdPartySignature(selectedAccountingVoucherThirdParty)
            !== accountingVoucherThirdPartySignature(persistedAccountingVoucherThirdParty);
        if (!savedAccount || selectedAccount !== savedAccount || thirdPartyChanged) {
            setStatus("Guarda la cuenta contable y el tercero antes de enviar.", "info");
            updateAccountingVoucherModalActions();
            return;
        }
        if (accountingVoucherRequiresThirdParty(row) && !accountingVoucherHasThirdParty(selectedAccountingVoucherThirdParty)) {
            setStatus("Selecciona el tercero real de Siigo antes de enviar el comprobante.", "info");
            updateAccountingVoucherModalActions();
            return;
        }

        if (accountingVoucherSend) {
            accountingVoucherSend.disabled = true;
        }
        const result = await runAccountingVoucherAction(accountingVoucherSend, {
            row: accountingVoucherRowHost(row),
            thirdParty: selectedAccountingVoucherThirdParty,
            loadingMessage: "Enviando comprobante contable real a Siigo...",
            successMessage: "Comprobante contable enviado a Siigo.",
            errorMessage: "No fue posible enviar el comprobante contable.",
            confirmMessage: "Esto creara un comprobante contable real en Siigo.",
            reloadOnSuccess: true
        });
        if (result?.success) {
            closeAccountingVoucherModal();
            return;
        }
        updateAccountingVoucherModalActions();
    };

    const extractDigits = (value) => String(value || "").replace(/\D+/g, "");

    const calculateColombianCheckDigit = (value) => {
        const digits = extractDigits(value);
        const weights = [71, 67, 59, 53, 47, 43, 41, 37, 29, 23, 19, 17, 13, 7, 3];
        const offset = Math.max(0, weights.length - digits.length);
        let sum = 0;
        digits.split("").forEach((digit, index) => {
            const weight = weights[index + offset];
            if (Number.isFinite(weight)) {
                sum += Number(digit) * weight;
            }
        });
        const remainder = sum % 11;
        return String(remainder > 1 ? 11 - remainder : remainder);
    };

    const closeDianSupplierModal = () => {
        if (dianSupplierModal) {
            dianSupplierModal.hidden = true;
        }
        activeDianSupplierRow = null;
        dianSupplierRutAnalyzed = false;
        dianSupplierEntryMode = "rut";
        if (dianSupplierRutFile) {
            dianSupplierRutFile.value = "";
        }
    };

    const setDianSupplierTypeDefaults = () => {
        const isCompany = (dianSupplierPersonType?.value || "Company") === "Company";
        if (dianSupplierIdType) {
            dianSupplierIdType.value = isCompany ? "31" : "13";
        }
        if (dianSupplierCheckDigit) {
            dianSupplierCheckDigit.disabled = !isCompany;
            if (isCompany && !dianSupplierCheckDigit.value && dianSupplierNit?.value) {
                dianSupplierCheckDigit.value = calculateColombianCheckDigit(dianSupplierNit.value);
            }
            if (!isCompany) {
                dianSupplierCheckDigit.value = "";
            }
        }
    };

    const syncDianSupplierFiscalFields = () => {};

    const setDianSupplierFeedback = (message = "", tone = "info") => {
        if (!dianSupplierFeedback) {
            return;
        }

        dianSupplierFeedback.textContent = message;
        dianSupplierFeedback.dataset.tone = tone;
        dianSupplierFeedback.hidden = !message;
    };

    const setDianSupplierFieldValidity = (field, isValid) => {
        if (!field) {
            return;
        }

        field.classList.toggle("is-invalid", !isValid);
        if (isValid) {
            field.removeAttribute("aria-invalid");
        } else {
            field.setAttribute("aria-invalid", "true");
        }
    };

    const clearDianSupplierFieldValidation = () => {
        [
            dianSupplierName,
            dianSupplierNit,
            dianSupplierAddress,
            dianSupplierCity
        ].forEach((field) => setDianSupplierFieldValidity(field, true));
    };

    const openDianSupplierModal = (row, options = {}) => {
        activeDianSupplierRow = row;
        const supplierName = row.dataset.supplierName || "";
        const supplierNit = row.dataset.supplierNit || "";
        const personType = row.dataset.supplierPersonType || "Company";
        dianSupplierEntryMode = options.mode === "manual" ? "manual" : "rut";
        const isManualEntry = dianSupplierEntryMode === "manual";
        dianSupplierRutAnalyzed = false;

        if (dianSupplierTitle) {
            dianSupplierTitle.textContent = isManualEntry
                ? "Crear proveedor manualmente"
                : "Crear proveedor desde el RUT";
        }
        if (dianSupplierDescription) {
            dianSupplierDescription.textContent = isManualEntry
                ? `${supplierName || "Proveedor sin nombre"} - ${supplierNit || "sin NIT"}. Completa manualmente los datos fiscales y de ubicación exigidos por Siigo.`
                : `${supplierName || "Proveedor sin nombre"} - ${supplierNit || "sin NIT"}. Adjunta el RUT, revisa la extracción y crea o asocia el proveedor en Siigo.`;
        }
        if (dianSupplierRutSection) {
            dianSupplierRutSection.hidden = isManualEntry;
        }
        if (dianSupplierName) {
            dianSupplierName.value = supplierName;
        }
        if (dianSupplierNit) {
            dianSupplierNit.value = supplierNit;
        }
        if (dianSupplierPersonType) {
            dianSupplierPersonType.value = personType;
        }
        if (dianSupplierIdType) {
            dianSupplierIdType.value = row.dataset.supplierIdType || (personType === "Company" ? "31" : "13");
        }
        if (dianSupplierCheckDigit) {
            dianSupplierCheckDigit.value = row.dataset.supplierCheckDigit || (personType === "Company" ? calculateColombianCheckDigit(supplierNit) : "");
        }
        if (dianSupplierVatResponsible) {
            dianSupplierVatResponsible.value = "false";
        }
        if (dianSupplierFiscalResponsibility) {
            dianSupplierFiscalResponsibility.value = "R-99-PN";
        }
        if (dianSupplierAddress) {
            dianSupplierAddress.value = "";
        }
        if (dianSupplierCity) {
            dianSupplierCity.value = "";
        }
        if (dianSupplierRutFile) {
            dianSupplierRutFile.value = "";
        }
        if (dianSupplierRutStatus) {
            dianSupplierRutStatus.textContent = isManualEntry
                ? ""
                : "Adjunta el RUT para completar automáticamente los datos fiscales.";
        }
        if (dianSupplierSave) {
            dianSupplierSave.textContent = isManualEntry
                ? "Crear/asociar manualmente"
                : "Crear/asociar proveedor";
        }
        clearDianSupplierFieldValidation();
        setDianSupplierFeedback(
            isManualEntry
                ? "Dirección y Ciudad Siigo son obligatorias. Al continuar verás el proceso de creación directa en Siigo."
                : "");
        setDianSupplierTypeDefaults();
        syncDianSupplierFiscalFields();

        if (dianSupplierModal) {
            dianSupplierModal.hidden = false;
        }
    };

    const analyzeDianSupplierRut = async () => {
        if (!activeDianSupplierRow || !dianAnalyzeRutUrl) {
            setStatus("No se encontro la ruta o el proveedor DIAN para analizar el RUT.", "error");
            return;
        }

        const file = dianSupplierRutFile?.files?.[0];
        if (!file) {
            setStatus("Adjunta el RUT del proveedor antes de analizarlo.", "info");
            return;
        }

        const formData = new FormData();
        formData.append("file", file);
        formData.append("recordId", activeDianSupplierRow.dataset.recordId || "");
        if (dianSupplierRutAnalyze) {
            dianSupplierRutAnalyze.disabled = true;
            dianSupplierRutAnalyze.textContent = "Analizando...";
        }
        if (dianSupplierRutStatus) {
            dianSupplierRutStatus.textContent = "La IA está leyendo y validando el RUT contra el NIT de la factura DIAN.";
        }
        dianSupplierRutAnalyzed = false;

        try {
            const response = await fetch(dianAnalyzeRutUrl, {
                method: "POST",
                headers: { "Accept": "application/json" },
                body: formData
            });
            const payload = await response.json().catch(() => ({}));
            if (!response.ok) {
                throw new Error(payload.detail || payload.message || "No fue posible analizar el RUT.");
            }

            if (dianSupplierName) {
                dianSupplierName.value = payload.supplierName || dianSupplierName.value;
            }
            if (dianSupplierNit) {
                dianSupplierNit.value = payload.supplierNit || dianSupplierNit.value;
            }
            if (dianSupplierPersonType) {
                dianSupplierPersonType.value = payload.personType || "Company";
            }
            if (dianSupplierIdType) {
                dianSupplierIdType.value = payload.idType || (payload.personType === "Person" ? "13" : "31");
            }
            if (dianSupplierCheckDigit) {
                dianSupplierCheckDigit.value = payload.checkDigit
                    || (payload.personType === "Person" ? "" : calculateColombianCheckDigit(payload.supplierNit || ""));
            }
            if (dianSupplierVatResponsible) {
                dianSupplierVatResponsible.value = payload.vatResponsible ? "true" : "false";
            }
            if (dianSupplierFiscalResponsibility) {
                const requestedResponsibility = payload.fiscalResponsibilityCode || "R-99-PN";
                dianSupplierFiscalResponsibility.value = Array.from(dianSupplierFiscalResponsibility.options)
                    .some((option) => option.value === requestedResponsibility)
                    ? requestedResponsibility
                    : "R-99-PN";
            }
            if (dianSupplierAddress) {
                dianSupplierAddress.value = payload.address || "";
            }
            if (dianSupplierCity) {
                const cityValue = [payload.countryCode, payload.stateCode, payload.cityCode]
                    .filter(Boolean)
                    .join("|");
                if (payload.cityMappingFound && cityValue.split("|").length === 3) {
                    if (!Array.from(dianSupplierCity.options).some((option) => option.value === cityValue)) {
                        const option = document.createElement("option");
                        option.value = cityValue;
                        option.textContent = payload.cityLabel || payload.city || cityValue;
                        dianSupplierCity.appendChild(option);
                    }
                    dianSupplierCity.value = cityValue;
                } else {
                    dianSupplierCity.value = "";
                }
            }

            setDianSupplierTypeDefaults();
            dianSupplierRutAnalyzed = true;
            const extractedLocation = [payload.city, payload.department].filter(Boolean).join(", ");
            const cityWarning = payload.cityMappingFound
                ? ""
                : ` La ciudad extraída (${extractedLocation || "sin identificar"}) no tiene mapeo automático; selecciónala antes de crear.`;
            if (dianSupplierRutStatus) {
                dianSupplierRutStatus.textContent = `${payload.message || "RUT analizado."}${cityWarning}`;
            }
            setStatus(`${payload.message || "RUT analizado."}${cityWarning}`, payload.cityMappingFound ? "success" : "warning");
        } catch (error) {
            const message = error instanceof Error ? error.message : "Ocurrio un error inesperado.";
            if (dianSupplierRutStatus) {
                dianSupplierRutStatus.textContent = message;
            }
            setStatus(message, "error");
        } finally {
            if (dianSupplierRutAnalyze) {
                dianSupplierRutAnalyze.disabled = false;
                dianSupplierRutAnalyze.textContent = "Extraer datos con IA";
            }
        }
    };

    const saveDianSupplier = async () => {
        if (!activeDianSupplierRow || !dianCreateSupplierUrl) {
            const message = "No se encontró la ruta o el proveedor DIAN.";
            setDianSupplierFeedback(message, "error");
            setStatus(message, "error");
            return;
        }

        const row = activeDianSupplierRow;
        const recordId = row.dataset.recordId || "";
        const supplierName = (dianSupplierName?.value || "").trim();
        const supplierNit = (dianSupplierNit?.value || "").trim();
        const supplierAddress = (dianSupplierAddress?.value || "").trim();
        const cityParts = (dianSupplierCity?.value || "").split("|").filter(Boolean);
        if (dianSupplierEntryMode === "rut" && !dianSupplierRutAnalyzed) {
            const message = "Adjunta y analiza el RUT antes de crear el proveedor en Siigo.";
            setDianSupplierFeedback(message, "error");
            setStatus(message, "info");
            dianSupplierRutFile?.focus();
            return;
        }

        if (!recordId) {
            const message = "No fue posible identificar la factura DIAN. Cierra el popup, recarga la bandeja y vuelve a intentarlo.";
            setDianSupplierFeedback(message, "error");
            setStatus(message, "error");
            return;
        }

        const fieldChecks = [
            {
                field: dianSupplierName,
                label: "Nombre proveedor",
                isValid: Boolean(supplierName)
            },
            {
                field: dianSupplierNit,
                label: "NIT / identificación",
                isValid: extractDigits(supplierNit).length >= 5
            },
            {
                field: dianSupplierAddress,
                label: "Dirección",
                isValid: Boolean(supplierAddress) && normalizeText(supplierAddress) !== "sin direccion"
            },
            {
                field: dianSupplierCity,
                label: "Ciudad Siigo",
                isValid: cityParts.length === 3
            }
        ];
        fieldChecks.forEach(({ field, isValid }) => setDianSupplierFieldValidity(field, isValid));
        const invalidFields = fieldChecks.filter(({ isValid }) => !isValid);
        if (invalidFields.length > 0) {
            const labels = invalidFields.map(({ label }) => label);
            const fieldsLabel = labels.length === 1
                ? labels[0]
                : `${labels.slice(0, -1).join(", ")} y ${labels.at(-1)}`;
            const message = `Falta completar: ${fieldsLabel}. Estos datos son obligatorios para crear el proveedor directamente en Siigo.`;
            setDianSupplierFeedback(message, "error");
            setStatus(message, "info");
            invalidFields[0].field?.focus();
            return;
        }

        const request = {
            recordId,
            year: Number(app.dataset.periodYear || 0),
            month: Number(app.dataset.periodMonth || 0),
            supplierName,
            supplierNit,
            personType: dianSupplierPersonType?.value || "Company",
            idType: dianSupplierIdType?.value || "31",
            checkDigit: dianSupplierCheckDigit?.value || "",
            vatResponsible: (dianSupplierVatResponsible?.value || "false") === "true",
            fiscalResponsibilityCode: dianSupplierFiscalResponsibility?.value || "R-99-PN",
            address: supplierAddress,
            countryCode: cityParts[0],
            stateCode: cityParts[1],
            cityCode: cityParts[2]
        };

        const previousSaveText = dianSupplierSave?.textContent || "Crear/asociar proveedor";
        if (dianSupplierSave) {
            dianSupplierSave.disabled = true;
            dianSupplierSave.textContent = "Creando en Siigo...";
        }
        setDianSupplierFeedback("Creando o asociando el proveedor directamente en Siigo. No cierres esta ventana.", "info");
        const progressModal = ensureBulkProgressModal();
        const progressList = progressModal.querySelector("[data-cnc-bulk-list]");
        const progressReload = progressModal.querySelector("[data-cnc-bulk-reload]");
        const progressClose = progressModal.querySelectorAll("[data-cnc-bulk-close]");
        const progressKicker = progressModal.querySelector("[data-cnc-bulk-kicker]");
        if (progressKicker) {
            progressKicker.textContent = "Proveedor Siigo";
        }
        progressModal._cncOnClose = () => {
            if (dianSupplierModal) {
                dianSupplierModal.hidden = false;
            }
            dianSupplierSave?.focus();
        };
        if (progressList) {
            progressList.innerHTML = "";
        }
        if (progressReload) {
            progressReload.hidden = true;
            progressReload.textContent = "Aceptar y actualizar";
        }
        progressClose.forEach((item) => {
            item.disabled = true;
            item.hidden = false;
        });
        const progressItem = createBulkProgressItem(progressModal, row, 0);
        setBulkProgress(progressModal, "Creando proveedor en Siigo", 0, 1, "Validando datos y consultando Siigo.");
        updateBulkProgressItem(progressItem, "running", "Creando o asociando el proveedor directamente en Siigo...");
        if (dianSupplierModal) {
            dianSupplierModal.hidden = true;
        }
        progressModal.hidden = false;
        progressModal.querySelector("[data-cnc-bulk-panel]")?.focus();
        setDianRowLoading(row, true);
        setStatus("Creando o asociando proveedor en Siigo...", "info");

        try {
            const response = await fetch(dianCreateSupplierUrl, {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify(request)
            });
            const payload = await response.json().catch(() => ({}));
            if (!response.ok) {
                throw new Error(payload.detail || payload.message || "No fue posible crear/asociar el proveedor.");
            }

            renderDianActionPayload(row, payload);
            const issues = Array.isArray(payload.issues) ? payload.issues : [];
            renderIssueList(row, "[data-cnc-dian-issues]", issues);
            const message = row.querySelector("[data-cnc-dian-message]");
            const displayMessage = issues.length
                ? `${payload.message || "Proveedor procesado."} Detalle: ${issues[0]}`
                : (payload.message || "Proveedor Siigo asociado.");
            const succeeded = payload.isSuccess !== false && issues.length === 0;
            if (message) {
                message.textContent = displayMessage;
            }

            updateBulkProgressItem(
                progressItem,
                succeeded ? "success" : "error",
                displayMessage);
            setBulkProgress(
                progressModal,
                succeeded ? "Proveedor listo en Siigo" : "No fue posible completar el proveedor",
                1,
                1,
                displayMessage);
            setStatus(displayMessage, succeeded ? "success" : "info");
            if (succeeded) {
                progressModal._cncOnClose = null;
                progressModal.dataset.cncCloseAction = "reload";
                closeDianSupplierModal();
                progressClose.forEach((item) => {
                    item.disabled = false;
                    item.hidden = false;
                });
                if (progressReload) {
                    progressReload.hidden = false;
                }
            } else {
                setDianSupplierFeedback(displayMessage, "error");
                progressClose.forEach((item) => {
                    item.disabled = false;
                    item.hidden = false;
                });
            }
        } catch (error) {
            const message = error instanceof Error ? error.message : "Ocurrio un error inesperado.";
            updateBulkProgressItem(progressItem, "error", message);
            setBulkProgress(progressModal, "Verificando proveedor", 1, 1, message);
            progressClose.forEach((item) => { item.disabled = false; });
            if (progressReload) {
                progressReload.hidden = true;
            }
            setDianSupplierFeedback(message, "error");
            setStatus(message, "error");
        } finally {
            setDianRowLoading(row, false);
            if (dianSupplierSave) {
                dianSupplierSave.disabled = false;
                dianSupplierSave.textContent = previousSaveText;
            }
        }
    };

    const validateDianSuppliers = async (button) => {
        if (!dianSupplierLookupUrl) {
            setStatus("No se encontro la ruta para validar proveedores DIAN.", "error");
            return;
        }

        const buttons = Array.from(app.querySelectorAll("[data-cnc-dian-supplier-lookup]"));
        buttons.forEach((item) => { item.disabled = true; });
        setStatus("Validando proveedores pendientes contra Siigo...", "info");

        try {
            const response = await fetch(dianSupplierLookupUrl, {
                method: "POST",
                headers: { "Content-Type": "application/json" }
            });
            const payload = await response.json().catch(() => ({}));
            if (!response.ok) {
                throw new Error(payload.detail || payload.message || "No fue posible validar proveedores contra Siigo.");
            }

            const details = [];
            if (Number.isFinite(Number(payload.supplierLookupReviewed))) {
                details.push(`revisados ${Number(payload.supplierLookupReviewed)}`);
            }
            if (Number.isFinite(Number(payload.supplierLookupRowsUpdated))) {
                details.push(`actualizados ${Number(payload.supplierLookupRowsUpdated)}`);
            }
            if (Number.isFinite(Number(payload.supplierLookupMissing))) {
                details.push(`faltantes ${Number(payload.supplierLookupMissing)}`);
            }
            const suffix = details.length ? ` (${details.join(", ")}).` : "";
            setStatus(`${payload.message || "Validacion de proveedores finalizada."}${suffix}`, "success");

            if (Number(payload.supplierLookupRowsUpdated || 0) > 0 || Number(payload.autoClassificationUpdated || 0) > 0) {
                window.setTimeout(reloadPreservingView, 900);
            } else {
                buttons.forEach((item) => { item.disabled = false; });
            }
        } catch (error) {
            setStatus(error instanceof Error ? error.message : "Ocurrio un error inesperado.", "error");
            buttons.forEach((item) => { item.disabled = false; });
        }
    };

    const setDianRowLoading = (row, loading) => {
        row.querySelectorAll("[data-cnc-dian-create-supplier], [data-cnc-dian-create-supplier-manual], [data-cnc-dian-dry-run], [data-cnc-dian-send], [data-cnc-dian-preview-send], [data-cnc-dian-open-edit]")
            .forEach((button) => { button.disabled = loading; });
    };

    const renderDianActionPayload = (row, payload) => {
        const preview = row.querySelector("[data-cnc-dian-preview]");
        const payloadBox = row.querySelector("[data-cnc-dian-payload]");
        const responseBox = row.querySelector("[data-cnc-dian-response]");
        if (payloadBox) {
            payloadBox.textContent = payload.payloadJson || "";
        }
        if (responseBox) {
            responseBox.textContent = payload.responseJson || "";
        }
        if (preview) {
            preview.hidden = !(payload.payloadJson || payload.responseJson);
        }
    };

    const runDianAction = async (button, url, options) => {
        const row = button.closest("tr[data-record-id]");
        const recordId = row?.dataset.recordId || "";
        if (!row || !recordId || !url) {
            setStatus("No se encontro la ruta o el documento DIAN.", "error");
            return { success: false, message: "No se encontro la ruta o el documento DIAN." };
        }

        if (options.confirmMessage && !options.skipConfirm && !window.confirm(options.confirmMessage)) {
            return { success: false, message: "Accion cancelada." };
        }

        setDianRowLoading(row, true);
        setStatus(options.loadingMessage, "info");

        try {
            const response = await fetch(url, {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify({ recordId })
            });
            const payload = await response.json().catch(() => ({}));
            if (!response.ok) {
                throw new Error(payload.detail || payload.message || options.errorMessage);
            }

            renderDianActionPayload(row, payload);
            const issues = Array.isArray(payload.issues) ? payload.issues : [];
            renderIssueList(row, "[data-cnc-dian-issues]", issues);
            const message = row.querySelector("[data-cnc-dian-message]");
            const displayMessage = issues.length
                ? `${payload.message || options.successMessage} Detalle: ${issues[0]}`
                : (payload.message || options.successMessage);
            if (message) {
                message.textContent = displayMessage;
            }
            const hasIssues = issues.length > 0;
            setStatus(displayMessage, hasIssues || payload.isSuccess === false ? "info" : "success");
            if (options.reloadOnSuccess && !options.suppressReload && payload.isSuccess !== false && !hasIssues) {
                window.setTimeout(reloadPreservingView, 800);
            }
            return {
                success: payload.isSuccess !== false && !hasIssues,
                message: displayMessage,
                payload
            };
        } catch (error) {
            const message = error instanceof Error ? error.message : options.errorMessage;
            setStatus(message, "error");
            return { success: false, message };
        } finally {
            setDianRowLoading(row, false);
        }
    };

    const renderCuentaCobroActionPayload = (row, payload) => {
        const preview = row.querySelector("[data-cnc-cuenta-cobro-preview]");
        const payloadBox = row.querySelector("[data-cnc-cuenta-cobro-payload]");
        const responseBox = row.querySelector("[data-cnc-cuenta-cobro-response]");
        if (payloadBox) {
            payloadBox.textContent = payload.payloadJson || "";
        }
        if (responseBox) {
            responseBox.textContent = payload.responseJson || "";
        }
        if (preview) {
            preview.hidden = !(payload.payloadJson || payload.responseJson);
        }
    };

    const setCuentaCobroRowLoading = (row, loading) => {
        row?.querySelectorAll("[data-cnc-cuenta-cobro-preflight], [data-cnc-cuenta-cobro-send], [data-cnc-cuenta-cobro-send-payment], [data-cnc-cuenta-cobro-manual], [data-cnc-cuenta-cobro-preview-send]").forEach((button) => {
            button.disabled = loading;
        });
    };

    const runCuentaCobroAction = async (button, url, options = {}) => {
        const row = button.closest("tr[data-record-id]");
        const recordId = row?.dataset.recordId || "";
        if (!row || !recordId || !url) {
            setStatus("No se encontro la ruta o la cuenta de cobro.", "error");
            return { success: false, message: "No se encontro la ruta o la cuenta de cobro." };
        }

        if (options.confirmMessage && !options.skipConfirm && !window.confirm(options.confirmMessage)) {
            return { success: false, message: "Accion cancelada." };
        }

         const body = {
             recordId,
             recordSource: row.dataset.recordSource || "",
             concurrencyToken: row.dataset.concurrencyToken || "",
             cashFlowRecordId: row.dataset.cashflowRecordId || "",
             cashFlowExternalKey: row.dataset.movementExternalKey || ""
         };
        setCuentaCobroRowLoading(row, true);
        setStatus(options.loadingMessage || "Procesando cuenta de cobro...", "info");

        try {
            const response = await fetch(url, {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify(body)
            });
            const payload = await response.json().catch(() => ({}));
            if (!response.ok) {
                throw new Error(payload.detail || payload.message || options.errorMessage || "No fue posible procesar la cuenta de cobro.");
            }

            renderCuentaCobroActionPayload(row, payload);
            if (payload.row) {
                row.dataset.recordId = payload.row.recordId || row.dataset.recordId || "";
                row.dataset.recordSource = payload.row.recordSource || row.dataset.recordSource || "";
                row.dataset.concurrencyToken = payload.row.concurrencyToken || row.dataset.concurrencyToken || "";
                row.dataset.cashflowRecordId = payload.row.cashFlowRecordId || row.dataset.cashflowRecordId || "";
                row.dataset.movementExternalKey = payload.row.cashFlowExternalKey || row.dataset.movementExternalKey || "";
            }
             const issues = Array.isArray(payload.issues) ? payload.issues : [];
            renderIssueList(row, "[data-cnc-cuenta-cobro-issues]", issues);
            const message = row.querySelector("[data-cnc-cuenta-cobro-message]");
            const displayMessage = issues.length
                ? `${payload.message || options.successMessage || "Accion finalizada."} Detalle: ${issues[0]}`
                : (payload.message || options.successMessage || "Accion finalizada.");
            if (message) {
                message.textContent = displayMessage;
            }
            const success = payload.isSuccess !== false && !issues.length;
            setStatus(displayMessage, success ? "success" : "info");
            if (options.reloadOnSuccess && !options.suppressReload && success) {
                window.setTimeout(reloadPreservingView, 800);
            }
            return { success, message: displayMessage, payload };
        } catch (error) {
            const message = error instanceof Error ? error.message : options.errorMessage || "Ocurrio un error inesperado.";
            setStatus(message, "error");
            return { success: false, message };
        } finally {
            setCuentaCobroRowLoading(row, false);
        }
    };

    const markCuentaCobroManualSiigo = (button, options = {}) => runCuentaCobroAction(button, cuentaCobroManualUrl, {
        loadingMessage: "Marcando cuenta de cobro como Conciliada manualmente...",
        successMessage: "Cuenta de cobro conciliada manualmente.",
        errorMessage: "No fue posible marcar la cuenta de cobro como manual.",
        confirmMessage: "Esto movera las cuentas seleccionadas a conciliadas sin enviar payload a Siigo. Continuar?",
        reloadOnSuccess: true,
        ...options
    });

    const sendCuentaCobroToSiigo = (button, options = {}) => runCuentaCobroAction(button, cuentaCobroSendUrl, {
        loadingMessage: "Enviando documento soporte y pago real a Siigo...",
        successMessage: "Cuenta de cobro enviada a Siigo.",
        errorMessage: "No fue posible enviar la cuenta de cobro.",
        confirmMessage: "Esto creara un documento soporte real y luego el comprobante de egreso en Siigo.",
        reloadOnSuccess: true,
        ...options
    });

    const sendCuentaCobroPaymentToSiigo = (button, options = {}) => runCuentaCobroAction(button, cuentaCobroPaymentUrl, {
        loadingMessage: "Enviando pago del documento soporte a Siigo...",
        successMessage: "Pago enviado a Siigo.",
        errorMessage: "No fue posible enviar el pago.",
        confirmMessage: "Esto creara el comprobante de egreso real en Siigo.",
        reloadOnSuccess: true,
        ...options
    });

    const renderAccountingVoucherPayload = (row, payload) => {
        const preview = row.querySelector("[data-cnc-accounting-voucher-preview]");
        const payloadBox = row.querySelector("[data-cnc-accounting-voucher-payload]");
        const responseBox = row.querySelector("[data-cnc-accounting-voucher-response]");
        if (payloadBox) {
            payloadBox.textContent = payload.payloadJson || "";
        }
        if (responseBox) {
            responseBox.textContent = payload.responseJson || "";
        }
        if (preview) {
            preview.hidden = !(payload.payloadJson || payload.responseJson);
        }
    };

    const setAccountingVoucherRowLoading = (row, loading) => {
        row?.querySelectorAll("[data-cnc-accounting-voucher-send]").forEach((button) => {
            button.disabled = loading;
        });
    };

    const runAccountingVoucherAction = async (button, options = {}) => {
        const row = options.row || button?.closest?.("tr[data-record-id]");
        const recordId = row?.dataset.cashflowRecordId || row?.dataset.recordId || "";
        const recordIds = parseDataList(row?.dataset.cashflowRecordIds || "");
        const movementExternalKeys = parseDataList(row?.dataset.movementExternalKeys || "");
        if (!row || (!recordId && !recordIds.length && !movementExternalKeys.length) || !accountingVoucherSendUrl) {
            setStatus("No se encontro la ruta o el comprobante contable.", "error");
            return { success: false, message: "No se encontro la ruta o el comprobante contable." };
        }

        const thirdParty = options.thirdParty || accountingVoucherThirdPartyFromRow(row);
        if (accountingVoucherRequiresThirdParty(row) && !accountingVoucherHasThirdParty(thirdParty)) {
            const message = "Selecciona el tercero real de Siigo antes de enviar el comprobante.";
            setStatus(message, "info");
            return { success: false, message };
        }

        if (options.confirmMessage && !options.skipConfirm && !window.confirm(options.confirmMessage)) {
            return { success: false, message: "Accion cancelada." };
        }

        setAccountingVoucherRowLoading(row, true);
        setStatus(options.loadingMessage || "Enviando comprobante contable a Siigo...", "info");

        try {
            const response = await fetch(accountingVoucherSendUrl, {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify({
                    recordId,
                    recordIds,
                    sourceKind: row.dataset.sourceKind || "Movimiento",
                    movementExternalKey: row.dataset.movementExternalKey || "",
                    movementExternalKeys,
                    groupKey: row.dataset.accountingVoucherGroupKey || "",
                    groupLabel: row.dataset.accountingVoucherGroupLabel || row.dataset.description || "",
                    ...accountingVoucherThirdPartyFields(thirdParty)
                })
            });
            const payload = await response.json().catch(() => ({}));
            if (!response.ok) {
                throw new Error(payload.detail || payload.message || options.errorMessage || "No fue posible enviar el comprobante.");
            }

            renderAccountingVoucherPayload(row, payload);
            const issues = Array.isArray(payload.issues) ? payload.issues : [];
            renderIssueList(row, "[data-cnc-accounting-voucher-issues]", issues);
            const success = payload.isSuccess !== false && !issues.length;
            const displayMessage = issues.length
                ? `${payload.message || options.successMessage || "Accion finalizada."} Detalle: ${issues[0]}`
                : (payload.message || options.successMessage || "Accion finalizada.");
            setStatus(displayMessage, success ? "success" : "info");
            if (options.reloadOnSuccess && !options.suppressReload && success) {
                window.setTimeout(reloadPreservingView, 800);
            }
            return { success, message: displayMessage, payload };
        } catch (error) {
            const message = error instanceof Error ? error.message : options.errorMessage || "Ocurrio un error inesperado.";
            setStatus(message, "error");
            return { success: false, message };
        } finally {
            setAccountingVoucherRowLoading(row, false);
        }
    };

    const ensureSiigoPreviewModal = () => {
        let modal = document.getElementById("cncSiigoPreviewModal");
        if (modal) {
            return modal;
        }

        modal = document.createElement("div");
        modal.className = "cnc-modal";
        modal.id = "cncSiigoPreviewModal";
        modal.setAttribute("role", "dialog");
        modal.setAttribute("aria-modal", "true");
        modal.hidden = true;
        modal.innerHTML = `
            <div class="cnc-modal__panel cnc-modal__panel--wide cnc-modal__panel--preview">
                <div class="cnc-modal__header">
                    <div>
                        <div class="cnc-kicker" data-cnc-siigo-preview-kicker>Siigo</div>
                        <h2 data-cnc-siigo-preview-title>Simulacion</h2>
                    </div>
                    <button type="button" class="btn btn-sm btn-outline-secondary" data-cnc-siigo-preview-close>Cerrar</button>
                </div>
                <p class="cnc-modal__description" data-cnc-siigo-preview-message></p>
                <div class="cnc-siigo-sheet" data-cnc-siigo-preview-sheet></div>
                <ul class="cnc-issue-list" data-cnc-siigo-preview-issues hidden></ul>
                <details class="cnc-json-preview" data-cnc-siigo-preview-json-wrap hidden>
                    <summary>Payload que se enviara</summary>
                    <pre data-cnc-siigo-preview-json></pre>
                </details>
                <div class="cnc-modal__actions">
                    <button type="button" class="btn btn-outline-secondary" data-cnc-siigo-preview-close>Cancelar</button>
                    <button type="button" class="btn btn-danger" data-cnc-siigo-preview-send disabled>Enviar a Siigo</button>
                </div>
            </div>`;
        document.body.appendChild(modal);
        modal.querySelectorAll("[data-cnc-siigo-preview-close]").forEach((button) => {
            button.addEventListener("click", () => {
                modal.hidden = true;
            });
        });
        modal.addEventListener("click", (event) => {
            if (event.target === modal) {
                modal.hidden = true;
            }
        });
        modal.querySelector("[data-cnc-siigo-preview-send]")?.addEventListener("click", async () => {
            if (typeof modal._cncSend === "function") {
                await modal._cncSend();
            }
        });
        return modal;
    };

    const renderSiigoSheet = (modal, payload, options = {}) => {
        const sheet = modal.querySelector("[data-cnc-siigo-preview-sheet]");
        if (!sheet) {
            return;
        }

        const root = getPreviewPayloadRoot(payload);
        const items = getPreviewItems(payload);
        const total = items.reduce((sum, item) => sum + resolvePreviewItemValue(item), 0);
        const documentId = root.document?.id || root.document?.code || root.document?.name || "Documento";
        const thirdParty = root.supplier?.identification
            || root.customer?.identification
            || root.movement?.description
            || options.thirdParty
            || "Tercero";
        const rows = items.length
            ? items.map((item, index) => `
                <tr>
                    <td>${index + 1}</td>
                    <td>${escapeHtml(resolvePreviewAccount(item) || "Sin cuenta")}</td>
                    <td>${escapeHtml(item.description || item.detail || "Sin descripcion")}</td>
                    <td class="text-end">${money(resolvePreviewItemValue(item))}</td>
                </tr>`).join("")
            : `<tr><td colspan="4">Sin lineas para mostrar.</td></tr>`;

        sheet.innerHTML = `
            <div class="cnc-siigo-sheet__mast">
                <span>${escapeHtml(options.kind || "Simulacion")}</span>
                <strong>${escapeHtml(options.documentLabel || String(documentId))}</strong>
            </div>
            <div class="cnc-siigo-sheet__meta">
                <div><span>Fecha</span><strong>${escapeHtml(root.date || payload?.date || "Sin fecha")}</strong></div>
                <div><span>Tercero</span><strong>${escapeHtml(String(thirdParty))}</strong></div>
                <div><span>Endpoint</span><strong>${escapeHtml(options.endpoint || payload?.targetEndpoint || "Siigo")}</strong></div>
            </div>
            <table class="table align-middle cnc-table cnc-siigo-sheet__table">
                <thead>
                    <tr>
                        <th>#</th>
                        <th>Cuenta / item</th>
                        <th>Descripcion</th>
                        <th class="text-end">Valor</th>
                    </tr>
                </thead>
                <tbody>${rows}</tbody>
                <tfoot>
                    <tr>
                        <td colspan="3">Total simulado</td>
                        <td class="text-end">${money(total)}</td>
                    </tr>
                </tfoot>
            </table>`;
    };

    function escapeHtml(value) {
        return String(value || "")
            .replaceAll("&", "&amp;")
            .replaceAll("<", "&lt;")
            .replaceAll(">", "&gt;")
            .replaceAll('"', "&quot;")
            .replaceAll("'", "&#39;");
    }

    const openSiigoPreviewModal = async (button, options) => {
        const modal = ensureSiigoPreviewModal();
        const title = modal.querySelector("[data-cnc-siigo-preview-title]");
        const kicker = modal.querySelector("[data-cnc-siigo-preview-kicker]");
        const message = modal.querySelector("[data-cnc-siigo-preview-message]");
        const send = modal.querySelector("[data-cnc-siigo-preview-send]");
        const jsonWrap = modal.querySelector("[data-cnc-siigo-preview-json-wrap]");
        const jsonBox = modal.querySelector("[data-cnc-siigo-preview-json]");
        const issueList = modal.querySelector("[data-cnc-siigo-preview-issues]");

        if (title) title.textContent = options.title || "Simulacion Siigo";
        if (kicker) kicker.textContent = options.kicker || "Siigo";
        if (message) message.textContent = options.loadingMessage || "Preparando simulacion...";
        if (send) {
            send.disabled = true;
            send.textContent = options.sendLabel || "Enviar a Siigo";
        }
        if (issueList) {
            issueList.innerHTML = "";
            issueList.hidden = true;
        }
        if (jsonWrap) jsonWrap.hidden = true;
        modal._cncSend = null;
        renderSiigoSheet(modal, {}, options);
        modal.hidden = false;

        const result = await options.preview(button);
        const payload = result?.payload || {};
        const issues = Array.isArray(payload.issues) ? payload.issues : [];
        const parsedPayload = parseJson(payload.payloadJson || "") || payload.payload || {};
        renderSiigoSheet(modal, parsedPayload, {
            ...options,
            endpoint: payload.targetEndpoint || options.endpoint || ""
        });
        if (message) {
            message.textContent = issues.length
                ? `${payload.message || result?.message || "Simulacion finalizada."} Revisa los pendientes antes de enviar.`
                : (payload.message || result?.message || "Simulacion lista para enviar.");
        }
        if (issueList) {
            renderIssueList(modal, "[data-cnc-siigo-preview-issues]", issues);
        }
        if (jsonBox) {
            renderJsonInto(jsonBox, payload.payloadJson || parsedPayload);
        }
        if (jsonWrap) {
            jsonWrap.hidden = !(payload.payloadJson || parsedPayload);
        }

        const ready = Boolean(payload.isReadyForSiigo || payload.canSend || result?.success) && issues.length === 0;
        if (send) {
            send.disabled = !ready;
        }
        modal._cncSend = async () => {
            if (send) {
                send.disabled = true;
                send.textContent = options.sendingLabel || "Enviando...";
            }
            const sendResult = await options.send(button);
            if (sendResult?.success) {
                modal.hidden = true;
            } else if (send) {
                send.disabled = false;
                send.textContent = options.sendLabel || "Enviar a Siigo";
            }
        };
    };

    const delay = (milliseconds) => new Promise((resolve) => window.setTimeout(resolve, milliseconds));

    const resolveBulkRowLabel = (row) => {
        const strong = row.querySelector("td strong")?.textContent?.trim();
        const recordId = row.dataset.recordId || "";
        return strong || recordId || "Registro";
    };

    const resetBulkProgressModalChrome = (modal) => {
        delete modal.dataset.cncCloseAction;
        modal._cncOnClose = null;
        const reload = modal.querySelector("[data-cnc-bulk-reload]");
        const kicker = modal.querySelector("[data-cnc-bulk-kicker]");
        if (reload) {
            reload.textContent = "Recargar vista";
        }
        if (kicker) {
            kicker.textContent = "Accion masiva";
        }
    };

    const ensureBulkProgressModal = () => {
        let modal = document.getElementById("cncBulkProgressModal");
        if (modal) {
            resetBulkProgressModalChrome(modal);
            return modal;
        }

        modal = document.createElement("div");
        modal.className = "cnc-modal";
        modal.id = "cncBulkProgressModal";
        modal.setAttribute("role", "dialog");
        modal.setAttribute("aria-modal", "true");
        modal.setAttribute("aria-labelledby", "cncBulkProgressTitle");
        modal.setAttribute("aria-describedby", "cncBulkProgressSummary");
        modal.hidden = true;
        modal.innerHTML = `
            <div class="cnc-modal__panel cnc-modal__panel--wide" data-cnc-bulk-panel tabindex="-1">
                <div class="cnc-modal__header">
                    <div>
                        <div class="cnc-kicker" data-cnc-bulk-kicker>Accion masiva</div>
                        <h2 id="cncBulkProgressTitle" data-cnc-bulk-title>Procesando registros</h2>
                    </div>
                    <button type="button" class="btn btn-sm btn-outline-secondary" data-cnc-bulk-close disabled>Cerrar</button>
                </div>
                <p class="cnc-modal__description" id="cncBulkProgressSummary" data-cnc-bulk-summary></p>
                <div class="cnc-progress" aria-hidden="true">
                    <div class="cnc-progress__bar" data-cnc-bulk-bar></div>
                </div>
                <div class="cnc-bulk-progress-list" data-cnc-bulk-list></div>
                <div class="cnc-modal__actions">
                    <button type="button" class="btn btn-outline-secondary" data-cnc-bulk-close disabled>Cerrar</button>
                    <button type="button" class="btn btn-primary" data-cnc-bulk-reload hidden>Recargar vista</button>
                </div>
            </div>`;
        document.body.appendChild(modal);
        modal.querySelectorAll("[data-cnc-bulk-close]").forEach((button) => {
            button.addEventListener("click", () => {
                if (modal.dataset.cncCloseAction === "reload") {
                    reloadPreservingView();
                    return;
                }

                const onClose = modal._cncOnClose;
                modal.hidden = true;
                resetBulkProgressModalChrome(modal);
                if (typeof onClose === "function") {
                    onClose();
                }
            });
        });
        modal.querySelector("[data-cnc-bulk-reload]")?.addEventListener("click", reloadPreservingView);
        return modal;
    };

    const setBulkProgress = (modal, title, completed, total, summary) => {
        const titleBox = modal.querySelector("[data-cnc-bulk-title]");
        const summaryBox = modal.querySelector("[data-cnc-bulk-summary]");
        const bar = modal.querySelector("[data-cnc-bulk-bar]");
        if (titleBox) {
            titleBox.textContent = title;
        }
        if (summaryBox) {
            summaryBox.textContent = summary || `${completed} de ${total} registros procesados.`;
        }
        if (bar) {
            bar.style.width = total ? `${Math.round(completed / total * 100)}%` : "0%";
        }
    };

    const createBulkProgressItem = (modal, row, index) => {
        const list = modal.querySelector("[data-cnc-bulk-list]");
        const item = document.createElement("div");
        item.className = "cnc-bulk-progress-item";
        item.innerHTML = `
            <span class="cnc-bulk-progress-item__index">${index + 1}</span>
            <strong></strong>
            <small>En espera</small>`;
        item.querySelector("strong").textContent = resolveBulkRowLabel(row);
        list?.appendChild(item);
        return item;
    };

    const updateBulkProgressItem = (item, state, message) => {
        item.dataset.state = state;
        const status = item.querySelector("small");
        if (status) {
            status.textContent = message || state;
        }
    };

    const getBulkSection = (element) =>
        element.closest(".cnc-pipeline-stage")
        || element.closest(".cnc-payment-panel")
        || element.closest(".cnc-table-wrap");

    const getBulkRows = (section) =>
        Array.from(section?.querySelectorAll("tr[data-record-id]") || [])
            .filter((row) => row.querySelector("[data-cnc-bulk-check]"));

    const getVisibleBulkRows = (section) =>
        getBulkRows(section).filter((row) => !row.hidden && !row.closest("[hidden]"));

    const getSelectedBulkRows = (section) =>
        getVisibleBulkRows(section).filter((row) => row.querySelector("[data-cnc-bulk-check]")?.checked);

    const bulkActions = [
        {
            key: "change-category",
            selector: "[data-cnc-bulk-category]",
            label: "Cambiar de categoria",
            title: "Cambiando categoria",
            runBulk: openBulkCategoryModal
        },
        {
            key: "manual-cashflow",
            selector: "[data-cnc-cashflow-manual]",
            label: "Conciliado manualmente",
            title: "Marcando flujo de caja manual",
            confirm: (count) => `Se marcaran ${count} movimientos como subidos manualmente y conciliados. Continuar?`,
            run: (button) => markCashFlowManualSiigo(button, {
                skipConfirm: true,
                suppressReload: true,
                reason: "Movimiento Conciliado manualmente a Siigo desde accion masiva de flujo de caja. No se envio payload desde la app."
            }),
            delayMs: 250,
            reloadOnComplete: true
        },
        {
            key: "approve",
            selector: '[data-cnc-action="Aprobado"]',
            label: "Aprobar seleccionadas",
            title: "Aprobando registros",
            confirm: (count) => `Se aprobaran ${count} registros y pasaran al siguiente estado. Continuar?`,
            run: (button) => updatePaymentStatus(button, {
                reason: "Aprobado desde accion masiva en Conciliacion.",
                suppressReload: true
            }),
            delayMs: 250
        },
        {
            key: "preflight",
            selector: "[data-cnc-preflight]",
            label: "Validar pre-Siigo",
            title: "Validando pre-Siigo",
            run: (button) => validatePaymentPreflight(button, { suppressReload: true }),
            delayMs: 250
        },
        {
            key: "manual-siigo",
            selector: "[data-cnc-manual-siigo]",
            label: "Conciliadas manualmente",
            title: "Marcando registros manuales en Siigo",
            confirm: (count) => `Se marcaran ${count} registros como subidos manualmente y conciliados. Continuar?`,
            run: (button) => markPaymentManualSiigo(button, {
                skipConfirm: true,
                suppressReload: true,
                reason: "Conciliada manualmente a Siigo desde accion masiva en Conciliacion. No se envio payload desde la app."
            }),
            delayMs: 250
        },
        {
            key: "send-payment",
            selector: "[data-cnc-send-siigo]:not(:disabled)",
            label: "Enviar comprobantes",
            title: "Enviando comprobantes a Siigo",
            confirm: (count) => `Se enviaran ${count} comprobantes reales a Siigo, uno por uno. Continuar?`,
            run: (button) => sendPaymentToSiigo(button, {
                skipConfirm: true,
                suppressReload: true
            }),
            delayMs: 1200
        },
        {
            key: "create-supplier",
            selector: "[data-cnc-dian-create-supplier]",
            label: "Crear/asociar proveedores",
            title: "Creando/asociando proveedores",
            confirm: (count) => `Se crearan o asociaran ${count} proveedores en Siigo, uno por uno. Continuar?`,
            run: (button) => runDianAction(button, dianCreateSupplierUrl, {
                loadingMessage: "Creando o asociando proveedor en Siigo...",
                successMessage: "Proveedor Siigo asociado.",
                errorMessage: "No fue posible crear/asociar el proveedor.",
                reloadOnSuccess: true,
                suppressReload: true
            }),
            delayMs: 1200,
            reloadOnComplete: true
        },
        {
            key: "adjust-dian-account",
            selector: "[data-cnc-dian-adjust-account]",
            label: "Ajustar cuenta contable",
            title: "Ajustando cuenta contable",
            runBulk: openBulkDianAccountModal
        },
        {
            key: "dry-run-dian",
            selector: "[data-cnc-dian-dry-run]",
            label: "Simular facturas",
            title: "Simulando facturas de compra",
            run: (button) => runDianAction(button, dianDryRunUrl, {
                loadingMessage: "Simulando factura de compra Siigo...",
                successMessage: "Simulacion finalizada.",
                errorMessage: "No fue posible simular la factura.",
                reloadOnSuccess: false
            }),
            delayMs: 500
        },
        {
            key: "send-dian",
            selector: "[data-cnc-dian-send]",
            label: "Enviar facturas",
            title: "Enviando facturas de compra a Siigo",
            confirm: (count) => `Se enviaran ${count} facturas de compra reales a Siigo, una por una. Continuar?`,
            run: (button) => runDianAction(button, dianSendUrl, {
                loadingMessage: "Enviando factura de compra real a Siigo...",
                successMessage: "Factura enviada a Siigo.",
                errorMessage: "No fue posible enviar la factura a Siigo.",
                confirmMessage: "Esto creara una factura de compra real en Siigo.",
                skipConfirm: true,
                reloadOnSuccess: true,
                suppressReload: true
            }),
            delayMs: 1200,
            reloadOnComplete: true
        },
        {
            key: "preflight-cuenta-cobro",
            selector: "[data-cnc-cuenta-cobro-preflight]",
            label: "Validar soportes",
            title: "Validando documentos soporte",
            run: (button) => runCuentaCobroAction(button, cuentaCobroPreflightUrl, {
                loadingMessage: "Validando documento soporte pre-Siigo...",
                successMessage: "Prevalidacion finalizada.",
                errorMessage: "No fue posible validar el documento soporte.",
                reloadOnSuccess: true,
                suppressReload: true
            }),
            delayMs: 500,
            reloadOnComplete: true
        },
        {
            key: "manual-cuenta-cobro",
            selector: "[data-cnc-cuenta-cobro-manual]",
            label: "Conciliadas manualmente",
            title: "Marcando cuentas de cobro manuales",
            confirm: (count) => `Se marcaran ${count} cuentas de cobro como Conciliadas manualmente y conciliadas. Continuar?`,
            run: (button) => markCuentaCobroManualSiigo(button, {
                skipConfirm: true,
                suppressReload: true
            }),
            delayMs: 250,
            reloadOnComplete: true
        },
        {
            key: "send-cuenta-cobro",
            selector: "[data-cnc-cuenta-cobro-send]",
            label: "Enviar a Siigo",
            title: "Enviando documentos soporte y pagos a Siigo",
            confirm: (count) => `Se enviaran ${count} cuentas de cobro reales a Siigo, una por una. Cada una crea primero documento soporte y luego pago. Continuar?`,
            run: (button) => sendCuentaCobroToSiigo(button, {
                skipConfirm: true,
                reloadOnSuccess: true,
                suppressReload: true
            }),
            delayMs: 1200,
            reloadOnComplete: true
        },
        {
            key: "send-cuenta-cobro-payment",
            selector: "[data-cnc-cuenta-cobro-send-payment]",
            label: "Reintentar pago",
            title: "Reintentando pagos de documentos soporte",
            confirm: (count) => `Se reintentaran ${count} pagos de documentos soporte en Siigo, uno por uno. Continuar?`,
            run: (button) => sendCuentaCobroPaymentToSiigo(button, {
                skipConfirm: true,
                reloadOnSuccess: true,
                suppressReload: true
            }),
            delayMs: 1200,
            reloadOnComplete: true
        },
        {
            key: "search-supplier-payment",
            selector: "[data-cnc-supplier-payment-search]",
            label: "Buscar facturas",
            title: "Buscando facturas proveedor",
            runBulk: (_section, rows) => openBulkSupplierPaymentSearchModal(rows)
        },
        {
            key: "manual-supplier-payment",
            selector: "[data-cnc-supplier-payment-manual]",
            label: "Conciliada manualmente",
            title: "Marcando salidas proveedor manuales",
            confirm: (count) => `Se marcaran ${count} salidas FC como Conciliadas manualmente y conciliadas. Continuar?`,
            run: (button) => markSupplierPaymentManualSiigo(button, {
                skipConfirm: true,
                suppressReload: true
            }),
            delayMs: 250,
            reloadOnComplete: true
        },
        {
            key: "send-supplier-payment",
            selector: "[data-cnc-supplier-payment-send]",
            label: "Aplicar pago",
            title: "Aplicando pagos proveedor",
            confirm: (count) => `Se aplicaran ${count} pagos proveedor reales en Siigo, uno por uno. Continuar?`,
            run: (button) => runSupplierPaymentAction(button, {
                loadingMessage: "Aplicando pago proveedor en Siigo...",
                successMessage: "Pago proveedor aplicado en Siigo.",
                errorMessage: "No fue posible aplicar el pago proveedor.",
                skipConfirm: true,
                reloadOnSuccess: true,
                suppressReload: true
            }),
            delayMs: 1200,
            reloadOnComplete: true
        },
        {
            key: "send-accounting-voucher",
            selector: "[data-cnc-accounting-voucher-send]",
            label: "Enviar a Siigo",
            title: "Enviando comprobantes contables a Siigo",
            confirm: (count) => `Se enviaran ${count} comprobantes contables reales a Siigo, uno por uno. Continuar?`,
            run: (button) => runAccountingVoucherAction(button, {
                loadingMessage: "Enviando comprobante contable real a Siigo...",
                successMessage: "Comprobante contable enviado a Siigo.",
                errorMessage: "No fue posible enviar el comprobante contable.",
                confirmMessage: "Esto creara un comprobante contable real en Siigo.",
                skipConfirm: true,
                reloadOnSuccess: true,
                suppressReload: true
            }),
            delayMs: 1200,
            reloadOnComplete: true
        }
    ];

    const getAvailableBulkActions = (section) =>
        bulkActions.filter((action) => getBulkRows(section).some((row) => row.querySelector(action.selector)));

    const refreshBulkSection = (section) => {
        const rows = getVisibleBulkRows(section);
        const selectedRows = getSelectedBulkRows(section);
        const selectAll = section.querySelector("[data-cnc-bulk-select-all]");
        if (selectAll) {
            selectAll.checked = rows.length > 0 && selectedRows.length === rows.length;
            selectAll.indeterminate = selectedRows.length > 0 && selectedRows.length < rows.length;
            selectAll.disabled = rows.length === 0;
        }

        const count = section.querySelector("[data-cnc-bulk-selected-count]");
        if (count) {
            count.textContent = selectedRows.length
                ? `${selectedRows.length} seleccionada${selectedRows.length === 1 ? "" : "s"}`
                : "Sin seleccion";
        }

        section.querySelectorAll("[data-cnc-bulk-action]").forEach((button) => {
            const action = bulkActions.find((item) => item.key === button.dataset.cncBulkAction);
            const actionable = action
                ? selectedRows.filter((row) => row.querySelector(action.selector)).length
                : 0;
            button.disabled = actionable === 0;
            button.textContent = actionable > 0
                ? `${button.dataset.baseLabel} (${actionable})`
                : button.dataset.baseLabel;
        });
    };

    const refreshBulkSections = () => {
        app.querySelectorAll("[data-cnc-bulk-section]").forEach(refreshBulkSection);
    };

    const runBulkAction = async (section, action) => {
        const rows = getSelectedBulkRows(section).filter((row) => row.querySelector(action.selector));
        if (rows.length === 0) {
            setStatus("Selecciona filas visibles con esa accion disponible.", "info");
            return;
        }

        if (action.key === "send-accounting-voucher") {
            const missingThirdParty = rows.filter((row) =>
                accountingVoucherRequiresThirdParty(row)
                && !accountingVoucherThirdPartyFromRow(row));
            if (missingThirdParty.length > 0) {
                if (missingThirdParty.length === 1 && rows.length === 1) {
                    openAccountingVoucherModal(missingThirdParty[0]);
                }
                setStatus(
                    missingThirdParty.length === 1
                        ? "Selecciona el tercero real de Siigo antes del envio."
                        : `${missingThirdParty.length} comprobantes no tienen tercero. Asigna uno por grupo antes del envio masivo.`,
                    "info");
                return;
            }
        }

        if (action.confirm && !window.confirm(action.confirm(rows.length))) {
            return;
        }

        if (typeof action.runBulk === "function") {
            action.runBulk(section, rows);
            return;
        }

        const modal = ensureBulkProgressModal();
        const list = modal.querySelector("[data-cnc-bulk-list]");
        const reload = modal.querySelector("[data-cnc-bulk-reload]");
        const closeButtons = modal.querySelectorAll("[data-cnc-bulk-close]");
        if (list) {
            list.innerHTML = "";
        }
        if (reload) {
            reload.hidden = true;
        }
        closeButtons.forEach((button) => { button.disabled = true; });
        modal.hidden = false;

        const items = rows.map((row, index) => createBulkProgressItem(modal, row, index));
        let ok = 0;
        let failed = 0;
        setBulkProgress(modal, action.title, 0, rows.length, `0 de ${rows.length} registros procesados.`);
        setStatus(`${action.title}: 0 de ${rows.length}.`, "info");

        for (let index = 0; index < rows.length; index += 1) {
            const row = rows[index];
            const button = row.querySelector(action.selector);
            const item = items[index];
            updateBulkProgressItem(item, "running", "Procesando...");
            let result = { success: false, message: "No se encontro accion disponible." };
            try {
                result = button
                    ? await action.run(button)
                    : result;
            } catch (error) {
                result = {
                    success: false,
                    message: error instanceof Error ? error.message : "Ocurrio un error inesperado."
                };
            }

            if (result?.success) {
                ok += 1;
                updateBulkProgressItem(item, "success", result.message || "Completado.");
            } else {
                failed += 1;
                updateBulkProgressItem(item, "error", result?.message || "No se pudo completar.");
            }

            const completed = index + 1;
            setBulkProgress(
                modal,
                action.title,
                completed,
                rows.length,
                `${completed} de ${rows.length} procesados. OK: ${ok}. Error: ${failed}.`);
            setStatus(`${action.title}: ${completed} de ${rows.length}.`, failed ? "info" : "success");
            if (index < rows.length - 1 && action.delayMs) {
                await delay(action.delayMs);
            }
        }

        closeButtons.forEach((button) => { button.disabled = false; });
        if (reload) {
            reload.hidden = false;
        }
        rows.forEach((row) => {
            const checkbox = row.querySelector("[data-cnc-bulk-check]");
            if (checkbox) {
                checkbox.checked = false;
            }
        });
        refreshBulkSection(section);
        setStatus(`Accion masiva finalizada. OK: ${ok}. Error: ${failed}.`, failed ? "info" : "success");
        if (ok > 0 && action.reloadOnComplete) {
            setStatus(`Accion masiva finalizada. OK: ${ok}. Error: ${failed}. Recargando vista...`, failed ? "info" : "success");
            window.setTimeout(reloadPreservingView, 900);
        }
    };

    const initializeBulkTables = () => {
        app.querySelectorAll(".cnc-table").forEach((table, tableIndex) => {
            if (table.classList.contains("cnc-table--v2") || table.closest(".cnc-v2-panel")) {
                return;
            }

            const bodyRows = Array.from(table.querySelectorAll("tbody tr[data-record-id]"));
            if (bodyRows.length === 0 || table.dataset.cncBulkReady === "true") {
                return;
            }

            table.dataset.cncBulkReady = "true";
            const section = getBulkSection(table);
            if (!section) {
                return;
            }
            section.dataset.cncBulkSection = "true";

            const headerRow = table.querySelector("thead tr");
            if (headerRow) {
                const th = document.createElement("th");
                th.className = "cnc-select-cell";
                const checkbox = document.createElement("input");
                checkbox.type = "checkbox";
                checkbox.className = "form-check-input";
                checkbox.dataset.cncBulkSelectAll = "";
                checkbox.setAttribute("aria-label", "Seleccionar filas visibles");
                checkbox.addEventListener("change", () => {
                    getVisibleBulkRows(section).forEach((row) => {
                        const rowCheck = row.querySelector("[data-cnc-bulk-check]");
                        if (rowCheck) {
                            rowCheck.checked = checkbox.checked;
                        }
                    });
                    refreshBulkSection(section);
                });
                th.appendChild(checkbox);
                headerRow.insertBefore(th, headerRow.firstElementChild);
            }

            bodyRows.forEach((row, index) => {
                const td = document.createElement("td");
                td.className = "cnc-select-cell";
                const checkbox = document.createElement("input");
                checkbox.type = "checkbox";
                checkbox.className = "form-check-input";
                checkbox.dataset.cncBulkCheck = "";
                checkbox.setAttribute("aria-label", `Seleccionar fila ${index + 1}`);
                checkbox.addEventListener("click", (event) => event.stopPropagation());
                checkbox.addEventListener("change", () => refreshBulkSection(section));
                td.appendChild(checkbox);
                row.insertBefore(td, row.firstElementChild);
            });

            const availableActions = getAvailableBulkActions(section);
            const header = section.querySelector(":scope > .cnc-pipeline-stage__header")
                || section.querySelector(":scope > .cnc-table-toolbar");
            if (header && !header.querySelector("[data-cnc-bulk-toolbar]")) {
                const toolbar = document.createElement("div");
                toolbar.className = "cnc-bulk-toolbar";
                toolbar.dataset.cncBulkToolbar = "";
                const count = document.createElement("span");
                count.className = "cnc-bulk-count";
                count.dataset.cncBulkSelectedCount = "";
                count.textContent = "Sin seleccion";
                toolbar.appendChild(count);

                availableActions.forEach((action) => {
                    const button = document.createElement("button");
                    button.type = "button";
                    button.className = action.key.includes("send") ? "btn btn-sm btn-primary" : "btn btn-sm btn-outline-primary";
                    button.dataset.cncBulkAction = action.key;
                    button.dataset.baseLabel = action.label;
                    button.textContent = action.label;
                    button.disabled = true;
                    button.addEventListener("click", () => runBulkAction(section, action));
                    toolbar.appendChild(button);
                });

                header.appendChild(toolbar);
            }

            table.dataset.cncBulkTableIndex = String(tableIndex);
            refreshBulkSection(section);
        });
    };

    const supplierPaymentLabel = (supplier) => {
        if (!supplier) {
            return "";
        }

        const name = supplier.commercialName || supplier.name || supplier.displayName || "Tercero Siigo";
        const rawIdentification = String(supplier.identification || "").trim();
        const nameDigits = String(name).replace(/\D+/g, "");
        const identification = rawIdentification && !nameDigits.includes(rawIdentification.replace(/\D+/g, ""))
            ? ` - ${rawIdentification}`
            : "";
        const branch = Number(supplier.branchOffice || 0) > 0 ? ` sucursal ${supplier.branchOffice}` : "";
        return `${name}${identification}${branch}`;
    };

    const compactSupplierPaymentKey = (value) =>
        String(value || "").trim().toLowerCase().replace(/[^a-z0-9]+/g, "");

    const supplierPaymentRequestForRow = (row, supplier = null) => ({
        recordId: row?.dataset.recordId || "",
        movementExternalKey: row?.dataset.movementExternalKey || "",
        supplierId: supplier?.id || "",
        supplierQuery: String(row?.dataset.supplierQuery || row?.dataset.dataverseSupplierNit || row?.dataset.dataverseSupplier || row?.dataset.description || "").trim(),
        lookbackMonths: 60
    });

    const supplierPaymentRetentionsForRow = (row, purchase = null) => {
        const rowReteFuente = Number(row?.dataset.reteFuenteValue || 0);
        const rowReteIca = Number(row?.dataset.reteIcaValue || 0);
        const purchaseReteFuente = Number(purchase?.dataverseReteFuenteValue || 0);
        const purchaseReteIca = Number(purchase?.dataverseReteIcaValue || 0);
        let reteFuente = rowReteFuente > 0 ? rowReteFuente : purchaseReteFuente;
        let reteIca = rowReteIca > 0 ? rowReteIca : purchaseReteIca;

        if (reteFuente <= 0 && reteIca <= 0 && purchase) {
            const bankPayment = Number(row?.dataset.exitValue || 0);
            const balance = Number(purchase.balance || 0);
            const inferredRetention = Math.max(0, balance - bankPayment);
            if (inferredRetention > 0 && balance > 0 && inferredRetention <= balance * 0.25) {
                reteFuente = inferredRetention;
            }
        }

        return {
            reteFuenteValue: Math.max(0, Math.round(reteFuente)),
            reteIcaValue: Math.max(0, Math.round(reteIca))
        };
    };

    const selectSupplierPaymentPurchaseForRow = (row, purchases) => {
        if (!Array.isArray(purchases) || purchases.length === 0) {
            return null;
        }

        const siigoDocumentId = String(row?.dataset.siigoDocumentId || "").trim().toLowerCase();
        const siigoDocumentName = compactSupplierPaymentKey(row?.dataset.siigoDocumentName);
        const dataverseRecordId = String(row?.dataset.dataverseRecordId || "").trim().toLowerCase();
        const dataverseInvoice = compactSupplierPaymentKey(row?.dataset.dataverseInvoice);
        const bankPayment = Number(row?.dataset.exitValue || 0);
        const dataversePayment = Number(row?.dataset.dataversePaymentValue || 0);
        const dataverseTotal = Number(row?.dataset.dataverseTotal || 0);

        const scored = purchases.map((purchase) => {
            let score = Number(purchase.matchScore || 0);
            const purchaseId = String(purchase.id || "").trim().toLowerCase();
            const purchaseName = compactSupplierPaymentKey(purchase.name);
            const providerInvoice = compactSupplierPaymentKey(purchase.providerInvoiceFullNumber || purchase.providerInvoiceNumber);
            const purchaseDataverseRecordId = String(purchase.dataverseRecordId || "").trim().toLowerCase();
            const purchaseDataverseInvoice = compactSupplierPaymentKey(purchase.dataverseInvoiceNumber);

            if (siigoDocumentId && purchaseId === siigoDocumentId) {
                score += 140;
            }
            if (siigoDocumentName && purchaseName === siigoDocumentName) {
                score += 120;
            }
            if (dataverseRecordId && purchaseDataverseRecordId === dataverseRecordId) {
                score += 110;
            }
            if (dataverseInvoice && (purchaseDataverseInvoice === dataverseInvoice || providerInvoice === dataverseInvoice)) {
                score += 95;
            }
            if (purchase.dataverseMatchTone === "success") {
                score += 75;
            }

            const balance = Number(purchase.balance || 0);
            const amountCandidates = [bankPayment, dataversePayment, dataverseTotal].filter((value) => Number.isFinite(value) && value > 0);
            const amountDelta = amountCandidates.length
                ? Math.min(...amountCandidates.map((value) => Math.abs(balance - value)))
                : Number.MAX_SAFE_INTEGER;
            if (amountDelta <= 1000) {
                score += 45;
            } else if (amountDelta <= Math.max(3000, balance * 0.02)) {
                score += 25;
            }

            return { purchase, score, amountDelta };
        });

        scored.sort((left, right) =>
            right.score - left.score
            || left.amountDelta - right.amountDelta
            || String(left.purchase.dateValue || "").localeCompare(String(right.purchase.dateValue || "")));
        return scored[0]?.purchase || null;
    };

    const setSupplierPaymentIssues = (issues) => {
        if (!supplierPaymentIssues) {
            return;
        }

        const values = Array.isArray(issues)
            ? issues.filter((issue) => String(issue || "").trim())
            : [];
        supplierPaymentIssues.innerHTML = "";
        supplierPaymentIssues.hidden = values.length === 0;
        values.forEach((issue) => {
            const item = document.createElement("li");
            item.textContent = issue;
            supplierPaymentIssues.appendChild(item);
        });
    };

    const setSupplierPaymentPreview = (payloadText, responseText) => {
        if (supplierPaymentPayload) {
            supplierPaymentPayload.textContent = payloadText || "";
        }
        if (supplierPaymentResponse) {
            supplierPaymentResponse.textContent = responseText || "";
        }
        if (supplierPaymentPreview) {
            supplierPaymentPreview.hidden = !payloadText && !responseText;
        }
    };

    const getSupplierPaymentAmount = () => Number(activeSupplierPaymentRow?.dataset.exitValue || 0);

    const updateSupplierPaymentSummary = () => {
        if (!supplierPaymentSummary) {
            return;
        }

        const bankPayment = getSupplierPaymentAmount();
        const reteFuente = positiveNumberFromInput(supplierPaymentReteFuenteValue);
        const reteIca = positiveNumberFromInput(supplierPaymentReteIcaValue);
        const appliedValue = bankPayment + reteFuente + reteIca;
        const balance = selectedSupplierPaymentPurchase ? Number(selectedSupplierPaymentPurchase.balance || 0) : 0;
        const difference = selectedSupplierPaymentPurchase ? balance - appliedValue : 0;
        const items = [
            ["Pago banco", money(bankPayment)],
            ["Retefuente", money(reteFuente)],
            ["ReteICA", money(reteIca)],
            ["Valor aplicado", money(appliedValue)]
        ];

        if (selectedSupplierPaymentPurchase) {
            items.push(["Saldo factura", money(balance)]);
            items.push(["Diferencia", money(difference)]);
        }
        const dataverseInvoice = selectedSupplierPaymentPurchase?.dataverseInvoiceNumber || activeSupplierPaymentRow?.dataset.dataverseInvoice || "";
        if (dataverseInvoice) {
            items.push(["Gastos Dataverse", dataverseInvoice]);
        }

        supplierPaymentSummary.innerHTML = "";
        items.forEach(([label, value]) => {
            const item = document.createElement("div");
            const title = document.createElement("span");
            const number = document.createElement("strong");
            title.textContent = label;
            number.textContent = value;
            item.append(title, number);
            supplierPaymentSummary.appendChild(item);
        });

        if (supplierPaymentSend) {
            supplierPaymentSend.disabled = !activeSupplierPaymentRow
                || !selectedSupplierPaymentPurchase
                || !supplierPaymentSendUrl
                || bankPayment <= 0;
        }
    };

    const resetSupplierPaymentModal = () => {
        selectedSupplierPaymentSupplier = null;
        selectedSupplierPaymentPurchase = null;
        setSupplierPaymentIssues([]);
        setSupplierPaymentPreview("", "");

        if (supplierPaymentSupplierQuery) {
            supplierPaymentSupplierQuery.value = "";
        }
        if (supplierPaymentSuppliers) {
            supplierPaymentSuppliers.innerHTML = "";
        }
        if (supplierPaymentPurchases) {
            supplierPaymentPurchases.innerHTML = '<tr><td colspan="6"><small>Busca o confirma el proveedor para ver facturas abiertas.</small></td></tr>';
        }
        [supplierPaymentReteFuenteValue, supplierPaymentReteIcaValue].forEach((input) => {
            if (input) {
                input.value = "0";
            }
        });
        [supplierPaymentReteFuenteRate, supplierPaymentReteIcaRate].forEach((input) => {
            if (input) {
                input.value = "";
            }
        });
        updateSupplierPaymentSummary();
    };

    const isSupplierPaymentBatchActive = () =>
        supplierPaymentBatchRows.length > 0 && supplierPaymentBatchIndex >= 0;

    const closeSupplierPaymentModal = () => {
        const shouldReload = supplierPaymentBatchDirty;
        supplierPaymentBatchRows = [];
        supplierPaymentBatchIndex = -1;
        supplierPaymentBatchDirty = false;
        if (supplierPaymentModal) {
            supplierPaymentModal.hidden = true;
        }
        if (supplierPaymentSkip) {
            supplierPaymentSkip.hidden = true;
        }
        activeSupplierPaymentRow = null;
        resetSupplierPaymentModal();
        if (shouldReload) {
            window.setTimeout(reloadPreservingView, 650);
        }
    };

    const supplierPaymentRequestBase = () => {
        const request = supplierPaymentRequestForRow(activeSupplierPaymentRow, selectedSupplierPaymentSupplier);
        request.supplierQuery = String(supplierPaymentSupplierQuery?.value || request.supplierQuery || "").trim();
        return request;
    };

    const renderSupplierPaymentSuppliers = (items, message, selectedSupplier = null) => {
        if (!supplierPaymentSuppliers) {
            return;
        }

        supplierPaymentSuppliers.innerHTML = "";
        if (selectedSupplier && (!Array.isArray(items) || items.length === 0)) {
            const selected = document.createElement("div");
            selected.className = "cnc-supplier-payment-selected";
            selected.textContent = `Proveedor seleccionado: ${supplierPaymentLabel(selectedSupplier)}`;
            supplierPaymentSuppliers.appendChild(selected);
            return;
        }

        if (message) {
            const detail = document.createElement("small");
            detail.textContent = message;
            supplierPaymentSuppliers.appendChild(detail);
        }

        if (!Array.isArray(items) || items.length === 0) {
            if (!message && !selectedSupplier) {
                const empty = document.createElement("small");
                empty.textContent = "No hay proveedores para mostrar.";
                supplierPaymentSuppliers.appendChild(empty);
            }
            return;
        }

        items.forEach((supplier) => {
            const button = document.createElement("button");
            const title = document.createElement("strong");
            const detail = document.createElement("small");
            button.type = "button";
            button.className = "cnc-supplier-payment-supplier";
            title.textContent = supplierPaymentLabel(supplier);
            detail.textContent = supplier.active === false ? "Inactivo" : "Activo en Siigo";
            button.append(title, detail);
            button.addEventListener("click", () => {
                selectedSupplierPaymentSupplier = supplier;
                selectedSupplierPaymentPurchase = null;
                if (supplierPaymentSupplierQuery) {
                    supplierPaymentSupplierQuery.value = supplierPaymentLabel(supplier);
                }
                updateSupplierPaymentSummary();
                loadSupplierPaymentPurchases({ supplierId: supplier.id || "", supplierQuery: supplier.identification || supplier.displayName || supplier.name || "" });
            });
            supplierPaymentSuppliers.appendChild(button);
        });
    };

    const renderSupplierPaymentPurchases = (items) => {
        if (!supplierPaymentPurchases) {
            return;
        }

        supplierPaymentPurchases.innerHTML = "";
        selectedSupplierPaymentPurchase = null;

        if (!Array.isArray(items) || items.length === 0) {
            const empty = document.createElement("tr");
            const cell = document.createElement("td");
            cell.colSpan = 6;
            cell.innerHTML = "<small>No hay facturas abiertas con saldo para este proveedor.</small>";
            empty.appendChild(cell);
            supplierPaymentPurchases.appendChild(empty);
            updateSupplierPaymentSummary();
            return;
        }

        items.forEach((purchase) => {
            const row = document.createElement("tr");
            row.className = "cnc-supplier-payment-invoice";
            row.tabIndex = 0;

            const date = document.createElement("td");
            const name = document.createElement("td");
            const providerInvoice = document.createElement("td");
            const dataverse = document.createElement("td");
            const balance = document.createElement("td");
            const total = document.createElement("td");
            const dataverseBadge = document.createElement("span");
            const dataverseDetail = document.createElement("small");

            date.textContent = purchase.dateDisplay || purchase.dateValue || "Sin fecha";
            name.textContent = purchase.name || "Sin numero";
            providerInvoice.textContent = purchase.providerInvoiceFullNumber || purchase.providerInvoiceNumber || "-";
            dataverseBadge.className = `cnc-badge cnc-badge--${purchase.dataverseMatchTone || "neutral"}`;
            dataverseBadge.textContent = purchase.dataverseMatchLabel || "Sin cruce Dataverse";
            dataverseDetail.textContent = purchase.dataverseSupplierName
                ? `${purchase.dataverseInvoiceNumber || "Sin numero"} - ${purchase.dataverseSupplierName}`
                : (purchase.dataverseInvoiceNumber || "");
            dataverse.append(dataverseBadge);
            if (dataverseDetail.textContent) {
                dataverse.appendChild(dataverseDetail);
            }
            balance.className = "text-end";
            balance.textContent = money(purchase.balance);
            total.className = "text-end";
            total.textContent = money(purchase.total);

            row.append(date, name, providerInvoice, dataverse, balance, total);
            const selectPurchase = () => {
                supplierPaymentPurchases.querySelectorAll(".cnc-supplier-payment-invoice").forEach((item) => item.classList.remove("is-selected"));
                row.classList.add("is-selected");
                selectedSupplierPaymentPurchase = purchase;

                const currentReteFuente = positiveNumberFromInput(supplierPaymentReteFuenteValue);
                const currentReteIca = positiveNumberFromInput(supplierPaymentReteIcaValue);
                const inferredRetention = Math.max(0, Number(purchase.balance || 0) - getSupplierPaymentAmount());
                if (currentReteFuente <= 0 && currentReteIca <= 0) {
                    const retentions = supplierPaymentRetentionsForRow(activeSupplierPaymentRow, purchase);
                    if (supplierPaymentReteFuenteValue && retentions.reteFuenteValue > 0) {
                        supplierPaymentReteFuenteValue.value = String(retentions.reteFuenteValue);
                    } else if (inferredRetention > 0 && supplierPaymentReteFuenteValue) {
                        supplierPaymentReteFuenteValue.value = String(Math.round(inferredRetention));
                    }
                    if (supplierPaymentReteIcaValue && retentions.reteIcaValue > 0) {
                        supplierPaymentReteIcaValue.value = String(retentions.reteIcaValue);
                    }
                }
                updateSupplierPaymentSummary();
            };
            row.addEventListener("click", selectPurchase);
            row.addEventListener("keydown", (event) => {
                if (event.key === "Enter" || event.key === " ") {
                    event.preventDefault();
                    selectPurchase();
                }
            });
            supplierPaymentPurchases.appendChild(row);
        });

        updateSupplierPaymentSummary();
    };

    const loadSupplierPaymentPurchases = async (options = {}) => {
        if (!activeSupplierPaymentRow || !supplierPaymentPurchasesUrl) {
            setStatus("No se encontro la ruta para consultar facturas abiertas.", "error");
            return;
        }

        const request = supplierPaymentRequestBase();
        request.supplierId = options.supplierId || request.supplierId;
        request.supplierQuery = options.supplierQuery || request.supplierQuery || activeSupplierPaymentRow.dataset.supplierQuery || "";

        selectedSupplierPaymentPurchase = null;
        setSupplierPaymentIssues([]);
        setSupplierPaymentPreview("", "");
        if (supplierPaymentPurchases) {
            supplierPaymentPurchases.innerHTML = '<tr><td colspan="6"><small>Consultando facturas abiertas en Siigo...</small></td></tr>';
        }
        if (supplierPaymentSupplierSearch) {
            supplierPaymentSupplierSearch.disabled = true;
        }
        updateSupplierPaymentSummary();

        try {
            const response = await fetch(supplierPaymentPurchasesUrl, {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify(request)
            });
            const payload = await response.json().catch(() => ({}));
            if (!response.ok) {
                throw new Error(payload.detail || payload.message || "No fue posible consultar facturas abiertas.");
            }

            if (payload.supplier) {
                selectedSupplierPaymentSupplier = payload.supplier;
                if (supplierPaymentSupplierQuery) {
                    supplierPaymentSupplierQuery.value = supplierPaymentLabel(payload.supplier);
                }
            }

            renderSupplierPaymentSuppliers(payload.supplierCandidates || [], payload.message || "", payload.supplier || selectedSupplierPaymentSupplier);
            renderSupplierPaymentPurchases(payload.purchases || []);
            setStatus(payload.message || "Consulta de facturas abiertas finalizada.", "info");
        } catch (error) {
            renderSupplierPaymentPurchases([]);
            renderSupplierPaymentSuppliers([], "");
            setStatus(error instanceof Error ? error.message : "Ocurrio un error inesperado.", "error");
        } finally {
            if (supplierPaymentSupplierSearch) {
                supplierPaymentSupplierSearch.disabled = false;
            }
        }
    };

    const searchSupplierPaymentSuppliers = async () => {
        const query = String(supplierPaymentSupplierQuery?.value || "").trim();
        if (!query) {
            setStatus("Escribe un nombre o NIT de proveedor para buscar en Siigo.", "info");
            return;
        }
        if (!siigoSupplierSearchUrl) {
            setStatus("No se encontro la ruta para buscar proveedores en Siigo.", "error");
            return;
        }

        selectedSupplierPaymentSupplier = null;
        selectedSupplierPaymentPurchase = null;
        setSupplierPaymentIssues([]);
        setSupplierPaymentPreview("", "");
        if (supplierPaymentSupplierSearch) {
            supplierPaymentSupplierSearch.disabled = true;
        }
        if (supplierPaymentSuppliers) {
            supplierPaymentSuppliers.innerHTML = "<small>Buscando proveedores en Siigo...</small>";
        }
        renderSupplierPaymentPurchases([]);

        try {
            const response = await fetch(siigoSupplierSearchUrl, {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify({ query, top: 12 })
            });
            const payload = await response.json().catch(() => ({}));
            if (!response.ok) {
                throw new Error(payload.detail || payload.message || "No fue posible buscar proveedores.");
            }

            renderSupplierPaymentSuppliers(payload.items || [], payload.message || "");
            setStatus(payload.message || "Selecciona un proveedor para ver sus facturas abiertas.", "info");
        } catch (error) {
            renderSupplierPaymentSuppliers([], "");
            setStatus(error instanceof Error ? error.message : "Ocurrio un error inesperado.", "error");
        } finally {
            if (supplierPaymentSupplierSearch) {
                supplierPaymentSupplierSearch.disabled = false;
            }
        }
    };

    const openSupplierPaymentModal = (row, options = {}) => {
        activeSupplierPaymentRow = row;
        resetSupplierPaymentModal();

        const query = row.dataset.supplierQuery || row.dataset.dataverseSupplierNit || row.dataset.dataverseSupplier || row.dataset.description || "";
        const dataverseInvoice = row.dataset.dataverseInvoice || "";
        if (supplierPaymentDescription) {
            const invoiceText = dataverseInvoice ? ` Factura encontrada: ${dataverseInvoice}.` : "";
            const prefix = options.prefix ? `${options.prefix} ` : "";
            supplierPaymentDescription.textContent = `${prefix}${row.dataset.description || "Salida sin descripcion."} Pago: ${money(Number(row.dataset.exitValue || 0))}.${invoiceText}`;
        }
        if (supplierPaymentSupplierQuery) {
            supplierPaymentSupplierQuery.value = query;
        }
        const retentions = supplierPaymentRetentionsForRow(row);
        if (supplierPaymentReteFuenteValue && retentions.reteFuenteValue > 0) {
            supplierPaymentReteFuenteValue.value = String(retentions.reteFuenteValue);
        }
        if (supplierPaymentReteIcaValue && retentions.reteIcaValue > 0) {
            supplierPaymentReteIcaValue.value = String(retentions.reteIcaValue);
        }
        if (supplierPaymentModal) {
            supplierPaymentModal.hidden = false;
        }
        if (supplierPaymentSkip) {
            supplierPaymentSkip.hidden = !isSupplierPaymentBatchActive();
        }
        updateSupplierPaymentSummary();

        if (query) {
            loadSupplierPaymentPurchases({ supplierQuery: query });
        }
    };

    const openNextSupplierPaymentBatchRow = (message = "", tone = "info") => {
        supplierPaymentBatchIndex += 1;
        if (supplierPaymentBatchIndex >= supplierPaymentBatchRows.length) {
            const hadChanges = supplierPaymentBatchDirty;
            supplierPaymentBatchRows = [];
            supplierPaymentBatchIndex = -1;
            supplierPaymentBatchDirty = false;
            if (supplierPaymentSkip) {
                supplierPaymentSkip.hidden = true;
            }
            if (supplierPaymentModal) {
                supplierPaymentModal.hidden = true;
            }
            resetSupplierPaymentModal();
            activeSupplierPaymentRow = null;
            setStatus(message || "Busqueda masiva de facturas finalizada.", hadChanges ? "success" : tone);
            if (hadChanges) {
                window.setTimeout(reloadPreservingView, 650);
            }
            return;
        }

        const current = supplierPaymentBatchRows[supplierPaymentBatchIndex];
        const prefix = `${supplierPaymentBatchIndex + 1} de ${supplierPaymentBatchRows.length}.`;
        if (message) {
            setStatus(message, tone);
        }
        openSupplierPaymentModal(current, { prefix });
    };

    const openBulkSupplierPaymentSearchModal = (rows) => {
        supplierPaymentBatchRows = rows.filter((row) => row?.dataset?.recordId || row?.dataset?.movementExternalKey);
        supplierPaymentBatchIndex = -1;
        supplierPaymentBatchDirty = false;
        if (supplierPaymentBatchRows.length === 0) {
            setStatus("Selecciona salidas FC para buscar facturas.", "info");
            return;
        }

        openNextSupplierPaymentBatchRow(`Buscando facturas para ${supplierPaymentBatchRows.length} salida${supplierPaymentBatchRows.length === 1 ? "" : "s"} FC.`, "info");
    };

    const skipSupplierPaymentBatchRow = () => {
        if (!isSupplierPaymentBatchActive()) {
            return;
        }

        openNextSupplierPaymentBatchRow("Salida omitida. Continuando con la siguiente.", "info");
    };

    const sendSupplierPaymentToSiigo = async () => {
        if (!activeSupplierPaymentRow || !selectedSupplierPaymentPurchase || !supplierPaymentSendUrl) {
            setStatus("Selecciona una factura abierta antes de enviar el pago.", "info");
            return;
        }

        const request = supplierPaymentRequestBase();
        request.supplierId = selectedSupplierPaymentSupplier?.id || request.supplierId;
        request.supplierIdentification = selectedSupplierPaymentSupplier?.identification || "";
        request.supplierName = supplierPaymentLabel(selectedSupplierPaymentSupplier);
        request.purchaseId = selectedSupplierPaymentPurchase.id || "";
        request.purchaseName = selectedSupplierPaymentPurchase.name || "";
        request.reteFuenteValue = positiveNumberFromInput(supplierPaymentReteFuenteValue);
        request.reteFuenteRate = positiveNumberFromInput(supplierPaymentReteFuenteRate);
        request.reteIcaValue = positiveNumberFromInput(supplierPaymentReteIcaValue);
        request.reteIcaRate = positiveNumberFromInput(supplierPaymentReteIcaRate);

        if (!request.purchaseId && !request.purchaseName) {
            setStatus("La factura seleccionada no tiene identificador valido.", "error");
            return;
        }

        if (supplierPaymentSend) {
            supplierPaymentSend.disabled = true;
        }
        setSupplierPaymentIssues([]);
        setSupplierPaymentPreview("", "");
        setStatus("Enviando pago proveedor a Siigo...", "info");

        try {
            const response = await fetch(supplierPaymentSendUrl, {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify(request)
            });
            const payload = await response.json().catch(() => ({}));
            setSupplierPaymentIssues(payload.issues || []);
            setSupplierPaymentPreview(payload.payloadJson || "", payload.responseJson || "");

            if (!response.ok) {
                throw new Error(payload.detail || payload.message || "No fue posible enviar el pago a Siigo.");
            }

            if (!payload.isSuccess) {
                setStatus(payload.message || "El pago quedo bloqueado por validacion.", "warning");
                updateSupplierPaymentSummary();
                return;
            }

            if (isSupplierPaymentBatchActive()) {
                supplierPaymentBatchDirty = true;
                openNextSupplierPaymentBatchRow(payload.message || "Pago proveedor enviado a Siigo.", "success");
                return;
            }

            setStatus(payload.message || "Pago proveedor enviado a Siigo.", "success");
            window.setTimeout(reloadPreservingView, 800);
        } catch (error) {
            setStatus(error instanceof Error ? error.message : "Ocurrio un error inesperado.", "error");
            updateSupplierPaymentSummary();
        }
    };

    const markSupplierPaymentManualSiigo = async (button, options = {}) => {
        const row = button?.closest("tr[data-record-id]");
        if (!row) {
            return { success: false, message: "No se encontro la salida FC." };
        }
        if (!supplierPaymentManualUrl) {
            return { success: false, message: "No se encontro la ruta para marcar la salida manual." };
        }
        if (!options.skipConfirm && !window.confirm("Esto marcara la salida FC como Conciliada manualmente y conciliada. Continuar?")) {
            return { success: false, message: "Operacion cancelada." };
        }

        const buttons = Array.from(row.querySelectorAll("[data-cnc-supplier-payment-manual], [data-cnc-supplier-payment-search]"));
        buttons.forEach((item) => { item.disabled = true; });
        setStatus("Marcando salida FC como Conciliada manualmente...", "info");

        try {
            const request = supplierPaymentRequestForRow(row);
            const response = await fetch(supplierPaymentManualUrl, {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify(request)
            });
            const payload = await response.json().catch(() => ({}));
            if (!response.ok || payload.isSuccess === false) {
                const issue = Array.isArray(payload.issues) && payload.issues.length ? payload.issues[0] : "";
                throw new Error(payload.detail || payload.message || issue || "No fue posible marcar la salida como manual.");
            }

            const message = payload.message || "Salida FC marcada como Conciliada manualmente.";
            setStatus(message, "success");
            if (!options.suppressReload) {
                window.setTimeout(reloadPreservingView, 650);
            }
            return { success: true, message, payload };
        } catch (error) {
            const message = error instanceof Error ? error.message : "Ocurrio un error inesperado.";
            setStatus(message, "error");
            return { success: false, message };
        } finally {
            buttons.forEach((item) => { item.disabled = false; });
        }
    };

    const searchSupplierPaymentPurchasesForAutoSend = async (row) => {
        const baseRequest = supplierPaymentRequestForRow(row);
        const response = await fetch(supplierPaymentPurchasesUrl, {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify(baseRequest)
        });
        const payload = await response.json().catch(() => ({}));
        if (!response.ok) {
            throw new Error(payload.detail || payload.message || "No fue posible consultar facturas abiertas en Siigo.");
        }

        let supplier = payload.supplier || null;
        let purchases = Array.isArray(payload.purchases) ? payload.purchases : [];
        let message = payload.message || "";
        if (purchases.length > 0 || !Array.isArray(payload.supplierCandidates) || payload.supplierCandidates.length === 0) {
            return { supplier, purchases, message };
        }

        for (const candidate of payload.supplierCandidates.slice(0, 6)) {
            const candidateRequest = supplierPaymentRequestForRow(row, candidate);
            candidateRequest.supplierId = candidate.id || "";
            candidateRequest.supplierQuery = candidate.identification || candidate.displayName || candidate.name || candidateRequest.supplierQuery;
            const candidateResponse = await fetch(supplierPaymentPurchasesUrl, {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify(candidateRequest)
            });
            const candidatePayload = await candidateResponse.json().catch(() => ({}));
            if (!candidateResponse.ok) {
                message = candidatePayload.detail || candidatePayload.message || message;
                continue;
            }

            const candidatePurchases = Array.isArray(candidatePayload.purchases) ? candidatePayload.purchases : [];
            const selected = selectSupplierPaymentPurchaseForRow(row, candidatePurchases);
            if (selected) {
                return {
                    supplier: candidatePayload.supplier || candidate,
                    purchases: candidatePurchases,
                    message: candidatePayload.message || message
                };
            }

            if (candidatePurchases.length > purchases.length) {
                supplier = candidatePayload.supplier || candidate;
                purchases = candidatePurchases;
                message = candidatePayload.message || message;
            }
        }

        return { supplier, purchases, message };
    };

    const runSupplierPaymentAction = async (button, options = {}) => {
        const row = button?.closest("[data-record-id]");
        if (!row) {
            return { success: false, message: "No se encontro la salida del flujo de caja." };
        }
        if (!supplierPaymentPurchasesUrl || !supplierPaymentSendUrl) {
            return { success: false, message: "No se encontraron las rutas de consulta/envio de pagos proveedor." };
        }
        if (!options.skipConfirm) {
            const message = options.confirmMessage || "Esto aplicara el pago proveedor real en Siigo. Continuar?";
            if (!window.confirm(message)) {
                return { success: false, message: "Operacion cancelada." };
            }
        }

        button.disabled = true;
        setStatus(options.loadingMessage || "Buscando factura abierta y aplicando pago proveedor...", "info");

        try {
            const searchPayload = await searchSupplierPaymentPurchasesForAutoSend(row);
            const purchase = selectSupplierPaymentPurchaseForRow(row, searchPayload.purchases || []);
            if (!purchase) {
                throw new Error(searchPayload.message
                    ? `${searchPayload.message} No encontramos una factura Siigo abierta para aplicar este pago. Abre Revisar para buscarla manualmente por proveedor.`
                    : "No encontramos una factura Siigo abierta para aplicar este pago. Abre Revisar para buscarla manualmente por proveedor.");
            }

            const retentions = supplierPaymentRetentionsForRow(row, purchase);
            const supplier = searchPayload.supplier || null;
            const request = supplierPaymentRequestForRow(row, supplier);
            request.supplierId = supplier?.id || request.supplierId || "";
            request.supplierIdentification = supplier?.identification || purchase.supplierIdentification || row.dataset.dataverseSupplierNit || "";
            request.supplierName = supplier ? supplierPaymentLabel(supplier) : row.dataset.dataverseSupplier || "";
            request.purchaseId = purchase.id || "";
            request.purchaseName = purchase.name || "";
            request.reteFuenteValue = retentions.reteFuenteValue;
            request.reteFuenteRate = 0;
            request.reteIcaValue = retentions.reteIcaValue;
            request.reteIcaRate = 0;

            const sendResponse = await fetch(supplierPaymentSendUrl, {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify(request)
            });
            const payload = await sendResponse.json().catch(() => ({}));
            if (!sendResponse.ok || !payload.isSuccess) {
                const issue = Array.isArray(payload.issues) && payload.issues.length ? payload.issues[0] : "";
                throw new Error(payload.detail || payload.message || issue || "No fue posible aplicar el pago en Siigo.");
            }

            const message = payload.message || options.successMessage || "Pago proveedor aplicado en Siigo.";
            setStatus(message, "success");
            if (options.reloadOnSuccess !== false && !options.suppressReload) {
                window.setTimeout(reloadPreservingView, 800);
            }

            return { success: true, message };
        } catch (error) {
            const message = error instanceof Error ? error.message : (options.errorMessage || "Ocurrio un error inesperado.");
            setStatus(message, "error");
            return { success: false, message };
        } finally {
            button.disabled = false;
        }
    };

    const closeInvoiceModal = () => {
        if (invoiceModal) {
            invoiceModal.hidden = true;
        }
        activeInvoiceRow = null;
        selectedInvoiceIds = [];
        selectedInvoices = [];
    };

    const setSelectedInvoices = (invoices) => {
        selectedInvoices = Array.isArray(invoices)
            ? invoices.filter((invoice) => invoice?.recordId)
            : [];
        selectedInvoiceIds = selectedInvoices
            .map((invoice) => invoice.recordId || "")
            .filter(Boolean);
        const paymentValue = Number(invoiceValue?.value || activeInvoiceRow?.dataset.entryValue || 0);
        if (invoiceSelected) {
            renderInvoiceSelectionSummary(
                invoiceSelected,
                selectedInvoices,
                paymentValue,
                "Selecciona una o varias facturas. La suma se comparara contra el pago.");
        }
        if (invoiceSave) {
            invoiceSave.disabled = selectedInvoiceIds.length === 0;
        }
    };

    const toggleSelectedInvoice = (invoice) => {
        const recordId = invoice?.recordId || "";
        if (!recordId) {
            return;
        }

        const exists = selectedInvoiceIds.includes(recordId);
        setSelectedInvoices(exists
            ? selectedInvoices.filter((item) => item.recordId !== recordId)
            : [...selectedInvoices, invoice]);
        invoiceResults?.querySelectorAll(".cnc-invoice-result").forEach((button) => {
            button.classList.toggle("is-selected", selectedInvoiceIds.includes(button.dataset.invoiceId || ""));
        });
    };

    const renderInvoiceResults = (items) => {
        if (!invoiceResults) {
            return;
        }

        invoiceResults.innerHTML = "";
        if (!Array.isArray(items) || items.length === 0) {
            const empty = document.createElement("small");
            empty.textContent = "No hay resultados con esos criterios.";
            invoiceResults.appendChild(empty);
            setSelectedInvoices([]);
            return;
        }

        items.forEach((invoice) => {
            const button = document.createElement("button");
            button.type = "button";
            button.className = "cnc-invoice-result";
            button.dataset.invoiceId = invoice.recordId || "";
            button.classList.toggle("is-selected", selectedInvoiceIds.includes(invoice.recordId || ""));
            const title = document.createElement("strong");
            const amount = document.createElement("span");
            const client = document.createElement("small");
            const retentions = document.createElement("small");
            const net = document.createElement("small");
            title.textContent = invoice.invoiceNumber || "Sin factura";
            amount.textContent = money(invoice.totalInvoice);
            client.textContent = `${invoice.clientName || "Sin cliente"} - ${invoice.emissionDateDisplay || "Sin fecha"}`;
            retentions.textContent = `Retenciones: ${money((invoice.reteFteValue || 0) + (invoice.reteIcaValue || 0) + (invoice.rteIvaValue || 0))}`;
            net.textContent = `Neto pago: ${money(Number(invoice.totalInvoice || 0) - invoiceRetentionTotal(invoice))}`;
            title.appendChild(amount);
            button.append(title, client, retentions, net);
            button.addEventListener("click", () => {
                toggleSelectedInvoice(invoice);
            });
            invoiceResults.appendChild(button);
        });
    };

    const searchDataverseInvoices = async () => {
        if (!invoiceSearchUrl) {
            setStatus("No se encontro la ruta para buscar facturas.", "error");
            return;
        }

        const query = String(invoiceQuery?.value || "").trim();
        const rawValue = Number(invoiceValue?.value || 0);
        const value = Number.isFinite(rawValue) && rawValue > 0 ? rawValue : null;
        if (!query && !value) {
            setStatus("Busca por cliente, numero de factura o valor.", "info");
            return;
        }

        setSelectedInvoices([]);
        if (invoiceSearchButton) {
            invoiceSearchButton.disabled = true;
        }
        if (invoiceResults) {
            invoiceResults.innerHTML = "<small>Buscando facturas en Dataverse...</small>";
        }

        try {
            const response = await fetch(invoiceSearchUrl, {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify({ query, value, top: 20 })
            });
            const payload = await response.json().catch(() => ({}));
            if (!response.ok) {
                throw new Error(payload.detail || payload.message || "No fue posible buscar facturas.");
            }

            renderInvoiceResults(payload.items || []);
            setStatus(payload.message || "Busqueda finalizada.", "info");
        } catch (error) {
            if (invoiceResults) {
                invoiceResults.innerHTML = "<small>No fue posible buscar facturas.</small>";
            }
            setStatus(error instanceof Error ? error.message : "Ocurrio un error inesperado.", "error");
        } finally {
            if (invoiceSearchButton) {
                invoiceSearchButton.disabled = false;
            }
        }
    };

    const openInvoiceModal = (row) => {
        activeInvoiceRow = row;
        selectedInvoiceIds = [];
        selectedInvoices = [];
        const flowInvoice = row.dataset.flowInvoice || "";
        const dataverseInvoice = row.dataset.dataverseInvoice || "";
        const dataverseClient = row.dataset.dataverseClient || "";
        const entryValue = row.dataset.entryValue || "";

        if (invoiceDescription) {
            invoiceDescription.textContent = `${row.dataset.description || "Pago sin descripcion."} Entrada: ${money(Number(entryValue || 0))}`;
        }
        if (invoiceQuery) {
            invoiceQuery.value = flowInvoice || dataverseInvoice || dataverseClient || "";
        }
        if (invoiceValue) {
            invoiceValue.value = entryValue;
        }
        if (invoiceResults) {
            invoiceResults.innerHTML = "<small>Busca por cliente, numero de factura o valor. Puedes seleccionar varias facturas y ver la suma contra el pago.</small>";
        }
        setSelectedInvoices([]);

        if (invoiceModal) {
            invoiceModal.hidden = false;
        }
    };

    const saveInvoiceAssignment = async () => {
        if (!activeInvoiceRow || selectedInvoiceIds.length === 0 || !invoiceAssignUrl) {
            setStatus("Selecciona una o varias facturas para guardar la asignacion.", "info");
            return;
        }

        const recordId = activeInvoiceRow.dataset.recordId || "";
        if (!recordId) {
            setStatus("No se encontro el cruce a actualizar.", "error");
            return;
        }

        if (invoiceSave) {
            invoiceSave.disabled = true;
        }
        setStatus("Guardando asignacion de factura en Dataverse...", "info");

        try {
            const response = await fetch(invoiceAssignUrl, {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify({
                    recordId,
                    invoiceRecordId: selectedInvoiceIds[0] || "",
                    invoiceRecordIds: selectedInvoiceIds
                })
            });
            const payload = await response.json().catch(() => ({}));
            if (!response.ok) {
                throw new Error(payload.detail || payload.message || "No fue posible asignar la factura.");
            }

            setStatus(payload.message || "Factura asignada.", "success");
            window.setTimeout(reloadPreservingView, 650);
        } catch (error) {
            setStatus(error instanceof Error ? error.message : "Ocurrio un error inesperado.", "error");
            if (invoiceSave) {
                invoiceSave.disabled = false;
            }
        }
    };

    const openMonthCloseModal = (button) => {
        if (!monthCloseModal) {
            return;
        }

        const bankLabel = button?.dataset?.bankLabel || "";
        if (monthCloseDescription) {
            monthCloseDescription.textContent = bankLabel
                ? `Carga el extracto de ${bankLabel} para compararlo contra el flujo de caja manual del mes.`
                : "Carga el extracto por banco para comparar contra el flujo de caja manual del mes.";
        }

        monthCloseModal.querySelectorAll("tr[data-bank-key]").forEach((row) => {
            row.classList.toggle("is-highlighted", Boolean(bankLabel) && row.dataset.bankKey === button?.dataset?.bankKey);
        });
        monthCloseModal.hidden = false;
    };

    const closeMonthCloseModal = () => {
        if (monthCloseModal) {
            monthCloseModal.hidden = true;
        }
    };

    const acknowledgeMonthCloseFiles = () => {
        const selected = Array.from(monthCloseModal?.querySelectorAll("[data-cnc-bank-statement]") || [])
            .filter((input) => input.files && input.files.length > 0)
            .length;
        setStatus(
            selected > 0
                ? `${selected} extracto(s) seleccionado(s). Queda listo para conectar el comparador cuando tengamos el formato del banco.`
                : "Selecciona al menos un extracto bancario para el cierre.",
            selected > 0 ? "info" : "warning");
        if (selected > 0) {
            closeMonthCloseModal();
        }
    };

    const validateCashFlowMonth = async (button) => {
        if (!cashFlowMonthValidateUrl) {
            setStatus("No se encontro la ruta para validar el mes.", "error");
            return;
        }

        const year = Number(button.dataset.year || 0);
        const month = Number(button.dataset.month || 0);
        if (!year || !month) {
            setStatus("No se encontro el periodo a validar.", "error");
            return;
        }

        if (!window.confirm("Marcar este mes como validado? Solo debe hacerse si todos los comparativos estan en cero.")) {
            return;
        }

        button.disabled = true;
        setStatus("Validando cierre mensual contra Siigo, Dataverse y flujo de caja...", "info");
        try {
            const response = await fetch(cashFlowMonthValidateUrl, {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify({
                    year,
                    month,
                    comments: "Mes validado manualmente desde flujo de caja."
                })
            });
            const payload = await response.json().catch(() => ({}));
            if (!response.ok) {
                throw new Error(payload.detail || payload.message || "No fue posible marcar el mes como validado.");
            }

            setStatus(payload.message || "Mes validado.", "success");
            window.setTimeout(reloadPreservingView, 700);
        } catch (error) {
            button.disabled = false;
            setStatus(error instanceof Error ? error.message : "Ocurrio un error inesperado.", "error");
        }
    };

    const shouldIgnoreRowClick = (target) => Boolean(target.closest("button, a, input, select, textarea, details, summary, label"));

    tabButtons.forEach((button) => {
        button.addEventListener("click", () => {
            setActiveTab(button.dataset.cncTab || "");
            refreshAllFilters();
        });
    });

    syncRefreshButton?.addEventListener("click", () => {
        syncLoaded = false;
        loadSyncHealth(true);
    });

    billingDiffRefreshButton?.addEventListener("click", () => {
        loadBillingDifferences();
    });

    billingCreateSelectedButton?.addEventListener("click", () => {
        runBillingDifferenceAction("create");
    });

    billingDeleteSelectedButton?.addEventListener("click", () => {
        runBillingDifferenceAction("delete");
    });

    deduccionesForm?.addEventListener("submit", importDeduccionesIva);
    deduccionesHistoryOpeners.forEach((opener) => {
        const open = (event) => {
            if (event?.target?.closest("a")) {
                return;
            }
            const historyId = opener.dataset.historyId || "";
            const history = deduccionesHistoryItems.find((item) =>
                String(item?.importId || "") === historyId);
            if (history) {
                openDeduccionesHistoryDetail(history);
            }
        };
        opener.addEventListener("click", open);
        opener.addEventListener("keydown", (event) => {
            if (event.key === "Enter" || event.key === " ") {
                event.preventDefault();
                open(event);
            }
        });
    });
    deduccionesFile?.addEventListener("change", () => {
        const file = deduccionesFile.files?.[0];
        if (file) {
            setDeduccionesResultLoading(`Listo para importar: ${file.name}`);
        }
    });

    bankImportForm?.addEventListener("submit", importBancolombiaStatement);
    bankImportForm?.querySelector("[data-cnc-bank-import-file]")?.addEventListener("change", (event) => {
        const file = event.target?.files?.[0];
        if (file) {
            setBankImportResult(`Listo: ${file.name}`, "info");
        }
    });
    bankBalanceSelect?.addEventListener("change", renderBankBalance);
    bankBalanceOpenButton?.addEventListener("click", openBankBalanceModal);
    bankBalanceSave?.addEventListener("click", saveBankOpeningBalance);
    app.querySelectorAll("[data-cnc-bank-balance-close], [data-cnc-bank-balance-cancel]")
        .forEach((button) => button.addEventListener("click", closeBankBalanceModal));
    bankBalanceModal?.addEventListener("click", (event) => {
        if (event.target === bankBalanceModal) {
            closeBankBalanceModal();
        }
    });
    bankBalanceInput?.addEventListener("keydown", (event) => {
        if (event.key === "Enter") {
            event.preventDefault();
            saveBankOpeningBalance();
        } else if (event.key === "Escape") {
            closeBankBalanceModal();
        }
    });

    app.querySelectorAll("[data-cnc-tab-target]").forEach((button) => {
        button.addEventListener("click", (event) => {
            event.stopPropagation();
            setActiveTab(button.dataset.cncTabTarget || "");
            refreshAllFilters();
        });
    });

    app.querySelectorAll("[data-cnc-start-cashflow]").forEach((button) => {
        button.addEventListener("click", (event) => {
            event.stopPropagation();
            openCashFlowWizard();
        });
    });

    app.querySelectorAll("[data-cnc-v2-row]").forEach((row) => {
        row.addEventListener("click", (event) => {
            if (shouldIgnoreRowClick(event.target)) {
                return;
            }

            openCashFlowWizardForRow(row);
        });
    });

    v2FilterButtons.forEach((button) => {
        button.addEventListener("click", () => setConciliacion2Filter(button));
    });

    app.querySelectorAll("[data-cnc-v2-description-save]").forEach((button) => {
        button.addEventListener("click", (event) => {
            event.stopPropagation();
            saveConciliacion2Description(button);
        });
    });

    app.querySelectorAll("[data-cnc-v2-description-input]").forEach((input) => {
        input.addEventListener("keydown", (event) => {
            if (event.key === "Enter") {
                event.preventDefault();
                saveConciliacion2Description(input);
            }
        });
    });

    app.querySelectorAll("[data-cnc-v2-conciliated]").forEach((checkbox) => {
        checkbox.addEventListener("click", (event) => event.stopPropagation());
        checkbox.addEventListener("change", () => markConciliacion2Check(checkbox));
    });

    app.querySelectorAll("[data-cnc-cashflow-manual]").forEach((button) => {
        button.addEventListener("click", (event) => {
            event.stopPropagation();
            markCashFlowManualSiigo(button);
        });
    });

    [paymentSearch, paymentStatusFilter].forEach((input) => {
        input?.addEventListener("input", applyPaymentFilters);
        input?.addEventListener("change", applyPaymentFilters);
    });

    verticalButtons.forEach((button) => {
        button.addEventListener("click", () => {
            activeVertical = resolveVerticalKey(button.dataset.cncVertical || "Cloud");
            persistViewState(resolveCurrentTab(), activeVertical);
            refreshAllFilters();
        });
    });

    genericTableSearches.forEach((input) => {
        const key = input.dataset.cncTableSearch || "";
        input.addEventListener("input", () => applyGenericTableFilter(key));
        input.addEventListener("change", () => applyGenericTableFilter(key));
    });

    columnFilterInputs.forEach((input) => {
        const key = input.dataset.cncColumnFilter || "";
        input.addEventListener("input", () => applyGenericTableFilter(key));
        input.addEventListener("change", () => applyGenericTableFilter(key));
    });

    paymentRowsBody?.querySelectorAll("[data-cnc-action]").forEach((button) => {
        button.addEventListener("click", () => updatePaymentStatus(button));
    });

    paymentRowsBody?.querySelectorAll("[data-cnc-preflight]").forEach((button) => {
        button.addEventListener("click", () => validatePaymentPreflight(button));
    });

    paymentRowsBody?.querySelectorAll("[data-cnc-dry-run]").forEach((button) => {
        button.addEventListener("click", () => simulatePaymentSiigoDryRun(button));
    });

    paymentRowsBody?.querySelectorAll("[data-cnc-send-siigo]").forEach((button) => {
        button.addEventListener("click", () => openPaymentSendPreview(button));
    });

    paymentRowsBody?.querySelectorAll("[data-cnc-manual-siigo]").forEach((button) => {
        button.addEventListener("click", (event) => {
            event.stopPropagation();
            markPaymentManualSiigo(button);
        });
    });

    app.querySelectorAll("[data-cnc-reassign]").forEach((row) => {
        row.addEventListener("click", (event) => {
            if (shouldIgnoreRowClick(event.target)) {
                return;
            }

            openReassignModal(row);
        });
    });

    app.querySelectorAll("[data-cnc-dian-edit]").forEach((row) => {
        row.addEventListener("click", (event) => {
            if (shouldIgnoreRowClick(event.target)) {
                return;
            }

            openDianEditModal(row);
        });
    });

    app.querySelectorAll("[data-cnc-dian-supplier-row]").forEach((row) => {
        row.addEventListener("click", (event) => {
            if (shouldIgnoreRowClick(event.target)) {
                return;
            }

            openDianSupplierModal(row);
        });
    });

    app.querySelectorAll("[data-cnc-cuenta-cobro-edit]").forEach((row) => {
        row.addEventListener("click", (event) => {
            if (shouldIgnoreRowClick(event.target)) {
                return;
            }

            openCuentaCobroModal(row);
        });
    });

    app.querySelectorAll("[data-cnc-accounting-voucher-edit]").forEach((row) => {
        row.addEventListener("click", (event) => {
            if (shouldIgnoreRowClick(event.target)) {
                return;
            }

            openAccountingVoucherModal(row);
        });
    });

    app.querySelectorAll("[data-cnc-accounting-voucher-line-edit]").forEach((button) => {
        button.addEventListener("click", (event) => {
            event.stopPropagation();
            openAccountingVoucherModal(button);
        });
    });

    app.querySelectorAll("[data-cnc-invoice-assign]").forEach((row) => {
        row.addEventListener("click", (event) => {
            if (shouldIgnoreRowClick(event.target)) {
                return;
            }

            openInvoiceModal(row);
        });
    });

    app.querySelectorAll("[data-cnc-open-invoice-modal]").forEach((button) => {
        button.addEventListener("click", (event) => {
            event.stopPropagation();
            const row = button.closest("[data-cnc-invoice-assign]");
            if (row) {
                openInvoiceModal(row);
            }
        });
    });

    app.querySelectorAll("[data-cnc-dian-open-edit]").forEach((button) => {
        button.addEventListener("click", (event) => {
            event.stopPropagation();
            const row = button.closest("[data-cnc-dian-edit]");
            if (row) {
                openDianEditModal(row);
            }
        });
    });

    app.querySelectorAll("[data-cnc-dian-create-supplier]").forEach((button) => {
        button.addEventListener("click", (event) => {
            event.stopPropagation();
            const row = button.closest("[data-cnc-dian-supplier-row]");
            if (row) {
                openDianSupplierModal(row, { mode: "rut" });
            }
        });
    });

    app.querySelectorAll("[data-cnc-dian-create-supplier-manual]").forEach((button) => {
        button.addEventListener("click", (event) => {
            event.stopPropagation();
            const row = button.closest("[data-cnc-dian-supplier-row]");
            if (row) {
                openDianSupplierModal(row, { mode: "manual" });
            }
        });
    });

    app.querySelectorAll("[data-cnc-dian-supplier-lookup]").forEach((button) => {
        button.addEventListener("click", (event) => {
            event.stopPropagation();
            validateDianSuppliers(button);
        });
    });

    app.querySelectorAll("[data-cnc-dian-dry-run]").forEach((button) => {
        button.addEventListener("click", (event) => {
            event.stopPropagation();
            runDianAction(button, dianDryRunUrl, {
                loadingMessage: "Simulando factura de compra Siigo...",
                successMessage: "Simulacion finalizada.",
                errorMessage: "No fue posible simular la factura.",
                reloadOnSuccess: false
            });
        });
    });

    app.querySelectorAll("[data-cnc-dian-send]").forEach((button) => {
        button.addEventListener("click", (event) => {
            event.stopPropagation();
            openDianInvoiceSendPreview(button);
        });
    });

    app.querySelectorAll("[data-cnc-dian-preview-send]").forEach((button) => {
        button.addEventListener("click", (event) => {
            event.stopPropagation();
            if (button.dataset.ambiguousSiigoWrite === "true") {
                runDianAction(button, dianSendUrl, {
                    loadingMessage: "Verificando si Siigo alcanzo a crear la factura...",
                    successMessage: "Verificacion Siigo finalizada.",
                    errorMessage: "La factura sigue pendiente de confirmacion segura en Siigo.",
                    skipConfirm: true,
                    reloadOnSuccess: true
                });
                return;
            }
            openDianInvoiceSendPreview(button);
        });
    });

    app.querySelectorAll("[data-cnc-cuenta-cobro-preflight]").forEach((button) => {
        button.addEventListener("click", (event) => {
            event.stopPropagation();
            runCuentaCobroAction(button, cuentaCobroPreflightUrl, {
                loadingMessage: "Validando documento soporte pre-Siigo...",
                successMessage: "Prevalidacion finalizada.",
                errorMessage: "No fue posible validar el documento soporte.",
                reloadOnSuccess: true
            });
        });
    });

    app.querySelectorAll("[data-cnc-cuenta-cobro-send]").forEach((button) => {
        button.addEventListener("click", (event) => {
            event.stopPropagation();
            openCuentaCobroSendPreview(button);
        });
    });

    app.querySelectorAll("[data-cnc-cuenta-cobro-preview-send]").forEach((button) => {
        button.addEventListener("click", (event) => {
            event.stopPropagation();
            openCuentaCobroSendPreview(button);
        });
    });

    app.querySelectorAll("[data-cnc-cuenta-cobro-send-payment]").forEach((button) => {
        button.addEventListener("click", (event) => {
            event.stopPropagation();
            sendCuentaCobroPaymentToSiigo(button);
        });
    });

    app.querySelectorAll("[data-cnc-cuenta-cobro-manual]").forEach((button) => {
        button.addEventListener("click", (event) => {
            event.stopPropagation();
            markCuentaCobroManualSiigo(button);
        });
    });

    app.querySelectorAll("[data-cnc-accounting-voucher-send]").forEach((button) => {
        button.addEventListener("click", (event) => {
            event.stopPropagation();
            const row = button.closest("tr[data-record-id]");
            if (accountingVoucherRequiresThirdParty(row)) {
                openAccountingVoucherModal(row);
                setStatus("Selecciona el tercero real de Siigo para continuar con el egreso.", "info");
                return;
            }
            runAccountingVoucherAction(button, {
                loadingMessage: "Enviando comprobante contable real a Siigo...",
                successMessage: "Comprobante contable enviado a Siigo.",
                errorMessage: "No fue posible enviar el comprobante contable.",
                confirmMessage: "Esto creara un comprobante contable real en Siigo.",
                reloadOnSuccess: true
            });
        });
    });

    app.querySelectorAll("[data-cnc-open-reassign]").forEach((button) => {
        button.addEventListener("click", (event) => {
            event.stopPropagation();
            const row = button.closest("[data-cnc-reassign]");
            if (row) {
                openReassignModal(row);
            }
        });
    });

    app.querySelectorAll("[data-cnc-open-supplier-payment]").forEach((button) => {
        button.addEventListener("click", (event) => {
            event.stopPropagation();
            const row = button.closest("[data-record-id]");
            if (row) {
                openSupplierPaymentModal(row);
            }
        });
    });

    app.querySelectorAll("[data-cnc-supplier-payment-send]").forEach((button) => {
        button.addEventListener("click", (event) => {
            event.stopPropagation();
            runSupplierPaymentAction(button, {
                loadingMessage: "Aplicando pago proveedor en Siigo...",
                successMessage: "Pago proveedor aplicado en Siigo.",
                errorMessage: "No fue posible aplicar el pago proveedor.",
                confirmMessage: "Esto aplicara el pago proveedor real en Siigo. Continuar?",
                reloadOnSuccess: true
            });
        });
    });

    app.querySelectorAll("[data-cnc-supplier-payment-row]").forEach((row) => {
        row.addEventListener("click", (event) => {
            if (shouldIgnoreRowClick(event.target)) {
                return;
            }

            openSupplierPaymentModal(row);
        });
    });

    app.querySelectorAll("[data-cnc-close-modal]").forEach((button) => {
        button.addEventListener("click", closeReassignModal);
    });

    reassignModal?.addEventListener("click", (event) => {
        if (event.target === reassignModal) {
            closeReassignModal();
        }
    });

    reassignApply?.addEventListener("click", applyReassignCategory);
    app.querySelectorAll("[data-cnc-close-dian-modal]").forEach((button) => {
        button.addEventListener("click", closeDianEditModal);
    });
    dianEditModal?.addEventListener("click", (event) => {
        if (event.target === dianEditModal) {
            closeDianEditModal();
        }
    });
    dianSave?.addEventListener("click", saveDianClassification);
    dianSkip?.addEventListener("click", skipDianAccountBatchRow);

    app.querySelectorAll("[data-cnc-close-cuenta-cobro-modal]").forEach((button) => {
        button.addEventListener("click", closeCuentaCobroModal);
    });

    cuentaCobroModal?.addEventListener("click", (event) => {
        if (event.target === cuentaCobroModal) {
            closeCuentaCobroModal();
        }
    });

    cuentaCobroSave?.addEventListener("click", saveCuentaCobroClassification);

    app.querySelectorAll("[data-cnc-close-accounting-voucher-modal]").forEach((button) => {
        button.addEventListener("click", closeAccountingVoucherModal);
    });

    accountingVoucherModal?.addEventListener("click", (event) => {
        if (event.target === accountingVoucherModal) {
            closeAccountingVoucherModal();
        }
    });

    accountingVoucherSave?.addEventListener("click", saveAccountingVoucherAccount);
    accountingVoucherSend?.addEventListener("click", sendAccountingVoucherFromModal);
    accountingVoucherAccount?.addEventListener("change", updateAccountingVoucherModalActions);
    accountingVoucherThirdPartySearch?.addEventListener("click", searchAccountingVoucherThirdParties);
    accountingVoucherThirdPartyQuery?.addEventListener("input", scheduleAccountingVoucherThirdPartySearch);
    accountingVoucherThirdPartyQuery?.addEventListener("keydown", (event) => {
        if (event.key === "Enter") {
            event.preventDefault();
            window.clearTimeout(accountingVoucherThirdPartySearchTimer);
            searchAccountingVoucherThirdParties();
        }
    });

    app.querySelectorAll("[data-cnc-close-supplier-payment-modal]").forEach((button) => {
        button.addEventListener("click", closeSupplierPaymentModal);
    });
    supplierPaymentModal?.addEventListener("click", (event) => {
        if (event.target === supplierPaymentModal) {
            closeSupplierPaymentModal();
        }
    });
    supplierPaymentSupplierSearch?.addEventListener("click", searchSupplierPaymentSuppliers);
    supplierPaymentSend?.addEventListener("click", sendSupplierPaymentToSiigo);
    supplierPaymentSkip?.addEventListener("click", skipSupplierPaymentBatchRow);
    supplierPaymentSupplierQuery?.addEventListener("keydown", (event) => {
        if (event.key === "Enter") {
            event.preventDefault();
            searchSupplierPaymentSuppliers();
        }
    });
    [
        supplierPaymentReteFuenteValue,
        supplierPaymentReteFuenteRate,
        supplierPaymentReteIcaValue,
        supplierPaymentReteIcaRate
    ].forEach((input) => {
        input?.addEventListener("input", updateSupplierPaymentSummary);
        input?.addEventListener("change", updateSupplierPaymentSummary);
    });

    app.querySelectorAll("[data-cnc-close-dian-supplier-modal]").forEach((button) => {
        button.addEventListener("click", closeDianSupplierModal);
    });
    dianSupplierModal?.addEventListener("click", (event) => {
        if (event.target === dianSupplierModal) {
            closeDianSupplierModal();
        }
    });
    dianSupplierPersonType?.addEventListener("change", setDianSupplierTypeDefaults);
    dianSupplierVatResponsible?.addEventListener("change", syncDianSupplierFiscalFields);
    dianSupplierFiscalResponsibility?.addEventListener("change", syncDianSupplierFiscalFields);
    dianSupplierNit?.addEventListener("input", () => {
        if ((dianSupplierPersonType?.value || "Company") === "Company" && dianSupplierCheckDigit) {
            dianSupplierCheckDigit.value = calculateColombianCheckDigit(dianSupplierNit.value);
        }
    });
    [
        dianSupplierName,
        dianSupplierNit,
        dianSupplierAddress,
        dianSupplierCity
    ].forEach((field) => {
        const eventName = field instanceof HTMLSelectElement ? "change" : "input";
        field?.addEventListener(eventName, () => {
            setDianSupplierFieldValidity(field, true);
            setDianSupplierFeedback();
        });
    });
    dianSupplierRutAnalyze?.addEventListener("click", analyzeDianSupplierRut);
    dianSupplierRutFile?.addEventListener("change", () => {
        dianSupplierRutAnalyzed = false;
        if (dianSupplierRutStatus) {
            dianSupplierRutStatus.textContent = dianSupplierRutFile.files?.[0]
                ? "RUT seleccionado. Haz clic en “Extraer datos con IA”."
                : "Adjunta el RUT para completar automáticamente los datos fiscales.";
        }
    });
    dianSupplierSave?.addEventListener("click", saveDianSupplier);

    app.querySelectorAll("[data-cnc-close-invoice-modal]").forEach((button) => {
        button.addEventListener("click", closeInvoiceModal);
    });
    invoiceModal?.addEventListener("click", (event) => {
        if (event.target === invoiceModal) {
            closeInvoiceModal();
        }
    });
    invoiceSearchButton?.addEventListener("click", searchDataverseInvoices);
    invoiceSave?.addEventListener("click", saveInvoiceAssignment);
    [invoiceQuery, invoiceValue].forEach((input) => {
        input?.addEventListener("keydown", (event) => {
            if (event.key === "Enter") {
                event.preventDefault();
                searchDataverseInvoices();
            }
        });
    });

    app.querySelectorAll("[data-cnc-open-month-close]").forEach((button) => {
        button.addEventListener("click", (event) => {
            event.stopPropagation();
            openMonthCloseModal(button);
        });
    });
    app.querySelectorAll("[data-cnc-close-month-modal]").forEach((button) => {
        button.addEventListener("click", closeMonthCloseModal);
    });
    monthCloseModal?.addEventListener("click", (event) => {
        if (event.target === monthCloseModal) {
            closeMonthCloseModal();
        }
    });
    monthCloseModal?.querySelectorAll("[data-cnc-bank-statement]").forEach((input) => {
        input.addEventListener("change", () => {
            const status = input.closest("tr")?.querySelector("[data-cnc-bank-statement-status]");
            if (status) {
                const hasFile = input.files && input.files.length > 0;
                status.textContent = hasFile ? input.files[0].name : "Pendiente";
                status.className = `cnc-badge cnc-badge--${hasFile ? "info" : "neutral"}`;
            }
        });
    });
    app.querySelector("[data-cnc-close-month-ack]")?.addEventListener("click", acknowledgeMonthCloseFiles);
    app.querySelectorAll("[data-cnc-validate-month]").forEach((button) => {
        button.addEventListener("click", () => validateCashFlowMonth(button));
    });
    window.addEventListener("hashchange", applyHashViewState);

    activeVertical = resolveInitialVertical();
    initializeAccountSearches();
    initializeBulkTables();
    initializeCollapsibleTables();
    const initialTab = resolveInitialTab();
    setActiveTab(initialTab, false);
    persistViewState(initialTab, activeVertical);
    refreshAllFilters();
    renderBankBalance();
})();
