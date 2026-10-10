using PD.Core.Axons;
using PD.Core.Drawing;
using PD.Core.Plans;
using Xunit;
using Xunit.Abstractions;

namespace PD.Core.Tests;

public class S4PlanAxonTests
{
    readonly ITestOutputHelper _out;
    public S4PlanAxonTests(ITestOutputHelper output) => _out = output;

    static IEnumerable<DItem> All(PD.Core.Drawing.Drawing d) => d.Layers.SelectMany(l => l.Items);

    static void UniqueNamesInsideArtboards(PD.Core.Drawing.Drawing d)
    {
        Assert.Equal(All(d).Count(), All(d).Select(i => i.Name).Distinct().Count());
        foreach (var it in All(d).Where(i => i.Paths != null))
        {
            var ab = d.Artboards[it.Artboard];
            Assert.All(it.Paths!.SelectMany(p => p), q => { Assert.InRange(q[0], -1, ab.W + 1); Assert.InRange(q[1], -1, ab.H + 1); });
        }
    }

    [Fact]
    public void Plans_OneArtboardPerLevelPlusOverview()
    {
        var ctx = S3SectionTests.Context();
        var d = PlanDrawing.Build(ctx);
        var r = AnalysisFixture.Result;
        _out.WriteLine(string.Join(", ", d.Artboards.Select(a => $"{a.Name} {a.W}x{a.H}")));
        Assert.Equal("01_Plans.ai", d.FileName);
        Assert.Equal("PLANS OVERVIEW", d.Artboards[^1].Name);
        Assert.Equal(r.Plans.Count(p => p.Footprints.Count > 0) + 1, d.Artboards.Count);
        // Every level: colour blocks, a heavy outline, a title, a legend, a scale bar and a north arrow.
        for (var a = 0; a < d.Artboards.Count - 1; a++)
        {
            var on = All(d).Where(i => i.Artboard == a).ToList();
            Assert.Contains(on, i => i.Name.EndsWith("_OUTLINE") && i.StrokePt == ctx.Style.Plan.OutlinePt);
            Assert.Contains(on, i => i.Name.EndsWith("_TITLE"));
            Assert.Contains(on, i => i.Name.Contains("_LEGEND_"));
            Assert.Contains(on, i => i.Name.EndsWith("_SCALEBAR_1"));
            Assert.Contains(on, i => i.Name.EndsWith("_NORTH"));
            Assert.Contains(on, i => i.Fill?.StartsWith("PRG_") == true);
        }
        Assert.Equal(r.Plans.Count(p => p.Footprints.Count > 0), All(d).Count(i => i.Artboard == d.Artboards.Count - 1 && i.Name.EndsWith("_NAME")));
        // Labels: most blocks labelled inside (centred), the rest with a leader.
        var labels = d.Layer("PD_LABELS").Items.Where(i => i.Kind == ItemKind.Text).ToList();
        Assert.True(labels.Count(l => l.Center) * 2 >= labels.Count, $"{labels.Count(l => l.Center)} of {labels.Count} inside");
        Assert.All(d.Layer("PD_LABELS").Items.Where(i => i.Name.EndsWith("_LDR")), l => Assert.False(l.Closed));
        Assert.NotEmpty(d.Layer("BG_CONTEXT").Items);
        Assert.NotEmpty(d.Layer("FACADE").Items);
        UniqueNamesInsideArtboards(d);
    }

    [Theory]
    [InlineData("L1", "Level {n}", "Level 1")]
    [InlineData("L12", "Level {n}", "Level 12")]
    [InlineData("B1", "Level {n}", "B1")]
    [InlineData("ROOF", "Level {n}", "ROOF")]
    public void LevelTitle(string level, string format, string expected) => Assert.Equal(expected, PlanDrawing.Title(format, level));

    [Fact]
    public void PlanLabel_ReadsAlongLongSideAtWidestSpot()
    {
        var f = PD.Core.Geometry.MeshSlicer.Factory;
        var tall = f.CreatePolygon(new[] { new NetTopologySuite.Geometries.Coordinate(0, 0), new(10, 0), new(10, 80), new(0, 80), new(0, 0) });
        var spot = PlanLabels.Fit(tall, "RESEARCH LAB", 1.5);
        Assert.NotNull(spot);
        Assert.Equal(90, spot!.AngleDeg);
        Assert.Equal(5, spot.X, 1);
        Assert.Null(PlanLabels.Fit(f.CreatePolygon(new[] { new NetTopologySuite.Geometries.Coordinate(0, 0), new(2, 0), new(2, 2), new(0, 2), new(0, 0) }), "RESEARCH LAB", 1.5));
    }

