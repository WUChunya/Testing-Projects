using System.Globalization;
using System.Text;

namespace PD.Core.Analysis;

public static class Reports
{
    /// <summary>Areas (SF) per level and program after overlap resolution; last column is the level total.</summary>
    public static string AreaCsv(AnalysisResult r, Func<string, string> programLabel)
    {
        var keys = r.Plans.SelectMany(p => p.AreaByProgram.Keys).Distinct().OrderBy(k => k, StringComparer.Ordinal).ToList();
        var sb = new StringBuilder();
        sb.AppendLine("Level,Elevation (ft)," + string.Join(",", keys.Select(k => Csv(programLabel(k)))) + ",Total");
        foreach (var p in r.Plans)
        {
            var cells = keys.Select(k => p.AreaByProgram.GetValueOrDefault(k).ToString("0", CultureInfo.InvariantCulture));
            sb.AppendLine($"{p.Level.Name},{p.Level.Elevation.ToString("0.00", CultureInfo.InvariantCulture)},{string.Join(",", cells)},{p.Union.Area.ToString("0", CultureInfo.InvariantCulture)}");
        }
        return sb.ToString();
    }

    /// <summary>The program table behind the charts (PRD 5.5 columns plus the computed area), model or Excel.</summary>
    public static string TableCsv(Charts.ProgramTable t, Func<string, string> programLabel)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# " + t.Source);
        sb.AppendLine(string.Join(",", Charts.ProgramTable.Columns.Select(Csv)) + ",Program,Area (" + t.Unit + ")");
        foreach (var r in t.Rows)
            sb.AppendLine(string.Join(",", new[]
            {
                Csv(r.Department), Csv(r.SubGroup), Csv(r.Code), Csv(r.RoomName), Csv(programLabel(r.ProgramKey)),
                r.Qty.ToString("0.##", CultureInfo.InvariantCulture), r.AreaEach.ToString("0.##", CultureInfo.InvariantCulture),
                r.Factor.ToString("0.##", CultureInfo.InvariantCulture), Csv(r.ProgramKey),
                r.Area.ToString("0", CultureInfo.InvariantCulture),
            }));
        sb.AppendLine($"TOTAL,,,,,,,,,{t.Total.ToString("0", CultureInfo.InvariantCulture)}");
        return sb.ToString();
    }

    static string Csv(string s) => s.Contains(',') || s.Contains('"') ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;

    /// <summary>B2 "section line misses every mass": true when the local-feet segment crosses any level footprint.</summary>
    public static bool SectionCrossesMasses(AnalysisResult r, double x0, double y0, double x1, double y1)
    {
        var line = Geometry.MeshSlicer.Factory.CreateLineString(new[]
        {
            new NetTopologySuite.Geometries.Coordinate(x0, y0), new NetTopologySuite.Geometries.Coordinate(x1, y1),
        });
        return r.Plans.Any(p => !p.Union.IsEmpty && p.Union.Intersects(line));
    }
}
