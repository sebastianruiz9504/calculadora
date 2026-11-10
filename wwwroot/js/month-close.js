(() => {
    "use strict";
    const root = document.getElementById("monthCloseApp");
    if (!root) return;
    const $ = id => document.getElementById(id);
    const months = ["Ene", "Feb", "Mar", "Abr", "May", "Jun", "Jul", "Ago", "Sep", "Oct", "Nov", "Dic"];
    const names = ["Enero", "Febrero", "Marzo", "Abril", "Mayo", "Junio", "Julio", "Agosto", "Septiembre", "Octubre", "Noviembre", "Diciembre"];
    const number = new Intl.NumberFormat("es-CO", { maximumFractionDigits: 2 });
    const compact = new Intl.NumberFormat("es-CO", { notation: "compact", maximumFractionDigits: 1 });
    const date = value => value ? String(value).slice(0, 10).split("-").reverse().join("/") : "—";
    const el = (tag, className, text) => { const node = document.createElement(tag); if (className) node.className = className; if (text !== undefined) node.textContent = text; return node; };
    const state = { loaded: false, data: null, period: null, snapshot: null, key: null, filters: {}, page: 1, pages: 1, vertical: "cloud", profitPage: 1, profitPages: 1, requests: {}, generation: 0, ytdId: null, ytdVisible: false };
    const currentYear = Number(new Intl.DateTimeFormat("en", { year: "numeric", timeZone: "America/Bogota" }).format(new Date()));
    for (let y = Math.max(2025, currentYear); y >= 2025; y--) $("mcYear").add(new Option(String(y), String(y)));
    const requestedYear = new URL(window.location.href).searchParams.get("year");
    if ([...$("mcYear").options].some(o => o.value === requestedYear)) $("mcYear").value = requestedYear;
    function request(kind) { state.requests[kind]?.abort(); const r = new AbortController(); state.requests[kind] = r; return r; }
    function url(base, parameters) { const u = new URL(base, location.origin); Object.entries(parameters).forEach(([k, v]) => { if (v !== null && v !== undefined && v !== "") u.searchParams.set(k, String(v)); }); return u; }
    async function json(base, parameters, signal) {
        const response = await fetch(url(base, parameters), { signal, headers: { Accept: "application/json" }, cache: "no-store" });
        if (!response.ok || !response.headers.get("content-type")?.includes("application/json")) {
            if (response.status === 403) throw new Error("Tu usuario no tiene permiso para consultar este reporte.");
            if (response.status === 404) throw new Error("Este detalle no fue incluido en la versión seleccionada.");
            if (response.redirected) throw new Error("La sesión venció. Recarga la página para iniciar sesión.");
            throw new Error("No fue posible consultar el cierre. Intenta nuevamente.");
        }
        return response.json();
    }
    const latest = () => { const map = new Map(); (state.data?.snapshots || []).forEach(s => { if (!map.has(s.period) || map.get(s.period).revision < s.revision) map.set(s.period, s); }); return map; };
    function status(text, error = false) { $("mcStatus").textContent = text; $("mcStatus").classList.toggle("is-error", error); }
    async function loadIndex(force = false, background = false) {
        if (state.loaded && !force) { state.ytdId = null; if (state.ytdVisible) loadYtd(); return; }
        const r = request("index"); if (!background) status("Consultando última captura…");
        try {
            const data = await json(root.dataset.indexUrl, { year: $("mcYear").value }, r.signal);
            if (r !== state.requests.index) return;
            state.data = data; state.loaded = true;
            const available = [...latest().keys()].sort();
            const keep = state.period?.startsWith($("mcYear").value + "-");
            const period = keep ? state.period : available.at(-1) || `${$("mcYear").value}-01`;
            const candidate = latest().get(period);
            if (!background || candidate?.hash !== state.snapshot?.hash) selectPeriod(period, undefined, background);
            status("");
        } catch (error) { if (error.name !== "AbortError") status(error.message, true); }
    }
    function drawMonths() {
        const all = latest(); $("mcMonths").replaceChildren(); const current = state.data.today.slice(0, 7);
        months.forEach((name, i) => {
            const period = `${$("mcYear").value}-${String(i + 1).padStart(2, "0")}`;
            const b = el("button", "mc-month", name); b.type = "button"; b.setAttribute("aria-pressed", String(period === state.period));
            b.append(el("small", "", period === current ? "En curso" : all.has(period) ? "Cerrado" : period > current ? "Próximo" : "Sin cierre"));
            b.addEventListener("click", () => selectPeriod(period)); $("mcMonths").append(b);
        });
    }
    function selectPeriod(period, id, preserveFilters = false) {
        ["detail", "profit", "ytd"].forEach(k => state.requests[k]?.abort());
        closeModal(); state.generation++; state.period = period; state.key = null; state.ytdId = null; state.preserveYtdFilters = preserveFilters;
        if (!preserveFilters) { state.profitPage = 1; $("mcProfitSearch").value = ""; window.MonthCloseYtd?.release(); }
        $("mcYtdStatus").textContent = "El gráfico se carga al llegar a esta sección.";
        const versions = (state.data.snapshots || []).filter(s => s.period === period).sort((a, b) => b.revision - a.revision);
        state.snapshot = versions.find(s => s.id === id) || versions[0] || null;
        $("mcContent").hidden = !state.snapshot; $("mcEmpty").hidden = !!state.snapshot; drawMonths();
        if (!state.snapshot) {
            $("mcRefresh").hidden = period !== state.data.today.slice(0, 7);
            $("mcEmpty").textContent = period === state.data.today.slice(0, 7) ? "Se está preparando la primera captura del mes en curso. Vuelve a consultar en unos minutos." : period > state.data.today.slice(0, 7) ? "Este mes aún no ha comenzado." : "No existe un cierre guardado para este mes.";
            return;
        }
        const s = state.snapshot, live = s.mode === "live";
        $("mcRefresh").hidden = !live; $("mcVersionLabel").hidden = live || versions.length < 2;
        $("mcVersion").replaceChildren(...versions.map(v => new Option(`Versión ${v.revision}`, v.id, false, v.id === s.id)));
        $("mcPeriodTitle").textContent = `${names[Number(period.slice(5)) - 1]} ${period.slice(0, 4)}`;
        $("mcMode").textContent = live ? "● Mes en curso" : "🔒 Cierre inmutable";
        $("mcCapture").textContent = (live ? "Última captura: " : "Captura guardada: ") + new Intl.DateTimeFormat("es-CO", { dateStyle: "medium", timeStyle: "short", timeZone: "America/Bogota" }).format(new Date(s.capturedAt)) + (live ? " · Actualización automática" : " · Solo consulta");
        if (live && Date.now() - Date.parse(s.capturedAt) > 20 * 60 * 1000) $("mcCapture").textContent += " · La actualización de fuentes está demorada";
        drawMetrics(); drawBars("mcAging", s.aging || [], true); drawBars("mcLines", s.billingLines || [], false);
        $("mcWarnings").replaceChildren(...(s.warnings || []).map(w => el("li", "", w))); $("mcHash").textContent = "SHA-256 · " + s.hash;
        $("mcAccountAudit").hidden = !s.reports.some(r => r.key === "account-audit");
        loadProfit(); if (state.ytdVisible) loadYtd();
    }
    function drawMetrics() {
        $("mcMetrics").replaceChildren();
        const items = [["receivables", "Cuentas por cobrar", "Saldo identificable al corte", "#3478db"], ["payables", "Cuentas por pagar", "Saldo identificable al corte", "#cc7b25"], ["billing", "Facturación del mes", "Valor antes de IVA · neto de notas crédito", "#875ad3"], ["business", "Total de negocios activos", "Valor total USD · anual, igual que Todos los negocios", "#008f83"]];
        items.forEach(([key, label, hint, tone]) => {
            const report = state.snapshot.reports.find(r => r.key === key), value = report?.total;
            const b = el("button", "mc-metric"); b.type = "button"; b.style.setProperty("--tone", tone); b.disabled = !report;
            b.append(el("span", "", label), el("strong", "", value == null ? "Sin captura" : (key === "business" ? "US$ " : "$ ") + compact.format(value)), el("small", "", hint));
            b.setAttribute("aria-label", `${label}: ${value == null ? "sin captura" : number.format(value)}. Ver detalle`);
            b.addEventListener("click", () => openReport(key, {}, label)); $("mcMetrics").append(b);
        });
    }
    function drawBars(id, items, aging) {
        const box = $(id); box.replaceChildren(); const max = Math.max(1, ...items.map(x => Math.abs(x.value)));
        const total = items.reduce((sum, x) => sum + x.value, 0);
        if (!items.length) { box.append(el("p", "", "Sin valores verificables para este corte.")); return; }
        items.forEach((item, i) => {
            const b = el("button", "mc-bar-button"); b.type = "button"; b.style.setProperty("--tone", ["#3478db", "#875ad3", "#cc7b25", "#cf5b76"][i % 4]);
            const label = el("div", "mc-bar-label"); label.append(el("span", "", item.label + (!aging && total !== 0 ? ` · ${number.format(item.value / total * 100)} %` : "")), el("strong", "", "$ " + number.format(item.value)));
            const track = el("div", "mc-track"), fill = el("div", "mc-fill"); fill.style.width = `${Math.abs(item.value) / max * 100}%`; track.append(fill); b.append(label, track);
            b.addEventListener("click", () => openReport(aging ? "receivables" : "billing", aging ? { aging: i } : { line: item.label }, (aging ? "Cartera · " : "Facturación · ") + item.label)); box.append(b);
        });
    }
    function format(value, column, unit) {
        if (value === null || value === undefined) return "Pendiente";
        return column.type === "money" ? (unit === "USD" ? "US$ " : "$ ") + number.format(value) : column.type === "number" ? number.format(value) : column.type === "date" ? date(value) : String(value);
    }
    function table(prefix, report, columns, clickable = false) {
        const header = el("tr"); columns.forEach(c => { const th = el("th", "", c.label); th.scope = "col"; header.append(th); }); $(prefix + "Head").replaceChildren(header);
        const fragment = document.createDocumentFragment();
        report.rows.forEach(row => {
            const tr = el("tr"); columns.forEach((c, i) => {
                const td = el("td", ["money", "number"].includes(c.type) ? "is-number" : "");
                if (clickable && i === 0) { const b = el("button", "mc-client-link", format(row[c.key], c, report.unit)); b.type = "button"; b.addEventListener("click", () => openReport("profit-detail", { line: state.vertical === "cloud" ? "Cloud" : "Copiers", clientKey: row.key }, "Utilidad · " + row.party)); td.append(b); }
                else td.textContent = format(row[c.key], c, report.unit);
                if (c.key === "profit" && row.profit != null) td.classList.add(row.profit < 0 ? "mc-negative" : "mc-positive"); tr.append(td);
            }); fragment.append(tr);
        });
        if (!report.rows.length) { const tr = el("tr"), td = el("td", "", "No hay registros para esta consulta."); td.colSpan = columns.length; tr.append(td); fragment.append(tr); }
        $(prefix + "Body").replaceChildren(fragment);
        const foot = el("tr"); columns.forEach((c, i) => foot.append(el("td", c.type === "money" ? "is-number" : "", i === 0 ? "Total identificable · consulta completa" : c.type === "money" && report.totals?.[c.key] != null ? format(report.totals[c.key], c, report.unit) : ""))); $(prefix + "Foot").replaceChildren(foot);
    }
    async function loadProfit() {
        if (!state.snapshot) return;
        const r = request("profit"), generation = state.generation;
        $("mcProfitStatus").textContent = "Cargando utilidad…"; $("mcProfitBody").replaceChildren(); $("mcProfitFoot").replaceChildren();
        $("mcProfitPrevious").disabled = $("mcProfitNext").disabled = true;
        $("mcProfitNote").textContent = state.vertical === "cloud" ? "Licenciamiento por fecha de factura, cruzado con lo facturado en el mismo mes. Los clientes con costo y sin factura ya aparecen; las relaciones pendientes no se presentan como utilidad confirmada." : "Costo por visita completada: $100.000 + flete de $40.000 / $100.000 / $500.000 según ciudad. Suministros al último costo aprobado disponible antes de su entrega. No incluye gastos generales.";
        try {
            const data = await json(root.dataset.reportUrl, { id: state.snapshot.id, key: "profit-" + state.vertical, page: state.profitPage, search: $("mcProfitSearch").value.trim() }, r.signal);
            if (r !== state.requests.profit || generation !== state.generation) return;
            const hidden = state.vertical === "cloud" ? ["supplyQuantity", "supplies", "maintenanceCount", "maintenance", "freight"] : ["licensing", "accounts"];
            table("mcProfit", data, data.columns.filter(c => !hidden.includes(c.key)), true);
            state.profitPage = data.page; state.profitPages = data.pages;
            $("mcProfitStatus").textContent = data.unresolved ? `${data.unresolved} clientes con cruce o costos pendientes. Los totales de utilidad incluyen solo filas determinables.` : "";
            $("mcProfitCount").textContent = `${data.count} clientes`; $("mcProfitPage").textContent = `${data.page} / ${data.pages}`;
            $("mcProfitPrevious").disabled = data.page <= 1; $("mcProfitNext").disabled = data.page >= data.pages;
        } catch (error) { if (error.name !== "AbortError") $("mcProfitStatus").textContent = error.message; }
    }
    function openReport(key, filters = {}, title = "Detalle") {
        if (!state.snapshot) return;
        state.key = key; state.filters = filters; state.page = 1; $("mcSearch").value = "";
        $("mcDetailTitle").textContent = title; $("mcDetailPeriod").textContent = `${state.period} · ${state.snapshot.mode === "live" ? "Última captura del mes en curso" : "Cierre inmutable · solo consulta"}`;
        if (!$("mcModal").open) $("mcModal").showModal(); loadReport();
    }
    function closeModal() { state.requests.detail?.abort(); if ($("mcModal").open) $("mcModal").close(); }
    async function loadReport() {
        if (!state.key || !state.snapshot) return;
        const r = request("detail"), generation = state.generation; $("mcCount").textContent = "Cargando detalle…";
        $("mcExport").hidden = true; $("mcTableHead").replaceChildren(); $("mcTableBody").replaceChildren(); $("mcTableFoot").replaceChildren();
        $("mcPrevious").disabled = $("mcNext").disabled = true; $("mcPage").textContent = ""; $("mcDetailSubtitle").textContent = ""; $("mcNeeded").hidden = true;
        try {
            const params = { id: state.snapshot.id, key: state.key, ...state.filters };
            const data = await json(root.dataset.reportUrl, { ...params, page: state.page, search: $("mcSearch").value.trim() }, r.signal);
            if (r !== state.requests.detail || generation !== state.generation) return;
            table("mcTable", data, data.columns); state.page = data.page; state.pages = data.pages;
            $("mcTableCaption").textContent = `${data.title} · ${state.period}`; $("mcDetailSubtitle").textContent = data.message;
            $("mcNeeded").hidden = !data.unresolved && data.state !== "unavailable"; $("mcNeeded").textContent = data.needed || "Este reporte no se capturó en esta versión.";
            $("mcCount").textContent = `${number.format(data.count)} registros`; $("mcPage").textContent = `Página ${data.page} de ${data.pages}`;
            $("mcPrevious").disabled = data.page <= 1; $("mcNext").disabled = data.page >= data.pages;
            $("mcExport").hidden = !data.count; $("mcExport").href = url(root.dataset.exportUrl, params);
        } catch (error) { if (error.name !== "AbortError") $("mcCount").textContent = error.message; }
    }
    async function loadYtd() {
        if (!state.snapshot || state.ytdId === state.snapshot.id || root.closest("[data-dashboard-panel]").hidden) return;
        const r = request("ytd"), generation = state.generation, id = state.snapshot.id; state.ytdId = id;
        $("mcYtdStatus").textContent = "Cargando el YTD de las capturas guardadas…";
        try {
            const data = await json(root.dataset.ytdUrl, { id }, r.signal);
            if (r !== state.requests.ytd || generation !== state.generation) return;
            if (root.closest("[data-dashboard-panel]").hidden) { state.ytdId = null; return; }
            window.MonthCloseYtd?.mount(data, $("mcYtdMount"), state.preserveYtdFilters); $("mcYtdStatus").textContent = data.sourceWarning || "";
        } catch (error) { if (error.name !== "AbortError") { state.ytdId = null; $("mcYtdStatus").textContent = error.message; } }
    }
    new IntersectionObserver(entries => { state.ytdVisible = entries.some(e => e.isIntersecting); if (state.ytdVisible) loadYtd(); }, { rootMargin: "150px" }).observe($("mcYtdPanel"));
    function debounce(input, action) { let timer; $(input).addEventListener("input", () => { clearTimeout(timer); timer = setTimeout(action, 250); }); }
    debounce("mcSearch", () => { state.page = 1; loadReport(); }); debounce("mcProfitSearch", () => { state.profitPage = 1; loadProfit(); });
    $("mcYear").addEventListener("change", () => { state.period = null; loadIndex(true); });
    $("mcRefresh").addEventListener("click", () => loadIndex(true)); $("mcVersion").addEventListener("change", () => selectPeriod(state.period, $("mcVersion").value));
    $("mcClose").addEventListener("click", closeModal); $("mcModal").addEventListener("close", () => state.requests.detail?.abort());
    $("mcModal").addEventListener("click", e => { if (e.target === $("mcModal")) { const rect = $("mcModal").getBoundingClientRect(); if (e.clientX < rect.left || e.clientX > rect.right || e.clientY < rect.top || e.clientY > rect.bottom) closeModal(); } });
    $("mcPrevious").addEventListener("click", () => { if (state.page > 1) { state.page--; loadReport(); } }); $("mcNext").addEventListener("click", () => { if (state.page < state.pages) { state.page++; loadReport(); } });
    $("mcProfitPrevious").addEventListener("click", () => { if (state.profitPage > 1) { state.profitPage--; loadProfit(); } }); $("mcProfitNext").addEventListener("click", () => { if (state.profitPage < state.profitPages) { state.profitPage++; loadProfit(); } });
    document.querySelectorAll("[data-mc-vertical]").forEach(b => b.addEventListener("click", () => { state.vertical = b.dataset.mcVertical; state.profitPage = 1; document.querySelectorAll("[data-mc-vertical]").forEach(x => x.setAttribute("aria-pressed", String(x === b))); loadProfit(); }));
    $("mcAccountAudit").addEventListener("click", () => openReport("account-audit", {}, "Revisión de Account ID"));
    window.addEventListener("dashboard:month-close", () => loadIndex());
    setInterval(() => { if (!document.hidden && !root.closest("[data-dashboard-panel]").hidden && state.snapshot?.mode === "live" && !$("mcModal").open && $("ytdDetailModal")?.hidden !== false) loadIndex(true, true); }, 60000);
    if (!root.closest("[data-dashboard-panel]").hidden) loadIndex();
})();
