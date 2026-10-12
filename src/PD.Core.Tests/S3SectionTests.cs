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

    /// <summary>Elevation from a line 30 ft south of the building, looking north.</summary>
    internal static SectionSetting ElevationSouth
    {
        get
        {
            var f = AnalysisFixture.Result.Frame;
            var (x0, y0, _) = f.ToModel(-20, -30, 0);
            var (x1, y1, _) = f.ToModel(140, -30, 0);
            return new SectionSetting { Name = "S", Kind = SectionSetting.ElevationKind, LookLeft = true, DepthFt = 600, Line = new[] { x0, y0, x1, y1 } };
        }
    }

    internal static ExportContext Context()
    {
        var settings = new PdSettings { Sections = { SectionA, ElevationSouth } };
        settings.Output.FillDefaults(PD.Core.Config.Rules.Default().Output);
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
        // White backing right under the colour blocks hides what is seen beyond through the gaps.
        var bg = Assert.Single(d.Layer("PD_PROGRAM_BG").Items);
        Assert.Equal("#FFFFFF", d.Swatches.Single(x => x.Name == bg.Fill).Hex);
        Assert.True(d.Layers.IndexOf(d.Layer("PD_PROGRAM_BG")) < d.Layers.IndexOf(d.Layer("PD_PROGRAM")));
        Assert.True(d.Layers.IndexOf(d.Layer("PD_BEYOND")) < d.Layers.IndexOf(d.Layer("PD_PROGRAM_BG")));
        Assert.NotEmpty(d.Layer("FACADE").Items);
        Assert.Equal(d.Layers.SelectMany(l => l.Items).Count(), d.Layers.SelectMany(l => l.Items).Select(i => i.Name).Distinct().Count());
        // Every item inside the artboard.
        var ab = d.Artboards[0];
        foreach (var it in d.Layers.SelectMany(l => l.Items).Where(i => i.Paths != null))
            Assert.All(it.Paths!.SelectMany(p => p), q => { Assert.InRange(q[0], -1, ab.W + 1); Assert.InRange(q[1], -1, ab.H + 1); });
    }

    [Fact]
    public void SectionA_ProgramSectionStyle()
    {
        var ctx = Context();
        var d = SectionDrawing.Build(ctx, SectionA);
        var st = ctx.Style.Section;
        // Sections have their own scale (1" = 16'-0") and absolute text sizes, whatever the plan scale.
        Assert.Equal(4.5, ctx.ForSection().PtPerFt, 6);
        Assert.Equal("Fit content", ctx.Settings.Output.Artboard);
        // Slabs: white strips 0.14 × floor with a 1 pt dark outline; tower floors 12'-6" apart at true scale.
        var slabs = d.Layer("PD_SLAB").Items;
        Assert.All(slabs, i => { Assert.Equal("PD_SLAB", i.Fill); Assert.Equal(st.SlabStrokePt, i.StrokePt); });
        Assert.Contains(slabs, i => Math.Abs(i.Paths![0][2][1] - i.Paths[0][0][1] - 0.14 * 12.5 * 4.5) < 0.05);
        var ys = slabs.Select(i => Math.Round((i.Paths![0][0][1] + i.Paths[0][2][1]) / 2, 2)).Distinct().OrderBy(y => y).ToList();
        Assert.Contains(ys.Zip(ys.Skip(1), (a, b) => b - a), dy => Math.Abs(dy - 12.5 * 4.5) < 0.05);
        // Bars: 0.68 × floor tall, no stroke, corner 0.14 × floor; tower bars 38.25 pt tall.
        var bars = d.Layer("PD_PROGRAM").Items;
        Assert.Contains(bars, b => Math.Abs(b.H - 0.68 * 12.5 * 4.5) < 0.05 && Math.Abs(b.R - 0.14 * 12.5 * 4.5) < 0.05);
        Assert.All(bars, b => Assert.Null(b.Stroke));
        // Labels right-aligned on the glyph box, ≤ 20 pt and ≥ 10 pt; one floor number per level, left in the bar.
        var labels = d.Layer("PD_LABELS").Items.Where(i => i.Name.EndsWith("_LBL") && !i.Name.Contains("CONTEXT")).ToList();
        Assert.NotEmpty(labels);
        Assert.All(labels, t => { Assert.Equal("right", t.GlyphAnchor); Assert.InRange(t.SizePt!.Value, st.LabelSizeMin, st.LabelSizeMax); });
        var numbers = d.Layer("PD_LABELS").Items.Where(i => i.Name.EndsWith("_NUM")).ToList();
        Assert.Equal(numbers.Count, numbers.Select(n => n.Text).Distinct().Count());
        Assert.Contains(numbers, n => n.Text == "03" && n.GlyphAnchor == "left");
        var font = d.ParaStyles.Single(x => x.Name == "PD Section Label");
        Assert.Equal("ArialMT", font.Font);
        Assert.Equal(0.85, font.Opacity);
        // Behind the cut: other buildings white, 1 pt #555555 dashed; our building as an opaque grey elevation.
        Assert.All(d.Layer("PD_BEYOND").Items, i => { Assert.Equal("VIEW_CONTEXT", i.Fill); Assert.Equal(new[] { 6.0, 4.0 }, i.Dash); });
        Assert.NotEmpty(d.Layer("VIEW_MASS").Items);
        Assert.All(d.Layer("PD_CONTEXT_CUT").Items, i => { Assert.Equal("PD_CUT_FILL", i.Fill); Assert.Equal(st.ContextCutStrokePt, i.StrokePt); });
        // Layer order: what is behind < level lines (visible in the gaps) < bars < slabs.
        var order = d.Layers.Select(l => l.Name).ToList();
        Assert.True(order.IndexOf("PD_BEYOND") < order.IndexOf("PD_CONTEXT_CUT") && order.IndexOf("PD_PROGRAM_BG") < order.IndexOf("PD_LEVELS"));
        Assert.True(order.IndexOf("PD_LEVELS") < order.IndexOf("PD_PROGRAM") && order.IndexOf("PD_PROGRAM") < order.IndexOf("PD_SLAB"));
        Assert.All(d.Layer("PD_LEVELS").Items, i => Assert.Equal(st.LevelLineStrokePt, i.StrokePt));
        // Ground = 3 pt black line at grade; nothing seen beyond goes below it.
        var ground = d.Layer("PD_GROUND").Items.Single();
        Assert.Equal(st.GroundPt, ground.StrokePt);
        Assert.False(ground.Closed);
        var gradeY = ground.Paths![0][0][1];
        Assert.All(d.Layers.Where(l => l.Name.StartsWith("VIEW_") || l.Name == "PD_BEYOND").SelectMany(l => l.Items).SelectMany(i => i.Paths!).SelectMany(p => p),
            q => Assert.True(q[1] <= gradeY + 0.01, $"beyond point at y={q[1]:0.0} below grade y={gradeY:0.0}"));
        Assert.DoesNotContain(d.Layer("PD_ANNOTATION").Items, i => i.Name == "SEC_A_LVL_L1_TXT");
        _out.WriteLine($"artboard {d.Artboards[0].W}x{d.Artboards[0].H}");
    }

    [Theory]
    [InlineData("1\" = 45'-0\"", 1.0)]
    [InlineData("1/32\" = 1'-0\"", 1.40625)]
    [InlineData("1\" = 90'-0\"", 0.5)]
    public void PlanTextScalesWithDrawingScale(string scale, double factor)
    {
        var ctx = Context();
        ctx.Settings.Output.Scale = scale;
        var d = PD.Core.Plans.PlanDrawing.Build(ctx);
        var label = d.ParaStyles.Single(s => s.Name == "PD Label");
        Assert.Equal(ctx.Style.Fonts.Label.SizePt * factor, label.SizePt, 6);
        Assert.Equal(factor, ctx.TextScale, 6);
        Assert.Equal(1, ctx.ForSection().TextScale);
    }

    [Fact]
    public void FixedSheet_KeepsScaleAndWarnsWhenTooSmall()
    {
        var ctx = Context();
        ctx.Settings.Output.Artboard = "8.5x11 Landscape";
        ctx.Settings.Output.SectionScale = "1/8\" = 1'-0\"";
        var d = SectionDrawing.Build(ctx, SectionA);
        Assert.Equal(792, d.Artboards[0].W);
        Assert.Contains(d.Notes, n => n.Contains("larger than"));
    }

    [Fact]
    public void Export_WritesFolderAndValidJsx()
    {
        var dir = Path.Combine(Path.GetTempPath(), "pd_export_" + Guid.NewGuid().ToString("N"));
        var res = Exporter.Write(Context(), dir);
        foreach (var f in new[] { "program_areas.csv", "program_table.csv", "diagram.json", "export_log.txt", "ProgramDiagram.jsx", "_preview/02_Section_A.svg", "_preview/07_Elevation_S.svg" })
            Assert.True(File.Exists(Path.Combine(dir, f)), f);
        foreach (var f in new[] { "05_Program_Chart.svg", "06_Program_Pie.svg", "01_Plans_PLAN_L1.svg", "01_Plans_PLANS_OVERVIEW.svg", "03_Axon_Stacked.svg", "04_Axon_Exploded.svg", "04_Axon_Exploded_B.svg", "04_Axon_Levels_AXON_L3.svg" })
            Assert.True(File.Exists(Path.Combine(dir, "_preview", f)), f);

        // Golden preview for review (checked in).
        File.Copy(Path.Combine(dir, "_preview", "02_Section_A.svg"), Path.Combine(Golden(), "section_A.svg"), true);
        File.Copy(Path.Combine(dir, "_preview", "07_Elevation_S.svg"), Path.Combine(Golden(), "elevation_S.svg"), true);
        File.Copy(Path.Combine(dir, "_preview", "01_Plans_PLAN_L1.svg"), Path.Combine(Golden(), "plan_L1.svg"), true);
        File.Copy(Path.Combine(dir, "_preview", "03_Axon_Stacked.svg"), Path.Combine(Golden(), "axon_stacked.svg"), true);
        File.Copy(Path.Combine(dir, "_preview", "04_Axon_Exploded.svg"), Path.Combine(Golden(), "axon_exploded.svg"), true);
        File.Copy(Path.Combine(dir, "_preview", "04_Axon_Levels_AXON_L3.svg"), Path.Combine(Golden(), "axon_levels_L3.svg"), true);
        File.Copy(Path.Combine(dir, "_preview", "04_Axon_Exploded_B.svg"), Path.Combine(Golden(), "axon_exploded_B.svg"), true);
        File.Copy(Path.Combine(dir, "_preview", "05_Program_Chart.svg"), Path.Combine(Golden(), "program_chart.svg"), true);
        File.Copy(Path.Combine(dir, "_preview", "06_Program_Pie.svg"), Path.Combine(Golden(), "program_pie.svg"), true);

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

    [Fact]
    public void ElevationSouth_GreyLayersGlassWhiteLevelMarkers()
    {
        var ctx = Context();
        var d = PD.Core.Elevations.ElevationDrawing.Build(ctx, ElevationSouth);
        var layers = d.Layers.Select(l => l.Name).ToList();
        _out.WriteLine(string.Join(", ", layers.Select(n => $"{n}:{d.Layer(n).Items.Count}")));
        Assert.Equal("07_Elevation_S.ai", d.FileName);
        Assert.NotEmpty(d.Layer("VIEW_MASS").Items);
        // A neighbour straddling the view line is not drawn in front of our building.
        var massArea = d.Layer("VIEW_MASS").Items.Sum(i => i.Paths!.Sum(Area));
        Assert.True(massArea > 20000, $"visible mass area {massArea:0} pt²");
        // Each facade/slab source layer is its own AI layer with its own global swatch; glass is solid white.
        Assert.Contains("VIEW_GLASS", layers);
        Assert.Equal("#FFFFFF", d.Swatches.Single(s => s.Name == "VIEW_GLASS").Hex);
        Assert.All(d.Layer("VIEW_GLASS").Items, i => Assert.Equal("VIEW_GLASS", i.Fill));
        var slab = d.Swatches.Single(s => s.Name == "VIEW_SLAB").Hex;
        Assert.NotEqual("#FFFFFF", slab);
        // Stacking: other buildings < masses < slabs < glass; annotations on top.
        Assert.True(layers.IndexOf("VIEW_MASS") < layers.IndexOf("VIEW_SLAB") && layers.IndexOf("VIEW_SLAB") < layers.IndexOf("VIEW_GLASS"));
        Assert.Contains(d.Layer("PD_ANNOTATION").Items, i => i.Name == "ELV_S_LVL_L1_TXT");
        Assert.Contains(d.Layer("PD_LEVELS").Items, i => i.Name == "ELV_S_LVL_L5_MARK");
        Assert.Equal(d.Layers.SelectMany(l => l.Items).Count(), d.Layers.SelectMany(l => l.Items).Select(i => i.Name).Distinct().Count());
        // No glass or element drawn where a nearer mass covers it is left untrimmed (all items have area).
        Assert.All(d.Layers.Where(l => l.Name.StartsWith("VIEW_")).SelectMany(l => l.Items), i => Assert.NotEmpty(i.Paths!));
    }

    static double Area(double[][] ring)
    {
        double a = 0;
        for (var i = 0; i < ring.Length; i++)
        {
            var (p, q) = (ring[i], ring[(i + 1) % ring.Length]);
            a += p[0] * q[1] - q[0] * p[1];
        }
        return Math.Abs(a) / 2;
    }

    [Fact]
    public void SiteSection_CutsEveryNeighbourOnTheLine()
    {
        // East–west line across the whole site: open-polysurface neighbours must still be cut (or at least drawn).
        var r = AnalysisFixture.Result;
        var y = (r.Building.MinY + r.Building.MaxY) / 2;
        var (x0, y0, _) = r.Frame.ToModel(-1500, y, 0);
        var (x1, y1, _) = r.Frame.ToModel(1500, y, 0);
        var sec = new SectionSetting { Name = "GG", Line = new[] { x0, y0, x1, y1 }, DepthFt = 100 };
        var plane = new PD.Core.Geometry.SectionPlane(-1500, y, 1500, y, true);
        var g = SectionGeometry.Build(r, plane, 100, ctxRules(), PD.Core.Config.Style.Default());
        var crossing = r.Local.Objects.Count(o => o.Role == "BG.Building" && o.Mesh != null && !r.Stray.Contains(o.Id)
            && plane.WRange(o.Mesh!) is var w && w.Min < 0 && w.Max > 0 && plane.UOverlaps(o.Mesh!));
        _out.WriteLine($"crossing {crossing}, cut {g.BuildingCuts.Count}");
        Assert.True(crossing >= 4);
        Assert.Equal(crossing, g.BuildingCuts.Count + g.View.Count(v => v.Kind == PD.Core.Views.ViewKind.Context && v.Distance == 0));
        Assert.True(g.BuildingCuts.Count >= crossing - 1);
        Assert.NotNull(SectionDrawing.Build(Context(), sec));
    }

    static PD.Core.Config.Rules ctxRules() => PD.Core.Config.Rules.Default();
}
