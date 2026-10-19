using NetTopologySuite.Geometries;
using NetTopologySuite.IO;
using PD.Core.Analysis;
using PD.Core.Axons;
using PD.Core.Classification;
using PD.Core.Geometry;
using PD.Core.IO;
using PD.Core.Plans;
using Xunit;

namespace PD.Core.Tests;

/// <summary>
/// v0.8.9 regressions from RIVER_2026_1016_FINAL.3dm: export failed with "found non-noded intersection" because two
/// masses share a slightly skewed wall and the level union keeps hair-thin sliver holes along it.
/// </summary>
public class Rev089Tests
{
    // A footprint and the union of the smaller masses on its level (local ft), clipped around the failure point.
    const string Footprint = "POLYGON ((266.48129440171465 252.07, 241.38 252.07, 241.38 298.7320568293679, 316.5130791561847 399.6533662908273, 316.92258232 400.20342488, 355.28573093162413 371.64110452041245, 355.42323972 371.5387258, 266.48129440171465 252.07))";
    const string Union =
        "MULTIPOLYGON (((266.9241966326772 252.66538092230644, 276.59777788 265.65945732, 287.18604511366146 257.7778023648819, 294.85394444450554 252.07, 266.4809588411817 252.07, 266.9241966326772 252.66538092230644)), ((355.37205621871107 371.4699772647293, 355.42323972 371.5387258, 374.6064485868948 357.259200181289, 374.67520884000004 357.20801668, 360.3924140991235 338.0248078131052, 360.34121888000004 337.95604756, 341.1612791352707 352.2388423008765, 341.09253060000003 352.29003752, 355.37205621871107 371.4699772647293)), ((441.33203512 414.36353032, 441.33203512 438.2804280824748, 441.33203512 438.36615576, 441.33203512 452.07, 441.38 452.07, 441.38 414.36353032, 441.33203512 414.36353032)), ((334.64200000330385 343.6263356336614, 341.09253060000003 352.29003752, 351.67899342547577 344.4065781566962, 360.34121888000004 337.95604756, 352.4595639248819 327.36958473452427, 346.01050976 318.70735928, 335.4222425263386 326.58901423511816, 326.75854064000004 333.03806840000004, 334.64200000330385 343.6263356336614)), ((374.81271762837594 357.1056496774221, 374.67520884000004 357.20801668, 417.0070717604875 414.07112762593863, 417.158806 414.27494764, 441.24569811507854 414.3632139384673, 441.33203512 414.36353032, 441.33203512 366.5362730393813, 441.33203512 366.36484112, 413.27642869605813 328.68166991879076, 413.17586624 328.54659844, 374.81271762837594 357.1056496774221)))";

    [Fact]
    public void SkewedSharedWall_OverlaysDoNotThrow()
    {
        var reader = new WKTReader(MeshSlicer.Factory.GeometryServices);
        var (f, u) = (reader.Read(Footprint), reader.Read(Union));
        var resolved = f.Difference(u);
        var merged = u.Union(f);
        Assert.InRange(resolved.Area + u.Area, merged.Area - 0.01, merged.Area + 0.01);
        Assert.InRange(f.Intersection(u).Area, 0, f.Area);
    }

    // ------------------------------------------------------------------ CW markup on the RIVER previews (tests/fixtures/river.json)

    [Fact]
    public void River_MarkupModel_Analyses()
    {
        var r = RiverAnalysis.Result;
        Assert.Equal(new[] { 0.0, 20.0, 36.0, 52.0 }, r.Levels.Take(4).Select(l => Math.Round(l.Elevation, 1)));
        Assert.Equal(0, r.Plans.Sum(p => p.AreaByProgram.GetValueOrDefault("INTERACTION"))); // the pavilion is a context building
        // L1 inside the glass is circulation; the stairs are structure (no program, no area).
        Assert.True(r.Plans[0].AreaByProgram.GetValueOrDefault("CIRCULATION") > 10000);
        Assert.DoesNotContain(r.Masses, m => m.ProgramKey == "STAIR");
        Assert.True(r.Plans[0].AreaByProgram.GetValueOrDefault("PARKING") > 30000);
        // The three diagonal-bar core labs stay double height.
        var tall = r.Spans.Where(s => s.Mode == PD.Core.Cleanup.SpanMode.KeepTall && s.LevelIndices.Length == 2).Select(s => s.MassId).ToList();
        Assert.All(new[] { "8afa85", "6a7d47", "a475fa" }, id => Assert.Contains(tall, m => m.StartsWith(id)));
    }

