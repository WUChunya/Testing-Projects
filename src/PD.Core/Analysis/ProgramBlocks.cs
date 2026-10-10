using PD.Core.Cleanup;
using NtsGeometry = NetTopologySuite.Geometries.Geometry;

namespace PD.Core.Analysis;

/// <summary>One resolved program footprint extruded over its band: level elevation → next level (or the top of a kept tall space).</summary>
public sealed record ProgramBlock(Footprint Footprint, int LevelIndex, double Z0, double Z1)
{
    public string MassId => Footprint.Mass.Id;
    public string ProgramKey => Footprint.Mass.ProgramKey;
    public NtsGeometry Geometry => Footprint.Resolved;
}

/// <summary>The same blocks the section bars use (resolved plan footprints, span rule), for plans and axons.</summary>
public static class ProgramBlocks
{
    public static List<ProgramBlock> Build(AnalysisResult r)
    {
        var spans = r.Spans.ToDictionary(s => s.MassId);
        var res = new List<ProgramBlock>();
        for (var i = 0; i < r.Plans.Count; i++)
        foreach (var f in r.Plans[i].Footprints.Where(f => !f.OpenToBelow && !f.Resolved.IsEmpty))
        {
            var span = spans.GetValueOrDefault(f.Mass.Id);
            var last = span is { Mode: SpanMode.KeepTall } ? span.LevelIndices[^1] : i;
            var b = f.Mass.Source.BBox;
            var z0 = Math.Max(r.Levels[i].Elevation, b.MinZ);
            var z1 = Math.Min(Sections.SectionGeometry.Top(r, last), b.MaxZ);
            if (z1 > z0 + 0.01) res.Add(new ProgramBlock(f, i, z0, z1));
        }
        return res;
    }
}
