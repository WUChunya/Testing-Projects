"""CAMPUS 2026-10-22: the green riverside parcel (SURROUNDING::GRASS) designed as a park after the RIVER 1022
riverfront park (same layers, materials and elements; deliverables/2026-10-22_RIVER_1022).

Usage: python3 -I c22_park.py <fixture.json of in.3dm> <in.3dm> <out.3dm>
Park = the GRASS area inside the parcel; the LOT ring round it stays as the perimeter sidewalk. y runs south → north
along the parcel, u east from its west (river-side) edge.
- RIVER WALK: 12 ft, sinuous, along the river side, both ends into the perimeter walk; two RIVER LINKs (12 ft) cross
  the bank planting to the riverside walkway (SITE CONTEXT::WALKWAY), as the RIVER walks join the neighbours'.
- GARDEN WALKs: three S-curved 12 ft walks from entry plazas on the street (facing the building's wings) to the river walk.
- Entry plazas (PLAZA) at the street; a SPLASH PAD with a ring of jets in the middle one.
- EVENT LAWN with PLAY HILLS (contours), two PICNIC LAWNs, a RAIN GARDEN with STEPPING STONES along the river walk,
  HAMMOCK GROVE (hammocks between paired trees), FLOWERING GROVE, RIVER SWINGS (frame + 4 swings), a timber RIVER
  OVERLOOK on the river walk, three round PICNIC courts (pavers, C-shaped bench, tables, planting ring, trees).
- The rest is RIVER MEADOW; trees along the walks and in the groves, benches on the river walk, people.
The GRASS solid goes to LEFTOVERS (the park layers replace it).
"""
import sys, math, random, json
sys.path.insert(0, __file__.rsplit('/', 1)[0])
from r21_lib import *
from shapely.ops import substring

M = Model(sys.argv[1], sys.argv[2])
rng = random.Random(1022)
Z = 1.55                                   # parcel ground at +1'-6"
P = 'PROJECT SITE::RIVERFRONT PARK'
C_ = 'PROJECT SITE::PICNIC'
WALK = 12.0


def fp(o):
    if o.get('mesh'): return footprint(o)
    pts = o.get('points')
    xy = [(x / FT, y / FT) for x, y, _ in pts]
    return Polygon(xy).buffer(0) if o['isClosed'] else LineString(xy)


gid = M.find('31147ee4')
G = fp(M.byid[gid])
PARCEL = fp(M.fo('35207315'))
RING = PARCEL.difference(G)
WALKWAY = unary_union([fp(o) for o in M.fx if o['layer'] == 'SITE CONTEXT::WALKWAY'])
y0, y1 = G.bounds[1], G.bounds[3]


def xs(y):
    """West / east edge of the park at y."""
    h = LineString([(G.bounds[0] - 50, y), (G.bounds[2] + 50, y)]).intersection(G)
    pts = [c[0] for g in getattr(h, 'geoms', [h]) for c in getattr(g, 'coords', [])]
    return (min(pts), max(pts)) if pts else (None, None)


def at(y, u):
    return (xs(y)[0] + u, y)


def curve(fx_, ya, yb, n=120):
    return LineString([(fx_(ya + (yb - ya) * k / n), ya + (yb - ya) * k / n) for k in range(n + 1)])


def blob(c, a, b, rot=0.0, seed=0, n=48, wob=0.12):
    rr = random.Random(seed); ph = [rr.uniform(0, 6.28) for _ in range(3)]
    pts = []
    for i in range(n):
        t = 2 * math.pi * i / n
        k = 1 + wob * (math.sin(2 * t + ph[0]) * .6 + math.sin(3 * t + ph[1]) * .3 + math.sin(5 * t + ph[2]) * .1)
        x, y = a / 2 * math.cos(t) * k, b / 2 * math.sin(t) * k
        pts.append((c[0] + x * math.cos(rot) - y * math.sin(rot), c[1] + x * math.sin(rot) + y * math.cos(rot)))
    return Polygon(pts).buffer(0)


def area(g, sub, name="", dz=0.0):
    return M.add_area(g, f'{P}::{sub}', name, Z + dz, min_area=2)


def line(coords, nm, path=None):
    M.add_line(LineString(coords), Z + 0.05, path or f'{P}::FURNITURE', nm)


def circle(c, r_, path, name=""):
    return str(M.m.Objects.AddCircle(r.Circle(r.Point3d(c[0], c[1], Z + 0.05), r_), M.attrs(path, name)))


