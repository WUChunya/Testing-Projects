using PD.Core.Classification;
using PD.Core.Drawing;
using PD.Core.Geometry;
using PD.Core.Model;
using PD.Core.Sections;

namespace PD.Core.Plans;

/// <summary>
/// Plan linework of the NC line roles (user rev 0.8): interior walls (NC.Interior, mid grey), furniture (NC.Furniture,
/// light grey, everything standing on the floor below the cut), screens (NC.Screen, cut as a line — never a block) and
/// the building envelope (NC.Massing, dashed cut outline). Paints come from style roles › plan.
/// </summary>
public static class PlanLinework
{
    public static readonly string[] Layers = { "PD_FURNITURE", "PD_INTERIOR", "PD_SCREEN", "PD_MASSING" };

    public static void Add(PD.Core.Drawing.Drawing d, ExportContext ctx, int artboard, PageMap map, int levelIndex, string prefix)
    {
        var r = ctx.Result;
        var lvl = r.Plans[levelIndex].Level;
        var z0 = lvl.Elevation;
        var z1 = levelIndex + 1 < r.Plans.Count ? r.Plans[levelIndex + 1].Level.Elevation : z0 + 15;
        var cutAt = z0 + ctx.Rules.Cleanup.PlanCutHeightFt;
        var weld = ctx.Rules.Cleanup.WeldToleranceFt;
        var n = 0;
        // Screens drawn solid (walls + floor, user rev 0.8.3) are not repeated as lines.
        var solid = PD.Core.Analysis.ScreenEnclosures.For(ctx).SelectMany(e => e.ObjectIds).ToHashSet();
        foreach (var o in r.Local.Objects.Where(o => !r.Stray.Contains(o.Id) && o.Role is Roles.Interior or Roles.Furniture or Roles.Massing or Roles.Screen && !solid.Contains(o.Id)))
        {
            var b = o.BBox;
            if (b.MaxZ < z0 - 0.5 || b.MinZ > z1 - 0.5) continue;
            var (layer, swatch) = o.Role switch
            {
                Roles.Interior => ("PD_INTERIOR", "NC_INTERIOR"),
                Roles.Furniture => ("PD_FURNITURE", "NC_FURNITURE"),
                Roles.Screen => ("PD_SCREEN", "NC_SCREEN"),
                _ => ("PD_MASSING", "NC_MASSING"),
            };
            var paint = PlanBackground.PaintOf(ctx.Style, o.Role, "plan");
            var lines = Lines(o, z0, z1, cutAt, weld, ctx.Rules.Cleanup.GapCloseToleranceFt);
            if (lines.Count == 0) continue;
            d.Layer(layer).Items.Add(new DItem
            {
                Kind = ItemKind.Path, Name = $"{prefix}{swatch[3..]}_{++n:000}", Artboard = artboard, Closed = false,
                Stroke = d.Swatch(swatch, paint.Stroke ?? "#8C8C8C"), StrokePt = paint.StrokePt ?? 0.5, Dash = paint.Dash,
                Paths = lines.Select(l => l.Select(q => map.P(q.X, q.Y)).ToArray()).ToList(),
            });
        }
    }

    /// <summary>
    /// Plan lines of one object on a level (feet): curves lying in the level as they are; solids cut at the plan
    /// height (open chains kept: a single wall surface cuts to a line); furniture and anything under the cut seen from above.
    /// </summary>
    public static List<List<(double X, double Y)>> Lines(SourceObject o, double z0, double z1, double cutAt, double weld, double gap)
    {
        var res = new List<List<(double X, double Y)>>();
        if (o.Mesh == null)
        {
            if (o.Points is { Length: >= 2 } pts && pts.All(p => p.Length < 3 || p[2] >= z0 - 0.5 && p[2] < z1 - 0.5))
                res.Add(pts.Select(p => (p[0], p[1])).ToList());
            return res;
        }
        var b = o.BBox;
        if (o.Role != Roles.Furniture && b.MinZ <= cutAt && b.MaxZ >= cutAt)
        {
            var open = new List<List<(double X, double Y)>>();
            var loops = MeshSlicer.Slice(o.Mesh, cutAt, weld, gap, false, out _, open);
            foreach (var l in loops) res.Add(l.Append(l[0]).ToList());
            res.AddRange(open);
            return res;
        }
        if (o.Role == Roles.Massing || b.MinZ > cutAt) return res;
        // Below the cut (furniture, low walls): the outline seen from above.
        var top = MeshProjection.TopDown(o.Mesh);
        for (var i = 0; i < top.NumGeometries; i++)
            if (top.GetGeometryN(i) is NetTopologySuite.Geometries.Polygon p)
                foreach (var ring in new[] { p.ExteriorRing }.Concat(p.InteriorRings))
                    res.Add(ring.Coordinates.Select(c => (c.X, c.Y)).ToList());
        return res;
    }
}
