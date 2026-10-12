using System.Text;
using System.Text.Json;
using PD.Core.Settings;
using PD.Core.Styles;

namespace PD.Core.Classification;

/// <summary>
/// The project's effective program list: company legend, optionally replaced by the saved project scheme
/// (with custom programs). Legend entries keep their special styles even when an older save lacks them.
/// </summary>
public static class ProgramCatalog
{
    public const string Unassigned = "UNASSIGNED";

    public static List<ProgramSetting> Effective(IReadOnlyList<ProgramDef> legend, IReadOnlyList<ProgramSetting>? saved)
    {
        if (saved == null) return legend.Select(FromDef).ToList();
        var byKey = legend.ToDictionary(d => d.Key);
        foreach (var s in saved.Where(s => !s.IsCustom && byKey.ContainsKey(s.Key)))
        {
            var d = byKey[s.Key];
            s.Stroke ??= d.Stroke;
            s.StrokePt ??= d.StrokePt;
            s.Dash ??= d.Dash;
            s.Hatch ??= d.Hatch;
            if (s.Aliases.Count == 0) s.Aliases = d.Aliases.ToList();
        }
        return saved.OrderBy(s => s.Order).ToList();
    }

    public static ProgramSetting FromDef(ProgramDef d, int order) => new()
    {
        Key = d.Key, Label = d.Label, Fill = d.Fill, Order = order, Aliases = d.Aliases.ToList(),
        Stroke = d.Stroke, StrokePt = d.StrokePt, Dash = d.Dash, Hatch = d.Hatch,
    };

    public static List<ProgramDef> ToDefs(IEnumerable<ProgramSetting> programs) => programs
        .Select(p => new ProgramDef(p.Key, p.Label, p.Fill, p.Aliases.ToArray(), p.Stroke, p.StrokePt, p.Dash, p.Hatch))
        .ToList();

    /// <summary>Layer auto-fill for this scheme; custom programs are matched first so their aliases beat the legend's.</summary>
    public static LayerClassifier Classifier(IEnumerable<ProgramSetting> programs) =>
        LayerClassifier.WithPrograms(ToDefs(programs.OrderByDescending(p => p.IsCustom).ThenBy(p => p.Order)));

    /// <summary>"Clean Room" → "CLEAN_ROOM"; adds _2, _3 … when taken.</summary>
    public static string MakeKey(string label, IEnumerable<string> existing)
    {
        var sb = new StringBuilder();
        foreach (var c in label.Trim().ToUpperInvariant())
            sb.Append(c is >= 'A' and <= 'Z' or >= '0' and <= '9' ? c : '_');
        var key = string.Join("_", sb.ToString().Split('_', StringSplitOptions.RemoveEmptyEntries));
        if (key.Length == 0) key = "CUSTOM";
        if (char.IsDigit(key[0])) key = "P_" + key;
        var taken = existing.ToHashSet(StringComparer.Ordinal);
        var result = key;
        for (var n = 2; taken.Contains(result); n++) result = $"{key}_{n}";
        return result;
    }

    /// <summary>Adds a custom program at the end of the list, picking the next unused palette colour when none is given.</summary>
    public static ProgramSetting AddCustom(List<ProgramSetting> list, string label, string? fill = null, IEnumerable<string>? aliases = null)
    {
        var used = list.Select(p => p.Fill).Where(f => f != null).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var palette = CustomPalette();
        var p = new ProgramSetting
        {
            Key = MakeKey(label, list.Select(x => x.Key)),
            Label = label.Trim(),
            Fill = fill ?? palette.FirstOrDefault(c => !used.Contains(c)) ?? palette[list.Count(x => x.IsCustom) % palette.Count],
            Order = list.Count == 0 ? 0 : list.Max(x => x.Order) + 1,
            IsCustom = true,
            Aliases = (aliases ?? new[] { label.Trim() }).Where(a => !string.IsNullOrWhiteSpace(a)).Select(a => a.Trim()).ToList(),
        };
        list.Add(p);
        return p;
    }

    /// <summary>Removes a custom program; layers and objects that used it fall back to no program (Unassigned).</summary>
    public static bool RemoveCustom(PdSettings s, string key)
    {
        var p = s.Programs?.FirstOrDefault(x => x.Key == key);
        if (p == null || !p.IsCustom) return false;
        s.Programs!.Remove(p);
        foreach (var l in s.Layers.Values.Where(l => l.ProgramKey == key)) l.ProgramKey = "";
        foreach (var id in s.ObjectPrograms.Where(kv => kv.Value == key).Select(kv => kv.Key).ToList()) s.ObjectPrograms.Remove(id);
        return true;
    }

    public static List<string> CustomPalette()
    {
        using var doc = StyleResources.Read(StyleResources.ProgramLegend);
        return doc.RootElement.TryGetProperty("customPalette", out var a)
            ? a.EnumerateArray().Select(x => x.GetString()!).ToList()
            : new List<string> { "#BFBFBF" };
    }
}
