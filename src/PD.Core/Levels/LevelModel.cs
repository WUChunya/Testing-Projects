namespace PD.Core.Levels;

[Flags]
public enum LevelSource { None = 0, Slab = 1, Mass = 2, Facade = 4 }

public enum CandidateKind { Slab, MassBottom, FacadeBottom }

public sealed record LevelCandidate(double Elevation, CandidateKind Kind, string[] ObjectIds, double Area = 0);

public sealed class Level
{
    public string Name { get; set; } = "";
    public double Elevation { get; set; }
    public LevelSource Sources { get; set; }
    public bool IsMezzanine { get; set; }
    public bool IsRoof { get; set; }
    /// <summary>Lowest and highest supporting elevation (slab tops, mass and facade bottoms).</summary>
    public double SpreadMin { get; set; }
    public double SpreadMax { get; set; }
    public double Spread => SpreadMax - SpreadMin;
    public List<LevelCandidate> Members { get; } = new();
}

public enum LevelIssueKind { Unsupported, Scattered, SingleSource, NoSlab, Mezzanine }

public sealed record LevelIssue(LevelIssueKind Kind, double Elevation, string Message, string[] ObjectIds);

public sealed record LevelResult(List<Level> Levels, List<LevelIssue> Issues);
