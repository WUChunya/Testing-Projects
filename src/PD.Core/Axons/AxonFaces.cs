using NetTopologySuite.Algorithm;
using NetTopologySuite.Geometries;
using NetTopologySuite.Operation.Overlay;
using NetTopologySuite.Operation.OverlayNG;
using NetTopologySuite.Operation.Union;
using NetTopologySuite.Simplify;
using PD.Core.Geometry;
using NtsGeometry = NetTopologySuite.Geometries.Geometry;

namespace PD.Core.Axons;

/// <summary>A plan polygon extruded from Z0 to Z1, with its paints. Group decides outlines and layers.</summary>
public sealed record Prism(string Name, NtsGeometry Plan, double Z0, double Z1, string? Top, string? Side1, string? Side2, string Group,
    string? Label = null, string? LabelStyle = null);

/// <summary>A face left visible after hidden-face removal, in screen feet.</summary>
/// <param name="Side">0 = top, 1 = lit side (turns left on the page), 2 = shaded side.</param>
/// <param name="Normal">The face's plane n·P = <paramref name="D0"/> in model feet (for depth tests against trees, people…).</param>
public sealed record VisibleFace(Prism Prism, string Name, bool IsTop, NtsGeometry Screen, string? Fill, int Side = 0,
    (double X, double Y, double Z) Normal = default, double D0 = 0)
{
    /// <summary>Depth of the face centre (larger = nearer); the list is painted far → near.</summary>
    public double Depth { get; init; }
}

/// <summary>
/// Hidden-face removal for extruded prisms: every front face (tops and walls facing the viewer) is projected, sorted by
/// depth and trimmed by every nearer face, so the output has no hidden geometry and adjacent faces never overlap.
/// </summary>
public static class AxonFaces
{
    public static List<VisibleFace> Render(AxonProjection proj, IEnumerable<Prism> prisms)
    {
        var faces = new List<Face>();
        foreach (var p in SplitAtNeighbours(prisms.ToList()))
        {
            var n = 0;
            foreach (var poly in Polygons(Simplify(p.Plan)))
            {
                var suffix = n > 0 ? $"_{n}" : "";
                var c = poly.InteriorPoint.Coordinate;
                faces.Add(new Face(p, $"{p.Name}_TOP{suffix}", true, proj.Plane(poly, p.Z1), proj.Depth(c.X, c.Y, p.Z1), p.Top, 0, (0, 0, 1), p.Z1));
                var w = 0;
                foreach (var ring in Rings(poly))
                {
                    var pts = ring.Coordinates;
                    for (var i = 0; i + 1 < pts.Length; i++)
                    {
                        var (a, q) = (pts[i], pts[i + 1]);
                        var normal = (q.Y - a.Y, a.X - q.X, 0.0);
                        if (!proj.Faces(normal) || a.Distance(q) < 1e-3) continue;
                        var quad = proj.Polygon(new[] { (a.X, a.Y, p.Z0), (q.X, q.Y, p.Z0), (q.X, q.Y, p.Z1), (a.X, a.Y, p.Z1) });
                        var depth = proj.Depth((a.X + q.X) / 2, (a.Y + q.Y) / 2, (p.Z0 + p.Z1) / 2);
                        var lit = proj.ScreenX(normal) < 0;
                        faces.Add(new Face(p, $"{p.Name}_SIDE{suffix}_{++w:000}", false, quad, depth, lit ? p.Side1 : p.Side2, lit ? 1 : 2,
                            normal, normal.Item1 * a.X + normal.Item2 * a.Y));
                    }
                }
                n++;
            }
        }

        // Hidden faces: where two faces overlap on the page, the one whose plane is nearer at a point of the overlap
        // hides the other. A single depth per face (its centre) is not enough — a large plate's centre can be nearer
        // than the wall of the block standing on it — so the test is made in the overlap itself.
        // The pairwise relation can form cycles (interlocking or coplanar faces), so it only sets an order: faces are
        // taken nearest-first (front faces before the faces they hide; ties and cycles by centre depth) and each is trimmed
        // by everything taken before it — every page point ends up covered by exactly one face, never a hole.
        var behind = faces.Select(_ => new List<int>()).ToList();
        var inDegree = new int[faces.Count];
        var index = new NetTopologySuite.Index.Strtree.STRtree<int>();
        for (var i = 0; i < faces.Count; i++) index.Insert(faces[i].Screen.EnvelopeInternal, i);
        for (var i = 0; i < faces.Count; i++)
            foreach (var j in index.Query(faces[i].Screen.EnvelopeInternal))
            {
                if (j <= i) continue;
                var front = InFront(proj, faces[i], faces[j]);
                if (front == 0) continue;
                var (near, far) = front > 0 ? (i, j) : (j, i);
                behind[near].Add(far);
                inDegree[far]++;
            }
        var order = new List<int>(faces.Count);
        var done = new bool[faces.Count];
        var ready = new SortedSet<(double Key, int I)>(Enumerable.Range(0, faces.Count).Where(i => inDegree[i] == 0).Select(i => (-faces[i].Depth, i)));
        while (order.Count < faces.Count)
        {
            int next;
            if (ready.Count > 0)
            {
                next = ready.Min.I;
                ready.Remove(ready.Min);
            }
            else // a cycle: take the nearest face left by its centre
                next = Enumerable.Range(0, faces.Count).Where(i => !done[i]).OrderByDescending(i => faces[i].Depth).First();
            if (done[next]) continue;
            done[next] = true;
            order.Add(next);
            foreach (var far in behind[next])
                if (!done[far] && --inDegree[far] == 0) ready.Add((-faces[far].Depth, far));
        }

        NtsGeometry? taken = null;
        var visible = new List<VisibleFace>();
        foreach (var i in order)
        {
            var f = faces[i];
            var g = f.Screen;
            if (taken != null && taken.EnvelopeInternal.Intersects(g.EnvelopeInternal)) g = Robust(g, taken, SpatialFunction.Difference) ?? g;
            taken = taken == null ? f.Screen : Robust(taken, f.Screen, SpatialFunction.Union) ?? taken;
            g = MeshSlicer.Polygonal(g);
            if (!g.IsEmpty && g.Area > 1e-3)
                visible.Add(new VisibleFace(f.Prism, f.Name, f.IsTop, g, f.Fill, f.Side, f.Normal, f.D0) { Depth = f.Depth });
        }
        return visible.OrderBy(v => v.Depth).ToList(); // far → near
    }

