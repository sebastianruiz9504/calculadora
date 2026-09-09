"use strict";

const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const vm = require("node:vm");
const { test } = require("node:test");

const source = fs.readFileSync(path.join(__dirname, "../wwwroot/js/support-cloud-surveys.js"), "utf8").replace(/\r\n/g, "\n");

// Execute the production functions; only browser I/O and unrelated rendering are replaced.
function productionFunction(name, scope = "public") {
    const scopeStart = scope === "helper" ? 0 : source.indexOf(scope === "admin"
        ? "    function initializeSurveyAdmin("
        : "    function initializeLivePublicSurvey(");
    const indent = scope === "helper" ? "    " : "        ";
    const pattern = new RegExp(`^${indent}(?:async )?function ${name}\\(`, "m");
    const match = pattern.exec(source.slice(scopeStart));
    assert.ok(match, `Production function ${scope}.${name} must exist.`);
    const start = scopeStart + match.index;
    const next = new RegExp(`^${indent}(?:async )?function `, "m").exec(source.slice(start + match[0].length));
    assert.ok(next, `End of production function ${scope}.${name} must exist.`);
    return source.slice(start, start + match[0].length + next.index);
}

async function flush() {
    for (let index = 0; index < 20; index++) await Promise.resolve();
}

function fakeClock() {
    let now = 0;
    let serial = 0;
    const jobs = new Map();
    return {
        get now() { return now; },
        setTimeout(callback, delay = 0) {
            const id = ++serial;
            jobs.set(id, { at: now + Number(delay), callback });
            return id;
        },
        clearTimeout(id) { jobs.delete(id); },
        async advance(milliseconds) {
            const target = now + milliseconds;
            for (;;) {
                const next = Array.from(jobs.entries()).filter(([, item]) => item.at <= target)
                    .sort((left, right) => left[1].at - right[1].at)[0];
                if (!next) break;
                now = next[1].at;
                jobs.delete(next[0]);
                next[1].callback();
                await flush();
            }
            now = target;
            await flush();
        }
    };
}

function network(clock, { ignoreAbort = false } = {}) {
    const requests = [];
    let active = 0;
    let maxActive = 0;
    const fetch = (url, options) => new Promise((resolve, reject) => {
        active++;
        maxActive = Math.max(active, maxActive);
        let settled = false;
        const finish = action => {
            if (settled) return;
            settled = true;
            active--;
            options.signal?.removeEventListener("abort", abort);
            action();
        };
        const abort = () => {
            if (!ignoreAbort) finish(() => reject(new DOMException("Aborted", "AbortError")));
        };
        options.signal?.addEventListener("abort", abort, { once: true });
        const request = {
            url, options, startedAt: clock.now,
            get settled() { return settled; },
            respond(value) {
                finish(() => resolve({ ok: true, headers: { get: () => "application/json" }, json: async () => value }));
            },
            respondWithBody(readBody) {
                finish(() => resolve({ ok: true, headers: { get: () => "application/json" }, json: readBody }));
            }
        };
        requests.push(request);
        if (options.signal?.aborted) abort();
    });
    return { fetch, requests, get maxActive() { return maxActive; } };
}

function liveState(overrides = {}) {
    return {
        sessionId: "session-1", phase: "registration", phaseLabel: "Registro", sequence: 1,
        participantProgress: { participantKey: "participant-1", fullName: "Prueba", answers: [] },
        ...overrides
    };
}

function questionState(sequence = 2, questionId = "question-1") {
    return liveState({
        phase: "question", phaseLabel: "Pregunta", sequence,
        currentQuestionIndex: 0, totalQuestions: 2,
        currentQuestion: { questionId, text: "Pregunta de prueba", options: [] }
    });
}

