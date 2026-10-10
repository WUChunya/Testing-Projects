using NetTopologySuite.Algorithm;
using NetTopologySuite.Geometries;
using NetTopologySuite.Simplify;
using PD.Core.Analysis;
using PD.Core.Classification;
using PD.Core.Drawing;
using PD.Core.Geometry;
using PD.Core.Plans;
using PD.Core.Sections;
using NtsGeometry = NetTopologySuite.Geometries.Geometry;

namespace PD.Core.Axons;

/// <summary>
/// PRD 5.4: 03_Axon_Stacked.ai (whole program masses, tops in the program colour, sides darkened, facade veil, green
/// site) and 04_Axon_Exploded.ai (white plates pulled apart, program colours on the plates, labels laid on the plates
/// as live text, level column with leaders). True scale, isometric by default.
/// </summary>
public static class AxonDrawing
{
    /// <summary>One face to paint: screen polygon (feet), depth (larger = nearer), paint and name.</summary>
    sealed record Face(string Name, NtsGeometry Screen, double Depth, string? Fill, string Layer);

    public static PD.Core.Drawing.Drawing Build(ExportContext ctx, bool exploded)
    {
        var r = ctx.Result;
        var ax = ctx.Style.Axon;
        var proj = AxonProjection.FromAngles(ax.AzimuthDeg, ax.ElevationDeg, ctx.Settings.Output.AxonViewDirection);
        var d = new PD.Core.Drawing.Drawing { FileName = exploded ? "04_Axon_Exploded.ai" : "03_Axon_Stacked.ai" };
        ctx.AddStandardStyles(d);
        var blocks = ProgramBlocks.Build(r);
        if (blocks.Count == 0)
        {
            d.Notes.Add("Axon: no program blocks.");
            return d;
        }
        var ground = ctx.Rules.Levels.GroundElevationFt;
        var site = new Envelope();
        foreach (var b in blocks) site.ExpandToInclude(b.Geometry.EnvelopeInternal);
        site.ExpandBy(ax.SiteMarginFt);

        var levels = blocks.Select(b => b.LevelIndex).Distinct().OrderBy(i => i).ToList();
        var e0 = r.Levels[levels[0]].Elevation;
        // Exploded: plate spacing = factor × story height (heights above the first plate stretch by the factor).
        double Lift(double z) => exploded ? e0 + ax.ExplodeFactor * (z - e0) : z;
        var siteZ = Math.Min(Lift(ground), Lift(e0) - (exploded ? ax.SlabThicknessFt : 0));

        var paint = new List<(string Layer, DItemSpec Spec)>();
        // Site plate and site background (flat, always behind everything).
        var siteGeom = MeshSlicer.Factory.ToGeometry(site);
        paint.Add(("AXON_SITE", new DItemSpec("SITE", proj.Plane(siteGeom, siteZ), d.Swatch("AXON_SITE", ax.SiteFill), d.Gray(ax.SiteStroke), ax.SiteStroke == null ? 0 : ax.EdgePt)));
        foreach (var (bg, n) in PlanBackground.Collect(r, site).Where(b => b.Role != Roles.Building).Select((b, n) => (b, n)))
        {
            var (fill, stroke, pt, dash) = PlanBackground.Paint(d, ctx.Style, bg);
            paint.Add(("AXON_SITE", new DItemSpec($"BG_{n:000}", proj.Plane(bg.Geometry, siteZ), fill, stroke, pt, dash, !bg.IsLine)));
        }

        if (exploded) Exploded(d, ctx, proj, blocks, levels, Lift, paint);
        else Stacked(d, ctx, proj, blocks, paint);

        // Page: everything at true scale, fit content (or centred on a fixed artboard).
        var env = new Envelope();
        foreach (var (_, s) in paint) env.ExpandToInclude(s.Geometry.EnvelopeInternal);
        var k = ctx.PtPerFt;
        var pg = ctx.Style.Page;
        double m = pg.MarginPt, w = env.Width * k, h = env.Height * k;
        var colR = exploded ? ctx.Pt(ax.LevelColumnPt) : 0;
        var titleBand = ctx.Pt(pg.TitleBandPt);
        double abW, abH, x0, y0;
        if (ctx.ArtboardSize is { } fixedSize)
        {
            (abW, abH) = fixedSize;
            x0 = (abW - w - colR) / 2;
            y0 = m + (abH - 2 * m - titleBand - h) / 2;
            if (x0 < m - 0.5 || y0 < m - 0.5)
                d.Notes.Add($"{(exploded ? "Exploded" : "Stacked")} axon at {ctx.Settings.Output.Scale} is larger than {ctx.Settings.Output.Artboard}; use \"Fit content\" or a smaller scale.");
        }
        else
        {
            abW = 2 * m + w + colR;
            abH = 2 * m + h + titleBand;
            x0 = m;
            y0 = m;
        }
        d.Artboards.Add(new Artboard(exploded ? "AXON EXPLODED" : "AXON STACKED", Math.Ceiling(abW), Math.Ceiling(abH)));
        var map = new PageMap(x0 - env.MinX * k, y0, k, env.MaxY, env.Width, flip: false);
        var p = exploded ? "AXE_" : "AXS_";

        foreach (var (layer, s) in paint) d.Layer(layer);
        foreach (var n in new[] { "PD_LABELS", "PD_ANNOTATION" }) d.Layer(n);
        foreach (var (layer, s) in paint)
        {
            if (s.Text != null)
            {
                var (sx, sy) = (s.TextAt!.Value.X, s.TextAt!.Value.Y);
                var t = SectionDrawing.Text(p + s.Name, map.X(sx), map.Y(sy), s.Text, "PD Label", "center", center: true);
                t.Matrix = s.Matrix;
                d.Layer(layer).Items.Add(t);
                continue;
            }
            if (s.Geometry.IsEmpty) continue;
            var item = map.Geom(p + s.Name, s.Geometry, s.Fill, s.Stroke, s.StrokePt, s.Closed);
            item.Dash = s.Dash;
            item.Opacity = s.Opacity;
            d.Layer(layer).Items.Add(item);
        }

        if (exploded) LevelColumn(d, ctx, proj, levels, Lift, map, x0 + w + ctx.Pt(24), p);
        var ty = y0 + h + ctx.Pt(26);
        d.Layer("PD_ANNOTATION").Items.Add(SectionDrawing.Text(p + "TITLE", x0, ty, exploded ? ax.TitleExploded : ax.TitleStacked, "PD Title", "left"));
        d.Layer("PD_ANNOTATION").Items.Add(SectionDrawing.Text(p + "SCALE", x0, ty + ctx.Pt(12), $"SCALE {ctx.Settings.Output.Scale}", "PD Note", "left"));
        return d;
    }

