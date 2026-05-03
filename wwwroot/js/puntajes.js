(() => {
    const app = document.getElementById("puntajesApp");
    if (!app) {
        return;
    }

    const optionsNode = document.getElementById("puntajes-options-data");
    const options = optionsNode?.textContent ? JSON.parse(optionsNode.textContent) : {};
    const groupsContainer = document.getElementById("scoresGroupsContainer");
    const statusBanner = document.getElementById("scoresStatusBanner");
    const refreshButton = document.getElementById("refreshScoresBtn");
    const filterButtons = Array.from(document.querySelectorAll(".scores-filter-btn"));
    const summaryClients = document.getElementById("summaryClients");
    const summaryRecords = document.getElementById("summaryRecords");
    const summaryProducts = document.getElementById("summaryProducts");
    const summaryScore = document.getElementById("summaryScore");
    const summaryCommission = document.getElementById("summaryCommission");
    const summaryAnnualValue = document.getElementById("summaryAnnualValue");
    const closeMonthButton = document.getElementById("closeMonthBtn");
    const undoCloseMonthButton = document.getElementById("undoCloseMonthBtn");
    const closeMonthSummaryText = document.getElementById("closeMonthSummaryText");
    const closeMonthLogEmpty = document.getElementById("closeMonthLogEmpty");
    const closeMonthLogList = document.getElementById("closeMonthLogList");
    const closeMonthReviewModalElement = document.getElementById("closeMonthReviewModal");
    const closeMonthReviewTitle = document.getElementById("closeMonthReviewTitle");
    const closeMonthReviewSubtitle = document.getElementById("closeMonthReviewSubtitle");
    const closeMonthReviewStatus = document.getElementById("closeMonthReviewStatus");
    const closeMonthReviewSummary = document.getElementById("closeMonthReviewSummary");
    const closeMonthReviewIncludedEmpty = document.getElementById("closeMonthReviewIncludedEmpty");
    const closeMonthReviewIncludedList = document.getElementById("closeMonthReviewIncludedList");
    const closeMonthReviewExcludedEmpty = document.getElementById("closeMonthReviewExcludedEmpty");
    const closeMonthReviewExcludedList = document.getElementById("closeMonthReviewExcludedList");
    const closeMonthReviewConfirmCheck = document.getElementById("closeMonthReviewConfirmCheck");
    const submitCloseMonthReviewBtn = document.getElementById("submitCloseMonthReviewBtn");

    const verifyModalElement = document.getElementById("verifyScoreModal");
    const verifyModalTitle = document.getElementById("verifyScoreModalTitle");
    const verifyModalSubtitle = document.getElementById("verifyScoreModalSubtitle");
    const verifyModalStatus = document.getElementById("verifyScoreModalStatus");
    const verifyModalLoading = document.getElementById("verifyScoreModalLoading");
    const verifyScoreForm = document.getElementById("verifyScoreForm");
    const verifyMetaCards = document.getElementById("verifyMetaCards");
    const verifyLinesBody = document.getElementById("verifyLinesBody");
    const dealTypeSelect = document.getElementById("dealTypeSelect");
    const dealTypeHelp = document.getElementById("dealTypeHelp");
    const requiresProrationSelect = document.getElementById("requiresProrationSelect");
    const scenarioStartDateInput = document.getElementById("scenarioStartDateInput");
    const scenarioEndDateInput = document.getElementById("scenarioEndDateInput");
    const scenarioEndDateSelect = document.getElementById("scenarioEndDateSelect");
    const scenarioEndDateHelp = document.getElementById("scenarioEndDateHelp");
    const verifyProrationClientWrap = document.getElementById("verifyProrationClientWrap");
    const verifyProrationClientInput = document.getElementById("verifyProrationClientInput");
    const verifyProrationClientHelp = document.getElementById("verifyProrationClientHelp");
    const firstContractSelect = document.getElementById("firstContractSelect");
    const verticalOptionSelect = document.getElementById("verticalOptionSelect");
    const billingDayInput = document.getElementById("billingDayInput");
    const billingDayHelp = document.getElementById("billingDayHelp");
    const renewalDateInput = document.getElementById("renewalDateInput");
    const renewalDateHelp = document.getElementById("renewalDateHelp");
    const autoBillSelect = document.getElementById("autoBillSelect");
    const contractTypeSelect = document.getElementById("contractTypeSelect");
    const addVerifyLineBtn = document.getElementById("addVerifyLineBtn");
    const recalculateVerifyScoreBtn = document.getElementById("recalculateVerifyScoreBtn");
    const submitVerifyScoreBtn = document.getElementById("submitVerifyScoreBtn");
    const verifyResultPoints = document.getElementById("verifyResultPoints");
    const verifyResultCommission = document.getElementById("verifyResultCommission");
    const verifyResultProration = document.getElementById("verifyResultProration");
    const verifyResultMonthly = document.getElementById("verifyResultMonthly");
    const verifyResultTotal = document.getElementById("verifyResultTotal");
    const verifyModal = verifyModalElement && window.bootstrap ? new bootstrap.Modal(verifyModalElement) : null;
    const closeMonthReviewModal = closeMonthReviewModalElement && window.bootstrap ? new bootstrap.Modal(closeMonthReviewModalElement) : null;

    const optionMaps = {
        dealType: buildOptionMap(options.dealTypeOptions),
        firstContract: buildOptionMap(options.firstContractOptions),
        line: buildOptionMap(options.lineOptions),
        vertical: buildOptionMap(options.verticalOptions),
        hasVat: buildOptionMap(options.hasVatOptions),
        autoBill: buildOptionMap(options.autoBillOptions),
        productLine: buildOptionMap(options.productLineOptions),
        contractType: buildOptionMap(options.contractTypeOptions)
    };

    const numberFormatter = new Intl.NumberFormat("es-CO", {
        minimumFractionDigits: 0,
        maximumFractionDigits: 2
    });

    const scoreFormatter = new Intl.NumberFormat("es-CO", {
        minimumFractionDigits: 2,
        maximumFractionDigits: 2
    });

    const CROSS_SALE_DEAL_TYPE = 1;
    const RENEWAL_DEAL_TYPES = new Set([2, 3, 4]);
    const AUTO_BILL_YES_VALUE = 1;
    const MODERN_WORK_LINE_OPTION_VALUE = 645250000;

    const state = {
        filter: app.dataset.initialFilter || "this-month",
        board: null,
        recordMap: new Map(),
        expandedGroups: new Set(),
        expandedRecords: new Set(),
        activeRecordId: "",
        activeDraft: null,
        prorationLookupToken: 0,
        isLoading: false,
        isSaving: false,
        isRecalculating: false,
        isClosingMonth: false,
        lastCloseResult: null,
        closeMonthPreview: null
    };

    function buildOptionMap(items) {
        const map = new Map();
        (Array.isArray(items) ? items : []).forEach(item => {
            map.set(String(item.value ?? item.Value), item.label ?? item.Label ?? "");
        });
        return map;
    }

    function escapeHtml(value) {
        return (value ?? "").toString()
            .replace(/&/g, "&amp;")
            .replace(/</g, "&lt;")
            .replace(/>/g, "&gt;")
            .replace(/"/g, "&quot;")
            .replace(/'/g, "&#39;");
    }

    function toNumber(value, fallback = 0) {
        const parsed = Number(value);
        return Number.isFinite(parsed) ? parsed : fallback;
    }

    function roundMoney(value) {
        return Number(toNumber(value, 0).toFixed(2));
    }

    function formatNumber(value) {
        return numberFormatter.format(Number(value || 0));
    }

    function formatScoreValue(value) {
        return scoreFormatter.format(Number(value || 0));
    }

    function formatPercent(value) {
        return `${formatNumber(value)}%`;
    }

    function containsPrepaidOrYear(value) {
        const normalized = (value || "").toString().toLowerCase();
        return normalized.includes("prepaid") || normalized.includes("1 year");
    }

    function isModernWorkLine(line) {
        return Number(line?.lineOptionValue || 0) === MODERN_WORK_LINE_OPTION_VALUE;
    }

    function normalizePositiveInteger(value, fallback) {
        return Math.max(1, Math.trunc(toNumber(value, fallback)));
    }

    function recomputeVerifyLine(line, source = "margin") {
        if (!line) {
            return line;
        }

        line.costUnit = roundMoney(Math.max(toNumber(line.costUnit, 0), 0));
        line.marginPercent = roundMoney(toNumber(line.marginPercent, 0));
        line.contractMonths = normalizePositiveInteger(line.contractMonths, 12);
        line.quantity = normalizePositiveInteger(line.quantity, 1);
        line.saleUnit = roundMoney(toNumber(line.saleUnit, 0));
        line.monthlyValue = roundMoney(Math.max(toNumber(line.monthlyValue, 0), 0));
        line.totalValue = roundMoney(Math.max(toNumber(line.totalValue, 0), 0));

        if (containsPrepaidOrYear(line.productName)) {
            line.contractMonths = 12;
        }

        if (source === "sale") {
            line.marginPercent = line.costUnit > 0 ? roundMoney(((line.saleUnit / line.costUnit) - 1) * 100) : 0;
            line.monthlyValue = roundMoney(line.saleUnit * line.quantity);
            line.totalValue = roundMoney(line.monthlyValue * line.contractMonths);
            return line;
        }

        if (source === "monthly") {
            line.saleUnit = line.quantity > 0 ? roundMoney(line.monthlyValue / line.quantity) : 0;
            line.marginPercent = line.costUnit > 0 ? roundMoney(((line.saleUnit / line.costUnit) - 1) * 100) : 0;
            line.totalValue = roundMoney(line.monthlyValue * line.contractMonths);
            return line;
        }

        if (source === "total") {
            line.monthlyValue = line.contractMonths > 0 ? roundMoney(line.totalValue / line.contractMonths) : 0;
            line.saleUnit = line.quantity > 0 ? roundMoney(line.monthlyValue / line.quantity) : 0;
            line.marginPercent = line.costUnit > 0 ? roundMoney(((line.saleUnit / line.costUnit) - 1) * 100) : 0;
            return line;
        }

        line.saleUnit = roundMoney(line.costUnit * (1 + (line.marginPercent / 100)));
        line.monthlyValue = roundMoney(line.saleUnit * line.quantity);
        line.totalValue = roundMoney(line.monthlyValue * line.contractMonths);
        return line;
    }

    function parseDateValue(value) {
        if (!value || typeof value !== "string") {
            return null;
        }

        const match = value.trim().match(/^(\d{4})-(\d{2})-(\d{2})$/);
        if (!match) {
            return null;
        }

        const year = Number(match[1]);
        const month = Number(match[2]);
        const day = Number(match[3]);
        const date = new Date(Date.UTC(year, month - 1, day));
        if (Number.isNaN(date.getTime())) {
            return null;
        }

        return date;
    }

    function buildDefaultRenewalDateValue(contractStartDateValue) {
        const contractStartDate = parseDateValue(contractStartDateValue);
        if (!contractStartDate) {
            return "";
        }

        const targetYear = contractStartDate.getUTCFullYear() + 1;
        const month = contractStartDate.getUTCMonth() + 1;
        const day = contractStartDate.getUTCDate();
        const maxDayOfTargetMonth = new Date(Date.UTC(targetYear, month, 0)).getUTCDate();
        return `${targetYear}-${String(month).padStart(2, "0")}-${String(Math.min(day, maxDayOfTargetMonth)).padStart(2, "0")}`;
    }

    function formatDateDisplay(value) {
        const parsedDate = parseDateValue(value);
        if (!parsedDate) {
            return value || "";
        }

        return `${String(parsedDate.getUTCDate()).padStart(2, "0")}/${String(parsedDate.getUTCMonth() + 1).padStart(2, "0")}/${parsedDate.getUTCFullYear()}`;
    }

    function normalizeRenewalOption(option) {
        const dateValue = typeof option?.dateValue === "string"
            ? option.dateValue.trim().slice(0, 10)
            : "";
        if (!parseDateValue(dateValue)) {
            return null;
        }

        return {
            recordId: (option?.recordId || "").toString(),
            dateValue,
            displayDate: (option?.displayDate || "").toString().trim() || formatDateDisplay(dateValue)
        };
    }

    async function fetchClientRenewalDates(clientId) {
        const items = await fetchJson(`/Calculator/ClientRenewalDates?clientId=${encodeURIComponent(clientId)}`);
        if (!Array.isArray(items)) {
            throw new Error("El servidor no devolvio una lista valida de fechas de renovacion.");
        }

        return items
            .map(normalizeRenewalOption)
            .filter(Boolean);
    }

    function resetProrationLookupState(draft, clearEndDate = true) {
        if (!draft) {
            return;
        }

        draft.prorationRenewalOptions = [];
        draft.prorationRenewalError = "";
        draft.prorationRenewalLoading = false;
        if (clearEndDate) {
            draft.scenarioEndDateValue = "";
        }
    }

    function renderProrationEndDateOptions() {
        const draft = state.activeDraft;
        const requiresProration = Boolean(draft?.requiresProration);

        if (verifyProrationClientWrap) {
            verifyProrationClientWrap.hidden = !requiresProration;
        }

        if (scenarioEndDateInput) {
            scenarioEndDateInput.hidden = requiresProration;
            scenarioEndDateInput.disabled = true;
        }

        if (scenarioEndDateSelect) {
            scenarioEndDateSelect.hidden = !requiresProration;
        }

        if (!requiresProration || !draft) {
            if (verifyProrationClientInput) {
                verifyProrationClientInput.value = draft?.clientName || "";
            }
            if (verifyProrationClientHelp) {
                verifyProrationClientHelp.textContent = "";
            }
            if (scenarioEndDateHelp) {
                scenarioEndDateHelp.textContent = "";
            }
            return;
        }

        const clientId = draft.prorationClient?.id || draft.clientId || "";
        const clientName = draft.prorationClient?.name || draft.clientName || "";
        const options = Array.isArray(draft.prorationRenewalOptions) ? draft.prorationRenewalOptions : [];

        let placeholder = "Selecciona una fecha";
        if (draft.prorationRenewalLoading) {
            placeholder = "Cargando fechas...";
        } else if (!clientId) {
            placeholder = "El registro no tiene cliente.";
        } else if (draft.prorationRenewalError) {
            placeholder = "No se pudieron consultar las fechas.";
        } else if (!options.length) {
            placeholder = "No se encontraron fechas";
        }

        if (scenarioEndDateSelect) {
            scenarioEndDateSelect.innerHTML = [
                `<option value="">${escapeHtml(placeholder)}</option>`,
                ...options.map(option => `<option value="${escapeHtml(option.dateValue)}">${escapeHtml(option.displayDate || formatDateDisplay(option.dateValue))}</option>`)
            ].join("");
            scenarioEndDateSelect.disabled = draft.prorationRenewalLoading || !options.length;
            scenarioEndDateSelect.value = options.some(option => option.dateValue === draft.scenarioEndDateValue)
                ? draft.scenarioEndDateValue
                : "";
        }

        if (verifyProrationClientInput) {
            verifyProrationClientInput.value = clientName;
        }

        if (verifyProrationClientHelp) {
            verifyProrationClientHelp.textContent = draft.prorationRenewalLoading
                ? `Consultando fechas disponibles para ${clientName || "el cliente"}...`
                : (draft.prorationRenewalError
                    ? draft.prorationRenewalError
                    : (clientId
                        ? `Se muestran las fechas disponibles de ${clientName || "este cliente"}.`
                        : "El registro no tiene cliente asociado."));
        }

        if (scenarioEndDateHelp) {
            scenarioEndDateHelp.textContent = draft.prorationRenewalError || "";
        }
    }

    function syncProrationControls() {
        renderProrationEndDateOptions();
    }

    async function ensureProrationRenewalOptions({ forceReload = false, preserveSelectedDate = true, silent = false } = {}) {
        const draft = state.activeDraft;
        if (!draft || !draft.requiresProration) {
            return;
        }

        const clientId = draft.prorationClient?.id || draft.clientId || "";
        const clientName = draft.prorationClient?.name || draft.clientName || "";
        draft.prorationClient = clientId
            ? { id: clientId, name: clientName }
            : null;

        if (!clientId) {
            resetProrationLookupState(draft, !preserveSelectedDate);
            draft.prorationRenewalError = "El registro no tiene un cliente valido para consultar fechas de prorrateo.";
            renderProrationEndDateOptions();
            return;
        }

        if (!forceReload && Array.isArray(draft.prorationRenewalOptions) && draft.prorationRenewalOptions.length) {
            renderProrationEndDateOptions();
            return;
        }

        const lookupToken = ++state.prorationLookupToken;
        draft.prorationRenewalLoading = true;
        draft.prorationRenewalError = "";
        if (!preserveSelectedDate) {
            draft.scenarioEndDateValue = "";
        }
        renderProrationEndDateOptions();

        try {
            const options = await fetchClientRenewalDates(clientId);
            if (lookupToken !== state.prorationLookupToken || state.activeDraft !== draft) {
                return;
            }

            draft.prorationRenewalOptions = options;
            draft.prorationRenewalError = "";
            draft.prorationRenewalLoading = false;
            if (!options.some(option => option.dateValue === draft.scenarioEndDateValue)) {
                draft.scenarioEndDateValue = "";
            }

            applyDraftDerivedDefaults(draft);
            renewalDateInput && (renewalDateInput.value = draft.renewalDateValue || "");
            syncBillingDayAvailability();
            syncRenewalDateHint();
            renderProrationEndDateOptions();
        } catch (error) {
            if (lookupToken !== state.prorationLookupToken || state.activeDraft !== draft) {
                return;
            }

            resetProrationLookupState(draft, true);
            draft.prorationRenewalError = formatErrorMessage(error, "No se pudieron consultar las fechas de renovacion.");
            renderProrationEndDateOptions();
            if (!silent) {
                setModalStatus("error", draft.prorationRenewalError);
            }
        }
    }

    function deriveBillingDayValue(...candidates) {
        for (const candidate of candidates) {
            const parsedDate = parseDateValue(candidate);
            if (parsedDate) {
                return parsedDate.getUTCDate();
            }
        }

        return 0;
    }

    function normalizeSelectValue(value, fallback = -1) {
        const parsed = Number(value);
        return Number.isFinite(parsed) ? parsed : fallback;
    }

    function buildRenewalSuggestion(draft) {
        if (!draft) {
            return { value: "", mode: "", hint: "" };
        }

        if (!draft.requiresProration && draft.renewalMode === "ONETIME" && !draft.renewalDateValue) {
            return {
                value: "",
                mode: "ONETIME",
                hint: "ONETIME"
            };
        }

        if (draft.requiresProration) {
            const endDateValue = draft.scenarioEndDateValue || "";
            return {
                value: endDateValue,
                mode: endDateValue ? "PRORATION" : "",
                hint: endDateValue
                    ? "Se propone la fecha final del prorrateo como renovación."
                    : "Selecciona la fecha final del prorrateo para definir la renovación."
            };
        }

        const firstLineMonths = Number(draft.lines?.[0]?.contractMonths || 0);
        if (firstLineMonths === 12) {
            const suggestedDate = buildDefaultRenewalDateValue(draft.contractStartDateValue);
            return {
                value: suggestedDate,
                mode: suggestedDate ? "ANNUAL" : "",
                hint: suggestedDate
                    ? "Se propone 1 año después del inicio del contrato."
                    : ""
            };
        }

        return {
            value: "",
            mode: "ONETIME",
            hint: "ONETIME"
        };
    }

    function deriveFirstContractValue(dealTypeValue) {
        return Number(dealTypeValue) === 0 ? 1 : 2;
    }

    function applyDraftDerivedDefaults(draft) {
        if (!draft) {
            return draft;
        }

        draft.dealTypeValue = Number(draft.dealTypeValue || 0);
        draft.requiresProration = draft.requiresProration === true || draft.requiresProration === "true";
        if (draft.requiresProration) {
            draft.dealTypeValue = CROSS_SALE_DEAL_TYPE;
        }
        draft.firstContractOptionValue = Number(draft.firstContractOptionValue || 0) || deriveFirstContractValue(draft.dealTypeValue);
        const renewalSuggestion = buildRenewalSuggestion(draft);
        if (!draft.renewalDateValue || draft.renewalDateValue === draft.renewalAutoValue) {
            draft.renewalDateValue = renewalSuggestion.value || "";
        }
        draft.renewalAutoValue = renewalSuggestion.value || "";
        draft.renewalMode = draft.renewalDateValue ? renewalSuggestion.mode || "" : (renewalSuggestion.mode || "");
        draft.renewalHint = renewalSuggestion.hint || "";
        draft.autoBillOptionValue = normalizeSelectValue(draft.autoBillOptionValue, -1);
        draft.billingDay = draft.autoBillOptionValue === AUTO_BILL_YES_VALUE
            ? (Number(draft.billingDay || 0) || deriveBillingDayValue(draft.renewalDateValue, draft.scenarioEndDateValue, draft.contractStartDateValue))
            : 0;
        return draft;
    }

    function syncDealTypeAvailability() {
        const draft = state.activeDraft;
        const requiresProration = Boolean(draft?.requiresProration);

        if (dealTypeSelect) {
            dealTypeSelect.disabled = requiresProration;
            if (requiresProration) {
                dealTypeSelect.value = String(CROSS_SALE_DEAL_TYPE);
            }
        }

        if (scenarioStartDateInput) {
            scenarioStartDateInput.disabled = !requiresProration;
        }
        syncProrationControls();

        if (dealTypeHelp) {
            dealTypeHelp.textContent = requiresProration
                ? "Cuando el negocio tiene prorrateo, el tipo negocio se fija automaticamente en CrossSale."
                : "";
        }
    }

    function syncBillingDayAvailability() {
        const draft = state.activeDraft;
        const rawAutoBillValue = autoBillSelect?.value;
        const autoBillValue = rawAutoBillValue === ""
            ? normalizeSelectValue(draft?.autoBillOptionValue, -1)
            : Number(rawAutoBillValue || -1);
        const isAutoBillEnabled = autoBillValue === AUTO_BILL_YES_VALUE;
        const derivedBillingDay = draft
            ? deriveBillingDayValue(renewalDateInput?.value || draft.renewalDateValue, draft.scenarioEndDateValue, draft.contractStartDateValue)
            : 0;

        if (billingDayInput) {
            billingDayInput.disabled = !isAutoBillEnabled;
            billingDayInput.required = false;
            if (!isAutoBillEnabled) {
                billingDayInput.value = "";
            } else if (!billingDayInput.value && derivedBillingDay > 0) {
                billingDayInput.value = String(derivedBillingDay);
            }
        }

        if (billingDayHelp) {
            billingDayHelp.textContent = isAutoBillEnabled
                ? "Si lo dejas vacio, se tomara automaticamente el dia de la fecha de renovacion."
                : "Al guardar se enviara un correo automatico a facturacion para gestionar este negocio.";
        }
    }

    function syncRenewalDateHint() {
        if (!renewalDateHelp) {
            return;
        }

        const draft = state.activeDraft;
        if (!draft) {
            renewalDateHelp.textContent = "";
            return;
        }

        const renewalValue = renewalDateInput?.value || draft.renewalDateValue || "";
        if (renewalValue) {
            renewalDateHelp.textContent = `Renovacion calculada: ${formatDateDisplay(renewalValue)}`;
            return;
        }

        renewalDateHelp.textContent = draft.renewalHint || "";
    }

    function optionLabel(map, value) {
        if (value === null || value === undefined || value === "") {
            return "";
        }
        return map.get(String(value)) || String(value);
    }

    function productLineLabel(value) {
        if (value === null || value === undefined || value === "") {
            return "";
        }

        const lineLabel = optionMaps.line.get(String(value));
        if (lineLabel) {
            return lineLabel;
        }

        return optionLabel(optionMaps.productLine, value);
    }

    function setStatus(type, message) {
        if (!statusBanner) {
            return;
        }

        if (!message) {
            statusBanner.className = "scores-status";
            statusBanner.textContent = "";
            return;
        }

        statusBanner.className = `scores-status show ${type}`;
        statusBanner.textContent = message;
    }

    function setModalStatus(type, message) {
        if (!verifyModalStatus) {
            return;
        }

        if (!message) {
            verifyModalStatus.className = "scores-modal__status";
            verifyModalStatus.textContent = "";
            return;
        }

        verifyModalStatus.className = `scores-modal__status show ${type}`;
        verifyModalStatus.textContent = message;
    }

    function setCloseReviewStatus(type, message) {
        if (!closeMonthReviewStatus) {
            return;
        }

        if (!message) {
            closeMonthReviewStatus.className = "scores-modal__status";
            closeMonthReviewStatus.textContent = "";
            return;
        }

        closeMonthReviewStatus.className = `scores-modal__status show ${type}`;
        closeMonthReviewStatus.textContent = message;
    }

    function formatErrorMessage(error, fallbackMessage) {
        if (!error) {
            return fallbackMessage;
        }

        const parts = [];
        const message = typeof error.message === "string" ? error.message.trim() : "";
        const detail = typeof error.detail === "string" ? error.detail.trim() : "";
        const traceId = typeof error.traceId === "string" ? error.traceId.trim() : "";

        if (message) {
            parts.push(message);
        }

        if (detail && detail !== message) {
            parts.push(`Detalle: ${detail}`);
        }

        if (traceId) {
            parts.push(`TraceId: ${traceId}`);
        }

        return parts.length ? parts.join(" | ") : fallbackMessage;
    }

    async function extractErrorPayload(response, contentType) {
        if (contentType.includes("application/json")) {
            try {
                const payload = await response.json();
                if (typeof payload === "string") {
                    return { message: payload.trim() };
                }

                if (payload && typeof payload === "object") {
                    const validationDetails = payload.errors && typeof payload.errors === "object"
                        ? Object.entries(payload.errors)
                            .flatMap(([field, messages]) => {
                                const values = Array.isArray(messages) ? messages : [messages];
                                return values
                                    .filter(Boolean)
                                    .map(message => field ? `${field}: ${message}` : `${message}`);
                            })
                        : [];

                    const detailParts = [];
                    if (typeof payload.detail === "string" && payload.detail.trim()) {
                        detailParts.push(payload.detail.trim());
                    }
                    if (validationDetails.length) {
                        detailParts.push(validationDetails.join(" | "));
                    }

                    return {
                        message: (payload.message || payload.title || "").toString().trim(),
                        detail: detailParts.join(" | "),
                        traceId: (payload.traceId || "").toString().trim()
                    };
                }
            } catch {
                // Falls back to plain text parsing below.
            }
        }

        const message = (await response.text()).trim();
        return { message };
    }

    function toggleModalLoading(show) {
        verifyModalLoading?.classList.toggle("show", !!show);
        if (verifyScoreForm) {
            verifyScoreForm.hidden = !!show;
        }
    }

    function setLoading(loading) {
        state.isLoading = loading;
        refreshButton && (refreshButton.disabled = loading || state.isSaving || state.isClosingMonth);
        filterButtons.forEach(button => {
            button.disabled = loading || state.isSaving || state.isClosingMonth;
        });
        renderCloseMonthPanel();
    }

    function setSaving(saving) {
        state.isSaving = saving;
        submitVerifyScoreBtn && (submitVerifyScoreBtn.disabled = saving);
        refreshButton && (refreshButton.disabled = saving || state.isLoading || state.isClosingMonth);
        filterButtons.forEach(button => {
            button.disabled = saving || state.isLoading || state.isClosingMonth;
        });
        groupsContainer?.querySelectorAll(".verify-record-btn, .delete-record-btn, .toggle-group-btn").forEach(button => {
            button.disabled = saving || state.isLoading || state.isClosingMonth;
        });
        renderCloseMonthPanel();
    }

    function setRecalculating(recalculating) {
        state.isRecalculating = recalculating;
        recalculateVerifyScoreBtn && (recalculateVerifyScoreBtn.disabled = recalculating || state.isSaving);
        submitVerifyScoreBtn && (submitVerifyScoreBtn.disabled = state.isSaving || recalculating);
    }

    function setClosingMonth(closing) {
        state.isClosingMonth = closing;
        closeMonthButton && (closeMonthButton.disabled = closing);
        refreshButton && (refreshButton.disabled = closing || state.isLoading || state.isSaving);
        updateCloseMonthReviewSubmitState();
        renderCloseMonthPanel();
    }

    function populateSelect(selectElement, items, placeholder) {
        if (!selectElement) {
            return;
        }

        const optionsMarkup = [`<option value="">${escapeHtml(placeholder)}</option>`]
            .concat((Array.isArray(items) ? items : []).map(item => (
                `<option value="${escapeHtml(item.value ?? item.Value)}">${escapeHtml(item.label ?? item.Label)}</option>`
            )))
            .join("");

        selectElement.innerHTML = optionsMarkup;
    }

    function getGroupKey(group) {
        const baseKey = group.clientId ? `id:${group.clientId}` : `name:${group.clientName}`;
        const sectionKey = group._sectionKey || group.sectionKey || "";
        return sectionKey ? `${sectionKey}:${baseKey}` : baseKey;
    }

    function rebuildIndexes(board) {
        state.recordMap = new Map();

        if (!board || !Array.isArray(board.groups)) {
            return;
        }

        board.groups.forEach(group => {
            (group.records || []).forEach(record => {
                state.recordMap.set(record.recordId, record);
            });
        });
    }

    function setFilterButtonState() {
        filterButtons.forEach(button => {
            button.classList.toggle("active", button.dataset.filter === state.filter);
        });
    }

    function updateSummary(board) {
        const safeBoard = board || {};
        summaryClients && (summaryClients.textContent = formatNumber(safeBoard.clientsCount));
        summaryRecords && (summaryRecords.textContent = formatNumber(safeBoard.recordsCount));
        summaryProducts && (summaryProducts.textContent = formatNumber(safeBoard.productLinesCount));
        summaryScore && (summaryScore.textContent = formatScoreValue(safeBoard.totalScore));
        summaryCommission && (summaryCommission.textContent = formatNumber(safeBoard.totalCommission));
        summaryAnnualValue && (summaryAnnualValue.textContent = formatNumber(safeBoard.totalValue ?? safeBoard.totalAnnualValue));
    }

    function isRenewalRecord(record) {
        return RENEWAL_DEAL_TYPES.has(Number(record?.dealTypeValue || 0));
    }

    function sortScoreRecords(records) {
        return [...records].sort((a, b) => {
            const dateCompare = (a.contractStartDateValue || "").localeCompare(b.contractStartDateValue || "", "es", { sensitivity: "base" });
            if (dateCompare !== 0) {
                return dateCompare;
            }

            const clientCompare = (a.clientName || "").localeCompare(b.clientName || "", "es", { sensitivity: "base" });
            if (clientCompare !== 0) {
                return clientCompare;
            }

            return (a.offer || "").localeCompare(b.offer || "", "es", { sensitivity: "base" });
        });
    }

    function sumRecords(records, selector) {
        return records.reduce((total, record) => total + toNumber(selector(record), 0), 0);
    }

    function cloneGroupForSection(group, records, sectionKey) {
        const orderedRecords = sortScoreRecords(records);
        return {
            ...group,
            _sectionKey: sectionKey,
            recordCount: orderedRecords.length,
            productLinesCount: sumRecords(orderedRecords, record => record.productLinesCount),
            totalCommission: sumRecords(orderedRecords, record => record.commission),
            totalScore: sumRecords(orderedRecords, record => record.score),
            totalMonthlyValue: sumRecords(orderedRecords, record => record.monthlyValue),
            totalValue: sumRecords(orderedRecords, record => record.totalValue ?? record.annualValue),
            totalAnnualValue: sumRecords(orderedRecords, record => record.totalValue ?? record.annualValue),
            allVerified: orderedRecords.length > 0 && orderedRecords.every(record => record.isVerified),
            salesPerson: orderedRecords.find(record => record.salesPerson)?.salesPerson || group.salesPerson || "Sin vendedor",
            records: orderedRecords
        };
    }

    function summarizeSection(section) {
        const groups = section.groups || [];
        return {
            clientsCount: groups.length,
            recordsCount: sumRecords(groups, group => group.recordCount),
            productLinesCount: sumRecords(groups, group => group.productLinesCount),
            totalScore: sumRecords(groups, group => group.totalScore),
            totalCommission: sumRecords(groups, group => group.totalCommission),
            totalValue: sumRecords(groups, group => group.totalValue ?? group.totalAnnualValue)
        };
    }

    function buildScoreSections(board) {
        const baseSections = [
            {
                key: "new-business",
                title: "Negocios nuevos",
                description: "ClienteNuevo y CrossSale."
            },
            {
                key: "renewals",
                title: "Renovaciones",
                description: "Renovacion 1 vez, 2 veces y 3 veces o mas."
            }
        ];

        const sections = baseSections.map(section => ({ ...section, groups: [] }));
        const newBusinessSection = sections[0];
        const renewalsSection = sections[1];

        (Array.isArray(board?.groups) ? board.groups : []).forEach(group => {
            const records = Array.isArray(group.records) ? group.records : [];
            const newBusinessRecords = records.filter(record => !isRenewalRecord(record));
            const renewalRecords = records.filter(isRenewalRecord);

            if (newBusinessRecords.length) {
                newBusinessSection.groups.push(cloneGroupForSection(group, newBusinessRecords, newBusinessSection.key));
            }

            if (renewalRecords.length) {
                renewalsSection.groups.push(cloneGroupForSection(group, renewalRecords, renewalsSection.key));
            }
        });

        sections.forEach(section => {
            section.summary = summarizeSection(section);
        });

        return sections;
    }

    function renderMetaChip(label, value) {
        if (!value) {
            return "";
        }

        return `
            <div class="scores-detail__chip">
                <span class="scores-detail__label">${escapeHtml(label)}</span>
                <span>${escapeHtml(value)}</span>
            </div>
        `;
    }

    function buildOfferUrl(recordId) {
        return `${app.dataset.offerUrl}?recordId=${encodeURIComponent(recordId)}`;
    }

    function renderOfferCell(record) {
        if (!record.hasOffer) {
            return '<span class="scores-empty-cell">-</span>';
        }

        const label = record.offerFileName || record.offer || "Descargar oferta";
        return `
            <a class="scores-offer-link"
               href="${escapeHtml(buildOfferUrl(record.recordId))}"
               title="${escapeHtml(label)}"
               aria-label="Descargar oferta">
                <svg viewBox="0 0 24 24" aria-hidden="true" focusable="false">
                    <path d="M12 3a1 1 0 0 1 1 1v8.59l2.3-2.29a1 1 0 1 1 1.4 1.41l-4 3.99a1 1 0 0 1-1.4 0l-4-3.99a1 1 0 0 1 1.4-1.41L11 12.59V4a1 1 0 0 1 1-1Zm-7 14a1 1 0 0 1 1 1v1h12v-1a1 1 0 1 1 2 0v2a1 1 0 0 1-1 1H5a1 1 0 0 1-1-1v-2a1 1 0 0 1 1-1Z"/>
                </svg>
            </a>
        `;
    }

    function renderProductLines(record) {
        const lines = Array.isArray(record.productLines) ? record.productLines : [];
        if (lines.length === 0) {
            return '<div class="scores-product-empty">No se detectaron lineas de productos en el registro.</div>';
        }

        return `
            <table class="scores-product-table">
                <thead>
                    <tr>
                        <th>Producto</th>
                        <th class="text-end">Cantidad</th>
                        <th class="text-end">Costo UND</th>
                        <th class="text-end">Margen %</th>
                        <th class="text-end">Duracion</th>
                        <th class="text-end">Venta UND</th>
                        <th class="text-end">Venta mensual</th>
                        <th class="text-end">Venta total</th>
                    </tr>
                </thead>
                <tbody>
                    ${lines.map(line => `
                        <tr>
                            <td>
                                <div class="scores-product-table__name">${escapeHtml(line.productName || "Producto sin nombre")}</div>
                                <div class="scores-product-table__id">${escapeHtml(line.productId || line.lineId || "")}</div>
                            </td>
                            <td class="text-end">${formatNumber(line.quantity)}</td>
                            <td class="text-end">${formatNumber(line.costUnit)}</td>
                            <td class="text-end">${formatPercent(line.marginPercent)}</td>
                            <td class="text-end">${formatNumber(line.contractMonths)}m</td>
                            <td class="text-end">${formatNumber(line.monthlyUnitValue)}</td>
                            <td class="text-end">${formatNumber(line.monthlyValue)}</td>
                            <td class="text-end">${formatNumber(line.totalValue ?? line.annualValue)}</td>
                        </tr>
                    `).join("")}
                </tbody>
            </table>
        `;
    }

    function renderRecordRows(record) {
        const canDelete = !record.isVerified && !record.isClosedForActivePeriod;
        const deleteButton = canDelete
            ? `<button type="button" class="btn btn-sm btn-outline-danger delete-record-btn" data-record-id="${escapeHtml(record.recordId)}" title="Eliminar registro pendiente">Eliminar</button>`
            : "";

        return `
            <tr class="scores-record-row" data-record-id="${escapeHtml(record.recordId)}">
                <td>
                    <div class="scores-cell-main">${escapeHtml(record.contractStartDateDisplay || "")}</div>
                    <div class="scores-cell-sub">${escapeHtml(record.contractType || "Sin tipo")}</div>
                </td>
                <td class="text-center">${renderOfferCell(record)}</td>
                <td>
                    <div class="scores-cell-main">${escapeHtml(record.salesPerson || "Sin vendedor")}</div>
                    <div class="scores-cell-sub">${escapeHtml(`${formatNumber(record.productLinesCount)} lineas`)}</div>
                </td>
                <td>
                    <div class="scores-cell-main scores-cell-main--proration">${escapeHtml(record.prorationText || "No")}</div>
                </td>
                <td class="text-end"><div class="scores-cell-main">${formatScoreValue(record.score)}</div></td>
                <td class="text-end"><div class="scores-cell-main">${formatNumber(record.commission)}</div></td>
                <td class="text-end"><div class="scores-cell-main">${formatNumber(record.monthlyValue)}</div></td>
                <td class="text-end"><div class="scores-cell-main">${formatNumber(record.totalValue ?? record.annualValue)}</div></td>
                <td class="text-end">
                    <div class="d-flex justify-content-end gap-2 flex-wrap">
                        <button type="button" class="btn btn-sm ${record.isVerified ? "btn-outline-primary" : "btn-primary"} verify-record-btn" data-record-id="${escapeHtml(record.recordId)}">
                            ${record.isVerified ? "Editar" : "Verificar"}
                        </button>
                        ${deleteButton}
                    </div>
                </td>
                <td class="text-center">
                    <span class="scores-verified ${record.isVerified ? "scores-verified--yes" : ""}">${record.isVerified ? "OK" : ""}</span>
                </td>
            </tr>
        `;
    }

    function renderGroupMetrics(group) {
        return `
            <div class="scores-group__body">
                <div class="scores-group__summary">
                    <div class="scores-group__metric">
                        <span class="scores-group__metric-label">Aprovisionamientos</span>
                        <span class="scores-group__metric-value">${formatNumber(group.recordCount)}</span>
                    </div>
                    <div class="scores-group__metric">
                        <span class="scores-group__metric-label">Productos</span>
                        <span class="scores-group__metric-value">${formatNumber(group.productLinesCount)}</span>
                    </div>
                    <div class="scores-group__metric">
                        <span class="scores-group__metric-label">Puntaje</span>
                        <span class="scores-group__metric-value">${formatScoreValue(group.totalScore)}</span>
                    </div>
                    <div class="scores-group__metric">
                        <span class="scores-group__metric-label">Comision</span>
                        <span class="scores-group__metric-value">${formatNumber(group.totalCommission)}</span>
                    </div>
                    <div class="scores-group__metric">
                        <span class="scores-group__metric-label">Venta mensual</span>
                        <span class="scores-group__metric-value">${formatNumber(group.totalMonthlyValue)}</span>
                    </div>
                    <div class="scores-group__metric">
                        <span class="scores-group__metric-label">Venta total</span>
                        <span class="scores-group__metric-value">${formatNumber(group.totalValue ?? group.totalAnnualValue)}</span>
                    </div>
                </div>

                <div class="scores-table-wrap">
                    <table class="table scores-table">
                        <thead>
                            <tr>
                                <th>Inicio contrato</th>
                                <th class="text-center">Oferta</th>
                                <th>Vendedor</th>
                                <th>Prorrateo</th>
                                <th class="text-end">Puntaje</th>
                                <th class="text-end">Comision</th>
                                <th class="text-end">Venta mensual</th>
                                <th class="text-end">Venta total</th>
                                <th class="text-end">Acciones</th>
                                <th class="text-center">Verificado</th>
                            </tr>
                        </thead>
                        <tbody>${(group.records || []).map(renderRecordRows).join("")}</tbody>
                    </table>
                </div>
            </div>
        `;
    }

    function renderGroupArticle(group) {
        const groupKey = getGroupKey(group);
        const isExpanded = state.expandedGroups.has(groupKey);
        return `
            <article class="scores-group ${isExpanded ? "scores-group--expanded" : "scores-group--collapsed"}" data-group-key="${escapeHtml(groupKey)}">
                <div class="scores-group__header">
                    <div class="scores-group__header-main">
                        <h2 class="scores-group__title">${escapeHtml(group.clientName || "Cliente sin asignar")}</h2>
                        <div class="scores-group__compact-line">
                            <span class="scores-group__salesperson">${escapeHtml(group.salesPerson || "Sin vendedor")}</span>
                            ${group.allVerified ? '<span class="scores-group__complete">Verificado completo</span>' : ""}
                        </div>
                        ${isExpanded ? `<p class="scores-group__subtitle">${formatNumber(group.recordCount)} aprovisionamientos y ${formatNumber(group.productLinesCount)} productos.</p>` : ""}
                    </div>
                    <button type="button" class="btn btn-sm btn-outline-secondary toggle-group-btn" data-group-key="${escapeHtml(groupKey)}">
                        ${isExpanded ? "Resumir" : "Desplegar"}
                    </button>
                </div>
                ${isExpanded ? renderGroupMetrics(group) : ""}
            </article>
        `;
    }

    function renderScoreSection(section) {
        const summary = section.summary || {};
        const groups = Array.isArray(section.groups) ? section.groups : [];
        return `
            <section class="scores-section" data-section-key="${escapeHtml(section.key)}">
                <div class="scores-section__header">
                    <div>
                        <h2 class="scores-section__title">${escapeHtml(section.title)}</h2>
                        <p class="scores-section__subtitle">${escapeHtml(section.description)}</p>
                    </div>
                    <div class="scores-section__meta">
                        <span>${formatNumber(summary.recordsCount)} aprovisionamientos</span>
                        <span>${formatNumber(summary.productLinesCount)} productos</span>
                        <span>Puntaje ${formatScoreValue(summary.totalScore)}</span>
                        <span>Venta ${formatNumber(summary.totalValue)}</span>
                    </div>
                </div>
                <div class="scores-section__groups">
                    ${groups.length
                        ? groups.map(renderGroupArticle).join("")
                        : '<div class="scores-section__empty">No hay registros en esta seccion.</div>'}
                </div>
            </section>
        `;
    }

    function renderGroups(board) {
        if (!groupsContainer) {
            return;
        }

        const groups = Array.isArray(board?.groups) ? board.groups : [];
        if (groups.length === 0) {
            groupsContainer.innerHTML = `
                <div class="scores-empty">
                    <h3 class="h5 mb-2">No hay registros para este periodo.</h3>
                    <p class="mb-0">Prueba con otro filtro o actualiza nuevamente la consulta.</p>
                </div>
            `;
            bindGroupEvents();
            renderCloseMonthPanel();
            return;
        }

        groupsContainer.innerHTML = buildScoreSections(board).map(renderScoreSection).join("");

        bindGroupEvents();
        renderCloseMonthPanel();
    }

    function renderCloseMonthPanel() {
        if (!closeMonthButton || !closeMonthSummaryText) {
            return;
        }

        const board = state.board;
        if (!board) {
            closeMonthButton.disabled = true;
            undoCloseMonthButton && (undoCloseMonthButton.disabled = true);
            closeMonthSummaryText.textContent = "Carga un periodo para revisar si el cierre mensual ya puede ejecutarse.";
            renderCloseMonthLogs();
            return;
        }

        if (!board.supportsMonthClose) {
            closeMonthButton.disabled = true;
            undoCloseMonthButton && (undoCloseMonthButton.disabled = true);
            closeMonthSummaryText.textContent = "El cierre de mes solo se habilita en vistas mensuales para evitar consolidaciones ambiguas.";
            renderCloseMonthLogs();
            return;
        }

        if (!board.recordsCount) {
            closeMonthButton.disabled = true;
            undoCloseMonthButton && (undoCloseMonthButton.disabled = true);
            closeMonthSummaryText.textContent = `No hay registros para consolidar en ${board.monthClosePeriodLabel || "el periodo actual"}.`;
            renderCloseMonthLogs();
            return;
        }

        if (board.verifiedRecordsCount < board.recordsCount) {
            closeMonthButton.disabled = true;
            undoCloseMonthButton && (undoCloseMonthButton.disabled = !board.canUndoMonthClose || state.isClosingMonth || state.isLoading || state.isSaving);
            closeMonthSummaryText.textContent = `${board.monthClosePeriodLabel}: faltan ${board.recordsCount - board.verifiedRecordsCount} lineas por verificar antes del cierre.`;
            renderCloseMonthLogs();
            return;
        }

        if (board.closedRecordsCount >= board.recordsCount) {
            closeMonthButton.disabled = true;
            undoCloseMonthButton && (undoCloseMonthButton.disabled = !board.canUndoMonthClose || state.isClosingMonth || state.isLoading || state.isSaving);
            closeMonthSummaryText.textContent = `${board.monthClosePeriodLabel}: todas las lineas visibles ya quedaron consolidadas en sales performance.${board.undoMonthCloseLabel ? ` ${board.undoMonthCloseLabel}` : ""}`;
            renderCloseMonthLogs();
            return;
        }

        closeMonthButton.disabled = state.isClosingMonth || state.isLoading || state.isSaving;
        undoCloseMonthButton && (undoCloseMonthButton.disabled = !board.canUndoMonthClose || state.isClosingMonth || state.isLoading || state.isSaving);
        closeMonthSummaryText.textContent = `${board.monthClosePeriodLabel}: ${board.verifiedRecordsCount}/${board.recordsCount} lineas verificadas y ${board.closedRecordsCount} ya consolidadas.${board.undoMonthCloseLabel ? ` ${board.undoMonthCloseLabel}` : ""}`;
        renderCloseMonthLogs();
    }

    function renderCloseMonthLogs() {
        if (!closeMonthLogList || !closeMonthLogEmpty) {
            return;
        }

        const logs = Array.isArray(state.lastCloseResult?.logs) ? state.lastCloseResult.logs : [];
        if (!logs.length) {
            closeMonthLogEmpty.style.display = "";
            closeMonthLogList.innerHTML = "";
            return;
        }

        closeMonthLogEmpty.style.display = "none";
        closeMonthLogList.innerHTML = logs.map(entry => `
            <article class="scores-close-log__entry ${escapeHtml(entry.level || "info")}">
                <div class="scores-close-log__title">${escapeHtml(entry.clientName || "Registro")} ${entry.productName ? `| ${escapeHtml(entry.productName)}` : ""}</div>
                <div class="scores-close-log__text">${escapeHtml(entry.message || "")}</div>
                ${entry.finalState ? `<div class="scores-close-log__text">${escapeHtml(entry.finalState)}</div>` : ""}
                ${entry.detail ? `<div class="scores-close-log__text"><strong>Detalle:</strong> ${escapeHtml(entry.detail)}</div>` : ""}
            </article>
        `).join("");
    }

    function normalizeCloseMonthPreview(preview) {
        return {
            ...(preview || {}),
            lines: (Array.isArray(preview?.lines) ? preview.lines : []).map(line => ({
                ...line,
                selected: line.selectedByDefault === true
            }))
        };
    }

    function getCloseMonthPreviewLines() {
        return Array.isArray(state.closeMonthPreview?.lines) ? state.closeMonthPreview.lines : [];
    }

    function updateCloseMonthReviewSubmitState() {
        if (!submitCloseMonthReviewBtn) {
            return;
        }

        const hasLines = getCloseMonthPreviewLines().length > 0;
        submitCloseMonthReviewBtn.disabled = state.isClosingMonth || !hasLines || !closeMonthReviewConfirmCheck?.checked;
    }

    function renderCloseMonthReviewList(lines, container, emptyState) {
        if (!container || !emptyState) {
            return;
        }

        if (!lines.length) {
            emptyState.style.display = "";
            container.innerHTML = "";
            return;
        }

        emptyState.style.display = "none";
        container.innerHTML = lines.map(line => {
            const locked = line.canChangeSelection === false;
            const warnings = Array.isArray(line.warnings) ? line.warnings : [];
            const predictedAction = line.predictedAction === "increment"
                ? `Incremento a ${formatNumber(line.finalQuantity)}`
                : `Nueva linea con ${formatNumber(line.finalQuantity)}`;

            return `
                <article class="close-review-item ${warnings.length ? "close-review-item--warning" : ""} ${locked ? "close-review-item--locked" : ""}">
                    <div class="close-review-item__top">
                        <label class="close-review-item__toggle">
                            <input type="checkbox" class="close-review-toggle" data-line-key="${escapeHtml(line.lineKey || "")}" ${line.selected ? "checked" : ""} ${locked ? "disabled" : ""} />
                            <span>${escapeHtml(line.clientName || "Cliente")} | ${escapeHtml(line.productName || "Producto")}</span>
                        </label>
                        <span class="close-review-chip ${line.predictedAction === "increment" ? "close-review-chip--muted" : "close-review-chip--success"}">${escapeHtml(predictedAction)}</span>
                    </div>
                    <div class="close-review-item__meta">
                        <span class="close-review-chip">Cantidad: ${escapeHtml(formatNumber(line.quantity))}</span>
                        <span class="close-review-chip">AutoBill: ${escapeHtml(optionLabel(optionMaps.autoBill, line.autoBillOptionValue))}</span>
                        <span class="close-review-chip">Contrato: ${escapeHtml(optionLabel(optionMaps.contractType, line.contractTypeOptionValue))}</span>
                        <span class="close-review-chip">Linea: ${escapeHtml(productLineLabel(line.productLineOptionValue))}</span>
                        <span class="close-review-chip">IVA: ${escapeHtml(optionLabel(optionMaps.hasVat, line.hasVatOptionValue))}</span>
                        <span class="close-review-chip">Facturacion: ${escapeHtml(line.billingDay ? `Dia ${line.billingDay}` : "Pendiente")}</span>
                        <span class="close-review-chip">Venta UND USD: ${escapeHtml(formatNumber(line.unitSaleUsd))}</span>
                        <span class="close-review-chip">Renovacion: ${escapeHtml(line.renewalDateDisplay || line.renewalDateValue || "Pendiente")}</span>
                    </div>
                    <div class="close-review-item__reason">${escapeHtml(line.reason || "")}</div>
                    ${warnings.length ? `<div class="close-review-item__warnings">${escapeHtml(warnings.join(" "))}</div>` : ""}
                </article>
            `;
        }).join("");
    }

    function renderCloseMonthReview() {
        const preview = state.closeMonthPreview;
        if (!preview) {
            closeMonthReviewSummary && (closeMonthReviewSummary.innerHTML = "");
            renderCloseMonthReviewList([], closeMonthReviewIncludedList, closeMonthReviewIncludedEmpty);
            renderCloseMonthReviewList([], closeMonthReviewExcludedList, closeMonthReviewExcludedEmpty);
            updateCloseMonthReviewSubmitState();
            return;
        }

        const lines = getCloseMonthPreviewLines();
        const selectedLines = lines.filter(line => line.selected);
        const excludedLines = lines.filter(line => !line.selected);
        closeMonthReviewTitle && (closeMonthReviewTitle.textContent = `Cerrar ${preview.periodLabel || "mes"}`);
        closeMonthReviewSubtitle && (closeMonthReviewSubtitle.textContent = preview.message || "");
        closeMonthReviewSummary && (closeMonthReviewSummary.innerHTML = `
            <article class="close-review-summary__card">
                <span class="scores-summary__label">Lineas revisadas</span>
                <strong class="close-review-summary__value">${formatNumber(lines.length)}</strong>
            </article>
            <article class="close-review-summary__card">
                <span class="scores-summary__label">Se enviaran</span>
                <strong class="close-review-summary__value">${formatNumber(selectedLines.length)}</strong>
            </article>
            <article class="close-review-summary__card">
                <span class="scores-summary__label">No se enviaran</span>
                <strong class="close-review-summary__value">${formatNumber(excludedLines.length)}</strong>
            </article>
            <article class="close-review-summary__card">
                <span class="scores-summary__label">Warnings</span>
                <strong class="close-review-summary__value">${formatNumber(lines.filter(line => Array.isArray(line.warnings) && line.warnings.length).length)}</strong>
            </article>
        `);

        renderCloseMonthReviewList(selectedLines, closeMonthReviewIncludedList, closeMonthReviewIncludedEmpty);
        renderCloseMonthReviewList(excludedLines, closeMonthReviewExcludedList, closeMonthReviewExcludedEmpty);
        (closeMonthReviewModalElement ? Array.from(closeMonthReviewModalElement.querySelectorAll(".close-review-toggle")) : []).forEach(toggle => {
            toggle.addEventListener("change", event => {
                const lineKey = event.currentTarget.dataset.lineKey;
                if (!lineKey) {
                    return;
                }

                const line = getCloseMonthPreviewLines().find(item => item.lineKey === lineKey);
                if (!line || line.canChangeSelection === false) {
                    return;
                }

                line.selected = event.currentTarget.checked;
                renderCloseMonthReview();
            });
        });
        updateCloseMonthReviewSubmitState();
    }

    function bindGroupEvents() {
        groupsContainer.querySelectorAll(".toggle-group-btn").forEach(button => {
            button.addEventListener("click", event => {
                const groupKey = event.currentTarget.dataset.groupKey;
                if (!groupKey) {
                    return;
                }

                if (state.expandedGroups.has(groupKey)) {
                    state.expandedGroups.delete(groupKey);
                } else {
                    state.expandedGroups.add(groupKey);
                }

                renderGroups(state.board);
            });
        });

        groupsContainer.querySelectorAll(".verify-record-btn").forEach(button => {
            button.addEventListener("click", event => {
                const recordId = event.currentTarget.dataset.recordId;
                if (!recordId) {
                    return;
                }

                openVerifyModal(recordId);
            });
        });

        groupsContainer.querySelectorAll(".delete-record-btn").forEach(button => {
            button.addEventListener("click", async event => {
                const recordId = event.currentTarget.dataset.recordId;
                if (!recordId || state.isLoading || state.isSaving || state.isClosingMonth) {
                    return;
                }

                const record = state.recordMap.get(recordId);
                if (record?.isVerified) {
                    setStatus("error", "El registro ya fue verificado y no se puede eliminar desde esta vista.");
                    return;
                }

                const clientLabel = record?.clientName ? ` de ${record.clientName}` : "";
                if (!window.confirm(`Eliminar el registro pendiente${clientLabel}? Esta accion no se puede deshacer.`)) {
                    return;
                }

                const button = event.currentTarget;
                button.disabled = true;
                try {
                    await deleteScoreRecord(recordId);
                } finally {
                    button.disabled = false;
                }
            });
        });
    }

    async function deleteScoreRecord(recordId) {
        if (!app.dataset.deleteUrl) {
            setStatus("error", "No se encontro la ruta para eliminar registros.");
            return;
        }

        setSaving(true);
        setStatus("info", "Eliminando registro pendiente...");

        try {
            const result = await fetchJson(`${app.dataset.deleteUrl}?recordId=${encodeURIComponent(recordId)}`, {
                method: "DELETE"
            });

            await loadBoard();
            setStatus("success", result?.message || "El registro pendiente fue eliminado correctamente.");
        } catch (error) {
            console.error(error);
            setStatus("error", formatErrorMessage(error, "No fue posible eliminar el registro."));
        } finally {
            setSaving(false);
        }
    }

    async function fetchJson(url, requestOptions = {}) {
        const response = await fetch(url, {
            headers: {
                Accept: "application/json",
                ...(requestOptions.headers || {})
            },
            ...requestOptions
        });

        const contentType = response.headers.get("content-type") || "";
        if (!response.ok) {
            const errorPayload = await extractErrorPayload(response, contentType);
            const error = new Error(errorPayload.message || "No fue posible completar la solicitud.");
            error.detail = errorPayload.detail || "";
            error.traceId = errorPayload.traceId || "";
            error.status = response.status;
            throw error;
        }

        if (!contentType.includes("application/json")) {
            const message = await response.text();
            throw new Error(message || "La respuesta del servidor no fue valida.");
        }

        return response.json();
    }

    async function loadBoard() {
        setLoading(true);
        setFilterButtonState();
        setStatus("info", "Consultando puntajes en Dataverse...");

        try {
            const recordsUrl = `${app.dataset.recordsUrl}?filter=${encodeURIComponent(state.filter)}`;
            const board = await fetchJson(recordsUrl);
            state.board = board;
            rebuildIndexes(board);
            updateSummary(board);
            renderGroups(board);
            setStatus("", "");
        } catch (error) {
            console.error(error);
            state.board = null;
            rebuildIndexes(null);
            updateSummary(null);
            renderGroups(null);
            setStatus("error", formatErrorMessage(error, "No fue posible cargar los puntajes."));
        } finally {
            setLoading(false);
            setFilterButtonState();
        }
    }

    function normalizeDraft(detail) {
        const clientId = detail.clientId || "";
        const clientName = detail.clientName || "";
        return applyDraftDerivedDefaults({
            ...detail,
            clientId,
            clientName,
            billingDay: Number(detail.billingDay || 0),
            dealTypeValue: Number(detail.dealTypeValue || 0),
            requiresProration: detail.requiresProration === true || detail.requiresProration === "true",
            autoBillOptionValue: normalizeSelectValue(detail.autoBillOptionValue, -1),
            contractTypeOptionValue: normalizeSelectValue(detail.contractTypeOptionValue, -1),
            prorationClient: clientId ? { id: clientId, name: clientName } : null,
            prorationRenewalOptions: [],
            prorationRenewalError: "",
            prorationRenewalLoading: false,
            lines: (Array.isArray(detail.lines) ? detail.lines : []).map((line, index) => normalizeLine(line, index))
        });
    }

    function normalizeLine(line, index) {
        const normalized = {
            lineId: line.lineId || `line-${index + 1}`,
            productId: line.productId || "",
            productName: line.productName || "",
            lineType: line.lineType || optionLabel(optionMaps.line, line.lineOptionValue) || "Otro",
            lineOptionValue: Number(line.lineOptionValue || 645250004),
            hasVat: line.hasVatOptionValue > 0 ? Number(line.hasVatOptionValue) === 1 : Boolean(line.hasVat),
            hasVatOptionValue: line.hasVatOptionValue > 0 ? Number(line.hasVatOptionValue) : (Boolean(line.hasVat) ? 1 : 0),
            costUnit: line.costUnit,
            marginPercent: line.marginPercent,
            contractMonths: line.contractMonths,
            quantity: line.quantity,
            suggestedRetailPrice: roundMoney(line.suggestedRetailPrice || 0),
            acelerador: roundMoney(line.acelerador || 0),
            saleUnit: line.saleUnit,
            monthlyValue: line.monthlyValue,
            totalValue: line.totalValue
        };

        return recomputeVerifyLine(normalized, "margin");
    }

    function createEmptyLine() {
        return normalizeLine({
            lineId: `line-${Date.now()}`,
            productId: "",
            productName: "",
            lineType: "Otro",
            lineOptionValue: 645250004,
            hasVat: false,
            hasVatOptionValue: 0,
            costUnit: 0,
            marginPercent: 0,
            contractMonths: 12,
            quantity: 1,
            suggestedRetailPrice: 0,
            acelerador: 0
        }, 0);
    }

    function syncDraftHeaderInputs() {
        if (!state.activeDraft) {
            return null;
        }

        state.activeDraft.dealTypeValue = Number(dealTypeSelect?.value || 0);
        state.activeDraft.requiresProration = requiresProrationSelect?.value === "true";
        state.activeDraft.scenarioStartDateValue = scenarioStartDateInput?.value || "";
        state.activeDraft.scenarioEndDateValue = state.activeDraft.requiresProration
            ? (scenarioEndDateSelect?.value || "")
            : (scenarioEndDateInput?.value || "");
        state.activeDraft.firstContractOptionValue = firstContractSelect?.value === ""
            ? deriveFirstContractValue(state.activeDraft.dealTypeValue)
            : Number(firstContractSelect?.value || 0);
        state.activeDraft.verticalOptionValue = Number(verticalOptionSelect?.value || 0);
        state.activeDraft.billingDay = Number(billingDayInput?.value || 0);
        state.activeDraft.renewalDateValue = renewalDateInput?.value || "";
        state.activeDraft.autoBillOptionValue = autoBillSelect?.value === "" ? -1 : Number(autoBillSelect?.value || -1);
        state.activeDraft.contractTypeOptionValue = contractTypeSelect?.value === "" ? -1 : Number(contractTypeSelect?.value || -1);
        applyDraftDerivedDefaults(state.activeDraft);
        dealTypeSelect && (dealTypeSelect.value = String(state.activeDraft.dealTypeValue ?? ""));
        requiresProrationSelect && (requiresProrationSelect.value = state.activeDraft.requiresProration ? "true" : "false");
        scenarioStartDateInput && (scenarioStartDateInput.value = state.activeDraft.scenarioStartDateValue || "");
        scenarioEndDateInput && (scenarioEndDateInput.value = state.activeDraft.requiresProration ? "" : (state.activeDraft.scenarioEndDateValue || ""));
        scenarioEndDateSelect && (scenarioEndDateSelect.value = state.activeDraft.scenarioEndDateValue || "");
        firstContractSelect && (firstContractSelect.value = state.activeDraft.firstContractOptionValue > 0 ? String(state.activeDraft.firstContractOptionValue) : "");
        verticalOptionSelect && (verticalOptionSelect.value = state.activeDraft.verticalOptionValue ? String(state.activeDraft.verticalOptionValue) : "");
        renewalDateInput && (renewalDateInput.value = state.activeDraft.renewalDateValue || "");
        billingDayInput && (billingDayInput.value = state.activeDraft.billingDay ? String(state.activeDraft.billingDay) : "");
        autoBillSelect && (autoBillSelect.value = state.activeDraft.autoBillOptionValue >= 0 ? String(state.activeDraft.autoBillOptionValue) : "");
        contractTypeSelect && (contractTypeSelect.value = state.activeDraft.contractTypeOptionValue >= 0 ? String(state.activeDraft.contractTypeOptionValue) : "");
        syncDealTypeAvailability();
        syncBillingDayAvailability();
        syncRenewalDateHint();
        return state.activeDraft;
    }

    function renderVerifyMetaCards() {
        if (!verifyMetaCards || !state.activeDraft) {
            return;
        }

        const draft = state.activeDraft;
        const offerMarkup = draft.hasOffer
            ? `<a class="scores-offer-link scores-offer-link--inline" href="${escapeHtml(buildOfferUrl(draft.recordId))}">Descargar oferta</a>`
            : '<span class="scores-empty-cell">Sin oferta</span>';

        verifyMetaCards.innerHTML = `
            <article class="scores-verify-meta__card scores-verify-meta__card--single">
                <div class="scores-verify-meta__single-grid">
                    <div>
                        <span class="scores-verify-meta__label">Cliente</span>
                        <div class="scores-verify-meta__value">${escapeHtml(draft.clientName || "Sin cliente")}</div>
                    </div>
                    <div>
                        <span class="scores-verify-meta__label">Inicio contrato</span>
                        <div class="scores-verify-meta__value">${escapeHtml(draft.contractStartDateDisplay || "Sin fecha")}</div>
                    </div>
                    <div>
                        <span class="scores-verify-meta__label">Tipo negocio</span>
                        <div class="scores-verify-meta__value">${escapeHtml(optionLabel(optionMaps.dealType, draft.dealTypeValue) || "Sin definir")}</div>
                    </div>
                    <div>
                        <span class="scores-verify-meta__label">Vendedor</span>
                        <div class="scores-verify-meta__value">${escapeHtml(draft.salesPerson || "Sin vendedor")}</div>
                    </div>
                    <div>
                        <span class="scores-verify-meta__label">Oferta</span>
                        <div class="scores-verify-meta__value">${offerMarkup}</div>
                    </div>
                    <div>
                        <span class="scores-verify-meta__label">Prorrateo</span>
                        <div class="scores-verify-meta__value">${escapeHtml(draft.result?.prorationText || draft.prorationSummary || "No")}</div>
                    </div>
                </div>
            </article>
        `;
    }

    function renderVerifyLines() {
        if (!verifyLinesBody || !state.activeDraft) {
            return;
        }

        const lines = state.activeDraft.lines || [];
        if (!lines.length) {
            verifyLinesBody.innerHTML = `<tr><td colspan="9" class="scores-verify-empty">No hay lineas cargadas. Usa "Agregar linea" para crear una nueva.</td></tr>`;
            return;
        }

        const rows = lines.map((line, index) => {
            const lockCost = isModernWorkLine(line);
            const lockMonths = containsPrepaidOrYear(line.productName);

            return `
            <tr data-line-index="${index}">
                <td>
                    <div class="scores-verify-product">
                        <input type="text" class="form-control form-control-sm verify-product-input" value="${escapeHtml(line.productName)}" placeholder="Buscar producto..." autocomplete="off" />
                        <div class="scores-verify-product__search"></div>
                        <div class="scores-verify-product__meta">Tipo: ${escapeHtml(line.lineType || "Otro")} | IVA: ${line.hasVat ? "Si" : "No"} | Lookup: ${escapeHtml(line.productId || "pendiente")} | Sug.: ${formatNumber(line.suggestedRetailPrice)} | Acel.: ${formatNumber(line.acelerador)}${lockCost ? " | Costo fijo por ModernWork" : ""}${lockMonths ? " | Duracion fija 12 meses" : ""}</div>
                    </div>
                </td>
                <td><input type="number" step="0.01" min="0" class="form-control form-control-sm text-end verify-cost-input" value="${line.costUnit}" ${lockCost ? "disabled" : ""} /></td>
                <td><input type="number" step="0.01" class="form-control form-control-sm text-end verify-margin-input" value="${line.marginPercent}" /></td>
                <td><input type="number" step="1" min="1" class="form-control form-control-sm text-end verify-months-input" value="${line.contractMonths}" ${lockMonths ? "disabled" : ""} /></td>
                <td><input type="number" step="0.01" min="0" class="form-control form-control-sm text-end verify-sale-input" value="${line.saleUnit}" /></td>
                <td><input type="number" step="1" min="1" class="form-control form-control-sm text-end verify-qty-input" value="${line.quantity}" /></td>
                <td><input type="number" step="0.01" min="0" class="form-control form-control-sm text-end verify-monthly-input" value="${line.monthlyValue}" /></td>
                <td><input type="number" step="0.01" min="0" class="form-control form-control-sm text-end verify-total-input" value="${line.totalValue}" /></td>
                <td class="text-center"><button type="button" class="btn btn-sm btn-outline-danger verify-remove-line-btn">×</button></td>
            </tr>
        `;
        }).join("");

        const totals = lines.reduce((acc, line) => {
            acc.cost += Number(line.costUnit || 0) * Number(line.quantity || 0);
            acc.monthly += Number(line.monthlyValue || 0);
            acc.total += Number(line.totalValue || 0);
            return acc;
        }, { cost: 0, monthly: 0, total: 0 });

        verifyLinesBody.innerHTML = rows + `
            <tr class="scores-verify-totals">
                <td>Totales</td>
                <td class="text-end">${formatNumber(totals.cost)}</td>
                <td class="text-end">—</td>
                <td class="text-end">—</td>
                <td class="text-end">—</td>
                <td class="text-end">${formatNumber(lines.reduce((acc, line) => acc + Number(line.quantity || 0), 0))}</td>
                <td class="text-end">${formatNumber(totals.monthly)}</td>
                <td class="text-end">${formatNumber(totals.total)}</td>
                <td></td>
            </tr>
        `;

        bindVerifyLineEvents();
    }

    function renderVerifyDraft() {
        if (!state.activeDraft) {
            return;
        }

        const draft = state.activeDraft;
        applyDraftDerivedDefaults(draft);
        verifyModalTitle && (verifyModalTitle.textContent = `Verificar ${draft.clientName || "registro"}`);
        verifyModalSubtitle && (verifyModalSubtitle.textContent = `${draft.offer || "Sin oferta"} | Inicio ${draft.contractStartDateDisplay || "sin fecha"} | ${formatNumber(draft.lines.length)} lineas`);
        dealTypeSelect && (dealTypeSelect.value = String(draft.dealTypeValue ?? ""));
        requiresProrationSelect && (requiresProrationSelect.value = draft.requiresProration ? "true" : "false");
        scenarioStartDateInput && (scenarioStartDateInput.value = draft.scenarioStartDateValue || "");
        scenarioEndDateInput && (scenarioEndDateInput.value = draft.requiresProration ? "" : (draft.scenarioEndDateValue || ""));
        scenarioEndDateSelect && (scenarioEndDateSelect.value = draft.scenarioEndDateValue || "");
        firstContractSelect && (firstContractSelect.value = draft.firstContractOptionValue ? String(draft.firstContractOptionValue) : "");
        verticalOptionSelect && (verticalOptionSelect.value = draft.verticalOptionValue ? String(draft.verticalOptionValue) : "");
        billingDayInput && (billingDayInput.value = draft.billingDay ? String(draft.billingDay) : "");
        renewalDateInput && (renewalDateInput.value = draft.renewalDateValue || "");
        autoBillSelect && (autoBillSelect.value = draft.autoBillOptionValue >= 0 ? String(draft.autoBillOptionValue) : "");
        contractTypeSelect && (contractTypeSelect.value = draft.contractTypeOptionValue >= 0 ? String(draft.contractTypeOptionValue) : "");
        renderVerifyMetaCards();
        renderVerifyLines();
        renderVerificationResult(draft.result);
        syncDealTypeAvailability();
        syncBillingDayAvailability();
        syncRenewalDateHint();
        setModalStatus(draft.warningMessage ? "info" : "", draft.warningMessage || "");
        toggleModalLoading(false);
    }

    function renderVerificationResult(result) {
        verifyResultPoints && (verifyResultPoints.textContent = result ? formatScoreValue(result.points) : "—");
        verifyResultCommission && (verifyResultCommission.textContent = result ? formatNumber(result.commission) : "—");
        verifyResultProration && (verifyResultProration.textContent = result ? (result.prorationText || "No") : "—");
        verifyResultMonthly && (verifyResultMonthly.textContent = result ? formatNumber(result.totalMonthlySale) : "—");
        verifyResultTotal && (verifyResultTotal.textContent = result ? formatNumber(result.totalSale) : "—");
    }

    function markDraftDirty() {
        if (!state.activeDraft) {
            return;
        }

        applyDraftDerivedDefaults(state.activeDraft);
        renewalDateInput && (renewalDateInput.value = state.activeDraft.renewalDateValue || "");
        billingDayInput && (billingDayInput.value = state.activeDraft.billingDay ? String(state.activeDraft.billingDay) : "");
        renderVerifyMetaCards();
        syncProrationControls();
        syncBillingDayAvailability();
        syncRenewalDateHint();
        state.activeDraft.result = null;
        renderVerificationResult(null);
        setModalStatus("info", "Hay cambios pendientes por recalcular antes de guardar.");
    }

    async function openVerifyModal(recordId) {
        if (!verifyModal) {
            return;
        }

        state.activeRecordId = recordId;
        state.activeDraft = null;
        verifyModalTitle && (verifyModalTitle.textContent = "Cargando negocio...");
        verifyModalSubtitle && (verifyModalSubtitle.textContent = "Estamos trayendo la información editable desde Dataverse.");
        setModalStatus("", "");
        toggleModalLoading(true);
        verifyModal.show();

        try {
            const detailUrl = `${app.dataset.detailUrl}?recordId=${encodeURIComponent(recordId)}&filter=${encodeURIComponent(state.filter)}`;
            const detail = await fetchJson(detailUrl);
            state.activeDraft = normalizeDraft(detail);
            if (state.activeDraft?.requiresProration) {
                state.activeDraft.prorationRenewalLoading = true;
            }
            renderVerifyDraft();
            if (state.activeDraft?.requiresProration) {
                await ensureProrationRenewalOptions({ forceReload: true, preserveSelectedDate: true, silent: true });
            }
        } catch (error) {
            console.error(error);
            toggleModalLoading(false);
            setModalStatus("error", formatErrorMessage(error, "No fue posible cargar el detalle de verificacion."));
        }
    }

    function bindVerifyLineEvents() {
        if (!verifyLinesBody || !state.activeDraft) {
            return;
        }

        verifyLinesBody.querySelectorAll("tr[data-line-index]").forEach(row => {
            const index = Number(row.dataset.lineIndex || "-1");
            const line = state.activeDraft.lines[index];
            if (!line) {
                return;
            }

            const productInput = row.querySelector(".verify-product-input");
            const searchBox = row.querySelector(".scores-verify-product__search");
            const costInput = row.querySelector(".verify-cost-input");
            const marginInput = row.querySelector(".verify-margin-input");
            const monthsInput = row.querySelector(".verify-months-input");
            const saleInput = row.querySelector(".verify-sale-input");
            const qtyInput = row.querySelector(".verify-qty-input");
            const monthlyInput = row.querySelector(".verify-monthly-input");
            const totalInput = row.querySelector(".verify-total-input");
            const removeBtn = row.querySelector(".verify-remove-line-btn");

            const hideSuggestions = () => {
                searchBox && searchBox.classList.remove("show");
                if (searchBox) {
                    searchBox.innerHTML = "";
                }
            };

            const applyLineChange = source => {
                if (costInput && !costInput.disabled) {
                    line.costUnit = roundMoney(costInput.value);
                }

                if (marginInput) {
                    line.marginPercent = roundMoney(marginInput.value);
                }

                if (monthsInput && !monthsInput.disabled) {
                    line.contractMonths = normalizePositiveInteger(monthsInput.value, line.contractMonths || 12);
                }

                if (qtyInput) {
                    line.quantity = normalizePositiveInteger(qtyInput.value, line.quantity || 1);
                }

                if (saleInput) {
                    line.saleUnit = roundMoney(saleInput.value);
                }

                if (monthlyInput) {
                    line.monthlyValue = roundMoney(monthlyInput.value);
                }

                if (totalInput) {
                    line.totalValue = roundMoney(totalInput.value);
                }

                recomputeVerifyLine(line, source);
                renderVerifyLines();
                markDraftDirty();
            };

            productInput?.addEventListener("input", () => {
                line.productName = productInput.value;
                line.productId = "";
                markDraftDirty();
                hideSuggestions();

                const query = productInput.value.trim();
                if (query.length < 2 || !searchBox) {
                    return;
                }

                window.clearTimeout(line.__timerId);
                line.__timerId = window.setTimeout(async () => {
                    try {
                        const items = await fetchJson(`${app.dataset.productSearchUrl}?q=${encodeURIComponent(query)}`);
                        if (!Array.isArray(items) || !items.length) {
                            hideSuggestions();
                            return;
                        }

                        searchBox.innerHTML = items.map((item, itemIndex) => `
                            <div class="scores-verify-product__item" data-item-index="${itemIndex}">
                                <strong>${escapeHtml(item.description || "")}</strong>
                                <span>Compra: ${formatNumber(item.purchasePrice)} | Sugerido: ${formatNumber(item.suggestedRetailPrice)}</span>
                            </div>
                        `).join("");
                        searchBox.classList.add("show");
                        searchBox.dataset.items = JSON.stringify(items);
                    } catch {
                        hideSuggestions();
                    }
                }, 220);
            });

            productInput?.addEventListener("focus", () => {
                if (searchBox?.children.length) {
                    searchBox.classList.add("show");
                }
            });

            searchBox?.addEventListener("click", event => {
                const itemNode = event.target.closest(".scores-verify-product__item");
                if (!itemNode) {
                    return;
                }

                const items = JSON.parse(searchBox.dataset.items || "[]");
                const item = items[Number(itemNode.dataset.itemIndex || "-1")];
                if (!item) {
                    return;
                }

                line.productId = item.id || "";
                line.productName = item.description || "";
                line.suggestedRetailPrice = roundMoney(item.suggestedRetailPrice || 0);
                line.acelerador = roundMoney(item.acelerador || 0);
                if (isModernWorkLine(line)) {
                    line.costUnit = roundMoney(item.purchasePrice || 0);
                }
                productInput.value = line.productName;
                hideSuggestions();
                recomputeVerifyLine(line, "cost");
                renderVerifyLines();
                markDraftDirty();
            });

            document.addEventListener("click", event => {
                if (event.target === productInput || searchBox?.contains(event.target)) {
                    return;
                }
                hideSuggestions();
            }, { once: true });

            costInput?.addEventListener("change", () => applyLineChange("cost"));
            marginInput?.addEventListener("change", () => applyLineChange("margin"));
            monthsInput?.addEventListener("change", () => applyLineChange("months"));
            saleInput?.addEventListener("change", () => applyLineChange("sale"));
            qtyInput?.addEventListener("change", () => applyLineChange("quantity"));
            monthlyInput?.addEventListener("change", () => applyLineChange("monthly"));
            totalInput?.addEventListener("change", () => applyLineChange("total"));
            removeBtn?.addEventListener("click", () => {
                state.activeDraft.lines.splice(index, 1);
                renderVerifyLines();
                markDraftDirty();
            });
        });
    }

    function buildDraftPayload() {
        const draft = syncDraftHeaderInputs();
        if (!draft) {
            return null;
        }

        return {
            recordId: draft.recordId,
            businessId: draft.businessId || "",
            dealTypeValue: Number(draft.dealTypeValue || 0),
            requiresProration: Boolean(draft.requiresProration),
            scenarioStartDateValue: draft.scenarioStartDateValue || "",
            scenarioEndDateValue: draft.scenarioEndDateValue || "",
            firstContractOptionValue: Number(draft.firstContractOptionValue || 0),
            lineOptionValue: Number(draft.lineOptionValue || 0),
            verticalOptionValue: Number(draft.verticalOptionValue || 0),
            billingDay: Number(draft.billingDay || 0),
            renewalDateValue: draft.renewalDateValue || "",
            alignmentDateValue: "",
            hasVatOptionValue: Number(draft.hasVatOptionValue || 0),
            autoBillOptionValue: Number(draft.autoBillOptionValue),
            productLineOptionValue: Number(draft.productLineOptionValue || 0),
            contractTypeOptionValue: Number(draft.contractTypeOptionValue),
            lines: (draft.lines || []).map(line => ({
                lineId: line.lineId || "",
                productId: line.productId || "",
                productName: line.productName || "",
                lineType: line.lineType || "Otro",
                lineOptionValue: Number(line.lineOptionValue || 645250004),
                hasVat: Boolean(line.hasVat),
                hasVatOptionValue: Number(line.hasVatOptionValue || 0),
                costUnit: Number(line.costUnit || 0),
                marginPercent: Number(line.marginPercent || 0),
                contractMonths: Number(line.contractMonths || 0),
                quantity: Number(line.quantity || 0),
                suggestedRetailPrice: Number(line.suggestedRetailPrice || 0),
                acelerador: Number(line.acelerador || 0),
                saleUnit: Number(line.saleUnit || 0),
                monthlyValue: Number(line.monthlyValue || 0),
                totalValue: Number(line.totalValue || 0)
            }))
        };
    }

    async function recalculateVerification() {
        const payload = buildDraftPayload();
        if (!payload) {
            return;
        }

        setRecalculating(true);
        try {
            const result = await fetchJson(app.dataset.recalculateUrl, {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify(payload)
            });

            state.activeDraft.result = result;
            renderVerifyMetaCards();
            renderVerificationResult(result);
            setModalStatus("success", "Puntaje recalculado usando la logica de la calculadora.");
        } catch (error) {
            console.error(error);
            setModalStatus("error", formatErrorMessage(error, "No fue posible recalcular el puntaje."));
        } finally {
            setRecalculating(false);
        }
    }

    async function submitVerification() {
        const payload = buildDraftPayload();
        if (!payload) {
            return;
        }

        setSaving(true);
        try {
            const result = await fetchJson(app.dataset.verifyUrl, {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify(payload)
            });

            verifyModal?.hide();
            state.activeRecordId = "";
            state.activeDraft = null;
            state.lastCloseResult = state.lastCloseResult;
            await loadBoard();
            setStatus("success", result?.message || "El registro se verifico correctamente.");
        } catch (error) {
            console.error(error);
            setModalStatus("error", formatErrorMessage(error, "No fue posible guardar la verificacion."));
        } finally {
            setSaving(false);
        }
    }

    async function closeMonth() {
        if (!state.board) {
            return;
        }

        setClosingMonth(true);
        setStatus("info", "Preparando la revision final del cierre...");

        try {
            const result = await fetchJson(app.dataset.previewCloseMonthUrl, {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify({ filter: state.filter })
            });

            state.closeMonthPreview = normalizeCloseMonthPreview(result);
            closeMonthReviewConfirmCheck && (closeMonthReviewConfirmCheck.checked = false);
            setCloseReviewStatus("", "");
            renderCloseMonthReview();
            closeMonthReviewModal?.show();
            setStatus("", "");
        } catch (error) {
            console.error(error);
            setStatus("error", formatErrorMessage(error, "No fue posible preparar el cierre del mes."));
        } finally {
            setClosingMonth(false);
        }
    }

    async function submitCloseMonthReview() {
        if (!state.closeMonthPreview) {
            return;
        }

        setClosingMonth(true);
        setCloseReviewStatus("info", "Enviando el cierre a sales performance...");

        try {
            const result = await fetchJson(app.dataset.closeMonthUrl, {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify({
                    filter: state.filter,
                    confirmed: true,
                    decisions: getCloseMonthPreviewLines().map(line => ({
                        lineKey: line.lineKey,
                        include: line.selected === true
                    }))
                })
            });

            state.lastCloseResult = result;
            state.closeMonthPreview = null;
            closeMonthReviewModal?.hide();
            await loadBoard();
            setStatus(result?.hasErrors ? "error" : result?.hasWarnings ? "info" : "success", result?.message || "Cierre mensual finalizado.");
        } catch (error) {
            console.error(error);
            setCloseReviewStatus("error", formatErrorMessage(error, "No fue posible cerrar el mes."));
        } finally {
            setClosingMonth(false);
            renderCloseMonthLogs();
            renderCloseMonthReview();
        }
    }

    async function undoCloseMonth() {
        if (!state.board) {
            return;
        }

        const label = state.board.monthClosePeriodLabel || "el periodo actual";
        if (!window.confirm(`Se va a deshacer el ultimo cierre de ${label}.`)) {
            return;
        }

        setClosingMonth(true);
        setStatus("info", "Deshaciendo el ultimo cierre del mes...");

        try {
            const result = await fetchJson(app.dataset.undoCloseMonthUrl, {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify({ filter: state.filter })
            });

            state.lastCloseResult = result;
            state.closeMonthPreview = null;
            closeMonthReviewModal?.hide();
            await loadBoard();
            setStatus(result?.hasErrors ? "error" : "success", result?.message || "Se deshizo el ultimo cierre.");
        } catch (error) {
            console.error(error);
            setStatus("error", formatErrorMessage(error, "No fue posible deshacer el cierre del mes."));
        } finally {
            setClosingMonth(false);
            renderCloseMonthLogs();
        }
    }

    filterButtons.forEach(button => {
        button.addEventListener("click", async () => {
            const nextFilter = button.dataset.filter;
            if (!nextFilter || nextFilter === state.filter || state.isLoading || state.isSaving || state.isClosingMonth) {
                return;
            }

            state.filter = nextFilter;
            await loadBoard();
        });
    });

    const handleScoringChange = () => {
        syncDraftHeaderInputs();
        renderVerifyMetaCards();
        markDraftDirty();
    };

    const handleAdministrativeChange = () => {
        syncDraftHeaderInputs();
    };

    dealTypeSelect?.addEventListener("change", handleScoringChange);

    requiresProrationSelect?.addEventListener("change", async () => {
        if (!state.activeDraft) {
            return;
        }

        if (requiresProrationSelect.value !== "true") {
            state.activeDraft.requiresProration = false;
            state.activeDraft.scenarioStartDateValue = "";
            resetProrationLookupState(state.activeDraft, true);
            syncDraftHeaderInputs();
            renderVerifyMetaCards();
            markDraftDirty();
            return;
        }

        state.activeDraft.requiresProration = true;
        state.activeDraft.prorationClient = state.activeDraft.clientId
            ? { id: state.activeDraft.clientId, name: state.activeDraft.clientName || "" }
            : null;
        resetProrationLookupState(state.activeDraft, false);
        syncDraftHeaderInputs();
        renderVerifyMetaCards();
        markDraftDirty();
        await ensureProrationRenewalOptions({ forceReload: true, preserveSelectedDate: true });
    });

    scenarioStartDateInput?.addEventListener("change", handleScoringChange);
    scenarioEndDateInput?.addEventListener("change", handleScoringChange);
    scenarioEndDateSelect?.addEventListener("change", handleScoringChange);

    [firstContractSelect, verticalOptionSelect, autoBillSelect, contractTypeSelect]
        .filter(Boolean)
        .forEach(element => {
            element.addEventListener("change", handleAdministrativeChange);
        });

    renewalDateInput?.addEventListener("change", handleAdministrativeChange);
    billingDayInput?.addEventListener("change", handleAdministrativeChange);

    refreshButton?.addEventListener("click", loadBoard);
    addVerifyLineBtn?.addEventListener("click", () => {
        if (!state.activeDraft) {
            return;
        }

        state.activeDraft.lines.push(createEmptyLine());
        renderVerifyLines();
        markDraftDirty();
    });
    recalculateVerifyScoreBtn?.addEventListener("click", recalculateVerification);
    submitVerifyScoreBtn?.addEventListener("click", submitVerification);
    closeMonthButton?.addEventListener("click", closeMonth);
    undoCloseMonthButton?.addEventListener("click", undoCloseMonth);
    submitCloseMonthReviewBtn?.addEventListener("click", submitCloseMonthReview);
    closeMonthReviewConfirmCheck?.addEventListener("change", updateCloseMonthReviewSubmitState);

    verifyModalElement?.addEventListener("hidden.bs.modal", () => {
        state.prorationLookupToken += 1;
        state.activeRecordId = "";
        state.activeDraft = null;
        setModalStatus("", "");
        toggleModalLoading(false);
    });

    closeMonthReviewModalElement?.addEventListener("hidden.bs.modal", () => {
        closeMonthReviewConfirmCheck && (closeMonthReviewConfirmCheck.checked = false);
        setCloseReviewStatus("", "");
        updateCloseMonthReviewSubmitState();
    });

    populateSelect(dealTypeSelect, options.dealTypeOptions, "Selecciona un tipo");
    populateSelect(firstContractSelect, options.firstContractOptions, "Selecciona una opcion");
    populateSelect(verticalOptionSelect, options.verticalOptions, "Selecciona una vertical");
    populateSelect(autoBillSelect, options.autoBillOptions, "Selecciona una opcion");
    populateSelect(contractTypeSelect, options.contractTypeOptions, "Selecciona un contrato");
    setFilterButtonState();
    updateSummary(null);
    renderCloseMonthPanel();
    renderCloseMonthReview();
    loadBoard();
})();
