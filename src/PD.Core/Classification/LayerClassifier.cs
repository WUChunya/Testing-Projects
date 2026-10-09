using System.Text.Json;
using System.Text.RegularExpressions;
using PD.Core.Model;
using PD.Core.Styles;

namespace PD.Core.Classification;

public static class Roles
{
    public const string Program = "Program";
    public const string Facade = "BG.Facade";
    public const string Slab = "BG.Slab";
    public const string Building = "BG.Building";
    public const string Base = "BG.Base";
    public const string Ignore = "Ignore";
}

public sealed record ProgramDef(string Key, string Label, string? Fill, string[] Aliases);

public sealed record LayerGuess(string LayerPath, string Role, string ProgramKey, bool IsFallback = false);

/// <summary>Pre-fills layer roles from keywords (PRD A1). Each layer is judged by its own name; no inheritance.</summary>
public sealed class LayerClassifier
{
    readonly List<(Regex re, string role)> _rules;
    readonly string _unmatched;
    public IReadOnlyList<ProgramDef> Programs { get; }

    public LayerClassifier(JsonDocument keywords, IReadOnlyList<ProgramDef> programs)
    {
        _rules = keywords.RootElement.GetProperty("rules").EnumerateArray()
            .Select(r => (new Regex(r.GetProperty("pattern").GetString()!, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
                r.GetProperty("role").GetString()!))
            .ToList();
        _unmatched = keywords.RootElement.GetProperty("unmatchedRole").GetString()!;
        Programs = programs;
    }

    public static LayerClassifier Default()
    {
        using var kw = StyleResources.Read(StyleResources.LayerKeywords);
        return new LayerClassifier(kw, LoadPrograms());
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
                p.GetProperty("aliases").EnumerateArray().Select(a => a.GetString()!).ToArray())).ToList();
        }
        finally
        {
            if (legend == null) doc.Dispose();
        }
    }

    public LayerGuess Guess(LayerInfo layer)
    {
        var name = layer.Name.Trim();
        // Strip a PRG:: style prefix: "PRG::Lab" has its own name "Lab" already; accept "PRG-Lab" too.
        var bare = Regex.Replace(name, "^PRG[-_ ]", "", RegexOptions.IgnoreCase);
        var program = MatchProgram(bare);
        if (program != null) return new LayerGuess(layer.Path, Roles.Program, program.Key);
        foreach (var (re, role) in _rules)
            if (re.IsMatch(name)) return new LayerGuess(layer.Path, role, "");
        return new LayerGuess(layer.Path, _unmatched, "", IsFallback: true);
    }

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
