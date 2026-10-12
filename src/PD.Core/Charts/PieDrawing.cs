using PD.Core.Drawing;
using PD.Core.Sections;

namespace PD.Core.Charts;

/// <summary>
/// PRD 5.6 "PROGRAM SF": a donut (inner radius 43 % of the outer), starting at 12 o'clock and running clockwise in
/// colour-scheme order, bold centre text, legend on the right (bold TOTAL: and one row per program), plus an
/// optional stacked bar. Source: the model's GSF or the Excel ASF table (same as the program chart).
/// </summary>
public static class PieDrawing
{
    const int Steps = 180; // arc segments of a full circle

    public static PD.Core.Drawing.Drawing Build(ExportContext outer)
    {
        var ctx = outer.ForChart();
        var cs = ctx.Style.Charts.Pie;
        var d = new PD.Core.Drawing.Drawing { FileName = "06_Program_Pie.ai" };
        ctx.AddStandardStyles(d);
        foreach (var n in new[] { "PD_PIE", "PD_PIE_LEGEND", "PD_PIE_TEXT" }) d.Layer(n);

        var table = ProgramTable.For(ctx);
        d.Notes.AddRange(table.Notes);
        var shares = table.ByProgram(ctx.Programs);
        var total = shares.Sum(s => s.Area);
        if (total <= 0)
        {
            d.Artboards.Add(new Artboard("PROGRAM SF", 600, 200));
            d.Notes.Add("Pie: no areas.");
            return d;
        }

        var legendW = shares.Max(s => TextFit.Width($"{ctx.ProgramLabel(s.Key)} {ProgramTable.Sf(s.Area)} SF", ctx.Style.Fonts.ChartLegend.SizePt))
                      + cs.LegendSwatchPt + 12;
        var legendH = (shares.Count + 2) * cs.LegendRowPt;
        var barH = cs.Bar.Enabled ? cs.Bar.HeightPt + 3 * cs.LegendRowPt : 0;
        var w = 2 * cs.MarginPt + cs.OuterPt + cs.LegendGapPt + legendW;
        if (cs.Bar.Enabled) w = Math.Max(w, 2 * cs.MarginPt + cs.Bar.WidthPt);
        var h = 2 * cs.MarginPt + Math.Max(cs.OuterPt, legendH) + barH;
        d.Artboards.Add(new Artboard("PROGRAM SF", Math.Ceiling(w), Math.Ceiling(h)));

        double cx = cs.MarginPt + cs.OuterPt / 2, cy = cs.MarginPt + cs.OuterPt / 2;
        double ro = cs.OuterPt / 2, ri = ro * cs.InnerRatio;
        var pie = d.Layer("PD_PIE");
        var angle = cs.StartDeg;
        foreach (var (key, area) in shares)
        {
            var sweep = 360.0 * area / total;
            var a0 = angle;
            var a1 = cs.Clockwise ? angle - sweep : angle + sweep;
            angle = a1;
            var (fill, stroke, pt, dash) = ctx.ProgramPaint(d, key);
            pie.Items.Add(new DItem
            {
                Kind = ItemKind.Path, Name = "PIE_" + key, Fill = fill,
                Stroke = stroke ?? d.Gray(cs.SegmentStroke), StrokePt = stroke != null ? pt : cs.SegmentStrokePt, Dash = dash,
                Paths = new() { Ring(cx, cy, ro, ri, a0, a1) },
            });
        }
        var centre = SectionDrawing.Text("PIE_CENTER", cx, cy, cs.CenterText, "PD Chart Total Left", "center", center: true);
        // Keep the bold centre text inside the hole.
        var fit = ri * 1.7 / Math.Max(1, TextFit.Width(cs.CenterText, 1));
        if (fit < ctx.Style.Fonts.ChartTotal.SizePt) centre.SizePt = fit;
        d.Layer("PD_PIE_TEXT").Items.Add(centre);

        // Legend: bold TOTAL: and the value, then "swatch + Office 12,813 SF" per row.
        double lx = cs.MarginPt + cs.OuterPt + cs.LegendGapPt, ly = cs.MarginPt + cs.LegendRowPt;
        var legend = d.Layer("PD_PIE_LEGEND");
        legend.Items.Add(SectionDrawing.Text("PIE_TOTAL_LBL", lx, ly, cs.TotalLabel, "PD Chart Total Left", "left"));
        legend.Items.Add(SectionDrawing.Text("PIE_TOTAL", lx + TextFit.Width(cs.TotalLabel, ctx.Style.Fonts.ChartTotal.SizePt) + 8, ly,
            $"{ProgramTable.Sf(total)} {table.Unit}", "PD Chart Total Left", "left"));
        ly += cs.LegendRowPt;
        foreach (var (key, area) in shares)
        {
            ly += cs.LegendRowPt;
            var (fill, stroke, pt, dash) = ctx.ProgramPaint(d, key);
            var s = cs.LegendSwatchPt;
            legend.Items.Add(new DItem
            {
                Kind = ItemKind.Path, Name = "PIE_LEG_" + key, Fill = fill, Stroke = stroke, StrokePt = pt, Dash = dash,
                Paths = new() { new[] { new[] { lx, ly - s }, new[] { lx + s, ly - s }, new[] { lx + s, ly }, new[] { lx, ly } } },
            });
            legend.Items.Add(SectionDrawing.Text("PIE_LEG_" + key + "_TXT", lx + s + 8, ly - 1,
                $"{ctx.ProgramLabel(key)}  {ProgramTable.Sf(area)} SF", "PD Chart Legend", "left"));
        }

        if (cs.Bar.Enabled) Bar(d, ctx, shares, total, cs.MarginPt, h - cs.MarginPt - cs.Bar.HeightPt, table.Unit);
        return d;
    }

