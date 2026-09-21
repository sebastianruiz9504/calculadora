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

function response(url) {
    const params = new URL(url, "https://local.test").searchParams;
    const individual = params.get("view") === "individual";
    const seller = individual && sellers.find(item => item.key === params.get("seller"));
    const selected = seller ? [seller] : sellers;
    return {
        filter: params.get("filter"), period: params.get("period"), view: individual ? "individual" : "global",
        appliedSellerKey: seller?.key || "", appliedSellerName: seller?.name || "Todos los vendedores", sellers,
        newClientsCount: seller ? (seller.key === "ana" ? 2 : 1) : 3, sellersCount: selected.length,
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
