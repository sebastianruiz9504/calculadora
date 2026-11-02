"use strict";
// Run with node --test; jsdom is a test-only dependency supplied via NODE_PATH.
const { test } = require("node:test");
const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const { JSDOM } = require("jsdom");
const root = path.join(__dirname, "..");
const source = fs.readFileSync(path.join(root, "wwwroot/js/licenciamiento.js"), "utf8");
const view = fs.readFileSync(path.join(root, "Views/Licenciamiento/Index.cshtml"), "utf8");
const settle = () => new Promise(resolve => setImmediate(resolve));

async function fixture(t) {
    const dom = new JSDOM(view, { runScripts: "outside-only", url: "https://local.test/Licenciamiento" });
    t.after(() => dom.window.close());
    const document = dom.window.document;
    const records = [
        ["nov-a-basic", "AERORENTAL", "Basic", "2026-11-05"],
        ["nov-a-premium", "AERORENTAL", "Premium", "2026-11-05"],
        ["nov-b-basic", "AGUAS", "Basic", "2026-11-05"],
        ["oct-a-basic", "AERORENTAL", "Basic", "2026-10-05"]
    ].map(([recordId, nombreCliente, productDisplay, facturaValue]) => ({
        recordId, nombreCliente, productDisplay, facturaValue, facturaDisplay: facturaValue,
        contractTypeLabel: "Monthly", valorTotalUsd: 10
    }));
    const requests = [];
    Object.assign(document.getElementById("licenciamientoApp").dataset, { loadUrl: "/data", updateContractUrl: "/contract" });
    dom.window.fetch = async (url, options = {}) => {
        requests.push({ url, ...options });
        return { ok: true, headers: { get: () => "application/json" }, json: async () =>
            url === "/data" ? { records, contractTypeOptions: [{ value: 1, label: "Monthly" }, { value: 2, label: "Annual" }] } : { message: "Actualizado" } };
    };
    dom.window.eval(source);
    await settle();
    assert.equal(document.getElementById("licStatus").textContent, "", "board load: " + document.getElementById("licStatus").textContent);
    const group = key => document.querySelector(`[data-lic-group-toggle="${key}"]`);
    const client = name => document.querySelector(`[data-lic-client-check="invoice:2026_11_05|client:${name.toLowerCase()}"]`);
    const filter = (key, value) => {
        const input = document.querySelector(`[data-lic-filter="${key}"]`);
        input.value = value;
        input.dispatchEvent(new dom.window.Event("input", { bubbles: true }));
    };
    const save = async () => {
        document.getElementById("licContractBtn").click();
        document.getElementById("licContractSelect").value = "2";
        document.getElementById("licContractForm").dispatchEvent(new dom.window.Event("submit", { bubbles: true, cancelable: true }));
        await settle();
        return JSON.parse(requests.find(request => request.url === "/contract").body);
    };
    group("invoice:2026_11_05").click();
    return { dom, document, requests, group, client, filter, save };
}

test("A collapsed client selects all its products in that invoice and reuses the contract update", async t => {
    const f = await fixture(t);
    assert.equal(f.document.querySelectorAll("[data-lic-row-check]").length, 0);
    f.client("AERORENTAL").click();
    assert.equal(f.client("AERORENTAL").checked, true);
    assert.equal(f.client("AGUAS").checked, false);
    assert.equal(f.document.getElementById("licSelectedCount").textContent, "2 seleccionados");
    assert.deepEqual(await f.save(), { recordIds: ["nov-a-basic", "nov-a-premium"], contractTypeValue: 2 });
    assert.equal(f.document.getElementById("licSelectedCount").textContent, "0 seleccionados");
});

test("Several clients can be selected and deselected without expanding products", async t => {
    const f = await fixture(t);
    f.client("AERORENTAL").click(); f.client("AGUAS").click();
    assert.equal(f.document.getElementById("licSelectedCount").textContent, "3 seleccionados");
    f.client("AERORENTAL").click();
    assert.deepEqual((await f.save()).recordIds, ["nov-b-basic"]);
});

test("Individual rows and client checkboxes stay synchronized, including partial selection", async t => {
    const f = await fixture(t);
    f.client("AERORENTAL").click();
    const key = "invoice:2026_11_05|client:aerorental";
    f.group(key).click(); f.group(`${key}|product:basic`).click();
    const row = f.document.querySelector('[data-record-id="nov-a-basic"]');
    assert.equal(row.checked, true);
    row.click();
    assert.equal(f.client("AERORENTAL").checked, false);
    assert.equal(f.client("AERORENTAL").indeterminate, true);
    f.client("AERORENTAL").click();
    assert.equal(row.checked, true);
    assert.equal(f.client("AERORENTAL").indeterminate, false);
    f.group(key).click(); f.group(key).click();
    assert.equal(f.client("AERORENTAL").checked, true);
});

test("Client selection respects product and invoice filters", async t => {
    const f = await fixture(t);
    f.filter("facturaValue", "2026-11"); f.filter("productDisplay", "Basic");
    f.client("AERORENTAL").click();
    assert.deepEqual((await f.save()).recordIds, ["nov-a-basic"]);
});

test("Select all and sorting preserve client selection; removing a filter shows partial state", async t => {
    const f = await fixture(t);
    f.filter("facturaValue", "2026-11"); f.filter("productDisplay", "Basic");
    f.document.getElementById("licSelectAll").click();
    assert.equal(f.client("AERORENTAL").checked, true);
    assert.equal(f.client("AGUAS").checked, true);
    f.filter("productDisplay", "");
    f.document.querySelector('[data-lic-sort="nombreCliente"]').click();
    assert.equal(f.client("AERORENTAL").indeterminate, true);
    assert.equal(f.client("AGUAS").checked, true);
    f.document.getElementById("licSelectAll").click();
    assert.equal(f.client("AERORENTAL").checked, true);
    f.document.getElementById("licSelectAll").click();
    assert.equal(f.client("AERORENTAL").checked, false);
    assert.equal(f.document.getElementById("licContractBtn").disabled, true);
});
