using System.Text.RegularExpressions;
using NetTopologySuite.Geometries;
using PD.Core.Analysis;
using PD.Core.Classification;
using PD.Core.Drawing;
using PD.Core.Geometry;
using PD.Core.Sections;
using NtsGeometry = NetTopologySuite.Geometries.Geometry;

namespace PD.Core.Plans;

/// <summary>
/// PRD 5.2: 01_Plans.ai — one artboard per level (title top-left, legend bottom-left, scale bar and north arrow
/// bottom-right) plus an overview artboard (all levels in a grid, bold level names). Every plan shares one extent
/// so levels line up; true scale like sections.
/// </summary>
public static class PlanDrawing
{
    public static PD.Core.Drawing.Drawing Build(ExportContext ctx)
    {
        var r = ctx.Result;
        var ps = ctx.Style.Plan;
        var pg = ctx.Style.Page;
        var k = ctx.PtPerFt;
        var d = new PD.Core.Drawing.Drawing { FileName = "01_Plans.ai" };
        ctx.AddStandardStyles(d);
        foreach (var n in new[] { "BG_EARTH", "BG_SITE", "BG_PROJECT_SITE", "BG_SITE_EDGE", "BG_CONTEXT_SHADOW", "BG_CONTEXT", "BG_TREE", "BG_VEIL", "PD_PROGRAM", "PD_STRUCTURE", "PD_FURNITURE", "PD_INTERIOR", "PD_SCREEN", "PD_MASSING", "PD_VOID", "PD_GRID", "PD_OVERHEAD", "PD_OUTLINE", "FACADE", "BG_SITE_LABELS", "PD_LABELS", "PD_LEGEND", "PD_ANNOTATION" })
            d.Layer(n);

        var levels = Enumerable.Range(0, r.Plans.Count).Where(i => r.Plans[i].Footprints.Any(x => !x.Resolved.IsEmpty)).ToList();
        if (levels.Count == 0)
        {
            d.Notes.Add("Plan: no program footprints.");
            return d;
        }
        var env = new Envelope();
        foreach (var i in levels) env.ExpandToInclude(r.Plans[i].Union.EnvelopeInternal);
        var core = new Envelope(env);
        env.ExpandBy(ps.ContextMarginFt);
        // Fixed artboard (user rev 0.8.4): the drawing fills the artboard around the building instead of spilling over.
        if (ctx.ArtboardSize != null) env = FitArtboard(ctx, core, levels.Max(i => Keys(ctx, r.Plans[i]).Count)) ?? env;
        var background = PlanBackground.Collect(ctx, env);
        double w = env.Width * k, h = env.Height * k, m = pg.MarginPt;

        // One artboard per level: basements on cut earth, the ground floor on the site, upper floors on the veiled site.
        foreach (var i in levels)
        {
            var lvl = r.Plans[i].Level;
            Sheet(d, ctx, i, env, background, $"PLAN {lvl.Name}", $"PLN_{SectionDrawing.Safe(lvl.Name)}_",
                Title(ps.LevelTitleFormat, lvl.Name), KindOf(ctx, i));
        }

        // Overview: all levels in a grid (lowest first), same scale, bold level names.
        var cols = Math.Max(1, Math.Min(ps.OverviewColumns, levels.Count));
        var gridRows = (int)Math.Ceiling(levels.Count / (double)cols);
        var nameBand = ctx.Pt(22);
        var gap = ctx.Pt(ps.OverviewGapPt);
        var ov = levels.Count == 0 ? 0 : d.Artboards.Count;
        d.Artboards.Add(new Artboard("PLANS OVERVIEW", Math.Ceiling(2 * m + cols * w + (cols - 1) * gap), Math.Ceiling(2 * m + gridRows * (h + nameBand) + (gridRows - 1) * gap)));
        foreach (var (i, n) in levels.Select((i, n) => (i, n)))
        {
            double cx = m + n % cols * (w + gap), cy = m + n / cols * (h + nameBand + gap);
            var lvl = r.Plans[i].Level;
            var p = $"PLO_{SectionDrawing.Safe(lvl.Name)}_";
            DrawLevel(d, ctx, ov, new PageMap(cx - env.MinX * k, cy + nameBand, k, env.MaxY, env.Width, flip: false), i, p, background, env, KindOf(ctx, i));
            d.Layer("PD_ANNOTATION").Items.Add(At(SectionDrawing.Text(p + "NAME", cx, cy + ctx.Pt(14), lvl.Name, "PD Heading", "left"), ov));
        }
        PlanLegendSheet.Add(d, ctx, (a, x, y) =>
        {
            North(d, ctx, a, "LGD_", x + ctx.Pt(ps.NorthArrowPt) * 0.5, y);
            Bar(d, ctx, a, "LGD_", ScaleBar(ps, 600, k), x + ctx.Pt(ps.NorthArrowPt) * 1.5, y + ctx.Pt(ps.NorthArrowPt) * 0.6);
        });
        return d;
    }

    /// <summary>How a level sits on the site: under grade (cut earth), at grade, above it (veiled site), or the site plan.</summary>
    public enum LevelKind { Basement, Ground, Upper, Site, Roof }

    /// <summary>The ground floor is the main level nearest the ground elevation; below it basements, above it upper floors.</summary>
    public static int GroundIndex(ExportContext ctx)
    {
        var plans = ctx.Result.Plans;
        var g = ctx.Rules.Levels.GroundElevationFt;
        return Enumerable.Range(0, plans.Count).Where(i => plans[i].Footprints.Count > 0 && !plans[i].Level.IsMezzanine)
            .OrderBy(i => Math.Abs(plans[i].Level.Elevation - g)).DefaultIfEmpty(0).First();
    }

    static LevelKind KindOf(ExportContext ctx, int i)
    {
        var ground = GroundIndex(ctx);
        return i == ground ? LevelKind.Ground : i < ground ? LevelKind.Basement : LevelKind.Upper;
    }

    /// <summary>
    /// 00_Site_Plan.ai (SITE_STYLE §2): the ground floor on the real site — blocks, roads, landscape, trees, other buildings
    /// with their shadow and name — cropped around the building, with people outdoors. True scale.
    /// </summary>
    public static PD.Core.Drawing.Drawing BuildSite(ExportContext ctx)
    {
        var r = ctx.Result;
        var ps = ctx.Style.Plan;
        var d = new PD.Core.Drawing.Drawing { FileName = "00_Site_Plan.ai" };
        ctx.AddStandardStyles(d);
        foreach (var n in new[] { "BG_SITE", "BG_PROJECT_SITE", "BG_SITE_EDGE", "BG_CONTEXT_SHADOW", "BG_CONTEXT", "BG_TREE", "PD_PROGRAM", "PD_STRUCTURE",
                     "PD_FURNITURE", "PD_INTERIOR", "PD_SCREEN", "PD_MASSING", "PD_VOID", "PD_OUTLINE", "FACADE", PD.Core.Entourage.EntouragePlacer.Layer, "BG_CONTEXT_LABELS", "BG_SITE_LABELS", "PD_LABELS", "PD_LEGEND", "PD_ANNOTATION" })
            d.Layer(n);
        var i = GroundIndex(ctx);
        if (r.Plans.Count == 0 || r.Plans[i].Union.IsEmpty)
        {
            d.Notes.Add("Site plan: no ground floor footprint.");
            return d;
        }
        var env = SiteExtent(ctx, i);
        if (ctx.ArtboardSize != null)
            env = FitArtboard(ctx, r.Plans[i].Union.EnvelopeInternal, Math.Max(Keys(ctx, r.Plans[i]).Count, Keys(ctx, RoofLevels(ctx).Select(j => r.Plans[j])).Count)) ?? env;
        var background = PlanBackground.Collect(ctx, env);
        Sheet(d, ctx, i, env, background, "SITE PLAN", "STP_", ps.SiteTitle, LevelKind.Site);
        // User rev 0.8.3: the same site with the whole building seen from above (each tier's roof in its programs' colour).
        if (ps.SiteRoofView) Sheet(d, ctx, i, env, background, "SITE PLAN ROOF", "STR_", ps.SiteRoofTitle, LevelKind.Roof);
        // Annotation symbols for analysis overlays (user rev 0.8.4).
        if (ctx.Style.Annotation.LegendArtboards)
        {
            var a = d.Artboards.Count;
            var bottom = AnnotationSymbols.Add(d, ctx, AnnotationSymbols.Set.Plan, "STL_", a, 54, 54);
            d.Artboards.Add(new Artboard("LEGEND & SYMBOLS", 480, Math.Ceiling(bottom + 54)));
        }
        return d;
    }

