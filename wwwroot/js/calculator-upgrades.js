(function (root, factory) {
    const api = factory();
    if (typeof module === "object" && module.exports) module.exports = api;
    else root.CalculatorUpgrades = api;
})(typeof globalThis !== "undefined" ? globalThis : this, function () {
    "use strict";

    const roundCost = value => Math.round((Number(value) + Number.EPSILON) * 10000) / 10000;
    const sameId = (a, b) => String(a || "").trim().toLowerCase() === String(b || "").trim().toLowerCase();
    function billingPeriod(description) {
        const text = String(description || "").toLowerCase();
        const year = text.match(/\b(\d+)\s*(?:years?|años?)\b/);
        const term = year ? `${year[1]}y` : (/\b(annual|annually|anual|yearly)\b/.test(text) ? "1y" : "");
        const payment = text.includes("prepaid") ? "prepaid" : (/\b(monthly|mensual)\b/.test(text) ? "monthly" : "");
        return `${term}:${payment}`;
    }

    function validate(line, base) {
        if (!line?.productId || !base?.id || !String(base.description || "").trim())
            return "Selecciona una licencia base del buscador.";
        if (sameId(line.productId, base.id)) return "La licencia base debe ser distinta del producto destino.";
        const target = Number(line.upgrade?.targetCostUnit ?? line.costUnit);
        const cost = base.purchasePrice == null ? NaN : Number(base.purchasePrice);
        if (!Number.isFinite(target) || !Number.isFinite(cost) || cost < 0 || target <= cost)
            return "El costo del producto destino debe ser mayor al de la licencia base.";
        if (billingPeriod(line.productDescription) !== billingPeriod(base.description))
            return "Selecciona licencias con el mismo período y modalidad de facturación.";
        return "";
    }

    function apply(line, base) {
        const error = validate(line, base);
        if (error) throw new Error(error);
        const upgrade = {
            targetProductId: line.productId,
            targetCostUnit: Number(line.upgrade?.targetCostUnit ?? line.costUnit),
            targetSuggestedRetailPrice: Number(line.upgrade?.targetSuggestedRetailPrice ?? line.suggestedRetailPrice ?? 0),
            baseProductId: base.id,
            baseProductDescription: base.description,
            baseCostUnit: Number(base.purchasePrice),
            baseSuggestedRetailPrice: Number(base.suggestedRetailPrice || 0)
        };
        line.upgrade = upgrade;
        line.costUnit = roundCost(upgrade.targetCostUnit - upgrade.baseCostUnit);
        line.suggestedRetailPrice = Math.max(0, roundCost(upgrade.targetSuggestedRetailPrice - upgrade.baseSuggestedRetailPrice));
        return line;
    }

    function clear(line) {
        if (!line.upgrade) return line;
        line.costUnit = line.upgrade.targetCostUnit;
        line.suggestedRetailPrice = line.upgrade.targetSuggestedRetailPrice;
        line.upgrade = null;
        return line;
    }

    function description(line) {
        return line.upgrade
            ? `Upgrade: ${line.upgrade.baseProductDescription} → ${line.productDescription || line.productName || ""}`
            : (line.productDescription || line.productName || "");
    }

    // Shared by the target and base pickers. Deduplicate simultaneous requests,
    // keep a bounded short-lived cache, and never query for fewer than 2 chars.
    function createProductSearch(fetcher, { ttlMs = 120000, maxEntries = 40, now = Date.now } = {}) {
        const cache = new Map();
        return async function search(query) {
            const q = String(query || "").trim();
            if (q.length < 2) return [];
            const key = q.toLowerCase();
            const cached = cache.get(key);
            if (cached && (cached.pending || cached.expires > now())) return cached.promise;
            cache.delete(key);
            const entry = { pending: true, expires: 0, promise: null };
            entry.promise = Promise.resolve().then(async () => {
                const response = await fetcher(`/Calculator/ProductSearch?q=${encodeURIComponent(q)}`, {
                    headers: { Accept: "application/json" }
                });
                if (!response.ok) throw new Error("No se pudo consultar el catálogo. Intenta nuevamente.");
                const items = await response.json();
                if (!Array.isArray(items)) throw new Error("El catálogo devolvió una respuesta inválida.");
                entry.pending = false;
                entry.expires = now() + ttlMs;
                return items;
            }).catch(error => { if (cache.get(key) === entry) cache.delete(key); throw error; });
            cache.set(key, entry);
            while (cache.size > maxEntries) cache.delete(cache.keys().next().value);
            return entry.promise;
        };
    }

    function createDialog({ dialog, search, onChange, onClose }) {
        const input = dialog.querySelector("[data-upgrade-search]");
        const results = dialog.querySelector("[data-upgrade-results]");
        const status = dialog.querySelector("[data-upgrade-status]");
        const summary = dialog.querySelector("[data-upgrade-summary]");
        const targetLabel = dialog.querySelector("[data-upgrade-target]");
        const confirm = dialog.querySelector("[data-upgrade-confirm]");
        const currency = new Intl.NumberFormat("es-CO", { style: "currency", currency: "USD", maximumFractionDigits: 4 });
        let line = null, base = null, timer = null, requestVersion = 0, returnFocus = null;

        function resetResults() {
            results.replaceChildren();
            input.setAttribute("aria-expanded", "false");
        }
        function renderSummary() {
            summary.replaceChildren();
            const error = validate(line, base);
            confirm.disabled = Boolean(error);
            status.textContent = base ? error : "Busca la licencia que el cliente ya tiene.";
            if (!base) return;
            const target = Number(line.upgrade?.targetCostUnit ?? line.costUnit);
            const rows = [
                ["Costo destino", target],
                ["Costo licencia base", Number(base.purchasePrice)],
                ["Diferencia por licencia", roundCost(target - Number(base.purchasePrice))]
            ];
            for (const [label, value] of rows) {
                const row = document.createElement("div");
                const name = document.createElement("span");
                const amount = document.createElement("strong");
                name.textContent = label;
                amount.textContent = currency.format(value);
                row.append(name, amount);
                summary.append(row);
            }
        }
        function select(item) {
            requestVersion++;
            clearTimeout(timer);
            base = item;
            input.value = item.description;
            resetResults();
            renderSummary();
            input.focus();
        }
        input.addEventListener("input", () => {
            clearTimeout(timer);
            const version = ++requestVersion;
            const q = input.value.trim();
            base = null;
            resetResults();
            renderSummary();
            if (q.length < 2) return;
            timer = setTimeout(async () => {
                status.textContent = "Buscando licencias…";
                try {
                    const items = await search(q);
                    if (version !== requestVersion || !dialog.open) return;
                    status.textContent = items.length ? "Selecciona la licencia base." : "No se encontraron licencias.";
                    for (const item of items) {
                        const button = document.createElement("button");
                        button.type = "button";
                        button.className = "calc-upgrade-option";
                        button.setAttribute("role", "option");
                        const name = document.createElement("span");
                        name.textContent = item.description;
                        const detail = document.createElement("small");
                        detail.textContent = item.purchasePrice == null ? "Sin costo disponible" : `Costo ${currency.format(item.purchasePrice)}`;
                        button.append(name, detail);
                        button.addEventListener("click", () => select(item));
                        results.append(button);
                    }
                    input.setAttribute("aria-expanded", String(items.length > 0));
                } catch (error) {
                    if (version === requestVersion && dialog.open) status.textContent = error.message;
                }
            }, 250);
        });
        input.addEventListener("keydown", event => {
            if (event.key === "ArrowDown" && results.firstElementChild) {
                event.preventDefault(); results.firstElementChild.focus();
            }
        });
        results.addEventListener("keydown", event => {
            if (event.key === "ArrowDown") { event.preventDefault(); event.target.nextElementSibling?.focus(); }
            if (event.key === "ArrowUp") { event.preventDefault(); (event.target.previousElementSibling || input).focus(); }
        });
        confirm.addEventListener("click", () => {
            if (!line || !base) return;
            try {
                apply(line, base);
                onChange(line);
                dialog.close();
            } catch (error) { status.textContent = error.message; }
        });
        dialog.querySelectorAll("[data-upgrade-cancel]").forEach(button => button.addEventListener("click", () => dialog.close()));
        dialog.addEventListener("close", () => {
            clearTimeout(timer);
            requestVersion++;
            line = null;
            base = null;
            onClose?.();
            if (returnFocus?.isConnected) returnFocus.focus();
        });
        return {
            open(value, trigger) {
                line = value;
                returnFocus = trigger || document.activeElement;
                base = value.upgrade ? {
                    id: value.upgrade.baseProductId,
                    description: value.upgrade.baseProductDescription,
                    purchasePrice: value.upgrade.baseCostUnit,
                    suggestedRetailPrice: value.upgrade.baseSuggestedRetailPrice
                } : null;
                input.value = base?.description || "";
                targetLabel.textContent = value.productDescription;
                resetResults();
                renderSummary();
                dialog.showModal();
                input.focus();
            }
        };
    }

    return { apply, clear, validate, billingPeriod, description, createProductSearch, createDialog };
});
