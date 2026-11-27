const assert = require("node:assert/strict");
const { test } = require("node:test");
const upgrades = require("../wwwroot/js/calculator-upgrades.js");
const line = () => ({ productId: "standard", productDescription: "Standard 1 Year Monthly", costUnit: 12.1234, suggestedRetailPrice: 15, marginPercent: 20, quantity: 10, acelerador: 4 });
const base = { id: "basic", description: "Basic 1 Year Monthly", purchasePrice: 6, suggestedRetailPrice: 7 };

test("apply, edit base, JSON round trip and uncheck retain original catalog values", () => {
    const item = line();
    upgrades.apply(item, base);
    assert.equal(item.costUnit, 6.1234);
    assert.equal(item.suggestedRetailPrice, 8);
    upgrades.apply(item, { ...base, purchasePrice: 5 });
    assert.equal(item.costUnit, 7.1234); // Never subtract twice.
    const reopened = JSON.parse(JSON.stringify(item));
    assert.equal(reopened.upgrade.baseCostUnit, 5);
    assert.match(upgrades.description(reopened), /Upgrade: Basic.*Standard/);
    upgrades.clear(reopened);
    assert.equal(reopened.costUnit, 12.1234);
    assert.equal(reopened.suggestedRetailPrice, 15);
    assert.equal(reopened.upgrade, null);
    assert.equal(reopened.marginPercent, 20);
});

test("reject same product, non-positive difference, missing price and incompatible period", () => {
    for (const item of [ { ...base, id: "standard" }, { ...base, purchasePrice: 20 },
        { ...base, purchasePrice: null }, { ...base, description: "Basic 1 Year Prepaid" } ]) {
        assert.throws(() => upgrades.apply(line(), item));
    }
});

test("search skips short queries, shares in-flight and cached results, expires and stays bounded", async () => {
    let calls = 0, time = 0;
    const search = upgrades.createProductSearch(async () => {
        calls++; return { ok: true, json: async () => [base] };
    }, { ttlMs: 100, maxEntries: 2, now: () => time });
    assert.deepEqual(await search("b"), []);
    await Promise.all([search("Basic"), search(" basic ")]);
    assert.equal(calls, 1);
    await search("BASIC");
    assert.equal(calls, 1);
    time = 101;
    await search("Basic");
    assert.equal(calls, 2);
    await search("Standard"); await search("Premium"); await search("Basic");
    assert.equal(calls, 5);
});

test("failed requests are retryable and are not cached", async () => {
    let calls = 0;
    const search = upgrades.createProductSearch(async () => {
        calls++; return { ok: calls > 1, json: async () => [base] };
    });
    await assert.rejects(search("Basic"));
    assert.deepEqual(await search("Basic"), [base]);
    assert.equal(calls, 2);
});