    /// <summary>One plan artboard: drawing, scale bar and north arrow top right, legend bottom left, title tag bottom right.</summary>
    static void Sheet(PD.Core.Drawing.Drawing d, ExportContext ctx, int i, Envelope env, List<BackgroundItem> background,
        string name, string p, string title, LevelKind kind)
    {
        var r = ctx.Result;
        var ps = ctx.Style.Plan;
        var pg = ctx.Style.Page;
        var k = ctx.PtPerFt;
        double w = env.Width * k, h = env.Height * k, m = pg.MarginPt;
        var titleH = ctx.Pt(pg.TitleBandPt);
        var band = ctx.Pt(ps.BandPt);
        var lvl = r.Plans[i].Level;
        var keys = kind == LevelKind.Roof ? Keys(ctx, RoofLevels(ctx).Select(j => r.Plans[j])) : Keys(ctx, r.Plans[i]);
        var rows = (int)Math.Ceiling(keys.Count / (double)Math.Max(1, ps.LegendColumns));
        var legendW = Math.Min(keys.Count, ps.LegendColumns) * Math.Max(ctx.Pt(ps.LegendColumnPt),
            keys.Select(key => LegendTextWidth(ctx, key)).DefaultIfEmpty(0).Max()
            + ctx.Pt(ps.LegendSwatchPt) + ctx.Pt(14));
        var legendH = rows * ctx.Pt(ps.LegendRowPt);
        var bar = ScaleBar(ps, w, k);
        var tools = bar * k + ctx.Pt(ps.NorthArrowPt) * 2.5;
        var tag = ctx.Style.Fonts.Title.SizePt * 1.2 + 2 * ps.TitleTagPadPt;
        var bottom = band + Math.Max(legendH, ctx.Pt(tag));
        double abW, abH, x0, y0;
        var fits = ctx.ArtboardSize is { } fs && w <= fs.W - 2 * m + 0.5 && h <= fs.H - 2 * m - titleH - bottom + 0.5;
        if (ctx.ArtboardSize is { } fixedSize && (fits || !pg.FitContentWhenLarger))
        {
            (abW, abH) = fixedSize;
            x0 = (abW - w) / 2;
            y0 = m + titleH + (abH - 2 * m - titleH - bottom - h) / 2;
            if (!fits) d.Notes.Add(ctx.LargerNote($"Plan {lvl.Name}"));
        }
        else
        {
            if (ctx.ArtboardSize != null) d.Notes.Add(ctx.LargerNote($"Plan {lvl.Name}"));
            abW = 2 * m + Math.Max(Math.Max(w, legendW + ctx.Pt(260)), tools);
            abH = 2 * m + titleH + h + bottom;
            x0 = m + (abW - 2 * m - w) / 2;
            y0 = m + titleH;
        }
        var a = d.Artboards.Count;
        d.Artboards.Add(new Artboard(name, Math.Ceiling(abW), Math.Ceiling(abH)));
        var map = new PageMap(x0 - env.MinX * k, y0, k, env.MaxY, env.Width, flip: false);
        DrawLevel(d, ctx, a, map, i, p, background, env, kind);

        var xr = abW - m;
        North(d, ctx, a, p, xr - ctx.Pt(ps.NorthArrowPt) * 0.5, m);
        Bar(d, ctx, a, p, bar, xr - ctx.Pt(ps.NorthArrowPt) * 2 - bar * k, m + ctx.Pt(ps.NorthArrowPt) * 0.6);
        var yb = abH - m - bottom + band;
        Legend(d, ctx, a, p, keys, m, yb);
        TitleTag(d, ctx, a, p, title, xr, abH - m);
    }

    /// <summary>
    /// The plan extent that fills the fixed artboard's drawing area (artboard less margins, title band, bottom band with
    /// the legend and level tag), centred on the page anchor around <paramref name="core"/>; null when the building
    /// itself does not fit.
    /// </summary>
    static Envelope? FitArtboard(ExportContext ctx, Envelope core, int keys)
    {
        if (ctx.ArtboardSize is not { } ab) return null;
        var ps = ctx.Style.Plan;
        var pg = ctx.Style.Page;
        var k = ctx.PtPerFt;
        var rows = (int)Math.Ceiling(keys / (double)Math.Max(1, ps.LegendColumns));
        var tag = ctx.Style.Fonts.Title.SizePt * 1.2 + 2 * ps.TitleTagPadPt;
        var bottom = ctx.Pt(ps.BandPt) + Math.Max(rows * ctx.Pt(ps.LegendRowPt), ctx.Pt(tag));
        double w = (ab.W - 2 * pg.MarginPt) / k, h = (ab.H - 2 * pg.MarginPt - ctx.Pt(pg.TitleBandPt) - bottom) / k;
        if (w < core.Width || h < core.Height) return null;
        // Centred on the property line (page.centreOn), moved only as far as it takes to keep the building in view.
        var anchor = ctx.PageAnchor();
        var c = anchor.IsEmpty ? core.Centre : anchor.EnvelopeInternal.Centre;
        double x0 = Math.Clamp(c.X - w / 2, core.MaxX - w, Math.Max(core.MaxX - w, core.MinX)), y0 = Math.Clamp(c.Y - h / 2, core.MaxY - h, Math.Max(core.MaxY - h, core.MinY));
        return new Envelope(x0, x0 + w, y0, y0 + h);
    }

    /// <summary>Background, program blocks, open-to-below voids, outline, facade line and labels of one level.</summary>
    /// <summary>
    /// Site plan extent (user rev 0.8): our site (ground floor + NC site objects) grown by plan.siteExtentFactor × its
    /// own size in each direction (100 × 80 ft site → ±500 ft N–S, ±400 ft E–W), never less than plan.siteMarginFt.
    /// </summary>
    public static Envelope SiteExtent(ExportContext ctx, int groundIndex)
    {
        var r = ctx.Result;
        var ps = ctx.Style.Plan;
        var site = new Envelope(r.Plans[groundIndex].Union.EnvelopeInternal);
        // Only NC site objects next to the building count: a stray or oversized one (a ground plane, a far-off
        // test object) would blow the extent up past Illustrator's canvas and the site plan would not be created.
        var near = new Envelope(site);
        near.ExpandBy(ps.SiteMarginFt);
        foreach (var o in r.Local.Objects.Where(o => !r.Stray.Contains(o.Id) && Roles.IsSite(o.Role) && Roles.IsNew(o.Role)))
        {
            var e = new Envelope(o.BBox.MinX, o.BBox.MaxX, o.BBox.MinY, o.BBox.MaxY);
            if (near.Contains(e)) site.ExpandToInclude(e);
        }
        var env = new Envelope(site);
        env.ExpandBy(Math.Max(ps.SiteMarginFt, ps.SiteExtentFactor * site.Width), Math.Max(ps.SiteMarginFt, ps.SiteExtentFactor * site.Height));
        // Illustrator refuses artboards past its canvas (runtime.jsx throws): keep the drawing inside page.maxArtboardPt.
        // With the roof view the file holds two such artboards stacked: each gets half the canvas height.
        var pg = ctx.Style.Page;
        var extra = 2 * pg.MarginPt + ctx.Pt(pg.TitleBandPt) + ctx.Pt(ps.BandPt) * 4;
        var maxW = (pg.MaxArtboardPt - extra) / ctx.PtPerFt;
        var maxH = ((ps.SiteRoofView ? (pg.MaxArtboardPt - 72) / 2 : pg.MaxArtboardPt) - extra) / ctx.PtPerFt;
        if (maxW > 0 && maxH > 0 && (env.Width > maxW || env.Height > maxH))
        {
            var c = site.Centre;
            double hw = Math.Min(env.Width, maxW) / 2, hh = Math.Min(env.Height, maxH) / 2;
            env = new Envelope(c.X - hw, c.X + hw, c.Y - hh, c.Y + hh);
        }
        return env;
    }

