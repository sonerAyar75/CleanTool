namespace CleanTool.Core.Pipeline;

/// <summary>Overall outcome of one operation.</summary>
public enum OperationStatus
{
    /// <summary>The operation ran and at least one object was changed, skipped or failed.</summary>
    Completed,

    /// <summary>The operation ran and there was nothing to do.</summary>
    NothingToDo,

    /// <summary>The operation failed completely with an exception; its changes were rolled back.</summary>
    Failed
}

/// <summary>Immutable result of one operation.</summary>
public sealed record OperationResult(
    string OperationId,
    string DisplayName,
    OperationStatus Status,
    int Examined,
    int Changed,
    int Skipped,
    int Failed,
    IReadOnlyList<string> Messages,
    string? Error = null)
{
    public static OperationResult FromException(string operationId, string displayName, Exception exception) =>
        new(operationId, displayName, OperationStatus.Failed, 0, 0, 0, 0, Array.Empty<string>(), exception.Message);
}