    [Fact]
    public void Projection_IsometricFromSouthWest()
    {
        var p = AxonProjection.FromAngles(225, 35.264);
        // +X goes right and up, +Y goes left and up, +Z straight up; south-west corner is nearest.
        var (xx, xy) = p.Project(1, 0, 0);
        var (yx, yy) = p.Project(0, 1, 0);
        var (zx, zy) = p.Project(0, 0, 1);
        Assert.True(xx > 0 && xy > 0 && yx < 0 && yy > 0);
        Assert.Equal(0, zx, 6);
        Assert.True(zy > 0);
        Assert.Equal(Math.Abs(xx), Math.Abs(yx), 6);
        Assert.True(p.Depth(0, 0, 0) > p.Depth(10, 10, 0));
        Assert.True(p.Faces((0, -1, 0)) && p.Faces((-1, 0, 0)) && !p.Faces((1, 0, 0)) && p.Faces((0, 0, 1)));
    }

    [Fact]
    public void AxonStacked_ProgramFacesSidesDarkerOutlineAndVeil()
    {
        var ctx = S3SectionTests.Context();
        var d = AxonDrawing.Build(ctx, exploded: false);
        Assert.Equal("03_Axon_Stacked.ai", d.FileName);
        var prog = d.Layer("PD_PROGRAM").Items;
        Assert.Contains(prog, i => i.Name.Contains("_TOP") && i.Fill == "PRG_LAB");
        Assert.Contains(prog, i => i.Fill == "PRG_LAB_SIDE1" || i.Fill == "PRG_LAB_SIDE2");
        Assert.Equal(AxonDrawing.Darken("#F7A238", 0.1), d.Swatches.Single(s => s.Name == "PRG_LAB_SIDE1").Hex);
        Assert.Single(d.Layer("AXON_OUTLINE").Items, i => i.StrokePt == ctx.Style.Axon.OutlinePt);
        Assert.All(d.Layer("FACADE").Items, i => Assert.Equal(ctx.Style.Facade.StackedAxonOpacity, i.Opacity));
        Assert.NotEmpty(d.Layer("FACADE").Items);
        Assert.NotEmpty(d.Layer("AXON_SITE").Items);
        UniqueNamesInsideArtboards(d);
    }

    [Fact]
    public void AxonExploded_PlatesPerLevelWithPlaneLabelsAndLevelColumn()
    {
        var ctx = S3SectionTests.Context();
        var d = AxonDrawing.Build(ctx, exploded: true);
        var r = AnalysisFixture.Result;
        Assert.Equal("04_Axon_Exploded.ai", d.FileName);
        var plates = d.Layers.Where(l => l.Name.StartsWith("AXON_L")).ToList();
        Assert.True(plates.Count >= 5, string.Join(",", d.Layers.Select(l => l.Name)));
        // Lower plates are painted first (layer order bottom → top).
        Assert.Equal(plates.Select(l => l.Name), plates.OrderBy(l => r.Levels.FindIndex(x => "AXON_" + x.Name.ToUpperInvariant() == l.Name)).Select(l => l.Name));
        Assert.All(plates, l => Assert.Contains(l.Items, i => i.Name.EndsWith("SLAB_TOP")));
        var labels = plates.SelectMany(l => l.Items).Where(i => i.Kind == ItemKind.Text).ToList();
        Assert.NotEmpty(labels);
        Assert.All(labels, t => { Assert.NotNull(t.Matrix); Assert.True(t.Matrix![0] > 0); });
        Assert.Equal(plates.Count, d.Layer("PD_ANNOTATION").Items.Count(i => i.Name.EndsWith("_LDR")));
        Assert.Empty(d.Layers.Where(l => l.Name == "FACADE").SelectMany(l => l.Items));
        UniqueNamesInsideArtboards(d);
    }
}
