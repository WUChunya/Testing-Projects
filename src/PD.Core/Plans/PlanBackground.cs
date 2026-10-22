using NetTopologySuite.Geometries;
using PD.Core.Analysis;
using PD.Core.Classification;
using PD.Core.Config;
using PD.Core.Drawing;
using PD.Core.Geometry;
using NtsGeometry = NetTopologySuite.Geometries.Geometry;

namespace PD.Core.Plans;

/// <summary>A background element seen from above, clipped to the drawing extent (feet). Z = top of the object.</summary>
public sealed record BackgroundItem(string Role, NtsGeometry Geometry, bool IsLine, string Name = "", double Z = 0, double Z0 = 0,
    PD.Core.Config.SiteMaterial? Material = null, int MaterialIndex = -1);

/// <summary>
/// Site and context seen from above (PRD 5.2 / A1, docs/SITE_STYLE.md §2): context ground (blocks, roads, landscape,
/// water), our own site (NC hardscape, green space, water), trees as circles, other buildings, property lines.
/// Paints come from style "roles".
/// </summary>
public static class PlanBackground
{
    /// <summary>Drawing order bottom → top.</summary>
    static readonly string[] Order =
    {
        Roles.Road, SiteLayout.Median, Roles.Hardscape, Roles.Landscape, Roles.Water, SiteLayout.Sidewalk, Roles.NcHardscape, Roles.Green, Roles.NcWater,
        Roles.Building, Roles.Tree, Roles.NcTree, Roles.Line, Roles.PropertyLine,
    };

    /// <summary>
    /// Site items inside <paramref name="extent"/>, bottom → top: road surface first (CADMAPPER-style models carry one road
    /// slab under the blocks), then blocks / hardscape, landscape, water, our site, other buildings, trees, lines.
    /// Other buildings standing on our own footprint (the existing building the project replaces) are left out.
    /// </summary>
    public static List<BackgroundItem> Collect(AnalysisResult r, Envelope extent, double[]? treeDiameterFt = null, Style? style = null,
        GreenTreeStyle? greenTrees = null, int seed = 0, double ground = 0)
    {
        var ours = Footprint(r);
        var clip = MeshSlicer.Factory.ToGeometry(extent);
        var dia = treeDiameterFt is { Length: 2 } td ? td : new[] { 14.0, 22.0 };
        var items = new List<BackgroundItem>();
        // Plan shapes before clipping (green areas are planted whole, so every drawing gets the same trees).
        var greens = new List<(NtsGeometry Full, string Role, string Key)>();
        var seen = new HashSet<string>();
        foreach (var o in r.Local.Objects.Where(o => !r.Stray.Contains(o.Id) && Order.Contains(o.Role)))
        {
            var b = o.BBox;
            if (!extent.Intersects(new Envelope(b.MinX, b.MaxX, b.MinY, b.MaxY))) continue;
            if (o.Role is Roles.Tree or Roles.NcTree)
            {
                // Trees: a canopy circle at the object's centre, diameter from its size within the style range.
                var d = Math.Clamp(Math.Max(b.MaxX - b.MinX, b.MaxY - b.MinY), dia[0], dia[1]);
                var c = MeshSlicer.Factory.CreatePoint(new Coordinate((b.MinX + b.MaxX) / 2, (b.MinY + b.MaxY) / 2)).Buffer(d / 2, 8);
                if (extent.Intersects(c.EnvelopeInternal)) items.Add(new BackgroundItem(o.Role, c, false, o.Name, b.MaxZ, b.MinZ));
                continue;
            }
            NtsGeometry? full = null;
            if (o.Role is Roles.Line or Roles.PropertyLine || (o.Mesh == null && o.Points is { Length: >= 2 }))
            {
                if (o.Points is not { Length: >= 2 }) continue;
                var pts = o.Points.Select(p => new Coordinate(p[0], p[1])).ToArray();
                var closed = pts.Length >= 4 && (o.IsClosed || pts[0].Distance(pts[^1]) < 0.01);
                var asArea = closed && o.Role is not (Roles.Line or Roles.PropertyLine or Roles.Building)
                    && (style?.Roles.GetValueOrDefault(o.Role)?.ClosedCurves ?? "area") != "line";
                if (!asArea)
                {
                    // Property lines, kerbs, river edges, paths: open strokes (building outlines come with their solids).
                    if (o.Role == Roles.Building) continue;
                    var line = MeshSlicer.Factory.CreateLineString(pts);
                    var cut = MeshSlicer.Lineal(Safe(() => line.Intersection(clip)));
                    if (!cut.IsEmpty && seen.Add(Key(o.Role, true, line.EnvelopeInternal))) items.Add(new BackgroundItem(o.Role, cut, true, o.Name, b.MaxZ, b.MinZ));
                    continue;
                }
                if (!pts[0].Equals2D(pts[^1])) pts = pts.Append(pts[0]).ToArray();
                // Closed site curves (CADMAPPER parks, lot outlines, paths) are flat areas like surfaces.
                full = MeshSlicer.Polygonal(Safe(() => MeshSlicer.Factory.CreatePolygon(pts).Buffer(0)));
            }
            else if (o.Mesh != null) full = MeshProjection.TopDown(o.Mesh);
            if (full == null || full.IsEmpty) continue;
            // The same shape drawn twice (a curve and its extrusion) is kept once.
            if (!seen.Add(Key(o.Role, false, full.EnvelopeInternal))) continue;
            var g = MeshSlicer.Polygonal(Safe(() => full.Intersection(clip)));
            if (o.Role == Roles.Building && !g.IsEmpty && !ours.IsEmpty && OnOurSite(g, ours)) continue;
            if (!g.IsEmpty && g.Area > 0.5)
            {
                items.Add(Mat(new BackgroundItem(o.Role, g, false, o.Name, b.MaxZ, b.MinZ), style, o.LayerPath));
                if (greenTrees is { Enabled: true } && greenTrees.Roles.Contains(o.Role) && b.MaxZ <= ground + greenTrees.MaxAboveGroundFt
                    && !greenTrees.OpenNameKeywords.Any(k => o.Name.Contains(k, StringComparison.OrdinalIgnoreCase)))
                    greens.Add((full, o.Role, o.Id));
            }
        }
        if (greens.Count > 0) items.AddRange(PlantGreens(r, greens, ours, extent, dia, greenTrees!, seed));
        return Sorted(items, ground);
    }

