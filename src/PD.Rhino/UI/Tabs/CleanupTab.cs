using Eto.Forms;
using PD.Core.Cleanup;
using PD.Rhino.Adapter;

namespace PD.Rhino.UI.Tabs;

/// <summary>② Geometry cleanup (PRD 3.5): mass status, span rule, single surfaces, stray objects.</summary>
internal sealed class CleanupTab : TabBase
{
    sealed record MassRow(Mass M, SpanDecision? Span, string Levels);
    sealed record StrayRow(string Id, string Layer, string Type);

    static readonly string[] SpanModes = { "Single", "KeepTall", "Split" };
    static readonly string[] Choices = { "Ask", "Ignore", "Facade", "Extrude" };

    readonly GridView _masses = Ui.Grid(260);
    readonly GridView _singles = Ui.Grid(90);
    readonly GridView _stray = Ui.Grid(80);
    readonly Label _summary = new();

    public CleanupTab(PdSession s) : base(s)
    {
        _masses.Columns.Add(Ui.Text<MassRow>("图层", r => r.M.Source.LayerPath, null, 90));
        _masses.Columns.Add(Ui.Text<MassRow>("状态 Status", r => Status(r.M.Status), null, 110));
        _masses.Columns.Add(Ui.Text<MassRow>("体积 CF", r => r.M.Volume.ToString("N0"), null, 80));
        _masses.Columns.Add(Ui.Text<MassRow>("楼层", r => r.Levels, null, 70));
        _masses.Columns.Add(Ui.Combo<MassRow>("跨层 Span", SpanModes, r => r.Span?.Mode.ToString() ?? "Single", SetSpan, 90));
        _masses.CellDoubleClick += (_, e) => { if (e.Item is MassRow r) Ui.SelectAndZoom(Session, new[] { r.M.Id }); };

        _singles.Columns.Add(Ui.Text<Mass>("单张曲面 Single surface", m => m.Source.LayerPath, null, 160));
        _singles.Columns.Add(Ui.Combo<Mass>("处理方式", Choices, m => s.Settings.SingleSurfaces.GetValueOrDefault(m.Id).ToString(),
            (m, v) => { s.Settings.SingleSurfaces[m.Id] = Enum.Parse<SingleSurfaceChoice>(v); s.Save(); }, 100));
        _singles.CellDoubleClick += (_, e) => { if (e.Item is Mass m) Ui.SelectAndZoom(Session, new[] { m.Id }); };

        _stray.Columns.Add(Ui.Check<StrayRow>("保留", r => s.Settings.KeepStray.Contains(r.Id), (r, v) =>
        {
            s.Settings.KeepStray.Remove(r.Id);
            if (v) s.Settings.KeepStray.Add(r.Id);
            s.Save();
        }));
        _stray.Columns.Add(Ui.Text<StrayRow>("游离对象 Stray", r => $"{r.Layer} ({r.Type})", null, 200));
        _stray.CellDoubleClick += (_, e) => { if (e.Item is StrayRow r) Ui.SelectAndZoom(Session, new[] { r.Id }); };

        var layout = Ui.Layout();
        layout.AddSeparateRow(Ui.Button("重新分析 Re-analyze", Analyze), null);
        layout.Add(_summary);
        layout.Add(new Label { Text = "体块（双击定位；重叠规则：体积小的优先）" });
        layout.Add(_masses, yscale: true);
        layout.Add(new Label { Text = "program 图层上的单张曲面：忽略 / 当作立面 / 挤出（改完点重新分析）" });
        layout.Add(_singles);
        layout.Add(new Label { Text = "远离建筑主体的对象（默认排除）" });
        layout.Add(_stray);
        layout.Add(Ui.Confirm(s, "cleanup"));
        Content = layout;
    }

    void SetSpan(MassRow r, string v)
    {
        if (r.Span == null || r.Span.LevelIndices.Length < 2) return;
        Session.Settings.SpanModes[r.M.Id] = Enum.Parse<SpanMode>(v);
        Application.Instance.AsyncInvoke(Analyze);
    }

    static string Status(MassStatus s) => s switch
    {
        MassStatus.Closed => "封闭 Closed",
        MassStatus.Capped => "已封口 Capped",
        MassStatus.SectionClosed => "按剖切闭合 (请检查)",
        MassStatus.SingleSurface => "单张曲面",
        _ => "无法封口 Open",
    };

    public override void Refresh()
    {
        var r = Session.Result;
        if (r == null) { _summary.Text = "尚未分析。"; return; }
        var spans = r.Spans.ToDictionary(x => x.MassId);
        string LevelText(SpanDecision? d) => d == null ? "—"
            : d.LevelIndices.Length == 1 ? r.Levels[d.LevelIndices[0]].Name
            : $"{r.Levels[d.LevelIndices[0]].Name}–{r.Levels[d.LevelIndices[^1]].Name}";
        _masses.DataStore = r.Masses.Select(m => new MassRow(m, spans.GetValueOrDefault(m.Id), LevelText(spans.GetValueOrDefault(m.Id))))
            .OrderBy(x => x.M.Source.LayerPath).ThenBy(x => x.M.Source.BBox.MinZ).ToList();
        _singles.DataStore = r.Masses.Where(m => m.Status == MassStatus.SingleSurface).ToList();
        _stray.DataStore = r.Local.Objects.Where(o => r.Stray.Contains(o.Id) || Session.Settings.KeepStray.Contains(o.Id))
            .Select(o => new StrayRow(o.Id, o.LayerPath, o.GeometryType)).ToList();
        var c = r.Masses.GroupBy(m => m.Status).ToDictionary(g => g.Key, g => g.Count());
        _summary.Text = $"体块 {r.Masses.Count}：封闭 {c.GetValueOrDefault(MassStatus.Closed)}，封口 {c.GetValueOrDefault(MassStatus.Capped)}，" +
            $"按剖切闭合 {c.GetValueOrDefault(MassStatus.SectionClosed)}，单张曲面 {c.GetValueOrDefault(MassStatus.SingleSurface)}，" +
            $"无法封口 {c.GetValueOrDefault(MassStatus.Open)}；重叠 {r.Overlaps.Count} 处；游离 {r.Stray.Count}";
    }
}
