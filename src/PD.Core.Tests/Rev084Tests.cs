using NetTopologySuite.Geometries;
using PD.Core.Analysis;
using PD.Core.Axons;
using PD.Core.Classification;
using PD.Core.Model;
using PD.Core.Plans;
using Xunit;

namespace PD.Core.Tests;

/// <summary>User feedback on v0.8.3 (Windows export of the Downtown model, their own settings: tests/fixtures/downtown_settings.json).</summary>
public class Rev084Tests
{
    static IEnumerable<double[]> Points(PD.Core.Drawing.DItem i) => i.Paths!.SelectMany(p => p);
    static Envelope Env(PD.Core.Drawing.DItem i)
    {
        var e = new Envelope();
        foreach (var q in Points(i)) e.ExpandToInclude(q[0], q[1]);
        return e;
    }

    /// <summary>Downtown with our property line (NC.PropertyLine): a closed polyline 20 ft around the building.</summary>
    static readonly Lazy<AnalysisResult> WithProperty = new(() =>
    {
        var m = DowntownAnalysis.Model;
        var layers = m.Layers.ToList();
        layers.Add(new LayerInfo(layers.Max(l => l.Index) + 1, "PROPERTY LINE", true, "#FF0000"));
        var b = DowntownAnalysis.Result.Building;
        var f = DowntownAnalysis.Result.Frame;
        var corners = new[] { (b.MinX - 20, b.MinY - 20), (b.MaxX + 20, b.MinY - 20), (b.MaxX + 20, b.MaxY + 20), (b.MinX - 20, b.MaxY + 20), (b.MinX - 20, b.MinY - 20) }
            .Select(c => f.ToModel(c.Item1, c.Item2, 0)).Select(c => new[] { c.x, c.y, 0.0 }).ToArray();
        // The model's own property line (since the 0.8.5 fixture) is left out: only ours, 20 ft out.
        var objs = m.Objects.Where(o => !o.LayerPath.Equals("property line", StringComparison.OrdinalIgnoreCase)).ToList();
        objs.Add(new SourceObject
        {
            Id = "property", LayerPath = "PROPERTY LINE", GeometryType = "PolylineCurve", IsClosed = true,
            BBox = new BBox3(corners.Min(c => c[0]), corners.Min(c => c[1]), 0, corners.Max(c => c[0]), corners.Max(c => c[1]), 0), Points = corners,
        });
        return Analyzer.Run(new AnalysisInput { Model = new SourceModel { Units = m.Units, Layers = layers, Objects = objs } });
    });

    /// <summary>Downtown without any property line (the default 04 site around the building).</summary>
    static readonly Lazy<AnalysisResult> WithoutProperty = new(() =>
    {
        var m = DowntownAnalysis.Model;
        return Analyzer.Run(new AnalysisInput { Model = new SourceModel { Units = m.Units, Layers = m.Layers,
            Objects = m.Objects.Where(o => !o.LayerPath.Equals("property line", StringComparison.OrdinalIgnoreCase)).ToList() } });
    });

    static PD.Core.Drawing.ExportContext PropertyContext()
    {
        var c = DowntownAnalysis.Context();
        return new PD.Core.Drawing.ExportContext { Result = WithProperty.Value, Settings = c.Settings, Programs = c.Programs };
    }

    // ------------------------------------------------------------------ model

    /// <summary>The user's section lines sit on PROGRAM::Break Room: curves never widen the building.</summary>
    [Fact]
    public void Building_IgnoresCurvesOnProgramLayers()
    {
        var b = DowntownAnalysis.UserResult.Building;
        Assert.InRange(b.MaxX - b.MinX, 250, 320);
        Assert.InRange(b.MaxY - b.MinY, 250, 320);
    }

    // ------------------------------------------------------------------ (1) context buildings like the site axon

