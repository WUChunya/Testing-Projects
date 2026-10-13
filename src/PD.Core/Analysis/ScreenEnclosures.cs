using NetTopologySuite.Geometries;
using NetTopologySuite.Operation.Polygonize;
using PD.Core.Classification;
using PD.Core.Geometry;
using PD.Core.Model;
using NtsGeometry = NetTopologySuite.Geometries.Geometry;

namespace PD.Core.Analysis;

/// <summary>
/// A run of NC.Screen surfaces standing together (a roof screen around the penthouse, user rev 0.8.3): its plan
/// trace, the solid wall it becomes (as thick as the model's slabs), the floor it encloses (empty when it does not
/// close), its height range and the level it stands on. Screens never make a level of their own.
/// </summary>
public sealed record ScreenEnclosure(int Index, NtsGeometry Trace, NtsGeometry Walls, NtsGeometry Floor, double Z0, double Z1, int LevelIndex,
    IReadOnlyList<string> ObjectIds)
{
    public bool Encloses => !Floor.IsEmpty;
}

public static class ScreenEnclosures
{
    /// <summary>Screens grouped into enclosures. <paramref name="thicknessFt"/> ≤ 0 → the median slab thickness of the model.</summary>
    public static List<ScreenEnclosure> Find(AnalysisResult r, double thicknessFt, double fallbackFt = 1, double closeGapFt = 3)
    {
        var screens = r.Local.Objects.Where(o => o.Role == Roles.Screen && o.Mesh != null && !r.Stray.Contains(o.Id)).ToList();
        var result = new List<ScreenEnclosure>();
        if (screens.Count == 0) return result;
        var t = thicknessFt > 0 ? thicknessFt : SlabThickness(r) ?? fallbackFt;
        var traces = screens.Select(o => (o, trace: Trace(o.Mesh!))).Where(x => !x.trace.IsEmpty).ToList();
        if (traces.Count == 0) return result;

        // Screens within closeGapFt of each other form one run.
        var blobs = MeshSlicer.Polygonal(Safe(() => NetTopologySuite.Operation.Union.CascadedPolygonUnion.Union(
            traces.Select(x => x.trace.Buffer(closeGapFt / 2)).ToList())));
        if (blobs == null) return result;
        for (var k = 0; k < blobs.NumGeometries; k++)
        {
            var blob = blobs.GetGeometryN(k);
            var members = traces.Where(x => blob.Intersects(x.trace)).ToList();
            if (members.Count == 0) continue;
            var trace = Safe(() => NetTopologySuite.Operation.Union.UnaryUnionOp.Union(members.Select(x => x.trace).ToList()))
                        ?? MeshSlicer.Factory.BuildGeometry(members.Select(x => x.trace).ToList());
            var walls = MeshSlicer.Polygonal(Safe(() => trace.Buffer(t / 2, new NetTopologySuite.Operation.Buffer.BufferParameters
            {
                EndCapStyle = NetTopologySuite.Operation.Buffer.EndCapStyle.Square,
                JoinStyle = NetTopologySuite.Operation.Buffer.JoinStyle.Mitre,
            })));
            if (walls == null || walls.IsEmpty) continue;
            // The floor: what the screen encloses (small gaps between panels closed), walls included.
            var closed = MeshSlicer.Polygonal(Safe(() => walls.Buffer(closeGapFt / 2).Buffer(-closeGapFt / 2)));
            var holes = new List<NtsGeometry>();
            if (closed != null)
                for (var i = 0; i < closed.NumGeometries; i++)
                    if (closed.GetGeometryN(i) is Polygon p)
                        holes.AddRange(p.InteriorRings.Select(h => (NtsGeometry)MeshSlicer.Factory.CreatePolygon(h.Coordinates)));
            if (holes.Count == 0)
            {
                var poly = new Polygonizer();
                poly.Add(trace);
                holes.AddRange(poly.GetPolygons().Where(g => g.Area > 4 * t * t));
            }
            NtsGeometry floor = MeshSlicer.Factory.CreatePolygon();
            if (holes.Count > 0)
            {
                var inner = Safe(() => NetTopologySuite.Operation.Union.CascadedPolygonUnion.Union(holes));
                if (inner != null) floor = MeshSlicer.Polygonal(Safe(() => inner.Union(walls)) ?? inner);
                // Only the closed loops' walls: keep the floor to the enclosed part (no stray wall stubs outside).
                floor = MeshSlicer.Factory.BuildGeometry(Enumerable.Range(0, floor.NumGeometries).Select(floor.GetGeometryN)
                    .OfType<Polygon>().Select(p => (NtsGeometry)MeshSlicer.Factory.CreatePolygon(p.Shell)).ToList());
            }
            var z0 = members.Min(x => x.o.BBox.MinZ);
            var z1 = members.Max(x => x.o.BBox.MaxZ);
            var level = r.Levels.Select((l, i) => (l, i)).Where(x => x.l.Elevation <= z0 + 1).Select(x => x.i).DefaultIfEmpty(0).Last();
            result.Add(new ScreenEnclosure(result.Count + 1, trace, walls, floor, z0, z1, level, members.Select(x => x.o.Id).ToList()));
        }
        return result;
    }

