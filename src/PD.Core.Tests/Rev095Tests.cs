using PD.Core.Analysis;
using PD.Core.Axons;
using PD.Core.Classification;
using PD.Core.Drawing;
using PD.Core.Model;
using PD.Core.Plans;
using Xunit;

namespace PD.Core.Tests;

/// <summary>
/// User rev 0.9.5 (RIVER 2026-10-22): site areas take material colours by layer / name; stairs (NC.Structure on a
/// stair layer) are drawn in plans and axons, white with a 0.5 pt dark grey line.
/// A 100 x 60 ft lab box on two storeys (feet), a 6-tread stair from 0 to 20 ft beside it, a gravel area and a lawn.
/// </summary>
public class Rev095Tests
{
    static readonly Lazy<AnalysisResult> Lazy = new(() =>
    {
        string[] names = { "PROGRAM::Lab", "PROGRAM::Stair", "PROJECT SITE::GARDENS::GRAVEL", "greens" };
        var layers = names.Select((n, i) => new LayerInfo(i, n, true, "#000000")).ToList();
        var objs = new List<SourceObject> { Box("lab1", "PROGRAM::Lab", 0, 0, 0, 100, 60, 18), Box("lab2", "PROGRAM::Lab", 0, 0, 20, 100, 60, 34) };
        for (var k = 0; k < 6; k++) objs.Add(Box($"tread{k}", "PROGRAM::Stair", 110 + 2 * k, 10, 3 * k, 112 + 2 * k, 16, 3 * k + 0.5));
        objs.Add(Box("gravel", "PROJECT SITE::GARDENS::GRAVEL", 0, -40, 0, 50, -10, 0.1));
        objs.Add(Box("lawn", "greens", 60, -40, 0, 100, -10, 0.1));
        var roles = new Dictionary<string, LayerGuess>
        {
            ["PROGRAM::Stair"] = new("PROGRAM::Stair", Roles.Structure, ""),
            ["PROJECT SITE::GARDENS::GRAVEL"] = new("PROJECT SITE::GARDENS::GRAVEL", Roles.NcHardscape, ""),
            ["greens"] = new("greens", Roles.Green, ""),
        };
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
    public void SiteMaterials_GravelAndLawnTakeTheirOwnColours()
    {
        var ctx = Ctx();
        var bg = PlanBackground.Collect(ctx, new NetTopologySuite.Geometries.Envelope(-100, 300, -100, 200));
        Assert.Equal("GRAVEL", bg.Single(b => b.Role == Roles.NcHardscape && b.Material != null).Material!.Key);
        Assert.Equal("LAWN", bg.First(b => b.Role == Roles.Green && b.Material != null).Material!.Key);
        var d = PlanDrawing.Build(ctx);
        Assert.Contains(d.Swatches, s => s.Name == "MAT_GRAVEL");
        Assert.Contains(d.Swatches, s => s.Name == "MAT_LAWN");
    }

    [Fact]
    public void Stairs_InPlanAndExplodedAxon_WhiteWithThinGreyLine()
    {
        var ctx = Ctx();
        var plan = PlanDrawing.Build(ctx);
        var st = plan.Layer("PD_STRUCTURE").Items.Where(i => i.Name.Contains("STAIR") && i.Artboard == 0).ToList();
        Assert.Equal(6, st.Count);                                   // every tread of the storey, seen from above
        Assert.All(st, i => { Assert.Equal("NC_STAIR", i.Fill); Assert.Equal(0.5, i.StrokePt); });
        Assert.Equal("#FFFFFF", plan.Swatches.First(s => s.Name == "NC_STAIR").Hex);
        var ax = AxonDrawing.Build(ctx, AxonKind.Exploded);
        Assert.Contains(ax.Layers.SelectMany(l => l.Items), i => i.Name.Contains("STAIR") && i.Fill == "NC_STAIR");
    }

    static SourceObject Box(string id, string layer, double x0, double y0, double z0, double x1, double y1, double z1)
    {
        double[] v = { x0, y0, z0, x1, y0, z0, x1, y1, z0, x0, y1, z0, x0, y0, z1, x1, y0, z1, x1, y1, z1, x0, y1, z1 };
        return new SourceObject
        {
            Id = id, LayerPath = layer, GeometryType = "Brep", IsClosed = true, FaceCount = 6, BBox = new BBox3(x0, y0, z0, x1, y1, z1),
            Mesh = new MeshData { Vertices = v, Faces = new[] { new[] { 0, 3, 2, 1 }, new[] { 4, 5, 6, 7 }, new[] { 0, 1, 5, 4 }, new[] { 1, 2, 6, 5 }, new[] { 2, 3, 7, 6 }, new[] { 3, 0, 4, 7 } } },
        };
    }
}
