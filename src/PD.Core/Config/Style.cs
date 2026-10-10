using System.Text.Json;
using PD.Core.Styles;

namespace PD.Core.Config;

/// <summary>Typed view of styles/style_default.json (the parts the drawing generators use).</summary>
public sealed class Style
{
    /// <summary>Filled by <see cref="Default"/> (the JSON mixes paints with flags).</summary>
    [System.Text.Json.Serialization.JsonIgnore] public Dictionary<string, Paint> Background { get; set; } = new();
    public Strokes Strokes { get; set; } = new();
    public Fonts Fonts { get; set; } = new();
    public FacadeStyle Facade { get; set; } = new();
    public SectionStyle Section { get; set; } = new();
    public ViewStyle View { get; set; } = new();
    public ElevationStyle Elevation { get; set; } = new();
    public PageStyle Page { get; set; } = new();

    public static Style Default()
    {
        using var doc = StyleResources.Read(StyleResources.StyleDefault);
        var root = doc.RootElement;
        var s = JsonSerializer.Deserialize<Style>(root.GetRawText(), Rules.Json)!;
        // "background" mixes paint objects with a boolean flag; read only the objects.
        s.Background = root.GetProperty("background").EnumerateObject()
            .Where(p => p.Value.ValueKind == JsonValueKind.Object)
            .ToDictionary(p => p.Name, p => p.Value.Deserialize<Paint>(Rules.Json)!);
        return s;
    }
}

public sealed class Paint
{
    public string? Fill { get; set; }
    public string? Stroke { get; set; }
    public double? StrokePt { get; set; }
    public double[]? Dash { get; set; }
}

public sealed class Strokes
{
    public double CutPt { get; set; }
    public double OutlinePt { get; set; }
    public double RoomPt { get; set; }
    public double BackgroundPt { get; set; }
    public Paint Beyond { get; set; } = new();
}

public sealed class Font
{
    public string Family { get; set; } = "";
    public string PostScript { get; set; } = "";
    public string FallbackPostScript { get; set; } = "";
    public double SizePt { get; set; }
    public bool AllCaps { get; set; }
    /// <summary>CSS weight for the SVG preview (Illustrator uses the PostScript name).</summary>
    public string Weight { get; set; } = "normal";
}

public sealed class Fonts
{
    public Font Label { get; set; } = new();
    public Font Title { get; set; } = new();
    public Font Note { get; set; } = new();
    public Font Chart { get; set; } = new();
}

public sealed class FacadeStyle
{
    public double StackedAxonOpacity { get; set; }
    public string ExplodedAxon { get; set; } = "";
}

public sealed class SectionStyle
{
    /// <summary>Slabs are drawn at a fixed visual weight (not their real thickness).</summary>
    public double SlabPt { get; set; }
    public string SlabColor { get; set; } = "";
    /// <summary>White space between a colour block and the slab above/below it.</summary>
    public double BarSlabGapPt { get; set; }
    public double BarCornerPt { get; set; }
    public double BarGapPt { get; set; }
    /// <summary>Fill behind the cut colour blocks (hides what is seen beyond through the gaps); null/empty = none.</summary>
    public string? ProgramBackground { get; set; }
    /// <summary>Exterior wall line of the cut building.</summary>
    public double OutlinePt { get; set; }
    /// <summary>Other buildings cut by the section: solid (filled) with a heavy outline.</summary>
    public string ContextCutStroke { get; set; } = "";
    public double ContextCutStrokePt { get; set; }
    public string ContextCutFill { get; set; } = "";
    /// <summary>Ground line weight = slab weight × this factor, centred on grade (basements draw like other levels).</summary>
    public double GroundSlabFactor { get; set; }
    public string GroundColor { get; set; } = "";
    public double FacadeCutPt { get; set; }
    public bool LevelColumn { get; set; }
    public string LevelLineStroke { get; set; } = "";
    public double LevelLineStrokePt { get; set; }
    public bool HeightDimension { get; set; }
    public string TitlePrefix { get; set; } = "";
}

public sealed class PageStyle
{
    public double MarginPt { get; set; }
    /// <summary>Scale at which text sizes and annotation offsets are given; they scale with the drawing scale.</summary>
    public string TextReferenceScale { get; set; } = "";
    public double LevelColumnPt { get; set; }
    public double DimColumnPt { get; set; }
    public double TitleBandPt { get; set; }
    public double TitleGapPt { get; set; }
    public string LabelColor { get; set; } = "";
    public double LabelMinBarWidthPt { get; set; }
}

/// <summary>Projected (Make2D-like) view: behind section cuts and in elevations.</summary>
public sealed class ViewStyle
{
    public string MassFill { get; set; } = "";
    public string MassStroke { get; set; } = "";
    public double MassStrokePt { get; set; }
    public string EdgeStroke { get; set; } = "";
    public double EdgeStrokePt { get; set; }
    /// <summary>Regex on the layer's own name: matching facade layers are glass (white, solid).</summary>
    public string GlassPattern { get; set; } = "";
    public string GlassFill { get; set; } = "";
    /// <summary>Greys handed out to the other element layers in name order.</summary>
    public List<string> LayerGrays { get; set; } = new();
    /// <summary>Per layer (own name) fill overrides.</summary>
    public Dictionary<string, string> LayerFills { get; set; } = new();
    public string ContextFill { get; set; } = "";
    public string ContextStroke { get; set; } = "";
    public double ContextStrokePt { get; set; }
    public double[]? ContextDash { get; set; }
}

public sealed class ElevationStyle
{
    public string TitlePrefix { get; set; } = "";
    public bool LevelMarkers { get; set; }
    public string LevelLineStroke { get; set; } = "";
    public double LevelLineStrokePt { get; set; }
    public double[]? LevelLineDash { get; set; }
    public double MarkerSizePt { get; set; }
}
