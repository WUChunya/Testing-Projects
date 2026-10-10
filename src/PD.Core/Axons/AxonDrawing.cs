using NetTopologySuite.Geometries;
using PD.Core.Analysis;
using PD.Core.Classification;
using PD.Core.Drawing;
using PD.Core.Geometry;
using PD.Core.Plans;
using PD.Core.Sections;
using NtsGeometry = NetTopologySuite.Geometries.Geometry;

namespace PD.Core.Axons;

public enum AxonKind { Stacked, Exploded, Levels }

/// <summary>
/// PRD 5.4, styled after the Atlanta exploded axon (thin dark outlines, darker sides, labels laid on the floors, white
/// context, level names with a yellow marker and grey leaders):
///  - 03_Axon_Stacked.ai: whole program masses among white context buildings, hidden faces removed;
///  - 04_Axon_Exploded.ai: one plate per level, spaced so no plate hides another; cores connect the plates and tall
///    spaces stretch to the plate above;
///  - 04_Axon_Levels.ai: one artboard per level — that level's plate inside the building volume drawn dashed.
/// True scale, isometric from the south-west by default (or the Rhino viewport direction).
/// </summary>
public static class AxonDrawing
{
    /// <summary>A path (or plane text) waiting for the page mapping; geometry in screen feet.</summary>
    sealed record Spec(string Name, NtsGeometry Geometry, string? Fill, string? Stroke, double StrokePt, double[]? Dash = null, bool Closed = true,
        double Opacity = 1, string? Text = null, (double X, double Y)? TextAt = null, double[]? Matrix = null, string? Style = null);

    sealed class Sheet
    {
        public string Name = "";
        public string Prefix = "";
        public string Title = "";
        public List<(string Layer, Spec Spec)> Paint { get; } = new();
        /// <summary>Level column entries: level index, drawn height of its plate.</summary>
        public List<(int Level, double Z)> Tags { get; } = new();
    }

    public static string FileName(AxonKind kind) => kind switch
    {
        AxonKind.Stacked => "03_Axon_Stacked.ai",
        AxonKind.Exploded => "04_Axon_Exploded.ai",
        _ => "04_Axon_Levels.ai",
    };

    public static PD.Core.Drawing.Drawing Build(ExportContext ctx, AxonKind kind)
    {
        var r = ctx.Result;
        var ax = ctx.Style.Axon;
        var proj = AxonProjection.FromAngles(ax.AzimuthDeg, ax.ElevationDeg, ctx.Settings.Output.AxonViewDirection);
        var d = new PD.Core.Drawing.Drawing { FileName = FileName(kind) };
        ctx.AddStandardStyles(d);
        var blocks = ProgramBlocks.Build(r);
        if (blocks.Count == 0)
        {
            d.Notes.Add("Axon: no program blocks.");
            return d;
        }
        var sheets = kind switch
        {
            AxonKind.Stacked => new List<Sheet> { Stacked(d, ctx, proj, blocks) },
            AxonKind.Exploded => new List<Sheet> { Exploded(d, ctx, proj, blocks) },
            _ => Levels(d, ctx, proj, blocks),
        };
        Page(d, ctx, proj, sheets, kind);
        return d;
    }

    // ---------------------------------------------------------------- stacked

