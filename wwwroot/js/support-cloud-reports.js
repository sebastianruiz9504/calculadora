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
        connect: root.dataset.connectUrl || "",
        test: root.dataset.testUrl || "",
        snapshot: root.dataset.snapshotUrl || ""
    };

    const els = {
        status: root.querySelector("[data-scr-status]"),
        client: root.querySelector("[data-scr-client]"),
        month: root.querySelector("[data-scr-month]"),
        tenant: root.querySelector("[data-scr-tenant]"),
        connect: root.querySelector("[data-scr-connect]"),
        test: root.querySelector("[data-scr-test]"),
        collect: root.querySelector("[data-scr-collect]"),
        selectionMeta: root.querySelector("[data-scr-selection-meta]"),
        totalClients: root.querySelector("[data-scr-total-clients]"),
        selectedMonth: root.querySelector("[data-scr-selected-month]"),
        connectionState: root.querySelector("[data-scr-connection-state]"),
        secureScore: root.querySelector("[data-scr-secure-score]"),
        alertsHigh: root.querySelector("[data-scr-alerts-high]"),
        incidentsActive: root.querySelector("[data-scr-incidents-active]"),
        consentCard: root.querySelector("[data-scr-consent-card]"),
        consentLink: root.querySelector("[data-scr-consent-link]"),
        consentUrl: root.querySelector("[data-scr-consent-url]"),
        permissions: root.querySelector("[data-scr-permissions]"),
        snapshotSummary: root.querySelector("[data-scr-snapshot-summary]")
    };

    const state = {
        clients: [],
        loaded: false,
        busy: false
    };

    populateMonthOptions();
    updateSelectionMeta();

    const moduleRoot = document.getElementById("soporteCloudModuleShell");
    moduleRoot?.addEventListener("supportcloud:modulechange", event => {
        if (event.detail?.activeKey === "reportes") {
            loadClientsOnce();
        }
    });

    const panel = root.closest("[data-scs-module-panel]");
    if (!panel || !panel.hidden) {
        loadClientsOnce();
    }

    els.client?.addEventListener("change", updateSelectionMeta);
    els.month?.addEventListener("change", updateSelectionMeta);
    els.tenant?.addEventListener("input", updateSelectionMeta);

    els.connect?.addEventListener("click", async () => {
        const clienteId = els.client?.value || "";
        if (!clienteId) {
            setStatus("error", "Selecciona un cliente para generar el consentimiento.");
            return;
        }

        setBusy(true);
        setStatus("info", "Generando URL de consentimiento Microsoft...");
        try {
            const result = await fetchJson(urls.connect, {
                method: "POST",
                body: JSON.stringify({
                    clienteId,
                    tenantIdOrDomain: els.tenant?.value || ""
                })
            });

            renderConsent(result);
            setStatus("success", "URL de consentimiento generada.");
        } catch (error) {
            setStatus("error", buildErrorMessage(error));
        } finally {
            setBusy(false);
        }
    });

    els.test?.addEventListener("click", async () => {
        const clienteId = els.client?.value || "";
        if (!clienteId && !(els.tenant?.value || "").trim()) {
            setStatus("error", "Selecciona un cliente o indica un tenant para probar la conexion.");
            return;
        }

        setBusy(true);
        setStatus("info", "Probando conexion con Microsoft Graph...");
        try {
            const result = await fetchJson(urls.test, {
                method: "POST",
                body: JSON.stringify({
                    clienteId,
                    tenantId: els.tenant?.value || ""
                })
            });

            if (els.connectionState) {
                els.connectionState.textContent = result?.estadoConexion || (result?.success ? "Conexion probada" : "Error prueba");
            }

            setStatus(result?.success ? "success" : "error", result?.message || "Prueba finalizada.");
        } catch (error) {
            if (els.connectionState) {
                els.connectionState.textContent = "Error prueba";
            }
            setStatus("error", buildErrorMessage(error));
        } finally {
            setBusy(false);
        }
    });

    els.collect?.addEventListener("click", async () => {
        const clienteId = els.client?.value || "";
        const periodo = els.month?.value || "";
        if (!clienteId) {
            setStatus("error", "Selecciona un cliente para recolectar el snapshot mensual.");
            return;
        }

        setBusy(true);
        setStatus("info", "Recolectando datos de seguridad Microsoft 365...");
        try {
            const result = await fetchJson(urls.snapshot, {
                method: "POST",
                body: JSON.stringify({
                    clienteId,
                    tenantId: els.tenant?.value || "",
                    periodo
                })
            });

            renderSnapshot(result);
            setStatus(
                result?.success ? "success" : "error",
                result?.success
                    ? (result?.message || "Recoleccion finalizada.")
                    : (result?.errorConsulta || result?.message || "La consulta a Microsoft Graph fallo."));
        } catch (error) {
            setStatus("error", buildErrorMessage(error));
        } finally {
            setBusy(false);
        }
    });

    async function loadClientsOnce() {
        if (state.loaded || state.busy || !urls.clients) {
            return;
        }

        setBusy(true);
        setStatus("info", "Cargando clientes...");
        try {
            const items = await fetchJson(urls.clients);
            state.clients = Array.isArray(items)
                ? items
                    .filter(item => item?.id && item?.name)
                    .sort((a, b) => String(a.name || "").localeCompare(String(b.name || ""), "es"))
                : [];
            state.loaded = true;
            renderClients();
            setStatus("success", state.clients.length
                ? "Clientes cargados para reportes."
                : "No se encontraron clientes.");
        } catch (error) {
            setStatus("error", buildErrorMessage(error));
            renderClients();
        } finally {
            setBusy(false);
        }
    }

    function renderClients() {
        if (!els.client) {
            return;
        }

        els.client.innerHTML = `
            <option value="">Selecciona un cliente...</option>
            ${state.clients.map(item => `
                <option value="${escapeHtml(item.id)}">${escapeHtml(item.name)}</option>
            `).join("")}
        `;

        if (els.totalClients) {
            els.totalClients.textContent = numberFormatter.format(state.clients.length);
        }

        updateSelectionMeta();
    }

    function populateMonthOptions() {
        if (!els.month) {
            return;
        }

        const now = new Date();
        const current = new Date(now.getFullYear(), now.getMonth(), 1);
        const months = [];
        for (let offset = 0; offset < 24; offset += 1) {
            const date = new Date(current.getFullYear(), current.getMonth() - offset, 1);
            const value = `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, "0")}`;
            months.push({
                value,
                label: monthFormatter.format(date)
            });
        }

        els.month.innerHTML = months.map((item, index) => `
            <option value="${escapeHtml(item.value)}" ${index === 0 ? "selected" : ""}>${escapeHtml(capitalizeFirst(item.label))}</option>
        `).join("");
    }

    function updateSelectionMeta() {
        const selectedClient = getSelectedClientName();
        const selectedMonthLabel = els.month?.selectedOptions?.[0]?.textContent || "-";
        const tenant = (els.tenant?.value || "").trim();

        if (els.selectionMeta) {
            els.selectionMeta.textContent = selectedClient
                ? `${selectedClient} · ${selectedMonthLabel}${tenant ? ` · ${tenant}` : ""}`
                : "Selecciona un cliente";
        }

        if (els.selectedMonth) {
            els.selectedMonth.textContent = selectedMonthLabel;
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
            els.connectionState.textContent = result?.estadoConsulta || (result?.success ? "Completado" : "Error consulta");
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

    function getSelectedClientName() {
        return els.client?.selectedOptions?.[0]?.textContent?.trim() || "";
    }

    function setBusy(isBusy) {
        state.busy = isBusy;
        [els.client, els.month, els.tenant, els.connect, els.test, els.collect].forEach(element => {
            if (element) {
                element.disabled = isBusy;
            }
        });
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
                    message = typeof payload === "string"
                        ? payload
                        : payload?.message || payload?.detail || payload?.title || rawBody;
                } catch {
                    message = rawBody;
                }
            }

            throw new Error(message || "No fue posible completar la solicitud.");
        }

        if (!contentType.includes("application/json")) {
            throw new Error(rawBody || "La respuesta del servidor no fue valida.");
        }

        return rawBody ? JSON.parse(rawBody) : null;
    }

    function setStatus(type, message) {
        if (!els.status) {
            return;
        }

        if (!message) {
            els.status.className = "support-cloud-status";
            els.status.textContent = "";
            return;
        }

        els.status.className = `support-cloud-status is-visible is-${type}`;
        els.status.textContent = message;
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
