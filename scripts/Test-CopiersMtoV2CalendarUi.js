"use strict";

const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const vm = require("node:vm");
const { test } = require("node:test");
const projectRoot = path.resolve(__dirname, "..");
const source = fs.readFileSync(path.join(projectRoot, "wwwroot/js/copiers-mto-v2-calendar.js"), "utf8");
const view = fs.readFileSync(path.join(projectRoot, "Views/Dashboard/Index.cshtml"), "utf8");
const dashboard = fs.readFileSync(path.join(projectRoot, "wwwroot/js/dashboard.js"), "utf8");
const calendar = require("../wwwroot/js/copiers-mto-v2-calendar.js");
const origin = "https://example.test";
const eventId = "11111111-2222-3333-4444-555555555555";

function event(id, start, end) {
    return { id, startAtUtc: `2026-09-08T${start}:00-05:00`, endAtUtc: `2026-09-08T${end}:00-05:00` };
}

class FakeElement {
    constructor(tag = "div") {
        this.tagName = tag.toUpperCase(); this.children = []; this.attributes = {}; this.dataset = {}; this.style = {};
        this.listeners = {}; this.text = ""; this.value = ""; this.open = false; this.isConnected = true;
        const classes = new Set();
        this.classList = { add: value => classes.add(value), contains: value => classes.has(value), toggle: (value, active) => active ? classes.add(value) : classes.delete(value) };
    }
    get textContent() { return this.text + this.children.map(child => child.textContent).join(""); }
    set textContent(value) { this.text = String(value); this.children = []; }
    get firstElementChild() { return this.children[0]; }
    get childElementCount() { return this.children.length; }
    get options() { return this.children; }
    append(...children) { this.children.push(...children.flatMap(child => child.tagName === "FRAGMENT" ? child.children : [child])); }
    replaceChildren(...children) { this.text = ""; this.children = []; this.append(...children); }
    setAttribute(name, value) { this.attributes[name] = value; }
    addEventListener(name, listener) { (this.listeners[name] ||= []).push(listener); }
    click() { (this.listeners.click || []).forEach(listener => listener({ target: this })); }
    focus() { this.focused = true; }
    showModal() { this.open = true; }
    close() { this.open = false; }
    getBoundingClientRect() { return { left: 0, top: 0, right: 100, bottom: 100 }; }
}

function descendants(node) { return [node, ...node.children.flatMap(descendants)]; }
async function settle() { for (let index = 0; index < 7; index++) await new Promise(resolve => setImmediate(resolve)); }

