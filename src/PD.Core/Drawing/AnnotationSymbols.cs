using PD.Core.Config;
using PD.Core.Sections;

namespace PD.Core.Drawing;

/// <summary>
/// Annotation symbols for later analysis work (user rev 0.8.4, docs/reference site plan / site axon legends): entry,
/// service, view marker, car, pedestrian / vehicle flow, view / gateway, transit, tree, tag, context label, street
/// name, note. Drawn as editable, named items (copy them onto ANNOTATION), each row with its name and its spec.
/// Absolute pt (like the charts); colours and sizes in style "annotation".
/// </summary>
public static class AnnotationSymbols
{
    public enum Set { Plan, Axon }

    /// <summary>Rows starting at (x, y) on artboard a; returns the y below the last row.</summary>
    public static double Add(PD.Core.Drawing.Drawing d, ExportContext ctx, Set set, string prefix, int a, double x, double y, string layerName = "PD_LEGEND")
    {
        var st = ctx.Style.Annotation;
        var layer = d.Layer(layerName);
        var row = st.LegendRowPt;
        var r = st.IconPt / 2;
        void Put(DItem it) { it.Artboard = a; layer.Items.Add(it); }
        void Text(string name, double tx, double ty, string text, string style, double? size = null)
        {
            var t = SectionDrawing.Text(prefix + name, tx, ty, text, style, "left");
            if (size != null) t.SizePt = size.Value;
            Put(t);
        }

        var rows = Rows(st, set);
        Text("SYMBOLS_HEADING", x, y, set == Set.Plan ? "ANNOTATION SYMBOLS" : "AXON LEGEND", "PD Heading");
        var cy = y + 16 + r;
        foreach (var (key, title, spec) in rows)
        {
            var name = $"SYM_{key}";
            var cx = x + r;
            foreach (var it in Symbol(d, st, key, name, prefix, cx, cy, r)) Put(it);
            Text(name + "_TITLE", x + st.IconPt * 3.6 + 8, cy - 1, title, "PD Note");
            Text(name + "_SPEC", x + st.IconPt * 3.6 + 8, cy + 9, spec, "PD Note", 6);
            cy += row;
        }
        return cy;
    }

    static List<(string Key, string Title, string Spec)> Rows(AnnotationStyle st, Set set)
    {
        var plan = new List<(string, string, string)>
        {
            ("ENTRY", "ENTRY", $"{st.EntryFill} ICON + TRIANGLE"),
            ("SERVICE", "SERVICE / LOADING", $"{st.ServiceFill} ICON + TRIANGLE"),
            ("VIEW_MARKER", "VIEW MARKER", $"{st.ViewMarkerFill}, WHITE NUMBER, LEADER"),
            ("CAR", "PARKING / CAR", $"{st.CarStroke} TOP VIEW"),
            ("PED_FLOW", "PEDESTRIAN FLOW", $"{st.PedFlowStroke} {st.FlowPt} PT DASH"),
            ("CAR_FLOW", "VEHICLE FLOW", $"{st.CarFlowStroke} {st.FlowPt} PT DASH"),
            ("GATEWAY", "VIEW / GATEWAY", $"{st.GatewayStroke} {st.GatewayPt} PT DASH"),
            ("TRANSIT", "TRANSIT STOP", $"{st.TransitFill} ICON"),
            ("TREE", "TREE", "CANOPY CIRCLE (PLAN) / LOLLIPOP (AXON)"),
            ("TAG", "TAG", "ROUNDED LABEL + LEADER"),
            ("CONTEXT", "FUTURE BUILDING", "CONTEXT LABEL: PD CONTEXT LABEL STYLE"),
            ("STREET", "STREET NAME", "PD NOTE CAPS, ROTATED ALONG THE STREET"),
            ("NOTE", "NOTE / ANNOTATION", "PD NOTE"),
        };
        if (set == Set.Plan) return plan;
        // Axons: tags, flows, transportation and annotation first (analysis overlays).
        var order = new[] { "TAG", "PED_FLOW", "CAR_FLOW", "TRANSIT", "GATEWAY", "SERVICE", "ENTRY", "VIEW_MARKER", "TREE", "CONTEXT", "STREET", "NOTE" };
        return order.Select(k => plan.First(p => p.Item1 == k)).ToList();
    }

