using PD.Core.Drawing;
using PD.Core.Sections;

namespace PD.Core.Charts;

/// <summary>
/// PRD 5.5 "Detailed Program Proposal": one dashed grey box per Department, title above it in bold caps, rooms
/// grouped by Sub-group, every room type a row of Qty rectangles scaled by area, department total bottom right.
/// Boxes are shelf-packed into the page width. Absolute point sizes (the chart has no drawing scale).
/// </summary>
public static class ProgramChartDrawing
{
    sealed record Rect(double W, double H, string Key);

    sealed record RoomRow(string Label, List<Rect> Rects, double W, double H);

    sealed record Group(string Name, List<RoomRow> Rows, double W, double H);

    sealed record Box(string Department, List<Group> Groups, double Total, double W, double H)
    {
        public double X { get; set; }
        public double Y { get; set; }
    }

    public static PD.Core.Drawing.Drawing Build(ExportContext outer)
    {
        var ctx = outer.ForChart();
        var cs = ctx.Style.Charts.ProgramChart;
        var d = new PD.Core.Drawing.Drawing { FileName = "05_Program_Chart.ai" };
        ctx.AddStandardStyles(d);
        foreach (var n in new[] { "PD_CHART_BOX", "PD_CHART_BLOCKS", "PD_CHART_TEXT" }) d.Layer(n);

        var table = ProgramTable.For(ctx);
        d.Notes.AddRange(table.Notes);
        if (table.Rows.Count == 0)
        {
            d.Artboards.Add(new Artboard("PROGRAM CHART", 600, 200));
            d.Notes.Add("Program chart: no rows.");
            return d;
        }

        var inner = cs.PageWidthPt - 2 * cs.MarginPt;
        var boxes = table.Rows
            .GroupBy(r => r.Department.Length > 0 ? r.Department : ctx.ProgramLabel(r.ProgramKey))
            .Select(g => Measure(ctx, g.Key, g.ToList(), inner))
            .ToList();
        var pageH = Pack(boxes, inner, cs.BoxGapPt) + 2 * cs.MarginPt + cs.MarginPt;
        d.Artboards.Add(new Artboard("PROGRAM CHART", Math.Ceiling(cs.PageWidthPt), Math.Ceiling(pageH)));

        var top = cs.MarginPt;
        if (cs.Title.Length > 0)
        {
            d.Layer("PD_CHART_TEXT").Items.Add(SectionDrawing.Text("CHT_TITLE", cs.MarginPt, top + 16, cs.Title, "PD Chart Total Left", "left"));
            top += cs.MarginPt;
        }
        foreach (var (b, n) in boxes.Select((b, n) => (b, n))) Draw(d, ctx, b, cs.MarginPt + b.X, top + b.Y, n + 1, table.Unit);
        d.Layer("PD_CHART_TEXT").Items.Add(SectionDrawing.Text("CHT_SOURCE", cs.MarginPt, pageH - cs.MarginPt / 2,
            $"{table.Source} — total {ProgramTable.Sf(table.Total)} {table.Unit}", "PD Note", "left"));
        return d;
    }

    /// <summary>Lays one department out (groups, rows of rectangles) and returns its size.</summary>
    static Box Measure(ExportContext ctx, string department, List<ProgramRow> rows, double maxWidth)
    {
        var cs = ctx.Style.Charts.ProgramChart;
        var contentW = Math.Max(120, maxWidth - 2 * cs.BoxPadPt);
        var groups = new List<Group>();
        double w = 0, h = 0;
        foreach (var g in rows.GroupBy(r => r.SubGroup))
        {
            var roomRows = new List<RoomRow>();
            double gw = 0, gh = 0;
            if (g.Key.Length > 0) gh += ctx.Style.Fonts.ChartGroup.SizePt + cs.LabelGapPt;
            // Rooms of the same name share one row: the model source gives one rectangle per mass.
            foreach (var room in g.GroupBy(r => (r.Code.Length > 0 ? r.Code + " " : "") + r.RoomName))
            {
                var rects = new List<Rect>();
                foreach (var r in room)
                {
                    var side = Math.Clamp(Math.Sqrt(Math.Max(1, r.AreaEach)) * cs.AreaScalePtPerSqrtSf, cs.RectMinPt, cs.RectMaxPt);
                    for (var i = 0; i < (int)Math.Round(Math.Max(1, r.Count)); i++) rects.Add(new Rect(side, side, r.ProgramKey));
                }
                var (rw, rh) = Flow(rects, contentW, cs.RectGapPt);
                rh += ctx.Style.Fonts.ChartLegend.SizePt + cs.LabelGapPt;
                roomRows.Add(new RoomRow(room.Key, rects, rw, rh));
                gw = Math.Max(gw, Math.Max(rw, TextFit.Width(room.Key, ctx.Style.Fonts.ChartLegend.SizePt)));
                gh += rh + cs.RowGapPt;
            }
            groups.Add(new Group(g.Key, roomRows, gw, gh));
            w = Math.Max(w, gw);
            h += gh + cs.GroupGapPt;
        }
        var total = rows.Sum(r => r.Area);
        var titleW = TextFit.Width(department, ctx.Style.Fonts.ChartTitle.SizePt);
        var totalW = TextFit.Width(ProgramTable.Sf(total), ctx.Style.Fonts.ChartTotal.SizePt);
        w = Math.Min(contentW, Math.Max(Math.Max(w, cs.MinBoxWidthPt), Math.Max(titleW, totalW)));
        h += ctx.Style.Fonts.ChartTotal.SizePt + cs.LabelGapPt;
        return new Box(department, groups, total, w + 2 * cs.BoxPadPt, h + 2 * cs.BoxPadPt + TitleBand(ctx));
    }