    /// <summary>
    /// User rev 0.8: a light 1 pt dark-grey line where two hardscape pieces meet, 1.5 pt where the site meets the road.
    /// </summary>
    static void SiteEdges(PD.Core.Drawing.Drawing d, ExportContext ctx, int a, PageMap map, string p, List<BackgroundItem> background)
    {
        var ps = ctx.Style.Plan;
        var polys = background.Where(b => !b.IsLine && !b.Geometry.IsEmpty).ToList();
        var hard = polys.Where(b => b.Role is Roles.Hardscape or Roles.NcHardscape).Select(b => b.Geometry).ToList();
        var road = polys.Where(b => b.Role == Roles.Road).Select(b => b.Geometry).ToList();
        var site = polys.Where(b => b.Role is Roles.Hardscape or Roles.NcHardscape or Roles.Landscape or Roles.Green or Roles.Water or Roles.NcWater)
            .Select(b => b.Geometry).ToList();
        void Add(string name, NtsGeometry? lines, Config.Paint paint)
        {
            if (lines == null || lines.IsEmpty || paint.StrokePt is not > 0) return;
            var item = At(map.Geom(p + name, MeshSlicer.Lineal(lines), null, d.Swatch(name, paint.Stroke ?? "#4A4A4A"), paint.StrokePt.Value, false), a);
            item.Opacity = paint.Opacity;
            item.Dash = paint.Dash;
            if (item.Paths is { Count: > 0 }) d.Layer("BG_SITE_EDGE").Items.Add(item);
        }
        if (hard.Count > 1)
            Add("HARDSCAPE_EDGE", Safe(() =>
            {
                var all = NetTopologySuite.Operation.Union.UnaryUnionOp.Union(hard.Select(g => g.Boundary).ToList());
                var outer = NetTopologySuite.Operation.Union.UnaryUnionOp.Union(hard).Boundary.Buffer(0.25);
                return all.Difference(outer);
            }), ps.HardscapeEdge);
        if (road.Count > 0 && site.Count > 0)
            Add("ROAD_EDGE", Safe(() => NetTopologySuite.Operation.Union.UnaryUnionOp.Union(road).Boundary
                .Intersection(NetTopologySuite.Operation.Union.UnaryUnionOp.Union(site).Buffer(0.5))), ps.RoadEdge);
    }

