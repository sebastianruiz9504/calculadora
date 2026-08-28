(function () {
    const app = document.getElementById("nominaApp");
    if (!app) {
        return;
    }

    const previewUrl = app.dataset.previewUrl || "";
    const confirmUrl = app.dataset.confirmUrl || "";
    const existingPeriodUrl = app.dataset.existingPeriodUrl || "";
    const saveClosedVerticalsUrl = app.dataset.saveClosedVerticalsUrl || "";
    const uploadPaymentProofUrl = app.dataset.uploadPaymentProofUrl || "";
    const downloadPaymentProofUrl = app.dataset.downloadPaymentProofUrl || "";
    const draftUrl = app.dataset.draftUrl || "";
    const periodInput = document.getElementById("periodInput");
    const paymentDateInput = document.getElementById("paymentDateInput");
    const previewBtn = document.getElementById("previewNominaBtn");
    const confirmBtn = document.getElementById("confirmNominaBtn");
    const saveDraftBtn = document.getElementById("saveNominaDraftBtn");
    const resetBtn = document.getElementById("resetNominaBtn");
    const statusBanner = document.getElementById("nominaStatusBanner");
    const closedCard = document.getElementById("nominaClosedCard");
    const closedRowsBody = document.getElementById("nominaClosedRowsBody");
    const closedPeriodLabel = document.getElementById("nominaClosedPeriodLabel");
    const saveClosedVerticalsBtn = document.getElementById("saveClosedVerticalsBtn");
    const closedVerticalsSaveStatus = document.getElementById("closedVerticalsSaveStatus");
    const summarySection = document.getElementById("nominaSummary");
    const rowsCard = document.getElementById("nominaRowsCard");
    const verticalsCard = document.getElementById("nominaVerticalsCard");
    const logsCard = document.getElementById("nominaLogsCard");
    const rowsBody = document.getElementById("nominaRowsBody");
    const verticalsBody = document.getElementById("nominaVerticalsBody");
    const logsList = document.getElementById("nominaLogsList");
    const logsEmpty = document.getElementById("nominaLogsEmpty");
    const periodLabel = document.getElementById("nominaPeriodLabel");
    const summaryEmployees = document.getElementById("summaryEmployees");
    const summaryPayroll = document.getElementById("summaryPayroll");
    const summaryCuentaCobro = document.getElementById("summaryCuentaCobro");
    const summaryDisbursement = document.getElementById("summaryDisbursement");
    const summaryCopiers = document.getElementById("summaryCopiers");
    const summaryCloud = document.getElementById("summaryCloud");
    const detailModal = document.getElementById("nominaDetailModal");
    const detailForm = document.getElementById("nominaDetailForm");
    const detailTitle = document.getElementById("nominaDetailTitle");
    const detailSubtitle = document.getElementById("nominaDetailSubtitle");
    const detailWarnings = document.getElementById("nominaDetailWarnings");
    const detailMeta = document.getElementById("nominaDetailMeta");
    const detailNoveltiesWrap = document.getElementById("detailNoveltiesWrap");
    const detailNoveltiesList = document.getElementById("detailNoveltiesList");
    const detailNoveltiesEmpty = document.getElementById("detailNoveltiesEmpty");
    const detailNoveltyCoverage = document.getElementById("detailNoveltyCoverage");
    const detailAddNoveltyBtn = document.getElementById("detailAddNoveltyBtn");
    const detailManualEditor = detailModal ? detailModal.querySelector(".payroll-manual-editor") : null;
    const detailExternalWithholdingWrap = document.getElementById("detailExternalWithholdingWrap");
    const detailNonCommissionBonusWithholdingWrap = document.getElementById("detailNonCommissionBonusWithholdingWrap");
    const detailManualEditToggle = document.getElementById("detailManualEditToggle");
    const detailManualEditPanel = document.getElementById("detailManualEditPanel");
    const detailManualFields = document.getElementById("detailManualFields");
    const detailManualResetBtn = document.getElementById("detailManualResetBtn");
    const detailPaymentProofSection = document.getElementById("detailPaymentProofSection");
    const detailPaymentProofList = document.getElementById("detailPaymentProofList");
    const detailInputs = detailModal ? Array.from(detailModal.querySelectorAll("[data-detail-field]")) : [];
    const detailCloseButtons = detailModal ? Array.from(detailModal.querySelectorAll("[data-nomina-detail-close]")) : [];
    const detailOutputs = detailModal
        ? Array.from(detailModal.querySelectorAll("[data-detail-output]")).reduce((map, element) => {
            const key = element.dataset.detailOutput;
            if (!key) {
                return map;
            }

            if (!map[key]) {
                map[key] = [];
            }

            map[key].push(element);
            return map;
        }, {})
        : {};
    const absenceReasonLabels = {
        ingreso: "Ingreso",
        incapacidad: "Incapacidad",
        vacaciones: "Vacaciones",
        calamidad: "Calamidad",
        no_remunerado: "Dia no remunerado"
    };
    const absenceReasonAliases = {
        "no remunerado": "no_remunerado",
        "dia no remunerado": "no_remunerado",
        "dias no remunerados": "no_remunerado",
        "dia no remuerado": "no_remunerado",
        "dias no remuerados": "no_remunerado"
    };
    const legacyDraftStorageKey = buildDraftStorageKey(app.dataset.draftOwner || "");
    const draftStorageKey = "cotizador.nomina.preliquidacion.shared";
    const draftStorageKeyPrefix = "cotizador.nomina.preliquidacion.";
    const draftVersion = 4;
    const payrollContractTypeOptionValue = 645250000;
    const serviceContractTypeOptionValue = 645250001;
    const defaultExternalWithholdingRate = 0.04;
    const deductionFields = new Set(["otherDeductions", "loan", "payrollWithholding"]);
    const closedVerticalEditableFields = new Set(["factorCopiers", "factorCloud"]);
    const manualEditableFields = [
        { field: "salaryBase", label: "Sueldo base proporcional" },
        { field: "auxilio", label: "Auxilio proporcional" },
        { field: "absencePayment", label: "Pago dias no trabajados" },
        { field: "commissionsCopiers", label: "Comisiones Copiers" },
        { field: "commissionsCloud", label: "Comisiones Cloud" },
        { field: "commissionsUnassigned", label: "Comisiones sin vertical" },
        { field: "appliedCommissionBase", label: "Base aplicada" },
        { field: "contributionBase", label: "Base aportes" },
        { field: "health", label: "Salud" },
        { field: "pension", label: "Pension" },
        { field: "cuentaDeCobro", label: "Cuenta cobro" },
        { field: "grossSalary", label: "Sueldo bruto" },
        { field: "netPayroll", label: "Monto pagado" },
        { field: "netCuentaDeCobro", label: "Monto pagado cxc" },
        { field: "verticalBase", label: "Base reparto" },
        { field: "baseCopiers", label: "Base Copiers" },
        { field: "baseCloud", label: "Base Cloud" },
        { field: "totalCopiers", label: "Total Copiers" },
        { field: "totalCloud", label: "Total Cloud" }
    ];
    const manualAllowNegativeFields = new Set(["netPayroll", "netCuentaDeCobro", "verticalBase", "baseCopiers", "baseCloud", "totalCopiers", "totalCloud"]);
    let sharedDraftSaveTimer = 0;
    let pendingSharedDraft = null;

    const state = {
        rows: [],
        closedRows: [],
        logs: [],
        activeRowId: "",
        activeClosedRowId: "",
        detailMode: "",
        busy: false,
        closedMode: false,
        closedRequestId: 0,
        activeClosedPaymentType: "",
        restoringDraft: false,
        draftSavedAt: ""
    };

    periodInput.value = app.dataset.initialPeriod || "";
    paymentDateInput.value = app.dataset.suggestedPaymentDate || "";

    periodInput.addEventListener("change", () => {
        if (!paymentDateInput.value) {
            paymentDateInput.value = getLastDayOfMonth(periodInput.value);
        }

        handleDraftDateChange(true);
        void loadExistingPeriod();
    });

    paymentDateInput.addEventListener("change", () => {
        handleDraftDateChange(false);
    });

    previewBtn.addEventListener("click", async () => {
        await requestPreview();
    });

    confirmBtn.addEventListener("click", async () => {
        await confirmPreview();
    });

    saveDraftBtn?.addEventListener("click", () => {
        saveDraft(true);
    });

    resetBtn.addEventListener("click", () => {
        const wasClosedMode = state.closedMode;
        clearState();
        if (!wasClosedMode) {
            clearSavedDraft();
        }
        renderStatus("info", wasClosedMode
            ? "La vista fue limpiada. El borrador guardado no se modifico."
            : "La vista fue limpiada. Selecciona mes y fecha de pago para preparar una nueva liquidacion.");
    });

    saveClosedVerticalsBtn?.addEventListener("click", () => {
        void saveClosedVerticalChanges();
    });

    rowsBody.addEventListener("click", (event) => {
        const target = event.target instanceof Element ? event.target : null;
        if (target?.closest("[data-row-edit]")) {
            return;
        }

        const rowElement = target ? target.closest("tr[data-row-id]") : null;
        if (!rowElement) {
            return;
        }

        openDetail(rowElement.dataset.rowId);
    });

    rowsBody.addEventListener("keydown", (event) => {
        if (event.key !== "Enter" && event.key !== " ") {
            return;
        }

        const target = event.target instanceof Element ? event.target : null;
        if (target?.closest("[data-row-edit]")) {
            return;
        }

        const rowElement = target ? target.closest("tr[data-row-id]") : null;
        if (!rowElement) {
            return;
        }

        event.preventDefault();
        openDetail(rowElement.dataset.rowId);
    });

    rowsBody.addEventListener("input", handleRowEditChange);
    rowsBody.addEventListener("change", handleRowEditChange);
    verticalsBody.addEventListener("click", handleVerticalRowOpen);
    verticalsBody.addEventListener("keydown", handleVerticalRowKeydown);
    verticalsBody.addEventListener("input", handleVerticalEditChange);
    verticalsBody.addEventListener("change", handleVerticalEditChange);
    closedRowsBody?.addEventListener("change", handlePaymentProofChange);
    closedRowsBody?.addEventListener("click", handleClosedRowOpen);
    closedRowsBody?.addEventListener("keydown", handleClosedRowKeydown);

    detailForm?.addEventListener("input", handleDetailFieldChange);
    detailForm?.addEventListener("change", handleDetailFieldChange);
    detailNoveltiesList?.addEventListener("input", handleNoveltyFieldChange);
    detailNoveltiesList?.addEventListener("change", handleNoveltyFieldChange);
    detailNoveltiesList?.addEventListener("click", handleNoveltyListClick);
    detailAddNoveltyBtn?.addEventListener("click", addNoveltyToActiveRow);
    detailManualEditToggle?.addEventListener("change", handleManualToggleChange);
    detailManualFields?.addEventListener("input", handleManualFieldChange);
    detailManualFields?.addEventListener("change", handleManualFieldChange);
    detailManualResetBtn?.addEventListener("click", resetManualOverrides);

    void initialize();

    async function initialize() {
        await restoreDraft();
        await loadExistingPeriod({ silent: true });
    }

    function handleRowEditChange(event) {
        const input = event.target;
        if (!(input instanceof HTMLInputElement)) {
            return;
        }

        const field = input.dataset.rowField;
        const rowElement = input.closest("tr[data-row-id]");
        const row = rowElement ? state.rows.find((item) => item.employeeId === rowElement.dataset.rowId) : null;
        if (!row) {
            return;
        }

        if (field === "verified") {
            row.verified = input.checked;
            updateRowOutputs(row, field);
            renderSummary();
            updateConfirmAvailability();
            saveDraft();
            return;
        }

        return;
    }

    function handleVerticalEditChange(event) {
        const input = event.target;
        if (!(input instanceof HTMLInputElement)) {
            return;
        }

        const field = input.dataset.verticalField;
        if (field !== "factorCopiers" && field !== "factorCloud") {
            return;
        }

        const rowElement = input.closest("tr[data-vertical-row-id]");
        const row = rowElement ? state.rows.find((item) => item.employeeId === rowElement.dataset.verticalRowId) : null;
        if (!row) {
            return;
        }

        markRowPendingVerification(row);
        row[field] = toPositiveNumber(input.value);
        recalculateRow(row);
        updateRowOutputs(row);
        updateVerticalOutputs(row, field);
        if (state.activeRowId === row.employeeId) {
            renderDetailValues(row);
            renderManualEditor(row);
            renderDetailWarnings(row);
        }

        renderSummary();
        updateConfirmAvailability();
        saveDraft();
    }

    function handleVerticalRowOpen(event) {
        const target = event.target instanceof Element ? event.target : null;
        if (target?.closest("[data-vertical-field]")) {
            return;
        }

        const rowElement = target ? target.closest("tr[data-vertical-row-id]") : null;
        if (!rowElement) {
            return;
        }

        openDetail(rowElement.dataset.verticalRowId);
    }

    function handleVerticalRowKeydown(event) {
        if (event.key !== "Enter" && event.key !== " ") {
            return;
        }

        const target = event.target instanceof Element ? event.target : null;
        if (target?.closest("[data-vertical-field]")) {
            return;
        }

        const rowElement = target ? target.closest("tr[data-vertical-row-id]") : null;
        if (!rowElement) {
            return;
        }

        event.preventDefault();
        openDetail(rowElement.dataset.verticalRowId);
    }

    function handleDetailFieldChange(event) {
        const input = event.target;
        if (!(input instanceof HTMLInputElement) && !(input instanceof HTMLSelectElement)) {
            return;
        }

        const field = input.dataset.detailField;
        if (!field) {
            return;
        }

        if (isClosedDetailMode()) {
            handleClosedDetailVerticalChange(input, field);
            return;
        }

        const row = getActiveDetailRow();
        if (!row) {
            return;
        }

        markRowPendingVerification(row);
        if (field === "workedDays") {
            row[field] = clampDays(toPositiveNumber(input.value), getPeriodDays(row));
            if (row[field] >= getPeriodDays(row)) {
                row.novelties = [];
            } else if (!Array.isArray(row.novelties) || row.novelties.length === 0) {
                row.novelties = [createNovelty({ days: roundMoney(getPeriodDays(row) - row[field]) })];
            }
        } else if (field === "applyNonCommissionBonusWithholding" || field === "applyExternalWithholding") {
            row[field] = input instanceof HTMLInputElement && input.type === "checkbox" ? input.checked : Boolean(input.value);
        } else if (field === "nonCommissionBonusWithholdingRate" || field === "externalWithholdingRate") {
            row[field] = parseRateInputValue(input.value);
        } else {
            row[field] = toPositiveNumber(input.value);
        }

        recalculateRow(row);
        updateRowOutputs(row);
        renderDetailInputs(row, field);
        renderNoveltyEditor(row);
        renderDetailValues(row);
        renderManualEditor(row);
        renderDetailWarnings(row);
        renderSummary();
        renderVerticals();
        updateConfirmAvailability();
        saveDraft();
    }

    function handleClosedDetailVerticalChange(input, field) {
        if (!closedVerticalEditableFields.has(field)) {
            return;
        }

        const closedRow = getActiveClosedRow();
        if (!closedRow || !closedRow.detail) {
            return;
        }

        const detail = closedRow.detail;
        detail[field] = toPositiveNumber(input.value);
        recalculateClosedVerticalDistribution(detail);
        syncClosedRowVerticalTotals(closedRow);
        updateClosedVerticalDirtyState(closedRow);
        renderDetailInputs(detail, field);
        renderDetailValues(detail);
        renderDetailWarnings(detail);
        renderClosedRows();
        setClosedVerticalSaveStatus(hasClosedVerticalChanges(closedRow) ? "Cambios pendientes por guardar. Se guardan como porcentaje entero." : "");
        updateClosedVerticalSaveAvailability();
    }

    function handleNoveltyFieldChange(event) {
        const input = event.target;
        if (!(input instanceof HTMLInputElement) && !(input instanceof HTMLSelectElement)) {
            return;
        }

        const field = input.dataset.noveltyField;
        const itemElement = input.closest("[data-novelty-index]");
        const row = getActiveDetailRow();
        if (!field || !itemElement || !row) {
            return;
        }

        const index = Number.parseInt(itemElement.dataset.noveltyIndex || "", 10);
        const novelty = getNoveltyAt(row, index);
        if (!novelty) {
            return;
        }

        markRowPendingVerification(row);
        if (field === "reason") {
            novelty.reason = normalizeAbsenceReason(input.value);
            if (isUnpaidNovelty(novelty)) {
                novelty.paymentManual = false;
                novelty.payment = 0;
            } else if (!novelty.paymentManual) {
                novelty.payment = calculateNoveltyPayment(row, novelty);
            }
        } else if (field === "days") {
            novelty.days = clampDays(toPositiveNumber(input.value), getPeriodDays(row));
            if (isUnpaidNovelty(novelty)) {
                novelty.paymentManual = false;
                novelty.payment = 0;
            } else if (!novelty.paymentManual) {
                novelty.payment = calculateNoveltyPayment(row, novelty);
            }
        } else if (field === "payment") {
            if (isUnpaidNovelty(novelty)) {
                novelty.payment = 0;
                novelty.paymentManual = false;
            } else {
                novelty.payment = toPositiveNumber(input.value);
                novelty.paymentManual = input.value !== "";
            }
        }

        recalculateRow(row);
        updateRowOutputs(row);
        renderDetailInputs(row);
        renderNoveltyEditor(row, { index, field });
        renderDetailValues(row);
        renderManualEditor(row);
        renderDetailWarnings(row);
        renderSummary();
        renderVerticals();
        updateConfirmAvailability();
        saveDraft();
    }

    function handleNoveltyListClick(event) {
        const target = event.target instanceof Element ? event.target : null;
        const removeButton = target?.closest("[data-novelty-remove]");
        if (!removeButton) {
            return;
        }

        const itemElement = removeButton.closest("[data-novelty-index]");
        const row = getActiveDetailRow();
        if (!itemElement || !row) {
            return;
        }

        const index = Number.parseInt(itemElement.dataset.noveltyIndex || "", 10);
        if (!Number.isFinite(index)) {
            return;
        }

        markRowPendingVerification(row);
        row.novelties = normalizeNovelties(row).filter((_, itemIndex) => itemIndex !== index);
        recalculateRow(row);
        updateRowOutputs(row);
        renderDetailInputs(row);
        renderNoveltyEditor(row);
        renderDetailValues(row);
        renderManualEditor(row);
        renderDetailWarnings(row);
        renderSummary();
        renderVerticals();
        updateConfirmAvailability();
        saveDraft();
    }

    function addNoveltyToActiveRow() {
        const row = getActiveDetailRow();
        if (!row) {
            return;
        }

        markRowPendingVerification(row);
        const coverage = getNoveltyCoverage(row);
        row.novelties = normalizeNovelties(row);
        row.novelties.push(createNovelty({ days: Math.max(coverage.missingDays, 0) }));
        recalculateRow(row);
        updateRowOutputs(row);
        renderDetailInputs(row);
        renderNoveltyEditor(row, { index: row.novelties.length - 1, field: "reason" });
        renderDetailValues(row);
        renderManualEditor(row);
        renderDetailWarnings(row);
        renderSummary();
        renderVerticals();
        updateConfirmAvailability();
        saveDraft();
    }

    function handleManualToggleChange(event) {
        const input = event.target;
        if (!(input instanceof HTMLInputElement)) {
            return;
        }

        const row = getActiveDetailRow();
        if (!row) {
            return;
        }

        markRowPendingVerification(row);
        row.manualEditEnabled = input.checked;
        if (!row.manualEditEnabled) {
            row.manualOverrides = {};
        } else {
            ensureManualOverrides(row);
        }

        recalculateRow(row);
        updateRowOutputs(row);
        renderDetailInputs(row);
        renderNoveltyEditor(row);
        renderDetailValues(row);
        renderManualEditor(row);
        renderDetailWarnings(row);
        renderSummary();
        renderVerticals();
        updateConfirmAvailability();
        saveDraft();
    }

    function handleManualFieldChange(event) {
        const input = event.target;
        if (!(input instanceof HTMLInputElement)) {
            return;
        }

        const field = input.dataset.manualField;
        if (!field) {
            return;
        }

        const row = getActiveDetailRow();
        if (!row) {
            return;
        }

        markRowPendingVerification(row);
        row.manualEditEnabled = true;
        ensureManualOverrides(row);

        if (input.value === "") {
            delete row.manualOverrides[field];
        } else {
            row.manualOverrides[field] = parseManualInputValue(input.value, field);
        }

        recalculateRow(row);
        updateRowOutputs(row);
        renderDetailInputs(row);
        renderNoveltyEditor(row);
        renderDetailValues(row);
        renderManualEditor(row, field);
        renderDetailWarnings(row);
        renderSummary();
        renderVerticals();
        updateConfirmAvailability();
        saveDraft();
    }

    function resetManualOverrides() {
        const row = getActiveDetailRow();
        if (!row) {
            return;
        }

        markRowPendingVerification(row);
        row.manualEditEnabled = false;
        row.manualOverrides = {};
        recalculateRow(row);
        updateRowOutputs(row);
        renderDetailInputs(row);
        renderNoveltyEditor(row);
        renderDetailValues(row);
        renderManualEditor(row);
        renderDetailWarnings(row);
        renderSummary();
        renderVerticals();
        updateConfirmAvailability();
        saveDraft();
    }

    function handleDraftDateChange(periodChanged) {
        if (!state.rows.length) {
            return;
        }

        if (periodChanged) {
            state.rows.forEach((row) => markRowPendingVerification(row));
            renderRows();
            updateConfirmAvailability();
            renderStatus("info", "El periodo cambio. Vuelve a preparar la liquidacion y verifica las filas antes de confirmar.");
        }

        saveDraft();
    }

    detailForm?.addEventListener("submit", (event) => {
        event.preventDefault();
    });

    detailCloseButtons.forEach((button) => {
        button.addEventListener("click", () => {
            closeDetail();
        });
    });

    document.addEventListener("keydown", (event) => {
        if (event.key === "Escape" && detailModal && !detailModal.hidden) {
            closeDetail();
        }
    });

    async function requestPreview() {
        try {
            ensureInputs();
            if (await loadExistingPeriod()) {
                return;
            }

            setBusy(true);
            renderStatus("info", "Preparando la preliquidacion...");

            const response = await fetch(previewUrl, {
                method: "POST",
                headers: {
                    "Content-Type": "application/json"
                },
                body: JSON.stringify(buildRequest(false))
            });

            const payload = await readPayload(response);
            if (!response.ok) {
                throw createResponseError(payload);
            }

            loadResult(payload, false);
            renderStatus(payload.hasWarnings ? "warning" : "success", payload.message || "Preliquidacion lista para confirmar.");
        } catch (error) {
            handleFailure(error);
        } finally {
            setBusy(false);
        }
    }

    async function confirmPreview() {
        try {
            ensureInputs();
            if (await loadExistingPeriod()) {
                return;
            }

            if (state.rows.length === 0) {
                renderStatus("warning", "Primero debes preparar la preliquidacion.");
                return;
            }

            if (!areAllRowsVerified()) {
                renderStatus("warning", "Marca todas las filas como Verificado antes de confirmar y enviar.");
                updateConfirmAvailability();
                return;
            }

            const blockingWarning = getFirstBlockingCoverageWarning();
            if (blockingWarning) {
                renderStatus("warning", blockingWarning);
                updateConfirmAvailability();
                return;
            }

            setBusy(true);
            renderStatus("info", "Confirmando la liquidacion y enviando a Dataverse...");

            const response = await fetch(confirmUrl, {
                method: "POST",
                headers: {
                    "Content-Type": "application/json"
                },
                body: JSON.stringify(buildRequest(true))
            });

            const payload = await readPayload(response);
            if (!response.ok) {
                throw createResponseError(payload);
            }

            loadResult(payload, true);
            if (payload.hasErrors) {
                renderStatus("warning", payload.message || "El proceso termino con errores. Revisa el detalle operativo.");
            } else if (payload.hasWarnings) {
                renderStatus("warning", payload.message || "La liquidacion termino con advertencias.");
            } else {
                renderStatus("success", payload.message || "Liquidacion confirmada correctamente.");
            }
        } catch (error) {
            handleFailure(error);
        } finally {
            setBusy(false);
        }
    }

    function buildRequest(confirmed) {
        return {
            periodKey: periodInput.value,
            paymentDateValue: paymentDateInput.value,
            confirmed,
            adjustments: state.rows.map((row) => {
                const serviceContract = isServiceContract(row);
                return {
                    employeeId: row.employeeId,
                    verified: Boolean(row.verified),
                    manualEditEnabled: Boolean(row.manualEditEnabled),
                    manualOverrides: buildManualOverridesPayload(row),
                    workedDays: row.workedDays,
                    absenceReason: row.absenceReason || "",
                    absencePayment: row.absencePayment,
                    novelties: buildNoveltiesPayload(row),
                    factorCopiers: row.factorCopiers,
                    factorCloud: row.factorCloud,
                    bonusCompliance: row.bonusCompliance,
                    nonCommissionBonus: row.nonCommissionBonus,
                    applyNonCommissionBonusWithholding: Boolean(row.applyNonCommissionBonusWithholding),
                    nonCommissionBonusWithholdingRate: row.nonCommissionBonusWithholdingRate,
                    otherDeductions: serviceContract ? 0 : row.otherDeductions,
                    loan: serviceContract ? 0 : row.loan,
                    payrollWithholding: serviceContract ? 0 : row.payrollWithholding,
                    applyExternalWithholding: Boolean(row.applyExternalWithholding),
                    externalWithholdingRate: row.externalWithholdingRate,
                    externalWithholding: row.externalWithholding
                };
            })
        };
    }

    function loadResult(payload, fromConfirm) {
        setClosedMode(false);
        closeDetail();
        state.rows = Array.isArray(payload.rows) ? payload.rows.map((row) => normalizeRow(row)) : [];
        state.logs = Array.isArray(payload.logs) ? payload.logs.slice() : [];

        summarySection.hidden = state.rows.length === 0;
        rowsCard.hidden = state.rows.length === 0;
        verticalsCard.hidden = state.rows.length === 0;
        logsCard.hidden = state.logs.length === 0 && !fromConfirm;
        updateConfirmAvailability();
        periodLabel.textContent = payload.periodLabel
            ? `${payload.periodLabel} | Pago: ${payload.paymentDateDisplay || payload.paymentDateValue || ""}`
            : "";

        renderRows();
        renderSummary();
        renderVerticals();
        renderLogs();

        if (fromConfirm && state.logs.length > 0) {
            logsCard.hidden = false;
        }

        if (!state.restoringDraft) {
            if (fromConfirm && !payload.hasErrors) {
                clearSavedDraft();
            } else {
                saveDraft();
            }
        }
    }

    async function loadExistingPeriod(options) {
        const silent = Boolean(options && options.silent);
        if (!existingPeriodUrl || !periodInput.value) {
            setClosedMode(false);
            return false;
        }

        const requestId = state.closedRequestId + 1;
        state.closedRequestId = requestId;
        try {
            if (!silent) {
                renderStatus("info", "Revisando si el mes ya tiene nomina en Dataverse...");
            }

            const url = new URL(existingPeriodUrl, window.location.origin);
            url.searchParams.set("periodKey", periodInput.value);
            const response = await fetch(url.toString(), {
                headers: {
                    "Accept": "application/json"
                }
            });
            const payload = await readPayload(response);
            if (requestId !== state.closedRequestId) {
                return false;
            }

            if (!response.ok) {
                throw createResponseError(payload);
            }

            if (payload && payload.hasRecords && Array.isArray(payload.rows) && payload.rows.length > 0) {
                loadClosedPeriod(payload, silent);
                return true;
            }

            setClosedMode(false);
            if (!silent) {
                renderStatus("info", "El mes seleccionado no tiene nomina enviada a Dataverse. Puedes preparar la liquidacion normalmente.");
            }

            return false;
        } catch (error) {
            if (!silent) {
                handleFailure(error);
            } else {
                console.warn("No fue posible revisar la nomina existente en Dataverse.", error);
            }

            setClosedMode(false);
            return false;
        }
    }

    function loadClosedPeriod(payload, silent) {
        closeDetail();
        state.closedRows = Array.isArray(payload.rows) ? payload.rows.map((row) => normalizeClosedRow(row)) : [];
        state.logs = [];
        setClosedMode(true);
        if (closedPeriodLabel) {
            closedPeriodLabel.textContent = payload.periodLabel
                ? `${payload.periodLabel} | ${state.closedRows.length} empleado(s)`
                : "";
        }

        renderClosedRows();
        setClosedVerticalSaveStatus("");
        updateConfirmAvailability();
        if (!silent) {
            renderStatus("info", payload.message || "Este mes ya tiene nomina en Dataverse. Se muestra la tabla de pagos.");
        }
    }

    function setClosedMode(enabled) {
        state.closedMode = Boolean(enabled);
        app.classList.toggle("payroll-shell--closed", state.closedMode);
        if (closedCard) {
            closedCard.hidden = !state.closedMode;
        }

        if (state.closedMode) {
            summarySection.hidden = true;
            rowsCard.hidden = true;
            verticalsCard.hidden = true;
            logsCard.hidden = true;
            logsList.innerHTML = "";
        } else {
            state.closedRows = [];
            if (closedRowsBody) {
                closedRowsBody.innerHTML = "";
            }

            if (closedPeriodLabel) {
                closedPeriodLabel.textContent = "";
            }

            setClosedVerticalSaveStatus("");
            summarySection.hidden = state.rows.length === 0;
            rowsCard.hidden = state.rows.length === 0;
            verticalsCard.hidden = state.rows.length === 0;
            logsCard.hidden = state.logs.length === 0;
        }

        updateClosedVerticalSaveAvailability();
        updateConfirmAvailability();
    }

    function normalizeClosedRow(row) {
        const normalized = {
            payrollRecordId: String(row?.payrollRecordId || "").trim(),
            employeeId: String(row?.employeeId || "").trim(),
            employeeName: String(row?.employeeName || "").trim(),
            valueToPay: roundMoney(toNumber(row?.valueToPay)),
            valueCopiers: roundMoney(toNumber(row?.valueCopiers)),
            valueCloud: roundMoney(toNumber(row?.valueCloud)),
            hasPaymentProof: Boolean(row?.hasPaymentProof),
            paymentProofFileName: String(row?.paymentProofFileName || "").trim(),
            hasCuentaDeCobroPaymentProof: Boolean(row?.hasCuentaDeCobroPaymentProof),
            cuentaDeCobroPaymentProofFileName: String(row?.cuentaDeCobroPaymentProofFileName || "").trim()
        };

        normalized.detail = normalizeClosedDetail(row?.detail, normalized);
        normalized.savedFactorCopiers = roundMoney(toNumber(normalized.detail.factorCopiers));
        normalized.savedFactorCloud = roundMoney(toNumber(normalized.detail.factorCloud));
        normalized.verticalDistributionDirty = false;
        return normalized;
    }

    function normalizeClosedDetail(detail, closedRow) {
        const source = detail && typeof detail === "object" && !Array.isArray(detail)
            ? detail
            : {};
        const normalized = { ...source };
        normalized.employeeId = String(source.employeeId || closedRow.employeeId || "").trim();
        normalized.employeeName = String(source.employeeName || closedRow.employeeName || "").trim();
        normalized.periodKey = String(source.periodKey || periodInput.value || "").trim();
        normalized.periodLabel = String(source.periodLabel || "").trim();
        normalized.paymentDateValue = String(source.paymentDateValue || paymentDateInput.value || "").trim();
        normalized.paymentDateDisplay = String(source.paymentDateDisplay || "").trim();
        normalized.operation = "closed";
        normalized.existingPayrollRecordId = String(source.existingPayrollRecordId || closedRow.payrollRecordId || "").trim();
        normalized.existingPayrollRecordCount = Number.parseInt(String(source.existingPayrollRecordCount || 1), 10) || 1;
        normalized.employeeContractTypeOptionValue = Number.parseInt(String(source.employeeContractTypeOptionValue || 0), 10) || 0;
        normalized.employeeContractTypeLabel = String(source.employeeContractTypeLabel || "").trim();
        normalized.isServiceContract = Boolean(source.isServiceContract) || isServiceContract(normalized);
        normalized.verified = true;
        normalized.manualEditEnabled = false;
        normalized.manualOverrides = {};
        normalized.warnings = Array.isArray(source.warnings) ? source.warnings.slice() : [];

        [
            "monthlySalaryBase",
            "monthlyAuxilio",
            "salaryBase",
            "auxilio",
            "bonusCompliance",
            "nonCommissionBonus",
            "nonCommissionBonusWithholding",
            "commissionsCopiers",
            "commissionsCloud",
            "commissionsUnassigned",
            "commissions",
            "commissionCap",
            "appliedCommissionBase",
            "contributionBase",
            "verticalBase",
            "baseCopiers",
            "baseCloud",
            "health",
            "pension",
            "otherDeductions",
            "loan",
            "payrollWithholding",
            "cuentaDeCobro",
            "externalWithholding",
            "grossSalary",
            "netPayroll",
            "netCuentaDeCobro",
            "factorCopiers",
            "factorCloud",
            "totalCopiers",
            "totalCloud"
        ].forEach((field) => {
            normalized[field] = roundMoney(toNumber(normalized[field]));
        });

        normalized.periodDays = Math.max(Math.round(toPositiveNumber(source.periodDays)) || getDaysInPeriod(normalized.periodKey), 1);
        normalized.workedDays = clampDays(
            source.workedDays === undefined || source.workedDays === null || source.workedDays === ""
                ? normalized.periodDays - toPositiveNumber(source.absenceDays)
                : source.workedDays,
            normalized.periodDays);
        normalized.absenceDays = roundMoney(toPositiveNumber(source.absenceDays || Math.max(normalized.periodDays - normalized.workedDays, 0)));
        normalized.absencePayment = roundMoney(toPositiveNumber(source.absencePayment));
        normalized.absenceReason = String(source.absenceReason || "").trim();
        normalized.absenceReasonLabel = String(source.absenceReasonLabel || "").trim();
        normalized.novelties = Array.isArray(source.novelties)
            ? source.novelties.map((novelty) => {
                const reason = normalizeAbsenceReason(novelty?.reason);
                const unpaid = reason === "no_remunerado";
                return {
                    reason,
                    reasonLabel: String(novelty?.reasonLabel || getAbsenceReasonLabel(reason) || "").trim(),
                    days: roundMoney(toPositiveNumber(novelty?.days)),
                    payment: unpaid ? 0 : roundMoney(toPositiveNumber(novelty?.payment)),
                    paymentManual: !unpaid
                };
            }).filter((novelty) => hasNoveltyData(novelty))
            : [];
        normalized.applyNonCommissionBonusWithholding = Boolean(source.applyNonCommissionBonusWithholding || normalized.nonCommissionBonusWithholding > 0);
        normalized.nonCommissionBonusWithholdingRate = normalizeClosedRate(
            source.nonCommissionBonusWithholdingRate
            || inferRate(normalized.nonCommissionBonusWithholding, normalized.nonCommissionBonus));
        normalized.applyExternalWithholding = Boolean(source.applyExternalWithholding || normalized.externalWithholding > 0);
        normalized.externalWithholdingRate = normalizeClosedRate(
            source.externalWithholdingRate
            || inferRate(normalized.externalWithholding, normalized.cuentaDeCobro));
        normalized.healthRate = normalizeClosedRate(source.healthRate);
        normalized.pensionRate = normalizeClosedRate(source.pensionRate);
        normalized.valueToPay = closedRow.valueToPay;

        if (normalized.totalCopiers === 0 && closedRow.valueCopiers !== 0) {
            normalized.totalCopiers = closedRow.valueCopiers;
        }

        if (normalized.totalCloud === 0 && closedRow.valueCloud !== 0) {
            normalized.totalCloud = closedRow.valueCloud;
        }

        return normalized;
    }

    function renderClosedRows() {
        if (!closedRowsBody) {
            return;
        }

        const paymentLines = state.closedRows.flatMap((row) => buildClosedPaymentLines(row));
        closedRowsBody.innerHTML = paymentLines.map((line) => `
            <tr data-closed-row-id="${escapeHtml(line.row.payrollRecordId)}" data-closed-line-type="${escapeHtml(line.type)}" class="${buildClosedPaymentLineClass(line)}" tabindex="0" role="button" aria-label="Desglose de ${escapeHtml(line.row.employeeName || "empleado")} ${escapeHtml(line.label)}">
                <td>
                    <div class="payroll-row__name">${escapeHtml(line.row.employeeName || "Empleado sin nombre")}</div>
                    ${line.showLabel ? `<div class="payroll-row__meta"><span class="payroll-closed-line-pill payroll-closed-line-pill--${escapeHtml(line.type)}">${escapeHtml(line.label)}</span></div>` : ""}
                </td>
                <td class="text-end payroll-closed-cell payroll-closed-cell--pay">${formatMoney(line.valueToPay)}</td>
                <td class="text-end payroll-closed-cell payroll-closed-cell--copiers">${formatMoney(line.valueCopiers)}</td>
                <td class="text-end payroll-closed-cell payroll-closed-cell--cloud">${formatMoney(line.valueCloud)}</td>
                <td>${renderPaymentProofCell(line)}</td>
            </tr>
        `).join("") + renderClosedTotalsRow(paymentLines);
        updateClosedVerticalSaveAvailability();
    }

    function buildClosedPaymentLines(row) {
        const cxcValue = roundMoney(toPositiveNumber(row.detail?.netCuentaDeCobro));
        if (cxcValue <= 0) {
            return [{
                row,
                type: "nomina",
                label: "Nomina",
                showLabel: false,
                valueToPay: row.valueToPay,
                valueCopiers: row.valueCopiers,
                valueCloud: row.valueCloud,
                hasPaymentProof: row.hasPaymentProof,
                paymentProofFileName: row.paymentProofFileName
            }];
        }

        const payrollValue = roundMoney(toNumber(row.detail?.netPayroll) || Math.max(row.valueToPay - cxcValue, 0));
        const cxcVerticals = splitClosedCuentaCobroVerticals(row, cxcValue);
        return [
            {
                row,
                type: "nomina",
                label: "Nomina",
                showLabel: true,
                valueToPay: payrollValue,
                valueCopiers: roundMoney(row.valueCopiers - cxcVerticals.valueCopiers),
                valueCloud: roundMoney(row.valueCloud - cxcVerticals.valueCloud),
                hasPaymentProof: row.hasPaymentProof,
                paymentProofFileName: row.paymentProofFileName
            },
            {
                row,
                type: "cxc",
                label: "Cuenta de cobro",
                showLabel: true,
                valueToPay: cxcValue,
                valueCopiers: cxcVerticals.valueCopiers,
                valueCloud: cxcVerticals.valueCloud,
                hasPaymentProof: row.hasCuentaDeCobroPaymentProof,
                paymentProofFileName: row.cuentaDeCobroPaymentProofFileName
            }
        ];
    }

    function splitClosedCuentaCobroVerticals(row, cxcValue) {
        const verticalTotal = roundMoney(Math.max(toNumber(row.valueCopiers) + toNumber(row.valueCloud), 0));
        if (verticalTotal <= 0 || cxcValue <= 0) {
            return {
                valueCopiers: 0,
                valueCloud: 0
            };
        }

        const boundedCxcValue = Math.min(cxcValue, verticalTotal);
        const valueCopiers = roundMoney(boundedCxcValue * toNumber(row.valueCopiers) / verticalTotal);
        return {
            valueCopiers,
            valueCloud: roundMoney(boundedCxcValue - valueCopiers)
        };
    }

    function buildClosedPaymentLineClass(line) {
        const classes = ["payroll-closed-row", "payroll-closed-row--interactive"];
        if (line.type === "cxc") {
            classes.push("payroll-closed-row--cxc");
        }

        if (hasClosedVerticalChanges(line.row)) {
            classes.push("payroll-closed-row--dirty");
        }

        if (line.hasPaymentProof) {
            classes.push("payroll-closed-row--paid");
        }

        return classes.join(" ");
    }

    function renderClosedTotalsRow(paymentLines) {
        const totals = paymentLines.reduce((result, line) => {
            result.valueToPay += line.valueToPay;
            result.valueCopiers += line.valueCopiers;
            result.valueCloud += line.valueCloud;
            return result;
        }, {
            valueToPay: 0,
            valueCopiers: 0,
            valueCloud: 0
        });

        return `
            <tr class="payroll-closed-total-row">
                <td>Total</td>
                <td class="text-end">${formatMoney(totals.valueToPay)}</td>
                <td class="text-end payroll-closed-cell payroll-closed-cell--copiers">${formatMoney(totals.valueCopiers)}</td>
                <td class="text-end payroll-closed-cell payroll-closed-cell--cloud">${formatMoney(totals.valueCloud)}</td>
                <td></td>
            </tr>
        `;
    }

    function handleClosedRowOpen(event) {
        const target = event.target instanceof Element ? event.target : null;
        if (target?.closest(".payroll-proof-cell, a, button, input, label")) {
            return;
        }

        const rowElement = target ? target.closest("tr[data-closed-row-id]") : null;
        if (!rowElement) {
            return;
        }

        openClosedDetail(rowElement.dataset.closedRowId, rowElement.dataset.closedLineType);
    }

    function handleClosedRowKeydown(event) {
        if (event.key !== "Enter" && event.key !== " ") {
            return;
        }

        const target = event.target instanceof Element ? event.target : null;
        if (target?.closest(".payroll-proof-cell, a, button, input, label")) {
            return;
        }

        const rowElement = target ? target.closest("tr[data-closed-row-id]") : null;
        if (!rowElement) {
            return;
        }

        event.preventDefault();
        openClosedDetail(rowElement.dataset.closedRowId, rowElement.dataset.closedLineType);
    }

    function renderPaymentProofCell(line) {
        if (line.hasPaymentProof) {
            return `
                <div class="payroll-proof-cell payroll-proof-cell--paid">
                    <span class="payroll-proof-paid">Pagado</span>
                </div>
            `;
        }

        return `
            <div class="payroll-proof-cell">
                <label class="payroll-proof-upload">
                    <input type="file" accept="application/pdf,image/*" data-payment-proof-input data-record-id="${escapeHtml(line.row.payrollRecordId)}" data-payment-type="${escapeHtml(line.type)}" ${state.busy ? "disabled" : ""} />
                    <span>Subir adjunto</span>
                </label>
            </div>
        `;
    }

    async function handlePaymentProofChange(event) {
        const input = event.target;
        if (!(input instanceof HTMLInputElement) || !input.matches("[data-payment-proof-input]")) {
            return;
        }

        const recordId = input.dataset.recordId || "";
        const paymentType = normalizePaymentType(input.dataset.paymentType || "");
        const file = input.files && input.files[0] ? input.files[0] : null;
        if (!recordId || !file) {
            return;
        }

        await uploadPaymentProof(recordId, paymentType, file, input);
    }

    async function uploadPaymentProof(recordId, paymentType, file, input) {
        const row = state.closedRows.find((item) => item.payrollRecordId === recordId);
        if (!row) {
            return;
        }

        if (!uploadPaymentProofUrl) {
            renderStatus("warning", "No esta configurada la ruta para cargar comprobantes.");
            return;
        }

        const statusElement = closedRowsBody?.querySelector(`[data-payment-proof-status="${cssEscape(buildPaymentProofStatusKey(recordId, paymentType))}"]`);
        try {
            setBusy(true);
            if (statusElement) {
                statusElement.textContent = "Cargando comprobante...";
                statusElement.className = "payroll-proof-status payroll-proof-status--info";
            }

            const formData = new FormData();
            formData.append("recordId", recordId);
            formData.append("paymentType", paymentType);
            formData.append("file", file);

            const response = await fetch(uploadPaymentProofUrl, {
                method: "POST",
                body: formData
            });
            const payload = await readPayload(response);
            if (!response.ok) {
                throw createResponseError(payload);
            }

            applyPaymentProofUploadResult(row, payload.paymentType || paymentType, payload.paymentProofFileName || file.name);
            renderClosedRows();
            if (state.activeClosedRowId === row.payrollRecordId) {
                renderDetailPaymentProofLinks(row.detail);
            }
            renderStatus("success", payload.message || "Comprobante de pago cargado correctamente.");
        } catch (error) {
            if (input) {
                input.value = "";
            }

            const payload = error && error.payload ? error.payload : null;
            if (statusElement) {
                statusElement.textContent = payload?.message || error?.message || "No fue posible cargar el comprobante.";
                statusElement.className = "payroll-proof-status payroll-proof-status--danger";
            }

            handleFailure(error);
        } finally {
            setBusy(false);
        }
    }

    function applyPaymentProofUploadResult(row, paymentType, fileName) {
        const normalizedType = normalizePaymentType(paymentType);
        if (normalizedType === "cxc") {
            row.hasCuentaDeCobroPaymentProof = true;
            row.cuentaDeCobroPaymentProofFileName = fileName;
            return;
        }

        row.hasPaymentProof = true;
        row.paymentProofFileName = fileName;
    }

    function buildPaymentProofDownloadUrl(recordId, paymentType) {
        const url = new URL(downloadPaymentProofUrl, window.location.origin);
        url.searchParams.set("recordId", recordId);
        url.searchParams.set("paymentType", normalizePaymentType(paymentType));
        return url.toString();
    }

    function buildPaymentProofStatusKey(recordId, paymentType) {
        return `${recordId}:${normalizePaymentType(paymentType)}`;
    }

    function normalizePaymentType(paymentType) {
        return String(paymentType || "").toLowerCase() === "cxc" ? "cxc" : "nomina";
    }

    function normalizeRow(row) {
        const normalized = {
            ...row,
            warnings: Array.isArray(row.warnings) ? row.warnings.slice() : []
        };

        normalized.employeeContractTypeOptionValue = Number.parseInt(String(normalized.employeeContractTypeOptionValue || 0), 10) || 0;
        normalized.employeeContractTypeLabel = String(normalized.employeeContractTypeLabel || "").trim();
        normalized.isServiceContract = isServiceContract(normalized);
        normalized.manualEditEnabled = Boolean(normalized.manualEditEnabled);
        normalized.manualOverrides = normalizeManualOverrides(normalized.manualOverrides);
        normalized.periodDays = Math.max(Math.round(toPositiveNumber(normalized.periodDays)) || getDaysInPeriod(normalized.periodKey), 1);
        normalized.monthlySalaryBase = toPositiveNumber(normalized.monthlySalaryBase || normalized.salaryBase);
        normalized.monthlyAuxilio = toPositiveNumber(normalized.monthlyAuxilio || normalized.auxilio);
        normalized.workedDays = clampDays(
            normalized.workedDays === undefined || normalized.workedDays === null || normalized.workedDays === ""
                ? normalized.periodDays
                : toPositiveNumber(normalized.workedDays),
            normalized.periodDays);
        normalized.absenceReason = normalizeAbsenceReason(normalized.absenceReason);
        normalized.absencePayment = toPositiveNumber(normalized.absencePayment);
        normalized.novelties = normalizeNovelties(normalized);
        normalized.bonusCompliance = toPositiveNumber(normalized.bonusCompliance);
        normalized.nonCommissionBonus = toPositiveNumber(normalized.nonCommissionBonus);
        normalized.nonCommissionBonusWithholding = toPositiveNumber(normalized.nonCommissionBonusWithholding);
        normalized.applyNonCommissionBonusWithholding = Boolean(normalized.applyNonCommissionBonusWithholding || normalized.nonCommissionBonusWithholding > 0);
        normalized.nonCommissionBonusWithholdingRate = normalizeRate(
            normalized.nonCommissionBonusWithholdingRate
            || inferRate(normalized.nonCommissionBonusWithholding, normalized.nonCommissionBonus)
            || defaultExternalWithholdingRate);
        normalized.otherDeductions = toPositiveNumber(normalized.otherDeductions);
        normalized.loan = toPositiveNumber(normalized.loan);
        normalized.payrollWithholding = toPositiveNumber(normalized.payrollWithholding);
        normalized.externalWithholding = toPositiveNumber(normalized.externalWithholding);
        normalized.applyExternalWithholding = Boolean(normalized.applyExternalWithholding || normalized.externalWithholding > 0);
        normalized.externalWithholdingRate = normalizeRate(
            normalized.externalWithholdingRate
            || inferRate(normalized.externalWithholding, normalized.cuentaDeCobro)
            || defaultExternalWithholdingRate);
        normalized.verticalBase = toNumber(normalized.verticalBase);
        normalized.baseCopiers = toNumber(normalized.baseCopiers);
        normalized.baseCloud = toNumber(normalized.baseCloud);
        normalized.verified = Boolean(normalized.verified);
        recalculateRow(normalized);
        return normalized;
    }

    function recalculateRow(row) {
        row.periodDays = getPeriodDays(row);
        row.monthlySalaryBase = toPositiveNumber(row.monthlySalaryBase || row.salaryBase);
        row.monthlyAuxilio = toPositiveNumber(row.monthlyAuxilio || row.auxilio);
        row.workedDays = clampDays(row.workedDays, row.periodDays);
        row.absenceDays = roundMoney(Math.max(row.periodDays - row.workedDays, 0));
        row.novelties = normalizeNovelties(row);
        if (row.absenceDays <= 0) {
            row.absenceReason = "";
            row.absencePayment = 0;
            row.novelties = [];
        } else {
            row.absenceReason = buildAbsenceReasonSummary(row);
            row.absencePayment = roundMoney(row.novelties.reduce((sum, novelty) => sum + toPositiveNumber(novelty.payment), 0));
        }
        row.absenceReasonLabel = buildAbsenceReasonLabel(row);

        row.salaryBase = resolveManualValue(row, "salaryBase", roundMoney(row.monthlySalaryBase * row.workedDays / row.periodDays));
        row.auxilio = resolveManualValue(row, "auxilio", roundMoney(row.monthlyAuxilio * row.workedDays / row.periodDays));
        row.absencePayment = resolveManualValue(row, "absencePayment", row.absencePayment);
        row.commissionsCopiers = resolveManualValue(row, "commissionsCopiers", toPositiveNumber(row.commissionsCopiers));
        row.commissionsCloud = resolveManualValue(row, "commissionsCloud", toPositiveNumber(row.commissionsCloud));
        row.commissionsUnassigned = resolveManualValue(row, "commissionsUnassigned", toPositiveNumber(row.commissionsUnassigned));
        row.commissionCap = toPositiveNumber(row.commissionCap);
        row.factorCopiers = toPositiveNumber(row.factorCopiers);
        row.factorCloud = toPositiveNumber(row.factorCloud);
        row.healthRate = toPositiveNumber(row.healthRate);
        row.pensionRate = toPositiveNumber(row.pensionRate);
        const serviceContract = isServiceContract(row);
        if (serviceContract) {
            applyServiceContractDeductionRule(row);
            row.healthRate = 0;
            row.pensionRate = 0;
        }

        row.nonCommissionBonus = toPositiveNumber(row.nonCommissionBonus);
        row.applyNonCommissionBonusWithholding = row.nonCommissionBonus > 0 && Boolean(row.applyNonCommissionBonusWithholding);
        row.nonCommissionBonusWithholdingRate = row.applyNonCommissionBonusWithholding
            ? normalizeRate(row.nonCommissionBonusWithholdingRate || defaultExternalWithholdingRate)
            : 0;
        row.nonCommissionBonusWithholding = row.applyNonCommissionBonusWithholding
            ? roundMoney(row.nonCommissionBonus * row.nonCommissionBonusWithholdingRate)
            : 0;
        row.commissions = roundMoney(row.commissionsCopiers + row.commissionsCloud + row.commissionsUnassigned);
        row.appliedCommissionBase = resolveManualValue(row, "appliedCommissionBase", roundMoney(row.commissionCap > 0 ? Math.min(row.commissions, row.commissionCap) : row.commissions));
        row.cuentaDeCobro = resolveManualValue(row, "cuentaDeCobro", roundMoney(row.commissionCap > 0 ? Math.max(row.commissions - row.commissionCap, 0) : 0));
        row.applyExternalWithholding = row.cuentaDeCobro > 0 && Boolean(row.applyExternalWithholding);
        row.externalWithholdingRate = row.applyExternalWithholding ? normalizeRate(row.externalWithholdingRate || defaultExternalWithholdingRate) : 0;
        row.contributionBase = resolveManualValue(row, "contributionBase", serviceContract
            ? 0
            : roundMoney(row.salaryBase + row.absencePayment + row.bonusCompliance + row.nonCommissionBonus + row.appliedCommissionBase));
        row.health = resolveManualValue(row, "health", roundMoney(row.contributionBase * row.healthRate));
        row.pension = resolveManualValue(row, "pension", roundMoney(row.contributionBase * row.pensionRate));
        row.grossSalary = resolveManualValue(row, "grossSalary", roundMoney(row.salaryBase + row.auxilio + row.absencePayment + row.bonusCompliance + row.nonCommissionBonus + row.appliedCommissionBase));
        row.netPayroll = resolveManualValue(row, "netPayroll", roundMoney(row.grossSalary - (row.health + row.pension + row.otherDeductions + row.loan + row.payrollWithholding + row.nonCommissionBonusWithholding)));
        row.externalWithholding = row.applyExternalWithholding ? roundMoney(row.cuentaDeCobro * row.externalWithholdingRate) : 0;
        row.netCuentaDeCobro = resolveManualValue(row, "netCuentaDeCobro", roundMoney(row.cuentaDeCobro - row.externalWithholding));
        row.verticalBase = resolveManualValue(row, "verticalBase", roundMoney(row.netPayroll - row.appliedCommissionBase));
        row.baseCopiers = resolveManualValue(row, "baseCopiers", roundMoney(row.verticalBase * (row.factorCopiers / 100)));
        row.baseCloud = resolveManualValue(row, "baseCloud", roundMoney(row.verticalBase * (row.factorCloud / 100)));
        row.totalCopiers = resolveManualValue(row, "totalCopiers", roundMoney(row.baseCopiers + row.commissionsCopiers));
        row.totalCloud = resolveManualValue(row, "totalCloud", roundMoney(row.baseCloud + row.commissionsCloud));
    }

    function recalculateClosedVerticalDistribution(row) {
        if (!row) {
            return;
        }

        row.factorCopiers = toPositiveNumber(row.factorCopiers);
        row.factorCloud = toPositiveNumber(row.factorCloud);
        row.verticalBase = roundMoney(toNumber(row.verticalBase));
        row.commissionsCopiers = roundMoney(toNumber(row.commissionsCopiers));
        row.commissionsCloud = roundMoney(toNumber(row.commissionsCloud));
        row.baseCopiers = roundMoney(row.verticalBase * (row.factorCopiers / 100));
        row.baseCloud = roundMoney(row.verticalBase * (row.factorCloud / 100));
        row.totalCopiers = roundMoney(row.baseCopiers + row.commissionsCopiers);
        row.totalCloud = roundMoney(row.baseCloud + row.commissionsCloud);
    }

    function syncClosedRowVerticalTotals(closedRow) {
        if (!closedRow || !closedRow.detail) {
            return;
        }

        closedRow.valueCopiers = roundMoney(toNumber(closedRow.detail.totalCopiers));
        closedRow.valueCloud = roundMoney(toNumber(closedRow.detail.totalCloud));
    }

    function updateClosedVerticalDirtyState(closedRow) {
        if (!closedRow || !closedRow.detail) {
            return false;
        }

        closedRow.verticalDistributionDirty = hasClosedVerticalChanges(closedRow);
        return closedRow.verticalDistributionDirty;
    }

    function hasClosedVerticalChanges(closedRow) {
        if (!closedRow || !closedRow.detail) {
            return false;
        }

        return Math.abs(roundMoney(toNumber(closedRow.detail.factorCopiers)) - roundMoney(toNumber(closedRow.savedFactorCopiers))) > 0.01
            || Math.abs(roundMoney(toNumber(closedRow.detail.factorCloud)) - roundMoney(toNumber(closedRow.savedFactorCloud))) > 0.01;
    }

    function getDirtyClosedVerticalRows() {
        return state.closedRows.filter((row) => hasClosedVerticalChanges(row));
    }

    function buildClosedVerticalSavePayload() {
        return {
            rows: getDirtyClosedVerticalRows().map((row) => ({
                payrollRecordId: row.payrollRecordId,
                employeeId: row.employeeId,
                factorCopiers: roundMoney(toNumber(row.detail?.factorCopiers)),
                factorCloud: roundMoney(toNumber(row.detail?.factorCloud))
            }))
        };
    }

    async function saveClosedVerticalChanges() {
        const payload = buildClosedVerticalSavePayload();
        if (!payload.rows.length) {
            setClosedVerticalSaveStatus("No hay cambios pendientes.");
            updateClosedVerticalSaveAvailability();
            return;
        }

        if (!saveClosedVerticalsUrl) {
            renderStatus("warning", "No esta configurada la ruta para guardar la distribucion por vertical.");
            return;
        }

        try {
            setBusy(true);
            setClosedVerticalSaveStatus("Guardando cambios...");
            const response = await fetch(saveClosedVerticalsUrl, {
                method: "POST",
                headers: {
                    "Content-Type": "application/json"
                },
                body: JSON.stringify(payload)
            });
            const result = await readPayload(response);
            if (!response.ok) {
                throw createResponseError(result);
            }

            await loadExistingPeriod({ silent: true });
            setClosedVerticalSaveStatus("");
            renderStatus("success", result.message || "Distribucion por vertical guardada correctamente.");
        } catch (error) {
            setClosedVerticalSaveStatus("No fue posible guardar.");
            handleFailure(error);
        } finally {
            setBusy(false);
            updateClosedVerticalSaveAvailability();
        }
    }

    function renderRows() {
        rowsBody.innerHTML = state.rows.map((row) => {
            return `
                <tr data-row-id="${escapeHtml(row.employeeId)}" class="${buildRowClass(row)}" tabindex="0" role="button" aria-label="Liquidacion de ${escapeHtml(row.employeeName || "empleado")}">
                    <td class="text-center payroll-verified-cell">
                        <label class="payroll-verify-toggle" data-row-edit>
                            <input class="form-check-input" type="checkbox" ${row.verified ? "checked" : ""} data-row-edit data-row-field="verified" aria-label="Verificado ${escapeHtml(row.employeeName || "empleado")}" />
                            <span>Verificado</span>
                        </label>
                    </td>
                    <td>
                        <div class="payroll-row__name">${escapeHtml(row.employeeName || "Empleado sin nombre")}</div>
                        <div class="payroll-row__meta">
                            <span class="payroll-contract-pill ${isServiceContract(row) ? "payroll-contract-pill--service" : "payroll-contract-pill--payroll"}">${escapeHtml(resolveContractTypeLabel(row))}</span>
                            <span data-role="noveltyMeta">${buildRowNoveltyMeta(row)}</span>
                        </div>
                    </td>
                    <td class="text-end" data-role="netPayroll">${formatMoney(row.netPayroll)}</td>
                </tr>
            `;
        }).join("");
    }

    function updateRowOutputs(row, skipField) {
        const tr = rowsBody.querySelector(`tr[data-row-id="${cssEscape(row.employeeId)}"]`);
        if (!tr) {
            return;
        }

        tr.className = buildRowClass(row);
        setCellText(tr, "netPayroll", formatMoney(row.netPayroll));
        const noveltyMeta = tr.querySelector('[data-role="noveltyMeta"]');
        if (noveltyMeta) {
            noveltyMeta.innerHTML = buildRowNoveltyMeta(row);
        }
        setRowCheckboxValue(tr, "verified", row.verified, skipField);
    }

    function openDetail(rowId) {
        if (!detailModal || !rowId) {
            return;
        }

        const row = state.rows.find((item) => item.employeeId === rowId);
        if (!row) {
            return;
        }

        state.activeRowId = row.employeeId;
        state.activeClosedRowId = "";
        state.detailMode = "editable";
        renderDetail(row);
        detailModal.hidden = false;
        document.body.classList.add("payroll-modal-open");

        window.requestAnimationFrame(() => {
            const firstInput = detailInputs[0];
            if (firstInput) {
                firstInput.focus();
                firstInput.select();
            }
        });
    }

    function openClosedDetail(recordId, paymentType) {
        if (!detailModal || !recordId) {
            return;
        }

        const row = state.closedRows.find((item) => item.payrollRecordId === recordId);
        if (!row || !row.detail) {
            return;
        }

        state.activeRowId = "";
        state.activeClosedRowId = row.payrollRecordId;
        state.activeClosedPaymentType = normalizePaymentType(paymentType || "nomina");
        state.detailMode = "closed";
        renderDetail(row.detail, { readOnly: true });
        detailModal.hidden = false;
        document.body.classList.add("payroll-modal-open");

        window.requestAnimationFrame(() => {
            const firstEditableInput = detailInputs.find((input) => !input.disabled);
            if (firstEditableInput) {
                firstEditableInput.focus();
                firstEditableInput.select();
                return;
            }

            const closeButton = detailCloseButtons[detailCloseButtons.length - 1] || detailCloseButtons[0];
            if (closeButton) {
                closeButton.focus();
            }
        });
    }

    function closeDetail() {
        if (!detailModal) {
            return;
        }

        detailModal.hidden = true;
        state.activeRowId = "";
        state.activeClosedRowId = "";
        state.activeClosedPaymentType = "";
        state.detailMode = "";
        detailModal.classList.remove("payroll-modal--readonly");
        document.body.classList.remove("payroll-modal-open");
    }

    function getActiveDetailRow() {
        if (isClosedDetailMode() || !state.activeRowId) {
            return null;
        }

        return state.rows.find((item) => item.employeeId === state.activeRowId) || null;
    }

    function getActiveClosedRow() {
        if (!isClosedDetailMode() || !state.activeClosedRowId) {
            return null;
        }

        return state.closedRows.find((item) => item.payrollRecordId === state.activeClosedRowId) || null;
    }

    function getActiveClosedDetailRow() {
        return getActiveClosedRow()?.detail || null;
    }

    function renderDetail(row, options) {
        const readOnly = Boolean(options?.readOnly || isClosedDetailMode());
        if (detailModal) {
            detailModal.classList.toggle("payroll-modal--readonly", readOnly);
        }

        if (detailManualEditor) {
            detailManualEditor.hidden = readOnly;
        }

        if (detailTitle) {
            detailTitle.textContent = row.employeeName || "Empleado sin nombre";
        }

        if (detailSubtitle) {
            detailSubtitle.textContent = readOnly
                ? [resolveContractTypeLabel(row), row.periodLabel || row.periodKey || "", row.paymentDateDisplay || ""]
                    .filter(Boolean)
                    .join(" | ")
                : `${resolveContractTypeLabel(row)} | ${row.employeeId || ""}`;
        }

        if (detailMeta) {
            detailMeta.textContent = isClosedDetailMode() && row.existingPayrollRecordId
                ? `Registro Dataverse: ${row.existingPayrollRecordId} | Ajusta porcentajes para recalcular verticales`
                : row.existingPayrollRecordId
                ? `Registro Dataverse: ${row.existingPayrollRecordId}`
                : "Sin registro previo";
        }

        renderDetailInputs(row);
        renderNoveltyEditor(row);
        renderDetailValues(row);
        renderManualEditor(row);
        renderDetailWarnings(row);
        renderDetailPaymentProofLinks(row);
    }

    function renderDetailInputs(row, skipField) {
        if (detailExternalWithholdingWrap) {
            detailExternalWithholdingWrap.hidden = toPositiveNumber(row.cuentaDeCobro) <= 0;
        }

        if (detailNonCommissionBonusWithholdingWrap) {
            detailNonCommissionBonusWithholdingWrap.classList.toggle("payroll-withholding-line--disabled", toPositiveNumber(row.nonCommissionBonus) <= 0);
        }

        detailInputs.forEach((input) => {
            const field = input.dataset.detailField;
            if (!field) {
                return;
            }

            input.disabled = shouldDisableDetailInput(input, row);
            if (skipField && field === skipField) {
                return;
            }

            if (input instanceof HTMLInputElement && input.type === "checkbox") {
                input.checked = Boolean(row[field]);
            } else if (field === "nonCommissionBonusWithholdingRate" || field === "externalWithholdingRate") {
                input.value = toRateInputValue(row[field]);
            } else {
                input.value = toInputValue(row[field]);
            }
        });
    }

    function renderNoveltyEditor(row, focusTarget) {
        if (!detailNoveltiesWrap || !detailNoveltiesList || !detailNoveltiesEmpty) {
            return;
        }

        const readOnly = isClosedDetailMode();
        const hasAbsence = toPositiveNumber(row.absenceDays) > 0;
        detailNoveltiesWrap.hidden = !hasAbsence;
        if (!hasAbsence) {
            detailNoveltiesList.innerHTML = "";
            detailNoveltiesEmpty.hidden = false;
            if (detailNoveltyCoverage) {
                detailNoveltyCoverage.textContent = "Dias cubiertos: 0 de 0.";
                detailNoveltyCoverage.className = "";
            }
            return;
        }

        const novelties = normalizeNovelties(row);
        const coverage = getNoveltyCoverage(row);
        if (detailNoveltyCoverage) {
            detailNoveltyCoverage.textContent = buildNoveltyCoverageText(coverage);
            detailNoveltyCoverage.className = buildNoveltyCoverageClass(coverage);
        }

        if (detailAddNoveltyBtn) {
            detailAddNoveltyBtn.disabled = state.busy || readOnly;
            detailAddNoveltyBtn.hidden = readOnly;
        }

        detailNoveltiesEmpty.hidden = novelties.length > 0;
        detailNoveltiesList.innerHTML = novelties.map((novelty, index) => {
            const hint = getNoveltyPaymentHint(novelty);
            const paymentDisabled = readOnly || state.busy || isUnpaidNovelty(novelty);
            return `
                <div class="payroll-novelty-row" data-novelty-index="${index}">
                    <label class="payroll-novelty-field payroll-novelty-field--reason">
                        <span>Motivo</span>
                        <select class="form-select payroll-detail-input" data-novelty-field="reason" ${readOnly || state.busy ? "disabled" : ""}>
                            <option value="">Selecciona motivo</option>
                            ${Object.keys(absenceReasonLabels).map((key) => `
                                <option value="${escapeHtml(key)}" ${normalizeAbsenceReason(novelty.reason) === key ? "selected" : ""}>${escapeHtml(absenceReasonLabels[key])}</option>
                            `).join("")}
                        </select>
                    </label>
                    <label class="payroll-novelty-field payroll-novelty-field--days">
                        <span>Dias</span>
                        <input class="form-control payroll-detail-input text-end" type="number" min="0" step="0.01" value="${toInputValue(novelty.days)}" data-novelty-field="days" ${readOnly || state.busy ? "disabled" : ""} />
                    </label>
                    <label class="payroll-novelty-field payroll-novelty-field--payment">
                        <span>Valor</span>
                        <input class="form-control payroll-detail-input text-end" type="number" min="0" step="0.01" value="${toInputValue(novelty.payment)}" data-novelty-field="payment" ${paymentDisabled ? "disabled" : ""} />
                    </label>
                    <button type="button" class="btn btn-outline-secondary btn-sm payroll-novelty-remove" data-novelty-remove ${readOnly || state.busy ? "disabled" : ""} ${readOnly ? "hidden" : ""} aria-label="Quitar novedad">Quitar</button>
                    ${hint ? `<div class="payroll-novelty-hint">${escapeHtml(hint)}</div>` : ""}
                </div>
            `;
        }).join("");

        const normalizedFocus = typeof focusTarget === "number"
            ? { index: focusTarget, field: "reason" }
            : focusTarget;
        if (normalizedFocus && Number.isInteger(normalizedFocus.index) && normalizedFocus.index >= 0) {
            const field = normalizedFocus.field || "reason";
            const activeInput = detailNoveltiesList.querySelector(`[data-novelty-index="${normalizedFocus.index}"] [data-novelty-field="${cssEscape(field)}"]`);
            if (activeInput instanceof HTMLInputElement || activeInput instanceof HTMLSelectElement) {
                activeInput.focus();
                if (activeInput instanceof HTMLInputElement) {
                    try {
                        activeInput.setSelectionRange(activeInput.value.length, activeInput.value.length);
                    } catch {
                    }
                }
            }
        }
    }

    function renderDetailValues(row) {
        setDetailOutput("operation", row.operation === "closed" ? "Liquidada" : row.operation === "update" ? "Actualizar" : "Crear");
        setDetailOutput("contractType", resolveContractTypeLabel(row));
        setDetailOutput("periodDays", formatNumber(row.periodDays));
        setDetailOutput("workedDays", formatNumber(row.workedDays));
        setDetailOutput("absenceDays", formatNumber(row.absenceDays));
        setDetailOutput("noveltyDays", formatNumber(getNoveltyCoverage(row).noveltyDays));
        setDetailOutput("absenceReason", row.absenceReasonLabel || getAbsenceReasonLabel(row.absenceReason) || "-");
        setDetailOutput("absencePayment", formatMoney(row.absencePayment));
        setDetailOutput("monthlySalaryBase", formatMoney(row.monthlySalaryBase));
        setDetailOutput("monthlyAuxilio", formatMoney(row.monthlyAuxilio));
        setDetailOutput("salaryBase", formatMoney(row.salaryBase));
        setDetailOutput("auxilio", formatMoney(row.auxilio));
        setDetailOutput("commissions", formatMoney(row.commissions));
        setDetailOutput("commissionsCopiers", formatMoney(row.commissionsCopiers));
        setDetailOutput("commissionsCloud", formatMoney(row.commissionsCloud));
        setDetailOutput("commissionsUnassigned", formatMoney(row.commissionsUnassigned));
        setDetailOutput("commissionCap", formatMoney(row.commissionCap));
        setDetailOutput("appliedCommissionBase", formatMoney(row.appliedCommissionBase));
        setDetailOutput("contributionBase", formatMoney(row.contributionBase));
        setDetailOutput("health", formatMoney(row.health));
        setDetailOutput("pension", formatMoney(row.pension));
        setDetailOutput("bonusCompliance", formatMoney(row.bonusCompliance));
        setDetailOutput("nonCommissionBonus", formatMoney(row.nonCommissionBonus));
        setDetailOutput("nonCommissionBonusWithholding", formatMoney(row.nonCommissionBonusWithholding));
        setDetailOutput("nonCommissionBonusWithholdingRate", formatPercent(row.nonCommissionBonusWithholdingRate * 100));
        setDetailOutput("otherDeductions", formatMoney(row.otherDeductions));
        setDetailOutput("loan", formatMoney(row.loan));
        setDetailOutput("payrollWithholding", formatMoney(row.payrollWithholding));
        setDetailOutput("cuentaDeCobro", formatMoney(row.cuentaDeCobro));
        setDetailOutput("externalWithholding", formatMoney(row.externalWithholding));
        setDetailOutput("externalWithholdingRate", formatPercent(row.externalWithholdingRate * 100));
        setDetailOutput("grossSalary", formatMoney(row.grossSalary));
        setDetailOutput("netPayroll", formatMoney(row.netPayroll));
        setDetailOutput("netCuentaDeCobro", formatMoney(row.netCuentaDeCobro));
        setDetailOutput("totalDisbursement", formatMoney(roundMoney(row.netPayroll + row.netCuentaDeCobro)));
        setDetailOutput("verticalBase", formatMoney(row.verticalBase));
        setDetailOutput("factorCopiers", formatPercent(row.factorCopiers));
        setDetailOutput("baseCopiers", formatMoney(row.baseCopiers));
        setDetailOutput("commissionsCopiersVertical", formatMoney(row.commissionsCopiers));
        setDetailOutput("totalCopiers", formatMoney(row.totalCopiers));
        setDetailOutput("factorCloud", formatPercent(row.factorCloud));
        setDetailOutput("baseCloud", formatMoney(row.baseCloud));
        setDetailOutput("commissionsCloudVertical", formatMoney(row.commissionsCloud));
        setDetailOutput("totalCloud", formatMoney(row.totalCloud));
        setDetailOutput("verticalTotal", formatMoney(roundMoney(row.totalCopiers + row.totalCloud)));
    }

    function renderManualEditor(row, skipField) {
        if (isClosedDetailMode()) {
            if (detailManualEditToggle) {
                detailManualEditToggle.checked = false;
                detailManualEditToggle.disabled = true;
            }

            if (detailManualEditPanel) {
                detailManualEditPanel.hidden = true;
            }

            if (detailManualFields) {
                detailManualFields.innerHTML = "";
            }

            return;
        }

        if (detailManualEditToggle) {
            detailManualEditToggle.checked = Boolean(row.manualEditEnabled);
            detailManualEditToggle.disabled = state.busy;
        }

        if (detailManualEditPanel) {
            detailManualEditPanel.hidden = !row.manualEditEnabled;
        }

        if (!detailManualFields) {
            return;
        }

        if (!row.manualEditEnabled) {
            detailManualFields.innerHTML = "";
            return;
        }

        const overrides = ensureManualOverrides(row);
        detailManualFields.innerHTML = manualEditableFields.map((definition) => {
            const overridden = Object.prototype.hasOwnProperty.call(overrides, definition.field);
            const value = overridden ? overrides[definition.field] : row[definition.field];
            return `
                <label class="payroll-manual-field ${overridden ? "payroll-manual-field--active" : ""}">
                    <span>${escapeHtml(definition.label)}</span>
                    <input class="form-control payroll-detail-input text-end" type="number" step="0.01" value="${toManualInputValue(value)}" data-manual-field="${escapeHtml(definition.field)}" ${state.busy ? "disabled" : ""} />
                </label>
            `;
        }).join("");

        if (skipField) {
            const activeInput = detailManualFields.querySelector(`[data-manual-field="${cssEscape(skipField)}"]`);
            if (activeInput instanceof HTMLInputElement) {
                activeInput.focus();
                try {
                    activeInput.setSelectionRange(activeInput.value.length, activeInput.value.length);
                } catch {
                    // Algunos navegadores no permiten seleccionar texto en inputs numericos.
                }
            }
        }
    }

    function renderDetailWarnings(row) {
        if (!detailWarnings) {
            return;
        }

        const warnings = isClosedDetailMode()
            ? Array.from(new Set([...(Array.isArray(row.warnings) ? row.warnings : []), ...buildVerticalFactorWarnings(row)]))
            : getRowWarnings(row);
        if (warnings.length === 0) {
            detailWarnings.hidden = true;
            detailWarnings.innerHTML = "";
            return;
        }

        detailWarnings.hidden = false;
        detailWarnings.innerHTML = warnings
            .map((warning) => `<span class="payroll-warning-tag">${escapeHtml(warning)}</span>`)
            .join("");
    }

    function buildVerticalFactorWarnings(row) {
        const factorTotal = roundMoney(toPositiveNumber(row?.factorCopiers) + toPositiveNumber(row?.factorCloud));
        return Math.abs(factorTotal - 100) > 0.01
            ? [`La suma de porcentajes Copiers/Cloud es ${formatPercent(factorTotal)}.`]
            : [];
    }

    function renderDetailPaymentProofLinks() {
        if (!detailPaymentProofSection || !detailPaymentProofList) {
            return;
        }

        if (!isClosedDetailMode()) {
            detailPaymentProofSection.hidden = true;
            detailPaymentProofList.innerHTML = "";
            return;
        }

        const closedRow = getActiveClosedRow();
        const entries = buildDetailPaymentProofEntries(closedRow, state.activeClosedPaymentType);
        detailPaymentProofSection.hidden = entries.length === 0;
        detailPaymentProofList.innerHTML = entries.map((entry) => {
            const content = entry.hasPaymentProof && downloadPaymentProofUrl
                ? `<a class="payroll-detail-proof-link" href="${escapeHtml(buildPaymentProofDownloadUrl(closedRow.payrollRecordId, entry.type))}" target="_blank" rel="noopener">${escapeHtml(entry.fileName || "Descargar comprobante")}</a>`
                : `<span class="payroll-detail-proof-empty">Sin comprobante cargado</span>`;
            return `
                <div class="payroll-detail-proof-item">
                    <span>${escapeHtml(entry.label)}</span>
                    ${content}
                </div>
            `;
        }).join("");
    }

    function buildDetailPaymentProofEntries(closedRow, paymentType) {
        if (!closedRow || !closedRow.payrollRecordId) {
            return [];
        }

        const normalizedType = normalizePaymentType(paymentType || "nomina");
        const entries = [{
            type: "nomina",
            label: "Nomina",
            hasPaymentProof: Boolean(closedRow.hasPaymentProof),
            fileName: closedRow.paymentProofFileName
        }];
        if (toPositiveNumber(closedRow.detail?.netCuentaDeCobro) > 0) {
            entries.push({
                type: "cxc",
                label: "Cuenta de cobro",
                hasPaymentProof: Boolean(closedRow.hasCuentaDeCobroPaymentProof),
                fileName: closedRow.cuentaDeCobroPaymentProofFileName
            });
        }

        return entries.filter((entry) => entry.type === normalizedType);
    }

    function setDetailOutput(role, value) {
        const elements = detailOutputs[role];
        if (Array.isArray(elements)) {
            elements.forEach((element) => {
                element.textContent = value;
            });
            return;
        }

        if (elements) {
            elements.textContent = value;
        }
    }

    function renderSummary() {
        const employees = state.rows.length;
        const totalPayroll = roundMoney(state.rows.reduce((sum, row) => sum + row.netPayroll, 0));
        const totalCuentaCobro = roundMoney(state.rows.reduce((sum, row) => sum + row.netCuentaDeCobro, 0));
        const totalDisbursement = roundMoney(totalPayroll + totalCuentaCobro);
        const totalCopiers = roundMoney(state.rows.reduce((sum, row) => sum + row.totalCopiers, 0));
        const totalCloud = roundMoney(state.rows.reduce((sum, row) => sum + row.totalCloud, 0));

        summaryEmployees.textContent = String(employees);
        summaryPayroll.textContent = formatMoney(totalPayroll);
        summaryCuentaCobro.textContent = formatMoney(totalCuentaCobro);
        summaryDisbursement.textContent = formatMoney(totalDisbursement);
        summaryCopiers.textContent = formatMoney(totalCopiers);
        summaryCloud.textContent = formatMoney(totalCloud);
    }

    function renderVerticals() {
        verticalsBody.innerHTML = state.rows.map((row) => `
            <tr data-vertical-row-id="${escapeHtml(row.employeeId)}" class="${buildVerticalRowClass(row)}" tabindex="0" role="button" aria-label="Detalle vertical de ${escapeHtml(row.employeeName || "empleado")}">
                <td>
                    <div class="payroll-row__name">${escapeHtml(row.employeeName || "Empleado sin nombre")}</div>
                    <div class="payroll-row__meta">${escapeHtml(resolveContractTypeLabel(row))}</div>
                </td>
                <td class="text-end payroll-vertical-cell payroll-vertical-cell--total" data-role="verticalTotal">${formatMoney(calculateRowVerticalTotal(row))}</td>
                <td class="text-end payroll-vertical-cell payroll-vertical-cell--copiers" data-role="totalCopiers">${formatMoney(row.totalCopiers)}</td>
                <td class="text-end payroll-vertical-cell payroll-vertical-cell--cloud" data-role="totalCloud">${formatMoney(row.totalCloud)}</td>
            </tr>
        `).join("") + renderVerticalTotalsRow();
    }

    function updateVerticalOutputs(row, skipField) {
        const tr = verticalsBody.querySelector(`tr[data-vertical-row-id="${cssEscape(row.employeeId)}"]`);
        if (!tr) {
            renderVerticals();
            return;
        }

        tr.className = buildVerticalRowClass(row);
        setCellText(tr, "verticalTotal", formatMoney(calculateRowVerticalTotal(row)));
        setCellText(tr, "totalCopiers", formatMoney(row.totalCopiers));
        setCellText(tr, "totalCloud", formatMoney(row.totalCloud));
        updateVerticalTotalsRow();
    }

    function renderVerticalTotalsRow() {
        const totals = calculateVerticalTotals();
        return `
            <tr class="payroll-vertical-total-row" data-vertical-summary-row>
                <td>Total</td>
                <td class="text-end payroll-vertical-cell payroll-vertical-cell--total" data-summary-role="verticalTotal">${formatMoney(totals.verticalTotal)}</td>
                <td class="text-end payroll-vertical-cell payroll-vertical-cell--copiers" data-summary-role="totalCopiers">${formatMoney(totals.totalCopiers)}</td>
                <td class="text-end payroll-vertical-cell payroll-vertical-cell--cloud" data-summary-role="totalCloud">${formatMoney(totals.totalCloud)}</td>
            </tr>
        `;
    }

    function updateVerticalTotalsRow() {
        const row = verticalsBody.querySelector("[data-vertical-summary-row]");
        if (!row) {
            return;
        }

        const totals = calculateVerticalTotals();
        setSummaryCellText(row, "verticalTotal", formatMoney(totals.verticalTotal));
        setSummaryCellText(row, "totalCopiers", formatMoney(totals.totalCopiers));
        setSummaryCellText(row, "totalCloud", formatMoney(totals.totalCloud));
    }

    function calculateVerticalTotals() {
        const totals = state.rows.reduce((totals, row) => {
            totals.verticalTotal += calculateRowVerticalTotal(row);
            totals.verticalBase += toNumber(row.verticalBase);
            totals.baseCopiers += toNumber(row.baseCopiers);
            totals.commissionsCopiers += toNumber(row.commissionsCopiers);
            totals.totalCopiers += toNumber(row.totalCopiers);
            totals.baseCloud += toNumber(row.baseCloud);
            totals.commissionsCloud += toNumber(row.commissionsCloud);
            totals.totalCloud += toNumber(row.totalCloud);
            totals.commissionsUnassigned += toNumber(row.commissionsUnassigned);
            return totals;
        }, {
            verticalTotal: 0,
            verticalBase: 0,
            baseCopiers: 0,
            commissionsCopiers: 0,
            totalCopiers: 0,
            baseCloud: 0,
            commissionsCloud: 0,
            totalCloud: 0,
            commissionsUnassigned: 0
        });

        Object.keys(totals).forEach((key) => {
            totals[key] = roundMoney(totals[key]);
        });

        return totals;
    }

    function calculateRowVerticalTotal(row) {
        return roundMoney(toNumber(row.totalCopiers) + toNumber(row.totalCloud));
    }

    function renderLogs() {
        if (!Array.isArray(state.logs) || state.logs.length === 0) {
            logsEmpty.hidden = false;
            logsList.innerHTML = "";
            logsCard.hidden = true;
            return;
        }

        logsCard.hidden = false;
        logsEmpty.hidden = true;
        logsList.innerHTML = state.logs.map((log) => `
            <article class="payroll-log payroll-log--${escapeHtml(log.level || "info")}">
                <div class="payroll-log__header">
                    <span class="payroll-badge payroll-badge--${resolveLogBadge(log.level)}">${escapeHtml(log.level || "info")}</span>
                    <strong>${escapeHtml(log.employeeName || "Proceso general")}</strong>
                    <span>${escapeHtml(log.operation || "operacion")}</span>
                </div>
                <div class="payroll-log__grid">
                    <div><span>Empleado</span><strong>${escapeHtml(log.employeeId || "-")}</strong></div>
                    <div><span>Tabla</span><strong>${escapeHtml(log.tableName || "-")}</strong></div>
                    <div><span>Campo</span><strong>${escapeHtml(log.fieldName || "-")}</strong></div>
                    <div><span>Registro</span><strong>${escapeHtml(log.recordId || "-")}</strong></div>
                </div>
                <p class="payroll-log__message">${escapeHtml(log.message || "Sin detalle principal.")}</p>
                ${log.detail ? `<p class="payroll-log__detail">${escapeHtml(log.detail)}</p>` : ""}
                ${log.offendingValue ? `<p class="payroll-log__detail"><strong>Dato:</strong> ${escapeHtml(log.offendingValue)}</p>` : ""}
                ${log.suggestion ? `<p class="payroll-log__suggestion">${escapeHtml(log.suggestion)}</p>` : ""}
            </article>
        `).join("");
    }

    function buildRowClass(row) {
        const classes = ["payroll-row"];
        if (getRowWarnings(row).length > 0) {
            classes.push("payroll-row--warning");
        }

        if (row.verified) {
            classes.push("payroll-row--verified");
        }

        return classes.join(" ");
    }

    function buildRowNoveltyMeta(row) {
        const coverage = getNoveltyCoverage(row);
        if (coverage.absenceDays <= 0) {
            return "";
        }

        let label = `${formatNumber(coverage.noveltyDays)} dias novedades`;
        let tone = "ok";
        if (coverage.missingDays > 0) {
            label = `Falta ${formatDayCount(coverage.missingDays)}`;
            tone = "warning";
        } else if (coverage.excessDays > 0) {
            label = `Sobran ${formatDayCount(coverage.excessDays)}`;
            tone = "warning";
        }

        return `<span class="payroll-row-novelty payroll-row-novelty--${tone}">${escapeHtml(label)}</span>`;
    }

    function buildVerticalRowClass(row) {
        const classes = ["payroll-vertical-row"];
        if (getRowWarnings(row).length > 0) {
            classes.push("payroll-vertical-row--warning");
        }

        return classes.join(" ");
    }

    function getRowWarnings(row) {
        const manualActive = Boolean(row.manualEditEnabled)
            && Object.keys(normalizeManualOverrides(row.manualOverrides)).length > 0;
        const warnings = Array.isArray(row.warnings)
            ? row.warnings.filter((warning) => {
                const normalized = normalizeText(warning);
                if (normalized.includes("suma de porcentajes")) {
                    return false;
                }

                return !normalized.includes("edicion manual activa") || manualActive;
            })
            : [];
        if (manualActive && !warnings.some((warning) => normalizeText(warning).includes("edicion manual activa"))) {
            warnings.push("Edicion manual activa; los valores marcados reemplazan el calculo automatico de esta fila.");
        }

        if (row.netPayroll < 0) {
            warnings.push("El monto pagado de nomina quedo negativo.");
        }

        if (row.netCuentaDeCobro < 0) {
            warnings.push("El monto de cuenta de cobro quedo negativo.");
        }

        const coverage = getNoveltyCoverage(row);
        if (coverage.absenceDays > 0) {
            if (normalizeNovelties(row).length === 0) {
                warnings.push("Hay dias no trabajados sin novedades registradas.");
            }

            if (coverage.missingDays > 0) {
                warnings.push(`Hace falta liquidar ${formatDayCount(coverage.missingDays)} del mes.`);
            } else if (coverage.excessDays > 0) {
                warnings.push(`Las novedades exceden los dias del mes por ${formatDayCount(coverage.excessDays)}.`);
            }

            if (normalizeNovelties(row).some((novelty) => toPositiveNumber(novelty.days) > 0 && !normalizeAbsenceReason(novelty.reason))) {
                warnings.push("Hay novedades con dias pendientes de motivo.");
            }
        }

        const factorTotal = roundMoney(toPositiveNumber(row.factorCopiers) + toPositiveNumber(row.factorCloud));
        if (Math.abs(factorTotal - 100) > 0.01) {
            warnings.push(`La suma de porcentajes Copiers/Cloud es ${formatPercent(factorTotal)}.`);
        }

        return Array.from(new Set(warnings));
    }

    function handleFailure(error) {
        const payload = error && error.payload ? error.payload : null;
        const message = payload && payload.message
            ? payload.message
            : "No fue posible completar la operacion.";
        const detailParts = [];

        if (payload && payload.detail) {
            detailParts.push(payload.detail);
        } else if (error && error.message) {
            detailParts.push(error.message);
        }

        if (payload && payload.traceId) {
            detailParts.push(`TraceId: ${payload.traceId}`);
        }

        renderStatus("danger", message, detailParts.join(" | "));
        state.logs = payload && Array.isArray(payload.logs) ? payload.logs.slice() : [];
        renderLogs();
    }

    function createResponseError(payload) {
        const error = new Error(payload && payload.message ? payload.message : "Respuesta no valida.");
        error.payload = payload;
        return error;
    }

    async function readPayload(response) {
        const contentType = response.headers.get("content-type") || "";
        if (contentType.includes("application/json")) {
            return await response.json();
        }

        const text = await response.text();
        return {
            message: text
        };
    }

    function ensureInputs() {
        if (!periodInput.value) {
            throw new Error("Debes seleccionar el mes a liquidar.");
        }

        if (!paymentDateInput.value) {
            throw new Error("Debes seleccionar la fecha de pago.");
        }
    }

    function clearState() {
        closeDetail();
        state.rows = [];
        state.closedRows = [];
        state.logs = [];
        state.closedMode = false;
        app.classList.remove("payroll-shell--closed");
        if (closedCard) {
            closedCard.hidden = true;
        }

        summarySection.hidden = true;
        rowsCard.hidden = true;
        verticalsCard.hidden = true;
        logsCard.hidden = true;
        periodLabel.textContent = "";
        if (closedPeriodLabel) {
            closedPeriodLabel.textContent = "";
        }

        rowsBody.innerHTML = "";
        if (closedRowsBody) {
            closedRowsBody.innerHTML = "";
        }
        setClosedVerticalSaveStatus("");
        verticalsBody.innerHTML = "";
        logsList.innerHTML = "";
        updateClosedVerticalSaveAvailability();
        updateConfirmAvailability();
    }

    function markRowPendingVerification(row) {
        if (row) {
            row.verified = false;
        }
    }

    function areAllRowsVerified() {
        return state.rows.length > 0 && state.rows.every((row) => Boolean(row.verified));
    }

    function updateConfirmAvailability() {
        previewBtn.disabled = state.busy || state.closedMode;
        if (state.closedMode) {
            confirmBtn.disabled = true;
            confirmBtn.title = "Este mes ya tiene nomina en Dataverse.";
            if (saveDraftBtn) {
                saveDraftBtn.disabled = true;
            }
            return;
        }

        const hasRows = state.rows.length > 0;
        const hasPendingVerification = hasRows && !areAllRowsVerified();
        const blockingWarning = getFirstBlockingCoverageWarning();
        confirmBtn.disabled = state.busy || !hasRows || hasPendingVerification || Boolean(blockingWarning);
        confirmBtn.title = blockingWarning
            ? blockingWarning
            : hasPendingVerification
            ? "Marca todas las filas como Verificado antes de confirmar."
            : "";

        if (saveDraftBtn) {
            saveDraftBtn.disabled = state.busy || !hasRows;
        }
    }

    function saveDraft(showStatus) {
        if (state.closedMode) {
            return;
        }

        if (!state.rows.length) {
            clearSavedDraft();
            return;
        }

        const savedAt = new Date().toISOString();
        const draft = buildDraftPayload(savedAt);
        const localResult = persistLocalDraft(draft);
        state.draftSavedAt = savedAt;
        updateConfirmAvailability();

        if (draftUrl) {
            queueSharedDraftSave(draft, Boolean(showStatus));
            return;
        }

        if (showStatus) {
            renderStatus(
                localResult.ok ? "success" : "warning",
                localResult.ok ? "Borrador guardado." : "No fue posible guardar el borrador en este navegador.",
                localResult.ok ? `Ultimo guardado: ${formatDraftDate(savedAt)}.` : (localResult.error?.message || ""));
        }
    }

    async function restoreDraft() {
        const sharedDraft = await loadSharedDraft();
        if (isValidDraft(sharedDraft)) {
            loadDraft(sharedDraft, "Borrador compartido restaurado.", buildDraftDetail(sharedDraft));
            persistLocalDraft(sharedDraft);
            return;
        }

        const localDraft = readBestLocalDraft();
        if (isValidDraft(localDraft)) {
            loadDraft(localDraft, "Borrador local restaurado.", buildDraftDetail(localDraft));
            if (draftUrl) {
                void saveSharedDraft(localDraft, true, "Borrador local restaurado y compartido.");
            }
            return;
        }

        updateConfirmAvailability();
    }

    function clearSavedDraft() {
        const periodKeyToDelete = periodInput.value || state.rows[0]?.periodKey || "";
        clearPendingSharedDraftSave();
        clearLocalDrafts();
        state.draftSavedAt = "";
        updateConfirmAvailability();
        deleteSharedDraft(periodKeyToDelete);
    }

    function buildDraftPayload(savedAt) {
        return {
            version: draftVersion,
            savedAt,
            periodKey: periodInput.value,
            paymentDateValue: paymentDateInput.value,
            periodLabel: state.rows[0]?.periodLabel || "",
            paymentDateDisplay: state.rows[0]?.paymentDateDisplay || "",
            rows: state.rows,
            logs: state.logs
        };
    }

    function loadDraft(draft, message, detail) {
        periodInput.value = draft.periodKey || periodInput.value;
        paymentDateInput.value = draft.paymentDateValue || paymentDateInput.value;
        state.draftSavedAt = draft.savedAt || "";

        try {
            state.restoringDraft = true;
            loadResult({
                periodLabel: draft.periodLabel || draft.rows[0]?.periodLabel || "",
                paymentDateValue: draft.paymentDateValue || "",
                paymentDateDisplay: draft.paymentDateDisplay || draft.rows[0]?.paymentDateDisplay || "",
                rows: draft.rows,
                logs: Array.isArray(draft.logs) ? draft.logs : []
            }, false);
        } finally {
            state.restoringDraft = false;
        }

        renderStatus("info", message || "Borrador de preliquidacion restaurado.", detail || buildDraftDetail(draft));
    }

    async function loadSharedDraft() {
        if (!draftUrl) {
            return null;
        }

        try {
            const response = await fetch(draftUrl, {
                headers: {
                    "Accept": "application/json"
                }
            });
            const payload = await readPayload(response);
            if (!response.ok) {
                throw createResponseError(payload);
            }

            return payload && payload.draft ? payload.draft : null;
        } catch (error) {
            console.warn("No fue posible cargar el borrador compartido de nomina.", error);
            return null;
        }
    }

    function queueSharedDraftSave(draft, showStatus) {
        if (!draftUrl) {
            return;
        }

        if (showStatus) {
            clearPendingSharedDraftSave();
            void saveSharedDraft(draft, true);
            return;
        }

        pendingSharedDraft = draft;
        window.clearTimeout(sharedDraftSaveTimer);
        sharedDraftSaveTimer = window.setTimeout(() => {
            const draftToSave = pendingSharedDraft;
            pendingSharedDraft = null;
            if (draftToSave) {
                void saveSharedDraft(draftToSave, false);
            }
        }, 900);
    }

    async function saveSharedDraft(draft, showStatus, successMessage) {
        if (!draftUrl || !isValidDraft(draft)) {
            return false;
        }

        try {
            const response = await fetch(draftUrl, {
                method: "POST",
                headers: {
                    "Content-Type": "application/json"
                },
                body: JSON.stringify(draft)
            });
            const payload = await readPayload(response);
            if (!response.ok) {
                throw createResponseError(payload);
            }

            const savedDraft = payload && payload.draft ? payload.draft : draft;
            if (isValidDraft(savedDraft)) {
                state.draftSavedAt = savedDraft.savedAt || state.draftSavedAt;
                persistLocalDraft(savedDraft);
            }

            if (showStatus) {
                renderStatus("success", successMessage || "Borrador compartido guardado.", buildDraftDetail(savedDraft));
            }

            return true;
        } catch (error) {
            if (showStatus) {
                renderStatus(
                    "warning",
                    "Borrador guardado solo en este navegador.",
                    `No fue posible publicarlo para los demas usuarios. ${error?.message || ""}`.trim());
            } else {
                console.warn("No fue posible guardar el borrador compartido de nomina.", error);
            }

            return false;
        }
    }

    function deleteSharedDraft(periodKey) {
        if (!draftUrl || !periodKey) {
            return;
        }

        const url = buildDraftUrl(periodKey);
        fetch(url, {
            method: "DELETE"
        }).catch((error) => {
            console.warn("No fue posible borrar el borrador compartido de nomina.", error);
        });
    }

    function buildDraftUrl(periodKey) {
        const url = new URL(draftUrl, window.location.origin);
        if (periodKey) {
            url.searchParams.set("periodKey", periodKey);
        }

        return url.toString();
    }

    function clearPendingSharedDraftSave() {
        window.clearTimeout(sharedDraftSaveTimer);
        pendingSharedDraft = null;
    }

    function persistLocalDraft(draft) {
        try {
            window.localStorage.setItem(draftStorageKey, JSON.stringify(draft));
            return { ok: true };
        } catch (error) {
            return { ok: false, error };
        }
    }

    function readBestLocalDraft() {
        const drafts = getLocalDraftKeys()
            .map((key) => readLocalDraft(key))
            .filter((draft) => isValidDraft(draft));

        drafts.sort((left, right) => {
            return getDraftTime(right) - getDraftTime(left);
        });

        return drafts[0] || null;
    }

    function readLocalDraft(key) {
        try {
            const rawDraft = window.localStorage.getItem(key) || "";
            return rawDraft ? JSON.parse(rawDraft) : null;
        } catch {
            return null;
        }
    }

    function getLocalDraftKeys() {
        const keys = new Set([draftStorageKey, legacyDraftStorageKey]);
        try {
            for (let index = 0; index < window.localStorage.length; index += 1) {
                const key = window.localStorage.key(index);
                if (key && key.startsWith(draftStorageKeyPrefix)) {
                    keys.add(key);
                }
            }
        } catch {
            // El borrador local es solo respaldo; si no se puede enumerar, seguimos con las llaves conocidas.
        }

        return Array.from(keys);
    }

    function clearLocalDrafts() {
        getLocalDraftKeys().forEach((key) => {
            try {
                window.localStorage.removeItem(key);
            } catch {
                // El borrador es una ayuda local; si el navegador lo bloquea, la vista puede seguir funcionando.
            }
        });
    }

    function isValidDraft(draft) {
        return Boolean(draft)
            && Number(draft.version) === draftVersion
            && Array.isArray(draft.rows)
            && draft.rows.length > 0;
    }

    function getDraftTime(draft) {
        const time = Date.parse(draft?.savedAt || "");
        return Number.isFinite(time) ? time : 0;
    }

    function buildDraftDetail(draft) {
        const parts = [];
        if (draft?.savedAt) {
            parts.push(`Ultimo guardado: ${formatDraftDate(draft.savedAt)}.`);
        }

        const savedBy = String(draft?.savedByName || draft?.savedByEmail || "").trim();
        if (savedBy) {
            parts.push(`Guardado por: ${savedBy}.`);
        }

        return parts.join(" ");
    }

    function renderStatus(type, message, detail) {
        if (!message && !detail) {
            statusBanner.className = "payroll-status";
            statusBanner.textContent = "";
            return;
        }

        statusBanner.className = `payroll-status payroll-status--${type || "info"}`;
        statusBanner.innerHTML = `
            <strong>${escapeHtml(message || "")}</strong>
            ${detail ? `<span>${escapeHtml(detail)}</span>` : ""}
        `;
    }

    function setClosedVerticalSaveStatus(message) {
        if (closedVerticalsSaveStatus) {
            closedVerticalsSaveStatus.textContent = message || "";
        }
    }

    function updateClosedVerticalSaveAvailability() {
        if (!saveClosedVerticalsBtn) {
            return;
        }

        const pendingCount = getDirtyClosedVerticalRows().length;
        saveClosedVerticalsBtn.disabled = state.busy || !state.closedMode || pendingCount === 0;
        saveClosedVerticalsBtn.textContent = pendingCount > 0
            ? `Guardar cambios (${pendingCount})`
            : "Guardar cambios";
        saveClosedVerticalsBtn.title = pendingCount > 0
            ? "Guarda los porcentajes Copiers/Cloud en Dataverse como enteros."
            : "No hay cambios pendientes por guardar.";
    }

    function setBusy(isBusy) {
        state.busy = isBusy;
        previewBtn.disabled = isBusy || state.closedMode;
        resetBtn.disabled = isBusy;
        closedRowsBody?.querySelectorAll("[data-payment-proof-input]").forEach((input) => {
            input.disabled = isBusy;
        });
        const detailRow = getActiveDetailRow() || getActiveClosedDetailRow();
        detailInputs.forEach((input) => {
            input.disabled = detailRow ? shouldDisableDetailInput(input, detailRow) : isBusy;
        });
        if (detailManualEditToggle) {
            detailManualEditToggle.disabled = isBusy || isClosedDetailMode();
        }

        if (detailManualResetBtn) {
            detailManualResetBtn.disabled = isBusy || isClosedDetailMode();
        }

        detailManualFields?.querySelectorAll("input").forEach((input) => {
            input.disabled = isBusy || isClosedDetailMode();
        });
        updateClosedVerticalSaveAvailability();
        updateConfirmAvailability();
    }

    function setCellText(tr, role, value) {
        const cell = tr.querySelector(`[data-role="${role}"]`);
        if (cell) {
            cell.textContent = value;
        }
    }

    function setSummaryCellText(tr, role, value) {
        const cell = tr.querySelector(`[data-summary-role="${role}"]`);
        if (cell) {
            cell.textContent = value;
        }
    }

    function setRowInputValue(tr, field, value, skipField) {
        if (skipField === field) {
            return;
        }

        const input = tr.querySelector(`[data-row-field="${field}"]`);
        if (input instanceof HTMLInputElement) {
            input.value = toInputValue(value);
        }
    }

    function setVerticalInputValue(tr, field, value, skipField) {
        if (skipField === field) {
            return;
        }

        const input = tr.querySelector(`[data-vertical-field="${field}"]`);
        if (input instanceof HTMLInputElement) {
            input.value = toInputValue(value);
        }
    }

    function setRowCheckboxValue(tr, field, value, skipField) {
        if (skipField === field) {
            return;
        }

        const input = tr.querySelector(`[data-row-field="${field}"]`);
        if (input instanceof HTMLInputElement) {
            input.checked = Boolean(value);
        }
    }

    function resolveLogBadge(level) {
        const normalized = String(level || "").toLowerCase();
        if (normalized === "error") {
            return "danger";
        }

        if (normalized === "warning") {
            return "warning";
        }

        if (normalized === "success") {
            return "success";
        }

        return "info";
    }

    function ensureManualOverrides(row) {
        if (!row.manualOverrides || typeof row.manualOverrides !== "object" || Array.isArray(row.manualOverrides)) {
            row.manualOverrides = {};
        }

        return row.manualOverrides;
    }

    function normalizeManualOverrides(overrides) {
        const normalized = {};
        if (!overrides || typeof overrides !== "object" || Array.isArray(overrides)) {
            return normalized;
        }

        manualEditableFields.forEach((definition) => {
            if (!Object.prototype.hasOwnProperty.call(overrides, definition.field)) {
                return;
            }

            const value = toNumber(overrides[definition.field]);
            if (Number.isFinite(value)) {
                normalized[definition.field] = roundMoney(value);
            }
        });

        return normalized;
    }

    function buildManualOverridesPayload(row) {
        if (!row.manualEditEnabled) {
            return {};
        }

        return normalizeManualOverrides(row.manualOverrides);
    }

    function createNovelty(source) {
        const novelty = source && typeof source === "object" ? source : {};
        const reason = normalizeAbsenceReason(novelty.reason);
        const unpaid = reason === "no_remunerado";
        return {
            reason,
            days: roundMoney(toPositiveNumber(novelty.days)),
            payment: unpaid ? 0 : roundMoney(toPositiveNumber(novelty.payment)),
            paymentManual: unpaid ? false : Boolean(novelty.paymentManual)
        };
    }

    function normalizeNovelties(row) {
        const source = Array.isArray(row?.novelties) ? row.novelties : [];
        let normalized = source
            .map((novelty) => createNovelty(novelty))
            .filter((novelty) => hasNoveltyData(novelty));

        const periodDays = getPeriodDays(row || {});
        const workedDays = clampDays(row?.workedDays, periodDays);
        const absenceDays = roundMoney(Math.max(periodDays - workedDays, 0));
        if (normalized.length === 0 && absenceDays > 0) {
            const legacyReason = normalizeAbsenceReason(row?.absenceReason);
            const legacyPayment = toPositiveNumber(row?.absencePayment);
            if (legacyReason || legacyPayment > 0) {
                normalized = [createNovelty({
                    reason: legacyReason,
                    days: toPositiveNumber(row?.absenceDays) || absenceDays,
                    payment: legacyPayment,
                    paymentManual: true
                })];
            }
        }

        normalized.forEach((novelty) => {
            if (!novelty.paymentManual) {
                novelty.payment = calculateNoveltyPayment(row, novelty);
            }
        });

        return normalized;
    }

    function hasNoveltyData(novelty) {
        return Boolean(normalizeAbsenceReason(novelty.reason))
            || toPositiveNumber(novelty.days) > 0
            || toPositiveNumber(novelty.payment) > 0;
    }

    function getNoveltyAt(row, index) {
        row.novelties = normalizeNovelties(row);
        return Number.isInteger(index) && index >= 0 ? row.novelties[index] : null;
    }

    function buildNoveltiesPayload(row) {
        return normalizeNovelties(row).map((novelty) => ({
            reason: normalizeAbsenceReason(novelty.reason),
            days: roundMoney(toPositiveNumber(novelty.days)),
            payment: roundMoney(toPositiveNumber(novelty.payment))
        }));
    }

    function getNoveltyCoverage(row) {
        const periodDays = getPeriodDays(row || {});
        const workedDays = clampDays(row?.workedDays, periodDays);
        const absenceDays = roundMoney(Math.max(periodDays - workedDays, 0));
        const noveltyDays = roundMoney(normalizeNovelties(row).reduce((sum, novelty) => sum + toPositiveNumber(novelty.days), 0));
        const coveredDays = roundMoney(workedDays + noveltyDays);
        return {
            periodDays,
            workedDays,
            absenceDays,
            noveltyDays,
            coveredDays,
            missingDays: roundMoney(Math.max(periodDays - coveredDays, 0)),
            excessDays: roundMoney(Math.max(coveredDays - periodDays, 0))
        };
    }

    function buildAbsenceReasonSummary(row) {
        const novelties = normalizeNovelties(row)
            .filter((novelty) => toPositiveNumber(novelty.days) > 0 || normalizeAbsenceReason(novelty.reason));
        if (novelties.length === 0) {
            return "";
        }

        if (novelties.length === 1) {
            return normalizeAbsenceReason(novelties[0].reason);
        }

        return novelties.map((novelty) => {
            const label = getAbsenceReasonLabel(novelty.reason) || "Pendiente";
            return `${label}: ${formatDayCount(novelty.days)}`;
        }).join("; ");
    }

    function buildAbsenceReasonLabel(row) {
        const novelties = normalizeNovelties(row)
            .filter((novelty) => toPositiveNumber(novelty.days) > 0 || normalizeAbsenceReason(novelty.reason));
        if (novelties.length === 0) {
            return "";
        }

        if (novelties.length === 1) {
            return getAbsenceReasonLabel(novelties[0].reason);
        }

        return buildAbsenceReasonSummary(row);
    }

    function buildNoveltyCoverageText(coverage) {
        const baseText = `Dias cubiertos: ${formatNumber(coverage.coveredDays)} de ${formatNumber(coverage.periodDays)}.`;
        if (coverage.missingDays > 0) {
            return `${baseText} Hace falta liquidar ${formatDayCount(coverage.missingDays)}.`;
        }

        if (coverage.excessDays > 0) {
            return `${baseText} Sobran ${formatDayCount(coverage.excessDays)} en novedades.`;
        }

        return baseText;
    }

    function buildNoveltyCoverageClass(coverage) {
        const classes = ["payroll-novelty-coverage"];
        if (coverage.missingDays > 0 || coverage.excessDays > 0) {
            classes.push("payroll-novelty-coverage--warning");
        } else {
            classes.push("payroll-novelty-coverage--ok");
        }

        return classes.join(" ");
    }

    function getFirstBlockingCoverageWarning() {
        for (const row of state.rows) {
            const warning = getRowWarnings(row).find((item) => isBlockingCoverageWarning(item));
            if (warning) {
                return `${row.employeeName || "Empleado sin nombre"}: ${warning}`;
            }
        }

        return "";
    }

    function isBlockingCoverageWarning(warning) {
        const normalized = normalizeText(warning);
        return normalized.includes("hace falta liquidar")
            || normalized.includes("exceden los dias")
            || normalized.includes("sin novedades")
            || normalized.includes("pendientes de motivo");
    }

    function hasManualOverride(row, field) {
        return Boolean(row?.manualEditEnabled)
            && row.manualOverrides
            && Object.prototype.hasOwnProperty.call(row.manualOverrides, field);
    }

    function resolveManualValue(row, field, automaticValue) {
        const value = hasManualOverride(row, field)
            ? row.manualOverrides[field]
            : automaticValue;

        const numeric = manualAllowNegativeFields.has(field)
            ? toNumber(value)
            : toPositiveNumber(value);

        return roundMoney(numeric);
    }

    function parseManualInputValue(value, field) {
        const numeric = manualAllowNegativeFields.has(field)
            ? toNumber(value)
            : toPositiveNumber(value);

        return roundMoney(numeric);
    }

    function parseRateInputValue(value) {
        const numeric = toPositiveNumber(value);
        return numeric > 1 ? numeric / 100 : numeric;
    }

    function isServiceDeductionField(field) {
        return deductionFields.has(String(field || ""));
    }

    function shouldDisableDetailInput(input, row) {
        const field = input?.dataset?.detailField || "";
        if (state.busy) {
            return true;
        }

        if (isClosedDetailMode()) {
            return !closedVerticalEditableFields.has(field);
        }

        if (row && isServiceContract(row) && isServiceDeductionField(field)) {
            return true;
        }

        if (field === "nonCommissionBonusWithholdingRate") {
            return !row || toPositiveNumber(row.nonCommissionBonus) <= 0 || !row.applyNonCommissionBonusWithholding;
        }

        if (field === "applyNonCommissionBonusWithholding") {
            return !row || toPositiveNumber(row.nonCommissionBonus) <= 0;
        }

        if (field === "externalWithholdingRate") {
            return !row || toPositiveNumber(row.cuentaDeCobro) <= 0 || !row.applyExternalWithholding;
        }

        if (field === "applyExternalWithholding") {
            return !row || toPositiveNumber(row.cuentaDeCobro) <= 0;
        }

        return false;
    }

    function applyServiceContractDeductionRule(row) {
        if (!row || !isServiceContract(row)) {
            return;
        }

        row.otherDeductions = 0;
        row.loan = 0;
        row.payrollWithholding = 0;
    }

    function isServiceContract(row) {
        if (!row) {
            return false;
        }

        if (row.isServiceContract === true) {
            return true;
        }

        const optionValue = Number.parseInt(String(row.employeeContractTypeOptionValue || row.contractTypeOptionValue || 0), 10) || 0;
        if (optionValue === serviceContractTypeOptionValue) {
            return true;
        }

        const label = normalizeText(row.employeeContractTypeLabel || row.contractTypeLabel || "");
        return label.includes("prestacion") && label.includes("servicio");
    }

    function resolveContractTypeLabel(row) {
        const label = String(row?.employeeContractTypeLabel || row?.contractTypeLabel || "").trim();
        if (label) {
            return label;
        }

        const optionValue = Number.parseInt(String(row?.employeeContractTypeOptionValue || row?.contractTypeOptionValue || 0), 10) || 0;
        if (optionValue === payrollContractTypeOptionValue) {
            return "Nomina";
        }

        return isServiceContract(row) ? "Prestacion de servicios" : "Sin tipo de contrato";
    }

    function normalizeText(value) {
        return String(value || "")
            .trim()
            .normalize("NFD")
            .replace(/[\u0300-\u036f]/g, "")
            .toLowerCase();
    }

    function roundMoney(value) {
        return Math.round((toNumber(value) + Number.EPSILON) * 100) / 100;
    }

    function toNumber(value) {
        const numeric = Number.parseFloat(String(value ?? "0").replace(",", "."));
        return Number.isFinite(numeric) ? numeric : 0;
    }

    function toPositiveNumber(value) {
        const numeric = toNumber(value);
        if (numeric < 0) {
            return 0;
        }

        return numeric;
    }

    function normalizeRate(value) {
        const numeric = toPositiveNumber(value);
        if (numeric <= 0) {
            return defaultExternalWithholdingRate;
        }

        return numeric > 1 ? numeric / 100 : numeric;
    }

    function normalizeClosedRate(value) {
        const numeric = toPositiveNumber(value);
        if (numeric <= 0) {
            return 0;
        }

        return numeric > 1 ? numeric / 100 : numeric;
    }

    function inferRate(amount, base) {
        const numericBase = toPositiveNumber(base);
        if (numericBase <= 0) {
            return 0;
        }

        const numericAmount = toPositiveNumber(amount);
        return numericAmount > 0 ? numericAmount / numericBase : 0;
    }

    function clampDays(value, periodDays) {
        return Math.min(Math.max(toPositiveNumber(value), 0), Math.max(toPositiveNumber(periodDays), 1));
    }

    function getPeriodDays(row) {
        return Math.max(Math.round(toPositiveNumber(row.periodDays)) || getDaysInPeriod(row.periodKey), 1);
    }

    function getDaysInPeriod(periodValue) {
        if (!periodValue || !/^\d{4}-\d{2}$/.test(periodValue)) {
            return 30;
        }

        const year = Number.parseInt(periodValue.substring(0, 4), 10);
        const month = Number.parseInt(periodValue.substring(5, 7), 10);
        return new Date(year, month, 0).getDate();
    }

    function normalizeAbsenceReason(value) {
        const raw = String(value || "").trim().toLowerCase();
        if (Object.prototype.hasOwnProperty.call(absenceReasonLabels, raw)) {
            return raw;
        }

        const normalized = normalizeText(raw)
            .replace(/[_-]+/g, " ")
            .replace(/\s+/g, " ")
            .trim();
        return absenceReasonAliases[normalized] || "";
    }

    function calculateNoveltyPayment(row, novelty) {
        const days = toPositiveNumber(novelty?.days);
        if (days <= 0) {
            return 0;
        }

        const dailySalary = getPeriodDays(row) > 0 ? toPositiveNumber(row.monthlySalaryBase || row.salaryBase) / getPeriodDays(row) : 0;
        switch (normalizeAbsenceReason(novelty?.reason)) {
            case "no_remunerado":
                return 0;
            case "incapacidad":
                return roundMoney((Math.min(days, 2) * dailySalary) + (Math.max(days - 2, 0) * dailySalary * (2 / 3)));
            case "vacaciones":
            case "calamidad":
                return roundMoney(days * dailySalary);
            default:
                return 0;
        }
    }

    function getAbsenceReasonLabel(value) {
        return absenceReasonLabels[normalizeAbsenceReason(value)] || "";
    }

    function getNoveltyPaymentHint(novelty) {
        if (toPositiveNumber(novelty?.days) <= 0 || !normalizeAbsenceReason(novelty?.reason)) {
            return "";
        }

        const reason = normalizeAbsenceReason(novelty.reason);
        if (reason === "no_remunerado") {
            return "Valor en 0; estos dias solo completan la liquidacion del mes.";
        }

        if (reason === "incapacidad") {
            return "Sugerido: primeros 2 dias al 100% y desde el dia 3 al 66.67% del salario diario.";
        }

        if (reason === "vacaciones") {
            return "Sugerido: salario ordinario diario por los dias de vacaciones.";
        }

        if (reason === "calamidad") {
            return "Sugerido: salario ordinario diario; ajusta el valor segun el caso aprobado.";
        }

        return "Sugerido: 0 para dias previos al ingreso.";
    }

    function isUnpaidNovelty(novelty) {
        return normalizeAbsenceReason(novelty?.reason) === "no_remunerado";
    }

    function formatDayCount(value) {
        const days = roundMoney(toPositiveNumber(value));
        const formatted = formatNumber(days);
        return Math.abs(days - 1) <= 0.01 ? `${formatted} dia` : `${formatted} dias`;
    }

    function toInputValue(value) {
        return toPositiveNumber(value).toFixed(2);
    }

    function toRateInputValue(value) {
        const rate = toPositiveNumber(value);
        return rate > 0 ? (rate * 100).toFixed(2) : "";
    }

    function toManualInputValue(value) {
        return toNumber(value).toFixed(2);
    }

    function formatNumber(value) {
        return toNumber(value).toLocaleString("es-CO", {
            minimumFractionDigits: 0,
            maximumFractionDigits: 2
        });
    }

    function formatMoney(value) {
        return new Intl.NumberFormat("es-CO", {
            style: "currency",
            currency: "COP",
            minimumFractionDigits: 2,
            maximumFractionDigits: 2
        }).format(toNumber(value));
    }

    function formatPercent(value) {
        return `${toNumber(value).toLocaleString("es-CO", {
            minimumFractionDigits: 2,
            maximumFractionDigits: 2
        })}%`;
    }

    function formatDraftDate(value) {
        const date = new Date(value);
        if (Number.isNaN(date.getTime())) {
            return "";
        }

        return date.toLocaleString("es-CO", {
            dateStyle: "medium",
            timeStyle: "short"
        });
    }

    function getLastDayOfMonth(periodValue) {
        if (!periodValue || !/^\d{4}-\d{2}$/.test(periodValue)) {
            return "";
        }

        const year = Number.parseInt(periodValue.substring(0, 4), 10);
        const month = Number.parseInt(periodValue.substring(5, 7), 10);
        const date = new Date(year, month, 0);
        const day = String(date.getDate()).padStart(2, "0");
        return `${periodValue}-${day}`;
    }

    function escapeHtml(value) {
        return String(value ?? "")
            .replaceAll("&", "&amp;")
            .replaceAll("<", "&lt;")
            .replaceAll(">", "&gt;")
            .replaceAll("\"", "&quot;")
            .replaceAll("'", "&#39;");
    }

    function cssEscape(value) {
        if (window.CSS && typeof window.CSS.escape === "function") {
            return window.CSS.escape(value);
        }

        return String(value).replaceAll("\"", "\\\"");
    }

    function isClosedDetailMode() {
        return state.detailMode === "closed";
    }

    function buildDraftStorageKey(owner) {
        const normalizedOwner = String(owner || "anonimo")
            .trim()
            .toLowerCase()
            .replace(/[^a-z0-9@._-]+/g, "-")
            .slice(0, 120) || "anonimo";

        return `cotizador.nomina.preliquidacion.${normalizedOwner}`;
    }
})();
