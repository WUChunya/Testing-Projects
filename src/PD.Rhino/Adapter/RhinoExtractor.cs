using Rhino;
using Rhino.DocObjects;
using Rhino.Geometry;
using PD.Core.Cleanup;
using PD.Core.Model;

namespace PD.Rhino.Adapter;

/// <summary>Result of reading the document: Core DTOs plus the map back to Rhino object ids.</summary>
public sealed class Extraction
{
    public required SourceModel Model { get; init; }
    /// <summary>DTO id → Rhino object id (block pieces map to their instance).</summary>
    public required Dictionary<string, Guid> RhinoIds { get; init; }
    public required Dictionary<string, int> ObjectCountByLayer { get; init; }
}

/// <summary>Rhino → Core boundary (PRD 6): layers, exploded blocks, 3D cleanup (CapPlanarHoles), render meshes.</summary>
public static class RhinoExtractor
{
    const int CurveSamples = 64;

    public static Extraction Extract(RhinoDoc doc, IReadOnlyDictionary<string, SingleSurfaceChoice> singleSurfaces, double extrudeFt)
    {
        var layers = doc.Layers.Where(l => !l.IsDeleted)
            .Select(l => new LayerInfo(l.Index, l.FullPath, l.IsVisible, $"#{l.Color.R:X2}{l.Color.G:X2}{l.Color.B:X2}"))
            .ToList();
        var layerPath = layers.ToDictionary(l => l.Index, l => l.Path);
        var tol = doc.ModelAbsoluteTolerance;
        var extrudeModel = extrudeFt / Units.ToFeet(doc.ModelUnitSystem.ToString());

        var objects = new List<SourceObject>();
        var ids = new Dictionary<string, Guid>();
        var settings = new ObjectEnumeratorSettings
        {
            NormalObjects = true, LockedObjects = true, HiddenObjects = false, DeletedObjects = false,
            IncludeLights = false, IncludeGrips = false, IncludePhantoms = false,
        };
        foreach (var obj in doc.Objects.GetObjectList(settings))
        {
            foreach (var (key, geom, layerIndex, name) in Flatten(obj))
            {
                if (!layerPath.TryGetValue(layerIndex, out var path)) continue;
                var choice = singleSurfaces.GetValueOrDefault(key);
                var dto = ToDto(key, geom, path, name, tol, choice == SingleSurfaceChoice.Extrude ? extrudeModel : 0);
                if (dto == null) continue;
                objects.Add(dto);
                ids[key] = obj.Id;
            }
        }

        return new Extraction
        {
            Model = new SourceModel { Units = doc.ModelUnitSystem.ToString(), Layers = layers, Objects = objects },
            RhinoIds = ids,
            ObjectCountByLayer = objects.GroupBy(o => o.LayerPath).ToDictionary(g => g.Key, g => g.Count()),
        };
    }

    /// <summary>Explodes block instances (nested transforms applied); pieces keep their own layer.</summary>
    static IEnumerable<(string key, GeometryBase geom, int layer, string name)> Flatten(RhinoObject obj)
    {
        if (obj is InstanceObject inst)
        {
            inst.Explode(true, out var pieces, out var attrs, out var xforms);
            for (var i = 0; i < pieces.Length; i++)
            {
                var g = pieces[i].Geometry.Duplicate();
                g.Transform(xforms[i]);
                var name = string.IsNullOrEmpty(attrs[i].Name) ? inst.Attributes.Name ?? "" : attrs[i].Name;
                yield return ($"{obj.Id}#{i}", g, attrs[i].LayerIndex, name);
            }
            yield break;
        }
        yield return (obj.Id.ToString(), obj.Geometry, obj.Attributes.LayerIndex, obj.Attributes.Name ?? "");
    }

    static SourceObject? ToDto(string id, GeometryBase g, string layer, string name, double tol, double extrude)
    {
        var bb = g.GetBoundingBox(true);
        if (!bb.IsValid) return null;
        if (g is Curve c)
            return Base(id, g, layer, name, bb, c.IsClosed, 0, null, CurvePoints(c));
        if (g is Mesh m)
            return Base(id, g, layer, name, bb, m.IsClosed, m.Faces.Count, ToMeshData(m));

        var brep = g switch
        {
            Brep b => b,
            Extrusion e => e.ToBrep(true),
            Surface s => s.ToBrep(),
            SubD d => d.ToBrep(SubDToBrepOptions.Default),
            _ => null,
        };
        if (brep == null) return null;
        var faces = brep.Faces.Count;
        var capped = false;
        if (!brep.IsSolid && faces > 1)
        {
            var cap = brep.CapPlanarHoles(tol);
            if (cap != null && cap.IsSolid) { brep = cap; capped = true; }
        }
        else if (faces == 1 && extrude > 0)
        {
            // PRD 3.5 single surface → user chose "extrude by depth".
            var solid = Brep.CreateFromOffsetFace(brep.Faces[0], extrude, tol, false, true);
            if (solid != null && solid.IsSolid) { brep = solid; faces = brep.Faces.Count; }
        }
        var meshes = Mesh.CreateFromBrep(brep, MeshingParameters.FastRenderMesh);
        var mesh = new Mesh();
        if (meshes != null) mesh.Append(meshes);
        return Base(id, g, layer, name, brep.GetBoundingBox(true), brep.IsSolid, faces, ToMeshData(mesh), null, capped);
    }

    static SourceObject Base(string id, GeometryBase g, string layer, string name, BoundingBox bb, bool closed, int faces,
        MeshData? mesh, double[][]? points = null, bool capped = false) => new()
    {
        Id = id, LayerPath = layer, Name = name, GeometryType = g.ObjectType.ToString(),
        BBox = new BBox3(bb.Min.X, bb.Min.Y, bb.Min.Z, bb.Max.X, bb.Max.Y, bb.Max.Z),
        IsClosed = closed, WasCapped = capped, FaceCount = faces, Mesh = mesh, Points = points,
    };

    static MeshData ToMeshData(Mesh m)
    {
        var v = new double[m.Vertices.Count * 3];
        for (var i = 0; i < m.Vertices.Count; i++)
        {
            var p = m.Vertices.Point3dAt(i);
            v[3 * i] = p.X; v[3 * i + 1] = p.Y; v[3 * i + 2] = p.Z;
        }
        var f = new int[m.Faces.Count][];
        for (var i = 0; i < m.Faces.Count; i++)
        {
            var face = m.Faces[i];
            f[i] = face.IsQuad ? new[] { face.A, face.B, face.C, face.D } : new[] { face.A, face.B, face.C };
        }
        return new MeshData { Vertices = v, Faces = f };
    }

    static double[][] CurvePoints(Curve c)
    {
        if (c.TryGetPolyline(out var pl))
            return pl.Select(p => new[] { p.X, p.Y, p.Z }).ToArray();
        var ts = c.DivideByCount(CurveSamples, true) ?? new[] { c.Domain.T0, c.Domain.T1 };
        return ts.Select(t => c.PointAt(t)).Select(p => new[] { p.X, p.Y, p.Z }).ToArray();
    }
}
