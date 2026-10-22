"""Riverfront park for the RIVER model, DRAFT_1 v4 (user 10-22): the earlier riverfront park outside the property line
(walks 12 ft and joined to the neighbours' riverside walkways, playful elements added), the plaza and the amphitheatre
inside it; nothing enters the building.

Usage: python3 waterfront8.py <fixture.json of in.3dm> <in.3dm> <out.3dm>
The previous park (PROJECT SITE::RIVERFRONT PARK::*) is removed first; the user's greens and the designed gardens
(PROJECT SITE::GARDENS) stay. River coordinates: s along the south water edge (west → east), u inland.
"""
import json, math, random, sys
sys.path.insert(0, __file__.rsplit('/', 1)[0])
import rhino3dm as r
from shapely.geometry import Polygon, MultiPolygon, LineString, Point, box
from shapely.ops import unary_union, linemerge, substring
from r21_lib import rounded, lines_of
from shapely import affinity
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

# ------------------------------------------------------------------ design (DRAFT_1 v4, user 10-22)
# Outside the property line: the riverfront park of the earlier rounds (sinuous river walk, S-curved garden walks,
# picnic and event lawns, flowering groves, rain garden, river steps, kayak launch, boardwalk), now with every walk
# 12 ft — the width of the neighbours' riverside walkways, which the river walk runs into at both ends — and playful
# elements: a splash pad, play hills, a hammock grove, stepping stones across the rain garden, river swings.
# Inside the property line: one hardscape plaza zoned by the user's greens, the amphitheatre round the river pavilion.
# Nothing enters the building: its rooms and the glass envelope at grade, grown by 3 ft, are cut off first.
from r20_envelope import envelope as _envelope
raw_fx = json.load(open(sys.argv[1]))['objects']
INDOOR = unary_union([_envelope(raw_fx, 0, 18)[0], build]).buffer(3)
keep = unary_union([o['fp'] for o in objs if (o['layer'] == 'greens' or o['layer'].startswith('PROJECT SITE::GARDENS::')) and o['z0'] < 3 and o['fp'] is not None
                    and o['fp'].geom_type in ('Polygon', 'MultiPolygon') and o['fp'].intersects(zone)])
pav_o = next(o for o in objs if o['name'] == 'RIVER PAVILION' and o['layer'].startswith('PROGRAM::'))
PAV = pav_o['fp']; PC = PAV.centroid; PR = math.sqrt(PAV.area / math.pi)
zone = zone.difference(INDOOR)
inside_all = zone.intersection(parcel).buffer(0)
outside = zone.difference(parcel).buffer(-0.3).buffer(0.3)
outside = unary_union([p for p in parts(outside) if p.area > 200])
WALK = 12.0                                         # the neighbours' riverside walkways are 12 ft

def strip(u0, u1, s_a, s_b, step=3.0):
    f0 = u0 if callable(u0) else (lambda s, v=u0: v)
    f1 = u1 if callable(u1) else (lambda s, v=u1: v)
    k = max(2, int((s_b - s_a) / step))
    ss = [s_a + (s_b - s_a) * i / k for i in range(k + 1)]
    return Polygon([at(s, f0(s)) for s in ss] + [at(s, f1(s)) for s in reversed(ss)]).buffer(0)

def su(p):
    """River coordinates (s, u) of a point."""
    s = E.project(Point(p)); x, y, nx, ny, _, _ = frame(s)
    return s, (p[0] - x) * nx + (p[1] - y) * ny

# Depth of the outside part at s (water's edge to the property line).
def u_pl(s):
    x, y, nx, ny, _, _ = frame(s)
    ray = LineString([(x, y), (x + nx * 400, y + ny * 400)])
    hit = ray.intersection(parcel.boundary)
    pts = [Point(c) for g in getattr(hit, 'geoms', [hit]) for c in getattr(g, 'coords', [])]
    return min((Point(x, y).distance(q) for q in pts), default=300.0)
_up = [(k * L / 120, u_pl(k * L / 120)) for k in range(121)]
def upl(s):
    w = [u for x, u in _up if abs(x - s) <= 25]
    return sum(w) / len(w)

