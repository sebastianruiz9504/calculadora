(function () {
    const app = document.getElementById("rhApp");
    if (!app) {
        return;
    }

    const tableKey = app.dataset.tableKey || "";
    const loadUrl = app.dataset.loadUrl || "";
    const saveUrl = app.dataset.saveUrl || "";
    const uploadUrl = app.dataset.uploadUrl || "";
    const downloadUrl = app.dataset.downloadUrl || "";

    const statusBanner = document.getElementById("rhStatusBanner");
    const refreshBtn = document.getElementById("rhRefreshBtn");
    const newBtn = document.getElementById("rhNewBtn");
    const saveBtn = document.getElementById("rhSaveBtn");
    const listHead = document.getElementById("rhListHead");
    const listBody = document.getElementById("rhListBody");
    const emptyState = document.getElementById("rhEmptyState");
    const formBody = document.getElementById("rhFormBody");
    const formTitle = document.getElementById("rhEditorModalLabel");
    const formSubtitle = document.getElementById("rhFormSubtitle");
    const recordPill = document.getElementById("rhRecordPill");
    const tableDescription = document.getElementById("rhTableDescription");
    const recordsCount = document.getElementById("rhRecordsCount");
    const modalElement = document.getElementById("rhEditorModal");
    const resultDialog = document.getElementById("rhResultDialog");
    const resultDialogTitle = document.getElementById("rhResultDialogTitle");
    const resultDialogMessage = document.getElementById("rhResultDialogMessage");
    const resultDialogDetail = document.getElementById("rhResultDialogDetail");
    const resultDialogCloseBtn = document.getElementById("rhResultDialogCloseBtn");
    const editorModal = modalElement && window.bootstrap
        ? window.bootstrap.Modal.getOrCreateInstance(modalElement)
        : null;

    const state = {
        data: null,
        currentId: "",
        busy: false
    };

    refreshBtn?.addEventListener("click", async () => {
        await loadData(state.currentId);
    });

    newBtn?.addEventListener("click", () => {
        openEditor("");
    });

    saveBtn?.addEventListener("click", async () => {
        await saveCurrentRecord();
    });

    listBody?.addEventListener("click", (event) => {
        if (state.busy) {
            return;
        }

        const target = event.target;
        const row = target instanceof HTMLElement
            ? target.closest("[data-record-id]")
            : null;

        if (!(row instanceof HTMLElement)) {
            return;
        }

        openEditor(row.dataset.recordId || "");
    });

    formBody?.addEventListener("click", async (event) => {
        const target = event.target;
        if (!(target instanceof HTMLElement)) {
            return;
        }

        const uploadButton = target.closest("[data-upload-field]");
        if (!(uploadButton instanceof HTMLElement)) {
            return;
        }

        const fieldName = uploadButton.dataset.uploadField || "";
        await uploadFieldFile(fieldName);
    });

    formBody?.addEventListener("input", (event) => {
        const target = event.target;
        if (!(target instanceof HTMLInputElement) || !target.dataset.rhLookupDisplay) {
            return;
        }

        syncLookupInput(target, false);
    });

    formBody?.addEventListener("change", (event) => {
        const target = event.target;
        if (!(target instanceof HTMLInputElement) || !target.dataset.rhLookupDisplay) {
            return;
        }

        syncLookupInput(target, true);
    });

    resultDialogCloseBtn?.addEventListener("click", () => {
        hideResultDialog();
    });

    resultDialog?.addEventListener("click", (event) => {
        if (event.target === resultDialog) {
            hideResultDialog();
        }
    });

    modalElement?.addEventListener("hidden.bs.modal", () => {
        hideResultDialog();
    });

    loadData();

    async function loadData(preferredRecordId) {
        try {
            setBusy(true);
            hideResultDialog();
            renderStatus("info", "Cargando registros de RH...");

            const response = await fetch(`${loadUrl}?tableKey=${encodeURIComponent(tableKey)}`, {
                headers: {
                    Accept: "application/json"
                }
            });

            const payload = await readPayload(response);
            if (!response.ok) {
                throw createResponseError(payload);
            }

            state.data = payload;
            const records = getRecords();

            if (preferredRecordId && records.some((item) => item.recordId === preferredRecordId)) {
                state.currentId = preferredRecordId;
            } else if (state.currentId && !records.some((item) => item.recordId === state.currentId)) {
                state.currentId = "";
            }

            renderAll();
            renderStatus("success", `Modulo cargado: ${payload.title || "RH"}.`);
        } catch (error) {
            renderStatus("error", buildErrorBannerMessage(error));
        } finally {
            setBusy(false);
        }
    }

    async function saveCurrentRecord() {
        try {
            if (!state.data) {
                return;
            }

            hideResultDialog();
            const lookupValidationMessage = validateLookupInputs();
            if (lookupValidationMessage) {
                showResultDialog(
                    "warning",
                    "Selecciona una opcion valida",
                    lookupValidationMessage);
                return;
            }

            const isCreate = !state.currentId;
            const values = collectValues();
            setBusy(true);

            const response = await fetch(saveUrl, {
                method: "POST",
                headers: {
                    "Content-Type": "application/json"
                },
                body: JSON.stringify({
                    tableKey,
                    recordId: state.currentId,
                    values
                })
            });

            const payload = await readPayload(response);
            if (!response.ok) {
                throw createResponseError(payload);
            }

            upsertRecord(payload.record);
            state.currentId = payload.record?.recordId || "";
            renderAll();
            renderForm();
            showResultDialog(
                "success",
                isCreate ? "Registro creado" : "Registro actualizado",
                payload.message || "Registro guardado correctamente.");
        } catch (error) {
            showResultDialog(
                "error",
                "No se pudo guardar el registro",
                buildErrorMessage(error),
                buildErrorDetail(error));
        } finally {
            setBusy(false);
        }
    }

    async function uploadFieldFile(fieldName) {
        try {
            hideResultDialog();

            if (!state.currentId) {
                showResultDialog(
                    "warning",
                    "Guarda primero el registro",
                    "Primero guarda el registro y luego carga el archivo.");
                return;
            }

            const fileInput = document.getElementById(`rhFile-${fieldName}`);
            if (!(fileInput instanceof HTMLInputElement) || !fileInput.files || fileInput.files.length === 0) {
                showResultDialog(
                    "warning",
                    "Archivo requerido",
                    "Selecciona un archivo antes de continuar.");
                return;
            }

            const file = fileInput.files[0];
            const formData = new FormData();
            formData.append("tableKey", tableKey);
            formData.append("recordId", state.currentId);
            formData.append("fieldName", fieldName);
            formData.append("file", file);

            setBusy(true);

            const response = await fetch(uploadUrl, {
                method: "POST",
                body: formData
            });

            const payload = await readPayload(response);
            if (!response.ok) {
                throw createResponseError(payload);
            }

            upsertRecord(payload.record);
            renderAll();
            renderForm();
            showResultDialog(
                "success",
                "Archivo cargado",
                payload.message || "Archivo cargado correctamente.");
        } catch (error) {
            showResultDialog(
                "error",
                "No se pudo cargar el archivo",
                buildErrorMessage(error),
                buildErrorDetail(error));
        } finally {
            setBusy(false);
        }
    }

    function openEditor(recordId) {
        state.currentId = recordId || "";
        hideResultDialog();
        renderList();
        renderForm();

        if (editorModal) {
            editorModal.show();
        }
    }

    function renderAll() {
        renderList();
        updateSummary();
    }

    function renderList() {
        const fields = getListFields();
        const records = getRecords();

        listHead.innerHTML = `
            <tr>
                ${fields.map((field) => `<th>${escapeHtml(field.label)}</th>`).join("")}
            </tr>
        `;

        listBody.innerHTML = records.map((record) => {
            const cells = fields.map((field) => {
                const cell = getCell(record, field.logicalName);
                return `<td>${renderListCell(field, cell)}</td>`;
            }).join("");

            return `
                <tr class="rh-list-row ${record.recordId === state.currentId ? "is-selected" : ""}" data-record-id="${escapeHtml(record.recordId)}">
                    ${cells}
                </tr>
            `;
        }).join("");

        emptyState.hidden = records.length > 0;
        emptyState.textContent = state.data?.emptyStateMessage || "No hay registros todavia.";
    }

    function renderForm() {
        const fields = getFields();
        const record = getCurrentRecord();
        const isNew = !record;

        formTitle.textContent = isNew ? "Nuevo registro" : "Editar registro";
        formSubtitle.textContent = isNew
            ? "Completa los campos y guarda para crear el registro."
            : "Actualiza la informacion necesaria y guarda los cambios.";
        recordPill.textContent = isNew ? "Sin guardar" : "Editando";
        formBody.innerHTML = fields.map((field) => buildFieldMarkup(field, record)).join("");
        setBusy(state.busy);
    }

    function buildFieldMarkup(field, record) {
        const value = getFieldValue(record, field);
        const isWide = field.logicalName === "cr07a_motivo"
            || field.logicalName === "cr07a_usuario"
            || field.editorType === "file"
            || field.editorType === "image";

        return `
            <div class="rh-field ${isWide ? "rh-field--span-2" : ""}">
                <label for="rhField-${field.logicalName}">
                    ${escapeHtml(field.label)}
                    ${field.required ? '<span class="rh-required">*</span>' : ""}
                </label>
                ${buildEditorMarkup(field, value, record)}
                ${field.helpText ? `<small>${escapeHtml(field.helpText)}</small>` : ""}
            </div>
        `;
    }

    function buildEditorMarkup(field, value, record) {
        if (field.editorType === "lookup") {
            return buildLookupEditorMarkup(field, value, record);
        }

        if (field.editorType === "option") {
            const options = Array.isArray(field.options) ? field.options : [];
            return `
                <select class="form-select" id="rhField-${field.logicalName}" data-rh-input="${field.logicalName}">
                    <option value="">Selecciona una opcion</option>
                    ${options.map((option) => `
                        <option value="${escapeHtml(option.value)}" ${option.value === value ? "selected" : ""}>
                            ${escapeHtml(option.label)}
                        </option>
                    `).join("")}
                </select>
            `;
        }

        if (field.editorType === "file" || field.editorType === "image") {
            const cell = record ? getCell(record, field.logicalName) : null;
            const hasContent = Boolean(cell && cell.hasContent);
            const inputId = `rhFile-${field.logicalName}`;
            const downloadHref = record && hasContent
                ? buildDownloadUrl(record.recordId, field.logicalName)
                : "";
            const previewHref = record && hasContent && field.editorType === "image"
                ? buildDownloadUrl(record.recordId, field.logicalName, true)
                : "";
            const preview = field.editorType === "image" && record && hasContent
                ? `<img class="rh-image-preview" src="${escapeHtml(previewHref)}" alt="${escapeHtml(field.label)}" />`
                : "";

            return `
                <div class="rh-file-card">
                    ${preview}
                    <div class="rh-file-meta">
                        ${hasContent ? escapeHtml(cell.fileName || cell.displayValue || "Archivo cargado") : "Sin archivo cargado"}
                    </div>
                    ${record && hasContent ? `<a class="rh-download-link" href="${escapeHtml(downloadHref)}" target="_blank" rel="noopener">Descargar</a>` : ""}
                    <div class="rh-file-actions">
                        <input class="form-control" id="${inputId}" type="file" accept="${escapeHtml(field.accept || "")}" ${record ? "" : "disabled"} />
                        <button type="button" class="btn btn-outline-primary" data-upload-field="${escapeHtml(field.logicalName)}" ${record ? "" : "disabled"}>
                            ${field.editorType === "image" ? "Cargar foto" : "Cargar archivo"}
                        </button>
                    </div>
                </div>
            `;
        }

        const inputType = resolveInputType(field.editorType);
        const step = field.editorType === "number" || field.editorType === "currency" ? 'step="0.01"' : "";
        return `
            <input
                class="form-control"
                id="rhField-${field.logicalName}"
                type="${inputType}"
                value="${escapeHtml(value)}"
                placeholder="${escapeHtml(field.placeholder || "")}"
                data-rh-input="${field.logicalName}"
                ${step} />
        `;
    }

    function buildLookupEditorMarkup(field, value, record) {
        const options = getLookupOptions(field);
        const selectedOption = options.find((option) => option.value === value) || null;
        const cell = record ? getCell(record, field.logicalName) : null;
        const displayValue = selectedOption?.label || cell?.lookupLabel || cell?.displayValue || "";
        const datalistId = `rhLookupOptions-${field.logicalName}`;

        return `
            <div class="rh-lookup">
                <input
                    class="form-control rh-lookup__input"
                    id="rhField-${field.logicalName}-display"
                    type="search"
                    value="${escapeHtml(displayValue)}"
                    placeholder="${escapeHtml(field.placeholder || "Escribe para buscar")}"
                    autocomplete="off"
                    list="${datalistId}"
                    data-rh-lookup-display="${field.logicalName}" />
                <datalist id="${datalistId}">
                    ${options.map((option) => `<option value="${escapeHtml(option.label)}"></option>`).join("")}
                </datalist>
                <input
                    id="rhField-${field.logicalName}"
                    type="hidden"
                    value="${escapeHtml(value)}"
                    data-rh-input="${field.logicalName}" />
            </div>
        `;
    }

    function collectValues() {
        const values = {};
        formBody.querySelectorAll("[data-rh-input]").forEach((element) => {
            if (!(element instanceof HTMLInputElement) && !(element instanceof HTMLSelectElement) && !(element instanceof HTMLTextAreaElement)) {
                return;
            }

            const fieldName = element.dataset.rhInput || "";
            if (!fieldName) {
                return;
            }

            values[fieldName] = element.value || "";
        });

        return values;
    }

    function validateLookupInputs() {
        const lookupInputs = formBody.querySelectorAll("[data-rh-lookup-display]");
        for (const element of lookupInputs) {
            if (!(element instanceof HTMLInputElement)) {
                continue;
            }

            const result = syncLookupInput(element, true);
            if (!result.isValid) {
                return result.message;
            }
        }

        return "";
    }

    function syncLookupInput(displayInput, enforceSelection) {
        const fieldName = displayInput.dataset.rhLookupDisplay || "";
        const hiddenInput = document.getElementById(`rhField-${fieldName}`);
        if (!(hiddenInput instanceof HTMLInputElement)) {
            return { isValid: true, message: "" };
        }

        const rawText = displayInput.value.trim();
        if (!rawText) {
            hiddenInput.value = "";
            displayInput.setCustomValidity("");
            return { isValid: true, message: "" };
        }

        const matchedOption = findLookupOption(fieldName, rawText);
        if (matchedOption) {
            hiddenInput.value = matchedOption.value;
            displayInput.value = matchedOption.label;
            displayInput.setCustomValidity("");
            return { isValid: true, message: "" };
        }

        hiddenInput.value = "";
        const message = `Selecciona una opcion valida para ${resolveFieldLabel(fieldName).toLowerCase()}.`;
        if (enforceSelection) {
            displayInput.setCustomValidity(message);
            displayInput.reportValidity();
        } else {
            displayInput.setCustomValidity("");
        }

        return { isValid: false, message };
    }

    function findLookupOption(fieldName, rawText) {
        const field = getFields().find((item) => item.logicalName === fieldName);
        const options = getLookupOptions(field);
        const normalizedText = normalizeLookupText(rawText);

        return options.find((option) => normalizeLookupText(option.label) === normalizedText)
            || options.find((option) => normalizeLookupText(option.value) === normalizedText)
            || null;
    }

    function getLookupOptions(field) {
        return Array.isArray(field?.options) ? field.options : [];
    }

    function resolveFieldLabel(fieldName) {
        return getFields().find((field) => field.logicalName === fieldName)?.label || "este campo";
    }

    function normalizeLookupText(value) {
        return String(value || "")
            .trim()
            .replace(/\s+/g, " ")
            .toLowerCase();
    }

    function updateSummary() {
        const records = getRecords();
        const title = state.data?.description || state.data?.title || "";
        tableDescription.textContent = title;
        recordsCount.textContent = `${records.length} ${records.length === 1 ? "registro" : "registros"}`;
    }

    function upsertRecord(record) {
        if (!record || !record.recordId || !state.data) {
            return;
        }

        const records = getRecords().slice();
        const index = records.findIndex((item) => item.recordId === record.recordId);
        if (index >= 0) {
            records[index] = record;
        } else {
            records.unshift(record);
        }

        state.data.records = records;
    }

    function getRecords() {
        return Array.isArray(state.data?.records) ? state.data.records : [];
    }

    function getFields() {
        return Array.isArray(state.data?.fields) ? state.data.fields : [];
    }

    function getListFields() {
        return getFields().filter((field) => field.showInList);
    }

    function getCurrentRecord() {
        return getRecords().find((item) => item.recordId === state.currentId) || null;
    }

    function getCell(record, logicalName) {
        return record && record.cells ? record.cells[logicalName] || null : null;
    }

    function getFieldValue(record, field) {
        const cell = getCell(record, field.logicalName);
        if (!cell) {
            return "";
        }

        if (field.editorType === "lookup") {
            return cell.lookupId || cell.value || "";
        }

        return cell.value || "";
    }

    function renderListCell(field, cell) {
        if (!cell) {
            return "";
        }

        if (field.editorType === "file" || field.editorType === "image") {
            return cell.hasContent ? escapeHtml(cell.fileName || cell.displayValue || "Cargado") : "";
        }

        return escapeHtml(cell.displayValue || cell.lookupLabel || cell.value || "");
    }

    function buildDownloadUrl(recordId, fieldName, inline) {
        const params = new URLSearchParams({
            tableKey,
            recordId,
            fieldName
        });

        if (inline) {
            params.set("inline", "true");
        }

        return `${downloadUrl}?${params.toString()}`;
    }

    function resolveInputType(editorType) {
        switch (editorType) {
            case "date":
                return "date";
            case "email":
                return "email";
            case "phone":
                return "tel";
            case "number":
            case "currency":
                return "number";
            default:
                return "text";
        }
    }

    function setBusy(isBusy) {
        state.busy = isBusy;

        if (refreshBtn) {
            refreshBtn.disabled = isBusy;
        }

        if (newBtn) {
            newBtn.disabled = isBusy;
        }

        if (saveBtn) {
            saveBtn.disabled = isBusy;
        }

        formBody.querySelectorAll("button, input, select, textarea").forEach((element) => {
            if (element instanceof HTMLButtonElement && element.dataset.uploadField) {
                element.disabled = isBusy || !state.currentId;
                return;
            }

            if (element instanceof HTMLInputElement && element.type === "file") {
                element.disabled = isBusy || !state.currentId;
                return;
            }

            if (element instanceof HTMLInputElement || element instanceof HTMLSelectElement || element instanceof HTMLTextAreaElement) {
                element.disabled = isBusy;
            }
        });
    }

    function renderStatus(level, message) {
        if (!statusBanner) {
            return;
        }

        statusBanner.className = `rh-status rh-status--${level} is-visible`;
        statusBanner.textContent = message;
    }

    function showResultDialog(level, title, message, detail) {
        const safeMessage = message || title || "Operacion completada.";
        const safeDetail = detail || "";

        if (!resultDialog || !resultDialogTitle || !resultDialogMessage || !resultDialogCloseBtn) {
            renderStatus(level, [safeMessage, safeDetail].filter(Boolean).join(" | "));
            return;
        }

        resultDialog.hidden = false;
        resultDialog.className = `rh-modal-result rh-modal-result--${level} is-visible`;
        resultDialogTitle.textContent = title || "Resultado";
        resultDialogMessage.textContent = safeMessage;

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
        resultDialog.className = "rh-modal-result";

        if (resultDialogDetail) {
            resultDialogDetail.hidden = true;
            resultDialogDetail.textContent = "";
        }
    }

    function buildErrorBannerMessage(error) {
        const message = buildErrorMessage(error);
        const detail = buildErrorDetail(error).replaceAll("\n", " | ");
        return [message, detail].filter(Boolean).join(" | ");
    }

    function buildErrorMessage(error) {
        if (!error) {
            return "Ocurrio un error inesperado.";
        }

        return error.message || "Ocurrio un error inesperado.";
    }

    function buildErrorDetail(error) {
        if (!error) {
            return "";
        }

        const parts = [];
        if (error.detail) {
            parts.push(error.detail);
        }

        if (error.traceId) {
            parts.push(`TraceId: ${error.traceId}`);
        }

        return parts.join("\n");
    }

    function createResponseError(payload) {
        return {
            message: payload?.message || "La operacion no se pudo completar.",
            detail: payload?.detail || "",
            traceId: payload?.traceId || ""
        };
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
