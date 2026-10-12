using System.Text;
using System.Text.Json;
using PD.Core.Analysis;
using PD.Core.Drawing;
using PD.Core.Model;
using PD.Core.Sections;

namespace PD.Core.Writers;

public sealed record ExportResult(string Folder, string JsxPath, List<string> Files, List<string> Notes);

/// <summary>PRD 5.0: writes the output folder (CSV, diagram.json, log, SVG previews, master JSX).</summary>
public static class Exporter
{
    public static List<PD.Core.Drawing.Drawing> BuildDrawings(ExportContext ctx, List<string> notes)
    {
        var drawings = new List<PD.Core.Drawing.Drawing>();
        var wanted = ctx.Settings.Output.Drawings ?? ctx.Rules.Output.Drawings;

        // One drawing that fails must not lose the whole export: it is reported and the others are still written.
        void Add(string what, Func<PD.Core.Drawing.Drawing> build)
        {
            try { drawings.Add(build()); }
            catch (Exception e) { notes.Add($"{what}: not written ({e.GetType().Name}: {e.Message})"); }
        }

        if (wanted.Contains("SitePlan")) Add("SitePlan", () => PD.Core.Plans.PlanDrawing.BuildSite(ctx));
        if (wanted.Contains("Plan")) Add("Plan", () => PD.Core.Plans.PlanDrawing.Build(ctx));
        if (wanted.Contains("Section"))
        {
            var secs = ctx.Settings.Sections.Where(s => s.Line.Length == 4 && !s.IsElevation).ToList();
            if (secs.Count == 0) notes.Add("Section: no section line (tab 6 Sections).");
            foreach (var s in secs) Add($"Section {s.Name}", () => SectionDrawing.Build(ctx, s));
        }
        if (wanted.Contains("Elevation"))
        {
            var els = ctx.Settings.Sections.Where(s => s.Line.Length == 4 && s.IsElevation).ToList();
            if (els.Count == 0) notes.Add("Elevation: no elevation line (tab 7 Elevations).");
            foreach (var s in els) Add($"Elevation {s.Name}", () => PD.Core.Elevations.ElevationDrawing.Build(ctx, s));
        }
        foreach (var (key, kind) in new[]
                 {
                     ("AxonStacked", PD.Core.Axons.AxonKind.Stacked), ("AxonExploded", PD.Core.Axons.AxonKind.Exploded),
                     ("AxonExplodedB", PD.Core.Axons.AxonKind.ExplodedB), ("AxonLevels", PD.Core.Axons.AxonKind.Levels),
                 })
            if (wanted.Contains(key))
                Add(key, () => PD.Core.Axons.AxonDrawing.Build(ctx, kind));
        if (wanted.Contains("ProgramChart")) Add("ProgramChart", () => PD.Core.Charts.ProgramChartDrawing.Build(ctx));
        if (wanted.Contains("Pie")) Add("Pie", () => PD.Core.Charts.PieDrawing.Build(ctx));
        foreach (var w in wanted.Where(w => w is not ("SitePlan" or "SiteAxon" or "Plan" or "Section" or "Elevation" or "AxonStacked" or "AxonExploded"
                     or "AxonExplodedB" or "AxonLevels" or "ProgramChart" or "Pie")))
            notes.Add($"{w}: not a known drawing.");
        foreach (var d in drawings) notes.AddRange(d.Notes);
        return drawings;
    }

    public static ExportResult Write(ExportContext ctx, string folder)
    {
        Directory.CreateDirectory(folder);
        var preview = Path.Combine(folder, "_preview");
        Directory.CreateDirectory(preview);
        var notes = new List<string>();
        var drawings = BuildDrawings(ctx, notes);
        var files = new List<string>();

        void Save(string path, string text)
        {
            File.WriteAllText(path, text, new UTF8Encoding(false));
            files.Add(path);
        }

        Save(Path.Combine(folder, "program_areas.csv"), Reports.AreaCsv(ctx.Result, ctx.ProgramLabel));
        var table = PD.Core.Charts.ProgramTable.For(ctx);
        notes.AddRange(table.Notes);
        Save(Path.Combine(folder, "program_table.csv"), Reports.TableCsv(table, ctx.ProgramLabel));
        foreach (var d in drawings)
            for (var a = 0; a < d.Artboards.Count; a++)
                Save(Path.Combine(preview, PreviewName(d, a)), SvgWriter.Write(d, a));
        var jsx = Path.Combine(folder, "ProgramDiagram.jsx");
        Save(jsx, JsxWriter.Write(drawings, folder, ctx.Settings.Output.ExportPng ?? ctx.Rules.Output.ExportPng, ctx.Rules.Output.PngScalePercent));
        Save(Path.Combine(folder, "diagram.json"), JsonSerializer.Serialize(new
        {
            generated = DateTime.Now.ToString("s"),
            units = "ft (local; XY origin at building bbox min)",
            origin = new { x = ctx.Result.Frame.OriginX, y = ctx.Result.Frame.OriginY, feetPerUnit = ctx.Result.Frame.FeetPerUnit },
            levels = ctx.Result.Levels.Select(l => new { l.Name, l.Elevation, l.IsMezzanine, l.IsRoof }),
            settings = ctx.Settings,
            drawings,
        }, JsxWriter.Json));
        Save(Path.Combine(folder, "export_log.txt"), Log(ctx, notes));
        return new ExportResult(folder, jsx, files, notes);
    }

    /// <summary>"02_Section_A.svg"; multi-artboard files add the artboard name ("01_Plans_PLAN_L1.svg").</summary>
    public static string PreviewName(PD.Core.Drawing.Drawing d, int artboard) =>
        Path.GetFileNameWithoutExtension(d.FileName) + (d.Artboards.Count > 1 ? "_" + SectionDrawing.Safe(d.Artboards[artboard].Name) : "") + ".svg";

    static string Log(ExportContext ctx, List<string> notes)
    {
        var sb = new StringBuilder($"Program Diagram export {DateTime.Now:yyyy-MM-dd HH:mm}\n\n");
        foreach (var n in notes) sb.AppendLine("NOTE  " + n);
        var ignored = ctx.Settings.IgnoredIssues.ToHashSet();
        foreach (var i in ctx.Result.Issues)
            sb.AppendLine((ignored.Contains(i.Key) ? "IGNORED  " : "CHECK  ") + i.Message);
        sb.AppendLine();
        foreach (var l in ctx.Result.Levels) sb.AppendLine($"LEVEL  {l.Name}  {FeetInches.Format(l.Elevation)}");
        return sb.ToString();
    }
}
