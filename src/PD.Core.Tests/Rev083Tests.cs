using NetTopologySuite.Geometries;
using PD.Core.Analysis;
using PD.Core.Axons;
using PD.Core.Classification;
using PD.Core.Model;
using PD.Core.Plans;
using Xunit;

namespace PD.Core.Tests;

/// <summary>
/// The Downtown model as the user now draws its roof (rev 0.8.3): no L11 shell; a screen (NC.Screen) around the
/// penthouse and two pieces of roof equipment of different heights inside it, next to the cores.
/// </summary>
public static class ScreenModel
{
    public const double Mm = 304.8;
    public const double ScreenHeightFt = 18;

    static readonly Lazy<SourceModel> LazyModel = new(Build);
    static readonly Lazy<AnalysisResult> LazyResult = new(() => Analyzer.Run(new AnalysisInput { Model = LazyModel.Value }));
    public static SourceModel Model => LazyModel.Value;
    public static AnalysisResult Result => LazyResult.Value;

    public static PD.Core.Drawing.ExportContext Context()
    {
        var settings = new PD.Core.Settings.PdSettings();
        settings.Output.FillDefaults(PD.Core.Config.Rules.Default().Output);
        return new PD.Core.Drawing.ExportContext
        {
            Result = Result, Settings = settings,
            Programs = ProgramCatalog.Effective(LayerClassifier.LoadPrograms(), null),
        };
    }

    /// <summary>The removed L11 shell's box (model mm): the screen stands on its outline.</summary>
    public static BBox3 Shell => DowntownAnalysis.Model.Objects.Single(o => o.LayerPath == "PROGRAM::Shell Space").BBox;

    static SourceModel Build()
    {
        var src = DowntownAnalysis.Model;
        var layers = src.Layers.ToList();
        layers.Add(new LayerInfo(layers.Max(l => l.Index) + 1, "SCREEN", true, "#000000"));
        var objects = src.Objects.Where(o => o.LayerPath != "PROGRAM::Shell Space").ToList();
        var b = Shell;
        double z0 = b.MinZ, z1 = b.MinZ + ScreenHeightFt * Mm;
        (double X, double Y)[] corners = { (b.MinX, b.MinY), (b.MaxX, b.MinY), (b.MaxX, b.MaxY), (b.MinX, b.MaxY) };
        for (var i = 0; i < 4; i++)
        {
            var (p, q) = (corners[i], corners[(i + 1) % 4]);
            objects.Add(new SourceObject
            {
                Id = $"screen-{i}", LayerPath = "SCREEN", GeometryType = "Brep", Name = "",
                BBox = new BBox3(Math.Min(p.X, q.X), Math.Min(p.Y, q.Y), z0, Math.Max(p.X, q.X), Math.Max(p.Y, q.Y), z1),
                IsClosed = false, FaceCount = 1,
                Mesh = new MeshData { Vertices = new[] { p.X, p.Y, z0, q.X, q.Y, z0, q.X, q.Y, z1, p.X, p.Y, z1 }, Faces = new[] { new[] { 0, 1, 2, 3 } } },
            });
        }
        // Roof equipment, 8 ft and 14 ft tall, inside the screen (program Support → building support).
        objects.Add(Box("equip-1", b.MinX + 0.15 * (b.MaxX - b.MinX), b.MinY + 0.1 * (b.MaxY - b.MinY), 20 * Mm, 24 * Mm, z0, 8 * Mm));
        objects.Add(Box("equip-2", b.MinX + 0.6 * (b.MaxX - b.MinX), b.MinY + 0.65 * (b.MaxY - b.MinY), 16 * Mm, 30 * Mm, z0, 14 * Mm));
        return new SourceModel { Units = src.Units, Layers = layers, Objects = objects };
    }

