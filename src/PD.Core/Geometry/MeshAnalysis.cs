using PD.Core.Model;

namespace PD.Core.Geometry;

public sealed record MeshReport(int NakedLoops, bool LoopsPlanar, double Volume, int Components)
{
    public bool IsClosed => NakedLoops == 0;
}

/// <summary>
/// Welds a mesh, orients it consistently, finds naked boundary loops and computes the volume with
/// every planar boundary loop treated as a cap (mesh-level equivalent of CapPlanarHoles).
/// </summary>
public static class MeshAnalysis
{
    public static MeshReport Analyze(MeshData mesh, double weldTol, double planarTol)
    {
        var welder = new PointWelder(weldTol);
        var map = new int[mesh.VertexCount];
        for (var i = 0; i < mesh.VertexCount; i++)
            map[i] = welder.Add(new V3(mesh.Vertices[3 * i], mesh.Vertices[3 * i + 1], mesh.Vertices[3 * i + 2]));
        var pts = welder.Points;

        var tris = new List<int[]>();
        foreach (var f in mesh.Faces)
        {
            AddTri(tris, map[f[0]], map[f[1]], map[f[2]]);
            if (f.Length > 3) AddTri(tris, map[f[0]], map[f[2]], map[f[3]]);
        }

        var edges = new Dictionary<(int, int), List<int>>();
        for (var t = 0; t < tris.Count; t++)
            for (var e = 0; e < 3; e++)
            {
                var key = Key(tris[t][e], tris[t][(e + 1) % 3]);
                if (!edges.TryGetValue(key, out var l)) edges[key] = l = new List<int>();
                l.Add(t);
            }

        var comp = Orient(tris, edges);
        var compCount = comp.Length == 0 ? 0 : comp.Max() + 1;
        var vol = new double[compCount];
        foreach (var (t, i) in tris.Select((t, i) => (t, i)))
            vol[comp[i]] += Det(pts[t[0]], pts[t[1]], pts[t[2]]) / 6.0;

        // Naked edges: undirected count 1. Cap loops run opposite to the owning triangle's edge.
        var next = new Dictionary<int, (int to, int comp)>();
        foreach (var (key, owners) in edges)
        {
            if (owners.Count != 1) continue;
            var t = tris[owners[0]];
            for (var e = 0; e < 3; e++)
            {
                int a = t[e], b = t[(e + 1) % 3];
                if (Key(a, b) == key) next[b] = (a, comp[owners[0]]);
            }
        }

        var loops = 0;
        var planar = true;
        while (next.Count > 0)
        {
            var start = next.Keys.First();
            var loop = new List<int> { start };
            var c = next[start].comp;
            var cur = start;
            while (next.Remove(cur, out var step))
            {
                cur = step.to;
                if (cur == start) break;
                loop.Add(cur);
            }
            if (loop.Count < 3) continue;
            loops++;
            planar &= IsPlanar(loop.Select(i => pts[i]).ToList(), planarTol);
            for (var i = 1; i + 1 < loop.Count; i++)
                vol[c] += Det(pts[loop[0]], pts[loop[i]], pts[loop[i + 1]]) / 6.0;
        }

        return new MeshReport(loops, planar, vol.Sum(Math.Abs), compCount);
    }

    static void AddTri(List<int[]> tris, int a, int b, int c)
    {
        if (a != b && b != c && a != c) tris.Add(new[] { a, b, c });
    }

    static (int, int) Key(int a, int b) => a < b ? (a, b) : (b, a);

    static double Det(V3 a, V3 b, V3 c) => V3.Dot(a, V3.Cross(b, c));

    /// <summary>Flood-fills components across manifold edges, flipping triangles to a consistent winding.</summary>
    static int[] Orient(List<int[]> tris, Dictionary<(int, int), List<int>> edges)
    {
        var comp = Enumerable.Repeat(-1, tris.Count).ToArray();
        var id = 0;
        for (var s = 0; s < tris.Count; s++)
        {
            if (comp[s] >= 0) continue;
            var queue = new Queue<int>();
            queue.Enqueue(s);
            comp[s] = id;
            while (queue.Count > 0)
            {
                var t = queue.Dequeue();
                for (var e = 0; e < 3; e++)
                {
                    int a = tris[t][e], b = tris[t][(e + 1) % 3];
                    var owners = edges[Key(a, b)];
                    if (owners.Count != 2) continue;
                    var n = owners[0] == t ? owners[1] : owners[0];
                    if (comp[n] >= 0) continue;
                    if (HasDirected(tris[n], a, b)) (tris[n][1], tris[n][2]) = (tris[n][2], tris[n][1]);
                    comp[n] = id;
                    queue.Enqueue(n);
                }
            }
            id++;
        }
        return comp;
    }

    static bool HasDirected(int[] t, int a, int b) =>
        (t[0] == a && t[1] == b) || (t[1] == a && t[2] == b) || (t[2] == a && t[0] == b);

    static bool IsPlanar(List<V3> loop, double tol)
    {
        var n = new V3(0, 0, 0);
        for (var i = 0; i < loop.Count; i++)
        {
            V3 p = loop[i], q = loop[(i + 1) % loop.Count];
            n += new V3((p.Y - q.Y) * (p.Z + q.Z), (p.Z - q.Z) * (p.X + q.X), (p.X - q.X) * (p.Y + q.Y));
        }
        var len = n.Length;
        if (len < 1e-12) return false;
        n *= 1.0 / len;
        var c = loop.Aggregate(new V3(0, 0, 0), (acc, p) => acc + p) * (1.0 / loop.Count);
        return loop.All(p => Math.Abs(V3.Dot(p - c, n)) <= tol);
    }
}
