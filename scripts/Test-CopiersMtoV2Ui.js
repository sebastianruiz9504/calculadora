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

test("reload starts a new capture instead of restoring a key without its form", () => {
    const elements = { submissionKey: control(), startedAtUtc: control(), previousAttempt: { hidden: true } };
    bind("initializeSubmission", {
        elements, readStoredSubmissionId: () => "previous-attempt-key",
        createSubmissionId: () => "fresh-form-key"
    })();
    assert.equal(elements.submissionKey.value, "fresh-form-key");
    assert.equal(elements.previousAttempt.hidden, false);
    assert.match(view, /Hay un envío anterior sin confirmar/);
});

function submissionIdentityHarness() {
    const elements = Object.fromEntries(["submissionKey", "recordId", "expectedVersion", "serviceReference", "submittedAtUtc", "latitude", "longitude", "accuracy", "geoCapturedAtUtc", "geoStatus", "clientId", "equipmentId"].map(key => [key, control("existing")]));
    elements.submissionKey.value = "first-attempt";
    elements.clientId.value = "CLIENT-A";
    elements.equipmentId.value = "EQUIPMENT-A";
    const state = { submissionAttempted: false, submissionScope: "", locationAttempted: true };
    const stored = [];
    const prepare = bind("prepareSubmissionIdentity", {
        elements, state, createSubmissionId: () => "new-maintenance",
        storeSubmissionId(value) { stored.push(value); }
    });
    return { elements, state, stored, prepare };
}

test("retry in the same form preserves the key and original capture", () => {
    const h = submissionIdentityHarness();
    h.prepare();
    h.prepare();
    assert.equal(h.elements.submissionKey.value, "first-attempt");
    assert.equal(h.state.locationAttempted, true);
    assert.equal(h.elements.latitude.value, "existing");
    assert.deepEqual(h.stored, ["first-attempt", "first-attempt"]);
});

for (const field of ["clientId", "equipmentId"]) {
    test(`a new ${field} after a failed send gets a new key without overwriting the previous row`, () => {
        const h = submissionIdentityHarness();
        h.prepare();
        h.elements[field].value = "NEW-SELECTION";
        h.prepare();
        assert.equal(h.elements.submissionKey.value, "new-maintenance");
        for (const key of ["recordId", "expectedVersion", "serviceReference", "submittedAtUtc", "latitude", "longitude", "accuracy", "geoCapturedAtUtc"]) assert.equal(h.elements[key].value, "");
        assert.equal(h.state.locationAttempted, false);
        assert.equal(h.elements.geoStatus.value, "pending");
    });
}

test("identity is prepared only after validation and before building the send payload", () => {
    const submit = functionSource("submitForm");
    assert.ok(submit.indexOf("validateAllSteps()") < submit.indexOf("prepareSubmissionIdentity()"));
    assert.ok(submit.indexOf("prepareSubmissionIdentity()") < submit.indexOf("new FormData(form)"));
});

test("automatic order is read-only and never copied from equipment", () => {
    const input = view.match(/<input[^>]+id="mtoV2ServiceReference"[^>]*>/)?.[0];
    assert.match(input, /readonly/);
    assert.match(input, /placeholder="Se asigna al enviar"/);
    assert.doesNotMatch(functionSource("syncEquipmentSelection"), /serviceReference/);
    assert.doesNotMatch(functionSource("buildStructuredAnswers"), /service_reference/);
    assert.match(functionSource("submitForm"), /textProperty\(result, "serviceReference", "ServiceReference"\)/);
});

test("signer identification is absent from the form, review and new submissions", () => {
    assert.doesNotMatch(view + script, /SignerDocument|signer_document|Sin identificación|Identificación de quien firma/);
    assert.match(view, /name="SignerName"[^>]*required/);
    assert.match(view, /name="SignerRole"[^>]*required/);
});