    /// <summary>
    /// A prism passing through another one's top or bottom (a roof core rising out of the penthouse) is split there, so
    /// each piece is wholly above or below that face: the one-sample depth test per overlap then holds everywhere.
    /// </summary>
    static IEnumerable<Prism> SplitAtNeighbours(List<Prism> prisms)
    {
        var index = new NetTopologySuite.Index.Strtree.STRtree<int>();
        for (var i = 0; i < prisms.Count; i++) index.Insert(prisms[i].Plan.EnvelopeInternal, i);
        for (var i = 0; i < prisms.Count; i++)
        {
            var p = prisms[i];
            var env = new Envelope(p.Plan.EnvelopeInternal);
            env.ExpandBy(0.5);
            var cuts = new SortedSet<double>();
            foreach (var j in index.Query(env))
            {
                if (j == i) continue;
                var q = prisms[j];
                foreach (var z in new[] { q.Z0, q.Z1 })
                    if (z > p.Z0 + 0.05 && z < p.Z1 - 0.05 && Safe(() => p.Plan.Buffer(0.5), p.Plan).Intersects(q.Plan)) cuts.Add(Math.Round(z, 3));
            }
            if (cuts.Count == 0)
            {
                yield return p;
                continue;
            }
            var lo = p.Z0;
            var k = 0;
            foreach (var z in cuts.Append(p.Z1))
            {
                // Only the top piece carries the label (and is named like the whole prism).
                yield return z >= p.Z1 ? p with { Z0 = lo } : p with { Name = $"{p.Name}_P{++k}", Z1 = z, Label = null };
                lo = z;
            }
        }
    }

    sealed record Face(Prism Prism, string Name, bool IsTop, NtsGeometry Screen, double Depth, string? Fill, int Side,
        (double X, double Y, double Z) Normal, double D0);

    /// <summary>+1 when <paramref name="a"/> hides <paramref name="b"/> where they overlap, −1 the other way, 0 when they do not overlap.</summary>
    static int InFront(AxonProjection proj, Face a, Face b)
    {
        var overlap = Robust(a.Screen, b.Screen, SpatialFunction.Intersection);
        if (overlap == null) return 0;
        overlap = MeshSlicer.Polygonal(overlap);
        if (overlap.IsEmpty || overlap.Area < 1e-4) return 0;
        var pt = overlap.InteriorPoint.Coordinate;
        var da = proj.DepthOnPlane(pt.X, pt.Y, a.Normal, a.D0);
        var db = proj.DepthOnPlane(pt.X, pt.Y, b.Normal, b.D0);
        if (double.IsNaN(da) || double.IsNaN(db) || Math.Abs(da - db) < 1e-6)
            return a.Depth >= b.Depth ? 1 : -1; // edge-on or coplanar: fall back to the face centres
        return da > db ? 1 : -1;
    }

