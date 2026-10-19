"""CAMPUS model: turn the program surfaces (the plugin's "single surface → extrude" choice, made here once for all)
into story-high solids, as in the RIVER model, and cap the open shell spaces.

Usage: python3 -I campus_solids.py <campus_pd.json fixture> <campus_pd.3dm> <out.3dm>
- Flat or < 2 ft thick masses on PROGRAM::Circulation / Break Room → extruded floor → ceiling of their story.
  Duplicates (same story, same outline) are kept once.
- PROGRAM::Outdoor Circulation stays a surface: role NC.Hardscape (terraces), set in campus_settings.py.
- Open Shell Space extrusions → capped; the Brep copy of the south one and the two flat shell outlines under the green
  roof go to OUTSIDE BUILDING (LEFTOVERS) with the originals of every replaced surface.
"""
import json, sys
import rhino3dm as r
from shapely.geometry import Polygon, MultiPolygon, MultiPoint
from shapely.ops import unary_union

fx = json.load(open(sys.argv[1]))
m = r.File3dm.Read(sys.argv[2])
FT = 304.8
STORIES = [(1.5, 20.5), (21.5, 40.5), (41.5, 56.5), (57.5, 72.5), (73.5, 92.5)]
# Open-plan rooms the user models as 1 ft slabs or single surfaces (no walls). Curves and the lone PROGRAM::Support
# surface at +65' stay as modelled (the plugin skips curves and asks about that surface).
SOLIDIFY = ("PROGRAM::Circulation", "PROGRAM::Break Room")
LEFTOVER_IDS = {"da830010", "32d6e5a6", "67a267de"}
CAP_IDS = {"171ac3ea", "f7cf6db3"}

def story(z0):
    return min(STORIES, key=lambda s: abs(s[0] - z0) if z0 >= s[0] - 1.5 else 1e9)

def outline(o):
    if o.get("points"):
        return Polygon([(p[0] / FT, p[1] / FT) for p in o["points"]]).buffer(0)
    v, f = o["mesh"]["v"], o["mesh"]["f"]
    pts = [(v[i] / FT, v[i + 1] / FT) for i in range(0, len(v), 3)]
    tris = []
    for face in f:
        for k in range(1, len(face) - 1):
            t = Polygon([pts[face[0]], pts[face[k]], pts[face[k + 1]]])
            if t.area > 1e-6: tris.append(t)
    return unary_union(tris).buffer(0.05, join_style=2).buffer(-0.05, join_style=2)

def solid(poly, z0, z1):
    ring = list(poly.exterior.coords)
    pl = r.Polyline(len(ring))
    for x, y in ring: pl.Add(x, y, z0)
    ext = r.Extrusion.Create(r.PolylineCurve(pl), z1 - z0, True)
    if ext.GetBoundingBox().Max.Z < z0 + 0.01:  # extruded down: the curve ran the other way
        ext = r.Extrusion.Create(r.PolylineCurve(pl), -(z1 - z0), True)
    return ext

layer_of = {l.FullPath: l.Index for l in m.Layers}
left = layer_of["OUTSIDE BUILDING (LEFTOVERS)"]
by_id = {str(o.Attributes.Id)[:8]: o for o in m.Objects}
done, report, single = [], [], []
for o in fx["objects"]:
    L, b = o["layer"], [v / FT for v in o["bbox"]]
    short = o["id"][:8]
    if L == "Shell Space" and short in LEFTOVER_IDS:
        by_id[short].Attributes.LayerIndex = left; report.append(f"leftover {short}"); continue
    if L == "Shell Space" and short in CAP_IDS:
        v = o["mesh"]["v"]
        hull = MultiPoint([(v[i] / FT, v[i + 1] / FT) for i in range(0, len(v), 3)]).convex_hull
        a = r.ObjectAttributes(); a.LayerIndex = layer_of[L]; a.Name = o["name"]
        nid = m.Objects.AddExtrusion(solid(hull, b[2], b[5]), a)
        # The north shell (L5 floor +73.5' to +98.5') pokes past the L6 roof level: it belongs to L5 only.
        if b[2] < 90: single.append(str(nid))
        by_id[short].Attributes.LayerIndex = left; report.append(f"capped {short}"); continue
    if L not in SOLIDIFY or "#" in o["id"]: continue
    thin = b[5] - b[2] < 2.0
    if not thin: continue
    if "Curve" in o["type"] and not o["isClosed"]: continue
    if short == "b64bf2e7": continue  # landing piece at the L2 floor, a real thin mass
    poly = outline(o)
    if poly.is_empty: continue
    if isinstance(poly, MultiPolygon): poly = max(poly.geoms, key=lambda p: p.area)
    z0, z1 = story(b[2])
    if any(L == dl and z0 == dz and poly.symmetric_difference(dp).area < 0.1 * poly.area for dl, dz, dp in done):
        by_id[short].Attributes.LayerIndex = left; report.append(f"duplicate {short}"); continue
    done.append((L, z0, poly))
    a = r.ObjectAttributes(); a.LayerIndex = layer_of[L]; a.Name = o["name"]
    m.Objects.AddExtrusion(solid(poly, z0, z1), a)
    by_id[short].Attributes.LayerIndex = left
    report.append(f"solid {short} {L.split('::')[-1]} {z0}-{z1} {poly.area:.0f} sf")
KEY = "ProgramDiagram\\settings"
st = json.loads(m.Strings[KEY])
for i in single: st["spanModes"][i] = "Single"
m.Strings[KEY] = json.dumps(st)
json.dump(st, open(sys.argv[3].replace(".3dm", "_settings.json"), "w"), indent=1)
m.Write(sys.argv[3], 8)
print("\n".join(report))
