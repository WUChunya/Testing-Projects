// Linux export harness: fixture JSON (mm) + plugin settings JSON (ft) -> one SVG per drawing artboard.
using PD.Core.Classification;
using PD.Core.Analysis; using PD.Core.Config; using PD.Core.IO; using PD.Core.Model;
var s = PD.Core.Settings.PdSettings.FromJson(File.ReadAllText(args[1]));
var m = FixtureReader.Load(args[0]);
foreach (var sec in s.Sections) sec.Line = sec.Line.Select(v => v * 304.8).ToArray();
s.Output.FillDefaults(Rules.Default().Output);
if (args.Length > 3) s.Output.Drawings = args[3].Split(',').ToList();
if (Environment.GetEnvironmentVariable("PD_ARTBOARD") is { Length: > 0 } ab) s.Output.Artboard = ab;
var programs = ProgramCatalog.Effective(LayerClassifier.LoadPrograms(), s.Programs);
var roles = s.RoleOverrides().ToDictionary(kv => kv.Key, kv => kv.Value);
foreach (var l in m.Layers.Where(l => !roles.ContainsKey(l.Path))) roles[l.Path] = new LayerGuess(l.Path, Roles.Ignore, "");
var r = Analyzer.Run(new AnalysisInput
{
    Model = m, Classifier = ProgramCatalog.Classifier(programs), RoleOverrides = roles, ProgramOverrides = s.ObjectPrograms,
    SpanOverrides = s.SpanModes, SingleSurfaceChoices = s.SingleSurfaces, KeepStray = s.KeepStray.ToHashSet(), Levels = s.Levels,
});
var ctx = new PD.Core.Drawing.ExportContext { Result = r, Settings = s, Programs = programs };
// PD_LABEL_NAMED=1 (CAMPUS 10-23): no leaders, names shrink to fit (down to 6 pt), only rooms with a name of their own.
if (Environment.GetEnvironmentVariable("PD_LABEL_NAMED") == "1")
{
    var ps = ctx.Style.Plan;
    ps.LabelUniform = false; ps.LabelLeaders = false; ps.LabelNamedOnly = true; ps.LabelMinPt = 6;
}
var dir = args[2]; Directory.CreateDirectory(dir);
var notes = new List<string>();
foreach (var d in PD.Core.Writers.Exporter.BuildDrawings(ctx, notes))
    for (var a = 0; a < d.Artboards.Count; a++)
        File.WriteAllText(Path.Combine(dir, PD.Core.Writers.Exporter.PreviewName(d, a)), PD.Core.Writers.SvgWriter.Write(d, a));
File.WriteAllLines(Path.Combine(dir, "notes.txt"), notes);
File.WriteAllLines(Path.Combine(dir, "levels.txt"), r.Levels.Select(l => $"{l.Name} {l.Elevation}"));
File.WriteAllLines(Path.Combine(dir, "roles.txt"), r.Roles.Values.Select(g => $"{g.LayerPath} -> {g.Role} {g.ProgramKey}"));
Console.WriteLine($"{notes.Count} notes; {Directory.GetFiles(dir, "*.svg").Length} svg");
