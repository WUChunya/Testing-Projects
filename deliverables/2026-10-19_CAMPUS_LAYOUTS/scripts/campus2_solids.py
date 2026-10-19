"""CAMPUS 261019 model → plugin-ready copy (the plugin's own choices, made once here; nothing is deleted).

Usage: python3 -I campus2_solids.py <fixture.json of in.3dm> <in.3dm> <out.3dm> <names.json>
- Open-plan rooms modelled as 1 ft slabs or single surfaces (PROGRAM::Circulation / Break Room): extruded floor →
  ceiling of their storey; the originals go to OUTSIDE BUILDING (LEFTOVERS). Duplicates (same storey, same outline) once.
- Open Shell Space boxes: capped (same outline, same height); of two overlapping copies the larger one is kept.
- Columns, Shell Space and facade blocks outside the building (floor plates + 5 ft) → LEFTOVERS.
- Parking garage: the FLOR-OTLN outline massed as 5 decks × 10'-6" on PROGRAM::Parking (PARKING GARAGE).
- Tall rooms L1–L2 (auditorium, prototyping) kept tall; the north shell (L5 floor to +98.5') belongs to L5 only.
- Room names (the plugin's names table) and terrace names from the 261019 layouts: <names.json> {short id: name}.
"""
import json, sys
import rhino3dm as r
from shapely.geometry import Polygon, MultiPolygon, MultiPoint, LineString
from shapely.ops import unary_union, polygonize
sys.path.insert(0, __file__.rsplit("/", 1)[0])
from inv import no_holes

fx = json.load(open(sys.argv[1]))
m = r.File3dm.Read(sys.argv[2])
NAMES = json.load(open(sys.argv[4]))
FT = 304.8
STORIES = [(1.5, 20.5), (21.5, 40.5), (41.5, 56.5), (57.5, 72.5), (73.5, 92.5)]
SOLIDIFY = ("PROGRAM::Circulation", "PROGRAM::Break Room")
DECKS, DECK_FT = 5, 10.5
KEY = "ProgramDiagram\\settings"
st = json.loads(m.Strings[KEY])

def story(z0):
    return min(STORIES, key=lambda s: abs(s[0] - z0) if z0 >= s[0] - 1.5 else 1e9)

def bbox(o):
    return [v / FT for v in o["bbox"]]

def outline(o):
    if o.get("points"):
        return Polygon([(p[0] / FT, p[1] / FT) for p in o["points"]]).buffer(0)
    v, f = o["mesh"]["v"], o["mesh"]["f"]
    pts = [(v[i] / FT, v[i + 1] / FT) for i in range(0, len(v), 3)]
    tris = [Polygon([pts[fc[0]], pts[fc[k]], pts[fc[k + 1]]]) for fc in f for k in range(1, len(fc) - 1)]
    g = unary_union([t for t in tris if t.area > 1e-6]).buffer(0.05, join_style=2).buffer(-0.05, join_style=2)
    return max(g.geoms, key=lambda p: p.area) if isinstance(g, MultiPolygon) else g

def hull(o):
    v = o["mesh"]["v"]
    return MultiPoint([(v[i] / FT, v[i + 1] / FT) for i in range(0, len(v), 3)]).convex_hull

def solid(poly, z0, z1):
    pl = r.Polyline(0)
    for x, y in poly.exterior.coords: pl.Add(x, y, z0)
    ext = r.Extrusion.Create(r.PolylineCurve(pl), z1 - z0, True)
    if ext.GetBoundingBox().Max.Z < z0 + 0.01:
        ext = r.Extrusion.Create(r.PolylineCurve(pl), -(z1 - z0), True)
    return ext

def layer(path, color=(160, 160, 160)):
    for l in m.Layers:
        if l.FullPath == path: return l.Index
    parent = None
    if "::" in path:
        parent = m.Layers[layer(path.rsplit("::", 1)[0])].Id
    l = r.Layer(); l.Name = path.rsplit("::", 1)[-1]; l.Color = (*color, 255)
    if parent: l.ParentLayerId = parent
    return m.Layers.Add(l)

LEFT = layer("OUTSIDE BUILDING (LEFTOVERS)")
by_id = {str(o.Attributes.Id): o for o in m.Objects}
def move_left(oid):
    by_id[oid].Attributes.LayerIndex = LEFT

def add(poly, z0, z1, path, name=""):
    """Extruded solid(s); a surface with an opening (the atrium) becomes hole-free pieces. Returns the first id."""
    a = r.ObjectAttributes(); a.LayerIndex = layer(path)
    if name: a.Name = name
    ids = [str(m.Objects.AddExtrusion(solid(q, z0, z1), a)) for q in [poly]]
    return ids[0]