# The neighbours' walkway ends (west: the one ending east of x = -100, east: the one starting at x = 708).
ends = {}
for o in objs:
    if o['layer'] != 'SITE CONTEXT::WALKWAY' or o['fp'] is None: continue
    b = o['fp'].bounds
    if b[2] < 20 and b[3] - b[1] < 80:
        ends['w'] = o['fp'].intersection(box(b[2] - 6, -1e4, b[2] + 1, 1e4)).centroid
    if b[0] > 600 and b[3] - b[1] < 80:
        ends['e'] = o['fp'].intersection(box(b[0] - 1, -1e4, b[0] + 6, 1e4)).centroid
uw = su((ends['w'].x, ends['w'].y))[1] if 'w' in ends else 44
ue = su((ends['e'].x, ends['e'].y))[1] if 'e' in ends else 44
def prom_u(s):
    base = 40 + 12 * math.sin(2 * math.pi * s / 230 + 0.7)
    base = min(base, upl(s) - WALK / 2 - 4)
    for s_end, u_end in ((0.0, uw), (L, ue)):
        w = max(0.0, 1 - abs(s - s_end) / 90)
        w = w * w * (3 - 2 * w)
        base = base * (1 - w) + u_end * w
    return base
promenade = band(None, prom_u, WALK, 0, L).intersection(zone)
links = []
for key, s_end in (('w', 0.0), ('e', L)):
    if key in ends:
        links.append(LineString([at(s_end, prom_u(s_end)), (ends[key].x, ends[key].y)]).buffer(WALK / 2, cap_style=2).difference(river))
promenade = unary_union([promenade] + links)
boardwalk = band(None, lambda s: 5.0, 9, 0.03 * L, 0.97 * L).intersection(outside)
# garden walks: S-curves from the property line down to the river walk, 12 ft, past the line into the plaza
walks = []
walk_s = [0.22 * L, 0.52 * L, 0.80 * L]
for k, s0 in enumerate(walk_s):
    ui = upl(s0) + 6; up_ = prom_u(s0 + 36)
    pts = []
    for i in range(41):
        t = i / 40; e = 3 * t * t - 2 * t ** 3
        s = s0 + 36 * e + 10 * math.sin(math.pi * t) * (1 if k % 2 else -1)
        pts.append(at(s, ui + (up_ - ui) * t))
    walks.append(LineString(pts).buffer(WALK / 2, cap_style=2))
walks = unary_union(walks).intersection(zone).difference(promenade)
steps = []
for s0 in (0.15 * L, 0.46 * L, 0.88 * L):
    for j, (u0, u1) in enumerate([(-12, -6.5), (-5.5, 0), (1, 6.5), (7.5, 13)]):
        w = 74 - 6 * j
        steps.append(strip(u0, u1, s0 - w / 2, s0 + w / 2, step=2))
steps = unary_union(steps)
kayak_s = 0.36 * L
kayak = unary_union([strip(-52, 0, kayak_s - 5, kayak_s + 5, step=2), strip(-65, -51, kayak_s - 16, kayak_s + 16, step=2)])
boardwalk = boardwalk.difference(steps).difference(kayak).difference(promenade)
hardO = unary_union([promenade, walks, boardwalk, steps.intersection(outside)])
free = outside.difference(hardO.buffer(3)).difference(keep.buffer(3))
lawn_geoms = []
for s, nm, ls, seed in [(0.33 * L, 'PICNIC LAWN', 120, 3), (0.64 * L, 'EVENT LAWN', 140, 5), (0.90 * L, 'PICNIC LAWN', 90, 9)]:
    lo, hi = prom_u(s) + WALK / 2 + 6, upl(s) - 6
    if hi - lo < 20: continue
    g = blob(s, (lo + hi) / 2, ls, hi - lo, 0.0, seed=seed).intersection(free)
    g = max(parts(g), key=lambda p: p.area) if parts(g) else g
    if not g.is_empty: lawn_geoms.append((nm, g))