    static Sheet Stacked(PD.Core.Drawing.Drawing d, ExportContext ctx, AxonProjection proj, List<ProgramBlock> blocks)
    {
        var ax = ctx.Style.Axon;
        var sheet = new Sheet { Name = "AXON STACKED", Prefix = "AXS_", Title = ax.TitleStacked };
        var ground = ctx.Rules.Levels.GroundElevationFt;
        Site(d, ctx, proj, blocks, sheet, ground, flatContext: false, out var context);
        var prisms = context.Concat(ProgramPrisms(d, ctx, blocks, b => (b.Z0, b.Z1))).ToList();
        EmitFaces(d, ctx, proj, AxonFaces.Render(proj, prisms), sheet, "PD_PROGRAM", "PD_LABELS");

        if (ax.FacadeVeil && ctx.Style.Facade.StackedAxonOpacity > 0)
        {
            var ids = ctx.Result.FacadeIds.ToHashSet();
            foreach (var group in ctx.Result.Local.Objects.Where(o => ids.Contains(o.Id) && o.Mesh != null && !ctx.Result.Stray.Contains(o.Id))
                         .GroupBy(o => o.LayerPath).OrderBy(g => g.Key, StringComparer.Ordinal))
            {
                var veil = AxonFaces.Union(group.Select(o => MeshProjection.Union(o.Mesh!, proj.Project, proj.Faces)));
                if (veil.IsEmpty) continue;
                sheet.Paint.Add(("FACADE", new Spec("FACADE_" + SectionDrawing.Safe(group.Key).ToUpperInvariant(), veil, d.Swatch("AXON_FACADE_VEIL", ax.FacadeVeilFill),
                    d.Gray(ax.FacadeVeilStroke), ax.FacadeVeilStrokePt, Opacity: ctx.Style.Facade.StackedAxonOpacity)));
            }
        }
        return sheet;
    }

    // ---------------------------------------------------------------- exploded

    /// <summary>
    /// Plates bottom → top, each on its own layer; the gap above each plate grows until the next plate clears every
    /// plate below it on the page (plus explodeGapPt). Tall spaces stretch to the underside of the plate above;
    /// connector programs (cores) present on consecutive plates are joined by translucent volumes.
    /// </summary>
    static Sheet Exploded(PD.Core.Drawing.Drawing d, ExportContext ctx, AxonProjection proj, List<ProgramBlock> blocks)
    {
        var r = ctx.Result;
        var ax = ctx.Style.Axon;
        var t = ax.SlabThicknessFt;
        var sheet = new Sheet { Name = "AXON EXPLODED", Prefix = "AXE_", Title = ax.TitleExploded };
        var levels = blocks.Select(b => b.LevelIndex).Distinct().OrderBy(i => i).ToList();
        var plates = levels.ToDictionary(i => i, i => AxonFaces.Union(blocks.Where(b => b.LevelIndex == i).Select(b => b.Geometry)));

        // Spacing: the next plate rises until its page footprint clears all lower plates.
        var gapFt = ctx.Pt(ax.ExplodeGapPt) / ctx.PtPerFt;
        var lifted = new Dictionary<int, double>();
        NtsGeometry? below = null;
        foreach (var (i, n) in levels.Select((i, n) => (i, n)))
        {
            var e = r.Levels[i].Elevation;
            var z = n == 0 ? e : lifted[levels[n - 1]] + Math.Max(1, e - r.Levels[levels[n - 1]].Elevation);
            NtsGeometry Screen(double zz) => AxonFaces.Union(new[] { proj.Plane(plates[i], zz), proj.Plane(plates[i], zz - t) });
            if (below != null)
            {
                var step = Math.Max(1, gapFt / Math.Max(0.05, proj.ZScale) / 4);
                for (var k = 0; k < 4000 && Screen(z).Intersects(below); k++) z += step;
            }
            lifted[i] = z;
            var me = Screen(z);
            below = below == null ? me.Buffer(gapFt) : AxonFaces.Safe(() => below.Union(me.Buffer(gapFt)), below);
        }
        // Real elevation → drawn height: piecewise linear through the plates (tall spaces stretch with it).
        double Lift(double z)
        {
            var pts = levels.Select(i => (e: r.Levels[i].Elevation, z: lifted[i])).ToList();
            if (z <= pts[0].e) return pts[0].z + (z - pts[0].e);
            for (var k = 0; k + 1 < pts.Count; k++)
                if (z <= pts[k + 1].e)
                    return pts[k].z + (z - pts[k].e) * (pts[k + 1].z - pts[k].z) / Math.Max(1e-6, pts[k + 1].e - pts[k].e);
            return pts[^1].z + (z - pts[^1].e);
        }

        var ground = ctx.Rules.Levels.GroundElevationFt;
        Site(d, ctx, proj, blocks, sheet, Math.Min(Lift(ground), lifted[levels[0]]) - t, flatContext: true, out _);
        var connector = ax.ConnectorPrograms.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var joined = 0;
        foreach (var (i, n) in levels.Select((i, n) => (i, n)))
        {
            var layer = "AXON_" + SectionDrawing.Safe(r.Levels[i].Name).ToUpperInvariant();
            var z = lifted[i];
            var mine = blocks.Where(b => b.LevelIndex == i).ToList();
            // One floor in the next band: plate thickness; a tall space reaches the underside of the plate above.
            var next = n + 1 < levels.Count ? r.Levels[levels[n + 1]].Elevation : double.MaxValue;
            var prisms = ProgramPrisms(d, ctx, mine, b => (z - t, b.Z1 > next + 0.5 ? Lift(b.Z1) - t : z)).ToList();
            EmitFaces(d, ctx, proj, AxonFaces.Render(proj, prisms), sheet, layer, layer, silhouetteName: $"{SectionDrawing.Safe(r.Levels[i].Name)}_OUTLINE");
            sheet.Tags.Add((i, z));

            // Cores: translucent volume from this plate to the underside of the next one, where both have the program.
            if (n + 1 >= levels.Count || connector.Count == 0) continue;
            var up = levels[n + 1];
            NtsGeometry Cores(int lvl) => AxonFaces.Union(blocks.Where(b => b.LevelIndex == lvl && connector.Contains(b.ProgramKey)).Select(b => b.Geometry));
            var shared = AxonFaces.Safe(() => Cores(i).Intersection(Cores(up)), MeshSlicer.Factory.CreatePolygon());
            if (shared.IsEmpty || shared.Area < 1) continue;
            var fill = d.Swatch("AXON_CONNECTOR", ax.ConnectorFill);
            var link = new Prism($"{SectionDrawing.Safe(r.Levels[i].Name)}_CONNECTOR", shared, z, lifted[up] - t, fill, fill, fill, "LINK");
            foreach (var f in AxonFaces.Render(proj, new[] { link }))
                sheet.Paint.Add((layer, new Spec(f.Name, f.Screen, f.Fill, d.Gray(ax.ConnectorStroke), ax.ConnectorPt, Opacity: ax.ConnectorOpacity)));
            joined++;
        }
        if (joined == 0 && connector.Count > 0)
            d.Notes.Add($"Exploded axon: no {string.Join("/", connector)} program on consecutive levels, so plates are not connected (assign the core masses to CORE).");
        return sheet;
    }

