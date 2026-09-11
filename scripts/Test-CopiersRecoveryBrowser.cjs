"use strict";
// Isolated headless browser + localhost fixtures. Never calls production/Dataverse.
const fs = require("node:fs"), path = require("node:path"), http = require("node:http"), assert = require("node:assert/strict");
const { chromium } = require("playwright");
const root = path.resolve(__dirname, "..");
const customer = { id: "11111111-1111-4111-8111-111111111111", name: "CLIENTE PRUEBA LOCAL", email: "local@example.test", contactName: "Persona prueba" };
const equipment = { id: "22222222-2222-4222-8222-222222222222", clientId: customer.id, clientName: customer.name, serial: "TEST-LOCAL-001", reference: "TEST" };
const view = fs.readFileSync(path.join(root, "Views/CopiersMtoV2/Index.cshtml"), "utf8");
const body = view.slice(view.indexOf('<div class="mto-v2-shell"'), view.lastIndexOf("@section Scripts"))
    .replaceAll("@recoveryOwner", "tenant/test-owner").replaceAll("@technicianLabel", "TEST TECHNICIAN")
    .replaceAll("@Html.AntiForgeryToken()", '<input name="__RequestVerificationToken" value="test-only" type="hidden">');
const html = `<!doctype html><html><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><link rel="stylesheet" href="/css/copiers-mto-v2.css"></head><body>${body}<script src="/js/copiers-mto-v2-drafts.js"></script><script src="/js/copiers-mto-v2-picker.js"></script><script src="/js/copiers-mto-v2.js"></script></body></html>`;
let posts = 0, receipt = null;
const server = http.createServer(async (req, res) => {
    const url = new URL(req.url, "http://localhost");
    const json = (value, status = 200) => { res.writeHead(status, { "Content-Type": "application/json" }); res.end(JSON.stringify(value)); };
    if (url.pathname === "/CopiersMtoV2/Bootstrap") return json({ schemaReady: true, activitiesEnabled: true, technicianName: "LOCAL TEST", clients: [customer], equipment: [equipment], maintenanceTypes: [{ value: 100000001, label: "Preventivo" }, { value: 100000002, label: "Correctivo" }] });
    if (url.pathname === "/CopiersMtoV2/Equipment") return json({ items: [equipment], allowExternalEquipment: false });
    if (url.pathname === "/CopiersMtoV2/CounterLatest") return json({ equipmentId: equipment.id, copiesCounter: null, scansCounter: null, recordId: "", dateValue: "" });
    if (url.pathname === "/CopiersMtoV2/SubmissionStatus") return json(receipt || {}, receipt ? 200 : 404);
    if (url.pathname === "/CopiersMtoV2/Status") return json({ recordId: "test-record", state: 2, emailState: 3, serviceReference: "TEST-LOCAL" });
    if (url.pathname === "/CopiersMtoV2/Finalize") {
        posts++;
        for await (const chunk of req) { /* consume local fixture only */ }
        receipt = { received: true, submissionKey: req.headers["idempotency-key"], status: "received", message: "Recibido en prueba local" };
        // Simulate a proxy error after acceptance; the browser never got the receipt.
        return json({ message: "No se confirmó la recepción: proxy de prueba" }, 503);
    }
    if (url.pathname.startsWith("/js/") || url.pathname.startsWith("/css/")) {
        const name = path.basename(url.pathname);
        if (!/^copiers-mto-v2[-a-z]*\.(js|css)$/.test(name)) { res.writeHead(404); return res.end(); }
        res.writeHead(200, { "Content-Type": name.endsWith("js") ? "text/javascript" : "text/css" });
        return res.end(fs.readFileSync(path.join(root, "wwwroot", url.pathname.startsWith("/js/") ? "js" : "css", name)));
    }
    res.writeHead(200, { "Content-Type": "text/html" }); res.end(html);
});
(async () => {
    await new Promise(resolve => server.listen(0, "127.0.0.1", resolve));
    const base = `http://127.0.0.1:${server.address().port}`;
    const browser = await chromium.launch({ channel: "msedge", headless: true });
    const context = await browser.newContext({ viewport: { width: 800, height: 1100 }, hasTouch: true });
    context.setDefaultTimeout(10000);
    const errors = [];
    try {
        let page = await context.newPage(); page.on("pageerror", e => errors.push(e.message));
        await page.goto(base + "/CopiersMtoV2");
        await page.waitForFunction(() => document.querySelector("#mtoV2RecoveryStatus").textContent.includes("automáticamente"));
        await page.locator("#mtoV2MaintenanceType").selectOption("100000001");
        await page.locator("#mtoV2ClientName").fill("CLIENTE");
        await page.locator('#mtoV2ClientOptions [role="option"]').first().click();
        await page.locator("#mtoV2EquipmentSerial").fill("TEST");
        await page.locator('#mtoV2EquipmentOptions [role="option"]').first().click();
        assert.equal(await page.locator("#mtoV2OnsiteContactName").inputValue(), "Persona prueba");
        await page.locator('[data-next-step="2"]').click();
        await page.locator("#mtoV2ServiceResult").selectOption({ index: 1 });
        await page.locator("#mtoV2WorkPerformed").fill("PRUEBA LOCAL DE RECUPERACION. No es un mantenimiento real.");
        await page.locator("#mtoV2CopiesAfter").fill("100"); await page.locator("#mtoV2ScansAfter").fill("10");
        await page.locator("#mtoV2EvidenceInput").setInputFiles({ name: "local-test.png", mimeType: "image/png", buffer: Buffer.from("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAusB9Wl6Ze0AAAAASUVORK5CYII=", "base64") });
        await page.locator('[data-next-step="3"]').click();
        await page.locator("#mtoV2SignerName").fill("FIRMA DE PRUEBA");
        await page.locator('[name="SignerRole"]').fill("Pruebas");
        await page.locator("#mtoV2CustomerAcceptance").check();
        const box = await page.locator("#mtoV2SignatureCanvas").boundingBox();
        await page.mouse.move(box.x + 30, box.y + 25); await page.mouse.down();
        for (let i = 1; i <= 15; i++) await page.mouse.move(box.x + 30 + i * 8, box.y + 25 + (i % 2) * 25);
        await page.mouse.up(); await page.locator('[data-next-step="4"]').click();
        await page.locator("#mtoV2FinalReviewConfirmed").check();
        await page.waitForTimeout(600);
        const original = await page.evaluate(() => window.CopiersMtoV2Drafts.read("tenant/test-owner"));
        assert.ok(original.strokes.length > 0);
        const key = await page.locator("#mtoV2SubmissionKey").inputValue();
        const second = await context.newPage(); await second.goto(base + "/CopiersMtoV2");
        await second.waitForFunction(() => document.querySelector("#mtoV2RecoveryStatus").textContent.includes("otra pestaña"));
        assert.equal(await second.locator("#mtoV2Form").evaluate(e => e.inert), true); await second.close();
        await page.reload();
        await page.waitForFunction(() => document.querySelector("#mtoV2RecoveryStatus").textContent.includes("recuperado"));
        assert.equal(await page.locator("#mtoV2SubmissionKey").inputValue(), key);
        assert.equal(await page.locator("#mtoV2CopiesAfter").inputValue(), "100");
        assert.equal(await page.locator("#mtoV2FileList img").count(), 1);
        assert.equal(await page.evaluate(async () => (await window.CopiersMtoV2Drafts.read("tenant/test-owner")).files[0].name), "local-test.png");
        assert.ok(Number(await page.locator("#mtoV2SignaturePointCount").inputValue()) >= 5);
        await context.setOffline(true);
        await page.locator("#mtoV2SubmitButton").click();
        await page.waitForFunction(() => document.querySelector("#mtoV2SubmitStatus").textContent.includes("Pendiente de conexión"));
        assert.equal(posts, 0);
        await page.close(); await context.setOffline(false);
        page = await context.newPage(); page.on("pageerror", e => errors.push(e.message)); await page.goto(base + "/CopiersMtoV2");
        await page.waitForFunction(() => document.querySelector("#mtoV2SubmitStatus").textContent.includes("fetch") || document.querySelector("#mtoV2SubmitStatus").textContent.includes("confirm"));
        assert.equal(posts, 1);
        await page.reload();
        await page.waitForFunction(() => document.querySelector("#mtoV2RecoveryStatus").textContent.includes("Recibido en el servidor"));
        assert.equal(posts, 1, "Lost acknowledgement must query receipt, not upload again");
        receipt = { ...receipt, status: "completed", result: { recordId: "test-record", state: 2, emailState: 1, serviceReference: "TEST-LOCAL" } };
        await page.reload(); await page.locator("#mtoV2CreateAnother").waitFor({ state: "visible" });
        assert.equal(posts, 1);
        await page.locator("#mtoV2CreateAnother").click();
        await page.waitForFunction(() => document.querySelector("#mtoV2RecoveryStatus").textContent.includes("automáticamente"));
        assert.notEqual(await page.locator("#mtoV2SubmissionKey").inputValue(), key);
        assert.deepEqual(errors, []);
        console.log(JSON.stringify({ passed: true, viewport: "800x1100 touch", checks: ["draft fields", "photos", "signature", "reload same key", "single editor", "offline close/reopen", "lost acknowledgement", "no duplicate upload", "email status", "create another"], productionWrites: 0 }));
    } catch (error) {
        console.error("Browser errors", errors);
        for (const p of context.pages()) console.error(await p.locator("#mtoV2RecoveryStatus").textContent(), await p.locator("#mtoV2SubmitStatus").textContent());
        throw error;
    } finally { await context.close(); await browser.close(); server.close(); }
})().catch(error => { console.error(error); server.close(); process.exitCode = 1; });