    /// <summary>
    /// Background of a drawing with the context's style, seed and green-area planting, plus the generated streetscape
    /// (<see cref="SiteLayout"/>: sidewalks, street trees, medians) clipped to the extent. Cached per extent.
    /// </summary>
    public static List<BackgroundItem> Collect(ExportContext ctx, Envelope extent)
    {
        var key = $"BG|{extent.MinX:0.##}|{extent.MinY:0.##}|{extent.MaxX:0.##}|{extent.MaxY:0.##}";
        if (ctx.Cache.TryGetValue(key, out var hit) && hit is List<BackgroundItem> cached) return cached;
        var items = Collect(ctx.Result, extent, ctx.Style.Plan.TreeDiameterFt, ctx.Style,
            PD.Core.Entourage.EntouragePlacer.Enabled(ctx) ? ctx.Style.Entourage.GreenTrees : null,
            PD.Core.Entourage.EntouragePlacer.Seed(ctx), ctx.Rules.Levels.GroundElevationFt);
        var clip = MeshSlicer.Factory.ToGeometry(extent);
        var trees = PD.Core.Entourage.EntouragePlacer.Enabled(ctx);
        foreach (var i in SiteLayout.For(ctx).Items)
        {
            if (!extent.Intersects(i.Geometry.EnvelopeInternal)) continue;
            if (i.Role is Roles.Tree)
            {
                if (trees && extent.Contains(i.Geometry.Centroid.Coordinate)) items.Add(i);
                continue;
            }
            var g = i.IsLine ? MeshSlicer.Lineal(Safe(() => i.Geometry.Intersection(clip))) : MeshSlicer.Polygonal(Safe(() => i.Geometry.Intersection(clip)));
            if (!g.IsEmpty) items.Add(i with { Geometry = g });
        }
        items = Sorted(items, ctx.Rules.Levels.GroundElevationFt);
        ctx.Cache[key] = items;
        return items;
    }

