using NetTopologySuite.Geometries;
using PD.Core.Classification;
using PD.Core.Geometry;
using NtsGeometry = NetTopologySuite.Geometries.Geometry;

namespace PD.Core.Analysis;

/// <summary>
/// The part of a floor slab (NC.Slab) or of horizontal glass (NC.Facade lying flat) with nothing standing on it
/// (user rev 0.8.4): a roof, terrace or canopy. Plans and axons show it — a slab in the roof style at its real
/// thickness, glass with a pale blue hatch — never labelled. Slabs at or below grade are the ground, not roofs.
/// <see cref="Area"/> is what its own level leaves open (plans, exploded plates); <see cref="Sky"/> what no block of a
/// higher level stands over (user rev 0.8.5: the assembled axons — a slab under the floor above is no roof).
/// </summary>
public sealed record ExposedSurface(string Id, bool IsGlass, NtsGeometry Area, double Z0, double Z1, int LevelIndex)
{
    public NtsGeometry Sky { get; init; } = Area;
}

/// <summary>A flat slab or flat glass above grade: its top-down shape and the level it belongs to.</summary>
public sealed record FlatSurface(string Id, bool IsGlass, NtsGeometry Top, double Z0, double Z1, int LevelIndex, bool AtLevel);

public static class ExposedSurfaces
{
    public static List<FlatSurface> Flats(AnalysisResult r, double groundFt, double minAreaSf = 20, double maxSlabFt = 4, double maxGlassFt = 2)
    {
        var found = new List<FlatSurface>();
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
            found.Add(new FlatSurface(o.Id, glass, top, b.MinZ, b.MaxZ, level, at != null));
        }
        return found;
    }

    public static List<ExposedSurface> Find(AnalysisResult r, double groundFt, double minAreaSf = 20, double maxSlabFt = 4, double maxGlassFt = 2) =>
        Find(r, Flats(r, groundFt, minAreaSf, maxSlabFt, maxGlassFt), minAreaSf);

    static List<ExposedSurface> Find(AnalysisResult r, List<FlatSurface> flats, double minAreaSf)
    {
        var found = new List<ExposedSurface>();
        foreach (var f in flats)
        {
            // Nothing standing on it: what that level's blocks leave open (only a surface at the level line carries them).
            var area = f.Top;
            if (f.AtLevel && f.LevelIndex < r.Plans.Count && !r.Plans[f.LevelIndex].Union.IsEmpty)
                area = MeshSlicer.Polygonal(Safe(() => f.Top.Difference(r.Plans[f.LevelIndex].Union)) ?? f.Top);
            area = Clean(area);
            if (area.IsEmpty || area.Area < minAreaSf) continue;
            var sky = Clean(MeshSlicer.Polygonal(Safe(() => area.Difference(Above(r, flats, f, int.MaxValue))) ?? area));
            found.Add(new ExposedSurface(f.Id, f.IsGlass, area, f.Z0, f.Z1, f.LevelIndex) { Sky = sky.Area < minAreaSf ? MeshSlicer.Factory.CreatePolygon() : sky });
        }
        return found;
    }

    /// <summary>
    /// What covers a flat surface from above, up to level <paramref name="maxLevel"/>: the blocks of the higher levels.
    /// A higher slab alone does not: the floor of an open storey under a roof still shows.
    /// </summary>
    public static NtsGeometry Above(AnalysisResult r, IEnumerable<FlatSurface> flats, FlatSurface f, int maxLevel)
    {
        var parts = new List<NtsGeometry>();
        for (var j = 0; j < r.Plans.Count && j <= maxLevel; j++)
            if (r.Levels[j].Elevation > f.Z1 + 1 && !r.Plans[j].Union.IsEmpty) parts.Add(r.Plans[j].Union);
        if (parts.Count == 0) return MeshSlicer.Factory.CreatePolygon();
        return Safe(() => NetTopologySuite.Operation.Union.CascadedPolygonUnion.Union(parts)) ?? MeshSlicer.Factory.CreatePolygon();
    }

    /// <summary>
    /// The open part of <paramref name="e"/> when everything above level <paramref name="maxLevel"/> is taken away
    /// (floor key axons: the levels under the key level).
    /// </summary>
    public static NtsGeometry SkyUpTo(PD.Core.Drawing.ExportContext ctx, ExposedSurface e, int maxLevel)
    {
        var flats = FlatsFor(ctx);
        var f = flats.FirstOrDefault(x => x.Id == e.Id);
        if (f == null) return e.Sky;
        return Clean(MeshSlicer.Polygonal(Safe(() => e.Area.Difference(Above(ctx.Result, flats, f, maxLevel))) ?? e.Area));
    }

    public static List<FlatSurface> FlatsFor(PD.Core.Drawing.ExportContext ctx)
    {
        if (ctx.Cache.TryGetValue("FlatSurfaces", out var c) && c is List<FlatSurface> s) return s;
        var found = Flats(ctx.Result, ctx.Rules.Levels.GroundElevationFt, ctx.Style.Exposed.MinAreaSf);
        ctx.Cache["FlatSurfaces"] = found;
        return found;
    }

    public static List<ExposedSurface> For(PD.Core.Drawing.ExportContext ctx)
    {
        if (ctx.Cache.TryGetValue("ExposedSurfaces", out var c) && c is List<ExposedSurface> s) return s;
        var st = ctx.Style.Exposed;
        var found = st.Show ? Find(ctx.Result, FlatsFor(ctx), st.MinAreaSf) : new List<ExposedSurface>();
        ctx.Cache["ExposedSurfaces"] = found;
        return found;
    }

    static NtsGeometry Clean(NtsGeometry g) => g.IsEmpty ? g : MeshSlicer.Polygonal(Safe(() => g.Buffer(-0.5).Buffer(0.5)) ?? g);

    static NtsGeometry? Safe(Func<NtsGeometry> op)
    {
        try { return op(); }
        catch (Exception e) when (e is TopologyException or ArgumentException) { return null; }
    }
}
