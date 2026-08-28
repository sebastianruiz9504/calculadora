(function () {
    const app = document.getElementById("licCruceApp");
    if (!app) {
        return;
    }

    const loadUrl = app.dataset.loadUrl || "";
    const exportUrl = app.dataset.exportUrl || "";
    const updateCostContractUrl = app.dataset.updateCostContractUrl || "";
    const updateBillingContractUrl = app.dataset.updateBillingContractUrl || "";
    const updateBillingVerticalUrl = app.dataset.updateBillingVerticalUrl || "";
    const updateCostAccountUrl = app.dataset.updateCostAccountUrl || "";
    const saveAccountMappingUrl = app.dataset.saveAccountMappingUrl || "";
    const searchAccountsUrl = app.dataset.searchAccountsUrl || "";
    const updateCostInvoiceDateUrl = app.dataset.updateCostInvoiceDateUrl || "";
    const filtersForm = document.getElementById("licCruceFilters");
    const yearInput = document.getElementById("licCruceYear");
    const monthSelect = document.getElementById("licCruceMonth");
    const periodModeSelect = document.getElementById("licCrucePeriodMode");
    const segmentSelect = document.getElementById("licCruceSegmentSelect");
    const downloadButton = document.getElementById("licCruceDownload");
    const status = document.getElementById("licCruceStatus");
    const matrixWrap = document.getElementById("licCruceMatrix");
    const matrixTitle = document.getElementById("licCruceMatrixTitle");
    const matrixSummary = document.getElementById("licCruceMatrixSummary");
    const periodLabel = document.getElementById("licCrucePeriodLabel");
    const negativeCount = document.getElementById("licCruceNegativeCount");
    const totalCost = document.getElementById("licCruceTotalCost");
    const totalBilling = document.getElementById("licCruceTotalBilling");
    const totalMargin = document.getElementById("licCruceTotalMargin");
    const totalMarginPct = document.getElementById("licCruceTotalMarginPct");
    const marginBreakdown = document.getElementById("licCruceMarginBreakdown");
    const totalTrendSummary = document.getElementById("licCruceTotalTrendSummary");
    const totalTrendChart = document.getElementById("licCruceTotalTrendChart");
    const clientTrendSearch = document.getElementById("licCruceClientTrendSearch");
    const clientTrendSelect = document.getElementById("licCruceClientTrendSelect");
    const clientTrendSummary = document.getElementById("licCruceClientTrendSummary");
    const clientTrendChart = document.getElementById("licCruceClientTrendChart");
    const clientMarginSearch = document.getElementById("licCruceClientMarginSearch");
    const clientMarginSelect = document.getElementById("licCruceClientMarginSelect");
    const clientMarginSummary = document.getElementById("licCruceClientMarginSummary");
    const clientMarginChart = document.getElementById("licCruceClientMarginChart");

    const chartColors = {
        cost: "#dc2626",
        billing: "#15803d",
        utilityPct: "#2563eb"
    };
    const clientLinePalette = [
        "#2563eb",
        "#16a34a",
        "#dc2626",
        "#9333ea",
        "#0891b2",
        "#ca8a04",
        "#be123c",
        "#4f46e5",
        "#0f766e",
        "#ea580c"
    ];

    const copFormatter = new Intl.NumberFormat("es-CO", {
        style: "currency",
        currency: "COP",
        minimumFractionDigits: 0,
        maximumFractionDigits: 0
    });
    const percentFormatter = new Intl.NumberFormat("es-CO", {
        minimumFractionDigits: 2,
        maximumFractionDigits: 2
    });
    const numberFormatter = new Intl.NumberFormat("es-CO", {
        minimumFractionDigits: 0,
        maximumFractionDigits: 0
    });

    let currentData = null;
    let currentSegments = [];
    let selectedSegmentKey = "";
    let detailDialog = null;
    let editDialog = null;
    let accountSearchTimer = null;
    let sortState = { key: "", direction: "" };
    let selectedClientTrendKeys = new Set();
    let selectedClientMarginKeys = new Set();
    let clientTrendSearchTerm = "";
    let clientMarginSearchTerm = "";

    yearInput.value = Number(app.dataset.defaultYear || 0) > 0 ? app.dataset.defaultYear : "";
    monthSelect.value = Number(app.dataset.defaultMonth || 0) > 0 ? app.dataset.defaultMonth : "";
    periodModeSelect.value = app.dataset.defaultPeriodMode || "month";

    filtersForm?.addEventListener("submit", async (event) => {
        event.preventDefault();
        await loadCruce();
    });

    segmentSelect?.addEventListener("change", () => {
        selectedSegmentKey = segmentSelect.value || "";
        renderSelectedSegment();
    });

    downloadButton?.addEventListener("click", () => {
        downloadCruce().catch((error) => {
            showStatus("error", error instanceof Error ? error.message : "No fue posible descargar el listado.");
        });
    });

    clientTrendSearch?.addEventListener("input", () => {
        clientTrendSearchTerm = clientTrendSearch.value || "";
        renderClientFilters();
    });

    clientTrendSelect?.addEventListener("change", () => {
        selectedClientTrendKeys = readUpdatedSelectedKeys(clientTrendSelect, selectedClientTrendKeys);
        renderSelectedCharts();
    });

    clientMarginSearch?.addEventListener("input", () => {
        clientMarginSearchTerm = clientMarginSearch.value || "";
        renderClientFilters();
    });

    clientMarginSelect?.addEventListener("change", () => {
        selectedClientMarginKeys = readUpdatedSelectedKeys(clientMarginSelect, selectedClientMarginKeys);
        renderSelectedCharts();
    });

    matrixWrap?.addEventListener("click", (event) => {
        const sortButton = event.target.closest("[data-sort-key]");
        if (sortButton) {
            handleSortClick(sortButton.dataset.sortKey || "");
            return;
        }

        const clientCell = event.target.closest("[data-map-client]");
        if (clientCell) {
            openClientEditDialog(clientCell.dataset.clientKey || "", clientCell.dataset.clientName || "");
            return;
        }

        const cell = event.target.closest("[data-detail-source]");
        if (!cell) {
            return;
        }

        openCellDetailDialog(cell.dataset.detailSource || "", cell.dataset.clientKey || "", cell.dataset.month || "", cell.dataset.clientName || "");
    });

    matrixWrap?.addEventListener("keydown", (event) => {
        if (event.key !== "Enter" && event.key !== " ") {
            return;
        }

        const clientCell = event.target.closest("[data-map-client]");
        if (clientCell) {
            event.preventDefault();
            openClientEditDialog(clientCell.dataset.clientKey || "", clientCell.dataset.clientName || "");
            return;
        }

        const cell = event.target.closest("[data-detail-source]");
        if (!cell) {
            return;
        }

        event.preventDefault();
        openCellDetailDialog(cell.dataset.detailSource || "", cell.dataset.clientKey || "", cell.dataset.month || "", cell.dataset.clientName || "");
    });

    document.addEventListener("keydown", (event) => {
        if (event.key === "Escape") {
            closeDetailDialog();
            closeEditDialog();
        }
    });

    loadCruce();

    async function loadCruce() {
        if (!loadUrl) {
            showStatus("error", "No se encontro la ruta de datos.");
            return;
        }

        showStatus("info", "Cargando cruce...");
        setBusy(true);
        try {
            const url = new URL(loadUrl, window.location.origin);
            url.searchParams.set("year", yearInput.value || "");
            url.searchParams.set("month", monthSelect.value || "");
            url.searchParams.set("periodMode", periodModeSelect.value || "month");

            const response = await fetch(url.toString(), {
                headers: { Accept: "application/json" }
            });
            const payload = await readJsonResponse(response);
            currentData = payload;
            syncFiltersFromPayload(payload);
            renderCruce(payload);
            showStatus("success", payload.message || "Cruce actualizado.");
        } catch (error) {
            currentData = null;
            renderCruce(null);
            showStatus("error", error instanceof Error ? error.message : "No fue posible cargar el cruce.");
        } finally {
            setBusy(false);
            if (currentData) {
                renderSegmentSelect();
            }
            updateDownloadButton();
        }
    }

    function syncFiltersFromPayload(data) {
        if (!data) {
            return;
        }

        if (Number(data.selectedYear || 0) > 0) {
            yearInput.value = String(data.selectedYear);
        }
        if (Number(data.selectedMonth || 0) > 0) {
            monthSelect.value = String(data.selectedMonth);
        }
        if (data.periodMode) {
            periodModeSelect.value = data.periodMode;
        }
    }

    function renderCruce(data) {
        currentSegments = Array.isArray(data?.matrixSegments) ? data.matrixSegments : [];
        renderSegmentSelect();
        renderSelectedSegment();
    }

    function renderSegmentSelect() {
        if (!segmentSelect) {
            return;
        }

        const visibleSegments = currentSegments.filter((segment) =>
            Number(segment.recordsCount || 0) > 0 || Number(segment.orphanCount || 0) > 0);
        const segments = visibleSegments.length > 0 ? visibleSegments : currentSegments;

        if (segments.length === 0) {
            segmentSelect.innerHTML = "";
            segmentSelect.disabled = true;
            selectedSegmentKey = "";
            return;
        }

        const selectedStillExists = segments.some((segment) => segment.key === selectedSegmentKey);
        selectedSegmentKey = selectedStillExists ? selectedSegmentKey : (segments[0]?.key || "");
        segmentSelect.innerHTML = segments.map((segment) => `
            <option value="${escapeHtml(segment.key || "")}">
                ${escapeHtml(segment.label || "Sin tipo")} (${numberFormatter.format(Number(segment.recordsCount || 0))})
            </option>
        `).join("");
        segmentSelect.value = selectedSegmentKey;
        segmentSelect.disabled = segments.length <= 1;
    }

    function renderSelectedSegment() {
        const segment = getSelectedSegment();
        const months = Array.isArray(currentData?.matrixMonths) ? currentData.matrixMonths : [];

        if (!segment || months.length === 0) {
            renderTotals({});
            renderSelectedCharts();
            if (matrixTitle) {
                matrixTitle.textContent = "Sin datos";
            }
            if (matrixSummary) {
                matrixSummary.textContent = "";
            }
            if (matrixWrap) {
                matrixWrap.innerHTML = "<div class=\"licx-empty\">No hay registros para este periodo.</div>";
            }
            updateDownloadButton();
            return;
        }

        selectedSegmentKey = segment.key || selectedSegmentKey;
        if (matrixTitle) {
            matrixTitle.textContent = segment.label || "Sin tipo";
        }
        if (periodLabel) {
            periodLabel.textContent = currentData?.periodLabel || "-";
        }
        if (matrixSummary) {
            matrixSummary.textContent = `${numberFormatter.format(Number(segment.recordsCount || 0))} cliente(s)`;
        }
        renderTotals(segment.totals || {});
        if (negativeCount) {
            negativeCount.textContent = numberFormatter.format(Number(segment.negativeMarginCount || 0));
            negativeCount.classList.toggle("is-negative", Number(segment.negativeMarginCount || 0) > 0);
        }
        renderMatrix(segment, months);
        renderSelectedCharts();
        updateDownloadButton();
    }

    function getSelectedSegment() {
        return currentSegments.find((item) => item.key === selectedSegmentKey)
            || currentSegments.find((item) => Number(item.recordsCount || 0) > 0)
            || currentSegments[0]
            || null;
    }

    function renderTotals(totals) {
        const margin = Number(totals.margenBrutoTotal || 0);
        const marginPct = totals.margenBrutoPct;
        totalCost.textContent = formatCurrency(totals.totalCostosLicenciamiento);
        totalBilling.textContent = formatCurrency(totals.totalFacturacionRelacionada);
        totalMargin.textContent = formatCurrency(margin);
        totalMargin.classList.toggle("is-negative", margin < 0);
        totalMarginPct.textContent = formatPercent(marginPct);
        totalMarginPct.classList.toggle("is-negative", Number(marginPct || 0) < 0);
        if (marginBreakdown) {
            marginBreakdown.innerHTML = `
                <span class="is-positive">+ ${formatCurrency(totals.totalUtilidadPositiva)}</span>
                <span class="is-negative">- ${formatCurrency(Math.abs(Number(totals.totalUtilidadNegativa || 0)))}</span>
            `;
        }
    }

    function renderMatrix(segment, months) {
        const rows = getSortedRows(Array.isArray(segment.rows) ? segment.rows : []);
        if (!matrixWrap) {
            return;
        }

        if (rows.length === 0) {
            matrixWrap.innerHTML = "<div class=\"licx-empty\">No hay clientes en este tipo de contrato.</div>";
            return;
        }

        const showAccumulated = (currentData?.periodMode || "month") !== "month" && months.length > 1;
        matrixWrap.innerHTML = `
            <div class="licx-table-wrap">
                <table class="table align-middle licx-table licx-matrix-table">
                    <thead>
                        <tr>
                            ${buildSortableHeader("client", "Cliente", "licx-client-col", "rowspan=\"2\"")}
                            ${months.map((month) => `<th colspan="4" class="text-center licx-month-head">${escapeHtml(month.label || month.key || "")}</th>`).join("")}
                            ${showAccumulated ? buildSortableHeader("totalUtility", "Utilidad acumulada", "text-end licx-accum-col", "rowspan=\"2\"") : ""}
                        </tr>
                        <tr>
                            ${months.map((month) => `
                                ${buildSortableHeader(`cell:${month.key || ""}:cost`, "Costo", "text-end")}
                                ${buildSortableHeader(`cell:${month.key || ""}:billing`, "Venta", "text-end")}
                                ${buildSortableHeader(`cell:${month.key || ""}:pct`, "% utilidad", "text-end")}
                                ${buildSortableHeader(`cell:${month.key || ""}:utility`, "Utilidad", "text-end")}
                            `).join("")}
                        </tr>
                    </thead>
                    <tbody>
                        ${rows.map((row) => buildMatrixRow(row, months, showAccumulated)).join("")}
                    </tbody>
                </table>
            </div>
        `;
    }

    function renderSelectedCharts() {
        const chartSegment = getSelectedChartSegment();
        const months = getChartMonths();

        if (!chartSegment || months.length === 0) {
            renderClientFilters([]);
            renderEmptyChart(totalTrendChart, "No hay datos desde enero 2026 para graficar.");
            renderEmptyChart(clientTrendChart, "No hay datos desde enero 2026 para graficar.");
            renderEmptyChart(clientMarginChart, "Selecciona uno o mas clientes para visualizar la evolucion de utilidad.");
            setText(totalTrendSummary, "");
            setText(clientTrendSummary, "");
            setText(clientMarginSummary, "");
            return;
        }

        const clientRows = getChartClientRows(chartSegment);
        pruneSelectedKeys(selectedClientTrendKeys, clientRows);
        pruneSelectedKeys(selectedClientMarginKeys, clientRows);
        renderClientFilters(clientRows);

        if (clientRows.length === 0) {
            renderEmptyChart(totalTrendChart, "No hay clientes en este tipo para graficar.");
            renderEmptyChart(clientTrendChart, "No hay clientes en este tipo para graficar.");
            renderEmptyChart(clientMarginChart, "Selecciona uno o mas clientes para visualizar la evolucion de utilidad.");
            setText(totalTrendSummary, "");
            setText(clientTrendSummary, "");
            setText(clientMarginSummary, "");
            return;
        }

        const totalMonthlyData = buildMonthlyTotals(clientRows, months);
        renderComboChart(totalTrendChart, totalMonthlyData, {
            title: "Total",
            selectedClientsLabel: "Todos los clientes",
            emptyMessage: "No hay datos desde enero 2026 para graficar."
        });
        setText(totalTrendSummary, `${formatMonthRangeLabel(months)} | ${numberFormatter.format(clientRows.length)} cliente(s)`);

        const trendRows = selectedClientTrendKeys.size > 0
            ? clientRows.filter((row) => selectedClientTrendKeys.has(row.rowKey || ""))
            : clientRows;
        const trendLabel = selectedClientTrendKeys.size > 0
            ? summarizeSelectedClients(trendRows)
            : "Todos los clientes";
        renderComboChart(clientTrendChart, buildMonthlyTotals(trendRows, months), {
            title: "Cliente(s)",
            selectedClientsLabel: trendLabel,
            emptyMessage: "Selecciona uno o mas clientes para visualizar la evolucion mensual."
        });
        setText(clientTrendSummary, `${trendLabel} | ${numberFormatter.format(trendRows.length)} cliente(s)`);

        const marginRows = selectedClientMarginKeys.size > 0
            ? clientRows.filter((row) => selectedClientMarginKeys.has(row.rowKey || ""))
            : getTopClientRowsByBilling(clientRows, 10);
        const visibleMarginRows = marginRows.slice(0, 10);
        const marginLabel = selectedClientMarginKeys.size > 0
            ? summarizeSelectedClients(visibleMarginRows)
            : "Top 10 por venta sin IVA";
        const marginWarning = marginRows.length > 10
            ? "Para mejor lectura, selecciona maximo 10 clientes."
            : "";
        renderClientMarginChart(clientMarginChart, visibleMarginRows, months, {
            emptyMessage: "Selecciona uno o mas clientes para visualizar la evolucion de utilidad.",
            warning: marginWarning
        });
        setText(clientMarginSummary, `${marginLabel} | ${numberFormatter.format(visibleMarginRows.length)} linea(s)`);
    }

    function renderClientFilters(clientRows) {
        const rows = Array.isArray(clientRows)
            ? clientRows
            : getChartClientRows(getSelectedChartSegment());
        renderClientSelectOptions(clientTrendSelect, rows, selectedClientTrendKeys, clientTrendSearchTerm);
        renderClientSelectOptions(clientMarginSelect, rows, selectedClientMarginKeys, clientMarginSearchTerm);
    }

    function getSelectedChartSegment() {
        const chartSegments = Array.isArray(currentData?.chartMatrixSegments)
            ? currentData.chartMatrixSegments
            : [];
        return chartSegments.find((item) => item.key === selectedSegmentKey)
            || chartSegments.find((item) => Number(item.recordsCount || 0) > 0)
            || chartSegments[0]
            || null;
    }

    function getChartMonths() {
        const chartMonths = Array.isArray(currentData?.chartMatrixMonths)
            ? currentData.chartMatrixMonths
            : [];
        return chartMonths.length > 0
            ? chartMonths
            : (Array.isArray(currentData?.matrixMonths) ? currentData.matrixMonths : []);
    }

    function getChartClientRows(segment) {
        return (Array.isArray(segment?.rows) ? segment.rows : [])
            .filter((row) => row && row.rowKey)
            .slice()
            .sort((left, right) => (left.cliente || "").localeCompare(right.cliente || "", "es", { sensitivity: "base" }));
    }

    function renderClientSelectOptions(select, rows, selectedKeys, searchTerm) {
        if (!select) {
            return;
        }

        const term = normalizeSearchText(searchTerm);
        const filteredRows = rows.filter((row) =>
            !term
            || selectedKeys.has(row.rowKey || "")
            || normalizeSearchText(buildClientSearchText(row)).includes(term));

        select.innerHTML = filteredRows.map((row) => {
            const key = row.rowKey || "";
            const meta = row.nitCliente || row.clienteId || row.grupoEmpresarialId || "";
            return `
                <option value="${escapeHtml(key)}" ${selectedKeys.has(key) ? "selected" : ""}>
                    ${escapeHtml(row.cliente || "Cliente sin nombre")}${meta ? ` - ${escapeHtml(meta)}` : ""}
                </option>
            `;
        }).join("");
        select.disabled = rows.length === 0;
    }

    function readUpdatedSelectedKeys(select, previousKeys) {
        const visibleKeys = new Set([...select.options].map((option) => option.value));
        const next = new Set([...previousKeys].filter((key) => !visibleKeys.has(key)));
        [...select.selectedOptions].forEach((option) => {
            if (option.value) {
                next.add(option.value);
            }
        });
        return next;
    }

    function pruneSelectedKeys(selectedKeys, rows) {
        const availableKeys = new Set(rows.map((row) => row.rowKey || ""));
        [...selectedKeys].forEach((key) => {
            if (!availableKeys.has(key)) {
                selectedKeys.delete(key);
            }
        });
    }

    function buildMonthlyTotals(rows, months) {
        return months.map((month) => {
            let cost = 0;
            let billing = 0;
            let negativeClients = 0;
            for (const row of rows) {
                const cell = getCellForMonth(row, month.key || "");
                const cellCost = Number(cell?.costoLicenciamiento || 0);
                const cellBilling = Number(cell?.facturacionSinIva || 0);
                cost += Number.isFinite(cellCost) ? cellCost : 0;
                billing += Number.isFinite(cellBilling) ? cellBilling : 0;
                if ((Number(cell?.utilidadValor ?? (cellBilling - cellCost)) || 0) < 0) {
                    negativeClients++;
                }
            }

            const utility = billing - cost;
            return {
                key: month.key || "",
                label: month.label || month.key || "",
                cost,
                billing,
                utility,
                utilityPct: calculateUtilityPct(cost, billing),
                negativeClients,
                totalClients: rows.length
            };
        });
    }

    function renderComboChart(container, data, options) {
        if (!container) {
            return;
        }

        const rows = Array.isArray(data) ? data : [];
        const hasValues = rows.some((item) =>
            Math.abs(Number(item.cost || 0)) >= 0.01
            || Math.abs(Number(item.billing || 0)) >= 0.01);
        if (!hasValues) {
            renderEmptyChart(container, options?.emptyMessage || "No hay datos para graficar.");
            return;
        }

        const margin = { top: 28, right: 78, bottom: 74, left: 86 };
        const width = Math.max(820, rows.length * 78 + margin.left + margin.right);
        const height = 360;
        const plotWidth = width - margin.left - margin.right;
        const plotHeight = height - margin.top - margin.bottom;
        const xStep = plotWidth / Math.max(rows.length, 1);
        const moneyMax = niceMax(Math.max(...rows.flatMap((item) => [Number(item.cost || 0), Number(item.billing || 0)]), 1));
        const pctValues = rows.map((item) => item.utilityPct).filter((value) => value !== null && Number.isFinite(Number(value))).map(Number);
        const pctDomain = buildPercentDomain(pctValues);
        const yMoney = (value) => margin.top + plotHeight - ((Number(value || 0) / moneyMax) * plotHeight);
        const yPct = (value) => margin.top + plotHeight - (((Number(value) - pctDomain.min) / (pctDomain.max - pctDomain.min)) * plotHeight);
        const yBottom = margin.top + plotHeight;
        const barWidth = Math.max(8, Math.min(18, xStep / 4.2));
        const linePoints = rows
            .map((item, index) => ({
                item,
                x: margin.left + (index * xStep) + (xStep / 2),
                y: item.utilityPct === null ? null : yPct(item.utilityPct)
            }));
        const linePaths = buildLinePaths(linePoints);

        const gridLines = buildGridLines(5, margin, plotWidth, plotHeight, moneyMax, yMoney);
        const pctLabels = buildRightAxisLabels(5, margin, plotWidth, plotHeight, pctDomain, yPct);
        const bars = rows.map((item, index) => {
            const centerX = margin.left + (index * xStep) + (xStep / 2);
            const costY = yMoney(item.cost);
            const billingY = yMoney(item.billing);
            const title = buildComboTooltip(item, options?.selectedClientsLabel || "");
            return `
                <g>
                    <rect x="${round(centerX - barWidth - 2)}" y="${round(costY)}" width="${round(barWidth)}" height="${round(Math.max(yBottom - costY, 1))}" fill="${chartColors.cost}">
                        ${title}
                    </rect>
                    <rect x="${round(centerX + 2)}" y="${round(billingY)}" width="${round(barWidth)}" height="${round(Math.max(yBottom - billingY, 1))}" fill="${chartColors.billing}">
                        ${title}
                    </rect>
                    <rect x="${round(centerX - (xStep / 2))}" y="${margin.top}" width="${round(xStep)}" height="${plotHeight}" fill="transparent">
                        ${title}
                    </rect>
                    <text class="licx-chart-label" x="${round(centerX)}" y="${height - 42}" text-anchor="middle">${escapeHtml(item.label || item.key || "")}</text>
                </g>
            `;
        }).join("");
        const line = linePaths.map((path) => `<path d="${path}" fill="none" stroke="${chartColors.utilityPct}" stroke-width="3" stroke-linecap="round" stroke-linejoin="round"></path>`).join("");
        const points = linePoints
            .filter((point) => point.y !== null)
            .map((point) => `
                <circle cx="${round(point.x)}" cy="${round(point.y)}" r="4" fill="#ffffff" stroke="${chartColors.utilityPct}" stroke-width="2">
                    ${buildComboTooltip(point.item, options?.selectedClientsLabel || "")}
                </circle>
            `).join("");

        container.innerHTML = `
            ${buildComboLegend()}
            <div class="licx-chart-scroll">
                <svg viewBox="0 0 ${width} ${height}" width="${width}" height="${height}" aria-hidden="true">
                    ${gridLines}
                    <line class="licx-chart-axis" x1="${margin.left}" y1="${yBottom}" x2="${width - margin.right}" y2="${yBottom}"></line>
                    <line class="licx-chart-axis" x1="${margin.left}" y1="${margin.top}" x2="${margin.left}" y2="${yBottom}"></line>
                    <line class="licx-chart-axis" x1="${width - margin.right}" y1="${margin.top}" x2="${width - margin.right}" y2="${yBottom}"></line>
                    <text class="licx-chart-title" x="${margin.left}" y="16">${escapeHtml(options?.title || "")}</text>
                    <text class="licx-chart-title" x="${margin.left}" y="${height - 8}">Mes consumo</text>
                    <text class="licx-chart-title" x="${width - margin.right}" y="16" text-anchor="end">Utilidad %</text>
                    ${pctLabels}
                    ${bars}
                    ${line}
                    ${points}
                </svg>
            </div>
        `;
    }

    function renderClientMarginChart(container, rows, months, options) {
        if (!container) {
            return;
        }

        const series = rows.map((row, index) => ({
            key: row.rowKey || `client-${index}`,
            label: row.cliente || "Cliente sin nombre",
            color: clientLinePalette[index % clientLinePalette.length],
            points: months.map((month) => {
                const cell = getCellForMonth(row, month.key || "");
                const cost = Number(cell?.costoLicenciamiento || 0);
                const billing = Number(cell?.facturacionSinIva || 0);
                const utility = billing - cost;
                return {
                    key: month.key || "",
                    label: month.label || month.key || "",
                    cost,
                    billing,
                    utility,
                    utilityPct: calculateUtilityPct(cost, billing)
                };
            })
        }));
        const percentValues = series
            .flatMap((item) => item.points.map((point) => point.utilityPct))
            .filter((value) => value !== null && Number.isFinite(Number(value)))
            .map(Number);

        if (series.length === 0 || percentValues.length === 0) {
            renderEmptyChart(container, options?.emptyMessage || "No hay datos para graficar.");
            return;
        }

        const margin = { top: 28, right: 42, bottom: 74, left: 72 };
        const width = Math.max(820, months.length * 78 + margin.left + margin.right);
        const height = 360;
        const plotWidth = width - margin.left - margin.right;
        const plotHeight = height - margin.top - margin.bottom;
        const xStep = plotWidth / Math.max(months.length, 1);
        const pctDomain = buildPercentDomain(percentValues);
        const yPct = (value) => margin.top + plotHeight - (((Number(value) - pctDomain.min) / (pctDomain.max - pctDomain.min)) * plotHeight);
        const xForIndex = (index) => margin.left + (index * xStep) + (xStep / 2);
        const yBottom = margin.top + plotHeight;
        const gridLines = buildPercentGridLines(5, margin, plotWidth, plotHeight, pctDomain, yPct);
        const xLabels = months.map((month, index) => `
            <text class="licx-chart-label" x="${round(xForIndex(index))}" y="${height - 42}" text-anchor="middle">${escapeHtml(month.label || month.key || "")}</text>
        `).join("");
        const lineSvg = series.map((item) => {
            const points = item.points.map((point, index) => ({
                item: point,
                x: xForIndex(index),
                y: point.utilityPct === null ? null : yPct(point.utilityPct)
            }));
            const paths = buildLinePaths(points)
                .map((path) => `<path d="${path}" fill="none" stroke="${item.color}" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round"></path>`)
                .join("");
            const circles = points
                .filter((point) => point.y !== null)
                .map((point) => `
                    <circle cx="${round(point.x)}" cy="${round(point.y)}" r="3.8" fill="#ffffff" stroke="${item.color}" stroke-width="2">
                        ${buildClientMarginTooltip(item.label, point.item)}
                    </circle>
                `).join("");
            return `<g>${paths}${circles}</g>`;
        }).join("");

        container.innerHTML = `
            ${options?.warning ? `<div class="licx-chart-warning">${escapeHtml(options.warning)}</div>` : ""}
            ${buildLineLegend(series)}
            <div class="licx-chart-scroll">
                <svg viewBox="0 0 ${width} ${height}" width="${width}" height="${height}" aria-hidden="true">
                    ${gridLines}
                    <line class="licx-chart-axis" x1="${margin.left}" y1="${yBottom}" x2="${width - margin.right}" y2="${yBottom}"></line>
                    <line class="licx-chart-axis" x1="${margin.left}" y1="${margin.top}" x2="${margin.left}" y2="${yBottom}"></line>
                    <text class="licx-chart-title" x="${margin.left}" y="16">Utilidad %</text>
                    <text class="licx-chart-title" x="${margin.left}" y="${height - 8}">Mes consumo</text>
                    ${xLabels}
                    ${lineSvg}
                </svg>
            </div>
        `;
    }

    function buildGridLines(count, margin, plotWidth, plotHeight, moneyMax, yMoney) {
        const lines = [];
        for (let index = 0; index <= count; index++) {
            const value = (moneyMax / count) * index;
            const y = yMoney(value);
            lines.push(`
                <line class="licx-chart-grid" x1="${margin.left}" y1="${round(y)}" x2="${margin.left + plotWidth}" y2="${round(y)}"></line>
                <text class="licx-chart-label" x="${margin.left - 10}" y="${round(y + 4)}" text-anchor="end">${escapeHtml(formatCompactCurrency(value))}</text>
            `);
        }
        return lines.join("");
    }

    function buildRightAxisLabels(count, margin, plotWidth, plotHeight, domain, yPct) {
        const lines = [];
        for (let index = 0; index <= count; index++) {
            const value = domain.min + (((domain.max - domain.min) / count) * index);
            const y = yPct(value);
            lines.push(`<text class="licx-chart-label" x="${margin.left + plotWidth + 10}" y="${round(y + 4)}">${escapeHtml(formatPercent(value))}</text>`);
        }
        return lines.join("");
    }

    function buildPercentGridLines(count, margin, plotWidth, plotHeight, domain, yPct) {
        const lines = [];
        for (let index = 0; index <= count; index++) {
            const value = domain.min + (((domain.max - domain.min) / count) * index);
            const y = yPct(value);
            lines.push(`
                <line class="licx-chart-grid" x1="${margin.left}" y1="${round(y)}" x2="${margin.left + plotWidth}" y2="${round(y)}"></line>
                <text class="licx-chart-label" x="${margin.left - 10}" y="${round(y + 4)}" text-anchor="end">${escapeHtml(formatPercent(value))}</text>
            `);
        }
        return lines.join("");
    }

    function buildComboTooltip(item, selectedClientsLabel) {
        return buildSvgTitle([
            `Mes: ${item.label || item.key || "-"}`,
            selectedClientsLabel ? `Clientes seleccionados: ${selectedClientsLabel}` : "",
            `Costo total: ${formatCurrency(item.cost)}`,
            `Venta sin IVA total: ${formatCurrency(item.billing)}`,
            `Utilidad nominal: ${formatCurrency(item.utility)}`,
            `Utilidad %: ${formatPercent(item.utilityPct)}`,
            `Clientes con margen negativo: ${numberFormatter.format(Number(item.negativeClients || 0))}`,
            `Total clientes: ${numberFormatter.format(Number(item.totalClients || 0))}`
        ].filter(Boolean));
    }

    function buildClientMarginTooltip(clientName, point) {
        const state = Math.abs(Number(point.billing || 0)) < 0.01
            ? "sin venta"
            : Number(point.utility || 0) < 0
                ? "negativo"
                : "positivo";
        return buildSvgTitle([
            `Cliente: ${clientName || "Cliente sin nombre"}`,
            `Mes: ${point.label || point.key || "-"}`,
            `Costo: ${formatCurrency(point.cost)}`,
            `Venta sin IVA: ${formatCurrency(point.billing)}`,
            `Utilidad nominal: ${formatCurrency(point.utility)}`,
            `Utilidad %: ${formatPercent(point.utilityPct)}`,
            `Estado margen: ${state}`
        ]);
    }

    function buildComboLegend() {
        return `
            <div class="licx-chart-legend">
                <span><i class="licx-chart-swatch" style="--licx-swatch:${chartColors.cost}"></i>Costo</span>
                <span><i class="licx-chart-swatch" style="--licx-swatch:${chartColors.billing}"></i>Venta sin IVA</span>
                <span><i class="licx-chart-swatch" style="--licx-swatch:${chartColors.utilityPct}"></i>Utilidad %</span>
            </div>
        `;
    }

    function buildLineLegend(series) {
        return `
            <div class="licx-chart-legend">
                ${series.map((item) => `
                    <span><i class="licx-chart-swatch" style="--licx-swatch:${item.color}"></i>${escapeHtml(item.label)}</span>
                `).join("")}
            </div>
        `;
    }

    function buildLinePaths(points) {
        const paths = [];
        let current = [];
        for (const point of points) {
            if (point.y === null || point.y === undefined || !Number.isFinite(point.y)) {
                if (current.length > 0) {
                    paths.push(current);
                    current = [];
                }
                continue;
            }
            current.push(point);
        }
        if (current.length > 0) {
            paths.push(current);
        }

        return paths.map((segment) => segment
            .map((point, index) => `${index === 0 ? "M" : "L"} ${round(point.x)} ${round(point.y)}`)
            .join(" "));
    }

    function getCellForMonth(row, monthKey) {
        return (Array.isArray(row?.cells) ? row.cells : [])
            .find((cell) => (cell.mes || "") === monthKey) || null;
    }

    function getTopClientRowsByBilling(rows, limit) {
        return rows
            .slice()
            .sort((left, right) => sumClientBilling(right) - sumClientBilling(left))
            .slice(0, limit);
    }

    function sumClientBilling(row) {
        return (Array.isArray(row?.cells) ? row.cells : [])
            .reduce((total, cell) => total + Number(cell.facturacionSinIva || 0), 0);
    }

    function calculateUtilityPct(cost, billing) {
        const billingNumber = Number(billing || 0);
        if (Math.abs(billingNumber) < 0.01) {
            return null;
        }

        return (1 - (Number(cost || 0) / billingNumber)) * 100;
    }

    function buildPercentDomain(values) {
        const numericValues = values.filter((value) => Number.isFinite(Number(value))).map(Number);
        if (numericValues.length === 0) {
            return { min: 0, max: 100 };
        }

        const rawMin = Math.min(0, ...numericValues);
        const rawMax = Math.max(100, ...numericValues);
        const padding = Math.max((rawMax - rawMin) * 0.08, 5);
        const min = Math.floor((rawMin - padding) / 10) * 10;
        const max = Math.ceil((rawMax + padding) / 10) * 10;
        return max === min ? { min: min - 10, max: max + 10 } : { min, max };
    }

    function niceMax(value) {
        const safeValue = Math.max(Number(value || 0), 1);
        const exponent = Math.floor(Math.log10(safeValue));
        const base = Math.pow(10, exponent);
        return Math.ceil(safeValue / base) * base;
    }

    function formatCompactCurrency(value) {
        const numberValue = Number(value || 0);
        if (Math.abs(numberValue) >= 1000000000) {
            return `$ ${percentFormatter.format(numberValue / 1000000000)}B`;
        }
        if (Math.abs(numberValue) >= 1000000) {
            return `$ ${percentFormatter.format(numberValue / 1000000)}M`;
        }
        if (Math.abs(numberValue) >= 1000) {
            return `$ ${percentFormatter.format(numberValue / 1000)}K`;
        }
        return formatCurrency(numberValue);
    }

    function summarizeSelectedClients(rows) {
        if (!Array.isArray(rows) || rows.length === 0) {
            return "Sin clientes";
        }
        if (rows.length <= 3) {
            return rows.map((row) => row.cliente || "Cliente sin nombre").join(", ");
        }
        return `${numberFormatter.format(rows.length)} clientes`;
    }

    function formatMonthRangeLabel(months) {
        if (!Array.isArray(months) || months.length === 0) {
            return "";
        }
        const first = months[0]?.label || months[0]?.key || "";
        const last = months[months.length - 1]?.label || months[months.length - 1]?.key || "";
        return first === last ? first : `${first} a ${last}`;
    }

    function buildClientSearchText(row) {
        return [
            row?.cliente,
            row?.nitCliente,
            row?.clienteId,
            row?.grupoEmpresarial,
            row?.grupoEmpresarialId
        ].filter(Boolean).join(" ");
    }

    function normalizeSearchText(value) {
        return (value || "")
            .toString()
            .normalize("NFD")
            .replace(/[\u0300-\u036f]/g, "")
            .toLowerCase()
            .trim();
    }

    function renderEmptyChart(container, message) {
        if (!container) {
            return;
        }

        container.innerHTML = `<div class="licx-empty">${escapeHtml(message || "No hay datos para mostrar.")}</div>`;
    }

    function buildSvgTitle(lines) {
        return `<title>${escapeHtml((lines || []).join("\n"))}</title>`;
    }

    function setText(element, value) {
        if (element) {
            element.textContent = value || "";
        }
    }

    function round(value) {
        return Math.round(Number(value || 0) * 100) / 100;
    }

    function buildSortableHeader(sortKey, label, classes = "", attrs = "") {
        const isActive = sortState.key === sortKey && sortState.direction;
        const indicator = isActive ? (sortState.direction === "asc" ? "↑" : "↓") : "";
        const ariaSort = isActive
            ? (sortState.direction === "asc" ? "ascending" : "descending")
            : "none";

        return `
            <th ${attrs} class="${classes}" aria-sort="${ariaSort}">
                <button type="button" class="licx-sort-button ${isActive ? "is-active" : ""}" data-sort-key="${escapeHtml(sortKey)}">
                    <span>${escapeHtml(label)}</span>
                    <span class="licx-sort-indicator" aria-hidden="true">${indicator}</span>
                </button>
            </th>
        `;
    }

    function handleSortClick(sortKey) {
        if (!sortKey) {
            return;
        }

        if (sortState.key !== sortKey) {
            sortState = { key: sortKey, direction: "asc" };
        } else if (sortState.direction === "asc") {
            sortState = { key: sortKey, direction: "desc" };
        } else {
            sortState = { key: "", direction: "" };
        }

        renderSelectedSegment();
    }

    function getSortedRows(rows) {
        if (!sortState.key || !sortState.direction) {
            return rows;
        }

        const descending = sortState.direction === "desc";
        return rows
            .map((row, index) => ({ row, index }))
            .sort((left, right) => {
                const result = compareRows(left.row, right.row, sortState.key, descending);
                return result !== 0 ? result : left.index - right.index;
            })
            .map((item) => item.row);
    }

    function compareRows(left, right, sortKey, descending) {
        if (sortKey === "client") {
            const leftText = (left.cliente || "").toString();
            const rightText = (right.cliente || "").toString();
            const result = leftText.localeCompare(rightText, "es", { sensitivity: "base" });
            return descending ? -result : result;
        }

        return compareNullableNumbers(
            resolveSortValue(left, sortKey),
            resolveSortValue(right, sortKey),
            descending);
    }

    function resolveSortValue(row, sortKey) {
        if (sortKey === "totalCost") {
            return toNumberOrNull(row.totalCostoLicenciamiento);
        }
        if (sortKey === "totalBilling") {
            return toNumberOrNull(row.totalFacturacionSinIva);
        }
        if (sortKey === "totalPct") {
            return toNumberOrNull(row.totalUtilidadPct);
        }
        if (sortKey === "totalUtility") {
            return toNumberOrNull(row.totalUtilidad);
        }
        if (!sortKey.startsWith("cell:")) {
            return null;
        }

        const parts = sortKey.split(":");
        if (parts.length !== 3) {
            return null;
        }

        const cell = (Array.isArray(row.cells) ? row.cells : [])
            .find((item) => (item.mes || "") === parts[1]);
        if (!cell) {
            return parts[2] === "pct" ? null : 0;
        }

        if (parts[2] === "cost") {
            return toNumberOrNull(cell.costoLicenciamiento);
        }
        if (parts[2] === "billing") {
            return toNumberOrNull(cell.facturacionSinIva);
        }
        if (parts[2] === "pct") {
            return toNumberOrNull(cell.utilidadPct);
        }
        if (parts[2] === "utility") {
            return toNumberOrNull(cell.utilidadValor);
        }

        return null;
    }

    function compareNullableNumbers(left, right, descending) {
        const leftMissing = left === null || left === undefined || Number.isNaN(left);
        const rightMissing = right === null || right === undefined || Number.isNaN(right);
        if (leftMissing && rightMissing) {
            return 0;
        }
        if (leftMissing) {
            return 1;
        }
        if (rightMissing) {
            return -1;
        }

        const result = left === right ? 0 : left < right ? -1 : 1;
        return descending ? -result : result;
    }

    function toNumberOrNull(value) {
        if (value === null || value === undefined || value === "") {
            return null;
        }

        const numberValue = Number(value);
        return Number.isFinite(numberValue) ? numberValue : null;
    }

    function buildMatrixRow(row, months, showAccumulated) {
        const cellsByMonth = new Map((Array.isArray(row.cells) ? row.cells : []).map((cell) => [cell.mes || "", cell]));
        const accumulated = Number(row.totalUtilidad || 0);
        const accumulatedClass = accumulated > 0 ? "is-utility-positive" : accumulated < 0 ? "is-utility-negative" : "is-utility-neutral";
        return `
            <tr class="${row.hasNegativeMargin ? "is-alert" : ""} ${row.hasOrphans ? "has-orphans" : ""}">
                <th scope="row" class="licx-client-col licx-client-map" tabindex="0" role="button" data-map-client data-client-key="${escapeHtml(row.rowKey || "")}" data-client-name="${escapeHtml(row.cliente || "Cliente sin nombre")}">
                    <strong>${escapeHtml(row.cliente || "Cliente sin nombre")}</strong>
                    <small>${escapeHtml(row.nitCliente || row.clienteId || "")}</small>
                </th>
                ${months.map((month) => buildMatrixCellGroup(cellsByMonth.get(month.key || ""), row, month)).join("")}
                ${showAccumulated ? `<td class="text-end licx-accum-col ${accumulatedClass}">${formatCurrency(row.totalUtilidad)}</td>` : ""}
            </tr>
        `;
    }

    function buildMatrixCellGroup(cell, row, month) {
        const safeCell = cell || {};
        const classes = [
            safeCell.hasNegativeMargin ? "is-negative-cell" : "",
            safeCell.hasOrphans ? "is-orphan-cell" : ""
        ].filter(Boolean).join(" ");
        const commonAttrs = `data-client-key="${escapeHtml(row.rowKey || "")}" data-client-name="${escapeHtml(row.cliente || "Cliente sin nombre")}" data-month="${escapeHtml(month.key || safeCell.mes || "")}"`;
        const utilityValue = Number(safeCell.utilidadValor || 0);
        const utilityClass = utilityValue > 0 ? "is-utility-positive" : utilityValue < 0 ? "is-utility-negative" : "is-utility-neutral";
        return `
            <td class="text-end ${classes} licx-drill-cell" tabindex="0" role="button" data-detail-source="cost" ${commonAttrs}>${formatCurrency(safeCell.costoLicenciamiento)}</td>
            <td class="text-end ${classes} licx-drill-cell" tabindex="0" role="button" data-detail-source="billing" ${commonAttrs}>${formatCurrency(safeCell.facturacionSinIva)}</td>
            <td class="text-end ${classes}">${formatPercent(safeCell.utilidadPct)}</td>
            <td class="text-end ${classes} ${utilityClass}">${formatCurrency(safeCell.utilidadValor)}</td>
        `;
    }

    function openCellDetailDialog(source, clientKey, monthKey, clientName) {
        const dialog = ensureDetailDialog();
        const items = getCellDetailItems(source, clientKey, monthKey);
        const sourceLabel = source === "billing" ? "Venta" : "Costo";
        dialog.querySelector("[data-detail-title]").textContent = `${sourceLabel} - ${clientName || "Cliente"}`;
        dialog.querySelector("[data-detail-subtitle]").textContent = `${monthKey || "-"} | ${items.length} registro(s)`;
        dialog.querySelector("[data-detail-body]").innerHTML = buildDetailTable(items, source);
        dialog.hidden = false;
        document.body.classList.add("licx-modal-open");
        dialog.querySelector("[data-detail-close]")?.focus();
    }

    function closeDetailDialog() {
        if (!detailDialog) {
            return;
        }

        detailDialog.hidden = true;
        document.body.classList.remove("licx-modal-open");
    }

    function ensureDetailDialog() {
        if (detailDialog) {
            return detailDialog;
        }

        detailDialog = document.createElement("div");
        detailDialog.className = "licx-modal";
        detailDialog.hidden = true;
        detailDialog.innerHTML = `
            <div class="licx-modal__backdrop" data-detail-backdrop></div>
            <section class="licx-modal__dialog" role="dialog" aria-modal="true" aria-label="Detalle de celda">
                <header class="licx-modal__header">
                    <div>
                        <div class="licx-kicker">Detalle</div>
                        <h2 data-detail-title>Detalle</h2>
                        <span data-detail-subtitle></span>
                    </div>
                    <button type="button" class="btn-close" aria-label="Cerrar" data-detail-close></button>
                </header>
                <div class="licx-modal__body">
                    <div data-detail-body></div>
                </div>
            </section>
        `;
        detailDialog.querySelector("[data-detail-close]")?.addEventListener("click", closeDetailDialog);
        detailDialog.querySelector("[data-detail-backdrop]")?.addEventListener("click", closeDetailDialog);
        detailDialog.addEventListener("click", handleDetailAction);
        document.body.appendChild(detailDialog);
        return detailDialog;
    }

    function getCellDetailItems(source, clientKey, monthKey) {
        const detailRows = Array.isArray(currentData?.rows) ? currentData.rows : [];
        return detailRows
            .filter((row) =>
                segmentMatches(row)
                && (row.mesCierre || "") === monthKey
                && (row.matrixClientKey || buildRowClientKey(row)) === clientKey)
            .flatMap((row) => {
                const trace = row.trace || {};
                return source === "billing"
                    ? (Array.isArray(trace.billingItems) ? trace.billingItems : [])
                    : (Array.isArray(trace.costItems) ? trace.costItems : []);
            });
    }

    function buildDetailTable(items, source) {
        if (!Array.isArray(items) || items.length === 0) {
            return "<div class=\"licx-empty\">No hay registros para esta celda.</div>";
        }

        const isBilling = source === "billing";
        const canMoveCostMonth = !isBilling && selectedSegmentKey === "onetime";
        return `
            <div class="licx-table-wrap licx-table-wrap--detail">
                <table class="table align-middle licx-table licx-detail-table">
                    <thead>
                        <tr>
                            <th>${isBilling ? "Fecha emision" : "Fecha factura costo"}</th>
                            <th>${isBilling ? "Factura" : "Referencia"}</th>
                            ${isBilling ? "" : "<th>Producto / licencia</th>"}
                            <th>${isBilling ? "Cliente / razon social" : "Cliente"}</th>
                            ${isBilling ? "<th>Vertical</th>" : "<th>Account ID</th><th>Account</th>"}
                            <th>Tipo</th>
                            ${isBilling ? "<th class=\"text-end\">Total factura</th><th class=\"text-end\">IVA</th>" : ""}
                            <th class="text-end">${isBilling ? "Venta sin IVA" : "Costo"}</th>
                            ${canMoveCostMonth ? "<th>Mover mes</th>" : ""}
                            ${isBilling ? "" : "<th>Record ID</th>"}
                        </tr>
                    </thead>
                    <tbody>
                        ${items.map((item) => buildDetailRow(item, isBilling, canMoveCostMonth)).join("")}
                    </tbody>
                </table>
            </div>
        `;
    }

    function buildDetailRow(item, isBilling, canMoveCostMonth) {
        const product = item.producto || (isBilling ? "No disponible en facturacion" : "Sin producto");
        const productCell = isBilling
            ? ""
            : `<td>
                    <strong>${escapeHtml(product)}</strong>
                    <small>${escapeHtml(item.productoId || "")}</small>
                </td>`;
        const clientMeta = item.grupoEmpresarial || item.grupoEmpresarialId || item.clienteId || "";
        const sourceCells = isBilling
            ? `<td>${escapeHtml(item.vertical || "-")}</td>`
            : `<td>${escapeHtml(item.accountId || "-")}</td><td>${escapeHtml(item.account || "-")}</td>`;
        const billingTotals = isBilling
            ? `<td class="text-end">${formatCurrency(item.valorTotal)}</td><td class="text-end">${formatCurrency(item.iva)}</td>`
            : "";
        const moveCostCell = canMoveCostMonth
            ? `<td>
                    <div class="licx-edit-group" data-record-id="${escapeHtml(item.recordId || "")}">
                        <input class="form-control form-control-sm" type="month" data-cost-month-input value="${escapeHtml(item.mes || "")}" />
                        <button type="button" class="btn btn-sm btn-outline-primary" data-detail-action="move-cost-month">Mover</button>
                    </div>
                </td>`
            : "";

        return `
            <tr>
                <td>${escapeHtml(item.fecha || item.mes || "-")}</td>
                <td>${escapeHtml(item.referencia || "-")}</td>
                ${productCell}
                <td>
                    <strong>${escapeHtml(item.cliente || "-")}</strong>
                    <small>${escapeHtml(clientMeta)}</small>
                </td>
                ${sourceCells}
                <td>${escapeHtml(item.tipoContrato || "-")}</td>
                ${billingTotals}
                <td class="text-end">${formatCurrency(item.valor)}</td>
                ${moveCostCell}
                ${isBilling ? "" : `<td><code>${escapeHtml(item.recordId || "")}</code></td>`}
            </tr>
        `;
    }

    async function handleDetailAction(event) {
        const button = event.target.closest("[data-detail-action]");
        if (!button) {
            return;
        }

        const action = button.dataset.detailAction || "";
        if (action !== "move-cost-month") {
            return;
        }

        const wrapper = button.closest("[data-record-id]");
        const recordId = wrapper?.dataset.recordId || "";
        const monthValue = wrapper?.querySelector("[data-cost-month-input]")?.value || "";
        if (!recordId || !monthValue) {
            showStatus("error", "Selecciona el mes destino del costo.");
            return;
        }

        button.disabled = true;
        try {
            await postJson(updateCostInvoiceDateUrl, {
                recordId,
                invoiceDate: `${monthValue}-01`
            });
            closeDetailDialog();
            showStatus("success", "Costo movido. Recalculando cruce...");
            await loadCruce();
        } catch (error) {
            showStatus("error", error instanceof Error ? error.message : "No fue posible mover el costo.");
        } finally {
            button.disabled = false;
        }
    }

    function openClientEditDialog(clientKey, clientName) {
        const dialog = ensureEditDialog();
        const items = getClientEditItems(clientKey);
        dialog.querySelector("[data-edit-title]").textContent = `Mapeo - ${clientName || "Cliente"}`;
        dialog.querySelector("[data-edit-subtitle]").textContent =
            `${items.accountItems.length} cuenta(s) origen | ${items.costItems.length} costo(s) | ${items.billingItems.length} factura(s)`;
        dialog.querySelector("[data-edit-body]").innerHTML = buildClientEditBody(items);
        dialog.hidden = false;
        document.body.classList.add("licx-modal-open");
        dialog.querySelector("[data-edit-close]")?.focus();
    }

    function closeEditDialog() {
        if (!editDialog) {
            return;
        }

        editDialog.hidden = true;
        document.body.classList.remove("licx-modal-open");
    }

    function ensureEditDialog() {
        if (editDialog) {
            return editDialog;
        }

        editDialog = document.createElement("div");
        editDialog.className = "licx-modal";
        editDialog.hidden = true;
        editDialog.innerHTML = `
            <div class="licx-modal__backdrop" data-edit-backdrop></div>
            <section class="licx-modal__dialog" role="dialog" aria-modal="true" aria-label="Editar cruce de cliente">
                <header class="licx-modal__header">
                    <div>
                        <div class="licx-kicker">Editar cliente</div>
                        <h2 data-edit-title>Cliente</h2>
                        <span data-edit-subtitle></span>
                    </div>
                    <button type="button" class="btn-close" aria-label="Cerrar" data-edit-close></button>
                </header>
                <div class="licx-modal__body">
                    <div data-edit-body></div>
                </div>
            </section>
        `;
        editDialog.querySelector("[data-edit-close]")?.addEventListener("click", closeEditDialog);
        editDialog.querySelector("[data-edit-backdrop]")?.addEventListener("click", closeEditDialog);
        editDialog.addEventListener("input", handleAccountSearchInput);
        editDialog.addEventListener("click", handleClientEditAction);
        document.body.appendChild(editDialog);
        return editDialog;
    }

    function getClientEditItems(clientKey) {
        const rows = (Array.isArray(currentData?.rows) ? currentData.rows : [])
            .filter((row) =>
                segmentMatches(row)
                && (row.matrixClientKey || buildRowClientKey(row)) === clientKey);
        const costItems = distinctByRecordId(rows.flatMap((row) => row.trace?.costItems || []));
        const billingItems = distinctByRecordId(rows.flatMap((row) => row.trace?.billingItems || []));
        const accountItems = buildAccountMapItems(costItems);
        return { accountItems, costItems, billingItems };
    }

    function buildClientEditBody(items) {
        return `
            <div class="licx-edit-sections">
                <section>
                    <h3>Mapeo permanente de Account ID</h3>
                    ${buildAccountMapTable(items.accountItems)}
                </section>
                <section>
                    <h3>Costos licenciamiento</h3>
                    ${buildCostEditTable(items.costItems)}
                </section>
                <section>
                    <h3>Facturacion</h3>
                    ${buildBillingEditTable(items.billingItems)}
                </section>
            </div>
        `;
    }

    function buildAccountMapItems(costItems) {
        const map = new Map();
        for (const item of Array.isArray(costItems) ? costItems : []) {
            const sourceAccountId = (item.accountIdOriginal || item.accountId || "").toString();
            const sourceAccountName = (item.accountOriginal || item.account || "").toString();
            const key = (sourceAccountId || sourceAccountName).toLowerCase();
            if (!key || map.has(key)) {
                continue;
            }

            map.set(key, {
                sourceAccountId,
                sourceAccountName,
                sourceClientName: item.cliente || "",
                currentAccountId: item.accountId || "",
                currentAccountName: item.account || "",
                currentClientName: item.cliente || "",
                mappingId: item.accountMappingId || "",
                mappingApplied: item.accountMappingApplied === true
            });
        }

        return [...map.values()].sort((left, right) =>
            (left.sourceAccountName || left.sourceAccountId).localeCompare(right.sourceAccountName || right.sourceAccountId, "es"));
    }

    function buildAccountMapTable(items) {
        if (!items.length) {
            return "<div class=\"licx-empty licx-empty--small\">Esta fila no tiene cuentas de costo para mapear.</div>";
        }

        return `
            <div class="licx-table-wrap licx-table-wrap--detail">
                <table class="table align-middle licx-table licx-map-table">
                    <thead>
                        <tr>
                            <th>Cuenta origen Excel</th>
                            <th>Cliente detectado</th>
                            <th>Cuenta destino actual</th>
                            <th>Nuevo Account ID destino</th>
                            <th></th>
                        </tr>
                    </thead>
                    <tbody>
                        ${items.map(buildAccountMapRow).join("")}
                    </tbody>
                </table>
            </div>
        `;
    }

    function buildAccountMapRow(item) {
        const currentOption = item.currentAccountId
            ? `<option value="${escapeHtml(item.currentAccountId)}">${escapeHtml(item.currentAccountName || item.currentAccountId)}${item.currentClientName ? ` - ${escapeHtml(item.currentClientName)}` : ""}</option>`
            : "<option value=\"\">Busca una cuenta destino</option>";
        return `
            <tr data-source-account-id="${escapeHtml(item.sourceAccountId || "")}" data-source-account-name="${escapeHtml(item.sourceAccountName || "")}" data-source-client-name="${escapeHtml(item.sourceClientName || "")}">
                <td>
                    <strong>${escapeHtml(item.sourceAccountName || item.sourceAccountId || "-")}</strong>
                    <small>${escapeHtml(item.sourceAccountId || "")}</small>
                </td>
                <td>${escapeHtml(item.sourceClientName || "-")}</td>
                <td>
                    <strong>${escapeHtml(item.currentAccountName || "-")}</strong>
                    <small>${item.mappingApplied ? "Mapeo aplicado" : "Relacion actual"}</small>
                </td>
                <td>
                    <input class="form-control form-control-sm" data-account-search-input placeholder="Buscar Account ID destino" />
                    <select class="form-select form-select-sm mt-1" data-target-account-select>
                        ${currentOption}
                    </select>
                </td>
                <td class="text-end">
                    <button type="button" class="btn btn-sm btn-primary" data-edit-action="save-account-mapping">Guardar mapeo</button>
                </td>
            </tr>
        `;
    }

    function buildCostEditTable(items) {
        if (!items.length) {
            return "<div class=\"licx-empty licx-empty--small\">No hay costos para esta fila.</div>";
        }

        const typeOptions = currentData?.costContractTypeOptions || [];
        return `
            <div class="licx-table-wrap licx-table-wrap--detail">
                <table class="table align-middle licx-table licx-edit-table">
                    <thead>
                        <tr>
                            <th>Fecha factura</th>
                            <th>Producto / licencia</th>
                            <th>Cuenta</th>
                            <th>Tipo contrato</th>
                            <th class="text-end">Costo</th>
                            <th>Record ID</th>
                        </tr>
                    </thead>
                    <tbody>
                        ${items.map((item) => buildCostEditRow(item, typeOptions)).join("")}
                    </tbody>
                </table>
            </div>
        `;
    }

    function buildCostEditRow(item, typeOptions) {
        return `
            <tr data-record-id="${escapeHtml(item.recordId || "")}">
                <td>${escapeHtml(item.fecha || item.mes || "-")}</td>
                <td>
                    <strong>${escapeHtml(item.producto || "Sin producto")}</strong>
                    <small>${escapeHtml(item.productoId || "")}</small>
                </td>
                <td>
                    <div class="licx-edit-group">
                        <input class="form-control form-control-sm" data-account-input value="${escapeHtml(item.account || item.accountId || "")}" />
                        <button type="button" class="btn btn-sm btn-outline-primary" data-edit-action="save-cost-account">Guardar</button>
                    </div>
                    <small>${escapeHtml(item.accountId || "")}</small>
                </td>
                <td>
                    <div class="licx-edit-group">
                        <select class="form-select form-select-sm" data-contract-select>
                            ${buildOptions(typeOptions, item.tipoContratoValue)}
                        </select>
                        <button type="button" class="btn btn-sm btn-outline-primary" data-edit-action="save-cost-contract">Guardar</button>
                    </div>
                </td>
                <td class="text-end">${formatCurrency(item.valor)}</td>
                <td><code>${escapeHtml(item.recordId || "")}</code></td>
            </tr>
        `;
    }

    function buildBillingEditTable(items) {
        if (!items.length) {
            return "<div class=\"licx-empty licx-empty--small\">No hay facturas para esta fila.</div>";
        }

        const typeOptions = currentData?.billingContractTypeOptions || [];
        const verticalOptions = currentData?.billingVerticalOptions || [];
        return `
            <div class="licx-table-wrap licx-table-wrap--detail">
                <table class="table align-middle licx-table licx-edit-table">
                    <thead>
                        <tr>
                            <th>Fecha emision</th>
                            <th>Factura</th>
                            <th>Cliente / razon social</th>
                            <th>Vertical</th>
                            <th>Tipo contrato</th>
                            <th class="text-end">Venta sin IVA</th>
                            <th>Record ID</th>
                        </tr>
                    </thead>
                    <tbody>
                        ${items.map((item) => buildBillingEditRow(item, typeOptions, verticalOptions)).join("")}
                    </tbody>
                </table>
            </div>
        `;
    }

    function buildBillingEditRow(item, typeOptions, verticalOptions) {
        return `
            <tr data-record-id="${escapeHtml(item.recordId || "")}">
                <td>${escapeHtml(item.fecha || item.mes || "-")}</td>
                <td>${escapeHtml(item.referencia || "-")}</td>
                <td>
                    <strong>${escapeHtml(item.cliente || "-")}</strong>
                    <small>${escapeHtml(item.grupoEmpresarial || item.grupoEmpresarialId || item.clienteId || "")}</small>
                </td>
                <td>
                    <div class="licx-edit-group">
                        <select class="form-select form-select-sm" data-vertical-select>
                            ${buildOptions(verticalOptions, item.verticalValue)}
                        </select>
                        <button type="button" class="btn btn-sm btn-outline-primary" data-edit-action="save-billing-vertical">Guardar</button>
                    </div>
                    <small>Actual: ${escapeHtml(item.vertical || "-")}</small>
                </td>
                <td>
                    <div class="licx-edit-group">
                        <select class="form-select form-select-sm" data-contract-select>
                            ${buildOptions(typeOptions, item.tipoContratoValue)}
                        </select>
                        <button type="button" class="btn btn-sm btn-outline-primary" data-edit-action="save-billing-contract">Guardar</button>
                    </div>
                </td>
                <td class="text-end">${formatCurrency(item.valor)}</td>
                <td><code>${escapeHtml(item.recordId || "")}</code></td>
            </tr>
        `;
    }

    function handleAccountSearchInput(event) {
        const input = event.target.closest("[data-account-search-input]");
        if (!input) {
            return;
        }

        window.clearTimeout(accountSearchTimer);
        accountSearchTimer = window.setTimeout(() => {
            searchAccountOptions(input).catch((error) => {
                showStatus("error", error instanceof Error ? error.message : "No fue posible buscar Account IDs.");
            });
        }, 350);
    }

    async function searchAccountOptions(input) {
        const query = (input.value || "").trim();
        const row = input.closest("tr");
        const select = row?.querySelector("[data-target-account-select]");
        if (!select || query.length < 2) {
            return;
        }

        const url = new URL(searchAccountsUrl, window.location.origin);
        url.searchParams.set("query", query);
        url.searchParams.set("top", "12");
        const response = await fetch(url.toString(), {
            headers: { Accept: "application/json" }
        });
        const accounts = await readJsonResponse(response);
        if (!Array.isArray(accounts) || accounts.length === 0) {
            select.innerHTML = "<option value=\"\">Sin resultados</option>";
            return;
        }

        select.innerHTML = accounts.map((account) => `
            <option value="${escapeHtml(account.accountId || "")}">
                ${escapeHtml(account.accountName || account.accountId || "")}${account.clientName ? ` - ${escapeHtml(account.clientName)}` : ""}
            </option>
        `).join("");
    }

    async function handleClientEditAction(event) {
        const button = event.target.closest("[data-edit-action]");
        if (!button) {
            return;
        }

        const row = button.closest("tr[data-record-id]");
        const mappingRow = button.closest("tr[data-source-account-id], tr[data-source-account-name]");
        const recordId = row?.dataset.recordId || "";
        const action = button.dataset.editAction || "";
        if (action !== "save-account-mapping" && !recordId) {
            showStatus("error", "No se encontro el ID del registro.");
            return;
        }

        button.disabled = true;
        try {
            if (action === "save-account-mapping") {
                const targetAccountId = mappingRow?.querySelector("[data-target-account-select]")?.value || "";
                await postJson(saveAccountMappingUrl, {
                    sourceAccountId: mappingRow?.dataset.sourceAccountId || "",
                    sourceAccountName: mappingRow?.dataset.sourceAccountName || "",
                    sourceClientName: mappingRow?.dataset.sourceClientName || "",
                    targetAccountId
                });
            } else if (action === "save-cost-contract") {
                const value = Number(row.querySelector("[data-contract-select]")?.value || 0);
                await postJson(updateCostContractUrl, { recordIds: [recordId], contractTypeValue: value });
            } else if (action === "save-cost-account") {
                const accountId = row.querySelector("[data-account-input]")?.value || "";
                await postJson(updateCostAccountUrl, { recordId, accountId });
            } else if (action === "save-billing-contract") {
                const value = Number(row.querySelector("[data-contract-select]")?.value || 0);
                await postJson(updateBillingContractUrl, { recordIds: [recordId], contractTypeOptionValue: value });
            } else if (action === "save-billing-vertical") {
                const value = Number(row.querySelector("[data-vertical-select]")?.value || 0);
                await postJson(updateBillingVerticalUrl, { recordIds: [recordId], verticalOptionValue: value });
            }

            closeEditDialog();
            showStatus("success", "Registro actualizado. Recalculando cruce...");
            await loadCruce();
        } catch (error) {
            showStatus("error", error instanceof Error ? error.message : "No fue posible actualizar el registro.");
        } finally {
            button.disabled = false;
        }
    }

    function buildOptions(options, selectedValue) {
        return (Array.isArray(options) ? options : []).map((option) => `
            <option value="${Number(option.value || 0)}" ${Number(option.value || 0) === Number(selectedValue || 0) ? "selected" : ""}>
                ${escapeHtml(option.label || option.value || "")}
            </option>
        `).join("");
    }

    function distinctByRecordId(items) {
        const seen = new Set();
        const result = [];
        for (const item of Array.isArray(items) ? items : []) {
            const key = (item?.recordId || "").toString().toLowerCase();
            if (!key || seen.has(key)) {
                continue;
            }
            seen.add(key);
            result.push(item);
        }
        return result;
    }

    async function downloadCruce() {
        if (!exportUrl) {
            showStatus("error", "No se encontro la ruta de descarga.");
            return;
        }

        const segment = getSelectedSegment();
        if (!segment || !Array.isArray(segment.rows) || segment.rows.length === 0) {
            showStatus("error", "No hay registros para descargar");
            updateDownloadButton();
            return;
        }

        const originalText = downloadButton?.textContent || "Descargar listado";
        if (downloadButton) {
            downloadButton.disabled = true;
            downloadButton.textContent = "Descargando...";
        }

        try {
            const url = new URL(exportUrl, window.location.origin);
            url.searchParams.set("year", yearInput.value || "");
            url.searchParams.set("month", monthSelect.value || "");
            url.searchParams.set("periodMode", periodModeSelect.value || "month");
            url.searchParams.set("segmentKey", selectedSegmentKey || segment.key || "");
            url.searchParams.set("sortKey", sortState.key || "");
            url.searchParams.set("sortDirection", sortState.direction || "");

            const response = await fetch(url.toString(), {
                headers: { Accept: "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet, application/json" }
            });
            if (!response.ok) {
                await readJsonResponse(response);
                return;
            }

            const blob = await response.blob();
            const fileName = resolveDownloadFileName(response) || "cruce_licenciamiento.xlsx";
            const objectUrl = URL.createObjectURL(blob);
            const anchor = document.createElement("a");
            anchor.href = objectUrl;
            anchor.download = fileName;
            document.body.appendChild(anchor);
            anchor.click();
            anchor.remove();
            URL.revokeObjectURL(objectUrl);
            showStatus("success", "Listado descargado.");
        } finally {
            if (downloadButton) {
                downloadButton.textContent = originalText;
            }
            updateDownloadButton();
        }
    }

    function updateDownloadButton() {
        if (!downloadButton) {
            return;
        }

        const segment = getSelectedSegment();
        const hasRows = !!currentData && !!segment && Array.isArray(segment.rows) && segment.rows.length > 0;
        downloadButton.disabled = !hasRows;
        downloadButton.title = hasRows ? "" : "No hay registros para descargar";
    }

    function resolveDownloadFileName(response) {
        const disposition = response.headers.get("content-disposition") || "";
        const utfMatch = disposition.match(/filename\*=UTF-8''([^;]+)/i);
        if (utfMatch?.[1]) {
            return decodeURIComponent(utfMatch[1].replace(/["']/g, ""));
        }

        const plainMatch = disposition.match(/filename="?([^";]+)"?/i);
        return plainMatch?.[1] || "";
    }

    async function postJson(url, payload) {
        if (!url) {
            throw new Error("No se encontro la ruta de actualizacion.");
        }

        const response = await fetch(url, {
            method: "POST",
            headers: {
                "Accept": "application/json",
                "Content-Type": "application/json"
            },
            body: JSON.stringify(payload)
        });
        return await readJsonResponse(response);
    }

    function buildRowClientKey(row) {
        const trace = row?.trace || {};
        const clientId = (trace.billingClientId || trace.costClientId || "").toString().trim();
        if (clientId) {
            return `client:${clientId.toLowerCase()}`;
        }

        const nameKey = normalizeClientKey(row?.cliente || "");
        return nameKey ? `name:${nameKey}` : `row:${row?.rowKey || ""}`;
    }

    function segmentMatches(row) {
        return selectedSegmentKey === "all" || (row?.tipoContratoKey || "") === selectedSegmentKey;
    }

    function normalizeClientKey(value) {
        const legalTokens = new Set(["SAS", "SA", "S A S", "S A", "LTDA", "LIMITADA", "INC", "CORP", "CORPORACION", "FUNDACION", "EMPRESA", "UNION TEMPORAL"]);
        const normalized = (value || "")
            .toString()
            .normalize("NFD")
            .replace(/[\u0300-\u036f]/g, "")
            .toUpperCase()
            .replace(/[^A-Z0-9]+/g, " ")
            .trim();

        if (!normalized) {
            return "";
        }

        return normalized
            .split(/\s+/)
            .filter((token) => !legalTokens.has(token))
            .join(" ");
    }

    function formatCurrency(value) {
        return copFormatter.format(Number(value || 0));
    }

    function formatPercent(value) {
        if (value === null || value === undefined || value === "") {
            return "N/A";
        }

        const numberValue = Number(value);
        if (!Number.isFinite(numberValue)) {
            return "N/A";
        }

        return `${percentFormatter.format(numberValue)}%`;
    }

    function showStatus(tone, message) {
        if (!status) {
            return;
        }

        status.className = `licx-status is-${tone}`;
        status.textContent = message || "";
        status.hidden = !message;
    }

    function setBusy(isBusy) {
        filtersForm?.querySelectorAll("input, select, button").forEach((element) => {
            element.disabled = isBusy;
        });
    }

    async function readJsonResponse(response) {
        const contentType = response.headers.get("content-type") || "";
        const payload = contentType.includes("application/json")
            ? await response.json()
            : { message: await response.text() };

        if (!response.ok) {
            throw new Error(payload?.detail || payload?.message || "Ocurrio un error inesperado.");
        }

        return payload;
    }

    function escapeHtml(value) {
        return (value ?? "").toString()
            .replace(/&/g, "&amp;")
            .replace(/</g, "&lt;")
            .replace(/>/g, "&gt;")
            .replace(/"/g, "&quot;")
            .replace(/'/g, "&#039;");
    }
}());
