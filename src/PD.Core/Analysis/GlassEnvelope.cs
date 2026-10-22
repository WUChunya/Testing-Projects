using NetTopologySuite.Geometries;
using NetTopologySuite.Operation.Union;
using PD.Core.Classification;
using PD.Core.Cleanup;
using PD.Core.Config;
using PD.Core.Geometry;
using PD.Core.Levels;
using PD.Core.Model;
using NtsGeometry = NetTopologySuite.Geometries.Geometry;

namespace PD.Core.Analysis;

/// <summary>
/// User rev 0.9.4: the glass is the building envelope. Per main storey the envelope is the area closed by the
/// standing glass (NC.Facade) and the program blocks of that storey; what no block fills inside it is indoor
/// circulation. It becomes a synthetic program solid (layer <see cref="Layer"/>) that goes through the usual level
/// assignment, so every modelled room still wins its area (smaller volume wins) and only the rest reads as circulation.
/// Slab holes of the storey's floor (atria, stair openings) stay open; areas the glass does not reach stay outside.
/// </summary>
public static class GlassEnvelope
{
    public const string Layer = "PD::INDOOR CIRCULATION";

    /// <summary>The envelope (area closed by glass and blocks) between <paramref name="z0"/> and <paramref name="z1"/>.</summary>
    public static (NtsGeometry Envelope, NtsGeometry Blocks, NtsGeometry Walls) At(IEnumerable<SourceObject> glass, IEnumerable<SourceObject> blocks,
        double z0, double z1, double gap, double cutFt = 4.0)
    {
        var segs = new List<NtsGeometry>();
        foreach (var o in glass.Where(o => o.Mesh != null && o.BBox.SizeZ >= 1 && o.BBox.MaxZ > z0 + 0.5 && o.BBox.MinZ < z1 - 0.5))
            segs.AddRange(StandingFaces(o.Mesh!, z0, z1));
        // Blocks standing at the plan cut of this storey (a deck starting on a mezzanine above it does not count).
        var parts = blocks.Where(o => o.Mesh != null && o.BBox.MaxZ > z0 + 0.5 && o.BBox.MinZ <= z0 + cutFt + 0.5)
            .Select(o => Safe(() => MeshProjection.TopDown(o.Mesh!))).Where(g => g is { IsEmpty: false }).Cast<NtsGeometry>().ToList();
        var f = MeshSlicer.Factory;
        var blocksU = parts.Count == 0 ? f.CreatePolygon() : MeshSlicer.Polygonal(Safe(() => CascadedPolygonUnion.Union(parts)) ?? f.CreatePolygon());
        if (segs.Count == 0) return (f.CreatePolygon(), blocksU, f.CreatePolygon());
        var walls = MeshSlicer.Polygonal(Safe(() => f.BuildGeometry(segs).Union().Buffer(gap / 2)) ?? f.CreatePolygon());
        var solid = MeshSlicer.Polygonal(Safe(() => blocksU.Union(walls)) ?? walls);
        var shells = new List<NtsGeometry>();
        for (var i = 0; i < solid.NumGeometries; i++)
            if (solid.GetGeometryN(i) is Polygon p) shells.Add(f.CreatePolygon(p.Shell));
        var filled = shells.Count == 0 ? f.CreatePolygon() : MeshSlicer.Polygonal(Safe(() => CascadedPolygonUnion.Union(shells)) ?? shells[0]);
        // Back to the glass line (the walls were grown by half the gap), slivers under the gap dropped on the way.
        var mitre = new NetTopologySuite.Operation.Buffer.BufferParameters { JoinStyle = NetTopologySuite.Operation.Buffer.JoinStyle.Mitre };
        var env = MeshSlicer.Polygonal(Safe(() => filled.Buffer(-gap, mitre).Buffer(gap / 2, mitre)) ?? filled);
        return (env, blocksU, walls);
    }

