using System.Text.Json;
using Eto.Drawing;
using Eto.Forms;
using PD.Core.Classification;
using PD.Core.Settings;
using PD.Rhino.Adapter;

namespace PD.Rhino.UI.Tabs;

/// <summary>
/// ⑤ Colour scheme: company legend plus project-specific (custom) programs. Every program here is offered in
/// tab ① (layer program) and tab ④ (per-object program) and becomes a swatch / legend entry in the AI output.
/// </summary>
internal sealed class ColorsTab : TabBase
{
    readonly GridView _grid = Ui.Grid(340);
    readonly TextBox _newLabel = new() { PlaceholderText = "新 program 名称，如 Clean Room" };
    readonly Label _status = new();

    public ColorsTab(PdSession s) : base(s)
    {
        var swatch = new DrawableCell();
        swatch.Paint += (_, e) =>
        {
            if (e.Item is not ProgramSetting p) return;
            if (p.Fill != null && Color.TryParse(p.Fill, out var c)) e.Graphics.FillRectangle(c, e.ClipRectangle);
            else if (p.Stroke != null) e.Graphics.DrawRectangle(Colors.Black, e.ClipRectangle.X + 2, e.ClipRectangle.Y + 2, e.ClipRectangle.Width - 5, e.ClipRectangle.Height - 5);
        };
        _grid.Columns.Add(new GridColumn { HeaderText = "", DataCell = swatch, Width = 30 });
        _grid.Columns.Add(Ui.Text<ProgramSetting>("Key", p => p.Key, null, 110));
        _grid.Columns.Add(Ui.Text<ProgramSetting>("显示名 Label", p => p.Label, (p, v) => Edit(p, x => x.Label = v.Trim()), 160));
        _grid.Columns.Add(Ui.Text<ProgramSetting>("颜色 #RRGGBB", p => p.Fill ?? "", SetFill, 90));
        _grid.Columns.Add(Ui.Text<ProgramSetting>("图层别名 Aliases", p => string.Join(", ", p.Aliases), SetAliases, 140));
        _grid.Columns.Add(Ui.Text<ProgramSetting>("类型", p => p.IsCustom ? "自定义" : "公司", null, 50));
        _grid.Columns.Add(Ui.Text<ProgramSetting>("使用中 (图层 / 单独指定)", UsedBy, null, 200));

        var layout = Ui.Layout();
        layout.Add(_grid, yscale: true);
        layout.AddSeparateRow(Ui.Button("上移", () => Move(-1)), Ui.Button("下移", () => Move(1)), Ui.Button("取色…", PickColor), null);
        layout.AddSeparateRow(_newLabel, Ui.Button("添加自定义 program", AddCustom), Ui.Button("删除自定义", RemoveCustom));
        layout.AddSeparateRow(Ui.Button("为未指定 program 的图层创建", CreateFromLayers), null);
        layout.AddSeparateRow(Ui.Button("导入方案 JSON…", Import), Ui.Button("恢复公司图例", ResetLegend), null);
        layout.Add(new Label
        {
            Text = "自定义 program 会出现在 ① 图层和 ④ 名称的 Program 下拉中；别名用于自动预填同名图层。\n" +
                   "Key 一经创建不再改变（用于对象名 L03_KEY_01 和 AI 色板），显示名可以随时改。",
        });
        layout.Add(_status);
        layout.Add(Ui.Confirm(s, "colors"));
        Content = layout;
    }

    string UsedBy(ProgramSetting p)
    {
        var layers = Session.Settings.Layers.Where(kv => kv.Value.Use && kv.Value.Role == Roles.Program && kv.Value.ProgramKey == p.Key).Select(kv => kv.Key).ToList();
        var objects = Session.Settings.ObjectPrograms.Count(kv => kv.Value == p.Key);
        var text = string.Join(", ", layers);
        return objects > 0 ? $"{text}{(text.Length > 0 ? " + " : "")}{objects} 个体块" : text;
    }

    /// <summary>Grid rows may be legend copies; edits go to the stored scheme (materialised on first edit).</summary>
    ProgramSetting Own(ProgramSetting row) => Session.EditablePrograms().First(x => x.Key == row.Key);

    void Edit(ProgramSetting row, Action<ProgramSetting> change)
    {
        var wasLegend = Session.Settings.Programs == null;
        change(Own(row));
        if (wasLegend) Session.NotifyChanged();
        else { Session.Save(); _grid.Invalidate(); }
    }

    void SetFill(ProgramSetting p, string v)
    {
        var t = v.Trim();
        if (t.Length == 0) { Edit(p, x => x.Fill = null); return; }
        if (!t.StartsWith('#')) t = "#" + t;
        if (Color.TryParse(t, out _)) Edit(p, x => x.Fill = t.ToUpperInvariant());
    }

