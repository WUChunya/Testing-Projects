using NetTopologySuite.Geometries;
using NtsGeometry = NetTopologySuite.Geometries.Geometry;
using PD.Core.Model;

namespace PD.Core.Geometry;

/// <summary>Cuts a mesh with a horizontal plane and returns closed XY loops (gaps closed within tolerance).</summary>
public static class MeshSlicer
{
    public static readonly GeometryFactory Factory = new();

    /// <summary>
    /// Only the polygonal parts of a geometry. An overlay (Intersection / Difference) can return a
    /// GeometryCollection when the result also has line or point pieces, and NTS refuses such a collection as an
    /// argument to the next overlay ("Operation does not support GeometryCollection arguments"), so every result
    /// that stands for an area goes through here.
    /// </summary>
    public static NtsGeometry Polygonal(NtsGeometry? g) => Keep(g, Dimension.Surface);

    /// <summary>Only the lineal parts of a geometry (clipped property lines, roads).</summary>
    public static NtsGeometry Lineal(NtsGeometry? g) => Keep(g, Dimension.Curve);

    static NtsGeometry Keep(NtsGeometry? g, Dimension dim)
    {
        if (g == null || g.IsEmpty) return Factory.CreatePolygon();
        if (g.Dimension == dim && g is not GeometryCollection { OgcGeometryType: OgcGeometryType.GeometryCollection }) return g;
        var parts = new List<NtsGeometry>();
        Collect(g, dim, parts);
        return parts.Count switch
        {
            0 => Factory.CreatePolygon(),
            1 => parts[0],
            _ => Factory.BuildGeometry(parts),
        };
    }

    static void Collect(NtsGeometry g, Dimension dim, List<NtsGeometry> parts)
    {
        if (g.IsEmpty) return;
        if (g is GeometryCollection { OgcGeometryType: OgcGeometryType.GeometryCollection } gc)
        {
            for (var i = 0; i < gc.NumGeometries; i++) Collect(gc.GetGeometryN(i), dim, parts);
            return;
        }
        if (g.Dimension == dim) parts.Add(g);
    }

    public static List<List<(double X, double Y)>> Slice(MeshData mesh, double z, double weldTol, double gapTol, bool closeOpen = false) =>
        Slice(mesh, z, weldTol, gapTol, closeOpen, out _);

    /// <param name="closeOpen">Close chains that stay open with a straight segment (PRD 3.5 "close along the cut").</param>
    /// <param name="openChains">Number of chains that could not be closed within the gap tolerance.</param>
    /// <param name="openOut">When given, receives the chains that stayed open (e.g. a cut through a single surface).</param>
    public static List<List<(double X, double Y)>> Slice(MeshData mesh, double z, double weldTol, double gapTol, bool closeOpen, out int openChains,
        List<List<(double X, double Y)>>? openOut = null)
    {
        var segs = new List<(V3 a, V3 b)>();
        var v = mesh.Vertices;
        V3 P(int i) => new(v[3 * i], v[3 * i + 1], v[3 * i + 2]);
        foreach (var f in mesh.Faces)
        {
            Cut(P(f[0]), P(f[1]), P(f[2]), z, segs);
            if (f.Length > 3) Cut(P(f[0]), P(f[2]), P(f[3]), z, segs);
        }
        return Chain(segs, weldTol, gapTol, closeOpen, out openChains, openOut);
    }

    /// <summary>Slices and converts to an even-odd polygon (holes from nested loops). Empty when nothing closes.</summary>
    public static NtsGeometry SliceToPolygon(MeshData mesh, double z, double weldTol, double gapTol, bool closeOpen = false) =>
        ToPolygon(Slice(mesh, z, weldTol, gapTol, closeOpen));

    public static NtsGeometry ToPolygon(IEnumerable<List<(double X, double Y)>> loops)
    {
        NtsGeometry acc = Factory.CreatePolygon();
        foreach (var loop in loops)
        {
            var coords = loop.Select(p => new Coordinate(p.X, p.Y)).Append(new Coordinate(loop[0].X, loop[0].Y)).ToArray();
            if (coords.Length < 4) continue;
            var poly = Factory.CreatePolygon(coords).Buffer(0);
            if (poly.Area < 1e-6) continue;
            acc = acc.IsEmpty ? poly : acc.SymmetricDifference(poly);
        }
        return acc.Buffer(0);
    }