    /// <summary>One symbol centred at (cx, cy), r = half the icon size.</summary>
    static IEnumerable<DItem> Symbol(PD.Core.Drawing.Drawing d, AnnotationStyle st, string key, string name, string prefix, double cx, double cy, double r)
    {
        var white = d.Gray("#FFFFFF");
        switch (key)
        {
            case "ENTRY":
                yield return Disc(prefix + name, cx, cy, r, d.Swatch("ANNO_ENTRY", st.EntryFill));
                yield return Icon(prefix + name + "_ICON", Walker, cx, cy, r * 0.8, white);
                yield return Poly(prefix + name + "_ARROW", new[] { (cx + r * 1.25, cy - r * 0.45), (cx + r * 1.25, cy + r * 0.45), (cx + r * 1.85, cy) }, d.Swatch("ANNO_ENTRY", st.EntryFill));
                break;
            case "SERVICE":
                yield return Disc(prefix + name, cx, cy, r, d.Swatch("ANNO_SERVICE", st.ServiceFill));
                foreach (var (part, k) in Truck.Select((p, k) => (p, k))) yield return Icon(prefix + name + $"_ICON_{k + 1}", part, cx, cy, r * 0.8, white);
                yield return Poly(prefix + name + "_ARROW", new[] { (cx + r * 1.25, cy - r * 0.45), (cx + r * 1.25, cy + r * 0.45), (cx + r * 1.85, cy) }, d.Swatch("ANNO_SERVICE", st.ServiceFill));
                break;
            case "VIEW_MARKER":
                yield return Line(prefix + name + "_LEADER", new[] { (cx + r * 0.7, cy - r * 0.7), (cx + r * 2.2, cy - r * 1.6) }, d.Swatch("ANNO_VIEW", st.ViewMarkerFill), 1.5);
                yield return Disc(prefix + name, cx, cy, r, d.Swatch("ANNO_VIEW", st.ViewMarkerFill));
                var n = SectionDrawing.Text(prefix + name + "_NO", cx, cy, "1", "PD Note", "center", center: true);
                n.SizePt = r * 1.1;
                yield return n;
                break;
            case "CAR":
                foreach (var part in CarTop) yield return Icon(prefix + name + (part == CarTop[0] ? "" : "_GLASS"), part, cx, cy, r * 1.1, part == CarTop[0] ? d.Gray("#FFFFFF") : d.Gray("#C8C8C8"), d.Gray(st.CarStroke), 0.75);
                break;
            case "PED_FLOW":
            case "CAR_FLOW":
            case "GATEWAY":
                var (stroke, pt, dash, icon, sw) = key switch
                {
                    "PED_FLOW" => (st.PedFlowStroke, st.FlowPt, st.PedFlowDash, Walker, "ANNO_PED_FLOW"),
                    "CAR_FLOW" => (st.CarFlowStroke, st.FlowPt, st.CarFlowDash, CarSide, "ANNO_CAR_FLOW"),
                    _ => (st.GatewayStroke, st.GatewayPt, st.GatewayDash, Eye, "ANNO_GATEWAY"),
                };
                var swatch = d.Swatch(sw, stroke);
                var line = Line(prefix + name + "_LINE", new[] { (cx - r, cy), (cx + r * 3.0, cy) }, swatch, pt);
                line.Dash = dash;
                yield return line;
                yield return Poly(prefix + name + "_ARROW", new[] { (cx + r * 3.0, cy - pt * 1.4), (cx + r * 3.0, cy + pt * 1.4), (cx + r * 3.0 + pt * 2.2, cy) }, swatch);
                yield return Disc(prefix + name, cx, cy, r * 0.9, swatch);
                yield return Icon(prefix + name + "_ICON", icon, cx, cy, r * 0.7, white);
                break;
            case "TRANSIT":
                yield return Disc(prefix + name, cx, cy, r, d.Swatch("ANNO_TRANSIT", st.TransitFill));
                foreach (var (part, k) in Bus.Select((p, k) => (p, k))) yield return Icon(prefix + name + $"_ICON_{k + 1}", part, cx, cy, r * 0.75, white);
                break;
            case "TREE":
                var tree = Disc(prefix + name, cx, cy, r, d.Swatch("ANNO_TREE", st.TreeFill));
                tree.Opacity = 0.6;
                yield return tree;
                break;
            case "TAG":
                yield return Line(prefix + name + "_LEADER", new[] { (cx - r, cy + r * 0.9), (cx - r * 0.2, cy + r * 0.2) }, d.Gray(st.TagStroke), 0.5);
                yield return new DItem
                {
                    Kind = ItemKind.RoundRect, Name = prefix + name, X = cx - r * 0.2, Y = cy - r * 0.6, W = r * 2.6, H = r * 1.2, R = r * 0.6,
                    Fill = white, Stroke = d.Gray(st.TagStroke), StrokePt = 0.75,
                };
                var t = SectionDrawing.Text(prefix + name + "_TXT", cx + r * 1.1, cy, "TAG", "PD Note", "center", center: true);
                t.SizePt = r * 0.8;
                yield return t;
                break;
            case "CONTEXT":
                var c = SectionDrawing.Text(prefix + name, cx - r, cy + 3, "FUTURE BLDG", "PD Context Label", "left");
                c.SizePt = r * 0.9;
                yield return c;
                break;
            case "STREET":
                var s = SectionDrawing.Text(prefix + name, cx - r, cy + 3, "MAIN ST", "PD Note", "left");
                s.SizePt = r * 0.9;
                yield return s;
                break;
            default:
                var note = SectionDrawing.Text(prefix + name, cx - r, cy + 3, "NOTE", "PD Note", "left");
                note.SizePt = r * 0.9;
                yield return note;
                break;
        }
    }

