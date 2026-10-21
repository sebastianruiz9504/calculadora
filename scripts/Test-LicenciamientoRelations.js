"use strict";
// jsdom is a test-only dependency provided via NODE_PATH.
const { test } = require("node:test");
const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const { JSDOM } = require("jsdom");
const root = path.join(__dirname, "..");
const source = fs.readFileSync(path.join(root, "wwwroot/js/cruce-licenciamiento.js"), "utf8");
const view = fs.readFileSync(path.join(root, "Views/CruceLicenciamiento/Index.cshtml"), "utf8");
const cost = { recordId: "cost-1", originalClientName: "Origen <Intcomex>", cliente: "Origen", clienteId: "client-cost", accountIdOriginal: "account-1", accountOriginal: "12345", producto: "Microsoft 365", mes: "2026-09", valor: 100, sourceVersion: 'W/"1"', accountRelationId: "map-1", accountRelationVersion: 'W/"2"' };
const billing = { recordId: "invoice-1", cliente: "Cliente venta", clienteId: "client-sale", referencia: "FV-1", fecha: "2026-10-05", valor: 150 };
cost.relationGroupKey = "client:original-client";
function data(mode = "separate", override = {}) {
    const c = { ...cost, ...override };
    const rows = mode === "both" ? [{ matrixClientKey: "both", cliente: "Cliente combinado", trace: { costItems: [c], billingItems: [billing] } }]
        : [{ matrixClientKey: "cost", cliente: "Cliente costo", trace: { costItems: [c], billingItems: [] } }, { matrixClientKey: "sale", cliente: "Cliente venta", trace: { costItems: [], billingItems: [billing] } }];
    rows.forEach(row => { row.tipoContratoKey = mode === "prepaid" ? "onetime" : "monthly"; row.mesCierre = "2026-09"; });
    return { selectedYear: 2026, selectedMonth: 9, periodMode: "month", periodLabel: "Consumo septiembre / facturacion octubre", rows,
        matrixMonths: [{ key: "2026-09", label: "Septiembre" }], matrixSegments: [{ key: mode === "prepaid" ? "onetime" : "all", label: "Todo", recordsCount: rows.length,
            rows: rows.map(row => ({ rowKey: row.matrixClientKey, cliente: row.cliente, cells: [{ mes: "2026-09", costoLicenciamiento: row.trace.costItems.length * 100, facturacionSinIva: row.trace.billingItems.length * 150 }] })) }] };
}
const settle = () => new Promise(resolve => setImmediate(resolve));
async function fixture(mode, override, search, onData) {
    const dom = new JSDOM(view, { runScripts: "outside-only", url: "https://local.test/CruceLicenciamiento", pretendToBeVisual: true });
    const document = dom.window.document;
    Object.assign(document.getElementById("licCruceApp").dataset, { loadUrl: "/data", searchClientsUrl: "/clients", saveClientRelationUrl: "/save", updateCostInvoiceDateUrl: "/move", defaultYear: "2026", defaultMonth: "9" });
    const requests = [];
    let loads = 0;
    dom.window.fetch = async (url, options = {}) => {
        requests.push({ url: String(url), ...options });
        let payload = String(url).includes("/data") ? (onData ? await onData(++loads) : data(mode, override)) : { message: "Relacion confirmada" };
        if (String(url).includes("/clients")) payload = search ? await search(String(url)) : [{ clientId: "client-sale", clientName: "Cliente venta", nit: "900123456" }];
        return { ok: true, headers: { get: () => "application/json" }, json: async () => payload };
    };
    dom.window.eval(source); await settle();
    const select = (selector, value) => { const element = document.querySelector(selector); element.value = value; element.dispatchEvent(new dom.window.Event("change", { bubbles: true })); };
    const input = (selector, value) => { const element = document.querySelector(selector); element.value = value; element.dispatchEvent(new dom.window.Event("input", { bubbles: true })); };
    const open = key => document.querySelector(`[data-map-client][data-client-key="${key}"]`).click();
    return { dom, document, requests, select, input, open };
}

test("Cost-only, sale-only and combined rows open the same two-sided sheet without raw account editing", async () => {
    for (const [mode, key] of [["separate", "cost"], ["separate", "sale"], ["both", "both"]]) {
        const f = await fixture(mode); f.open(key);
        assert.equal(f.document.querySelectorAll(".licx-relation-card").length, 2);
        assert.equal(f.document.querySelectorAll("[data-account-input], [data-target-account-select]").length, 0);
        assert.equal(f.document.querySelector("[data-relation-scope]").value, "month");
        assert.equal(f.document.querySelectorAll(".licx-relationship script").length, 0);
        if (key === "sale") { assert.match(f.document.querySelector(".licx-relation-pair").textContent, /Sin costo/); assert.equal(f.document.querySelector("[data-relation-cost]").value, ""); }
        if (key === "cost") assert.match(f.document.querySelector(".licx-relation-pair").textContent, /Origen <Intcomex>/);
        f.dom.window.close();
    }
});

