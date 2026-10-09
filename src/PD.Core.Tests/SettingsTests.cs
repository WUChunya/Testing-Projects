using PD.Core.Analysis;
using PD.Core.Cleanup;
using PD.Core.Settings;
using Xunit;

namespace PD.Core.Tests;

public class SettingsTests
{
    [Fact]
    public void Settings_RoundTrip()
    {
        var s = new PdSettings();
        s.Layers["LAB"] = new LayerSetting { Role = "Program", ProgramKey = "LAB" };
        s.SpanModes["x"] = SpanMode.KeepTall;
        s.SingleSurfaces["y"] = SingleSurfaceChoice.Facade;
        s.Levels = new() { new LevelOverride("L1", 0, false) };
        s.Confirmed["layers"] = true;
        var back = PdSettings.FromJson(s.ToJson());
        Assert.Equal("LAB", back.Layers["LAB"].ProgramKey);
        Assert.Equal(SpanMode.KeepTall, back.SpanModes["x"]);
        Assert.Equal(SingleSurfaceChoice.Facade, back.SingleSurfaces["y"]);
        Assert.Single(back.Levels!);
        Assert.Contains("KeepTall", s.ToJson());
        Assert.NotNull(PdSettings.FromJson("{broken"));
    }

    [Fact]
    public void Naming_StableIdsUnique()
    {
        var rows = Naming.Build(AnalysisFixture.Result, new Dictionary<string, string>(), k => k);
        Assert.Equal(rows.Count, rows.Select(r => r.StableId).Distinct().Count());
        Assert.Contains(rows, r => r.StableId == "L01_LOBBY_01");
        Assert.All(rows, r => Assert.Matches("^[BL]\\d\\dM?\\d*_[A-Z_]+_\\d\\d$", r.StableId));
    }

    [Theory]
    [InlineData("L3", "L03")]
    [InlineData("L2M", "L02M")]
    [InlineData("B1", "B01")]
    public void Naming_Pad(string level, string expected) => Assert.Equal(expected, Naming.Pad(level));
}

public class ReportTests
{
    [Fact]
    public void AreaCsv_HasRowPerLevel()
    {
        var csv = PD.Core.Analysis.Reports.AreaCsv(AnalysisFixture.Result, k => k).Trim().Split('\n');
        Assert.Equal(AnalysisFixture.Result.Plans.Count + 1, csv.Length);
        Assert.StartsWith("Level,Elevation", csv[0]);
    }

    [Fact]
    public void Section_ThroughBuildingCrosses()
    {
        var b = AnalysisFixture.Result.Building;
        var midY = (b.MinY + b.MaxY) / 2;
        Assert.True(PD.Core.Analysis.Reports.SectionCrossesMasses(AnalysisFixture.Result, b.MinX - 10, midY, b.MaxX + 10, midY));
        Assert.False(PD.Core.Analysis.Reports.SectionCrossesMasses(AnalysisFixture.Result, b.MaxX + 100, b.MinY, b.MaxX + 100, b.MaxY));
    }
}