    [Fact]
    public void SitePlan_ContextBuildingsOutlinedLikeTheSiteAxon()
    {
        var ctx = DowntownAnalysis.UserContext();
        var d = PlanDrawing.BuildSite(ctx);
        var ctxItems = d.Layer("BG_CONTEXT").Items.Where(i => i.Artboard == 0).ToList();
        Assert.NotEmpty(ctxItems);
        var axonPaint = PlanBackground.PaintOf(ctx.Style, Roles.Building, "axon");
        Assert.All(ctxItems, i => Assert.Equal(axonPaint.Stroke, d.Swatches.Single(s => s.Name == i.Stroke).Hex));
        Assert.Empty(d.Layer("BG_CONTEXT_SHADOW").Items);
    }

    // ------------------------------------------------------------------ (2) open slabs / glass

    [Fact]
    public void Exposed_SlabRoofAndGlassCanopy_InPlanAndAxon_NoLabel()
    {
        var ctx = DowntownAnalysis.UserContext();
        var exposed = ExposedSurfaces.For(ctx);
        Assert.Contains(exposed, e => !e.IsGlass);
        Assert.Contains(exposed, e => e.IsGlass);
        Assert.DoesNotContain(exposed, e => ctx.Result.Levels[e.LevelIndex].Elevation <= ctx.Rules.Levels.GroundElevationFt + 1);
        var plans = PlanDrawing.Build(ctx);
        var items = plans.Layer("PD_PROGRAM").Items;
        Assert.Contains(items, i => i.Name.Contains("EXPOSED_ROOF"));
        Assert.Contains(items, i => i.Name.Contains("EXPOSED_GLASS") && i.Name.EndsWith("_HATCH") && i.Closed == false);
        Assert.DoesNotContain(plans.Layer("PD_LABELS").Items, i => i.Name.Contains("EXPOSED"));
        var ax = AxonDrawing.Build(ctx, AxonKind.Stacked);
        Assert.Contains(ax.Layer("PD_PROGRAM").Items, i => i.Name.Contains("EXPOSED_ROOF"));
        Assert.Contains(ax.Layer("PD_PROGRAM").Items, i => i.Name.Contains("EXPOSED_GLASS") && i.Name.EndsWith("_HATCH"));
    }

    // ------------------------------------------------------------------ (3) screen box: no diagonal wall

    [Fact]
    public void Screen_ClosedBoxGivesFourWallsNoDiagonal()
    {
        var r = DowntownAnalysis.UserResult;
        var e = Assert.Single(ScreenEnclosures.Find(r, 0));
        Assert.True(e.Encloses);
        // Four straight walls: the wall band is thin compared with the floor, and the floor's middle is open.
        Assert.True(e.Walls.Area < 0.1 * e.Floor.Area, $"walls {e.Walls.Area:0} vs floor {e.Floor.Area:0}");
        Assert.False(e.Walls.Contains(e.Floor.Centroid));
    }

    // ------------------------------------------------------------------ (4) 03 line weights, tree clearance

    [Fact]
    public void Axon03_LevelLinesHalfPoint_TreesStopShortOfTheBuilding()
    {
        var ctx = DowntownAnalysis.UserContext();
        foreach (var kind in new[] { AxonKind.Site, AxonKind.Stacked })
            Assert.Equal(0.5, Assert.Single(AxonDrawing.Build(ctx, kind).Layer("AXON_LEVEL_LINE").Items).StrokePt);
        // A tree half behind a face loses the hidden half plus the clearance.
        var f = new GeometryFactory();
        var face = new Prism("WALL", f.CreatePolygon(new[] { new Coordinate(0, 0), new Coordinate(10, 0), new Coordinate(10, 1), new Coordinate(0, 1), new Coordinate(0, 0) }), 0, 30, "T", "S", "S", "PRG");
        var proj = AxonProjection.FromAngles(225, 35.264);
        var faces = AxonFaces.Render(proj, new[] { face });
        var tree = f.CreatePoint(new Coordinate(faces.Max(x => x.Screen.EnvelopeInternal.MaxX), faces.Average(x => x.Screen.EnvelopeInternal.Centre.Y))).Buffer(6);
        var plain = AxonFaces.Hide(proj, tree, -1e9, faces);
        var gap = AxonFaces.Hide(proj, tree, -1e9, faces, 1);
        Assert.True(gap!.Area < plain!.Area - 1, $"{gap.Area:0.0} vs {plain.Area:0.0}");
    }

