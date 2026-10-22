"""RIVER 2026-10-22 markup (layer 000-WORKINGLINE) → model edits. Nothing else in the user's model is changed.

Usage: python3 -I d2_edits.py <fixture.json of in.3dm> <in.3dm> <out.3dm>
- Polylines: walks as wide as the riverside walkways next door (12 ft), corners filleted, timber paving; both sides a
  planting strip with trees, a water rill along the long runs, benches (user reference: a boardwalk through a garden).
- Circles: round outdoor picnic areas (user reference: sunken round courts with curved timber benches and tables in
  planting) at the level the circle lies on — ground, the L2 / L3 / L4 terraces or the parking roof. The large circle
  round the river pavilion is a picnic grove of several such courts round the pavilion and the amphitheatre.
- Site areas under the new pieces are cut (no overlaps); trees under walks / courts → LEFTOVERS. The building (rooms
  and the glass envelope, grown by 3 ft), the roads, the river, the pavilion and the amphitheatre are never entered.
"""
import sys, math, random
sys.path.insert(0, __file__.rsplit('/', 1)[0])
from r21_lib import *
from r20_envelope import envelope
from shapely.ops import nearest_points, substring
import ref2

M = Model(sys.argv[1], sys.argv[2])
rng = random.Random(1022)
FP = {o['id']: footprint(o) for o in M.fx if o.get('mesh')}
Z = {o['id']: (o['bbox'][2] / FT, o['bbox'][5] / FT) for o in M.fx}


def fp_of(o):
    if o.get('mesh'): return FP.get(o['id'])
    pts = o.get('points')
    if not pts or len(pts) < 3: return None
    xy = [(x / FT, y / FT) for x, y, _ in pts]
    return Polygon(xy).buffer(0) if o['isClosed'] else LineString(xy)


W_ = 'PROJECT SITE::WALKWAYS'
C_ = 'PROJECT SITE::PICNIC'
for path, role, mode in [(f'{W_}::PATH', 'NC.Hardscape', 'color'), (f'{W_}::PLANTING', 'NC.Green', 'color'), (f'{W_}::RILL', 'NC.Water', 'color'),
                         (f'{W_}::TREES', 'NC.Tree', 'color'), (f'{W_}::FURNITURE', 'NC.Furniture', 'line'),
                         (f'{C_}::COURT', 'NC.Hardscape', 'color'), (f'{C_}::BENCH', 'NC.Hardscape', 'color'), (f'{C_}::PLANTING', 'NC.Green', 'color'),
                         (f'{C_}::TREES', 'NC.Tree', 'color'), (f'{C_}::FURNITURE', 'NC.Furniture', 'line'), (f'{C_}::PEOPLE', 'Entourage', 'color')]:
    M.role(path, role, mode=mode)
M.role('000-WORKINGLINE', 'Ignore', mode='')


def circle(c, r_, z, path, name=""):
    return str(M.m.Objects.AddCircle(r.Circle(r.Point3d(c[0], c[1], z), r_), M.attrs(path, name)))


# ------------------------------------------------------------------ what may not be entered
def lay(o): return o['layer']
masses0 = [FP[o['id']] for o in M.fx if lay(o).startswith('PROGRAM::') and lay(o) not in ('PROGRAM::Glass', 'PROGRAM::Stair')
           and o.get('mesh') and FP.get(o['id']) is not None and Z[o['id']][0] < 1 and Z[o['id']][1] > 3]
INDOOR0 = unary_union([envelope(M.fx, 0, 18)[0]] + masses0).buffer(3)
ROADS = unary_union([g for o in M.fx if lay(o) in ('ROAD', 'SITE CONTEXT::STREET', 'SITE CONTEXT::SIDEWALK', 'SITE CONTEXT::CITY BLOCKS')
                     and Z[o['id']][1] < 3 for g in [fp_of(o)] if g is not None and g.geom_type in ('Polygon', 'MultiPolygon')])
RIVER = unary_union([fp_of(o) for o in M.fx if lay(o) == 'SITE CONTEXT::RIVER'])
PAV = next(FP[o['id']] for o in M.fx if o['name'] == 'RIVER PAVILION' and lay(o).startswith('PROGRAM::'))
AMPH = unary_union([g for o in M.fx if lay(o).endswith(('::SEATING STEPS', '::STAGE')) and 'RIVERFRONT' in lay(o)
                    for g in [fp_of(o)] if g is not None and g.geom_type == 'Polygon'])
PROTECT0 = unary_union([INDOOR0, RIVER, PAV.buffer(3), AMPH.buffer(1.5)])

