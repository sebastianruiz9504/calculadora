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
    const verifyModalElement = document.getElementById("verifyScoreModal");
    const verifyModalTitle = document.getElementById("verifyScoreModalTitle");
    const verifyModalSubtitle = document.getElementById("verifyScoreModalSubtitle");
    const verifyModalStatus = document.getElementById("verifyScoreModalStatus");
    const firstContractSelect = document.getElementById("firstContractSelect");
    const lineOptionSelect = document.getElementById("lineOptionSelect");
    const verticalOptionSelect = document.getElementById("verticalOptionSelect");
    const submitVerifyScoreBtn = document.getElementById("submitVerifyScoreBtn");
    const verifyModal = verifyModalElement && window.bootstrap ? new bootstrap.Modal(verifyModalElement) : null;

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
        groupMap: new Map(),
        expandedGroups: new Set(),
        expandedRecords: new Set(),
        activeRecordId: "",
        isLoading: false,
        isSaving: false
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

    function formatPercent(value) {
        return `${formatNumber(value)}%`;
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

    function setLoading(loading) {
        state.isLoading = loading;
        refreshButton && (refreshButton.disabled = loading || state.isSaving);
        filterButtons.forEach(button => {
            button.disabled = loading || state.isSaving;
        });
    }

    function setSaving(saving) {
        state.isSaving = saving;
        submitVerifyScoreBtn && (submitVerifyScoreBtn.disabled = saving);
        refreshButton && (refreshButton.disabled = saving || state.isLoading);
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
        state.groupMap = new Map();

        if (!board || !Array.isArray(board.groups)) {
            return;
        }

        board.groups.forEach(group => {
            const groupKey = getGroupKey(group);
            state.groupMap.set(groupKey, group);

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
            return '<div class="scores-product-empty">No se detectaron lineas de productos en la descripcion.</div>';
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
            record.descriptionClientName && record.descriptionClientName !== record.clientName
                ? renderMetaChip("Cliente detectado", record.descriptionClientName)
                : ""
        ].join("");

        return `
            <tr class="scores-record-row" data-record-id="${escapeHtml(record.recordId)}">
                <td>
                    <div class="scores-cell-main">${escapeHtml(record.contractStartDateDisplay || "")}</div>
                    <div class="scores-cell-sub">${escapeHtml(record.contractType || "Sin tipo")}</div>
                </td>
                <td class="text-center">
                    ${renderOfferCell(record)}
                </td>
                <td>
                    <div class="scores-cell-main">${escapeHtml(record.salesPerson || "Sin vendedor")}</div>
                    <div class="scores-cell-sub">${escapeHtml(record.businessId || "Sin BusinessId")}</div>
                </td>
                <td>
                    <div class="scores-cell-main scores-cell-main--proration">${escapeHtml(record.prorationText || "No")}</div>
                </td>
                <td class="text-end">
                    <div class="scores-cell-main">${formatScoreValue(record.score)}</div>
                </td>
                <td class="text-end">
                    <div class="scores-cell-main">${formatNumber(record.commission)}</div>
                </td>
                <td class="text-end">
                    <div class="scores-cell-main">${formatNumber(record.monthlyValue)}</div>
                </td>
                <td class="text-end">
                    <div class="scores-cell-main">${formatNumber(record.totalValue ?? record.annualValue)}</div>
                </td>
                <td class="text-end">
                    <div class="d-flex justify-content-end gap-2 flex-wrap">
                        <button type="button" class="btn btn-sm btn-outline-secondary toggle-detail-btn" data-record-id="${escapeHtml(record.recordId)}">
                            ${isExpanded ? "Ocultar" : `Explorar (${formatNumber(record.productLinesCount)})`}
                        </button>
                        <button type="button" class="btn btn-sm ${record.isVerified ? "btn-outline-success" : "btn-primary"} verify-record-btn" data-record-id="${escapeHtml(record.recordId)}">
                            Verificar
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
                        <tbody>
                            ${(group.records || []).map(renderRecordRows).join("")}
                        </tbody>
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
            return;
        }

        groupsContainer.innerHTML = groups.map(group => {
            const groupKey = getGroupKey(group);
            const isExpanded = state.expandedGroups.has(groupKey);
            const compactMeta = `
                <div class="scores-group__compact-line">
                    <span class="scores-group__salesperson">${escapeHtml(group.salesPerson || "Sin vendedor")}</span>
                    ${group.allVerified ? '<span class="scores-group__complete">Verificado completo</span>' : ""}
                </div>
            `;

            return `
                <article class="scores-group ${isExpanded ? "scores-group--expanded" : "scores-group--collapsed"}" data-group-key="${escapeHtml(groupKey)}">
                    <div class="scores-group__header">
                        <div class="scores-group__header-main">
                            <h2 class="scores-group__title">${escapeHtml(group.clientName || "Cliente sin asignar")}</h2>
                            ${compactMeta}
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

    async function fetchJson(url, options = {}) {
        const response = await fetch(url, {
            headers: {
                Accept: "application/json",
                ...(options.headers || {})
            },
            ...options
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
            setStatus("error", error?.message || "No fue posible cargar los puntajes.");
        } finally {
            setLoading(false);
            setFilterButtonState();
        }
    }

    function openVerifyModal(recordId) {
        const record = state.recordMap.get(recordId);
        if (!record || !verifyModal) {
            return;
        }

        state.activeRecordId = recordId;
        verifyModalTitle && (verifyModalTitle.textContent = `Verificar ${record.clientName || "registro"}`);
        verifyModalSubtitle && (verifyModalSubtitle.textContent = `${record.offer || "Sin oferta"} | Inicio ${record.contractStartDateDisplay || "sin fecha"} | ${formatNumber(record.productLinesCount)} productos`);
        firstContractSelect && (firstContractSelect.value = record.firstContractOptionValue ? String(record.firstContractOptionValue) : "");
        lineOptionSelect && (lineOptionSelect.value = record.lineOptionValue ? String(record.lineOptionValue) : "");
        verticalOptionSelect && (verticalOptionSelect.value = record.verticalOptionValue ? String(record.verticalOptionValue) : "");
        setModalStatus("", "");
        verifyModal.show();
    }

    async function submitVerification() {
        if (!state.activeRecordId) {
            return;
        }

        const firstContractValue = Number(firstContractSelect?.value || 0);
        const lineValue = Number(lineOptionSelect?.value || 0);
        const verticalValue = Number(verticalOptionSelect?.value || 0);

        if (!firstContractValue || !lineValue || !verticalValue) {
            setModalStatus("error", "Debes completar los tres campos antes de enviar.");
            return;
        }

        setSaving(true);
        setModalStatus("", "");

        try {
            const result = await fetchJson(app.dataset.verifyUrl, {
                method: "POST",
                headers: {
                    "Content-Type": "application/json"
                },
                body: JSON.stringify({
                    recordId: state.activeRecordId,
                    firstContractOptionValue: firstContractValue,
                    lineOptionValue: lineValue,
                    verticalOptionValue: verticalValue
                })
            });

            verifyModal && verifyModal.hide();
            state.activeRecordId = "";
            const successMessage = result?.message || "El registro se verifico correctamente.";
            await loadBoard();
            setStatus("success", successMessage);
        } catch (error) {
            console.error(error);
            setModalStatus("error", error?.message || "No fue posible guardar la verificacion.");
        } finally {
            setSaving(false);
        }
    }

    filterButtons.forEach(button => {
        button.addEventListener("click", async () => {
            const nextFilter = button.dataset.filter;
            if (!nextFilter || nextFilter === state.filter || state.isLoading || state.isSaving) {
                return;
            }

            state.filter = nextFilter;
            await loadBoard();
        });
    });

    refreshButton?.addEventListener("click", loadBoard);
    submitVerifyScoreBtn?.addEventListener("click", submitVerification);

    verifyModalElement?.addEventListener("hidden.bs.modal", () => {
        state.activeRecordId = "";
        setModalStatus("", "");
    });

    populateSelect(firstContractSelect, options.firstContractOptions, "Selecciona una opcion");
    populateSelect(lineOptionSelect, options.lineOptions, "Selecciona una linea");
    populateSelect(verticalOptionSelect, options.verticalOptions, "Selecciona una vertical");
    setFilterButtonState();
    updateSummary(null);
    loadBoard();
})();
