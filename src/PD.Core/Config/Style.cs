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
    /// <summary>
    /// User rev 0.9.5: our site areas (NC.Hardscape / NC.Green / NC.Water) take a material paint by keyword in their
    /// layer path or object name (pavers, gravel, timber, red walk, lawn, planting, meadow…); first match wins; drawn in
    /// list order within their role (base materials first).
    /// </summary>
    public List<SiteMaterial> SiteMaterials { get; set; } = new();
    /// <summary>User rev 0.9.5: stairs (NC.Structure on a layer named like a stair) in plans and axons.</summary>
    public StairStyle Stairs { get; set; } = new();
    public AxonStyle Axon { get; set; } = new();
    public ChartsStyle Charts { get; set; } = new();
    public EntourageStyle Entourage { get; set; } = new();
    /// <summary>Generated streetscape: sidewalks, street trees, road medians (site plan, plans, axons, sections).</summary>
    public SiteStyle Site { get; set; } = new();
    /// <summary>NC.Screen as solid walls with a floor and an open top (roof screens, user rev 0.8.3).</summary>
    public ScreenStyle Screen { get; set; } = new();
    /// <summary>Slabs / flat glass with nothing on them: roofs, terraces, canopies (user rev 0.8.4).</summary>
    public ExposedStyle Exposed { get; set; } = new();
    /// <summary>Annotation symbols and legends for analysis overlays (user rev 0.8.4).</summary>
    public AnnotationStyle Annotation { get; set; } = new();

    /// <summary>
    /// Project colours from tab 5 (user rev 0.8.4) over the style: one tree colour for plan canopies, axon lollipops and
    /// section trees; the people colour. Unset = the style's own colours. Call on a fresh style (each export).
    /// </summary>
    public void ApplyColors(PD.Core.Settings.PdSettings s)
    {
        if (!string.IsNullOrWhiteSpace(s.TreeColor))
        {
            var c = s.TreeColor.Trim();
            foreach (var role in new[] { "BG.Tree", "NC.Tree" })
                if (Roles.TryGetValue(role, out var rp))
                {
                    if (rp.Plan != null) rp.Plan.Fill = c;
                    if (rp.Axon != null) rp.Axon.Fill = c;
                }
            Axon.Trees.Canopies = new List<string> { c };
            Entourage.TreeFills = new List<string> { c };
            Annotation.TreeFill = c;
        }
        if (!string.IsNullOrWhiteSpace(s.PeopleColor)) Entourage.Color = s.PeopleColor.Trim();
    }

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
    /// <summary>Closed curves on this role's layers: "area" (filled like a surface) or "line" (kerb / edge lines).</summary>
    public string ClosedCurves { get; set; } = "area";
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
    /// <summary>Open curves of this role (river edge, kerb, path): stroke only. Missing → a darker shade of the fill.</summary>
    public Paint? Line { get; set; }
    /// <summary>Hatch drawn over the fill (green: grass ticks, water: ripples); no outline.</summary>
    public HatchStyle? Hatch { get; set; }
}

public sealed class SiteMaterial
{
    public string Key { get; set; } = "";
    public List<string> Match { get; set; } = new();
    /// <summary>Roles it applies to (empty: NC.Hardscape, NC.Green and NC.Water).</summary>
    public List<string> Roles { get; set; } = new();
    public Paint Plan { get; set; } = new();
    public Paint? Axon { get; set; }
}

public sealed class StairStyle
{
    /// <summary>Layer path keywords of NC.Structure objects drawn as stairs (each tread as seen from above).</summary>
    public List<string> LayerKeywords { get; set; } = new() { "STAIR", "TREAD" };
    public string Fill { get; set; } = "#FFFFFF";
    public string Stroke { get; set; } = "#4A4A4A";
    public double StrokePt { get; set; } = 0.5;
}

/// <summary>Parallel lines clipped to an area, at true scale.</summary>
public sealed class HatchStyle
{
    public string Stroke { get; set; } = "#9A9A9A";
    public double StrokePt { get; set; } = 0.35;
    public double SpacingFt { get; set; } = 6;
    public double AngleDeg { get; set; } = 45;
    public double[]? Dash { get; set; }
    public double Opacity { get; set; } = 1;
}