# ------------------------------------------------------------------ markup
mk = [o for o in M.fx if lay(o) == '000-WORKINGLINE']
lines = {o['id'][:8]: [(x / FT, y / FT) for x, y, _ in o['points']] for o in mk if o['type'] != 'ArcCurve'}
circles = []
for o in mk:
    if o['type'] != 'ArcCurve': continue
    xs = [x / FT for x, _, _ in o['points']]; ys = [y / FT for _, y, _ in o['points']]
    circles.append((o['id'][:8], ((max(xs) + min(xs)) / 2, (max(ys) + min(ys)) / 2), (max(xs) - min(xs)) / 2))
# one route per chain (the street link and the west run are one walk; the branch joins it)
west = [lines['3a2c4f86'][1]] + lines['78a129f9']                   # street (40.8, -67.6) → … → pavilion (356.4, 463.2)
east = lines['e5f55a36']
east = [(x + (3.0 if x > 655 else 0.0), y) for x, y in east]        # clear of the service drive (3 ft east)
routes = [west, lines['895c7ca2'], lines['25ac2b15'], east, lines['7da764e5']]
WALK = 12.0
fil = [ref2.fillet_polyline(r_, 22.0) if len(r_) > 2 else LineString(r_) for r_ in routes]
walk = unary_union([l.buffer(WALK / 2, cap_style=2, join_style=1) for l in fil]).buffer(1, join_style=1).buffer(-1, join_style=1)
walk = walk.difference(PROTECT0).difference(ROADS.buffer(0.5))
walk = unary_union([p for p in parts(walk) if p.area > 40])
# both sides: planting strips 7 ft, a rill along the long straight runs, trees, benches
side = unary_union([l.buffer(WALK / 2 + 7, cap_style=2, join_style=1) for l in fil]).difference(walk.buffer(0.05))
side = side.difference(PROTECT0).difference(ROADS.buffer(0.5))
side = unary_union([p for p in parts(side) if p.area > 60])
rill = Polygon()
for l in fil[:1] + fil[3:4]:
    for k in range(len(l.coords) - 1):
        a, b = l.coords[k], l.coords[k + 1]
        if math.dist(a, b) < 70: continue
        seg = LineString([a, b])
        off = seg.parallel_offset(WALK / 2 + 3.2, 'left')
        if not off.is_empty: rill = rill.union(off.buffer(1.25, cap_style=2))
rill = rill.intersection(side.buffer(-0.5))
side = side.difference(rill.buffer(0.3))
CUT0 = unary_union([walk, side, rill])

# ------------------------------------------------------------------ cut what lies under new ground pieces
def cut_under(cutter, zlo, zhi, protect_ids=()):
    n = 0
    for o in list(M.fx):
        L = lay(o)
        if o['id'] in protect_ids or o['id'] not in M.obj: continue
        site = L == 'greens' or (L.startswith('PROJECT SITE') and not any(k in L for k in ('TREES', 'PEOPLE', 'FURNITURE', 'PAVILION', 'WALKWAYS', 'PICNIC')))
        if not site or L.endswith(('::SEATING STEPS', '::STAGE')): continue
        z0, z1 = Z[o['id']]
        if z1 < zlo - 0.5 or z0 > zhi + 0.5: continue
        g = fp_of(o)
        if g is None or g.geom_type not in ('Polygon', 'MultiPolygon') or not g.intersects(cutter): continue
        if g.intersection(cutter).area < 1: continue
        rest = g.difference(cutter.buffer(0.05)).buffer(-0.05).buffer(0.05)
        if o.get('mesh') and z1 - z0 > 3.5 and o['type'] == 'Brep':
            M.clip_mesh(o['id'], box(-1e4, -1e4, 1e4, 1e4).difference(cutter.buffer(0.05)))
        elif o.get('mesh'):
            M.replace_solid(o['id'], rest)
        else:
            M.add_area(rest, L, o['name'], z0)
            M.retire(o['id'])
        n += 1
    for o in M.fx:
        if ('TREES' in lay(o)) and o['id'] in M.obj and Z[o['id']][0] >= zlo - 1 and Z[o['id']][0] <= zhi + 1:
            g = fp_of(o)
            if g is not None and cutter.buffer(-2).contains(g.centroid): M.retire(o['id']); n += 1
    return n


