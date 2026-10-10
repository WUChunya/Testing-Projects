using NetTopologySuite.Geometries;
using PD.Core.Analysis;
using PD.Core.Classification;
using PD.Core.Cleanup;
using PD.Core.Config;
using PD.Core.Geometry;
using PD.Core.Levels;
using PD.Core.Model;
using NtsGeometry = NetTopologySuite.Geometries.Geometry;

namespace PD.Core.Sections;

public sealed record CutBar(string MassId, string ProgramKey, double U0, double U1, double Z0, double Z1, double Story);


/// <summary>Everything cut or seen by one section, in section feet (u along the line, z elevation).</summary>
public sealed class SectionGeometry
{
    public required SectionPlane Plane { get; init; }
    public List<CutBar> Bars { get; } = new();
    public NtsGeometry Outline { get; set; } = MeshSlicer.Factory.CreatePolygon();
    /// <summary>Visual slabs (drawn at a fixed weight): every level and every roof/top edge of the cut, U0–U1 at Z.</summary>
    public List<(double U0, double U1, double Z)> SlabLines { get; } = new();
    public List<(string Id, NtsGeometry Geometry)> BuildingCuts { get; } = new();
    /// <summary>What is seen behind the cut (projected view): our uncut masses/slabs/facade and other buildings.</summary>
    public List<Views.ViewItem> View { get; } = new();
    public List<List<(double X, double Y)>> FacadeCut { get; } = new();
    public List<double> PropertyLines { get; } = new();
    public List<Level> Levels { get; init; } = new();
    public double Ground { get; init; }

    public static SectionGeometry Build(AnalysisResult r, SectionPlane plane, double depthFt, Rules rules, Style style)
    {
        var c = rules.Cleanup;
        var g = new SectionGeometry { Plane = plane, Levels = r.Levels, Ground = rules.Levels.GroundElevationFt };
        var line = plane.PlanLine();
        var spans = r.Spans.ToDictionary(s => s.MassId);

        // Program bars from the resolved plan footprints (same overlap result as plans and areas).
        for (var i = 0; i < r.Plans.Count; i++)
        foreach (var f in r.Plans[i].Footprints.Where(f => !f.OpenToBelow))
        {
            var span = spans.GetValueOrDefault(f.Mass.Id);
            var last = span is { Mode: SpanMode.KeepTall } ? span.LevelIndices[^1] : i;
            var bandBot = r.Levels[i].Elevation;
            var bandTop = Top(r, last);
            var b = f.Mass.Source.BBox;
            foreach (var (u0, u1) in Segments(f.Resolved.Intersection(line), plane))
                g.Bars.Add(new CutBar(f.Mass.Id, f.Mass.ProgramKey, u0, u1, Math.Max(bandBot, b.MinZ), Math.Min(bandTop, b.MaxZ), Top(r, i) - bandBot));
        }
        g.Outline = g.Bars.Count == 0 ? g.Outline
            : MeshSlicer.Factory.BuildGeometry(g.Bars.Select(x => Rect(x.U0, x.U1, x.Z0, x.Z1))).Union().Buffer(0.01).Buffer(-0.01);

        var (massIds, facadeIds) = Views.ProjectedView.Ids(r);
        var glass = Views.ProjectedView.GlassRegex(style.View);

        foreach (var o in r.Local.Objects.Where(o => !r.Stray.Contains(o.Id) && o.Mesh != null))
        {
            if (Views.ProjectedView.Kind(o, massIds, facadeIds, glass) is not { } kind || !plane.UOverlaps(o.Mesh!)) continue;
            var (wMin, wMax) = plane.WRange(o.Mesh!);
            var crosses = wMin < 0 && wMax > 0;
            if (crosses)
            {
                // Cut: our masses are the bars, slabs are the visual slab lines; facade and other buildings are cut here.
                if (kind is Views.ViewKind.Glass or Views.ViewKind.Facade)
                {
                    var open = new List<List<(double X, double Y)>>();
                    g.FacadeCut.AddRange(plane.Cut(o.Mesh!, c.WeldToleranceFt, c.GapCloseToleranceFt, open));
                    g.FacadeCut.AddRange(open);
                }
                else if (kind == Views.ViewKind.Context)
                {
                    // Neighbours are often open polysurfaces: close the cut along the section line (PRD 3.5, tab 2);
                    // if it still does not close, show the building as seen rather than dropping it.
                    var cut = Clip(Polys(plane.Cut(o.Mesh!, c.WeldToleranceFt, c.GapCloseToleranceFt)), plane, g.Ground);
                    if (cut.Area < 1)
                        cut = Clip(Polys(plane.Cut(o.Mesh!, c.WeldToleranceFt, c.GapCloseToleranceFt * 10, closeOpen: true)), plane, g.Ground);
                    if (cut.Area >= 1) g.BuildingCuts.Add((o.Id, cut));
                    else
                    {
                        var seen = Views.ProjectedView.Item(o, kind, plane, 0, g.Ground);
                        if (!seen.Geometry.IsEmpty) g.View.Add(seen);
                    }
                }
                continue;
            }
            if (wMin < 0 || wMin > depthFt) continue;
            // Seen beyond the cut: below grade it is hidden by the earth (only the cut basement shows).
            var item = Views.ProjectedView.Item(o, kind, plane, wMin, g.Ground);
            if (!item.Geometry.IsEmpty) g.View.Add(item);
        }

        foreach (var o in r.Local.Objects.Where(o => o.Role == "BG.Line" && o.Points is { Length: > 1 }))
        {
            var pl = MeshSlicer.Factory.CreateLineString(o.Points!.Select(p => new Coordinate(p[0], p[1])).ToArray());
            foreach (var pt in pl.Intersection(line).Coordinates) g.PropertyLines.Add(plane.U(pt.X, pt.Y));
        }

        // Slab lines: every level across the cut, plus an exposed top or bottom of a block between levels (roof, setback),
        // limited to that block so it never runs through a neighbouring program.
        if (!g.Outline.IsEmpty)
        {
            foreach (var l in r.Levels)
            {
                // A tall space (one bar crossing this level) has no floor through it.
                var open = g.Bars.Where(b => b.Z0 < l.Elevation - 0.1 && b.Z1 > l.Elevation + 0.1).Select(b => (b.U0, b.U1)).ToList();
                foreach (var range in Merge(Across(g.Outline, l.Elevation + 0.05).Concat(Across(g.Outline, l.Elevation - 0.05))))
                    foreach (var (u0, u1) in Subtract(range, open))
                        g.SlabLines.Add((u0, u1, l.Elevation));
            }
            bool OnLevel(double z) => r.Levels.Any(l => Math.Abs(l.Elevation - z) < 0.1);
            bool Inside(double u, double z) => g.Outline.Contains(MeshSlicer.Factory.CreatePoint(new Coordinate(u, z)));
            foreach (var b in g.Bars)
            {
                var mid = (b.U0 + b.U1) / 2;
                if (!OnLevel(b.Z1) && !Inside(mid, b.Z1 + 0.05)) g.SlabLines.Add((b.U0, b.U1, b.Z1));
                if (!OnLevel(b.Z0) && !Inside(mid, b.Z0 - 0.05)) g.SlabLines.Add((b.U0, b.U1, b.Z0));
            }
        }
        return g;
    }

