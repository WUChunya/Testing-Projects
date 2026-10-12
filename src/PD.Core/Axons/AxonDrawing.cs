using NetTopologySuite.Geometries;
using PD.Core.Analysis;
using PD.Core.Classification;
using PD.Core.Drawing;
using PD.Core.Geometry;
using PD.Core.Plans;
using PD.Core.Sections;
using NtsGeometry = NetTopologySuite.Geometries.Geometry;

namespace PD.Core.Axons;

public enum AxonKind { Stacked, Exploded, ExplodedB, Levels, Site }

/// <summary>
/// PRD 5.4 axons, styled per docs/AXON_STYLE.md and docs/SITE_STYLE.md:
///  - 03_Axon_Stacked.ai: site program axon (§1) — program masses with floor lines among white context, trees, names;
///  - 04_Axon_Exploded.ai: exploded A (Atlanta) — plates with ≥ 70 % of each top visible, labels at the front corner,
///    cores / stairs / tall spaces as translucent connectors, "Level 7" tags with leaders along the drawing;
///  - 04_Axon_Exploded_B.ai: exploded B — planometric 30/60, every program a translucent block, plates clear of each
///    other, atria and shafts, a white site plane with the plan printed on it;
///  - 04_Axon_Levels.ai: one artboard per level — that plate inside the building drawn as dashed hidden lines.
/// True scale; axes at true length (isometric drawing).
/// </summary>
public static class AxonDrawing
{
    /// <summary>A path (or plane text) waiting for the page mapping; geometry in screen feet.</summary>
    sealed record Spec(string Name, NtsGeometry Geometry, string? Fill, string? Stroke, double StrokePt, double[]? Dash = null, bool Closed = true,
        double Opacity = 1, string? Text = null, (double X, double Y)? TextAt = null, double[]? Matrix = null, string? Style = null, string Align = "center",
        double? Depth = null);

    enum TagStyle { A, B }

    sealed class Sheet
    {
        public string Name = "";
        public string Prefix = "";
        public string Title = "";
        public TagStyle Tag = TagStyle.A;
        public List<(string Layer, Spec Spec)> Paint { get; } = new();
        /// <summary>Level column entries: level index, drawn height of its plate.</summary>
        public List<(int Level, double Z)> Tags { get; } = new();
    }

    public static string FileName(AxonKind kind) => kind switch
    {
        AxonKind.Stacked => "03_Axon_Stacked.ai",
        AxonKind.Site => "03_Site_Axon.ai",
        AxonKind.Exploded => "04_Axon_Exploded.ai",
        AxonKind.ExplodedB => "04_Axon_Exploded_B.ai",
        _ => "04_Axon_Levels.ai",
    };

    public static PD.Core.Drawing.Drawing Build(ExportContext ctx, AxonKind kind)
    {
        var ax = ctx.Style.Axon;
        var proj = kind == AxonKind.ExplodedB
            ? AxonProjection.Planometric(ax.ExplodedB.YAxisDeg)
            : AxonProjection.FromAngles(ax.AzimuthDeg, ax.ElevationDeg, ctx.Settings.Output.AxonViewDirection, trueLength: !ax.Foreshorten);
        var d = new PD.Core.Drawing.Drawing { FileName = FileName(kind) };
        ctx.AddStandardStyles(d);
        var blocks = ProgramBlocks.Build(ctx.Result);
        if (blocks.Count == 0)
        {
            d.Notes.Add("Axon: no program blocks.");
            return d;
        }
        var sheets = kind switch
        {
            AxonKind.Stacked => new List<Sheet> { Stacked(d, ctx, proj, blocks) },
            AxonKind.Site => new List<Sheet> { Stacked(d, ctx, proj, blocks, site: true) },
            AxonKind.Exploded => new List<Sheet> { ExplodedA(d, ctx, proj, blocks) },
            AxonKind.ExplodedB => new List<Sheet> { ExplodedB(d, ctx, proj, blocks) },
            _ => Levels(d, ctx, proj, blocks),
        };
        Page(d, ctx, proj, sheets, kind);
        if (kind == AxonKind.Site && ax.Site.AnnotationLayer.Length > 0 && !d.ProtectedLayers.Contains(ax.Site.AnnotationLayer))
            d.ProtectedLayers.Add(ax.Site.AnnotationLayer);
        return d;
    }

    // ---------------------------------------------------------------- stacked (site program axon)

