using System.Text.RegularExpressions;
using PD.Core.Drawing;

namespace PD.Core.Sections;

/// <summary>
/// Program section bars (docs/SITE_STYLE.md §4): rounded bars 0.68 × floor tall (corner 0.14 × floor, no stroke), 0.08 ×
/// floor above the floor line and 0.10 × floor clear of the slab above; label right-aligned (Regular caps ≤ 20 pt =
/// 0.7 × bar, shrunk to fit, dropped under 10 pt); floor number ("01") left in the first bar of each level.
/// </summary>
public static class SectionBars
{
    public static void Add(PD.Core.Drawing.Drawing d, ExportContext ctx, SectionGeometry g, PageMap map, string prefix)
    {
        var st = ctx.Style.Section;
        var label = ctx.Style.Fonts.SectionLabel;
        var count = new Dictionary<string, int>();
        var numbered = new HashSet<double>();
        var unnumbered = st.UnnumberedPrograms.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var bottom = 1 - st.SlabHeightRatio - st.BarTopGapRatio - st.BarHeightRatio;
        foreach (var b in g.Bars.OrderBy(b => b.Z0).ThenBy(b => Math.Min(map.X(b.U0), map.X(b.U1))))
        {
            var h = b.Story;
            var x0 = Math.Min(map.X(b.U0), map.X(b.U1));
            var x1 = Math.Max(map.X(b.U0), map.X(b.U1));
            // A kept tall space runs from its base floor to the same clearance under the slab at its top.
            var yBot = map.Y(b.Z0 + bottom * h);
            var yTop = map.Y(b.Z1 - (1 - bottom - st.BarHeightRatio) * h);
            var w = x1 - x0;
            var barH = yBot - yTop;
            if (w < 1 || barH < 1) continue;

            var id = ctx.Names.TryGetValue(b.MassId, out var row) ? row.StableId : b.MassId[..Math.Min(8, b.MassId.Length)];
            var n = count[id] = count.GetValueOrDefault(id) + 1;
            var name = prefix + id + (n > 1 ? $"_{n}" : "");
            var (fill, stroke, strokePt, dash) = ctx.ProgramPaint(d, b.ProgramKey);
            d.Layer("PD_PROGRAM").Items.Add(new DItem
            {
                Kind = ItemKind.RoundRect, Name = name, X = x0, Y = yTop, W = w, H = barH,
                R = Math.Min(st.BarCornerRatio * h * map.K, Math.Min(w, barH) / 2), Fill = fill, Stroke = stroke, StrokePt = strokePt, Dash = dash,
            });

            // One floor number per level, in the first (leftmost) bar that is not an edge zone.
            var cy = yTop + barH / 2;
            var oneFloorH = Math.Min(barH, st.BarHeightRatio * h * map.K);
            var fsz = Math.Min(st.FloorNumberSizeMax, st.FloorNumberSizeRatio * oneFloorH);
            var level = ctx.Result.Levels.FirstOrDefault(l => Math.Abs(l.Elevation - b.Z0) < 0.6) ?? ctx.Result.Levels.LastOrDefault(l => l.Elevation <= b.Z0 + 0.6);
            var withNumber = level != null && fsz >= st.LabelSizeMin * 0.6 && !unnumbered.Contains(b.ProgramKey) && numbered.Add(level.Elevation);
            if (withNumber)
            {
                var num = FloorNumber(level!.Name);
                var t = SectionDrawing.Text(name + "_NUM", x0 + st.FloorNumberInsetPt, map.Y(b.Z0 + bottom * h) - oneFloorH / 2, num, "PD Floor Number", "left");
                t.GlyphAnchor = "left";
                t.SizePt = fsz;
                d.Layer("PD_LABELS").Items.Add(t);
            }

            var text = row?.Label ?? ctx.ProgramLabel(b.ProgramKey);
            if (label.AllCaps) text = text.ToUpperInvariant();
            var room = w - st.LabelInsetPt - (withNumber ? st.FloorNumberInsetPt + 1.4 * fsz + 8 : st.LabelInsetPt);
            var size = Math.Min(Math.Min(st.LabelSizeMax, st.LabelSizeRatio * oneFloorH), room / Math.Max(1, text.Length * 0.62));
            if (size < st.LabelSizeMin) continue;
            var lbl = SectionDrawing.Text(name + "_LBL", x1 - st.LabelInsetPt, cy, text, "PD Section Label", "right");
            lbl.GlyphAnchor = "right";
            lbl.SizePt = Math.Round(size, 2);
            d.Layer("PD_LABELS").Items.Add(lbl);
        }
    }

    /// <summary>"L3" → "03", "L12" → "12"; other names (B1, ROOF) as they are.</summary>
    public static string FloorNumber(string level)
    {
        var m = Regex.Match(level, "^L(\\d+)$", RegexOptions.IgnoreCase);
        return m.Success ? int.Parse(m.Groups[1].Value).ToString("00") : level;
    }
}