# ------------------------------------------------------------------ walks
def rw_x(y):
    xw, xe = xs(min(max(y, y0 + 1), y1 - 1))
    return xw + 28 + 9 * math.sin(2 * math.pi * (y - y0) / 240 + 0.4)


river_walk = curve(rw_x, y0 - 18, y1 + 18).buffer(WALK / 2, cap_style=2).intersection(PARCEL.buffer(-1))
# links across the bank planting to the riverside walkway (west), as in RIVER
links = []
for yl in (2150.0, 2470.0):
    a = (rw_x(yl), yl)
    tgt = WALKWAY.boundary.interpolate(WALKWAY.boundary.project(Point(a[0] - 120, yl)))
    links.append(LineString([a, (a[0] - 25, yl + 2), (tgt.x + 4, tgt.y)]).buffer(WALK / 2, cap_style=2, join_style=1))
links = unary_union(links).difference(WALKWAY).difference(river_walk)
promenade = unary_union([river_walk, links])

ENTRY_Y = [2150.0, 2330.0, 2510.0]                    # facing the building's south wing, courtyard, north wing
plazas, walks = [], []
for k, ye in enumerate(ENTRY_Y):
    xe = xs(ye)[1]
    plazas.append(rounded(box(xe - 46, ye - 22, xe + 30, ye + 22), 8).intersection(PARCEL.buffer(-0.5)))
    ys = ye + (24 if k % 2 == 0 else -24)                # S-curve: lands a little north / south on the river walk
    pts = []
    for i in range(41):
        t = i / 40; e = 3 * t * t - 2 * t ** 3
        y = ye + (ys - ye) * e + 14 * math.sin(math.pi * t) * (1 if k % 2 else -1)
        x = (xe - 40) + (rw_x(ys) - (xe - 40)) * t
        pts.append((x, y))
    walks.append(LineString(pts).buffer(WALK / 2, cap_style=2))
plaza = unary_union(plazas).difference(promenade)
walks = unary_union(walks).intersection(G).difference(promenade).difference(plaza)
HARD = unary_union([promenade, walks, plaza])

# timber overlook on the river walk's west side, mid-park
yo = 2300.0
ov_c = (xs(yo)[0] + 8, yo)
overlook = Point(ov_c).buffer(18, 48).intersection(box(ov_c[0] - 30, yo - 30, rw_x(yo) - WALK / 2, yo + 30)).intersection(G).difference(promenade)
HARD = unary_union([HARD, overlook])
free = G.difference(HARD.buffer(3))


def part_near(g, c):
    ps = parts(g)
    return min(ps, key=lambda p: p.distance(Point(c))) if ps else Polygon()


# ------------------------------------------------------------------ lawns, rain garden, groves
lawns = []
for c, a, b, nm, seed in [((2905, 2240), 150, 120, 'EVENT LAWN', 5), ((2880, 2085), 120, 60, 'PICNIC LAWN', 3),
                          ((2900, 2420), 130, 95, 'PICNIC LAWN', 9), ((2880, 2590), 120, 40, 'PICNIC LAWN', 7)]:
    g = part_near(blob(c, a, b, seed=seed).intersection(free), c)
    if not g.is_empty: lawns.append((nm, g))
lawnU = unary_union([g for _, g in lawns])
# rain garden: a band on the river walk's landward side, y 2340 – 2470 (the walk's swale)
rain = curve(lambda y: rw_x(y) + WALK / 2 + 12, 2345, 2475).buffer(7, cap_style=1).intersection(free).difference(lawnU.buffer(3))
play = {}
ev = dict(lawns)['EVENT LAWN']; q = ev.representative_point()
play['PLAY HILLS'] = unary_union([Point(q.x + dx, q.y + dy).buffer(r_, 32) for dx, dy, r_ in [(16, 18, 12), (34, -4, 9), (12, -22, 8)]]).intersection(ev.buffer(-4))
sp_c = (xs(2330)[1] - 18, 2330)
play['SPLASH PAD'] = Point(sp_c).buffer(13, 48).intersection(plaza)
stones = []
for k in range(7):
    yk = 2380 + k * 4.5
    stones.append(Point(rw_x(yk) + WALK / 2 + 3 + k * 2.4, yk).buffer(1.4, 12))
