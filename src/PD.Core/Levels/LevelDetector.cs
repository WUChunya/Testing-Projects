using PD.Core.Config;
using PD.Core.Model;

namespace PD.Core.Levels;

/// <summary>
/// PRD B1: levels from slab tops (anchor), mass bottoms and repeated facade-panel bottoms.
/// Mass/facade bottoms snap down to the nearest slab top within tolerance; a candidate one hop from a
/// snapped one joins that level (flagged as scattered); leftovers form mass-only levels when well supported,
/// otherwise they are reported as unsupported (outlier / mezzanine candidates).
/// </summary>
public static class LevelDetector
{
    public static List<LevelCandidate> SlabCandidates(IEnumerable<SourceObject> slabs, LevelRules r) => slabs
        .Where(o => o.Points == null && o.BBox.SizeZ < r.SlabMaxThicknessFt)
        .Select(o => (o, area: (o.BBox.MaxX - o.BBox.MinX) * (o.BBox.MaxY - o.BBox.MinY)))
        .Where(x => x.area > r.SlabMinAreaSf)
        .Select(x => new LevelCandidate(x.o.BBox.MaxZ, CandidateKind.Slab, new[] { x.o.Id }, x.area))
        .ToList();

    public static List<LevelCandidate> MassCandidates(IEnumerable<SourceObject> masses) =>
        masses.Select(o => new LevelCandidate(o.BBox.MinZ, CandidateKind.MassBottom, new[] { o.Id })).ToList();

    /// <summary>Facade bottoms repeated at least <c>FacadeMinRepeat</c> times (within 0.25 ft).</summary>
    public static List<LevelCandidate> FacadeCandidates(IEnumerable<SourceObject> panels, LevelRules r) =>
        Cluster(panels.Where(o => o.Points == null).Select(o => (z: o.BBox.MinZ, id: o.Id)).ToList(), x => x.z, 0.25)
            .Where(g => g.Count >= r.FacadeMinRepeat)
            .Select(g => new LevelCandidate(g.Min(x => x.z), CandidateKind.FacadeBottom, g.Select(x => x.id).ToArray()))
            .ToList();

    public static LevelResult Detect(List<LevelCandidate> slabs, List<LevelCandidate> others, LevelRules r)
    {
        var tol = r.ClusterToleranceFt;
        var belowSlack = r.SlabSnapBelowFt;
        var levels = Cluster(slabs, c => c.Elevation, tol).Select(g =>
        {
            var l = new Level { Sources = LevelSource.Slab };
            l.Members.AddRange(g);
            return l;
        }).ToList();

        // 1. Snap down to the nearest slab top.
        var support = new Dictionary<LevelCandidate, int>();
        var snapped = new List<(LevelCandidate c, Level l)>();
        var rest = new List<LevelCandidate>();
        foreach (var c in others.OrderBy(c => c.Elevation))
        {
            var best = levels.SelectMany(l => l.Members.Where(m => m.Kind == CandidateKind.Slab).Select(m => (l, m)))
                .Where(x => x.m.Elevation <= c.Elevation + belowSlack && c.Elevation - x.m.Elevation <= tol)
                .OrderByDescending(x => x.m.Elevation)
                .FirstOrDefault();
            if (best.l == null) { rest.Add(c); continue; }
            support[best.m] = support.GetValueOrDefault(best.m) + 1;
            snapped.Add((c, best.l));
        }
        foreach (var (c, l) in snapped) Attach(l, c);

        // 2. One hop from a snapped candidate (never below the level's slab).
        var leftovers = new List<LevelCandidate>();
        foreach (var c in rest)
        {
            var hop = snapped.Where(s => Math.Abs(s.c.Elevation - c.Elevation) <= tol && c.Elevation >= SlabTop(s.l) - belowSlack)
                .OrderBy(s => Math.Abs(s.c.Elevation - c.Elevation)).FirstOrDefault();
            if (hop.l != null) Attach(hop.l, c); else leftovers.Add(c);
        }

        // 3. Elevation = best-supported slab top (ties → larger slab).
        foreach (var l in levels)
            l.Elevation = l.Members.Where(m => m.Kind == CandidateKind.Slab)
                .OrderByDescending(m => support.GetValueOrDefault(m)).ThenByDescending(m => m.Area).First().Elevation;

        // 4. Leftovers: well supported clusters become slab-less levels; the rest are unsupported.
        var issues = new List<LevelIssue>();
        foreach (var g in Cluster(leftovers, c => c.Elevation, tol))
        {
            if (g.Count >= r.MassOnlyMinSupport)
            {
                var l = new Level();
                foreach (var c in g) Attach(l, c);
                var sorted = g.Select(c => c.Elevation).OrderBy(z => z).ToArray();
                l.Elevation = sorted[sorted.Length / 2];
                levels.Add(l);
                continue;
            }
            foreach (var c in g)
                issues.Add(new LevelIssue(LevelIssueKind.Unsupported, c.Elevation,
                    $"{FeetInches.Format(c.Elevation)}: {Describe(c.Kind)} without slab support (mezzanine or outlier?)", c.ObjectIds));
        }

        foreach (var l in levels)
        {
            l.SpreadMin = l.Members.Min(m => m.Elevation);
            l.SpreadMax = l.Members.Max(m => m.Elevation);
        }
        levels = levels.OrderBy(l => l.Elevation).ToList();
        Name(levels, r, issues);
        return new LevelResult(levels, issues);
    }

