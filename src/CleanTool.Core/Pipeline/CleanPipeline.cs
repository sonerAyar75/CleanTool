using CleanTool.Core.Abstractions;

namespace CleanTool.Core.Pipeline;

/// <summary>Options that control how the pipeline reacts to failures.</summary>
public sealed record PipelineOptions
{
    /// <summary>When true, the remaining operations are not run after one operation fails completely.</summary>
    public bool StopOnError { get; init; }
}

/// <summary>
/// Runs operations in <see cref="CleanStage"/> order. Every operation runs in its own scope (transaction):
/// if one fails, only its changes are rolled back and the others are not affected.
/// The pipeline does not know which operations exist; it only talks to <see cref="ICleanOperation"/> (DIP).
/// </summary>
public sealed class CleanPipeline
{
    private readonly IReadOnlyList<ICleanOperation> _operations;
    private readonly IOperationScopeFactory _scopeFactory;
    private readonly PipelineOptions _options;

    /// <summary>Creates a pipeline.</summary>
    /// <param name="operations">Operations to run; they are sorted by <see cref="CleanStage"/>.</param>
    /// <param name="scopeFactory">Creates the transaction-like scope for each operation.</param>
    /// <param name="options">Failure handling; defaults when null.</param>
    /// <exception cref="ArgumentException">Two operations have the same id.</exception>
    public CleanPipeline(
        IEnumerable<ICleanOperation> operations,
        IOperationScopeFactory scopeFactory,
        PipelineOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(operations);
        ArgumentNullException.ThrowIfNull(scopeFactory);

        // Order by stage (OrderBy is stable: operations in the same stage keep the order they were given in).
        _operations = operations.OrderBy(o => o.Stage).ToList();
        _scopeFactory = scopeFactory;
        _options = options ?? new PipelineOptions();

        var duplicateId = _operations
            .GroupBy(o => o.Id, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(g => g.Count() > 1);

        if (duplicateId is not null)
            throw new ArgumentException($"Operation id '{duplicateId.Key}' is registered more than once.", nameof(operations));
    }

    /// <summary>Execution order (sorted by stage).</summary>
    public IReadOnlyList<ICleanOperation> Operations => _operations;

    /// <summary>Runs every operation in its own scope and collects the results.</summary>
    /// <param name="context">Dry-run flag and logger shared by all operations.</param>
    /// <returns>One result per operation that ran.</returns>
    public CleanReport Run(CleanContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var results = new List<OperationResult>(_operations.Count);

        foreach (var operation in _operations)
        {
            var result = RunSingle(operation, context);
            results.Add(result);

            if (result.Status == OperationStatus.Failed && _options.StopOnError) break;
        }

        return new CleanReport(results, context.DryRun);
    }

    private OperationResult RunSingle(ICleanOperation operation, CleanContext context)
    {
        using var scope = _scopeFactory.Begin(operation.Id);
        try
        {
            var result = operation.Execute(context);

            // A preview must not persist anything: the scope is not completed, so Dispose rolls it back.
            if (!context.DryRun) scope.Complete();

            return result;
        }
        catch (Exception ex)
        {
            // Boundary: a single failing operation must not crash the whole tool.
            context.Logger.Warn($"{operation.DisplayName} failed: {ex.Message}");
            return OperationResult.FromException(operation.Id, operation.DisplayName, ex);
        }
    }
}
