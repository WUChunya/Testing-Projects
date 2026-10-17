// Print each SVG to a one-page vector PDF at its own size (points), then the caller merges them.
// Usage: node svgs_to_pdf.mjs <outDir> <a.svg> <b.svg> ...
import { chromium } from '/opt/node22/lib/node_modules/playwright/index.mjs';
import fs from 'fs';
import path from 'path';
const [, , outDir, ...svgs] = process.argv;
const browser = await chromium.launch({ args: ['--disable-gpu'] });
const page = await browser.newPage();
for (const svg of svgs) {
  const src = fs.readFileSync(svg, 'utf8');
  const m = src.match(/width="([\d.]+)" height="([\d.]+)"/);
  const w = +m[1], h = +m[2];
  await page.goto('file://' + path.resolve(svg));
  await page.pdf({ path: path.join(outDir, path.basename(svg, '.svg') + '.pdf'), width: `${w / 72}in`, height: `${h / 72}in`,
    printBackground: true, pageRanges: '1', margin: { top: 0, right: 0, bottom: 0, left: 0 } });
}
await browser.close();
