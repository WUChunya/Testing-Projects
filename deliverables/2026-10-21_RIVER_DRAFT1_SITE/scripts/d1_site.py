"""RIVER 2026-10-21 DRAFT_1 → spiral stairs, auditorium furniture, gardens and terraces designed (step 1 of 3).

Usage: python3 -I d1_site.py <fixture.json of in.3dm> <in.3dm> <out.3dm>
- Every round double-height opening (a circular hole in a floor plate) gets a small spiral stair on PROGRAM::Stair,
  from the floor below the hole to the floor with the hole, standing against the hole's edge.
- Auditorium: seats in curved rows with aisles, stage and screen (2D curves on FURNITURE).
- Every named area on `greens` (GARDEN / TERRACE / GREEN ROOF) is designed inside its own outline after the user's two
  reference plans (ref1.py, ref2.py): the irregular gardens and the terraces take the zoning of the riverside park plan
  (paved plaza, gravel, timber deck, angular walk, lawn mounds and planting beds with filleted corners, tree grates),
  mapped into each area along its spine; the south gardens and the parking roof garden take the terraced lawn of
  straight lines turning on fillets, crossed by straight paths, with planters between the lines.
  The cut green keeps its layer and name; replaced objects move to OUTSIDE BUILDING (LEFTOVERS).
- The previous PROJECT SITE::PLAZA paving moves to LEFTOVERS (the site is laid out again in d1_plaza.py).
"""
import sys, math, random
sys.path.insert(0, __file__.rsplit('/', 1)[0])
from r21_lib import *
from shapely.ops import polylabel, nearest_points, triangulate

M = Model(sys.argv[1], sys.argv[2])
rng = random.Random(1021)
G = 'PROJECT SITE::GARDENS'
M.role(G, 'Ignore', mode='')
M.role(f'{G}::PATH', 'NC.Hardscape')
M.role(f'{G}::SEATING', 'NC.Hardscape')
M.role(f'{G}::DECK', 'NC.Hardscape')
M.role(f'{G}::PAVING INLAY', 'NC.Hardscape')
M.role(f'{G}::FURNITURE', 'NC.Furniture', mode='line')
M.role(f'{G}::TREES', 'NC.Tree')
M.role(f'{G}::PEOPLE', 'Entourage')
M.role('FURNITURE', 'NC.Furniture', mode='line')
FP = {o['id']: footprint(o) for o in M.fx if o.get('mesh')}
Z = {o['id']: (o['bbox'][2] / FT, o['bbox'][5] / FT) for o in M.fx}


def walls_at(z):
    """Building footprint (program masses) standing at height z."""
    return unary_union([FP[i] for i in FP if FP[i] is not None and M.byid[i]['layer'].startswith('PROGRAM::')
                        and M.byid[i]['layer'] not in ('PROGRAM::Stair',) and Z[i][0] <= z + 0.5 and Z[i][1] >= z + 2])


def circle_curve(c, rad, z, path, name=""):
    return str(M.m.Objects.AddCircle(r.Circle(r.Point3d(c[0], c[1], z), rad), M.attrs(path, name)))


def arc_line(c, rad, a0, a1, n=24):
    return LineString([(c[0] + rad * math.cos(math.radians(a0 + (a1 - a0) * k / n)),
                        c[1] + rad * math.sin(math.radians(a0 + (a1 - a0) * k / n))) for k in range(n + 1)])

# ------------------------------------------------------------------ 1. spiral stairs in the round openings
holes = []
for o in M.fx:
    if o['layer'] != 'FLOOR PLATE' or not o.get('mesh') or FP[o['id']] is None: continue
    for p in parts(FP[o['id']]):
        for h in p.interiors:
            hp = Polygon(h)
            if hp.area > 300 and 4 * math.pi * hp.area / hp.length ** 2 > 0.85 and hp.area < 2000:
                c = hp.centroid
                if not any(abs(c.x - x) < 2 and abs(c.y - y) < 2 for x, y, _, _, _ in holes):
                    holes.append((c.x, c.y, math.sqrt(hp.area / math.pi), Z[o['id']][1], o['id']))
