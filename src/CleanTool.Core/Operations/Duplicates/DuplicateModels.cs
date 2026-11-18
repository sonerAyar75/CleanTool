using CleanTool.Core.Geometry;
using CleanTool.Core.Geometry.Shapes;
using CleanTool.Core.Model;

namespace CleanTool.Core.Operations.Duplicates;

/// <summary>Summary of a drawing object for duplicate detection.</summary>
/// <param name="Handle">Object identity.</param>
/// <param name="Owner">
/// The space that contains the object (model, layout or block definition). Only objects in the same space can be
/// copies of each other: a line in model space and a line inside a block are not copies even at the same coordinates.
/// </param>
/// <param name="Shape">Geometry.</param>
/// <param name="Properties">Layer, color, linetype etc.</param>
/// <param name="IsOnLockedLayer">Objects on locked layers cannot be erased.</param>
/// <param name="BlockContent">
/// null: a loose object. Otherwise: an object inside a block definition, transformed into the space that holds
/// the insert. In that case Handle is the handle of the object in the definition and Owner is the insert's space.
/// </param>
public sealed record ShapeRecord(
    EntityHandle Handle,
    EntityHandle Owner,
    IShape Shape,
    DisplayProperties Properties,
    bool IsOnLockedLayer,
    BlockContentInfo? BlockContent = null)
{
    /// <summary>True for an object that is not part of block content.</summary>
    public bool IsLoose => BlockContent is null;
}

/// <param name="BlockName">Block name shown in reports.</param>
/// <param name="Insert">The (top-level) block reference that displays the object.</param>
/// <param name="CannotRemoveReason">
/// null if the object can safely be erased from the definition (the block is used once, not nested, not dynamic/xref).
/// Otherwise the reason (e.g. "block is used 4 times").
/// </param>
public sealed record BlockContentInfo(string BlockName, EntityHandle Insert, string? CannotRemoveReason)
{
    /// <summary>Can the object be erased from the block definition safely?</summary>
    public bool CanRemoveFromDefinition => CannotRemoveReason is null;
}

/// <summary>What to do when a loose object duplicates content of the block it lies on.</summary>
public enum BlockOverlapMode
{
    /// <summary>Block content and loose objects are not compared.</summary>
    DontCompare,

    /// <summary>
    /// The loose object that is an EXACT copy of block content is erased. Affects only that spot; the safe default.
    /// (Partial coverage and trimming belong to a separate tool: WallTrim / BlockTrimOperation.)
    /// </summary>
    RemoveLooseObject,

    /// <summary>
    /// The object in the block definition is erased. Because changing a definition changes every insert, this is
    /// applied only to blocks used once; the others are skipped and reported.
    /// </summary>
    RemoveFromBlock
}

/// <summary>Settings of the duplicate removal (<see cref="DuplicateOperation"/>).</summary>
public sealed record DuplicateOptions
{
    /// <summary>Points / radii / bulges closer than this are equal (drawing units).</summary>
    public Tolerance Tolerance { get; init; } = new(1e-4);

    /// <summary>
    /// Properties whose differences are ignored. Default: none (like OVERKILL, layer, color, linetype etc. must all
    /// match for objects to be copies). For example, adding Layer also removes copies on other layers.
    /// </summary>
    public ComparedProperty IgnoredProperties { get; init; } = ComparedProperty.None;

    /// <summary>What to do when a loose object duplicates content of the block it lies on.</summary>
    public BlockOverlapMode BlockOverlap { get; init; } = BlockOverlapMode.RemoveLooseObject;

    /// <summary>
    /// false (default): only geometry is compared with block content. Since the block already displays the object,
    /// layer/color differences of the loose copy rarely matter (e.g. block content on layer 0 → A-DOOR, loose line on A-WALL).
    /// true: the <see cref="IgnoredProperties"/> rules apply to this step as well.
    /// </summary>
    public bool MatchPropertiesForBlockOverlap { get; init; }
}

/// <summary>Objects that are copies of each other: one is kept, the others are erased.</summary>
public sealed record DuplicateGroup(ShapeRecord Keep, IReadOnlyList<ShapeRecord> Duplicates)
{
    /// <summary>
    /// Chooses the object to keep: one on a locked layer is preferred (it cannot be erased anyway),
    /// then the lowest handle (the oldest object). The result is deterministic.
    /// </summary>
    public static DuplicateGroup Create(IReadOnlyCollection<ShapeRecord> members)
    {
        if (members.Count < 2) throw new ArgumentException("A duplicate group needs at least two members.", nameof(members));

        var keep = members
            .OrderByDescending(m => m.IsOnLockedLayer)
            .ThenBy(m => m.Handle.Value)
            .First();

        var duplicates = members.Where(m => m.Handle != keep.Handle).OrderBy(m => m.Handle.Value).ToList();
        return new DuplicateGroup(keep, duplicates);
    }
}

/// <summary>Port through which the Duplicate operation reads and changes the drawing.</summary>
public interface IDuplicateGateway
{
    IReadOnlyList<ShapeRecord> GetShapes();

    /// <summary>
    /// Content of the block references in scope, transformed into the coordinates of the space holding each insert,
    /// with effective properties resolved (layer 0 / ByBlock). Called only when BlockOverlap is not DontCompare.
    /// </summary>
    IReadOnlyList<ShapeRecord> GetBlockContentShapes();

    bool TryErase(EntityHandle handle, out string? failureReason);
}

/// <summary>
/// A decision for one overlap (shared by Duplicate and BlockTrim).
/// - SkipReason set: the object is left alone (the reason is reported).
/// - Pieces null: the object is erased.
/// - Pieces set: the object is replaced by these pieces (trim).
/// </summary>
public sealed record OverlapAction(EntityHandle Handle, string Label, string? SkipReason, IReadOnlyList<IShape>? Pieces = null)
{
    /// <summary>The object should be erased.</summary>
    public bool ShouldErase => SkipReason is null && Pieces is null;

    /// <summary>The object should be replaced by <see cref="Pieces"/>.</summary>
    public bool ShouldReplace => SkipReason is null && Pieces is not null;
}
