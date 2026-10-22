using PD.Core.Analysis;
using PD.Core.Classification;
using PD.Core.Config;
using PD.Core.Geometry;
using PD.Core.Model;
using PD.Core.Sections;
using Xunit;

namespace PD.Core.Tests;

/// <summary>
/// User rev 0.9.4 (RIVER DRAFT_1 run): the glass is the envelope and the rest inside it is circulation; a 2 ft
/// interaction plate on the floor is a room on that floor (not paving, not lost); section slabs stay in the cut building
/// and a mezzanine floor only under the blocks that start on it.
/// A 100 x 60 ft two-storey box (feet): glass all round, a lab on the west 40 ft of both storeys, a 2.05 ft
/// INTERACTIVE plate on the ground floor, a site-wide floor plate at 18–20 ft, a 30 x 60 ft parking deck block from a
/// 10 ft mezzanine on the east.
/// </summary>
public class Rev094Tests
{
    static readonly Lazy<AnalysisResult> Lazy = new(() =>
    {
        string[] names = { "PROGRAM::Lab", "PROGRAM::INTERACTIVE", "PROGRAM::Glass", "PROGRAM::Parking", "FLOOR PLATE" };
        var layers = names.Select((n, i) => new LayerInfo(i, n, true, "#000000")).ToList();
        var objs = new List<SourceObject>
        {
            Box("lab1", "PROGRAM::Lab", 0, 0, 0, 40, 60, 18), Box("lab2", "PROGRAM::Lab", 0, 0, 20, 40, 60, 34),
            Box("talk", "PROGRAM::INTERACTIVE", 50, 10, 0, 80, 50, 2.05),
            Box("plate", "FLOOR PLATE", -200, -200, 18, 300, 260, 20),
            Box("deck", "PROGRAM::Parking", 70, 0, 10, 100, 60, 18),
        };
        foreach (var (z0, z1, s) in new[] { (0.0, 18.0, "a"), (20.0, 34.0, "b") })
        {
            objs.Add(Panel($"g{s}1", 0, 0, 100, 0, z0, z1)); objs.Add(Panel($"g{s}2", 100, 0, 100, 60, z0, z1));
            objs.Add(Panel($"g{s}3", 100, 60, 0, 60, z0, z1)); objs.Add(Panel($"g{s}4", 0, 60, 0, 0, z0, z1));
        }
        var roles = new Dictionary<string, LayerGuess>
        {
            ["PROGRAM::Glass"] = new("PROGRAM::Glass", Roles.Facade, ""),
            ["FLOOR PLATE"] = new("FLOOR PLATE", Roles.Slab, ""),
        };
        return Analyzer.Run(new AnalysisInput
        {
            Model = new SourceModel { Units = "Feet", Layers = layers, Objects = objs }, RoleOverrides = roles,
            Levels = new[] { new LevelOverride("L1", 0, false), new LevelOverride("L1M", 10, true), new LevelOverride("L2", 20, false) },
        });
    });

    [Fact]
    public void Interactive_IsTheInteractionProgram()
    {
        Assert.Equal("INTERACTION", LayerClassifier.Default().Guess(new LayerInfo(0, "PROGRAM::INTERACTIVE", true, "#000000")).ProgramKey);
    }

    [Fact]
    public void GlassEnvelope_RestInsideIsCirculation()
    {
        var r = Lazy.Value;
        Assert.Contains(r.Masses, m => m.Source.LayerPath == GlassEnvelope.Layer);
        // L1: 6000 sf inside the glass − lab 2400 − interaction 1200 − parking under the deck (none at L1) = 2400.
        var l1 = r.Plans[0].AreaByProgram;
        Assert.InRange(l1.GetValueOrDefault("CIRCULATION"), 2100, 2500);
        Assert.InRange(l1.GetValueOrDefault("LAB"), 2300, 2500);
        // L2: 6000 − 2400 lab = 3600.
        Assert.InRange(r.Plans[2].AreaByProgram.GetValueOrDefault("CIRCULATION"), 3300, 3700);
    }

    [Fact]
    public void ThinInteractionPlate_OnItsFloor_NotPaving()
    {
        var r = Lazy.Value;
        Assert.DoesNotContain(r.Issues, i => i.Kind == IssueKind.SitePaving);
        Assert.InRange(r.Plans[0].AreaByProgram.GetValueOrDefault("INTERACTION"), 1100, 1250);
    }

    [Fact]
    public void SectionSlabs_StayInTheBuilding_MezzanineOnlyUnderItsBlocks()
    {
        var r = Lazy.Value;
        var g = SectionGeometry.Build(r, new SectionPlane(-150, 30, 250, 30, true), 50, Rules.Default(), Style.Default());
        var at20 = g.SlabLines.Where(s => Math.Abs(s.Z - 20) < 0.5).ToList();
        Assert.NotEmpty(at20);
        // the site-wide plate does not carry the floor past the building (u 150 … 250 along the cut)
        Assert.All(at20, s => Assert.True(s.U0 >= 145 && s.U1 <= 255, $"{s.U0:0}–{s.U1:0}"));
        var at10 = g.SlabLines.Where(s => Math.Abs(s.Z - 10) < 0.5).ToList();
        Assert.NotEmpty(at10);
        Assert.All(at10, s => Assert.True(s.U1 - s.U0 < 35, $"mezzanine slab {s.U0:0}–{s.U1:0}"));
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

    static SourceObject Panel(string id, double x0, double y0, double x1, double y1, double z0, double z1) => new()
    {
        Id = id, LayerPath = "PROGRAM::Glass", GeometryType = "Brep", IsClosed = false, FaceCount = 1,
        BBox = new BBox3(Math.Min(x0, x1), Math.Min(y0, y1), z0, Math.Max(x0, x1), Math.Max(y0, y1), z1),
        Mesh = new MeshData { Vertices = new[] { x0, y0, z0, x1, y1, z0, x1, y1, z1, x0, y0, z1 }, Faces = new[] { new[] { 0, 1, 2, 3 } } },
    };
}