levels = sorted({l['elevation'] for l in M.st['levels']})
STAIR_R, RISE = 6.0, 0.67
for k, (x, y, hr, ztop, pid) in enumerate(holes):
    zb = max(l for l in levels if l < ztop - 1)           # the floor below the opening
    # Against the hole's edge, on the side towards the floor's middle (the landing side).
    pc = FP[pid].centroid
    ux, uy = pc.x - x, pc.y - y; n = math.hypot(ux, uy) or 1; ux, uy = ux / n, uy / n
    cx, cy = x + ux * (hr - STAIR_R - 1.0), y + uy * (hr - STAIR_R - 1.0)
    steps = int(round((ztop - zb) / RISE))
    sweep = 330.0
    a_end = math.degrees(math.atan2(uy, ux))              # the top tread points at the landing
    a0 = a_end - sweep
    da = sweep / steps
    ids = []
    for s in range(steps):
        a = a0 + s * da
        wedge = Polygon([(cx + 0.45 * math.cos(math.radians(a)), cy + 0.45 * math.sin(math.radians(a)))]
                        + [(cx + STAIR_R * math.cos(math.radians(a + da * 1.15 * t / 6)), cy + STAIR_R * math.sin(math.radians(a + da * 1.15 * t / 6))) for t in range(7)]
                        + [(cx + 0.45 * math.cos(math.radians(a + da * 1.15)), cy + 0.45 * math.sin(math.radians(a + da * 1.15)))])
        zt = zb + (s + 1) * (ztop - zb) / steps
        ids += M.add_solid(wedge.buffer(0), zt - 0.25, zt, 'PROGRAM::Stair', 'SPIRAL STAIR', min_area=0.5)
    ids += M.add_solid(Point(cx, cy).buffer(0.45, 16), zb, ztop + 3.0, 'PROGRAM::Stair', 'SPIRAL STAIR', min_area=0.1)
    M.log.append(f"spiral stair {k + 1}: opening r {hr:.0f} ft at ({x:.0f}, {y:.0f}), {zb:.0f}' → {ztop:.0f}', {steps} treads, Ø {2 * STAIR_R:.0f} ft")

# ------------------------------------------------------------------ 2. auditorium: seats, aisles, stage, screen
aid = M.find('54da442f')
aud = FP[aid]; ac = aud.centroid; AR = math.sqrt(aud.area / math.pi)
zf = Z[aid][0] + 0.1
inner = Point(ac.x, ac.y).buffer(AR - 2.5, 96)
stage_y = ac.y + AR * 0.42                                # stage on the north (river) side
stage = Point(ac.x, ac.y).buffer(AR - 2.0, 96).intersection(box(ac.x - AR, stage_y, ac.x + AR, ac.y + AR))
M.add_line(LineString(stage.exterior.coords), zf, 'FURNITURE', 'STAGE')
scr_y = ac.y + AR - 7
half = math.sqrt(max(0, (AR - 3) ** 2 - (scr_y - ac.y) ** 2)) * 0.85
screen = box(ac.x - half, scr_y, ac.x + half, scr_y + 0.8)
M.add_line(LineString(screen.exterior.coords), zf, 'FURNITURE', 'SCREEN')
M.add_line(LineString(box(ac.x + half * 0.55, stage_y + 4, ac.x + half * 0.55 + 2.5, stage_y + 6).exterior.coords), zf, 'FURNITURE', 'LECTERN')
focus = (ac.x, stage_y + 6)
aisles = unary_union([LineString([focus, (focus[0] + 300 * math.cos(math.radians(a)), focus[1] + 300 * math.sin(math.radians(a)))]).buffer(2.2)
                      for a in (238, 270, 302)]
                     + [Point(focus).buffer(30.5).difference(Point(focus).buffer(27.5))])          # cross aisle
seats = 0
rad = 15.0
while rad < AR * 1.6:
    step = 2.0 / rad                                       # 2 ft seat pitch
    a = math.radians(185)
    while a < math.radians(355):
        sx, sy = focus[0] + rad * math.cos(a), focus[1] + rad * math.sin(a)
        seat = affinity.rotate(box(sx - 0.85, sy - 0.85, sx + 0.85, sy + 0.85), math.degrees(a) + 90, origin=(sx, sy))
        if inner.contains(seat) and not aisles.intersects(seat) and not stage.buffer(3).intersects(seat):
            M.add_line(LineString(seat.exterior.coords), zf, 'FURNITURE', 'AUDITORIUM SEAT'); seats += 1
        a += step
    rad += 3.0
M.log.append(f"auditorium: {seats} seats in curved rows (3 radial aisles + cross aisle), stage, screen {2 * half:.0f} ft, lectern (FURNITURE, 2D)")

