using System.Text.RegularExpressions;
using NetTopologySuite.Geometries;
using PD.Core.Analysis;
using PD.Core.Classification;
using PD.Core.Drawing;
using PD.Core.Geometry;
using PD.Core.Model;
using PD.Core.Sections;
using NtsGeometry = NetTopologySuite.Geometries.Geometry;

namespace PD.Core.Views;

/// <summary>Stacking rank (bottom → top layer): other buildings, our masses (solid base), slabs/other elements, glass, facade.</summary>
public enum ViewKind { Context = 0, Mass = 1, Element = 2, Glass = 3, Facade = 4 }

/// <param name="Layer">Source layer path (element/glass/facade items get one AI layer per source layer).</param>
/// <param name="Distance">Nearest distance in front of the plane (ft): near hides far.</param>
public sealed record ViewItem(string Id, ViewKind Kind, string Layer, NtsGeometry Geometry, double Distance, string Label);

/// <summary>
/// Make2D-like projected view (elevations, and what a section sees beyond its cut): every element is an opaque
/// projected silhouette. Items are painted far → near; an item on a higher layer is trimmed where a nearer item on a
/// lower layer covers it, so occlusion stays right even though each source layer keeps its own AI layer.
/// </summary>
public static class ProjectedView
{
    /// <summary>Classifies a live object for the view, or null when it is not drawn (site, base, curves…).</summary>
    public static ViewKind? Kind(SourceObject o, ISet<string> massIds, ISet<string> facadeIds, Regex glass)
    {
        if (o.Mesh == null) return null;
        if (o.Role == Roles.Building) return ViewKind.Context;
        if (massIds.Contains(o.Id)) return ViewKind.Mass;
        if (facadeIds.Contains(o.Id)) return glass.IsMatch(OwnName(o.LayerPath)) ? ViewKind.Glass : ViewKind.Facade;
        if (o.Role == Roles.Slab) return ViewKind.Element;
        return null;
    }

    public static ViewItem Item(SourceObject o, ViewKind kind, SectionPlane plane, double distance, double minZ) =>
        new(o.Id, kind, o.LayerPath, Clip(plane.Projection(o.Mesh!), plane, minZ), distance, kind == ViewKind.Context ? o.Name : "");

    /// <summary>
    /// Elevation: everything in front of the line within the depth; nothing is cut. Other buildings that straddle the
    /// line are at the viewer's position and are left out; our own building is always shown.
    /// </summary>
    public static List<ViewItem> Collect(AnalysisResult r, SectionPlane plane, double depthFt, Regex glass, double ground)
    {
        var (massIds, facadeIds) = Ids(r);
        var items = new List<ViewItem>();
        foreach (var o in r.Local.Objects.Where(o => !r.Stray.Contains(o.Id) && o.Mesh != null))
        {
            if (Kind(o, massIds, facadeIds, glass) is not { } kind || !plane.UOverlaps(o.Mesh!)) continue;
            var (wMin, wMax) = plane.WRange(o.Mesh!);
            if (wMax < 0 || wMin > depthFt || (kind == ViewKind.Context && wMin < 0)) continue;
            var item = Item(o, kind, plane, Math.Max(0, wMin), ground);
            if (!item.Geometry.IsEmpty) items.Add(item);
        }
        return items;
    }

    public static (HashSet<string> masses, HashSet<string> facades) Ids(AnalysisResult r) =>
        (r.Masses.Where(m => m.IsSolid).Select(m => m.Id).ToHashSet(), r.FacadeIds.ToHashSet());

