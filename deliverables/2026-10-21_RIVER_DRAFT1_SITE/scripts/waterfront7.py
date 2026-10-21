"""Riverfront park for the RIVER model, DRAFT_1 v3 (user markup 10-21): outside the property line a riverside walk,
inside it one hardscape plaza zoned by the user's north greens, an amphitheatre round the river pavilion.

Usage: python3 waterfront7.py <fixture.json of in.3dm> <in.3dm> <out.3dm>
The previous park (PROJECT SITE::RIVERFRONT PARK::*) is removed first; the user's greens and the designed gardens
(PROJECT SITE::GARDENS) stay. River coordinates: s along the south water edge (west → east), u inland.
"""
import json, math, random, sys
sys.path.insert(0, __file__.rsplit('/', 1)[0])
import rhino3dm as r
from shapely.geometry import Polygon, MultiPolygon, LineString, Point, box
from shapely.ops import unary_union, linemerge, substring
from r21_lib import rounded, lines_of
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

# ------------------------------------------------------------------ design (DRAFT_1 v3, user markup 10-21)
# Outside the property line: a riverside walk (timber boardwalk at the water, a planted strip with a tree row, the
# 22 ft RIVER WALK, a planted strip with trees and benches up to the property line), all smooth bands.
# Inside the property line: one hardscape plaza; the user's north greens are the main planting and zone it — the paving
# itself is not cut up. Round the river pavilion (the user's PROGRAM::Break Room "RIVER PAVILION"): an outdoor
# amphitheatre, timber seating tiers on arcs facing the pavilion as its stage.
keep = unary_union([o['fp'] for o in objs if (o['layer'] == 'greens' or o['layer'].startswith('PROJECT SITE::GARDENS::')) and o['z0'] < 3 and o['fp'] is not None
                    and o['fp'].geom_type in ('Polygon', 'MultiPolygon') and o['fp'].intersects(zone)])
pav_o = next(o for o in objs if o['name'] == 'RIVER PAVILION' and o['layer'].startswith('PROGRAM::'))
PAV = pav_o['fp']; PC = PAV.centroid; PR = math.sqrt(PAV.area / math.pi)
print(f"kept greens {keep.area:,.0f} sf; pavilion at ({PC.x:.0f}, {PC.y:.0f}) r {PR:.0f}")
inside_all = zone.intersection(parcel).buffer(0)
outside = zone.difference(parcel).buffer(-0.3).buffer(0.3)
outside = unary_union([p for p in parts(outside) if p.area > 200])

def strip(u0, u1, s_a, s_b, step=3.0):
    f0 = u0 if callable(u0) else (lambda s, v=u0: v)
    f1 = u1 if callable(u1) else (lambda s, v=u1: v)
    k = max(2, int((s_b - s_a) / step))
    ss = [s_a + (s_b - s_a) * i / k for i in range(k + 1)]
    return Polygon([at(s, f0(s)) for s in ss] + [at(s, f1(s)) for s in reversed(ss)]).buffer(0)

def smooth(g, r=3.0):
    return g.buffer(r, join_style=1).buffer(-2 * r, join_style=1).buffer(r, join_style=1) if not g.is_empty else g

# Riverside walk bands (u from the water's edge).
boardwalk = strip(0, 9, 0.01 * L, 0.99 * L).intersection(outside)
plant_w = strip(9, 15, 0, L).intersection(outside)
promenade = strip(15, 37, 0, L).intersection(outside)
plant_pl = outside.difference(strip(-80, 37, -5, L + 5))
# River steps and the kayak launch stay at the water.
steps = []
for s0 in (0.15 * L, 0.46 * L, 0.88 * L):
    for j, (u0, u1) in enumerate([(-12, -6.5), (-5.5, 0), (1, 6.5), (7.5, 13), (14, 19.5)]):
        w = 74 - 6 * j
        steps.append(strip(u0, u1, s0 - w / 2, s0 + w / 2, step=2))