# ------------------------------------------------------------------ 3. gardens and terraces
def twin_curves(oid):
    """Curves on greens drawing the same outline as a green solid (its 2D copy)."""
    b = M.byid[oid]['bbox']
    return [o['id'] for o in M.fx if o['layer'] == 'greens' and 'Curve' in o['type'] and o['id'] != oid
            and all(abs(o['bbox'][i] - b[i]) < 2 * FT for i in (0, 1, 3, 4)) and abs(o['bbox'][2] - b[2]) < 1 * FT]


def long_axis(g):
    mr = g.minimum_rotated_rectangle
    cs = list(mr.exterior.coords)[:4]
    e = sorted([(cs[i], cs[i + 1]) for i in range(2)] + [(cs[2], cs[3])], key=lambda s: -math.dist(*s))[0]
    vx, vy = e[1][0] - e[0][0], e[1][1] - e[0][1]; n = math.hypot(vx, vy)
    return (vx / n, vy / n), min(math.dist(cs[0], cs[1]), math.dist(cs[1], cs[2]))


def winding_path(g, width, seed):
    """S-curve along the area's long axis, from edge to edge."""
    (dx, dy), short = long_axis(g)
    c = g.centroid
    proj = [((x - c.x) * dx + (y - c.y) * dy) for x, y in g.exterior.coords]
    a, b = min(proj) - 5, max(proj) + 5
    nx, ny = -dy, dx
    amp = max(0.0, min(short * 0.22, 18.0))
    ph = random.Random(seed).uniform(0, math.pi)
    pts = []
    for i in range(61):
        t = i / 60; s = a + (b - a) * t
        o = amp * math.sin(2 * math.pi * t * 1.0 + ph) * math.sin(math.pi * t)
        pts.append((c.x + dx * s + nx * o, c.y + dy * s + ny * o))
    line = LineString(pts)
    return line, line.buffer(width / 2, cap_style=2, join_style=1).intersection(g)


def benches_round(c, rad, z, n=3, span=55):
    """Curved benches inside a round seating area."""
    for k in range(n):
        a0 = 360 / n * k + rng.uniform(0, 30)
        M.add_line(arc_line(c, rad, a0, a0 + span), z, f'{G}::FURNITURE', 'BENCH')


def tables(c, z):
    circle_curve(c, 1.6, z, f'{G}::FURNITURE', 'TABLE')
    for k in range(4):
        a = math.radians(45 + 90 * k)
        circle_curve((c[0] + 3.0 * math.cos(a), c[1] + 3.0 * math.sin(a)), 0.7, z, f'{G}::FURNITURE', 'CHAIR')


def people(g, n, z):
    g = g.buffer(-1.5)
    if g.is_empty: return 0
    minx, miny, maxx, maxy = g.bounds; placed = 0
    for _ in range(400):
        if placed >= n: break
        p = Point(rng.uniform(minx, maxx), rng.uniform(miny, maxy))
        if g.contains(p):
            circle_curve((p.x, p.y), 1.0, z, f'{G}::PEOPLE'); placed += 1
    return placed


def trees_in(g, spacing, z, radius=(7, 9.5), cap=99):
    g = g.buffer(-3.5)
    pts = []
    if g.is_empty: return pts
    minx, miny, maxx, maxy = g.bounds
    for _ in range(1500):
        if len(pts) >= cap: break
        p = Point(rng.uniform(minx, maxx), rng.uniform(miny, maxy))
        if g.contains(p) and all(p.distance(q) >= spacing for q in pts): pts.append(p)
    for p in pts: circle_curve((p.x, p.y), rng.uniform(*radius), z, f'{G}::TREES')
    return pts


def z_on(o):
    """Height of a sloped site mesh at (x, y), from its triangles."""
    tris = [(Polygon(t[:, :2]), np.linalg.lstsq(np.c_[t[:, :2], np.ones(3)], t[:, 2], rcond=None)[0]) for t in triangles(o)]
    tris = [(p, c) for p, c in tris if p.area > 1e-3]
    def f(x, y):
        q = Point(x, y)
        for p, c in tris:
            if p.buffer(0.05).contains(q): return float(c[0] * x + c[1] * y + c[2])
        return o['bbox'][5] / FT
    return f


def mesh_part(o, keep, path, name):
    """The part of a site mesh inside `keep`, as its own mesh (the original is retired separately)."""
    mesh = r.Mesh(); n = 0
    for t in triangles(o):
        tp = Polygon(t[:, :2])
        if tp.area < 1e-4: continue
        try: coef = np.linalg.solve(np.c_[t[:, :2], np.ones(3)], t[:, 2])
        except np.linalg.LinAlgError: continue
        for p in parts(tp.intersection(keep)):
            for tri in triangulate(p):
                if p.area < 1e-3 or not p.contains(tri.representative_point()): continue
                for x, y in list(tri.exterior.coords)[:3]: mesh.Vertices.Add(x, y, coef[0] * x + coef[1] * y + coef[2])
                mesh.Faces.AddFace(n, n + 1, n + 2); n += 3
    if n == 0: return None
    mesh.Normals.ComputeNormals(); mesh.Compact()
    return str(M.m.Objects.AddMesh(mesh, M.attrs(path, name)))


