using System.Drawing;
using Rhino.Display;
using Rhino.Geometry;
using PD.Rhino.Adapter;

namespace PD.Rhino.Display;

/// <summary>
/// The axon view marker (user rev 0.8.3): a V in plan at grade, apex where the viewer stands, its arms opening
/// towards the building. Shown for a building corner (SW / SE / NE / NW) and for a placed view point.
/// </summary>
public sealed class AxonViewConduit : DisplayConduit
{
    Line[] _lines = Array.Empty<Line>();
    Point3d _label = Point3d.Unset;
    BoundingBox _bbox = BoundingBox.Empty;

    public static AxonViewConduit Instance { get; } = new();

    public void Update(PdSession s)
    {
        _lines = Array.Empty<Line>();
        _label = Point3d.Unset;
        _bbox = BoundingBox.Empty;
        var r = s.Result;
        if (r == null) return;
        var o = s.Settings.Output;
        var b = r.Building;
        var (cx, cy, _) = r.Frame.ToModel((b.MinX + b.MaxX) / 2, (b.MinY + b.MaxY) / 2, 0);
        var (x0, y0, _) = r.Frame.ToModel(b.MinX, b.MinY, 0);
        var (x1, y1, _) = r.Frame.ToModel(b.MaxX, b.MaxY, 0);
        var span = Math.Max(Math.Abs(x1 - x0), Math.Abs(y1 - y0));
        var mode = (o.AxonView ?? "").Trim().ToUpperInvariant();
        Point3d apex;
        switch (mode)
        {
            case "POINT" when o.AxonViewPoint is { Length: >= 2 } p: apex = new Point3d(p[0], p[1], 0); break;
            case "SE": apex = new Point3d(x1 + 0.3 * span, y0 - 0.3 * span, 0); break;
            case "NE": apex = new Point3d(x1 + 0.3 * span, y1 + 0.3 * span, 0); break;
            case "NW": apex = new Point3d(x0 - 0.3 * span, y1 + 0.3 * span, 0); break;
            case "VIEWPORT": return;
            default: apex = new Point3d(x0 - 0.3 * span, y0 - 0.3 * span, 0); break;
        }
        var dir = new Vector3d(cx - apex.X, cy - apex.Y, 0);
        if (!dir.Unitize()) return;
        var arm = 0.2 * span;
        var left = dir; left.Rotate(Math.PI / 6, Vector3d.ZAxis);
        var right = dir; right.Rotate(-Math.PI / 6, Vector3d.ZAxis);
        _lines = new[] { new Line(apex, apex + left * arm), new Line(apex, apex + right * arm) };
        _label = apex - dir * 0.05 * span;
        _bbox = new BoundingBox(new[] { apex, apex + left * arm, apex + right * arm });
    }

    protected override void CalculateBoundingBox(CalculateBoundingBoxEventArgs e)
    {
        if (_bbox.IsValid) e.IncludeBoundingBox(_bbox);
    }

    protected override void PostDrawObjects(DrawEventArgs e)
    {
        foreach (var l in _lines) e.Display.DrawLine(l, Color.OrangeRed, 3);
        if (_label.IsValid) e.Display.Draw2dText("AXON VIEW", Color.OrangeRed, _label, true, 12);
    }
}
