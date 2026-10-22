using PD.Core.Analysis;
using PD.Core.Classification;
using PD.Core.Drawing;
using PD.Core.Model;
using PD.Core.Plans;
using Xunit;

namespace PD.Core.Tests;

/// <summary>
/// User rev 0.9.6 (RIVER 2026-10-22): our sloped ground over a block (the stepped slope garden over the parking) is seen
/// from above in the site plan and the roof view; a terrace or roof garden on a roof shows in the roof view only.
/// A lab (0-18 ft) with a 1.5 ft terrace on part of its roof, a parking block (0-9 ft) under a slope rising 0 → 14 ft.
/// </summary>
public class Rev096Tests
{
    static readonly Lazy<AnalysisResult> Lazy = new(() =>
    {
        string[] names = { "PROGRAM::Lab", "PROGRAM::Parking", "greens" };
        var layers = names.Select((n, i) => new LayerInfo(i, n, true, "#000000")).ToList();
        var objs = new List<SourceObject>
        {
            Prism("lab", "PROGRAM::Lab", 0, 0, 100, 60, 0, 0, 18, 18),
            Prism("park", "PROGRAM::Parking", 120, 0, 180, 60, 0, 0, 9, 9),
            Prism("terrace", "greens", 60, 0, 100, 60, 18, 18, 19.5, 19.5),
            Prism("slope", "greens", 120, -20, 180, 60, -1, 13, 0, 14),
        };
        var roles = new Dictionary<string, LayerGuess> { ["greens"] = new("greens", Roles.Green, "") };
        return Analyzer.Run(new AnalysisInput
        {
            Model = new SourceModel { Units = "Feet", Layers = layers, Objects = objs }, RoleOverrides = roles,
            Levels = new[] { new LevelOverride("L1", 0, false), new LevelOverride("L2", 20, false) },
        });
    });

    static ExportContext Ctx()
    {
        var c = DowntownAnalysis.Context();
        return new ExportContext { Result = Lazy.Value, Settings = c.Settings, Programs = c.Programs };
    }

    [Fact]
    public void SlopeClimbs_TerraceDoesNot()
    {
        var o = Lazy.Value.Local.Objects;
        Assert.True(PlanBackground.Climbs(o.Single(x => x.Id == "slope").Mesh!, 3));
        Assert.False(PlanBackground.Climbs(o.Single(x => x.Id == "terrace").Mesh!, 3));
    }

    [Fact]
    public void SiteAbove_SlopeHidesTheParking_TerraceOnlyFromAbove()
    {
        var ctx = Ctx();
        var fp = ctx.Result.Plans[0].Footprints;
        var park = fp.Single(f => f.Mass.Id == "park");
        var lab = fp.Single(f => f.Mass.Id == "lab");
        Assert.True(PlanBackground.SiteRemoved(ctx, park.Resolved, park.Mass.Source.BBox.MaxZ, slopedOnly: true).Area < 1);
        Assert.Equal(lab.Resolved.Area, PlanBackground.SiteRemoved(ctx, lab.Resolved, lab.Mass.Source.BBox.MaxZ, slopedOnly: true).Area, 1);
        Assert.InRange(PlanBackground.SiteRemoved(ctx, lab.Resolved, lab.Mass.Source.BBox.MaxZ, slopedOnly: false).Area, 3550, 3650);
        var d = PlanDrawing.BuildSite(ctx);
        Assert.Contains(d.Layer("PD_PROGRAM").Items, i => i.Artboard == 1);
    }

    /// <summary>A slab whose top runs from z0 (at y0) to z1 (at y1); its bottom from b0 to b1.</summary>
    static SourceObject Prism(string id, string layer, double x0, double y0, double x1, double y1, double b0, double b1, double z0, double z1)
    {
        double[] v = { x0, y0, b0, x1, y0, b0, x1, y1, b1, x0, y1, b1, x0, y0, z0, x1, y0, z0, x1, y1, z1, x0, y1, z1 };
        return new SourceObject
        {
            Id = id, LayerPath = layer, GeometryType = "Brep", IsClosed = true, FaceCount = 6,
            BBox = new BBox3(x0, y0, Math.Min(b0, b1), x1, y1, Math.Max(z0, z1)),
            Mesh = new MeshData { Vertices = v, Faces = new[] { new[] { 0, 3, 2, 1 }, new[] { 4, 5, 6, 7 }, new[] { 0, 1, 5, 4 }, new[] { 1, 2, 6, 5 }, new[] { 2, 3, 7, 6 }, new[] { 3, 0, 4, 7 } } },
        };
    }
}
