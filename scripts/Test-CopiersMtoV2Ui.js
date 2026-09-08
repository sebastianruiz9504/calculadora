"use strict";

const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const { test } = require("node:test");
const projectRoot = path.resolve(__dirname, "..");
const script = fs.readFileSync(path.join(projectRoot, "wwwroot/js/copiers-mto-v2.js"), "utf8");
const view = fs.readFileSync(path.join(projectRoot, "Views/CopiersMtoV2/Index.cshtml"), "utf8");

function functionSource(name) {
    const start = script.search(new RegExp(`    (?:async )?function ${name}\\(`));
    assert.ok(start >= 0, `${name} exists`);
    const rest = script.slice(start);
    const next = rest.slice(1).search(/\n    (?:async )?function /);
    return next >= 0 ? rest.slice(0, next + 1) : rest.slice(0, rest.lastIndexOf("})()"));
}

function bind(name, dependencies = {}) {
    return new Function(...Object.keys(dependencies), `${functionSource(name)}\nreturn ${name};`)(...Object.values(dependencies));
}

function control(value = "") {
    return {
        value, validityMessage: "", disabled: false,
        classList: { add() {}, remove() {} },
        setCustomValidity(message) { this.validityMessage = message; },
        focus() {}, reportValidity() { return Boolean(this.value.trim()); }
    };
}

const catalogKey = value => String(value || "").trim().toLowerCase();
const sameCatalogId = (left, right) => Boolean(left && right) && catalogKey(left) === catalogKey(right);

test("automatic order is read-only and never copied from equipment", () => {
    const input = view.match(/<input[^>]+id="mtoV2ServiceReference"[^>]*>/)?.[0];
    assert.match(input, /readonly/);
    assert.match(input, /placeholder="Se asigna al enviar"/);
    assert.doesNotMatch(functionSource("syncEquipmentSelection"), /serviceReference/);
    assert.doesNotMatch(functionSource("buildStructuredAnswers"), /service_reference/);
    assert.match(functionSource("submitForm"), /textProperty\(result, "serviceReference", "ServiceReference"\)/);
});

