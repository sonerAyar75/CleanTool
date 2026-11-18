using CleanTool.Core.Geometry;
using CleanTool.Core.Geometry.Shapes;
using CleanTool.Core.Model;
using CleanTool.Core.Operations.Duplicates;
using CleanTool.Core.Pipeline;
using CleanTool.Core.Tests.Fakes;

namespace CleanTool.Core.Tests;

/// <summary>Block content overlapping loose objects drawn on top of the block (Duplicate operation).</summary>
public class BlockOverlapOperationTests
{
    private static DuplicateOperation Operation(FakeDuplicateGateway gateway, BlockOverlapMode mode,
        ComparedProperty ignore = ComparedProperty.None, bool matchProperties = false) =>
        new(gateway, new DuplicateOptions { BlockOverlap = mode, IgnoredProperties = ignore, MatchPropertiesForBlockOverlap = matchProperties });

    [Fact]
    public void Remove_loose_object_erases_the_loose_copy_and_keeps_block_content()
    {
        var gateway = new FakeDuplicateGateway(Make.Line(1, 0, 0, 10, 0));
        gateway.BlockContent.Add(Make.Line(500, 0, 0, 10, 0).InBlock());

        var result = Operation(gateway, BlockOverlapMode.RemoveLooseObject).Execute(new CleanContext());

        Assert.Equal(new[] { new EntityHandle(1) }, gateway.Erased);
        Assert.Equal(1, result.Changed);
    }

    [Fact]
    public void Default_mode_is_remove_loose_object()
    {
        Assert.Equal(BlockOverlapMode.RemoveLooseObject, new DuplicateOptions().BlockOverlap);
    }

    [Fact]
    public void Dont_compare_never_asks_for_block_content()
    {
        var gateway = new FakeDuplicateGateway(Make.Line(1, 0, 0, 10, 0));
        gateway.BlockContent.Add(Make.Line(500, 0, 0, 10, 0).InBlock());

        Operation(gateway, BlockOverlapMode.DontCompare).Execute(new CleanContext());

        Assert.Empty(gateway.Erased);
        Assert.Equal(0, gateway.BlockContentRequests);
    }

    [Fact]
    public void Remove_from_block_erases_the_definition_entity_when_safe()
    {
        var gateway = new FakeDuplicateGateway(Make.Line(1, 0, 0, 10, 0));
        gateway.BlockContent.Add(Make.Line(500, 0, 0, 10, 0).InBlock());

        Operation(gateway, BlockOverlapMode.RemoveFromBlock).Execute(new CleanContext());

        Assert.Equal(new[] { new EntityHandle(500) }, gateway.Erased);
    }

    [Fact]
    public void Remove_from_block_skips_blocks_used_more_than_once()
    {
        var gateway = new FakeDuplicateGateway(Make.Line(1, 0, 0, 10, 0));
        gateway.BlockContent.Add(Make.Line(500, 0, 0, 10, 0).InBlock(cannotRemove: "block is used 4 times"));

        var result = Operation(gateway, BlockOverlapMode.RemoveFromBlock).Execute(new CleanContext());

        Assert.Empty(gateway.Erased);
        Assert.Equal(1, result.Skipped);
        Assert.Contains(result.Messages, m => m.Contains("used 4 times"));
    }

    [Fact]
    public void Locked_loose_object_is_skipped()
    {
        var gateway = new FakeDuplicateGateway(Make.Line(1, 0, 0, 10, 0, locked: true));
        gateway.BlockContent.Add(Make.Line(500, 0, 0, 10, 0).InBlock());

        var result = Operation(gateway, BlockOverlapMode.RemoveLooseObject).Execute(new CleanContext());

        Assert.Empty(gateway.Erased);
        Assert.Equal(1, result.Skipped);
    }

    [Fact]
    public void By_default_block_overlap_ignores_property_differences()
    {
        // Field bug: block content on layer 0 → the insert's layer (DOOR), the loose line on WALL. It must still be removed.
        var gateway = new FakeDuplicateGateway(Make.Line(1, 0, 0, 10, 0, layer: "WALL"));
        gateway.BlockContent.Add(Make.Line(500, 0, 0, 10, 0, layer: "DOOR").InBlock());

        Operation(gateway, BlockOverlapMode.RemoveLooseObject).Execute(new CleanContext());

        Assert.Equal(new[] { new EntityHandle(1) }, gateway.Erased);
    }

    [Fact]
    public void Match_properties_keeps_loose_object_on_other_layer()
    {
        var gateway = new FakeDuplicateGateway(Make.Line(1, 0, 0, 10, 0, layer: "WALL"));
        gateway.BlockContent.Add(Make.Line(500, 0, 0, 10, 0, layer: "DOOR").InBlock());

        Operation(gateway, BlockOverlapMode.RemoveLooseObject, matchProperties: true).Execute(new CleanContext());

        Assert.Empty(gateway.Erased);
    }

