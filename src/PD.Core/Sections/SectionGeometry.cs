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
    /// <summary>NC.Massing (building envelope) cut by the section: drawn dashed.</summary>
    public List<List<(double X, double Y)>> MassingCut { get; } = new();
    /// <summary>The closed massing cuts with the object's name (parking under a ramp), for their backing and label.</summary>
    public List<(List<(double X, double Y)> Loop, string Name)> MassingAreas { get; } = new();
    /// <summary>Raised site parts of ours cut by the plane (section.siteCutRoles): closed loops in (u, z).</summary>
    public List<List<(double X, double Y)>> SiteCut { get; } = new();
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
            // Masses are usually drawn a little short of the slabs (from the soffit, to the ceiling): snap them to the
            // level band so every bar sits centred between its two slabs; a clearly shorter mass keeps its own height.
            var story = Top(r, i) - bandBot;
            var snap = Math.Max(2.0, 0.2 * story);
            var z0 = Math.Abs(b.MinZ - bandBot) <= snap ? bandBot : Math.Max(bandBot, b.MinZ);
            var z1 = Math.Abs(b.MaxZ - bandTop) <= snap ? bandTop : Math.Min(bandTop, b.MaxZ);
            // A thin floor zone (a 1-ft circulation slab, a roof garden) is no volume in section: no bar, no slab edge.
            if (z1 - z0 <= c.FlatMassMaxThicknessFt) continue;
            foreach (var (u0, u1) in Segments(MeshSlicer.Lineal(f.Resolved.Intersection(line)), plane))
                g.Bars.Add(new CutBar(f.Mass.Id, f.Mass.ProgramKey, u0, u1, z0, z1, story));
        }
        g.Outline = g.Bars.Count == 0 ? g.Outline
            : MeshSlicer.Factory.BuildGeometry(g.Bars.Select(x => Rect(x.U0, x.U1, x.Z0, x.Z1))).Union().Buffer(0.01).Buffer(-0.01);

        var (massIds, facadeIds) = Views.ProjectedView.Ids(r);
        var glass = Views.ProjectedView.GlassRegex(style.View);

        foreach (var o in r.Local.Objects.Where(o => o.Role == Roles.Massing && !r.Stray.Contains(o.Id) && o.Mesh != null))
        {
            var (mw0, mw1) = plane.WRange(o.Mesh!);
            if (!(mw0 < 0 && mw1 > 0) || !plane.UOverlaps(o.Mesh!)) continue;
            var open = new List<List<(double X, double Y)>>();
            foreach (var loop in plane.Cut(o.Mesh!, c.WeldToleranceFt, c.GapCloseToleranceFt, open))
            {
                g.MassingCut.Add(loop.Append(loop[0]).ToList());
                g.MassingAreas.Add((loop.Append(loop[0]).ToList(), o.Name));
            }
            g.MassingCut.AddRange(open);
        }

        var st = style.Section;
        foreach (var o in r.Local.Objects.Where(o => st.SiteCutRoles.Contains(o.Role) && !r.Stray.Contains(o.Id) && o.Mesh != null
                     && o.BBox.MaxZ >= g.Ground + st.SiteCutMinAboveGroundFt))
        {
            var (sw0, sw1) = plane.WRange(o.Mesh!);
            if (!(sw0 < 0 && sw1 > 0) || !plane.UOverlaps(o.Mesh!)) continue;
            foreach (var loop in plane.Cut(o.Mesh!, c.WeldToleranceFt, c.GapCloseToleranceFt)) g.SiteCut.Add(loop.Append(loop[0]).ToList());
        }

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

        foreach (var o in r.Local.Objects.Where(o => o.Role is "BG.Line" or PD.Core.Classification.Roles.PropertyLine && o.Points is { Length: > 1 } && !r.Stray.Contains(o.Id)))
        {
            var pl = MeshSlicer.Factory.CreateLineString(o.Points!.Select(p => new Coordinate(p[0], p[1])).ToArray());
            foreach (var pt in pl.Intersection(line).Coordinates) g.PropertyLines.Add(plane.U(pt.X, pt.Y));
        }

        // Slab lines: every level across the cut, plus an exposed top or bottom of a block between levels (roof, setback),
        // limited to that block so it never runs through a neighbouring program.
        if (!g.Outline.IsEmpty)
        {
            var slabCuts = SlabRanges(r, plane, c);
            foreach (var l in r.Levels)
            {
                // A tall space (one bar crossing this level) has no floor through it.
                var open = g.Bars.Where(b => b.Z0 < l.Elevation - 0.1 && b.Z1 > l.Elevation + 0.1).Select(b => (b.U0, b.U1)).ToList();
                var ranges = Merge(Across(g.Outline, l.Elevation + 0.05).Concat(Across(g.Outline, l.Elevation - 0.05)));
                if (ranges.Count == 0) continue;
                // One slab per level: the modelled floor plates decide where it really opens (a deliberate void);
                // without plates at this level the cut is one continuous slab.
                var plates = slabCuts.Where(x => Math.Abs(x.Z - l.Elevation) <= rules.Levels.ClusterToleranceFt).Select(x => (x.U0, x.U1)).ToList();
                var slab = plates.Count > 0
                    ? Merge(ranges.Concat(plates), gap: 0.5)
                    : new List<(double, double)> { (ranges.Min(x => x.Item1), ranges.Max(x => x.Item2)) };
                foreach (var range in slab)
                    foreach (var (u0, u1) in Subtract(range, open))
                        g.SlabLines.Add((u0, u1, l.Elevation));
            }
            bool OnLevel(double z) => r.Levels.Any(l => Math.Abs(l.Elevation - z) < 0.1);
            bool Inside(double u, double z) => g.Outline.Contains(MeshSlicer.Factory.CreatePoint(new Coordinate(u, z)));
            var exposed = new List<(double U0, double U1, double Z)>();
            foreach (var b in g.Bars)
            {
                var mid = (b.U0 + b.U1) / 2;
                if (!OnLevel(b.Z1) && !Inside(mid, b.Z1 + 0.05)) exposed.Add((b.U0, b.U1, b.Z1));
                if (!OnLevel(b.Z0) && !Inside(mid, b.Z0 - 0.05)) exposed.Add((b.U0, b.U1, b.Z0));
            }
            // Roof edges a foot or two apart (a core standing a little above the roof) are one slab, at the higher one.
            foreach (var cluster in LevelDetector.Cluster(exposed, x => x.Z, rules.Levels.ClusterToleranceFt))
            {
                var z = cluster.Max(x => x.Z);
                foreach (var (u0, u1) in Merge(cluster.Select(x => (x.U0, x.U1)), gap: 0.5)) g.SlabLines.Add((u0, u1, z));
            }
        }
        return g;
    }

    /// <summary>
    /// The drawing's one floor height for diagram sizes (slab thickness, bar clearance, text): the median story of the
    /// main levels, so every slab is drawn equally thick whatever the real slab is.
    /// </summary>
    public double TypicalStory
    {
        get
        {
            var main = Levels.Where(l => !l.IsMezzanine).Select(l => l.Elevation).OrderBy(z => z).ToList();
            var stories = main.Zip(main.Skip(1), (a, b) => b - a).Where(h => h > 1).OrderBy(h => h).ToList();
            return stories.Count == 0 ? 15 : Math.Clamp(stories[stories.Count / 2], 8, 30);
        }
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

    /// <summary>u-ranges and top elevation of the modelled floor plates (NC.Slab) that the section line crosses.</summary>
    static List<(double U0, double U1, double Z)> SlabRanges(AnalysisResult r, SectionPlane plane, CleanupRules c)
    {
        var res = new List<(double, double, double)>();
        var line = plane.PlanLine();
        foreach (var o in r.Local.Objects.Where(o => o.Role == Roles.Slab && o.Mesh != null && !r.Stray.Contains(o.Id)))
        {
            var foot = MeshProjection.TopDown(o.Mesh!);
            if (foot.IsEmpty || !foot.EnvelopeInternal.Intersects(line.EnvelopeInternal)) continue;
            NtsGeometry cut;
            try { cut = MeshSlicer.Lineal(foot.Intersection(line)); }
            catch (Exception e) when (e is TopologyException or ArgumentException) { continue; }
            foreach (var (u0, u1) in Segments(cut, plane)) res.Add((u0, u1, o.BBox.MaxZ));
        }
        return res;
    }

    static List<(double, double)> Merge(IEnumerable<(double a, double b)> ranges, double gap = 0.01)
    {
        var res = new List<(double, double)>();
        foreach (var (a, b) in ranges.OrderBy(r => r.a))
        {
            if (res.Count > 0 && a <= res[^1].Item2 + gap) res[^1] = (res[^1].Item1, Math.Max(res[^1].Item2, b));
            else res.Add((a, b));
        }
        return res;
    }

    public static IEnumerable<(double, double)> Segments(NtsGeometry g, SectionPlane p)
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
        g.IsEmpty ? g : MeshSlicer.Polygonal(g.Intersection(Rect(0, p.Length, minZ, 1e6)));

}
