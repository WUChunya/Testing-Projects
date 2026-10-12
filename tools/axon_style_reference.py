"""Render the exploded-axon style reference sheet (SVG, true 1" = 45'-0" on ARCH D).

Usage: python3 -I tools/axon_style_reference.py styles/axon_style.json docs/reference/axon_exploded_reference.svg
All style values come from styles/axon_style.json; the sample building below is illustrative only.
"""
import json
import math
import sys

C30, S30, T30 = math.cos(math.radians(30)), math.sin(math.radians(30)), math.tan(math.radians(30))

# Affine maps of text (u right, v down) onto iso planes, SVG y-down. Same result as the AI SSR recipe.
TOP_X = (C30, S30, -C30, S30)    # top plane, reads down-right along +X
TOP_Y = (C30, -S30, C30, S30)    # top plane, reads up-right along -Y
VERT_X = (C30, S30, 0, 1)        # vertical plane y = const, reads down-right
VERT_Y = (C30, -S30, 0, 1)       # vertical plane x = const, reads up-right

PROGRAM = {  # subset of PRD 5.1 legend, used by the sample building; CORE comes from elements.core
    "LOBBY": "#EFB7D7", "CONFERENCE": "#C97CA6", "SUPPORT": "#D8D8D6", "CORE": None,
    "LAB": "#F7A238", "LAB SUPPORT": "#F5CB78", "OFFICE": "#7CBECD", "OPEN OFFICE": "#B1D7DE",
    "COLLAB": "#DFAFBF", "ASSEMBLY": "#A16C9B",
}

# (label, floor_height_ft, [(name, x0, y0, x1, y1) in ft])
PLATES = [
    ("Level 1", 18.0, [("LOBBY", 0, 0, 120, 70), ("ASSEMBLY", 120, 0, 200, 70), ("SUPPORT", 0, 70, 80, 140),
                       ("CORE", 80, 70, 120, 110), ("CONFERENCE", 120, 70, 200, 140), ("SUPPORT", 80, 110, 120, 140)]),
    ("Level 2", 15.0, [("LAB", 0, 0, 130, 80), ("LAB SUPPORT", 130, 0, 200, 80), ("LAB SUPPORT", 0, 80, 80, 140),
                       ("CORE", 80, 80, 120, 120), ("LAB", 120, 80, 200, 140), ("SUPPORT", 80, 120, 120, 140)]),
    ("Level 3", 15.0, [("OFFICE", 0, 0, 100, 140), ("CORE", 100, 50, 140, 90), ("COLLAB", 100, 0, 160, 50),
                       ("OPEN OFFICE", 140, 50, 160, 140), ("OPEN OFFICE", 100, 90, 140, 140)]),
    ("Level 4–6", 15.0, [("OFFICE", 0, 0, 160, 50), ("OPEN OFFICE", 0, 50, 100, 140), ("CORE", 100, 50, 140, 90),
                             ("COLLAB", 100, 90, 160, 140), ("OPEN OFFICE", 140, 50, 160, 90)]),
    ("Level 7", 15.0, [("COLLAB", 0, 0, 60, 140), ("CORE", 100, 50, 140, 90), ("OFFICE", 60, 0, 160, 50),
                       ("OPEN OFFICE", 60, 50, 100, 140), ("OPEN OFFICE", 100, 90, 160, 140), ("OFFICE", 140, 50, 160, 90)]),
]
ATRIUM = (0, 100, 40, 140)   # translucent vertical volume (facade style), Level 1 -> Level 4-6
FLOW_X = 14.0                # flow ribbon runs up the atrium's front face (y = 140)

WEIGHT = {"Regular": 400, "Semibold": 600, "Bold": 700}