    /// <summary>
    /// Bottom → top: areas, then trees, then lines; our site raised above grade (the stepped slope garden, terraces,
    /// roof gardens) over the ground and higher pieces over lower ones, as seen from above (rev 0.9.6); by role at grade.
    /// </summary>
    static List<BackgroundItem> Sorted(IEnumerable<BackgroundItem> items, double ground) => items
        .OrderBy(i => IsLineRole(i.Role) ? 2 : i.Role is Roles.Tree or Roles.NcTree ? 1 : 0)
        .ThenBy(i => Roles.IsNew(i.Role) && i.Z0 > ground + 1 ? i.Z0 : ground)
        .ThenBy(Rank).ToList();

    static double Rank(BackgroundItem i) => i.IsLine && !IsLineRole(i.Role) ? Array.IndexOf(Order, Roles.Building) - 0.5
        : Array.IndexOf(Order, i.Role) + (i.MaterialIndex + 1) * 0.001;

    /// <summary>The site material of an NC area by keyword in its layer path or name (style siteMaterials).</summary>
    static BackgroundItem Mat(BackgroundItem i, Style? s, string layer)
    {
        if (s == null || s.SiteMaterials.Count == 0 || i.Role is not (Roles.NcHardscape or Roles.Green or Roles.NcWater)) return i;
        var text = (layer + " " + i.Name).ToUpperInvariant();
        // The longest matching keyword wins ("PAVING INLAY" over "PAVING"); list order is the drawing order.
        var best = (k: -1, len: 0);
        for (var k = 0; k < s.SiteMaterials.Count; k++)
        {
            var m = s.SiteMaterials[k];
            if (m.Roles.Count > 0 && !m.Roles.Contains(i.Role)) continue;
            foreach (var w in m.Match)
                if (w.Length > best.len && text.Contains(w.ToUpperInvariant())) best = (k, w.Length);
        }
        return best.k < 0 ? i : i with { Material = s.SiteMaterials[best.k], MaterialIndex = best.k };
    }

    /// <summary>Paint of a background item: its site material when it has one, else its role's.</summary>
    public static Paint PaintFor(Style s, BackgroundItem i, string family) => i.Material is { } m
        ? (family == "axon" ? m.Axon ?? m.Plan : m.Plan) : PaintOf(s, i.Role, family);

    static string SwatchOf(BackgroundItem i) => i.Material is { } m ? "MAT_" + m.Key.ToUpperInvariant() : SwatchName(i.Role);

    /// <summary>Roles that are lines by nature (their own paint is the stroke).</summary>
    public static bool IsLineRole(string role) => role is Roles.Line or Roles.PropertyLine;

    /// <summary>Our property line as areas (closed curves), unioned; empty when the model has none.</summary>
    public static NtsGeometry PropertyArea(AnalysisResult r)
    {
        var polys = new List<NtsGeometry>();
        foreach (var o in r.Local.Objects.Where(o => o.Role == Roles.PropertyLine && !r.Stray.Contains(o.Id) && o.Points is { Length: >= 4 }))
        {
            var pts = o.Points!.Select(p => new Coordinate(p[0], p[1])).ToList();
            if (pts[0].Distance(pts[^1]) > 1) continue;
            pts[^1] = pts[0];
            var g = Safe(() => MeshSlicer.Factory.CreatePolygon(pts.ToArray()).Buffer(0));
            if (g is { IsEmpty: false }) polys.Add(g);
        }
        return polys.Count == 0 ? MeshSlicer.Factory.CreatePolygon()
            : MeshSlicer.Polygonal(Safe(() => NetTopologySuite.Operation.Union.CascadedPolygonUnion.Union(polys)) ?? polys[0]);
    }