    // ------------------------------------------------------------------ (5) medians clear of the crossings

    [Fact]
    public void Medians_StopTwentyFeetBeforeTheCrossing()
    {
        var ctx = DowntownAnalysis.Context();
        Assert.Equal(20, ctx.Style.Site.Median.EndClearFt);
        var near = DowntownAnalysis.Context();
        near.Style.Site.Median.EndClearFt = 8;
        var a = SiteLayout.For(ctx).MedianLines;
        var b = SiteLayout.For(near).MedianLines;
        Assert.NotEmpty(a);
        // The same median is 2 × 12 ft shorter than with 8 ft clearance (both ends move in).
        var pairs = a.Select(x => (x, y: b.FirstOrDefault(y => y.Centroid.Distance(x.Centroid) < 3))).Where(p => p.y != null).ToList();
        Assert.NotEmpty(pairs);
        Assert.All(pairs, p => Assert.InRange(p.y!.Length - p.x.Length, 23, 25));
    }

    // ------------------------------------------------------------------ (6) exploded strokes

    [Fact]
    public void Exploded_OneOutlinePerLevel_WhiteEdges_NoFolds()
    {
        var ctx = DowntownAnalysis.UserContext();
        var d = AxonDrawing.Build(ctx, AxonKind.Exploded);
        var all = d.Layers.SelectMany(l => l.Items).ToList();
        Assert.DoesNotContain(all, i => i.Name.EndsWith("FOLDS"));
        var outlines = all.Where(i => i.Name.EndsWith("_OUTLINE")).ToList();
        Assert.True(outlines.Count >= 10, $"{outlines.Count} level outlines");
        var plateFaces = all.Where(i => i.Name.Contains("_LAB_") && i.Name.EndsWith("_TOP")).ToList();
        Assert.NotEmpty(plateFaces);
        Assert.All(plateFaces, i => Assert.Equal(ctx.Style.Axon.Levels.EdgePt, i.StrokePt));
    }

    // ------------------------------------------------------------------ (7) property line

    [Fact]
    public void PropertyLine_PhantomInPlans_SiteEdgeIn04_DashedInSections()
    {
        Assert.Equal(Roles.PropertyLine, LayerClassifier.Default().Guess(new LayerInfo(0, "PROPERTY LINE", true, "#000000")).Role);
        var ctx = PropertyContext();
        var plans = PlanDrawing.Build(ctx);
        var pl = plans.Layer("PD_PROPERTY_LINE").Items;
        Assert.NotEmpty(pl);
        Assert.All(pl, i => Assert.True(i.Dash is { Length: 6 }, "long-short-short phantom dash"));
        // 04: the site is the property line grown by 50 ft.
        var ex = AxonDrawing.Build(ctx, AxonKind.Levels);
        var site = ex.Layers.SelectMany(l => l.Items).First(i => i.Name.EndsWith("_SITE") && i.Artboard == 1);
        var c0 = DowntownAnalysis.Context();
        var plain = AxonDrawing.Build(new PD.Core.Drawing.ExportContext { Result = WithoutProperty.Value, Settings = c0.Settings, Programs = c0.Programs }, AxonKind.Levels).Layers.SelectMany(l => l.Items).First(i => i.Name.EndsWith("_SITE") && i.Artboard == 1);
        // Property line 20 ft out, + 50 ft = 70 ft around the building (the default site is 40 ft around it).
        var k = ctx.PtPerFt;
        Assert.InRange(Env(site).Width - Env(plain).Width, 0.9 * 2 * 30 * k, 1.6 * 2 * 30 * k * 1.3);
        Assert.Contains(ex.Layer("AXON_SITE").Items, i => i.Dash is { Length: 6 });
        // Sections: dashed verticals where the cut crosses it.
        var bb = ctx.Result.Building;
        var (x0, y0, _) = ctx.Result.Frame.ToModel(bb.MinX - 60, (bb.MinY + bb.MaxY) / 2, 0);
        var (x1, y1, _) = ctx.Result.Frame.ToModel(bb.MaxX + 60, (bb.MinY + bb.MaxY) / 2, 0);
        var sec = PD.Core.Sections.SectionDrawing.Build(ctx.ForSection(), new PD.Core.Settings.SectionSetting { Name = "P", Line = new[] { x0, y0, x1, y1 }, DepthFt = 200 });
        Assert.Equal(2, sec.Layer("PD_ANNOTATION").Items.Count(i => i.Name.Contains("PROPERTY_") && i.Dash != null));
    }

