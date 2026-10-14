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
        /// <summary>Layers drawn under everything, the site included (exploded basements, user rev 0.8.6).</summary>
        public List<string> UnderSite { get; } = new();
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
            : AxonProjection.FromAngles(ctx.AxonAzimuth() ?? ax.AzimuthDeg, ax.ElevationDeg, ctx.AxonViewDirection(), trueLength: !ax.Foreshorten);
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
        // User rev 0.8.1: 03 shows the building from grade up; basements would stand on the site plate.
        if (!(site ? ax.Site.ShowBasements : st.ShowBasements))
        {
            var above = AboveGrade(ctx.Result, blocks, ground);
            if (above.Count > 0) blocks = above;
        }
        var full = site ? ax.Site.FullHeightBlocks : st.FullHeightBlocks;
        // User rev 0.8.3: the site axon fills its page — the ground under the whole frame, everything clipped to it.
        var frame = site && ax.Site.FillPage ? PageFrame(ctx, proj, blocks, ground) : null;
        Envelope? area = null;
        if (frame != null)
        {
            area = new Envelope();
            foreach (var c in frame.Coordinates)
            {
                var (x, y) = proj.Unproject(c.X, c.Y, ground);
                area.ExpandToInclude(x, y);
            }
            area.ExpandBy(10);
        }
        var context = Site(d, ctx, proj, blocks, sheet, ground, flatContext: false, family: "axon", opacity: 1, reach: reach, area: area);
        Func<ProgramBlock, (double, double)> band = full ? b => FullHeight(ctx.Result, b) : b => (b.Z0, b.Z1);
        // Open slabs and glass at their real height (user rev 0.8.6: every floor edge the model shows, as in Rhino);
        // the blocks stretched under them stop at their underside.
        var plates = UnderScreens(ctx, Exposed(d, ctx, blocks, band: null));
        var program = UnderPlates(ProgramPrisms(d, ctx, blocks, band, st.SideDarken, coreOverride: false).ToList(), plates);
        // Screens as solid walls with a white floor, open on top (user rev 0.8.3).
        var screens = ScreenPrisms(d, ctx, ScreenEnclosures.For(ctx).Where(e => blocks.Any(b => b.LevelIndex == e.LevelIndex))).ToList();
        // Glass (flat canopies and the facade glass) is see-through (user rev 0.8.6): it hides nothing, so it is left
        // out of the opaque pass and drawn over it at its own opacity where nothing opaque stands in front of it.
        var glass = plates.Where(IsGlass).Concat(ax.FacadeVeil ? FacadeGlass(d, ctx, blocks) : Enumerable.Empty<Prism>()).ToList();
        var solid = context.Concat(program).Concat(screens).Concat(plates.Where(q => !IsGlass(q))).ToList();
        var faces = AxonFaces.Render(proj, solid);
        var glassFaces = glass.Count == 0 ? new List<VisibleFace>()
            : AxonFaces.Render(proj, solid.Concat(glass)).Where(f => IsGlass(f.Prism)).ToList();
        // Trees stop short of the building by axon.treeClearPt (user rev 0.8.4: room for the black outline).
        HideBehind(sheet, proj, faces, ax.TreeClearPt / ctx.PtPerFt);
        var line = d.Gray(ax.LineColor);
        var ctxPaint = PlanBackground.PaintOf(ctx.Style, Roles.Building, "axon");
        foreach (var f in faces)
        {
            if (f.Prism.Group == "CTX")
                sheet.Paint.Add(("AXON_CONTEXT", new Spec(f.Name, f.Screen, f.Fill, d.Gray(ctxPaint.Stroke), ctxPaint.StrokePt ?? ax.ContextPt)));
            else if (f.Prism.Group == "SCR")
                sheet.Paint.Add(("PD_PROGRAM", new Spec(f.Name, f.Screen, f.Fill, d.Gray(ctx.Style.Screen.AxonLineColor), ctx.Style.Screen.AxonLinePt)));
            else if (f.Prism.Group == "EXP")
                sheet.Paint.Add(("PD_PROGRAM", new Spec(f.Name, f.Screen, f.Fill, d.Gray(ctx.Style.Exposed.LineColor), ctx.Style.Exposed.LinePt)));
            else
                // Faces carry the floor lines: 0.5 pt in their own colour, 35 % darker.
                sheet.Paint.Add(("PD_PROGRAM", new Spec(f.Name, f.Screen, f.Fill, FloorLine(d, f.Fill, st.FloorLineDarken), st.FloorLinePt)));
        }
        PaintGlass(d, ctx, proj, sheet, glassFaces);
        ScreensAndMassing(d, ctx, proj, sheet, faces);
        var withScreens = faces.Where(f => f.Prism.Group is "PRG" or "SCR" or "EXP").ToList();
        var mine = faces.Where(f => f.Prism.Group == "PRG").ToList();
        var folds = AxonFaces.Folds(withScreens);
        if (!folds.IsEmpty) sheet.Paint.Add(("AXON_LINE", new Spec("FOLDS", folds, null, line, st.EdgePt, Closed: false)));
        // The glass is part of the envelope: the outline runs round it too.
        var outline = AxonFaces.Union(withScreens.Concat(glassFaces).Select(f => f.Screen));
        if (full)
        {
            // The heavy line only on the outer silhouette (no holes, no hairline gaps between faces).
            outline = AxonFaces.Safe(() => outline.Buffer(0.05).Buffer(-0.05), outline);
            outline = MeshSlicer.Factory.BuildGeometry(AxonFaces.Polygons(outline).Select(p => (NtsGeometry)MeshSlicer.Factory.CreatePolygon(p.Shell)).ToList());
            var (lineColor, linePt) = site ? (ax.Site.LevelLineColor, ax.Site.LevelLinePt) : (st.LevelLineColor, st.LevelLinePt);
            LevelLines(d, proj, sheet, mine, ground, lineColor, linePt);
        }
        if (!outline.IsEmpty) sheet.Paint.Add(("AXON_LINE", new Spec("OUTLINE", outline, null, line, st.SilhouettePt)));
        if (st.Labels) CentredLabels(ctx, proj, mine, sheet, "PD_LABELS", "PD Plane Label", "PD Plane Label Small");

        // Building names on the roofs, horizontal (not sheared).
        foreach (var c in context.Where(c => SectionDrawing.IsDisplayName(c.Label)))
        {
            var pt = c.Plan.InteriorPoint.Coordinate;
            sheet.Paint.Add(("PD_LABELS", new Spec(c.Name + "_NAME", MeshSlicer.Factory.CreatePolygon(), null, null, 0,
                Text: c.Label!.ToUpperInvariant(), TextAt: proj.Project(pt.X, pt.Y, c.Z1), Style: "PD Context Label")));
        }

        if (frame != null) ClipToFrame(sheet, frame);

        return sheet;
    }

    /// <summary>
    /// Screen frame of the site axon's page (screen feet): the rectangle around our building and its site margin as
    /// projected (at least axon.site.pageWidthIn × pageHeightIn), or the fixed artboard less margins and title band.
    /// </summary>
    static Polygon PageFrame(ExportContext ctx, AxonProjection proj, List<ProgramBlock> blocks, double ground)
    {
        var pg = ctx.Style.Page;
        var k = ctx.PtPerFt;
        var own = new Envelope();
        foreach (var b in blocks) own.ExpandToInclude(b.Geometry.EnvelopeInternal);
        var top = blocks.Count == 0 ? ground : blocks.Max(b => b.Z1);
        var site = new Envelope(own);
        site.ExpandBy(ctx.Style.Axon.Site.SiteMarginFt);
        var scr = new Envelope();
        foreach (var (x, y) in new[] { (site.MinX, site.MinY), (site.MaxX, site.MinY), (site.MaxX, site.MaxY), (site.MinX, site.MaxY) })
        {
            var (sx, sy) = proj.Project(x, y, ground);
            scr.ExpandToInclude(sx, sy);
        }
        foreach (var (x, y) in new[] { (own.MinX, own.MinY), (own.MaxX, own.MinY), (own.MaxX, own.MaxY), (own.MinX, own.MaxY) })
        {
            var (sx, sy) = proj.Project(x, y, top);
            scr.ExpandToInclude(sx, sy);
        }
        double w, h;
        if (ctx.ArtboardSize is { } ab)
        {
            w = Math.Max(72, ab.W - 2 * pg.MarginPt) / k;
            h = Math.Max(72, ab.H - 2 * pg.MarginPt - ctx.Pt(pg.TitleBandPt)) / k;
        }
        else
        {
            w = Math.Max(scr.Width, (ctx.Style.Axon.Site.PageWidthIn * 72 - 2 * pg.MarginPt) / k);
            h = Math.Max(scr.Height, (ctx.Style.Axon.Site.PageHeightIn * 72 - 2 * pg.MarginPt - ctx.Pt(pg.TitleBandPt)) / k);
        }
        double cx = scr.Centre.X, cy = scr.Centre.Y;
        return MeshSlicer.Factory.CreatePolygon(new[]
        {
            new Coordinate(cx - w / 2, cy - h / 2), new Coordinate(cx + w / 2, cy - h / 2), new Coordinate(cx + w / 2, cy + h / 2),
            new Coordinate(cx - w / 2, cy + h / 2), new Coordinate(cx - w / 2, cy - h / 2),
        });
    }

    /// <summary>Site, context, trees and people clipped to the page frame; context names outside it dropped.</summary>
    static void ClipToFrame(Sheet sheet, Polygon frame)
    {
        string[] clip = { "AXON_SITE", "AXON_CONTEXT", "AXON_TREE", PD.Core.Entourage.EntouragePlacer.Layer };
        var fe = frame.EnvelopeInternal;
        for (var i = sheet.Paint.Count - 1; i >= 0; i--)
        {
            var (layer, spec) = sheet.Paint[i];
            if (spec.Text != null)
            {
                if (spec.Name.EndsWith("_NAME") && spec.TextAt is { } at && !fe.Contains(at.X, at.Y)) sheet.Paint.RemoveAt(i);
                continue;
            }
            if (!clip.Contains(layer) || spec.Geometry.IsEmpty) continue;
            if (fe.Contains(spec.Geometry.EnvelopeInternal)) continue;
            var g = AxonFaces.Safe(() => spec.Closed ? MeshSlicer.Polygonal(spec.Geometry.Intersection(frame)) : MeshSlicer.Lineal(spec.Geometry.Intersection(frame)),
                MeshSlicer.Factory.CreatePolygon());
            if (g.IsEmpty) sheet.Paint.RemoveAt(i);
            else sheet.Paint[i] = (layer, spec with { Geometry = g });
        }
    }

    /// <summary>
    /// Site axon block band (user rev 0.8, no slabs): from the level line to the next level line whenever the mass
    /// reaches within 3 ft of them (the slab), else the mass's own bottom / top (a penthouse top, a low pavilion).
    /// </summary>
    public static (double Z0, double Z1) FullHeight(AnalysisResult r, ProgramBlock b)
    {
        var lv = r.Levels;
        var z0 = Math.Abs(b.Z0 - lv[b.LevelIndex].Elevation) <= 3 ? lv[b.LevelIndex].Elevation : b.Z0;
        var next = lv.FirstOrDefault(l => !l.IsMezzanine && l.Elevation > b.Z1 - 3 && l.Elevation > z0 + 1);
        var z1 = next != null && next.Elevation - b.Z1 <= 3 ? next.Elevation : b.Z1;
        return (z0, Math.Max(z1, z0 + 0.1));
    }

    /// <summary>
    /// A block stretched floor to floor stops under an open slab or glass lying at its top, only where that plate is
    /// (user rev 0.8.5 / 0.8.6: two tops at the level line let the program show through the roof). The block is split:
    /// the part under the plate ends at the plate's underside, the rest keeps its height (and the label).
    /// </summary>
    public static List<Prism> UnderPlates(List<Prism> blocks, IReadOnlyList<Prism> plates)
    {
        var res = new List<Prism>();
        var empty = MeshSlicer.Factory.CreatePolygon();
        foreach (var p in blocks)
        {
            var rest = p.Plan;
            var pieces = new List<Prism>();
            foreach (var q in plates.Where(q => q.Z0 >= p.Z1 - 2 && q.Z0 < p.Z1 - 0.01 && q.Z0 > p.Z0 + 0.1
                         && q.Plan.EnvelopeInternal.Intersects(p.Plan.EnvelopeInternal)))
            {
                var under = AxonFaces.Safe(() => MeshSlicer.Polygonal(rest.Intersection(q.Plan)), empty);
                if (under.IsEmpty || under.Area < 1) continue;
                pieces.Add(p with { Name = $"{p.Name}_U{pieces.Count + 1}", Plan = under, Z1 = q.Z0, Label = null });
                rest = AxonFaces.Safe(() => MeshSlicer.Polygonal(rest.Difference(q.Plan)), rest);
            }
            if (pieces.Count == 0) { res.Add(p); continue; }
            if (!rest.IsEmpty && rest.Area >= 1) res.Add(p with { Plan = rest });
            else pieces[0] = pieces[0] with { Label = p.Label };
            res.AddRange(pieces);
        }
        return res;
    }

    /// <summary>Blocks of the levels at or above grade (the ground-floor plate and up; see <see cref="GroundAndExcavation"/>).</summary>
    public static List<ProgramBlock> AboveGrade(AnalysisResult r, List<ProgramBlock> blocks, double ground)
    {
        var levels = Plates(blocks);
        if (levels.Count == 0) return blocks;
        var (at, _) = GroundAndExcavation(r, blocks, levels, ground);
        return blocks.Where(b => r.Levels[b.LevelIndex].Elevation >= r.Levels[at].Elevation - 1).ToList();
    }

    /// <summary>
    /// 2 pt line between floors (own layer): the bottom edge of every visible wall of a block that stands above the
    /// ground, kept where that wall is seen.
    /// </summary>
    static void LevelLines(PD.Core.Drawing.Drawing d, AxonProjection proj, Sheet sheet, List<VisibleFace> mine, double ground, string color, double pt)
    {
        if (pt <= 0) return;
        var lines = new List<NtsGeometry>();
        foreach (var f in mine.Where(f => !f.IsTop && f.Prism.Z0 > ground + 0.5))
        {
            var len = Math.Sqrt(f.Normal.X * f.Normal.X + f.Normal.Y * f.Normal.Y);
            if (len < 1e-9) continue;
            foreach (var poly in AxonFaces.Polygons(AxonFaces.Simplify(f.Prism.Plan)))
            foreach (var ring in AxonFaces.Rings(poly))
            {
                var pts = ring.Coordinates;
                for (var i = 0; i + 1 < pts.Length; i++)
                {
                    var (a, q) = (pts[i], pts[i + 1]);
                    var n = (q.Y - a.Y, a.X - q.X);
                    var nl = Math.Sqrt(n.Item1 * n.Item1 + n.Item2 * n.Item2);
                    if (nl < 1e-9) continue;
                    // The wall this face belongs to: same direction and same plane.
                    if (Math.Abs(n.Item1 / nl - f.Normal.X / len) > 1e-3 || Math.Abs(n.Item2 / nl - f.Normal.Y / len) > 1e-3) continue;
                    if (Math.Abs((n.Item1 * a.X + n.Item2 * a.Y) / nl - f.D0 / len) > 0.05) continue;
                    var (ax, ay) = proj.Project(a.X, a.Y, f.Prism.Z0);
                    var (qx, qy) = proj.Project(q.X, q.Y, f.Prism.Z0);
                    var seg = MeshSlicer.Factory.CreateLineString(new[] { new Coordinate(ax, ay), new Coordinate(qx, qy) });
                    var seen = AxonFaces.Safe(() => seg.Intersection(f.Screen.Buffer(0.05)), seg);
                    if (!seen.IsEmpty) lines.Add(seen);
                }
            }
        }
        if (lines.Count == 0) return;
        var all = AxonFaces.Safe(() => NetTopologySuite.Operation.Union.UnaryUnionOp.Union(lines), MeshSlicer.Factory.BuildGeometry(lines));
        sheet.Paint.Add(("AXON_LEVEL_LINE", new Spec("LEVEL_LINES", MeshSlicer.Lineal(all), null, d.Gray(color), pt, Closed: false)));
    }

    /// <summary>
    /// NC.Screen objects as the thin surfaces they are (projected, trimmed where a nearer face hides them — never
    /// extruded) and the NC.Massing envelope as a dashed silhouette over the drawing.
    /// </summary>
    static void ScreensAndMassing(PD.Core.Drawing.Drawing d, ExportContext ctx, AxonProjection proj, Sheet sheet, List<VisibleFace> faces)
    {
        var r = ctx.Result;
        var n = 0;
        var solid = ScreenEnclosures.For(ctx).SelectMany(e => e.ObjectIds).ToHashSet();
        foreach (var o in r.Local.Objects.Where(o => o.Mesh != null && !r.Stray.Contains(o.Id) && o.Role is Roles.Screen or Roles.Massing && !solid.Contains(o.Id)))
        {
            var shape = AxonFaces.Safe(() => MeshProjection.Union(o.Mesh!, proj.Project), MeshSlicer.Factory.CreatePolygon());
            if (shape.IsEmpty) continue;
            var paint = PlanBackground.PaintOf(ctx.Style, o.Role, "axon");
            var b = o.BBox;
            if (o.Role == Roles.Screen)
            {
                var depth = proj.Depth((b.MinX + b.MaxX) / 2, (b.MinY + b.MaxY) / 2, (b.MinZ + b.MaxZ) / 2);
                var seen = AxonFaces.Hide(proj, shape, depth, faces);
                if (seen == null || seen.IsEmpty) continue;
                sheet.Paint.Add(("AXON_SCREEN", new Spec($"SCREEN_{++n:000}", seen, d.Swatch("NC_SCREEN", paint.Fill), d.Swatch("NC_SCREEN_LINE", paint.Stroke),
                    paint.StrokePt ?? 0.5, Opacity: paint.Opacity)));
            }
            else
            {
                var shells = MeshSlicer.Factory.BuildGeometry(AxonFaces.Polygons(shape).Select(p => (NtsGeometry)p.ExteriorRing).ToList());
                sheet.Paint.Add(("AXON_MASSING", new Spec($"MASSING_{++n:000}", shells, null, d.Swatch("NC_MASSING", paint.Stroke ?? "#4A4A4A"),
                    paint.StrokePt ?? 0.75, paint.Dash, Closed: false)));
            }
        }
    }

    /// <summary>
    /// A roof plate under a screen's floor loses that part: the white floor lies on it, and two tops a few inches apart
    /// let the hidden-face order pick the wrong one (user rev 0.8.6, the L11 roof over the penthouse floor).
    /// </summary>
    static List<Prism> UnderScreens(ExportContext ctx, IEnumerable<Prism> plates)
    {
        var floors = ScreenEnclosures.For(ctx).Where(e => e.Encloses && !e.Floor.IsEmpty).ToList();
        var res = new List<Prism>();
        foreach (var q in plates)
        {
            var plan = q.Plan;
            foreach (var e in floors.Where(e => Math.Abs(e.Z0 - q.Z1) <= 1.5 && e.Floor.EnvelopeInternal.Intersects(plan.EnvelopeInternal)))
                plan = AxonFaces.Safe(() => MeshSlicer.Polygonal(plan.Difference(e.Floor)), plan);
            if (!plan.IsEmpty && plan.Area >= 1) res.Add(q with { Plan = plan });
        }
        return res;
    }

    static bool IsGlass(Prism p) => p.Group == "GLS" || p.Group == "EXP" && p.Name.Contains("EXPOSED_GLASS");

    /// <summary>
    /// The facade glass standing on the drawn levels (NC.Facade surfaces, curtain walls: user rev 0.8.6) as thin walls
    /// along their plan trace, from their foot to their top. Flat glass is an exposed surface, not here.
    /// </summary>
    static IEnumerable<Prism> FacadeGlass(PD.Core.Drawing.Drawing d, ExportContext ctx, List<ProgramBlock> blocks)
    {
        var r = ctx.Result;
        var ax = ctx.Style.Axon;
        if (blocks.Count == 0) yield break;
        var bottom = blocks.Min(b => b.Z0) - 1;
        var ids = r.FacadeIds.ToHashSet();
        var fill = d.Swatch("AXON_GLASS", ax.FacadeVeilFill);
        var n = 0;
        foreach (var o in r.Local.Objects.Where(o => (o.Role == Roles.Facade || ids.Contains(o.Id)) && o.Mesh != null && !r.Stray.Contains(o.Id)
                     && o.BBox.SizeZ > 2 && o.BBox.MaxZ > bottom))
        {
            var trace = ScreenEnclosures.Trace(o.Mesh!);
            if (trace.IsEmpty) continue;
            var wall = AxonFaces.Safe(() => MeshSlicer.Polygonal(trace.Buffer(0.1, new NetTopologySuite.Operation.Buffer.BufferParameters
            {
                EndCapStyle = NetTopologySuite.Operation.Buffer.EndCapStyle.Flat,
            })), MeshSlicer.Factory.CreatePolygon());
            if (wall.IsEmpty) continue;
            yield return new Prism($"FACADE_GLASS_{++n:00}", wall, Math.Max(o.BBox.MinZ, bottom), o.BBox.MaxZ, fill, fill, fill, "GLS");
        }
    }

    /// <summary>
    /// The part of the facade glass between <paramref name="z0"/> and <paramref name="z1"/>, moved up by
    /// <paramref name="dz"/> (a storey on its exploded plate, user rev 0.8.7).
    /// </summary>
    static IEnumerable<Prism> GlassBand(IEnumerable<Prism> glass, double z0, double z1, double dz, string prefix)
    {
        foreach (var g in glass)
        {
            double a = Math.Max(g.Z0, z0), b = Math.Min(g.Z1, z1);
            if (b - a < 0.5) continue;
            yield return g with { Name = prefix + g.Name, Z0 = a + dz, Z1 = b + dz };
        }
    }

    /// <summary>Facade glass in the exploded / floor key axons (facade.explodedAxon: "storey" = each storey's glass, "hidden").</summary>
    static bool GlassInExploded(ExportContext ctx) => ctx.Style.Axon.FacadeVeil && !string.Equals(ctx.Style.Facade.ExplodedAxon, "hidden", StringComparison.OrdinalIgnoreCase);

    /// <summary>Glass faces over the drawing at axon.facadeVeil* / exposed.glassOpacity, flat glass with its hatch.</summary>
    static void PaintGlass(PD.Core.Drawing.Drawing d, ExportContext ctx, AxonProjection proj, Sheet sheet, List<VisibleFace> faces, string layer = "FACADE")
    {
        var ax = ctx.Style.Axon;
        var ex = ctx.Style.Exposed;
        foreach (var f in faces)
        {
            if (f.Prism.Group == "GLS")
            {
                if (f.IsTop) continue; // a curtain wall shows its faces, not its 0.2 ft top
                sheet.Paint.Add((layer, new Spec(f.Name, f.Screen, f.Fill, d.Gray(ax.FacadeVeilStroke), ax.FacadeVeilStrokePt, Opacity: ctx.Style.Facade.StackedAxonOpacity)));
            }
            else
                sheet.Paint.Add((layer, new Spec(f.Name, f.Screen, f.Fill, d.Gray(ex.LineColor), ex.LinePt, Opacity: ex.GlassOpacity)));
        }
        GlassHatch(d, ctx, proj, sheet, layer, faces);
    }

    /// <summary>
    /// Screens as solid walls (as thick as the slabs) from their foot to their top, and a white floor plate inside
    /// those that close, laid on the roof below; nothing on top (user rev 0.8.3). <paramref name="foot"/> moves the
    /// foot (exploded plates); default = the screen's real foot.
    /// </summary>
    static IEnumerable<Prism> ScreenPrisms(PD.Core.Drawing.Drawing d, ExportContext ctx, IEnumerable<ScreenEnclosure> list, Func<ScreenEnclosure, double>? foot = null,
        double? floorThicknessFt = null)
    {
        var st = ctx.Style.Screen;
        var dk = st.AxonWallDarken;
        var wall = d.Swatch("NC_SCREEN_WALL", st.AxonWallFill);
        var side1 = d.Swatch("NC_SCREEN_WALL_SIDE1", Darken(st.AxonWallFill, dk.ElementAtOrDefault(0)));
        var side2 = d.Swatch("NC_SCREEN_WALL_SIDE2", Darken(st.AxonWallFill, dk.ElementAtOrDefault(1)));
        var floor = d.Swatch("NC_SCREEN_FLOOR", st.AxonFloorFill);
        foreach (var e in list)
        {
            var z = foot?.Invoke(e) ?? e.Z0;
            yield return new Prism($"SCREEN_{e.Index:00}_WALL", e.Walls, z, z + (e.Z1 - e.Z0), wall, side1, side2, "SCR");
            if (!e.Encloses) continue;
            var inside = AxonFaces.Safe(() => MeshSlicer.Polygonal(e.Floor.Difference(e.Walls)), MeshSlicer.Factory.CreatePolygon());
            if (!inside.IsEmpty) yield return new Prism($"SCREEN_{e.Index:00}_FLOOR", inside, z, z + (floorThicknessFt ?? st.AxonFloorThicknessFt), floor, floor, floor, "SCR");
        }
    }

    /// <summary>
    /// The 04 axons' site (user rev 0.8.4): our property line (NC.PropertyLine) grown by axon.propertySiteMarginFt,
    /// context included as far as that; null without a property line (the margin around the building then).
    /// </summary>
    static NtsGeometry? PropertySite(ExportContext ctx)
    {
        var pl = PlanBackground.PropertyArea(ctx.Result);
        return pl.IsEmpty ? null : AxonFaces.Safe(() => MeshSlicer.Polygonal(pl.Buffer(ctx.Style.Axon.PropertySiteMarginFt)), pl);
    }

    /// <summary>
    /// Open slabs and flat glass (roofs, terraces, canopies, user rev 0.8.4) as prisms of their real thickness:
    /// slabs in the NC.Roof axon paint, glass pale blue (hatched by <see cref="GlassHatch"/>). Only those on the given
    /// blocks' levels; <paramref name="band"/> moves them (exploded plates), default = their real height.
    /// </summary>
    static IEnumerable<Prism> Exposed(PD.Core.Drawing.Drawing d, ExportContext ctx, IEnumerable<ProgramBlock> blocks, Func<ExposedSurface, (double Z0, double Z1)>? band,
        Func<ExposedSurface, bool>? which = null, Func<ExposedSurface, NtsGeometry>? shape = null)
    {
        var levels = blocks.Select(b => b.LevelIndex).ToHashSet();
        var st = ctx.Style.Exposed;
        var roof = PlanBackground.PaintOf(ctx.Style, Roles.Roof, "axon");
        var dk = ctx.Style.Axon.SideDarken;
        foreach (var (e, n) in ExposedSurfaces.For(ctx).Where(e => levels.Contains(e.LevelIndex) && (which?.Invoke(e) ?? true)).Select((e, n) => (e, n)))
        {
            var hex = e.IsGlass ? st.GlassFill : roof.Fill ?? "#F2F2F2";
            var name = e.IsGlass ? "EXPOSED_GLASS" : "EXPOSED_ROOF";
            var top = d.Swatch(name, hex);
            var (z0, z1) = band?.Invoke(e) ?? (e.Z0, Math.Max(e.Z1, e.Z0 + 0.3));
            var plan = shape?.Invoke(e) ?? e.Area;
            if (plan.IsEmpty) continue;
            yield return new Prism($"{SectionDrawing.Safe(ctx.Result.Levels[e.LevelIndex].Name)}_{name}_{n + 1:00}", plan, z0, z1, top,
                d.Swatch(name + "_SIDE1", Darken(hex, dk.ElementAtOrDefault(0))), d.Swatch(name + "_SIDE2", Darken(hex, dk.ElementAtOrDefault(1))), "EXP");
        }
    }

    /// <summary>The pale blue diagonal hatch on the seen part of every glass top.</summary>
    static void GlassHatch(PD.Core.Drawing.Drawing d, ExportContext ctx, AxonProjection proj, Sheet sheet, string layer, IEnumerable<VisibleFace> faces)
    {
        var h = ctx.Style.Exposed.GlassHatch;
        foreach (var f in faces.Where(f => f.IsTop && f.Prism.Group == "EXP" && f.Prism.Name.Contains("EXPOSED_GLASS")))
        {
            var lines = PlanBackground.Hatch(f.Prism.Plan, h);
            if (lines.IsEmpty) continue;
            var seen = AxonFaces.Safe(() => MeshSlicer.Lineal(proj.Plane(lines, f.Prism.Z1).Intersection(f.Screen)), MeshSlicer.Factory.CreateLineString());
            if (!seen.IsEmpty)
                sheet.Paint.Add((layer, new Spec(f.Name + "_HATCH", seen, null, d.Swatch("EXPOSED_GLASS_HATCH", h.Stroke), h.StrokePt, h.Dash, Closed: false)));
        }
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

        // The site follows Level 1 (user rev 0.8 / 0.8.1): L1 stands on it; basements are underground — they hang below
        // the whole site plate (never drawn over it) and the site shows the dug-out hole with its far walls.
        var ground = ctx.Rules.Levels.GroundElevationFt;
        var (siteLevel, dig) = GroundAndExcavation(r, blocks, levels, ground);
        var siteZ = lifted[siteLevel] - t;
        // The pit walls rise from the top basement to its grade: they too stay under the site plate.
        var pitRise = levels.Where(i => r.Levels[i].Elevation < r.Levels[siteLevel].Elevation - 1).Select(i => r.Levels[siteLevel].Elevation - r.Levels[i].Elevation - t)
            .DefaultIfEmpty(0).Min();
        var propSite = PropertySite(ctx);
        var basements = DropBasements(r, proj, blocks, levels, lifted, siteLevel, siteZ, ax.SiteMarginFt, ex.BasementClearFt, Math.Max(0, pitRise), propSite);
        Site(d, ctx, proj, blocks, sheet, siteZ, flatContext: true, family: "axon", opacity: 1, excavation: dig, shape: propSite);
        // The dug-out walls go with the basements (user rev 0.8.3): from the lowest basement plate up to where grade
        // is relative to the top basement, so B1 sits in its own pit below the site.
        if (dig != null && basements.Count > 0)
        {
            var topB = basements[^1];
            var grade = lifted[topB] + (r.Levels[siteLevel].Elevation - r.Levels[topB].Elevation) - t;
            Excavation(d, ctx, proj, sheet, dig, lifted[basements[0]] - t, grade);
        }
        var allFaces = new List<VisibleFace>();
        var facade = GlassInExploded(ctx) ? FacadeGlass(d, ctx, blocks).ToList() : null;
        var joined = 0;
        foreach (var (i, n) in levels.Select((i, n) => (i, n)))
        {
            var lvl = r.Levels[i];
            var layer = "AXON_" + SectionDrawing.Safe(lvl.Name).ToUpperInvariant();
            // Basements lie under the shafts that rise from them, and both under the site (ground › shafts › basements).
            if (basements.Contains(i)) sheet.UnderSite.Add(layer);
            var z = lifted[i];
            var mine = blocks.Where(b => b.LevelIndex == i).ToList();
            var prisms = ProgramPrisms(d, ctx, mine, _ => (z - t, z), ax.SideDarken, coreOverride: true).ToList();
            // A screen on this level stands on the plate: solid walls, white floor, open top (user rev 0.8.3).
            prisms.AddRange(ScreenPrisms(d, ctx, ScreenEnclosures.For(ctx).Where(e => e.LevelIndex == i), _ => z - t, floorThicknessFt: t));
            // Open slabs / glass of this level (terrace, canopy) as part of its plate (user rev 0.8.4).
            prisms.AddRange(Exposed(d, ctx, mine, _ => (z - t, z)));
            // Glass is see-through (user rev 0.8.7, as in 03): flat glass of the plate and the facade glass of this
            // storey standing on it at its real height, drawn over the plate at their opacity.
            var ceil = levels.Where(j => r.Levels[j].Elevation > lvl.Elevation + 1).Select(j => r.Levels[j].Elevation).DefaultIfEmpty(lvl.Elevation + 15).Min();
            var glass = prisms.Where(IsGlass).Concat(facade == null ? Enumerable.Empty<Prism>()
                : GlassBand(facade, lvl.Elevation, ceil, z - lvl.Elevation, $"{SectionDrawing.Safe(lvl.Name)}_")).ToList();
            prisms = prisms.Where(q => !IsGlass(q)).ToList();
            var faces = AxonFaces.Render(proj, prisms);
            allFaces.AddRange(faces);
            PlateFaces(d, ctx, faces, sheet, layer, $"{SectionDrawing.Safe(lvl.Name)}_");
            if (glass.Count > 0)
                PaintGlass(d, ctx, proj, sheet, AxonFaces.Render(proj, prisms.Concat(glass)).Where(f => IsGlass(f.Prism)).ToList(), layer);
            FrontCornerLabels(ctx, proj, faces, sheet, layer);
            sheet.Tags.Add((i, z));

            // Connectors up to the next plate: cores / stairs present on both, and tall spaces (stretched).
            if (n + 1 >= levels.Count) continue;
            var up = levels[n + 1];
            var fill = d.Swatch("AXON_CONNECTOR", ax.ConnectorFill);
            foreach (var (shape, top, name) in Connectors(ctx, blocks, i, up, Lift))
            {
                var z1 = Math.Max(z + 1, top - t);
                // Under the site plate the link goes on its own layer behind the site (user rev 0.8.5): the shafts from
                // the basements read as passing under the ground, not over it.
                var pieces = z < siteZ && z1 > siteZ
                    ? new[] { (z, siteZ, "AXON_CONNECTOR_BELOW", "_BELOW"), (siteZ, z1, layer, "") }
                    : new[] { (z, z1, z1 <= siteZ + 0.01 ? "AXON_CONNECTOR_BELOW" : layer, "") };
                foreach (var (a0, a1, lay, suffix) in pieces)
                {
                    var link = new Prism($"{SectionDrawing.Safe(lvl.Name)}_{name}{suffix}", shape, a0, a1, fill, fill, fill, "LINK");
                    foreach (var f in AxonFaces.Render(proj, new[] { link }))
                        sheet.Paint.Add((lay, new Spec(f.Name, f.Screen, f.Fill, d.Gray(ax.ConnectorStroke), ax.ConnectorPt, Opacity: ax.ConnectorOpacity)));
                }
                if (name.StartsWith("CONNECTOR")) joined++;
            }
        }
        // Trees (and people) on the site disappear behind the plates in front of them.
        HideBehind(sheet, proj, allFaces);
        if (joined == 0 && ax.ConnectorPrograms.Count > 0)
            d.Notes.Add($"Exploded axon: no {string.Join("/", ax.ConnectorPrograms)} program on consecutive levels, so plates are not connected (set core / stair layers to NC.Core / NC.Stair on tab 1).");
        return sheet;
    }

    /// <summary>
    /// Basements are underground (user rev 0.8.1): every level below <paramref name="siteLevel"/> is lowered together
    /// until the top basement's highest drawn point sits <paramref name="clearFt"/> (drawn height) under the front
    /// edge of the site plate (blocks' envelope + <paramref name="siteMarginFt"/> at <paramref name="siteZ"/>), so no
    /// basement plate is drawn over the site. Returns the basement levels, lowest first.
    /// </summary>
    static List<int> DropBasements(AnalysisResult r, AxonProjection proj, List<ProgramBlock> blocks, List<int> levels,
        Dictionary<int, double> lifted, int siteLevel, double siteZ, double siteMarginFt, double clearFt, double riseFt = 0, NtsGeometry? siteShape = null)
    {
        var basements = levels.Where(i => i != siteLevel && r.Levels[i].Elevation < r.Levels[siteLevel].Elevation - 1).ToList();
        if (basements.Count == 0) return basements;
        var own = new Envelope();
        foreach (var b in blocks) own.ExpandToInclude(b.Geometry.EnvelopeInternal);
        own.ExpandBy(siteMarginFt);
        var siteBottom = proj.Plane(siteShape ?? MeshSlicer.Factory.ToGeometry(own), siteZ).EnvelopeInternal.MinY;
        var top = basements[^1];
        var plate = AxonFaces.Union(blocks.Where(b => b.LevelIndex == top).Select(b => b.Geometry));
        if (plate.IsEmpty) return basements;
        var plateTop = proj.Plane(dig(plate, riseFt), lifted[top] + riseFt).EnvelopeInternal.MaxY;
        static NtsGeometry dig(NtsGeometry g, double rise) => rise > 0 ? AxonFaces.Safe(() => g.Buffer(0.5), g) : g;
        var drop = (plateTop - siteBottom) / proj.ZScale + clearFt;
        if (drop > 0)
            foreach (var i in basements) lifted[i] -= drop;
        return basements;
    }

    /// <summary>
    /// Plate faces of one level (user rev 0.8.4, as the floor key axons): every face split from its neighbours by the
    /// 1 pt white edge (axon.levels.edgeStroke / edgePt), screens and open slabs with their own thin line, and one
    /// heavy outline around this level's plate only — no fold lines.
    /// </summary>
    static void PlateFaces(PD.Core.Drawing.Drawing d, ExportContext ctx, List<VisibleFace> faces, Sheet sheet, string layer, string prefix)
    {
        var ax = ctx.Style.Axon;
        var lv = ax.Levels;
        var edge = d.Gray(lv.EdgeStroke);
        foreach (var f in faces)
            sheet.Paint.Add((layer, f.Prism.Group switch
            {
                "SCR" => new Spec(f.Name, f.Screen, f.Fill, d.Gray(ctx.Style.Screen.AxonLineColor), ctx.Style.Screen.AxonLinePt),
                "EXP" => new Spec(f.Name, f.Screen, f.Fill, d.Gray(ctx.Style.Exposed.LineColor), ctx.Style.Exposed.LinePt),
                _ => new Spec(f.Name, f.Screen, f.Fill, edge, lv.EdgePt),
            }));
        var outline = Shells(AxonFaces.Union(faces.Select(f => f.Screen)));
        if (!outline.IsEmpty) sheet.Paint.Add((layer, new Spec(prefix + "OUTLINE", outline, null, d.Gray(ax.LineColor), ax.SlabSilhouettePt)));
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
        var propSite = PropertySite(ctx);
        DropBasements(r, proj, blocks, levels, lifted, groundPlate, lifted[groundPlate] - t, ax.SiteMarginFt, ax.Exploded.BasementClearFt, siteShape: propSite);
        Site(d, ctx, proj, blocks, sheet, lifted[groundPlate] - t, flatContext: true, family: "plan", opacity: eb.SitePlanOpacity,
            plane: (eb.SitePlaneFill, eb.SitePlaneStroke, eb.SitePlaneStrokePt), shape: propSite);

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

    /// <summary>
    /// One sheet per level, an axonometric cutaway (user rev 0.8.3): the floors below in plain white with thin level
    /// lines, this floor's program blocks floor to floor in colour, split by 1 pt white lines (exploded B style), and
    /// above it only the building's total outline (dashed). Below grade the site is a dashed hint over the dug-out hole.
    /// </summary>
    static List<Sheet> Levels(PD.Core.Drawing.Drawing d, ExportContext ctx, AxonProjection proj, List<ProgramBlock> blocks)
    {
        var r = ctx.Result;
        var ax = ctx.Style.Axon;
        var lv = ax.Levels;
        var t = ax.Exploded.SlabThicknessFt;
        var ground = ctx.Rules.Levels.GroundElevationFt;
        var levels = Plates(blocks);
        var (siteLevel, dig) = GroundAndExcavation(r, blocks, levels, ground);
        var band = blocks.ToDictionary(b => b, b => FullHeight(r, b));
        // The building's total outline: every block, floor to floor.
        var screens = ScreenEnclosures.For(ctx);
        var all = AxonFaces.Render(proj, blocks.Select((b, n) => new Prism($"G{n:000}", b.Geometry, band[b].Z0, band[b].Z1, null, null, null, "G"))
            .Concat(screens.Select(e => new Prism($"GS{e.Index:00}", e.Walls, e.Z0, e.Z1, null, null, null, "G"))));
        var total = Shells(AxonFaces.Union(all.Select(f => f.Screen)));
        var white = d.Swatch("AXON_LEVELS_BELOW", lv.BelowFill);
        var belowLine = d.Gray(lv.BelowStroke);
        var edge = d.Gray(lv.EdgeStroke);
        var line = d.Gray(ax.LineColor);
        var gradeElev = r.Levels[siteLevel].Elevation;
        var propSite = PropertySite(ctx);
        var facade = GlassInExploded(ctx) ? FacadeGlass(d, ctx, blocks).ToList() : null;
        var sheets = new List<Sheet>();
        foreach (var i in levels)
        {
            var lvl = r.Levels[i];
            var n = SectionDrawing.Safe(lvl.Name);
            var sheet = new Sheet { Name = $"AXON {lvl.Name}", Prefix = $"AXK_{n}_", Title = $"{ax.TitleLevels} {PlanDrawing.Title(ctx.Style.Plan.LevelTitleFormat, lvl.Name)}" };
            var below = lvl.Elevation < gradeElev - 1;
            Site(d, ctx, proj, blocks, sheet, ground, flatContext: true, family: "axon", opacity: 1, excavation: below ? dig : null, outlineOnly: below, shape: propSite);
            if (below && dig != null) Excavation(d, ctx, proj, sheet, dig, lvl.Elevation - t, ground);

            // Floors below in white, one mass per level and height band (no program divisions); basements stay
            // underground on the floors at grade and above.
            var lower = levels.Where(j => j < i && (below || r.Levels[j].Elevation >= gradeElev - 1)).ToList();
            var prisms = new List<Prism>();
            foreach (var j in lower)
                foreach (var (g, k) in blocks.Where(b => b.LevelIndex == j).GroupBy(b => (Math.Round(band[b].Z0, 1), Math.Round(band[b].Z1, 1))).Select((g, k) => (g, k)))
                {
                    var shape = AxonFaces.Union(g.Select(b => b.Geometry));
                    if (!shape.IsEmpty) prisms.Add(new Prism($"{SectionDrawing.Safe(r.Levels[j].Name)}_BELOW_{k + 1:00}", shape, g.Key.Item1, g.Key.Item2, white, white, white, "BELOW"));
                }
            prisms.AddRange(ProgramPrisms(d, ctx, blocks.Where(b => b.LevelIndex == i), b => band[b], ax.SideDarken, coreOverride: true));
            // Open slabs / glass of this floor in their paint, those of the floors below in white (user rev 0.8.4);
            // the blocks stretched under them stop at their underside (user rev 0.8.6).
            var plates = UnderScreens(ctx, Exposed(d, ctx, blocks.Where(b => b.LevelIndex == i), null));
            plates.AddRange(UnderScreens(ctx, Exposed(d, ctx, blocks.Where(b => lower.Contains(b.LevelIndex)), null))
                .Select(x => x with { Top = white, Side1 = white, Side2 = white, Group = "BELOW" }));
            prisms = UnderPlates(prisms, plates);
            prisms.AddRange(plates);
            // Screens standing on this floor (or on a roof below it): solid walls, white floor, open top.
            prisms.AddRange(ScreenPrisms(d, ctx, screens.Where(e => e.LevelIndex == i || lower.Contains(e.LevelIndex))));
            // Glass is see-through (user rev 0.8.7): this floor's flat glass, and the facade glass up to its ceiling.
            var ceiling = band.Where(kv => kv.Key.LevelIndex == i).Select(kv => kv.Value.Z1).DefaultIfEmpty(lvl.Elevation).Max();
            var floor = lower.Select(j => r.Levels[j].Elevation).Append(lvl.Elevation).Min();
            var glass = prisms.Where(q => IsGlass(q) && q.Group != "BELOW").Concat(facade == null ? Enumerable.Empty<Prism>()
                : GlassBand(facade, floor, ceiling, 0, "")).ToList();
            prisms = prisms.Where(q => !(IsGlass(q) && q.Group != "BELOW")).ToList();
            var faces = AxonFaces.Render(proj, prisms);
            foreach (var f in faces)
                sheet.Paint.Add(("PD_PROGRAM", f.Prism.Group switch
                {
                    "BELOW" => new Spec(f.Name, f.Screen, white, belowLine, lv.BelowStrokePt),
                    "SCR" => new Spec(f.Name, f.Screen, f.Fill, d.Gray(ctx.Style.Screen.AxonLineColor), ctx.Style.Screen.AxonLinePt),
                    "EXP" => new Spec(f.Name, f.Screen, f.Fill, d.Gray(ctx.Style.Exposed.LineColor), ctx.Style.Exposed.LinePt),
                    _ => new Spec(f.Name, f.Screen, f.Fill, edge, lv.EdgePt),
                }));
            GlassHatch(d, ctx, proj, sheet, "PD_PROGRAM", faces);
            var glassFaces = glass.Count == 0 ? new List<VisibleFace>()
                : AxonFaces.Render(proj, prisms.Concat(glass)).Where(f => IsGlass(f.Prism)).ToList();
            PaintGlass(d, ctx, proj, sheet, glassFaces);
            var stack = AxonFaces.Union(faces.Select(f => f.Screen));
            var silhouette = Shells(stack);
            if (!silhouette.IsEmpty) sheet.Paint.Add(("AXON_LINE", new Spec("OUTLINE", silhouette, null, line, lv.SilhouettePt)));
            // Above this floor: only the building's total outline, where the drawn stack does not cover it.
            var ghost = AxonFaces.Safe(() => MeshSlicer.Lineal(total.Boundary.Difference(stack.Buffer(0.05))), MeshSlicer.Factory.CreateLineString());
            if (!ghost.IsEmpty) sheet.Paint.Add(("AXON_GHOST_ABOVE", new Spec("BUILDING_OUTLINE", ghost, null, d.Gray(lv.OutlineStroke), lv.OutlinePt, lv.OutlineDash, Closed: false)));
            var mine = faces.Where(f => f.Prism.Group == "PRG").ToList();
            CentredLabels(ctx, proj, mine, sheet, "PD_LABELS", "PD Plane Label", "PD Plane Label Small");
            // Trees standing behind the building lose the part it hides.
            HideBehind(sheet, proj, faces);
            sheet.Tags.Add((i, band.Where(kv => kv.Key.LevelIndex == i).Select(kv => kv.Value.Z1).DefaultIfEmpty(lvl.Elevation).Max()));
            sheets.Add(sheet);
        }
        return sheets;
    }

    /// <summary>Outer shells of a screen union (no holes, no hairline gaps).</summary>
    static NtsGeometry Shells(NtsGeometry g)
    {
        if (g.IsEmpty) return g;
        g = AxonFaces.Safe(() => g.Buffer(0.05).Buffer(-0.05), g);
        return MeshSlicer.Factory.BuildGeometry(AxonFaces.Polygons(g).Select(p => (NtsGeometry)MeshSlicer.Factory.CreatePolygon(p.Shell)).ToList());
    }

    /// <summary>
    /// The far walls of the dug-out hole (their inner faces look at the viewer), solid from <paramref name="z0"/> up to
    /// grade, drawn under the plate.
    /// </summary>
    static void Excavation(PD.Core.Drawing.Drawing d, ExportContext ctx, AxonProjection proj, Sheet sheet, NtsGeometry dig, double z0, double ground)
    {
        var ax = ctx.Style.Axon;
        var k = 0;
        foreach (var poly in AxonFaces.Polygons(AxonFaces.Simplify(dig)))
        {
            var pts = poly.ExteriorRing.Coordinates;
            var ccw = !NetTopologySuite.Algorithm.Orientation.IsCCW(pts);
            for (var i = 0; i + 1 < pts.Length; i++)
            {
                var (a, q) = ccw ? (pts[i + 1], pts[i]) : (pts[i], pts[i + 1]);
                var outward = (q.Y - a.Y, a.X - q.X, 0.0);
                // A wall whose outside turns away from the viewer shows its inside: a far wall of the hole.
                if (proj.Faces(outward) || a.Distance(q) < 0.1) continue;
                var quad = proj.Polygon(new[] { (a.X, a.Y, z0), (q.X, q.Y, z0), (q.X, q.Y, ground), (a.X, a.Y, ground) });
                sheet.Paint.Add(("AXON_EXCAVATION", new Spec($"EXCAVATION_{++k:000}", quad, d.Swatch("AXON_EXCAVATION", ax.ExcavationFill),
                    d.Gray(ax.ExcavationStroke), ax.ExcavationPt)));
            }
        }
    }

    // ---------------------------------------------------------------- spacing

    static List<int> Plates(List<ProgramBlock> blocks) => blocks.Select(b => b.LevelIndex).Distinct().OrderBy(i => i).ToList();

    /// <summary>Drawn height of each plate: the first at its real elevation, each next one a pitch above the one below.</summary>
    /// <summary>
    /// The plate the site belongs to (the first level at or above grade, else the lowest) and the excavation: the
    /// footprint of every level below grade, slightly grown.
    /// </summary>
    static (int Level, NtsGeometry? Dig) GroundAndExcavation(AnalysisResult r, List<ProgramBlock> blocks, List<int> levels, double ground)
    {
        var at = levels.Where(i => r.Levels[i].Elevation >= ground - 1).DefaultIfEmpty(levels[0]).First();
        var below = levels.Where(i => r.Levels[i].Elevation < r.Levels[at].Elevation - 1).ToHashSet();
        if (below.Count == 0) return (at, null);
        var dig = AxonFaces.Union(blocks.Where(b => below.Contains(b.LevelIndex)).Select(b => b.Geometry));
        return (at, dig.IsEmpty ? null : AxonFaces.Safe(() => dig.Buffer(0.5), dig));
    }

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
                    // Text always starts at the left end of its edge: from the front corner when the edge runs up to the
                    // right, from the block's leftmost corner (the far end of the edge) when it runs down to the right.
                    var start = sx >= 0 ? corner : along;
                    var ax0 = start.X + rx * inset + ux * inset;
                    var ay0 = start.Y + ry * inset + uy * inset;
                    var angle = Math.Atan2(ry, rx) * 180 / Math.PI;
                    sheet.Paint.Add((layer, new Spec(p.Name + "_LBL", MeshSlicer.Factory.CreatePolygon(), null, null, 0, Text: text,
                        TextAt: proj.Project(ax0, ay0, p.Z1), Matrix: proj.PlaneTextMatrix(angle), Style: style, Align: "left")));
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
        bool flatContext, string family, double opacity, (string Fill, string Stroke, double Pt)? plane = null, SiteReach? reach = null,
        NtsGeometry? excavation = null, bool outlineOnly = false, Envelope? area = null, NtsGeometry? shape = null)
    {
        var r = ctx.Result;
        var ax = ctx.Style.Axon;
        reach ??= SiteReach.Of(ax);
        var own = new Envelope();
        foreach (var b in blocks) own.ExpandToInclude(b.Geometry.EnvelopeInternal);
        // A given area (site axon filling the page) replaces the margin around our building; a given shape (the
        // property line grown by 50 ft, user rev 0.8.4) is the site itself.
        if (shape != null) area = shape.EnvelopeInternal;
        var site = area != null ? new Envelope(area) : new Envelope(own);
        if (area == null) site.ExpandBy(reach.SiteMarginFt);
        var siteGeom = shape ?? MeshSlicer.Factory.ToGeometry(site);
        var (pf, ps, pp) = plane ?? (ax.SiteFill, ax.SiteStroke ?? "", ax.SiteStroke == null ? 0 : ax.ContextPt);
        // Basements below: the site is dug open over them (exploded / level axons).
        var ground = excavation == null ? siteGeom : AxonFaces.Safe(() => siteGeom.Difference(excavation), siteGeom);
        // Below grade the site is only hinted: dashed outlines, no fill, no trees or people (user rev 0.8).
        var hint = (Stroke: d.Gray(ax.HiddenStroke), Pt: ax.HiddenPt, Dash: ax.HiddenDash);
        sheet.Paint.Add(("AXON_SITE", outlineOnly
            ? new Spec("SITE", proj.Plane(ground, z), null, hint.Stroke, hint.Pt, hint.Dash)
            : new Spec("SITE", proj.Plane(ground, z), d.Swatch("AXON_SITE", pf), d.Gray(ps), pp)));
        var near = area != null ? new Envelope(area) : new Envelope(own);
        near.ExpandBy(shape != null ? 1 : area != null ? reach.ContextMarginFt - reach.SiteMarginFt : reach.ContextMarginFt);
        var items = PlanBackground.Collect(ctx, near);
        // Below grade the hint leaves out the sidewalk paving lines (too busy dashed).
        foreach (var (bg, n) in items.Where(b => b.Role != Roles.Building && !IsTree(b) && site.Intersects(b.Geometry.EnvelopeInternal)
                     && !(outlineOnly && b.IsLine && b.Role == SiteLayout.Sidewalk)).Select((b, n) => (b, n)))
        {
            var g = AxonFaces.Safe(() => bg.Geometry.Intersection(ground), bg.Geometry);
            var (fill, stroke, pt, dash, op) = PlanBackground.Paint(d, ctx.Style, bg, family);
            sheet.Paint.Add(("AXON_SITE", outlineOnly
                ? new Spec($"BG_{n:000}", proj.Plane(g, z), null, hint.Stroke, hint.Pt, hint.Dash, !bg.IsLine)
                : new Spec($"BG_{n:000}", proj.Plane(g, z), fill, stroke, pt, dash, !bg.IsLine, op * opacity)));
            // Green and water: hatch over the fill (as in plan).
            var hatch = PlanBackground.PaintOf(ctx.Style, bg.Role, family).Hatch;
            if (hatch != null && !bg.IsLine && !outlineOnly)
            {
                var lines = PlanBackground.Hatch(g, hatch);
                if (!lines.IsEmpty)
                    sheet.Paint.Add(("AXON_SITE", new Spec($"BG_{n:000}_HATCH", proj.Plane(lines, z), null,
                        d.Swatch(PlanBackground.SwatchName(bg.Role) + "_HATCH", hatch.Stroke), hatch.StrokePt, hatch.Dash, Closed: false, Opacity: hatch.Opacity * opacity)));
            }
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
            // Within the property site only (the context it takes in).
            if (shape != null) foot = AxonFaces.Safe(() => MeshSlicer.Polygonal(foot.Intersection(shape)), foot);
            if (foot.IsEmpty || foot.Area < 4) continue;
            var name = $"CONTEXT_{++k:000}";
            if (flatContext)
            {
                sheet.Paint.Add(("AXON_CONTEXT", outlineOnly
                    ? new Spec(name, proj.Plane(foot, z), null, hint.Stroke, hint.Pt, hint.Dash)
                    : new Spec(name, proj.Plane(foot, z), white, d.Gray(ax.ContextStroke), ax.ContextPt, Opacity: opacity)));
                continue;
            }
            var z0 = Math.Max(z, bg.Z0);
            var z1 = reach.ContextHeightFt > 0 ? Math.Min(bg.Z, z0 + reach.ContextHeightFt) : Math.Min(bg.Z, z0 + reach.MaxContextHeightFt);
            if (z1 > z0 + 0.1) context.Add(new Prism(name, foot, z0, z1, white, white, white, "CTX", bg.Name));
        }

        // Lollipop trees (site axon), far → near, on top.
        var tr = ax.Trees;
        var rng = new Random(17);
        foreach (var (tree, n) in items.Where(_ => !outlineOnly).Where(IsTree).Where(b => site.Intersects(b.Geometry.EnvelopeInternal))
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
        if (reach.People && !outlineOnly) People(d, ctx, sheet, proj, AxonFaces.Safe(() => PlanBackground.Walkable(items, ours).Intersection(siteGeom), siteGeom), ours, z);
        return context;
    }

    /// <summary>PRD 4: people standing on the site plane around our building (never on top of it).</summary>
    static void People(PD.Core.Drawing.Drawing d, ExportContext ctx, Sheet sheet, AxonProjection proj, NtsGeometry site, NtsGeometry ours, double z)
    {
        if (!PD.Core.Entourage.EntouragePlacer.Enabled(ctx)) return;
        var e = ctx.Style.Entourage;
        // Hardscape and sidewalks (site = walkable ground) around our building: never on a road or a building.
        var near = AxonFaces.Safe(() => ours.Buffer(ctx.Style.Plan.ContextMarginFt).Intersection(site), site);
        var open = MeshSlicer.Polygonal(AxonFaces.Safe(() => near.Difference(ours), near));
        var n = 0;
        foreach (var f in PD.Core.Entourage.EntouragePlacer.Outdoors(ctx, open, sheet.Prefix + "SITE")
                     .OrderBy(f => proj.Depth(f.X, f.Y, z)))
        {
            var (px, py) = proj.Project(f.X, f.Y, z);
            var (hx, hy) = proj.Project(f.X, f.Y, z + e.HeightFt);
            var rings = PD.Core.Entourage.EntourageSymbols.Projected(px, py, (hx - px, hy - py), f.Variant, f.Mirror);
            if (rings.Count == 0) continue;
            // Office figure rings (holes between arm and body, or two people) → one clean shape for the overlays.
            NtsGeometry ring = MeshSlicer.Factory.CreatePolygon();
            foreach (var r in rings.Where(r => r.Length >= 3))
            {
                var poly = MeshSlicer.Factory.CreatePolygon(r.Select(q => new Coordinate(q[0], q[1])).Append(new Coordinate(r[0][0], r[0][1])).ToArray()).Buffer(0);
                ring = AxonFaces.Safe(() => ring.SymmetricDifference(poly), ring);
            }
            if (ring.IsEmpty) continue;
            sheet.Paint.Add((PD.Core.Entourage.EntouragePlacer.Layer,
                new Spec($"PERSON_{++n:000}", ring, d.Swatch("ENTOURAGE", e.Color), null, 0, Opacity: e.Opacity, Depth: proj.Depth(f.X, f.Y, z))));
        }
    }

    /// <summary>Trees and people standing behind a building lose the part the building hides.</summary>
    static void HideBehind(Sheet sheet, AxonProjection proj, List<VisibleFace> faces, double treeGap = 0)
    {
        for (var i = sheet.Paint.Count - 1; i >= 0; i--)
        {
            var (layer, spec) = sheet.Paint[i];
            if (spec.Depth is not { } depth || spec.Geometry.IsEmpty) continue;
            var left = AxonFaces.Hide(proj, spec.Geometry, depth, faces, layer == "AXON_TREE" ? treeGap : 0);
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

    /// <summary>
    /// North arrow for an axon: a shaft along the projected plan north (+Y), an arrowhead and "N", centred at (x, y)
    /// on the page (y down).
    /// </summary>
    static IEnumerable<DItem> AxonNorth(PD.Core.Drawing.Drawing d, ExportContext ctx, AxonProjection proj, string prefix, double x, double y)
    {
        var (ax0, ay0) = proj.Project(0, 0, 0);
        var (ax1, ay1) = proj.Project(0, 1, 0);
        double vx = ax1 - ax0, vy = -(ay1 - ay0);
        var len = Math.Sqrt(vx * vx + vy * vy);
        if (len < 1e-9) yield break;
        vx /= len; vy /= len;
        var l = ctx.Style.Annotation.AxonNorthPt;
        double tx = x + vx * l / 2, ty = y + vy * l / 2, bx = x - vx * l / 2, by = y - vy * l / 2;
        var black = d.Gray("#000000");
        yield return new DItem { Kind = ItemKind.Path, Name = prefix + "NORTH_SHAFT", Closed = false, Stroke = black, StrokePt = 1, Paths = new() { new[] { new[] { bx, by }, new[] { tx, ty } } } };
        double hx = -vy * l * 0.18, hy = vx * l * 0.18;
        yield return new DItem
        {
            Kind = ItemKind.Path, Name = prefix + "NORTH_HEAD", Fill = black,
            Paths = new() { new[] { new[] { tx + vx * l * 0.15, ty + vy * l * 0.15 }, new[] { tx - vx * l * 0.15 + hx, ty - vy * l * 0.15 + hy }, new[] { tx - vx * l * 0.15 - hx, ty - vy * l * 0.15 - hy } } },
        };
        yield return SectionDrawing.Text(prefix + "NORTH_N", tx + vx * l * 0.45, ty + vy * l * 0.45, "N", "PD Note", "center", center: true);
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
        string[] first = { "AXON_CONNECTOR_BELOW", "AXON_SITE", "AXON_CONTEXT", "AXON_EXCAVATION", "AXON_GHOST_BELOW", "PD_PROGRAM", "AXON_LEVEL_LINE", "AXON_LINE" };
        string[] last = { "AXON_SCREEN", "AXON_TREE", PD.Core.Entourage.EntouragePlacer.Layer, "FACADE", "AXON_GHOST_ABOVE", "AXON_MASSING" };
        foreach (var n in sheets.SelectMany(s => s.UnderSite).Distinct().Where(used.Contains)) d.Layer(n);
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
            // North arrow in axon mode (user rev 0.8.4): along the projected plan north, right of the title.
            var titleW = TextFit.Width(s.Title.ToUpperInvariant(), ctx.Pt(ctx.Style.Fonts.Title.SizePt));
            foreach (var it in AxonNorth(d, ctx, proj, s.Prefix, x0 + titleW + ctx.Pt(48), ty - ctx.Pt(8))) anno.Items.Add(At(it, a));
        }
        // A LEGEND artboard with the axon annotation set (tags, flows, transportation, annotation) for later analysis.
        if (ctx.Style.Annotation.LegendArtboards && sheets.Count > 0)
        {
            var la = d.Artboards.Count;
            var bottom = AnnotationSymbols.Add(d, ctx, AnnotationSymbols.Set.Axon, sheets[0].Prefix + "LGD_", la, 54, 54);
            d.Artboards.Add(new Artboard("LEGEND", 480, Math.Ceiling(bottom + 54)));
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
        // Leader from each plate's rightmost point along (dx, dy) (page y up) to the column; tags that would land on
        // top of each other are pushed apart by a line height and their leaders follow.
        var rows = new List<(int I, double X, double Y, double End, double Ey)>();
        foreach (var (i, z) in s.Tags)
        {
            var plate = ctx.Result.Plans[i].Union;
            if (plate.IsEmpty) continue;
            var right = plate.Coordinates.Select(c => proj.Project(c.X, c.Y, z)).OrderByDescending(q => q.X).First();
            double x = map.X(right.X), y = map.Y(right.Y);
            var end = Math.Max(x, colX);
            rows.Add((i, x, y, end, y - dy * (end - x) / Math.Max(1e-6, dx)));
        }
        var minGap = Math.Max(sq, ctx.Pt(font.SizePt)) * 1.4;
        rows = rows.OrderBy(r => r.Ey).ToList();
        for (var k = 1; k < rows.Count; k++)
            if (rows[k].Ey < rows[k - 1].Ey + minGap) rows[k] = rows[k] with { Ey = rows[k - 1].Ey + minGap };
        foreach (var (i, x, y, end, ey) in rows)
        {
            var lvl = ctx.Result.Levels[i];
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
