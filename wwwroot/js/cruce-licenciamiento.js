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
    const status = document.getElementById("licCruceStatus");
    const segmentsWrap = document.getElementById("licCruceSegments");
    const alertsWrap = document.getElementById("licCruceAlerts");
    const validationsWrap = document.getElementById("licCruceValidations");
    const rankingWrap = document.getElementById("licCruceRanking");
    const costMonth = document.getElementById("licCruceCostMonth");
    const billingMonth = document.getElementById("licCruceBillingMonth");
    const closeState = document.getElementById("licCruceCloseState");

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
        const segments = Array.isArray(data?.contractSegments) ? data.contractSegments : [];
        const totals = data?.totals || {};
        const negativeCount = rows.filter((row) => Number(row.margenBruto || 0) < 0).length;

        totalCost.textContent = formatCurrency(totals.totalCostosLicenciamiento);
        totalBilling.textContent = formatCurrency(totals.totalFacturacionRelacionada);
        totalMargin.textContent = formatCurrency(totals.margenBrutoTotal);
        totalMargin.classList.toggle("is-negative", Number(totals.margenBrutoTotal || 0) < 0);
        totalMarginPct.textContent = formatPercent(totals.margenBrutoPct);
        totalMarginPct.classList.toggle("is-negative", Number(totals.margenBrutoPct || 0) < 0);

        costMonth.textContent = data?.mesCosto || "-";
        billingMonth.textContent = data?.mesFacturacion || "-";
        closeState.textContent = negativeCount > 0
            ? `${numberFormatter.format(negativeCount)} cliente(s) con margen negativo`
            : rows.length > 0 ? "Sin margen negativo" : "Sin datos";
        closeState.classList.toggle("is-negative", negativeCount > 0);

        renderSegments(segments);
        renderAlerts(Array.isArray(data?.alerts) ? data.alerts : []);
        renderValidations(Array.isArray(data?.validations) ? data.validations : []);
        renderRanking(rows);
    }

    function renderSegments(segments) {
        if (!segmentsWrap) {
            return;
        }

        if (segments.length === 0) {
            segmentsWrap.innerHTML = "<section class=\"licx-panel\"><div class=\"licx-empty\">No hay registros para este periodo.</div></section>";
            return;
        }

        segmentsWrap.innerHTML = segments.map((segment) => {
            const totals = segment.totals || {};
            const counts = segment.statusCounts || {};
            const rows = Array.isArray(segment.rows) ? segment.rows : [];
            return `
                <section class="licx-panel licx-segment is-${escapeHtml(segment.key || "otros")}">
                    <div class="licx-panel__header licx-segment__header">
                        <div>
                            <div class="licx-kicker">Tipo de contrato</div>
                            <h2>${escapeHtml(segment.label || "Sin tipo")}</h2>
                        </div>
                        <div class="licx-segment-totals">
                            <span><strong>${formatCurrency(totals.totalCostosLicenciamiento)}</strong> costo</span>
                            <span><strong>${formatCurrency(totals.totalFacturacionRelacionada)}</strong> facturacion</span>
                            <span class="${Number(totals.margenBrutoTotal || 0) < 0 ? "is-negative" : ""}"><strong>${formatCurrency(totals.margenBrutoTotal)}</strong> margen</span>
                        </div>
                    </div>
                    <div class="licx-summary-pills">
                        ${buildPill("Clientes", segment.recordsCount || 0, "neutral")}
                        ${buildPill("Match exacto", counts.matchExacto || 0, "ok")}
                        ${buildPill("Match probable", counts.matchProbable || 0, "probable")}
                        ${buildPill("Costo sin facturacion", counts.costoSinFacturacion || 0, "warning")}
                        ${buildPill("Facturacion sin costo", counts.facturacionSinCosto || 0, "neutral")}
                        ${buildPill("Margen negativo", segment.negativeMarginCount || 0, Number(segment.negativeMarginCount || 0) > 0 ? "danger" : "ok")}
                    </div>
                    ${buildSegmentTable(rows)}
                </section>
            `;
        }).join("");
    }

    function buildSegmentTable(rows) {
        if (rows.length === 0) {
            return "<div class=\"licx-empty\">No hay clientes en este tipo de contrato.</div>";
        }

        return `
            <div class="licx-table-wrap">
                <table class="table align-middle licx-table">
                    <thead>
                        <tr>
                            <th>Cliente</th>
                            <th>NIT</th>
                            <th>Producto/licencia</th>
                            <th>Vertical</th>
                            <th class="text-end">Costo</th>
                            <th class="text-end">Facturacion sin IVA</th>
                            <th class="text-end">Margen</th>
                            <th class="text-end">Margen %</th>
                            <th>Estado</th>
                        </tr>
                    </thead>
                    <tbody>
                        ${rows.map(buildSegmentRow).join("")}
                    </tbody>
                </table>
            </div>
        `;
    }

    function buildSegmentRow(row) {
        return `
            <tr class="${row.isMarginAlert ? "is-alert" : ""}">
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
        `;
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

        const negativeRows = rows
            .filter((row) => Number(row.margenBruto || 0) < 0)
            .sort((a, b) => Number(a.margenBruto || 0) - Number(b.margenBruto || 0))
            .slice(0, 8);

        rankingWrap.innerHTML = negativeRows.length === 0
            ? "<div class=\"licx-ranking__empty\">Sin clientes con margen negativo</div>"
            : negativeRows.map((row) => `
                <div class="licx-ranking__item">
                    <span>${escapeHtml(row.cliente || "Cliente sin nombre")} <small>${escapeHtml(row.tipoContrato || "")}</small></span>
                    <strong>${formatCurrency(row.margenBruto)}</strong>
                </div>
            `).join("");
    }

    function buildPill(label, count, tone) {
        return `<span class="licx-pill is-${tone}">${escapeHtml(label)} <strong>${numberFormatter.format(Number(count || 0))}</strong></span>`;
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
        if (statusValue === "Facturacion sin costo") {
            return "is-neutral";
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