    // ------------------------------------------------------------------ (8) site axon reach

    [Fact]
    public void SiteAxon_ReachesTwiceAsFar()
    {
        var st = DowntownAnalysis.Context().Style.Axon.Site;
        Assert.Equal(440, st.SiteMarginFt);
        Assert.True(st.ContextMarginFt > st.SiteMarginFt);
    }

    // ------------------------------------------------------------------ (9) legends, axon north

    [Fact]
    public void Legends_SymbolsOnPlansSitePlanAndAxons_AxonTitleNorth()
    {
        var ctx = DowntownAnalysis.UserContext();
        var plans = PlanDrawing.Build(ctx);
        var leg = plans.Artboards.FindIndex(a => a.Name == "LEGEND & SYMBOLS");
        foreach (var key in new[] { "ENTRY", "SERVICE", "VIEW_MARKER", "CAR", "PED_FLOW", "CAR_FLOW", "GATEWAY", "TRANSIT", "TAG" })
            Assert.Contains(plans.Layer("PD_LEGEND").Items, i => i.Artboard == leg && i.Name == $"LGD_SYM_{key}");
        var site = PlanDrawing.BuildSite(ctx);
        Assert.Equal("LEGEND & SYMBOLS", site.Artboards[^1].Name);
        foreach (var kind in new[] { AxonKind.Site, AxonKind.Stacked, AxonKind.Exploded, AxonKind.ExplodedB, AxonKind.Levels })
        {
            var ax = AxonDrawing.Build(ctx, kind);
            Assert.Equal("LEGEND", ax.Artboards[^1].Name);
            Assert.Contains(ax.Layer("PD_LEGEND").Items, i => i.Name.EndsWith("SYM_PED_FLOW_LINE") && i.Dash != null);
            Assert.Contains(ax.Layer("PD_ANNOTATION").Items, i => i.Name.EndsWith("NORTH_HEAD") && i.Artboard == 0);
        }
    }

    // ------------------------------------------------------------------ (10) tree / people colours

    [Fact]
    public void Colours_TreesAndPeopleFromTab5()
    {
        var ctx = DowntownAnalysis.UserContext();
        ctx.Settings.TreeColor = "#336699";
        ctx.Settings.PeopleColor = "#AA0000";
        ctx.Settings.Output.Drawings = new() { "SitePlan" };
        var drawings = PD.Core.Writers.Exporter.BuildDrawings(ctx, new List<string>());
        Assert.Equal("#AA0000", ctx.Style.Entourage.Color);
        var site = drawings.First(d => d.FileName == "00_Site_Plan.ai");
        var tree = site.Layer("BG_TREE").Items.First();
        Assert.Equal("#336699", site.Swatches.Single(s => s.Name == tree.Fill).Hex);
        Assert.Equal(new[] { "#336699" }, ctx.Style.Axon.Trees.Canopies);
    }

    // ------------------------------------------------------------------ fixed artboard

    [Fact]
    public void FixedArtboard_PlansSectionsElevationsFit()
    {
        var ctx = DowntownAnalysis.UserContext();
        Assert.Equal("11x17 Landscape", ctx.Settings.Output.Artboard);
        var notes = new List<string>();
        ctx.Settings.Output.Drawings = new() { "SitePlan", "Plan", "Section", "Elevation" };
        var drawings = PD.Core.Writers.Exporter.BuildDrawings(ctx, notes);
        Assert.DoesNotContain(notes, n => n.Contains("larger than"));
        Assert.DoesNotContain(notes, n => n.Contains("none of our building"));
        var l1 = drawings.First(d => d.FileName == "01_Plans.ai").Artboards.First(a => a.Name == "PLAN L1");
        Assert.Equal((1224.0, 792.0), (l1.W, l1.H));
    }
}
