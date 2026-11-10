"use strict";
// Real browser and IndexedDB, isolated localhost responses. No business data or mail.
const fs = require("node:fs"), path = require("node:path"), http = require("node:http"), assert = require("node:assert/strict");
const { chromium } = require("playwright");
const root = path.resolve(__dirname, "..");
const owner = "tenant/reset-test";
const customer = { id: "11111111-1111-4111-8111-111111111111", name: "CLIENTE LOCAL", email: "local@example.test", contactName: "Contacto local" };
const equipment = { id: "22222222-2222-4222-8222-222222222222", clientId: customer.id, clientName: customer.name, serial: "TEST-001", reference: "TEST" };
const view = fs.readFileSync(path.join(root, "Views/CopiersMtoV2/Index.cshtml"), "utf8");
const body = view.slice(view.indexOf('<div class="mto-v2-shell"'), view.lastIndexOf("@section Scripts"))
    .replaceAll("@recoveryOwner", owner).replaceAll("@technicianLabel", "LOCAL TEST")
    .replaceAll("@Html.AntiForgeryToken()", '<input name="__RequestVerificationToken" value="local-csrf" type="hidden">');
const html = `<!doctype html><html><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><link rel="stylesheet" href="/css/copiers-mto-v2.css"></head><body>${body}<script src="/js/copiers-mto-v2-drafts.js"></script><script src="/js/copiers-mto-v2-picker.js"></script><script src="/js/copiers-mto-v2.js"></script></body></html>`;
let scenario, receipt, posts, discards, lastPayload, failDiscard;
const server = http.createServer(async (req, res) => {
    const url = new URL(req.url, "http://localhost");
    const json = (value, status = 200) => { res.writeHead(status, { "Content-Type": "application/json" }); res.end(JSON.stringify(value)); };
    if (url.pathname.endsWith("/Bootstrap")) return json({ schemaReady: true, activitiesEnabled: true, technicianName: "LOCAL TEST", clients: [customer], equipment: [equipment], maintenanceTypes: [{ value: 100000001, label: "Preventivo" }, { value: 100000002, label: "Correctivo" }] });
    if (url.pathname.endsWith("/Equipment")) return json({ items: [equipment] });
    if (url.pathname.endsWith("/CounterLatest")) return json({ equipmentId: equipment.id, copiesCounter: null, scansCounter: null, recordId: "", dateValue: "" });
    if (url.pathname.endsWith("/SubmissionStatus")) return json(receipt || {}, receipt ? 200 : 404);
    if (req.method === "POST") {
        const chunks = []; for await (const chunk of req) chunks.push(chunk);
        const payload = await new Request(url, { method: "POST", headers: req.headers, body: Buffer.concat(chunks), duplex: "half" }).formData();
        if (url.pathname.endsWith("/DiscardDraft")) {
            discards++;
            assert.equal(payload.get("__RequestVerificationToken"), "local-csrf");
            assert.equal(payload.get("submissionKey"), lastPayload.get("SubmissionKey"));
            if (failDiscard) return json({ message: "No fue posible verificar el envío. Conservamos el borrador." }, 502);
            receipt = { received: true, status: "discarded", submissionKey: payload.get("submissionKey"), message: "Borrador descartado" };
            return json(receipt);
        }
        if (url.pathname.endsWith("/Finalize")) {
            posts++; lastPayload = payload;
            if (scenario === "correctable") return json({ message: "Revisa los datos y firma nuevamente" }, 400);
            const notes = JSON.parse(payload.get("AnswersJson")).find(x => x.key === "activity_notes")?.value;
            if (notes) {
                assert.equal(notes, "Primera tarea\nSegunda tarea");
                assert.equal(payload.get("WorkPerformed"), "Primera tarea\r\nSegunda tarea");
            }
            receipt = { received: true, status: "needs_review", submissionKey: payload.get("SubmissionKey"), message: "Los datos de la atención cambiaron. Revisa el formulario y firma nuevamente." };
            return json(receipt, 202);
        }
    }
    if (/^\/(js|css)\/copiers-mto-v2[-a-z]*\.(js|css)$/.test(url.pathname)) {
        res.writeHead(200, { "Content-Type": url.pathname.endsWith(".js") ? "text/javascript" : "text/css" });
        return res.end(fs.readFileSync(path.join(root, "wwwroot", url.pathname)));
    }
    res.writeHead(200, { "Content-Type": "text/html" }); res.end(html);
});

