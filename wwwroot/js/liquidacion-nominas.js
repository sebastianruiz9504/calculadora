(function () {
    const app = document.getElementById("nominaApp");
    if (!app) {
        return;
    }

    const previewUrl = app.dataset.previewUrl || "";
    const confirmUrl = app.dataset.confirmUrl || "";
    const periodInput = document.getElementById("periodInput");
    const paymentDateInput = document.getElementById("paymentDateInput");
    const previewBtn = document.getElementById("previewNominaBtn");
    const confirmBtn = document.getElementById("confirmNominaBtn");
    const saveDraftBtn = document.getElementById("saveNominaDraftBtn");
    const resetBtn = document.getElementById("resetNominaBtn");
    const statusBanner = document.getElementById("nominaStatusBanner");
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
    const detailAbsenceReasonWrap = document.getElementById("detailAbsenceReasonWrap");
    const detailAbsencePaymentWrap = document.getElementById("detailAbsencePaymentWrap");
    const detailAbsencePaymentLabel = document.getElementById("detailAbsencePaymentLabel");
    const detailAbsencePaymentHint = document.getElementById("detailAbsencePaymentHint");
    const detailManualEditToggle = document.getElementById("detailManualEditToggle");
    const detailManualEditPanel = document.getElementById("detailManualEditPanel");
    const detailManualFields = document.getElementById("detailManualFields");
    const detailManualResetBtn = document.getElementById("detailManualResetBtn");
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
        calamidad: "Calamidad"
    };
    const draftStorageKey = buildDraftStorageKey(app.dataset.draftOwner || "");
    const draftVersion = 4;
    const payrollContractTypeOptionValue = 645250000;
    const serviceContractTypeOptionValue = 645250001;
    const defaultExternalWithholdingRate = 0.04;
    const deductionFields = new Set(["otherDeductions", "loan", "payrollWithholding"]);
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
        { field: "externalWithholding", label: "Rete fuente cxc" },
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

    const state = {
        rows: [],
        logs: [],
        activeRowId: "",
        busy: false,
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
        clearState();
        clearSavedDraft();
        renderStatus("info", "La vista fue limpiada. Selecciona mes y fecha de pago para preparar una nueva liquidacion.");
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

    detailForm?.addEventListener("input", handleDetailFieldChange);
    detailForm?.addEventListener("change", handleDetailFieldChange);
    detailManualEditToggle?.addEventListener("change", handleManualToggleChange);
    detailManualFields?.addEventListener("input", handleManualFieldChange);
    detailManualFields?.addEventListener("change", handleManualFieldChange);
    detailManualResetBtn?.addEventListener("click", resetManualOverrides);

    restoreDraft();

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

        const row = getActiveDetailRow();
        if (!row) {
            return;
        }

        markRowPendingVerification(row);
        if (input instanceof HTMLSelectElement) {
            row[field] = normalizeAbsenceReason(input.value);
            row.absencePayment = calculateAbsencePayment(row);
        } else if (field === "absenceReason") {
            row[field] = normalizeAbsenceReason(input.value);
            row.absencePayment = calculateAbsencePayment(row);
        } else if (field === "workedDays") {
            row[field] = clampDays(toPositiveNumber(input.value), getPeriodDays(row));
            if (row[field] >= getPeriodDays(row)) {
                row.absenceReason = "";
                row.absencePayment = 0;
            } else if (!row.absenceReason) {
                row.absenceReason = "ingreso";
                row.absencePayment = calculateAbsencePayment(row);
            } else {
                row.absencePayment = calculateAbsencePayment(row);
            }
        } else {
            row[field] = toPositiveNumber(input.value);
        }

        recalculateRow(row);
        updateRowOutputs(row);
        renderDetailInputs(row, field);
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
            if (state.rows.length === 0) {
                renderStatus("warning", "Primero debes preparar la preliquidacion.");
                return;
            }

            if (!areAllRowsVerified()) {
                renderStatus("warning", "Marca todas las filas como Verificado antes de confirmar y enviar.");
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
                    factorCopiers: row.factorCopiers,
                    factorCloud: row.factorCloud,
                    bonusCompliance: row.bonusCompliance,
                    otherDeductions: serviceContract ? 0 : row.otherDeductions,
                    loan: serviceContract ? 0 : row.loan,
                    payrollWithholding: serviceContract ? 0 : row.payrollWithholding,
                    externalWithholding: row.externalWithholding
                };
            })
        };
    }

    function loadResult(payload, fromConfirm) {
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
        normalized.bonusCompliance = toPositiveNumber(normalized.bonusCompliance);
        normalized.otherDeductions = toPositiveNumber(normalized.otherDeductions);
        normalized.loan = toPositiveNumber(normalized.loan);
        normalized.payrollWithholding = toPositiveNumber(normalized.payrollWithholding);
        normalized.externalWithholding = toPositiveNumber(normalized.externalWithholding);
        normalized.externalWithholdingRate = normalizeRate(normalized.externalWithholdingRate || defaultExternalWithholdingRate);
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
        if (row.absenceDays <= 0) {
            row.absenceReason = "";
            row.absencePayment = 0;
        } else {
            row.absenceReason = normalizeAbsenceReason(row.absenceReason);
            row.absencePayment = toPositiveNumber(row.absencePayment);
        }
        row.absenceReasonLabel = getAbsenceReasonLabel(row.absenceReason);

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

        row.commissions = roundMoney(row.commissionsCopiers + row.commissionsCloud + row.commissionsUnassigned);
        row.appliedCommissionBase = resolveManualValue(row, "appliedCommissionBase", roundMoney(row.commissionCap > 0 ? Math.min(row.commissions, row.commissionCap) : row.commissions));
        row.cuentaDeCobro = resolveManualValue(row, "cuentaDeCobro", roundMoney(row.commissionCap > 0 ? Math.max(row.commissions - row.commissionCap, 0) : 0));
        row.externalWithholdingRate = row.cuentaDeCobro > 0 ? normalizeRate(row.externalWithholdingRate || defaultExternalWithholdingRate) : 0;
        row.contributionBase = resolveManualValue(row, "contributionBase", serviceContract
            ? 0
            : roundMoney(row.salaryBase + row.absencePayment + row.bonusCompliance + row.appliedCommissionBase));
        row.health = resolveManualValue(row, "health", roundMoney(row.contributionBase * row.healthRate));
        row.pension = resolveManualValue(row, "pension", roundMoney(row.contributionBase * row.pensionRate));
        row.grossSalary = resolveManualValue(row, "grossSalary", roundMoney(row.salaryBase + row.auxilio + row.absencePayment + row.bonusCompliance + row.commissions));
        row.netPayroll = resolveManualValue(row, "netPayroll", roundMoney(row.grossSalary - (row.health + row.pension + row.otherDeductions + row.loan + row.payrollWithholding)));
        row.externalWithholding = resolveManualValue(row, "externalWithholding", row.cuentaDeCobro > 0 ? roundMoney(row.cuentaDeCobro * row.externalWithholdingRate) : 0);
        row.netCuentaDeCobro = resolveManualValue(row, "netCuentaDeCobro", roundMoney(row.cuentaDeCobro - row.externalWithholding));
        row.verticalBase = resolveManualValue(row, "verticalBase", roundMoney(row.netPayroll - row.commissions));
        row.baseCopiers = resolveManualValue(row, "baseCopiers", roundMoney(row.verticalBase * (row.factorCopiers / 100)));
        row.baseCloud = resolveManualValue(row, "baseCloud", roundMoney(row.verticalBase * (row.factorCloud / 100)));
        row.totalCopiers = resolveManualValue(row, "totalCopiers", roundMoney(row.baseCopiers + row.commissionsCopiers));
        row.totalCloud = resolveManualValue(row, "totalCloud", roundMoney(row.baseCloud + row.commissionsCloud));
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

    function closeDetail() {
        if (!detailModal) {
            return;
        }

        detailModal.hidden = true;
        state.activeRowId = "";
        document.body.classList.remove("payroll-modal-open");
    }

    function getActiveDetailRow() {
        if (!state.activeRowId) {
            return null;
        }

        return state.rows.find((item) => item.employeeId === state.activeRowId) || null;
    }

    function renderDetail(row) {
        if (detailTitle) {
            detailTitle.textContent = row.employeeName || "Empleado sin nombre";
        }

        if (detailSubtitle) {
            detailSubtitle.textContent = `${resolveContractTypeLabel(row)} | ${row.employeeId || ""}`;
        }

        if (detailMeta) {
            detailMeta.textContent = row.existingPayrollRecordId
                ? `Registro Dataverse: ${row.existingPayrollRecordId}`
                : "Sin registro previo";
        }

        renderDetailInputs(row);
        renderDetailValues(row);
        renderManualEditor(row);
        renderDetailWarnings(row);
    }

    function renderDetailInputs(row, skipField) {
        const serviceContract = isServiceContract(row);
        detailInputs.forEach((input) => {
            const field = input.dataset.detailField;
            if (!field) {
                return;
            }

            input.disabled = state.busy || field === "externalWithholding" || (serviceContract && isServiceDeductionField(field));
            if (skipField && field === skipField) {
                return;
            }

            if (input instanceof HTMLSelectElement) {
                input.value = normalizeAbsenceReason(row[field]);
            } else {
                input.value = toInputValue(row[field]);
            }
        });

        const hasAbsence = toPositiveNumber(row.absenceDays) > 0;
        if (detailAbsenceReasonWrap) {
            detailAbsenceReasonWrap.hidden = !hasAbsence;
        }

        if (detailAbsencePaymentWrap) {
            detailAbsencePaymentWrap.hidden = !hasAbsence || !row.absenceReason;
        }

        if (detailAbsencePaymentLabel) {
            detailAbsencePaymentLabel.textContent = row.absenceReason
                ? `Valor ${getAbsenceReasonLabel(row.absenceReason).toLowerCase()}`
                : "Valor dias no trabajados";
        }

        if (detailAbsencePaymentHint) {
            detailAbsencePaymentHint.textContent = getAbsencePaymentHint(row);
        }
    }

    function renderDetailValues(row) {
        setDetailOutput("operation", row.operation === "update" ? "Actualizar" : "Crear");
        setDetailOutput("contractType", resolveContractTypeLabel(row));
        setDetailOutput("periodDays", formatNumber(row.periodDays));
        setDetailOutput("workedDays", formatNumber(row.workedDays));
        setDetailOutput("absenceDays", formatNumber(row.absenceDays));
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
        if (detailManualEditToggle) {
            detailManualEditToggle.checked = Boolean(row.manualEditEnabled);
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

        const warnings = getRowWarnings(row);
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

        if (toPositiveNumber(row.absenceDays) > 0 && !normalizeAbsenceReason(row.absenceReason)) {
            warnings.push("Hay dias no trabajados sin motivo.");
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
        state.logs = [];
        summarySection.hidden = true;
        rowsCard.hidden = true;
        verticalsCard.hidden = true;
        logsCard.hidden = true;
        periodLabel.textContent = "";
        rowsBody.innerHTML = "";
        verticalsBody.innerHTML = "";
        logsList.innerHTML = "";
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
        const hasRows = state.rows.length > 0;
        const hasPendingVerification = hasRows && !areAllRowsVerified();
        confirmBtn.disabled = state.busy || !hasRows || hasPendingVerification;
        confirmBtn.title = hasPendingVerification
            ? "Marca todas las filas como Verificado antes de confirmar."
            : "";

        if (saveDraftBtn) {
            saveDraftBtn.disabled = state.busy || !hasRows;
        }
    }

    function saveDraft(showStatus) {
        if (!state.rows.length) {
            clearSavedDraft();
            return;
        }

        const savedAt = new Date().toISOString();
        const draft = {
            version: draftVersion,
            savedAt,
            periodKey: periodInput.value,
            paymentDateValue: paymentDateInput.value,
            periodLabel: state.rows[0]?.periodLabel || "",
            paymentDateDisplay: state.rows[0]?.paymentDateDisplay || "",
            rows: state.rows,
            logs: state.logs
        };

        try {
            window.localStorage.setItem(draftStorageKey, JSON.stringify(draft));
            state.draftSavedAt = savedAt;
            updateConfirmAvailability();
            if (showStatus) {
                renderStatus("success", "Borrador guardado.", `Ultimo guardado: ${formatDraftDate(savedAt)}.`);
            }
        } catch (error) {
            if (showStatus) {
                renderStatus("warning", "No fue posible guardar el borrador en este navegador.", error?.message || "");
            }
        }
    }

    function restoreDraft() {
        let rawDraft = "";
        try {
            rawDraft = window.localStorage.getItem(draftStorageKey) || "";
        } catch {
            return;
        }

        if (!rawDraft) {
            updateConfirmAvailability();
            return;
        }

        let draft;
        try {
            draft = JSON.parse(rawDraft);
        } catch {
            clearSavedDraft();
            return;
        }

        if (!draft || draft.version !== draftVersion || !Array.isArray(draft.rows) || draft.rows.length === 0) {
            clearSavedDraft();
            return;
        }

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

        renderStatus("info", "Borrador de preliquidacion restaurado.", state.draftSavedAt ? `Ultimo guardado: ${formatDraftDate(state.draftSavedAt)}.` : "");
    }

    function clearSavedDraft() {
        try {
            window.localStorage.removeItem(draftStorageKey);
        } catch {
            // El borrador es una ayuda local; si el navegador lo bloquea, la vista puede seguir funcionando.
        }

        state.draftSavedAt = "";
        updateConfirmAvailability();
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

    function setBusy(isBusy) {
        state.busy = isBusy;
        previewBtn.disabled = isBusy;
        resetBtn.disabled = isBusy;
        const detailRow = getActiveDetailRow();
        detailInputs.forEach((input) => {
            const field = input.dataset.detailField;
            input.disabled = isBusy || field === "externalWithholding" || (detailRow && isServiceContract(detailRow) && isServiceDeductionField(field));
        });
        if (detailManualEditToggle) {
            detailManualEditToggle.disabled = isBusy;
        }

        if (detailManualResetBtn) {
            detailManualResetBtn.disabled = isBusy;
        }

        detailManualFields?.querySelectorAll("input").forEach((input) => {
            input.disabled = isBusy;
        });
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

    function isServiceDeductionField(field) {
        return deductionFields.has(String(field || ""));
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
        const normalized = String(value || "").trim().toLowerCase();
        return Object.prototype.hasOwnProperty.call(absenceReasonLabels, normalized) ? normalized : "";
    }

    function calculateAbsencePayment(row) {
        const absenceDays = Math.max(getPeriodDays(row) - clampDays(row.workedDays, getPeriodDays(row)), 0);
        if (absenceDays <= 0) {
            return 0;
        }

        const dailySalary = getPeriodDays(row) > 0 ? toPositiveNumber(row.monthlySalaryBase || row.salaryBase) / getPeriodDays(row) : 0;
        switch (normalizeAbsenceReason(row.absenceReason)) {
            case "incapacidad":
                return roundMoney((Math.min(absenceDays, 2) * dailySalary) + (Math.max(absenceDays - 2, 0) * dailySalary * (2 / 3)));
            case "vacaciones":
            case "calamidad":
                return roundMoney(absenceDays * dailySalary);
            default:
                return 0;
        }
    }

    function getAbsenceReasonLabel(value) {
        return absenceReasonLabels[normalizeAbsenceReason(value)] || "";
    }

    function getAbsencePaymentHint(row) {
        if (toPositiveNumber(row.absenceDays) <= 0 || !row.absenceReason) {
            return "";
        }

        const reason = normalizeAbsenceReason(row.absenceReason);
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

    function toInputValue(value) {
        return toPositiveNumber(value).toFixed(2);
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

    function buildDraftStorageKey(owner) {
        const normalizedOwner = String(owner || "anonimo")
            .trim()
            .toLowerCase()
            .replace(/[^a-z0-9@._-]+/g, "-")
            .slice(0, 120) || "anonimo";

        return `cotizador.nomina.preliquidacion.${normalizedOwner}`;
    }
})();
