"""Export a .3dm model to a compact JSON fixture for PD.Core tests.

Usage: python3 -I tools/fixture_from_3dm.py <in.3dm> <out.json>

Per object: id, layer path, type, bbox, isClosed, faceCount, mesh (render
meshes, coordinates rounded to 1 mm) or polyline points for curves.
Coordinates stay in model units (see "units"); Core converts to feet.
If the file exceeds MAX_BYTES, CONTEXT layer meshes are dropped (bbox kept).
"""
import json
import sys

import rhino3dm as r

MAX_BYTES = 5 * 1024 * 1024
MM_PER_UNIT = {"Millimeters": 1.0, "Centimeters": 10.0, "Meters": 1000.0,
               "Inches": 25.4, "Feet": 304.8}
CURVE_SAMPLES = 64


def unit_name(model):
    return str(model.Settings.ModelUnitSystem).split(".")[-1]


def rnd(v, step):
    return int(round(v / step))


def bbox(geom, step):
    bb = geom.GetBoundingBox()
    return [rnd(bb.Min.X, step), rnd(bb.Min.Y, step), rnd(bb.Min.Z, step),
            rnd(bb.Max.X, step), rnd(bb.Max.Y, step), rnd(bb.Max.Z, step)]


def append_mesh(mesh, step, verts, faces):
    """Append a rhino3dm mesh to flat vertex/face lists; returns nothing."""
    if mesh is None:
        return
    base = len(verts) // 3
    for i in range(len(mesh.Vertices)):
        p = mesh.Vertices[i]
        verts.extend((rnd(p.X, step), rnd(p.Y, step), rnd(p.Z, step)))
    for i in range(mesh.Faces.Count):
        a, b, c, d = mesh.Faces[i]
        f = [base + a, base + b, base + c]
        if d != c:
            f.append(base + d)
        faces.append(f)


def mesh_of(geom, step):
    verts, faces = [], []
    kind = type(geom).__name__
    if kind == "Brep":
        for i in range(len(geom.Faces)):
            append_mesh(geom.Faces[i].GetMesh(r.MeshType.Render), step, verts, faces)
    elif kind == "Extrusion":
        append_mesh(geom.GetMesh(r.MeshType.Render), step, verts, faces)
        if not faces:  # no cached render mesh: fall back to the extrusion's Brep faces
            brep = geom.ToBrep(False)
            if brep is not None:
                for i in range(len(brep.Faces)):
                    append_mesh(brep.Faces[i].GetMesh(r.MeshType.Render), step, verts, faces)
    elif kind == "Mesh":
        append_mesh(geom, step, verts, faces)
    if not faces:
        return None
    return {"v": verts, "f": faces}


def box_mesh(bb):
    x0, y0, z0, x1, y1, z1 = bb
    v = []
    for z in (z0, z1):
        v.extend((x0, y0, z, x1, y0, z, x1, y1, z, x0, y1, z))
    f = [[0, 3, 2, 1], [4, 5, 6, 7], [0, 1, 5, 4], [1, 2, 6, 5], [2, 3, 7, 6], [3, 0, 4, 7]]
    return {"v": v, "f": f}


def curve_points(geom, step):
    dom = geom.Domain
    n = 1 if type(geom).__name__ == "LineCurve" else CURVE_SAMPLES
    if type(geom).__name__ == "PolylineCurve":
        pts = [geom.Point(i) for i in range(geom.PointCount)]
    else:
        pts = [geom.PointAt(dom.T0 + (dom.T1 - dom.T0) * i / n) for i in range(n + 1)]
    return [[rnd(p.X, step), rnd(p.Y, step), rnd(p.Z, step)] for p in pts]


def closed_and_faces(geom):
    kind = type(geom).__name__
    if kind == "Brep":
        return bool(geom.IsSolid), len(geom.Faces)
    if kind == "Extrusion":
        return bool(geom.IsSolid), len(geom.ToBrep(False).Faces)
    if kind == "Mesh":
        return bool(geom.IsClosed), geom.Faces.Count
    if hasattr(geom, "IsClosed"):
        return bool(geom.IsClosed), 0
    return False, 0


