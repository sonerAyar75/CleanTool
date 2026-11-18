using CleanTool.Core.Geometry;
using CleanTool.Core.Model;
using CleanTool.Core.Operations.BlockTrim;
using CleanTool.Core.Pipeline;
using CleanTool.Core.Tests.Fakes;

namespace CleanTool.Core.Tests;

/// <summary>
/// Closed wall outlines (<see cref="WallOutlineBuilder"/>): walls are two parallel faces 20 units apart,
/// max wall thickness 60.
/// </summary>
public class WallOutlineBuilderTests
{
    private static int _nextId;

    private static WallPath Path(params (double X, double Y)[] points) =>
        new(_nextId++, "A", points.Select(p => new Point3(p.X, p.Y, 0)).ToList());

    private static WallOutlineResult Build(params WallPath[] paths) =>
        new WallOutlineBuilder(new Tolerance(1e-4), 60).Build(paths);

    private static bool HasVertices(WallOutline outline, params (double X, double Y)[] expected) =>
        outline.Vertices.Count == expected.Length &&
        expected.All(e => outline.Vertices.Any(v => Math.Abs(v.X - e.X) < 1e-6 && Math.Abs(v.Y - e.Y) < 1e-6));

    [Fact]
    public void Straight_wall_between_two_columns_becomes_a_rectangle()
    {
        //  ┤════════├   two faces, ends trimmed at the column faces
        var a = Path((0, 0), (300, 0));
        var b = Path((0, 20), (300, 20));

        var result = Build(a, b);

        var outline = Assert.Single(result.Outlines);
        Assert.True(HasVertices(outline, (0, 0), (300, 0), (300, 20), (0, 20)));
        Assert.Equal(2, outline.CapCount);
        Assert.Equal(new[] { a.Id, b.Id }.OrderBy(i => i), result.UsedPaths.OrderBy(i => i));
    }

    [Fact]
    public void L_corner_without_column_becomes_one_L_shaped_outline()
    {
        var result = Build(
            Path((0, 0), (300, 0)), Path((300, 0), (300, -200)),     // inner faces
            Path((0, 20), (320, 20)), Path((320, 20), (320, -200))); // outer faces

        var outline = Assert.Single(result.Outlines);
        Assert.True(HasVertices(outline, (0, 0), (300, 0), (300, -200), (320, -200), (320, 20), (0, 20)));
    }

    [Fact]
    public void L_corner_with_a_small_gap_is_joined_like_FILLET()
    {
        // The corner lines stop short of each other (sloppy drafting): extended to the corner.
        var result = Build(
            Path((0, 0), (299.5, 0)), Path((300, 0.4), (300, -200)),
            Path((0, 20), (319.6, 20)), Path((320, 19.5), (320, -200)));

        var outline = Assert.Single(result.Outlines);
        Assert.True(HasVertices(outline, (0, 0), (300, 0), (300, -200), (320, -200), (320, 20), (0, 20)));
    }

    [Fact]
    public void L_corner_with_overshooting_lines_is_trimmed_to_the_corner()
    {
        var result = Build(
            Path((0, 0), (303, 0)), Path((300, 3), (300, -200)),
            Path((0, 20), (323, 20)), Path((320, 23), (320, -200)));

        var outline = Assert.Single(result.Outlines);
        Assert.True(HasVertices(outline, (0, 0), (300, 0), (300, -200), (320, -200), (320, 20), (0, 20)));
    }

    [Fact]
    public void Walls_trimmed_at_a_column_corner_are_capped_not_extended_into_the_column()
    {
        // Horizontal wall ends at the column's left face (x = 100), vertical wall at its bottom face (y = -10).
        var result = Build(
            Path((0, 0), (100, 0)), Path((0, 20), (100, 20)),
            Path((110, -10), (110, -200)), Path((130, -10), (130, -200)));

        Assert.Equal(2, result.Outlines.Count);
        Assert.Contains(result.Outlines, o => HasVertices(o, (0, 0), (100, 0), (100, 20), (0, 20)));
    }

    [Fact]
    public void Polyline_faces_are_joined_like_lines()
    {
        var result = Build(Path((0, 0), (300, 0), (300, -200)), Path((0, 20), (320, 20), (320, -200)));

        Assert.Single(result.Outlines);
        Assert.Equal(2, result.UsedPaths.Count);
    }

