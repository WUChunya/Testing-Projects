"""Reference plan 1 (the user's riverside park plan, 1125 x 2000 px png) as vector shapes in image pixels, and a mapper
that lays them into any strip of our site.

Image frame: the river on the left, the park strip from the water's edge (x ≈ 60) to the cycling track (x 535–555),
y 90 → 1400 along the river. Everything is traced on a 50 px grid over the png (colour masks were too broken up by the tree canopies): the
water's edge, the walk, the timber deck, the lawn mounds and planting beds (corners filleted), the tree squares.
"""
import json, math, os
from shapely.geometry import Polygon, LineString, Point, box
from shapely.ops import unary_union
from shapely import affinity

HERE = os.path.dirname(__file__)
Y0, Y1, X_WATER, X_PLAZA, X_TRACK0, X_TRACK1 = 90, 1400, 60, 200, 535, 555

WATER_EDGE = [(200, 90), (200, 300), (140, 375), (195, 460), (200, 700), (100, 875), (160, 960), (205, 1010), (205, 1400)]
WALK = [(392, 90), (394, 330), (400, 470), (398, 560), (392, 640), (325, 700), (322, 770), (400, 880), (395, 950),
        (270, 1110), (255, 1170), (258, 1300), (300, 1345), (375, 1400)]
DECK = [(282, 90), (306, 90), (306, 285), (250, 312), (232, 350), (330, 452), (425, 535), (512, 548), (546, 520), (450, 300), (400, 290), (400, 90)]
DECK2 = [(310, 568), (388, 590), (372, 640), (340, 640)]
TREE_SQUARES = [(250, y) for y in (637, 700, 762, 822, 885, 937, 993)]
LAWNS = [[(320, 92), (370, 92), (372, 240), (352, 248), (322, 200)],
         [(322, 255), (345, 250), (345, 380), (330, 385)],
         [(447, 240), (522, 190), (530, 195), (530, 468), (508, 462)],
         [(430, 668), (495, 605), (522, 600), (522, 695), (445, 700)],
         [(332, 700), (350, 685), (525, 790), (530, 955), (470, 1000), (400, 900), (335, 790)],
         [(298, 1085), (330, 1060), (390, 962), (410, 968), (505, 1105), (505, 1180), (445, 1228), (300, 1140)],
         [(300, 1300), (500, 1212), (530, 1218), (530, 1360), (380, 1400), (300, 1400)]]
SHRUBS = [[(362, 300), (398, 290), (400, 375), (385, 398), (365, 395)],
          [(405, 90), (530, 90), (530, 140), (470, 155), (425, 150), (405, 120)],
          [(333, 840), (355, 840), (385, 880), (388, 960), (345, 990), (333, 980)],
          [(277, 1160), (300, 1160), (335, 1200), (345, 1250), (320, 1280), (280, 1280)]]
BENCHES = [((262, 1062), (300, 1028)), ((488, 1012), (490, 1070)), ((250, 330), (300, 360)), ((470, 360), (530, 470))]


def shapes():
    """Reference areas in image px: dict name → Polygon / MultiPolygon, by priority (later ones win)."""
    fil = lambda p, r=9: Polygon(p).buffer(-r, join_style=1).buffer(r, join_style=1)     # filleted corners
    lawns = [fil(p) for p in LAWNS]
    shrubs = [fil(p, 6) for p in SHRUBS]
    full = box(X_WATER, Y0, X_TRACK1, Y1)
    water_side = Polygon([(X_WATER, Y0)] + WATER_EDGE + [(X_WATER, Y1)])
    walk_line = LineString(WALK)
    walk = walk_line.buffer(10, join_style=1, cap_style=2)
    west = Polygon(WATER_EDGE + [(x, y) for x, y in reversed(WALK)])
    track = box(X_TRACK0, Y0, X_TRACK1, Y1)
    deck = unary_union([Polygon(DECK), Polygon(DECK2)]).buffer(0)
    out = {}
    out['EDGE PLANTING'] = water_side.intersection(full)
    out['PLAZA'] = west.buffer(0).difference(water_side)
    out['GRAVEL'] = full.difference(water_side).difference(west.buffer(0)).difference(track)
    out['DECK'] = deck
    out['LAWN'] = unary_union(lawns)
    out['SHRUB'] = unary_union(shrubs)
    out['WALK'] = walk.intersection(full)
    out['TRACK'] = track
    # priority: each later one is cut out of the earlier ones
    order = list(out)
    for i, k in enumerate(order):
        for k2 in order[i + 1:]:
            out[k] = out[k].difference(out[k2])
    # lawn mounds: an inner contour line 25 px in
    mounds = [l.buffer(-22, join_style=1).exterior for l in lawns if l.area > 9000 and l.buffer(-22).geom_type == 'Polygon']
    return out, mounds


def densify(g, step=4.0):
    if g.geom_type == 'Polygon':
        return Polygon(densify(LineString(g.exterior.coords), step).coords, [densify(LineString(i.coords), step).coords for i in g.interiors])
    if g.geom_type == 'LineString':
        pts = []
        for a, b in zip(g.coords[:-1], g.coords[1:]):
            n = max(1, int(math.dist(a, b) / step))
            pts += [(a[0] + (b[0] - a[0]) * k / n, a[1] + (b[1] - a[1]) * k / n) for k in range(n)]
        return LineString(pts + [g.coords[-1]])
    return g


def map_geom(g, f):
    """Map a shape in image px through f(x, y) → (X, Y) (model feet)."""
    from r21_lib import parts, lines_of
    if g.is_empty: return g
    if g.geom_type == 'LinearRing': g = LineString(g.coords)
    if g.geom_type == 'MultiLineString':
        from shapely.geometry import MultiLineString
        return MultiLineString([map_geom(l, f) for l in g.geoms])
    if g.geom_type in ('MultiPolygon', 'GeometryCollection'):
        return unary_union([map_geom(p, f) for p in g.geoms if p.geom_type in ('Polygon', 'LineString')])
    d = densify(g)
    if d.geom_type == 'LineString':
        return LineString([f(x, y) for x, y in d.coords])
    return Polygon([f(x, y) for x, y in d.exterior.coords], [[f(x, y) for x, y in i.coords] for i in d.interiors]).buffer(0)
