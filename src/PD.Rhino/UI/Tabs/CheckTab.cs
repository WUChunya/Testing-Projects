using Eto.Forms;
using PD.Core.Analysis;
using PD.Rhino.Adapter;

namespace PD.Rhino.UI.Tabs;

/// <summary>⑧ Uncertain items (PRD B2): double-click to locate, tick to ignore.</summary>
internal sealed class CheckTab : TabBase
{
    readonly GridView _grid = Ui.Grid(420);
    readonly CheckBox _showIgnored = new() { Text = "显示已忽略项" };
    readonly Label _status = new();

    public CheckTab(PdSession s) : base(s)
    {
        _grid.Columns.Add(Ui.Check<Issue>("忽略", i => s.Settings.IgnoredIssues.Contains(i.Key), (i, v) =>
        {
            s.Settings.IgnoredIssues.Remove(i.Key);
            if (v) s.Settings.IgnoredIssues.Add(i.Key);
            s.Save();
        }));
        _grid.Columns.Add(Ui.Text<Issue>("类别", i => KindText(i.Kind), null, 90));
        _grid.Columns.Add(Ui.Text<Issue>("说明 (双击定位)", i => i.Message, null, 420));
        _grid.CellDoubleClick += (_, e) => { if (e.Item is Issue i) Ui.SelectAndZoom(Session, i.ObjectIds); };
        _showIgnored.CheckedChanged += (_, _) => Refresh();

        var layout = Ui.Layout();
        layout.AddSeparateRow(Ui.Button("重新分析 Re-analyze", Analyze), _showIgnored, null);
        layout.Add(_grid, yscale: true);
        layout.Add(_status);
        layout.Add(Ui.Confirm(s, "check"));
        Content = layout;
    }

    public override void Refresh()
    {
        var r = Session.Result;
        if (r == null) { _status.Text = "尚未分析。"; _grid.DataStore = null; return; }
        var ignored = Session.Settings.IgnoredIssues.ToHashSet();
        var shown = r.Issues.Where(i => _showIgnored.Checked == true || !ignored.Contains(i.Key)).OrderBy(i => i.Kind).ToList();
        _grid.DataStore = shown;
        _status.Text = $"{r.Issues.Count(i => !ignored.Contains(i.Key))} 项待处理，{r.Issues.Count(i => ignored.Contains(i.Key))} 项已忽略。";
    }

    static string KindText(IssueKind k) => k switch
    {
        IssueKind.OpenMass => "体块未闭合",
        IssueKind.RepairedMass => "已自动修复",
        IssueKind.SingleSurface => "单张曲面",
        IssueKind.Overlap => "体块重叠",
        IssueKind.FacadeCandidate => "立面候选",
        IssueKind.StrayObject => "游离对象",
        IssueKind.UnclassifiedLayer => "图层未分类",
        IssueKind.UnnamedMass => "体块无名称",
        IssueKind.Mezzanine => "夹层待确认",
        IssueKind.LevelUnsupported => "离群标高",
        IssueKind.LevelScattered => "楼层数据分散",
        IssueKind.LevelSingleSource => "楼层单一来源",
        IssueKind.LevelNoSlab => "楼层无楼板",
        IssueKind.SectionMissesMasses => "剖切线未穿过体块",
        IssueKind.ProgramNoColor => "缺少配色",
        _ => k.ToString(),
    };
}