/// <summary>Annotation symbols (docs/reference site plan / site axon legends, user rev 0.8.4). Absolute pt.</summary>
public sealed class AnnotationStyle
{
    public double IconPt { get; set; } = 18;
    public double LegendRowPt { get; set; } = 30;
    public string EntryFill { get; set; } = "#18427E";
    public string ServiceFill { get; set; } = "#6C6C6C";
    public string ViewMarkerFill { get; set; } = "#D8AE78";
    public string CarStroke { get; set; } = "#6C6C6C";
    public string PedFlowStroke { get; set; } = "#E47200";
    public double[] PedFlowDash { get; set; } = { 6, 4.5 };
    public string CarFlowStroke { get; set; } = "#6C6C6C";
    public double[] CarFlowDash { get; set; } = { 10, 5 };
    public double FlowPt { get; set; } = 4;
    public string GatewayStroke { get; set; } = "#183C6C";
    public double GatewayPt { get; set; } = 2;
    public double[] GatewayDash { get; set; } = { 12, 8 };
    public string TransitFill { get; set; } = "#2F6DB5";
    public string TreeFill { get; set; } = "#90C696";
    public string TagStroke { get; set; } = "#333333";
    /// <summary>A "LEGEND" artboard in every axon file and in the site plan.</summary>
    public bool LegendArtboards { get; set; } = true;
    /// <summary>Axon title: a north arrow along the projected north, this long.</summary>
    public double AxonNorthPt { get; set; } = 26;
}

/// <summary>
/// User rev 0.8.4: the open part of a floor slab or of flat glass (a roof, terrace, canopy) is drawn — slabs in the
/// NC.Roof paint at their real thickness, glass pale blue with a slightly bluer diagonal hatch — never labelled.
/// </summary>
public sealed class ExposedStyle
{
    public bool Show { get; set; } = true;
    public double MinAreaSf { get; set; } = 20;
    public string GlassFill { get; set; } = "#EEF6FB";
    public HatchStyle GlassHatch { get; set; } = new() { Stroke = "#A9CBE3", StrokePt = 0.35, SpacingFt = 3, AngleDeg = 45 };
    public string LineColor { get; set; } = "#9A9A9A";
    /// <summary>Opacity of flat glass fills in plans and axons (user rev 0.8.6: 50 %).</summary>
    public double GlassOpacity { get; set; } = 0.5;
    public double LinePt { get; set; } = 0.35;
}

/// <summary>
/// User rev 0.8.3: screens (NC.Screen) drawn as what they are — solid walls as thick as the model's slabs, a solid
/// floor where they close around an area (the roof inside a penthouse screen), open on top so the equipment inside
/// shows at its real heights. They never add a level.
/// </summary>
public sealed class ScreenStyle
{
    public bool Solid { get; set; } = true;
    /// <summary>Wall thickness; 0 = the median thickness of the model's slabs (NC.Slab), else fallbackThicknessFt.</summary>
    public double ThicknessFt { get; set; }
    public double FallbackThicknessFt { get; set; } = 1;
    /// <summary>Panels this close together count as one screen; gaps this small still close the floor.</summary>
    public double CloseGapFt { get; set; } = 3;
    public string AxonWallFill { get; set; } = "#FFFFFF";
    public double[] AxonWallDarken { get; set; } = { 0.08, 0.16 };
    public string AxonFloorFill { get; set; } = "#FFFFFF";
    /// <summary>The floor plate inside the screen, laid on the roof below (opaque white).</summary>
    public double AxonFloorThicknessFt { get; set; } = 0.5;
    public string AxonLineColor { get; set; } = "#4A4A4A";
    public double AxonLinePt { get; set; } = 0.35;
    public string PlanFloorFill { get; set; } = "#FFFFFF";
    public string PlanFloorStroke { get; set; } = "#4A4A4A";
    public double PlanFloorPt { get; set; } = 0.5;
    public string PlanWallFill { get; set; } = "#4A4A4A";
    public string SectionWallFill { get; set; } = "#4A4A4A";
    public string SectionFloorFill { get; set; } = "#FFFFFF";
    /// <summary>Section: the part of the penthouse volume the screen does not close (its top), dashed.</summary>
    public string SectionDashStroke { get; set; } = "#4A4A4A";
    public double SectionDashPt { get; set; } = 0.75;
    public double[] SectionDash { get; set; } = { 4, 3 };
}

