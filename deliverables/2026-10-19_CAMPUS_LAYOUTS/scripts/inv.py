"""Inventory helpers for the RIVER fixture: footprints (ft, raw model coords) and markup page → model mapping."""
import json, numpy as np
from shapely.geometry import Polygon, Point, LineString
from shapely.ops import unary_union
MM = 304.8

def load(path):
    d = json.load(open(path))
    objs = []
    for o in d['objects']:
        b = [v / MM for v in o['bbox']]
        fp = None
        if o.get('mesh'):
            v = np.array(o['mesh']['v'], float).reshape(-1, 3) / MM
            tris = []
            for f in o['mesh']['f']:
                p = Polygon([(v[i][0], v[i][1]) for i in f])
                if p.area > 1e-4: tris.append(p.buffer(0))
            if tris: fp = unary_union(tris)
        elif o.get('points'):
            pts = [(x / MM, y / MM) for x, y, z in o['points']]
            fp = Polygon(pts).buffer(0) if o['isClosed'] and len(pts) > 3 else LineString(pts)
        objs.append(dict(id=o['id'], layer=o['layer'], type=o['type'], name=o['name'], z0=b[2], z1=b[5], bbox=b, fp=fp, closed=o['isClosed']))
    return objs

# Overview page (markup page 1) → model: fitted on the L1M property line.
PAGE = [(194.268, 916.538), (934.797, 914.939), (925.226, 130.082), (732.116, 157.93), (531.528, 301.94), (388.031, 471.746), (199.35, 576.059)]
MODEL = [(9.7, -67.5), (667.9, -66.1), (659.4, 631.5), (487.8, 606.8), (309.5, 478.8), (181.9, 327.8), (14.2, 235.1)]
A = np.array([(x, -y) for x, y in PAGE]); B = np.array(MODEL)
ma, mb = A.mean(0), B.mean(0)
U, S_, Vt = np.linalg.svd((B - mb).T @ (A - ma)); D = np.diag([1, np.sign(np.linalg.det(U @ Vt))]); R = U @ D @ Vt
s = (S_ * np.diag(D)).sum() / ((A - ma) ** 2).sum(); t = mb - s * R @ ma
PANEL = {'L1M': (0, 0), 'L2': (1083.746, 0), 'L3': (2167.492, 0), 'L4': (0, 1062.191), 'L5': (1083.746, 1062.191)}

def page_to_model(x, y, panel='L1M', k=0.75):
    """Markup page points (the old PDF drew 1 SVG unit = 0.75 pt) → model feet."""
    x, y = x / k, y / k
    ox, oy = PANEL[panel]
    v = s * R @ np.array([x - ox, -(y - oy)]) + t
    return float(v[0]), float(v[1])


def no_holes(poly):
    """Hole-free pieces of a polygon (cut through each hole), since closed curves on a layer cannot carry holes."""
    from shapely.geometry import Polygon, LineString
    from shapely.ops import split
    todo, out = [poly], []
    while todo:
        p = todo.pop()
        if p.is_empty or p.area < 1e-6: continue
        if not p.interiors:
            out.append(p); continue
        c = p.interiors[0].centroid
        minx, miny, maxx, maxy = p.bounds
        pieces = split(p, LineString([(c.x, miny - 1), (c.x, maxy + 1)]))
        todo.extend(g for g in pieces.geoms if g.geom_type == 'Polygon')
    return out


# V2 markup (true-scale PDF, 1 pt = 1 SVG unit): the overview's panels moved down by 89.099 pt.
PANEL_V2 = {'L1M': (0, 89.099), 'L2': (1083.746, 89.099), 'L3': (2167.492, 89.099), 'L4': (0, 1151.291 + 89.099)}

def page_to_model_v2(x, y, panel='L1M'):
    ox, oy = PANEL_V2[panel]
    v = s * R @ np.array([x - ox, -(y - oy)]) + t
    return float(v[0]), float(v[1])