    static void DrawLevel(PD.Core.Drawing.Drawing d, ExportContext ctx, int a, PageMap map, int i, string p, List<BackgroundItem> background,
        Envelope env, LevelKind kind)
    {
        var r = ctx.Result;
        var ps = ctx.Style.Plan;
        var plan = r.Plans[i];
        var sheet = MeshSlicer.Factory.ToGeometry(env);

        // Basements are cut into the ground: grey earth, no site. Everything else sits on the real site.
        if (kind == LevelKind.Basement)
            d.Layer("BG_EARTH").Items.Add(At(map.Poly(p + "EARTH", sheet, d.Swatch("PD_EARTH", ps.BasementEarthFill), null, 0), a));
        foreach (var (b, n) in background.Select((b, n) => (b, n)).Where(_ => kind != LevelKind.Basement))
        {
            var (fill, stroke, pt, dash, opacity) = PlanBackground.Paint(d, ctx.Style, b);
            var item = map.Geom($"{p}BG_{n:000}", b.Geometry, fill, stroke, pt, !b.IsLine);
            item.Dash = dash;
            item.Opacity = opacity;
            d.Layer(PlanBackground.LayerName(b)).Items.Add(At(item, a));
            // Green and water: a hatch over the fill instead of an outline.
            var hatch = PlanBackground.PaintFor(ctx.Style, b, "plan").Hatch;
            if (hatch != null && !b.IsLine)
            {
                var lines = PlanBackground.Hatch(b.Geometry, hatch);
                if (!lines.IsEmpty)
                {
                    var h = map.Geom($"{p}BG_{n:000}_HATCH", lines, null, d.Swatch(PlanBackground.HatchSwatch(b), hatch.Stroke), hatch.StrokePt, false);
                    h.Dash = hatch.Dash;
                    h.Opacity = hatch.Opacity;
                    d.Layer(PlanBackground.LayerName(b)).Items.Add(At(h, a));
                }
            }
            // Other buildings cast a hard shadow to the lower right (a copy on the layer below).
            var paint = PlanBackground.PaintOf(ctx.Style, b.Role, "plan");
            if (paint.Shadow != null && paint.ShadowOffsetPt is { Length: 2 } off && !b.IsLine)
            {
                var shadow = map.Geom($"{p}BG_{n:000}_SHADOW", b.Geometry, d.Swatch(PlanBackground.SwatchName(b.Role) + "_SHADOW", paint.Shadow), null, 0, true);
                foreach (var ring in shadow.Paths!) foreach (var q in ring) { q[0] += ctx.Pt(off[0]); q[1] += ctx.Pt(off[1]); }
                d.Layer("BG_CONTEXT_SHADOW").Items.Add(At(shadow, a));
            }
        }

        if (kind != LevelKind.Basement) SiteEdges(d, ctx, a, map, p, background);

        // Upper floors: the same site under a white veil, so the floor reads first (Typical Floor reference).
        if (kind == LevelKind.Upper && ps.UpperVeilOpacity > 0)
        {
            var veil = At(map.Poly(p + "VEIL", sheet, d.Swatch("PD_VEIL", ps.UpperVeilFill), null, 0), a);
            veil.Opacity = ps.UpperVeilOpacity;
            d.Layer("BG_VEIL").Items.Add(veil);
        }
        if (kind is LevelKind.Site or LevelKind.Roof && ps.ContextNames) ContextNames(d, ctx, a, map, p, background);
        if (kind is LevelKind.Ground or LevelKind.Site or LevelKind.Roof && ps.SiteNameRoles.Count > 0) SiteNames(d, ctx, a, map, p, background, kind);

        if (kind == LevelKind.Roof)
        {
            RoofView(d, ctx, a, map, p);
            People(d, ctx, a, map, p, plan, background);
            return;
        }

        // Our structure (columns, walls) cut at the plan height: solid dark.
        var cutAt = plan.Level.Elevation + ctx.Rules.Cleanup.PlanCutHeightFt;
        var sPaint = PlanBackground.PaintOf(ctx.Style, Roles.Structure, "plan");
        foreach (var (o, n) in r.Local.Objects.Where(o => o.Role == Roles.Structure && o.Mesh != null && !r.Stray.Contains(o.Id) && !IsStair(ctx, o)
                     && o.BBox.MinZ <= cutAt && o.BBox.MaxZ >= cutAt).Select((o, n) => (o, n)))
        {
            var cut = MeshSlicer.SliceToPolygon(o.Mesh!, cutAt, ctx.Rules.Cleanup.WeldToleranceFt, ctx.Rules.Cleanup.GapCloseToleranceFt, closeOpen: true);
            if (cut.IsEmpty) cut = MeshProjection.TopDown(o.Mesh!);
            d.Layer("PD_STRUCTURE").Items.Add(At(map.Poly($"{p}STRUCTURE_{n:000}", cut, d.Swatch("NC_STRUCTURE", sPaint.Fill), null, 0), a));
        }
        Stairs(d, ctx, a, map, i, p);
        PlanLinework.Add(d, ctx, a, map, i, p);
        // Open slabs and flat glass on this level (roof, terrace, canopy, user rev 0.8.4): under the blocks, no label.
        foreach (var it in ExposedItems(d, ctx, map, p, i)) d.Layer("PD_PROGRAM").Items.Insert(0, At(it, a));
        // A screen standing on this floor (roof screen around the penthouse): its floor solid, its walls as poche
        // (user rev 0.8.3) — the same object as in the axons. The floor goes under the program blocks.
        var sc = ctx.Style.Screen;
        foreach (var e in PD.Core.Analysis.ScreenEnclosures.For(ctx).Where(e => e.LevelIndex == i))
        {
            if (e.Encloses)
                d.Layer("PD_PROGRAM").Items.Insert(0, At(map.Poly($"{p}SCREEN_{e.Index:00}_FLOOR", e.Floor, d.Swatch("NC_SCREEN_FLOOR_PLAN", sc.PlanFloorFill),
                    d.Gray(sc.PlanFloorStroke), sc.PlanFloorPt), a));
            d.Layer("PD_SCREEN").Items.Add(At(map.Poly($"{p}SCREEN_{e.Index:00}_WALL", e.Walls, d.Swatch("NC_SCREEN_WALL_PLAN", sc.PlanWallFill), null, 0), a));
        }

        var count = new Dictionary<string, int>();
        var sizeFt = ctx.Pt(ctx.Style.Fonts.Label.SizePt) / map.K;
        var outline = plan.Union;
        // Site plan (rev 0.9.6): our sloped ground climbing over a ground block (the stepped slope garden over the
        // parking) is seen from above, so the block and the outline give way to it.
        var shown = plan.Footprints.ToDictionary(f => f, f => kind == LevelKind.Site && !f.OpenToBelow
            ? PlanBackground.SiteRemoved(ctx, f.Resolved, f.Mass.Source.BBox.MaxZ, slopedOnly: true) : f.Resolved);
        var hidden = plan.Footprints.Where(f => shown[f] != f.Resolved).Select(f => MeshSlicer.Polygonal(Safe(() => f.Resolved.Difference(shown[f]))))
            .Where(g => !g.IsEmpty).ToList();
        var under = hidden.Count == 0 ? null : Safe(() => NetTopologySuite.Operation.Union.CascadedPolygonUnion.Union(hidden));
        if (under is { IsEmpty: false }) outline = MeshSlicer.Polygonal(Safe(() => outline.Difference(under.Buffer(0.05))) ?? outline);
        var centre = outline.IsEmpty ? null : outline.Centroid.Coordinate;
        BelowItems(d, ctx, a, map, p, i, sizeFt, centre, outline);
        // One name per program (per label text) and level: the largest block carries it, the legend covers the rest.
        var named = ps.LabelOncePerProgram
            ? plan.Footprints.Where(f => !f.Resolved.IsEmpty).GroupBy(f => LabelText(ctx, f))
                .Select(g => g.OrderByDescending(f => f.Resolved.Area).First().Mass.Id).ToHashSet()
            : plan.Footprints.Select(f => f.Mass.Id).ToHashSet();
        // A name that does not fit even at the smallest size gets a leader, once per program.
        var leaders = plan.Footprints.Where(f => !f.Resolved.IsEmpty && named.Contains(f.Mass.Id) && !Fits(ctx, map, f, sizeFt))
            .GroupBy(f => f.OpenToBelow ? "#VOID" : f.Mass.ProgramKey)
            .Select(g => g.OrderByDescending(f => f.Resolved.Area).First().Mass.Id)
            .ToHashSet();
        foreach (var f in plan.Footprints.Where(x => !x.Resolved.IsEmpty))
        {
            var row = ctx.Names.GetValueOrDefault(f.Mass.Id);
            var id = row?.StableId ?? f.Mass.Id[..Math.Min(8, f.Mass.Id.Length)];
            var c = count[id] = count.GetValueOrDefault(id) + 1;
            var name = p + id + (c > 1 ? $"_{c}" : "");
            if (f.OpenToBelow)
            {
                var v = d.Layer("PD_VOID");
                var stroke = d.Gray(ps.VoidStroke);
                var edge = At(map.Poly(name, f.Resolved, null, stroke, ps.VoidPt), a);
                edge.Dash = ps.VoidDash;
                v.Items.Add(edge);
                var e = f.Resolved.EnvelopeInternal;
                foreach (var (q, j) in new[] { (e.MinX, e.MinY, e.MaxX, e.MaxY), (e.MinX, e.MaxY, e.MaxX, e.MinY) }.Select((q, j) => (q, j)))
                {
                    var diag = MeshSlicer.Factory.CreateLineString(new[] { new Coordinate(q.Item1, q.Item2), new Coordinate(q.Item3, q.Item4) });
                    var cut = Safe(() => f.Resolved.Intersection(diag));
                    if (cut is { IsEmpty: false }) v.Items.Add(At(map.Geom($"{name}_X{j + 1}", cut, null, stroke, ps.VoidPt, false), a));
                }
                if (named.Contains(f.Mass.Id))
                    Label(d, ctx, a, map, name, f.Resolved, ps.OpenToBelowLabel, sizeFt, centre, outline, allowLeader: leaders.Contains(f.Mass.Id));
                continue;
            }
            var (fill, pStroke, pPt, pDash) = ctx.ProgramPaint(d, f.Mass.ProgramKey);
            if (shown[f].IsEmpty) continue;
            var block = At(map.Poly(name, shown[f], fill, pStroke ?? d.Gray(ps.RoomStroke), pStroke != null ? pPt : ps.RoomPt), a);
            block.Dash = pDash;
            d.Layer("PD_PROGRAM").Items.Add(block);
            if (named.Contains(f.Mass.Id))
                Label(d, ctx, a, map, name, shown[f], LabelText(ctx, f), sizeFt, centre, outline, ctx.LabelStyle(d, fill), leaders.Contains(f.Mass.Id));
        }

        if (kind == LevelKind.Site) People(d, ctx, a, map, p, plan, background);

        // Blocks sharing a name (three PROTOTYPING labs) read as one room on a grid: dashed lines where they meet.
        if (ps.GridPt > 0)
        {
            var g = 0;
            foreach (var same in plan.Footprints.Where(f => !f.OpenToBelow && !f.Resolved.IsEmpty).GroupBy(f => LabelText(ctx, f)).Where(x => x.Count() > 1))
            {
                var list = same.ToList();
                for (var u = 0; u < list.Count; u++)
                for (var v = u + 1; v < list.Count; v++)
                {
                    if (!list[u].Resolved.EnvelopeInternal.Intersects(list[v].Resolved.EnvelopeInternal)) continue;
                    var shared = MeshSlicer.Lineal(Safe(() => list[u].Resolved.Boundary.Intersection(list[v].Resolved.Buffer(0.3))));
                    if (shared.IsEmpty || shared.Length < 2) continue;
                    var line = At(map.Geom($"{p}GRID_{++g:000}", shared, null, d.Gray(ps.GridStroke), ps.GridPt, false), a);
                    line.Dash = ps.GridDash;
                    d.Layer("PD_GRID").Items.Add(line);
                }
            }
        }

        // The floor above where it overhangs this one (an open ground floor under it): its outline dashed.
        if (ps.OverheadPt > 0 && kind is LevelKind.Ground or LevelKind.Upper && i + 1 < r.Plans.Count && !outline.IsEmpty)
        {
            var over = MeshSlicer.Polygonal(Safe(() => r.Plans[i + 1].Union.Difference(outline.Buffer(1))));
            var n = 0;
            foreach (var part in Enumerable.Range(0, over.NumGeometries).Select(over.GetGeometryN).Where(x => x.Area >= ps.OverheadMinAreaSf))
            {
                var edge = At(map.Poly($"{p}OVERHEAD_{++n:00}", part, null, d.Gray(ps.OverheadStroke), ps.OverheadPt), a);
                edge.Dash = ps.OverheadDash;
                d.Layer("PD_OVERHEAD").Items.Add(edge);
            }
        }

        if (!outline.IsEmpty)
        {
            d.Layer("PD_OUTLINE").Items.Add(At(map.Poly(p + "OUTLINE", outline, null, d.Gray(ps.OutlineStroke), ps.OutlinePt), a));
            // Glass line just outside the outline where the facade reaches the cut height (A3).
            var cutZ = plan.Level.Elevation + ctx.Rules.Cleanup.PlanCutHeightFt;
            var ids = r.FacadeIds.ToHashSet();
            var oe = outline.EnvelopeInternal;
            var glazed = r.Local.Objects.Any(o => ids.Contains(o.Id) && o.BBox.MinZ <= cutZ && o.BBox.MaxZ >= cutZ
                && oe.Intersects(new Envelope(o.BBox.MinX, o.BBox.MaxX, o.BBox.MinY, o.BBox.MaxY)));
            if (glazed && ps.FacadePt > 0)
            {
                var off = Safe(() => outline.Buffer(ctx.Pt(ps.FacadeOffsetPt) / map.K));
                for (var n = 0; off != null && n < off.NumGeometries; n++)
                    if (off.GetGeometryN(n) is Polygon poly)
                        d.Layer("FACADE").Items.Add(At(map.Geom($"{p}FACADE_{n:00}", poly.ExteriorRing, null, d.Gray(ps.FacadeStroke), ps.FacadePt, true), a));
            }
        }
    }

