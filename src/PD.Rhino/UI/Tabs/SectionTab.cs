using Eto.Forms;
using Rhino;
using Rhino.Geometry;
using PD.Core.Analysis;
using PD.Core.Settings;
using PD.Rhino.Adapter;

namespace PD.Rhino.UI.Tabs;

/// <summary>
/// Tab 6 Sections / tab 7 Elevations (kept apart): lines on SEC::* (or ELEV::*) layers, or picked; look direction and
/// visible depth. A section cuts the building; an elevation is a projected view where nothing is cut.
/// </summary>
internal sealed class SectionTab : TabBase
{
    readonly GridView _grid = Ui.Grid(220);
    readonly Label _status = new();
    readonly string _kind;
    readonly string _prefix;

    bool IsElevation => _kind == SectionSetting.ElevationKind;
    string Noun => IsElevation ? "elevation" : "section";

    public SectionTab(PdSession s, string kind) : base(s)
    {
        _kind = kind;
        _prefix = IsElevation ? "ELEV" : "SEC";
        _grid.Columns.Add(Ui.Text<SectionSetting>("Name", x => x.Name, (x, v) => Edit(() => x.Name = v), 60));
        if (IsElevation)
            _grid.Columns.Add(Ui.Check<SectionSetting>("Auto side", x => x.AutoSide, (x, v) => Edit(() => x.AutoSide = v)));
        _grid.Columns.Add(Ui.Check<SectionSetting>("Look left", x => x.LookLeft, (x, v) => Edit(() => x.LookLeft = v)));
        _grid.Columns.Add(Ui.Text<SectionSetting>("Visible depth", x => Ui.Ft(Depth(x)),
            (x, v) => { if (PD.Core.Model.FeetInches.TryParse(v, out var f)) Edit(() => x.DepthFt = f); }, 90));
        if (!IsElevation)
            _grid.Columns.Add(Ui.Text<SectionSetting>("Cuts masses", x => Crosses(x) switch { true => "yes", false => "NO - misses", _ => "?" }, null, 90));
        _grid.CellDoubleClick += (_, e) => { if (e.Item is SectionSetting x && x.ObjectId != null) SelectCurve(x.ObjectId); };

        var layout = Ui.Layout();
        layout.AddSeparateRow(Ui.Button($"Read {_prefix}:: layers", ReadLayers), Ui.Button($"Pick {Noun} line…", Pick), Ui.Button("Remove selected", Remove), null);
        layout.Add(_grid, yscale: true);
        layout.Add(_status);
        layout.Add(Ui.Confirm(s, IsElevation ? "elevation" : "section"));
        Content = layout;
    }

    double Depth(SectionSetting x) => x.DepthFt > 0 ? x.DepthFt : Session.Rules.Output.SectionDepthFt;

    void Edit(Action change)
    {
        change();
        Session.Save();
    }

    bool? Crosses(SectionSetting x)
    {
        var r = Session.Result;
        if (r == null || x.Line.Length < 4) return null;
        var (ax, ay, _) = r.Frame.ToLocal(x.Line[0], x.Line[1], 0);
        var (bx, by, _) = r.Frame.ToLocal(x.Line[2], x.Line[3], 0);
        return Reports.SectionCrossesMasses(r, ax, ay, bx, by);
    }

    public override void Refresh()
    {
        var rows = Session.Settings.Sections.Where(x => x.Kind == _kind).ToList();
        _grid.DataStore = rows;
        _status.Text = rows.Count == 0
            ? $"No {Noun} lines yet: draw a line on a layer such as {(IsElevation ? "ELEV::North" : "SEC::A")} and click Read, or click Pick."
            : $"{rows.Count} {Noun} line(s). Seen from the start point towards the end point; \"Look left\" sets the viewing side{(IsElevation ? " (elevations with \"Auto side\" look towards the building)" : "")}.";
    }

    /// <summary>Every curve on a layer whose path starts with SEC (e.g. SEC::A) or ELEV (ELEV::North) becomes a section / elevation named after the sub-layer.</summary>
    /// <remarks>This tab reads only its own prefix.</remarks>
    void ReadLayers()
    {
        var doc = Session.Doc;
        if (doc == null) return;
        var found = 0;
        foreach (var layer in doc.Layers.Where(l => !l.IsDeleted && l.FullPath.StartsWith(_prefix, StringComparison.OrdinalIgnoreCase)))
        foreach (var obj in doc.Objects.FindByLayer(layer) ?? Array.Empty<global::Rhino.DocObjects.RhinoObject>())
        {
            if (obj.Geometry is not Curve c || Session.Settings.Sections.Any(x => x.ObjectId == obj.Id.ToString())) continue;
            Session.Settings.Sections.Add(new SectionSetting
            {
                Name = !layer.Name.Equals(_prefix, StringComparison.OrdinalIgnoreCase) ? layer.Name
                    : IsElevation ? $"E{Session.Settings.Sections.Count(x => x.IsElevation) + 1}"
                    : $"{(char)('A' + Session.Settings.Sections.Count(x => !x.IsElevation) % 26)}",
                Kind = _kind,
                ObjectId = obj.Id.ToString(),
                Line = new[] { c.PointAtStart.X, c.PointAtStart.Y, c.PointAtEnd.X, c.PointAtEnd.Y },
            });
            found++;
        }
        Session.NotifyChanged();
        if (found == 0) MessageBox.Show(this, $"No new curves found on {_prefix}:: layers.", MessageBoxType.Information);
    }

    void Pick() => RhinoApp.RunScript(IsElevation ? "_ProgramDiagramPickElevation" : "_ProgramDiagramPickSection", false);

    void Remove()
    {
        if (_grid.SelectedItem is not SectionSetting x) return;
        Session.Settings.Sections.Remove(x);
        Session.NotifyChanged();
    }

    void SelectCurve(string id)
    {
        var doc = Session.Doc;
        if (doc == null || !Guid.TryParse(id, out var g)) return;
        doc.Objects.UnselectAll();
        doc.Objects.Select(g, true, true);
        doc.Views.Redraw();
    }
}
