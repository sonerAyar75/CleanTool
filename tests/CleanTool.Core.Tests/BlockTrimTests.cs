using CleanTool.Core.Geometry;
using CleanTool.Core.Geometry.Shapes;
using CleanTool.Core.Model;
using CleanTool.Core.Operations.BlockTrim;
using CleanTool.Core.Operations.Duplicates;
using CleanTool.Core.Pipeline;
using CleanTool.Core.Tests.Fakes;

namespace CleanTool.Core.Tests;

/// <summary>
/// Column scenario: wall lines running over or through a column block are trimmed at the column face.
/// Column: a (0,0)-(4,4) square, block name "K1".
/// </summary>
public class BlockTrimTests
{
    private static readonly Tolerance Tol = new(1e-6);

    private static ShapeRecord Column(long handle = 500, string name = "K1") =>
        Make.Poly(handle, true, (0, 0, 0), (4, 0, 0), (4, 4, 0), (0, 4, 0)).InBlock(name);

    private static IReadOnlyList<OverlapAction> Run(ShapeRecord loose, string[]? trimInside, params ShapeRecord[] content) =>
        new BlockCoverage(new Tolerance(1e-4), ComparedProperty.All, trimInside).FindCoveredLoose(new[] { loose }, content);

    private static IReadOnlyList<Segment3> Segments(OverlapAction action) =>
        action.Pieces!.Select(p => ((LineShape)p).Segment).ToList();

    private static bool Same(Segment3 s, double x1, double y1, double x2, double y2) =>
        s.IsEqualTo(new Segment3(new Point3(x1, y1, 0), new Point3(x2, y2, 0)), Tol);

    // ---------------------------------------------------------------- inside (closed outline)

    [Fact]
    public void Wall_line_crossing_a_column_is_cut_at_the_column_faces()
    {
        // ────┼────┼────  →  ────┤    ├────
        var action = Assert.Single(Run(Make.Line(1, -5, 2, 9, 2), new[] { "K*" }, Column()));

        Assert.True(action.ShouldReplace);
        var pieces = Segments(action);
        Assert.Equal(2, pieces.Count);
        Assert.True(Same(pieces[0], -5, 2, 0, 2));
        Assert.True(Same(pieces[1], 4, 2, 9, 2));
    }

    [Fact]
    public void Line_ending_inside_a_column_is_shortened()
    {
        var action = Assert.Single(Run(Make.Line(1, -5, 2, 2, 2), new[] { "K*" }, Column()));

        Assert.True(Same(Assert.Single(Segments(action)), -5, 2, 0, 2));
    }

    [Fact]
    public void Line_completely_inside_a_column_is_erased()
    {
        var action = Assert.Single(Run(Make.Line(1, 1, 2, 3, 2), new[] { "K*" }, Column()));

        Assert.True(action.ShouldErase);
    }

    [Fact]
    public void Diagonal_line_through_a_column_is_cut_too()
    {
        var action = Assert.Single(Run(Make.Line(1, -2, -2, 6, 6), new[] { "K*" }, Column()));

        var pieces = Segments(action);
        Assert.True(Same(pieces[0], -2, -2, 0, 0));
        Assert.True(Same(pieces[1], 4, 4, 6, 6));
    }

    [Fact]
    public void Inside_trimming_needs_a_matching_block_name()
    {
        // Blocks such as title-block frames do not match the pattern, so lines inside them are left alone.
        Assert.Empty(Run(Make.Line(1, -5, 2, 9, 2), new[] { "K*" }, Column(name: "FRAME-A1")));
        Assert.Empty(Run(Make.Line(1, -5, 2, 9, 2), trimInside: null, Column()));
    }

    [Fact]
    public void Block_name_patterns_are_case_insensitive()
    {
        Assert.Single(Run(Make.Line(1, -5, 2, 9, 2), new[] { "kolon*" }, Column(name: "KOLON-40x40")));
    }

    [Fact]
    public void Round_column_cuts_the_line_at_the_circle()
    {
        var round = Make.Circle(500, 0, 0, 2).InBlock("K-ROUND");

        var action = Assert.Single(Run(Make.Line(1, -5, 0, 5, 0), new[] { "K*" }, round));

        var pieces = Segments(action);
        Assert.True(Same(pieces[0], -5, 0, -2, 0));
        Assert.True(Same(pieces[1], 2, 0, 5, 0));
    }