    static SourceObject Box(string id, double x, double y, double dx, double dy, double z, double h)
    {
        double[] v =
        {
            x, y, z, x + dx, y, z, x + dx, y + dy, z, x, y + dy, z,
            x, y, z + h, x + dx, y, z + h, x + dx, y + dy, z + h, x, y + dy, z + h,
        };
        int[][] f = { new[] { 0, 3, 2, 1 }, new[] { 4, 5, 6, 7 }, new[] { 0, 1, 5, 4 }, new[] { 1, 2, 6, 5 }, new[] { 2, 3, 7, 6 }, new[] { 3, 0, 4, 7 } };
        return new SourceObject
        {
            Id = id, LayerPath = "PROGRAM::Support", GeometryType = "Brep", Name = "", BBox = new BBox3(x, y, z, x + dx, y + dy, z + h),
            IsClosed = true, FaceCount = 6, Mesh = new MeshData { Vertices = v, Faces = f },
        };
    }
}

/// <summary>User feedback on v0.8.2 (Windows preview).</summary>
public class Rev083Tests
{
    /// <summary>Review helper: with PD_DUMP_SCREEN=&lt;dir&gt; writes every drawing of the screen model as SVG.</summary>
    [Fact]
    public void DumpScreenModelForReview()
    {
        var dir = Environment.GetEnvironmentVariable("PD_DUMP_SCREEN");
        if (string.IsNullOrEmpty(dir)) return;
        var ctx = ScreenModel.Context();
        var bb = ctx.Result.Building;
        var (x0, y0, _) = ctx.Result.Frame.ToModel(bb.MinX - 40, (bb.MinY + bb.MaxY) / 2, 0);
        var (x1, y1, _) = ctx.Result.Frame.ToModel(bb.MaxX + 40, (bb.MinY + bb.MaxY) / 2, 0);
        ctx.Settings.Sections.Add(new PD.Core.Settings.SectionSetting { Name = "DT", Line = new[] { x0, y0, x1, y1 }, LookLeft = true, DepthFt = 400 });
        var only = Environment.GetEnvironmentVariable("PD_ONLY");
        if (!string.IsNullOrEmpty(only)) ctx.Settings.Output.Drawings = only.Split(',').ToList();
        var notes = new List<string>();
        foreach (var d in PD.Core.Writers.Exporter.BuildDrawings(ctx, notes))
            for (var a = 0; a < d.Artboards.Count; a++)
                File.WriteAllText(Path.Combine(dir, PD.Core.Writers.Exporter.PreviewName(d, a)), PD.Core.Writers.SvgWriter.Write(d, a));
        File.WriteAllLines(Path.Combine(dir, "notes.txt"), notes);
        File.WriteAllLines(Path.Combine(dir, "levels.txt"), ctx.Result.Levels.Select(l => $"{l.Name} {l.Elevation:0.0}"));
    }

    [Fact]
    public void Screen_IsOneClosedEnclosureOnTheRoof_NoNewLevel()
    {
        var r = ScreenModel.Result;
        Assert.Equal(DowntownAnalysis.Result.Levels.Count, r.Levels.Count);
        Assert.Contains(r.Local.Objects, o => o.Id.StartsWith("screen-") && o.Role == Roles.Screen);
        var e = Assert.Single(ScreenEnclosures.Find(r, 0));
        Assert.True(e.Encloses);
        Assert.Equal("L11", r.Levels[e.LevelIndex].Name);
        Assert.Equal(ScreenModel.ScreenHeightFt, e.Z1 - e.Z0, 1);
        // Walls as thick as the model's slabs (~1.1 ft); the floor covers the whole enclosed roof.
        var t = ScreenEnclosures.SlabThickness(r)!.Value;
        Assert.InRange(t, 0.9, 1.4);
        var shell = ScreenModel.Shell;
        var area = (shell.MaxX - shell.MinX) * (shell.MaxY - shell.MinY) / (ScreenModel.Mm * ScreenModel.Mm);
        Assert.InRange(e.Floor.Area, 0.97 * area, (Math.Sqrt(area) + t) * (Math.Sqrt(area) + t) * 1.05);
    }