    /// <summary>
    /// Lower floors seen from this plan's cut, as top views (user rev 0.8.5, <see cref="SeenBelow"/>): roof paint for
    /// plates and roofs, the glass hatch for flat glass, under this level's blocks. What shows through an opening in the
    /// floor itself (<see cref="SeenBelow.OpeningsAt"/>) gets the open-to-below dashes, cross and label.
    /// </summary>
    static void BelowItems(PD.Core.Drawing.Drawing d, ExportContext ctx, int a, PageMap map, string p, int i, double sizeFt, Coordinate? centre,
        NtsGeometry outline)
    {
        var ps = ctx.Style.Plan;
        var st = ctx.Style.Exposed;
        var roof = PlanBackground.PaintOf(ctx.Style, Roles.Roof, "plan");
        var seen = SeenBelow.For(ctx, i);
        var items = new List<DItem>();
        foreach (var (e, n) in seen.Select((e, n) => (e, n)))
        {
            var name = $"{p}BELOW_{SectionDrawing.Safe(ctx.Result.Levels[e.LevelIndex].Name)}_{n + 1:00}";
            if (!e.IsGlass)
            {
                items.Add(map.Poly(name, e.Area, d.Swatch("EXPOSED_ROOF_PLAN", roof.Fill ?? "#EFEFEF"), d.Gray(roof.Stroke ?? "#B4B4B4"), roof.StrokePt ?? 0.5));
                continue;
            }
            var pane = map.Poly(name, e.Area, d.Swatch("EXPOSED_GLASS", st.GlassFill), d.Gray(st.GlassHatch.Stroke), st.LinePt);
            pane.Opacity = st.GlassOpacity;
            items.Add(pane);
            var lines = PlanBackground.Hatch(e.Area, st.GlassHatch);
            if (!lines.IsEmpty) items.Add(map.Geom(name + "_HATCH", lines, null, d.Swatch("EXPOSED_GLASS_HATCH", st.GlassHatch.Stroke), st.GlassHatch.StrokePt, false));
        }
        // Inserted at the bottom one by one, nearest level first: deeper levels end up under nearer ones, fills under hatches.
        foreach (var it in items) d.Layer("PD_PROGRAM").Items.Insert(0, At(it, a));

        // The openings in this floor itself (a hole in the ground floor plate down to the basement, user rev 0.8.6).
        var all = SeenBelow.OpeningsAt(ctx, i);
        if (all.IsEmpty) return;
        var v = d.Layer("PD_VOID");
        var stroke = d.Gray(ps.VoidStroke);
        foreach (var (hole, k) in Enumerable.Range(0, all.NumGeometries).Select(k => (all.GetGeometryN(k), k)).Where(x => x.Item1 is Polygon))
        {
            var name = $"{p}OPENING_{k + 1:00}";
            var edge = At(map.Poly(name, hole, null, stroke, ps.VoidPt), a);
            edge.Dash = ps.VoidDash;
            v.Items.Add(edge);
            var env = hole.EnvelopeInternal;
            foreach (var (q, j) in new[] { (env.MinX, env.MinY, env.MaxX, env.MaxY), (env.MinX, env.MaxY, env.MaxX, env.MinY) }.Select((q, j) => (q, j)))
            {
                var diag = MeshSlicer.Factory.CreateLineString(new[] { new Coordinate(q.Item1, q.Item2), new Coordinate(q.Item3, q.Item4) });
                var cut = Safe(() => hole.Intersection(diag));
                if (cut is { IsEmpty: false }) v.Items.Add(At(map.Geom($"{name}_X{j + 1}", cut, null, stroke, ps.VoidPt, false), a));
            }
            if (k == 0) Label(d, ctx, a, map, name, hole, ps.OpenToBelowLabel, sizeFt, centre, outline, allowLeader: true);
        }
    }

    /// <summary>
    /// The open part of slabs (NC.Roof paint) and flat glass (pale blue + diagonal hatch) of level i, glass hatch
    /// after its fill.
    /// </summary>
    static IEnumerable<DItem> ExposedItems(PD.Core.Drawing.Drawing d, ExportContext ctx, PageMap map, string p, int i)
    {
        var st = ctx.Style.Exposed;
        var roof = PlanBackground.PaintOf(ctx.Style, Roles.Roof, "plan");
        var list = PD.Core.Analysis.ExposedSurfaces.For(ctx).Where(e => e.LevelIndex == i).ToList();
        var items = new List<DItem>();
        foreach (var (e0, n) in list.Select((e, n) => (e, n)))
        {
            // A terrace or roof garden on the slab (and ramp landscape over it) is what shows from above (rev 0.9.6).
            var e = e0 with { Area = PlanBackground.SiteRemoved(ctx, PlanBackground.UnderSiteRemoved(ctx, e0.Area, i), e0.Z1, slopedOnly: false) };
            if (e.Area.IsEmpty) continue;
            var name = $"{p}{(e.IsGlass ? "EXPOSED_GLASS" : "EXPOSED_ROOF")}_{n + 1:00}";
            if (!e.IsGlass)
            {
                items.Add(map.Poly(name, e.Area, d.Swatch("EXPOSED_ROOF_PLAN", roof.Fill ?? "#EFEFEF"), d.Gray(roof.Stroke ?? "#B4B4B4"), roof.StrokePt ?? 0.5));
                continue;
            }
            var pane = map.Poly(name, e.Area, d.Swatch("EXPOSED_GLASS", st.GlassFill), d.Gray(st.GlassHatch.Stroke), st.LinePt);
            pane.Opacity = st.GlassOpacity;
            items.Add(pane);
            var lines = PlanBackground.Hatch(e.Area, st.GlassHatch);
            if (!lines.IsEmpty) items.Add(map.Geom(name + "_HATCH", lines, null, d.Swatch("EXPOSED_GLASS_HATCH", st.GlassHatch.Stroke), st.GlassHatch.StrokePt, false));
        }
        // Inserted at the bottom one by one: reverse so fills stay under their hatch.
        items.Reverse();
        return items;
    }

    /// <summary>Levels from grade up that carry program, lowest first (the roof view paints them in this order).</summary>
    static List<int> RoofLevels(ExportContext ctx)
    {
        var r = ctx.Result;
        var g = GroundIndex(ctx);
        var roofs = PD.Core.Analysis.ExposedSurfaces.For(ctx).Select(e => e.LevelIndex).ToHashSet();
        return Enumerable.Range(0, r.Plans.Count).Where(j => j >= g && (roofs.Contains(j) || r.Plans[j].Footprints.Any(f => !f.OpenToBelow && !f.Resolved.IsEmpty))).ToList();
    }

