"""Shared helpers for the RIVER 1021 model edits (model units: feet; fixture JSON in mm)."""
import json, math
import numpy as np
import rhino3dm as r
from shapely.geometry import Polygon, MultiPolygon, LineString, MultiLineString, Point, box, GeometryCollection
from shapely.ops import unary_union, linemerge
from shapely import affinity

FT = 304.8
KEY = "ProgramDiagram\\settings"


def parts(g, kind=Polygon):
    if g is None or g.is_empty: return []
    if isinstance(g, kind): return [g]
    return [p for p in getattr(g, 'geoms', []) if isinstance(p, kind)]


def lines_of(g):
    if g is None or g.is_empty: return []
    if isinstance(g, LineString): return [g]
    if isinstance(g, (MultiLineString, GeometryCollection)): return [x for p in g.geoms for x in lines_of(p)]
    if isinstance(g, Polygon): return [LineString(g.exterior.coords)] + [LineString(i.coords) for i in g.interiors]
    if isinstance(g, MultiPolygon): return [x for p in g.geoms for x in lines_of(p)]
    return []


def mesh_arrays(o):
    m = o.get('mesh')
    if not m: return None, None
    return np.array(m['v'], float).reshape(-1, 3) / FT, m['f']


def ordered(v, f):
    """Face vertex indices in cyclic order (quads in the fixture may come unordered)."""
    if len(f) < 4: return list(f)
    p = v[f]; c = p.mean(0)
    n = np.zeros(3)
    for i in range(len(p)):
        n += np.cross(p[i] - c, p[(i + 1) % len(p)] - c)
    if np.linalg.norm(n) < 1e-9:
        a, b = p[1] - p[0], p[2] - p[0]; n = np.cross(a, b)
    n = n / (np.linalg.norm(n) or 1)
    u = p[0] - c; u = u - n * (u @ n); u /= (np.linalg.norm(u) or 1); w = np.cross(n, u)
    ang = [math.atan2((q - c) @ w, (q - c) @ u) for q in p]
    return [f[i] for i in np.argsort(ang)]


def triangles(o):
    v, fs = mesh_arrays(o)
    if v is None: return []
    out = []
    for f in fs:
        f = ordered(v, f)
        for k in range(1, len(f) - 1):
            out.append(v[[f[0], f[k], f[k + 1]]])
    return out


def footprint(o):
    tris = [Polygon(t[:, :2]) for t in triangles(o)]
    tris = [t.buffer(0) for t in tris if t.area > 1e-4]
    return unary_union(tris).buffer(0.01).buffer(-0.01) if tris else None