    void SetAliases(ProgramSetting row, string v)
    {
        Own(row).Aliases = v.Split(',', ';').Select(a => a.Trim()).Where(a => a.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        Session.ProgramsChanged();
    }

    public override void Refresh()
    {
        _grid.DataStore = Session.Programs.ToList();
        var custom = Session.Programs.Count(p => p.IsCustom);
        _status.Text = Session.Settings.Programs == null
            ? "公司图例 (styles/program_legend.json)。任何修改都会保存为本项目的方案。"
            : $"项目方案（保存在 3dm 中），{custom} 个自定义。顺序决定图例和饼图的顺序。";
    }

    ProgramSetting? Selected() => _grid.SelectedItem is ProgramSetting row ? Own(row) : null;

    void Move(int delta)
    {
        if (Selected() is not { } p) return;
        var list = Session.EditablePrograms().OrderBy(x => x.Order).ToList();
        var i = list.IndexOf(p);
        var j = Math.Clamp(i + delta, 0, list.Count - 1);
        (list[i], list[j]) = (list[j], list[i]);
        for (var k = 0; k < list.Count; k++) list[k].Order = k;
        Session.ProgramsChanged();
        _grid.SelectedRow = j;
    }

    void PickColor()
    {
        if (Selected() is not { } p) return;
        var c = System.Drawing.Color.Gray;
        if (p.Fill != null && Color.TryParse(p.Fill, out var ec)) c = System.Drawing.Color.FromArgb(ec.Rb, ec.Gb, ec.Bb);
        if (!global::Rhino.UI.Dialogs.ShowColorDialog(ref c)) return;
        p.Fill = $"#{c.R:X2}{c.G:X2}{c.B:X2}";
        Session.NotifyChanged();
    }

    void AddCustom()
    {
        var label = _newLabel.Text?.Trim();
        if (string.IsNullOrEmpty(label)) { MessageBox.Show(this, "请先输入 program 名称。", MessageBoxType.Information); return; }
        var p = ProgramCatalog.AddCustom(Session.EditablePrograms(), label);
        _newLabel.Text = "";
        Session.ProgramsChanged();
        _grid.SelectedRow = Session.Programs.FindIndex(x => x.Key == p.Key);
    }

    /// <summary>Program-role layers with no program get a custom program named after the layer, and are linked to it.</summary>
    void CreateFromLayers()
    {
        var targets = Session.Settings.Layers.Where(kv => kv.Value.Use && kv.Value.Role == Roles.Program && string.IsNullOrEmpty(kv.Value.ProgramKey)).ToList();
        if (targets.Count == 0)
        {
            MessageBox.Show(this, "没有「角色 = Program 但未指定 program」的图层。\n先在 ① 把图层角色设为 Program、Program 列留空。", MessageBoxType.Information);
            return;
        }
        var list = Session.EditablePrograms();
        foreach (var (path, layer) in targets)
        {
            var own = path.Split("::").Last();
            layer.ProgramKey = ProgramCatalog.AddCustom(list, own, aliases: new[] { own }).Key;
        }
        Session.ProgramsChanged();
        Analyze();
    }

    void RemoveCustom()
    {
        if (Selected() is not { } p) return;
        if (!p.IsCustom) { MessageBox.Show(this, "公司图例中的 program 不能删除（可以不使用）。", MessageBoxType.Information); return; }
        var used = UsedBy(p);
        if (used.Length > 0 && MessageBox.Show(this, $"「{p.Label}」正在使用：{used}\n删除后这些体块变为 Unassigned。继续？",
                MessageBoxButtons.YesNo, MessageBoxType.Question) != DialogResult.Yes) return;
        ProgramCatalog.RemoveCustom(Session.Settings, p.Key);
        Session.ProgramsChanged();
        if (used.Length > 0) Analyze();
    }

    /// <summary>Accepts the program_legend.json format (programs[]) or a saved list; unknown keys become custom programs.</summary>
    void Import()
    {
        var dlg = new OpenFileDialog { Title = "配色方案" };
        dlg.Filters.Add(new FileFilter("JSON", ".json"));
        if (dlg.ShowDialog(this) != DialogResult.Ok) return;
        using var doc = JsonDocument.Parse(File.ReadAllText(dlg.FileName));
        var defs = doc.RootElement.ValueKind == JsonValueKind.Object
            ? LayerClassifier.LoadPrograms(doc).Select(ProgramCatalog.FromDef).ToList()
            : JsonSerializer.Deserialize<List<ProgramSetting>>(doc.RootElement.GetRawText(), PdSettings.Json) ?? new();
        var legendKeys = LayerClassifier.LoadPrograms().Select(d => d.Key).ToHashSet();
        foreach (var p in defs) p.IsCustom = !legendKeys.Contains(p.Key);
        Session.Settings.Programs = defs;
        Session.ProgramsChanged();
        Analyze();
    }

    void ResetLegend()
    {
        var custom = Session.Programs.Where(p => p.IsCustom).ToList();
        if (custom.Count > 0 && MessageBox.Show(this, $"将删除 {custom.Count} 个自定义 program，使用它们的图层和体块变为 Unassigned。继续？",
                MessageBoxButtons.YesNo, MessageBoxType.Question) != DialogResult.Yes) return;
        foreach (var p in custom) ProgramCatalog.RemoveCustom(Session.Settings, p.Key);
        Session.Settings.Programs = null;
        Session.ProgramsChanged();
        if (custom.Count > 0) Analyze();
    }
}