lawnU = unary_union([g for _, g in lawn_geoms])
rain = band(None, lambda s: prom_u(s) + WALK / 2 + 9 + 4 * math.sin(2 * math.pi * s / 90), 8, 0.38 * L, 0.56 * L, step=3)
rain = rain.intersection(free).difference(lawnU.buffer(3))
# ---- playful elements
play = {}
sp = 0.30 * L                                          # splash pad on the river walk's landward side, near the plaza
c = Point(at(sp, prom_u(sp) + WALK / 2 + 14))
play['SPLASH PAD'] = c.buffer(13, 48).intersection(outside).difference(hardO)
hills = []                                             # play hills on the event lawn
for nm, g in lawn_geoms:
    if nm == 'EVENT LAWN':
        q = g.representative_point()
        for k, (dx, dy, r_) in enumerate([(-14, 4, 11), (6, -6, 9), (18, 8, 7)]):
            hills.append(Point(q.x + dx, q.y + dy).buffer(r_, 32))
play['PLAY HILLS'] = unary_union(hills).intersection(lawnU) if hills else Polygon()
stones = []                                            # stepping stones across the rain garden
if not rain.is_empty:
    mid = 0.47 * L
    for k in range(6):
        p = Point(at(mid + (k % 2) * 1.5, prom_u(mid) + WALK / 2 + 4 + k * 3.2))
        stones.append(affinity.rotate(p.buffer(1.4, 12), 0).buffer(0))
play['STEPPING STONES'] = unary_union(stones)
swing_s = 0.72 * L                                     # river swings: a pergola frame on the river walk's edge
sc = at(swing_s, prom_u(swing_s) + WALK / 2 + 6)
t_ = frame(swing_s); ang_ = math.degrees(math.atan2(t_[5], t_[4]))
swing = affinity.rotate(box(sc[0] - 15, sc[1] - 4, sc[0] + 15, sc[1] + 4), ang_, origin=sc).intersection(outside.difference(hardO))
play['RIVER SWINGS'] = swing
grove_s = 0.10 * L                                     # hammock grove: trees in pairs with hammocks between
groves = [blob(grove_s, (prom_u(grove_s) + upl(grove_s)) / 2 + 4, 60, 30, seed=11).intersection(free).difference(lawnU.buffer(4))]
groves.append(blob(0.56 * L, (prom_u(0.56 * L) + upl(0.56 * L)) / 2 + 10, 60, 28, seed=13).intersection(free).difference(lawnU.buffer(4)).difference(rain.buffer(3)))
grove = unary_union(groves)
playU = unary_union([g for g in play.values() if not g.is_empty])
meadow = free.difference(unary_union([lawnU, rain, grove, playU]).buffer(0.5))

# ---- amphitheatre round the pavilion (landward side), clear of the building
ang = math.degrees(math.atan2(PC.y - at(su((PC.x, PC.y))[0], 0)[1], PC.x - at(su((PC.x, PC.y))[0], 0)[0]))
def sector(r0, r1, a0, a1, n=64):
    out = [(PC.x + r1 * math.cos(math.radians(a0 + (a1 - a0) * k / n)), PC.y + r1 * math.sin(math.radians(a0 + (a1 - a0) * k / n))) for k in range(n + 1)]
    inn = [(PC.x + r0 * math.cos(math.radians(a0 + (a1 - a0) * k / n)), PC.y + r0 * math.sin(math.radians(a0 + (a1 - a0) * k / n))) for k in range(n, -1, -1)]
    return Polygon(out + inn).buffer(0)
R0, R1, A = PR + 9, PR + 37, 70
amph = sector(R0, R1, ang - A, ang + A)
aisles = unary_union([LineString([(PC.x, PC.y), (PC.x + 200 * math.cos(math.radians(ang + d)), PC.y + 200 * math.sin(math.radians(ang + d)))]).buffer(2.5) for d in (-25, 25)])
avail = zone.difference(keep.buffer(2))
amph = amph.intersection(avail)
seats = amph.difference(aisles)
tiers = []
r_ = R0 + 3.5
while r_ < R1 - 0.5:
    arc = LineString([(PC.x + r_ * math.cos(math.radians(ang - A + 2 * A * k / 80)), PC.y + r_ * math.sin(math.radians(ang - A + 2 * A * k / 80))) for k in range(81)])
    tiers += [l for l in lines_of(arc.intersection(seats.buffer(-0.3))) if l.length > 4]
    r_ += 3.5
