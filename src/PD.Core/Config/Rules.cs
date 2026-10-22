using System.Text.Json;
using PD.Core.Styles;

namespace PD.Core.Config;

/// <summary>Typed view of styles/rules_default.json. Lengths in feet.</summary>
public sealed class Rules
{
    public LevelRules Levels { get; set; } = new();
    public CleanupRules Cleanup { get; set; } = new();
    public FacadeRules Facade { get; set; } = new();
    public OutputRules Output { get; set; } = new();

    internal static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    public static Rules Default() => JsonSerializer.Deserialize<Rules>(StyleResources.ReadText(StyleResources.RulesDefault), Json)!;
}

public sealed class LevelRules
{
    public double ClusterToleranceFt { get; set; }
    /// <summary>A mass or facade bottom may sit this far BELOW a slab top and still snap to it (masses are often
    /// drawn from the slab soffit, PRD B1 step 2).</summary>
    public double SlabSnapBelowFt { get; set; } = 0.5;
    public double MinFloorHeightFt { get; set; }
    public double GroundElevationFt { get; set; }
    public double SlabMaxThicknessFt { get; set; }
    public double SlabMinAreaSf { get; set; }
    public int FacadeMinRepeat { get; set; }
    public int MassOnlyMinSupport { get; set; }
    public double ScatterWarnFt { get; set; }
    public double SlabMismatchFt { get; set; }
}

public sealed class CleanupRules
{
    public double OutlierDistanceFactor { get; set; }
    public double OutlierMinDistanceFt { get; set; }
    public double WeldToleranceFt { get; set; }
    public double CapPlanarToleranceFt { get; set; }
    public double MinSolidVolumeCf { get; set; }
    public double GapCloseToleranceFt { get; set; }
    /// <summary>Local coordinates snap to this grid (1 mm): raw Rhino doubles break the 2D overlays. 0 = off.</summary>
    public double CoordSnapFt { get; set; } = 1 / 304.8;
    public double PlanCutHeightFt { get; set; }
    public double MinLevelOverlapFt { get; set; }
    /// <summary>A mass no thicker than this is a slab-like element (roof garden, terrace, paving): it belongs to the
    /// level under its own top face instead of needing a full story of overlap.</summary>
    public double FlatMassMaxThicknessFt { get; set; } = 2.5;
    /// <summary>
    /// A mezzanine level holds the masses whose bottom is within this of its elevation or above (user rev 0.9.3: the
    /// RIVER parking decks at 10'); main storeys leave those to it and run to the next main level.
    /// </summary>
    public double MezzanineStartToleranceFt { get; set; } = 1.0;
    /// <summary>
    /// User rev 0.9.4: the glass (NC.Facade standing in a storey) is the building envelope. Inside it, on each main
    /// storey, what no program fills is indoor circulation (program <see cref="EnvelopeProgram"/>).
    /// </summary>
    public bool GlassEnvelope { get; set; } = true;
    public string EnvelopeProgram { get; set; } = "CIRCULATION";
    /// <summary>Glass and blocks closer than this close the envelope (a mullion gap, a block set back from the glass).</summary>
    public double EnvelopeGapFt { get; set; } = 1.5;
    public double EnvelopeMinAreaSf { get; set; } = 200;
    public string Overlap { get; set; } = "smallerVolumeWins";
    public int SpanKeepTallMaxLevels { get; set; }
    public double SpanKeepTallMaxStoryFactor { get; set; }
    public string SingleSurface { get; set; } = "ask";
    public double SingleSurfaceExtrudeFt { get; set; }
    public double MinOverlapAreaSf { get; set; }
    /// <summary>Blocks on a level closer than this are treated as touching (drawing geometry only; areas unchanged).</summary>
    public double SealGapFt { get; set; } = 0.2;
}

public sealed class FacadeRules
{
    public double ThinSheetMaxFt { get; set; }
    public double OutlineDistanceFt { get; set; }
}

public sealed class OutputRules
{
    /// <summary>Sub-folder created per export in the save location; see <see cref="Writers.OutputFolder"/>.</summary>
    public string FolderPattern { get; set; } = "";
    /// <summary>Generate the .ai files in Illustrator right after the export (in the background).</summary>
    public bool RunIllustrator { get; set; } = true;
    /// <summary>Quit Illustrator after generating when the plugin started it (files are written, nothing stays open).</summary>
    public bool QuitIllustratorWhenDone { get; set; } = true;
    public string Scheme { get; set; } = "";
    public List<string> Drawings { get; set; } = new();
    public List<string> Scales { get; set; } = new();
    public string DefaultScale { get; set; } = "";
    /// <summary>Program sections have their own scale (docs/SITE_STYLE.md §4).</summary>
    public string DefaultSectionScale { get; set; } = "1\" = 16'-0\"";
    public List<string> Artboards { get; set; } = new();
    public string DefaultArtboard { get; set; } = "";
    public double SectionDepthFt { get; set; }
    public Dictionary<string, double[]> ArtboardSizesPt { get; set; } = new();
    public double IllustratorTimeoutMin { get; set; }
    public string ScaleHelp { get; set; } = "";
    public bool ExportPng { get; set; }
    public double PngScalePercent { get; set; }
}
