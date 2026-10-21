using NetTopologySuite.Geometries;
using PD.Core.Config;
using PD.Core.Geometry;
using PD.Core.Levels;
using PD.Core.Model;
using NtsGeometry = NetTopologySuite.Geometries.Geometry;

namespace PD.Core.Cleanup;

/// <summary>Single: one level (for a multi-level mass the user forces its base level only). KeepTall: counted on the base
/// level, upper levels drawn open-to-below. Split: counted on every level.</summary>
public enum SpanMode { Single, KeepTall, Split }

public sealed record SpanDecision(string MassId, int[] LevelIndices, SpanMode Mode, double Height, double StoryHeight);

public sealed class Footprint
{
    public required Mass Mass { get; init; }
    public int LevelIndex { get; init; }
    public required NtsGeometry Raw { get; init; }
    public NtsGeometry Resolved { get; set; } = MeshSlicer.Factory.CreatePolygon();
    /// <summary>True on the upper levels of a kept tall space: drawn as open-to-below, not counted in areas.</summary>
    public bool OpenToBelow { get; init; }
}

public sealed record Overlap(int LevelIndex, string WinnerId, string LoserId, double Area);

public sealed class LevelPlan
{
    public required Level Level { get; init; }
    public List<Footprint> Footprints { get; } = new();
    public NtsGeometry Union { get; set; } = MeshSlicer.Factory.CreatePolygon();
    public Dictionary<string, double> AreaByProgram { get; } = new();
}

/// <summary>Assigns solids to levels (span rule), slices footprints and resolves overlaps (smaller volume wins).</summary>
public static class LevelAssignment
{
    public static (List<SpanDecision> spans, List<LevelPlan> plans, List<Overlap> overlaps) Run(
        IReadOnlyList<Mass> masses, IReadOnlyList<Level> levels, CleanupRules r, IReadOnlyDictionary<string, SpanMode>? overrides = null)
    {
        var main = levels.ToList();
        var plans = main.Select(l => new LevelPlan { Level = l }).ToList();
        var spans = new List<SpanDecision>();

        foreach (var m in masses.Where(m => m.IsSolid && m.Source.Mesh != null))
        {
            var b = m.Source.BBox;
            var occ = Enumerable.Range(0, main.Count).Where(i => Occupies(b.MinZ, b.MaxZ, main, i, r)).ToArray();
            // A slab-like mass (roof garden, terrace, paving) never fills a story: give it the level under its top face.
            if (occ.Length == 0 && b.SizeZ <= r.FlatMassMaxThicknessFt) occ = FlatLevel(b, main, r);
            if (occ.Length == 0) continue;
            var story = Top(main, occ[0]) - main[occ[0]].Elevation;
            var mode = occ.Length == 1 ? SpanMode.Single
                : occ.Length <= r.SpanKeepTallMaxLevels && b.SizeZ <= r.SpanKeepTallMaxStoryFactor * story ? SpanMode.KeepTall
                : SpanMode.Split;
            if (occ.Length > 1 && overrides != null && overrides.TryGetValue(m.Id, out var o))
            {
                mode = o;
                if (o == SpanMode.Single) occ = new[] { occ[0] }; // user: belongs to its base level only
            }
            spans.Add(new SpanDecision(m.Id, occ, mode, b.SizeZ, story));

            foreach (var i in occ)
            {
                var z = Math.Clamp(main[i].Elevation + r.PlanCutHeightFt, b.MinZ + 0.05, b.MaxZ - 0.05);
                var poly = MeshSlicer.SliceToPolygon(m.Source.Mesh!, z, r.WeldToleranceFt, r.GapCloseToleranceFt, m.Status == MassStatus.SectionClosed);
                // A cut just under the top of a thin mass can miss it (a top face a hair lower than its bounding box,
                // user rev 0.8.7: the B1 circulation plate vanished); its mid-height always cuts it.
                if (poly.IsEmpty && b.SizeZ <= r.FlatMassMaxThicknessFt && Math.Abs(z - (b.MinZ + b.MaxZ) / 2) > 0.05)
                    poly = MeshSlicer.SliceToPolygon(m.Source.Mesh!, (b.MinZ + b.MaxZ) / 2, r.WeldToleranceFt, r.GapCloseToleranceFt, m.Status == MassStatus.SectionClosed);
                if (poly.IsEmpty) continue;
                plans[i].Footprints.Add(new Footprint
                {
                    Mass = m, LevelIndex = i, Raw = poly, OpenToBelow = mode == SpanMode.KeepTall && i != occ[0],
                });
            }
        }

        var overlaps = new List<Overlap>();
        foreach (var (plan, i) in plans.Select((p, i) => (p, i)))
            Resolve(plan, i, r, overlaps);
        return (spans, plans, overlaps);
    }

    /// <summary>Level whose elevation is at or just below a flat mass's top, within one story; empty when it is below L1.</summary>
    static int[] FlatLevel(BBox3 b, IReadOnlyList<Level> lv, CleanupRules r)
    {
        var top = b.MaxZ + r.FlatMassMaxThicknessFt;
        var i = -1;
        for (var k = 0; k < lv.Count; k++)
            if (lv[k].Elevation <= top) i = k;
        if (i < 0) return Array.Empty<int>();
        var story = Top(lv, i) - lv[i].Elevation;
        return b.MinZ - lv[i].Elevation > r.SpanKeepTallMaxStoryFactor * story ? Array.Empty<int>() : new[] { i };
    }

