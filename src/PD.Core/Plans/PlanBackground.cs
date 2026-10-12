using NetTopologySuite.Geometries;
using PD.Core.Analysis;
using PD.Core.Classification;
using PD.Core.Config;
using PD.Core.Drawing;
using PD.Core.Geometry;
using NtsGeometry = NetTopologySuite.Geometries.Geometry;

namespace PD.Core.Plans;

/// <summary>A background element seen from above, clipped to the drawing extent (feet). Z = top of the object.</summary>
public sealed record BackgroundItem(string Role, NtsGeometry Geometry, bool IsLine, string Name = "", double Z = 0, double Z0 = 0);

/// <summary>
/// Site and context seen from above (PRD 5.2 / A1, docs/SITE_STYLE.md §2): context ground (blocks, roads, landscape,
/// water), our own site (NC hardscape, green space, water), trees as circles, other buildings, property lines.
/// Paints come from style "roles".
/// </summary>
public static class PlanBackground
{
    /// <summary>Drawing order bottom → top.</summary>
    static readonly string[] Order =
    {
        Roles.Hardscape, Roles.Road, Roles.Landscape, Roles.Water, Roles.NcHardscape, Roles.Green, Roles.NcWater,
        Roles.Building, Roles.Tree, Roles.NcTree, Roles.Line,
    };

    public static List<BackgroundItem> Collect(AnalysisResult r, Envelope extent, double[]? treeDiameterFt = null)
    {
        var clip = MeshSlicer.Factory.ToGeometry(extent);
        var dia = treeDiameterFt is { Length: 2 } td ? td : new[] { 14.0, 22.0 };
        var items = new List<BackgroundItem>();
        foreach (var o in r.Local.Objects.Where(o => !r.Stray.Contains(o.Id) && Order.Contains(o.Role)))
        {
            var b = o.BBox;
            if (!extent.Intersects(new Envelope(b.MinX, b.MaxX, b.MinY, b.MaxY))) continue;
            if (o.Role is Roles.Tree or Roles.NcTree)
            {
                // Trees: a canopy circle at the object's centre, diameter from its size within the style range.
                var d = Math.Clamp(Math.Max(b.MaxX - b.MinX, b.MaxY - b.MinY), dia[0], dia[1]);
                var c = MeshSlicer.Factory.CreatePoint(new Coordinate((b.MinX + b.MaxX) / 2, (b.MinY + b.MaxY) / 2)).Buffer(d / 2, 8);
                if (extent.Intersects(c.EnvelopeInternal)) items.Add(new BackgroundItem(o.Role, c, false, o.Name, b.MaxZ, b.MinZ));
                continue;
            }
            if (o.Role == Roles.Line || (o.Mesh == null && o.Points is { Length: >= 2 }))
            {
                if (o.Points is not { Length: >= 2 }) continue;
                var line = MeshSlicer.Factory.CreateLineString(o.Points.Select(p => new Coordinate(p[0], p[1])).ToArray());
                var cut = MeshSlicer.Lineal(Safe(() => line.Intersection(clip)));
                if (!cut.IsEmpty && o.Role == Roles.Line) items.Add(new BackgroundItem(o.Role, cut, true, o.Name));
                continue;
            }
            if (o.Mesh == null) continue;
            var g = MeshSlicer.Polygonal(Safe(() => MeshProjection.TopDown(o.Mesh).Intersection(clip)));
            if (!g.IsEmpty && g.Area > 0.5) items.Add(new BackgroundItem(o.Role, g, false, o.Name, b.MaxZ, b.MinZ));
        }
        return items.OrderBy(i => Array.IndexOf(Order, i.Role)).ToList();
    }

    /// <summary>AI layer: other buildings, trees, the context ground, our own site.</summary>
    public static string LayerName(BackgroundItem i) => i.Role switch
    {
        Roles.Building => "BG_CONTEXT",
        Roles.Tree or Roles.NcTree => "BG_TREE",
        _ when Roles.IsNew(i.Role) => "BG_PROJECT_SITE",
        _ => "BG_SITE",
    };

    /// <summary>Paint of a role in a drawing family ("plan" | "axon" | "view"); unknown roles get a light grey.</summary>
    public static Paint PaintOf(Style s, string role, string family)
    {
        var rp = s.Roles.GetValueOrDefault(role);
        var p = family switch { "axon" => rp?.Axon ?? rp?.Plan, "view" => rp?.View, _ => rp?.Plan };
        return p ?? new Paint { Fill = "#E5E5E5" };
    }

    /// <summary>(fill, stroke, strokePt, dash, opacity) as global swatches named after the role ("BG_ROAD", "NC_GREEN_LINE").</summary>
    public static (string? fill, string? stroke, double strokePt, double[]? dash, double opacity) Paint(PD.Core.Drawing.Drawing d, Style s, BackgroundItem i,
        string family = "plan")
    {
        var p = PaintOf(s, i.Role, family);
        var name = SwatchName(i.Role);
        return (i.IsLine ? null : d.Swatch(name, p.Fill), d.Swatch(name + "_LINE", p.Stroke), p.StrokePt ?? s.Strokes.BackgroundPt, p.Dash, p.Opacity);
    }

    public static string SwatchName(string role) => role.Replace('.', '_').ToUpperInvariant();

    static NtsGeometry? Safe(Func<NtsGeometry> op)
    {
        try { return op(); }
        catch (Exception e) when (e is TopologyException or ArgumentException) { return null; }
    }
}
