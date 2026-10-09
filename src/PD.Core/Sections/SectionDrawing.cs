using NetTopologySuite.Geometries;
using PD.Core.Drawing;
using PD.Core.Model;
using PD.Core.Settings;
using NtsGeometry = NetTopologySuite.Geometries.Geometry;

namespace PD.Core.Sections;

/// <summary>PRD 5.3: turns <see cref="SectionGeometry"/> into a one-artboard Illustrator drawing.</summary>
public static class SectionDrawing
{
    public static PD.Core.Drawing.Drawing Build(ExportContext ctx, SectionSetting sec)
    {
        var r = ctx.Result;
        var (ax, ay, _) = r.Frame.ToLocal(sec.Line[0], sec.Line[1], 0);
        var (bx, by, _) = r.Frame.ToLocal(sec.Line[2], sec.Line[3], 0);
        var plane = new Geometry.SectionPlane(ax, ay, bx, by, sec.LookLeft);
        var depth = sec.DepthFt > 0 ? sec.DepthFt : ctx.Rules.Output.SectionDepthFt;
        var g = SectionGeometry.Build(r, plane, depth, ctx.Rules, ctx.Style);
        var st = ctx.Style.Section;
        var pg = ctx.Style.Page;

        var d = new PD.Core.Drawing.Drawing { FileName = $"02_Section_{Safe(sec.Name)}.ai" };
        ctx.AddStandardStyles(d);
        foreach (var n in new[] { "PD_CONTEXT", "PD_LEVELS", "PD_BEYOND", "FACADE", "PD_PROGRAM", "PD_OUTLINE", "PD_SLAB", "PD_GROUND", "PD_LABELS", "PD_ANNOTATION" })
            d.Layer(n);

        // True scale always; the artboard grows around the drawing ("Fit content") or the drawing is centred on a fixed sheet.
        var k = ctx.PtPerFt;
        var zMax = new[] { g.Outline }.Concat(g.Beyond.Select(b => b.Geometry)).Concat(g.BuildingCuts.Select(b => b.Geometry))
            .Where(x => !x.IsEmpty).Select(x => x.EnvelopeInternal.MaxY).DefaultIfEmpty(g.Ground + 30).Max();
        double w = plane.Length * k, h = (zMax - g.Ground) * k + st.GroundPt, m = pg.MarginPt;
        // Side columns only when their annotations are switched on.
        var colL = st.LevelColumn ? pg.LevelColumnPt : 0;
        var colR = st.HeightDimension ? pg.DimColumnPt : 0;
        double abW, abH, x0, y0;
        if (ctx.ArtboardSize is { } fixedSize)
        {
            (abW, abH) = fixedSize;
            x0 = m + colL + (abW - 2 * m - colL - colR - w) / 2;
            y0 = m + (abH - 2 * m - pg.TitleBandPt - h) / 2;
            if (x0 < m + colL - 0.5 || y0 < m - 0.5)
                d.Notes.Add($"Section {sec.Name} at {ctx.Settings.Output.Scale} is larger than {ctx.Settings.Output.Artboard}; use \"Fit content\" or a smaller scale.");
        }
        else
        {
            abW = 2 * m + colL + w + colR;
            abH = 2 * m + h + pg.TitleBandPt;
            x0 = m + colL;
            y0 = m;
        }
        var map = new PageMap(x0, y0, k, zMax, plane.Length, flip: false);
        d.Artboards.Add(new Artboard($"SECTION {sec.Name}", Math.Ceiling(abW), Math.Ceiling(abH)));
        var p = $"SEC_{Safe(sec.Name)}_";

        // Other buildings: light grey silhouettes with a thin outline (cut or seen), trimmed at grade.
        var ctxFill = d.Swatch("BG_CONTEXT", st.ContextFill);
        var ctxLine = d.Gray(st.ContextStroke);
        foreach (var (b, i) in g.Beyond.Select((b, i) => (b, i)).Where(x => x.b.Kind == BeyondKind.Building && !x.b.Geometry.IsEmpty))
        {
            d.Layer("PD_CONTEXT").Items.Add(map.Poly(p + $"CONTEXT_{i:000}", b.Geometry, ctxFill, ctxLine, st.ContextStrokePt));
            if (b.Label.Length == 0) continue;
            var e = b.Geometry.EnvelopeInternal;
            d.Layer("PD_LABELS").Items.Add(Text(p + $"CONTEXT_{i:000}_LBL", map.X((e.MinX + e.MaxX) / 2), map.Y(e.MaxY) - 6, b.Label, "PD Note", "center"));
        }
        foreach (var (id, geom) in g.BuildingCuts.Where(x => !x.Geometry.IsEmpty))
            d.Layer("PD_CONTEXT").Items.Add(map.Poly(p + "CONTEXT_CUT_" + id[..Math.Min(8, id.Length)], geom, ctxFill, ctxLine, st.ContextStrokePt));

        // Faint level lines and the left level column: bold level name + floor-to-floor height.
        var main = g.Levels.Where(l => !l.IsMezzanine).ToList();
        foreach (var l in g.Levels)
        {
            d.Layer("PD_LEVELS").Items.Add(map.Line(p + "LVL_" + l.Name, new[] { (0.0, l.Elevation), (plane.Length, l.Elevation) }, d.Gray(st.LevelLineStroke), st.LevelLineStrokePt));
            if (!st.LevelColumn) continue;
            var next = main.FirstOrDefault(x => x.Elevation > l.Elevation + 0.01);
            var height = next == null || l.IsRoof ? "ROOF" : FeetInches.Format(next.Elevation - l.Elevation);
            var y = map.Y(l.Elevation) - 3;
            var lx = x0 - colL + 6;
            d.Layer("PD_ANNOTATION").Items.Add(Text(p + "LVL_" + l.Name + "_TXT", lx, y, l.Name, "PD Level", "left"));
            d.Layer("PD_ANNOTATION").Items.Add(Text(p + "LVL_" + l.Name + "_HT", lx + 22, y, height, "PD Note", "left"));
        }

        // Our own building behind the cut (uncut masses, facade grid): faint lines only, hidden by the colour blocks.
        foreach (var (b, i) in g.Beyond.Select((b, i) => (b, i)).Where(x => x.b.Kind != BeyondKind.Building && !x.b.Geometry.IsEmpty))
            d.Layer(b.Kind == BeyondKind.Facade ? "FACADE" : "PD_BEYOND").Items.Add(
                map.Poly(p + $"BEYOND_{i:000}", b.Geometry, null, d.Gray(st.BeyondStroke), st.BeyondStrokePt));

        SectionBars.Add(d, ctx, g, map, p);

        // Thin exterior wall line, visual slabs (fixed weight, centred on the floor line), facade cuts, ground line.
        if (!g.Outline.IsEmpty)
            d.Layer("PD_OUTLINE").Items.Add(map.Poly(p + "OUTLINE", g.Outline, null, d.Gray("#000000"), st.OutlinePt));
        foreach (var (c, i) in g.FacadeCut.Select((c, i) => (c, i)))
            d.Layer("FACADE").Items.Add(map.Line(p + $"FACADE_CUT_{i:000}", c, d.Gray("#000000"), st.FacadeCutPt));
        var slab = d.Swatch("PD_SLAB", st.SlabColor);
        foreach (var (u0, u1, z) in g.SlabLines)
            d.Layer("PD_SLAB").Items.Add(new DItem
            {
                Kind = ItemKind.Path, Name = p + $"SLAB_{FeetInches.Format(z)}_{u0:0}", Fill = slab,
                Paths = new() { RectPts(Math.Min(map.X(u0), map.X(u1)), map.Y(z) - st.SlabPt / 2, Math.Max(map.X(u0), map.X(u1)), map.Y(z) + st.SlabPt / 2) },
            });
        d.Layer("PD_GROUND").Items.Add(new DItem
        {
            Kind = ItemKind.Path, Name = p + "GROUND", Fill = d.Swatch("BG_GROUND", st.GroundFill),
            Paths = new() { RectPts(map.X(0), map.Y(g.Ground), map.X(plane.Length), map.Y(g.Ground) + st.GroundPt) },
        });

        SectionAnnotation.Add(d, ctx, g, map, p, sec, g.Ground, zMax, titleX: x0, titleY: map.Y(g.Ground) + st.GroundPt + 26);
        return d;
    }