    /// <summary>
    /// Where people may stand outdoors (user rev 0.8.3): hardscape (ours and the context's, sidewalks included), never
    /// on a road, a building (ours or another), water or a green.
    /// </summary>
    public static NtsGeometry Walkable(IEnumerable<BackgroundItem> items, NtsGeometry ours)
    {
        var list = items.Where(i => !i.IsLine).ToList();
        var hard = list.Where(i => i.Role is Roles.Hardscape or Roles.NcHardscape or SiteLayout.Sidewalk).Select(i => i.Geometry).ToList();
        if (hard.Count == 0) return MeshSlicer.Factory.CreatePolygon();
        var walk = Safe(() => NetTopologySuite.Operation.Union.CascadedPolygonUnion.Union(hard)) ?? MeshSlicer.Factory.CreatePolygon();
        var off = list.Where(i => i.Role is Roles.Building or Roles.Water or Roles.NcWater or SiteLayout.Median)
            .Select(i => i.Geometry).ToList();
        if (!ours.IsEmpty) off.Add(ours);
        // Greens, except where the sidewalk runs along their road edge.
        var greens = list.Where(i => i.Role is Roles.Green or Roles.Landscape).Select(i => i.Geometry).ToList();
        if (greens.Count > 0)
        {
            var g = Safe(() => NetTopologySuite.Operation.Union.CascadedPolygonUnion.Union(greens));
            var walks = list.Where(i => i.Role == SiteLayout.Sidewalk).Select(i => i.Geometry).ToList();
            if (g != null && walks.Count > 0) g = Safe(() => g.Difference(NetTopologySuite.Operation.Union.CascadedPolygonUnion.Union(walks))) ?? g;
            if (g != null) off.Add(g);
        }
        if (off.Count > 0)
        {
            var o = Safe(() => NetTopologySuite.Operation.Union.CascadedPolygonUnion.Union(off));
            if (o != null) walk = Safe(() => walk.Difference(o)) ?? walk;
        }
        return MeshSlicer.Polygonal(walk);
    }

    static string Key(string role, bool line, Envelope e) =>
        $"{role}|{line}|{Math.Round(e.MinX * 2)}|{Math.Round(e.MinY * 2)}|{Math.Round(e.MaxX * 2)}|{Math.Round(e.MaxY * 2)}";

