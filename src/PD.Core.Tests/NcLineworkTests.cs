using PD.Core.Analysis;
using PD.Core.Axons;
using PD.Core.Classification;
using PD.Core.Drawing;
using PD.Core.Model;
using PD.Core.Plans;
using PD.Core.Sections;
using PD.Core.Settings;
using Xunit;

namespace PD.Core.Tests;

/// <summary>
/// User rev 0.8 roles on the Downtown model plus four synthetic NC objects (mm): an interior wall, a desk, the building
/// envelope (massing) and a free-standing screen south of the tower.
/// </summary>
public class NcLineworkTests
{
    const double Ft = 304.8;

    static readonly Lazy<AnalysisResult> Lazy = new(() =>
    {
        var m = DowntownAnalysis.Model;
        var layers = m.Layers.ToList();
        foreach (var n in new[] { "NC::INTERIOR", "NC::FURNITURE", "NC::MASSING", "NC::SCREEN" }) layers.Add(new LayerInfo(layers.Count, n, true, "#000000"));
        var objs = m.Objects.ToList();
        objs.Add(Box("wall", "NC::INTERIOR", 3200, 2400, 0.2, 3201, 2450, 18));
        objs.Add(Box("desk", "NC::FURNITURE", 3220, 2420, 0.2, 3226, 2423, 2.7));
        objs.Add(Box("envelope", "NC::MASSING", 3121.5, 2290.4, -23, 3409.5, 2554.4, 200));
        objs.Add(Panel("screen", "NC::SCREEN", 3150, 2280, 3250, 0, 12));
        return Analyzer.Run(new AnalysisInput { Model = new SourceModel { Units = m.Units, Layers = layers, Objects = objs } });
    });

    static ExportContext Context()
    {
        var c = DowntownAnalysis.Context();
        return new ExportContext { Result = Lazy.Value, Settings = c.Settings, Programs = c.Programs };
    }

    [Fact]
    public void RolesComeFromTheLayerNames_NoneIsAMass()
    {
        var r = Lazy.Value;
        Assert.Equal(Roles.Interior, r.Local.Objects.Single(o => o.Id == "wall").Role);
        Assert.Equal(Roles.Furniture, r.Local.Objects.Single(o => o.Id == "desk").Role);
        Assert.Equal(Roles.Massing, r.Local.Objects.Single(o => o.Id == "envelope").Role);
        Assert.Equal(Roles.Screen, r.Local.Objects.Single(o => o.Id == "screen").Role);
        Assert.DoesNotContain(r.Masses, x => new[] { "wall", "desk", "envelope", "screen" }.Contains(x.Id));
        Assert.Equal(DowntownAnalysis.Result.Levels.Count, r.Levels.Count);
    }

    [Fact]
    public void Plan_InteriorMidGrey_FurnitureLightGrey_MassingDashed_ScreenLine()
    {
        var ctx = Context();
        var d = PlanDrawing.Build(ctx);
        var l1 = d.Artboards.FindIndex(a => a.Name == "PLAN L1");
        DItem One(string layer) => Assert.Single(d.Layer(layer).Items, i => i.Artboard == l1);
        var hex = d.Swatches.ToDictionary(s => s.Name, s => s.Hex);
        Assert.Equal("#8C8C8C", hex[One("PD_INTERIOR").Stroke!]);
        Assert.Equal("#C8C8C8", hex[One("PD_FURNITURE").Stroke!]);
        Assert.NotNull(One("PD_MASSING").Dash);
        Assert.False(One("PD_SCREEN").Closed);
        // The desk stands on L1 only; the envelope is cut on every level.
        var l5 = d.Artboards.FindIndex(a => a.Name == "PLAN L5");
        Assert.DoesNotContain(d.Layer("PD_FURNITURE").Items, i => i.Artboard == l5);
        Assert.Contains(d.Layer("PD_MASSING").Items, i => i.Artboard == l5);
    }

    [Fact]
    public void Section_MassingCutDashed_SiteAxon_ScreenIsAThinPanel()
    {
        var ctx = Context();
        var b = ctx.Result.Building;
        var (x0, y0, _) = ctx.Result.Frame.ToModel(b.MinX - 40, (b.MinY + b.MaxY) / 2, 0);
        var (x1, y1, _) = ctx.Result.Frame.ToModel(b.MaxX + 40, (b.MinY + b.MaxY) / 2, 0);
        var s = SectionDrawing.Build(ctx, new SectionSetting { Name = "DT", Line = new[] { x0, y0, x1, y1 }, LookLeft = true, DepthFt = 400 });
        Assert.NotEmpty(s.Layer("PD_MASSING").Items);
        Assert.All(s.Layer("PD_MASSING").Items, i => Assert.NotNull(i.Dash));

        var ax = AxonDrawing.Build(ctx, AxonKind.Site);
        var screen = Assert.Single(ax.Layer("AXON_SCREEN").Items);
        Assert.NotEmpty(ax.Layer("AXON_MASSING").Items);
        // A 100 ft × 12 ft panel stays thin in the drawing (not a block).
        var ys = screen.Paths!.SelectMany(p => p).Select(q => q[1]).ToList();
        var xs = screen.Paths!.SelectMany(p => p).Select(q => q[0]).ToList();
        Assert.True(ys.Max() - ys.Min() < (xs.Max() - xs.Min()) * 1.2);
    }

    static SourceObject Box(string id, string layer, double x0, double y0, double z0, double x1, double y1, double z1)
    {
        double[] v = { x0, y0, z0, x1, y0, z0, x1, y1, z0, x0, y1, z0, x0, y0, z1, x1, y0, z1, x1, y1, z1, x0, y1, z1 };
        return new SourceObject
        {
            Id = id, LayerPath = layer, GeometryType = "Brep", IsClosed = true, FaceCount = 6,
            BBox = new BBox3(x0 * Ft, y0 * Ft, z0 * Ft, x1 * Ft, y1 * Ft, z1 * Ft),
            Mesh = new MeshData
            {
                Vertices = v.Select(x => x * Ft).ToArray(),
                Faces = new[] { new[] { 0, 3, 2, 1 }, new[] { 4, 5, 6, 7 }, new[] { 0, 1, 5, 4 }, new[] { 1, 2, 6, 5 }, new[] { 2, 3, 7, 6 }, new[] { 3, 0, 4, 7 } },
            },
        };
    }

    /// <summary>A single vertical surface along y = <paramref name="y"/>.</summary>
    static SourceObject Panel(string id, string layer, double x0, double y, double x1, double z0, double z1) => new()
    {
        Id = id, LayerPath = layer, GeometryType = "Brep", IsClosed = false, FaceCount = 1,
        BBox = new BBox3(x0 * Ft, y * Ft, z0 * Ft, x1 * Ft, y * Ft, z1 * Ft),
        Mesh = new MeshData { Vertices = new[] { x0, y, z0, x1, y, z0, x1, y, z1, x0, y, z1 }.Select(x => x * Ft).ToArray(), Faces = new[] { new[] { 0, 1, 2, 3 } } },
    };
}
