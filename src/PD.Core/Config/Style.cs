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
    /// <summary>Paint per layer role and drawing family (plan / axon / view): BG.* context and NC.* new construction.</summary>
    public Dictionary<string, RolePaint> Roles { get; set; } = new();
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

/// <summary>How one role is drawn in plans, axons and projected views (sections / elevations).</summary>
public sealed class RolePaint
{
    public Paint? Plan { get; set; }
    public Paint? Axon { get; set; }
    public Paint? View { get; set; }
}

public sealed class Paint
{
    public string? Fill { get; set; }
    public double Opacity { get; set; } = 1;
    /// <summary>Hard drop shadow (plans: other buildings), offset right / down in pt.</summary>
    public string? Shadow { get; set; }
    public double[]? ShadowOffsetPt { get; set; }
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
    public string Color { get; set; } = "#000000";
    /// <summary>Text opacity 0–1 (white labels at 70 % let the block colour through).</summary>
    public double Opacity { get; set; } = 1;
    /// <summary>Illustrator tracking (1/1000 em).</summary>
    public double Tracking { get; set; }
    /// <summary>Leading in pt; 0 = auto.</summary>
    public double LeadingPt { get; set; }
}

/// <summary>
/// Unified typography (docs/SITE_STYLE.md §0.1, styles/site_style.json › typography): one family (Arial) for every
/// drawing. Sizes are pt at the text reference scale (1" = 45'-0"); sections use their own absolute sizes.
/// </summary>
public sealed class Fonts
{
    /// <summary>Room names in plans (Regular 19, #333333).</summary>
    public Font Label { get; set; } = new();
    /// <summary>Drawing title tag (Regular 31, #333333).</summary>
    public Font Title { get; set; } = new();
    /// <summary>Legend and small notes (Regular 11, #444444).</summary>
    public Font Note { get; set; } = new();
    public Font Chart { get; set; } = new();
    /// <summary>Exploded axon A level names ("Level 7", Regular 14.8).</summary>
    public Font Level { get; set; } = new();
    /// <summary>Exploded axon B level tags ("LEVEL 6", Bold 17).</summary>
    public Font LevelTag { get; set; } = new();
    /// <summary>Program labels on axon tops: A 20 / 16 pt white 70 %, B 26 / 13 pt white 90 %.</summary>
    public Font PlaneLabel { get; set; } = new();
    public Font PlaneLabelSmall { get; set; } = new();
    public Font PlaneLabelB { get; set; } = new();
    public Font PlaneLabelBSmall { get; set; } = new();
    public Font CoreLabel { get; set; } = new();
    /// <summary>Building / place names (Bold 24, #6C6C6C).</summary>
    public Font ContextLabel { get; set; } = new();
    /// <summary>Street names (Regular 22, tracking 200, #5A5A5A).</summary>
    public Font Street { get; set; } = new();
    /// <summary>Callout headings (Bold 19, tracking 100, #8C8C8C).</summary>
    public Font Heading { get; set; } = new();
    /// <summary>Section bar labels (Regular, ≤ 20 pt, #333333 85 %) and floor numbers (Regular, ≤ 16 pt, #1A1A1A).</summary>
    public Font SectionLabel { get; set; } = new();
    public Font FloorNumber { get; set; } = new();
}

public sealed class FacadeStyle
{
    public double StackedAxonOpacity { get; set; }
    public string ExplodedAxon { get; set; } = "";
}

