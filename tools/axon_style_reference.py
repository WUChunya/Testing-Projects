"""Render the exploded-axon style reference sheet (SVG, true 1" = 45'-0" on ARCH D).

Usage: python3 -I tools/axon_style_reference.py styles/axon_style.json docs/reference/axon_exploded_reference.svg
All style values come from styles/axon_style.json; the sample building below is illustrative only.
"""
import json
import math
import sys

C30, S30 = math.cos(math.radians(30)), math.sin(math.radians(30))

PROGRAM = {  # subset of PRD 5.1 legend, used by the sample building
    "LOBBY": "#EFB7D7", "CONFERENCE": "#C97CA6", "SUPPORT": "#D8D8D6", "CORE": "#999999",
    "LAB": "#F7A238", "LAB SUPPORT": "#F5CB78", "OFFICE": "#7CBECD", "OPEN OFFICE": "#B1D7DE",
    "COLLAB": "#DFAFBF", "ASSEMBLY": "#A16C9B",
}

# (label, z_top_ft, floor_height_ft, [(name, x0, y0, x1, y1) in ft])
PLATES = [
    ("Level 1", 0.0, 18.0, [("LOBBY", 0, 0, 120, 70), ("ASSEMBLY", 120, 0, 200, 70),
                            ("SUPPORT", 0, 70, 80, 140), ("CORE", 80, 70, 120, 110), ("CONFERENCE", 120, 70, 200, 140),
                            ("SUPPORT", 80, 110, 120, 140)]),
    ("Level 2", 18.0, 15.0, [("LAB", 0, 0, 130, 80), ("LAB SUPPORT", 130, 0, 200, 80),
                             ("LAB SUPPORT", 0, 80, 80, 140), ("CORE", 80, 80, 120, 120), ("LAB", 120, 80, 200, 140),
                             ("SUPPORT", 80, 120, 120, 140)]),
    ("Level 3", 33.0, 15.0, [("OFFICE", 0, 0, 100, 140), ("CORE", 100, 50, 140, 90), ("COLLAB", 100, 0, 160, 50),
                             ("OPEN OFFICE", 140, 50, 160, 140), ("OPEN OFFICE", 100, 90, 140, 140)]),
    ("Level 4–6", 48.0, 15.0, [("OFFICE", 0, 0, 160, 50), ("OPEN OFFICE", 0, 50, 100, 140),
                                   ("CORE", 100, 50, 140, 90), ("COLLAB", 100, 90, 160, 140), ("OPEN OFFICE", 140, 50, 160, 90)]),
    ("Level 7", 93.0, 15.0, [("COLLAB", 0, 0, 60, 140), ("CORE", 100, 50, 140, 90), ("OFFICE", 60, 0, 160, 50),
                             ("OPEN OFFICE", 60, 50, 100, 140), ("OPEN OFFICE", 100, 90, 160, 140), ("OFFICE", 140, 50, 160, 90)]),
]
ATRIUM = (0, 100, 40, 140)  # translucent vertical connector, Level 1 -> Level 4-6