    /// <summary>Height of the bold department title drawn above each box.</summary>
    static double TitleBand(ExportContext ctx) => ctx.Style.Fonts.ChartTitle.SizePt + ctx.Style.Charts.ProgramChart.TitleGapPt;

    /// <summary>Splits a run of rectangles into the lines that fit the available width.</summary>
    static List<List<Rect>> Lines(List<Rect> rects, double maxW, double gap)
    {
        var lines = new List<List<Rect>> { new() };
        double x = 0;
        foreach (var r in rects)
        {
            if (x > 0 && x + r.W > maxW + 0.01)
            {
                lines.Add(new List<Rect>());
                x = 0;
            }
            lines[^1].Add(r);
            x += r.W + gap;
        }
        return lines.Where(l => l.Count > 0).ToList();
    }

    /// <summary>Size of a wrapped run of rectangles.</summary>
    static (double W, double H) Flow(List<Rect> rects, double maxW, double gap)
    {
        var lines = Lines(rects, maxW, gap);
        var w = lines.Max(l => l.Sum(r => r.W + gap) - gap);
        var h = lines.Sum(l => l.Max(r => r.H) + gap) - gap;
        return (w, h);
    }

    /// <summary>Shelf packing: boxes fill a row until the page width is reached, then a new row starts.</summary>
    static double Pack(List<Box> boxes, double width, double gap)
    {
        double x = 0, y = 0, rowH = 0;
        foreach (var b in boxes)
        {
            if (x > 0 && x + b.W > width)
            {
                x = 0;
                y += rowH + gap;
                rowH = 0;
            }
            b.X = x;
            b.Y = y;
            x += b.W + gap;
            rowH = Math.Max(rowH, b.H);
        }
        return y + rowH;
    }

    static void Draw(PD.Core.Drawing.Drawing d, ExportContext ctx, Box b, double x0, double y0, int index, string unit)
    {
        var cs = ctx.Style.Charts.ProgramChart;
        var p = $"CHT_{index:00}_";
        var text = d.Layer("PD_CHART_TEXT");
        var blocks = d.Layer("PD_CHART_BLOCKS");

        var band = TitleBand(ctx);
        text.Items.Add(SectionDrawing.Text(p + "TITLE", x0, y0 + ctx.Style.Fonts.ChartTitle.SizePt, b.Department.ToUpperInvariant(), "PD Chart Title", "left"));
        var top = y0 + band;
        d.Layer("PD_CHART_BOX").Items.Add(new DItem
        {
            Kind = ItemKind.Path, Name = p + "BOX", Stroke = d.Gray(cs.BoxStroke), StrokePt = cs.BoxPt, Dash = cs.BoxDash,
            Paths = new() { new[] { new[] { x0, top }, new[] { x0 + b.W, top }, new[] { x0 + b.W, y0 + b.H }, new[] { x0, y0 + b.H } } },
        });

        double x = x0 + cs.BoxPadPt, y = top + cs.BoxPadPt;
        var n = 0;
        foreach (var g in b.Groups)
        {
            if (g.Name.Length > 0)
            {
                text.Items.Add(SectionDrawing.Text($"{p}G{++n:00}", x, y + ctx.Style.Fonts.ChartGroup.SizePt, g.Name, "PD Chart Group", "left"));
                y += ctx.Style.Fonts.ChartGroup.SizePt + cs.LabelGapPt;
            }
            foreach (var row in g.Rows)
            {
                text.Items.Add(SectionDrawing.Text($"{p}R{++n:00}_LBL", x, y + ctx.Style.Fonts.ChartLegend.SizePt, row.Label, "PD Chart Legend", "left"));
                var ry = y + ctx.Style.Fonts.ChartLegend.SizePt + cs.LabelGapPt;
                var k = 0;
                foreach (var line in Lines(row.Rects, b.W - 2 * cs.BoxPadPt, cs.RectGapPt))
                {
                    var lineH = line.Max(r => r.H);
                    double rx = x;
                    foreach (var rect in line)
                    {
                        // Rectangles of one line sit on a common baseline, like the reference sheets.
                        var ty = ry + lineH - rect.H;
                        var (fill, stroke, pt, dash) = ctx.ProgramPaint(d, rect.Key);
                        blocks.Items.Add(new DItem
                        {
                            Kind = ItemKind.Path, Name = $"{p}R{n:00}_{++k:00}", Fill = fill,
                            Stroke = stroke ?? (cs.RectStrokePt > 0 ? d.Gray(cs.RectStroke) : null),
                            StrokePt = stroke != null ? pt : cs.RectStrokePt, Dash = dash,
                            Paths = new() { new[] { new[] { rx, ty }, new[] { rx + rect.W, ty }, new[] { rx + rect.W, ty + rect.H }, new[] { rx, ty + rect.H } } },
                        });
                        rx += rect.W + cs.RectGapPt;
                    }
                    ry += lineH + cs.RectGapPt;
                }
                y += row.H + cs.RowGapPt;
            }
            y += cs.GroupGapPt;
        }
        text.Items.Add(SectionDrawing.Text(p + "TOTAL", x0 + b.W - cs.BoxPadPt, y0 + b.H - cs.BoxPadPt,
            string.Format(cs.TotalFormat, b.Total, unit), "PD Chart Total", "right"));
    }
}
