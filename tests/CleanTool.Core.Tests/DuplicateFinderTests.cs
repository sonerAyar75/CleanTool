using CleanTool.Core.Geometry;
using CleanTool.Core.Model;
using CleanTool.Core.Operations.Duplicates;
using CleanTool.Core.Tests.Fakes;

namespace CleanTool.Core.Tests;

public static class Finders
{
    public static DuplicateFinder Create(double tol = 1e-4, ComparedProperty ignore = ComparedProperty.None) =>
        new(new DuplicateOptions { Tolerance = new Tolerance(tol), IgnoredProperties = ignore });

    /// <summary>One sample of each supported kind, so the layer tests run the same scenario for all kinds.</summary>
    public static ShapeRecord Sample(string kind, long handle, string layer, bool locked = false) => kind switch
    {
        "Line" => Make.Line(handle, 0, 0, 10, 0, layer, locked),
        "Circle" => Make.Circle(handle, 5, 5, 3, layer, locked),
        "Arc" => Make.Record(handle, Make.Arc(handle, 10, 0, 90).Shape, layer, locked),
        "Polyline" => Make.Record(handle, Make.Poly(handle, true, (0, 0, 0), (10, 0, 0), (10, 10, 0)).Shape, layer, locked),
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    public static IEnumerable<object[]> AllKinds() =>
        new[] { "Line", "Circle", "Arc", "Polyline" }.Select(k => new object[] { k });
}

/// <summary>Layer and lock rules — the same behavior is expected for every object kind.</summary>
public class DuplicateFinderLayerTests
{
    [Theory]
    [MemberData(nameof(Finders.AllKinds), MemberType = typeof(Finders))]
    public void Same_layer_copies_are_duplicates(string kind)
    {
        var groups = Finders.Create().Find(new[] { Finders.Sample(kind, 1, "A"), Finders.Sample(kind, 2, "A") });

        Assert.Single(groups);
    }

    [Theory]
    [MemberData(nameof(Finders.AllKinds), MemberType = typeof(Finders))]
    public void Different_layer_copies_are_kept_by_default(string kind)
    {
        var groups = Finders.Create().Find(new[] { Finders.Sample(kind, 1, "A"), Finders.Sample(kind, 2, "B") });

        Assert.Empty(groups);
    }

    [Theory]
    [MemberData(nameof(Finders.AllKinds), MemberType = typeof(Finders))]
    public void Different_layer_copies_match_when_layer_is_ignored(string kind)
    {
        var groups = Finders.Create(ignore: ComparedProperty.Layer).Find(new[] { Finders.Sample(kind, 1, "A"), Finders.Sample(kind, 2, "B") });

        Assert.Single(groups);
    }

    [Theory]
    [MemberData(nameof(Finders.AllKinds), MemberType = typeof(Finders))]
    public void Layer_names_are_case_insensitive(string kind)
    {
        var groups = Finders.Create().Find(new[] { Finders.Sample(kind, 1, "Wall"), Finders.Sample(kind, 2, "WALL") });

        Assert.Single(groups);
    }

    [Fact]
    public void Mixed_layers_only_group_copies_on_the_same_layer()
    {
        // 2 copies on A, 2 copies on B, a single one on C: two separate groups, the one on C is untouched.
        var groups = Finders.Create().Find(new[]
        {
            Make.Line(1, 0, 0, 10, 0, "A"),
            Make.Line(2, 0, 0, 10, 0, "B"),
            Make.Line(3, 0, 0, 10, 0, "A"),
            Make.Line(4, 0, 0, 10, 0, "C"),
            Make.Line(5, 10, 0, 0, 0, "B")
        });

        Assert.Equal(2, groups.Count);
        Assert.Contains(groups, g => g.Keep.Handle.Value == 1 && g.Duplicates.Single().Handle.Value == 3);
        Assert.Contains(groups, g => g.Keep.Handle.Value == 2 && g.Duplicates.Single().Handle.Value == 5);
        Assert.DoesNotContain(groups, g => g.Keep.Handle.Value == 4 || g.Duplicates.Any(d => d.Handle.Value == 4));
    }

    [Fact]
    public void Copies_across_layers_form_one_group_when_layer_is_ignored()
    {
        var groups = Finders.Create(ignore: ComparedProperty.Layer).Find(new[]
        {
            Make.Circle(30, 0, 0, 5, "C"),
            Make.Circle(10, 0, 0, 5, "A"),
            Make.Circle(20, 0, 0, 5, "B")
        });

        var group = Assert.Single(groups);
        Assert.Equal(10, group.Keep.Handle.Value);
        Assert.Equal(new long[] { 20, 30 }, group.Duplicates.Select(d => d.Handle.Value));
    }

    [Fact]
    public void Locked_copy_is_kept_even_if_it_is_newer()
    {
        var groups = Finders.Create().Find(new[] { Make.Circle(1, 0, 0, 5), Make.Circle(2, 0, 0, 5, locked: true) });

        Assert.Equal(2, Assert.Single(groups).Keep.Handle.Value);
    }