    [Fact]
    public void Line_passing_beside_a_column_is_untouched()
    {
        Assert.Empty(Run(Make.Line(1, -5, 6, 9, 6), new[] { "K*" }, Column()));
    }

    [Fact]
    public void Line_at_another_elevation_is_not_trimmed_inside()
    {
        Assert.Empty(Run(Make.Line(1, -5, 2, 9, 2, z1: 3, z2: 3), new[] { "K*" }, Column()));
    }

    // ---------------------------------------------------------------- kenar boyu

    [Fact]
    public void Wall_line_running_along_a_column_face_is_cut_there()
    {
        // The wall face continues along the column's bottom edge: the [0,4] part overlapping the edge is removed.
        // Even with inside trimming off (trimming along edges applies to every block).
        var action = Assert.Single(Run(Make.Line(1, -5, 0, 9, 0), trimInside: null, Column()));

        var pieces = Segments(action);
        Assert.Equal(2, pieces.Count);
        Assert.True(Same(pieces[0], -5, 0, 0, 0));
        Assert.True(Same(pieces[1], 4, 0, 9, 0));
    }

    [Fact]
    public void Edge_and_inside_trimming_combine()
    {
        // Two separate lines: one along the column's bottom edge, one passing through it.
        var along = Assert.Single(Run(Make.Line(1, -5, 0, 9, 0), new[] { "K*" }, Column()));
        var through = Assert.Single(Run(Make.Line(2, -5, 1, 9, 1), new[] { "K*" }, Column()));

        Assert.Equal(2, Segments(along).Count);
        Assert.Equal(2, Segments(through).Count);
    }

    // ---------------------------------------------------------------- polyline

    [Fact]
    public void Wall_polyline_crossing_a_column_is_split_into_two_polylines()
    {
        var wall = Make.Poly(1, false, (-5, 2, 0), (9, 2, 0), (9, 10, 0));

        var action = Assert.Single(Run(wall, new[] { "K*" }, Column()));

        var pieces = action.Pieces!.Cast<PolylineShape>().ToList();
        Assert.Equal(2, pieces.Count);
        Assert.Equal(2, pieces[0].Vertices.Count);                       // (-5,2) → (0,2)
        Assert.True(pieces[0].Vertices[1].Position.IsEqualTo(new Point3(0, 2, 0), Tol));
        Assert.Equal(3, pieces[1].Vertices.Count);                       // (4,2) → (9,2) → (9,10)
        Assert.True(pieces[1].Vertices[0].Position.IsEqualTo(new Point3(4, 2, 0), Tol));
        Assert.True(pieces[1].Vertices[2].Position.IsEqualTo(new Point3(9, 10, 0), Tol));
    }

    [Fact]
    public void Closed_polyline_cut_once_becomes_one_open_polyline()
    {
        // One corner of a closed room polyline enters the column: one open piece remains, joined at the start point.
        var room = Make.Poly(1, true, (2, 2, 0), (20, 2, 0), (20, 20, 0), (2, 20, 0));

        var action = Assert.Single(Run(room, new[] { "K*" }, Column()));

        var piece = Assert.IsType<PolylineShape>(Assert.Single(action.Pieces!));
        Assert.False(piece.IsClosed);
        Assert.True(piece.Vertices[0].Position.IsEqualTo(new Point3(4, 2, 0), Tol));
        Assert.True(piece.Vertices[^1].Position.IsEqualTo(new Point3(2, 4, 0), Tol));
    }

    [Fact]
    public void Arc_segments_are_kept_whole()
    {
        var wall = Make.Poly(1, false, (-5, 2, 0), (9, 2, 0.5), (9, 10, 0));

        var action = Assert.Single(Run(wall, new[] { "K*" }, Column()));

        var last = (PolylineShape)action.Pieces![^1];
        Assert.Equal(0.5, last.Vertices[^2].Bulge);
    }

    // ---------------------------------------------------------------- notes (field: "true" had been typed)

    private static IReadOnlyList<string> Notes(string[]? trimInside, params ShapeRecord[] content)
    {
        var coverage = new BlockCoverage(new Tolerance(1e-4), ComparedProperty.All, trimInside);
        coverage.FindCoveredLoose(new[] { Make.Line(1, -5, 2, 9, 2) }, content);
        return coverage.Notes;
    }