n_cut = cut_under(CUT0, -4, 3)
M.add_area(walk, f'{W_}::PATH', 'TIMBER WALK', 0.05)
M.add_area(side, f'{W_}::PLANTING', 'PLANTING', 0.05)
M.add_area(rill, f'{W_}::RILL', 'RILL', 0.05)
trees, nb = [], 0
for l in fil:
    s, sgn = 10.0, 1
    while s < l.length - 4:
        p = l.interpolate(s); q = l.interpolate(min(l.length, s + 1))
        tx, ty = q.x - p.x, q.y - p.y; nn = math.hypot(tx, ty) or 1
        c = Point(p.x - ty / nn * sgn * (WALK / 2 + 3.5), p.y + tx / nn * sgn * (WALK / 2 + 3.5))
        if side.buffer(-1).contains(c) and all(c.distance(t) > 16 for t in trees):
            trees.append(c); circle((c.x, c.y), rng.uniform(6.5, 9), 0.05, f'{W_}::TREES')
        # a bench on the walk's edge between trees, every other step
        if sgn > 0 and s + 8 < l.length:
            seg = substring(l, s + 6, s + 14).parallel_offset(WALK / 2 - 1.0, 'right')
            if not seg.is_empty and walk.buffer(-0.2).contains(seg):
                M.add_line(seg, 0.1, f'{W_}::FURNITURE', 'BENCH'); nb += 1
        s += 22; sgn = -sgn
M.log.append(f"walks: {len(routes)} markup lines → 12 ft filleted timber walk {walk.area:,.0f} sf, planting strips {side.area:,.0f} sf, "
             f"rill {rill.area:,.0f} sf, {len(trees)} trees, {nb} benches; {n_cut} site objects cut / trees moved")

# ------------------------------------------------------------------ picnic courts
levels = sorted({l['elevation'] for l in M.st['levels']})


def surface_z(c):
    """Top of the highest site surface or floor plate under c with nothing built above it."""
    p = Point(c)
    cands = []
    for o in M.fx:
        L = lay(o)
        if not (L == 'greens' or L.startswith('PROJECT SITE') or L == 'FLOOR PLATE'): continue
        if any(k in L for k in ('TREES', 'PEOPLE', 'FURNITURE')): continue
        g = fp_of(o)
        if g is None or g.geom_type not in ('Polygon', 'MultiPolygon') or not g.contains(p): continue
        cands.append(Z[o['id']][1])
    for zc in sorted(set(round(z, 1) for z in cands), reverse=True):
        above = [o for o in M.fx if lay(o).startswith('PROGRAM::') and lay(o) != 'PROGRAM::Glass' and FP.get(o['id']) is not None
                 and Z[o['id']][0] >= zc - 1.0 and FP[o['id']].contains(p)]
        if not above: return zc
    return 0.0


def walls_at(z):
    return unary_union([FP[o['id']] for o in M.fx if lay(o).startswith('PROGRAM::') and lay(o) not in ('PROGRAM::Glass', 'PROGRAM::Stair')
                        and FP.get(o['id']) is not None and Z[o['id']][0] <= z + 0.5 and Z[o['id']][1] >= z + 2])


def court(c, R, z, toward, keep_out, label):
    """Sunken round court: pavers, a C-shaped timber bench open towards `toward`, tables, planting ring with trees."""
    ang = math.degrees(math.atan2(toward[1] - c[1], toward[0] - c[0]))
    pad = Point(c).buffer(R * 0.78, 64)
    def arc_band(r0, r1, a0, a1, n=48):
        o_ = [(c[0] + r1 * math.cos(math.radians(a0 + (a1 - a0) * k / n)), c[1] + r1 * math.sin(math.radians(a0 + (a1 - a0) * k / n))) for k in range(n + 1)]
        i_ = [(c[0] + r0 * math.cos(math.radians(a0 + (a1 - a0) * k / n)), c[1] + r0 * math.sin(math.radians(a0 + (a1 - a0) * k / n))) for k in range(n, -1, -1)]
        return Polygon(o_ + i_).buffer(0)
    bench = arc_band(R * 0.56, R * 0.56 + 1.6, ang + 70, ang + 290)
    gap = LineString([c, (c[0] + 2 * R * math.cos(math.radians(ang)), c[1] + 2 * R * math.sin(math.radians(ang)))]).buffer(max(2.5, R * 0.3), cap_style=2)
    ring = Point(c).buffer(R + 2.0, 64).difference(pad).difference(gap)
    pad = pad.difference(keep_out); bench = bench.difference(keep_out); ring = ring.difference(keep_out)
    court_area = pad.difference(bench.buffer(0.05))
    if z < 3:
        M.add_area(court_area, f'{C_}::COURT', label, z + 0.05)
        M.add_area(bench, f'{C_}::BENCH', '', z + 0.05)
        M.add_area(ring, f'{C_}::PLANTING', '', z + 0.05)
    else:
        M.add_solid(court_area, z - 0.5, z, f'{C_}::COURT', label)
        M.add_solid(bench, z - 0.5, z + 1.5, f'{C_}::BENCH', '')
        M.add_solid(ring, z - 0.5, z + 1.5, f'{C_}::PLANTING', '')
    nt = 2 if R < 8 else 3
    for k in range(nt):
        a = math.radians(ang + 120 + k * 120 / max(1, nt - 1)) if nt > 1 else math.radians(ang + 180)
        q = (c[0] + (R + 0.8) * math.cos(a), c[1] + (R + 0.8) * math.sin(a))
        if ring.buffer(0.5).contains(Point(q)): circle(q, rng.uniform(5, 7), z + 0.1, f'{C_}::TREES')
    tables = [(0.0, 0.0)] if R < 8 else [(-R * 0.18, R * 0.12), (R * 0.2, -R * 0.1)]
    for dx, dy in tables:
        t = (c[0] + dx, c[1] + dy)
        circle(t, 1.8, z + 0.1, f'{C_}::FURNITURE', 'TABLE')
        for k in range(4):
            a = math.radians(45 + 90 * k)
            circle((t[0] + 3.0 * math.cos(a), t[1] + 3.0 * math.sin(a)), 0.7, z + 0.1, f'{C_}::FURNITURE', 'STOOL')
    for k in range(rng.randint(2, 4)):
        a = rng.uniform(0, 2 * math.pi); rr = rng.uniform(1.5, R * 0.45)
        circle((c[0] + rr * math.cos(a), c[1] + rr * math.sin(a)), 1.0, z + 0.1, f'{C_}::PEOPLE')
    return Point(c).buffer(R + 2.0, 64).difference(keep_out)


