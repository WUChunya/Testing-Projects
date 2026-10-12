namespace PD.Core.Model;

/// <summary>Axis-aligned bounding box.</summary>
public readonly record struct BBox3(double MinX, double MinY, double MinZ, double MaxX, double MaxY, double MaxZ)
{
    public double SizeZ => MaxZ - MinZ;
    public BBox3 Union(BBox3 o) => new(Math.Min(MinX, o.MinX), Math.Min(MinY, o.MinY), Math.Min(MinZ, o.MinZ),
        Math.Max(MaxX, o.MaxX), Math.Max(MaxY, o.MaxY), Math.Max(MaxZ, o.MaxZ));
}

/// <summary>Triangle/quad mesh. Vertices are flat xyz triples; faces have 3 or 4 indices.</summary>
public sealed class MeshData
{
    public double[] Vertices { get; init; } = Array.Empty<double>();
    public int[][] Faces { get; init; } = Array.Empty<int[]>();
    public int VertexCount => Vertices.Length / 3;
}

public sealed record LayerInfo(int Index, string Path, bool Visible, string Color)
{
    /// <summary>Last path segment ("BACKGROUND FRAME::GLASS" → "GLASS").</summary>
    public string Name => Path.LastIndexOf("::", StringComparison.Ordinal) is var i and >= 0 ? Path[(i + 2)..] : Path;
}

/// <summary>One Rhino object handed from the adapter (or a fixture) to Core.</summary>
public sealed class SourceObject
{
    public string Id { get; init; } = "";
    public string LayerPath { get; init; } = "";
    public string GeometryType { get; init; } = "";
    public string Name { get; init; } = "";
    public string Role { get; set; } = "";
    public string ProgramKey { get; set; } = "";
    public BBox3 BBox { get; init; }
    public bool IsClosed { get; init; }
    /// <summary>Set by the Rhino adapter when CapPlanarHoles closed the Brep.</summary>
    public bool WasCapped { get; init; }
    public int FaceCount { get; init; }
    public double Volume { get; set; }
    public MeshData? Mesh { get; init; }
    /// <summary>Polyline points (xyz) for curve objects.</summary>
    public double[][]? Points { get; init; }
}

public sealed class SourceModel
{
    /// <summary>Rhino unit system name, e.g. "Millimeters".</summary>
    public string Units { get; init; } = "Feet";
    public IReadOnlyList<LayerInfo> Layers { get; init; } = Array.Empty<LayerInfo>();
    public IReadOnlyList<SourceObject> Objects { get; init; } = Array.Empty<SourceObject>();
}
