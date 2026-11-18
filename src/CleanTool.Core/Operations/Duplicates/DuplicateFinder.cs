namespace CleanTool.Core.Operations.Duplicates;

/// <summary>
/// Pure algorithm that groups overlapping objects of the same kind. It never touches the drawing and is fully testable.
///
/// 1. Objects are split by space (model / block definition) and kind: a Line is compared only with Lines in the same space.
/// 2. Each kind is sorted by SortKey.
/// 3. Each object is compared only with neighbours whose SortKey is within the tolerance (sweep).
/// </summary>
public sealed class DuplicateFinder
{
    private readonly DuplicateOptions _options;
    private readonly ShapeComparerRegistry _comparers;

    /// <summary>Creates the finder.</summary>
    /// <param name="options">Tolerance and ignored properties; defaults when null.</param>
    /// <param name="comparers">Comparers per shape kind; <see cref="ShapeComparerRegistry.Default"/> when null.</param>
    public DuplicateFinder(DuplicateOptions? options = null, ShapeComparerRegistry? comparers = null)
    {
        _options = options ?? new DuplicateOptions();
        _comparers = comparers ?? ShapeComparerRegistry.Default;
    }

    /// <summary>Groups the shapes that are copies of each other.</summary>
    /// <param name="shapes">All candidate shapes (any kind, any space).</param>
    /// <returns>One group per set of copies; shapes without copies are not returned.</returns>
    public IReadOnlyList<DuplicateGroup> Find(IReadOnlyList<ShapeRecord> shapes)
    {
        ArgumentNullException.ThrowIfNull(shapes);

        var groups = new List<DuplicateGroup>();

        foreach (var bucket in shapes.GroupBy(s => (s.Owner, Type: s.Shape.GetType())))
        {
            // A kind without a comparer cannot be compared safely: leave it alone.
            if (!_comparers.TryGet(bucket.Key.Type, out var comparer)) continue;

            var sorted = bucket.OrderBy(s => s.Shape.SortKey).ToList();
            FindInSortedKind(sorted, comparer, groups);
        }

        return groups;
    }

    private void FindInSortedKind(List<ShapeRecord> sorted, IShapeComparer comparer, List<DuplicateGroup> groups)
    {
        var tolerance = _options.Tolerance;
        var assigned = new bool[sorted.Count];

        for (var i = 0; i < sorted.Count; i++)
        {
            if (assigned[i]) continue;

            var current = sorted[i];
            List<ShapeRecord>? members = null;

            for (var j = i + 1; j < sorted.Count; j++)
            {
                var candidate = sorted[j];

                // Past the window: no later object can be equal.
                if (candidate.Shape.SortKey - current.Shape.SortKey > tolerance.Value) break;
                if (assigned[j]) continue;
                if (!PropertyMatcher.Matches(current.Properties, candidate.Properties, _options.IgnoredProperties)) continue;
                if (!comparer.AreEqual(current.Shape, candidate.Shape, tolerance)) continue;

                members ??= new List<ShapeRecord> { current };
                members.Add(candidate);
                assigned[j] = true;
            }

            if (members is null) continue;

            assigned[i] = true;
            groups.Add(DuplicateGroup.Create(members));
        }
    }
}
