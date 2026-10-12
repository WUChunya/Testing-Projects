using System.Text.Json;
using System.Text.RegularExpressions;
using PD.Core.Model;
using PD.Core.Styles;

namespace PD.Core.Classification;

/// <summary>
/// Layer roles. Program and NC.* are our new construction (NC); BG.* is the existing context. NC.Core / NC.Stair objects
/// are masses (program CORE / STAIR): counted, drawn and connected like any program.
/// </summary>
public static class Roles
{
    public const string Program = "Program";
    public const string Core = "NC.Core";
    public const string Stair = "NC.Stair";
    public const string Slab = "NC.Slab";
    public const string Facade = "NC.Facade";
    public const string Structure = "NC.Structure";
    public const string Green = "NC.Green";
    public const string NcHardscape = "NC.Hardscape";
    public const string NcTree = "NC.Tree";
    public const string NcWater = "NC.Water";
    public const string Building = "BG.Building";
    public const string Road = "BG.Road";
    public const string Hardscape = "BG.Hardscape";
    public const string Landscape = "BG.Landscape";
    public const string Tree = "BG.Tree";
    public const string Water = "BG.Water";
    public const string Base = "BG.Base";
    public const string Line = "BG.Line";
    public const string Ignore = "Ignore";

    public static readonly string[] All =
    {
        Program, Core, Stair, Slab, Facade, Structure, Green, NcHardscape, NcTree, NcWater,
        Building, Road, Hardscape, Landscape, Tree, Water, Base, Line, Ignore,
    };

    /// <summary>Roles whose objects are program masses, and the program they get when the layer names none.</summary>
    public static string? MassProgram(string role) => role switch
    {
        Program => "",
        Core => "CORE",
        Stair => "STAIR",
        _ => null,
    };

    public static bool IsMass(string role) => MassProgram(role) != null;

    /// <summary>Flat site elements (ours and the context's), drawn under buildings in plans and axons.</summary>
    public static bool IsSite(string role) => role is Green or NcHardscape or NcTree or NcWater or Road or Hardscape or Landscape or Tree or Water;

    public static bool IsNew(string role) => role == Program || role.StartsWith("NC.", StringComparison.Ordinal);

    /// <summary>Roles saved by older versions (BG.Facade / BG.Slab are our own building).</summary>
    public static string Normalize(string role) => role switch
    {
        "BG.Facade" => Facade,
        "BG.Slab" => Slab,
        _ => role,
    };

    /// <summary>NC variant of a site role (a tree on an NC:: layer is ours).</summary>
    public static string ToNew(string role) => role switch
    {
        Tree => NcTree,
        Water => NcWater,
        Landscape => Green,
        Hardscape => NcHardscape,
        _ => role,
    };
}

/// <summary>One legend entry; stroke/dash/hatch carry the special styles (Shell, Void, Unassigned) to the AI output.</summary>
public sealed record ProgramDef(string Key, string Label, string? Fill, string[] Aliases,
    string? Stroke = null, double? StrokePt = null, double[]? Dash = null, string? Hatch = null);

public sealed record LayerGuess(string LayerPath, string Role, string ProgramKey, bool IsFallback = false);

/// <summary>Pre-fills layer roles from keywords (PRD A1). Each layer is judged by its own name; no inheritance.</summary>
public sealed class LayerClassifier
{
    readonly List<(Regex re, string role, bool first)> _rules;
    readonly Regex? _ncPath;
    readonly string _unmatched;
    public IReadOnlyList<ProgramDef> Programs { get; }

