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
        if (wanted.Contains("Section"))
        {
            var secs = ctx.Settings.Sections.Where(s => s.Line.Length == 4).ToList();
            if (secs.Count == 0) notes.Add("Section: no section line (tab ⑥).");
            foreach (var s in secs) drawings.Add(SectionDrawing.Build(ctx, s));
        }
        foreach (var w in wanted.Where(w => w != "Section"))
            notes.Add($"{w}: not implemented yet (Plan/Axon in S4, charts in S5).");
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
        foreach (var d in drawings)
            for (var a = 0; a < d.Artboards.Count; a++)
                Save(Path.Combine(preview, Path.GetFileNameWithoutExtension(d.FileName) + (a > 0 ? $"_{a + 1}" : "") + ".svg"), SvgWriter.Write(d, a));
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