    static DItem Disc(string name, double cx, double cy, double r, string? fill) => new()
    {
        Kind = ItemKind.Path, Name = name, Fill = fill,
        Paths = new() { Enumerable.Range(0, 32).Select(i => new[] { cx + r * Math.Cos(i * Math.PI / 16), cy + r * Math.Sin(i * Math.PI / 16) }).ToArray() },
    };

    static DItem Poly(string name, (double X, double Y)[] pts, string? fill) => new()
    {
        Kind = ItemKind.Path, Name = name, Fill = fill, Paths = new() { pts.Select(p => new[] { p.X, p.Y }).ToArray() },
    };

    static DItem Line(string name, (double X, double Y)[] pts, string? stroke, double pt) => new()
    {
        Kind = ItemKind.Path, Name = name, Closed = false, Stroke = stroke, StrokePt = pt, Paths = new() { pts.Select(p => new[] { p.X, p.Y }).ToArray() },
    };

    /// <summary>Icon rings (unit box −1…1, y down) placed at (cx, cy) with half size s.</summary>
    static DItem Icon(string name, double[][][] rings, double cx, double cy, double s, string? fill, string? stroke = null, double pt = 0) => new()
    {
        Kind = ItemKind.Path, Name = name, Fill = fill, Stroke = stroke, StrokePt = pt,
        Paths = rings.Select(ring => ring.Select(p => new[] { cx + p[0] * s, cy + p[1] * s }).ToArray()).ToList(),
    };

    static double[][] Circle(double x, double y, double r) => Enumerable.Range(0, 16).Select(i => new[] { x + r * Math.Cos(i * Math.PI / 8), y + r * Math.Sin(i * Math.PI / 8) }).ToArray();

    static double[][] P(params double[] xy) => Enumerable.Range(0, xy.Length / 2).Select(i => new[] { xy[2 * i], xy[2 * i + 1] }).ToArray();

    /// <summary>Walking figure: head and a striding body.</summary>
    static readonly double[][][] Walker =
    {
        Circle(0.12, -0.72, 0.17),
        P(-0.05, -0.48, 0.22, -0.46, 0.36, -0.16, 0.56, -0.04, 0.50, 0.06, 0.26, -0.06, 0.18, -0.22, 0.12, 0.08, 0.34, 0.34, 0.36, 0.86,
          0.20, 0.86, 0.18, 0.42, 0.00, 0.22, -0.12, 0.52, -0.36, 0.86, -0.50, 0.76, -0.28, 0.42, -0.14, 0.06, -0.10, -0.26, -0.24, -0.06,
          -0.36, 0.12, -0.48, 0.04, -0.30, -0.26),
    };

    /// <summary>Delivery truck: box, cab, wheels.</summary>
    static readonly double[][][][] Truck =
    {
        new[] { P(-0.85, -0.45, 0.20, -0.45, 0.20, 0.30, -0.85, 0.30) },
        new[] { P(0.26, -0.20, 0.58, -0.20, 0.85, 0.05, 0.85, 0.30, 0.26, 0.30) },
        new[] { Circle(-0.50, 0.42, 0.16), Circle(0.55, 0.42, 0.16) },
    };

    /// <summary>Bus: body with a window band (the band is a hole) and wheels.</summary>
    static readonly double[][][][] Bus =
    {
        new[] { P(-0.80, -0.60, 0.80, -0.60, 0.80, 0.45, -0.80, 0.45), P(-0.62, -0.42, 0.62, -0.42, 0.62, -0.05, -0.62, -0.05) },
        new[] { Circle(-0.45, 0.55, 0.15), Circle(0.45, 0.55, 0.15) },
    };

    /// <summary>Car from above: body and windscreen.</summary>
    static readonly double[][][][] CarTop =
    {
        new[] { P(-0.90, -0.42, 0.75, -0.42, 0.95, -0.25, 0.95, 0.25, 0.75, 0.42, -0.90, 0.42, -0.98, 0.25, -0.98, -0.25) },
        new[] { P(0.25, -0.32, 0.50, -0.30, 0.50, 0.30, 0.25, 0.32), P(-0.70, -0.30, -0.50, -0.32, -0.50, 0.32, -0.70, 0.30) },
    };

    /// <summary>Car from the side, for the vehicle flow badge.</summary>
    static readonly double[][] CarSideBody = P(-0.85, 0.20, -0.85, -0.05, -0.50, -0.10, -0.25, -0.40, 0.30, -0.40, 0.55, -0.10, 0.85, -0.05, 0.85, 0.20);
    static readonly double[][][] CarSide = { CarSideBody, Circle(-0.45, 0.25, 0.15), Circle(0.45, 0.25, 0.15) };

    /// <summary>Eye: almond with the pupil as a hole.</summary>
    static readonly double[][][] Eye =
    {
        Enumerable.Range(0, 24).Select(i =>
        {
            var t = i / 24.0 * 2 * Math.PI;
            return new[] { 0.9 * Math.Cos(t), 0.5 * Math.Sin(t) * Math.Abs(Math.Sin(t)) };
        }).ToArray(),
        Circle(0, 0, 0.22),
    };
}
