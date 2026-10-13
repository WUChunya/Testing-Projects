using PD.Core.Charts;
using PD.Core.Drawing;
using PD.Core.Entourage;
using PD.Core.Plans;
using PD.Core.Sections;
using Xunit;
using Xunit.Abstractions;

namespace PD.Core.Tests;

/// <summary>S5: program chart (PRD 5.5), donut pie (5.6), Excel table and entourage (§4).</summary>
public class S5ChartEntourageTests
{
    readonly ITestOutputHelper _out;
    public S5ChartEntourageTests(ITestOutputHelper output) => _out = output;

    static IEnumerable<DItem> All(PD.Core.Drawing.Drawing d) => d.Layers.SelectMany(l => l.Items);

    [Fact]
    public void ProgramTable_FromModelMatchesTheAreaReport()
    {
        var ctx = S3SectionTests.Context();
        var t = ProgramTable.FromModel(ctx);
        Assert.Equal("GSF", t.Unit);
        Assert.NotEmpty(t.Rows);
        var fromPlans = ctx.Result.Plans.Sum(p => p.AreaByProgram.Values.Sum());
        Assert.Equal(fromPlans, t.Total, 1);
        // Every row carries a program of the scheme, so the chart can colour it.
        Assert.All(t.Rows, r => Assert.Contains(ctx.Programs, p => p.Key == r.ProgramKey));
    }

    [Fact]
    public void ExcelTemplateRoundTrips()
    {
        var ctx = S3SectionTests.Context();
        var path = Path.Combine(Path.GetTempPath(), "pd_program_" + Guid.NewGuid().ToString("N") + ".xlsx");
        ProgramTable.WriteTemplate(path, ctx.Programs);
        var t = ProgramTable.FromExcel(path, PD.Core.Classification.ProgramCatalog.Classifier(ctx.Programs));
        var row = Assert.Single(t.Rows);
        Assert.Equal("RESEARCH LABS", row.Department);
        Assert.Equal("LAB", row.ProgramKey);
        Assert.Equal(8, row.Qty);
        Assert.Equal(6, row.Multiplier);
        Assert.Equal(8 * 420 * 6, row.Area);
        Assert.Equal("ASF", t.Unit);
        File.Delete(path);
    }

    [Fact]
    public void ProgramChart_BoxPerDepartmentWithScaledRectangles()
    {
        var ctx = S3SectionTests.Context();
        var d = ProgramChartDrawing.Build(ctx);
        Assert.Equal("05_Program_Chart.ai", d.FileName);
        var ab = Assert.Single(d.Artboards);
        _out.WriteLine($"{ab.Name} {ab.W}x{ab.H}, {All(d).Count()} items");
        var boxes = d.Layer("PD_CHART_BOX").Items;
        var departments = ProgramTable.FromModel(ctx).Rows.Select(r => r.Department).Distinct().Count();
        Assert.Equal(departments, boxes.Count);
        Assert.All(boxes, b => Assert.Equal(ctx.Style.Charts.ProgramChart.BoxDash, b.Dash));
        Assert.Contains(All(d), i => i.Name.EndsWith("_TOTAL") && i.Kind == ItemKind.Text);
        Assert.All(d.Layer("PD_CHART_BLOCKS").Items, i => Assert.StartsWith("PRG_", i.Fill));
        // Everything stays on the sheet and keeps a unique name (in-place update in Illustrator).
        Assert.Equal(All(d).Count(), All(d).Select(i => i.Name).Distinct().Count());
        foreach (var it in All(d).Where(i => i.Paths != null))
            Assert.All(it.Paths!.SelectMany(q => q), q => { Assert.InRange(q[0], -1, ab.W + 1); Assert.InRange(q[1], -1, ab.H + 1); });
    }