class Sheet:
    def __init__(self, st):
        self.st, self.k = st, st["scale"]["pt_per_ft"]
        self.ts = st["text"]["page_text_scale"]
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

    def size_of(self, style):
        """Final page pt: 11x17 sizes are scaled by page_text_scale unless the style says page_scaled=false."""
        t = self.st["text"][style]
        return t["size"] * (self.ts if t.get("page_scaled", True) else 1.0)

    def text(self, x, y, s, style, anchor="middle", matrix=None, size=None, color=None, baseline="central"):
        t = self.st["text"][style]
        size = size or self.size_of(style)
        tr = (f'transform="matrix({matrix[0]:.4f} {matrix[1]:.4f} {matrix[2]:.4f} {matrix[3]:.4f} {x:.2f} {y:.2f})"'
              if matrix else f'x="{x:.2f}" y="{y:.2f}"')
        s = s.upper() if t["case"] == "upper" else s
        op = t.get("opacity", 1.0)
        self.out.append(f'<text {tr} font-family="{self.st["text"]["font"]}, Myriad, PT Sans, Segoe UI, sans-serif" '
                        f'font-size="{size:.1f}" font-weight="{WEIGHT[t["weight"]]}" '
                        f'letter-spacing="{t.get("tracking", 0) / 1000 * size:.2f}" fill="{color or t["color"]}" '
                        f'fill-opacity="{op}" text-anchor="{anchor}" dominant-baseline="{baseline}">{s}</text>')

    def box(self, x0, y0, x1, y1, z0, z1, color, shade, sides=(True, True)):
        """Top + two front faces; side darkening = black overlay at N% (mirrors the AI appearance trick)."""
        P = self.p
        left = [P(x0, y1, z1), P(x1, y1, z1), P(x1, y1, z0), P(x0, y1, z0)]
        right = [P(x1, y0, z1), P(x1, y1, z1), P(x1, y1, z0), P(x1, y0, z0)]
        top = [P(x0, y0, z1), P(x1, y0, z1), P(x1, y1, z1), P(x0, y1, z1)]
        f = self.st["faces"]
        faces = ((left, f[shade].get("left_black_pct", 0)), (right, f[shade].get("right_black_pct", 0)))
        for (face, pct), show in zip(faces, sides):
            if not show:
                continue
            self.poly(face, f["neutral"]["left" if face is left else "right"] if shade == "neutral" else color)
            if pct:
                self.poly(face, "#000000", None, f'fill-opacity="{pct / 100}"')
        self.poly(top, color or f["neutral"]["top"])
        return top

    def translucent_box(self, x0, y0, x1, y1, z0, z1, el):
        """Facade-style volume: one flat translucent fill + thin outline (as in reference 1)."""
        self.out.append(f'<g opacity="{el["opacity"]}">')
        self.box(x0, y0, x1, y1, z0, z1, el["fill"], "program_flat")
        self.out.append("</g>")
        P = self.p
        self.poly([P(x0, y1, z1), P(x0, y0, z1), P(x1, y0, z1), P(x1, y0, z0), P(x1, y1, z0), P(x0, y1, z0)], "none",
                  f'stroke="{el["stroke"]}" stroke-width="{el["stroke_pt"] * self.ts:.2f}"')
        self.line([P(x0, y1, z1), P(x1, y1, z1), P(x1, y0, z1)], f'stroke="{el["stroke"]}" stroke-width="{el["stroke_pt"] * self.ts:.2f}"')
        self.line([P(x1, y1, z1), P(x1, y1, z0)], f'stroke="{el["stroke"]}" stroke-width="{el["stroke_pt"] * self.ts:.2f}"')

    def band_ft(self, el):
        return el["band_pt"] * self.ts / self.k

    def ribbon(self, pts, el):
        """Flow / transportation band given as a closed ft polygon list [(x, y, z)]."""
        st = (f'stroke="{el["stroke"]}" stroke-width="{el["stroke_pt"] * self.ts:.2f}" stroke-linejoin="round"'
              if el.get("stroke") else None)
        self.poly([self.p(*q) for q in pts], el["fill"], st, f'fill-opacity="{el.get("opacity", 1.0)}"')

    def arrow(self, a, b):
        """Annotation arrow from a to b (screen pt), open head at b."""
        el = self.st["elements"]["annotation"]
        w, h = el["arrow_pt"] * self.ts, el["head_pt"] * self.ts
        attr = f'stroke="{el["arrow_color"]}" stroke-width="{w:.2f}" stroke-linecap="round" stroke-linejoin="round"'
        self.line([a, b], attr)
        ang = math.atan2(b[1] - a[1], b[0] - a[0])
        hd = [(b[0] - h * math.cos(ang + s), b[1] - h * math.sin(ang + s)) for s in (0.5, -0.5)]
        self.line([hd[0], b, hd[1]], attr)


