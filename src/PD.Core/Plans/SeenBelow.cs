using NetTopologySuite.Geometries;
using PD.Core.Analysis;
using PD.Core.Drawing;
using PD.Core.Geometry;
using NtsGeometry = NetTopologySuite.Geometries.Geometry;

namespace PD.Core.Plans;

/// <summary>A lower floor's top seen from a plan's cut (user rev 0.8.5): a roof / floor plate, or flat glass.</summary>
/// <param name="ThroughOpening">Seen through an opening in the floors at grade (a basement under an atrium).</param>
public sealed record SeenSurface(int LevelIndex, bool IsGlass, NtsGeometry Area, bool ThroughOpening);

/// <summary>
/// What a plan sees below its own floor (user rev 0.8.5): every lower level that is not cut is drawn as a top view where
/// nothing nearer covers it — the podium roof from every tower floor, the floor plate one level down beyond the
/// building's edge, and through an opening in the ground floor the basement. Below grade only openings look down:
/// elsewhere the ground covers the basements.
/// </summary>
public static class SeenBelow
{
    public static List<SeenSurface> For(ExportContext ctx, int i)
    {
        var key = $"SeenBelow{i}";
        if (ctx.Cache.TryGetValue(key, out var c) && c is List<SeenSurface> s) return s;
        var found = Find(ctx, i);
        ctx.Cache[key] = found;
        return found;
    }

    static List<SeenSurface> Find(ExportContext ctx, int i)
    {
        var r = ctx.Result;
        var found = new List<SeenSurface>();
        if (i <= 0 || i >= r.Plans.Count) return found;
        var g = PlanDrawing.GroundIndex(ctx);
        var flats = ExposedSurfaces.FlatsFor(ctx);
        var minArea = ctx.Style.Exposed.MinAreaSf;

        NtsGeometry Floor(int j, bool glass)
        {
            var parts = flats.Where(f => f.LevelIndex == j && f.IsGlass == glass).Select(f => f.Top).ToList();
            if (!glass && !r.Plans[j].Union.IsEmpty) parts.Add(r.Plans[j].Union);
            return Union(parts);
        }

        var covered = Union(new[] { Floor(i, false), Floor(i, true) });
        for (var j = i - 1; j >= 0; j--)
        {
            if (covered.IsEmpty && j < g) break;
            // Under grade the ground hides everything but what the floors above leave open inside their outline.
            NtsGeometry? within = j < g ? Openings(covered) : null;
            if (within is { IsEmpty: true }) break;
            foreach (var glass in new[] { false, true })
            {
                var seen = Minus(Floor(j, glass), covered);
                if (within != null) seen = Clip(seen, within);
                seen = Clean(seen);
                if (!seen.IsEmpty && seen.Area >= minArea)
                {
                    found.Add(new SeenSurface(j, glass, seen, j < g));
                    covered = Union(new[] { covered, seen });
                }
            }
        }
        return found;
    }

    /// <summary>The holes of a floor outline (atriums, stair openings), as areas.</summary>
    public static NtsGeometry Openings(NtsGeometry floor)
    {
        var holes = new List<NtsGeometry>();
        for (var k = 0; k < floor.NumGeometries; k++)
            if (floor.GetGeometryN(k) is Polygon p)
                holes.AddRange(p.InteriorRings.Select(h => (NtsGeometry)MeshSlicer.Factory.CreatePolygon(h.Coordinates)));
        return Minus(Union(holes), floor);
    }

    static NtsGeometry Union(IEnumerable<NtsGeometry> parts)
    {
        var list = parts.Where(p => !p.IsEmpty).ToList();
        if (list.Count == 0) return MeshSlicer.Factory.CreatePolygon();
        return MeshSlicer.Polygonal(Safe(() => NetTopologySuite.Operation.Union.CascadedPolygonUnion.Union(list)) ?? list[0]);
    }

    static NtsGeometry Minus(NtsGeometry a, NtsGeometry b) =>
        a.IsEmpty || b.IsEmpty ? a : MeshSlicer.Polygonal(Safe(() => a.Difference(b)) ?? a);

    static NtsGeometry Clip(NtsGeometry a, NtsGeometry b) =>
        a.IsEmpty ? a : MeshSlicer.Polygonal(Safe(() => a.Intersection(b)) ?? MeshSlicer.Factory.CreatePolygon());

    static NtsGeometry Clean(NtsGeometry g) => g.IsEmpty ? g : MeshSlicer.Polygonal(Safe(() => g.Buffer(-0.25).Buffer(0.25)) ?? g);

    static NtsGeometry? Safe(Func<NtsGeometry> op)
    {
        try { return op(); }
        catch (Exception e) when (e is TopologyException or ArgumentException) { return null; }
    }
}
