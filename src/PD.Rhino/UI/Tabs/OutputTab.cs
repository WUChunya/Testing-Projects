using System.Diagnostics;
using Eto.Forms;
using Rhino;
using PD.Core.Drawing;
using PD.Core.Writers;
using PD.Rhino.Adapter;
using PD.Rhino.Output;

namespace PD.Rhino.UI.Tabs;

/// <summary>Tab 8 Output: save location, drawings, scale, artboard. Export is enabled once all tabs are confirmed.</summary>
internal sealed class OutputTab : TabBase
{
    public static readonly string[] TabKeys = { "layers", "cleanup", "levels", "names", "colors", "section", "elevation", "output", "check" };

    readonly TextBox _folder = new() { PlaceholderText = "(folder of the Rhino model)" };
    readonly Label _folderInfo = new() { TextColor = Eto.Drawing.Colors.Gray };
    readonly TextBox _scheme = new();
    /// <summary>Free text like Bluebeam (3/64" = 1'-0", 1" = 30', 1:250) with presets in the list.</summary>
    readonly ComboBox _scale = new() { AutoComplete = true };
    /// <summary>Program sections have their own scale (SITE_STYLE §4, default 1" = 16'-0").</summary>
    readonly ComboBox _sectionScale = new() { AutoComplete = true };
    readonly Label _scaleInfo = new();
    readonly CheckBox _png = new() { Text = "Also export a PNG per artboard" };
    readonly DropDown _artboard = new();
    readonly StackLayout _drawings = new() { Orientation = Orientation.Vertical, Spacing = 2 };
    readonly Button _export;
    readonly CheckBox _launch = new() { Text = "Generate the .ai files in Illustrator (runs in the background; nothing is opened)" };
    readonly CheckBox _axonView = new() { Text = "Axons: use the active viewport's view direction (else isometric from the south-west)" };
    /// <summary>PRD 5.5: Excel program table for the charts; empty = the model's own areas (GSF).</summary>
    readonly TextBox _excel = new() { PlaceholderText = "(no Excel: the charts use the model areas, GSF)" };
    readonly CheckBox _entourage = new() { Text = "Draw entourage (people) on an ENTOURAGE layer" };
    readonly Label _seed = new() { TextColor = Eto.Drawing.Colors.Gray };
    readonly Label _status = new();
    bool _loading;

    public OutputTab(PdSession s) : base(s)
    {
        var o = s.Rules.Output;
        _scale.DataStore = o.Scales;
        _sectionScale.DataStore = o.Scales;
        _artboard.DataStore = o.Artboards;
        foreach (var d in o.Drawings)
        {
            var cb = new CheckBox { Text = d, Tag = d };
            cb.CheckedChanged += (_, _) => { if (!_loading) Store(); };
            _drawings.Items.Add(cb);
        }
        _folder.LostFocus += (_, _) => { Store(); ShowFolder(); };
        _scheme.LostFocus += (_, _) => Store();
        _scale.TextChanged += (_, _) => { if (!_loading) { ShowScale(); Store(); } };
        _sectionScale.TextChanged += (_, _) => { if (!_loading) Store(); };
        _png.CheckedChanged += (_, _) => { if (!_loading) Store(); };
        _launch.CheckedChanged += (_, _) => { if (!_loading) Store(); };
        _axonView.CheckedChanged += (_, _) => { if (!_loading) Store(); };
        _excel.LostFocus += (_, _) => Store();
        _entourage.CheckedChanged += (_, _) => { if (!_loading) Store(); };
        _artboard.SelectedValueChanged += (_, _) => { if (!_loading) Store(); };
        _export = Ui.Button("Export", Export);

        var layout = Ui.Layout();
        layout.AddRow(new Label { Text = "Save in" }, _folder, Ui.Button("…", Browse));
        layout.AddRow(null, _folderInfo);
        layout.AddRow(new Label { Text = "Scheme" }, _scheme);
        layout.AddRow(new Label { Text = "Scale (type any)" }, _scale);
        layout.AddRow(new Label { Text = "Section scale" }, _sectionScale);
        layout.AddRow(null, _scaleInfo);
        layout.AddRow(new Label { Text = "Artboard" }, _artboard);
        layout.AddRow(null, new Label { Text = "Drawings are always at true scale; \"Fit content\" sizes the artboard to the drawing." });
        layout.AddRow(new Label { Text = "Drawings" }, _drawings);
        layout.Add(_launch);
        layout.Add(_png);
        layout.Add(_axonView);
        layout.AddRow(new Label { Text = "Program Excel" }, _excel, Ui.Button("…", BrowseExcel));
        layout.AddSeparateRow(null, Ui.Button("Save Excel template...", SaveTemplate), null);
        layout.Add(_entourage);
        layout.AddSeparateRow(null, _seed, Ui.Button("Re-randomize entourage", Reseed), null);
        layout.AddSeparateRow(_export, Ui.Button("Open last export folder", OpenFolder), null);
        layout.Add(_status);
        layout.Add(Ui.Confirm(s, "output"));
        layout.Add(null);
        Content = layout;
    }

