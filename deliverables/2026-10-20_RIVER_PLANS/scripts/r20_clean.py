"""Clean the RIVER 2026-10-20 check-in model for the plugin (nothing is deleted; objects only change layer).

Usage: python3 -I r20_clean.py <fixture.json of in.3dm> <in.3dm> <out.3dm>
- Curves on PROGRAM::* layers (room outlines drawn at the floor) → PROGRAM OUTLINES (2D): on a program layer the plugin
  takes a closed curve at grade for site paving and flags the others.
- Open extrusions on program layers (the two roof MECHANICAL boxes have no caps) → capped copies; originals to
  OUTSIDE BUILDING (LEFTOVERS).
- Settings rows of deleted objects dropped; the 0–34' auditorium and labs kept tall.
- GRID back to Ignore (the check-in settings had it as paving again).
- Objects far outside the site (a parking surface and a working line 700 ft south) → LEFTOVERS.
"""
import json, sys
import rhino3dm as r

fx = json.load(open(sys.argv[1]))
m = r.File3dm.Read(sys.argv[2])
FT = 304.8
KEY = "ProgramDiagram\\settings"
st = json.loads(m.Strings[KEY])

def layer(path, color=(160, 160, 160)):
    for l in m.Layers:
        if l.FullPath == path: return l.Index
    l = r.Layer(); l.Name = path; l.Color = (*color, 255)
    return m.Layers.Add(l)

OUT2D, LEFT = layer("PROGRAM OUTLINES (2D)"), layer("OUTSIDE BUILDING (LEFTOVERS)")
# GRID (12' grid lines) stays off the drawings, as asked in the CW markup V2 ("删除这些灰色的线条").
for p in ("PROGRAM OUTLINES (2D)", "OUTSIDE BUILDING (LEFTOVERS)", "GRID"):
    st["layers"][p] = {"use": True, "role": "Ignore", "programKey": "", "mode": ""}
by_id = {str(o.Attributes.Id): o for o in m.Objects}
mx3 = next(o for o in fx["objects"] if o["layer"] == "Plats::MX3")["bbox"]
X0, Y0, X1, Y1 = mx3[0] / FT - 400, mx3[1] / FT - 400, mx3[3] / FT + 400, mx3[4] / FT + 400
moved = {"outlines": 0, "capped": 0, "far": 0}
for o in fx["objects"]:
    L, b = o["layer"], [v / FT for v in o["bbox"]]
    ro = by_id.get(o["id"])
    if ro is None: continue
    cx, cy = (b[0] + b[3]) / 2, (b[1] + b[4]) / 2
    if not L.startswith("SITE CONTEXT") and not (X0 <= cx <= X1 and Y0 <= cy <= Y1):
            ro.Attributes.LayerIndex = LEFT; moved["far"] += 1; print("far", L, o["id"][:8]); continue
    if not L.startswith("PROGRAM::") or L == "PROGRAM::Glass": continue
    if "Curve" in o["type"]:
        ro.Attributes.LayerIndex = OUT2D; moved["outlines"] += 1
    elif o["type"] == "Extrusion" and not o["isClosed"] and b[5] - b[2] > 2:
        g = ro.Geometry
        prof = g.Profile3d(0, 0.0)
        ext = r.Extrusion.Create(prof, b[5] - b[2], True)
        if ext.GetBoundingBox().Max.Z < b[2] + 0.01: ext = r.Extrusion.Create(prof, -(b[5] - b[2]), True)
        a = r.ObjectAttributes(); a.LayerIndex = ro.Attributes.LayerIndex; a.Name = ro.Attributes.Name or ""
        nid = str(m.Objects.AddExtrusion(ext, a))
        if o["id"] in st["names"]: st["names"][nid] = st["names"][o["id"]]
        ro.Attributes.LayerIndex = LEFT; moved["capped"] += 1
# Settings rows of objects no longer in the model go; two-storey rooms (0–34', the auditorium and the labs) stay tall
# as in the earlier rounds (L2 open to below).
live = {str(o.Attributes.Id) for o in m.Objects}
for k in ("names", "spanModes", "objectPrograms", "singleSurfaces"):
    before = len(st.get(k) or {})
    st[k] = {i: v for i, v in (st.get(k) or {}).items() if i in live}
    moved[f"stale {k}"] = before - len(st[k])
for o in fx["objects"]:
    b = [v / FT for v in o["bbox"]]
    if o["layer"] in ("PROGRAM::Seminar Room", "PROGRAM::Lab") and o["isClosed"] and b[2] < 1 and 32 < b[5] < 36:
        st["spanModes"][o["id"]] = "KeepTall"; moved.setdefault("kept tall", []).append(o["name"] or o["layer"].split("::")[1])
m.Strings[KEY] = json.dumps(st)
m.Write(sys.argv[3], 8)
print(moved)
