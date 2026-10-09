using System.Drawing;
using Rhino.Display;
using Rhino.Geometry;
using PD.Core.Analysis;
using PD.Core.Levels;

namespace PD.Rhino.Display;

/// <summary>PRD B1.6: translucent level planes with labels and the sources that support each level.</summary>
public sealed class LevelConduit : DisplayConduit
{
    sealed record Plane3(Point3d[] Corners, Mesh Quad, string Label, Color Color, DisplayMaterial Material);

    List<Plane3> _planes = new();
    BoundingBox _bbox = BoundingBox.Empty;

    public static LevelConduit Instance { get; } = new();

    public void Update(AnalysisResult? r)
    {
        _planes = new();
        _bbox = BoundingBox.Empty;
        if (r == null) return;
        var b = r.Building;
        const double margin = 10; // ft around the building
        foreach (var l in r.Levels)
        {
            var (x0, y0, z) = r.Frame.ToModel(b.MinX - margin, b.MinY - margin, l.Elevation);
            var (x1, y1, _) = r.Frame.ToModel(b.MaxX + margin, b.MaxY + margin, l.Elevation);
            var pts = new[] { new Point3d(x0, y0, z), new Point3d(x1, y0, z), new Point3d(x1, y1, z), new Point3d(x0, y1, z), new Point3d(x0, y0, z) };
            var quad = new Mesh();
            quad.Vertices.AddVertices(pts.Take(4));
            quad.Faces.AddFace(0, 1, 2, 3);
            var color = l.IsMezzanine ? Color.DarkOrange : (l.Sources & LevelSource.Slab) == 0 ? Color.Firebrick : Color.SteelBlue;
            _planes.Add(new Plane3(pts, quad, $"{l.Name}  {PD.Core.Model.FeetInches.Format(l.Elevation)}  [{l.Sources}]", color, new DisplayMaterial(color, 0.85)));
            _bbox.Union(new BoundingBox(pts));
        }
    }

    protected override void CalculateBoundingBox(CalculateBoundingBoxEventArgs e)
    {
        if (_bbox.IsValid) e.IncludeBoundingBox(_bbox);
    }

    protected override void PostDrawObjects(DrawEventArgs e)
    {
        foreach (var p in _planes)
        {
            e.Display.DrawMeshShaded(p.Quad, p.Material);
            e.Display.DrawPolyline(p.Corners, p.Color, 1);
            e.Display.Draw2dText(p.Label, p.Color, p.Corners[0], false, 12);
        }
    }
}
