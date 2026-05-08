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
    const segmentSelect = document.getElementById("licCruceSegmentSelect");
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
    let currentSegments = [];
    let currentRowsByKey = new Map();
    let selectedSegmentKey = "";
    let traceDialog = null;

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

    segmentSelect?.addEventListener("change", () => {
        selectedSegmentKey = segmentSelect.value || "";
        renderSelectedSegment();
    });

    segmentsWrap?.addEventListener("click", (event) => {
        const rowElement = event.target.closest("tr[data-row-key]");
        if (!rowElement) {
            return;
        }

        const row = currentRowsByKey.get(rowElement.dataset.rowKey || "");
        if (row?.canInspect) {
            openTraceDialog(row);
        }
    });

    segmentsWrap?.addEventListener("keydown", (event) => {
        if (event.key !== "Enter" && event.key !== " ") {
            return;
        }

        const rowElement = event.target.closest("tr[data-row-key]");
        if (!rowElement) {
            return;
        }

        const row = currentRowsByKey.get(rowElement.dataset.rowKey || "");
        if (row?.canInspect) {
            event.preventDefault();
            openTraceDialog(row);
        }
    });

    document.addEventListener("keydown", (event) => {
        if (event.key === "Escape") {
            closeTraceDialog();
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

        currentSegments = Array.isArray(segments) ? segments : [];

        if (currentSegments.length === 0) {
            if (segmentSelect) {
                segmentSelect.innerHTML = "";
                segmentSelect.disabled = true;
            }
            segmentsWrap.innerHTML = "<div class=\"licx-empty\">No hay registros para este periodo.</div>";
            return;
        }

        const selectedExists = currentSegments.some((segment) => segment.key === selectedSegmentKey);
        const preferredSegment = selectedExists
            ? currentSegments.find((segment) => segment.key === selectedSegmentKey)
            : currentSegments.find((segment) => Number(segment.recordsCount || 0) > 0) || currentSegments[0];
        selectedSegmentKey = preferredSegment?.key || "";

        if (segmentSelect) {
            segmentSelect.innerHTML = currentSegments.map((segment) => `
                <option value="${escapeHtml(segment.key || "")}">${escapeHtml(segment.label || "Sin tipo")} (${numberFormatter.format(Number(segment.recordsCount || 0))})</option>
            `).join("");
            segmentSelect.value = selectedSegmentKey;
            segmentSelect.disabled = currentSegments.length <= 1;
        }

        renderSelectedSegment();
    }

    function renderSelectedSegment() {
        if (!segmentsWrap) {
            return;
        }

        const segment = currentSegments.find((item) => item.key === selectedSegmentKey) || currentSegments[0];
        if (!segment) {
            segmentsWrap.innerHTML = "<div class=\"licx-empty\">No hay registros para este periodo.</div>";
            return;
        }

        const totals = segment.totals || {};
        const counts = segment.statusCounts || {};
        const rows = Array.isArray(segment.rows) ? segment.rows : [];
        currentRowsByKey = new Map(rows.map((row) => [row.rowKey || "", row]));

        segmentsWrap.innerHTML = `
            <div class="licx-segment-detail is-${escapeHtml(segment.key || "otros")}">
                <div class="licx-segment-totals">
                    <span><strong>${formatCurrency(totals.totalCostosLicenciamiento)}</strong> costo</span>
                    <span><strong>${formatCurrency(totals.totalFacturacionRelacionada)}</strong> facturacion</span>
                    <span class="${Number(totals.margenBrutoTotal || 0) < 0 ? "is-negative" : ""}"><strong>${formatCurrency(totals.margenBrutoTotal)}</strong> margen</span>
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
            </div>
        `;
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
        const isInspectable = Boolean(row.canInspect);
        const rowClasses = [
            row.isMarginAlert ? "is-alert" : "",
            isInspectable ? "is-inspectable" : ""
        ].filter(Boolean).join(" ");

        return `
            <tr class="${rowClasses}" data-row-key="${escapeHtml(row.rowKey || "")}" ${isInspectable ? "tabindex=\"0\" title=\"Ver detalle del cruce\"" : ""}>
                <td data-label="Cliente">
                    <strong>${escapeHtml(row.cliente || "Cliente sin nombre")}</strong>
                    <small>${formatSourceCount(row)}</small>
                </td>
                <td data-label="NIT">${escapeHtml(row.nitCliente || "-")}</td>
                <td data-label="Vertical">${escapeHtml(row.vertical || "-")}</td>
                <td data-label="Costo" class="text-end">${formatCurrency(row.costoLicenciamiento)}</td>
                <td data-label="Facturacion sin IVA" class="text-end">${formatCurrency(row.facturacionSinIva)}</td>
                <td data-label="Margen" class="text-end ${Number(row.margenBruto || 0) < 0 ? "is-negative" : ""}">${formatCurrency(row.margenBruto)}</td>
                <td data-label="Margen %" class="text-end ${Number(row.margenBrutoPct || 0) < 0 ? "is-negative" : ""}">${formatPercent(row.margenBrutoPct)}</td>
                <td data-label="Estado"><span class="licx-badge ${getStatusClass(row.estadoCruce)}">${escapeHtml(row.estadoCruce || "-")}</span></td>
            </tr>
        `;
    }

    function openTraceDialog(row) {
        const dialog = ensureTraceDialog();
        const trace = row.trace || {};
        dialog.querySelector("[data-trace-title]").textContent = row.cliente || "Cliente sin nombre";
        dialog.querySelector("[data-trace-subtitle]").textContent = `${row.estadoCruce || "-"} | ${row.tipoContrato || "-"}`;
        dialog.querySelector("[data-trace-mode]").textContent = trace.matchMode || row.estadoCruce || "-";
        dialog.querySelector("[data-trace-rule]").textContent = trace.rule || "-";
        dialog.querySelector("[data-trace-client-cost]").textContent = trace.costClientId || "Sin cliente en costo";
        dialog.querySelector("[data-trace-client-billing]").textContent = trace.billingClientId || "Sin cliente en facturacion";
        dialog.querySelector("[data-trace-cost-items]").innerHTML = buildTraceItems(trace.costItems || [], "cost");
        dialog.querySelector("[data-trace-billing-items]").innerHTML = buildTraceItems(trace.billingItems || [], "billing");
        dialog.hidden = false;
        document.body.classList.add("licx-modal-open");
        dialog.querySelector("[data-trace-close]")?.focus();
    }

    function closeTraceDialog() {
        if (!traceDialog) {
            return;
        }

        traceDialog.hidden = true;
        document.body.classList.remove("licx-modal-open");
    }

    function ensureTraceDialog() {
        if (traceDialog) {
            return traceDialog;
        }

        traceDialog = document.createElement("div");
        traceDialog.className = "licx-modal";
        traceDialog.hidden = true;
        traceDialog.innerHTML = `
            <div class="licx-modal__backdrop" data-trace-backdrop></div>
            <section class="licx-modal__dialog" role="dialog" aria-modal="true" aria-label="Detalle del cruce">
                <header class="licx-modal__header">
                    <div>
                        <div class="licx-kicker">Revision de cruce</div>
                        <h2 data-trace-title>Cliente</h2>
                        <span data-trace-subtitle></span>
                    </div>
                    <button type="button" class="btn-close" aria-label="Cerrar" data-trace-close></button>
                </header>
                <div class="licx-modal__body">
                    <section class="licx-trace-summary">
                        <div>
                            <span>Modo</span>
                            <strong data-trace-mode>-</strong>
                        </div>
                        <div>
                            <span>Cliente costo</span>
                            <strong data-trace-client-cost>-</strong>
                        </div>
                        <div>
                            <span>Cliente facturacion</span>
                            <strong data-trace-client-billing>-</strong>
                        </div>
                    </section>
                    <p class="licx-trace-rule" data-trace-rule></p>
                    <div class="licx-trace-grid">
                        <section>
                            <h3>Items costo</h3>
                            <div data-trace-cost-items></div>
                        </section>
                        <section>
                            <h3>Items facturacion</h3>
                            <div data-trace-billing-items></div>
                        </section>
                    </div>
                </div>
            </section>
        `;
        traceDialog.querySelector("[data-trace-close]")?.addEventListener("click", closeTraceDialog);
        traceDialog.querySelector("[data-trace-backdrop]")?.addEventListener("click", closeTraceDialog);
        document.body.appendChild(traceDialog);
        return traceDialog;
    }

    function buildTraceItems(items, type) {
        if (!Array.isArray(items) || items.length === 0) {
            return "<div class=\"licx-empty licx-empty--small\">Sin items en esta fuente.</div>";
        }

        const valueLabel = type === "billing" ? "Sin IVA" : "Valor";
        const extraHeader = type === "billing" ? "<th class=\"text-end\">Total</th><th class=\"text-end\">IVA</th>" : "<th>Account ID</th><th>Account</th>";
        return `
            <div class="licx-table-wrap licx-table-wrap--trace">
                <table class="table align-middle licx-table licx-table--trace">
                    <thead>
                        <tr>
                            <th>Referencia</th>
                            <th>Record ID</th>
                            <th>Cliente</th>
                            <th>Tipo</th>
                            <th>Vertical</th>
                            <th>Fecha</th>
                            ${extraHeader}
                            <th class="text-end">${valueLabel}</th>
                        </tr>
                    </thead>
                    <tbody>
                        ${items.map((item) => buildTraceItemRow(item, type)).join("")}
                    </tbody>
                </table>
            </div>
        `;
    }

    function buildTraceItemRow(item, type) {
        const extraCells = type === "billing"
            ? `<td class="text-end">${formatCurrency(item.valorTotal)}</td><td class="text-end">${formatCurrency(item.iva)}</td>`
            : `<td>${escapeHtml(item.accountId || "-")}</td><td>${escapeHtml(item.account || "-")}</td>`;
        return `
            <tr>
                <td>${escapeHtml(item.referencia || "-")}</td>
                <td><code>${escapeHtml(item.recordId || "-")}</code></td>
                <td>
                    <strong>${escapeHtml(item.cliente || "-")}</strong>
                    <small>${escapeHtml(item.clienteId || "")}</small>
                </td>
                <td>${escapeHtml(item.tipoContrato || "-")}</td>
                <td>${escapeHtml(item.vertical || "-")}</td>
                <td>${escapeHtml(item.fecha || item.mes || "-")}</td>
                ${extraCells}
                <td class="text-end">${formatCurrency(item.valor)}</td>
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
