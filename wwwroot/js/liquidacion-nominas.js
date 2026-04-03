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

    const state = {
        rows: [],
        logs: []
    };

    periodInput.value = app.dataset.initialPeriod || "";
    paymentDateInput.value = app.dataset.suggestedPaymentDate || "";

    periodInput.addEventListener("change", () => {
        if (!paymentDateInput.value) {
            paymentDateInput.value = getLastDayOfMonth(periodInput.value);
        }
    });

    previewBtn.addEventListener("click", async () => {
        await requestPreview();
    });

    confirmBtn.addEventListener("click", async () => {
        await confirmPreview();
    });

    resetBtn.addEventListener("click", () => {
        clearState();
        renderStatus("info", "La vista fue limpiada. Selecciona mes y fecha de pago para preparar una nueva liquidacion.");
    });

    rowsBody.addEventListener("input", (event) => {
        const input = event.target;
        if (!(input instanceof HTMLInputElement)) {
            return;
        }

        const rowId = input.dataset.rowId;
        const field = input.dataset.field;
        if (!rowId || !field) {
            return;
        }

        const row = state.rows.find((item) => item.employeeId === rowId);
        if (!row) {
            return;
        }

        row[field] = toPositiveNumber(input.value);
        recalculateRow(row);
        updateRowOutputs(row);
        renderSummary();
        renderVerticals();
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
            adjustments: state.rows.map((row) => ({
                employeeId: row.employeeId,
                bonusCompliance: row.bonusCompliance,
                otherDeductions: row.otherDeductions,
                loan: row.loan,
                payrollWithholding: row.payrollWithholding,
                externalWithholding: row.externalWithholding
            }))
        };
    }

    function loadResult(payload, fromConfirm) {
        state.rows = Array.isArray(payload.rows) ? payload.rows.map((row) => normalizeRow(row)) : [];
        state.logs = Array.isArray(payload.logs) ? payload.logs.slice() : [];

        summarySection.hidden = state.rows.length === 0;
        rowsCard.hidden = state.rows.length === 0;
        verticalsCard.hidden = state.rows.length === 0;
        logsCard.hidden = state.logs.length === 0 && !fromConfirm;
        confirmBtn.disabled = state.rows.length === 0;
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
    }

    function normalizeRow(row) {
        const normalized = {
            ...row,
            warnings: Array.isArray(row.warnings) ? row.warnings.slice() : []
        };

        normalized.bonusCompliance = toPositiveNumber(normalized.bonusCompliance);
        normalized.otherDeductions = toPositiveNumber(normalized.otherDeductions);
        normalized.loan = toPositiveNumber(normalized.loan);
        normalized.payrollWithholding = toPositiveNumber(normalized.payrollWithholding);
        normalized.externalWithholding = toPositiveNumber(normalized.externalWithholding);
        recalculateRow(normalized);
        return normalized;
    }

    function recalculateRow(row) {
        row.salaryBase = toPositiveNumber(row.salaryBase);
        row.auxilio = toPositiveNumber(row.auxilio);
        row.commissionsCopiers = toPositiveNumber(row.commissionsCopiers);
        row.commissionsCloud = toPositiveNumber(row.commissionsCloud);
        row.commissionCap = toPositiveNumber(row.commissionCap);
        row.factorCopiers = toPositiveNumber(row.factorCopiers);
        row.factorCloud = toPositiveNumber(row.factorCloud);
        row.healthRate = toPositiveNumber(row.healthRate);
        row.pensionRate = toPositiveNumber(row.pensionRate);
        row.commissions = roundMoney(row.commissionsCopiers + row.commissionsCloud);
        row.appliedCommissionBase = roundMoney(row.commissionCap > 0 ? Math.min(row.commissions, row.commissionCap) : row.commissions);
        row.cuentaDeCobro = roundMoney(row.commissionCap > 0 ? Math.max(row.commissions - row.commissionCap, 0) : 0);
        row.contributionBase = roundMoney(row.salaryBase + row.bonusCompliance + row.appliedCommissionBase);
        row.health = roundMoney(row.contributionBase * row.healthRate);
        row.pension = roundMoney(row.contributionBase * row.pensionRate);
        row.grossSalary = roundMoney(row.salaryBase + row.auxilio + row.bonusCompliance + row.commissions);
        row.netPayroll = roundMoney(row.grossSalary - (row.health + row.pension + row.otherDeductions + row.loan + row.payrollWithholding));
        row.netCuentaDeCobro = roundMoney(row.cuentaDeCobro - row.externalWithholding);
        row.totalCopiers = roundMoney((row.salaryBase * (row.factorCopiers / 100)) + row.commissionsCopiers);
        row.totalCloud = roundMoney((row.salaryBase * (row.factorCloud / 100)) + row.commissionsCloud);
    }

    function renderRows() {
        rowsBody.innerHTML = state.rows.map((row) => {
            const warningTags = buildWarningTags(row);
            return `
                <tr data-row-id="${escapeHtml(row.employeeId)}" class="${row.warnings.length > 0 ? "payroll-row payroll-row--warning" : "payroll-row"}">
                    <td>
                        <div class="payroll-row__name">${escapeHtml(row.employeeName || "Empleado sin nombre")}</div>
                        <div class="payroll-row__meta">${escapeHtml(row.employeeId)}</div>
                    </td>
                    <td>
                        <span class="payroll-badge ${row.operation === "update" ? "payroll-badge--warning" : "payroll-badge--success"}">${escapeHtml(row.operation || "create")}</span>
                    </td>
                    <td class="text-end">${formatMoney(row.salaryBase)}</td>
                    <td class="text-end">${formatMoney(row.auxilio)}</td>
                    <td class="text-end">${formatMoney(row.commissionsCopiers)}</td>
                    <td class="text-end">${formatMoney(row.commissionsCloud)}</td>
                    <td class="text-end">${buildInput(row, "bonusCompliance")}</td>
                    <td class="text-end" data-role="health">${formatMoney(row.health)}</td>
                    <td class="text-end" data-role="pension">${formatMoney(row.pension)}</td>
                    <td class="text-end">${buildInput(row, "otherDeductions")}</td>
                    <td class="text-end">${buildInput(row, "loan")}</td>
                    <td class="text-end">${buildInput(row, "payrollWithholding")}</td>
                    <td class="text-end" data-role="cuentaDeCobro">${formatMoney(row.cuentaDeCobro)}</td>
                    <td class="text-end">${buildInput(row, "externalWithholding")}</td>
                    <td class="text-end" data-role="netPayroll">${formatMoney(row.netPayroll)}</td>
                    <td class="text-end" data-role="netCuentaDeCobro">${formatMoney(row.netCuentaDeCobro)}</td>
                    <td class="text-end" data-role="totalCopiers">${formatMoney(row.totalCopiers)}</td>
                    <td class="text-end" data-role="totalCloud">${formatMoney(row.totalCloud)}</td>
                    <td>${warningTags}</td>
                </tr>
            `;
        }).join("");
    }

    function updateRowOutputs(row) {
        const tr = rowsBody.querySelector(`tr[data-row-id="${cssEscape(row.employeeId)}"]`);
        if (!tr) {
            return;
        }

        setCellText(tr, "health", formatMoney(row.health));
        setCellText(tr, "pension", formatMoney(row.pension));
        setCellText(tr, "cuentaDeCobro", formatMoney(row.cuentaDeCobro));
        setCellText(tr, "netPayroll", formatMoney(row.netPayroll));
        setCellText(tr, "netCuentaDeCobro", formatMoney(row.netCuentaDeCobro));
        setCellText(tr, "totalCopiers", formatMoney(row.totalCopiers));
        setCellText(tr, "totalCloud", formatMoney(row.totalCloud));
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
        const totalCopiers = roundMoney(state.rows.reduce((sum, row) => sum + row.totalCopiers, 0));
        const totalCloud = roundMoney(state.rows.reduce((sum, row) => sum + row.totalCloud, 0));

        verticalsBody.innerHTML = `
            <tr>
                <td>Copiers</td>
                <td class="text-end">${formatMoney(totalCopiers)}</td>
            </tr>
            <tr>
                <td>Cloud</td>
                <td class="text-end">${formatMoney(totalCloud)}</td>
            </tr>
        `;
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

    function buildInput(row, field) {
        return `<input class="form-control form-control-sm payroll-input text-end"
                       type="number"
                       min="0"
                       step="0.01"
                       value="${toInputValue(row[field])}"
                       data-row-id="${escapeHtml(row.employeeId)}"
                       data-field="${escapeHtml(field)}" />`;
    }

    function buildWarningTags(row) {
        const warnings = Array.isArray(row.warnings) ? row.warnings.slice() : [];
        if (row.netPayroll < 0) {
            warnings.push("El monto pagado de nomina quedo negativo.");
        }

        if (row.netCuentaDeCobro < 0) {
            warnings.push("El monto de cuenta de cobro quedo negativo.");
        }

        if (warnings.length === 0) {
            return "<span class=\"text-muted\">Sin novedades</span>";
        }

        return warnings.map((warning) => `<span class="payroll-warning-tag">${escapeHtml(warning)}</span>`).join("");
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
        state.rows = [];
        state.logs = [];
        summarySection.hidden = true;
        rowsCard.hidden = true;
        verticalsCard.hidden = true;
        logsCard.hidden = true;
        confirmBtn.disabled = true;
        periodLabel.textContent = "";
        rowsBody.innerHTML = "";
        verticalsBody.innerHTML = "";
        logsList.innerHTML = "";
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
        previewBtn.disabled = isBusy;
        confirmBtn.disabled = isBusy || state.rows.length === 0;
        resetBtn.disabled = isBusy;
    }

    function setCellText(tr, role, value) {
        const cell = tr.querySelector(`[data-role="${role}"]`);
        if (cell) {
            cell.textContent = value;
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

    function toInputValue(value) {
        return toPositiveNumber(value).toFixed(2);
    }

    function formatMoney(value) {
        return new Intl.NumberFormat("es-CO", {
            style: "currency",
            currency: "COP",
            minimumFractionDigits: 2,
            maximumFractionDigits: 2
        }).format(toNumber(value));
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
})();