function harness(options = {}) {
    const ids = new Map();
    for (const match of view.matchAll(/id="(copiersMtoV2Calendar|mtoCalendar[^"]+)"/g)) ids.set(match[1], new FakeElement(match[1] === "mtoCalendarTechnician" ? "select" : "div"));
    const root = ids.get("copiersMtoV2Calendar");
    root.dataset = { bootstrapUrl: "/CopiersMtoV2Calendar/Bootstrap", weekUrl: "/CopiersMtoV2Calendar/Week", detailUrl: "/CopiersMtoV2Calendar/Detail" };
    const calls = [];
    const fixtureDetail = {
        id: eventId, serviceReference: "MTO-001234", clientName: "Cliente de prueba", technicianId: "tech-1", technicianName: "Técnico de prueba", technicianEmail: "tech@example.test",
        maintenanceType: "Preventivo", workflowState: "ReadyToSend", emailState: "Sent", serviceDate: "2026-09-08", startAtUtc: "2026-09-08T13:00:00Z", endAtUtc: "2026-09-08T14:00:00Z",
        deviceSignedAtUtc: "2026-09-08T14:00:00Z", serverFinalizedAtUtc: "2026-09-08T14:01:00Z", signerName: "Cliente firmante", signerRole: "Encargado", customerAccepted: true,
        workPerformed: "Revisión completa", customerObservations: "Todo correcto", internalNotes: "Nota interna", serviceAddress: "Sede norte", answers: [{ key: "test", label: "Campo diligenciado", value: "Valor del campo" }],
        evidences: [{ evidenceKey: "abc", purpose: "CustomerAttachment", fileName: "imagen.jpg", contentType: "image/jpeg", sizeBytes: 1024, url: "/CopiersMtoV2Calendar/Evidence?id=1&evidenceKey=abc" }],
        reportUrl: "/CopiersMtoV2Calendar/Evidence?id=1&evidenceKey=pdf", signatureUrl: "/CopiersMtoV2Calendar/Evidence?id=1&evidenceKey=signature",
        location: { latitude: 4.7, longitude: -74.1, accuracyMeters: 12, capturedAtUtc: "2026-09-08T14:00:00Z" }, ...options.detail
    };
    const window = {
        document: { getElementById: id => ids.get(id), createElement: tag => new FakeElement(tag), createDocumentFragment: () => new FakeElement("fragment") },
        location: { origin, search: options.search || "" },
        fetch: async (href, request) => {
            const url = new URL(href); calls.push({ url, request });
            if (options.fetch) return options.fetch(url, request);
            let result;
            if (url.pathname.endsWith("/Bootstrap")) result = { technicians: options.noTechnicians ? [] : [{ id: "tech-1", name: "Técnico de prueba" }], defaultTechnicianId: options.allTechnicians ? "all" : "tech-1", activitiesEnabled: options.activitiesEnabled === true };
            else if (url.pathname.endsWith("/Detail")) result = fixtureDetail;
            else result = { weekStart: url.searchParams.get("weekStart"), events: options.noEvents ? [] : [{ id: eventId, clientName: "Cliente de prueba", technicianName: "Técnico de prueba", maintenanceType: "Preventivo", serviceReference: "MTO-001234", startAtUtc: `${url.searchParams.get("weekStart")}T${options.startTime || "13:00"}:00Z`, endAtUtc: `${url.searchParams.get("weekStart")}T${options.endTime || "14:00"}:00Z`, workflowState: options.failed ? "Failed" : "ReadyToSend" }] };
            return { status: 200, ok: true, redirected: false, headers: { get: () => "application/json" }, json: async () => result };
        }
    };
    vm.runInNewContext(source, { window, URL, URLSearchParams, AbortController, Intl, Date, console });
    return { ids, root, calls, window, detail: fixtureDetail, async activate() { window.CopiersMtoV2Calendar.activate(); await settle(); }, events() { return descendants(ids.get("mtoCalendarGrid")).filter(node => node.dataset.mtoEventId); } };
}

test("dashboard contains the isolated maintenance-v2 tab and resources", () => {
    assert.match(view, /data-copiers-subtab="maintenance-v2">Mantenimiento V2/);
    assert.match(view, /data-copiers-subpanel="maintenance-v2" hidden/);
    assert.ok(view.indexOf('src="~/js/copiers-mto-v2-calendar.js"') < view.indexOf('src="~/js/dashboard.js"'));
    assert.match(dashboard, /state\.copiersSubtab === "maintenance-v2"[\s\S]{0,110}CopiersMtoV2Calendar\?\.activate\(\);[\s\S]{0,30}return;/);
});

test("date keys always use Colombia timezone, independent of browser timezone", () => {
    assert.equal(calendar.dateKey("2026-09-08T03:59:00Z"), "2026-09-07");
    assert.equal(calendar.dateKey("2026-09-08T05:00:00Z"), "2026-09-08");
    assert.equal(calendar.dateKey("bad-date"), "");
});

test("Monday weeks and year transitions are calendar dates, not locale parsing", () => {
    assert.equal(calendar.mondayOf("2026-09-13"), "2026-09-07");
    assert.equal(calendar.mondayOf("2026-09-07"), "2026-09-07");
    assert.equal(calendar.addDays("2026-12-28", 7), "2027-01-04");
    assert.equal(calendar.mondayOf("2026-09-14T01:00:00Z"), "2026-09-07");
});

test("overlapping visits get independent visible columns", () => {
    const day = calendar.splitEvents([event("a", "08:00", "10:00"), event("b", "08:30", "09:30"), event("c", "09:00", "11:00")], "2026-09-07")[1];
    assert.equal(day.segments.length, 3);
    assert.deepEqual(day.segments.map(item => item.column), [0, 1, 2]);
    assert.ok(day.segments.every(item => item.columns === 3));
});

test("non-overlapping clusters regain full width", () => {
    const day = calendar.splitEvents([event("a", "08:00", "09:00"), event("b", "08:30", "09:30"), event("c", "11:00", "12:00")], "2026-09-07")[1];
    assert.deepEqual(day.segments.map(item => item.columns), [2, 2, 1]);
});

test("short visits reserve visual collision space to avoid hidden labels", () => {
    const day = calendar.splitEvents([event("a", "08:00", "08:01"), event("b", "08:05", "08:10")], "2026-09-07")[1];
    assert.ok(day.segments.every(item => item.columns === 2));
    assert.equal(day.segments[0].layoutEnd - day.segments[0].startMinute, 50);
});