function harness(options = {}) {
    const clock = fakeClock();
    const net = network(clock, options);
    const state = {
        participantKey: "participant-1", participant: { fullName: "Prueba", email: "prueba@empresa.test" },
        pollTimer: 0, pollInFlight: false, pollEnabled: false, pollGeneration: 0, pollController: null,
        registrationBusy: false, registrationStep: 0, restoreRegisterAttempted: false,
        lastSequence: -1, lastPhase: "", visibleQuestionId: "", answeredQuestionIds: new Set(),
        answers: new Map(), score: 0, maxScore: 0, scoreMaxQuestions: 0, correctAnswers: 0,
        shownIntro: false, shownRankingSequence: -1, shownWinnersSequence: -1
    };
    const messages = [];
    const renders = [];
    const phases = [];
    const root = { dataset: { liveScreen: "waiting" } };
    const context = {
        state, root, fetch: net.fetch, AbortController, URL,
        window: {
            setTimeout: clock.setTimeout, clearTimeout: clock.clearTimeout,
            location: { origin: "https://survey.test", pathname: "/live" }
        },
        payload: { liveStateUrl: "/LiveState", liveRegisterUrl: "/LiveRegister" },
        els: { messages: { set innerHTML(value) { messages.length = 0; } } },
        registrationFields: [{}, {}, {}, {}],
        liveWheelEnabled: false,
        numberFormatter: new Intl.NumberFormat("es-CO"),
        surveyInputRating: 645250001, surveyInputText: 645250002,
        surveyInputMultipleChoice: 645250003, surveyInputMatching: 645250004,
        persistLiveSnapshot() {}, updateLiveScore() {},
        setLivePhase(...values) { phases.push(values); },
        showLiveTopbar(value) { state.topbar = value; },
        showPanel(value) { state.panel = value; },
        addBot(value) { messages.push(value); return { classList: { add() {} }, innerHTML: "" }; },
        addFeedback(ok, value) { messages.push(value); },
        buildErrorMessage: error => error.message,
        askRegistration() { state.askedToRegister = true; },
        startLiveQuestionTimer() { state.questionTimerActive = true; },
        stopLiveQuestionTimer() { state.questionTimerActive = false; },
        renderLiveSingleChoiceQuestion(bubble, question) { renders.push(question.questionId); },
        escapeHtml: value => value,
        handleRemovedParticipant() { state.removed = true; context.stopLivePolling(); },
        showLiveRanking(value) { state.ranking = value; },
        showLiveWinners() {}, showFinalThanksAfterSurvey() {}, askSatisfactionQuestion() {},
        renderLiveFinalThanks: () => "Gracias"
    };
    const functions = ["registerParticipant", "startLivePolling", "stopLivePolling", "loadLiveParticipantState",
        "handleLiveParticipantState", "showLiveQuestion", "setLiveScreen", "restoreParticipantProgress",
        "updateScoreWindowFromLiveState"];
    vm.runInNewContext(functions.map(name => productionFunction(name)).concat([
        productionFunction("fetchJson", "helper"), productionFunction("buildUrl", "helper")
    ]).join("\n"), context);
    return { clock, net, state, root, messages, renders, phases, context };
}

test("a slow participant GET stays single-flight and waits one second after completion", async () => {
    const h = harness();
    h.context.startLivePolling();
    assert.equal(h.net.requests.length, 1);
    await h.clock.advance(2500);
    await h.context.loadLiveParticipantState();
    assert.equal(h.net.requests.length, 1);
    h.net.requests[0].respond(liveState());
    await flush();
    await h.clock.advance(999);
    assert.equal(h.net.requests.length, 1);
    await h.clock.advance(1);
    assert.equal(h.net.requests.length, 2);
    assert.equal(h.net.requests[1].startedAt, 3500);
    assert.equal(h.net.requests[0].options.cache, "no-store");
    assert.equal(h.net.maxActive, 1);
    h.context.stopLivePolling();
});

test("a stalled participant GET times out, shows reconnection and recovers the question", async () => {
    const h = harness();
    h.context.startLivePolling();
    await h.clock.advance(5000);
    assert.equal(h.net.requests[0].options.signal.aborted, true);
    assert.equal(h.state.pollInFlight, false);
    assert.equal(h.phases.at(-1)[0], "Reconectando");
    await h.clock.advance(1000);
    h.net.requests[1].respond(questionState());
    await flush();
    assert.equal(h.root.dataset.liveScreen, "question");
    assert.deepEqual(h.renders, ["question-1"]);
    h.context.stopLivePolling();
});

test("a late response from a stopped generation cannot overwrite the current screen", async () => {
    const h = harness({ ignoreAbort: true });
    h.context.startLivePolling();
    h.context.stopLivePolling();
    h.context.startLivePolling();
    h.net.requests[0].respond(liveState());
    await flush();
    assert.equal(h.state.topbar, undefined);
    await h.clock.advance(1000);
    h.net.requests[1].respond(questionState(8));
    await flush();
    assert.equal(h.state.lastSequence, 8);
    h.context.stopLivePolling();
});

