(() => {
    const app = document.getElementById("billingDashboardApp");
    if (!app) {
        return;
    }

    const dashboardPeriodScope = document.getElementById("dashboardPeriodScope");
    const yearFilter = document.getElementById("dashboardYearFilter");
    const periodFilter = document.getElementById("dashboardPeriodFilter");
    const valueFilter = document.getElementById("dashboardValueFilter");
    const refreshButton = document.getElementById("dashboardRefreshBtn");
    const portfolioRefreshButton = document.getElementById("portfolioRefreshBtn");
    const billingStatusBanner = document.getElementById("dashboardStatusBanner");
    const taxesStatusBanner = document.getElementById("taxesStatusBanner");
    const portfolioStatusBanner = document.getElementById("portfolioStatusBanner");
    const periodLabel = document.getElementById("dashboardPeriodLabel");
    const dateRangeLabel = document.getElementById("dashboardDateRangeLabel");
    const compareLabel = document.getElementById("dashboardCompareLabel");
    const granularityLabel = document.getElementById("dashboardGranularityLabel");
    const recordCount = document.getElementById("dashboardRecordCount");

    const billingKpisContainer = document.getElementById("billingKpisContainer");
    const trendsContainer = document.getElementById("billingTrendsContainer");

    const copiersRefreshButton = document.getElementById("copiersRefreshBtn");
    const copiersStatusBanner = document.getElementById("copiersStatusBanner");
    const copiersAsOfLabel = document.getElementById("copiersAsOfLabel");
    const copiersFocusLabel = document.getElementById("copiersFocusLabel");
    const copiersResultsCount = document.getElementById("copiersResultsCount");
    const copiersKpisContainer = document.getElementById("copiersKpisContainer");
    const copiersBillingBody = document.getElementById("copiersBillingBody");

    const taxesReteFuenteDescription = document.getElementById("taxesReteFuenteDescription");
    const taxesReteIvaDescription = document.getElementById("taxesReteIvaDescription");
    const taxesReteIcaDescription = document.getElementById("taxesReteIcaDescription");
    const taxesReteFuenteContainer = document.getElementById("taxesReteFuenteContainer");
    const taxesReteIvaContainer = document.getElementById("taxesReteIvaContainer");
    const taxesReteIcaContainer = document.getElementById("taxesReteIcaContainer");
    const taxesExpenseBody = document.getElementById("taxesExpenseBody");
    const taxesExpenseResultsCount = document.getElementById("taxesExpenseResultsCount");

    const portfolioAsOfLabel = document.getElementById("portfolioAsOfLabel");
    const portfolioFocusLabel = document.getElementById("portfolioFocusLabel");
    const portfolioClientSearch = document.getElementById("portfolioClientSearch");
    const portfolioSortFilter = document.getElementById("portfolioSortFilter");
    const portfolioResultsCount = document.getElementById("portfolioResultsCount");
    const portfolioKpisContainer = document.getElementById("portfolioKpisContainer");
    const portfolioUnpaidBody = document.getElementById("portfolioUnpaidBody");

    const pnlYearFilter = document.getElementById("pnlYearFilter");
    const pnlMonthFilter = document.getElementById("pnlMonthFilter");
    const pnlVerticalFilter = document.getElementById("pnlVerticalFilter");
    const pnlRefreshButton = document.getElementById("pnlRefreshBtn");
    const pnlStatusBanner = document.getElementById("pnlStatusBanner");
    const pnlPeriodLabel = document.getElementById("pnlPeriodLabel");
    const pnlDateRangeLabel = document.getElementById("pnlDateRangeLabel");
    const pnlKpisContainer = document.getElementById("pnlKpisContainer");
    const pnlDescription = document.getElementById("pnlDescription");
    const pnlTableContainer = document.getElementById("pnlTableContainer");
    const pnlDetailModal = document.getElementById("pnlDetailModal");
    const pnlDetailCloseBtn = document.getElementById("pnlDetailCloseBtn");
    const pnlDetailTitle = document.getElementById("pnlDetailTitle");
    const pnlDetailSubtitle = document.getElementById("pnlDetailSubtitle");
    const pnlDetailStatus = document.getElementById("pnlDetailStatus");
    const pnlDetailBody = document.getElementById("pnlDetailBody");

    const tabButtons = Array.from(document.querySelectorAll("[data-dashboard-tab]"));
    const tabPanels = Array.from(document.querySelectorAll("[data-dashboard-panel]"));

    const currentYear = Number(app.dataset.initialYear || new Date().getFullYear());
    const currentPeriod = app.dataset.initialPeriod || "month";
    const currentValue = Number(app.dataset.initialValue || 1);

    const currencyFormatter = new Intl.NumberFormat("es-CO", {
        style: "currency",
        currency: "COP",
        minimumFractionDigits: 0,
        maximumFractionDigits: 0
    });

    const numberFormatter = new Intl.NumberFormat("es-CO", {
        minimumFractionDigits: 0,
        maximumFractionDigits: 2
    });

    const monthLabels = ["Enero", "Febrero", "Marzo", "Abril", "Mayo", "Junio", "Julio", "Agosto", "Septiembre", "Octubre", "Noviembre", "Diciembre"];

    const state = {
        activeTab: "billing",
        year: currentYear,
        period: currentPeriod,
        value: currentValue,
        billingDashboard: null,
        copiersDashboard: null,
        taxesDashboard: null,
        portfolioDashboard: null,
        pnlDashboard: null,
        billingSignature: "",
        taxesSignature: "",
        pnlSignature: "",
        copiersLoading: false,
        portfolioSearchTerm: "",
        portfolioSort: "age",
        pnlYear: currentYear,
        pnlMonth: new Date().getMonth() + 1,
        pnlVertical: "all",
        pnlDetail: null,
        pnlDetailContext: null,
        pnlDetailLoading: false,
        pnlDetailSavingRecordId: "",
        periodLoading: false,
        portfolioLoading: false,
        pnlLoading: false
    };

    function escapeHtml(value) {
        return (value ?? "").toString()
            .replace(/&/g, "&amp;")
            .replace(/</g, "&lt;")
            .replace(/>/g, "&gt;")
            .replace(/"/g, "&quot;")
            .replace(/'/g, "&#39;");
    }

    function normalizeText(value) {
        return (value ?? "")
            .toString()
            .normalize("NFD")
            .replace(/[\u0300-\u036f]/g, "")
            .toLowerCase()
            .trim();
    }

    function getPeriodSignature() {
        return `${state.year}|${state.period}|${state.value}`;
    }

    function getPnlSignature() {
        return `${state.pnlYear}|${state.pnlMonth}|${state.pnlVertical}`;
    }

    function formatMetric(value, format) {
        const numericValue = Number(value || 0);
        if (format === "number") {
            return numberFormatter.format(numericValue);
        }

        if (format === "days") {
            return `${numberFormatter.format(numericValue)} dias`;
        }

        if (format === "percent") {
            return `${numberFormatter.format(numericValue)}%`;
        }

        return currencyFormatter.format(numericValue);
    }

    function formatGrowth(value) {
        if (value === null || value === undefined) {
            return "Nuevo";
        }

        const numericValue = Number(value || 0);
        const prefix = numericValue > 0 ? "+" : "";
        return `${prefix}${numberFormatter.format(numericValue)}%`;
    }

    function formatPercent(value) {
        return `${numberFormatter.format(Number(value || 0))}%`;
    }

    function setStatus(target, type, message) {
        if (!target) {
            return;
        }

        if (!message) {
            target.className = "dashboard-status";
            target.textContent = "";
            return;
        }

        target.className = `dashboard-status show ${type}`;
        target.textContent = message;
    }

    function setPeriodLoading(loading) {
        state.periodLoading = loading;
        [yearFilter, periodFilter, valueFilter, refreshButton].forEach(element => {
            if (element) {
                element.disabled = loading;
            }
        });
    }

    function setPortfolioLoading(loading) {
        state.portfolioLoading = loading;
        [portfolioRefreshButton, portfolioClientSearch, portfolioSortFilter].forEach(element => {
            if (element) {
                element.disabled = loading;
            }
        });
    }

    function setCopiersLoading(loading) {
        state.copiersLoading = loading;
        [copiersRefreshButton].forEach(element => {
            if (element) {
                element.disabled = loading;
            }
        });
    }

    function setPnlLoading(loading) {
        state.pnlLoading = loading;
        [pnlYearFilter, pnlMonthFilter, pnlVerticalFilter, pnlRefreshButton].forEach(element => {
            if (element) {
                element.disabled = loading;
            }
        });
    }

    function isPnlDetailOpen() {
        return Boolean(pnlDetailModal && !pnlDetailModal.hidden);
    }

    function openPnlDetailModal() {
        if (!pnlDetailModal) {
            return;
        }

        pnlDetailModal.hidden = false;
        document.body.classList.add("dashboard-modal-open");
    }

    function closePnlDetailModal() {
        if (!pnlDetailModal) {
            return;
        }

        pnlDetailModal.hidden = true;
        document.body.classList.remove("dashboard-modal-open");
        state.pnlDetail = null;
        state.pnlDetailContext = null;
        state.pnlDetailLoading = false;
        state.pnlDetailSavingRecordId = "";
        setStatus(pnlDetailStatus, "", "");

        if (pnlDetailTitle) {
            pnlDetailTitle.textContent = "Detalle de la celda";
        }

        if (pnlDetailSubtitle) {
            pnlDetailSubtitle.textContent = "Selecciona una celda del P&L para ver su composición.";
        }

        if (pnlDetailBody) {
            pnlDetailBody.innerHTML = '<tr><td colspan="14" class="dashboard-table__empty">Selecciona una celda del P&L para ver el detalle.</td></tr>';
        }
    }

    function renderPnlDetailLoading(rowLabel, cellMonth) {
        if (pnlDetailTitle) {
            pnlDetailTitle.textContent = rowLabel || "Detalle de la celda";
        }

        if (pnlDetailSubtitle) {
            pnlDetailSubtitle.textContent = cellMonth
                ? `Cargando composición de ${rowLabel || "la celda"} para ${monthLabels[Math.max(Number(cellMonth) - 1, 0)] || "el mes seleccionado"}...`
                : `Cargando composición acumulada de ${rowLabel || "la celda"}...`;
        }

        if (pnlDetailBody) {
            pnlDetailBody.innerHTML = '<tr><td colspan="14" class="dashboard-table__empty">Cargando detalle...</td></tr>';
        }
    }

    function buildPnlDetailSubtitle(detail) {
        const parts = [];
        if (detail?.cellLabel) {
            parts.push(detail.cellLabel);
        }

        if (detail?.verticalLabel) {
            parts.push(detail.verticalLabel);
        }

        parts.push(formatMetric(detail?.total || 0, detail?.valueFormat || "currency"));
        parts.push(`${numberFormatter.format(Number(detail?.recordsCount || 0))} registros`);
        return parts.join(" · ");
    }

    function buildPnlDetailSelectOptions(options, currentKey, currentLabel, currentValue, useNumericValue) {
        const normalizedOptions = Array.isArray(options) ? [...options] : [];
        const hasCurrent = normalizedOptions.some(option => {
            if (useNumericValue) {
                return Number(option?.value) === Number(currentValue);
            }

            return (option?.key || "") === (currentKey || "");
        });

        if (!hasCurrent && ((useNumericValue && Number.isFinite(Number(currentValue))) || (!useNumericValue && currentKey))) {
            normalizedOptions.unshift({
                key: currentKey || String(currentValue || ""),
                label: currentLabel || "Actual",
                value: useNumericValue ? Number(currentValue) : currentValue
            });
        }

        return normalizedOptions;
    }

    function renderPnlVerticalEditor(record, verticalOptions) {
        if (!record?.canEditVertical) {
            return `<span class="dashboard-pnl-detail__static">${escapeHtml(record?.verticalLabel || "No aplica")}</span>`;
        }

        const options = buildPnlDetailSelectOptions(
            verticalOptions,
            record?.verticalKey || "",
            record?.verticalLabel || "",
            record?.verticalKey || "",
            false
        );

        return `
            <select class="form-select form-select-sm dashboard-select dashboard-select--detail" data-pnl-edit-field="vertical">
                ${options.map(option => `
                    <option value="${escapeHtml(option?.key || "")}" ${(option?.key || "") === (record?.verticalKey || "") ? "selected" : ""}>
                        ${escapeHtml(option?.label || option?.key || "")}
                    </option>
                `).join("")}
            </select>
        `;
    }

    function renderPnlCategoryEditor(record, categoryOptions) {
        if (!record?.canEditCategory) {
            return `<span class="dashboard-pnl-detail__static">${escapeHtml(record?.categoryLabel || "No aplica")}</span>`;
        }

        const options = buildPnlDetailSelectOptions(
            categoryOptions,
            String(record?.categoryOptionValue || ""),
            record?.categoryLabel || "",
            record?.categoryOptionValue,
            true
        );

        return `
            <select class="form-select form-select-sm dashboard-select dashboard-select--detail" data-pnl-edit-field="category">
                ${options.map(option => `
                    <option value="${escapeHtml(String(option?.value ?? ""))}" ${Number(option?.value) === Number(record?.categoryOptionValue) ? "selected" : ""}>
                        ${escapeHtml(option?.label || "")}
                    </option>
                `).join("")}
            </select>
        `;
    }

    function renderPnlDetail(detail) {
        state.pnlDetail = detail || null;

        if (pnlDetailTitle) {
            pnlDetailTitle.textContent = detail?.rowLabel || "Detalle de la celda";
        }

        if (pnlDetailSubtitle) {
            pnlDetailSubtitle.textContent = buildPnlDetailSubtitle(detail);
        }

        if (!pnlDetailBody) {
            return;
        }

        const records = Array.isArray(detail?.records) ? detail.records : [];
        if (!records.length) {
            pnlDetailBody.innerHTML = `
                <tr>
                    <td colspan="14" class="dashboard-table__empty">${escapeHtml(detail?.emptyMessage || "No encontramos registros para esta celda.")}</td>
                </tr>
            `;
            return;
        }

        pnlDetailBody.innerHTML = records.map(record => {
            const isSaving = state.pnlDetailSavingRecordId && state.pnlDetailSavingRecordId === record.recordId;
            return `
                <tr
                    data-record-id="${escapeHtml(record.recordId || "")}"
                    data-source-type="${escapeHtml(record.sourceType || "")}"
                    data-original-vertical="${escapeHtml(record.verticalKey || "")}"
                    data-original-category="${escapeHtml(String(record.categoryOptionValue ?? ""))}">
                    <td>${escapeHtml(record.sourceLabel || "")}</td>
                    <td>${escapeHtml(record.documentNumber || "")}</td>
                    <td>${escapeHtml(record.dateDisplay || "-")}</td>
                    <td>${escapeHtml(record.description || "")}</td>
                    <td>${renderPnlVerticalEditor(record, detail?.verticalOptions)}</td>
                    <td>${renderPnlCategoryEditor(record, detail?.categoryOptions)}</td>
                    <td class="text-end">${escapeHtml(currencyFormatter.format(Number(record.totalInvoice || 0)))}</td>
                    <td class="text-end">${escapeHtml(currencyFormatter.format(Number(record.vatValue || 0)))}</td>
                    <td class="text-end">${escapeHtml(currencyFormatter.format(Number(record.totalBeforeVatValue || 0)))}</td>
                    <td class="text-end">${escapeHtml(currencyFormatter.format(Number(record.paymentValue || 0)))}</td>
                    <td class="text-end">${escapeHtml(currencyFormatter.format(Number(record.cloudValue || 0)))}</td>
                    <td class="text-end">${escapeHtml(currencyFormatter.format(Number(record.copiersValue || 0)))}</td>
                    <td class="text-end"><strong>${escapeHtml(currencyFormatter.format(Number(record.cellValue || 0)))}</strong></td>
                    <td>
                        <button type="button" class="btn btn-sm btn-outline-primary" data-pnl-detail-save ${isSaving ? "disabled" : ""}>
                            ${isSaving ? "Guardando..." : "Guardar"}
                        </button>
                    </td>
                </tr>
            `;
        }).join("");
    }

    async function loadPnlDetail(rowKey, rowLabel, cellMonth) {
        if (!rowKey) {
            return;
        }

        state.pnlDetailContext = {
            rowKey,
            rowLabel: rowLabel || "Detalle de la celda",
            cellMonth: Number.isFinite(Number(cellMonth)) ? Number(cellMonth) : null
        };

        state.pnlDetailLoading = true;
        openPnlDetailModal();
        renderPnlDetailLoading(rowLabel, state.pnlDetailContext.cellMonth);
        setStatus(pnlDetailStatus, "info", "Cargando detalle de la celda...");

        try {
            const detail = await fetchJson(buildPnlDetailUrl(rowKey, state.pnlDetailContext.cellMonth));
            renderPnlDetail(detail);
            setStatus(pnlDetailStatus, "", "");
        } catch (error) {
            setStatus(pnlDetailStatus, "error", error instanceof Error ? error.message : "No fue posible cargar el detalle de la celda.");
            if (pnlDetailBody) {
                pnlDetailBody.innerHTML = '<tr><td colspan="14" class="dashboard-table__empty">No fue posible cargar el detalle.</td></tr>';
            }
            throw error;
        } finally {
            state.pnlDetailLoading = false;
        }
    }

    async function refreshCurrentPnlDetail() {
        if (!state.pnlDetailContext) {
            return;
        }

        await loadPnlDetail(
            state.pnlDetailContext.rowKey,
            state.pnlDetailContext.rowLabel,
            state.pnlDetailContext.cellMonth
        );
    }

    async function savePnlDetailRecord(saveButton) {
        if (!saveButton) {
            return;
        }

        const row = saveButton.closest("tr[data-record-id]");
        if (!row) {
            return;
        }

        const payload = {
            sourceType: row.dataset.sourceType || "",
            recordId: row.dataset.recordId || ""
        };

        const verticalSelect = row.querySelector("[data-pnl-edit-field='vertical']");
        const categorySelect = row.querySelector("[data-pnl-edit-field='category']");
        const originalVertical = (row.dataset.originalVertical || "").toLowerCase();
        const originalCategory = Number(row.dataset.originalCategory || 0);
        const verticalValue = verticalSelect && !verticalSelect.disabled ? (verticalSelect.value || "").toLowerCase() : "";
        if ((verticalValue === "cloud" || verticalValue === "copiers") && verticalValue !== originalVertical) {
            payload.verticalKey = verticalValue;
        }

        const categoryValue = categorySelect && !categorySelect.disabled ? Number(categorySelect.value || 0) : NaN;
        if (!Number.isNaN(categoryValue) && categoryValue > 0 && categoryValue !== originalCategory) {
            payload.categoryOptionValue = categoryValue;
        }

        if (!payload.verticalKey && !payload.categoryOptionValue) {
            setStatus(pnlDetailStatus, "info", "No hay cambios pendientes en este registro.");
            return;
        }

        state.pnlDetailSavingRecordId = payload.recordId;
        renderPnlDetail(state.pnlDetail);
        setStatus(pnlDetailStatus, "info", "Guardando cambios en Dataverse...");

        try {
            const result = await fetchJson(app.dataset.pnlDetailUpdateUrl, {
                method: "POST",
                body: JSON.stringify(payload)
            });

            await loadPnl();
            await refreshCurrentPnlDetail();
            setStatus(pnlDetailStatus, "info", result?.message || "Registro actualizado correctamente.");
        } catch (error) {
            state.pnlDetailSavingRecordId = "";
            renderPnlDetail(state.pnlDetail);
            setStatus(pnlDetailStatus, "error", error instanceof Error ? error.message : "No fue posible guardar el registro.");
            return;
        }

        state.pnlDetailSavingRecordId = "";
        renderPnlDetail(state.pnlDetail);
    }

    function buildYearOptions() {
        if (!yearFilter) {
            return;
        }

        const options = [];
        for (let year = currentYear + 1; year >= currentYear - 5; year -= 1) {
            options.push(`<option value="${year}">${year}</option>`);
        }

        yearFilter.innerHTML = options.join("");
        yearFilter.value = String(state.year);
    }

    function buildPnlYearOptions() {
        if (!pnlYearFilter) {
            return;
        }

        const options = [];
        for (let year = currentYear + 1; year >= currentYear - 5; year -= 1) {
            options.push(`<option value="${year}">${year}</option>`);
        }

        pnlYearFilter.innerHTML = options.join("");
        pnlYearFilter.value = String(state.pnlYear);
    }

    function buildPnlMonthOptions(maxMonth = 12) {
        if (!pnlMonthFilter) {
            return;
        }

        const safeMaxMonth = Math.min(Math.max(Number(maxMonth || 1), 1), 12);
        if (state.pnlMonth > safeMaxMonth) {
            state.pnlMonth = safeMaxMonth;
        }

        pnlMonthFilter.innerHTML = monthLabels
            .slice(0, safeMaxMonth)
            .map((label, index) => `<option value="${index + 1}">${escapeHtml(label)}</option>`)
            .join("");
        pnlMonthFilter.value = String(state.pnlMonth);
    }

    function getDefaultValue(period, year) {
        if (year !== currentYear) {
            return 1;
        }

        const today = new Date();
        switch (period) {
            case "quarter":
                return Math.floor(today.getMonth() / 3) + 1;
            case "semester":
                return today.getMonth() < 6 ? 1 : 2;
            case "year":
                return 1;
            default:
                return today.getMonth() + 1;
        }
    }

    function buildValueOptions() {
        if (!valueFilter) {
            return;
        }

        const options = [];
        switch (state.period) {
            case "quarter":
                ["T1", "T2", "T3", "T4"].forEach((label, index) => {
                    options.push({ value: index + 1, label });
                });
                break;
            case "semester":
                ["S1", "S2"].forEach((label, index) => {
                    options.push({ value: index + 1, label });
                });
                break;
            case "year":
                options.push({ value: 1, label: "Ano completo" });
                break;
            default:
                ["Enero", "Febrero", "Marzo", "Abril", "Mayo", "Junio", "Julio", "Agosto", "Septiembre", "Octubre", "Noviembre", "Diciembre"]
                    .forEach((label, index) => {
                        options.push({ value: index + 1, label });
                    });
                break;
        }

        if (!options.some(option => option.value === state.value)) {
            state.value = getDefaultValue(state.period, state.year);
        }

        valueFilter.innerHTML = options
            .map(option => `<option value="${option.value}">${escapeHtml(option.label)}</option>`)
            .join("");
        valueFilter.value = String(state.value);
    }

    function buildBillingUrl() {
        const params = new URLSearchParams({
            year: String(state.year),
            period: state.period,
            value: String(state.value)
        });

        return `${app.dataset.billingUrl}?${params.toString()}`;
    }

    function buildTaxesUrl() {
        const params = new URLSearchParams({
            year: String(state.year),
            period: state.period,
            value: String(state.value)
        });

        return `${app.dataset.taxesUrl}?${params.toString()}`;
    }

    function buildPortfolioUrl() {
        return app.dataset.portfolioUrl || "";
    }

    function buildCopiersUrl() {
        return app.dataset.copiersUrl || "";
    }

    function buildPnlUrl() {
        const params = new URLSearchParams({
            year: String(state.pnlYear),
            month: String(state.pnlMonth),
            vertical: state.pnlVertical
        });

        return `${app.dataset.pnlUrl}?${params.toString()}`;
    }

    function buildPnlDetailUrl(rowKey, cellMonth) {
        const params = new URLSearchParams({
            year: String(state.pnlYear),
            cutoffMonth: String(state.pnlMonth),
            vertical: state.pnlVertical,
            rowKey: rowKey || ""
        });

        if (Number.isFinite(Number(cellMonth))) {
            params.set("cellMonth", String(Number(cellMonth)));
        }

        return `${app.dataset.pnlDetailUrl}?${params.toString()}`;
    }

    async function fetchJson(url, options = {}) {
        const headers = {
            Accept: "application/json",
            ...(options.headers || {})
        };
        if (options.body && !headers["Content-Type"]) {
            headers["Content-Type"] = "application/json";
        }

        const response = await fetch(url, {
            method: options.method || "GET",
            headers,
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
                        : payload?.message || payload?.title || payload?.error?.message || rawBody;
                } catch {
                    message = rawBody;
                }
            }

            throw new Error(message || "No fue posible completar la solicitud.");
        }

        if (!contentType.includes("application/json")) {
            const message = await response.text();
            throw new Error(message || "La respuesta del servidor no fue valida.");
        }

        return response.json();
    }

    function renderComparativeKpis(container, kpis, compareYear) {
        if (!container) {
            return;
        }

        const items = Array.isArray(kpis) ? kpis : [];
        const renderBreakdowns = breakdowns => {
            if (!Array.isArray(breakdowns) || !breakdowns.length) {
                return "";
            }

            return `
                <div class="dashboard-kpi__breakdowns">
                    ${breakdowns.map(item => `
                        <div class="dashboard-kpi__breakdown">
                            <div class="dashboard-kpi__breakdown-head">
                                <span class="dashboard-kpi__breakdown-label">${escapeHtml(item.label)}</span>
                                <span class="dashboard-kpi__breakdown-value">${escapeHtml(currencyFormatter.format(Number(item.value || 0)))} · ${escapeHtml(formatPercent(item.sharePercent || 0))}</span>
                            </div>
                            <div class="dashboard-kpi__breakdown-track">
                                <span class="dashboard-kpi__breakdown-fill" style="width:${Math.min(Number(item.sharePercent || 0), 100)}%"></span>
                            </div>
                        </div>
                    `).join("")}
                </div>
            `;
        };

        container.innerHTML = items.map(kpi => `
            <article class="dashboard-kpi dashboard-kpi--${escapeHtml(kpi.tone || "neutral")}">
                <div class="dashboard-kpi__header">
                    <span class="dashboard-kpi__label">${escapeHtml(kpi.label)}</span>
                    <span class="dashboard-kpi__delta">${escapeHtml(formatGrowth(kpi.growthPercent))}</span>
                </div>
                <strong class="dashboard-kpi__value">${escapeHtml(formatMetric(kpi.value, kpi.valueFormat))}</strong>
                <span class="dashboard-kpi__hint">${escapeHtml(kpi.hint)}</span>
                <div class="dashboard-kpi__footer">
                    <span>${escapeHtml(String(compareYear || ""))}</span>
                    <strong>${escapeHtml(formatMetric(kpi.previousValue, kpi.valueFormat))}</strong>
                </div>
                ${kpi.secondaryLabel || kpi.secondaryValue ? `
                    <div class="dashboard-kpi__secondary">
                        <span>${escapeHtml(kpi.secondaryLabel || "")}</span>
                        <strong>${escapeHtml(kpi.secondaryValue || "")}</strong>
                    </div>
                ` : ""}
                ${renderBreakdowns(kpi.breakdowns)}
            </article>
        `).join("");
    }

    function renderPortfolioKpis(dashboard) {
        const kpis = Array.isArray(dashboard?.kpis) ? dashboard.kpis : [];
        if (!portfolioKpisContainer) {
            return;
        }

        portfolioKpisContainer.innerHTML = kpis.map(kpi => `
            <article class="dashboard-kpi dashboard-kpi--neutral">
                <div class="dashboard-kpi__header">
                    <span class="dashboard-kpi__label">${escapeHtml(kpi.label)}</span>
                </div>
                <strong class="dashboard-kpi__value">${escapeHtml(formatMetric(kpi.value, kpi.valueFormat))}</strong>
                <span class="dashboard-kpi__hint">${escapeHtml(kpi.hint)}</span>
                <div class="dashboard-kpi__alert">
                    <span class="dashboard-kpi__alert-label">${escapeHtml(kpi.secondaryLabel || "")}</span>
                    <strong class="dashboard-kpi__alert-value">${escapeHtml(kpi.secondaryValue || "")}</strong>
                </div>
            </article>
        `).join("");
    }

    function renderCopiersKpis(dashboard) {
        const kpis = Array.isArray(dashboard?.kpis) ? dashboard.kpis : [];
        if (!copiersKpisContainer) {
            return;
        }

        copiersKpisContainer.innerHTML = kpis.map(kpi => `
            <article class="dashboard-kpi dashboard-kpi--neutral">
                <div class="dashboard-kpi__header">
                    <span class="dashboard-kpi__label">${escapeHtml(kpi.label)}</span>
                </div>
                <strong class="dashboard-kpi__value">${escapeHtml(formatMetric(kpi.value, kpi.valueFormat))}</strong>
                <span class="dashboard-kpi__hint">${escapeHtml(kpi.hint)}</span>
                <div class="dashboard-kpi__alert">
                    <span class="dashboard-kpi__alert-label">${escapeHtml(kpi.secondaryLabel || "")}</span>
                    <strong class="dashboard-kpi__alert-value">${escapeHtml(kpi.secondaryValue || "")}</strong>
                </div>
            </article>
        `).join("");
    }

    function renderPnlKpis(dashboard) {
        const kpis = Array.isArray(dashboard?.kpis) ? dashboard.kpis : [];
        if (!pnlKpisContainer) {
            return;
        }

        pnlKpisContainer.innerHTML = kpis.map(kpi => `
            <article class="dashboard-kpi dashboard-kpi--${escapeHtml(kpi.tone || "neutral")}">
                <div class="dashboard-kpi__header">
                    <span class="dashboard-kpi__label">${escapeHtml(kpi.label)}</span>
                </div>
                <strong class="dashboard-kpi__value">${escapeHtml(formatMetric(kpi.value, kpi.valueFormat))}</strong>
                <span class="dashboard-kpi__hint">${escapeHtml(kpi.hint)}</span>
            </article>
        `).join("");
    }

    function getNiceMaxValue(value) {
        const numericValue = Number(value || 0);
        if (numericValue <= 0) {
            return 1;
        }

        const exponent = Math.floor(Math.log10(numericValue));
        const base = 10 ** exponent;
        const fraction = numericValue / base;

        if (fraction <= 1) {
            return base;
        }

        if (fraction <= 2) {
            return 2 * base;
        }

        if (fraction <= 5) {
            return 5 * base;
        }

        return 10 * base;
    }

    function buildLinePath(points) {
        if (!points.length) {
            return "";
        }

        return points
            .map((point, index) => `${index === 0 ? "M" : "L"} ${point.x.toFixed(2)} ${point.y.toFixed(2)}`)
            .join(" ");
    }

    function buildAreaPath(points, baselineY) {
        if (!points.length) {
            return "";
        }

        const start = `M ${points[0].x.toFixed(2)} ${baselineY.toFixed(2)}`;
        const line = points.map(point => `L ${point.x.toFixed(2)} ${point.y.toFixed(2)}`).join(" ");
        const end = `L ${points[points.length - 1].x.toFixed(2)} ${baselineY.toFixed(2)} Z`;
        return `${start} ${line} ${end}`;
    }

    function buildSampleLabelIndexes(length) {
        if (length <= 8) {
            return new Set(Array.from({ length }, (_, index) => index));
        }

        const indexes = new Set([0, length - 1]);
        const step = Math.ceil(length / 5);
        for (let index = 0; index < length; index += step) {
            indexes.add(index);
        }

        return indexes;
    }

    function renderTrendChart(trend, currentKey, previousKey, accentColor) {
        const labels = trend.map(item => item.label || "");
        const currentValues = trend.map(item => Number(item[currentKey] || 0));
        const previousValues = trend.map(item => Number(item[previousKey] || 0));
        const maxValue = getNiceMaxValue(Math.max(1, ...currentValues, ...previousValues));
        const width = 620;
        const height = 220;
        const padding = { top: 18, right: 20, bottom: 36, left: 52 };
        const plotWidth = width - padding.left - padding.right;
        const plotHeight = height - padding.top - padding.bottom;
        const denominator = Math.max(labels.length - 1, 1);
        const labelIndexes = buildSampleLabelIndexes(labels.length);

        const gridLines = Array.from({ length: 4 }, (_, index) => {
            const ratio = index / 3;
            const y = padding.top + plotHeight - (plotHeight * ratio);
            const value = maxValue * ratio;
            return `
                <line class="dashboard-chart__grid" x1="${padding.left}" y1="${y}" x2="${width - padding.right}" y2="${y}"></line>
                <text class="dashboard-chart__axis-label" x="${padding.left - 8}" y="${y + 4}" text-anchor="end">${escapeHtml(numberFormatter.format(value))}</text>
            `;
        }).join("");

        const xLabels = labels.map((label, index) => {
            if (!labelIndexes.has(index)) {
                return "";
            }

            const x = padding.left + (plotWidth * (index / denominator));
            return `<text class="dashboard-chart__axis-label" x="${x}" y="${height - 10}" text-anchor="middle">${escapeHtml(label)}</text>`;
        }).join("");

        const buildPoints = values => values.map((value, index) => ({
            x: padding.left + (plotWidth * (index / denominator)),
            y: padding.top + plotHeight - ((Number(value || 0) / maxValue) * plotHeight),
            value: Number(value || 0),
            label: labels[index] || ""
        }));

        const currentPoints = buildPoints(currentValues);
        const previousPoints = buildPoints(previousValues);
        const currentPath = buildLinePath(currentPoints);
        const previousPath = buildLinePath(previousPoints);
        const areaPath = buildAreaPath(currentPoints, padding.top + plotHeight);

        const currentDots = currentPoints.map(point => `
            <g>
                <title>${escapeHtml(`${point.label}: ${currencyFormatter.format(point.value)}`)}</title>
                <circle class="dashboard-chart__dot" cx="${point.x}" cy="${point.y}" r="4.2" fill="${accentColor}" stroke="#ffffff"></circle>
            </g>
        `).join("");

        const previousDots = previousPoints.map(point => `
            <g>
                <title>${escapeHtml(`${point.label}: ${currencyFormatter.format(point.value)}`)}</title>
                <circle class="dashboard-chart__dot" cx="${point.x}" cy="${point.y}" r="3.4" fill="#ffffff" stroke="#94a3b8"></circle>
            </g>
        `).join("");

        return `
            <svg class="dashboard-chart" viewBox="0 0 ${width} ${height}" role="img" aria-label="Tendencia">
                <defs>
                    <linearGradient id="dashboardGradient-${currentKey}" x1="0" x2="0" y1="0" y2="1">
                        <stop offset="0%" stop-color="${accentColor}" stop-opacity=".28"></stop>
                        <stop offset="100%" stop-color="${accentColor}" stop-opacity="0"></stop>
                    </linearGradient>
                </defs>
                ${gridLines}
                ${xLabels}
                <path class="dashboard-chart__area" d="${areaPath}" fill="url(#dashboardGradient-${currentKey})"></path>
                <path class="dashboard-chart__line dashboard-chart__line--reference" d="${previousPath}" stroke="#94a3b8"></path>
                <path class="dashboard-chart__line" d="${currentPath}" stroke="${accentColor}"></path>
                ${previousDots}
                ${currentDots}
            </svg>
        `;
    }

    function renderTrends(dashboard) {
        const trend = Array.isArray(dashboard?.trend) ? dashboard.trend : [];
        if (!trendsContainer) {
            return;
        }

        if (!trend.length) {
            trendsContainer.innerHTML = '<div class="dashboard-table__empty">Todavia no hay puntos suficientes para dibujar la tendencia.</div>';
            return;
        }

        const cards = [
            { title: "Facturacion emitida", currentKey: "billingCurrent", previousKey: "billingPrevious", color: "#0f766e" },
            { title: "Recaudo", currentKey: "collectionsCurrent", previousKey: "collectionsPrevious", color: "#1d4ed8" },
            { title: "Retenciones", currentKey: "retentionsCurrent", previousKey: "retentionsPrevious", color: "#f97316" }
        ];

        trendsContainer.innerHTML = cards.map(card => {
            const currentTotal = trend.reduce((sum, point) => sum + Number(point[card.currentKey] || 0), 0);
            const previousTotal = trend.reduce((sum, point) => sum + Number(point[card.previousKey] || 0), 0);
            return `
                <article class="dashboard-trend-card">
                    <div class="dashboard-trend-card__header">
                        <strong>${escapeHtml(card.title)}</strong>
                        <span class="dashboard-growth ${currentTotal >= previousTotal ? "is-positive" : "is-negative"}">${escapeHtml(formatGrowth(previousTotal === 0 && currentTotal > 0 ? null : ((currentTotal - previousTotal) / (previousTotal || 1)) * 100))}</span>
                    </div>
                    <div class="dashboard-trend-card__value">${escapeHtml(currencyFormatter.format(currentTotal))}</div>
                    ${renderTrendChart(trend, card.currentKey, card.previousKey, card.color)}
                    <div class="dashboard-trend-legend">
                        <span class="dashboard-legend-chip" style="color:${card.color}">Actual</span>
                        <span class="dashboard-legend-chip dashboard-legend-chip--muted">Ano anterior</span>
                    </div>
                </article>
            `;
        }).join("");
    }

    function renderTaxesSection(descriptionElement, container, section, compareYear) {
        if (descriptionElement) {
            descriptionElement.textContent = section?.description || "";
        }

        renderComparativeKpis(container, Array.isArray(section?.metrics) ? section.metrics : [], compareYear);
    }

    function renderTaxesExpenseTable(dashboard) {
        const rows = Array.isArray(dashboard?.expenseDetails) ? dashboard.expenseDetails : [];
        if (taxesExpenseResultsCount) {
            taxesExpenseResultsCount.textContent = `Mostrando ${numberFormatter.format(rows.length)} registros`;
        }

        if (!taxesExpenseBody) {
            return;
        }

        taxesExpenseBody.innerHTML = rows.length
            ? rows.map(row => `
                <tr>
                    <td>${escapeHtml(row.paymentDateDisplay)}</td>
                    <td class="text-end">${escapeHtml(currencyFormatter.format(Number(row.paymentValue || 0)))}</td>
                    <td class="text-end">${escapeHtml(currencyFormatter.format(Number(row.reteFuenteValue || 0)))}</td>
                    <td>${escapeHtml(row.recipientName)}</td>
                    <td>${escapeHtml(row.recipientNit)}</td>
                    <td class="text-end">${escapeHtml(currencyFormatter.format(Number(row.cloudValue || 0)))}</td>
                    <td class="text-end">${escapeHtml(currencyFormatter.format(Number(row.copiersValue || 0)))}</td>
                </tr>
            `).join("")
            : '<tr><td colspan="7" class="dashboard-table__empty">No hay gastos con retefuente en este periodo.</td></tr>';
    }

    function renderCopiersTable(dashboard) {
        const rows = Array.isArray(dashboard?.rows)
            ? [...dashboard.rows]
            : [];

        rows.sort((left, right) => {
            const leftDay = Number(left.billingDay || 0) > 0 ? Number(left.billingDay || 0) : Number.MAX_SAFE_INTEGER;
            const rightDay = Number(right.billingDay || 0) > 0 ? Number(right.billingDay || 0) : Number.MAX_SAFE_INTEGER;
            if (leftDay !== rightDay) {
                return leftDay - rightDay;
            }

            const clientCompare = normalizeText(left.clientName).localeCompare(normalizeText(right.clientName), "es");
            if (clientCompare !== 0) {
                return clientCompare;
            }

            return normalizeText(left.productName).localeCompare(normalizeText(right.productName), "es");
        });

        if (copiersResultsCount) {
            copiersResultsCount.textContent = `Mostrando ${numberFormatter.format(rows.length)} registros`;
        }

        if (!copiersBillingBody) {
            return;
        }

        copiersBillingBody.innerHTML = rows.length
            ? rows.map(row => `
                <tr>
                    <td>${escapeHtml(row.billingDayDisplay || "Sin dia")}</td>
                    <td>${escapeHtml(row.clientName)}</td>
                    <td>${escapeHtml(row.productName)}</td>
                    <td class="text-end">${escapeHtml(numberFormatter.format(Number(row.quantity || 0)))}</td>
                    <td class="text-end">${escapeHtml(numberFormatter.format(Number(row.includedOperations || 0)))}</td>
                    <td class="text-end">${escapeHtml(currencyFormatter.format(Number(row.unitValueBeforeVat || 0)))}</td>
                    <td class="text-end">${escapeHtml(currencyFormatter.format(Number(row.unitValueWithVat || 0)))}</td>
                    <td class="text-end">${escapeHtml(currencyFormatter.format(Number(row.totalWithVat || 0)))}</td>
                </tr>
            `).join("")
            : '<tr><td colspan="8" class="dashboard-table__empty">No hay registros de facturacion copiers disponibles.</td></tr>';
    }

    function renderPnlTable(dashboard) {
        if (!pnlTableContainer) {
            return;
        }

        const months = Array.isArray(dashboard?.months) ? dashboard.months : [];
        const rows = Array.isArray(dashboard?.rows) ? dashboard.rows : [];

        if (!months.length || !rows.length) {
            pnlTableContainer.innerHTML = `
                <div class="dashboard-table__empty">
                    <strong>${escapeHtml(dashboard?.emptyStateTitle || "No hay datos para el P&L.")}</strong><br />
                    <span>${escapeHtml(dashboard?.emptyStateMessage || "Sin movimientos en este corte.")}</span>
                </div>
            `;
            return;
        }

        const headerCells = months
            .map(month => `<th class="text-end">${escapeHtml(month.label)}</th>`)
            .join("");

        const bodyRows = rows.map(row => {
            if ((row.rowType || "").toLowerCase() === "section") {
                return `
                    <tr class="dashboard-pnl-row dashboard-pnl-row--section">
                        <td colspan="${months.length + 2}">${escapeHtml(row.label)}</td>
                    </tr>
                `;
            }

            const valueCells = (Array.isArray(row.values) ? row.values : [])
                .map((value, index) => {
                    const month = Number(months[index]?.month || index + 1);
                    return `
                        <td class="text-end">
                            <button
                                type="button"
                                class="dashboard-pnl-cell-btn"
                                data-pnl-row-key="${escapeHtml(row.key || "")}"
                                data-pnl-row-label="${escapeHtml(row.label || "")}"
                                data-pnl-cell-month="${month}">
                                ${escapeHtml(formatMetric(value, row.valueFormat))}
                            </button>
                        </td>
                    `;
                })
                .join("");

            return `
                <tr class="dashboard-pnl-row dashboard-pnl-row--${escapeHtml(row.rowType || "detail")}">
                    <td class="dashboard-pnl-row__label dashboard-pnl-row__label--level-${Number(row.level || 0)}">${escapeHtml(row.label)}</td>
                    ${valueCells}
                    <td class="text-end dashboard-pnl-row__total">
                        <button
                            type="button"
                            class="dashboard-pnl-cell-btn dashboard-pnl-cell-btn--total"
                            data-pnl-row-key="${escapeHtml(row.key || "")}"
                            data-pnl-row-label="${escapeHtml(row.label || "")}">
                            ${escapeHtml(formatMetric(row.total, row.valueFormat))}
                        </button>
                    </td>
                </tr>
            `;
        }).join("");

        pnlTableContainer.innerHTML = `
            <table class="table dashboard-table dashboard-pnl-table">
                <thead>
                    <tr>
                        <th>Cuenta</th>
                        ${headerCells}
                        <th class="text-end">Total YTD</th>
                    </tr>
                </thead>
                <tbody>${bodyRows}</tbody>
            </table>
        `;

        pnlTableContainer.querySelectorAll("[data-pnl-row-key]").forEach(button => {
            button.addEventListener("click", () => {
                loadPnlDetail(
                    button.dataset.pnlRowKey || "",
                    button.dataset.pnlRowLabel || "",
                    button.dataset.pnlCellMonth || null
                ).catch(() => {});
            });
        });
    }

    function getFilteredPortfolioRows() {
        const rows = Array.isArray(state.portfolioDashboard?.overdueInvoices)
            ? [...state.portfolioDashboard.overdueInvoices]
            : [];
        const searchTerm = normalizeText(state.portfolioSearchTerm);

        const filteredRows = !searchTerm
            ? rows
            : rows.filter(row => normalizeText(row.clientName).includes(searchTerm));

        filteredRows.sort((left, right) => {
            if (state.portfolioSort === "value") {
                const valueDiff = Number(right.totalInvoice || 0) - Number(left.totalInvoice || 0);
                if (valueDiff !== 0) {
                    return valueDiff;
                }

                return Number(right.ageDays || 0) - Number(left.ageDays || 0);
            }

            const ageDiff = Number(right.ageDays || 0) - Number(left.ageDays || 0);
            if (ageDiff !== 0) {
                return ageDiff;
            }

            return Number(right.totalInvoice || 0) - Number(left.totalInvoice || 0);
        });

        return filteredRows;
    }

    function renderOverdueTable(rows, tbody) {
        if (!tbody) {
            return;
        }

        tbody.innerHTML = rows.length
            ? rows.map(row => `
                <tr>
                    <td>${escapeHtml(row.invoiceNumber)}</td>
                    <td>${escapeHtml(row.clientName)}</td>
                    <td>${escapeHtml(row.verticalLabel)}</td>
                    <td>${escapeHtml(row.contractTypeLabel)}</td>
                    <td>${escapeHtml(row.dueDateDisplay)}</td>
                    <td><span class="dashboard-badge is-danger">${escapeHtml(numberFormatter.format(Number(row.ageDays || 0)))} dias</span></td>
                    <td class="text-end">${escapeHtml(currencyFormatter.format(Number(row.totalInvoice || 0)))}</td>
                </tr>
            `).join("")
            : '<tr><td colspan="7" class="dashboard-table__empty">No hay facturas vencidas sin pago en este momento.</td></tr>';
    }

    function renderPortfolioTable() {
        const allRows = Array.isArray(state.portfolioDashboard?.overdueInvoices)
            ? state.portfolioDashboard.overdueInvoices
            : [];
        const filteredRows = getFilteredPortfolioRows();

        if (portfolioResultsCount) {
            portfolioResultsCount.textContent = `Mostrando ${numberFormatter.format(filteredRows.length)} de ${numberFormatter.format(allRows.length)} registros`;
        }

        renderOverdueTable(filteredRows, portfolioUnpaidBody);
    }

    function updateHeroForBilling(dashboard) {
        compareLabel && (compareLabel.textContent = dashboard?.compareLabel || "Mismo periodo del ano anterior");
        granularityLabel && (granularityLabel.textContent = dashboard?.granularityLabel || "Mensual");
        recordCount && (recordCount.textContent = numberFormatter.format(Number(dashboard?.recordsCount || 0)));
    }

    function updateHeroForTaxes(dashboard) {
        compareLabel && (compareLabel.textContent = dashboard?.compareLabel || "Mismo periodo del ano anterior");
        granularityLabel && (granularityLabel.textContent = dashboard?.granularityLabel || "Mensual");
        recordCount && (recordCount.textContent = numberFormatter.format(Number(dashboard?.recordsCount || 0)));
    }

    function updateHeroForPortfolio(dashboard) {
        compareLabel && (compareLabel.textContent = dashboard?.asOfDateLabel ? `Corte al ${dashboard.asOfDateLabel}` : "Corte actual");
        granularityLabel && (granularityLabel.textContent = dashboard?.focusLabel || "Facturas vencidas sin pago");
        recordCount && (recordCount.textContent = numberFormatter.format(Number(dashboard?.recordsCount || 0)));
    }

    function updateHeroForCopiers(dashboard) {
        compareLabel && (compareLabel.textContent = dashboard?.asOfDateLabel ? `Corte al ${dashboard.asOfDateLabel}` : "Corte actual");
        granularityLabel && (granularityLabel.textContent = dashboard?.focusLabel || "Ordenado por dia de facturacion");
        recordCount && (recordCount.textContent = numberFormatter.format(Number(dashboard?.recordsCount || 0)));
    }

    function updateHeroForPnl(dashboard) {
        compareLabel && (compareLabel.textContent = dashboard?.monthCutoffLabel ? `Corte a ${dashboard.monthCutoffLabel} ${dashboard.year || ""}` : "Corte P&L");
        granularityLabel && (granularityLabel.textContent = dashboard?.focusLabel || "P&L mensual");
        recordCount && (recordCount.textContent = numberFormatter.format(Number(dashboard?.recordsCount || 0)));
    }

    function updateBillingContext(dashboard) {
        state.billingDashboard = dashboard;
        state.billingSignature = getPeriodSignature();
        periodLabel && (periodLabel.textContent = dashboard?.periodLabel || "Sin periodo");
        dateRangeLabel && (dateRangeLabel.textContent = dashboard?.dateRangeLabel || "-");

        if (state.activeTab === "billing") {
            updateHeroForBilling(dashboard);
        }
    }

    function updateTaxesContext(dashboard) {
        state.taxesDashboard = dashboard;
        state.taxesSignature = getPeriodSignature();
        periodLabel && (periodLabel.textContent = dashboard?.periodLabel || "Sin periodo");
        dateRangeLabel && (dateRangeLabel.textContent = dashboard?.dateRangeLabel || "-");

        if (state.activeTab === "taxes") {
            updateHeroForTaxes(dashboard);
        }
    }

    function updatePortfolioContext(dashboard) {
        state.portfolioDashboard = dashboard;
        portfolioAsOfLabel && (portfolioAsOfLabel.textContent = dashboard?.asOfDateLabel || "Sin corte");
        portfolioFocusLabel && (portfolioFocusLabel.textContent = dashboard?.focusLabel || "Facturas vencidas sin pago");

        if (state.activeTab === "portfolio") {
            updateHeroForPortfolio(dashboard);
        }
    }

    function updateCopiersContext(dashboard) {
        state.copiersDashboard = dashboard;
        copiersAsOfLabel && (copiersAsOfLabel.textContent = dashboard?.asOfDateLabel || "Sin corte");
        copiersFocusLabel && (copiersFocusLabel.textContent = dashboard?.focusLabel || "Ordenado por dia de facturacion");

        if (state.activeTab === "copiers") {
            updateHeroForCopiers(dashboard);
        }
    }

    function updatePnlContext(dashboard) {
        state.pnlDashboard = dashboard;
        state.pnlYear = Number(dashboard?.year || state.pnlYear);
        state.pnlMonth = Number(dashboard?.monthCutoff || state.pnlMonth || 1);
        state.pnlVertical = dashboard?.verticalKey || state.pnlVertical || "all";
        state.pnlSignature = getPnlSignature();
        pnlPeriodLabel && (pnlPeriodLabel.textContent = dashboard?.monthCutoffLabel ? `${dashboard.monthCutoffLabel} ${dashboard.year || ""}` : "Sin corte");
        pnlDateRangeLabel && (pnlDateRangeLabel.textContent = dashboard?.dateRangeLabel || "-");
        pnlDescription && (pnlDescription.textContent = dashboard?.description || "");

        if (pnlYearFilter) {
            pnlYearFilter.value = String(state.pnlYear);
        }

        if (pnlVerticalFilter) {
            pnlVerticalFilter.value = state.pnlVertical;
        }

        buildPnlMonthOptions(dashboard?.latestMonthAvailable || 12);

        if (state.activeTab === "pnl") {
            updateHeroForPnl(dashboard);
        }
    }

    function syncPeriodScopeVisibility() {
        if (dashboardPeriodScope) {
            dashboardPeriodScope.hidden = state.activeTab === "portfolio" || state.activeTab === "copiers" || state.activeTab === "pnl";
        }
    }

    function setActiveTab(tabKey) {
        state.activeTab = tabKey;
        syncPeriodScopeVisibility();

        if (tabKey !== "pnl" && isPnlDetailOpen()) {
            closePnlDetailModal();
        }

        tabButtons.forEach(button => {
            const isActive = button.dataset.dashboardTab === tabKey;
            button.classList.toggle("is-active", isActive);
            button.setAttribute("aria-selected", isActive ? "true" : "false");
        });

        tabPanels.forEach(panel => {
            const isActive = panel.dataset.dashboardPanel === tabKey;
            panel.classList.toggle("is-active", isActive);
            panel.hidden = !isActive;
        });

        if (tabKey === "pnl") {
            if (state.pnlDashboard && state.pnlSignature === getPnlSignature()) {
                updateHeroForPnl(state.pnlDashboard);
            } else {
                loadPnl();
            }
            return;
        }

        if (tabKey === "copiers") {
            if (state.copiersDashboard) {
                updateHeroForCopiers(state.copiersDashboard);
            } else {
                loadCopiers();
            }
            return;
        }

        if (tabKey === "portfolio") {
            if (state.portfolioDashboard) {
                updateHeroForPortfolio(state.portfolioDashboard);
            } else {
                loadPortfolio();
            }
            return;
        }

        if (tabKey === "taxes") {
            if (state.taxesDashboard && state.taxesSignature === getPeriodSignature()) {
                updateHeroForTaxes(state.taxesDashboard);
            } else {
                loadTaxes();
            }
            return;
        }

        if (state.billingDashboard && state.billingSignature === getPeriodSignature()) {
            updateHeroForBilling(state.billingDashboard);
        } else {
            loadBilling();
        }
    }

    function loadActivePeriodTab() {
        if (state.activeTab === "taxes") {
            loadTaxes();
            return;
        }

        loadBilling();
    }

    async function loadBilling() {
        setPeriodLoading(true);
        setStatus(billingStatusBanner, "info", "Actualizando tablero de facturacion...");

        try {
            const dashboard = await fetchJson(buildBillingUrl());
            updateBillingContext(dashboard);
            renderComparativeKpis(billingKpisContainer, dashboard?.kpis, dashboard?.compareYear);
            renderTrends(dashboard);
            setStatus(billingStatusBanner, "", "");
        } catch (error) {
            setStatus(billingStatusBanner, "error", error instanceof Error ? error.message : "No fue posible cargar el dashboard.");
        } finally {
            setPeriodLoading(false);
        }
    }

    async function loadTaxes() {
        setPeriodLoading(true);
        setStatus(taxesStatusBanner, "info", "Actualizando tablero de impuestos...");

        try {
            const dashboard = await fetchJson(buildTaxesUrl());
            updateTaxesContext(dashboard);
            renderTaxesSection(taxesReteFuenteDescription, taxesReteFuenteContainer, dashboard?.reteFuente, dashboard?.compareYear);
            renderTaxesSection(taxesReteIvaDescription, taxesReteIvaContainer, dashboard?.reteIva, dashboard?.compareYear);
            renderTaxesSection(taxesReteIcaDescription, taxesReteIcaContainer, dashboard?.reteIca, dashboard?.compareYear);
            renderTaxesExpenseTable(dashboard);
            setStatus(taxesStatusBanner, "", "");
        } catch (error) {
            setStatus(taxesStatusBanner, "error", error instanceof Error ? error.message : "No fue posible cargar el dashboard de impuestos.");
        } finally {
            setPeriodLoading(false);
        }
    }

    async function loadPortfolio() {
        setPortfolioLoading(true);
        setStatus(portfolioStatusBanner, "info", "Actualizando tablero de cartera...");

        try {
            const dashboard = await fetchJson(buildPortfolioUrl());
            updatePortfolioContext(dashboard);
            renderPortfolioKpis(dashboard);
            renderPortfolioTable();
            setStatus(portfolioStatusBanner, "", "");
        } catch (error) {
            setStatus(portfolioStatusBanner, "error", error instanceof Error ? error.message : "No fue posible cargar la cartera.");
        } finally {
            setPortfolioLoading(false);
        }
    }

    async function loadCopiers() {
        setCopiersLoading(true);
        setStatus(copiersStatusBanner, "info", "Actualizando facturacion copiers...");

        try {
            const dashboard = await fetchJson(buildCopiersUrl());
            updateCopiersContext(dashboard);
            renderCopiersKpis(dashboard);
            renderCopiersTable(dashboard);
            setStatus(copiersStatusBanner, "", "");
        } catch (error) {
            setStatus(copiersStatusBanner, "error", error instanceof Error ? error.message : "No fue posible cargar la facturacion copiers.");
        } finally {
            setCopiersLoading(false);
        }
    }

    async function loadPnl() {
        setPnlLoading(true);
        setStatus(pnlStatusBanner, "info", "Actualizando tablero P&L...");

        try {
            const dashboard = await fetchJson(buildPnlUrl());
            updatePnlContext(dashboard);
            renderPnlKpis(dashboard);
            renderPnlTable(dashboard);
            setStatus(pnlStatusBanner, "", "");
        } catch (error) {
            setStatus(pnlStatusBanner, "error", error instanceof Error ? error.message : "No fue posible cargar el dashboard P&L.");
        } finally {
            setPnlLoading(false);
        }
    }

    yearFilter?.addEventListener("change", () => {
        state.year = Number(yearFilter.value || currentYear);
        state.value = getDefaultValue(state.period, state.year);
        buildValueOptions();
        loadActivePeriodTab();
    });

    periodFilter?.addEventListener("change", () => {
        state.period = periodFilter.value || "month";
        state.value = getDefaultValue(state.period, state.year);
        buildValueOptions();
        loadActivePeriodTab();
    });

    valueFilter?.addEventListener("change", () => {
        state.value = Number(valueFilter.value || 1);
        loadActivePeriodTab();
    });

    refreshButton?.addEventListener("click", loadActivePeriodTab);
    portfolioRefreshButton?.addEventListener("click", loadPortfolio);
    copiersRefreshButton?.addEventListener("click", loadCopiers);
    pnlRefreshButton?.addEventListener("click", loadPnl);
    portfolioClientSearch?.addEventListener("input", () => {
        state.portfolioSearchTerm = portfolioClientSearch.value || "";
        renderPortfolioTable();
    });
    portfolioSortFilter?.addEventListener("change", () => {
        state.portfolioSort = portfolioSortFilter.value || "age";
        renderPortfolioTable();
    });
    pnlYearFilter?.addEventListener("change", () => {
        closePnlDetailModal();
        state.pnlYear = Number(pnlYearFilter.value || currentYear);
        state.pnlMonth = Math.min(state.pnlMonth || 1, 12);
        buildPnlMonthOptions(12);
        loadPnl();
    });
    pnlMonthFilter?.addEventListener("change", () => {
        closePnlDetailModal();
        state.pnlMonth = Number(pnlMonthFilter.value || 1);
        loadPnl();
    });
    pnlVerticalFilter?.addEventListener("change", () => {
        closePnlDetailModal();
        state.pnlVertical = pnlVerticalFilter.value || "all";
        loadPnl();
    });
    pnlDetailCloseBtn?.addEventListener("click", closePnlDetailModal);
    pnlDetailModal?.querySelectorAll("[data-pnl-detail-close]").forEach(element => {
        element.addEventListener("click", closePnlDetailModal);
    });
    pnlDetailBody?.addEventListener("click", event => {
        const saveButton = event.target.closest("[data-pnl-detail-save]");
        if (!saveButton) {
            return;
        }

        savePnlDetailRecord(saveButton);
    });
    document.addEventListener("keydown", event => {
        if (event.key === "Escape" && isPnlDetailOpen()) {
            closePnlDetailModal();
        }
    });

    tabButtons.forEach(button => {
        button.addEventListener("click", () => {
            const tabKey = button.dataset.dashboardTab || "billing";
            if (tabKey !== state.activeTab) {
                setActiveTab(tabKey);
            }
        });
    });

    buildYearOptions();
    buildPnlYearOptions();
    periodFilter && (periodFilter.value = state.period);
    portfolioSortFilter && (portfolioSortFilter.value = state.portfolioSort);
    pnlVerticalFilter && (pnlVerticalFilter.value = state.pnlVertical);
    buildValueOptions();
    buildPnlMonthOptions(12);
    syncPeriodScopeVisibility();
    loadBilling();
})();
