using CleanTool.Core.Model;
using CleanTool.Core.Geometry;
using CleanTool.Core.Operations.BlockTrim;
using CleanTool.Core.Pipeline;
using CleanTool.Core.Tests.Fakes;

namespace CleanTool.Core.Tests;

/// <summary>
/// Opening T and cross junctions (<see cref="WallJunctionOpener"/>). Walls are 20 thick, max thickness 60.
/// </summary>
public class WallJunctionOpenerTests
{
    private static int _nextId;

    private static WallPath Path(params (double X, double Y)[] points) =>
        new(_nextId++, "WALL", points.Select(p => new Point3(p.X, p.Y, 0)).ToList());

    private static WallJunctionResult Open(params WallPath[] paths) =>
        new WallJunctionOpener(new Tolerance(1e-4), 60).Open(paths);

    private static bool IsSegment(IReadOnlyList<Point3> chain, double x1, double y1, double x2, double y2) =>
        chain.Count == 2 &&
        new Segment3(chain[0], chain[1]).IsEqualTo(new Segment3(new Point3(x1, y1, 0), new Point3(x2, y2, 0)), new Tolerance(1e-6));

    [Fact]
    public void T_junction_cuts_the_main_face_between_the_branch_faces()
    {
        //  ════╦════   →   ════╝ ╚════
        var far = Path((0, 0), (300, 0));
        var near = Path((0, 20), (300, 20));
        var left = Path((140, 20), (140, 200));
        var right = Path((160, 20), (160, 200));

        var result = Open(far, near, left, right);

        var chains = Assert.Single(result.Changed).Value;
        Assert.Equal(near.Id, result.Changed.Keys.Single());
        Assert.Equal(2, chains.Count);
        Assert.Contains(chains, c => IsSegment(c, 0, 20, 140, 20));
        Assert.Contains(chains, c => IsSegment(c, 160, 20, 300, 20));
    }

    [Fact]
    public void Oblique_T_junction_is_opened_although_the_faces_end_at_different_points()
    {
        // A 60 degree wall, 20 thick, ending on the main face y = 20: its faces end 23.09 apart along the face.
        var a = Math.PI / 3;
        var offset = 20 / Math.Sin(a);
        var near = Path((0, 20), (600, 20));
        var left = Path((300, 20), (300 + 300 * Math.Cos(a), 20 + 300 * Math.Sin(a)));
        var right = Path((300 + offset, 20), (300 + offset + 300 * Math.Cos(a), 20 + 300 * Math.Sin(a)));

        var result = Open(Path((0, 0), (600, 0)), near, left, right);

        var chains = result.Changed[near.Id];
        Assert.Equal(2, chains.Count);
        Assert.Contains(chains, c => IsSegment(c, 0, 20, 300, 20));
        Assert.Contains(chains, c => IsSegment(c, 300 + offset, 20, 600, 20));
    }

    [Fact]
    public void Cross_junction_cuts_all_four_faces()
    {
        var result = Open(
            Path((0, 0), (300, 0)), Path((0, 20), (300, 20)),
            Path((140, -150), (140, 200)), Path((160, -150), (160, 200)));

        Assert.Equal(4, result.Changed.Count);
        Assert.Equal(4, result.CutCount);
        Assert.All(result.Changed.Values, chains => Assert.Equal(2, chains.Count));
    }

    [Fact]
    public void L_corner_is_not_cut()
    {
        var result = Open(
            Path((0, 0), (300, 0)), Path((300, 0), (300, -200)),
            Path((0, 20), (320, 20)), Path((320, 20), (320, -200)));

        Assert.Empty(result.Changed);
    }

    [Fact]
    public void Overshooting_L_corner_is_not_cut()
    {
        var result = Open(
            Path((0, 0), (303, 0)), Path((300, 3), (300, -200)),
            Path((0, 20), (323, 20)), Path((320, 23), (320, -200)));

        Assert.Empty(result.Changed);
    }

    [Fact]
    public void Wall_end_line_and_door_jamb_are_not_cut()
    {
        var result = Open(Path((0, 0), (100, 0)), Path((0, 20), (100, 20)), Path((100, 0), (100, 20)));

        Assert.Empty(result.Changed);
    }

    [Fact]
    public void Gap_between_two_walls_is_not_a_wall()
    {
        // Two walls 30 apart; a third wall crosses both. Its faces stay intact in the gap.
        var a = Path((140, -100), (140, 200));

        var result = Open(
            Path((0, 0), (300, 0)), Path((0, 20), (300, 20)),
            Path((0, 50), (300, 50)), Path((0, 70), (300, 70)),
            a, Path((160, -100), (160, 200)));

        var chains = result.Changed[a.Id];
        Assert.Equal(3, chains.Count); // below, between the walls, above
        Assert.Contains(chains, c => IsSegment(c, 140, 20, 140, 50));
    }

