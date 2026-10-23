using PD.Core.Cleanup;

namespace PD.Core.Analysis;

/// <param name="Own">A name of its own: the object's name, or a tab 4 name other than the program's label.</param>
public sealed record NameRow(string MassId, string Level, string ProgramKey, string Label, string StableId, bool Own = false);

/// <summary>Tab 4: label = object name, else program label; stable id = L03_LAB_01 (level, program, counter by position).</summary>
public static class Naming
{
    public static List<NameRow> Build(AnalysisResult r, IReadOnlyDictionary<string, string> userNames, Func<string, string> programLabel)
    {
        var rows = new List<NameRow>();
        var baseLevel = r.Spans.ToDictionary(s => s.MassId, s => s.LevelIndices[0]);
        var groups = r.Masses.Where(m => m.IsSolid)
            .GroupBy(m => (lvl: baseLevel.TryGetValue(m.Id, out var i) ? i : -1, key: m.ProgramKey))
            .OrderBy(g => g.Key.lvl).ThenBy(g => g.Key.key, StringComparer.Ordinal);
        foreach (var g in groups)
        {
            var levelName = g.Key.lvl >= 0 ? r.Levels[g.Key.lvl].Name : "XX";
            var n = 0;
            foreach (var m in g.OrderBy(m => Math.Round(m.Source.BBox.MinY)).ThenBy(m => m.Source.BBox.MinX))
            {
                var label = userNames.TryGetValue(m.Id, out var u) && !string.IsNullOrWhiteSpace(u) ? u
                    : !string.IsNullOrWhiteSpace(m.Source.Name) ? m.Source.Name : programLabel(m.ProgramKey);
                var own = !string.IsNullOrWhiteSpace(m.Source.Name)
                          || !string.Equals(label.Trim(), programLabel(m.ProgramKey), StringComparison.OrdinalIgnoreCase);
                rows.Add(new NameRow(m.Id, levelName, m.ProgramKey, label.ToUpperInvariant(), $"{Pad(levelName)}_{m.ProgramKey}_{++n:00}", own));
            }
        }
        return rows;
    }

    /// <summary>"L3" → "L03", "B1" → "B01", "L2M" → "L02M".</summary>
    public static string Pad(string level)
    {
        var digits = new string(level.SkipWhile(c => !char.IsDigit(c)).TakeWhile(char.IsDigit).ToArray());
        if (digits.Length == 0) return level;
        var at = level.IndexOf(digits, StringComparison.Ordinal);
        return level[..at] + int.Parse(digits).ToString("00") + level[(at + digits.Length)..];
    }
}
