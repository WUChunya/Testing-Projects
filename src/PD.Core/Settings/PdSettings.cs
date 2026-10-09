using System.Text.Json;
using System.Text.Json.Serialization;
using PD.Core.Analysis;
using PD.Core.Classification;
using PD.Core.Cleanup;

namespace PD.Core.Settings;

/// <summary>Everything the user confirmed in the panel; stored as JSON in the 3dm DocumentUserText.</summary>
public sealed class PdSettings
{
    public int Version { get; set; } = 1;
    public Dictionary<string, LayerSetting> Layers { get; set; } = new();
    public Dictionary<string, SpanMode> SpanModes { get; set; } = new();
    public Dictionary<string, SingleSurfaceChoice> SingleSurfaces { get; set; } = new();
    public List<string> KeepStray { get; set; } = new();
    /// <summary>Null until the user edits levels on tab ③.</summary>
    public List<LevelOverride>? Levels { get; set; }
    public Dictionary<string, string> Names { get; set; } = new();
    /// <summary>Per-object program (mass id → program key) overriding its layer's program; tab ④.</summary>
    public Dictionary<string, string> ObjectPrograms { get; set; } = new();
    /// <summary>Null means the company legend (styles/program_legend.json).</summary>
    public List<ProgramSetting>? Programs { get; set; }
    public List<SectionSetting> Sections { get; set; } = new();
    public OutputSetting Output { get; set; } = new();
    public Dictionary<string, bool> Confirmed { get; set; } = new();
    public List<string> IgnoredIssues { get; set; } = new();

    public static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public string ToJson() => JsonSerializer.Serialize(this, Json);

    public static PdSettings FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new PdSettings();
        try { return JsonSerializer.Deserialize<PdSettings>(json, Json) ?? new PdSettings(); }
        catch (JsonException) { return new PdSettings(); }
    }

    public Dictionary<string, LayerGuess> RoleOverrides() =>
        Layers.ToDictionary(kv => kv.Key, kv => kv.Value.Use
            ? new LayerGuess(kv.Key, kv.Value.Role, kv.Value.ProgramKey)
            : new LayerGuess(kv.Key, Roles.Ignore, ""));
}

public sealed class LayerSetting
{
    /// <summary>Unchecked layers are ignored (hidden layers start unchecked).</summary>
    public bool Use { get; set; } = true;
    public string Role { get; set; } = Roles.Ignore;
    public string ProgramKey { get; set; } = "";
    /// <summary>Style mode per drawing: grayscale | line | color.</summary>
    public string Mode { get; set; } = "";
}

/// <summary>One program in the project colour scheme (tab ⑤). Becomes a global swatch and legend entry in the AI output.</summary>
public sealed class ProgramSetting
{
    /// <summary>Stable id used in layer mapping, object names (L03_KEY_01) and swatch names. Never renamed.</summary>
    public string Key { get; set; } = "";
    public string Label { get; set; } = "";
    public string? Fill { get; set; }
    public int Order { get; set; }
    /// <summary>Added by the user on tab ⑤ (not in the company legend); only these can be deleted.</summary>
    public bool IsCustom { get; set; }
    /// <summary>Layer names that auto-fill to this program on tab ①.</summary>
    public List<string> Aliases { get; set; } = new();
    public string? Stroke { get; set; }
    public double? StrokePt { get; set; }
    public double[]? Dash { get; set; }
    public string? Hatch { get; set; }
}

/// <summary>A section cut or an elevation: a plan line, the side it looks at, and the visible depth.</summary>
public sealed class SectionSetting
{
    public const string SectionKind = "Section";
    public const string ElevationKind = "Elevation";

    public string Name { get; set; } = "A";
    /// <summary>"Section" (cuts the building) or "Elevation" (projected view, nothing cut).</summary>
    public string Kind { get; set; } = SectionKind;
    public bool IsElevation => Kind == ElevationKind;
    public string? ObjectId { get; set; }
    /// <summary>Two model-unit points [x0,y0,x1,y1].</summary>
    public double[] Line { get; set; } = Array.Empty<double>();
    /// <summary>Look to the left (+) or right (−) of the line direction.</summary>
    public bool LookLeft { get; set; } = true;
    public double DepthFt { get; set; }
}

public sealed class OutputSetting
{
    /// <summary>Empty values are filled from rules_default.json "output".</summary>
    public string Folder { get; set; } = "";
    public string Scheme { get; set; } = "";
    public List<string>? Drawings { get; set; }
    public string Scale { get; set; } = "";
    public string Artboard { get; set; } = "";
    /// <summary>Also export a PNG per artboard from Illustrator; null = rules default.</summary>
    public bool? ExportPng { get; set; }

    public void FillDefaults(PD.Core.Config.OutputRules r, string folder)
    {
        if (string.IsNullOrEmpty(Scheme)) Scheme = r.Scheme;
        if (string.IsNullOrEmpty(Folder)) Folder = folder;
        Drawings ??= r.Drawings.ToList();
        if (string.IsNullOrEmpty(Scale)) Scale = r.DefaultScale;
        if (string.IsNullOrEmpty(Artboard)) Artboard = r.DefaultArtboard;
    }
}
