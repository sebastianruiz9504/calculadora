(function () {
    const app = document.getElementById("cuentasCobroApp");
    if (!app) {
        return;
    }

    const loadUrl = app.dataset.loadUrl || "";
    const saveUrl = app.dataset.saveUrl || "";
    const uploadUrl = app.dataset.uploadUrl || "";
    const downloadUrl = app.dataset.downloadUrl || "";
    const markPrintedUrl = app.dataset.markPrintedUrl || "";
    const printUrl = app.dataset.printUrl || "";

    const yearSelect = document.getElementById("ccbYearSelect");
    const monthSelect = document.getElementById("ccbMonthSelect");
    const refreshBtn = document.getElementById("ccbRefreshBtn");
    const addRowBtn = document.getElementById("ccbAddRowBtn");
    const statusBanner = document.getElementById("ccbStatusBanner");
    const rowsBody = document.getElementById("ccbRowsBody");
    const emptyState = document.getElementById("ccbEmptyState");
    const recordsCount = document.getElementById("ccbRecordsCount");
    const tableTitle = document.getElementById("ccbTableTitle");
    const periodSource = document.getElementById("ccbPeriodSource");
    const summaryCount = document.getElementById("ccbSummaryCount");
    const summaryValorTotal = document.getElementById("ccbSummaryValorTotal");
    const summaryValorPago = document.getElementById("ccbSummaryValorPago");
    const summaryReteValor = document.getElementById("ccbSummaryReteValor");

    const moneyFormatter = new Intl.NumberFormat("es-CO", {
        style: "currency",
        currency: "COP",
        maximumFractionDigits: 2
    });

    const state = {
        year: parseInteger(app.dataset.initialYear, new Date().getFullYear()),
        month: parseInteger(app.dataset.initialMonth, new Date().getMonth() + 1),
        board: null,
        rows: [],
        busy: false,
        sequence: 0
    };

    refreshBtn?.addEventListener("click", async () => {
        await loadBoard(state.year, state.month);
    });

    addRowBtn?.addEventListener("click", () => {
        state.rows.unshift(createEmptyRow());
        renderRows();
        updateSummary();
        renderStatus("info", "Nueva linea lista para diligenciar.");
    });

    yearSelect?.addEventListener("change", async () => {
        const year = parseInteger(yearSelect.value, state.year);
        const month = parseInteger(monthSelect?.value, state.month);
        await loadBoard(year, month);
    });

    monthSelect?.addEventListener("change", async () => {
        const year = parseInteger(yearSelect?.value, state.year);
        const month = parseInteger(monthSelect.value, state.month);
        await loadBoard(year, month);
    });

    rowsBody?.addEventListener("input", (event) => {
        const target = event.target;
        if (!(target instanceof HTMLInputElement)) {
            return;
        }

        const row = resolveRowFromElement(target);
        if (!row) {
            return;
        }

        syncInputIntoRow(row, target);
        const rowElement = target.closest("tr");
        if (rowElement instanceof HTMLTableRowElement) {
            syncDerivedMarkup(row, rowElement);
        }

        updateSummary();
    });

    rowsBody?.addEventListener("change", async (event) => {
        const target = event.target;
        if (!(target instanceof HTMLElement)) {
            return;
        }

        const row = resolveRowFromElement(target);
        if (!row) {
            return;
        }

        if (target instanceof HTMLInputElement && target.dataset.field === "adjunto" && target.files && target.files.length > 0) {
            row.pendingFile = target.files[0];
            renderRows();
            renderStatus("info", `Adjunto listo para guardarse en ${row.receptor || "la nueva linea"}.`);
        }
    });

    rowsBody?.addEventListener("click", async (event) => {
        const target = event.target;
        if (!(target instanceof HTMLElement)) {
            return;
        }

        const actionElement = target.closest("[data-action]");
        if (!(actionElement instanceof HTMLElement)) {
            return;
        }

        const row = resolveRowFromElement(actionElement);
        if (!row) {
            return;
        }

        const action = actionElement.dataset.action || "";
        if (action === "save") {
            await saveRow(row.localId);
            return;
        }

        if (action === "print") {
            await printRow(row.localId);
            return;
        }
    });

    loadBoard(state.year, state.month);

    async function loadBoard(year, month) {
        try {
            setBusy(true);
            renderStatus("info", "Cargando cuentas de cobro...");

            const response = await fetch(`${loadUrl}?year=${encodeURIComponent(year)}&month=${encodeURIComponent(month)}`, {
                headers: {
                    Accept: "application/json"
                }
            });

            const payload = await readPayload(response);
            if (!response.ok) {
                throw createResponseError(payload);
            }

            state.board = payload;
            state.year = payload.selectedYear || year;
            state.month = payload.selectedMonth || month;
            state.rows = Array.isArray(payload.records) ? payload.records.map(hydrateRow) : [];

            renderFilters();
            renderRows();
            updateSummary();
            renderStatus("success", payload.message || "Tabla cargada correctamente.");
        } catch (error) {
            renderStatus("error", buildErrorBannerMessage(error));
        } finally {
            setBusy(false);
        }
    }

    async function saveRow(localId) {
        const row = state.rows.find((item) => item.localId === localId);
        if (!row) {
            return;
        }

        let didSaveRecord = false;
        const pendingFile = row.pendingFile || null;

        const validationMessage = validateRow(row);
        if (validationMessage) {
            renderStatus("error", validationMessage);
            return;
        }

        try {
            setBusy(true);
            renderStatus("info", `Guardando ${row.receptor || "cuenta de cobro"}...`);

            const response = await fetch(saveUrl, {
                method: "POST",
                headers: {
                    "Content-Type": "application/json"
                },
                body: JSON.stringify({
                    recordId: row.recordId,
                    year: state.year,
                    month: state.month,
                    receptor: row.receptor,
                    nitOCedula: row.nitOCedula,
                    valorTotal: row.valorTotal,
                    reteFuentePorcentaje: row.reteFuentePorcentaje,
                    valorPago: row.valorPago
                })
            });

            const payload = await readPayload(response);
            if (!response.ok) {
                throw createResponseError(payload);
            }

            mergeRow(payload.record, row.localId);
            didSaveRecord = true;
            const persistedRow = state.rows.find((item) => item.localId === localId || item.recordId === payload.record?.recordId);
            if (persistedRow && pendingFile) {
                persistedRow.pendingFile = pendingFile;
                await uploadRowAttachment(persistedRow);
            }

            renderRows();
            updateSummary();
            renderStatus("success", payload.message || "Cuenta de cobro guardada correctamente.");
        } catch (error) {
            if (didSaveRecord) {
                renderRows();
                updateSummary();
            }
            renderStatus("error", buildErrorBannerMessage(error));
        } finally {
            setBusy(false);
        }
    }

    async function uploadRowAttachment(row) {
        if (!row.recordId || !row.pendingFile) {
            return;
        }

        const formData = new FormData();
        formData.append("recordId", row.recordId);
        formData.append("file", row.pendingFile);

        const response = await fetch(uploadUrl, {
            method: "POST",
            body: formData
        });

        const payload = await readPayload(response);
        if (!response.ok) {
            throw createResponseError(payload);
        }

        mergeRow(payload.record, row.localId);
    }

    async function printRow(localId) {
        const row = state.rows.find((item) => item.localId === localId);
        if (!row) {
            return;
        }

        if (!row.recordId) {
            renderStatus("error", "Guarda la cuenta de cobro antes de imprimir.");
            return;
        }

        try {
            setBusy(true);

            if (!row.impresa) {
                renderStatus("info", `Marcando ${row.receptor || "la cuenta"} como impresa...`);
                const response = await fetch(markPrintedUrl, {
                    method: "POST",
                    headers: {
                        "Content-Type": "application/json"
                    },
                    body: JSON.stringify(row.recordId)
                });

                const payload = await readPayload(response);
                if (!response.ok) {
                    throw createResponseError(payload);
                }

                mergeRow(payload.record, row.localId);
                renderRows();
                updateSummary();
                renderStatus("success", payload.message || "La cuenta de cobro quedo marcada como impresa.");
            }

            const popup = window.open(`${printUrl}?recordId=${encodeURIComponent(row.recordId)}&autoprint=1`, "_blank", "noopener");
            if (!popup) {
                renderStatus("warning", "El navegador bloqueo la ventana de impresion. Permite popups e intenta de nuevo.");
            }
        } catch (error) {
            renderStatus("error", buildErrorBannerMessage(error));
        } finally {
            setBusy(false);
        }
    }

    function renderFilters() {
        const years = Array.isArray(state.board?.availableYears) ? state.board.availableYears : [state.year];
        yearSelect.innerHTML = years.map((value) => `
            <option value="${escapeHtml(value)}" ${value === state.year ? "selected" : ""}>${escapeHtml(value)}</option>
        `).join("");

        const months = Array.isArray(state.board?.availableMonths) ? state.board.availableMonths : [];
        monthSelect.innerHTML = months.map((item) => `
            <option value="${escapeHtml(item.value)}" ${item.value === state.month ? "selected" : ""}>
                ${escapeHtml(capitalize(item.label || ""))}${item.count ? ` (${escapeHtml(item.count)})` : ""}
            </option>
        `).join("");

        tableTitle.textContent = state.board?.selectedPeriodLabel
            ? `Detalle de ${capitalize(state.board.selectedPeriodLabel)}`
            : "Detalle por periodo";
        periodSource.textContent = state.board?.periodSourceLabel
            ? `El agrupador usa: ${state.board.periodSourceLabel}.`
            : "El agrupador usa el periodo del documento.";
    }

    function renderRows() {
        rowsBody.innerHTML = state.rows.map((row) => buildRowMarkup(row)).join("");
        emptyState.hidden = state.rows.length > 0;
        recordsCount.textContent = `${state.rows.length} ${state.rows.length === 1 ? "fila" : "filas"}`;
    }

    function buildRowMarkup(row) {
        const attachmentText = row.pendingFile
            ? `${row.pendingFile.name} pendiente`
            : row.hasAdjunto
                ? row.adjuntoFileName || "Adjunto cargado"
                : "Sin adjunto";
        const statusText = row.recordId
            ? (row.modifiedOnDisplay ? `Actualizada ${row.modifiedOnDisplay}` : `Creada ${row.createdOnDisplay || ""}`.trim())
            : "Nueva linea";

        return `
            <tr class="ccb-row ${row.totalesCuadran ? "" : "is-invalid"}" data-local-id="${escapeHtml(row.localId)}">
                <td>
                    <input class="form-control ccb-input" type="text" value="${escapeHtml(row.receptor)}" data-field="receptor" />
                </td>
                <td>
                    <input class="form-control ccb-input" type="text" value="${escapeHtml(row.nitOCedula)}" data-field="nitOCedula" />
                </td>
                <td>
                    <input class="form-control ccb-input ccb-input--number" type="number" step="0.01" value="${escapeHtml(formatInputNumber(row.valorTotal))}" data-field="valorTotal" />
                </td>
                <td>
                    <input class="form-control ccb-input ccb-input--number" type="number" step="0.01" value="${escapeHtml(formatInputNumber(row.reteFuentePorcentaje))}" data-field="reteFuentePorcentaje" />
                </td>
                <td>
                    <input class="form-control ccb-input ccb-input--number" type="number" step="0.01" value="${escapeHtml(formatInputNumber(row.valorPago))}" data-field="valorPago" />
                </td>
                <td>
                    <input class="form-control ccb-input ccb-input--number is-readonly" type="number" step="0.01" value="${escapeHtml(formatInputNumber(row.reteFuenteValor))}" data-derived="reteFuenteValor" readonly />
                </td>
                <td>
                    <label class="ccb-check">
                        <input type="checkbox" data-derived="totalesCuadran" ${row.totalesCuadran ? "checked" : ""} disabled />
                        <span class="ccb-check__text" data-derived="totalesCuadranLabel">${row.totalesCuadran ? "Cuadra" : "No cuadra"}</span>
                    </label>
                </td>
                <td>
                    <div class="ccb-attachment">
                        <div class="ccb-attachment__name">${escapeHtml(attachmentText)}</div>
                        ${row.recordId && row.hasAdjunto ? `<a class="ccb-attachment__link" href="${escapeHtml(buildDownloadUrl(row.recordId))}" target="_blank" rel="noopener">Descargar</a>` : `<span class="ccb-attachment__link is-muted">Descargar</span>`}
                        <input class="form-control form-control-sm" type="file" accept=".pdf,.jpg,.jpeg,.png,.doc,.docx" data-field="adjunto" ${state.busy ? "disabled" : ""} />
                    </div>
                </td>
                <td>
                    <button type="button" class="btn ${row.impresa ? "btn-outline-secondary" : "btn-outline-success"} btn-sm" data-action="print" ${!row.recordId || state.busy ? "disabled" : ""}>
                        ${row.impresa ? "Impresa" : "Imprimir"}
                    </button>
                </td>
                <td>
                    <div class="ccb-row-status">${escapeHtml(statusText)}</div>
                    <div class="ccb-row-period">${escapeHtml(row.periodLabel || buildFallbackPeriodLabel())}</div>
                </td>
                <td class="text-end">
                    <button type="button" class="btn btn-primary btn-sm" data-action="save" ${state.busy ? "disabled" : ""}>Guardar</button>
                </td>
            </tr>
        `;
    }

    function syncInputIntoRow(row, input) {
        const field = input.dataset.field || "";
        if (!field) {
            return;
        }

        switch (field) {
            case "receptor":
            case "nitOCedula":
                row[field] = input.value || "";
                break;
            case "valorTotal":
            case "reteFuentePorcentaje":
            case "valorPago":
                row[field] = parseDecimal(input.value);
                recomputeRow(row);
                break;
            default:
                break;
        }
    }

    function syncDerivedMarkup(row, rowElement) {
        const derivedInput = rowElement.querySelector('[data-derived="reteFuenteValor"]');
        if (derivedInput instanceof HTMLInputElement) {
            derivedInput.value = formatInputNumber(row.reteFuenteValor);
        }

        const derivedCheck = rowElement.querySelector('[data-derived="totalesCuadran"]');
        if (derivedCheck instanceof HTMLInputElement) {
            derivedCheck.checked = row.totalesCuadran;
        }

        const derivedLabel = rowElement.querySelector('[data-derived="totalesCuadranLabel"]');
        if (derivedLabel instanceof HTMLElement) {
            derivedLabel.textContent = row.totalesCuadran ? "Cuadra" : "No cuadra";
        }

        rowElement.classList.toggle("is-invalid", !row.totalesCuadran);
    }

    function updateSummary() {
        const rows = Array.isArray(state.rows) ? state.rows : [];
        const totalValorTotal = rows.reduce((total, row) => total + (row.valorTotal || 0), 0);
        const totalValorPago = rows.reduce((total, row) => total + (row.valorPago || 0), 0);
        const totalReteValor = rows.reduce((total, row) => total + (row.reteFuenteValor || 0), 0);

        summaryCount.textContent = String(rows.length);
        summaryValorTotal.textContent = moneyFormatter.format(totalValorTotal);
        summaryValorPago.textContent = moneyFormatter.format(totalValorPago);
        summaryReteValor.textContent = moneyFormatter.format(totalReteValor);
    }

    function mergeRow(record, preferredLocalId) {
        if (!record || !record.recordId) {
            return;
        }

        const incoming = hydrateRow(record);
        const index = state.rows.findIndex((item) => item.localId === preferredLocalId || (item.recordId && item.recordId === record.recordId));
        if (index >= 0) {
            const previous = state.rows[index];
            incoming.localId = previous.localId;
            incoming.pendingFile = null;
            state.rows[index] = incoming;
            return;
        }

        state.rows.unshift(incoming);
    }

    function resolveRowFromElement(element) {
        const rowElement = element.closest("tr[data-local-id]");
        if (!(rowElement instanceof HTMLTableRowElement)) {
            return null;
        }

        const localId = rowElement.dataset.localId || "";
        return state.rows.find((item) => item.localId === localId) || null;
    }

    function createEmptyRow() {
        state.sequence += 1;
        return recomputeRow({
            localId: `new-${state.sequence}`,
            recordId: "",
            receptor: "",
            nitOCedula: "",
            valorTotal: 0,
            reteFuentePorcentaje: 0,
            valorPago: 0,
            reteFuenteValor: 0,
            totalesCuadran: true,
            impresa: false,
            hasAdjunto: false,
            adjuntoFileName: "",
            periodLabel: buildFallbackPeriodLabel(),
            createdOnDisplay: "",
            modifiedOnDisplay: "",
            pendingFile: null
        });
    }

    function hydrateRow(record) {
        return recomputeRow({
            localId: record.recordId || `row-${++state.sequence}`,
            recordId: record.recordId || "",
            receptor: record.receptor || "",
            nitOCedula: record.nitOCedula || "",
            valorTotal: Number(record.valorTotal || 0),
            reteFuentePorcentaje: Number(record.reteFuentePorcentaje || 0),
            valorPago: Number(record.valorPago || 0),
            reteFuenteValor: Number(record.reteFuenteValor || 0),
            totalesCuadran: Boolean(record.totalesCuadran),
            impresa: Boolean(record.impresa),
            hasAdjunto: Boolean(record.hasAdjunto),
            adjuntoFileName: record.adjuntoFileName || "",
            periodLabel: record.periodLabel || buildFallbackPeriodLabel(),
            createdOnDisplay: record.createdOnDisplay || "",
            modifiedOnDisplay: record.modifiedOnDisplay || "",
            pendingFile: null
        });
    }

    function recomputeRow(row) {
        row.valorTotal = roundCurrency(row.valorTotal || 0);
        row.reteFuentePorcentaje = roundCurrency(row.reteFuentePorcentaje || 0);
        row.valorPago = roundCurrency(row.valorPago || 0);
        row.reteFuenteValor = roundCurrency(row.valorTotal * (row.reteFuentePorcentaje / 100));
        row.totalesCuadran = Math.abs(row.valorTotal - (row.valorPago + row.reteFuenteValor)) <= 0.01;
        return row;
    }

    function validateRow(row) {
        if (!row.receptor.trim()) {
            return "Debes diligenciar el receptor.";
        }

        if (!row.nitOCedula.trim()) {
            return "Debes diligenciar el NIT o cedula.";
        }

        if (row.valorTotal <= 0) {
            return "El valor total debe ser mayor a cero.";
        }

        if (row.reteFuentePorcentaje < 0 || row.reteFuentePorcentaje > 100) {
            return "La rete fuente % debe estar entre 0 y 100.";
        }

        if (!row.totalesCuadran) {
            return "El valor total debe ser igual a valor pago + rete fuente valor.";
        }

        return "";
    }

    function buildDownloadUrl(recordId) {
        return `${downloadUrl}?recordId=${encodeURIComponent(recordId)}`;
    }

    function buildFallbackPeriodLabel() {
        const selectedMonth = monthSelect?.selectedOptions?.[0]?.textContent || "";
        const selectedYear = yearSelect?.value || String(state.year || "");
        return [selectedMonth.replace(/\(\d+\)/g, "").trim(), selectedYear].filter(Boolean).join(" ");
    }

    function setBusy(isBusy) {
        state.busy = isBusy;

        if (yearSelect) {
            yearSelect.disabled = isBusy;
        }

        if (monthSelect) {
            monthSelect.disabled = isBusy;
        }

        if (refreshBtn) {
            refreshBtn.disabled = isBusy;
        }

        if (addRowBtn) {
            addRowBtn.disabled = isBusy;
        }

        rowsBody.querySelectorAll("input, button").forEach((element) => {
            if (element instanceof HTMLInputElement || element instanceof HTMLButtonElement) {
                element.disabled = isBusy;
            }
        });
    }

    function renderStatus(level, message) {
        if (!statusBanner) {
            return;
        }

        statusBanner.className = `ccb-status ccb-status--${level} is-visible`;
        statusBanner.textContent = message;
    }

    function roundCurrency(value) {
        return Math.round((Number(value || 0) + Number.EPSILON) * 100) / 100;
    }

    function parseDecimal(value) {
        const parsed = Number.parseFloat(String(value || "").replace(",", "."));
        return Number.isFinite(parsed) ? parsed : 0;
    }

    function parseInteger(value, fallback) {
        const parsed = Number.parseInt(String(value || ""), 10);
        return Number.isFinite(parsed) ? parsed : fallback;
    }

    function formatInputNumber(value) {
        return roundCurrency(value).toFixed(2);
    }

    function capitalize(value) {
        if (!value) {
            return "";
        }

        return value.charAt(0).toUpperCase() + value.slice(1);
    }

    function createResponseError(payload) {
        return {
            message: payload?.message || "La operacion no se pudo completar.",
            detail: payload?.detail || "",
            traceId: payload?.traceId || ""
        };
    }

    function buildErrorBannerMessage(error) {
        const parts = [error?.message || "Ocurrio un error inesperado."];
        if (error?.detail) {
            parts.push(error.detail);
        }

        if (error?.traceId) {
            parts.push(`TraceId: ${error.traceId}`);
        }

        return parts.filter(Boolean).join(" | ");
    }

    async function readPayload(response) {
        const contentType = response.headers.get("content-type") || "";
        if (contentType.includes("application/json")) {
            return await response.json();
        }

        return {
            message: await response.text()
        };
    }

    function escapeHtml(value) {
        return String(value ?? "")
            .replaceAll("&", "&amp;")
            .replaceAll("<", "&lt;")
            .replaceAll(">", "&gt;")
            .replaceAll('"', "&quot;")
            .replaceAll("'", "&#39;");
    }
})();
