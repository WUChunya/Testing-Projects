using System.Text.RegularExpressions;
using NetTopologySuite.Geometries;
using PD.Core.Analysis;
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
        var f = ctx.Style.Fonts.Label;
        d.ParaStyles.Add(new ParaStyle("PD Heading", f.PostScript, f.FallbackPostScript, f.Family, ctx.Pt(ctx.Style.Fonts.Title.SizePt), true, "left", d.Gray(pg.LabelColor)!, f.Weight));
        foreach (var n in new[] { "BG_SITE", "BG_CONTEXT", "PD_PROGRAM", "PD_VOID", "PD_OUTLINE", "FACADE", "PD_LABELS", "PD_LEGEND", "PD_ANNOTATION" })
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
        var background = PlanBackground.Collect(r, env);
        double w = env.Width * k, h = env.Height * k, m = pg.MarginPt;

        // One artboard per level.
        var titleH = ctx.Pt(pg.TitleBandPt);
        var band = ctx.Pt(ps.BandPt);
        foreach (var (i, a) in levels.Select((i, a) => (i, a)))
        {
            var lvl = r.Plans[i].Level;
            var keys = Keys(ctx, r.Plans[i]);
            var rows = (int)Math.Ceiling(keys.Count / (double)Math.Max(1, ps.LegendColumns));
            var legendW = Math.Min(keys.Count, ps.LegendColumns) * ctx.Pt(ps.LegendColumnPt);
            var legendH = rows * ctx.Pt(ps.LegendRowPt);
            var bar = ScaleBar(ps, w, k);
            var tools = bar * k + ctx.Pt(ps.NorthArrowPt) * 2.5;
            var bottom = band + Math.Max(legendH, ctx.Pt(ps.NorthArrowPt) * 1.8);
            double abW, abH, x0, y0;
            if (ctx.ArtboardSize is { } fixedSize)
            {
                (abW, abH) = fixedSize;
                x0 = (abW - w) / 2;
                y0 = m + titleH + (abH - 2 * m - titleH - bottom - h) / 2;
                if (x0 < m - 0.5 || y0 < m + titleH - 0.5)
                    d.Notes.Add($"Plan {lvl.Name} at {ctx.Settings.Output.Scale} is larger than {ctx.Settings.Output.Artboard}; use \"Fit content\" or a smaller scale.");
            }
            else
            {
                abW = 2 * m + Math.Max(w, legendW + tools + ctx.Pt(24));
                abH = 2 * m + titleH + h + bottom;
                x0 = m + (abW - 2 * m - w) / 2;
                y0 = m + titleH;
            }
            d.Artboards.Add(new Artboard($"PLAN {lvl.Name}", Math.Ceiling(abW), Math.Ceiling(abH)));
            var map = new PageMap(x0 - env.MinX * k, y0, k, env.MaxY, env.Width, flip: false);
            var p = $"PLN_{SectionDrawing.Safe(lvl.Name)}_";
            DrawLevel(d, ctx, a, map, i, p, background);

            var anno = d.Layer("PD_ANNOTATION");
            // Title: yellow square + "Level 1" (reference: Atlanta level labels).
            var sq = ctx.Pt(ps.TitleMarkerPt);
            var ty = m + ctx.Pt(16);
            if (sq > 0)
                anno.Items.Add(At(new DItem
                {
                    Kind = ItemKind.Path, Name = p + "TITLE_MARK", Fill = d.Swatch("PD_LEVEL_MARKER", ps.TitleMarker),
                    Paths = new() { new[] { new[] { m, ty - sq }, new[] { m + sq, ty - sq }, new[] { m + sq, ty }, new[] { m, ty } } },
                }, a));
            anno.Items.Add(At(SectionDrawing.Text(p + "TITLE", m + (sq > 0 ? sq * 1.8 : 0), ty, Title(ps.LevelTitleFormat, lvl.Name), "PD Level Title", "left"), a));
            var yb = abH - m - bottom + band;
            Legend(d, ctx, a, p, keys, m, yb);
            var xr = abW - m;
            North(d, ctx, a, p, xr - ctx.Pt(ps.NorthArrowPt) * 0.5, yb);
            Bar(d, ctx, a, p, bar, xr - ctx.Pt(ps.NorthArrowPt) * 2 - bar * k, yb + ctx.Pt(ps.NorthArrowPt) * 0.6);
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
            DrawLevel(d, ctx, ov, new PageMap(cx - env.MinX * k, cy + nameBand, k, env.MaxY, env.Width, flip: false), i, p, background);
            d.Layer("PD_ANNOTATION").Items.Add(At(SectionDrawing.Text(p + "NAME", cx, cy + ctx.Pt(14), lvl.Name, "PD Heading", "left"), ov));
        }
        return d;
    }

    /// <summary>Background, program blocks, open-to-below voids, outline, facade line and labels of one level.</summary>
    static void DrawLevel(PD.Core.Drawing.Drawing d, ExportContext ctx, int a, PageMap map, int i, string p, List<BackgroundItem> background)
    {
        var r = ctx.Result;
        var ps = ctx.Style.Plan;
        var plan = r.Plans[i];

        foreach (var (b, n) in background.Select((b, n) => (b, n)))
        {
            var (fill, stroke, pt, dash) = PlanBackground.Paint(d, ctx.Style, b);
            var item = map.Geom($"{p}BG_{n:000}", b.Geometry, fill, stroke, pt, !b.IsLine);
            item.Dash = dash;
            d.Layer(PlanBackground.LayerName(b)).Items.Add(At(item, a));
        }

        var count = new Dictionary<string, int>();
        var sizeFt = ctx.Pt(ctx.Style.Fonts.Label.SizePt) / map.K;
        var outline = plan.Union;
        var centre = outline.IsEmpty ? null : outline.Centroid.Coordinate;
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
                Label(d, ctx, a, map, name, f.Resolved, ps.OpenToBelowLabel, sizeFt, centre, outline);
                continue;
            }
            var (fill, pStroke, pPt, pDash) = ctx.ProgramPaint(d, f.Mass.ProgramKey);
            var block = At(map.Poly(name, f.Resolved, fill, pStroke ?? d.Gray(ps.RoomStroke), pStroke != null ? pPt : ps.RoomPt), a);
            block.Dash = pDash;
            d.Layer("PD_PROGRAM").Items.Add(block);
            var text = row?.Label ?? ctx.ProgramLabel(f.Mass.ProgramKey);
            Label(d, ctx, a, map, name, f.Resolved, text, sizeFt, centre, outline, ctx.LabelStyle(d, fill));
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

    /// <summary>Inside the block when it fits (widest spot, long direction); otherwise a leader to a label outside the building.</summary>
    static void Label(PD.Core.Drawing.Drawing d, ExportContext ctx, int a, PageMap map, string name, NtsGeometry block, string text,
        double sizeFt, Coordinate? centre, NtsGeometry outline, string inside = "PD Label")
    {
        if (ctx.Style.Fonts.Label.AllCaps) text = text.ToUpperInvariant();
        var layer = d.Layer("PD_LABELS");
        var spot = PlanLabels.Fit(block, text, sizeFt);
        if (spot != null)
        {
            layer.Items.Add(At(SectionDrawing.Text(name + "_LBL", map.X(spot.X), map.Y(spot.Y), spot.Text, inside, "center", spot.AngleDeg, center: true), a));
            return;
        }
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
        layer.Items.Add(At(map.Line(name + "_LDR", new[] { (anchor.X, anchor.Y), (end.X, end.Y) }, d.Gray(ps.LeaderStroke), ps.LeaderPt), a));
        var right = dx >= 0;
        layer.Items.Add(At(SectionDrawing.Text(name + "_LBL", map.X(end.X) + (right ? 2 : -2), map.Y(end.Y) + ctx.Pt(2.5), text, "PD Label", right ? "left" : "right"), a));
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
        foreach (var (key, n) in keys.Select((key, n) => (key, n)))
        {
            double x = x0 + n % cols * ctx.Pt(ps.LegendColumnPt), y = y0 + n / cols * ctx.Pt(ps.LegendRowPt);
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
        layer.Items.Add(At(SectionDrawing.Text($"{p}SCALE", x0, y0 - ctx.Pt(4), $"SCALE {ctx.Settings.Output.Scale}", "PD Note", "left"), a));
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
        catch (TopologyException) { return null; }
    }
}