    string Project => string.IsNullOrEmpty(Session.Doc?.Name) ? "Untitled" : Path.GetFileNameWithoutExtension(Session.Doc!.Name);

    /// <summary>A new sub-folder per export: save location (default: model folder) + rules "folderPattern" at this moment.</summary>
    string NewFolder(DateTime now) => OutputFolder.Create(OutputFolder.SaveIn(Session.Settings.Output.SaveIn, Session.Doc?.Path),
        Session.Rules.Output.FolderPattern, now, Project, Session.Settings.Output.Scheme);

    void ShowFolder() => _folderInfo.Text = "Each export creates: " + NewFolder(DateTime.Now);

    public override void Refresh()
    {
        _loading = true;
        var o = Session.Settings.Output;
        o.FillDefaults(Session.Rules.Output);
        _folder.Text = o.SaveIn;
        _launch.Checked = o.RunIllustrator ?? Session.Rules.Output.RunIllustrator;
        _scheme.Text = o.Scheme;
        _scale.Text = o.Scale;
        _sectionScale.Text = o.SectionScale;
        _png.Checked = o.ExportPng ?? Session.Rules.Output.ExportPng;
        _axonView.Checked = o.AxonFromViewport;
        _excel.Text = o.ProgramExcel;
        _entourage.Checked = o.Entourage ?? Session.Style.Entourage.Enabled;
        _seed.Text = $"Entourage seed {o.EntourageSeed ?? Session.Style.Entourage.Seed} (kept with the project, so people stay put)";
        ShowScale();
        ShowFolder();
        _artboard.SelectedValue = o.Artboard;
        foreach (var cb in _drawings.Items.Select(i => i.Control).OfType<CheckBox>())
            cb.Checked = o.Drawings!.Contains((string)cb.Tag);
        _loading = false;

        var missing = TabKeys.Select((k, i) => (k, i)).Where(x => !Session.IsConfirmed(x.k)).Select(x => (x.i + 1).ToString()).ToList();
        _export.Enabled = missing.Count == 0 && Session.Result != null;
        _status.Text = missing.Count == 0 ? "All tabs confirmed: ready to export." : $"Confirm tabs first: {string.Join(", ", missing)}";
    }

    void Store()
    {
        var o = Session.Settings.Output;
        o.SaveIn = _folder.Text.Trim();
        o.RunIllustrator = _launch.Checked == true;
        o.Scheme = _scheme.Text;
        if (ExportContext.ParseScale(_scale.Text) != null) o.Scale = _scale.Text.Trim();
        if (ExportContext.ParseScale(_sectionScale.Text) != null) o.SectionScale = _sectionScale.Text.Trim();
        o.ExportPng = _png.Checked == true;
        o.AxonFromViewport = _axonView.Checked == true;
        o.ProgramExcel = _excel.Text.Trim();
        o.Entourage = _entourage.Checked == true;
        o.Artboard = _artboard.SelectedValue as string ?? o.Artboard;
        o.Drawings = _drawings.Items.Select(i => i.Control).OfType<CheckBox>().Where(c => c.Checked == true).Select(c => (string)c.Tag).ToList();
        Session.Save();
    }

    void ShowScale()
    {
        var k = ExportContext.ParseScale(_scale.Text);
        _scaleInfo.Text = k == null ? $"Unknown scale. {Session.Rules.Output.ScaleHelp}" : $"1 ft = {k:0.###} pt (1\" = {72 / k:0.##} ft)";
        _scaleInfo.TextColor = k == null ? Eto.Drawing.Colors.Red : Eto.Drawing.Colors.Gray;
    }