objs = fx["objects"]
plates = [bbox(o) for o in objs if o["layer"] == "FLOOR PLATE"]
X0, Y0 = min(b[0] for b in plates) - 5, min(b[1] for b in plates) - 5
X1, Y1 = max(b[3] for b in plates) + 5, max(b[4] for b in plates) + 5
inside = lambda b: X0 <= (b[0] + b[3]) / 2 <= X1 and Y0 <= (b[1] + b[4]) / 2 <= Y1
report, names, moved = [], dict(st.get("names") or {}), set()
new_of = {}

# Outside the building: whole objects; block pieces move their instance.
for o in objs:
    if (o["layer"] in ("Columns", "Shell Space") or o["layer"].startswith("Facade")) and not inside(bbox(o)):
        oid = o["id"].split("#")[0]
        if oid not in moved: move_left(oid); moved.add(oid)
report.append(f"outside the building: {len(moved)} objects / block instances")

# Shell spaces: cap the open boxes; overlapping copies keep the larger one.
shells = [o for o in objs if o["layer"] == "Shell Space" and o["id"] not in moved and bbox(o)[5] - bbox(o)[2] > 2]
shells.sort(key=lambda o: -hull(o).area)
kept = []
for o in shells:
    b, h = bbox(o), hull(o)
    if any(abs(b[2] - kb[2]) < 1 and h.intersection(kh).area > 0.5 * h.area for kb, kh in kept):
        move_left(o["id"]); report.append(f"shell copy {o['id'][:8]}"); continue
    kept.append((b, h))
    if not o["isClosed"]:
        nid = add(h, b[2], b[5], "Shell Space", o["name"]); move_left(o["id"]); new_of[o["id"][:8]] = nid
        report.append(f"shell capped {o['id'][:8]} {b[2]:.1f}-{b[5]:.1f}")
        if b[2] < 90: st["spanModes"][nid] = "Single"

# Open-plan rooms.
done = []
for o in objs:
    L, b = o["layer"], bbox(o)
    if L not in SOLIDIFY or "#" in o["id"] or b[5] - b[2] >= 2.0: continue
    if "Curve" in o["type"] and not o["isClosed"]: continue
    poly = outline(o)
    if poly.is_empty: continue
    z0, z1 = story(b[2])
    if any(L == dl and z0 == dz and poly.symmetric_difference(dp).area < 0.1 * poly.area for dl, dz, dp in done):
        move_left(o["id"]); report.append(f"duplicate {o['id'][:8]}"); continue
    done.append((L, z0, poly))
    new_of[o["id"][:8]] = add(poly, z0, z1, L, o["name"])
    move_left(o["id"])
report.append(f"open-plan rooms extruded: {len(done)}")

# Parking garage from its floor outline (largest closed loop of FLOR-OTLN).
lines = [LineString([(p[0] / FT, p[1] / FT) for p in o["points"]]) for o in objs if o["layer"] == "FLOR-OTLN" and o.get("points")]
loops = list(polygonize(unary_union(lines)))
garage = unary_union(loops).buffer(0.5, join_style=2).buffer(-0.5, join_style=2)
garage = Polygon(garage.exterior) if isinstance(garage, Polygon) else max(garage.geoms, key=lambda p: p.area)
gid = add(garage, 1.5, 1.5 + DECKS * DECK_FT, "PROGRAM::Parking", "PARKING GARAGE")
names[gid] = "PARKING GARAGE"
report.append(f"parking garage {garage.area:.0f} sf x {DECKS} decks, 1.5-{1.5 + DECKS * DECK_FT}")

# Tall rooms L1–L2.
for o in objs:
    b = bbox(o)
    if o["layer"].startswith("PROGRAM::") and o["isClosed"] and o["id"] not in moved and abs(b[2] - 1.5) < 0.6 and 39.5 < b[5] < 42:
        st["spanModes"][o["id"]] = "KeepTall"; report.append(f"kept tall {o['id'][:8]} {o['layer']}")

# Names from the layouts.
full = {o["id"][:8]: o["id"] for o in objs}
for short, name in NAMES.items():
    if short.startswith("_"): continue
    oid = new_of.get(short) or full.get(short)
    if oid is None: report.append(f"name target missing {short}"); continue
    names[oid] = name
    if oid in by_id and by_id[oid].Attributes.LayerIndex != LEFT and "Outdoor" in m.Layers[by_id[oid].Attributes.LayerIndex].FullPath:
        pass  # terraces keep no object name: the site plan would label them at grade
st["names"] = names
m.Strings[KEY] = json.dumps(st)
json.dump(st, open(sys.argv[3].replace(".3dm", "_settings.json"), "w"), indent=1)
m.Write(sys.argv[3], 8)
print("\n".join(report))
