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
        if (refreshButton) {
            refreshButton.disabled = loading || state.isSaving;
        }

        filterButtons.forEach(button => {
            button.disabled = loading || state.isSaving;
        });
    }

    function setSaving(saving) {
        state.isSaving = saving;
        if (submitVerifyScoreBtn) {
            submitVerifyScoreBtn.disabled = saving;
        }

        if (refreshButton) {
            refreshButton.disabled = saving || state.isLoading;
        }
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

    function rebuildRecordMap(board) {
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

        if (summaryClients) {
            summaryClients.textContent = formatNumber(safeBoard.clientsCount);
        }

        if (summaryRecords) {
            summaryRecords.textContent = formatNumber(safeBoard.recordsCount);
        }

        if (summaryProducts) {
            summaryProducts.textContent = formatNumber(safeBoard.productLinesCount);
        }

        if (summaryScore) {
            summaryScore.textContent = formatScoreValue(safeBoard.totalScore);
        }

        if (summaryCommission) {
            summaryCommission.textContent = formatNumber(safeBoard.totalCommission);
        }

        if (summaryAnnualValue) {
            summaryAnnualValue.textContent = formatNumber(safeBoard.totalAnnualValue);
        }
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

    function renderProductLines(record) {
        const lines = Array.isArray(record.productLines) ? record.productLines : [];
        if (lines.length === 0) {
            return `<div class="scores-product-empty">No se detectaron lineas de productos en la descripcion.</div>`;
        }

        return `
            <table class="scores-product-table">
                <thead>
                    <tr>
                        <th>Producto</th>
                        <th class="text-end">Cantidad</th>
                        <th class="text-end">Valor unitario mes</th>
                        <th class="text-end">Valor mes</th>
                        <th class="text-end">Valor 12m</th>
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
                            <td class="text-end">${formatNumber(line.monthlyUnitValue)}</td>
                            <td class="text-end">${formatNumber(line.monthlyValue)}</td>
                            <td class="text-end fw-semibold">${formatNumber(line.annualValue)}</td>
                        </tr>
                    `).join("")}
                </tbody>
            </table>
        `;
    }

    function renderRecordRows(record) {
        const isExpanded = state.expandedRecords.has(record.recordId);
        const detailLabel = isExpanded
            ? "Ocultar detalle"
            : `Ver detalle${record.productLinesCount ? ` (${formatNumber(record.productLinesCount)} productos)` : ""}`;
        const verifyButtonClass = record.isVerified ? "btn-outline-success" : "btn-primary";

        const detailMeta = [
            renderMetaChip("Fecha aprovisionamiento", record.provisioningDateDisplay),
            renderMetaChip("Tipo contrato", record.contractType),
            renderMetaChip("BusinessId", record.businessId),
            record.descriptionClientName && record.descriptionClientName !== record.clientName
                ? renderMetaChip("Cliente detectado", record.descriptionClientName)
                : ""
        ].join("");

        return `
            <tr class="scores-record-row" data-record-id="${escapeHtml(record.recordId)}">
                <td>
                    <div class="scores-cell-main">${escapeHtml(record.contractStartDateDisplay || "")}</div>
                    <div class="scores-cell-sub">${escapeHtml(record.recordId || "")}</div>
                </td>
                <td>
                    <div class="scores-cell-main">${escapeHtml(record.offer || "Sin oferta")}</div>
                    <div class="scores-cell-sub">${escapeHtml(record.contractType || "Sin tipo")}</div>
                </td>
                <td>
                    <div class="scores-cell-main">${escapeHtml(record.salesPerson || "Sin vendedor")}</div>
                    <div class="scores-cell-sub">${escapeHtml(record.provisioningDateDisplay || "Sin fecha aprovisionamiento")}</div>
                </td>
                <td class="text-end">
                    <div class="scores-cell-main">${formatScoreValue(record.score)}</div>
                </td>
                <td class="text-end">
                    <div class="scores-cell-main">${formatNumber(record.commission)}</div>
                </td>
                <td>
                    <span class="scores-record-pill">${formatNumber(record.productLinesCount)} productos</span>
                </td>
                <td class="text-center">
                    <span class="scores-verified ${record.isVerified ? "scores-verified--yes" : ""}">${record.isVerified ? "✓" : ""}</span>
                </td>
                <td class="text-end">
                    <div class="scores-cell-main">${formatNumber(record.annualValue)}</div>
                    <div class="scores-cell-sub">Mensual ${formatNumber(record.monthlyValue)}</div>
                </td>
                <td class="text-end">
                    <div class="d-flex justify-content-end gap-2 flex-wrap">
                        <button type="button" class="btn btn-sm btn-outline-secondary toggle-detail-btn" data-record-id="${escapeHtml(record.recordId)}">${escapeHtml(detailLabel)}</button>
                        <button type="button" class="btn btn-sm ${verifyButtonClass} verify-record-btn" data-record-id="${escapeHtml(record.recordId)}">Verificar</button>
                    </div>
                </td>
            </tr>
            <tr class="scores-detail-row ${isExpanded ? "show" : ""}" data-detail-row-for="${escapeHtml(record.recordId)}">
                <td colspan="9">
                    <div class="scores-detail">
                        <div class="scores-detail__meta">${detailMeta}</div>
                        ${renderProductLines(record)}
                    </div>
                </td>
            </tr>
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

        groupsContainer.innerHTML = groups.map(group => `
            <article class="scores-group">
                <div class="scores-group__header">
                    <div>
                        <h2 class="scores-group__title">${escapeHtml(group.clientName || "Cliente sin asignar")}</h2>
                        <p class="scores-group__subtitle">
                            ${formatNumber(group.recordCount)} aprovisionamientos y ${formatNumber(group.productLinesCount)} productos detectados.
                        </p>
                    </div>
                    <div class="scores-group__metrics">
                        <div class="scores-group__metric">
                            <span class="scores-group__metric-label">Puntaje</span>
                            <strong class="scores-group__metric-value">${formatScoreValue(group.totalScore)}</strong>
                        </div>
                        <div class="scores-group__metric">
                            <span class="scores-group__metric-label">Comision</span>
                            <strong class="scores-group__metric-value">${formatNumber(group.totalCommission)}</strong>
                        </div>
                        <div class="scores-group__metric">
                            <span class="scores-group__metric-label">Valor mensual</span>
                            <strong class="scores-group__metric-value">${formatNumber(group.totalMonthlyValue)}</strong>
                        </div>
                        <div class="scores-group__metric">
                            <span class="scores-group__metric-label">Valor 12 meses</span>
                            <strong class="scores-group__metric-value">${formatNumber(group.totalAnnualValue)}</strong>
                        </div>
                    </div>
                </div>

                <div class="scores-table-wrap">
                    <table class="table scores-table">
                        <thead>
                            <tr>
                                <th>Inicio contrato</th>
                                <th>Oferta</th>
                                <th>Vendedor</th>
                                <th class="text-end">Puntaje</th>
                                <th class="text-end">Comision</th>
                                <th>Detalle</th>
                                <th class="text-center">Verificado</th>
                                <th class="text-end">Valor contrato</th>
                                <th class="text-end">Acciones</th>
                            </tr>
                        </thead>
                        <tbody>
                            ${(group.records || []).map(renderRecordRows).join("")}
                        </tbody>
                    </table>
                </div>
            </article>
        `).join("");

        bindGroupEvents();
    }

    function bindGroupEvents() {
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

        if (!response.ok) {
            const message = await response.text();
            throw new Error(message || "No fue posible completar la solicitud.");
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
            rebuildRecordMap(board);
            updateSummary(board);
            renderGroups(board);
            setStatus("", "");
        } catch (error) {
            console.error(error);
            state.board = null;
            rebuildRecordMap(null);
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
        if (verifyModalTitle) {
            verifyModalTitle.textContent = `Verificar ${record.clientName || "registro"}`;
        }

        if (verifyModalSubtitle) {
            verifyModalSubtitle.textContent = `${record.offer || "Sin oferta"} · Inicio ${record.contractStartDateDisplay || "sin fecha"} · ${formatNumber(record.productLinesCount)} productos`;
        }

        if (firstContractSelect) {
            firstContractSelect.value = record.firstContractOptionValue ? String(record.firstContractOptionValue) : "";
        }

        if (lineOptionSelect) {
            lineOptionSelect.value = record.lineOptionValue ? String(record.lineOptionValue) : "";
        }

        if (verticalOptionSelect) {
            verticalOptionSelect.value = record.verticalOptionValue ? String(record.verticalOptionValue) : "";
        }

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

            if (verifyModal) {
                verifyModal.hide();
            }

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
