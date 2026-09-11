// Local-only browser fixture. Never connects to Dataverse or sends customer mail.
const fs = require("node:fs"), path = require("node:path"), http = require("node:http"), assert = require("node:assert/strict");
const { chromium } = require("playwright");
const root = path.resolve(__dirname, ".."), view = fs.readFileSync(path.join(root, "Views/CopiersEquipmentOperations/Index.cshtml"), "utf8");
const owner = "00000000-0000-0000-0000-000000000001/00000000-0000-0000-0000-000000000002";
const markup = view.slice(view.indexOf("<main"), view.indexOf("@section Scripts")).replace('@owner', owner).replace('@Html.AntiForgeryToken()', '<input name="__RequestVerificationToken" value="local-only" type="hidden">');
const html = '<!doctype html><html><head><meta name="viewport" content="width=device-width,initial-scale=1"><link rel="stylesheet" href="/css/copiers-mto-v2.css"><link rel="stylesheet" href="/css/copiers-equipment-operations.css"></head><body>' + markup + ["copiers-mto-v2-picker", "copiers-mto-v2-drafts", "copiers-equipment-operations"].map(x => `<script src="/js/${x}.js"></script>`).join("") + "</body></html>";
const clientA = { id: "a", name: "Cliente A", email: "a@example.test", contactName: "Persona A", isInternal: false }, clientB = { id: "b", name: "Cliente B", email: "b@example.test", contactName: "Persona B", isInternal: false };
const catalog = { technician: "Técnico local", clients: [clientA, clientB, { id: "depot", name: "Depósito", isInternal: true }, { id: "office", name: "Oficina", isInternal: true }], equipment: [
    { id: "1", serial: "INTERNO-001", reference: "Equipo nuevo", clientId: "depot", clientName: "Depósito" },
    { id: "2", serial: "CLIENTE-002", reference: "Equipo anterior", clientId: "a", clientName: "Cliente A" },
    { id: "3", serial: "PENDIENTE-003", reference: "Equipo trasladado", clientId: "", clientName: "En tránsito", pending: { operationKey: "departure", originId: "a", originName: "Cliente A", destinationId: "b", destinationName: "Cliente B" } },
    { id: "4", serial: "PENDIENTE-004", reference: "Equipo interno", clientId: "", clientName: "En tránsito", pending: { operationKey: "departure2", originId: "a", originName: "Cliente A", destinationId: "depot", destinationName: "Depósito" } }
] };
let receipts = new Map(), posts = [], mode = "normal";
const server = http.createServer(async (req, res) => {
    const url = new URL(req.url, "http://localhost");
    const json = (value, status = 200) => { res.writeHead(status, { "Content-Type": "application/json" }); res.end(JSON.stringify(value)); };
    if (url.pathname.endsWith("/Bootstrap")) return json(catalog);
    if (url.pathname.endsWith("/SubmissionStatus")) { const key = url.searchParams.get("submissionKey"); return receipts.has(key) ? json(receipts.get(key)) : json({}, 404); }
    if (url.pathname === "/CopiersMtoV2/Status") return json({ emailState: 3 });
    if (url.pathname.endsWith("/Submit")) {
        let body = ""; for await (const chunk of req) body += chunk.toString("latin1");
        posts.push(body); const key = req.headers["idempotency-key"];
        if (mode === "validation") return json({ message: "Estado de equipo inválido" }, 400);
        const internal = /name="InternalConfirmed"\r\n\r\ntrue/.test(body);
        const receipt = { status: "completed", received: true, result: { recordId: key, serviceReference: "ACT-LOCAL", emailRequired: !internal } }; receipts.set(key, receipt);
        return mode === "uncertain" ? json({ message: "Proxy local perdió la respuesta" }, 503) : json(receipt, 202);
    }
    if (/^\/(js|css)\/copiers-[a-z0-9-]+\.(js|css)$/.test(url.pathname)) { res.writeHead(200, { "Content-Type": url.pathname.endsWith("js") ? "text/javascript" : "text/css" }); return res.end(fs.readFileSync(path.join(root, "wwwroot", url.pathname))); }
    res.writeHead(200, { "Content-Type": "text/html; charset=utf-8" }); res.end(html);
});
(async () => {
    await new Promise(resolve => server.listen(0, "127.0.0.1", resolve));
    const base = `http://127.0.0.1:${server.address().port}`, browser = await chromium.launch({ channel: "msedge", headless: true });
    const errors = []; let checks = 0;
    const context = await browser.newContext({ viewport: { width: 800, height: 1100 }, hasTouch: true });
    context.setDefaultTimeout(10000);
    async function choose(page, key, text) { await page.locator(`#eo${key}Search`).fill(text); await page.locator(`#eo${key}List [data-picker-id]`).first().tap(); }
    async function details(page, kind) {
        await page.locator('[name="OperationKind"]').selectOption(kind);
        if (kind !== "internal" && kind !== "receipt") await choose(page, "Client", "Cliente A");
        if (["withdrawal", "replacement", "internal"].includes(kind)) await choose(page, "Destination", kind === "withdrawal" ? "Cliente B" : "Oficina");
        if (kind === "receipt") await page.locator("#eoPending").selectOption("3");
        else await choose(page, "Equipment", ["delivery", "internal"].includes(kind) ? "INTERNO" : "CLIENTE");
        if (kind === "replacement") { await choose(page, "Replacement", "INTERNO"); await page.locator('[name="ReplacementCondition"]').fill("Operativo nuevo"); }
        await page.locator('[name="EquipmentCondition"]').fill("Operativo revisado"); await page.locator('[name="MovementReason"]').fill("Prueba exclusivamente local");
        await page.locator("#eoNext").tap(); await page.locator("#eoReview").waitFor({ state: "visible" });
    }
    async function sign(page) {
        await page.locator('[name="SignerName"]').fill("Firmante de prueba"); await page.locator('[name="SignerRole"]').fill("Pruebas"); await page.locator('[name="CustomerAccepted"]').check();
        await page.locator("#eoSignature").scrollIntoViewIfNeeded(); const box = await page.locator("#eoSignature").boundingBox();
        await page.mouse.move(box.x + 20, box.y + 20); await page.mouse.down(); for (let i = 1; i <= 20; i++) await page.mouse.move(box.x + 20 + i * 10, box.y + 20 + (i % 2) * 30); await page.mouse.up();
    }
    try {
        let page = await context.newPage(); page.on("pageerror", x => errors.push(x.message));
        await page.goto(base); await page.waitForFunction(() => !document.querySelector("#eoFields").disabled);
        await details(page, "replacement"); checks++;
        const summary = await page.locator("#eoSummary").innerText(); assert.match(summary, /CLIENTE-002/); assert.match(summary, /INTERNO-001/); assert.match(summary, /Salida:/); assert.doesNotMatch(summary, /Oficina|Depósito|Cliente B/); checks++;
        await sign(page); const before = await page.locator("#eoSignature").evaluate(c => c.toDataURL());
        await page.locator("#eoCamera").setInputFiles({ name: "foto.png", mimeType: "image/png", buffer: Buffer.from("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAusB9Wl6Ze0AAAAASUVORK5CYII=", "base64") });
        assert.equal(await page.locator("#eoSignature").evaluate(c => c.toDataURL()), before); checks++;
        await page.reload(); await page.locator("#eoReview").waitFor({ state: "visible" }); assert.equal(await page.locator("#eoSignature").evaluate(c => c.toDataURL()), before); assert.match(await page.locator("#eoPhotos").innerText(), /foto.png/); checks++;
        mode = "uncertain"; await page.locator("#eoSend").tap(); await page.locator("#eoRetry").waitFor({ state: "visible" }); assert.equal(posts.length, 1); assert.equal(await page.locator("#eoAnother").isVisible(), false);
        await page.reload(); await page.locator("#eoAnother").waitFor({ state: "visible" }); assert.equal(posts.length, 1); checks++;
        mode = "normal"; await page.locator("#eoAnother").tap(); await page.waitForFunction(() => !document.querySelector("#eoFields").disabled);
        await details(page, "receipt"); const receiptSummary = await page.locator("#eoSummary").innerText(); assert.match(receiptSummary, /Cliente B/); assert.doesNotMatch(receiptSummary, /Cliente A/); await sign(page); await page.locator("#eoSend").tap(); await page.locator("#eoAnother").waitFor({ state: "visible" }); assert.match(posts[1], /name="PendingKey"\r\n\r\ndeparture/); checks++;
        await page.locator("#eoAnother").tap(); await page.waitForFunction(() => !document.querySelector("#eoFields").disabled);
        await details(page, "internal"); assert.equal(await page.locator("#eoSignature").isVisible(), false); await page.locator('[name="InternalConfirmed"]').check(); await page.locator("#eoSend").tap(); await page.locator("#eoAnother").waitFor({ state: "visible" }); assert.doesNotMatch(posts[2], /name="Signature"/); assert.match(await page.locator("#eoStatus").innerText(), /No requiere correo/); checks++;
        await page.locator("#eoAnother").tap(); await page.waitForFunction(() => !document.querySelector("#eoFields").disabled);
        await details(page, "delivery"); await sign(page); mode = "validation"; await page.locator("#eoSend").tap(); await page.locator("#eoDetails").waitFor({ state: "visible" }); assert.equal(await page.locator("#eoFields").isDisabled(), false); assert.match(await page.locator("#eoStatus").innerText(), /todavía no se recibió/); checks++;
        const second = await context.newPage(); await second.goto(base); await second.waitForFunction(() => document.querySelector("#eoStatus").textContent.includes("otra pestaña")); assert.equal(await second.locator("#eoFields").evaluate(x => x.disabled), true); checks++;
        assert.deepEqual(errors, []); console.log(JSON.stringify({ passed: checks, browser: "Edge headless touch 800x1100", productionWrites: 0, emails: 0 }));
    } finally { await context.close(); await browser.close(); server.close(); }
})().catch(error => { console.error(error); server.close(); process.exitCode = 1; });
