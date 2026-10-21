"""RIVER 1020: the glass (PROGRAM::Glass) is the envelope. Inside it, where no room is, is indoor circulation.

Usage: python3 -I r20_interior.py <fixture.json> <in.3dm> <out.3dm>
- Per storey (L1 0–18', L2 20–34', L3 36–50') the area enclosed by the glass and the program masses (r20_envelope.py)
  becomes one PROGRAM::Circulation solid; the rooms inside are smaller, so the plugin's smaller-volume rule leaves
  circulation as the walkable rest (as in the 10-18 round). Where the glass does not close (loading) nothing is added.
- Levels as the user set them: L1 0' (ground), L1M 10' (parking mezzanine), L2 20' (entrance), L3 36', L4 52', L5 77'-1".
"""
import json, sys
import rhino3dm as r
sys.path.insert(0, __file__.rsplit('/', 1)[0])
from r20_envelope import envelope, STOREYS

fx = json.load(open(sys.argv[1]))['objects']
m = r.File3dm.Read(sys.argv[2])
KEY = "ProgramDiagram\\settings"
st = json.loads(m.Strings[KEY])

def solid(poly, z0, z1):
    pl = r.Polyline(0)
    for x, y in poly.exterior.coords: pl.Add(x, y, z0)
    ext = r.Extrusion.Create(r.PolylineCurve(pl), z1 - z0, True)
    if ext.GetBoundingBox().Max.Z < z0 + 0.01: ext = r.Extrusion.Create(r.PolylineCurve(pl), -(z1 - z0), True)
    pln = ext.GetProfilePlane(0.0); o, ax, ay = pln.Origin, pln.XAxis, pln.YAxis
    for ring in poly.interiors:
        h = r.Polyline(0)
        for x, y in ring.coords:
            dx, dy, dz = x - o.X, y - o.Y, z0 - o.Z
            h.Add(dx * ax.X + dy * ax.Y + dz * ax.Z, dx * ay.X + dy * ay.Y + dz * ay.Z, 0)
        ext.AddInnerProfile(r.PolylineCurve(h))
    return ext

li = next(l.Index for l in m.Layers if l.FullPath == "PROGRAM::Circulation")
for name, (z0, z1) in STOREYS.items():
    env, rooms, _ = envelope(fx, z0, z1)
    for p in getattr(env, 'geoms', [env]):
        if p.geom_type != 'Polygon' or p.area < 200: continue
        a = r.ObjectAttributes(); a.LayerIndex = li
        m.Objects.AddExtrusion(solid(p.simplify(0.1), z0, z1), a)
        print(f"{name}: indoor circulation envelope {p.area:,.0f} sf (rooms {rooms.area:,.0f} sf)")
st["layers"]["PROGRAM::Circulation"] = {"use": True, "role": "Program", "programKey": "CIRCULATION", "mode": ""}
st["levels"] = [{"name": "L1", "elevation": 0, "isMezzanine": False}, {"name": "L1M", "elevation": 10, "isMezzanine": True},
                {"name": "L2", "elevation": 20, "isMezzanine": False}, {"name": "L3", "elevation": 36, "isMezzanine": False},
                {"name": "L4", "elevation": 52, "isMezzanine": False}, {"name": "L5", "elevation": 77.07, "isMezzanine": False}]
m.Strings[KEY] = json.dumps(st)
m.Write(sys.argv[3], 8)