    /// <summary>Stacked axon; with <paramref name="site"/> the site axon (wider site, real-height context, people).</summary>
    static Sheet Stacked(PD.Core.Drawing.Drawing d, ExportContext ctx, AxonProjection proj, List<ProgramBlock> blocks, bool site = false)
    {
        var ax = ctx.Style.Axon;
        var st = ax.Stacked;
        var sheet = site
            ? new Sheet { Name = "SITE AXON", Prefix = "AXT_", Title = ax.Site.Title }
            : new Sheet { Name = "AXON STACKED", Prefix = "AXS_", Title = ax.TitleStacked };
        var ground = ctx.Rules.Levels.GroundElevationFt;
        var reach = site
            ? new SiteReach(ax.Site.SiteMarginFt, ax.Site.ContextMarginFt, ax.Site.ContextHeightFt, ax.Site.MaxContextHeightFt, ax.Site.People)
            : SiteReach.Of(ax);
        var context = Site(d, ctx, proj, blocks, sheet, ground, flatContext: false, family: "axon", opacity: 1, reach: reach);
        var program = ProgramPrisms(d, ctx, blocks, b => (b.Z0, b.Z1), st.SideDarken, coreOverride: false).ToList();
        var faces = AxonFaces.Render(proj, context.Concat(program));
        HideBehind(sheet, proj, faces);
        var line = d.Gray(ax.LineColor);
        var ctxPaint = PlanBackground.PaintOf(ctx.Style, Roles.Building, "axon");
        foreach (var f in faces)
        {
            if (f.Prism.Group == "CTX")
                sheet.Paint.Add(("AXON_CONTEXT", new Spec(f.Name, f.Screen, f.Fill, d.Gray(ctxPaint.Stroke), ctxPaint.StrokePt ?? ax.ContextPt)));
            else
                // Faces carry the floor lines: 0.5 pt in their own colour, 35 % darker.
                sheet.Paint.Add(("PD_PROGRAM", new Spec(f.Name, f.Screen, f.Fill, FloorLine(d, f.Fill, st.FloorLineDarken), st.FloorLinePt)));
        }
        var mine = faces.Where(f => f.Prism.Group == "PRG").ToList();
        var folds = AxonFaces.Folds(mine);
        if (!folds.IsEmpty) sheet.Paint.Add(("AXON_LINE", new Spec("FOLDS", folds, null, line, st.EdgePt, Closed: false)));
        var outline = AxonFaces.Union(mine.Select(f => f.Screen));
        if (!outline.IsEmpty) sheet.Paint.Add(("AXON_LINE", new Spec("OUTLINE", outline, null, line, st.SilhouettePt)));
        if (st.Labels) CentredLabels(ctx, proj, mine, sheet, "PD_LABELS", "PD Plane Label", "PD Plane Label Small");

        // Building names on the roofs, horizontal (not sheared).
        foreach (var c in context.Where(c => SectionDrawing.IsDisplayName(c.Label)))
        {
            var pt = c.Plan.InteriorPoint.Coordinate;
            sheet.Paint.Add(("PD_LABELS", new Spec(c.Name + "_NAME", MeshSlicer.Factory.CreatePolygon(), null, null, 0,
                Text: c.Label!.ToUpperInvariant(), TextAt: proj.Project(pt.X, pt.Y, c.Z1), Style: "PD Context Label")));
        }

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

    // ---------------------------------------------------------------- exploded A (Atlanta)

    static Sheet ExplodedA(PD.Core.Drawing.Drawing d, ExportContext ctx, AxonProjection proj, List<ProgramBlock> blocks)
    {
        var r = ctx.Result;
        var ax = ctx.Style.Axon;
        var ex = ax.Exploded;
        var t = ex.SlabThicknessFt;
        var sheet = new Sheet { Name = "AXON EXPLODED", Prefix = "AXE_", Title = ax.TitleExploded };
        var levels = Plates(blocks);
        // Pitch (top to top) = max(1.5 × floor height, 70 % of the lower plate's projected depth): the front 70 % stays visible.
        var lifted = Lifted(r, blocks, levels, (lower, story) => Math.Max(ex.GapFactor * story, ex.MinTopVisible * Depth(proj, lower) / proj.ZScale));
        double Lift(double z) => LiftZ(r, levels, lifted, z);

        Site(d, ctx, proj, blocks, sheet, lifted[levels[0]] - t, flatContext: true, family: "axon", opacity: 1);
        var joined = 0;
        foreach (var (i, n) in levels.Select((i, n) => (i, n)))
        {
            var lvl = r.Levels[i];
            var layer = "AXON_" + SectionDrawing.Safe(lvl.Name).ToUpperInvariant();
            var z = lifted[i];
            var mine = blocks.Where(b => b.LevelIndex == i).ToList();
            var prisms = ProgramPrisms(d, ctx, mine, _ => (z - t, z), ax.SideDarken, coreOverride: true).ToList();
            var faces = AxonFaces.Render(proj, prisms);
            PlateFaces(d, ctx, faces, sheet, layer, $"{SectionDrawing.Safe(lvl.Name)}_");
            FrontCornerLabels(ctx, proj, faces, sheet, layer);
            sheet.Tags.Add((i, z));

            // Connectors up to the next plate: cores / stairs present on both, and tall spaces (stretched).
            if (n + 1 >= levels.Count) continue;
            var up = levels[n + 1];
            var fill = d.Swatch("AXON_CONNECTOR", ax.ConnectorFill);
            foreach (var (shape, top, name) in Connectors(ctx, blocks, i, up, Lift))
            {
                var link = new Prism($"{SectionDrawing.Safe(lvl.Name)}_{name}", shape, z, Math.Max(z + 1, top - t), fill, fill, fill, "LINK");
                foreach (var f in AxonFaces.Render(proj, new[] { link }))
                    sheet.Paint.Add((layer, new Spec(f.Name, f.Screen, f.Fill, d.Gray(ax.ConnectorStroke), ax.ConnectorPt, Opacity: ax.ConnectorOpacity)));
                if (name.StartsWith("CONNECTOR")) joined++;
            }
        }
        if (joined == 0 && ax.ConnectorPrograms.Count > 0)
            d.Notes.Add($"Exploded axon: no {string.Join("/", ax.ConnectorPrograms)} program on consecutive levels, so plates are not connected (set core / stair layers to NC.Core / NC.Stair on tab 1).");
        return sheet;
    }

    /// <summary>Plate faces (AXON_STYLE §3): tops with white 0.5 pt divisions, sides plain, 1.0 pt folds, 2.0 pt plate silhouette.</summary>
    static void PlateFaces(PD.Core.Drawing.Drawing d, ExportContext ctx, List<VisibleFace> faces, Sheet sheet, string layer, string prefix)
    {
        var ax = ctx.Style.Axon;
        var line = d.Gray(ax.LineColor);
        foreach (var f in faces)
            sheet.Paint.Add((layer, f.IsTop
                ? new Spec(f.Name, f.Screen, f.Fill, d.Gray(ax.DivisionColor), ax.DivisionPt)
                : new Spec(f.Name, f.Screen, f.Fill, null, 0)));
        var folds = AxonFaces.Folds(faces);
        if (!folds.IsEmpty) sheet.Paint.Add((layer, new Spec(prefix + "FOLDS", folds, null, line, ax.FoldPt, Closed: false)));
        var outline = AxonFaces.Union(faces.Select(f => f.Screen));
        if (!outline.IsEmpty) sheet.Paint.Add((layer, new Spec(prefix + "OUTLINE", outline, null, line, ax.SlabSilhouettePt)));
    }

    // ---------------------------------------------------------------- exploded B (planometric)

    static Sheet ExplodedB(PD.Core.Drawing.Drawing d, ExportContext ctx, AxonProjection proj, List<ProgramBlock> blocks)
    {
        var r = ctx.Result;
        var ax = ctx.Style.Axon;
        var eb = ax.ExplodedB;
        var t = eb.BlockThicknessFt;
        var sheet = new Sheet { Name = "AXON EXPLODED B", Prefix = "AXB_", Title = ax.TitleExplodedB, Tag = TagStyle.B };
        var levels = Plates(blocks);
        var lifted = Lifted(r, blocks, levels, (lower, story) => Math.Max(story, eb.ClearRatio * Depth(proj, lower) / proj.ZScale));
        double Lift(double z) => LiftZ(r, levels, lifted, z);

        // White site plane under the ground floor with the site plan printed on it at 55 %.
        var ground = ctx.Rules.Levels.GroundElevationFt;
        var groundPlate = levels.FirstOrDefault(i => r.Levels[i].Elevation >= ground - 0.5, levels[0]);
        Site(d, ctx, proj, blocks, sheet, lifted[groundPlate] - t, flatContext: true, family: "plan", opacity: eb.SitePlanOpacity,
            plane: (eb.SitePlaneFill, eb.SitePlaneStroke, eb.SitePlaneStrokePt));

        var white = d.Gray(eb.EdgeStroke);
        foreach (var (i, n) in levels.Select((i, n) => (i, n)))
        {
            var lvl = r.Levels[i];
            var ln = SectionDrawing.Safe(lvl.Name);
            var layer = "AXON_" + ln.ToUpperInvariant();
            var z = lifted[i];
            // Atria: the open-to-below part of this level as a pale translucent plate.
            foreach (var (f, k) in r.Plans[i].Footprints.Where(f => f.OpenToBelow && !f.Resolved.IsEmpty).Select((f, k) => (f, k)))
                sheet.Paint.Add((layer, new Spec($"{ln}_ATRIUM_{k + 1:00}", proj.Plane(f.Resolved, z - t), d.Swatch("AXON_ATRIUM", eb.AtriumFill),
                    null, 0, Opacity: eb.AtriumOpacity)));
            var mine = blocks.Where(b => b.LevelIndex == i).ToList();
            var prisms = ProgramPrisms(d, ctx, mine, _ => (z - t, z), eb.SideDarken, coreOverride: false).ToList();
            var faces = AxonFaces.Render(proj, prisms);
            foreach (var f in faces)
                sheet.Paint.Add((layer, new Spec(f.Name, f.Screen, f.Fill, white, eb.EdgePt, Opacity: eb.PlateOpacity)));
            CentredLabels(ctx, proj, faces, sheet, layer, "PD Plane Label B", "PD Plane Label B Small");
            sheet.Tags.Add((i, z));
            if (n + 1 >= levels.Count) continue;

            // Tall spaces: atrium edges as thin vertical lines up to their top; cores / stairs as heavy shafts.
            var up = levels[n + 1];
            foreach (var (shape, top, name) in Connectors(ctx, blocks, i, up, Lift))
            {
                if (name.StartsWith("TALL"))
                {
                    var hull = AxonFaces.Simplify(shape).ConvexHull();
                    foreach (var (c, m) in hull.Coordinates.Distinct().Select((c, m) => (c, m)))
                        sheet.Paint.Add((layer, new Spec($"{ln}_{name}_EDGE_{m:00}", Seg(proj, c.X, c.Y, z, top - t), null,
                            d.Gray(eb.AtriumEnvelope), eb.AtriumEnvelopePt, Closed: false)));
                    continue;
                }
                var p = shape.InteriorPoint.Coordinate;
                sheet.Paint.Add((layer, new Spec($"{ln}_SHAFT_{name}", Seg(proj, p.X, p.Y, z, top - t), null,
                    d.Swatch("AXON_SHAFT", eb.ShaftStroke), eb.ShaftPt, Closed: false)));
            }
        }
        return sheet;
    }

    // ---------------------------------------------------------------- floor keys

    /// <summary>One sheet per level: the building as dashed hidden lines (below the plate / above it), that level's plate in colour.</summary>
    static List<Sheet> Levels(PD.Core.Drawing.Drawing d, ExportContext ctx, AxonProjection proj, List<ProgramBlock> blocks)
    {
        var r = ctx.Result;
        var ax = ctx.Style.Axon;
        var t = ax.Exploded.SlabThicknessFt;
        var ground = ctx.Rules.Levels.GroundElevationFt;
        var ghost = AxonFaces.Render(proj, blocks.Select((b, n) => new Prism($"G{n:000}", b.Geometry, b.Z0, b.Z1, null, null, null, b.LevelIndex.ToString())));
        var sheets = new List<Sheet>();
        foreach (var i in Plates(blocks))
        {
            var lvl = r.Levels[i];
            var n = SectionDrawing.Safe(lvl.Name);
            var sheet = new Sheet { Name = $"AXON {lvl.Name}", Prefix = $"AXK_{n}_", Title = $"{ax.TitleLevels} {PlanDrawing.Title(ctx.Style.Plan.LevelTitleFormat, lvl.Name)}" };
            Site(d, ctx, proj, blocks, sheet, ground, flatContext: true, family: "axon", opacity: 1);
            var stroke = d.Gray(ax.HiddenStroke);
            var lowerEdges = AxonFaces.Edges(ghost.Where(f => int.Parse(f.Prism.Group) < i).Select(f => f.Screen));
            if (!lowerEdges.IsEmpty) sheet.Paint.Add(("AXON_GHOST_BELOW", new Spec("GHOST_BELOW", lowerEdges, null, stroke, ax.HiddenPt, ax.HiddenDash, Closed: false)));
            var prisms = ProgramPrisms(d, ctx, blocks.Where(b => b.LevelIndex == i), _ => (lvl.Elevation - t, lvl.Elevation), ax.SideDarken, coreOverride: true).ToList();
            var faces = AxonFaces.Render(proj, prisms);
            PlateFaces(d, ctx, faces, sheet, "PD_PROGRAM", "");
            FrontCornerLabels(ctx, proj, faces, sheet, "PD_LABELS");
            var upperEdges = AxonFaces.Edges(ghost.Where(f => int.Parse(f.Prism.Group) >= i).Select(f => f.Screen));
            if (!upperEdges.IsEmpty) sheet.Paint.Add(("AXON_GHOST_ABOVE", new Spec("GHOST_ABOVE", upperEdges, null, stroke, ax.HiddenPt, ax.HiddenDash, Closed: false)));
            sheet.Tags.Add((i, lvl.Elevation));
            sheets.Add(sheet);
        }
        return sheets;
    }

    // ---------------------------------------------------------------- spacing

    static List<int> Plates(List<ProgramBlock> blocks) => blocks.Select(b => b.LevelIndex).Distinct().OrderBy(i => i).ToList();

    /// <summary>Drawn height of each plate: the first at its real elevation, each next one a pitch above the one below.</summary>
    static Dictionary<int, double> Lifted(AnalysisResult r, List<ProgramBlock> blocks, List<int> levels, Func<NtsGeometry, double, double> pitch)
    {
        var lifted = new Dictionary<int, double>();
        foreach (var (i, n) in levels.Select((i, n) => (i, n)))
        {
            if (n == 0) { lifted[i] = r.Levels[i].Elevation; continue; }
            var below = levels[n - 1];
            var plate = AxonFaces.Union(blocks.Where(b => b.LevelIndex == below).Select(b => b.Geometry));
            lifted[i] = lifted[below] + pitch(plate, Math.Max(1, r.Levels[i].Elevation - r.Levels[below].Elevation));
        }
        return lifted;
    }

    /// <summary>Page height (screen feet) of a plate's top.</summary>
    static double Depth(AxonProjection proj, NtsGeometry plate) => plate.IsEmpty ? 0 : proj.Plane(plate, 0).EnvelopeInternal.Height;

    /// <summary>Real elevation → drawn height: piecewise linear through the plates (tall spaces stretch with it).</summary>
    static double LiftZ(AnalysisResult r, List<int> levels, Dictionary<int, double> lifted, double z)
    {
        var pts = levels.Select(i => (e: r.Levels[i].Elevation, z: lifted[i])).ToList();
        if (z <= pts[0].e) return pts[0].z + (z - pts[0].e);
        for (var k = 0; k + 1 < pts.Count; k++)
            if (z <= pts[k + 1].e)
                return pts[k].z + (z - pts[k].e) * (pts[k + 1].z - pts[k].z) / Math.Max(1e-6, pts[k + 1].e - pts[k].e);
        return pts[^1].z + (z - pts[^1].e);
    }

    /// <summary>
    /// Vertical connectors from plate i: core / stair footprints shared with the next plate ("CONNECTOR…"), and tall
    /// spaces of this plate ("TALL…") stretched to their drawn top.
    /// </summary>
    static IEnumerable<(NtsGeometry Shape, double Top, string Name)> Connectors(ExportContext ctx, List<ProgramBlock> blocks, int i, int up, Func<double, double> lift)
    {
        var r = ctx.Result;
        var programs = ctx.Style.Axon.ConnectorPrograms.ToHashSet(StringComparer.OrdinalIgnoreCase);
        NtsGeometry Cores(int lvl) => AxonFaces.Union(blocks.Where(b => b.LevelIndex == lvl && programs.Contains(b.ProgramKey)).Select(b => b.Geometry));
        var shared = AxonFaces.Safe(() => Cores(i).Intersection(Cores(up)), MeshSlicer.Factory.CreatePolygon());
        var k = 0;
        foreach (var part in AxonFaces.Polygons(shared).Where(p => p.Area >= 1))
            yield return (part, lift(r.Levels[up].Elevation), $"CONNECTOR_{++k:00}");
        k = 0;
        foreach (var b in blocks.Where(b => b.LevelIndex == i && b.Z1 > r.Levels[up].Elevation + 0.5))
            yield return (b.Geometry, lift(b.Z1), $"TALL_{++k:00}");
    }

    // ---------------------------------------------------------------- shared

    /// <summary>Program prisms: top in the program colour, sides darkened (own global swatches), label = tab 4 name.</summary>
    static IEnumerable<Prism> ProgramPrisms(PD.Core.Drawing.Drawing d, ExportContext ctx, IEnumerable<ProgramBlock> blocks, Func<ProgramBlock, (double Z0, double Z1)> band,
        double[] darken, bool coreOverride)
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
            var core = b.ProgramKey == "CORE";
            var (fill, _, _, _) = ctx.ProgramPaint(d, b.ProgramKey);
            if (core && coreOverride && !string.IsNullOrEmpty(ax.CoreFill)) fill = d.Swatch("PRG_CORE_AXON", ax.CoreFill);
            var hex = fill == null ? null : d.Swatches.First(s => s.Name == fill).Hex;
            var tag = $"_{(int)Math.Round(darken.ElementAtOrDefault(0) * 100)}_{(int)Math.Round(darken.ElementAtOrDefault(1) * 100)}";
            var side1 = hex == null ? null : d.Swatch(fill + "_SIDE1" + tag, Darken(hex, darken.ElementAtOrDefault(0)));
            var side2 = hex == null ? null : d.Swatch(fill + "_SIDE2" + tag, Darken(hex, darken.ElementAtOrDefault(1)));
            var text = core ? ax.CoreLabel : row?.Label ?? ctx.ProgramLabel(b.ProgramKey);
            var (z0, z1) = band(b);
            yield return new Prism(id, b.Geometry, z0, z1, fill, side1, side2, "PRG", text, core ? "PD Core Label" : null);
        }
    }

    /// <summary>Labels centred at the widest visible spot of each top, along its long side (stacked, exploded B, cores).</summary>
    static void CentredLabels(ExportContext ctx, AxonProjection proj, List<VisibleFace> faces, Sheet sheet, string layer, string style, string small)
    {
        var f = ctx.Style.Fonts;
        foreach (var group in faces.Where(x => x.IsTop && x.Prism.Label != null).GroupBy(x => x.Prism))
        {
            var p = group.Key;
            var top = proj.Unplane(AxonFaces.Union(group.Select(x => x.Screen)), p.Z1);
            var text = p.Label!.ToUpperInvariant();
            var styles = p.LabelStyle != null ? new[] { p.LabelStyle } : new[] { style, small };
            foreach (var st in styles)
            {
                var spot = PlanLabels.Fit(top, text, ctx.Pt(Font(f, st).SizePt) / ctx.PtPerFt);
                if (spot == null) continue;
                var m = proj.PlaneTextMatrix(spot.AngleDeg);
                if (m[0] < 0) m = proj.PlaneTextMatrix(spot.AngleDeg + 180); // read left → right on the page
                sheet.Paint.Add((layer, new Spec(p.Name + "_LBL", MeshSlicer.Factory.CreatePolygon(), null, null, 0,
                    Text: spot.Text, TextAt: proj.Project(spot.X, spot.Y, p.Z1), Matrix: m, Style: st)));
                break;
            }
        }
    }

    /// <summary>
    /// AXON_STYLE §6: label at the top's front corner (nearest the viewer, never under the plate above), along the
    /// front edge it fits, inset 8 pt both ways: text reading away from the corner starts there (left-aligned), text
    /// reading towards it ends there (right-aligned). 20 pt, else 16 pt; length ≤ 60 % of the edge, height ≤ 45 % of
    /// the short side. Cores are centred.
    /// </summary>
    static void FrontCornerLabels(ExportContext ctx, AxonProjection proj, List<VisibleFace> faces, Sheet sheet, string layer)
    {
        var ex = ctx.Style.Axon.Exploded;
        var f = ctx.Style.Fonts;
        var k = ctx.PtPerFt;
        var inset = ctx.Pt(ex.LabelInsetPt) / k;
        foreach (var group in faces.Where(x => x.IsTop && x.Prism.Label != null).GroupBy(x => x.Prism))
        {
            var p = group.Key;
            if (p.LabelStyle == "PD Core Label")
            {
                CentredLabels(ctx, proj, group.ToList(), sheet, layer, "PD Core Label", "PD Core Label");
                continue;
            }
            var text = p.Label!.ToUpperInvariant();
            var visible = AxonFaces.Simplify(proj.Unplane(AxonFaces.Union(group.Select(x => x.Screen)), p.Z1));
            if (AxonFaces.Polygons(visible).OrderByDescending(x => x.Area).FirstOrDefault() is not { } top) continue;
            var shortSide = new NetTopologySuite.Algorithm.MinimumDiameter(top).Length;
            var ring = top.ExteriorRing.Coordinates[..^1];
            if (ring.Length < 3) continue;
            var ci = Enumerable.Range(0, ring.Length).OrderByDescending(i => proj.Depth(ring[i].X, ring[i].Y, p.Z1)).First();
            var corner = ring[ci];
            var neighbours = new[] { ring[(ci + 1) % ring.Length], ring[(ci + ring.Length - 1) % ring.Length] };
            var placed = false;
            foreach (var (style, sizePt) in new[] { ("PD Plane Label", f.PlaneLabel.SizePt), ("PD Plane Label Small", f.PlaneLabelSmall.SizePt) })
            {
                var size = ctx.Pt(sizePt) / k;
                if (size > 0.45 * shortSide) continue;
                for (var e = 0; e < 2 && !placed; e++)
                {
                    var (along, other) = (neighbours[e], neighbours[1 - e]);
                    double vx = along.X - corner.X, vy = along.Y - corner.Y, len = Math.Sqrt(vx * vx + vy * vy);
                    if (len < 1e-6 || TextFit.Width(text, size) > ex.LabelFitRatio * len) continue;
                    (vx, vy) = (vx / len, vy / len);
                    // Reading direction: left → right on the page.
                    var (sx, _) = proj.Project(vx, vy, 0);
                    var (rx, ry) = sx >= 0 ? (vx, vy) : (-vx, -vy);
                    // The text's "up" must point into the block.
                    var (ux, uy) = (-ry, rx);
                    if ((other.X - corner.X) * ux + (other.Y - corner.Y) * uy <= 0) continue;
                    var ax0 = corner.X + vx * inset + ux * inset;
                    var ay0 = corner.Y + vy * inset + uy * inset;
                    var angle = Math.Atan2(ry, rx) * 180 / Math.PI;
                    sheet.Paint.Add((layer, new Spec(p.Name + "_LBL", MeshSlicer.Factory.CreatePolygon(), null, null, 0, Text: text,
                        TextAt: proj.Project(ax0, ay0, p.Z1), Matrix: proj.PlaneTextMatrix(angle), Style: style, Align: sx >= 0 ? "left" : "right")));
                    placed = true;
                }
                if (placed) break;
            }
            // Nothing fits at the corner: centred (wrapped) at the secondary size, as in the reference's small blocks.
            if (!placed) CentredLabels(ctx, proj, group.ToList(), sheet, layer, "PD Plane Label Small", "PD Plane Label Small");
        }
    }

    static Config.Font Font(Config.Fonts f, string style) => style switch
    {
        "PD Plane Label Small" => f.PlaneLabelSmall,
        "PD Plane Label B" => f.PlaneLabelB,
        "PD Plane Label B Small" => f.PlaneLabelBSmall,
        "PD Core Label" => f.CoreLabel,
        _ => f.PlaneLabel,
    };

    /// <summary>
    /// Site plate and site background at <paramref name="z"/>, other buildings (white prisms for hidden-face removal
    /// with our masses, or flat footprints), and lollipop trees. Returns the context prisms.
    /// </summary>
    /// <summary>How far the site and the context reach around our building, how tall context is drawn, and people.</summary>
    sealed record SiteReach(double SiteMarginFt, double ContextMarginFt, double ContextHeightFt, double MaxContextHeightFt, bool People)
    {
        public static SiteReach Of(Config.AxonStyle ax) => new(ax.SiteMarginFt, ax.ContextMarginFt, ax.ContextHeightFt, double.PositiveInfinity, false);
    }

    static List<Prism> Site(PD.Core.Drawing.Drawing d, ExportContext ctx, AxonProjection proj, List<ProgramBlock> blocks, Sheet sheet, double z,
        bool flatContext, string family, double opacity, (string Fill, string Stroke, double Pt)? plane = null, SiteReach? reach = null)
    {
        var r = ctx.Result;
        var ax = ctx.Style.Axon;
        reach ??= SiteReach.Of(ax);
        var own = new Envelope();
        foreach (var b in blocks) own.ExpandToInclude(b.Geometry.EnvelopeInternal);
        var site = new Envelope(own);
        site.ExpandBy(reach.SiteMarginFt);
        var siteGeom = MeshSlicer.Factory.ToGeometry(site);
        var (pf, ps, pp) = plane ?? (ax.SiteFill, ax.SiteStroke ?? "", ax.SiteStroke == null ? 0 : ax.ContextPt);
        sheet.Paint.Add(("AXON_SITE", new Spec("SITE", proj.Plane(siteGeom, z), d.Swatch("AXON_SITE", pf), d.Gray(ps), pp)));
        var near = new Envelope(own);
        near.ExpandBy(reach.ContextMarginFt);
        var items = PlanBackground.Collect(r, near, ctx.Style.Plan.TreeDiameterFt);
        foreach (var (bg, n) in items.Where(b => b.Role != Roles.Building && !IsTree(b) && site.Intersects(b.Geometry.EnvelopeInternal)).Select((b, n) => (b, n)))
        {
            var g = AxonFaces.Safe(() => bg.Geometry.Intersection(siteGeom), bg.Geometry);
            var (fill, stroke, pt, dash, op) = PlanBackground.Paint(d, ctx.Style, bg, family);
            sheet.Paint.Add(("AXON_SITE", new Spec($"BG_{n:000}", proj.Plane(g, z), fill, stroke, pt, dash, !bg.IsLine, op * opacity)));
        }

        // Other buildings near ours; never over our own footprint.
        var ours = AxonFaces.Union(blocks.Select(b => b.Geometry)).Buffer(1);
        var paint = PlanBackground.PaintOf(ctx.Style, Roles.Building, family);
        var white = d.Swatch("AXON_CONTEXT", paint.Fill ?? "#FFFFFF");
        var context = new List<Prism>();
        var k = 0;
        foreach (var bg in items.Where(b => b.Role == Roles.Building && !b.IsLine))
        {
            var foot = AxonFaces.Safe(() => bg.Geometry.Difference(ours), bg.Geometry);
            if (foot.IsEmpty || foot.Area < 4) continue;
            var name = $"CONTEXT_{++k:000}";
            if (flatContext)
            {
                sheet.Paint.Add(("AXON_CONTEXT", new Spec(name, proj.Plane(foot, z), white, d.Gray(ax.ContextStroke), ax.ContextPt, Opacity: opacity)));
                continue;
            }
            var z0 = Math.Max(z, bg.Z0);
            var z1 = reach.ContextHeightFt > 0 ? Math.Min(bg.Z, z0 + reach.ContextHeightFt) : Math.Min(bg.Z, z0 + reach.MaxContextHeightFt);
            if (z1 > z0 + 0.1) context.Add(new Prism(name, foot, z0, z1, white, white, white, "CTX", bg.Name));
        }

        // Lollipop trees (site axon), far → near, on top.
        var tr = ax.Trees;
        var rng = new Random(17);
        foreach (var (tree, n) in items.Where(IsTree).Where(b => site.Intersects(b.Geometry.EnvelopeInternal))
                     .OrderBy(b => proj.Depth(b.Geometry.Centroid.X, b.Geometry.Centroid.Y, z)).Select((b, n) => (b, n)))
        {
            var c = tree.Geometry.Centroid.Coordinate;
            var trunkTop = z + tr.TrunkHFt;
            var (tx, ty) = proj.Project(c.X, c.Y, trunkTop + tr.CanopyHFt / 2);
            var canopy = tr.Canopies.Count > 0 ? tr.Canopies[rng.Next(tr.Canopies.Count)] : "#C4ECBC";
            var swatch = d.Swatch("AXON_TREE_" + canopy.TrimStart('#'), canopy);
            var treeDepth = proj.Depth(c.X, c.Y, z);
            sheet.Paint.Add(("AXON_TREE", new Spec($"TREE_{n:000}_TRUNK", Seg(proj, c.X, c.Y, z, trunkTop), null, d.Gray(tr.Trunk), tr.TrunkPt, Closed: false, Depth: treeDepth)));
            var ellipse = MeshSlicer.Factory.CreatePolygon(Enumerable.Range(0, 25).Select(j =>
            {
                var a = 2 * Math.PI * (j % 24) / 24;
                return new Coordinate(tx + tr.CanopyWFt / 2 * Math.Cos(a), ty + tr.CanopyHFt / 2 * Math.Sin(a));
            }).ToArray());
            sheet.Paint.Add(("AXON_TREE", new Spec($"TREE_{n:000}", ellipse, swatch, d.Swatch("AXON_TREE_" + canopy.TrimStart('#') + "_LINE", Darken(canopy, tr.StrokeDarken)),
                tr.StrokePt, Opacity: tr.Opacity, Depth: treeDepth)));
        }
        if (reach.People) People(d, ctx, sheet, proj, siteGeom, ours, z);
        return context;
    }

    /// <summary>PRD 4: people standing on the site plane around our building (never on top of it).</summary>
    static void People(PD.Core.Drawing.Drawing d, ExportContext ctx, Sheet sheet, AxonProjection proj, NtsGeometry site, NtsGeometry ours, double z)
    {
        if (!PD.Core.Entourage.EntouragePlacer.Enabled(ctx)) return;
        var e = ctx.Style.Entourage;
        // Our plaza and the ground right around it, never on top of a building.
        var near = AxonFaces.Safe(() => ours.Buffer(ctx.Style.Plan.ContextMarginFt).Intersection(site), site);
        var open = MeshSlicer.Polygonal(AxonFaces.Safe(() => near.Difference(ours), near));
        var n = 0;
        foreach (var f in PD.Core.Entourage.EntouragePlacer.Outdoors(ctx, open, sheet.Prefix + "SITE")
                     .OrderBy(f => proj.Depth(f.X, f.Y, z)))
        {
            var (px, py) = proj.Project(f.X, f.Y, z);
            var (hx, hy) = proj.Project(f.X, f.Y, z + e.HeightFt);
            var pts = PD.Core.Entourage.EntourageSymbols.Projected(px, py, (hx - px, hy - py), f.Variant, f.Mirror);
            if (pts.Length == 0) continue;
            NtsGeometry ring = MeshSlicer.Factory.CreatePolygon(pts.Select(q => new Coordinate(q[0], q[1]))
                .Append(new Coordinate(pts[0][0], pts[0][1])).ToArray());
            if (!ring.IsValid) ring = ring.Buffer(0); // the silhouette's legs cross: make it a clean shape for the overlays
            sheet.Paint.Add((PD.Core.Entourage.EntouragePlacer.Layer,
                new Spec($"PERSON_{++n:000}", ring, d.Swatch("ENTOURAGE", e.Color), null, 0, Opacity: e.Opacity, Depth: proj.Depth(f.X, f.Y, z))));
        }
    }

    /// <summary>Trees and people standing behind a building lose the part the building hides.</summary>
    static void HideBehind(Sheet sheet, AxonProjection proj, List<VisibleFace> faces)
    {
        for (var i = sheet.Paint.Count - 1; i >= 0; i--)
        {
            var (layer, spec) = sheet.Paint[i];
            if (spec.Depth is not { } depth || spec.Geometry.IsEmpty) continue;
            var left = AxonFaces.Hide(proj, spec.Geometry, depth, faces);
            if (left == null) sheet.Paint.RemoveAt(i);
            else if (!ReferenceEquals(left, spec.Geometry)) sheet.Paint[i] = (layer, spec with { Geometry = left });
        }
    }

    static bool IsTree(BackgroundItem b) => b.Role is Roles.Tree or Roles.NcTree;

    static NtsGeometry Seg(AxonProjection proj, double x, double y, double z0, double z1)
    {
        var (ax0, ay0) = proj.Project(x, y, z0);
        var (ax1, ay1) = proj.Project(x, y, z1);
        return MeshSlicer.Factory.CreateLineString(new[] { new Coordinate(ax0, ay0), new Coordinate(ax1, ay1) });
    }

    /// <summary>Artboards (one per sheet, same size), items, level column, titles.</summary>
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
                d.Notes.Add($"{kind} axon at {ctx.ScaleText} is larger than {ctx.Settings.Output.Artboard}; use \"Fit content\" or a smaller scale.");
        }
        else
        {
            abW = 2 * m + w + colR;
            abH = 2 * m + h + titleBand;
            x0 = m;
            y0 = m;
        }
        var map = new PageMap(x0 - env.MinX * k, y0, k, env.MaxY, env.Width, flip: false);

        // Layer order (AXON_STYLE §9, bottom → top): site, context, hidden below, program / plates, lines, trees, facade, hidden above, labels, annotation.
        var used = sheets.SelectMany(s => s.Paint.Select(x => x.Layer)).Distinct().ToList();
        string[] first = { "AXON_SITE", "AXON_CONTEXT", "AXON_GHOST_BELOW", "PD_PROGRAM", "AXON_LINE" };
        string[] last = { "AXON_TREE", PD.Core.Entourage.EntouragePlacer.Layer, "FACADE", "AXON_GHOST_ABOVE" };
        foreach (var n in first.Where(used.Contains)) d.Layer(n);
        foreach (var n in used.Where(n => !first.Contains(n) && !last.Contains(n) && n != "PD_LABELS")) d.Layer(n);
        foreach (var n in last.Where(used.Contains)) d.Layer(n);
        foreach (var n in new[] { "PD_LABELS", "PD_ANNOTATION" }) d.Layer(n);

        foreach (var (s, a) in sheets.Select((s, a) => (s, a)))
        {
            d.Artboards.Add(new Artboard(s.Name, Math.Ceiling(abW), Math.Ceiling(abH)));
            foreach (var (layer, spec) in s.Paint)
            {
                DItem item;
                if (spec.Text != null)
                {
                    item = SectionDrawing.Text(s.Prefix + spec.Name, map.X(spec.TextAt!.Value.X), map.Y(spec.TextAt!.Value.Y), spec.Text, spec.Style ?? "PD Plane Label",
                        spec.Align, center: spec.Align == "center");
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
            LevelColumn(d, ctx, proj, s, map, x0 + w + ctx.Pt(s.Tag == TagStyle.A ? ax.Exploded.ColumnOffsetPt : 72), a);
            var ty = y0 + h + ctx.Pt(40);
            var anno = d.Layer("PD_ANNOTATION");
            anno.Items.Add(At(SectionDrawing.Text(s.Prefix + "TITLE", x0, ty, s.Title, "PD Title", "left"), a));
            anno.Items.Add(At(SectionDrawing.Text(s.Prefix + "SCALE", x0, ty + ctx.Pt(18), $"SCALE {ctx.ScaleText}", "PD Note", "left"), a));
        }
    }

    /// <summary>
    /// Level tags. A (AXON_STYLE §7): leader from the plate's rightmost top corner along the drawing's down-right axis to
    /// the column; yellow square + "Level 7". B (SITE_STYLE §3): leader to the lower right at 30°; orange square + "LEVEL 6".
    /// </summary>
    static void LevelColumn(PD.Core.Drawing.Drawing d, ExportContext ctx, AxonProjection proj, Sheet s, PageMap map, double colX, int a)
    {
        var ax = ctx.Style.Axon;
        var ex = ax.Exploded;
        var eb = ax.ExplodedB;
        var layer = d.Layer("PD_ANNOTATION");
        var isA = s.Tag == TagStyle.A;
        var (dx, dy) = isA ? proj.DownRightAxis() : (Math.Cos(Math.PI / 6), -0.5);
        var sq = ctx.Pt(isA ? ex.LevelMarkerPt : eb.LevelMarkerPt);
        var gap = ctx.Pt(isA ? ex.LevelMarkerGapPt : eb.LevelGapPt);
        var font = isA ? ctx.Style.Fonts.Level : ctx.Style.Fonts.LevelTag;
        foreach (var (i, z) in s.Tags)
        {
            var lvl = ctx.Result.Levels[i];
            var plate = ctx.Result.Plans[i].Union;
            if (plate.IsEmpty) continue;
            var right = plate.Coordinates.Select(c => proj.Project(c.X, c.Y, z)).OrderByDescending(q => q.X).First();
            double x = map.X(right.X), y = map.Y(right.Y);
            // Run along (dx, dy) (page y up) until the column.
            var end = Math.Max(x, colX);
            var ey = y - dy * (end - x) / Math.Max(1e-6, dx);
            var n = SectionDrawing.Safe(lvl.Name);
            layer.Items.Add(At(new DItem
            {
                Kind = ItemKind.Path, Name = $"{s.Prefix}LVL_{n}_LDR", Closed = false,
                Stroke = d.Gray(isA ? ex.LeaderStroke : eb.LeaderStroke), StrokePt = isA ? ex.LeaderPt : eb.LeaderPt,
                Paths = new() { new[] { new[] { x, y }, new[] { end, ey } } },
            }, a));
            var mx = end + ctx.Pt(2);
            layer.Items.Add(At(new DItem
            {
                Kind = ItemKind.Path, Name = $"{s.Prefix}LVL_{n}_MARK", Fill = d.Swatch(isA ? "PD_LEVEL_MARKER" : "PD_LEVEL_MARKER_B", isA ? ex.LevelMarker : eb.LevelMarker),
                Stroke = isA ? d.Gray(ex.LevelMarkerStroke) : null, StrokePt = isA ? ex.LevelMarkerStrokePt : 0,
                Paths = new() { new[] { new[] { mx, ey - sq / 2 }, new[] { mx + sq, ey - sq / 2 }, new[] { mx + sq, ey + sq / 2 }, new[] { mx, ey + sq / 2 } } },
            }, a));
            layer.Items.Add(At(SectionDrawing.Text($"{s.Prefix}LVL_{n}_TXT", mx + sq + gap, ey + ctx.Pt(font.SizePt) * 0.35,
                PlanDrawing.Title(ctx.Style.Plan.LevelTitleFormat, lvl.Name), isA ? "PD Level" : "PD Level Tag", "left"), a));
        }
    }

    static string? FloorLine(PD.Core.Drawing.Drawing d, string? fill, double darken)
    {
        var hex = fill == null ? null : d.Swatches.FirstOrDefault(s => s.Name == fill)?.Hex;
        return hex == null ? null : d.Swatch(fill + "_FLOOR", Darken(hex, darken));
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
