using PD.Core.Axons;
using PD.Core.Plans;
using Xunit;

namespace PD.Core.Tests;

/// <summary>User feedback on v0.8.0 (Windows): site plan missing, basements in 03, stacked axon slabs and lines.</summary>
public class Rev081Tests
{
    static IEnumerable<double[]> Points(PD.Core.Drawing.DItem i) => i.Paths!.SelectMany(p => p);

    /// <summary>Illustrator refuses artboards past its canvas, so an oversized site plan was never created.</summary>
    [Fact]
    public void SitePlan_NeverLargerThanTheIllustratorCanvas()
    {
        var ctx = DowntownAnalysis.Context();
        ctx.Style.Plan.SiteExtentFactor = 60;
        var d = PlanDrawing.BuildSite(ctx);
        var ab = Assert.Single(d.Artboards);
        Assert.True(ab.W <= ctx.Style.Page.MaxArtboardPt && ab.H <= ctx.Style.Page.MaxArtboardPt, $"{ab.W} × {ab.H}");
        Assert.NotEmpty(d.Layer("PD_PROGRAM").Items);
    }

    [Theory]
    [InlineData(AxonKind.Stacked, "AXS_")]
    [InlineData(AxonKind.Site, "AXT_")]
    public void Axon03_NoBasements(AxonKind kind, string prefix)
    {
        var d = AxonDrawing.Build(DowntownAnalysis.Context(), kind);
        var program = d.Layer("PD_PROGRAM").Items;
        Assert.Contains(program, i => i.Name.StartsWith(prefix + "L1_"));
        Assert.DoesNotContain(program, i => i.Name.StartsWith(prefix + "B1_"));
    }

    [Fact]
    public void StackedAxon_FloorToFloorWithLevelLinesAndOuterSilhouette()
    {
        var ctx = DowntownAnalysis.Context();
        var d = AxonDrawing.Build(ctx, AxonKind.Stacked);
        var line = Assert.Single(d.Layer("AXON_LEVEL_LINE").Items);
        Assert.Equal(ctx.Style.Axon.Stacked.LevelLinePt, line.StrokePt);
        // One heavy outline around the whole building, not one per floor band.
        var outline = d.Layer("AXON_LINE").Items.Single(i => i.Name.EndsWith("OUTLINE"));
        Assert.True(outline.Paths!.Count <= 3, $"{outline.Paths.Count} outline rings");
    }

    /// <summary>The site follows Level 1; basements are underground: below the whole site plate, never drawn over it.</summary>
    [Theory]
    [InlineData(AxonKind.Exploded, "AXE_")]
    [InlineData(AxonKind.ExplodedB, "AXB_")]
    public void Exploded_BasementsHangBelowTheSitePlate(AxonKind kind, string prefix)
    {
        var d = AxonDrawing.Build(DowntownAnalysis.Context(), kind);
        var site = d.Layers.SelectMany(l => l.Items).Single(i => i.Name == prefix + "SITE");
        var b1 = d.Layers.SelectMany(l => l.Items).Where(i => i.Name.StartsWith(prefix + "B1_B") && i.Paths is { Count: > 0 }).ToList();
        // Plate faces only (B1_B01_…); the core connectors rightly rise from B1 through the site hole.
        Assert.NotEmpty(b1);
        // Page y grows downwards: B1's top-most point lies under the site's bottom-most point.
        var siteBottom = Points(site).Max(q => q[1]);
        var b1Top = b1.SelectMany(Points).Min(q => q[1]);
        Assert.True(b1Top > siteBottom, $"B1 top {b1Top}, site bottom {siteBottom}");
        if (kind == AxonKind.Exploded) Assert.NotEmpty(d.Layer("AXON_EXCAVATION").Items);
    }
}