    /// <summary>A path (or plane text) waiting for the page mapping; geometry in screen feet.</summary>
    sealed record DItemSpec(string Name, NtsGeometry Geometry, string? Fill, string? Stroke, double StrokePt, double[]? Dash = null, bool Closed = true,
        double Opacity = 1, string? Text = null, (double X, double Y)? TextAt = null, double[]? Matrix = null);

    /// <summary>Whole blocks as prisms; near hides far (each face trimmed by every nearer face), then one heavy silhouette.</summary>
    static void Stacked(PD.Core.Drawing.Drawing d, ExportContext ctx, AxonProjection proj, List<ProgramBlock> blocks, List<(string, DItemSpec)> paint)
    {
        var ax = ctx.Style.Axon;
        var faces = new List<Face>();
        var count = new Dictionary<string, int>();
        foreach (var b in blocks)
        {
            var id = Id(ctx, b, count);
            var (top, side1, side2) = Paints(d, ctx, b.ProgramKey);
            var plan = Simplify(b.Geometry);
            var n = 0;
            foreach (var poly in Polygons(plan))
            {
                var c = poly.Centroid.Coordinate;
                faces.Add(new Face($"{id}_TOP{(n > 0 ? $"_{n}" : "")}", proj.Plane(poly, b.Z1), proj.Depth(c.X, c.Y, b.Z1), top, "PD_PROGRAM"));
                var w = 0;
                foreach (var ring in Rings(poly))
                {
                    var pts = ring.Coordinates;
                    for (var i = 0; i + 1 < pts.Length; i++)
                    {
                        var (a, q) = (pts[i], pts[i + 1]);
                        var normal = (q.Y - a.Y, a.X - q.X, 0.0);
                        if (!proj.Faces(normal) || a.Distance(q) < 1e-3) continue;
                        var quad = proj.Polygon(new[] { (a.X, a.Y, b.Z0), (q.X, q.Y, b.Z0), (q.X, q.Y, b.Z1), (a.X, a.Y, b.Z1) });
                        var depth = proj.Depth((a.X + q.X) / 2, (a.Y + q.Y) / 2, (b.Z0 + b.Z1) / 2);
                        faces.Add(new Face($"{id}_SIDE{(n > 0 ? $"{n}_" : "")}{++w:000}", quad, depth, proj.ScreenX(normal) < 0 ? side1 : side2, "PD_PROGRAM"));
                    }
                }
                n++;
            }
        }

        // Near → far: trim by everything nearer; paint far → near.
        NtsGeometry? nearer = null;
        var visible = new List<(Face face, NtsGeometry geom)>();
        foreach (var f in faces.OrderByDescending(f => f.Depth))
        {
            var g = f.Screen;
            if (nearer != null && nearer.EnvelopeInternal.Intersects(g.EnvelopeInternal)) g = Safe(() => g.Difference(nearer), g);
            nearer = nearer == null ? f.Screen : Safe(() => nearer.Union(f.Screen), nearer);
            if (!g.IsEmpty && g.Area > 1e-3) visible.Add((f, g));
        }
        var edge = d.Gray(ax.EdgeStroke);
        foreach (var (f, g) in Enumerable.Reverse(visible))
            paint.Add((f.Layer, new DItemSpec(f.Name, g, f.Fill, edge, ax.EdgePt)));
        if (nearer != null)
            paint.Add(("AXON_OUTLINE", new DItemSpec("OUTLINE", nearer, null, d.Gray(ax.OutlineStroke), ax.OutlinePt)));

        // Facade veil: front faces of every facade layer, translucent white (A3: stacked axon 30 %).
        var opacity = ctx.Style.Facade.StackedAxonOpacity;
        var ids = ctx.Result.FacadeIds.ToHashSet();
        foreach (var group in ctx.Result.Local.Objects.Where(o => ids.Contains(o.Id) && o.Mesh != null && !ctx.Result.Stray.Contains(o.Id)).GroupBy(o => o.LayerPath).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var parts = group.Select(o => MeshProjection.Union(o.Mesh!, proj.Project, proj.Faces)).Where(x => !x.IsEmpty).ToList();
            if (parts.Count == 0 || opacity <= 0) continue;
            var veil = Safe(() => NetTopologySuite.Operation.Union.CascadedPolygonUnion.Union(parts), parts[0]);
            paint.Add(("FACADE", new DItemSpec("FACADE_" + SectionDrawing.Safe(group.Key).ToUpperInvariant(), veil, d.Swatch("AXON_FACADE_VEIL", ax.FacadeVeilFill),
                d.Gray(ax.FacadeVeilStroke), ax.FacadeVeilStrokePt, Opacity: opacity)));
        }
    }

