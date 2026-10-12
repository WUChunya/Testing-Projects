using System.Runtime.InteropServices;
using Eto.Forms;
using Rhino.UI;
using PD.Core.Settings;
using PD.Rhino.Adapter;
using PD.Rhino.UI.Tabs;

namespace PD.Rhino.UI;

/// <summary>Dockable panel with tabs 1–9 (PRD 3); sections and elevations have separate tabs.</summary>
[Guid("3C1F7E2A-58B4-4D0B-9E61-7A2D4B8C5F90")]
public sealed class PdPanel : Panel, IPanel
{
    readonly PdSession _session;
    readonly List<TabBase> _tabs;

    public PdPanel(uint documentSerialNumber)
    {
        _session = PdSession.For(documentSerialNumber);
        _tabs = new List<TabBase>
        {
            new LayersTab(_session), new CleanupTab(_session), new LevelsTab(_session), new NamesTab(_session),
            new ColorsTab(_session), new SectionTab(_session, SectionSetting.SectionKind), new SectionTab(_session, SectionSetting.ElevationKind),
            new OutputTab(_session), new CheckTab(_session),
        };
        var titles = new[] { "1 Layers", "2 Cleanup", "3 Levels", "4 Names", "5 Colors", "6 Sections", "7 Elevations", "8 Output", "9 Check" };
        var tabs = new TabControl();
        for (var i = 0; i < _tabs.Count; i++)
            tabs.Pages.Add(new TabPage { Text = titles[i], Content = new Scrollable { Content = _tabs[i], Border = BorderType.None } });
        Content = tabs;
        foreach (var t in _tabs) t.Refresh();
    }

    public void PanelShown(uint documentSerialNumber, ShowPanelReason reason) { }

    public void PanelHidden(uint documentSerialNumber, ShowPanelReason reason) { }

    public void PanelClosing(uint documentSerialNumber, bool onCloseDocument) { }
}
