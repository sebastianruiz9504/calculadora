(function () {
    const app = document.getElementById("licenciamientoApp");
    if (!app) {
        return;
    }

    const loadUrl = app.dataset.loadUrl || "";
    const previewUrl = app.dataset.previewUrl || "";
    const productSearchUrl = app.dataset.productSearchUrl || "";
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
    const previewHiddenCount = document.getElementById("licPreviewHiddenCount");
    const previewTotalUsd = document.getElementById("licPreviewTotalUsd");
    const previewWrap = document.getElementById("licPreviewWrap");
    const previewAccountSection = document.getElementById("licPreviewAccountSection");
    const previewAccountCount = document.getElementById("licPreviewAccountCount");
    const previewAccountBody = document.getElementById("licPreviewAccountBody");
    const previewProductSection = document.getElementById("licPreviewProductSection");
    const previewProductCount = document.getElementById("licPreviewProductCount");
    const previewBody = document.getElementById("licPreviewBody");
    const previewDataSection = document.getElementById("licPreviewDataSection");
    const previewDataCount = document.getElementById("licPreviewDataCount");
    const previewDataBody = document.getElementById("licPreviewDataBody");
    const previewClean = document.getElementById("licPreviewClean");

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
        previewResult: null,
        contractTypeOptions: [],
        productLookupTimers: new Map(),
        productLookupRequests: new Map(),
        productLookupRequestSeq: 0
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

    document.addEventListener("input", (event) => {
        const target = event.target;
        if (!(target instanceof HTMLInputElement) || !target.matches("[data-preview-product-search]")) {
            return;
        }

        handlePreviewProductInput(target);
    });

    document.addEventListener("focusin", (event) => {
        const target = event.target;
        if (!(target instanceof HTMLInputElement) || !target.matches("[data-preview-product-search]")) {
            return;
        }

        if (target.value.trim().length >= 2) {
            openPreviewProductMenu(target);
        }
    });

    document.addEventListener("click", (event) => {
        const target = event.target;
        if (!(target instanceof HTMLElement)) {
            return;
        }

        const productOption = target.closest("[data-preview-product-option]");
        if (productOption instanceof HTMLElement) {
            selectPreviewProductOption(productOption);
            return;
        }

        if (!target.closest(".lic-lookup")) {
            closeProductLookupMenus();
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
        state.previewResult = null;
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
            state.previewResult = result;
            state.contractTypeOptions = Array.isArray(result.contractTypeOptions)
                ? result.contractTypeOptions
                : state.contractTypeOptions;
            renderPreview(result);
            const hasPreviewIssues = state.previewRows.some((row) => !row.isValid || hasAccountLookupIssue(row) || shouldSkipPreviewRow(row));
            showStatus(uploadStatus, hasPreviewIssues ? "warning" : "success", result.message || "Vista previa lista.");
        } catch (error) {
            state.previewRows = [];
            state.previewResult = null;
            renderPreview();
            showStatus(uploadStatus, "error", getErrorMessage(error));
        } finally {
            previewBtn.disabled = false;
            setBusy(false);
        }
    }

    function renderPreview(result) {
        const rows = state.previewRows;
        const summary = result || state.previewResult || {};
        const accountGroups = buildAccountIssueGroups(rows);
        const productRows = getProductIssueRows(rows);
        const dataRows = getDataIssueRows(rows);
        const hiddenRows = rows.filter((row) => isPreviewRowReadyToHide(row)).length;

        previewSummary.hidden = rows.length === 0;
        previewWrap.hidden = rows.length === 0;
        previewRowsCount.textContent = numberFormatter.format(Number(summary?.totalRows || rows.length || 0));
        previewValidCount.textContent = numberFormatter.format(Number(summary?.validRows || rows.filter((row) => row.isValid).length || 0));
        previewHiddenCount.textContent = numberFormatter.format(hiddenRows);
        previewTotalUsd.textContent = usdFormatter.format(Number(summary?.totalUsd || rows.reduce((sum, row) => sum + Number(row.valorTotalUsd || 0), 0)));
        updatePreviewImportState();

        if (previewAccountSection) {
            previewAccountSection.hidden = accountGroups.length === 0;
        }

        if (previewAccountCount) {
            previewAccountCount.textContent = `${numberFormatter.format(accountGroups.length)} grupo${accountGroups.length === 1 ? "" : "s"}`;
        }

        if (previewAccountBody) {
            previewAccountBody.innerHTML = accountGroups.map((group) => `
                <tr>
                    <td data-label="Account ID">
                        <div>${escapeHtml(group.accountId || "Sin cuenta")}</div>
                        <small class="lic-muted">${numberFormatter.format(group.count)} fila${group.count === 1 ? "" : "s"}</small>
                    </td>
                    <td data-label="Filas">${escapeHtml(group.sourceRows.join(", "))}</td>
                    <td data-label="Clientes">${escapeHtml(group.clients.join(", ") || "Sin cliente")}</td>
                    <td data-label="Motivo"><span class="lic-lookup-note is-warning">${escapeHtml(group.reason || "Sin lookup")}</span></td>
                </tr>
            `).join("");
        }

        if (previewProductSection) {
            previewProductSection.hidden = productRows.length === 0;
        }

        if (previewProductCount) {
            previewProductCount.textContent = `${numberFormatter.format(productRows.length)} fila${productRows.length === 1 ? "" : "s"}`;
        }

        previewBody.innerHTML = productRows.map(({ row, index }) => {
            const messages = getPreviewMessages(row);
            const badgeClass = row.isValid
                ? (messages.length > 0 ? "is-warning" : "is-good")
                : "is-danger";
            const statusText = row.isValid
                ? (messages.length > 0 ? messages.join(" | ") : "Lista")
                : messages.join(" | ");

            return `
                <tr data-preview-index="${index}">
                    <td data-label="Fila">${numberFormatter.format(Number(row.sourceRowNumber || 0))}</td>
                    <td data-label="Cliente">${escapeHtml(row.nombreCliente)}</td>
                    <td data-label="Cuenta">
                        <div>${escapeHtml(row.companyAccountId || "Sin cuenta")}</div>
                        ${renderLookupHelper(row.companyAccountLookupFound, row.companyAccountLookupRequired, row.companyAccountLookupLabel, row.companyAccountLookupFailureReason)}
                    </td>
                    <td data-label="Vendor">${escapeHtml(row.vendor)}</td>
                    <td data-label="Producto">
                        ${renderPreviewProductCell(row, index)}
                    </td>
                    <td data-label="Factura">${escapeHtml(row.facturaDisplay || row.facturaValue)}</td>
                    <td class="text-end" data-label="USD">${usdFormatter.format(Number(row.valorTotalUsd || 0))}</td>
                    <td data-label="Tipo">
                        <select class="form-select form-select-sm" data-preview-contract="${index}">
                            ${renderContractOptions(row.contractTypeValue)}
                        </select>
                    </td>
                    <td data-label="Estado"><span class="lic-badge ${badgeClass}" data-preview-status="${index}">${escapeHtml(statusText || "Error")}</span></td>
                </tr>`;
        }).join("");

        if (previewDataSection) {
            previewDataSection.hidden = dataRows.length === 0;
        }

        if (previewDataCount) {
            previewDataCount.textContent = `${numberFormatter.format(dataRows.length)} fila${dataRows.length === 1 ? "" : "s"}`;
        }

        if (previewDataBody) {
            previewDataBody.innerHTML = dataRows.map(({ row }) => {
                const messages = getPreviewMessages(row);
                return `
                    <tr>
                        <td data-label="Fila">${numberFormatter.format(Number(row.sourceRowNumber || 0))}</td>
                        <td data-label="Cliente">${escapeHtml(row.nombreCliente)}</td>
                        <td data-label="Cuenta">${escapeHtml(row.companyAccountId || "Sin cuenta")}</td>
                        <td data-label="Producto">${escapeHtml(row.productDescription || "Sin producto")}</td>
                        <td data-label="Estado"><span class="lic-badge is-danger">${escapeHtml(messages.join(" | ") || "Error")}</span></td>
                    </tr>`;
            }).join("");
        }

        if (previewClean) {
            previewClean.hidden = rows.length === 0 || accountGroups.length > 0 || productRows.length > 0 || dataRows.length > 0;
        }
    }

    function buildAccountIssueGroups(rows) {
        const groups = new Map();
        rows.forEach((row) => {
            if (!hasAccountLookupIssue(row)) {
                return;
            }

            const key = normalizeLookupGroupKey(row.companyAccountId);
            if (!groups.has(key)) {
                groups.set(key, {
                    accountId: (row.companyAccountId || "").trim(),
                    count: 0,
                    sourceRows: [],
                    clients: [],
                    reason: row.companyAccountLookupFailureReason || ""
                });
            }

            const group = groups.get(key);
            group.count += 1;
            if (row.sourceRowNumber) {
                group.sourceRows.push(numberFormatter.format(Number(row.sourceRowNumber)));
            }

            const client = (row.nombreCliente || "").trim();
            if (client && !group.clients.some((value) => value.toLowerCase() === client.toLowerCase())) {
                group.clients.push(client);
            }

            if (!group.reason && row.companyAccountLookupFailureReason) {
                group.reason = row.companyAccountLookupFailureReason;
            }
        });

        return Array.from(groups.values())
            .sort((left, right) => left.accountId.localeCompare(right.accountId, "es", { sensitivity: "base" }));
    }

    function getProductIssueRows(rows) {
        return rows
            .map((row, index) => ({ row, index }))
            .filter((item) => shouldSkipPreviewRow(item.row));
    }

    function getDataIssueRows(rows) {
        return rows
            .map((row, index) => ({ row, index }))
            .filter((item) => !item.row?.isValid && !shouldSkipPreviewRow(item.row));
    }

    function isPreviewRowReadyToHide(row) {
        return Boolean(row?.isValid)
            && !hasAccountLookupIssue(row)
            && !shouldSkipPreviewRow(row);
    }

    function hasAccountLookupIssue(row) {
        return Boolean(row?.companyAccountLookupRequired)
            && !(row.companyAccountLookupId || "").trim()
            && !row.companyAccountLookupFound;
    }

    function normalizeLookupGroupKey(value) {
        return (value || "").trim().toLowerCase() || "__empty__";
    }

    function renderPreviewProductCell(row, index) {
        if (!row.productLookupRequired) {
            return `
                <div>${escapeHtml(row.productDescription)}</div>
                <small class="lic-muted">Producto de texto</small>`;
        }

        const value = row.productLookupLabel || row.productDescription || "";
        const helperClass = row.productLookupId ? "lic-muted" : "lic-lookup-note is-warning";
        const helperText = row.productLookupId
            ? `Lookup encontrado${row.productLookupLabel ? ": " + row.productLookupLabel : ""}`
            : (row.productLookupFailureReason || "Sin lookup de producto. Esta fila se omitira al procesar si no seleccionas uno.");

        return `
            <div class="lic-lookup">
                <input class="form-control form-control-sm lic-lookup-input"
                       type="search"
                       value="${escapeHtml(value)}"
                       placeholder="Buscar producto..."
                       autocomplete="off"
                       data-preview-product-search="${index}" />
                <div class="lic-lookup-menu" data-preview-product-menu="${index}"></div>
            </div>
            <small class="${helperClass}" data-preview-product-helper="${index}">${escapeHtml(helperText)}</small>`;
    }

    function renderLookupHelper(found, required, label, failureReason) {
        if (!required) {
            return "<small class=\"lic-muted\">No requiere lookup</small>";
        }

        if (found) {
            const suffix = label ? `: ${escapeHtml(label)}` : "";
            return `<small class="lic-muted">Lookup encontrado${suffix}</small>`;
        }

        return `<small class="lic-lookup-note is-warning">${escapeHtml(failureReason || "Sin lookup")}</small>`;
    }

    function getPreviewMessages(row) {
        const messages = []
            .concat(Array.isArray(row.errors) ? row.errors : [])
            .concat(Array.isArray(row.warnings) ? row.warnings : []);

        if (shouldSkipPreviewRow(row)) {
            messages.push(row.productLookupFailureReason || "Se omitira al procesar porque no tiene lookup de producto.");
        }

        return Array.from(new Set(messages.filter(Boolean)));
    }

    function shouldSkipPreviewRow(row) {
        return Boolean(row?.productLookupRequired && !(row.productLookupId || "").trim());
    }

    function getImportablePreviewRows() {
        return state.previewRows.filter((row) => row.isValid && !shouldSkipPreviewRow(row));
    }

    function updatePreviewImportState() {
        if (!importBtn) {
            return;
        }

        importBtn.disabled = state.previewRows.length === 0
            || state.previewRows.some((row) => !row.isValid)
            || getImportablePreviewRows().length === 0;
    }

    function refreshPreviewRowDecorations(index) {
        const row = state.previewRows[index];
        if (!row) {
            return;
        }

        const helper = previewBody.querySelector(`[data-preview-product-helper="${index}"]`);
        if (helper) {
            helper.className = row.productLookupId ? "lic-muted" : "lic-lookup-note is-warning";
            helper.textContent = row.productLookupId
                ? `Lookup encontrado${row.productLookupLabel ? ": " + row.productLookupLabel : ""}`
                : (row.productLookupFailureReason || "Sin lookup de producto. Esta fila se omitira al procesar si no seleccionas uno.");
        }

        const status = previewBody.querySelector(`[data-preview-status="${index}"]`);
        if (status) {
            const messages = getPreviewMessages(row);
            status.className = `lic-badge ${row.isValid ? (messages.length > 0 ? "is-warning" : "is-good") : "is-danger"}`;
            status.textContent = row.isValid
                ? (messages.length > 0 ? messages.join(" | ") : "Lista")
                : (messages.join(" | ") || "Error");
        }

        updatePreviewImportState();
    }

    function handlePreviewProductInput(input) {
        const index = Number.parseInt(input.getAttribute("data-preview-product-search") || "-1", 10);
        const row = state.previewRows[index];
        if (!row) {
            return;
        }

        const query = input.value.trim();
        row.productDescription = query;
        row.productLookupId = "";
        row.productLookupLabel = "";
        row.productLookupFound = false;
        row.productLookupFailureReason = query.length < 2
            ? "Escribe al menos 2 caracteres para buscar en cr07a_precioscloud."
            : "";
        removeProductLookupWarnings(row);
        refreshPreviewRowDecorations(index);

        if (query.length < 2 || !productSearchUrl) {
            hideProductLookupMenu(index);
            return;
        }

        schedulePreviewProductSearch(index, query, 280);
    }

    function openPreviewProductMenu(input) {
        const index = Number.parseInt(input.getAttribute("data-preview-product-search") || "-1", 10);
        const query = input.value.trim();
        if (index < 0 || query.length < 2 || !productSearchUrl) {
            return;
        }

        schedulePreviewProductSearch(index, query, 0);
    }

    function schedulePreviewProductSearch(index, query, delay) {
        const previousTimer = state.productLookupTimers.get(index);
        if (previousTimer) {
            window.clearTimeout(previousTimer);
        }

        const timer = window.setTimeout(() => searchPreviewProduct(index, query), delay);
        state.productLookupTimers.set(index, timer);
    }

    async function searchPreviewProduct(index, query) {
        const menu = getProductLookupMenu(index);
        if (!menu) {
            return;
        }

        const requestId = ++state.productLookupRequestSeq;
        state.productLookupRequests.set(index, requestId);
        menu.innerHTML = "<div class=\"lic-lookup-empty\">Buscando...</div>";
        menu.classList.add("is-open");

        try {
            const items = await fetchJson(buildProductSearchUrl(query));
            if (state.productLookupRequests.get(index) !== requestId) {
                return;
            }

            if (!Array.isArray(items) || items.length === 0) {
                const row = state.previewRows[index];
                if (row) {
                    row.productLookupFailureReason = `No se encontraron productos en cr07a_precioscloud para "${query}".`;
                    refreshPreviewRowDecorations(index);
                }

                menu.innerHTML = "<div class=\"lic-lookup-empty\">Sin resultados</div>";
                menu.classList.add("is-open");
                return;
            }

            menu.innerHTML = items.map((item) => `
                <button type="button"
                        class="lic-lookup-option"
                        data-preview-product-option
                        data-preview-index="${index}"
                        data-id="${escapeHtml(item.id || "")}"
                        data-label="${escapeHtml(item.label || "")}">
                    <span>${escapeHtml(item.label || "Producto sin nombre")}</span>
                    <small>${escapeHtml(item.matchedValue || item.searchField || "")}</small>
                </button>
            `).join("");
            menu.classList.add("is-open");
        } catch (error) {
            if (state.productLookupRequests.get(index) !== requestId) {
                return;
            }

            const row = state.previewRows[index];
            if (row) {
                row.productLookupFailureReason = getErrorMessage(error);
                refreshPreviewRowDecorations(index);
            }

            menu.innerHTML = "<div class=\"lic-lookup-empty\">No se pudo buscar</div>";
            menu.classList.add("is-open");
        }
    }

    function selectPreviewProductOption(option) {
        const index = Number.parseInt(option.getAttribute("data-preview-index") || "-1", 10);
        const row = state.previewRows[index];
        if (!row) {
            return;
        }

        row.productLookupId = option.getAttribute("data-id") || "";
        row.productLookupLabel = option.getAttribute("data-label") || "";
        row.productDescription = row.productLookupLabel || row.productDescription;
        row.productLookupFound = Boolean(row.productLookupId);
        row.productLookupFailureReason = "";
        removeProductLookupWarnings(row);

        const input = previewBody.querySelector(`[data-preview-product-search="${index}"]`);
        if (input instanceof HTMLInputElement) {
            input.value = row.productLookupLabel || row.productDescription;
        }

        hideProductLookupMenu(index);
        renderPreview();
    }

    function removeProductLookupWarnings(row) {
        if (!Array.isArray(row.warnings)) {
            row.warnings = [];
            return;
        }

        row.warnings = row.warnings.filter((message) => {
            const text = (message || "").toString().toLowerCase();
            return !text.includes("priceableitem")
                && !text.includes("lookup de producto")
                && !text.includes("cr07a_precioscloud")
                && !text.includes("producto en la vista previa");
        });
    }

    function buildProductSearchUrl(query) {
        const separator = productSearchUrl.includes("?") ? "&" : "?";
        return `${productSearchUrl}${separator}q=${encodeURIComponent(query)}&top=8`;
    }

    function getProductLookupMenu(index) {
        return previewBody.querySelector(`[data-preview-product-menu="${index}"]`);
    }

    function hideProductLookupMenu(index) {
        const menu = getProductLookupMenu(index);
        if (!menu) {
            return;
        }

        menu.classList.remove("is-open");
        menu.innerHTML = "";
    }

    function closeProductLookupMenus() {
        previewBody.querySelectorAll(".lic-lookup-menu.is-open").forEach((menu) => {
            menu.classList.remove("is-open");
            menu.innerHTML = "";
        });
    }

    async function importPreviewRows() {
        if (state.previewRows.length === 0 || state.previewRows.some((row) => !row.isValid)) {
            showStatus(uploadStatus, "warning", "La vista previa tiene filas pendientes.");
            return;
        }

        const importableRows = getImportablePreviewRows();
        if (importableRows.length === 0) {
            showStatus(uploadStatus, "warning", "Selecciona al menos un producto con lookup antes de procesar.");
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
            updatePreviewImportState();
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
