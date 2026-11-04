"use strict";
// Run with node --test; jsdom is a test-only dependency supplied via NODE_PATH.
const { test } = require("node:test");
const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const { JSDOM } = require("jsdom");
const root = path.join(__dirname, "..");
const source = fs.readFileSync(path.join(root, "wwwroot/js/liquidacion-nominas.js"), "utf8");
const view = fs.readFileSync(path.join(root, "Views/LiquidacionNominas/Index.cshtml"), "utf8");
const staleWarnings = [
    "Las novedades exceden los dias del mes por 13 dias.",
    "Hace falta liquidar 1 dia del mes.",
    "Hay dias no trabajados sin novedades registradas.",
    "Hay novedades con dias pendientes de motivo."
];

function row(overrides = {}) {
    return {
        employeeId: "employee-1", employeeName: "Empleado de prueba", periodKey: "2026-10",
        periodDays: 31, workedDays: 31, absenceDays: 0, novelties: [], verified: true,
        monthlySalaryBase: 1900000, monthlyAuxilio: 250000, bonusCompliance: 250000,
        healthRate: 0.04, pensionRate: 0.04, factorCopiers: 0, factorCloud: 100,
        employeeContractTypeOptionValue: 645250000, warnings: staleWarnings,
        ...overrides
    };
}

async function fixture(t, rows) {
    const dom = new JSDOM(view, { runScripts: "outside-only", pretendToBeVisual: true,
        url: "https://local.test/LiquidacionNominas" });
    t.after(() => dom.window.close());
    const { document } = dom.window;
    const draft = { version: 4, periodKey: "2026-10", paymentDateValue: "2026-11-05", rows };
    const requests = [];
    Object.assign(document.getElementById("nominaApp").dataset, {
        previewUrl: "/preview", confirmUrl: "/confirm", draftUrl: "/draft",
        existingPeriodUrl: "/existing", initialPeriod: "2026-10", suggestedPaymentDate: "2026-11-05"
    });
    dom.window.fetch = async (url, options = {}) => {
        requests.push({ url: String(url), ...options });
        return { ok: true, headers: { get: () => "application/json" }, json: async () =>
            String(url) === "/draft" ? { draft } : { hasRecords: false, rows: [] } };
    };
    dom.window.eval(source);
    for (let i = 0; i < 5; i++) await new Promise(resolve => setImmediate(resolve));
    assert.equal(document.querySelectorAll("#nominaRowsBody [data-row-id]").length, rows.length,
        document.getElementById("nominaStatusBanner").textContent);
    const open = (id = rows[0].employeeId) => document.querySelector(`[data-row-id="${id}"]`).click();
    const edit = (selector, value) => {
        const input = document.querySelector(selector);
        input.value = value;
        input.dispatchEvent(new dom.window.Event("input", { bubbles: true }));
    };
    const warnings = () => document.getElementById("nominaDetailWarnings").textContent.trim();
    const confirm = document.getElementById("confirmNominaBtn");
    return { dom, document, requests, open, edit, warnings, confirm };
}

test("Restoring a shared draft removes obsolete day warnings for every employee without saving or sending", async t => {
    const rows = Array.from({ length: 18 }, (_, i) => row({ employeeId: `employee-${i}` }));
    const f = await fixture(t, rows);
    for (const employee of rows) {
        f.open(employee.employeeId);
        assert.equal(f.warnings(), "");
        assert.equal(f.document.getElementById("detailWorkedDays").value, "31.00");
    }
    assert.equal(f.confirm.disabled, false);
    assert.equal(f.requests.some(r => r.method && r.method !== "GET"), false);
    assert.equal(f.document.querySelectorAll(".payroll-row--warning").length, 0);
});

test("A valid combination of worked days and novelties clears the previous excess warning", async t => {
    const f = await fixture(t, [row({ workedDays: 26, novelties: [{ reason: "vacaciones", days: 5 }] })]);
    f.open();
    assert.equal(f.warnings(), "");
    assert.equal(f.confirm.disabled, false);
});

test("A real excess is recalculated using current days and remains blocking", async t => {
    const f = await fixture(t, [row({ workedDays: 26, novelties: [{ reason: "ingreso", days: 7 }] })]);
    f.open();
    assert.equal(f.warnings(), "Las novedades exceden los dias del mes por 2 dias.");
    assert.equal(f.confirm.disabled, true);
    assert.match(f.confirm.title, /2 dias/);
});

test("Missing days and missing novelties still prevent confirmation", async t => {
    const f = await fixture(t, [row({ workedDays: 30 })]);
    f.open();
    assert.match(f.warnings(), /Hay dias no trabajados sin novedades registradas/);
    assert.match(f.warnings(), /Hace falta liquidar 1 dia del mes/);
    assert.doesNotMatch(f.warnings(), /exceden|pendientes de motivo/);
    assert.equal(f.confirm.disabled, true);
});

test("Partial novelty coverage reports only the current missing days", async t => {
    const f = await fixture(t, [row({ workedDays: 27, novelties: [{ reason: "vacaciones", days: 3 }] })]);
    f.open();
    assert.equal(f.warnings(), "Hace falta liquidar 1 dia del mes.");
    assert.equal(f.confirm.disabled, true);
});

test("A novelty without a reason stays blocked until its reason is selected and the row verified", async t => {
    const f = await fixture(t, [row({ workedDays: 30, novelties: [{ reason: "", days: 1 }] })]);
    f.open();
    assert.equal(f.warnings(), "Hay novedades con dias pendientes de motivo.");
    assert.equal(f.confirm.disabled, true);
    f.edit('[data-novelty-field="reason"]', "ingreso");
    assert.equal(f.warnings(), "");
    assert.equal(f.confirm.disabled, true, "Editing must invalidate employee verification");
    f.document.querySelector('[data-row-field="verified"]').click();
    assert.equal(f.confirm.disabled, false);
});

test("Correcting worked days clears the warning immediately while retaining the verification requirement", async t => {
    const f = await fixture(t, [row({ workedDays: 15, novelties: [{ reason: "ingreso", days: 29 }] })]);
    f.open();
    assert.equal(f.warnings(), staleWarnings[0]);
    f.edit("#detailWorkedDays", "31");
    assert.equal(f.warnings(), "");
    assert.equal(f.confirm.disabled, true);
    f.document.querySelector('[data-row-field="verified"]').click();
    assert.equal(f.confirm.disabled, false);
});

test("Unrelated source warnings and manual amounts survive warning refresh", async t => {
    const sourceWarning = "El empleado tiene comisiones pero no tiene tope comisional configurado.";
    const f = await fixture(t, [row({ manualEditEnabled: true, manualOverrides: { netPayroll: 1234567 },
        warnings: [...staleWarnings, sourceWarning] })]);
    f.open();
    assert.match(f.warnings(), /no tiene tope comisional/);
    assert.match(f.warnings(), /Edicion manual activa/);
    assert.doesNotMatch(f.warnings(), /exceden|Hace falta|sin novedades|pendientes de motivo/);
    assert.equal(f.document.querySelector('[data-manual-field="netPayroll"]').value, "1234567.00");
    assert.equal(f.confirm.disabled, false);
});
