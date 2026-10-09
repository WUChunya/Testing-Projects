using PD.Core.Config;
using PD.Core.Geometry;
using PD.Core.Model;

namespace PD.Core.Cleanup;

public enum MassStatus { Closed, Capped, SectionClosed, SingleSurface, Open }

public enum SingleSurfaceChoice { Ask, Ignore, Facade, Extrude }

public sealed record Mass(SourceObject Source, MassStatus Status, double Volume)
{
    public string Id => Source.Id;
    public string ProgramKey => Source.ProgramKey;
    public bool IsSolid => Status is MassStatus.Closed or MassStatus.Capped or MassStatus.SectionClosed;
}

/// <summary>PRD 3.5 "非封闭体块 / 单张曲面": closed, cappable (planar holes), single surface, or open.</summary>
public static class MassClassifier
{
    public static Mass Classify(SourceObject o, CleanupRules r)
    {
        if (o.Mesh == null || o.Mesh.Faces.Length == 0) return new Mass(o, MassStatus.Open, 0);
        var rep = MeshAnalysis.Analyze(o.Mesh, r.WeldToleranceFt, r.CapPlanarToleranceFt);
        var solid = rep.Volume >= r.MinSolidVolumeCf;
        if (o.FaceCount == 1 && !(o.IsClosed && solid)) return new Mass(o, MassStatus.SingleSurface, 0);
        if (solid && (o.IsClosed || rep.IsClosed)) return new Mass(o, MassStatus.Closed, rep.Volume);
        if (solid && rep.LoopsPlanar) return new Mass(o, MassStatus.Capped, rep.Volume);
        var sectionVolume = SectionCloseVolume(o, r);
        if (sectionVolume >= r.MinSolidVolumeCf) return new Mass(o, MassStatus.SectionClosed, sectionVolume);
        return new Mass(o, rep.Volume < r.MinSolidVolumeCf && sectionVolume <= 0 ? MassStatus.SingleSurface : MassStatus.Open, 0);
    }

    /// <summary>
    /// PRD 3.5 step ②: when every test slice gives exactly one open chain (a wall set missing a side),
    /// close it along the section line. Volume ≈ mid-height area × height; 0 when not applicable.
    /// </summary>
    static double SectionCloseVolume(SourceObject o, CleanupRules r)
    {
        var b = o.BBox;
        if (b.SizeZ < 1) return 0;
        double mid = 0;
        foreach (var t in new[] { 0.25, 0.5, 0.75 })
        {
            var loops = MeshSlicer.Slice(o.Mesh!, b.MinZ + t * b.SizeZ, r.WeldToleranceFt, r.GapCloseToleranceFt, true, out var open);
            if (open != 1 || loops.Count != 1) return 0;
            var area = MeshSlicer.ToPolygon(loops).Area;
            if (area < 1) return 0;
            if (t == 0.5) mid = area;
        }
        return mid * b.SizeZ;
    }
}
