using CleanTool.Core.Pipeline;

namespace CleanTool.Core.Abstractions;

/// <summary>
/// Cleanup stages. The pipeline always runs operations in this order, regardless of the order in which
/// they were registered or selected.
///
/// Why this order?
/// 1. Normalize   – Thickness = 0. Thickness extrudes an object along Z; if it is not reset first, the Z range
///                  is measured wrongly and even a flat circle looks "3D" and cannot be flattened.
/// 2. Flatten     – Z = 0. Copies that differ only in Z start to overlap after this stage.
/// 3. Deduplicate – Removing copies. Must work on flattened geometry.
/// 4. Purge       – Unused layers etc. Definitions emptied by deleted objects are cleaned up last.
/// </summary>
public enum CleanStage
{
    Normalize = 10,
    Flatten = 20,
    Deduplicate = 30,
    Purge = 40
}

/// <summary>
/// A single cleanup operation (Z = 0, duplicate removal, layer purge, ...).
/// A new feature is a new class implementing this interface; existing code does not change (Open/Closed).
/// </summary>
public interface ICleanOperation
{
    /// <summary>Unique, stable identifier (e.g. "flatten"). Commands and settings refer to the operation by this id.</summary>
    string Id { get; }

    /// <summary>Name shown in reports and in the dialog.</summary>
    string DisplayName { get; }

    /// <summary>The stage the operation runs in. Operations in the same stage keep the order they were given in.</summary>
    CleanStage Stage { get; }

    /// <summary>
    /// Runs the operation. When <see cref="CleanContext.DryRun"/> is true it must not change anything
    /// and only report what would change.
    /// </summary>
    /// <param name="context">Shared run context (dry-run flag, logger).</param>
    /// <returns>Counters and messages describing what was examined, changed, skipped or failed.</returns>
    OperationResult Execute(CleanContext context);
}