    /// <summary>Horizontal stacked bar in the same order and colours (PRD 5.6 optional output).</summary>
    static void Bar(PD.Core.Drawing.Drawing d, ExportContext ctx, List<(string Key, double Area)> shares, double total,
        double x0, double y0, string unit)
    {
        var cs = ctx.Style.Charts.Pie;
        var layer = d.Layer("PD_PIE");
        if (cs.Bar.Title.Length > 0)
            d.Layer("PD_PIE_TEXT").Items.Add(SectionDrawing.Text("BAR_TITLE", x0, y0 - 10, cs.Bar.Title, "PD Chart Title", "left"));
        var x = x0;
        foreach (var (key, area) in shares)
        {
            var w = Math.Max(0.5, cs.Bar.WidthPt * area / total - cs.Bar.GapPt);
            var (fill, stroke, pt, dash) = ctx.ProgramPaint(d, key);
            layer.Items.Add(new DItem
            {
                Kind = ItemKind.Path, Name = "BAR_" + key, Fill = fill, Stroke = stroke, StrokePt = pt, Dash = dash,
                Paths = new() { new[] { new[] { x, y0 }, new[] { x + w, y0 }, new[] { x + w, y0 + cs.Bar.HeightPt }, new[] { x, y0 + cs.Bar.HeightPt } } },
            });
            if (w > 34)
                d.Layer("PD_PIE_TEXT").Items.Add(SectionDrawing.Text("BAR_" + key + "_TXT", x + w / 2, y0 + cs.Bar.HeightPt + 12,
                    $"{100 * area / total:0}%", "PD Chart Legend", "center"));
            x += w + cs.Bar.GapPt;
        }
        d.Layer("PD_PIE_TEXT").Items.Add(SectionDrawing.Text("BAR_TOTAL", x0 + cs.Bar.WidthPt, y0 - 10,
            $"{ProgramTable.Sf(total)} {unit}", "PD Chart Legend Right", "right"));
    }

    /// <summary>One donut segment as a closed ring: outer arc from a0 to a1, inner arc back. Degrees, y down.</summary>
    static double[][] Ring(double cx, double cy, double ro, double ri, double a0, double a1)
    {
        var n = Math.Max(2, (int)Math.Ceiling(Math.Abs(a1 - a0) / 360.0 * Steps));
        var pts = new List<double[]>();
        for (var i = 0; i <= n; i++) pts.Add(At(cx, cy, ro, a0 + (a1 - a0) * i / n));
        for (var i = n; i >= 0; i--) pts.Add(At(cx, cy, ri, a0 + (a1 - a0) * i / n));
        return pts.ToArray();
    }

    static double[] At(double cx, double cy, double r, double deg)
    {
        var a = deg * Math.PI / 180;
        return new[] { cx + r * Math.Cos(a), cy - r * Math.Sin(a) };
    }
}
