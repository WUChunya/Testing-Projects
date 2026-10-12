using PD.Core.Elevations;
using PD.Core.Settings;
using Xunit;

namespace PD.Core.Tests;

/// <summary>Regressions from the Windows run of v0.6.1 (Illustrator errors, elevation side).</summary>
public class RobustnessTests
{
    /// <summary>Illustrator cuts long swatch names; two cut names collided and stopped 03_Axon_Stacked.ai.</summary>
    [Fact]
    public void SwatchNamesFitIllustratorAndStayUnique()
    {
        foreach (var kind in new[] { PD.Core.Axons.AxonKind.Stacked, PD.Core.Axons.AxonKind.Exploded, PD.Core.Axons.AxonKind.ExplodedB })
        {
            var d = PD.Core.Axons.AxonDrawing.Build(DowntownAnalysis.Context(), kind);
            Assert.All(d.Swatches, s => Assert.True(s.Name.Length <= PD.Core.Drawing.Drawing.MaxSwatchName, s.Name));
            Assert.Equal(d.Swatches.Count, d.Swatches.Select(s => s.Name).Distinct().Count());
            var used = d.Layers.SelectMany(l => l.Items).SelectMany(i => new[] { i.Fill, i.Stroke }).Where(x => x != null).Distinct();
            Assert.All(used, n => Assert.Contains(d.Swatches, s => s.Name == n));
        }
        Assert.NotEqual(PD.Core.Drawing.Drawing.SwatchName("PRG_BUILDING_SUPPORT_SIDE1_6_14_FLOOR"),
            PD.Core.Drawing.Drawing.SwatchName("PRG_BUILDING_SUPPORT_SIDE1_6_14_FLOOS"));
        Assert.Equal("PRG_LAB", PD.Core.Drawing.Drawing.SwatchName("PRG_LAB"));
    }

    /// <summary>An elevation drawn in either direction looks at our building (auto side).</summary>
    [Fact]
    public void ElevationLooksTowardsTheBuildingEitherWay()
    {
        var ctx = DowntownAnalysis.Context();
        var b = ctx.Result.Building;
        var (x0, y0, _) = ctx.Result.Frame.ToModel(b.MinX - 40, b.MinY - 60, 0);
        var (x1, y1, _) = ctx.Result.Frame.ToModel(b.MaxX + 40, b.MinY - 60, 0);
        foreach (var (line, left) in new[] { (new[] { x0, y0, x1, y1 }, true), (new[] { x1, y1, x0, y0 }, true), (new[] { x0, y0, x1, y1 }, false) })
        {
            var el = new SectionSetting { Name = "S", Kind = SectionSetting.ElevationKind, Line = line, LookLeft = left, DepthFt = 600 };
            var d = ElevationDrawing.Build(ctx, el);
            Assert.DoesNotContain(d.Notes, n => n.Contains("none of our building"));
            Assert.NotEmpty(d.Layer("VIEW_MASS").Items);
        }
    }
}