# Design language (user, 10-21: "一系列fillet线"): every garden / terrace is a family of long parallel bands that follow
# the area's own spine, with filleted (round) ends and staggered lengths — seating steps / deck, a walk, and planting
# between them, like the stepped riverside seating of the reference.
from shapely.ops import substring


def spine(g, axis=None):
    """Smooth centre line of an area along `axis` (default its long axis): midpoints of the cross-sections."""
    (dx, dy), short = long_axis(g)
    if axis is not None: dx, dy = axis
    nx, ny = -dy, dx
    c = g.centroid
    proj = [((x - c.x) * dx + (y - c.y) * dy) for x, y in g.exterior.coords]
    a, b = min(proj), max(proj)
    mids = []
    s = a + 2
    while s < b - 2:
        p = (c.x + dx * s, c.y + dy * s)
        cut = LineString([(p[0] - nx * 2000, p[1] - ny * 2000), (p[0] + nx * 2000, p[1] + ny * 2000)]).intersection(g)
        segs = lines_of(cut)
        if segs:
            seg = max(segs, key=lambda l: l.length)
            mids.append(seg.interpolate(0.5, normalized=True).coords[0])
        s += 3
    if len(mids) < 4: return None
    k = 6   # moving average
    sm = [tuple(np.mean(mids[max(0, i - k):i + k + 1], axis=0)) for i in range(len(mids))]
    return LineString(sm)


