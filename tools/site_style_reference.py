"""Render style reference sheets for the site program axon, site plan and exploded axon B (ARCH D, 1" = 45'-0").

Usage: python3 -I tools/site_style_reference.py styles/site_style.json docs/reference
Writes site_axon_reference.svg, site_plan_reference.svg, axon_exploded_b_reference.svg. Sample geometry is illustrative.
"""
import json
import math
import os
import sys

C30, S30 = math.cos(math.radians(30)), math.sin(math.radians(30))
# Plan (x east, y north) -> screen (y down) linear maps; z always goes straight up.
ISO_SW = (C30, -C30, -S30, -S30)   # isometric seen from the south-west: x up-right, y up-left (plugin azimuth 225)
PLAN = (1.0, 0.0, 0.0, -1.0)
PLANO = (S30, C30, C30, -S30)      # planometric 30/60 (ref 5): plan y up-right 30 deg, plan x down-right 60 deg

FONTS = {"Arial": "Arial, Helvetica, Liberation Sans, sans-serif",
         "Gotham Book": "Gotham, Gotham Book, Montserrat, Arial, sans-serif"}
WEIGHT = {"Regular": 400, "Bold": 700}


def darken(hex_color, d):
    r, g, b = (int(hex_color[i:i + 2], 16) for i in (1, 3, 5))
    return "#%02X%02X%02X" % tuple(round(c * (1 - d)) for c in (r, g, b))


class Rng:  # tiny deterministic LCG so the sheets are reproducible
    def __init__(self, seed):
        self.s = seed

    def __call__(self):
        self.s = (self.s * 1103515245 + 12345) % 2 ** 31
        return self.s / 2 ** 31


class Sheet:
    def __init__(self, A, k, ox, oy):
        self.A, self.k, self.ox, self.oy, self.out = A, k, ox, oy, []

    def p(self, x, y, z=0.0):
        a, b, c, d = self.A
        return (self.ox + (a * x + b * y) * self.k, self.oy + (c * x + d * y - z) * self.k)

    def vec(self, dx, dy):
        a, b, c, d = self.A
        return (a * dx + b * dy, c * dx + d * dy)

    # --- primitives -------------------------------------------------------------------------------
    def poly(self, pts, fill="none", stroke=None, sw=0.0, op=1.0, extra=""):
        d = " ".join(f"{x:.2f},{y:.2f}" for x, y in pts)
        st = f'stroke="{stroke}" stroke-width="{sw}" stroke-linejoin="round"' if stroke else 'stroke="none"'
        o = f' fill-opacity="{op}"' if op < 1 else ""
        self.out.append(f'<polygon points="{d}" fill="{fill}" {st}{o} {extra}/>')

    def line(self, pts, stroke, sw, dash=None, cap="butt", extra=""):
        d = " ".join(f"{x:.2f},{y:.2f}" for x, y in pts)
        da = f' stroke-dasharray="{dash[0]} {dash[1]}"' if dash else ""
        self.out.append(f'<polyline points="{d}" fill="none" stroke="{stroke}" stroke-width="{sw}" '
                        f'stroke-linecap="{cap}" stroke-linejoin="round"{da} {extra}/>')

    def circle(self, c, r, fill, stroke=None, sw=0, op=1.0):
        st = f'stroke="{stroke}" stroke-width="{sw}"' if stroke else ""
        self.out.append(f'<circle cx="{c[0]:.2f}" cy="{c[1]:.2f}" r="{r:.2f}" fill="{fill}" fill-opacity="{op}" {st}/>')

    def text(self, x, y, s, st, anchor="middle", matrix=None, size=None, color=None, op=1.0, baseline="central"):
        size = size or st["size"]
        s = s.upper() if st.get("case") == "upper" else s
        tr = (f'transform="matrix({matrix[0]:.4f} {matrix[1]:.4f} {matrix[2]:.4f} {matrix[3]:.4f} {x:.2f} {y:.2f})"'
              if matrix else f'x="{x:.2f}" y="{y:.2f}"')
        self.out.append(f'<text {tr} font-family="{FONTS.get(st.get("font", "Arial"), "Arial, sans-serif")}" '
                        f'font-size="{size:.1f}" font-weight="{WEIGHT.get(st.get("weight", "Regular"), 400)}" '
                        f'letter-spacing="{st.get("tracking", 0) / 1000 * size:.2f}" fill="{color or st["color"]}" '
                        f'fill-opacity="{op}" text-anchor="{anchor}" dominant-baseline="{baseline}">{s}</text>')

    def lines_text(self, x, y, rows, st, **kw):
        lead = st.get("leading", st["size"] * 1.2)
        y0 = y - lead * (len(rows) - 1) / 2
        for i, r in enumerate(rows):
            self.text(x, y0 + i * lead, r, st, **kw)

    # --- 3D helpers -------------------------------------------------------------------------------
    def plane_matrix(self, dx, dy):
        """Text on a horizontal plane reading along plan direction (dx, dy); SSR-equivalent, never mirrored."""
        u = self.vec(dx, dy)
        if u[0] < 0:
            u = (-u[0], -u[1])
        n = math.hypot(*u)
        w = self.vec(-dy, dx)
        for v in (w, (-w[0], -w[1])):
            if u[0] * v[1] - u[1] * v[0] > 0:
                return (u[0], u[1], v[0], v[1]) if n else None
        return None

    def rot_matrix(self, dx, dy):
        """Upright text rotated to follow a plan direction (street names, view labels)."""
        u = self.vec(dx, dy)
        if u[0] < 0:
            u = (-u[0], -u[1])
        n = math.hypot(*u)
        c, s = u[0] / n, u[1] / n
        return (c, s, -s, c)

    def prism(self, fp, z0, z1, color, dk=(0.1, 0.2), stroke=None, sw=0.0, op=1.0, floors=None, floor_st=None):
        """Extruded CCW footprint: visible sides (shaded by facing) then top; optional floor lines on sides."""
        sides = []
        for i in range(len(fp)):
            (x0, y0), (x1, y1) = fp[i], fp[(i + 1) % len(fp)]
            nx, ny = self.vec(y1 - y0, -(x1 - x0))
            if ny > 1e-9:
                q = [self.p(x0, y0, z1), self.p(x1, y1, z1), self.p(x1, y1, z0), self.p(x0, y0, z0)]
                sides.append((q[0][1] + q[1][1], q, darken(color, dk[0] if nx < 0 else dk[1]), (x0, y0, x1, y1)))
        if op < 1:
            self.out.append(f'<g opacity="{op}">')
        for _, q, c, e in sorted(sides, key=lambda s: s[0]):
            self.poly(q, c, stroke, sw)
            if floors and floor_st:
                for zf in floors:
                    if z0 < zf < z1:
                        self.line([self.p(e[0], e[1], zf), self.p(e[2], e[3], zf)], *floor_st)
        self.poly([self.p(x, y, z1) for x, y in fp], color, stroke, sw)
        if op < 1:
            self.out.append("</g>")
        return [self.p(x, y, z1) for x, y in fp]

    def outline(self, fp, z0, z1, stroke, sw):
        """Silhouette of an extruded convex footprint (thick focus / project outline)."""
        pts = [self.p(x, y, z) for x, y in fp for z in (z0, z1)]
        hull = convex_hull(pts)
        self.poly(hull, "none", stroke, sw)

    # --- symbols ----------------------------------------------------------------------------------
    def arrow_path(self, pts, color, sw, dash, head):
        self.line(pts, color, sw, dash)
        (ax, ay), (bx, by) = pts[-2], pts[-1]
        ang = math.atan2(by - ay, bx - ax)
        hd = [(bx - head * math.cos(ang + s), by - head * math.sin(ang + s)) for s in (0.6, -0.6)]
        self.line([hd[0], (bx, by), hd[1]], color, sw * 0.7 if sw > 6 else sw, cap="round")

    def icon(self, c, d, color, kind):
        r = d / 2
        self.circle(c, r, color)
        x, y, w = c[0], c[1], "#FFFFFF"
        if kind == "ped":
            self.circle((x + 0.08 * d, y - 0.27 * d), 0.08 * d, w)
            self.line([(x + 0.05 * d, y - 0.15 * d), (x - 0.03 * d, y + 0.08 * d), (x - 0.14 * d, y + 0.3 * d)], w, 0.08 * d, cap="round")
            self.line([(x - 0.03 * d, y + 0.08 * d), (x + 0.12 * d, y + 0.3 * d)], w, 0.08 * d, cap="round")
            self.line([(x - 0.16 * d, y - 0.02 * d), (x + 0.03 * d, y - 0.12 * d), (x + 0.17 * d, y)], w, 0.06 * d, cap="round")
        elif kind == "truck":
            self.poly([(x - 0.3 * d, y - 0.15 * d), (x + 0.08 * d, y - 0.15 * d), (x + 0.08 * d, y + 0.1 * d), (x - 0.3 * d, y + 0.1 * d)], w)
            self.poly([(x + 0.1 * d, y - 0.05 * d), (x + 0.25 * d, y - 0.05 * d), (x + 0.3 * d, y + 0.03 * d), (x + 0.3 * d, y + 0.1 * d), (x + 0.1 * d, y + 0.1 * d)], w)
            for wx in (-0.18, 0.18):
                self.circle((x + wx * d, y + 0.14 * d), 0.06 * d, w, color, 0.03 * d)
        elif kind == "eye":
            self.out.append(f'<ellipse cx="{x:.2f}" cy="{y:.2f}" rx="{0.3 * d:.2f}" ry="{0.17 * d:.2f}" fill="{w}"/>')
            self.circle((x, y), 0.11 * d, color)


