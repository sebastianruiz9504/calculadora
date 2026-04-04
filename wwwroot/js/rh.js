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
    const editorModal = modalElement && window.bootstrap
        ? window.bootstrap.Modal.getOrCreateInstance(modalElement)
        : null;

    const state = {
        data: null,
        currentId: "",
        busy: false
    };

    refreshBtn.addEventListener("click", async () => {
        await loadData(state.currentId);
    });

    newBtn.addEventListener("click", () => {
        openEditor("");
    });

    saveBtn.addEventListener("click", async () => {
        await saveCurrentRecord();
    });

    listBody.addEventListener("click", (event) => {
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

    formBody.addEventListener("click", async (event) => {
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

    loadData();

    async function loadData(preferredRecordId) {
        try {
            setBusy(true);
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
            renderStatus("error", buildErrorMessage(error));
        } finally {
            setBusy(false);
        }
    }

    async function saveCurrentRecord() {
        try {
            if (!state.data) {
                return;
            }

            const values = collectValues();
            setBusy(true);
            renderStatus("info", "Guardando cambios...");

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
            renderStatus("success", payload.message || "Registro guardado correctamente.");
        } catch (error) {
            renderStatus("error", buildErrorMessage(error));
        } finally {
            setBusy(false);
        }
    }

    async function uploadFieldFile(fieldName) {
        try {
            if (!state.currentId) {
                renderStatus("warning", "Primero guarda el registro y luego carga el archivo.");
                return;
            }

            const fileInput = document.getElementById(`rhFile-${fieldName}`);
            if (!(fileInput instanceof HTMLInputElement) || !fileInput.files || fileInput.files.length === 0) {
                renderStatus("warning", "Selecciona un archivo antes de continuar.");
                return;
            }

            const file = fileInput.files[0];
            const formData = new FormData();
            formData.append("tableKey", tableKey);
            formData.append("recordId", state.currentId);
            formData.append("fieldName", fieldName);
            formData.append("file", file);

            setBusy(true);
            renderStatus("info", "Cargando archivo...");

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
            renderStatus("success", payload.message || "Archivo cargado correctamente.");
        } catch (error) {
            renderStatus("error", buildErrorMessage(error));
        } finally {
            setBusy(false);
        }
    }

    function openEditor(recordId) {
        state.currentId = recordId || "";
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
    }

    function buildFieldMarkup(field, record) {
        const value = getFieldValue(record, field);
        const isWide = field.logicalName === "cr07a_motivo"
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
        if (field.editorType === "lookup" || field.editorType === "option") {
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
        refreshBtn.disabled = isBusy;
        newBtn.disabled = isBusy;
        saveBtn.disabled = isBusy;

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
        statusBanner.className = `rh-status rh-status--${level} is-visible`;
        statusBanner.textContent = message;
    }

    function buildErrorMessage(error) {
        if (!error) {
            return "Ocurrio un error inesperado.";
        }

        const parts = [];
        if (error.message) {
            parts.push(error.message);
        }

        if (error.detail) {
            parts.push(error.detail);
        }

        if (error.traceId) {
            parts.push(`TraceId: ${error.traceId}`);
        }

        return parts.join(" | ");
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
