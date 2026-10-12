using System.Globalization;
using ClosedXML.Excel;
using PD.Core.Classification;
using PD.Core.Drawing;
using PD.Core.Settings;

namespace PD.Core.Charts;

/// <summary>
/// One line of the program table (PRD 5.5): Department, Sub-group, Code, Room Name, Program Category, Qty,
/// ASF/each, Multiplier (e.g. ×6 floors).
/// </summary>
public sealed record ProgramRow(string Department, string SubGroup, string Code, string RoomName,
    string ProgramKey, double Qty, double AreaEach, double Multiplier = 1)
{
    public double Factor => Multiplier <= 0 ? 1 : Multiplier;
    public double Count => Qty * Factor;
    public double Area => Qty * AreaEach * Factor;
}

/// <summary>
/// The program table behind the charts: read from the user's Excel (ASF) or built from the model's resolved
/// footprints (GSF). Both feed the program chart (5.5) and the pie (5.6).
/// </summary>
public sealed class ProgramTable
{
    public List<ProgramRow> Rows { get; init; } = new();
    /// <summary>"Model (GSF)" or "Excel: name.xlsx".</summary>
    public string Source { get; init; } = "";
    /// <summary>SF unit shown in the totals: ASF for an Excel table, GSF for the model.</summary>
    public string Unit { get; init; } = "GSF";
    public List<string> Notes { get; } = new();

    public double Total => Rows.Sum(r => r.Area);

    /// <summary>Area per program key, in colour-scheme order.</summary>
    public List<(string Key, double Area)> ByProgram(IEnumerable<ProgramSetting> order)
    {
        var sums = Rows.GroupBy(r => r.ProgramKey).ToDictionary(g => g.Key, g => g.Sum(r => r.Area));
        var keys = order.Select(p => p.Key).ToList();
        return sums.OrderBy(kv => keys.IndexOf(kv.Key) is var i and >= 0 ? i : int.MaxValue)
            .ThenBy(kv => kv.Key, StringComparer.Ordinal)
            .Where(kv => kv.Value > 0)
            .Select(kv => (kv.Key, kv.Value)).ToList();
    }

    public static readonly string[] Columns =
        { "Department", "Sub-group", "Code", "Room Name", "Program Category", "Qty", "ASF/each", "Multiplier" };

    /// <summary>
    /// PRD 5.5 option 2: no Excel, so every mass becomes a row. Department = program, Sub-group = level,
    /// Room Name = the name from tab 4, area = the resolved footprint (overlaps already removed).
    /// </summary>
    public static ProgramTable FromModel(ExportContext ctx)
    {
        var t = new ProgramTable { Source = "Model (GSF)", Unit = "GSF" };
        foreach (var plan in ctx.Result.Plans)
            foreach (var f in plan.Footprints.Where(f => !f.OpenToBelow && !f.Resolved.IsEmpty && f.Resolved.Area > 1))
            {
                var name = ctx.Names.TryGetValue(f.Mass.Id, out var n) ? n.Label : ctx.ProgramLabel(f.Mass.ProgramKey);
                t.Rows.Add(new ProgramRow(ctx.ProgramLabel(f.Mass.ProgramKey), plan.Level.Name, "", name,
                    f.Mass.ProgramKey, 1, f.Resolved.Area));
            }
        if (t.Rows.Count == 0) t.Notes.Add("Program chart: no program footprints in the model.");
        return t;
    }

