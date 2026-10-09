using System.Globalization;
using System.Text.RegularExpressions;
using PD.Core.Analysis;
using PD.Core.Classification;
using PD.Core.Config;
using PD.Core.Settings;

namespace PD.Core.Drawing;

/// <summary>Everything a drawing generator needs: analysis, user choices, scheme, style and page setup.</summary>
public sealed class ExportContext
{
    public required AnalysisResult Result { get; init; }
    public required PdSettings Settings { get; init; }
    public required List<ProgramSetting> Programs { get; init; }
    public Style Style { get; init; } = Style.Default();
    public Rules Rules { get; init; } = Rules.Default();

    Dictionary<string, NameRow>? _names;

    /// <summary>Label and stable id per mass (tab ④).</summary>
    public IReadOnlyDictionary<string, NameRow> Names => _names ??=
        Naming.Build(Result, Settings.Names, ProgramLabel).ToDictionary(n => n.MassId);

    public ProgramSetting? Program(string key) => Programs.FirstOrDefault(p => p.Key == key);

    public string ProgramLabel(string key) => Program(key)?.Label ?? key;

    public double PtPerFt => ParseScale(Settings.Output.Scale) ?? ParseScale(Rules.Output.DefaultScale) ?? 2.25;

    public (double W, double H) ArtboardSize =>
        Rules.Output.ArtboardSizesPt.TryGetValue(Settings.Output.Artboard, out var s) ? (s[0], s[1])
        : Rules.Output.ArtboardSizesPt.TryGetValue(Rules.Output.DefaultArtboard, out var d) ? (d[0], d[1])
        : (1224, 792);

    /// <summary>"1/32\" = 1'-0\"" → 2.25 pt per foot; "1\" = 50'-0\"" → 1.44.</summary>
    public static double? ParseScale(string? scale)
    {
        if (scale == null) return null;
        var m = Regex.Match(scale, "^\\s*(\\d+(?:\\.\\d+)?)(?:\\s*/\\s*(\\d+))?\\s*\"\\s*=\\s*(\\d+(?:\\.\\d+)?)\\s*'");
        if (!m.Success) return null;
        var inches = double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
        if (m.Groups[2].Success) inches /= double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
        var feet = double.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture);
        return feet <= 0 ? null : 72.0 * inches / feet;
    }

    /// <summary>Program fill/stroke as swatches (special styles: Shell outline, Void dashed, Unassigned grey).</summary>
    public (string? fill, string? stroke, double strokePt, double[]? dash) ProgramPaint(Drawing d, string key)
    {
        var p = Program(key) ?? Program(ProgramCatalog.Unassigned);
        if (p == null) return (d.Gray("#BFBFBF"), null, 0, null);
        var fill = d.Swatch("PRG_" + p.Key, p.Fill);
        var stroke = p.Stroke == null ? null : d.Swatch("PRG_" + p.Key + "_LINE", p.Stroke);
        return (fill, stroke, p.StrokePt ?? 0, p.Dash);
    }

    /// <summary>Paragraph styles shared by every file.</summary>
    public void AddStandardStyles(Drawing d)
    {
        var f = Style.Fonts;
        var color = d.Gray(Style.Page.LabelColor)!;
        d.ParaStyles.Add(new ParaStyle("PD Label", f.Label.PostScript, f.Label.FallbackPostScript, f.Label.Family, f.Label.SizePt, f.Label.AllCaps, "center", color));
        d.ParaStyles.Add(new ParaStyle("PD Note", f.Label.PostScript, f.Label.FallbackPostScript, f.Label.Family, f.Label.SizePt, f.Label.AllCaps, "left", color));
        d.ParaStyles.Add(new ParaStyle("PD Title", f.Title.PostScript, f.Title.FallbackPostScript, f.Title.Family, f.Title.SizePt, f.Title.AllCaps, "left", color));
    }
}
