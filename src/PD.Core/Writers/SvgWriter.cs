using System.Security;
using System.Text;
using PD.Core.Drawing;
using static PD.Core.Drawing.Drawing;

namespace PD.Core.Writers;

/// <summary>SVG preview of one artboard (same geometry the JSX draws in Illustrator).</summary>
public static class SvgWriter
{
    public static string Write(PD.Core.Drawing.Drawing d, int artboard = 0)
    {
        var ab = d.Artboards[artboard];
        var hex = d.Swatches.ToDictionary(s => s.Name, s => s.Hex);
        string Paint(string? swatch) => swatch != null && hex.TryGetValue(swatch, out var h) ? h : "none";
        var styles = d.ParaStyles.ToDictionary(p => p.Name);
        var sb = new StringBuilder();
        sb.Append($"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{F(ab.W)}\" height=\"{F(ab.H)}\" viewBox=\"0 0 {F(ab.W)} {F(ab.H)}\">\n");
        sb.Append($"<title>{Esc(ab.Name)}</title>\n<rect width=\"100%\" height=\"100%\" fill=\"#FFFFFF\"/>\n");
        var used = d.Layers.SelectMany(l => l.Items).Where(i => i.Artboard == artboard && i.Kind == ItemKind.Symbol).Select(i => i.Symbol).ToHashSet();
        if (used.Count > 0)
        {
            sb.Append("<defs>\n");
            foreach (var sym in d.Symbols.Where(x => used.Contains(x.Name)))
            {
                sb.Append($"<g id=\"{Esc(SymbolId(sym.Name))}\" fill=\"{Paint(sym.Fill)}\">");
                foreach (var part in sym.Parts)
                    sb.Append($"<path fill-rule=\"evenodd\" d=\"{PathData(part, true)}\"/>");
                sb.Append("</g>\n");
            }
            sb.Append("</defs>\n");
        }
        foreach (var layer in d.Layers)
        {
            sb.Append($"<g id=\"{Esc(layer.Name)}\">\n");
            foreach (var it in layer.Items.Where(i => i.Artboard == artboard))
            {
                var common = $"id=\"{Esc(it.Name)}\" fill=\"{Paint(it.Fill)}\" stroke=\"{Paint(it.Stroke)}\" stroke-width=\"{F(it.StrokePt)}\""
                    + (it.Dash is { Length: > 0 } ? $" stroke-dasharray=\"{string.Join(" ", it.Dash.Select(F))}\"" : "")
                    + (it.Opacity < 1 ? $" opacity=\"{F(it.Opacity)}\"" : "");
                switch (it.Kind)
                {
                    case ItemKind.Path when it.Paths is { Count: > 0 }:
                        sb.Append($"<path {common} fill-rule=\"evenodd\" d=\"{PathData(it.Paths, it.Closed)}\"/>\n");
                        break;
                    case ItemKind.Symbol when it.Symbol != null && it.Matrix is { Length: 4 } m:
                        sb.Append($"<use id=\"{Esc(it.Name)}\" href=\"#{Esc(SymbolId(it.Symbol))}\" transform=\"matrix({F(m[0])} {F(m[1])} {F(m[2])} {F(m[3])} {F(it.X)} {F(it.Y)})\""
                            + (it.Opacity < 1 ? $" opacity=\"{F(it.Opacity)}\"" : "") + "/>\n");
                        break;
                    case ItemKind.RoundRect:
                        sb.Append($"<rect {common} x=\"{F(it.X)}\" y=\"{F(it.Y)}\" width=\"{F(it.W)}\" height=\"{F(it.H)}\" rx=\"{F(it.R)}\"/>\n");
                        break;
                    case ItemKind.Text when it.Text != null:
                        sb.Append(Text(it, styles.GetValueOrDefault(it.ParaStyle ?? ""), Paint));
                        break;
                }
            }
            sb.Append("</g>\n");
        }
        sb.Append("</svg>\n");
        return sb.ToString();
    }

    static string PathData(IEnumerable<double[][]> rings, bool closed) => string.Join(" ", rings.Where(p => p.Length > 0)
        .Select(p => "M" + string.Join(" L", p.Select(q => $"{F(q[0])},{F(q[1])}")) + (closed ? " Z" : "")));

    static string SymbolId(string name) => "sym_" + name;

    static string Text(DItem it, ParaStyle? ps, Func<string?, string> paint)
    {
        var size = it.SizePt ?? ps?.SizePt ?? 7;
        var align = it.GlyphAnchor ?? it.Align;
        var anchor = align switch { "center" => "middle", "right" => "end", _ => "start" };
        var opacity = it.Opacity < 1 ? it.Opacity : ps?.Opacity ?? 1;
        var extra = (opacity < 1 ? $" fill-opacity=\"{F(opacity)}\"" : "") + (ps is { Tracking: not 0 } ? $" letter-spacing=\"{F(ps.Tracking / 1000 * size)}\"" : "");
        var text = ps?.AllCaps == true ? it.Text!.ToUpperInvariant() : it.Text!;
        var lines = text.Split('\n');
        // Plane text draws in its own space at the origin; the matrix carries it onto the page.
        var m = it.Matrix is { Length: 4 } mm ? mm : null;
        double ax = m == null ? it.X : 0, ay = m == null ? it.Y : 0;
        var rot = m != null ? $" transform=\"matrix({F(m[0])} {F(m[1])} {F(m[2])} {F(m[3])} {F(it.X)} {F(it.Y)})\""
            : it.Rotation != 0 ? $" transform=\"rotate({F(-it.Rotation)} {F(it.X)} {F(it.Y)})\"" : "";
        // Centred labels: (X,Y) is the visual centre; cap height ≈ 0.7 × size.
        var y = it.Center || it.GlyphAnchor != null ? ay - (lines.Length - 1) * size * 1.2 / 2 + size * 0.35 : ay;
        var sb = new StringBuilder($"<text id=\"{Esc(it.Name)}\" x=\"{F(ax)}\" y=\"{F(y)}\" font-family=\"{Esc(ps?.Family ?? "Futura PT")}, Futura, Arial, sans-serif\" " +
            $"font-weight=\"{ps?.Weight ?? "normal"}\" font-size=\"{F(size)}\" text-anchor=\"{anchor}\" fill=\"{paint(ps?.Color)}\"{extra}{rot}>");
        for (var i = 0; i < lines.Length; i++)
            sb.Append($"<tspan x=\"{F(ax)}\" dy=\"{(i == 0 ? "0" : F(size * 1.2))}\">{Esc(lines[i])}</tspan>");
        return sb.Append("</text>\n").ToString();
    }

    static string Esc(string s) => SecurityElement.Escape(s) ?? "";
}
