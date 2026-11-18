using CleanTool.Core.Abstractions;
using CleanTool.Core.Model;
using CleanTool.Core.Pipeline;

namespace CleanTool.Core.Operations.Duplicates;

/// <summary>
/// Keeps one of each set of overlapping objects (Line, Circle, Arc, Polyline, Block) and erases the rest.
///
/// Two steps:
/// 1. Plain copies: copies within the same space (model / block definition).
/// 2. Block overlap (<see cref="DuplicateOptions.BlockOverlap"/>): block content vs. loose objects lying on the block.
///    Both modes look for exact copies (<see cref="BlockOverlapResolver"/>):
///    Remove loose object → the loose copy is erased; Remove from block → the copy in the definition is erased.
///    Partial coverage and trimming belong to a separate tool (BlockTrim / WallTrim).
///
/// Finding is done by <see cref="DuplicateFinder"/>, erasing by the gateway (SRP).
/// </summary>
public sealed class DuplicateOperation : ICleanOperation
{
    /// <summary>Stable operation id used by commands and settings.</summary>
    public const string OperationId = "duplicates";

    private readonly IDuplicateGateway _gateway;
    private readonly DuplicateOptions _options;
    private readonly DuplicateFinder _finder;
    private readonly ShapeComparerRegistry? _comparers;

    /// <summary>Creates the operation.</summary>
    /// <param name="gateway">Access to the drawing.</param>
    /// <param name="options">Settings; defaults are used when null.</param>
    /// <param name="comparers">Comparers per shape kind; defaults when null.</param>
    public DuplicateOperation(IDuplicateGateway gateway, DuplicateOptions? options = null, ShapeComparerRegistry? comparers = null)
    {
        _gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
        _options = options ?? new DuplicateOptions();
        _comparers = comparers;
        _finder = new DuplicateFinder(_options, comparers);
    }

    /// <inheritdoc/>
    public string Id => OperationId;

    /// <inheritdoc/>
    public string DisplayName => "Duplicate objects";

    /// <inheritdoc/>
    public CleanStage Stage => CleanStage.Deduplicate;

    /// <inheritdoc/>
    public OperationResult Execute(CleanContext context)
    {
        var result = new OperationResultBuilder(Id, DisplayName);

        var shapes = _gateway.GetShapes();
        result.AddExamined(shapes.Count);

        // Count objects as removed even in a preview, so step 2 does not count the same object twice.
        var removed = new HashSet<EntityHandle>();

        RemoveDuplicates(shapes, context, result, removed);

        if (_options.BlockOverlap != BlockOverlapMode.DontCompare)
            ResolveBlockOverlaps(shapes, context, result, removed);

        return result.Build();
    }

    private void RemoveDuplicates(IReadOnlyList<ShapeRecord> shapes, CleanContext context, OperationResultBuilder result, HashSet<EntityHandle> removed)
    {
        foreach (var group in _finder.Find(shapes))
        {
            foreach (var duplicate in group.Duplicates)
            {
                var label = $"{duplicate.Shape.Kind} <{duplicate.Handle}>";

                if (duplicate.IsOnLockedLayer)
                {
                    result.AddSkipped($"{label} duplicates <{group.Keep.Handle}> but is on a locked layer.");
                    continue;
                }

                Erase(duplicate.Handle, label, context, result, removed);
            }
        }
    }

    private void ResolveBlockOverlaps(IReadOnlyList<ShapeRecord> shapes, CleanContext context, OperationResultBuilder result, HashSet<EntityHandle> removed)
    {
        var content = _gateway.GetBlockContentShapes();
        if (content.Count == 0) return;

        var loose = shapes.Where(s => s.IsLoose && !removed.Contains(s.Handle)).ToList();

        // Default: block overlap compares geometry only (see MatchPropertiesForBlockOverlap).
        var ignored = _options.MatchPropertiesForBlockOverlap ? _options.IgnoredProperties : ComparedProperty.All;

        // Both modes look for EXACT copies; partial coverage / trimming is the job of the WallTrim tool.
        var groups = new DuplicateFinder(_options with { IgnoredProperties = ignored }, _comparers)
            .Find(loose.Concat(content).ToList());

        var resolver = new BlockOverlapResolver();
        var actions = _options.BlockOverlap == BlockOverlapMode.RemoveLooseObject
            ? resolver.ResolveRemoveLoose(groups)
            : resolver.ResolveRemoveFromBlock(groups);

        foreach (var action in actions)
        {
            if (action.SkipReason is not null)
            {
                result.AddSkipped($"{action.Label}: not removed, {action.SkipReason}.");
                continue;
            }

            Erase(action.Handle, action.Label, context, result, removed);
        }
    }

    private void Erase(EntityHandle handle, string label, CleanContext context, OperationResultBuilder result, HashSet<EntityHandle> removed)
    {
        if (!removed.Add(handle)) return;

        if (context.DryRun)
        {
            result.AddChanged();
            return;
        }

        if (_gateway.TryErase(handle, out var reason))
            result.AddChanged();
        else
            result.AddFailed($"{label}: {reason ?? "unknown error"}");
    }
}