    [Fact]
    public void ExplodedB_SiteTreesNeitherFloatNorCoverThePlates()
    {
        // "配景重叠了": trees beside the site plate floated in the air and site trees were drawn over the lifted plates.
        var d = AxonDrawing.Build(RiverAnalysis.Context(), AxonKind.ExplodedB);
        var trees = d.Layer("AXON_TREE").Items.Where(i => i.Closed != false).Select(Poly).Where(g => !g.IsEmpty).ToList();
        Assert.NotEmpty(trees);
        var upper = d.Layers.Where(l => l.Name.StartsWith("AXON_L") && l.Name != "AXON_L1M").SelectMany(l => l.Items)
            .Where(i => i.Paths != null && i.Closed != false).Select(Poly).Where(g => !g.IsEmpty).ToList();
        var plates = MeshSlicer.Polygonal(NetTopologySuite.Operation.Union.CascadedPolygonUnion.Union(upper));
        var covered = trees.Sum(t => t.Intersection(plates).Area);
        Assert.True(covered < 0.02 * trees.Sum(t => t.Area), $"{covered:0} of {trees.Sum(t => t.Area):0} pt² of tree over the plates");
    }

    [Fact]
    public void Plans_NameOurSiteAreas_WithoutOverlaps()
    {
        var d = PlanDrawing.Build(RiverAnalysis.Context());
        var labels = d.Layer("BG_SITE_LABELS").Items;
        Assert.Contains(labels, i => i.Text == "ENTRANCE PLAZA");
        Assert.Contains(labels, i => i.Text == "LOADING AND BOH SERVICE");
        Assert.Contains(labels, i => i.Text == "RIVER PROMENADE");
        foreach (var g in labels.GroupBy(i => i.Artboard))
            Assert.Equal(g.Count(), g.Select(i => i.Text).Distinct().Count());
    }

    [Fact]
    public void OpenLawns_AreNotPlanted_ModelledPeopleStand()
    {
        var ctx = RiverAnalysis.Context();
        var env = new Envelope(-300, 900, -200, 1000);
        var bg = PlanBackground.Collect(ctx, env);
        var meadows = bg.Where(b => b.Name is "RIVER MEADOW" or "PICNIC LAWN" or "EVENT LAWN").Select(b => b.Geometry).ToList();
        Assert.NotEmpty(meadows);
        var generated = bg.Where(b => b.Name.StartsWith("GREEN_TREE")).Select(b => b.Geometry.Centroid).ToList();
        var inside = generated.Where(c => meadows.Any(m => m.Buffer(-1).Contains(c))).ToList();
        Assert.True(inside.Count == 0, string.Join("; ", inside.Take(5).Select(c => $"{c} in " + string.Join("/", bg.Where(b => b.Geometry.Buffer(-1).Contains(c)).Select(b => b.Role + ":" + b.Name)))));
        Assert.Contains(bg, b => b.Name == "FLOWERING GROVE");
        // People placed in the model (picnics, the river steps) are drawn where they stand.
        var modelled = PD.Core.Entourage.EntouragePlacer.Modelled(ctx, env);
        Assert.True(modelled.Count > 80, $"{modelled.Count} modelled people");
        var site = AxonDrawing.Build(ctx, AxonKind.Site);
        Assert.True(site.Layer(PD.Core.Entourage.EntouragePlacer.Layer).Items.Count >= modelled.Count / 2);
    }

    [Fact]
    public void Section_RoofLevelIsNotMarkedTwice()
    {
        // "L5 ROOF" and "T.O. PENTHOUSE" at the same height were printed over each other.
        var ctx = RiverAnalysis.Context();
        foreach (var sec in ctx.Settings.Sections.Where(s => !s.IsElevation))
        {
            var d = PD.Core.Sections.SectionDrawing.Build(ctx, sec);
            var texts = d.Layers.SelectMany(l => l.Items).Where(i => i.Text != null && i.Name.EndsWith("_TXT")).ToList();
            Assert.False(texts.Any(i => i.Name.Contains("PENTHOUSE_TOP")) && texts.Any(i => i.Text!.Contains("ROOF")),
                string.Join(", ", texts.Select(i => i.Text)));
        }
    }

    // ------------------------------------------------------------------ CW markup V2

    [Fact]
    public void ExplodedA_TallSpacesOpenThePlate_NoLinks()
    {
        // "auditorium不是core，lab两层通高也不是core，楼板正常挖开就可以，不需要core的引线"
        var d = AxonDrawing.Build(RiverAnalysis.Context(), AxonKind.Exploded);
        var items = d.Layers.SelectMany(l => l.Items).ToList();
        Assert.DoesNotContain(items, i => i.Name.Contains("_TALL_"));
        Assert.Contains(items, i => i.Name.Contains("_CONNECTOR_"));
    }

