using Rhino.PlugIns;

namespace PD.Rhino;

public sealed class ProgramDiagramPlugin : PlugIn
{
    public ProgramDiagramPlugin() => Instance = this;

    public static ProgramDiagramPlugin? Instance { get; private set; }

    public override PlugInLoadTime LoadTime => PlugInLoadTime.AtStartup;
}
