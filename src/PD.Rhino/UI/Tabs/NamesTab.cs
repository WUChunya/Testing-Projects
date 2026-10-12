using Eto.Forms;
using PD.Core.Analysis;
using PD.Rhino.Adapter;

namespace PD.Rhino.UI.Tabs;

/// <summary>Tab 4 Names: label per mass (object name → program name), stable ids L03_LAB_01, batch replace.</summary>
internal sealed class NamesTab : TabBase
{
    readonly GridView _grid = Ui.Grid(360, multi: true);
    readonly MultiSelect<NameRow> _sel;
    readonly DropDown _bulkProgram = new() { Width = 140 };
    readonly ComboBoxCell _programCell;
    readonly TextBox _find = new() { PlaceholderText = "Find" };
    readonly TextBox _replace = new() { PlaceholderText = "Replace with" };

    public NamesTab(PdSession s) : base(s)
    {
        _sel = new MultiSelect<NameRow>(_grid);
        _grid.Columns.Add(Ui.Text<NameRow>("ID", r => r.StableId, null, 130));
        // Per-object program: overrides the layer's program for special cases (re-analyses on change).
        var programCol = Ui.Combo<NameRow>("Program", Keys().Cast<string>(), r => r.ProgramKey, SetProgram, 120);
        _programCell = (ComboBoxCell)programCol.DataCell;
        _grid.Columns.Add(programCol);
        _grid.Columns.Add(Ui.Text<NameRow>("Label", r => r.Label, (r, v) =>
        {
            foreach (var x in _sel.Targets(r)) s.Settings.Names[x.MassId] = v;
            s.Save();
        }, 180));
        _grid.CellDoubleClick += (_, e) => { if (e.Item is NameRow r) Ui.SelectAndZoom(Session, _sel.Targets(r).Select(x => x.MassId)); };

        var layout = Ui.Layout();
        layout.Add(_grid, yscale: true);
        layout.AddSeparateRow(_find, _replace, Ui.Button("Replace all", ReplaceAll), Ui.Button("Clear edits", Reset));
        layout.AddSeparateRow(new Label { Text = "Selected rows (Ctrl / Shift):" }, _bulkProgram,
            Ui.Button("Set program", () => SetPrograms(_sel.Selected, _bulkProgram.SelectedKey)), null);
        layout.Add(new Label { Text = "The Program column overrides the layer's program for one mass (special cases)." });
        layout.Add(Ui.Confirm(s, "names"));
        Content = layout;
    }

    List<NameRow> Rows() => Session.Result == null ? new() : Naming.Build(Session.Result, Session.Settings.Names, Session.ProgramLabel);

    List<object> Keys() => Session.Programs.Select(p => (object)p.Key).ToList();

    public override void Refresh()
    {
        _programCell.DataStore = Keys();
        _bulkProgram.Items.Clear();
        foreach (var k in Keys()) _bulkProgram.Items.Add((string)k, (string)k);
        _sel.Reset();
        _grid.DataStore = Rows();
    }

    void SetProgram(NameRow r, string key) => SetPrograms(_sel.Targets(r), key);

    /// <summary>Per-object program for every given row (one re-analysis for the whole selection).</summary>
    void SetPrograms(IEnumerable<NameRow> rows, string? key)
    {
        if (string.IsNullOrEmpty(key)) return;
        var changed = false;
        foreach (var r in rows.Where(r => r.ProgramKey != key))
        {
            var mass = Session.Result?.Masses.FirstOrDefault(m => m.Id == r.MassId);
            var layerKey = mass == null ? null : Session.Settings.Layers.GetValueOrDefault(mass.Source.LayerPath)?.ProgramKey;
            if (key == layerKey) Session.Settings.ObjectPrograms.Remove(r.MassId);
            else Session.Settings.ObjectPrograms[r.MassId] = key;
            changed = true;
        }
        if (changed) Application.Instance.AsyncInvoke(Analyze);
    }

    void ReplaceAll()
    {
        if (string.IsNullOrEmpty(_find.Text)) return;
        foreach (var r in Rows().Where(r => r.Label.Contains(_find.Text, StringComparison.OrdinalIgnoreCase)))
            Session.Settings.Names[r.MassId] = r.Label.Replace(_find.Text, _replace.Text ?? "", StringComparison.OrdinalIgnoreCase);
        Session.NotifyChanged();
    }

    void Reset()
    {
        Session.Settings.Names.Clear();
        var hadPrograms = Session.Settings.ObjectPrograms.Count > 0;
        Session.Settings.ObjectPrograms.Clear();
        if (hadPrograms) Analyze(); else Session.NotifyChanged();
    }
}