    [Fact]
    public void Duplicates_only_removes_exact_copies_of_block_content()
    {
        // Only part of the block's line: that is WallTrim's job now, Duplicate leaves it alone.
        var gateway = new FakeDuplicateGateway(Make.Line(1, 0, 0, 5, 0));
        gateway.BlockContent.Add(Make.Line(500, 0, 0, 10, 0).InBlock());

        Operation(gateway, BlockOverlapMode.RemoveLooseObject).Execute(new CleanContext());

        Assert.Empty(gateway.Erased);
    }

    [Fact]
    public void Match_properties_uses_the_ignore_rules()
    {
        var gateway = new FakeDuplicateGateway(Make.Line(1, 0, 0, 10, 0, layer: "WALL"));
        gateway.BlockContent.Add(Make.Line(500, 0, 0, 10, 0, layer: "DOOR").InBlock());

        Operation(gateway, BlockOverlapMode.RemoveLooseObject, ComparedProperty.Layer, matchProperties: true).Execute(new CleanContext());

        Assert.Equal(new[] { new EntityHandle(1) }, gateway.Erased);
    }

    [Fact]
    public void Remove_from_block_also_ignores_properties_by_default()
    {
        var gateway = new FakeDuplicateGateway(Make.Line(1, 0, 0, 10, 0, layer: "WALL"));
        gateway.BlockContent.Add(Make.Line(500, 0, 0, 10, 0, layer: "DOOR").InBlock());

        Operation(gateway, BlockOverlapMode.RemoveFromBlock).Execute(new CleanContext());

        Assert.Equal(new[] { new EntityHandle(500) }, gateway.Erased);
    }

    [Fact]
    public void Remove_from_block_needs_an_exact_copy_not_just_coverage()
    {
        // The loose line is only part of the block's line: erasing from the block would lose geometry.
        var gateway = new FakeDuplicateGateway(Make.Line(1, 0, 0, 5, 0));
        gateway.BlockContent.Add(Make.Line(500, 0, 0, 10, 0).InBlock());

        Operation(gateway, BlockOverlapMode.RemoveFromBlock).Execute(new CleanContext());

        Assert.Empty(gateway.Erased);
    }

    [Fact]
    public void Content_in_other_space_does_not_match()
    {
        var gateway = new FakeDuplicateGateway(Make.Line(1, 0, 0, 10, 0).InOwner(10));
        gateway.BlockContent.Add(Make.Line(500, 0, 0, 10, 0).InOwner(20).InBlock());

        Operation(gateway, BlockOverlapMode.RemoveLooseObject).Execute(new CleanContext());

        Assert.Empty(gateway.Erased);
    }

    [Fact]
    public void Loose_copies_are_counted_once_across_both_steps()
    {
        // Two loose copies + block content: step 1 erases one, step 2 erases the other. Two in total, no double counting.
        var gateway = new FakeDuplicateGateway(Make.Line(1, 0, 0, 10, 0), Make.Line(2, 0, 0, 10, 0));
        gateway.BlockContent.Add(Make.Line(500, 0, 0, 10, 0).InBlock());

        var result = Operation(gateway, BlockOverlapMode.RemoveLooseObject).Execute(new CleanContext());

        Assert.Equal(new long[] { 1, 2 }, gateway.Erased.Select(h => h.Value).OrderBy(v => v));
        Assert.Equal(2, result.Changed);
    }

    [Fact]
    public void Overlapping_content_of_two_blocks_is_left_alone()
    {
        var gateway = new FakeDuplicateGateway();
        gateway.BlockContent.Add(Make.Line(500, 0, 0, 10, 0).InBlock("A", 900));
        gateway.BlockContent.Add(Make.Line(600, 0, 0, 10, 0).InBlock("B", 901));

        var result = Operation(gateway, BlockOverlapMode.RemoveFromBlock).Execute(new CleanContext());

        Assert.Empty(gateway.Erased);
        Assert.Equal(OperationStatus.NothingToDo, result.Status);
    }

    [Fact]
    public void Dry_run_reports_without_erasing()
    {
        var gateway = new FakeDuplicateGateway(Make.Circle(1, 5, 5, 2));
        gateway.BlockContent.Add(Make.Circle(500, 5, 5, 2).InBlock());

        var result = Operation(gateway, BlockOverlapMode.RemoveLooseObject).Execute(new CleanContext(dryRun: true));

        Assert.Empty(gateway.Erased);
        Assert.Equal(1, result.Changed);
    }
}

/// <summary>Transformations that bring block content into world coordinates.</summary>
public class TransformTests
{
    private static readonly Tolerance Tol = new(1e-9);

    [Fact]
    public void Insert_transform_moves_rotates_and_scales_points()
    {
        var t = Transform3.Insert(100, 50, 0, Math.PI / 2, 2, 2);

        var p = t.Apply(new Point3(1, 0, 0));

        Assert.True(p.IsEqualTo(new Point3(100, 52, 0), Tol));
    }

