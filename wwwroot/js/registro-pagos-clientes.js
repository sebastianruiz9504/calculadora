(() => {
    const app = document.getElementById("registroPagosClientesApp");
    if (!app) {
        return;
    }

    const loadUrl = app.dataset.loadUrl || "";
    const saveUrl = app.dataset.saveUrl || "";
    const statusBanner = document.getElementById("rpcStatusBanner");
    const resultsCount = document.getElementById("rpcResultsCount");
    const rowsBody = document.getElementById("rpcRowsBody");
    const emptyState = document.getElementById("rpcEmptyState");
    const tableTitle = document.getElementById("rpcTableTitle");
    const tabButtons = Array.from(document.querySelectorAll("[data-rpc-tab]"));
    const pendingRetentionsCount = document.getElementById("rpcPendingRetentionsCount");
    const refreshButton = document.getElementById("rpcRefreshBtn");
    const clearFiltersButton = document.getElementById("rpcClearFiltersBtn");

    const invoiceFilter = document.getElementById("rpcInvoiceFilter");
    const emissionFilter = document.getElementById("rpcEmissionFilter");
    const clientFilter = document.getElementById("rpcClientFilter");
    const clientFilterOptions = document.getElementById("rpcClientFilterOptions");
    const totalMinFilter = document.getElementById("rpcTotalMinFilter");
    const totalMaxFilter = document.getElementById("rpcTotalMaxFilter");
    const statusFilter = document.getElementById("rpcStatusFilter");

    const summaryRecords = document.getElementById("rpcSummaryRecords");
    const summaryPaid = document.getElementById("rpcSummaryPaid");
    const summaryOverdue = document.getElementById("rpcSummaryOverdue");
    const summaryPendingValue = document.getElementById("rpcSummaryPendingValue");

    const modal = document.getElementById("rpcPaymentModal");
    const modalCloseButton = document.getElementById("rpcModalCloseBtn");
    const modalCancelButton = document.getElementById("rpcModalCancelBtn");
    const modalStatus = document.getElementById("rpcModalStatus");
    const paymentForm = document.getElementById("rpcPaymentForm");
    const modalTitle = document.getElementById("rpcPaymentTitle");
    const modalSubtitle = document.getElementById("rpcPaymentSubtitle");
    const recordIdInput = document.getElementById("rpcRecordIdInput");
    const modalInvoiceNumber = document.getElementById("rpcModalInvoiceNumber");
    const modalClientName = document.getElementById("rpcModalClientName");
    const modalTotalInvoice = document.getElementById("rpcModalTotalInvoice");
    const modalStatusLabel = document.getElementById("rpcModalStatusLabel");
    const paymentValueInput = document.getElementById("rpcPaymentValueInput");
    const paymentDateInput = document.getElementById("rpcPaymentDateInput");
    const reteFtePercentInput = document.getElementById("rpcReteFtePercentInput");
    const reteIcaPercentInput = document.getElementById("rpcReteIcaPercentInput");
    const rteIvaPercentInput = document.getElementById("rpcRteIvaPercentInput");
    const reteFteValue = document.getElementById("rpcReteFteValue");
    const rteIvaValue = document.getElementById("rpcRteIvaValue");
    const reteIcaValue = document.getElementById("rpcReteIcaValue");
    const differenceValue = document.getElementById("rpcDifferenceValue");
    const differenceCard = document.getElementById("rpcDifferenceCard");
    const saveButton = document.getElementById("rpcModalSaveBtn");
    const suggestionSummary = document.getElementById("rpcSuggestionSummary");
    const suggestionDetail = document.getElementById("rpcSuggestionDetail");
    const useAverageButton = document.getElementById("rpcUseAverageBtn");
    const useLatestButton = document.getElementById("rpcUseLatestBtn");
    const solutionModal = document.getElementById("rpcSolutionModal");
    const solutionCloseButton = document.getElementById("rpcSolutionCloseBtn");
    const solutionCancelButton = document.getElementById("rpcSolutionCancelBtn");
    const solutionStatus = document.getElementById("rpcSolutionStatus");
    const solutionSubtitle = document.getElementById("rpcSolutionSubtitle");
    const solutionDetailBox = document.getElementById("rpcSolutionDetail");
    const solutionApplyButton = document.getElementById("rpcSolutionApplyBtn");

    const currencyFormatter = new Intl.NumberFormat("es-CO", {
        style: "currency",
        currency: "COP",
        maximumFractionDigits: 0
    });
    const numberFormatter = new Intl.NumberFormat("es-CO", {
        maximumFractionDigits: 4
    });
    const retentionTolerance = 5000;
    const reteFteOptions = [
        { value: 0, label: "Sin retefuente" },
        { value: 0.025, label: "Compras 2,5%" },
        { value: 0.035, label: "Otros ingresos 3,5%" },
        { value: 0.04, label: "Servicios declarantes 4%" },
        { value: 0.06, label: "Servicios no declarantes 6%" },
        { value: 0.10, label: "Honorarios 10%" },
        { value: 0.11, label: "Honorarios 11%" }
    ];
    const reteIcaOptions = [
        { value: 0, label: "Sin ReteICA" },
        { value: 4.14, label: "ICA 4,14 x mil" },
        { value: 6.9, label: "ICA 6,9 x mil" },
        { value: 7, label: "ICA 7 x mil" },
        { value: 8, label: "ICA 8 x mil" },
        { value: 9.66, label: "ICA 9,66 x mil" },
        { value: 11.04, label: "ICA 11,04 x mil" },
        { value: 13.8, label: "ICA 13,8 x mil" }
    ];
    const rteIvaOptions = [
        { value: 0, label: "Sin ReteIVA" },
        { value: 0.15, label: "ReteIVA 15%" }
    ];

    const state = {
        board: null,
        rows: [],
        filteredRows: [],
        activeTab: "all",
        currentInvoice: null,
        currentSolution: null
    };

    function setStatus(element, message, tone) {
        if (!element) {
            return;
        }

        element.textContent = message || "";
        element.className = "rpc-status";
        if (tone) {
            element.classList.add(`is-${tone}`);
        }
        element.classList.toggle("show", Boolean(message));
    }

    function escapeHtml(value) {
        return String(value ?? "")
            .replace(/&/g, "&amp;")
            .replace(/</g, "&lt;")
            .replace(/>/g, "&gt;")
            .replace(/"/g, "&quot;")
            .replace(/'/g, "&#039;");
    }

    function parseDecimal(value) {
        const raw = String(value ?? "").trim();
        if (!raw) {
            return 0;
        }

        const normalized = raw.includes(",")
            ? raw.replace(/\./g, "").replace(",", ".")
            : raw;
        const parsed = Number.parseFloat(normalized);
        return Number.isFinite(parsed) ? parsed : 0;
    }

    function roundCurrency(value) {
        return Math.round((Number(value || 0) + Number.EPSILON) * 100) / 100;
    }

    function roundPercent(value) {
        return Math.round((Number(value || 0) + Number.EPSILON) * 10000) / 10000;
    }

    function formatCurrency(value) {
        return currencyFormatter.format(Number(value || 0));
    }

    function formatRate(value) {
        return numberFormatter.format(Number(value || 0));
    }

    function formatInputNumber(value, precision = 2, emptyWhenZero = false) {
        const number = Number(value || 0);
        if (emptyWhenZero && Math.abs(number) < 0.000001) {
            return "";
        }

        const rounded = Number(number.toFixed(precision));
        return rounded.toString();
    }

    function updateSummary(board) {
        summaryRecords && (summaryRecords.textContent = Number(board?.recordsCount || 0).toLocaleString("es-CO"));
        summaryPaid && (summaryPaid.textContent = Number(board?.paidCount || 0).toLocaleString("es-CO"));
        summaryOverdue && (summaryOverdue.textContent = Number(board?.overdueCount || 0).toLocaleString("es-CO"));
        summaryPendingValue && (summaryPendingValue.textContent = formatCurrency(board?.totalPendingValue || 0));
    }

    function updateSummaryFromRows() {
        const rows = state.rows;
        const paidCount = rows.filter(row => row.paymentStatusKey === "paid").length;
        const overdueCount = rows.filter(row => row.paymentStatusKey === "overdue").length;
        const pendingValue = rows
            .filter(row => row.paymentStatusKey !== "paid" && row.paymentStatusKey !== "credited")
            .reduce((total, row) => total + Number(row.totalInvoice || 0), 0);

        updateSummary({
            recordsCount: rows.length,
            paidCount,
            overdueCount,
            totalPendingValue: pendingValue
        });
    }

    function isPendingRetentionRow(row) {
        return Number(row?.paymentValue || 0) > 0
            && Math.abs(Number(row?.differenceValue || 0)) > 5000;
    }

    function getPendingRetentionRows() {
        return state.rows.filter(isPendingRetentionRow);
    }

    function getBaseRowsForActiveTab() {
        return state.activeTab === "pending-retentions"
            ? getPendingRetentionRows()
            : state.rows;
    }

    function updatePendingRetentionCount() {
        pendingRetentionsCount && (pendingRetentionsCount.textContent = getPendingRetentionRows().length.toLocaleString("es-CO"));
    }

    function setActiveTab(tabKey) {
        state.activeTab = tabKey === "pending-retentions" ? "pending-retentions" : "all";
        tabButtons.forEach((button) => {
            const isActive = button.dataset.rpcTab === state.activeTab;
            button.classList.toggle("is-active", isActive);
            button.setAttribute("aria-selected", isActive ? "true" : "false");
        });

        app.classList.toggle("is-retentions-tab", state.activeTab === "pending-retentions");
        tableTitle && (tableTitle.textContent = state.activeTab === "pending-retentions" ? "RETENCIONES PENDIENTES" : "Facturas");
        applyFilters();
    }

    function updateClientOptions() {
        if (!clientFilterOptions) {
            return;
        }

        const clients = Array.from(new Set(
            state.rows
                .map(row => String(row.clientName || "").trim())
                .filter(Boolean)
        )).sort((left, right) => left.localeCompare(right, "es"));

        clientFilterOptions.innerHTML = "";
        clients.forEach((client) => {
            const option = document.createElement("option");
            option.value = client;
            clientFilterOptions.appendChild(option);
        });
    }

    function getFilters() {
        return {
            invoice: String(invoiceFilter?.value || "").trim().toLowerCase(),
            emission: String(emissionFilter?.value || "").trim(),
            client: String(clientFilter?.value || "").trim().toLowerCase(),
            totalMin: parseDecimal(totalMinFilter?.value),
            totalMax: parseDecimal(totalMaxFilter?.value),
            hasTotalMin: String(totalMinFilter?.value || "").trim() !== "",
            hasTotalMax: String(totalMaxFilter?.value || "").trim() !== "",
            status: String(statusFilter?.value || "").trim()
        };
    }

    function applyFilters() {
        const filters = getFilters();
        const baseRows = getBaseRowsForActiveTab();
        state.filteredRows = baseRows.filter((row) => {
            const invoiceMatches = !filters.invoice || String(row.invoiceNumber || "").toLowerCase().includes(filters.invoice);
            const emissionMatches = state.activeTab === "pending-retentions"
                ? true
                : !filters.emission || row.emissionDateValue === filters.emission;
            const clientMatches = !filters.client || String(row.clientName || "").toLowerCase().includes(filters.client);
            const total = Number(row.totalInvoice || 0);
            const totalMinMatches = !filters.hasTotalMin || total >= filters.totalMin;
            const totalMaxMatches = !filters.hasTotalMax || total <= filters.totalMax;
            const statusMatches = state.activeTab === "pending-retentions"
                ? true
                : !filters.status || row.paymentStatusKey === filters.status;

            return invoiceMatches
                && emissionMatches
                && clientMatches
                && totalMinMatches
                && totalMaxMatches
                && statusMatches;
        });

        updatePendingRetentionCount();
        renderRows();
    }

    function renderRows() {
        if (!rowsBody) {
            return;
        }

        const rows = state.filteredRows;
        resultsCount && (resultsCount.textContent = `${rows.length.toLocaleString("es-CO")} fila${rows.length === 1 ? "" : "s"}`);

        if (rows.length === 0) {
            const emptyMessage = state.activeTab === "pending-retentions"
                ? "No hay retenciones pendientes para mostrar."
                : "No hay facturas para mostrar.";
            rowsBody.innerHTML = `<tr><td colspan="8" class="rpc-table__empty">${escapeHtml(emptyMessage)}</td></tr>`;
            emptyState && (emptyState.hidden = false);
            emptyState && (emptyState.textContent = emptyMessage);
            return;
        }

        emptyState && (emptyState.hidden = true);
        rowsBody.innerHTML = rows.map(row => `
            <tr data-record-id="${escapeHtml(row.recordId)}" tabindex="0" aria-label="Abrir factura ${escapeHtml(row.invoiceNumber)}">
                <td title="${escapeHtml(row.invoiceNumber)}">${escapeHtml(row.invoiceNumber || "-")}</td>
                <td title="${escapeHtml(row.emissionDateDisplay)}">${escapeHtml(row.emissionDateDisplay || "-")}</td>
                <td title="${escapeHtml(row.clientName)}">${escapeHtml(row.clientName || "Cliente sin nombre")}</td>
                <td class="text-end" title="${escapeHtml(formatCurrency(row.totalInvoice))}">${escapeHtml(formatCurrency(row.totalInvoice))}</td>
                <td class="text-end rpc-pending-only" title="${escapeHtml(formatCurrency(row.paymentValue))}">${escapeHtml(formatCurrency(row.paymentValue))}</td>
                <td class="rpc-all-only"><span class="rpc-status-badge is-${escapeHtml(row.paymentStatusTone || "pending")}">${escapeHtml(row.paymentStatusLabel || "-")}</span></td>
                <td class="text-end rpc-pending-only" title="${escapeHtml(formatCurrency(row.differenceValue))}">${escapeHtml(formatCurrency(row.differenceValue))}</td>
                <td class="rpc-pending-only">${renderSuggestedSolutionCell(row)}</td>
            </tr>
        `).join("");

        rowsBody.querySelectorAll("tr[data-record-id]").forEach((rowElement) => {
            rowElement.addEventListener("click", () => openInvoice(rowElement.dataset.recordId || ""));
            rowElement.addEventListener("keydown", (event) => {
                if (event.key === "Enter" || event.key === " ") {
                    event.preventDefault();
                    openInvoice(rowElement.dataset.recordId || "");
                }
            });
        });
        rowsBody.querySelectorAll("[data-rpc-solution-record-id]").forEach((button) => {
            button.addEventListener("click", (event) => {
                event.preventDefault();
                event.stopPropagation();
                openSuggestedSolution(button.dataset.rpcSolutionRecordId || "");
            });
        });
    }

    function findInvoice(recordId) {
        return state.rows.find(row => String(row.recordId || "").toLowerCase() === String(recordId || "").toLowerCase()) || null;
    }

    function setSuggestionButtons(suggestion) {
        const hasSuggestion = Boolean(suggestion?.hasSuggestion);
        [useAverageButton, useLatestButton].forEach((button) => {
            if (button) {
                button.disabled = !hasSuggestion;
            }
        });

        if (!hasSuggestion) {
            useAverageButton?.removeAttribute("data-rete-fte");
            useAverageButton?.removeAttribute("data-rete-ica");
            useAverageButton?.removeAttribute("data-rte-iva");
            useLatestButton?.removeAttribute("data-rete-fte");
            useLatestButton?.removeAttribute("data-rete-ica");
            useLatestButton?.removeAttribute("data-rte-iva");
            return;
        }

        useAverageButton && (useAverageButton.dataset.reteFte = String(suggestion.averageReteFtePercent || 0));
        useAverageButton && (useAverageButton.dataset.reteIca = String(suggestion.averageReteIcaPercent || 0));
        useAverageButton && (useAverageButton.dataset.rteIva = String(suggestion.averageRteIvaPercent || 0));

        const latest = suggestion.latestScenario || {};
        useLatestButton && (useLatestButton.dataset.reteFte = String(latest.reteFtePercent || 0));
        useLatestButton && (useLatestButton.dataset.reteIca = String(latest.reteIcaPercent || 0));
        useLatestButton && (useLatestButton.dataset.rteIva = String(latest.rteIvaPercent || 0));
    }

    function renderSuggestion(invoice) {
        const suggestion = invoice?.suggestion || {};
        if (!suggestion?.hasSuggestion) {
            suggestionSummary && (suggestionSummary.textContent = "Sin escenarios anteriores para este cliente.");
            suggestionDetail && (suggestionDetail.textContent = "Registra este pago manualmente.");
            setSuggestionButtons(null);
            return;
        }

        const averageText = `Promedio: FTE ${formatRate(suggestion.averageReteFtePercent)}, ICA ${formatRate(suggestion.averageReteIcaPercent)} x mil, IVA ${formatRate(suggestion.averageRteIvaPercent)}`;
        const latest = suggestion.latestScenario || {};
        const latestText = latest.invoiceNumber
            ? `Ultimo: ${latest.invoiceNumber} (${latest.paymentDateDisplay || "sin fecha"}) FTE ${formatRate(latest.reteFtePercent)}, ICA ${formatRate(latest.reteIcaPercent)} x mil, IVA ${formatRate(latest.rteIvaPercent)}`
            : "";

        suggestionSummary && (suggestionSummary.textContent = `${suggestion.sourceCount || 0} escenario${suggestion.sourceCount === 1 ? "" : "s"} del cliente`);
        suggestionDetail && (suggestionDetail.textContent = latestText ? `${averageText}. ${latestText}.` : `${averageText}.`);
        setSuggestionButtons(suggestion);
    }

    function updateModalSummary(invoice) {
        modalTitle && (modalTitle.textContent = `Registrar pago ${invoice?.invoiceNumber || ""}`.trim());
        modalSubtitle && (modalSubtitle.textContent = invoice?.clientName || "Factura seleccionada");
        modalInvoiceNumber && (modalInvoiceNumber.textContent = invoice?.invoiceNumber || "-");
        modalClientName && (modalClientName.textContent = invoice?.clientName || "-");
        modalTotalInvoice && (modalTotalInvoice.textContent = formatCurrency(invoice?.totalInvoice || 0));
        modalStatusLabel && (modalStatusLabel.textContent = invoice?.paymentStatusLabel || "-");
    }

    function openInvoice(recordId) {
        const invoice = findInvoice(recordId);
        if (!invoice || !modal) {
            return;
        }

        state.currentInvoice = invoice;
        setStatus(modalStatus, "", "");
        recordIdInput && (recordIdInput.value = invoice.recordId || "");
        updateModalSummary(invoice);
        renderSuggestion(invoice);

        paymentValueInput && (paymentValueInput.value = formatInputNumber(invoice.paymentValue || 0, 2, true));
        paymentDateInput && (paymentDateInput.value = invoice.paymentDateValue || "");
        reteFtePercentInput && (reteFtePercentInput.value = formatInputNumber(invoice.reteFtePercent || 0, 4, true));
        reteIcaPercentInput && (reteIcaPercentInput.value = formatInputNumber(invoice.reteIcaPercent || 0, 4, true));
        rteIvaPercentInput && (rteIvaPercentInput.value = formatInputNumber(invoice.rteIvaPercent || 0, 4, true));

        updateCalculation();
        modal.hidden = false;
        document.body.classList.add("rpc-modal-open");
        paymentValueInput?.focus();
    }

    function closeModal() {
        if (modal) {
            modal.hidden = true;
        }
        state.currentInvoice = null;
        document.body.classList.remove("rpc-modal-open");
    }

    function getBaseBeforeVat(total, vatValue) {
        const totalNumber = Number(total || 0);
        const vatNumber = Number(vatValue || 0);
        if (totalNumber <= 0) {
            return 0;
        }

        return roundCurrency(Math.max(totalNumber - Math.max(vatNumber, 0), 0));
    }

    function getVatFromIncludedTotal(total) {
        const totalNumber = Number(total || 0);
        if (totalNumber <= 0) {
            return 0;
        }

        return roundCurrency(totalNumber - (totalNumber / 1.19));
    }

    function normalizeRate(value) {
        return roundPercent(Number(value || 0));
    }

    function calculateRetentionForRates(invoice, rates) {
        const total = Number(invoice?.totalInvoice || 0);
        const vatValue = Number(invoice?.vatValue || 0);
        const payment = roundCurrency(Number(invoice?.paymentValue || 0));
        const baseBeforeVat = getBaseBeforeVat(total, vatValue);
        const vatFromIncludedTotal = getVatFromIncludedTotal(total);
        const reteFte = roundCurrency(baseBeforeVat * normalizeRate(rates?.reteFte));
        const reteIca = roundCurrency((baseBeforeVat * normalizeRate(rates?.reteIca)) / 1000);
        const rteIva = roundCurrency(vatFromIncludedTotal * normalizeRate(rates?.rteIva));
        const difference = roundCurrency(total - payment - reteFte - reteIca - rteIva);

        return {
            total,
            payment,
            baseBeforeVat,
            vatFromIncludedTotal,
            reteFte,
            reteIca,
            rteIva,
            difference
        };
    }

    function getCurrentRates(invoice) {
        return {
            reteFte: normalizeRate(invoice?.reteFtePercent),
            reteIca: normalizeRate(invoice?.reteIcaPercent),
            rteIva: normalizeRate(invoice?.rteIvaPercent)
        };
    }

    function getOptionLabel(options, value) {
        const normalized = normalizeRate(value);
        const match = options.find(option => Math.abs(normalizeRate(option.value) - normalized) < 0.0001);
        return match?.label || formatRate(normalized);
    }

    function getRteIvaOptionsForInvoice(invoice) {
        const hasVat = Number(invoice?.vatValue || 0) > 0 || normalizeRate(invoice?.rteIvaPercent) > 0;
        return hasVat ? rteIvaOptions : rteIvaOptions.filter(option => option.value === 0);
    }

    function countRateChanges(currentRates, candidateRates) {
        return [
            Math.abs(currentRates.reteFte - candidateRates.reteFte) > 0.0001,
            Math.abs(currentRates.reteIca - candidateRates.reteIca) > 0.0001,
            Math.abs(currentRates.rteIva - candidateRates.rteIva) > 0.0001
        ].filter(Boolean).length;
    }

    function getRateDistance(currentRates, candidateRates) {
        return Math.abs(currentRates.reteFte - candidateRates.reteFte)
            + (Math.abs(currentRates.reteIca - candidateRates.reteIca) / 1000)
            + Math.abs(currentRates.rteIva - candidateRates.rteIva);
    }

    function compareSolutions(left, right) {
        if (!right) {
            return -1;
        }

        const leftAbs = Math.abs(left.calculation.difference);
        const rightAbs = Math.abs(right.calculation.difference);
        if (Math.abs(leftAbs - rightAbs) > 0.01) {
            return leftAbs - rightAbs;
        }

        if (left.changeCount !== right.changeCount) {
            return left.changeCount - right.changeCount;
        }

        return left.rateDistance - right.rateDistance;
    }

    function getSuggestedRetentionSolution(invoice) {
        const currentRates = getCurrentRates(invoice);
        const currentCalculation = calculateRetentionForRates(invoice, currentRates);
        const rteIvaCandidates = getRteIvaOptionsForInvoice(invoice);
        let best = null;

        reteFteOptions.forEach((reteFteOption) => {
            reteIcaOptions.forEach((reteIcaOption) => {
                rteIvaCandidates.forEach((rteIvaOption) => {
                    const rates = {
                        reteFte: normalizeRate(reteFteOption.value),
                        reteIca: normalizeRate(reteIcaOption.value),
                        rteIva: normalizeRate(rteIvaOption.value)
                    };
                    const calculation = calculateRetentionForRates(invoice, rates);
                    const candidate = {
                        rates,
                        labels: {
                            reteFte: reteFteOption.label,
                            reteIca: reteIcaOption.label,
                            rteIva: rteIvaOption.label
                        },
                        calculation,
                        changeCount: countRateChanges(currentRates, rates),
                        rateDistance: getRateDistance(currentRates, rates)
                    };

                    if (compareSolutions(candidate, best) < 0) {
                        best = candidate;
                    }
                });
            });
        });

        const currentAbs = Math.abs(currentCalculation.difference);
        const bestAbs = Math.abs(best?.calculation?.difference || 0);
        const improves = bestAbs + 0.01 < currentAbs;
        const balanced = bestAbs <= retentionTolerance;

        return {
            currentRates,
            currentCalculation,
            best,
            improves,
            balanced,
            canApply: Boolean(best && improves)
        };
    }

    function renderSuggestedSolutionCell(invoice) {
        const suggestion = getSuggestedRetentionSolution(invoice);
        const best = suggestion.best;
        if (!best) {
            return '<span class="text-muted">Sin sugerencia</span>';
        }

        const tone = suggestion.balanced
            ? "is-balanced"
            : suggestion.improves ? "" : "is-limited";
        const title = suggestion.improves
            ? `Aplicar sugerencia para ${invoice.invoiceNumber || "factura"}`
            : "Ver combinacion mas cercana";
        const label = suggestion.balanced
            ? "Cuadra en rango"
            : suggestion.improves ? "Mejor opcion" : "Sin mejora";
        const detail = `Dif. ${formatCurrency(best.calculation.difference)}`;

        return `
            <button type="button"
                    class="rpc-solution-btn ${tone}"
                    data-rpc-solution-record-id="${escapeHtml(invoice.recordId)}"
                    title="${escapeHtml(title)}">
                <span>${escapeHtml(label)}</span>
                <small>${escapeHtml(detail)}</small>
            </button>
        `;
    }

    function getCalculation() {
        const total = Number(state.currentInvoice?.totalInvoice || 0);
        const vatValue = Number(state.currentInvoice?.vatValue || 0);
        const baseBeforeVat = getBaseBeforeVat(total, vatValue);
        const vatFromIncludedTotal = getVatFromIncludedTotal(total);
        const payment = roundCurrency(parseDecimal(paymentValueInput?.value));
        const reteFtePercent = roundPercent(parseDecimal(reteFtePercentInput?.value));
        const reteIcaPercent = roundPercent(parseDecimal(reteIcaPercentInput?.value));
        const rteIvaPercent = roundPercent(parseDecimal(rteIvaPercentInput?.value));
        const reteFte = roundCurrency(baseBeforeVat * reteFtePercent);
        const reteIca = roundCurrency((baseBeforeVat * reteIcaPercent) / 1000);
        const rteIva = roundCurrency(vatFromIncludedTotal * rteIvaPercent);
        const difference = roundCurrency(total - payment - reteFte - reteIca - rteIva);

        return {
            payment,
            reteFtePercent,
            reteIcaPercent,
            rteIvaPercent,
            reteFte,
            reteIca,
            rteIva,
            difference
        };
    }

    function updateCalculation() {
        const calculation = getCalculation();
        reteFteValue && (reteFteValue.textContent = formatCurrency(calculation.reteFte));
        rteIvaValue && (rteIvaValue.textContent = formatCurrency(calculation.rteIva));
        reteIcaValue && (reteIcaValue.textContent = formatCurrency(calculation.reteIca));
        differenceValue && (differenceValue.textContent = formatCurrency(calculation.difference));

        if (differenceCard) {
            const balanced = Math.abs(calculation.difference) <= 2000;
            differenceCard.classList.toggle("is-balanced", balanced);
            differenceCard.classList.toggle("is-unbalanced", !balanced);
        }
    }

    function applySuggestionFromButton(button) {
        if (!button || button.disabled) {
            return;
        }

        reteFtePercentInput && (reteFtePercentInput.value = formatInputNumber(parseDecimal(button.dataset.reteFte), 4, false));
        reteIcaPercentInput && (reteIcaPercentInput.value = formatInputNumber(parseDecimal(button.dataset.reteIca), 4, false));
        rteIvaPercentInput && (rteIvaPercentInput.value = formatInputNumber(parseDecimal(button.dataset.rteIva), 4, false));
        updateCalculation();
        reteFtePercentInput?.focus();
    }

    function validateSavePayload(payload) {
        if (!payload.recordId) {
            return "No se encontro la factura seleccionada.";
        }
        if (payload.paymentValue <= 0) {
            return "El valor pago debe ser mayor a cero.";
        }
        if (!payload.paymentDateValue) {
            return "Indica la fecha de pago.";
        }

        if (payload.reteFtePercent < 0 || payload.reteFtePercent > 1) {
            return "Rete fte debe estar entre 0 y 1. Usa 0,04 para 4%.";
        }
        if (payload.reteIcaPercent < 0 || payload.reteIcaPercent > 1000) {
            return "Rete ica debe estar entre 0 y 1000. Usa 11,04 para 11,04 por mil.";
        }
        if (payload.rteIvaPercent < 0 || payload.rteIvaPercent > 1) {
            return "Rte iva debe estar entre 0 y 1. Usa 0,15 para 15%.";
        }

        return "";
    }

    function updateInvoiceAfterSave(responsePayload) {
        const updatedInvoice = responsePayload?.invoice;
        if (!updatedInvoice?.recordId) {
            return null;
        }

        const index = state.rows.findIndex(row => String(row.recordId).toLowerCase() === String(updatedInvoice.recordId).toLowerCase());
        if (index >= 0) {
            state.rows[index] = updatedInvoice;
        }
        state.currentInvoice = updatedInvoice;
        updateModalSummary(updatedInvoice);
        renderSuggestion(updatedInvoice);
        updateSummaryFromRows();
        applyFilters();
        return updatedInvoice;
    }

    async function savePayment(event) {
        event?.preventDefault();
        if (!saveUrl) {
            setStatus(modalStatus, "No se encontro la ruta para guardar.", "error");
            return;
        }

        const calculation = getCalculation();
        const payload = {
            recordId: recordIdInput?.value || "",
            paymentValue: calculation.payment,
            paymentDateValue: paymentDateInput?.value || "",
            reteFtePercent: calculation.reteFtePercent,
            reteIcaPercent: calculation.reteIcaPercent,
            rteIvaPercent: calculation.rteIvaPercent
        };
        const validationMessage = validateSavePayload(payload);
        if (validationMessage) {
            setStatus(modalStatus, validationMessage, "error");
            return;
        }

        saveButton && (saveButton.disabled = true);
        setStatus(modalStatus, "Guardando pago en Dataverse...", "info");

        try {
            const response = await fetch(saveUrl, {
                method: "POST",
                headers: {
                    "Content-Type": "application/json"
                },
                body: JSON.stringify(payload)
            });
            const responsePayload = await response.json().catch(() => ({}));
            if (!response.ok) {
                throw new Error(responsePayload.detail || responsePayload.message || "No fue posible registrar el pago.");
            }

            updateInvoiceAfterSave(responsePayload);

            setStatus(modalStatus, responsePayload.message || "Pago registrado correctamente.", "success");
            setStatus(statusBanner, responsePayload.message || "Pago registrado correctamente.", "success");
        } catch (error) {
            setStatus(modalStatus, error instanceof Error ? error.message : "Ocurrio un error inesperado.", "error");
        } finally {
            saveButton && (saveButton.disabled = false);
        }
    }

    function renderSolutionLine(label, value) {
        return `
            <div class="rpc-solution-line">
                <span>${escapeHtml(label)}</span>
                <strong>${escapeHtml(value)}</strong>
            </div>
        `;
    }

    function renderSolutionRateLine(label, value, optionLabel) {
        const suffix = optionLabel ? ` - ${optionLabel}` : "";
        return renderSolutionLine(label, `${formatRate(value)}${suffix}`);
    }

    function openSuggestedSolution(recordId) {
        const invoice = findInvoice(recordId);
        if (!invoice || !solutionModal || !solutionDetailBox) {
            return;
        }

        const suggestion = getSuggestedRetentionSolution(invoice);
        const best = suggestion.best;
        if (!best) {
            return;
        }

        state.currentSolution = { invoice, suggestion };
        setStatus(solutionStatus, "", "");
        solutionSubtitle && (solutionSubtitle.textContent = `${invoice.invoiceNumber || "-"} - ${invoice.clientName || "Cliente sin nombre"}`);

        const noteTone = suggestion.balanced
            ? "is-balanced"
            : suggestion.improves ? "" : "is-limited";
        const note = suggestion.balanced
            ? "La combinacion sugerida deja la diferencia dentro del rango objetivo."
            : suggestion.improves
                ? "No queda en cero, pero es la combinacion vigente mas cercana encontrada."
                : "El catalogo vigente no mejora la combinacion actual; se muestra la opcion mas cercana.";

        solutionDetailBox.innerHTML = `
            <div class="rpc-solution-note ${noteTone}">
                ${escapeHtml(note)}
            </div>
            <div class="rpc-solution-grid">
                <article class="rpc-solution-card">
                    <h3>Actual</h3>
                    <div class="rpc-solution-lines">
                        ${renderSolutionRateLine("ReteFuente", suggestion.currentRates.reteFte, getOptionLabel(reteFteOptions, suggestion.currentRates.reteFte))}
                        ${renderSolutionRateLine("ReteICA", suggestion.currentRates.reteIca, getOptionLabel(reteIcaOptions, suggestion.currentRates.reteIca))}
                        ${renderSolutionRateLine("ReteIVA", suggestion.currentRates.rteIva, getOptionLabel(rteIvaOptions, suggestion.currentRates.rteIva))}
                        ${renderSolutionLine("Valor pago", formatCurrency(suggestion.currentCalculation.payment))}
                        ${renderSolutionLine("Diferencia", formatCurrency(suggestion.currentCalculation.difference))}
                    </div>
                </article>
                <article class="rpc-solution-card">
                    <h3>Sugerida</h3>
                    <div class="rpc-solution-lines">
                        ${renderSolutionRateLine("ReteFuente", best.rates.reteFte, best.labels.reteFte)}
                        ${renderSolutionRateLine("ReteICA", best.rates.reteIca, best.labels.reteIca)}
                        ${renderSolutionRateLine("ReteIVA", best.rates.rteIva, best.labels.rteIva)}
                        ${renderSolutionLine("RTE FTE valor", formatCurrency(best.calculation.reteFte))}
                        ${renderSolutionLine("ReteICA valor", formatCurrency(best.calculation.reteIca))}
                        ${renderSolutionLine("RTE IVA valor", formatCurrency(best.calculation.rteIva))}
                        ${renderSolutionLine("Diferencia", formatCurrency(best.calculation.difference))}
                    </div>
                </article>
            </div>
        `;

        if (solutionApplyButton) {
            solutionApplyButton.disabled = !suggestion.canApply;
        }

        solutionModal.hidden = false;
        document.body.classList.add("rpc-modal-open");
    }

    function closeSolutionModal() {
        if (solutionModal) {
            solutionModal.hidden = true;
        }
        state.currentSolution = null;
        if (!modal || modal.hidden) {
            document.body.classList.remove("rpc-modal-open");
        }
    }

    function applyRatesToPaymentModal(invoice, rates) {
        openInvoice(invoice.recordId);
        reteFtePercentInput && (reteFtePercentInput.value = formatInputNumber(rates.reteFte, 4, false));
        reteIcaPercentInput && (reteIcaPercentInput.value = formatInputNumber(rates.reteIca, 4, false));
        rteIvaPercentInput && (rteIvaPercentInput.value = formatInputNumber(rates.rteIva, 4, false));
        updateCalculation();
        setStatus(modalStatus, "Sugerencia aplicada. Revisa y guarda la factura.", "info");
    }

    async function applySuggestedSolution() {
        const current = state.currentSolution;
        const invoice = current?.invoice;
        const best = current?.suggestion?.best;
        if (!invoice || !best || !saveUrl) {
            return;
        }

        if (!current.suggestion.canApply) {
            setStatus(solutionStatus, "La sugerencia no mejora la combinacion actual.", "info");
            return;
        }

        if (!invoice.paymentDateValue) {
            closeSolutionModal();
            applyRatesToPaymentModal(invoice, best.rates);
            return;
        }

        const payload = {
            recordId: invoice.recordId || "",
            paymentValue: roundCurrency(Number(invoice.paymentValue || 0)),
            paymentDateValue: invoice.paymentDateValue || "",
            reteFtePercent: best.rates.reteFte,
            reteIcaPercent: best.rates.reteIca,
            rteIvaPercent: best.rates.rteIva
        };
        const validationMessage = validateSavePayload(payload);
        if (validationMessage) {
            setStatus(solutionStatus, validationMessage, "error");
            return;
        }

        solutionApplyButton && (solutionApplyButton.disabled = true);
        setStatus(solutionStatus, "Aplicando sugerencia en Dataverse...", "info");

        try {
            const response = await fetch(saveUrl, {
                method: "POST",
                headers: {
                    "Content-Type": "application/json"
                },
                body: JSON.stringify(payload)
            });
            const responsePayload = await response.json().catch(() => ({}));
            if (!response.ok) {
                throw new Error(responsePayload.detail || responsePayload.message || "No fue posible aplicar la sugerencia.");
            }

            updateInvoiceAfterSave(responsePayload);
            closeSolutionModal();
            setStatus(statusBanner, responsePayload.message || "Sugerencia aplicada correctamente.", "success");
        } catch (error) {
            setStatus(solutionStatus, error instanceof Error ? error.message : "Ocurrio un error inesperado.", "error");
            solutionApplyButton && (solutionApplyButton.disabled = false);
        }
    }

    async function loadData(showSuccess = false) {
        if (!loadUrl) {
            setStatus(statusBanner, "No se encontro la ruta para cargar facturas.", "error");
            return;
        }

        setStatus(statusBanner, "Cargando facturas desde Dataverse...", "info");
        rowsBody && (rowsBody.innerHTML = '<tr><td colspan="8" class="rpc-table__empty">Cargando facturas...</td></tr>');

        try {
            const response = await fetch(loadUrl, {
                headers: {
                    "Accept": "application/json"
                }
            });
            const payload = await response.json().catch(() => ({}));
            if (!response.ok) {
                throw new Error(payload.detail || payload.message || "No fue posible cargar las facturas.");
            }

            state.board = payload;
            state.rows = Array.isArray(payload.invoices) ? payload.invoices : [];
            updateSummary(payload);
            updateClientOptions();
            applyFilters();
            setStatus(statusBanner, showSuccess ? "Facturas actualizadas." : "", showSuccess ? "success" : "");
        } catch (error) {
            state.rows = [];
            state.filteredRows = [];
            updateSummary({});
            renderRows();
            setStatus(statusBanner, error instanceof Error ? error.message : "Ocurrio un error inesperado cargando facturas.", "error");
        }
    }

    function clearFilters() {
        [invoiceFilter, emissionFilter, clientFilter, totalMinFilter, totalMaxFilter].forEach((input) => {
            if (input) {
                input.value = "";
            }
        });
        statusFilter && (statusFilter.value = "");
        applyFilters();
    }

    [invoiceFilter, emissionFilter, clientFilter, totalMinFilter, totalMaxFilter, statusFilter].forEach((input) => {
        input?.addEventListener("input", applyFilters);
        input?.addEventListener("change", applyFilters);
    });

    [paymentValueInput, reteFtePercentInput, reteIcaPercentInput, rteIvaPercentInput].forEach((input) => {
        input?.addEventListener("input", updateCalculation);
    });

    refreshButton?.addEventListener("click", () => loadData(true));
    clearFiltersButton?.addEventListener("click", clearFilters);
    tabButtons.forEach((button) => {
        button.addEventListener("click", () => setActiveTab(button.dataset.rpcTab || "all"));
    });
    modalCloseButton?.addEventListener("click", closeModal);
    modalCancelButton?.addEventListener("click", closeModal);
    modal?.querySelector("[data-rpc-close]")?.addEventListener("click", closeModal);
    solutionCloseButton?.addEventListener("click", closeSolutionModal);
    solutionCancelButton?.addEventListener("click", closeSolutionModal);
    solutionModal?.querySelector("[data-rpc-solution-close]")?.addEventListener("click", closeSolutionModal);
    solutionApplyButton?.addEventListener("click", applySuggestedSolution);
    paymentForm?.addEventListener("submit", savePayment);
    useAverageButton?.addEventListener("click", () => applySuggestionFromButton(useAverageButton));
    useLatestButton?.addEventListener("click", () => applySuggestionFromButton(useLatestButton));

    document.addEventListener("keydown", (event) => {
        if (event.key !== "Escape") {
            return;
        }

        if (solutionModal && !solutionModal.hidden) {
            closeSolutionModal();
        } else if (modal && !modal.hidden) {
            closeModal();
        }
    });

    loadData();
})();
