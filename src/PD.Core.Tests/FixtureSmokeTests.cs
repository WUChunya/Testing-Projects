using System.Text.Json;
using PD.Core.IO;
using PD.Core.Model;
using PD.Core.Styles;
using Xunit;

namespace PD.Core.Tests;

public class FixtureSmokeTests
{
    static readonly string[] ProgramLayers = { "LOBBY", "LAB", "LOUNGE", "UTILITY", "OFFICE" };

    internal static string FixturePath(string name)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "PD.sln"))) dir = dir.Parent;
        return Path.Combine(dir?.FullName ?? throw new DirectoryNotFoundException("repo root"), "tests", "fixtures", name);
    }

    static readonly Lazy<SourceModel> Template = new(() => FixtureReader.Load(FixturePath("template.json")));

    [Fact]
    public void TemplateFixture_MatchesPrdInventory()
    {
        var m = Template.Value;
        Assert.Equal("Millimeters", m.Units);
        Assert.Equal(12, m.Layers.Count);
        Assert.Equal(382, m.Objects.Count);
        var program = m.Objects.Where(o => ProgramLayers.Contains(o.LayerPath)).ToList();
        Assert.Equal(21, program.Count);
        Assert.Equal(14, program.Count(o => o.IsClosed));
        Assert.All(program, o => Assert.NotNull(o.Mesh));
        Assert.Equal(231, m.Objects.Count(o => o.LayerPath == "slab"));
        Assert.Equal("GLASS", m.Layers.Single(l => l.Path == "BACKGROUND FRAME::GLASS").Name);
    }

    [Fact]
    public void TemplateFixture_MeshesAreWellFormed()
    {
        foreach (var o in Template.Value.Objects.Where(o => o.Mesh != null))
        {
            var mesh = o.Mesh!;
            Assert.True(mesh.Faces.Length > 0, o.Id);
            Assert.All(mesh.Faces, f => Assert.All(f, i => Assert.InRange(i, 0, mesh.VertexCount - 1)));
        }
    }

    [Fact]
    public void TemplateFixture_LabSpansWholeBuilding()
    {
        var ft = Units.ToFeet(Template.Value.Units);
        var tallest = Template.Value.Objects.Where(o => o.LayerPath == "LAB").Max(o => o.BBox.SizeZ) * ft;
        Assert.InRange(tallest, 125, 135); // PRD 7.1: one LAB mass 0'-6" → 129'-10"
    }

    [Theory]
    [InlineData(StyleResources.ProgramLegend)]
    [InlineData(StyleResources.StyleDefault)]
    [InlineData(StyleResources.LayerKeywords)]
    public void EmbeddedStyles_Parse(string file)
    {
        using var doc = StyleResources.Read(file);
        Assert.True(doc.RootElement.GetProperty("version").GetInt32() >= 1);
    }

    [Fact]
    public void ProgramLegend_HasPrdColors()
    {
        using var doc = StyleResources.Read(StyleResources.ProgramLegend);
        var fills = doc.RootElement.GetProperty("programs").EnumerateArray()
            .ToDictionary(p => p.GetProperty("key").GetString()!, p => p.GetProperty("fill"));
        Assert.Equal("#7CBECD", fills["OFFICE"].GetString());
        Assert.Equal("#F7A238", fills["LAB"].GetString());
        Assert.Equal(JsonValueKind.Null, fills["VOID"].ValueKind);
    }
}
