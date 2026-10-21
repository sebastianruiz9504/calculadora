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
async function fixture(mode, override, search) {
    const dom = new JSDOM(view, { runScripts: "outside-only", url: "https://local.test/CruceLicenciamiento", pretendToBeVisual: true });
    const document = dom.window.document;
    Object.assign(document.getElementById("licCruceApp").dataset, { loadUrl: "/data", searchClientsUrl: "/clients", saveClientRelationUrl: "/save", updateCostInvoiceDateUrl: "/move", defaultYear: "2026", defaultMonth: "9" });
    const requests = [];
    dom.window.fetch = async (url, options = {}) => {
        requests.push({ url: String(url), ...options });
        let payload = String(url).includes("/data") ? data(mode, override) : { message: "Relacion confirmada" };
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
        assert.equal(f.document.querySelector("[data-relation-scope]").value, "record");
        assert.equal(f.document.querySelectorAll(".licx-relationship script").length, 0);
        if (key === "sale") { assert.match(f.document.querySelector(".licx-relation-pair").textContent, /Sin costo/); assert.equal(f.document.querySelector("[data-relation-cost]").value, ""); }
        if (key === "cost") assert.match(f.document.querySelector(".licx-relation-pair").textContent, /Origen <Intcomex>/);
        f.dom.window.close();
    }
});

test("A sale-only row can select a cost from another row and save only that consumption", async () => {
    const f = await fixture("separate"); f.open("sale");
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
