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

    const modal = document.getElementById("ccbEditorModal");
    const modalStatus = document.getElementById("ccbModalStatus");
    const modalTitle = document.getElementById("ccbEditorTitle");
    const modalSubtitle = document.getElementById("ccbEditorSubtitle");
    const modalMeta = document.getElementById("ccbEditorMeta");
    const modalCloseBtn = document.getElementById("ccbModalCloseBtn");
    const modalCancelBtn = document.getElementById("ccbModalCancelBtn");
    const modalSaveBtn = document.getElementById("ccbModalSaveBtn");
    const modalPrintBtn = document.getElementById("ccbModalPrintBtn");
    const editorForm = document.getElementById("ccbEditorForm");
    const resultDialog = document.getElementById("ccbResultDialog");
    const resultDialogTitle = document.getElementById("ccbResultDialogTitle");
    const resultDialogMessage = document.getElementById("ccbResultDialogMessage");
    const resultDialogDetail = document.getElementById("ccbResultDialogDetail");
    const resultDialogCloseBtn = document.getElementById("ccbResultDialogCloseBtn");

    const receptorInput = document.getElementById("ccbReceptorInput");
    const nitInput = document.getElementById("ccbNitInput");
    const fechaEmisionInput = document.getElementById("ccbFechaEmisionInput");
    const fechaPagoInput = document.getElementById("ccbFechaPagoInput");
    const retePorcentajeInput = document.getElementById("ccbRetePorcentajeInput");
    const valorTotalInput = document.getElementById("ccbValorTotalInput");
    const valorPagoInput = document.getElementById("ccbValorPagoInput");
    const reteValorInput = document.getElementById("ccbReteValorInput");
    const observacionesInput = document.getElementById("ccbObservacionesInput");
    const validationCard = document.getElementById("ccbValidationCard");
    const validationText = document.getElementById("ccbValidationText");
    const attachmentName = document.getElementById("ccbAttachmentName");
    const attachmentHint = document.getElementById("ccbAttachmentHint");
    const attachmentInput = document.getElementById("ccbAttachmentInput");
    const attachmentDownloadLink = document.getElementById("ccbAttachmentDownloadLink");

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
        sequence: 0,
        editor: {
            isOpen: false,
            localId: "",
            record: null,
            pendingFile: null
        }
    };

    refreshBtn?.addEventListener("click", async () => {
        if (state.busy) {
            return;
        }

        await loadBoard(state.year, state.month);
    });

    addRowBtn?.addEventListener("click", () => {
        if (state.busy) {
            return;
        }

        openEditor("");
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

    rowsBody?.addEventListener("click", async (event) => {
        const target = event.target;
        if (!(target instanceof HTMLElement)) {
            return;
        }

        if (state.busy) {
            return;
        }

        const action = target.closest("[data-action]");
        if (action instanceof HTMLElement) {
            const row = resolveRowFromElement(action);
            if (!row) {
                return;
            }

            if (action.dataset.action === "print") {
                event.stopPropagation();
                await printRecord(row.recordId);
            }

            return;
        }

        const row = resolveRowFromElement(target);
        if (!row) {
            return;
        }

        openEditor(row.localId);
    });

    rowsBody?.addEventListener("keydown", (event) => {
        const target = event.target;
        if (!(target instanceof HTMLElement)) {
            return;
        }

        if (event.key !== "Enter" && event.key !== " ") {
            return;
        }

        const row = resolveRowFromElement(target);
        if (!row) {
            return;
        }

        event.preventDefault();
        openEditor(row.localId);
    });

    [modalCloseBtn, modalCancelBtn].forEach((element) => {
        element?.addEventListener("click", () => {
            closeEditor();
        });
    });

    modal?.addEventListener("click", (event) => {
        const target = event.target;
        if (!(target instanceof HTMLElement)) {
            return;
        }

        if (target.hasAttribute("data-ccb-close")) {
            closeEditor();
        }
    });

    resultDialogCloseBtn?.addEventListener("click", () => {
        hideResultDialog();
    });

    resultDialog?.addEventListener("click", (event) => {
        const target = event.target;
        if (target instanceof HTMLElement && target.hasAttribute("data-ccb-result-close")) {
            hideResultDialog();
        }
    });

    document.addEventListener("keydown", (event) => {
        if (event.key === "Escape" && resultDialog && !resultDialog.hidden) {
            hideResultDialog();
            return;
        }

        if (event.key === "Escape" && state.editor.isOpen && !state.busy) {
            closeEditor();
        }
    });

    [receptorInput, nitInput, fechaEmisionInput, fechaPagoInput, retePorcentajeInput, valorTotalInput, valorPagoInput, observacionesInput].forEach((element) => {
        element?.addEventListener("input", syncEditorFromInputs);
    });

    attachmentInput?.addEventListener("change", () => {
        if (!state.editor.record) {
            return;
        }

        state.editor.pendingFile = attachmentInput?.files?.[0] || null;
        renderEditorFileState();
    });

    editorForm?.addEventListener("submit", async (event) => {
        event.preventDefault();
        await saveEditor();
    });

    modalPrintBtn?.addEventListener("click", async () => {
        if (!state.editor.record?.recordId) {
            showModalStatus("warning", "Guarda el registro antes de imprimir.");
            return;
        }

        await printRecord(state.editor.record.recordId, true);
    });

    loadBoard(state.year, state.month);

    async function loadBoard(year, month, options) {
        const config = {
            silent: false,
            ...options
        };

        try {
            setBusy(true);
            if (!config.silent) {
                renderStatus("info", "Cargando cuentas de cobro...");
            }

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

            if (state.editor.isOpen && state.editor.record?.recordId) {
                const refreshed = findRowByRecordId(state.editor.record.recordId);
                if (refreshed) {
                    state.editor.record = cloneRow(refreshed);
                    renderEditor();
                }
            }

            if (!config.silent) {
                renderStatus("success", payload.message || "Tabla cargada correctamente.");
            }
        } catch (error) {
            renderStatus("error", buildErrorBannerMessage(error));
        } finally {
            setBusy(false);
        }
    }

    function renderFilters() {
        const years = Array.isArray(state.board?.availableYears) ? state.board.availableYears : [state.year];
        if (yearSelect) {
            yearSelect.innerHTML = years.map((value) => `
                <option value="${escapeHtml(value)}" ${value === state.year ? "selected" : ""}>${escapeHtml(value)}</option>
            `).join("");
        }

        const months = Array.isArray(state.board?.availableMonths) ? state.board.availableMonths : [];
        if (monthSelect) {
            monthSelect.innerHTML = months.map((item) => `
                <option value="${escapeHtml(item.value)}" ${item.value === state.month ? "selected" : ""}>
                    ${escapeHtml(capitalize(item.label || ""))}${item.count ? ` (${escapeHtml(item.count)})` : ""}
                </option>
            `).join("");
        }

        tableTitle.textContent = state.board?.selectedPeriodLabel
            ? `Detalle de ${capitalize(state.board.selectedPeriodLabel)}`
            : "Detalle por periodo";
        periodSource.textContent = state.board?.periodSourceLabel
            ? `El filtro usa: ${state.board.periodSourceLabel}.`
            : "El filtro usa la fecha de emision.";
    }

    function renderRows() {
        if (!rowsBody) {
            return;
        }

        rowsBody.innerHTML = state.rows.map((row) => `
            <tr class="ccb-row ${row.totalesCuadran ? "" : "is-invalid"}" data-local-id="${escapeHtml(row.localId)}" tabindex="0">
                <td>
                    <div class="ccb-row__title">${escapeHtml(row.receptor || "Sin receptor")}</div>
                    <div class="ccb-row__subtitle">${escapeHtml(row.nitOCedula || "Sin NIT o cedula")}</div>
                </td>
                <td>
                    <div class="ccb-row__title">${escapeHtml(row.fechaEmisionDisplay || "Sin fecha")}</div>
                    <div class="ccb-row__subtitle">${row.fechaPagoDisplay ? `Pago ${escapeHtml(row.fechaPagoDisplay)}` : "Sin fecha de pago"}</div>
                </td>
                <td class="text-end">${escapeHtml(moneyFormatter.format(Number(row.valorTotal || 0)))}</td>
                <td class="text-end">${escapeHtml(moneyFormatter.format(Number(row.valorPago || 0)))}</td>
                <td>
                    <span class="ccb-pill ${row.totalesCuadran ? "is-success" : "is-danger"}">
                        ${row.totalesCuadran ? "Cuadra" : "No cuadra"}
                    </span>
                </td>
                <td>
                    <button type="button" class="btn ${row.impresa ? "btn-outline-secondary" : "btn-outline-success"} btn-sm" data-action="print" ${!row.recordId || state.busy ? "disabled" : ""}>
                        ${row.impresa ? "Impresa" : "Imprimir"}
                    </button>
                </td>
            </tr>
        `).join("");

        if (emptyState) {
            emptyState.hidden = state.rows.length > 0;
        }

        if (recordsCount) {
            recordsCount.textContent = `${state.rows.length} ${state.rows.length === 1 ? "fila" : "filas"}`;
        }
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

    function openEditor(localId) {
        if (state.busy) {
            return;
        }

        hideResultDialog();

        const sourceRow = localId
            ? state.rows.find((item) => item.localId === localId)
            : null;

        state.editor.localId = localId || "";
        state.editor.record = sourceRow ? cloneRow(sourceRow) : createEmptyRow();
        state.editor.pendingFile = null;
        state.editor.isOpen = true;

        if (modal) {
            modal.hidden = false;
        }

        document.body.classList.add("ccb-modal-open");
        renderEditor();
    }

    function closeEditor(options) {
        const config = {
            force: false,
            ...options
        };

        if (state.busy && !config.force) {
            return;
        }

        state.editor.isOpen = false;
        state.editor.localId = "";
        state.editor.record = null;
        state.editor.pendingFile = null;
        resetAttachmentInput();
        clearModalStatus();
        hideResultDialog();

        if (modal) {
            modal.hidden = true;
        }

        document.body.classList.remove("ccb-modal-open");
    }

    function renderEditor() {
        const record = state.editor.record;
        if (!record) {
            return;
        }

        const isNew = !record.recordId;
        modalTitle.textContent = isNew ? "Nueva cuenta de cobro" : "Editar cuenta de cobro";
        modalSubtitle.textContent = isNew
            ? `El registro se filtrara por la fecha de emision.`
            : `Edita el formulario completo para ${record.receptor || "la cuenta de cobro seleccionada"}.`;
        modalMeta.textContent = isNew
            ? `Fecha de emision: ${record.fechaEmisionDisplay || "sin fecha"}`
            : `Fecha de emision: ${record.fechaEmisionDisplay || "sin fecha"}${record.modifiedOnDisplay ? ` | Actualizada ${record.modifiedOnDisplay}` : ""}`;

        receptorInput.value = record.receptor || "";
        nitInput.value = record.nitOCedula || "";
        fechaEmisionInput.value = record.fechaEmisionValue || "";
        fechaPagoInput.value = record.fechaPagoValue || "";
        retePorcentajeInput.value = formatInputNumber(record.reteFuentePorcentaje);
        valorTotalInput.value = formatInputNumber(record.valorTotal);
        valorPagoInput.value = formatInputNumber(record.valorPago);
        reteValorInput.value = formatInputNumber(record.reteFuenteValor);
        observacionesInput.value = record.observaciones || "";

        validationCard.classList.toggle("is-danger", !record.totalesCuadran);
        validationCard.classList.toggle("is-success", record.totalesCuadran);
        validationText.textContent = record.totalesCuadran ? "La cuenta cuadra correctamente" : "La cuenta no cuadra";

        modalPrintBtn.disabled = !record.recordId || state.busy;
        renderEditorFileState();
        clearModalStatus();

        window.setTimeout(() => {
            receptorInput?.focus();
        }, 0);
    }

    function renderEditorFileState() {
        const record = state.editor.record;
        if (!record) {
            return;
        }

        const pendingFile = state.editor.pendingFile;
        const downloadHref = record.recordId ? buildDownloadUrl(record.recordId) : "#";
        const hasPersistedAttachment = Boolean(record.recordId && record.hasAdjunto);

        attachmentName.textContent = pendingFile
            ? pendingFile.name
            : hasPersistedAttachment
                ? record.adjuntoFileName || "Adjunto cargado"
                : "Sin adjunto cargado";

        attachmentHint.textContent = pendingFile
            ? "El archivo se subira junto con el guardado del formulario."
            : hasPersistedAttachment
                ? "Ya existe un soporte guardado para esta cuenta."
                : "Selecciona un soporte en PDF, imagen o Word.";

        attachmentDownloadLink.href = hasPersistedAttachment ? downloadHref : "#";
        attachmentDownloadLink.classList.toggle("is-disabled", !hasPersistedAttachment);
    }

    function syncEditorFromInputs() {
        const record = state.editor.record;
        if (!record) {
            return;
        }

        record.receptor = receptorInput.value || "";
        record.nitOCedula = nitInput.value || "";
        record.fechaEmisionValue = fechaEmisionInput.value || "";
        record.fechaEmisionDisplay = formatDateDisplay(record.fechaEmisionValue);
        record.fechaPagoValue = fechaPagoInput.value || "";
        record.fechaPagoDisplay = formatDateDisplay(record.fechaPagoValue);
        record.reteFuentePorcentaje = parseDecimal(retePorcentajeInput.value);
        record.valorTotal = parseDecimal(valorTotalInput.value);
        record.valorPago = parseDecimal(valorPagoInput.value);
        record.observaciones = observacionesInput.value || "";

        recomputeRow(record);

        reteValorInput.value = formatInputNumber(record.reteFuenteValor);
        validationCard.classList.toggle("is-danger", !record.totalesCuadran);
        validationCard.classList.toggle("is-success", record.totalesCuadran);
        validationText.textContent = record.totalesCuadran ? "La cuenta cuadra correctamente" : "La cuenta no cuadra";
    }

    async function saveEditor() {
        const record = state.editor.record;
        if (!record) {
            return;
        }

        let savedRecordId = record.recordId || "";
        let savedRecord = null;

        const validationMessage = validateRow(record);
        if (validationMessage) {
            showModalStatus("error", validationMessage);
            showResultDialog("error", "No se pudo guardar", validationMessage);
            return;
        }

        try {
            setBusy(true);
            showModalStatus("info", `Guardando ${record.receptor || "cuenta de cobro"}...`);

            const response = await fetch(saveUrl, {
                method: "POST",
                headers: {
                    "Content-Type": "application/json"
                },
                body: JSON.stringify({
                    recordId: record.recordId,
                    year: state.year,
                    month: state.month,
                    receptor: record.receptor,
                    nitOCedula: record.nitOCedula,
                    observaciones: record.observaciones,
                    fechaEmisionValue: record.fechaEmisionValue,
                    fechaPagoValue: record.fechaPagoValue,
                    valorTotal: record.valorTotal,
                    reteFuentePorcentaje: record.reteFuentePorcentaje,
                    valorPago: record.valorPago
                })
            });

            const payload = await readPayload(response);
            if (!response.ok) {
                throw createResponseError(payload);
            }

            savedRecord = hydrateRow(payload.record);
            savedRecordId = savedRecord.recordId || savedRecordId;
            state.editor.record = cloneRow(savedRecord);
            if (state.editor.pendingFile) {
                showModalStatus("info", "Guardado correcto. Subiendo adjunto...");
                savedRecord = await uploadPendingAttachment(savedRecord.recordId);
                state.editor.record = cloneRow(savedRecord);
            }

            const message = state.editor.pendingFile
                ? "Cuenta de cobro y adjunto guardados correctamente."
                : payload.message || "Cuenta de cobro guardada correctamente.";

            state.editor.pendingFile = null;
            const targetYear = savedRecord?.periodYear || state.year;
            const targetMonth = savedRecord?.periodMonth || state.month;
            closeEditor({ force: true });
            await loadBoard(targetYear, targetMonth, { silent: true });
            renderStatus("success", message);
            showResultDialog("success", "Guardado exitosamente", message);

            if (savedRecord.recordId) {
                const refreshed = findRowByRecordId(savedRecord.recordId);
                if (refreshed) {
                    savedRecord = refreshed;
                }
            }
        } catch (error) {
            if (savedRecordId) {
                const refreshed = findRowByRecordId(savedRecordId);
                if (savedRecord) {
                    state.editor.record = cloneRow(savedRecord);
                } else if (refreshed) {
                    state.editor.record = cloneRow(refreshed);
                }

                await loadBoard(state.year, state.month, { silent: true });
                renderEditor();
            }

            const details = buildErrorBannerMessage(error);
            showModalStatus("error", details);
            renderStatus("error", details);
            showResultDialog(
                "error",
                "No se pudo guardar",
                buildErrorMessage(error),
                buildErrorDetail(error));
        } finally {
            setBusy(false);
        }
    }

    async function uploadPendingAttachment(recordId) {
        const pendingFile = state.editor.pendingFile;
        if (!recordId || !pendingFile) {
            return state.editor.record;
        }

        const formData = new FormData();
        formData.append("recordId", recordId);
        formData.append("file", pendingFile);

        const response = await fetch(uploadUrl, {
            method: "POST",
            body: formData
        });

        const payload = await readPayload(response);
        if (!response.ok) {
            throw createResponseError({
                message: "La cuenta se guardó, pero el adjunto no se pudo cargar.",
                detail: payload?.detail || payload?.message || ""
            });
        }

        return hydrateRow(payload.record);
    }

    async function printRecord(recordId, fromModal) {
        if (!recordId) {
            renderStatus("error", "Guarda la cuenta de cobro antes de imprimir.");
            return;
        }

        try {
            setBusy(true);

            const currentRow = findRowByRecordId(recordId);
            if (currentRow && !currentRow.impresa) {
                const response = await fetch(markPrintedUrl, {
                    method: "POST",
                    headers: {
                        "Content-Type": "application/json"
                    },
                    body: JSON.stringify(recordId)
                });

                const payload = await readPayload(response);
                if (!response.ok) {
                    throw createResponseError(payload);
                }
            }

            await loadBoard(state.year, state.month, { silent: true });

            if (fromModal && state.editor.record?.recordId === recordId) {
                const refreshed = findRowByRecordId(recordId);
                if (refreshed) {
                    state.editor.record = cloneRow(refreshed);
                    renderEditor();
                }
            }

            const popup = window.open(`${printUrl}?recordId=${encodeURIComponent(recordId)}&autoprint=1`, "_blank", "noopener");
            if (!popup) {
                renderStatus("warning", "El navegador bloqueo la ventana de impresion. Permite popups e intenta de nuevo.");
                return;
            }

            renderStatus("success", "La cuenta de cobro quedo lista para impresion.");
        } catch (error) {
            const message = buildErrorBannerMessage(error);
            if (fromModal) {
                showModalStatus("error", message);
            }

            renderStatus("error", message);
        } finally {
            setBusy(false);
        }
    }

    function showModalStatus(level, message) {
        if (!modalStatus) {
            return;
        }

        modalStatus.className = `ccb-status ccb-status--${level} is-visible`;
        modalStatus.textContent = message;
    }

    function clearModalStatus() {
        if (!modalStatus) {
            return;
        }

        modalStatus.className = "ccb-status";
        modalStatus.textContent = "";
    }

    function showResultDialog(level, title, message, detail) {
        const safeMessage = message || title || "Operacion completada.";
        const safeDetail = detail || "";

        if (!resultDialog || !resultDialogTitle || !resultDialogMessage || !resultDialogCloseBtn) {
            renderStatus(level, [safeMessage, safeDetail].filter(Boolean).join(" | "));
            return;
        }

        resultDialog.hidden = false;
        resultDialog.className = `ccb-result-dialog ccb-result-dialog--${level} is-visible`;
        resultDialogTitle.textContent = title || "Resultado";
        resultDialogMessage.textContent = safeMessage;
        document.body.classList.add("ccb-result-open");

        if (resultDialogDetail) {
            resultDialogDetail.hidden = !safeDetail;
            resultDialogDetail.textContent = safeDetail;
        }

        window.setTimeout(() => {
            resultDialogCloseBtn.focus();
        }, 0);
    }

    function hideResultDialog() {
        if (!resultDialog) {
            return;
        }

        resultDialog.hidden = true;
        resultDialog.className = "ccb-result-dialog";
        document.body.classList.remove("ccb-result-open");

        if (resultDialogDetail) {
            resultDialogDetail.hidden = true;
            resultDialogDetail.textContent = "";
        }
    }

    function resolveRowFromElement(element) {
        const rowElement = element.closest("tr[data-local-id]");
        if (!(rowElement instanceof HTMLTableRowElement)) {
            return null;
        }

        const localId = rowElement.dataset.localId || "";
        return state.rows.find((item) => item.localId === localId) || null;
    }

    function findRowByRecordId(recordId) {
        return state.rows.find((item) => item.recordId === recordId) || null;
    }

    function createEmptyRow() {
        state.sequence += 1;
        const fechaEmisionValue = buildFallbackEmissionDateValue();
        return recomputeRow({
            localId: `new-${state.sequence}`,
            recordId: "",
            receptor: "",
            nitOCedula: "",
            observaciones: "",
            fechaEmisionValue,
            fechaEmisionDisplay: formatDateDisplay(fechaEmisionValue),
            fechaPagoValue: "",
            fechaPagoDisplay: "",
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
            modifiedOnDisplay: ""
        });
    }

    function hydrateRow(record) {
        return recomputeRow({
            localId: record.recordId || `row-${++state.sequence}`,
            recordId: record.recordId || "",
            receptor: record.receptor || "",
            nitOCedula: record.nitOCedula || "",
            observaciones: record.observaciones || "",
            fechaEmisionValue: record.fechaEmisionValue || "",
            fechaEmisionDisplay: record.fechaEmisionDisplay || formatDateDisplay(record.fechaEmisionValue || ""),
            fechaPagoValue: record.fechaPagoValue || "",
            fechaPagoDisplay: record.fechaPagoDisplay || formatDateDisplay(record.fechaPagoValue || ""),
            valorTotal: Number(record.valorTotal || 0),
            reteFuentePorcentaje: Number(record.reteFuentePorcentaje || 0),
            valorPago: Number(record.valorPago || 0),
            reteFuenteValor: Number(record.reteFuenteValor || 0),
            totalesCuadran: Boolean(record.totalesCuadran),
            impresa: Boolean(record.impresa),
            hasAdjunto: Boolean(record.hasAdjunto),
            adjuntoFileName: record.adjuntoFileName || "",
            periodYear: Number(record.periodYear || 0),
            periodMonth: Number(record.periodMonth || 0),
            periodLabel: record.periodLabel || buildFallbackPeriodLabel(),
            createdOnDisplay: record.createdOnDisplay || "",
            modifiedOnDisplay: record.modifiedOnDisplay || ""
        });
    }

    function cloneRow(row) {
        return {
            ...row
        };
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

        if (!isValidDateValue(row.fechaEmisionValue)) {
            return "Debes diligenciar una fecha de emision valida.";
        }

        if (row.fechaPagoValue && !isValidDateValue(row.fechaPagoValue)) {
            return "La fecha de pago debe ser valida.";
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

    function buildFallbackEmissionDateValue() {
        const selectedYear = parseInteger(yearSelect?.value, state.year);
        const selectedMonth = parseInteger(monthSelect?.value, state.month);
        return `${String(selectedYear).padStart(4, "0")}-${String(selectedMonth).padStart(2, "0")}-01`;
    }

    function resetAttachmentInput() {
        if (attachmentInput) {
            attachmentInput.value = "";
        }
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

        if (modalCloseBtn) {
            modalCloseBtn.disabled = isBusy;
        }

        if (modalCancelBtn) {
            modalCancelBtn.disabled = isBusy;
        }

        if (modalSaveBtn) {
            modalSaveBtn.disabled = isBusy;
        }

        if (modalPrintBtn) {
            modalPrintBtn.disabled = isBusy || !state.editor.record?.recordId;
        }

        [receptorInput, nitInput, fechaEmisionInput, fechaPagoInput, retePorcentajeInput, valorTotalInput, valorPagoInput, observacionesInput, attachmentInput].forEach((element) => {
            if (element) {
                element.disabled = isBusy;
            }
        });

        rowsBody?.querySelectorAll("button, tr").forEach((element) => {
            if (element instanceof HTMLButtonElement) {
                element.disabled = isBusy || (element.dataset.action === "print" && !element.closest("tr[data-local-id]")?.dataset.localId);
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

    function isValidDateValue(value) {
        return /^\d{4}-\d{2}-\d{2}$/.test(String(value || ""));
    }

    function formatDateDisplay(value) {
        const match = /^(\d{4})-(\d{2})-(\d{2})$/.exec(String(value || ""));
        if (!match) {
            return "";
        }

        return `${match[3]}/${match[2]}/${match[1]}`;
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
        return [buildErrorMessage(error), buildErrorDetail(error).replaceAll("\n", " | ")]
            .filter(Boolean)
            .join(" | ");
    }

    function buildErrorMessage(error) {
        return error?.message || "Ocurrio un error inesperado.";
    }

    function buildErrorDetail(error) {
        const parts = [];
        if (error?.detail) {
            parts.push(error.detail);
        }

        if (error?.traceId) {
            parts.push(`TraceId: ${error.traceId}`);
        }

        return parts.filter(Boolean).join("\n");
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
