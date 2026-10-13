using System.Diagnostics;
using PD.Core.Plans;
using PD.Core.Sections;
using PD.Core.Settings;
using PD.Core.Writers;
using Xunit;

namespace PD.Core.Tests;

/// <summary>
/// S6 acceptance, the parts checkable on Linux (PRD 7.3). T1–T4 are S1GeometryTests (template model); T5–T7 here.
/// Illustrator itself (T6 "opens without errors", T7 in a real .ai) is on the Windows checklist.
/// </summary>
public class S6AcceptanceTests
{
    /// <summary>T5: facades drawn by the A3 defaults, each on its own layer (plans: glass line; sections: cut / seen facade layers).</summary>
    [Fact]
    public void T5_FacadesOnTheirOwnLayers()
    {
        var ctx = S3SectionTests.Context();
        var plan = PlanDrawing.Build(ctx);
        Assert.NotEmpty(plan.Layer("FACADE").Items);
        Assert.All(plan.Layer("FACADE").Items, i => Assert.Null(i.Fill));
        var sec = SectionDrawing.Build(ctx, S3SectionTests.SectionA);
        var facadeLayers = sec.Layers.Where(l => l.Name == "FACADE" || l.Name.StartsWith("VIEW_GLASS") || l.Name.StartsWith("VIEW_FACADE")).ToList();
        Assert.Contains(facadeLayers, l => l.Items.Count > 0);
        // Exploded axons hide the facade (A3 default); stacked axons keep the veil off unless the style turns it on.
        var exploded = PD.Core.Axons.AxonDrawing.Build(ctx, PD.Core.Axons.AxonKind.Exploded);
        Assert.DoesNotContain(exploded.Layers, l => l.Name == "FACADE" && l.Items.Count > 0);
    }

    /// <summary>
    /// T6: one export writes every file of PRD 5.0 (every drawing switched on), the master JSX is valid JavaScript and
    /// every generated item has a unique stable name within its file (needed for the in-place update).
    /// </summary>
    [Fact]
    public void T6_OneExportWritesEveryFile()
    {
        var ctx = DowntownAnalysis.Context();
        var b = ctx.Result.Building;
        var (x0, y0, _) = ctx.Result.Frame.ToModel(b.MinX - 40, (b.MinY + b.MaxY) / 2, 0);
        var (x1, y1, _) = ctx.Result.Frame.ToModel(b.MaxX + 40, (b.MinY + b.MaxY) / 2, 0);
        var (ex0, ey0, _) = ctx.Result.Frame.ToModel(b.MinX - 40, b.MinY - 60, 0);
        var (ex1, ey1, _) = ctx.Result.Frame.ToModel(b.MaxX + 40, b.MinY - 60, 0);
        ctx.Settings.Sections.Add(new SectionSetting { Name = "A", Line = new[] { x0, y0, x1, y1 }, LookLeft = true, DepthFt = 400 });
        ctx.Settings.Sections.Add(new SectionSetting { Name = "North", Kind = SectionSetting.ElevationKind, Line = new[] { ex0, ey0, ex1, ey1 }, LookLeft = true, DepthFt = 400 });
        ctx.Settings.Output.Drawings = ctx.Rules.Output.Drawings.ToList();
        var dir = Path.Combine(Path.GetTempPath(), "pd_s6_" + Guid.NewGuid().ToString("N"));
        var res = Exporter.Write(ctx, dir);
        foreach (var f in new[] { "program_areas.csv", "program_table.csv", "diagram.json", "export_log.txt", "ProgramDiagram.jsx" })
            Assert.True(File.Exists(Path.Combine(dir, f)), f);
        foreach (var ai in new[] { "00_Site_Plan_SITE_PLAN", "00_Site_Plan_SITE_PLAN_ROOF", "01_Plans_PLAN_L1", "01_Plans_LEGEND___SYMBOLS", "02_Section_A", "07_Elevation_North", "03_Site_Axon",
                     "03_Axon_Stacked", "04_Axon_Exploded", "04_Axon_Exploded_B", "04_Axon_Levels_AXON_B1", "05_Program_Chart", "06_Program_Pie" })
            Assert.True(File.Exists(Path.Combine(dir, "_preview", ai + ".svg")), ai);
        Assert.DoesNotContain(res.Notes, n => n.Contains("failed", StringComparison.OrdinalIgnoreCase) || n.Contains("not implemented"));

        foreach (var d in Exporter.BuildDrawings(ctx, new List<string>()))
        {
            var names = d.Layers.SelectMany(l => l.Items).Select(i => i.Name).ToList();
            Assert.DoesNotContain(names, string.IsNullOrEmpty);
            var dup = names.GroupBy(n => n).FirstOrDefault(g => g.Count() > 1);
            Assert.True(dup == null, $"{d.FileName}: item name {dup?.Key} used {dup?.Count()} times");
        }
        CheckJs(File.ReadAllLines(res.JsxPath), dir);
        Directory.Delete(dir, true);
    }

