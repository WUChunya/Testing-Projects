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
        var st = ctx.Style.Section;
        var pg = ctx.Style.Page;
        // A fixed artboard (user rev 0.8.4: 11x17 at 1/64"): a longer line is cut down around the building.
        if (ctx.ArtboardSize is { } ab0)
        {
            var cols = (st.LevelColumn || st.LevelMarkers ? ctx.Pt(pg.LevelColumnPt) : 0) + (st.HeightDimension ? ctx.Pt(pg.DimColumnPt) : 0);
            plane = plane.Fit((ab0.W - 2 * pg.MarginPt - cols) / ctx.PtPerFt, PD.Core.Plans.PlanBackground.Footprint(r));
        }
        var g = SectionGeometry.Build(r, plane, depth, ctx.Rules, ctx.Style);

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
        // Roof screens cut by the section (user rev 0.8.3) replace the grey penthouse block of that level.
        var screenCuts = ScreenCuts(ctx, plane);
        // A level already marked at that height (the roof level on the penthouse roof) is not marked twice.
        bool Marked(double z) => g.Levels.Any(l => Math.Abs(l.Elevation - z) < 1.0);
        if (st.LevelMarkers && screenCuts.Count > 0 && !Marked(screenCuts.Max(c => c.E.Z1)))
            LevelMarkers.One(d, ctx, map, p, "PENTHOUSE_TOP", st.PenthouseTopLabel, screenCuts.Max(c => c.E.Z1), x0 - colL, x0 + w);
        else if (st.LevelMarkers && screenCuts.Count == 0 && Penthouse(ctx, g) is { } top && !Marked(top.Z1))
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
        if (pent != null && screenCuts.Any(c => Math.Abs(c.E.Z0 - pent.Z0) < 3)) pent = null;
        var labelBoxes = SectionBars.Add(d, ctx, g, map, p, pent?.Bars);
        if (pent != null) DrawPenthouse(d, ctx, g, map, p, pent, labelBoxes);
        DrawScreens(d, ctx, g, map, p, screenCuts, labelBoxes);

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
        People(d, ctx, g, map, p, plane, labelBoxes);

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
        // Modelled trees within the view depth, and the generated ones (street, median, green-area trees: user rev 0.8.3)
        // within entourage.sectionTreeBandFt beyond the cut.
        var trees = ctx.Result.Local.Objects
            .Where(o => o.Role is PD.Core.Classification.Roles.Tree or PD.Core.Classification.Roles.NcTree && !ctx.Result.Stray.Contains(o.Id))
            .Select(o => (Key: o.Id, X: (o.BBox.MinX + o.BBox.MaxX) / 2, Y: (o.BBox.MinY + o.BBox.MaxY) / 2,
                H: o.BBox.SizeZ >= 8 ? Math.Clamp(o.BBox.SizeZ, e.TreeHeightFt[0], e.TreeHeightFt[1]) : e.TreeDefaultHeightFt))
            .Where(c => plane.W(c.X, c.Y) <= depth)
            .ToList();
        var band = Math.Min(depth, e.SectionTreeBandFt);
        if (band > 0)
        {
            var strip = new NetTopologySuite.Geometries.Envelope(plane.Ax, plane.Ax + plane.Dx * plane.Length, plane.Ay, plane.Ay + plane.Dy * plane.Length);
            strip.ExpandToInclude(plane.Ax + plane.Nx * band, plane.Ay + plane.Ny * band);
            strip.ExpandToInclude(plane.Ax + plane.Dx * plane.Length + plane.Nx * band, plane.Ay + plane.Dy * plane.Length + plane.Ny * band);
            foreach (var t in PD.Core.Plans.PlanBackground.Collect(ctx, strip).Where(i => i.Role is PD.Core.Classification.Roles.Tree or PD.Core.Classification.Roles.NcTree
                         && (i.Name.StartsWith("STREET_TREE") || i.Name.StartsWith("MEDIAN_TREE") || i.Name.StartsWith("GREEN_TREE"))))
            {
                var c = t.Geometry.Centroid.Coordinate;
                if (plane.W(c.X, c.Y) <= band) trees.Add(($"{c.X:0.0}_{c.Y:0.0}", c.X, c.Y, e.TreeDefaultHeightFt));
            }
        }
        var n = 0;
        foreach (var (key, x, y, h) in trees.Select(c => (c.Key, c.X, c.Y, c.H)).Where(c => plane.W(c.X, c.Y) > 0 && plane.U(c.X, c.Y) > -20 && plane.U(c.X, c.Y) < plane.Length + 20)
                     .OrderByDescending(c => plane.W(c.X, c.Y)).ThenBy(c => c.Key, StringComparer.Ordinal))
        {
            var rng = new Random(PD.Core.Entourage.EntouragePlacer.Mix(PD.Core.Entourage.EntouragePlacer.Seed(ctx), "TREE_" + key));
            layer.Items.Add(PD.Core.Entourage.EntourageSymbols.Tree(d, e, $"{p}TREE_{++n:000}", map.X(plane.U(x, y)), map.Y(ground), h * map.K, rng));
        }
    }

    /// <summary>
    /// PRD 4: people stand on the floor of each cut bar (never taller than the bar, so nobody pokes into the level
    /// above) and on the ground outside the building.
    /// </summary>
    static void People(PD.Core.Drawing.Drawing d, ExportContext ctx, SectionGeometry g, PageMap map, string p, Geometry.SectionPlane plane,
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
        // Outside: on the ground line where the cut crosses hardscape / sidewalk (never a road or a building).
        foreach (var (u0, u1, tag) in WalkableAlong(ctx, plane, ctx.Style.Entourage.SectionBandFt))
            foreach (var f in PD.Core.Entourage.EntouragePlacer.AlongLine(ctx, u0, u1, "", p + "GROUND" + tag, outdoors: true))
                layer.Items.Add(PD.Core.Entourage.EntourageSymbols.Standing(d, e, $"{p}PERSON_{++n:000}",
                    map.X(f.X), map.Y(g.Ground), e.HeightFt * map.K, f.Variant, f.Mirror));
    }


    /// <summary>
    /// Stretches of the ground line (u from / to) where people may stand: the strip from the plane to
    /// <paramref name="band"/> ft beyond it, walkable ground only (<see cref="PD.Core.Plans.PlanBackground.Walkable"/>).
    /// </summary>
    public static List<(double U0, double U1, string Tag)> WalkableAlong(ExportContext ctx, Geometry.SectionPlane plane, double band)
    {
        var strip = PD.Core.Geometry.MeshSlicer.Factory.CreatePolygon(new[]
        {
            new NetTopologySuite.Geometries.Coordinate(plane.Ax, plane.Ay),
            new NetTopologySuite.Geometries.Coordinate(plane.Ax + plane.Dx * plane.Length, plane.Ay + plane.Dy * plane.Length),
            new NetTopologySuite.Geometries.Coordinate(plane.Ax + plane.Dx * plane.Length + plane.Nx * band, plane.Ay + plane.Dy * plane.Length + plane.Ny * band),
            new NetTopologySuite.Geometries.Coordinate(plane.Ax + plane.Nx * band, plane.Ay + plane.Ny * band),
            new NetTopologySuite.Geometries.Coordinate(plane.Ax, plane.Ay),
        });
        var env = strip.EnvelopeInternal.Copy();
        env.ExpandBy(1);
        var walk = PD.Core.Plans.PlanBackground.Walkable(PD.Core.Plans.PlanBackground.Collect(ctx, env), PD.Core.Plans.PlanBackground.Footprint(ctx.Result));
        NtsGeometry hit;
        try { hit = PD.Core.Geometry.MeshSlicer.Polygonal(walk.Intersection(strip)); }
        catch (Exception e) when (e is NetTopologySuite.Geometries.TopologyException or ArgumentException) { return new(); }
        var spans = new List<(double A, double B)>();
        for (var i = 0; i < hit.NumGeometries; i++)
        {
            var us = hit.GetGeometryN(i).Coordinates.Select(c => plane.U(c.X, c.Y)).ToList();
            if (us.Count > 0 && us.Max() - us.Min() > 2) spans.Add((Math.Max(0, us.Min()), Math.Min(plane.Length, us.Max())));
        }
        var merged = new List<(double U0, double U1, string Tag)>();
        foreach (var (a, b) in spans.OrderBy(x => x.A))
            if (merged.Count > 0 && a <= merged[^1].U1) merged[^1] = (merged[^1].U0, Math.Max(merged[^1].U1, b), merged[^1].Tag);
            else merged.Add((a, b, merged.Count.ToString()));
        return merged;
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

    /// <summary>Screen enclosures the cut crosses: the u-ranges of their walls and of their floor.</summary>
    public static List<(PD.Core.Analysis.ScreenEnclosure E, List<(double U0, double U1)> Walls, List<(double U0, double U1)> Floor)> ScreenCuts(
        ExportContext ctx, Geometry.SectionPlane plane)
    {
        var line = plane.PlanLine();
        var cuts = new List<(PD.Core.Analysis.ScreenEnclosure, List<(double, double)>, List<(double, double)>)>();
        foreach (var e in PD.Core.Analysis.ScreenEnclosures.For(ctx))
        {
            List<(double, double)> Along(NtsGeometry g)
            {
                try { return SectionGeometry.Segments(Geometry.MeshSlicer.Lineal(g.Intersection(line)), plane).OrderBy(x => x.Item1).ToList(); }
                catch (Exception ex) when (ex is NetTopologySuite.Geometries.TopologyException or ArgumentException) { return new(); }
            }
            var walls = Along(e.Walls);
            var floor = e.Encloses ? Along(e.Floor) : new List<(double, double)>();
            if (walls.Count > 0 || floor.Count > 0) cuts.Add((e, walls, floor));
        }
        return cuts;
    }

    /// <summary>
    /// A cut roof screen (user rev 0.8.3): its walls solid from foot to top, the penthouse volume it stands for
    /// completed with dashed lines where the screen does not close it (the open top, an open side), the PENTHOUSE
    /// name inside. The floor is the roof slab below (drawn with the slabs).
    /// </summary>
    static void DrawScreens(PD.Core.Drawing.Drawing d, ExportContext ctx, SectionGeometry g, PageMap map, string p,
        List<(PD.Core.Analysis.ScreenEnclosure E, List<(double U0, double U1)> Walls, List<(double U0, double U1)> Floor)> cuts,
        List<(double X0, double X1, double Y0, double Y1)> labels)
    {
        var st = ctx.Style.Section;
        var sc = ctx.Style.Screen;
        var layer = d.Layer("PD_SCREEN");
        var wall = d.Swatch("NC_SCREEN_WALL_SECTION", sc.SectionWallFill);
        var dash = d.Gray(sc.SectionDashStroke);
        foreach (var (e, walls, floor) in cuts)
        {
            var id = $"{p}SCREEN_{e.Index:00}";
            foreach (var ((u0, u1), k) in walls.Select((w, k) => (w, k)))
                layer.Items.Add(new DItem
                {
                    Kind = ItemKind.Path, Name = $"{id}_WALL_{k + 1:00}", Fill = wall,
                    Paths = new() { RectPts(Math.Min(map.X(u0), map.X(u1)), map.Y(e.Z1), Math.Max(map.X(u0), map.X(u1)), map.Y(e.Z0)) },
                });
            var spans = floor.Count > 0 ? floor : walls;
            double a = spans.Min(x => x.U0), b = spans.Max(x => x.U1);
            if (b - a < 1) continue;
            // The volume's outline where no wall stands: the top always (open), a side without a wall at its edge.
            var pts = new List<(double, double)[]> { new[] { (a, e.Z1), (b, e.Z1) } };
            if (!walls.Any(w => w.U0 <= a + 1)) pts.Add(new[] { (a, e.Z0), (a, e.Z1) });
            if (!walls.Any(w => w.U1 >= b - 1)) pts.Add(new[] { (b, e.Z0), (b, e.Z1) });
            foreach (var (seg, k) in pts.Select((x, k) => (x, k)))
            {
                var line = map.Line($"{id}_OPEN_{k + 1:00}", seg, dash, sc.SectionDashPt);
                line.Dash = sc.SectionDash;
                layer.Items.Add(line);
            }
            // The penthouse name just under the screen top, in the widest stretch clear of the walls and of the bars
            // (cores, equipment) standing at that height.
            var size = Math.Clamp(st.LabelSizeRatio * (e.Z1 - e.Z0) * map.K * 0.5, st.LabelSizeMin, st.LabelSizeMax);
            var cy = map.Y(e.Z1) + size * 1.2;
            var zLabel = e.Z1 - size * 1.2 / map.K;
            var blocked = walls.Concat(g.Bars.Where(x => x.Z1 > zLabel - size / map.K && x.Z0 < zLabel + size / map.K).Select(x => (x.U0, x.U1)))
                .OrderBy(x => x.Item1).ToList();
            var (fa, fb, cursor) = (a, a, a);
            foreach (var (u0, u1) in blocked)
            {
                if (u0 > cursor && u0 - cursor > fb - fa) (fa, fb) = (cursor, Math.Min(u0, b));
                cursor = Math.Max(cursor, u1);
            }
            if (b - cursor > fb - fa) (fa, fb) = (cursor, b);
            double xa = Math.Min(map.X(fa), map.X(fb)), xb = Math.Max(map.X(fa), map.X(fb));
            if (SectionBars.Fit(st.PenthouseLabel.ToUpperInvariant(), size, xb - xa, st.LabelInsetPt) is not { } text) continue;
            var t = Text(id + "_LBL", (xa + xb) / 2, cy, text, "PD Section Label", "center", center: true);
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