    /// <summary>
    /// Roof view of the whole building (user rev 0.8.3): every level from grade up painted bottom to top, so each
    /// point shows the top of the highest block over it; thin dark lines where a tier steps, the 3 pt outline around all.
    /// </summary>
    static void RoofView(PD.Core.Drawing.Drawing d, ExportContext ctx, int a, PageMap map, string p)
    {
        var r = ctx.Result;
        var ps = ctx.Style.Plan;
        var levels = RoofLevels(ctx);
        var tiers = new List<NtsGeometry>();
        var sc = ctx.Style.Screen;
        var screens = PD.Core.Analysis.ScreenEnclosures.For(ctx);
        // Blocks under our climbing ground (the stepped slope garden over the parking) are out of the outline (rev 0.9.6).
        var buried = new List<NtsGeometry>();
        foreach (var j in levels)
        {
            var ln = SectionDrawing.Safe(r.Plans[j].Level.Name);
            var k = 0;
            foreach (var it in ExposedItems(d, ctx, map, $"{p}{ln}_", j).Reverse()) d.Layer("PD_PROGRAM").Items.Add(At(it, a));
            // A roof screen standing on this level: its floor under this level's blocks, its walls over them.
            foreach (var e in screens.Where(e => e.LevelIndex == j && e.Encloses))
                d.Layer("PD_PROGRAM").Items.Add(At(map.Poly($"{p}SCREEN_{e.Index:00}_FLOOR", e.Floor, d.Swatch("NC_SCREEN_FLOOR_PLAN", sc.PlanFloorFill),
                    d.Gray(sc.PlanFloorStroke), sc.PlanFloorPt), a));
            foreach (var f in r.Plans[j].Footprints.Where(f => !f.OpenToBelow && !f.Resolved.IsEmpty))
            {
                var (fill, _, _, _) = ctx.ProgramPaint(d, f.Mass.ProgramKey);
                // Seen from above, our ramp landscape over a block (parking under the ramp) and a garden on its roof is what shows.
                var top = PlanBackground.SiteRemoved(ctx, PlanBackground.UnderSiteRemoved(ctx, f.Resolved, j), f.Mass.Source.BBox.MaxZ, slopedOnly: false);
                if (!top.IsEmpty) d.Layer("PD_PROGRAM").Items.Add(At(map.Poly($"{p}{ln}_{++k:000}", top, fill, d.Gray(ps.RoomStroke), ps.RoomPt), a));
                var open = PlanBackground.SiteRemoved(ctx, f.Resolved, f.Mass.Source.BBox.MaxZ, slopedOnly: true);
                if (open != f.Resolved && MeshSlicer.Polygonal(Safe(() => f.Resolved.Difference(open))) is { IsEmpty: false } b) buried.Add(b);
            }
            foreach (var e in screens.Where(e => e.LevelIndex == j))
                d.Layer("PD_PROGRAM").Items.Add(At(map.Poly($"{p}SCREEN_{e.Index:00}_WALL", e.Walls, d.Swatch("NC_SCREEN_WALL_PLAN", sc.PlanWallFill), null, 0), a));
            // A tier is the level's blocks and its open slabs / glass (user rev 0.8.5: a roof slab over lower blocks
            // hides their edges, and the outline runs round it).
            var parts = PD.Core.Analysis.ExposedSurfaces.For(ctx).Where(e => e.LevelIndex == j).Select(e => e.Area).ToList();
            if (!r.Plans[j].Union.IsEmpty) parts.Add(r.Plans[j].Union);
            var tier = parts.Count == 0 ? null : Safe(() => NetTopologySuite.Operation.Union.CascadedPolygonUnion.Union(parts));
            if (tier is { IsEmpty: false }) tiers.Add(MeshSlicer.Polygonal(tier));
        }
        var under = buried.Count == 0 ? null : Safe(() => NetTopologySuite.Operation.Union.CascadedPolygonUnion.Union(buried).Buffer(0.05));
        if (under is { IsEmpty: false })
            for (var t = 0; t < tiers.Count; t++) tiers[t] = MeshSlicer.Polygonal(Safe(() => tiers[t].Difference(under)) ?? tiers[t]);
        // A tier's edge shows where nothing higher covers it.
        for (var t = 0; t < tiers.Count; t++)
        {
            var above = tiers.Skip(t + 1).ToList();
            NtsGeometry edge = tiers[t].Boundary;
            if (above.Count > 0)
            {
                var cover = Safe(() => NetTopologySuite.Operation.Union.CascadedPolygonUnion.Union(above));
                if (cover != null) edge = Safe(() => edge.Difference(cover.Buffer(-0.05))) ?? edge;
            }
            var lines = MeshSlicer.Lineal(edge);
            if (!lines.IsEmpty) d.Layer("PD_OUTLINE").Items.Add(At(map.Geom($"{p}TIER_{t + 1:00}", lines, null, d.Gray(ps.RoofTierStroke), ps.RoofTierPt, false), a));
        }
        if (tiers.Count > 0)
        {
            var all = Safe(() => NetTopologySuite.Operation.Union.CascadedPolygonUnion.Union(tiers));
            if (all is { IsEmpty: false }) d.Layer("PD_OUTLINE").Items.Add(At(map.Poly($"{p}OUTLINE", all, null, d.Gray(ps.OutlineStroke), ps.OutlinePt), a));
        }
    }

    /// <summary>Other buildings' names on their roofs (SITE_STYLE §2: Arial Bold caps, #6C6C6C, horizontal).</summary>
    static void ContextNames(PD.Core.Drawing.Drawing d, ExportContext ctx, int a, PageMap map, string p, List<BackgroundItem> background)
    {
        var n = 0;
        var size = ctx.Pt(ctx.Style.Fonts.ContextLabel.SizePt);
        foreach (var b in background.Where(b => b.Role == Roles.Building && !b.IsLine && SectionDrawing.IsDisplayName(b.Name)))
        {
            var e = b.Geometry.EnvelopeInternal;
            if (TextFit.Width(b.Name, size) > e.Width * map.K * 1.2) continue;
            var c = b.Geometry.InteriorPoint.Coordinate;
            var t = SectionDrawing.Text($"{p}CONTEXT_{++n:000}_LBL", map.X(c.X), map.Y(c.Y), b.Name.ToUpperInvariant(), "PD Context Label", "center", center: true);
            d.Layer("BG_CONTEXT_LABELS").Items.Add(At(t, a));
        }
    }

    /// <summary>Names of our own named site areas (plan.siteNameRoles), centred on the area when they fit; each name once.</summary>
    static void SiteNames(PD.Core.Drawing.Drawing d, ExportContext ctx, int a, PageMap map, string p, List<BackgroundItem> background, LevelKind kind)
    {
        // At grade only the areas of the ground storey (user rev 0.9.4: no roof garden names on the ground floor; the
        // raised plaza on the ramp, up to the next main level, still counts).
        var storeyTop = PD.Core.Sections.SectionGeometry.Top(ctx.Result, GroundIndex(ctx)) + 2;
        if (kind != LevelKind.Roof) background = background.Where(b => b.Z0 <= storeyTop).ToList();
        var ps = ctx.Style.Plan;
        var roles = ps.SiteNameRoles;
        var size = ctx.Pt(ps.SiteNamePt > 0 ? ps.SiteNamePt : ctx.Style.Fonts.ContextLabel.SizePt);
        var placed = new List<Envelope>();
        var n = 0;
        // Largest areas first; a name that would overlap one already placed is left out.
        foreach (var g in background.Where(b => !b.IsLine && roles.Contains(b.Role) && SectionDrawing.IsDisplayName(b.Name))
                     .GroupBy(b => b.Name.Trim().ToUpperInvariant())
                     .OrderByDescending(g => g.Max(x => x.Geometry.Area)))
        {
            var b = g.OrderByDescending(x => x.Geometry.Area).First();
            var e = b.Geometry.EnvelopeInternal;
            var w = TextFit.Width(g.Key, size);
            if (w > Math.Max(e.Width, e.Height) * map.K * 1.4) continue;
            var c = b.Geometry.InteriorPoint.Coordinate;
            var box = new Envelope(map.X(c.X) - w / 2 - 2, map.X(c.X) + w / 2 + 2, map.Y(c.Y) - size * 0.7, map.Y(c.Y) + size * 0.7);
            if (placed.Any(q => q.Intersects(box))) continue;
            placed.Add(box);
            var t = SectionDrawing.Text($"{p}SITE_{++n:000}_LBL", map.X(c.X), map.Y(c.Y), g.Key, "PD Context Label", "center", center: true);
            t.SizePt = size;
            d.Layer("BG_SITE_LABELS").Items.Add(At(t, a));
        }
    }