steps = unary_union(steps)
kayak_s = 0.36 * L
kayak = unary_union([strip(-52, 0, kayak_s - 5, kayak_s + 5, step=2), strip(-65, -51, kayak_s - 16, kayak_s + 16, step=2)])
boardwalk = boardwalk.difference(steps).difference(kayak); plant_w = plant_w.difference(steps)
promenade = promenade.difference(steps).union(steps.intersection(outside).difference(strip(-80, 0, -5, L + 5)).intersection(strip(0, 37, 0, L)))
promenade = promenade.difference(boardwalk).difference(plant_w)
# Cross walks from the plaza through the riverside planting to the river walk, every ~110 ft (straight, 10 ft).
links = []
s = 55.0
while s < L - 30:
    if all(abs(s - s0) > 50 for s0 in (0.15 * L, 0.46 * L, 0.88 * L)):
        links.append(strip(30, 300, s - 5, s + 5, step=1))
    s += 110
links = unary_union(links).intersection(plant_pl.buffer(0.2)) if links else Polygon()
plant_pl = plant_pl.difference(links.buffer(0.1))
promenade = promenade.union(links)

# Amphitheatre round the pavilion: tiers on arcs about its centre on the landward side, two aisles.
inland = at(L / 2, 300); inl = math.degrees(math.atan2(inland[1] - PC.y, inland[0] - PC.x))
# face the audience towards the river: the seats lie on the landward half, ±70° about the inland direction
ang = math.degrees(math.atan2(PC.y - at(L * 0.5, 0)[1], PC.x - at(L * 0.5, 0)[0]))
def sector(r0, r1, a0, a1, n=64):
    out = [(PC.x + r1 * math.cos(math.radians(a0 + (a1 - a0) * k / n)), PC.y + r1 * math.sin(math.radians(a0 + (a1 - a0) * k / n))) for k in range(n + 1)]
    inn = [(PC.x + r0 * math.cos(math.radians(a0 + (a1 - a0) * k / n)), PC.y + r0 * math.sin(math.radians(a0 + (a1 - a0) * k / n))) for k in range(n, -1, -1)]
    return Polygon(out + inn).buffer(0)
R0, R1, A = PR + 9, PR + 37, 70
amph_dir = ang                       # from the river through the pavilion: the audience sits beyond it
amph = sector(R0, R1, amph_dir - A, amph_dir + A)
aisles = unary_union([LineString([(PC.x, PC.y), (PC.x + 200 * math.cos(math.radians(amph_dir + d)), PC.y + 200 * math.sin(math.radians(amph_dir + d)))]).buffer(2.5)
                      for d in (-25, 25)])
avail = inside_all.union(outside).difference(keep.buffer(2))
amph = amph.intersection(avail)
seats = amph.difference(aisles)
tiers = []
r_ = R0 + 3.5
while r_ < R1 - 0.5:
    arc = LineString([(PC.x + r_ * math.cos(math.radians(amph_dir - A + 2 * A * k / 80)), PC.y + r_ * math.sin(math.radians(amph_dir - A + 2 * A * k / 80))) for k in range(81)])
    tiers += [l for l in lines_of(arc.intersection(seats.buffer(-0.3))) if l.length > 4]
    r_ += 3.5
stage_apron = Point(PC.x, PC.y).buffer(PR + 6, 64).difference(PAV.buffer(0.2)).intersection(avail)

# The hardscape plaza inside the property line: everything there that is not green, the amphitheatre or the pavilion.
inside = inside_all.difference(keep.buffer(0.2)).difference(PAV.buffer(0.2))
plaza = smooth(inside.difference(amph).difference(stage_apron), 2.0).intersection(inside)
plaza = unary_union([p for p in parts(plaza) if p.area > 50])
# the plaza meets the river walk where the property line is close to the water: no seam, both are paving
promenade = promenade.difference(amph).difference(stage_apron)
plant_pl = plant_pl.difference(amph).difference(stage_apron)
pavilion = Polygon()

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
for g, sub, name in [(boardwalk, 'BOARDWALK', 'BOARDWALK'), (plant_w, 'GROVE', ''), (promenade, 'PROMENADE', 'RIVER WALK'),
                     (plant_pl, 'GROVE', 'RIVERSIDE PLANTING'), (steps, 'RIVER STEPS', 'RIVER STEPS'), (kayak, 'KAYAK LAUNCH', 'KAYAK LAUNCH'),
                     (plaza, 'PLAZA', 'RIVER PLAZA'), (seats, 'SEATING STEPS', 'AMPHITHEATRE'), (stage_apron, 'STAGE', ''),
                     (amph.difference(seats), 'PLAZA', '')]:
    area(g, f'{P}::{sub}', name)
    CHANGES.append(f"+ {(name or sub).lower()}: {g.area:,.0f} sf")
