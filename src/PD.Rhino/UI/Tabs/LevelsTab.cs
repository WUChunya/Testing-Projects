using Eto.Forms;
using PD.Core.Analysis;
using PD.Core.Model;
using PD.Rhino.Adapter;
using PD.Rhino.Commands;

namespace PD.Rhino.UI.Tabs;

/// <summary>Tab 3 Levels (PRD B1): edit names/elevations, mezzanines, adopt candidate elevations, viewport preview.</summary>
internal sealed class LevelsTab : TabBase
{
    sealed class Row
    {
        public string Name = "";
        public double Elevation;
        public bool Mezzanine;
        public string Sources = "";
        public string Spread = "";
        public string[] Ids = Array.Empty<string>();
    }

    sealed record Candidate(double Elevation, string Message, string[] Ids);

    readonly GridView _grid = Ui.Grid(300);
    readonly GridView _candidates = Ui.Grid(110);
    readonly CheckBox _preview = new() { Text = "Preview levels in the viewport" };
    readonly Label _status = new();
    List<Row> _rows = new();

    public LevelsTab(PdSession s) : base(s)
    {
        _grid.Columns.Add(Ui.Text<Row>("Name", r => r.Name, (r, v) => r.Name = v.Trim(), 60));
        _grid.Columns.Add(Ui.Text<Row>("Elevation", r => Ui.Ft(r.Elevation), (r, v) => { if (FeetInches.TryParse(v, out var f)) r.Elevation = f; }, 80));
        _grid.Columns.Add(Ui.Check<Row>("Mezzanine", r => r.Mezzanine, (r, v) => r.Mezzanine = v));
        _grid.Columns.Add(Ui.Text<Row>("Sources", r => r.Sources, null, 130));
        _grid.Columns.Add(Ui.Text<Row>("Spread", r => r.Spread, null, 60));
        _grid.CellDoubleClick += (_, e) => { if (e.Item is Row r) Ui.SelectAndZoom(Session, r.Ids); };

        _candidates.Columns.Add(Ui.Text<Candidate>("Elevations not used (mezzanine or outlier)", c => c.Message, null, 380));
        _candidates.CellDoubleClick += (_, e) => { if (e.Item is Candidate c) Ui.SelectAndZoom(Session, c.Ids); };

        _preview.CheckedChanged += (_, _) => LevelPreviewCommand.Toggle(Session, _preview.Checked == true);

        var layout = Ui.Layout();
        layout.AddSeparateRow(Ui.Button("Apply", Apply), Ui.Button("Remove / merge down", RemoveSelected), Ui.Button("Re-detect", Redetect), null);
        layout.Add(_grid, yscale: true);
        layout.Add(_candidates);
        layout.AddSeparateRow(Ui.Button("Add as mezzanine", AdoptCandidate), null);
        layout.Add(_preview);
        layout.Add(_status);
        layout.Add(Ui.Confirm(s, "levels"));
        Content = layout;
    }

    public override void Refresh()
    {
        var r = Session.Result;
        if (r == null) { _status.Text = "Not analyzed yet."; return; }
        _rows = r.Levels.Select(l => new Row
        {
            Name = l.Name, Elevation = l.Elevation, Mezzanine = l.IsMezzanine,
            Sources = l.Sources == 0 ? "User" : l.Sources.ToString(),
            Spread = l.Members.Count == 0 ? "" : Ui.Ft(l.Spread),
            Ids = l.Members.SelectMany(m => m.ObjectIds).Distinct().ToArray(),
        }).ToList();
        _grid.DataStore = _rows;
        _candidates.DataStore = r.Issues.Where(i => i.Kind == IssueKind.LevelUnsupported && i.Elevation != null)
            .Select(i => new Candidate(i.Elevation!.Value, i.Message, i.ObjectIds)).ToList();
        var warn = r.Issues.Count(i => i.Kind is IssueKind.LevelScattered or IssueKind.LevelNoSlab or IssueKind.Mezzanine);
        _status.Text = (Session.Settings.Levels == null ? "Detected" : "Edited by user") + $": {r.Levels.Count} levels, {warn} item(s) to check (tab 9).";
        if (_preview.Checked == true) LevelPreviewCommand.Toggle(Session, true);
    }

    void Apply()
    {
        Session.Settings.Levels = _rows.OrderBy(r => r.Elevation).Select(r => new LevelOverride(r.Name, r.Elevation, r.Mezzanine)).ToList();
        Analyze();
    }

    void RemoveSelected()
    {
        if (_grid.SelectedItem is not Row row) return;
        _rows.Remove(row);
        Apply();
    }

    void Redetect()
    {
        Session.Settings.Levels = null;
        Analyze();
    }

    void AdoptCandidate()
    {
        if (_candidates.SelectedItem is not Candidate c) return;
        var below = _rows.Where(r => r.Elevation <= c.Elevation && !r.Mezzanine).OrderBy(r => r.Elevation).LastOrDefault();
        _rows.Add(new Row { Name = (below?.Name ?? "L0") + "M", Elevation = c.Elevation, Mezzanine = true });
        Apply();
    }
}
