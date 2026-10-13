using NetTopologySuite.Geometries;
using NetTopologySuite.Index.Strtree;
using NetTopologySuite.Simplify;
using PD.Core.Classification;
using PD.Core.Drawing;
using PD.Core.Geometry;
using NtsGeometry = NetTopologySuite.Geometries.Geometry;

namespace PD.Core.Plans;

/// <summary>
/// Generated streetscape (user rev 0.8.3, style "site"): a sidewalk band inside every block along the road, street
/// trees 1.5 ft from the kerb, and a planted median with round ends on roads wider than 50 ft. Blocks are the site
/// areas (hardscape, landscape, our site) plus closed kerb curves on road layers; roads are what lies between them.
/// Built once per export over the site plan extent, so every drawing (site plan, plans, axons, sections) agrees.
/// </summary>
public sealed class SiteLayout
{
    /// <summary>Pseudo roles (never assigned on tab 1): paints in style "roles".</summary>
    public const string Sidewalk = "BG.Sidewalk";
    public const string Median = "BG.Median";

    public NtsGeometry Blocks { get; private init; } = MeshSlicer.Factory.CreatePolygon();
    /// <summary>Sidewalk bands, sidewalk lines (inner edge, joints), medians and trees (canopy circles), unclipped.</summary>
    public List<BackgroundItem> Items { get; } = new();
    public List<Coordinate> StreetTrees { get; } = new();
    public List<Coordinate> MedianTrees { get; } = new();
    public List<LineString> MedianLines { get; } = new();

    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<PD.Core.Analysis.AnalysisResult, Dictionary<string, SiteLayout>> Shared = new();

    public static SiteLayout For(ExportContext ctx)
    {
        if (ctx.Cache.TryGetValue("SiteLayout", out var c) && c is SiteLayout s) return s;
        // Same analysis and same streetscape settings → same layout (exports of one session, tests).
        var key = System.Text.Json.JsonSerializer.Serialize(ctx.Style.Site) + "|" + PD.Core.Entourage.EntouragePlacer.Seed(ctx) + "|"
                  + string.Join(",", ctx.Style.Plan.TreeDiameterFt ?? Array.Empty<double>()) + "|" + ctx.Settings.Output.Scale
                  + "|" + Extent(ctx); // plan style / artboard change the extent
        var byKey = Shared.GetOrCreateValue(ctx.Result);
        SiteLayout? built;
        lock (byKey)
            if (!byKey.TryGetValue(key, out built)) byKey[key] = built = Build(ctx);
        ctx.Cache["SiteLayout"] = built;
        return built;
    }

    /// <summary>Extent the streetscape is built over: the site plan's, with room for the site axon's page.</summary>
    public static Envelope Extent(ExportContext ctx)
    {
        var r = ctx.Result;
        var g = PlanDrawing.GroundIndex(ctx);
        Envelope env;
        if (r.Plans.Count > 0 && !r.Plans[g].Union.IsEmpty) env = PlanDrawing.SiteExtent(ctx, g);
        else
        {
            env = PlanBackground.Footprint(r).EnvelopeInternal.Copy();
            if (env.IsNull) return env;
        }
        env.ExpandBy(300);
        return env;
    }