    /// <summary>
    /// Trees at random in green areas (SITE_STYLE reference): seeded Poisson-disk trunks per green, inset from its edge,
    /// clear of buildings (ours and others) and water. Each green is planted whole with a seed from its id, so the site
    /// plan, the ground floor plan and the site axon show the same trees.
    /// </summary>
    static IEnumerable<BackgroundItem> PlantGreens(AnalysisResult r, List<(NtsGeometry Full, string Role, string Key)> greens, NtsGeometry ours,
        Envelope extent, double[] dia, GreenTreeStyle st, int seed)
    {
        var total = 0;
        var blockers = new Dictionary<string, NtsGeometry>();
        foreach (var (full, role, key) in greens.OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var env = full.EnvelopeInternal;
            var keep = full;
            foreach (var o in r.Local.Objects.Where(o => !r.Stray.Contains(o.Id) && o.Mesh != null
                         && o.Role is Roles.Building or Roles.Water or Roles.NcWater
                         && env.Intersects(new Envelope(o.BBox.MinX, o.BBox.MaxX, o.BBox.MinY, o.BBox.MaxY))))
            {
                if (!blockers.TryGetValue(o.Id, out var top)) blockers[o.Id] = top = MeshProjection.TopDown(o.Mesh!);
                keep = Safe(() => keep.Difference(top)) ?? keep;
            }
            if (!ours.IsEmpty) keep = Safe(() => keep.Difference(ours)) ?? keep;
            keep = MeshSlicer.Polygonal(Safe(() => keep.Buffer(-st.EdgeInsetFt)));
            if (keep.IsEmpty) continue;
            var s = Math.Max(4, st.SpacingFt);
            var want = Math.Min(st.MaxPerArea, (int)Math.Round(st.Coverage * keep.Area / (0.866 * s * s)));
            if (want <= 0 && keep.Area > s * s * 0.5) want = 1;
            var rng = new Random(PD.Core.Entourage.EntouragePlacer.Mix(seed, "GREEN_" + key));
            var ke = keep.EnvelopeInternal;
            var placed = new List<Coordinate>();
            var grid = new Dictionary<(long, long), List<Coordinate>>();
            (long, long) Cell(Coordinate c) => ((long)Math.Floor(c.X / s), (long)Math.Floor(c.Y / s));
            var located = new NetTopologySuite.Algorithm.Locate.IndexedPointInAreaLocator(keep);
            for (var tries = 0; placed.Count < want && tries < want * 40 + 100; tries++)
            {
                var c = new Coordinate(ke.MinX + rng.NextDouble() * ke.Width, ke.MinY + rng.NextDouble() * ke.Height);
                var (cx, cy) = Cell(c);
                var near = false;
                for (var dx = -1; dx <= 1 && !near; dx++)
                for (var dy = -1; dy <= 1 && !near; dy++)
                    if (grid.TryGetValue((cx + dx, cy + dy), out var list)) near = list.Any(q => q.Distance(c) < s);
                if (near || located.Locate(c) != Location.Interior) continue;
                placed.Add(c);
                (grid.TryGetValue((cx, cy), out var cell) ? cell : grid[(cx, cy)] = new List<Coordinate>()).Add(c);
            }
            var treeRole = Roles.IsNew(role) ? Roles.NcTree : Roles.Tree;
            foreach (var (c, n) in placed.Select((c, n) => (c, n)))
            {
                var d = dia[0] + rng.NextDouble() * (dia[1] - dia[0]);
                if (!extent.Contains(c)) continue;
                if (++total > st.MaxTotal) yield break;
                yield return new BackgroundItem(treeRole, MeshSlicer.Factory.CreatePoint(c).Buffer(d / 2, 8), false, $"GREEN_TREE_{n + 1:000}");
            }
        }
    }

    /// <summary>Hatch lines over an area (true scale), clipped to it; empty when the style has none.</summary>
    public static NtsGeometry Hatch(NtsGeometry area, HatchStyle h)
    {
        area = MeshSlicer.Polygonal(area);
        if (area.IsEmpty || h.SpacingFt <= 0) return MeshSlicer.Factory.CreateMultiLineString();
        var env = area.EnvelopeInternal;
        var c = env.Centre;
        var a = h.AngleDeg * Math.PI / 180;
        double ux = Math.Cos(a), uy = Math.Sin(a), nx = -uy, ny = ux;
        var half = Math.Sqrt(env.Width * env.Width + env.Height * env.Height) / 2 + h.SpacingFt;
        var lines = new List<LineString>();
        for (var t = -half; t <= half; t += h.SpacingFt)
        {
            double ox = c.X + nx * t, oy = c.Y + ny * t;
            lines.Add(MeshSlicer.Factory.CreateLineString(new[] { new Coordinate(ox - ux * half, oy - uy * half), new Coordinate(ox + ux * half, oy + uy * half) }));
        }
        var all = MeshSlicer.Factory.CreateMultiLineString(lines.ToArray());
        return MeshSlicer.Lineal(Safe(() => all.Intersection(area)));
    }

