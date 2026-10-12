using PD.Core.Analysis;
using PD.Core.Classification;
using PD.Core.Model;
using PD.Core.Settings;
using Xunit;

namespace PD.Core.Tests;

/// <summary>Layer roles: NC.* (our new construction) vs BG.* (context).</summary>
public class RoleTests
{
    [Theory]
    [InlineData("CORE", Roles.Core)]
    [InlineData("STAIR 2", Roles.Stair)]
    [InlineData("ELEVATOR", Roles.Stair)]
    [InlineData("COLUMNS", Roles.Structure)]
    [InlineData("GREEN ROOF", Roles.Green)]
    [InlineData("GLASS", Roles.Facade)]
    [InlineData("slab", Roles.Slab)]
    [InlineData("TREES", Roles.Tree)]
    [InlineData("NC::TREES", Roles.NcTree)]
    [InlineData("PROJECT::PLAZA", Roles.NcHardscape)]
    [InlineData("NEW::LAWN", Roles.Green)]
    [InlineData("SITE BLOCKS", Roles.Hardscape)]
    [InlineData("CONTEXT BUILDINGS", Roles.Building)]
    [InlineData("LAB", Roles.Program)]
    public void Keywords(string path, string role)
    {
        Assert.Equal(role, LayerClassifier.Default().Guess(new LayerInfo(0, path, true, "#000000")).Role);
        Assert.Contains(role, Roles.All);
    }

    [Fact]
    public void LegacyRolesAreRenamed()
    {
        var s = PdSettings.FromJson("{\"layers\":{\"GLASS\":{\"use\":true,\"role\":\"BG.Facade\"},\"slab\":{\"use\":true,\"role\":\"BG.Slab\"}}}");
        Assert.Equal(Roles.Facade, s.Layers["GLASS"].Role);
        Assert.Equal(Roles.Slab, s.Layers["slab"].Role);
    }

    [Fact]
    public void CoreAndStairLayersAreMassesWithTheirProgram()
    {
        BBox3 B(double z0, double z1) => new(0, 0, z0, 10, 10, z1);
        MeshData Box(double z0, double z1)
        {
            var v = new List<double>();
            foreach (var z in new[] { z0, z1 }) v.AddRange(new double[] { 0, 0, z, 10, 0, z, 10, 10, z, 0, 10, z });
            return new MeshData
            {
                Vertices = v.ToArray(),
                Faces = new[] { new[] { 0, 3, 2, 1 }, new[] { 4, 5, 6, 7 }, new[] { 0, 1, 5, 4 }, new[] { 1, 2, 6, 5 }, new[] { 2, 3, 7, 6 }, new[] { 3, 0, 4, 7 } },
            };
        }
        SourceObject Obj(string id, string layer) => new()
        {
            Id = id, LayerPath = layer, GeometryType = "Brep", BBox = B(0, 12), IsClosed = true, FaceCount = 6, Mesh = Box(0, 12),
        };
        var model = new SourceModel
        {
            Units = "Feet",
            Layers = new[] { new LayerInfo(0, "CORE", true, "#000000"), new LayerInfo(1, "STAIR", true, "#000000") },
            Objects = new[] { Obj("c", "CORE"), Obj("s", "STAIR") },
        };
        var r = Analyzer.Run(new AnalysisInput { Model = model });
        Assert.Equal("CORE", r.Masses.Single(m => m.Id == "c").ProgramKey);
        Assert.Equal("STAIR", r.Masses.Single(m => m.Id == "s").ProgramKey);
    }
}