    static IEnumerable<double[]> Points(PD.Core.Drawing.DItem i) => i.Paths!.SelectMany(p => p);
    static Envelope Env(PD.Core.Drawing.DItem i)
    {
        var e = new Envelope();
        foreach (var q in Points(i)) e.ExpandToInclude(q[0], q[1]);
        return e;
    }

    // ------------------------------------------------------------------ (1) streetscape

    [Fact]
    public void Site_SidewalksStreetTreesAndMedians()
    {
        var ctx = DowntownAnalysis.Context();
        var site = SiteLayout.For(ctx);
        Assert.Contains(site.Items, i => i.Role == SiteLayout.Sidewalk && !i.IsLine);
        Assert.Contains(site.Items, i => i.Role == SiteLayout.Sidewalk && i.Name == "SIDEWALK_EDGE");
        Assert.True(site.StreetTrees.Count > 100, $"{site.StreetTrees.Count} street trees");
        // Trunks 1.5 ft inside the kerb.
        var kerb = site.Blocks.Boundary;
        var near = site.StreetTrees.Select(c => kerb.Distance(new GeometryFactory().CreatePoint(c))).OrderBy(x => x).ToList();
        Assert.InRange(near[near.Count / 2], 1.4, 1.6);
        // Sidewalk band 4.5 ft wide: the inner edge lies 4.5 ft from the kerb.
        var edge = site.Items.First(i => i.Name == "SIDEWALK_EDGE").Geometry;
        Assert.InRange(edge.Coordinates.Take(50).Average(c => kerb.Distance(new GeometryFactory().CreatePoint(c))), 4.3, 4.7);
        // Wide roads get a 5 ft median with round ends and trees on it, kept on the road.
        Assert.NotEmpty(site.MedianLines);
        Assert.NotEmpty(site.MedianTrees);
        var median = site.Items.First(i => i.Role == SiteLayout.Median).Geometry;
        Assert.False(median.Intersects(site.Blocks.Buffer(-0.5)));
        var line = site.MedianLines[0];
        Assert.InRange(median.Area, line.Length * 5 * 0.95, (line.Length + 5) * 5 * 1.05);
    }

    [Fact]
    public void SitePlan_SecondArtboardSeesTheWholeBuildingFromAbove()
    {
        var d = PlanDrawing.BuildSite(DowntownAnalysis.Context());
        Assert.Equal(new[] { "SITE PLAN", "SITE PLAN ROOF" }, d.Artboards.Select(a => a.Name));
        var roof = d.Layer("PD_PROGRAM").Items.Where(i => i.Artboard == 1).ToList();
        // Every level from grade up, the top one last (it shows on top).
        Assert.Contains(roof, i => i.Name.StartsWith("STR_L1_"));
        Assert.Equal("STR_L11", string.Join("_", roof.Last().Name.Split('_').Take(2)));
        Assert.Contains(d.Layer("PD_OUTLINE").Items, i => i.Artboard == 1 && i.Name == "STR_OUTLINE");
    }

    [Fact]
    public void Section_DrawsStreetTreesBeyondTheCut()
    {
        var ctx = DowntownAnalysis.Context();
        var bb = ctx.Result.Building;
        var (x0, y0, _) = ctx.Result.Frame.ToModel(bb.MinX - 40, (bb.MinY + bb.MaxY) / 2, 0);
        var (x1, y1, _) = ctx.Result.Frame.ToModel(bb.MaxX + 40, (bb.MinY + bb.MaxY) / 2, 0);
        var sec = new PD.Core.Settings.SectionSetting { Name = "DT", Line = new[] { x0, y0, x1, y1 }, LookLeft = true, DepthFt = 400 };
        var d = PD.Core.Sections.SectionDrawing.Build(ctx.ForSection(), sec);
        var modelled = ctx.Result.Local.Objects.Count(o => o.Role is Roles.Tree or Roles.NcTree);
        Assert.True(d.Layer("BG_TREE").Items.Count > modelled, $"{d.Layer("BG_TREE").Items.Count} trees");
    }

