"""Riverfront park for the RIVER model, DRAFT_1 (2026-10-21): laid out again in the building's language — long filleted
bands parallel to the river that gather into points at two cross walks and stop together on straight lines (user
references: Vancouver WA / Seattle waterfronts, stepped riverside seating, varied paving).

Usage: python3 waterfront5.py <fixture.json of in.3dm> <in.3dm> <out.3dm>
The previous park (PROJECT SITE::RIVERFRONT PARK::*) is removed first; the user's greens and the designed gardens
(PROJECT SITE::GARDENS) stay. Laid out in river coordinates: s along the south water edge (west → east), u inland.
Paving types on their own layers (hatches): BOARDWALK / SEATING STEPS timber, PROMENADE / GARDEN WALK light pavers,
INLAY dark pavers at the cross walks.
"""
import json, math, random, sys
sys.path.insert(0, __file__.rsplit('/', 1)[0])
import rhino3dm as r
from shapely.geometry import Polygon, MultiPolygon, LineString, Point, box
from shapely.ops import unary_union, linemerge, substring
from r21_lib import rounded
import inv

objs = inv.load(sys.argv[1])
byid = {o['id']: o for o in objs}
model = r.File3dm.Read(sys.argv[2])
settings = json.loads(model.Strings["ProgramDiagram\\settings"])
rng = random.Random(1016)
CHANGES = []

def find(prefix):
    hits = [o for o in objs if o['id'].startswith(prefix)]
    assert len(hits) == 1, prefix
    return hits[0]

layer_index = {model.Layers[i].FullPath: i for i in range(len(model.Layers))}
def layer(path, color=(0, 0, 0)):
    if path in layer_index: return layer_index[path]
    parent_path, _, name = path.rpartition('::')
    l = r.Layer(); l.Name = name; l.Color = (*color, 255)
    if parent_path: l.ParentLayerId = model.Layers[layer(parent_path)].Id
    layer_index[path] = model.Layers.Add(l)
    return layer_index[path]

def attrs(path, name=""):
    a = r.ObjectAttributes(); a.LayerIndex = layer(path)
    if name: a.Name = name
    return a

def parts(g):
    if g is None or g.is_empty: return []
    if isinstance(g, Polygon): return [g]
    return [p for p in getattr(g, 'geoms', []) if isinstance(p, Polygon)]

def pl(coords, z):
    p = r.Polyline(len(coords))
    for x, y in coords: p.Add(x, y, z)
    return p

def area(g, path, name="", z=0.0, min_area=6):
    n = 0
    for p in parts(g):
        if p.area < min_area: continue
        p = p.simplify(0.2)
        for ring in [q.exterior for q in inv.no_holes(p)]:
            model.Objects.AddCurve(r.PolylineCurve(pl(list(ring.coords), z)), attrs(path, name))
        n += 1
    return n

def solid(g, path, name, z0, z1):
    for p in parts(g):
        if p.area < 6: continue
        c = list(p.simplify(0.2).exterior.coords)
        if not Polygon(c).exterior.is_ccw: c = c[::-1]
        model.Objects.AddExtrusion(r.Extrusion.Create(r.PolylineCurve(pl(c, z0)), z1 - z0, True), attrs(path, name))

# ------------------------------------------------------------------ the old park goes
OLD = 'PROJECT SITE::RIVERFRONT PARK::'
old_ids = [o['id'] for o in objs if o['layer'].startswith(OLD)]
for x in list(model.Objects):
    if str(x.Attributes.Id) in old_ids: model.Objects.Delete(x.Attributes.Id)
objs = [o for o in objs if not o['layer'].startswith(OLD)]
CHANGES.append(f"- previous riverfront park: {len(old_ids)} objects")

# ------------------------------------------------------------------ frame
river = unary_union([o['fp'] for o in objs if o['layer'] == 'SITE CONTEXT::RIVER'])
parcel = find('5a6931')['fp']
PL = LineString([(14.2, 235.1), (181.9, 327.8), (309.5, 478.8), (487.8, 606.8), (659.4, 631.5)])
edge = river.boundary.intersection(PL.buffer(200))
edge = linemerge([g for g in getattr(edge, 'geoms', [edge]) if g.distance(PL) < 190])
E = max(getattr(edge, 'geoms', [edge]), key=lambda g: g.length)
if E.coords[0][0] > E.coords[-1][0]: E = LineString(list(E.coords)[::-1])
# Frontage: from 90 ft west of the parcel to 70 ft east of it.
proj = [E.project(Point(p)) for p in (PL.coords[0], PL.coords[-1])]
S0, S1 = max(0.0, proj[0] - 90), min(E.length, proj[1] + 70)
E = LineString([E.interpolate(s).coords[0] for s in [S0 + (S1 - S0) * k / 400 for k in range(401)]])
L = E.length

