using PD.Core.Analysis;
using PD.Core.Classification;
using PD.Core.Drawing;
using PD.Core.Model;
using PD.Core.Sections;
using PD.Core.Settings;
using Xunit;

namespace PD.Core.Tests;

/// <summary>Synthetic block with one parking level below grade (the test model has none).</summary>
public class BasementSectionTests
{
    static MeshData Box(double x0, double y0, double z0, double x1, double y1, double z1) => new()
    {
        Vertices = new[] { x0, y0, z0, x1, y0, z0, x1, y1, z0, x0, y1, z0, x0, y0, z1, x1, y0, z1, x1, y1, z1, x0, y1, z1 },
        Faces = new[]
        {
            new[] { 0, 3, 2, 1 }, new[] { 4, 5, 6, 7 }, new[] { 0, 1, 5, 4 }, new[] { 1, 2, 6, 5 }, new[] { 2, 3, 7, 6 }, new[] { 3, 0, 4, 7 },
        },
    };

    static SourceObject Obj(string id, string layer, double z0, double z1) => new()
    {
        Id = id, LayerPath = layer, GeometryType = "Brep", BBox = new BBox3(0, 0, z0, 100, 60, z1),
        IsClosed = true, FaceCount = 6, Mesh = Box(0, 0, z0, 100, 60, z1),
    };

    [Fact]
    public void BasementProgramsDrawBelowTheGroundLine()
    {
        var model = new SourceModel
        {
            Units = "Feet",
            Layers = new[] { new LayerInfo(0, "slab", true, "#000000"), new LayerInfo(1, "STORAGE", true, "#000000"), new LayerInfo(2, "OFFICE", true, "#000000") },
            Objects = new[]
            {
                Obj("s0", "slab", -13, -12), Obj("s1", "slab", -1, 0), Obj("s2", "slab", 13, 14), Obj("s3", "slab", 27, 28),
                Obj("b1", "STORAGE", -12, -0.5), Obj("l1", "OFFICE", 0, 13), Obj("l2", "OFFICE", 14, 27),
            },
        };
        var r = Analyzer.Run(new AnalysisInput { Model = model });
        Assert.Equal(new[] { "B1", "L1", "L2", "L3" }, r.Levels.Select(l => l.Name).ToArray());

        var settings = new PdSettings();
        settings.Output.FillDefaults(PD.Core.Config.Rules.Default().Output, "");
        var ctx = new ExportContext { Result = r, Settings = settings, Programs = ProgramCatalog.Effective(LayerClassifier.LoadPrograms(), null) };
        var d = SectionDrawing.Build(ctx, new SectionSetting { Name = "S", Line = new double[] { -10, 30, 110, 30 } });

        var ground = d.Layer("PD_GROUND").Items.Single();
        var groundMid = (ground.Paths![0][0][1] + ground.Paths[0][2][1]) / 2;
        var bars = d.Layer("PD_PROGRAM").Items;
        Assert.Contains(bars, b => b.Y > groundMid && b.Fill == "PRG_BUILDING_SUPPORT"); // parking bar below grade
        Assert.Contains(bars, b => b.Y + b.H < groundMid && b.Fill == "PRG_OFFICE");
        Assert.Contains(d.Layer("PD_ANNOTATION").Items, i => i.Name == "SEC_S_LVL_B1_TXT");
        // The whole basement is on the artboard.
        Assert.All(bars, b => Assert.InRange(b.Y + b.H, 0, d.Artboards[0].H));
    }
}