    /// <summary>Reads the plugin's Excel template (first worksheet, header row 1); unknown categories match by name.</summary>
    public static ProgramTable FromExcel(string path, LayerClassifier classifier)
    {
        var t = new ProgramTable { Source = "Excel: " + Path.GetFileName(path), Unit = "ASF" };
        using var wb = new XLWorkbook(path);
        var ws = wb.Worksheets.First();
        var head = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var first = ws.FirstRowUsed();
        if (first == null) return t;
        foreach (var c in first.CellsUsed()) head[c.GetString().Trim()] = c.Address.ColumnNumber;
        int Col(params string[] names) => names.Select(n => head.GetValueOrDefault(n)).FirstOrDefault(i => i > 0);
        int dep = Col("Department"), sub = Col("Sub-group", "Subgroup", "Sub group"), code = Col("Code"),
            room = Col("Room Name", "Room"), cat = Col("Program Category", "Category", "Program"),
            qty = Col("Qty", "Quantity"), asf = Col("ASF/each", "ASF each", "ASF"), mul = Col("Multiplier", "Mult");
        if (room == 0 || asf == 0)
        {
            t.Notes.Add($"Excel: '{Path.GetFileName(path)}' has no 'Room Name' / 'ASF/each' column; using the model instead.");
            return t;
        }
        foreach (var row in ws.RowsUsed().Skip(1))
        {
            string S(int c) => c > 0 ? row.Cell(c).GetString().Trim() : "";
            double N(int c, double fallback) => c > 0 && row.Cell(c).TryGetValue<double>(out var v) ? v : fallback;
            var name = S(room);
            var area = N(asf, 0);
            if (name.Length == 0 && area <= 0) continue;
            var category = S(cat);
            var def = classifier.MatchProgram(category.Length > 0 ? category : name);
            if (def == null && category.Length > 0) t.Notes.Add($"Excel: program '{category}' is not in the colour scheme (tab 5).");
            t.Rows.Add(new ProgramRow(S(dep), S(sub), S(code), name, def?.Key ?? ProgramCatalog.Unassigned,
                Math.Max(1, N(qty, 1)), area, N(mul, 1)));
        }
        return t;
    }

    /// <summary>Writes the empty template the user fills in (PRD 5.5), with the scheme's programs on a second sheet.</summary>
    public static void WriteTemplate(string path, IEnumerable<ProgramSetting> programs)
    {
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("Program");
        for (var i = 0; i < Columns.Length; i++)
        {
            ws.Cell(1, i + 1).Value = Columns[i];
            ws.Cell(1, i + 1).Style.Font.Bold = true;
        }
        ws.Cell(2, 1).Value = "RESEARCH LABS";
        ws.Cell(2, 2).Value = "Typical Floor";
        ws.Cell(2, 3).Value = "1.3.3";
        ws.Cell(2, 4).Value = "Reservable Benching";
        ws.Cell(2, 5).Value = "Lab";
        ws.Cell(2, 6).Value = 8;
        ws.Cell(2, 7).Value = 420;
        ws.Cell(2, 8).Value = 6;
        ws.Columns().AdjustToContents();

        var help = wb.AddWorksheet("Programs");
        help.Cell(1, 1).Value = "Program Category";
        help.Cell(1, 2).Value = "Key";
        help.Cell(1, 1).Style.Font.Bold = true;
        help.Cell(1, 2).Style.Font.Bold = true;
        var r = 2;
        foreach (var p in programs)
        {
            help.Cell(r, 1).Value = p.Label;
            help.Cell(r++, 2).Value = p.Key;
        }
        help.Columns().AdjustToContents();
        wb.SaveAs(path);
    }

    /// <summary>The table the charts use: Excel when the user picked one and it reads, else the model.</summary>
    public static ProgramTable For(ExportContext ctx)
    {
        var path = ctx.Settings.Output.ProgramExcel;
        if (string.IsNullOrWhiteSpace(path)) return FromModel(ctx);
        if (!File.Exists(path))
        {
            var t = FromModel(ctx);
            t.Notes.Add($"Excel: '{path}' not found; using the model areas.");
            return t;
        }
        try
        {
            var t = FromExcel(path, ProgramCatalog.Classifier(ctx.Programs));
            if (t.Rows.Count > 0) return t;
            var m = FromModel(ctx);
            m.Notes.AddRange(t.Notes);
            return m;
        }
        catch (Exception e)
        {
            var t = FromModel(ctx);
            t.Notes.Add($"Excel: could not read '{Path.GetFileName(path)}' ({e.Message}); using the model areas.");
            return t;
        }
    }

    public static string Sf(double v) => v.ToString("N0", CultureInfo.InvariantCulture);
}
