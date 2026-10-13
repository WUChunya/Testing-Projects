using System.Text.Json;
using System.Text.Json.Serialization;
using PD.Core.Analysis;
using PD.Core.Classification;
using PD.Core.Cleanup;

namespace PD.Core.Settings;

/// <summary>Everything the user confirmed in the panel; stored as JSON in the 3dm DocumentUserText.</summary>
public sealed class PdSettings
{
    /// <summary>2 = v0.7.0 (site plan / site axon outputs exist).</summary>
    public int Version { get; set; } = CurrentVersion;

    public const int CurrentVersion = 2;
    /// <summary>When these settings were last saved (UTC, ISO 8601): the newest copy wins on load (user rev 0.8.3).</summary>
    public string SavedUtc { get; set; } = "";
    /// <summary>File name of the copy written next to the model and into every export folder.</summary>
    public const string FileName = "pd_settings.json";
    public Dictionary<string, LayerSetting> Layers { get; set; } = new();
    public Dictionary<string, SpanMode> SpanModes { get; set; } = new();
    public Dictionary<string, SingleSurfaceChoice> SingleSurfaces { get; set; } = new();
    public List<string> KeepStray { get; set; } = new();
    /// <summary>Null until the user edits levels on tab 3.</summary>
    public List<LevelOverride>? Levels { get; set; }
    public Dictionary<string, string> Names { get; set; } = new();
    /// <summary>Per-object program (mass id → program key) overriding its layer's program; tab 4.</summary>
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

    /// <summary>
    /// Settings from JSON. A part a newer / older plugin cannot read (a renamed enum value, a changed shape) is
    /// skipped on its own instead of losing everything (user rev 0.8.3: settings survived plugin updates badly).
    /// </summary>
    public static PdSettings FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new PdSettings();
        PdSettings? s;
        try { s = JsonSerializer.Deserialize<PdSettings>(json, Json); }
        catch (JsonException) { s = Lenient(json); }
        if (s == null) return new PdSettings();
        foreach (var l in s.Layers.Values) l.Role = Roles.Normalize(l.Role);
        // Saves from before v0.7.0 list their drawings without the new site plan / site axon: tick them once.
        if (s.Version < 2 && s.Output.Drawings != null)
            foreach (var add in new[] { "SitePlan", "SiteAxon" })
                if (!s.Output.Drawings.Contains(add)) s.Output.Drawings.Add(add);
        s.Version = CurrentVersion;
        return s;
    }

    /// <summary>Property by property: keeps every top-level setting that still reads.</summary>
    static PdSettings? Lenient(string json)
    {
        System.Text.Json.Nodes.JsonObject? obj;
        try { obj = System.Text.Json.Nodes.JsonNode.Parse(json) as System.Text.Json.Nodes.JsonObject; }
        catch (JsonException) { return null; }
        if (obj == null) return null;
        var result = new PdSettings();
        foreach (var prop in typeof(PdSettings).GetProperties().Where(p => p.CanWrite))
        {
            var node = obj.FirstOrDefault(kv => string.Equals(kv.Key, prop.Name, StringComparison.OrdinalIgnoreCase)).Value;
            if (node == null) continue;
            try { prop.SetValue(result, node.Deserialize(prop.PropertyType, Json)); }
            catch (Exception e) when (e is JsonException or NotSupportedException or InvalidOperationException) { }
        }
        return result;
    }

    /// <summary>The most recently saved of several copies (3dm, model side file); null when none.</summary>
    public static PdSettings? Newest(params PdSettings?[] copies) =>
        copies.Where(c => c != null).OrderByDescending(c => c!.SavedUtc, StringComparer.Ordinal).FirstOrDefault();

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

/// <summary>One program in the project colour scheme (tab 5). Becomes a global swatch and legend entry in the AI output.</summary>
public sealed class ProgramSetting
{
    /// <summary>Stable id used in layer mapping, object names (L03_KEY_01) and swatch names. Never renamed.</summary>
    public string Key { get; set; } = "";
    public string Label { get; set; } = "";
    public string? Fill { get; set; }
    public int Order { get; set; }
    /// <summary>Added by the user on tab 5 (not in the company legend); only these can be deleted.</summary>
    public bool IsCustom { get; set; }
    /// <summary>Layer names that auto-fill to this program on tab 1.</summary>
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
    /// <summary>Elevations: look towards our building whichever side of the line it is on (ignores <see cref="LookLeft"/>).</summary>
    public bool AutoSide { get; set; } = true;
    public double DepthFt { get; set; }
}

public sealed class OutputSetting
{
    /// <summary>Save location; empty = the Rhino model's folder. Each export makes a new timestamped sub-folder in it.</summary>
    public string SaveIn { get; set; } = "";
    /// <summary>Folder of the last export (for "Open folder").</summary>
    public string LastFolder { get; set; } = "";
    /// <summary>Generate the .ai files in Illustrator after exporting; null = rules default.</summary>
    public bool? RunIllustrator { get; set; }
    public string Scheme { get; set; } = "";
    public List<string>? Drawings { get; set; }
    public string Scale { get; set; } = "";
    /// <summary>Section scale; empty = rules default (1" = 16'-0").</summary>
    public string SectionScale { get; set; } = "";
    public string Artboard { get; set; } = "";
    /// <summary>Also export a PNG per artboard from Illustrator; null = rules default.</summary>
    public bool? ExportPng { get; set; }
    /// <summary>Axon view direction (into the scene) taken from the Rhino viewport; null = style axon azimuth/elevation (isometric).</summary>
    public double[]? AxonViewDirection { get; set; }
    /// <summary>Take <see cref="AxonViewDirection"/> from the active Rhino viewport at export time.</summary>
    public bool AxonFromViewport { get; set; }
    /// <summary>
    /// Where every axon is seen from (user rev 0.8.3): a building corner "SW" / "SE" / "NE" / "NW", "Viewport" (the
    /// active Rhino view at export), or "Point" (the V view marker placed in plan, <see cref="AxonViewPoint"/>).
    /// Empty = the style's azimuth (south-west), or the viewport when <see cref="AxonFromViewport"/> is set.
    /// </summary>
    public string AxonView { get; set; } = "";
    /// <summary>The V view marker: viewer position [x, y] in model units; the axons look from it at the building.</summary>
    public double[]? AxonViewPoint { get; set; }
    /// <summary>Excel program table for the charts (PRD 5.5); empty = use the model's areas (GSF).</summary>
    public string ProgramExcel { get; set; } = "";
    /// <summary>Draw entourage (PRD 4); null = style default.</summary>
    public bool? Entourage { get; set; }
    /// <summary>Entourage random seed: kept in the project so a re-export puts the people back in the same places.</summary>
    public int? EntourageSeed { get; set; }

    public void FillDefaults(PD.Core.Config.OutputRules r)
    {
        if (string.IsNullOrEmpty(Scheme)) Scheme = r.Scheme;
        Drawings ??= r.Drawings.ToList();
        if (string.IsNullOrEmpty(Scale)) Scale = r.DefaultScale;
        if (string.IsNullOrEmpty(SectionScale)) SectionScale = r.DefaultSectionScale;
        if (string.IsNullOrEmpty(Artboard)) Artboard = r.DefaultArtboard;
    }
}