    static SiteLayout Build(ExportContext ctx)
    {
        var r = ctx.Result;
        var st = ctx.Style.Site;
        var env = Extent(ctx);
        if (env.IsNull) return new SiteLayout();
        var raw = PlanBackground.Collect(r, env, ctx.Style.Plan.TreeDiameterFt, ctx.Style);
        var ours = PlanBackground.Footprint(r);

        // Blocks: big site areas, closed kerb curves, our own building.
        var parts = new List<NtsGeometry>();
        foreach (var i in raw)
        {
            if (!i.IsLine && i.Role is Roles.Hardscape or Roles.Landscape or Roles.NcHardscape or Roles.Green && i.Geometry.Area >= st.MinBlockAreaSf)
                parts.Add(i.Geometry);
            else if (i.IsLine && i.Role == Roles.Road)
                for (var k = 0; k < i.Geometry.NumGeometries; k++)
                    if (i.Geometry.GetGeometryN(k) is LineString { IsClosed: true, NumPoints: >= 4 } ls)
                    {
                        var poly = Safe(() => MeshSlicer.Factory.CreatePolygon(ls.Coordinates).Buffer(0));
                        if (poly != null && poly.Area >= st.MinBlockAreaSf) parts.Add(poly);
                    }
        }
        if (!ours.IsEmpty) parts.Add(ours);
        if (parts.Count == 0) return new SiteLayout();
        var blocks = MeshSlicer.Polygonal(Safe(() => NetTopologySuite.Operation.Union.CascadedPolygonUnion.Union(parts.Select(p => p.Buffer(0.25)).ToList())));
        if (blocks == null || blocks.IsEmpty) return new SiteLayout();
        blocks = MeshSlicer.Polygonal(Safe(() => blocks.Buffer(-0.25)) ?? blocks);
        var layout = new SiteLayout { Blocks = blocks };

        // Road surfaces (when the model has them): kerbs only where the block meets the road, not the model's edge.
        var roadPolys = raw.Where(i => !i.IsLine && i.Role == Roles.Road).Select(i => i.Geometry).ToList();
        var road = roadPolys.Count == 0 ? null
            : MeshSlicer.Polygonal(Safe(() => NetTopologySuite.Operation.Union.CascadedPolygonUnion.Union(roadPolys)));
        var roadLoc = road == null || road.IsEmpty ? null : new NetTopologySuite.Algorithm.Locate.IndexedPointInAreaLocator(road);
        var blockLoc = new NetTopologySuite.Algorithm.Locate.IndexedPointInAreaLocator(blocks);
        bool InBlock(Coordinate c) => blockLoc.Locate(c) != Location.Exterior;
        bool OnRoad(Coordinate c) => !InBlock(c) && (roadLoc == null || roadLoc.Locate(c) != Location.Exterior);

        // Buildings and water: no trees there.
        var blockers = raw.Where(i => !i.IsLine && i.Role is Roles.Building or Roles.Water or Roles.NcWater).Select(i => i.Geometry).ToList();
        if (!ours.IsEmpty) blockers.Add(ours);
        var blockerIdx = new STRtree<NtsGeometry>();
        foreach (var b in blockers) blockerIdx.Insert(b.EnvelopeInternal, b);
        bool Blocked(Coordinate c, double clear)
        {
            var e = new Envelope(c);
            e.ExpandBy(clear);
            var pt = MeshSlicer.Factory.CreatePoint(c);
            return blockerIdx.Query(e).Any(b => b.IsWithinDistance(pt, clear));
        }

        // Kerb edges: simplified block boundary segments with their outward normal (towards the road).
        var simple = DouglasPeuckerSimplifier.Simplify(blocks, 0.75);
        var edges = new List<(Coordinate A, Coordinate B, double L, double Ux, double Uy, double Nx, double Ny)>();
        var dry = new List<LineString>();
        foreach (var poly in Polygons(simple))
        foreach (var ring in new[] { poly.ExteriorRing }.Concat(poly.InteriorRings))
        {
            var pts = ring.Coordinates;
            for (var k = 0; k + 1 < pts.Length; k++)
            {
                var (a, b) = (pts[k], pts[k + 1]);
                var len = a.Distance(b);
                if (len < 1) continue;
                double ux = (b.X - a.X) / len, uy = (b.Y - a.Y) / len;
                double nx = uy, ny = -ux;
                var mid = new Coordinate((a.X + b.X) / 2, (a.Y + b.Y) / 2);
                if (InBlock(new Coordinate(mid.X + nx * 0.5, mid.Y + ny * 0.5))) { nx = -nx; ny = -ny; }
                if (!OnRoad(new Coordinate(mid.X + nx * 2, mid.Y + ny * 2)))
                {
                    dry.Add(MeshSlicer.Factory.CreateLineString(new[] { a, b }));
                    continue;
                }
                edges.Add((a, b, len, ux, uy, nx, ny));
            }
        }

        // Sidewalk: the band inside the block along those kerbs (not along the model's edge), its inner edge and the
        // paving joints across it.
        var w = st.SidewalkWidthFt;
        if (w > 0 && edges.Count > 0)
        {
            var core = MeshSlicer.Polygonal(Safe(() => blocks.Buffer(-w)));
            var band = MeshSlicer.Polygonal(Safe(() => blocks.Difference(core)));
            NtsGeometry? dryZone = null;
            if (dry.Count > 0)
            {
                dryZone = Safe(() => MeshSlicer.Factory.BuildGeometry(dry.Cast<NtsGeometry>().ToList())
                    .Buffer(w + 0.05, new NetTopologySuite.Operation.Buffer.BufferParameters { EndCapStyle = NetTopologySuite.Operation.Buffer.EndCapStyle.Flat }));
                if (dryZone != null) band = MeshSlicer.Polygonal(Safe(() => band.Difference(dryZone)) ?? band);
            }
            if (!band.IsEmpty)
            {
                layout.Items.Add(new BackgroundItem(Sidewalk, band, false, "SIDEWALK"));
                NtsGeometry inner = core.Boundary;
                if (dryZone != null) inner = Safe(() => inner.Difference(dryZone)) ?? inner;
                inner = MeshSlicer.Lineal(inner);
                if (!inner.IsEmpty) layout.Items.Add(new BackgroundItem(Sidewalk, inner, true, "SIDEWALK_EDGE"));
                if (st.SidewalkJointFt > 0)
                {
                    var bandLoc = new NetTopologySuite.Algorithm.Locate.IndexedPointInAreaLocator(band);
                    var joints = new List<LineString>();
                    foreach (var e in edges.Where(e => e.L >= st.SidewalkJointFt))
                        for (var t = st.SidewalkJointFt; t < e.L - 0.5; t += st.SidewalkJointFt)
                        {
                            var p = new Coordinate(e.A.X + e.Ux * t, e.A.Y + e.Uy * t);
                            var q = new Coordinate(p.X - e.Nx * w, p.Y - e.Ny * w);
                            // Across the band only (not at a corner where the band turns).
                            if (bandLoc.Locate(new Coordinate((p.X + q.X) / 2, (p.Y + q.Y) / 2)) == Location.Interior)
                                joints.Add(MeshSlicer.Factory.CreateLineString(new[] { p, q }));
                        }
                    if (joints.Count > 0) layout.Items.Add(new BackgroundItem(Sidewalk, MeshSlicer.Factory.CreateMultiLineString(joints.ToArray()), true, "SIDEWALK_JOINTS"));
                }
            }
        }

        var dia = ctx.Style.Plan.TreeDiameterFt is { Length: 2 } td ? td : new[] { 14.0, 22.0 };
        var rng = new Random(PD.Core.Entourage.EntouragePlacer.Mix(PD.Core.Entourage.EntouragePlacer.Seed(ctx), "STREET"));
        BackgroundItem Tree(Coordinate c, string name) =>
            new(Roles.Tree, MeshSlicer.Factory.CreatePoint(c).Buffer((dia[0] + rng.NextDouble() * (dia[1] - dia[0])) / 2, 8), false, name);

        // Street trees: 1.5 ft inside the kerb, evenly spaced, clear of corners, buildings and water.
        var stt = st.StreetTrees;
        if (stt.Enabled && stt.SpacingFt > 0)
            foreach (var e in edges.Where(e => e.L >= Math.Max(stt.MinEdgeFt, 2 * stt.CornerClearFt)))
            {
                var usable = e.L - 2 * stt.CornerClearFt;
                var count = (int)Math.Floor(usable / stt.SpacingFt) + 1;
                var t0 = stt.CornerClearFt + (usable - (count - 1) * stt.SpacingFt) / 2;
                for (var k = 0; k < count; k++)
                {
                    var t = t0 + k * stt.SpacingFt;
                    var c = new Coordinate(e.A.X + e.Ux * t - e.Nx * stt.OffsetFt, e.A.Y + e.Uy * t - e.Ny * stt.OffsetFt);
                    if (!InBlock(c) || Blocked(c, 3)) continue;
                    layout.StreetTrees.Add(c);
                    layout.Items.Add(Tree(c, $"STREET_TREE_{layout.StreetTrees.Count:0000}"));
                }
            }

        // Medians: where the facing kerb is a steady 50–160 ft away, a 5 ft green strip on the centre line, round ends.
        var md = st.Median;
        if (md.Enabled && md.WidthFt > 0)
        {
            var boundary = blocks.Boundary;
            var segIdx = new STRtree<(Coordinate P, Coordinate Q)>();
            for (var k = 0; k < boundary.NumGeometries; k++)
            {
                var ring = boundary.GetGeometryN(k).Coordinates;
                for (var j = 0; j + 1 < ring.Length; j++)
                    segIdx.Insert(new Envelope(ring[j], ring[j + 1]), (ring[j], ring[j + 1]));
            }
            // Distance along the ray (from, n) to the first kerb across the road (plain ray–segment test).
            double Hit(Coordinate from, double nx, double ny)
            {
                double ox = from.X + nx * 0.5, oy = from.Y + ny * 0.5, len = md.MaxRoadWidthFt;
                var env = new Envelope(ox, ox + nx * len, oy, oy + ny * len);
                var best = double.MaxValue;
                foreach (var (p, q) in segIdx.Query(env))
                {
                    double ex = q.X - p.X, ey = q.Y - p.Y;
                    var den = nx * ey - ny * ex;
                    if (Math.Abs(den) < 1e-12) continue;
                    double wx = p.X - ox, wy = p.Y - oy;
                    var t = (wx * ey - wy * ex) / den;
                    var u = (wx * ny - wy * nx) / den;
                    if (t >= 0 && t <= len && u >= 0 && u <= 1) best = Math.Min(best, t + 0.5);
                }
                return best;
            }
            var accepted = new List<LineString>();
            foreach (var e in edges.Where(e => e.L >= 2 * md.EndClearFt + md.WidthFt).OrderByDescending(e => e.L))
            {
                // Sample the corridor every 5 ft: keep the runs where the far kerb is at a steady road width.
                var samples = new List<(double T, double D)>();
                for (var t = 2.5; t < e.L; t += 5)
                {
                    var c = new Coordinate(e.A.X + e.Ux * t, e.A.Y + e.Uy * t);
                    samples.Add((t, Hit(c, e.Nx, e.Ny)));
                }
                var runs = new List<List<(double T, double D)>>();
                foreach (var s in samples)
                {
                    var ok = s.D > md.MinRoadWidthFt && s.D <= md.MaxRoadWidthFt;
                    if (ok && runs.Count > 0 && runs[^1].Count > 0 && Math.Abs(runs[^1][^1].D - s.D) < 3 && Math.Abs(runs[^1][^1].T - s.T) < 5.1) runs[^1].Add(s);
                    else if (ok) runs.Add(new List<(double, double)> { s });
                }
                foreach (var run in runs)
                {
                    var d = run.Average(s => s.D) / 2;
                    double t0 = run[0].T - 2.5 + md.EndClearFt + md.WidthFt / 2, t1 = run[^1].T + 2.5 - md.EndClearFt - md.WidthFt / 2;
                    if (t1 - t0 < 10) continue;
                    var line = MeshSlicer.Factory.CreateLineString(new[]
                    {
                        new Coordinate(e.A.X + e.Ux * t0 + e.Nx * d, e.A.Y + e.Uy * t0 + e.Ny * d),
                        new Coordinate(e.A.X + e.Ux * t1 + e.Nx * d, e.A.Y + e.Uy * t1 + e.Ny * d),
                    });
                    // The kerb across the road finds the same centre line: keep it once.
                    if (accepted.Any(a => a.Distance(line.Centroid) < 2 && a.Distance(line.StartPoint) < 3)) continue;
                    if (Blocked(line.Centroid.Coordinate, 0) || !OnRoad(line.Centroid.Coordinate)) continue;
                    accepted.Add(line);
                }
            }
            foreach (var (line, k) in accepted.Select((l, k) => (l, k)))
            {
                layout.MedianLines.Add(line);
                layout.Items.Add(new BackgroundItem(Median, line.Buffer(md.WidthFt / 2, 8), false, $"MEDIAN_{k + 1:000}"));
                if (md.TreeSpacingFt <= 0) continue;
                var len = line.Length;
                var count = (int)Math.Floor(len / md.TreeSpacingFt) + 1;
                var t0 = (len - (count - 1) * md.TreeSpacingFt) / 2;
                var (a, b) = (line.StartPoint.Coordinate, line.EndPoint.Coordinate);
                for (var j = 0; j < count; j++)
                {
                    var f = (t0 + j * md.TreeSpacingFt) / len;
                    var c = new Coordinate(a.X + (b.X - a.X) * f, a.Y + (b.Y - a.Y) * f);
                    layout.MedianTrees.Add(c);
                    layout.Items.Add(Tree(c, $"MEDIAN_TREE_{layout.MedianTrees.Count:0000}"));
                }
            }
        }
        return layout;
    }

    static IEnumerable<Polygon> Polygons(NtsGeometry g)
    {
        for (var i = 0; i < g.NumGeometries; i++)
            if (g.GetGeometryN(i) is Polygon p && !p.IsEmpty) yield return p;
    }

    static NtsGeometry? Safe(Func<NtsGeometry> op)
    {
        try { return op(); }
        catch (Exception e) when (e is TopologyException or ArgumentException) { return null; }
    }
}
