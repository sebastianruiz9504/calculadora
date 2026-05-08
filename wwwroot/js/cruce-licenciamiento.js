(function () {
    const app = document.getElementById("licCruceApp");
    if (!app) {
        return;
    }

    const loadUrl = app.dataset.loadUrl || "";
    const updateCostContractUrl = app.dataset.updateCostContractUrl || "";
    const updateBillingContractUrl = app.dataset.updateBillingContractUrl || "";
    const updateCostAccountUrl = app.dataset.updateCostAccountUrl || "";
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
    const openOrphansButton = document.getElementById("licCruceOpenOrphans");
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

    let currentData = null;
    let currentSegments = [];
    let selectedSegmentKey = "";
    let orphanDialog = null;

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

    openOrphansButton?.addEventListener("click", () => {
        openOrphanDialog();
    });

    document.addEventListener("keydown", (event) => {
        if (event.key === "Escape") {
            closeOrphanDialog();
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
            updateOrphanButton(0);
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
        updateOrphanButton(Array.isArray(currentData?.orphans) ? currentData.orphans.length : 0);
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
    }

    function updateOrphanButton(count) {
        if (!openOrphansButton) {
            return;
        }

        openOrphansButton.textContent = `Ver ${numberFormatter.format(Number(count || 0))}`;
        openOrphansButton.disabled = Number(count || 0) === 0;
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

        matrixWrap.innerHTML = `
            <div class="licx-table-wrap">
                <table class="table align-middle licx-table licx-matrix-table">
                    <thead>
                        <tr>
                            <th rowspan="2" class="licx-client-col">Cliente</th>
                            ${months.map((month) => `<th colspan="4" class="text-center licx-month-head">${escapeHtml(month.label || month.key || "")}</th>`).join("")}
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
                        ${rows.map((row) => buildMatrixRow(row, months)).join("")}
                    </tbody>
                </table>
            </div>
        `;
    }

    function buildMatrixRow(row, months) {
        const cellsByMonth = new Map((Array.isArray(row.cells) ? row.cells : []).map((cell) => [cell.mes || "", cell]));
        return `
            <tr class="${row.hasNegativeMargin ? "is-alert" : ""} ${row.hasOrphans ? "has-orphans" : ""}">
                <th scope="row" class="licx-client-col">
                    <strong>${escapeHtml(row.cliente || "Cliente sin nombre")}</strong>
                    <small>${escapeHtml(row.nitCliente || row.clienteId || "")}</small>
                </th>
                ${months.map((month) => buildMatrixCellGroup(cellsByMonth.get(month.key || ""))).join("")}
            </tr>
        `;
    }

    function buildMatrixCellGroup(cell) {
        const safeCell = cell || {};
        const classes = [
            safeCell.hasNegativeMargin ? "is-negative-cell" : "",
            safeCell.hasOrphans ? "is-orphan-cell" : ""
        ].filter(Boolean).join(" ");
        return `
            <td class="text-end ${classes}">${formatCurrency(safeCell.costoLicenciamiento)}</td>
            <td class="text-end ${classes}">${formatCurrency(safeCell.facturacionSinIva)}</td>
            <td class="text-end ${classes}">${formatPercent(safeCell.utilidadPct)}</td>
            <td class="text-end ${classes} ${Number(safeCell.utilidadValor || 0) < 0 ? "is-negative" : ""}">${formatCurrency(safeCell.utilidadValor)}</td>
        `;
    }

    function openOrphanDialog() {
        const dialog = ensureOrphanDialog();
        dialog.querySelector("[data-orphan-count]").textContent =
            numberFormatter.format(Number(currentData?.orphans?.length || 0));
        dialog.querySelector("[data-orphan-table]").innerHTML = buildOrphanTable(currentData?.orphans || []);
        dialog.hidden = false;
        document.body.classList.add("licx-modal-open");
        dialog.querySelector("[data-orphan-close]")?.focus();
    }

    function closeOrphanDialog() {
        if (!orphanDialog) {
            return;
        }

        orphanDialog.hidden = true;
        document.body.classList.remove("licx-modal-open");
    }

    function ensureOrphanDialog() {
        if (orphanDialog) {
            return orphanDialog;
        }

        orphanDialog = document.createElement("div");
        orphanDialog.className = "licx-modal";
        orphanDialog.hidden = true;
        orphanDialog.innerHTML = `
            <div class="licx-modal__backdrop" data-orphan-backdrop></div>
            <section class="licx-modal__dialog" role="dialog" aria-modal="true" aria-label="Registros huerfanos">
                <header class="licx-modal__header">
                    <div>
                        <div class="licx-kicker">Revision</div>
                        <h2>Registros huerfanos</h2>
                        <span><strong data-orphan-count>0</strong> registro(s)</span>
                    </div>
                    <button type="button" class="btn-close" aria-label="Cerrar" data-orphan-close></button>
                </header>
                <div class="licx-modal__body">
                    <div data-orphan-table></div>
                </div>
            </section>
        `;
        orphanDialog.querySelector("[data-orphan-close]")?.addEventListener("click", closeOrphanDialog);
        orphanDialog.querySelector("[data-orphan-backdrop]")?.addEventListener("click", closeOrphanDialog);
        orphanDialog.addEventListener("click", handleOrphanAction);
        document.body.appendChild(orphanDialog);
        return orphanDialog;
    }

    function buildOrphanTable(orphans) {
        if (!Array.isArray(orphans) || orphans.length === 0) {
            return "<div class=\"licx-empty\">No hay registros huerfanos para corregir.</div>";
        }

        return `
            <div class="licx-table-wrap licx-table-wrap--orphans">
                <table class="table align-middle licx-table licx-orphan-table">
                    <thead>
                        <tr>
                            <th>Fuente</th>
                            <th>Mes</th>
                            <th>Cliente</th>
                            <th>Referencia</th>
                            <th class="text-end">Valor</th>
                            <th>Tipo de contrato</th>
                            <th>Account ID consumo</th>
                        </tr>
                    </thead>
                    <tbody>
                        ${orphans.map(buildOrphanRow).join("")}
                    </tbody>
                </table>
            </div>
        `;
    }

    function buildOrphanRow(orphan) {
        const isCost = orphan.source === "cost";
        const typeOptions = isCost
            ? currentData?.costContractTypeOptions || []
            : currentData?.billingContractTypeOptions || [];
        return `
            <tr data-source="${escapeHtml(orphan.source || "")}" data-record-id="${escapeHtml(orphan.recordId || "")}">
                <td>
                    <span class="licx-badge ${isCost ? "is-warning" : "is-neutral"}">${isCost ? "Costo" : "Facturacion"}</span>
                    <small>${escapeHtml(orphan.status || "")}</small>
                </td>
                <td>${escapeHtml(orphan.mes || "-")}</td>
                <td>
                    <strong>${escapeHtml(orphan.cliente || "-")}</strong>
                    <small>${escapeHtml(orphan.reason || "")}</small>
                </td>
                <td>
                    <div>${escapeHtml(orphan.referencia || "-")}</div>
                    <code>${escapeHtml(orphan.recordId || "")}</code>
                </td>
                <td class="text-end">${formatCurrency(orphan.valor)}</td>
                <td>
                    <div class="licx-edit-group">
                        <select class="form-select form-select-sm" data-contract-select>
                            ${typeOptions.map((option) => `
                                <option value="${Number(option.value || 0)}" ${Number(option.value || 0) === Number(orphan.tipoContratoValue || 0) ? "selected" : ""}>
                                    ${escapeHtml(option.label || option.value || "")}
                                </option>
                            `).join("")}
                        </select>
                        <button type="button" class="btn btn-sm btn-outline-primary" data-action="${isCost ? "save-cost-contract" : "save-billing-contract"}">Guardar</button>
                    </div>
                    <small>Actual: ${escapeHtml(orphan.tipoContrato || "-")}</small>
                </td>
                <td>
                    ${isCost ? `
                        <div class="licx-edit-group">
                            <input class="form-control form-control-sm" data-account-input value="${escapeHtml(orphan.account || orphan.accountId || "")}" placeholder="Account ID" />
                            <button type="button" class="btn btn-sm btn-outline-primary" data-action="save-cost-account">Guardar</button>
                        </div>
                        <small>${escapeHtml(orphan.accountId || "")}</small>
                    ` : "<span class=\"licx-muted\">No aplica</span>"}
                </td>
            </tr>
        `;
    }

    async function handleOrphanAction(event) {
        const button = event.target.closest("[data-action]");
        if (!button) {
            return;
        }

        const row = button.closest("tr[data-record-id]");
        const recordId = row?.dataset.recordId || "";
        const action = button.dataset.action || "";
        if (!recordId) {
            showStatus("error", "No se encontro el ID del registro.");
            return;
        }

        button.disabled = true;
        try {
            if (action === "save-cost-contract") {
                const value = Number(row.querySelector("[data-contract-select]")?.value || 0);
                await postJson(updateCostContractUrl, { recordIds: [recordId], contractTypeValue: value });
            } else if (action === "save-billing-contract") {
                const value = Number(row.querySelector("[data-contract-select]")?.value || 0);
                await postJson(updateBillingContractUrl, { recordIds: [recordId], contractTypeOptionValue: value });
            } else if (action === "save-cost-account") {
                const accountId = row.querySelector("[data-account-input]")?.value || "";
                await postJson(updateCostAccountUrl, { recordId, accountId });
            }

            closeOrphanDialog();
            showStatus("success", "Registro actualizado. Recalculando cruce...");
            await loadCruce();
        } catch (error) {
            showStatus("error", error instanceof Error ? error.message : "No fue posible actualizar el registro.");
        } finally {
            button.disabled = false;
        }
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
