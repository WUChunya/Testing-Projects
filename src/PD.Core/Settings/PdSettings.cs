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
        Layers.ToDictionary(kv => kv.Key, kv => new LayerGuess(kv.Key, kv.Value.Role, kv.Value.ProgramKey));
}

public sealed class LayerSetting
{
    public string Role { get; set; } = Roles.Ignore;
    public string ProgramKey { get; set; } = "";
    /// <summary>Style mode per drawing: grayscale | line | color.</summary>
    public string Mode { get; set; } = "";
}

public sealed class ProgramSetting
{
    public string Key { get; set; } = "";
    public string Label { get; set; } = "";
    public string? Fill { get; set; }
    public int Order { get; set; }
}

public sealed class SectionSetting
{
    public string Name { get; set; } = "A";
    public string? ObjectId { get; set; }
    /// <summary>Two model-unit points [x0,y0,x1,y1].</summary>
    public double[] Line { get; set; } = Array.Empty<double>();
    /// <summary>Look to the left (+) or right (−) of the line direction.</summary>
    public bool LookLeft { get; set; } = true;
    public double DepthFt { get; set; } = 100;
}

public sealed class OutputSetting
{
    public string Folder { get; set; } = "";
    public string Scheme { get; set; } = "OPT-1";
    public List<string> Drawings { get; set; } = new() { "Plan", "Section", "AxonStacked", "AxonExploded", "ProgramChart", "Pie" };
    public string Scale { get; set; } = "1/32\" = 1'-0\"";
    public string Artboard { get; set; } = "11x17 Landscape";
}
