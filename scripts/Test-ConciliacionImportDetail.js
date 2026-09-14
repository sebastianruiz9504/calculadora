"use strict";
// Test-only dependency: jsdom (resolve with NODE_PATH; never shipped to the application).
const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const { test } = require("node:test");
const { JSDOM } = require("jsdom");
const source = fs.readFileSync(path.join(__dirname, "../wwwroot/js/conciliacion.js"), "utf8");
const functions = source.slice(source.indexOf("    const ensureDeduccionesDetailModal ="), source.indexOf("    const renderDeduccionesMetric ="));
const sample = {
    periodLabel: "septiembre 2026", importedAtDisplay: "14/09/2026", siigoRows: 1,
    documents: [{ invoiceNumber: "A-2879", supplierName: "Proveedor de prueba con un nombre largo", supplierNit: "901000000", totalValue: 869999.98,
        statusLabel: "Contacto pendiente", detail: "Siigo rechazó la creación. ".repeat(20), recordId: "sample", needsRut: true }]
};
function fixture() {
    const dom = new JSDOM('<div class="cnc-shell" id="app"><button id="launch">Ver importación</button></div>', { runScripts: "outside-only", url: "https://local.test/" });
    dom.window.eval(`const app = document.getElementById('app'); const numberLabel = String; const moneyPrecise = value => '$ ' + Number(value).toFixed(2);
        const calculateColombianCheckDigit = () => ''; const openDianSupplierModal = (...args) => { window.supplierAction = args; };
        ${functions}
        window.openHistory = openDeduccionesHistoryDetail;`);
    dom.window.document.getElementById("launch").focus();
    dom.window.openHistory(structuredClone(sample));
    return dom;
}
test("summary has four columns and keeps long details outside every row", () => {
    const dom = fixture(), document = dom.window.document;
    assert.equal(document.querySelectorAll("thead th").length, 4);
    assert.equal(document.querySelectorAll("tbody tr td").length, 4);
    assert.equal(document.querySelector("table").textContent.includes("Siigo rechazó"), false);
    document.querySelector("tbody tr td:nth-child(2)").click();
    const detail = document.getElementById("cncDeduccionesRowModal");
    assert.ok(detail.textContent.includes(sample.documents[0].detail));
    assert.ok(detail.textContent.includes("901000000"));
    assert.equal(document.getElementById("cncDeduccionesDetailModal").hidden, true);
    assert.equal(detail.querySelectorAll("dl > div").length, 10);
    dom.window.close();
});
test("keyboard dismissal restores the selected document and isolates supplier actions", () => {
    const dom = fixture(), document = dom.window.document;
    const trigger = document.querySelector(".cnc-import-open");
    trigger.click();
    let detail = document.getElementById("cncDeduccionesRowModal");
    assert.equal(document.activeElement, detail.querySelector("[data-import-row-close]"));
    detail.dispatchEvent(new dom.window.KeyboardEvent("keydown", { key: "Escape", bubbles: true }));
    assert.equal(document.getElementById("cncDeduccionesRowModal"), null);
    assert.equal(document.activeElement, trigger);
    assert.equal(document.getElementById("cncDeduccionesDetailModal").hidden, false);
    trigger.click();
    document.querySelector("[data-import-row-actions] button").click();
    assert.equal(document.getElementById("cncDeduccionesRowModal"), null);
    assert.equal(dom.window.supplierAction[0].dataset.recordId, "sample");
    assert.equal(dom.window.supplierAction[1].mode, "rut");
    dom.window.close();
});
test("untrusted text is rendered as text and omitted rows retain their reasons", () => {
    const dom = fixture(), document = dom.window.document;
    dom.window.openHistory({ documents: [], skipped: [{ reason: '<img src=x onerror="alert(1)">', prefix: "X", folio: "3" }] });
    document.querySelector(".cnc-import-open").click();
    assert.equal(document.querySelectorAll("img").length, 0);
    assert.ok(document.getElementById("cncDeduccionesRowModal").textContent.includes('<img src=x onerror="alert(1)">'));
    dom.window.close();
});
test("history does not silently hide records after row 500", () => {
    const dom = fixture(), document = dom.window.document;
    dom.window.openHistory({ documents: Array.from({ length: 501 }, (_, index) => ({ invoiceNumber: String(index) })) });
    assert.equal(document.querySelectorAll("tbody tr").length, 501);
    dom.window.close();
});
