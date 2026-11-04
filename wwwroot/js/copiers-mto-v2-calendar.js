(function (global) {
    "use strict";

    const timeZone = "America/Bogota";
    const dayMilliseconds = 86400000;
    const minuteHeight = 48 / 60;
    const minimumEventMinutes = 50;
    const datePattern = /^\d{4}-\d{2}-\d{2}$/;

    function dateKey(value) {
        const date = new Date(value);
        if (!Number.isFinite(date.getTime())) return "";
        const parts = new Intl.DateTimeFormat("en-CA", { timeZone, year: "numeric", month: "2-digit", day: "2-digit" }).formatToParts(date);
        const part = type => parts.find(item => item.type === type)?.value;
        return `${part("year")}-${part("month")}-${part("day")}`;
    }

    function addDays(key, days) {
        if (!datePattern.test(key)) return "";
        const date = new Date(`${key}T12:00:00Z`);
        if (!Number.isFinite(date.getTime())) return "";
        date.setUTCDate(date.getUTCDate() + days);
        return date.toISOString().slice(0, 10);
    }

    function mondayOf(value) {
        const key = datePattern.test(String(value)) ? value : dateKey(value);
        const weekday = new Date(`${key}T12:00:00Z`).getUTCDay();
        return addDays(key, -((weekday + 6) % 7));
    }

    function localDayStart(key) {
        return new Date(`${key}T00:00:00-05:00`).getTime();
    }

    function monthOf(value) {
        const key = datePattern.test(String(value)) ? value : dateKey(value);
        return key ? `${key.slice(0, 7)}-01` : "";
    }

    function addMonths(key, months) {
        const date = new Date(`${monthOf(key)}T12:00:00Z`);
        date.setUTCMonth(date.getUTCMonth() + months);
        return date.toISOString().slice(0, 10);
    }

    function eventDay(event) {
        return datePattern.test(event.serviceDate || "") ? event.serviceDate : dateKey(event.startAtUtc);
    }

    function monthDays(events, monthStart) {
        const first = monthOf(monthStart);
        const until = addMonths(first, 1);
        const start = mondayOf(first);
        const end = addDays(mondayOf(addDays(until, -1)), 7);
        const days = [];
        for (let key = start; key < end; key = addDays(key, 1)) {
            const inMonth = key >= first && key < until;
            days.push({ key, inMonth, events: inMonth ? events.filter(event => event.id && eventDay(event) === key)
                .sort((a, b) => String(a.startAtUtc).localeCompare(String(b.startAtUtc)) || String(a.id).localeCompare(String(b.id))) : [] });
        }
        return days;
    }

    function technicianKey(event) {
        return String(event.technicianId || event.technicianName || "sin-tecnico").trim().toLowerCase();
    }

    function technicianHue(key) {
        let hash = 2166136261;
        for (const char of String(key).toLowerCase()) hash = Math.imul(hash ^ char.charCodeAt(0), 16777619);
        return (hash >>> 0) % 360;
    }

    function technicianColors(technicians) {
        const palette = [215, 28, 155, 280, 340, 185, 75, 0, 245, 110, 310, 50];
        return new Map([...new Set(technicians.map(item => String(item.id).toLowerCase()))].sort()
            .map((id, index) => [id, palette[index] ?? (index * 137.508) % 360]));
    }

    function summarize(events) {
        const completed = [...new Map(events.filter(event => event.id && (event.activityKind || "maintenance") === "maintenance"
            && ["ReadyToSend", "Historical"].includes(event.workflowState)).map(event => [event.id, event])).values()];
        const group = (key, label) => {
            const groups = new Map();
            completed.forEach(event => {
                const id = key(event);
                if (!groups.has(id)) groups.set(id, { id, label: label(event), count: 0 });
                groups.get(id).count++;
            });
            return [...groups.values()].sort((a, b) => b.count - a.count || a.label.localeCompare(b.label, "es"));
        };
        return { total: completed.length,
            technicians: group(technicianKey, event => event.technicianName || "Sin técnico"),
            types: group(event => typeInfo(event.maintenanceType).label, event => typeInfo(event.maintenanceType).label),
            clients: group(event => String(event.clientId || event.clientName || "sin-cliente").trim().toLowerCase(), event => event.clientName || "Sin cliente") };
    }

    function typeInfo(value) {
        const normalized = String(value ?? "").normalize("NFD").replace(/[\u0300-\u036f]/g, "").toLowerCase();
        if (normalized.includes("prevent")) return { label: "Preventivo", css: "preventive" };
        if (normalized.includes("correct")) return { label: "Correctivo", css: "corrective" };
        if (normalized.includes("movimiento") || normalized === "movement") return { label: "Movimiento", css: "movement" };
        if (normalized.includes("toner")) return { label: "Entrega de tóner", css: "toner" };
        if (["entrega de equipo", "retiro", "cambio", "salida interna", "recepcion interna", "recepcion de equipo"].includes(normalized)) return { label: value, css: "movement" };
        return { label: String(value || "Mantenimiento"), css: "other" };
    }

    function splitEvents(events, weekStart) {
        const days = Array.from({ length: 7 }, (_, index) => ({ key: addDays(weekStart, index), segments: [] }));
        for (const event of Array.isArray(events) ? events : []) {
            const start = Date.parse(event.startAtUtc);
            if (!event.id || !Number.isFinite(start)) continue;
            const parsedEnd = Date.parse(event.endAtUtc);
            const end = Number.isFinite(parsedEnd) && parsedEnd > start ? parsedEnd : start + 30 * 60000;
            for (const day of days) {
                const midnight = localDayStart(day.key);
                if (start >= midnight + dayMilliseconds || end <= midnight) continue;
                const startMinute = Math.max(0, (start - midnight) / 60000);
                const endMinute = Math.min(1440, (end - midnight) / 60000);
                const layoutStart = Math.min(startMinute, 1440 - minimumEventMinutes);
                day.segments.push({ event, startMinute, endMinute, layoutStart, layoutEnd: Math.min(1440, Math.max(endMinute, layoutStart + minimumEventMinutes)), column: 0, columns: 1 });
            }
            // Minimum visual slots do not alter actual visit times; late-night labels stay inside their day.
        }
        days.forEach(day => layoutOverlaps(day.segments));
        return days;
    }

    function layoutOverlaps(segments) {
        segments.sort((left, right) => left.layoutStart - right.layoutStart || right.layoutEnd - left.layoutEnd || String(left.event.id).localeCompare(String(right.event.id)));
        let cluster = [];
        let clusterEnd = -1;
        let columns = [];
        const finish = () => cluster.forEach(segment => { segment.columns = columns.length; });
        for (const segment of segments) {
            if (segment.layoutStart >= clusterEnd) {
                finish();
                cluster = [];
                columns = [];
                clusterEnd = -1;
            }
            let column = columns.findIndex(end => end <= segment.layoutStart);
            if (column < 0) column = columns.length;
            columns[column] = segment.layoutEnd;
            segment.column = column;
            cluster.push(segment);
            clusterEnd = Math.max(clusterEnd, segment.layoutEnd);
        }
        finish();
        return segments;
    }

    function safeAppUrl(value, origin) {
        if (typeof value !== "string" || !value.trim()) return "";
        try {
            const url = new URL(value, origin);
            return url.origin === origin && /^\/CopiersMtoV2Calendar\//i.test(url.pathname) && !url.username && !url.password
                ? url.href : "";
        } catch { return ""; }
    }

    function validLocation(location) {
        return Boolean(location && typeof location.latitude === "number" && Number.isFinite(location.latitude)
            && typeof location.longitude === "number" && Number.isFinite(location.longitude)
            && Math.abs(location.latitude) <= 90 && Math.abs(location.longitude) <= 180);
    }

    function mapUrl(location) {
        if (!validLocation(location)) return "";
        const latitude = location.latitude;
        const longitude = location.longitude;
        const span = 0.008;
        const bbox = [Math.max(-180, longitude - span), Math.max(-90, latitude - span), Math.min(180, longitude + span), Math.min(90, latitude + span)];
        const url = new URL("https://www.openstreetmap.org/export/embed.html");
        url.searchParams.set("bbox", bbox.join(","));
        url.searchParams.set("layer", "mapnik");
        url.searchParams.set("marker", `${latitude},${longitude}`);
        return url.href;
    }

    function stateLabel(value, kind) {
        if (value === "Historical") return "Histórico migrado";
        if (value === "NotApplicable") return "No aplica: histórico";
        const states = kind === "email"
            ? { NotRequired: "No aplica · registro interno", NotReady: "No preparado", Pending: "Pendiente", Processing: "En proceso", Sent: "Enviado", Failed: "Fallido" }
            : { Draft: "Borrador", Finalizing: "Finalizando", ReadyToSend: "Finalizado", Failed: "Pendiente de finalizar" };
        return states[value] || String(value || "No informado");
    }

    const helpers = { dateKey, addDays, mondayOf, monthOf, addMonths, monthDays, technicianHue, technicianColors, summarize, splitEvents, layoutOverlaps, typeInfo, safeAppUrl, validLocation, mapUrl, stateLabel };
    if (typeof module === "object" && module.exports) module.exports = helpers;
    if (!global.document) return;

    const document = global.document;
    const root = document.getElementById("copiersMtoV2Calendar");
    if (!root) return;
    const byId = id => document.getElementById(id);
    const controls = {
        technician: byId("mtoCalendarTechnician"), previous: byId("mtoCalendarPrevious"), next: byId("mtoCalendarNext"),
        today: byId("mtoCalendarToday"), refresh: byId("mtoCalendarRefresh"), range: byId("mtoCalendarRange"),
        status: byId("mtoCalendarStatus"), viewport: byId("mtoCalendarViewport"), grid: byId("mtoCalendarGrid"),
        week: byId("mtoCalendarWeek"), month: byId("mtoCalendarMonth"), summary: byId("mtoCalendarSummary"), legend: byId("mtoCalendarLegend"), slotNote: byId("mtoCalendarSlotNote"),
        dialog: byId("mtoCalendarDetail"), detail: byId("mtoCalendarDetailBody"), title: byId("mtoCalendarDetailTitle"), close: byId("mtoCalendarDetailClose")
    };
    const state = { activated: false, activating: false, weekLoading: false, bootstrap: null, bootstrapPromise: null, view: "month", anchor: dateKey(new Date()), weekStart: mondayOf(new Date()), generation: 0, detailGeneration: 0, weekController: null, detailController: null, focusReturn: null, positioned: false };
    const timeFormatter = new Intl.DateTimeFormat("es-CO", { timeZone, hour: "2-digit", minute: "2-digit", hourCycle: "h23" });
    const dateFormatter = new Intl.DateTimeFormat("es-CO", { timeZone, day: "numeric", month: "short", year: "numeric" });
    const dayFormatter = new Intl.DateTimeFormat("es-CO", { timeZone, weekday: "short", day: "numeric" });
    const monthFormatter = new Intl.DateTimeFormat("es-CO", { timeZone, month: "long", year: "numeric" });
    let technicianPalette = new Map();

    function colorize(node, key) {
        node.style.setProperty("--mto-tech-hue", technicianPalette.get(key) ?? technicianHue(key));
    }

    function renderSummary(events, loading = false) {
        const summary = summarize(events);
        const cards = [["Por técnico", summary.technicians], ["Por tipo de mto.", summary.types], ["Por cliente", summary.clients]];
        controls.summary.replaceChildren(...cards.map(([title, groups], index) => {
            const card = element("section", "mto-calendar__summary-card");
            card.append(element("h4", "", title), element("strong", "mto-calendar__summary-total", loading ? "—" : summary.total), element("span", "mto-calendar__muted", " mantenimientos"));
            const list = element("ul", "mto-calendar__summary-list");
            groups.forEach(group => {
                const row = element("li", "mto-calendar__summary-row");
                const label = element("span", index === 0 ? "mto-calendar__technician-label" : "", group.label);
                if (index === 0) colorize(label, group.id);
                row.append(label, element("strong", "", group.count));
                list.append(row);
            });
            if (!groups.length) list.append(element("li", "mto-calendar__muted", loading ? "Consultando…" : "Sin mantenimientos finalizados"));
            card.append(list);
            return card;
        }));
        const technicians = new Map();
        events.forEach(event => technicians.set(technicianKey(event), event.technicianName || "Sin técnico"));
        const legend = [...technicians].sort((a, b) => a[1].localeCompare(b[1], "es")).map(([key, name]) => {
            const label = element("span", "mto-calendar__technician-label", name);
            colorize(label, key);
            return label;
        });
        controls.legend.replaceChildren(...legend, element("span", "mto-calendar__muted", "Hora de Colombia (UTC−5)"));
    }

    function renderMonth(events) {
        const days = monthDays(events, monthOf(state.anchor));
        const today = dateKey(new Date());
        const header = element("div", "mto-calendar__month-headings");
        ["Lunes", "Martes", "Miércoles", "Jueves", "Viernes", "Sábado", "Domingo"].forEach(label => header.append(element("div", "", label)));
        const body = element("div", "mto-calendar__month-weeks");
        for (let offset = 0; offset < days.length; offset += 7) {
            const week = element("div", "mto-calendar__month-week");
            week.setAttribute("aria-label", `Semana del ${dateFormatter.format(new Date(`${days[offset].key}T12:00:00Z`))}`);
            days.slice(offset, offset + 7).forEach(day => {
                const cell = element("section", "mto-calendar__month-day");
                if (!day.inMonth) cell.classList.add("is-outside");
                if (day.key === today) cell.classList.add("is-today");
                cell.setAttribute("aria-label", dateFormatter.format(new Date(`${day.key}T12:00:00Z`)));
                const heading = element("div", "mto-calendar__month-date");
                heading.append(element("span", "", Number(day.key.slice(-2))));
                if (day.events.length) heading.append(element("small", "", `${day.events.length} ${day.events.length === 1 ? "registro" : "registros"}`));
                cell.append(heading);
                const more = day.events.length > 3 ? element("details", "mto-calendar__more") : null;
                if (more) more.append(element("summary", "", `Ver ${day.events.length - 3} más`));
                day.events.forEach((event, index) => {
                    const button = element("button", "mto-calendar__event mto-calendar__event--month");
                    button.type = "button";
                    button.dataset.mtoEventId = event.id;
                    colorize(button, technicianKey(event));
                    if (event.workflowState === "Failed") button.classList.add("is-pending");
                    const type = typeInfo(event.maintenanceType).label;
                    const time = event.workflowState === "Historical" ? "Histórico" : timeFormatter.format(new Date(event.startAtUtc));
                    const description = [event.clientName || "Cliente sin nombre", type, event.technicianName, time, event.workflowState === "Failed" ? "Pendiente de finalizar" : "", event.serviceReference].filter(Boolean).join(" · ");
                    button.title = description;
                    button.setAttribute("aria-label", `${description}. Abrir detalle`);
                    button.append(element("strong", "mto-calendar__event-client", event.clientName || "Cliente sin nombre"), element("span", "mto-calendar__event-type", `${type}${event.workflowState === "Failed" ? " · Pendiente" : ""}`), element("span", "mto-calendar__event-time", `${time} · ${event.technicianName || "Sin técnico"}`));
                    button.addEventListener("click", () => openDetail(event, button));
                    (index >= 3 && more ? more : cell).append(button);
                });
                if (more) cell.append(more);
                week.append(cell);
            });
            body.append(week);
        }
        controls.grid.replaceChildren(header, body);
        controls.range.textContent = monthFormatter.format(new Date(`${monthOf(state.anchor)}T12:00:00Z`));
        controls.viewport.scrollTop = 0;
    }

    function renderCalendar(events, loading = false) {
        const monthly = state.view === "month";
        controls.week.setAttribute("aria-pressed", String(!monthly));
        controls.month.setAttribute("aria-pressed", String(monthly));
        controls.viewport.classList.toggle("is-month", monthly);
        controls.grid.classList.toggle("is-month", monthly);
        controls.slotNote.hidden = monthly;
        controls.previous.setAttribute("aria-label", monthly ? "Mes anterior" : "Semana anterior");
        controls.next.setAttribute("aria-label", monthly ? "Mes siguiente" : "Semana siguiente");
        controls.viewport.setAttribute("aria-label", monthly ? "Calendario mensual por semanas. Desplaza horizontalmente para ver todos los días." : "Calendario semanal de 24 horas. Desplaza para ver todos los días y horas.");
        if (monthly) renderMonth(events); else renderWeek(events);
        renderSummary(events, loading);
    }

    function element(tag, className, text) {
        const node = document.createElement(tag);
        if (className) node.className = className;
        if (text !== undefined && text !== null) node.textContent = String(text);
        return node;
    }

    function formatInstant(value) {
        const date = new Date(value);
        return value && Number.isFinite(date.getTime()) ? `${dateFormatter.format(date)}, ${timeFormatter.format(date)}` : "No registrado";
    }

    function setStatus(message, error = false) {
        controls.status.textContent = message;
        controls.status.classList.toggle("is-error", error);
    }

    async function getJson(endpoint, parameters, signal) {
        const target = safeAppUrl(endpoint, global.location.origin);
        if (!target) throw new Error("La ruta del calendario no está configurada.");
        const url = new URL(target);
        Object.entries(parameters || {}).forEach(([key, value]) => url.searchParams.set(key, value));
        const response = await global.fetch(url.href, { credentials: "same-origin", headers: { Accept: "application/json" }, signal, cache: "no-store" });
        if (response.status === 401 || response.status === 403) throw new Error("Tu sesión no tiene acceso al calendario. Actualiza la página o solicita acceso.");
        if (response.redirected || !(response.headers.get("content-type") || "").includes("application/json")) throw new Error("La sesión expiró. Actualiza la página para volver a autenticarte.");
        let result;
        try { result = await response.json(); } catch { throw new Error("No fue posible leer la respuesta del calendario."); }
        if (!response.ok) throw new Error(result?.message || result?.error || "No fue posible consultar los mantenimientos. Intenta actualizar.");
        return result;
    }

    function renderWeek(events) {
        const days = splitEvents(events, state.weekStart);
        const currentDay = dateKey(new Date());
        const header = element("div", "mto-calendar__days");
        header.append(element("div", "mto-calendar__timezone", "UTC−5"));
        days.forEach(day => {
            const heading = element("div", "mto-calendar__day-heading", dayFormatter.format(new Date(`${day.key}T12:00:00Z`)));
            if (day.key === currentDay) heading.classList.add("is-today");
            header.append(heading);
        });
        const body = element("div", "mto-calendar__body");
        const hours = element("div", "mto-calendar__hours");
        for (let hour = 0; hour < 24; hour++) hours.append(element("span", "mto-calendar__hour", `${String(hour).padStart(2, "0")}:00`));
        body.append(hours);
        days.forEach(day => {
            const column = element("div", "mto-calendar__day");
            column.setAttribute("aria-label", dayFormatter.format(new Date(`${day.key}T12:00:00Z`)));
            if (day.key === currentDay) column.classList.add("is-today");
            day.segments.forEach(segment => {
                const event = segment.event;
                const type = typeInfo(event.maintenanceType);
                const button = element("button", `mto-calendar__event mto-calendar__event--${type.css}`);
                colorize(button, technicianKey(event));
                button.type = "button";
                button.dataset.mtoEventId = event.id;
                button.style.top = `${segment.layoutStart * minuteHeight}px`;
                button.style.height = `${(segment.layoutEnd - segment.layoutStart) * minuteHeight - 3}px`;
                button.style.left = `calc(${segment.column * 100 / segment.columns}% + 2px)`;
                button.style.width = `calc(${100 / segment.columns}% - 4px)`;
                const end = Number.isFinite(Date.parse(event.endAtUtc)) ? event.endAtUtc : event.startAtUtc;
                const time = `${timeFormatter.format(new Date(event.startAtUtc))}–${timeFormatter.format(new Date(end))}`;
                const pending = event.workflowState === "Failed";
                if (pending) button.classList.add("is-pending");
                const technician = controls.technician.value === "all" ? event.technicianName : "";
                const description = [event.clientName || "Cliente sin nombre", type.label, technician, event.serviceReference, time, pending ? "Pendiente de finalizar" : "", event.durationEstimated ? event.timingNote || "Duración estimada" : ""].filter(Boolean).join(" · ");
                button.setAttribute("aria-label", `${description}. Abrir detalle`);
                button.title = description;
                button.append(element("strong", "mto-calendar__event-client", event.clientName || "Cliente sin nombre"), element("span", "mto-calendar__event-type", [type.label, pending ? "Pendiente" : "", technician].filter(Boolean).join(" · ")), element("span", "mto-calendar__event-time", time));
                button.addEventListener("click", () => openDetail(event, button));
                column.append(button);
            });
            body.append(column);
        });
        controls.grid.replaceChildren(header, body);
        controls.range.textContent = `${dateFormatter.format(new Date(`${state.weekStart}T12:00:00Z`))} – ${dateFormatter.format(new Date(`${addDays(state.weekStart, 6)}T12:00:00Z`))}`;
        if (!state.positioned) {
            const segments = days.flatMap(day => day.segments);
            const firstMinute = segments.length ? Math.min(...segments.map(segment => segment.layoutStart)) : 7 * 60;
            controls.viewport.scrollTop = Math.max(0, firstMinute * minuteHeight - 24);
            state.positioned = true;
        }
    }

    async function initialize() {
        if (state.bootstrap) return state.bootstrap;
        if (state.bootstrapPromise) return state.bootstrapPromise;
        setStatus("Cargando técnicos…");
        controls.refresh.disabled = true;
        state.bootstrapPromise = getJson(root.dataset.bootstrapUrl).then(result => {
            const technicians = Array.isArray(result.technicians) ? result.technicians.filter(item => item.id) : [];
            // Use the full roster, so changing month or technician never reassigns colors.
            technicianPalette = technicianColors(technicians);
            controls.technician.replaceChildren(element("option", "", "Selecciona un técnico"));
            controls.technician.firstElementChild.value = "";
            if (technicians.length) {
                const all = element("option", "", "Todos los técnicos");
                all.value = "all";
                controls.technician.append(all);
            }
            technicians.forEach(technician => {
                const option = element("option", "", technician.name || technician.email || "Técnico");
                option.value = technician.id;
                controls.technician.append(option);
            });
            controls.technician.disabled = technicians.length === 0;
            const preferred = technicians.find(item => item.id === result.defaultTechnicianId) || technicians[0];
            controls.technician.value = result.defaultTechnicianId === "all" && technicians.length ? "all" : preferred?.id || "";
            state.bootstrap = result;
            return result;
        }).finally(() => { state.bootstrapPromise = null; controls.refresh.disabled = false; });
        return state.bootstrapPromise;
    }

    async function loadWeek() {
        const generation = ++state.generation;
        state.weekLoading = true;
        state.weekController?.abort();
        state.weekController = new AbortController();
        root.setAttribute("aria-busy", "true");
        renderCalendar([], true);
        try {
            await initialize();
            if (generation !== state.generation) return;
            const technicianId = controls.technician.value;
            if (!technicianId) {
                renderSummary([]);
                setStatus(controls.technician.disabled ? "Aún no hay técnicos con mantenimientos V2 registrados." : "Selecciona un técnico para consultar el calendario.");
                return;
            }
            const monthly = state.view === "month";
            const period = monthly ? "este mes" : "esta semana";
            setStatus(`Consultando mantenimientos de ${period}…`);
            const result = await getJson(monthly ? root.dataset.monthUrl : root.dataset.weekUrl,
                monthly ? { technicianId, monthStart: monthOf(state.anchor) } : { technicianId, weekStart: state.weekStart }, state.weekController.signal);
            if (generation !== state.generation) return;
            if (datePattern.test(result.weekStart || "")) state.weekStart = result.weekStart;
            const events = Array.isArray(result.events) ? result.events : [];
            state.positioned = false;
            renderCalendar(events);
            const estimated = events.some(event => event.durationEstimated);
            const activities = state.bootstrap?.activitiesEnabled === true;
            const noun = activities ? events.length === 1 ? "actividad" : "actividades" : events.length === 1 ? "mantenimiento" : "mantenimientos";
            setStatus(events.length ? `${events.length} ${noun} en ${period}.${estimated ? " Algunos registros tienen duración estimada; consulta el detalle." : ""}`
                : `${technicianId === "all" ? "No hay" : "Este técnico no tiene"} ${activities ? "actividades" : "mantenimientos V2"} en ${period}.`);
        } catch (error) {
            if (error.name !== "AbortError" && generation === state.generation) {
                controls.summary.replaceChildren(element("p", "mto-calendar__muted", "Cantidades no disponibles. Actualiza para reintentar."));
                setStatus(error.message || "No fue posible cargar el calendario.", true);
            }
        } finally {
            if (generation === state.generation) {
                state.weekLoading = false;
                root.setAttribute("aria-busy", "false");
            }
        }
    }

    function detailSection(title) {
        const section = element("section", "mto-calendar-detail__section");
        section.append(element("h3", "", title));
        return section;
    }

    function detailFields(section, fields) {
        const list = element("dl", "mto-calendar-detail__fields");
        fields.forEach(([label, value]) => {
            if (value === undefined || value === null || value === "") return;
            const pair = element("div", "mto-calendar-detail__field");
            pair.append(element("dt", "", label), element("dd", "", typeof value === "boolean" ? value ? "Sí" : "No" : value));
            list.append(pair);
        });
        section.append(list);
    }

    function attachmentLink(url, label) {
        const safe = safeAppUrl(url, global.location.origin);
        if (!safe) return element("span", "mto-calendar__muted", "Archivo no disponible");
        const link = element("a", "mto-calendar-detail__file-link", label);
        link.href = safe;
        link.target = "_blank";
        link.rel = "noopener noreferrer";
        return link;
    }

    function renderLocation(location) {
        const section = detailSection("Ubicación de cierre · Uso interno");
        if (!validLocation(location)) {
            section.append(element("p", "mto-calendar__muted", "No se registró una ubicación para este mantenimiento."));
            return section;
        }
        detailFields(section, [["Coordenadas", `${location.latitude.toFixed(6)}, ${location.longitude.toFixed(6)}`], ["Precisión", typeof location.accuracyMeters === "number" ? `± ${Math.round(location.accuracyMeters).toLocaleString("es-CO")} m` : "No informada"], ["Capturada", formatInstant(location.capturedAtUtc)]]);
        const container = element("div", "mto-calendar-detail__map-placeholder");
        const notice = element("p", "mto-calendar__muted", "Al cargar el mapa se comparte esta ubicación con OpenStreetMap. El mapa no se consulta automáticamente.");
        const button = element("button", "btn btn-outline-primary", "Cargar mapa");
        button.type = "button";
        button.addEventListener("click", () => {
            const frame = element("iframe", "mto-calendar-detail__map");
            frame.title = "Mapa de la ubicación capturada al cerrar el mantenimiento";
            frame.referrerPolicy = "no-referrer";
            frame.loading = "lazy";
            frame.setAttribute("sandbox", "allow-scripts allow-same-origin allow-popups");
            frame.src = mapUrl(location);
            container.replaceChildren(frame);
        }, { once: true });
        container.append(notice, button);
        section.append(container);
        return section;
    }

    function renderDetail(detail) {
        const historical = detail.workflowState === "Historical";
        const fragment = document.createDocumentFragment();
        const summary = detailSection("Datos de la visita");
        detailFields(summary, [["Consecutivo", detail.serviceReference], ["Cliente", detail.clientName], ["Tipo", typeInfo(detail.maintenanceType).label], ["Técnico", detail.technicianName], ["Correo del técnico", detail.technicianEmail], ["Serial del equipo", detail.equipmentSerial], ["Persona que atiende", detail.clientContactName], ["Correo de envío", detail.clientEmail], ["Dirección o sede", detail.serviceAddress], ["Fecha del servicio", detail.serviceDate], ["Hora de entrada", historical ? "No registrada" : formatInstant(detail.startAtUtc)], ["Hora de salida", historical ? "No registrada" : formatInstant(detail.endAtUtc)], ["Firma registrada", formatInstant(detail.deviceSignedAtUtc)], ["Finalización del servidor", formatInstant(detail.serverFinalizedAtUtc)], ["Estado del reporte", stateLabel(detail.workflowState, "workflow")], ["Estado del correo", stateLabel(detail.emailState, "email")], ["Título", detail.title]]);
        if (detail.durationEstimated) summary.append(element("p", "mto-calendar-detail__note", detail.timingNote || "La duración de esta franja es estimada: no se registraron ambas horas de la visita."));
        fragment.append(summary);
        if (Array.isArray(detail.movementDetails) && detail.movementDetails.length) {
            const movement = detailSection("Movimiento · uso interno");
            detailFields(movement, detail.movementDetails.map(x => [x.label, x.value]));
            if (/^activity:[0-9a-f-]{36}$/i.test(detail.relatedActivityId || "")) {
                const link = element("a", "btn btn-outline-primary", "Abrir salida / recepción relacionada");
                link.href = "/Dashboard?tab=copiers&copiersTab=maintenance-v2&maintenanceId=" + encodeURIComponent(detail.relatedActivityId);
                movement.append(link);
            }
            fragment.append(movement);
        }
        const work = detailSection("Formulario y trabajo realizado");
        detailFields(work, [["Trabajo realizado", detail.workPerformed], ["Observaciones del cliente", detail.customerObservations], ["Notas internas", detail.internalNotes]]);
        const answers = Array.isArray(detail.answers) ? detail.answers : [];
        detailFields(work, answers.filter(answer => !String(answer.key).startsWith("equipment_item_") && !["service_started_at_utc", "service_ended_at_utc"].includes(answer.key) && answer.value !== undefined && answer.value !== null && answer.value !== "").map(answer => [answer.label || answer.key, answer.value]));
        fragment.append(work);
        const signature = detailSection("Conformidad del cliente");
        detailFields(signature, [["Nombre del firmante", detail.signerName], ["Cargo o relación", detail.signerRole], ["Aceptó el reporte", detail.customerAccepted]]);
        const signatureUrl = safeAppUrl(detail.signatureUrl, global.location.origin);
        if (signatureUrl) {
            const image = element("img", "mto-calendar-detail__signature");
            image.alt = "Firma del cliente guardada con este mantenimiento";
            image.loading = "lazy";
            image.src = signatureUrl;
            signature.append(image);
        }
        if (!detail.internalOperation && !historical) fragment.append(signature);
        const report = detailSection(detail.workflowState === "Historical" ? "Documento histórico original" : "Reporte firmado y adjuntos");
        const reportUrl = safeAppUrl(detail.reportUrl, global.location.origin);
        if (reportUrl) {
            report.append(attachmentLink(reportUrl, historical ? "Abrir documento original ↗" : "Abrir o descargar PDF firmado ↗"));
            const frame = element("iframe", "mto-calendar-detail__pdf");
            frame.title = detail.workflowState === "Historical" ? "Documento histórico original" : "PDF del mantenimiento con la firma del cliente";
            frame.loading = "lazy";
            frame.src = reportUrl;
            report.append(frame);
        } else report.append(element("p", "mto-calendar__muted", historical ? "El registro original no tenía documento adjunto." : detail.internalOperation ? "Registro interno: no requiere certificado firmado ni correo al cliente." : "Este mantenimiento aún no tiene un PDF firmado disponible."));
        const files = element("ul", "mto-calendar-detail__files");
        (Array.isArray(detail.evidences) ? detail.evidences : []).forEach(evidence => {
            const row = element("li", "");
            row.append(attachmentLink(evidence.url, evidence.fileName || "Adjunto"));
            const kilobytes = Number.isFinite(evidence.sizeBytes) ? `${Math.max(1, Math.round(evidence.sizeBytes / 1024))} KB` : "";
            const purpose = { HistoricalDocument: "Documento histórico original", SignedReport: "PDF firmado", Signature: "Firma del cliente", OriginalAttachment: "Adjunto original", CustomerAttachment: "Copia para el cliente" }[evidence.purpose] || "Evidencia";
            row.append(element("span", "mto-calendar__muted", [purpose, evidence.contentType, kilobytes].filter(Boolean).join(" · ")));
            files.append(row);
        });
        if (files.childElementCount) report.append(files);
        fragment.append(report, renderLocation(detail.location));
        controls.detail.replaceChildren(fragment);
    }

    async function openDetail(event, button) {
        const generation = ++state.detailGeneration;
        state.detailController?.abort();
        state.detailController = new AbortController();
        state.focusReturn = button;
        controls.title.textContent = [event.serviceReference, event.clientName].filter(Boolean).join(" · ") || "Detalle del mantenimiento";
        controls.detail.replaceChildren(element("p", "mto-calendar__status", "Cargando el reporte, sus datos y evidencias…"));
        if (!controls.dialog.open) controls.dialog.showModal();
        controls.close.focus();
        try {
            const detail = await getJson(root.dataset.detailUrl, { id: event.id }, state.detailController.signal);
            if (generation !== state.detailGeneration || !controls.dialog.open) return;
            renderDetail(detail);
        } catch (error) {
            if (error.name === "AbortError" || generation !== state.detailGeneration) return;
            const message = element("p", "mto-calendar__status is-error", error.message || "No fue posible abrir este mantenimiento.");
            const retry = element("button", "btn btn-outline-primary", "Reintentar");
            retry.type = "button";
            retry.addEventListener("click", () => openDetail(event, button));
            controls.detail.replaceChildren(message, retry);
        }
    }

    function closeDetail() {
        state.detailGeneration++;
        state.detailController?.abort();
        controls.dialog.close();
        controls.detail.replaceChildren();
        if (state.focusReturn?.isConnected) state.focusReturn.focus();
    }

    controls.close.addEventListener("click", closeDetail);
    controls.dialog.addEventListener("cancel", event => { event.preventDefault(); closeDetail(); });
    controls.dialog.addEventListener("click", event => {
        if (event.target !== controls.dialog) return;
        const bounds = controls.dialog.getBoundingClientRect();
        if (event.clientX < bounds.left || event.clientX > bounds.right || event.clientY < bounds.top || event.clientY > bounds.bottom) closeDetail();
    });
    controls.technician.addEventListener("change", () => { state.positioned = false; loadWeek(); });
    function navigate(direction) {
        state.anchor = state.view === "month" ? addMonths(state.anchor, direction) : addDays(state.weekStart, direction * 7);
        state.weekStart = mondayOf(state.anchor);
        loadWeek();
    }
    function switchView(view) {
        if (view === state.view) return;
        state.view = view;
        state.positioned = false;
        loadWeek();
    }
    controls.week.addEventListener("click", () => switchView("week"));
    controls.month.addEventListener("click", () => switchView("month"));
    controls.previous.addEventListener("click", () => navigate(-1));
    controls.next.addEventListener("click", () => navigate(1));
    controls.today.addEventListener("click", () => { state.anchor = dateKey(new Date()); state.weekStart = mondayOf(state.anchor); loadWeek(); });
    controls.refresh.addEventListener("click", loadWeek);
    async function openLinkedMaintenance(id) {
        try {
            await initialize();
            const detail = await getJson(root.dataset.detailUrl, { id });
            if (detail.technicianId && Array.from(controls.technician.options).some(option => option.value === detail.technicianId)) {
                controls.technician.value = detail.technicianId;
            }
            if (datePattern.test(String(detail.serviceDate || "").slice(0, 10))) state.anchor = detail.serviceDate.slice(0, 10);
            else if (Number.isFinite(Date.parse(detail.startAtUtc))) state.anchor = dateKey(detail.startAtUtc);
            state.weekStart = mondayOf(state.anchor);
            await loadWeek();
            controls.title.textContent = [detail.serviceReference, detail.clientName].filter(Boolean).join(" · ") || "Detalle del mantenimiento";
            renderDetail(detail);
            state.focusReturn = controls.technician;
            controls.dialog.showModal();
            controls.close.focus();
        } catch (error) {
            setStatus(error.message || "No fue posible abrir el mantenimiento solicitado.", true);
        }
    }

    global.CopiersMtoV2Calendar = {
        activate() {
            // A technician can complete a visit in another tab while this dashboard
            // remains open. Re-entering the calendar must read that week's current
            // Dataverse rows, not retain its first snapshot for the page lifetime.
            if (state.activating || state.weekLoading || controls.dialog.open) return;
            if (state.activated) {
                void loadWeek();
                return;
            }
            state.activated = true;
            state.activating = true;
            const parameters = new URLSearchParams(global.location.search);
            const maintenanceId = parameters.get("maintenanceId") || "";
            const operation = /^(?:activity:)?[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(maintenanceId)
                ? openLinkedMaintenance(maintenanceId) : loadWeek();
            void operation.finally(() => { state.activating = false; });
        }
    };
})(typeof window !== "undefined" ? window : globalThis);
