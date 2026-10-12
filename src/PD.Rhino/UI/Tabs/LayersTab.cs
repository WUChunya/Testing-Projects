using System.Text.Json;
using Eto.Forms;
using PD.Core.Settings;
using PD.Rhino.Adapter;

namespace PD.Rhino.UI.Tabs;

/// <summary>Tab 1 Layer roles and style modes (PRD A1). Pre-filled from keywords; sub-layers judged on their own.</summary>
internal sealed class LayersTab : TabBase
{
    sealed record Row(string Path, int Count, LayerSetting S);

    /// <summary>Program and NC.* = our new construction; BG.* = existing context.</summary>
    static readonly string[] Roles = PD.Core.Classification.Roles.All;
    static readonly string[] Modes = { "", "grayscale", "line", "color" };
    static readonly string[] Groups = { "All", "NC (new construction)", "BG (context)", "Entourage", "Unclassified" };

    readonly GridView _grid = Ui.Grid(320, multi: true);
    readonly MultiSelect<Row> _sel;
    readonly DropDown _bulkRole = new() { Width = 120 };
    readonly DropDown _bulkProgram = new() { Width = 130 };
    readonly ComboBoxCell _programCell;
    readonly DropDown _group = new() { Width = 170 };
    readonly Label _status = new();

    public LayersTab(PdSession s) : base(s)
    {
        _sel = new MultiSelect<Row>(_grid);
        // Ctrl / Shift-select rows, then change any cell of a selected row: every selected layer follows.
        _grid.Columns.Add(Ui.Check<Row>("Use", r => r.S.Use, (r, v) => EditAll(r, x => x.S.Use = v)));
        _grid.Columns.Add(Ui.Text<Row>("Layer", r => r.Path, null, 170));
        _grid.Columns.Add(Ui.Text<Row>("Objects", r => r.Count.ToString(), null, 44));
        _grid.Columns.Add(Ui.Text<Row>("Group", r => Group(r.S.Role), null, 46));
        _grid.Columns.Add(Ui.Combo<Row>("Role", Roles, r => r.S.Role, (r, v) => EditAll(r, x => { x.S.Role = v; x.S.Mode = PdSession.DefaultMode(v); }), 110));
        var programCol = Ui.Combo<Row>("Program", ProgramKeys().Cast<string>(), r => r.S.ProgramKey, (r, v) => EditAll(r, x => x.S.ProgramKey = v), 120);
        _programCell = (ComboBoxCell)programCol.DataCell;
        _grid.Columns.Add(programCol);
        _grid.Columns.Add(Ui.Combo<Row>("Mode", Modes, r => r.S.Mode, (r, v) => EditAll(r, x => x.S.Mode = v), 90));
        foreach (var r in Roles) _bulkRole.Items.Add(r);

        foreach (var g in Groups) _group.Items.Add(g);
        _group.SelectedIndex = 0;
        _group.SelectedIndexChanged += (_, _) => Refresh();

        var layout = Ui.Layout();
        layout.AddSeparateRow(Ui.Button("Analyze model", Analyze), Ui.Button("Re-fill from keywords", Refill),
            new Label { Text = "Show:" }, _group, null);
        layout.Add(_grid, yscale: true);
        layout.AddSeparateRow(new Label { Text = "Selected rows:" }, Ui.Button("Use", () => Bulk(x => x.S.Use = true)),
            Ui.Button("Don't use", () => Bulk(x => x.S.Use = false)), _bulkRole,
            Ui.Button("Set role", () => { if (_bulkRole.SelectedValue is IListItem i) Bulk(x => { x.S.Role = i.Text; x.S.Mode = PdSession.DefaultMode(i.Text); }); }),
            _bulkProgram, Ui.Button("Set program", () => Bulk(x => x.S.ProgramKey = _bulkProgram.SelectedKey ?? "")), null);
        layout.AddSeparateRow(Ui.Button("Save mapping template...", SaveTemplate), Ui.Button("Load mapping template...", LoadTemplate), null);
        layout.Add(_status);
        layout.Add(Ui.Confirm(s, "layers"));
        Content = layout;
    }