    [Fact]
    public void Pie_DonutInSchemeOrderWithLegendAndTotal()
    {
        var ctx = S3SectionTests.Context();
        var d = PieDrawing.Build(ctx);
        var cs = ctx.Style.Charts.Pie;
        Assert.Equal("06_Program_Pie.ai", d.FileName);
        var shares = ProgramTable.FromModel(ctx).ByProgram(ctx.Programs);
        var segments = d.Layer("PD_PIE").Items.Where(i => i.Name.StartsWith("PIE_")).ToList();
        Assert.Equal(shares.Count, segments.Count);
        Assert.Equal(shares.Select(s => "PIE_" + s.Key), segments.Select(s => s.Name));

        // Clockwise from 12 o'clock, and the inner radius is 43 % of the outer.
        double cx = cs.MarginPt + cs.OuterPt / 2, cy = cx;
        var first = segments[0].Paths![0];
        Assert.Equal(cx, first[0][0], 1);
        Assert.Equal(cy - cs.OuterPt / 2, first[0][1], 1);
        var radii = first.Select(q => Math.Sqrt(Math.Pow(q[0] - cx, 2) + Math.Pow(q[1] - cy, 2))).ToList();
        Assert.Equal(cs.OuterPt / 2 * cs.InnerRatio, radii.Min(), 1);
        Assert.True(first[1][0] > cx, "the first segment must run clockwise");

        Assert.Contains(All(d), i => i.Name == "PIE_CENTER" && i.Text == cs.CenterText);
        Assert.Contains(All(d), i => i.Name == "PIE_TOTAL");
        Assert.Equal(shares.Count, d.Layer("PD_PIE_LEGEND").Items.Count(i => i.Name.StartsWith("PIE_LEG_") && i.Kind == ItemKind.Path));
        Assert.Equal(shares.Count, All(d).Count(i => i.Name.StartsWith("BAR_") && i.Kind == ItemKind.Path));
    }

    [Fact]
    public void Entourage_SameSeedSameLayoutAndNoneInCores()
    {
        var ctx = S3SectionTests.Context();
        var a = PlanDrawing.BuildSite(ctx);
        var b = PlanDrawing.BuildSite(ctx);
        var pa = a.Layer(EntouragePlacer.Layer).Items;
        var pb = b.Layer(EntouragePlacer.Layer).Items;
        Assert.NotEmpty(pa);
        Assert.Equal(pa.Count, pb.Count);
        for (var i = 0; i < pa.Count; i++)
            Assert.Equal((pa[i].X, pa[i].Y, pa[i].Symbol), (pb[i].X, pb[i].Y, pb[i].Symbol));

        // A different seed moves them.
        ctx.Settings.Output.EntourageSeed = EntouragePlacer.Seed(ctx) + 1;
        var moved = PlanDrawing.BuildSite(ctx).Layer(EntouragePlacer.Layer).Items;
        Assert.NotEqual((pa[0].X, pa[0].Y), (moved[0].X, moved[0].Y));
        ctx.Settings.Output.EntourageSeed = null;

        // Density 0 programs (Core, Stair, Shell, Mechanical) stay empty.
        foreach (var key in new[] { "CORE", "STAIR", "SHELL", "BUILDING_SUPPORT" })
            Assert.Equal(0, ctx.Style.Entourage.Density(key));
        var plan = ctx.Result.Plans.First(p => p.Footprints.Count > 0);
        var core = plan.Footprints.FirstOrDefault(f => f.Mass.ProgramKey == "CORE");
        if (core != null) Assert.Empty(EntouragePlacer.InProgram(ctx, core.Resolved, "CORE", "x"));
    }

    [Fact]
    public void Entourage_SectionPeopleStandOnFloorsAndFitTheirBar()
    {
        var ctx = S3SectionTests.Context();
        var d = SectionDrawing.Build(ctx, S3SectionTests.SectionA);
        var people = d.Layer(EntouragePlacer.Layer).Items;
        Assert.NotEmpty(people);
        var ab = d.Artboards[0];
        foreach (var p in people)
        {
            // Office symbol, never taller than 5'-9" at the section scale, feet inside the artboard.
            Assert.InRange(p.Matrix![3] * EntourageArt.UnitPt, 1, ctx.ForSection().PtPerFt * ctx.Style.Entourage.HeightFt + 0.5);
            Assert.InRange(p.X, -1, ab.W + 1);
            Assert.InRange(p.Y, -1, ab.H + 1);
        }
        Assert.All(people, p => Assert.StartsWith("PD_PERSON_P", p.Symbol));
        Assert.All(d.Symbols.Where(s => s.Name.StartsWith("PD_PERSON")), s => Assert.Equal("PD_PEOPLE", s.Fill));
    }

    [Fact]
    public void Entourage_CanBeTurnedOff()
    {
        var ctx = S3SectionTests.Context();
        ctx.Settings.Output.Entourage = false;
        Assert.Empty(PlanDrawing.BuildSite(ctx).Layer(EntouragePlacer.Layer).Items);
        Assert.Empty(SectionDrawing.Build(ctx, S3SectionTests.SectionA).Layer(EntouragePlacer.Layer).Items);
        ctx.Settings.Output.Entourage = null;
    }