def label(sh, name, x0, y0, x1, y1, z):
    t, k, ts = sh.st["text"], sh.k, sh.ts
    along_x = (x1 - x0) >= (y1 - y0)
    length = (x1 - x0 if along_x else y1 - y0) * k
    width = (y1 - y0 if along_x else x1 - x0) * k
    style = "label_core" if name == "CORE" else "label_primary"
    fit = min(t["label_fit_ratio"] * length / (len(name) * 0.6), 0.45 * width)
    size = min(sh.size_of(style), fit)
    if style != "label_core" and size < sh.size_of("label_primary"):
        style, size = "label_secondary", min(sh.size_of("label_secondary"), fit)
    if size < t["label_min_size"] * ts:
        return
    m = TOP_X if along_x else TOP_Y
    if style == "label_core":
        cx, cy = sh.p((x0 + x1) / 2, (y0 + y1) / 2, z)
        sh.text(cx, cy, name, style, matrix=m, size=size)
        return
    # Front corner (x1, y1) is the part an upper plate never hides. Down-right text (+X) ends there,
    # up-right text (-Y) starts there; baseline sits label_inset_pt inside the parallel front edge.
    d = t["label_inset_pt"] / k
    ax, ay = sh.p(x1 - d, y1 - d, z)
    sh.text(ax, ay, name, style, anchor="end" if along_x else "start", matrix=m, size=size, baseline="alphabetic")


def plate_z(rooms, prev_top, fh, ex):
    depth = max(r[3] for r in rooms) + max(r[4] for r in rooms)
    return prev_top + max(ex["gap_factor"] * fh, ex["min_top_visible"] * depth / 2)


