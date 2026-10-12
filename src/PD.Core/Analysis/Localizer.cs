using PD.Core.Config;
using PD.Core.Model;

namespace PD.Core.Analysis;

/// <summary>Model-unit → local-feet transform. XY move to the building bbox min; Z is kept (it is the level datum).</summary>
public sealed record LocalFrame(double FeetPerUnit, double OriginX, double OriginY)
{
    public (double x, double y, double z) ToLocal(double x, double y, double z) =>
        ((x - OriginX) * FeetPerUnit, (y - OriginY) * FeetPerUnit, z * FeetPerUnit);

    public (double x, double y, double z) ToModel(double x, double y, double z) =>
        (x / FeetPerUnit + OriginX, y / FeetPerUnit + OriginY, z / FeetPerUnit);
}

public static class Localizer
{
    /// <summary>
    /// Builds the local frame from program objects, converts every object to local feet and flags
    /// objects far from the building (PRD 3.5 "stray objects").
    /// </summary>
    public static (SourceModel local, LocalFrame frame, BBox3 building, HashSet<string> outliers) Localize(
        SourceModel model, Func<SourceObject, bool> isProgram, CleanupRules rules)
    {
        var k = Units.ToFeet(model.Units);
        var prog = model.Objects.Where(isProgram).ToList();
        if (prog.Count == 0) prog = model.Objects.ToList();
        var building = RobustUnion(prog.Select(o => o.BBox).ToList(), rules, k);
        var frame = new LocalFrame(k, building.MinX, building.MinY);

        var diagFt = Math.Sqrt(Sq(building.MaxX - building.MinX) + Sq(building.MaxY - building.MinY)) * k;
        var limit = Math.Max(rules.OutlierMinDistanceFt, rules.OutlierDistanceFactor * diagFt);
        // Only our own objects are expected near the building; context (roads, blocks, trees) covers the whole site.
        var outliers = model.Objects
            .Where(o => IsOurs(o) && Distance(o.BBox, building) * k > limit)
            .Select(o => o.Id).ToHashSet();

        var objs = model.Objects.Select(o => Transform(o, frame)).ToList();
        var local = new SourceModel { Units = "Feet", Layers = model.Layers, Objects = objs };
        return (local, frame, Transform(building, frame), outliers);
    }

    static bool IsOurs(SourceObject o) => o.Role.Length == 0 || Classification.Roles.IsNew(o.Role);

    static BBox3 RobustUnion(List<BBox3> boxes, CleanupRules rules, double k)
    {
        // Union of boxes whose centre is near the median centre, so one stray program object cannot blow up the frame.
        double Med(IEnumerable<double> v) { var a = v.OrderBy(x => x).ToArray(); return a[a.Length / 2]; }
        var cx = Med(boxes.Select(b => (b.MinX + b.MaxX) / 2));
        var cy = Med(boxes.Select(b => (b.MinY + b.MaxY) / 2));
        var limit = rules.OutlierMinDistanceFt / k;
        var near = boxes.Where(b => Math.Abs((b.MinX + b.MaxX) / 2 - cx) < limit && Math.Abs((b.MinY + b.MaxY) / 2 - cy) < limit).ToList();
        if (near.Count == 0) near = boxes;
        return near.Aggregate((a, b) => a.Union(b));
    }

    static double Sq(double v) => v * v;

    /// <summary>Planar (XY) gap between two boxes; 0 when they overlap.</summary>
    public static double Distance(BBox3 a, BBox3 b)
    {
        var dx = Math.Max(0, Math.Max(a.MinX - b.MaxX, b.MinX - a.MaxX));
        var dy = Math.Max(0, Math.Max(a.MinY - b.MaxY, b.MinY - a.MaxY));
        return Math.Sqrt(dx * dx + dy * dy);
    }

    static BBox3 Transform(BBox3 b, LocalFrame f)
    {
        var (x0, y0, z0) = f.ToLocal(b.MinX, b.MinY, b.MinZ);
        var (x1, y1, z1) = f.ToLocal(b.MaxX, b.MaxY, b.MaxZ);
        return new BBox3(x0, y0, z0, x1, y1, z1);
    }

    static double[] TransformFlat(double[] v, LocalFrame f)
    {
        var r = new double[v.Length];
        for (var i = 0; i + 2 < v.Length; i += 3)
            (r[i], r[i + 1], r[i + 2]) = f.ToLocal(v[i], v[i + 1], v[i + 2]);
        return r;
    }

    static SourceObject Transform(SourceObject o, LocalFrame f) => new()
    {
        Id = o.Id, LayerPath = o.LayerPath, GeometryType = o.GeometryType, Name = o.Name,
        Role = o.Role, ProgramKey = o.ProgramKey, IsClosed = o.IsClosed, WasCapped = o.WasCapped, FaceCount = o.FaceCount,
        Volume = o.Volume * f.FeetPerUnit * f.FeetPerUnit * f.FeetPerUnit,
        BBox = Transform(o.BBox, f),
        Mesh = o.Mesh == null ? null : new MeshData { Vertices = TransformFlat(o.Mesh.Vertices, f), Faces = o.Mesh.Faces },
        Points = o.Points?.Select(p => TransformFlat(p, f)).ToArray(),
    };
}
