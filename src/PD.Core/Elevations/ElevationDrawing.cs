using PD.Core.Drawing;
using PD.Core.Geometry;
using PD.Core.Model;
using PD.Core.Sections;
using PD.Core.Settings;
using PD.Core.Views;

namespace PD.Core.Elevations;

/// <summary>
/// Elevation from a plan line: Make2D-like projected view (no shadows) — our masses as a light grey solid base, every
/// slab/facade layer in its own grey on its own AI layer (glass solid white), other buildings white with a dashed outline;
/// level markers, ground line, title. True scale; text scales like sections.
/// </summary>
public static class ElevationDrawing
{
    public static PD.Core.Drawing.Drawing Build(ExportContext ctx, SectionSetting el)
    {
        var r = ctx.Result;
        var (ax, ay, _) = r.Frame.ToLocal(el.Line[0], el.Line[1], 0);
        var (bx, by, _) = r.Frame.ToLocal(el.Line[2], el.Line[3], 0);
        var plane = new SectionPlane(ax, ay, bx, by, LooksLeft(r, el, ax, ay, bx, by));
        // A fixed artboard (user rev 0.8.4): a longer line is cut down around the building.
        if (ctx.ArtboardSize is { } ab0)
            plane = plane.Fit((ab0.W - 2 * ctx.Style.Page.MarginPt - (ctx.Style.Elevation.LevelMarkers ? ctx.Pt(ctx.Style.Page.LevelColumnPt) : 0)) / ctx.PtPerFt,
                ctx.PageAnchor());
        var depth = el.DepthFt > 0 ? el.DepthFt : ctx.Rules.Output.SectionDepthFt;
        var ground = ctx.Rules.Levels.GroundElevationFt;
        var items = ProjectedView.Collect(r, plane, depth, ProjectedView.GlassRegex(ctx.Style.View), ground);
        var st = ctx.Style.Section;
        var es = ctx.Style.Elevation;
        var pg = ctx.Style.Page;

        var name = SectionDrawing.Safe(el.Name);
        var d = new PD.Core.Drawing.Drawing { FileName = $"07_Elevation_{name}.ai" };
        ctx.AddStandardStyles(d);
        ProjectedView.AddLayers(d, items);
        foreach (var n in new[] { "PD_LEVELS", "PD_GROUND", "PD_LABELS", PD.Core.Entourage.EntouragePlacer.Layer, "PD_ANNOTATION" }) d.Layer(n);

        var k = ctx.PtPerFt;
        var groundT = st.GroundPt;
        var zMax = items.Select(i => i.Geometry.EnvelopeInternal.MaxY).DefaultIfEmpty(ground + 30).Max();
        double w = plane.Length * k, h = (zMax - ground) * k + groundT / 2, m = pg.MarginPt;
        var colL = es.LevelMarkers ? ctx.Pt(pg.LevelColumnPt) : 0;
        var titleBand = ctx.Pt(pg.TitleBandPt);
        double abW, abH, x0, y0;
        var fits = ctx.ArtboardSize is { } fs && colL + w <= fs.W - 2 * m + 0.5 && h <= fs.H - 2 * m - titleBand + 0.5;
        if (ctx.ArtboardSize is { } fixedSize && (fits || !pg.FitContentWhenLarger))
        {
            (abW, abH) = fixedSize;
            // The property line (page.centreOn) mid-page, as far as the drawing allows.
            var mid = plane.Mid(ctx.PageAnchor()) ?? plane.Length / 2;
            x0 = ExportContext.Place(m + colL, abW - m, w, mid * k) ?? m + colL + (abW - 2 * m - colL - w) / 2;
            y0 = m + (abH - 2 * m - titleBand - h) / 2;
            if (!fits) d.Notes.Add(ctx.LargerNote($"Elevation {el.Name}"));
        }
        else
        {
            if (ctx.ArtboardSize != null) d.Notes.Add(ctx.LargerNote($"Elevation {el.Name}"));
            abW = 2 * m + colL + w;
            abH = 2 * m + h + titleBand;
            x0 = m + colL;
            y0 = m;
        }
        var map = new PageMap(x0, y0, k, zMax, plane.Length, flip: false);
        d.Artboards.Add(new Artboard($"ELEVATION {el.Name}", Math.Ceiling(abW), Math.Ceiling(abH)));
        var p = $"ELV_{name}_";

        ProjectedView.Draw(d, ctx, map, p, items);
        if (!items.Any(i => i.Kind != ViewKind.Context))
            d.Notes.Add($"Elevation {el.Name}: none of our building is in view; move the line or deepen its depth on tab 7.");

        // Level markers: ▼, level name and elevation, dashed line to our building (same column as sections).
        var own = items.Where(i => i.Kind != ViewKind.Context).Select(i => i.Geometry.EnvelopeInternal).ToList();
        if (es.LevelMarkers && own.Count > 0)
            // Same level column as sections; the dashed level line spans the whole drawing (user rev 0.8).
            LevelMarkers.Add(d, ctx, map, p, r.Levels, x0 - colL, x0 + w, zMax);
            // Penthouse top, as in sections: the top row is only shell / core / support.
            var blocks = PD.Core.Analysis.ProgramBlocks.Build(r);
            var top = blocks.Count == 0 ? -1 : blocks.Max(b => b.LevelIndex);
            var pent = ctx.Style.Section.PenthousePrograms;
            if (top > 0 && pent.Count > 0 && blocks.Where(b => b.LevelIndex == top).All(b => pent.Contains(b.ProgramKey, StringComparer.OrdinalIgnoreCase)))
            {
                var z = blocks.Where(b => b.LevelIndex == top).Max(b => b.Z1);
                if (z <= zMax + 0.5) LevelMarkers.One(d, ctx, map, p, "PENTHOUSE_TOP", ctx.Style.Section.PenthouseTopLabel, z, x0 - colL, x0 + w);
            }

        d.Layer("PD_GROUND").Items.Add(map.Line(p + "GROUND", new[] { (0.0, ground), (plane.Length, ground) }, d.Gray(st.GroundStroke), st.GroundPt));

        // PRD 4: people on the ground line in front of the elevation.
        if (PD.Core.Entourage.EntouragePlacer.Enabled(ctx))
        {
            var e = ctx.Style.Entourage;
            var n = 0;
            foreach (var (u0, u1, tag) in SectionDrawing.WalkableAlong(ctx, plane, Math.Min(depth, ctx.Style.Entourage.ElevationBandFt)))
            foreach (var f in PD.Core.Entourage.EntouragePlacer.AlongLine(ctx, u0, u1, "", p + "GROUND" + tag, outdoors: true))
                d.Layer(PD.Core.Entourage.EntouragePlacer.Layer).Items.Add(PD.Core.Entourage.EntourageSymbols.Standing(
                    d, e, $"{p}PERSON_{++n:000}", map.X(f.X), map.Y(ground), e.HeightFt * map.K, f.Variant, f.Mirror));
        }

        var ty = map.Y(ground) + groundT / 2 + ctx.Pt(40);
        d.Layer("PD_ANNOTATION").Items.Add(SectionDrawing.Text(p + "TITLE", x0, ty, $"{es.TitlePrefix} {el.Name}", "PD Title", "left"));
        d.Layer("PD_ANNOTATION").Items.Add(SectionDrawing.Text(p + "SCALE", x0, ty + ctx.Pt(18), $"SCALE {ctx.ScaleText}", "PD Note", "left"));
        return d;
    }

    /// <summary>
    /// Viewing side: with <see cref="SectionSetting.AutoSide"/> (default) the elevation looks towards our building —
    /// the side of the line holding most of its footprint — so a line drawn in either direction works.
    /// </summary>
    public static bool LooksLeft(PD.Core.Analysis.AnalysisResult r, SectionSetting el, double ax, double ay, double bx, double by)
    {
        if (!el.AutoSide) return el.LookLeft;
        var left = new SectionPlane(ax, ay, bx, by, true);
        double sum = 0;
        foreach (var p in r.Plans)
            foreach (var f in p.Footprints.Where(f => !f.Resolved.IsEmpty))
            {
                var c = f.Resolved.Centroid;
                sum += left.W(c.X, c.Y) * f.Resolved.Area;
            }
        return sum == 0 ? el.LookLeft : sum > 0;
    }
}
