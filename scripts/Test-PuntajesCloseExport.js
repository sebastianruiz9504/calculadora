"use strict";
// Run with node --test; jsdom is a test-only dependency supplied via NODE_PATH.
const { test } = require("node:test");
const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const { JSDOM } = require("jsdom");
const root = path.join(__dirname, "..");
const source = fs.readFileSync(path.join(root, "wwwroot/js/puntajes.js"), "utf8");
const view = fs.readFileSync(path.join(root, "Views/Puntajes/Index.cshtml"), "utf8");
const settle = async () => { for (let i = 0; i < 5; i++) await new Promise(resolve => setImmediate(resolve)); };
const excelType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

async function fixture(t, exportMode = "ok") {
    const dom = new JSDOM(view.replace("@Html.Raw(uiOptionsJson)", "{}"), { runScripts: "outside-only", url: "https://local.test/Puntajes" });
    t.after(() => dom.window.close());
    const { document } = dom.window;
    Object.assign(document.getElementById("puntajesApp").dataset, {
        initialFilter: "previous-month", initialBusinessFilter: "new-business",
        recordsUrl: "/records", previewCloseMonthUrl: "/preview", closeMonthUrl: "/close",
        undoCloseMonthUrl: "/undo", exportCloseMonthUrl: "/export"
    });
    const requests = [], downloads = [];
    const report = { periodKey: "2026-10", periodLabel: "octubre de 2026", batchId: "batch-1", message: "Cierre completo",
        logs: [{ action: "increment", level: "success", clientName: "Cliente A", productName: "Producto A", previousQuantity: 10, appliedQuantity: 3, finalQuantity: 13, message: "Cantidad incrementada: 10 + 3 = 13." }] };
    dom.window.bootstrap = { Modal: class { show() {} hide() {} } };
    dom.window.confirm = () => true;
    dom.window.console.error = () => {};
    dom.window.URL.createObjectURL = () => "blob:test-export";
    dom.window.URL.revokeObjectURL = () => {};
    dom.window.HTMLAnchorElement.prototype.click = function () { downloads.push({ filename: this.download, href: this.href }); };
    dom.window.fetch = async (url, options = {}) => {
        requests.push({ url, ...options });
        if (url === "/export") {
            return { ok: exportMode !== "error", status: exportMode === "error" ? 500 : 200,
                headers: { get: () => exportMode === "html" ? "text/html" : exportMode === "error" ? "application/json" : excelType },
                json: async () => ({ message: "No se pudo generar el Excel" }), text: async () => "Login", blob: async () => new dom.window.Blob(["xlsx"]) };
        }
        const data = url === "/close" ? report : url === "/undo"
            ? { ...report, message: "Se deshizo el cierre", logs: [{ action: "undo-increment", level: "success", clientName: "Cliente A", productName: "Producto A", finalQuantity: 10 }] }
            : url === "/preview" ? { periodKey: "2026-10", lines: [{ lineKey: "line-1", selectedByDefault: true, canChangeSelection: true }] }
            : { groups: [], supportsMonthClose: true, recordsCount: 1, verifiedRecordsCount: 1, closedRecordsCount: 0, canUndoMonthClose: true, monthClosePeriodLabel: "octubre de 2026" };
        return { ok: true, headers: { get: () => "application/json" }, json: async () => data };
    };
    dom.window.eval(source);
    await settle();
    const click = async id => { document.getElementById(id).click(); await settle(); };
    const close = async () => {
        await click("closeMonthBtn");
        const checkbox = document.getElementById("closeMonthReviewConfirmCheck");
        checkbox.checked = true;
        checkbox.dispatchEvent(new dom.window.Event("change", { bubbles: true }));
        await click("submitCloseMonthReviewBtn");
        assert.equal(document.querySelectorAll(".scores-close-log__entry").length, 1);
    };
    return { dom, document, requests, downloads, report, click, close };
}

test("Export activates only after a result and downloads that result without closing again", async t => {
    const f = await fixture(t);
    assert.equal(f.document.getElementById("exportCloseMonthBtn").disabled, true);
    await f.close();
    assert.equal(f.document.getElementById("exportCloseMonthBtn").disabled, false);
    await f.click("exportCloseMonthBtn");
    assert.equal(f.downloads[0].filename, "cierre-puntajes-2026-10.xlsx");
    assert.deepEqual(JSON.parse(f.requests.find(r => r.url === "/export").body), { ...f.report, operation: "close" });
    assert.equal(f.requests.filter(r => r.url === "/close").length, 1);
    assert.equal(f.requests.filter(r => r.url === "/undo").length, 0);
    assert.equal(f.document.getElementById("exportCloseMonthBtn").disabled, false);
});

test("Changing filters keeps the report period and exported detail tied to the completed close", async t => {
    const f = await fixture(t);
    await f.close();
    f.document.querySelector('[data-filter="this-month"]').click();
    await settle();
    await f.click("exportCloseMonthBtn");
    assert.equal(JSON.parse(f.requests.find(r => r.url === "/export").body).periodKey, "2026-10");
    assert.match(f.document.getElementById("closeMonthReportCaption").textContent, /octubre de 2026/);
});

test("Undo replaces the visible report and exports it as a reversal", async t => {
    const f = await fixture(t);
    await f.close();
    await f.click("undoCloseMonthBtn");
    await f.click("exportCloseMonthBtn");
    const data = JSON.parse(f.requests.find(r => r.url === "/export").body);
    assert.equal(data.operation, "undo");
    assert.equal(data.logs[0].action, "undo-increment");
    assert.equal(f.downloads[0].filename, "reversion-cierre-puntajes-2026-10.xlsx");
    assert.match(f.document.getElementById("closeMonthReportCaption").textContent, /reversión/);
});

for (const mode of ["error", "html"]) {
    test(`Export ${mode} response retains the report, reports the failure and permits retry`, async t => {
        const f = await fixture(t, mode);
        await f.close();
        await f.click("exportCloseMonthBtn");
        assert.equal(f.downloads.length, 0);
        assert.equal(f.document.getElementById("exportCloseMonthBtn").disabled, false);
        assert.equal(f.document.querySelectorAll(".scores-close-log__entry").length, 1);
        assert.match(f.document.getElementById("scoresStatusBanner").className, /error/);
        assert.equal(f.requests.filter(r => r.url === "/close").length, 1);
    });
}
