namespace PD.Core.Analysis;

/// <summary>PRD B2 uncertain items (tab ⑧).</summary>
public enum IssueKind
{
    OpenMass, RepairedMass, SingleSurface, Overlap, FacadeCandidate, StrayObject, UnclassifiedLayer,
    UnnamedMass, Mezzanine, LevelUnsupported, LevelScattered, LevelSingleSource, LevelNoSlab,
    SectionMissesMasses, ProgramNoColor,
}

public sealed record Issue(IssueKind Kind, string Message, string[] ObjectIds, double? Elevation = null)
{
    /// <summary>Stable key so "ignore" choices survive re-analysis.</summary>
    public string Key => $"{Kind}:{string.Join(",", ObjectIds.OrderBy(i => i).Take(4))}:{Elevation:0.00}";
}