def frame(s):
    s = min(max(s, 0.0), L)
    a, b = E.interpolate(max(0, s - 2)), E.interpolate(min(L, s + 2))
    tx, ty = b.x - a.x, b.y - a.y; n = math.hypot(tx, ty); tx, ty = tx / n, ty / n
    nx, ny = -ty, tx                       # left of west → east: test which side is land
    p = E.interpolate(s)
    if river.contains(Point(p.x + nx * 5, p.y + ny * 5)): nx, ny = -nx, -ny
    return p.x, p.y, nx, ny, tx, ty

def at(s, u):
    x, y, nx, ny, _, _ = frame(s)
    return x + nx * u, y + ny * u

def band(f_s, u_of, width, s_a, s_b, step=4.0):
    """Ribbon of the given width around the curve u = u_of(s), s in [s_a, s_b]."""
    k = max(2, int((s_b - s_a) / step))
    line = LineString([at(s_a + (s_b - s_a) * i / k, u_of(s_a + (s_b - s_a) * i / k)) for i in range(k + 1)])
    return line.buffer(width / 2, cap_style=2, join_style=1)

def blob(s, u, ls, lu, rot=0.0, n=48, wobble=0.12, seed=0):
    """Soft organic shape centred at (s, u), ls along the river, lu inland."""
    rr = random.Random(seed)
    ph = [rr.uniform(0, 2 * math.pi) for _ in range(3)]
    pts = []
    for i in range(n):
        t = 2 * math.pi * i / n
        k = 1 + wobble * (math.sin(2 * t + ph[0]) * .6 + math.sin(3 * t + ph[1]) * .3 + math.sin(5 * t + ph[2]) * .1)
        a, b = ls / 2 * math.cos(t) * k, lu / 2 * math.sin(t) * k
        a, b = a * math.cos(rot) - b * math.sin(rot), a * math.sin(rot) + b * math.cos(rot)
        pts.append(at(s + a, u + b))
    return Polygon(pts).buffer(0)

# ------------------------------------------------------------------ the park zone
ss = [L * k / 200 for k in range(201)]
land_band = Polygon([at(s, -1) for s in ss] + [at(s, 260) for s in reversed(ss)]).buffer(0)
build = unary_union([o['fp'] for o in objs if o['fp'] is not None and o['fp'].geom_type in ('Polygon', 'MultiPolygon') and (
    o['layer'].startswith('PROGRAM::') and 'Curve' not in o['type'] and o['z0'] < 1)])
inner = parcel.difference(build.buffer(8))
strip = unary_union([p for p in parts(inner.intersection(PL.buffer(110))) if p.distance(PL) < 3 and p.area > 400])
bank = land_band.difference(river).difference(parcel)
bank = bank.intersection(PL.buffer(170).union(river.buffer(140)))
cut_w = LineString([at(0, -50), at(0, 300)]).buffer(0.5); cut_e = LineString([at(L, -50), at(L, 300)]).buffer(0.5)
zone = unary_union([bank, strip]).buffer(0.2).buffer(-0.2)
zone = zone.difference(unary_union([cut_w, cut_e]))
zone = max(parts(zone), key=lambda p: p.area)
# the parcel's own street edge (south of the west tip) stays as it is
zone = zone.difference(box(-1e4, -1e4, 1e4, 200).difference(parcel.buffer(0)))
zone = max(parts(zone), key=lambda p: p.area)
water_edge_zone = zone
print(f"park zone {zone.area:,.0f} sf, frontage {L:,.0f} ft")

def inner_u(s):
    """Distance inland from the water edge to the far side of the park at s."""
    x, y, nx, ny, _, _ = frame(s)
    ray = LineString([(x + nx * 2, y + ny * 2), (x + nx * 300, y + ny * 300)])
    hit = ray.intersection(zone)
    segs = [g for g in getattr(hit, 'geoms', [hit]) if g.length > 0]
    return max((Point(x, y).distance(Point(g.coords[-1])) for g in segs), default=60)

