using Eto.Forms;
using Rhino;
using Rhino.Geometry;
using PD.Core.Analysis;
using PD.Core.Settings;
using PD.Rhino.Adapter;

namespace PD.Rhino.UI.Tabs;

/// <summary>⑥ Section cuts: curves on SEC::* layers, or picked; look direction and visible depth.</summary>
internal sealed class SectionTab : TabBase
{
    readonly GridView _grid = Ui.Grid(220);
    readonly Label _status = new();

    public SectionTab(PdSession s) : base(s)
    {
        _grid.Columns.Add(Ui.Text<SectionSetting>("名称", x => x.Name, (x, v) => Edit(() => x.Name = v), 60));
        _grid.Columns.Add(Ui.Check<SectionSetting>("看向左侧", x => x.LookLeft, (x, v) => Edit(() => x.LookLeft = v)));
        _grid.Columns.Add(Ui.Text<SectionSetting>("可见进深", x => Ui.Ft(Depth(x)),
            (x, v) => { if (PD.Core.Model.FeetInches.TryParse(v, out var f)) Edit(() => x.DepthFt = f); }, 80));
        _grid.Columns.Add(Ui.Text<SectionSetting>("穿过体块", x => Crosses(x) switch { true => "✓", false => "✗ 未穿过", _ => "?" }, null, 80));
        _grid.CellDoubleClick += (_, e) => { if (e.Item is SectionSetting x && x.ObjectId != null) SelectCurve(x.ObjectId); };

        var layout = Ui.Layout();
        layout.AddSeparateRow(Ui.Button("读取 SEC:: 图层", ReadLayers), Ui.Button("拾取剖切线…", Pick), Ui.Button("删除选中", Remove), null);
        layout.Add(_grid, yscale: true);
        layout.Add(_status);
        layout.Add(Ui.Confirm(s, "section"));
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
        _grid.DataStore = Session.Settings.Sections.ToList();
        _status.Text = Session.Settings.Sections.Count == 0
            ? "没有剖切线：把直线放在 SEC::A 等图层后点「读取」，或点「拾取」。"
            : $"{Session.Settings.Sections.Count} 条剖切线。从起点看向终点，「看向左侧」决定投影方向。";
    }

    /// <summary>Every curve on a layer whose path starts with SEC (e.g. SEC::A) becomes a section named after the sub-layer.</summary>
    void ReadLayers()
    {
        var doc = Session.Doc;
        if (doc == null) return;
        var found = 0;
        foreach (var layer in doc.Layers.Where(l => !l.IsDeleted && l.FullPath.StartsWith("SEC", StringComparison.OrdinalIgnoreCase)))
        foreach (var obj in doc.Objects.FindByLayer(layer) ?? Array.Empty<global::Rhino.DocObjects.RhinoObject>())
        {
            if (obj.Geometry is not Curve c || Session.Settings.Sections.Any(x => x.ObjectId == obj.Id.ToString())) continue;
            Session.Settings.Sections.Add(new SectionSetting
            {
                Name = layer.Name.Equals("SEC", StringComparison.OrdinalIgnoreCase) ? $"{(char)('A' + Session.Settings.Sections.Count % 26)}" : layer.Name,
                ObjectId = obj.Id.ToString(),
                Line = new[] { c.PointAtStart.X, c.PointAtStart.Y, c.PointAtEnd.X, c.PointAtEnd.Y },
            });
            found++;
        }
        Session.NotifyChanged();
        if (found == 0) MessageBox.Show(this, "没有在 SEC:: 图层上找到新的曲线。", MessageBoxType.Information);
    }

    void Pick() => RhinoApp.RunScript("_ProgramDiagramPickSection", false);

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
