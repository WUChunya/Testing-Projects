using System.Reflection;

namespace PD.Rhino.Output;

/// <summary>
/// PRD 5.0: runs the master JSX in Illustrator through COM (falls back to manual "File > Scripts > Other Script").
/// The script saves and closes every document; when Illustrator was not running before, it is quit afterwards so the
/// export only generates files.
/// </summary>
public static class IllustratorLauncher
{
    const string ProgId = "Illustrator.Application";

    public static bool IsInstalled() => Type.GetTypeFromProgID(ProgId) != null;

    /// <summary>COM errors that mean Illustrator was busy, starting up or crashed in the call, not that the script is wrong.</summary>
    static readonly int[] Transient =
    {
        unchecked((int)0x80010105), // RPC_E_SERVERFAULT: the server threw an exception
        unchecked((int)0x80010001), // RPC_E_CALL_REJECTED
        unchecked((int)0x8001010A), // RPC_E_SERVERCALL_RETRYLATER
        unchecked((int)0x800706BA), // RPC_S_SERVER_UNAVAILABLE
        unchecked((int)0x800706BE), // RPC_S_CALL_FAILED
    };

    /// <summary>
    /// Blocks until Illustrator has run the script; call it off the UI thread. Returns an error message or null.
    /// A transient COM failure (Illustrator still starting, or it crashed in the call) is retried after a pause: the
    /// script resumes from its illustrator_progress.txt, keeping the files already saved and skipping the one Illustrator
    /// died on (named in illustrator_log.txt, user rev 0.8.7), so retries go on while each one gets further.
    /// </summary>
    public static string? Run(string jsxPath, bool quitWhenDone = true, int maxAttempts = 12)
    {
        var progress = Path.Combine(Path.GetDirectoryName(jsxPath) ?? "", "illustrator_progress.txt");
        var first = RunOnce(jsxPath, quitWhenDone, out var hresult);
        var error = first;
        for (var attempt = 2; error != null && Transient.Contains(hresult) && attempt <= maxAttempts; attempt++)
        {
            var before = Length(progress);
            // After the first retry, go on only while the script gets further each time (a crash at start-up repeats).
            if (attempt > 2 && before == 0) break;
            Thread.Sleep(TimeSpan.FromSeconds(8));
            error = RunOnce(jsxPath, quitWhenDone, out hresult);
            if (error != null && attempt > 2 && Length(progress) <= before) break;
        }
        return error == null || error == first ? error : $"{error} (first try: {first})";
    }

    static long Length(string path) => File.Exists(path) ? new FileInfo(path).Length : 0;

    static string? RunOnce(string jsxPath, bool quitWhenDone, out int hresult)
    {
        hresult = 0;
        var wasRunning = System.Diagnostics.Process.GetProcessesByName("Illustrator").Length > 0;
        // VERIFY-WIN: late-bound COM call, works without the Illustrator type library.
        var type = Type.GetTypeFromProgID(ProgId);
        if (type == null) return "Illustrator is not installed (no COM registration).";
        object? app = null;
        try
        {
            app = Activator.CreateInstance(type);
            type.InvokeMember("DoJavaScriptFile", BindingFlags.InvokeMethod, null, app, new object[] { jsxPath });
            // VERIFY-WIN: Application.Quit only when we started Illustrator; the user's own session stays open.
            if (quitWhenDone && !wasRunning)
                try { type.InvokeMember("Quit", BindingFlags.InvokeMethod, null, app, null); } catch (Exception) { }
            return null;
        }
        catch (TargetInvocationException ex)
        {
            hresult = ex.InnerException?.HResult ?? ex.HResult;
            return ex.InnerException?.Message ?? ex.Message;
        }
        catch (Exception ex)
        {
            hresult = ex.HResult;
            return ex.Message;
        }
        finally
        {
            if (app != null && OperatingSystem.IsWindows()) System.Runtime.InteropServices.Marshal.ReleaseComObject(app);
        }
    }
}
