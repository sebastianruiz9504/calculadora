(function () {
    const app = document.getElementById("cuentasCobroApp");
    if (!app) {
        return;
    }

    const loadUrl = app.dataset.loadUrl || "";
    const saveUrl = app.dataset.saveUrl || "";
    const uploadUrl = app.dataset.uploadUrl || "";
    const downloadUrl = app.dataset.downloadUrl || "";
    const downloadReportUrl = app.dataset.downloadReportUrl || "";
    const markPrintedUrl = app.dataset.markPrintedUrl || "";
    const printUrl = app.dataset.printUrl || "";

    const yearSelect = document.getElementById("ccbYearSelect");
    const monthSelect = document.getElementById("ccbMonthSelect");
    const refreshBtn = document.getElementById("ccbRefreshBtn");
    const addRowBtn = document.getElementById("ccbAddRowBtn");
    const downloadReportBtn = document.getElementById("ccbDownloadReportBtn");
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
    const valorTotalInput = document.getElementById("ccbValorTotalInput");
    const valorPagoInput = document.getElementById("ccbValorPagoInput");
    const totalRetentionsInput = document.getElementById("ccbTotalRetentionsInput");
    const addRetentionBtn = document.getElementById("ccbAddRetentionBtn");
    const retentionsList = document.getElementById("ccbRetentionsList");
    const retentionsEmpty = document.getElementById("ccbRetentionsEmpty");
    const retentionsStorageWarning = document.getElementById("ccbRetentionsStorageWarning");
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

    downloadReportBtn?.addEventListener("click", async () => {
        if (state.busy) {
            return;
        }

        await downloadReport();
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

    [receptorInput, nitInput, fechaEmisionInput, fechaPagoInput, valorTotalInput, valorPagoInput, observacionesInput].forEach((element) => {
        element?.addEventListener("input", syncEditorFromInputs);
    });

    addRetentionBtn?.addEventListener("click", () => {
        const record = state.editor.record;
        if (!record || state.busy) {
            return;
        }

        record.retentions.push(createEmptyRetention("ReteFuente", record.valorTotal));
        recomputeRow(record);
        renderRetentionsEditor();
        renderValidationState(record);
    });

    retentionsList?.addEventListener("input", handleRetentionEditorInput);
    retentionsList?.addEventListener("change", handleRetentionEditorInput);
    retentionsList?.addEventListener("click", (event) => {
        const target = event.target;
        if (!(target instanceof HTMLElement) || state.busy) {
            return;
        }

        const removeButton = target.closest("[data-retention-remove]");
        if (!(removeButton instanceof HTMLElement) || !state.editor.record) {
            return;
        }

        const index = parseInteger(removeButton.dataset.retentionRemove, -1);
        if (index < 0 || index >= state.editor.record.retentions.length) {
            return;
        }

        state.editor.record.retentions.splice(index, 1);
        recomputeRow(state.editor.record);
        renderRetentionsEditor();
        renderValidationState(state.editor.record);
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

        if (retentionsStorageWarning) {
            retentionsStorageWarning.hidden = Boolean(state.board?.retentionJsonAvailable);
        }
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
        const totalReteValor = rows.reduce((total, row) => total + (row.totalRetentionsValue || 0), 0);

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
        valorTotalInput.value = formatInputNumber(record.valorTotal);
        valorPagoInput.value = formatInputNumber(record.valorPago);
        totalRetentionsInput.value = formatInputNumber(record.totalRetentionsValue);
        observacionesInput.value = record.observaciones || "";

        renderRetentionsEditor();
        renderValidationState(record);

        modalPrintBtn.disabled = !record.recordId || state.busy;
        renderEditorFileState();
        clearModalStatus();

        window.setTimeout(() => {
            receptorInput?.focus();
        }, 0);
    }

    function renderRetentionsEditor() {
        const record = state.editor.record;
        if (!record || !retentionsList) {
            return;
        }

        const hasJsonStorage = Boolean(state.board?.retentionJsonAvailable);
        const retentions = Array.isArray(record.retentions) ? record.retentions : [];
        retentionsList.innerHTML = retentions.map((retention, index) => {
            const kind = normalizeRetentionKind(retention.kind);
            const rateUnit = kind === "ReteICA" ? "‰" : "%";
            const legacyOnly = !hasJsonStorage;
            return `
                <article class="ccb-retention-row" data-retention-index="${index}">
                    <div class="ccb-retention-row__heading">
                        <strong>${escapeHtml(retention.label || resolveRetentionLabel(kind))}</strong>
                        <button type="button" class="btn btn-outline-danger btn-sm" data-retention-remove="${index}" ${state.busy ? "disabled" : ""}>Eliminar</button>
                    </div>
                    <div class="ccb-retention-row__grid">
                        <label class="ccb-form__field">
                            <span class="ccb-form__label">Tipo</span>
                            <select class="form-select" data-retention-field="kind" data-retention-index="${index}" ${state.busy || legacyOnly ? "disabled" : ""}>
                                ${buildRetentionKindOptions(kind)}
                            </select>
                        </label>
                        <label class="ccb-form__field">
                            <span class="ccb-form__label">Etiqueta</span>
                            <input class="form-control" type="text" maxlength="120" value="${escapeHtml(retention.label || "")}" data-retention-field="label" data-retention-index="${index}" ${state.busy || legacyOnly ? "disabled" : ""} />
                        </label>
                        <label class="ccb-form__field">
                            <span class="ccb-form__label">ID impuesto Siigo</span>
                            <input class="form-control" type="text" maxlength="100" value="${escapeHtml(retention.taxId || "")}" data-retention-field="taxId" data-retention-index="${index}" ${state.busy || legacyOnly ? "disabled" : ""} />
                        </label>
                        <label class="ccb-form__field">
                            <span class="ccb-form__label">Cuenta contable</span>
                            <input class="form-control" type="text" maxlength="50" value="${escapeHtml(retention.accountCode || "")}" data-retention-field="accountCode" data-retention-index="${index}" ${state.busy || legacyOnly ? "disabled" : ""} />
                        </label>
                        <label class="ccb-form__field">
                            <span class="ccb-form__label">Base</span>
                            <input class="form-control" type="number" min="0" step="0.01" value="${escapeHtml(formatInputNumber(retention.baseValue))}" data-retention-field="baseValue" data-retention-index="${index}" ${state.busy || legacyOnly ? "disabled" : ""} />
                        </label>
                        <label class="ccb-form__field">
                            <span class="ccb-form__label">Tasa (${rateUnit})</span>
                            <input class="form-control" type="number" min="0" step="0.0001" value="${escapeHtml(formatRateNumber(retention.rate))}" data-retention-field="rate" data-retention-index="${index}" ${state.busy ? "disabled" : ""} />
                        </label>
                        <label class="ccb-form__field">
                            <span class="ccb-form__label">Valor retenido</span>
                            <input class="form-control" type="number" min="0" step="0.01" value="${escapeHtml(formatInputNumber(retention.value))}" data-retention-field="value" data-retention-index="${index}" ${state.busy ? "disabled" : ""} />
                        </label>
                    </div>
                </article>
            `;
        }).join("");

        if (retentionsEmpty) {
            retentionsEmpty.hidden = retentions.length > 0;
        }

        if (retentionsStorageWarning) {
            retentionsStorageWarning.hidden = hasJsonStorage;
        }

        if (addRetentionBtn) {
            addRetentionBtn.disabled = state.busy || (!hasJsonStorage && retentions.length >= 1);
        }

        if (totalRetentionsInput) {
            totalRetentionsInput.value = formatInputNumber(record.totalRetentionsValue);
        }
    }

    function handleRetentionEditorInput(event) {
        const target = event.target;
        const record = state.editor.record;
        if (!(target instanceof HTMLInputElement || target instanceof HTMLSelectElement) || !record) {
            return;
        }

        const index = parseInteger(target.dataset.retentionIndex, -1);
        const field = target.dataset.retentionField || "";
        const retention = record.retentions[index];
        if (!retention || !field) {
            return;
        }

        if (field === "kind") {
            const oldLabel = retention.label || "";
            retention.kind = normalizeRetentionKind(target.value);
            if (!oldLabel || isDefaultRetentionLabel(oldLabel)) {
                retention.label = resolveRetentionLabel(retention.kind);
            }
            retention.value = calculateRetentionValue(retention);
        } else if (field === "label" || field === "taxId" || field === "accountCode") {
            retention[field] = target.value || "";
        } else if (field === "baseValue" || field === "rate") {
            retention[field] = parseDecimal(target.value);
            retention.value = calculateRetentionValue(retention);
        } else if (field === "value") {
            retention.value = parseDecimal(target.value);
        }

        recomputeRow(record);
        if (field === "kind") {
            renderRetentionsEditor();
        } else if (field === "baseValue" || field === "rate") {
            const valueInput = retentionsList.querySelector(`[data-retention-index="${index}"][data-retention-field="value"]`);
            if (valueInput instanceof HTMLInputElement) {
                valueInput.value = formatInputNumber(retention.value);
            }
        }

        if (totalRetentionsInput) {
            totalRetentionsInput.value = formatInputNumber(record.totalRetentionsValue);
        }
        renderValidationState(record);
    }

    function renderValidationState(record) {
        validationCard.classList.toggle("is-danger", !record.totalesCuadran);
        validationCard.classList.toggle("is-success", record.totalesCuadran);
        validationText.textContent = record.totalesCuadran
            ? "La cuenta cuadra correctamente"
            : "La cuenta no cuadra";
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

        const previousValorTotal = record.valorTotal;
        record.receptor = receptorInput.value || "";
        record.nitOCedula = nitInput.value || "";
        record.fechaEmisionValue = fechaEmisionInput.value || "";
        record.fechaEmisionDisplay = formatDateDisplay(record.fechaEmisionValue);
        record.fechaPagoValue = fechaPagoInput.value || "";
        record.fechaPagoDisplay = formatDateDisplay(record.fechaPagoValue);
        record.valorTotal = parseDecimal(valorTotalInput.value);
        record.valorPago = parseDecimal(valorPagoInput.value);
        record.observaciones = observacionesInput.value || "";

        if (!state.board?.retentionJsonAvailable
            && Math.abs(previousValorTotal - record.valorTotal) > 0.001
            && record.retentions.length === 1
            && record.retentions[0].kind === "ReteFuente") {
            record.retentions[0].baseValue = record.valorTotal;
            record.retentions[0].value = calculateRetentionValue(record.retentions[0]);
        }

        recomputeRow(record);

        totalRetentionsInput.value = formatInputNumber(record.totalRetentionsValue);
        renderValidationState(record);
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
                    reteFuenteValor: record.reteFuenteValor,
                    valorPago: record.valorPago,
                    retentions: record.retentions.map(toRetentionPayload)
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

    async function downloadReport() {
        if (!downloadReportUrl) {
            return;
        }

        if (!state.rows.length) {
            renderStatus("warning", "No hay filas en pantalla para exportar.");
            return;
        }

        try {
            setBusy(true);
            renderStatus("info", "Preparando reporte de cuentas de cobro...");

            const response = await fetch(downloadReportUrl, {
                method: "POST",
                headers: {
                    "Content-Type": "application/json",
                    Accept: "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
                },
                body: JSON.stringify({
                    year: state.year,
                    month: state.month,
                    periodLabel: state.board?.selectedPeriodLabel || buildFallbackPeriodLabel(),
                    rows: state.rows.map(toReportRow)
                })
            });

            if (!response.ok) {
                throw createResponseError(await readPayload(response));
            }

            const blob = await response.blob();
            const href = URL.createObjectURL(blob);
            const link = document.createElement("a");
            link.href = href;
            link.download = resolveDownloadFileName(response, "cuentas-cobro.xlsx");
            document.body.appendChild(link);
            link.click();
            link.remove();
            URL.revokeObjectURL(href);
            renderStatus("success", "Reporte descargado correctamente.");
        } catch (error) {
            renderStatus("error", buildErrorBannerMessage(error));
        } finally {
            setBusy(false);
        }
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
            retentions: [],
            totalRetentionsValue: 0,
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
            retentions: hydrateRetentions(record),
            totalRetentionsValue: Number(record.totalRetentionsValue || 0),
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

    function hydrateRetentions(record) {
        const source = Array.isArray(record?.retentions) ? record.retentions : [];
        if (source.length > 0) {
            return source.map(normalizeRetention);
        }

        const legacyRate = Number(record?.reteFuentePorcentaje || 0);
        let legacyValue = Number(record?.reteFuenteValor || 0);
        if (legacyValue === 0 && legacyRate > 0) {
            legacyValue = roundCurrency(Number(record?.valorTotal || 0) * legacyRate / 100);
        }

        if (legacyRate <= 0 && legacyValue <= 0) {
            return [];
        }

        return [normalizeRetention({
            kind: "ReteFuente",
            label: resolveRetentionLabel("ReteFuente"),
            taxId: "",
            accountCode: "",
            baseValue: Number(record?.valorTotal || 0),
            rate: legacyRate,
            value: legacyValue
        })];
    }

    function createEmptyRetention(kind, baseValue) {
        const normalizedKind = normalizeRetentionKind(kind);
        return normalizeRetention({
            kind: normalizedKind,
            label: resolveRetentionLabel(normalizedKind),
            taxId: "",
            accountCode: "",
            baseValue: Number(baseValue || 0),
            rate: 0,
            value: 0
        });
    }

    function normalizeRetention(retention) {
        const kind = normalizeRetentionKind(retention?.kind);
        const normalized = {
            kind,
            label: String(retention?.label || resolveRetentionLabel(kind)).trim(),
            taxId: String(retention?.taxId || "").trim(),
            accountCode: String(retention?.accountCode || "").trim(),
            baseValue: roundCurrency(retention?.baseValue || 0),
            rate: roundRate(retention?.rate || 0),
            value: roundCurrency(retention?.value || 0)
        };

        if (normalized.value === 0 && normalized.baseValue > 0 && normalized.rate > 0) {
            normalized.value = calculateRetentionValue(normalized);
        }

        return normalized;
    }

    function normalizeRetentionKind(value) {
        const normalized = String(value || "")
            .trim()
            .replaceAll("-", "")
            .replaceAll("_", "")
            .replaceAll(" ", "")
            .toLowerCase();

        if (["retefuente", "retefte", "retencionfuente"].includes(normalized)) {
            return "ReteFuente";
        }
        if (["reteica", "retencionica"].includes(normalized)) {
            return "ReteICA";
        }
        if (["reteiva", "rteiva", "ivaretenido"].includes(normalized)) {
            return "RteIVA";
        }
        return "Otra";
    }

    function resolveRetentionLabel(kind) {
        if (kind === "ReteFuente") {
            return "Retencion en la fuente";
        }
        if (kind === "ReteICA") {
            return "Retencion ICA";
        }
        if (kind === "RteIVA") {
            return "IVA retenido";
        }
        return "Otra retencion";
    }

    function isDefaultRetentionLabel(value) {
        const normalized = String(value || "").trim().toLowerCase();
        return [
            "retencion en la fuente",
            "retencion ica",
            "iva retenido",
            "otra retencion"
        ].includes(normalized);
    }

    function calculateRetentionValue(retention) {
        const divisor = normalizeRetentionKind(retention?.kind) === "ReteICA" ? 1000 : 100;
        return roundCurrency(Number(retention?.baseValue || 0) * Number(retention?.rate || 0) / divisor);
    }

    function buildRetentionKindOptions(selectedKind) {
        return [
            ["ReteFuente", "ReteFuente"],
            ["ReteICA", "ReteICA"],
            ["RteIVA", "RteIVA"],
            ["Otra", "Otra"]
        ].map(([value, label]) => `
            <option value="${value}" ${value === selectedKind ? "selected" : ""}>${label}</option>
        `).join("");
    }

    function toRetentionPayload(retention) {
        const normalized = normalizeRetention(retention);
        return {
            kind: normalized.kind,
            label: normalized.label,
            taxId: normalized.taxId,
            accountCode: normalized.accountCode,
            baseValue: normalized.baseValue,
            rate: normalized.rate,
            value: normalized.value
        };
    }

    function cloneRow(row) {
        return {
            ...row,
            retentions: Array.isArray(row.retentions)
                ? row.retentions.map((retention) => ({ ...retention }))
                : []
        };
    }

    function recomputeRow(row) {
        row.valorTotal = roundCurrency(row.valorTotal || 0);
        row.valorPago = roundCurrency(row.valorPago || 0);
        row.retentions = Array.isArray(row.retentions)
            ? row.retentions.map(normalizeRetention)
            : [];

        if (!state.board?.retentionJsonAvailable
            && row.retentions.length === 1
            && row.retentions[0].kind === "ReteFuente") {
            row.retentions[0].baseValue = row.valorTotal;
        }

        row.totalRetentionsValue = roundCurrency(row.retentions.reduce(
            (total, retention) => total + retention.value,
            0));
        const reteFuente = row.retentions.find((retention) => retention.kind === "ReteFuente");
        row.reteFuentePorcentaje = reteFuente ? reteFuente.rate : 0;
        row.reteFuenteValor = reteFuente ? reteFuente.value : 0;
        row.totalesCuadran = Math.abs(row.valorTotal - (row.valorPago + row.totalRetentionsValue)) <= 0.01;
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

        for (let index = 0; index < row.retentions.length; index += 1) {
            const retention = row.retentions[index];
            const displayIndex = index + 1;
            if (!["ReteFuente", "ReteICA", "RteIVA", "Otra"].includes(retention.kind)) {
                return `El tipo de la retencion ${displayIndex} no es valido.`;
            }

            if (retention.baseValue <= 0) {
                return `La base de la retencion ${displayIndex} debe ser mayor a cero.`;
            }

            const maximumRate = retention.kind === "ReteICA" ? 1000 : 100;
            if (retention.rate < 0 || retention.rate > maximumRate) {
                return `La tasa de la retencion ${displayIndex} no es valida.`;
            }

            if (retention.value <= 0) {
                return `El valor de la retencion ${displayIndex} debe ser mayor a cero.`;
            }
        }

        if (!state.board?.retentionJsonAvailable && row.retentions.length > 0) {
            const legacyRetention = row.retentions.length === 1 ? row.retentions[0] : null;
            const canUseLegacyFields = legacyRetention
                && legacyRetention.kind === "ReteFuente"
                && isDefaultRetentionLabel(legacyRetention.label)
                && !legacyRetention.taxId
                && !legacyRetention.accountCode
                && Math.abs(legacyRetention.baseValue - row.valorTotal) <= 0.01;

            if (!canUseLegacyFields) {
                return "Dataverse requiere el campo cr07a_retencionesjson para guardar retenciones multiples o detalladas.";
            }
        }

        if (!row.totalesCuadran) {
            return "El valor total debe ser igual a valor pago + suma de retenciones.";
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

        if (downloadReportBtn) {
            downloadReportBtn.disabled = isBusy || state.rows.length === 0;
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

        if (addRetentionBtn) {
            addRetentionBtn.disabled = isBusy
                || (!state.board?.retentionJsonAvailable && (state.editor.record?.retentions?.length || 0) >= 1);
        }

        [receptorInput, nitInput, fechaEmisionInput, fechaPagoInput, valorTotalInput, valorPagoInput, observacionesInput, attachmentInput].forEach((element) => {
            if (element) {
                element.disabled = isBusy;
            }
        });

        if (isBusy) {
            retentionsList?.querySelectorAll("input, select, button").forEach((element) => {
                if (element instanceof HTMLInputElement
                    || element instanceof HTMLSelectElement
                    || element instanceof HTMLButtonElement) {
                    element.disabled = true;
                }
            });
        } else if (state.editor.isOpen) {
            renderRetentionsEditor();
        }

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

    function roundRate(value) {
        return Math.round((Number(value || 0) + Number.EPSILON) * 10000) / 10000;
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

    function toReportRow(row) {
        return {
            recordId: row.recordId || "",
            receptor: row.receptor || "",
            nitOCedula: row.nitOCedula || "",
            observaciones: row.observaciones || "",
            valorTotal: row.valorTotal || 0,
            reteFuentePorcentaje: row.reteFuentePorcentaje || 0,
            valorPago: row.valorPago || 0,
            reteFuenteValor: row.reteFuenteValor || 0,
            retentions: Array.isArray(row.retentions) ? row.retentions.map(toRetentionPayload) : [],
            totalRetentionsValue: row.totalRetentionsValue || 0,
            totalesCuadran: Boolean(row.totalesCuadran),
            impresa: Boolean(row.impresa),
            hasAdjunto: Boolean(row.hasAdjunto),
            adjuntoFileName: row.adjuntoFileName || "",
            periodYear: row.periodYear || state.year,
            periodMonth: row.periodMonth || state.month,
            periodLabel: row.periodLabel || "",
            fechaEmisionValue: row.fechaEmisionValue || "",
            fechaEmisionDisplay: row.fechaEmisionDisplay || "",
            fechaPagoValue: row.fechaPagoValue || "",
            fechaPagoDisplay: row.fechaPagoDisplay || "",
            createdOnValue: row.createdOnValue || "",
            createdOnDisplay: row.createdOnDisplay || "",
            modifiedOnDisplay: row.modifiedOnDisplay || ""
        };
    }

    function formatInputNumber(value) {
        return roundCurrency(value).toFixed(2);
    }

    function formatRateNumber(value) {
        return roundRate(value).toFixed(4);
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

    function resolveDownloadFileName(response, fallback) {
        const disposition = response.headers.get("content-disposition") || "";
        const utf8Match = disposition.match(/filename\*=UTF-8''([^;]+)/i);
        if (utf8Match) {
            return decodeURIComponent(utf8Match[1].trim().replaceAll("\"", ""));
        }

        const plainMatch = disposition.match(/filename="?([^";]+)"?/i);
        return plainMatch ? plainMatch[1].trim() : fallback;
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
