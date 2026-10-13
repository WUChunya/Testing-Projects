using System.Text.Json;
using Eto.Drawing;
using Eto.Forms;
using PD.Core.Classification;
using PD.Core.Settings;
using PD.Rhino.Adapter;

namespace PD.Rhino.UI.Tabs;

/// <summary>
/// Tab 5 Colour scheme: company legend plus project-specific (custom) programs. Every program here is offered in
/// tab 1 (layer program) and tab 4 (per-object program) and becomes a swatch / legend entry in the AI output.
/// </summary>
internal sealed class ColorsTab : TabBase
{
    readonly GridView _grid = Ui.Grid(340);
    readonly TextBox _newLabel = new() { PlaceholderText = "New program name, e.g. Clean Room" };
    readonly Label _status = new();
    readonly TextBox _trees = new() { PlaceholderText = "(default)", Width = 80 };
    readonly TextBox _people = new() { PlaceholderText = "(default)", Width = 80 };

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
        _grid.Columns.Add(Ui.Text<ProgramSetting>("Label", p => p.Label, (p, v) => Edit(p, x => x.Label = v.Trim()), 160));
        _grid.Columns.Add(Ui.Text<ProgramSetting>("Colour #RRGGBB", p => p.Fill ?? "", SetFill, 90));
        _grid.Columns.Add(Ui.Text<ProgramSetting>("Layer aliases", p => string.Join(", ", p.Aliases), SetAliases, 140));
        _grid.Columns.Add(Ui.Text<ProgramSetting>("Type", p => p.IsCustom ? "Custom" : "Company", null, 60));
        _grid.Columns.Add(Ui.Text<ProgramSetting>("Used by (layers / objects)", UsedBy, null, 200));

        var layout = Ui.Layout();
        layout.Add(_grid, yscale: true);
        layout.AddSeparateRow(Ui.Button("Move up", () => Move(-1)), Ui.Button("Move down", () => Move(1)), Ui.Button("Pick colour...", PickColor), null);
        layout.AddSeparateRow(_newLabel, Ui.Button("Add custom program", AddCustom), Ui.Button("Delete custom", RemoveCustom));
        layout.AddSeparateRow(Ui.Button("Create for layers without a program", CreateFromLayers), null);
        layout.AddSeparateRow(Ui.Button("Import scheme JSON...", Import), Ui.Button("Reset to company legend", ResetLegend), null);
        // User rev 0.8.4: trees and people can have their own colour too (empty = the style's defaults).
        _trees.LostFocus += (_, _) => StoreEntourage();
        _people.LostFocus += (_, _) => StoreEntourage();
        layout.AddSeparateRow(new Label { Text = "Trees" }, _trees, Ui.Button("Pick...", () => Pick(_trees)), new Label { Text = "People" }, _people,
            Ui.Button("Pick...", () => Pick(_people)), Ui.Button("Default", () => { _trees.Text = ""; _people.Text = ""; StoreEntourage(); }), null);
        layout.Add(new Label
        {
            Text = "Custom programs appear in the Program lists of tab 1 Layers and tab 4 Names; aliases pre-fill layers with that name.\n" +
                   "A key never changes once created (object names L03_KEY_01 and AI swatches); the label can change any time.",
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
        return objects > 0 ? $"{text}{(text.Length > 0 ? " + " : "")}{objects} object(s)" : text;
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

    void Pick(TextBox box)
    {
        var c = System.Drawing.Color.Gray;
        if (Color.TryParse(box.Text ?? "", out var ec)) c = System.Drawing.Color.FromArgb(ec.Rb, ec.Gb, ec.Bb);
        if (!global::Rhino.UI.Dialogs.ShowColorDialog(ref c)) return;
        box.Text = $"#{c.R:X2}{c.G:X2}{c.B:X2}";
        StoreEntourage();
    }

    void StoreEntourage()
    {
        static string? Hex(string? t) => !string.IsNullOrWhiteSpace(t) && Color.TryParse(t.Trim(), out _) ? t.Trim().ToUpperInvariant() : null;
        Session.Settings.TreeColor = Hex(_trees.Text);
        Session.Settings.PeopleColor = Hex(_people.Text);
        Session.Save();
    }

    public override void Refresh()
    {
        _trees.Text = Session.Settings.TreeColor ?? "";
        _people.Text = Session.Settings.PeopleColor ?? "";
        _grid.DataStore = Session.Programs.ToList();
        var custom = Session.Programs.Count(p => p.IsCustom);
        _status.Text = Session.Settings.Programs == null
            ? "Company legend (styles/program_legend.json). Any change is saved as this project's scheme."
            : $"Project scheme (saved in the 3dm), {custom} custom. The order sets the legend and pie order.";
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
        if (string.IsNullOrEmpty(label)) { MessageBox.Show(this, "Type a program name first.", MessageBoxType.Information); return; }
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
            MessageBox.Show(this, "No layer has Role = Program without a program.\nOn tab 1 set the layer role to Program and leave the Program column empty.", MessageBoxType.Information);
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
        if (!p.IsCustom) { MessageBox.Show(this, "Company legend programs cannot be deleted (just leave them unused).", MessageBoxType.Information); return; }
        var used = UsedBy(p);
        if (used.Length > 0 && MessageBox.Show(this, $"\"{p.Label}\" is used by: {used}\nAfter deleting, these masses become Unassigned. Continue?",
                MessageBoxButtons.YesNo, MessageBoxType.Question) != DialogResult.Yes) return;
        ProgramCatalog.RemoveCustom(Session.Settings, p.Key);
        Session.ProgramsChanged();
        if (used.Length > 0) Analyze();
    }

    /// <summary>Accepts the program_legend.json format (programs[]) or a saved list; unknown keys become custom programs.</summary>
    void Import()
    {
        var dlg = new OpenFileDialog { Title = "Colour scheme" };
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
        if (custom.Count > 0 && MessageBox.Show(this, $"This deletes {custom.Count} custom program(s); layers and masses using them become Unassigned. Continue?",
                MessageBoxButtons.YesNo, MessageBoxType.Question) != DialogResult.Yes) return;
        foreach (var p in custom) ProgramCatalog.RemoveCustom(Session.Settings, p.Key);
        Session.Settings.Programs = null;
        Session.ProgramsChanged();
        if (custom.Count > 0) Analyze();
    }
}