def convex_hull(pts):
    pts = sorted(set((round(x, 3), round(y, 3)) for x, y in pts))
    if len(pts) < 3:
        return pts

    def half(seq):
        h = []
        for p in seq:
            while len(h) >= 2 and (h[-1][0] - h[-2][0]) * (p[1] - h[-2][1]) - (h[-1][1] - h[-2][1]) * (p[0] - h[-2][0]) <= 0:
                h.pop()
            h.append(p)
        return h
    lo, hi = half(pts), half(reversed(pts))
    return lo[:-1] + hi[:-1]


def rect(x0, y0, x1, y1):
    return [(x0, y0), (x1, y0), (x1, y1), (x0, y1)]


def ellipse_fp(cx, cy, rx, ry, n=28):
    return [(cx + rx * math.cos(2 * math.pi * i / n), cy + ry * math.sin(2 * math.pi * i / n)) for i in range(n)]


# ------------------------------------------------------------------------------------------------
# Sample site (ft, x east, y north). Roads 70' wide between blocks.
BLOCKS = {"site": rect(0, 0, 290, 330), "east": rect(360, 0, 450, 330), "north": rect(0, 410, 450, 550),
          "west": rect(-250, 0, -90, 550), "south": rect(0, -250, 450, -90), "sw": rect(-250, -250, -90, -90)}
SITE_FP = [(10, 10), (280, 10), (280, 320), (70, 320), (10, 260)]
CONTEXT = [  # (label lines, footprint, height, focus)
    (["FUTURE", "HOTEL"], rect(20, 430, 140, 520), 80, False),
    (["FUTURE", "HEALTH", "SERVICES"], rect(220, 430, 420, 530), 100, False),
    (["FUTURE", "PARKING", "GARAGE"], rect(370, 40, 440, 300), 60, True),
    (["FUTURE", "MIXED-USE", "BUILDING"], rect(-230, 150, -110, 380), 70, False),
    (["FUTURE", "MOB"], rect(60, -250, 300, -140), 60, False),
    (["HUMAN", "RESOURCES"], rect(-230, -230, -110, -110), 50, False),
]
STREETS = [("University Blvd NE", (-45, 80), (0, 1)), ("Lomas Blvd NE", (340, -50), (1, 0)),
           ("Tucker Ave NE", (150, 375), (1, 0)), ("Service Rd", (325, 200), (0, 1))]
LAB = rect(150, 120, 215, 300)
ACADEMIC = rect(40, 60, 110, 230)
ATRIUM = rect(110, 120, 150, 230)
LAWNS = [ellipse_fp(70, 270, 45, 30), ellipse_fp(240, 70, 35, 50), ellipse_fp(250, 250, 25, 45)]


def trees(rng, n, area):
    pts = []
    while len(pts) < n:
        x, y = area[0] + rng() * (area[2] - area[0]), area[1] + rng() * (area[3] - area[1])
        if not (40 < x < 215 and 60 < y < 300):
            pts.append((x, y, rng()))
    return pts


def ground(sh, cfg_g, block_fill, road_fill, curb=None):
    span = rect(-250, -250, 450, 550)
    sh.poly([sh.p(x, y) for x, y in span], road_fill)
    for fp in BLOCKS.values():
        if curb:
            sh.poly([sh.p(x, y) for x, y in fp], curb["curb_strip"], curb["curb_line"], curb["curb_line_pt"])
            c = curb["curb_strip_ft"]
            x0, y0 = fp[0]
            x1, y1 = fp[2]
            sh.poly([sh.p(x, y) for x, y in rect(x0 + c, y0 + c, x1 - c, y1 - c)], block_fill, curb["curb_line"], curb["curb_line_pt"])
        else:
            sh.poly([sh.p(x, y) for x, y in fp], block_fill)