# ------------------------------------------------------------------ design (DRAFT_1: converging bands)
# Language of the building (user 10-21): long filleted bands parallel to the river that gather into points at the
# cross walks (nodes) and stop together on straight lines; scale after the Vancouver WA / Seattle waterfronts:
# timber boardwalk at the water, a 22 ft river walk, planting strips with tree rows and long benches, leaf-shaped
# families of walks / seating steps / lawn between the nodes, an upper walk along the building, dark paving inlays at
# the nodes. The designed gardens and the user's greens stay; every band goes round them.
keep = unary_union([o['fp'] for o in objs if (o['layer'] == 'greens' or o['layer'].startswith('PROJECT SITE::GARDENS::')) and o['z0'] < 3 and o['fp'] is not None
                    and o['fp'].geom_type in ('Polygon', 'MultiPolygon') and o['fp'].intersects(zone)])
print(f"kept gardens {keep.area:,.0f} sf")
_iu = [(k * L / 120, inner_u(k * L / 120)) for k in range(121)]
def iu(s):
    w = [u for x, u in _iu if abs(x - s) <= 30]
    return sum(w) / len(w)

def strip(u0, u1, s_a, s_b, step=3.0):
    """Area between u = u0(s) and u = u1(s) for s in [s_a, s_b] (u0 / u1 may be numbers)."""
    f0 = u0 if callable(u0) else (lambda s, v=u0: v)
    f1 = u1 if callable(u1) else (lambda s, v=u1: v)
    k = max(2, int((s_b - s_a) / step))
    ss = [s_a + (s_b - s_a) * i / k for i in range(k + 1)]
    return Polygon([at(s, f0(s)) for s in ss] + [at(s, f1(s)) for s in reversed(ss)]).buffer(0)

free = zone.difference(keep.buffer(3))
clip = lambda g: unary_union([p for p in parts(g.intersection(free)) if p.area > 30])
NODES = [0.0, 0.30 * L, 0.62 * L, L]
U_UP = lambda s: iu(s) - 10                       # upper walk centre
boardwalk = clip(strip(0, 10, 0.02 * L, 0.98 * L))
promenade = clip(strip(12, 34, 0, L))
upper = clip(strip(lambda s: U_UP(s) - 6, lambda s: U_UP(s) + 6, 0.04 * L, 0.96 * L))
cross, nodes_pts = [], []
for sn in NODES[1:-1]:
    cross.append(strip(30, lambda s: iu(s), sn - 7, sn + 7))
inlay = clip(unary_union(cross))
fam = {'GARDEN WALK': [], 'SEATING STEPS': [], 'LAWN': [], 'RAIN GARDEN': []}
tree_lines = [LineString([at(s, 38) for s in [L * k / 200 for k in range(201)]])]
PATTERN = [(-0.80, -0.62, 'GARDEN WALK'), (-0.56, -0.42, 'SEATING STEPS'), (-0.38, 0.20, 'LAWN'), (0.26, 0.40, 'GARDEN WALK')]
for c, (sa, sb) in enumerate(zip(NODES[:-1], NODES[1:])):
    lo = lambda s: 42.0
    hi = lambda s: U_UP(s) - 12
    def env(s, sa=sa, sb=sb):
        return math.sin(math.pi * min(1, max(0, (s - sa) / (sb - sa)))) ** 0.7
    uc = lambda s: (lo(s) + hi(s)) / 2
    hh = lambda s, env=env: max(0.0, (hi(s) - lo(s)) / 2) * env(s)
    for f0, f1, typ in PATTERN:
        g = strip(lambda s, f=f0: uc(s) + hh(s) * f, lambda s, f=f1: uc(s) + hh(s) * f, sa, sb)
        if typ == 'LAWN' and c == 1:      # the middle chapter: a rain garden swale in front of its lawn
            fam['RAIN GARDEN'].append(strip(lambda s: uc(s) - hh(s) * 0.38, lambda s: uc(s) - hh(s) * 0.22, sa, sb))
            g = strip(lambda s: uc(s) - hh(s) * 0.16, lambda s: uc(s) + hh(s) * 0.20, sa, sb)
        fam[typ].append(g)
    for f in (-0.90, 0.73):
        tree_lines.append(LineString([at(s, uc(s) + hh(s) * f) for s in [sa + (sb - sa) * k / 80 for k in range(81)]]))
fam = {k: clip(unary_union(v)) for k, v in fam.items()}
# Pavilions beside the two cross walks, a water play court at the first node.
pav = []
for sn, side in zip(NODES[1:-1], (1, -1)):
    pav.append(Point(at(sn + side * 30, (42 + U_UP(sn) - 12) / 2 + 8)).buffer(15, 48))