    // ------------------------------------------------------------------ (5) entourage

    [Fact]
    public void People_OnlyOnHardscape()
    {
        var ctx = DowntownAnalysis.Context();
        var g = PlanDrawing.GroundIndex(ctx);
        var env = PlanDrawing.SiteExtent(ctx, g);
        var items = PlanBackground.Collect(ctx, env);
        var walk = PlanBackground.Walkable(items, PlanBackground.Footprint(ctx.Result));
        Assert.False(walk.IsEmpty);
        var f = new GeometryFactory();
        foreach (var b in items.Where(i => i.Role is Roles.Building && !i.IsLine).Take(50))
            Assert.True(walk.Intersection(b.Geometry).Area < 1, "people could stand in a building");
        // Road surface outside every block is never walkable.
        var site = SiteLayout.For(ctx);
        var road = items.Where(i => i.Role == Roles.Road && !i.IsLine).Select(i => i.Geometry).Aggregate((a, b) => a.Union(b)).Difference(site.Blocks.Buffer(0.5));
        Assert.True(walk.Intersection(road).Area < 0.01 * road.Area);
    }

    // ------------------------------------------------------------------ (2)(3)(4) roof screen

    [Fact]
    public void ScreenAxon_SolidWallsWhiteFloorOpenTop()
    {
        var ctx = ScreenModel.Context();
        var d = AxonDrawing.Build(ctx, AxonKind.Stacked);
        var program = d.Layer("PD_PROGRAM").Items;
        Assert.Contains(program, i => i.Name.StartsWith("AXS_SCREEN_01_WALL"));
        var floor = program.Where(i => i.Name.StartsWith("AXS_SCREEN_01_FLOOR")).ToList();
        Assert.NotEmpty(floor);
        Assert.All(floor, i => Assert.Equal("#FFFFFF", d.Swatches.Single(s => s.Name == i.Fill).Hex));
        // No top over the screen: the floor's top face is seen (open), and the screens are no thin veils any more.
        Assert.Contains(floor, i => i.Name.EndsWith("TOP"));
        Assert.DoesNotContain(d.Layers, l => l.Name == "AXON_SCREEN" && l.Items.Count > 0);
        // Both pieces of equipment stand inside at their own heights.
        Assert.Equal(2, ScreenModel.Result.Local.Objects.Count(o => o.Id.StartsWith("equip-")));
    }

    [Fact]
    public void ScreenPlan_FloorSolidWallsPoche()
    {
        var ctx = ScreenModel.Context();
        var d = PlanDrawing.Build(ctx);
        var l11 = d.Artboards.FindIndex(a => a.Name == "PLAN L11");
        Assert.Contains(d.Layer("PD_PROGRAM").Items, i => i.Artboard == l11 && i.Name.EndsWith("SCREEN_01_FLOOR") && i.Fill != null);
        Assert.Contains(d.Layer("PD_SCREEN").Items, i => i.Artboard == l11 && i.Name.EndsWith("SCREEN_01_WALL") && i.Fill != null);
    }

