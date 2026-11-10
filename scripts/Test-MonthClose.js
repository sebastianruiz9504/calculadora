// node --test scripts/Test-MonthClose.js (jsdom is a local test-only dependency).
const { test } = require("node:test");
const assert = require("node:assert/strict");
const fs = require("node:fs");
const { JSDOM } = require("jsdom");
const source = fs.readFileSync("wwwroot/js/month-close.js", "utf8");
const html = fs.readFileSync("Views/Dashboard/_MonthClose.cshtml", "utf8").replace(/@Url.Action\("(\w+)","MonthClose"\)/g, "/MonthClose/$1").replace('data-dashboard-panel="month-close" hidden', 'data-dashboard-panel="month-close"');
const tick = (ms = 20) => new Promise(resolve => setTimeout(resolve, ms));
function setup(extra = {}) {
    const dom = new JSDOM(html, { url: "http://localhost/Dashboard?tab=month-close", runScripts: "outside-only" });
    const w = dom.window, calls = [], errors = [];
    w.addEventListener("error", e => errors.push(e.error));
    w.HTMLDialogElement.prototype.showModal = function () { this.open = true; };
    w.HTMLDialogElement.prototype.close = function () { this.open = false; this.dispatchEvent(new w.Event("close")); };
    w.IntersectionObserver = class { constructor(cb) { w.revealYtd = () => cb([{ isIntersecting: true }]); } observe() {} };
    w.MonthCloseYtd = { mount: () => {}, release: () => {} };
    const report = (key, total = 100) => ({ key, total, title: key, state: "captured", unit: "COP", count: 1 });
    const summary = period => ({ id: period, period, revision: 1, mode: period === "2026-11" ? "live" : "captured", cutoff: period + "-10", capturedAt: "2026-11-10T20:00:00Z", hash: period, warnings: [], aging: [{ label: "Al día", value: 10 }, { label: "1–30 días", value: 90 }], billingLines: [{ label: "Cloud", value: 80 }, { label: "Copiers", value: 20 }], reports: ["receivables", "payables", "billing", "business", "profit-cloud", "profit-copiers", "account-audit"].map(k => report(k)) });
    w.fetch = async (value, options) => {
        const u = new URL(value); calls.push(u);
        let data;
        if (u.pathname.endsWith("Index")) data = { today: "2026-11-10", snapshots: [summary("2026-10"), summary("2026-11")] };
        else if (u.pathname.endsWith("Ytd")) data = { chart: {}, sourceWarning: "" };
        else {
            const key = u.searchParams.get("key");
            if (extra.response) { const custom = await extra.response(u, options); if (custom) return custom; }
            data = { key, title: key, message: "Consulta guardada", needed: "", columns: [{ key: "party", label: "Cliente" }, { key: "profit", label: "Utilidad", type: "money" }], rows: [{ party: "Cliente A", key: "group:uno", profit: -50 }], unit: "COP", page: 1, pages: 1, count: 1, totalCount: 1, totals: { profit: -50 }, unresolved: 0 };
        }
        return { ok: true, headers: { get: () => "application/json" }, json: async () => data };
    };
    w.eval(source);
    return { w, calls, errors, close: () => w.close(), $: id => w.document.getElementById(id) };
}
test("first view loads summary and profitability, deferring document details and YTD", async () => {
    const t = setup(); try {
        await tick(); assert.deepEqual(t.errors, []);
        assert.deepEqual(t.calls.map(u => u.pathname + ":" + (u.searchParams.get("key") || "")), ["/MonthClose/Index:", "/MonthClose/Report:profit-cloud"]);
        assert.equal(t.$("mcMetrics").children.length, 4); assert.equal(t.$("mcModal").open, false);
        t.w.revealYtd(); await tick(); assert(t.calls.some(u => u.pathname.endsWith("Ytd")));
    } finally { t.close(); }
});
test("cards open a large read-only dialog and close without changing data", async () => {
    const t = setup(); try { await tick(); t.$("mcMetrics").children[0].click(); await tick();
        assert.equal(t.$("mcModal").open, true); assert.equal(t.calls.at(-1).searchParams.get("key"), "receivables");
        assert.match(t.$("mcTableBody").textContent, /Cliente A/); t.$("mcClose").click(); assert.equal(t.$("mcModal").open, false);
        assert(t.calls.every(u => u.pathname !== "/MonthClose/Update"));
    } finally { t.close(); }
});
test("aging and vertical popups carry exact filters and display percentage", async () => {
    const t = setup(); try { await tick(); t.$("mcAging").children[1].click(); await tick();
        assert.equal(t.calls.at(-1).searchParams.get("aging"), "1");
        t.$("mcClose").click(); assert.match(t.$("mcLines").children[0].textContent, /80 %/);
        t.$("mcLines").children[0].click(); await tick(); assert.equal(t.calls.at(-1).searchParams.get("line"), "Cloud");
        assert.match(t.$("mcExport").href, /line=Cloud/);
    } finally { t.close(); }
});
test("client detail uses group key and selected vertical", async () => {
    const t = setup(); try { await tick(); t.$("mcProfitBody").querySelector("button").click(); await tick();
        assert.equal(t.calls.at(-1).searchParams.get("key"), "profit-detail"); assert.equal(t.calls.at(-1).searchParams.get("clientKey"), "group:uno"); assert.equal(t.calls.at(-1).searchParams.get("line"), "Cloud");
    } finally { t.close(); }
});
test("closed month hides refresh and retains read-only drilldown", async () => {
    const t = setup(); try { await tick(); t.$("mcMonths").children[9].click(); await tick();
        assert.equal(t.$("mcRefresh").hidden, true); assert.match(t.$("mcMode").textContent, /inmutable/);
        t.$("mcMetrics").children[2].click(); await tick(); assert.equal(t.$("mcModal").open, true); assert.match(t.$("mcDetailPeriod").textContent, /2026-10/);
    } finally { t.close(); }
});
test("late response from previous month cannot replace current detail", async () => {
    let finish;
    const t = setup({ response: async u => { if (u.searchParams.get("key") === "receivables") return new Promise(resolve => { finish = resolve; }); } });
    try { await tick(); t.$("mcMetrics").children[0].click(); await tick(); t.$("mcMonths").children[9].click(); await tick();
        finish({ ok: true, headers: { get: () => "application/json" }, json: async () => ({ rows: [{ party: "STALE" }] }) }); await tick();
        assert.equal(t.$("mcModal").open, false); assert.doesNotMatch(t.$("mcTableBody").textContent, /STALE/); assert.deepEqual(t.errors, []);
    } finally { t.close(); }
});
