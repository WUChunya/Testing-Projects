"""RIVER DRAFT_1 step 3: the parcel ground the roads, greens, gardens, park and building leave is laid out as named
paved areas (PROJECT SITE::PLAZA) with trees, benches, bike racks and loading bays.

Usage: python3 -I d1_plaza.py <fixture.json of in.3dm> <in.3dm> <out.3dm>
"""
import sys, math, random
sys.path.insert(0, __file__.rsplit('/', 1)[0])
from r21_lib import *
from d1_left import leftover

M = Model(sys.argv[1], sys.argv[2])
rng = random.Random(10212)
left, building, parcel = leftover(sys.argv[1])
P = 'PROJECT SITE'
M.role(f'{P}::PLAZA', 'NC.Hardscape')
M.role(f'{P}::PLAZA TREES', 'NC.Tree')
M.role(f'{P}::SITE FURNITURE', 'NC.Furniture', mode='line')
ZONES = [   # name, region (first match wins)
    ('LOADING', box(185, -80, 300, 60)),
    ('SECONDARY DROP OFF PLAZA', box(-50, -80, 185, 50)),
    ('ACCESS TO RIVER WALK', box(-50, 50, 185, 240)),
    ('SOUTH WALK', box(300, -80, 640, 40)),
    ('EAST WALK', box(600, -80, 700, 540)),
    ('AUDITORIUM FORECOURT', box(480, 500, 640, 600)),
]
taken = Polygon()
areas = {}
for name, reg in ZONES:
    g = left.intersection(reg).difference(taken)
    g = unary_union([p for p in parts(g) if p.area > 60])
    if g.is_empty: continue
    areas[name] = g; taken = taken.union(reg)
rest = unary_union([p for p in parts(left.difference(taken)) if p.area > 60])
for name, g in areas.items():
    M.add_area(g, f'{P}::PLAZA', name)
    M.log.append(f"{name}: {g.area:,.0f} sf")
M.add_area(rest, f'{P}::PLAZA', '')
M.log.append(f"other paving: {rest.area:,.0f} sf in {len(parts(rest))} pieces")


def tree(p, rad=None):
    M.m.Objects.AddCircle(r.Circle(r.Point3d(p[0], p[1], 0), rad or rng.uniform(7, 9)), M.attrs(f'{P}::PLAZA TREES'))


def furniture(ls, name):
    M.add_line(ls, 0.05, f'{P}::SITE FURNITURE', name)


def along(g, spacing, inset, side_of=None):
    """Points every `spacing` ft along the long axis of each piece, `inset` ft inside its edge."""
    out = []
    for p in parts(g):
        core = p.buffer(-inset)
        for q in parts(core):
            line = q.exterior
            # the centre line of a strip: the longer half of the ring
            s = 0.0
            while s < line.length:
                pt = line.interpolate(s)
                if all(pt.distance(o) >= spacing * 0.9 for o in out): out.append(pt)
                s += spacing
    return out


def bench_at(pt, ang, length=7.0, name='BENCH'):
    dx, dy = math.cos(ang) * length / 2, math.sin(ang) * length / 2
    furniture(LineString([(pt.x - dx, pt.y - dy), (pt.x + dx, pt.y + dy)]), name)


def axis_angle(g):
    mr = g.minimum_rotated_rectangle; cs = list(mr.exterior.coords)
    e = max([(cs[i], cs[i + 1]) for i in range(4)], key=lambda s: math.dist(*s))
    return math.atan2(e[1][1] - e[0][1], e[1][0] - e[0][0])

# Trees: rows along the walks and the drop-off, kept clear of the building and the loading bays.
clear = building.buffer(10)
nt = nb = 0
for name, g in areas.items():
    if name == 'LOADING': continue
    pts = [p for p in along(g, 32 if 'WALK' in name else 28, 6) if not clear.contains(p)]
    for p in pts:
        tree((p.x, p.y)); nt += 1
    # benches between the trees, along the strip's axis
    ang = axis_angle(g)
    for a, b in zip(pts, pts[1:]):
        m = Point((a.x + b.x) / 2, (a.y + b.y) / 2)
        if a.distance(b) < 45 and g.buffer(-3).contains(m) and not clear.contains(m) and rng.random() < 0.7:
            bench_at(m, math.atan2(b.y - a.y, b.x - a.x)); nb += 1
M.log.append(f"plaza trees {nt}, benches {nb}")

# Bike racks by the gym (secondary drop-off) — "BIKE PARKING".
if 'SECONDARY DROP OFF PLAZA' in areas:
    g = areas['SECONDARY DROP OFF PLAZA']
    gym_edge = building.buffer(4).boundary.intersection(g.buffer(-2))
    pts = [l.interpolate(0.5, normalized=True) for l in lines_of(gym_edge) if l.length > 20][:2]
    nr = 0
    for c in pts:
        for k in range(6):
            off = (k - 2.5) * 2.5
            furniture(LineString([(c.x + off, c.y - 2.5), (c.x + off, c.y + 2.5)]), 'BIKE RACK'); nr += 1
    M.log.append(f"bike racks: {nr}")

# Loading bays against the building's south face.
if 'LOADING' in areas:
    g = areas['LOADING']
    nbays = 0
    for k in range(5):
        x0 = 195 + k * 14
        v = LineString([(x0 + 6, -80), (x0 + 6, 80)]).intersection(g)
        segs = lines_of(v)
        if not segs: continue
        top = max(max(y for _, y in l.coords) for l in segs)
        bay = box(x0, top - 40, x0 + 12, top)
        if g.buffer(0.5).contains(bay):
            furniture(LineString([(x0, top), (x0, top - 40), (x0 + 12, top - 40), (x0 + 12, top)]), 'LOADING BAY'); nbays += 1
    M.log.append(f"loading bays: {nbays}")

M.write(sys.argv[3])
print("\n".join(M.log))