    /// <summary>Top of a level's band: next level, or for the top level the highest mass counted on it.</summary>
    public static double Top(AnalysisResult r, int i)
    {
        if (i + 1 < r.Levels.Count) return r.Levels[i + 1].Elevation;
        var tops = r.Plans[i].Footprints.Select(f => f.Mass.Source.BBox.MaxZ).ToList();
        return tops.Count > 0 ? tops.Max() : r.Levels[i].Elevation + 12;
    }

    /// <summary>u-ranges where a horizontal line at z lies inside the outline.</summary>
    static IEnumerable<(double, double)> Across(NtsGeometry outline, double z)
    {
        var e = outline.EnvelopeInternal;
        var h = MeshSlicer.Factory.CreateLineString(new[] { new Coordinate(e.MinX - 1, z), new Coordinate(e.MaxX + 1, z) });
        var x = outline.Intersection(h);
        for (var i = 0; i < x.NumGeometries; i++)
            if (x.GetGeometryN(i) is LineString ls && ls.Length > 0.1)
                yield return (Math.Min(ls.StartPoint.X, ls.EndPoint.X), Math.Max(ls.StartPoint.X, ls.EndPoint.X));
    }

    static IEnumerable<(double, double)> Subtract((double a, double b) range, List<(double a, double b)> holes)
    {
        var start = range.a;
        foreach (var h in holes.Where(h => h.b > range.a && h.a < range.b).OrderBy(h => h.a))
        {
            if (h.a > start + 0.05) yield return (start, h.a);
            start = Math.Max(start, h.b);
        }
        if (range.b > start + 0.05) yield return (start, range.b);
    }

    static List<(double, double)> Merge(IEnumerable<(double a, double b)> ranges)
    {
        var res = new List<(double, double)>();
        foreach (var (a, b) in ranges.OrderBy(r => r.a))
        {
            if (res.Count > 0 && a <= res[^1].Item2 + 0.01) res[^1] = (res[^1].Item1, Math.Max(res[^1].Item2, b));
            else res.Add((a, b));
        }
        return res;
    }

    static IEnumerable<(double, double)> Segments(NtsGeometry g, SectionPlane p)
    {
        for (var i = 0; i < g.NumGeometries; i++)
            if (g.GetGeometryN(i) is LineString ls && ls.Length > 0.05)
            {
                double a = p.U(ls.StartPoint.X, ls.StartPoint.Y), b = p.U(ls.EndPoint.X, ls.EndPoint.Y);
                yield return (Math.Min(a, b), Math.Max(a, b));
            }
    }

    public static Polygon Rect(double u0, double u1, double z0, double z1) => MeshSlicer.Factory.CreatePolygon(new[]
    {
        new Coordinate(u0, z0), new Coordinate(u1, z0), new Coordinate(u1, z1), new Coordinate(u0, z1), new Coordinate(u0, z0),
    });

    static NtsGeometry Polys(List<List<(double X, double Y)>> loops) => MeshSlicer.ToPolygon(loops);

    /// <summary>Keeps geometry within the section line's extent (and above <paramref name="minZ"/>).</summary>
    static NtsGeometry Clip(NtsGeometry g, SectionPlane p, double minZ = -1e6) =>
        g.IsEmpty ? g : g.Intersection(Rect(0, p.Length, minZ, 1e6));

}