    void BrowseExcel()
    {
        var dlg = new OpenFileDialog { Title = "Program table (Excel)" };
        dlg.Filters.Add(new FileFilter("Excel", ".xlsx"));
        if (dlg.ShowDialog(this) != DialogResult.Ok) return;
        _excel.Text = dlg.FileName;
        Store();
    }

    /// <summary>Writes the empty program table of PRD 5.5 for the user to fill in.</summary>
    void SaveTemplate()
    {
        var dlg = new SaveFileDialog { Title = "Program table template", FileName = "program_table.xlsx" };
        dlg.Filters.Add(new FileFilter("Excel", ".xlsx"));
        if (dlg.ShowDialog(this) != DialogResult.Ok) return;
        PD.Core.Charts.ProgramTable.WriteTemplate(dlg.FileName, Session.Programs);
        _excel.Text = dlg.FileName;
        Store();
        _status.Text = "Template written: fill it in, then export.";
    }

    /// <summary>New entourage seed (PRD 4): the people move, and the new seed is saved with the project.</summary>
    void Reseed()
    {
        Session.Settings.Output.EntourageSeed = new Random().Next(1, int.MaxValue);
        Session.Save();
        Refresh();
    }

    void Browse()
    {
        var dlg = new SelectFolderDialog { Title = "Save in", Directory = OutputFolder.SaveIn(_folder.Text, Session.Doc?.Path) };
        if (dlg.ShowDialog(this) != DialogResult.Ok) return;
        _folder.Text = dlg.Directory;
        Store();
        ShowFolder();
    }

    /// <summary>Re-analyses (so the export never uses stale results), writes the folder, then runs the JSX in Illustrator.</summary>
    void Export()
    {
        Store();
        // VERIFY-WIN: camera direction of the active viewport (points into the scene).
        var cam = Session.Doc?.Views.ActiveView?.ActiveViewport.CameraDirection;
        Session.Settings.Output.AxonViewDirection = Session.Settings.Output.AxonFromViewport && cam is { } c && c.IsValid && !c.IsZero
            ? new[] { c.X, c.Y, c.Z } : null;
        if (!Session.Analyze() || Session.Result == null)
        {
            MessageBox.Show(this, $"Analysis failed: {Session.LastError}", MessageBoxType.Error);
            return;
        }
        ExportResult res;
        try
        {
            res = Exporter.Write(new ExportContext
            {
                Result = Session.Result, Settings = Session.Settings, Programs = Session.Programs, Rules = Session.Rules, Style = Session.Style,
            }, NewFolder(DateTime.Now));
            Session.Settings.Output.LastFolder = res.Folder;
            Session.Save();
        }
        catch (Exception ex)
        {
            RhinoApp.WriteLine($"Program Diagram: export failed: {ex}");
            MessageBox.Show(this, $"Export failed: {ex.Message}", MessageBoxType.Error);
            return;
        }
        var notes = res.Notes.Count == 0 ? "" : "\n\n" + string.Join("\n", res.Notes);
        if (_launch.Checked != true || !IllustratorLauncher.IsInstalled())
        {
            MessageBox.Show(this, $"Written to:\n{res.Folder}\n\nTo make the .ai files: in Illustrator run File > Scripts > Other Script… and pick ProgramDiagram.jsx.{notes}", MessageBoxType.Information);
            return;
        }
        _status.Text = "Illustrator is generating the .ai files in the background…";
        RhinoApp.WriteLine("Program Diagram: running ProgramDiagram.jsx in Illustrator…");
        Task.Run(() =>
        {
            var error = IllustratorLauncher.Run(res.JsxPath, Session.Rules.Output.QuitIllustratorWhenDone);
            var log = Path.Combine(res.Folder, "illustrator_log.txt");
            var summary = File.Exists(log) ? File.ReadAllText(log) : "(no illustrator_log.txt)";
            Application.Instance.AsyncInvoke(() =>
            {
                _status.Text = error == null ? "Illustrator finished." : "Illustrator failed.";
                MessageBox.Show(this, error == null
                    ? $"Done:\n{summary}\nFolder: {res.Folder}{notes}"
                    : $"Illustrator could not run the script: {error}\nRun it manually: {res.JsxPath}", error == null ? MessageBoxType.Information : MessageBoxType.Warning);
            });
        });
    }

    void OpenFolder()
    {
        var dir = Session.Settings.Output.LastFolder;
        if (Directory.Exists(dir)) Process.Start(new ProcessStartInfo(dir) { UseShellExecute = true });
    }
}
