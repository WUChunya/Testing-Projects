using NetTopologySuite.Geometries;
using PD.Core.Classification;
using PD.Core.Geometry;
using NtsGeometry = NetTopologySuite.Geometries.Geometry;

namespace PD.Core.Analysis;

/// <summary>
/// The part of a floor slab (NC.Slab) or of horizontal glass (NC.Facade lying flat) with nothing standing on it
/// (user rev 0.8.4): a roof, terrace or canopy. Plans and axons show it — a slab in the roof style at its real
/// thickness, glass with a pale blue hatch — never labelled. Slabs at or below grade are the ground, not roofs.
/// </summary>
public sealed record ExposedSurface(string Id, bool IsGlass, NtsGeometry Area, double Z0, double Z1, int LevelIndex);

public static class ExposedSurfaces
{
    public static List<ExposedSurface> Find(AnalysisResult r, double groundFt, double minAreaSf = 20, double maxSlabFt = 4, double maxGlassFt = 2)
    {
        var found = new List<ExposedSurface>();
        if (r.Levels.Count == 0) return found;
        var facade = r.FacadeIds.ToHashSet();
        foreach (var o in r.Local.Objects.Where(o => o.Mesh != null && !r.Stray.Contains(o.Id)))
        {
            var glass = o.Role == Roles.Facade || facade.Contains(o.Id);
            if (!(o.Role == Roles.Slab || glass)) continue;
            var b = o.BBox;
            if (b.SizeZ > (glass ? maxGlassFt : maxSlabFt) || b.MaxZ <= groundFt + 1) continue;
            var top = MeshSlicer.Polygonal(Safe(() => MeshProjection.TopDown(o.Mesh!)));
            if (top.IsEmpty || top.Area < minAreaSf) continue;
            // The level the surface carries (its top at the level line), else the level below it (a canopy).
            var at = r.Levels.Select((l, i) => (l, i)).Where(x => Math.Abs(x.l.Elevation - b.MaxZ) <= 3).OrderBy(x => Math.Abs(x.l.Elevation - b.MaxZ))
                .Select(x => (int?)x.i).FirstOrDefault();
            var level = at ?? r.Levels.Select((l, i) => (l, i)).Where(x => x.l.Elevation <= b.MaxZ + 1).Select(x => x.i).DefaultIfEmpty(0).Last();
            // Nothing standing on it: what that level's blocks leave open (only a surface at the level line carries them).
            var area = top;
            if (at != null && level < r.Plans.Count && !r.Plans[level].Union.IsEmpty)
                area = MeshSlicer.Polygonal(Safe(() => top.Difference(r.Plans[level].Union)) ?? top);
            area = MeshSlicer.Polygonal(Safe(() => area.Buffer(-0.5).Buffer(0.5)) ?? area);
            if (area.IsEmpty || area.Area < minAreaSf) continue;
            found.Add(new ExposedSurface(o.Id, glass, area, b.MinZ, b.MaxZ, level));
        }
        return found;
    }

    public static List<ExposedSurface> For(PD.Core.Drawing.ExportContext ctx)
    {
        if (ctx.Cache.TryGetValue("ExposedSurfaces", out var c) && c is List<ExposedSurface> s) return s;
        var st = ctx.Style.Exposed;
        var found = st.Show ? Find(ctx.Result, ctx.Rules.Levels.GroundElevationFt, st.MinAreaSf) : new List<ExposedSurface>();
        ctx.Cache["ExposedSurfaces"] = found;
        return found;
    }

    static NtsGeometry? Safe(Func<NtsGeometry> op)
    {
        try { return op(); }
        catch (Exception e) when (e is TopologyException or ArgumentException) { return null; }
    }
}