    /// <summary>
    /// T7: the runtime's update steps (clear named items, purge stale generated layers) leave _MANUAL / OVERLAY and the
    /// user's unnamed items alone — run in node against a mock Illustrator document.
    /// </summary>
    [Fact]
    public void T7_ManualLayersSurviveReExport()
    {
        var harness = """
            var doc = { layers: [] };
            function Item(name) { this.name = name; this.locked = false; var self = this; this.remove = function () { self.removed = true; }; }
            function Layer(name, items) {
                this.name = name; this.locked = true; this.visible = false; this.layers = [];
                var all = items.map(function (n) { return new Item(n); });
                this.all = all;
                var self = this;
                this.pageItems = {
                    get length() { return all.filter(function (i) { return !i.removed; }).length; },
                    // ExtendScript collections are indexed; removal shrinks them.
                };
                this.refresh = function () {
                    var live = all.filter(function (i) { return !i.removed; });
                    for (var k = 0; k < 50; k++) delete self.pageItems[k];
                    live.forEach(function (it, k) { self.pageItems[k] = it; });
                };
                this.refresh();
                this.remove = function () { self.removedLayer = true; };
            }
            var manual = new Layer("_MANUAL", ["", "MY_NOTE", ""]);
            var overlay = new Layer("OVERLAY", ["ARROW"]);
            var program = new Layer("PD_PROGRAM", ["SEC_A_L03_LAB_01", ""]);
            var stale = new Layer("PD_OLD", ["SEC_A_OLD_01"]);
            doc.layers = [manual, overlay, program, stale];
            // Removing an item re-indexes its layer, like Illustrator.
            [manual, overlay, program, stale].forEach(function (l) {
                l.all.forEach(function (it) { var r = it.remove; it.remove = function () { r(); l.refresh(); }; });
            });
            var dw = { layers: [{ name: "PD_PROGRAM", items: [] }], protectedLayers: ["_MANUAL", "OVERLAY"] };
            PD.clearNamed(program);
            PD.purgeStaleLayers(doc, dw);
            function live(l) { var k = l.all.filter(function (i) { return !i.removed; }); return k.length + ":" + k.map(function (i) { return i.name || "-"; }).join(","); }
            var out = [live(manual), live(overlay), live(program), live(stale), stale.removedLayer ? "gone" : "kept", manual.removedLayer ? "x" : "ok"].join("|");
            console.log(out);
            """;
        var dir = Path.Combine(Path.GetTempPath(), "pd_t7_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var js = Path.Combine(dir, "t7.js");
        File.WriteAllText(js, JsxWriter.Runtime() + "\n" + harness);
        var output = RunNode(js);
        if (output == null) return; // node not installed
        // _MANUAL and OVERLAY untouched (named or not); PD_PROGRAM keeps only the user's unnamed item; the stale PD_ layer is emptied and removed.
        Assert.Equal("3:-,MY_NOTE,-|1:ARROW|1:-|0:|gone|ok", output.Trim());
        Directory.Delete(dir, true);
    }

    static void CheckJs(IEnumerable<string> jsx, string dir)
    {
        var js = Path.Combine(dir, "check.js");
        File.WriteAllLines(js, jsx.Where(l => !l.StartsWith("#target")));
        var psi = new ProcessStartInfo("node", $"--check \"{js}\"") { RedirectStandardError = true, UseShellExecute = false };
        Process? p;
        try { p = Process.Start(psi); }
        catch (System.ComponentModel.Win32Exception) { return; }
        if (p == null) return;
        var err = p.StandardError.ReadToEnd();
        p.WaitForExit();
        Assert.True(p.ExitCode == 0, err);
    }

    static string? RunNode(string js)
    {
        var psi = new ProcessStartInfo("node", $"\"{js}\"") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        Process? p;
        try { p = Process.Start(psi); }
        catch (System.ComponentModel.Win32Exception) { return null; }
        if (p == null) return null;
        var o = p.StandardOutput.ReadToEnd();
        var e = p.StandardError.ReadToEnd();
        p.WaitForExit();
        Assert.True(p.ExitCode == 0, e);
        return o;
    }
}
