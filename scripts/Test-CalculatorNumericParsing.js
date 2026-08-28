"use strict";

const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");

const view = fs.readFileSync(
    path.join(__dirname, "..", "Views", "Calculator", "Index.cshtml"),
    "utf8");
const start = view.indexOf("    function clampDec(v, fallback) {");
const end = view.indexOf("\n    function ", start + 1);
assert.ok(start >= 0 && end > start, "No se encontró clampDec en la calculadora.");

const source = view.slice(start, end);
const clampDec = new Function(`${source}\nreturn clampDec;`)();
const cases = [
    ["1.000,00", 1000],
    ["1.234.567,89", 1234567.89],
    ["1,234,567.89", 1234567.89],
    ["20,00", 20],
    ["-12,50", -12.5],
    ["0.5", 0.5],
    ["1.000", 1000],
    ["", 77],
    ["texto", 77]
];

for (const [input, expected] of cases) {
    assert.equal(clampDec(input, 77), expected, `Valor inesperado para ${input}.`);
}

console.log(`Calculator numeric parsing: OK (${cases.length} casos).`);
