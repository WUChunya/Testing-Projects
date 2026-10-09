using Rhino;
using Rhino.Commands;
using Rhino.DocObjects;
using Rhino.Input;
using PD.Core.Settings;
using PD.Rhino.Adapter;

namespace PD.Rhino.Commands;

/// <summary>Tab ⑥ "pick": select a line or curve as a section cut (start → end, looking left).</summary>
public sealed class PickSectionCommand : Command
{
    public override string EnglishName => "ProgramDiagramPickSection";

    protected override Result RunCommand(RhinoDoc doc, RunMode mode)
    {
        var rc = RhinoGet.GetOneObject("Select section line", false, ObjectType.Curve, out var objRef);
        if (rc != Result.Success || objRef?.Curve() is not { } curve) return rc;
        var session = PdSession.For(doc.RuntimeSerialNumber);
        var name = ((char)('A' + session.Settings.Sections.Count % 26)).ToString();
        session.Settings.Sections.Add(new SectionSetting
        {
            // An edge picked from a Brep is fine as a cut line, but its object id is the Brep, so do not store it.
            Name = name, ObjectId = objRef.Object() is CurveObject ? objRef.ObjectId.ToString() : null,
            Line = new[] { curve.PointAtStart.X, curve.PointAtStart.Y, curve.PointAtEnd.X, curve.PointAtEnd.Y },
        });
        session.NotifyChanged();
        RhinoApp.WriteLine($"Program Diagram: section {name} added.");
        return Result.Success;
    }
}