class Model:
    def __init__(self, fixture, path):
        self.fx = json.load(open(fixture))['objects']
        self.byid = {o['id']: o for o in self.fx}
        self.m = r.File3dm.Read(path)
        self.obj = {str(x.Attributes.Id): x for x in self.m.Objects}
        self.st = json.loads(self.m.Strings[KEY])
        self.layers = {self.m.Layers[i].FullPath: i for i in range(len(self.m.Layers))}
        self.LEFT = self.layer("OUTSIDE BUILDING (LEFTOVERS)")
        self.log = []

    def find(self, prefix):
        hits = [i for i in self.byid if i.startswith(prefix)]
        assert len(hits) == 1, (prefix, hits)
        return hits[0]

    def fo(self, prefix):
        return self.byid[self.find(prefix)]

    def layer(self, path, color=(120, 120, 120)):
        if path in self.layers: return self.layers[path]
        parent, _, name = path.rpartition('::')
        l = r.Layer(); l.Name = name; l.Color = (*color, 255)
        if parent: l.ParentLayerId = self.m.Layers[self.layer(parent)].Id
        self.layers[path] = self.m.Layers.Add(l)
        return self.layers[path]

    def role(self, path, role, key="", mode="color"):
        self.layer(path)
        self.st['layers'][path] = {"use": True, "role": role, "programKey": key, "mode": mode}

    def attrs(self, path, name=""):
        a = r.ObjectAttributes(); a.LayerIndex = self.layer(path)
        if name: a.Name = name
        return a

    def retire(self, oid):
        """Move an object to LEFTOVERS (never deleted)."""
        self.obj[oid].Attributes.LayerIndex = self.LEFT

    def remap(self, old, new):
        for k in ("names", "spanModes", "objectPrograms", "singleSurfaces"):
            d = self.st.get(k) or {}
            if old in d: d[new] = d[old]

    # ---------------------------------------------------------------- geometry builders
    @staticmethod
    def polyline(coords, z):
        p = r.Polyline(len(coords))
        for x, y in coords: p.Add(x, y, z)
        return p

    def extrusion(self, poly, z0, z1):
        poly = poly.simplify(0.05)
        c = list(poly.exterior.coords)
        if not Polygon(c).exterior.is_ccw: c = c[::-1]
        ext = r.Extrusion.Create(r.PolylineCurve(self.polyline(c, z0)), z1 - z0, True)
        if ext.GetBoundingBox().Max.Z < z0 + 0.01:
            ext = r.Extrusion.Create(r.PolylineCurve(self.polyline(c, z0)), -(z1 - z0), True)
        pln = ext.GetProfilePlane(0.0); o, ax, ay = pln.Origin, pln.XAxis, pln.YAxis
        for ring in poly.interiors:
            h = r.Polyline(0)
            for x, y in ring.coords:
                dx, dy, dz = x - o.X, y - o.Y, z0 - o.Z
                h.Add(dx * ax.X + dy * ax.Y + dz * ax.Z, dx * ay.X + dy * ay.Y + dz * ay.Z, 0)
            ext.AddInnerProfile(r.PolylineCurve(h))
        return ext

    def add_solid(self, g, z0, z1, path, name="", min_area=4):
        ids = []
        for p in parts(g):
            if p.area < min_area: continue
            ids.append(str(self.m.Objects.AddExtrusion(self.extrusion(p, z0, z1), self.attrs(path, name))))
        return ids

    def add_area(self, g, path, name="", z=0.0, min_area=4):
        """Closed curves (no holes: cut through) for site areas."""
        from inv import no_holes
        ids = []
        for p in parts(g):
            if p.area < min_area: continue
            for q in no_holes(p.simplify(0.1)):
                ids.append(str(self.m.Objects.AddCurve(r.PolylineCurve(self.polyline(list(q.exterior.coords), z)), self.attrs(path, name))))
        return ids

    def add_line(self, ls, z, path, name=""):
        return str(self.m.Objects.AddCurve(r.PolylineCurve(self.polyline(list(ls.coords), z)), self.attrs(path, name)))

    def add_walls(self, lines, z0, z1, path, name=""):
        """Vertical mesh walls along plan lines."""
        ids = []
        for ls in lines:
            cs = list(ls.coords)
            if len(cs) < 2 or ls.length < 0.5: continue
            mesh = r.Mesh()
            for x, y in cs:
                mesh.Vertices.Add(x, y, z0); mesh.Vertices.Add(x, y, z1)
            for i in range(len(cs) - 1):
                mesh.Faces.AddFace(2 * i, 2 * i + 2, 2 * i + 3, 2 * i + 1)
            mesh.Normals.ComputeNormals(); mesh.Compact()
            ids.append(str(self.m.Objects.AddMesh(mesh, self.attrs(path, name))))
        return ids

    def replace_solid(self, oid, g, z0=None, z1=None, path=None, name=None):
        """Object → extrusion(s) of the new footprint, same layer/name/settings; the old one to LEFTOVERS."""
        o = self.byid[oid]; b = [v / FT for v in o['bbox']]
        z0 = b[2] if z0 is None else z0; z1 = b[5] if z1 is None else z1
        path = path or o['layer']; name = o['name'] if name is None else name
        ids = self.add_solid(g, z0, z1, path, name)
        for n in ids: self.remap(oid, n)
        self.retire(oid)
        return ids

    def clip_mesh(self, oid, keep, path=None, name=None):
        """Site mesh (slopes, ramps) clipped in plan to `keep`; z from each triangle's plane. Old one to LEFTOVERS."""
        o = self.byid[oid]
        mesh = r.Mesh(); n = 0
        for t in triangles(o):
            tp = Polygon(t[:, :2])
            if tp.area < 1e-4: continue
            a = np.c_[t[:, :2], np.ones(3)]
            try: coef = np.linalg.solve(a, t[:, 2])
            except np.linalg.LinAlgError: continue
            inter = tp.intersection(keep)
            for p in parts(inter):
                if p.area < 1e-3: continue
                from shapely.ops import triangulate
                for tri in triangulate(p):
                    if not p.contains(tri.representative_point()): continue
                    cs = list(tri.exterior.coords)[:3]
                    for x, y in cs: mesh.Vertices.Add(x, y, coef[0] * x + coef[1] * y + coef[2])
                    mesh.Faces.AddFace(n, n + 1, n + 2); n += 3
        if n == 0:
            self.retire(oid); return None
        mesh.Normals.ComputeNormals(); mesh.Compact()
        nid = str(self.m.Objects.AddMesh(mesh, self.attrs(path or o['layer'], o['name'] if name is None else name)))
        self.remap(oid, nid); self.retire(oid)
        return nid

    def translate(self, oid, dx, dy=0.0, dz=0.0):
        self.obj[oid].Geometry.Translate(r.Vector3d(dx, dy, dz))

    def write(self, path):
        self.m.Strings[KEY] = json.dumps(self.st)
        self.m.Write(path, 8)


def rounded(g, rad):
    """Convex and concave corners rounded by rad."""
    return g.buffer(-rad, join_style=1).buffer(2 * rad, join_style=1).buffer(-rad, join_style=1)


def stair_symbol(outline, step, direction):
    """Outline + tread lines across `direction` (unit vector of travel) every `step` ft."""
    dx, dy = direction
    nx, ny = -dy, dx
    c = outline.centroid
    proj = [(x - c.x) * dx + (y - c.y) * dy for x, y in outline.exterior.coords]
    a, b = min(proj), max(proj)
    lines = [LineString(outline.exterior.coords)]
    s = a + step
    while s < b - 0.5:
        L = LineString([(c.x + dx * s - nx * 500, c.y + dy * s - ny * 500), (c.x + dx * s + nx * 500, c.y + dy * s + ny * 500)])
        lines += lines_of(L.intersection(outline))
        s += step
    # travel arrow along the centre
    lines.append(LineString([(c.x + dx * (a + 2), c.y + dy * (a + 2)), (c.x + dx * (b - 3), c.y + dy * (b - 3))]))
    tip = (c.x + dx * (b - 3), c.y + dy * (b - 3))
    lines.append(LineString([(tip[0] - dx * 3 + nx * 1.5, tip[1] - dy * 3 + ny * 1.5), tip, (tip[0] - dx * 3 - nx * 1.5, tip[1] - dy * 3 - ny * 1.5)]))
    return lines
