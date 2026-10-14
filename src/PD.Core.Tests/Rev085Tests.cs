using NetTopologySuite.Geometries;
using PD.Core.Analysis;
using PD.Core.Axons;
using PD.Core.Classification;
using PD.Core.IO;
using PD.Core.Model;
using PD.Core.Plans;
using Xunit;

namespace PD.Core.Tests;

/// <summary>User feedback on v0.8.4 (Windows export of the updated Downtown model, NE view, property line).</summary>
public class Rev085Tests
{
    const string PodiumRoof = "f67445df-aa2b-4a78-b9e5-8cd6c681a06a";   // FLOOR PLATE at L4 over the L3 lobbies
    const string TowerL2Plate = "b26819ca-4702-4166-8d06-d5b3f1be449b"; // L2 plate inside the tower, under the L4 floor

    /// <summary>
    /// The user model as Rhino hands it over: raw doubles instead of the fixture's whole millimetres (every mesh vertex
    /// moved by up to ±0.1 mm), so the overlays meet near-coincident edges.
    /// </summary>
    static readonly Lazy<AnalysisResult> Noisy = new(() =>
    {
        var model = FixtureReader.Load(FixtureSmokeTests.FixturePath("downtown.json"));
        var rnd = new Random(7);
        double J(double x) => x + (rnd.NextDouble() - 0.5) * 2e-1;
        model = new SourceModel
        {
            Units = model.Units, Layers = model.Layers,
            Objects = model.Objects.Select(x => x.Mesh == null ? x : new SourceObject
            {
                Id = x.Id, LayerPath = x.LayerPath, GeometryType = x.GeometryType, Name = x.Name, BBox = x.BBox, IsClosed = x.IsClosed,
                WasCapped = x.WasCapped, FaceCount = x.FaceCount, Mesh = new MeshData { Vertices = x.Mesh.Vertices.Select(J).ToArray(), Faces = x.Mesh.Faces },
            }).ToList(),
        };
        var s = DowntownAnalysis.UserSettings();
        var roles = s.RoleOverrides().ToDictionary(kv => kv.Key, kv => kv.Value);
        foreach (var l in model.Layers.Where(l => !roles.ContainsKey(l.Path))) roles[l.Path] = new LayerGuess(l.Path, Roles.Ignore, "");
        return Analyzer.Run(new AnalysisInput { Model = model, RoleOverrides = roles, KeepStray = s.KeepStray.ToHashSet() });
    });

    static PD.Core.Drawing.ExportContext NoisyContext()
    {
        var c = DowntownAnalysis.UserContext();
        return new PD.Core.Drawing.ExportContext { Result = Noisy.Value, Settings = c.Settings, Programs = c.Programs };
    }

    // ------------------------------------------------------------------ Windows-only failures: coordinates snapped

    [Fact]
    public void RawRhinoCoordinates_AreSnapped_AndTheScreenStaysOpen()
    {
        var r = Noisy.Value;
        Assert.Equal(DowntownAnalysis.UserResult.Levels.Count, r.Levels.Count);
        var snap = PD.Core.Config.Rules.Default().Cleanup.CoordSnapFt;
        Assert.True(snap > 0);
        foreach (var v in r.Local.Objects.First(o => o.Mesh != null).Mesh!.Vertices.Take(30))
            Assert.True(Math.Abs(v / snap - Math.Round(v / snap)) < 1e-6, $"{v} not on the {snap} ft grid");
        // The roof screen: its white floor and the inner faces of its far walls are drawn (blank before on Windows).
        var d = AxonDrawing.Build(NoisyContext(), AxonKind.Stacked);
        var names = d.Layers.SelectMany(l => l.Items).Select(i => i.Name).ToList();
        Assert.Contains(names, n => n.Contains("SCREEN_01_FLOOR_TOP"));
        Assert.True(names.Count(n => n.Contains("SCREEN_01_WALL") && n.Contains("SIDE")) >= 4);
    }

    // ------------------------------------------------------------------ (2) roofs: open to the sky vs open on their level

    [Fact]
    public void Exposed_SkyLeavesOutSlabsUnderHigherFloors()
    {
        var all = ExposedSurfaces.Find(DowntownAnalysis.UserResult, 0);
        var roof = all.Single(e => e.Id == PodiumRoof);
        Assert.InRange(roof.Sky.Area, roof.Area.Area - 5, roof.Area.Area + 5);
        var plate = all.Single(e => e.Id == TowerL2Plate);
        Assert.True(plate.Area.Area > 500);   // open on its own level (the plans show it)…
        Assert.True(plate.Sky.IsEmpty);        // …but the L4 floor stands over it: no roof in the stacked axon
    }

