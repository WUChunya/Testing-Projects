using PD.Core.Classification;
using PD.Core.Cleanup;
using PD.Core.Config;
using PD.Core.Levels;
using PD.Core.Model;

namespace PD.Core.Analysis;

public sealed record LevelOverride(string Name, double Elevation, bool IsMezzanine);

public sealed class AnalysisInput
{
    public required SourceModel Model { get; init; }
    public Rules Rules { get; init; } = Rules.Default();
    public LayerClassifier Classifier { get; init; } = LayerClassifier.Default();
    /// <summary>User role choices by layer path; missing layers are auto-filled.</summary>
    public IReadOnlyDictionary<string, LayerGuess> RoleOverrides { get; init; } = new Dictionary<string, LayerGuess>();
    /// <summary>Per-object program keys overriding the layer's program.</summary>
    public IReadOnlyDictionary<string, string> ProgramOverrides { get; init; } = new Dictionary<string, string>();
    public IReadOnlyDictionary<string, SpanMode> SpanOverrides { get; init; } = new Dictionary<string, SpanMode>();
    public IReadOnlyDictionary<string, SingleSurfaceChoice> SingleSurfaceChoices { get; init; } = new Dictionary<string, SingleSurfaceChoice>();
    /// <summary>Stray objects the user chose to keep.</summary>
    public IReadOnlySet<string> KeepStray { get; init; } = new HashSet<string>();
    /// <summary>User-edited levels; null means detect.</summary>
    public IReadOnlyList<LevelOverride>? Levels { get; init; }
}

public sealed class AnalysisResult
{
    public required LocalFrame Frame { get; init; }
    public required BBox3 Building { get; init; }
    public required SourceModel Local { get; init; }
    public required Dictionary<string, LayerGuess> Roles { get; init; }
    public required HashSet<string> Stray { get; init; }
    public required List<Mass> Masses { get; init; }
    public required List<string> FacadeIds { get; init; }
    public required LevelResult Detected { get; init; }
    public required List<Level> Levels { get; init; }
    public required List<SpanDecision> Spans { get; init; }
    public required List<LevelPlan> Plans { get; init; }
    public required List<Overlap> Overlaps { get; init; }
    public required List<Issue> Issues { get; init; }
}