    [Fact]
    public void Already_open_T_is_left_alone()
    {
        var result = Open(
            Path((0, 0), (300, 0)), Path((0, 20), (140, 20)), Path((160, 20), (300, 20)),
            Path((140, 20), (140, 200)), Path((160, 20), (160, 200)));

        Assert.Empty(result.Changed);
    }

    [Fact]
    public void Polyline_face_is_split_into_open_polylines()
    {
        // The near face is a polyline that turns down at the right end.
        var near = Path((0, 20), (300, 20), (300, 100));

        var result = Open(Path((0, 0), (320, 0), (320, 100)), near,
            Path((140, 20), (140, 200)), Path((160, 20), (160, 200)));

        var chains = result.Changed[near.Id];
        Assert.Equal(2, chains.Count);
        Assert.Contains(chains, c => c.Count == 3); // (160,20) → (300,20) → (300,100)
    }

    [Fact]
    public void Walls_on_other_layers_do_not_cut()
    {
        var other = new WallPath(_nextId++, "FURNITURE", new[] { new Point3(0, 20, 0), new Point3(300, 20, 0) });

        var result = new WallJunctionOpener(new Tolerance(1e-4), 60).Open(new[]
        {
            Path((140, 20), (140, 200)), Path((160, 20), (160, 200)), Path((0, 0), (300, 0)), other,
        });

        Assert.Empty(result.Changed);
    }
}

/// <summary>WallTrim with "Open wall junctions" (and closed polylines).</summary>
public class BlockTrimJunctionTests
{
    private static FakeDuplicateGateway UntrimmedT() => new(
        Make.Line(1, 0, 0, 300, 0, "WALL"), Make.Line(2, 0, 20, 300, 20, "WALL"),
        Make.Line(3, 140, 20, 140, 200, "WALL"), Make.Line(4, 160, 20, 160, 200, "WALL"));

    [Fact]
    public void Open_junctions_replaces_the_main_face_by_two_pieces()
    {
        var gateway = UntrimmedT();

        new BlockTrimOperation(gateway, new BlockTrimOptions { OpenJunctions = true }).Execute(new CleanContext());

        var pieces = Assert.Single(gateway.Replaced);
        Assert.Equal(new EntityHandle(2), pieces.Key);
        Assert.Equal(2, pieces.Value.Count);
        Assert.Empty(gateway.Erased);
    }

    [Fact]
    public void Opened_T_becomes_one_closed_polyline()
    {
        var gateway = UntrimmedT();

        new BlockTrimOperation(gateway, new BlockTrimOptions { OpenJunctions = true, CreateClosedOutlines = true })
            .Execute(new CleanContext());

        var (_, vertices) = Assert.Single(gateway.AddedPolylines);
        Assert.Equal(8, vertices.Count);
        Assert.Equal(4, gateway.Erased.Count); // every line went into the polyline
    }

    [Fact]
    public void Lines_outside_the_wall_layers_are_not_joined()
    {
        // Window lines between two jambs: parallel and close, but on PENCERE.
        var gateway = new FakeDuplicateGateway(
            Make.Line(1, 0, 0, 300, 0, "DUVAR"), Make.Line(2, 0, 20, 300, 20, "DUVAR"),
            Make.Line(3, 400, 0, 520, 0, "PENCERE"), Make.Line(4, 400, 7, 520, 7, "PENCERE"));

        var options = new BlockTrimOptions { CreateClosedOutlines = true, WallLayers = new[] { "DUV*" } };
        new BlockTrimOperation(gateway, options).Execute(new CleanContext());

        Assert.Single(gateway.AddedPolylines);
        Assert.DoesNotContain(new EntityHandle(3), gateway.Erased);
        Assert.DoesNotContain(new EntityHandle(4), gateway.Erased);
    }

    [Fact]
    public void Preview_reports_the_opened_lines_without_changes()
    {
        var gateway = UntrimmedT();

        var result = new BlockTrimOperation(gateway, new BlockTrimOptions { OpenJunctions = true })
            .Execute(new CleanContext(dryRun: true));

        Assert.Empty(gateway.Replaced);
        Assert.Contains(result.Messages, m => m.Contains("1 wall line(s) would be opened"));
    }
}

/// <summary>The layer mismatch note of <see cref="WallOutlineBuilder"/>.</summary>
public class WallOutlineLayerNoteTests
{
    [Fact]
    public void Wall_lines_meeting_lines_on_another_layer_are_reported()
    {
        static WallPath P(int id, string layer, params (double X, double Y)[] pts) =>
            new(id, layer, pts.Select(p => new Point3(p.X, p.Y, 0)).ToList());

        var result = new WallOutlineBuilder(new Tolerance(1e-4), 60).Build(new[]
        {
            P(1, "WALL", (0, 0), (0, 300)), P(2, "WALL", (20, 20), (20, 300)),
            P(3, "0", (0, 0), (300, 0)), P(4, "0", (20, 20), (300, 20)),
        });

        Assert.Contains(result.Notes, n => n.Contains("'WALL'") && n.Contains("'0'") && n.Contains("not joined"));
    }
}
