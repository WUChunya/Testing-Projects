using Rhino;
using Rhino.Commands;
using PD.Rhino.Adapter;
using PD.Rhino.Display;

namespace PD.Rhino.Commands;

/// <summary>Toggles the level-plane preview in all viewports.</summary>
public sealed class LevelPreviewCommand : Command
{
    public override string EnglishName => "ProgramDiagramLevels";

    protected override Result RunCommand(RhinoDoc doc, RunMode mode)
    {
        var session = PdSession.For(doc.RuntimeSerialNumber);
        if (session.Result == null) session.Analyze();
        Toggle(session, !LevelConduit.Instance.Enabled);
        return Result.Success;
    }

    public static void Toggle(PdSession session, bool on)
    {
        LevelConduit.Instance.Update(session.Result);
        LevelConduit.Instance.Enabled = on;
        session.Doc?.Views.Redraw();
    }
}
