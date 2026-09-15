(() => {
    "use strict";
    const money = new Intl.NumberFormat("es-CO", { style: "currency", currency: "COP", maximumFractionDigits: 0 });
    const escape = value => String(value ?? "").replace(/[&<>"']/g, character => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[character]));
    const key = line => `${line.source}:${line.recordId}`;
    const maxLines = 200;
    function create({ root, fetchJson, getDashboard, refreshDashboard }) {
        if (!root) return null;
        const body = root.querySelector("[data-lic-detail-body]");
        const status = root.querySelector("[data-lic-detail-status]");
        const selectAll = root.querySelector("[data-lic-detail-all]");
        const save = root.querySelector("[data-lic-detail-save]");
        const reload = root.querySelector("[data-lic-detail-reload]");
        const count = root.querySelector("[data-lic-detail-count]");
        let context = null, busy = false, stale = false, opener = null, previousOverflow = "";
        let lines = [], selected = new Set();
        const label = contract => contract === "monthly" ? "Monthly" : "Prepaid";
        const target = () => context?.contractKey === "monthly" ? "onetime" : "monthly";
        const eligible = line => Boolean(line.recordId) && Number.isInteger(line.contractTypeValue);
        function message(text = "", kind = "") {
            status.className = `dashboard-status${text ? " show" : ""}${kind ? ` ${kind}` : ""}`;
            status.textContent = text;
        }
        function sync() {
            const available = lines.filter(eligible);
            count.textContent = `${selected.size} ${selected.size === 1 ? "seleccionada" : "seleccionadas"}`;
            selectAll.checked = available.length > 0 && selected.size === available.length;
            selectAll.indeterminate = selected.size > 0 && selected.size < available.length;
            selectAll.disabled = busy || stale || available.length === 0;
            body.querySelectorAll("[data-lic-line]").forEach(checkbox => {
                checkbox.disabled = busy || stale || !eligible(lines[Number(checkbox.dataset.licLine)]);
                checkbox.checked = selected.has(key(lines[Number(checkbox.dataset.licLine)]));
            });
            root.querySelectorAll("button[data-lic-detail-close]").forEach(button => { button.disabled = busy; });
            reload.disabled = busy;
            save.disabled = busy || stale || selected.size === 0 || selected.size > maxLines;
            save.textContent = busy ? "Procesando…" : `Cambiar ${selected.size ? `${selected.size} ${selected.size === 1 ? "fila" : "filas"}` : "selección"} a ${label(target())}`;
            root.setAttribute("aria-busy", String(busy));
        }
        function render(dashboard) {
            const card = context.contractKey === "monthly" ? dashboard.monthlyCostCard : dashboard.prepaidCostCard;
            const client = (card?.breakdown || []).find(item => item.clientKey === context.clientKey);
            lines = client?.lines || [];
            selected.clear();
            document.getElementById("licenciamientoDetailTitle").textContent = client?.clientName || context.clientName;
            document.getElementById("licenciamientoDetailSubtitle").textContent = `${label(context.contractKey)} · ${card?.monthLabel || dashboard.monthLabel || ""} · ${lines.length} ${lines.length === 1 ? "fila" : "filas"}`;
            body.innerHTML = lines.length ? lines.map((line, index) => `
                <tr>
                    <td><input type="checkbox" data-lic-line="${index}" aria-label="${escape(`Seleccionar ${line.description} ${line.reference}`)}" ${eligible(line) ? "" : "disabled"} /></td>
                    <td><strong>${escape(line.description)}</strong><small>${line.source === "cost" ? "Costo" : "Facturación"} · ${escape(line.clientName)}</small></td>
                    <td>${escape(line.reference || "—")}<small>${escape(line.date || "Sin fecha")}</small></td>
                    <td class="text-end">${escape(money.format(Number(line.amount || 0)))}</td>
                    <td>${escape(line.contractTypeLabel || label(context.contractKey))}</td>
                </tr>`).join("") : '<tr><td colspan="5" class="dashboard-table__empty">No quedan líneas en este tipo de contrato para el cliente.</td></tr>';
            root.querySelector("[data-lic-detail-summary]").textContent = `Costos ${money.format(Number(client?.cost || 0))} · Ventas ${money.format(Number(client?.sales || 0))}`;
            sync();
        }
        function close() {
            if (busy || root.hidden) return;
            root.hidden = true;
            document.body.style.overflow = previousOverflow;
            const currentOpener = Array.from(document.querySelectorAll("[data-lic-client]")).find(row => row.dataset.licClient === context?.clientKey && row.dataset.licContract === context?.contractKey);
            (currentOpener || (opener?.isConnected ? opener : document.getElementById("licenciamientoRefreshBtn")))?.focus();
        }
        function open(clientKey, contractKey, trigger) {
            if (busy || !["monthly", "onetime"].includes(contractKey)) return;
            const dashboard = getDashboard();
            const card = contractKey === "monthly" ? dashboard?.monthlyCostCard : dashboard?.prepaidCostCard;
            const client = (card?.breakdown || []).find(item => item.clientKey === clientKey);
            if (!client) return;
            opener = trigger;
            context = { clientKey, contractKey, clientName: client.clientName, year: dashboard.year, month: dashboard.month };
            stale = false;
            message();
            render(dashboard);
            if (root.hidden) previousOverflow = document.body.style.overflow;
            document.body.style.overflow = "hidden";
            root.hidden = false;
            root.querySelector("button[data-lic-detail-close]").focus();
        }
        async function refresh() {
            if (busy) return;
            busy = true; sync(); message("Actualizando detalle…", "info");
            try {
                const dashboard = await refreshDashboard();
                if (dashboard.year !== context.year || dashboard.month !== context.month) throw new Error("El periodo cambió. Cierra y abre nuevamente el cliente.");
                stale = false; render(dashboard); message();
            } catch (error) {
                stale = true; message(error.message || "No se pudo recargar el detalle.", "error");
            } finally { busy = false; sync(); }
        }
        async function changeContract() {
            if (save.disabled || busy || stale) return;
            const payload = {
                year: context.year, month: context.month, clientKey: context.clientKey,
                sourceContractKey: context.contractKey, targetContractKey: target(),
                lines: lines.filter(line => selected.has(key(line))).map(line => ({ source: line.source, recordId: line.recordId, expectedContractTypeValue: line.contractTypeValue }))
            };
            busy = true; sync(); message("Guardando las filas seleccionadas…", "info");
            let saved = false;
            try {
                const result = await fetchJson(root.dataset.saveUrl, {
                    method: "POST", cache: "no-store",
                    headers: { RequestVerificationToken: root.querySelector('input[name="__RequestVerificationToken"]').value },
                    body: JSON.stringify(payload)
                });
                saved = true; selected.clear();
                const dashboard = await refreshDashboard();
                render(dashboard);
                message(result.message || "Tipo de contrato actualizado.", "success");
            } catch (error) {
                stale = true;
                selected.clear();
                message(saved ? "El cambio se guardó. No se pudo actualizar el tablero; pulsa Recargar detalle."
                    : `${error.message || "No se pudo confirmar el cambio."} Recarga el detalle antes de guardar nuevamente.`, "error");
            } finally { busy = false; sync(); }
        }
        selectAll.addEventListener("change", () => {
            selected = selectAll.checked ? new Set(lines.filter(eligible).map(key)) : new Set();
            message(selected.size > maxLines ? `Puedes cambiar hasta ${maxLines} filas a la vez. Reduce la selección.` : "", selected.size > maxLines ? "error" : ""); sync();
        });
        body.addEventListener("change", event => {
            const checkbox = event.target.closest("[data-lic-line]");
            if (!checkbox || busy || stale) return;
            const lineKey = key(lines[Number(checkbox.dataset.licLine)]);
            checkbox.checked ? selected.add(lineKey) : selected.delete(lineKey);
            message(selected.size > maxLines ? `Puedes cambiar hasta ${maxLines} filas a la vez. Reduce la selección.` : "", selected.size > maxLines ? "error" : ""); sync();
        });
        root.querySelectorAll("[data-lic-detail-close]").forEach(element => element.addEventListener("click", close));
        save.addEventListener("click", changeContract);
        reload.addEventListener("click", refresh);
        root.addEventListener("keydown", event => {
            if (event.key === "Escape") { event.preventDefault(); event.stopPropagation(); close(); }
            if (event.key !== "Tab") return;
            const controls = Array.from(root.querySelectorAll('button:not(:disabled), input:not(:disabled)')).filter(element => element.type !== "hidden");
            if (!controls.length) { event.preventDefault(); return; }
            const first = controls[0], last = controls[controls.length - 1];
            if (event.shiftKey && document.activeElement === first) { event.preventDefault(); last.focus(); }
            if (!event.shiftKey && document.activeElement === last) { event.preventDefault(); first.focus(); }
        });
        return { open, close };
    }
    window.DashboardLicenciamientoDetail = { create };
})();