    static double[][] RectPts(double x0, double y0, double x1, double y1) =>
        new[] { new[] { x0, y0 }, new[] { x1, y0 }, new[] { x1, y1 }, new[] { x0, y1 } };

    static NetTopologySuite.Geometries.GeometryFactory MeshSlicer() => Geometry.MeshSlicer.Factory;

    public static DItem Text(string name, double x, double y, string text, string style, string align, double rotation = 0, bool center = false) => new()
    {
        Kind = ItemKind.Text, Name = name, X = x, Y = y, Text = text, ParaStyle = style, Align = align, Rotation = rotation, Center = center,
    };

    public static string Safe(string s) => new(s.Select(ch => char.IsLetterOrDigit(ch) ? ch : '_').ToArray());
}

/// <summary>Section feet (u, z) → artboard points (x right, y down).</summary>
public sealed class PageMap
{
    readonly double _x0, _y0, _zMax, _len;
    readonly bool _flip;
    public double K { get; }

    public PageMap(double x0, double y0, double k, double zMax, double length, bool flip)
    {
        _x0 = x0; _y0 = y0; K = k; _zMax = zMax; _len = length; _flip = flip;
    }

    public double X(double u) => _x0 + (_flip ? _len - u : u) * K;
    public double Y(double z) => _y0 + (_zMax - z) * K;
    public double[] P(double u, double z) => new[] { Math.Round(X(u), 3), Math.Round(Y(z), 3) };

    public DItem Line(string name, IEnumerable<(double X, double Y)> pts, string? stroke, double strokePt) => new()
    {
        Kind = ItemKind.Path, Name = name, Closed = false, Stroke = stroke, StrokePt = strokePt,
        Paths = new() { pts.Select(q => P(q.X, q.Y)).ToArray() },
    };

    public DItem Poly(string name, NtsGeometry g, string? fill, string? stroke, double strokePt) => Geom(name, g, fill, stroke, strokePt, true);

    /// <summary>Polygons become compound rings; lines become open paths.</summary>
    public DItem Geom(string name, NtsGeometry g, string? fill, string? stroke, double strokePt, bool closed)
    {
        var paths = new List<double[][]>();
        for (var i = 0; i < g.NumGeometries; i++)
        {
            var part = g.GetGeometryN(i);
            if (part is Polygon poly)
            {
                paths.Add(Ring(poly.ExteriorRing));
                paths.AddRange(poly.InteriorRings.Select(Ring));
            }
            else if (part is LineString ls) paths.Add(ls.Coordinates.Select(c => P(c.X, c.Y)).ToArray());
        }
        return new DItem { Kind = ItemKind.Path, Name = name, Closed = closed, Fill = fill, Stroke = stroke, StrokePt = strokePt, Paths = paths };
    }

    double[][] Ring(LineString r) => r.Coordinates.Take(r.NumPoints - 1).Select(c => P(c.X, c.Y)).ToArray();
}
