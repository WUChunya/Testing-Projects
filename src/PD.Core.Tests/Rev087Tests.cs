using PD.Core.Axons;
using PD.Core.Writers;
using Xunit;

namespace PD.Core.Tests;

/// <summary>User feedback on v0.8.6 (Windows export of the Downtown model rev B, NE view): the same four points again.</summary>
public class Rev087Tests
{
    // ------------------------------------------------------------------ (1) stacked: plates as in Rhino, no overlaps

    [Fact]
    public void Stacked_NoRoofUnderAFloorPlateShowsThrough()
    {
        // A block stretched to the floor above stops under that floor's plate (its "_U" piece). Its top lies 1 ft under
        // the plate, so it can never be seen: it showed where the cycle breaker of the hidden-face pass took it before
        // the plate (the L4 plate edge round the recessed L4, and the L2 plate over the L1 lobby, were missing).
        var d = AxonDrawing.Build(DowntownAnalysis.UserContext(), AxonKind.Stacked);
        var hidden = d.Layer("PD_PROGRAM").Items.Where(i => System.Text.RegularExpressions.Regex.IsMatch(i.Name, @"_U\d+_TOP")).Select(i => i.Name).ToList();
        Assert.True(hidden.Count == 0, string.Join(", ", hidden));
        Assert.Contains(d.Layer("PD_PROGRAM").Items, i => i.Name.Contains("_L4_EXPOSED_ROOF") && i.Name.EndsWith("_TOP"));
    }

    // ------------------------------------------------------------------ (2) exploded: the basement complete

    [Fact]
    public void Basement_CirculationPlateIsAProgram()
    {
        // The 1 ft circulation plate on the B1 floor was cut just under its top and came back empty: B1 showed a hole
        // (with pit walls round it) where its corridors are.
        var r = DowntownAnalysis.Result;
        Assert.Contains(r.Plans[0].Footprints, f => f.Mass.ProgramKey == "CIRCULATION" && f.Resolved.Area > 5000);
        var d = AxonDrawing.Build(DowntownAnalysis.UserContext(), AxonKind.Exploded);
        Assert.Contains(d.Layer("AXON_B1").Items, i => i.Name.Contains("_CIRCULATION_"));
        var order = d.Layers.Select(l => l.Name).ToList();
        Assert.True(order.IndexOf("AXON_B1") < order.IndexOf("AXON_CONNECTOR_BELOW"));
        Assert.True(order.IndexOf("AXON_CONNECTOR_BELOW") < order.IndexOf("AXON_SITE"));
    }

    // ------------------------------------------------------------------ (4) glass in every axon, 50 %

    [Fact]
    public void Glass_InTheExplodedAndFloorKeyAxons()
    {
        var ctx = DowntownAnalysis.UserContext();
        Assert.Equal("storey", ctx.Style.Facade.ExplodedAxon);
        var ex = AxonDrawing.Build(ctx, AxonKind.Exploded);
        var items = ex.Layers.Where(l => l.Name.StartsWith("AXON_L")).SelectMany(l => l.Items).ToList();
        Assert.Contains(items, i => i.Name.Contains("FACADE_GLASS") && i.Opacity == ctx.Style.Facade.StackedAxonOpacity);
        Assert.Contains(items, i => i.Name.Contains("EXPOSED_GLASS") && i.Opacity == ctx.Style.Exposed.GlassOpacity);
        var levels = AxonDrawing.Build(ctx, AxonKind.Levels);
        Assert.Contains(levels.Layer("FACADE").Items, i => i.Name.Contains("FACADE_GLASS") && i.Opacity == ctx.Style.Facade.StackedAxonOpacity);

        ctx = DowntownAnalysis.UserContext();
        ctx.Style.Facade.ExplodedAxon = "hidden";
        Assert.DoesNotContain(AxonDrawing.Build(ctx, AxonKind.Exploded).Layers.SelectMany(l => l.Items), i => i.Name.Contains("FACADE_GLASS"));
    }
}

/// <summary>User rev 0.8.7: Illustrator died in the script (RPC_E_SERVERFAULT twice) and left no log at all.</summary>
public class Rev087RuntimeTests
{
    /// <summary>
    /// A run that takes Illustrator down is resumed: the files saved before are kept, the file it died on is skipped and
    /// named with its last step in illustrator_log.txt, the rest is drawn; the progress file goes once a run finishes.
    /// </summary>
    [Fact]
    public void CrashedRun_ResumesAfterTheFileItDiedOn()
    {
        var dir = Path.Combine(Path.GetTempPath(), "pd_resume_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var harness = """
            var fs = require("fs");
            function File(p) { this.p = p; this.encoding = ""; this.lines = null; this.i = 0; this.mode = ""; }
            Object.defineProperty(File.prototype, "exists", { get: function () { return fs.existsSync(this.p); } });
            Object.defineProperty(File.prototype, "eof", { get: function () { return this.i >= this.lines.length; } });
            File.prototype.open = function (m) { this.mode = m; if (m === "r") { this.lines = fs.readFileSync(this.p, "utf8").split("\n").filter(function (l) { return l.length; }); this.i = 0; } else if (m === "w") fs.writeFileSync(this.p, ""); return true; };
            File.prototype.readln = function () { return this.lines[this.i++]; };
            File.prototype.writeln = function (t) { fs.appendFileSync(this.p, t + "\n"); };
            File.prototype.close = function () { };
            File.prototype.remove = function () { fs.unlinkSync(this.p); };
            var app = { userInteractionLevel: 0, coordinateSystem: 0 };
            var UserInteractionLevel = { DONTDISPLAYALERTS: 1 }, CoordinateSystem = { DOCUMENTCOORDINATESYSTEM: 1 };
            var crashOn = process.argv[2];
            PD.drawFile = function (dw, opts, warnings) {
                PD.progress("AT layer PD_PROGRAM");
                if (dw.fileName === crashOn) process.exit(3); // Illustrator gone mid-file
                fs.appendFileSync(opts.outDir + "/drawn.txt", dw.fileName + "\n");
            };
            var out = process.argv[3];
            PD.run([{ fileName: "A.ai" }, { fileName: "B.ai" }, { fileName: "C.ai" }], { outDir: out });
            """;
        var js = Path.Combine(dir, "resume.js");
        File.WriteAllText(js, JsxWriter.Runtime() + "\n" + harness);
        int Node(string crash)
        {
            var psi = new System.Diagnostics.ProcessStartInfo("node", $"\"{js}\" {crash} \"{dir.Replace('\\', '/')}\"") { UseShellExecute = false, RedirectStandardError = true };
            System.Diagnostics.Process? p;
            try { p = System.Diagnostics.Process.Start(psi); }
            catch (System.ComponentModel.Win32Exception) { return -1; }
            p!.WaitForExit();
            return p.ExitCode;
        }
        if (Node("B.ai") == -1) return; // node not installed
        Assert.False(File.Exists(Path.Combine(dir, "illustrator_log.txt")));
        Assert.Equal(0, Node("none"));
        Assert.Equal(new[] { "A.ai", "C.ai" }, File.ReadAllLines(Path.Combine(dir, "drawn.txt")));
        var log = File.ReadAllLines(Path.Combine(dir, "illustrator_log.txt"));
        Assert.Equal("OK A.ai", log[0]);
        Assert.Equal("ERROR B.ai: Illustrator crashed while drawing it (last step: layer PD_PROGRAM); skipped", log[1]);
        Assert.Equal("OK C.ai", log[2]);
        Assert.False(File.Exists(Path.Combine(dir, "illustrator_progress.txt")));
        Directory.Delete(dir, true);
    }
}
