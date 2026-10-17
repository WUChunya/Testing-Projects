using NetTopologySuite.Geometries;
using PD.Core.Axons;
using PD.Core.Classification;
using PD.Core.Plans;
using Xunit;

namespace PD.Core.Tests;

/// <summary>User feedback on v0.8.1: richer site (curves, trees in greens, hatches), stacked axon thin floor lines.</summary>
public class Rev082Tests
{
    static Envelope SiteEnv()
    {
        var ctx = DowntownAnalysis.Context();
        return PlanDrawing.SiteExtent(ctx, ctx.Result.Plans.FindIndex(p => p.Level.Name == "L1"));
    }

    /// <summary>CADMAPPER parks are closed curves (no surface): they were dropped, now they are green areas.</summary>
    [Fact]
    public void Background_ClosedCurvesBecomeAreas_RoadCurvesKerbLines()
    {
        var ctx = DowntownAnalysis.Context();
        var items = PlanBackground.Collect(ctx, SiteEnv());
        Assert.True(items.Count(i => i.Role == Roles.Landscape && !i.IsLine) >= 4, "parks");
        Assert.Contains(items, i => i.Role == Roles.Road && i.IsLine);       // ROAD LINES → kerbs (closedCurves "line")
        Assert.Contains(items, i => i.Role == Roles.Water && i.IsLine);      // open river curve
        // Walkway curves and their extrusions are the same shapes: drawn once.
        var walk = items.Where(i => i.Role == Roles.Hardscape && !i.IsLine).Select(i => i.Geometry.EnvelopeInternal.ToString()).ToList();
        Assert.Equal(walk.Count, walk.Distinct().Count());
    }

    [Fact]
    public void Background_GreensArePlantedTheSameInEveryDrawing()
    {
        var ctx = DowntownAnalysis.Context();
        var env = SiteEnv();
        var trees = PlanBackground.Collect(ctx, env).Where(i => i.Name.StartsWith("GREEN_TREE")).ToList();
        Assert.True(trees.Count >= 50, $"{trees.Count} trees");
        var gs = ctx.Style.Entourage.GreenTrees;
        // Trunks keep their spacing (greens are planted one by one, so only neighbouring greens may come closer)
        // and stay off other buildings.
        var pts = trees.Select(t => t.Geometry.Centroid.Coordinate).ToList();
        var crowded = pts.Count(p => pts.Any(q => !ReferenceEquals(p, q) && p.Distance(q) < gs.SpacingFt - 1e-6));
        Assert.True(crowded <= pts.Count / 10, $"{crowded} of {pts.Count} trees closer than {gs.SpacingFt} ft");
        var buildings = PlanBackground.Collect(ctx, env).Where(i => i.Role == Roles.Building && !i.IsLine).Select(i => i.Geometry).ToList();
        Assert.DoesNotContain(pts, p => buildings.Any(b => b.Contains(new GeometryFactory().CreatePoint(p))));
        // A smaller window shows exactly the trees of the big one that fall inside it.
        var small = new Envelope(env.Centre);
        small.ExpandBy(env.Width / 4, env.Height / 4);
        var inner = PlanBackground.Collect(ctx, small).Where(i => i.Name.StartsWith("GREEN_TREE")).Select(i => i.Geometry.Centroid.Coordinate).ToList();
        Assert.All(inner, c => Assert.Contains(pts, p => p.Distance(c) < 1e-6));
        Assert.Equal(pts.Count(small.Contains), inner.Count);

        // Switched off with the entourage.
        var off = DowntownAnalysis.Context();
        off.Style.Entourage.GreenTrees.Enabled = false;
        Assert.DoesNotContain(PlanBackground.Collect(off, env), i => i.Name.StartsWith("GREEN_TREE"));
    }

    [Fact]
    public void Hatch_ParallelLinesInsideTheArea()
    {
        var f = PD.Core.Geometry.MeshSlicer.Factory;
        var sq = f.CreatePolygon(new[] { new Coordinate(0, 0), new Coordinate(40, 0), new Coordinate(40, 40), new Coordinate(0, 40), new Coordinate(0, 0) });
        var h = PlanBackground.Hatch(sq, new Config.HatchStyle { SpacingFt = 5, AngleDeg = 0 });
        Assert.InRange(h.NumGeometries, 7, 9);
        Assert.True(sq.Buffer(1e-6).Covers(h));
    }

    [Fact]
    public void SitePlan_GreenAndWaterHatchedWithoutOutline()
    {
        var ctx = DowntownAnalysis.Context();
        var d = PlanDrawing.BuildSite(ctx);
        var site = d.Layer("BG_SITE").Items;
        Assert.Contains(site, i => i.Name.EndsWith("_HATCH") && i.Fill == null && i.Closed == false);
        var green = d.Swatches.Single(s => s.Name == "BG_LANDSCAPE").Name;
        Assert.All(site.Where(i => i.Fill == green), i => Assert.Null(i.Stroke));
        Assert.True(d.Layer("BG_TREE").Items.Count >= 50);
    }

    [Fact]
    public void StackedAxon_OneThinLineBetweenFloors()
    {
        var ctx = DowntownAnalysis.Context();
        var d = AxonDrawing.Build(ctx, AxonKind.Stacked);
        var line = Assert.Single(d.Layer("AXON_LEVEL_LINE").Items);
        Assert.True(line.StrokePt <= 0.5, $"{line.StrokePt} pt");
        Assert.True(ctx.Style.Axon.Stacked.SilhouettePt >= 4 * line.StrokePt);
        // User rev 0.8.4: the site axon's floor lines are 0.5 pt too.
        Assert.Equal(0.5, Assert.Single(AxonDrawing.Build(ctx, AxonKind.Site).Layer("AXON_LEVEL_LINE").Items).StrokePt);
    }
}
