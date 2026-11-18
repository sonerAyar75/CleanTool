using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using CleanTool.AcadTool.Infrastructure;
using CleanTool.Core.Pipeline;

namespace CleanTool.AcadTool.Composition;

/// <summary>
/// Bridge between commands/UI and the Core pipeline: creates the operations, runs them and returns the report.
/// The caller decides where the report is shown (command line or dialog).
///
/// The execution order is determined in the pipeline by each operation's CleanStage:
///   Thickness = 0 → Z = 0 → Duplicates → Unused layers → (regenerating dynamic block copies)
/// </summary>
internal sealed class CleanRunner
{
    private readonly OperationRegistry _registry;

    /// <summary>Creates a runner for the operations of one tool.</summary>
    public CleanRunner(OperationRegistry registry)
    {
        _registry = registry;
    }

    /// <summary>Creates the requested operations for the document and runs them through the pipeline.</summary>
    /// <param name="document">Document to work on (locked for the duration of the run).</param>
    /// <param name="scope">Objects to work on; wrapped with block content according to <c>settings.Blocks</c>.</param>
    /// <param name="operationIds">Ids of the operations to run (order does not matter, CleanStage decides).</param>
    /// <param name="dryRun">true for a preview that changes nothing.</param>
    /// <param name="settings">Settings of the tool.</param>
    /// <returns>The report of the run.</returns>
    public CleanReport Run(Document document, IEntityScope scope, IReadOnlyList<string> operationIds, bool dryRun, ToolSettings settings)
    {
        // Lock the document explicitly because this may be called from modal or modeless windows.
        using var documentLock = document.LockDocument();

        var effectiveScope = BlockContentScope.Wrap(scope, settings.Blocks);
        var session = new AcadSession(document, effectiveScope);
        var logger = new EditorLogger(document.Editor);

        var operations = operationIds
            .Select(id => _registry.Get(id).Create(session, settings))
            .ToList();

        var pipeline = new CleanPipeline(operations, new AcadOperationScopeFactory(document));
        var report = pipeline.Run(new CleanContext(dryRun, logger));

        if (!dryRun && report.TotalChanged > 0)
        {
            if (effectiveScope is BlockContentScope blocks)
                UpdateDynamicBlocks(document, blocks.DynamicDefinitions, logger);

            document.Editor.Regen();
        }

        return report;
    }

    /// <summary>
    /// Final step: regenerates the anonymous *U copies of dynamic blocks whose content changed from the base definition.
    /// Without this, inserts with modified parameters would keep showing the old (unflattened) geometry.
    /// </summary>
    private static void UpdateDynamicBlocks(Document document, IReadOnlyCollection<ObjectId> definitions, EditorLogger logger)
    {
        if (definitions.Count == 0) return;

        using var transaction = document.TransactionManager.StartTransaction();
        foreach (var id in definitions)
        {
            try
            {
                var definition = (BlockTableRecord)transaction.GetObject(id, OpenMode.ForWrite);
                definition.UpdateAnonymousBlocks();
            }
            catch (Exception ex)
            {
                logger.Warn($"Could not update dynamic block copies of <{id.Handle}>: {ex.Message}");
            }
        }
        transaction.Commit();
    }
}