/// <summary>Program section (docs/SITE_STYLE.md §4, styles/site_style.json › program_section). Ratios are × floor height.</summary>
public sealed class SectionStyle
{
    public double BarHeightRatio { get; set; }
    /// <summary>Gap between the bar top and the slab above.</summary>
    public double BarTopGapRatio { get; set; }
    public double BarCornerRatio { get; set; }
    /// <summary>Fill behind the cut colour blocks (hides what is seen beyond through the gaps); null/empty = none.</summary>
    public string? ProgramBackground { get; set; }
    public double SlabHeightRatio { get; set; }
    public string SlabFill { get; set; } = "";
    public string SlabStroke { get; set; } = "";
    public double SlabStrokePt { get; set; }
    public double SlabOverhangFt { get; set; }
    public double LabelSizeMax { get; set; }
    public double LabelSizeRatio { get; set; }
    public double LabelSizeMin { get; set; }
    public double LabelInsetPt { get; set; }
    public double FloorNumberSizeMax { get; set; }
    public double FloorNumberSizeRatio { get; set; }
    public double FloorNumberInsetPt { get; set; }
    /// <summary>Programs that never carry the floor number (edge zones, bridges).</summary>
    public List<string> UnnumberedPrograms { get; set; } = new();
    public string EnvelopeStroke { get; set; } = "";
    public double EnvelopePt { get; set; }
    public string LevelLineStroke { get; set; } = "";
    public double LevelLineStrokePt { get; set; }
    public string GroundStroke { get; set; } = "";
    public double GroundPt { get; set; }
    public string PropertyStroke { get; set; } = "";
    public double PropertyPt { get; set; }
    public double[]? PropertyDash { get; set; }
    /// <summary>Other buildings cut by the section.</summary>
    public string ContextCutStroke { get; set; } = "";
    public double ContextCutStrokePt { get; set; }
    public string ContextCutFill { get; set; } = "";
    public double FacadeCutPt { get; set; }
    public bool LevelColumn { get; set; }
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
    /// <summary>Room names that do not fit at the label size try this size (site plan: 19 → 13 pt).</summary>
    public double LabelSmallPt { get; set; }
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
    /// <summary>Title tag behind the level name, bottom right (site plan "Ground Floor").</summary>
    public string TitleTagFill { get; set; } = "";
    public double TitleTagPadPt { get; set; }
    /// <summary>Trees are circles of this diameter range (ft).</summary>
    public double[] TreeDiameterFt { get; set; } = { 14, 22 };
}

/// <summary>
/// PRD 5.4 axons: shared settings (docs/AXON_STYLE.md) plus one block per drawing — stacked (SITE_STYLE §1 site
/// program axon), exploded A (AXON_STYLE §7, Atlanta) and exploded B (SITE_STYLE §3, planometric).
/// </summary>
public sealed class AxonStyle
{
    /// <summary>Direction the viewer stands in (degrees counter-clockwise from +X): 225 = south-west.</summary>
    public double AzimuthDeg { get; set; }
    /// <summary>35.264 = isometric.</summary>
    public double ElevationDeg { get; set; }
    /// <summary>false = isometric drawing: every axis at true length (AXON_STYLE §2).</summary>
    public bool Foreshorten { get; set; }
    public string LineColor { get; set; } = "#000000";
    /// <summary>Line hierarchy (AXON_STYLE §3): silhouette 2.5 › plate silhouette 2.0 › fold 1.0 › division 0.5 › context 0.35.</summary>
    public double SilhouettePt { get; set; }
    public double SlabSilhouettePt { get; set; }
    public double FoldPt { get; set; }
    public double DivisionPt { get; set; }
    public string DivisionColor { get; set; } = "#FFFFFF";
    public string ContextStroke { get; set; } = "";
    public double ContextPt { get; set; }
    public string HiddenStroke { get; set; } = "";
    public double HiddenPt { get; set; }
    public double[]? HiddenDash { get; set; }
    /// <summary>Side faces darken the program colour: [lit (left) side, shaded (right) side].</summary>
    public double[] SideDarken { get; set; } = { 0.1, 0.2 };
    /// <summary>Core colour in axons (plans keep the legend's #999999).</summary>
    public string CoreFill { get; set; } = "";
    public string SiteFill { get; set; } = "";
    public string? SiteStroke { get; set; }
    public double SiteMarginFt { get; set; }
    public double ContextMarginFt { get; set; }
    /// <summary>Other buildings as blocks of this height (0 = real height).</summary>
    public double ContextHeightFt { get; set; }
    public bool FacadeVeil { get; set; }
    public string FacadeVeilFill { get; set; } = "";
    public string FacadeVeilStroke { get; set; } = "";
    public double FacadeVeilStrokePt { get; set; }
    /// <summary>Programs joined between exploded plates (cores, stairs).</summary>
    public List<string> ConnectorPrograms { get; set; } = new();
    public string ConnectorFill { get; set; } = "";
    public double ConnectorOpacity { get; set; }
    public string ConnectorStroke { get; set; } = "";
    public double ConnectorPt { get; set; }
    public string CoreLabel { get; set; } = "";
    public double LevelColumnPt { get; set; }
    public TreeStyle Trees { get; set; } = new();
    public StackedAxonStyle Stacked { get; set; } = new();
    public ExplodedAxonStyle Exploded { get; set; } = new();
    public ExplodedBAxonStyle ExplodedB { get; set; } = new();
    public string TitleStacked { get; set; } = "";
    public string TitleExploded { get; set; } = "";
    public string TitleExplodedB { get; set; } = "";
    public string TitleLevels { get; set; } = "";
}