    [Fact]
    public void ScreenSection_WallsSolidOpenTopDashedPenthouseNamedAndMarked()
    {
        var ctx = ScreenModel.Context();
        var bb = ctx.Result.Building;
        var (x0, y0, _) = ctx.Result.Frame.ToModel(bb.MinX - 40, (bb.MinY + bb.MaxY) / 2, 0);
        var (x1, y1, _) = ctx.Result.Frame.ToModel(bb.MaxX + 40, (bb.MinY + bb.MaxY) / 2, 0);
        var sec = new PD.Core.Settings.SectionSetting { Name = "DT", Line = new[] { x0, y0, x1, y1 }, LookLeft = true, DepthFt = 400 };
        var d = PD.Core.Sections.SectionDrawing.Build(ctx.ForSection(), sec);
        var scr = d.Layer("PD_SCREEN").Items;
        Assert.Equal(2, scr.Count(i => i.Name.Contains("SCREEN_01_WALL")));
        var open = Assert.Single(scr, i => i.Name.Contains("SCREEN_01_OPEN"));
        Assert.NotNull(open.Dash);
        Assert.Contains(d.Layer("PD_LABELS").Items, i => i.Name.EndsWith("SCREEN_01_LBL") && i.Text == "PENTHOUSE");
        Assert.DoesNotContain(d.Layer("PD_PROGRAM").Items, i => i.Name.Contains("PENTHOUSE_"));
        // T.O. PENTHOUSE at the screen top (L11 + 18 ft), in line with the open top.
        var top = d.Layers.SelectMany(l => l.Items).First(i => i.Name.Contains("PENTHOUSE_TOP"));
        var openY = open.Paths![0][0][1];
        Assert.True(Points(top).Any(q => Math.Abs(q[1] - openY) < 1.5) || Math.Abs(top.Y - openY) < 12, $"marker {top.Y}, top {openY}");
    }

    // ------------------------------------------------------------------ (6) exploded basement walls

    [Fact]
    public void Exploded_ExcavationWallsGoWithB1()
    {
        var d = AxonDrawing.Build(DowntownAnalysis.Context(), AxonKind.Exploded);
        var site = d.Layers.SelectMany(l => l.Items).Single(i => i.Name == "AXE_SITE");
        var walls = d.Layer("AXON_EXCAVATION").Items;
        Assert.NotEmpty(walls);
        var siteBottom = Points(site).Max(q => q[1]);
        // Page y grows down: every wall lies under the site plate, around the B1 plate.
        Assert.All(walls, w => Assert.True(Env(w).MinY > siteBottom - 1, $"wall top {Env(w).MinY}, site bottom {siteBottom}"));
    }

    // ------------------------------------------------------------------ (7) floor key cutaway

    [Fact]
    public void Levels_CutawayWhiteBelowColourOnTheFloorOutlineAbove()
    {
        var ctx = DowntownAnalysis.Context();
        var d = AxonDrawing.Build(ctx, AxonKind.Levels);
        var l5 = d.Artboards.FindIndex(a => a.Name == "AXON L5");
        var faces = d.Layer("PD_PROGRAM").Items.Where(i => i.Artboard == l5).ToList();
        var white = d.Swatches.Single(s => s.Name == "AXON_LEVELS_BELOW").Name;
        Assert.Contains(faces, i => i.Name.Contains("_BELOW_") && i.Fill == white);
        Assert.DoesNotContain(faces, i => (i.Name.StartsWith("AXK_L5_L4_") && !i.Name.Contains("_BELOW_")) || i.Name.StartsWith("AXK_L5_L6_"));
        var current = faces.Where(i => i.Name.StartsWith("AXK_L5_L5_")).ToList();
        Assert.NotEmpty(current);
        Assert.All(current, i => Assert.Equal(1.0, i.StrokePt));
        Assert.All(current, i => Assert.Equal("#FFFFFF", d.Swatches.Single(s => s.Name == i.Stroke).Hex));
        Assert.Contains(d.Layer("AXON_GHOST_ABOVE").Items, i => i.Artboard == l5 && i.Name.EndsWith("BUILDING_OUTLINE"));
        Assert.DoesNotContain(d.Layers, l => l.Name == "AXON_GHOST_BELOW" && l.Items.Count > 0);
        // Basements stay underground on the floors at grade and above.
        Assert.DoesNotContain(faces, i => i.Name.Contains("B1_BELOW"));
    }

    // ------------------------------------------------------------------ (8) site axon fills the page

