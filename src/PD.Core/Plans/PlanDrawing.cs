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
        foreach (var n in new[] { "BG_EARTH", "BG_SITE", "BG_PROJECT_SITE", "BG_CONTEXT_SHADOW", "BG_CONTEXT", "BG_TREE", "BG_VEIL", "PD_PROGRAM", "PD_STRUCTURE", "PD_FURNITURE", "PD_INTERIOR", "PD_SCREEN", "PD_MASSING", "PD_VOID", "PD_OUTLINE", "FACADE", "PD_LABELS", "PD_LEGEND", "PD_ANNOTATION" })
            d.Layer(n);

        var levels = Enumerable.Range(0, r.Plans.Count).Where(i => r.Plans[i].Footprints.Any(x => !x.Resolved.IsEmpty)).ToList();
        if (levels.Count == 0)
        {
            d.Notes.Add("Plan: no program footprints.");
            return d;
        }
        var env = new Envelope();
        foreach (var i in levels) env.ExpandToInclude(r.Plans[i].Union.EnvelopeInternal);
        env.ExpandBy(ps.ContextMarginFt);
        var background = PlanBackground.Collect(r, env, ps.TreeDiameterFt);
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
        return d;
    }

    /// <summary>How a level sits on the site: under grade (cut earth), at grade, above it (veiled site), or the site plan.</summary>
    public enum LevelKind { Basement, Ground, Upper, Site }

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
        foreach (var n in new[] { "BG_SITE", "BG_PROJECT_SITE", "BG_CONTEXT_SHADOW", "BG_CONTEXT", "BG_TREE", "PD_PROGRAM", "PD_STRUCTURE",
                     "PD_FURNITURE", "PD_INTERIOR", "PD_SCREEN", "PD_MASSING", "PD_VOID", "PD_OUTLINE", "FACADE", PD.Core.Entourage.EntouragePlacer.Layer, "BG_CONTEXT_LABELS", "PD_LABELS", "PD_LEGEND", "PD_ANNOTATION" })
            d.Layer(n);
        var i = GroundIndex(ctx);
        if (r.Plans.Count == 0 || r.Plans[i].Union.IsEmpty)
        {
            d.Notes.Add("Site plan: no ground floor footprint.");
            return d;
        }
        var env = new Envelope(r.Plans[i].Union.EnvelopeInternal);
        env.ExpandBy(ps.SiteMarginFt);
        Sheet(d, ctx, i, env, PlanBackground.Collect(r, env, ps.TreeDiameterFt), "SITE PLAN", "STP_", ps.SiteTitle, LevelKind.Site);
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
        var keys = Keys(ctx, r.Plans[i]);
        var rows = (int)Math.Ceiling(keys.Count / (double)Math.Max(1, ps.LegendColumns));
        var legendW = Math.Min(keys.Count, ps.LegendColumns) * Math.Max(ctx.Pt(ps.LegendColumnPt),
            keys.Select(key => TextFit.Width(ctx.ProgramLabel(key), ctx.Pt(ctx.Style.Fonts.Note.SizePt)) * 0.85).DefaultIfEmpty(0).Max()
            + ctx.Pt(ps.LegendSwatchPt) + ctx.Pt(14));
        var legendH = rows * ctx.Pt(ps.LegendRowPt);
        var bar = ScaleBar(ps, w, k);
        var tools = bar * k + ctx.Pt(ps.NorthArrowPt) * 2.5;
        var tag = ctx.Style.Fonts.Title.SizePt * 1.2 + 2 * ps.TitleTagPadPt;
        var bottom = band + Math.Max(legendH, ctx.Pt(tag));
        double abW, abH, x0, y0;
        if (ctx.ArtboardSize is { } fixedSize)
        {
            (abW, abH) = fixedSize;
            x0 = (abW - w) / 2;
            y0 = m + titleH + (abH - 2 * m - titleH - bottom - h) / 2;
            if (x0 < m - 0.5 || y0 < m + titleH - 0.5)
                d.Notes.Add($"Plan {lvl.Name} at {ctx.ScaleText} is larger than {ctx.Settings.Output.Artboard}; use \"Fit content\" or a smaller scale.");
        }
        else
        {
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

    /// <summary>Background, program blocks, open-to-below voids, outline, facade line and labels of one level.</summary>
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
            // Other buildings cast a hard shadow to the lower right (a copy on the layer below).
            var paint = PlanBackground.PaintOf(ctx.Style, b.Role, "plan");
            if (paint.Shadow != null && paint.ShadowOffsetPt is { Length: 2 } off && !b.IsLine)
            {
                var shadow = map.Geom($"{p}BG_{n:000}_SHADOW", b.Geometry, d.Swatch(PlanBackground.SwatchName(b.Role) + "_SHADOW", paint.Shadow), null, 0, true);
                foreach (var ring in shadow.Paths!) foreach (var q in ring) { q[0] += ctx.Pt(off[0]); q[1] += ctx.Pt(off[1]); }
                d.Layer("BG_CONTEXT_SHADOW").Items.Add(At(shadow, a));
            }
        }

        // Upper floors: the same site under a white veil, so the floor reads first (Typical Floor reference).
        if (kind == LevelKind.Upper && ps.UpperVeilOpacity > 0)
        {
            var veil = At(map.Poly(p + "VEIL", sheet, d.Swatch("PD_VEIL", ps.UpperVeilFill), null, 0), a);
            veil.Opacity = ps.UpperVeilOpacity;
            d.Layer("BG_VEIL").Items.Add(veil);
        }
        if (kind == LevelKind.Site && ps.ContextNames) ContextNames(d, ctx, a, map, p, background);

        // Our structure (columns, walls) cut at the plan height: solid dark.
        var cutAt = plan.Level.Elevation + ctx.Rules.Cleanup.PlanCutHeightFt;
        var sPaint = PlanBackground.PaintOf(ctx.Style, Roles.Structure, "plan");
        foreach (var (o, n) in r.Local.Objects.Where(o => o.Role == Roles.Structure && o.Mesh != null && !r.Stray.Contains(o.Id)
                     && o.BBox.MinZ <= cutAt && o.BBox.MaxZ >= cutAt).Select((o, n) => (o, n)))
        {
            var cut = MeshSlicer.SliceToPolygon(o.Mesh!, cutAt, ctx.Rules.Cleanup.WeldToleranceFt, ctx.Rules.Cleanup.GapCloseToleranceFt, closeOpen: true);
            if (cut.IsEmpty) cut = MeshProjection.TopDown(o.Mesh!);
            d.Layer("PD_STRUCTURE").Items.Add(At(map.Poly($"{p}STRUCTURE_{n:000}", cut, d.Swatch("NC_STRUCTURE", sPaint.Fill), null, 0), a));
        }
        PlanLinework.Add(d, ctx, a, map, i, p);

        var count = new Dictionary<string, int>();
        var sizeFt = ctx.Pt(ctx.Style.Fonts.Label.SizePt) / map.K;
        var outline = plan.Union;
        var centre = outline.IsEmpty ? null : outline.Centroid.Coordinate;
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
            var block = At(map.Poly(name, f.Resolved, fill, pStroke ?? d.Gray(ps.RoomStroke), pStroke != null ? pPt : ps.RoomPt), a);
            block.Dash = pDash;
            d.Layer("PD_PROGRAM").Items.Add(block);
            if (named.Contains(f.Mass.Id))
                Label(d, ctx, a, map, name, f.Resolved, LabelText(ctx, f), sizeFt, centre, outline, ctx.LabelStyle(d, fill), leaders.Contains(f.Mass.Id));
        }

        if (kind == LevelKind.Site) People(d, ctx, a, map, p, plan, background);

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
        var near = MeshSlicer.Polygonal(Safe(() => PlanBackground.Footprint(ctx.Result).Buffer(ctx.Style.Plan.ContextMarginFt)));
        foreach (var (b, i) in background.Where(b => b.Role is Roles.NcHardscape or Roles.Hardscape && !b.IsLine).Select((b, i) => (b, i)))
        {
            var area = b.Role == Roles.Hardscape ? Safe(() => b.Geometry.Intersection(near)) : b.Geometry;
            var open = MeshSlicer.Polygonal(Safe(() => area!.Difference(plan.Union)));
            if (!open.IsEmpty) Draw(PD.Core.Entourage.EntouragePlacer.Outdoors(ctx, open, $"{p}PLAZA{i}"));
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
        var leaderSize = text.Length > 20 && small > 0 ? small : ctx.Pt(ctx.Style.Fonts.Label.SizePt);
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
        var size = ctx.Pt(ctx.Style.Fonts.Title.SizePt);
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
        anno.Items.Add(At(t, a));
    }

    /// <summary>Programs drawn on this level, in colour-scheme order.</summary>
    static List<string> Keys(ExportContext ctx, PD.Core.Cleanup.LevelPlan plan)
    {
        var used = plan.Footprints.Where(f => !f.OpenToBelow && !f.Resolved.IsEmpty).Select(f => f.Mass.ProgramKey).ToHashSet();
        var order = ctx.Programs.Select(x => x.Key).ToList();
        return used.OrderBy(k => order.IndexOf(k) is var n and >= 0 ? n : int.MaxValue).ThenBy(k => k, StringComparer.Ordinal).ToList();
    }

    static void Legend(PD.Core.Drawing.Drawing d, ExportContext ctx, int a, string p, List<string> keys, double x0, double y0)
    {
        var ps = ctx.Style.Plan;
        var s = ctx.Pt(ps.LegendSwatchPt);
        var cols = Math.Max(1, ps.LegendColumns);
        var layer = d.Layer("PD_LEGEND");
        // Wide enough for the longest name, so entries never run into each other.
        var colW = Math.Max(ctx.Pt(ps.LegendColumnPt),
            keys.Select(k => TextFit.Width(ctx.ProgramLabel(k), ctx.Pt(ctx.Style.Fonts.Note.SizePt)) * 0.85).DefaultIfEmpty(0).Max() + s + ctx.Pt(14));
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