    // ---------------------------------------------------------------- floor keys

    /// <summary>One sheet per level: the building volume as dashed hidden-line edges, that level's plate in colour inside it.</summary>
    static List<Sheet> Levels(PD.Core.Drawing.Drawing d, ExportContext ctx, AxonProjection proj, List<ProgramBlock> blocks)
    {
        var r = ctx.Result;
        var ax = ctx.Style.Axon;
        var t = ax.SlabThicknessFt;
        var ground = ctx.Rules.Levels.GroundElevationFt;
        var ghost = AxonFaces.Render(proj, blocks.Select((b, n) => new Prism($"G{n:000}", b.Geometry, b.Z0, b.Z1, null, null, null, b.LevelIndex.ToString())));
        var sheets = new List<Sheet>();
        foreach (var i in blocks.Select(b => b.LevelIndex).Distinct().OrderBy(i => i))
        {
            var lvl = r.Levels[i];
            var n = SectionDrawing.Safe(lvl.Name);
            var sheet = new Sheet { Name = $"AXON {lvl.Name}", Prefix = $"AXK_{n}_", Title = $"{ax.TitleLevels} {PlanDrawing.Title(ctx.Style.Plan.LevelTitleFormat, lvl.Name)}" };
            Site(d, ctx, proj, blocks, sheet, ground, flatContext: true, out _);
            var stroke = d.Gray(ax.GhostStroke);
            var lowerEdges = AxonFaces.Edges(ghost.Where(f => int.Parse(f.Prism.Group) < i).Select(f => f.Screen));
            if (!lowerEdges.IsEmpty) sheet.Paint.Add(("AXON_GHOST_BELOW", new Spec("GHOST_BELOW", lowerEdges, null, stroke, ax.GhostPt, ax.GhostDash, Closed: false)));
            var prisms = ProgramPrisms(d, ctx, blocks.Where(b => b.LevelIndex == i), _ => (lvl.Elevation - t, lvl.Elevation)).ToList();
            EmitFaces(d, ctx, proj, AxonFaces.Render(proj, prisms), sheet, "PD_PROGRAM", "PD_LABELS");
            var upperEdges = AxonFaces.Edges(ghost.Where(f => int.Parse(f.Prism.Group) >= i).Select(f => f.Screen));
            if (!upperEdges.IsEmpty) sheet.Paint.Add(("AXON_GHOST_ABOVE", new Spec("GHOST_ABOVE", upperEdges, null, stroke, ax.GhostPt, ax.GhostDash, Closed: false)));
            sheet.Tags.Add((i, lvl.Elevation));
            sheets.Add(sheet);
        }
        return sheets;
    }

