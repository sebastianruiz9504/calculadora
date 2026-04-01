(() => {
    const app = document.getElementById("puntajesApp");
    if (!app) {
        return;
    }

    const optionsNode = document.getElementById("puntajes-options-data");
    const options = optionsNode?.textContent ? JSON.parse(optionsNode.textContent) : {};
    const groupsContainer = document.getElementById("scoresGroupsContainer");
    const statusBanner = document.getElementById("scoresStatusBanner");
    const refreshButton = document.getElementById("refreshScoresBtn");
    const filterButtons = Array.from(document.querySelectorAll(".scores-filter-btn"));
    const summaryClients = document.getElementById("summaryClients");
    const summaryRecords = document.getElementById("summaryRecords");
    const summaryProducts = document.getElementById("summaryProducts");
    const summaryScore = document.getElementById("summaryScore");
    const summaryCommission = document.getElementById("summaryCommission");
    const summaryAnnualValue = document.getElementById("summaryAnnualValue");
    const closeMonthButton = document.getElementById("closeMonthBtn");
    const closeMonthSummaryText = document.getElementById("closeMonthSummaryText");
    const closeMonthLogEmpty = document.getElementById("closeMonthLogEmpty");
    const closeMonthLogList = document.getElementById("closeMonthLogList");

    const verifyModalElement = document.getElementById("verifyScoreModal");
    const verifyModalTitle = document.getElementById("verifyScoreModalTitle");
    const verifyModalSubtitle = document.getElementById("verifyScoreModalSubtitle");
    const verifyModalStatus = document.getElementById("verifyScoreModalStatus");
    const verifyModalLoading = document.getElementById("verifyScoreModalLoading");
    const verifyScoreForm = document.getElementById("verifyScoreForm");
    const verifyMetaCards = document.getElementById("verifyMetaCards");
    const verifyLinesBody = document.getElementById("verifyLinesBody");
    const firstContractSelect = document.getElementById("firstContractSelect");
    const lineOptionSelect = document.getElementById("lineOptionSelect");
    const verticalOptionSelect = document.getElementById("verticalOptionSelect");
    const billingDayInput = document.getElementById("billingDayInput");
    const renewalDateInput = document.getElementById("renewalDateInput");
    const alignmentDateInput = document.getElementById("alignmentDateInput");
    const hasVatSelect = document.getElementById("hasVatSelect");
    const autoBillSelect = document.getElementById("autoBillSelect");
    const productLineSelect = document.getElementById("productLineSelect");
    const contractTypeSelect = document.getElementById("contractTypeSelect");
    const addVerifyLineBtn = document.getElementById("addVerifyLineBtn");
    const recalculateVerifyScoreBtn = document.getElementById("recalculateVerifyScoreBtn");
    const submitVerifyScoreBtn = document.getElementById("submitVerifyScoreBtn");
    const verifyResultPoints = document.getElementById("verifyResultPoints");
    const verifyResultCommission = document.getElementById("verifyResultCommission");
    const verifyResultProration = document.getElementById("verifyResultProration");
    const verifyResultMonthly = document.getElementById("verifyResultMonthly");
    const verifyResultTotal = document.getElementById("verifyResultTotal");
    const verifyModal = verifyModalElement && window.bootstrap ? new bootstrap.Modal(verifyModalElement) : null;

    const optionMaps = {
        firstContract: buildOptionMap(options.firstContractOptions),
        line: buildOptionMap(options.lineOptions),
        vertical: buildOptionMap(options.verticalOptions),
        hasVat: buildOptionMap(options.hasVatOptions),
        autoBill: buildOptionMap(options.autoBillOptions),
        productLine: buildOptionMap(options.productLineOptions),
        contractType: buildOptionMap(options.contractTypeOptions)
    };

    const numberFormatter = new Intl.NumberFormat("es-CO", {
        minimumFractionDigits: 0,
        maximumFractionDigits: 2
    });

    const scoreFormatter = new Intl.NumberFormat("es-CO", {
        minimumFractionDigits: 2,
        maximumFractionDigits: 2
    });

    const state = {
        filter: app.dataset.initialFilter || "this-month",
        board: null,
        recordMap: new Map(),
        expandedGroups: new Set(),
        expandedRecords: new Set(),
        activeRecordId: "",
        activeDraft: null,
        isLoading: false,
        isSaving: false,
        isRecalculating: false,
        isClosingMonth: false,
        lastCloseResult: null
    };

    function buildOptionMap(items) {
        const map = new Map();
        (Array.isArray(items) ? items : []).forEach(item => {
            map.set(String(item.value ?? item.Value), item.label ?? item.Label ?? "");
        });
        return map;
    }

    function escapeHtml(value) {
        return (value ?? "").toString()
            .replace(/&/g, "&amp;")
            .replace(/</g, "&lt;")
            .replace(/>/g, "&gt;")
            .replace(/"/g, "&quot;")
            .replace(/'/g, "&#39;");
    }

    function roundMoney(value) {
        return Number(Number(value || 0).toFixed(2));
    }

    function formatNumber(value) {
        return numberFormatter.format(Number(value || 0));
    }

    function formatScoreValue(value) {
        return scoreFormatter.format(Number(value || 0));
    }

    function formatPercent(value) {
        return `${formatNumber(value)}%`;
    }

    function optionLabel(map, value) {
        if (value === null || value === undefined || value === "") {
            return "";
        }
        return map.get(String(value)) || String(value);
    }

    function setStatus(type, message) {
        if (!statusBanner) {
            return;
        }

        if (!message) {
            statusBanner.className = "scores-status";
            statusBanner.textContent = "";
            return;
        }

        statusBanner.className = `scores-status show ${type}`;
        statusBanner.textContent = message;
    }

    function setModalStatus(type, message) {
        if (!verifyModalStatus) {
            return;
        }

        if (!message) {
            verifyModalStatus.className = "scores-modal__status";
            verifyModalStatus.textContent = "";
            return;
        }

        verifyModalStatus.className = `scores-modal__status show ${type}`;
        verifyModalStatus.textContent = message;
    }

    function formatErrorMessage(error, fallbackMessage) {
        if (!error) {
            return fallbackMessage;
        }

        const parts = [];
        const message = typeof error.message === "string" ? error.message.trim() : "";
        const detail = typeof error.detail === "string" ? error.detail.trim() : "";
        const traceId = typeof error.traceId === "string" ? error.traceId.trim() : "";

        if (message) {
            parts.push(message);
        }

        if (detail && detail !== message) {
            parts.push(`Detalle: ${detail}`);
        }

        if (traceId) {
            parts.push(`TraceId: ${traceId}`);
        }

        return parts.length ? parts.join(" | ") : fallbackMessage;
    }

    async function extractErrorPayload(response, contentType) {
        if (contentType.includes("application/json")) {
            try {
                const payload = await response.json();
                if (typeof payload === "string") {
                    return { message: payload.trim() };
                }

                if (payload && typeof payload === "object") {
                    const validationDetails = payload.errors && typeof payload.errors === "object"
                        ? Object.entries(payload.errors)
                            .flatMap(([field, messages]) => {
                                const values = Array.isArray(messages) ? messages : [messages];
                                return values
                                    .filter(Boolean)
                                    .map(message => field ? `${field}: ${message}` : `${message}`);
                            })
                        : [];

                    const detailParts = [];
                    if (typeof payload.detail === "string" && payload.detail.trim()) {
                        detailParts.push(payload.detail.trim());
                    }
                    if (validationDetails.length) {
                        detailParts.push(validationDetails.join(" | "));
                    }

                    return {
                        message: (payload.message || payload.title || "").toString().trim(),
                        detail: detailParts.join(" | "),
                        traceId: (payload.traceId || "").toString().trim()
                    };
                }
            } catch {
                // Falls back to plain text parsing below.
            }
        }

        const message = (await response.text()).trim();
        return { message };
    }

    function toggleModalLoading(show) {
        verifyModalLoading?.classList.toggle("show", !!show);
        if (verifyScoreForm) {
            verifyScoreForm.hidden = !!show;
        }
    }

    function setLoading(loading) {
        state.isLoading = loading;
        refreshButton && (refreshButton.disabled = loading || state.isSaving || state.isClosingMonth);
        filterButtons.forEach(button => {
            button.disabled = loading || state.isSaving || state.isClosingMonth;
        });
        renderCloseMonthPanel();
    }

    function setSaving(saving) {
        state.isSaving = saving;
        submitVerifyScoreBtn && (submitVerifyScoreBtn.disabled = saving);
        refreshButton && (refreshButton.disabled = saving || state.isLoading || state.isClosingMonth);
        renderCloseMonthPanel();
    }

    function setRecalculating(recalculating) {
        state.isRecalculating = recalculating;
        recalculateVerifyScoreBtn && (recalculateVerifyScoreBtn.disabled = recalculating || state.isSaving);
        submitVerifyScoreBtn && (submitVerifyScoreBtn.disabled = state.isSaving || recalculating);
    }

    function setClosingMonth(closing) {
        state.isClosingMonth = closing;
        closeMonthButton && (closeMonthButton.disabled = closing);
        refreshButton && (refreshButton.disabled = closing || state.isLoading || state.isSaving);
        renderCloseMonthPanel();
    }

    function populateSelect(selectElement, items, placeholder) {
        if (!selectElement) {
            return;
        }

        const optionsMarkup = [`<option value="">${escapeHtml(placeholder)}</option>`]
            .concat((Array.isArray(items) ? items : []).map(item => (
                `<option value="${escapeHtml(item.value ?? item.Value)}">${escapeHtml(item.label ?? item.Label)}</option>`
            )))
            .join("");

        selectElement.innerHTML = optionsMarkup;
    }

    function getGroupKey(group) {
        return group.clientId ? `id:${group.clientId}` : `name:${group.clientName}`;
    }

    function rebuildIndexes(board) {
        state.recordMap = new Map();

        if (!board || !Array.isArray(board.groups)) {
            return;
        }

        board.groups.forEach(group => {
            (group.records || []).forEach(record => {
                state.recordMap.set(record.recordId, record);
            });
        });
    }

    function setFilterButtonState() {
        filterButtons.forEach(button => {
            button.classList.toggle("active", button.dataset.filter === state.filter);
        });
    }

    function updateSummary(board) {
        const safeBoard = board || {};
        summaryClients && (summaryClients.textContent = formatNumber(safeBoard.clientsCount));
        summaryRecords && (summaryRecords.textContent = formatNumber(safeBoard.recordsCount));
        summaryProducts && (summaryProducts.textContent = formatNumber(safeBoard.productLinesCount));
        summaryScore && (summaryScore.textContent = formatScoreValue(safeBoard.totalScore));
        summaryCommission && (summaryCommission.textContent = formatNumber(safeBoard.totalCommission));
        summaryAnnualValue && (summaryAnnualValue.textContent = formatNumber(safeBoard.totalValue ?? safeBoard.totalAnnualValue));
    }

    function renderMetaChip(label, value) {
        if (!value) {
            return "";
        }

        return `
            <div class="scores-detail__chip">
                <span class="scores-detail__label">${escapeHtml(label)}</span>
                <span>${escapeHtml(value)}</span>
            </div>
        `;
    }

    function buildOfferUrl(recordId) {
        return `${app.dataset.offerUrl}?recordId=${encodeURIComponent(recordId)}`;
    }

    function renderOfferCell(record) {
        if (!record.hasOffer) {
            return '<span class="scores-empty-cell">-</span>';
        }

        const label = record.offerFileName || record.offer || "Descargar oferta";
        return `
            <a class="scores-offer-link"
               href="${escapeHtml(buildOfferUrl(record.recordId))}"
               title="${escapeHtml(label)}"
               aria-label="Descargar oferta">
                <svg viewBox="0 0 24 24" aria-hidden="true" focusable="false">
                    <path d="M12 3a1 1 0 0 1 1 1v8.59l2.3-2.29a1 1 0 1 1 1.4 1.41l-4 3.99a1 1 0 0 1-1.4 0l-4-3.99a1 1 0 0 1 1.4-1.41L11 12.59V4a1 1 0 0 1 1-1Zm-7 14a1 1 0 0 1 1 1v1h12v-1a1 1 0 1 1 2 0v2a1 1 0 0 1-1 1H5a1 1 0 0 1-1-1v-2a1 1 0 0 1 1-1Z"/>
                </svg>
            </a>
        `;
    }

    function renderProductLines(record) {
        const lines = Array.isArray(record.productLines) ? record.productLines : [];
        if (lines.length === 0) {
            return '<div class="scores-product-empty">No se detectaron lineas de productos en el registro.</div>';
        }

        return `
            <table class="scores-product-table">
                <thead>
                    <tr>
                        <th>Producto</th>
                        <th class="text-end">Cantidad</th>
                        <th class="text-end">Costo UND</th>
                        <th class="text-end">Margen %</th>
                        <th class="text-end">Duracion</th>
                        <th class="text-end">Venta UND</th>
                        <th class="text-end">Venta mensual</th>
                        <th class="text-end">Venta total</th>
                    </tr>
                </thead>
                <tbody>
                    ${lines.map(line => `
                        <tr>
                            <td>
                                <div class="scores-product-table__name">${escapeHtml(line.productName || "Producto sin nombre")}</div>
                                <div class="scores-product-table__id">${escapeHtml(line.productId || line.lineId || "")}</div>
                            </td>
                            <td class="text-end">${formatNumber(line.quantity)}</td>
                            <td class="text-end">${formatNumber(line.costUnit)}</td>
                            <td class="text-end">${formatPercent(line.marginPercent)}</td>
                            <td class="text-end">${formatNumber(line.contractMonths)}m</td>
                            <td class="text-end">${formatNumber(line.monthlyUnitValue)}</td>
                            <td class="text-end">${formatNumber(line.monthlyValue)}</td>
                            <td class="text-end">${formatNumber(line.totalValue ?? line.annualValue)}</td>
                        </tr>
                    `).join("")}
                </tbody>
            </table>
        `;
    }

    function renderRecordRows(record) {
        const isExpanded = state.expandedRecords.has(record.recordId);
        const detailMeta = [
            renderMetaChip("Registro", record.recordId),
            renderMetaChip("Fecha aprovisionamiento", record.provisioningDateDisplay),
            renderMetaChip("Tipo contrato", record.contractType),
            renderMetaChip("BusinessId", record.businessId),
            renderMetaChip("Prorrateo", record.prorationText || "No"),
            renderMetaChip("Dia facturacion", record.billingDay ? String(record.billingDay) : ""),
            renderMetaChip("Renovacion", record.renewalDateDisplay),
            renderMetaChip("Alineacion", record.alignmentDateDisplay),
            renderMetaChip("IVA", optionLabel(optionMaps.hasVat, record.hasVatOptionValue)),
            renderMetaChip("Auto facturable", optionLabel(optionMaps.autoBill, record.autoBillOptionValue)),
            renderMetaChip("Linea resumen", optionLabel(optionMaps.productLine, record.productLineOptionValue)),
            renderMetaChip("Contrato resumen", optionLabel(optionMaps.contractType, record.contractTypeOptionValue)),
            record.isClosedForActivePeriod ? renderMetaChip("Cierre", "Consolidado") : ""
        ].join("");

        return `
            <tr class="scores-record-row" data-record-id="${escapeHtml(record.recordId)}">
                <td>
                    <div class="scores-cell-main">${escapeHtml(record.contractStartDateDisplay || "")}</div>
                    <div class="scores-cell-sub">${escapeHtml(record.contractType || "Sin tipo")}</div>
                </td>
                <td class="text-center">${renderOfferCell(record)}</td>
                <td>
                    <div class="scores-cell-main">${escapeHtml(record.salesPerson || "Sin vendedor")}</div>
                    <div class="scores-cell-sub">${escapeHtml(record.businessId || "Sin BusinessId")}</div>
                </td>
                <td>
                    <div class="scores-cell-main scores-cell-main--proration">${escapeHtml(record.prorationText || "No")}</div>
                </td>
                <td class="text-end"><div class="scores-cell-main">${formatScoreValue(record.score)}</div></td>
                <td class="text-end"><div class="scores-cell-main">${formatNumber(record.commission)}</div></td>
                <td class="text-end"><div class="scores-cell-main">${formatNumber(record.monthlyValue)}</div></td>
                <td class="text-end"><div class="scores-cell-main">${formatNumber(record.totalValue ?? record.annualValue)}</div></td>
                <td class="text-end">
                    <div class="d-flex justify-content-end gap-2 flex-wrap">
                        <button type="button" class="btn btn-sm btn-outline-secondary toggle-detail-btn" data-record-id="${escapeHtml(record.recordId)}">
                            ${isExpanded ? "Ocultar" : `Explorar (${formatNumber(record.productLinesCount)})`}
                        </button>
                        <button type="button" class="btn btn-sm ${record.isVerified ? "btn-outline-primary" : "btn-primary"} verify-record-btn" data-record-id="${escapeHtml(record.recordId)}">
                            ${record.isVerified ? "Editar" : "Verificar"}
                        </button>
                    </div>
                </td>
                <td class="text-center">
                    <span class="scores-verified ${record.isVerified ? "scores-verified--yes" : ""}">${record.isVerified ? "OK" : ""}</span>
                </td>
            </tr>
            <tr class="scores-detail-row ${isExpanded ? "show" : ""}" data-detail-row-for="${escapeHtml(record.recordId)}">
                <td colspan="10">
                    <div class="scores-detail">
                        <div class="scores-detail__meta">${detailMeta}</div>
                        ${renderProductLines(record)}
                    </div>
                </td>
            </tr>
        `;
    }

    function renderGroupMetrics(group) {
        return `
            <div class="scores-group__body">
                <div class="scores-group__summary">
                    <div class="scores-group__metric">
                        <span class="scores-group__metric-label">Aprovisionamientos</span>
                        <span class="scores-group__metric-value">${formatNumber(group.recordCount)}</span>
                    </div>
                    <div class="scores-group__metric">
                        <span class="scores-group__metric-label">Productos</span>
                        <span class="scores-group__metric-value">${formatNumber(group.productLinesCount)}</span>
                    </div>
                    <div class="scores-group__metric">
                        <span class="scores-group__metric-label">Puntaje</span>
                        <span class="scores-group__metric-value">${formatScoreValue(group.totalScore)}</span>
                    </div>
                    <div class="scores-group__metric">
                        <span class="scores-group__metric-label">Comision</span>
                        <span class="scores-group__metric-value">${formatNumber(group.totalCommission)}</span>
                    </div>
                    <div class="scores-group__metric">
                        <span class="scores-group__metric-label">Venta mensual</span>
                        <span class="scores-group__metric-value">${formatNumber(group.totalMonthlyValue)}</span>
                    </div>
                    <div class="scores-group__metric">
                        <span class="scores-group__metric-label">Venta total</span>
                        <span class="scores-group__metric-value">${formatNumber(group.totalValue ?? group.totalAnnualValue)}</span>
                    </div>
                </div>

                <div class="scores-table-wrap">
                    <table class="table scores-table">
                        <thead>
                            <tr>
                                <th>Inicio contrato</th>
                                <th class="text-center">Oferta</th>
                                <th>Vendedor</th>
                                <th>Prorrateo</th>
                                <th class="text-end">Puntaje</th>
                                <th class="text-end">Comision</th>
                                <th class="text-end">Venta mensual</th>
                                <th class="text-end">Venta total</th>
                                <th class="text-end">Acciones</th>
                                <th class="text-center">Verificado</th>
                            </tr>
                        </thead>
                        <tbody>${(group.records || []).map(renderRecordRows).join("")}</tbody>
                    </table>
                </div>
            </div>
        `;
    }

    function renderGroups(board) {
        if (!groupsContainer) {
            return;
        }

        const groups = Array.isArray(board?.groups) ? board.groups : [];
        if (groups.length === 0) {
            groupsContainer.innerHTML = `
                <div class="scores-empty">
                    <h3 class="h5 mb-2">No hay registros para este periodo.</h3>
                    <p class="mb-0">Prueba con otro filtro o actualiza nuevamente la consulta.</p>
                </div>
            `;
            bindGroupEvents();
            renderCloseMonthPanel();
            return;
        }

        groupsContainer.innerHTML = groups.map(group => {
            const groupKey = getGroupKey(group);
            const isExpanded = state.expandedGroups.has(groupKey);
            return `
                <article class="scores-group ${isExpanded ? "scores-group--expanded" : "scores-group--collapsed"}" data-group-key="${escapeHtml(groupKey)}">
                    <div class="scores-group__header">
                        <div class="scores-group__header-main">
                            <h2 class="scores-group__title">${escapeHtml(group.clientName || "Cliente sin asignar")}</h2>
                            <div class="scores-group__compact-line">
                                <span class="scores-group__salesperson">${escapeHtml(group.salesPerson || "Sin vendedor")}</span>
                                ${group.allVerified ? '<span class="scores-group__complete">Verificado completo</span>' : ""}
                            </div>
                            ${isExpanded ? `<p class="scores-group__subtitle">${formatNumber(group.recordCount)} aprovisionamientos y ${formatNumber(group.productLinesCount)} productos.</p>` : ""}
                        </div>
                        <button type="button" class="btn btn-sm btn-outline-secondary toggle-group-btn" data-group-key="${escapeHtml(groupKey)}">
                            ${isExpanded ? "Resumir" : "Desplegar"}
                        </button>
                    </div>
                    ${isExpanded ? renderGroupMetrics(group) : ""}
                </article>
            `;
        }).join("");

        bindGroupEvents();
        renderCloseMonthPanel();
    }

    function renderCloseMonthPanel() {
        if (!closeMonthButton || !closeMonthSummaryText) {
            return;
        }

        const board = state.board;
        if (!board) {
            closeMonthButton.disabled = true;
            closeMonthSummaryText.textContent = "Carga un periodo para revisar si el cierre mensual ya puede ejecutarse.";
            renderCloseMonthLogs();
            return;
        }

        if (!board.supportsMonthClose) {
            closeMonthButton.disabled = true;
            closeMonthSummaryText.textContent = "El cierre de mes solo se habilita en vistas mensuales para evitar consolidaciones ambiguas.";
            renderCloseMonthLogs();
            return;
        }

        if (!board.recordsCount) {
            closeMonthButton.disabled = true;
            closeMonthSummaryText.textContent = `No hay registros para consolidar en ${board.monthClosePeriodLabel || "el periodo actual"}.`;
            renderCloseMonthLogs();
            return;
        }

        if (board.verifiedRecordsCount < board.recordsCount) {
            closeMonthButton.disabled = true;
            closeMonthSummaryText.textContent = `${board.monthClosePeriodLabel}: faltan ${board.recordsCount - board.verifiedRecordsCount} lineas por verificar antes del cierre.`;
            renderCloseMonthLogs();
            return;
        }

        if (board.closedRecordsCount >= board.recordsCount) {
            closeMonthButton.disabled = true;
            closeMonthSummaryText.textContent = `${board.monthClosePeriodLabel}: todas las lineas visibles ya quedaron consolidadas en sales performance.`;
            renderCloseMonthLogs();
            return;
        }

        closeMonthButton.disabled = state.isClosingMonth || state.isLoading || state.isSaving;
        closeMonthSummaryText.textContent = `${board.monthClosePeriodLabel}: ${board.verifiedRecordsCount}/${board.recordsCount} lineas verificadas y ${board.closedRecordsCount} ya consolidadas.`;
        renderCloseMonthLogs();
    }

    function renderCloseMonthLogs() {
        if (!closeMonthLogList || !closeMonthLogEmpty) {
            return;
        }

        const logs = Array.isArray(state.lastCloseResult?.logs) ? state.lastCloseResult.logs : [];
        if (!logs.length) {
            closeMonthLogEmpty.style.display = "";
            closeMonthLogList.innerHTML = "";
            return;
        }

        closeMonthLogEmpty.style.display = "none";
        closeMonthLogList.innerHTML = logs.map(entry => `
            <article class="scores-close-log__entry ${escapeHtml(entry.level || "info")}">
                <div class="scores-close-log__title">${escapeHtml(entry.clientName || "Registro")} ${entry.productName ? `| ${escapeHtml(entry.productName)}` : ""}</div>
                <div class="scores-close-log__text">${escapeHtml(entry.message || "")}</div>
            </article>
        `).join("");
    }

    function bindGroupEvents() {
        groupsContainer.querySelectorAll(".toggle-group-btn").forEach(button => {
            button.addEventListener("click", event => {
                const groupKey = event.currentTarget.dataset.groupKey;
                if (!groupKey) {
                    return;
                }

                if (state.expandedGroups.has(groupKey)) {
                    state.expandedGroups.delete(groupKey);
                } else {
                    state.expandedGroups.add(groupKey);
                }

                renderGroups(state.board);
            });
        });

        groupsContainer.querySelectorAll(".toggle-detail-btn").forEach(button => {
            button.addEventListener("click", event => {
                const recordId = event.currentTarget.dataset.recordId;
                if (!recordId) {
                    return;
                }

                if (state.expandedRecords.has(recordId)) {
                    state.expandedRecords.delete(recordId);
                } else {
                    state.expandedRecords.add(recordId);
                }

                renderGroups(state.board);
            });
        });

        groupsContainer.querySelectorAll(".verify-record-btn").forEach(button => {
            button.addEventListener("click", event => {
                const recordId = event.currentTarget.dataset.recordId;
                if (!recordId) {
                    return;
                }

                openVerifyModal(recordId);
            });
        });
    }

    async function fetchJson(url, requestOptions = {}) {
        const response = await fetch(url, {
            headers: {
                Accept: "application/json",
                ...(requestOptions.headers || {})
            },
            ...requestOptions
        });

        const contentType = response.headers.get("content-type") || "";
        if (!response.ok) {
            const errorPayload = await extractErrorPayload(response, contentType);
            const error = new Error(errorPayload.message || "No fue posible completar la solicitud.");
            error.detail = errorPayload.detail || "";
            error.traceId = errorPayload.traceId || "";
            error.status = response.status;
            throw error;
        }

        if (!contentType.includes("application/json")) {
            const message = await response.text();
            throw new Error(message || "La respuesta del servidor no fue valida.");
        }

        return response.json();
    }

    async function loadBoard() {
        setLoading(true);
        setFilterButtonState();
        setStatus("info", "Consultando puntajes en Dataverse...");

        try {
            const recordsUrl = `${app.dataset.recordsUrl}?filter=${encodeURIComponent(state.filter)}`;
            const board = await fetchJson(recordsUrl);
            state.board = board;
            rebuildIndexes(board);
            updateSummary(board);
            renderGroups(board);
            setStatus("", "");
        } catch (error) {
            console.error(error);
            state.board = null;
            rebuildIndexes(null);
            updateSummary(null);
            renderGroups(null);
            setStatus("error", formatErrorMessage(error, "No fue posible cargar los puntajes."));
        } finally {
            setLoading(false);
            setFilterButtonState();
        }
    }

    function normalizeDraft(detail) {
        return {
            ...detail,
            billingDay: Number(detail.billingDay || 0),
            dealTypeValue: Number(detail.dealTypeValue || 0),
            requiresProration: Boolean(detail.requiresProration),
            lines: (Array.isArray(detail.lines) ? detail.lines : []).map((line, index) => normalizeLine(line, index))
        };
    }

    function normalizeLine(line, index) {
        const costUnit = roundMoney(line.costUnit || 0);
        const marginPercent = roundMoney(line.marginPercent || 0);
        const contractMonths = Math.max(1, Number(line.contractMonths || 12));
        const quantity = Math.max(1, Number(line.quantity || 1));
        const saleUnit = roundMoney(costUnit * (1 + (marginPercent / 100)));
        const monthlyValue = roundMoney(saleUnit * quantity);
        const totalValue = roundMoney(monthlyValue * contractMonths);

        return {
            lineId: line.lineId || `line-${index + 1}`,
            productId: line.productId || "",
            productName: line.productName || "",
            costUnit,
            marginPercent,
            contractMonths,
            quantity,
            suggestedRetailPrice: roundMoney(line.suggestedRetailPrice || 0),
            acelerador: roundMoney(line.acelerador || 0),
            saleUnit,
            monthlyValue,
            totalValue
        };
    }

    function createEmptyLine() {
        return normalizeLine({
            lineId: `line-${Date.now()}`,
            productId: "",
            productName: "",
            costUnit: 0,
            marginPercent: 0,
            contractMonths: 12,
            quantity: 1,
            suggestedRetailPrice: 0,
            acelerador: 0
        }, 0);
    }

    function syncDraftHeaderInputs() {
        if (!state.activeDraft) {
            return null;
        }

        state.activeDraft.firstContractOptionValue = Number(firstContractSelect?.value || 0);
        state.activeDraft.lineOptionValue = Number(lineOptionSelect?.value || 0);
        state.activeDraft.verticalOptionValue = Number(verticalOptionSelect?.value || 0);
        state.activeDraft.billingDay = Number(billingDayInput?.value || 0);
        state.activeDraft.renewalDateValue = renewalDateInput?.value || "";
        state.activeDraft.alignmentDateValue = alignmentDateInput?.value || "";
        state.activeDraft.hasVatOptionValue = Number(hasVatSelect?.value || 0);
        state.activeDraft.autoBillOptionValue = Number(autoBillSelect?.value || 0);
        state.activeDraft.productLineOptionValue = Number(productLineSelect?.value || 0);
        state.activeDraft.contractTypeOptionValue = Number(contractTypeSelect?.value || 0);
        return state.activeDraft;
    }

    function renderVerifyMetaCards() {
        if (!verifyMetaCards || !state.activeDraft) {
            return;
        }

        const draft = state.activeDraft;
        const cards = [
            { label: "Cliente", value: draft.clientName || "Sin cliente" },
            { label: "Inicio contrato", value: draft.contractStartDateDisplay || "Sin fecha" },
            { label: "Vendedor", value: draft.salesPerson || "Sin vendedor" },
            { label: "Oferta", value: draft.offer || "Sin oferta" },
            { label: "BusinessId", value: draft.businessId || "Sin BusinessId" },
            { label: "Prorrateo", value: draft.prorationSummary || "No" }
        ];

        verifyMetaCards.innerHTML = cards.map(card => `
            <article class="scores-verify-meta__card">
                <span class="scores-verify-meta__label">${escapeHtml(card.label)}</span>
                <div class="scores-verify-meta__value">${escapeHtml(card.value)}</div>
            </article>
        `).join("");
    }

    function renderVerifyLines() {
        if (!verifyLinesBody || !state.activeDraft) {
            return;
        }

        const lines = state.activeDraft.lines || [];
        if (!lines.length) {
            verifyLinesBody.innerHTML = `<tr><td colspan="9" class="scores-verify-empty">No hay lineas cargadas. Usa "Agregar linea" para crear una nueva.</td></tr>`;
            return;
        }

        const rows = lines.map((line, index) => `
            <tr data-line-index="${index}">
                <td>
                    <div class="scores-verify-product">
                        <input type="text" class="form-control form-control-sm verify-product-input" value="${escapeHtml(line.productName)}" placeholder="Buscar producto..." autocomplete="off" />
                        <div class="scores-verify-product__search"></div>
                        <div class="scores-verify-product__meta">Lookup: ${escapeHtml(line.productId || "pendiente")} | Sug.: ${formatNumber(line.suggestedRetailPrice)} | Acel.: ${formatNumber(line.acelerador)}</div>
                    </div>
                </td>
                <td><input type="number" step="0.01" min="0" class="form-control form-control-sm text-end verify-cost-input" value="${line.costUnit}" /></td>
                <td><input type="number" step="0.01" class="form-control form-control-sm text-end verify-margin-input" value="${line.marginPercent}" /></td>
                <td><input type="number" step="1" min="1" class="form-control form-control-sm text-end verify-months-input" value="${line.contractMonths}" /></td>
                <td><input type="number" step="0.01" min="0" class="form-control form-control-sm text-end verify-sale-input" value="${line.saleUnit}" /></td>
                <td><input type="number" step="1" min="1" class="form-control form-control-sm text-end verify-qty-input" value="${line.quantity}" /></td>
                <td><input type="number" step="0.01" min="0" class="form-control form-control-sm text-end verify-monthly-input" value="${line.monthlyValue}" /></td>
                <td><input type="number" step="0.01" min="0" class="form-control form-control-sm text-end verify-total-input" value="${line.totalValue}" /></td>
                <td class="text-center"><button type="button" class="btn btn-sm btn-outline-danger verify-remove-line-btn">×</button></td>
            </tr>
        `).join("");

        const totals = lines.reduce((acc, line) => {
            acc.cost += Number(line.costUnit || 0);
            acc.monthly += Number(line.monthlyValue || 0);
            acc.total += Number(line.totalValue || 0);
            return acc;
        }, { cost: 0, monthly: 0, total: 0 });

        verifyLinesBody.innerHTML = rows + `
            <tr class="scores-verify-totals">
                <td>Totales</td>
                <td class="text-end">${formatNumber(totals.cost)}</td>
                <td class="text-end">—</td>
                <td class="text-end">—</td>
                <td class="text-end">—</td>
                <td class="text-end">${formatNumber(lines.reduce((acc, line) => acc + Number(line.quantity || 0), 0))}</td>
                <td class="text-end">${formatNumber(totals.monthly)}</td>
                <td class="text-end">${formatNumber(totals.total)}</td>
                <td></td>
            </tr>
        `;

        bindVerifyLineEvents();
    }

    function renderVerifyDraft() {
        if (!state.activeDraft) {
            return;
        }

        const draft = state.activeDraft;
        verifyModalTitle && (verifyModalTitle.textContent = `Verificar ${draft.clientName || "registro"}`);
        verifyModalSubtitle && (verifyModalSubtitle.textContent = `${draft.offer || "Sin oferta"} | Inicio ${draft.contractStartDateDisplay || "sin fecha"} | ${formatNumber(draft.lines.length)} lineas`);
        firstContractSelect && (firstContractSelect.value = draft.firstContractOptionValue ? String(draft.firstContractOptionValue) : "");
        lineOptionSelect && (lineOptionSelect.value = draft.lineOptionValue ? String(draft.lineOptionValue) : "");
        verticalOptionSelect && (verticalOptionSelect.value = draft.verticalOptionValue ? String(draft.verticalOptionValue) : "");
        billingDayInput && (billingDayInput.value = draft.billingDay ? String(draft.billingDay) : "");
        renewalDateInput && (renewalDateInput.value = draft.renewalDateValue || "");
        alignmentDateInput && (alignmentDateInput.value = draft.alignmentDateValue || "");
        hasVatSelect && (hasVatSelect.value = String(draft.hasVatOptionValue ?? ""));
        autoBillSelect && (autoBillSelect.value = String(draft.autoBillOptionValue ?? ""));
        productLineSelect && (productLineSelect.value = String(draft.productLineOptionValue ?? ""));
        contractTypeSelect && (contractTypeSelect.value = String(draft.contractTypeOptionValue ?? ""));
        renderVerifyMetaCards();
        renderVerifyLines();
        renderVerificationResult(draft.result);
        setModalStatus(draft.warningMessage ? "info" : "", draft.warningMessage || "");
        toggleModalLoading(false);
    }

    function renderVerificationResult(result) {
        verifyResultPoints && (verifyResultPoints.textContent = result ? formatScoreValue(result.points) : "—");
        verifyResultCommission && (verifyResultCommission.textContent = result ? formatNumber(result.commission) : "—");
        verifyResultProration && (verifyResultProration.textContent = result ? (result.prorationText || "No") : "—");
        verifyResultMonthly && (verifyResultMonthly.textContent = result ? formatNumber(result.totalMonthlySale) : "—");
        verifyResultTotal && (verifyResultTotal.textContent = result ? formatNumber(result.totalSale) : "—");
    }

    function markDraftDirty() {
        if (!state.activeDraft) {
            return;
        }

        state.activeDraft.result = null;
        renderVerificationResult(null);
        setModalStatus("info", "Hay cambios pendientes por recalcular antes de guardar.");
    }

    async function openVerifyModal(recordId) {
        if (!verifyModal) {
            return;
        }

        state.activeRecordId = recordId;
        state.activeDraft = null;
        verifyModalTitle && (verifyModalTitle.textContent = "Cargando negocio...");
        verifyModalSubtitle && (verifyModalSubtitle.textContent = "Estamos trayendo la información editable desde Dataverse.");
        setModalStatus("", "");
        toggleModalLoading(true);
        verifyModal.show();

        try {
            const detailUrl = `${app.dataset.detailUrl}?recordId=${encodeURIComponent(recordId)}&filter=${encodeURIComponent(state.filter)}`;
            const detail = await fetchJson(detailUrl);
            state.activeDraft = normalizeDraft(detail);
            renderVerifyDraft();
        } catch (error) {
            console.error(error);
            toggleModalLoading(false);
            setModalStatus("error", formatErrorMessage(error, "No fue posible cargar el detalle de verificacion."));
        }
    }

    function bindVerifyLineEvents() {
        if (!verifyLinesBody || !state.activeDraft) {
            return;
        }

        verifyLinesBody.querySelectorAll("tr[data-line-index]").forEach(row => {
            const index = Number(row.dataset.lineIndex || "-1");
            const line = state.activeDraft.lines[index];
            if (!line) {
                return;
            }

            const productInput = row.querySelector(".verify-product-input");
            const searchBox = row.querySelector(".scores-verify-product__search");
            const costInput = row.querySelector(".verify-cost-input");
            const marginInput = row.querySelector(".verify-margin-input");
            const monthsInput = row.querySelector(".verify-months-input");
            const saleInput = row.querySelector(".verify-sale-input");
            const qtyInput = row.querySelector(".verify-qty-input");
            const monthlyInput = row.querySelector(".verify-monthly-input");
            const totalInput = row.querySelector(".verify-total-input");
            const removeBtn = row.querySelector(".verify-remove-line-btn");

            const hideSuggestions = () => {
                searchBox && searchBox.classList.remove("show");
                if (searchBox) {
                    searchBox.innerHTML = "";
                }
            };

            const syncInputs = () => {
                costInput && (costInput.value = String(line.costUnit));
                marginInput && (marginInput.value = String(line.marginPercent));
                monthsInput && (monthsInput.value = String(line.contractMonths));
                saleInput && (saleInput.value = String(line.saleUnit));
                qtyInput && (qtyInput.value = String(line.quantity));
                monthlyInput && (monthlyInput.value = String(line.monthlyValue));
                totalInput && (totalInput.value = String(line.totalValue));
                const meta = row.querySelector(".scores-verify-product__meta");
                if (meta) {
                    meta.textContent = `Lookup: ${line.productId || "pendiente"} | Sug.: ${formatNumber(line.suggestedRetailPrice)} | Acel.: ${formatNumber(line.acelerador)}`;
                }
            };

            const normalizeFromSource = source => {
                line.costUnit = roundMoney(costInput?.value || line.costUnit);
                line.marginPercent = roundMoney(marginInput?.value || line.marginPercent);
                line.contractMonths = Math.max(1, Number(monthsInput?.value || line.contractMonths || 12));
                line.quantity = Math.max(1, Number(qtyInput?.value || line.quantity || 1));
                line.saleUnit = roundMoney(saleInput?.value || line.saleUnit);
                line.monthlyValue = roundMoney(monthlyInput?.value || line.monthlyValue);
                line.totalValue = roundMoney(totalInput?.value || line.totalValue);

                if (source === "sale") {
                    line.marginPercent = line.costUnit > 0 ? roundMoney(((line.saleUnit / line.costUnit) - 1) * 100) : 0;
                    line.monthlyValue = roundMoney(line.saleUnit * line.quantity);
                    line.totalValue = roundMoney(line.monthlyValue * line.contractMonths);
                } else if (source === "monthly") {
                    line.saleUnit = line.quantity > 0 ? roundMoney(line.monthlyValue / line.quantity) : 0;
                    line.marginPercent = line.costUnit > 0 ? roundMoney(((line.saleUnit / line.costUnit) - 1) * 100) : 0;
                    line.totalValue = roundMoney(line.monthlyValue * line.contractMonths);
                } else if (source === "total") {
                    line.monthlyValue = line.contractMonths > 0 ? roundMoney(line.totalValue / line.contractMonths) : 0;
                    line.saleUnit = line.quantity > 0 ? roundMoney(line.monthlyValue / line.quantity) : 0;
                    line.marginPercent = line.costUnit > 0 ? roundMoney(((line.saleUnit / line.costUnit) - 1) * 100) : 0;
                } else {
                    line.saleUnit = roundMoney(line.costUnit * (1 + (line.marginPercent / 100)));
                    line.monthlyValue = roundMoney(line.saleUnit * line.quantity);
                    line.totalValue = roundMoney(line.monthlyValue * line.contractMonths);
                }

                syncInputs();
                renderVerifyLines();
                markDraftDirty();
            };

            productInput?.addEventListener("input", () => {
                line.productName = productInput.value;
                line.productId = "";
                markDraftDirty();
                hideSuggestions();

                const query = productInput.value.trim();
                if (query.length < 2 || !searchBox) {
                    return;
                }

                window.clearTimeout(line.__timerId);
                line.__timerId = window.setTimeout(async () => {
                    try {
                        const items = await fetchJson(`${app.dataset.productSearchUrl}?q=${encodeURIComponent(query)}`);
                        if (!Array.isArray(items) || !items.length) {
                            hideSuggestions();
                            return;
                        }

                        searchBox.innerHTML = items.map((item, itemIndex) => `
                            <div class="scores-verify-product__item" data-item-index="${itemIndex}">
                                <strong>${escapeHtml(item.description || "")}</strong>
                                <span>Compra: ${formatNumber(item.purchasePrice)} | Sugerido: ${formatNumber(item.suggestedRetailPrice)}</span>
                            </div>
                        `).join("");
                        searchBox.classList.add("show");
                        searchBox.dataset.items = JSON.stringify(items);
                    } catch {
                        hideSuggestions();
                    }
                }, 220);
            });

            productInput?.addEventListener("focus", () => {
                if (searchBox?.children.length) {
                    searchBox.classList.add("show");
                }
            });

            searchBox?.addEventListener("click", event => {
                const itemNode = event.target.closest(".scores-verify-product__item");
                if (!itemNode) {
                    return;
                }

                const items = JSON.parse(searchBox.dataset.items || "[]");
                const item = items[Number(itemNode.dataset.itemIndex || "-1")];
                if (!item) {
                    return;
                }

                line.productId = item.id || "";
                line.productName = item.description || "";
                line.costUnit = roundMoney(item.purchasePrice || 0);
                line.suggestedRetailPrice = roundMoney(item.suggestedRetailPrice || 0);
                line.acelerador = roundMoney(item.acelerador || 0);
                productInput.value = line.productName;
                hideSuggestions();
                normalizeFromSource("cost");
            });

            document.addEventListener("click", event => {
                if (event.target === productInput || searchBox?.contains(event.target)) {
                    return;
                }
                hideSuggestions();
            }, { once: true });

            costInput?.addEventListener("change", () => normalizeFromSource("cost"));
            marginInput?.addEventListener("change", () => normalizeFromSource("margin"));
            monthsInput?.addEventListener("change", () => normalizeFromSource("months"));
            saleInput?.addEventListener("change", () => normalizeFromSource("sale"));
            qtyInput?.addEventListener("change", () => normalizeFromSource("quantity"));
            monthlyInput?.addEventListener("change", () => normalizeFromSource("monthly"));
            totalInput?.addEventListener("change", () => normalizeFromSource("total"));
            removeBtn?.addEventListener("click", () => {
                state.activeDraft.lines.splice(index, 1);
                renderVerifyLines();
                markDraftDirty();
            });
        });
    }

    function buildDraftPayload() {
        const draft = syncDraftHeaderInputs();
        if (!draft) {
            return null;
        }

        return {
            recordId: draft.recordId,
            businessId: draft.businessId || "",
            dealTypeValue: Number(draft.dealTypeValue || 0),
            requiresProration: Boolean(draft.requiresProration),
            scenarioStartDateValue: draft.scenarioStartDateValue || "",
            scenarioEndDateValue: draft.scenarioEndDateValue || "",
            firstContractOptionValue: Number(draft.firstContractOptionValue || 0),
            lineOptionValue: Number(draft.lineOptionValue || 0),
            verticalOptionValue: Number(draft.verticalOptionValue || 0),
            billingDay: Number(draft.billingDay || 0),
            renewalDateValue: draft.renewalDateValue || "",
            alignmentDateValue: draft.alignmentDateValue || "",
            hasVatOptionValue: Number(draft.hasVatOptionValue || 0),
            autoBillOptionValue: Number(draft.autoBillOptionValue || 0),
            productLineOptionValue: Number(draft.productLineOptionValue || 0),
            contractTypeOptionValue: Number(draft.contractTypeOptionValue || 0),
            lines: (draft.lines || []).map(line => ({
                lineId: line.lineId || "",
                productId: line.productId || "",
                productName: line.productName || "",
                costUnit: Number(line.costUnit || 0),
                marginPercent: Number(line.marginPercent || 0),
                contractMonths: Number(line.contractMonths || 0),
                quantity: Number(line.quantity || 0),
                suggestedRetailPrice: Number(line.suggestedRetailPrice || 0),
                acelerador: Number(line.acelerador || 0),
                saleUnit: Number(line.saleUnit || 0),
                monthlyValue: Number(line.monthlyValue || 0),
                totalValue: Number(line.totalValue || 0)
            }))
        };
    }

    async function recalculateVerification() {
        const payload = buildDraftPayload();
        if (!payload) {
            return;
        }

        setRecalculating(true);
        try {
            const result = await fetchJson(app.dataset.recalculateUrl, {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify(payload)
            });

            state.activeDraft.result = result;
            renderVerificationResult(result);
            setModalStatus("success", "Puntaje recalculado usando la logica de la calculadora.");
        } catch (error) {
            console.error(error);
            setModalStatus("error", formatErrorMessage(error, "No fue posible recalcular el puntaje."));
        } finally {
            setRecalculating(false);
        }
    }

    async function submitVerification() {
        const payload = buildDraftPayload();
        if (!payload) {
            return;
        }

        setSaving(true);
        try {
            const result = await fetchJson(app.dataset.verifyUrl, {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify(payload)
            });

            verifyModal?.hide();
            state.activeRecordId = "";
            state.activeDraft = null;
            state.lastCloseResult = state.lastCloseResult;
            await loadBoard();
            setStatus("success", result?.message || "El registro se verifico correctamente.");
        } catch (error) {
            console.error(error);
            setModalStatus("error", formatErrorMessage(error, "No fue posible guardar la verificacion."));
        } finally {
            setSaving(false);
        }
    }

    async function closeMonth() {
        if (!state.board) {
            return;
        }

        setClosingMonth(true);
        setStatus("info", "Consolidando registros del mes en sales performance...");

        try {
            const result = await fetchJson(app.dataset.closeMonthUrl, {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify({ filter: state.filter })
            });

            state.lastCloseResult = result;
            await loadBoard();
            setStatus(result?.hasErrors ? "error" : "success", result?.message || "Cierre mensual finalizado.");
        } catch (error) {
            console.error(error);
            setStatus("error", formatErrorMessage(error, "No fue posible cerrar el mes."));
        } finally {
            setClosingMonth(false);
            renderCloseMonthLogs();
        }
    }

    filterButtons.forEach(button => {
        button.addEventListener("click", async () => {
            const nextFilter = button.dataset.filter;
            if (!nextFilter || nextFilter === state.filter || state.isLoading || state.isSaving || state.isClosingMonth) {
                return;
            }

            state.filter = nextFilter;
            await loadBoard();
        });
    });

    [firstContractSelect, lineOptionSelect, verticalOptionSelect, billingDayInput, renewalDateInput, alignmentDateInput, hasVatSelect, autoBillSelect, productLineSelect, contractTypeSelect]
        .filter(Boolean)
        .forEach(element => {
            element.addEventListener("change", markDraftDirty);
        });

    refreshButton?.addEventListener("click", loadBoard);
    addVerifyLineBtn?.addEventListener("click", () => {
        if (!state.activeDraft) {
            return;
        }

        state.activeDraft.lines.push(createEmptyLine());
        renderVerifyLines();
        markDraftDirty();
    });
    recalculateVerifyScoreBtn?.addEventListener("click", recalculateVerification);
    submitVerifyScoreBtn?.addEventListener("click", submitVerification);
    closeMonthButton?.addEventListener("click", closeMonth);

    verifyModalElement?.addEventListener("hidden.bs.modal", () => {
        state.activeRecordId = "";
        state.activeDraft = null;
        setModalStatus("", "");
        toggleModalLoading(false);
    });

    populateSelect(firstContractSelect, options.firstContractOptions, "Selecciona una opcion");
    populateSelect(lineOptionSelect, options.lineOptions, "Selecciona una linea");
    populateSelect(verticalOptionSelect, options.verticalOptions, "Selecciona una vertical");
    populateSelect(hasVatSelect, options.hasVatOptions, "Selecciona una opcion");
    populateSelect(autoBillSelect, options.autoBillOptions, "Selecciona una opcion");
    populateSelect(productLineSelect, options.productLineOptions, "Selecciona una linea");
    populateSelect(contractTypeSelect, options.contractTypeOptions, "Selecciona un contrato");
    setFilterButtonState();
    updateSummary(null);
    renderCloseMonthPanel();
    loadBoard();
})();
