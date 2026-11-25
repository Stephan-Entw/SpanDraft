#!/usr/bin/env node
// Renders hero.html once per culture into a 3840 x 2160 PNG (deviceScaleFactor 1).
//
//   node render-hero.mjs [--shots <dir>] [--out <dir>] [--lang all|de-DE|en-US]
//
// --shots  directory holding hero-project-<culture>.png (default: the penpot/ folder of the
//          newest run under artifacts/screenshots/)
// --out    output directory (default: hero/ next to the --shots folder, i.e. inside the run)
//
// The app screenshot is inserted 1:1; the script fails if it is not rendered at its natural
// size, if a placeholder stays unfilled, or if a text line wraps or leaves the canvas.
import { chromium } from 'playwright';
import { existsSync, mkdirSync, readdirSync, readFileSync, statSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';

const here = path.dirname(fileURLToPath(import.meta.url));
const repoRoot = path.resolve(here, '../../..');
const cultures = ['de-DE', 'en-US'];
const canvas = { width: 3840, height: 2160 };

function parseArgs(argv) {
  const options = { lang: 'all', shots: null, out: null };
  for (let i = 0; i < argv.length; i += 2) {
    const [name, value] = [argv[i], argv[i + 1]];
    if (value === undefined) throw new Error(`Missing value for ${name}.`);
    if (name === '--shots') options.shots = path.resolve(value);
    else if (name === '--out') options.out = path.resolve(value);
    else if (name === '--lang') options.lang = value;
    else throw new Error(`Unknown argument ${name}.`);
  }
  if (options.lang !== 'all' && !cultures.includes(options.lang)) throw new Error(`Unknown language ${options.lang}.`);
  return options;
}

function newestPenpotDirectory() {
  const root = path.join(repoRoot, 'artifacts/screenshots');
  const runs = existsSync(root)
    ? readdirSync(root, { withFileTypes: true })
        // Run names are <version>_<UTC timestamp>; order by timestamp, not by version text.
        .filter((e) => e.isDirectory() && /^\d+\.\d+\.\d+_/.test(e.name) && existsSync(path.join(root, e.name, 'penpot')))
        .map((e) => e.name)
        .sort((a, b) => a.slice(a.indexOf('_')).localeCompare(b.slice(b.indexOf('_'))))
    : [];
  if (runs.length === 0) throw new Error(`No capture run with a penpot/ folder under ${root}. Pass --shots.`);
  return path.join(root, runs[runs.length - 1], 'penpot');
}

async function render(browser, culture, shotsDirectory, outDirectory) {
  const shotPath = path.join(shotsDirectory, `hero-project-${culture}.png`);
  if (!existsSync(shotPath)) throw new Error(`Missing screenshot ${shotPath}.`);
  const texts = JSON.parse(readFileSync(path.join(here, 'texts', `${culture}.json`), 'utf8'));

  const context = await browser.newContext({ viewport: canvas, deviceScaleFactor: 1 });
  const page = await context.newPage();
  try {
    await page.goto(pathToFileURL(path.join(here, 'hero.html')).href);

    // Fill {{placeholders}} through textContent, so the texts are never interpreted as markup.
    const problems = await page.evaluate((values) => {
      const used = new Set();
      const walker = document.createTreeWalker(document.body, NodeFilter.SHOW_TEXT);
      for (let node = walker.nextNode(); node; node = walker.nextNode()) {
        node.textContent = node.textContent.replace(/\{\{(\w+)\}\}/g, (match, key) => {
          if (!(key in values)) return match;
          used.add(key);
          return values[key];
        });
      }
      const missing = (document.body.textContent.match(/\{\{\w+\}\}/g) ?? []).map((m) => `unfilled ${m}`);
      const unused = Object.keys(values).filter((k) => !used.has(k)).map((k) => `unused text key ${k}`);
      return [...missing, ...unused];
    }, texts);
    if (problems.length) throw new Error(`${culture}: ${problems.join(', ')}`);

    await page.evaluate(async (src) => {
      const img = document.getElementById('shot');
      img.src = src;
      await img.decode();
    }, pathToFileURL(shotPath).href);
    await page.evaluate(() =>
      Promise.all(['600 180px Oxanium', '400 86px "Golos Text"', '500 62px "Golos Text"'].map((f) => document.fonts.load(f))).then(() => document.fonts.ready));

    const report = await page.evaluate(() => {
      const issues = [];
      for (const face of ['600 180px Oxanium', '400 86px "Golos Text"', '500 62px "Golos Text"'])
        if (!document.fonts.check(face)) issues.push(`font not loaded: ${face}`);
      // Fonts are only loaded when the file is really used, not when a fallback matched.
      const loaded = [...document.fonts].filter((f) => f.status === 'loaded').length;
      if (loaded < 3) issues.push(`expected 3 loaded font faces, got ${loaded}`);

      const img = document.getElementById('shot');
      const box = img.getBoundingClientRect();
      if (box.width !== img.naturalWidth || box.height !== img.naturalHeight || devicePixelRatio !== 1)
        issues.push(`screenshot scaled: shown ${box.width}x${box.height}, natural ${img.naturalWidth}x${img.naturalHeight}, dpr ${devicePixelRatio}`);
      if (box.left % 1 || box.top % 1) issues.push(`screenshot not on the pixel grid: ${box.left},${box.top}`);

      // Every text line must stay on a single line (1.2 line height) and inside the canvas.
      const lines = [...document.querySelectorAll('.brand, .tagline > div, .features > p')];
      for (const el of lines) {
        const r = el.getBoundingClientRect();
        const lineHeight = parseFloat(getComputedStyle(el).fontSize) * 1.2;
        if (Math.abs(r.height - lineHeight) > 0.5) issues.push(`wrapped line: "${el.textContent}" (${r.height}px)`);
        const range = document.createRange();
        range.selectNodeContents(el);
        const w = range.getBoundingClientRect().right;
        if (w > innerWidth || w > img.getBoundingClientRect().left && r.top < img.getBoundingClientRect().bottom && r.bottom > img.getBoundingClientRect().top)
          issues.push(`text reaches the screenshot or the canvas edge: "${el.textContent}" (right edge ${w})`);
      }
      const scroll = { w: document.documentElement.scrollWidth, h: document.documentElement.scrollHeight };
      if (scroll.w > innerWidth || scroll.h > innerHeight) issues.push(`page overflows: ${scroll.w}x${scroll.h}`);
      return { issues, shot: { x: box.left, y: box.top, w: box.width, h: box.height } };
    });
    if (report.issues.length) throw new Error(`${culture}: ${report.issues.join('; ')}`);

    mkdirSync(outDirectory, { recursive: true });
    const outPath = path.join(outDirectory, `hero-${culture}.png`);
    await page.screenshot({ path: outPath, type: 'png', clip: { x: 0, y: 0, ...canvas }, animations: 'disabled' });
    console.log(`${culture}: ${outPath} (screenshot ${report.shot.w}x${report.shot.h} at ${report.shot.x},${report.shot.y})`);
  } finally {
    await context.close();
  }
}

const options = parseArgs(process.argv.slice(2));
const shotsDirectory = options.shots ?? newestPenpotDirectory();
const outDirectory = options.out ?? path.join(shotsDirectory, '..', 'hero');
if (!existsSync(shotsDirectory) || !statSync(shotsDirectory).isDirectory()) throw new Error(`Not a directory: ${shotsDirectory}`);
const browser = await chromium.launch();
try {
  for (const culture of options.lang === 'all' ? cultures : [options.lang]) await render(browser, culture, shotsDirectory, outDirectory);
} finally {
  await browser.close();
}
