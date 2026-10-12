using NetTopologySuite.Geometries;
using PD.Core.Analysis;
using PD.Core.Classification;
using PD.Core.Config;
using PD.Core.Drawing;
using PD.Core.Geometry;
using NtsGeometry = NetTopologySuite.Geometries.Geometry;

namespace PD.Core.Plans;

/// <summary>A background element seen from above, clipped to the drawing extent (feet).</summary>
public sealed record BackgroundItem(string Role, NtsGeometry Geometry, bool IsLine);

/// <summary>PRD 5.2 / A1 background: site (hardscape, roads, landscape, water, trees), other buildings, property lines.</summary>
public static class PlanBackground
{
    /// <summary>Drawing order (bottom → top) and the style.background key for each site role.</summary>
    static readonly (string Role, string Key)[] Site =
    {
        ("BG.Hardscape", "hardscape"), ("BG.Road", "road"), ("BG.Landscape", "landscape"), ("BG.Water", "water"), ("BG.Tree", "tree"),
    };

    public static List<BackgroundItem> Collect(AnalysisResult r, Envelope extent)
    {
        var clip = MeshSlicer.Factory.ToGeometry(extent);
        var items = new List<BackgroundItem>();
        foreach (var o in r.Local.Objects.Where(o => !r.Stray.Contains(o.Id)))
        {
            var isSite = Site.Any(s => s.Role == o.Role);
            if (!isSite && o.Role != Roles.Building && o.Role != "BG.Line") continue;
            var b = o.BBox;
            if (!extent.Intersects(new Envelope(b.MinX, b.MaxX, b.MinY, b.MaxY))) continue;
            if (o.Role == "BG.Line" || (o.Mesh == null && o.Points is { Length: >= 2 }))
            {
                if (o.Points is not { Length: >= 2 }) continue;
                var line = MeshSlicer.Factory.CreateLineString(o.Points.Select(p => new Coordinate(p[0], p[1])).ToArray());
                var cut = Safe(() => line.Intersection(clip));
                if (cut is { IsEmpty: false } && o.Role == "BG.Line") items.Add(new BackgroundItem(o.Role, cut, true));
                continue;
            }
            if (o.Mesh == null) continue;
            var g = Safe(() => MeshProjection.TopDown(o.Mesh).Intersection(clip));
            if (g is { IsEmpty: false } && g.Area > 0.5) items.Add(new BackgroundItem(o.Role, g, false));
        }
        int Rank(string role) => role == Roles.Building ? 10 : role == "BG.Line" ? 11 : Array.FindIndex(Site, s => s.Role == role);
        return items.OrderBy(i => Rank(i.Role)).ToList();
    }

    public static string LayerName(BackgroundItem i) => i.Role == Roles.Building ? "BG_CONTEXT" : "BG_SITE";

    /// <summary>(fill, stroke, strokePt, dash) for a background item; site paints come from style "background".</summary>
    public static (string? fill, string? stroke, double strokePt, double[]? dash) Paint(PD.Core.Drawing.Drawing d, Style s, BackgroundItem i)
    {
        if (i.Role == Roles.Building)
        {
            var c = s.Plan.ContextBuilding;
            return (d.Swatch("BG_CONTEXT", c.Fill), d.Swatch("BG_CONTEXT_LINE", c.Stroke), c.StrokePt ?? 0, c.Dash);
        }
        var key = i.Role == "BG.Line" ? "propertyLine" : Site.FirstOrDefault(x => x.Role == i.Role).Key ?? "";
        if (!s.Background.TryGetValue(key, out var p)) p = new Paint { Fill = "#E5E5E5" };
        var name = "BG_" + key.ToUpperInvariant();
        return (i.IsLine ? null : d.Swatch(name, p.Fill), d.Swatch(name + "_LINE", p.Stroke), p.StrokePt ?? s.Strokes.BackgroundPt, p.Dash);
    }

    static NtsGeometry? Safe(Func<NtsGeometry> op)
    {
        try { return op(); }
        catch (TopologyException) { return null; }
    }
}
