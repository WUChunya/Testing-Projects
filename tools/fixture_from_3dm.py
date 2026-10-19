"""Export a .3dm model to a compact JSON fixture for PD.Core tests.

Usage: python3 -I tools/fixture_from_3dm.py <in.3dm> <out.json> [--full]

Per object: id, layer path, type, bbox, isClosed, faceCount, mesh (render
meshes, coordinates rounded to 1 mm) or polyline points for curves.
Coordinates stay in model units (see "units"); Core converts to feet.
If the file exceeds MAX_BYTES, CONTEXT layer meshes are dropped (bbox kept).
Block instances are exploded like the plugin does (nested transforms applied, pieces keep their own layer, id
"<instance id>#<n>"); a piece mesh over INSTANCE_MESH_MAX_FACES faces (people, trees, cars) is kept as its box.
"""
import json
import sys

import rhino3dm as r

MAX_BYTES = 5 * 1024 * 1024
MM_PER_UNIT = {"Millimeters": 1.0, "Centimeters": 10.0, "Meters": 1000.0,
               "Inches": 25.4, "Feet": 304.8}
CURVE_SAMPLES = 64
INSTANCE_MESH_MAX_FACES = 400


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
        if not faces:  # written by rhino3dm (no meshes at all): mesh the profiles along the path
            extrusion_mesh(geom, step, verts, faces)
    elif kind == "Mesh":
        append_mesh(geom, step, verts, faces)
    if not faces:
        return None
    return {"v": verts, "f": faces}


def profile_points(crv):
    if type(crv).__name__ == "PolylineCurve":
        pts = [crv.Point(i) for i in range(crv.PointCount)]
    else:
        dom = crv.Domain
        pts = [crv.PointAt(dom.T0 + (dom.T1 - dom.T0) * i / CURVE_SAMPLES) for i in range(CURVE_SAMPLES + 1)]
    if len(pts) > 1 and pts[0].DistanceTo(pts[-1]) < 1e-9:
        pts = pts[:-1]
    return pts


def extrusion_mesh(geom, step, verts, faces):
    """Closed mesh of an extrusion from its profiles (outer + holes), caps triangulated with shapely if present."""
    try:
        import shapely
        from shapely.geometry import Polygon
    except ImportError:
        return
    rings = [profile_points(geom.Profile3d(i, 0.0)) for i in range(geom.ProfileCount)]
    if not rings or len(rings[0]) < 3:
        return
    p0, p1 = geom.PathStart, geom.PathEnd
    d = r.Vector3d(p1.X - p0.X, p1.Y - p0.Y, p1.Z - p0.Z)
    poly = Polygon([(p.X, p.Y) for p in rings[0]], [[(p.X, p.Y) for p in h] for h in rings[1:]])
    if not poly.is_valid:
        return
    z0 = rings[0][0].Z

    def vid(x, y, z):
        verts.extend((rnd(x, step), rnd(y, step), rnd(z, step)))
        return len(verts) // 3 - 1

    for tri in shapely.constrained_delaunay_triangles(poly).geoms:
        c = list(tri.exterior.coords)[:3]
        if not Polygon(c).is_valid or Polygon(c).area < 1e-9:
            continue
        bottom = [vid(x, y, z0) for x, y in c]
        top = [vid(x + d.X, y + d.Y, z0 + d.Z) for x, y in c]
        faces.append(bottom[::-1])
        faces.append(top)
    for ring in rings:
        n = len(ring)
        for i in range(n):
            a, b = ring[i], ring[(i + 1) % n]
            q = [vid(a.X, a.Y, a.Z), vid(b.X, b.Y, b.Z), vid(b.X + d.X, b.Y + d.Y, b.Z + d.Z), vid(a.X + d.X, a.Y + d.Y, a.Z + d.Z)]
            faces.append(q)


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


def xmul(a, b):
    """a × b (rhino3dm Transform has no product operator)."""
    t = r.Transform(1.0)
    for i in range(4):
        for j in range(4):
            setattr(t, f"M{i}{j}", sum(getattr(a, f"M{i}{k}") * getattr(b, f"M{k}{j}") for k in range(4)))
    return t


def flatten(model):
    """(id, geometry, layer index, name, from an instance) per drawable object; block instances exploded."""
    defs = {str(d.Id): d for d in model.InstanceDefinitions}
    by_id = {str(o.Attributes.Id): o for o in model.Objects}

    def pieces(ref, xform, depth):
        d = defs.get(str(ref.ParentIdefId))
        if d is None or depth > 8:
            return
        for pid in d.GetObjectIds():
            o = by_id.get(str(pid))
            if o is None:
                continue
            x = xmul(xform, o.Geometry.Xform) if isinstance(o.Geometry, r.InstanceReference) else None
            if x is not None:
                yield from pieces(o.Geometry, x, depth + 1)
                continue
            g = o.Geometry.Duplicate()
            g.Transform(xform)
            yield o.Attributes, g

    for o in model.Objects:
        a = o.Attributes
        if a.IsInstanceDefinitionObject:
            continue
        if isinstance(o.Geometry, r.InstanceReference):
            for i, (pa, g) in enumerate(pieces(o.Geometry, o.Geometry.Xform, 0)):
                yield f"{a.Id}#{i}", g, pa.LayerIndex, pa.Name or a.Name or "", True
            continue
        yield str(a.Id), o.Geometry, a.LayerIndex, a.Name or "", False


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
    for oid, g, li, name, piece in flatten(model):
        try:
            bb = bbox(g, step)
        except TypeError:
            continue
        closed, nfaces = closed_and_faces(g)
        rec = {"id": oid, "layer": model.Layers[li].FullPath,
               "type": type(g).__name__, "name": name, "bbox": bb,
               "isClosed": closed, "faceCount": nfaces}
        if piece and isinstance(g, r.Mesh) and len(g.Faces) > INSTANCE_MESH_MAX_FACES:
            rec["mesh"] = box_mesh(bb)
            rec["meshFromBBox"] = True
            objects.append(rec)
            continue
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
    """Meshes only: curve points are cheap and site curves (context areas, property line) are drawn from them."""
    for rec in data["objects"]:
        if rec["layer"] == layer:
            rec["mesh"] = None


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
    args = [a for a in sys.argv[1:] if not a.startswith("--")]
    src, dst = args[0], args[1]
    data = export(src)
    text = dump(data)
    # --full: keep every mesh (a working copy of a big model; test fixtures stay under MAX_BYTES).
    if len(text) > MAX_BYTES and "--full" not in sys.argv:
        text = shrink(data, text)
    with open(dst, "w", encoding="utf-8") as fh:
        fh.write(text)
    print(f"{dst}: {len(data['objects'])} objects, {len(text) / 1e6:.2f} MB")


if __name__ == "__main__":
    main()
