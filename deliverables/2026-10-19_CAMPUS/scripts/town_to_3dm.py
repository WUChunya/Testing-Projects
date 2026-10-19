"""Add the Town site plan (.ai drawings, flattened by extract_ai.py) to a copy of the CAMPUS model as plugin-ready
layers, the same layers as the RIVER model: SITE CONTEXT::*, PROPERTY LINE, SETBACK, PROJECT SITE::PLAZA, and the
section / elevation lines on DETAIL::CUTLINE.

Usage: python3 -I town_to_3dm.py <town.json> <in.3dm> <out.3dm>
Registration (town_fit.py): 1 pt = 100/72 ft (the sheet is 1" = 100'), no rotation; the campus parcel matches the
model's SURROUNDING::LOT outline within 0.3 ft.
"""
import json, sys
import rhino3dm as r
from shapely.geometry import Polygon, MultiPolygon, LineString
from shapely.ops import unary_union
sys.path.insert(0, __file__.rsplit('/', 1)[0])
from inv import no_holes

site = json.load(open(sys.argv[1]))
model = r.File3dm.Read(sys.argv[2])
S, TX, TY = 100 / 72, 2338.49, 3054.54

def to_model(p):
    return TX + S * p[0], TY - S * p[1]

def poly(rings):
    ps = [Polygon(rg).buffer(0) for rg in rings if len(rg) >= 3]
    ps = [p for p in ps if not p.is_empty]
    if not ps: return None
    g = ps[0]
    for p in ps[1:]:
        g = g.symmetric_difference(p)
    return g

# Fills of the Town sheet → kinds (paint order kept).
FILL = {
    (0.886, 0.886, 0.886): "block", (0.929, 0.929, 0.933): "sidewalk", (0.922, 0.929, 0.933): "sidewalk",
    (0.965, 0.973, 0.973): "light", (0.808, 0.918, 0.855): "green", (0.882, 0.98, 0.98): "water",
}
shapes, trees = [], []
for d in site:
    if d["layer"] == "Trees":
        g = poly(d["rings"])
        if g is not None: trees.append(g)
        continue
    if d["layer"] in ("Crosswalks", "Buildings") or not d["fill"]:
        continue
    kind = FILL.get(tuple(d["fill"]))
    if tuple(d["fill"]) == (0.992, 0.961, 0.824): kind = "block"  # other development parcels read as city blocks
    if kind is None: continue
    g = poly(d["rings"])
    if g is not None: shapes.append([kind, g])

# Buildings: filled outlines, else their strokes.
buildings = []
for d in site:
    if d["layer"] == "Buildings" and d["rings"] and len(d["rings"][0]) >= 3:
        g = Polygon(d["rings"][0]).buffer(0)
        if not any(g.symmetric_difference(b).area < 0.05 * g.area for b in buildings): buildings.append(g)

# Light grey: the big network is the street, the narrow strips along the river are walks.
for item in shapes:
    if item[0] == "light":
        item[0] = "street" if item[1].area > 50000 else "walk"
for i in range(len(shapes)):
    later = [g for _, g in shapes[i + 1:]] + buildings
    if later: shapes[i][1] = shapes[i][1].difference(unary_union(later))

strokes = [d for d in site if d["layer"] == "Site Plan" and d["stroke"] and d["rings"]]
solid = [Polygon(d["rings"][0]) for d in strokes if not d["dashes"].strip("[] 0")]
dashed = [Polygon(d["rings"][0]) for d in strokes if d["dashes"].strip("[] 0")]
# Our parcel: the one under the campus building (model x 3050–3390, y 2230–2620 → page).
probe = ((3220 - TX) / S, (TY - 2420) / S)
from shapely.geometry import Point
parcel = next(p for p in solid if p.contains(Point(probe)))
setback = next(p for p in dashed if p.contains(Point(probe)))
for item in shapes:
    item[1] = item[1].difference(parcel)