play['STEPPING STONES'] = unary_union(stones).intersection(rain.buffer(1))
ysw = 2215.0                                            # river swings between the river walk and the river edge
sc = (rw_x(ysw) - WALK / 2 - 7, ysw)
play['RIVER SWINGS'] = box(sc[0] - 4, sc[1] - 15, sc[0] + 4, sc[1] + 15).intersection(free).difference(rain.buffer(2))
for nm in ('PLAY HILLS', 'STEPPING STONES'): pass
hammock = part_near(blob((2815, 2110), 110, 80, seed=11).intersection(free).difference(lawnU.buffer(4)), (2815, 2110))
flowering = part_near(blob((2840, 2560), 150, 60, seed=13).intersection(free).difference(lawnU.buffer(4)), (2840, 2560))
playU = unary_union([g for g in play.values() if not g.is_empty])

# ------------------------------------------------------------------ picnic courts (RIVER 1022 picnic reference)
courts = []
def court(c, R, toward):
    ang = math.degrees(math.atan2(toward[1] - c[1], toward[0] - c[0]))
    pad = Point(c).buffer(R * 0.78, 64)
    def arc_band(r0, r1, a0, a1, n=48):
        o_ = [(c[0] + r1 * math.cos(math.radians(a0 + (a1 - a0) * k / n)), c[1] + r1 * math.sin(math.radians(a0 + (a1 - a0) * k / n))) for k in range(n + 1)]
        i_ = [(c[0] + r0 * math.cos(math.radians(a0 + (a1 - a0) * k / n)), c[1] + r0 * math.sin(math.radians(a0 + (a1 - a0) * k / n))) for k in range(n, -1, -1)]
        return Polygon(o_ + i_).buffer(0)
    bench = arc_band(R * 0.56, R * 0.56 + 1.6, ang + 70, ang + 290)
    gap = LineString([c, (c[0] + 2 * R * math.cos(math.radians(ang)), c[1] + 2 * R * math.sin(math.radians(ang)))]).buffer(max(2.5, R * 0.3), cap_style=2)
    ring = Point(c).buffer(R + 2.0, 64).difference(pad).difference(gap)
    M.add_area(pad.difference(bench.buffer(0.05)), f'{C_}::COURT', 'PICNIC', Z)
    M.add_area(bench, f'{C_}::BENCH', '', Z)
    M.add_area(ring, f'{C_}::PLANTING', '', Z)
    for k in range(3):
        a = math.radians(ang + 120 + k * 60)
        circle((c[0] + (R + 0.8) * math.cos(a), c[1] + (R + 0.8) * math.sin(a)), rng.uniform(5, 7), f'{C_}::TREES')
    for dx, dy in [(-R * 0.18, R * 0.12), (R * 0.2, -R * 0.1)]:
        t = (c[0] + dx, c[1] + dy)
        circle(t, 1.8, f'{C_}::FURNITURE', 'TABLE')
        for k in range(4):
            a = math.radians(45 + 90 * k)
            circle((t[0] + 3.0 * math.cos(a), t[1] + 3.0 * math.sin(a)), 0.7, f'{C_}::FURNITURE', 'STOOL')
    for k in range(rng.randint(2, 4)):
        a = rng.uniform(0, 2 * math.pi); rr = rng.uniform(1.5, R * 0.45)
        circle((c[0] + rr * math.cos(a), c[1] + rr * math.sin(a)), 1.0, f'{C_}::PEOPLE')
    return Point(c).buffer(R + 2.0, 64)


def court_site(c0, R):
    """Nearest spot to c0 where the whole court lies in free meadow (not on a lawn / grove / play area)."""
    room = free.difference(unary_union([lawnU, rain, hammock, flowering, playU] + courts).buffer(2))
    best = None
    for dx in range(-40, 41, 4):
        for dy in range(-40, 41, 4):
            p = Point(c0[0] + dx, c0[1] + dy)
            if room.contains(p.buffer(R + 2.5)) and (best is None or p.distance(Point(c0)) < best.distance(Point(c0))): best = p
    return best


for c0 in [(2960, 2190), (2950, 2370), (2950, 2560)]:
    R = 12.0
    p = court_site(c0, R)
    if p is None: M.log.append(f"no room for a picnic court near {c0}"); continue
    near = walks.boundary.interpolate(walks.boundary.project(p)) if not walks.is_empty else Point(p.x, p.y + 10)
    courts.append(court((p.x, p.y), R, (near.x, near.y)))
courtU = unary_union(courts)
meadow = G.difference(unary_union([HARD, lawnU, rain, hammock, flowering, playU, courtU]).buffer(0.3))

