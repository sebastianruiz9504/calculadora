"use strict";

const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const view = fs.readFileSync(path.join(__dirname, "..", "Views", "Calculator", "Index.cshtml"), "utf8");
const start = view.indexOf("    function normalizeInlineMessage(value)");
const end = view.indexOf("    function formatDateOptionLabel(value)", start);
assert.ok(start >= 0 && end > start);
const { parseErrorResponseMessage, readErrorResponseMessage } = new Function(
    `${view.slice(start, end)}\nreturn { parseErrorResponseMessage, readErrorResponseMessage };`)();
const fallback = "No fue posible enviar la solicitud.";
const cases = [
    ["<!DOCTYPE html><html lang=\"es\"><head><title>Error - Digital Tech</title></head></html>", fallback],
    ["<html><body>Sign in</body></html>", fallback],
    ["<div>Error del proxy</div>", fallback],
    ["", fallback],
    ["  Debes seleccionar un cliente.  ", "Debes seleccionar un cliente."],
    ['"Nombre de producto requerido."', "Nombre de producto requerido."],
    [JSON.stringify({ message: "No se pudo guardar.", traceId: "trace-123" }), "No se pudo guardar. TraceId: trace-123"],
    [JSON.stringify({ message: "Error del flujo.", detail: "<html>Internal error</html>" }), "Error del flujo."],
    [JSON.stringify({ message: "<html>Error</html>" }), fallback],
    ["El valor debe ser < 100.", "El valor debe ser < 100."]
];
for (const [body, expected] of cases)
    assert.equal(parseErrorResponseMessage(body, fallback), expected);

const submitStart = view.indexOf("    async function submitProvisioningRequest()");
const submitEnd = view.indexOf('    scenarioName.addEventListener("input"', submitStart);
assert.ok(submitStart >= 0 && submitEnd > submitStart);
const createSubmit = new Function("env", `
    const { provisioningState, setProvisioningStatus, buildProvisioningPayload, fetch,
        readErrorResponseMessage, btnConfirmProvisioning, PROVISIONING_SUBMIT_URL,
        parseErrorResponseMessage, getActiveScenario, appendProvisioningLog,
        syncScenarioActionButtons, updateCalculatorCrmLink, setTimeout, closeProvisioningModal } = env;
    ${view.slice(submitStart, submitEnd)}
    return submitProvisioningRequest;`);

(async () => {
    assert.equal(await readErrorResponseMessage({ text: async () => cases[0][0] }, fallback), fallback);
    assert.equal(await readErrorResponseMessage({ text: async () => { throw new Error("network"); } }, fallback), fallback);
    for (const [status, body, expectedId] of [
        [500, cases[0][0], ""],
        [200, cases[1][0], ""],
        [200, JSON.stringify({ ok: true, requestId: "accepted" }), "accepted"],
        [503, JSON.stringify({ flowAccepted: true, requestId: "already-sent", message: "CRM pendiente" }), "already-sent"]
    ]) {
        const scenario = { provisioningRequestId: "" };
        const messages = [];
        const provisioningState = { loading: false };
        const button = {};
        let fetchCount = 0;
        const submit = createSubmit({
            provisioningState,
            btnConfirmProvisioning: button,
            PROVISIONING_SUBMIT_URL: "/Calculator/SubmitProvisioning",
            readErrorResponseMessage,
            parseErrorResponseMessage,
            buildProvisioningPayload: () => ({ lineItems: [{}], cliente: { nombre: "Cliente" } }),
            fetch: async (_url, options) => {
                assert.equal(options.headers.Accept, "application/json");
                return ++fetchCount === 1
                    ? { ok: true }
                    : { ok: status === 200, status, text: async () => body };
            },
            getActiveScenario: () => scenario,
            setProvisioningStatus: text => messages.push(text),
            appendProvisioningLog: text => messages.push(text),
            syncScenarioActionButtons: () => {},
            updateCalculatorCrmLink: () => {},
            setTimeout: () => {},
            closeProvisioningModal: () => {}
        });
        await submit();
        assert.equal(scenario.provisioningRequestId, expectedId);
        assert.equal(button.disabled, Boolean(expectedId));
        assert.equal(provisioningState.loading, false);
        assert.equal(fetchCount, 2);
        assert.ok(messages.every(message => !message.includes("<html") && !message.includes("<!DOCTYPE")));
    }
    console.log(`Calculator API errors: OK (${cases.length + 6} cases).`);
})().catch(error => { console.error(error); process.exitCode = 1; });
