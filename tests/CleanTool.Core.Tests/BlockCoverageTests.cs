using CleanTool.Core.Geometry;
using CleanTool.Core.Geometry.Shapes;
using CleanTool.Core.Model;
using CleanTool.Core.Operations.BlockTrim;
using CleanTool.Core.Operations.Duplicates;
using CleanTool.Core.Tests.Fakes;

namespace CleanTool.Core.Tests;

/// <summary>Block coverage: loose objects fully or partly covered by block content (the lines left over in the field test).</summary>
public class BlockCoverageTests
{
    private static IReadOnlyList<OverlapAction> Covered(ShapeRecord[] loose, params ShapeRecord[] content) =>
        new BlockCoverage(new Tolerance(1e-4), ComparedProperty.All).FindCoveredLoose(loose, content);

    /// <summary>Sadece "tamamen silinen" kararlar.</summary>
    private static IReadOnlyList<OverlapAction> Erased(ShapeRecord[] loose, params ShapeRecord[] content) =>
        Covered(loose, content).Where(a => a.ShouldErase).ToList();

    private static ShapeRecord Content(ShapeRecord record) => record.InBlock();

    [Fact]
    public void Exact_copy_is_covered() =>
        Assert.Single(Covered(new[] { Make.Line(1, 0, 0, 10, 0) }, Content(Make.Line(500, 0, 0, 10, 0))));

    [Fact]
    public void Reversed_copy_is_covered() =>
        Assert.Single(Covered(new[] { Make.Line(1, 10, 0, 0, 0) }, Content(Make.Line(500, 0, 0, 10, 0))));

    [Fact]
    public void Part_of_a_block_line_is_covered() =>
        Assert.Single(Covered(new[] { Make.Line(1, 2, 0, 7, 0) }, Content(Make.Line(500, 0, 0, 10, 0))));

    [Fact]
    public void Line_spanning_two_collinear_block_lines_is_covered() =>
        Assert.Single(Covered(new[] { Make.Line(1, 0, 0, 10, 0) },
            Content(Make.Line(500, 0, 0, 6, 0)),
            Content(Make.Line(501, 4, 0, 10, 0))));

    [Fact]
    public void Line_over_a_gap_is_not_erased_but_trimmed_to_the_gap() =>
        // No block line in the gap: the line is not erased, only its two ends overlapping the block are trimmed.
        Assert.Empty(Erased(new[] { Make.Line(1, 0, 0, 10, 0) },
            Content(Make.Line(500, 0, 0, 4, 0)),
            Content(Make.Line(501, 6, 0, 10, 0))));

    [Fact]
    public void Line_extending_beyond_the_block_is_not_erased() =>
        Assert.Empty(Erased(new[] { Make.Line(1, 0, 0, 15, 0) }, Content(Make.Line(500, 0, 0, 10, 0))));

    [Fact]
    public void Parallel_offset_line_is_not_covered() =>
        Assert.Empty(Covered(new[] { Make.Line(1, 0, 0.01, 10, 0.01) }, Content(Make.Line(500, 0, 0, 10, 0))));

    [Fact]
    public void Line_on_a_block_polyline_segment_is_covered() =>
        Assert.Single(Covered(new[] { Make.Line(1, 10, 0, 10, 10) },
            Content(Make.Poly(500, true, (0, 0, 0), (10, 0, 0), (10, 10, 0), (0, 10, 0)))));

    [Fact]
    public void Loose_polyline_over_block_lines_is_covered() =>
        Assert.Single(Covered(new[] { Make.Poly(1, false, (0, 0, 0), (10, 0, 0), (10, 10, 0)) },
            Content(Make.Line(500, 0, 0, 10, 0)),
            Content(Make.Line(501, 10, 0, 10, 10))));

    [Fact]
    public void Loose_polyline_partly_outside_the_block_is_not_erased() =>
        Assert.Empty(Erased(new[] { Make.Poly(1, false, (0, 0, 0), (10, 0, 0), (10, 10, 0)) },
            Content(Make.Line(500, 0, 0, 10, 0))));

    [Fact]
    public void Arc_matching_a_block_polyline_arc_segment_is_covered()
    {
        // Blok: (10,0)'dan (0,10)'a, merkez orijin, 90° CCW yay → bulge = tan(90°/4).
        var bulge = Math.Tan(Math.PI / 8);
        var block = Content(Make.Poly(500, false, (10, 0, bulge), (0, 10, 0)));
        var arc = Make.Arc(1, 10, 0, 90);

        Assert.Single(Covered(new[] { arc }, block));
    }

    [Fact]
    public void Arc_bulging_the_other_way_is_not_covered()
    {
        var bulge = Math.Tan(Math.PI / 8);
        var block = Content(Make.Poly(500, false, (10, 0, -bulge), (0, 10, 0)));

        Assert.Empty(Covered(new[] { Make.Arc(1, 10, 0, 90) }, block));
    }

    [Fact]
    public void Circle_matching_a_block_circle_is_covered() =>
        Assert.Single(Covered(new[] { Make.Circle(1, 5, 5, 2) }, Content(Make.Circle(500, 5, 5, 2))));

    [Fact]
    public void Content_in_another_space_does_not_cover() =>
        Assert.Empty(Covered(new[] { Make.Line(1, 0, 0, 10, 0).InOwner(10) }, Content(Make.Line(500, 0, 0, 10, 0).InOwner(20))));

    [Fact]
    public void Report_names_the_covering_block()
    {
        var action = Assert.Single(Covered(new[] { Make.Line(1, 0, 0, 10, 0) }, Make.Line(500, 0, 0, 10, 0).InBlock("WINDOW-120")));

        Assert.True(action.ShouldErase);
        Assert.Contains("WINDOW-120", action.Label);
    }

    [Fact]
    public void Arc_edge_conversion_gives_ccw_positive_bulge()
    {
        var edge = ArcGeometry.ToEdge(new ArcShape(Point3.Origin, 10,
            new Point3(10, 0, 0), new Point3(0, 10, 0), new Point3(10 * Math.Cos(Math.PI / 4), 10 * Math.Sin(Math.PI / 4), 0)));

        Assert.Equal(Math.Tan(Math.PI / 8), edge.Bulge, 9);
    }

    [Fact]
    public void Arc_edge_conversion_handles_major_arcs()
    {
        // 0°→270° CCW (midpoint at 135°): a 270° sweep.
        var edge = ArcGeometry.ToEdge(new ArcShape(Point3.Origin, 10,
            new Point3(10, 0, 0), new Point3(0, -10, 0), new Point3(10 * Math.Cos(3 * Math.PI / 4), 10 * Math.Sin(3 * Math.PI / 4), 0)));

        Assert.Equal(Math.Tan(3 * Math.PI / 8), edge.Bulge, 9);
    }
}
