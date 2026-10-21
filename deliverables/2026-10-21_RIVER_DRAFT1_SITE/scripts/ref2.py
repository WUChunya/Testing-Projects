"""Reference plan 2 (the user's terraced lawn: a family of straight lines that turn on fillets, crossed by straight
paths, planters between the lines) as a generator for any area.

fan(g, axis, seed) → dict: LINES (curb lines), PATHS (polygon), PLANTERS (polygon), guide.
"""
import math, random
from shapely.geometry import Polygon, LineString, Point, box
from shapely.ops import unary_union
from shapely import affinity


def rot(v, deg):
    a = math.radians(deg)
    return (v[0] * math.cos(a) - v[1] * math.sin(a), v[0] * math.sin(a) + v[1] * math.cos(a))


def fillet_polyline(pts, radius, n=24):
    """Polyline with every corner replaced by a tangent arc of `radius` (clamped to half the shorter leg)."""
    out = [pts[0]]
    for i in range(1, len(pts) - 1):
        a, b, c = pts[i - 1], pts[i], pts[i + 1]
        u = ((a[0] - b[0]), (a[1] - b[1])); lu = math.hypot(*u); u = (u[0] / lu, u[1] / lu)
        v = ((c[0] - b[0]), (c[1] - b[1])); lv = math.hypot(*v); v = (v[0] / lv, v[1] / lv)
        cosang = max(-1, min(1, u[0] * v[0] + u[1] * v[1]))
        ang = math.acos(cosang)
        if ang < 1e-3 or abs(ang - math.pi) < 1e-3:
            out.append(b); continue
        t = radius / math.tan(ang / 2)
        t = min(t, 0.5 * lu, 0.5 * lv)
        r = t * math.tan(ang / 2)
        p1 = (b[0] + u[0] * t, b[1] + u[1] * t); p2 = (b[0] + v[0] * t, b[1] + v[1] * t)
        bis = (u[0] + v[0], u[1] + v[1]); lb = math.hypot(*bis); bis = (bis[0] / lb, bis[1] / lb)
        d = r / math.sin(ang / 2)
        cc = (b[0] + bis[0] * d, b[1] + bis[1] * d)
        a1 = math.atan2(p1[1] - cc[1], p1[0] - cc[0]); a2 = math.atan2(p2[1] - cc[1], p2[0] - cc[0])
        da = (a2 - a1 + math.pi) % (2 * math.pi) - math.pi
        out += [(cc[0] + r * math.cos(a1 + da * k / n), cc[1] + r * math.sin(a1 + da * k / n)) for k in range(n + 1)]
    out.append(pts[-1])
    return LineString(out)


def fan(g, axis, seed, spacing=7.0, path_w=9.0, turn=None):
    """Lines parallel to a guide that runs along `axis` and turns once on a fillet; two straight paths across them;
    planters (6 x 18 ft, rounded) between the lines."""
    rr = random.Random(seed)
    c = g.centroid
    minx, miny, maxx, maxy = g.bounds
    ext = math.hypot(maxx - minx, maxy - miny)
    d = axis
    turn = turn if turn is not None else rr.choice([-1, 1]) * rr.uniform(28, 40)
    nx, ny = -d[1], d[0]
    proj = [(x - c.x) * nx + (y - c.y) * ny for x, y in g.exterior.coords]
    W = max(proj) - min(proj)
    # The guide runs on the concave side of the bend; the lines are its offsets towards the convex side, so every
    # line turns on its own concentric fillet (radius R + offset) and the straight runs stay straight.
    side = 1 if turn > 0 else -1                              # concave side = left of the travel for a left turn
    shift = side * (W * 0.55)
    bend = (c.x + d[0] * ext * 0.1 + nx * shift, c.y + d[1] * ext * 0.1 + ny * shift)
    a = (bend[0] - d[0] * ext * 1.5, bend[1] - d[1] * ext * 1.5)
    e = rot(d, turn)
    b = (bend[0] + e[0] * ext * 1.5, bend[1] + e[1] * ext * 1.5)
    R = 14 + 0.2 * W
    guide = fillet_polyline([a, bend, b], R)
    lines = []
    k, off = 0, 0.0
    while off < W * 1.25 + ext * 0.3:
        o = -side * off
        g2 = guide.offset_curve(o, join_style=1) if off else guide
        cut = g2.intersection(g.buffer(-2.0))
        for l in getattr(cut, 'geoms', [cut]):
            if l.geom_type == 'LineString' and l.length > 8: lines.append((k, l))
        k += 1
        off += spacing * (1 + 0.035 * k)                  # the lines fan out a little, as in the reference
    # two straight paths across the lines
    paths = []
    for frac, ang in ((0.35, 62 * (1 if turn > 0 else -1)), (0.72, -48 * (1 if turn > 0 else -1))):
        base = d if frac < 0.5 else e
        p = Point(c.x + base[0] * (frac - 0.5) * ext * 0.5, c.y + base[1] * (frac - 0.5) * ext * 0.5)
        dd = rot(base, ang)
        ln = LineString([(p.x - dd[0] * ext, p.y - dd[1] * ext), (p.x + dd[0] * ext, p.y + dd[1] * ext)])
        paths.append(ln.buffer(path_w / 2, cap_style=2).intersection(g))
    paths = unary_union(paths)
    # planters between pairs of lines, every few strips, clear of the paths
    planters = []
    byk = {}
    for k, l in lines: byk.setdefault(k, []).append(l)
    for k in sorted(byk):
        if k % 3: continue
        for l in byk[k]:
            s = rr.uniform(8, 25)
            while s + 18 < l.length:
                p0 = l.interpolate(s); p1 = l.interpolate(s + 18)
                ang = math.degrees(math.atan2(p1.y - p0.y, p1.x - p0.x))
                mid = ((p0.x + p1.x) / 2, (p0.y + p1.y) / 2)
                q = l.interpolate(s + 9)
                tx, ty = p1.x - p0.x, p1.y - p0.y; n = math.hypot(tx, ty) or 1
                ctr = (q.x - ty / n * spacing / 2, q.y + tx / n * spacing / 2)
                rect = affinity.rotate(box(ctr[0] - 9, ctr[1] - 2.6, ctr[0] + 9, ctr[1] + 2.6), ang, origin=ctr)
                rect = rect.buffer(-1.2, join_style=2).buffer(1.2, join_style=1)
                if g.buffer(-2.5).contains(rect) and not rect.intersects(paths.buffer(3)):
                    planters.append(rect)
                s += rr.uniform(40, 70)
    planters = unary_union(planters)
    lines = [l for _, l in lines]
    cut = unary_union([paths.buffer(0.3), planters.buffer(0.3)])
    lines = [x for l in lines for x in getattr(l.difference(cut), 'geoms', [l.difference(cut)]) if x.geom_type == 'LineString' and x.length > 4]
    return {'LINES': lines, 'PATHS': paths, 'PLANTERS': planters, 'guide': guide}