    /// <summary>Index of the level a storey runs up to: the next level, past mezzanines for a main level; Count at the top.</summary>
    static int Next(IReadOnlyList<Level> lv, int i)
    {
        var n = i + 1;
        while (!lv[i].IsMezzanine && n < lv.Count && lv[n].IsMezzanine) n++;
        return n;
    }

    static double Top(IReadOnlyList<Level> lv, int i)
    {
        var n = Next(lv, i);
        return n < lv.Count ? lv[n].Elevation : lv[i].Elevation + (i > 0 ? lv[i].Elevation - lv[i - 1].Elevation : 12.0);
    }

    static bool Occupies(double z0, double z1, IReadOnlyList<Level> lv, int i, CleanupRules r)
    {
        var lo = lv[i].Elevation;
        var n = Next(lv, i);
        var hi = n < lv.Count ? lv[n].Elevation : double.PositiveInfinity;
        // A mezzanine holds what starts on it; its main storey leaves those to it (user rev 0.9.3).
        var tol = r.MezzanineStartToleranceFt;
        if (lv[i].IsMezzanine ? z0 < lo - tol : Enumerable.Range(i + 1, n - i - 1).Any(k => z0 >= lv[k].Elevation - tol)) return false;
        var overlap = Math.Min(z1, hi) - Math.Max(z0, lo);
        var story = (double.IsInfinity(hi) ? Top(lv, i) : hi) - lo;
        return overlap >= Math.Min(r.MinLevelOverlapFt, 0.5 * story);
    }

    static void Resolve(LevelPlan plan, int levelIndex, CleanupRules r, List<Overlap> overlaps)
    {
        var counted = plan.Footprints.Where(f => !f.OpenToBelow).OrderBy(f => f.Mass.Volume).ToList();
        // The upper part of a kept tall room is a void on this level: no other program counts there (user rev 0.9.2,
        // CAMPUS: a whole-floor L2 circulation mass took over the auditorium's open-to-below).
        var voidParts = plan.Footprints.Where(f => f.OpenToBelow).Select(f => f.Raw).ToList();
        var voids = voidParts.Count == 0 ? MeshSlicer.Factory.CreatePolygon()
            : MeshSlicer.Polygonal(NetTopologySuite.Operation.Union.CascadedPolygonUnion.Union(voidParts));
        var union = voids;
        var winners = new List<Footprint>();
        foreach (var f in counted)
        {
            foreach (var w in winners)
            {
                if (!w.Raw.EnvelopeInternal.Intersects(f.Raw.EnvelopeInternal)) continue;
                var a = w.Raw.Intersection(f.Raw).Area;
                if (a >= r.MinOverlapAreaSf) overlaps.Add(new Overlap(levelIndex, w.Mass.Id, f.Mass.Id, a));
            }
            // Overlays keep only their polygons: a sliver can come back as a line, and NTS refuses a mixed collection as
            // the argument of the next overlay (RIVER 2026-10-19 TK model).
            f.Resolved = union.IsEmpty ? f.Raw : MeshSlicer.Polygonal(f.Raw.Difference(union));
            union = union.IsEmpty ? f.Raw : MeshSlicer.Polygonal(union.Union(f.Raw));
            winners.Add(f);
            var key = f.Mass.ProgramKey;
            plan.AreaByProgram[key] = plan.AreaByProgram.GetValueOrDefault(key) + f.Resolved.Area;
        }
        plan.Union = union;
        Seal(plan, counted, r.SealGapFt, voids);
    }

    /// <summary>
    /// Blocks closer than <paramref name="gap"/> are modelling error and meant to touch (user rev 0.8): the level outline
    /// is closed over such gaps and each footprint grows into them (smaller volume first, no overlaps), so drawings get
    /// one outline and shared edges. Areas were counted before, from the modelled geometry.
    /// </summary>
    static void Seal(LevelPlan plan, List<Footprint> counted, double gap, NtsGeometry voids)
    {
        if (gap <= 0 || plan.Union.IsEmpty) return;
        var mitre = new NetTopologySuite.Operation.Buffer.BufferParameters { JoinStyle = NetTopologySuite.Operation.Buffer.JoinStyle.Mitre };
        NtsGeometry Grow(NtsGeometry g, double d) => NetTopologySuite.Operation.Buffer.BufferOp.Buffer(g, d, mitre);
        try
        {
            var closed = Grow(Grow(plan.Union, gap / 2), -gap / 2);
            if (closed.IsEmpty) return;
            NtsGeometry taken = voids;
            var sealedParts = new List<(Footprint F, NtsGeometry G)>();
            foreach (var f in counted.Where(f => !f.Resolved.IsEmpty))
            {
                var g = MeshSlicer.Polygonal(Grow(f.Resolved, gap / 2).Intersection(closed));
                if (!taken.IsEmpty) g = MeshSlicer.Polygonal(g.Difference(taken));
                if (g.IsEmpty) continue;
                sealedParts.Add((f, g));
                taken = taken.IsEmpty ? g : MeshSlicer.Polygonal(taken.Union(g));
            }
            foreach (var (f, g) in sealedParts) f.Resolved = g;
            plan.Union = closed;
        }
        catch (Exception e) when (e is TopologyException or ArgumentException)
        {
            // Keep the modelled geometry.
        }
    }
}
