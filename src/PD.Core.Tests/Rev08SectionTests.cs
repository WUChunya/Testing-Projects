using PD.Core.Drawing;
using PD.Core.Sections;
using PD.Core.Settings;
using Xunit;

namespace PD.Core.Tests;

/// <summary>User rev 0.8 section requests on the Downtown model.</summary>
public class Rev08SectionTests
{
    static PD.Core.Drawing.Drawing Section()
    {
        var ctx = DowntownAnalysis.Context();
        var b = ctx.Result.Building;
        var (x0, y0, _) = ctx.Result.Frame.ToModel(b.MinX - 40, (b.MinY + b.MaxY) / 2, 0);
        var (x1, y1, _) = ctx.Result.Frame.ToModel(b.MaxX + 40, (b.MinY + b.MaxY) / 2, 0);
        return SectionDrawing.Build(ctx, new SectionSetting { Name = "DT", Line = new[] { x0, y0, x1, y1 }, LookLeft = true, DepthFt = 400 });
    }

    static readonly Lazy<PD.Core.Drawing.Drawing> Dt = new(Section);

    [Theory]
    [InlineData("LAB", 200, "LAB")]
    [InlineData("CONFERENCE", 60, "CON")]
    [InlineData("CONFERENCE", 10, null)]
    [InlineData("LOBBY / BREAK", 400, "LOBBY / BREAK")]
    public void Labels_FullNameElseThreeLettersElseNothing(string text, double width, string? expected) =>
        Assert.Equal(expected, SectionBars.Fit(text, 12, width, 4));

    [Fact]
    public void Labels_CentredInEveryBlock_RepeatedForEachBlock()
    {
        var d = Dt.Value;
        var bars = d.Layer("PD_PROGRAM").Items.Where(i => i.Kind == ItemKind.RoundRect).ToList();
        var labels = d.Layer("PD_LABELS").Items.Where(i => i.Name.EndsWith("_LBL") && i.Name.StartsWith("SEC_DT_") && !i.Name.Contains("VIEW")).ToList();
        foreach (var l in labels)
        {
            var bar = bars.Single(b => b.Name + "_LBL" == l.Name);
            Assert.Equal(bar.X + bar.W / 2, l.X, 3);
            Assert.True(l.Center);
        }
        // Several LAB blocks, each with its own label.
        Assert.True(labels.Count(l => l.Text == "LAB") >= 5);
    }

    [Fact]
    public void Penthouse_GreyBlock_NoSlabOver_TopMarker()
    {
        var d = Dt.Value;
        var pent = Assert.Single(d.Layer("PD_PROGRAM").Items, i => i.Name.StartsWith("SEC_DT_PENTHOUSE"));
        Assert.Equal("PD_PENTHOUSE", pent.Fill);
        Assert.Contains(d.Layer("PD_LABELS").Items, i => i.Text == "PENTHOUSE");
        // Nothing of the slab layer lies above the penthouse's floor.
        Assert.DoesNotContain(d.Layer("PD_SLAB").Items, s => s.Paths![0].Max(q => q[1]) < pent.Y + pent.H - 1 && s.Paths![0].Min(q => q[0]) < pent.X + pent.W && s.Paths![0].Max(q => q[0]) > pent.X);
        Assert.Contains(d.Layer("PD_ANNOTATION").Items, i => i.Name == "SEC_DT_PENTHOUSE_TOP_TXT" && i.Text == "T.O. PENTHOUSE");
        var mark = Assert.Single(d.Layer("PD_LEVELS").Items, i => i.Name == "SEC_DT_PENTHOUSE_TOP_MARK");
        Assert.Equal(pent.Y, mark.Paths![0].Max(q => q[1]), 1);
    }

    [Fact]
    public void People_RandomlySpaced_ClearOfLabels()
    {
        var d = Dt.Value;
        var people = d.Layer("ENTOURAGE").Items;
        Assert.True(people.Count > 20);
        // Gaps on one floor line are not all alike (no even spacing).
        var gaps = people.GroupBy(p => Math.Round(p.Y)).Where(g => g.Count() >= 3)
            .SelectMany(g => g.OrderBy(p => p.X).Zip(g.OrderBy(p => p.X).Skip(1), (a, b) => b.X - a.X)).ToList();
        Assert.True(gaps.Count >= 4);
        Assert.True(gaps.Max() > 2 * gaps.Min());
        var labels = d.Layer("PD_LABELS").Items.Where(i => i.Name.EndsWith("_LBL") && i.SizePt != null).ToList();
        foreach (var p in people)
        foreach (var l in labels)
        {
            var half = SectionBars.TextWidth(l.Text!, l.SizePt!.Value) / 2;
            var h = p.Matrix![3] * PD.Core.Entourage.EntourageArt.UnitPt;
            var hit = p.X > l.X - half && p.X < l.X + half && p.Y > l.Y && p.Y - h < l.Y;
            Assert.False(hit, $"{p.Name} on {l.Name}");
        }
    }

    /// <summary>Elevations use the section's level column, and the dashed level lines of both span the whole drawing.</summary>
    [Fact]
    public void Elevation_LevelLinesLikeTheSection_AcrossTheWholeDrawing()
    {
        var ctx = DowntownAnalysis.Context();
        var b = ctx.Result.Building;
        var (x0, y0, _) = ctx.Result.Frame.ToModel(b.MinX - 40, b.MinY - 60, 0);
        var (x1, y1, _) = ctx.Result.Frame.ToModel(b.MaxX + 40, b.MinY - 60, 0);
        var el = PD.Core.Elevations.ElevationDrawing.Build(ctx, new SectionSetting { Name = "S", Kind = SectionSetting.ElevationKind, Line = new[] { x0, y0, x1, y1 }, LookLeft = true, DepthFt = 400 });
        foreach (var d in new[] { el, Dt.Value })
        {
            var ground = d.Layer("PD_GROUND").Items.Single();
            var right = ground.Paths![0].Max(q => q[0]);
            var dashes = d.Layer("PD_LEVELS").Items.Where(i => i.Name.EndsWith("_DASH")).ToList();
            Assert.True(dashes.Count >= 12);
            Assert.All(dashes, l => Assert.Equal(right, l.Paths![0].Max(q => q[0]), 1));
            Assert.Contains(d.Layer("PD_ANNOTATION").Items, i => i.Name.EndsWith("_LVL_L11_TXT") && i.Text == "L11 ROOF" && i.ParaStyle == "PD Note");
            Assert.Contains(d.Layer("PD_ANNOTATION").Items, i => i.Name.EndsWith("PENTHOUSE_TOP_TXT"));
        }
    }
}