test("no visible location controls, GPS messages, or step-one location prerequisite", () => {
    const visibleCopy = view.replace(/<[^>]*>/g, "");
    assert.doesNotMatch(visibleCopy, /ubicaci[oó]n|coordenada|\bGPS\b/i);
    assert.doesNotMatch(view + script, /mtoV2CaptureLocation|mtoV2GeoFeedback/);
    assert.doesNotMatch(functionSource("validateStep"), /geo|location/i);
    assert.doesNotMatch(functionSource("captureGeolocation"), /setStatus|textContent|alert\(/);
});

test("email editor is outside the maintenance form and hidden controls cannot block a step", () => {
    assert.ok(view.indexOf("</form>") < view.indexOf('id="mtoV2ClientEmailDialog"'));
    assert.match(functionSource("validateStep"), /!control\.closest\("\[hidden\]"\)/);
    assert.match(functionSource("showStep"), /normalizedStep === 3[\s\S]*resizeSignatureCanvas\(\)/);
});

test("equipment requires a persisted Dataverse id", () => {
    const normalize = bind("normalizeEquipment", { textProperty: bind("textProperty") });
    assert.equal(normalize({ serial: "S-1", clientId: "C-1" }), null);
    assert.equal(normalize({ id: "E-1", serial: "S-1", clientId: "C-1" }).id, "E-1");
});

function catalogValidation({ email = "copiers@example.test", serial = "S-1", selectedEquipment = { id: "E-1" }, equipment = [{ id: "E-1", serial: "S-1", clientId: "C-1" }] } = {}) {
    const elements = { clientName: control("Cliente"), clientId: control("C-1"), equipmentSerial: control(serial) };
    const state = { catalog: { loaded: true, schemaReady: true, selectedClient: { email }, selectedEquipment, equipment } };
    bind("prepareCatalogValidity", { elements, state, catalogKey, sameCatalogId })();
    return elements;
}

test("known client and equipment with stored email can continue", () => {
    const elements = catalogValidation();
    assert.equal(elements.clientName.validityMessage, "");
    assert.equal(elements.equipmentSerial.validityMessage, "");
});

test("empty client email directs user to always-available plus editor", () => {
    assert.match(catalogValidation({ email: "" }).clientName.validityMessage, /botón \+/);
});

test("external serial cannot continue", () => {
    assert.match(catalogValidation({ serial: "UNKNOWN", selectedEquipment: null }).equipmentSerial.validityMessage, /equipo registrado/);
});

test("serial owned by another client cannot continue", () => {
    assert.match(catalogValidation({ selectedEquipment: null, equipment: [{ serial: "S-1", clientId: "C-2" }] }).equipmentSerial.validityMessage, /otro cliente/);
});

for (const email of ["", "existing@example.test"]) {
    test(`plus opens editor with ${email ? "existing" : "empty"} email`, () => {
        let opened = false;
        const state = { catalog: { selectedClient: { id: "C-1", name: "Cliente", email } } };
        const elements = { emailClientName: {}, clientEmailEditor: control(), clientEmailDialog: { showModal() { opened = true; } } };
        bind("openClientEmailEditor", { state, elements, clearStatus() {} })();
        assert.equal(opened, true);
        assert.equal(elements.clientEmailEditor.value, email);
        assert.equal(state.editingClientId, "C-1");
    });
}

function captureHarness(getCurrentPosition, secure = true) {
    const timers = [];
    const elements = Object.fromEntries(["latitude", "longitude", "accuracy", "geoCapturedAtUtc", "geoStatus"].map(key => [key, control()]));
    const capture = bind("captureGeolocation", {
        elements, navigator: { geolocation: { getCurrentPosition } },
        window: { isSecureContext: secure, setTimeout(callback) { timers.push(callback); return timers.length; }, clearTimeout() {} }
    });
    return { capture, elements, timers };
}

test("send captures actual coordinates and precision internally", async () => {
    const h = captureHarness(success => success({ coords: { latitude: 4.711, longitude: -74.0721, accuracy: 500 } }));
    await h.capture();
    assert.equal(h.elements.latitude.value, "4.7110000");
    assert.equal(h.elements.longitude.value, "-74.0721000");
    assert.equal(h.elements.accuracy.value, "500.0");
    assert.equal(h.elements.geoStatus.value, "captured");
});

test("browser denial resolves without blocking send or inventing coordinates", async () => {
    const h = captureHarness((_, failure) => failure({ code: 1 }));
    await h.capture();
    assert.equal(h.elements.geoStatus.value, "denied");
    assert.equal(h.elements.latitude.value, "");
});

test("unsupported browser resolves without blocking send", async () => {
    const h = captureHarness(() => assert.fail("must not request"), false);
    await h.capture();
    assert.equal(h.elements.geoStatus.value, "unsupported");
});

test("timeout bounds send and ignores late permission response", async () => {
    let lateSuccess;
    const h = captureHarness(success => { lateSuccess = success; });
    const pending = h.capture();
    h.timers[0]();
    await pending;
    lateSuccess({ coords: { latitude: 1, longitude: 2, accuracy: 3 } });
    assert.equal(h.elements.geoStatus.value, "timeout");
    assert.equal(h.elements.latitude.value, "");
});

test("submission captures once, preserves retry key, and freezes visible controls", () => {
    const submit = functionSource("submitForm");
    assert.match(submit, /if \(!state\.locationAttempted\)/);
    assert.ok(submit.indexOf("await captureGeolocation()") < submit.indexOf("new FormData(form)"));
    assert.match(submit, /"Idempotency-Key": elements\.submissionKey\.value/);
    assert.match(submit, /submittedAtUtc\.value \|\|=/);
    assert.match(functionSource("setSubmitState"), /panel\.inert = pending \|\| completed/);
});

function emailHarness(result, ok = true) {
    const client = { id: "C-1", name: "Cliente", email: "" };
    const state = { editingClientId: "C-1", catalog: { clients: [client], selectedClient: client } };
    let request, closed = false, invalidated = false, error = "";
    const elements = {
        clientEmailEditor: control("new@example.test"), saveClientEmail: {}, cancelClientEmail: {},
        onsiteContactEmail: control(), clientName: control("Cliente"), clientEmailDialog: { close() { closed = true; } }
    };
    const save = bind("saveClientEmail", {
        state, elements, root: { dataset: {} },
        form: { querySelector() { return { value: "csrf-token" }; } },
        fetch: async (url, options) => { request = { url, options }; return { ok }; },
        readResponse: async () => result, textProperty: bind("textProperty"), sameCatalogId,
        clearStatus() {}, setCatalogFeedback() {}, setStatus(_, __, message) { error = message; },
        invalidateSignatureForChange() { invalidated = true; }
    });
    return { save, state, elements, result: () => ({ request, closed, invalidated, error }) };
}

test("email saves through antiforgery JSON endpoint and invalidates old signature", async () => {
    const h = emailHarness({ clientId: "C-1", email: "new@example.test" });
    await h.save({ preventDefault() {} });
    const actual = h.result();
    assert.equal(actual.request.url, "/CopiersMtoV2/SaveClientEmail");
    assert.equal(actual.request.options.headers.RequestVerificationToken, "csrf-token");
    assert.deepEqual(JSON.parse(actual.request.options.body), { clientId: "C-1", email: "new@example.test" });
    assert.equal(h.state.catalog.selectedClient.email, "new@example.test");
    assert.equal(actual.closed, true);
    assert.equal(actual.invalidated, true);
});

test("email persistence failure keeps editor open and previous client email untouched", async () => {
    const h = emailHarness({ message: "No guardado" }, false);
    await h.save({ preventDefault() {} });
    assert.equal(h.result().closed, false);
    assert.equal(h.result().error, "No guardado");
    assert.equal(h.state.catalog.selectedClient.email, "");
    assert.equal(h.elements.saveClientEmail.disabled, false);
});

test("email response for wrong client is rejected", async () => {
    const h = emailHarness({ clientId: "C-2", email: "new@example.test" });
    await h.save({ preventDefault() {} });
    assert.equal(h.result().closed, false);
    assert.equal(h.state.catalog.selectedClient.email, "");
    assert.match(h.result().error, /No se confirmó/);
});

test("empty email cannot be submitted", async () => {
    const h = emailHarness({ clientId: "C-1", email: "" });
    h.elements.clientEmailEditor.value = "   ";
    await h.save({ preventDefault() {} });
    assert.equal(h.result().request, undefined);
});
