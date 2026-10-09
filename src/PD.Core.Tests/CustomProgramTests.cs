using PD.Core.Analysis;
using PD.Core.Classification;
using PD.Core.IO;
using PD.Core.Model;
using PD.Core.Settings;
using Xunit;

namespace PD.Core.Tests;

public class CustomProgramTests
{
    static readonly List<ProgramDef> Legend = LayerClassifier.LoadPrograms();

    static SourceModel Fixture() => FixtureReader.Load(FixtureSmokeTests.FixturePath("template.json"));

    [Theory]
    [InlineData("Clean Room", "CLEAN_ROOM")]
    [InlineData("  maker-space / fab ", "MAKER_SPACE_FAB")]
    [InlineData("3D Lab", "P_3D_LAB")]
    [InlineData("实验室", "CUSTOM")]
    public void MakeKey_Sanitises(string label, string key) => Assert.Equal(key, ProgramCatalog.MakeKey(label, Array.Empty<string>()));

    [Fact]
    public void MakeKey_AvoidsCollision() => Assert.Equal("LAB_2", ProgramCatalog.MakeKey("Lab", new[] { "LAB" }));

    [Fact]
    public void Legend_KeepsSpecialStyles()
    {
        var all = ProgramCatalog.Effective(Legend, null);
        var v = all.Single(p => p.Key == "VOID");
        Assert.Null(v.Fill);
        Assert.Equal(new double[] { 3, 2 }, v.Dash);
        Assert.Equal("diagonal", all.Single(p => p.Key == ProgramCatalog.Unassigned).Hatch);
        Assert.Equal(0.75, all.Single(p => p.Key == "SHELL").StrokePt);
    }

    [Fact]
    public void Effective_RestoresStylesMissingFromOldSaves()
    {
        // Saves from v0.1.0 had no stroke/dash/hatch/aliases.
        var old = Legend.Select((d, i) => new ProgramSetting { Key = d.Key, Label = d.Label, Fill = d.Fill, Order = i }).ToList();
        var eff = ProgramCatalog.Effective(Legend, PdSettings.FromJson(new PdSettings { Programs = old }.ToJson()).Programs);
        Assert.Equal(new double[] { 3, 2 }, eff.Single(p => p.Key == "VOID").Dash);
        Assert.Contains("UTILITY", eff.Single(p => p.Key == "BUILDING_SUPPORT").Aliases);
    }

    [Fact]
    public void AddCustom_AppendsWithPaletteColour()
    {
        var list = ProgramCatalog.Effective(Legend, null);
        var p = ProgramCatalog.AddCustom(list, "Clean Room");
        Assert.True(p.IsCustom);
        Assert.Equal("CLEAN_ROOM", p.Key);
        Assert.Equal(ProgramCatalog.CustomPalette()[0], p.Fill);
        Assert.Equal(list.Max(x => x.Order), p.Order);
        Assert.Equal("CLEAN_ROOM_2", ProgramCatalog.AddCustom(list, "clean room").Key);
    }

    [Fact]
    public void CustomAlias_AutoFillsLayer()
    {
        var list = ProgramCatalog.Effective(Legend, null);
        ProgramCatalog.AddCustom(list, "Café", aliases: new[] { "LOUNGE" });
        // The legend's INTERACTION also lists LOUNGE; the custom alias wins.
        var c = ProgramCatalog.Classifier(list);
        Assert.Equal("CAF", c.Guess(new LayerInfo(9, "LOUNGE", true, "#000000")).ProgramKey);
    }

    [Fact]
    public void RemoveCustom_UnlinksLayersAndObjects()
    {
        var s = new PdSettings { Programs = ProgramCatalog.Effective(Legend, null) };
        var p = ProgramCatalog.AddCustom(s.Programs, "Clean Room");
        s.Layers["LAB"] = new LayerSetting { Role = Roles.Program, ProgramKey = p.Key };
        s.ObjectPrograms["x"] = p.Key;
        Assert.False(ProgramCatalog.RemoveCustom(s, "LAB"));
        Assert.True(ProgramCatalog.RemoveCustom(s, p.Key));
        Assert.Equal("", s.Layers["LAB"].ProgramKey);
        Assert.Empty(s.ObjectPrograms);
    }

    [Fact]
    public void Analyzer_LayerAndObjectOverridesFlowToAreasAndIds()
    {
        var programs = ProgramCatalog.Effective(Legend, null);
        var clean = ProgramCatalog.AddCustom(programs, "Clean Room");
        var model = Fixture();
        var oneOffice = model.Objects.First(o => o.LayerPath == "OFFICE" && o.IsClosed).Id;
        var r = Analyzer.Run(new AnalysisInput
        {
            Model = model,
            Classifier = ProgramCatalog.Classifier(programs),
            RoleOverrides = new Dictionary<string, LayerGuess> { ["UTILITY"] = new("UTILITY", Roles.Program, clean.Key) },
            ProgramOverrides = new Dictionary<string, string> { [oneOffice] = clean.Key },
        });
        Assert.All(r.Masses.Where(m => m.Source.LayerPath == "UTILITY"), m => Assert.Equal(clean.Key, m.ProgramKey));
        Assert.Equal(clean.Key, r.Masses.Single(m => m.Id == oneOffice).ProgramKey);
        Assert.Contains(r.Plans, p => p.AreaByProgram.GetValueOrDefault(clean.Key) > 0);
        Assert.Contains(Naming.Build(r, new Dictionary<string, string>(), k => k), n => n.StableId.Contains("_CLEAN_ROOM_"));
        Assert.DoesNotContain(r.Issues, i => i.Kind == IssueKind.ProgramNoColor);
    }

    [Fact]
    public void Analyzer_ProgramLayerWithoutKey_IsUnassigned()
    {
        var r = Analyzer.Run(new AnalysisInput
        {
            Model = Fixture(),
            RoleOverrides = new Dictionary<string, LayerGuess> { ["LOUNGE"] = new("LOUNGE", Roles.Program, "") },
        });
        Assert.All(r.Masses.Where(m => m.Source.LayerPath == "LOUNGE"), m => Assert.Equal(ProgramCatalog.Unassigned, m.ProgramKey));
        Assert.Contains(r.Issues, i => i.Kind == IssueKind.ProgramNoColor && i.Message.Contains("Unassigned"));
    }
}
