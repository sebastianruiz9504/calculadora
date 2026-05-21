(() => {
    const app = document.getElementById("conciliacionApp");
    if (!app) {
        return;
    }

    const updatePaymentUrl = app.dataset.updatePaymentUrl || "";
    const preflightPaymentUrl = app.dataset.preflightPaymentUrl || "";
    const statusBox = document.getElementById("cncStatus");
    const tabButtons = Array.from(app.querySelectorAll("[data-cnc-tab]"));
    const panels = Array.from(app.querySelectorAll("[data-cnc-panel]"));
    const paymentSearch = document.getElementById("cncPaymentSearch");
    const paymentStatusFilter = document.getElementById("cncPaymentStatusFilter");
    const paymentFlowFilter = document.getElementById("cncPaymentFlowFilter");
    const paymentRowsBody = document.getElementById("cncPaymentRows");
    const paymentCount = document.getElementById("cncPaymentCount");
    const genericTableSearches = Array.from(app.querySelectorAll("[data-cnc-table-search]"));
    const reassignModal = document.getElementById("cncReassignModal");
    const reassignDescription = document.getElementById("cncReassignDescription");
    const reassignCategory = document.getElementById("cncReassignCategory");
    const reassignApply = document.getElementById("cncReassignApply");
    let activeReassignRow = null;

    const categoryOptions = {
        Entrada: [
            { value: "entrada-fe", label: "Pago de factura" },
            { value: "entrada-comprobante", label: "Comprobante contable" },
            { value: "traslado-interno", label: "Traslado interno" }
        ],
        Salida: [
            { value: "salida-fe", label: "Factura electronica" },
            { value: "cuenta-cobro", label: "Documento soporte" },
            { value: "comprobante-contable", label: "Comprobante contable" }
        ],
        Traslado: [
            { value: "traslado-interno", label: "Traslado interno" }
        ]
    };

    const categoryTone = (value) => {
        switch (value) {
            case "entrada-fe":
            case "salida-fe":
                return "success";
            case "cuenta-cobro":
            case "comprobante-contable":
            case "entrada-comprobante":
                return "info";
            case "traslado-interno":
                return "neutral";
            default:
                return "warning";
        }
    };

    const categoryLabel = (value) => {
        const allOptions = Object.values(categoryOptions).flat();
        return allOptions.find((item) => item.value === value)?.label || "Sin clasificar";
    };

    const setStatus = (message, tone) => {
        if (!statusBox) {
            return;
        }

        statusBox.textContent = message || "";
        statusBox.className = "cnc-status";
        if (tone) {
            statusBox.classList.add(`is-${tone}`);
        }
        statusBox.classList.toggle("show", Boolean(message));
    };

    const setActiveTab = (key) => {
        tabButtons.forEach((button) => {
            const active = button.dataset.cncTab === key;
            button.classList.toggle("is-active", active);
            button.setAttribute("aria-selected", active ? "true" : "false");
        });

        panels.forEach((panel) => {
            const active = panel.dataset.cncPanel === key;
            panel.classList.toggle("is-active", active);
            panel.hidden = !active;
        });
    };

    const normalizeText = (value) => String(value || "").trim().toLowerCase();

    const applyGenericTableFilter = (key) => {
        const input = app.querySelector(`[data-cnc-table-search="${CSS.escape(key)}"]`);
        const body = app.querySelector(`[data-cnc-table-body="${CSS.escape(key)}"]`);
        const count = app.querySelector(`[data-cnc-table-count="${CSS.escape(key)}"]`);
        const query = normalizeText(input?.value);
        let visible = 0;

        Array.from(body?.querySelectorAll("tr[data-record-id]") || []).forEach((row) => {
            const matches = !query || normalizeText(row.dataset.search).includes(query);
            row.hidden = !matches;
            if (matches) {
                visible += 1;
            }
        });

        if (count) {
            count.textContent = `${visible.toLocaleString("es-CO")} fila${visible === 1 ? "" : "s"}`;
        }
    };

    const getPaymentRows = () => Array.from(paymentRowsBody?.querySelectorAll("tr[data-record-id]") || []);

    const getDetailRow = (recordId) => paymentRowsBody?.querySelector(`tr[data-detail-for="${CSS.escape(recordId)}"]`);

    const applyPaymentFilters = () => {
        const query = normalizeText(paymentSearch?.value);
        const status = String(paymentStatusFilter?.value || "").trim();
        const flow = String(paymentFlowFilter?.value || "").trim();
        let visible = 0;

        getPaymentRows().forEach((row) => {
            const rowStatus = row.dataset.status || "";
            const rowFlow = row.dataset.flow || "";
            const rowSearch = normalizeText(row.dataset.search);
            const matches = (!query || rowSearch.includes(query))
                && (!status || rowStatus === status)
                && (!flow || rowFlow === flow);
            row.hidden = !matches;
            const detail = getDetailRow(row.dataset.recordId || "");
            if (detail) {
                detail.hidden = !matches;
            }
            if (matches) {
                visible += 1;
            }
        });

        if (paymentCount) {
            paymentCount.textContent = `${visible.toLocaleString("es-CO")} fila${visible === 1 ? "" : "s"}`;
        }
    };

    const statusTone = (status) => {
        switch (status) {
            case "Aprobado":
            case "ListoSiigo":
                return "success";
            case "Rechazado":
            case "BloqueadoSiigo":
                return "danger";
            case "RevisionManual":
            case "DiferenciaFueraRango":
            case "FacturaAmbigua":
                return "warning";
            case "Sugerido":
                return "info";
            default:
                return "neutral";
        }
    };

    const statusLabel = (status) => {
        switch (status) {
            case "RevisionManual":
                return "Revision manual";
            case "ListoSiigo":
                return "Listo Siigo";
            case "BloqueadoSiigo":
                return "Bloqueado pre-Siigo";
            case "DiferenciaFueraRango":
                return "Diferencia fuera de rango";
            case "SinFacturaDescripcion":
                return "Sin factura en descripcion";
            case "FacturaNoEncontrada":
                return "Factura no encontrada";
            case "FacturaAmbigua":
                return "Factura ambigua";
            default:
                return status || "Sin estado";
        }
    };

    const money = (value) => Number(value || 0).toLocaleString("es-CO", {
        style: "currency",
        currency: "COP",
        maximumFractionDigits: 0
    });

    const updateRowStatus = (row, payloadRow, fallbackStatus) => {
        const nextStatus = payloadRow?.status || fallbackStatus;
        row.dataset.status = nextStatus;
        const badge = row.querySelector("[data-status-label]");
        if (badge) {
            badge.textContent = payloadRow?.statusLabel || statusLabel(nextStatus);
            badge.className = `cnc-badge cnc-badge--${payloadRow?.statusTone || statusTone(nextStatus)}`;
        }

        if (payloadRow) {
            const preflightBadge = row.querySelector("[data-preflight-label]");
            if (preflightBadge) {
                preflightBadge.textContent = payloadRow.preflightStatusLabel || "Sin validar";
                preflightBadge.className = `cnc-badge cnc-badge--${payloadRow.preflightStatusTone || "neutral"}`;
            }

            const totals = row.querySelector("[data-preflight-totals]");
            if (totals) {
                const debit = Number(payloadRow.preflightDebitTotal || 0);
                const credit = Number(payloadRow.preflightCreditTotal || 0);
                totals.textContent = debit || credit
                    ? `Db ${money(debit)} / Cr ${money(credit)}`
                    : (payloadRow.preflightValidatedOnDisplay || "Sin log");
            }

            const detail = getDetailRow(row.dataset.recordId || "");
            const message = detail?.querySelector("[data-preflight-message]");
            if (message) {
                message.textContent = payloadRow.preflightMessage || "Sin validacion pre-Siigo.";
            }
        }
    };

    const actionReason = (action) => {
        if (action === "Aprobado") {
            return "Aprobado desde modulo Conciliacion.";
        }

        const label = action === "Rechazado"
            ? "Motivo del rechazo"
            : "Nota de revision";
        return window.prompt(label, "") || "";
    };

    const updatePaymentStatus = async (button) => {
        const row = button.closest("tr[data-record-id]");
        if (!row) {
            return;
        }

        const action = button.dataset.cncAction || "";
        const recordId = row.dataset.recordId || "";
        if (!recordId || !action || !updatePaymentUrl) {
            setStatus("No se encontro la ruta o el registro para actualizar.", "error");
            return;
        }

        const reason = actionReason(action);
        if ((action === "Rechazado" || action === "RevisionManual") && !reason.trim()) {
            return;
        }

        const buttons = Array.from(row.querySelectorAll("[data-cnc-action]"));
        buttons.forEach((item) => { item.disabled = true; });
        setStatus("Actualizando cruce en Dataverse...", "info");

        try {
            const response = await fetch(updatePaymentUrl, {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify({ recordId, status: action, reason })
            });
            const payload = await response.json().catch(() => ({}));
            if (!response.ok) {
                throw new Error(payload.detail || payload.message || "No fue posible actualizar el cruce.");
            }

            updateRowStatus(row, payload.row, action);
            applyPaymentFilters();
            setStatus(payload.message || "Cruce actualizado.", "success");
        } catch (error) {
            setStatus(error instanceof Error ? error.message : "Ocurrio un error inesperado.", "error");
        } finally {
            buttons.forEach((item) => { item.disabled = false; });
        }
    };

    const validatePaymentPreflight = async (button) => {
        const row = button.closest("tr[data-record-id]");
        if (!row) {
            return;
        }

        const recordId = row.dataset.recordId || "";
        if (!recordId || !preflightPaymentUrl) {
            setStatus("No se encontro la ruta o el registro para validar.", "error");
            return;
        }

        const buttons = Array.from(row.querySelectorAll("[data-cnc-action], [data-cnc-preflight]"));
        buttons.forEach((item) => { item.disabled = true; });
        setStatus("Validando borrador pre-Siigo...", "info");

        try {
            const response = await fetch(preflightPaymentUrl, {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify({ recordId })
            });
            const payload = await response.json().catch(() => ({}));
            if (!response.ok) {
                throw new Error(payload.detail || payload.message || "No fue posible validar el borrador.");
            }

            updateRowStatus(row, payload.row, payload.row?.status || row.dataset.status || "");
            applyPaymentFilters();
            setStatus(payload.message || "Validacion pre-Siigo finalizada.", payload.isReadyForSiigo ? "success" : "info");
        } catch (error) {
            setStatus(error instanceof Error ? error.message : "Ocurrio un error inesperado.", "error");
        } finally {
            buttons.forEach((item) => { item.disabled = false; });
        }
    };

    const closeReassignModal = () => {
        if (reassignModal) {
            reassignModal.hidden = true;
        }
        activeReassignRow = null;
    };

    const openReassignModal = (row) => {
        activeReassignRow = row;
        const direction = row.dataset.direction || "";
        const options = categoryOptions[direction] || categoryOptions.Salida;

        if (reassignDescription) {
            reassignDescription.textContent = row.dataset.description || "Registro sin descripcion.";
        }

        if (reassignCategory) {
            reassignCategory.innerHTML = "";
            options.forEach((option) => {
                const item = document.createElement("option");
                item.value = option.value;
                item.textContent = option.label;
                item.selected = option.value === row.dataset.currentType;
                reassignCategory.appendChild(item);
            });
        }

        if (reassignModal) {
            reassignModal.hidden = false;
        }
    };

    const applyReassignCategory = () => {
        if (!activeReassignRow || !reassignCategory) {
            return;
        }

        const recordId = activeReassignRow.dataset.recordId || "";
        const nextValue = reassignCategory.value;
        const nextLabel = categoryLabel(nextValue);
        const nextTone = categoryTone(nextValue);
        const rows = recordId
            ? Array.from(app.querySelectorAll(`[data-record-id="${CSS.escape(recordId)}"]`))
            : [activeReassignRow];

        rows.forEach((row) => {
            row.dataset.currentType = nextValue;
            const badge = row.querySelector("[data-cnc-type-label]");
            if (badge) {
                badge.textContent = nextLabel;
                badge.className = `cnc-badge cnc-badge--${nextTone}`;
            }
        });

        closeReassignModal();
        setStatus("Categoria aplicada en esta vista. Falta conectar el guardado en Dataverse.", "info");
    };

    const shouldIgnoreRowClick = (target) => Boolean(target.closest("button, a, input, select, textarea, details, summary, label"));

    tabButtons.forEach((button) => {
        button.addEventListener("click", () => setActiveTab(button.dataset.cncTab || ""));
    });

    app.querySelectorAll("[data-cnc-tab-target]").forEach((button) => {
        button.addEventListener("click", (event) => {
            event.stopPropagation();
            setActiveTab(button.dataset.cncTabTarget || "");
        });
    });

    [paymentSearch, paymentStatusFilter, paymentFlowFilter].forEach((input) => {
        input?.addEventListener("input", applyPaymentFilters);
        input?.addEventListener("change", applyPaymentFilters);
    });

    genericTableSearches.forEach((input) => {
        const key = input.dataset.cncTableSearch || "";
        input.addEventListener("input", () => applyGenericTableFilter(key));
        input.addEventListener("change", () => applyGenericTableFilter(key));
        applyGenericTableFilter(key);
    });

    paymentRowsBody?.querySelectorAll("[data-cnc-action]").forEach((button) => {
        button.addEventListener("click", () => updatePaymentStatus(button));
    });

    paymentRowsBody?.querySelectorAll("[data-cnc-preflight]").forEach((button) => {
        button.addEventListener("click", () => validatePaymentPreflight(button));
    });

    app.querySelectorAll("[data-cnc-reassign]").forEach((row) => {
        row.addEventListener("click", (event) => {
            if (shouldIgnoreRowClick(event.target)) {
                return;
            }

            openReassignModal(row);
        });
    });

    app.querySelectorAll("[data-cnc-open-reassign]").forEach((button) => {
        button.addEventListener("click", (event) => {
            event.stopPropagation();
            const row = button.closest("[data-cnc-reassign]");
            if (row) {
                openReassignModal(row);
            }
        });
    });

    app.querySelectorAll("[data-cnc-close-modal]").forEach((button) => {
        button.addEventListener("click", closeReassignModal);
    });

    reassignModal?.addEventListener("click", (event) => {
        if (event.target === reassignModal) {
            closeReassignModal();
        }
    });

    reassignApply?.addEventListener("click", applyReassignCategory);

    applyPaymentFilters();
})();