LAYERS = {
    "block": ("CITY BLOCKS", (226, 226, 226)), "street": ("STREET", (246, 248, 248)),
    "sidewalk": ("SIDEWALK", (237, 237, 238)), "walk": ("WALKWAY", (246, 248, 248)),
    "green": ("BANK LANDSCAPE", (206, 234, 218)), "water": ("RIVER", (225, 250, 250)),
    "building": ("CONTEXT BUILDINGS", (255, 255, 255)), "tree": ("TREES", (8, 149, 72)),
}
def add_layer(name, col, parent=None):
    l = r.Layer(); l.Name = name; l.Color = (*col, 255)
    if parent is not None: l.ParentLayerId = parent
    i = model.Layers.Add(l)
    return i, model.Layers[i].Id
_, ctx_id = add_layer("SITE CONTEXT", (0, 0, 0))
lidx = {k: add_layer(n, c, ctx_id)[0] for k, (n, c) in LAYERS.items()}
lidx["property"] = add_layer("PROPERTY LINE", (255, 0, 0))[0]
lidx["setback"] = add_layer("SETBACK", (128, 128, 128))[0]
_, ps_id = add_layer("PROJECT SITE", (0, 0, 0))
lidx["plaza"] = add_layer("PLAZA", (240, 230, 210), ps_id)[0]
_, det_id = add_layer("DETAIL", (0, 0, 0))
lidx["cut"] = add_layer("CUTLINE", (255, 0, 255), det_id)[0]

def attrs(kind, name=""):
    a = r.ObjectAttributes(); a.LayerIndex = lidx[kind]
    if name: a.Name = name
    return a

def polyline(coords, z=0.0, page=True):
    pts = [to_model(p) if page else p for p in coords]
    if pts[0] != pts[-1]: pts.append(pts[0])
    pl = r.Polyline(len(pts))
    for x, y in pts: pl.Add(x, y, z)
    return pl

def parts(g):
    if g.is_empty: return []
    return list(g.geoms) if isinstance(g, MultiPolygon) else ([g] if isinstance(g, Polygon) else [p for p in getattr(g, "geoms", []) if isinstance(p, Polygon)])

count = {}
for kind, g in shapes:
    for p in parts(g.buffer(0)):
        if p.area < 4: continue
        for ring in [q.exterior for q in no_holes(p.simplify(0.15))]:
            model.Objects.AddCurve(r.PolylineCurve(polyline(list(ring.coords))), attrs(kind))
        count[kind] = count.get(kind, 0) + 1
H = 40.0  # context building height, ft (the site plan has none), same as RIVER
for b in buildings:
    ext = r.Extrusion.Create(r.PolylineCurve(polyline(list(b.exterior.coords))), H, True)
    if ext is not None: model.Objects.AddExtrusion(ext, attrs("building"))
count["building"] = len(buildings)
for g in trees:
    c = g.centroid; x, y = to_model((c.x, c.y))
    rad = max(6.0, min(18.0, (g.area / 3.14159) ** 0.5 * S))
    model.Objects.AddCircle(r.Circle(r.Point3d(x, y, 0), rad), attrs("tree"))
count["tree"] = len(trees)
model.Objects.AddCurve(r.PolylineCurve(polyline(list(parcel.exterior.coords))), attrs("property", "CAMPUS PARCEL"))
model.Objects.AddCurve(r.PolylineCurve(polyline(list(setback.exterior.coords))), attrs("setback"))
model.Objects.AddCurve(r.PolylineCurve(polyline(list(parcel.exterior.coords), 0.0)), attrs("plaza"))

# Section / elevation lines (model ft), like RIVER's: two sections through the building, two elevations.
CUTS = json.load(open(__file__.rsplit('/', 1)[0] + "/campus_cuts.json"))
ids = {}
for c in CUTS:
    (x0, y0), (x1, y1) = c["from"], c["to"]
    pl = r.Polyline(2); pl.Add(x0, y0, 0); pl.Add(x1, y1, 0)
    ids[c["name"]] = str(model.Objects.AddCurve(r.PolylineCurve(pl), attrs("cut", c["label"])))
json.dump(ids, open(sys.argv[3] + ".cuts.json", "w"))
print(count, "parcel area sf", round(parcel.area * S * S), "cuts", ids)
model.Write(sys.argv[3], 8)