# ------------------------------------------------------------------ write areas
# Stacked by height instead of cut out (a closed curve cannot carry holes: cutting would draw seams):
# meadow = the whole park at +0.00, lawns / groves / rain garden +0.02, walks, plazas, play +0.04.
area(G, 'GROVE', 'RIVER MEADOW', dz=-0.05)
M.log.append(f"+ river meadow (base): {meadow.area:,.0f} sf uncovered")
for g, sub, name in [(hammock, 'GROVE', 'HAMMOCK GROVE'), (flowering, 'GROVE', 'FLOWERING GROVE'), (rain, 'RAIN GARDEN', 'RAIN GARDEN')]:
    area(g, sub, name, dz=-0.03); M.log.append(f"+ {name.lower()}: {g.area:,.0f} sf")
for nm, g in lawns:
    area(g, 'LAWN', nm, dz=-0.03); M.log.append(f"+ {nm.lower()}: {g.area:,.0f} sf")
for g, sub, name in [(river_walk, 'PROMENADE', 'RIVER WALK'), (links, 'PROMENADE', 'RIVER LINK'), (walks, 'GARDEN WALK', 'GARDEN WALK'),
                     (plaza, 'PLAZA', 'PARK ENTRY'), (overlook, 'BOARDWALK', 'RIVER OVERLOOK'),
                     (play['PLAY HILLS'], 'PLAY', 'PLAY HILLS'), (play['STEPPING STONES'], 'PLAY', ''), (play['RIVER SWINGS'], 'PLAY', 'RIVER SWINGS')]:
    area(g, sub, name)
    M.log.append(f"+ {(name or sub).lower()}: {g.area:,.0f} sf")

area(play['SPLASH PAD'], 'WATER PLAY', 'SPLASH PAD', dz=0.03)
for h in parts(play['PLAY HILLS']):
    for k in (2.5, 5.0):
        for q in parts(h.buffer(-k)): line(q.exterior.coords, 'PLAY HILL')
for p in parts(play['SPLASH PAD']):
    c0 = p.centroid
    for k in range(10):
        a = 2 * math.pi * k / 10
        circle((c0.x + 6 * math.cos(a), c0.y + 6 * math.sin(a)), 0.4, f'{P}::FURNITURE', 'WATER JET')
for p in parts(play['RIVER SWINGS']):
    mr = list(p.minimum_rotated_rectangle.exterior.coords)
    line(mr, 'SWING FRAME')
    a, b = (mr[0], mr[1]) if math.dist(mr[0], mr[1]) > math.dist(mr[1], mr[2]) else (mr[1], mr[2])
    c0 = p.centroid
    for k in range(4):
        t = (k + 0.5) / 4; q = (a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t)
        m = (q[0] + (c0.x - q[0]) * 0.9, q[1] + (c0.y - q[1]) * 0.9)
        line([(m[0] - 1, m[1]), (m[0] + 1, m[1])], 'SWING')
# overlook deck boards
for p in parts(overlook):
    xb = p.bounds[0] + 3
    while xb < p.bounds[2]:
        for l in lines_of(LineString([(xb, p.bounds[1] - 1), (xb, p.bounds[3] + 1)]).intersection(p.buffer(-0.3))): line(l.coords, 'DECK BOARD')
        xb += 3

# ------------------------------------------------------------------ trees, hammocks, benches, people
trees = []
TREE_OK = unary_union([meadow, hammock, flowering]).buffer(-4)
def plant(region, spacing, cap):
    g = region.buffer(-4).intersection(TREE_OK)
    if g.is_empty: return
    minx, miny, maxx, maxy = g.bounds; n = 0
    for _ in range(4000):
        if n >= cap: break
        q = Point(rng.uniform(minx, maxx), rng.uniform(miny, maxy))
        if g.contains(q) and all(q.distance(t) > spacing for t in trees): trees.append(q); n += 1
plant(hammock, 15, 14)
plant(flowering, 16, 14)
y = y0 + 12                                             # loose row along the river walk, both sides
while y < y1 - 12:
    for side in (-1, 1):
        q = Point(rw_x(y) + side * (WALK / 2 + 7 + rng.uniform(0, 4)), y + rng.uniform(-3, 3))
        if TREE_OK.contains(q) and all(q.distance(t) > 20 for t in trees): trees.append(q)
    y += rng.uniform(24, 32)