# ------------------------------------------------------------------------------------------------
def site_axon(cfg, W, H):
    a = cfg["site_axon"]
    sh = Sheet(ISO_SW, cfg["scale"]["pt_per_ft"], 928, 1148)
    clip_w = W - 9.3 * 72
    sh.out.append(f'<rect width="{W}" height="{H}" fill="#FFFFFF"/>'
                  f'<clipPath id="scene"><rect width="{clip_w}" height="{H}"/></clipPath><g clip-path="url(#scene)">')
    ground(sh, a["ground"], a["ground"]["block"], a["ground"]["road"], a["ground"])
    for fp in (rect(-70, 20, -20, 300), rect(20, -70, 300, -20)):  # medians / bioswales
        sh.poly([sh.p(x, y) for x, y in fp], a["median"]["fill"], a["median"]["stroke"], a["median"]["stroke_pt"])
    # site landscape: plaza stipple, lawns with dash hatch
    sh.poly([sh.p(x, y) for x, y in SITE_FP], a["plaza"]["fill"], a["ground"]["curb_line"], 0.5)
    rng = Rng(7)
    sp = a["plaza"]["stipple_spacing_ft"]
    for i in range(int(270 / sp)):
        for j in range(int(310 / sp)):
            x, y = 10 + i * sp + rng() * sp, 10 + j * sp + rng() * sp
            if (y < 260 or x > 70 or y - x < 250) and not (35 < x < 220 and 55 < y < 305):
                q = sh.p(x, y)
                sh.line([q, (q[0], q[1] - 2.2)], a["plaza"]["stipple"], a["plaza"]["stipple_pt"])
    for fp in LAWNS:
        sh.poly([sh.p(x, y) for x, y in fp], a["lawn"]["fill"], a["bed_edge"]["stroke"], a["bed_edge"]["stroke_pt"])
        cx = sum(x for x, _ in fp) / len(fp)
        cy = sum(y for _, y in fp) / len(fp)
        hs = a["lawn"]["hatch_spacing_ft"]
        for i in range(-5, 6):
            for j in range(-6, 7):
                x, y = cx + i * hs + rng() * 3, cy + j * hs + rng() * 3
                if ((x - cx) / (max(abs(px - cx) for px, _ in fp))) ** 2 + ((y - cy) / max(abs(py - cy) for _, py in fp)) ** 2 < 0.75:
                    q = sh.p(x, y)
                    sh.line([(q[0] - 2, q[1]), (q[0] + 2, q[1])], a["lawn"]["hatch"], a["lawn"]["hatch_pt"])
    # all vertical things painted far -> near: context buildings, project, trees
    items = []
    for lab, fp, h, focus in CONTEXT:
        items.append((-sum(x + y for x, y in fp) / 4, "ctx", (lab, fp, h, focus)))
    items.append((-(127 + 180), "project", None))
    t = a["trees"]
    for x, y, r in trees(Rng(3), 34, (15, 15, 280, 315)):
        items.append((-(x + y), "tree", (x, y, r)))
    for _, kind, d in sorted(items, key=lambda it: it[0]):
        if kind == "ctx":
            lab, fp, h, focus = d
            st = a["focus_bldg"] if focus else a["context_bldg"]
            sh.prism(fp, 0, h, st["fill"], (0, 0), a["context_bldg"]["stroke"], a["context_bldg"]["stroke_pt"])
            if focus:
                sh.outline(fp, 0, h, st["stroke"], st["stroke_pt"])
            cx, cy = sum(x for x, _ in fp) / 4, sum(y for _, y in fp) / 4
            q = sh.p(cx, cy, h)
            sh.lines_text(q[0], q[1], lab, a["label_context"], anchor="middle")
        elif kind == "project":
            pr, cm = a["project"], a["project"]["colors_measured"]
            fl = (darken(cm["academic_green"], 0.25), pr["floor_line_pt"])
            for fp, h, col, fls in ((LAB, 90, cm["lab_blue"], range(15, 90, 15)), (ATRIUM, 75, cm["atrium_yellow"], ()),
                                    (ACADEMIC, 75, cm["academic_green"], range(15, 75, 15))):  # far -> near
                sh.prism(fp, 0, h, col, pr["side_darken"], "#000000", 0.75, floors=fls,
                         floor_st=(darken(col, pr["floor_line_darken"]), pr["floor_line_pt"]))
                if fp is not ATRIUM:
                    sh.outline(fp, 0, h, "#000000", pr["silhouette_pt"])
        else:
            x, y, r = d
            c = t["canopies"][int(r * len(t["canopies"])) % len(t["canopies"])]
            b, top = sh.p(x, y, 0), sh.p(x, y, t["trunk_h_ft"] + t["canopy_h_ft"])
            sh.line([b, sh.p(x, y, t["trunk_h_ft"] + 4)], t["trunk"], t["trunk_pt"])
            cx, cy = top[0], (top[1] + sh.p(x, y, t["trunk_h_ft"])[1]) / 2
            sh.out.append(f'<ellipse cx="{cx:.2f}" cy="{cy:.2f}" rx="{t["canopy_w_ft"] * sh.k / 2:.2f}" '
                          f'ry="{t["canopy_h_ft"] * sh.k / 2:.2f}" fill="{c}" fill-opacity="{t["opacity"]}" '
                          f'stroke="{darken(c, 0.2)}" stroke-width="{t["stroke_pt"]}"/>')
    # streets
    for name, (x, y), (dx, dy) in STREETS:
        q = sh.p(x, y)
        sh.text(q[0], q[1], name, a["label_street"], matrix=sh.rot_matrix(dx, dy))
    # annotation layer: ped flows, gateway view, service
    pf = a["ped_flow"]
    for path, ic in (([(100, 330), (60, 360), (-20, 380)], 1), ([(330, 380), (260, 330), (225, 250)], 1),
                     ([(330, -130), (250, 40), (170, 110)], 1)):
        pts = [sh.p(x, y) for x, y in path]
        sh.arrow_path(pts, pf["color"], pf["stroke_pt"], pf["dash"], pf["head_pt"])
        sh.icon(pts[ic], pf["icon_d_pt"], pf["color"], "ped")
    v = a["view"]
    eye = sh.p(-60, -60)
    for tgt in ((-40, 280), (260, -40)):
        sh.arrow_path([eye, sh.p(*tgt)], v["color"], v["stroke_pt"], v["dash"], v["head_pt"])
    sh.icon(eye, v["icon_d_pt"], v["color"], "eye")
    q = sh.p(110, -22)
    sh.text(q[0], q[1], "Gateway", dict(a["label_street"], color=v["color"], tracking=400), matrix=sh.rot_matrix(1, 0))
    s = a["service"]
    ic = sh.p(250, 345, 45)
    sh.line([ic, sh.p(235, 318)], s["color"], s["leader_pt"])
    sh.icon(ic, s["icon_d_pt"], s["color"], "truck")
    sh.out.append("</g>")
    legend(sh, W, H, "SITE PROGRAM AXON", [
        ("swatch", a["ground"]["block"], "BLOCK / ROAD", f'{a["ground"]["block"]} / {a["ground"]["road"]}, CURB STRIP {a["ground"]["curb_strip"]}'),
        ("swatch", a["plaza"]["fill"], "PLAZA", f'{a["plaza"]["fill"]} + STIPPLE {a["plaza"]["stipple"]}'),
        ("swatch", a["lawn"]["fill"], "LAWN / PLANTING", f'{a["lawn"]["fill"]} + DASH HATCH {a["lawn"]["hatch"]}'),
        ("swatch", a["median"]["fill"], "MEDIAN / PARK", f'{a["median"]["fill"]}, EDGE {a["median"]["stroke_pt"]} PT {a["median"]["stroke"]}'),
        ("swatch", t["canopies"][0], "TREES", "LOLLIPOP, " + " ".join(t["canopies"]) + f' @ {int(t["opacity"] * 100)}%'),
        ("line", (a["context_bldg"]["stroke"], a["context_bldg"]["stroke_pt"]), "CONTEXT BUILDING", f'WHITE, {a["context_bldg"]["stroke_pt"]} PT {a["context_bldg"]["stroke"]}'),
        ("line", ("#000000", a["focus_bldg"]["stroke_pt"]), "FOCUS BUILDING", f'{a["focus_bldg"]["stroke_pt"]} PT BLACK SILHOUETTE'),
        ("swatch", a["project"]["colors_measured"]["lab_blue"], "PROJECT MASSING", f'PROGRAM COLOURS, {a["project"]["silhouette_pt"]} PT SILHOUETTE, FLOOR LINES {a["project"]["floor_line_pt"]} PT'),
        ("icon", (pf["color"], "ped"), "PEDESTRIAN FLOW", f'{pf["color"]} {pf["stroke_pt"]} PT DASH {pf["dash"][0]}/{pf["dash"][1]}, ICON {pf["icon_d_pt"]} PT'),
        ("icon", (v["color"], "eye"), "VIEW / GATEWAY", f'{v["color"]} {v["stroke_pt"]} PT DASH {v["dash"][0]}/{v["dash"][1]}'),
        ("icon", (s["color"], "truck"), "SERVICE", f'{s["color"]} ICON {s["icon_d_pt"]} PT'),
        ("text", a["label_context"], "CONTEXT LABEL", f'ARIAL BOLD {a["label_context"]["size"]} PT {a["label_context"]["color"]}, UNSHEARED'),
        ("text", a["label_street"], "STREET NAME", f'ARIAL {a["label_street"]["size"]} PT, TRACKING {a["label_street"]["tracking"]}, ROTATED'),
    ])
    return sh.out


