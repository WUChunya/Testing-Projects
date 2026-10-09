using System.Globalization;
using System.Text.Json.Serialization;

namespace PD.Core.Drawing;

public enum ItemKind { Path, RoundRect, Text }

/// <summary>
/// One drawn element. Coordinates are points on the artboard, origin top-left, y down.
/// Fill/Stroke name a swatch in <see cref="Drawing.Swatches"/>; null means none.
/// </summary>
public sealed class DItem
{
    public ItemKind Kind { get; set; }
    /// <summary>Stable object name used for in-place update (e.g. SEC_A_L03_LAB_01).</summary>
    public string Name { get; set; } = "";
    public int Artboard { get; set; }
    public string? Fill { get; set; }
    public string? Stroke { get; set; }
    public double StrokePt { get; set; }
    public double[]? Dash { get; set; }
    /// <summary>0–1.</summary>
    public double Opacity { get; set; } = 1;

    /// <summary>Path: rings or polylines [[x,y],…]; several rings form a compound path (holes).</summary>
    public List<double[][]>? Paths { get; set; }
    public bool Closed { get; set; } = true;

    /// <summary>RoundRect: top-left, size, corner radius.</summary>
    public double X { get; set; }
    public double Y { get; set; }
    public double W { get; set; }
    public double H { get; set; }
    public double R { get; set; }

    /// <summary>Text: anchor (X,Y) is the baseline point; lines separated by \n.</summary>
    public string? Text { get; set; }
    public string? ParaStyle { get; set; }
    /// <summary>left | center | right.</summary>
    public string Align { get; set; } = "left";
    /// <summary>Degrees counter-clockwise.</summary>
    public double Rotation { get; set; }
    /// <summary>Text: (X,Y) is the visual centre of the glyphs (Illustrator measures outlined glyphs and moves the text there).</summary>
    public bool Center { get; set; }
}

public sealed class DLayer
{
    public string Name { get; set; } = "";
    public List<DItem> Items { get; set; } = new();
}

public sealed record Artboard(string Name, double W, double H);

public sealed record Swatch(string Name, string Hex);

/// <param name="Font">PostScript name for Illustrator.</param>
/// <param name="Family">CSS family for the SVG preview.</param>
/// <param name="Color">Swatch name.</param>
/// <param name="Weight">CSS weight for the SVG preview.</param>
public sealed record ParaStyle(string Name, string Font, string FallbackFont, string Family, double SizePt, bool AllCaps, string Align, string Color,
    string Weight = "normal");

/// <summary>A whole .ai file: artboards, layers (bottom → top), global swatches and paragraph styles.</summary>
public sealed class Drawing
{
    /// <summary>Output file name without folder, e.g. 02_Section_A.ai.</summary>
    public string FileName { get; set; } = "";
    public List<Artboard> Artboards { get; set; } = new();
    public List<DLayer> Layers { get; set; } = new();
    public List<Swatch> Swatches { get; set; } = new();
    public List<ParaStyle> ParaStyles { get; set; } = new();
    /// <summary>Layers the script must never clear (PRD 5.0); created empty if missing.</summary>
    public List<string> ProtectedLayers { get; set; } = new() { "_MANUAL", "OVERLAY" };
    [JsonIgnore] public List<string> Notes { get; } = new();

    public DLayer Layer(string name)
    {
        var l = Layers.FirstOrDefault(x => x.Name == name);
        if (l == null) Layers.Add(l = new DLayer { Name = name });
        return l;
    }

    /// <summary>Registers a global swatch and returns its name; null hex → null (no paint).</summary>
    public string? Swatch(string name, string? hex)
    {
        if (string.IsNullOrEmpty(hex)) return null;
        hex = hex.ToUpperInvariant();
        if (Swatches.All(s => s.Name != name)) Swatches.Add(new Swatch(name, hex));
        return name;
    }

    /// <summary>Swatch named after its colour, for neutral greys and annotation colours.</summary>
    public string? Gray(string? hex) => hex == null ? null : Swatch("PD_" + hex.TrimStart('#').ToUpperInvariant(), hex);

    public static string F(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);
}
