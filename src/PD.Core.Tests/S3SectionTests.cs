using System.Diagnostics;
using PD.Core.Classification;
using PD.Core.Drawing;
using PD.Core.Sections;
using PD.Core.Settings;
using PD.Core.Writers;
using Xunit;
using Xunit.Abstractions;

namespace PD.Core.Tests;

public class S3SectionTests
{
    readonly ITestOutputHelper _out;
    public S3SectionTests(ITestOutputHelper output) => _out = output;

    /// <summary>Section A as picked by the user on Windows (model mm), looking left, 500 ft deep.</summary>
    internal static SectionSetting SectionA => new()
    {
        Name = "A", LookLeft = true, DepthFt = 500,
        Line = new[] { -4138907.2863464905, 3565993.778312886, -4138907.2863464905, 3442730.248196322 },
    };

    internal static ExportContext Context()
    {
        var settings = new PdSettings { Sections = { SectionA } };
        settings.Output.FillDefaults(PD.Core.Config.Rules.Default().Output, "");
        return new ExportContext
        {
            Result = AnalysisFixture.Result, Settings = settings,
            Programs = ProgramCatalog.Effective(LayerClassifier.LoadPrograms(), null),
        };
    }

    [Theory]
    [InlineData("1/32\" = 1'-0\"", 2.25)]
    [InlineData("1/16\" = 1'-0\"", 4.5)]
    [InlineData("1\" = 50'-0\"", 1.44)]
    public void ParseScale(string text, double ptPerFt) => Assert.Equal(ptPerFt, ExportContext.ParseScale(text)!.Value, 6);

    [Fact]
    public void SectionA_HasBarsOnEveryLevelSlabsAndOutline()
    {
        var d = SectionDrawing.Build(Context(), SectionA);
        var bars = d.Layer("PD_PROGRAM").Items;
        _out.WriteLine(string.Join("\n", bars.Select(b => $"{b.Name} y={b.Y:0} h={b.H:0} w={b.W:0}")));
        Assert.Equal("02_Section_A.ai", d.FileName);
        Assert.True(bars.Count >= AnalysisFixture.Result.Levels.Count, $"{bars.Count} bars");
        Assert.All(bars, b => Assert.Equal(ItemKind.RoundRect, b.Kind));
        Assert.All(bars, b => Assert.StartsWith("PRG_", b.Fill));
        Assert.NotEmpty(d.Layer("PD_SLAB").Items);
        Assert.Single(d.Layer("PD_OUTLINE").Items, i => i.Name == "SEC_A_OUTLINE");
        Assert.NotEmpty(d.Layer("PD_LABELS").Items);
        Assert.NotEmpty(d.Layer("FACADE").Items);
        Assert.Equal(d.Layers.SelectMany(l => l.Items).Count(), d.Layers.SelectMany(l => l.Items).Select(i => i.Name).Distinct().Count());
        // Every item inside the artboard.
        var ab = d.Artboards[0];
        foreach (var it in d.Layers.SelectMany(l => l.Items).Where(i => i.Paths != null))
            Assert.All(it.Paths!.SelectMany(p => p), q => { Assert.InRange(q[0], -1, ab.W + 1); Assert.InRange(q[1], -1, ab.H + 1); });
    }

    [Fact]
    public void Export_WritesFolderAndValidJsx()
    {
        var dir = Path.Combine(Path.GetTempPath(), "pd_export_" + Guid.NewGuid().ToString("N"));
        var res = Exporter.Write(Context(), dir);
        foreach (var f in new[] { "program_areas.csv", "diagram.json", "export_log.txt", "ProgramDiagram.jsx", "_preview/02_Section_A.svg" })
            Assert.True(File.Exists(Path.Combine(dir, f)), f);
        Assert.Contains(res.Notes, n => n.StartsWith("Plan"));

        // Golden preview for review (checked in).
        File.Copy(Path.Combine(dir, "_preview", "02_Section_A.svg"), Path.Combine(Golden(), "section_A.svg"), true);

        // node --check after stripping the #target directive (ExtendScript-only).
        var js = Path.Combine(dir, "check.js");
        File.WriteAllLines(js, File.ReadAllLines(res.JsxPath).Where(l => !l.StartsWith("#target")));
        var psi = new ProcessStartInfo("node", $"--check \"{js}\"") { RedirectStandardError = true, UseShellExecute = false };
        using var p = Process.Start(psi);
        if (p == null) return; // node not installed: syntax check skipped
        var err = p.StandardError.ReadToEnd();
        p.WaitForExit();
        Assert.True(p.ExitCode == 0, err);
        Directory.Delete(dir, true);
    }

    static string Golden() => Path.Combine(Path.GetDirectoryName(FixtureSmokeTests.FixturePath("x"))!, "..", "golden");
}
