using CleanTool.Core.Model;
using CleanTool.Core.Operations.Duplicates;
using CleanTool.Core.Operations.Flatten;
using CleanTool.Core.Operations.Layers;
using CleanTool.Core.Pipeline;
using CleanTool.Core.Tests.Fakes;

namespace CleanTool.Core.Tests;

public class FlattenOperationTests
{
    [Fact]
    public void Flattens_only_entities_off_zero()
    {
        var gateway = new FakeFlattenGateway(
            Make.Candidate(1, 0, 0),
            Make.Candidate(2, 0, 5),
            Make.Candidate(3, -2, -2));

        var result = new FlattenOperation(gateway).Execute(new CleanContext());

        Assert.Equal(new[] { new EntityHandle(2), new EntityHandle(3) }, gateway.Flattened);
        Assert.Equal(3, result.Examined);
        Assert.Equal(2, result.Changed);
    }

    [Fact]
    public void Values_within_tolerance_count_as_flat()
    {
        var gateway = new FakeFlattenGateway(Make.Candidate(1, -1e-9, 1e-9));

        var result = new FlattenOperation(gateway).Execute(new CleanContext());

        Assert.Empty(gateway.Flattened);
        Assert.Equal(OperationStatus.NothingToDo, result.Status);
    }

    [Fact]
    public void Locked_entities_are_skipped()
    {
        var gateway = new FakeFlattenGateway(Make.Candidate(1, 5, 5, locked: true));

        var result = new FlattenOperation(gateway).Execute(new CleanContext());

        Assert.Empty(gateway.Flattened);
        Assert.Equal(1, result.Skipped);
    }

    [Fact]
    public void Dry_run_changes_nothing_but_reports_count()
    {
        var gateway = new FakeFlattenGateway(Make.Candidate(1, 5, 5), Make.Candidate(2, 1, 3));

        var result = new FlattenOperation(gateway).Execute(new CleanContext(dryRun: true));

        Assert.Empty(gateway.Flattened);
        Assert.Equal(2, result.Changed);
    }

    [Fact]
    public void Gateway_failures_are_reported_and_do_not_stop_the_operation()
    {
        var gateway = new FakeFlattenGateway(Make.Candidate(1, 5, 5), Make.Candidate(2, 5, 5));
        gateway.FailOn.Add(new EntityHandle(1));

        var result = new FlattenOperation(gateway).Execute(new CleanContext());

        Assert.Equal(1, result.Failed);
        Assert.Equal(1, result.Changed);
        Assert.Contains(result.Messages, m => m.Contains("boom"));
    }
}

public class DuplicateOperationTests
{
    [Fact]
    public void Erases_duplicates_and_keeps_original()
    {
        var gateway = new FakeDuplicateGateway(Make.Line(1, 0, 0, 10, 0), Make.Line(2, 10, 0, 0, 0), Make.Line(3, 0, 1, 10, 1));

        var result = new DuplicateOperation(gateway).Execute(new CleanContext());

        Assert.Equal(new[] { new EntityHandle(2) }, gateway.Erased);
        Assert.Equal(3, result.Examined);
        Assert.Equal(1, result.Changed);
    }

    [Fact]
    public void Erases_duplicates_of_every_supported_kind_in_one_run()
    {
        var gateway = new FakeDuplicateGateway(
            Make.Line(1, 0, 0, 10, 0), Make.Line(2, 0, 0, 10, 0),
            Make.Circle(3, 5, 5, 2), Make.Circle(4, 5, 5, 2),
            Make.Arc(5, 10, 0, 90), Make.Arc(6, 10, 0, 90),
            Make.Poly(7, false, (0, 0, 0), (10, 0, 0), (10, 10, 0)), Make.Poly(8, false, (0, 0, 0), (10, 0, 0), (10, 10, 0)));

        var result = new DuplicateOperation(gateway).Execute(new CleanContext());

        Assert.Equal(new long[] { 2, 4, 6, 8 }, gateway.Erased.Select(h => h.Value).OrderBy(v => v));
        Assert.Equal(4, result.Changed);
    }