    [Fact]
    public void Pattern_matching_no_block_is_reported_with_available_names()
    {
        var note = Assert.Single(Notes(new[] { "true" }, Column(name: "K1"), Column(501, "FRAME")));

        Assert.Contains("'true' matches no block", note);
        Assert.Contains("FRAME, K1", note);
    }

    [Fact]
    public void Empty_pattern_explains_that_only_edges_are_trimmed()
    {
        var note = Assert.Single(Notes(null, Column(name: "K1")));

        Assert.Contains("only trimmed along block edges", note);
        Assert.Contains("K1", note);
    }

    [Fact]
    public void Matching_pattern_adds_no_note()
    {
        Assert.Empty(Notes(new[] { "K*" }, Column(name: "K1")));
    }

    [Fact]
    public void Blocks_without_closed_outlines_are_not_listed()
    {
        Assert.Empty(Notes(new[] { "X*" }, Make.Line(500, 0, 0, 4, 0).InBlock("DOOR")));
    }

    [Fact]
    public void Operation_puts_notes_in_the_report()
    {
        var gateway = new FakeDuplicateGateway(Make.Line(1, -5, 2, 9, 2));
        gateway.BlockContent.Add(Column());

        var result = new BlockTrimOperation(gateway, new BlockTrimOptions { TrimInsideBlocks = new[] { "true" } })
            .Execute(new CleanContext(dryRun: true));

        Assert.Contains(result.Messages, m => m.Contains("matches no block"));
    }

    // ---------------------------------------------------------------- operation (gateway) level

    [Fact]
    public void Operation_replaces_trimmed_objects_through_the_gateway()
    {
        var gateway = new FakeDuplicateGateway(Make.Line(1, -5, 2, 9, 2));
        gateway.BlockContent.Add(Column());

        var result = new BlockTrimOperation(gateway, new BlockTrimOptions { TrimInsideBlocks = new[] { "K*" } })
            .Execute(new CleanContext());

        Assert.True(gateway.Replaced.ContainsKey(new EntityHandle(1)));
        Assert.Equal(2, gateway.Replaced[new EntityHandle(1)].Count);
        Assert.Empty(gateway.Erased);
        Assert.Equal(1, result.Changed);
        Assert.Equal(0, result.Skipped);
    }

    [Fact]
    public void Dry_run_does_not_replace()
    {
        var gateway = new FakeDuplicateGateway(Make.Line(1, -5, 2, 9, 2));
        gateway.BlockContent.Add(Column());

        var result = new BlockTrimOperation(gateway, new BlockTrimOptions { TrimInsideBlocks = new[] { "K*" } })
            .Execute(new CleanContext(dryRun: true));

        Assert.Empty(gateway.Replaced);
        Assert.Equal(1, result.Changed);
        Assert.Contains(result.Messages, m => m.Contains("would remain"));
    }

    [Fact]
    public void Match_properties_keeps_lines_on_other_layers_and_explains_why()
    {
        var gateway = new FakeDuplicateGateway(Make.Line(1, -5, 2, 9, 2, layer: "A-WALL"));
        gateway.BlockContent.Add(Column());

        var result = new BlockTrimOperation(gateway, new BlockTrimOptions { TrimInsideBlocks = new[] { "K*" }, MatchProperties = true })
            .Execute(new CleanContext());

        Assert.Empty(gateway.Replaced);
        Assert.Contains(result.Messages, m => m.Contains("differs"));
    }

    [Fact]
    public void Without_blocks_in_the_selection_the_report_says_so()
    {
        var gateway = new FakeDuplicateGateway(Make.Line(1, -5, 2, 9, 2));

        var result = new BlockTrimOperation(gateway).Execute(new CleanContext());

        Assert.Contains(result.Messages, m => m.Contains("No blocks in the selection"));
    }

    [Fact]
    public void Locked_wall_line_is_not_trimmed()
    {
        var gateway = new FakeDuplicateGateway(Make.Line(1, -5, 2, 9, 2, locked: true));
        gateway.BlockContent.Add(Column());

        var result = new BlockTrimOperation(gateway, new BlockTrimOptions { TrimInsideBlocks = new[] { "K*" } })
            .Execute(new CleanContext());

        Assert.Empty(gateway.Replaced);
        Assert.Equal(1, result.Skipped);
    }
}
