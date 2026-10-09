using System.Runtime.InteropServices;
using Eto.Forms;
using Rhino.UI;
using PD.Rhino.Adapter;
using PD.Rhino.UI.Tabs;

namespace PD.Rhino.UI;

/// <summary>Dockable panel with tabs ①–⑧ (PRD 3).</summary>
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
            new ColorsTab(_session), new SectionTab(_session), new OutputTab(_session), new CheckTab(_session),
        };
        var titles = new[] { "① 图层", "② 清洗", "③ 楼层", "④ 名称", "⑤ 配色", "⑥ 剖切", "⑦ 输出", "⑧ 检查" };
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
