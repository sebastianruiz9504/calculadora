(function () {
    const app = document.getElementById("licCruceApp");
    if (!app) {
        return;
    }

    const loadUrl = app.dataset.loadUrl || "";
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

    matrixWrap?.addEventListener("click", (event) => {
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
        const segment = currentSegments.find((item) => item.key === selectedSegmentKey)
            || currentSegments.find((item) => Number(item.recordsCount || 0) > 0)
            || currentSegments[0]
            || null;
        const months = Array.isArray(currentData?.matrixMonths) ? currentData.matrixMonths : [];

        if (!segment || months.length === 0) {
            renderTotals({});
            if (matrixTitle) {
                matrixTitle.textContent = "Sin datos";
            }
            if (matrixSummary) {
                matrixSummary.textContent = "";
            }
            if (matrixWrap) {
                matrixWrap.innerHTML = "<div class=\"licx-empty\">No hay registros para este periodo.</div>";
            }
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
        const rows = Array.isArray(segment.rows) ? segment.rows : [];
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
                            <th rowspan="2" class="licx-client-col">Cliente</th>
                            ${months.map((month) => `<th colspan="4" class="text-center licx-month-head">${escapeHtml(month.label || month.key || "")}</th>`).join("")}
                            ${showAccumulated ? "<th rowspan=\"2\" class=\"text-end licx-accum-col\">Utilidad acumulada</th>" : ""}
                        </tr>
                        <tr>
                            ${months.map(() => `
                                <th class="text-end">Costo</th>
                                <th class="text-end">Venta</th>
                                <th class="text-end">% utilidad</th>
                                <th class="text-end">Utilidad</th>
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
                            <th>Producto / licencia</th>
                            <th>Cliente</th>
                            ${isBilling ? "<th>Vertical</th>" : "<th>Account ID</th><th>Account</th>"}
                            <th>Tipo</th>
                            ${isBilling ? "<th class=\"text-end\">Total factura</th><th class=\"text-end\">IVA</th>" : ""}
                            <th class="text-end">${isBilling ? "Venta sin IVA" : "Costo"}</th>
                            ${canMoveCostMonth ? "<th>Mover mes</th>" : ""}
                            <th>Record ID</th>
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
                <td>
                    <strong>${escapeHtml(product)}</strong>
                    <small>${escapeHtml(item.productoId || "")}</small>
                </td>
                <td>
                    <strong>${escapeHtml(item.cliente || "-")}</strong>
                    <small>${escapeHtml(item.clienteId || "")}</small>
                </td>
                ${sourceCells}
                <td>${escapeHtml(item.tipoContrato || "-")}</td>
                ${billingTotals}
                <td class="text-end">${formatCurrency(item.valor)}</td>
                ${moveCostCell}
                <td><code>${escapeHtml(item.recordId || "")}</code></td>
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
