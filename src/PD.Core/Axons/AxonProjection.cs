using NetTopologySuite.Geometries;
using PD.Core.Geometry;
using NtsGeometry = NetTopologySuite.Geometries.Geometry;

namespace PD.Core.Axons;

/// <summary>
/// Parallel (axonometric) projection: model feet (x, y, z) → screen feet (sx right, sy up) and a depth (larger = nearer
/// the viewer). Default isometric from the south-west; or the direction of the user's Rhino viewport.
/// </summary>
public sealed class AxonProjection
{
    readonly (double X, double Y, double Z) _r, _u, _c;

    /// <param name="toViewer">Unit vector from the model towards the viewer (must point upwards).</param>
    public AxonProjection((double X, double Y, double Z) toViewer)
    {
        var c = Norm(toViewer);
        if (c.Z < 0.05 || Math.Abs(c.Z) > 0.999) c = Norm(FromAngles(225, 35.264));
        _c = c;
        _r = Norm((-c.Y, c.X, 0)); // = view direction × Z, with view = −c
        _u = Cross(_r, (-c.X, -c.Y, -c.Z));
    }

    /// <summary>Raw affine rows (screen x, screen y-up) with the direction towards the viewer derived from them.</summary>
    AxonProjection((double X, double Y, double Z) r, (double X, double Y, double Z) u)
    {
        _r = r;
        _u = u;
        var c = (r.Y * u.Z - r.Z * u.Y, r.Z * u.X - r.X * u.Z, r.X * u.Y - r.Y * u.X);
        _c = Norm(c.Item3 < 0 ? (-c.Item1, -c.Item2, -c.Item3) : c);
    }

    /// <param name="trueLength">Isometric drawing (AXON_STYLE §2): no foreshortening, every axis at the drawing scale.</param>
    public static AxonProjection FromAngles(double azimuthDeg, double elevationDeg, double[]? viewDirection = null, bool trueLength = false)
    {
        if (viewDirection is { Length: 3 } v) return new AxonProjection((-v[0], -v[1], -v[2]));
        var p = new AxonProjection(FromAngles(azimuthDeg, elevationDeg));
        if (!trueLength) return p;
        // Scale so a horizontal axis keeps its true length on the page (isometric: × 1.22474).
        var (ax, ay) = p.Project(1, 0, 0);
        var f = 1 / Math.Sqrt(ax * ax + ay * ay);
        return new AxonProjection((p._r.X * f, p._r.Y * f, p._r.Z * f), (p._u.X * f, p._u.Y * f, p._u.Z * f));
    }

    /// <summary>Planometric 30°/60° (SITE_STYLE §3): plan undistorted, plan +Y up-right 30°, plan +X down-right 60°, height true.</summary>
    public static AxonProjection Planometric(double yAxisDeg = 30)
    {
        double t = yAxisDeg * Math.PI / 180;
        // Screen x = sin t·x + cos t·y; screen y-up = −cos t·x + sin t·y + z.
        return new AxonProjection((Math.Sin(t), Math.Cos(t), 0), (-Math.Cos(t), Math.Sin(t), 1));
    }

    /// <summary>The page direction (unit, y up) of a plan axis that runs to the lower right — for leaders along the drawing.</summary>
    public (double X, double Y) DownRightAxis()
    {
        foreach (var (x, y) in new[] { (1.0, 0.0), (0.0, 1.0), (-1.0, 0.0), (0.0, -1.0) })
        {
            var (sx, sy) = Project(x, y, 0);
            if (sx > 1e-6 && sy < -1e-6)
            {
                var l = Math.Sqrt(sx * sx + sy * sy);
                return (sx / l, sy / l);
            }
        }
        return (Math.Cos(Math.PI / 6), -0.5);
    }

    static (double, double, double) FromAngles(double az, double el)
    {
        double a = az * Math.PI / 180, e = el * Math.PI / 180;
        return (Math.Cos(e) * Math.Cos(a), Math.Cos(e) * Math.Sin(a), Math.Sin(e));
    }

    public (double X, double Y) Project(double x, double y, double z) => (Dot(_r, x, y, z), Dot(_u, x, y, z));

    /// <summary>Screen feet per foot of height.</summary>
    public double ZScale => _u.Z;

    public double Depth(double x, double y, double z) => Dot(_c, x, y, z);