test("cross-midnight events split into their actual daily intervals", () => {
    const days = calendar.splitEvents([{ id: "overnight", startAtUtc: "2026-09-08T23:30:00-05:00", endAtUtc: "2026-09-09T01:00:00-05:00" }], "2026-09-07");
    assert.equal(days[1].segments[0].startMinute, 1410);
    assert.equal(days[1].segments[0].endMinute, 1440);
    assert.equal(days[2].segments[0].startMinute, 0);
    assert.equal(days[2].segments[0].endMinute, 60);
});

test("out-of-week and invalid events cannot corrupt calendar layout", () => {
    const days = calendar.splitEvents([{ id: "bad", startAtUtc: "invalid" }, { id: "outside", startAtUtc: "2026-10-01T13:00Z" }, { startAtUtc: "2026-09-08T13:00Z" }], "2026-09-07");
    assert.ok(days.every(day => day.segments.length === 0));
});

test("23:59 visual labels remain inside their own day without changing actual time", () => {
    const day = calendar.splitEvents([event("late", "23:59", "23:59")], "2026-09-07")[1];
    const segment = day.segments[0];
    assert.equal(segment.startMinute, 1439);
    assert.equal(segment.layoutEnd, 1440);
    assert.equal(segment.layoutEnd - segment.layoutStart, 50);
    assert.ok(segment.layoutStart >= 0);
});

test("same-origin authenticated evidence paths are the only allowed file URLs", () => {
    assert.equal(calendar.safeAppUrl("/CopiersMtoV2Calendar/Evidence?id=1", origin), `${origin}/CopiersMtoV2Calendar/Evidence?id=1`);
    for (const unsafe of ["https://attacker.test/CopiersMtoV2Calendar/Evidence", "//attacker.test/image", "javascript:alert(1)", "data:text/html,evil", "/admin", "/CopiersMtoV2Calendar/../admin", "https://user:pass@example.test/CopiersMtoV2Calendar/Evidence"]) assert.equal(calendar.safeAppUrl(unsafe, origin), "", unsafe);
});

test("geolocation validates finite bounded numbers and accepts the equator", () => {
    assert.equal(calendar.validLocation({ latitude: 0, longitude: 0 }), true);
    for (const location of [null, {}, { latitude: 4, longitude: Infinity }, { latitude: 91, longitude: 0 }, { latitude: 4, longitude: "-74" }]) assert.equal(calendar.validLocation(location), false);
    assert.equal(calendar.mapUrl(null), "");
    const url = new URL(calendar.mapUrl({ latitude: 4.7, longitude: -74.1 }));
    assert.equal(url.origin, "https://www.openstreetmap.org");
    assert.equal(url.searchParams.get("marker"), "4.7,-74.1");
});

test("failed signed reports are clearly pending, not falsely completed", () => {
    assert.equal(calendar.stateLabel("Failed", "workflow"), "Pendiente de finalizar");
    assert.equal(calendar.stateLabel("Failed", "email"), "Fallido");
    assert.equal(calendar.stateLabel("ReadyToSend", "workflow"), "Finalizado");
    assert.equal(calendar.stateLabel("Pending", "email"), "Pendiente");
});