def draw(st):
    W, H = st["artboard"]["width_in"] * 72, st["artboard"]["height_in"] * 72
    sh = Sheet(st)
    sh.ox, sh.oy = 0.33 * W, H - 2.5 * 72 - 380
    ex, cx, el = st["exploded"], st["context"], st["elements"]
    th = ex["slab_thickness_ft"]
    sh.out.append(f'<rect width="{W}" height="{H}" fill="#FFFFFF"/>')

    # --- SITE / CONTEXT (line mode) + TRANSPORTATION ---
    g = [(-60, -60), (260, -60), (260, 200), (-60, 200)]
    sh.poly([sh.p(x, y, -th) for x, y in g], cx["ground"]["fill"], "context")
    sh.poly([sh.p(x, y, -th) for x, y in [(210, -50), (250, -50), (250, 150), (210, 150)]], cx["landscape"]["fill"], None)
    for bx in [(-55, -55, -15, 60, 40), (-55, 70, -15, 150, 25), (10, -55, 120, -15, 55)]:
        top = sh.box(bx[0], bx[1], bx[2], bx[3], -th, bx[4], None, "neutral")
        sh.poly([sh.p(bx[0], bx[3], -th), sh.p(bx[2], bx[3], -th), sh.p(bx[2], bx[1], -th), top[1], top[0], top[3]], "none", "context")
        sh.line([top[3], top[2], top[1]], "context")
        sh.line([top[2], sh.p(bx[2], bx[3], -th)], "context")
    tr, b = el["transportation"], sh.band_ft(el["transportation"])
    ry = 175.0
    sh.ribbon([(-140, ry, -th), (330, ry, -th), (330, ry + b, -th), (-140, ry + b, -th)], tr)
    sh.ribbon([(265, -140, -th), (265 + b, -140, -th), (265 + b, ry, -th), (265, ry, -th)], tr)
    sx, sy = sh.p(60, ry + b + 8, -th)
    sh.text(sx, sy, "Main St.", "street", matrix=TOP_X)
    sx, sy = sh.p(265 + b + 8, -60, -th)
    sh.text(sx, sy, "Second Ave.", "street", matrix=TOP_Y)
    fl, fb = el["flow"], sh.band_ft(el["flow"])
    ay1 = ATRIUM[3]
    sh.ribbon([(FLOW_X, ay1, -th), (FLOW_X + fb, ay1, -th), (FLOW_X + fb, ry, -th), (FLOW_X, ry, -th)], fl)

    # --- ANNOTATION: district arrow + text on a vertical plane ---
    a, bpt = sh.p(-90, 120, 0), sh.p(-190, 120, 0)
    sh.arrow(a, bpt)
    tx, ty = sh.p(-140, 120, 30)
    lead = st["text"]["annotation"]["leading"] * sh.ts
    sh.text(tx, ty - lead, "District", "annotation", matrix=VERT_X)
    sh.text(tx, ty, "Name", "annotation", matrix=VERT_X)

    # --- PLATES, bottom to top (upper plate occludes the back of the lower one) ---
    tags, prev_top = [], None
    for i, (name, fh, rooms) in enumerate(PLATES):
        z = 0.0 if i == 0 else plate_z(rooms, prev_top, fh, ex)
        if prev_top is not None and i <= 3:  # facade-style atrium + flow, drawn between plates
            ax0, ay0, ax1, ay1 = ATRIUM
            sh.translucent_box(ax0, ay0, ax1, ay1, prev_top, z - th, ex["connector"] | {"stroke_pt": el["facade"]["stroke_pt"]})
            sh.ribbon([(FLOW_X, ay1, prev_top), (FLOW_X + fb, ay1, prev_top), (FLOW_X + fb, ay1, z - th), (FLOW_X, ay1, z - th)], fl)
        X, Y = max(r[3] for r in rooms), max(r[4] for r in rooms)
        for r in rooms:  # only exterior side faces are visible; interior ones would be hidden anyway
            sh.box(r[1], r[2], r[3], r[4], z - th, z, PROGRAM[r[0]], ex["side_faces"], (r[4] == Y, r[3] == X))
        for r in rooms:  # program divisions on the top face
            sh.poly([sh.p(r[1], r[2], z), sh.p(r[3], r[2], z), sh.p(r[3], r[4], z), sh.p(r[1], r[4], z)], "none", "division")
        for r in rooms:
            label(sh, r[0], r[1], r[2], r[3], r[4], z)
        sh.line([sh.p(0, Y, z), sh.p(X, Y, z), sh.p(X, 0, z)], "fold_edge")
        sh.line([sh.p(X, Y, z), sh.p(X, Y, z - th)], "fold_edge")
        sh.poly([sh.p(0, 0, z), sh.p(X, 0, z), sh.p(X, 0, z - th), sh.p(X, Y, z - th), sh.p(0, Y, z - th), sh.p(0, Y, z)],
                "none", "silhouette_slab")
        tags.append((name, sh.p(X, 0, z)))
        prev_top = z

    # --- LEVEL TAGS (right column, leaders along the iso x axis) ---
    col = max(t[1][0] for t in tags) + st["level_tag"]["column_offset_in"] * 72
    for name, (px, py) in tags:
        ly, lx = level_tag(sh, (px + 3, py + 3 * T30), col)
        sh.text(lx, ly, name, "level", anchor="start")

    legend(sh, W, H)
    return W, H, sh.out


def level_tag(sh, start, col):
    lt, ts = sh.st["level_tag"], sh.ts
    ly = start[1] + (col - start[0]) * T30
    sh.line([start, (col, ly)], "leader")
    m = lt["marker_pt"] * ts
    sh.out.append(f'<rect x="{col + 2:.2f}" y="{ly - m / 2:.2f}" width="{m:.2f}" height="{m:.2f}" fill="{lt["marker_fill"]}" '
                  f'stroke="{lt["marker_stroke"]}" stroke-width="{lt["marker_stroke_pt"] * ts:.2f}"/>')
    return ly, col + 2 + m + lt["marker_text_gap_pt"] * ts


