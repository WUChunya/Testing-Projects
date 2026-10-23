using System.Globalization;
using System.Text.RegularExpressions;
using PD.Core.Analysis;
using PD.Core.Classification;
using PD.Core.Config;
using PD.Core.Settings;

namespace PD.Core.Drawing;

/// <summary>Everything a drawing generator needs: analysis, user choices, scheme, style and page setup.</summary>
public sealed class ExportContext
{
    public required AnalysisResult Result { get; init; }
    public required PdSettings Settings { get; init; }
    public required List<ProgramSetting> Programs { get; init; }
    public Style Style { get; init; } = Style.Default();
    public Rules Rules { get; init; } = Rules.Default();

    Dictionary<string, NameRow>? _names;

    /// <summary>Derived data shared by every drawing of one export (site layout, background per extent).</summary>
    public Dictionary<string, object> Cache { get; init; } = new();

    /// <summary>Label and stable id per mass (tab 4).</summary>
    public IReadOnlyDictionary<string, NameRow> Names => _names ??=
        Naming.Build(Result, Settings.Names, ProgramLabel).ToDictionary(n => n.MassId);

    public ProgramSetting? Program(string key) => Programs.FirstOrDefault(p => p.Key == key);

    public string ProgramLabel(string key) => Program(key)?.Label ?? key;

    /// <summary>The block has a name of its own (the object's name, or a tab 4 name other than its program's label).</summary>
    public bool HasOwnName(string massId, string programKey) => Names.TryGetValue(massId, out var row) && row.Own;

    /// <summary>Drawing scale; drawings are always at true scale (never fitted).</summary>
    public double PtPerFt => ParseScale(ScaleOverride) ?? ParseScale(Settings.Output.Scale) ?? ParseScale(Rules.Output.DefaultScale) ?? 1.6;

    /// <summary>Scale of this drawing as typed (sections have their own).</summary>
    public string ScaleText => ParseScale(ScaleOverride) != null ? ScaleOverride! : Settings.Output.Scale;

    /// <summary>A drawing-specific scale (sections: 1" = 16'-0").</summary>
    public string? ScaleOverride { get; init; }

    /// <summary>Fixed text scale (sections give absolute pt sizes).</summary>
    public double? TextScaleOverride { get; init; }

    /// <summary>Text and annotation spacing scale: drawing scale ÷ the style's reference scale (1" = 45' → 1).</summary>
    public double TextScale => TextScaleOverride ?? PtPerFt / (ParseScale(Style.Page.TextReferenceScale) ?? PtPerFt);

    /// <summary>Context for program sections (docs/SITE_STYLE.md §4): own scale, absolute text sizes.</summary>
    public ExportContext ForSection() => new()
    {
        Result = Result, Settings = Settings, Programs = Programs, Style = Style, Rules = Rules, Cache = Cache,
        ScaleOverride = string.IsNullOrWhiteSpace(Settings.Output.SectionScale) ? Rules.Output.DefaultSectionScale : Settings.Output.SectionScale,
        TextScaleOverride = 1,
    };

    /// <summary>Context for the charts (PRD 5.5 / 5.6): absolute point sizes, no drawing scale.</summary>
    public ExportContext ForChart() => new()
    {
        Result = Result, Settings = Settings, Programs = Programs, Style = Style, Rules = Rules, Cache = Cache, TextScaleOverride = 1,
    };

