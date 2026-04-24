(function () {
    const app = document.getElementById("licenciamientoApp");
    if (!app) {
        return;
    }

    const loadUrl = app.dataset.loadUrl || "";
    const previewUrl = app.dataset.previewUrl || "";
    const accountSearchUrl = app.dataset.accountSearchUrl || "";
    const productSearchUrl = app.dataset.productSearchUrl || "";
    const importUrl = app.dataset.importUrl || "";
    const adjustTrmUrl = app.dataset.adjustTrmUrl || "";
    const updateContractUrl = app.dataset.updateContractUrl || "";
    const breakdownProductName = "Acronis Cyber Cloud Commitment (SPLA) Manual Provisioning - One Time Setup Fee";

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

    const breakdownModal = document.getElementById("licBreakdownModal");
    const breakdownStatus = document.getElementById("licBreakdownStatus");
    const breakdownProduct = document.getElementById("licBreakdownProduct");
    const breakdownOriginalTotal = document.getElementById("licBreakdownOriginalTotal");
    const breakdownRemaining = document.getElementById("licBreakdownRemaining");
    const breakdownBody = document.getElementById("licBreakdownBody");
    const breakdownAddRowBtn = document.getElementById("licBreakdownAddRowBtn");
    const breakdownSaveBtn = document.getElementById("licBreakdownSaveBtn");

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
        productLookupRequestSeq: 0,
        breakdownSourceIndex: -1,
        breakdownDraftRows: [],
        breakdownDraftSeq: 0,
        breakdownLookupTimers: new Map(),
        breakdownLookupRequests: new Map(),
        breakdownLookupRequestSeq: 0
    };

    refreshBtn?.addEventListener("click", loadBoard);
    newBtn?.addEventListener("click", openUploadModal);
    trmBtn?.addEventListener("click", openTrmModal);
    contractBtn?.addEventListener("click", openContractModal);
    selectAll?.addEventListener("change", toggleSelectAll);
    importBtn?.addEventListener("click", importPreviewRows);
    breakdownAddRowBtn?.addEventListener("click", () => {
        addBreakdownDraftRow();
        renderBreakdownModal();
    });
    breakdownSaveBtn?.addEventListener("click", saveBreakdownRows);

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
        if (!(target instanceof HTMLInputElement)) {
            return;
        }

        if (target.matches("[data-preview-product-search]")) {
            handlePreviewProductInput(target);
            return;
        }

        if (target.matches("[data-breakdown-client-search]")) {
            handleBreakdownLookupInput(target, "client");
            return;
        }

        if (target.matches("[data-breakdown-product-search]")) {
            handleBreakdownLookupInput(target, "product");
            return;
        }

        if (target.matches("[data-breakdown-value]")) {
            handleBreakdownValueInput(target);
        }
    });

    document.addEventListener("focusin", (event) => {
        const target = event.target;
        if (!(target instanceof HTMLInputElement)) {
            return;
        }

        if (target.matches("[data-preview-product-search]") && target.value.trim().length >= 2) {
            openPreviewProductMenu(target);
            return;
        }

        if (target.matches("[data-breakdown-client-search]") && target.value.trim().length >= 2) {
            openBreakdownLookupMenu(target, "client");
            return;
        }

        if (target.matches("[data-breakdown-product-search]") && target.value.trim().length >= 2) {
            openBreakdownLookupMenu(target, "product");
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

        const breakdownClientOption = target.closest("[data-breakdown-client-option]");
        if (breakdownClientOption instanceof HTMLElement) {
            selectBreakdownLookupOption(breakdownClientOption, "client");
            return;
        }

        const breakdownProductOption = target.closest("[data-breakdown-product-option]");
        if (breakdownProductOption instanceof HTMLElement) {
            selectBreakdownLookupOption(breakdownProductOption, "product");
            return;
        }

        if (!target.closest(".lic-lookup")) {
            closeProductLookupMenus();
            closeBreakdownLookupMenus();
        }

        if (target.hasAttribute("data-lic-close")) {
            closeUploadModal();
        } else if (target.hasAttribute("data-lic-breakdown-close")) {
            closeBreakdownModal();
        } else if (target.hasAttribute("data-lic-trm-close")) {
            closeTrmModal();
        } else if (target.hasAttribute("data-lic-contract-close")) {
            closeContractModal();
        } else if (target.hasAttribute("data-preview-breakdown")) {
            openBreakdownModal(Number.parseInt(target.getAttribute("data-preview-breakdown") || "-1", 10));
        } else if (target.hasAttribute("data-breakdown-remove")) {
            removeBreakdownDraftRow(target.getAttribute("data-breakdown-remove") || "");
            return;
        }
    });

    document.addEventListener("keydown", (event) => {
        if (event.key !== "Escape" || state.busy) {
            return;
        }

        if (breakdownModal && !breakdownModal.hidden) {
            closeBreakdownModal();
        } else if (uploadModal && !uploadModal.hidden) {
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
        resetBreakdownState();
        if (fileInput) {
            fileInput.value = "";
        }
        renderPreview();
        clearStatus(uploadStatus);
        uploadModal.hidden = false;
        fileInput?.focus();
    }

    function closeUploadModal() {
        closeBreakdownModal({ preserveStatus: false });
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
            const hasPreviewIssues = state.previewRows.some((row) => !row.isValid || hasAccountLookupIssue(row) || shouldSkipPreviewRow(row) || requiresBreakdown(row));
            showStatus(uploadStatus, hasPreviewIssues ? "warning" : "success", result.message || "Vista previa lista.");
        } catch (error) {
            state.previewRows = [];
            state.previewResult = null;
            resetBreakdownState();
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
        const useServerSummary = Boolean(result);
        const accountGroups = buildAccountIssueGroups(rows);
        const productRows = getProductIssueRows(rows);
        const dataRows = getDataIssueRows(rows);
        const hiddenRows = rows.filter((row) => isPreviewRowReadyToHide(row)).length;

        previewSummary.hidden = rows.length === 0;
        previewWrap.hidden = rows.length === 0;
        previewRowsCount.textContent = numberFormatter.format(Number(useServerSummary ? (summary?.totalRows || rows.length || 0) : rows.length || 0));
        previewValidCount.textContent = numberFormatter.format(Number(useServerSummary ? (summary?.validRows || rows.filter((row) => row.isValid && !requiresBreakdown(row)).length || 0) : rows.filter((row) => row.isValid && !requiresBreakdown(row)).length || 0));
        previewHiddenCount.textContent = numberFormatter.format(hiddenRows);
        previewTotalUsd.textContent = usdFormatter.format(Number(useServerSummary ? (summary?.totalUsd || rows.reduce((sum, row) => sum + Number(row.valorTotalUsd || 0), 0)) : rows.reduce((sum, row) => sum + Number(row.valorTotalUsd || 0), 0)));
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
            const actions = requiresBreakdown(row)
                ? `<button type="button" class="btn btn-outline-primary btn-sm" data-preview-breakdown="${index}">Desglosar</button>`
                : "<span class=\"lic-muted\">Resuelve el lookup</span>";

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
                    <td data-label="Acciones">${actions}</td>
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
            .filter((item) => shouldSkipPreviewRow(item.row) || requiresBreakdown(item.row));
    }

    function getDataIssueRows(rows) {
        return rows
            .map((row, index) => ({ row, index }))
            .filter((item) => !item.row?.isValid && !shouldSkipPreviewRow(item.row));
    }

    function isPreviewRowReadyToHide(row) {
        return Boolean(row?.isValid)
            && !hasAccountLookupIssue(row)
            && !shouldSkipPreviewRow(row)
            && !requiresBreakdown(row);
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
        if (requiresBreakdown(row)) {
            return `
                <div>${escapeHtml(row.productDescription || row.productLookupLabel || "Sin producto")}</div>
                <small class="lic-lookup-note is-warning">Este cargo debe desglosarse antes de procesar.</small>`;
        }

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

        if (requiresBreakdown(row)) {
            messages.push(`Desglosa ${usdFormatter.format(Number(row.valorTotalUsd || 0))} en clientes y productos antes de procesar.`);
        }

        if (shouldSkipPreviewRow(row)) {
            messages.push(row.productLookupFailureReason || "Se omitira al procesar porque no tiene lookup de producto.");
        }

        return Array.from(new Set(messages.filter(Boolean)));
    }

    function shouldSkipPreviewRow(row) {
        return Boolean(row?.productLookupRequired && !(row.productLookupId || "").trim());
    }

    function requiresBreakdown(row) {
        if (!row) {
            return false;
        }

        if (row.breakdownGenerated) {
            return false;
        }

        if (row.requiresBreakdown === true) {
            return true;
        }

        return normalizeBreakdownProduct(row.productDescription || row.productLookupLabel || "") === normalizeBreakdownProduct(breakdownProductName);
    }

    function getImportablePreviewRows() {
        return state.previewRows.filter((row) => row.isValid && !shouldSkipPreviewRow(row) && !requiresBreakdown(row));
    }

    function updatePreviewImportState() {
        if (!importBtn) {
            return;
        }

        importBtn.disabled = state.previewRows.length === 0
            || state.previewRows.some((row) => !row.isValid)
            || state.previewRows.some((row) => requiresBreakdown(row))
            || getImportablePreviewRows().length === 0;
    }

    function refreshPreviewRowDecorations(index) {
        const row = state.previewRows[index];
        if (!row) {
            return;
        }

        if (requiresBreakdown(row)) {
            updatePreviewImportState();
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
        if (!productSearchUrl) {
            return "";
        }

        const separator = productSearchUrl.includes("?") ? "&" : "?";
        return `${productSearchUrl}${separator}q=${encodeURIComponent(query)}&top=8`;
    }

    function buildAccountSearchUrl(query) {
        if (!accountSearchUrl) {
            return "";
        }

        const separator = accountSearchUrl.includes("?") ? "&" : "?";
        return `${accountSearchUrl}${separator}q=${encodeURIComponent(query)}&top=8`;
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

    function resetBreakdownState() {
        state.breakdownLookupTimers.forEach((timerId) => window.clearTimeout(timerId));
        state.breakdownLookupTimers.clear();
        state.breakdownLookupRequests.clear();
        state.breakdownSourceIndex = -1;
        state.breakdownDraftRows = [];
        clearStatus(breakdownStatus);
        if (breakdownBody) {
            breakdownBody.innerHTML = "";
        }
        if (breakdownProduct) {
            breakdownProduct.textContent = "-";
        }
        if (breakdownOriginalTotal) {
            breakdownOriginalTotal.textContent = usdFormatter.format(0);
        }
        if (breakdownRemaining) {
            breakdownRemaining.textContent = usdFormatter.format(0);
            breakdownRemaining.className = "lic-breakdown-total";
        }
    }

    function openBreakdownModal(index) {
        const sourceRow = state.previewRows[index];
        if (!sourceRow || !requiresBreakdown(sourceRow)) {
            return;
        }

        const shouldReuseDraft = state.breakdownSourceIndex === index && state.breakdownDraftRows.length > 0;
        if (!shouldReuseDraft) {
            resetBreakdownState();
            state.breakdownSourceIndex = index;
            state.breakdownDraftRows = [createBreakdownDraftRow(Number(sourceRow.valorTotalUsd || 0))];
        }

        clearStatus(breakdownStatus);
        renderBreakdownModal();
        breakdownModal.hidden = false;
        const firstInput = breakdownBody?.querySelector("[data-breakdown-client-search]");
        if (firstInput instanceof HTMLInputElement) {
            firstInput.focus();
        }
    }

    function closeBreakdownModal(options) {
        const preserveDraft = options?.preserveDraft ?? true;
        const preserveStatus = options?.preserveStatus ?? true;
        if (breakdownModal) {
            breakdownModal.hidden = true;
        }
        closeBreakdownLookupMenus();
        if (!preserveStatus) {
            clearStatus(breakdownStatus);
        }
        if (!preserveDraft) {
            resetBreakdownState();
        }
    }

    function renderBreakdownModal() {
        const sourceRow = getBreakdownOriginalRow();
        if (!sourceRow) {
            closeBreakdownModal({ preserveDraft: false, preserveStatus: false });
            return;
        }

        if (breakdownProduct) {
            breakdownProduct.textContent = sourceRow.productDescription || sourceRow.productLookupLabel || "Sin producto";
        }

        if (breakdownOriginalTotal) {
            breakdownOriginalTotal.textContent = usdFormatter.format(Number(sourceRow.valorTotalUsd || 0));
        }

        if (breakdownBody) {
            breakdownBody.innerHTML = state.breakdownDraftRows.length === 0
                ? `
                    <tr>
                        <td colspan="4" class="lic-breakdown-empty">Agrega al menos una fila para repartir el valor.</td>
                    </tr>`
                : state.breakdownDraftRows.map((draft) => `
                    <tr data-breakdown-id="${escapeHtml(draft.id)}">
                        <td data-label="Cliente">
                            ${renderBreakdownLookupControl(draft, "client")}
                        </td>
                        <td data-label="Producto">
                            ${renderBreakdownLookupControl(draft, "product")}
                        </td>
                        <td class="text-end" data-label="Valor USD">
                            <input class="form-control form-control-sm"
                                   type="number"
                                   min="0"
                                   step="0.01"
                                   inputmode="decimal"
                                   value="${escapeHtml(formatBreakdownInputValue(draft.value))}"
                                   data-breakdown-value
                                   data-breakdown-id="${escapeHtml(draft.id)}" />
                        </td>
                        <td data-label="Accion">
                            <button type="button" class="btn btn-outline-danger btn-sm" data-breakdown-remove="${escapeHtml(draft.id)}">Quitar</button>
                        </td>
                    </tr>
                `).join("");
        }

        refreshBreakdownModalState();
    }

    function renderBreakdownLookupControl(draft, kind) {
        const fieldLabel = kind === "client" ? "cliente" : "producto";
        const queryValue = kind === "client"
            ? (draft.clientLabel || draft.clientQuery || "")
            : (draft.productLabel || draft.productQuery || "");
        const helperClass = getBreakdownLookupSelectedValue(draft, kind)
            ? "lic-muted"
            : "lic-lookup-note is-warning";
        const helperText = getBreakdownLookupHelperText(draft, kind);

        return `
            <div class="lic-lookup">
                <input class="form-control form-control-sm lic-lookup-input"
                       type="search"
                       value="${escapeHtml(queryValue)}"
                       placeholder="Buscar ${escapeHtml(fieldLabel)}..."
                       autocomplete="off"
                       data-breakdown-${kind}-search
                       data-breakdown-id="${escapeHtml(draft.id)}" />
                <div class="lic-lookup-menu" data-breakdown-${kind}-menu="${escapeHtml(draft.id)}"></div>
            </div>
            <small class="${helperClass}" data-breakdown-${kind}-helper="${escapeHtml(draft.id)}">${escapeHtml(helperText)}</small>`;
    }

    function refreshBreakdownModalState() {
        const remaining = getBreakdownRemainingValue();
        if (breakdownRemaining) {
            breakdownRemaining.textContent = usdFormatter.format(remaining);
            breakdownRemaining.className = `lic-breakdown-total ${Math.abs(remaining) < 0.005 ? "is-zero" : "is-warning"}`;
        }

        if (breakdownSaveBtn) {
            breakdownSaveBtn.disabled = getBreakdownValidationMessage() !== "";
        }
    }

    function addBreakdownDraftRow(initialValue) {
        state.breakdownDraftRows.push(createBreakdownDraftRow(initialValue));
    }

    function removeBreakdownDraftRow(draftId) {
        state.breakdownDraftRows = state.breakdownDraftRows.filter((draft) => draft.id !== draftId);
        renderBreakdownModal();
    }

    function createBreakdownDraftRow(initialValue) {
        state.breakdownDraftSeq += 1;
        return {
            id: `draft-${state.breakdownDraftSeq}`,
            clientLookupId: "",
            clientLabel: "",
            clientQuery: "",
            clientMatchedValue: "",
            clientFailureReason: "",
            companyAccountId: "",
            productLookupId: "",
            productLabel: "",
            productQuery: "",
            productMatchedValue: "",
            productFailureReason: "",
            value: roundCurrency(Number(initialValue || 0))
        };
    }

    function handleBreakdownValueInput(input) {
        const draft = getBreakdownDraftRow(input.getAttribute("data-breakdown-id") || "");
        if (!draft) {
            return;
        }

        const parsed = Number.parseFloat(input.value || "0");
        draft.value = Number.isFinite(parsed) ? roundCurrency(Math.max(parsed, 0)) : 0;
        refreshBreakdownModalState();
    }

    function handleBreakdownLookupInput(input, kind) {
        const draft = getBreakdownDraftRow(input.getAttribute("data-breakdown-id") || "");
        if (!draft) {
            return;
        }

        const query = input.value.trim();
        if (kind === "client") {
            draft.clientLookupId = "";
            draft.clientLabel = "";
            draft.clientQuery = query;
            draft.clientMatchedValue = "";
            draft.clientFailureReason = query.length < 2
                ? "Escribe al menos 2 caracteres para buscar el cliente."
                : "";
            draft.companyAccountId = "";
        } else {
            draft.productLookupId = "";
            draft.productLabel = "";
            draft.productQuery = query;
            draft.productMatchedValue = "";
            draft.productFailureReason = query.length < 2
                ? "Escribe al menos 2 caracteres para buscar el producto."
                : "";
        }

        refreshBreakdownLookupHelper(draft.id, kind);
        refreshBreakdownModalState();

        if (query.length < 2) {
            hideBreakdownLookupMenu(draft.id, kind);
            return;
        }

        scheduleBreakdownLookupSearch(draft.id, kind, query, 280);
    }

    function openBreakdownLookupMenu(input, kind) {
        const draftId = input.getAttribute("data-breakdown-id") || "";
        const query = input.value.trim();
        if (!draftId || query.length < 2) {
            return;
        }

        scheduleBreakdownLookupSearch(draftId, kind, query, 0);
    }

    function scheduleBreakdownLookupSearch(draftId, kind, query, delay) {
        const key = `${kind}:${draftId}`;
        const previousTimer = state.breakdownLookupTimers.get(key);
        if (previousTimer) {
            window.clearTimeout(previousTimer);
        }

        const timer = window.setTimeout(() => searchBreakdownLookup(draftId, kind, query), delay);
        state.breakdownLookupTimers.set(key, timer);
    }

    async function searchBreakdownLookup(draftId, kind, query) {
        const menu = getBreakdownLookupMenu(draftId, kind);
        const draft = getBreakdownDraftRow(draftId);
        if (!menu || !draft) {
            return;
        }

        const url = kind === "client"
            ? buildAccountSearchUrl(query)
            : buildProductSearchUrl(query);
        if (!url) {
            return;
        }

        const requestKey = `${kind}:${draftId}`;
        const requestId = ++state.breakdownLookupRequestSeq;
        state.breakdownLookupRequests.set(requestKey, requestId);
        menu.innerHTML = "<div class=\"lic-lookup-empty\">Buscando...</div>";
        menu.classList.add("is-open");

        try {
            const items = await fetchJson(url);
            if (state.breakdownLookupRequests.get(requestKey) !== requestId) {
                return;
            }

            if (!Array.isArray(items) || items.length === 0) {
                setBreakdownLookupFailure(draft, kind, `No se encontraron ${kind === "client" ? "clientes" : "productos"} para "${query}".`);
                menu.innerHTML = "<div class=\"lic-lookup-empty\">Sin resultados</div>";
                menu.classList.add("is-open");
                return;
            }

            menu.innerHTML = items.map((item) => `
                <button type="button"
                        class="lic-lookup-option"
                        data-breakdown-${kind}-option
                        data-breakdown-id="${escapeHtml(draftId)}"
                        data-id="${escapeHtml(item.id || "")}"
                        data-label="${escapeHtml(item.label || "")}"
                        data-matched-value="${escapeHtml(item.matchedValue || "")}">
                    <span>${escapeHtml(item.label || (kind === "client" ? "Cliente sin nombre" : "Producto sin nombre"))}</span>
                    <small>${escapeHtml(item.matchedValue || item.searchField || "")}</small>
                </button>
            `).join("");
            menu.classList.add("is-open");
        } catch (error) {
            if (state.breakdownLookupRequests.get(requestKey) !== requestId) {
                return;
            }

            setBreakdownLookupFailure(draft, kind, getErrorMessage(error));
            menu.innerHTML = "<div class=\"lic-lookup-empty\">No se pudo buscar</div>";
            menu.classList.add("is-open");
        }
    }

    function selectBreakdownLookupOption(option, kind) {
        const draft = getBreakdownDraftRow(option.getAttribute("data-breakdown-id") || "");
        if (!draft) {
            return;
        }

        const selectedId = option.getAttribute("data-id") || "";
        const selectedLabel = option.getAttribute("data-label") || "";
        const matchedValue = option.getAttribute("data-matched-value") || "";
        if (kind === "client") {
            draft.clientLookupId = selectedId;
            draft.clientLabel = selectedLabel;
            draft.clientQuery = selectedLabel;
            draft.clientMatchedValue = matchedValue;
            draft.clientFailureReason = "";
            draft.companyAccountId = matchedValue || selectedLabel;
        } else {
            draft.productLookupId = selectedId;
            draft.productLabel = selectedLabel;
            draft.productQuery = selectedLabel;
            draft.productMatchedValue = matchedValue;
            draft.productFailureReason = "";
        }

        const input = breakdownBody?.querySelector(`[data-breakdown-${kind}-search][data-breakdown-id="${draft.id}"]`);
        if (input instanceof HTMLInputElement) {
            input.value = selectedLabel;
        }

        refreshBreakdownLookupHelper(draft.id, kind);
        refreshBreakdownModalState();
        hideBreakdownLookupMenu(draft.id, kind);
    }

    function setBreakdownLookupFailure(draft, kind, message) {
        if (kind === "client") {
            draft.clientFailureReason = message;
        } else {
            draft.productFailureReason = message;
        }

        refreshBreakdownLookupHelper(draft.id, kind);
        refreshBreakdownModalState();
    }

    function refreshBreakdownLookupHelper(draftId, kind) {
        const draft = getBreakdownDraftRow(draftId);
        if (!draft) {
            return;
        }

        const helper = breakdownBody?.querySelector(`[data-breakdown-${kind}-helper="${draftId}"]`);
        if (!helper) {
            return;
        }

        helper.className = getBreakdownLookupSelectedValue(draft, kind)
            ? "lic-muted"
            : "lic-lookup-note is-warning";
        helper.textContent = getBreakdownLookupHelperText(draft, kind);
    }

    function getBreakdownLookupHelperText(draft, kind) {
        const hasSelection = Boolean(getBreakdownLookupSelectedValue(draft, kind));
        if (hasSelection) {
            return `Lookup encontrado: ${kind === "client" ? draft.clientLabel : draft.productLabel}`;
        }

        const failureReason = kind === "client" ? draft.clientFailureReason : draft.productFailureReason;
        if (failureReason) {
            return failureReason;
        }

        return kind === "client"
            ? "Busca y selecciona un cliente."
            : "Busca y selecciona un producto.";
    }

    function getBreakdownLookupSelectedValue(draft, kind) {
        return kind === "client" ? draft.clientLookupId : draft.productLookupId;
    }

    function getBreakdownDraftRow(draftId) {
        return state.breakdownDraftRows.find((draft) => draft.id === draftId);
    }

    function getBreakdownLookupMenu(draftId, kind) {
        return breakdownBody?.querySelector(`[data-breakdown-${kind}-menu="${draftId}"]`) || null;
    }

    function hideBreakdownLookupMenu(draftId, kind) {
        const menu = getBreakdownLookupMenu(draftId, kind);
        if (!menu) {
            return;
        }

        menu.classList.remove("is-open");
        menu.innerHTML = "";
    }

    function closeBreakdownLookupMenus() {
        breakdownBody?.querySelectorAll(".lic-lookup-menu.is-open").forEach((menu) => {
            menu.classList.remove("is-open");
            menu.innerHTML = "";
        });
    }

    function getBreakdownOriginalRow() {
        return state.breakdownSourceIndex >= 0 ? state.previewRows[state.breakdownSourceIndex] : null;
    }

    function getBreakdownAssignedTotal() {
        return roundCurrency(state.breakdownDraftRows.reduce((sum, draft) => sum + Number(draft.value || 0), 0));
    }

    function getBreakdownRemainingValue() {
        const sourceRow = getBreakdownOriginalRow();
        const total = Number(sourceRow?.valorTotalUsd || 0);
        return roundCurrency(total - getBreakdownAssignedTotal());
    }

    function getBreakdownValidationMessage() {
        const sourceRow = getBreakdownOriginalRow();
        if (!sourceRow) {
            return "No encontramos la fila original para desglosar.";
        }

        if (state.breakdownDraftRows.length === 0) {
            return "Agrega al menos una fila en el desglose.";
        }

        for (const draft of state.breakdownDraftRows) {
            if (!draft.clientLookupId) {
                return "Cada fila del desglose debe tener un cliente seleccionado.";
            }

            if (!draft.productLookupId) {
                return "Cada fila del desglose debe tener un producto seleccionado.";
            }

            if (!Number.isFinite(Number(draft.value)) || Number(draft.value) <= 0) {
                return "Cada fila del desglose debe tener un valor mayor a cero.";
            }
        }

        if (Math.abs(getBreakdownRemainingValue()) >= 0.005) {
            return "El saldo del desglose debe quedar en 0.";
        }

        return "";
    }

    function saveBreakdownRows() {
        const sourceRow = getBreakdownOriginalRow();
        const validationMessage = getBreakdownValidationMessage();
        if (!sourceRow || validationMessage) {
            showStatus(breakdownStatus, "warning", validationMessage || "No encontramos la fila original para desglosar.");
            return;
        }

        const replacementRows = state.breakdownDraftRows.map((draft) => buildBreakdownPreviewRow(sourceRow, draft));
        state.previewRows.splice(state.breakdownSourceIndex, 1, ...replacementRows);
        closeBreakdownModal({ preserveDraft: false, preserveStatus: false });
        renderPreview();
        showStatus(uploadStatus, "success", `Se reemplazo la fila original por ${replacementRows.length} fila(s) de desglose.`);
    }

    function buildBreakdownPreviewRow(sourceRow, draft) {
        const amount = roundCurrency(Number(draft.value || 0));
        const clientLabel = draft.clientLabel || draft.clientMatchedValue || sourceRow.nombreCliente || "";
        const productLabel = draft.productLabel || draft.productMatchedValue || sourceRow.productDescription || "";
        return {
            ...sourceRow,
            companyAccountId: draft.companyAccountId || draft.clientMatchedValue || clientLabel,
            companyAccountLookupId: draft.clientLookupId,
            companyAccountLookupLabel: clientLabel,
            companyAccountLookupFound: Boolean(draft.clientLookupId),
            companyAccountLookupFailureReason: "",
            nombreCliente: clientLabel,
            productDescription: productLabel,
            productLookupId: draft.productLookupId,
            productLookupLabel: productLabel,
            productLookupFound: Boolean(draft.productLookupId),
            productLookupFailureReason: "",
            valorTotalUsd: amount,
            unidadUsd: amount,
            cantidad: 1,
            requiresBreakdown: false,
            breakdownGenerated: true,
            isValid: true,
            warnings: [],
            errors: []
        };
    }

    async function importPreviewRows() {
        if (state.previewRows.length === 0 || state.previewRows.some((row) => !row.isValid)) {
            showStatus(uploadStatus, "warning", "La vista previa tiene filas pendientes.");
            return;
        }

        if (state.previewRows.some((row) => requiresBreakdown(row))) {
            showStatus(uploadStatus, "warning", "Debes desglosar todas las filas de cargo manual antes de procesar.");
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

    function formatBreakdownInputValue(value) {
        if (!Number.isFinite(Number(value))) {
            return "";
        }

        return Number(value).toFixed(2);
    }

    function normalizeBreakdownProduct(value) {
        return (value || "")
            .toString()
            .trim()
            .toLowerCase();
    }

    function roundCurrency(value) {
        const amount = Number(value || 0);
        if (!Number.isFinite(amount)) {
            return 0;
        }

        return Math.round(amount * 100) / 100;
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
