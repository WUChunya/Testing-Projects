// Usage: node svgs_to_png.mjs <outDir> <maxPx> <a.svg> ...  (quick review renders)
import { chromium } from '/opt/node22/lib/node_modules/playwright/index.mjs';
import fs from 'fs'; import path from 'path';
const [, , outDir, maxPx, ...svgs] = process.argv;
const browser = await chromium.launch({ args: ['--disable-gpu'] });
const page = await browser.newPage();
for (const svg of svgs) {
  const src = fs.readFileSync(svg, 'utf8');
  const m = src.match(/viewBox="0 0 ([\d.]+) ([\d.]+)"/);
  const w = +m[1], h = +m[2], k = +maxPx / Math.max(w, h);
  await page.setViewportSize({ width: Math.ceil(w * k), height: Math.ceil(h * k) });
  await page.goto('file://' + path.resolve(svg));
  await page.evaluate(([w, h]) => { const e = document.documentElement; e.setAttribute('width', w); e.setAttribute('height', h); }, [w * k, h * k]);
  await page.screenshot({ path: path.join(outDir, path.basename(svg, '.svg') + '.png') });
}
await browser.close();