    /// <summary>
    /// Axon viewer azimuth in degrees (0 = east, counter-clockwise, 225 = south-west) from the output settings: a
    /// building corner, or the direction from the building's centre to the V view marker; null = the style's azimuth.
    /// </summary>
    public double? AxonAzimuth()
    {
        var o = Settings.Output;
        switch ((o.AxonView ?? "").Trim().ToUpperInvariant())
        {
            case "SW": return 225;
            case "SE": return 315;
            case "NE": return 45;
            case "NW": return 135;
            case "POINT" when o.AxonViewPoint is { Length: >= 2 } pt:
                var (x, y, _) = Result.Frame.ToLocal(pt[0], pt[1], 0);
                var c = PD.Core.Plans.PlanBackground.Footprint(Result).EnvelopeInternal;
                if (c.IsNull) c = new NetTopologySuite.Geometries.Envelope(Result.Building.MinX, Result.Building.MaxX, Result.Building.MinY, Result.Building.MaxY);
                double dx = x - c.Centre.X, dy = y - c.Centre.Y;
                return Math.Abs(dx) + Math.Abs(dy) < 1e-6 ? null : Math.Atan2(dy, dx) * 180 / Math.PI;
            default: return null;
        }
    }

    /// <summary>Use the Rhino viewport's direction (only when the view setting asks for it).</summary>
    public double[]? AxonViewDirection()
    {
        var o = Settings.Output;
        var mode = (o.AxonView ?? "").Trim().ToUpperInvariant();
        return mode == "VIEWPORT" || (mode.Length == 0 && o.AxonFromViewport) ? o.AxonViewDirection : null;
    }

    /// <summary>A size given at the reference scale, in points at the drawing scale.</summary>
    public double Pt(double atReference) => atReference * TextScale;

    /// <summary>Fixed artboard size, or null for "Fit content" (artboard grows around the drawing).</summary>
    public (double W, double H)? ArtboardSize =>
        Rules.Output.ArtboardSizesPt.TryGetValue(Settings.Output.Artboard, out var s) ? (s[0], s[1]) : null;

    /// <summary>
    /// Plan-space shape a fixed artboard is centred on (user rev 0.9.2, page.centreOn): our property line, else the
    /// building's footprint.
    /// </summary>
    public NetTopologySuite.Geometries.Geometry PageAnchor()
    {
        if (Cache.TryGetValue("pageAnchor", out var c)) return (NetTopologySuite.Geometries.Geometry)c;
        var g = string.Equals(Style.Page.CentreOn, "Building", StringComparison.OrdinalIgnoreCase)
            ? PD.Core.Plans.PlanBackground.Footprint(Result) : PD.Core.Plans.PlanBackground.PropertyArea(Result);
        if (g.IsEmpty) g = PD.Core.Plans.PlanBackground.Footprint(Result);
        Cache["pageAnchor"] = g;
        return g;
    }

    /// <summary>
    /// Start of a <paramref name="size"/>-long drawing on a fixed artboard's free span lo…hi: <paramref name="centre"/>
    /// (the anchor's position within the drawing) lands mid-span, kept inside the span; null when it does not fit.
    /// </summary>
    public static double? Place(double lo, double hi, double size, double centre)
    {
        if (size > hi - lo + 0.5) return null;
        return Math.Clamp((lo + hi) / 2 - centre, lo, Math.Max(lo, hi - size));
    }

    /// <summary>Note for a drawing larger than the fixed artboard: fitted to its content (same scale) or left as is.</summary>
    public string LargerNote(string what) => Style.Page.FitContentWhenLarger
        ? $"{what} at {ScaleText} is larger than {Settings.Output.Artboard}: its artboard fits the content instead (same scale)."
        : $"{what} at {ScaleText} is larger than {Settings.Output.Artboard}; use \"Fit content\" or a smaller scale.";

