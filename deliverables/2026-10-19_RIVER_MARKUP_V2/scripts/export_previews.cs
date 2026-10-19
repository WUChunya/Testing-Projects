using PD.Core.Analysis; using PD.Core.Classification; using PD.Core.Config; using PD.Core.Settings; using PD.Core.Drawing; using PD.Core.Writers; using PD.Core.IO; using PD.Core.Model;
var model = FixtureReader.Load(args[0]);
var settings = PdSettings.FromJson(File.ReadAllText(args[1]));
var k = model.Units == "Millimeters" ? 304.8 : 1.0;
foreach (var sec in settings.Sections) sec.Line = sec.Line.Select(v => v * k).ToArray();
if (settings.Output.AxonViewPoint is { } vp) settings.Output.AxonViewPoint = vp.Select(v => v * k).ToArray();
var rules = Rules.Default();
settings.Output.FillDefaults(rules.Output);
var programs = ProgramCatalog.Effective(LayerClassifier.LoadPrograms(), settings.Programs);
var roles = settings.RoleOverrides().ToDictionary(kv => kv.Key, kv => kv.Value);
foreach (var l in model.Layers.Where(l => !roles.ContainsKey(l.Path))) roles[l.Path] = new LayerGuess(l.Path, Roles.Ignore, "");
var res = Analyzer.Run(new AnalysisInput { Model = model, Rules = rules, Classifier = ProgramCatalog.Classifier(programs),
  RoleOverrides = roles, ProgramOverrides = settings.ObjectPrograms, SpanOverrides = settings.SpanModes,
  SingleSurfaceChoices = settings.SingleSurfaces, KeepStray = settings.KeepStray.ToHashSet(), Levels = settings.Levels });
Console.WriteLine($"analysis ok: {res.Levels.Count} levels, {res.Masses.Count} masses");
var ctx = new ExportContext { Result = res, Settings = settings, Programs = programs, Rules = rules };
var all = (Environment.GetEnvironmentVariable("PD_ONLY")?.Split(",").ToList()) ?? (settings.Output.Drawings ?? rules.Output.Drawings).ToList();
var outDir = args.Length > 2 ? args[2] : null;
foreach (var w in all)
{
  settings.Output.Drawings = new() { w };
  try { var notes = new List<string>(); var ds = Exporter.BuildDrawings(ctx, notes); Console.WriteLine($"OK {w}");
    if (outDir != null) foreach (var d in ds) for (var a = 0; a < d.Artboards.Count; a++) File.WriteAllText(Path.Combine(outDir, Exporter.PreviewName(d, a)), SvgWriter.Write(d, a)); }
  catch (Exception ex) { Console.WriteLine($"FAIL {w}: {ex.GetType().Name} {ex.Message}\n{ex.StackTrace}"); }
}
