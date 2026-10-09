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
        // Bottom → top: what is farther away is painted first and hidden by what is nearer.
        // Level lines run over everything behind the cut (incl. cut context) but under our colour blocks.
        Views.ProjectedView.AddLayers(d, g.View);
        foreach (var n in new[] { "FACADE", "PD_CONTEXT_CUT", "PD_LEVELS", "PD_PROGRAM", "PD_OUTLINE", "PD_SLAB", "PD_GROUND", "PD_LABELS", "PD_ANNOTATION" })
            d.Layer(n);

        // True scale always. Text and annotation spacing scale with the drawing (style reference scale = 1).
        var k = ctx.PtPerFt;
        var groundT = st.SlabPt * st.GroundSlabFactor;
        var geoms = new[] { g.Outline }.Concat(g.View.Select(b => b.Geometry)).Concat(g.BuildingCuts.Select(b => b.Geometry)).Where(x => !x.IsEmpty).ToList();
        var zMax = geoms.Select(x => x.EnvelopeInternal.MaxY).DefaultIfEmpty(g.Ground + 30).Max();
        var zMin = Math.Min(g.Ground, geoms.Select(x => x.EnvelopeInternal.MinY).DefaultIfEmpty(g.Ground).Min());
        double w = plane.Length * k, h = (zMax - zMin) * k + groundT / 2, m = pg.MarginPt;
        var colL = st.LevelColumn ? ctx.Pt(pg.LevelColumnPt) : 0;
        var colR = st.HeightDimension ? ctx.Pt(pg.DimColumnPt) : 0;
        var titleBand = ctx.Pt(pg.TitleBandPt);
        double abW, abH, x0, y0;
        if (ctx.ArtboardSize is { } fixedSize)
        {
            (abW, abH) = fixedSize;
            x0 = m + colL + (abW - 2 * m - colL - colR - w) / 2;
            y0 = m + (abH - 2 * m - titleBand - h) / 2;
            if (x0 < m + colL - 0.5 || y0 < m - 0.5)
                d.Notes.Add($"Section {sec.Name} at {ctx.Settings.Output.Scale} is larger than {ctx.Settings.Output.Artboard}; use \"Fit content\" or a smaller scale.");
        }
        else
        {
            abW = 2 * m + colL + w + colR;
            abH = 2 * m + h + titleBand;
            x0 = m + colL;
            y0 = m;
        }
        var map = new PageMap(x0, y0, k, zMax, plane.Length, flip: false);
        d.Artboards.Add(new Artboard($"SECTION {sec.Name}", Math.Ceiling(abW), Math.Ceiling(abH)));
        var p = $"SEC_{Safe(sec.Name)}_";

        // Level lines and the left level column (name + floor-to-floor height).
        var main = g.Levels.Where(l => !l.IsMezzanine).ToList();
        foreach (var l in g.Levels)
        {
            d.Layer("PD_LEVELS").Items.Add(map.Line(p + "LVL_" + l.Name, new[] { (0.0, l.Elevation), (plane.Length, l.Elevation) }, d.Gray(st.LevelLineStroke), st.LevelLineStrokePt));
            if (!st.LevelColumn) continue;
            var next = main.FirstOrDefault(x => x.Elevation > l.Elevation + 0.01);
            var label = next == null || l.IsRoof ? $"{l.Name} ROOF" : $"{l.Name}  {FeetInches.Format(next.Elevation - l.Elevation)}";
            d.Layer("PD_ANNOTATION").Items.Add(Text(p + "LVL_" + l.Name + "_TXT", x0 - colL + ctx.Pt(4), map.Y(l.Elevation) - ctx.Pt(2), label, "PD Note", "left"));
        }

        // Behind the cut: our building as an opaque grey Make2D-like elevation, other buildings white with a dashed outline.
        Views.ProjectedView.Draw(d, ctx, map, p + "VIEW_", g.View);

        // Other buildings cut by the section: solid section (white fill) with a heavy outline, above everything behind.
        foreach (var (id, geom) in g.BuildingCuts.Where(x => !x.Geometry.IsEmpty))
            d.Layer("PD_CONTEXT_CUT").Items.Add(map.Poly(p + "CONTEXT_CUT_" + id[..Math.Min(8, id.Length)], geom,
                d.Swatch("PD_CUT_FILL", st.ContextCutFill), d.Gray(st.ContextCutStroke), st.ContextCutStrokePt));

        SectionBars.Add(d, ctx, g, map, p);

        // Our building: thin wall line, visual slabs (fixed weight, centred on the floor line), facade cuts.
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

        // Ground line: slab weight × factor, centred on grade; basement levels below it draw like any other level.
        d.Layer("PD_GROUND").Items.Add(new DItem
        {
            Kind = ItemKind.Path, Name = p + "GROUND", Fill = d.Swatch("BG_GROUND", st.GroundColor),
            Paths = new() { RectPts(map.X(0), map.Y(g.Ground) - groundT / 2, map.X(plane.Length), map.Y(g.Ground) + groundT / 2) },
        });

        SectionAnnotation.Add(d, ctx, g, map, p, sec, g.Ground, zMax, titleX: x0, titleY: map.Y(zMin) + groundT / 2 + ctx.Pt(26));
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
