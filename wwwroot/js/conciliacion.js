(() => {
    const app = document.getElementById("conciliacionApp");
    if (!app) {
        return;
    }

    const updatePaymentUrl = app.dataset.updatePaymentUrl || "";
    const preflightPaymentUrl = app.dataset.preflightPaymentUrl || "";
    const dryRunPaymentUrl = app.dataset.dryRunPaymentUrl || "";
    const sendPaymentUrl = app.dataset.sendPaymentUrl || "";
    const invoiceSearchUrl = app.dataset.invoiceSearchUrl || "";
    const invoiceAssignUrl = app.dataset.invoiceAssignUrl || "";
    const syncHealthUrl = app.dataset.syncHealthUrl || "";
    const statusBox = document.getElementById("cncStatus");
    const tabButtons = Array.from(app.querySelectorAll("[data-cnc-tab]"));
    const panels = Array.from(app.querySelectorAll("[data-cnc-panel]"));
    const verticalBar = app.querySelector(".cnc-vertical-bar");
    const paymentSearch = document.getElementById("cncPaymentSearch");
    const paymentStatusFilter = document.getElementById("cncPaymentStatusFilter");
    const paymentRowsBody = document.getElementById("cncPaymentRows");
    const paymentCount = document.getElementById("cncPaymentCount");
    const genericTableSearches = Array.from(app.querySelectorAll("[data-cnc-table-search]"));
    const verticalButtons = Array.from(app.querySelectorAll("[data-cnc-vertical]"));
    const verticalCount = document.getElementById("cncVerticalCount");
    const reassignModal = document.getElementById("cncReassignModal");
    const reassignDescription = document.getElementById("cncReassignDescription");
    const reassignCategory = document.getElementById("cncReassignCategory");
    const reassignApply = document.getElementById("cncReassignApply");
    const invoiceModal = document.getElementById("cncInvoiceModal");
    const invoiceDescription = document.getElementById("cncInvoiceDescription");
    const invoiceQuery = document.getElementById("cncInvoiceQuery");
    const invoiceValue = document.getElementById("cncInvoiceValue");
    const invoiceSearchButton = document.getElementById("cncInvoiceSearchButton");
    const invoiceResults = document.getElementById("cncInvoiceResults");
    const invoiceSelected = document.getElementById("cncInvoiceSelected");
    const invoiceSave = document.getElementById("cncInvoiceSave");
    const syncSummary = app.querySelector("[data-cnc-sync-summary]");
    const syncGrid = app.querySelector("[data-cnc-sync-grid]");
    const syncRefreshButton = app.querySelector("[data-cnc-sync-refresh]");
    let activeReassignRow = null;
    let activeInvoiceRow = null;
    let selectedInvoiceId = "";
    let activeVertical = app.dataset.activeVertical || "Cloud";
    let syncLoaded = false;
    let syncLoading = false;
    const validTabKeys = new Set(tabButtons.map((button) => button.dataset.cncTab || "").filter(Boolean));
    const activeTabStorageKey = `conciliacion.activeTab:${window.location.pathname}:${window.location.search}`;

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

    const resolveTabKey = (key) => {
        const candidate = String(key || "").trim();
        if (validTabKeys.has(candidate)) {
            return candidate;
        }

        return tabButtons.find((button) => button.classList.contains("is-active"))?.dataset.cncTab
            || tabButtons[0]?.dataset.cncTab
            || "";
    };

    const persistActiveTab = (key) => {
        const resolved = resolveTabKey(key);
        if (!resolved) {
            return;
        }

        try {
            window.localStorage.setItem(activeTabStorageKey, resolved);
        } catch {
            // Local storage can be disabled; the URL hash still preserves the tab.
        }

        const nextUrl = `${window.location.pathname}${window.location.search}#${encodeURIComponent(resolved)}`;
        window.history.replaceState(null, "", nextUrl);
    };

    const resolveInitialTab = () => {
        const hashTab = decodeURIComponent((window.location.hash || "").replace(/^#/, ""));
        if (validTabKeys.has(hashTab)) {
            return hashTab;
        }

        try {
            const stored = window.localStorage.getItem(activeTabStorageKey) || "";
            if (validTabKeys.has(stored)) {
                return stored;
            }
        } catch {
            // Ignore storage errors and keep the server-rendered active tab.
        }

        return resolveTabKey("");
    };

    const setActiveTab = (key, persist = true) => {
        const resolvedKey = resolveTabKey(key);
        tabButtons.forEach((button) => {
            const active = button.dataset.cncTab === resolvedKey;
            button.classList.toggle("is-active", active);
            button.setAttribute("aria-selected", active ? "true" : "false");
        });

        panels.forEach((panel) => {
            const active = panel.dataset.cncPanel === resolvedKey;
            panel.classList.toggle("is-active", active);
            panel.hidden = !active;
        });

        if (verticalBar) {
            verticalBar.hidden = resolvedKey === "sincronizacion";
        }
        if (persist) {
            persistActiveTab(resolvedKey);
        }
        if (resolvedKey === "sincronizacion") {
            loadSyncHealth();
        }
    };

    const normalizeText = (value) => String(value || "").trim().toLowerCase();

    const rowCountLabel = (value) => `${value.toLocaleString("es-CO")} fila${value === 1 ? "" : "s"}`;

    const verticalMatches = (flow) => {
        const normalizedFlow = normalizeText(flow);
        if (!normalizedFlow) {
            return true;
        }

        return normalizedFlow.includes(normalizeText(activeVertical));
    };

    const updateVerticalButtons = () => {
        verticalButtons.forEach((button) => {
            const active = button.dataset.cncVertical === activeVertical;
            button.classList.toggle("is-active", active);
            button.setAttribute("aria-pressed", active ? "true" : "false");
        });
        app.dataset.activeVertical = activeVertical;
    };

    const updateVerticalCount = () => {
        if (!verticalCount) {
            return;
        }

        const activePanel = panels.find((panel) => !panel.hidden);
        const visibleRows = activePanel
            ? Array.from(activePanel.querySelectorAll("tr[data-record-id]")).filter((row) => !row.hidden).length
            : 0;
        verticalCount.textContent = `${activeVertical}: ${rowCountLabel(visibleRows)}`;
    };

    const applyGenericTableFilter = (key) => {
        const input = app.querySelector(`[data-cnc-table-search="${CSS.escape(key)}"]`);
        const body = app.querySelector(`[data-cnc-table-body="${CSS.escape(key)}"]`);
        const count = app.querySelector(`[data-cnc-table-count="${CSS.escape(key)}"]`);
        const query = normalizeText(input?.value);
        const stageCounters = Array.from(body?.querySelectorAll("[data-cnc-stage-count]") || []);
        const stageCounts = new Map(stageCounters.map((counter) => [counter, 0]));
        let visible = 0;

        Array.from(body?.querySelectorAll("tr[data-record-id]") || []).forEach((row) => {
            const matches = verticalMatches(row.dataset.flow)
                && (!query || normalizeText(row.dataset.search).includes(query));
            row.hidden = !matches;
            if (matches) {
                visible += 1;
                const counter = row.closest(".cnc-pipeline-stage")?.querySelector("[data-cnc-stage-count]");
                if (counter) {
                    stageCounts.set(counter, (stageCounts.get(counter) || 0) + 1);
                }
            }
        });

        if (count) {
            count.textContent = rowCountLabel(visible);
        }
        stageCounters.forEach((counter) => {
            counter.textContent = rowCountLabel(stageCounts.get(counter) || 0);
        });
        updateVerticalCount();
    };

    const getPaymentRows = () => Array.from(paymentRowsBody?.querySelectorAll("tr[data-record-id]") || []);

    const getDetailRow = (recordId) => paymentRowsBody?.querySelector(`tr[data-detail-for="${CSS.escape(recordId)}"]`);

    const applyPaymentFilters = () => {
        const query = normalizeText(paymentSearch?.value);
        const status = String(paymentStatusFilter?.value || "").trim();
        let visible = 0;
        const stageCounters = Array.from(paymentRowsBody?.querySelectorAll("[data-cnc-stage-count]") || []);
        const stageCounts = new Map(stageCounters.map((counter) => [counter, 0]));

        getPaymentRows().forEach((row) => {
            const rowStatus = row.dataset.status || "";
            const rowFlow = row.dataset.flow || "";
            const rowSearch = normalizeText(row.dataset.search);
            const matches = (!query || rowSearch.includes(query))
                && (!status || rowStatus === status)
                && verticalMatches(rowFlow);
            row.hidden = !matches;
            const detail = getDetailRow(row.dataset.recordId || "");
            if (detail) {
                detail.hidden = !matches;
            }
            if (matches) {
                visible += 1;
                const counter = row.closest(".cnc-pipeline-stage")?.querySelector("[data-cnc-stage-count]");
                if (counter) {
                    stageCounts.set(counter, (stageCounts.get(counter) || 0) + 1);
                }
            }
        });

        if (paymentCount) {
            paymentCount.textContent = rowCountLabel(visible);
        }
        stageCounters.forEach((counter) => {
            counter.textContent = rowCountLabel(stageCounts.get(counter) || 0);
        });
        updateVerticalCount();
    };

    const refreshAllFilters = () => {
        updateVerticalButtons();
        applyPaymentFilters();
        genericTableSearches.forEach((input) => applyGenericTableFilter(input.dataset.cncTableSearch || ""));
        updateVerticalCount();
    };

    const setCollapsibleState = (section, collapsed) => {
        section.dataset.cncCollapsed = collapsed ? "true" : "false";
        const button = section.querySelector(":scope > .cnc-pipeline-stage__header [data-cnc-collapse-toggle], :scope > .cnc-table-toolbar [data-cnc-collapse-toggle]");
        if (button) {
            button.textContent = collapsed ? "Expandir" : "Contraer";
            button.setAttribute("aria-expanded", collapsed ? "false" : "true");
        }
    };

    const initializeCollapsibleTables = () => {
        const sections = Array.from(app.querySelectorAll(".cnc-pipeline-stage, .cnc-payment-panel"))
            .filter((section) => section.querySelector(":scope > .cnc-table-wrap"));

        sections.forEach((section, index) => {
            section.dataset.cncCollapsible = "true";
            const tableWrap = section.querySelector(":scope > .cnc-table-wrap");
            const header = section.querySelector(":scope > .cnc-pipeline-stage__header")
                || section.querySelector(":scope > .cnc-table-toolbar");
            if (!tableWrap || !header || header.querySelector("[data-cnc-collapse-toggle]")) {
                setCollapsibleState(section, true);
                return;
            }

            const button = document.createElement("button");
            button.type = "button";
            button.className = "cnc-collapse-button";
            button.dataset.cncCollapseToggle = "";
            button.setAttribute("aria-controls", `cncTableSection${index}`);
            tableWrap.id = tableWrap.id || `cncTableSection${index}`;
            button.addEventListener("click", () => {
                setCollapsibleState(section, section.dataset.cncCollapsed !== "true");
            });
            header.appendChild(button);
            setCollapsibleState(section, true);
        });
    };

    const statusTone = (status) => {
        switch (status) {
            case "Aprobado":
            case "ListoSiigo":
            case "EnviadoSiigo":
            case "Conciliado":
                return "success";
            case "Rechazado":
            case "BloqueadoSiigo":
            case "ErrorSiigo":
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
            case "EnviadoSiigo":
                return "Enviado Siigo";
            case "ErrorSiigo":
                return "Error Siigo";
            case "Conciliado":
                return "Conciliado";
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

    const canSendPaymentStatus = (status) => status === "ListoSiigo" || status === "ErrorSiigo";

    const money = (value) => Number(value || 0).toLocaleString("es-CO", {
        style: "currency",
        currency: "COP",
        maximumFractionDigits: 0
    });

    const renderIssueList = (row, selector, issues) => {
        const list = row.querySelector(selector);
        if (!list) {
            return;
        }

        list.innerHTML = "";
        const values = Array.isArray(issues)
            ? issues.filter((issue) => String(issue || "").trim())
            : [];
        list.hidden = values.length === 0;
        values.forEach((issue) => {
            const item = document.createElement("li");
            item.textContent = issue;
            list.appendChild(item);
        });
    };

    const moneyPrecise = (value) => Number(value || 0).toLocaleString("es-CO", {
        style: "currency",
        currency: "COP",
        maximumFractionDigits: 2
    });

    const numberLabel = (value) => Number(value || 0).toLocaleString("es-CO");

    const setSyncLoading = () => {
        if (syncSummary) {
            syncSummary.innerHTML = "";
            const badge = document.createElement("span");
            badge.className = "cnc-badge cnc-badge--info";
            badge.textContent = "Consultando";
            const text = document.createElement("strong");
            text.textContent = "Calculando totales de Dataverse y Siigo...";
            syncSummary.append(badge, text);
        }
        if (syncGrid) {
            syncGrid.innerHTML = "";
            const empty = document.createElement("div");
            empty.className = "cnc-sync-empty";
            empty.textContent = "Consultando fuentes del periodo.";
            syncGrid.appendChild(empty);
        }
    };

    const renderSyncMetric = (label, value) => {
        const item = document.createElement("div");
        item.className = "cnc-sync-metric";
        const title = document.createElement("span");
        title.textContent = label;
        const number = document.createElement("strong");
        number.textContent = value;
        item.append(title, number);
        return item;
    };

    const renderSyncSystem = (label, total, count, vat) => {
        const system = document.createElement("div");
        system.className = "cnc-sync-system";
        const title = document.createElement("span");
        title.textContent = label;
        const amount = document.createElement("strong");
        amount.textContent = moneyPrecise(total);
        const meta = document.createElement("small");
        meta.textContent = `${numberLabel(count)} registros | IVA ${moneyPrecise(vat)}`;
        system.append(title, amount, meta);
        return system;
    };

    const renderSyncHealth = (payload) => {
        syncLoaded = true;
        if (syncSummary) {
            syncSummary.innerHTML = "";
            const badge = document.createElement("span");
            badge.className = `cnc-badge cnc-badge--${payload.statusTone || "neutral"}`;
            badge.textContent = payload.statusLabel || "Sin estado";
            const text = document.createElement("strong");
            text.textContent = `${payload.periodLabel || "Periodo"} | ${numberLabel(payload.totalDifferenceRows)} filas con diferencia`;
            const time = document.createElement("small");
            time.textContent = `Ultima consulta: ${payload.generatedAtDisplay || "sin fecha"}`;
            syncSummary.append(badge, text, time);
        }

        if (!syncGrid) {
            return;
        }

        syncGrid.innerHTML = "";
        const items = Array.isArray(payload.items) ? payload.items : [];
        if (items.length === 0) {
            const empty = document.createElement("div");
            empty.className = "cnc-sync-empty";
            empty.textContent = "No hay cruces configurados para este periodo.";
            syncGrid.appendChild(empty);
            return;
        }

        items.forEach((item) => {
            const card = document.createElement("article");
            card.className = `cnc-sync-card cnc-sync-card--${item.statusTone || "neutral"}`;

            const header = document.createElement("header");
            const heading = document.createElement("div");
            const title = document.createElement("h3");
            title.textContent = item.label || "Cruce";
            const description = document.createElement("p");
            description.textContent = item.description || "";
            heading.append(title, description);
            const badge = document.createElement("span");
            badge.className = `cnc-badge cnc-badge--${item.statusTone || "neutral"}`;
            badge.textContent = item.statusLabel || "Sin estado";
            header.append(heading, badge);

            const systems = document.createElement("div");
            systems.className = "cnc-sync-systems";
            systems.append(
                renderSyncSystem(item.dataverseLabel || "Dataverse", item.dataverseTotal, item.dataverseCount, item.dataverseVat),
                renderSyncSystem(item.siigoLabel || "Siigo", item.siigoTotal, item.siigoCount, item.siigoVat)
            );

            const metrics = document.createElement("div");
            metrics.className = "cnc-sync-metrics";
            metrics.append(
                renderSyncMetric("Diferencia Dataverse - Siigo", moneyPrecise(item.differenceTotal)),
                renderSyncMetric("Diferencia registros", numberLabel(item.countDifference)),
                renderSyncMetric("Diferencia IVA", moneyPrecise(item.vatDifference)),
                renderSyncMetric("Filas por revisar", rowCountLabel(Number(item.differenceRows || 0)))
            );

            const notes = document.createElement("small");
            notes.className = "cnc-sync-notes";
            notes.textContent = item.notes || "";
            card.append(header, systems, metrics, notes);
            syncGrid.appendChild(card);
        });
    };

    const loadSyncHealth = async (force = false) => {
        if (!syncHealthUrl || syncLoading || (syncLoaded && !force)) {
            return;
        }

        syncLoading = true;
        if (syncRefreshButton) {
            syncRefreshButton.disabled = true;
        }
        setSyncLoading();

        try {
            const response = await fetch(syncHealthUrl, { headers: { "Accept": "application/json" } });
            const payload = await response.json().catch(() => ({}));
            if (!response.ok) {
                throw new Error(payload.detail || payload.message || "No fue posible consultar la salud de sincronizacion.");
            }
            renderSyncHealth(payload);
        } catch (error) {
            if (syncSummary) {
                syncSummary.innerHTML = "";
                const badge = document.createElement("span");
                badge.className = "cnc-badge cnc-badge--danger";
                badge.textContent = "Error";
                const text = document.createElement("strong");
                text.textContent = error instanceof Error ? error.message : "Ocurrio un error inesperado.";
                syncSummary.append(badge, text);
            }
            if (syncGrid) {
                syncGrid.innerHTML = "";
                const empty = document.createElement("div");
                empty.className = "cnc-sync-empty";
                empty.textContent = "No se pudieron cargar los cruces.";
                syncGrid.appendChild(empty);
            }
        } finally {
            syncLoading = false;
            if (syncRefreshButton) {
                syncRefreshButton.disabled = false;
            }
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
            const message = row.querySelector("[data-preflight-message]") || detail?.querySelector("[data-preflight-message]");
            if (message) {
                message.textContent = payloadRow.preflightMessage || "Sin validacion pre-Siigo.";
            }

            const sendButton = row.querySelector("[data-cnc-send-siigo]");
            if (sendButton) {
                sendButton.disabled = !canSendPaymentStatus(nextStatus);
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
            if (action === "Aprobado") {
                window.setTimeout(() => window.location.reload(), 550);
            }
        } catch (error) {
            setStatus(error instanceof Error ? error.message : "Ocurrio un error inesperado.", "error");
        } finally {
            buttons.forEach((item) => { item.disabled = false; });
            const sendButton = row.querySelector("[data-cnc-send-siigo]");
            if (sendButton) {
                sendButton.disabled = !canSendPaymentStatus(row.dataset.status || "");
            }
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

        const buttons = Array.from(row.querySelectorAll("[data-cnc-action], [data-cnc-preflight], [data-cnc-dry-run], [data-cnc-send-siigo]"));
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
            renderIssueList(row, "[data-preflight-issues]", payload.issues || []);
            applyPaymentFilters();
            setStatus(
                payload.isReadyForSiigo
                    ? (payload.message || "Validacion pre-Siigo finalizada.")
                    : `${payload.message || "Validacion pre-Siigo finalizada."} Revisa los pendientes visibles en la fila.`,
                payload.isReadyForSiigo ? "success" : "info");
        } catch (error) {
            setStatus(error instanceof Error ? error.message : "Ocurrio un error inesperado.", "error");
        } finally {
            buttons.forEach((item) => { item.disabled = false; });
            const sendButton = row.querySelector("[data-cnc-send-siigo]");
            if (sendButton) {
                sendButton.disabled = !canSendPaymentStatus(row.dataset.status || "");
            }
        }
    };

    const simulatePaymentSiigoDryRun = async (button) => {
        const row = button.closest("tr[data-record-id]");
        if (!row) {
            return;
        }

        const recordId = row.dataset.recordId || "";
        if (!recordId || !dryRunPaymentUrl) {
            setStatus("No se encontro la ruta o el registro para simular.", "error");
            return;
        }

        const buttons = Array.from(row.querySelectorAll("[data-cnc-action], [data-cnc-preflight], [data-cnc-dry-run], [data-cnc-send-siigo]"));
        buttons.forEach((item) => { item.disabled = true; });
        setStatus("Simulando payload de envio a Siigo...", "info");

        try {
            const response = await fetch(dryRunPaymentUrl, {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify({ recordId })
            });
            const payload = await response.json().catch(() => ({}));
            if (!response.ok) {
                throw new Error(payload.detail || payload.message || "No fue posible simular el envio.");
            }

            const message = row.querySelector("[data-siigo-dryrun-message]");
            const preview = row.querySelector("[data-siigo-dryrun-preview]");
            const payloadBox = row.querySelector("[data-siigo-dryrun-payload]");
            const issues = Array.isArray(payload.issues) ? payload.issues : [];

            if (message) {
                message.textContent = issues.length
                    ? `${payload.message || "Simulacion finalizada."} Pendientes abajo.`
                    : (payload.message || "Simulacion finalizada.");
                message.className = payload.isReadyForSiigo ? "cnc-tone-success" : "cnc-tone-warning";
            }
            renderIssueList(row, "[data-siigo-dryrun-issues]", issues);
            if (payloadBox) {
                payloadBox.textContent = payload.payloadJson || "";
            }
            if (preview) {
                preview.hidden = !payload.payloadJson;
            }

            setStatus(payload.message || "Simulacion finalizada.", payload.isReadyForSiigo ? "success" : "info");
        } catch (error) {
            setStatus(error instanceof Error ? error.message : "Ocurrio un error inesperado.", "error");
        } finally {
            buttons.forEach((item) => { item.disabled = false; });
        }
    };

    const sendPaymentToSiigo = async (button) => {
        const row = button.closest("tr[data-record-id]");
        if (!row) {
            return;
        }

        const recordId = row.dataset.recordId || "";
        if (!recordId || !sendPaymentUrl) {
            setStatus("No se encontro la ruta o el registro para enviar a Siigo.", "error");
            return;
        }

        if (!canSendPaymentStatus(row.dataset.status || "")) {
            setStatus("El cruce debe estar Listo Siigo o Error Siigo antes del envio real.", "info");
            return;
        }

        const confirmed = window.confirm("Esto creara un comprobante de ingreso real en Siigo. Revisa que la fila sea la correcta antes de continuar.");
        if (!confirmed) {
            return;
        }

        const buttons = Array.from(row.querySelectorAll("[data-cnc-action], [data-cnc-preflight], [data-cnc-dry-run], [data-cnc-send-siigo]"));
        buttons.forEach((item) => { item.disabled = true; });
        setStatus("Enviando pago real a Siigo...", "info");

        try {
            const response = await fetch(sendPaymentUrl, {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify({ recordId })
            });
            const payload = await response.json().catch(() => ({}));
            if (!response.ok) {
                throw new Error(payload.detail || payload.message || "No fue posible enviar a Siigo.");
            }

            updateRowStatus(row, payload.row, payload.row?.status || row.dataset.status || "");
            renderIssueList(row, "[data-siigo-send-issues]", payload.issues || []);

            const message = row.querySelector("[data-siigo-send-message]");
            const payloadPreview = row.querySelector("[data-siigo-send-payload-preview]");
            const payloadBox = row.querySelector("[data-siigo-send-payload]");
            const preview = row.querySelector("[data-siigo-send-preview]");
            const responseBox = row.querySelector("[data-siigo-send-response]");
            if (message) {
                message.textContent = payload.message || "Envio finalizado.";
                message.className = payload.isSuccess ? "cnc-tone-success" : "cnc-tone-warning";
            }
            if (payloadBox) {
                payloadBox.textContent = payload.payloadJson || "";
            }
            if (payloadPreview) {
                payloadPreview.hidden = !payload.payloadJson;
            }
            if (responseBox) {
                responseBox.textContent = payload.responseJson || "";
            }
            if (preview) {
                preview.hidden = !payload.responseJson;
            }

            setStatus(payload.message || "Envio finalizado.", payload.isSuccess ? "success" : "info");
            if (payload.isSuccess) {
                window.setTimeout(() => window.location.reload(), 900);
            } else {
                buttons.forEach((item) => { item.disabled = false; });
                button.disabled = !canSendPaymentStatus(row.dataset.status || "");
            }
        } catch (error) {
            setStatus(error instanceof Error ? error.message : "Ocurrio un error inesperado.", "error");
            buttons.forEach((item) => { item.disabled = false; });
            button.disabled = !canSendPaymentStatus(row.dataset.status || "");
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

    const closeInvoiceModal = () => {
        if (invoiceModal) {
            invoiceModal.hidden = true;
        }
        activeInvoiceRow = null;
        selectedInvoiceId = "";
    };

    const setSelectedInvoice = (invoice) => {
        selectedInvoiceId = invoice?.recordId || "";
        if (invoiceSelected) {
            invoiceSelected.hidden = !invoice;
            invoiceSelected.textContent = invoice
                ? `Seleccionada: ${invoice.invoiceNumber || "Sin factura"} - ${invoice.clientName || "Sin cliente"} - ${money(invoice.totalInvoice)}`
                : "";
        }
        if (invoiceSave) {
            invoiceSave.disabled = !selectedInvoiceId;
        }
    };

    const renderInvoiceResults = (items) => {
        if (!invoiceResults) {
            return;
        }

        invoiceResults.innerHTML = "";
        if (!Array.isArray(items) || items.length === 0) {
            const empty = document.createElement("small");
            empty.textContent = "No hay resultados con esos criterios.";
            invoiceResults.appendChild(empty);
            setSelectedInvoice(null);
            return;
        }

        items.forEach((invoice) => {
            const button = document.createElement("button");
            button.type = "button";
            button.className = "cnc-invoice-result";
            button.dataset.invoiceId = invoice.recordId || "";
            const title = document.createElement("strong");
            const amount = document.createElement("span");
            const client = document.createElement("small");
            const retentions = document.createElement("small");
            title.textContent = invoice.invoiceNumber || "Sin factura";
            amount.textContent = money(invoice.totalInvoice);
            client.textContent = `${invoice.clientName || "Sin cliente"} - ${invoice.emissionDateDisplay || "Sin fecha"}`;
            retentions.textContent = `Retenciones: ${money((invoice.reteFteValue || 0) + (invoice.reteIcaValue || 0) + (invoice.rteIvaValue || 0))}`;
            title.appendChild(amount);
            button.append(title, client, retentions);
            button.addEventListener("click", () => {
                invoiceResults.querySelectorAll(".cnc-invoice-result").forEach((item) => item.classList.remove("is-selected"));
                button.classList.add("is-selected");
                setSelectedInvoice(invoice);
            });
            invoiceResults.appendChild(button);
        });
    };

    const searchDataverseInvoices = async () => {
        if (!invoiceSearchUrl) {
            setStatus("No se encontro la ruta para buscar facturas.", "error");
            return;
        }

        const query = String(invoiceQuery?.value || "").trim();
        const rawValue = Number(invoiceValue?.value || 0);
        const value = Number.isFinite(rawValue) && rawValue > 0 ? rawValue : null;
        if (!query && !value) {
            setStatus("Busca por cliente, numero de factura o valor.", "info");
            return;
        }

        setSelectedInvoice(null);
        if (invoiceSearchButton) {
            invoiceSearchButton.disabled = true;
        }
        if (invoiceResults) {
            invoiceResults.innerHTML = "<small>Buscando facturas en Dataverse...</small>";
        }

        try {
            const response = await fetch(invoiceSearchUrl, {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify({ query, value, top: 20 })
            });
            const payload = await response.json().catch(() => ({}));
            if (!response.ok) {
                throw new Error(payload.detail || payload.message || "No fue posible buscar facturas.");
            }

            renderInvoiceResults(payload.items || []);
            setStatus(payload.message || "Busqueda finalizada.", "info");
        } catch (error) {
            if (invoiceResults) {
                invoiceResults.innerHTML = "<small>No fue posible buscar facturas.</small>";
            }
            setStatus(error instanceof Error ? error.message : "Ocurrio un error inesperado.", "error");
        } finally {
            if (invoiceSearchButton) {
                invoiceSearchButton.disabled = false;
            }
        }
    };

    const openInvoiceModal = (row) => {
        activeInvoiceRow = row;
        selectedInvoiceId = "";
        const flowInvoice = row.dataset.flowInvoice || "";
        const dataverseInvoice = row.dataset.dataverseInvoice || "";
        const dataverseClient = row.dataset.dataverseClient || "";
        const entryValue = row.dataset.entryValue || "";

        if (invoiceDescription) {
            invoiceDescription.textContent = `${row.dataset.description || "Pago sin descripcion."} Entrada: ${money(Number(entryValue || 0))}`;
        }
        if (invoiceQuery) {
            invoiceQuery.value = flowInvoice || dataverseInvoice || dataverseClient || "";
        }
        if (invoiceValue) {
            invoiceValue.value = entryValue;
        }
        if (invoiceResults) {
            invoiceResults.innerHTML = "<small>Busca por cliente, numero de factura o valor para seleccionar la factura correcta.</small>";
        }
        setSelectedInvoice(null);

        if (invoiceModal) {
            invoiceModal.hidden = false;
        }
    };

    const saveInvoiceAssignment = async () => {
        if (!activeInvoiceRow || !selectedInvoiceId || !invoiceAssignUrl) {
            setStatus("Selecciona una factura para guardar la asignacion.", "info");
            return;
        }

        const recordId = activeInvoiceRow.dataset.recordId || "";
        if (!recordId) {
            setStatus("No se encontro el cruce a actualizar.", "error");
            return;
        }

        if (invoiceSave) {
            invoiceSave.disabled = true;
        }
        setStatus("Guardando asignacion de factura en Dataverse...", "info");

        try {
            const response = await fetch(invoiceAssignUrl, {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify({ recordId, invoiceRecordId: selectedInvoiceId })
            });
            const payload = await response.json().catch(() => ({}));
            if (!response.ok) {
                throw new Error(payload.detail || payload.message || "No fue posible asignar la factura.");
            }

            setStatus(payload.message || "Factura asignada.", "success");
            window.setTimeout(() => window.location.reload(), 650);
        } catch (error) {
            setStatus(error instanceof Error ? error.message : "Ocurrio un error inesperado.", "error");
            if (invoiceSave) {
                invoiceSave.disabled = false;
            }
        }
    };

    const shouldIgnoreRowClick = (target) => Boolean(target.closest("button, a, input, select, textarea, details, summary, label"));

    tabButtons.forEach((button) => {
        button.addEventListener("click", () => {
            setActiveTab(button.dataset.cncTab || "");
            refreshAllFilters();
        });
    });

    syncRefreshButton?.addEventListener("click", () => {
        syncLoaded = false;
        loadSyncHealth(true);
    });

    app.querySelectorAll("[data-cnc-tab-target]").forEach((button) => {
        button.addEventListener("click", (event) => {
            event.stopPropagation();
            setActiveTab(button.dataset.cncTabTarget || "");
            refreshAllFilters();
        });
    });

    [paymentSearch, paymentStatusFilter].forEach((input) => {
        input?.addEventListener("input", applyPaymentFilters);
        input?.addEventListener("change", applyPaymentFilters);
    });

    verticalButtons.forEach((button) => {
        button.addEventListener("click", () => {
            activeVertical = button.dataset.cncVertical || "Cloud";
            refreshAllFilters();
        });
    });

    genericTableSearches.forEach((input) => {
        const key = input.dataset.cncTableSearch || "";
        input.addEventListener("input", () => applyGenericTableFilter(key));
        input.addEventListener("change", () => applyGenericTableFilter(key));
    });

    paymentRowsBody?.querySelectorAll("[data-cnc-action]").forEach((button) => {
        button.addEventListener("click", () => updatePaymentStatus(button));
    });

    paymentRowsBody?.querySelectorAll("[data-cnc-preflight]").forEach((button) => {
        button.addEventListener("click", () => validatePaymentPreflight(button));
    });

    paymentRowsBody?.querySelectorAll("[data-cnc-dry-run]").forEach((button) => {
        button.addEventListener("click", () => simulatePaymentSiigoDryRun(button));
    });

    paymentRowsBody?.querySelectorAll("[data-cnc-send-siigo]").forEach((button) => {
        button.addEventListener("click", () => sendPaymentToSiigo(button));
    });

    app.querySelectorAll("[data-cnc-reassign]").forEach((row) => {
        row.addEventListener("click", (event) => {
            if (shouldIgnoreRowClick(event.target)) {
                return;
            }

            openReassignModal(row);
        });
    });

    app.querySelectorAll("[data-cnc-invoice-assign]").forEach((row) => {
        row.addEventListener("click", (event) => {
            if (shouldIgnoreRowClick(event.target)) {
                return;
            }

            openInvoiceModal(row);
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
    app.querySelectorAll("[data-cnc-close-invoice-modal]").forEach((button) => {
        button.addEventListener("click", closeInvoiceModal);
    });
    invoiceModal?.addEventListener("click", (event) => {
        if (event.target === invoiceModal) {
            closeInvoiceModal();
        }
    });
    invoiceSearchButton?.addEventListener("click", searchDataverseInvoices);
    invoiceSave?.addEventListener("click", saveInvoiceAssignment);
    [invoiceQuery, invoiceValue].forEach((input) => {
        input?.addEventListener("keydown", (event) => {
            if (event.key === "Enter") {
                event.preventDefault();
                searchDataverseInvoices();
            }
        });
    });

    initializeCollapsibleTables();
    setActiveTab(resolveInitialTab(), false);
    refreshAllFilters();
})();
