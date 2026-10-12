using Eto.Drawing;
using Eto.Forms;
using Rhino.Geometry;
using PD.Rhino.Adapter;

namespace PD.Rhino.UI;

/// <summary>Small Eto helpers so each tab stays short.</summary>
internal static class Ui
{
    public static GridView Grid(int height = 220, bool multi = false) => new() { ShowHeader = true, AllowMultipleSelection = multi, Height = height };

    public static GridColumn Text<T>(string header, Func<T, string> get, Action<T, string>? set = null, int width = 0)
    {
        var col = new GridColumn
        {
            HeaderText = header, Editable = set != null, Sortable = false,
            DataCell = new TextBoxCell { Binding = Binding.Delegate<T, string>(get, set == null ? null : (r, v) => set(r, v ?? "")) },
        };
        if (width > 0) col.Width = width;
        return col;
    }

    public static GridColumn Check<T>(string header, Func<T, bool> get, Action<T, bool>? set = null) => new()
    {
        HeaderText = header, Editable = set != null, Width = 44,
        DataCell = new CheckBoxCell { Binding = Binding.Delegate<T, bool?>(r => get(r), set == null ? null : (r, v) => set(r, v ?? false)) },
    };

    public static GridColumn Combo<T>(string header, IEnumerable<string> items, Func<T, string> get, Action<T, string> set, int width = 0)
    {
        var col = new GridColumn
        {
            HeaderText = header, Editable = true,
            DataCell = new ComboBoxCell
            {
                DataStore = items.Cast<object>().ToList(),
                Binding = Binding.Delegate<T, object>(r => get(r), (r, v) => set(r, v as string ?? (v as IListItem)?.Key ?? v?.ToString() ?? "")),
            },
        };
        if (width > 0) col.Width = width;
        return col;
    }

    public static Button Button(string text, Action click)
    {
        var b = new Button { Text = text };
        b.Click += (_, _) => click();
        return b;
    }

    /// <summary>The "Confirmed" box at the bottom of every tab.</summary>
    public static CheckBox Confirm(PdSession s, string tab)
    {
        var cb = new CheckBox { Text = "Confirmed", Checked = s.IsConfirmed(tab) };
        cb.CheckedChanged += (_, _) => { if (s.IsConfirmed(tab) != (cb.Checked == true)) s.SetConfirmed(tab, cb.Checked == true); };
        s.Changed += () => cb.Checked = s.IsConfirmed(tab);
        return cb;
    }

    public static DynamicLayout Layout() => new() { Padding = new Padding(6), Spacing = new Size(4, 4) };

    /// <summary>Selects the Rhino objects behind DTO ids and zooms to them.</summary>
    public static void SelectAndZoom(PdSession s, IEnumerable<string> dtoIds)
    {
        var doc = s.Doc;
        if (doc == null || s.Extraction == null) return;
        doc.Objects.UnselectAll();
        var box = BoundingBox.Empty;
        foreach (var guid in dtoIds.Select(id => s.Extraction.RhinoIds.GetValueOrDefault(id)).Where(g => g != Guid.Empty).Distinct())
        {
            var obj = doc.Objects.FindId(guid);
            if (obj == null) continue;
            doc.Objects.Select(guid, true, true);
            box.Union(obj.Geometry.GetBoundingBox(true));
        }
        if (box.IsValid)
        {
            box.Inflate(box.Diagonal.Length * 0.15 + doc.ModelAbsoluteTolerance);
            doc.Views.ActiveView?.ActiveViewport.ZoomBoundingBox(box);
        }
        doc.Views.Redraw();
    }

    public static string Ft(double feet) => PD.Core.Model.FeetInches.Format(feet);
}

/// <summary>
/// Ctrl / Shift multi-selection that behaves like Rhino's layer panel: changing a cell of a selected row changes every
/// selected row. Clicking a cell can collapse the selection to that one row before the edit arrives, so the selection
/// just before the click is remembered too. VERIFY-WIN: Eto WPF grid selection order around cell edits.
/// </summary>
internal sealed class MultiSelect<T> where T : class
{
    readonly GridView _grid;
    List<T> _current = new(), _previous = new();

    public MultiSelect(GridView grid)
    {
        _grid = grid;
        grid.SelectedItemsChanged += (_, _) =>
        {
            _previous = _current;
            _current = grid.SelectedItems.OfType<T>().ToList();
        };
    }

    /// <summary>Rows an edit on <paramref name="row"/> applies to: the (previous) multi-selection holding it, else the row.</summary>
    public List<T> Targets(T row)
    {
        if (_current.Count > 1 && _current.Contains(row)) return _current.ToList();
        if (_previous.Count > 1 && _previous.Contains(row)) return _previous.ToList();
        return new List<T> { row };
    }

    public List<T> Selected => _grid.SelectedItems.OfType<T>().ToList();

    /// <summary>Forget the selection when the rows are rebuilt (the old row objects are gone).</summary>
    public void Reset() => _current = _previous = new List<T>();
}

/// <summary>Base for the tabs: rebuilds its rows when the session changes.</summary>
internal abstract class TabBase : Panel
{
    protected PdSession Session { get; }

    protected TabBase(PdSession session)
    {
        Session = session;
        session.Changed += () => Application.Instance.AsyncInvoke(Refresh);
    }

    public abstract void Refresh();

    protected void Analyze()
    {
        if (!Session.Analyze())
            MessageBox.Show(this, $"Analysis failed:\n{Session.LastError}", MessageBoxType.Error);
    }
}
