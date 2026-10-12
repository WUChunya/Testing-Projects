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

    static double Top(IReadOnlyList<Level> lv, int i) =>
        i + 1 < lv.Count ? lv[i + 1].Elevation : lv[i].Elevation + (i > 0 ? lv[i].Elevation - lv[i - 1].Elevation : 12.0);

    static bool Occupies(double z0, double z1, IReadOnlyList<Level> lv, int i, CleanupRules r)
    {
        var lo = lv[i].Elevation;
        var hi = i + 1 < lv.Count ? lv[i + 1].Elevation : double.PositiveInfinity;
        var overlap = Math.Min(z1, hi) - Math.Max(z0, lo);
        var story = (double.IsInfinity(hi) ? Top(lv, i) : hi) - lo;
        return overlap >= Math.Min(r.MinLevelOverlapFt, 0.5 * story);
    }

    static void Resolve(LevelPlan plan, int levelIndex, CleanupRules r, List<Overlap> overlaps)
    {
        var counted = plan.Footprints.Where(f => !f.OpenToBelow).OrderBy(f => f.Mass.Volume).ToList();
        NtsGeometry union = MeshSlicer.Factory.CreatePolygon();
        var winners = new List<Footprint>();
        foreach (var f in counted)
        {
            foreach (var w in winners)
            {
                if (!w.Raw.EnvelopeInternal.Intersects(f.Raw.EnvelopeInternal)) continue;
                var a = w.Raw.Intersection(f.Raw).Area;
                if (a >= r.MinOverlapAreaSf) overlaps.Add(new Overlap(levelIndex, w.Mass.Id, f.Mass.Id, a));
            }
            f.Resolved = union.IsEmpty ? f.Raw : f.Raw.Difference(union);
            union = union.IsEmpty ? f.Raw : union.Union(f.Raw);
            winners.Add(f);
            var key = f.Mass.ProgramKey;
            plan.AreaByProgram[key] = plan.AreaByProgram.GetValueOrDefault(key) + f.Resolved.Area;
        }
        plan.Union = union;
    }
}
