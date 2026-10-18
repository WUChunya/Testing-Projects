using NetTopologySuite.Geometries;
using PD.Core.Classification;
using PD.Core.Config;
using PD.Core.Drawing;
using PD.Core.Geometry;
using NtsGeometry = NetTopologySuite.Geometries.Geometry;

namespace PD.Core.Entourage;

/// <summary>One placed figure in local feet; <see cref="Mirror"/> flips the silhouette left / right.</summary>
public sealed record Figure(double X, double Y, int Variant, bool Mirror);

/// <summary>
/// PRD 4: people placed on a seeded Poisson disk, density by program (Lobby high, Lab / Office medium, Core,
/// Shell and Mechanical none). The seed lives in the project settings, so a re-export puts everyone back where
/// they were. People the user modelled (role Entourage) are used as they are instead of generated ones.
/// </summary>
public static class EntouragePlacer
{
    public const string Layer = "ENTOURAGE";

    public static bool Enabled(ExportContext ctx) => ctx.Settings.Output.Entourage ?? ctx.Style.Entourage.Enabled;

    public static int Seed(ExportContext ctx) => ctx.Settings.Output.EntourageSeed ?? ctx.Style.Entourage.Seed;

    /// <summary>
    /// Figures inside a program footprint. The count follows the program's density; a seed per (drawing, level,
    /// program) keeps every drawing of one export consistent and stable between exports.
    /// </summary>
    public static List<Figure> InProgram(ExportContext ctx, NtsGeometry area, string programKey, string seedKey)
    {
        var e = ctx.Style.Entourage;
        var density = e.Density(programKey);
        return density <= 0 ? new List<Figure>() : Scatter(area, density, e.MinSpacingFt, e.MaxPerArea, Mix(Seed(ctx), seedKey));
    }

    /// <summary>Figures on open ground (plazas, the site plane in axons, in front of an elevation).</summary>
    public static List<Figure> Outdoors(ExportContext ctx, NtsGeometry area, string seedKey)
    {
        var e = ctx.Style.Entourage;
        return Scatter(area, e.OutdoorDensityPer1000Sf, e.OutdoorMinSpacingFt, e.MaxPerArea, Mix(Seed(ctx), seedKey));
    }

    /// <summary>
    /// Figures standing on a floor line between u0 and u1 (section / elevation). The count follows the program's density
    /// (a lobby bar fills up, a lab bar stays sparse); positions are random (user rev 0.8: no even spacing), at least a
    /// shoulder apart, sometimes in pairs, and never inside <paramref name="blocked"/> (labels). Returns offsets along the line.
    /// </summary>
    public static List<Figure> AlongLine(ExportContext ctx, double u0, double u1, string programKey, string seedKey, bool outdoors = false,
        IReadOnlyList<(double A, double B)>? blocked = null)
    {
        var e = ctx.Style.Entourage;
        var density = outdoors ? e.OutdoorDensityPer1000Sf : e.Density(programKey);
        var figures = new List<Figure>();
        double lo = Math.Min(u0, u1) + 1.5, hi = Math.Max(u0, u1) - 1.5;
        if (density <= 0 || hi - lo < 3) return figures;
        var rng = new Random(Mix(Seed(ctx), seedKey));
        // A 30 ft deep strip of floor is what one line of people stands for; stochastic rounding keeps small bars alive.
        var expected = (hi - lo) * 30 / 1000.0 * density;
        var want = Math.Min(e.MaxPerArea, (int)Math.Floor(expected + rng.NextDouble()));
        const double shoulder = 2.4;
        var gap = Math.Max(shoulder * 1.5, Math.Min(e.MinSpacingFt, (hi - lo) / Math.Max(1, want) * 0.6));
        var at = new List<double>();
        bool Free(double u, double minGap) => u >= lo && u <= hi && at.All(x => Math.Abs(x - u) >= minGap)
            && (blocked == null || !blocked.Any(q => u >= q.A && u <= q.B));
        for (var t = 0; t < want * 40 && at.Count < want; t++)
        {
            var u = lo + rng.NextDouble() * (hi - lo);
            if (!Free(u, gap)) continue;
            at.Add(u);
            // Now and then a companion a shoulder or two away.
            if (at.Count < want && rng.NextDouble() < 0.3)
            {
                var v = u + (rng.Next(2) == 0 ? -1 : 1) * (shoulder + rng.NextDouble() * 1.2);
                if (Free(v, shoulder)) at.Add(v);
            }
        }
        foreach (var u in at.OrderBy(x => x))
            figures.Add(new Figure(u, 0, rng.Next(EntourageSymbols.Variants), rng.Next(2) == 0));
        return figures;
    }

    /// <summary>People the user modelled (role Entourage), as figures at their centre; empty when there are none.</summary>
    public static List<Figure> Modelled(ExportContext ctx, Envelope extent)
    {
        var e = ctx.Style.Entourage;
        if (!e.UseModelled) return new List<Figure>();
        var n = 0;
        return ctx.Result.Local.Objects
            .Where(o => o.Role == Roles.Entourage && !ctx.Result.Stray.Contains(o.Id))
            .Select(o => ((o.BBox.MinX + o.BBox.MaxX) / 2, (o.BBox.MinY + o.BBox.MaxY) / 2))
            .Where(c => extent.Contains(c.Item1, c.Item2))
            .Select(c => new Figure(c.Item1, c.Item2, n++ % EntourageSymbols.Variants, n % 2 == 0))
            .ToList();
    }