def site_plan(cfg, W, H):
    a = cfg["site_plan"]
    sh = Sheet(PLAN, cfg["scale"]["pt_per_ft"], 0.08 * W + 250 * 1.6, 0.1 * H + 550 * 1.6)
    sh.out.append(f'<rect width="{W}" height="{H}" fill="#FFFFFF"/>')
    ground(sh, None, a["sidewalk"], a["road"])
    for name, fp in BLOCKS.items():
        x0, y0 = fp[0]
        x1, y1 = fp[2]
        sh.poly([sh.p(x, y) for x, y in rect(x0 + 8, y0 + 8, x1 - 8, y1 - 8)], a["block"])
    sh.poly([sh.p(x, y) for x, y in rect(370, -240, 440, -100)], a["park"])
    sh.poly([sh.p(x, y) for x, y in rect(410, 0, 450, 550)], a["trail"])
    cb = a["context_bldg"]
    for lab, fp, h, focus in CONTEXT:
        if lab[1] == "PARKING":
            continue
        dx, dy = cb["shadow_offset_pt"]
        sh.poly([(x + dx, y + dy) for x, y in (sh.p(*q) for q in fp)], cb["shadow"])
        sh.poly([sh.p(*q) for q in fp], cb["fill"])
        cx, cy = sum(x for x, _ in fp) / 4, sum(y for _, y in fp) / 4
        q = sh.p(cx, cy)
        sh.lines_text(q[0], q[1], lab, a["label_context"])
    gfp = CONTEXT[2][1]
    sh.poly([sh.p(*q) for q in gfp], cb["fill"], "#AEAEAE", 1.0)
    q = sh.p(405, 170)
    sh.lines_text(q[0], q[1], ["FUTURE", "PARKING"], a["label_context"])
    s = a["site"]
    sh.poly([sh.p(*q) for q in SITE_FP], s["plaza"])
    for fp in LAWNS:
        sh.poly([sh.p(*q) for q in fp], s["bed"])
    sh.poly([sh.p(*q) for q in rect(200, 150, 250, 190)], s["court"])
    sh.line([sh.p(*q) for q in [(20, 30), (120, 40), (230, 110), (260, 300)]], s["dashed_path"], s["dashed_path_pt"], s["dash"])
    tr = a["trees"]
    for x, y, r in trees(Rng(3), 40, (15, 15, 280, 315)):
        sh.circle(sh.p(x, y), (tr["d_ft"][0] + r * (tr["d_ft"][1] - tr["d_ft"][0])) * sh.k / 2, tr["fill"], op=tr["opacity"])
    # ground floor footprint with rooms
    b, cm = a["building"], a["building"]["colors_measured"]
    rooms = [("LOADING", rect(150, 260, 215, 300), cm["loading"]), ("VIVARIUM SUPPORT", rect(150, 240, 215, 260), cm["vivarium_support"]),
             ("EVENT SUPPORT", rect(150, 190, 215, 240), cm["event_support"]), ("FOOD SERVICE", rect(150, 140, 215, 190), cm["event_support"]),
             ("RR", rect(150, 120, 215, 140), cm["loading"]), ("", ATRIUM, s["path"]), ("", ACADEMIC, s["path"])]
    for name, fp, c in rooms:
        sh.poly([sh.p(*q) for q in fp], c, b["room_line"], b["room_line_pt"])
        if name:
            x0, y0 = fp[0]
            x1, y1 = fp[2]
            q = sh.p((x0 + x1) / 2, (y0 + y1) / 2)
            sz = a["label_room"]["size"] * (0.7 if len(name) > 13 else 1.0)
            sh.lines_text(q[0], q[1], name.split(" ") if len(name) > 10 else [name], dict(a["label_room"], size=sz, leading=sz * 1.15))
    for name, cx, cy in (("200 SEAT LECTURE", 70, 110), ("270 SEAT LECTURE", 80, 190)):
        fp = ellipse_fp(cx, cy, 28, 36)
        sh.poly([sh.p(*q) for q in fp], cm["lecture"], b["outline"], b["lecture_outline_pt"])
        q = sh.p(cx, cy)
        sh.lines_text(q[0], q[1], ["200 SEAT" if "200" in name else "270 SEAT", "LECTURE"], dict(a["label_room"], size=a["label_room"]["size"] * 0.8))
    outline = [(150, 120), (215, 120), (215, 300), (150, 300), (150, 230), (110, 230), (110, 60), (40, 60), (40, 230), (110, 230)]
    sh.poly([sh.p(*q) for q in rect(150, 120, 215, 300)], "none", b["outline"], b["outline_pt"])
    sh.poly([sh.p(*q) for q in rect(40, 60, 150, 230)], "none", b["outline"], b["outline_pt"])
    del outline
    e = a["entry"]
    for (x, y), left in (((25, 150), False), ((128, 50), True)):
        q = sh.p(x, y)
        sh.icon(q, e["icon_d_pt"], e["color"], "ped")
        t, sgn = e["triangle_pt"], -1 if left else 1
        bx = q[0] + sgn * (e["icon_d_pt"] / 2 + 6)
        sh.poly([(bx, q[1] - t / 2), (bx + sgn * t * 0.8, q[1]), (bx, q[1] + t / 2)], e["color"])
    vm = a["view_marker"]
    for n, (x, y), (tx, ty) in ((1, (-30, -30), (-5, 5)), (2, (275, 100), (250, 110))):
        q, t2 = sh.p(x, y), sh.p(tx, ty)
        sh.line([q, t2], vm["color"], vm["arrow_pt"], cap="round")
        sh.circle(q, vm["d_pt"] / 2, vm["color"])
        sh.text(q[0], q[1], str(n), {"size": vm["d_pt"] * 0.6, "weight": "Bold", "color": vm["number"]})
    sv = a["service"]
    q = sh.p(180, 335)
    sh.icon(q, sv["icon_d_pt"], sv["color"], "truck")
    sh.poly([(q[0] - 14, q[1] + 32), (q[0] + 14, q[1] + 32), (q[0], q[1] + 46)], sv["color"])
    for name, (x, y), (dx, dy) in STREETS:
        q = sh.p(x, y)
        sh.text(q[0], q[1], name, a["label_street"], matrix=sh.rot_matrix(dx, dy))
    tt = a["title_tag"]
    q = sh.p(450, -250)
    sh.poly([(q[0] - 200, q[1] - 50), (q[0], q[1] - 50), (q[0], q[1]), (q[0] - 200, q[1])], tt["fill"])
    sh.text(q[0] - 100, q[1] - 25, "Ground Floor", {"font": tt["font"], "size": tt["size"], "color": tt["color"]})
    legend(sh, W, H, "SITE PLAN", [
        ("swatch", a["road"], "ROAD / SIDEWALK / BLOCK", f'{a["road"]} / {a["sidewalk"]} / {a["block"]}'),
        ("shadow", None, "CONTEXT BUILDING", f'WHITE, NO STROKE, SHADOW {cb["shadow"]} OFFSET {cb["shadow_offset_pt"][0]}/{cb["shadow_offset_pt"][1]} PT'),
        ("swatch", a["park"], "PARK / TRAIL", f'{a["park"]} / {a["trail"]}'),
        ("swatch", s["plaza"], "PLAZA / PATH / BED", f'{s["plaza"]} / {s["path"]} / {s["bed"]}'),
        ("circle", tr["fill"], "TREES", f'{tr["fill"]} {int(tr["opacity"] * 100)}%, NO STROKE, {tr["d_ft"][0]}-{tr["d_ft"][1]} FT'),
        ("line", (b["outline"], b["outline_pt"]), "BUILDING OUTLINE", f'{b["outline_pt"]} PT BLACK; ROOMS {b["room_line_pt"]} PT WHITE'),
        ("swatch", cm["lecture"], "LECTURE", f'{cm["lecture"]}, {b["lecture_outline_pt"]} PT BLACK OUTLINE'),
        ("icon", (e["color"], "ped"), "ENTRY", f'{e["color"]} ICON {e["icon_d_pt"]} PT + TRIANGLE {e["triangle_pt"]} PT'),
        ("circle", vm["color"], "VIEW MARKER", f'{vm["color"]} {vm["d_pt"]} PT, WHITE NUMBER, ARROW {vm["arrow_pt"]} PT'),
        ("icon", (sv["color"], "truck"), "SERVICE", f'{sv["color"]} ICON {sv["icon_d_pt"]} PT + TRIANGLE'),
        ("text", a["label_context"], "CONTEXT LABEL", f'ARIAL BOLD {a["label_context"]["size"]} PT {a["label_context"]["color"]} (SAME AS SITE AXON)'),
        ("text", a["label_room"], "ROOM LABEL", f'ARIAL {a["label_room"]["size"]} PT {a["label_room"]["color"]}'),
    ])
    return sh.out


