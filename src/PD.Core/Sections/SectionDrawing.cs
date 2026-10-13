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
        // Program sections have their own scale and absolute text sizes (docs/SITE_STYLE.md §4).
        if (ctx.TextScaleOverride == null) ctx = ctx.ForSection();
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
        // Trees stand beyond the cut: behind the cut colour blocks, in front of what is seen further away.
        foreach (var n in new[] { "BG_TREE", "FACADE", "PD_CONTEXT_CUT", "PD_PROGRAM_BG", "PD_LEVELS", "PD_OUTLINE", "PD_PROGRAM", "PD_SLAB", "PD_MASSING", "PD_GROUND", "PD_LABELS", PD.Core.Entourage.EntouragePlacer.Layer, "PD_ANNOTATION" })
            d.Layer(n);

        // True scale always. Text and annotation spacing scale with the drawing (style reference scale = 1).
        var k = ctx.PtPerFt;
        var groundT = st.GroundPt;
        var geoms = new[] { g.Outline }.Concat(g.View.Select(b => b.Geometry)).Concat(g.BuildingCuts.Select(b => b.Geometry)).Where(x => !x.IsEmpty).ToList();
        var zMax = geoms.Select(x => x.EnvelopeInternal.MaxY).DefaultIfEmpty(g.Ground + 30).Max();
        var zMin = Math.Min(g.Ground, geoms.Select(x => x.EnvelopeInternal.MinY).DefaultIfEmpty(g.Ground).Min());
        double w = plane.Length * k, h = (zMax - zMin) * k + groundT / 2, m = pg.MarginPt;
        var colL = st.LevelColumn || st.LevelMarkers ? ctx.Pt(pg.LevelColumnPt) : 0;
        var colR = st.HeightDimension ? ctx.Pt(pg.DimColumnPt) : 0;
        var titleBand = ctx.Pt(pg.TitleBandPt);
        double abW, abH, x0, y0;
        if (ctx.ArtboardSize is { } fixedSize)
        {
            (abW, abH) = fixedSize;
            x0 = m + colL + (abW - 2 * m - colL - colR - w) / 2;
            y0 = m + (abH - 2 * m - titleBand - h) / 2;
            if (x0 < m + colL - 0.5 || y0 < m - 0.5)
                d.Notes.Add($"Section {sec.Name} at {ctx.ScaleText} is larger than {ctx.Settings.Output.Artboard}; use \"Fit content\" or a smaller scale.");
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
            // With level markers the marker's dashed line runs across the whole drawing instead (same as elevations).
            if (!st.LevelMarkers)
                d.Layer("PD_LEVELS").Items.Add(map.Line(p + "LVL_" + l.Name, new[] { (0.0, l.Elevation), (plane.Length, l.Elevation) }, d.Gray(st.LevelLineStroke), st.LevelLineStrokePt));
            if (!st.LevelColumn || st.LevelMarkers) continue;
            var next = main.FirstOrDefault(x => x.Elevation > l.Elevation + 0.01);
            var label = next == null || l.IsRoof ? $"{l.Name} ROOF" : $"{l.Name}  {FeetInches.Format(next.Elevation - l.Elevation)}";
            d.Layer("PD_ANNOTATION").Items.Add(Text(p + "LVL_" + l.Name + "_TXT", x0 - colL + ctx.Pt(4), map.Y(l.Elevation) - ctx.Pt(2), label, "PD Note", "left"));
        }

        // ▼ markers, level names and elevations in the left column, dashed to the cut (as in elevations).
        if (st.LevelMarkers && !g.Outline.IsEmpty)
            LevelMarkers.Add(d, ctx, map, p, g.Levels, x0 - colL, x0 + w, zMax);
        if (st.LevelMarkers && Penthouse(ctx, g) is { } top)
            LevelMarkers.One(d, ctx, map, p, "PENTHOUSE_TOP", st.PenthouseTopLabel, top.Z1, x0 - colL, x0 + w);

        // Behind the cut: our building as an opaque grey Make2D-like elevation, other buildings white with a dashed outline.
        Views.ProjectedView.Draw(d, ctx, map, p + "VIEW_", g.View);

        // Other buildings cut by the section: solid section (white fill) with a heavy outline, above everything behind.
        foreach (var (id, geom) in g.BuildingCuts.Where(x => !x.Geometry.IsEmpty))
            d.Layer("PD_CONTEXT_CUT").Items.Add(map.Poly(p + "CONTEXT_CUT_" + id[..Math.Min(8, id.Length)], geom,
                d.Swatch("PD_CUT_FILL", st.ContextCutFill), d.Gray(st.ContextCutStroke), st.ContextCutStrokePt));

        // Opaque white backing behind the colour blocks: the white gaps between blocks must not show what is behind the cut.
        if (!g.Outline.IsEmpty && st.ProgramBackground is { Length: > 0 } bg)
            d.Layer("PD_PROGRAM_BG").Items.Add(map.Poly(p + "PROGRAM_BG", g.Outline, d.Swatch("PD_PROGRAM_BG", bg), null, 0));
        var pent = Penthouse(ctx, g);
        var labelBoxes = SectionBars.Add(d, ctx, g, map, p, pent?.Bars);
        if (pent != null) DrawPenthouse(d, ctx, g, map, p, pent, labelBoxes);

        // Envelope: thin light-grey line under the bars; slabs: white strips 0.14 × floor with a dark outline, 2 ft past the bars.
        if (!g.Outline.IsEmpty)
            d.Layer("PD_OUTLINE").Items.Add(map.Poly(p + "OUTLINE", g.Outline, null, d.Gray(st.EnvelopeStroke), st.EnvelopePt));
        foreach (var (c, i) in g.FacadeCut.Select((c, i) => (c, i)))
            d.Layer("FACADE").Items.Add(map.Line(p + $"FACADE_CUT_{i:000}", c, d.Gray("#000000"), st.FacadeCutPt));
        // Building envelope (NC.Massing): dashed cut outline over everything of ours.
        var mPaint = Plans.PlanBackground.PaintOf(ctx.Style, PD.Core.Classification.Roles.Massing, "view");
        foreach (var (c, i) in g.MassingCut.Select((c, i) => (c, i)))
        {
            var line = map.Line(p + $"MASSING_CUT_{i:000}", c, d.Swatch("NC_MASSING", mPaint.Stroke ?? "#4A4A4A"), mPaint.StrokePt ?? 0.75);
            line.Dash = mPaint.Dash;
            d.Layer("PD_MASSING").Items.Add(line);
        }
        var slab = d.Swatch("PD_SLAB", st.SlabFill);
        var slabLine = d.Gray(st.SlabStroke);
        var slabNames = new HashSet<string>();
        foreach (var (u0, u1, z) in g.SlabLines.OrderBy(x => x.Z).ThenBy(x => x.U0))
        {
            if (Math.Abs(z - g.Ground) < 0.5) continue; // the ground line is the ground floor's slab
            // No roof slab over the penthouse (user rev 0.8): its top gets an elevation marker instead.
            if (pent != null && z > pent.Z0 + 0.5 && pent.Ranges.Any(q => u1 > q.U0 + 0.1 && u0 < q.U1 - 0.1)) continue;
            var half = st.SlabHeightRatio * g.TypicalStory * k / 2; // one thickness for every slab of the drawing
            var xa = Math.Min(map.X(u0), map.X(u1)) - st.SlabOverhangFt * k;
            var xb = Math.Max(map.X(u0), map.X(u1)) + st.SlabOverhangFt * k;
            var name = p + $"SLAB_{FeetInches.Format(z)}_{u0:0}";
            for (var n = 2; !slabNames.Add(name); n++) name = p + $"SLAB_{FeetInches.Format(z)}_{u0:0}_{n}";
            d.Layer("PD_SLAB").Items.Add(new DItem
            {
                Kind = ItemKind.Path, Name = name, Fill = slab, Stroke = slabLine, StrokePt = st.SlabStrokePt,
                Paths = new() { RectPts(xa, map.Y(z) - half, xb, map.Y(z) + half) },
            });
        }

        // Ground: a 3 pt black line at grade (no fill); basement bars sit below it.
        d.Layer("PD_GROUND").Items.Add(map.Line(p + "GROUND", new[] { (0.0, g.Ground), (plane.Length, g.Ground) }, d.Gray(st.GroundStroke), st.GroundPt));

        Trees(d, ctx, map, p, plane, depth, g.Ground);
        People(d, ctx, g, map, p, plane.Length, labelBoxes);

        SectionAnnotation.Add(d, ctx, g, map, p, sec, g.Ground, zMax, titleX: x0, titleY: map.Y(zMin) + groundT / 2 + ctx.Pt(40));
        return d;
    }

    /// <summary>
    /// Trees behind the cut (SITE_STYLE §4 配景): office tree silhouettes (TREES.ai) standing on the ground at each
    /// NC/BG.Tree object, as tall as the object (flat plan trees get the default height), far ones first.
    /// </summary>
    static void Trees(PD.Core.Drawing.Drawing d, ExportContext ctx, PageMap map, string p, Geometry.SectionPlane plane, double depth, double ground)
    {
        var e = ctx.Style.Entourage;
        if (!PD.Core.Entourage.EntouragePlacer.Enabled(ctx) || PD.Core.Entourage.EntourageLibrary.Default.Trees.Count == 0) return;
        var layer = d.Layer("BG_TREE");
        var trees = ctx.Result.Local.Objects
            .Where(o => o.Role is PD.Core.Classification.Roles.Tree or PD.Core.Classification.Roles.NcTree && !ctx.Result.Stray.Contains(o.Id))
            .Select(o => (o, u: plane.U((o.BBox.MinX + o.BBox.MaxX) / 2, (o.BBox.MinY + o.BBox.MaxY) / 2), w: plane.W((o.BBox.MinX + o.BBox.MaxX) / 2, (o.BBox.MinY + o.BBox.MaxY) / 2)))
            .Where(c => c.w > 0 && c.w <= depth && c.u > -20 && c.u < plane.Length + 20)
            .OrderByDescending(c => c.w).ThenBy(c => c.o.Id, StringComparer.Ordinal)
            .ToList();
        var n = 0;
        foreach (var (o, u, _) in trees)
        {
            var h = o.BBox.SizeZ >= 8 ? Math.Clamp(o.BBox.SizeZ, e.TreeHeightFt[0], e.TreeHeightFt[1]) : e.TreeDefaultHeightFt;
            var rng = new Random(PD.Core.Entourage.EntouragePlacer.Mix(PD.Core.Entourage.EntouragePlacer.Seed(ctx), "TREE_" + o.Id));
            layer.Items.Add(PD.Core.Entourage.EntourageSymbols.Tree(d, e, $"{p}TREE_{++n:000}", map.X(u), map.Y(ground), h * map.K, rng));
        }
    }

    /// <summary>
    /// PRD 4: people stand on the floor of each cut bar (never taller than the bar, so nobody pokes into the level
    /// above) and on the ground outside the building.
    /// </summary>
    static void People(PD.Core.Drawing.Drawing d, ExportContext ctx, SectionGeometry g, PageMap map, string p, double length,
        List<(double X0, double X1, double Y0, double Y1)> labels)
    {
        if (!PD.Core.Entourage.EntouragePlacer.Enabled(ctx)) return;
        var e = ctx.Style.Entourage;
        var layer = d.Layer(PD.Core.Entourage.EntouragePlacer.Layer);
        var n = 0;
        foreach (var b in g.Bars)
        {
            // Standing on the slab's top face (slabs are drawn centred on the level line).
            var floor = b.Z0 + ctx.Style.Section.SlabHeightRatio * g.TypicalStory / 2;
            var h = Math.Min(e.HeightFt, (b.Z1 - floor) * 0.8);
            if (h < 3) continue;
            // Keep clear of this floor's label and number (half a figure either side).
            var yFeet = map.Y(floor);
            var half = 0.25 * h;
            var blocked = labels.Where(q => q.Y1 > yFeet - h * map.K && q.Y0 < yFeet)
                .Select(q => (map.U(q.X0) - half, map.U(q.X1) + half)).Select(q => (Math.Min(q.Item1, q.Item2), Math.Max(q.Item1, q.Item2))).ToList();
            foreach (var f in PD.Core.Entourage.EntouragePlacer.AlongLine(ctx, b.U0, b.U1, b.ProgramKey, p + b.MassId, blocked: blocked))
                layer.Items.Add(PD.Core.Entourage.EntourageSymbols.Standing(d, e, $"{p}PERSON_{++n:000}",
                    map.X(f.X), map.Y(floor), h * map.K, f.Variant, f.Mirror));
        }
        // Outside: on the ground line, left and right of the building footprint.
        double lo = g.Bars.Count == 0 ? length : g.Bars.Min(b => b.U0), hi = g.Bars.Count == 0 ? 0 : g.Bars.Max(b => b.U1);
        foreach (var (u0, u1, tag) in new[] { (0.0, lo, "L"), (hi, length, "R") })
            foreach (var f in PD.Core.Entourage.EntouragePlacer.AlongLine(ctx, u0, u1, "", p + "GROUND" + tag, outdoors: true))
                layer.Items.Add(PD.Core.Entourage.EntourageSymbols.Standing(d, e, $"{p}PERSON_{++n:000}",
                    map.X(f.X), map.Y(g.Ground), e.HeightFt * map.K, f.Variant, f.Mirror));
    }


    /// <summary>The penthouse: the top row of bars when all of them are section.penthousePrograms (shell, core…).</summary>
    public sealed record PenthouseRow(List<CutBar> Bars, List<(double U0, double U1)> Ranges, double Z0, double Z1);

    public static PenthouseRow? Penthouse(ExportContext ctx, SectionGeometry g)
    {
        var st = ctx.Style.Section;
        if (st.PenthousePrograms.Count == 0 || g.Bars.Count == 0) return null;
        var z0 = g.Bars.Max(b => b.Z0);
        var row = g.Bars.Where(b => b.Z0 > z0 - 0.5).ToList();
        if (row.Count == 0 || row.Any(b => !st.PenthousePrograms.Contains(b.ProgramKey, StringComparer.OrdinalIgnoreCase))) return null;
        // Only when something else is below it (a one-storey building has no penthouse).
        if (g.Bars.All(b => b.Z0 > z0 - 0.5)) return null;
        var ranges = new List<(double, double)>();
        foreach (var (a, b) in row.Select(b => (Math.Min(b.U0, b.U1), Math.Max(b.U0, b.U1))).OrderBy(x => x.Item1))
            if (ranges.Count > 0 && a <= ranges[^1].Item2 + 1) ranges[^1] = (ranges[^1].Item1, Math.Max(ranges[^1].Item2, b));
            else ranges.Add((a, b));
        return new PenthouseRow(row, ranges, z0, row.Max(b => b.Z1));
    }

    /// <summary>One grey block per continuous stretch of the penthouse, its name centred (or shortened, or none).</summary>
    static void DrawPenthouse(PD.Core.Drawing.Drawing d, ExportContext ctx, SectionGeometry g, PageMap map, string p, PenthouseRow pent,
        List<(double X0, double X1, double Y0, double Y1)> labels)
    {
        var st = ctx.Style.Section;
        var typical = g.TypicalStory;
        var halfSlab = st.SlabHeightRatio * typical / 2;
        var clear = Math.Max(0, (1 - st.SlabHeightRatio - st.BarHeightRatio) / 2 * typical);
        var size = Math.Min(st.LabelSizeMax, st.LabelSizeRatio * st.BarHeightRatio * typical * map.K);
        foreach (var ((u0, u1), i) in pent.Ranges.Select((r, i) => (r, i)))
        {
            double xa = Math.Min(map.X(u0), map.X(u1)), xb = Math.Max(map.X(u0), map.X(u1));
            double yTop = map.Y(pent.Z1), yBot = map.Y(pent.Z0 + halfSlab + clear);
            if (xb - xa < 1 || yBot - yTop < 1) continue;
            var name = p + $"PENTHOUSE_{i + 1:00}";
            d.Layer("PD_PROGRAM").Items.Add(new DItem
            {
                Kind = ItemKind.RoundRect, Name = name, X = xa, Y = yTop, W = xb - xa, H = yBot - yTop,
                R = Math.Min(st.BarCornerRatio * typical * map.K, Math.Min(xb - xa, yBot - yTop) / 2), Fill = d.Swatch("PD_PENTHOUSE", st.PenthouseFill),
            });
            if (SectionBars.Fit(st.PenthouseLabel.ToUpperInvariant(), size, xb - xa, st.LabelInsetPt) is not { } text) continue;
            var cy = (yTop + yBot) / 2;
            var t = Text(name + "_LBL", (xa + xb) / 2, cy, text, "PD Section Label", "center", center: true);
            t.SizePt = Math.Round(size, 2);
            d.Layer("PD_LABELS").Items.Add(t);
            var half = SectionBars.TextWidth(text, size) / 2;
            labels.Add(((xa + xb) / 2 - half, (xa + xb) / 2 + half, cy - size / 2, cy + size / 2));
        }
    }

    static double[][] RectPts(double x0, double y0, double x1, double y1) =>
        new[] { new[] { x0, y0 }, new[] { x1, y0 }, new[] { x1, y1 }, new[] { x0, y1 } };

    static NetTopologySuite.Geometries.GeometryFactory MeshSlicer() => Geometry.MeshSlicer.Factory;

    public static DItem Text(string name, double x, double y, string text, string style, string align, double rotation = 0, bool center = false) => new()
    {
        Kind = ItemKind.Text, Name = name, X = x, Y = y, Text = text, ParaStyle = style, Align = align, Rotation = rotation, Center = center,
    };

    /// <summary>
    /// A name worth printing: not empty and not a bare GIS / object id (CADMAPPER writes OSM ids such as 492879470).
    /// </summary>
    public static bool IsDisplayName(string? name) =>
        !string.IsNullOrWhiteSpace(name) && !System.Text.RegularExpressions.Regex.IsMatch(name.Trim(), "^[0-9 _.:#-]+$");

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
    /// <summary>Inverse of <see cref="X"/>.</summary>
    public double U(double x) => _flip ? _len - (x - _x0) / K : (x - _x0) / K;
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
