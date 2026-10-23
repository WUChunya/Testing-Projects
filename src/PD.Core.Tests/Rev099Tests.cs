using PD.Core.Analysis;
using PD.Core.Axons;
using PD.Core.Classification;
using PD.Core.Drawing;
using PD.Core.Model;
using Xunit;

namespace PD.Core.Tests;

/// <summary>
/// User rev 0.9.9 (RIVER 1022 axons): a mezzanine (the parking deck) is no plate of its own in the axons — its blocks
/// stand on the main level below, so the L1 blocks are no tall spaces stretched past it.
/// A 100 x 60 ft lab on two storeys (L1 0–18, L2 20–34), parking 0–9 ft and its upper deck 10–18 ft from a 10 ft mezzanine.
/// </summary>
public class Rev099Tests
{
    static readonly Lazy<AnalysisResult> Lazy = new(() =>
    {
        string[] names = { "PROGRAM::Lab", "PROGRAM::Parking" };
        var layers = names.Select((n, i) => new LayerInfo(i, n, true, "#000000")).ToList();
        var objs = new List<SourceObject>
        {
            Box("lab1", "PROGRAM::Lab", 0, 0, 0, 100, 60, 18), Box("lab2", "PROGRAM::Lab", 0, 0, 20, 100, 60, 34),
            Box("park", "PROGRAM::Parking", 120, 0, 0, 180, 60, 9), Box("deck", "PROGRAM::Parking", 120, 0, 10, 180, 60, 18),
        };
        return Analyzer.Run(new AnalysisInput
        {
            Model = new SourceModel { Units = "Feet", Layers = layers, Objects = objs },
            Levels = new[] { new LevelOverride("L1", 0, false), new LevelOverride("L1M", 10, true), new LevelOverride("L2", 20, false) },
        });
    });

    static ExportContext Ctx()
    {
        var c = DowntownAnalysis.Context();
        return new ExportContext { Result = Lazy.Value, Settings = c.Settings, Programs = c.Programs };
    }

    [Fact]
    public void MezzanineBlocks_GoWithTheMainLevel()
    {
        var r = Lazy.Value;
        var blocks = AxonDrawing.OnMainLevels(r, ProgramBlocks.Build(r));
        var deck = blocks.Single(b => b.MassId == "deck");
        Assert.True(deck.FromMezzanine);
        Assert.Equal(0, deck.LevelIndex);
        Assert.DoesNotContain(blocks, b => r.Levels[b.LevelIndex].IsMezzanine);
    }

    [Fact]
    public void Axons_NoMezzaninePlate_NoTallStretch()
    {
        var ctx = Ctx();
        var b = AxonDrawing.Build(ctx, AxonKind.ExplodedB);
        var items = b.Layers.SelectMany(l => l.Items).ToList();
        Assert.DoesNotContain(items, i => i.Name.Contains("_TALL_"));
        Assert.DoesNotContain(b.Layers, l => l.Name == "AXON_L1M" && l.Items.Count > 0);
        var levels = AxonDrawing.Build(ctx, AxonKind.Levels);
        Assert.DoesNotContain(levels.Artboards, a => a.Name.Contains("L1M"));
        var a = AxonDrawing.Build(ctx, AxonKind.Exploded);
        Assert.DoesNotContain(a.Layers, l => l.Name == "AXON_L1M" && l.Items.Count > 0);
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
