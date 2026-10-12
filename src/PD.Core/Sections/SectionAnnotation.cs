using PD.Core.Drawing;
using PD.Core.Model;
using PD.Core.Settings;

namespace PD.Core.Sections;

/// <summary>PRD 5.3 annotations: property lines, overall height dimension with slash ticks, title and scale.</summary>
public static class SectionAnnotation
{
    public static void Add(PD.Core.Drawing.Drawing d, ExportContext ctx, SectionGeometry g, PageMap map, string p,
        SectionSetting sec, double zMin, double zMax, double titleX, double titleY)
    {
        var anno = d.Layer("PD_ANNOTATION");
        var black = d.Gray("#000000");
        var thin = ctx.Style.Strokes.BackgroundPt;

        var st = ctx.Style.Section;
        foreach (var (u, i) in g.PropertyLines.Select((u, i) => (u, i)))
        {
            var line = map.Line(p + $"PROPERTY_{i}", new[] { (u, zMin), (u, zMax) }, d.Swatch("BG_PROPERTY_LINE", st.PropertyStroke), st.PropertyPt);
            line.Dash = st.PropertyDash;
            anno.Items.Add(line);
        }

        var env = g.Outline.EnvelopeInternal;
        if (ctx.Style.Section.HeightDimension && !g.Outline.IsEmpty && env.MaxY > g.Ground)
        {
            var x = map.X(g.Plane.Length) + ctx.Pt(22);
            double yTop = map.Y(env.MaxY), yBot = map.Y(g.Ground);
            var tick = ctx.Pt(4);
            anno.Items.Add(Seg(p + "DIM_HEIGHT", x, yTop, x, yBot, black, thin));
            anno.Items.Add(Seg(p + "DIM_HEIGHT_T1", x - tick, yTop + tick, x + tick, yTop - tick, black, thin));
            anno.Items.Add(Seg(p + "DIM_HEIGHT_T2", x - tick, yBot + tick, x + tick, yBot - tick, black, thin));
            anno.Items.Add(SectionDrawing.Text(p + "DIM_HEIGHT_TXT", x - ctx.Pt(4), (yTop + yBot) / 2, FeetInches.Format(env.MaxY - g.Ground), "PD Note", "center", 90));
        }

        anno.Items.Add(SectionDrawing.Text(p + "TITLE", titleX, titleY, $"{ctx.Style.Section.TitlePrefix} {sec.Name}", "PD Title", "left"));
        anno.Items.Add(SectionDrawing.Text(p + "SCALE", titleX, titleY + ctx.Pt(18), $"SCALE {ctx.ScaleText}", "PD Note", "left"));
    }

    static DItem Seg(string name, double x0, double y0, double x1, double y1, string? stroke, double pt) => new()
    {
        Kind = ItemKind.Path, Name = name, Closed = false, Stroke = stroke, StrokePt = pt,
        Paths = new() { new[] { new[] { x0, y0 }, new[] { x1, y1 } } },
    };
}