def exploded_b(cfg, W, H):
    a, k = cfg["exploded_b"], cfg["scale"]["pt_per_ft"]
    cm = a["colors_measured"]
    t = a["block_thickness_ft"]
    sh = Sheet(PLANO, k, 0.4 * W, 0)
    corners = [(x, y) for x in (40, 215) for y in (60, 300)]
    depth = max(sh.vec(x, y)[1] for x, y in corners) - min(sh.vec(x, y)[1] for x, y in corners)
    pitch = a["clear_ratio"] * depth
    top = min(sh.vec(x, y)[1] for x, y in corners) - 4 * pitch - t
    sh.oy = 1.2 * 72 - top * k  # top plate just under the top margin
    sh.out.append(f'<rect width="{W}" height="{H}" fill="#FFFFFF"/>')
    labs = [("_OFFICE", rect(150, 110, 215, 300), cm["lab_blue"]), ("LABS", rect(160, 130, 205, 290), cm["labs"]),
            ("GATHER", rect(110, 100, 150, 150), cm["gather"])]
    levels = [
        ("BASEMENT", -pitch, [("VIVARIUM", rect(150, 100, 215, 260), cm["vivarium"])]),
        ("LEVEL 1", 0, [("LECTURE", rect(40, 60, 110, 140), cm["class"]), ("FOOD", rect(40, 150, 100, 230), cm["gather"]),
                        ("BUILDING SUPPORT", rect(150, 180, 210, 300), cm["building_support"]),
                        ("EVENT SUPP", rect(150, 120, 210, 180), cm["gather"])]),
        ("LEVEL 2", pitch, [("SIMULATION", rect(40, 60, 110, 230), cm["simulation"])] + labs),
        ("LEVEL 3", 2 * pitch, [("CLASS", rect(40, 60, 110, 230), cm["class"])] + labs),
        ("LEVEL 4", 3 * pitch, [("CLASS", rect(40, 60, 110, 230), cm["class"])] + labs),
        ("LEVEL 5", 4 * pitch, [("OFFICE", rect(40, 60, 110, 230), cm["office"])] + labs),
    ]
    at, sf, L = a["atrium"], a["shaft"], a["label"]
    tags = []
    for n, (name, z, blocks) in enumerate(levels):
        if name == "LEVEL 1":  # site plane, with the site plan printed faintly on it
            sp, pl = a["site_plane"], cfg["site_plan"]
            sh.poly([sh.p(*q) for q in rect(-70, -70, 330, 380)], sp["fill"], sp["stroke"], sp["stroke_pt"])
            sh.out.append(f'<g opacity="{sp["plan_opacity"]}">')
            sh.poly([sh.p(*q) for q in SITE_FP], pl["site"]["plaza"])
            for fp in LAWNS:
                sh.poly([sh.p(*q) for q in fp], pl["site"]["bed"])
            for x, y, r in trees(Rng(3), 40, (15, 15, 280, 315)):
                sh.circle(sh.p(x, y), 8 * k, pl["trees"]["fill"], op=pl["trees"]["opacity"])
            sh.out.append("</g>")
            for st, (x, y), (dx, dy) in (("UNIVERSITY BLVD", (-40, 170), (0, 1)), ("LOMAS BLVD", (160, -40), (1, 0)),
                                        ("TUCKER AVE", (160, 355), (1, 0))):
                q = sh.p(x, y)
                sh.text(q[0], q[1], st, sp["street"], matrix=sh.plane_matrix(dx, dy))
        if name != "BASEMENT":
            sh.prism(rect(110, 150, 150, 240), z, z + t * 0.5, at["fill"], (0.05, 0.1), op=at["opacity"])
        if n >= 2:  # shafts and atrium envelope between this plate and the one below
            zb = levels[n - 1][1] + t
            for x, y in ((110, 150), (150, 150), (110, 240)):
                sh.line([sh.p(x, y, zb), sh.p(x, y, z)], at["envelope"], at["envelope_pt"])
        if n >= 1:
            zb = levels[n - 1][1] + t
            for x, y in ((130, 125), (125, 235)):
                sh.line([sh.p(x, y, zb), sh.p(x, y, z)], sf["stroke"], sf["stroke_pt"], cap="round")
        for bname, fp, c in blocks:
            zt = z + t + (1 if bname == "LABS" else 0)
            sh.prism(fp, z, zt, c, a["side_darken"], a["edge"]["stroke"], a["edge"]["stroke_pt"], op=a["plate_opacity"])
            x0, y0 = fp[0]
            x1, y1 = fp[2]
            long_x = (x1 - x0) >= (y1 - y0)
            length = (x1 - x0 if long_x else y1 - y0) * k
            size = min(L["size"], 0.75 * length / (len(bname) * 0.62))
            if size < L["small"]:
                size = L["small"] if L["small"] * len(bname) * 0.62 < length else 0
            if size and not bname.startswith("_"):
                q = sh.p((x0 + x1) / 2, (y0 + y1) / 2, zt)
                sh.text(q[0], q[1], bname, L, matrix=sh.plane_matrix(1, 0) if long_x else sh.plane_matrix(0, 1),
                        size=size, op=L["opacity"])
        right = max((sh.p(x, y, z + t) for _, fp, _ in blocks for x, y in fp), key=lambda q: q[0])
        tags.append((name, right))
    lt = a["level_tag"]
    col = max(q[0] for _, q in tags) + 120
    for name, (px, py) in tags:
        ly = py + (col - px) * 0.35
        sh.line([(px + 4, py + 1.4), (col, ly)], lt["leader"], lt["leader_pt"])
        m = lt["marker_pt"]
        sh.poly([(col + 4, ly - m / 2), (col + 4 + m, ly - m / 2), (col + 4 + m, ly + m / 2), (col + 4, ly + m / 2)], lt["marker_fill"])
        sh.text(col + 4 + m + lt["gap_pt"], ly, name, lt, anchor="start")
    legend(sh, W, H, "EXPLODED AXON B", [
        ("swatch", cm["labs"], "PROGRAM BLOCKS", f'{int(a["plate_opacity"] * 100)}% OPACITY, {t} FT THICK, SIDES -{int(a["side_darken"][0] * 100)}/-{int(a["side_darken"][1] * 100)}%, {a["edge"]["stroke_pt"]} PT WHITE EDGE'),
        ("swatch", at["fill"], "ATRIUM / CIRCULATION", f'{at["fill"]} {int(at["opacity"] * 100)}%, ENVELOPE {at["envelope_pt"]} PT {at["envelope"]}'),
        ("line", (sf["stroke"], sf["stroke_pt"]), "VERTICAL CORE / STAIR", f'{sf["stroke_pt"]} PT {sf["stroke"]}'),
        ("swatch", cm["vivarium"], "BASEMENT", f'{cm["vivarium"]}, SAME BLOCK STYLE'),
        ("swatch", "#FFFFFF", "SITE PLANE", f'WHITE, {a["site_plane"]["stroke_pt"]} PT {a["site_plane"]["stroke"]}, SITE PLAN @ {int(a["site_plane"]["plan_opacity"] * 100)}%'),
        ("text", dict(L, color="#999999"), "PROGRAM LABEL", f'ARIAL BOLD {L["size"]}/{L["small"]} PT WHITE {int(L["opacity"] * 100)}%, CENTRED, ON PLANE'),
        ("swatch", lt["marker_fill"], "LEVEL TAG", f'ARIAL BOLD {lt["size"]} PT CAPS {lt["color"]}, MARKER {lt["marker_pt"]} PT, LEADER {lt["leader_pt"]} PT {lt["leader"]}'),
        ("text", {"font": "Arial", "size": 14, "color": "#333333"}, "PROJECTION",
         f'PLANOMETRIC 30/60 (PLAN Y UP-RIGHT 30\u00b0), PITCH = {a["clear_ratio"]} x PLATE DEPTH = {pitch:.0f} FT; ARCH D PORTRAIT'),
    ], x0=1.0 * 72)
    return sh.out