def export(path_in):
    model = r.File3dm.Read(path_in)
    units = unit_name(model)
    step = 1.0 / MM_PER_UNIT.get(units, 1.0)  # 1 mm in model units
    layers = []
    for lyr in model.Layers:
        c = lyr.Color
        layers.append({"index": lyr.Index, "path": lyr.FullPath, "visible": bool(lyr.Visible),
                       "color": "#%02X%02X%02X" % (c[0], c[1], c[2])})
    objects = []
    for o in model.Objects:
        g, a = o.Geometry, o.Attributes
        try:
            bb = bbox(g, step)
        except TypeError:
            continue
        closed, nfaces = closed_and_faces(g)
        rec = {"id": str(a.Id), "layer": model.Layers[a.LayerIndex].FullPath,
               "type": type(g).__name__, "name": a.Name or "", "bbox": bb,
               "isClosed": closed, "faceCount": nfaces}
        if "Curve" in rec["type"]:
            rec["points"] = curve_points(g, step)
        else:
            rec["mesh"] = mesh_of(g, step)
            # Solids whose render mesh rhino3dm cannot build (some extrusions): stand in with the bounding box,
            # so the fixture still carries a volume. Rhino itself meshes these properly.
            if rec["mesh"] is None and closed:
                rec["mesh"] = box_mesh(bb)
                rec["meshFromBBox"] = True
        objects.append(rec)
    # Coordinates are integer millimetres whatever the model unit is, so the fixture always declares millimetres;
    # "sourceUnits" keeps the model's own unit for reference.
    return {"source": path_in.replace("\\", "/").split("/")[-1], "units": "Millimeters", "sourceUnits": units,
            "coordStepMm": 1, "layers": layers, "objects": objects}


def dump(data):
    return json.dumps(data, separators=(",", ":"))


# Layers whose geometry must survive the shrink step: our new construction and the levels.
KEEP = ("PROGRAM", "PRG", "SLAB", "FLOOR PLATE", "CORE", "COLUMN", "GLASS", "FACADE", "SEC", "ELEV")
# Make2D / clipping-plane output (see styles/layer_keywords.json ignorePathPattern): dropped first, it is 2D line work.
DROP_FIRST = ("::VISIBLE::", "::HIDDEN::", "::MAKE2D::", "CLIPPINGPLANEINTERSECTION", "VIEWPORTRECTANGLE")


def layer_bytes(data):
    """Bytes per layer, heaviest first, skipping the layers we must keep."""
    sizes = {}
    for rec in data["objects"]:
        sizes[rec["layer"]] = sizes.get(rec["layer"], 0) + len(dump(rec))
    return sorted(sizes.items(), key=lambda kv: -kv[1])


def strip(data, layer):
    for rec in data["objects"]:
        if rec["layer"] == layer:
            rec["mesh"] = None
            rec.pop("points", None)


def shrink(data, text):
    """Drop geometry (bboxes stay) until the fixture fits: 2D output layers first, then the heaviest background layers."""
    dropped = []
    for layer, _ in layer_bytes(data):
        up = ("::" + layer.upper() + "::").replace(" ", "")
        if not any(d in up for d in DROP_FIRST):
            continue
        strip(data, layer)
        dropped.append(layer)
    text = dump(data)
    for layer, _ in layer_bytes(data):
        if len(text) <= MAX_BYTES:
            break
        if any(k in layer.upper() for k in KEEP):
            continue
        strip(data, layer)
        dropped.append(layer)
        text = dump(data)
    if dropped:
        data["droppedMeshes"] = dropped
    return dump(data)


def main():
    src, dst = sys.argv[1], sys.argv[2]
    data = export(src)
    text = dump(data)
    if len(text) > MAX_BYTES:
        text = shrink(data, text)
    with open(dst, "w", encoding="utf-8") as fh:
        fh.write(text)
    print(f"{dst}: {len(data['objects'])} objects, {len(text) / 1e6:.2f} MB")


if __name__ == "__main__":
    main()
