using Rhino;
using Rhino.PlugIns;
using Rhino.UI;
using PD.Rhino.UI;

namespace PD.Rhino;

public sealed class ProgramDiagramPlugin : PlugIn
{
    public ProgramDiagramPlugin() => Instance = this;

    public static ProgramDiagramPlugin? Instance { get; private set; }

    public override PlugInLoadTime LoadTime => PlugInLoadTime.AtStartup;

    protected override LoadReturnCode OnLoad(ref string errorMessage)
    {
        // VERIFY-WIN: panel icon from embedded .ico
        Panels.RegisterPanel(this, typeof(PdPanel), "Program Diagram", typeof(ProgramDiagramPlugin).Assembly,
            "PD.Rhino.Resources.icon.ico", PanelType.PerDoc);
        RhinoDoc.CloseDocument += (_, e) => Adapter.PdSession.Forget(e.Document.RuntimeSerialNumber);
        return LoadReturnCode.Success;
    }
}