    /// <summary>Synthetic indoor-circulation solids, one per piece per main storey (local feet).</summary>
    public static List<SourceObject> IndoorCirculation(IReadOnlyList<SourceObject> glass, IReadOnlyList<SourceObject> blocks,
        IReadOnlyList<SourceObject> slabs, IReadOnlyList<Level> levels, CleanupRules r)
    {
        var res = new List<SourceObject>();
        if (!r.GlassEnvelope) return res;
        var main = levels.Where(l => !l.IsMezzanine).OrderBy(l => l.Elevation).ToList();
        for (var i = 0; i < main.Count; i++)
        {
            var z0 = main[i].Elevation;
            var z1 = i + 1 < main.Count ? main[i + 1].Elevation : z0 + 15;
            var (env, blocksU, walls) = At(glass, blocks, z0, z1, r.EnvelopeGapFt, r.PlanCutHeightFt);
            if (env.IsEmpty) continue;
            var free = MeshSlicer.Polygonal(Safe(() => env.Difference(blocksU.Buffer(0.05))) ?? env);
            // The floor's own openings (atrium, stair holes) stay open.
            var floor = slabs.Where(o => o.Mesh != null && Math.Abs(o.BBox.MaxZ - z0) <= 1.0).Select(o => Safe(() => MeshProjection.TopDown(o.Mesh!)))
                .Where(g => g is { IsEmpty: false }).Cast<NtsGeometry>().ToList();
            foreach (var g in floor)
                for (var k = 0; k < g.NumGeometries; k++)
                    if (g.GetGeometryN(k) is Polygon p)
                        foreach (var h in p.Holes)
                            free = MeshSlicer.Polygonal(Safe(() => free.Difference(MeshSlicer.Factory.CreatePolygon(h))) ?? free);
            var top = Math.Max(z0 + 6, z1 - 2);
            var n = 0;
            for (var k = 0; k < free.NumGeometries; k++)
            {
                if (free.GetGeometryN(k) is not Polygon piece || piece.Area < r.EnvelopeMinAreaSf) continue;
                // A piece the glass does not touch is a courtyard between blocks, not indoors (small gaps excepted).
                var touch = Safe(() => piece.Boundary.Intersection(walls.Buffer(r.EnvelopeGapFt)).Length) ?? 0;
                if (touch < 0.15 * piece.Length && piece.Area > 1000) continue;
                var mesh = Prism(piece, z0, top);
                if (mesh == null) continue;
                res.Add(new SourceObject
                {
                    Id = $"PD-INDOOR-{SanitizeName(main[i].Name)}-{++n:00}", LayerPath = Layer, GeometryType = "Mesh", Name = "",
                    Role = Roles.Program, ProgramKey = r.EnvelopeProgram, IsClosed = true, FaceCount = mesh.Faces.Length, Mesh = mesh,
                    BBox = new BBox3(piece.EnvelopeInternal.MinX, piece.EnvelopeInternal.MinY, z0, piece.EnvelopeInternal.MaxX, piece.EnvelopeInternal.MaxY, top),
                });
            }
        }
        return res;
    }

    /// <summary>Plan segments of the faces standing in [z0, z1] (polygons for faces that are not vertical).</summary>
    static IEnumerable<NtsGeometry> StandingFaces(MeshData m, double z0, double z1)
    {
        var v = m.Vertices;
        var f = MeshSlicer.Factory;
        foreach (var face in m.Faces)
        {
            double lo = double.MaxValue, hi = double.MinValue;
            foreach (var k in face) { lo = Math.Min(lo, v[3 * k + 2]); hi = Math.Max(hi, v[3 * k + 2]); }
            if (hi < z0 + 0.5 || lo > z1 - 0.5 || hi - lo < 0.5) continue;
            // the two plan points furthest apart: a vertical face projects to a segment
            Coordinate? a = null, b = null; var best = -1.0;
            foreach (var p in face)
                foreach (var q in face)
                {
                    double dx = v[3 * p] - v[3 * q], dy = v[3 * p + 1] - v[3 * q + 1], d = dx * dx + dy * dy;
                    if (d > best) { best = d; a = new Coordinate(v[3 * p], v[3 * p + 1]); b = new Coordinate(v[3 * q], v[3 * q + 1]); }
                }
            if (best > 0.09) yield return f.CreateLineString(new[] { a!, b! });
        }
    }

    /// <summary>Closed prism mesh of a polygon (holes kept) from z0 to z1.</summary>
    static MeshData? Prism(Polygon p, double z0, double z1)
    {
        var tris = Safe(() => NetTopologySuite.Triangulate.Polygon.PolygonTriangulator.Triangulate(p));
        if (tris == null || tris.IsEmpty) return null;
        var verts = new List<double>();
        var faces = new List<int[]>();
        int Add(double x, double y, double z) { verts.Add(x); verts.Add(y); verts.Add(z); return verts.Count / 3 - 1; }
        for (var i = 0; i < tris.NumGeometries; i++)
        {
            var c = tris.GetGeometryN(i).Coordinates;
            if (c.Length < 3) continue;
            faces.Add(new[] { Add(c[0].X, c[0].Y, z0), Add(c[2].X, c[2].Y, z0), Add(c[1].X, c[1].Y, z0) });
            faces.Add(new[] { Add(c[0].X, c[0].Y, z1), Add(c[1].X, c[1].Y, z1), Add(c[2].X, c[2].Y, z1) });
        }
        foreach (var ring in new[] { p.Shell }.Concat(p.Holes))
        {
            var c = ring.Coordinates;
            for (var k = 0; k + 1 < c.Length; k++)
                faces.Add(new[] { Add(c[k].X, c[k].Y, z0), Add(c[k + 1].X, c[k + 1].Y, z0), Add(c[k + 1].X, c[k + 1].Y, z1), Add(c[k].X, c[k].Y, z1) });
        }
        return new MeshData { Vertices = verts.ToArray(), Faces = faces.ToArray() };
    }

    static string SanitizeName(string s) => new(s.Select(ch => char.IsLetterOrDigit(ch) ? ch : '_').ToArray());

    static T? Safe<T>(Func<T> f) where T : class
    {
        try { return f(); } catch (Exception e) when (e is ArgumentException or InvalidOperationException or NetTopologySuite.Geometries.TopologyException) { return null; }
    }

    static double? Safe(Func<double> f)
    {
        try { return f(); } catch (Exception e) when (e is ArgumentException or InvalidOperationException or NetTopologySuite.Geometries.TopologyException) { return null; }
    }
}