    /// <summary>Union of our building's footprints on every level.</summary>
    /// <summary>
    /// Plan area of our own site (NC site areas and BG.Road, not trees) standing higher than <paramref name="z"/>: seen
    /// from above it covers a block's or slab's top at that height (user rev 0.9.3, RIVER 1020: the ramp landscape and
    /// turnaround over the parking; rev 0.9.6: terraces and roof gardens on a slab, the stepped slope garden over the
    /// parking). <paramref name="slopedOnly"/> keeps ground that climbs (spans <paramref name="z"/> or rises at least
    /// cleanup.slopedSiteMinRiseFt), so a flat or raised terrace does not hide a floor plan's blocks.
    /// </summary>
    public static NtsGeometry SiteAbove(ExportContext ctx, double z, bool slopedOnly)
    {
        var key = $"SiteAbove|{z:0.00}|{slopedOnly}";
        if (ctx.Cache.TryGetValue(key, out var c) && c is NtsGeometry g0) return g0;
        var r = ctx.Result;
        var rise = ctx.Rules.Cleanup.SlopedSiteMinRiseFt;
        var parts = new List<NtsGeometry>();
        foreach (var o in r.Local.Objects.Where(o => o.Mesh != null && !r.Stray.Contains(o.Id) && Roles.IsSite(o.Role) && o.Role is not (Roles.Tree or Roles.NcTree)
                     && (Roles.IsNew(o.Role) || o.Role == Roles.Road) && o.BBox.MaxZ > z + 0.5
                     && (!slopedOnly || o.BBox.MinZ < z - 1 || o.BBox.MaxZ - o.BBox.MinZ >= rise && Climbs(o.Mesh!, rise))))
        {
            var top = Safe(() => MeshProjection.TopDown(o.Mesh!));
            if (top is { IsEmpty: false }) parts.Add(top);
        }
        var u = parts.Count == 0 ? MeshSlicer.Factory.CreatePolygon()
            : MeshSlicer.Polygonal(Safe(() => NetTopologySuite.Operation.Union.CascadedPolygonUnion.Union(parts)) ?? parts[0]);
        ctx.Cache[key] = u;
        return u;
    }

    /// <summary>
    /// Sloped or stepped ground: its tilted faces, or its flat faces at four or more heights (steps), span at least
    /// <paramref name="rise"/>. A raised planter or a terrace slab (flat top) does not climb.
    /// </summary>
    public static bool Climbs(PD.Core.Model.MeshData m, double rise)
    {
        double t0 = double.MaxValue, t1 = double.MinValue;
        var flats = new SortedSet<double>();
        var v = m.Vertices;
        foreach (var f in m.Faces)
        {
            if (f.Length < 3) continue;
            double nx = 0, ny = 0, nz = 0;
            for (var k = 0; k < f.Length; k++)
            {
                int a = f[k] * 3, b = f[(k + 1) % f.Length] * 3;
                nx += (v[a + 1] - v[b + 1]) * (v[a + 2] + v[b + 2]);
                ny += (v[a + 2] - v[b + 2]) * (v[a] + v[b]);
                nz += (v[a] - v[b]) * (v[a + 1] + v[b + 1]);
            }
            var len = Math.Sqrt(nx * nx + ny * ny + nz * nz);
            if (len < 1e-9) continue;
            var c = Math.Abs(nz) / len;
            if (c < 0.05) continue;                                   // vertical side
            if (c > 0.999) { flats.Add(Math.Round(v[f[0] * 3 + 2], 1)); continue; }
            foreach (var i in f) { t0 = Math.Min(t0, v[i * 3 + 2]); t1 = Math.Max(t1, v[i * 3 + 2]); }
        }
        return t1 - t0 >= rise || flats.Count >= 4 && flats.Max - flats.Min >= rise;
    }

    /// <summary><paramref name="g"/> (a top at height <paramref name="z"/>) less our site standing above it (see <see cref="SiteAbove"/>).</summary>
    public static NtsGeometry SiteRemoved(ExportContext ctx, NtsGeometry g, double z, bool slopedOnly)
    {
        if (g.IsEmpty) return g;
        var over = SiteAbove(ctx, z, slopedOnly);
        if (over.IsEmpty || !over.EnvelopeInternal.Intersects(g.EnvelopeInternal)) return g;
        return MeshSlicer.Polygonal(Safe(() => g.Difference(over)) ?? g);
    }

    /// <summary>Mid-height of level <paramref name="j"/>'s storey (to its next level; 6 ft above it at the top).</summary>
    public static double StoreyMid(AnalysisResult r, int j) =>
        j + 1 < r.Levels.Count ? (r.Levels[j].Elevation + r.Levels[j + 1].Elevation) / 2 : r.Levels[j].Elevation + 6;