class Sheet:
    def __init__(self, st):
        self.st, self.k = st, st["scale"]["pt_per_ft"]
        self.out, self.ox, self.oy = [], 0.0, 0.0

    def p(self, x, y, z):  # iso drawing (no foreshortening), SVG y down
        return (self.ox + (x - y) * C30 * self.k, self.oy + ((x + y) * S30 - z) * self.k)

    def poly(self, pts, fill="none", stroke=None, extra=""):
        d = " ".join(f"{a:.2f},{b:.2f}" for a, b in pts)
        self.out.append(f'<polygon points="{d}" fill="{fill}" {self.stroke(stroke)} {extra}/>')

    def line(self, pts, stroke, extra=""):
        d = " ".join(f"{a:.2f},{b:.2f}" for a, b in pts)
        self.out.append(f'<polyline points="{d}" fill="none" {self.stroke(stroke)} {extra}/>')

    def stroke(self, key):
        if key is None:
            return 'stroke="none"'
        if key.startswith("stroke="):  # raw attribute string
            return key
        s = self.st["lines"][key]
        a = f'stroke="{s["color"]}" stroke-width="{s["width"]}" stroke-linecap="{s.get("cap", "butt")}" stroke-linejoin="{s.get("join", "miter")}"'
        return a + (f' stroke-dasharray="{s["dash"][0]} {s["dash"][1]}"' if "dash" in s else "")

    def text(self, x, y, s, style, color, anchor="middle", matrix=None, size=None):
        t = self.st["text"][style]
        size = size or t["size"]
        tr = f'transform="matrix({matrix[0]:.4f} {matrix[1]:.4f} {matrix[2]:.4f} {matrix[3]:.4f} {x:.2f} {y:.2f})"' if matrix else f'x="{x:.2f}" y="{y:.2f}"'
        w = 700 if t["weight"] == "Bold" else 400
        s = s.upper() if t["case"] == "upper" else s
        self.out.append(f'<text {tr} font-family="Century Gothic, URW Gothic, Futura, sans-serif" font-size="{size:.1f}" '
                        f'font-weight="{w}" letter-spacing="{t.get("tracking", 0) / 1000 * size:.2f}" fill="{color}" '
                        f'text-anchor="{anchor}" dominant-baseline="central">{s}</text>')

    def box(self, x0, y0, x1, y1, z0, z1, color, shade, sides=(True, True)):
        """Top + two front faces; side darkening = black overlay at N% (mirrors the AI appearance trick)."""
        P = self.p
        left = [P(x0, y1, z1), P(x1, y1, z1), P(x1, y1, z0), P(x0, y1, z0)]
        right = [P(x1, y0, z1), P(x1, y1, z1), P(x1, y1, z0), P(x1, y0, z0)]
        top = [P(x0, y0, z1), P(x1, y0, z1), P(x1, y1, z1), P(x0, y1, z1)]
        f = self.st["faces"]
        faces = ((left, f[shade]["left_black_pct"] if shade == "program" else 0),
                          (right, f[shade]["right_black_pct"] if shade == "program" else 0))
        for (face, pct), show in zip(faces, sides):
            if not show:
                continue
            self.poly(face, color)
            if pct:
                self.poly(face, "#000000", None, f'fill-opacity="{pct / 100}"')
        self.poly(top, color)
        return top


def luminance(hex_color):
    def ch(v):
        v /= 255
        return v / 12.92 if v <= 0.03928 else ((v + 0.055) / 1.055) ** 2.4
    r, g, b = (int(hex_color[i:i + 2], 16) for i in (1, 3, 5))
    return 0.2126 * ch(r) + 0.7152 * ch(g) + 0.0722 * ch(b)


def label(sh, name, x0, y0, x1, y1, z, color):
    t, k = sh.st["text"], sh.k
    along_x = (x1 - x0) >= (y1 - y0)
    length = (x1 - x0 if along_x else y1 - y0) * k
    width = (y1 - y0 if along_x else x1 - x0) * k
    style = "label_core" if name == "CORE" else "label_primary"
    size = t[style]["size"]
    size = min(size, t["label_fit_ratio"] * length / (len(name) * 0.72), 0.5 * width)
    if size < t["label_secondary"]["size"] and style != "label_core":
        style, size = "label_secondary", min(t["label_secondary"]["size"], size)
    if size < 5:
        return
    fg = t[style].get("color") or (t["label_dark"] if luminance(color) > t["label_dark_if_luminance_above"] else t["label_light"])
    # Affine map of the text's (u, v) onto the top plane: SSR equivalent, text stays live/editable.
    m = (C30, S30, -C30, S30) if along_x else (C30, -S30, C30, S30)
    cx, cy = sh.p((x0 + x1) / 2, (y0 + y1) / 2, z)
    sh.text(cx, cy, name, style, fg, matrix=m, size=size)


