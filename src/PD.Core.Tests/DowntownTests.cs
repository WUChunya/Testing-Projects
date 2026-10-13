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
    [InlineData("LOT FILL", Roles.Hardscape)]                    // city blocks: 街区 ground
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
        // Rev 2026-10-13: no roof slab over the penthouse any more (its top gets an elevation marker instead).
        Assert.Equal(12, levels.Count);
        Assert.Equal("B1", levels[0].Name);
        Assert.Equal(-21.99, levels[0].Elevation, 1);
        Assert.Equal("L1", levels[1].Name);
        Assert.Equal(0.18, levels[1].Elevation, 1);
        Assert.Equal("L11", levels[^1].Name);
        Assert.Equal(178.01, levels[^1].Elevation, 1);
        // Masses are drawn from the slab soffit here (bottom 3" below the slab top): no phantom level, no mezzanine.
        Assert.All(levels, l => Assert.False(l.IsMezzanine));
        Assert.All(levels, l => Assert.True(l.Sources.HasFlag(LevelSource.Slab), $"{l.Name} has no slab"));
    }

    /// <summary>
    /// The roof garden sits 4,000 ft from the tower (drawn aside), so it is reported as a stray program mass;
    /// once kept it is 34 ft above the top level L11 (rev 2026-10-13 has no roof slab), more than 1.6 stories, so the
    /// flat-mass rule does not put it on a level.
    /// </summary>
    [Fact]
    public void SkyGardenIsReportedStrayAndCountedOnceKept()
    {
        var model = Model.Value;
        var sky = model.Objects.First(o => o.LayerPath.EndsWith("Green Roof", StringComparison.Ordinal) && o.BBox.MinZ > 200 * 304.8);
        Assert.Contains(Analysis.Value.Issues, i => i.Kind == IssueKind.StrayObject && i.ObjectIds.Contains(sky.Id));

        var kept = Analyzer.Run(new AnalysisInput { Model = model, KeepStray = new HashSet<string> { sky.Id } });
        var top = kept.Plans[^1];
        Assert.Equal("L11", top.Level.Name);
        Assert.False(top.AreaByProgram.ContainsKey("GREEN_ROOF"));
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
        // The plaza slab (PROGRAM::Circulation) and the lawns (PROGRAM::Green Roof) lie at grade: site, not program.
        Assert.Contains(r.Issues, i => i.Kind == IssueKind.SitePaving && i.Message.Contains("PROGRAM::Circulation"));
        Assert.Contains(r.Issues, i => i.Kind == IssueKind.SitePaving && i.Message.Contains("planting"));
        Assert.DoesNotContain(r.Masses, m => m.Source.BBox.SizeZ <= 2 && Math.Abs(m.Source.BBox.MaxZ) <= 2);
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
        Assert.Equal(15, r.Local.Objects.Count(o => o.Role == Roles.Slab));
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
        ctx.Settings.Output.Drawings = new List<string> { "SitePlan", "Plan", "Section", "SiteAxon", "AxonStacked", "ProgramChart", "Pie", "AxonExploded" };

        var notes = new List<string>();
        var drawings = PD.Core.Writers.Exporter.BuildDrawings(ctx, notes);
        Assert.Equal(8, drawings.Count);
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
        PD.Core.Drawing.Drawing By(string f) => drawings.Single(x => x.FileName == f);
        var plans = By("01_Plans.ai");
        int Ab(string n) => plans.Artboards.FindIndex(x => x.Name == n);
        foreach (var (d, a, name) in new[]
                 {
                     (By("00_Site_Plan.ai"), 0, "downtown_site_plan"), (plans, Ab("PLAN L1"), "downtown_plan_L1"),
                     (plans, Ab("PLAN L5"), "downtown_plan_L5"), (plans, Ab("PLAN B1"), "downtown_plan_B1"),
                     (By("02_Section_DT.ai"), 0, "downtown_section_DT"), (By("03_Axon_Stacked.ai"), 0, "downtown_axon_stacked"),
                     (By("04_Axon_Exploded.ai"), 0, "downtown_axon_exploded"), (By("03_Site_Axon.ai"), 0, "downtown_site_axon"),
                     (By("05_Program_Chart.ai"), 0, "downtown_program_chart"), (By("06_Program_Pie.ai"), 0, "downtown_program_pie"),
                 })
            File.WriteAllText(Path.Combine(dir, name + ".svg"), PD.Core.Writers.SvgWriter.Write(d, Math.Min(a, d.Artboards.Count - 1)));
    }

    /// <summary>Review helper: with PD_DUMP=&lt;dir&gt; writes every Downtown drawing / artboard as SVG (no-op otherwise).</summary>
    [Fact]
    public void DumpForReview()
    {
        var dir = Environment.GetEnvironmentVariable("PD_DUMP");
        if (string.IsNullOrEmpty(dir)) return;
        var ctx = DowntownAnalysis.Context();
        var b = ctx.Result.Building;
        var (x0, y0, _) = ctx.Result.Frame.ToModel(b.MinX - 40, (b.MinY + b.MaxY) / 2, 0);
        var (x1, y1, _) = ctx.Result.Frame.ToModel(b.MaxX + 40, (b.MinY + b.MaxY) / 2, 0);
        var (ex0, ey0, _) = ctx.Result.Frame.ToModel(b.MinX - 40, b.MinY - 60, 0);
        var (ex1, ey1, _) = ctx.Result.Frame.ToModel(b.MaxX + 40, b.MinY - 60, 0);
        ctx.Settings.Sections.Add(new PD.Core.Settings.SectionSetting { Name = "DT", Line = new[] { x0, y0, x1, y1 }, LookLeft = true, DepthFt = 400 });
        ctx.Settings.Sections.Add(new PD.Core.Settings.SectionSetting { Name = "S", Kind = PD.Core.Settings.SectionSetting.ElevationKind, Line = new[] { ex0, ey0, ex1, ey1 }, LookLeft = true, DepthFt = 400 });
        var fr = ctx.Result.Frame;
        File.WriteAllLines(Path.Combine(dir, "blocks.txt"), PD.Core.Analysis.ProgramBlocks.Build(ctx.Result)
            .Select(b => $"{ctx.Result.Levels[b.LevelIndex].Name} {b.ProgramKey} z {b.Z0:0.0}-{b.Z1:0.0} env {b.Geometry.EnvelopeInternal.MinX:0.0},{b.Geometry.EnvelopeInternal.MinY:0.0} {b.Geometry.EnvelopeInternal.MaxX:0.0},{b.Geometry.EnvelopeInternal.MaxY:0.0} src {b.Footprint.Mass.Source.BBox.MinZ / 304.8:0.0}-{b.Footprint.Mass.Source.BBox.MaxZ / 304.8:0.0}"));
        File.WriteAllLines(Path.Combine(dir, "roles.txt"), ctx.Result.Roles.Values.Where(g => !g.LayerPath.Contains("::Visible") && !g.LayerPath.StartsWith("NE ISO"))
            .Select(g => $"{g.LayerPath} -> {g.Role} {g.ProgramKey}"));
        var only = Environment.GetEnvironmentVariable("PD_ONLY");
        if (!string.IsNullOrEmpty(only)) ctx.Settings.Output.Drawings = only.Split(',').ToList();
        var dumpNotes = new List<string>();
        foreach (var d in PD.Core.Writers.Exporter.BuildDrawings(ctx, dumpNotes))
            for (var a = 0; a < d.Artboards.Count; a++)
                File.WriteAllText(Path.Combine(dir, PD.Core.Writers.Exporter.PreviewName(d, a)), PD.Core.Writers.SvgWriter.Write(d, a));
        File.WriteAllLines(Path.Combine(dir, "notes.txt"), dumpNotes);
    }

    /// <summary>
    /// The whole export of the user's model, every drawing on: an elevation used to die with
    /// "Operation does not support GeometryCollection arguments" because a clipped projection came back as a
    /// GeometryCollection and the next overlay refused it.
    /// </summary>
    [Fact]
    public void FullExportRunsWithSectionAndElevation()
    {
        var ctx = DowntownAnalysis.Context();
        var b = ctx.Result.Building;
        var (x0, y0, _) = ctx.Result.Frame.ToModel(b.MinX - 40, (b.MinY + b.MaxY) / 2, 0);
        var (x1, y1, _) = ctx.Result.Frame.ToModel(b.MaxX + 40, (b.MinY + b.MaxY) / 2, 0);
        ctx.Settings.Sections.Add(new PD.Core.Settings.SectionSetting { Name = "DT", Line = new[] { x0, y0, x1, y1 }, LookLeft = true, DepthFt = 400 });
        ctx.Settings.Sections.Add(new PD.Core.Settings.SectionSetting { Name = "N", Kind = PD.Core.Settings.SectionSetting.ElevationKind, Line = new[] { x0, y0, x1, y1 }, LookLeft = false, DepthFt = 400 });

        var notes = new List<string>();
        var drawings = PD.Core.Writers.Exporter.BuildDrawings(ctx, notes);
        Assert.DoesNotContain(notes, n => n.Contains("not written", StringComparison.Ordinal));
        // Site plan, plans, section, elevation, 4 axons, chart, pie (+ site axon when built).
        foreach (var f in new[] { "00_Site_Plan.ai", "01_Plans.ai", "02_Section_DT.ai", "07_Elevation_N.ai", "03_Site_Axon.ai", "03_Axon_Stacked.ai",
                     "04_Axon_Exploded.ai", "04_Axon_Exploded_B.ai", "04_Axon_Levels.ai", "05_Program_Chart.ai", "06_Program_Pie.ai" })
            Assert.Contains(drawings, d => d.FileName == f);
        Assert.All(drawings, d => Assert.NotEmpty(d.Artboards));
    }
}