    void Edit(Action change)
    {
        change();
        Session.Save();
    }

    /// <summary>A cell edit on a selected row applies to the whole selection (Rhino layer panel behaviour).</summary>
    void EditAll(Row row, Action<Row> change)
    {
        var rows = _sel.Targets(row);
        foreach (var r in rows) change(r);
        Session.Save();
        if (rows.Count > 1) _grid.Invalidate();
    }

    void Bulk(Action<Row> change)
    {
        var rows = _sel.Selected;
        if (rows.Count == 0) return;
        foreach (var r in rows) change(r);
        Session.Save();
        _grid.Invalidate();
    }

    /// <summary>Keys of the current scheme, custom programs included (refreshed when tab 5 changes).</summary>
    List<object> ProgramKeys() => new object[] { "" }.Concat(Session.Programs.Select(p => (object)p.Key)).ToList();

    public override void Refresh()
    {
        _programCell.DataStore = ProgramKeys();
        _bulkProgram.Items.Clear();
        foreach (var k in ProgramKeys()) _bulkProgram.Items.Add((string)k, (string)k);
        _sel.Reset();
        var ex = Session.Extraction;
        if (ex == null)
        {
            _grid.DataStore = null;
            _status.Text = "Click Analyze model to read the layers.";
            return;
        }
        _grid.DataStore = Session.Settings.Layers
            .Where(kv => ex.ObjectCountByLayer.ContainsKey(kv.Key))
            .Select(kv => new Row(kv.Key, ex.ObjectCountByLayer[kv.Key], kv.Value))
            .Where(InGroup)
            .OrderBy(r => r.Path).ToList();
        var unclassified = Session.Result?.Issues.Count(i => i.Kind == PD.Core.Analysis.IssueKind.UnclassifiedLayer) ?? 0;
        _status.Text = $"{ex.ObjectCountByLayer.Count} layers with objects; {unclassified} without a keyword match. Click Analyze model after changes.";
    }

    /// <summary>Two-letter group of a role (PRD A1): NC = our new construction, BG = context.</summary>
    static string Group(string role) => role switch
    {
        "" or PD.Core.Classification.Roles.Ignore => "",
        PD.Core.Classification.Roles.Entourage => "ENT",
        _ => PD.Core.Classification.Roles.IsNew(role) ? "NC" : "BG",
    };

    bool InGroup(Row r) => _group.SelectedIndex switch
    {
        1 => Group(r.S.Role) == "NC",
        2 => Group(r.S.Role) == "BG",
        3 => Group(r.S.Role) == "ENT",
        4 => Group(r.S.Role).Length == 0,
        _ => true,
    };

    void Refill()
    {
        Session.Settings.Layers.Clear();
        Analyze();
    }

    void SaveTemplate()
    {
        var dlg = new SaveFileDialog { Title = "Layer mapping template", FileName = "layer_mapping.json" };
        dlg.Filters.Add(new FileFilter("JSON", ".json"));
        if (dlg.ShowDialog(this) != DialogResult.Ok) return;
        File.WriteAllText(dlg.FileName, JsonSerializer.Serialize(Session.Settings.Layers, PdSettings.Json));
    }

    /// <summary>Applies a saved mapping by full path, else by the layer's own name.</summary>
    void LoadTemplate()
    {
        var dlg = new OpenFileDialog { Title = "Layer mapping template" };
        dlg.Filters.Add(new FileFilter("JSON", ".json"));
        if (dlg.ShowDialog(this) != DialogResult.Ok) return;
        var map = JsonSerializer.Deserialize<Dictionary<string, LayerSetting>>(File.ReadAllText(dlg.FileName), PdSettings.Json);
        if (map == null) return;
        static string Own(string p) => p.Split("::").Last();
        foreach (var key in Session.Settings.Layers.Keys.ToList())
        {
            var hit = map.TryGetValue(key, out var v) ? v : map.FirstOrDefault(kv => Own(kv.Key).Equals(Own(key), StringComparison.OrdinalIgnoreCase)).Value;
            if (hit != null) Session.Settings.Layers[key] = hit;
        }
        Analyze();
    }
}