    /// <summary>Plates bottom → top, each on its own layer so upper plates cover lower ones: slab sides, white top, program colours, labels.</summary>
    static void Exploded(PD.Core.Drawing.Drawing d, ExportContext ctx, AxonProjection proj, List<ProgramBlock> blocks, List<int> levels,
        Func<double, double> lift, List<(string, DItemSpec)> paint)
    {
        var r = ctx.Result;
        var ax = ctx.Style.Axon;
        var ps = ctx.Style.Plan;
        var label = ctx.Style.Fonts.Label;
        var sizeFt = ctx.Pt(label.SizePt) / ctx.PtPerFt;
        var count = new Dictionary<string, int>();
        foreach (var i in levels)
        {
            var lvl = r.Levels[i];
            var layer = "AXON_" + SectionDrawing.Safe(lvl.Name).ToUpperInvariant();
            var p = SectionDrawing.Safe(lvl.Name) + "_";
            var z = lift(lvl.Elevation);
            var mine = blocks.Where(b => b.LevelIndex == i).ToList();
            var plate = Simplify(Safe(() => NetTopologySuite.Operation.Union.CascadedPolygonUnion.Union(mine.Select(b => b.Geometry).ToList()), mine[0].Geometry));

            // Slab: front side faces (far → near), then the white top with a heavy outline.
            var sides = new List<(NtsGeometry g, double depth)>();
            foreach (var poly in Polygons(plate))
            foreach (var ring in Rings(poly))
            {
                var pts = ring.Coordinates;
                for (var n = 0; n + 1 < pts.Length; n++)
                {
                    var (a, q) = (pts[n], pts[n + 1]);
                    var normal = (q.Y - a.Y, a.X - q.X, 0.0);
                    if (!proj.Faces(normal) || a.Distance(q) < 1e-3) continue;
                    sides.Add((proj.Polygon(new[] { (a.X, a.Y, z - ax.SlabThicknessFt), (q.X, q.Y, z - ax.SlabThicknessFt), (q.X, q.Y, z), (a.X, a.Y, z) }),
                        proj.Depth((a.X + q.X) / 2, (a.Y + q.Y) / 2, z)));
                }
            }
            var slabFill = d.Swatch("AXON_SLAB", ax.SlabFill);
            var slabLine = d.Gray(ax.SlabStroke);
            foreach (var (s, n) in sides.OrderBy(s => s.depth).Select((s, n) => (s, n)))
                paint.Add((layer, new DItemSpec($"{p}SLAB_SIDE_{n + 1:000}", s.g, slabFill, slabLine, ax.EdgePt)));
            var top = proj.Plane(plate, z);
            paint.Add((layer, new DItemSpec($"{p}SLAB_TOP", top, slabFill, slabLine, ax.EdgePt)));

            var ids = mine.Select(b => Id(ctx, b, count)).ToList();
            foreach (var (b, id) in mine.Zip(ids))
            {
                var (fill, stroke, pt, dash) = ctx.ProgramPaint(d, b.ProgramKey);
                paint.Add((layer, new DItemSpec($"{p}{id}", proj.Plane(b.Geometry, z), fill, stroke ?? d.Gray(ps.RoomStroke), stroke != null ? pt : ps.RoomPt, dash)));
            }
            // Heavy plate outline: the silhouette of the whole plate (top and sides).
            var silhouette = Safe(() => NetTopologySuite.Operation.Union.CascadedPolygonUnion.Union(sides.Select(s => s.g).Append(top).ToList()), top);
            paint.Add((layer, new DItemSpec($"{p}SLAB_OUTLINE", silhouette, null, slabLine, ax.SlabOutlinePt)));

            foreach (var (b, id) in mine.Zip(ids))
            {
                var row = ctx.Names.GetValueOrDefault(b.MassId);
                var text = b.ProgramKey == "CORE" ? ax.CoreLabel : row?.Label ?? ctx.ProgramLabel(b.ProgramKey);
                if (label.AllCaps) text = text.ToUpperInvariant();
                var spot = PlanLabels.Fit(b.Geometry, text, sizeFt);
                if (spot == null) continue;
                var angle = spot.AngleDeg;
                var m = proj.PlaneTextMatrix(angle);
                if (m[0] < 0) m = proj.PlaneTextMatrix(angle += 180); // read left → right on the page
                var at = proj.Project(spot.X, spot.Y, z);
                var name = $"{p}{id}_LBL";
                paint.Add((layer, new DItemSpec(name, MeshSlicer.Factory.CreatePolygon(), null, null, 0, Text: spot.Text, TextAt: at, Matrix: m)));
            }
        }
    }

