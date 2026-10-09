using Rhino;
using Rhino.Commands;

namespace PD.Rhino.Commands;

/// <summary>Opens the Program Diagram panel (S2). For now reports layer/object counts.</summary>
public sealed class ProgramDiagramCommand : Command
{
    public override string EnglishName => "ProgramDiagram";

    protected override Result RunCommand(RhinoDoc doc, RunMode mode)
    {
        RhinoApp.WriteLine($"Program Diagram: {doc.Layers.Count} layers, {doc.Objects.Count} objects.");
        return Result.Success;
    }
}