    /// <summary>A section through the Downtown tower: people stand on the floors and the site trees come with it.</summary>
    [Fact]
    public void Downtown_SectionHasPeopleAndTrees()
    {
        var ctx = DowntownAnalysis.Context();
        var b = ctx.Result.Building;
        // East-west cut through the middle of the building, looking north, deep enough to catch the site trees.
        var (x0, y0, _) = ctx.Result.Frame.ToModel(b.MinX - 40, (b.MinY + b.MaxY) / 2, 0);
        var (x1, y1, _) = ctx.Result.Frame.ToModel(b.MaxX + 40, (b.MinY + b.MaxY) / 2, 0);
        ctx.Settings.Sections.Add(new PD.Core.Settings.SectionSetting
        {
            Name = "DT", Line = new[] { x0, y0, x1, y1 }, LookLeft = true, DepthFt = 400,
        });
        var d = SectionDrawing.Build(ctx, ctx.Settings.Sections[0]);
        _out.WriteLine($"bars people={d.Layer(EntouragePlacer.Layer).Items.Count} trees={d.Layer("BG_TREE").Items.Count}");
        Assert.NotEmpty(d.Layer(EntouragePlacer.Layer).Items);
        Assert.NotEmpty(d.Layer("BG_TREE").Items);
        // Office tree silhouettes (TREES.ai) as symbols, at the entourage tree opacity.
        Assert.All(d.Layer("BG_TREE").Items, i => Assert.StartsWith("PD_TREE_T", i.Symbol));
        Assert.All(d.Layer("BG_TREE").Items, i => Assert.Equal(ctx.Style.Entourage.TreeOpacity, i.Opacity));
    }

    /// <summary>The Downtown model carries its own people (role Entourage); those are used instead of generated ones.</summary>
    [Fact]
    public void Entourage_ModelledPeopleAreRead()
    {
        var r = DowntownAnalysis.Result;
        var ctx = DowntownAnalysis.Context();
        var env = new NetTopologySuite.Geometries.Envelope(-1e9, 1e9, -1e9, 1e9);
        Assert.Equal(155, EntouragePlacer.Modelled(ctx, env).Count);
        Assert.Equal(155, r.Local.Objects.Count(o => o.Role == PD.Core.Classification.Roles.Entourage && o.Mesh != null));
    }

    /// <summary>Office people (ENTOURAGES.ai) are placed as AI symbols, every variant a library figure, mirrored on request.</summary>
    [Fact]
    public void Entourage_StandingPeopleAreOfficeSymbols()
    {
        var d = new PD.Core.Drawing.Drawing();
        var e = new PD.Core.Config.EntourageStyle();
        Assert.Equal(39, EntourageSymbols.Variants);
        for (var v = 0; v < EntourageSymbols.Variants; v++)
            foreach (var mirror in new[] { false, true })
            {
                var it = EntourageSymbols.Standing(d, e, "P", 0, 0, 10, v, mirror);
                Assert.Equal(PD.Core.Drawing.ItemKind.Symbol, it.Kind);
                Assert.Equal(mirror ? -0.1 : 0.1, it.Matrix![0], 6);
                Assert.Equal(0.1, it.Matrix[3], 6);
                Assert.Contains(d.Symbols, s => s.Name == it.Symbol);
            }
        Assert.Equal(39, d.Symbols.Count);
    }

    /// <summary>The library holds the office figures normalised to height 1 with the feet at the origin.</summary>
    [Fact]
    public void Entourage_LibraryFromOfficeFiles()
    {
        var lib = EntourageLibrary.Default;
        Assert.Equal(39, lib.People.Count);
        Assert.Equal(19, lib.Trees.Count);
        foreach (var f in lib.People.Concat(lib.Trees))
        {
            var pts = f.Parts.SelectMany(p => p).SelectMany(r => r).ToList();
            Assert.InRange(pts.Min(q => q[1]), -0.01, 0.01);
            Assert.InRange(pts.Max(q => q[1]), 0.99, 1.01);
        }
        var d = new PD.Core.Drawing.Drawing();
        var tree = EntourageSymbols.Tree(d, new PD.Core.Config.EntourageStyle { TreeFills = { "#9CC078" } }, "T", 0, 0, 100, new Random(1));
        Assert.Matches("^PD_TREE_T\\d\\d_9CC078$", tree.Symbol!);
        var svg = PD.Core.Writers.SvgWriter.Write(WithArtboard(d, tree));
        Assert.Contains("<use id=\"T\" href=\"#sym_PD_TREE_", svg);
    }

    static PD.Core.Drawing.Drawing WithArtboard(PD.Core.Drawing.Drawing d, PD.Core.Drawing.DItem item)
    {
        d.Artboards.Add(new PD.Core.Drawing.Artboard("A", 200, 200));
        d.Layer("ENTOURAGE").Items.Add(item);
        return d;
    }
}
