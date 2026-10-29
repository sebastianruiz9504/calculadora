(() => {
    "use strict";
    const root = document.getElementById("monthCloseApp");
    if (!root) return;
    const $ = id => document.getElementById(id);
    const months = ["Ene", "Feb", "Mar", "Abr", "May", "Jun", "Jul", "Ago", "Sep", "Oct", "Nov", "Dic"];
    const names = ["Enero", "Febrero", "Marzo", "Abril", "Mayo", "Junio", "Julio", "Agosto", "Septiembre", "Octubre", "Noviembre", "Diciembre"];
    const tones = ["#3478db", "#cc7b25", "#875ad3", "#008f83", "#008f83", "#875ad3", "#b16c8b"];
    const labels = { partial: "Parcial · revisar", reconstructed: "Reconstruido", unavailable: "Pendiente de historial", restricted: "Acceso restringido", captured: "Capturado" };
    const money = new Intl.NumberFormat("es-CO", { maximumFractionDigits: 0 });
    const compact = new Intl.NumberFormat("es-CO", { notation: "compact", maximumFractionDigits: 1 });
    const number = new Intl.NumberFormat("es-CO", { maximumFractionDigits: 2 });
    const date = value => value ? String(value).slice(0, 10).split("-").reverse().join("/") : "—";
    const el = (tag, className, text) => { const node = document.createElement(tag); if (className) node.className = className; if (text !== undefined) node.textContent = text; return node; };
    const state = { loaded: false, loading: false, data: null, period: null, snapshot: null, key: null, page: 1, pages: 1, indexRequest: null, detailRequest: null, generation: 0 };
    const localYear = Number(new Intl.DateTimeFormat("en", { year: "numeric", timeZone: "America/Bogota" }).format(new Date()));
    for (let y = Math.max(2026, localYear); y >= 2026; y--) $("mcYear").add(new Option(String(y), String(y)));
    function status(text, error = false) { $("mcStatus").textContent = text; $("mcStatus").classList.toggle("is-error", error); }
    function latest() {
        const map = new Map();
        (state.data?.snapshots || []).forEach(s => { if (!map.has(s.period) || map.get(s.period).revision < s.revision) map.set(s.period, s); });
        return map;
    }
    async function json(url, signal) {
        const response = await fetch(url, { signal, headers: { Accept: "application/json" }, cache: "no-store" });
        if (!response.ok || !response.headers.get("content-type")?.includes("application/json")) {
            if (response.status === 403) throw new Error("Tu usuario no tiene permiso para consultar este reporte.");
            if (response.redirected) throw new Error("La sesión venció. Recarga la página para iniciar sesión.");
            throw new Error("No fue posible consultar el cierre. Intenta nuevamente.");
        }
        return response.json();
    }
    function url(base, parameters) { const u = new URL(base, window.location.origin); Object.entries(parameters).forEach(([k,v]) => u.searchParams.set(k, String(v))); return u; }
    async function loadIndex(force = false) {
        if ((state.loaded && !force) || (state.loading && !force)) return;
        state.indexRequest?.abort(); state.detailRequest?.abort();
        const request = new AbortController(); state.indexRequest = request;
        state.loading = true; root.setAttribute("aria-busy", "true"); status("Consultando cierres guardados…");
        try {
            const data = await json(url(root.dataset.indexUrl, { year: $("mcYear").value }), request.signal);
            if (request !== state.indexRequest) return;
            state.data = data; state.loaded = true; status("");
            const available = [...latest().keys()].sort();
            const keep = state.period?.startsWith($("mcYear").value + "-") && available.includes(state.period);
            selectPeriod(keep ? state.period : available.at(-1) || `${$("mcYear").value}-01`);
        } catch (error) { if (error.name !== "AbortError") { status(error.message, true); state.loaded = false; } }
        finally { if (request === state.indexRequest) { state.loading = false; root.setAttribute("aria-busy", "false"); } }
    }
    function drawMonths() {
        const all = latest(); $("mcMonths").replaceChildren();
        months.forEach((name, index) => {
            const period = `${$("mcYear").value}-${String(index + 1).padStart(2, "0")}`;
            const b = el("button", "mc-month", name); b.type = "button"; b.setAttribute("aria-pressed", String(period === state.period));
            const current = state.data.today.slice(0, 7);
            b.append(el("small", "", all.has(period) ? "Guardado" : period === current ? "En curso" : period > current ? "Próximo" : "Sin cierre"));
            b.addEventListener("click", () => selectPeriod(period)); $("mcMonths").append(b);
        });
    }
    function selectPeriod(period, id) {
        state.detailRequest?.abort(); state.generation++; state.period = period; state.key = null; state.page = 1; $("mcSearch").value = ""; $("mcDetail").hidden = true;
        const versions = (state.data.snapshots || []).filter(s => s.period === period).sort((a,b) => b.revision - a.revision);
        state.snapshot = versions.find(s => s.id === id) || versions[0] || null;
        $("mcContent").hidden = !state.snapshot; $("mcEmpty").hidden = !!state.snapshot; drawMonths();
        if (!state.snapshot) {
            const current = state.data.today.slice(0, 7);
            $("mcEmpty").textContent = period >= current ? "Este mes todavía no ha cerrado. La captura se realizará al iniciar el mes siguiente, en hora de Colombia." : "No hay un cierre guardado para este mes. La recuperación automática está pendiente; no se muestran saldos calculados con datos actuales.";
            return;
        }
        const s = state.snapshot;
        $("mcVersion").replaceChildren(...versions.map(v => new Option(`Versión ${v.revision}`, v.id, false, v.id === s.id)));
        $("mcPeriodTitle").textContent = `${names[Number(period.slice(5)) - 1]} ${period.slice(0,4)} · corte ${date(s.cutoff)}`;
        $("mcMode").textContent = s.mode === "captured" ? "Captura guardada" : "Reconstrucción histórica";
        $("mcCapture").textContent = "Guardado el " + new Intl.DateTimeFormat("es-CO", { dateStyle: "medium", timeStyle: "short", timeZone: "America/Bogota" }).format(new Date(s.capturedAt)) + " · No cambia con los pagos posteriores";
        drawMetrics(); drawBars("mcAging", s.aging, [tones[0], tones[2], tones[1], "#cf5b76"]); drawBars("mcLines", s.billingLines, [tones[0], tones[2], tones[1]]); drawTrend(); drawReports();
        $("mcWarnings").replaceChildren(...s.warnings.map(w => el("li", "", w))); $("mcHash").textContent = "SHA-256 · " + s.hash;
    }
    function drawMetrics() {
        $("mcMetrics").replaceChildren();
        const reports = state.snapshot.reports;
        const items = [
            ["Cuentas por cobrar", reports.find(x => x.key === "receivables")?.total, "Saldo identificable · no incluye filas sin soporte", tones[0]],
            ["Cuentas por pagar", reports.find(x => x.key === "payables")?.total, "Saldo identificable · cobertura parcial", tones[1]],
            ["Facturación del mes", reports.find(x => x.key === "billing")?.total, "Base neta de notas crédito", tones[2]]
        ];
        items.forEach(([label,value,hint,tone]) => { const card = el("article", "mc-metric"); card.style.setProperty("--tone", tone); card.append(el("span", "", label), el("strong", "", value === null || value === undefined ? "Pendiente" : "$ " + compact.format(value)), el("small", "", hint)); $("mcMetrics").append(card); });
        const count = reports.filter(r => !["unavailable", "restricted"].includes(r.state)).length;
        const card = el("article", "mc-metric"); card.style.setProperty("--tone", tones[3]); card.append(el("span", "", "Reportes con datos"), el("strong", "", `${count} / 7`), el("small", "", "Cada reporte muestra su alcance y pendientes")); $("mcMetrics").append(card);
    }
    function drawBars(id, items, colors) {
        const box = $(id); box.replaceChildren(); const max = Math.max(1, ...items.map(x => Math.abs(x.value)));
        if (!items.length) { box.append(el("p", "", "Sin valores verificables para este corte.")); return; }
        items.forEach((item,i) => { const row = el("div"); row.style.setProperty("--tone", colors[i % colors.length]); const label = el("div", "mc-bar-label"); label.append(el("span", "", item.label), el("strong", "", "$ " + money.format(item.value))); const track = el("div", "mc-track"); const fill = el("div", "mc-fill"); fill.style.width = `${Math.abs(item.value) / max * 100}%`; track.append(fill); row.append(label, track); box.append(row); });
    }
    function drawTrend() {
        const snapshots = [...latest().values()].sort((a,b) => a.period.localeCompare(b.period));
        const values = snapshots.map(s => ({ s, amount: s.reports.find(r => r.key === "billing")?.total ?? null })); const max = Math.max(1, ...values.map(x => Math.abs(x.amount ?? 0)));
        $("mcTrend").replaceChildren();
        values.forEach(({s,amount}) => { const b = el("button"); b.type = "button"; b.setAttribute("aria-pressed", String(s.period === state.period)); b.setAttribute("aria-label", `${s.period}, base facturada ${amount === null ? "pendiente" : money.format(amount)} pesos`); const bar = el("span", "mc-trend-bar"); bar.style.height = `${Math.abs(amount ?? 0) / max * 105}px`; b.append(el("strong", "", amount === null ? "—" : "$ " + compact.format(amount)), bar, el("span", "", months[Number(s.period.slice(5)) - 1])); b.addEventListener("click", () => selectPeriod(s.period)); $("mcTrend").append(b); });
    }
    function drawReports() {
        $("mcReports").replaceChildren();
        state.snapshot.reports.forEach((r,i) => { const b = el("button", "mc-report"); b.type = "button"; b.dataset.state = r.state; b.style.setProperty("--tone", tones[i]); b.setAttribute("aria-pressed", String(state.key === r.key)); b.disabled = r.state === "restricted"; b.append(el("span", "mc-report-index", `0${i + 1}`), el("strong", "", r.title), el("span", "mc-badge", labels[r.state] || r.state), el("small", "", r.state === "unavailable" ? "Ver información necesaria" : r.state === "restricted" ? "Requiere RH o Nómina" : `${number.format(r.count)} registros guardados`)); b.addEventListener("click", () => { state.key = r.key; state.page = 1; $("mcSearch").value = ""; drawReports(); loadReport(); }); $("mcReports").append(b); });
    }
    async function loadReport() {
        if (!state.key || !state.snapshot) return;
        state.detailRequest?.abort(); const request = new AbortController(); state.detailRequest = request; const generation = state.generation;
        $("mcDetail").hidden = false; $("mcDetail").setAttribute("aria-busy", "true"); $("mcCount").textContent = "Cargando detalle…";
        $("mcExport").hidden = true; $("mcPrevious").disabled = true; $("mcNext").disabled = true; $("mcTableBody").replaceChildren();
        try {
            const r = await json(url(root.dataset.reportUrl, { id: state.snapshot.id, key: state.key, page: state.page, pageSize: 50, search: $("mcSearch").value.trim() }), request.signal);
            if (request !== state.detailRequest || generation !== state.generation) return;
            state.page = r.page; state.pages = r.pages; $("mcDetailTitle").textContent = r.title; $("mcTableCaption").textContent = `${r.title} · ${state.period}`;
            $("mcDetailSubtitle").textContent = r.message; $("mcNeeded").textContent = "Para completar este reporte: " + r.needed;
            $("mcCount").textContent = `${number.format(r.count)} de ${number.format(r.totalCount)} registros`; $("mcPage").textContent = `Página ${r.page} de ${r.pages}`;
            $("mcPrevious").disabled = r.page <= 1; $("mcNext").disabled = r.page >= r.pages;
            $("mcExport").hidden = !r.totalCount; $("mcExport").href = url(root.dataset.exportUrl, { id: state.snapshot.id, key: state.key });
            const header = el("tr"); r.columns.forEach(c => { const cell = el("th", "", c.label); cell.scope = "col"; header.append(cell); }); $("mcTableHead").replaceChildren(header);
            const fragment = document.createDocumentFragment();
            r.rows.forEach(row => { const tr = el("tr"); r.columns.forEach(c => { const value = row[c.key]; const missing = value === null || value === undefined; const formatted = missing ? "—" : c.type === "money" ? "$ " + number.format(value) : c.type === "number" ? number.format(value) : c.type === "date" ? date(value) : String(value); tr.append(el("td", ["money", "number"].includes(c.type) ? "is-number" : "", formatted)); }); fragment.append(tr); });
            if (!r.rows.length) { const tr = el("tr"), cell = el("td", "", r.state === "unavailable" ? "Historial pendiente. No se ha supuesto un saldo cero." : "No hay registros para esta búsqueda."); cell.colSpan = r.columns.length; tr.append(cell); fragment.append(tr); }
            $("mcTableBody").replaceChildren(fragment);
        } catch (error) { if (error.name !== "AbortError") { $("mcCount").textContent = error.message; $("mcTableHead").replaceChildren(); } }
        finally { if (request === state.detailRequest) $("mcDetail").setAttribute("aria-busy", "false"); }
    }
    let timer;
    $("mcSearch").addEventListener("input", () => { clearTimeout(timer); timer = setTimeout(() => { state.page = 1; loadReport(); }, 300); });
    $("mcYear").addEventListener("change", () => { state.period = null; loadIndex(true); });
    $("mcRefresh").addEventListener("click", () => loadIndex(true));
    $("mcVersion").addEventListener("change", () => selectPeriod(state.period, $("mcVersion").value));
    $("mcPrevious").addEventListener("click", () => { if (state.page > 1) { state.page--; loadReport(); } });
    $("mcNext").addEventListener("click", () => { if (state.page < state.pages) { state.page++; loadReport(); } });
    window.addEventListener("dashboard:month-close", () => loadIndex());
    if (!root.closest("[data-dashboard-panel]").hidden) loadIndex();
})();
