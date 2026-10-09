using NetTopologySuite.Geometries;
using PD.Core.Drawing;
using PD.Core.Model;
using PD.Core.Settings;
using NtsGeometry = NetTopologySuite.Geometries.Geometry;

namespace PD.Core.Sections;

/// <summary>PRD 5.3: turns <see cref="SectionGeometry"/> into a one-artboard Illustrator drawing.</summary>
public static class SectionDrawing
{
    const double LevelColumnPt = 78, DimColumnPt = 46, TitleBandPt = 44;

    public static PD.Core.Drawing.Drawing Build(ExportContext ctx, SectionSetting sec)
    {
        var r = ctx.Result;
        var (ax, ay, _) = r.Frame.ToLocal(sec.Line[0], sec.Line[1], 0);
        var (bx, by, _) = r.Frame.ToLocal(sec.Line[2], sec.Line[3], 0);
        var plane = new Geometry.SectionPlane(ax, ay, bx, by, sec.LookLeft);
        var depth = sec.DepthFt > 0 ? sec.DepthFt : ctx.Rules.Output.SectionDepthFt;
        var g = SectionGeometry.Build(r, plane, depth, ctx.Rules, ctx.Style);
        var st = ctx.Style.Section;

        var d = new PD.Core.Drawing.Drawing { FileName = $"02_Section_{Safe(sec.Name)}.ai" };
        ctx.AddStandardStyles(d);
        foreach (var n in new[] { "PD_GROUND", "PD_LEVELS", "PD_BEYOND", "PD_SLAB", "PD_PROGRAM", "FACADE", "PD_OUTLINE", "PD_LABELS", "PD_ANNOTATION" })
            d.Layer(n);

        // Page fit: drawing scale unless the cut does not fit the artboard.
        var zMin = g.Ground - st.GroundBandDepthFt;
        var zMax = new[] { g.Outline, }.Concat(g.Beyond.Select(b => b.Geometry)).Concat(g.BuildingCuts.Select(b => b.Geometry))
            .Where(x => !x.IsEmpty).Select(x => x.EnvelopeInternal.MaxY).DefaultIfEmpty(g.Ground + 30).Max();
        var (abW, abH) = ctx.ArtboardSize;
        var m = ctx.Style.Page.MarginPt;
        var availW = abW - 2 * m - LevelColumnPt - DimColumnPt;
        var availH = abH - 2 * m - TitleBandPt;
        var k = ctx.PtPerFt;
        var fit = Math.Min(availW / plane.Length, availH / (zMax - zMin));
        if (fit < k)
        {
            d.Notes.Add($"Section {sec.Name}: {ctx.Settings.Output.Scale} does not fit {ctx.Settings.Output.Artboard}; scaled to fit.");
            k = fit;
        }
        var x0 = m + LevelColumnPt + (availW - plane.Length * k) / 2;
        var y0 = m + (availH - (zMax - zMin) * k) / 2;
        var map = new PageMap(x0, y0, k, zMax, plane.Length, flip: false);
        d.Artboards.Add(new Artboard($"SECTION {sec.Name}", abW, abH));
        var p = $"SEC_{Safe(sec.Name)}_";

        // Ground band and line.
        d.Layer("PD_GROUND").Items.Add(map.Poly(p + "GROUND", SectionGeometry.Rect(0, plane.Length, zMin, g.Ground),
            d.Swatch("BG_GROUND", ctx.Style.Background.GetValueOrDefault("groundBand")?.Fill ?? "#E5E5E5"), null, 0));
        d.Layer("PD_GROUND").Items.Add(map.Line(p + "GROUND_LINE", new[] { (0.0, g.Ground), (plane.Length, g.Ground) }, d.Gray("#000000"), ctx.Style.Strokes.RoomPt));

        // Level lines and the left level column (name + floor-to-floor height).
        var main = g.Levels.Where(l => !l.IsMezzanine).ToList();
        for (var i = 0; i < g.Levels.Count; i++)
        {
            var l = g.Levels[i];
            d.Layer("PD_LEVELS").Items.Add(map.Line(p + "LVL_" + l.Name, new[] { (0.0, l.Elevation), (plane.Length, l.Elevation) }, d.Gray(st.LevelLineStroke), st.LevelLineStrokePt));
            if (!st.LevelColumn) continue;
            var next = main.FirstOrDefault(x => x.Elevation > l.Elevation + 0.01);
            var label = next == null || l.IsRoof ? $"{l.Name} {(l.IsRoof ? "ROOF" : "")}".Trim() : $"{l.Name}  {FeetInches.Format(next.Elevation - l.Elevation)}";
            d.Layer("PD_ANNOTATION").Items.Add(Text(p + "LVL_" + l.Name + "_TXT", map.X(0) - 8, map.Y(l.Elevation) - 2, label, "PD Note", "right"));
        }

        // Beyond: dashed outlines (programs with their level lines, context buildings with name), facade grid in light grey.
        var beyond = ctx.Style.Strokes.Beyond;
        foreach (var (b, i) in g.Beyond.Select((b, i) => (b, i)))
        {
            if (b.Geometry.IsEmpty) continue;
            if (b.Kind == BeyondKind.Facade)
            {
                d.Layer("FACADE").Items.Add(map.Poly(p + $"FACADE_BEYOND_{i:000}", b.Geometry, null, d.Gray(st.FacadeBeyond), st.FacadeBeyondPt));
                continue;
            }
            var item = map.Poly(p + $"BEYOND_{i:000}", b.Geometry, null, d.Gray(beyond.Stroke), beyond.StrokePt ?? 0.5);
            item.Dash = beyond.Dash;
            d.Layer("PD_BEYOND").Items.Add(item);
            if (b.Kind == BeyondKind.Program)
                foreach (var l in g.Levels)
                {
                    var cut = b.Geometry.Intersection(MeshSlicer().CreateLineString(new[] { new Coordinate(-1, l.Elevation), new Coordinate(plane.Length + 1, l.Elevation) }));
                    if (cut.IsEmpty || cut.Length < 0.5) continue;
                    var li = map.Geom(p + $"BEYOND_{i:000}_{l.Name}", cut, null, d.Gray(beyond.Stroke), beyond.StrokePt ?? 0.5, closed: false);
                    li.Dash = beyond.Dash;
                    d.Layer("PD_BEYOND").Items.Add(li);
                }
            if (b.Kind == BeyondKind.Building && b.Label.Length > 0)
            {
                var e = b.Geometry.EnvelopeInternal;
                d.Layer("PD_LABELS").Items.Add(Text(p + $"BEYOND_{i:000}_LBL", map.X((e.MinX + e.MaxX) / 2), map.Y(e.MaxY) - 4, b.Label, "PD Label", "center"));
            }
        }
        foreach (var (id, geom) in g.BuildingCuts.Where(x => !x.Geometry.IsEmpty))
            d.Layer("PD_OUTLINE").Items.Add(map.Poly(p + "CTX_" + id[..Math.Min(8, id.Length)], geom, null, d.Gray("#000000"), ctx.Style.Strokes.CutPt));

        // Slabs: real cut poche, or the generated double line where no slab is cut.
        foreach (var (s, i) in g.Slabs.Select((s, i) => (s, i)))
            d.Layer("PD_SLAB").Items.Add(map.Poly(p + $"SLAB_{i:00}", s, d.Swatch("PD_SLAB", st.SlabFill), null, 0));
        foreach (var (u0, u1, z) in g.NoSlabLines)
        {
            var gapFt = st.NoSlabLineGapPt / k;
            foreach (var (zz, tag) in new[] { (z, "A"), (z - gapFt, "B") })
                d.Layer("PD_SLAB").Items.Add(map.Line(p + $"SLABLINE_{FeetInches.Format(z)}_{tag}", new[] { (Math.Max(0, u0), zz), (Math.Min(plane.Length, u1), zz) }, d.Gray("#000000"), ctx.Style.Strokes.BackgroundPt));
        }

        SectionBars.Add(d, ctx, g, map, p);

        // Cut outline, facade cuts, property lines, height dimension, title.
        if (!g.Outline.IsEmpty)
            d.Layer("PD_OUTLINE").Items.Add(map.Poly(p + "OUTLINE", g.Outline, null, d.Gray("#000000"), ctx.Style.Strokes.CutPt));
        foreach (var (c, i) in g.FacadeCut.Select((c, i) => (c, i)))
            d.Layer("FACADE").Items.Add(map.Line(p + $"FACADE_CUT_{i:000}", c, d.Gray("#000000"), st.FacadeCutPt));
        SectionAnnotation.Add(d, ctx, g, map, p, sec, zMin, zMax);
        return d;
    }

    static NetTopologySuite.Geometries.GeometryFactory MeshSlicer() => Geometry.MeshSlicer.Factory;

    public static DItem Text(string name, double x, double y, string text, string style, string align, double rotation = 0) => new()
    {
        Kind = ItemKind.Text, Name = name, X = x, Y = y, Text = text, ParaStyle = style, Align = align, Rotation = rotation,
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
