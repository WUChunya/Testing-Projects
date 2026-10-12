using PD.Core.Analysis;
using PD.Core.Classification;
using PD.Core.IO;
using PD.Core.Levels;
using PD.Core.Model;
using Xunit;

namespace PD.Core.Tests;

/// <summary>
/// Regressions from the user's Windows test on 261012_Downtown_Massing.3dm: floor plates were not read as slabs
/// (no levels), the sky garden was dropped by the level-overlap rule, unknown PROGRAM:: sub-layers fell to Ignore,
/// and Make2D / clipping-plane copies were classified as the layers they came from.
/// </summary>
public static class DowntownAnalysis
{
    static readonly Lazy<SourceModel> Lazy = new(() => FixtureReader.Load(FixtureSmokeTests.FixturePath("downtown.json")));
    static readonly Lazy<AnalysisResult> LazyResult = new(() => Analyzer.Run(new AnalysisInput { Model = Model }));

    public static SourceModel Model => Lazy.Value;
    public static AnalysisResult Result => LazyResult.Value;

    public static PD.Core.Drawing.ExportContext Context()
    {
        var settings = new PD.Core.Settings.PdSettings();
        settings.Output.FillDefaults(PD.Core.Config.Rules.Default().Output);
        return new PD.Core.Drawing.ExportContext
        {
            Result = Result, Settings = settings,
            Programs = ProgramCatalog.Effective(LayerClassifier.LoadPrograms(), null),
        };
    }
}

public class DowntownTests
{
    static readonly Lazy<SourceModel> Model = new(() => DowntownAnalysis.Model);
    static readonly Lazy<AnalysisResult> Analysis = new(() => DowntownAnalysis.Result);

    static string Role(string layer) => LayerClassifier.Default().Guess(new LayerInfo(0, layer, true, "#000000")).Role;

    [Theory]
    [InlineData("FLOOR PLATE", Roles.Slab)]                       // levels come from here; "SLAB" alone missed it
    [InlineData("COLUMNS", Roles.Structure)]
    [InlineData("PROGRAM::Core", Roles.Core)]
    [InlineData("PROGRAM::Glass", Roles.Facade)]
    [InlineData("PROGRAM::Lab", Roles.Program)]
    [InlineData("PROGRAM::Seminar Room", Roles.Program)]          // unknown name under PROGRAM:: stays a program
    [InlineData("PROGRAM::Support", Roles.Program)]
    [InlineData("PROGRAM::Green Roof", Roles.Program)]            // sky garden is program, not site landscape
    [InlineData("GREEN ROOF", Roles.Green)]                       // the same name outside PROGRAM:: is still site
    [InlineData("CADMAPPER::CADMAPPER BUILDINGS", Roles.Building)]
    [InlineData("CADMAPPER::CADMAPPER WATER", Roles.Water)]
    [InlineData("CADMAPPER::CADMAPPER  OUTLINE", Roles.Ignore)]
    [InlineData("CADMAPPER::CADMAPPER PARK", Roles.Landscape)]
    [InlineData("CADMAPPER::ROAD LINES", Roles.Road)]
    [InlineData("Pedestrian Walkway 2", Roles.Hardscape)]
    [InlineData("LOT FILL", Roles.Base)]
    [InlineData("PEOPLE / CARS", Roles.Entourage)]
    [InlineData("GRID (12x12')", Roles.Ignore)]            // drafting grid, not a property line
    [InlineData("PROPERTY LINE", Roles.Line)]
    [InlineData("CLIPPING PLANE", Roles.Ignore)]
    [InlineData("NE ISO::Visible::Curves::PROGRAM::Lab", Roles.Ignore)]            // Make2D copy
    [InlineData("1.80 [0]::Visible::ClippingPlaneIntersections::PROGRAM::Core", Roles.Ignore)]
    [InlineData("NE ISO::ViewportRectangle", Roles.Ignore)]
    public void LayerRoles(string layer, string role) => Assert.Equal(role, Role(layer));

    [Fact]
    public void ProgramLayersGetTheirProgram()
    {
        var c = LayerClassifier.Default();
        string Key(string path) => c.Guess(new LayerInfo(0, path, true, "#000000")).ProgramKey;
        Assert.Equal("LAB", Key("PROGRAM::Lab"));
        Assert.Equal("LAB_SUPPORT", Key("PROGRAM::Lab Support"));
        Assert.Equal("OFFICE", Key("PROGRAM::Enclosed Office"));
        Assert.Equal("LOBBY", Key("PROGRAM::Break Room"));
        Assert.Equal("ASSEMBLY", Key("PROGRAM::Seminar Room"));
        Assert.Equal("BUILDING_SUPPORT", Key("PROGRAM::Support"));
        Assert.Equal("SHELL", Key("PROGRAM::Shell Space"));
        Assert.Equal("GREEN_ROOF", Key("PROGRAM::Green Roof"));
        Assert.Equal("CONFERENCE", Key("PROGRAM::Conference"));
        Assert.Equal("CIRCULATION", Key("PROGRAM::Circulation"));
    }

    /// <summary>The 16 floor plates give one basement and 12 floors (slab tops -22 ft to 194 ft).</summary>
    [Fact]
    public void LevelsComeFromTheFloorPlates()
    {
        var levels = Analysis.Value.Levels;
        Assert.Equal(13, levels.Count);
        Assert.Equal("B1", levels[0].Name);
        Assert.Equal(-21.99, levels[0].Elevation, 1);
        Assert.Equal("L1", levels[1].Name);
        Assert.Equal(0.18, levels[1].Elevation, 1);
        Assert.Equal("L12", levels[^1].Name);
        Assert.Equal(194.01, levels[^1].Elevation, 1);
        // Masses are drawn from the slab soffit here (bottom 3" below the slab top): no phantom level, no mezzanine.
        Assert.All(levels, l => Assert.False(l.IsMezzanine));
        Assert.All(levels, l => Assert.True(l.Sources.HasFlag(LevelSource.Slab), $"{l.Name} has no slab"));
    }