stage_apron = Point(PC.x, PC.y).buffer(PR + 6, 64).difference(PAV.buffer(0.2)).intersection(avail)
amphU = unary_union([amph, stage_apron]).buffer(0.2)
for nm in list(play): play[nm] = play[nm].difference(amphU)
meadow = meadow.difference(amphU); grove = grove.difference(amphU); rain = rain.difference(amphU)
lawn_geoms = [(n, g.difference(amphU)) for n, g in lawn_geoms]
promenade = promenade.difference(amphU); walks = walks.difference(amphU)
# ---- the plaza inside the property line
inside = inside_all.difference(keep.buffer(0.2)).difference(PAV.buffer(0.2)).difference(amphU).difference(walks)
plaza = inside.buffer(2, join_style=1).buffer(-4, join_style=1).buffer(2, join_style=1).intersection(inside)
plaza = unary_union([p for p in parts(plaza) if p.area > 50])
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
for g, sub, name in [(boardwalk, 'BOARDWALK', 'BOARDWALK'), (promenade, 'PROMENADE', 'RIVER WALK'), (walks, 'GARDEN WALK', 'GARDEN WALK'),
                     (steps, 'RIVER STEPS', 'RIVER STEPS'), (kayak, 'KAYAK LAUNCH', 'KAYAK LAUNCH'), (rain, 'RAIN GARDEN', 'RAIN GARDEN'),
                     (grove, 'GROVE', 'HAMMOCK GROVE'), (meadow, 'GROVE', 'RIVER MEADOW'), (plaza, 'PLAZA', 'RIVER PLAZA'),
                     (seats, 'SEATING STEPS', 'AMPHITHEATRE'), (stage_apron, 'STAGE', ''), (amph.difference(seats), 'PLAZA', ''),
                     (play['SPLASH PAD'], 'WATER PLAY', 'SPLASH PAD'), (play['PLAY HILLS'], 'PLAY', 'PLAY HILLS'),
                     (play['STEPPING STONES'], 'PLAY', ''), (play['RIVER SWINGS'], 'PLAY', 'RIVER SWINGS')]:
    area(g, f'{P}::{sub}', name, min_area=2)
    CHANGES.append(f"+ {(name or sub).lower()}: {g.area:,.0f} sf")
for nm, g in lawn_geoms:
    area(g.difference(play['PLAY HILLS'].buffer(0.1)), P + '::LAWN', nm)
for l in tiers:
    model.Objects.AddCurve(r.PolylineCurve(pl(list(l.coords), 0.05)), attrs(P + '::FURNITURE', 'SEATING TIER'))
F_ = P + '::FURNITURE'
def line(coords, nm): model.Objects.AddCurve(r.PolylineCurve(pl(list(coords), 0.05)), attrs(F_, nm))
# play hills: contour rings; splash pad: jets; swings: the frame's beams and four seats
for h in parts(play['PLAY HILLS']):
    for k in (2.5, 5.0):
        for q in parts(h.buffer(-k)): line(q.exterior.coords, 'PLAY HILL')
for p in parts(play['SPLASH PAD']):
    c0 = p.centroid
    for k in range(10):
        a = 2 * math.pi * k / 10
        model.Objects.AddCircle(r.Circle(r.Point3d(c0.x + 6 * math.cos(a), c0.y + 6 * math.sin(a), 0.05), 0.4), attrs(F_, 'WATER JET'))
for p in parts(play['RIVER SWINGS']):
    mr = list(p.minimum_rotated_rectangle.exterior.coords)
    line(mr, 'SWING FRAME')
    a, b = (mr[0], mr[1]) if math.dist(mr[0], mr[1]) > math.dist(mr[1], mr[2]) else (mr[1], mr[2])
    for k in range(4):
        t = (k + 0.5) / 4; q = (a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t)
        c0 = p.centroid; m = (q[0] + (c0.x - q[0]) * 0.9, q[1] + (c0.y - q[1]) * 0.9)
        line([(m[0] - 1, m[1]), (m[0] + 1, m[1])], 'SWING')
CHANGES.append(f"+ amphitheatre: {len(tiers)} tier lines; play: splash pad, {len(parts(play['PLAY HILLS']))} play hills, {len(stones)} stepping stones, river swings")

