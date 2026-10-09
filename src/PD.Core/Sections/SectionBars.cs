using PD.Core.Drawing;

namespace PD.Core.Sections;

/// <summary>PRD 5.3 cut program bars: rounded, inset 0.12 × story top and bottom, 1.5 pt white gaps, centred wrapped labels.</summary>
public static class SectionBars
{
    public static void Add(PD.Core.Drawing.Drawing d, ExportContext ctx, SectionGeometry g, PageMap map, string prefix)
    {
        var st = ctx.Style.Section;
        var label = ctx.Style.Fonts.Label;
        var count = new Dictionary<string, int>();
        foreach (var b in g.Bars.OrderBy(b => b.Z0).ThenBy(b => b.U0))
        {
            var inset = st.BarInsetStoryFactor * b.Story * map.K;
            var x0 = Math.Min(map.X(b.U0), map.X(b.U1)) + st.BarGapPt / 2;
            var x1 = Math.Max(map.X(b.U0), map.X(b.U1)) - st.BarGapPt / 2;
            var yTop = map.Y(b.Z1) + inset;
            var yBot = map.Y(b.Z0) - inset;
            var w = x1 - x0;
            var h = yBot - yTop;
            if (w < 1 || h < 1) continue;

            var id = ctx.Names.TryGetValue(b.MassId, out var row) ? row.StableId : b.MassId[..Math.Min(8, b.MassId.Length)];
            var n = count[id] = count.GetValueOrDefault(id) + 1;
            var name = prefix + id + (n > 1 ? $"_{n}" : "");
            var (fill, stroke, strokePt, dash) = ctx.ProgramPaint(d, b.ProgramKey);
            d.Layer("PD_PROGRAM").Items.Add(new DItem
            {
                Kind = ItemKind.RoundRect, Name = name, X = x0, Y = yTop, W = w, H = h,
                R = Math.Min(st.BarCornerPt, Math.Min(w, h) / 2), Fill = fill, Stroke = stroke, StrokePt = strokePt, Dash = dash,
            });

            if (w < ctx.Style.Page.LabelMinBarWidthPt) continue;
            var text = row?.Label ?? ctx.ProgramLabel(b.ProgramKey);
            if (label.AllCaps) text = text.ToUpperInvariant();
            var wrapped = TextFit.Wrap(text, label.SizePt, w - 4, h - 2);
            if (wrapped == null) continue;
            var th = TextFit.Height(wrapped, label.SizePt);
            // Baseline of the first line so the block is vertically centred in the bar.
            var y = yTop + (h - th) / 2 + label.SizePt * 0.95;
            d.Layer("PD_LABELS").Items.Add(SectionDrawing.Text(name + "_LBL", x0 + w / 2, y, wrapped, "PD Label", "center"));
        }
    }
}
