"""Riverfront park for the RIVER model (design elements after the Sasaki waterfront reference, plugin roles).

Usage: python3 waterfront.py <fixture.json of in.3dm> <in.3dm> <out.3dm>
Everything is laid out in river coordinates: s along the south water edge (west → east), u inland from it.
The near bank (context bank landscape, walkway, street trees) inside the park is replaced by the design.
"""
import json, math, random, sys
sys.path.insert(0, __file__.rsplit('/', 1)[0])
import rhino3dm as r
from shapely.geometry import Polygon, MultiPolygon, LineString, Point, box
from shapely.ops import unary_union, linemerge
import inv

objs = inv.load(sys.argv[1])
byid = {o['id']: o for o in objs}
model = r.File3dm.Read(sys.argv[2])
settings = json.loads(model.Strings[0][1])
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
    (o['layer'].startswith('PROGRAM::') and o['type'] != 'PolylineCurve' and o['z0'] < 1) or o['id'][:6] == 'f4881b')])
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

# ------------------------------------------------------------------ design
keep = unary_union([find(i)['fp'] for i in ('d7242b', 'ab79f8', '8d8aa1')])    # the user's three green mounds
prom_u = lambda s: 44 + 13 * math.sin(2 * math.pi * s / 230 + 0.7)
promenade = band(None, prom_u, 18, 0, L).intersection(zone)

# Garden walks: S-curves from the building edge down to the promenade.
walks = []
walk_s = [0.24 * L, 0.52 * L, 0.80 * L]
for k, s0 in enumerate(walk_s):
    ui = inner_u(s0) - 4; up = prom_u(s0 + 36)
    pts = []
    for i in range(41):
        t = i / 40; e = 3 * t * t - 2 * t ** 3
        s = s0 + 36 * e + 10 * math.sin(math.pi * t) * (1 if k % 2 else -1)
        pts.append(at(s, ui + (up - ui) * t))
    walks.append(LineString(pts).buffer(5.5, cap_style=1))
walks = unary_union(walks).intersection(zone)

plaza_c = at(walk_s[1] + 36, prom_u(walk_s[1] + 36) + 16)
sculpture = Point(plaza_c).buffer(26, 48).intersection(zone)
pav_s = 0.64 * L
pavilion = Point(at(pav_s, prom_u(pav_s) + 36)).buffer(19, 48)
pav_apron = Point(at(pav_s, prom_u(pav_s) + 36)).buffer(29, 48).difference(pavilion)

steps, step_names = [], []
for s0 in (0.15 * L, 0.46 * L, 0.88 * L):
    for j, (u0, u1) in enumerate([(-12, -6.5), (-5.5, 0), (1, 6.5), (7.5, 13), (14, 19.5)]):
        w = 74 - 6 * j
        steps.append(band(None, lambda s, a=u0, b=u1: (a + b) / 2, u1 - u0, s0 - w / 2, s0 + w / 2, step=2))
steps = unary_union(steps)
kayak_s = 0.33 * L
dock = band(None, lambda s: -26, 52, kayak_s - 5, kayak_s + 5, step=2)
deck = band(None, lambda s: -58, 14, kayak_s - 16, kayak_s + 16, step=2)
kayak = unary_union([dock, deck])

# Inner edge of the park (building terrace / property line), smoothed over 60 ft.
_iu = [(k * L / 120, inner_u(k * L / 120)) for k in range(121)]
def iu(s):
    w = [u for x, u in _iu if abs(x - s) <= 30]
    return sum(w) / len(w)
# Upper ribbon along the building: links the garden walks and the building's river doors.
upper = band(None, lambda s: iu(s) - 14, 10, 0.10 * L, 0.90 * L, step=4).intersection(zone)
# Water's edge boardwalk between the step terraces.
boardwalk = band(None, lambda s: 8.5, 9, 0.03 * L, 0.97 * L, step=4).intersection(zone)
hard = unary_union([promenade, walks, upper, boardwalk, sculpture, pav_apron, steps.intersection(zone)])
lawns = [(0.31 * L, 'PICNIC LAWN', 130, 3), (0.62 * L, 'EVENT LAWN', 150, 5), (0.86 * L, 'PICNIC LAWN', 110, 9)]
lawn_geoms = []
for s, name, ls, seed in lawns:
    lo, hi = prom_u(s) + 9 + 6, iu(s) - 14 - 5 - 6
    g = blob(s, (lo + hi) / 2, ls, max(26, hi - lo), 0.0, seed=seed).intersection(zone).difference(hard.buffer(5))
    g = g.difference(keep.buffer(6)).difference(pavilion.buffer(12))
    g = max(parts(g), key=lambda p: p.area) if parts(g) else g
    lawn_geoms.append((name, g))
rain = band(None, lambda s: prom_u(s) + 22 + 6 * math.sin(2 * math.pi * s / 90), 9, 0.36 * L, 0.56 * L, step=3)
rain = rain.intersection(zone).difference(hard.buffer(3)).difference(unary_union([g for _, g in lawn_geoms]).buffer(3))
taken = unary_union([hard, rain.buffer(2), pavilion.buffer(1), keep] + [g.buffer(1) for _, g in lawn_geoms])
rest = zone.difference(taken)
# Flowering groves: loose clumps in the leftover ground; the rest stays an open meadow (not planted by the plugin).
groves = []
for k in range(400):
    if len(groves) >= 11: break
    sx, uu = rng.uniform(0, L), rng.uniform(0.25, 0.95)
    c = Point(at(sx, prom_u(sx) * uu if rng.random() < 0.4 else prom_u(sx) + 12 + (iu(sx) - prom_u(sx) - 12) * uu))
    if not rest.contains(c) or any(c.distance(g.centroid) < 85 for g in groves): continue
    groves.append(blob(sx, Point(*frame(sx)[:2]).distance(c), rng.uniform(55, 80), rng.uniform(30, 42), seed=k).intersection(rest))
