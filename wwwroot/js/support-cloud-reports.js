(function () {
    const root = document.getElementById("soporteCloudReports");
    if (!root) {
        return;
    }

    const numberFormatter = new Intl.NumberFormat("es-CO", {
        minimumFractionDigits: 0,
        maximumFractionDigits: 0
    });

    const scoreFormatter = new Intl.NumberFormat("es-CO", {
        minimumFractionDigits: 0,
        maximumFractionDigits: 2
    });

    const monthFormatter = new Intl.DateTimeFormat("es-CO", {
        month: "long",
        year: "numeric"
    });

    const urls = {
        clients: root.dataset.clientsUrl || "",
        connected: root.dataset.connectedUrl || "",
        connect: root.dataset.connectUrl || "",
        snapshot: root.dataset.snapshotUrl || "",
        generate: root.dataset.generateUrl || "",
        generatedReports: root.dataset.generatedReportsUrl || "",
        reportDetail: root.dataset.reportDetailUrl || ""
    };

    const els = {
        status: root.querySelector("[data-scr-status]"),
        connectedRows: root.querySelector("[data-scr-connected-rows]"),
        connectedEmpty: root.querySelector("[data-scr-connected-empty]"),
        refreshConnected: root.querySelector("[data-scr-refresh-connected]"),
        openConsent: root.querySelector("[data-scr-open-consent]"),
        consentModal: root.querySelector("[data-scr-consent-modal]"),
        closeConsent: root.querySelectorAll("[data-scr-close-consent]"),
        consentStatus: root.querySelector("[data-scr-consent-status]"),
        consentClient: root.querySelector("[data-scr-consent-client]"),
        tenant: root.querySelector("[data-scr-tenant]"),
        connect: root.querySelector("[data-scr-connect]"),
        consentCard: root.querySelector("[data-scr-consent-card]"),
        consentLink: root.querySelector("[data-scr-consent-link]"),
        consentUrl: root.querySelector("[data-scr-consent-url]"),
        permissions: root.querySelector("[data-scr-permissions]"),
        month: root.querySelector("[data-scr-month]"),
        generate: root.querySelector("[data-scr-generate]"),
        selectionMeta: root.querySelector("[data-scr-selection-meta]"),
        totalConnected: root.querySelector("[data-scr-total-connected]"),
        selectedMonth: root.querySelector("[data-scr-selected-month]"),
        connectionState: root.querySelector("[data-scr-connection-state]"),
        reportState: root.querySelector("[data-scr-report-state]"),
        secureScore: root.querySelector("[data-scr-secure-score]"),
        alertsHigh: root.querySelector("[data-scr-alerts-high]"),
        incidentsActive: root.querySelector("[data-scr-incidents-active]"),
        snapshotSummary: root.querySelector("[data-scr-snapshot-summary]"),
        progressCard: root.querySelector("[data-scr-progress-card]"),
        progressMessage: root.querySelector("[data-scr-progress-message]"),
        progressBar: root.querySelector("[data-scr-progress-bar]"),
        progressSteps: root.querySelector("[data-scr-progress-steps]"),
        reportCard: root.querySelector("[data-scr-report-card]"),
        reportFrame: root.querySelector("[data-scr-report-frame]"),
        reportId: root.querySelector("[data-scr-report-id]"),
        openReport: root.querySelector("[data-scr-open-report]"),
        historyMonth: root.querySelector("[data-scr-history-month]"),
        historyYear: root.querySelector("[data-scr-history-year]"),
        loadHistory: root.querySelector("[data-scr-load-history]"),
        historyRows: root.querySelector("[data-scr-history-rows]"),
        historyEmpty: root.querySelector("[data-scr-history-empty]"),
        loadingModal: root.querySelector("[data-scr-loading-modal]"),
        loadingMessage: root.querySelector("[data-scr-loading-message]")
    };

    const state = {
        clients: [],
        connected: [],
        clientsLoaded: false,
        connectedLoaded: false,
        selectedConnectionId: "",
        reportHtml: "",
        reportBlobUrl: "",
        busy: false
    };

    populateMonthOptions();
    populateHistoryFilters();
    updateSelectionMeta();
    renderProgress("idle");

    const moduleRoot = document.getElementById("soporteCloudModuleShell");
    moduleRoot?.addEventListener("supportcloud:modulechange", event => {
        if (event.detail?.activeKey === "reportes") {
            loadInitialData();
        }
    });

    const panel = root.closest("[data-scs-module-panel]");
    if (!panel || !panel.hidden) {
        loadInitialData();
    }

    els.refreshConnected?.addEventListener("click", () => loadConnectedClients({ force: true }));
    els.openConsent?.addEventListener("click", openConsentModal);
    els.closeConsent?.forEach(element => element.addEventListener("click", closeConsentModal));
    els.month?.addEventListener("change", () => {
        clearGeneratedReport();
        updateSelectionMeta();
    });

    els.connectedRows?.addEventListener("click", event => {
        const button = event.target.closest("[data-scr-select-connection]");
        if (!button) {
            return;
        }

        selectConnection(button.dataset.scrSelectConnection || "");
    });

    els.connect?.addEventListener("click", async () => {
        const clienteId = els.consentClient?.value || "";
        if (!clienteId) {
            setConsentStatus("error", "Selecciona un cliente para generar el consentimiento.");
            return;
        }

        setBusy(true);
        setConsentStatus("info", "Generando URL de consentimiento Microsoft...");
        try {
            const result = await fetchJson(urls.connect, {
                method: "POST",
                body: JSON.stringify({
                    clienteId,
                    tenantIdOrDomain: els.tenant?.value || ""
                })
            });

            renderConsent(result);
            setConsentStatus("success", "URL de consentimiento generada. Abre Microsoft para completar el permiso.");
        } catch (error) {
            setConsentStatus("error", buildErrorMessage(error));
        } finally {
            setBusy(false);
        }
    });

    els.generate?.addEventListener("click", generateSelectedReport);
    els.openReport?.addEventListener("click", openCurrentReport);
    els.loadHistory?.addEventListener("click", () => loadGeneratedReports());

    els.historyRows?.addEventListener("click", async event => {
        const button = event.target.closest("[data-scr-open-generated]");
        if (!button) {
            return;
        }

        await openGeneratedReport(button.dataset.scrOpenGenerated || "");
    });

    async function loadInitialData() {
        await Promise.all([
            loadClientsOnce(),
            loadConnectedClients(),
            loadGeneratedReports()
        ]);
    }

    async function loadClientsOnce() {
        if (state.clientsLoaded || !urls.clients) {
            return;
        }

        try {
            const items = await fetchJson(urls.clients);
            state.clients = Array.isArray(items)
                ? items
                    .filter(item => item?.id && item?.name)
                    .sort((a, b) => String(a.name || "").localeCompare(String(b.name || ""), "es"))
                : [];
            state.clientsLoaded = true;
            renderConsentClients();
        } catch (error) {
            setConsentStatus("error", buildErrorMessage(error));
            renderConsentClients();
        }
    }

    async function loadConnectedClients(options = {}) {
        if (state.busy && !options.force) {
            return;
        }

        setStatus("info", "Cargando clientes con consentimiento activo...");
        try {
            const items = await fetchJson(urls.connected);
            state.connected = Array.isArray(items)
                ? items
                    .filter(item => item?.clienteId)
                    .sort((a, b) => getClientLabel(a).localeCompare(getClientLabel(b), "es"))
                : [];
            state.connectedLoaded = true;
            renderConnectedClients();
            setStatus("success", state.connected.length
                ? "Clientes conectados cargados."
                : "No hay clientes con consentimiento activo.");
        } catch (error) {
            setStatus("error", buildErrorMessage(error));
            renderConnectedClients();
        }
    }

    function renderConsentClients() {
        if (!els.consentClient) {
            return;
        }

        els.consentClient.innerHTML = `
            <option value="">Selecciona un cliente...</option>
            ${state.clients.map(item => `
                <option value="${escapeHtml(item.id)}">${escapeHtml(item.name)}</option>
            `).join("")}
        `;
    }

    function renderConnectedClients() {
        if (els.totalConnected) {
            els.totalConnected.textContent = numberFormatter.format(state.connected.length);
        }

        if (!els.connectedRows) {
            return;
        }

        if (state.connected.length === 0) {
            els.connectedRows.innerHTML = "";
            if (els.connectedEmpty) {
                els.connectedEmpty.hidden = false;
            }
            selectConnection("");
            return;
        }

        if (els.connectedEmpty) {
            els.connectedEmpty.hidden = true;
        }

        els.connectedRows.innerHTML = state.connected.map(item => {
            const isSelected = getConnectionKey(item) === state.selectedConnectionId;
            return `
                <tr class="support-cloud-table__row ${isSelected ? "is-selected" : ""}">
                    <td data-label="Cliente">
                        <strong>${escapeHtml(getClientLabel(item))}</strong>
                    </td>
                    <td data-label="Tenant">
                        <span class="support-cloud-table__muted">${escapeHtml(item.tenantId || item.tenantHint || "-")}</span>
                    </td>
                    <td data-label="Estado">
                        <span class="support-cloud-pill">${escapeHtml(item.estadoConexion || "Conectado")}</span>
                    </td>
                    <td data-label="Consentimiento">${escapeHtml(formatDateTime(item.fechaConexion))}</td>
                    <td data-label="Permisos">
                        <span class="support-cloud-table__muted">${escapeHtml(compactPermissionText(item.permisosSolicitados))}</span>
                    </td>
                    <td data-label="Accion" class="text-end">
                        <button type="button" class="btn btn-sm ${isSelected ? "btn-primary" : "btn-outline-primary"}" data-scr-select-connection="${escapeHtml(getConnectionKey(item))}">
                            ${isSelected ? "Seleccionado" : "Seleccionar"}
                        </button>
                    </td>
                </tr>
            `;
        }).join("");

        if (!state.selectedConnectionId && state.connected.length === 1) {
            selectConnection(getConnectionKey(state.connected[0]));
        } else {
            updateSelectionMeta();
        }
    }

    function selectConnection(connectionId) {
        state.selectedConnectionId = connectionId || "";
        clearGeneratedReport();
        renderConnectedClients();
        updateSelectionMeta();
    }

    async function generateSelectedReport() {
        const connection = getSelectedConnection();
        const periodo = els.month?.value || "";
        if (!connection) {
            setStatus("error", "Selecciona un cliente conectado para generar el informe.");
            return;
        }

        setBusy(true);
        showLoadingModal("Recolectando datos Microsoft 365 antes de generar el informe...");
        clearGeneratedReport();
        setReportState("Recolectando");
        renderProgress("collecting");
        setStatus("info", "Recolectando datos Microsoft 365 antes de generar el informe...");

        try {
            const snapshot = await fetchJson(urls.snapshot, {
                method: "POST",
                body: JSON.stringify({
                    clienteId: connection.clienteId,
                    tenantId: connection.tenantId || connection.tenantHint || "",
                    periodo
                })
            });
            renderSnapshot(snapshot);

            renderProgress("generating", snapshot?.success
                ? "Snapshot recolectado. Generando informe HTML con Azure OpenAI..."
                : "Snapshot guardado con advertencias. Generando informe con la evidencia disponible...");
            showLoadingModal(snapshot?.success
                ? "Snapshot recolectado. Generando informe HTML con Azure OpenAI..."
                : "Snapshot guardado con advertencias. Generando informe con la evidencia disponible...");
            setReportState("Generando");

            const result = await fetchJson(urls.generate, {
                method: "POST",
                body: JSON.stringify({
                    clienteId: connection.clienteId,
                    periodo
                })
            });

            renderGeneratedReport(result);
            renderProgress("done");
            const isError = String(result?.estado || "").toLowerCase() === "error";
            setStatus(
                isError ? "error" : "success",
                isError
                    ? (result?.error || "No fue posible generar el informe.")
                    : "Informe HTML generado correctamente.");

            await loadGeneratedReports();
        } catch (error) {
            setReportState("Error");
            renderProgress("error", buildErrorMessage(error));
            renderGeneratedReport({
                idReporte: "",
                html: "",
                estado: "Error",
                error: buildErrorMessage(error)
            });
            setStatus("error", buildErrorMessage(error));
        } finally {
            hideLoadingModal();
            setBusy(false);
        }
    }

    async function loadGeneratedReports() {
        if (!urls.generatedReports || !els.historyRows) {
            return;
        }

        const periodo = getHistoryPeriod();
        try {
            const items = await fetchJson(`${urls.generatedReports}?periodo=${encodeURIComponent(periodo)}`);
            renderGeneratedReports(Array.isArray(items) ? items : []);
        } catch (error) {
            setStatus("error", buildErrorMessage(error));
            renderGeneratedReports([]);
        }
    }

    function renderGeneratedReports(items) {
        if (!els.historyRows) {
            return;
        }

        if (items.length === 0) {
            els.historyRows.innerHTML = "";
            if (els.historyEmpty) {
                els.historyEmpty.hidden = false;
            }
            return;
        }

        if (els.historyEmpty) {
            els.historyEmpty.hidden = true;
        }

        els.historyRows.innerHTML = items.map(item => `
            <tr>
                <td data-label="Cliente">
                    <strong>${escapeHtml(item.clienteNombre || item.clienteId || "Cliente")}</strong>
                </td>
                <td data-label="Periodo">${escapeHtml(item.periodo || "-")}</td>
                <td data-label="Estado"><span class="support-cloud-pill">${escapeHtml(item.estado || "-")}</span></td>
                <td data-label="Generado">${escapeHtml(formatDateTime(item.fechaGeneracion))}</td>
                <td data-label="Accion" class="text-end">
                    <button type="button" class="btn btn-sm btn-outline-primary" data-scr-open-generated="${escapeHtml(item.idReporte || "")}">
                        Consultar
                    </button>
                </td>
            </tr>
        `).join("");
    }

    async function openGeneratedReport(idReporte) {
        if (!idReporte) {
            setStatus("error", "No se recibio el id del informe.");
            return;
        }

        setBusy(true);
        setStatus("info", "Cargando informe generado...");
        try {
            const result = await fetchJson(`${urls.reportDetail}/${encodeURIComponent(idReporte)}`);
            renderGeneratedReport(result);
            setStatus("success", "Informe cargado desde Dataverse.");
        } catch (error) {
            setStatus("error", buildErrorMessage(error));
        } finally {
            setBusy(false);
        }
    }

    function renderConsent(result) {
        const url = result?.url || "";
        if (!url) {
            throw new Error("La respuesta no incluyo URL de consentimiento.");
        }

        if (els.consentCard) {
            els.consentCard.hidden = false;
        }
        if (els.consentLink) {
            els.consentLink.href = url;
        }
        if (els.consentUrl) {
            els.consentUrl.value = url;
        }

        const permissions = Array.isArray(result?.requestedPermissions)
            ? result.requestedPermissions
            : [];
        if (els.permissions) {
            els.permissions.innerHTML = permissions.length
                ? permissions.map(permission => `
                    <div class="support-cloud-breakdown__row">
                        <div class="support-cloud-breakdown__head">
                            <span class="support-cloud-breakdown__label">${escapeHtml(permission)}</span>
                            <span class="support-cloud-breakdown__value">App permission</span>
                        </div>
                        <div class="support-cloud-breakdown__track">
                            <span class="support-cloud-breakdown__fill" style="width: 100%"></span>
                        </div>
                    </div>
                `).join("")
                : '<div class="support-cloud-placeholder support-cloud-placeholder--compact">Permisos definidos por la App Registration.</div>';
        }
    }

    function renderSnapshot(result) {
        const currentScore = Number(result?.secureScoreActual || 0);
        const maxScore = Number(result?.secureScoreMaximo || 0);
        const highAlerts = Number(result?.alertasHigh || 0);
        const mediumAlerts = Number(result?.alertasMedium || 0);
        const lowAlerts = Number(result?.alertasLow || 0);
        const activeIncidents = Number(result?.incidentesActivos || 0);
        const resolvedIncidents = Number(result?.incidentesResueltos || 0);
        const scoreLabel = maxScore > 0
            ? `${scoreFormatter.format(currentScore)} / ${scoreFormatter.format(maxScore)}`
            : scoreFormatter.format(currentScore);
        const scoreWidth = maxScore > 0
            ? Math.max(0, Math.min(100, (currentScore / maxScore) * 100))
            : 0;
        const totalAlerts = highAlerts + mediumAlerts + lowAlerts;
        const totalIncidents = activeIncidents + resolvedIncidents;

        if (els.secureScore) {
            els.secureScore.textContent = scoreLabel || "-";
        }
        if (els.alertsHigh) {
            els.alertsHigh.textContent = numberFormatter.format(highAlerts);
        }
        if (els.incidentsActive) {
            els.incidentsActive.textContent = numberFormatter.format(activeIncidents);
        }
        if (els.connectionState) {
            els.connectionState.textContent = result?.estadoConsulta || (result?.success ? "Snapshot recolectado" : "Error snapshot");
        }
        if (!els.snapshotSummary) {
            return;
        }

        els.snapshotSummary.innerHTML = `
            ${renderMetricRow("Secure Score", scoreLabel, scoreWidth)}
            ${renderMetricRow("Alertas high", numberFormatter.format(highAlerts), totalAlerts ? (highAlerts * 100) / totalAlerts : 0)}
            ${renderMetricRow("Alertas medium", numberFormatter.format(mediumAlerts), totalAlerts ? (mediumAlerts * 100) / totalAlerts : 0)}
            ${renderMetricRow("Alertas low", numberFormatter.format(lowAlerts), totalAlerts ? (lowAlerts * 100) / totalAlerts : 0)}
            ${renderMetricRow("Incidentes activos", numberFormatter.format(activeIncidents), totalIncidents ? (activeIncidents * 100) / totalIncidents : 0)}
            ${renderMetricRow("Incidentes resueltos", numberFormatter.format(resolvedIncidents), totalIncidents ? (resolvedIncidents * 100) / totalIncidents : 0)}
            <div class="support-cloud-breakdown__row">
                <div class="support-cloud-breakdown__head">
                    <span class="support-cloud-breakdown__label">${escapeHtml(result?.periodo || "")}</span>
                    <span class="support-cloud-breakdown__value">${escapeHtml(result?.estadoConsulta || "")}</span>
                </div>
            </div>
            ${result?.errorConsulta ? `
                <div class="support-cloud-breakdown__row">
                    <div class="support-cloud-breakdown__head">
                        <span class="support-cloud-breakdown__label">Error Graph</span>
                        <span class="support-cloud-breakdown__value">${escapeHtml(result.errorConsulta)}</span>
                    </div>
                </div>
            ` : ""}
        `;
    }

    function renderGeneratedReport(result) {
        const html = result?.html || "";
        const estado = result?.estado || (html ? "Generado" : "Error");
        const idReporte = result?.idReporte || "";
        state.reportHtml = html;

        if (els.reportCard) {
            els.reportCard.hidden = false;
        }
        if (els.reportFrame) {
            els.reportFrame.srcdoc = html || buildReportErrorHtml(result?.error || "No se recibio HTML generado.");
        }
        if (els.reportId) {
            els.reportId.textContent = idReporte || "-";
        }
        if (els.openReport) {
            els.openReport.disabled = !html;
        }

        setReportState(estado);
    }

    function clearGeneratedReport() {
        state.reportHtml = "";
        if (state.reportBlobUrl) {
            URL.revokeObjectURL(state.reportBlobUrl);
            state.reportBlobUrl = "";
        }
        if (els.reportCard) {
            els.reportCard.hidden = true;
        }
        if (els.reportFrame) {
            els.reportFrame.removeAttribute("srcdoc");
        }
        if (els.reportId) {
            els.reportId.textContent = "-";
        }
        if (els.openReport) {
            els.openReport.disabled = true;
        }
        setReportState("Sin generar");
    }

    function openCurrentReport() {
        if (!state.reportHtml) {
            setStatus("error", "No hay HTML generado para abrir.");
            return;
        }

        if (state.reportBlobUrl) {
            URL.revokeObjectURL(state.reportBlobUrl);
        }

        const blob = new Blob([state.reportHtml], { type: "text/html;charset=utf-8" });
        state.reportBlobUrl = URL.createObjectURL(blob);
        window.open(state.reportBlobUrl, "_blank", "noopener");
    }

    function openConsentModal() {
        if (els.consentModal) {
            els.consentModal.hidden = false;
            document.body.classList.add("support-cloud-modal-open");
        }
        loadClientsOnce();
        setConsentStatus("", "");
    }

    function closeConsentModal() {
        if (els.consentModal) {
            els.consentModal.hidden = true;
            document.body.classList.remove("support-cloud-modal-open");
        }
        loadConnectedClients({ force: true });
    }

    function showLoadingModal(message) {
        if (els.loadingMessage) {
            els.loadingMessage.textContent = message || "Procesando la solicitud. Este proceso puede tardar unos minutos.";
        }
        if (els.loadingModal) {
            els.loadingModal.hidden = false;
            document.body.classList.add("support-cloud-modal-open");
        }
    }

    function hideLoadingModal() {
        if (els.loadingModal) {
            els.loadingModal.hidden = true;
            document.body.classList.remove("support-cloud-modal-open");
        }
    }

    function populateMonthOptions() {
        if (!els.month) {
            return;
        }

        const months = buildRecentMonths(24);
        els.month.innerHTML = months.map((item, index) => `
            <option value="${escapeHtml(item.value)}" ${index === 0 ? "selected" : ""}>${escapeHtml(item.label)}</option>
        `).join("");
    }

    function populateHistoryFilters() {
        const now = new Date();
        if (els.historyMonth) {
            els.historyMonth.innerHTML = Array.from({ length: 12 }, (_, index) => {
                const value = String(index + 1).padStart(2, "0");
                const date = new Date(now.getFullYear(), index, 1);
                return `<option value="${value}" ${index === now.getMonth() ? "selected" : ""}>${escapeHtml(capitalizeFirst(new Intl.DateTimeFormat("es-CO", { month: "long" }).format(date)))}</option>`;
            }).join("");
        }

        if (els.historyYear) {
            const years = [];
            for (let year = now.getFullYear(); year >= now.getFullYear() - 5; year -= 1) {
                years.push(year);
            }

            els.historyYear.innerHTML = years.map(year => `
                <option value="${year}">${year}</option>
            `).join("");
        }
    }

    function buildRecentMonths(count) {
        const now = new Date();
        const current = new Date(now.getFullYear(), now.getMonth(), 1);
        const months = [];
        for (let offset = 0; offset < count; offset += 1) {
            const date = new Date(current.getFullYear(), current.getMonth() - offset, 1);
            months.push({
                value: `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, "0")}`,
                label: capitalizeFirst(monthFormatter.format(date))
            });
        }

        return months;
    }

    function getHistoryPeriod() {
        const year = els.historyYear?.value || new Date().getFullYear();
        const month = els.historyMonth?.value || String(new Date().getMonth() + 1).padStart(2, "0");
        return `${year}-${month}`;
    }

    function updateSelectionMeta() {
        const connection = getSelectedConnection();
        const selectedMonthLabel = els.month?.selectedOptions?.[0]?.textContent || "-";

        if (els.selectionMeta) {
            els.selectionMeta.textContent = connection
                ? `${getClientLabel(connection)} · ${selectedMonthLabel}`
                : "Selecciona un cliente conectado";
        }

        if (els.selectedMonth) {
            els.selectedMonth.textContent = selectedMonthLabel;
        }

        if (els.connectionState) {
            els.connectionState.textContent = connection?.estadoConexion || "Sin seleccion";
        }

        if (els.generate) {
            els.generate.disabled = state.busy || !connection;
        }
    }

    function renderProgress(status, customMessage) {
        const config = {
            idle: { visible: false, width: 0, active: -1, message: "" },
            collecting: { visible: true, width: 35, active: 0, message: "Recolectando Secure Score, alertas e incidentes desde Microsoft Graph..." },
            generating: { visible: true, width: 72, active: 1, message: customMessage || "Generando HTML ejecutivo con Azure OpenAI..." },
            done: { visible: true, width: 100, active: 2, message: "Informe generado y guardado en Dataverse." },
            error: { visible: true, width: 100, active: 3, message: customMessage || "La generacion no pudo completarse." }
        }[status] || { visible: false, width: 0, active: -1, message: "" };

        if (els.progressCard) {
            els.progressCard.hidden = !config.visible;
        }
        if (els.progressMessage) {
            els.progressMessage.textContent = config.message;
        }
        if (els.progressBar) {
            els.progressBar.style.width = `${config.width}%`;
        }
        if (els.progressSteps) {
            const steps = [
                "Recolectar datos M365",
                "Generar informe HTML",
                "Guardar resultado",
                "Revisar error"
            ];
            els.progressSteps.innerHTML = steps.map((step, index) => `
                <li class="${index < config.active ? "is-done" : ""} ${index === config.active ? "is-active" : ""}">
                    ${escapeHtml(step)}
                </li>
            `).join("");
        }
    }

    function renderMetricRow(label, value, width) {
        const normalizedWidth = Math.max(0, Math.min(100, Number(width || 0)));
        return `
            <div class="support-cloud-breakdown__row">
                <div class="support-cloud-breakdown__head">
                    <span class="support-cloud-breakdown__label">${escapeHtml(label)}</span>
                    <span class="support-cloud-breakdown__value">${escapeHtml(value)}</span>
                </div>
                <div class="support-cloud-breakdown__track">
                    <span class="support-cloud-breakdown__fill" style="width:${normalizedWidth}%"></span>
                </div>
            </div>
        `;
    }

    function buildReportErrorHtml(message) {
        return `<!DOCTYPE html>
<html lang="es">
<head>
    <meta charset="utf-8">
    <title>Informe no generado</title>
    <style>
        body { margin: 0; font-family: Arial, sans-serif; color: #17263c; background: #f7fbff; }
        main { padding: 32px; }
        h1 { margin: 0 0 12px; font-size: 24px; }
        p { margin: 0; color: #5f7088; line-height: 1.5; }
    </style>
</head>
<body>
    <main>
        <h1>Informe no generado</h1>
        <p>${escapeHtml(message)}</p>
    </main>
</body>
</html>`;
    }

    function setBusy(isBusy) {
        state.busy = isBusy;
        [
            els.refreshConnected,
            els.openConsent,
            els.consentClient,
            els.tenant,
            els.connect,
            els.month,
            els.generate,
            els.openReport,
            els.historyMonth,
            els.historyYear,
            els.loadHistory
        ].forEach(element => {
            if (element) {
                element.disabled = isBusy || (element === els.openReport && !state.reportHtml) || (element === els.generate && !getSelectedConnection());
            }
        });

        root.querySelectorAll("[data-scr-select-connection], [data-scr-open-generated]").forEach(button => {
            button.disabled = isBusy;
        });
    }

    function setReportState(value) {
        if (els.reportState) {
            els.reportState.textContent = value || "Sin generar";
        }
    }

    function setStatus(type, message) {
        setStatusElement(els.status, type, message);
    }

    function setConsentStatus(type, message) {
        setStatusElement(els.consentStatus, type, message);
    }

    function setStatusElement(element, type, message) {
        if (!element) {
            return;
        }

        if (!message) {
            element.className = "support-cloud-status";
            element.textContent = "";
            return;
        }

        element.className = `support-cloud-status is-visible is-${type}`;
        element.textContent = message;
    }

    async function fetchJson(url, options = {}) {
        const headers = {
            Accept: "application/json",
            ...(options.headers || {})
        };

        if (options.body && !headers["Content-Type"]) {
            headers["Content-Type"] = "application/json";
        }

        const response = await fetch(url, {
            method: options.method || "GET",
            headers,
            body: options.body
        });

        const contentType = response.headers.get("content-type") || "";
        const rawBody = await response.text();
        if (!response.ok) {
            let message = rawBody;
            if (contentType.includes("application/json")) {
                try {
                    const payload = rawBody ? JSON.parse(rawBody) : null;
                    const baseMessage = typeof payload === "string"
                        ? payload
                        : payload?.message || payload?.title || rawBody;
                    const detail = typeof payload === "object" && payload?.detail && payload.detail !== baseMessage
                        ? ` ${payload.detail}`
                        : "";
                    message = `${baseMessage || ""}${detail}`.trim();
                } catch {
                    message = rawBody;
                }
            } else if (isHtmlGatewayError(response.status, rawBody)) {
                message = "El App Service corto la generacion del informe por tiempo de espera. Vuelve a intentarlo; si persiste, revisa el estado guardado del reporte en Dataverse.";
            } else if (rawBody && rawBody.trimStart().startsWith("<")) {
                message = stripHtml(rawBody) || `El servidor devolvio un error HTTP ${response.status}.`;
            }

            throw new Error(message || "No fue posible completar la solicitud.");
        }

        if (!contentType.includes("application/json")) {
            throw new Error(rawBody || "La respuesta del servidor no fue valida.");
        }

        return rawBody ? JSON.parse(rawBody) : null;
    }

    function getSelectedConnection() {
        return state.connected.find(item => getConnectionKey(item) === state.selectedConnectionId) || null;
    }

    function getConnectionKey(item) {
        return item?.connectionId || `${item?.clienteId || ""}|${item?.tenantId || item?.tenantHint || ""}`;
    }

    function getClientLabel(item) {
        return item?.clienteNombre || item?.name || item?.clienteId || "Cliente";
    }

    function compactPermissionText(value) {
        const text = String(value || "").replace(/^Scopes:\s*/i, "").replace(/\s*\|\s*Permisos:\s*/i, " · ");
        return text.length > 120 ? `${text.slice(0, 117)}...` : (text || "-");
    }

    function formatDateTime(value) {
        if (!value) {
            return "-";
        }

        const date = new Date(value);
        if (Number.isNaN(date.getTime())) {
            return value;
        }

        return new Intl.DateTimeFormat("es-CO", {
            year: "numeric",
            month: "2-digit",
            day: "2-digit",
            hour: "2-digit",
            minute: "2-digit"
        }).format(date);
    }

    function isHtmlGatewayError(status, body) {
        const text = body || "";
        return (status === 502 || status === 503 || status === 504)
            && (text.includes("<html") || text.includes("<!DOCTYPE"))
            && (text.includes("gateway") || text.includes("proxy server") || text.includes("Server Error"));
    }

    function stripHtml(value) {
        const element = document.createElement("div");
        element.innerHTML = value;
        return (element.textContent || element.innerText || "").replace(/\s+/g, " ").trim();
    }

    function buildErrorMessage(error) {
        return error instanceof Error
            ? error.message
            : "Ocurrio un error inesperado.";
    }

    function capitalizeFirst(value) {
        const text = String(value || "").trim();
        return text ? `${text.slice(0, 1).toUpperCase()}${text.slice(1)}` : "";
    }

    function escapeHtml(value) {
        return String(value ?? "")
            .replaceAll("&", "&amp;")
            .replaceAll("<", "&lt;")
            .replaceAll(">", "&gt;")
            .replaceAll('"', "&quot;")
            .replaceAll("'", "&#39;");
    }
})();
