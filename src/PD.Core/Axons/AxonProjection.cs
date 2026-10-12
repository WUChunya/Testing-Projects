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

    public static AxonProjection FromAngles(double azimuthDeg, double elevationDeg, double[]? viewDirection = null)
    {
        if (viewDirection is { Length: 3 } v) return new AxonProjection((-v[0], -v[1], -v[2]));
        return new AxonProjection(FromAngles(azimuthDeg, elevationDeg));
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
