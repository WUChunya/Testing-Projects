using NetTopologySuite.Geometries;
using NetTopologySuite.Operation.Union;
using PD.Core.Model;
using NtsGeometry = NetTopologySuite.Geometries.Geometry;

namespace PD.Core.Geometry;

/// <summary>
/// Vertical cutting plane through a plan line A→B (local feet). Section coordinates: u along the line from A,
/// z elevation, w signed distance towards the viewing side (positive = in front of the viewer, i.e. "beyond").
/// </summary>
public sealed class SectionPlane
{
    public double Ax { get; }
    public double Ay { get; }
    public double Dx { get; }
    public double Dy { get; }
    public double Nx { get; }
    public double Ny { get; }
    public double Length { get; }

    public SectionPlane(double ax, double ay, double bx, double by, bool lookLeft)
    {
        Ax = ax; Ay = ay;
        Length = Math.Sqrt((bx - ax) * (bx - ax) + (by - ay) * (by - ay));
        if (Length < 1e-9) throw new ArgumentException("Section line has zero length");
        Dx = (bx - ax) / Length; Dy = (by - ay) / Length;
        (Nx, Ny) = lookLeft ? (-Dy, Dx) : (Dy, -Dx);
    }

    public double U(double x, double y) => (x - Ax) * Dx + (y - Ay) * Dy;
    public double W(double x, double y) => (x - Ax) * Nx + (y - Ay) * Ny;

    public LineString PlanLine() => MeshSlicer.Factory.CreateLineString(new[]
    {
        new Coordinate(Ax, Ay), new Coordinate(Ax + Dx * Length, Ay + Dy * Length),
    });

    /// <summary>Mesh re-expressed as (u, z, w) so the horizontal slicer at 0 cuts along the plane.</summary>
    public MeshData ToSectionSpace(MeshData m)
    {
        var v = new double[m.Vertices.Length];
        for (var i = 0; i + 2 < v.Length; i += 3)
        {
            double x = m.Vertices[i], y = m.Vertices[i + 1];
            v[i] = U(x, y); v[i + 1] = m.Vertices[i + 2]; v[i + 2] = W(x, y);
        }
        return new MeshData { Vertices = v, Faces = m.Faces };
    }

    /// <summary>Cut loops in (u, z).</summary>
    public List<List<(double X, double Y)>> Cut(MeshData m, double weldTol, double gapTol, List<List<(double X, double Y)>>? open = null,
        bool closeOpen = false) =>
        MeshSlicer.Slice(ToSectionSpace(m), 0, weldTol, gapTol, closeOpen, out _, open);

    /// <summary>Nearest and farthest signed distance of the mesh from the plane (positive = in the viewing direction).</summary>
    public (double Min, double Max) WRange(MeshData m)
    {
        double lo = double.MaxValue, hi = double.MinValue;
        for (var i = 0; i + 2 < m.Vertices.Length; i += 3)
        {
            var w = W(m.Vertices[i], m.Vertices[i + 1]);
            lo = Math.Min(lo, w); hi = Math.Max(hi, w);
        }
        return (lo, hi);
    }

    /// <summary>True when the mesh overlaps the line's extent along u.</summary>
    public bool UOverlaps(MeshData m)
    {
        double lo = double.MaxValue, hi = double.MinValue;
        for (var i = 0; i + 2 < m.Vertices.Length; i += 3)
        {
            var u = U(m.Vertices[i], m.Vertices[i + 1]);
            lo = Math.Min(lo, u); hi = Math.Max(hi, u);
        }
        return hi > 0 && lo < Length;
    }

    /// <summary>Union of the mesh's faces projected onto the plane, in (u, z).</summary>
    public NtsGeometry Projection(MeshData m)
    {
        var polys = new List<NtsGeometry>();
        var v = m.Vertices;
        foreach (var f in m.Faces)
        {
            var ring = f.Select(i => new Coordinate(U(v[3 * i], v[3 * i + 1]), v[3 * i + 2])).ToList();
            ring.Add(ring[0]);
            var p = MeshSlicer.Factory.CreatePolygon(ring.ToArray());
            if (Math.Abs(p.Area) > 1e-6) polys.Add(p.IsValid ? p : p.Buffer(0));
        }
        return polys.Count == 0 ? MeshSlicer.Factory.CreatePolygon() : CascadedPolygonUnion.Union(polys);
    }
}