    [Fact]
    public void Stacked_PodiumBlocksStopUnderTheirRoof()
    {
        // A block stretched to the level line under a plate lying there: only the part under the plate is lowered.
        var f = new GeometryFactory();
        Polygon Rect(double x0, double x1) => f.CreatePolygon(new[] { new Coordinate(x0, 0), new Coordinate(x1, 0), new Coordinate(x1, 10), new Coordinate(x0, 10), new Coordinate(x0, 0) });
        var block = new Prism("B", Rect(0, 20), 0, 20, "T", "S", "S", "PRG", "LAB");
        var plate = new Prism("R", Rect(10, 30), 19, 20, "T", "S", "S", "EXP");
        var parts = AxonDrawing.UnderPlates(new List<Prism> { block }, new[] { plate });
        Assert.Equal(2, parts.Count);
        Assert.Contains(parts, x => x.Name == "B" && x.Z1 == 20 && x.Label == "LAB" && Math.Abs(x.Plan.Area - 100) < 1e-6);
        Assert.Contains(parts, x => x.Name == "B_U1" && x.Z1 == 19 && Math.Abs(x.Plan.Area - 100) < 1e-6);
        // Downtown: the L3 lobbies under the podium roof never show their tops through it.
        var d = AxonDrawing.Build(DowntownAnalysis.UserContext(), AxonKind.Stacked);
        // (Since 0.8.6 the glass canopy is see-through: tops may show under it, nowhere else.)
        static Envelope Env(PD.Core.Drawing.DItem i)
        {
            var e = new Envelope();
            foreach (var q in i.Paths!.SelectMany(x => x)) e.ExpandToInclude(q[0], q[1]);
            return e;
        }
        var glass = d.Layer("FACADE").Items.Where(i => i.Name.Contains("EXPOSED_GLASS") && i.Paths != null).Select(Env).ToList();
        foreach (var top in d.Layer("PD_PROGRAM").Items.Where(i => i.Name.Contains("_L3_") && i.Name.Contains("LOBBY") && i.Name.Contains("_TOP")))
        {
            var e = Env(top);
            e.ExpandBy(-0.5);
            Assert.True(glass.Any(g => { var x = new Envelope(g); x.ExpandBy(1); return x.Contains(e); }), $"{top.Name} shows outside the glass");
        }
    }

    // ------------------------------------------------------------------ (1) plans see the floors below

    [Fact]
    public void Plans_SeeLowerRoofsAndTheBasementThroughTheOpening()
    {
        var ctx = DowntownAnalysis.UserContext();
        var r = ctx.Result;
        var l5 = r.Levels.FindIndex(l => l.Name == "L5");
        var seen = SeenBelow.For(ctx, l5);
        Assert.Contains(seen, e => e.LevelIndex == 4 && !e.IsGlass && e.Area.Area > 5000); // the podium roof
        Assert.Contains(seen, e => e.LevelIndex == 4 && e.IsGlass);                         // the glass canopy
        var g = PlanDrawing.GroundIndex(ctx);
        var atGrade = SeenBelow.For(ctx, g);
        Assert.All(atGrade, e => Assert.True(e.ThroughOpening));
        Assert.Contains(atGrade, e => e.LevelIndex < g);
        var plans = PlanDrawing.Build(ctx);
        Assert.Contains(plans.Layers.SelectMany(l => l.Items), i => i.Name.Contains("L1_OPENING_01"));
        Assert.Contains(plans.Layers.SelectMany(l => l.Items), i => i.Name.Contains("L5_BELOW_L4"));
    }

    // ------------------------------------------------------------------ (3) exploded: shafts under the site go behind it

    [Fact]
    public void Exploded_ShaftsUnderTheSiteSitBehindIt()
    {
        var d = AxonDrawing.Build(DowntownAnalysis.UserContext(), AxonKind.Exploded);
        var order = d.Layers.Select(l => l.Name).ToList();
        Assert.True(order.IndexOf("AXON_CONNECTOR_BELOW") >= 0 && order.IndexOf("AXON_CONNECTOR_BELOW") < order.IndexOf("AXON_SITE"));
        Assert.NotEmpty(d.Layer("AXON_CONNECTOR_BELOW").Items);
    }

    // ------------------------------------------------------------------ (4) trees behind a slim face

    [Fact]
    public void Hide_SlimFaceAcrossATreeCrown()
    {
        var f = new GeometryFactory();
        var proj = AxonProjection.FromAngles(225, 35.264);
        // A 1 ft thick, 80 ft tall wall; a tree crown centred beside it so that neither its centre nor its outline
        // points fall on the wall's faces — only the crown's middle band does.
        var wall = new Prism("WALL", f.CreatePolygon(new[] { new Coordinate(0, 0), new Coordinate(1, 0), new Coordinate(1, 40), new Coordinate(0, 40), new Coordinate(0, 0) }), 0, 80, "T", "S", "S", "PRG");
        var faces = AxonFaces.Render(proj, new[] { wall });
        var face = faces.OrderByDescending(x => x.Screen.Area).First().Screen;
        var c = face.Centroid.Coordinate;
        var w = face.EnvelopeInternal.Width;
        var tree = f.CreatePoint(new Coordinate(c.X + w * 0.6, c.Y)).Buffer(w * 1.2, 4);
        var left = AxonFaces.Hide(proj, tree, -1e9, faces);
        Assert.NotNull(left);
        Assert.True(left!.Area < tree.Area - 1e-3, $"{left.Area:0.000} vs {tree.Area:0.000}");
    }
}
