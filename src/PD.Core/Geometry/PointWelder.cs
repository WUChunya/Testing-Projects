namespace PD.Core.Geometry;

/// <summary>Merges points closer than a tolerance using a hashed grid (checks neighbouring cells).</summary>
public sealed class PointWelder
{
    readonly double _tol;
    readonly Dictionary<(long, long, long), List<int>> _grid = new();
    public List<V3> Points { get; } = new();

    public PointWelder(double tolerance) => _tol = tolerance;

    public int Add(V3 p)
    {
        var c = Cell(p);
        for (var dx = -1; dx <= 1; dx++)
        for (var dy = -1; dy <= 1; dy++)
        for (var dz = -1; dz <= 1; dz++)
        {
            if (!_grid.TryGetValue((c.Item1 + dx, c.Item2 + dy, c.Item3 + dz), out var list)) continue;
            foreach (var i in list)
                if ((Points[i] - p).Length <= _tol) return i;
        }
        Points.Add(p);
        if (!_grid.TryGetValue(c, out var l)) _grid[c] = l = new List<int>();
        l.Add(Points.Count - 1);
        return Points.Count - 1;
    }

    (long, long, long) Cell(V3 p) =>
        ((long)Math.Floor(p.X / _tol), (long)Math.Floor(p.Y / _tol), (long)Math.Floor(p.Z / _tol));
}