test("A sale-only row can select a cost from another row and save only that consumption", async () => {
    const f = await fixture("separate"); f.open("sale");
    f.select("[data-relation-scope]", "record");
    f.select("[data-relation-cost]", "cost-1");
    assert.equal(f.document.querySelector("[data-relation-save]").disabled, false);
    assert.match(f.document.querySelector("[data-relation-preview]").textContent, /unicamente este registro/);
    f.document.querySelector("[data-relation-save]").click(); await settle();
    const body = JSON.parse(f.requests.find(request => request.url === "/save").body);
    assert.deepEqual(body, { costRecordId: "cost-1", clientId: "client-sale", scope: "record", expectedCostVersion: 'W/"1"', expectedMappingId: "", expectedMappingVersion: "" });
    f.dom.window.close();
});

test("Permanent relation shows all-period impact and sends the account relation version", async () => {
    const f = await fixture("both"); f.open("both"); f.select("[data-relation-scope]", "account");
    assert.match(f.document.querySelector("[data-relation-preview]").textContent, /todos los periodos/);
    f.document.querySelector("[data-relation-save]").click(); await settle();
    const body = JSON.parse(f.requests.find(request => request.url === "/save").body);
    assert.equal(body.scope, "account"); assert.equal(body.expectedMappingId, "map-1"); assert.equal(body.expectedMappingVersion, 'W/"2"');
    f.dom.window.close();
});

test("Missing account and existing per-consumption exception disable permanent scope", async () => {
    for (const override of [{ accountIdOriginal: "" }, { recordRelationId: "record-map" }]) {
        const f = await fixture("both", override); f.open("both");
        assert.equal(f.document.querySelector('[data-relation-scope] [value="account"]').disabled, true);
        f.dom.window.close();
    }
});

test("Searching by NIT requires an explicit result selection and cannot save stale selections", async () => {
    const f = await fixture("separate"); f.open("cost"); f.input("[data-relation-client-search]", "900123456");
    f.select("[data-relation-scope]", "record");
    assert.equal(f.document.querySelector("[data-relation-save]").disabled, true);
    await new Promise(resolve => setTimeout(resolve, 280)); await settle();
    assert.match(f.requests.find(request => request.url.includes("/clients")).url, /query=900123456/);
    assert.match(f.document.querySelector("[data-relation-client]").textContent, /NIT 900123456/);
    assert.equal(f.document.querySelector("[data-relation-save]").disabled, true);
    f.select("[data-relation-client]", "client-sale"); assert.equal(f.document.querySelector("[data-relation-save]").disabled, false);
    f.input("[data-relation-cost-search]", "does not exist"); assert.equal(f.document.querySelector("[data-relation-save]").disabled, true);
    f.dom.window.close();
});

test("An old search cannot overwrite a newer search or a reopened sheet", async () => {
    let resolveOld;
    const f = await fixture("separate", {}, url => url.includes("query=old") ? new Promise(resolve => { resolveOld = resolve; }) : [{ clientId: "new", clientName: "Nuevo" }]);
    f.open("cost"); f.input("[data-relation-client-search]", "old"); await new Promise(resolve => setTimeout(resolve, 280));
    f.input("[data-relation-client-search]", "new"); await new Promise(resolve => setTimeout(resolve, 280)); await settle();
    resolveOld([{ clientId: "old", clientName: "Viejo" }]); await settle();
    assert.match(f.document.querySelector("[data-relation-client]").textContent, /Nuevo/);
    assert.doesNotMatch(f.document.querySelector("[data-relation-client]").textContent, /Viejo/);
    f.dom.window.close();
});

test("Moving a prepaid cost submits a consumption month and version, never a replacement invoice date", async () => {
    const f = await fixture("prepaid");
    f.document.querySelector('[data-detail-source="cost"]').click();
    f.input("[data-cost-month-input]", "2026-08");
    f.document.querySelector('[data-detail-action="move-cost-month"]').click(); await settle();
    const body = JSON.parse(f.requests.find(request => request.url === "/move").body);
    assert.deepEqual(body, { recordId: "cost-1", consumptionMonth: "2026-08", expectedCostVersion: 'W/"1"' });
    f.dom.window.close();
});