def draw(st):
    W, H = st["artboard"]["width_in"] * 72, st["artboard"]["height_in"] * 72
    sh = Sheet(st)
    sh.ox, sh.oy = 0.36 * W, H - 2.5 * 72 - 380
    ex, cx = st["exploded"], st["context"]
    th = ex["slab_thickness_ft"]
    sh.out.append(f'<rect width="{W}" height="{H}" fill="#FFFFFF"/>')

    # --- SITE / CONTEXT (line mode) ---
    g = [(-60, -60), (260, -60), (260, 200), (-60, 200)]
    sh.poly([sh.p(x, y, -th) for x, y in g], cx["ground"]["fill"], "context")
    sh.poly([sh.p(x, y, -th) for x, y in [(-60, 160), (260, 160), (260, 200), (-60, 200)]], cx["road"]["fill"], None)
    sh.poly([sh.p(x, y, -th) for x, y in [(210, -50), (250, -50), (250, 150), (210, 150)]], cx["landscape"]["fill"], None)
    for bx in [(-55, -55, -15, 60, 40), (-55, 70, -15, 150, 25), (10, -55, 120, -15, 55)]:
        top = sh.box(bx[0], bx[1], bx[2], bx[3], -th, bx[4], cx["building"]["fill"], "neutral")
        sh.poly([sh.p(bx[0], bx[3], -th), sh.p(bx[2], bx[3], -th), sh.p(bx[2], bx[1], -th)] + top[1:2] + top[:1] + top[3:4], "none", "context")
        sh.line([top[3], top[2], top[1]], "context")
        sh.line([top[2], sh.p(bx[2], bx[3], -th)], "context")

    # --- PLATES, bottom to top (upper plate occludes the back of the lower one) ---
    z_screen, tags, prev_top = [], [], None
    for i, (name, z_floor, fh, rooms) in enumerate(PLATES):
        if i == 0:
            z = 0.0
        else:
            depth = max(r[3] for r in rooms) + max(r[4] for r in rooms)
            z = prev_top + max(ex["gap_factor"] * fh, ex["min_top_visible"] * depth / 2)
        if prev_top is not None and i <= 3:  # connector segment drawn between plates
            c = ex["connector"]
            sh.out.append(f'<g opacity="{c["opacity"]}" style="mix-blend-mode:{c["blend"]}">')
            ax0, ay0, ax1, ay1 = ATRIUM
            sh.box(ax0, ay0, ax1, ay1, prev_top, z - th, c["fill"], "neutral")
            sh.out.append("</g>")
            sh.poly([sh.p(ax0, ay1, z - th), sh.p(ax1, ay1, z - th), sh.p(ax1, ay0, z - th), sh.p(ax1, ay0, prev_top),
                     sh.p(ax1, ay1, prev_top), sh.p(ax0, ay1, prev_top)], "none",
                    f'stroke="{c["stroke"]}" stroke-width="{c["stroke_width"]}"')
        X, Y = max(r[3] for r in rooms), max(r[4] for r in rooms)
        for r in rooms:  # only exterior side faces are visible; interior ones would be hidden anyway
            sh.box(r[1], r[2], r[3], r[4], z - th, z, PROGRAM[r[0]], ex["side_faces"], (r[4] == Y, r[3] == X))
        for r in rooms:  # program divisions on the top face
            sh.poly([sh.p(r[1], r[2], z), sh.p(r[3], r[2], z), sh.p(r[3], r[4], z), sh.p(r[1], r[4], z)], "none", "division")
        for r in rooms:
            label(sh, r[0], r[1], r[2], r[3], r[4], z, PROGRAM[r[0]])
        sh.line([sh.p(0, Y, z), sh.p(X, Y, z), sh.p(X, 0, z)], "fold_edge")
        sh.line([sh.p(X, Y, z), sh.p(X, Y, z - th)], "fold_edge")
        sil = [sh.p(0, 0, z), sh.p(X, 0, z), sh.p(X, 0, z - th), sh.p(X, Y, z - th), sh.p(0, Y, z - th), sh.p(0, Y, z)]
        sh.poly(sil, "none", "silhouette_slab")
        tags.append((name, sh.p(X, 0, z)))
        prev_top = z

    # --- LEVEL TAGS (right column, leaders along the iso x axis) ---
    lt = st["level_tag"]
    col = max(t[1][0] for t in tags) + lt["column_offset_in"] * 72
    for name, (px, py) in tags:
        ly = py + (col - px) * math.tan(math.radians(30))
        sh.line([(px + 3, py + 3 * math.tan(math.radians(30))), (col, ly)], "leader")
        m = lt["marker_size"]
        sh.out.append(f'<rect x="{col + 2:.2f}" y="{ly - m / 2:.2f}" width="{m}" height="{m}" fill="{lt["marker_fill"]}"/>')
        sh.text(col + 2 + m + lt["marker_text_gap"], ly, name, "level", st["text"]["level"]["color"], anchor="start")

    key(sh, W, H)
    return W, H, sh.out


