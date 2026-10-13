using PD.Core.Drawing;

namespace PD.Core.Entourage;

/// <summary>
/// Registers library figures as drawing symbols (PD_PERSON_P03, PD_TREE_T05_9CC078, PD_PERSON_TOP) and places
/// instances. Symbol space: pt, y down, origin at the feet / trunk foot (top view: the centre), height <see cref="UnitPt"/>.
/// </summary>
public static class EntourageArt
{
    public const double UnitPt = 100;

    public static string Person(PD.Core.Drawing.Drawing d, LibraryFigure f, string fillHex) =>
        Register(d, "PD_PERSON_" + f.Id, f.Parts, d.Swatch("PD_PEOPLE", fillHex));

    /// <summary>One symbol per tree shape and colour (symbols carry their fill).</summary>
    public static string Tree(PD.Core.Drawing.Drawing d, LibraryFigure f, string fillHex)
    {
        var hex = fillHex.TrimStart('#').ToUpperInvariant();
        return Register(d, $"PD_TREE_{f.Id}_{hex}", f.Parts, d.Swatch("PD_TREE_" + hex, fillHex));
    }

    /// <summary>Built-in plan figure seen from above: shoulders with the head inside a thin gap; 1 unit = shoulder width.</summary>
    public static string PersonTop(PD.Core.Drawing.Drawing d, string fillHex)
    {
        static double[][] Ellipse(double rx, double ry, double cy = 0) => Enumerable.Range(0, 24)
            .Select(i => new[] { rx * Math.Cos(i * Math.PI / 12), cy + ry * Math.Sin(i * Math.PI / 12) }).ToArray();
        var parts = new List<List<double[][]>>
        {
            new() { Ellipse(0.5, 0.24, 0.5), Ellipse(0.19, 0.19, 0.5) },
            new() { Ellipse(0.15, 0.15, 0.5) },
        };
        return Register(d, "PD_PERSON_TOP", parts, d.Swatch("PD_PEOPLE", fillHex), centred: true);
    }

    static string Register(PD.Core.Drawing.Drawing d, string name, List<List<double[][]>> parts, string? fill, bool centred = false)
    {
        if (d.Symbols.Any(s => s.Name == name)) return name;
        // Library y is up from the feet; symbol y is down. Top-view figures are centred on the origin.
        var shift = centred ? 0.5 : 0;
        var sp = parts.Select(p => p.Select(r => r.Select(q => new[] { Math.Round(q[0] * UnitPt, 2), Math.Round(-(q[1] - shift) * UnitPt, 2) }).ToArray()).ToList()).ToList();
        var all = sp.SelectMany(p => p).SelectMany(r => r).ToList();
        d.Symbols.Add(new DSymbol
        {
            Name = name, Fill = fill, Parts = sp,
            Bounds = new[] { all.Min(q => q[0]), all.Min(q => q[1]), all.Max(q => q[0]), all.Max(q => q[1]) },
        });
        return name;
    }

    /// <summary>An instance <paramref name="heightPt"/> tall with its origin at (x, y); mirrored and/or rotated (degrees, CCW).</summary>
    public static DItem Place(string name, string symbol, double x, double y, double heightPt, bool mirror = false, double opacity = 1, double rotationDeg = 0)
    {
        var s = heightPt / UnitPt;
        var a = rotationDeg * Math.PI / 180;
        double cos = Math.Cos(a), sin = Math.Sin(a), mx = mirror ? -1 : 1;
        // Page y is down: a CCW rotation on the page is [cos, -sin; sin, cos] in (x right, y down).
        return new DItem
        {
            Kind = ItemKind.Symbol, Name = name, Symbol = symbol, X = Math.Round(x, 3), Y = Math.Round(y, 3), Opacity = opacity,
            Matrix = new[] { Math.Round(s * mx * cos, 5), Math.Round(-s * mx * sin, 5), Math.Round(s * sin, 5), Math.Round(s * cos, 5) },
        };
    }

    /// <summary>A random library figure (null when the library is empty).</summary>
    public static LibraryFigure? Pick(Random rng, List<LibraryFigure> figures) => figures.Count == 0 ? null : figures[rng.Next(figures.Count)];
}
