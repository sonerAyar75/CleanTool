namespace CleanTool.Core.Operations.Duplicates;

/// <summary>
/// Decides what to remove from groups of EXACT copies between block content and loose objects.
/// Pure decision logic; it never touches the drawing.
///
/// - Remove loose object: the loose copy is erased and the block does not change.
/// - Remove from block: block content identical to a loose object is erased from the definition.
///
/// Exact copies (not mere coverage) are required on purpose: erasing from a definition affects every insert of
/// the block, so only objects with an exact counterpart outside are removed. When it is not safe (the block is
/// used more than once, nested or dynamic) the object is skipped and the reason is reported.
///
/// Only groups containing both loose objects and block content matter; overlapping content of two blocks is left alone.
/// </summary>
public sealed class BlockOverlapResolver
{
    /// <summary>Remove loose object: erase loose objects that are exact copies of block content.</summary>
    /// <param name="groups">Duplicate groups that may mix loose objects and block content.</param>
    public IReadOnlyList<OverlapAction> ResolveRemoveLoose(IEnumerable<DuplicateGroup> groups)
    {
        var actions = new List<OverlapAction>();
        var handled = new HashSet<Model.EntityHandle>();

        foreach (var group in groups)
        {
            var members = group.Duplicates.Prepend(group.Keep).ToList();
            var block = members.FirstOrDefault(m => !m.IsLoose)?.BlockContent;
            if (block is null) continue;

            foreach (var item in members.Where(m => m.IsLoose))
            {
                if (!handled.Add(item.Handle)) continue;

                var label = $"{item.Shape.Kind} <{item.Handle}> duplicates content of block '{block.BlockName}'";
                actions.Add(new OverlapAction(item.Handle, label, item.IsOnLockedLayer ? "it is on a locked layer" : null));
            }
        }

        return actions;
    }

    /// <summary>Remove from block: erase block content that is an exact copy of a loose object, when that is safe.</summary>
    /// <param name="groups">Duplicate groups that may mix loose objects and block content.</param>
    public IReadOnlyList<OverlapAction> ResolveRemoveFromBlock(IEnumerable<DuplicateGroup> groups)
    {
        var actions = new List<OverlapAction>();
        var handled = new HashSet<Model.EntityHandle>();

        foreach (var group in groups)
        {
            var members = group.Duplicates.Prepend(group.Keep).ToList();
            if (!members.Any(m => m.IsLoose)) continue;

            foreach (var item in members.Where(m => !m.IsLoose))
            {
                if (!handled.Add(item.Handle)) continue;

                var info = item.BlockContent!;
                var label = $"{item.Shape.Kind} <{item.Handle}> inside block '{info.BlockName}'";

                if (!info.CanRemoveFromDefinition)
                    actions.Add(new OverlapAction(item.Handle, label, info.CannotRemoveReason));
                else if (item.IsOnLockedLayer)
                    actions.Add(new OverlapAction(item.Handle, label, "it is on a locked layer"));
                else
                    actions.Add(new OverlapAction(item.Handle, label, null));
            }
        }

        return actions;
    }
}