    static void Name(List<Level> levels, LevelRules r, List<LevelIssue> issues)
    {
        var tol = r.ClusterToleranceFt;
        var below = levels.Count(l => l.Elevation < r.GroundElevationFt - tol);
        Level? main = null;
        int n = 0, mezz = 0;
        for (var i = 0; i < levels.Count; i++)
        {
            var l = levels[i];
            if (main != null && l.Elevation - main.Elevation < r.MinFloorHeightFt)
            {
                l.IsMezzanine = true;
                l.Name = main.Name + (++mezz > 1 ? $"M{mezz}" : "M");
                issues.Add(new LevelIssue(LevelIssueKind.Mezzanine, l.Elevation, $"{l.Name} at {FeetInches.Format(l.Elevation)}: confirm mezzanine (own level or merge down)", Ids(l)));
                continue;
            }
            l.Name = i < below ? $"B{below - i}" : $"L{++n}";
            main = l;
            mezz = 0;
        }
        var top = levels.LastOrDefault(l => !l.IsMezzanine);
        if (top != null) top.IsRoof = true;

        foreach (var l in levels)
        {
            var at = $"{l.Name} at {FeetInches.Format(l.Elevation)}";
            if (l.Spread > r.ScatterWarnFt)
                issues.Add(new LevelIssue(LevelIssueKind.Scattered, l.Elevation, $"{at}: data spread {FeetInches.Format(l.SpreadMin)} to {FeetInches.Format(l.SpreadMax)}, please confirm", Ids(l)));
            if ((l.Sources & LevelSource.Slab) == 0)
                issues.Add(new LevelIssue(LevelIssueKind.NoSlab, l.Elevation, $"{at}: no slab found, elevation taken from mass bottoms", Ids(l)));
            else if (l.Sources == LevelSource.Slab)
                issues.Add(new LevelIssue(LevelIssueKind.SingleSource, l.Elevation, $"{at}: only slabs support this level", Ids(l)));
        }
    }

    static double SlabTop(Level l) => l.Members.Where(m => m.Kind == CandidateKind.Slab).Select(m => m.Elevation).DefaultIfEmpty(l.Elevation).Min();

    static void Attach(Level l, LevelCandidate c)
    {
        l.Members.Add(c);
        l.Sources |= c.Kind switch
        {
            CandidateKind.Slab => LevelSource.Slab,
            CandidateKind.MassBottom => LevelSource.Mass,
            _ => LevelSource.Facade,
        };
    }

    static string[] Ids(Level l) => l.Members.SelectMany(m => m.ObjectIds).Distinct().ToArray();

    static string Describe(CandidateKind k) => k switch
    {
        CandidateKind.MassBottom => "mass bottom",
        CandidateKind.FacadeBottom => "facade panel bottom",
        _ => "slab top",
    };

    /// <summary>Single-linkage clustering of sorted values.</summary>
    public static List<List<T>> Cluster<T>(IEnumerable<T> items, Func<T, double> key, double tol)
    {
        var groups = new List<List<T>>();
        foreach (var it in items.OrderBy(key))
        {
            if (groups.Count > 0 && key(it) - key(groups[^1][^1]) <= tol) groups[^1].Add(it);
            else groups.Add(new List<T> { it });
        }
        return groups;
    }
}
