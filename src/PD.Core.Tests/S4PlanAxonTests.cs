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
    public void AxonStacked_ThinOutlinesWhiteContextAndPlaneLabels()
    {
        var ctx = S3SectionTests.Context();
        var d = AxonDrawing.Build(ctx, AxonKind.Stacked);
        Assert.Equal("03_Axon_Stacked.ai", d.FileName);
        var prog = d.Layer("PD_PROGRAM").Items;
        Assert.Contains(prog, i => i.Name.Contains("_TOP") && i.Fill == "PRG_LAB");
        Assert.Contains(prog, i => i.Fill == "PRG_LAB_SIDE1" || i.Fill == "PRG_LAB_SIDE2");
        Assert.Equal(AxonDrawing.Darken("#F7A238", ctx.Style.Axon.SideDarken[0]), d.Swatches.Single(s => s.Name == "PRG_LAB_SIDE1").Hex);
        Assert.All(prog, i => Assert.Equal(ctx.Style.Axon.EdgePt, i.StrokePt));
        var outline = Assert.Single(d.Layer("AXON_OUTLINE").Items);
        Assert.Equal(ctx.Style.Axon.SilhouettePt, outline.StrokePt);
        Assert.True(ctx.Style.Axon.SilhouettePt <= 1.5, "line hierarchy: no heavy silhouette");
        // Other buildings: white prisms with grey edges, hidden faces removed together with ours.
        var context = d.Layer("AXON_CONTEXT").Items;
        Assert.NotEmpty(context);
        Assert.All(context, i => Assert.Equal("AXON_CONTEXT", i.Fill));
        var labels = d.Layer("PD_LABELS").Items;
        Assert.NotEmpty(labels);
        Assert.All(labels, t => { Assert.NotNull(t.Matrix); Assert.StartsWith("PD Plane Label", t.ParaStyle); });
        Assert.Empty(d.Layers.Where(l => l.Name == "FACADE").SelectMany(l => l.Items));
        UniqueNamesInsideArtboards(d);
    }

    [Fact]
    public void AxonExploded_PlatesClearOfEachOtherWithLevelColumn()
    {
        var ctx = S3SectionTests.Context();
        var d = AxonDrawing.Build(ctx, AxonKind.Exploded);
        var r = AnalysisFixture.Result;
        Assert.Equal("04_Axon_Exploded.ai", d.FileName);
        var plates = d.Layers.Where(l => l.Name.StartsWith("AXON_L")).ToList();
        Assert.True(plates.Count >= 5, string.Join(",", d.Layers.Select(l => l.Name)));
        Assert.Equal(plates.Select(l => l.Name), plates.OrderBy(l => r.Levels.FindIndex(x => "AXON_" + x.Name.ToUpperInvariant() == l.Name)).Select(l => l.Name));
        // No plate hides another: plate outlines (page bounding boxes) are disjoint up the stack.
        var outlines = plates.Select(l => l.Items.Single(i => i.Name.EndsWith("_OUTLINE"))).ToList();
        var f = PD.Core.Geometry.MeshSlicer.Factory;
        NetTopologySuite.Geometries.Geometry Poly(DItem i) => f.BuildGeometry(i.Paths!.Select(p =>
            (NetTopologySuite.Geometries.Geometry)f.CreatePolygon(p.Append(p[0]).Select(q => new NetTopologySuite.Geometries.Coordinate(q[0], q[1])).ToArray()).Buffer(0))).Union();
        // A tall space stretches from its plate up to the next one, so that pair touches by design.
        var tallBase = r.Spans.Where(x => x.Mode == PD.Core.Cleanup.SpanMode.KeepTall).Select(x => "AXON_" + r.Levels[x.LevelIndices[0]].Name.ToUpperInvariant()).ToHashSet();
        for (var n = 1; n < outlines.Count; n++)
            if (!tallBase.Contains(plates[n - 1].Name))
                Assert.True(Poly(outlines[n]).Intersection(Poly(outlines[n - 1])).Area < 1, $"{plates[n].Name} overlaps {plates[n - 1].Name}");
        var labels = plates.SelectMany(l => l.Items).Where(i => i.Kind == ItemKind.Text).ToList();
        Assert.NotEmpty(labels);
        Assert.All(labels, t => { Assert.NotNull(t.Matrix); Assert.True(t.Matrix![0] > 0); });
        var anno = d.Layer("PD_ANNOTATION").Items;
        Assert.Equal(plates.Count, anno.Count(i => i.Name.EndsWith("_LDR")));
        Assert.Equal(plates.Count, anno.Count(i => i.Name.EndsWith("_MARK")));
        Assert.Contains(anno, i => i.Text == "Level 1" && i.ParaStyle == "PD Level");
        UniqueNamesInsideArtboards(d);
    }

    [Fact]
    public void AxonExploded_CoresConnectPlates()
    {
        // Treat the stacked LAB masses as the core: consecutive LAB plates must be joined.
        var model = PD.Core.IO.FixtureReader.Load(FixtureSmokeTests.FixturePath("template.json"));
        var labs = AnalysisFixture.Result.Masses.Where(m => m.ProgramKey == "LAB").ToDictionary(m => m.Id, _ => "CORE");
        var res = PD.Core.Analysis.Analyzer.Run(new PD.Core.Analysis.AnalysisInput { Model = model, ProgramOverrides = labs });
        var ctx = S3SectionTests.Context();
        var core = new ExportContext { Result = res, Settings = ctx.Settings, Programs = ctx.Programs };
        var d = AxonDrawing.Build(core, AxonKind.Exploded);
        var links = d.Layers.SelectMany(l => l.Items).Where(i => i.Name.Contains("_CONNECTOR")).ToList();
        Assert.NotEmpty(links);
        Assert.All(links, i => Assert.Equal(core.Style.Axon.ConnectorOpacity, i.Opacity));
        Assert.DoesNotContain(d.Notes, n => n.Contains("not connected"));
        Assert.Contains(AxonDrawing.Build(ctx, AxonKind.Exploded).Notes, n => n.Contains("not connected"));
    }

    [Fact]
    public void AxonLevels_OneArtboardPerLevelWithDashedVolume()
    {
        var ctx = S3SectionTests.Context();
        var d = AxonDrawing.Build(ctx, AxonKind.Levels);
        Assert.Equal("04_Axon_Levels.ai", d.FileName);
        var levels = ProgramBlocksLevels();
        Assert.Equal(levels, d.Artboards.Count);
        for (var a = 0; a < d.Artboards.Count; a++)
        {
            var on = d.Layers.SelectMany(l => l.Items).Where(i => i.Artboard == a).ToList();
            Assert.Contains(on, i => i.Fill?.StartsWith("PRG_") == true);
            Assert.Contains(on, i => i.Name.Contains("GHOST") && i.Dash != null && !i.Closed);
            Assert.Single(on, i => i.Name.EndsWith("_LDR"));
        }
        Assert.True(d.Layers.FindIndex(l => l.Name == "AXON_GHOST_BELOW") < d.Layers.FindIndex(l => l.Name == "PD_PROGRAM"));
        Assert.True(d.Layers.FindIndex(l => l.Name == "AXON_GHOST_ABOVE") > d.Layers.FindIndex(l => l.Name == "PD_PROGRAM"));
        UniqueNamesInsideArtboards(d);
    }

    static int ProgramBlocksLevels() => PD.Core.Analysis.ProgramBlocks.Build(AnalysisFixture.Result).Select(b => b.LevelIndex).Distinct().Count();

    [Fact]
    public void Artboards_LaidOutInRowsInsideTheCanvas()
    {
        var d = PlanDrawing.Build(S3SectionTests.Context());
        var (w, h) = d.LayoutArtboards();
        Assert.True(w <= 16000 && h <= 16000, $"{w} x {h}");
        Assert.True(d.Artboards.Select(a => a.Y).Distinct().Count() > 1, "wraps into rows");
        for (var i = 0; i < d.Artboards.Count; i++)
        for (var j = i + 1; j < d.Artboards.Count; j++)
        {
            var (a, b) = (d.Artboards[i], d.Artboards[j]);
            Assert.False(a.X < b.X + b.W && b.X < a.X + a.W && a.Y < b.Y + b.H && b.Y < a.Y + a.H, $"{a.Name} overlaps {b.Name}");
        }
    }

    [Fact]
    public void LabelStyle_WhiteOnDarkFills()
    {
        var ctx = S3SectionTests.Context();
        var d = new PD.Core.Drawing.Drawing();
        Assert.Equal("PD Label Light", ctx.LabelStyle(d, d.Swatch("X", "#5A3E8C")));
        Assert.Equal("PD Label", ctx.LabelStyle(d, d.Swatch("Y", "#FDF5D1")));
    }
}