    // ---------------------------------------------------------------- shared

    /// <summary>Program prisms: top in the program colour, sides darkened (own global swatches), label = tab 4 name.</summary>
    static IEnumerable<Prism> ProgramPrisms(PD.Core.Drawing.Drawing d, ExportContext ctx, IEnumerable<ProgramBlock> blocks, Func<ProgramBlock, (double Z0, double Z1)> band)
    {
        var count = new Dictionary<string, int>();
        var ax = ctx.Style.Axon;
        foreach (var b in blocks)
        {
            var lvl = SectionDrawing.Safe(ctx.Result.Levels[b.LevelIndex].Name);
            var row = ctx.Names.GetValueOrDefault(b.MassId);
            var id = $"{lvl}_{row?.StableId ?? b.MassId[..Math.Min(8, b.MassId.Length)]}";
            var c = count[id] = count.GetValueOrDefault(id) + 1;
            if (c > 1) id += $"_{c}";
            var (fill, _, _, _) = ctx.ProgramPaint(d, b.ProgramKey);
            var hex = fill == null ? null : d.Swatches.First(s => s.Name == fill).Hex;
            var dk = ax.SideDarken;
            var side1 = hex == null ? null : d.Swatch(fill + "_SIDE1", Darken(hex, dk.ElementAtOrDefault(0)));
            var side2 = hex == null ? null : d.Swatch(fill + "_SIDE2", Darken(hex, dk.ElementAtOrDefault(1)));
            var text = b.ProgramKey == "CORE" ? ax.CoreLabel : row?.Label ?? ctx.ProgramLabel(b.ProgramKey);
            var (z0, z1) = band(b);
            yield return new Prism(id, b.Geometry, z0, z1, fill, side1, side2, "PRG", text, ctx.LabelStyle(d, fill, "PD Plane Label"));
        }
    }

