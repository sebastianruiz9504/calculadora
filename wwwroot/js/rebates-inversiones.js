(function () {
    const app = document.getElementById("rebatesInversionesApp");
    if (!app) {
        return;
    }

    const loadUrl = app.dataset.loadUrl || "";
    const saveUrl = app.dataset.saveUrl || "";
    const deleteUrl = app.dataset.deleteUrl || "";
    const yearSelect = document.getElementById("rbiYearSelect");
    const refreshBtn = document.getElementById("rbiRefreshBtn");
    const status = document.getElementById("rbiStatus");
    const boardMessage = document.getElementById("rbiBoardMessage");
    const monthStrip = document.getElementById("rbiMonthStrip");
    const rebatesBody = document.getElementById("rbiRebatesBody");
    const financialIncomeBody = document.getElementById("rbiFinancialIncomeBody");
    const rebatesEmpty = document.getElementById("rbiRebatesEmpty");
    const financialIncomeEmpty = document.getElementById("rbiFinancialIncomeEmpty");
    const rebatesTotal = document.getElementById("rbiRebatesTotal");
    const rebatesCount = document.getElementById("rbiRebatesCount");
    const financialIncomeTotal = document.getElementById("rbiFinancialIncomeTotal");
    const financialIncomeCount = document.getElementById("rbiFinancialIncomeCount");
    const combinedTotal = document.getElementById("rbiCombinedTotal");
    const combinedCount = document.getElementById("rbiCombinedCount");

    const modal = document.getElementById("rbiEditorModal");
    const modalStatus = document.getElementById("rbiModalStatus");
    const editorTitle = document.getElementById("rbiEditorTitle");
    const editorSubtitle = document.getElementById("rbiEditorSubtitle");
    const closeBtn = document.getElementById("rbiCloseBtn");
    const cancelBtn = document.getElementById("rbiCancelBtn");
    const saveBtn = document.getElementById("rbiSaveBtn");
    const deleteBtn = document.getElementById("rbiDeleteBtn");
    const form = document.getElementById("rbiEditorForm");
    const recordIdInput = document.getElementById("rbiRecordIdInput");
    const typeInput = document.getElementById("rbiTypeInput");
    const dateInput = document.getElementById("rbiDateInput");
    const valueInput = document.getElementById("rbiValueInput");

    const moneyFormatter = new Intl.NumberFormat("es-CO", {
        style: "currency",
        currency: "COP",
        minimumFractionDigits: 0,
        maximumFractionDigits: 0
    });

    const state = {
        year: parseInteger(app.dataset.initialYear, new Date().getFullYear()),
        board: null,
        busy: false,
        editorRecord: null
    };

    refreshBtn?.addEventListener("click", () => {
        if (!state.busy) {
            loadBoard(state.year);
        }
    });

    yearSelect?.addEventListener("change", () => {
        const year = parseInteger(yearSelect.value, state.year);
        loadBoard(year);
    });

    document.querySelectorAll("[data-rbi-add]").forEach(button => {
        button.addEventListener("click", () => {
            openEditor(null, button.getAttribute("data-rbi-add") || "rebate");
        });
    });

    [rebatesBody, financialIncomeBody].forEach(body => {
        body?.addEventListener("click", event => {
            const target = event.target;
            if (!(target instanceof HTMLElement) || state.busy) {
                return;
            }

            const deleteAction = target.closest("[data-rbi-delete]");
            if (deleteAction instanceof HTMLElement) {
                event.stopPropagation();
                const record = findRecord(deleteAction.getAttribute("data-rbi-delete") || "");
                if (record) {
                    openEditor(record, record.typeKey);
                    deleteRecord();
                }
                return;
            }

            const row = target.closest("[data-rbi-record-id]");
            if (!(row instanceof HTMLElement)) {
                return;
            }

            const record = findRecord(row.getAttribute("data-rbi-record-id") || "");
            if (record) {
                openEditor(record, record.typeKey);
            }
        });
    });

    [closeBtn, cancelBtn].forEach(button => {
        button?.addEventListener("click", closeEditor);
    });

    modal?.addEventListener("click", event => {
        const target = event.target;
        if (target instanceof HTMLElement && target.hasAttribute("data-rbi-close")) {
            closeEditor();
        }
    });

    document.addEventListener("keydown", event => {
        if (event.key === "Escape" && modal && !modal.hidden && !state.busy) {
            closeEditor();
        }
    });

    form?.addEventListener("submit", event => {
        event.preventDefault();
        saveEditor();
    });

    deleteBtn?.addEventListener("click", deleteRecord);

    loadBoard(state.year);

    async function loadBoard(year) {
        try {
            setBusy(true);
            showStatus(status, "info", "Cargando registros manuales...");

            const response = await fetch(`${loadUrl}?year=${encodeURIComponent(year)}`, {
                headers: { Accept: "application/json" }
            });
            const payload = await readPayload(response);
            if (!response.ok) {
                throw createResponseError(payload);
            }

            state.board = payload;
            state.year = Number(payload.selectedYear || year);
            renderBoard();
            showStatus(status, "", "");
        } catch (error) {
            showStatus(status, "error", error instanceof Error ? error.message : "No fue posible cargar registros.");
        } finally {
            setBusy(false);
        }
    }

    function renderBoard() {
        const board = state.board || {};
        const rebates = Array.isArray(board.rebates) ? board.rebates : [];
        const financialIncome = Array.isArray(board.financialIncome) ? board.financialIncome : [];
        renderYearOptions(board.availableYears || [state.year]);
        boardMessage && (boardMessage.textContent = board.message || "Registros manuales cargados.");
        rebatesTotal && (rebatesTotal.textContent = moneyFormatter.format(Number(board.rebatesTotal || 0)));
        rebatesCount && (rebatesCount.textContent = `${rebates.length} registro${rebates.length === 1 ? "" : "s"}`);
        financialIncomeTotal && (financialIncomeTotal.textContent = moneyFormatter.format(Number(board.financialIncomeTotal || 0)));
        financialIncomeCount && (financialIncomeCount.textContent = `${financialIncome.length} registro${financialIncome.length === 1 ? "" : "s"}`);
        combinedTotal && (combinedTotal.textContent = moneyFormatter.format(Number(board.rebatesTotal || 0) + Number(board.financialIncomeTotal || 0)));
        combinedCount && (combinedCount.textContent = `${Number(board.totalCount || 0)} registro${Number(board.totalCount || 0) === 1 ? "" : "s"}`);
        renderMonths(board.months || []);
        renderRecords(rebatesBody, rebates, rebatesEmpty);
        renderRecords(financialIncomeBody, financialIncome, financialIncomeEmpty);
    }

    function renderYearOptions(years) {
        if (!yearSelect) {
            return;
        }

        const normalizedYears = Array.from(new Set((Array.isArray(years) ? years : [])
            .map(value => Number(value))
            .filter(value => Number.isInteger(value) && value > 0)
            .concat([state.year])))
            .sort((a, b) => b - a);

        yearSelect.innerHTML = normalizedYears
            .map(year => `<option value="${year}">${year}</option>`)
            .join("");
        yearSelect.value = String(state.year);
    }

    function renderMonths(months) {
        if (!monthStrip) {
            return;
        }

        monthStrip.innerHTML = (Array.isArray(months) ? months : []).map(month => {
            const rebateTotal = Number(month.rebatesTotal || 0);
            const incomeTotal = Number(month.financialIncomeTotal || 0);
            const classes = [
                "rbi-month",
                Math.abs(rebateTotal) >= 0.01 ? "has-rebate" : "",
                Math.abs(incomeTotal) >= 0.01 ? "has-income" : ""
            ].filter(Boolean).join(" ");

            return `
                <article class="${classes}">
                    <strong>${escapeHtml(month.label || "")}</strong>
                    <span>Rebates: ${escapeHtml(moneyFormatter.format(rebateTotal))}</span>
                    <span>Financieros: ${escapeHtml(moneyFormatter.format(incomeTotal))}</span>
                </article>
            `;
        }).join("");
    }

    function renderRecords(target, records, emptyTarget) {
        if (!target) {
            return;
        }

        const rows = Array.isArray(records) ? records : [];
        if (emptyTarget) {
            emptyTarget.hidden = rows.length > 0;
        }

        if (!rows.length) {
            target.innerHTML = "";
            return;
        }

        target.innerHTML = rows.map(record => `
            <tr class="rbi-row" data-rbi-record-id="${escapeHtml(record.recordId || "")}" tabindex="0">
                <td>${escapeHtml(record.dateDisplay || "-")}</td>
                <td>${escapeHtml(record.monthLabel || "-")}</td>
                <td class="text-end rbi-row__value">${escapeHtml(moneyFormatter.format(Number(record.value || 0)))}</td>
                <td>${escapeHtml(record.modifiedOnDisplay || record.createdOnDisplay || "-")}</td>
                <td class="text-end">
                    <button type="button" class="btn btn-sm btn-outline-danger rbi-action-btn" data-rbi-delete="${escapeHtml(record.recordId || "")}">
                        Eliminar
                    </button>
                </td>
            </tr>
        `).join("");
    }

    function findRecord(recordId) {
        const board = state.board || {};
        const rows = []
            .concat(Array.isArray(board.rebates) ? board.rebates : [])
            .concat(Array.isArray(board.financialIncome) ? board.financialIncome : []);
        return rows.find(record => (record.recordId || "") === recordId) || null;
    }

    function openEditor(record, typeKey) {
        state.editorRecord = record || null;
        const resolvedType = normalizeType(typeKey || record?.typeKey || "rebate");
        recordIdInput && (recordIdInput.value = record?.recordId || "");
        typeInput && (typeInput.value = resolvedType);
        dateInput && (dateInput.value = record?.dateValue || todayValue());
        valueInput && (valueInput.value = record ? String(Number(record.value || 0).toFixed(2)) : "");
        deleteBtn && (deleteBtn.hidden = !record?.recordId);
        showStatus(modalStatus, "", "");

        const typeLabel = resolvedType === "financial-income" ? "Ingreso financiero" : "Rebate";
        editorTitle && (editorTitle.textContent = record ? `Editar ${typeLabel.toLowerCase()}` : `Nuevo ${typeLabel.toLowerCase()}`);
        editorSubtitle && (editorSubtitle.textContent = `${typeLabel}: fecha y valor que se reflejaran en el P&L.`);

        if (modal) {
            modal.hidden = false;
            document.body.classList.add("rbi-modal-open");
        }

        window.setTimeout(() => dateInput?.focus(), 30);
    }

    function closeEditor() {
        if (modal) {
            modal.hidden = true;
        }

        document.body.classList.remove("rbi-modal-open");
        state.editorRecord = null;
        showStatus(modalStatus, "", "");
    }

    async function saveEditor() {
        const payload = {
            recordId: recordIdInput?.value || "",
            typeKey: normalizeType(typeInput?.value || "rebate"),
            dateValue: dateInput?.value || "",
            value: Number(valueInput?.value || 0)
        };

        if (!payload.dateValue) {
            showStatus(modalStatus, "error", "Selecciona una fecha.");
            return;
        }

        if (!Number.isFinite(payload.value) || Math.abs(payload.value) < 0.01) {
            showStatus(modalStatus, "error", "El valor debe ser diferente de cero.");
            return;
        }

        try {
            setBusy(true);
            showStatus(modalStatus, "info", "Guardando registro en Dataverse...");
            const response = await fetch(saveUrl, {
                method: "POST",
                headers: {
                    Accept: "application/json",
                    "Content-Type": "application/json"
                },
                body: JSON.stringify(payload)
            });
            const result = await readPayload(response);
            if (!response.ok) {
                throw createResponseError(result);
            }

            closeEditor();
            await loadBoard(new Date(`${payload.dateValue}T00:00:00`).getFullYear() || state.year);
            showStatus(status, "success", result.message || "Registro guardado correctamente.");
        } catch (error) {
            showStatus(modalStatus, "error", error instanceof Error ? error.message : "No fue posible guardar el registro.");
        } finally {
            setBusy(false);
        }
    }

    async function deleteRecord() {
        const recordId = recordIdInput?.value || state.editorRecord?.recordId || "";
        if (!recordId || state.busy) {
            return;
        }

        if (!window.confirm("¿Eliminar este registro manual?")) {
            return;
        }

        try {
            setBusy(true);
            showStatus(modalStatus, "info", "Eliminando registro...");
            const response = await fetch(deleteUrl, {
                method: "POST",
                headers: {
                    Accept: "application/json",
                    "Content-Type": "application/json"
                },
                body: JSON.stringify({ recordId })
            });
            const result = await readPayload(response);
            if (!response.ok) {
                throw createResponseError(result);
            }

            closeEditor();
            await loadBoard(state.year);
            showStatus(status, "success", result.message || "Registro eliminado correctamente.");
        } catch (error) {
            showStatus(modalStatus, "error", error instanceof Error ? error.message : "No fue posible eliminar el registro.");
        } finally {
            setBusy(false);
        }
    }

    function setBusy(isBusy) {
        state.busy = isBusy;
        [yearSelect, refreshBtn, saveBtn, deleteBtn, closeBtn, cancelBtn, dateInput, valueInput].forEach(element => {
            if (element) {
                element.disabled = isBusy;
            }
        });

        document.querySelectorAll("[data-rbi-add], [data-rbi-delete]").forEach(element => {
            element.disabled = isBusy;
        });
    }

    function showStatus(target, type, message) {
        if (!target) {
            return;
        }

        target.textContent = message || "";
        target.className = "rbi-status";
        if (type) {
            target.classList.add(`is-${type}`);
        }

        target.classList.toggle("is-visible", Boolean(message));
    }

    async function readPayload(response) {
        const text = await response.text();
        if (!text) {
            return {};
        }

        try {
            return JSON.parse(text);
        } catch {
            return { message: text };
        }
    }

    function createResponseError(payload) {
        return new Error(payload?.detail || payload?.message || "La solicitud no fue exitosa.");
    }

    function parseInteger(value, fallback) {
        const parsed = Number.parseInt(value, 10);
        return Number.isFinite(parsed) ? parsed : fallback;
    }

    function normalizeType(value) {
        return value === "financial-income" ? "financial-income" : "rebate";
    }

    function todayValue() {
        const today = new Date();
        const month = String(today.getMonth() + 1).padStart(2, "0");
        const day = String(today.getDate()).padStart(2, "0");
        return `${today.getFullYear()}-${month}-${day}`;
    }

    function escapeHtml(value) {
        return (value ?? "").toString()
            .replace(/&/g, "&amp;")
            .replace(/</g, "&lt;")
            .replace(/>/g, "&gt;")
            .replace(/"/g, "&quot;")
            .replace(/'/g, "&#39;");
    }
})();
