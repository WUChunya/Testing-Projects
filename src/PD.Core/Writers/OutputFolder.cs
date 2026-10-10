using System.Text.RegularExpressions;

namespace PD.Core.Writers;

/// <summary>
/// PRD 5.0: every export goes to a new sub-folder of the save location (default: the Rhino model's folder), named from
/// rules "folderPattern", e.g. "2026 1010_Conceptual Program Diagram_143205".
/// </summary>
public static class OutputFolder
{
    /// <summary>Tokens: {yyyy} {MM} {dd} {MMdd} {HH} {mm} {ss} {HHmmss} {project} {scheme}.</summary>
    public static string Name(string pattern, DateTime now, string project = "", string scheme = "")
    {
        var name = Regex.Replace(pattern, "\\{(\\w+)\\}", m => m.Groups[1].Value switch
        {
            "project" => project,
            "scheme" => scheme,
            "date" => now.ToString("yyyyMMdd"),
            var f when Regex.IsMatch(f, "^[yMdHms]+$") => now.ToString(f),
            var other => m.Value,
        });
        return string.Concat(name.Split(Path.GetInvalidFileNameChars())).Trim();
    }

    /// <summary>Save location: the user's folder, else the model's folder, else Documents.</summary>
    public static string SaveIn(string? userFolder, string? modelPath) =>
        !string.IsNullOrWhiteSpace(userFolder) ? userFolder!
        : !string.IsNullOrEmpty(modelPath) ? Path.GetDirectoryName(modelPath)!
        : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

    public static string Create(string saveIn, string pattern, DateTime now, string project = "", string scheme = "") =>
        Path.Combine(saveIn, Name(pattern, now, project, scheme));
}
