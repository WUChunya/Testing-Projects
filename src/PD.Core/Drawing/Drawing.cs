using System.Globalization;
using System.Text.Json.Serialization;

namespace PD.Core.Drawing;

public enum ItemKind { Path, RoundRect, Text, Symbol }

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
    /// <summary>
    /// Text laid on a plane (axon labels): [a, b, c, d] maps text space (x right, y down, points) to the page (y down):
    /// page = (X, Y) + (a·x + c·y, b·x + d·y). Replaces <see cref="Rotation"/>; the text stays editable.
    /// </summary>
    public double[]? Matrix { get; set; }
    /// <summary>Text size override (pt), e.g. section labels sized to their bar.</summary>
    public double? SizePt { get; set; }
    /// <summary>
    /// Text placed by its glyph box: "right" puts the glyphs' right edge at X, "left" their left edge, both centred
    /// vertically on Y (section bar labels and floor numbers). Null = <see cref="Center"/> / baseline placement.
    /// </summary>
    public string? GlyphAnchor { get; set; }

    /// <summary>
    /// Symbol: instance of <see cref="Drawing.Symbols"/> by name. (X, Y) is where the symbol origin (feet / trunk foot)
    /// lands and <see cref="Matrix"/> [a, b, c, d] maps symbol space (pt, y down) to the page (scale, mirror).
    /// </summary>
    public string? Symbol { get; set; }
}

/// <summary>
/// Symbol art (entourage people and trees): parts are compound paths in symbol space (pt, y down, origin at the
/// foot), all filled with <see cref="Fill"/>. Illustrator keeps a same-named symbol already in the file, so the office
/// can replace the art once and every re-export reuses it.
/// </summary>
public sealed class DSymbol
{
    public string Name { get; set; } = "";
    public string? Fill { get; set; }
    public List<List<double[][]>> Parts { get; set; } = new();
    /// <summary>[minX, minY, maxX, maxY] in symbol space.</summary>
    public double[] Bounds { get; set; } = new double[4];
}

public sealed class DLayer
{
    public string Name { get; set; } = "";
    public List<DItem> Items { get; set; } = new();
}

/// <param name="X">Left of the artboard in the file's artboard layout (points, y down); set by <see cref="Drawing.LayoutArtboards"/>.</param>
public sealed record Artboard(string Name, double W, double H)
{
    public double X { get; set; }
    public double Y { get; set; }
}

public sealed record Swatch(string Name, string Hex);

/// <param name="Font">PostScript name for Illustrator.</param>
/// <param name="Family">CSS family for the SVG preview.</param>
/// <param name="Color">Swatch name.</param>
/// <param name="Weight">CSS weight for the SVG preview.</param>
/// <param name="Opacity">Text opacity 0–1 (applied to each text item).</param>
/// <param name="Tracking">Illustrator tracking (1/1000 em).</param>
/// <param name="LeadingPt">0 = auto.</param>
public sealed record ParaStyle(string Name, string Font, string FallbackFont, string Family, double SizePt, bool AllCaps, string Align, string Color,
    string Weight = "normal", double Opacity = 1, double Tracking = 0, double LeadingPt = 0);

/// <summary>A whole .ai file: artboards, layers (bottom → top), global swatches and paragraph styles.</summary>
public sealed class Drawing
{
    /// <summary>Output file name without folder, e.g. 02_Section_A.ai.</summary>
    public string FileName { get; set; } = "";
    public List<Artboard> Artboards { get; set; } = new();
    public List<DLayer> Layers { get; set; } = new();
    public List<Swatch> Swatches { get; set; } = new();
    public List<ParaStyle> ParaStyles { get; set; } = new();
    public List<DSymbol> Symbols { get; set; } = new();
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
        name = SwatchName(name);
        if (Swatches.All(s => s.Name != name)) Swatches.Add(new Swatch(name, hex));
        return name;
    }

    /// <summary>Longest swatch name we hand to Illustrator. Longer names are cut there, and two cut names can
    /// collide (".._SIDE1_6_14" vs ".._SIDE1_6_14_FLOOR"), which stopped a whole file with "the name was not found".</summary>
    public const int MaxSwatchName = 31;

    /// <summary>The name unchanged when short enough, else a stable 31-character form: head + "_" + 6-hex hash.</summary>
    public static string SwatchName(string name)
    {
        if (name.Length <= MaxSwatchName) return name;
        uint h = 2166136261;
        foreach (var ch in name) h = unchecked((h ^ ch) * 16777619);
        var tail = "_" + (h & 0xFFFFFF).ToString("X6");
        return name[..(MaxSwatchName - tail.Length)] + tail;
    }

    /// <summary>Swatch named after its colour, for neutral greys and annotation colours.</summary>
    public string? Gray(string? hex) => hex == null ? null : Swatch("PD_" + hex.TrimStart('#').ToUpperInvariant(), hex);

    /// <summary>
    /// Places artboards in rows (wrapping at <paramref name="maxRowPt"/>) so a many-artboard file stays inside
    /// Illustrator's 16383 pt canvas; returns the layout size. Illustrator centres the layout on the canvas.
    /// </summary>
    public (double W, double H) LayoutArtboards(double maxRowPt = 6000, double gap = 72)
    {
        double x = 0, y = 0, rowH = 0, w = 0;
        foreach (var a in Artboards)
        {
            if (x > 0 && x + a.W > maxRowPt)
            {
                x = 0;
                y += rowH + gap;
                rowH = 0;
            }
            a.X = x;
            a.Y = y;
            x += a.W + gap;
            rowH = Math.Max(rowH, a.H);
            w = Math.Max(w, a.X + a.W);
        }
        return (w, y + rowH);
    }

    public static string F(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);
}
