"""Riverfront park for the RIVER model, DRAFT_1 v2 (2026-10-21): the user's reference plan (a riverside park: jagged
plaza edge, angular walk, timber decks, lawn mounds, planting beds, cycle track; ref1.py) laid along our river.

Usage: python3 waterfront6.py <fixture.json of in.3dm> <in.3dm> <out.3dm>
The previous park (PROJECT SITE::RIVERFRONT PARK::*) is removed first; the user's greens and the designed gardens
(PROJECT SITE::GARDENS) stay. River coordinates: s along the south water edge (west → east), u inland.
Paving types on their own layers (hatches): PROMENADE pavers, GRAVEL, DECK timber, GARDEN WALK / CYCLE TRACK red.
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

# ------------------------------------------------------------------ design (DRAFT_1 v2: the user's reference plan 1)
# The riverside park plan the user sent (ref1.py) laid along our river: y along the river at 0.4 ft / px (one plan
# ≈ 520 ft, the second half mirrored so the two meet), x across from the water (px 60) to the building side (px 555),
# stretched to the park's depth there. Jagged plaza edge with planting to the water, plaza paving with a row of tree
# grates, gravel, timber decks, an angular walk, lawn mounds, planting beds, a cycle track along the building.
import ref1
keep = unary_union([o['fp'] for o in objs if (o['layer'] == 'greens' or o['layer'].startswith('PROJECT SITE::GARDENS::')) and o['z0'] < 3 and o['fp'] is not None
                    and o['fp'].geom_type in ('Polygon', 'MultiPolygon') and o['fp'].intersects(zone)])
print(f"kept gardens {keep.area:,.0f} sf")
_iu = [(k * L / 120, inner_u(k * L / 120)) for k in range(121)]
def iu(s):
    w = [u for x, u in _iu if abs(x - s) <= 30]
    return sum(w) / len(w)
REF1, MOUNDS1 = ref1.shapes()
SPAN = (ref1.Y1 - ref1.Y0)
HALF = L / 2
def to_model(x, y, half):
    t = (y - ref1.Y0) / SPAN                          # 0..1 along one plan
    s = t * HALF if half == 0 else L - t * HALF       # the second half mirrored: it runs back from the east end
    u = (x - ref1.X_WATER) / (ref1.X_TRACK1 - ref1.X_WATER) * (iu(s) + 1.5)
    return at(s, u)
free = zone.difference(keep.buffer(3))
mapped = {}
for half in (0, 1):
    f = lambda x, y, h=half: to_model(x, y, h)
    for k, g in REF1.items():
        mapped.setdefault(k, []).append(ref1.map_geom(g, f))
    mapped.setdefault('MOUNDS', []).extend(ref1.map_geom(l, f) for l in MOUNDS1)
    mapped.setdefault('TREES', []).extend(f(x, y) for x, y in ref1.TREE_SQUARES)
    mapped.setdefault('BENCHES', []).extend(LineString([f(*a), f(*b)]) for a, b in ref1.BENCHES)
for k in list(REF1):
    mapped[k] = unary_union([p for p in parts(unary_union(mapped[k]).intersection(free)) if p.area > 25])
# The river steps and the kayak launch stay (cut into the edge planting), two pavilions on the decks' landward side.
steps, step_names = [], []
for s0 in (0.15 * L, 0.46 * L, 0.88 * L):
    for j, (u0, u1) in enumerate([(-12, -6.5), (-5.5, 0), (1, 6.5), (7.5, 13), (14, 19.5)]):
        w = 74 - 6 * j
        steps.append(band(None, lambda s, a=u0, b=u1: (a + b) / 2, u1 - u0, s0 - w / 2, s0 + w / 2, step=2))
steps = unary_union(steps)
kayak_s = 0.36 * L
kayak = unary_union([band(None, lambda s: -26, 52, kayak_s - 5, kayak_s + 5, step=2), band(None, lambda s: -58, 14, kayak_s - 16, kayak_s + 16, step=2)])
for k in ('EDGE PLANTING', 'PLAZA'):
    mapped[k] = mapped[k].difference(steps).difference(kayak)
pav = []
for d in parts(mapped['DECK']):
    if d.area > 2500 and len(pav) < 2:
        c = d.representative_point()
        pav.append(c.buffer(14, 48))
pavilion = unary_union(pav)
mapped['DECK'] = mapped['DECK'].difference(pavilion.buffer(1))
covered = unary_union([mapped[k] for k in REF1] + [steps, kayak, pavilion])
meadow = unary_union([p for p in parts(free.difference(covered.buffer(0.2))) if p.area > 25])
promenade = mapped['PLAZA']

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
NAMES = {'EDGE PLANTING': ('GROVE', 'RIVER EDGE PLANTING'), 'PLAZA': ('PROMENADE', 'RIVER WALK'), 'GRAVEL': ('GRAVEL', ''),
         'DECK': ('DECK', 'TIMBER DECK'), 'LAWN': ('LAWN', 'LAWN MOUNDS'), 'SHRUB': ('PLANTING', ''), 'WALK': ('GARDEN WALK', 'GARDEN WALK'),
         'TRACK': ('CYCLE TRACK', 'CYCLE TRACK')}
for k, (sub, name) in NAMES.items():
    area(mapped[k], f'{P}::{sub}', name)
    CHANGES.append(f"+ {name or sub.lower()}: {mapped[k].area:,.0f} sf")
area(meadow, P + '::GROVE', '')
area(steps, P + '::RIVER STEPS', 'RIVER STEPS')
area(kayak, P + '::KAYAK LAUNCH', 'KAYAK LAUNCH')
for k, p in enumerate(parts(pavilion)):
    solid(p, P + '::PAVILION', 'RIVER PAVILION' if k == 0 else 'GARDEN PAVILION', 0.0, 14.0)
CHANGES.append(f"+ river steps {steps.area:,.0f} sf, kayak launch, {len(parts(pavilion))} pavilions, meadow {meadow.area:,.0f} sf")
for l in mapped['MOUNDS']:
    for x in getattr(l.intersection(mapped['LAWN']), 'geoms', [l.intersection(mapped['LAWN'])]):
        if x.geom_type == 'LineString' and x.length > 8:
            model.Objects.AddCurve(r.PolylineCurve(pl(list(x.coords), 0.05)), attrs(P + '::FURNITURE', 'LAWN MOUND'))
nb = 0
for b in mapped['BENCHES']:
    if free.buffer(-1).contains(b):
        model.Objects.AddCurve(r.PolylineCurve(pl(list(b.coords), 0.05)), attrs(P + '::FURNITURE', 'BENCH')); nb += 1
# Trees: the grates of the plaza, a loose band in the edge planting, a few in the lawns and beds.
trees = []
for x, y in mapped['TREES']:
    p = Point(x, y)
    if promenade.buffer(-2).contains(p):
        model.Objects.AddCurve(r.PolylineCurve(pl(list(box(x - 2.5, y - 2.5, x + 2.5, y + 2.5).exterior.coords), 0.05)), attrs(P + '::FURNITURE', 'TREE GRATE'))
        trees.append(p)
for g, spacing, cap in ((mapped['EDGE PLANTING'], 26, 60), (unary_union([mapped['LAWN'], mapped['SHRUB'], meadow]), 34, 40)):
    gg = g.buffer(-4); n = 0
    if gg.is_empty: continue
    minx, miny, maxx, maxy = gg.bounds
    for _ in range(4000):
        if n >= cap: break
        p = Point(rng.uniform(minx, maxx), rng.uniform(miny, maxy))
        if gg.contains(p) and all(p.distance(q) > spacing for q in trees): trees.append(p); n += 1
for p in trees:
    model.Objects.AddCircle(r.Circle(r.Point3d(p.x, p.y, 0), rng.uniform(8, 11)), attrs(P + '::FLOWERING TREES'))
CHANGES.append(f"+ trees {len(trees)}, benches {nb}")
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
scatter(promenade, 40); scatter(mapped['DECK'], 14, groups=True); scatter(mapped['LAWN'], 10, groups=True)
scatter(mapped['WALK'], 10); scatter(mapped['TRACK'], 6); scatter(steps.intersection(zone.union(river)), 12)
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
                  ('GRAVEL', 'NC.Hardscape'), ('DECK', 'NC.Hardscape'), ('CYCLE TRACK', 'NC.Hardscape'), ('PLANTING', 'NC.Green'),
                  ('KAYAK LAUNCH', 'NC.Hardscape'), ('RAIN GARDEN', 'NC.Water'), ('LAWN', 'NC.Green'), ('GROVE', 'NC.Green'),
                  ('FLOWERING TREES', 'NC.Tree'), ('PEOPLE', 'Entourage')]:
    set_layer(f'{P}::{sub}', role)
set_layer(f'{P}::PAVILION', 'BG.Building')
set_layer(f'{P}::FURNITURE', 'NC.Furniture', mode='line')
model.Strings["ProgramDiagram\\settings"] = json.dumps(settings)
model.Write(sys.argv[3], 8)
json.dump(settings, open(sys.argv[3].rsplit('.', 1)[0] + '_settings.json', 'w'), indent=1)
print("\n".join(CHANGES))