    /// <summary>
    /// Imperial "1/32\" = 1'-0\"" → 2.25 pt per foot, "1\" = 50'" → 1.44; metric ratio "1:200" → 4.32.
    /// </summary>
    public static double? ParseScale(string? scale)
    {
        if (scale == null) return null;
        var ratio = Regex.Match(scale, "^\\s*1\\s*:\\s*(\\d+(?:\\.\\d+)?)\\s*$");
        if (ratio.Success)
        {
            var n = double.Parse(ratio.Groups[1].Value, CultureInfo.InvariantCulture);
            return n <= 0 ? null : 12.0 * 72.0 / n;
        }
        var m = Regex.Match(scale, "^\\s*(\\d+(?:\\.\\d+)?)(?:\\s*/\\s*(\\d+))?\\s*(?:\"|in)\\s*=\\s*(\\d+(?:\\.\\d+)?)\\s*(?:'|ft)");
        if (!m.Success) return null;
        var inches = double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
        if (m.Groups[2].Success) inches /= double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
        var feet = double.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture);
        return feet <= 0 ? null : 72.0 * inches / feet;
    }

    /// <summary>Program fill/stroke as swatches (special styles: Shell outline, Void dashed, Unassigned grey).</summary>
    public (string? fill, string? stroke, double strokePt, double[]? dash) ProgramPaint(Drawing d, string key)
    {
        var p = Program(key) ?? Program(ProgramCatalog.Unassigned);
        if (p == null) return (d.Gray("#BFBFBF"), null, 0, null);
        var fill = d.Swatch("PRG_" + p.Key, p.Fill);
        var stroke = p.Stroke == null ? null : d.Swatch("PRG_" + p.Key + "_LINE", p.Stroke);
        return (fill, stroke, p.StrokePt ?? 0, p.Dash);
    }

    /// <summary>Paragraph styles shared by every file (unified Arial typography).</summary>
    public void AddStandardStyles(Drawing d)
    {
        var f = Style.Fonts;
        void Add(string name, Config.Font font, string align, string? color = null) =>
            d.ParaStyles.Add(new ParaStyle(name, font.PostScript, font.FallbackPostScript, font.Family, Pt(font.SizePt), font.AllCaps, align,
                d.Gray(color ?? font.Color)!, font.Weight, font.Opacity, font.Tracking, Pt(font.LeadingPt)));
        Add("PD Label", f.Label, "center");
        Add("PD Label Light", f.Label, "center", Style.Page.LightLabelColor);
        Add("PD Note", f.Note, "left");
        Add("PD Note Right", f.Note, "right");
        Add("PD Title", f.Title, "left");
        Add("PD Level", f.Level, "left");
        Add("PD Level Tag", f.LevelTag, "left");
        Add("PD Plane Label", f.PlaneLabel, "center");
        Add("PD Plane Label Small", f.PlaneLabelSmall, "center");
        Add("PD Plane Label B", f.PlaneLabelB, "center");
        Add("PD Plane Label B Small", f.PlaneLabelBSmall, "center");
        Add("PD Plane Label Min", f.PlaneLabelMin, "center");
        Add("PD Core Label", f.CoreLabel, "center");
        Add("PD Context Label", f.ContextLabel, "center");
        Add("PD Street", f.Street, "center");
        Add("PD Heading", f.Heading, "left");
        Add("PD Section Label", f.SectionLabel, "right");
        Add("PD Floor Number", f.FloorNumber, "left");
        Add("PD Chart Title", f.ChartTitle, "left");
        Add("PD Chart Group", f.ChartGroup, "left");
        Add("PD Chart Total", f.ChartTotal, "right");
        Add("PD Chart Total Left", f.ChartTotal, "left");
        Add("PD Chart Legend", f.ChartLegend, "left");
        Add("PD Chart Legend Right", f.ChartLegend, "right");
    }

    /// <summary>The dark or the light ("… Light") variant of a label style, whichever reads on the given fill swatch.</summary>
    public string LabelStyle(Drawing d, string? fillSwatch, string baseStyle = "PD Label")
    {
        var hex = fillSwatch == null ? null : d.Swatches.FirstOrDefault(s => s.Name == fillSwatch)?.Hex;
        if (hex is not { Length: 7 }) return baseStyle;
        int C(int i) => Convert.ToInt32(hex.Substring(1 + 2 * i, 2), 16);
        var luma = 0.299 * C(0) + 0.587 * C(1) + 0.114 * C(2);
        return luma < Style.Page.LabelLightBelowLuma ? baseStyle + " Light" : baseStyle;
    }
}
