using Rhino;
using Rhino.Commands;
using Rhino.UI;
using PD.Rhino.Adapter;
using PD.Rhino.UI;

namespace PD.Rhino.Commands;

/// <summary>Opens the Program Diagram panel and analyses the model on first use.</summary>
public sealed class ProgramDiagramCommand : Command
{
    public override string EnglishName => "ProgramDiagram";

    protected override Result RunCommand(RhinoDoc doc, RunMode mode)
    {
        Panels.OpenPanel(typeof(PdPanel).GUID);
        var session = PdSession.For(doc.RuntimeSerialNumber);
        if (session.Result == null && !session.Analyze())
        {
            RhinoApp.WriteLine($"Program Diagram: {session.LastError}");
            return Result.Failure;
        }
        var r = session.Result!;
        RhinoApp.WriteLine($"Program Diagram: {r.Masses.Count} masses, {r.Levels.Count} levels, {r.Issues.Count} items to check.");
        return Result.Success;
    }
}
