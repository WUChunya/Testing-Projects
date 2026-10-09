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
    [InlineData("1\" = 40'", 1.8)]
    [InlineData("3/64\" = 1'-0\"", 3.375)]
    [InlineData("1:200", 4.32)]
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
        Assert.All(d.Layer("PD_LABELS").Items.Where(i => i.Name.EndsWith("_LBL") && !i.Name.Contains("CONTEXT")), t => Assert.True(t.Center));
        Assert.NotEmpty(d.Layer("FACADE").Items);
        Assert.Equal(d.Layers.SelectMany(l => l.Items).Count(), d.Layers.SelectMany(l => l.Items).Select(i => i.Name).Distinct().Count());
        // Every item inside the artboard.
        var ab = d.Artboards[0];
        foreach (var it in d.Layers.SelectMany(l => l.Items).Where(i => i.Paths != null))
            Assert.All(it.Paths!.SelectMany(p => p), q => { Assert.InRange(q[0], -1, ab.W + 1); Assert.InRange(q[1], -1, ab.H + 1); });
    }

    [Fact]
    public void SectionA_TrueScaleVisualSlabsAndContextStyles()
    {
        var ctx = Context();
        var d = SectionDrawing.Build(ctx, SectionA);
        var st = ctx.Style.Section;
        Assert.Equal(2.25, ctx.PtPerFt);
        // Fit content: artboard = drawing at true scale + margins (never fitted).
        Assert.Equal("Fit content", ctx.Settings.Output.Artboard);
        // Slabs have the fixed visual weight; consecutive tower slabs are 12'-6" apart at true scale.
        var slabs = d.Layer("PD_SLAB").Items.Select(i => (top: i.Paths![0][0][1], h: i.Paths[0][2][1] - i.Paths[0][0][1])).ToList();
        Assert.All(slabs, s => Assert.Equal(st.SlabPt, s.h, 3));
        var ys = slabs.Select(s => Math.Round(s.top, 2)).Distinct().OrderBy(y => y).ToList();
        Assert.Contains(ys.Zip(ys.Skip(1), (a, b) => b - a), dy => Math.Abs(dy - 12.5 * 2.25) < 0.05);
        // No slab runs through a block (tall spaces stay open; roof slabs only cap their own block).
        foreach (var bar in d.Layer("PD_PROGRAM").Items)
            Assert.DoesNotContain(d.Layer("PD_SLAB").Items, sl =>
                sl.Paths![0][0][1] > bar.Y + 0.5 && sl.Paths[0][2][1] < bar.Y + bar.H - 0.5 &&
                sl.Paths[0][0][0] < bar.X + bar.W - 0.5 && sl.Paths[0][1][0] > bar.X + 0.5);
        // Bars clear the slab by half its weight plus the gap.
        Assert.All(d.Layer("PD_PROGRAM").Items, b => Assert.True(b.H > 0));
        // Behind the cut (ours and others): opaque fill + dashed outline; cut context: solid white with a heavy outline.
        Assert.NotEmpty(d.Layer("PD_BEYOND").Items);
        Assert.All(d.Layer("PD_BEYOND").Items, i => { Assert.Equal("PD_BEYOND_FILL", i.Fill); Assert.NotNull(i.Dash); });
        Assert.All(d.Layer("PD_CONTEXT_CUT").Items, i => { Assert.Equal("PD_CUT_FILL", i.Fill); Assert.Equal(st.ContextCutStrokePt, i.StrokePt); });
        Assert.All(d.Layer("FACADE").Items, i => Assert.Null(i.Fill));
        // Painter's order: layers bottom → top put what is behind under what is cut.
        var order = d.Layers.Select(l => l.Name).ToList();
        Assert.True(order.IndexOf("PD_BEYOND") < order.IndexOf("PD_CONTEXT_CUT") && order.IndexOf("PD_CONTEXT_CUT") < order.IndexOf("PD_PROGRAM"));
        Assert.True(order.IndexOf("PD_CONTEXT_CUT") < order.IndexOf("PD_LEVELS") && order.IndexOf("PD_LEVELS") < order.IndexOf("PD_PROGRAM"));
        // Ground line = 2 × slab weight, centred on grade.
        var ground = d.Layer("PD_GROUND").Items.Single();
        Assert.Equal(st.SlabPt * st.GroundSlabFactor, ground.Paths![0][2][1] - ground.Paths[0][0][1], 3);
        // Level column and height dimension are back.
        Assert.Contains(d.Layer("PD_ANNOTATION").Items, i => i.Name == "SEC_A_LVL_L1_TXT");
        Assert.Contains(d.Layer("PD_ANNOTATION").Items, i => i.Name == "SEC_A_DIM_HEIGHT_TXT");
        _out.WriteLine($"artboard {d.Artboards[0].W}x{d.Artboards[0].H}, beyond {d.Layer("PD_BEYOND").Items.Count}, cut ctx {d.Layer("PD_CONTEXT_CUT").Items.Count}");
    }

    [Theory]
    [InlineData("1\" = 45'-0\"", 1.0)]
    [InlineData("1/32\" = 1'-0\"", 1.40625)]
    [InlineData("1\" = 90'-0\"", 0.5)]
    public void TextScalesWithDrawingScale(string scale, double factor)
    {
        var ctx = Context();
        ctx.Settings.Output.Scale = scale;
        var d = SectionDrawing.Build(ctx, SectionA);
        var label = d.ParaStyles.Single(s => s.Name == "PD Label");
        Assert.Equal(ctx.Style.Fonts.Label.SizePt * factor, label.SizePt, 6);
        Assert.Equal(factor, ctx.TextScale, 6);
    }

    [Fact]
    public void FixedSheet_KeepsScaleAndWarnsWhenTooSmall()
    {
        var ctx = Context();
        ctx.Settings.Output.Artboard = "8.5x11 Landscape";
        ctx.Settings.Output.Scale = "1/8\" = 1'-0\"";
        var d = SectionDrawing.Build(ctx, SectionA);
        Assert.Equal(792, d.Artboards[0].W);
        Assert.Contains(d.Notes, n => n.Contains("larger than"));
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
