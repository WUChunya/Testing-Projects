"""Plugin settings for the CAMPUS model, matching the RIVER deliverables: layer roles, section lines, output
(11x17 Landscape, 1" = 100'-0" drawings, 1" = 50'-0" sections, the RIVER axon direction).

Usage: python3 -I campus_settings.py <campus_site.3dm> [<out.3dm>]   (writes <out>.settings.json too)
"""
import json, sys
import rhino3dm as r

src = sys.argv[1]; out = sys.argv[2] if len(sys.argv) > 2 else src
m = r.File3dm.Read(src)
KEY = "ProgramDiagram\\settings"
s = json.loads(m.Strings[KEY])
cuts = json.load(open(src + ".cuts.json"))
lines = json.load(open(__file__.rsplit("/", 1)[0] + "/campus_cuts.json"))

def role(path, role, key="", mode="", use=True):
    s["layers"][path] = {"use": use, "role": role, "programKey": key, "mode": mode}

# Building (same roles as the RIVER model's matching layers).
for p, k in {"PROGRAM::Lab": "LAB", "PROGRAM::Enclosed Office": "OFFICE", "PROGRAM::Conference": "CONFERENCE",
             "PROGRAM::Break Room": "LOBBY", "PROGRAM::Circulation": "CIRCULATION",
             "PROGRAM::Seminar Room": "ASSEMBLY", "PROGRAM::Support": "BUILDING_SUPPORT", "PROGRAM::Green Roof": "GREEN_ROOF",
             "Shell Space": "SHELL"}.items():
    role(p, "Program", k)
# Outdoor circulation surfaces are terraces / walkways on the roofs below: paving, not rooms.
role("PROGRAM::Outdoor Circulation", "NC.Hardscape", mode="color")
role("CORE", "NC.Core")
role("FLOOR PLATE", "NC.Slab", mode="grayscale")
role("Columns", "NC.Structure", mode="grayscale")
# PROGRAM::Glass is an earlier scheme's glazing (east of today's building); Facade::Glass is the curtain wall.
role("PROGRAM::Glass", "Ignore")
role("Facade::Glass", "NC.Facade", mode="line")
for p in ("Facade::Mullion", "Facade::Glass Joint", "Facade::Wood Verticals 1", "Facade::Wood Verticals 2", "Facade::fix", "Facade::Help Lines"):
    role(p, "Ignore")
role("Facade", "Ignore")
# Entourage: modelled people and trees are drawn; cars and auditorium seats are not.
role("ENTOURAGE", "Ignore")
role("ENTOURAGE::PEOPLE", "Entourage", mode="color")
role("ENTOURAGE::TREES", "NC.Tree", mode="color")
role("ENTOURAGE::CARS", "Ignore")
role("ENTOURAGE::AUDITORIUM", "Ignore")
# Site: the Town site plan replaces the model's simplified surroundings.
for p in ("SURROUNDING", "SURROUNDING::LOT", "SURROUNDING::RIVER", "SURROUNDING::ROAD", "PIC", "CLIPPING PLANE", "TEXT", "12x12 layout",
          "GRID (12x12')", "PROGRAM", "SITE CONTEXT", "PROJECT SITE", "DETAIL"):
    role(p, "Ignore")
role("DETAIL::CUTLINE", "Ignore", use=False)
for leaf, rl in {"CITY BLOCKS": "BG.Hardscape", "STREET": "BG.Road", "SIDEWALK": "BG.Hardscape", "WALKWAY": "BG.Hardscape",
                 "BANK LANDSCAPE": "BG.Landscape", "RIVER": "BG.Water", "CONTEXT BUILDINGS": "BG.Building", "TREES": "BG.Tree"}.items():
    role(f"SITE CONTEXT::{leaf}", rl, mode="color")
role("PROPERTY LINE", "NC.PropertyLine", mode="color")
role("SETBACK", "BG.Line", mode="line")
role("PROJECT SITE::PLAZA", "NC.Hardscape", mode="color")

# Leftovers outside the building (83 columns south of the parcel, a copy of the north shell space 200 ft further
# north): moved to their own layer, not deleted.
plates = [o.Geometry.GetBoundingBox() for o in m.Objects if m.Layers[o.Attributes.LayerIndex].FullPath == "FLOOR PLATE"]
x0, y0 = min(b.Min.X for b in plates) - 5, min(b.Min.Y for b in plates) - 5
x1, y1 = max(b.Max.X for b in plates) + 5, max(b.Max.Y for b in plates) + 5
lyr = r.Layer(); lyr.Name = "OUTSIDE BUILDING (LEFTOVERS)"; lyr.Color = (160, 160, 160, 255)
li = m.Layers.Add(lyr)
moved = 0
for o in m.Objects:
    if m.Layers[o.Attributes.LayerIndex].FullPath not in ("Columns", "Shell Space") or o.Attributes.IsInstanceDefinitionObject: continue
    b = o.Geometry.GetBoundingBox(); cx, cy = (b.Min.X + b.Max.X) / 2, (b.Min.Y + b.Max.Y) / 2
    if not (x0 <= cx <= x1 and y0 <= cy <= y1):
        o.Attributes.LayerIndex = li; moved += 1
role("OUTSIDE BUILDING (LEFTOVERS)", "Ignore")
print("moved outside the building:", moved)

# Two-storey rooms (L1–L2): the seminar room / auditorium and the east lab stay one tall room, open to below on L2
# (same as the RIVER auditorium and core labs).
s["spanModes"] = {"65ad0753-f844-485a-98dd-91e05aa6eb9f": "KeepTall", "84089f72-53d1-493e-9b1c-35631e29dcbb": "KeepTall"}

s["sections"] = []
for c in lines:
    (x0, y0), (x1, y1) = c["from"], c["to"]
    el = c["name"].startswith("E")
    s["sections"].append({"name": c["name"], "kind": "Elevation" if el else "Section", "isElevation": el, "objectId": cuts[c["name"]],
                          "line": [x0, y0, x1, y1], "lookLeft": True, "autoSide": True, "depthFt": 1000})
o = s["output"]
o.update({"scheme": "OPT-1", "scale": "1\" = 100'-0\"", "sectionScale": "1\" = 50'-0\"", "artboard": "11x17 Landscape",
          "exportPng": True, "entourage": True, "runIllustrator": True,
          "drawings": ["SitePlan", "Plan", "Section", "Elevation", "SiteAxon", "AxonStacked", "AxonExploded", "AxonExplodedB",
                       "AxonLevels", "ProgramChart", "Pie"],
          # The RIVER sheets' view direction (user's axon screenshot), so both schemes read the same way.
          "axonView": "Viewport", "axonFromViewport": True, "axonViewDirection": [-0.2563, -0.6039, -0.7547]})
m.Strings[KEY] = json.dumps(s)
m.Write(out, 8)
json.dump(json.loads(r.File3dm.Read(out).Strings[KEY]), open(out + ".settings.json", "w"), indent=1)
print("settings written", out, len(s["layers"]), "layers", [x["name"] for x in s["sections"]])