    /// <summary>
    /// Depth (larger = nearer) of the point of the plane n·P = d0 that projects to screen (sx, sy). The screen rows r, u
    /// and the view direction c are mutually perpendicular, so that point is A + λ·c with A = sx·r/|r|² + sy·u/|u|²;
    /// NaN for a plane seen edge-on.
    /// </summary>
    public double DepthOnPlane(double sx, double sy, (double X, double Y, double Z) n, double d0)
    {
        double rr = Dot(_r, _r.X, _r.Y, _r.Z), uu = Dot(_u, _u.X, _u.Y, _u.Z);
        double ax = sx * _r.X / rr + sy * _u.X / uu, ay = sx * _r.Y / rr + sy * _u.Y / uu, az = sx * _r.Z / rr + sy * _u.Z / uu;
        var nc = n.X * _c.X + n.Y * _c.Y + n.Z * _c.Z;
        if (Math.Abs(nc) < 1e-9) return double.NaN;
        return (d0 - (n.X * ax + n.Y * ay + n.Z * az)) / nc;
    }

    /// <summary>True when a face with this outward normal faces the viewer.</summary>
    public bool Faces((double X, double Y, double Z) n) => n.X * _c.X + n.Y * _c.Y + n.Z * _c.Z > 1e-9;

    /// <summary>Screen x component of a direction: negative = the face turns to the left (lit side).</summary>
    public double ScreenX((double X, double Y, double Z) n) => Dot(_r, n.X, n.Y, n.Z);

    /// <summary>Page matrix [a, b, c, d] (y down) laying text on a horizontal plane, reading at angle (deg, CCW in plan).</summary>
    public double[] PlaneTextMatrix(double angleDeg)
    {
        var t = angleDeg * Math.PI / 180;
        double ex = Math.Cos(t), ey = Math.Sin(t), ux = -Math.Sin(t), uy = Math.Cos(t);
        return new[]
        {
            Dot(_r, ex, ey, 0), -Dot(_u, ex, ey, 0),
            -Dot(_r, ux, uy, 0), Dot(_u, ux, uy, 0),
        };
    }

    /// <summary>Inverse of <see cref="Project"/> on the horizontal plane at height z.</summary>
    public (double X, double Y) Unproject(double sx, double sy, double z)
    {
        double a = _r.X, b = _r.Y, c = _u.X, d = _u.Y;
        double px = sx - _r.Z * z, py = sy - _u.Z * z, det = a * d - b * c;
        return ((d * px - b * py) / det, (a * py - c * px) / det);
    }

    /// <summary>Screen geometry lying on the horizontal plane at z, back in plan feet.</summary>
    public NtsGeometry Unplane(NtsGeometry screen, double z)
    {
        var g = screen.Copy();
        g.Apply(new PlaneFilter(this, z, inverse: true));
        g.GeometryChanged();
        return g;
    }

    /// <summary>A horizontal polygon (holes kept) at height z, projected; parallel projection keeps it a polygon.</summary>
    public NtsGeometry Plane(NtsGeometry plan, double z)
    {
        var g = plan.Copy();
        g.Apply(new PlaneFilter(this, z));
        g.GeometryChanged();
        return g;
    }

    public NtsGeometry Polygon(IEnumerable<(double X, double Y, double Z)> pts)
    {
        var ring = pts.Select(p => Project(p.X, p.Y, p.Z)).Select(p => new Coordinate(p.X, p.Y)).ToList();
        ring.Add(ring[0]);
        var poly = MeshSlicer.Factory.CreatePolygon(ring.ToArray());
        return poly.IsValid ? poly : poly.Buffer(0);
    }

    sealed class PlaneFilter : ICoordinateSequenceFilter
    {
        readonly AxonProjection _p;
        readonly double _z;
        readonly bool _inverse;
        public PlaneFilter(AxonProjection p, double z, bool inverse = false) { _p = p; _z = z; _inverse = inverse; }
        public void Filter(CoordinateSequence seq, int i)
        {
            var (x, y) = _inverse ? _p.Unproject(seq.GetX(i), seq.GetY(i), _z) : _p.Project(seq.GetX(i), seq.GetY(i), _z);
            seq.SetX(i, x);
            seq.SetY(i, y);
        }
        public bool Done => false;
        public bool GeometryChanged => true;
    }

    static double Dot((double X, double Y, double Z) a, double x, double y, double z) => a.X * x + a.Y * y + a.Z * z;

    static (double X, double Y, double Z) Norm((double X, double Y, double Z) v)
    {
        var l = Math.Sqrt(v.X * v.X + v.Y * v.Y + v.Z * v.Z);
        return l < 1e-12 ? (0, 0, 1) : (v.X / l, v.Y / l, v.Z / l);
    }

    static (double X, double Y, double Z) Cross((double X, double Y, double Z) a, (double X, double Y, double Z) b) =>
        Norm((a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X));
}
