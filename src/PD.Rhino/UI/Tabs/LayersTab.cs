using System.Text.Json;
using Eto.Forms;
using PD.Core.Settings;
using PD.Rhino.Adapter;

namespace PD.Rhino.UI.Tabs;

/// <summary>Tab 1 Layer roles and style modes (PRD A1). Pre-filled from keywords; sub-layers judged on their own.</summary>
internal sealed class LayersTab : TabBase
{
    sealed record Row(string Path, int Count, LayerSetting S);

    static readonly string[] Roles = { "Program", "BG.Building", "BG.Road", "BG.Hardscape", "BG.Landscape", "BG.Tree", "BG.Water", "BG.Facade", "BG.Slab", "BG.Base", "BG.Line", "Ignore" };
    static readonly string[] Modes = { "", "grayscale", "line", "color" };

    readonly GridView _grid = Ui.Grid(320);
    readonly ComboBoxCell _programCell;
    readonly Label _status = new();

    public LayersTab(PdSession s) : base(s)
    {
        _grid.Columns.Add(Ui.Check<Row>("Use", r => r.S.Use, (r, v) => Edit(() => r.S.Use = v)));
        _grid.Columns.Add(Ui.Text<Row>("Layer", r => r.Path, null, 170));
        _grid.Columns.Add(Ui.Text<Row>("Objects", r => r.Count.ToString(), null, 44));
        _grid.Columns.Add(Ui.Combo<Row>("Role", Roles, r => r.S.Role, (r, v) => Edit(() => { r.S.Role = v; r.S.Mode = PdSession.DefaultMode(v); }), 110));
        var programCol = Ui.Combo<Row>("Program", ProgramKeys().Cast<string>(), r => r.S.ProgramKey, (r, v) => Edit(() => r.S.ProgramKey = v), 120);
        _programCell = (ComboBoxCell)programCol.DataCell;
        _grid.Columns.Add(programCol);
        _grid.Columns.Add(Ui.Combo<Row>("Mode", Modes, r => r.S.Mode, (r, v) => Edit(() => r.S.Mode = v), 90));

        var layout = Ui.Layout();
        layout.AddSeparateRow(Ui.Button("Analyze model", Analyze), Ui.Button("Re-fill from keywords", Refill), null);
        layout.Add(_grid, yscale: true);
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

    /// <summary>Keys of the current scheme, custom programs included (refreshed when tab 5 changes).</summary>
    List<object> ProgramKeys() => new object[] { "" }.Concat(Session.Programs.Select(p => (object)p.Key)).ToList();

    public override void Refresh()
    {
        _programCell.DataStore = ProgramKeys();
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
            .OrderBy(r => r.Path).ToList();
        var unclassified = Session.Result?.Issues.Count(i => i.Kind == PD.Core.Analysis.IssueKind.UnclassifiedLayer) ?? 0;
        _status.Text = $"{ex.ObjectCountByLayer.Count} layers with objects; {unclassified} without a keyword match. Click Analyze model after changes.";
    }

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
