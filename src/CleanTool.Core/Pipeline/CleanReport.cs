namespace CleanTool.Core.Pipeline;

/// <summary>Aggregated result of one pipeline run.</summary>
public sealed record CleanReport(IReadOnlyList<OperationResult> Results, bool IsDryRun)
{
    /// <summary>Total number of changed objects over all operations.</summary>
    public int TotalChanged => Results.Sum(r => r.Changed);

    /// <summary>True if any operation failed completely or reported failed objects.</summary>
    public bool HasFailures => Results.Any(r => r.Status == OperationStatus.Failed || r.Failed > 0);
}
