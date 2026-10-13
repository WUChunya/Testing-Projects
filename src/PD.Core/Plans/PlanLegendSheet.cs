using PD.Core.Classification;
using PD.Core.Drawing;
using PD.Core.Entourage;
using PD.Core.Sections;

namespace PD.Core.Plans;

/// <summary>
/// "LEGEND & SYMBOLS" artboard of 01_Plans.ai (user rev 0.8): every legend entry and symbol the styles use, kept in one
/// place for later work — all programs of the scheme, every role paint, the line types, north arrow, scale bar, level
/// tag, and every office person / tree and the plan figure as AI symbols. Absolute pt (like the charts).
/// </summary>
public static class PlanLegendSheet
{
    const string Prefix = "LGD_";

    public static void Add(PD.Core.Drawing.Drawing d, ExportContext ctx, Action<int, double, double> northAndBar)
    {
        var ps = ctx.Style.Plan;
        var e = ctx.Style.Entourage;
        var a = d.Artboards.Count;
        const double m = 54, row = 22, sw = 14;
        // Columns as wide as their longest entry.
        var noteSize = ctx.Pt(ctx.Style.Fonts.Note.SizePt);
        var colW = ctx.Programs.Select(p => p.Label).Concat(Roles.All).Concat(new[] { "SITE / CONTEXT / NC" })
            .Max(t => TextFit.Width(t.ToUpperInvariant(), noteSize)) + sw * 3 + 24;
        var layer = d.Layer("PD_LEGEND");
        var x = m;
        var y = m;

        DItem At(DItem it) { it.Artboard = a; return it; }
        void Heading(string text, double hx, double hy) => layer.Items.Add(At(SectionDrawing.Text(Prefix + Safe(text), hx, hy, text, "PD Heading", "left")));
        void Label(string name, string text, double lx, double ly) => layer.Items.Add(At(SectionDrawing.Text(Prefix + name + "_TXT", lx + sw + 8, ly + sw * 0.8, text, "PD Note", "left")));
        double[][] Rect(double rx, double ry) => new[] { new[] { rx, ry }, new[] { rx + sw, ry }, new[] { rx + sw, ry + sw }, new[] { rx, ry + sw } };

        // 1. Programs (scheme order).
        Heading("PROGRAM", x, y);
        var programs = ctx.Programs.OrderBy(p => p.Order).ToList();
        var py = y + 16;
        foreach (var p in programs)
        {
            var (fill, stroke, pt, dash) = ctx.ProgramPaint(d, p.Key);
            layer.Items.Add(At(new DItem
            {
                Kind = ItemKind.Path, Name = $"{Prefix}PRG_{p.Key}", Fill = fill, Stroke = stroke ?? d.Gray("#BDBDBD"), StrokePt = stroke != null ? pt : 0.25, Dash = dash,
                Paths = new() { Rect(x, py) },
            }));
            Label($"PRG_{p.Key}", p.Label, x, py);
            py += row;
        }

        // 2. Roles (plan paints): fills as swatches, line roles as line samples.
        x += colW;
        Heading("SITE / CONTEXT / NC", x, y);
        var ry = y + 16;
        foreach (var role in Roles.All.Where(r => ctx.Style.Roles.ContainsKey(r)))
        {
            var paint = PlanBackground.PaintOf(ctx.Style, role, "plan");
            var name = "ROLE_" + PlanBackground.SwatchName(role);
            var line = paint.Fill == null;
            layer.Items.Add(At(new DItem
            {
                Kind = ItemKind.Path, Name = $"{Prefix}{name}", Closed = !line, Opacity = paint.Opacity,
                Fill = line ? null : d.Swatch(PlanBackground.SwatchName(role), paint.Fill),
                Stroke = d.Swatch(PlanBackground.SwatchName(role) + "_LINE", paint.Stroke), StrokePt = paint.StrokePt ?? (line ? 0.5 : 0), Dash = paint.Dash,
                Paths = new() { line ? new[] { new[] { x, ry + sw / 2 }, new[] { x + sw * 2, ry + sw / 2 } } : Rect(x, ry) },
            }));
            Label(name, role, x + (line ? sw : 0), ry);
            ry += row;
        }

        // 3. Line types.
        x += colW;
        Heading("LINES", x, y);
        var ly = y + 16;
        void Line(string name, string text, string? stroke, double pt, double[]? dash = null, double opacity = 1)
        {
            layer.Items.Add(At(new DItem
            {
                Kind = ItemKind.Path, Name = $"{Prefix}LINE_{name}", Closed = false, Stroke = d.Gray(stroke ?? "#000000"), StrokePt = pt, Dash = dash, Opacity = opacity,
                Paths = new() { new[] { new[] { x, ly + sw / 2 }, new[] { x + sw * 2, ly + sw / 2 } } },
            }));
            Label("LINE_" + name, text, x + sw, ly);
            ly += row;
        }
        Line("OUTLINE", "BUILDING OUTLINE", ps.OutlineStroke, ps.OutlinePt);
        Line("ROOM", "ROOM DIVIDER", "#BDBDBD", ps.RoomPt);
        Line("FACADE", "GLASS LINE", ps.FacadeStroke, ps.FacadePt);
        Line("VOID", "OPEN TO BELOW", ps.VoidStroke, ps.VoidPt, ps.VoidDash);
        Line("LEADER", "LEADER", ps.LeaderStroke, ps.LeaderPt);
        Line("HARDSCAPE_EDGE", "HARDSCAPE EDGE", ps.HardscapeEdge.Stroke, ps.HardscapeEdge.StrokePt ?? 1, ps.HardscapeEdge.Dash, ps.HardscapeEdge.Opacity);
        Line("ROAD_EDGE", "SITE / ROAD EDGE", ps.RoadEdge.Stroke, ps.RoadEdge.StrokePt ?? 1.5, ps.RoadEdge.Dash, ps.RoadEdge.Opacity);

        // 4. Annotation symbols: north arrow, scale bar, level tag.
        ly += 24;
        Heading("ANNOTATION", x, ly);
        northAndBar(a, x, ly + 20);
        var tag = SectionDrawing.Text(Prefix + "LEVEL_TAG", x, ly + 110, PlanDrawing.Title(ps.LevelTitleFormat, "L1"), "PD Title", "left");
        tag.SizePt = ctx.Pt(ctx.Style.Fonts.Label.SizePt);
        layer.Items.Add(At(tag));

        // 5. Annotation symbols for analysis overlays (user rev 0.8.4): entry, service, view, car, flows, transit, tags…
        var sx = x + colW;
        var sy = AnnotationSymbols.Add(d, ctx, AnnotationSymbols.Set.Plan, Prefix, a, sx, y);

        // 6. Entourage symbols: every office person and tree (all colours), the plan figure.
        var ey = Math.Max(Math.Max(Math.Max(py, ry), ly + 140), sy) + 30;
        Heading("ENTOURAGE", m, ey);
        var lib = EntourageLibrary.Default;
        var ex = m;
        var top = EntourageArt.PersonTop(d, e.Color);
        layer.Items.Add(At(EntourageArt.Place(Prefix + "PERSON_TOP", top, ex + 12, ey + 40, 24)));
        ex += 40;
        foreach (var f in lib.People)
        {
            layer.Items.Add(At(EntourageArt.Place($"{Prefix}{f.Id}", EntourageArt.Person(d, f, e.Color), ex + 8, ey + 70, 50)));
            ex += 22;
        }
        var tx = m;
        var ty = ey + 190;
        foreach (var fill in e.TreeFills.DefaultIfEmpty("#9CC078"))
        {
            tx = m;
            foreach (var f in lib.Trees)
            {
                layer.Items.Add(At(EntourageArt.Place($"{Prefix}{f.Id}_{fill.TrimStart('#')}", EntourageArt.Tree(d, f, fill), tx + 35, ty, 90, false, e.TreeOpacity)));
                tx += 80;
            }
            ty += 110;
        }
        d.Artboards.Add(new Artboard("LEGEND & SYMBOLS", Math.Ceiling(Math.Max(Math.Max(ex, tx) + m, sx + 360 + m)), Math.Ceiling(ty + m)));
    }

    static string Safe(string s) => SectionDrawing.Safe(s.ToUpperInvariant());
}
