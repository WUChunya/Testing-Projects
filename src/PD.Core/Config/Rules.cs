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
    public double PlanCutHeightFt { get; set; }
    public double MinLevelOverlapFt { get; set; }
    public string Overlap { get; set; } = "smallerVolumeWins";
    public int SpanKeepTallMaxLevels { get; set; }
    public double SpanKeepTallMaxStoryFactor { get; set; }
    public string SingleSurface { get; set; } = "ask";
    public double SingleSurfaceExtrudeFt { get; set; }
    public double MinOverlapAreaSf { get; set; }
}

public sealed class FacadeRules
{
    public double ThinSheetMaxFt { get; set; }
    public double OutlineDistanceFt { get; set; }
}

public sealed class OutputRules
{
    public string FolderPattern { get; set; } = "";
    public string Scheme { get; set; } = "";
    public List<string> Drawings { get; set; } = new();
    public List<string> Scales { get; set; } = new();
    public string DefaultScale { get; set; } = "";
    public List<string> Artboards { get; set; } = new();
    public string DefaultArtboard { get; set; } = "";
    public double SectionDepthFt { get; set; }
}