    [Fact]
    public void Locked_copy_on_other_layer_is_kept_when_layer_is_ignored()
    {
        var groups = Finders.Create(ignore: ComparedProperty.Layer).Find(new[]
        {
            Make.Line(1, 0, 0, 10, 0, "A"),
            Make.Line(2, 0, 0, 10, 0, "LOCKED", locked: true)
        });

        var group = Assert.Single(groups);
        Assert.Equal(2, group.Keep.Handle.Value);
        Assert.Equal(1, group.Duplicates.Single().Handle.Value);
    }
}

public class DuplicateFinderKindTests
{
    [Fact]
    public void Different_kinds_are_never_duplicates()
    {
        // A single-segment polyline and a line at the same place are different kinds.
        var groups = Finders.Create().Find(new[]
        {
            Make.Line(1, 0, 0, 10, 0),
            Make.Poly(2, false, (0, 0, 0), (10, 0, 0))
        });

        Assert.Empty(groups);
    }

    [Fact]
    public void Empty_input_returns_no_groups()
    {
        Assert.Empty(Finders.Create().Find(Array.Empty<ShapeRecord>()));
    }

    [Fact]
    public void Unknown_shape_kind_is_ignored()
    {
        var finder = new DuplicateFinder(comparers: new ShapeComparerRegistry(new IShapeComparer[] { new LineComparer() }));

        var groups = finder.Find(new[] { Make.Circle(1, 0, 0, 5), Make.Circle(2, 0, 0, 5) });

        Assert.Empty(groups);
    }
}

public class DuplicateFinderLineTests
{
    [Fact]
    public void Reversed_lines_are_duplicates() =>
        Assert.Single(Finders.Create().Find(new[] { Make.Line(1, 0, 0, 10, 5), Make.Line(2, 10, 5, 0, 0) }));

    [Fact]
    public void Lines_within_tolerance_are_duplicates() =>
        Assert.Single(Finders.Create(tol: 0.01).Find(new[] { Make.Line(1, 0, 0, 10, 0), Make.Line(2, 0.005, 0, 10, 0.005) }));

    [Fact]
    public void Lines_outside_tolerance_are_not_duplicates() =>
        Assert.Empty(Finders.Create(tol: 0.001).Find(new[] { Make.Line(1, 0, 0, 10, 0), Make.Line(2, 0.01, 0, 10, 0) }));

    [Fact]
    public void Lines_with_different_z_are_not_duplicates() =>
        Assert.Empty(Finders.Create().Find(new[] { Make.Line(1, 0, 0, 10, 0), Make.Line(2, 0, 0, 10, 0, z1: 5, z2: 5) }));

    [Fact]
    public void Partially_overlapping_lines_are_not_duplicates() =>
        Assert.Empty(Finders.Create().Find(new[] { Make.Line(1, 0, 0, 10, 0), Make.Line(2, 0, 0, 5, 0) }));

    [Fact]
    public void Three_copies_form_one_group_keeping_lowest_handle()
    {
        var group = Assert.Single(Finders.Create().Find(new[]
        {
            Make.Line(30, 0, 0, 10, 0),
            Make.Line(10, 10, 0, 0, 0),
            Make.Line(20, 0, 0, 10, 0)
        }));

        Assert.Equal(10, group.Keep.Handle.Value);
        Assert.Equal(new long[] { 20, 30 }, group.Duplicates.Select(d => d.Handle.Value));
    }

    [Fact]
    public void Many_lines_on_same_x_only_match_real_copies()
    {
        var lines = Enumerable.Range(0, 50).Select(i => Make.Line(i + 1, 0, i * 10, 0, i * 10 + 5)).ToList();
        lines.Add(Make.Line(999, 0, 0, 0, 5));

        var group = Assert.Single(Finders.Create().Find(lines));
        Assert.Equal(1, group.Keep.Handle.Value);
        Assert.Equal(999, group.Duplicates.Single().Handle.Value);
    }
}

public class DuplicateFinderCircleTests
{
    [Fact]
    public void Same_center_and_radius_are_duplicates() =>
        Assert.Single(Finders.Create().Find(new[] { Make.Circle(1, 3, 4, 5), Make.Circle(2, 3, 4, 5) }));

    [Fact]
    public void Concentric_circles_with_different_radius_are_not_duplicates() =>
        Assert.Empty(Finders.Create().Find(new[] { Make.Circle(1, 0, 0, 5), Make.Circle(2, 0, 0, 6) }));

    [Fact]
    public void Same_radius_different_center_are_not_duplicates() =>
        Assert.Empty(Finders.Create().Find(new[] { Make.Circle(1, 0, 0, 5), Make.Circle(2, 0, 1, 5) }));

    [Fact]
    public void Circles_within_tolerance_are_duplicates() =>
        Assert.Single(Finders.Create(tol: 0.01).Find(new[] { Make.Circle(1, 0, 0, 5), Make.Circle(2, 0.005, 0, 5.005) }));

