using PD.Core.Axons;
using Xunit;

namespace PD.Core.Tests;

/// <summary>User feedback on v0.8.6 (Windows export of the Downtown model rev B, NE view): the same four points again.</summary>
public class Rev087Tests
{
    // ------------------------------------------------------------------ (1) stacked: plates as in Rhino, no overlaps

    [Fact]
    public void Stacked_NoRoofUnderAFloorPlateShowsThrough()
    {
        // A block stretched to the floor above stops under that floor's plate (its "_U" piece). Its top lies 1 ft under
        // the plate, so it can never be seen: it showed where the cycle breaker of the hidden-face pass took it before
        // the plate (the L4 plate edge round the recessed L4, and the L2 plate over the L1 lobby, were missing).
        var d = AxonDrawing.Build(DowntownAnalysis.UserContext(), AxonKind.Stacked);
        var hidden = d.Layer("PD_PROGRAM").Items.Where(i => System.Text.RegularExpressions.Regex.IsMatch(i.Name, @"_U\d+_TOP")).Select(i => i.Name).ToList();
        Assert.True(hidden.Count == 0, string.Join(", ", hidden));
        Assert.Contains(d.Layer("PD_PROGRAM").Items, i => i.Name.Contains("_L4_EXPOSED_ROOF") && i.Name.EndsWith("_TOP"));
    }

    // ------------------------------------------------------------------ (2) exploded: the basement complete

    [Fact]
    public void Basement_CirculationPlateIsAProgram()
    {
        // The 1 ft circulation plate on the B1 floor was cut just under its top and came back empty: B1 showed a hole
        // (with pit walls round it) where its corridors are.
        var r = DowntownAnalysis.Result;
        Assert.Contains(r.Plans[0].Footprints, f => f.Mass.ProgramKey == "CIRCULATION" && f.Resolved.Area > 5000);
        var d = AxonDrawing.Build(DowntownAnalysis.UserContext(), AxonKind.Exploded);
        Assert.Contains(d.Layer("AXON_B1").Items, i => i.Name.Contains("_CIRCULATION_"));
        var order = d.Layers.Select(l => l.Name).ToList();
        Assert.True(order.IndexOf("AXON_B1") < order.IndexOf("AXON_CONNECTOR_BELOW"));
        Assert.True(order.IndexOf("AXON_CONNECTOR_BELOW") < order.IndexOf("AXON_SITE"));
    }

    // ------------------------------------------------------------------ (4) glass in every axon, 50 %

    [Fact]
    public void Glass_InTheExplodedAndFloorKeyAxons()
    {
        var ctx = DowntownAnalysis.UserContext();
        Assert.Equal("storey", ctx.Style.Facade.ExplodedAxon);
        var ex = AxonDrawing.Build(ctx, AxonKind.Exploded);
        var items = ex.Layers.Where(l => l.Name.StartsWith("AXON_L")).SelectMany(l => l.Items).ToList();
        Assert.Contains(items, i => i.Name.Contains("FACADE_GLASS") && i.Opacity == ctx.Style.Facade.StackedAxonOpacity);
        Assert.Contains(items, i => i.Name.Contains("EXPOSED_GLASS") && i.Opacity == ctx.Style.Exposed.GlassOpacity);
        var levels = AxonDrawing.Build(ctx, AxonKind.Levels);
        Assert.Contains(levels.Layer("FACADE").Items, i => i.Name.Contains("FACADE_GLASS") && i.Opacity == ctx.Style.Facade.StackedAxonOpacity);

        ctx = DowntownAnalysis.UserContext();
        ctx.Style.Facade.ExplodedAxon = "hidden";
        Assert.DoesNotContain(AxonDrawing.Build(ctx, AxonKind.Exploded).Layers.SelectMany(l => l.Items), i => i.Name.Contains("FACADE_GLASS"));
    }
}