    /// <summary><paramref name="g"/> less what our sloped ground covers at level <paramref name="j"/> (see <see cref="SiteAbove"/>).</summary>
    public static NtsGeometry UnderSiteRemoved(ExportContext ctx, NtsGeometry g, int j)
    {
        // Basements are seen only through openings in the floors above: the site does not cover them.
        if (g.IsEmpty || j < PlanDrawing.GroundIndex(ctx)) return g;
        return SiteRemoved(ctx, g, StoreyMid(ctx.Result, j), slopedOnly: true);
    }

    public static NtsGeometry Footprint(AnalysisResult r)
    {
        var parts = r.Plans.Select(p => p.Union).Where(u => !u.IsEmpty).ToList();
        return parts.Count == 0 ? MeshSlicer.Factory.CreatePolygon()
            : MeshSlicer.Polygonal(Safe(() => NetTopologySuite.Operation.Union.CascadedPolygonUnion.Union(parts)) ?? parts[0]);
    }

    /// <summary>A context building is "on our site" when a fifth or more of it lies under our building.</summary>
    static bool OnOurSite(NtsGeometry building, NtsGeometry ours)
    {
        if (!building.EnvelopeInternal.Intersects(ours.EnvelopeInternal)) return false;
        var shared = MeshSlicer.Polygonal(Safe(() => building.Intersection(ours)));
        return shared.Area >= 0.2 * building.Area;
    }

    /// <summary>AI layer: other buildings, trees, the context ground, our own site.</summary>
    public static string LayerName(BackgroundItem i) => i.Role switch
    {
        Roles.Building => "BG_CONTEXT",
        Roles.PropertyLine => "PD_PROPERTY_LINE",
        Roles.Tree or Roles.NcTree => "BG_TREE",
        _ when Roles.IsNew(i.Role) => "BG_PROJECT_SITE",
        _ => "BG_SITE",
    };

    /// <summary>Paint of a role in a drawing family ("plan" | "axon" | "view"); unknown roles get a light grey.</summary>
    public static Paint PaintOf(Style s, string role, string family)
    {
        var rp = s.Roles.GetValueOrDefault(role);
        var p = family switch { "axon" => rp?.Axon ?? rp?.Plan, "view" => rp?.View, _ => rp?.Plan };
        return p ?? new Paint { Fill = "#E5E5E5" };
    }

    /// <summary>(fill, stroke, strokePt, dash, opacity) as global swatches named after the role ("BG_ROAD", "NC_GREEN_LINE").</summary>
    public static (string? fill, string? stroke, double strokePt, double[]? dash, double opacity) Paint(PD.Core.Drawing.Drawing d, Style s, BackgroundItem i,
        string family = "plan")
    {
        var p = PaintFor(s, i, family);
        var name = SwatchOf(i);
        if (i.IsLine && !IsLineRole(i.Role))
        {
            // Open curves of an area role: the role's line paint, else a darker shade of its fill.
            var l = p.Line ?? new Paint { Stroke = p.Stroke ?? (p.Fill == null ? null : PD.Core.Axons.AxonDrawing.Darken(p.Fill, 0.25)), StrokePt = p.StrokePt ?? 0.5 };
            return (null, d.Swatch(name + "_CURVE", l.Stroke), l.StrokePt ?? s.Strokes.BackgroundPt, l.Dash, l.Opacity);
        }
        return (i.IsLine ? null : d.Swatch(name, p.Fill), d.Swatch(name + "_LINE", p.Stroke), p.StrokePt ?? s.Strokes.BackgroundPt, p.Dash, p.Opacity);
    }

    public static string HatchSwatch(BackgroundItem i) => SwatchOf(i) + "_HATCH";

    public static string SwatchName(string role) => role.Replace('.', '_').ToUpperInvariant();

    static NtsGeometry? Safe(Func<NtsGeometry> op)
    {
        try { return op(); }
        catch (Exception e) when (e is TopologyException or ArgumentException) { return null; }
    }
}