def width_of(g, sp):
    """Extent of the area across its spine."""
    a, b = sp.coords[0], sp.coords[-1]
    dx, dy = b[0] - a[0], b[1] - a[1]; n = math.hypot(dx, dy) or 1
    nx, ny = -dy / n, dx / n
    pr = [x * nx + y * ny for x, y in g.exterior.coords]
    local = sorted(2 * g.exterior.distance(sp.interpolate(k / 10, normalized=True)) for k in range(2, 9))
    return min(max(pr) - min(pr), local[len(local) // 2] * 1.1)


# Scale after the Vancouver WA / Seattle waterfronts; shapes from the user's two reference plans (10-21):
# ref 1 (riverside park plan) zoning for the irregular gardens and the terraces, ref 2 (terraced lawn: straight lines
# turning on fillets) for the south gardens and the parking roof garden.
import ref1, ref2
REF1, MOUNDS1 = ref1.shapes()
SOUTH = ('c4485c57', '1f02ebc1', '0cb1ec08', '67c09ee7', 'f5b835df')
M.role(f'{G}::PAVING', 'NC.Hardscape')
M.role(f'{G}::GRAVEL', 'NC.Hardscape')
M.role(f'{G}::PLANTING', 'NC.Green')
M.role(f'{G}::PLANTER', 'NC.Green')
M.role(f'{G}::TERRACE LINES', 'NC.Furniture', mode='line')


def slope_axis(o):
    """Contour direction of a sloped site mesh (perpendicular to its steepest fall)."""
    v = np.array(o['mesh']['v'], float).reshape(-1, 3) / FT
    a, b, _ = np.linalg.lstsq(np.c_[v[:, :2], np.ones(len(v))], v[:, 2], rcond=None)[0]
    n = math.hypot(a, b) or 1
    return (-b / n, a / n)


def cross_frame(g, sp, near=None):
    """f(x, y) image px → model: y along the spine (window [ya, yb]), x across the area between its two edges."""
    N = 80
    pts = [sp.interpolate(k / N, normalized=True) for k in range(N + 1)]
    secs = []
    for k in range(N + 1):
        a, b = pts[max(0, k - 1)], pts[min(N, k + 1)]
        tx, ty = b.x - a.x, b.y - a.y; m = math.hypot(tx, ty) or 1
        nx, ny = -ty / m, tx / m
        p = pts[k]
        cut = LineString([(p.x - nx * 900, p.y - ny * 900), (p.x + nx * 900, p.y + ny * 900)]).intersection(g)
        segs = [l for l in lines_of(cut) if l.distance(p) < 0.5] or lines_of(cut)
        if not segs: secs.append(None); continue
        s = min(segs, key=lambda l: l.distance(p))
        c0, c1 = s.coords[0], s.coords[-1]
        if (c1[0] - c0[0]) * nx + (c1[1] - c0[1]) * ny < 0: c0, c1 = c1, c0
        secs.append((c0, c1))
    idx = [i for i, s in enumerate(secs) if s]
    for i in range(len(secs)):
        if secs[i] is None: secs[i] = secs[min(idx, key=lambda j: abs(j - i))]
    # smooth the two edges
    def sm(vals): return [tuple(np.mean(vals[max(0, i - 2):i + 3], axis=0)) for i in range(len(vals))]
    L0, L1 = sm([s[0] for s in secs]), sm([s[1] for s in secs])
    flip = False
    if near is not None and not near.is_empty:      # x = 60 (the "water" side) faces away from the building
        flip = Point(L0[N // 2]).distance(near) < Point(L1[N // 2]).distance(near)

    def make(ya, yb, xa, xb):
        def f(x, y):
            t = min(1.0, max(0.0, (y - ya) / (yb - ya))) * N
            i = min(N - 1, int(t)); w = t - i
            u = min(1.0, max(0.0, (x - xa) / (xb - xa)))
            if flip: u = 1 - u
            a0 = (L0[i][0] * (1 - w) + L0[i + 1][0] * w, L0[i][1] * (1 - w) + L0[i + 1][1] * w)
            a1 = (L1[i][0] * (1 - w) + L1[i + 1][0] * w, L1[i][1] * (1 - w) + L1[i + 1][1] * w)
            return (a0[0] + (a1[0] - a0[0]) * u, a0[1] + (a1[1] - a0[1]) * u)
        return f
    return make


def ref1_design(g, seed, near, terrace):
    """Reference 1 laid into g: a window of the reference at about park scale (0.37 ft / px along)."""
    sp = spine(g)
    if sp is None: return None
    W = width_of(g, sp)
    rr = random.Random(seed)
    span = min(1310.0, max(260.0, sp.length / 0.4))
    ya = ref1.Y0 + rr.uniform(0, 1310 - span); yb = ya + span
    xa, xb = (ref1.X_PLAZA, ref1.X_TRACK0) if W > 45 else (290.0, ref1.X_TRACK0)
    f = cross_frame(g, sp, near)(ya, yb, xa, xb)
    win = box(xa, ya, xb, yb)
    out = {}
    for k, s in REF1.items():
        if k in ('TRACK', 'EDGE PLANTING'): continue
        m = ref1.map_geom(s.intersection(win), f)
        out[k] = unary_union([p for p in parts(m.intersection(g)) if p.area > 30])
    out['MOUNDS'] = [ref1.map_geom(l.intersection(win), f) for l in MOUNDS1 if l.intersects(win)]
    out['TREES'] = [f(x, y) for x, y in ref1.TREE_SQUARES if ya <= y <= yb and xa <= x <= xb]
    out['BENCHES'] = [LineString([f(*a), f(*b)]) for a, b in ref1.BENCHES if win.contains(Point(a)) and win.contains(Point(b))]
    # fillet everything a little in model space
    for k in ('PLAZA', 'GRAVEL', 'DECK', 'LAWN', 'SHRUB'):
        out[k] = rounded(out[k], 1.5) if not out[k].is_empty else out[k]
    return out


RAMPED = ('c4485c57', '1f02ebc1')      # the south slope: ramps and stairs along the fall line (user markup 10-21)


def fall_frame(o, g):
    """Uphill unit vector and contour unit vector of a sloped mesh, and a local → world mapper about the centroid."""
    v = np.array(o['mesh']['v'], float).reshape(-1, 3) / FT
    a, b, _ = np.linalg.lstsq(np.c_[v[:, :2], np.ones(len(v))], v[:, 2], rcond=None)[0]
    n = math.hypot(a, b) or 1
    up = (a / n, b / n); ct = (-up[1], up[0])
    c = g.centroid
    to_w = lambda u, w: (c.x + ct[0] * u + up[0] * w, c.y + ct[1] * u + up[1] * w)
    us = [(x - c.x) * ct[0] + (y - c.y) * ct[1] for x, y in g.exterior.coords]
    ws = [(x - c.x) * up[0] + (y - c.y) * up[1] for x, y in g.exterior.coords]
    return to_w, (min(us), max(us)), (min(ws), max(ws))


def ramps(o, g, seed):
    """
    Strips along the fall line, every strip boundary the same polyline — straight runs with two angled jogs (the
    markup's red lines) filleted — offset across the slope: ramps (10 ft, paved) alternating with planting strips;
    one ramp is a stair. Returns paths, stairs, the strip lines, symbols (tread lines, ramp arrows).
    """
    to_w, (u0, u1), (w0, w1) = fall_frame(o, g)
    H = w1 - w0
    J, D = 14.0, 26.0                                   # jog across, over this much of the run
    wa, wb = w0 + 0.30 * H, w0 + 0.62 * H
    guide_local = [(0, w0 - 10), (0, wa), (J, wa + D), (J, wb), (0, wb + D), (0, w1 + 10)]
    RW, PW = 10.0, 14.0                                 # ramp width, planting width
    pitch = RW + PW
    n = max(1, int((u1 - u0 - J) // pitch))
    start = u0 + (u1 - u0 - J - (n * pitch - PW)) / 2
    import ref2
    strips, lines = [], []
    for k in range(n):
        a = start + k * pitch
        for uu in (a, a + RW):
            loc = ref2.fillet_polyline([(x + uu, w) for x, w in guide_local], 9.0)
            lines.append(LineString([to_w(x, w) for x, w in loc.coords]).intersection(g.buffer(-0.5)))
        la = ref2.fillet_polyline([(x + a, w) for x, w in guide_local], 9.0)
        lb = ref2.fillet_polyline([(x + a + RW, w) for x, w in guide_local], 9.0)
        poly = Polygon([to_w(x, w) for x, w in la.coords] + [to_w(x, w) for x, w in reversed(lb.coords)]).buffer(0)
        strips.append((poly.intersection(g), la, a))
    stair_k = n // 2
    stairs = Polygon(); symbols = []
    for k, (poly, la, a) in enumerate(strips):
        mid_u = a + RW / 2
        if k == stair_k:
            # stair on the lower straight run: treads every 1.5 ft across the strip, an arrow up the middle
            s0, s1 = w0 + 0.08 * H, wa - 4
            st = Polygon([to_w(a, s0), to_w(a + RW, s0), to_w(a + RW, s1), to_w(a, s1)]).intersection(g)
            stairs = st
            w = s0
            while w <= s1:
                symbols.append(('STAIR', LineString([to_w(a, w), to_w(a + RW, w)]).intersection(st.buffer(0.01))))
                w += 1.5
            symbols.append(('STAIR', LineString([to_w(mid_u, s0 + 1), to_w(mid_u, s1 - 1)])))
            symbols.append(('STAIR', LineString([to_w(mid_u - 2, s1 - 4), to_w(mid_u, s1 - 1), to_w(mid_u + 2, s1 - 4)])))
        else:
            # ramp arrows on the upper straight run: a line up the middle with a chevron at the top
            s0, s1 = wa + D + 6, wb - 4
            x = mid_u + J
            symbols.append(('RAMP', LineString([to_w(x, s0), to_w(x, s1)])))
            symbols.append(('RAMP', LineString([to_w(x - 2.5, s1 - 4), to_w(x, s1), to_w(x + 2.5, s1 - 4)])))
    paths = unary_union([p for p, _, _ in strips])
    return paths, stairs, [l for x in lines for l in lines_of(x) if l.length > 4], [(t, l) for t, l in symbols if not l.is_empty]


summary = []
# User markup 10-21 (plan of the plugin run): the strip west of the parking structure is LOADING; the strips along the
# west edge and the drop-off island are hardscape. Their greens become paving (PROJECT SITE::LOADING / PLAZA).
TO_LOADING = ('1f02ebc1',)
TO_PAVING = ('a0dd3665', '3f41822b', '0faf9be8', '67c09ee7')
M.role('PROJECT SITE::LOADING', 'NC.Hardscape')
M.role('PROJECT SITE::PLAZA', 'NC.Hardscape')
for o in [o for o in M.fx if o['layer'] == 'greens' and o['id'][:8] in TO_LOADING + TO_PAVING and o.get('mesh')]:
    oid = o['id']; g = FP[oid]
    if g is None: continue
    loading = oid[:8] in TO_LOADING
    path, nm = ('PROJECT SITE::LOADING', 'LOADING') if loading else ('PROJECT SITE::PLAZA', '')
    if o['type'] == 'Brep' and Z[oid][1] - Z[oid][0] > 4:
        mesh_part(o, g.buffer(0.01), path, nm)
    else:
        M.add_area(g, path, nm, Z[oid][0])
    M.retire(oid)
    for c in twin_curves(oid): M.retire(c)
    summary.append(f"greens {oid[:8]} → {'LOADING' if loading else 'hardscape'} ({g.area:,.0f} sf)")
greens = [o for o in M.fx if o['layer'] == 'greens' and o['name'] in ('GARDEN', 'TERRACE', 'GREEN ROOF') and o.get('mesh')
          and o['id'][:8] not in TO_LOADING + TO_PAVING]
for gi, o in enumerate(greens):
    oid, name = o['id'], o['name']
    g = FP[oid]
    if g is None: continue
    g = max(parts(g), key=lambda p: p.area)
    z0, z1 = Z[oid]
    sloped = o['type'] == 'Brep' and z1 - z0 > 4
    zf = z_on(o) if sloped else (lambda x, y, _z=z1: _z)
    (_, _), short = long_axis(g)
    zt = z1 + 1.5 if name == 'TERRACE' else (z1 if not sloped else 0.0)
    if g.area < 1500 or short < 16:
        ts = trees_in(g, 26, z1 if not sloped else z0, cap=int(g.area / 600) + 1)
        summary.append(f"{name} {oid[:8]}: small — {len(ts)} trees")
        continue
    if oid[:8] in RAMPED:
        # ---- the slope: ramps and a stair along the fall line, planting strips between (user markup 10-21)
        paths, stairs, lines, symbols = ramps(o, g, gi)
        lawn = g.difference(paths.buffer(0.01))
        mesh_part(o, lawn, 'greens', name)
        mesh_part(o, paths.difference(stairs.buffer(0.01)), f'{G}::PATH', 'RAMP')
        if not stairs.is_empty: mesh_part(o, stairs, f'{G}::PAVING', 'STAIR')
        for l in lines:
            q = l.interpolate(0.5, normalized=True); M.add_line(l, zf(q.x, q.y), f'{G}::TERRACE LINES', 'RAMP EDGE')
        for typ, l in symbols:
            for x in lines_of(l):
                q = x.interpolate(0.5, normalized=True); M.add_line(x, zf(q.x, q.y) + 0.05, f'{G}::FURNITURE', typ)
        planting = lawn.buffer(-4)
        nt = len(trees_in(planting, 26, 0.0, (6.5, 8.5), cap=int(lawn.area / 900) + 1))
        npl = people(paths, max(2, int(paths.area / 500)), 0.0)
        summary.append(f"{name} {oid[:8]} (slope) — ramps: {len(parts(paths))} ramps / stair strips ({paths.area:,.0f} sf), stair {stairs.area:,.0f} sf, "
                       f"{len(lines)} strip lines, {nt} trees, {npl} people")
    elif o['bbox'][2] / FT < 3 and oid[:8] not in SOUTH:
        # ---- the north gardens on the ground stay whole: the main planting, which zones the paving round them
        # (user markup 10-21). Only trees and a mound contour are added; the green itself is not touched.
        inner_line = g.buffer(-8, join_style=1)
        for p in parts(inner_line):
            M.add_line(LineString(p.exterior.coords), z1 + 0.05, f'{G}::FURNITURE', 'LAWN MOUND')
        nt = len(trees_in(g, 24, z1, (7, 9.5), cap=int(g.area / 700) + 1))
        summary.append(f"{name} {oid[:8]} @{z0:.0f}' — kept whole: mound line, {nt} trees")
        continue
    elif oid[:8] in SOUTH:
        # ---- reference 2: lines turning on fillets, straight paths, planters
        (ax, _), _s = long_axis(g)
        axis = slope_axis(o) if sloped and short > 60 else long_axis(g)[0]
        r2 = ref2.fan(g, axis, gi)
        hard = r2['PATHS']; planters = r2['PLANTERS']
        lawn = g.difference(unary_union([hard, planters]).buffer(0.01))
        if sloped:
            mesh_part(o, lawn, 'greens', name)
            mesh_part(o, hard, f'{G}::PATH', 'GARDEN WALK')
            mesh_part(o, planters, f'{G}::PLANTER', '')
        else:
            M.add_solid(lawn, z0, z1, 'greens', name)
            M.add_area(hard, f'{G}::PATH', 'GARDEN WALK' if name != 'GREEN ROOF' else 'ROOF WALK', z1)
            M.add_solid(planters, z1, z1 + 1.5, f'{G}::PLANTER', '')
        for l in r2['LINES']:
            q = l.interpolate(0.5, normalized=True)
            M.add_line(l, zf(q.x, q.y) if sloped else z1, f'{G}::TERRACE LINES', 'TERRACE STEP')
        # trees beside the paths (palms in the reference), people on the paths and the lawn strips
        nt = 0 if name == 'GREEN ROOF' else len(trees_in(lawn.intersection(hard.buffer(10)).difference(hard.buffer(4)), 30, zt, (6, 8),
                                                        cap=int(hard.area / 250) + 1))
        npl = people(hard, max(2, int(hard.area / 400)), z1 if not sloped else 0.0) + people(lawn, max(2, int(lawn.area / 2500)), z1 if not sloped else 0.0)
        summary.append(f"{name} {oid[:8]}{' (slope)' if sloped else ''} — ref 2: {len(r2['LINES'])} terrace lines, paths {hard.area:,.0f} sf, "
                       f"{len(parts(planters))} planters, {nt} trees, {npl} people")
    else:
        # ---- reference 1: plaza / gravel / timber deck / walk / lawn mounds / planting beds
        near = walls_at(z0 + 0.5)
        r1 = ref1_design(g, gi, near, name == 'TERRACE')
        if r1 is None:
            summary.append(f"{name} {oid[:8]}: no spine — left as it is"); continue
        soft = unary_union([r1['LAWN'], r1['SHRUB']])
        hard_parts = {'PLAZA': (f'{G}::PAVING', ''), 'GRAVEL': (f'{G}::GRAVEL', ''), 'DECK': (f'{G}::DECK', ''), 'WALK': (f'{G}::PATH', 'GARDEN WALK')}
        if name == 'TERRACE':
            for k, (lay, nm) in hard_parts.items():
                M.add_solid(r1[k], z0, z1, lay, 'TERRACE' if k == 'PLAZA' else nm)
            M.add_solid(r1['LAWN'], z0, z1 + 1.5, 'greens', '')
            M.add_solid(r1['SHRUB'], z0, z1 + 1.5, f'{G}::PLANTING', '')
        else:
            M.add_solid(r1['LAWN'], z0, z1, 'greens', name)
            M.add_solid(r1['SHRUB'], z0, z1, f'{G}::PLANTING', '')
            for k, (lay, nm) in hard_parts.items():
                M.add_area(r1[k], lay, nm, z1)
        rest = g.difference(unary_union([soft] + [r1[k] for k in hard_parts]).buffer(0.05))
        if rest.area > 20:      # what the reference leaves between its pieces: gravel in a garden, paving on a terrace
            if name == 'TERRACE': M.add_solid(rest, z0, z1, f'{G}::PAVING', '')
            else: M.add_area(rest, f'{G}::GRAVEL', '', z1)
        for l in r1['MOUNDS']:
            for x in lines_of(l.intersection(g.buffer(-1))):
                if x.length > 6: M.add_line(x, zt, f'{G}::FURNITURE', 'LAWN MOUND')
        for b in r1['BENCHES']:
            if g.buffer(-1).contains(b): M.add_line(b, z1, f'{G}::FURNITURE', 'BENCH')
        for x, y in r1['TREES']:
            if g.buffer(-3).contains(Point(x, y)):
                M.add_line(LineString(box(x - 2, y - 2, x + 2, y + 2).exterior.coords), z1, f'{G}::FURNITURE', 'TREE GRATE')
                circle_curve((x, y), rng.uniform(7, 9), zt, f'{G}::TREES')
        ts = trees_in(unary_union([r1['LAWN'], r1['SHRUB']]), 30, zt, (6, 8.5) if name == 'TERRACE' else (7, 9.5), cap=int(g.area / 1500) + 1)
        npl = people(unary_union([r1['PLAZA'], r1['DECK'], r1['WALK']]), max(2, int(g.area / 1200)), z1)
        summary.append(f"{name} {oid[:8]} @{z0:.0f}' — ref 1: " + ", ".join(f"{k.lower()} {r1[k].area:,.0f}" for k in ('PLAZA', 'GRAVEL', 'DECK', 'WALK', 'LAWN', 'SHRUB'))
                       + f" sf, {len(ts) + len(r1['TREES'])} trees, {npl} people")
    M.retire(oid)
    for t in twin_curves(oid): M.retire(t)
M.log += summary

# ------------------------------------------------------------------ 4. previous site paving out (laid out again later)
old = [o['id'] for o in M.fx if o['layer'] == 'PROJECT SITE::PLAZA']
for i in old: M.retire(i)
M.log.append(f"previous PROJECT SITE::PLAZA paving: {len(old)} pieces → LEFTOVERS")
M.write(sys.argv[3])
print("\n".join(M.log))
