using System.Reflection;
using System.Text.Json;

namespace PD.Core.Styles;

/// <summary>Default style JSON files (styles/*.json) embedded in PD.Core.</summary>
public static class StyleResources
{
    public const string ProgramLegend = "program_legend.json";
    public const string StyleDefault = "style_default.json";
    public const string LayerKeywords = "layer_keywords.json";

    public static string ReadText(string fileName)
    {
        var asm = typeof(StyleResources).Assembly;
        using var s = asm.GetManifestResourceStream("PD.Core.Styles." + fileName)
            ?? throw new FileNotFoundException($"Embedded style '{fileName}' not found");
        using var r = new StreamReader(s);
        return r.ReadToEnd();
    }

    public static JsonDocument Read(string fileName) => JsonDocument.Parse(ReadText(fileName));
}