def program_section(cfg, W, H):
    a, ty = cfg["program_section"], cfg["typography"]
    k, fh = a["pt_per_ft"], a["floor_ft"]
    cm = a["colors_measured"]
    sh = Sheet((1.0, 0.0, 0.0, 0.0), k, (72 + W - 9.0 * 72) / 2 - 130 * k, 0.62 * H)  # x along the cut, z up
    sh.out.append(f'<rect width="{W}" height="{H}" fill="#FFFFFF"/>')
    P = lambda x, z: sh.p(x, 0, z)
    # (block, floor index, x0, x1, colour key, label); floor -1 = basement
    bars = [
        ("W", 0, 40, 100, "shared_public", "SHARED PUBLIC"), ("W", 1, 40, 110, "academic", "ACADEMIC"),
        ("W", 2, 40, 100, "academic", ""), ("W", 3, 50, 110, "academic", ""), ("W", 4, 60, 100, "admin", "ADMIN"),
        ("W", 1, 112, 120, "circulation", ""), ("W", 2, 100, 115, "circulation", ""), ("W", 3, 112, 120, "circulation", ""),
        ("W", 4, 100, 115, "circulation", ""),
        ("C", 1, 132, 152, "circulation", "COMMUNITY"), ("C", 2, 132, 152, "circulation", ""), ("C", 3, 132, 152, "circulation", ""),
        ("E", 0, 172, 235, "building_support", "BUILDING SUPPORT"), ("E", 1, 172, 235, "simulation", "SIMULATION"),
        ("E", 2, 166, 235, "research", "RESEARCH"), ("E", 3, 172, 235, "research", ""), ("E", 4, 172, 225, "research", ""),
        ("E", 1, 160, 170, "circulation", ""), ("E", 2, 160, 165, "circulation", ""), ("E", 3, 160, 170, "circulation", ""),
        ("E", 4, 160, 170, "circulation", ""), ("E", -1, 172, 235, "vivarium", "VIVARIUM"),
    ]
    sl, br, lb, fn = a["slab"], a["bar"], a["label"], a["floor_number"]
    bar_h = br["height_ratio"] * fh * k
    # envelope lines and floor lines (thin) behind everything
    env = a["envelope"]
    for x0, x1, top in ((40, 120, 5), (160, 235, 5), (128, 156, 4)):
        sh.poly([P(x0, 0), P(x1, 0), P(x1, top * fh), P(x0, top * fh)], "none", env["stroke"], env["stroke_pt"])
    for f in range(1, 5):
        sh.line([P(20, f * fh), P(250, f * fh)], env["stroke"], env["stroke_pt"])
    # future building beyond, property lines
    fb = a["future_building"]
    for f in range(0, 5):
        sh.line([P(300, (f + 1) * fh), P(285, (f + 1) * fh), P(285, f * fh)], fb["stroke"], fb["stroke_pt"], fb["dash"])
    pl = a["property_line"]
    for x in (0, 262):
        sh.line([P(x, -22), P(x, 6.2 * fh)], pl["stroke"], pl["stroke_pt"], pl["dash"])
    # trees and people (entourage) on grade and terraces
    tr = a["trees"]
    rng = Rng(11)
    for i, (x, z, h) in enumerate([(-30, 0, 30), (-12, 0, 24), (18, 0, 28), (124, 0, 32), (138, 0, 26), (146, 0, 34),
                                   (250, 0, 26), (272, 0, 30), (140, 4 * fh + 1, 18), (113, 3 * fh + 1, 12)]):
        c = tr["fills"][i % len(tr["fills"])]
        sh.line([P(x, z), P(x, z + h * 0.45)], tr["trunk"], tr["trunk_pt"])
        cx, cy = P(x, z + h * 0.68)
        sh.out.append(f'<ellipse cx="{cx:.2f}" cy="{cy:.2f}" rx="{h * 0.28 * k:.2f}" ry="{h * 0.33 * k:.2f}" fill="{c}" fill-opacity="{tr["opacity"]}"/>')
    for x, f in ((70, 0), (88, 1), (190, 2), (205, 0), (140, 1), (118, 3), (-40, 0), (230, 0)):
        bx, by = P(x + rng() * 6, f * fh)
        sh.out.append(f'<rect x="{bx:.2f}" y="{by - 5.6 * k:.2f}" width="{0.9 * k:.2f}" height="{5.6 * k:.2f}" rx="{0.45 * k:.2f}" fill="{a["people"]["fill"]}"/>')
    # bars, then slabs over them
    floors = {}
    for blk, f, x0, x1, key, name in bars:
        z0 = f * fh + (1 - sl["height_ratio"] - br["top_gap_ratio"] - br["height_ratio"]) * fh
        xa, ya = P(x0, z0 + br["height_ratio"] * fh)
        w = (x1 - x0) * k
        r = br["corner_ratio"] * fh * k
        sh.out.append(f'<rect x="{xa:.2f}" y="{ya:.2f}" width="{w:.2f}" height="{bar_h:.2f}" rx="{r:.2f}" fill="{cm[key]}"/>')
        size = min(lb["size_max"], lb["size_ratio"] * bar_h)
        fsz = min(fn["size_max"], fn["size_ratio"] * bar_h)
        cy = ya + bar_h / 2
        numbered = key != "circulation" and f >= 0 and (blk, f) not in floors
        if numbered:
            sh.text(xa + fn["inset_pt"], cy, f"{f + 1:02d}", dict(fn, font="Arial"), anchor="start", size=fsz)
        if name:  # shrink to fit beside the floor number, skip if it would drop below size_min
            room = w - lb["inset_pt"] - (fn["inset_pt"] + 1.4 * fsz + 8 if numbered else lb["inset_pt"])
            size = min(size, room / (len(name) * 0.62))
            if size >= lb["size_min"]:
                sh.text(xa + w - lb["inset_pt"], cy, name, dict(lb, font="Arial"), anchor="end", size=size, op=lb["opacity"])
        if f >= 0:
            lo, hi = floors.get((blk, f), (x0, x1))
            floors[(blk, f)] = (min(lo, x0), max(hi, x1))
    sh_h = sl["height_ratio"] * fh * k
    for (blk, f), (x0, x1) in floors.items():
        if f == 0:
            continue
        xa, ya = P(x0 - sl["overhang_ft"], f * fh)
        sh.poly([(xa, ya - sh_h / 2), (xa + (x1 - x0 + 2 * sl["overhang_ft"]) * k, ya - sh_h / 2),
                 (xa + (x1 - x0 + 2 * sl["overhang_ft"]) * k, ya + sh_h / 2), (xa, ya + sh_h / 2)], sl["fill"], sl["stroke"], sl["stroke_pt"])
    for x0, x1 in ((38, 122), (158, 237), (128, 156)):  # roofs
        top = 4 if x0 == 128 else 5
        xa, ya = P(x0, top * fh)
        sh.poly([(xa, ya - sh_h / 2), (xa + (x1 - x0) * k, ya - sh_h / 2), (xa + (x1 - x0) * k, ya + sh_h / 2), (xa, ya + sh_h / 2)],
                sl["fill"], sl["stroke"], sl["stroke_pt"])
    # ground line
    g = a["ground"]
    sh.line([P(-60, 0), P(320, 0)], g["stroke"], g["stroke_pt"])
    # view chevrons
    v = a["view"]
    for x, z, d in ((32, 3.4 * fh, -1), (125, 2.5 * fh, 1), (157, 3.4 * fh, -1)):
        tip = P(x, z)
        ends = [(tip[0] + d * 6 * k, tip[1] - 3.6 * k), (tip[0] + d * 6 * k, tip[1] + 3.6 * k)]
        sh.line([ends[0], tip, ends[1]], v["stroke"], v["stroke_pt"])
        for e in ends:
            sh.circle(e, v["dot_pt"] / 2, v["stroke"])
    # headings and street labels
    hd = dict(ty["heading"], font="Arial")
    for x, t in ((75, "Inside / outside"), (142, "Daylight"), (203, "Research stack")):
        q = P(x, 6.0 * fh)
        sh.text(q[0], q[1], t, hd)
    st = dict(a["street"], font="Arial")
    for x, t in ((-30, "Lomas"), (142, "Courtyard"), (262 + 18, "Tucker")):
        q = P(x, -16)
        sh.text(q[0], q[1], t, st)
    q = P(305, -16)
    sh.lines_text(q[0], q[1], ["FUTURE", "BUILDING"], dict(st, leading=st["size"] * 1.1))
    legend(sh, W, H, "PROGRAM SECTION", [
        ("swatch", cm["academic"], "PROGRAM BAR", f'ROUNDED, R = {br["corner_ratio"]} x FLOOR, HEIGHT {br["height_ratio"]} x FLOOR, NO STROKE'),
        ("line", (sl["stroke"], sl["stroke_pt"]), "SLAB", f'WHITE BAR {sl["height_ratio"]} x FLOOR TALL, {sl["stroke_pt"]} PT {sl["stroke"]}, +{sl["overhang_ft"]} FT EACH END'),
        ("swatch", cm["circulation"], "CIRCULATION / COMMUNITY", f'{cm["circulation"]}, EDGE ZONE AND BRIDGES'),
        ("text", dict(lb, font="Arial", size=20, color="#333333"), "PROGRAM LABEL", f'ARIAL {lb["size_max"]} PT MAX ({lb["size_ratio"]} x BAR HEIGHT), {lb["color"]} {int(lb["opacity"] * 100)}%, RIGHT ALIGNED'),
        ("text", dict(fn, font="Arial", size=20), "FLOOR NUMBER", f'ARIAL {fn["size_ratio"]} x BAR HEIGHT, {fn["color"]}, LEFT IN BAR, 01 02 03'),
        ("line", (env["stroke"], env["stroke_pt"]), "ENVELOPE / FLOOR LINES", f'{env["stroke_pt"]} PT {env["stroke"]}'),
        ("line", (g["stroke"], g["stroke_pt"]), "GROUND", f'{g["stroke_pt"]} PT BLACK'),
        ("line", (pl["stroke"], pl["stroke_pt"]), "PROPERTY LINE", f'{pl["stroke_pt"]} PT {pl["stroke"]} DASH {pl["dash"][0]}/{pl["dash"][1]}'),
        ("line", (fb["stroke"], fb["stroke_pt"]), "FUTURE / BEYOND", f'{fb["stroke_pt"]} PT {fb["stroke"]} DASH {fb["dash"][0]}/{fb["dash"][1]}'),
        ("line", (v["stroke"], v["stroke_pt"]), "VIEW CHEVRON", f'{v["stroke_pt"]} PT {v["stroke"]}, DOT ENDS {v["dot_pt"]} PT'),
        ("circle", tr["fills"][0], "TREES / PEOPLE", f'LOLLIPOP {" ".join(tr["fills"])} @ {int(tr["opacity"] * 100)}%; PEOPLE {a["people"]["fill"]}'),
        ("text", dict(hd, size=20), "HEADING", f'ARIAL BOLD {hd["size"]} PT CAPS, TRACKING {hd["tracking"]}, {hd["color"]}'),
        ("text", dict(st, size=20), "STREET / PLACE", f'ARIAL {st["size"]} PT CAPS {st["color"]}, BELOW GRADE'),
    ], sub="1\" = 16'-0\" ON ARCH D 36\u00d724 (SECTION ONLY). ALL TEXT ARIAL. REF 6: PT = PX \u00d7 1.556")
    return sh.out


