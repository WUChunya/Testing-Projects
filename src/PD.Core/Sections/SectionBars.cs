using System.Text.RegularExpressions;
using PD.Core.Drawing;

namespace PD.Core.Sections;

/// <summary>
/// Program section bars (docs/SITE_STYLE.md §4): rounded bars 0.68 × typical floor tall (corner 0.14 × floor, no stroke),
/// centred between the slabs; label right-aligned (Regular caps ≤ 20 pt = 0.7 × bar, one size per drawing, shrunk only
/// when too long, dropped under 10 pt); floor number ("01") left in the first bar of each level.
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
        // One diagram geometry for the whole drawing: slabs st.SlabHeightRatio × typical floor thick, centred on the level
        // line; each bar is centred between the top of its slab and the underside of the next one with an even clearance,
        // so a bar sits in the middle of its floor whatever the floor height.
        var typical = g.TypicalStory;
        var halfSlab = st.SlabHeightRatio * typical / 2;
        var clear = Math.Max(0, (1 - st.SlabHeightRatio - st.BarHeightRatio) / 2 * typical);
        // One text size for every label and floor number of the drawing (shrunk per bar only when the name is too long).
        var barRef = st.BarHeightRatio * typical * map.K;
        var labelSize = Math.Min(st.LabelSizeMax, st.LabelSizeRatio * barRef);
        var numberSize = Math.Min(st.FloorNumberSizeMax, st.FloorNumberSizeRatio * barRef);
        foreach (var b in g.Bars.OrderBy(b => b.Z0).ThenBy(b => Math.Min(map.X(b.U0), map.X(b.U1))))
        {
            var x0 = Math.Min(map.X(b.U0), map.X(b.U1));
            var x1 = Math.Max(map.X(b.U0), map.X(b.U1));
            var yBot = map.Y(b.Z0 + halfSlab + clear);
            var yTop = map.Y(b.Z1 - halfSlab - clear);
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
                R = Math.Min(st.BarCornerRatio * typical * map.K, Math.Min(w, barH) / 2), Fill = fill, Stroke = stroke, StrokePt = strokePt, Dash = dash,
            });

            // Text sits on the middle of the bar's first floor (the whole bar for a one-floor bar).
            var firstTop = Math.Min(b.Z1, b.Z0 + b.Story);
            var cy = (yBot + map.Y(firstTop - halfSlab - clear)) / 2;
            var level = ctx.Result.Levels.FirstOrDefault(l => Math.Abs(l.Elevation - b.Z0) < 0.6) ?? ctx.Result.Levels.LastOrDefault(l => l.Elevation <= b.Z0 + 0.6);
            var withNumber = level != null && numberSize >= st.LabelSizeMin * 0.6 && !unnumbered.Contains(b.ProgramKey) && numbered.Add(level.Elevation);
            if (withNumber)
            {
                var t = SectionDrawing.Text(name + "_NUM", x0 + st.FloorNumberInsetPt, cy, FloorNumber(level!.Name), "PD Floor Number", "left");
                t.GlyphAnchor = "left";
                t.SizePt = Math.Round(numberSize, 2);
                d.Layer("PD_LABELS").Items.Add(t);
            }

            var text = row?.Label ?? ctx.ProgramLabel(b.ProgramKey);
            if (label.AllCaps) text = text.ToUpperInvariant();
            var room = w - st.LabelInsetPt - (withNumber ? st.FloorNumberInsetPt + 1.4 * numberSize + 8 : st.LabelInsetPt);
            var size = Math.Min(labelSize, room / Math.Max(1, text.Length * 0.62));
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