for l in tiers:
    model.Objects.AddCurve(r.PolylineCurve(pl(list(l.coords), 0.05)), attrs(P + '::FURNITURE', 'SEATING TIER'))
CHANGES.append(f"+ amphitheatre: {len(tiers)} tier lines, r {R0:.0f}–{R1:.0f} ft about the pavilion, two aisles")

trees = []
def row(u, spacing, region, off=0.0):
    s = off
    while s < L:
        p = Point(at(s, u))
        if region.buffer(-1.5).contains(p) and all(p.distance(q) > spacing * 0.8 for q in trees): trees.append(p)
        s += spacing
row(12, 28, plant_w, 6)
for p in parts(plant_pl):
    if p.area > 400:
        g = p.buffer(-5)
        if g.is_empty: continue
        minx, miny, maxx, maxy = g.bounds
        for _ in range(600):
            q = Point(rng.uniform(minx, maxx), rng.uniform(miny, maxy))
            if g.contains(q) and all(q.distance(t) > 26 for t in trees): trees.append(q)
# plaza: tree grates in rows parallel to the river, clear of the amphitheatre and the greens
grates = []
for u in (60, 95, 130):
    s = 14.0
    while s < L:
        p = Point(at(s, u))
        if plaza.buffer(-6).contains(p) and not amph.buffer(8).contains(p) and all(p.distance(q) > 24 for q in trees + grates): grates.append(p)
        s += 32
for p in grates:
    model.Objects.AddCurve(r.PolylineCurve(pl(list(box(p.x - 2.5, p.y - 2.5, p.x + 2.5, p.y + 2.5).exterior.coords), 0.05)), attrs(P + '::FURNITURE', 'TREE GRATE'))
for p in trees + grates:
    model.Objects.AddCircle(r.Circle(r.Point3d(p.x, p.y, 0), rng.uniform(8, 10.5)), attrs(P + '::FLOWERING TREES'))
CHANGES.append(f"+ trees {len(trees)} in the planting, {len(grates)} in plaza grates")
# long benches along the river walk's landward edge
nb = 0
edge = LineString([at(s, 35.5) for s in [L * k / 300 for k in range(301)]])
s = 20.0
while s + 14 < edge.length:
    seg = substring(edge, s, s + 14)
    if promenade.buffer(-0.3).contains(seg):
        model.Objects.AddCurve(r.PolylineCurve(pl(list(seg.coords), 0.05)), attrs(P + '::FURNITURE', 'BENCH')); nb += 1
    s += 42
CHANGES.append(f"+ benches {nb}")
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
scatter(promenade, 34); scatter(boardwalk, 10); scatter(plaza, 30); scatter(seats, 26); scatter(steps.intersection(zone.union(river)), 12)
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
                  ('BOARDWALK', 'NC.Hardscape'), ('SEATING STEPS', 'NC.Hardscape'), ('STAGE', 'NC.Hardscape'),
                  ('KAYAK LAUNCH', 'NC.Hardscape'), ('RAIN GARDEN', 'NC.Water'), ('LAWN', 'NC.Green'), ('GROVE', 'NC.Green'),
                  ('FLOWERING TREES', 'NC.Tree'), ('PEOPLE', 'Entourage')]:
    set_layer(f'{P}::{sub}', role)
set_layer(f'{P}::PAVILION', 'BG.Building')
set_layer(f'{P}::FURNITURE', 'NC.Furniture', mode='line')
model.Strings["ProgramDiagram\\settings"] = json.dumps(settings)
model.Write(sys.argv[3], 8)
json.dump(settings, open(sys.argv[3].rsplit('.', 1)[0] + '_settings.json', 'w'), indent=1)
print("\n".join(CHANGES))
