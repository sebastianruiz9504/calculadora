(() => {
    const app = document.getElementById("metricasApp");
    if (!app) {
        return;
    }

    const chartsContainer = document.getElementById("metricsChartsContainer");
    const statusBanner = document.getElementById("metricsStatusBanner");
    const refreshButton = document.getElementById("refreshMetricsBtn");
    const sellerFilter = document.getElementById("metricsSellerFilter");
    const sellerFilterGroup = document.getElementById("metricsSellerFilterGroup");
    const viewButtons = Array.from(document.querySelectorAll(".metrics-view-tab"));
    const filterButtons = Array.from(document.querySelectorAll(".metrics-filter-btn"));
    const summaryRecords = document.getElementById("metricsSummaryRecords");
    const summarySellers = document.getElementById("metricsSummarySellers");
    const summaryVerticals = document.getElementById("metricsSummaryVerticals");
    const summaryScore = document.getElementById("metricsSummaryScore");
    const summaryAnnualValue = document.getElementById("metricsSummaryAnnualValue");
    const insightView = document.getElementById("metricsInsightView");
    const insightRange = document.getElementById("metricsInsightRange");
    const insightSeller = document.getElementById("metricsInsightSeller");
    const insightGranularity = document.getElementById("metricsInsightGranularity");
    const insightUpdatedAt = document.getElementById("metricsInsightUpdatedAt");

    const numberFormatter = new Intl.NumberFormat("es-CO", {
        minimumFractionDigits: 0,
        maximumFractionDigits: 2
    });

    const scoreFormatter = new Intl.NumberFormat("es-CO", {
        minimumFractionDigits: 2,
        maximumFractionDigits: 2
    });

    const dateTimeFormatter = new Intl.DateTimeFormat("es-CO", {
        dateStyle: "medium",
        timeStyle: "short"
    });

    const state = {
        filter: app.dataset.initialFilter || "this-year",
        view: app.dataset.initialView || "global",
        seller: "",
        dashboard: null,
        isLoading: false,
        updatedAt: null
    };

    function escapeHtml(value) {
        return (value ?? "").toString()
            .replace(/&/g, "&amp;")
            .replace(/</g, "&lt;")
            .replace(/>/g, "&gt;")
            .replace(/"/g, "&quot;")
            .replace(/'/g, "&#39;");
    }

    function formatNumber(value) {
        return numberFormatter.format(Number(value || 0));
    }

    function formatScoreValue(value) {
        return scoreFormatter.format(Number(value || 0));
    }

    function formatDateTimeValue(value) {
        return value instanceof Date ? dateTimeFormatter.format(value) : "Pendiente";
    }

    function setStatus(type, message) {
        if (!statusBanner) {
            return;
        }

        if (!message) {
            statusBanner.className = "metrics-status";
            statusBanner.textContent = "";
            return;
        }

        statusBanner.className = `metrics-status show ${type}`;
        statusBanner.textContent = message;
    }

    function setLoading(loading) {
        state.isLoading = loading;
        refreshButton && (refreshButton.disabled = loading);
        sellerFilter && (sellerFilter.disabled = loading || state.view !== "individual" || sellerFilter.options.length <= 1);

        filterButtons.forEach(button => {
            button.disabled = loading;
            button.classList.toggle("active", button.dataset.filter === state.filter);
        });

        viewButtons.forEach(button => {
            button.disabled = loading;
            button.classList.toggle("active", button.dataset.view === state.view);
        });
    }

    function buildDashboardUrl() {
        const query = new URLSearchParams({
            filter: state.filter,
            view: state.view
        });

        if (state.view === "individual" && state.seller) {
            query.set("seller", state.seller);
        }

        return `${app.dataset.chartsUrl}?${query.toString()}`;
    }

    async function fetchJson(url) {
        const response = await fetch(url, {
            headers: {
                Accept: "application/json"
            }
        });

        const contentType = response.headers.get("content-type") || "";
        if (!response.ok) {
            const message = contentType.includes("application/json")
                ? (await response.json())?.message || "No fue posible completar la solicitud."
                : await response.text();
            throw new Error(message || "No fue posible completar la solicitud.");
        }

        if (!contentType.includes("application/json")) {
            const message = await response.text();
            throw new Error(message || "La respuesta del servidor no fue valida.");
        }

        return response.json();
    }

    function updateSummary(dashboard) {
        const safeDashboard = dashboard || {};
        summaryRecords && (summaryRecords.textContent = formatNumber(safeDashboard.recordsCount));
        summarySellers && (summarySellers.textContent = formatNumber(safeDashboard.sellersCount));
        summaryVerticals && (summaryVerticals.textContent = formatNumber(safeDashboard.verticalsCount));
        summaryScore && (summaryScore.textContent = formatScoreValue(safeDashboard.totalScore));
        summaryAnnualValue && (summaryAnnualValue.textContent = formatNumber(safeDashboard.totalAnnualValue));
    }

    function updateInsights(dashboard) {
        const safeDashboard = dashboard || {};
        insightView && (insightView.textContent = safeDashboard.viewLabel || "Pendiente");
        insightRange && (insightRange.textContent = safeDashboard.filterLabel || "Pendiente");
        insightSeller && (insightSeller.textContent = safeDashboard.appliedSellerName || "Todos los vendedores");
        insightGranularity && (insightGranularity.textContent = safeDashboard.granularityLabel || "Pendiente");
        insightUpdatedAt && (insightUpdatedAt.textContent = formatDateTimeValue(state.updatedAt));
    }

    function syncViewLayout(dashboard) {
        const requiresSelection = Boolean(dashboard?.requiresSellerSelection);
        sellerFilterGroup?.classList.toggle("is-hidden", state.view !== "individual");
        app.classList.toggle("metrics-shell--individual-empty", requiresSelection);
    }

    function renderSellerOptions(dashboard) {
        if (!sellerFilter) {
            return;
        }

        const sellers = Array.isArray(dashboard?.sellers) ? dashboard.sellers : [];
        const options = [
            '<option value="">Todos los vendedores</option>',
            ...sellers.map(option => `<option value="${escapeHtml(option.key)}">${escapeHtml(option.name)}</option>`)
        ];

        sellerFilter.innerHTML = options.join("");
        sellerFilter.value = dashboard?.appliedSellerKey || "";
        sellerFilter.disabled = state.isLoading || state.view !== "individual" || sellerFilter.options.length <= 1;
    }

    function buildSampleLabelIndexes(length) {
        if (length <= 12) {
            return new Set(Array.from({ length }, (_, index) => index));
        }

        const indexes = new Set([0, length - 1]);
        const step = Math.ceil(length / (length <= 18 ? 8 : 6));

        for (let index = 0; index < length; index += step) {
            indexes.add(index);
        }

        return indexes;
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

    function toDomId(value) {
        return (value || "metrics")
            .toString()
            .toLowerCase()
            .replace(/[^a-z0-9]+/g, "-")
            .replace(/^-+|-+$/g, "") || "metrics";
    }

    function renderChartSvg(chart) {
        const categories = Array.isArray(chart.categories) ? chart.categories : [];
        const series = Array.isArray(chart.series) ? chart.series : [];

        if (!categories.length || !series.length) {
            return `<div class="metrics-chart__empty">${escapeHtml(chart.emptyMessage || "No hay datos para este grafico.")}</div>`;
        }

        const width = 960;
        const height = 320;
        const padding = { top: 24, right: 24, bottom: 44, left: 64 };
        const plotWidth = width - padding.left - padding.right;
        const plotHeight = height - padding.top - padding.bottom;
        const rawMaxValue = Math.max(1, ...series.flatMap(item => item.values || []).map(value => Number(value || 0)));
        const maxValue = getNiceMaxValue(rawMaxValue);
        const labelIndexes = buildSampleLabelIndexes(categories.length);
        const denominator = Math.max(categories.length - 1, 1);
        const guideY2 = height - padding.bottom;

        const gridLines = Array.from({ length: 5 }, (_, index) => {
            const ratio = index / 4;
            const y = padding.top + plotHeight - (plotHeight * ratio);
            const value = maxValue * ratio;
            return `
                <line class="metrics-chart__grid" x1="${padding.left}" y1="${y}" x2="${width - padding.right}" y2="${y}"></line>
                <text class="metrics-chart__axis-label" x="${padding.left - 10}" y="${y + 4}" text-anchor="end">${escapeHtml(formatNumber(value))}</text>
            `;
        }).join("");

        const xLabels = categories.map((label, index) => {
            if (!labelIndexes.has(index)) {
                return "";
            }

            const x = padding.left + (plotWidth * (index / denominator));
            return `<text class="metrics-chart__axis-label" x="${x}" y="${height - 10}" text-anchor="middle">${escapeHtml(label)}</text>`;
        }).join("");

        const defs = series.map((item, index) => `
            <linearGradient id="${toDomId(chart.key)}-gradient-${index}" x1="0" x2="0" y1="0" y2="1">
                <stop offset="0%" stop-color="${escapeHtml(item.color)}" stop-opacity=".22"></stop>
                <stop offset="100%" stop-color="${escapeHtml(item.color)}" stop-opacity="0"></stop>
            </linearGradient>
        `).join("");

        const paths = series.map((item, index) => {
            const values = Array.isArray(item.values) ? item.values : [];
            const annualValues = Array.isArray(item.annualValues) ? item.annualValues : [];
            const gradientId = `${toDomId(chart.key)}-gradient-${index}`;
            const points = values.map((value, pointIndex) => {
                const x = padding.left + (plotWidth * (pointIndex / denominator));
                const y = padding.top + plotHeight - ((Number(value || 0) / maxValue) * plotHeight);
                return {
                    x,
                    y,
                    score: Number(value || 0),
                    annualValue: Number(annualValues[pointIndex] || 0),
                    category: categories[pointIndex] || "",
                    seriesName: item.name
                };
            });

            const linePath = buildLinePath(points);
            const areaPath = buildAreaPath(points, padding.top + plotHeight);
            const circles = points.map(point => {
                const visibleCircle = `<circle class="metrics-chart__dot${item.isReference ? " metrics-chart__dot--reference" : ""}" cx="${point.x}" cy="${point.y}" r="${item.isReference ? "3.6" : "4.4"}" fill="${item.isReference ? "#ffffff" : escapeHtml(item.color)}" stroke="${escapeHtml(item.color)}"></circle>`;
                if (item.isReference) {
                    return `
                        <g class="metrics-chart__point metrics-chart__point--reference" data-series-key="${escapeHtml(item.key)}">
                            ${visibleCircle}
                        </g>
                    `;
                }

                const ariaLabel = `${point.seriesName}, ${point.category}, puntaje ${formatScoreValue(point.score)}, valor contratos ${formatNumber(point.annualValue)}`;
                return `
                    <g class="metrics-chart__point" data-series-key="${escapeHtml(item.key)}">
                        ${visibleCircle}
                        <circle class="metrics-chart__target"
                                cx="${point.x}"
                                cy="${point.y}"
                                r="12"
                                fill="transparent"
                                tabindex="0"
                                focusable="true"
                                data-color="${escapeHtml(item.color)}"
                                data-category="${escapeHtml(point.category)}"
                                data-series="${escapeHtml(point.seriesName)}"
                                data-score="${point.score}"
                                data-annual-value="${point.annualValue}"
                                aria-label="${escapeHtml(ariaLabel)}"></circle>
                    </g>
                `;
            }).join("");

            return `
                ${series.length === 1 ? `<path class="metrics-chart__area" d="${areaPath}" fill="url(#${gradientId})"></path>` : ""}
                <path class="metrics-chart__line${item.isReference ? " metrics-chart__line--reference" : ""}" d="${linePath}" stroke="${escapeHtml(item.color)}"${item.strokeDasharray ? ` stroke-dasharray="${escapeHtml(item.strokeDasharray)}"` : ""}></path>
                ${circles}
            `;
        }).join("");

        return `
            <div class="metrics-chart__plot">
                <svg class="metrics-chart__svg" viewBox="0 0 ${width} ${height}" preserveAspectRatio="none" role="img" aria-label="${escapeHtml(chart.title)}">
                    <defs>${defs}</defs>
                    ${gridLines}
                    <line class="metrics-chart__hover-guide" x1="${padding.left}" y1="${padding.top}" x2="${padding.left}" y2="${guideY2}"></line>
                    ${paths}
                    ${xLabels}
                </svg>
                <div class="metrics-chart__tooltip" hidden>
                    <div class="metrics-chart__tooltip-period"></div>
                    <div class="metrics-chart__tooltip-series"></div>
                    <div class="metrics-chart__tooltip-grid">
                        <span>Puntaje</span>
                        <strong data-role="score">0.00</strong>
                        <span>Valor contratos</span>
                        <strong data-role="annualValue">0</strong>
                    </div>
                </div>
            </div>
        `;
    }

    function renderLegend(chart) {
        const series = Array.isArray(chart.series) ? chart.series : [];
        if (!series.length) {
            return "";
        }

        return `
            <div class="metrics-chart__legend">
                ${series.map(item => `
                    <div class="metrics-chart__legend-item${item.isReference ? " metrics-chart__legend-item--reference" : ""}">
                        <div class="metrics-chart__legend-main">
                            <span class="metrics-chart__legend-color" style="background:${escapeHtml(item.color)}"></span>
                            <span class="metrics-chart__legend-name">${escapeHtml(item.name)}</span>
                        </div>
                        <div class="metrics-chart__legend-meta">
                            ${item.legendNote
                                ? `<span class="metrics-chart__legend-highlight">${escapeHtml(item.legendNote)}</span>`
                                : `
                                    <span>Puntaje ${escapeHtml(formatScoreValue(item.totalScore))}</span>
                                    <span class="metrics-chart__legend-highlight">Valor contratos ${escapeHtml(formatNumber(item.totalAnnualValue))}</span>
                                `}
                        </div>
                    </div>
                `).join("")}
            </div>
        `;
    }

    function renderGoalStatus(chart) {
        const goalStatuses = Array.isArray(chart.goalStatuses) ? chart.goalStatuses : [];
        if (!goalStatuses.length) {
            return "";
        }

        const evaluatedStatuses = goalStatuses.filter(status => status.statusTone === "met" || status.statusTone === "missed");
        const metCount = evaluatedStatuses.filter(status => status.isMet).length;
        const summaryText = evaluatedStatuses.length
            ? `${formatNumber(metCount)} / ${formatNumber(evaluatedStatuses.length)} periodos cerrados en meta`
            : "Sin periodos cerrados";

        return `
            <div class="metrics-chart__goal-block">
                <div class="metrics-chart__goal-head">
                    <span class="metrics-chart__goal-label">${escapeHtml(chart.goalLabel || "Meta")}</span>
                    <span class="metrics-chart__goal-summary">${escapeHtml(summaryText)}</span>
                </div>
                <div class="metrics-chart__goal-statuses">
                    ${goalStatuses.map(status => `
                        <div class="metrics-chart__goal-chip is-${escapeHtml(status.statusTone || "upcoming")}">
                            <span class="metrics-chart__goal-chip-period">${escapeHtml(status.category)}</span>
                            <span class="metrics-chart__goal-chip-state">${escapeHtml(status.statusLabel || "")}</span>
                            <span class="metrics-chart__goal-chip-values">${escapeHtml(formatScoreValue(status.actualValue))} / ${escapeHtml(formatScoreValue(status.targetValue))}</span>
                        </div>
                    `).join("")}
                </div>
            </div>
        `;
    }

    function renderChartBadges(chart) {
        const seriesCount = Array.isArray(chart.series) ? chart.series.length : 0;
        const categoryCount = Array.isArray(chart.categories) ? chart.categories.length : 0;

        return `
            <div class="metrics-chart__badges">
                <span class="metrics-chart__badge">${escapeHtml(formatNumber(seriesCount))} ${seriesCount === 1 ? "serie" : "series"}</span>
                <span class="metrics-chart__badge">${escapeHtml(formatNumber(categoryCount))} ${categoryCount === 1 ? "periodo" : "periodos"}</span>
            </div>
        `;
    }

    function renderCharts(dashboard) {
        if (!chartsContainer) {
            return;
        }

        const charts = Array.isArray(dashboard?.charts) ? dashboard.charts : [];
        if (!charts.length) {
            chartsContainer.innerHTML = `
                <div class="metrics-chart__empty">
                    <strong>${escapeHtml(dashboard?.emptyStateTitle || "No hay metricas para mostrar.")}</strong>
                    <span>${escapeHtml(dashboard?.emptyStateMessage || "No hay metricas para mostrar en este rango.")}</span>
                </div>
            `;
            return;
        }

        chartsContainer.innerHTML = charts.map(chart => `
            <article class="metrics-chart">
                <div class="metrics-chart__head">
                    <div>
                        <div class="metrics-chart__eyebrow">Grafica</div>
                        <h2 class="metrics-chart__title">${escapeHtml(chart.title)}</h2>
                        <p class="metrics-chart__subtitle">${escapeHtml(chart.subtitle || "")}</p>
                        ${renderChartBadges(chart)}
                    </div>
                    <div class="metrics-chart__totals">
                        <span class="metrics-chart__total-label">Puntaje total</span>
                        <span class="metrics-chart__total-value">${escapeHtml(formatScoreValue(chart.totalScore))}</span>
                        <span class="metrics-chart__total-label">Valor contratos</span>
                        <span class="metrics-chart__total-value">${escapeHtml(formatNumber(chart.totalAnnualValue))}</span>
                    </div>
                </div>
                ${renderChartSvg(chart)}
                ${renderLegend(chart)}
                ${renderGoalStatus(chart)}
            </article>
        `).join("");

        bindChartInteractions();
    }

    function setActivePoint(plot, target) {
        plot.querySelectorAll(".metrics-chart__point.is-active").forEach(point => {
            if (point !== target.parentElement) {
                point.classList.remove("is-active");
            }
        });

        target.parentElement?.classList.add("is-active");
    }

    function hidePointTooltip(plot) {
        const tooltip = plot.querySelector(".metrics-chart__tooltip");
        const guide = plot.querySelector(".metrics-chart__hover-guide");

        plot.querySelectorAll(".metrics-chart__point.is-active").forEach(point => point.classList.remove("is-active"));

        if (tooltip) {
            tooltip.hidden = true;
            tooltip.style.left = "";
            tooltip.style.top = "";
        }

        guide?.classList.remove("is-visible");
    }

    function positionTooltip(plot, svg, tooltip, cx, cy) {
        const plotRect = plot.getBoundingClientRect();
        const svgRect = svg.getBoundingClientRect();
        const viewBox = svg.viewBox?.baseVal;
        const viewWidth = viewBox?.width || 1;
        const viewHeight = viewBox?.height || 1;
        const offsetX = svgRect.left - plotRect.left;
        const offsetY = svgRect.top - plotRect.top;
        const x = offsetX + ((cx / viewWidth) * svgRect.width);
        const y = offsetY + ((cy / viewHeight) * svgRect.height);

        let left = x + 16;
        let top = y - tooltip.offsetHeight - 16;

        if (left + tooltip.offsetWidth > plot.clientWidth - 12) {
            left = x - tooltip.offsetWidth - 16;
        }

        if (left < 12) {
            left = 12;
        }

        if (top < 12) {
            top = y + 16;
        }

        if (top + tooltip.offsetHeight > plot.clientHeight - 12) {
            top = plot.clientHeight - tooltip.offsetHeight - 12;
        }

        tooltip.style.left = `${left}px`;
        tooltip.style.top = `${top}px`;
    }

    function showPointTooltip(plot, target) {
        const tooltip = plot.querySelector(".metrics-chart__tooltip");
        const svg = plot.querySelector(".metrics-chart__svg");
        const guide = plot.querySelector(".metrics-chart__hover-guide");
        const periodNode = tooltip?.querySelector(".metrics-chart__tooltip-period");
        const seriesNode = tooltip?.querySelector(".metrics-chart__tooltip-series");
        const scoreNode = tooltip?.querySelector('[data-role="score"]');
        const annualValueNode = tooltip?.querySelector('[data-role="annualValue"]');

        if (!tooltip || !svg || !guide || !periodNode || !seriesNode || !scoreNode || !annualValueNode) {
            return;
        }

        periodNode.textContent = target.dataset.category || "";
        seriesNode.textContent = target.dataset.series || "";
        scoreNode.textContent = formatScoreValue(target.dataset.score);
        annualValueNode.textContent = formatNumber(target.dataset.annualValue);

        tooltip.hidden = false;
        setActivePoint(plot, target);

        const cx = Number(target.getAttribute("cx") || 0);
        const cy = Number(target.getAttribute("cy") || 0);

        guide.setAttribute("x1", cx);
        guide.setAttribute("x2", cx);
        guide.classList.add("is-visible");

        positionTooltip(plot, svg, tooltip, cx, cy);
    }

    function bindChartInteractions() {
        chartsContainer?.querySelectorAll(".metrics-chart__plot").forEach(plot => {
            const targets = plot.querySelectorAll(".metrics-chart__target");
            plot.addEventListener("mouseleave", () => hidePointTooltip(plot));

            targets.forEach(target => {
                target.addEventListener("mouseenter", () => showPointTooltip(plot, target));
                target.addEventListener("focus", () => showPointTooltip(plot, target));
                target.addEventListener("blur", () => hidePointTooltip(plot));
                target.addEventListener("click", () => showPointTooltip(plot, target));
                target.addEventListener("keydown", event => {
                    if (event.key === "Enter" || event.key === " ") {
                        event.preventDefault();
                        showPointTooltip(plot, target);
                    }

                    if (event.key === "Escape") {
                        hidePointTooltip(plot);
                        target.blur();
                    }
                });
            });
        });
    }

    async function loadDashboard() {
        const previousDashboard = state.dashboard;
        const requestedSeller = state.seller;

        setLoading(true);
        setStatus("info", "Consultando metricas en Dataverse...");

        try {
            const dashboard = await fetchJson(buildDashboardUrl());
            state.dashboard = dashboard;
            state.filter = dashboard.filter || state.filter;
            state.view = dashboard.view || state.view;
            state.seller = dashboard.appliedSellerKey || "";
            state.updatedAt = new Date();

            renderSellerOptions(dashboard);
            updateSummary(dashboard);
            updateInsights(dashboard);
            syncViewLayout(dashboard);
            renderCharts(dashboard);

            if (state.view === "individual" && requestedSeller && requestedSeller !== state.seller) {
                setStatus("info", "El vendedor seleccionado no tiene datos en este rango. Se muestran todos los vendedores.");
            } else {
                setStatus("", "");
            }
        } catch (error) {
            console.error(error);

            if (previousDashboard) {
                state.dashboard = previousDashboard;
                state.filter = previousDashboard.filter || state.filter;
                state.view = previousDashboard.view || state.view;
                state.seller = previousDashboard.appliedSellerKey || "";

                renderSellerOptions(previousDashboard);
                updateSummary(previousDashboard);
                updateInsights(previousDashboard);
                syncViewLayout(previousDashboard);
                renderCharts(previousDashboard);
            } else {
                state.dashboard = null;
                state.updatedAt = null;
                renderSellerOptions(null);
                updateSummary(null);
                updateInsights(null);
                syncViewLayout(null);
                renderCharts(null);
            }

            setStatus("error", error?.message || "No fue posible cargar las metricas.");
        } finally {
            setLoading(false);
        }
    }

    filterButtons.forEach(button => {
        button.addEventListener("click", async () => {
            const nextFilter = button.dataset.filter;
            if (!nextFilter || nextFilter === state.filter || state.isLoading) {
                return;
            }

            state.filter = nextFilter;
            await loadDashboard();
        });
    });

    viewButtons.forEach(button => {
        button.addEventListener("click", async () => {
            const nextView = button.dataset.view;
            if (!nextView || nextView === state.view || state.isLoading) {
                return;
            }

            state.view = nextView;
            await loadDashboard();
        });
    });

    sellerFilter?.addEventListener("change", async () => {
        if (state.isLoading || state.view !== "individual") {
            return;
        }

        state.seller = sellerFilter.value || "";
        await loadDashboard();
    });

    refreshButton?.addEventListener("click", loadDashboard);
    renderSellerOptions(null);
    updateSummary(null);
    updateInsights(null);
    syncViewLayout(null);
    setLoading(false);
    loadDashboard();
})();
