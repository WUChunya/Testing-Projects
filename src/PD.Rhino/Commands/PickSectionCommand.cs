using Rhino;
using Rhino.Commands;
using Rhino.DocObjects;
using Rhino.Input;
using PD.Core.Settings;
using PD.Rhino.Adapter;

namespace PD.Rhino.Commands;

/// <summary>Tab 6 / tab 7 "Pick": select a line or curve as a section cut or an elevation (start → end, looking left).</summary>
public abstract class PickLineCommand : Command
{
    protected abstract string Kind { get; }

    protected override Result RunCommand(RhinoDoc doc, RunMode mode)
    {
        var elevation = Kind == SectionSetting.ElevationKind;
        var rc = RhinoGet.GetOneObject(elevation ? "Select elevation line" : "Select section line", false, ObjectType.Curve, out var objRef);
        if (rc != Result.Success || objRef?.Curve() is not { } curve) return rc;
        var session = PdSession.For(doc.RuntimeSerialNumber);
        var count = session.Settings.Sections.Count(s => s.Kind == Kind);
        var name = elevation ? $"E{count + 1}" : ((char)('A' + count % 26)).ToString();
        session.Settings.Sections.Add(new SectionSetting
        {
            // An edge picked from a Brep is fine as a line, but its object id is the Brep, so do not store it.
            Name = name, Kind = Kind, ObjectId = objRef.Object() is CurveObject ? objRef.ObjectId.ToString() : null,
            Line = new[] { curve.PointAtStart.X, curve.PointAtStart.Y, curve.PointAtEnd.X, curve.PointAtEnd.Y },
        });
        session.NotifyChanged();
        RhinoApp.WriteLine($"Program Diagram: {(elevation ? "elevation" : "section")} {name} added.");
        return Result.Success;
    }
}

public sealed class PickSectionCommand : PickLineCommand
{
    public override string EnglishName => "ProgramDiagramPickSection";
    protected override string Kind => SectionSetting.SectionKind;
}

public sealed class PickElevationCommand : PickLineCommand
{
    public override string EnglishName => "ProgramDiagramPickElevation";
    protected override string Kind => SectionSetting.ElevationKind;
}
