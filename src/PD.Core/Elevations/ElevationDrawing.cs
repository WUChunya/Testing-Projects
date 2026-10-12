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
        if (ctx.ArtboardSize is { } fixedSize)
        {
            (abW, abH) = fixedSize;
            x0 = m + colL + (abW - 2 * m - colL - w) / 2;
            y0 = m + (abH - 2 * m - titleBand - h) / 2;
            if (x0 < m + colL - 0.5 || y0 < m - 0.5)
                d.Notes.Add($"Elevation {el.Name} at {ctx.ScaleText} is larger than {ctx.Settings.Output.Artboard}; use \"Fit content\" or a smaller scale.");
        }
        else
        {
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

        // Level markers: ▼, level name and elevation, dashed line to our building.
        var own = items.Where(i => i.Kind != ViewKind.Context).Select(i => i.Geometry.EnvelopeInternal).ToList();
        if (es.LevelMarkers && own.Count > 0)
        {
            var reach = map.X(own.Min(e => e.MinX));
            var s = ctx.Pt(es.MarkerSizePt);
            foreach (var l in r.Levels.Where(l => !l.IsMezzanine && l.Elevation <= zMax))
            {
                var y = map.Y(l.Elevation);
                var mx = x0 - colL + ctx.Pt(4);
                d.Layer("PD_LEVELS").Items.Add(new DItem
                {
                    Kind = ItemKind.Path, Name = p + "LVL_" + l.Name + "_MARK", Fill = d.Gray("#000000"),
                    Paths = new() { new[] { new[] { mx, y - s * 0.6 }, new[] { mx + s, y - s * 0.6 }, new[] { mx + s / 2, y } } },
                });
                var line = map.Line(p + "LVL_" + l.Name, Array.Empty<(double, double)>(), d.Gray(es.LevelLineStroke), es.LevelLineStrokePt);
                line.Paths = new() { new[] { new[] { mx + s * 1.3, y }, new[] { reach, y } } };
                line.Dash = es.LevelLineDash;
                d.Layer("PD_LEVELS").Items.Add(line);
                var label = l.IsRoof ? $"{l.Name} ROOF" : l.Name;
                d.Layer("PD_ANNOTATION").Items.Add(SectionDrawing.Text(p + "LVL_" + l.Name + "_TXT", mx + s * 1.5, y - ctx.Pt(2.5), label, "PD Note", "left"));
                d.Layer("PD_ANNOTATION").Items.Add(SectionDrawing.Text(p + "LVL_" + l.Name + "_EL", mx + s * 1.5, y + ctx.Pt(7), FeetInches.Format(l.Elevation), "PD Note", "left"));
            }
        }

        d.Layer("PD_GROUND").Items.Add(map.Line(p + "GROUND", new[] { (0.0, ground), (plane.Length, ground) }, d.Gray(st.GroundStroke), st.GroundPt));

        // PRD 4: people on the ground line in front of the elevation.
        if (PD.Core.Entourage.EntouragePlacer.Enabled(ctx))
        {
            var e = ctx.Style.Entourage;
            var n = 0;
            foreach (var f in PD.Core.Entourage.EntouragePlacer.AlongLine(ctx, 0, plane.Length, "", p + "GROUND", outdoors: true))
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