    /// <summary>
    /// Right-hand column of level names, bottom → top, never closer than 1.6 × the text size; each joined by a thin leader
    /// to the rightmost corner of its plate.
    /// </summary>
    static void LevelColumn(PD.Core.Drawing.Drawing d, ExportContext ctx, AxonProjection proj, List<int> levels,
        Func<double, double> lift, PageMap map, double colX, string p)
    {
        var ax = ctx.Style.Axon;
        var layer = d.Layer("PD_ANNOTATION");
        var minGap = ctx.Pt(ctx.Style.Fonts.Note.SizePt) * 1.6;
        var labelY = double.MaxValue;
        foreach (var i in levels)
        {
            var lvl = ctx.Result.Levels[i];
            var z = lift(lvl.Elevation);
            var plate = ctx.Result.Plans[i].Union;
            if (plate.IsEmpty) continue;
            var right = plate.Coordinates.Select(c => proj.Project(c.X, c.Y, z)).OrderByDescending(q => q.X).First();
            double x = map.X(right.X) + ctx.Pt(4), y = map.Y(right.Y);
            labelY = Math.Min(y, labelY - minGap);
            var n = SectionDrawing.Safe(lvl.Name);
            layer.Items.Add(new DItem
            {
                Kind = ItemKind.Path, Name = $"{p}LVL_{n}_LDR", Closed = false, Stroke = d.Gray(ax.LeaderStroke), StrokePt = ax.LeaderPt,
                Paths = new() { new[] { new[] { x, y }, new[] { colX - ctx.Pt(10), labelY }, new[] { colX - ctx.Pt(4), labelY } } },
            });
            layer.Items.Add(SectionDrawing.Text($"{p}LVL_{n}_TXT", colX, labelY + ctx.Pt(2.5), PlanDrawing.Title(ctx.Style.Plan.LevelTitleFormat, lvl.Name), "PD Note", "left"));
        }
    }

