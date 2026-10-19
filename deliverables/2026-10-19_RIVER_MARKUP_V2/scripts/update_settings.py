"""Add the SITE CONTEXT layer roles and the axon view (viewport direction of the user's screenshot) to the plugin settings stored in the 3dm."""
import json, math, sys
import rhino3dm as r

m = r.File3dm.Read(sys.argv[1])
key = "ProgramDiagram\\settings"
k0, v0 = m.Strings[0]
assert k0 == key, k0
s = json.loads(v0)
roles = {"CITY BLOCKS": "BG.Hardscape", "STREET": "BG.Road", "SIDEWALK": "BG.Hardscape", "WALKWAY": "BG.Hardscape",
         "BANK LANDSCAPE": "BG.Landscape", "RIVER": "BG.Water", "CONTEXT BUILDINGS": "BG.Building", "TREES": "BG.Tree"}
for leaf, role in roles.items():
    s["layers"][f"SITE CONTEXT::{leaf}"] = {"use": True, "role": role, "programKey": "", "mode": "color"}
s["layers"]["SITE CONTEXT"] = {"use": True, "role": "Ignore", "programKey": "", "mode": ""}
az, el = math.radians(67), math.radians(49)
s["output"]["axonView"] = "Viewport"
s["output"]["axonFromViewport"] = True
s["output"]["axonViewDirection"] = [round(-math.cos(el) * math.cos(az), 4), round(-math.cos(el) * math.sin(az), 4), round(-math.sin(el), 4)]
m.Strings[key] = json.dumps(s)
m.Write(sys.argv[1], 8)
m2 = r.File3dm.Read(sys.argv[1])
s2 = json.loads(m2.Strings[0][1])
print(len(m2.Strings), s2["output"]["axonView"], s2["output"]["axonViewDirection"], "SITE CONTEXT::RIVER" in s2["layers"])
json.dump(s2, open(sys.argv[2], "w"), indent=1)
