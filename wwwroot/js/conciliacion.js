(() => {
    const app = document.getElementById("conciliacionApp");
    if (!app) {
        return;
    }

    const updatePaymentUrl = app.dataset.updatePaymentUrl || "";
    const statusBox = document.getElementById("cncStatus");
    const tabButtons = Array.from(app.querySelectorAll("[data-cnc-tab]"));
    const panels = Array.from(app.querySelectorAll("[data-cnc-panel]"));
    const paymentSearch = document.getElementById("cncPaymentSearch");
    const paymentStatusFilter = document.getElementById("cncPaymentStatusFilter");
    const paymentFlowFilter = document.getElementById("cncPaymentFlowFilter");
    const paymentRowsBody = document.getElementById("cncPaymentRows");
    const paymentCount = document.getElementById("cncPaymentCount");

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
                return "success";
            case "Rechazado":
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

    const updateRowStatus = (row, payloadRow, fallbackStatus) => {
        const nextStatus = payloadRow?.status || fallbackStatus;
        row.dataset.status = nextStatus;
        const badge = row.querySelector("[data-status-label]");
        if (badge) {
            badge.textContent = payloadRow?.statusLabel || statusLabel(nextStatus);
            badge.className = `cnc-badge cnc-badge--${payloadRow?.statusTone || statusTone(nextStatus)}`;
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

    tabButtons.forEach((button) => {
        button.addEventListener("click", () => setActiveTab(button.dataset.cncTab || ""));
    });

    [paymentSearch, paymentStatusFilter, paymentFlowFilter].forEach((input) => {
        input?.addEventListener("input", applyPaymentFilters);
        input?.addEventListener("change", applyPaymentFilters);
    });

    paymentRowsBody?.querySelectorAll("[data-cnc-action]").forEach((button) => {
        button.addEventListener("click", () => updatePaymentStatus(button));
    });

    applyPaymentFilters();
})();