grove = unary_union(groves)
meadow = rest.difference(grove.buffer(0.5))
# Water's edge between the steps: a planted bank (groves) to the water.

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
area(promenade, P + '::PROMENADE', 'RIVER PROMENADE')
area(walks, P + '::GARDEN WALK', 'GARDEN WALK')
area(upper.difference(walks), P + '::GARDEN WALK', 'UPPER RIBBON')
area(boardwalk.difference(steps), P + '::RIVER STEPS', 'BOARDWALK')
area(sculpture, P + '::PLAZA', 'SCULPTURE PLAZA')
area(pav_apron, P + '::PLAZA', '')
area(steps, P + '::RIVER STEPS', 'RIVER STEPS')
area(kayak, P + '::KAYAK LAUNCH', 'KAYAK LAUNCH')
area(rain, P + '::RAIN GARDEN', 'RAIN GARDEN')
for name, g in lawn_geoms:
    area(g, P + '::LAWN', name)                         # LAWN / MEADOW names stay open (style greenTrees.openNameKeywords)
area(grove, P + '::GROVE', 'FLOWERING GROVE')
area(meadow, P + '::LAWN', 'RIVER MEADOW')
solid(pavilion, P + '::PAVILION', 'RIVER PAVILION', 0.0, 14.0)    # V2: off our site — a context building, not our program
for name, g in [('promenade', promenade), ('garden walks', walks), ('upper ribbon', upper), ('boardwalk', boardwalk), ('sculpture plaza', sculpture), ('river steps', steps), ('kayak launch', kayak),
                ('rain garden', rain), ('groves', grove), ('meadow', meadow), ('pavilion', pavilion)] + [(n.lower(), g) for n, g in lawn_geoms]:
    CHANGES.append(f"+ {name}: {g.area:,.0f} sf")

# Flowering trees: a loose rhythm along both sides of the promenade (pink in spring), clear of every path.
trees = []
s = 6.0
while s < L - 6:
    side = 1 if int(s / 37) % 2 else -1
    u = prom_u(s) + side * (16 + rng.uniform(0, 8))
    p = Point(at(s, u))
    if zone.contains(p) and not hard.buffer(7).contains(p) and not unary_union([g for _, g in lawn_geoms]).buffer(4).contains(p) \
            and not rain.buffer(5).contains(p) and not pavilion.buffer(10).contains(p):
        trees.append(p)
    s += rng.uniform(26, 40)
for p in trees:
    model.Objects.AddCircle(r.Circle(r.Point3d(p.x, p.y, 0), rng.uniform(8, 11)), attrs(P + '::FLOWERING TREES'))
CHANGES.append(f"+ flowering trees along the promenade: {len(trees)}")

# People: picnics on the lawns, strollers and joggers, a crowd on the river steps, the plaza, the kayak launch.
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
            q = Point(c.x + 2.6 * math.cos(2 * math.pi * j / k), c.y + 2.6 * math.sin(2 * math.pi * j / k)) if k > 1 else c
            people.append(q)
        n -= 1
for _, g in lawn_geoms: scatter(g, 5, groups=True)
scatter(promenade, 22)
scatter(steps.intersection(zone.union(river)), 16)
scatter(sculpture, 5)
scatter(deck, 3)
scatter(pav_apron, 5)
scatter(walks, 6)
scatter(upper, 8)
scatter(boardwalk, 10)
for p in people:
    model.Objects.AddCircle(r.Circle(r.Point3d(p.x, p.y, 0), 1.0), attrs(P + '::PEOPLE'))
CHANGES.append(f"+ people (activity points): {len(people)}")

# ------------------------------------------------------------------ roles
def set_layer(path, role, key="", mode="color"):
    settings['layers'][path] = {"use": True, "role": role, "programKey": key, "mode": mode}
set_layer(P, 'Ignore', '', '')
for sub, role in [('PROMENADE', 'NC.Hardscape'), ('GARDEN WALK', 'NC.Hardscape'), ('PLAZA', 'NC.Hardscape'), ('RIVER STEPS', 'NC.Hardscape'),
                  ('KAYAK LAUNCH', 'NC.Hardscape'), ('RAIN GARDEN', 'NC.Water'), ('LAWN', 'NC.Green'), ('GROVE', 'NC.Green'),
                  ('FLOWERING TREES', 'NC.Tree'), ('PEOPLE', 'Entourage')]:
    set_layer(f'{P}::{sub}', role)
set_layer(f'{P}::PAVILION', 'BG.Building')
model.Strings["ProgramDiagram\\settings"] = json.dumps(settings)
model.Write(sys.argv[3], 8)
json.dump(settings, open(sys.argv[3].rsplit('.', 1)[0] + '_settings.json', 'w'), indent=1)
print("\n".join(CHANGES))
