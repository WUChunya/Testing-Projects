"""Edited PLANS OVERVIEW (Illustrator SVG export) → 11x17 sheets with scale bar, north arrow and level title.

Usage: python3 -I ov_pages.py <overview.svg> <outdir>
Page 0: the whole overview at 1" = 400'-0"; then one sheet per level at true scale 1" = 100'-0" (the overview is
drawn at 1" = 100', 0.72 pt/ft). Panel grid of the plugin's overview: 3 columns, panel 1152.8 x 648.3 pt drawing plus
a 9.9 pt name band, columns every 1173.6 pt, rows every 678.96 pt, 36 pt margin. Nothing in the drawing is changed;
Futura PT falls back to Liberation Sans (Arial metrics) where it is not installed.
"""
import os, re, sys

src, out = sys.argv[1], sys.argv[2]
os.makedirs(out, exist_ok=True)
t = open(src, encoding="utf-8").read()
t = t.replace("'Futura PT'", "'Futura PT', 'Liberation Sans', Arial, sans-serif")
root = re.search(r"<svg\b[^>]*>", t)
inner = t[root.end():t.rindex("</svg>")]
VW, VH = 3572, 1409
PW, PH, M = 1224, 792, 36
PANEL_W, PANEL_H, BAND = 1152.8, 648.3, 9.9
DX, DY = 1173.6, 678.96
LEVELS = [("L1", "LEVEL 1"), ("L1M", "LEVEL 1 MEZZANINE (PARKING, +10')"), ("L2", "LEVEL 2 (ENTRANCE, +20')"),
          ("L3", "LEVEL 3"), ("L4", "LEVEL 4")]
FONT = "'Futura PT', 'Liberation Sans', Arial, sans-serif"

def text(x, y, s, size, anchor="start", weight="normal", color="#333333"):
    return (f'<text x="{x:.2f}" y="{y:.2f}" font-family="{FONT}" font-size="{size}" font-weight="{weight}" '
            f'text-anchor="{anchor}" fill="{color}" letter-spacing="{size * 0.08:.2f}">{s}</text>')

def scale_bar(x, y, pt_per_ft, ticks, label):
    """Alternating black / white bar, tick values in feet, scale text above."""
    h, parts = 4.0, [text(x, y - 8, label, 7)]
    for i in range(len(ticks) - 1):
        a, b = ticks[i] * pt_per_ft, ticks[i + 1] * pt_per_ft
        fill = "#000000" if i % 2 == 0 else "#FFFFFF"
        parts.append(f'<rect x="{x + a:.2f}" y="{y:.2f}" width="{b - a:.2f}" height="{h}" fill="{fill}" stroke="#000000" stroke-width="0.5"/>')
    for v in ticks:
        parts.append(text(x + v * pt_per_ft, y + h + 8, f"{v}", 6, "middle"))
    parts.append(text(x + ticks[-1] * pt_per_ft + 12, y + h + 8, "FT", 6))
    return "".join(parts)

def north(x, y, s=14):
    return (f'<path d="M{x:.2f},{y:.2f} L{x - s * 0.4:.2f},{y + s:.2f} L{x:.2f},{y + s * 0.75:.2f} L{x + s * 0.4:.2f},{y + s:.2f} Z" fill="#000000"/>'
            + text(x, y - 3, "N", 8, "middle", "bold", "#000000"))

def page(name, crop, w, h, title, bar):
    x0, y0 = (PW - w) / 2, M + max(0.0, (PH - 2 * M - 70 - h) / 2)
    svg = (f'<svg xmlns="http://www.w3.org/2000/svg" xmlns:xlink="http://www.w3.org/1999/xlink" width="{PW}" height="{PH}" viewBox="0 0 {PW} {PH}">'
           f'<rect width="{PW}" height="{PH}" fill="#FFFFFF"/>'
           f'<svg x="{x0:.2f}" y="{y0:.2f}" width="{w:.2f}" height="{h:.2f}" viewBox="{crop[0]:.2f} {crop[1]:.2f} {crop[2]:.2f} {crop[3]:.2f}" overflow="hidden">{inner}</svg>'
           + bar + north(PW - M - 10, PH - M - 46)
           + text(PW - M, PH - M - 4, title, 16, "end", "bold", "#333333") + "</svg>")
    open(os.path.join(out, name + ".svg"), "w", encoding="utf-8").write(svg)

# Overview at 1" = 400' (a quarter of the drawing scale).
k = 0.25
page("00_PLANS_OVERVIEW", (0, 0, VW, VH), VW * k, VH * k, "PLANS OVERVIEW",
     scale_bar(M, PH - M - 20, 0.72 * k, [0, 200, 400, 800], 'SCALE 1" = 400\'-0"'))
for n, (key, title) in enumerate(LEVELS):
    col, row = n % 3, n // 3
    cx, cy = M + col * DX, M + row * DY
    crop = (cx, cy, PANEL_W, PANEL_H + BAND)
    page(f"{n + 1:02d}_PLAN_{key}", crop, PANEL_W, PANEL_H + BAND, title,
         scale_bar(M, PH - M - 20, 0.72, [0, 50, 100, 200], 'SCALE 1" = 100\'-0"'))
print("pages:", len(LEVELS) + 1)
