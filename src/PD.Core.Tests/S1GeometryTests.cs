using PD.Core.Analysis;
using PD.Core.Classification;
using PD.Core.Cleanup;
using PD.Core.Geometry;
using PD.Core.Model;
using Xunit;
using Xunit.Abstractions;

namespace PD.Core.Tests;

public class S1GeometryTests
{
    readonly ITestOutputHelper _out;
    public S1GeometryTests(ITestOutputHelper output) => _out = output;
    static AnalysisResult R => AnalysisFixture.Result;

    [Fact]
    public void T1_LayerRolesPrefilled()
    {
        var expected = new Dictionary<string, string>
        {
            ["Default"] = Roles.Ignore, ["CONTEXT BUILDINGS"] = Roles.Building, ["SITE BLOCKS"] = "BG.Hardscape",
            ["BACKGROUND FRAME"] = Roles.Base, ["BACKGROUND FRAME::GLASS"] = Roles.Facade, ["GLASS"] = Roles.Facade,
            ["slab"] = Roles.Slab, ["LOBBY"] = Roles.Program, ["LAB"] = Roles.Program, ["LOUNGE"] = Roles.Program,
            ["UTILITY"] = Roles.Program, ["OFFICE"] = Roles.Program,
        };
        var correct = expected.Count(kv => R.Roles[kv.Key].Role == kv.Value);
        foreach (var kv in expected) _out.WriteLine($"{kv.Key}: {R.Roles[kv.Key].Role} {R.Roles[kv.Key].ProgramKey}");
        Assert.True(correct >= 10, $"{correct}/12");
        Assert.Equal("LAB", R.Roles["LAB"].ProgramKey);
    }

    [Fact]
    public void Localize_FeetAndStrayMesh()
    {
        Assert.Equal("Feet", R.Local.Units);
        Assert.InRange(R.Building.MinX, -0.01, 0.01);
        Assert.InRange(R.Building.MaxX, 50, 1000);
        var stray = R.Stray.Select(id => R.Local.Objects.First(o => o.Id == id)).ToList();
        foreach (var s in stray) _out.WriteLine($"stray {s.LayerPath} {s.GeometryType}");
        Assert.Contains(stray, o => o.LayerPath == "slab" && o.GeometryType == "Mesh");
        Assert.DoesNotContain(stray, o => o.Role == Roles.Program);
    }

    [Fact]
    public void T2_OpenMassesCappedOrFlagged()
    {
        foreach (var m in R.Masses)
            _out.WriteLine($"{m.Source.LayerPath} {m.Source.GeometryType} closed={m.Source.IsClosed} faces={m.Source.FaceCount} -> {m.Status} {m.Volume:0}");
        var open = R.Masses.Where(m => !m.Source.IsClosed).ToList();
        Assert.Equal(7, open.Count);
        Assert.Equal(5, open.Count(m => m.Status is MassStatus.Capped or MassStatus.SectionClosed));
        Assert.Equal(2, open.Count(m => m.Status == MassStatus.SingleSurface));
        Assert.All(R.Masses.Where(m => m.Source.IsClosed), m => Assert.Equal(MassStatus.Closed, m.Status));
        Assert.Equal(2, R.Issues.Count(i => i.Kind == IssueKind.SingleSurface));
    }

    [Fact]
    public void MeshVolume_BoxMatchesBbox()
    {
        var box = R.Masses.First(m => m.Status == MassStatus.Closed && m.Source.GeometryType == "Extrusion");
        var b = box.Source.BBox;
        var bboxVol = (b.MaxX - b.MinX) * (b.MaxY - b.MinY) * b.SizeZ;
        Assert.InRange(box.Volume, 0.2 * bboxVol, 1.001 * bboxVol);
    }

    static readonly (string name, double lo, double hi)[] ExpectedMain =
    {
        ("L1", -0.5, 0.5), ("L2", 14.08 - 0.5, 15.67 + 0.5), ("L3", 29.08 - 0.5, 30.5 + 0.5),
        ("L4", 41.67 - 0.5, 41.67 + 0.5), ("L5", 54.17 - 0.5, 54.17 + 0.5), ("L6", 66.67 - 0.5, 66.67 + 0.5),
        ("L7", 79.17 - 0.5, 79.17 + 0.5), ("L8", 91.67 - 0.5, 91.67 + 0.5), ("L9", 104.25 - 0.5, 104.25 + 0.5),
        ("L10", 116.75 - 0.5, 116.75 + 0.5), ("L11", 130.25 - 0.5, 130.25 + 0.5),
    };

    [Fact]
    public void T3_LevelsMatchPrd()
    {
        foreach (var l in R.Levels) _out.WriteLine($"{l.Name} {FeetInches.Format(l.Elevation)} {l.Sources} spread={l.Spread:0.00} mezz={l.IsMezzanine} roof={l.IsRoof}");
        foreach (var i in R.Issues.Where(i => i.Elevation != null)) _out.WriteLine($"  {i.Kind}: {i.Message}");
        var main = R.Levels.Where(l => !l.IsMezzanine).ToList();
        Assert.Equal(ExpectedMain.Length, main.Count);
        for (var i = 0; i < main.Count; i++)
        {
            Assert.Equal(ExpectedMain[i].name, main[i].Name);
            Assert.InRange(main[i].Elevation, ExpectedMain[i].lo, ExpectedMain[i].hi);
        }
        Assert.True(main[^1].IsRoof);
        Assert.Contains(R.Issues, i => i.Kind == IssueKind.LevelScattered && i.Message.StartsWith("L2"));
    }

