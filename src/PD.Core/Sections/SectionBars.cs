using System.Text.RegularExpressions;
using PD.Core.Drawing;

namespace PD.Core.Sections;

/// <summary>
/// Program section bars (docs/SITE_STYLE.md §4): rounded bars 0.68 × typical floor tall (corner 0.14 × floor, no stroke),
/// centred between the slabs; label in the exact centre of every block (one size per drawing; user rev 0.8: a block too
/// narrow for its name shows the first three letters, or nothing); floor number ("01") left in the first bar of each level.
/// </summary>
public static class SectionBars
{
    /// <param name="skip">Bars drawn elsewhere (the penthouse block).</param>
    /// <returns>Page boxes (x0, x1, y0, y1) of the labels and floor numbers, kept clear of people.</returns>
    public static List<(double X0, double X1, double Y0, double Y1)> Add(PD.Core.Drawing.Drawing d, ExportContext ctx, SectionGeometry g, PageMap map, string prefix,
        ICollection<CutBar>? skip = null)
    {
        var boxes = new List<(double, double, double, double)>();
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
        foreach (var b in g.Bars.Where(b => skip == null || !skip.Contains(b)).OrderBy(b => b.Z0).ThenBy(b => Math.Min(map.X(b.U0), map.X(b.U1))))
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
            if (withNumber) boxes.Add((x0 + st.FloorNumberInsetPt, x0 + st.FloorNumberInsetPt + 1.4 * numberSize, cy - numberSize / 2, cy + numberSize / 2));
            // Centred in the block, clear of the floor number on the left (mirrored, so the text stays centred).
            var side = withNumber ? st.FloorNumberInsetPt + 1.4 * numberSize + 6 : st.LabelInsetPt;
            if (Fit(text, labelSize, w, side) is not { } shown) continue;
            var cx = (x0 + x1) / 2;
            var lbl = SectionDrawing.Text(name + "_LBL", cx, cy, shown, "PD Section Label", "center", center: true);
            lbl.SizePt = Math.Round(labelSize, 2);
            d.Layer("PD_LABELS").Items.Add(lbl);
            var half = TextWidth(shown, labelSize) / 2;
            boxes.Add((cx - half, cx + half, cy - labelSize / 2, cy + labelSize / 2));
        }
        return boxes;
    }

    /// <summary>Caps width estimate (Futura PT / Arial capitals): narrow I, punctuation and spaces, wide M / W.</summary>
    public static double TextWidth(string text, double size) => size * text.Sum(ch => ch switch
    {
        ' ' => 0.28,
        '/' or '.' or ',' or '-' or '\'' or '|' => 0.32,
        'I' or 'J' or '1' => 0.3,
        'M' or 'W' => 0.85,
        _ => 0.64,
    });

    /// <summary>The name if it fits centred in a block <paramref name="w"/> wide with <paramref name="side"/> kept free on both sides, else its first three letters, else null.</summary>
    public static string? Fit(string text, double size, double w, double side)
    {
        var room = w - 2 * side;
        if (TextWidth(text, size) <= room) return text;
        var letters = new string(text.Where(char.IsLetterOrDigit).Take(3).ToArray());
        return letters.Length > 0 && TextWidth(letters, size) <= room ? letters : null;
    }

    /// <summary>"L3" → "03", "L12" → "12"; other names (B1, ROOF) as they are.</summary>
    public static string FloorNumber(string level)
    {
        var m = Regex.Match(level, "^L(\\d+)$", RegexOptions.IgnoreCase);
        return m.Success ? int.Parse(m.Groups[1].Value).ToString("00") : level;
    }
}
