// Screenshot an SVG preview at a given pixel width (review helper; uses the global Playwright + preinstalled Chromium).
// Usage: node tools/svg_shot.mjs <in.svg> <out.png> [width=1400]
// Crop afterwards if needed: convert out.png -crop WxH+X+Y +repage crop.png
import { chromium } from '/opt/node22/lib/node_modules/playwright/index.mjs';
import fs from 'fs';
import path from 'path';

const [, , svg, png, W] = process.argv;
const src = fs.readFileSync(svg, 'utf8');
const m = src.match(/width="([\d.]+)" height="([\d.]+)"/);
const w = +W || 1400;
const h = Math.ceil(w * m[2] / m[1]);
const browser = await chromium.launch({ args: ['--disable-gpu'] });
const page = await browser.newPage({ viewport: { width: w, height: h } });
await page.goto('file://' + path.resolve(svg));
await page.evaluate((width) => { const e = document.documentElement; e.setAttribute('width', width); e.removeAttribute('height'); }, w);
await page.screenshot({ path: png, fullPage: false });
await browser.close();