    [Theory]
    [InlineData(13 + 4 / 12.0)]
    [InlineData(16 + 8 / 12.0)]
    [InlineData(23 + 3 / 12.0)]
    public void T3_OutlierElevationsFlagged(double elevation)
    {
        Assert.Contains(R.Issues, i => i.Kind is IssueKind.LevelUnsupported or IssueKind.Mezzanine
            && i.Elevation is { } e && Math.Abs(e - elevation) < 0.5);
    }

    [Fact]
    public void T4_AreasAddUpAfterResolution()
    {
        foreach (var o in R.Overlaps) _out.WriteLine($"overlap {R.Levels[o.LevelIndex].Name} {o.Area:0}");
        foreach (var s in R.Spans.Where(s => s.Mode != SpanMode.Single)) _out.WriteLine($"span {s.MassId[..8]} {s.Mode} levels={s.LevelIndices.Length} h={s.Height:0.0}");
        Assert.NotEmpty(R.Overlaps);
        foreach (var p in R.Plans)
        {
            var sum = p.AreaByProgram.Values.Sum();
            _out.WriteLine($"{p.Level.Name}: union={p.Union.Area:0} sum={sum:0} " + string.Join(" ", p.AreaByProgram.Select(kv => $"{kv.Key}={kv.Value:0}")));
            // The upper part of a kept tall room is in the level outline but counts for no program (v0.9.2).
            var voids = p.Footprints.Where(f => f.OpenToBelow).Select(f => f.Raw).Aggregate((NetTopologySuite.Geometries.Geometry?)null, (a, g) => a == null ? g : a.Union(g));
            Assert.Equal(p.Union.Area - (voids == null ? 0 : p.Union.Intersection(voids).Area), sum, 0.5);
        }
        Assert.True(R.Plans.Sum(p => p.Union.Area) > 1000);
    }

    [Fact]
    public void T4_SmallerVolumeWins()
    {
        // PRD 7.1: per-level OFFICE masses (54'-6", 67'-0") sit inside the full-height LAB mass.
        var l5 = R.Plans.First(p => p.Level.Name == "L5");
        Assert.Contains(R.Overlaps, o => o.LevelIndex == R.Plans.IndexOf(l5)
            && R.Masses.First(m => m.Id == o.WinnerId).ProgramKey == "OFFICE"
            && R.Masses.First(m => m.Id == o.LoserId).ProgramKey == "LAB");
    }

    [Fact]
    public void Slicer_BoxGivesRectangle()
    {
        var mesh = new MeshData
        {
            Vertices = new double[] { 0,0,0, 10,0,0, 10,5,0, 0,5,0, 0,0,3, 10,0,3, 10,5,3, 0,5,3 },
            Faces = new[] { new[] { 0,1,5,4 }, new[] { 1,2,6,5 }, new[] { 2,3,7,6 }, new[] { 3,0,4,7 } }, // open tube
        };
        Assert.Equal(50, MeshSlicer.SliceToPolygon(mesh, 1.5, 0.01, 0.5).Area, 3);
        var rep = MeshAnalysis.Analyze(mesh, 0.01, 0.05);
        Assert.Equal(2, rep.NakedLoops);
        Assert.True(rep.LoopsPlanar);
        Assert.Equal(150, rep.Volume, 3);
    }

    [Theory]
    [InlineData("14'-1\"", 14 + 1 / 12.0)]
    [InlineData("-0'-10\"", -10 / 12.0)]
    [InlineData("129.5", 129.5)]
    [InlineData("6\"", 0.5)]
    public void FeetInches_RoundTrip(string text, double feet)
    {
        Assert.True(FeetInches.TryParse(text, out var v));
        Assert.Equal(feet, v, 6);
        if (text.Contains('\'')) Assert.Equal(text, FeetInches.Format(feet));
    }

    [Fact]
    public void SpanOverride_SingleKeepsBaseLevelOnly()
    {
        var tall = R.Spans.First(s => s.LevelIndices.Length == 2);
        var r = Analyzer.Run(new AnalysisInput
        {
            Model = PD.Core.IO.FixtureReader.Load(FixtureSmokeTests.FixturePath("template.json")),
            SpanOverrides = new Dictionary<string, SpanMode> { [tall.MassId] = SpanMode.Single },
        });
        var span = r.Spans.Single(s => s.MassId == tall.MassId);
        Assert.Equal(SpanMode.Single, span.Mode);
        Assert.Single(span.LevelIndices);
        Assert.Equal(1, r.Plans.Sum(p => p.Footprints.Count(f => f.Mass.Id == tall.MassId)));
    }
}
