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
        var minArea = ctx.Style.Exposed.MinAreaSf;

        var covered = Cover(ctx, i);
        for (var j = i - 1; j >= 0; j--)
        {
            if (covered.IsEmpty && j < g) break;
            // Under grade the ground hides everything but what the floors above leave open inside their outline.
            NtsGeometry? within = j < g ? Openings(covered) : null;
            if (within is { IsEmpty: true }) break;
            foreach (var glass in new[] { false, true })
            {
                // Slabs at or below grade are the ground: seen only through an opening (the basement floor).
                var seen = Minus(Floor(ctx, j, glass, groundSlabs: j < g), covered);
                if (within != null) seen = Clip(seen, within);
                // Our ramp landscape climbing over a lower roof hides it (the site underneath shows instead).
                seen = PlanBackground.UnderSiteRemoved(ctx, seen, j);
                seen = Clean(seen);
                if (!seen.IsEmpty && seen.Area >= minArea)
                    found.Add(new SeenSurface(j, glass, seen, j < g));
            }
            covered = Union(new[] { covered, Cover(ctx, j) });
        }
        return found;
    }

    /// <summary>
    /// Openings in level <paramref name="i"/>'s floor (its slabs and blocks together): a hole in the ground floor
    /// plate down to the basement (user rev 0.8.6), an atrium. Drawn open to below in its plan.
    /// </summary>
    public static NtsGeometry OpeningsAt(ExportContext ctx, int i)
    {
        var key = $"Openings{i}";
        if (ctx.Cache.TryGetValue(key, out var c) && c is NtsGeometry g0) return g0;
        var holes = i <= 0 || i >= ctx.Result.Plans.Count ? MeshSlicer.Factory.CreatePolygon() : Clean(Openings(Cover(ctx, i)));
        var min = ctx.Style.Exposed.MinAreaSf;
        var parts = Enumerable.Range(0, holes.NumGeometries).Select(holes.GetGeometryN).Where(h => h.Area >= min).ToList();
        var res = parts.Count == 0 ? MeshSlicer.Factory.CreatePolygon() : Union(parts);
        ctx.Cache[key] = res;
        return res;
    }

    /// <summary>What a level's floor is made of: its blocks, its slabs (every one, grade included) and its flat glass.</summary>
    static NtsGeometry Cover(ExportContext ctx, int j) =>
        Union(new[] { Floor(ctx, j, false, groundSlabs: true), Floor(ctx, j, true, groundSlabs: true) });

    /// <summary>A level's tops: blocks and slabs (plates, roofs) or its flat glass.</summary>
    static NtsGeometry Floor(ExportContext ctx, int j, bool glass, bool groundSlabs)
    {
        var r = ctx.Result;
        var parts = ExposedSurfaces.FlatsFor(ctx).Where(f => f.LevelIndex == j && f.IsGlass == glass).Select(f => f.Top).ToList();
        if (!glass)
        {
            if (!r.Plans[j].Union.IsEmpty) parts.Add(r.Plans[j].Union);
            if (groundSlabs) parts.AddRange(GroundSlabs(ctx).Where(x => x.Level == j).Select(x => x.Top));
        }
        return Union(parts);
    }

    /// <summary>Slabs at or below grade (not exposed surfaces), with their level: the ground floor plate, basement floors.</summary>
    static List<(int Level, NtsGeometry Top)> GroundSlabs(ExportContext ctx)
    {
        if (ctx.Cache.TryGetValue("GroundSlabs", out var c) && c is List<(int, NtsGeometry)> s) return s;
        var r = ctx.Result;
        var ground = ctx.Rules.Levels.GroundElevationFt;
        var found = new List<(int, NtsGeometry)>();
        foreach (var o in r.Local.Objects.Where(o => o.Role == PD.Core.Classification.Roles.Slab && o.Mesh != null && !r.Stray.Contains(o.Id)
                     && o.BBox.SizeZ <= 4 && o.BBox.MaxZ <= ground + 1))
        {
            var lv = r.Levels.Select((l, k) => (l, k)).Where(x => Math.Abs(x.l.Elevation - o.BBox.MaxZ) <= 3).OrderBy(x => Math.Abs(x.l.Elevation - o.BBox.MaxZ))
                .Select(x => (int?)x.k).FirstOrDefault();
            if (lv == null) continue;
            var top = MeshSlicer.Polygonal(Safe(() => MeshProjection.TopDown(o.Mesh!)));
            if (!top.IsEmpty) found.Add((lv.Value, top));
        }
        ctx.Cache["GroundSlabs"] = found;
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
