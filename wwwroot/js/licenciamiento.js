(function () {
    const app = document.getElementById("licenciamientoApp");
    if (!app) {
        return;
    }

    const loadUrl = app.dataset.loadUrl || "";
    const previewUrl = app.dataset.previewUrl || "";
    const importUrl = app.dataset.importUrl || "";
    const adjustTrmUrl = app.dataset.adjustTrmUrl || "";
    const updateContractUrl = app.dataset.updateContractUrl || "";

    const statusBanner = document.getElementById("licStatus");
    const refreshBtn = document.getElementById("licRefreshBtn");
    const newBtn = document.getElementById("licNewBtn");
    const trmBtn = document.getElementById("licTrmBtn");
    const contractBtn = document.getElementById("licContractBtn");
    const selectedCount = document.getElementById("licSelectedCount");
    const totalCount = document.getElementById("licTotalCount");
    const totalUsd = document.getElementById("licTotalUsd");
    const totalCop = document.getElementById("licTotalCop");
    const rowsBody = document.getElementById("licRowsBody");
    const emptyState = document.getElementById("licEmpty");
    const selectAll = document.getElementById("licSelectAll");

    const uploadModal = document.getElementById("licUploadModal");
    const uploadStatus = document.getElementById("licUploadStatus");
    const uploadForm = document.getElementById("licUploadForm");
    const fileInput = document.getElementById("licExcelFile");
    const previewBtn = document.getElementById("licPreviewBtn");
    const importBtn = document.getElementById("licImportBtn");
    const previewSummary = document.getElementById("licPreviewSummary");
    const previewRowsCount = document.getElementById("licPreviewRowsCount");
    const previewValidCount = document.getElementById("licPreviewValidCount");
    const previewTotalUsd = document.getElementById("licPreviewTotalUsd");
    const previewWrap = document.getElementById("licPreviewWrap");
    const previewBody = document.getElementById("licPreviewBody");

    const trmModal = document.getElementById("licTrmModal");
    const trmStatus = document.getElementById("licTrmStatus");
    const trmForm = document.getElementById("licTrmForm");
    const facturaSelect = document.getElementById("licFacturaSelect");
    const trmInput = document.getElementById("licTrmInput");
    const trmSaveBtn = document.getElementById("licTrmSaveBtn");

    const contractModal = document.getElementById("licContractModal");
    const contractStatus = document.getElementById("licContractStatus");
    const contractForm = document.getElementById("licContractForm");
    const contractSelect = document.getElementById("licContractSelect");
    const contractSaveBtn = document.getElementById("licContractSaveBtn");

    const usdFormatter = new Intl.NumberFormat("es-CO", {
        style: "currency",
        currency: "USD",
        minimumFractionDigits: 2,
        maximumFractionDigits: 2
    });
    const copFormatter = new Intl.NumberFormat("es-CO", {
        style: "currency",
        currency: "COP",
        minimumFractionDigits: 0,
        maximumFractionDigits: 0
    });
    const numberFormatter = new Intl.NumberFormat("es-CO", {
        minimumFractionDigits: 0,
        maximumFractionDigits: 2
    });

    const state = {
        busy: false,
        board: null,
        selectedIds: new Set(),
        previewRows: [],
        contractTypeOptions: []
    };

    refreshBtn?.addEventListener("click", loadBoard);
    newBtn?.addEventListener("click", openUploadModal);
    trmBtn?.addEventListener("click", openTrmModal);
    contractBtn?.addEventListener("click", openContractModal);
    selectAll?.addEventListener("change", toggleSelectAll);
    importBtn?.addEventListener("click", importPreviewRows);

    uploadForm?.addEventListener("submit", async (event) => {
        event.preventDefault();
        await previewExcel();
    });

    trmForm?.addEventListener("submit", async (event) => {
        event.preventDefault();
        await adjustTrm();
    });

    contractForm?.addEventListener("submit", async (event) => {
        event.preventDefault();
        await updateContractType();
    });

    document.addEventListener("change", (event) => {
        const target = event.target;
        if (!(target instanceof HTMLElement)) {
            return;
        }

        if (target.matches("[data-lic-row-check]")) {
            const checkbox = target;
            const recordId = checkbox.getAttribute("data-record-id") || "";
            if (checkbox.checked) {
                state.selectedIds.add(recordId);
            } else {
                state.selectedIds.delete(recordId);
            }
            renderSelectionState();
            return;
        }

        if (target.matches("[data-preview-contract]")) {
            const select = target;
            const index = Number.parseInt(select.getAttribute("data-preview-contract") || "-1", 10);
            const row = state.previewRows[index];
            if (!row) {
                return;
            }

            row.contractTypeValue = Number.parseInt(select.value, 10);
            const option = state.contractTypeOptions.find((item) => Number(item.value) === Number(row.contractTypeValue));
            row.contractTypeLabel = option?.label || "";
        }
    });

    document.addEventListener("click", (event) => {
        const target = event.target;
        if (!(target instanceof HTMLElement)) {
            return;
        }

        if (target.hasAttribute("data-lic-close")) {
            closeUploadModal();
        } else if (target.hasAttribute("data-lic-trm-close")) {
            closeTrmModal();
        } else if (target.hasAttribute("data-lic-contract-close")) {
            closeContractModal();
        }
    });

    document.addEventListener("keydown", (event) => {
        if (event.key !== "Escape" || state.busy) {
            return;
        }

        if (uploadModal && !uploadModal.hidden) {
            closeUploadModal();
        } else if (trmModal && !trmModal.hidden) {
            closeTrmModal();
        } else if (contractModal && !contractModal.hidden) {
            closeContractModal();
        }
    });

    loadBoard();

    async function loadBoard() {
        try {
            setBusy(true);
            showStatus(statusBanner, "info", "Cargando consumos...");
            state.board = await fetchJson(loadUrl);
            state.contractTypeOptions = Array.isArray(state.board?.contractTypeOptions) ? state.board.contractTypeOptions : [];
            trimSelectionToCurrentRows();
            renderBoard();
            clearStatus(statusBanner);
        } catch (error) {
            showStatus(statusBanner, "error", getErrorMessage(error));
        } finally {
            setBusy(false);
        }
    }

    function renderBoard() {
        const records = getRecords();
        totalCount.textContent = numberFormatter.format(Number(state.board?.totalCount || records.length || 0));
        totalUsd.textContent = usdFormatter.format(Number(state.board?.totalUsd || 0));
        totalCop.textContent = copFormatter.format(Number(state.board?.totalCop || 0));
        emptyState.hidden = records.length > 0;

        rowsBody.innerHTML = records.map((row) => {
            const checked = state.selectedIds.has(row.recordId);
            const trClass = checked ? " class=\"is-selected\"" : "";
            return `
                <tr${trClass}>
                    <td class="lic-check-cell" data-label="Seleccionar">
                        <input class="form-check-input" type="checkbox" data-lic-row-check data-record-id="${escapeHtml(row.recordId)}" ${checked ? "checked" : ""} aria-label="Seleccionar fila" />
                    </td>
                    <td data-label="Cliente">${escapeHtml(row.nombreCliente)}</td>
                    <td data-label="Cuenta">
                        <div>${escapeHtml(row.companyAccountDisplay || row.companyAccountId || "Sin cuenta")}</div>
                        ${row.hasAccountLookup ? "" : "<small class=\"lic-muted\">Sin lookup</small>"}
                    </td>
                    <td data-label="Vendor">${escapeHtml(row.vendor)}</td>
                    <td data-label="Producto">
                        <div>${escapeHtml(row.productDisplay || "Sin producto")}</div>
                        ${row.hasProductLookup ? "" : "<small class=\"lic-muted\">Sin lookup</small>"}
                    </td>
                    <td data-label="Factura">${escapeHtml(row.facturaDisplay || row.facturaValue)}</td>
                    <td class="text-end" data-label="USD">${usdFormatter.format(Number(row.valorTotalUsd || 0))}</td>
                    <td class="text-end" data-label="TRM">${Number(row.trm || 0) > 0 ? numberFormatter.format(Number(row.trm)) : "-"}</td>
                    <td class="text-end" data-label="COP">${Number(row.pesosTotal || 0) > 0 ? copFormatter.format(Number(row.pesosTotal)) : "-"}</td>
                    <td data-label="Tipo"><span class="lic-badge">${escapeHtml(row.contractTypeLabel || "Sin tipo")}</span></td>
                </tr>`;
        }).join("");

        renderSelectionState();
    }

    function renderSelectionState() {
        const records = getRecords();
        const selected = records.filter((row) => state.selectedIds.has(row.recordId)).length;
        selectedCount.textContent = `${selected} seleccionado${selected === 1 ? "" : "s"}`;

        if (selectAll) {
            selectAll.checked = records.length > 0 && selected === records.length;
            selectAll.indeterminate = selected > 0 && selected < records.length;
        }

        if (contractBtn) {
            contractBtn.disabled = state.busy || selected === 0;
        }
    }

    function toggleSelectAll() {
        const records = getRecords();
        if (selectAll.checked) {
            records.forEach((row) => state.selectedIds.add(row.recordId));
        } else {
            records.forEach((row) => state.selectedIds.delete(row.recordId));
        }
        renderBoard();
    }

    function trimSelectionToCurrentRows() {
        const available = new Set(getRecords().map((row) => row.recordId));
        Array.from(state.selectedIds).forEach((recordId) => {
            if (!available.has(recordId)) {
                state.selectedIds.delete(recordId);
            }
        });
    }

    function openUploadModal() {
        state.previewRows = [];
        if (fileInput) {
            fileInput.value = "";
        }
        renderPreview();
        clearStatus(uploadStatus);
        uploadModal.hidden = false;
        fileInput?.focus();
    }

    function closeUploadModal() {
        uploadModal.hidden = true;
    }

    async function previewExcel() {
        const file = fileInput?.files?.[0];
        if (!file) {
            showStatus(uploadStatus, "warning", "Selecciona un archivo de Excel.");
            return;
        }

        try {
            setBusy(true);
            previewBtn.disabled = true;
            importBtn.disabled = true;
            showStatus(uploadStatus, "info", "Preparando vista previa...");

            const formData = new FormData();
            formData.append("file", file);
            const result = await fetchJson(previewUrl, {
                method: "POST",
                body: formData
            });

            state.previewRows = Array.isArray(result.rows) ? result.rows : [];
            state.contractTypeOptions = Array.isArray(result.contractTypeOptions)
                ? result.contractTypeOptions
                : state.contractTypeOptions;
            renderPreview(result);
            showStatus(uploadStatus, state.previewRows.some((row) => !row.isValid) ? "warning" : "success", result.message || "Vista previa lista.");
        } catch (error) {
            state.previewRows = [];
            renderPreview();
            showStatus(uploadStatus, "error", getErrorMessage(error));
        } finally {
            previewBtn.disabled = false;
            setBusy(false);
        }
    }

    function renderPreview(result) {
        const rows = state.previewRows;
        previewSummary.hidden = rows.length === 0;
        previewWrap.hidden = rows.length === 0;
        previewRowsCount.textContent = numberFormatter.format(Number(result?.totalRows || rows.length || 0));
        previewValidCount.textContent = numberFormatter.format(Number(result?.validRows || rows.filter((row) => row.isValid).length || 0));
        previewTotalUsd.textContent = usdFormatter.format(Number(result?.totalUsd || rows.reduce((sum, row) => sum + Number(row.valorTotalUsd || 0), 0)));
        importBtn.disabled = rows.length === 0 || rows.some((row) => !row.isValid);

        previewBody.innerHTML = rows.map((row, index) => {
            const messages = []
                .concat(Array.isArray(row.errors) ? row.errors : [])
                .concat(Array.isArray(row.warnings) ? row.warnings : []);
            const badgeClass = row.isValid
                ? (messages.length > 0 ? "is-warning" : "is-good")
                : "is-danger";
            const statusText = row.isValid
                ? (messages.length > 0 ? messages.join(" | ") : "Lista")
                : messages.join(" | ");

            return `
                <tr>
                    <td data-label="Fila">${numberFormatter.format(Number(row.sourceRowNumber || 0))}</td>
                    <td data-label="Cliente">${escapeHtml(row.nombreCliente)}</td>
                    <td data-label="Cuenta">
                        <div>${escapeHtml(row.companyAccountId || "Sin cuenta")}</div>
                        ${row.companyAccountLookupFound ? "<small class=\"lic-muted\">Lookup encontrado</small>" : "<small class=\"lic-muted\">Sin lookup</small>"}
                    </td>
                    <td data-label="Vendor">${escapeHtml(row.vendor)}</td>
                    <td data-label="Producto">
                        <div>${escapeHtml(row.productDescription)}</div>
                        ${row.productLookupFound ? "<small class=\"lic-muted\">Lookup encontrado</small>" : "<small class=\"lic-muted\">Sin lookup</small>"}
                    </td>
                    <td data-label="Factura">${escapeHtml(row.facturaDisplay || row.facturaValue)}</td>
                    <td class="text-end" data-label="USD">${usdFormatter.format(Number(row.valorTotalUsd || 0))}</td>
                    <td data-label="Tipo">
                        <select class="form-select form-select-sm" data-preview-contract="${index}">
                            ${renderContractOptions(row.contractTypeValue)}
                        </select>
                    </td>
                    <td data-label="Estado"><span class="lic-badge ${badgeClass}">${escapeHtml(statusText || "Error")}</span></td>
                </tr>`;
        }).join("");
    }

    async function importPreviewRows() {
        if (state.previewRows.length === 0 || state.previewRows.some((row) => !row.isValid)) {
            showStatus(uploadStatus, "warning", "La vista previa tiene filas pendientes.");
            return;
        }

        try {
            setBusy(true);
            importBtn.disabled = true;
            showStatus(uploadStatus, "info", "Procesando en Dataverse...");
            const result = await fetchJson(importUrl, {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify({ rows: state.previewRows })
            });

            closeUploadModal();
            await loadBoard();
            showStatus(statusBanner, "success", result.message || "Consumos cargados.");
        } catch (error) {
            showStatus(uploadStatus, "error", getErrorMessage(error));
        } finally {
            importBtn.disabled = state.previewRows.length === 0 || state.previewRows.some((row) => !row.isValid);
            setBusy(false);
        }
    }

    function openTrmModal() {
        renderFacturaOptions();
        if (trmInput) {
            trmInput.value = "";
        }
        clearStatus(trmStatus);
        trmModal.hidden = false;
        facturaSelect?.focus();
    }

    function closeTrmModal() {
        trmModal.hidden = true;
    }

    function renderFacturaOptions() {
        const options = Array.isArray(state.board?.facturaOptions) ? state.board.facturaOptions : [];
        facturaSelect.innerHTML = options.length === 0
            ? "<option value=\"\">Sin facturas</option>"
            : options.map((option) => `
                <option value="${escapeHtml(option.value)}">${escapeHtml(option.label)} (${numberFormatter.format(Number(option.count || 0))})</option>
            `).join("");
        facturaSelect.disabled = options.length === 0;
        trmSaveBtn.disabled = options.length === 0;
    }

    async function adjustTrm() {
        const facturaValue = facturaSelect?.value || "";
        const trm = Number(trmInput?.value || 0);
        if (!facturaValue || !Number.isFinite(trm) || trm <= 0) {
            showStatus(trmStatus, "warning", "Indica factura y TRM.");
            return;
        }

        try {
            setBusy(true);
            trmSaveBtn.disabled = true;
            showStatus(trmStatus, "info", "Calculando pesos...");
            const result = await fetchJson(adjustTrmUrl, {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify({ facturaValue, trm })
            });

            closeTrmModal();
            await loadBoard();
            showStatus(statusBanner, "success", result.message || "TRM actualizada.");
        } catch (error) {
            showStatus(trmStatus, "error", getErrorMessage(error));
        } finally {
            trmSaveBtn.disabled = false;
            setBusy(false);
        }
    }

    function openContractModal() {
        const selected = getSelectedRecordIds();
        if (selected.length === 0) {
            showStatus(statusBanner, "warning", "Selecciona al menos una fila.");
            return;
        }

        contractSelect.innerHTML = state.contractTypeOptions.map((option) => `
            <option value="${Number(option.value)}">${escapeHtml(option.label)}</option>
        `).join("");
        clearStatus(contractStatus);
        contractModal.hidden = false;
        contractSelect?.focus();
    }

    function closeContractModal() {
        contractModal.hidden = true;
    }

    async function updateContractType() {
        const recordIds = getSelectedRecordIds();
        const contractTypeValue = Number.parseInt(contractSelect?.value || "0", 10);
        if (recordIds.length === 0 || !Number.isInteger(contractTypeValue)) {
            showStatus(contractStatus, "warning", "Selecciona filas y tipo de contrato.");
            return;
        }

        try {
            setBusy(true);
            contractSaveBtn.disabled = true;
            showStatus(contractStatus, "info", "Actualizando tipo de contrato...");
            const result = await fetchJson(updateContractUrl, {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify({ recordIds, contractTypeValue })
            });

            closeContractModal();
            state.selectedIds.clear();
            await loadBoard();
            showStatus(statusBanner, "success", result.message || "Tipo de contrato actualizado.");
        } catch (error) {
            showStatus(contractStatus, "error", getErrorMessage(error));
        } finally {
            contractSaveBtn.disabled = false;
            setBusy(false);
        }
    }

    function renderContractOptions(selectedValue) {
        const options = state.contractTypeOptions.length > 0
            ? state.contractTypeOptions
            : [
                { value: 645250000, label: "Monthly" },
                { value: 645250001, label: "Onetime" },
                { value: 645250002, label: "Prepaid" }
            ];

        return options.map((option) => {
            const selected = Number(option.value) === Number(selectedValue) ? "selected" : "";
            return `<option value="${Number(option.value)}" ${selected}>${escapeHtml(option.label)}</option>`;
        }).join("");
    }

    function getRecords() {
        return Array.isArray(state.board?.records) ? state.board.records : [];
    }

    function getSelectedRecordIds() {
        const available = new Set(getRecords().map((row) => row.recordId));
        return Array.from(state.selectedIds).filter((recordId) => available.has(recordId));
    }

    function setBusy(value) {
        state.busy = value;
        refreshBtn.disabled = value;
        newBtn.disabled = value;
        trmBtn.disabled = value;
        renderSelectionState();
    }

    async function fetchJson(url, options) {
        const response = await fetch(url, {
            ...(options || {}),
            headers: {
                Accept: "application/json",
                ...(options?.headers || {})
            }
        });
        const payload = await readPayload(response);
        if (!response.ok) {
            throw createResponseError(payload);
        }
        return payload;
    }

    async function readPayload(response) {
        const contentType = response.headers.get("content-type") || "";
        if (contentType.includes("application/json")) {
            return await response.json();
        }
        return { message: await response.text() };
    }

    function createResponseError(payload) {
        const error = new Error(payload?.detail || payload?.message || "Ocurrio un error inesperado.");
        error.payload = payload;
        return error;
    }

    function getErrorMessage(error) {
        return error instanceof Error ? error.message : "Ocurrio un error inesperado.";
    }

    function showStatus(element, tone, message) {
        if (!element) {
            return;
        }

        element.textContent = message || "";
        element.className = `lic-status is-visible ${tone ? "is-" + tone : ""}`;
    }

    function clearStatus(element) {
        if (!element) {
            return;
        }

        element.textContent = "";
        element.className = "lic-status";
    }

    function escapeHtml(value) {
        return (value ?? "").toString()
            .replace(/&/g, "&amp;")
            .replace(/</g, "&lt;")
            .replace(/>/g, "&gt;")
            .replace(/"/g, "&quot;")
            .replace(/'/g, "&#39;");
    }
})();