    /// <summary>
    /// The generated figures plus the modelled ones standing inside <paramref name="area"/>; a generated figure closer than
    /// <paramref name="clearFt"/> to a modelled one gives way (the modelled people replace the generated ones where they exist).
    /// </summary>
    public static List<Figure> WithModelled(ExportContext ctx, IEnumerable<Figure> generated, NtsGeometry area, double clearFt = 4)
    {
        var mine = Modelled(ctx, area.EnvelopeInternal)
            .Where(f => area.Contains(MeshSlicer.Factory.CreatePoint(new Coordinate(f.X, f.Y)))).ToList();
        if (mine.Count == 0) return generated.ToList();
        return generated.Where(g => mine.All(m => Math.Abs(m.X - g.X) + Math.Abs(m.Y - g.Y) > clearFt)).Concat(mine).ToList();
    }

    /// <summary>Bridson-style Poisson-disk sampling in the geometry's envelope, keeping the points inside it.</summary>
    static List<Figure> Scatter(NtsGeometry area, double densityPer1000Sf, double minSpacingFt, int max, int seed)
    {
        var figures = new List<Figure>();
        area = MeshSlicer.Polygonal(area);   // Contains() does not take a GeometryCollection either
        if (area.IsEmpty || area.Area < 10 || densityPer1000Sf <= 0) return figures;
        var want = Math.Min(max, (int)Math.Round(area.Area / 1000.0 * densityPer1000Sf));
        if (want <= 0) return figures;
        var env = area.EnvelopeInternal;
        // Keep the disk from starving when the room is crowded: shrink the spacing to what the area can hold.
        var spacing = Math.Min(minSpacingFt, Math.Sqrt(area.Area / Math.Max(1, want)) * 0.75);
        var rng = new Random(seed);
        var placed = new List<Coordinate>();
        var tries = 0;
        var prepared = area;
        while (placed.Count < want && tries < want * 60 + 200)
        {
            tries++;
            var c = new Coordinate(env.MinX + rng.NextDouble() * env.Width, env.MinY + rng.NextDouble() * env.Height);
            if (placed.Any(p => p.Distance(c) < spacing)) continue;
            if (!prepared.Contains(MeshSlicer.Factory.CreatePoint(c))) continue;
            placed.Add(c);
            figures.Add(new Figure(c.X, c.Y, rng.Next(EntourageSymbols.Variants), rng.Next(2) == 0));
        }
        return figures;
    }

    /// <summary>Stable per-drawing seed: the project seed mixed with the drawing / level / program name.</summary>
    public static int Mix(int seed, string key)
    {
        unchecked
        {
            var h = (uint)seed * 2166136261;
            foreach (var ch in key) h = (h ^ ch) * 16777619;
            return (int)(h & 0x7FFFFFFF);
        }
    }
}

/// <summary>
/// Office entourage (styles/entourage_library.json from ENTOURAGES.ai / TREES.ai, tools/entourage_from_ai.py): standing
/// people and trees are AI symbols (<see cref="EntourageArt"/>); plans use the built-in top-view figure; axons draw the
/// same people outlines in the projected frame (they are clipped by the hidden-face pass).
/// </summary>
public static class EntourageSymbols
{
    /// <summary>One variant per office figure.</summary>
    public static int Variants => Math.Max(1, EntourageLibrary.Default.People.Count);

    static LibraryFigure Person(int variant) => EntourageLibrary.Default.People[((variant % Variants) + Variants) % Variants];

    /// <summary>Head-and-shoulders seen from above, centred on (x, y) in points, turned per variant.</summary>
    public static DItem Plan(PD.Core.Drawing.Drawing d, EntourageStyle e, string name, double x, double y, double ptPerFt, int variant) =>
        EntourageArt.Place(name, EntourageArt.PersonTop(d, e.Color), x, y, e.PlanShoulderFt * ptPerFt, false, e.Opacity, variant * 360.0 / Variants);

    /// <summary>Office figure <paramref name="variant"/> standing on (x, y) (the feet) in points, <paramref name="h"/> tall.</summary>
    public static DItem Standing(PD.Core.Drawing.Drawing d, EntourageStyle e, string name, double x, double y, double h, int variant, bool mirror) =>
        EntourageArt.Place(name, EntourageArt.Person(d, Person(variant), e.Color), x, y, h, mirror, e.Opacity);

    /// <summary>
    /// The figure's rings in a projected (axon) frame: <paramref name="up"/> is one figure height in projected units
    /// and the width runs along the perpendicular, so people stand upright on the site plane.
    /// </summary>
    public static List<double[][]> Projected(double x, double y, (double X, double Y) up, int variant, bool mirror)
    {
        var len = Math.Sqrt(up.X * up.X + up.Y * up.Y);
        if (len < 1e-9) return new();
        double rx = up.Y / len, ry = -up.X / len;  // screen-right, same length as one height unit
        var sx = mirror ? -1 : 1;
        return Person(variant).Parts.SelectMany(p => p).Select(r => r.Select(q => new[]
        {
            x + up.X * q[1] + rx * sx * q[0] * len,
            y + up.Y * q[1] + ry * sx * q[0] * len,
        }).ToArray()).ToList();
    }

    /// <summary>A tree silhouette, chosen and coloured by the tree's own seed, trunk foot at (x, y), <paramref name="h"/> tall (points).</summary>
    public static DItem Tree(PD.Core.Drawing.Drawing d, EntourageStyle e, string name, double x, double y, double h, Random rng)
    {
        var trees = EntourageLibrary.Default.Trees;
        var fill = e.TreeFills.Count > 0 ? e.TreeFills[rng.Next(e.TreeFills.Count)] : "#9CC078";
        return EntourageArt.Place(name, EntourageArt.Tree(d, trees[rng.Next(trees.Count)], fill), x, y, h, rng.Next(2) == 0, e.TreeOpacity);
    }
}