    [Fact]
    public void Circles_at_different_z_are_not_duplicates() =>
        Assert.Empty(Finders.Create().Find(new[] { Make.Circle(1, 0, 0, 5), Make.Circle(2, 0, 0, 5, cz: 3) }));
}

public class DuplicateFinderArcTests
{
    [Fact]
    public void Identical_arcs_are_duplicates() =>
        Assert.Single(Finders.Create().Find(new[] { Make.Arc(1, 10, 0, 90), Make.Arc(2, 10, 0, 90) }));

    [Fact]
    public void Same_arc_with_swapped_ends_is_duplicate() =>
        Assert.Single(Finders.Create().Find(new[] { Make.Arc(1, 10, 0, 90), Make.Arc(2, 10, 0, 90, reversed: true) }));

    [Fact]
    public void Minor_and_major_arc_with_same_ends_are_not_duplicates() =>
        // 0°→90° minor arc, 90°→360° major arc: same ends, different midpoints.
        Assert.Empty(Finders.Create().Find(new[] { Make.Arc(1, 10, 0, 90), Make.Arc(2, 10, 90, 360) }));

    [Fact]
    public void Arcs_with_different_span_are_not_duplicates() =>
        Assert.Empty(Finders.Create().Find(new[] { Make.Arc(1, 10, 0, 90), Make.Arc(2, 10, 0, 120) }));
}

public class DuplicateFinderPolylineTests
{
    private static readonly (double, double, double)[] Square =
        { (0, 0, 0), (10, 0, 0), (10, 10, 0), (0, 10, 0) };

    [Fact]
    public void Identical_open_polylines_are_duplicates() =>
        Assert.Single(Finders.Create().Find(new[]
        {
            Make.Poly(1, false, (0, 0, 0), (10, 0, 0), (10, 10, 0)),
            Make.Poly(2, false, (0, 0, 0), (10, 0, 0), (10, 10, 0))
        }));

    [Fact]
    public void Reversed_open_polyline_is_duplicate() =>
        Assert.Single(Finders.Create().Find(new[]
        {
            Make.Poly(1, false, (0, 0, 0), (10, 0, 0), (10, 10, 0)),
            Make.Poly(2, false, (10, 10, 0), (10, 0, 0), (0, 0, 0))
        }));

    [Fact]
    public void Closed_polyline_with_different_start_vertex_is_duplicate() =>
        Assert.Single(Finders.Create().Find(new[]
        {
            Make.Poly(1, true, Square),
            Make.Poly(2, true, (10, 10, 0), (0, 10, 0), (0, 0, 0), (10, 0, 0))
        }));

    [Fact]
    public void Closed_polyline_drawn_in_opposite_direction_is_duplicate() =>
        Assert.Single(Finders.Create().Find(new[]
        {
            Make.Poly(1, true, Square),
            Make.Poly(2, true, (0, 0, 0), (0, 10, 0), (10, 10, 0), (10, 0, 0))
        }));

    [Fact]
    public void Open_and_closed_polylines_are_not_duplicates() =>
        Assert.Empty(Finders.Create().Find(new[] { Make.Poly(1, true, Square), Make.Poly(2, false, Square) }));

    [Fact]
    public void Different_vertex_count_is_not_duplicate() =>
        Assert.Empty(Finders.Create().Find(new[]
        {
            Make.Poly(1, false, (0, 0, 0), (10, 0, 0)),
            Make.Poly(2, false, (0, 0, 0), (5, 0, 0), (10, 0, 0))
        }));

    [Fact]
    public void Same_vertices_different_shape_is_not_duplicate() =>
        // The same 4 points connected in a different order (bow-tie shape).
        Assert.Empty(Finders.Create().Find(new[]
        {
            Make.Poly(1, true, Square),
            Make.Poly(2, true, (0, 0, 0), (10, 10, 0), (10, 0, 0), (0, 10, 0))
        }));

    [Fact]
    public void Reversed_polyline_with_arc_segment_is_duplicate() =>
        // Drawn in the opposite direction, the same arc has a bulge of the opposite sign.
        Assert.Single(Finders.Create().Find(new[]
        {
            Make.Poly(1, false, (0, 0, 0.5), (10, 0, 0)),
            Make.Poly(2, false, (10, 0, -0.5), (0, 0, 0))
        }));

    [Fact]
    public void Mirrored_arc_segment_is_not_duplicate() =>
        Assert.Empty(Finders.Create().Find(new[]
        {
            Make.Poly(1, false, (0, 0, 0.5), (10, 0, 0)),
            Make.Poly(2, false, (0, 0, -0.5), (10, 0, 0))
        }));

    [Fact]
    public void Polylines_at_different_elevation_are_not_duplicates() =>
        Assert.Empty(Finders.Create().Find(new[]
        {
            Make.PolyAtZ(1, "A", true, 0, Square),
            Make.PolyAtZ(2, "A", true, 5, Square)
        }));

    [Fact]
    public void Polylines_within_tolerance_are_duplicates() =>
        Assert.Single(Finders.Create(tol: 0.01).Find(new[]
        {
            Make.Poly(1, false, (0, 0, 0), (10, 0, 0)),
            Make.Poly(2, false, (0.005, 0, 0), (10, 0.005, 0))
        }));
}