    /// <summary>PRD 4: people in the program footprints (density per program) and on our own plaza outside.</summary>
    static void People(PD.Core.Drawing.Drawing d, ExportContext ctx, int a, PageMap map, string p, PD.Core.Cleanup.LevelPlan plan,
        List<BackgroundItem> background)
    {
        if (!PD.Core.Entourage.EntouragePlacer.Enabled(ctx)) return;
        var e = ctx.Style.Entourage;
        var layer = d.Layer(PD.Core.Entourage.EntouragePlacer.Layer);
        var n = 0;
        void Draw(IEnumerable<PD.Core.Entourage.Figure> people)
        {
            foreach (var f in people)
                layer.Items.Add(At(PD.Core.Entourage.EntourageSymbols.Plan(d, e, $"{p}PERSON_{++n:000}", map.X(f.X), map.Y(f.Y), map.K, f.Variant), a));
        }
        // Outdoors only: program blocks stay solid colour.
        // Our plaza, and the sidewalks / blocks right around it (not the whole neighbourhood).
        // Only on hardscape and sidewalks (user rev 0.8.3): never on a road, a building, water or a green.
        var near = MeshSlicer.Polygonal(Safe(() => PlanBackground.Footprint(ctx.Result).Buffer(ctx.Style.Plan.ContextMarginFt)));
        var walk = PlanBackground.Walkable(background, plan.Union);
        var open = MeshSlicer.Polygonal(Safe(() => walk.Intersection(near)));
        var generated = Enumerable.Range(0, open.NumGeometries)
            .SelectMany(i => PD.Core.Entourage.EntouragePlacer.Outdoors(ctx, open.GetGeometryN(i), $"{p}PLAZA{i}")).ToList();
        // People the user placed (picnics on a lawn, a crowd on the river steps) wherever they stand outside the building.
        var drawn = new Envelope();
        foreach (var b in background) drawn.ExpandToInclude(b.Geometry.EnvelopeInternal);
        var outside = MeshSlicer.Polygonal(Safe(() => MeshSlicer.Factory.ToGeometry(drawn).Difference(plan.Union)));
        Draw(drawn.IsNull || outside.IsEmpty ? generated : PD.Core.Entourage.EntouragePlacer.WithModelled(ctx, generated, outside));
    }

