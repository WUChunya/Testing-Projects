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
    /// Figures standing on a floor line between u0 and u1 (section / elevation): the spacing follows the program's
    /// density, so a lobby bar fills up and a lab bar stays sparse. Returns the offsets along the line.
    /// </summary>
    public static List<Figure> AlongLine(ExportContext ctx, double u0, double u1, string programKey, string seedKey, bool outdoors = false)
    {
        var e = ctx.Style.Entourage;
        var density = outdoors ? e.OutdoorDensityPer1000Sf : e.Density(programKey);
        var figures = new List<Figure>();
        var length = Math.Abs(u1 - u0);
        if (density <= 0 || length < 6) return figures;
        // A 30 ft deep strip of floor is what one line of people stands for.
        var want = Math.Min(e.MaxPerArea, (int)Math.Round(length * 30 / 1000.0 * density));
        if (want <= 0) return figures;
        var spacing = Math.Max(3.5, Math.Min(e.MinSpacingFt, length / want));
        var rng = new Random(Mix(Seed(ctx), seedKey));
        var at = Math.Min(u0, u1) + spacing / 2;
        var end = Math.Max(u0, u1) - spacing / 4;
        while (at < end && figures.Count < want)
        {
            figures.Add(new Figure(at + (rng.NextDouble() - 0.5) * spacing * 0.4, 0, rng.Next(EntourageSymbols.Variants), rng.Next(2) == 0));
            at += spacing;
        }
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

    /// <summary>Bridson-style Poisson-disk sampling in the geometry's envelope, keeping the points inside it.</summary>
    static List<Figure> Scatter(NtsGeometry area, double densityPer1000Sf, double minSpacingFt, int max, int seed)
    {
        var figures = new List<Figure>();
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
    static int Mix(int seed, string key)
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
/// Built-in placeholder figures (PRD 4: replaced by the company's entourage.ai symbols later). Plan = head and
/// shoulders seen from above; elevation = a 5'-9" silhouette. Coordinates are returned in page points.
/// </summary>
public static class EntourageSymbols
{
    public const int Variants = 4;

    /// <summary>Head-and-shoulders seen from above, centred on (x, y) in points.</summary>
    public static DItem Plan(PD.Core.Drawing.Drawing d, EntourageStyle e, string name, double x, double y, double ptPerFt, int variant)
    {
        var r = e.PlanRadiusFt * ptPerFt;
        var s = e.PlanShoulderFt * ptPerFt / 2;
        var turn = variant * Math.PI / 2;
        var ring = new List<double[]>();
        for (var i = 0; i < 20; i++)
        {
            var a = 2 * Math.PI * i / 20;
            // An egg: shoulders wider across than deep, turned per variant.
            double px = s * Math.Cos(a), py = r * Math.Sin(a);
            ring.Add(new[] { x + px * Math.Cos(turn) - py * Math.Sin(turn), y + px * Math.Sin(turn) + py * Math.Cos(turn) });
        }
        return new DItem
        {
            Kind = ItemKind.Path, Name = name, Fill = d.Swatch("ENTOURAGE", e.Color), Opacity = e.Opacity, Paths = new() { ring.ToArray() },
        };
    }

    /// <summary>
    /// Silhouette standing on (x, y) (the feet) in points, <paramref name="h"/> tall; four poses, optionally
    /// mirrored. Simple and readable at small sizes, like the reference drawings.
    /// </summary>
    public static DItem Standing(PD.Core.Drawing.Drawing d, EntourageStyle e, string name, double x, double y, double h, int variant, bool mirror)
    {
        var pts = Points(variant, mirror).Select(q => new[] { x + q[0] * h, y - q[1] * h }).ToArray();
        return Shape(d, e, name, pts);
    }

    /// <summary>
    /// The same silhouette drawn in a projected (axon) frame: <paramref name="up"/> is one figure height in
    /// projected units and the width runs along the perpendicular, so people stand upright on the site plane.
    /// </summary>
    public static double[][] Projected(double x, double y, (double X, double Y) up, int variant, bool mirror)
    {
        var len = Math.Sqrt(up.X * up.X + up.Y * up.Y);
        if (len < 1e-9) return Array.Empty<double[]>();
        double rx = up.Y / len, ry = -up.X / len;  // screen-right, same length as one height unit
        return Points(variant, mirror).Select(q => new[]
        {
            x + up.X * q[1] + rx * q[0] * len,
            y + up.Y * q[1] + ry * q[0] * len,
        }).ToArray();
    }

    /// <summary>Silhouette outline in figure units: x across (± 0.13), y up (0 feet … 1 head top).</summary>
    static List<double[]> Points(int variant, bool mirror)
    {
        var w = 0.26;                           // shoulder width / height
        var stride = variant % 2 == 0 ? 0.07 : 0.13;
        var arm = variant >= 2 ? 0.10 : 0.05;
        var sx = mirror ? -1 : 1;
        double[] P(double u, double v) => new[] { sx * u * w, v };
        return new List<double[]>
        {
            P(-0.5 - stride / w * 0.26, 0),     // left foot
            P(-0.18, 0.46),                     // left hip
            P(0.18, 0.46),                      // right hip
            P(0.5 + stride / w * 0.26, 0),      // right foot
            P(0.28, 0.02),
            P(0.14, 0.46),
            P(0.30 + arm, 0.52),                // right hand
            P(0.46, 0.76),                      // right shoulder
            P(0.20, 0.84),
            P(0.16, 0.90),                      // neck
            P(0.16, 0.95),
            P(0.00, 1.00),                      // head top
            P(-0.16, 0.95),
            P(-0.16, 0.90),
            P(-0.20, 0.84),
            P(-0.46, 0.76),                     // left shoulder
            P(-0.30 - arm, 0.52),               // left hand
            P(-0.14, 0.46),
            P(-0.28, 0.02),
        };
    }

    static DItem Shape(PD.Core.Drawing.Drawing d, EntourageStyle e, string name, double[][] pts) => new()
    {
        Kind = ItemKind.Path, Name = name, Fill = d.Swatch("ENTOURAGE", e.Color), Opacity = e.Opacity, Paths = new() { pts },
    };
}