    public static Regex GlassRegex(Config.ViewStyle v) => new(v.GlassPattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static string OwnName(string path) => path.Split("::").Last();

    public static string LayerName(ViewItem i) => i.Kind switch
    {
        ViewKind.Context => "PD_BEYOND",
        ViewKind.Mass => "VIEW_MASS",
        _ => "VIEW_" + Safe(OwnName(i.Layer)),
    };

    /// <summary>Creates the view layers in stacking order (call before adding layers that must sit above them).</summary>
    public static void AddLayers(PD.Core.Drawing.Drawing d, IEnumerable<ViewItem> items)
    {
        foreach (var name in items.OrderBy(i => (int)i.Kind).ThenBy(i => OwnName(i.Layer), StringComparer.Ordinal).Select(LayerName).Distinct())
            d.Layer(name);
    }

    public static void Draw(PD.Core.Drawing.Drawing d, ExportContext ctx, PageMap map, string prefix, IReadOnlyList<ViewItem> items)
    {
        var v = ctx.Style.View;
        AddLayers(d, items);
        var fills = LayerFills(d, v, items);

        // Near → far: trim each item by nearer items on lower layers.
        var ordered = items.Where(i => !i.Geometry.IsEmpty).OrderBy(i => i.Distance).ToList();
        var below = new NtsGeometry?[5];
        var visible = new List<(ViewItem item, NtsGeometry geom, int index)>();
        for (var n = 0; n < ordered.Count; n++)
        {
            var it = ordered[n];
            var g = it.Geometry;
            for (var rank = 0; rank < (int)it.Kind && !g.IsEmpty; rank++)
                if (below[rank] is { } occ && occ.EnvelopeInternal.Intersects(g.EnvelopeInternal))
                    g = Safe(() => g.Difference(occ), g);
            visible.Add((it, g, n));
            var k = (int)it.Kind;
            below[k] = below[k] == null ? it.Geometry : Safe(() => below[k]!.Union(it.Geometry), below[k]!);
        }

        // Paint far → near.
        foreach (var (it, g, n) in Enumerable.Reverse(visible))
        {
            if (g.IsEmpty || g.Area < 1e-4) continue;
            var name = $"{prefix}{it.Kind.ToString().ToUpperInvariant()}_{n:000}";
            DItem item = it.Kind switch
            {
                ViewKind.Context => map.Poly(name, g, d.Swatch("VIEW_CONTEXT", v.ContextFill), d.Gray(v.ContextStroke), v.ContextStrokePt),
                ViewKind.Mass => map.Poly(name, g, d.Swatch("VIEW_MASS", v.MassFill), d.Gray(v.MassStroke), v.MassStrokePt),
                _ => map.Poly(name, g, fills[LayerName(it)], d.Gray(v.EdgeStroke), v.EdgeStrokePt),
            };
            if (it.Kind == ViewKind.Context) item.Dash = v.ContextDash;
            d.Layer(LayerName(it)).Items.Add(item);
            if (it.Kind == ViewKind.Context && it.Label.Length > 0)
            {
                var e = g.EnvelopeInternal;
                d.Layer("PD_LABELS").Items.Add(PD.Core.Sections.SectionDrawing.Text(name + "_LBL", map.X((e.MinX + e.MaxX) / 2), map.Y(e.MaxY) - ctx.Pt(4), it.Label, "PD Note", "center"));
            }
        }
    }

    /// <summary>One global swatch per element layer: glass white, overrides by own name, else greys in name order.</summary>
    static Dictionary<string, string?> LayerFills(PD.Core.Drawing.Drawing d, Config.ViewStyle v, IEnumerable<ViewItem> items)
    {
        var res = new Dictionary<string, string?>();
        var grey = 0;
        foreach (var it in items.Where(i => i.Kind >= ViewKind.Element).GroupBy(LayerName).Select(gr => gr.First())
                     .OrderBy(i => (int)i.Kind).ThenBy(i => OwnName(i.Layer), StringComparer.Ordinal))
        {
            var layer = LayerName(it);
            var own = OwnName(it.Layer);
            var hex = v.LayerFills.TryGetValue(own, out var o) ? o
                : it.Kind == ViewKind.Glass ? v.GlassFill
                : v.LayerGrays.Count > 0 ? v.LayerGrays[grey++ % v.LayerGrays.Count] : "#C8C8C8";
            res[layer] = d.Swatch(layer, hex);
        }
        return res;
    }

    public static NtsGeometry Clip(NtsGeometry g, SectionPlane p, double minZ = -1e6) =>
        g.IsEmpty ? g : Safe(() => g.Intersection(PD.Core.Sections.SectionGeometry.Rect(0, p.Length, minZ, 1e6)), g);

    static NtsGeometry Safe(Func<NtsGeometry> op, NtsGeometry fallback)
    {
        try { return op(); }
        catch (TopologyException) { return fallback; }
    }

    static string Safe(string s) => new(s.Select(ch => char.IsLetterOrDigit(ch) ? char.ToUpperInvariant(ch) : '_').ToArray());
}
