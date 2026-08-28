import { pathToFileURL } from "node:url";
import path from "node:path";
import process from "node:process";

const root = process.cwd();
const assetsDir = path.resolve(root, "docs", "portafolio-apps-assets");
const puppeteerEntry = "C:/Users/SebastianRuiz/Documents/Codex/2026-05-20/necesito-hacer-una-auditoria-de-un/node_modules/puppeteer-core/lib/puppeteer/puppeteer-core.js";
const chromePath = "C:/Program Files/Google/Chrome/Application/chrome.exe";

const { default: puppeteer } = await import(pathToFileURL(puppeteerEntry).href);

const shots = [
  "polla",
  "sda",
  "cotizador",
  "evaluacion",
  "induccion",
  "mesa",
  "portal",
];

const browser = await puppeteer.launch({
  executablePath: chromePath,
  headless: true,
  args: ["--no-sandbox", "--disable-setuid-sandbox"],
});

try {
  const page = await browser.newPage();
  await page.setViewport({ width: 1400, height: 920, deviceScaleFactor: 1 });
  await page.goto(pathToFileURL(path.join(assetsDir, "screenshots-source.html")).href, {
    waitUntil: "networkidle0",
  });

  for (const shot of shots) {
    const element = await page.$(`[data-shot="${shot}"]`);
    if (!element) {
      throw new Error(`No se encontro la captura ${shot}`);
    }
    await element.screenshot({
      path: path.join(assetsDir, `screen-${shot}.png`),
    });
  }
} finally {
  await browser.close();
}
