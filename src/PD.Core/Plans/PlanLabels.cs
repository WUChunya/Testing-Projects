using NetTopologySuite.Algorithm;
using NetTopologySuite.Algorithm.Construct;
using NetTopologySuite.Geometries;
using PD.Core.Drawing;
using PD.Core.Geometry;
using NtsGeometry = NetTopologySuite.Geometries.Geometry;

namespace PD.Core.Plans;

/// <summary>Where a label goes inside a block (feet), its reading angle and the wrapped text; null = use a leader.</summary>
public sealed record LabelSpot(double X, double Y, double AngleDeg, string Text);

/// <summary>
/// PRD 5.2 labels: at the centre of the largest inscribed circle (the widest spot), reading along the block's long
/// direction; wrapped to fit the chord through that spot. Shared by plans and the exploded axon.
/// </summary>
public static class PlanLabels
{
    /// <param name="sizeFt">Font size converted to feet at the drawing scale.</param>
    public static LabelSpot? Fit(NtsGeometry block, string text, double sizeFt)
    {
        var poly = Largest(block);
        if (poly == null || poly.Area < 1e-3) return null;
        var mic = new MaximumInscribedCircle(poly, Math.Max(0.05, Math.Sqrt(poly.Area) / 200));
        var c = mic.GetCenter().Coordinate;
        var radius = mic.GetRadiusLine().Length;
        foreach (var angle in Angles(poly))
        {
            var chord = Chord(poly, c, angle);
            var wrapped = TextFit.Wrap(text, sizeFt, chord * 0.9 - sizeFt * 0.5, 2 * radius - sizeFt * 0.3);
            if (wrapped != null) return new LabelSpot(c.X, c.Y, angle, wrapped);
        }
        return null;
    }

    /// <summary>A point for the label's anchor (widest spot), used by leaders.</summary>
    public static Coordinate Anchor(NtsGeometry block)
    {
        var poly = Largest(block);
        return poly == null ? block.Centroid.Coordinate : new MaximumInscribedCircle(poly, Math.Max(0.05, Math.Sqrt(poly.Area) / 200)).GetCenter().Coordinate;
    }

    /// <summary>Long direction first (read left → right, or bottom → top when vertical), then horizontal.</summary>
    static IEnumerable<double> Angles(Polygon poly)
    {
        var rect = new MinimumDiameter(poly).GetMinimumRectangle();
        var a = 0.0;
        if (rect is Polygon r && r.ExteriorRing.NumPoints >= 4)
        {
            var p = r.ExteriorRing.Coordinates;
            var e1 = (p[1].X - p[0].X, p[1].Y - p[0].Y);
            var e2 = (p[2].X - p[1].X, p[2].Y - p[1].Y);
            var (dx, dy) = Len(e1) >= Len(e2) ? e1 : e2;
            a = Math.Atan2(dy, dx) * 180 / Math.PI;
            while (a > 90) a -= 180;
            while (a <= -90) a += 180;
            if (Math.Abs(a) < 2) a = 0;
            if (Math.Abs(a) > 88) a = 90;
        }
        yield return a;
        if (a != 0) yield return 0;
    }

    static double Len((double X, double Y) v) => Math.Sqrt(v.X * v.X + v.Y * v.Y);

    /// <summary>Length of the part of the line through c at the angle that lies inside the polygon around c.</summary>
    static double Chord(Polygon poly, Coordinate c, double angleDeg)
    {
        var t = angleDeg * Math.PI / 180;
        var L = poly.EnvelopeInternal.Width + poly.EnvelopeInternal.Height;
        var line = MeshSlicer.Factory.CreateLineString(new[]
        {
            new Coordinate(c.X - Math.Cos(t) * L, c.Y - Math.Sin(t) * L), new Coordinate(c.X + Math.Cos(t) * L, c.Y + Math.Sin(t) * L),
        });
        NtsGeometry inside;
        try { inside = poly.Intersection(line); }
        catch (TopologyException) { return 0; }
        var pt = MeshSlicer.Factory.CreatePoint(c);
        for (var i = 0; i < inside.NumGeometries; i++)
            if (inside.GetGeometryN(i) is LineString ls && ls.Distance(pt) < 1e-6)
            {
                // Symmetric around the centre so a centred label never overhangs.
                var d0 = ls.StartPoint.Distance(pt);
                var d1 = ls.EndPoint.Distance(pt);
                return 2 * Math.Min(d0, d1);
            }
        return 0;
    }

    static Polygon? Largest(NtsGeometry g)
    {
        Polygon? best = null;
        for (var i = 0; i < g.NumGeometries; i++)
            if (g.GetGeometryN(i) is Polygon p && (best == null || p.Area > best.Area)) best = p;
        return best;
    }
}