/// <summary>Runs the Core pipeline: roles → localize → masses → levels → spans/overlaps → issues.</summary>
public static class Analyzer
{
    public static AnalysisResult Run(AnalysisInput input)
    {
        var rules = input.Rules;
        var roles = input.Model.Layers.ToDictionary(l => l.Path,
            l => input.RoleOverrides.TryGetValue(l.Path, out var o) ? o : input.Classifier.Guess(l));
        foreach (var o in input.Model.Objects)
        {
            if (roles.TryGetValue(o.LayerPath, out var g)) { o.Role = Roles.Normalize(g.Role); o.ProgramKey = g.ProgramKey; }
            // Cores and stairs are masses with their own program (unless the layer names one).
            if (Roles.MassProgram(o.Role) is not { } implied) continue;
            o.Role = Roles.Program;
            if (string.IsNullOrEmpty(o.ProgramKey)) o.ProgramKey = implied;
            if (input.ProgramOverrides.TryGetValue(o.Id, out var key) && !string.IsNullOrEmpty(key)) o.ProgramKey = key;
            if (string.IsNullOrEmpty(o.ProgramKey)) o.ProgramKey = ProgramCatalog.Unassigned;
        }

        var (local, frame, building, stray) = Localizer.Localize(input.Model, o => o.Role == Roles.Program, rules.Cleanup);
        stray.ExceptWith(input.KeepStray);
        var live = local.Objects.Where(o => !stray.Contains(o.Id)).ToList();

        var masses = live.Where(o => o.Role == Roles.Program).Select(o => MassClassifier.Classify(o, rules.Cleanup)).ToList();
        var facade = live.Where(o => o.Role == Roles.Facade).ToList();
        facade.AddRange(masses.Where(m => m.Status == MassStatus.SingleSurface
            && input.SingleSurfaceChoices.GetValueOrDefault(m.Id) == SingleSurfaceChoice.Facade).Select(m => m.Source));

        var detected = LevelDetector.Detect(
            LevelDetector.SlabCandidates(live.Where(o => o.Role == Roles.Slab), rules.Levels),
            LevelDetector.MassCandidates(masses.Select(m => m.Source))
                .Concat(LevelDetector.FacadeCandidates(facade, rules.Levels)).ToList(),
            rules.Levels);
        var levels = input.Levels == null ? detected.Levels
            : input.Levels.OrderBy(l => l.Elevation).Select(l => new Level { Name = l.Name, Elevation = l.Elevation, IsMezzanine = l.IsMezzanine }).ToList();
        if (input.Levels != null && levels.LastOrDefault(l => !l.IsMezzanine) is { } roof) roof.IsRoof = true;

        var (spans, plans, overlaps) = LevelAssignment.Run(masses, levels, rules.Cleanup, input.SpanOverrides);

        var issues = new List<Issue>();
        foreach (var id in stray)
            issues.Add(new Issue(IssueKind.StrayObject, $"Object on '{Layer(local, id)}' is far from the building: excluded", new[] { id }));
        foreach (var m in masses)
        {
            if (m.Status == MassStatus.Open)
                issues.Add(new Issue(IssueKind.OpenMass, $"{m.Source.LayerPath} mass is open and cannot be capped", new[] { m.Id }));
            if (m.Status == MassStatus.SectionClosed)
                issues.Add(new Issue(IssueKind.RepairedMass, $"{m.Source.LayerPath} mass had a non-planar opening: closed along the section line, please check", new[] { m.Id }));
            if (m.Status == MassStatus.SingleSurface && input.SingleSurfaceChoices.GetValueOrDefault(m.Id) == SingleSurfaceChoice.Ask)
                issues.Add(new Issue(IssueKind.SingleSurface, $"{m.Source.LayerPath}: single surface on a program layer (ignore / facade / extrude)", new[] { m.Id }));
        }
        foreach (var o in overlaps)
            issues.Add(new Issue(IssueKind.Overlap,
                $"{levels[o.LevelIndex].Name}: {Program(masses, o.WinnerId)} overrides {Program(masses, o.LoserId)} ({o.Area:0} SF, smaller volume wins)",
                new[] { o.WinnerId, o.LoserId }, levels[o.LevelIndex].Elevation));
        issues.AddRange(FacadeDetector.Candidates(live, building, rules));
        foreach (var l in input.Model.Layers.Where(l => roles[l.Path].IsFallback && input.Model.Objects.Any(o => o.LayerPath == l.Path)))
            issues.Add(new Issue(IssueKind.UnclassifiedLayer, $"Layer '{l.Path}' has no role (set on tab 1)", Array.Empty<string>()));
        var unnamed = masses.Where(m => string.IsNullOrWhiteSpace(m.Source.Name)).Select(m => m.Id).ToArray();
        if (unnamed.Length > 0)
            issues.Add(new Issue(IssueKind.UnnamedMass, $"{unnamed.Length} masses have no name: program name used", unnamed));
        foreach (var g in masses.GroupBy(m => m.ProgramKey))
        {
            var ids = g.Select(m => m.Id).ToArray();
            if (g.Key == ProgramCatalog.Unassigned)
                issues.Add(new Issue(IssueKind.ProgramNoColor, $"{ids.Length} masses have no program (layer or tab 4): drawn as Unassigned", ids));
            else if (input.Classifier.Programs.FirstOrDefault(d => d.Key == g.Key) is not { } def)
                issues.Add(new Issue(IssueKind.ProgramNoColor, $"Program '{g.Key}' is not in the colour scheme (tab 5)", ids));
            else if (def.Fill == null && def.Stroke == null)
                issues.Add(new Issue(IssueKind.ProgramNoColor, $"Program '{def.Label}' has neither fill nor outline", ids));
        }
        if (input.Levels == null)
            issues.AddRange(detected.Issues.Select(i => new Issue(i.Kind switch
            {
                LevelIssueKind.Mezzanine => IssueKind.Mezzanine,
                LevelIssueKind.Scattered => IssueKind.LevelScattered,
                LevelIssueKind.SingleSource => IssueKind.LevelSingleSource,
                LevelIssueKind.NoSlab => IssueKind.LevelNoSlab,
                _ => IssueKind.LevelUnsupported,
            }, i.Message, i.ObjectIds, i.Elevation)));

        return new AnalysisResult
        {
            Frame = frame, Building = building, Local = local, Roles = roles, Stray = stray, Masses = masses,
            FacadeIds = facade.Select(f => f.Id).ToList(), Detected = detected, Levels = levels, Spans = spans,
            Plans = plans, Overlaps = overlaps, Issues = issues,
        };
    }

    static string Layer(SourceModel m, string id) => m.Objects.First(o => o.Id == id).LayerPath;

    static string Program(List<Mass> masses, string id) => masses.First(m => m.Id == id).Source.LayerPath;
}
