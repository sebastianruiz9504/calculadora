(function () {
    const app = document.getElementById("licCruceApp");
    if (!app) {
        return;
    }

    const loadUrl = app.dataset.loadUrl || "";
    const filtersForm = document.getElementById("licCruceFilters");
    const yearInput = document.getElementById("licCruceYear");
    const monthSelect = document.getElementById("licCruceMonth");
    const offsetSelect = document.getElementById("licCruceOffset");
    const thresholdInput = document.getElementById("licCruceThreshold");
    const status = document.getElementById("licCruceStatus");
    const rowsBody = document.getElementById("licCruceRows");
    const emptyState = document.getElementById("licCruceEmpty");
    const monthRowsBody = document.getElementById("licCruceMonthRows");
    const alertsWrap = document.getElementById("licCruceAlerts");
    const validationsWrap = document.getElementById("licCruceValidations");
    const rankingWrap = document.getElementById("licCruceRanking");
    const statusPills = document.getElementById("licCruceStatusPills");

    const totalCost = document.getElementById("licCruceTotalCost");
    const totalBilling = document.getElementById("licCruceTotalBilling");
    const totalMargin = document.getElementById("licCruceTotalMargin");
    const totalMarginPct = document.getElementById("licCruceTotalMarginPct");

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

    yearInput.value = app.dataset.defaultYear || new Date().getFullYear().toString();
    monthSelect.value = app.dataset.defaultMonth || "1";
    offsetSelect.value = app.dataset.defaultOffset || "1";
    thresholdInput.value = app.dataset.defaultThreshold || "20";

    filtersForm?.addEventListener("submit", async (event) => {
        event.preventDefault();
        await loadCruce();
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
            url.searchParams.set("billingOffsetMonths", offsetSelect.value || "1");
            url.searchParams.set("marginThresholdPercent", thresholdInput.value || "20");

            const response = await fetch(url.toString(), {
                headers: { Accept: "application/json" }
            });
            const payload = await readJsonResponse(response);
            renderCruce(payload);
            showStatus("success", payload.message || "Cruce actualizado.");
        } catch (error) {
            renderCruce(null);
            showStatus("error", error instanceof Error ? error.message : "No fue posible cargar el cruce.");
        } finally {
            setBusy(false);
        }
    }

    function renderCruce(data) {
        const rows = Array.isArray(data?.rows) ? data.rows : [];
        const totals = data?.totals || {};

        totalCost.textContent = formatCurrency(totals.totalCostosLicenciamiento);
        totalBilling.textContent = formatCurrency(totals.totalFacturacionRelacionada);
        totalMargin.textContent = formatCurrency(totals.margenBrutoTotal);
        totalMargin.classList.toggle("is-negative", Number(totals.margenBrutoTotal || 0) < 0);
        totalMarginPct.textContent = formatPercent(totals.margenBrutoPct);
        totalMarginPct.classList.toggle("is-negative", Number(totals.margenBrutoPct || 0) < 0);

        renderStatusPills(data?.statusCounts || {});
        renderRows(rows);
        renderMonthSummary(Array.isArray(data?.monthSummaries) ? data.monthSummaries : []);
        renderAlerts(Array.isArray(data?.alerts) ? data.alerts : []);
        renderValidations(Array.isArray(data?.validations) ? data.validations : []);
        renderRanking(rows);
    }

    function renderRows(rows) {
        if (!rowsBody) {
            return;
        }

        emptyState.hidden = rows.length > 0;
        rowsBody.innerHTML = rows.map((row) => `
            <tr class="${row.isMarginAlert ? "is-alert" : ""}">
                <td data-label="Mes cierre">${escapeHtml(row.mesCierre)}</td>
                <td data-label="Mes costo">${escapeHtml(row.mesCosto)}</td>
                <td data-label="Mes facturacion">${escapeHtml(row.mesFacturacion)}</td>
                <td data-label="Cliente">
                    <strong>${escapeHtml(row.cliente || "Cliente sin nombre")}</strong>
                    <small>${formatSourceCount(row)}</small>
                </td>
                <td data-label="NIT">${escapeHtml(row.nitCliente || "-")}</td>
                <td data-label="Producto/licencia">${escapeHtml(row.productoLicencia || "-")}</td>
                <td data-label="Vertical">${escapeHtml(row.vertical || "-")}</td>
                <td data-label="Costo" class="text-end">${formatCurrency(row.costoLicenciamiento)}</td>
                <td data-label="Facturacion sin IVA" class="text-end">${formatCurrency(row.facturacionSinIva)}</td>
                <td data-label="Margen" class="text-end ${Number(row.margenBruto || 0) < 0 ? "is-negative" : ""}">${formatCurrency(row.margenBruto)}</td>
                <td data-label="Margen %" class="text-end ${Number(row.margenBrutoPct || 0) < 0 ? "is-negative" : ""}">${formatPercent(row.margenBrutoPct)}</td>
                <td data-label="Estado"><span class="licx-badge ${getStatusClass(row.estadoCruce)}">${escapeHtml(row.estadoCruce || "-")}</span></td>
            </tr>
        `).join("");
    }

    function renderStatusPills(counts) {
        if (!statusPills) {
            return;
        }

        const items = [
            ["Match exacto", counts.matchExacto || 0, "ok"],
            ["Match probable", counts.matchProbable || 0, "probable"],
            ["Costo sin facturacion", counts.costoSinFacturacion || 0, "warning"],
            ["Facturacion sin costo", counts.facturacionSinCosto || 0, "warning"]
        ];

        statusPills.innerHTML = items.map(([label, count, tone]) => `
            <span class="licx-pill is-${tone}">${escapeHtml(label)} <strong>${numberFormatter.format(Number(count || 0))}</strong></span>
        `).join("");
    }

    function renderMonthSummary(rows) {
        if (!monthRowsBody) {
            return;
        }

        monthRowsBody.innerHTML = rows.map((row) => `
            <tr>
                <td>${escapeHtml(row.mesCierre || "-")}</td>
                <td class="text-end">${formatCurrency(row.costosLicenciamiento)}</td>
                <td class="text-end">${formatCurrency(row.facturacionRelacionada)}</td>
                <td class="text-end ${Number(row.margenBruto || 0) < 0 ? "is-negative" : ""}">${formatCurrency(row.margenBruto)}</td>
                <td class="text-end">${formatPercent(row.margenBrutoPct)}</td>
            </tr>
        `).join("");
    }

    function renderAlerts(alerts) {
        if (!alertsWrap) {
            return;
        }

        alertsWrap.innerHTML = alerts.map((alert) => `
            <article class="licx-alert is-${escapeHtml(alert.severity || "info")}">
                <span>${escapeHtml(alert.label || "Alerta")}</span>
                <strong>${numberFormatter.format(Number(alert.count || 0))}</strong>
                <small>${formatCurrency(alert.value)}</small>
            </article>
        `).join("");
    }

    function renderValidations(validations) {
        if (!validationsWrap) {
            return;
        }

        validationsWrap.innerHTML = validations.map((validation) => `
            <article class="licx-validation is-${escapeHtml(validation.status || "ok")}">
                <div>
                    <strong>${escapeHtml(validation.label || "Validacion")}</strong>
                    <span>${escapeHtml(validation.detail || "")}</span>
                </div>
            </article>
        `).join("");
    }

    function renderRanking(rows) {
        if (!rankingWrap) {
            return;
        }

        const lowMargin = rows
            .filter((row) => Number.isFinite(Number(row.margenBrutoPct)) && Number(row.facturacionSinIva || 0) > 0)
            .sort((a, b) => Number(a.margenBrutoPct || 0) - Number(b.margenBrutoPct || 0))
            .slice(0, 5);
        const costOnly = rows
            .filter((row) => row.estadoCruce === "Costo sin facturacion")
            .sort((a, b) => Number(b.costoLicenciamiento || 0) - Number(a.costoLicenciamiento || 0))
            .slice(0, 5);
        const billingOnly = rows
            .filter((row) => row.estadoCruce === "Facturacion sin costo")
            .sort((a, b) => Number(b.facturacionSinIva || 0) - Number(a.facturacionSinIva || 0))
            .slice(0, 5);

        rankingWrap.innerHTML = [
            buildRankingBlock("Menor margen", lowMargin, (row) => `${formatPercent(row.margenBrutoPct)} | ${formatCurrency(row.margenBruto)}`),
            buildRankingBlock("Costos sin facturacion", costOnly, (row) => formatCurrency(row.costoLicenciamiento)),
            buildRankingBlock("Facturacion sin costo", billingOnly, (row) => formatCurrency(row.facturacionSinIva))
        ].join("");
    }

    function buildRankingBlock(title, rows, valueFactory) {
        const content = rows.length === 0
            ? "<div class=\"licx-ranking__empty\">Sin registros</div>"
            : rows.map((row) => `
                <div class="licx-ranking__item">
                    <span>${escapeHtml(row.cliente || "Cliente sin nombre")}</span>
                    <strong>${escapeHtml(valueFactory(row))}</strong>
                </div>
            `).join("");

        return `
            <section class="licx-ranking__block">
                <h3>${escapeHtml(title)}</h3>
                ${content}
            </section>
        `;
    }

    function formatSourceCount(row) {
        const costCount = Number(row.costRecordCount || 0);
        const billingCount = Number(row.billingRecordCount || 0);
        const parts = [];
        if (costCount > 0) {
            parts.push(`${numberFormatter.format(costCount)} costo(s)`);
        }
        if (billingCount > 0) {
            parts.push(`${numberFormatter.format(billingCount)} factura(s)`);
        }

        return parts.join(" | ");
    }

    function getStatusClass(statusValue) {
        if (statusValue === "Match exacto") {
            return "is-ok";
        }
        if (statusValue === "Match probable") {
            return "is-probable";
        }
        return "is-warning";
    }

    function formatCurrency(value) {
        return copFormatter.format(Number(value || 0));
    }

    function formatPercent(value) {
        if (value === null || value === undefined || value === "") {
            return "N/A";
        }

        return `${percentFormatter.format(Number(value || 0))}%`;
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
