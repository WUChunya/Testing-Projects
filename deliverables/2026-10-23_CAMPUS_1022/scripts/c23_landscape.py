"""CAMPUS 1022 REV1: landscape on the terraces, the roofs and the courtyard, after the RIVER scheme's gardens
(RIVER 1021/1022: timber deck along the building side, raised planters with a row of trees, a lawn mound with
contours, a paved plaza with a pergola and benches, round picnic courts; same layers PROJECT SITE::GARDENS::*).

Usage: python3 -I c23_landscape.py <fixture.json of in.3dm> <in.3dm> <out.3dm>
User 10-23: every edge is a straight line or a true circle arc. Each piece is built from lines and arcs only
(rectangles, rectangles with round corners, circles, arc bands) and written as a Rhino curve of LineCurve / ArcCurve
segments, or extruded from one; nothing is cut by a boolean (pieces are laid out not to overlap, or stacked by height).

Areas (model feet, top of the surface they sit on):
- Courtyard (open to the sky between the wings, ground +1'-6").
- L2 OUTDOOR TERRACE and DINING TERRACE (on their 1 ft terrace blocks, +22'-7").
- L4 terrace west of the courtyard (+57'-7"), under the L5 slab.
- L5: ROOFTOP TERRACE (on its block, +74'-7"), the deck under the slatted trellis, the south-wing roof round the
  shell space, the strip over the L4 terrace (+73'-7").
- Top roof of the north wing round the shell space (+94'-2"): green roof.
"""
import sys, math, json, random
sys.path.insert(0, __file__.rsplit('/', 1)[0])
from r21_lib import *

M = Model(sys.argv[1], sys.argv[2])
rng = random.Random(1023)
G = 'PROJECT SITE::GARDENS'
for sub, role, mode in [('PATH', 'NC.Hardscape', 'color'), ('DECK', 'NC.Hardscape', 'color'), ('PAVING', 'NC.Hardscape', 'color'),
                        ('GRAVEL', 'NC.Hardscape', 'color'), ('LAWN', 'NC.Green', 'color'), ('PLANTING', 'NC.Green', 'color'),
                        ('PLANTER', 'NC.Green', 'color'), ('GREEN ROOF', 'NC.Green', 'color'), ('TREES', 'NC.Tree', 'color'),
                        ('FURNITURE', 'NC.Furniture', 'line'), ('STRUCTURE', 'NC.Furniture', 'line'), ('PEOPLE', 'Entourage', 'color')]:
    M.role(f'{G}::{sub}', role, mode=mode)
M.role(G, 'Ignore', mode=''); M.role('PROJECT SITE', 'Ignore', mode='')


# ------------------------------------------------------------------ lines-and-arcs kit
class Shape:
    """Closed outline of ('L', p0, p1) lines and ('A', p0, pm, p1) true arcs (pm = a point on the arc)."""
    def __init__(self, segs): self.segs = segs

    def curve(self, z):
        pc = r.PolyCurve()
        for s in self.segs:
            P = [r.Point3d(x, y, z) for x, y in s[1:]]
            if s[0] == 'L': pc.Append(r.Line(P[0], P[1]))
            else: pc.Append(r.Arc(P[0], P[1], P[2]))
        return pc

    def poly(self, n=24):
        pts = []
        for s in self.segs:
            if s[0] == 'L': pts.append(s[1]); continue
            (x1, y1), (x2, y2), (x3, y3) = s[1:]
            d = 2 * (x1 * (y2 - y3) + x2 * (y3 - y1) + x3 * (y1 - y2))
            ux = ((x1 * x1 + y1 * y1) * (y2 - y3) + (x2 * x2 + y2 * y2) * (y3 - y1) + (x3 * x3 + y3 * y3) * (y1 - y2)) / d
            uy = ((x1 * x1 + y1 * y1) * (x3 - x2) + (x2 * x2 + y2 * y2) * (x1 - x3) + (x3 * x3 + y3 * y3) * (x2 - x1)) / d
            R = math.dist((ux, uy), s[1]); t1, t2, t3 = (math.atan2(q[1] - uy, q[0] - ux) for q in s[1:])
            sw = (t3 - t1) % (2 * math.pi); go = 1 if (t2 - t1) % (2 * math.pi) < sw else -1
            if go < 0: sw = 2 * math.pi - sw
            pts += [(ux + R * math.cos(t1 + go * sw * k / n), uy + R * math.sin(t1 + go * sw * k / n)) for k in range(n)]
        return Polygon(pts)


