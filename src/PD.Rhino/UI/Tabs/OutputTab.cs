using Eto.Forms;
using PD.Core.Analysis;
using PD.Rhino.Adapter;

namespace PD.Rhino.UI.Tabs;

/// <summary>⑦ Output: folder, drawings, scale, artboard. Export is enabled once all tabs are confirmed.</summary>
internal sealed class OutputTab : TabBase
{
    public static readonly string[] TabKeys = { "layers", "cleanup", "levels", "names", "colors", "section", "output", "check" };
    static readonly string[] TabNames = { "①", "②", "③", "④", "⑤", "⑥", "⑦", "⑧" };

    readonly TextBox _folder = new();
    readonly TextBox _scheme = new();
    readonly DropDown _scale = new();
    readonly DropDown _artboard = new();
    readonly StackLayout _drawings = new() { Orientation = Orientation.Vertical, Spacing = 2 };
    readonly Button _export;
    readonly Label _status = new();
    bool _loading;

    public OutputTab(PdSession s) : base(s)
    {
        var o = s.Rules.Output;
        _scale.DataStore = o.Scales;
        _artboard.DataStore = o.Artboards;
        foreach (var d in o.Drawings)
        {
            var cb = new CheckBox { Text = d, Tag = d };
            cb.CheckedChanged += (_, _) => { if (!_loading) Store(); };
            _drawings.Items.Add(cb);
        }
        _folder.LostFocus += (_, _) => Store();
        _scheme.LostFocus += (_, _) => Store();
        _scale.SelectedValueChanged += (_, _) => { if (!_loading) Store(); };
        _artboard.SelectedValueChanged += (_, _) => { if (!_loading) Store(); };
        _export = Ui.Button("导出 Export", Export);

        var layout = Ui.Layout();
        layout.AddRow(new Label { Text = "输出文件夹" }, _folder, Ui.Button("…", Browse));
        layout.AddRow(new Label { Text = "方案" }, _scheme);
        layout.AddRow(new Label { Text = "比例" }, _scale);
        layout.AddRow(new Label { Text = "画板" }, _artboard);
        layout.AddRow(new Label { Text = "输出哪些图" }, _drawings);
        layout.AddSeparateRow(_export, null);
        layout.Add(_status);
        layout.Add(Ui.Confirm(s, "output"));
        layout.Add(null);
        Content = layout;
    }

    string DefaultFolder()
    {
        var doc = Session.Doc;
        var dir = string.IsNullOrEmpty(doc?.Path) ? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) : Path.GetDirectoryName(doc!.Path)!;
        var project = string.IsNullOrEmpty(doc?.Name) ? "Untitled" : Path.GetFileNameWithoutExtension(doc!.Name);
        var rel = Session.Rules.Output.FolderPattern
            .Replace("{project}", project).Replace("{scheme}", Session.Settings.Output.Scheme)
            .Replace("{date}", DateTime.Now.ToString("yyyyMMdd"));
        return Path.Combine(dir, rel.Replace('/', Path.DirectorySeparatorChar));
    }

    public override void Refresh()
    {
        _loading = true;
        var o = Session.Settings.Output;
        o.FillDefaults(Session.Rules.Output, "");
        if (string.IsNullOrEmpty(o.Folder)) o.Folder = DefaultFolder();
        _folder.Text = o.Folder;
        _scheme.Text = o.Scheme;
        _scale.SelectedValue = o.Scale;
        _artboard.SelectedValue = o.Artboard;
        foreach (var cb in _drawings.Items.Select(i => i.Control).OfType<CheckBox>())
            cb.Checked = o.Drawings!.Contains((string)cb.Tag);
        _loading = false;

        var missing = TabKeys.Select((k, i) => (k, i)).Where(x => !Session.IsConfirmed(x.k)).Select(x => TabNames[x.i]).ToList();
        _export.Enabled = missing.Count == 0 && Session.Result != null;
        _status.Text = missing.Count == 0 ? "全部确认，可以导出。" : $"还需确认：{string.Join(" ", missing)}";
    }

    void Store()
    {
        var o = Session.Settings.Output;
        o.Folder = _folder.Text;
        o.Scheme = _scheme.Text;
        o.Scale = _scale.SelectedValue as string ?? o.Scale;
        o.Artboard = _artboard.SelectedValue as string ?? o.Artboard;
        o.Drawings = _drawings.Items.Select(i => i.Control).OfType<CheckBox>().Where(c => c.Checked == true).Select(c => (string)c.Tag).ToList();
        Session.Save();
    }

    void Browse()
    {
        var dlg = new SelectFolderDialog { Title = "输出文件夹", Directory = _folder.Text };
        if (dlg.ShowDialog(this) != DialogResult.Ok) return;
        _folder.Text = dlg.Directory;
        Store();
    }

    /// <summary>S2: writes the area table and settings; Illustrator drawings arrive in S3.</summary>
    void Export()
    {
        Store();
        var r = Session.Result;
        if (r == null) return;
        var dir = Session.Settings.Output.Folder;
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "areas.csv"), Reports.AreaCsv(r, Session.ProgramLabel));
        File.WriteAllText(Path.Combine(dir, "settings.json"), Session.Settings.ToJson());
        MessageBox.Show(this, $"已写出 areas.csv 和 settings.json 到：\n{dir}\n\nIllustrator 图纸将在 S3 加入。", MessageBoxType.Information);
    }
}