    /// <summary>An NC.Structure object on a stair layer (style stairs.layerKeywords).</summary>
    public static bool IsStair(ExportContext ctx, PD.Core.Model.SourceObject o) => o.Role == Roles.Structure
        && ctx.Style.Stairs.LayerKeywords.Any(k => o.LayerPath.Contains(k, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Stairs of this storey as seen from above (user rev 0.9.5): every tread / part that lies in the storey, lower ones
    /// first, white with a thin dark line.
    /// </summary>
    static void Stairs(PD.Core.Drawing.Drawing d, ExportContext ctx, int a, PageMap map, int i, string p)
    {
        var r = ctx.Result;
        var st = ctx.Style.Stairs;
        double z0 = r.Plans[i].Level.Elevation, z1 = Sections.SectionGeometry.Top(r, i);
        var fill = d.Swatch("NC_STAIR", st.Fill);
        var line = d.Gray(st.Stroke);
        var n = 0;
        foreach (var o in r.Local.Objects.Where(o => IsStair(ctx, o) && o.Mesh != null && !r.Stray.Contains(o.Id)
                     && o.BBox.MaxZ > z0 + 0.3 && o.BBox.MinZ < z1 - 0.3).OrderBy(o => o.BBox.MaxZ))
        {
            var top = MeshProjection.TopDown(o.Mesh!);
            if (top.IsEmpty || top.Area < 0.05) continue;
            d.Layer("PD_STRUCTURE").Items.Add(At(map.Poly($"{p}STAIR_{++n:000}", top, fill, line, st.StrokePt), a));
        }
    }

    static string LabelText(ExportContext ctx, PD.Core.Cleanup.Footprint f) => f.OpenToBelow ? ctx.Style.Plan.OpenToBelowLabel
        : ctx.Names.GetValueOrDefault(f.Mass.Id)?.Label ?? ctx.ProgramLabel(f.Mass.ProgramKey);

    /// <summary>True when the block's own name fits inside it at some size down to the minimum.</summary>
    static bool Fits(ExportContext ctx, PageMap map, PD.Core.Cleanup.Footprint f, double sizeFt)
    {
        var text = LabelText(ctx, f);
        if (ctx.Style.Fonts.Label.AllCaps) text = text.ToUpperInvariant();
        return FitShrinking(ctx, map, f.Resolved, text, sizeFt).Spot != null;
    }

    /// <summary>Label size, then the small size, then 1 pt steps down to labelMinPt; size null = the paragraph style's own.</summary>
    static (LabelSpot? Spot, double? SizePt) FitShrinking(ExportContext ctx, PageMap map, NtsGeometry block, string text, double sizeFt)
    {
        var spot = PlanLabels.Fit(block, text, sizeFt);
        if (spot != null) return (spot, null);
        var ps = ctx.Style.Plan;
        if (ps.LabelUniform) return (null, null);      // user rev 0.9.4: one size for every name; a leader instead
        var full = ctx.Pt(ctx.Style.Fonts.Label.SizePt);
        var sizes = new List<double>();
        if (ps.LabelSmallPt > 0) sizes.Add(ctx.Pt(ps.LabelSmallPt));
        for (var pt = Math.Floor(ps.LabelSmallPt > 0 ? ps.LabelSmallPt : ctx.Style.Fonts.Label.SizePt) - 1; pt >= ps.LabelMinPt; pt--)
            sizes.Add(ctx.Pt(pt));
        foreach (var size in sizes.Where(x => x < full))
            if (PlanLabels.Fit(block, text, size / map.K) is { } s) return (s, size);
        return (null, null);
    }

    /// <summary>Inside the block when it fits (widest spot, long direction); otherwise a leader to a label outside the building.</summary>
    static void Label(PD.Core.Drawing.Drawing d, ExportContext ctx, int a, PageMap map, string name, NtsGeometry block, string text,
        double sizeFt, Coordinate? centre, NtsGeometry outline, string inside = "PD Label", bool allowLeader = true)
    {
        if (ctx.Style.Fonts.Label.AllCaps) text = text.ToUpperInvariant();
        var layer = d.Layer("PD_LABELS");
        // Room names: 19 pt, long names 13 pt (site plan §2), then smaller down to the minimum; a leader only after that.
        var small = ctx.Pt(ctx.Style.Plan.LabelSmallPt);
        var (spot, size) = FitShrinking(ctx, map, block, text, sizeFt);
        if (spot != null)
        {
            var t = SectionDrawing.Text(name + "_LBL", map.X(spot.X), map.Y(spot.Y), spot.Text, inside, "center", spot.AngleDeg, center: true);
            t.SizePt = size;
            layer.Items.Add(At(t, a));
            return;
        }
        if (!allowLeader) return;   // too small for its name: the legend and the one leader per program cover it
        var anchor = PlanLabels.Anchor(block);
        double dx = anchor.X - (centre?.X ?? anchor.X), dy = anchor.Y - (centre?.Y ?? anchor.Y - 1);
        var len = Math.Sqrt(dx * dx + dy * dy);
        if (len < 1e-6) (dx, dy, len) = (0, 1, 1);
        (dx, dy) = (dx / len, dy / len);
        // Leave the building, then run on by the leader length.
        var reach = outline.EnvelopeInternal.Width + outline.EnvelopeInternal.Height;
        var ray = MeshSlicer.Factory.CreateLineString(new[] { anchor, new Coordinate(anchor.X + dx * reach, anchor.Y + dy * reach) });
        var exit = Safe(() => ray.Difference(outline));
        var start = exit is { IsEmpty: false } ? exit.Coordinates.OrderBy(c => c.Distance(anchor)).First().Distance(anchor) : 0;
        var dist = start + ctx.Pt(ctx.Style.Plan.LeaderLengthPt) / map.K;
        var end = new Coordinate(anchor.X + dx * dist, anchor.Y + dy * dist);
        var ps = ctx.Style.Plan;
        // Keep the label (and its leader) on the sheet: the text runs away from the leader, so clamp both ends.
        var ab = d.Artboards[a];
        // Long names outside the plan are set at the small size, like the reference sheets.
        var leaderSize = text.Length > 20 && small > 0 && !ps.LabelUniform ? small : ctx.Pt(ctx.Style.Fonts.Label.SizePt);
        var width = TextFit.Width(text, leaderSize) * 1.05;
        var right = dx >= 0;
        var px = Math.Clamp(map.X(end.X), right ? 4 : Math.Min(width + 4, ab.W - 4), right ? Math.Max(4, ab.W - width - 4) : ab.W - 4);
        var py = Math.Clamp(map.Y(end.Y), 10, ab.H - 10);
        layer.Items.Add(At(map.Line(name + "_LDR", new[] { (anchor.X, anchor.Y), (end.X, end.Y) }, d.Gray(ps.LeaderStroke), ps.LeaderPt), a));
        layer.Items[^1].Paths![0][1] = new[] { px, py };
        var lbl = SectionDrawing.Text(name + "_LBL", px + (right ? 2 : -2), py + ctx.Pt(2.5), text, "PD Label", right ? "left" : "right");
        lbl.SizePt = leaderSize;
        layer.Items.Add(At(lbl, a));
    }

    /// <summary>Level name in a tinted box, right-aligned at (right, bottom).</summary>
    static void TitleTag(PD.Core.Drawing.Drawing d, ExportContext ctx, int a, string p, string text, double right, double bottom)
    {
        var ps = ctx.Style.Plan;
        // "LEVEL 1" as plain text the size of the largest program name (user rev 0.8); a fill only if the style sets one.
        var size = ctx.Pt(ctx.Style.Fonts.Label.SizePt);
        var pad = ctx.Pt(ps.TitleTagPadPt);
        var w = TextFit.Width(text, size) * 0.85 + 2 * pad;
        var h = size * 1.2 + 2 * pad;
        var anno = d.Layer("PD_ANNOTATION");
        if (!string.IsNullOrEmpty(ps.TitleTagFill))
            anno.Items.Add(At(new DItem
            {
                Kind = ItemKind.Path, Name = p + "TITLE_TAG", Fill = d.Swatch("PD_TITLE_TAG", ps.TitleTagFill),
                Paths = new() { new[] { new[] { right - w, bottom - h }, new[] { right, bottom - h }, new[] { right, bottom }, new[] { right - w, bottom } } },
            }, a));
        var t = SectionDrawing.Text(p + "TITLE", right - w / 2, bottom - h / 2, text, "PD Title", "center", center: true);
        t.SizePt = size;
        anno.Items.Add(At(t, a));
    }

    /// <summary>Programs drawn on this level, in colour-scheme order.</summary>
    static List<string> Keys(ExportContext ctx, PD.Core.Cleanup.LevelPlan plan) => Keys(ctx, new[] { plan });

    static List<string> Keys(ExportContext ctx, IEnumerable<PD.Core.Cleanup.LevelPlan> plans)
    {
        var used = plans.SelectMany(plan => plan.Footprints).Where(f => !f.OpenToBelow && !f.Resolved.IsEmpty).Select(f => f.Mass.ProgramKey).ToHashSet();
        var order = ctx.Programs.Select(x => x.Key).ToList();
        return used.OrderBy(k => order.IndexOf(k) is var n and >= 0 ? n : int.MaxValue).ThenBy(k => k, StringComparer.Ordinal).ToList();
    }

    /// <summary>Legend name width as printed: the note style sets capitals, so measure the capitals.</summary>
    static double LegendTextWidth(ExportContext ctx, string key)
    {
        var f = ctx.Style.Fonts.Note;
        var text = f.AllCaps ? ctx.ProgramLabel(key).ToUpperInvariant() : ctx.ProgramLabel(key);
        return TextFit.Width(text, ctx.Pt(f.SizePt));
    }

    static void Legend(PD.Core.Drawing.Drawing d, ExportContext ctx, int a, string p, List<string> keys, double x0, double y0)
    {
        var ps = ctx.Style.Plan;
        var s = ctx.Pt(ps.LegendSwatchPt);
        var cols = Math.Max(1, ps.LegendColumns);
        var layer = d.Layer("PD_LEGEND");
        // Wide enough for the longest name, so entries never run into each other.
        var colW = Math.Max(ctx.Pt(ps.LegendColumnPt),
            keys.Select(k => LegendTextWidth(ctx, k)).DefaultIfEmpty(0).Max() + s + ctx.Pt(14));
        foreach (var (key, n) in keys.Select((key, n) => (key, n)))
        {
            double x = x0 + n % cols * colW, y = y0 + n / cols * ctx.Pt(ps.LegendRowPt);
            var (fill, stroke, pt, dash) = ctx.ProgramPaint(d, key);
            layer.Items.Add(At(new DItem
            {
                Kind = ItemKind.Path, Name = $"{p}LEGEND_{key}", Fill = fill, Stroke = stroke ?? d.Gray(ps.RoomStroke), StrokePt = stroke != null ? pt : ps.RoomPt, Dash = dash,
                Paths = new() { new[] { new[] { x, y }, new[] { x + s, y }, new[] { x + s, y + s }, new[] { x, y + s } } },
            }, a));
            layer.Items.Add(At(SectionDrawing.Text($"{p}LEGEND_{key}_TXT", x + s + ctx.Pt(4), y + s * 0.8, ctx.ProgramLabel(key), "PD Note", "left"), a));
        }
    }

    /// <summary>Largest listed length whose bar stays within a third of the drawing width.</summary>
    static double ScaleBar(Config.PlanStyle ps, double drawingW, double k)
    {
        var fit = ps.ScaleBarChoicesFt.Where(x => x * k <= drawingW / 3).DefaultIfEmpty(ps.ScaleBarChoicesFt.DefaultIfEmpty(50).Min()).Max();
        return fit;
    }

    static void Bar(PD.Core.Drawing.Drawing d, ExportContext ctx, int a, string p, double lengthFt, double x0, double y0)
    {
        var ps = ctx.Style.Plan;
        var hgt = ctx.Pt(ps.ScaleBarHeightPt);
        var seg = lengthFt * ctx.PtPerFt / 4;
        var layer = d.Layer("PD_ANNOTATION");
        for (var n = 0; n < 4; n++)
        {
            double x = x0 + n * seg;
            layer.Items.Add(At(new DItem
            {
                Kind = ItemKind.Path, Name = $"{p}SCALEBAR_{n + 1}", Fill = d.Gray(n % 2 == 0 ? "#000000" : "#FFFFFF"), Stroke = d.Gray("#000000"), StrokePt = 0.5,
                Paths = new() { new[] { new[] { x, y0 }, new[] { x + seg, y0 }, new[] { x + seg, y0 + hgt }, new[] { x, y0 + hgt } } },
            }, a));
        }
        var ty = y0 + hgt + ctx.Pt(8);
        layer.Items.Add(At(SectionDrawing.Text($"{p}SCALEBAR_0", x0, ty, "0", "PD Note", "center"), a));
        layer.Items.Add(At(SectionDrawing.Text($"{p}SCALEBAR_END", x0 + 4 * seg, ty, $"{lengthFt:0}'", "PD Note", "center"), a));
        layer.Items.Add(At(SectionDrawing.Text($"{p}SCALE", x0, y0 - ctx.Pt(4), $"SCALE {ctx.ScaleText}", "PD Note", "left"), a));
    }

    /// <summary>North arrow (model +Y is north): filled triangle with an N above, centred on x.</summary>
    static void North(PD.Core.Drawing.Drawing d, ExportContext ctx, int a, string p, double x, double y0)
    {
        var s = ctx.Pt(ctx.Style.Plan.NorthArrowPt);
        var layer = d.Layer("PD_ANNOTATION");
        layer.Items.Add(At(SectionDrawing.Text(p + "NORTH_TXT", x, y0 + s * 0.35, "N", "PD Note", "center"), a));
        layer.Items.Add(At(new DItem
        {
            Kind = ItemKind.Path, Name = p + "NORTH", Fill = d.Gray("#000000"),
            Paths = new() { new[] { new[] { x, y0 + s * 0.5 }, new[] { x + s * 0.3, y0 + s * 1.4 }, new[] { x, y0 + s * 1.2 }, new[] { x - s * 0.3, y0 + s * 1.4 } } },
        }, a));
    }

    /// <summary>"Level {n}" for L-numbered levels ("L3" → "Level 3"); other names (B1, ROOF) as they are.</summary>
    public static string Title(string format, string level)
    {
        var m = Regex.Match(level, "^L(\\d+)$", RegexOptions.IgnoreCase);
        return m.Success && format.Contains("{n}") ? format.Replace("{n}", m.Groups[1].Value) : level;
    }

    static DItem At(DItem item, int artboard)
    {
        item.Artboard = artboard;
        return item;
    }

    static NtsGeometry? Safe(Func<NtsGeometry> op)
    {
        try { return op(); }
        catch (Exception e) when (e is TopologyException or ArgumentException) { return null; }
    }
}