def arc(c, R, a0, a1):
    """Arc about c from angle a0 to a1 (degrees, counter-clockwise when a1 > a0)."""
    p = lambda a: (c[0] + R * math.cos(math.radians(a)), c[1] + R * math.sin(math.radians(a)))
    return ('A', p(a0), p((a0 + a1) / 2), p(a1))


def rrect(x0, y0, x1, y1, rad=0.0):
    rad = min(rad, (x1 - x0) / 2 - 0.01, (y1 - y0) / 2 - 0.01) if rad > 0 else 0
    if rad <= 0:
        return Shape([('L', (x0, y0), (x1, y0)), ('L', (x1, y0), (x1, y1)), ('L', (x1, y1), (x0, y1)), ('L', (x0, y1), (x0, y0))])
    return Shape([('L', (x0 + rad, y0), (x1 - rad, y0)), arc((x1 - rad, y0 + rad), rad, -90, 0),
                  ('L', (x1, y0 + rad), (x1, y1 - rad)), arc((x1 - rad, y1 - rad), rad, 0, 90),
                  ('L', (x1 - rad, y1), (x0 + rad, y1)), arc((x0 + rad, y1 - rad), rad, 90, 180),
                  ('L', (x0, y1 - rad), (x0, y0 + rad)), arc((x0 + rad, y0 + rad), rad, 180, 270)])


def circle(c, R):
    return Shape([arc(c, R, 0, 180), arc(c, R, 180, 360)])


def band(c, r0, r1, a0, a1):
    """Arc band (C-shaped bench): two concentric arcs joined by two radial lines."""
    p = lambda R, a: (c[0] + R * math.cos(math.radians(a)), c[1] + R * math.sin(math.radians(a)))
    return Shape([arc(c, r1, a0, a1), ('L', p(r1, a1), p(r0, a1)), arc(c, r0, a1, a0), ('L', p(r0, a0), p(r1, a0))])


def area(s, sub, name, z):
    """Flat site area (ground)."""
    return str(M.m.Objects.AddCurve(s.curve(z), M.attrs(f'{G}::{sub}', name)))


def solid(s, sub, name, z0, h):
    """Extruded piece on a terrace / roof (deck, planter, green roof)."""
    c = s.curve(z0)
    e = r.Extrusion.Create(c, h, True)
    if e is None or e.GetBoundingBox().Max.Z < z0 + 0.01: e = r.Extrusion.Create(c, -h, True)
    return str(M.m.Objects.AddExtrusion(e, M.attrs(f'{G}::{sub}', name)))


def line(a, b, z, name, sub='FURNITURE'):
    M.m.Objects.AddCurve(r.LineCurve(r.Point3d(a[0], a[1], z), r.Point3d(b[0], b[1], z)), M.attrs(f'{G}::{sub}', name))


def ring(s, z, name, sub='FURNITURE'):
    M.m.Objects.AddCurve(s.curve(z), M.attrs(f'{G}::{sub}', name))


def tree(c, z, R=None):
    M.m.Objects.AddCircle(r.Circle(r.Point3d(c[0], c[1], z), R or rng.uniform(6.5, 9.0)), M.attrs(f'{G}::TREES'))


def person(c, z):
    M.m.Objects.AddCircle(r.Circle(r.Point3d(c[0], c[1], z), 1.0), M.attrs(f'{G}::PEOPLE'))