def legend(sh, W, H, title, rows, x0=None, sub=None):
    x0, y = (W - 8.6 * 72) if x0 is None else x0, 1.0 * 72
    out = sh.out
    sh.text(x0, y, title + " — STYLE REFERENCE", {"font": "Arial", "size": 20, "weight": "Bold", "color": "#000000"}, anchor="start")
    y += 22
    sh.text(x0, y, "1\" = 45'-0\" ON ARCH D 36×24. PT = PX × 1.556 (REFERENCE WIDTH = SHEET WIDTH)",
            {"font": "Arial", "size": 11, "color": "#666666"}, anchor="start")
    for kind, v, name, spec in rows:
        y += 46
        cx, cy = x0 + 24, y
        if kind == "swatch":
            out.append(f'<rect x="{cx - 18}" y="{cy - 12}" width="36" height="24" fill="{v}" stroke="#BBBBBB" stroke-width="0.5"/>')
        elif kind == "line":
            sh.line([(cx - 20, cy), (cx + 20, cy)], v[0], v[1])
        elif kind == "icon":
            sh.icon((cx, cy), 30, v[0], v[1])
        elif kind == "circle":
            sh.circle((cx, cy), 13, v, op=0.8)
        elif kind == "shadow":
            out.append(f'<rect x="{cx - 14}" y="{cy - 8}" width="32" height="20" fill="#AEAEAE"/>'
                       f'<rect x="{cx - 18}" y="{cy - 12}" width="32" height="20" fill="#FFFFFF" stroke="#DDDDDD" stroke-width="0.5"/>')
        elif kind == "text":
            sh.text(cx, cy, "Aa", dict(v, case=None), size=20)
        sh.text(x0 + 60, y - 9, name, {"font": "Arial", "size": 14, "weight": "Bold", "color": "#000000"}, anchor="start")
        sh.text(x0 + 60, y + 9, spec, {"font": "Arial", "size": 11, "color": "#444444"}, anchor="start")


def main(style_path, out_dir):
    with open(style_path, encoding="utf-8") as f:
        cfg = json.load(f)
    for name, fn, ab in (("site_axon_reference.svg", site_axon, cfg["artboard"]),
                         ("site_plan_reference.svg", site_plan, cfg["artboard"]),
                         ("axon_exploded_b_reference.svg", exploded_b, cfg["exploded_b"]["artboard"]),
                         ("program_section_reference.svg", program_section, cfg["artboard"])):
        W, H = ab["width_in"] * 72, ab["height_in"] * 72
        body = fn(cfg, W, H)
        svg = (f'<svg xmlns="http://www.w3.org/2000/svg" width="{W / 72}in" height="{H / 72}in" viewBox="0 0 {W} {H}">\n'
               + "\n".join(body) + "\n</svg>\n")
        with open(os.path.join(out_dir, name), "w", encoding="utf-8") as f:
            f.write(svg)


if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2])