    static void Cut(V3 a, V3 b, V3 c, double z, List<(V3, V3)> segs)
    {
        double da = D(a.Z - z), db = D(b.Z - z), dc = D(c.Z - z);
        var pts = new List<V3>(2);
        Edge(a, b, da, db, pts);
        Edge(b, c, db, dc, pts);
        Edge(c, a, dc, da, pts);
        if (pts.Count == 2 && (pts[0] - pts[1]).Length > 1e-9) segs.Add((pts[0], pts[1]));
    }

    // Vertices exactly on the plane count as "above" so every crossing is a clean sign change.
    static double D(double d) => Math.Abs(d) < 1e-9 ? 1e-9 : d;

    static void Edge(V3 p, V3 q, double dp, double dq, List<V3> pts)
    {
        if (dp > 0 == dq > 0) return;
        var t = dp / (dp - dq);
        pts.Add(p + (q - p) * t);
    }

    static List<List<(double, double)>> Chain(List<(V3 a, V3 b)> segs, double weldTol, double gapTol, bool closeOpen, out int openChains,
        List<List<(double, double)>>? openOut)
    {
        var welder = new PointWelder(weldTol);
        var adj = new Dictionary<int, List<int>>();
        var ends = new List<(int, int)>();
        foreach (var (a, b) in segs)
        {
            int i = welder.Add(a with { Z = 0 }), j = welder.Add(b with { Z = 0 });
            if (i == j) continue;
            ends.Add((i, j));
            Link(adj, i, ends.Count - 1);
            Link(adj, j, ends.Count - 1);
        }

        var used = new bool[ends.Count];
        var chains = new List<List<int>>();
        // Start from dangling ends first so open chains are walked end to end.
        var starts = adj.Where(kv => kv.Value.Count == 1).Select(kv => kv.Key).Concat(adj.Keys);
        foreach (var s in starts)
        {
            var seg = adj[s].FirstOrDefault(e => !used[e], -1);
            if (seg < 0) continue;
            var chain = new List<int> { s };
            var cur = s;
            while (seg >= 0)
            {
                used[seg] = true;
                cur = ends[seg].Item1 == cur ? ends[seg].Item2 : ends[seg].Item1;
                chain.Add(cur);
                seg = adj[cur].FirstOrDefault(e => !used[e], -1);
            }
            chains.Add(chain);
        }

        var pts = welder.Points;
        var loops = new List<List<int>>();
        var open = new List<List<int>>();
        foreach (var c in chains)
            (c[0] == c[^1] ? loops : open).Add(c);
        openChains = CloseGaps(open, loops, pts, gapTol, closeOpen, openOut == null ? null : c => openOut.Add(c.Select(i => (pts[i].X, pts[i].Y)).ToList()));

        return loops.Where(l => l.Count >= 4)
            .Select(l => l.Take(l.Count - 1).Select(i => (pts[i].X, pts[i].Y)).ToList()).ToList();
    }

    /// <summary>Greedily joins open chains end-to-start (including to themselves) when the gap is within tolerance.</summary>
    static int CloseGaps(List<List<int>> open, List<List<int>> loops, List<V3> pts, double gapTol, bool closeOpen, Action<List<int>>? onOpen)
    {
        var stillOpen = 0;
        while (open.Count > 0)
        {
            var c = open[0];
            open.RemoveAt(0);
            while (true)
            {
                var end = pts[c[^1]];
                if ((pts[c[0]] - end).Length <= gapTol) { c.Add(c[0]); loops.Add(c); break; }
                int best = -1; bool reverse = false; double bestD = gapTol;
                for (var k = 0; k < open.Count; k++)
                {
                    var d0 = (pts[open[k][0]] - end).Length;
                    var d1 = (pts[open[k][^1]] - end).Length;
                    if (d0 <= bestD) { best = k; reverse = false; bestD = d0; }
                    if (d1 <= bestD) { best = k; reverse = true; bestD = d1; }
                }
                if (best < 0)
                {
                    // Stays open: not a region (e.g. a single vertical sheet) unless forced closed.
                    stillOpen++;
                    onOpen?.Invoke(c);
                    if (closeOpen && c.Count >= 3) { c.Add(c[0]); loops.Add(c); }
                    break;
                }
                var other = open[best];
                open.RemoveAt(best);
                if (reverse) other.Reverse();
                c.AddRange(other);
            }
        }
        return stillOpen;
    }

    static void Link(Dictionary<int, List<int>> adj, int v, int seg)
    {
        if (!adj.TryGetValue(v, out var l)) adj[v] = l = new List<int>();
        l.Add(seg);
    }
}
