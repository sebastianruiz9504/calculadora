(() => {
    const app = document.getElementById("metricasApp");
    if (!app) {
        return;
    }

    const chartsContainer = document.getElementById("metricsChartsContainer");
    const statusBanner = document.getElementById("metricsStatusBanner");
    const refreshButton = document.getElementById("refreshMetricsBtn");
    const filterButtons = Array.from(document.querySelectorAll(".metrics-filter-btn"));
    const summaryRecords = document.getElementById("metricsSummaryRecords");
    const summarySellers = document.getElementById("metricsSummarySellers");
    const summaryVerticals = document.getElementById("metricsSummaryVerticals");
    const summaryScore = document.getElementById("metricsSummaryScore");
    const summaryAnnualValue = document.getElementById("metricsSummaryAnnualValue");

    const numberFormatter = new Intl.NumberFormat("es-CO", {
        minimumFractionDigits: 0,
        maximumFractionDigits: 2
    });

    const scoreFormatter = new Intl.NumberFormat("es-CO", {
        minimumFractionDigits: 2,
        maximumFractionDigits: 2
    });

    const state = {
        filter: app.dataset.initialFilter || "this-year",
        dashboard: null,
        isLoading: false
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
        filterButtons.forEach(button => {
            button.disabled = loading;
            button.classList.toggle("active", button.dataset.filter === state.filter);
        });
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

    function buildSampleLabelIndexes(length) {
        if (length <= 6) {
            return new Set(Array.from({ length }, (_, index) => index));
        }

        const indexes = new Set([0, length - 1]);
        const step = Math.ceil(length / 6);
        for (let index = 0; index < length; index += step) {
            indexes.add(index);
        }

        return indexes;
    }

    function buildLinePath(points) {
        if (!points.length) {
            return "";
        }

        return points.map((point, index) => `${index === 0 ? "M" : "L"} ${point.x.toFixed(2)} ${point.y.toFixed(2)}`).join(" ");
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

    function renderChartSvg(chart) {
        const categories = Array.isArray(chart.categories) ? chart.categories : [];
        const series = Array.isArray(chart.series) ? chart.series : [];

        if (!categories.length || !series.length) {
            return `<div class="metrics-chart__empty">${escapeHtml(chart.emptyMessage || "No hay datos para este grafico.")}</div>`;
        }

        const width = 760;
        const height = 260;
        const padding = { top: 18, right: 18, bottom: 34, left: 48 };
        const plotWidth = width - padding.left - padding.right;
        const plotHeight = height - padding.top - padding.bottom;
        const maxValue = Math.max(1, ...series.flatMap(item => item.values || []).map(value => Number(value || 0)));
        const labelIndexes = buildSampleLabelIndexes(categories.length);
        const denominator = Math.max(categories.length - 1, 1);

        const gridLines = Array.from({ length: 5 }, (_, index) => {
            const ratio = index / 4;
            const y = padding.top + plotHeight - (plotHeight * ratio);
            const value = maxValue * ratio;
            return `
                <line class="metrics-chart__grid" x1="${padding.left}" y1="${y}" x2="${width - padding.right}" y2="${y}"></line>
                <text class="metrics-chart__axis-label" x="${padding.left - 8}" y="${y + 4}" text-anchor="end">${escapeHtml(formatNumber(value))}</text>
            `;
        }).join("");

        const xLabels = categories.map((label, index) => {
            if (!labelIndexes.has(index)) {
                return "";
            }

            const x = padding.left + (plotWidth * (index / denominator));
            return `<text class="metrics-chart__axis-label" x="${x}" y="${height - 8}" text-anchor="middle">${escapeHtml(label)}</text>`;
        }).join("");

        const defs = series.map((item, index) => `
            <linearGradient id="metrics-gradient-${escapeHtml(chart.key)}-${index}" x1="0" x2="0" y1="0" y2="1">
                <stop offset="0%" stop-color="${escapeHtml(item.color)}" stop-opacity=".26"></stop>
                <stop offset="100%" stop-color="${escapeHtml(item.color)}" stop-opacity="0"></stop>
            </linearGradient>
        `).join("");

        const paths = series.map((item, index) => {
            const values = Array.isArray(item.values) ? item.values : [];
            const points = values.map((value, pointIndex) => {
                const x = padding.left + (plotWidth * (pointIndex / denominator));
                const y = padding.top + plotHeight - ((Number(value || 0) / maxValue) * plotHeight);
                return { x, y, value: Number(value || 0) };
            });

            const linePath = buildLinePath(points);
            const areaPath = buildAreaPath(points, padding.top + plotHeight);
            const circles = points.map(point => `
                <circle class="metrics-chart__dot" cx="${point.x}" cy="${point.y}" r="3.6" fill="${escapeHtml(item.color)}"></circle>
            `).join("");

            return `
                ${series.length === 1 ? `<path class="metrics-chart__area" d="${areaPath}" fill="url(#metrics-gradient-${escapeHtml(chart.key)}-${index})"></path>` : ""}
                <path class="metrics-chart__line" d="${linePath}" stroke="${escapeHtml(item.color)}"></path>
                ${circles}
            `;
        }).join("");

        return `
            <div class="metrics-chart__plot">
                <svg class="metrics-chart__svg" viewBox="0 0 ${width} ${height}" preserveAspectRatio="none" role="img" aria-label="${escapeHtml(chart.title)}">
                    <defs>${defs}</defs>
                    ${gridLines}
                    ${paths}
                    ${xLabels}
                </svg>
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
                    <div class="metrics-chart__legend-item">
                        <div class="metrics-chart__legend-main">
                            <span class="metrics-chart__legend-color" style="background:${escapeHtml(item.color)}"></span>
                            <span class="metrics-chart__legend-name">${escapeHtml(item.name)}</span>
                        </div>
                        <div class="metrics-chart__legend-meta">
                            <span>Puntaje ${escapeHtml(formatScoreValue(item.totalScore))}</span>
                            <span class="metrics-chart__legend-highlight">Contratos ${escapeHtml(formatNumber(item.totalAnnualValue))}</span>
                        </div>
                    </div>
                `).join("")}
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
                    No hay metricas para mostrar en este rango.
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
                    </div>
                    <div class="metrics-chart__totals">
                        <span class="metrics-chart__total-label">Puntaje total</span>
                        <span class="metrics-chart__total-value">${escapeHtml(formatScoreValue(chart.totalScore))}</span>
                        <span class="metrics-chart__total-label mt-2">Valor contratos</span>
                        <span class="metrics-chart__total-value">${escapeHtml(formatNumber(chart.totalAnnualValue))}</span>
                    </div>
                </div>
                ${renderChartSvg(chart)}
                ${renderLegend(chart)}
            </article>
        `).join("");
    }

    async function loadDashboard() {
        setLoading(true);
        setStatus("info", "Consultando metricas en Dataverse...");

        try {
            const dashboard = await fetchJson(`${app.dataset.chartsUrl}?filter=${encodeURIComponent(state.filter)}`);
            state.dashboard = dashboard;
            updateSummary(dashboard);
            renderCharts(dashboard);
            setStatus("", "");
        } catch (error) {
            console.error(error);
            state.dashboard = null;
            updateSummary(null);
            renderCharts(null);
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

    refreshButton?.addEventListener("click", loadDashboard);
    updateSummary(null);
    setLoading(false);
    loadDashboard();
})();
