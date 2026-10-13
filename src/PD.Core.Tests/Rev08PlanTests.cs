using PD.Core.Drawing;
using PD.Core.Plans;
using Xunit;

namespace PD.Core.Tests;

/// <summary>User rev 0.8 plan requests on the Downtown model.</summary>
public class Rev08PlanTests
{
    static readonly Lazy<PD.Core.Drawing.Drawing> Plans = new(() => PlanDrawing.Build(DowntownAnalysis.Context()));

    [Fact]
    public void SitePlan_ShowsFiveTimesTheSiteEachWay()
    {
        var ctx = DowntownAnalysis.Context();
        var g = ctx.Result.Plans.FindIndex(p => p.Level.Name == "L1");
        var site = ctx.Result.Plans[g].Union.EnvelopeInternal;
        var env = PlanDrawing.SiteExtent(ctx, g);
        Assert.True(env.Width >= 10.9 * site.Width && env.Height >= 10.9 * site.Height, $"{env.Width} × {env.Height} for {site.Width} × {site.Height}");
    }

    [Fact]
    public void Plans_LevelTagIsPlainTextAtTheProgramLabelSize()
    {
        var d = Plans.Value;
        var ctx = DowntownAnalysis.Context();
        Assert.DoesNotContain(d.Layer("PD_ANNOTATION").Items, i => i.Name.EndsWith("TITLE_TAG"));
        var t = d.Layer("PD_ANNOTATION").Items.First(i => i.Name == "PLN_L1_TITLE");
        Assert.Equal(ctx.Pt(ctx.Style.Fonts.Label.SizePt), t.SizePt);
        Assert.True(d.ParaStyles.Single(p => p.Name == "PD Title").AllCaps);
    }

    [Fact]
    public void Plans_HardscapeJunctionsAndRoadEdges()
    {
        var d = Plans.Value;
        var l1 = d.Artboards.FindIndex(a => a.Name == "PLAN L1");
        var edges = d.Layer("BG_SITE_EDGE").Items.Where(i => i.Artboard == l1).ToList();
        var hex = d.Swatches.ToDictionary(s => s.Name, s => s.Hex);
        var hard = Assert.Single(edges, i => i.Name.EndsWith("HARDSCAPE_EDGE"));
        Assert.Equal(1.0, hard.StrokePt);
        Assert.True(hard.Opacity < 1);
        Assert.Equal("#4A4A4A", hex[hard.Stroke!]);
        Assert.All(edges, e => Assert.Null(e.Fill));
        Assert.Contains(d.Layer("BG_SITE_EDGE").Items, i => i.Name.EndsWith("ROAD_EDGE") && i.StrokePt == 1.5);
    }

    [Fact]
    public void Plans_LegendAndSymbolsArtboardKeepsEverything()
    {
        var d = Plans.Value;
        var a = d.Artboards.FindIndex(x => x.Name == "LEGEND & SYMBOLS");
        Assert.Equal(d.Artboards.Count - 1, a);
        var items = d.Layers.SelectMany(l => l.Items).Where(i => i.Artboard == a).ToList();
        var ctx = DowntownAnalysis.Context();
        Assert.All(ctx.Programs, p => Assert.Contains(items, i => i.Name == "LGD_PRG_" + p.Key));
        Assert.Contains(items, i => i.Name == "LGD_ROLE_NC_MASSING" && i.Dash != null);
        Assert.Contains(items, i => i.Name == "LGD_NORTH");
        Assert.Contains(items, i => i.Name == "LGD_PERSON_TOP");
        Assert.Equal(39, items.Count(i => i.Symbol?.StartsWith("PD_PERSON_P") == true));
        Assert.Equal(19 * ctx.Style.Entourage.TreeFills.Count, items.Count(i => i.Symbol?.StartsWith("PD_TREE_") == true));
    }

    [Fact]
    public void Seal_BlocksCloserThanTwoTenthsTouch_AreasUnchanged()
    {
        var r = DowntownAnalysis.Result;
        foreach (var plan in r.Plans.Where(p => !p.Union.IsEmpty))
        {
            var blocks = plan.Footprints.Where(f => !f.OpenToBelow && !f.Resolved.IsEmpty).ToList();
            // Every block lies inside the closed outline and no two overlap.
            Assert.All(blocks, f => Assert.True(f.Resolved.Difference(plan.Union).Area < 0.5, $"{plan.Level.Name} {f.Mass.Id}"));
            for (var i = 0; i < blocks.Count; i++)
            for (var j = i + 1; j < blocks.Count; j++)
                Assert.True(blocks[i].Resolved.Intersection(blocks[j].Resolved).Area < 0.05, $"{plan.Level.Name} overlap");
            // The outline has no slivers narrower than the seal gap: closing it again changes nothing.
            var again = plan.Union.Buffer(0.1).Buffer(-0.1);
            Assert.True(Math.Abs(again.Area - plan.Union.Area) < 0.02 * plan.Union.Area + 1, plan.Level.Name);
        }
    }
}
