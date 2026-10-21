"""RIVER 2026-10-21 DRAFT_1 → spiral stairs, auditorium furniture, gardens and terraces designed (step 1 of 3).

Usage: python3 -I d1_site.py <fixture.json of in.3dm> <in.3dm> <out.3dm>
- Every round double-height opening (a circular hole in a floor plate) gets a small spiral stair on PROGRAM::Stair,
  from the floor below the hole to the floor with the hole, standing against the hole's edge.
- Auditorium: seats in curved rows with aisles, stage and screen (2D curves on FURNITURE).
- Every named area on `greens` (GARDEN / TERRACE / GREEN ROOF) is designed inside its own outline: gardens get a
  family of long parallel bands with filleted ends (seating steps, a walk, planting between them, after the user's
  reference of stepped riverside seating); terraces the same with deck bands and raised planters; trees and people.
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


# Scale after the Vancouver WA / Seattle waterfronts: 10 ft walks, 8–9 ft seating steps / decks, 8–10 ft planting
# strips between them with a row of trees; benches long and linear along the planting.
SPEC = {'GARDEN': (14.0, 7.0), 'TERRACE': (15.0, 9.0), 'GREEN ROOF': (26.0, 8.0)}


def band_family(g, kind, seed, axis=None, near=None):
    """
    Bands that converge (user 10-21: ends not round — they gather into one point or stop on one straight line):
    every band follows the spine at offset o·e(t); e rises from 0 at the gathering point to 1 and holds, so all bands
    meet in a point at that end and stop together on one straight line across the spine at the other. The bands keep
    their fillet curvature from the spine. The tapered tip region is a dark paving inlay.
    Returns [(geometry, centre line, type)] with type STEPS / WALK / DECK / INLAY, the spine, and tree-row lines.
    """
    sp = spine(g, axis)
    if sp is None: return [], None, []
    if near is not None and not near.is_empty and Point(sp.coords[-1]).distance(near) < Point(sp.coords[0]).distance(near):
        sp = LineString(list(sp.coords)[::-1])          # the gathering point at the end nearer the building / entry
    W = width_of(g, sp)
    pitch, bw = SPEC.get(kind, SPEC['GARDEN'])
    n = max(1, int((W - 6) // pitch))
    if kind == 'TERRACE':
        pitch = (W - 4) / n; bw = max(8.0, 0.62 * pitch)
    H = (n - 1) / 2 * pitch + bw / 2 + 2
    N = 120
    pts = [sp.interpolate(k / N, normalized=True) for k in range(N + 1)]
    nrm = []
    for k in range(N + 1):
        a, b = pts[max(0, k - 1)], pts[min(N, k + 1)]
        tx, ty = b.x - a.x, b.y - a.y; m = math.hypot(tx, ty) or 1
        nrm.append((-ty / m, tx / m))
    edge = g.exterior
    raw = []
    for k in range(N + 1):
        t = k / N
        shape = math.sin(math.pi / 2 * min(1.0, t / 0.42)) ** 0.8
        room = max(0.0, (edge.distance(pts[k]) - 2.0) / H)
        raw.append(min(shape, room, 1.0))
    e = [float(np.mean(raw[max(0, k - 3):k + 4])) if k else 0.0 for k in range(N + 1)]
    e[0] = 0.0
    k_end = N - 2                                           # the common straight end line

    def curve(o):
        return [(pts[k].x + nrm[k][0] * o * e[k], pts[k].y + nrm[k][1] * o * e[k]) for k in range(k_end + 1)]

    def ribbon(o0, o1):
        return Polygon(curve(o0) + curve(o1)[::-1]).buffer(0)

    offs = [(-(n - 1) / 2 + i) * pitch for i in range(n)]
    walk_i = n // 2 if kind != 'TERRACE' else -1
    out, gaps = [], []
    for i, o in enumerate(offs):
        typ = 'WALK' if i == walk_i else ('DECK' if kind == 'TERRACE' else 'STEPS')
        w = (9.0 if kind == 'GARDEN' else 8.0) if typ == 'WALK' else bw
        b = ribbon(o - w / 2, o + w / 2).intersection(g.buffer(-1.0))
        b = unary_union([q for q in parts(b) if q.area > 40])
        if not b.is_empty: out.append((b, LineString(curve(o)), typ))
        if i + 1 < n: gaps.append(LineString(curve(o + pitch / 2)))
    # The gathering tip: dark paving where the bands converge (the first stretch of the envelope).
    tip_k = int(N * 0.2)
    tip = Polygon([(pts[k].x + nrm[k][0] * H * e[k], pts[k].y + nrm[k][1] * H * e[k]) for k in range(tip_k + 1)]
                  + [(pts[k].x - nrm[k][0] * H * e[k], pts[k].y - nrm[k][1] * H * e[k]) for k in range(tip_k, -1, -1)]).buffer(0)
    tip = tip.intersection(g.buffer(-1.0)).difference(unary_union([b for b, _, _ in out]))
    tip = unary_union([q for q in parts(tip) if q.area > 30])
    if not tip.is_empty and kind != 'GREEN ROOF': out.append((tip, LineString(curve(0)[:tip_k + 1]), 'INLAY'))
    return out, sp, gaps


def steps_and_benches(b, seg, typ, z):
    """Seating steps read as tiers (two lines along the band); decks and walks get long linear benches."""
    if typ == 'INLAY':
        return
    if typ == 'STEPS':
        for f in (-1.4, 1.4):
            for l in lines_of(seg.offset_curve(f, join_style=1).intersection(b.buffer(-0.6))):
                if l.length > 6: M.add_line(l, z, f'{G}::FURNITURE', 'SEATING STEP')
        return
    edge = seg.offset_curve((3.2 if typ == 'DECK' else 3.8), join_style=1)
    for l in lines_of(edge.intersection(b.buffer(-0.4))):
        s = 4.0
        while s + 14 < l.length:
            M.add_line(substring(l, s, s + 14), z, f'{G}::FURNITURE', 'BENCH')
            s += 34


def tree_rows(gaps, plant, z, spacing=30, radius=(7, 9.5)):
    pts = []
    ok = plant.buffer(-2.5)
    for k, l in enumerate(gaps):
        if k % 2: continue                       # every other planting strip carries trees
        s = spacing / 2
        while s < l.length:
            p = l.interpolate(s)
            if ok.contains(p) and all(p.distance(q) > spacing * 0.6 for q in pts): pts.append(p)
            s += spacing
    for p in pts: circle_curve((p.x, p.y), rng.uniform(*radius), z, f'{G}::TREES')
    return pts


def slope_axis(o):
    """Contour direction of a sloped site mesh (perpendicular to its steepest fall)."""
    v = np.array(o['mesh']['v'], float).reshape(-1, 3) / FT
    A = np.c_[v[:, :2], np.ones(len(v))]
    a, b, _ = np.linalg.lstsq(A, v[:, 2], rcond=None)[0]
    n = math.hypot(a, b) or 1
    return (-b / n, a / n)


summary = []
greens = [o for o in M.fx if o['layer'] == 'greens' and o['name'] in ('GARDEN', 'TERRACE', 'GREEN ROOF') and o.get('mesh')]
for gi, o in enumerate(greens):
    oid, name = o['id'], o['name']
    g = FP[oid]
    if g is None: continue
    g = max(parts(g), key=lambda p: p.area)
    z0, z1 = Z[oid]
    sloped = o['type'] == 'Brep' and z1 - z0 > 4
    zf = z_on(o) if sloped else (lambda x, y, _z=z1: _z)
    (_, _), short = long_axis(g)
    if g.area < 1500 or short < 16:
        ts = trees_in(g, 26, z1 if not sloped else z0, cap=int(g.area / 600) + 1)
        summary.append(f"{name} {oid[:8]}: small — {len(ts)} trees")
        continue
    fam, sp, gaps = band_family(g, name, gi, slope_axis(o) if sloped and short > 60 else None, near=walls_at(z0 + 0.5))
    if not fam:
        summary.append(f"{name} {oid[:8]}: no spine — left as it is"); continue
    hard = unary_union([b for b, _, _ in fam]).buffer(0.01)
    plant = g.difference(hard)
    lay = {'STEPS': (f'{G}::SEATING', 'SEATING STEPS'), 'WALK': (f'{G}::PATH', 'GARDEN WALK' if name != 'GREEN ROOF' else 'ROOF WALK'),
           'DECK': (f'{G}::DECK', 'TERRACE'), 'INLAY': (f'{G}::PAVING INLAY', '')}
    if sloped:
        mesh_part(o, plant, 'greens', name)
        for b, _, typ in fam: mesh_part(o, b, *lay[typ])
    elif name == 'TERRACE':
        # Terrace: deck bands at floor level, the planting between them raised as planters.
        for b, _, typ in fam: M.add_solid(b, z0, z1, *lay[typ])
        M.add_solid(plant, z0, z1 + 1.5, 'greens', '')
    else:
        M.add_solid(plant, z0, z1, 'greens', name)
        for b, _, typ in fam: M.add_area(b, *lay[typ], z1)
    for b, seg, typ in fam:
        q = seg.interpolate(0.5, normalized=True)
        steps_and_benches(b, seg, typ, zf(q.x, q.y) if sloped else z1)
    zt = z1 + 1.5 if name == 'TERRACE' else (z1 if not sloped else 0.0)
    ts = [] if name == 'GREEN ROOF' else tree_rows(gaps, plant, zt, 28 if name == 'TERRACE' else 30, (6, 8) if name == 'TERRACE' else (7, 9.5))
    if name != 'GREEN ROOF' and not ts:
        wide = unary_union([q for q in parts(plant.buffer(-3.5).buffer(3.5)) if q.area > 150])
        ts = trees_in(wide, 28, zt, (6, 8) if name == 'TERRACE' else (7, 9.5), cap=int(g.area / 1200) + 1)
    npl = 0
    for b, seg, typ in fam:
        q = b.representative_point()
        npl += people(b, max(1, int(b.area / 500)), zf(q.x, q.y))
    counts = {t: sum(1 for _, _, x in fam if x == t) for t in ('STEPS', 'WALK', 'DECK', 'INLAY')}
    summary.append(f"{name} {oid[:8]}{' (slope)' if sloped else ''} @{z0:.0f}': {', '.join(f'{v} {k.lower()}' for k, v in counts.items() if v)} bands "
                   f"({hard.area:,.0f} sf), planting {plant.area:,.0f} sf, {len(ts)} trees, {npl} people")
    M.retire(oid)
    for t in twin_curves(oid): M.retire(t)
M.log += summary

# ------------------------------------------------------------------ 4. previous site paving out (laid out again later)
old = [o['id'] for o in M.fx if o['layer'] == 'PROJECT SITE::PLAZA']
for i in old: M.retire(i)
M.log.append(f"previous PROJECT SITE::PLAZA paving: {len(old)} pieces → LEFTOVERS")
M.write(sys.argv[3])
print("\n".join(M.log))