def legend(sh, W, H):
    st, ts, el, tx = sh.st, sh.ts, sh.st["elements"], sh.st["text"]
    x0, y = W - 10.0 * 72, 1.2 * 72
    row = 96

    def head(s):
        sh.text(x0, y, s, "legend", anchor="start", size=tx["legend"]["size"] * ts * 1.15, color="#000000")

    def spec(name, lines, yy):
        sh.text(x0 + 190, yy - 14, name, "legend", anchor="start", color="#000000")
        for j, s in enumerate(lines):
            sh.text(x0 + 190, yy + 6 + j * 15, s, "legend", anchor="start", size=tx["legend"]["size"] * ts * 0.72)

    def mini(yy):
        sh.ox, sh.oy = x0 + 70, yy - 20

    def pt(style):
        t = tx[style]
        sz = sh.size_of(style)
        return f'{tx["font"].upper()} {t["weight"].upper()} {sz:.1f} PT ({sz / ts:.1f} PT @ 11×17)'

    head("LEGEND")
    PROGRAM_OFFICE = PROGRAM["OFFICE"]

    y += row  # program label
    mini(y)
    sh.box(0, 0, 70, 35, -3.75, 0, PROGRAM_OFFICE, "program")
    label(sh, "OFFICE", 0, 0, 70, 35, 0)
    spec("PROGRAM LABEL", [pt("label_primary") + ", CAPS", "#FFFFFF 70% FILL OPACITY, SSR ON TOP PLANE, ANCHORED AT FRONT CORNER",
                           f'SECONDARY {sh.size_of("label_secondary"):.1f} PT, MIN {tx["label_min_size"] * ts:.1f} PT'], y)

    y += row  # core
    mini(y)
    sh.box(0, 0, 70, 35, -3.75, 0, PROGRAM_OFFICE, "program")
    sh.box(20, 5, 50, 30, -3.75, 0, el["core"]["fill"], "program", (False, False))
    label(sh, "CORE", 20, 5, 50, 30, 0)
    spec("CORE", [f'FILL {el["core"]["fill"]}, NO STROKE', pt("label_core") + f', {tx["label_core"]["color"]} 100%'], y)

    y += row  # facade
    mini(y)
    sh.box(0, 0, 70, 35, -3.75, 0, PROGRAM_OFFICE, "program")
    sh.translucent_box(0, 20, 20, 35, 0, 35, el["facade"])
    f = el["facade"]
    spec("FACADE / ATRIUM VOLUME", [f'FILL {f["fill"]} {int(f["opacity"] * 100)}% OPACITY, NORMAL',
                                     f'STROKE {f["stroke_pt"] * ts:.2f} PT {f["stroke"]}'], y)

    y += row  # transportation
    mini(y)
    t, b = el["transportation"], sh.band_ft(el["transportation"])
    sh.ribbon([(-10, 15, 0), (80, 15, 0), (80, 15 + b, 0), (-10, 15 + b, 0)], t)
    sx, sy = sh.p(35, 15 + b + 6, 0)
    sh.text(sx, sy, "Main St.", "street", matrix=TOP_X)
    spec("TRANSPORTATION", [f'BAND {t["fill"]} 100%, {t["band_pt"] * ts:.1f} PT WIDE, NO STROKE',
                            "STREET NAME " + pt("street") + f', {tx["street"]["color"]}'], y)

    y += row  # flow
    mini(y)
    fl, fb = el["flow"], sh.band_ft(el["flow"])
    sh.ribbon([(0, 30, 0), (60, 30, 0), (60, 30 + fb, 0), (0, 30 + fb, 0)], fl)
    sh.ribbon([(60, 30, 0), (60 + fb, 30, 0), (60 + fb, 30, 40), (60, 30, 40)], fl)
    spec("FLOW", [f'BAND {fl["fill"]} {int(fl["opacity"] * 100)}%, {fl["band_pt"] * ts:.1f} PT WIDE',
                  f'STROKE {fl["stroke_pt"] * ts:.2f} PT {fl["stroke"]}, ROUND JOINS'], y)

    y += row  # annotation
    mini(y + 12)
    a = el["annotation"]
    sh.arrow(sh.p(40, 10, 0), sh.p(-20, 10, 0))
    ax, ay = sh.p(10, 10, 14)
    sh.text(ax, ay, "District", "annotation", matrix=VERT_X)
    spec("ANNOTATION", [pt("annotation") + ", CAPS, #000000, SSR ON VERTICAL PLANE",
                        f'ARROW {a["arrow_pt"] * ts:.1f} PT {a["arrow_color"]}, OPEN HEAD {a["head_pt"] * ts:.1f} PT'], y)

    y += row  # level tag
    ly, lx = level_tag(sh, (x0 + 10, y - 20), x0 + 110)
    sh.text(lx, ly, "Level 1", "level", anchor="start")
    lt = st["level_tag"]
    spec("LEVEL TAG", [pt("level") + f', {tx["level"]["color"]}',
                       f'MARKER {lt["marker_pt"] * ts:.1f} PT {lt["marker_fill"]}, LEADER {st["lines"]["leader"]["width"]} PT {st["lines"]["leader"]["color"]}'], y)

    y += row * 0.8
    head("LINE HIERARCHY")
    for name in ["silhouette", "silhouette_slab", "fold_edge", "division_massing", "context", "hidden", "leader", "action", "cut_line"]:
        y += 22
        sh.line([(x0, y), (x0 + 90, y)], name)
        s = st["lines"][name]
        dash = f'  DASH {s["dash"][0]}/{s["dash"][1]}' if "dash" in s else ""
        sh.text(x0 + 104, y, f'{name}  {s["width"]} PT  {s["color"]}{dash}', "legend", anchor="start", size=tx["legend"]["size"] * ts * 0.72)
    y += 50
    head("FACE SHADING  (TOP / LEFT +10% K / RIGHT +20% K)")
    sh.ox, sh.oy = x0 + 70, y + 60
    for i, n in enumerate(["OFFICE", "LAB", "LOBBY", "CORE"]):
        sh.box(i * 45, -i * 45, i * 45 + 30, -i * 45 + 30, 0, 20, PROGRAM[n], "program")
    y += 130
    head("SCALE CHECK  1\" = 45'-0\"  (BAR = 45' = 72 PT)")
    sh.out.append(f'<rect x="{x0}" y="{y + 16}" width="36" height="6" fill="#000"/>'
                  f'<rect x="{x0 + 36}" y="{y + 16}" width="36" height="6" fill="none" stroke="#000" stroke-width="0.5"/>')
    for dx, s in ((0, "0"), (36, "22'-6\""), (72, "45'")):
        sh.text(x0 + dx, y + 38, s, "legend", size=tx["legend"]["size"] * ts * 0.72)
    sh.text(1.0 * 72, H - 1.0 * 72, "EXPLODED AXON — STYLE REFERENCE   ISOMETRIC 30°/30°   1\" = 45'-0\" ON ARCH D 36×24"
            "   (TEXT SIZED TO MATCH THE REFERENCE WHEN REDUCED TO 11×17)", "step_title", anchor="start")


def main(style_path, out_path):
    with open(style_path, encoding="utf-8") as f:
        st = json.load(f)
    st["faces"]["program_flat"] = {"left_black_pct": 0, "right_black_pct": 0}
    PROGRAM["CORE"] = st["elements"]["core"]["fill"]
    W, H, body = draw(st)
    svg = (f'<svg xmlns="http://www.w3.org/2000/svg" width="{W / 72}in" height="{H / 72}in" viewBox="0 0 {W} {H}">\n'
           + "\n".join(body) + "\n</svg>\n")
    with open(out_path, "w", encoding="utf-8") as f:
        f.write(svg)


if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2])