done = []
for cid, c, R in sorted(circles, key=lambda x: -x[2]):
    if R > 30:
        # the picnic grove round the river pavilion: courts in the free ring between the pavilion and the circle,
        # clear of the amphitheatre and the walk, on pavers; planting islands between them
        z = 0.0
        keep_out = unary_union([PAV.buffer(3), AMPH.buffer(2), walk.buffer(1), INDOOR0, RIVER])
        ring = Point(c).buffer(R, 96).difference(keep_out)
        cut_under(Point(c).buffer(R, 96).difference(unary_union([PAV.buffer(0.5), AMPH.buffer(0.5), walk])), -1, 3)
        pods = []
        rmid = (math.sqrt(PAV.area / math.pi) + 4 + R) / 2 + 2
        for k in range(36):
            a = math.radians(k * 10)
            q = (c[0] + rmid * math.cos(a), c[1] + rmid * math.sin(a))
            if ring.buffer(-0.5).contains(Point(q).buffer(10.5)) and all(math.dist(q, p) > 24 for p in pods): pods.append(q)
        base = ring
        for q in pods:
            base = base.difference(court(q, 9.0, z, c, keep_out, 'PICNIC').buffer(0.05))
        isl = []
        for k in range(18):
            a = math.radians(k * 20 + 10)
            q = Point(c[0] + (R - 7) * math.cos(a), c[1] + (R - 7) * math.sin(a))
            blob = q.buffer(5.5, 24)
            if base.buffer(-1).contains(blob) and all(q.distance(Point(p)) > 15 for p in pods) and all(q.distance(i) > 13 for i in isl): isl.append(q)
        islands = unary_union([q.buffer(5.5, 24) for q in isl])
        M.add_area(base.difference(islands.buffer(0.05)), f'{C_}::COURT', 'PICNIC GROVE', 0.05)
        M.add_area(islands, f'{C_}::PLANTING', '', 0.05)
        for q in isl: circle((q.x, q.y), rng.uniform(6, 8), 0.1, f'{C_}::TREES')
        M.log.append(f"picnic grove {cid} round the river pavilion (r {R:.0f} ft): {len(pods)} courts, {len(isl)} planting islands with trees")
        done.append(Point(c).buffer(R))
        continue
    z = surface_z(c)
    if z < 3:
        keep_out = unary_union([PROTECT0, ROADS.buffer(0.5)])
        near = nearest_points(walk.difference(Point(c).buffer(R + 3)), Point(c))[0] if not walk.is_empty and walk.distance(Point(c)) < 60 else None
        toward = (near.x, near.y) if near is not None else (c[0], c[1] + 10)
        cut_under(Point(c).buffer(R + 2.0, 64).difference(keep_out), -1, 3)
    else:
        keep_out = walls_at(z + 0.5).buffer(1.0)
        bp = nearest_points(keep_out, Point(c))[0] if not keep_out.is_empty else Point(c[0], c[1] + 1)
        toward = (bp.x, bp.y)
        cut_under(Point(c).buffer(R + 2.0, 64).difference(keep_out), z - 3, z + 0.5)
    court(c, R, z, toward, keep_out, 'PICNIC')
    M.log.append(f"picnic court {cid} at ({c[0]:.0f}, {c[1]:.0f}) r {R:.0f} ft on {'ground' if z < 3 else f'the +{z:.0f} ft terrace / roof'}")

M.write(sys.argv[3])
print("\n".join(M.log))
