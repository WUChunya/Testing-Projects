using System.Text.Json;
using Eto.Forms;
using PD.Core.Settings;
using PD.Rhino.Adapter;

namespace PD.Rhino.UI.Tabs;

/// <summary>① Layer roles and style modes (PRD A1). Pre-filled from keywords; sub-layers judged on their own.</summary>
internal sealed class LayersTab : TabBase
{
    sealed record Row(string Path, int Count, LayerSetting S);

    static readonly string[] Roles = { "Program", "BG.Building", "BG.Road", "BG.Hardscape", "BG.Landscape", "BG.Tree", "BG.Water", "BG.Facade", "BG.Slab", "BG.Base", "BG.Line", "Ignore" };
    static readonly string[] Modes = { "", "grayscale", "line", "color" };

    readonly GridView _grid = Ui.Grid(320);
    readonly Label _status = new();

    public LayersTab(PdSession s) : base(s)
    {
        var programs = new[] { "" }.Concat(s.Classifier.Programs.Select(p => p.Key)).ToArray();
        _grid.Columns.Add(Ui.Check<Row>("使用", r => r.S.Use, (r, v) => Edit(() => r.S.Use = v)));
        _grid.Columns.Add(Ui.Text<Row>("图层 Layer", r => r.Path, null, 170));
        _grid.Columns.Add(Ui.Text<Row>("对象", r => r.Count.ToString(), null, 44));
        _grid.Columns.Add(Ui.Combo<Row>("角色 Role", Roles, r => r.S.Role, (r, v) => Edit(() => { r.S.Role = v; r.S.Mode = PdSession.DefaultMode(v); }), 110));
        _grid.Columns.Add(Ui.Combo<Row>("Program", programs, r => r.S.ProgramKey, (r, v) => Edit(() => r.S.ProgramKey = v), 120));
        _grid.Columns.Add(Ui.Combo<Row>("风格 Mode", Modes, r => r.S.Mode, (r, v) => Edit(() => r.S.Mode = v), 90));

        var layout = Ui.Layout();
        layout.AddSeparateRow(Ui.Button("分析模型 Analyze", Analyze), Ui.Button("按关键词重新预填", Refill), null);
        layout.Add(_grid, yscale: true);
        layout.AddSeparateRow(Ui.Button("保存映射模板…", SaveTemplate), Ui.Button("载入映射模板…", LoadTemplate), null);
        layout.Add(_status);
        layout.Add(Ui.Confirm(s, "layers"));
        Content = layout;
    }

    void Edit(Action change)
    {
        change();
        Session.Save();
    }

    public override void Refresh()
    {
        var ex = Session.Extraction;
        if (ex == null)
        {
            _grid.DataStore = null;
            _status.Text = "点击「分析模型」读取图层。Click Analyze to read layers.";
            return;
        }
        _grid.DataStore = Session.Settings.Layers
            .Where(kv => ex.ObjectCountByLayer.ContainsKey(kv.Key))
            .Select(kv => new Row(kv.Key, ex.ObjectCountByLayer[kv.Key], kv.Value))
            .OrderBy(r => r.Path).ToList();
        var unclassified = Session.Result?.Issues.Count(i => i.Kind == PD.Core.Analysis.IssueKind.UnclassifiedLayer) ?? 0;
        _status.Text = $"{ex.ObjectCountByLayer.Count} 个有对象的图层；{unclassified} 个未匹配关键词。修改后点「分析模型」更新。";
    }

    void Refill()
    {
        Session.Settings.Layers.Clear();
        Analyze();
    }

    void SaveTemplate()
    {
        var dlg = new SaveFileDialog { Title = "图层映射模板", FileName = "layer_mapping.json" };
        dlg.Filters.Add(new FileFilter("JSON", ".json"));
        if (dlg.ShowDialog(this) != DialogResult.Ok) return;
        File.WriteAllText(dlg.FileName, JsonSerializer.Serialize(Session.Settings.Layers, PdSettings.Json));
    }

    /// <summary>Applies a saved mapping by full path, else by the layer's own name.</summary>
    void LoadTemplate()
    {
        var dlg = new OpenFileDialog { Title = "图层映射模板" };
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