test("registration cancels polling, is single-flight and renders its question before another GET", async () => {
    const h = harness();
    h.state.answers.set("answered-before", { questionId: "answered-before", points: 10 });
    h.state.answeredQuestionIds.add("answered-before");
    h.context.startLivePolling();
    const registration = h.context.registerParticipant();
    await flush();
    assert.equal(h.net.requests[0].options.signal.aborted, true);
    await h.context.registerParticipant();
    await h.context.loadLiveParticipantState();
    await h.clock.advance(2000);
    assert.equal(h.net.requests.length, 2);
    h.net.requests[1].respond({ participantKey: "participant-1", state: questionState(5) });
    await registration;
    assert.equal(h.root.dataset.liveScreen, "question");
    assert.deepEqual(h.renders, ["question-1"]);
    assert.equal(h.state.questionTimerActive, true, "Restarting polling must preserve the new countdown.");
    assert.equal(h.state.answers.has("answered-before"), true);
    assert.equal(h.state.answeredQuestionIds.has("answered-before"), true);
    assert.equal(h.net.requests.length, 3, "A fresh state GET resumes only after registration resolves.");
    h.context.stopLivePolling();
});

test("automatic registration recovery cannot erase a question while its POST is pending", async () => {
    const h = harness();
    h.context.startLivePolling();
    h.net.requests[0].respond(questionState(4));
    await flush();
    assert.deepEqual(h.renders, ["question-1"]);
    await h.clock.advance(1000);
    h.net.requests[1].respond(liveState({ sequence: 5, participantProgress: null }));
    await flush();
    assert.equal(h.state.registrationBusy, true);
    assert.equal(h.net.requests[2].url, "/LiveRegister");
    await h.clock.advance(1500);
    await h.context.loadLiveParticipantState();
    assert.equal(h.net.requests.length, 3);
    h.net.requests[2].respond({ participantKey: "participant-1", state: questionState(6) });
    await flush();
    assert.equal(h.root.dataset.liveScreen, "question");
    assert.equal(h.state.visibleQuestionId, "question-1");
    assert.deepEqual(h.renders, ["question-1"], "Recovery must retain the already-visible unanswered question.");
    assert.equal(h.net.requests.length, 4);
    h.context.stopLivePolling();
});

test("registration timeout releases its guard and allows an idempotent participant retry", async () => {
    const h = harness();
    const registration = h.context.registerParticipant();
    await h.clock.advance(15000);
    await registration;
    assert.equal(h.state.registrationBusy, false);
    assert.equal(h.state.askedToRegister, true);
    assert.equal(h.state.mode, "registration");
    assert.equal(h.state.participantKey, "participant-1");
    const retry = h.context.registerParticipant();
    h.net.requests[1].respond({ participantKey: "participant-1", state: questionState() });
    await retry;
    assert.equal(h.root.dataset.liveScreen, "question");
    h.context.stopLivePolling();
});

test("recovering a participant preserves the text-answer input and its unsent draft", async () => {
    const h = harness();
    const current = questionState(4);
    current.currentQuestion.inputTypeValue = 645250002;
    const input = { value: "Borrador sin enviar", focus() {} };
    h.context.els.input = input;
    h.context.handleLiveParticipantState(current);
    assert.equal(h.state.panel, "input");
    const registration = h.context.registerParticipant();
    assert.equal(h.state.panel, "input");
    h.net.requests[0].respond({ participantKey: "participant-1", state: { ...current, sequence: 5 } });
    await registration;
    assert.equal(h.state.panel, "input");
    assert.equal(h.state.mode, "questionText");
    assert.equal(input.value, "Borrador sin enviar");
    assert.equal(h.state.questionTimerActive, true);
    h.context.stopLivePolling();
});

