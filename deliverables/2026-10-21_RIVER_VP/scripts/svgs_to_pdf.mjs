// Print each SVG to a one-page vector PDF at true size: 1 SVG unit = 1 pt (the drawings' scale).
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
  // Browsers draw SVG user units as CSS px (96/in); sizing the root in inches makes 1 unit = 1/72 in.
  await page.evaluate(([w, h]) => {
    const e = document.documentElement;
    e.setAttribute('width', `${w / 72}in`);
    e.setAttribute('height', `${h / 72}in`);
    e.setAttribute('viewBox', `0 0 ${w} ${h}`);
  }, [w, h]);
  await page.pdf({ path: path.join(outDir, path.basename(svg, '.svg') + '.pdf'), width: `${w / 72}in`, height: `${h / 72}in`,
    printBackground: true, pageRanges: '1', margin: { top: 0, right: 0, bottom: 0, left: 0 } });
}
await browser.close();