function fiveLineData(saved = false) {
    const payload = data("separate");
    const lines = Array.from({ length: 5 }, (_, i) => ({ ...cost, recordId: `cost-${i + 1}`,
        recordRelationId: i === 0 ? "existing-record" : "", recordRelationVersion: i === 0 ? 'W/"3"' : "" }));
    // One line was previously mapped; another is prepaid and hidden by the Monthly filter.
    payload.rows[0].trace.costItems = saved ? [] : lines.slice(1, 4);
    payload.rows[1].trace.costItems = saved ? lines : [lines[0]];
    payload.rows.push({ ...payload.rows[0], matrixClientKey: "prepaid", tipoContratoKey: "onetime", trace: { costItems: saved ? [] : [lines[4]], billingItems: [] } });
    payload.rows.push({ ...payload.rows[0], matrixClientKey: "excluded", trace: { costItems: [
        { ...cost, recordId: "another-month", mes: "2026-08" },
        { ...cost, recordId: "another-client", relationGroupKey: "client:other" }
    ], billingItems: [] } });
    payload.matrixSegments[0].key = "monthly";
    payload.matrixSegments[0].rows[0].cells[0].costoLicenciamiento = saved ? 0 : 300;
    payload.matrixSegments[0].rows[1].cells[0].costoLicenciamiento = saved ? 500 : 100;
    payload.matrixSegments[0].totals = { totalCostosLicenciamiento: 500, totalFacturacionSinIva: 150 };
    return payload;
}

test("Selecting one line submits all five client/month costs, including hidden and previously related lines, and refreshes the matrix", async () => {
    const f = await fixture("separate", {}, null, count => fiveLineData(count > 1));
    f.open("sale"); f.select("[data-relation-cost]", "cost-2");
    assert.equal(f.document.querySelector("[data-relation-scope]").value, "month");
    assert.match(f.document.querySelector("[data-relation-preview]").textContent, /5 lineas de costo/);
    f.document.querySelector("[data-relation-save]").click(); await settle(); await settle();
    const body = JSON.parse(f.requests.find(request => request.url === "/save").body);
    assert.equal(body.scope, "month");
    assert.deepEqual(body.lines.map(line => line.recordId).sort(), ["cost-1", "cost-2", "cost-3", "cost-4", "cost-5"]);
    assert.equal(body.lines.find(line => line.recordId === "cost-1").expectedMappingId, "existing-record");
    assert.equal(body.lines.find(line => line.recordId === "cost-1").expectedMappingVersion, 'W/"3"');
    assert.equal(f.document.querySelector(".licx-modal").hidden, true);
    assert.match(f.document.querySelector('[data-detail-source="cost"][data-client-key="sale"]').textContent, /500/);
    assert.match(f.document.getElementById("licCruceStatus").textContent, /Vista y totales actualizados/);
    assert.equal(f.requests.filter(request => request.url.includes("/data")).length, 2);
    assert.ok(f.requests.filter(request => request.url.includes("/data")).every(request => request.cache === "no-store"));
    f.dom.window.close();
});

test("A failed refresh after saving is reported honestly and does not ask the user to save again", async () => {
    const f = await fixture("separate", {}, null, count => { if (count > 1) throw new Error("Refresh failed"); return fiveLineData(); });
    f.open("sale"); f.select("[data-relation-cost]", "cost-2");
    f.document.querySelector("[data-relation-save]").click(); await settle(); await settle();
    assert.match(f.document.getElementById("licCruceStatus").textContent, /se guardo.*no se pudo refrescar.*no necesitas guardar de nuevo/);
    assert.doesNotMatch(f.document.getElementById("licCruceStatus").textContent, /Vista y totales actualizados/);
    f.dom.window.close();
});

test("A delayed read from before the save cannot overwrite the newly refreshed costs", async () => {
    let oldRead;
    const f = await fixture("separate", {}, null, count => count === 2 ? new Promise(resolve => { oldRead = resolve; }) : fiveLineData(count > 2));
    f.document.getElementById("licCruceFilters").dispatchEvent(new f.dom.window.Event("submit", { bubbles: true, cancelable: true }));
    f.open("sale"); f.select("[data-relation-cost]", "cost-2");
    f.document.querySelector("[data-relation-save]").click(); await settle(); await settle();
    oldRead(fiveLineData()); await settle();
    assert.match(f.document.querySelector('[data-detail-source="cost"][data-client-key="sale"]').textContent, /500/);
    assert.match(f.document.getElementById("licCruceStatus").textContent, /Vista y totales actualizados/);
    f.dom.window.close();
});
