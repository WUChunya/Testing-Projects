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
public sealed record VisibleFace(Prism Prism, string Name, bool IsTop, NtsGeometry Screen, string? Fill, int Side = 0);

/// <summary>
/// Hidden-face removal for extruded prisms: every front face (tops and walls facing the viewer) is projected, sorted by
/// depth and trimmed by every nearer face, so the output has no hidden geometry and adjacent faces never overlap.
/// </summary>
public static class AxonFaces
{
    public static List<VisibleFace> Render(AxonProjection proj, IEnumerable<Prism> prisms)
    {
        var faces = new List<(Prism p, string name, bool top, NtsGeometry g, double depth, string? fill, int side)>();
        foreach (var p in prisms)
        {
            var n = 0;
            foreach (var poly in Polygons(Simplify(p.Plan)))
            {
                var suffix = n > 0 ? $"_{n}" : "";
                var c = poly.InteriorPoint.Coordinate;
                faces.Add((p, $"{p.Name}_TOP{suffix}", true, proj.Plane(poly, p.Z1), proj.Depth(c.X, c.Y, p.Z1), p.Top, 0));
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
                        faces.Add((p, $"{p.Name}_SIDE{suffix}_{++w:000}", false, quad, depth, lit ? p.Side1 : p.Side2, lit ? 1 : 2));
                    }
                }
                n++;
            }
        }

        // Near → far: trim by everything nearer.
        NtsGeometry? nearer = null;
        var visible = new List<VisibleFace>();
        foreach (var f in faces.OrderByDescending(f => f.depth))
        {
            var g = f.g;
            if (nearer != null && nearer.EnvelopeInternal.Intersects(g.EnvelopeInternal)) g = Robust(g, nearer, SpatialFunction.Difference) ?? g;
            nearer = nearer == null ? f.g : Robust(nearer, f.g, SpatialFunction.Union) ?? nearer;
            if (!g.IsEmpty && g.Area > 1e-3) visible.Add(new VisibleFace(f.p, f.name, f.top, g, f.fill, f.side));
        }
        visible.Reverse(); // far → near
        return visible;
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
    static IEnumerable<LineString> Rings(Polygon p)
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