pavilion = unary_union(pav).intersection(free)
sn = NODES[1]
water_play = clip(strip(lambda s: U_UP(s) - 40, lambda s: U_UP(s) - 22, sn - 32, sn - 9))
water_play = rounded(water_play, 4) if not water_play.is_empty else water_play
steps, step_names = [], []
for s0 in (0.15 * L, 0.46 * L, 0.88 * L):
    for j, (u0, u1) in enumerate([(-12, -6.5), (-5.5, 0), (1, 6.5), (7.5, 13)]):
        w = 74 - 6 * j
        steps.append(strip(u0, u1, s0 - w / 2, s0 + w / 2, step=2))
steps = unary_union(steps)
kayak_s = 0.36 * L
kayak = unary_union([strip(-52, 0, kayak_s - 5, kayak_s + 5, step=2), strip(-65, -51, kayak_s - 16, kayak_s + 16, step=2)])
hard_main = unary_union([boardwalk, promenade, upper, inlay, fam['GARDEN WALK'], fam['SEATING STEPS'], water_play, pavilion.buffer(6)])
for k in ('GARDEN WALK', 'SEATING STEPS', 'LAWN', 'RAIN GARDEN'):
    fam[k] = fam[k].difference(pavilion.buffer(6)).difference(water_play.buffer(2)).difference(inlay)
for k in ('GARDEN WALK', 'SEATING STEPS'):
    fam[k] = unary_union([p for p in parts(fam[k]) if p.area > 40])
promenade = promenade.difference(steps); boardwalk = boardwalk.difference(steps).difference(kayak)
meadow = free.difference(unary_union([hard_main, fam['LAWN'], fam['RAIN GARDEN'], steps]).buffer(0.3))

# ------------------------------------------------------------------ replace the near bank context
removed = 0
for o in objs:
    if o['layer'] not in ('SITE CONTEXT::BANK LANDSCAPE', 'SITE CONTEXT::WALKWAY', 'SITE CONTEXT::CITY BLOCKS', 'SITE CONTEXT::TREES'): continue
    g = o['fp']
    if g is None or not g.intersects(zone): continue
    model.Objects.Delete(next(x.Attributes.Id for x in model.Objects if str(x.Attributes.Id) == o['id']))
    removed += 1
    if o['layer'] == 'SITE CONTEXT::TREES': continue
    rest = g.difference(zone)
    area(rest, o['layer'], '', 0.0)
CHANGES.append(f"~ near bank: {removed} context objects replaced by the park (rest of each kept)")

P = 'PROJECT SITE::RIVERFRONT PARK'
area(boardwalk, P + '::BOARDWALK', 'BOARDWALK')
area(promenade, P + '::PROMENADE', 'RIVER WALK')
area(upper, P + '::GARDEN WALK', 'UPPER WALK')
area(fam['GARDEN WALK'], P + '::GARDEN WALK', 'GARDEN WALK')
area(inlay, P + '::INLAY', 'CROSS WALK')
area(fam['SEATING STEPS'], P + '::SEATING STEPS', 'SEATING STEPS')
area(steps, P + '::RIVER STEPS', 'RIVER STEPS')
area(kayak, P + '::KAYAK LAUNCH', 'KAYAK LAUNCH')
area(fam['LAWN'], P + '::LAWN', 'LAWN')
area(fam['RAIN GARDEN'], P + '::RAIN GARDEN', 'RAIN GARDEN')
area(water_play, P + '::WATER PLAY', 'WATER PLAY')
area(meadow, P + '::GROVE', 'RIVER MEADOW')
for k, p in enumerate(parts(pavilion)):
    solid(p, P + '::PAVILION', 'RIVER PAVILION' if k == 0 else 'GARDEN PAVILION', 0.0, 14.0)
for name, g in [('boardwalk', boardwalk), ('river walk', promenade), ('upper walk', upper), ('garden walks', fam['GARDEN WALK']),
                ('cross walk inlays', inlay), ('seating steps', fam['SEATING STEPS']), ('river steps', steps), ('lawns', fam['LAWN']),
                ('rain garden', fam['RAIN GARDEN']), ('water play', water_play), ('meadow planting', meadow), ('pavilions', pavilion)]:
    CHANGES.append(f"+ {name}: {g.area:,.0f} sf")