/// <summary>Lollipop trees (SITE_STYLE §1).</summary>
public sealed class TreeStyle
{
    public List<string> Canopies { get; set; } = new();
    public double Opacity { get; set; }
    public double StrokePt { get; set; }
    public double StrokeDarken { get; set; }
    public string Trunk { get; set; } = "";
    public double TrunkPt { get; set; }
    public double CanopyWFt { get; set; }
    public double CanopyHFt { get; set; }
    public double TrunkHFt { get; set; }
}

/// <summary>Stacked axon = site program axon (SITE_STYLE §1).</summary>
public sealed class StackedAxonStyle
{
    public double SilhouettePt { get; set; }
    public double EdgePt { get; set; }
    /// <summary>Floor lines (and colour changes): this much darker than the face colour.</summary>
    public double FloorLinePt { get; set; }
    public double FloorLineDarken { get; set; }
    public double[] SideDarken { get; set; } = { 0.06, 0.14 };
    public bool Labels { get; set; }
}

/// <summary>Exploded axon A (AXON_STYLE §6–7).</summary>
public sealed class ExplodedAxonStyle
{
    public double SlabThicknessFt { get; set; }
    /// <summary>Pitch (top to top) = max(gapFactor × floor height, minTopVisible × projected top depth).</summary>
    public double GapFactor { get; set; }
    public double MinTopVisible { get; set; }
    public double LabelInsetPt { get; set; }
    /// <summary>Label length ≤ this × the edge it runs along; height ≤ 0.45 × the block's short side.</summary>
    public double LabelFitRatio { get; set; }
    public double LevelMarkerPt { get; set; }
    public string LevelMarker { get; set; } = "";
    public string LevelMarkerStroke { get; set; } = "";
    public double LevelMarkerStrokePt { get; set; }
    public double LevelMarkerGapPt { get; set; }
    public string LeaderStroke { get; set; } = "";
    public double LeaderPt { get; set; }
    /// <summary>Level column: this far right of the drawing's rightmost point.</summary>
    public double ColumnOffsetPt { get; set; }
}

/// <summary>Exploded axon B (SITE_STYLE §3): planometric 30/60, every program its own translucent block.</summary>
public sealed class ExplodedBAxonStyle
{
    public double YAxisDeg { get; set; }
    public double BlockThicknessFt { get; set; }
    public double PlateOpacity { get; set; }
    public double[] SideDarken { get; set; } = { 0.15, 0.3 };
    public string EdgeStroke { get; set; } = "";
    public double EdgePt { get; set; }
    /// <summary>Pitch = clearRatio × projected depth of the plate below (plates hardly overlap).</summary>
    public double ClearRatio { get; set; }
    public string AtriumFill { get; set; } = "";
    public double AtriumOpacity { get; set; }
    public string AtriumEnvelope { get; set; } = "";
    public double AtriumEnvelopePt { get; set; }
    public string ShaftStroke { get; set; } = "";
    public double ShaftPt { get; set; }
    public double LevelMarkerPt { get; set; }
    public string LevelMarker { get; set; } = "";
    public string LeaderStroke { get; set; } = "";
    public double LeaderPt { get; set; }
    public double LevelGapPt { get; set; }
    public string SitePlaneFill { get; set; } = "";
    public string SitePlaneStroke { get; set; } = "";
    public double SitePlaneStrokePt { get; set; }
    public double SitePlanOpacity { get; set; }
}