    public LayerClassifier(JsonDocument keywords, IReadOnlyList<ProgramDef> programs)
    {
        _rules = keywords.RootElement.GetProperty("rules").EnumerateArray()
            .Select(r => (new Regex(r.GetProperty("pattern").GetString()!, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
                Roles.Normalize(r.GetProperty("role").GetString()!), r.TryGetProperty("first", out var f) && f.GetBoolean()))
            .ToList();
        _ncPath = keywords.RootElement.TryGetProperty("ncPathPattern", out var nc)
            ? new Regex(nc.GetString()!, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant) : null;
        _unmatched = keywords.RootElement.GetProperty("unmatchedRole").GetString()!;
        Programs = programs;
    }

    public static LayerClassifier Default() => WithPrograms(LoadPrograms());

    /// <summary>Keyword rules from styles/layer_keywords.json with a project's effective program list.</summary>
    public static LayerClassifier WithPrograms(IReadOnlyList<ProgramDef> programs)
    {
        using var kw = StyleResources.Read(StyleResources.LayerKeywords);
        return new LayerClassifier(kw, programs);
    }

    public static List<ProgramDef> LoadPrograms(JsonDocument? legend = null)
    {
        var doc = legend ?? StyleResources.Read(StyleResources.ProgramLegend);
        try
        {
            return doc.RootElement.GetProperty("programs").EnumerateArray().Select(p => new ProgramDef(
                p.GetProperty("key").GetString()!,
                p.GetProperty("label").GetString()!,
                p.GetProperty("fill").ValueKind == JsonValueKind.String ? p.GetProperty("fill").GetString() : null,
                p.GetProperty("aliases").EnumerateArray().Select(a => a.GetString()!).ToArray(),
                Str(p, "stroke"), p.TryGetProperty("strokePt", out var sp) ? sp.GetDouble() : null,
                p.TryGetProperty("dash", out var da) && da.ValueKind == JsonValueKind.Array ? da.EnumerateArray().Select(x => x.GetDouble()).ToArray() : null,
                Str(p, "hatch"))).ToList();
        }
        finally
        {
            if (legend == null) doc.Dispose();
        }
    }

    static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    public LayerGuess Guess(LayerInfo layer)
    {
        var name = layer.Name.Trim();
        // Strip a PRG:: style prefix: "PRG::Lab" has its own name "Lab" already; accept "PRG-Lab" too.
        var bare = Regex.Replace(name, "^PRG[-_ ]", "", RegexOptions.IgnoreCase);
        foreach (var (re, role, _) in _rules.Where(r => r.first))
            if (re.IsMatch(name)) return new LayerGuess(layer.Path, role, "");
        var program = MatchProgram(bare);
        if (program != null) return new LayerGuess(layer.Path, Roles.Program, program.Key);
        foreach (var (re, role, _) in _rules.Where(r => !r.first))
            if (re.IsMatch(name)) return new LayerGuess(layer.Path, IsNewPath(layer.Path) ? Roles.ToNew(role) : role, "");
        return new LayerGuess(layer.Path, _unmatched, "", IsFallback: true);
    }

    /// <summary>Layer path with an NC / NEW / PROJECT / PROPOSED segment: our new construction.</summary>
    public bool IsNewPath(string path) => _ncPath?.IsMatch(path) == true;

    /// <summary>Exact alias, then key, then whole-word alias contained in the name ("LAB SUPPORT" beats "LAB").</summary>
    public ProgramDef? MatchProgram(string name)
    {
        var n = Normalize(name);
        if (n.Length == 0) return null;
        var exact = Programs.FirstOrDefault(p => p.Aliases.Any(a => Normalize(a) == n) || Normalize(p.Key) == n);
        if (exact != null) return exact;
        return Programs
            .SelectMany(p => p.Aliases.Select(a => (p, a: Normalize(a))))
            .Where(x => x.a.Length > 0 && Regex.IsMatch(n, $"\\b{Regex.Escape(x.a)}\\b"))
            .OrderByDescending(x => x.a.Length)
            .Select(x => x.p)
            .FirstOrDefault();
    }

    static string Normalize(string s) => Regex.Replace(s.ToUpperInvariant().Replace('_', ' ').Replace('-', ' '), "\\s+", " ").Trim();
}