test("movement and toner have distinct labels and calendar colors without pretending to be maintenance", () => {
    assert.deepEqual(calendar.typeInfo("Movimiento"), { label: "Movimiento", css: "movement" });
    assert.deepEqual(calendar.typeInfo("Entrega de tóner"), { label: "Entrega de tóner", css: "toner" });
    assert.deepEqual(calendar.typeInfo("movement"), { label: "Movimiento", css: "movement" });
    assert.match(view, /mto-calendar__legend-item--movement">Movimiento/);
    assert.match(view, /mto-calendar__legend-item--toner">Entrega de tóner/);
});

test("module makes zero calls before activation and refreshes its week when reopened", async () => {
    const app = harness();
    assert.equal(app.calls.length, 0);
    await app.activate();
    assert.equal(app.calls.length, 2);
    await app.activate();
    assert.equal(app.calls.length, 3);
    assert.equal(app.calls.filter(call => call.url.pathname.endsWith("/Bootstrap")).length, 1);
    assert.equal(app.calls.at(-1).url.searchParams.get("weekStart"), app.calls[1].url.searchParams.get("weekStart"));
    assert.equal(app.calls.at(-1).url.searchParams.get("technicianId"), "tech-1");
    assert.ok(app.calls.every(call => call.request.credentials === "same-origin"));
    assert.ok(app.calls.every(call => call.request.cache === "no-store"));
});

test("repeated activation while a week request is pending does not duplicate or cancel it", async () => {
    let releaseWeek;
    const app = harness({ fetch: async (url, request) => {
        const result = url.pathname.endsWith("/Bootstrap")
            ? { technicians: [{ id: "tech-1", name: "Técnico" }], defaultTechnicianId: "tech-1" }
            : await new Promise(resolve => { releaseWeek = () => resolve({ weekStart: url.searchParams.get("weekStart"), events: [] }); });
        return { status: 200, ok: true, headers: { get: () => "application/json" }, json: async () => result };
    } });
    app.window.CopiersMtoV2Calendar.activate();
    app.window.CopiersMtoV2Calendar.activate();
    await settle();
    assert.equal(app.calls.length, 2);
    app.window.CopiersMtoV2Calendar.activate();
    assert.equal(app.calls.length, 2);
    assert.equal(app.calls[1].request.signal.aborted, false);
    releaseWeek(); await settle();
    assert.equal(app.root.attributes["aria-busy"], "false");
});

test("reopening the calendar exposes a newly completed maintenance without reloading the dashboard", async () => {
    let published = false;
    const app = harness({ fetch: async url => {
        const result = url.pathname.endsWith("/Bootstrap")
            ? { technicians: [{ id: "tech-1", name: "Técnico" }], defaultTechnicianId: "tech-1" }
            : { weekStart: url.searchParams.get("weekStart"), events: published ? [{ id: eventId, clientName: "Cliente recién atendido", maintenanceType: "Correctivo", startAtUtc: `${url.searchParams.get("weekStart")}T16:00:00Z`, endAtUtc: `${url.searchParams.get("weekStart")}T17:00:00Z`, workflowState: "ReadyToSend", emailState: "Sent" }] : [] };
        return { status: 200, ok: true, headers: { get: () => "application/json" }, json: async () => result };
    } });
    await app.activate();
    assert.equal(app.events().length, 0);
    published = true;
    await app.activate();
    assert.equal(app.events().length, 1);
    assert.match(app.events()[0].textContent, /Cliente recién atendidoCorrectivo/);
});

test("calendar reactivation does not close an open signed report or restart its deep link", async () => {
    const app = harness({ search: `?maintenanceId=${eventId}` });
    await app.activate();
    const requests = app.calls.length;
    await app.activate();
    assert.equal(app.calls.length, requests);
    assert.equal(app.ids.get("mtoCalendarDetail").open, true);
    app.ids.get("mtoCalendarDetailClose").click();
    await app.activate();
    assert.equal(app.calls.filter(call => call.url.pathname.endsWith("/Detail")).length, 1);
    assert.equal(app.ids.get("mtoCalendarDetail").open, false);
});

test("weekly calendar shows seven days, 24 hours, client and maintenance type", async () => {
    const app = harness(); await app.activate();
    const nodes = descendants(app.ids.get("mtoCalendarGrid"));
    assert.equal(nodes.filter(node => node.className === "mto-calendar__day").length, 7);
    assert.equal(nodes.filter(node => node.className === "mto-calendar__hour").length, 24);
    assert.match(app.events()[0].textContent, /Cliente de pruebaPreventivo/);
    assert.match(app.events()[0].attributes["aria-label"], /Abrir detalle/);
    assert.equal(app.root.attributes["aria-busy"], "false");
});

test("late-afternoon maintenance scrolls into view instead of staying at seven AM", async () => {
    const app = harness({ startTime: "23:48", endTime: "23:59" }); await app.activate();
    assert.equal(app.ids.get("mtoCalendarViewport").scrollTop, (18 * 60 + 48) * .8 - 24);
    const empty = harness({ noEvents: true }); await empty.activate();
    assert.equal(empty.ids.get("mtoCalendarViewport").scrollTop, 7 * 60 * .8 - 24);
});

test("week arrows and today query the selected technician", async () => {
    const app = harness(); await app.activate();
    const firstWeek = app.calls[1].url.searchParams.get("weekStart");
    app.ids.get("mtoCalendarNext").click(); await settle();
    assert.equal(app.calls.at(-1).url.searchParams.get("weekStart"), calendar.addDays(firstWeek, 7));
    assert.equal(app.calls.at(-1).url.searchParams.get("technicianId"), "tech-1");
    app.ids.get("mtoCalendarToday").click(); await settle();
    assert.equal(app.calls.at(-1).url.searchParams.get("weekStart"), firstWeek);
});

test("all technicians is selectable and defaults to the reporting scope returned by the server", async () => {
    const app = harness({ allTechnicians: true }); await app.activate();
    const selector = app.ids.get("mtoCalendarTechnician");
    assert.equal(selector.value, "all");
    assert.equal(selector.options.find(option => option.value === "all").textContent, "Todos los técnicos");
    assert.equal(app.calls.at(-1).url.searchParams.get("technicianId"), "all");
    assert.match(app.events()[0].attributes["aria-label"], /Técnico de prueba/);
    app.ids.get("mtoCalendarNext").click(); await settle();
    assert.equal(app.calls.at(-1).url.searchParams.get("technicianId"), "all");
    selector.value = "tech-1";
    selector.listeners.change[0](); await settle();
    assert.equal(app.calls.at(-1).url.searchParams.get("technicianId"), "tech-1");
    assert.doesNotMatch(app.events()[0].textContent, /Técnico de prueba/);
});

test("all-technician empty week is not mislabeled as one technician", async () => {
    const app = harness({ allTechnicians: true, noEvents: true }); await app.activate();
    assert.match(app.ids.get("mtoCalendarStatus").textContent, /No hay mantenimientos V2/);
});

test("detail shows entry and exit without repeating internal canonical timestamps as form fields", async () => {
    const app = harness({ detail: { answers: [
        { key: "service_started_at_utc", label: "Internal start UTC", value: "2026-09-08T13:00:00Z" },
        { key: "service_ended_at_utc", label: "Internal end UTC", value: "2026-09-08T14:00:00Z" },
        { key: "service_ended_at", label: "Salida confirmada", value: "8 sept 2026, 9:00 a. m." }
    ] } });
    await app.activate(); app.events()[0].click(); await settle();
    const body = app.ids.get("mtoCalendarDetailBody").textContent;
    assert.match(body, /Hora de entrada/);
    assert.match(body, /Hora de salida/);
    assert.match(body, /Salida confirmada/);
    assert.doesNotMatch(body, /Internal start UTC|Internal end UTC/);
});

test("empty catalog and empty week give actionable states", async () => {
    const noTechnicians = harness({ noTechnicians: true }); await noTechnicians.activate();
    assert.equal(noTechnicians.calls.length, 1);
    assert.match(noTechnicians.ids.get("mtoCalendarStatus").textContent, /Aún no hay técnicos/);
    const noEvents = harness({ noEvents: true }); await noEvents.activate();
    assert.match(noEvents.ids.get("mtoCalendarStatus").textContent, /no tiene mantenimientos/);
});

test("detail includes form fields, internal notes, signature, PDF and delivery status", async () => {
    const app = harness(); await app.activate(); app.events()[0].click(); await settle();
    const body = app.ids.get("mtoCalendarDetailBody");
    assert.equal(app.ids.get("mtoCalendarDetail").open, true);
    for (const value of ["MTO-001234", "Revisión completa", "Nota interna", "Valor del campo", "Cliente firmante", "Enviado", "Sede norte", "Copia para el cliente"]) assert.ok(body.textContent.includes(value), value);
    assert.equal(descendants(body).filter(node => node.tagName === "IFRAME").length, 1);
    assert.ok(descendants(body).find(node => node.tagName === "IMG").src.startsWith(origin));
    assert.match(descendants(body).find(node => node.tagName === "IFRAME").src, /^https:\/\/example.test\/CopiersMtoV2Calendar\/Evidence/);
});

test("OpenStreetMap receives no coordinates until the explicit map button click", async () => {
    const app = harness(); await app.activate(); app.events()[0].click(); await settle();
    const body = app.ids.get("mtoCalendarDetailBody");
    assert.ok(descendants(body).every(node => !String(node.src || "").includes("openstreetmap")));
    const button = descendants(body).find(node => node.tagName === "BUTTON" && node.textContent === "Cargar mapa");
    assert.ok(button);
    button.click();
    const map = descendants(body).find(node => String(node.src || "").includes("openstreetmap"));
    assert.ok(map);
    assert.equal(map.referrerPolicy, "no-referrer");
});

test("missing location never fabricates a map or coordinates", async () => {
    const app = harness({ detail: { location: null } }); await app.activate(); app.events()[0].click(); await settle();
    const body = app.ids.get("mtoCalendarDetailBody");
    assert.match(body.textContent, /No se registró una ubicación/);
    assert.ok(!descendants(body).some(node => node.textContent === "Cargar mapa"));
});

test("closing detail destroys embedded content, cancels detail and returns focus", async () => {
    const app = harness(); await app.activate(); const event = app.events()[0]; event.click(); await settle();
    app.ids.get("mtoCalendarDetailClose").click();
    assert.equal(app.ids.get("mtoCalendarDetail").open, false);
    assert.equal(app.ids.get("mtoCalendarDetailBody").children.length, 0);
    assert.equal(event.focused, true);
});

test("untrusted server strings render as text and unsafe attachment URLs are rejected", async () => {
    const injection = "<img src=x onerror=alert(1)>";
    const app = harness({ detail: { workPerformed: injection, reportUrl: "https://attacker.test/report.pdf", signatureUrl: "javascript:alert(1)", evidences: [{ fileName: injection, url: "data:text/html,evil" }] } });
    await app.activate(); app.events()[0].click(); await settle();
    const body = app.ids.get("mtoCalendarDetailBody");
    assert.ok(body.textContent.includes(injection));
    assert.ok(descendants(body).every(node => !node.src && !node.href));
    assert.doesNotMatch(source, /innerHTML|insertAdjacentHTML|eval\(/);
});

test("deep link opens the exact persisted report and selects its service week", async () => {
    const app = harness({ search: `?tab=copiers&copiersTab=maintenance-v2&maintenanceId=${eventId}` }); await app.activate();
    assert.equal(app.calls.find(call => call.url.pathname.endsWith("/Detail")).url.searchParams.get("id"), eventId);
    assert.equal(app.calls.find(call => call.url.pathname.endsWith("/Week")).url.searchParams.get("weekStart"), "2026-09-07");
    assert.equal(app.ids.get("mtoCalendarDetail").open, true);
    assert.match(app.ids.get("mtoCalendarDetailTitle").textContent, /MTO-001234/);
});

test("enabled mixed calendar counts activities instead of calling every record a maintenance", async () => {
    const app = harness({ activitiesEnabled: true }); await app.activate();
    assert.match(app.ids.get("mtoCalendarStatus").textContent, /1 actividad en esta semana/);
    const empty = harness({ activitiesEnabled: true, noEvents: true, allTechnicians: true }); await empty.activate();
    assert.match(empty.ids.get("mtoCalendarStatus").textContent, /No hay actividades/);
});

test("prefixed activity deep link opens the activity header and keeps its protected report URL", async () => {
    const activityId = `activity:${eventId}`;
    const app = harness({ search: `?maintenanceId=${encodeURIComponent(activityId)}`, detail: {
        id: activityId, serviceReference: "ACT-000123", maintenanceType: "Movimiento", activityKind: "movement",
        reportUrl: `/CopiersMtoV2Calendar/Evidence?id=${encodeURIComponent(activityId)}&evidenceKey=pdf`
    } });
    await app.activate();
    assert.equal(app.calls.find(call => call.url.pathname.endsWith("/Detail")).url.searchParams.get("id"), activityId);
    assert.match(app.ids.get("mtoCalendarDetailTitle").textContent, /ACT-000123/);
    assert.match(app.ids.get("mtoCalendarDetailBody").textContent, /Movimiento/);
    assert.ok(descendants(app.ids.get("mtoCalendarDetailBody")).find(node => node.tagName === "IFRAME").src.includes("activity%3A"));
});

test("failed event style and label remain distinguishable in the calendar", async () => {
    const app = harness({ failed: true }); await app.activate();
    assert.equal(app.events()[0].classList.contains("is-pending"), true);
    assert.match(app.events()[0].attributes["aria-label"], /Pendiente de finalizar/);
});

test("authentication errors stop cleanly and tell the user to refresh session", async () => {
    const app = harness({ fetch: async () => ({ status: 401, ok: false }) }); await app.activate();
    assert.match(app.ids.get("mtoCalendarStatus").textContent, /sesión no tiene acceso/);
    assert.equal(app.root.attributes["aria-busy"], "false");
});

test("an HTML login redirect is never parsed as successful calendar data", async () => {
    const app = harness({ fetch: async () => ({ status: 200, ok: true, redirected: true }) }); await app.activate();
    assert.match(app.ids.get("mtoCalendarStatus").textContent, /sesión expiró/);
});