    [Fact]
    public void Multiply_applies_inner_transform_first()
    {
        var outer = Transform3.Translation(100, 0, 0);
        var inner = Transform3.Insert(0, 0, 0, Math.PI, 1, 1);

        var p = outer.Multiply(inner).Apply(new Point3(1, 0, 0));

        Assert.True(p.IsEqualTo(new Point3(99, 0, 0), Tol));
    }

    [Theory]
    [InlineData(1, 1, true)]
    [InlineData(2, 2, true)]
    [InlineData(-1, 1, true)]   // mirrored but still a similarity
    [InlineData(2, 1, false)]   // non-uniform scale
    public void Planar_similarity_detection(double sx, double sy, bool expected)
    {
        Assert.Equal(expected, Transform3.Insert(0, 0, 0, 0.3, sx, sy).IsPlanarSimilarity(out _, out _));
    }

    [Fact]
    public void Circle_is_scaled_uniformly()
    {
        var circle = (CircleShape)new CircleShape(new Point3(1, 0, 0), 2).TryTransform(Transform3.Insert(10, 0, 0, 0, 3, 3))!;

        Assert.True(circle.Center.IsEqualTo(new Point3(13, 0, 0), Tol));
        Assert.Equal(6, circle.Radius, 9);
    }

    [Fact]
    public void Circle_with_non_uniform_scale_cannot_be_compared()
    {
        Assert.Null(new CircleShape(Point3.Origin, 2).TryTransform(Transform3.Insert(0, 0, 0, 0, 2, 1)));
    }

    [Fact]
    public void Straight_polyline_survives_non_uniform_scale()
    {
        var shape = new PolylineShape(new[] { new PolylineVertex(Point3.Origin, 0), new PolylineVertex(new Point3(1, 1, 0), 0) }, false);

        var result = (PolylineShape)shape.TryTransform(Transform3.Insert(0, 0, 0, 0, 2, 1))!;

        Assert.True(result.Vertices[1].Position.IsEqualTo(new Point3(2, 1, 0), Tol));
    }

    [Fact]
    public void Mirroring_flips_polyline_bulge()
    {
        var shape = new PolylineShape(new[] { new PolylineVertex(Point3.Origin, 0.5), new PolylineVertex(new Point3(10, 0, 0), 0) }, false);

        var result = (PolylineShape)shape.TryTransform(Transform3.Insert(0, 0, 0, 0, -1, 1))!;

        Assert.Equal(-0.5, result.Vertices[0].Bulge);
    }

    [Fact]
    public void Polyline_with_arcs_and_non_uniform_scale_cannot_be_compared()
    {
        var shape = new PolylineShape(new[] { new PolylineVertex(Point3.Origin, 0.5), new PolylineVertex(new Point3(10, 0, 0), 0) }, false);

        Assert.Null(shape.TryTransform(Transform3.Insert(0, 0, 0, 0, 2, 1)));
    }

    [Fact]
    public void Transformed_block_line_matches_world_line()
    {
        // A (0,0)-(1,0) line inside the block; the block is inserted at (100,50), rotated 90° and scaled 2x.
        var content = Make.Record(500, new LineShape(new Segment3(Point3.Origin, new Point3(1, 0, 0)))
            .TryTransform(Transform3.Insert(100, 50, 0, Math.PI / 2, 2, 2))!).InBlock();
        var loose = Make.Line(1, 100, 52, 100, 50);

        Assert.Single(Finders.Create().Find(new[] { loose, content }));
    }
}

/// <summary>Displayed properties of an object inside a block (layer 0 and ByBlock).</summary>
public class ResolveInBlockTests
{
    private static readonly DisplayProperties Insert = new("A-DOOR", "3", "DASHED", 1, "LineWeight030", "BYLAYER");

    [Fact]
    public void Layer_zero_takes_the_insert_layer() =>
        Assert.Equal("A-DOOR", DisplayProperties.OnLayer("0").ResolveInBlock(Insert).Layer);

    [Fact]
    public void Other_layers_are_kept() =>
        Assert.Equal("GLASS", DisplayProperties.OnLayer("GLASS").ResolveInBlock(Insert).Layer);

    [Fact]
    public void ByBlock_values_take_the_insert_values()
    {
        var content = new DisplayProperties("0", "BYBLOCK", "ByBlock", 1, "ByBlock", "BYBLOCK");

        var resolved = content.ResolveInBlock(Insert);

        Assert.Equal(Insert with { LinetypeScale = 1 }, resolved);
    }

    [Fact]
    public void ByLayer_and_explicit_values_are_kept()
    {
        var content = new DisplayProperties("GLASS", "BYLAYER", "CONTINUOUS", 1, "BYLAYER", "50");

        Assert.Equal(content, content.ResolveInBlock(Insert));
    }
}