    /// <summary>Faces (context white with grey edges, program with dark edges), the program silhouette, and labels on visible tops.</summary>
    static void EmitFaces(PD.Core.Drawing.Drawing d, ExportContext ctx, AxonProjection proj, List<VisibleFace> faces, Sheet sheet, string layer, string labelLayer,
        string silhouetteName = "OUTLINE")
    {
        var ax = ctx.Style.Axon;
        foreach (var f in faces)
        {
            var isContext = f.Prism.Group == "CTX";
            sheet.Paint.Add((isContext ? "AXON_CONTEXT" : layer, new Spec(f.Name, f.Screen, f.Fill,
                d.Gray(isContext ? ax.ContextStroke : ax.EdgeStroke), isContext ? ax.ContextPt : ax.EdgePt)));
        }
        var program = faces.Where(f => f.Prism.Group == "PRG").ToList();
        var outline = AxonFaces.Union(program.Select(f => f.Screen));
        if (!outline.IsEmpty)
            sheet.Paint.Add((layer == "PD_PROGRAM" ? "AXON_OUTLINE" : layer, new Spec(silhouetteName, outline, null, d.Gray(ax.SilhouetteStroke), ax.SilhouettePt)));

        var sizeFt = ctx.Pt(ax.LabelSizePt > 0 ? ax.LabelSizePt : ctx.Style.Fonts.Label.SizePt) / ctx.PtPerFt;
        var caps = ctx.Style.Fonts.Label.AllCaps;
        foreach (var group in program.Where(f => f.IsTop && f.Prism.Label != null).GroupBy(f => f.Prism))
        {
            var p = group.Key;
            var top = proj.Unplane(AxonFaces.Union(group.Select(f => f.Screen)), p.Z1);
            var text = caps ? p.Label!.ToUpperInvariant() : p.Label!;
            var spot = PlanLabels.Fit(top, text, sizeFt);
            if (spot == null) continue;
            var angle = spot.AngleDeg;
            var m = proj.PlaneTextMatrix(angle);
            if (m[0] < 0) m = proj.PlaneTextMatrix(angle + 180); // read left → right on the page
            sheet.Paint.Add((labelLayer, new Spec(p.Name + "_LBL", MeshSlicer.Factory.CreatePolygon(), null, null, 0,
                Text: spot.Text, TextAt: proj.Project(spot.X, spot.Y, p.Z1), Matrix: m, Style: p.LabelStyle)));
        }
    }

    /// <summary>
    /// Site plate and site background at <paramref name="z"/>; other buildings either as white prisms (stacked, returned
    /// for hidden-face removal with our masses) or as flat white footprints on the site.
    /// </summary>
    static void Site(PD.Core.Drawing.Drawing d, ExportContext ctx, AxonProjection proj, List<ProgramBlock> blocks, Sheet sheet, double z,
        bool flatContext, out List<Prism> context)
    {
        var r = ctx.Result;
        var ax = ctx.Style.Axon;
        var own = new Envelope();
        foreach (var b in blocks) own.ExpandToInclude(b.Geometry.EnvelopeInternal);
        var site = new Envelope(own);
        site.ExpandBy(ax.SiteMarginFt);
        sheet.Paint.Add(("AXON_SITE", new Spec("SITE", proj.Plane(MeshSlicer.Factory.ToGeometry(site), z), d.Swatch("AXON_SITE", ax.SiteFill),
            d.Gray(ax.SiteStroke), ax.SiteStroke == null ? 0 : ax.ContextPt)));
        foreach (var (bg, n) in PlanBackground.Collect(r, site).Where(b => b.Role != Roles.Building).Select((b, n) => (b, n)))
        {
            var (fill, stroke, pt, dash) = PlanBackground.Paint(d, ctx.Style, bg);
            sheet.Paint.Add(("AXON_SITE", new Spec($"BG_{n:000}", proj.Plane(bg.Geometry, z), fill, stroke, pt, dash, !bg.IsLine)));
        }

        // Other buildings near ours; never over our own footprint.
        var near = new Envelope(own);
        near.ExpandBy(ax.ContextMarginFt);
        var ours = AxonFaces.Union(blocks.Select(b => b.Geometry)).Buffer(1);
        var white = d.Swatch("AXON_CONTEXT", ax.ContextFill);
        context = new List<Prism>();
        var k = 0;
        foreach (var bg in PlanBackground.Collect(r, near).Where(b => b.Role == Roles.Building && !b.IsLine))
        {
            var foot = AxonFaces.Safe(() => bg.Geometry.Difference(ours), bg.Geometry);
            if (foot.IsEmpty || foot.Area < 4) continue;
            var src = r.Local.Objects.Where(o => o.Role == Roles.Building && o.Mesh != null).FirstOrDefault(o =>
                new Envelope(o.BBox.MinX, o.BBox.MaxX, o.BBox.MinY, o.BBox.MaxY).Intersects(bg.Geometry.EnvelopeInternal) &&
                MeshProjection.TopDown(o.Mesh!).Intersects(bg.Geometry.InteriorPoint));
            var name = $"CONTEXT_{++k:000}";
            if (flatContext || src == null || (ax.ContextHeightFt > 0 && ax.ContextHeightFt < 0.5))
                sheet.Paint.Add(("AXON_CONTEXT", new Spec(name, proj.Plane(foot, z), white, d.Gray(ax.ContextStroke), ax.ContextPt)));
            else
            {
                // Low white blocks so our building reads (reference); contextHeightFt 0 = real height.
                var z0 = Math.Max(z, src.BBox.MinZ);
                var z1 = ax.ContextHeightFt > 0 ? Math.Min(src.BBox.MaxZ, z0 + ax.ContextHeightFt) : src.BBox.MaxZ;
                context.Add(new Prism(name, foot, z0, z1, white, white, white, "CTX"));
            }
        }
    }

