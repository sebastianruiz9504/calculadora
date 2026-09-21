"use strict";
// Test-only jsdom dependency, supplied through NODE_PATH and excluded from deployment.
const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const { test } = require("node:test");
const { JSDOM } = require("jsdom");
const root = path.join(__dirname, "..");
const source = fs.readFileSync(path.join(root, "wwwroot/js/metricas.js"), "utf8");
const view = fs.readFileSync(path.join(root, "Views/Metricas/Index.cshtml"), "utf8");
const sellers = [{ key: "ana", name: "Ana", color: "#145AF2" }, { key: "luis", name: "Luis", color: "#10B981" }];
const newClients = [
    { seller: "ana", clientName: "Cliente <especial>", contractStartDateDisplay: "01/03/2026" },
    { seller: "luis", clientName: "Cliente Luis", contractStartDateDisplay: "01/02/2026" },
    { seller: "ana", clientName: "Cliente Ana", contractStartDateDisplay: "01/01/2026" }
];

function response(url) {
    const params = new URL(url, "https://local.test").searchParams;
    const individual = params.get("view") === "individual";
    const seller = individual && sellers.find(item => item.key === params.get("seller"));
    const selected = seller ? [seller] : sellers;
    const clientsInPeriod = params.get("filter") === "previous-year" ? [] : newClients;
    const clients = clientsInPeriod.filter(client => !seller || client.seller === seller.key);
    return {
        filter: params.get("filter"), period: params.get("period"), view: individual ? "individual" : "global",
        filterLabel: params.get("filter") === "previous-year" ? "Año pasado" : "Este año",
        appliedSellerKey: seller?.key || "", appliedSellerName: seller?.name || "Todos los vendedores", sellers,
        newClientsCount: clients.length, newClients: clients, sellersCount: selected.length,
        charts: Array.from({ length: individual ? 6 : 5 }, (_, index) => ({
            key: `chart-${index}`, title: `Grafica ${index}`, categories: ["ene."],
            series: selected.map(item => ({ name: item.name, color: item.color, values: [10], annualValues: [100] })),
            goalStatuses: selected.map(item => ({ sellerName: individual ? item.name : "", sellerColor: item.color,
                category: "ene.", hasTarget: true, actualValue: 10, targetValue: 125, statusTone: "missed", statusLabel: "No cumplido",
                details: [{ clientName: `Cliente ${item.name}`, score: 10, contractValue: 100, detail: "Contrato" }] }))
        }))
    };
}

async function fixture() {
    const dom = new JSDOM(view, { runScripts: "outside-only", url: "https://local.test/Metricas", pretendToBeVisual: true });
    const app = dom.window.document.getElementById("metricasApp");
    Object.assign(app.dataset, { chartsUrl: "/Metricas/Charts", initialFilter: "this-year", initialView: "individual", initialPeriod: "month" });
    const requests = [];
    dom.window.fetch = async url => { requests.push(url); return { ok: true, headers: { get: () => "application/json" }, json: async () => response(url) }; };
    dom.window.eval(source);
    await settle();
    return { dom, document: dom.window.document, requests };
}
const settle = () => new Promise(resolve => setImmediate(resolve));

test("Individuales loads all charts without requiring a seller and replaces only its summary card", async () => {
    const { dom, document } = await fixture();
    assert.equal(document.querySelectorAll(".metrics-chart").length, 6);
    assert.equal(document.getElementById("metricsSummarySellersLabel").textContent, "Clientes nuevos");
    assert.equal(document.getElementById("metricsSummarySellers").textContent, "3");
    assert.equal(document.querySelectorAll(".metrics-chart__seller-periods[open]").length, 0);
    document.querySelector('[data-view="global"]').click();
    await settle();
    assert.equal(document.getElementById("metricsSummarySellersLabel").textContent, "Vendedores");
    assert.equal(document.querySelectorAll(".metrics-chart").length, 5);
    dom.window.close();
});

test("Seller filtering and clearing update totals and keep the same line color", async () => {
    const { dom, document, requests } = await fixture();
    const select = document.getElementById("metricsSellerFilter");
    const colorsBefore = [...document.querySelectorAll(".metrics-chart__line")].map(line => line.getAttribute("stroke"));
    select.value = "luis";
    select.dispatchEvent(new dom.window.Event("change"));
    await settle();
    assert.equal(new URL(requests.at(-1), "https://local.test").searchParams.get("seller"), "luis");
    assert.equal(document.getElementById("metricsSummarySellers").textContent, "1");
    assert.equal(document.querySelectorAll(".metrics-chart__seller-periods[open]").length, 6);
    assert.equal(document.querySelector(".metrics-chart__line").getAttribute("stroke"), colorsBefore[1]);
    select.value = "";
    select.dispatchEvent(new dom.window.Event("change"));
    await settle();
    assert.equal(new URL(requests.at(-1), "https://local.test").searchParams.has("seller"), false);
    assert.equal(document.getElementById("metricsSummarySellers").textContent, "3");
    dom.window.close();
});

