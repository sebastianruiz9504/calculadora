(function () {
    "use strict";
    const root = document.getElementById("dashboardCopiersPreventive");
    if (!root) return;
    const body = root.querySelector("[data-preventive-body]");
    const status = root.querySelector("[data-preventive-status]");
    const summary = root.querySelector("[data-preventive-summary]");
    const search = root.querySelector("[data-preventive-search]");
    const periods = [...root.querySelectorAll("[data-preventive-period]")];
    const refresh = root.querySelector("[data-preventive-refresh]");
    const token = root.querySelector('input[name="__RequestVerificationToken"]')?.value || "";
    const state = { period: "this-month", data: null, generation: 0, controller: null, expanded: new Set(), saving: new Set(), loadedAt: 0 };
    const number = new Intl.NumberFormat("es-CO");
    const escape = value => String(value ?? "").replace(/[&<>"']/g, character => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" })[character]);
    const fold = value => String(value ?? "").normalize("NFD").replace(/[\u0300-\u036f]/g, "").toLowerCase();
    const count = value => number.format(Number(value) || 0);
    const reading = value => value === null || value === undefined ? "—" : count(value);

    async function json(url, options = {}) {
        const response = await fetch(url, { credentials: "same-origin", cache: "no-store", ...options,
            headers: { Accept: "application/json", ...options.headers } });
        if (!response.headers.get("content-type")?.includes("application/json")) throw new Error("La sesión no está disponible. Recarga la página e inicia sesión.");
        const data = await response.json();
        if (!response.ok) throw new Error(data.message || "No fue posible consultar el panel.");
        return data;
    }

    function message(text, error = false) {
        status.textContent = text;
        status.classList.toggle("is-error", error);
    }

    function reportLink(report) {
        if (!report.hasAttachment) return "Sin archivo";
        const url = new URL(root.dataset.reportUrl, window.location.origin);
        url.searchParams.set("maintenanceId", report.recordId);
        return `<a href="${escape(url.href)}" target="_blank" rel="noopener">Ver reporte</a>`;
    }

    function detail(client) {
        const reports = Array.isArray(client.reports) ? client.reports : [];
        const equipment = Array.isArray(client.equipment) ? client.equipment : [];
        return `<tr class="preventive-board__detail"><td colspan="5">
            <h3>Visitas preventivas · ${escape(state.data.periodLabel)}</h3>
            ${reports.length ? `<div class="preventive-board__table-wrap"><table class="table dashboard-table"><thead><tr><th>Fecha</th><th>Referencia</th><th>Técnico</th><th>Equipos atendidos</th><th>Reporte</th></tr></thead><tbody>${reports.map(report => `<tr>
                <td>${escape(report.dateDisplay)}</td><td>${escape(report.reference || (report.isHistorical ? "Histórico" : "MTO v2"))}</td>
                <td>${escape(report.technicianName)}</td><td>${escape((report.equipmentSerials || []).join(" · "))}</td><td>${reportLink(report)}</td>
            </tr>`).join("")}</tbody></table></div>` : "<p>Sin visitas preventivas finalizadas en este periodo.</p>"}
            <h3>Equipos asignados actualmente</h3>
            <p>${count(client.maintenanceRegisteredCount)} de ${count(client.equipmentCount)} con preventivo en el periodo · ${count(client.countersRegisteredCount)} con contador.</p>
            ${equipment.length ? `<div class="preventive-board__table-wrap"><table class="table dashboard-table"><thead><tr><th>Equipo / referencia</th><th>Sede / área</th><th>Preventivo del mes</th><th>Técnico</th><th>Fecha contador</th><th>Impresora</th><th>Escáner</th></tr></thead><tbody>${equipment.map(row => `<tr>
                <td><strong>${escape(row.serial)}</strong><small>${escape(row.reference)} · ${escape(row.categoryLabel)}</small></td>
                <td>${escape(row.site || "—")}<small>${escape(row.area)}</small></td>
                <td>${row.hasMonthlyMaintenance ? escape(row.lastMaintenanceDateDisplay) : "Sin registro"}</td><td>${escape(row.technicianName || "—")}</td>
                <td>${escape(row.hasMonthlyCounter ? row.counterDateDisplay : "Sin registro")}</td><td>${reading(row.counterCopies)}</td><td>${reading(row.counterScans)}</td>
            </tr>`).join("")}</tbody></table></div>` : "<p>Este cliente no tiene equipos asignados actualmente.</p>"}
        </td></tr>`;
    }

    function render() {
        if (!state.data) return;
        const all = state.data.clients || [];
        const query = fold(search.value.trim());
        const clients = all.filter(client => fold(`${client.clientName} ${client.clientCity}`).includes(query));
        const done = all.filter(client => client.hasMonthlyPreventive).length;
        summary.textContent = `${state.data.periodLabel} · ${count(all.length)} clientes · ${count(done)} con preventivo · ${count(all.length - done)} sin preventivo registrado`;
        body.innerHTML = clients.length ? clients.map(client => {
            const expanded = state.expanded.has(client.clientKey);
            const editable = state.data.canEditMaintenanceFrequency && client.clientId;
            return `<tr><td><button type="button" class="preventive-board__client" data-preventive-toggle="${escape(client.clientKey)}" aria-expanded="${expanded}">
                <span aria-hidden="true">${expanded ? "−" : "+"}</span> ${escape(client.clientName)}</button><small>${escape(client.clientCity)}</small></td>
                <td>${editable ? `<select class="form-select" data-preventive-frequency="${escape(client.clientKey)}" aria-label="Periodicidad de ${escape(client.clientName)}" ${state.saving.has(client.clientKey) ? "disabled" : ""}>
                    <option value="monthly" ${!client.isBimonthlyMaintenance ? "selected" : ""}>Mensual</option><option value="bimonthly" ${client.isBimonthlyMaintenance ? "selected" : ""}>Bimensual</option></select>` : escape(client.maintenanceFrequencyLabel)}</td>
                <td><span class="preventive-board__badge ${client.hasMonthlyPreventive ? "is-done" : "is-pending"}">${client.hasMonthlyPreventive ? "Preventivo realizado" : "Sin preventivo registrado"}</span>
                    <small>${count(client.maintenanceReportCount)} reporte(s) en el mes</small></td>
                <td>${escape(client.lastMaintenanceDateDisplay || "—")}</td><td>${count(client.servicedEquipmentCount)}</td></tr>${expanded ? detail(client) : ""}`;
        }).join("") : '<tr><td colspan="5">No hay clientes para esta búsqueda.</td></tr>';
    }

    async function load() {
        const generation = ++state.generation;
        state.controller?.abort();
        state.controller = new AbortController();
        state.data = null;
        state.loadedAt = 0;
        root.setAttribute("aria-busy", "true");
        body.innerHTML = '<tr><td colspan="5">Consultando mantenimientos y contadores…</td></tr>';
        summary.textContent = "";
        message("Cargando el mes seleccionado…");
        periods.forEach(button => button.setAttribute("aria-pressed", String(button.dataset.preventivePeriod === state.period)));
        try {
            const url = new URL(root.dataset.boardUrl, window.location.origin);
            url.searchParams.set("period", state.period);
            const data = await json(url, { signal: state.controller.signal });
            if (generation !== state.generation) return;
            if (!Array.isArray(data.clients)) throw new Error("La respuesta del panel está incompleta.");
            state.data = data;
            state.loadedAt = Date.now();
            render();
            message("");
        } catch (error) {
            if (generation !== state.generation || error.name === "AbortError") return;
            body.replaceChildren();
            message(error.message || "No fue posible cargar el panel.", true);
        } finally {
            if (generation === state.generation) root.setAttribute("aria-busy", "false");
        }
    }

    root.addEventListener("click", event => {
        const button = event.target.closest("[data-preventive-toggle]");
        if (!button) return;
        const key = button.dataset.preventiveToggle;
        if (state.expanded.has(key)) state.expanded.delete(key); else state.expanded.add(key);
        render();
        [...root.querySelectorAll("[data-preventive-toggle]")].find(item => item.dataset.preventiveToggle === key)?.focus();
    });
    root.addEventListener("change", async event => {
        const select = event.target.closest("[data-preventive-frequency]");
        if (!select) return;
        const client = state.data?.clients.find(item => item.clientKey === select.dataset.preventiveFrequency);
        if (!client || state.saving.has(client.clientKey)) return;
        state.saving.add(client.clientKey);
        select.disabled = true;
        periods.forEach(button => button.disabled = true);
        refresh.disabled = true;
        try {
            const result = await json(root.dataset.frequencyUrl, { method: "POST", headers: { "Content-Type": "application/json", RequestVerificationToken: token },
                body: JSON.stringify({ clientId: client.clientId, clientName: client.clientName, frequencyKey: select.value }) });
            client.maintenanceFrequencyKey = result.maintenanceFrequencyKey;
            client.maintenanceFrequencyLabel = result.maintenanceFrequencyLabel;
            client.isBimonthlyMaintenance = result.isBimonthlyMaintenance;
            message(result.message || "Periodicidad actualizada.");
        } catch (error) { message(error.message, true); }
        finally {
            state.saving.delete(client.clientKey);
            periods.forEach(button => button.disabled = state.saving.size > 0);
            refresh.disabled = state.saving.size > 0;
            render();
        }
    });
    periods.forEach(button => button.addEventListener("click", () => {
        if (button.dataset.preventivePeriod === state.period) return;
        state.period = button.dataset.preventivePeriod;
        state.expanded.clear();
        load();
    }));
    refresh.addEventListener("click", load);
    search.addEventListener("input", render);
    window.CopiersPreventiveDashboard = { activate() { if (!state.saving.size && root.getAttribute("aria-busy") !== "true" && Date.now() - state.loadedAt > 60000) load(); } };
})();