for w in parts(walks):                                  # garden walks lined with trees
    edge = w.exterior
    s = 6.0
    while s < edge.length:
        e = edge.interpolate(s); c = w.centroid
        q = Point(e.x + (e.x - c.x) * 0.0, e.y)
        dx, dy = e.x - w.representative_point().x, e.y - w.representative_point().y
        n = math.hypot(dx, dy) or 1
        q = Point(e.x + dx / n * 7, e.y + dy / n * 7)
        if TREE_OK.contains(q) and all(q.distance(t) > 22 for t in trees): trees.append(q)
        s += 24
plant(meadow, 27, 60)
for q in trees:
    circle((q.x, q.y), rng.uniform(8, 10.5), f'{P}::FLOWERING TREES')
gtrees = [q for q in trees if hammock.buffer(1).contains(q)]
used, nh = set(), 0
for i, a in enumerate(gtrees):
    if i in used: continue
    cand = [(a.distance(q), j) for j, q in enumerate(gtrees) if j != i and j not in used and 12 < a.distance(q) < 24]
    if not cand: continue
    j = min(cand)[1]; used.update({i, j}); nh += 1
    line([(a.x, a.y), (gtrees[j].x, gtrees[j].y)], 'HAMMOCK')
M.log.append(f"+ trees {len(trees)}, hammocks {nh}, picnic courts {len(courts)}")
nb = 0
edge = curve(lambda y: rw_x(y) + WALK / 2 - 1.2, y0, y1, 300)
s = 20.0
while s + 14 < edge.length:
    seg = substring(edge, s, s + 14)
    if river_walk.buffer(-0.3).contains(seg) and not overlook.buffer(4).intersects(seg): line(seg.coords, 'BENCH'); nb += 1
    s += 42
M.log.append(f"+ benches {nb}")
people = []
def scatter(g, n, groups=False):
    g = g.buffer(-2)
    if g.is_empty: return
    minx, miny, maxx, maxy = g.bounds; tries = 0
    while n > 0 and tries < 4000:
        tries += 1
        c0 = Point(rng.uniform(minx, maxx), rng.uniform(miny, maxy))
        if not g.contains(c0) or any(c0.distance(q) < 6 for q in people): continue
        k = rng.randint(2, 4) if groups else 1
        for j in range(k):
            people.append(Point(c0.x + 2.6 * math.cos(2 * math.pi * j / k), c0.y + 2.6 * math.sin(2 * math.pi * j / k)) if k > 1 else c0)
        n -= 1
scatter(river_walk, 22); scatter(walks, 10); scatter(plaza, 12); scatter(lawnU, 10, groups=True)
scatter(play['SPLASH PAD'], 5); scatter(play['PLAY HILLS'].buffer(4), 5); scatter(overlook, 4)
for q in people: circle((q.x, q.y), 1.0, f'{P}::PEOPLE')
M.log.append(f"+ people {len(people)}")

M.retire(gid)
M.log.append("GRASS solid 31147ee4 → LEFTOVERS (replaced by the park)")

# ------------------------------------------------------------------ roles (as RIVER 1022)
M.role(P, 'Ignore', mode=''); M.role('PROJECT SITE', 'Ignore', mode=''); M.role(C_, 'Ignore', mode='')
for sub, role in [('PROMENADE', 'NC.Hardscape'), ('GARDEN WALK', 'NC.Hardscape'), ('PLAZA', 'NC.Hardscape'), ('BOARDWALK', 'NC.Hardscape'),
                  ('PLAY', 'NC.Hardscape'), ('WATER PLAY', 'NC.Water'), ('RAIN GARDEN', 'NC.Water'), ('LAWN', 'NC.Green'),
                  ('GROVE', 'NC.Green'), ('FLOWERING TREES', 'NC.Tree'), ('PEOPLE', 'Entourage')]:
    M.role(f'{P}::{sub}', role)
M.role(f'{P}::FURNITURE', 'NC.Furniture', mode='line')
for path, role, mode in [(f'{C_}::COURT', 'NC.Hardscape', 'color'), (f'{C_}::BENCH', 'NC.Hardscape', 'color'), (f'{C_}::PLANTING', 'NC.Green', 'color'),
                         (f'{C_}::TREES', 'NC.Tree', 'color'), (f'{C_}::FURNITURE', 'NC.Furniture', 'line'), (f'{C_}::PEOPLE', 'Entourage', 'color')]:
    M.role(path, role, mode=mode)

M.write(sys.argv[3])
json.dump(M.st, open(sys.argv[3].rsplit('.', 1)[0] + '_settings.json', 'w'), indent=1)
print("\n".join(M.log))
