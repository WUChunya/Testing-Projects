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
                        var dd = string.Join(" ", it.Paths.Where(p => p.Length > 0)
                            .Select(p => "M" + string.Join(" L", p.Select(q => $"{F(q[0])},{F(q[1])}")) + (it.Closed ? " Z" : "")));
                        sb.Append($"<path {common} fill-rule=\"evenodd\" d=\"{dd}\"/>\n");
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

    static string Text(DItem it, ParaStyle? ps, Func<string?, string> paint)
    {
        var size = ps?.SizePt ?? 7;
        var anchor = it.Align switch { "center" => "middle", "right" => "end", _ => "start" };
        var text = ps?.AllCaps == true ? it.Text!.ToUpperInvariant() : it.Text!;
        var rot = it.Rotation != 0 ? $" transform=\"rotate({F(-it.Rotation)} {F(it.X)} {F(it.Y)})\"" : "";
        var sb = new StringBuilder($"<text id=\"{Esc(it.Name)}\" x=\"{F(it.X)}\" y=\"{F(it.Y)}\" font-family=\"{Esc(ps?.Family ?? "Arial")}, Arial, sans-serif\" " +
            $"font-size=\"{F(size)}\" text-anchor=\"{anchor}\" fill=\"{paint(ps?.Color)}\"{rot}>");
        var lines = text.Split('\n');
        for (var i = 0; i < lines.Length; i++)
            sb.Append($"<tspan x=\"{F(it.X)}\" dy=\"{(i == 0 ? "0" : F(size * 1.2))}\">{Esc(lines[i])}</tspan>");
        return sb.Append("</text>\n").ToString();
    }

    static string Esc(string s) => SecurityElement.Escape(s) ?? "";
}