/// <summary>
/// Generated streetscape (user rev 0.8.3). Blocks = site areas (hardscape, landscape, our site) of at least
/// minBlockAreaSf, plus closed kerb curves on road layers; roads are what lies between them.
/// </summary>
public sealed class SiteStyle
{
    public double MinBlockAreaSf { get; set; } = 1500;
    /// <summary>Sidewalk: a band this wide inside every block along the road (paint: roles "BG.Sidewalk").</summary>
    public double SidewalkWidthFt { get; set; } = 4.5;
    /// <summary>Paving joints across the sidewalk, every this many feet (0 = none).</summary>
    public double SidewalkJointFt { get; set; } = 5;
    public StreetTreeStyle StreetTrees { get; set; } = new();
    public MedianStyle Median { get; set; } = new();
}

public sealed class StreetTreeStyle
{
    public bool Enabled { get; set; } = true;
    /// <summary>Trunks this far from the kerb, inside the sidewalk.</summary>
    public double OffsetFt { get; set; } = 1.5;
    public double SpacingFt { get; set; } = 30;
    /// <summary>No tree this close to a block corner (crossings, sight lines).</summary>
    public double CornerClearFt { get; set; } = 25;
    /// <summary>Kerb stretches shorter than this get no trees.</summary>
    public double MinEdgeFt { get; set; } = 40;
}

/// <summary>Green median on wide roads: on the road centre line, round (semicircle) ends short of the crossings.</summary>
public sealed class MedianStyle
{
    public bool Enabled { get; set; } = true;
    /// <summary>Kerb-to-kerb width above which a road gets a median.</summary>
    public double MinRoadWidthFt { get; set; } = 50;
    /// <summary>Wider gaps are open ground, not a road.</summary>
    public double MaxRoadWidthFt { get; set; } = 160;
    public double WidthFt { get; set; } = 5;
    /// <summary>The round end stops this far before the crossing (the block corner line).</summary>
    public double EndClearFt { get; set; } = 20;
    public double TreeSpacingFt { get; set; } = 30;
}