test("structured answers never request or submit signer identification", () => {
    const requestedFields = [];
    const answers = bind("buildStructuredAnswers", {
        state: { catalog: { selectedEquipment: { reference: "Equipo de prueba" } }, counters: {} },
        valueOf(id) { requestedFields.push(id); return "Dato de prueba"; },
        selectedText() { return "Preventivo"; },
        formatLocalDateTime(value) { return value; },
        buildCountersSummary() { return "No aplica"; }
    })();
    assert.ok(answers.length > 0);
    assert.ok(answers.every(answer => answer.key !== "signer_document"));
    assert.ok(requestedFields.every(id => id !== "mtoV2SignerDocument"));
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

test("mobile catalogs use app-owned lists, not native datalist", () => {
    assert.doesNotMatch(view, /<datalist|\blist="mtoV2/);
    assert.match(view, /id="mtoV2ClientOptions"[^>]*hidden/);
    assert.ok(view.indexOf("copiers-mto-v2-picker.js") < view.indexOf('src="~/js/copiers-mto-v2.js"'));
    assert.ok(functionSource("initialize").indexOf("void loadBootstrap()") < functionSource("initialize").indexOf("renderFiles()"));
});

test("child replacement supports older mobile DOM implementations", () => {
    const children = ["old"];
    const parent = {
        get firstChild() { return children[0]; },
        removeChild() { children.shift(); }, appendChild(value) { children.push(value); }
    };
    bind("replaceContents")(parent, "new", "second");
    assert.deepEqual(children, ["new", "second"]);
    assert.doesNotMatch(script, /elements\.[\w]+\??\.replaceChildren/);
});

function responseHarness(response) {
    const requests = [];
    const read = bind("readCatalogResponse", {
        root: { dataset: {} },
        fetch: async (url, options) => { requests.push({ url, options }); if (response instanceof Error) throw response; return response; },
        readResponse: bind("readResponse")
    });
    return { read, requests };
}

for (const variant of [
    { status: 401, ok: false }, { status: 403, ok: false },
    { status: 200, ok: true, redirected: true },
    { status: 200, ok: true, headers: { get: () => "text/html" } }
]) test(`bootstrap rejects expired or redirected session ${JSON.stringify(variant)}`, async () => {
    await assert.rejects(responseHarness(variant).read(), /sesión/);
});

test("bootstrap distinguishes network failure and remains a read-only no-store request", async () => {
    const h = responseHarness(new TypeError("network failed"));
    await assert.rejects(h.read(), /Revisa internet/);
    assert.equal(h.requests[0].options.method, "GET");
    assert.equal(h.requests[0].options.cache, "no-store");
    assert.equal(h.requests[0].options.credentials, "same-origin");
});

test("bootstrap keeps explicit server error instead of treating it as an empty catalog", async () => {
    await assert.rejects(responseHarness({ ok: false, status: 502, headers: { get: () => "application/json" }, json: async () => ({ message: "Error controlado" }) }).read(), /Error controlado/);
});

test("bootstrap reads a complete successful JSON catalog", async () => {
    const result = { clients: [{ id: "C-1", name: "Cliente" }], equipment: [], schemaReady: true };
    assert.deepEqual(await responseHarness({ ok: true, status: 200, headers: { get: () => "application/json" }, json: async () => result }).read(), result);
});

function deadlineHarness(reader, withAbort = true) {
    const timers = [], cleared = [];
    let aborted = false;
    const run = bind("fetchCatalog", {
        AbortController: withAbort ? class { signal = {}; abort() { aborted = true; } } : undefined,
        window: { setTimeout(callback, delay) { timers.push({ callback, delay }); return timers.length; }, clearTimeout(id) { cleared.push(id); } },
        readCatalogResponse: reader
    });
    return { run, timers, cleared, aborted: () => aborted };
}

for (const withAbort of [true, false]) test(`bootstrap timeout is bounded and retryable with AbortController=${withAbort}`, async () => {
    let finish;
    const h = deadlineHarness(() => new Promise(resolve => { finish = resolve; }), withAbort);
    const pending = h.run();
    const rejected = assert.rejects(pending, /tardó demasiado.*Reintentar/);
    assert.equal(h.timers[0].delay, 25000);
    h.timers[0].callback();
    await rejected;
    finish({ clients: ["late response"] });
    assert.deepEqual(h.cleared, [1]);
    assert.equal(h.aborted(), withAbort);
});

test("successful bootstrap clears deadline without aborting", async () => {
    const h = deadlineHarness(async () => ({ clients: ["ok"] }));
    assert.deepEqual(await h.run(), { clients: ["ok"] });
    assert.deepEqual(h.cleared, [1]);
    assert.equal(h.aborted(), false);
});

function loadHarness(reader) {
    const elements = { retryBootstrap: {}, catalogFeedback: {}, equipmentFeedback: {}, technicianName: {} };
    const picker = () => ({ status: "", setStatus(value) { this.status = value; } });
    const state = { catalog: { loaded: false, loading: false }, clientPicker: picker(), equipmentPicker: picker() };
    const textProperty = bind("textProperty");
    const load = bind("loadBootstrap", {
        state, elements, fetchCatalog: reader,
        normalizeClient: bind("normalizeClient", { textProperty }),
        normalizeEquipment: bind("normalizeEquipment", { textProperty }),
        normalizeMaintenanceType: bind("normalizeMaintenanceType", { textProperty }), textProperty,
        setCatalogFeedback(element, message) { element.textContent = message; },
        renderClientOptions() {}, renderMaintenanceTypeOptions() {}, syncClientSelection() {}
    });
    return { elements, state, load };
}

test("failed load restores retry; a later retry loads the catalog", async () => {
    let calls = 0;
    const h = loadHarness(async () => {
        if (++calls === 1) throw new Error("Sin conexión");
        return { clients: [{ id: "C-1", name: "Cliente" }], equipment: [], schemaReady: true };
    });
    await h.load();
    assert.equal(h.state.catalog.loading, false);
    assert.equal(h.elements.retryBootstrap.hidden, false);
    assert.equal(h.elements.retryBootstrap.disabled, false);
    assert.equal(h.state.clientPicker.status, "Sin conexión");
    await h.load();
    assert.equal(h.state.catalog.loaded, true);
    assert.equal(h.elements.retryBootstrap.hidden, true);
});

function selectionHarness() {
    const clients = [{ id: "C-1", name: "Duplicado" }, { id: "C-2", name: "Duplicado" }];
    const state = { catalog: { loaded: true, schemaReady: true, clients, equipment: [], selectedClient: null, selectedEquipment: null } };
    const elements = { clientName: control("Duplicado"), clientId: control(), editClientEmail: {}, catalogFeedback: {}, equipmentId: control(), equipmentSerial: control() };
    const sync = bind("syncClientSelection", {
        state, elements, catalogKey, sameCatalogId, prefillClientContact() {}, clearClientPrefill() {},
        setCatalogFeedback() {}, renderEquipmentOptions() {}, syncEquipmentSelection() {}
    });
    return { state, elements, sync };
}

test("duplicate client names require a concrete ID and preserve the selected match", () => {
    const h = selectionHarness();
    h.sync();
    assert.equal(h.state.catalog.selectedClient, null);
    h.sync("C-2");
    assert.equal(h.elements.clientId.value, "C-2");
    h.sync();
    assert.equal(h.elements.clientId.value, "C-2");
});

test("changing client via picker clears the previous equipment selection", () => {
    const h = selectionHarness();
    h.sync("C-1");
    h.state.catalog.selectedEquipment = { id: "E-1", clientId: "C-1", serial: "OLD-1" };
    h.elements.equipmentSerial.value = "OLD-1";
    h.elements.equipmentId.value = "E-1";
    h.sync("C-2");
    assert.equal(h.state.catalog.selectedEquipment, null);
    assert.equal(h.elements.equipmentSerial.value, "");
    assert.equal(h.elements.equipmentId.value, "");
});

test("picker selection invokes authoritative ID synchronization and invalidates signature", () => {
    const callbacks = [];
    const state = {};
    const elements = { clientName: control(), clientOptions: {}, equipmentSerial: control(), equipmentOptions: {} };
    let signatureInvalidations = 0;
    const selected = [];
    bind("initializeCatalogPickers", {
        state, elements, window: { CopiersMtoV2Picker: { create(input, list, options) { callbacks.push(options.onSelect); return {}; } } },
        syncClientSelection(id) { selected.push(["client", id]); },
        syncEquipmentSelection(id) { selected.push(["equipment", id]); },
        invalidateSignatureForChange() { signatureInvalidations++; }
    })();
    callbacks[0]({ id: "C-2", label: "Duplicado" });
    callbacks[1]({ id: "E-2", label: "SERIAL-2" });
    assert.deepEqual(selected, [["client", "C-2"], ["equipment", "E-2"]]);
    assert.equal(signatureInvalidations, 2);
});

test("reduced form removes the requested fields from input, review, answers and signature contract", () => {
    assert.doesNotMatch(view + script, /mtoV2ReportedIssue|mtoV2TechnicalDiagnosis|mtoV2PartsUsed|reported_issue|technical_diagnosis|parts_used/);
    assert.match(view, /name="WorkPerformed"[^>]+required/);
    assert.match(view, /name="FormVersion"[^>]+value="copiers-mto-v2-2026-09-10"/);
});

test("previous counters are read-only with small dates while both current readings require nonnegative integers", () => {
    for (const id of ["mtoV2CopiesBefore", "mtoV2ScansBefore"]) {
        assert.match(view.match(new RegExp(`<input[^>]+id="${id}"[^>]*>`))[0], /readonly/);
        assert.match(view, new RegExp(`<small[^>]+id="${id}Date"`));
    }
    for (const id of ["mtoV2CopiesAfter", "mtoV2ScansAfter"]) {
        const input = view.match(new RegExp(`<input[^>]+id="${id}"[^>]*>`))[0];
        assert.match(input, /required/);
        assert.match(input, /min="0"/);
        assert.match(input, /step="1"/);
        assert.match(input, /max="2147483647"/);
        assert.doesNotMatch(input, /readonly/);
    }
});

test("counter normalization preserves real zero and never invents a value for absent history", () => {
    const normalize = bind("normalizeCounterValue");
    for (const value of [null, undefined, ""]) assert.equal(normalize(value), "");
    for (const value of [0, "0"]) assert.equal(normalize(value), "0");
    assert.equal(normalize(127001), "127001");
    for (const value of [-1, 1.1, "invalid", Infinity, Number.MAX_SAFE_INTEGER + 1]) assert.throws(() => normalize(value), /lectura válida/);
});

function counterHarness(reader) {
    const elements = Object.fromEntries(["copiesBefore", "copiesAfter", "scansBefore", "scansAfter"].map(key => [key, control()]));
    Object.assign(elements, { copiesBeforeDate: {}, scansBeforeDate: {}, counterFeedback: {}, retryCounters: {} });
    const state = {
        counters: { scope: "c-1|e-1", requestId: 0, loading: false, loaded: false, error: "", recordId: "", dateValue: "", dateDisplay: "" },
        catalog: { selectedEquipment: { id: "E-1", clientId: "C-1" } }
    };
    const load = bind("loadCounterLatest", {
        state, elements, fetchCounterLatest: reader, sameCatalogId,
        textProperty: bind("textProperty"), normalizeCounterValue: bind("normalizeCounterValue"),
        setCatalogFeedback(element, text, tone) { element.textContent = text; element.tone = tone; }
    });
    return { elements, state, load };
}

test("loading the latest counter displays both historical readings and their actual date", async () => {
    const calls = [];
    const h = counterHarness(async (...args) => {
        calls.push(args);
        return { equipmentId: "E-1", recordId: "R-1", copiesCounter: 300, scansCounter: 0, dateValue: "2026-09-10", dateDisplay: "10/09/2026 08:00" };
    });
    await h.load();
    assert.deepEqual(calls, [["C-1", "E-1"]]);
    assert.equal(h.elements.copiesBefore.value, "300");
    assert.equal(h.elements.scansBefore.value, "0");
    assert.equal(h.elements.copiesAfter.value, "");
    assert.equal(h.elements.scansAfter.value, "");
    assert.equal(h.state.counters.recordId, "R-1");
    assert.equal(h.state.counters.loaded, true);
    assert.equal(h.state.counters.loading, false);
    assert.match(h.elements.copiesBeforeDate.textContent, /10\/09\/2026 08:00/);
});

test("an equipment with no previous counter remains blank rather than showing zero", async () => {
    const h = counterHarness(async () => ({ equipmentId: "E-1", copiesCounter: null, scansCounter: null }));
    await h.load();
    assert.equal(h.state.counters.loaded, true);
    assert.equal(h.elements.copiesBefore.value, "");
    assert.equal(h.elements.scansBefore.value, "");
    assert.equal(h.elements.copiesBeforeDate.textContent, "Sin registro previo");
    assert.match(h.elements.counterFeedback.textContent, /no tiene contadores anteriores/);
});

test("counter errors expose retry and a successful retry recovers without changing current readings", async () => {
    let calls = 0;
    const h = counterHarness(async () => {
        if (++calls === 1) throw new Error("Sin conexión");
        return { equipmentId: "E-1", copiesCounter: 10, scansCounter: 20 };
    });
    h.elements.copiesAfter.value = "15";
    h.elements.scansAfter.value = "25";
    await h.load();
    assert.equal(h.state.counters.loaded, false);
    assert.equal(h.elements.retryCounters.hidden, false);
    assert.equal(h.elements.retryCounters.disabled, false);
    await h.load();
    assert.equal(h.state.counters.loaded, true);
    assert.equal(h.elements.retryCounters.hidden, true);
    assert.equal(h.elements.copiesAfter.value, "15");
    assert.equal(h.elements.scansAfter.value, "25");
});

test("a mismatched equipment counter fails closed instead of prefilling another equipment", async () => {
    const h = counterHarness(async () => ({ equipmentId: "E-OTHER", copiesCounter: 100, scansCounter: 50 }));
    await h.load();
    assert.equal(h.state.counters.loaded, false);
    assert.equal(h.elements.copiesBefore.value, "");
    assert.match(h.state.counters.error, /no corresponde/);
});

test("a late counter response cannot overwrite the new equipment selection", async () => {
    let resolve;
    const h = counterHarness(() => new Promise(done => { resolve = done; }));
    const load = h.load();
    h.state.counters.scope = "c-2|e-2";
    h.state.counters.requestId++;
    h.state.counters.loading = true;
    h.elements.copiesBefore.value = "900";
    resolve({ equipmentId: "E-1", copiesCounter: 10, scansCounter: 20 });
    await load;
    assert.equal(h.elements.copiesBefore.value, "900");
    assert.equal(h.state.counters.loading, true, "the obsolete finally must not cancel the new loading state");
});

test("changing equipment clears all old readings and reselecting the same equipment preserves entered readings", () => {
    const h = counterHarness();
    const calls = [];
    const sync = bind("syncCounterSelection", {
        state: h.state, elements: h.elements, loadCounterLatest() { calls.push("load"); },
        invalidateSignatureForChange() { calls.push("invalidate"); }, setCatalogFeedback() {}
    });
    for (const field of [h.elements.copiesBefore, h.elements.copiesAfter, h.elements.scansBefore, h.elements.scansAfter]) field.value = "50";
    sync();
    assert.equal(h.elements.copiesAfter.value, "50");
    assert.deepEqual(calls, []);
    h.state.catalog.selectedEquipment = { id: "E-2", clientId: "C-1" };
    sync();
    assert.deepEqual(calls, ["invalidate", "load"]);
    for (const field of [h.elements.copiesBefore, h.elements.copiesAfter, h.elements.scansBefore, h.elements.scansAfter]) assert.equal(field.value, "");
    h.state.catalog.selectedEquipment = null;
    sync();
    assert.equal(h.state.counters.scope, "");
    assert.equal(h.state.counters.loaded, false);
});

test("counter validation blocks pending and failed history but accepts absent history and equal real zero", () => {
    const elements = { copiesBefore: control(), copiesAfter: control("0"), scansBefore: control(), scansAfter: control("0") };
    const state = { counters: { loaded: false, loading: true } };
    const messages = [];
    const validate = bind("validateCounters", { elements, state, setStatus(element, tone, message) { messages.push(message); } });
    assert.equal(validate(), false);
    state.counters.loading = false;
    assert.equal(validate(), false);
    state.counters.loaded = true;
    assert.equal(validate(), true);
    elements.copiesBefore.value = "0";
    assert.equal(validate(), true);
    elements.copiesBefore.value = "1";
    assert.equal(validate(), false);
    assert.match(elements.copiesAfter.validityMessage, /menor/);
});

function counterRequestHarness(fetch) {
    const timers = [];
    const cleared = [];
    let aborted = false;
    const run = bind("fetchCounterLatest", {
        fetch, root: { dataset: {} }, readResponse: bind("readResponse"),
        AbortController: class { constructor() { this.signal = {}; } abort() { aborted = true; } },
        window: { setTimeout(callback) { timers.push(callback); return timers.length; }, clearTimeout(id) { cleared.push(id); } }
    });
    return { run, timers, cleared, aborted: () => aborted };
}

test("counter request is read-only, no-store, scoped to client and equipment and has a bounded timeout", async () => {
    const calls = [];
    const h = counterRequestHarness((...args) => { calls.push(args); return new Promise(() => {}); });
    const pending = h.run("C 1", "E&1");
    assert.equal(calls[0][0], "/CopiersMtoV2/CounterLatest?clientId=C%201&equipmentId=E%261");
    assert.equal(calls[0][1].method, "GET");
    assert.equal(calls[0][1].cache, "no-store");
    assert.equal(calls[0][1].credentials, "same-origin");
    h.timers[0]();
    await assert.rejects(pending, /tardó demasiado/);
    assert.equal(h.aborted(), true);
    assert.deepEqual(h.cleared, [1]);
});

test("counter request rejects redirected HTML session and leaves no dangling timeout", async () => {
    const h = counterRequestHarness(async () => ({ status: 200, redirected: true }));
    await assert.rejects(h.run("C", "E"), /sesión expiró/);
    assert.deepEqual(h.cleared, [1]);
});

function visitTimesHarness() {
    let now = "2026-09-10T16:00:00.000Z";
    class FakeDate extends Date { constructor(value) { super(arguments.length ? value : now); } }
    const elements = { serviceStartedAtLocal: control("2026-09-10T15:00:00Z"), serviceStartedAtUtc: control(), serviceEndedAtUtc: control(), signatureStartedAt: {}, signatureEndedAt: {} };
    const update = bind("updateVisitTimes", { elements, Date: FakeDate, formatLocalDateTime: bind("formatLocalDateTime") });
    return { elements, update, setNow(value) { now = value; } };
}

test("entry and exit are visible before tracing the signature and exit stays frozen while navigating", () => {
    const h = visitTimesHarness();
    h.update(true);
    assert.equal(h.elements.serviceStartedAtUtc.value, "2026-09-10T15:00:00.000Z");
    assert.equal(h.elements.serviceEndedAtUtc.value, "2026-09-10T16:00:00.000Z");
    assert.match(h.elements.signatureStartedAt.textContent, /10:00/);
    assert.match(h.elements.signatureEndedAt.textContent, /11:00/);
    h.setNow("2026-09-10T17:00:00Z");
    h.update(true);
    assert.equal(h.elements.serviceEndedAtUtc.value, "2026-09-10T16:00:00.000Z");
    assert.ok(view.indexOf('id="mtoV2SignatureEndedAt"') < view.indexOf('id="mtoV2SignatureCanvas"'));
    assert.ok(functionSource("beginSignatureStroke").indexOf("updateVisitTimes(true)") < functionSource("beginSignatureStroke").indexOf("drawSignatureDot"));
    assert.doesNotMatch(functionSource("submitForm"), /serviceEndedAtUtc\s*\.value\s*=/);
});

test("clearing or invalidating the signature resets its exit time before a new signature", () => {
    const h = visitTimesHarness();
    h.update(true);
    const state = { currentStep: 3, maxUnlockedStep: 4, signature: { strokes: [[1, 2]], activeStroke: [], activePointerId: 1 } };
    Object.assign(h.elements, { signedAtUtc: control("signed"), customerAccepted: { checked: true }, finalReviewConfirmed: { checked: true } });
    h.setNow("2026-09-10T18:00:00Z");
    const clear = bind("clearSignature", {
        state, elements: h.elements, updateVisitTimes: h.update, updateProgressAvailability() {}, redrawSignature() {}, updateSignatureState() {}
    });
    clear();
    assert.equal(h.elements.serviceEndedAtUtc.value, "2026-09-10T18:00:00.000Z");
    assert.equal(h.elements.signedAtUtc.value, "");
    assert.equal(h.elements.customerAccepted.checked, false);
    assert.equal(h.elements.finalReviewConfirmed.checked, false);
    assert.equal(state.maxUnlockedStep, 3);
    assert.equal(state.signature.strokes.length, 0);
    assert.match(functionSource("invalidateSignatureForChange"), /clearSignature\(\)/);
});

test("structured answers preserve exact historical/current counters including zero and signed ISO times", () => {
    const values = {
        mtoV2ServiceStartedAtUtc: "2026-09-10T15:00:00.000Z", mtoV2ServiceEndedAtUtc: "2026-09-10T16:00:00.000Z",
        mtoV2CopiesBefore: "", mtoV2ScansBefore: "0", mtoV2CopiesAfter: "100", mtoV2ScansAfter: "0"
    };
    const answers = bind("buildStructuredAnswers", {
        state: { catalog: {}, counters: { recordId: "R-1", dateValue: "2026-09-09" } },
        valueOf: id => values[id] || "", selectedText: () => "", formatLocalDateTime: bind("formatLocalDateTime"), buildCountersSummary: () => "Impresiones: Sin registro → 100"
    })();
    const byKey = Object.fromEntries(answers.map(answer => [answer.key, answer.value]));
    assert.equal(byKey.copies_before, undefined);
    assert.equal(byKey.scans_before, "0");
    assert.equal(byKey.copies_after, "100");
    assert.equal(byKey.scans_after, "0");
    assert.equal(byKey.counter_record_id, "R-1");
    assert.equal(byKey.counter_recorded_at, "2026-09-09");
    assert.equal(byKey.service_started_at_utc, values.mtoV2ServiceStartedAtUtc);
    assert.equal(byKey.service_ended_at_utc, values.mtoV2ServiceEndedAtUtc);
    assert.match(byKey.service_started_at, /10:00/);
    assert.match(byKey.service_ended_at, /11:00/);
});

test("one-page narrative limits are visible and counting never truncates user text", () => {
    for (const [id, maximum] of [["mtoV2WorkPerformed", 1000], ["mtoV2Recommendations", 250], ["mtoV2CustomerObservations", 250]]) {
        const field = view.match(new RegExp(`<textarea[^>]+id="${id}"[^>]*>`))[0];
        assert.match(field, new RegExp(`maxlength="${maximum}"`));
        assert.match(view, new RegExp(`data-character-count-for="${id}"`));
    }
    const field = { value: "Texto intacto\ncon saltos", maxLength: 1000 };
    const label = { dataset: { characterCountFor: "work" } };
    bind("updateNarrativeCounts", {
        root: { querySelectorAll() { return [label]; } }, document: { getElementById() { return field; } }
    })();
    assert.equal(field.value, "Texto intacto\ncon saltos");
    assert.match(label.textContent, /^24 \/ 1000 caracteres/);
    assert.doesNotMatch(functionSource("updateNarrativeCounts"), /field\.value\s*=/);
});
