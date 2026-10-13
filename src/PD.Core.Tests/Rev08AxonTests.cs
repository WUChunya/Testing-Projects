using PD.Core.Axons;
using PD.Core.Drawing;
using Xunit;

namespace PD.Core.Tests;

/// <summary>User rev 0.8 axon requests on the Downtown model.</summary>
public class Rev08AxonTests
{
    static readonly Lazy<PD.Core.Drawing.Drawing> Site = new(() => AxonDrawing.Build(DowntownAnalysis.Context(), AxonKind.Site));

    [Fact]
    public void SiteAxon_LevelLinesTwoPointBlackOnTheirOwnLayer()
    {
        var d = Site.Value;
        var line = Assert.Single(d.Layer("AXON_LEVEL_LINE").Items);
        Assert.Equal(DowntownAnalysis.Context().Style.Axon.Site.LevelLinePt, line.StrokePt);
        Assert.Equal("#000000", d.Swatches.Single(s => s.Name == line.Stroke).Hex);
        Assert.False(line.Closed);
        Assert.True(line.Paths!.Count >= 10, $"{line.Paths.Count} segments");
        // The layer sits right above the program faces.
        var names = d.Layers.Select(l => l.Name).ToList();
        Assert.Equal(names.IndexOf("PD_PROGRAM") + 1, names.IndexOf("AXON_LEVEL_LINE"));
    }

    [Fact]
    public void SiteAxon_HeavyLineOnlyOnTheOuterSilhouette()
    {
        var d = Site.Value;
        var outline = d.Layer("AXON_LINE").Items.Single(i => i.Name.EndsWith("OUTLINE"));
        Assert.Equal(DowntownAnalysis.Context().Style.Axon.Stacked.SilhouettePt, outline.StrokePt);
        // Shells only: no ring lies inside another ring of the outline.
        var rings = outline.Paths!.Select(r => new NetTopologySuite.Geometries.GeometryFactory().CreatePolygon(
            r.Append(r[0]).Select(q => new NetTopologySuite.Geometries.Coordinate(q[0], q[1])).ToArray())).ToList();
        for (var i = 0; i < rings.Count; i++)
        for (var j = 0; j < rings.Count; j++)
            if (i != j) Assert.False(rings[i].Contains(rings[j]), "a hole is outlined");
    }

    [Fact]
    public void SiteAxon_BlocksRunFloorToFloor()
    {
        var ctx = DowntownAnalysis.Context();
        var blocks = PD.Core.Analysis.ProgramBlocks.Build(ctx.Result);
        // Typical lab floors: the modelled masses stop under the slab, the site axon closes the gap.
        var lab = blocks.First(b => b.ProgramKey == "LAB" && ctx.Result.Levels[b.LevelIndex].Name == "L5");
        var next = ctx.Result.Levels[lab.LevelIndex + 1].Elevation;
        Assert.True(next - lab.Z1 > 0.1, "the model leaves a slab gap");
        var (z0, z1) = AxonDrawing.FullHeight(ctx.Result, lab);
        Assert.Equal(ctx.Result.Levels[lab.LevelIndex].Elevation, z0, 3);
        Assert.Equal(next, z1, 3);
    }

    /// <summary>A core rising out of a penthouse roof: its part inside the penthouse is hidden, the part above stands on the roof.</summary>
    [Fact]
    public void Faces_PrismThroughARoof_LowerPartHidden()
    {
        var f = new NetTopologySuite.Geometries.GeometryFactory();
        NetTopologySuite.Geometries.Polygon Sq(double x0, double y0, double x1, double y1) => f.CreatePolygon(new[]
        {
            new NetTopologySuite.Geometries.Coordinate(x0, y0), new NetTopologySuite.Geometries.Coordinate(x1, y0),
            new NetTopologySuite.Geometries.Coordinate(x1, y1), new NetTopologySuite.Geometries.Coordinate(x0, y1), new NetTopologySuite.Geometries.Coordinate(x0, y0),
        });
        var shell = new Prism("SHELL", Sq(0, 0, 40, 60).Difference(Sq(15, 20, 25, 30)), 0, 40, "T", "S1", "S2", "PRG");
        var core = new Prism("CORE", Sq(15, 20, 25, 30), 0, 80, "T", "S1", "S2", "PRG");
        var proj = AxonProjection.FromAngles(225, 35.264);
        var faces = AxonFaces.Render(proj, new[] { shell, core });
        Assert.True(faces.Where(v => v.Name.StartsWith("CORE_P1")).Sum(v => v.Screen.Area) < 1e-3, "the core shows through the roof");
        Assert.True(faces.Where(v => v.Name.StartsWith("CORE_SIDE") || v.Name == "CORE_TOP").Sum(v => v.Screen.Area) > 100);
    }

    [Fact]
    public void Exploded_SiteAtGroundLevelDugOpenOverTheBasement()
    {
        var ctx = DowntownAnalysis.Context();
        var d = AxonDrawing.Build(ctx, AxonKind.Exploded);
        var site = d.Layer("AXON_SITE").Items.Single(i => i.Name == "AXE_SITE");
        Assert.True(site.Paths!.Count >= 2, "the site has a hole over B1");
        // The site plate is drawn at L1's height: its level tag and the site's top-most point line up with the L1 plate, above B1.
        var b1 = d.Layer("PD_ANNOTATION").Items.Single(i => i.Name == "AXE_LVL_B1_LDR").Paths![0][0][1];
        var l1 = d.Layer("PD_ANNOTATION").Items.Single(i => i.Name == "AXE_LVL_L1_LDR").Paths![0][0][1];
        var siteTop = site.Paths!.SelectMany(p => p).Min(q => q[1]);
        Assert.True(siteTop < b1 - 20 && l1 < b1, $"site {siteTop}, L1 {l1}, B1 {b1}");
    }

    [Fact]
    public void Levels_BasementSiteDashedWithExcavationWalls_ColourSiteFromLevel1()
    {
        var d = AxonDrawing.Build(DowntownAnalysis.Context(), AxonKind.Levels);
        int Ab(string name) => d.Artboards.FindIndex(a => a.Name == name);
        var b1 = Ab("AXON B1");
        var l1 = Ab("AXON L1");
        var site = d.Layer("AXON_SITE").Items;
        var b1Site = site.Single(i => i.Artboard == b1 && i.Name == "AXK_B1_SITE");
        Assert.Null(b1Site.Fill);
        Assert.NotNull(b1Site.Dash);
        Assert.All(site.Where(i => i.Artboard == b1), i => Assert.Null(i.Fill));
        Assert.NotEmpty(d.Layer("AXON_EXCAVATION").Items.Where(i => i.Artboard == b1));
        Assert.DoesNotContain(d.Layer("AXON_TREE").Items, i => i.Artboard == b1);

        Assert.NotNull(site.Single(i => i.Artboard == l1 && i.Name == "AXK_L1_SITE").Fill);
        Assert.DoesNotContain(d.Layer("AXON_EXCAVATION").Items, i => i.Artboard == l1);
        // The excavation sits under the plate.
        var names = d.Layers.Select(l => l.Name).ToList();
        Assert.True(names.IndexOf("AXON_EXCAVATION") < names.IndexOf("PD_PROGRAM"));
    }
}