def key(sh, W, H):
    st, x0, y = sh.st, W - 8.2 * 72, 2.2 * 72
    sh.text(x0, y, "LINE HIERARCHY", "step_title", "#000000", anchor="start")
    for name in ["silhouette", "silhouette_slab", "fold_edge", "division_massing", "context", "hidden", "leader", "action", "cut_line"]:
        y += 24
        sh.line([(x0, y), (x0 + 90, y)], name)
        s = st["lines"][name]
        dash = f'  dash {s["dash"][0]}/{s["dash"][1]}' if "dash" in s else ""
        sh.text(x0 + 104, y, f'{name}  {s["width"]} pt  {s["color"]}{dash}', "level", "#333333", anchor="start")
    y += 48
    sh.text(x0, y, "FACE SHADING  (TOP / LEFT +10% K / RIGHT +20% K)", "step_title", "#000000", anchor="start")
    sh.ox, sh.oy = x0 + 70, y + 60
    for i, n in enumerate(["OFFICE", "LAB", "LOBBY", "CORE"]):
        sh.box(i * 45, -i * 45, i * 45 + 30, -i * 45 + 30, 0, 20, PROGRAM[n], "program")
    y += 130
    sh.text(x0, y, "SCALE CHECK  1\" = 45'-0\"  (BAR = 45' = 72 PT)", "step_title", "#000000", anchor="start")
    sh.out.append(f'<rect x="{x0}" y="{y + 14}" width="36" height="6" fill="#000"/><rect x="{x0 + 36}" y="{y + 14}" width="36" height="6" fill="none" stroke="#000" stroke-width="0.5"/>')
    sh.text(x0, y + 34, "0", "level", "#333333")
    sh.text(x0 + 36, y + 34, "22'-6\"", "level", "#333333")
    sh.text(x0 + 72, y + 34, "45'", "level", "#333333")
    sh.text(1.0 * 72, H - 1.0 * 72, "EXPLODED AXON — STYLE REFERENCE   ISOMETRIC 30°/30°   1\" = 45'-0\" ON ARCH D 36×24",
            "step_title", "#000000", anchor="start")


def main(style_path, out_path):
    with open(style_path, encoding="utf-8") as f:
        st = json.load(f)
    W, H, body = draw(st)
    svg = (f'<svg xmlns="http://www.w3.org/2000/svg" width="{W / 72}in" height="{H / 72}in" viewBox="0 0 {W} {H}">\n'
           + "\n".join(body) + "\n</svg>\n")
    with open(out_path, "w", encoding="utf-8") as f:
        f.write(svg)


if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2])
