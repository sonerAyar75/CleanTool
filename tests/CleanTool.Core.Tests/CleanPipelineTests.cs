using CleanTool.Core.Pipeline;
using CleanTool.Core.Tests.Fakes;

namespace CleanTool.Core.Tests;

public class CleanPipelineTests
{
    [Fact]
    public void Runs_operations_in_registration_order()
    {
        var log = new List<string>();
        var pipeline = new CleanPipeline(
            new[] { new StubOperation("a", log), new StubOperation("b", log), new StubOperation("c", log) },
            new RecordingScopeFactory());

        pipeline.Run(new CleanContext());

        Assert.Equal(new[] { "a", "b", "c" }, log);
    }

    [Fact]
    public void Each_successful_operation_completes_its_own_scope()
    {
        var scopes = new RecordingScopeFactory();
        var pipeline = new CleanPipeline(new[] { new StubOperation("a"), new StubOperation("b") }, scopes);

        pipeline.Run(new CleanContext());

        Assert.Equal(new[] { ("a", true), ("b", true) }, scopes.Scopes);
    }

    [Fact]
    public void Dry_run_never_completes_scopes()
    {
        var scopes = new RecordingScopeFactory();
        var pipeline = new CleanPipeline(new[] { new StubOperation("a") }, scopes);

        var report = pipeline.Run(new CleanContext(dryRun: true));

        Assert.Equal(new[] { ("a", false) }, scopes.Scopes);
        Assert.True(report.IsDryRun);
    }

    [Fact]
    public void Failing_operation_is_rolled_back_and_others_continue()
    {
        var scopes = new RecordingScopeFactory();
        var logger = new ListLogger();
        var pipeline = new CleanPipeline(
            new[]
            {
                new StubOperation("bad", execute: _ => throw new InvalidOperationException("kaboom")),
                new StubOperation("good")
            },
            scopes);

        var report = pipeline.Run(new CleanContext(logger: logger));

        Assert.Equal(new[] { ("bad", false), ("good", true) }, scopes.Scopes);
        Assert.Equal(OperationStatus.Failed, report.Results[0].Status);
        Assert.Equal("kaboom", report.Results[0].Error);
        Assert.Equal(OperationStatus.Completed, report.Results[1].Status);
        Assert.True(report.HasFailures);
        Assert.Contains(logger.Lines, l => l.Contains("kaboom"));
    }

    [Fact]
    public void Stop_on_error_skips_remaining_operations()
    {
        var log = new List<string>();
        var pipeline = new CleanPipeline(
            new[]
            {
                new StubOperation("bad", log, _ => throw new InvalidOperationException()),
                new StubOperation("never", log)
            },
            new RecordingScopeFactory(),
            new PipelineOptions { StopOnError = true });

        var report = pipeline.Run(new CleanContext());

        Assert.Equal(new[] { "bad" }, log);
        Assert.Single(report.Results);
    }

    [Fact]
    public void Duplicate_operation_ids_are_rejected()
    {
        Assert.Throws<ArgumentException>(() =>
            new CleanPipeline(new[] { new StubOperation("x"), new StubOperation("X") }, new RecordingScopeFactory()));
    }

    [Fact]
    public void Formatter_uses_preview_wording_in_dry_run()
    {
        var pipeline = new CleanPipeline(new[] { new StubOperation("a") }, new RecordingScopeFactory());

        var lines = CleanReportFormatter.Format(pipeline.Run(new CleanContext(dryRun: true)));

        Assert.Contains("preview", lines[0]);
        Assert.Contains(lines, l => l.Contains("would change 1"));
    }

    [Fact]
    public void Result_builder_caps_messages()
    {
        var builder = new OperationResultBuilder("x", "X", maxMessages: 2);
        for (var i = 0; i < 5; i++) builder.AddSkipped($"m{i}");

        var result = builder.Build();

        Assert.Equal(5, result.Skipped);
        Assert.Equal(3, result.Messages.Count);
        Assert.Contains("3 more", result.Messages[^1]);
    }
}