/// <summary>Trees planted at random in green areas (site plan, plans, site axon), like the section entourage.</summary>
public sealed class GreenTreeStyle
{
    public bool Enabled { get; set; } = true;
    public List<string> Roles { get; set; } = new() { "BG.Landscape", "NC.Green" };
    /// <summary>Minimum distance between trunks.</summary>
    public double SpacingFt { get; set; } = 26;
    /// <summary>Share of the densest packing at that spacing (1 = as many as fit).</summary>
    public double Coverage { get; set; } = 0.6;
    /// <summary>Trunks keep this far from the edge of the green and from buildings / water.</summary>
    public double EdgeInsetFt { get; set; } = 6;
    public int MaxPerArea { get; set; } = 600;
    public int MaxTotal { get; set; } = 3000;
    /// <summary>Only greens at grade (top no higher than ground + this): no planting on roof gardens.</summary>
    public double MaxAboveGroundFt { get; set; } = 3;
    /// <summary>Greens whose object name contains one of these words are open lawns: never planted.</summary>
    public List<string> OpenNameKeywords { get; set; } = new();
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
/// Unified typography (user rev 0.8, was docs/SITE_STYLE.md §0.1 Arial): Futura PT Heavy / Book, all capitals, for every
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
    /// <summary>Last resort for a program label that fits neither size above (all axons).</summary>
    public Font PlaneLabelMin { get; set; } = new();
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
    /// <summary>Charts (PRD 5.5 / 5.6): department title (Bold caps 13), sub-group (11), total (Bold 22), legend (11).</summary>
    public Font ChartTitle { get; set; } = new();
    public Font ChartGroup { get; set; } = new();
    public Font ChartTotal { get; set; } = new();
    public Font ChartLegend { get; set; } = new();
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
    /// <summary>Our raised site parts cut by the section (a ramp over parking, an entrance plaza on a podium): these roles…</summary>
    public List<string> SiteCutRoles { get; set; } = new();
    /// <summary>…when their top is at least this far above the ground (flat paving stays the ground line).</summary>
    public double SiteCutMinAboveGroundFt { get; set; } = 2;
    public string SiteCutFill { get; set; } = "#D9D9D9";
    public string SiteCutStroke { get; set; } = "#000000";
    public double SiteCutPt { get; set; } = 0.75;
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
    /// <summary>▼ + level name + elevation in a left column with a dashed line to the building, as in elevations.</summary>
    public bool LevelMarkers { get; set; } = true;
    public bool HeightDimension { get; set; }
    /// <summary>
    /// Penthouse (user rev 0.8): a top row made only of these programs is one grey block, no slab over it, with a
    /// top-of-penthouse elevation marker.
    /// </summary>
    public List<string> PenthousePrograms { get; set; } = new();
    public string PenthouseFill { get; set; } = "#BDBDBD";
    public string PenthouseLabel { get; set; } = "PENTHOUSE";
    public string PenthouseTopLabel { get; set; } = "T.O. PENTHOUSE";
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
    /// <summary>Largest artboard side; Illustrator's canvas is 16383 pt and runtime.jsx refuses layouts past 16000.</summary>
    public double MaxArtboardPt { get; set; } = 15000;
    /// <summary>
    /// Fixed artboards (user rev 0.9.2): what the drawing is centred on — "PropertyLine" (our NC.PropertyLine, the
    /// building when there is none) or "Building".
    /// </summary>
    public string CentreOn { get; set; } = "PropertyLine";
    /// <summary>A drawing larger than the fixed artboard gets a "Fit content" artboard at the same scale (user rev 0.9.2).</summary>
    public bool FitContentWhenLarger { get; set; } = true;
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
    /// <summary>User rev 0.9.4: every room name at the label size (fonts.label); one that does not fit gets a leader.</summary>
    public bool LabelUniform { get; set; } = true;
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
    /// <summary>Edges where two hardscape pieces meet (user rev 0.8: 1 pt dark grey, light).</summary>
    public Paint HardscapeEdge { get; set; } = new();
    /// <summary>Edge between the site and the road (1.5 pt dark grey, light).</summary>
    public Paint RoadEdge { get; set; } = new();
    public double TitleTagPadPt { get; set; }
    /// <summary>Trees are circles of this diameter range (ft).</summary>
    public double[] TreeDiameterFt { get; set; } = { 14, 22 };
    /// <summary>00_Site_Plan.ai: site shown this far around the ground floor.</summary>
    public double SiteMarginFt { get; set; } = 160;
    /// <summary>Site plan: margin = this × the NC site's own width / depth on each side (user rev 0.8: 5).</summary>
    public double SiteExtentFactor { get; set; } = 5;
    public string SiteTitle { get; set; } = "Site Plan";
    /// <summary>00_Site_Plan.ai second artboard: the whole building seen from above (user rev 0.8.3).</summary>
    public bool SiteRoofView { get; set; } = true;
    public string SiteRoofTitle { get; set; } = "Site Plan - Roof";
    public string RoofTierStroke { get; set; } = "#4A4A4A";
    public double RoofTierPt { get; set; } = 0.75;
    /// <summary>Upper floors sit on the same site, washed out by a white veil of this opacity.</summary>
    public string UpperVeilFill { get; set; } = "#FFFFFF";
    public double UpperVeilOpacity { get; set; } = 0.7;
    /// <summary>Material hatches on the veiled upper floors and on the plans overview (rev 0.9.7: off by default — they
    /// are barely visible there and thousands of hatch lines make Illustrator take an hour).</summary>
    public bool UpperFloorHatch { get; set; }
    public bool OverviewHatch { get; set; }
    /// <summary>Basement plans: cut earth instead of the site.</summary>
    public string BasementEarthFill { get; set; } = "#D9D9D9";
    /// <summary>Smallest room-name size (pt at the reference scale) tried before a leader is drawn.</summary>
    public double LabelMinPt { get; set; } = 8;
    /// <summary>A program's name appears once per level (on its largest block); the legend covers the rest.</summary>
    public bool LabelOncePerProgram { get; set; } = true;
    /// <summary>Site plan: names of the other buildings (numeric GIS ids are never shown).</summary>
    public bool ContextNames { get; set; } = true;
    /// <summary>Ground floor and site plans: names of our own site areas with these roles (MAIN ENTRANCE, RIVER STEPS).</summary>
    public List<string> SiteNameRoles { get; set; } = new();
    /// <summary>Size of those names (pt at the reference scale; the context label's own size when 0).</summary>
    public double SiteNamePt { get; set; }
    /// <summary>Lines where blocks of the same name meet (a lab row on its grid): dashed; 0 = none.</summary>
    public string GridStroke { get; set; } = "#7A7A7A";
    public double GridPt { get; set; }
    public double[]? GridDash { get; set; }
    /// <summary>The floor above where it overhangs this one (open ground floor under an upper floor): dashed outline; 0 = none.</summary>
    public string OverheadStroke { get; set; } = "#555555";
    public double OverheadPt { get; set; }
    public double[]? OverheadDash { get; set; }
    public double OverheadMinAreaSf { get; set; } = 200;
}

