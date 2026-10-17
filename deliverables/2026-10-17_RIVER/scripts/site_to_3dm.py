"""Add the site plan (.ai drawings, flattened by extract_ai.py) to a copy of the RIVER model as plugin-ready layers.

Usage: python3 -I site_to_3dm.py <site.json> <in.3dm> <out.3dm>
Paint order of the .ai is kept by subtracting every later shape from earlier ones (a planar partition, so the
plugin's own role draw order cannot hide a path under a lawn). Page points are registered to model feet on the
property line (similarity fit of the 7 parcel corners).
"""
import json, math, sys
import numpy as np
import rhino3dm as r
from shapely.geometry import Polygon, MultiPolygon
from shapely.ops import unary_union

site = json.load(open(sys.argv[1]))
model = r.File3dm.Read(sys.argv[2])

PAGE = [(376.6, 611.7), (850.5, 610.7), (844.4, 108.4), (720.8, 126.2), (592.5, 218.4), (500.6, 327.1), (379.9, 393.8)]
MODEL = [(9.7, -67.5), (667.9, -66.1), (659.4, 631.5), (487.8, 606.8), (309.5, 478.8), (181.9, 327.8), (14.2, 235.1)]

# Similarity fit (Umeyama) of page (y flipped) → model.
A = np.array([(x, -y) for x, y in PAGE]); B = np.array(MODEL)
ma, mb = A.mean(0), B.mean(0)
U, S, Vt = np.linalg.svd((B - mb).T @ (A - ma))
D = np.diag([1, np.sign(np.linalg.det(U @ Vt))])
R = U @ D @ Vt
s = (S * np.diag(D)).sum() / ((A - ma) ** 2).sum()
t = mb - s * R @ ma
err = np.abs((s * (R @ A.T)).T + t - B).max()
print(f"scale {s:.4f} ft/pt (1:{s * 72:.1f}' per inch), rotation {math.degrees(math.atan2(R[1, 0], R[0, 0])):.3f} deg, max corner error {err:.2f} ft")

def to_model(p):
    v = s * R @ np.array([p[0], -p[1]]) + t
    return float(v[0]), float(v[1])

def poly(rings):
    ps = [Polygon(rg).buffer(0) for rg in rings if len(rg) >= 3]
    ps = [p for p in ps if not p.is_empty]
    if not ps: return None
    # even-odd: the first ring is the shell, later rings inside it are holes
    g = ps[0]
    for p in ps[1:]:
        g = g.symmetric_difference(p)
    return g

FILL = {
    (0.965, 0.973, 0.973): "light", (0.886, 0.886, 0.886): "block", (0.808, 0.918, 0.855): "green",
    (0.882, 0.98, 0.98): "water", (0.992, 0.961, 0.824): "parcel", (1.0, 1.0, 1.0): "building",
}
shapes = []  # (kind, geometry) in paint order
trees = []
for d in site:
    if d["layer"] == "Trees":
        g = poly(d["rings"])
        if g is not None: trees.append(g)
        continue
    if d["layer"] == "Crosswalks" or not d["fill"]:
        continue
    kind = FILL.get(tuple(d["fill"]))
    if kind is None: continue
    if d["layer"] == "Sidewalks": kind = "sidewalk"
    if d["layer"] == "Buildings":
        # the filled copy is clipped at the artboard; the stroke of the same building is not — use the bigger one
        kind = "building"
    g = poly(d["rings"])
    if g is not None: shapes.append((kind, g))

# Buildings: unclipped stroke outlines (closed) where they exist.
bld_strokes = [Polygon(d["rings"][0]).buffer(0) for d in site if d["layer"] == "Buildings" and d["stroke"] and d["rings"]]
buildings = []
for _, g in [x for x in shapes if x[0] == "building"]:
    best = max((b for b in bld_strokes if b.intersects(g)), key=lambda b: b.intersection(g).area, default=None)
    buildings.append(best if best is not None and best.area >= g.area else g)
shapes = [x for x in shapes if x[0] not in ("building", "parcel")]

# Light grey fills: the big band south of the site is the street, the long river strips are river walks.
named = []
for kind, g in shapes:
    if kind == "light":
        kind = "street" if g.bounds[1] > 600 else "walk"
    named.append([kind, g])
