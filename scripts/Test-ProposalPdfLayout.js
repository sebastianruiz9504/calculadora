"use strict";

const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");

const pdfPath = path.join(__dirname, "..", "wwwroot", "js", "proposal-pdf-v17.js");
const source = fs.readFileSync(pdfPath, "utf8");
const loadPdfRuntime = new Function(
    "window",
    source + "\nreturn { DTPDF: DTPDF, buildProposalPDF: buildProposalPDF };"
);
const { DTPDF, buildProposalPDF } = loadPdfRuntime({});

const wrapper = new DTPDF({});
const longToken = "X".repeat(500);
const wrappedToken = wrapper._wrap(longToken, 11, false, 120);
assert.ok(wrappedToken.length > 1, "El token largo debe dividirse en varias líneas.");
wrappedToken.forEach(line =>
    assert.ok(wrapper._tw(line, 11, false) <= 120.01, "Cada fragmento debe respetar el ancho disponible."));

const baseProposal = {
    anio: "2026",
    consecutivo: "DT-2026-STRESS",
    cliente: "Cliente",
    comercial: "Digital Tech",
    moneda: "COP",
    resumen: "Resumen",
    notas: "Notas",
    servicios: [],
    proposals: [{
        title: "Alternativa",
        items: [],
        monthlySubtotal: "$0",
        monthlyIva: "$0",
        monthlyTotal: "$0",
        contractSubtotal: "$0",
        contractIva: "$0",
        contractTotal: "$0"
    }],
    cross: []
};
const assetSpecs = {
    header: ["header.jpg", 1400, 266],
    footer: ["footer.jpg", 1400, 266],
    coverLogo: ["coverLogo.jpg", 1891, 399],
    marca_microsoft: ["marca_microsoft.jpg", 520, 125],
    marca_acronis: ["marca_acronis.jpg", 520, 121],
    marca_kyocera: ["marca_kyocera.jpg", 520, 125],
    cert_partner_sec: ["cert_partner_sec.jpg", 520, 217],
    cert_partner_mw: ["cert_partner_mw.jpg", 520, 214],
    cert_mct2: ["cert_mct2.jpg", 520, 519],
    badge_cyber: ["badge_cyber.jpg", 520, 536],
    badge_ea: ["badge_ea.jpg", 520, 535],
    badge_azure: ["badge_azure.jpg", 520, 535],
    cli_aguas: ["cli_aguas.jpg", 520, 221],
    cli_dimpor: ["cli_dimpor.jpg", 520, 145],
    cli_aero: ["cli_aero.jpg", 520, 195],
    cli_inssa: ["cli_inssa.jpg", 520, 317],
    cli_pepe: ["cli_pepe.jpg", 520, 239],
    cli_carco: ["cli_carco.jpg", 520, 193]
};
const assetDirectory = path.join(__dirname, "..", "wwwroot", "img", "proposals", "v17");
const images = Object.fromEntries(Object.entries(assetSpecs).map(([name, spec]) => [
    name,
    {
        data: fs.readFileSync(path.join(assetDirectory, spec[0])).toString("latin1"),
        w: spec[1],
        h: spec[2]
    }
]));

const emptyPdf = Buffer.from(buildProposalPDF({ ...baseProposal, valoresAgregados: [] }, images)).toString("latin1");
assert.ok(
    emptyPdf.includes("No se agregaron valores adicionales a esta propuesta."),
    "El PDF vacío no debe inventar beneficios."
);

const operations = [];
const originalText = DTPDF.prototype._textAbs;
DTPDF.prototype._textAbs = function (x, y, text, size, bold, color, letterSpacing) {
    operations.push({
        page: this.pages.length,
        x,
        y,
        text: String(text),
        width: this._tw(String(text), size, bold, letterSpacing)
    });
    return originalText.call(this, x, y, text, size, bold, color, letterSpacing);
};

const token = length => "X".repeat(length);
const stressProposal = {
    ...baseProposal,
    cliente: token(180),
    contacto: token(160),
    nit: token(40),
    comercial: token(180),
    comercial_mail: token(160),
    comercial_tel: token(80),
    vigencia: token(80),
    formaPago: token(120),
    tiempoEntrega: token(120),
    tiempoContrato: token(120),
    notas: token(1200),
    valoresAgregados: Array.from({ length: 24 }, (_, index) => ({
        front: `Frente ${index + 1}`,
        name: token(160),
        detail: token(600),
        modalidad: "Incluido"
    }))
};
const stressPdf = buildProposalPDF(stressProposal, images);
assert.ok(stressPdf.length > 0, "La propuesta extrema debe generar bytes PDF.");
if (process.argv[2]) {
    const outputPath = path.resolve(process.argv[2]);
    fs.mkdirSync(path.dirname(outputPath), { recursive: true });
    fs.writeFileSync(outputPath, Buffer.from(stressPdf));
}

const pageWidth = 595.28;
const pageMargin = 56;
operations.forEach(operation => {
    assert.ok(operation.x >= pageMargin - 0.01, `Texto fuera del margen izquierdo en página ${operation.page}.`);
    assert.ok(
        operation.x + operation.width <= pageWidth - pageMargin + 0.01,
        `Texto fuera del margen derecho en página ${operation.page}.`
    );
});

const pageCount = Math.max(...operations.map(operation => operation.page));
const footerLimit = pageWidth * 266 / 1400 + 18;
operations
    .filter(operation => operation.page > 1 && operation.page < pageCount)
    .forEach(operation =>
        assert.ok(operation.y >= footerLimit, `Texto sobre el footer en página ${operation.page}.`));

assert.ok(pageCount >= 15, "La carga extrema debe repartirse en múltiples páginas.");
console.log(`Proposal PDF layout stress: OK (${pageCount} páginas).`);