/// <summary>
/// PRD 5.4 axons: shared settings (docs/AXON_STYLE.md) plus one block per drawing — stacked (SITE_STYLE §1 site
/// program axon), exploded A (AXON_STYLE §7, Atlanta) and exploded B (SITE_STYLE §3, planometric).
/// </summary>
public sealed class AxonStyle
{
    /// <summary>04_Axon_Levels as an axonometric cutaway (user rev 0.8.3).</summary>
    public LevelsAxonStyle Levels { get; set; } = new();
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
    /// <summary>Level axons below grade (user rev 0.8): solid far walls of the dug-out site from the basement floor up to grade.</summary>
    public string ExcavationFill { get; set; } = "#C8C8C8";
    public string ExcavationStroke { get; set; } = "#4A4A4A";
    public double ExcavationPt { get; set; } = 0.5;
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
    /// <summary>
    /// User rev 0.9.3: open space (circulation, interaction) is never a volume in the exploded, exploded B and floor
    /// key axons — a flat layer openLayerFt thick on its floor, information on the slab.
    /// </summary>
    public List<string> OpenPrograms { get; set; } = new();
    public double OpenLayerFt { get; set; } = 0.25;
    public double LevelColumnPt { get; set; }
    /// <summary>Trees cut by a building stop this far short of it (room for its outline, user rev 0.8.4).</summary>
    public double TreeClearPt { get; set; } = 1;
    /// <summary>04 axons: the site is the property line (NC.PropertyLine) grown by this much (user rev 0.8.4).</summary>
    public double PropertySiteMarginFt { get; set; } = 50;
    public TreeStyle Trees { get; set; } = new();
    public StackedAxonStyle Stacked { get; set; } = new();
    public ExplodedAxonStyle Exploded { get; set; } = new();
    public ExplodedBAxonStyle ExplodedB { get; set; } = new();
    public SiteAxonStyle Site { get; set; } = new();
    public string TitleStacked { get; set; } = "";
    public string TitleExploded { get; set; } = "";
    public string TitleExplodedB { get; set; } = "";
    public string TitleLevels { get; set; } = "";
}

/// <summary>03_Site_Axon.ai (SITE_STYLE §1): wider site, context at real height, people, an ANNOTATION layer kept for the user.</summary>
public sealed class SiteAxonStyle
{
    public double SiteMarginFt { get; set; } = 440;
    public double ContextMarginFt { get; set; } = 460;
    /// <summary>0 = real height.</summary>
    public double ContextHeightFt { get; set; }
    /// <summary>Taller context (a merged mesh, a tower far away) is cut at this height.</summary>
    public double MaxContextHeightFt { get; set; } = 400;
    public bool People { get; set; } = true;
    /// <summary>Dashed NC.Massing outlines (parking under a ramp, an envelope) in the site axon; the other axons keep them.</summary>
    public bool Massing { get; set; }
    public string Title { get; set; } = "SITE AXON";
    /// <summary>Layer for flows, view lines and service icons: created empty, never cleared by the plugin.</summary>
    public string AnnotationLayer { get; set; } = "ANNOTATION";
    /// <summary>User rev 0.8: no slab gaps — each block runs from its floor to the next floor (pure stacked massing).</summary>
    public bool FullHeightBlocks { get; set; } = true;
    /// <summary>Line between two floors on the faces (own layer AXON_LEVEL_LINE).</summary>
    public string LevelLineColor { get; set; } = "#000000";
    public double LevelLinePt { get; set; } = 0.5;
    /// <summary>Basement levels in the site axon (user rev 0.8.1: no — 03 shows the building from grade up).</summary>
    public bool ShowBasements { get; set; }
    /// <summary>
    /// User rev 0.8.3: the site and its context fill the whole page (no blank corners), clipped to a frame of the
    /// artboard size (or pageWidthIn × pageHeightIn with "Fit content") centred on our building.
    /// </summary>
    public bool FillPage { get; set; } = true;
    public double PageWidthIn { get; set; } = 17;
    public double PageHeightIn { get; set; } = 11;
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
    /// <summary>User rev 0.8.1: like the site axon — blocks floor to floor, heavy line only outside.</summary>
    public bool FullHeightBlocks { get; set; } = true;
    /// <summary>User rev 0.8.2: one thin line between floors (not the site axon's 2 pt).</summary>
    public string LevelLineColor { get; set; } = "#333333";
    public double LevelLinePt { get; set; } = 0.5;
    /// <summary>Basement levels in the stacked axon (user rev 0.8.1: no).</summary>
    public bool ShowBasements { get; set; }
    /// <summary>Open programs (axon.openPrograms) as a flat layer on their floor here too (false: massing stays solid).</summary>
    public bool OpenProgramsFlat { get; set; }
}

