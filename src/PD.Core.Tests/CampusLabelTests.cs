using PD.Core.Drawing;
using PD.Core.Plans;
using Xunit;

namespace PD.Core.Tests;

/// <summary>CAMPUS 10-23 output keys: no leaders, names shrink to fit, only rooms with a name of their own.</summary>
public class CampusLabelTests
{
    static ExportContext Ctx(bool namedOnly)
    {
        var c = S3SectionTests.Context();
        var ctx = new ExportContext { Result = c.Result, Settings = c.Settings, Programs = c.Programs, Style = PD.Core.Config.Style.Default() };
        if (namedOnly)
        {
            var ps = ctx.Style.Plan;
            ps.LabelUniform = false; ps.LabelLeaders = false; ps.LabelNamedOnly = true; ps.LabelMinPt = 6;
        }
        return ctx;
    }

    static string Norm(string t) => string.Join(" ", t.Split(new[] { '\n', '\r', ' ' }, StringSplitOptions.RemoveEmptyEntries)).ToUpperInvariant();

    [Fact]
    public void NamedOnly_NoLeadersAndOnlyOwnNames()
    {
        var ctx = Ctx(true);
        var d = PlanDrawing.Build(ctx);
        var labels = d.Layer("PD_LABELS").Items;
        Assert.DoesNotContain(labels, i => i.Name.EndsWith("_LDR"));
        var own = ctx.Names.Values.Where(n => n.Own).Select(n => n.Label).ToHashSet();
        var generic = ctx.Programs.Select(p => p.Label.ToUpperInvariant()).Where(x => !own.Contains(x)).ToHashSet();
        Assert.DoesNotContain(labels, i => i.Name.EndsWith("_LBL") && i.Text != null && generic.Contains(Norm(i.Text)));
        Assert.DoesNotContain(labels, i => i.Text == ctx.Style.Plan.OpenToBelowLabel);
    }

    [Fact]
    public void Default_KeepsProgramLabels()
    {
        var ctx = Ctx(false);
        Assert.True(ctx.Style.Plan.LabelLeaders);
        Assert.False(ctx.Style.Plan.LabelNamedOnly);
        var d = PlanDrawing.Build(ctx);
        // Unnamed blocks still carry their program's label.
        var generic = ctx.Programs.Select(p => p.Label.ToUpperInvariant()).ToHashSet();
        Assert.Contains(d.Layer("PD_LABELS").Items, i => i.Text != null && generic.Contains(Norm(i.Text)));
    }
}