# Tree rows (flowering) along the planting strips, clear of every walk.
hard_all = unary_union([hard_main, steps, kayak]).buffer(5)
trees = []
for line in tree_lines:
    s = rng.uniform(5, 20)
    while s < line.length:
        p = line.interpolate(s)
        if meadow.buffer(-2).contains(p) and not hard_all.contains(p) and all(p.distance(q) > 22 for q in trees): trees.append(p)
        s += 30
for p in trees:
    model.Objects.AddCircle(r.Circle(r.Point3d(p.x, p.y, 0), rng.uniform(8, 10.5)), attrs(P + '::FLOWERING TREES'))
CHANGES.append(f"+ trees in rows: {len(trees)}")
# Long linear benches facing the river along the river walk's inland edge, and along the garden walks.
nb = 0
edge = LineString([at(s, 32.5) for s in [L * k / 300 for k in range(301)]])
s = 20.0
while s + 14 < edge.length:
    seg = substring(edge, s, s + 14)
    if promenade.buffer(-0.3).contains(seg):
        model.Objects.AddCurve(r.PolylineCurve(pl(list(seg.coords), 0.05)), attrs(P + '::FURNITURE', 'BENCH')); nb += 1
    s += 42
CHANGES.append(f"+ linear benches: {nb}")

people = []
def scatter(g, n, groups=False):
    g = g.buffer(-2)
    if g.is_empty: return
    minx, miny, maxx, maxy = g.bounds
    tries = 0
    while n > 0 and tries < 4000:
        tries += 1
        c = Point(rng.uniform(minx, maxx), rng.uniform(miny, maxy))
        if not g.contains(c) or any(c.distance(q) < 6 for q in people): continue
        k = rng.randint(2, 4) if groups else 1
        for j in range(k):
            people.append(Point(c.x + 2.6 * math.cos(2 * math.pi * j / k), c.y + 2.6 * math.sin(2 * math.pi * j / k)) if k > 1 else c)
        n -= 1
scatter(fam['LAWN'], 12, groups=True)
scatter(promenade, 26); scatter(boardwalk, 10); scatter(upper, 8); scatter(fam['GARDEN WALK'], 10)
scatter(fam['SEATING STEPS'], 14); scatter(steps.intersection(zone.union(river)), 14); scatter(water_play, 6); scatter(inlay, 6)
for p in people:
    model.Objects.AddCircle(r.Circle(r.Point3d(p.x, p.y, 0), 1.0), attrs(P + '::PEOPLE'))
CHANGES.append(f"+ people (activity points): {len(people)}")

# ------------------------------------------------------------------ the rest of the parcel
# Ground of our parcel that nothing covers any more (round the auditorium, along the building, the west edge): paving
# (PROJECT SITE::PLAZA), so the site plan has no blank holes. Thin slivers (< 50 sf) are left.
covered = unary_union([build, zone] + [o['fp'] for o in objs if o['fp'] is not None and o['fp'].geom_type in ('Polygon', 'MultiPolygon')
                                       and (o['layer'].startswith('PROJECT SITE') or o['layer'] in ('greens', 'ROAD'))])
CHANGES.append("~ parcel ground outside the park: laid out by d1_plaza.py")

# ------------------------------------------------------------------ roles
def set_layer(path, role, key="", mode="color"):
    settings['layers'][path] = {"use": True, "role": role, "programKey": key, "mode": mode}
set_layer(P, 'Ignore', '', '')
for sub, role in [('PROMENADE', 'NC.Hardscape'), ('GARDEN WALK', 'NC.Hardscape'), ('PLAZA', 'NC.Hardscape'), ('RIVER STEPS', 'NC.Hardscape'),
                  ('BOARDWALK', 'NC.Hardscape'), ('INLAY', 'NC.Hardscape'), ('SEATING STEPS', 'NC.Hardscape'), ('WATER PLAY', 'NC.Water'),
                  ('KAYAK LAUNCH', 'NC.Hardscape'), ('RAIN GARDEN', 'NC.Water'), ('LAWN', 'NC.Green'), ('GROVE', 'NC.Green'),
                  ('FLOWERING TREES', 'NC.Tree'), ('PEOPLE', 'Entourage')]:
    set_layer(f'{P}::{sub}', role)
set_layer(f'{P}::PAVILION', 'BG.Building')
set_layer(f'{P}::FURNITURE', 'NC.Furniture', mode='line')
model.Strings["ProgramDiagram\\settings"] = json.dumps(settings)
model.Write(sys.argv[3], 8)
json.dump(settings, open(sys.argv[3].rsplit('.', 1)[0] + '_settings.json', 'w'), indent=1)
print("\n".join(CHANGES))
