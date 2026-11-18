using CleanTool.Core.Geometry;
using CleanTool.Core.Geometry.Shapes;
using CleanTool.Core.Operations.Duplicates;
using CleanTool.Core.Tests.Fakes;

namespace CleanTool.Core.Tests;

/// <summary>Overlapping blocks (field test result 6).</summary>
public class DuplicateBlockTests
{
    private static ShapeRecord Block(long handle, string definition = "1F",
        double x = 10, double y = 20, double z = 0, double rotation = 0,
        double sx = 1, double sy = 1, double sz = 1, string[]? attributes = null, string layer = "A") =>
        Make.Record(handle, new BlockShape(
            definition,
            new Point3(x, y, z),
            rotation,
            sx, sy, sz,
            new Point3(0, 0, 1),
            attributes ?? Array.Empty<string>()), layer);

    [Fact]
    public void Identical_blocks_are_duplicates()
    {
        var group = Assert.Single(Finders.Create().Find(new[] { Block(1), Block(2) }));
        Assert.Equal(1, group.Keep.Handle.Value);
    }

    [Fact]
    public void Three_identical_blocks_keep_one()
    {
        var group = Assert.Single(Finders.Create().Find(new[] { Block(3), Block(1), Block(2) }));
        Assert.Equal(2, group.Duplicates.Count);
    }

    [Fact]
    public void Different_definitions_are_not_duplicates() =>
        Assert.Empty(Finders.Create().Find(new[] { Block(1, definition: "1F"), Block(2, definition: "2A") }));

    [Fact]
    public void Different_position_is_not_duplicate() =>
        Assert.Empty(Finders.Create().Find(new[] { Block(1), Block(2, x: 11) }));

    [Fact]
    public void Different_elevation_is_not_duplicate() =>
        Assert.Empty(Finders.Create().Find(new[] { Block(1), Block(2, z: 5) }));

    [Fact]
    public void Different_rotation_is_not_duplicate() =>
        Assert.Empty(Finders.Create().Find(new[] { Block(1), Block(2, rotation: Math.PI / 2) }));

    [Fact]
    public void Rotation_0_and_360_degrees_are_the_same() =>
        Assert.Single(Finders.Create().Find(new[] { Block(1, rotation: 0), Block(2, rotation: 2 * Math.PI) }));

    [Fact]
    public void Mirrored_block_is_not_duplicate() =>
        Assert.Empty(Finders.Create().Find(new[] { Block(1), Block(2, sx: -1) }));

    [Fact]
    public void Different_scale_is_not_duplicate() =>
        Assert.Empty(Finders.Create().Find(new[] { Block(1), Block(2, sx: 2, sy: 2) }));

    [Fact]
    public void Same_attributes_are_duplicates() =>
        Assert.Single(Finders.Create().Find(new[]
        {
            Block(1, attributes: new[] { "NO=D1", "W=90" }),
            Block(2, attributes: new[] { "NO=D1", "W=90" })
        }));

    [Fact]
    public void Different_attribute_values_are_not_duplicates() =>
        // Two different door tags at the same spot: both must stay.
        Assert.Empty(Finders.Create().Find(new[]
        {
            Block(1, attributes: new[] { "NO=D1" }),
            Block(2, attributes: new[] { "NO=D2" })
        }));

    [Fact]
    public void Block_on_other_layer_follows_ignore_rules()
    {
        var blocks = new[] { Block(1, layer: "A"), Block(2, layer: "B") };

        Assert.Empty(Finders.Create().Find(blocks));
        Assert.Single(Finders.Create(ignore: Model.ComparedProperty.Layer).Find(blocks));
    }

    [Fact]
    public void Block_and_line_at_same_place_are_different_kinds() =>
        Assert.Empty(Finders.Create().Find(new[] { Block(1, x: 0, y: 0), Make.Line(2, 0, 0, 0, 0) }));
}

/// <summary>Overlapping polylines (field test result 5): different drawing habits can produce the same geometry.</summary>
public class PolylineNormalizeTests
{
    private static readonly (double, double, double)[] Square =
        { (0, 0, 0), (10, 0, 0), (10, 10, 0), (0, 10, 0) };

    [Fact]
    public void Closed_flag_and_snapped_closing_vertex_are_the_same() =>
        // One closed with "Close", the other by snapping back to the start point.
        Assert.Single(Finders.Create().Find(new[]
        {
            Make.Poly(1, true, Square),
            Make.Poly(2, false, (0, 0, 0), (10, 0, 0), (10, 10, 0), (0, 10, 0), (0, 0, 0))
        }));

    [Fact]
    public void Closed_flag_with_extra_closing_vertex_is_the_same() =>
        Assert.Single(Finders.Create().Find(new[]
        {
            Make.Poly(1, true, Square),
            Make.Poly(2, true, (0, 0, 0), (10, 0, 0), (10, 10, 0), (0, 10, 0), (0, 0, 0))
        }));

    [Fact]
    public void Repeated_vertex_is_ignored() =>
        // The same point clicked twice (zero-length segment).
        Assert.Single(Finders.Create().Find(new[]
        {
            Make.Poly(1, false, (0, 0, 0), (10, 0, 0), (10, 10, 0)),
            Make.Poly(2, false, (0, 0, 0), (10, 0, 0), (10, 0, 0), (10, 10, 0))
        }));

    [Fact]
    public void Arc_segment_survives_normalization()
    {
        var shape = new PolylineShape(new[]
        {
            new PolylineVertex(new Point3(0, 0, 0), 0),
            new PolylineVertex(new Point3(10, 0, 0), 0.5),
            new PolylineVertex(new Point3(10, 10, 0), 0),
            new PolylineVertex(new Point3(0, 0, 0), 0)
        }, IsClosed: false);

        var normalized = shape.Normalize(Tolerance.Default);

        Assert.True(normalized.IsClosed);
        Assert.Equal(3, normalized.Vertices.Count);
        Assert.Equal(0.5, normalized.Vertices[1].Bulge);
    }

    [Fact]
    public void Open_two_point_polyline_back_to_start_is_not_closed()
    {
        // A 2-vertex "there and back" polyline is not a closed shape.
        var shape = new PolylineShape(new[]
        {
            new PolylineVertex(new Point3(0, 0, 0), 0),
            new PolylineVertex(new Point3(10, 0, 0), 0)
        }, IsClosed: false);

        Assert.False(shape.Normalize(Tolerance.Default).IsClosed);
    }

    [Fact]
    public void Flattened_3d_polyline_matches_lw_polyline_with_same_points() =>
        // The readers convert 2D/3D/LW polylines into the same model: after Z = 0 these are found as copies too.
        Assert.Single(Finders.Create().Find(new[]
        {
            Make.Poly(1, false, (0, 0, 0), (10, 0, 0), (10, 10, 0)),
            Make.Poly(2, false, (10, 10, 0), (10, 0, 0), (0, 0, 0))
        }));
}
