using Rhino;
using Rhino.Commands;
using Rhino.Input;
using PD.Rhino.Adapter;
using PD.Rhino.Display;

namespace PD.Rhino.Commands;

/// <summary>
/// Tab 8 "Place view in plan…" (user rev 0.8.3): pick where the viewer stands in plan; every axon then looks from
/// that point at the building. Shown as a V marker (apex at the viewer, opening towards the building).
/// </summary>
public sealed class PlaceAxonViewCommand : Command
{
    public override string EnglishName => "ProgramDiagramPlaceAxonView";

    protected override Result RunCommand(RhinoDoc doc, RunMode mode)
    {
        var rc = RhinoGet.GetPoint("Viewer position for the axons (click in plan, outside the building)", false, out var pt);
        if (rc != Result.Success) return rc;
        var session = PdSession.For(doc.RuntimeSerialNumber);
        session.Settings.Output.AxonView = "Point";
        session.Settings.Output.AxonViewPoint = new[] { pt.X, pt.Y };
        session.NotifyChanged();
        AxonViewConduit.Instance.Update(session);
        doc.Views.Redraw();
        RhinoApp.WriteLine("Program Diagram: axon view placed; every axon now looks from this point at the building.");
        return Result.Success;
    }
}
