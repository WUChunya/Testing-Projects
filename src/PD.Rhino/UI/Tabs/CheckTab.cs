using Eto.Forms;
using PD.Core.Analysis;
using PD.Rhino.Adapter;

namespace PD.Rhino.UI.Tabs;

/// <summary>Tab 9 Uncertain items (PRD B2): double-click to locate, tick to ignore.</summary>
internal sealed class CheckTab : TabBase
{
    readonly GridView _grid = Ui.Grid(420);
    readonly CheckBox _showIgnored = new() { Text = "Show ignored" };
    readonly Label _status = new();

    public CheckTab(PdSession s) : base(s)
    {
        _grid.Columns.Add(Ui.Check<Issue>("Ignore", i => s.Settings.IgnoredIssues.Contains(i.Key), (i, v) =>
        {
            s.Settings.IgnoredIssues.Remove(i.Key);
            if (v) s.Settings.IgnoredIssues.Add(i.Key);
            s.Save();
        }));
        _grid.Columns.Add(Ui.Text<Issue>("Kind", i => KindText(i.Kind), null, 90));
        _grid.Columns.Add(Ui.Text<Issue>("Description (double-click to locate)", i => i.Message, null, 420));
        _grid.CellDoubleClick += (_, e) => { if (e.Item is Issue i) Ui.SelectAndZoom(Session, i.ObjectIds); };
        _showIgnored.CheckedChanged += (_, _) => Refresh();

        var layout = Ui.Layout();
        layout.AddSeparateRow(Ui.Button("Re-analyze", Analyze), _showIgnored, null);
        layout.Add(_grid, yscale: true);
        layout.Add(_status);
        layout.Add(Ui.Confirm(s, "check"));
        Content = layout;
    }

    public override void Refresh()
    {
        var r = Session.Result;
        if (r == null) { _status.Text = "Not analyzed yet."; _grid.DataStore = null; return; }
        var ignored = Session.Settings.IgnoredIssues.ToHashSet();
        var shown = r.Issues.Where(i => _showIgnored.Checked == true || !ignored.Contains(i.Key)).OrderBy(i => i.Kind).ToList();
        _grid.DataStore = shown;
        _status.Text = $"{r.Issues.Count(i => !ignored.Contains(i.Key))} to check, {r.Issues.Count(i => ignored.Contains(i.Key))} ignored.";
    }

    static string KindText(IssueKind k) => k switch
    {
        IssueKind.OpenMass => "Open mass",
        IssueKind.RepairedMass => "Auto-repaired",
        IssueKind.SingleSurface => "Single surface",
        IssueKind.Overlap => "Overlap",
        IssueKind.FacadeCandidate => "Facade candidate",
        IssueKind.StrayObject => "Stray object",
        IssueKind.UnclassifiedLayer => "Layer without role",
        IssueKind.UnnamedMass => "Unnamed mass",
        IssueKind.Mezzanine => "Mezzanine?",
        IssueKind.LevelUnsupported => "Outlier elevation",
        IssueKind.LevelScattered => "Scattered level",
        IssueKind.LevelSingleSource => "Single-source level",
        IssueKind.LevelNoSlab => "Level without slab",
        IssueKind.SectionMissesMasses => "Section misses masses",
        IssueKind.ProgramNoColor => "Missing colour",
        _ => k.ToString(),
    };
}