    [Fact]
    public void SiteAxon_GroundCoversTheWholeFrame()
    {
        var d = AxonDrawing.Build(DowntownAnalysis.Context(), AxonKind.Site);
        var site = d.Layers.SelectMany(l => l.Items).Single(i => i.Name == "AXT_SITE");
        var e = Env(site);
        // The site plate is the frame (a full rectangle), and nothing of the site / context spills past it.
        var ring = new GeometryFactory().CreatePolygon(site.Paths![0].Append(site.Paths[0][0]).Select(q => new Coordinate(q[0], q[1])).ToArray());
        Assert.InRange(ring.Area / (e.Width * e.Height), 0.98, 1.001);
        foreach (var layer in new[] { "AXON_SITE", "AXON_CONTEXT", "AXON_TREE" })
            Assert.All(d.Layer(layer).Items.Where(i => i.Paths is { Count: > 0 }), i => Assert.True(e.Covers(Env(i)) || Env(i).Intersection(e).Area >= 0.99 * Env(i).Area,
                $"{i.Name} outside the frame"));
    }

    // ------------------------------------------------------------------ (9) view corner

    [Theory]
    [InlineData("NE", 45)]
    [InlineData("NW", 135)]
    [InlineData("SE", 315)]
    [InlineData("SW", 225)]
    public void AxonView_Corners(string corner, double azimuth)
    {
        var ctx = DowntownAnalysis.Context();
        ctx.Settings.Output.AxonView = corner;
        Assert.Equal(azimuth, ctx.AxonAzimuth());
    }

    [Fact]
    public void AxonView_PlacedPointLooksAtTheBuilding()
    {
        var ctx = DowntownAnalysis.Context();
        var c = PlanBackground.Footprint(ctx.Result).EnvelopeInternal.Centre;
        var (x, y, _) = ctx.Result.Frame.ToModel(c.X + 500, c.Y - 500, 0);
        ctx.Settings.Output.AxonView = "Point";
        ctx.Settings.Output.AxonViewPoint = new[] { x, y };
        Assert.Equal(-45, ctx.AxonAzimuth()!.Value, 3);
        // Drawn from the south-east, the building's east face is now in view.
        var d = AxonDrawing.Build(ctx, AxonKind.Stacked);
        Assert.NotEmpty(d.Layer("PD_PROGRAM").Items);
    }

    // ------------------------------------------------------------------ (10) settings survive

    [Fact]
    public void Settings_UnreadablePartIsSkippedNotEverything()
    {
        var s = new PD.Core.Settings.PdSettings();
        s.Names["abc"] = "Lobby";
        s.Output.Scale = "1\" = 30'";
        var json = s.ToJson().Replace("\"spanModes\":{}", "\"spanModes\":{\"m1\":\"NoSuchMode\"}");
        Assert.Contains("NoSuchMode", json);
        var back = PD.Core.Settings.PdSettings.FromJson(json);
        Assert.Equal("Lobby", back.Names["abc"]);
        Assert.Equal("1\" = 30'", back.Output.Scale);
    }

    [Fact]
    public void Settings_NewestCopyWins_AndTravelWithTheExport()
    {
        var older = new PD.Core.Settings.PdSettings { SavedUtc = "2026-10-01T00:00:00Z" };
        var newer = new PD.Core.Settings.PdSettings { SavedUtc = "2026-10-13T00:00:00Z" };
        Assert.Same(newer, PD.Core.Settings.PdSettings.Newest(older, null, newer));
        var ctx = DowntownAnalysis.Context();
        ctx.Settings.Output.Drawings = new() { "Pie" };
        ctx.Settings.Names["x"] = "Kept";
        var dir = Path.Combine(Path.GetTempPath(), "pd_settings_" + Guid.NewGuid().ToString("N"));
        var res = PD.Core.Writers.Exporter.Write(ctx, dir);
        var file = Path.Combine(res.Folder, PD.Core.Settings.PdSettings.FileName);
        Assert.True(File.Exists(file));
        Assert.Equal("Kept", PD.Core.Settings.PdSettings.FromJson(File.ReadAllText(file)).Names["x"]);
        Directory.Delete(dir, true);
    }
}