test("Period detail opens the selected seller's records using its original index", async () => {
    const { dom, document } = await fixture();
    const groups = document.querySelectorAll(".metrics-chart__seller-periods");
    groups[1].open = true;
    groups[1].querySelector("button[data-metrics-detail]").click();
    assert.equal(document.getElementById("metricsDetailModal").hidden, false);
    assert.ok(document.getElementById("metricsDetailModalTitle").textContent.includes("Luis"));
    assert.ok(document.getElementById("metricsDetailModalBody").textContent.includes("Cliente Luis"));
    assert.equal(document.getElementById("metricsDetailModalBody").textContent.includes("Cliente Ana"), false);
    document.dispatchEvent(new dom.window.KeyboardEvent("keydown", { key: "Escape" }));
    assert.equal(document.getElementById("metricsDetailModal").hidden, true);
    dom.window.close();
});

test("New clients popup shows every name and date, escapes names and closes with Escape returning focus", async () => {
    const { dom, document } = await fixture();
    const button = document.getElementById("metricsNewClientsButton");
    const modal = document.getElementById("metricsNewClientsModal");
    button.click();
    assert.equal(modal.hidden, false);
    assert.equal(modal.getAttribute("aria-hidden"), "false");
    assert.equal(document.querySelectorAll("#metricsNewClientsBody tr").length, 3);
    assert.equal(document.querySelector("#metricsNewClientsBody td").textContent, "Cliente <especial>");
    assert.equal(document.querySelector("#metricsNewClientsBody especial"), null);
    assert.equal(document.querySelector("#metricsNewClientsBody td:nth-child(2)").textContent, "01/03/2026");
    assert.equal(document.getElementById("metricsNewClientsSubtitle").textContent, "Este año · Todos los vendedores");
    assert.equal(document.body.classList.contains("metrics-modal-open"), true);
    const dialog = modal.querySelector('[role="dialog"]');
    dialog.focus();
    document.dispatchEvent(new dom.window.KeyboardEvent("keydown", { key: "Tab", cancelable: true }));
    assert.equal(document.activeElement, modal.querySelector("button"));
    document.dispatchEvent(new dom.window.KeyboardEvent("keydown", { key: "Tab", shiftKey: true, cancelable: true }));
    assert.equal(document.activeElement, modal.querySelector('[tabindex="0"]'));
    document.dispatchEvent(new dom.window.KeyboardEvent("keydown", { key: "Escape" }));
    assert.equal(modal.hidden, true);
    assert.equal(document.activeElement, button);
    assert.equal(document.body.classList.contains("metrics-modal-open"), false);
    dom.window.close();
});

test("New clients popup follows seller and period changes and handles zero clients", async () => {
    const { dom, document } = await fixture();
    const button = document.getElementById("metricsNewClientsButton");
    const modal = document.getElementById("metricsNewClientsModal");
    const select = document.getElementById("metricsSellerFilter");
    button.click();
    select.value = "luis";
    select.dispatchEvent(new dom.window.Event("change"));
    assert.equal(modal.hidden, true);
    assert.equal(button.disabled, true);
    await settle();
    button.click();
    assert.equal(document.querySelectorAll("#metricsNewClientsBody tr").length, 1);
    assert.equal(document.querySelector("#metricsNewClientsBody td").textContent, "Cliente Luis");
    assert.equal(document.getElementById("metricsNewClientsSubtitle").textContent, "Este año · Luis");
    modal.querySelector(".metrics-detail-modal__backdrop").click();
    assert.equal(modal.hidden, true);
    document.querySelector('[data-filter="previous-year"]').click();
    await settle();
    button.click();
    assert.equal(document.querySelectorAll("#metricsNewClientsBody tr").length, 0);
    assert.equal(document.getElementById("metricsNewClientsEmpty").hidden, false);
    assert.equal(modal.querySelector("table").hidden, true);
    assert.equal(document.getElementById("metricsNewClientsSubtitle").textContent, "Año pasado · Luis");
    modal.querySelector("button").click();
    assert.equal(modal.hidden, true);
    document.querySelector('[data-view="global"]').click();
    await settle();
    assert.equal(button.disabled, true);
    button.click();
    assert.equal(modal.hidden, true);
    assert.equal(document.getElementById("metricsNewClientsHint").hidden, true);
    dom.window.close();
});
