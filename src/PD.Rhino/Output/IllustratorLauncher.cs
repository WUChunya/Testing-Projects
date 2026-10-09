using System.Reflection;

namespace PD.Rhino.Output;

/// <summary>PRD 5.0: runs the master JSX in Illustrator through COM; falls back to manual "File > Scripts > Other Script".</summary>
public static class IllustratorLauncher
{
    const string ProgId = "Illustrator.Application";

    public static bool IsInstalled() => Type.GetTypeFromProgID(ProgId) != null;

    /// <summary>Blocks until Illustrator has run the script; call it off the UI thread. Returns an error message or null.</summary>
    public static string? Run(string jsxPath)
    {
        // VERIFY-WIN: late-bound COM call, works without the Illustrator type library.
        var type = Type.GetTypeFromProgID(ProgId);
        if (type == null) return "Illustrator is not installed (no COM registration).";
        object? app = null;
        try
        {
            app = Activator.CreateInstance(type);
            type.InvokeMember("DoJavaScriptFile", BindingFlags.InvokeMethod, null, app, new object[] { jsxPath });
            return null;
        }
        catch (TargetInvocationException ex)
        {
            return ex.InnerException?.Message ?? ex.Message;
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
        finally
        {
            if (app != null && OperatingSystem.IsWindows()) System.Runtime.InteropServices.Marshal.ReleaseComObject(app);
        }
    }
}