async function fillSignedDraft(page, other) {
    await page.waitForFunction(() => !document.querySelector("#mtoV2DiscardDraft").disabled);
    await page.locator("#mtoV2MaintenanceType").selectOption(other ? "other" : "100000001");
    await page.locator("#mtoV2ClientName").fill("CLIENTE");
    await page.locator('#mtoV2ClientOptions [role="option"]').first().click();
    const localTime = offset => new Date(Date.now() - (5 * 60 + offset) * 60000).toISOString().slice(0, 16);
    await page.locator("#mtoV2ServiceStartedAtLocal").fill(localTime(60));
    if (other) await page.locator("#mtoV2ServiceEndedAtLocal").fill(localTime(30));
    else {
        await page.locator("#mtoV2EquipmentSerial").fill("TEST");
        await page.locator('#mtoV2EquipmentOptions [role="option"]').first().click();
    }
    await page.locator('[data-next-step="2"]').click();
    if (!other) {
        await page.locator("#mtoV2ServiceResult").selectOption({ index: 1 });
        await page.locator("#mtoV2CopiesAfter").fill("100");
        await page.locator("#mtoV2ScansAfter").fill("10");
    }
    await page.locator("#mtoV2WorkPerformed").fill("Primera tarea\nSegunda tarea");
    await page.locator("#mtoV2EvidenceInput").setInputFiles({ name: "local.png", mimeType: "image/png", buffer: Buffer.from("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAusB9Wl6Ze0AAAAASUVORK5CYII=", "base64") });
    await page.locator('[data-next-step="3"]').click();
    await page.locator("#mtoV2SignerName").fill("FIRMA LOCAL DE PRUEBA");
    await page.locator('[name="SignerRole"]').fill("Pruebas");
    await page.locator("#mtoV2CustomerAcceptance").check();
    const box = await page.locator("#mtoV2SignatureCanvas").boundingBox();
    await page.mouse.move(box.x + 20, box.y + 20); await page.mouse.down();
    for (let i = 1; i <= 15; i++) await page.mouse.move(box.x + 20 + i * 8, box.y + 20 + (i % 2) * 25);
    await page.mouse.up();
    await page.locator('[data-next-step="4"]').click();
    await page.locator("#mtoV2FinalReviewConfirmed").check();
    await page.waitForFunction(async owner => (await window.CopiersMtoV2Drafts.read(owner))?.strokes.length > 0, owner);
}

(async () => {
    await new Promise(resolve => server.listen(0, "127.0.0.1", resolve));
    const base = `http://127.0.0.1:${server.address().port}/CopiersMtoV2`;
    const browser = await chromium.launch({ channel: "msedge", headless: true });
    const outcomes = [];
    try {
        for (scenario of ["local", "rejected", "correctable"]) {
            receipt = null; posts = 0; discards = 0; failDiscard = false; lastPayload = null;
            const context = await browser.newContext({ viewport: { width: 390, height: 844 }, timezoneId: "America/Bogota" });
            context.setDefaultTimeout(12000);
            const page = await context.newPage(), errors = [];
            page.on("pageerror", error => errors.push(error.message));
            await page.goto(base); await fillSignedDraft(page, scenario === "rejected");
            const originalKey = await page.locator("#mtoV2SubmissionKey").inputValue();
            if (scenario !== "local") {
                await page.locator("#mtoV2SubmitButton").click();
                if (scenario === "correctable") {
                    await page.locator('[data-step-panel="1"]').waitFor({ state: "visible" });
                    assert.equal(await page.locator('[data-next-step="2"]').isEnabled(), true);
                    assert.equal(await page.locator("#mtoV2SignaturePointCount").inputValue(), "0");
                    assert.equal(await page.locator("#mtoV2WorkPerformed").inputValue(), "Primera tarea\nSegunda tarea");
                    assert.equal(await page.locator("#mtoV2FileList img").count(), 1);
                } else {
                    await page.locator("#mtoV2SubmitStatus").filter({ hasText: "Los datos" }).waitFor();
                    await page.reload();
                    await page.locator("#mtoV2SubmitStatus").filter({ hasText: "Los datos" }).waitFor();
                    assert.equal(posts, 1, "Recovery must query the receipt instead of resending rejected content");
                    failDiscard = true;
                    page.once("dialog", dialog => dialog.accept());
                    await page.locator("#mtoV2DiscardDraft").click();
                    await page.locator("#mtoV2RecoveryStatus").filter({ hasText: "No fue posible verificar" }).waitFor();
                    assert.equal(await page.locator("#mtoV2SubmissionKey").inputValue(), originalKey);
                    assert.equal((await page.evaluate(owner => window.CopiersMtoV2Drafts.read(owner), owner)).files.length, 1);
                    failDiscard = false;
                }
            }
            page.once("dialog", dialog => dialog.dismiss());
            await page.locator("#mtoV2DiscardDraft").click();
            assert.equal(await page.locator("#mtoV2SubmissionKey").inputValue(), originalKey);
            const beforeReset = discards;
            page.once("dialog", dialog => dialog.accept());
            await page.locator("#mtoV2DiscardDraft").click();
            await page.waitForFunction(key => document.querySelector("#mtoV2SubmissionKey").value !== key, originalKey);
            await page.waitForFunction(() => !document.querySelector("#mtoV2DiscardDraft").disabled);
            assert.equal(await page.locator("#mtoV2ClientName").inputValue(), "");
            assert.equal(await page.locator("#mtoV2WorkPerformed").inputValue(), "");
            assert.equal(await page.locator("#mtoV2SignaturePointCount").inputValue(), "0");
            assert.equal(await page.locator("#mtoV2FileList img").count(), 0);
            assert.equal(await page.locator("#mtoV2FinalReviewConfirmed").isChecked(), false);
            assert.equal(await page.evaluate(owner => window.CopiersMtoV2Drafts.read(owner), owner), undefined);
            assert.equal(discards - beforeReset, scenario === "local" ? 0 : 1);
            assert.equal(posts, scenario === "local" ? 0 : 1);
            assert.deepEqual(errors, []);
            outcomes.push({ scenario, posts, discards, cleared: true });
            await context.close();
        }
        console.log(JSON.stringify({ passed: true, outcomes }));
    } finally { await browser.close(); await new Promise(resolve => server.close(resolve)); }
})().catch(error => { console.error(error); process.exitCode = 1; server.close(); });
