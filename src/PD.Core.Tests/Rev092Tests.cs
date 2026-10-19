using PD.Core.Axons;
using PD.Core.Drawing;
using PD.Core.Plans;
using PD.Core.Sections;
using PD.Core.Writers;
using Xunit;

namespace PD.Core.Tests;

/// <summary>User rev 0.9.2: RIVER TK model; 11x17 sheets at 1" = 100' centred on the property line.</summary>
public class Rev092Tests
{
    /// <summary>RIVER 2026-10-19 TK model, its settings carried over, output set to 11x17 Landscape at 1" = 100'-0".</summary>
    static readonly FixtureAnalysis Tk = new("river_tk.json", "river_tk_settings.json");

    [Fact]
    public void TkModel_AnalysesAndEveryDrawingBuilds()
    {
        // The level overlay once returned a GeometryCollection (polygon + sliver line) and the next overlay threw.
        var r = Tk.Result;
        Assert.Equal(5, r.Levels.Count);
        var notes = new List<string>();
        var drawings = Exporter.BuildDrawings(Tk.Context(), notes);
        Assert.Contains(drawings, d => d.FileName == "04_Axon_Exploded_B.ai");
        Assert.DoesNotContain(notes, n => n.Contains("failed", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void FixedArtboard_PlansCentredOnThePropertyLine()
    {
        var ctx = Tk.Context();
        var d = PlanDrawing.BuildSite(ctx);
        Assert.Equal((1224.0, 792.0), (d.Artboards[0].W, d.Artboards[0].H));
        var pl = d.Layers.Where(l => l.Name == "PD_PROPERTY_LINE").SelectMany(l => l.Items).Where(i => i.Artboard == 0 && i.Paths != null)
            .SelectMany(i => i.Paths!).SelectMany(p => p).ToList();
        Assert.NotEmpty(pl);
        double cx = (pl.Min(p => p[0]) + pl.Max(p => p[0])) / 2;
        Assert.InRange(cx, 1224 / 2.0 - 2, 1224 / 2.0 + 2);
    }

    [Fact]
    public void FixedArtboard_TooLargeDrawingsFitTheirContentAtTheSameScale()
    {
        var ctx = Tk.Context();
        var exploded = AxonDrawing.Build(ctx, AxonKind.ExplodedB);
        // The exploded stack is taller than 11x17: its artboard grows instead (same 1" = 100' scale), with a note.
        Assert.True(exploded.Artboards[0].H > 792);
        Assert.Contains(exploded.Notes, n => n.Contains("fits the content"));
        var stacked = AxonDrawing.Build(ctx, AxonKind.Stacked);
        Assert.Equal((1224.0, 792.0), (stacked.Artboards[0].W, stacked.Artboards[0].H));
        Assert.DoesNotContain(stacked.Notes, n => n.Contains("larger than"));
    }

    [Fact]
    public void FixedArtboard_SectionsCentreWhereTheCutCrossesThePropertyLine()
    {
        var ctx = Tk.Context();
        var sec = ctx.Settings.Sections.First(s => s.Name == "A");
        var d = SectionDrawing.Build(ctx, sec);
        Assert.Equal(1224.0, d.Artboards[0].W);
        Assert.DoesNotContain(d.Notes, n => n.Contains("larger than"));
    }

    [Fact]
    public void Place_KeepsTheDrawingOnThePage()
    {
        Assert.Equal(400, ExportContext.Place(0, 1000, 200, 100));   // anchor mid-span
        Assert.Equal(100, ExportContext.Place(0, 1000, 900, 100));   // pushed back inside
        Assert.Null(ExportContext.Place(0, 1000, 1200, 600));        // does not fit
    }
}

/// <summary>CAMPUS 2026-10-19 (deliverables/2026-10-19_CAMPUS): Town site plan context, same output as RIVER.</summary>
public class CampusTests
{
    static readonly FixtureAnalysis Campus = new("campus.json", "campus_settings.json");

    [Fact]
    public void Campus_LevelsAndTheAuditoriumVoid()
    {
        var r = Campus.Result;
        Assert.Equal(6, r.Levels.Count);
        // The seminar room / auditorium is kept tall: on L2 its upper half is a void, and the whole-floor L2
        // circulation mass must not count there.
        var l2 = r.Plans[1];
        var otb = l2.Footprints.Where(f => f.OpenToBelow).ToList();
        Assert.NotEmpty(otb);
        foreach (var f in l2.Footprints.Where(f => !f.OpenToBelow && !f.Resolved.IsEmpty))
            foreach (var v in otb)
                Assert.True(f.Resolved.Intersection(v.Raw).Area < 1, $"{f.Mass.ProgramKey} counts inside the void");
        Assert.Equal(0, l2.AreaByProgram.GetValueOrDefault("ASSEMBLY"));
    }

    [Fact]
    public void Campus_SitePlanCentredOnTheParcel()
    {
        var d = PlanDrawing.BuildSite(Campus.Context());
        Assert.Equal((1224.0, 792.0), (d.Artboards[0].W, d.Artboards[0].H));
        var pl = d.Layers.Where(l => l.Name == "PD_PROPERTY_LINE").SelectMany(l => l.Items).Where(i => i.Artboard == 0 && i.Paths != null)
            .SelectMany(i => i.Paths!).SelectMany(p => p).ToList();
        Assert.NotEmpty(pl);
        Assert.InRange((pl.Min(p => p[0]) + pl.Max(p => p[0])) / 2, 610, 614);
    }
}