    /// <summary>Cached per export.</summary>
    public static List<ScreenEnclosure> For(PD.Core.Drawing.ExportContext ctx)
    {
        if (ctx.Cache.TryGetValue("ScreenEnclosures", out var c) && c is List<ScreenEnclosure> s) return s;
        var st = ctx.Style.Screen;
        var found = st.Solid ? Find(ctx.Result, st.ThicknessFt, st.FallbackThicknessFt, st.CloseGapFt) : new List<ScreenEnclosure>();
        ctx.Cache["ScreenEnclosures"] = found;
        return found;
    }

    /// <summary>Median thickness of the model's slabs (NC.Slab solids), if any.</summary>
    public static double? SlabThickness(AnalysisResult r)
    {
        var t = r.Local.Objects.Where(o => o.Role == Roles.Slab && o.Mesh != null && o.BBox.SizeZ is > 0.2 and < 4).Select(o => o.BBox.SizeZ).OrderBy(x => x).ToList();
        return t.Count == 0 ? null : t[t.Count / 2];
    }

    /// <summary>Plan trace of a vertical surface: each face seen from above collapses to a segment (its longest span).</summary>
    public static NtsGeometry Trace(MeshData m)
    {
        var v = m.Vertices;
        var segs = new List<LineString>();
        foreach (var f in m.Faces)
        {
            if (f.Length < 3) continue;
            // Vertical faces only: the top and bottom of a screen modelled as a closed box would collapse to a
            // diagonal (user rev 0.8.4 "diagonal wall").
            double ax = v[3 * f[1]] - v[3 * f[0]], ay = v[3 * f[1] + 1] - v[3 * f[0] + 1], az = v[3 * f[1] + 2] - v[3 * f[0] + 2];
            double bx = v[3 * f[2]] - v[3 * f[0]], by = v[3 * f[2] + 1] - v[3 * f[0] + 1], bz = v[3 * f[2] + 2] - v[3 * f[0] + 2];
            double nx = ay * bz - az * by, ny = az * bx - ax * bz, nz = ax * by - ay * bx;
            var nl = Math.Sqrt(nx * nx + ny * ny + nz * nz);
            if (nl < 1e-12 || Math.Abs(nz) / nl > 0.5) continue;
            var pts = f.Select(i => new Coordinate(v[3 * i], v[3 * i + 1])).ToList();
            var (a, b, best) = (pts[0], pts[1], -1.0);
            for (var i = 0; i < pts.Count; i++)
            for (var j = i + 1; j < pts.Count; j++)
            {
                var dd = pts[i].Distance(pts[j]);
                if (dd > best) (a, b, best) = (pts[i], pts[j], dd);
            }
            if (best > 0.05) segs.Add(MeshSlicer.Factory.CreateLineString(new[] { a, b }));
        }
        if (segs.Count == 0) return MeshSlicer.Factory.CreateLineString();
        return Safe(() => NetTopologySuite.Operation.Union.UnaryUnionOp.Union(segs.Cast<NtsGeometry>().ToList()))
               ?? MeshSlicer.Factory.CreateMultiLineString(segs.ToArray());
    }

    static NtsGeometry? Safe(Func<NtsGeometry> op)
    {
        try { return op(); }
        catch (Exception e) when (e is TopologyException or ArgumentException) { return null; }
    }
}
