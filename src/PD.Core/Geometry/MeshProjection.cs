using NetTopologySuite.Geometries;
using NetTopologySuite.Operation.Union;
using PD.Core.Model;
using NtsGeometry = NetTopologySuite.Geometries.Geometry;

namespace PD.Core.Geometry;

/// <summary>Silhouette of a mesh under any parallel projection: union of its projected faces.</summary>
public static class MeshProjection
{
    /// <param name="project">3D (x, y, z) → 2D.</param>
    /// <param name="keepFace">Optional face filter on the face normal (e.g. front faces only).</param>
    public static NtsGeometry Union(MeshData m, Func<double, double, double, (double X, double Y)> project, Func<(double X, double Y, double Z), bool>? keepFace = null)
    {
        var polys = new List<NtsGeometry>();
        var v = m.Vertices;
        foreach (var f in m.Faces)
        {
            if (f.Length < 3) continue;
            if (keepFace != null && !keepFace(Normal(v, f))) continue;
            var ring = f.Select(i => project(v[3 * i], v[3 * i + 1], v[3 * i + 2])).Select(p => new Coordinate(p.X, p.Y)).ToList();
            ring.Add(ring[0]);
            var p = MeshSlicer.Factory.CreatePolygon(ring.ToArray());
            if (Math.Abs(p.Area) > 1e-6) polys.Add(p.IsValid ? p : p.Buffer(0));
        }
        return polys.Count == 0 ? MeshSlicer.Factory.CreatePolygon() : CascadedPolygonUnion.Union(polys);
    }

    public static NtsGeometry TopDown(MeshData m) => Union(m, (x, y, _) => (x, y));

    static (double X, double Y, double Z) Normal(double[] v, int[] f)
    {
        double X(int k) => v[3 * f[k]];
        double Y(int k) => v[3 * f[k] + 1];
        double Z(int k) => v[3 * f[k] + 2];
        double ax = X(1) - X(0), ay = Y(1) - Y(0), az = Z(1) - Z(0);
        double bx = X(2) - X(0), by = Y(2) - Y(0), bz = Z(2) - Z(0);
        return (ay * bz - az * by, az * bx - ax * bz, ax * by - ay * bx);
    }
}