trees = []
def plant(region, spacing, cap, rad=(8, 10.5)):
    g = region.buffer(-4)
    if g.is_empty: return
    minx, miny, maxx, maxy = g.bounds; n = 0
    for _ in range(3000):
        if n >= cap: break
        q = Point(rng.uniform(minx, maxx), rng.uniform(miny, maxy))
        if g.contains(q) and all(q.distance(t) > spacing for t in trees): trees.append(q); n += 1
plant(grove, 16, 14)
# a loose row along the river walk's landward edge, clear of every walk and play area
s = 8.0
while s < L - 8:
    q = Point(at(s, prom_u(s) + WALK / 2 + 6 + rng.uniform(0, 5)))
    if meadow.buffer(-1).contains(q) and all(q.distance(t) > 22 for t in trees): trees.append(q)
    s += rng.uniform(26, 36)
plant(meadow, 40, 18)
for q in trees:
    model.Objects.AddCircle(r.Circle(r.Point3d(q.x, q.y, 0), rng.uniform(8, 10.5)), attrs(P + '::FLOWERING TREES'))
# hammocks between neighbouring trees of the hammock grove
gtrees = [q for q in trees if grove.buffer(1).contains(q)]
used = set(); nh = 0
for i, a in enumerate(gtrees):
    b = min((q for j, q in enumerate(gtrees) if j != i and j not in used), key=lambda q: a.distance(q), default=None)
    if b is None or i in used or not (12 < a.distance(b) < 24): continue
    used.update({i, gtrees.index(b)}); nh += 1
    line([(a.x, a.y), (b.x, b.y)], 'HAMMOCK')
CHANGES.append(f"+ trees {len(trees)}, hammocks {nh}")
nb = 0
edge = LineString([at(s, prom_u(s) + WALK / 2 - 1.2) for s in [L * k / 300 for k in range(301)]])
s = 20.0
while s + 14 < edge.length:
    seg = substring(edge, s, s + 14)
    if promenade.buffer(-0.3).contains(seg): line(seg.coords, 'BENCH'); nb += 1
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
        c0 = Point(rng.uniform(minx, maxx), rng.uniform(miny, maxy))
        if not g.contains(c0) or any(c0.distance(q) < 6 for q in people): continue
        k = rng.randint(2, 4) if groups else 1
        for j in range(k):
            people.append(Point(c0.x + 2.6 * math.cos(2 * math.pi * j / k), c0.y + 2.6 * math.sin(2 * math.pi * j / k)) if k > 1 else c0)
        n -= 1
scatter(promenade, 30); scatter(boardwalk, 8); scatter(walks, 10); scatter(plaza, 26); scatter(seats, 24); scatter(lawnU, 8, groups=True)
scatter(play['SPLASH PAD'], 6); scatter(play['PLAY HILLS'].buffer(4), 5); scatter(steps.intersection(zone.union(river)), 10)
for q in people:
    model.Objects.AddCircle(r.Circle(r.Point3d(q.x, q.y, 0), 1.0), attrs(P + '::PEOPLE'))
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
                  ('BOARDWALK', 'NC.Hardscape'), ('SEATING STEPS', 'NC.Hardscape'), ('STAGE', 'NC.Hardscape'), ('PLAY', 'NC.Hardscape'),
                  ('WATER PLAY', 'NC.Water'),
                  ('KAYAK LAUNCH', 'NC.Hardscape'), ('RAIN GARDEN', 'NC.Water'), ('LAWN', 'NC.Green'), ('GROVE', 'NC.Green'),
                  ('FLOWERING TREES', 'NC.Tree'), ('PEOPLE', 'Entourage')]:
    set_layer(f'{P}::{sub}', role)
set_layer(f'{P}::PAVILION', 'BG.Building')
set_layer(f'{P}::FURNITURE', 'NC.Furniture', mode='line')
model.Strings["ProgramDiagram\\settings"] = json.dumps(settings)
model.Write(sys.argv[3], 8)
json.dump(settings, open(sys.argv[3].rsplit('.', 1)[0] + '_settings.json', 'w'), indent=1)
print("\n".join(CHANGES))
