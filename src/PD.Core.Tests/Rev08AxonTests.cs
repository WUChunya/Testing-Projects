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
        Assert.Equal(2.0, line.StrokePt);
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
}