    [Fact]
    public void Trimmed_T_junction_becomes_one_T_shaped_outline()
    {
        // The main wall's face is broken between the branch faces: the faces continue into each other.
        var result = Build(
            Path((0, 0), (300, 0)),
            Path((0, 20), (140, 20)), Path((160, 20), (300, 20)),
            Path((140, 20), (140, 200)), Path((160, 20), (160, 200)));

        var outline = Assert.Single(result.Outlines);
        Assert.Equal(8, outline.Vertices.Count);
        Assert.Equal(5, outline.SourceIds.Count);
    }

    [Fact]
    public void Wall_running_into_an_untrimmed_face_gets_its_own_outline()
    {
        // The main wall's face is continuous: the branch is closed with a cap lying on that face.
        var result = Build(
            Path((0, 0), (300, 0)), Path((0, 20), (300, 20)),
            Path((140, 20), (140, 200)), Path((160, 20), (160, 200)));

        Assert.Equal(2, result.Outlines.Count);
        Assert.Contains(result.Outlines, o => HasVertices(o, (0, 0), (300, 0), (300, 20), (0, 20)));
        Assert.Contains(result.Outlines, o => HasVertices(o, (140, 20), (160, 20), (160, 200), (140, 200)));
    }

    [Fact]
    public void Door_opening_closes_each_side_separately()
    {
        var result = Build(
            Path((0, 0), (100, 0)), Path((0, 20), (100, 20)),
            Path((190, 0), (300, 0)), Path((190, 20), (300, 20)));

        Assert.Equal(2, result.Outlines.Count);
        Assert.All(result.Outlines, o => Assert.Equal(4, o.Vertices.Count));
    }

    [Fact]
    public void Faces_farther_apart_than_the_max_thickness_are_not_a_wall()
    {
        // A 120 wide corridor between two walls.
        var a = Path((0, 0), (300, 0));
        var b = Path((0, 120), (300, 120));

        var result = Build(a, b);

        Assert.Empty(result.Outlines);
        Assert.Contains(a.Id, result.UnclosedPaths);
        Assert.Contains(result.Notes, n => n.Contains("not part of a closed outline"));
    }

    [Fact]
    public void Single_line_is_left_open()
    {
        var a = Path((0, 0), (300, 0));

        var result = Build(a);

        Assert.Empty(result.Outlines);
        Assert.Contains(a.Id, result.UnclosedPaths);
    }

    [Fact]
    public void Face_with_two_equally_good_partners_is_not_guessed()
    {
        var result = Build(Path((0, 0), (300, 0)), Path((0, 20), (300, 20)), Path((0, -20), (300, -20)));

        Assert.Empty(result.Outlines);
        Assert.Contains(result.Notes, n => n.Contains("more than one matching face"));
    }

    [Fact]
    public void Nearest_face_wins_when_two_walls_end_at_the_same_column_line()
    {
        // Two parallel walls (20 thick) 50 apart, both ending at x = 0.
        var result = Build(
            Path((0, 0), (300, 0)), Path((0, 20), (300, 20)),
            Path((0, 70), (300, 70)), Path((0, 90), (300, 90)));

        Assert.Equal(2, result.Outlines.Count);
        Assert.All(result.Outlines, o => Assert.Equal(4, o.Vertices.Count));
    }

    [Fact]
    public void Closed_wall_ring_gives_an_outer_and_an_inner_outline()
    {
        var result = Build(
            Path((0, 0), (400, 0), (400, 300), (0, 300), (0, 0)),
            Path((20, 20), (380, 20), (380, 280), (20, 280), (20, 20)));

        Assert.Equal(2, result.Outlines.Count); // the inner one is an island for a hatch
    }

    [Fact]
    public void Lines_on_different_layers_are_not_joined()
    {
        var a = new WallPath(_nextId++, "WALL", new[] { new Point3(0, 0, 0), new Point3(300, 0, 0) });
        var b = new WallPath(_nextId++, "FURNITURE", new[] { new Point3(0, 20, 0), new Point3(300, 20, 0) });

        Assert.Empty(new WallOutlineBuilder(new Tolerance(1e-4), 60).Build(new[] { a, b }).Outlines);
    }

    [Fact]
    public void Max_thickness_must_be_positive() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new WallOutlineBuilder(new Tolerance(1e-4), 0));
}

