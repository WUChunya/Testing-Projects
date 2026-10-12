using PD.Core.Drawing;
using PD.Core.Levels;
using PD.Core.Model;

namespace PD.Core.Sections;

/// <summary>
/// Left level column shared by sections and elevations: ▼ marker, level name (L3, "L12 ROOF") and elevation, and a
/// dashed level line from the marker to the building. Sized and styled by style "elevation".
/// </summary>
public static class LevelMarkers
{
    /// <param name="columnLeft">Left edge of the level column (page pt).</param>
    /// <param name="reachX">Where the dashed level line ends (the building's left edge, page pt).</param>
    public static void Add(PD.Core.Drawing.Drawing d, ExportContext ctx, PageMap map, string prefix, IEnumerable<Level> levels,
        double columnLeft, double reachX, double zMax)
    {
        var es = ctx.Style.Elevation;
        var s = ctx.Pt(es.MarkerSizePt);
        foreach (var l in levels.Where(l => !l.IsMezzanine && l.Elevation <= zMax))
        {
            var y = map.Y(l.Elevation);
            var mx = columnLeft + ctx.Pt(4);
            d.Layer("PD_LEVELS").Items.Add(new DItem
            {
                Kind = ItemKind.Path, Name = prefix + "LVL_" + l.Name + "_MARK", Fill = d.Gray("#000000"),
                Paths = new() { new[] { new[] { mx, y - s * 0.6 }, new[] { mx + s, y - s * 0.6 }, new[] { mx + s / 2, y } } },
            });
            if (reachX > mx + s * 1.3)
            {
                var line = map.Line(prefix + "LVL_" + l.Name + "_DASH", Array.Empty<(double, double)>(), d.Gray(es.LevelLineStroke), es.LevelLineStrokePt);
                line.Paths = new() { new[] { new[] { mx + s * 1.3, y }, new[] { reachX, y } } };
                line.Dash = es.LevelLineDash;
                d.Layer("PD_LEVELS").Items.Add(line);
            }
            var label = l.IsRoof ? $"{l.Name} ROOF" : l.Name;
            d.Layer("PD_ANNOTATION").Items.Add(SectionDrawing.Text(prefix + "LVL_" + l.Name + "_TXT", mx + s * 1.5, y - ctx.Pt(2.5), label, "PD Note", "left"));
            d.Layer("PD_ANNOTATION").Items.Add(SectionDrawing.Text(prefix + "LVL_" + l.Name + "_EL", mx + s * 1.5, y + ctx.Pt(7), FeetInches.Format(l.Elevation), "PD Note", "left"));
        }
    }
}