/// <summary>
/// Floor key axons (user rev 0.8.3): an axonometric cutaway — the floors below in plain white with thin level lines,
/// this floor's program blocks in colour split by white lines (as exploded B), nothing above but the building's
/// total outline.
/// </summary>
public sealed class LevelsAxonStyle
{
    public string BelowFill { get; set; } = "#FFFFFF";
    public string BelowStroke { get; set; } = "#8C8C8C";
    public double BelowStrokePt { get; set; } = 0.35;
    public string EdgeStroke { get; set; } = "#FFFFFF";
    public double EdgePt { get; set; } = 1.0;
    public double SilhouettePt { get; set; } = 2.0;
    public string OutlineStroke { get; set; } = "#808080";
    public double OutlinePt { get; set; } = 0.5;
    public double[]? OutlineDash { get; set; } = { 3, 2 };
}

/// <summary>Exploded axon A (AXON_STYLE §6–7).</summary>
public sealed class ExplodedAxonStyle
{
    public double SlabThicknessFt { get; set; }
    /// <summary>
    /// Basements hang below the whole site plate (user rev 0.8.1): the top basement's far edge sits this many feet
    /// (drawn height) under the site plate's front edge, so no basement plate is drawn over the site.
    /// </summary>
    public double BasementClearFt { get; set; } = 12;
    /// <summary>Tall spaces (double-height lab, auditorium) as translucent links to the plate above; false = the plate above is simply open.</summary>
    public bool TallConnectors { get; set; }
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
    /// <summary>
    /// User rev 0.9.3: each level is a neutral slab (slabThicknessFt, plateFill) with its programs standing on it as
    /// volumes this tall; open programs (axon.openPrograms) lie on the slab as a flat layer. 0 = the older style
    /// (every program is its own coloured slab).
    /// </summary>
    public double VolumeHeightFt { get; set; }
    /// <summary>User rev 0.9.4: the volumes keep their blocks' own heights (volumeHeightFt only when false).</summary>
    public bool VolumesAtModelHeight { get; set; } = true;
    public string PlateFill { get; set; } = "#F2F2F2";
    public double[] PlateSideDarken { get; set; } = { 0.08, 0.16 };
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


/// <summary>PRD 5.5 program chart and 5.6 pie; sizes are absolute points (charts are not drawn at building scale).</summary>
public sealed class ChartsStyle
{
    public ProgramChartStyle ProgramChart { get; set; } = new();
    public PieStyle Pie { get; set; } = new();
}

public sealed class ProgramChartStyle
{
    public double PageWidthPt { get; set; } = 1584;
    public double MarginPt { get; set; } = 36;
    public double BoxPadPt { get; set; } = 14;
    public double BoxGapPt { get; set; } = 20;
    /// <summary>Boxes are at least this wide, so short room lists still read as a block.</summary>
    public double MinBoxWidthPt { get; set; } = 300;
    public string BoxStroke { get; set; } = "#9A9A9A";
    public double BoxPt { get; set; } = 0.75;
    public double[]? BoxDash { get; set; }
    public double TitleGapPt { get; set; } = 8;
    public double GroupGapPt { get; set; } = 12;
    public double RowGapPt { get; set; } = 14;
    public double LabelGapPt { get; set; } = 4;
    /// <summary>White gap between the rectangles of one room type.</summary>
    public double RectGapPt { get; set; } = 1;
    public double RectMinPt { get; set; } = 4;
    public double RectMaxPt { get; set; } = 64;
    /// <summary>Rectangle side = this × sqrt(area in SF), so the drawn area stays proportional.</summary>
    public double AreaScalePtPerSqrtSf { get; set; } = 1.6;
    public string RectStroke { get; set; } = "#FFFFFF";
    public double RectStrokePt { get; set; }
    public string TotalFormat { get; set; } = "{0:N0} {1}";
    public string Title { get; set; } = "";
}

public sealed class PieStyle
{
    public double OuterPt { get; set; } = 300;
    public double InnerRatio { get; set; } = 0.43;
    /// <summary>90 = 12 o'clock.</summary>
    public double StartDeg { get; set; } = 90;
    public bool Clockwise { get; set; } = true;
    public string SegmentStroke { get; set; } = "#FFFFFF";
    public double SegmentStrokePt { get; set; } = 0.75;
    public string CenterText { get; set; } = "PROGRAM SF";
    public double LegendGapPt { get; set; } = 56;
    public double LegendSwatchPt { get; set; } = 13;
    public double LegendRowPt { get; set; } = 21;
    public string TotalLabel { get; set; } = "TOTAL:";
    public double MarginPt { get; set; } = 48;
    public BarStyle Bar { get; set; } = new();
}

/// <summary>Optional stacked bar (PRD 5.6 "Program Development Summary").</summary>
public sealed class BarStyle
{
    public bool Enabled { get; set; } = true;
    public double WidthPt { get; set; } = 760;
    public double HeightPt { get; set; } = 34;
    public double GapPt { get; set; } = 1;
    public string Title { get; set; } = "";
}

/// <summary>PRD 4 entourage: seeded Poisson-disk people, density per program.</summary>
public sealed class EntourageStyle
{
    public bool Enabled { get; set; } = true;
    public GreenTreeStyle GreenTrees { get; set; } = new();
    /// <summary>Section ground people: walkable ground within this far beyond the cut.</summary>
    public double SectionBandFt { get; set; } = 20;
    /// <summary>Sections: generated trees (street, median, greens) within this far beyond the cut.</summary>
    public double SectionTreeBandFt { get; set; } = 60;
    /// <summary>Elevation ground people: walkable ground within this far in front of the building.</summary>
    public double ElevationBandFt { get; set; } = 120;
    public string Color { get; set; } = "#9A9A9A";
    public double Opacity { get; set; } = 0.9;
    public double HeightFt { get; set; } = 5.75;
    public double MinSpacingFt { get; set; } = 11;
    public int Seed { get; set; } = 20251012;
    /// <summary>Upper bound per placement area, so a huge floor plate cannot flood the drawing.</summary>
    public int MaxPerArea { get; set; } = 150;
    /// <summary>People modelled by the user (role Entourage) replace the generated ones where they exist.</summary>
    public bool UseModelled { get; set; } = true;
    public double PlanRadiusFt { get; set; } = 1.15;
    public double PlanShoulderFt { get; set; } = 1.9;
    public double DensityPer1000Sf { get; set; } = 1.0;
    public double OutdoorDensityPer1000Sf { get; set; } = 0.3;
    public double OutdoorMinSpacingFt { get; set; } = 26;
    public Dictionary<string, double> DensityByProgram { get; set; } = new();
    /// <summary>Section / elevation trees (office TREES.ai silhouettes, SITE_STYLE §4 colours).</summary>
    public List<string> TreeFills { get; set; } = new();
    public double TreeOpacity { get; set; } = 0.85;
    /// <summary>Height from the tree object, clamped to this range; flat tree objects (plan circles) use the default.</summary>
    public double[] TreeHeightFt { get; set; } = { 20, 50 };
    public double TreeDefaultHeightFt { get; set; } = 32;

    /// <summary>People per 1,000 SF for a program (PRD 4: Lobby high, Lab/Office medium, Core / Shell / Mechanical 0).</summary>
    public double Density(string programKey) =>
        DensityByProgram.TryGetValue(programKey, out var v) ? v : DensityPer1000Sf;
}