    [Fact]
    public void Dry_run_erases_nothing()
    {
        var gateway = new FakeDuplicateGateway(Make.Circle(1, 0, 0, 5), Make.Circle(2, 0, 0, 5));

        var result = new DuplicateOperation(gateway).Execute(new CleanContext(dryRun: true));

        Assert.Empty(gateway.Erased);
        Assert.Equal(1, result.Changed);
    }

    [Fact]
    public void Locked_duplicates_are_skipped_and_reported()
    {
        var gateway = new FakeDuplicateGateway(
            Make.Line(1, 0, 0, 10, 0, locked: true),
            Make.Line(2, 0, 0, 10, 0, locked: true));

        var result = new DuplicateOperation(gateway).Execute(new CleanContext());

        Assert.Empty(gateway.Erased);
        Assert.Equal(1, result.Skipped);
        Assert.Contains(result.Messages, m => m.Contains("locked"));
    }

    [Fact]
    public void Unlocked_copy_is_erased_when_original_is_locked()
    {
        var gateway = new FakeDuplicateGateway(
            Make.Line(1, 0, 0, 10, 0),
            Make.Line(2, 0, 0, 10, 0, locked: true));

        new DuplicateOperation(gateway).Execute(new CleanContext());

        Assert.Equal(new[] { new EntityHandle(1) }, gateway.Erased);
    }
}

public class LayerPurgeOperationTests
{
    [Fact]
    public void Deletes_only_unused_unprotected_layers()
    {
        var gateway = new FakeLayerGateway(
            Make.Layer("0"),
            Make.Layer("Defpoints"),
            Make.Layer("CURRENT", current: true),
            Make.Layer("XREF|WALL", xref: true),
            Make.Layer("USED", inUse: true),
            Make.Layer("EMPTY"));

        var result = new LayerPurgeOperation(gateway).Execute(new CleanContext());

        Assert.Equal(new[] { "EMPTY" }, gateway.Deleted);
        Assert.Equal(6, result.Examined);
        Assert.Equal(1, result.Changed);
    }

    [Fact]
    public void Built_in_layer_names_are_case_insensitive()
    {
        var gateway = new FakeLayerGateway(Make.Layer("DEFPOINTS"));

        new LayerPurgeOperation(gateway).Execute(new CleanContext());

        Assert.Empty(gateway.Deleted);
    }

    [Fact]
    public void Excluded_patterns_are_kept_and_reported()
    {
        var gateway = new FakeLayerGateway(Make.Layer("A-WALL"), Make.Layer("TEMP1"), Make.Layer("JUNK"));
        var options = new LayerPurgeOptions { ExcludedPatterns = new[] { "a-*", "TEMP?" } };

        var result = new LayerPurgeOperation(gateway, options).Execute(new CleanContext());

        Assert.Equal(new[] { "JUNK" }, gateway.Deleted);
        Assert.Equal(2, result.Skipped);
    }

    [Fact]
    public void Dry_run_deletes_nothing()
    {
        var gateway = new FakeLayerGateway(Make.Layer("EMPTY"));

        var result = new LayerPurgeOperation(gateway).Execute(new CleanContext(dryRun: true));

        Assert.Empty(gateway.Deleted);
        Assert.Equal(1, result.Changed);
    }

    [Theory]
    [InlineData("WALL", "W*", true)]
    [InlineData("WALL", "w?ll", true)]
    [InlineData("WALL", "W?", false)]
    [InlineData("A.B", "A.B", true)]
    [InlineData("AXB", "A.B", false)]
    public void Wildcard_matching(string input, string pattern, bool expected)
    {
        Assert.Equal(expected, WildcardMatcher.IsMatch(input, pattern));
    }
}
