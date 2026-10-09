using PD.Core.Analysis;
using PD.Core.IO;

namespace PD.Core.Tests;

/// <summary>Runs the pipeline once on template.json for all S1 tests.</summary>
public static class AnalysisFixture
{
    static readonly Lazy<AnalysisResult> Lazy = new(() =>
        Analyzer.Run(new AnalysisInput { Model = FixtureReader.Load(FixtureSmokeTests.FixturePath("template.json")) }));

    public static AnalysisResult Result => Lazy.Value;
}