    [Fact]
    public void SiteAxon_NoMassingOutlines()
    {
        // "这是什么线，应该不需要请删除": the parking-under-the-ramp massing drew a dashed outline over the site.
        var ctx = RiverAnalysis.Context();
        Assert.Empty(AxonDrawing.Build(ctx, AxonKind.Site).Layers.Where(l => l.Name == "AXON_MASSING").SelectMany(l => l.Items));
    }

    [Fact]
    public void Plans_GridLinesAndOverhangOutline()
    {
        // Three PROTOTYPING blocks read as one room on a dashed grid; the L2 overhang over the open ground floor is dashed.
        var d = PlanDrawing.Build(RiverAnalysis.Context());
        Assert.True(d.Layer("PD_GRID").Items.Count(i => i.Artboard == 0) >= 2);
        Assert.All(d.Layer("PD_GRID").Items, i => Assert.NotNull(i.Dash));
        Assert.Contains(d.Layer("PD_OVERHEAD").Items, i => i.Artboard == 0 && i.Dash != null);
    }

    [Fact]
    public void Sections_ShowTheRampOverParking()
    {
        // New section C runs up the ramp: the ramp slab is cut, the parking under it is backed and named.
        var ctx = RiverAnalysis.Context();
        var sec = ctx.Settings.Sections.Single(s => s.Name == "C");
        var d = PD.Core.Sections.SectionDrawing.Build(ctx, sec);
        Assert.NotEmpty(d.Layer("PD_SITE_CUT").Items);
        Assert.Contains(d.Layers.SelectMany(l => l.Items), i => i.Text == "PARKING");
        // A small context building far behind (the pavilion) is not named over our building.
        Assert.DoesNotContain(d.Layers.SelectMany(l => l.Items), i => i.Text == "RIVER PAVILION");
    }

    static NetTopologySuite.Geometries.Geometry Poly(PD.Core.Drawing.DItem i)
    {
        var rings = i.Paths!.Where(p => p.Length >= 3)
            .Select(p => MeshSlicer.Factory.CreatePolygon(p.Select(q => new Coordinate(q[0], q[1])).Append(new Coordinate(p[0][0], p[0][1])).ToArray()).Buffer(0));
        return MeshSlicer.Polygonal(MeshSlicer.Factory.BuildGeometry(rings.ToList()).Buffer(0));
    }
}

/// <summary>The RIVER model after the CW markup (deliverables/2026-10-18_RIVER_MARKUP) with its plugin settings.</summary>
public static class RiverAnalysis
{
    static readonly Lazy<(AnalysisResult R, PD.Core.Settings.PdSettings S)> Lazy = new(() =>
    {
        var s = PD.Core.Settings.PdSettings.FromJson(File.ReadAllText(FixtureSmokeTests.FixturePath("river_settings.json")));
        // The settings hold the 3dm's own units (feet); the fixture stores millimetres.
        foreach (var sec in s.Sections) sec.Line = sec.Line.Select(v => v * 304.8).ToArray();
        s.Output.FillDefaults(PD.Core.Config.Rules.Default().Output);
        var model = FixtureReader.Load(FixtureSmokeTests.FixturePath("river.json"));
        var programs = ProgramCatalog.Effective(LayerClassifier.LoadPrograms(), s.Programs);
        var roles = s.RoleOverrides().ToDictionary(kv => kv.Key, kv => kv.Value);
        foreach (var l in model.Layers.Where(l => !roles.ContainsKey(l.Path))) roles[l.Path] = new LayerGuess(l.Path, Roles.Ignore, "");
        var r = Analyzer.Run(new AnalysisInput
        {
            Model = model, Classifier = ProgramCatalog.Classifier(programs), RoleOverrides = roles, ProgramOverrides = s.ObjectPrograms,
            SpanOverrides = s.SpanModes, SingleSurfaceChoices = s.SingleSurfaces, KeepStray = s.KeepStray.ToHashSet(), Levels = s.Levels,
        });
        return (r, s);
    });

    public static AnalysisResult Result => Lazy.Value.R;

    public static PD.Core.Drawing.ExportContext Context() => new()
    {
        Result = Lazy.Value.R, Settings = Lazy.Value.S, Programs = ProgramCatalog.Effective(LayerClassifier.LoadPrograms(), Lazy.Value.S.Programs),
    };
}