    /// <summary>
    /// The roof garden sits 4,000 ft from the tower (drawn aside), so it is reported as a stray program mass;
    /// once kept it lands on the top level although it is only 6" thick (flat-mass rule).
    /// </summary>
    [Fact]
    public void SkyGardenIsReportedStrayAndCountedOnceKept()
    {
        var model = Model.Value;
        var sky = model.Objects.First(o => o.LayerPath.EndsWith("Green Roof", StringComparison.Ordinal) && o.BBox.MinZ > 200 * 304.8);
        Assert.Contains(Analysis.Value.Issues, i => i.Kind == IssueKind.StrayObject && i.ObjectIds.Contains(sky.Id));

        var kept = Analyzer.Run(new AnalysisInput { Model = model, KeepStray = new HashSet<string> { sky.Id } });
        var top = kept.Plans[^1];
        Assert.Equal("L12", top.Level.Name);
        Assert.Equal(28800, top.AreaByProgram["GREEN_ROOF"], 0);
    }

    /// <summary>Context spreads over the whole downtown site: only our own objects can be stray (PRD 3.5).</summary>
    [Fact]
    public void ContextIsNeverStray()
    {
        var r = Analysis.Value;
        Assert.DoesNotContain(r.Local.Objects.Where(o => r.Stray.Contains(o.Id)), o => o.Role == Roles.Building);
        Assert.True(r.Issues.Count(i => i.Kind == IssueKind.StrayObject) < 20);
    }

    /// <summary>Every program layer of the model reaches the drawing: none is left as Unassigned.</summary>
    [Fact]
    public void AllProgramMassesAreNamed()
    {
        var r = Analysis.Value;
        var keys = r.Masses.Select(m => m.ProgramKey).Distinct().ToList();
        Assert.DoesNotContain(ProgramCatalog.Unassigned, keys);
        Assert.Contains("CORE", keys);
        Assert.Contains("ASSEMBLY", keys);
        Assert.Contains("BUILDING_SUPPORT", keys);
        Assert.Contains("GREEN_ROOF", keys);
        Assert.True(r.Plans.Sum(p => p.AreaByProgram.Values.Sum()) > 250000,
            $"GSF {r.Plans.Sum(p => p.AreaByProgram.Values.Sum()):0}");
    }

    /// <summary>Make2D copies and entourage never become masses or slabs.</summary>
    [Fact]
    public void TwoDimensionalCopiesAreIgnored()
    {
        var r = Analysis.Value;
        Assert.DoesNotContain(r.Masses, m => m.Source.LayerPath.Contains("::Visible::", StringComparison.Ordinal));
        Assert.DoesNotContain(r.Local.Objects.Where(o => o.Role == Roles.Slab), o => o.LayerPath.Contains("Visible", StringComparison.Ordinal));
        Assert.Equal(16, r.Local.Objects.Count(o => o.Role == Roles.Slab));
        Assert.Equal(155, r.Local.Objects.Count(o => o.Role == Roles.Entourage && o.Mesh != null));
    }

    /// <summary>
    /// Full export of the user's model: the drawings must build and every item stay on its artboard.
    /// Writes the Downtown goldens that are checked in for review.
    /// </summary>
    [Fact]
    public void ExportBuildsEveryDrawing()
    {
        var ctx = DowntownAnalysis.Context();
        var b = ctx.Result.Building;
        var (x0, y0, _) = ctx.Result.Frame.ToModel(b.MinX - 40, (b.MinY + b.MaxY) / 2, 0);
        var (x1, y1, _) = ctx.Result.Frame.ToModel(b.MaxX + 40, (b.MinY + b.MaxY) / 2, 0);
        ctx.Settings.Sections.Add(new PD.Core.Settings.SectionSetting { Name = "DT", Line = new[] { x0, y0, x1, y1 }, LookLeft = true, DepthFt = 400 });
        ctx.Settings.Output.Drawings = new List<string> { "Plan", "Section", "AxonStacked", "ProgramChart", "Pie" };

        var notes = new List<string>();
        var drawings = PD.Core.Writers.Exporter.BuildDrawings(ctx, notes);
        Assert.Equal(5, drawings.Count);
        foreach (var d in drawings)
        {
            Assert.NotEmpty(d.Artboards);
            foreach (var it in d.Layers.SelectMany(l => l.Items).Where(i => i.Paths != null))
            {
                var ab = d.Artboards[it.Artboard];
                Assert.All(it.Paths!.SelectMany(q => q), q => { Assert.InRange(q[0], -1, ab.W + 1); Assert.InRange(q[1], -1, ab.H + 1); });
            }
        }
        var dir = Path.Combine(Path.GetDirectoryName(FixtureSmokeTests.FixturePath("x"))!, "..", "golden");
        foreach (var (d, a, name) in new[]
                 {
                     (drawings[0], 1, "downtown_plan_L1"), (drawings[1], 0, "downtown_section_DT"),
                     (drawings[2], 0, "downtown_axon_stacked"), (drawings[3], 0, "downtown_program_chart"),
                     (drawings[4], 0, "downtown_program_pie"),
                 })
            File.WriteAllText(Path.Combine(dir, name + ".svg"), PD.Core.Writers.SvgWriter.Write(d, Math.Min(a, d.Artboards.Count - 1)));
    }
}