    /// <summary>Top in the program colour; sides darkened by style axon.sideDarken (lit, shaded) as their own global swatches.</summary>
    static (string? top, string? side1, string? side2) Paints(PD.Core.Drawing.Drawing d, ExportContext ctx, string key)
    {
        var (fill, _, _, _) = ctx.ProgramPaint(d, key);
        var hex = fill == null ? null : d.Swatches.First(s => s.Name == fill).Hex;
        if (hex == null) return (null, null, null);
        var dk = ctx.Style.Axon.SideDarken;
        return (fill, d.Swatch(fill + "_SIDE1", Darken(hex, dk.ElementAtOrDefault(0))), d.Swatch(fill + "_SIDE2", Darken(hex, dk.ElementAtOrDefault(1))));
    }

    public static string Darken(string hex, double amount)
    {
        int C(int i) => (int)Math.Round(Convert.ToInt32(hex.Substring(1 + 2 * i, 2), 16) * (1 - amount));
        return $"#{C(0):X2}{C(1):X2}{C(2):X2}";
    }

    static string Id(ExportContext ctx, ProgramBlock b, Dictionary<string, int> count)
    {
        var lvl = SectionDrawing.Safe(ctx.Result.Levels[b.LevelIndex].Name);
        var id = ctx.Names.TryGetValue(b.MassId, out var row) ? row.StableId : b.MassId[..Math.Min(8, b.MassId.Length)];
        var key = $"{lvl}_{id}";
        var c = count[key] = count.GetValueOrDefault(key) + 1;
        return c > 1 ? $"{key}_{c}" : key;
    }

    static NtsGeometry Simplify(NtsGeometry g) => Safe(() => TopologyPreservingSimplifier.Simplify(g, 0.1), g);

    static IEnumerable<Polygon> Polygons(NtsGeometry g)
    {
        for (var i = 0; i < g.NumGeometries; i++)
            if (g.GetGeometryN(i) is Polygon p && p.Area > 1e-3) yield return p;
    }

    /// <summary>Shell counter-clockwise, holes clockwise, so (dy, −dx) is always the outward normal of an edge.</summary>
    static IEnumerable<LineString> Rings(Polygon p)
    {
        yield return Orientation.IsCCW(p.ExteriorRing.CoordinateSequence) ? p.ExteriorRing : (LineString)p.ExteriorRing.Reverse();
        foreach (var h in p.InteriorRings)
            yield return Orientation.IsCCW(h.CoordinateSequence) ? (LineString)h.Reverse() : h;
    }

    static NtsGeometry Safe(Func<NtsGeometry> op, NtsGeometry fallback)
    {
        try { return op(); }
        catch (TopologyException) { return fallback; }
    }
}
