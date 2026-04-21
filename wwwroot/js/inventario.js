(function () {
    const app = document.getElementById("copiersAdminInventoryApp");
    if (!app) {
        return;
    }

    const loadUrl = app.dataset.loadUrl || "";
    const createUrl = app.dataset.createUrl || "";

    const statusBanner = document.getElementById("copiersAdminStatus");
    const refreshBtn = document.getElementById("copiersAdminRefreshBtn");
    const newBtn = document.getElementById("copiersAdminNewBtn");
    const countLabel = document.getElementById("copiersAdminCount");
    const rowsBody = document.getElementById("copiersAdminRowsBody");
    const emptyState = document.getElementById("copiersAdminEmpty");
    const modal = document.getElementById("copiersAdminModal");
    const modalStatus = document.getElementById("copiersAdminModalStatus");
    const form = document.getElementById("copiersAdminForm");
    const invoiceNumberInput = document.getElementById("copiersAdminInvoiceNumber");
    const linesContainer = document.getElementById("copiersAdminLines");
    const addLineBtn = document.getElementById("copiersAdminAddLineBtn");
    const saveBtn = document.getElementById("copiersAdminSaveBtn");

    const numberFormatter = new Intl.NumberFormat("es-CO", {
        minimumFractionDigits: 0,
        maximumFractionDigits: 2
    });
    const currencyFormatter = new Intl.NumberFormat("es-CO", {
        style: "currency",
        currency: "COP",
        minimumFractionDigits: 0,
        maximumFractionDigits: 0
    });

    const state = {
        busy: false,
        board: null,
        lines: []
    };

    refreshBtn?.addEventListener("click", loadBoard);
    newBtn?.addEventListener("click", openModal);
    addLineBtn?.addEventListener("click", addLine);

    form?.addEventListener("submit", async (event) => {
        event.preventDefault();
        await saveInvoice();
    });

    document.addEventListener("click", (event) => {
        const target = event.target;
        if (!(target instanceof HTMLElement)) {
            return;
        }

        if (target.hasAttribute("data-copiers-admin-close")) {
            closeModal();
            return;
        }

        const removeButton = target.closest("[data-remove-line]");
        if (removeButton instanceof HTMLElement) {
            const lineId = removeButton.dataset.removeLine || "";
            state.lines = state.lines.filter((line) => line.localId !== lineId);
            if (state.lines.length === 0) {
                addLine();
                return;
            }
            renderLines();
        }
    });

    document.addEventListener("input", (event) => {
        const target = event.target;
        if (!(target instanceof HTMLElement)) {
            return;
        }

        const lineElement = target.closest("[data-line-id]");
        if (!(lineElement instanceof HTMLElement)) {
            return;
        }

        const line = state.lines.find((item) => item.localId === lineElement.dataset.lineId);
        if (!line) {
            return;
        }

        const field = target.getAttribute("data-line-field");
        if (field === "supplyId") {
            line.supplyId = target.value;
        } else if (field === "quantity") {
            line.quantity = target.value;
        } else if (field === "unitValueBeforeVat") {
            line.unitValueBeforeVat = target.value;
        }
    });

    document.addEventListener("keydown", (event) => {
        if (event.key === "Escape" && modal && !modal.hidden && !state.busy) {
            closeModal();
        }
    });

    loadBoard();

    async function loadBoard() {
        try {
            setBusy(true);
            showStatus(statusBanner, "info", "Cargando facturas proveedor...");
            state.board = await fetchJson(loadUrl);
            renderRows();
            clearStatus(statusBanner);
        } catch (error) {
            showStatus(statusBanner, "error", getErrorMessage(error));
        } finally {
            setBusy(false);
        }
    }

    function renderRows() {
        const records = Array.isArray(state.board?.records) ? state.board.records : [];
        countLabel.textContent = `${records.length} registro${records.length === 1 ? "" : "s"}`;
        emptyState.hidden = records.length > 0;
        rowsBody.innerHTML = records.map((row) => {
            const approved = Number(row.approvedValue || 0) === 1;
            return `
                <tr>
                    <td>${escapeHtml(row.invoiceNumber)}</td>
                    <td>${escapeHtml(row.supplyName)}</td>
                    <td class="text-end">${numberFormatter.format(Number(row.quantity || 0))}</td>
                    <td class="text-end">${currencyFormatter.format(Number(row.unitValueBeforeVat || 0))}</td>
                    <td><span class="copiers-badge ${approved ? "is-good" : "is-warning"}">${escapeHtml(row.approvedLabel || (approved ? "Si" : "No"))}</span></td>
                </tr>`;
        }).join("");
    }

    function openModal() {
        invoiceNumberInput.value = "";
        state.lines = [];
        addLine();
        clearStatus(modalStatus);
        modal.hidden = false;
        invoiceNumberInput.focus();
    }

    function closeModal() {
        modal.hidden = true;
    }

    function addLine() {
        state.lines.push({
            localId: crypto.randomUUID ? crypto.randomUUID() : `${Date.now()}_${Math.random()}`,
            supplyId: "",
            quantity: "",
            unitValueBeforeVat: ""
        });
        renderLines();
    }

    function renderLines() {
        const supplyOptions = Array.isArray(state.board?.supplyOptions) ? state.board.supplyOptions : [];
        linesContainer.innerHTML = state.lines.map((line, index) => `
            <div class="copiers-line-row" data-line-id="${escapeHtml(line.localId)}">
                <label class="copiers-field">
                    <span>Suministro fila ${index + 1}</span>
                    <select class="form-select" data-line-field="supplyId">
                        <option value="">Selecciona un suministro</option>
                        ${supplyOptions.map((option) => `
                            <option value="${escapeHtml(option.id)}" ${option.id === line.supplyId ? "selected" : ""}>
                                ${escapeHtml(option.label)}
                            </option>`).join("")}
                    </select>
                </label>
                <label class="copiers-field">
                    <span>Cantidad</span>
                    <input class="form-control" data-line-field="quantity" type="number" min="1" step="1" value="${escapeHtml(line.quantity)}" />
                </label>
                <label class="copiers-field">
                    <span>Valor unitario antes IVA</span>
                    <input class="form-control" data-line-field="unitValueBeforeVat" type="number" min="0.01" step="0.01" inputmode="decimal" value="${escapeHtml(line.unitValueBeforeVat)}" />
                </label>
                <button type="button" class="copiers-icon-btn" data-remove-line="${escapeHtml(line.localId)}" title="Eliminar linea" aria-label="Eliminar linea">×</button>
            </div>
        `).join("");
    }

    async function saveInvoice() {
        try {
            setBusy(true);
            saveBtn.disabled = true;
            showStatus(modalStatus, "info", "Guardando factura...");

            const payload = {
                invoiceNumber: invoiceNumberInput.value,
                lines: state.lines.map((line) => ({
                    supplyId: line.supplyId,
                    quantity: Number(line.quantity || 0),
                    unitValueBeforeVat: Number(line.unitValueBeforeVat || 0)
                }))
            };

            const result = await fetchJson(createUrl, {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify(payload)
            });

            closeModal();
            await loadBoard();
            showStatus(statusBanner, "success", result.message || "Factura registrada.");
        } catch (error) {
            showStatus(modalStatus, "error", getErrorMessage(error));
        } finally {
            saveBtn.disabled = false;
            setBusy(false);
        }
    }

    async function fetchJson(url, options) {
        const response = await fetch(url, {
            ...(options || {}),
            headers: {
                Accept: "application/json",
                ...(options?.headers || {})
            }
        });
        const payload = await readPayload(response);
        if (!response.ok) {
            throw createResponseError(payload);
        }
        return payload;
    }

    async function readPayload(response) {
        const contentType = response.headers.get("content-type") || "";
        if (contentType.includes("application/json")) {
            return await response.json();
        }
        return { message: await response.text() };
    }

    function createResponseError(payload) {
        const error = new Error(payload?.detail || payload?.message || "Ocurrio un error inesperado.");
        error.payload = payload;
        return error;
    }

    function getErrorMessage(error) {
        return error instanceof Error ? error.message : "Ocurrio un error inesperado.";
    }

    function setBusy(value) {
        state.busy = value;
        refreshBtn.disabled = value;
        newBtn.disabled = value;
    }

    function showStatus(element, tone, message) {
        if (!element) {
            return;
        }

        element.textContent = message || "";
        element.className = `copiers-status is-visible ${tone ? "is-" + tone : ""}`;
    }

    function clearStatus(element) {
        if (!element) {
            return;
        }

        element.textContent = "";
        element.className = "copiers-status";
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