    /// <summary>Artboards (one per sheet, same size), items, level column with markers and leaders, titles.</summary>
    static void Page(PD.Core.Drawing.Drawing d, ExportContext ctx, AxonProjection proj, List<Sheet> sheets, AxonKind kind)
    {
        var ax = ctx.Style.Axon;
        var pg = ctx.Style.Page;
        var k = ctx.PtPerFt;
        var env = new Envelope();
        foreach (var s in sheets)
        foreach (var (_, spec) in s.Paint.Where(x => x.Spec.Text == null && !x.Spec.Geometry.IsEmpty))
            env.ExpandToInclude(spec.Geometry.EnvelopeInternal);
        double m = pg.MarginPt, w = env.Width * k, h = env.Height * k;
        var colR = sheets.Any(s => s.Tags.Count > 0) ? ctx.Pt(ax.LevelColumnPt) : 0;
        var titleBand = ctx.Pt(pg.TitleBandPt);
        double abW, abH, x0, y0;
        if (ctx.ArtboardSize is { } fixedSize)
        {
            (abW, abH) = fixedSize;
            x0 = (abW - w - colR) / 2;
            y0 = m + (abH - 2 * m - titleBand - h) / 2;
            if (x0 < m - 0.5 || y0 < m - 0.5)
                d.Notes.Add($"{kind} axon at {ctx.Settings.Output.Scale} is larger than {ctx.Settings.Output.Artboard}; use \"Fit content\" or a smaller scale.");
        }
        else
        {
            abW = 2 * m + w + colR;
            abH = 2 * m + h + titleBand;
            x0 = m;
            y0 = m;
        }
        var map = new PageMap(x0 - env.MinX * k, y0, k, env.MaxY, env.Width, flip: false);

        // Layer order: site and context, then per-sheet layers in first-use order, labels and annotation on top.
        var used = sheets.SelectMany(s => s.Paint.Select(x => x.Layer)).Distinct().ToList();
        string[] order = { "AXON_SITE", "AXON_CONTEXT", "AXON_GHOST_BELOW", "PD_PROGRAM", "AXON_OUTLINE" };
        foreach (var n in order.Where(used.Contains)) d.Layer(n);
        foreach (var n in used.Where(n => !order.Contains(n) && n is not ("FACADE" or "AXON_GHOST_ABOVE" or "PD_LABELS"))) d.Layer(n);
        foreach (var n in new[] { "FACADE", "AXON_GHOST_ABOVE" }.Where(used.Contains)) d.Layer(n);
        foreach (var n in new[] { "PD_LABELS", "PD_ANNOTATION" }) d.Layer(n);

        foreach (var (s, a) in sheets.Select((s, a) => (s, a)))
        {
            d.Artboards.Add(new Artboard(s.Name, Math.Ceiling(abW), Math.Ceiling(abH)));
            foreach (var (layer, spec) in s.Paint)
            {
                DItem item;
                if (spec.Text != null)
                {
                    item = SectionDrawing.Text(s.Prefix + spec.Name, map.X(spec.TextAt!.Value.X), map.Y(spec.TextAt!.Value.Y), spec.Text, spec.Style ?? "PD Plane Label", "center", center: true);
                    item.Matrix = spec.Matrix;
                }
                else
                {
                    if (spec.Geometry.IsEmpty) continue;
                    item = map.Geom(s.Prefix + spec.Name, spec.Geometry, spec.Fill, spec.Stroke, spec.StrokePt, spec.Closed);
                    item.Dash = spec.Dash;
                    item.Opacity = spec.Opacity;
                }
                item.Artboard = a;
                d.Layer(layer).Items.Add(item);
            }
            LevelColumn(d, ctx, proj, s, map, x0 + w + ctx.Pt(24), a);
            var ty = y0 + h + ctx.Pt(26);
            var anno = d.Layer("PD_ANNOTATION");
            anno.Items.Add(At(SectionDrawing.Text(s.Prefix + "TITLE", x0, ty, s.Title, "PD Title", "left"), a));
            anno.Items.Add(At(SectionDrawing.Text(s.Prefix + "SCALE", x0, ty + ctx.Pt(12), $"SCALE {ctx.Settings.Output.Scale}", "PD Note", "left"), a));
        }
    }

