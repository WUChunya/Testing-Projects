using PD.Core.Classification;
using PD.Core.Config;
using PD.Core.Model;

namespace PD.Core.Analysis;

/// <summary>
/// PRD A3 geometric facade test for layers not already marked as facade: thin vertical sheets near the
/// building outline whose bottoms repeat per floor. One issue per layer.
/// </summary>
public static class FacadeDetector
{
    public static IEnumerable<Issue> Candidates(IEnumerable<SourceObject> objects, BBox3 building, Rules rules)
    {
        var f = rules.Facade;
        foreach (var g in objects.Where(o => o.Role is not (Roles.Program or Roles.Facade or Roles.Ignore) && o.Points == null)
                     .GroupBy(o => o.LayerPath))
        {
            var thin = g.Where(o =>
            {
                var b = o.BBox;
                var dx = b.MaxX - b.MinX; var dy = b.MaxY - b.MinY;
                if (Math.Min(dx, dy) > f.ThinSheetMaxFt || b.SizeZ < 4) return false;
                var inside = b.MinX >= building.MinX - f.OutlineDistanceFt && b.MaxX <= building.MaxX + f.OutlineDistanceFt
                    && b.MinY >= building.MinY - f.OutlineDistanceFt && b.MaxY <= building.MaxY + f.OutlineDistanceFt;
                var nearEdge = Math.Min(Math.Min(Math.Abs(b.MinX - building.MinX), Math.Abs(b.MaxX - building.MaxX)),
                    Math.Min(Math.Abs(b.MinY - building.MinY), Math.Abs(b.MaxY - building.MaxY))) <= f.OutlineDistanceFt;
                return inside && nearEdge;
            }).ToList();
            var floors = thin.Select(o => Math.Round(o.BBox.MinZ)).Distinct().Count();
            if (thin.Count >= rules.Levels.FacadeMinRepeat && floors >= rules.Levels.FacadeMinRepeat)
                yield return new Issue(IssueKind.FacadeCandidate,
                    $"Layer '{g.Key}': {thin.Count} thin sheets on the building outline repeat on {floors} floors: facade?",
                    thin.Select(o => o.Id).ToArray());
        }
    }
}
