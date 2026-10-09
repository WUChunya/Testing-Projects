using System.Text.Json;
using Eto.Drawing;
using Eto.Forms;
using PD.Core.Classification;
using PD.Core.Settings;
using PD.Rhino.Adapter;

namespace PD.Rhino.UI.Tabs;

/// <summary>⑤ Colour scheme: company legend by default, or a project JSON; label, fill and order per program.</summary>
internal sealed class ColorsTab : TabBase
{
    readonly GridView _grid = Ui.Grid(380);
    readonly Label _status = new();

    public ColorsTab(PdSession s) : base(s)
    {
        var swatch = new DrawableCell();
        swatch.Paint += (_, e) =>
        {
            if (e.Item is ProgramSetting p && p.Fill != null && Color.TryParse(p.Fill, out var c))
                e.Graphics.FillRectangle(c, e.ClipRectangle);
        };
        _grid.Columns.Add(new GridColumn { HeaderText = "", DataCell = swatch, Width = 30 });
        _grid.Columns.Add(Ui.Text<ProgramSetting>("Key", p => p.Key, null, 110));
        _grid.Columns.Add(Ui.Text<ProgramSetting>("显示名 Label", p => p.Label, (p, v) => Edit(() => p.Label = v), 170));
        _grid.Columns.Add(Ui.Text<ProgramSetting>("颜色 #RRGGBB", p => p.Fill ?? "", (p, v) => Edit(() => p.Fill = string.IsNullOrWhiteSpace(v) ? null : v.Trim()), 90));
        _grid.Columns.Add(Ui.Text<ProgramSetting>("使用中", p => Used(p.Key) ? "●" : "", null, 50));

        var layout = Ui.Layout();
        layout.Add(_grid, yscale: true);
        layout.AddSeparateRow(Ui.Button("上移", () => Move(-1)), Ui.Button("下移", () => Move(1)), Ui.Button("取色…", PickColor), null);
        layout.AddSeparateRow(Ui.Button("导入方案 JSON…", Import), Ui.Button("恢复公司图例", ResetLegend), null);
        layout.Add(_status);
        layout.Add(Ui.Confirm(s, "colors"));
        Content = layout;
    }

    bool Used(string key) => Session.Result?.Masses.Any(m => m.ProgramKey == key) == true;

    /// <summary>The editable list; materialised from the legend on first edit.</summary>
    List<ProgramSetting> Programs() => Session.Settings.Programs ??= Session.Classifier.Programs
        .Select((p, i) => new ProgramSetting { Key = p.Key, Label = p.Label, Fill = p.Fill, Order = i }).ToList();

    void Edit(Action change)
    {
        change();
        Session.Save();
        _grid.Invalidate();
    }

    public override void Refresh()
    {
        _grid.DataStore = Programs().OrderBy(p => p.Order).ToList();
        _status.Text = Session.Settings.Programs == null ? "公司图例 (styles/program_legend.json)" : "项目方案（已保存在 3dm 中）。顺序决定图例和饼图的顺序。";
    }

    void Move(int delta)
    {
        if (_grid.SelectedItem is not ProgramSetting p) return;
        var list = Programs().OrderBy(x => x.Order).ToList();
        var i = list.IndexOf(p);
        var j = Math.Clamp(i + delta, 0, list.Count - 1);
        (list[i], list[j]) = (list[j], list[i]);
        for (var k = 0; k < list.Count; k++) list[k].Order = k;
        Session.NotifyChanged();
        _grid.SelectedRow = j;
    }

    void PickColor()
    {
        if (_grid.SelectedItem is not ProgramSetting p) return;
        var c = System.Drawing.Color.Gray;
        if (p.Fill != null && Color.TryParse(p.Fill, out var ec)) c = System.Drawing.Color.FromArgb(ec.Rb, ec.Gb, ec.Bb);
        if (!global::Rhino.UI.Dialogs.ShowColorDialog(ref c)) return;
        Edit(() => p.Fill = $"#{c.R:X2}{c.G:X2}{c.B:X2}");
    }

    /// <summary>Accepts the program_legend.json format (programs[]) or a plain list of ProgramSetting.</summary>
    void Import()
    {
        var dlg = new OpenFileDialog { Title = "配色方案" };
        dlg.Filters.Add(new FileFilter("JSON", ".json"));
        if (dlg.ShowDialog(this) != DialogResult.Ok) return;
        using var doc = JsonDocument.Parse(File.ReadAllText(dlg.FileName));
        var defs = doc.RootElement.ValueKind == JsonValueKind.Object
            ? LayerClassifier.LoadPrograms(doc).Select((p, i) => new ProgramSetting { Key = p.Key, Label = p.Label, Fill = p.Fill, Order = i }).ToList()
            : JsonSerializer.Deserialize<List<ProgramSetting>>(doc.RootElement.GetRawText(), PdSettings.Json) ?? new();
        Session.Settings.Programs = defs;
        Session.NotifyChanged();
    }

    void ResetLegend()
    {
        Session.Settings.Programs = null;
        Session.NotifyChanged();
    }
}