test("timed-out recovery retains the draft and resumes after one second without asking registration again", async () => {
    const h = harness();
    const current = questionState(4);
    current.currentQuestion.inputTypeValue = 645250002;
    h.context.els.input = { value: "Respuesta en curso", focus() {} };
    h.context.handleLiveParticipantState(current);
    h.state.restoreRegisterAttempted = true;
    const recovery = h.context.registerParticipant();
    await h.clock.advance(15000);
    await recovery;
    assert.equal(h.state.registrationBusy, false);
    assert.equal(h.state.mode, "questionText");
    assert.equal(h.state.askedToRegister, undefined);
    assert.equal(h.context.els.input.value, "Respuesta en curso");
    assert.equal(h.state.restoreRegisterAttempted, false);
    await h.clock.advance(999);
    assert.equal(h.net.requests.length, 1);
    await h.clock.advance(1);
    assert.equal(h.net.requests[1].options.method, "GET");
    h.net.requests[1].respond(current);
    await flush();
    assert.equal(h.root.dataset.liveScreen, "question");
    h.context.stopLivePolling();
});

test("cleared questions may render again but answered questions do not permit duplicate input", () => {
    const h = harness();
    h.context.handleLiveParticipantState(questionState());
    h.context.setLiveScreen("waiting", true);
    h.context.handleLiveParticipantState(questionState());
    assert.deepEqual(h.renders, ["question-1", "question-1"]);
    h.state.answeredQuestionIds.add("question-1");
    h.context.setLiveScreen("waiting", true);
    h.context.handleLiveParticipantState(questionState());
    assert.equal(h.renders.length, 2);
});

test("shared sequence never regresses while same-sequence participant progress is still applied", () => {
    const h = harness();
    h.context.handleLiveParticipantState(questionState(8));
    h.context.handleLiveParticipantState(liveState({ sequence: 7 }));
    assert.equal(h.state.lastSequence, 8);
    assert.equal(h.state.topbar.phase, "question");
    h.context.handleLiveParticipantState(liveState({ sequence: 9, phase: "intro" }));
    h.context.handleLiveParticipantState(liveState({ sequence: 9, phase: "intro",
        participantProgress: { participantKey: "participant-1", score: 10, maxScore: 10, answers: [] }
    }));
    assert.equal(h.state.score, 10);
});

test("fetch timeout remains active while the JSON response body is still downloading", async () => {
    const h = harness();
    const request = h.context.fetchJson("/slow-body", { timeoutMs: 5000 });
    const pending = h.net.requests[0];
    pending.respondWithBody(() => new Promise((resolve, reject) => {
        pending.options.signal.addEventListener("abort", () => reject(new DOMException("Aborted", "AbortError")), { once: true });
    }));
    await flush();
    const outcome = request.then(() => null, error => error);
    await h.clock.advance(5000);
    assert.match((await outcome).message, /tardo demasiado/);
});

function adminHarness() {
    const h = harness();
    Object.assign(h.state, {
        liveSessionId: "session-1", liveState: null, livePollTimer: 0, livePollInFlight: false,
        livePollEnabled: false, livePollGeneration: 0, livePollController: null,
        liveStateRequestId: 0, liveStateAppliedRequestId: 0
    });
    Object.assign(h.context, {
        urls: { liveState: "/AdminLiveState" }, els: {}, setStatus() {}, setValue() {}, setText() {},
        renderLiveResponses() {}, resolveLivePresenterTitle: () => "Registro"
    });
    vm.runInNewContext(["loadLiveState", "startLivePolling", "stopLivePolling", "renderLiveState"]
        .map(name => productionFunction(name, "admin")).join("\n"), h.context);
    return h;
}

test("administrator polling also waits after slow responses and recovers from a five-second timeout", async () => {
    const h = adminHarness();
    h.context.startLivePolling();
    await h.clock.advance(2500);
    h.net.requests[0].respond(liveState());
    await flush();
    await h.clock.advance(999);
    assert.equal(h.net.requests.length, 1);
    await h.clock.advance(1);
    assert.equal(h.net.requests.length, 2);
    await h.clock.advance(5000);
    assert.equal(h.state.livePollInFlight, false);
    await h.clock.advance(1000);
    assert.equal(h.net.requests.length, 3);
    assert.equal(h.net.maxActive, 1);
    h.context.stopLivePolling();
});

test("administrator rendering rejects an older shared sequence even from a later GET", () => {
    const h = adminHarness();
    h.context.renderLiveState(liveState({ sequence: 8, phase: "intro" }));
    h.context.renderLiveState(liveState({ sequence: 7 }));
    assert.equal(h.state.liveState.sequence, 8);
    assert.equal(h.state.liveState.phase, "intro");
    h.context.renderLiveState(liveState({ sessionId: "session-2", sequence: 1 }));
    assert.equal(h.state.liveState.sessionId, "session-2");
});