    /// <summary>
    /// What is left of a page shape (tree, person, label backing) standing at <paramref name="depth"/> once every visible face
    /// in front of it is taken away; null when nothing is left.
    /// </summary>
    public static NtsGeometry? Hide(AxonProjection proj, NtsGeometry screen, double depth, IReadOnlyList<VisibleFace> faces, double gap = 0)
    {
        // Sample the shape (its vertices and an inside point) and keep every face that covers a sample and lies in front
        // there. Point-in-polygon also works on the odd self-touching face where an overlay would refuse to run.
        var samples = screen.Coordinates.Select(c => c.Copy()).ToList();
        if (screen.Dimension == Dimension.Surface) samples.Add(screen.InteriorPoint.Coordinate);
        var front = new List<NtsGeometry>();
        foreach (var f in faces)
        {
            if (!f.Screen.EnvelopeInternal.Intersects(screen.EnvelopeInternal)) continue;
            var locator = new NetTopologySuite.Algorithm.Locate.IndexedPointInAreaLocator(f.Screen);
            foreach (var pt in samples)
            {
                var loc = locator.Locate(pt);
                if (loc == Location.Exterior) continue;
                var df = proj.DepthOnPlane(pt.X, pt.Y, f.Normal, f.D0);
                if (double.IsNaN(df) || df <= depth) continue;
                front.Add(f.Screen);
                break;
            }
        }
        if (front.Count == 0) return screen;
        var cover = Union(front);
        if (!cover.IsValid) cover = cover.Buffer(0);
        // Keep clear of the faces by a gap (room for the building's outline stroke).
        if (gap > 0) cover = Safe(() => cover.Buffer(gap), cover);
        var shape = screen.Dimension == Dimension.Surface && !screen.IsValid ? screen.Buffer(0) : screen;
        var left = Robust(shape, cover, SpatialFunction.Difference) ?? Safe(() => shape.Difference(cover), shape);
        left = screen.Dimension == Dimension.Surface ? MeshSlicer.Polygonal(left) : MeshSlicer.Lineal(left);
        return left.IsEmpty ? null : left;
    }

    /// <summary>Union of a set of faces (a group's silhouette).</summary>
    public static NtsGeometry Union(IEnumerable<NtsGeometry> parts)
    {
        var list = parts.Where(g => !g.IsEmpty).ToList();
        return list.Count == 0 ? MeshSlicer.Factory.CreatePolygon() : Safe(() => CascadedPolygonUnion.Union(list), list[0]);
    }

    /// <summary>Every visible edge once (dissolved boundaries of the faces), for hidden-line drawings.</summary>
    public static NtsGeometry Edges(IEnumerable<NtsGeometry> faces)
    {
        var lines = faces.Where(g => !g.IsEmpty).Select(g => g.Boundary).Where(b => !b.IsEmpty).ToList();
        return lines.Count == 0 ? MeshSlicer.Factory.CreateMultiLineString() : Safe(() => UnaryUnionOp.Union(lines), MeshSlicer.Factory.BuildGeometry(lines));
    }

    /// <summary>Overlay that falls back to snapping (OverlayNGRobust) instead of failing on near-coincident edges.</summary>
    static NtsGeometry? Robust(NtsGeometry a, NtsGeometry b, SpatialFunction op)
    {
        // NTS refuses a GeometryCollection argument outright, so both sides are reduced to their polygons first.
        (a, b) = (MeshSlicer.Polygonal(a), MeshSlicer.Polygonal(b));
        try { return OverlayNG.Overlay(a, b, op); }
        catch (Exception e) when (e is TopologyException or ArgumentException) { }
        try { return OverlayNGRobust.Overlay(a, b, op); }
        catch (Exception e) when (e is TopologyException or ArgumentException) { return null; }
    }

    /// <summary>
    /// Fold edges: where a top meets a side, or a lit side a shaded one, plus the outline — the boundaries of the unions
    /// of tops, lit sides and shaded sides (colour changes on one plane and floor joints drop out).
    /// </summary>
    public static NtsGeometry Folds(IEnumerable<VisibleFace> faces)
    {
        var list = faces.ToList();
        var parts = Enumerable.Range(0, 3).Select(side => Union(list.Where(f => f.Side == side).Select(f => f.Screen)));
        return Edges(parts);
    }

    public static NtsGeometry Simplify(NtsGeometry g) => Safe(() => TopologyPreservingSimplifier.Simplify(g, 0.1), g);

    public static IEnumerable<Polygon> Polygons(NtsGeometry g)
    {
        for (var i = 0; i < g.NumGeometries; i++)
            if (g.GetGeometryN(i) is Polygon p && p.Area > 1e-3) yield return p;
    }

    /// <summary>Shell counter-clockwise, holes clockwise, so (dy, −dx) is always the outward normal of an edge.</summary>
    public static IEnumerable<LineString> Rings(Polygon p)
    {
        yield return Orientation.IsCCW(p.ExteriorRing.CoordinateSequence) ? p.ExteriorRing : (LineString)p.ExteriorRing.Reverse();
        foreach (var h in p.InteriorRings)
            yield return Orientation.IsCCW(h.CoordinateSequence) ? (LineString)h.Reverse() : h;
    }

    public static NtsGeometry Safe(Func<NtsGeometry> op, NtsGeometry fallback)
    {
        try { return op(); }
        catch (Exception e) when (e is TopologyException or ArgumentException) { return fallback; }
        catch (ArgumentException) { return fallback; }
    }
}
