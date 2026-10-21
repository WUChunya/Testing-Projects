using PD.Core.Axons;
using PD.Core.Drawing;
using Xunit;

namespace PD.Core.Tests;

/// <summary>User rev 0.9.3: axon style after the reference (slabs with program volumes, open space flat, larger labels).</summary>
public class Rev093Tests
{
    static readonly FixtureAnalysis Tk = new("river_tk.json", "river_tk_settings.json");

    /// <summary>Thickest face (page pt, 2 × area / perimeter: the height of a thin side) painted in the given swatch.</summary>
    static double Tallest(PD.Core.Drawing.Drawing d, Func<string, bool> fill) =>
        d.Layers.SelectMany(l => l.Items).Where(i => i.Fill != null && fill(i.Fill) && i.Paths != null)
            .SelectMany(i => i.Paths!).Where(p => p.Length >= 3).Select(Thickness).DefaultIfEmpty(0).Max();

    static double Thickness(double[][] p)
    {
        double a = 0, per = 0;
        for (var i = 0; i < p.Length; i++)
        {
            var (q, r) = (p[i], p[(i + 1) % p.Length]);
            a += q[0] * r[1] - r[0] * q[1];
            per += Math.Sqrt((r[0] - q[0]) * (r[0] - q[0]) + (r[1] - q[1]) * (r[1] - q[1]));
        }
        return per < 1e-9 ? 0 : Math.Abs(a) / per;
    }

    [Fact]
    public void ExplodedA_SlabsCarryVolumes_OpenSpaceIsFlat()
    {
        // "interaction和circulation一样，属于开放空间，两者都不是volume，是一层叠加在楼板上的信息"
        var ctx = Tk.Context();
        var d = AxonDrawing.Build(ctx, AxonKind.Exploded);
        var items = d.Layers.SelectMany(l => l.Items).ToList();
        Assert.Contains(items, i => i.Name.Contains("_SLAB"));
        var k = ctx.PtPerFt;
        // Circulation has no standing sides; a lab's sides are the volume height (projected, at least half of it).
        Assert.True(Tallest(d, f => f.Contains("CIRCULATION") && f.Contains("_SIDE")) < 1.0 * k, "circulation stands up");
        Assert.True(Tallest(d, f => f.Contains("LAB") && f.Contains("_SIDE")) > 0.5 * ctx.Style.Axon.Exploded.VolumeHeightFt * k, "labs are flat");
    }

    [Fact]
    public void FloorKeyAndExplodedB_OpenSpaceIsFlat()
    {
        var ctx = Tk.Context();
        foreach (var kind in new[] { AxonKind.ExplodedB, AxonKind.Levels })
            Assert.True(Tallest(AxonDrawing.Build(ctx, kind), f => f.Contains("CIRCULATION") && f.Contains("_SIDE")) < 1.0 * ctx.PtPerFt, kind.ToString());
    }

    [Fact]
    public void Labels_LargerAndAMinimumStyle()
    {
        var ctx = Tk.Context();
        var d = AxonDrawing.Build(ctx, AxonKind.Exploded);
        Assert.Contains(d.ParaStyles, s => s.Name == "PD Plane Label Min");
        var label = d.ParaStyles.First(s => s.Name == "PD Plane Label");
        Assert.True(label.SizePt >= ctx.Pt(30), $"{label.SizePt}");
        Assert.True(d.Layers.SelectMany(l => l.Items).Count(i => i.Text != null && i.Name.EndsWith("_LBL")) >= 20);
    }
}
