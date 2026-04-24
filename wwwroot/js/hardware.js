(function () {
    const root = document.getElementById("hardwareApp");
    if (!root) {
        return;
    }

    const config = {
        previewUrl: root.dataset.previewUrl || "",
        provisionUrl: root.dataset.provisionUrl || "",
        boardUrl: root.dataset.boardUrl || "",
        saveUrl: root.dataset.saveUrl || "",
        uploadUrl: root.dataset.uploadUrl || "",
        downloadUrl: root.dataset.downloadUrl || "",
        invoiceSearchUrl: root.dataset.invoiceSearchUrl || ""
    };

    const elements = {
        status: root.querySelector("[data-hw-status]"),
        fileInput: document.getElementById("hardwareCsvFile"),
        analyzeBtn: document.getElementById("hardwareAnalyzeBtn"),
        provisionBtn: document.getElementById("hardwareProvisionBtn"),
        importStatus: document.getElementById("hardwareStatus"),
        summaryWrap: document.getElementById("hardwareSummary"),
        summaryList: document.getElementById("hardwareSummaryList"),
        columnsWrap: document.getElementById("hardwareColumnsWrap"),
        columnsBody: document.getElementById("hardwareColumnsBody"),
        systemColumnsNote: document.getElementById("hardwareSystemColumnsNote"),
        provisionWrap: document.getElementById("hardwareProvisionWrap"),
        provisionList: document.getElementById("hardwareProvisionList"),
        boardStatus: root.querySelector("[data-hw-board-status]"),
        stateFilter: root.querySelector("[data-hw-state-filter]"),
        filterLabel: root.querySelector("[data-hw-filter-label]"),
        refreshBtn: root.querySelector("[data-hw-refresh]"),
        warnings: root.querySelector("[data-hw-warnings]"),
        stateSummary: root.querySelector("[data-hw-state-summary]"),
        rows: root.querySelector("[data-hw-rows]"),
        totalRecords: root.querySelector("[data-hw-total-records]"),
        totalSales: root.querySelector("[data-hw-total-sales]"),
        pendingActions: root.querySelector("[data-hw-pending-actions]"),
        closedCount: root.querySelector("[data-hw-closed-count]"),
        modal: root.querySelector("[data-hw-modal]"),
        modalTitle: root.querySelector("[data-hw-modal-title]"),
        modalSubtitle: root.querySelector("[data-hw-modal-subtitle]"),
        modalStatus: root.querySelector("[data-hw-modal-status]"),
        modalMeta: root.querySelector("[data-hw-modal-meta]"),
        form: root.querySelector("[data-hw-form]"),
        saveStageBtn: root.querySelector("[data-hw-save-stage]"),
        recordId: root.querySelector("[data-hw-record-id]"),
        actionKey: root.querySelector("[data-hw-action-key]"),
        recordName: root.querySelector("[data-hw-record-name]"),
        recordMeta: root.querySelector("[data-hw-record-meta]"),
        recordState: root.querySelector("[data-hw-record-state]"),
        closeModalButtons: Array.from(root.querySelectorAll("[data-hw-close-modal]")),
        stagePanels: Array.from(root.querySelectorAll("[data-hw-stage-panel]")),
        invoiceOptions: root.querySelector("[data-hw-invoice-options]"),
        fields: {
            odcDate: root.querySelector('[data-hw-field="odcDate"]'),
            supplierUnitCost: root.querySelector('[data-hw-field="supplierUnitCost"]'),
            provider: root.querySelector('[data-hw-field="provider"]'),
            supplierPaymentDate: root.querySelector('[data-hw-field="supplierPaymentDate"]'),
            deliveryRecordDate: root.querySelector('[data-hw-field="deliveryRecordDate"]'),
            invoiceNumber: root.querySelector('[data-hw-field="invoiceNumber"]')
        },
        fileInputs: Array.from(root.querySelectorAll("[data-hw-file-input]")),
        fileNames: Array.from(root.querySelectorAll("[data-hw-file-name]")),
        fileHints: Array.from(root.querySelectorAll("[data-hw-file-hint]")),
        downloadLinks: Array.from(root.querySelectorAll("[data-hw-download-link]"))
    };

    if (!elements.fileInput
        || !elements.analyzeBtn
        || !elements.provisionBtn
        || !elements.importStatus
        || !elements.summaryWrap
        || !elements.summaryList
        || !elements.columnsWrap
        || !elements.columnsBody
        || !elements.systemColumnsNote
        || !elements.provisionWrap
        || !elements.provisionList
        || !elements.boardStatus
        || !elements.stateFilter
        || !elements.filterLabel
        || !elements.refreshBtn
        || !elements.warnings
        || !elements.stateSummary
        || !elements.rows
        || !elements.totalRecords
        || !elements.totalSales
        || !elements.pendingActions
        || !elements.closedCount
        || !elements.modal
        || !elements.modalTitle
        || !elements.modalSubtitle
        || !elements.modalStatus
        || !elements.modalMeta
        || !elements.form
        || !elements.saveStageBtn
        || !elements.recordId
        || !elements.actionKey
        || !elements.recordName
        || !elements.recordMeta
        || !elements.recordState
        || !elements.invoiceOptions) {
        return;
    }

    const state = {
        preview: null,
        board: null,
        rows: [],
        busy: false,
        boardLoading: false,
        saving: false,
        modalRecord: null,
        pendingFiles: {},
        invoiceSuggestions: [],
        invoiceLookupTimer: 0,
        invoiceLookupSequence: 0
    };

    [elements.status, elements.importStatus, elements.boardStatus, elements.modalStatus]
        .filter(Boolean)
        .forEach(element => {
            element.dataset.baseClass = element.className;
        });

    elements.fileHints.forEach(item => {
        item.dataset.defaultHint = item.textContent || "";
    });

    const stageConfig = {
        "register-documentation": {
            title: "Registrar documentación",
            subtitle: "Completa la documentación inicial y deja la línea lista para pago a proveedor.",
            buttonLabel: "Registrar documentación",
            meta: "Próximo estado: Ok para pago a proveedor",
            requiredFiles: ["cr07a_ordendecompra", "cr07a_adjuntarproforma"]
        },
        "register-supplier-payment": {
            title: "Registrar pago a proveedor",
            subtitle: "Adjunta el soporte de pago y registra la fecha correspondiente.",
            buttonLabel: "Registrar pago a proveedor",
            meta: "Próximo estado: Pagada a proveedor",
            requiredFiles: ["cr07a_pagoaproveedor"]
        },
        "register-received": {
            title: "Registrar recibido",
            subtitle: "Confirma el recibido por comercial para mover la línea a tránsito.",
            buttonLabel: "Aprobar recibido por comercial",
            meta: "Próximo estado: En tránsito a oficina o cliente",
            requiredFiles: []
        },
        "register-client-received": {
            title: "Registrar recibido cliente",
            subtitle: "Carga el acta de entrega y registra la fecha para habilitar la facturación.",
            buttonLabel: "Registrar recibido cliente",
            meta: "Próximo estado: Entregado en espera de facturación",
            requiredFiles: ["cr07a_actadeentrega"]
        },
        "register-invoice": {
            title: "Registrar factura",
            subtitle: "Selecciona una factura exacta desde la tabla Facturación.",
            buttonLabel: "Registrar factura",
            meta: "Próximo estado: Facturado en espera de pago",
            requiredFiles: []
        },
        "register-client-payment": {
            title: "Registrar pago cliente",
            subtitle: "Se consultará la factura en Facturación para cerrar automáticamente si ya tiene pago.",
            buttonLabel: "Validar pago cliente",
            meta: "Próximo estado: Cerrado si la factura ya tiene pago",
            requiredFiles: []
        }
    };

    elements.analyzeBtn.addEventListener("click", previewCsv);
    elements.provisionBtn.addEventListener("click", provisionCsv);
    elements.refreshBtn.addEventListener("click", () => loadBoard());
    elements.fileInput.addEventListener("change", handleCsvFileChange);
    elements.stateFilter.addEventListener("change", () => {
        elements.filterLabel.textContent = elements.stateFilter.options[elements.stateFilter.selectedIndex]?.text || "Todos los estados";
        loadBoard();
    });

    elements.rows.addEventListener("click", event => {
        const target = event.target;
        if (!(target instanceof HTMLElement)) {
            return;
        }

        const actionButton = target.closest("[data-hw-action-record]");
        if (!(actionButton instanceof HTMLButtonElement)) {
            return;
        }

        openModal(actionButton.dataset.hwActionRecord || "");
    });

    elements.form.addEventListener("submit", async event => {
        event.preventDefault();
        await saveStage();
    });

    elements.closeModalButtons.forEach(button => {
        button.addEventListener("click", closeModal);
    });

    elements.modal.addEventListener("click", event => {
        const target = event.target;
        if (target instanceof HTMLElement && target.hasAttribute("data-hw-close-modal")) {
            closeModal();
        }
    });

    document.addEventListener("keydown", event => {
        if (event.key === "Escape" && !elements.modal.hidden) {
            closeModal();
        }
    });

    elements.fileInputs.forEach(input => {
        input.addEventListener("change", () => {
            const fieldName = input.dataset.hwFileInput || "";
            state.pendingFiles[fieldName] = input.files && input.files.length > 0 ? input.files[0] : null;
            renderFileCards();
        });
    });

    elements.fields.invoiceNumber?.addEventListener("input", handleInvoiceLookupInput);
    elements.fields.invoiceNumber?.addEventListener("change", syncInvoiceSelection);
    elements.fields.invoiceNumber?.addEventListener("blur", syncInvoiceSelection);

    loadBoard();

    function handleCsvFileChange() {
        state.preview = null;
        elements.provisionBtn.disabled = true;
        hidePreview();
        hideProvisionResult();
        clearStatus(elements.importStatus);
    }

    async function previewCsv() {
        const file = elements.fileInput.files && elements.fileInput.files.length > 0
            ? elements.fileInput.files[0]
            : null;
        if (!file) {
            setStatus(elements.importStatus, "warning", "Selecciona un archivo CSV antes de analizar.");
            return;
        }

        try {
            setBusy(true);
            hideProvisionResult();
            setStatus(elements.importStatus, "info", "Analizando estructura del CSV...");
            const formData = new FormData();
            formData.append("file", file);
            const result = await fetchJson(config.previewUrl, {
                method: "POST",
                body: formData
            });

            state.preview = result;
            renderPreview(result);
            setStatus(elements.importStatus, "success", result?.message || "Vista previa lista.");
        } catch (error) {
            state.preview = null;
            hidePreview();
            hideProvisionResult();
            setStatus(elements.importStatus, "error", getErrorMessage(error));
        } finally {
            setBusy(false);
        }
    }

    async function provisionCsv() {
        const file = elements.fileInput.files && elements.fileInput.files.length > 0
            ? elements.fileInput.files[0]
            : null;
        if (!file) {
            setStatus(elements.importStatus, "warning", "Selecciona un archivo CSV antes de importar.");
            return;
        }

        if (!state.preview) {
            setStatus(elements.importStatus, "warning", "Analiza primero el archivo para confirmar el esquema.");
            return;
        }

        try {
            setBusy(true);
            setStatus(elements.importStatus, "info", "Creando tabla y columnas de Hardware en Dataverse...");

            const formData = new FormData();
            formData.append("file", file);
            const result = await fetchJson(config.provisionUrl, {
                method: "POST",
                body: formData
            });

            renderProvisionResult(result);
            setStatus(elements.importStatus, "success", result?.message || "Carga completada.");
            await loadBoard();
        } catch (error) {
            hideProvisionResult();
            setStatus(elements.importStatus, "error", getErrorMessage(error));
        } finally {
            setBusy(false);
        }
    }

    async function loadBoard() {
        if (state.boardLoading || !config.boardUrl) {
            return;
        }

        try {
            state.boardLoading = true;
            elements.refreshBtn.disabled = true;
            setStatus(elements.boardStatus, "info", "Cargando tabla de Hardware...");

            const result = await fetchJson(buildBoardUrl(), { method: "GET" });
            state.board = result;
            state.rows = Array.isArray(result?.rows) ? result.rows : [];
            renderBoard(result);
        } catch (error) {
            elements.rows.innerHTML = `
                <tr>
                    <td colspan="7" class="hardware-table__empty">${escapeHtml(getErrorMessage(error))}</td>
                </tr>
            `;
            setStatus(elements.boardStatus, "error", getErrorMessage(error));
        } finally {
            state.boardLoading = false;
            elements.refreshBtn.disabled = false;
        }
    }

    function renderPreview(result) {
        const columns = Array.isArray(result?.columns) ? result.columns : [];
        const systemColumns = Array.isArray(result?.systemColumns) ? result.systemColumns : [];

        elements.summaryList.innerHTML = [
            `<li><strong>Archivo:</strong> ${escapeHtml(result?.fileName || "")}</li>`,
            `<li><strong>Tabla objetivo:</strong> <code>${escapeHtml(result?.tableLogicalName || "")}</code></li>`,
            `<li><strong>Separador detectado:</strong> ${escapeHtml(result?.detectedDelimiterLabel || "-")}</li>`,
            `<li><strong>Filas detectadas:</strong> ${formatNumber(result?.totalRows || 0)}</li>`,
            `<li><strong>Columnas del CSV:</strong> ${formatNumber(result?.totalColumns || columns.length)}</li>`
        ].join("");
        elements.summaryWrap.hidden = false;

        elements.systemColumnsNote.textContent = systemColumns.length > 0
            ? `Campos técnicos adicionales: ${systemColumns.join(", ")}`
            : "";

        elements.columnsBody.innerHTML = columns.map(column => `
            <tr>
                <td>${escapeHtml(column.sourceHeader || column.displayLabel || "")}</td>
                <td><code>${escapeHtml(column.logicalName || "")}</code></td>
                <td>${escapeHtml(column.dataverseType || "")}</td>
                <td>${escapeHtml(column.exampleValue || "-")}</td>
            </tr>
        `).join("");

        elements.columnsWrap.hidden = columns.length === 0;
        elements.provisionBtn.disabled = columns.length === 0 || Number(result?.totalRows || 0) === 0;
    }

    function hidePreview() {
        elements.summaryWrap.hidden = true;
        elements.columnsWrap.hidden = true;
        elements.summaryList.innerHTML = "";
        elements.columnsBody.innerHTML = "";
        elements.systemColumnsNote.textContent = "";
    }

    function renderProvisionResult(result) {
        elements.provisionList.innerHTML = [
            `<li><strong>Tabla:</strong> <code>${escapeHtml(result?.tableLogicalName || "")}</code></li>`,
            `<li><strong>Entity set:</strong> <code>${escapeHtml(result?.entitySetName || "")}</code></li>`,
            `<li><strong>Tabla creada:</strong> ${result?.tableCreated ? "Sí" : "No"}</li>`,
            `<li><strong>Columnas nuevas:</strong> ${formatNumber(result?.createdColumnsCount || 0)}</li>`,
            `<li><strong>Columnas reutilizadas:</strong> ${formatNumber(result?.existingColumnsCount || 0)}</li>`,
            `<li><strong>Filas importadas:</strong> ${formatNumber(result?.importedCount || 0)}</li>`,
            `<li><strong>Filas duplicadas omitidas:</strong> ${formatNumber(result?.skippedDuplicatesCount || 0)}</li>`
        ].join("");
        elements.provisionWrap.hidden = false;
    }

    function hideProvisionResult() {
        elements.provisionWrap.hidden = true;
        elements.provisionList.innerHTML = "";
    }

    function renderBoard(board) {
        renderFilterOptions(board);
        renderWarnings(board);
        renderStateSummary(board);
        renderSummaryCards(board);
        renderRows(board);

        const syncMessages = Array.isArray(board?.syncMessages) ? board.syncMessages.filter(Boolean) : [];
        const warnings = Array.isArray(board?.warnings) ? board.warnings.filter(Boolean) : [];
        const summaryParts = [];
        if (board?.message) {
            summaryParts.push(String(board.message));
        }
        if (Number(board?.syncedRequestsCount || 0) > 0) {
            summaryParts.push(`Se procesaron ${formatNumber(board.syncedRequestsCount)} solicitud(es) aprobadas.`);
        }
        if (Number(board?.syncedImportedCount || 0) > 0) {
            summaryParts.push(`Se importaron ${formatNumber(board.syncedImportedCount)} línea(s) desde aprobaciones.`);
        }
        if (syncMessages.length > 0) {
            summaryParts.push(syncMessages.join(" | "));
        }

        const kind = warnings.length > 0 || syncMessages.length > 0
            ? "warning"
            : state.rows.length > 0 ? "success" : "info";
        setStatus(elements.boardStatus, kind, summaryParts.join(" ").trim() || "Tabla cargada.");
    }

    function renderFilterOptions(board) {
        const options = Array.isArray(board?.stateOptions) ? board.stateOptions : [];
        const selectedValue = board?.selectedStateValue ? String(board.selectedStateValue) : "";

        elements.stateFilter.innerHTML = `
            <option value="">Todos los estados</option>
            ${options.map(option => `
                <option value="${escapeHtml(option.value)}" ${String(option.value) === selectedValue ? "selected" : ""}>
                    ${escapeHtml(option.label || "")}
                </option>
            `).join("")}
        `;

        elements.filterLabel.textContent =
            elements.stateFilter.options[elements.stateFilter.selectedIndex]?.text || "Todos los estados";
    }

    function renderWarnings(board) {
        const warnings = Array.isArray(board?.warnings) ? board.warnings.filter(Boolean) : [];
        if (!warnings.length) {
            elements.warnings.hidden = true;
            elements.warnings.innerHTML = "";
            return;
        }

        elements.warnings.hidden = false;
        elements.warnings.innerHTML = warnings
            .map(message => `<div class="hardware-warning-list__item">${escapeHtml(message)}</div>`)
            .join("");
    }

    function renderStateSummary(board) {
        const items = Array.isArray(board?.stateSummaries) ? board.stateSummaries : [];
        if (!items.length) {
            elements.stateSummary.innerHTML = "";
            return;
        }

        elements.stateSummary.innerHTML = items.map(item => `
            <article class="hardware-state-card">
                <span class="hardware-state-card__label">${escapeHtml(item.label || "")}</span>
                <strong class="hardware-state-card__value">${formatNumber(item.count || 0)}</strong>
                <span class="hardware-pill ${toneClass(item.tone)}">${escapeHtml(item.label || "")}</span>
            </article>
        `).join("");
    }

    function renderSummaryCards(board) {
        const rows = Array.isArray(board?.rows) ? board.rows : [];
        const totalVisibleSales = rows.reduce((total, row) => total + Number(row?.totalSale || 0), 0);
        const pendingActions = rows.filter(row => Boolean(row?.hasAction)).length;
        const closedCount = rows.filter(row => Number(row?.stateValue || 0) === 645250006).length;

        elements.totalRecords.textContent = formatNumber(rows.length);
        elements.totalSales.textContent = formatCurrency(totalVisibleSales);
        elements.pendingActions.textContent = formatNumber(pendingActions);
        elements.closedCount.textContent = formatNumber(closedCount);
    }

    function renderRows(board) {
        const rows = Array.isArray(board?.rows) ? board.rows : [];
        if (!rows.length) {
            elements.rows.innerHTML = `
                <tr>
                    <td colspan="7" class="hardware-table__empty">No hay registros de Hardware para mostrar.</td>
                </tr>
            `;
            return;
        }

        elements.rows.innerHTML = rows.map(row => `
            <tr class="hardware-table__row ${toneClass(row?.stateTone)}">
                <td>
                    <div class="hardware-table__title">
                        <strong>${escapeHtml(row?.name || "")}</strong>
                        <div class="hardware-table__meta">
                            ${escapeHtml(buildRowMeta(row))}
                        </div>
                        <div class="hardware-table__tags">
                            ${renderRowTags(row)}
                        </div>
                    </div>
                </td>
                <td>
                    <div class="hardware-table__title">
                        <strong>${escapeHtml(row?.clientName || "-")}</strong>
                        <div class="hardware-table__submeta">${escapeHtml(row?.provider || "Sin proveedor registrado")}</div>
                    </div>
                </td>
                <td class="text-end">${formatNumber(row?.quantity || 0)}</td>
                <td class="text-end">${formatCurrency(row?.saleUnit || 0)}</td>
                <td class="text-end">${formatCurrency(row?.totalSale || 0)}</td>
                <td>${renderPill(row?.stateLabel || "Sin estado", row?.stateTone || "")}</td>
                <td>
                    <div class="hardware-action-cell">
                        ${row?.hasAction
                            ? `<button type="button" class="btn btn-sm btn-primary" data-hw-action-record="${escapeHtml(row?.recordId || "")}">${escapeHtml(row?.actionLabel || "Gestionar")}</button>`
                            : `<span class="hardware-table__submeta">Sin botón</span>`}
                    </div>
                </td>
            </tr>
        `).join("");
    }

    function renderRowTags(row) {
        const tags = [];

        if (row?.invoiceNumber) {
            tags.push(`<span class="hardware-tag ${toneClass(row?.stateTone)}">Factura: ${escapeHtml(row.invoiceNumber)}</span>`);
        }
        if (row?.hasOrderPurchase) {
            tags.push('<span class="hardware-tag is-documentation">ODC</span>');
        }
        if (row?.hasProforma) {
            tags.push('<span class="hardware-tag is-documentation">Proforma</span>');
        }
        if (row?.hasSupplierPaymentProof) {
            tags.push('<span class="hardware-tag is-supplier-paid">Pago proveedor</span>');
        }
        if (row?.hasDeliveryRecord) {
            tags.push('<span class="hardware-tag is-in-transit">Acta entrega</span>');
        }

        return tags.join("");
    }

    function buildRowMeta(row) {
        const details = [];
        if (row?.odcDateDisplay) {
            details.push(`ODC: ${row.odcDateDisplay}`);
        }
        if (row?.supplierPaymentDateDisplay) {
            details.push(`Pago proveedor: ${row.supplierPaymentDateDisplay}`);
        }
        if (row?.deliveryRecordDateDisplay) {
            details.push(`Acta: ${row.deliveryRecordDateDisplay}`);
        }
        if (Number(row?.supplierUnitCost || 0) > 0) {
            details.push(`Costo proveedor: ${formatCurrency(row.supplierUnitCost)}`);
        }
        if (row?.modifiedOnDisplay) {
            details.push(`Actualizado: ${row.modifiedOnDisplay}`);
        }

        return details.length ? details.join(" · ") : "Sin gestión registrada";
    }

    function openModal(recordId) {
        const record = state.rows.find(item => item.recordId === recordId);
        if (!record) {
            return;
        }

        state.modalRecord = { ...record };
        state.pendingFiles = {};
        resetFileInputs();
        renderModal();
        elements.modal.hidden = false;
        document.body.classList.add("hardware-modal-open");
    }

    function renderModal() {
        if (!state.modalRecord) {
            return;
        }

        const actionKey = state.modalRecord.actionKey || "";
        const configItem = stageConfig[actionKey] || {
            title: "Gestionar hardware",
            subtitle: "Completa la etapa seleccionada.",
            buttonLabel: "Guardar etapa",
            meta: "Gestión manual",
            requiredFiles: []
        };

        elements.recordId.value = state.modalRecord.recordId || "";
        elements.actionKey.value = actionKey;
        elements.modalTitle.textContent = configItem.title;
        elements.modalSubtitle.textContent = configItem.subtitle;
        elements.modalMeta.textContent = configItem.meta;
        elements.saveStageBtn.textContent = configItem.buttonLabel;
        elements.recordName.textContent = state.modalRecord.name || "Hardware";
        elements.recordMeta.textContent = `${state.modalRecord.clientName || "Sin cliente"} · ${formatNumber(state.modalRecord.quantity || 0)} und · ${formatCurrency(state.modalRecord.totalSale || 0)}`;
        elements.recordState.className = `hardware-pill ${toneClass(state.modalRecord.stateTone)}`;
        elements.recordState.textContent = state.modalRecord.stateLabel || "Sin estado";

        setFieldValue(elements.fields.odcDate, state.modalRecord.odcDateValue || "");
        setFieldValue(elements.fields.supplierUnitCost, formatInputNumber(state.modalRecord.supplierUnitCost || 0));
        setFieldValue(elements.fields.provider, state.modalRecord.provider || "");
        setFieldValue(elements.fields.supplierPaymentDate, state.modalRecord.supplierPaymentDateValue || "");
        setFieldValue(elements.fields.deliveryRecordDate, state.modalRecord.deliveryRecordDateValue || "");
        setFieldValue(elements.fields.invoiceNumber, state.modalRecord.invoiceNumber || "");
        elements.invoiceOptions.innerHTML = "";
        state.invoiceSuggestions = [];

        elements.stagePanels.forEach(panel => {
            panel.hidden = panel.dataset.hwStagePanel !== actionKey;
        });

        renderFileCards();
        clearStatus(elements.modalStatus);
    }

    function closeModal(force = false) {
        if (state.saving && !force) {
            return;
        }

        state.modalRecord = null;
        state.pendingFiles = {};
        resetFileInputs();
        clearStatus(elements.modalStatus);
        elements.modal.hidden = true;
        document.body.classList.remove("hardware-modal-open");
    }

    function renderFileCards() {
        const fileFields = [
            "cr07a_ordendecompra",
            "cr07a_adjuntarproforma",
            "cr07a_pagoaproveedor",
            "cr07a_actadeentrega"
        ];

        fileFields.forEach(fieldName => {
            const fileNameTarget = elements.fileNames.find(item => item.dataset.hwFileName === fieldName);
            const fileHintTarget = elements.fileHints.find(item => item.dataset.hwFileHint === fieldName);
            const downloadLink = elements.downloadLinks.find(item => item.dataset.hwDownloadLink === fieldName);
            const pendingFile = state.pendingFiles[fieldName];
            const existingFile = resolveExistingFileName(fieldName);
            const hasExisting = hasExistingFile(fieldName);

            if (fileNameTarget) {
                fileNameTarget.textContent = pendingFile
                    ? pendingFile.name
                    : existingFile || "Sin archivo";
            }

            if (fileHintTarget) {
                fileHintTarget.textContent = pendingFile
                    ? "El archivo se cargará antes de guardar la etapa."
                    : hasExisting
                        ? "Ya hay un archivo registrado para este campo."
                        : (fileHintTarget.dataset.defaultHint || "");
            }

            if (downloadLink) {
                const enabled = Boolean(state.modalRecord?.recordId && hasExisting);
                downloadLink.href = enabled
                    ? buildDownloadUrl(state.modalRecord.recordId, fieldName)
                    : "#";
                downloadLink.classList.toggle("is-disabled", !enabled);
            }
        });
    }

    async function saveStage() {
        if (state.saving || !state.modalRecord) {
            return;
        }

        let payload;
        try {
            payload = buildStagePayload();
        } catch (error) {
            setStatus(elements.modalStatus, "error", getErrorMessage(error));
            return;
        }

        try {
            state.saving = true;
            setBusy(true);
            setStatus(elements.modalStatus, "info", "Guardando etapa de Hardware...");
            await uploadPendingFiles();
            const result = await fetchJson(config.saveUrl, {
                method: "POST",
                body: JSON.stringify(payload)
            });

            closeModal(true);
            setBusy(false);
            await loadBoard();
            setStatus(elements.status, "success", result?.message || "Etapa guardada correctamente.");
        } catch (error) {
            setStatus(elements.modalStatus, "error", getErrorMessage(error));
        } finally {
            state.saving = false;
            if (state.busy) {
                setBusy(false);
            }
        }
    }

    async function uploadPendingFiles() {
        if (!state.modalRecord) {
            return;
        }

        const entries = Object.entries(state.pendingFiles)
            .filter(([, file]) => file instanceof File);
        for (const [fieldName, file] of entries) {
            const formData = new FormData();
            formData.append("recordId", state.modalRecord.recordId || "");
            formData.append("fieldName", fieldName);
            formData.append("file", file);

            const result = await fetchJson(config.uploadUrl, {
                method: "POST",
                body: formData
            });

            state.modalRecord = result?.record ? { ...result.record } : state.modalRecord;
            state.pendingFiles[fieldName] = null;
        }

        renderFileCards();
    }

    function buildStagePayload() {
        if (!state.modalRecord) {
            throw new Error("No hay un registro de Hardware activo.");
        }

        const actionKey = elements.actionKey.value || "";
        const recordId = elements.recordId.value || "";
        const odcDate = (elements.fields.odcDate?.value || "").trim();
        const supplierUnitCost = parseDecimal(elements.fields.supplierUnitCost?.value || "");
        const provider = (elements.fields.provider?.value || "").trim();
        const supplierPaymentDate = (elements.fields.supplierPaymentDate?.value || "").trim();
        const deliveryRecordDate = (elements.fields.deliveryRecordDate?.value || "").trim();
        const invoiceNumber = (elements.fields.invoiceNumber?.value || "").trim();

        const requiredFiles = stageConfig[actionKey]?.requiredFiles || [];
        requiredFiles.forEach(fieldName => {
            if (!hasFileOrPending(fieldName)) {
                throw new Error(`Debes cargar el archivo requerido para ${resolveFileLabel(fieldName)} antes de guardar.`);
            }
        });

        switch (actionKey) {
            case "register-documentation":
                if (!odcDate) {
                    throw new Error("Debes diligenciar la Fecha ODC.");
                }
                if (!(supplierUnitCost > 0)) {
                    throw new Error("Debes diligenciar un Costo Unt Proveedor antes de IVA válido.");
                }
                if (!provider) {
                    throw new Error("Debes diligenciar el Proveedor.");
                }
                break;

            case "register-supplier-payment":
                if (!supplierPaymentDate) {
                    throw new Error("Debes diligenciar la Fecha de pago a proveedor.");
                }
                break;

            case "register-client-received":
                if (!deliveryRecordDate) {
                    throw new Error("Debes diligenciar la Fecha acta de entrega.");
                }
                break;

            case "register-invoice":
                if (!invoiceNumber) {
                    throw new Error("Debes seleccionar un número de factura.");
                }
                break;

            default:
                break;
        }

        return {
            recordId,
            actionKey,
            odcDateValue: odcDate,
            supplierUnitCost,
            provider,
            supplierPaymentDateValue: supplierPaymentDate,
            deliveryRecordDateValue: deliveryRecordDate,
            invoiceNumber
        };
    }

    function hasFileOrPending(fieldName) {
        return Boolean(state.pendingFiles[fieldName] instanceof File) || hasExistingFile(fieldName);
    }

    function hasExistingFile(fieldName) {
        if (!state.modalRecord) {
            return false;
        }

        switch (fieldName) {
            case "cr07a_ordendecompra":
                return Boolean(state.modalRecord.hasOrderPurchase);
            case "cr07a_adjuntarproforma":
                return Boolean(state.modalRecord.hasProforma);
            case "cr07a_pagoaproveedor":
                return Boolean(state.modalRecord.hasSupplierPaymentProof);
            case "cr07a_actadeentrega":
                return Boolean(state.modalRecord.hasDeliveryRecord);
            default:
                return false;
        }
    }

    function resolveExistingFileName(fieldName) {
        if (!state.modalRecord) {
            return "";
        }

        switch (fieldName) {
            case "cr07a_ordendecompra":
                return state.modalRecord.orderPurchaseFileName || "";
            case "cr07a_adjuntarproforma":
                return state.modalRecord.proformaFileName || "";
            case "cr07a_pagoaproveedor":
                return state.modalRecord.supplierPaymentProofFileName || "";
            case "cr07a_actadeentrega":
                return state.modalRecord.deliveryRecordFileName || "";
            default:
                return "";
        }
    }

    function resolveFileLabel(fieldName) {
        switch (fieldName) {
            case "cr07a_ordendecompra":
                return "Adjuntar ODC";
            case "cr07a_adjuntarproforma":
                return "Adjuntar Proforma";
            case "cr07a_pagoaproveedor":
                return "Adjuntar pago a proveedor";
            case "cr07a_actadeentrega":
                return "Adjuntar acta de entrega";
            default:
                return "archivo";
        }
    }

    function handleInvoiceLookupInput() {
        const query = (elements.fields.invoiceNumber?.value || "").trim();
        window.clearTimeout(state.invoiceLookupTimer);

        if (query.length < 2) {
            state.invoiceSuggestions = [];
            elements.invoiceOptions.innerHTML = "";
            return;
        }

        const sequence = ++state.invoiceLookupSequence;
        state.invoiceLookupTimer = window.setTimeout(async () => {
            try {
                const result = await fetchJson(buildInvoiceSearchUrl(query), { method: "GET" });
                if (sequence !== state.invoiceLookupSequence) {
                    return;
                }

                state.invoiceSuggestions = Array.isArray(result) ? result : [];
                elements.invoiceOptions.innerHTML = state.invoiceSuggestions.map(item => `
                    <option value="${escapeHtml(item.number || "")}" label="${escapeHtml(buildInvoiceOptionLabel(item))}"></option>
                `).join("");
            } catch {
                if (sequence !== state.invoiceLookupSequence) {
                    return;
                }

                state.invoiceSuggestions = [];
                elements.invoiceOptions.innerHTML = "";
            }
        }, 220);
    }

    function syncInvoiceSelection() {
        const inputValue = normalizeText(elements.fields.invoiceNumber?.value || "");
        const exactMatch = state.invoiceSuggestions.find(item => normalizeText(item.number || "") === inputValue);
        if (exactMatch && elements.fields.invoiceNumber) {
            elements.fields.invoiceNumber.value = exactMatch.number || "";
        }
    }

    function buildInvoiceOptionLabel(item) {
        const parts = [];
        if (item?.clientName) {
            parts.push(item.clientName);
        }
        if (Number(item?.paymentValue || 0) > 0) {
            parts.push(`Pago ${formatCurrency(item.paymentValue || 0)}`);
        }
        return parts.join(" · ");
    }

    function buildBoardUrl() {
        const url = new URL(config.boardUrl, window.location.origin);
        const stateValue = elements.stateFilter.value || "";
        if (stateValue) {
            url.searchParams.set("stateValue", stateValue);
        }

        return `${url.pathname}${url.search}`;
    }

    function buildInvoiceSearchUrl(query) {
        const url = new URL(config.invoiceSearchUrl, window.location.origin);
        url.searchParams.set("q", query);
        return `${url.pathname}${url.search}`;
    }

    function buildDownloadUrl(recordId, fieldName) {
        const url = new URL(config.downloadUrl, window.location.origin);
        url.searchParams.set("recordId", recordId);
        url.searchParams.set("fieldName", fieldName);
        return `${url.pathname}${url.search}`;
    }

    function setBusy(isBusy) {
        state.busy = isBusy;
        [
            elements.fileInput,
            elements.analyzeBtn,
            elements.provisionBtn,
            elements.stateFilter,
            elements.refreshBtn,
            elements.fields.odcDate,
            elements.fields.supplierUnitCost,
            elements.fields.provider,
            elements.fields.supplierPaymentDate,
            elements.fields.deliveryRecordDate,
            elements.fields.invoiceNumber,
            elements.saveStageBtn
        ].forEach(element => {
            if (element) {
                element.disabled = isBusy;
            }
        });

        elements.fileInputs.forEach(input => {
            input.disabled = isBusy;
        });

        elements.closeModalButtons.forEach(button => {
            button.disabled = isBusy;
        });
    }

    async function fetchJson(url, options = {}) {
        const isFormData = options.body instanceof FormData;
        const headers = {
            Accept: "application/json",
            ...(options.headers || {})
        };

        if (!isFormData && options.body && !headers["Content-Type"]) {
            headers["Content-Type"] = "application/json";
        }

        const response = await fetch(url, {
            method: options.method || "GET",
            headers: isFormData ? { Accept: headers.Accept } : headers,
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
                        : payload?.message || payload?.detail || payload?.title || rawBody;
                } catch {
                    message = rawBody;
                }
            }

            throw new Error(message || "No fue posible completar la solicitud.");
        }

        if (!contentType.includes("application/json")) {
            const message = await response.text();
            throw new Error(message || "La respuesta del servidor no fue válida.");
        }

        return response.json();
    }

    function setStatus(target, type, message) {
        if (!target) {
            return;
        }

        if (!message) {
            clearStatus(target);
            return;
        }

        const baseClass = target.dataset.baseClass || target.className || "hardware-status";
        target.className = `${baseClass} is-visible is-${type}`;
        target.textContent = message;
    }

    function clearStatus(target) {
        if (!target) {
            return;
        }

        target.className = target.dataset.baseClass || "hardware-status";
        target.textContent = "";
    }

    function getErrorMessage(error) {
        return error instanceof Error ? error.message : "Ocurrió un error inesperado.";
    }

    function setFieldValue(element, value) {
        if (element) {
            element.value = value ?? "";
        }
    }

    function resetFileInputs() {
        elements.fileInputs.forEach(input => {
            input.value = "";
        });
    }

    function toneClass(tone) {
        return tone ? `is-${escapeHtml(tone)}` : "";
    }

    function renderPill(label, tone) {
        return `<span class="hardware-pill ${toneClass(tone)}">${escapeHtml(label || "-")}</span>`;
    }

    function formatNumber(value) {
        return new Intl.NumberFormat("es-CO").format(Number(value || 0));
    }

    function formatCurrency(value) {
        return new Intl.NumberFormat("es-CO", {
            style: "currency",
            currency: "COP",
            minimumFractionDigits: 0,
            maximumFractionDigits: 0
        }).format(Number(value || 0));
    }

    function parseDecimal(value) {
        const parsed = Number.parseFloat(String(value || "").replace(",", "."));
        return Number.isFinite(parsed) ? parsed : 0;
    }

    function formatInputNumber(value) {
        return Number(value || 0) > 0 ? Number(value).toFixed(2) : "";
    }

    function normalizeText(value) {
        return String(value || "")
            .normalize("NFD")
            .replace(/[\u0300-\u036f]/g, "")
            .toLowerCase()
            .trim();
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
