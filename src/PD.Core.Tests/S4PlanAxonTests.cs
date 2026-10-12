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
        // Isometric drawing: axes at true length.
        var t = AxonProjection.FromAngles(225, 35.264, trueLength: true);
        Assert.Equal(1, Math.Sqrt(Math.Pow(t.Project(1, 0, 0).X, 2) + Math.Pow(t.Project(1, 0, 0).Y, 2)), 6);
        Assert.Equal(1, t.Project(0, 0, 1).Y, 4);
        Assert.True(p.Depth(0, 0, 0) > p.Depth(10, 10, 0));
        Assert.True(p.Faces((0, -1, 0)) && p.Faces((-1, 0, 0)) && !p.Faces((1, 0, 0)) && p.Faces((0, 0, 1)));
    }

    [Fact]
    public void AxonStacked_SiteAxonLineHierarchy()
    {
        var ctx = S3SectionTests.Context();
        var d = AxonDrawing.Build(ctx, AxonKind.Stacked);
        var st = ctx.Style.Axon.Stacked;
        Assert.Equal("03_Axon_Stacked.ai", d.FileName);
        var prog = d.Layer("PD_PROGRAM").Items;
        Assert.Contains(prog, i => i.Name.Contains("_TOP") && i.Fill == "PRG_LAB");
        // Sides 6 % / 14 % darker; faces outlined by their floor-line colour (35 % darker, 0.5 pt).
        Assert.Equal(AxonDrawing.Darken("#F7A238", st.SideDarken[0]), d.Swatches.Single(s => s.Name == "PRG_LAB_SIDE1_6_14").Hex);
        Assert.Equal(AxonDrawing.Darken("#F7A238", st.FloorLineDarken), d.Swatches.Single(s => s.Name == "PRG_LAB_FLOOR").Hex);
        Assert.All(prog, i => Assert.Equal(st.FloorLinePt, i.StrokePt));
        // Folds 0.75 pt and silhouette 2.5 pt black on their own layer.
        var lines = d.Layer("AXON_LINE").Items;
        Assert.Contains(lines, i => i.Name.EndsWith("FOLDS") && i.StrokePt == st.EdgePt && !i.Closed);
        Assert.Contains(lines, i => i.Name.EndsWith("OUTLINE") && i.StrokePt == st.SilhouettePt);
        // Other buildings white with a 0.75 pt #5A5A5A edge.
        var context = d.Layer("AXON_CONTEXT").Items;
        Assert.NotEmpty(context);
        Assert.All(context, i => { Assert.Equal("AXON_CONTEXT", i.Fill); Assert.Equal(0.75, i.StrokePt); });
        var labels = d.Layer("PD_LABELS").Items.Where(i => i.Matrix != null).ToList();
        Assert.NotEmpty(labels);
        Assert.All(labels, t => Assert.StartsWith("PD Plane Label", t.ParaStyle));
        Assert.Equal("Arial-BoldMT", d.ParaStyles.Single(p => p.Name == "PD Plane Label").Font);
        Assert.Equal(0.7, d.ParaStyles.Single(p => p.Name == "PD Plane Label").Opacity);
        UniqueNamesInsideArtboards(d);
    }

    [Fact]
    public void AxonExploded_A_TopsSeventyPercentVisibleFrontCornerLabels()
    {
        var ctx = S3SectionTests.Context();
        var d = AxonDrawing.Build(ctx, AxonKind.Exploded);
        var r = AnalysisFixture.Result;
        var ax = ctx.Style.Axon;
        Assert.Equal("04_Axon_Exploded.ai", d.FileName);
        var plates = d.Layers.Where(l => l.Name.StartsWith("AXON_L")).ToList();
        Assert.True(plates.Count >= 5, string.Join(",", d.Layers.Select(l => l.Name)));
        Assert.Equal(plates.Select(l => l.Name), plates.OrderBy(l => r.Levels.FindIndex(x => "AXON_" + x.Name.ToUpperInvariant() == l.Name)).Select(l => l.Name));
        // Line hierarchy: plate silhouette 2.0, folds 1.0, white 0.5 divisions on tops, plain sides.
        Assert.All(plates, l =>
        {
            Assert.Single(l.Items, i => i.Name.EndsWith("_OUTLINE") && i.StrokePt == ax.SlabSilhouettePt);
            Assert.Single(l.Items, i => i.Name.EndsWith("_FOLDS") && i.StrokePt == ax.FoldPt);
            var plate = l.Items.Where(i => !i.Name.Contains("_TALL_") && !i.Name.Contains("_CONNECTOR_")).ToList();
            Assert.All(plate.Where(i => i.Name.Contains("_TOP")), i => Assert.Equal(ax.DivisionPt, i.StrokePt));
            Assert.All(plate.Where(i => i.Name.Contains("_SIDE")), i => Assert.Null(i.Stroke));
        });
        // Front-corner labels: left or right aligned on the plane, white 70 %.
        var labels = plates.SelectMany(l => l.Items).Where(i => i.Kind == ItemKind.Text).ToList();
        Assert.NotEmpty(labels);
        Assert.All(labels, t => { Assert.NotNull(t.Matrix); Assert.True(t.Matrix![0] > 0); Assert.Contains(t.Align, new[] { "left", "right", "center" }); });
        Assert.Contains(labels, t => t.Align != "center");
        // Level tags: marker + "Level 1" + leader along the drawing; the tall space at L2 is a translucent connector.
        var anno = d.Layer("PD_ANNOTATION").Items;
        Assert.Equal(plates.Count, anno.Count(i => i.Name.EndsWith("_LDR")));
        Assert.Contains(anno, i => i.Text == "Level 1" && i.ParaStyle == "PD Level");
        Assert.Contains(d.Layers.SelectMany(l => l.Items), i => i.Name.Contains("_TALL_") && i.Opacity == ax.ConnectorOpacity);
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
        Assert.Contains(d.Swatches, s => s.Name == "PRG_CORE_AXON" && s.Hex == "#D9D9D9");
        Assert.Contains(d.Layers.SelectMany(l => l.Items), i => i.ParaStyle == "PD Core Label");
        Assert.Contains(AxonDrawing.Build(ctx, AxonKind.Exploded).Notes, n => n.Contains("not connected"));
        // Exploded B: cores become heavy shafts.
        var b = AxonDrawing.Build(core, AxonKind.ExplodedB);
        Assert.Contains(b.Layers.SelectMany(l => l.Items), i => i.Name.Contains("_SHAFT_") && i.StrokePt == core.Style.Axon.ExplodedB.ShaftPt);
    }

    [Fact]
    public void AxonExploded_B_PlanometricTranslucentBlocksClearPlates()
    {
        var ctx = S3SectionTests.Context();
        var d = AxonDrawing.Build(ctx, AxonKind.ExplodedB);
        var eb = ctx.Style.Axon.ExplodedB;
        Assert.Equal("04_Axon_Exploded_B.ai", d.FileName);
        var plates = d.Layers.Where(l => l.Name.StartsWith("AXON_L")).ToList();
        var faces = plates.SelectMany(l => l.Items).Where(i => i.Name.Contains("_TOP") || i.Name.Contains("_SIDE")).ToList();
        Assert.NotEmpty(faces);
        Assert.All(faces, i => { Assert.Equal(eb.PlateOpacity, i.Opacity); Assert.Equal(eb.EdgePt, i.StrokePt); Assert.Equal("PD_FFFFFF", i.Stroke); });
        Assert.DoesNotContain(plates.SelectMany(l => l.Items), i => i.Name.EndsWith("_OUTLINE"));
        var labels = plates.SelectMany(l => l.Items).Where(i => i.Kind == ItemKind.Text).ToList();
        Assert.NotEmpty(labels);
        Assert.All(labels, t => Assert.StartsWith("PD Plane Label B", t.ParaStyle));
        // Planometric: plan +X runs down-right at 60°, +Y up-right at 30°.
        var p = AxonProjection.Planometric(30);
        var (xx, xy) = p.Project(1, 0, 0);
        var (yx, yy) = p.Project(0, 1, 0);
        Assert.Equal(-60, Math.Atan2(xy, xx) * 180 / Math.PI, 6);
        Assert.Equal(30, Math.Atan2(yy, yx) * 180 / Math.PI, 6);
        Assert.Equal(1, p.Project(0, 0, 1).Y, 6);
        // Level tags "LEVEL 3" (bold caps style), white site plane under the ground floor, the plan printed at 55 %.
        Assert.Contains(d.Layer("PD_ANNOTATION").Items, i => i.Text == "Level 3" && i.ParaStyle == "PD Level Tag");
        Assert.True(d.ParaStyles.Single(s => s.Name == "PD Level Tag").AllCaps);
        Assert.Contains(d.Layer("AXON_SITE").Items, i => i.Name == "AXB_SITE" && i.StrokePt == eb.SitePlaneStrokePt);
        Assert.Contains(d.Layer("AXON_SITE").Items, i => i.Opacity == eb.SitePlanOpacity);
        UniqueNamesInsideArtboards(d);
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