    /// <summary>
    /// Level names right of the drawing, bottom → top and never closer than 1.8 × the text size: a yellow square,
    /// "Level 3", and a thin grey leader to the rightmost corner of the plate.
    /// </summary>
    static void LevelColumn(PD.Core.Drawing.Drawing d, ExportContext ctx, AxonProjection proj, Sheet s, PageMap map, double colX, int a)
    {
        var ax = ctx.Style.Axon;
        var layer = d.Layer("PD_ANNOTATION");
        var minGap = ctx.Pt(ctx.Style.Fonts.Level.SizePt) * 1.8;
        var sq = ctx.Pt(ax.LevelMarkerPt);
        var labelY = double.MaxValue;
        foreach (var (i, z) in s.Tags)
        {
            var lvl = ctx.Result.Levels[i];
            var plate = ctx.Result.Plans[i].Union;
            if (plate.IsEmpty) continue;
            var right = plate.Coordinates.Select(c => proj.Project(c.X, c.Y, z)).OrderByDescending(q => q.X).First();
            double x = map.X(right.X) + ctx.Pt(3), y = map.Y(right.Y);
            labelY = Math.Min(y, labelY - minGap);
            var n = SectionDrawing.Safe(lvl.Name);
            layer.Items.Add(At(new DItem
            {
                Kind = ItemKind.Path, Name = $"{s.Prefix}LVL_{n}_LDR", Closed = false, Stroke = d.Gray(ax.LeaderStroke), StrokePt = ax.LeaderPt,
                Paths = new() { new[] { new[] { x, y }, new[] { colX - ctx.Pt(4), labelY } } },
            }, a));
            layer.Items.Add(At(new DItem
            {
                Kind = ItemKind.Path, Name = $"{s.Prefix}LVL_{n}_MARK", Fill = d.Swatch("PD_LEVEL_MARKER", ax.LevelMarker),
                Paths = new() { new[] { new[] { colX, labelY - sq / 2 }, new[] { colX + sq, labelY - sq / 2 }, new[] { colX + sq, labelY + sq / 2 }, new[] { colX, labelY + sq / 2 } } },
            }, a));
            layer.Items.Add(At(SectionDrawing.Text($"{s.Prefix}LVL_{n}_TXT", colX + sq * 2, labelY + ctx.Pt(ctx.Style.Fonts.Level.SizePt) * 0.35,
                PlanDrawing.Title(ctx.Style.Plan.LevelTitleFormat, lvl.Name), "PD Level", "left"), a));
        }
    }

    public static string Darken(string hex, double amount)
    {
        int C(int i) => (int)Math.Round(Convert.ToInt32(hex.Substring(1 + 2 * i, 2), 16) * (1 - amount));
        return $"#{C(0):X2}{C(1):X2}{C(2):X2}";
    }

    static DItem At(DItem item, int artboard)
    {
        item.Artboard = artboard;
        return item;
    }
}
