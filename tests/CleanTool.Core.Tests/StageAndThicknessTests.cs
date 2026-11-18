using CleanTool.Core.Abstractions;
using CleanTool.Core.Model;
using CleanTool.Core.Operations.Duplicates;
using CleanTool.Core.Operations.Flatten;
using CleanTool.Core.Operations.Layers;
using CleanTool.Core.Operations.Thickness;
using CleanTool.Core.Pipeline;
using CleanTool.Core.Tests.Fakes;

namespace CleanTool.Core.Tests;

/// <summary>Operations run in the right order.</summary>
public class StageOrderTests
{
    [Fact]
    public void Operations_run_in_stage_order_regardless_of_given_order()
    {
        var log = new List<string>();
        var pipeline = new CleanPipeline(
            new[]
            {
                new StubOperation("purge", log, stage: CleanStage.Purge),
                new StubOperation("dedupe", log, stage: CleanStage.Deduplicate),
                new StubOperation("flatten", log, stage: CleanStage.Flatten),
                new StubOperation("thickness", log, stage: CleanStage.Normalize)
            },
            new RecordingScopeFactory());

        pipeline.Run(new CleanContext());

        Assert.Equal(new[] { "thickness", "flatten", "dedupe", "purge" }, log);
    }

    [Fact]
    public void Operations_in_the_same_stage_keep_their_given_order()
    {
        var log = new List<string>();
        var pipeline = new CleanPipeline(
            new[]
            {
                new StubOperation("b", log, stage: CleanStage.Flatten),
                new StubOperation("a", log, stage: CleanStage.Flatten),
                new StubOperation("first", log, stage: CleanStage.Normalize)
            },
            new RecordingScopeFactory());

        pipeline.Run(new CleanContext());

        Assert.Equal(new[] { "first", "b", "a" }, log);
    }

    [Fact]
    public void Built_in_operations_have_the_expected_stage_order()
    {
        // Even if the real operations are given in reverse order: Thickness → Flatten → Duplicates → Layers.
        var pipeline = new CleanPipeline(
            new ICleanOperation[]
            {
                new LayerPurgeOperation(new FakeLayerGateway()),
                new DuplicateOperation(new FakeDuplicateGateway()),
                new FlattenOperation(new FakeFlattenGateway()),
                new ThicknessOperation(new FakeThicknessGateway())
            },
            new RecordingScopeFactory());

        Assert.Equal(
            new[] { ThicknessOperation.OperationId, FlattenOperation.OperationId, DuplicateOperation.OperationId, LayerPurgeOperation.OperationId },
            pipeline.Operations.Select(o => o.Id));
    }
}

public class ThicknessOperationTests
{
    private static ThicknessCandidate Candidate(long handle, double thickness, bool locked = false) =>
        new(new EntityHandle(handle), "Circle", thickness, locked);

    [Fact]
    public void Resets_only_nonzero_thickness()
    {
        var gateway = new FakeThicknessGateway(Candidate(1, 0), Candidate(2, 10), Candidate(3, -5), Candidate(4, 1e-9));

        var result = new ThicknessOperation(gateway).Execute(new CleanContext());

        Assert.Equal(new long[] { 2, 3 }, gateway.Reset.Select(h => h.Value));
        Assert.Equal(4, result.Examined);
        Assert.Equal(2, result.Changed);
    }

    [Fact]
    public void Locked_objects_are_skipped()
    {
        var gateway = new FakeThicknessGateway(Candidate(1, 10, locked: true));

        var result = new ThicknessOperation(gateway).Execute(new CleanContext());

        Assert.Empty(gateway.Reset);
        Assert.Equal(1, result.Skipped);
    }

    [Fact]
    public void Dry_run_changes_nothing()
    {
        var gateway = new FakeThicknessGateway(Candidate(1, 10));

        var result = new ThicknessOperation(gateway).Execute(new CleanContext(dryRun: true));

        Assert.Empty(gateway.Reset);
        Assert.Equal(1, result.Changed);
    }
}