/// <summary>WallTrim with "Create closed polylines": trim in memory, then outlines, then the line edits.</summary>
public class BlockTrimOutlineTests
{
    private static BlockTrimOptions Outlines(bool keep = false) =>
        new() { CreateClosedOutlines = true, MaxWallThickness = 60, KeepOriginalLines = keep };

    private static FakeDuplicateGateway Wall() =>
        new(Make.Line(1, 0, 0, 300, 0, "WALL"), Make.Line(2, 0, 20, 300, 20, "WALL"));

    [Fact]
    public void Wall_lines_are_replaced_by_one_closed_polyline()
    {
        var gateway = Wall();

        var result = new BlockTrimOperation(gateway, Outlines()).Execute(new CleanContext());

        var (source, vertices) = Assert.Single(gateway.AddedPolylines);
        Assert.Equal(4, vertices.Count);
        Assert.Contains(source, new[] { new EntityHandle(1), new EntityHandle(2) });
        Assert.Equal(new[] { new EntityHandle(1), new EntityHandle(2) }, gateway.Erased.OrderBy(h => h.Value));
        Assert.Contains(result.Messages, m => m.Contains("1 closed polyline(s) created"));
    }

    [Fact]
    public void Keep_original_lines_adds_the_polyline_and_erases_nothing()
    {
        var gateway = Wall();

        new BlockTrimOperation(gateway, Outlines(keep: true)).Execute(new CleanContext());

        Assert.Single(gateway.AddedPolylines);
        Assert.Empty(gateway.Erased);
    }

    [Fact]
    public void Preview_changes_nothing_and_reports_the_count()
    {
        var gateway = Wall();

        var result = new BlockTrimOperation(gateway, Outlines()).Execute(new CleanContext(dryRun: true));

        Assert.Empty(gateway.AddedPolylines);
        Assert.Empty(gateway.Erased);
        Assert.Contains(result.Messages, m => m.Contains("1 closed polyline(s) would be created"));
    }

    [Fact]
    public void Lines_are_kept_when_the_polyline_cannot_be_created()
    {
        var gateway = Wall();
        gateway.FailPolylines = true;

        var result = new BlockTrimOperation(gateway, Outlines()).Execute(new CleanContext());

        Assert.Empty(gateway.Erased);
        Assert.Equal(1, result.Failed);
    }

    [Fact]
    public void Lines_on_a_locked_layer_are_not_used()
    {
        var gateway = new FakeDuplicateGateway(
            Make.Line(1, 0, 0, 300, 0, "WALL", locked: true), Make.Line(2, 0, 20, 300, 20, "WALL", locked: true));

        new BlockTrimOperation(gateway, Outlines()).Execute(new CleanContext());

        Assert.Empty(gateway.AddedPolylines);
        Assert.Empty(gateway.Erased);
    }

    [Fact]
    public void Wall_through_a_column_is_trimmed_and_both_sides_are_closed()
    {
        // Column K1: square (100,-10)-(140,30). The wall faces run through it.
        var gateway = new FakeDuplicateGateway(Make.Line(1, 0, 0, 300, 0, "WALL"), Make.Line(2, 0, 20, 300, 20, "WALL"));
        gateway.BlockContent.Add(Make.Poly(500, true, (100, -10, 0), (140, -10, 0), (140, 30, 0), (100, 30, 0)).InBlock("K1"));

        var options = Outlines() with { TrimInsideBlocks = new[] { "K*" } };
        new BlockTrimOperation(gateway, options).Execute(new CleanContext());

        Assert.Equal(2, gateway.AddedPolylines.Count);
        Assert.Contains(gateway.AddedPolylines, p => p.Vertices.All(v => v.X <= 100 + 1e-6));
        Assert.Contains(gateway.AddedPolylines, p => p.Vertices.All(v => v.X >= 140 - 1e-6));

        // Every piece ended up in a polyline, so the original lines are erased rather than trimmed.
        Assert.Equal(2, gateway.Erased.Count);
        Assert.Empty(gateway.Replaced);
    }

    [Fact]
    public void Without_blocks_only_the_outlines_are_built()
    {
        var gateway = Wall();

        var result = new BlockTrimOperation(gateway, Outlines()).Execute(new CleanContext());

        Assert.Single(gateway.AddedPolylines);
        Assert.Contains(result.Messages, m => m.Contains("lines are not trimmed"));
    }
}
