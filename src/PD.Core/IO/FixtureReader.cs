using System.Text.Json;
using PD.Core.Model;

namespace PD.Core.IO;

/// <summary>Reads JSON written by tools/fixture_from_3dm.py into a <see cref="SourceModel"/> (model units, unconverted).</summary>
public static class FixtureReader
{
    public static SourceModel Load(string path)
    {
        using var stream = File.OpenRead(path);
        using var doc = JsonDocument.Parse(stream);
        var root = doc.RootElement;
        var layers = root.GetProperty("layers").EnumerateArray()
            .Select(l => new LayerInfo(l.GetProperty("index").GetInt32(), l.GetProperty("path").GetString()!,
                l.GetProperty("visible").GetBoolean(), l.GetProperty("color").GetString()!))
            .ToList();
        var objects = root.GetProperty("objects").EnumerateArray().Select(ReadObject).ToList();
        return new SourceModel { Units = root.GetProperty("units").GetString()!, Layers = layers, Objects = objects };
    }

    static SourceObject ReadObject(JsonElement o)
    {
        var b = o.GetProperty("bbox").EnumerateArray().Select(e => e.GetDouble()).ToArray();
        return new SourceObject
        {
            Id = o.GetProperty("id").GetString()!,
            LayerPath = o.GetProperty("layer").GetString()!,
            GeometryType = o.GetProperty("type").GetString()!,
            Name = o.GetProperty("name").GetString()!,
            BBox = new BBox3(b[0], b[1], b[2], b[3], b[4], b[5]),
            IsClosed = o.GetProperty("isClosed").GetBoolean(),
            FaceCount = o.GetProperty("faceCount").GetInt32(),
            Mesh = o.TryGetProperty("mesh", out var m) && m.ValueKind == JsonValueKind.Object ? ReadMesh(m) : null,
            Points = o.TryGetProperty("points", out var p)
                ? p.EnumerateArray().Select(pt => pt.EnumerateArray().Select(e => e.GetDouble()).ToArray()).ToArray()
                : null,
        };
    }

    static MeshData ReadMesh(JsonElement m) => new()
    {
        Vertices = m.GetProperty("v").EnumerateArray().Select(e => e.GetDouble()).ToArray(),
        Faces = m.GetProperty("f").EnumerateArray().Select(f => f.EnumerateArray().Select(e => e.GetInt32()).ToArray()).ToArray(),
    };
}
