using NetTopologySuite.Geometries;
using PD.Core.Axons;
using PD.Core.Plans;
using Xunit;

namespace PD.Core.Tests;

/// <summary>User feedback on v0.8.5 (Windows export of the Downtown model rev B, NE view).</summary>
public class Rev086Tests
{
    // ------------------------------------------------------------------ (1) stacked: every floor edge the model shows

    [Fact]
    public void Stacked_ShowsTheFloorPlatesOfRecessedLevels()
    {
        var d = AxonDrawing.Build(DowntownAnalysis.UserContext(), AxonKind.Stacked);
        var names = d.Layer("PD_PROGRAM").Items.Select(i => i.Name).ToList();
        // L4 and L7 stand back from the floors above: their plates show round them, as in Rhino.
        Assert.Contains(names, n => n.Contains("_L4_EXPOSED_ROOF"));
        Assert.Contains(names, n => n.Contains("_L7_EXPOSED_ROOF"));
    }

    // ------------------------------------------------------------------ (2) exploded: ground › shafts › basements

    [Fact]
    public void Exploded_BasementsUnderTheShaftsUnderTheSite()
    {
        var d = AxonDrawing.Build(DowntownAnalysis.UserContext(), AxonKind.Exploded);
        var order = d.Layers.Select(l => l.Name).ToList();
        Assert.True(order.IndexOf("AXON_B1") >= 0);
        Assert.True(order.IndexOf("AXON_B1") < order.IndexOf("AXON_CONNECTOR_BELOW"));
        Assert.True(order.IndexOf("AXON_CONNECTOR_BELOW") < order.IndexOf("AXON_SITE"));
    }

    // ------------------------------------------------------------------ (3) the hole in the ground floor plate

    [Fact]
    public void GroundFloor_OpeningIsTheHoleInItsPlate()
    {
        var ctx = DowntownAnalysis.UserContext();
        var g = PlanDrawing.GroundIndex(ctx);
        var open = SeenBelow.OpeningsAt(ctx, g);
        var f = new GeometryFactory();
        Assert.True(open.Contains(f.CreatePoint(new Coordinate(156, 108))), "the 24 x 48 ft hole in the L1 plate");
        Assert.False(open.Contains(f.CreatePoint(new Coordinate(66, 96))), "a gap between blocks over the plate is no opening");
        var site = PlanDrawing.BuildSite(ctx);
        Assert.Contains(site.Layer("PD_VOID").Items, i => i.Name == "STP_OPENING_01");
    }

    // ------------------------------------------------------------------ (4) glass: 50 %, the facade glass in the axons

    [Fact]
    public void Glass_HalfOpaque_FacadeGlassInTheStackedAxon()
    {
        var ctx = DowntownAnalysis.UserContext();
        Assert.Equal(0.5, ctx.Style.Exposed.GlassOpacity);
        Assert.Equal(0.5, ctx.Style.Facade.StackedAxonOpacity);
        var d = AxonDrawing.Build(ctx, AxonKind.Stacked);
        var glass = d.Layer("FACADE").Items;
        Assert.Contains(glass, i => i.Name.Contains("FACADE_GLASS") && i.Opacity == 0.5);
        Assert.Contains(glass, i => i.Name.Contains("EXPOSED_GLASS") && i.Opacity == 0.5);
        var plans = PlanDrawing.Build(ctx);
        Assert.Contains(plans.Layer("PD_PROGRAM").Items, i => i.Name.Contains("EXPOSED_GLASS") && i.Opacity == 0.5);
    }
}