# Planar partition in paint order: later shapes cover earlier ones.
for i in range(len(named)):
    later = [g for _, g in named[i + 1:]] + buildings
    if later: named[i][1] = named[i][1].difference(unary_union(later))

LAYERS = {  # leaf names chosen so the plugin's layer keywords pre-fill the right role
    "block": ("CITY BLOCKS", (226, 226, 226)),        # BG.Hardscape
    "street": ("STREET", (246, 248, 248)),            # BG.Road
    "sidewalk": ("SIDEWALK", (246, 248, 248)),        # BG.Hardscape
    "walk": ("WALKWAY", (246, 248, 248)),             # BG.Hardscape (river walk)
    "green": ("BANK LANDSCAPE", (206, 234, 218)),     # BG.Landscape
    "water": ("RIVER", (225, 250, 250)),              # BG.Water
    "building": ("CONTEXT BUILDINGS", (255, 255, 255)),  # BG.Building
    "tree": ("TREES", (8, 149, 72)),                  # BG.Tree
}
parent = r.Layer(); parent.Name = "SITE CONTEXT"; parent.Color = (0, 0, 0, 255)
pidx = model.Layers.Add(parent)
pid = model.Layers[pidx].Id
lidx = {}
for k, (name, col) in LAYERS.items():
    l = r.Layer(); l.Name = name; l.ParentLayerId = pid; l.Color = (*col, 255)
    lidx[k] = model.Layers.Add(l)

def attrs(kind, name=""):
    a = r.ObjectAttributes(); a.LayerIndex = lidx[kind]
    if name: a.Name = name
    return a

def polyline(coords, z=0.0):
    pts = [to_model(p) for p in coords]
    if pts[0] != pts[-1]: pts.append(pts[0])
    pl = r.Polyline(len(pts))
    for x, y in pts: pl.Add(x, y, z)
    return pl

def parts(g):
    if g.is_empty: return []
    return list(g.geoms) if isinstance(g, MultiPolygon) else ([g] if isinstance(g, Polygon) else [p for p in getattr(g, "geoms", []) if isinstance(p, Polygon)])

count = {}
for kind, g in named:
    for p in parts(g.buffer(0)):
        if p.area < 4: continue  # < ~8 sf slivers from the partition
        p = p.simplify(0.15)
        for ring in [p.exterior, *p.interiors]:
            model.Objects.AddCurve(r.PolylineCurve(polyline(list(ring.coords))), attrs(kind))
        count[kind] = count.get(kind, 0) + 1

H = 40.0  # context building height, ft (the site plan has none)
for i, b in enumerate(buildings):
    crv = r.PolylineCurve(polyline(list(b.exterior.coords)))
    ext = r.Extrusion.Create(crv, H, True)
    if ext is None: continue
    model.Objects.AddExtrusion(ext, attrs("building"))
count["building"] = len(buildings)

for i, g in enumerate(trees):
    c = g.centroid; x, y = to_model((c.x, c.y))
    rad = max(6.0, min(18.0, math.sqrt(g.area / math.pi) * s))
    model.Objects.AddCircle(r.Circle(r.Point3d(x, y, 0), rad), attrs("tree"))
count["tree"] = len(trees)
print(count)

# Named view matching the user's axon screenshot (viewer azimuth ~67 deg, ~49 deg above the horizon).
az, el = math.radians(67), math.radians(49)
c = (math.cos(el) * math.cos(az), math.cos(el) * math.sin(az), math.sin(el))
target = r.Point3d(340, 280, 20)
vp = r.ViewportInfo()
vp.ChangeToParallelProjection(True)
dist = 1500
vp.SetCameraLocation(r.Point3d(target.X + c[0] * dist, target.Y + c[1] * dist, target.Z + c[2] * dist))
vp.SetCameraDirection(r.Vector3d(-c[0], -c[1], -c[2]))
vp.SetCameraUp(r.Vector3d(0, 0, 1))
vi = r.ViewInfo(); vi.Name = "PD AXON (site plan view)"; vi.Viewport = vp
model.NamedViews.Add(vi)

model.Write(sys.argv[3], 8)
print("written", sys.argv[3])
