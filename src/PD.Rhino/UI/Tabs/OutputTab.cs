using System.Diagnostics;
using Eto.Forms;
using Rhino;
using PD.Core.Drawing;
using PD.Core.Writers;
using PD.Rhino.Adapter;
using PD.Rhino.Output;

namespace PD.Rhino.UI.Tabs;

/// <summary>⑦ Output: folder, drawings, scale, artboard. Export is enabled once all tabs are confirmed.</summary>
internal sealed class OutputTab : TabBase
{
    public static readonly string[] TabKeys = { "layers", "cleanup", "levels", "names", "colors", "section", "output", "check" };
    static readonly string[] TabNames = { "①", "②", "③", "④", "⑤", "⑥", "⑦", "⑧" };

    readonly TextBox _folder = new();
    readonly TextBox _scheme = new();
    /// <summary>Free text like Bluebeam (3/64" = 1'-0", 1" = 30', 1:250) with presets in the list.</summary>
    readonly ComboBox _scale = new() { AutoComplete = true };
    readonly Label _scaleInfo = new();
    readonly CheckBox _png = new() { Text = "同时导出 PNG（每个画板一张）" };
    readonly DropDown _artboard = new();
    readonly StackLayout _drawings = new() { Orientation = Orientation.Vertical, Spacing = 2 };
    readonly Button _export;
    readonly CheckBox _launch = new() { Text = "导出后自动在 Illustrator 中生成 .ai", Checked = true };
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
        _scale.TextChanged += (_, _) => { if (!_loading) { ShowScale(); Store(); } };
        _png.CheckedChanged += (_, _) => { if (!_loading) Store(); };
        _artboard.SelectedValueChanged += (_, _) => { if (!_loading) Store(); };
        _export = Ui.Button("导出 Export", Export);

        var layout = Ui.Layout();
        layout.AddRow(new Label { Text = "输出文件夹" }, _folder, Ui.Button("…", Browse));
        layout.AddRow(new Label { Text = "方案" }, _scheme);
        layout.AddRow(new Label { Text = "比例（可直接输入）" }, _scale);
        layout.AddRow(null, _scaleInfo);
        layout.AddRow(new Label { Text = "画板" }, _artboard);
        layout.AddRow(null, new Label { Text = "图纸始终按真实比例；「Fit content」时画板随图面大小自动调整。" });
        layout.AddRow(new Label { Text = "输出哪些图" }, _drawings);
        layout.Add(_launch);
        layout.Add(_png);
        layout.AddSeparateRow(_export, Ui.Button("打开文件夹", OpenFolder), null);
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
        _scale.Text = o.Scale;
        _png.Checked = o.ExportPng ?? Session.Rules.Output.ExportPng;
        ShowScale();
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
        if (ExportContext.ParseScale(_scale.Text) != null) o.Scale = _scale.Text.Trim();
        o.ExportPng = _png.Checked == true;
        o.Artboard = _artboard.SelectedValue as string ?? o.Artboard;
        o.Drawings = _drawings.Items.Select(i => i.Control).OfType<CheckBox>().Where(c => c.Checked == true).Select(c => (string)c.Tag).ToList();
        Session.Save();
    }

    void ShowScale()
    {
        var k = ExportContext.ParseScale(_scale.Text);
        _scaleInfo.Text = k == null ? $"无法识别的比例。{Session.Rules.Output.ScaleHelp}" : $"1 ft = {k:0.###} pt（1\" = {72 / k:0.##} ft）";
        _scaleInfo.TextColor = k == null ? Eto.Drawing.Colors.Red : Eto.Drawing.Colors.Gray;
    }

    void Browse()
    {
        var dlg = new SelectFolderDialog { Title = "输出文件夹", Directory = _folder.Text };
        if (dlg.ShowDialog(this) != DialogResult.Ok) return;
        _folder.Text = dlg.Directory;
        Store();
    }

    /// <summary>Re-analyses (so the export never uses stale results), writes the folder, then runs the JSX in Illustrator.</summary>
    void Export()
    {
        Store();
        if (!Session.Analyze() || Session.Result == null)
        {
            MessageBox.Show(this, $"分析失败：{Session.LastError}", MessageBoxType.Error);
            return;
        }
        ExportResult res;
        try
        {
            res = Exporter.Write(new ExportContext
            {
                Result = Session.Result, Settings = Session.Settings, Programs = Session.Programs, Rules = Session.Rules,
            }, Session.Settings.Output.Folder);
        }
        catch (Exception ex)
        {
            RhinoApp.WriteLine($"Program Diagram: export failed: {ex}");
            MessageBox.Show(this, $"导出失败：{ex.Message}", MessageBoxType.Error);
            return;
        }
        var notes = res.Notes.Count == 0 ? "" : "\n\n" + string.Join("\n", res.Notes);
        if (_launch.Checked != true || !IllustratorLauncher.IsInstalled())
        {
            MessageBox.Show(this, $"已写出到：\n{res.Folder}\n\n在 Illustrator 中运行 文件 > 脚本 > 其他脚本… 选择 ProgramDiagram.jsx 生成 .ai。{notes}", MessageBoxType.Information);
            return;
        }
        _status.Text = "Illustrator 正在生成 .ai …";
        RhinoApp.WriteLine("Program Diagram: running ProgramDiagram.jsx in Illustrator…");
        Task.Run(() =>
        {
            var error = IllustratorLauncher.Run(res.JsxPath);
            var log = Path.Combine(res.Folder, "illustrator_log.txt");
            var summary = File.Exists(log) ? File.ReadAllText(log) : "(no illustrator_log.txt)";
            Application.Instance.AsyncInvoke(() =>
            {
                _status.Text = error == null ? "Illustrator 完成。" : "Illustrator 出错。";
                MessageBox.Show(this, error == null
                    ? $"完成：\n{summary}\n文件夹：{res.Folder}{notes}"
                    : $"Illustrator 未能运行脚本：{error}\n可手动运行 {res.JsxPath}", error == null ? MessageBoxType.Information : MessageBoxType.Warning);
            });
        });
    }

    void OpenFolder()
    {
        var dir = Session.Settings.Output.Folder;
        if (Directory.Exists(dir)) Process.Start(new ProcessStartInfo(dir) { UseShellExecute = true });
    }
}
