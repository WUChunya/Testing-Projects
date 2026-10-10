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
    public PlanStyle Plan { get; set; } = new();
    public AxonStyle Axon { get; set; } = new();

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
    /// <summary>Level names next to axons and plan titles ("Level 3", not all caps).</summary>
    public Font Level { get; set; } = new();
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
    /// <summary>Labels on fills darker than this luma (0–255) are white.</summary>
    public double LabelLightBelowLuma { get; set; } = 150;
    public string LightLabelColor { get; set; } = "#FFFFFF";
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

/// <summary>PRD 5.2 plans (sizes in points at the text reference scale unless named Ft).</summary>
public sealed class PlanStyle
{
    public string OutlineStroke { get; set; } = "";
    public double OutlinePt { get; set; }
    public string RoomStroke { get; set; } = "";
    public double RoomPt { get; set; }
    /// <summary>Open-to-below (upper levels of a tall space): dashed outline with a diagonal cross.</summary>
    public double[]? VoidDash { get; set; }
    public string VoidStroke { get; set; } = "";
    public double VoidPt { get; set; }
    public string OpenToBelowLabel { get; set; } = "";
    /// <summary>Glass line outside the building outline (A3).</summary>
    public string FacadeStroke { get; set; } = "";
    public double FacadePt { get; set; }
    public double FacadeOffsetPt { get; set; }
    /// <summary>Background drawn around the building (site, other buildings), clipped to this margin.</summary>
    public double ContextMarginFt { get; set; }
    public Paint ContextBuilding { get; set; } = new();
    public string LevelTitleFormat { get; set; } = "";
    public int LegendColumns { get; set; }
    public double LegendSwatchPt { get; set; }
    public double LegendColumnPt { get; set; }
    public double LegendRowPt { get; set; }
    public List<double> ScaleBarChoicesFt { get; set; } = new();
    public double ScaleBarHeightPt { get; set; }
    public double NorthArrowPt { get; set; }
    /// <summary>Space between the drawing and the title / legend bands.</summary>
    public double BandPt { get; set; }
    public int OverviewColumns { get; set; }
    public double OverviewGapPt { get; set; }
    public string LeaderStroke { get; set; } = "";
    public double LeaderPt { get; set; }
    public double LeaderLengthPt { get; set; }
    /// <summary>Small square before the level title (reference: Atlanta level labels).</summary>
    public string TitleMarker { get; set; } = "";
    public double TitleMarkerPt { get; set; }
}

/// <summary>PRD 5.4 axons (stacked, exploded, floor keys), styled after the Atlanta exploded axon.</summary>
public sealed class AxonStyle
{
    /// <summary>Direction the viewer stands in (degrees counter-clockwise from +X): 225 = south-west.</summary>
    public double AzimuthDeg { get; set; }
    /// <summary>35.264 = true isometric.</summary>
    public double ElevationDeg { get; set; }
    /// <summary>Outline of our building (stacked) or of each plate (exploded).</summary>
    public string SilhouetteStroke { get; set; } = "";
    public double SilhouettePt { get; set; }
    public string EdgeStroke { get; set; } = "";
    public double EdgePt { get; set; }
    /// <summary>Side faces darken the program colour: [lit side, shaded side].</summary>
    public double[] SideDarken { get; set; } = { 0.12, 0.24 };
    public string ContextFill { get; set; } = "";
    public string ContextStroke { get; set; } = "";
    public double ContextPt { get; set; }
    public double ContextMarginFt { get; set; }
    /// <summary>Other buildings as low white blocks of this height (0 = real height).</summary>
    public double ContextHeightFt { get; set; }
    public string SiteFill { get; set; } = "";
    public string? SiteStroke { get; set; }
    public double SiteMarginFt { get; set; }
    public bool FacadeVeil { get; set; }
    public string FacadeVeilFill { get; set; } = "";
    public string FacadeVeilStroke { get; set; } = "";
    public double FacadeVeilStrokePt { get; set; }
    public double LabelSizePt { get; set; }
    public double SlabThicknessFt { get; set; }
    /// <summary>Exploded: clear page space between plates (none hides another).</summary>
    public double ExplodeGapPt { get; set; }
    /// <summary>Programs drawn as vertical connectors between exploded plates (cores).</summary>
    public List<string> ConnectorPrograms { get; set; } = new();
    public string ConnectorFill { get; set; } = "";
    public double ConnectorOpacity { get; set; }
    public string ConnectorStroke { get; set; } = "";
    public double ConnectorPt { get; set; }
    /// <summary>Floor keys: the building volume as dashed hidden-line edges.</summary>
    public string GhostStroke { get; set; } = "";
    public double GhostPt { get; set; }
    public double[]? GhostDash { get; set; }
    public double LevelColumnPt { get; set; }
    public string LeaderStroke { get; set; } = "";
    public double LeaderPt { get; set; }
    public string LevelMarker { get; set; } = "";
    public double LevelMarkerPt { get; set; }
    public string CoreLabel { get; set; } = "";
    public string TitleStacked { get; set; } = "";
    public string TitleExploded { get; set; } = "";
    public string TitleLevels { get; set; } = "";
}