def tree_row(x0, y0, x1, y1, z, step=24.0, R=None):
    """Trees on the centre line of a planter, every `step` ft, ends kept clear."""
    L = math.dist((x0, y0), (x1, y1)); n = max(1, int(L // step))
    for k in range(n):
        t = (k + 0.5) / n
        tree((x0 + (x1 - x0) * t, y0 + (y1 - y0) * t), z, R)


def bench(x, y, z, along='x', L=8.0):
    s = rrect(x - L / 2, y - 0.9, x + L / 2, y + 0.9) if along == 'x' else rrect(x - 0.9, y - L / 2, x + 0.9, y + L / 2)
    ring(s, z, 'BENCH')


def table(c, z, n=4):
    ring(circle(c, 1.8), z, 'TABLE')
    for k in range(n):
        a = math.radians(45 + 360 * k / n)
        ring(circle((c[0] + 3.0 * math.cos(a), c[1] + 3.0 * math.sin(a)), 0.7), z, 'STOOL')


def pergola(x0, y0, x1, y1, z, along='x'):
    """Frame + slats every 2 ft (RIVER GARDENS::STRUCTURE)."""
    ring(rrect(x0, y0, x1, y1), z, 'PERGOLA', 'STRUCTURE')
    if along == 'x':
        x = x0 + 2
        while x < x1 - 0.5: line((x, y0), (x, y1), z, 'PERGOLA SLAT', 'STRUCTURE'); x += 2
    else:
        y = y0 + 2
        while y < y1 - 0.5: line((x0, y), (x1, y), z, 'PERGOLA SLAT', 'STRUCTURE'); y += 2


def mound(x0, y0, x1, y1, rad, z, name, sub='LAWN', h=None):
    """Lawn mound: the outline plus two inner contours (offset 4 / 8 ft, same corner centres → still lines + arcs)."""
    if h is None: area(rrect(x0, y0, x1, y1, rad), sub, name, z)
    else: solid(rrect(x0, y0, x1, y1, rad), sub, name, z, h)
    top = z + (h or 0) + 0.05
    for k in (4, 8):
        if x1 - x0 > 2 * k + 4 and y1 - y0 > 2 * k + 4:
            ring(rrect(x0 + k, y0 + k, x1 - k, y1 - k, max(rad - k, 0)), top, 'LAWN CONTOUR')


def court(c, R, z, toward_deg):
    """Round picnic court (RIVER 1022 PICNIC): paving, C-shaped timber bench open towards `toward_deg`, two tables,
    planting ring, three trees."""
    area(circle(c, R * 0.78), 'PAVING', 'PICNIC', z)
    area(band(c, R * 0.56, R * 0.56 + 1.6, toward_deg + 70, toward_deg + 290), 'DECK', '', z + 0.02)
    area(band(c, R * 0.78, R + 2.0, toward_deg + 35, toward_deg + 325), 'PLANTING', '', z)
    for k in range(3):
        a = math.radians(toward_deg + 120 + k * 60)
        tree((c[0] + (R + 0.8) * math.cos(a), c[1] + (R + 0.8) * math.sin(a)), z + 0.1, 6.0)
    for dx, dy in [(-R * 0.18, R * 0.12), (R * 0.2, -R * 0.1)]: table((c[0] + dx, c[1] + dy), z + 0.1)
    for k in range(3):
        a = rng.uniform(0, 2 * math.pi); rr = rng.uniform(1.5, R * 0.45)
        person((c[0] + rr * math.cos(a), c[1] + rr * math.sin(a)), z + 0.1)


def people(x0, y0, x1, y1, z, n):
    for _ in range(n): person((rng.uniform(x0 + 2, x1 - 2), rng.uniform(y0 + 2, y1 - 2)), z)


log = M.log

# ------------------------------------------------------------------ courtyard (ground, open sky x 3294–3463, y 2233–2366)
z = 1.55
X0, Y0, X1, Y1 = 3298.0, 2237.0, 3459.0, 2362.0
area(rrect(X0, Y0, X1, Y1), 'GRAVEL', 'COURTYARD', z - 0.03)                   # base: fine gravel
area(rrect(X0, Y1 - 12, X1, Y1), 'DECK', 'COURTYARD DECK', z)                  # timber deck along the north wing
area(rrect(X0, Y0, X1, Y0 + 9), 'PLANTING', '', z)                              # planting bed along the south wing
tree_row(X0 + 6, Y0 + 4.5, X1 - 6, Y0 + 4.5, z + 0.1)
PC = (3440.0, 2300.0); PR = 15.0                                                # plaza at the open (east) end
area(rrect(X0, 2296.0, PC[0] - PR + 0.5, 2304.0), 'PATH', 'GARDEN WALK', z)    # straight walk from the west spine
area(circle(PC, PR), 'PAVING', 'COURTYARD PLAZA', z)
pergola(PC[0] - 9, PC[1] - 7, PC[0] + 9, PC[1] + 7, z + 9)
for dy in (-10.5, 10.5): bench(PC[0], PC[1] + dy, z + 0.1)
mound(3326, 2308, 3418, 2343, 12, z, 'COURTYARD LAWN')                          # two lawn mounds either side of the walk
mound(3326, 2253, 3418, 2290, 12, z, 'COURTYARD LAWN')
area(rrect(X0 + 4, 2308, X0 + 20, 2343, 8), 'PLANTING', '', z)                  # planters at the west entry
area(rrect(X0 + 4, 2253, X0 + 20, 2290, 8), 'PLANTING', '', z)
tree((X0 + 12, 2325.5), z + 0.1); tree((X0 + 12, 2271.5), z + 0.1)
court((3439.0, 2335.0), 11.0, z, 225)                                           # picnic courts in the corners
court((3439.0, 2262.0), 11.0, z, 135)
for x in (3340, 3372, 3404): bench(x, 2306, z + 0.1); bench(x, 2294, z + 0.1)
people(X0, Y1 - 12, X1, Y1, z + 0.1, 8); people(3326, 2253, 3418, 2343, z + 0.1, 10)
log.append("courtyard: gravel base, deck along the north wing, planting + tree row along the south wing, walk from the "
           "spine to a round plaza (pergola, benches) at the open end, two lawn mounds, two picnic courts")


# ------------------------------------------------------------------ terraces (pieces sit on their surface z)
def terrace(name, x0, y0, x1, y1, z, building_side):
    """Long terrace strip: timber deck along the building, raised planter (1.5 ft) with a tree row along the open
    edge, tables and benches between; round-cornered planter and deck."""
    w = x1 - x0
    deck_w = min(12.0, w * 0.34); pl_w = min(8.0, w * 0.22)
    if building_side == 'E':
        deck = rrect(x1 - deck_w, y0 + 1, x1 - 1, y1 - 1); pl = (x0 + 1.5, x0 + 1.5 + pl_w)
    else:
        deck = rrect(x0 + 1, y0 + 1, x0 + deck_w, y1 - 1); pl = (x1 - 1.5 - pl_w, x1 - 1.5)
    solid(deck, 'DECK', name, z, 0.5)
    solid(rrect(pl[0], y0 + 3, pl[1], y1 - 3, pl_w / 2), 'PLANTER', '', z, 1.5)
    tree_row((pl[0] + pl[1]) / 2, y0 + 3, (pl[0] + pl[1]) / 2, y1 - 3, z + 1.6)
    mid = ((x0 + deck_w if building_side == 'W' else x0 + 1.5 + pl_w) + (x1 - deck_w if building_side == 'E' else x1 - 1.5 - pl_w)) / 2
    y = y0 + 16
    while y < y1 - 12:
        table((mid, y), z + 0.1); y += 22
    xb = x1 - deck_w + 1.5 if building_side == 'E' else x0 + deck_w - 1.5
    for k in range(int((y1 - y0) // 30)): bench(xb, y0 + 15 + 30 * k, z + 0.6, 'y')
    people(x0, y0, x1, y1, z + 0.6, int((y1 - y0) // 18))
    log.append(f"{name}: deck along the building ({building_side}), raised planter with tree row on the open edge, tables, benches")


terrace('OUTDOOR TERRACE', 3142.9, 2230.3, 3179.1, 2374.3, 22.6, 'E')
terrace('DINING TERRACE', 3265.7, 2232.0, 3300.0, 2372.6, 22.6, 'W')
terrace('L4 TERRACE', 3264.0, 2230.3, 3294.0, 2374.3, 57.6, 'W')

# ------------------------------------------------------------------ L5
z5 = 73.6
# ROOFTOP TERRACE (block top +74.6) under the high trellis: lawn mound and a deck
zt = 74.6
mound(3219, 2242, 3253, 2304, 14, zt, 'ROOFTOP LAWN', h=1.5)
solid(rrect(3217, 2312, 3255, 2363, 0), 'DECK', 'ROOFTOP TERRACE', zt, 0.5)
for y in (2322, 2338, 2354): table((3236, y), zt + 0.6)
people(3217, 2312, 3255, 2363, zt + 0.6, 6)
# deck under the slatted trellis (x 3134–3178) and a planter between it and the rooftop terrace
solid(rrect(3136, 2237, 3177, 2368, 0), 'DECK', 'TRELLIS DECK', z5, 0.5)
solid(rrect(3184, 2240, 3209, 2364, 8), 'PLANTER', '', z5, 1.5)
tree_row(3196.5, 2240, 3196.5, 2364, z5 + 1.6)
for y in (2260, 2300, 2340): bench(3170, y, z5 + 0.6, 'y')
people(3136, 2237, 3177, 2368, z5 + 0.6, 6)
# over the L4 terrace (x 3258–3294): green roof
solid(rrect(3262, 2240, 3291, 2362, 8), 'GREEN ROOF', 'GREEN ROOF', z5, 0.5)
# south-wing roof round the shell space (lengthened 40 ft west in c23_comments: x 3203–3444, y 2142–2214)
RG = (3168.0, 2167.0)                                       # roof garden west of the shell: round lawn mound in a deck
solid(rrect(3140, 2105, 3196, 2229, 0), 'DECK', 'ROOF GARDEN', z5, 0.5)
solid(circle(RG, 22), 'LAWN', 'ROOF LAWN', z5 + 0.5, 1.0)
for k in (5, 10): ring(circle(RG, 22 - k), z5 + 1.55, 'LAWN CONTOUR')
for c in [(3148, 2113), (3188, 2113), (3148, 2221), (3188, 2221)]: tree(c, z5 + 0.6, 6.0)
for c, d in [((3168, 2195), 'x'), ((3168, 2139), 'x')]: bench(c[0], c[1], z5 + 0.6, d)
people(3140, 2105, 3196, 2229, z5 + 0.6, 6)
solid(rrect(3206, 2106, 3441, 2137, 10), 'GREEN ROOF', 'GREEN ROOF', z5, 0.5)            # south of the shell
solid(rrect(3206, 2218, 3441, 2229, 0), 'PAVING', 'ROOF WALK', z5, 0.2)                 # north of the shell
solid(rrect(3450, 2106, 3484, 2228, 10), 'GREEN ROOF', 'GREEN ROOF', z5, 0.5)           # east of the shell
log.append("L5: rooftop terrace lawn mound + deck; trellis deck + planter with tree row; roof garden west of the shell "
           "(round lawn mound R 22 ft with contours, trees, benches); green roof south / east of the shell and over the L4 terrace")

# ------------------------------------------------------------------ top roof of the north wing (+94.2), round the shell
zr = 94.2
for x0, y0, x1, y1, rad in [(3186, 2371, 3226, 2499, 10), (3437, 2371, 3461, 2499, 8), (3231, 2471, 3432, 2499, 8), (3231, 2371, 3432, 2389, 6)]:
    solid(rrect(x0, y0, x1, y1, rad), 'GREEN ROOF', 'GREEN ROOF', zr, 0.5)
log.append("top roof (north wing): extensive green roof round the shell space, four round-cornered fields")

M.write(sys.argv[3])
json.dump(M.st, open(sys.argv[3].rsplit('.', 1)[0] + '_settings.json', 'w'), indent=1)
print("\n".join(log))
