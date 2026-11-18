using CleanTool.Core.Model;
using CleanTool.Core.Operations.Duplicates;
using CleanTool.Core.Tests.Fakes;

namespace CleanTool.Core.Tests;

/// <summary>OVERKILL-style "Ignore object property" behavior.</summary>
public class DuplicatePropertyTests
{
    /// <summary>For each property: two copies that differ only in that property.</summary>
    public static IEnumerable<object[]> EachProperty() => new[]
    {
        new object[] { ComparedProperty.Layer },
        new object[] { ComparedProperty.Color },
        new object[] { ComparedProperty.Linetype },
        new object[] { ComparedProperty.LinetypeScale },
        new object[] { ComparedProperty.Lineweight },
        new object[] { ComparedProperty.Transparency }
    };

    private static ShapeRecord Differ(ShapeRecord record, ComparedProperty property) => record.With(p => property switch
    {
        ComparedProperty.Layer => p with { Layer = "OTHER" },
        ComparedProperty.Color => p with { Color = "1" },
        ComparedProperty.Linetype => p with { Linetype = "DASHED" },
        ComparedProperty.LinetypeScale => p with { LinetypeScale = 2.0 },
        ComparedProperty.Lineweight => p with { Lineweight = "LineWeight050" },
        ComparedProperty.Transparency => p with { Transparency = "128" },
        _ => throw new ArgumentOutOfRangeException(nameof(property))
    });

    [Theory]
    [MemberData(nameof(EachProperty))]
    public void Copies_with_different_property_are_kept_by_default(ComparedProperty property)
    {
        var groups = Finders.Create().Find(new[] { Make.Line(1, 0, 0, 10, 0), Differ(Make.Line(2, 0, 0, 10, 0), property) });

        Assert.Empty(groups);
    }

    [Theory]
    [MemberData(nameof(EachProperty))]
    public void Copies_with_different_property_match_when_that_property_is_ignored(ComparedProperty property)
    {
        var groups = Finders.Create(ignore: property).Find(new[] { Make.Line(1, 0, 0, 10, 0), Differ(Make.Line(2, 0, 0, 10, 0), property) });

        Assert.Single(groups);
    }

    [Fact]
    public void Ignoring_one_property_does_not_ignore_others()
    {
        var a = Make.Circle(1, 0, 0, 5);
        var b = Make.Circle(2, 0, 0, 5).With(p => p with { Layer = "OTHER", Color = "1" });

        Assert.Empty(Finders.Create(ignore: ComparedProperty.Layer).Find(new[] { a, b }));
        Assert.Single(Finders.Create(ignore: ComparedProperty.Layer | ComparedProperty.Color).Find(new[] { a, b }));
    }

    [Fact]
    public void Property_values_are_compared_case_insensitively()
    {
        var a = Make.Line(1, 0, 0, 10, 0).With(p => p with { Linetype = "Dashed", Color = "ByLayer" });
        var b = Make.Line(2, 0, 0, 10, 0).With(p => p with { Linetype = "DASHED", Color = "BYLAYER" });

        Assert.Single(Finders.Create().Find(new[] { a, b }));
    }

    [Fact]
    public void ByLayer_and_explicit_color_are_different()
    {
        // Raw values are compared, like OVERKILL: ByLayer ≠ 1 (red) even if the layer is red.
        var a = Make.Line(1, 0, 0, 10, 0);
        var b = Make.Line(2, 0, 0, 10, 0).With(p => p with { Color = "1" });

        Assert.Empty(Finders.Create().Find(new[] { a, b }));
    }

    [Fact]
    public void Ignoring_everything_matches_completely_different_properties()
    {
        var all = ComparedProperty.Layer | ComparedProperty.Color | ComparedProperty.Linetype
                  | ComparedProperty.LinetypeScale | ComparedProperty.Lineweight | ComparedProperty.Transparency;

        var a = Make.Poly(1, true, (0, 0, 0), (10, 0, 0), (10, 10, 0));
        var b = Make.Poly(2, true, (0, 0, 0), (10, 0, 0), (10, 10, 0))
            .With(_ => new DisplayProperties("X", "255,0,0", "HIDDEN", 3, "LineWeight100", "50"));

        Assert.Single(Finders.Create(ignore: all).Find(new[] { a, b }));
    }

    [Fact]
    public void Property_matcher_handles_linetype_scale_precision()
    {
        var a = DisplayProperties.OnLayer("A");
        var b = a with { LinetypeScale = 1.0 + 1e-12 };

        Assert.True(PropertyMatcher.Matches(a, b, ComparedProperty.None));
    }
}

/// <summary>Cleaning inside blocks: copies are searched only within the same space (model / block definition).</summary>
public class DuplicateOwnerTests
{
    [Fact]
    public void Same_geometry_in_different_owners_is_not_duplicate()
    {
        // A line in model space and a line in a block definition at the same coordinates: both must stay.
        var groups = Finders.Create().Find(new[]
        {
            Make.Line(1, 0, 0, 10, 0).InOwner(100),
            Make.Line(2, 0, 0, 10, 0).InOwner(200)
        });

        Assert.Empty(groups);
    }

    [Fact]
    public void Copies_inside_the_same_block_definition_are_duplicates()
    {
        var groups = Finders.Create().Find(new[]
        {
            Make.Circle(1, 0, 0, 5).InOwner(200),
            Make.Circle(2, 0, 0, 5).InOwner(200)
        });

        Assert.Single(groups);
    }

    [Fact]
    public void Each_owner_is_cleaned_independently()
    {
        // 2 copies in model space, 3 copies in block A, 1 object in block B.
        var groups = Finders.Create().Find(new[]
        {
            Make.Line(1, 0, 0, 10, 0).InOwner(100),
            Make.Line(2, 0, 0, 10, 0).InOwner(100),
            Make.Line(3, 0, 0, 10, 0).InOwner(200),
            Make.Line(4, 0, 0, 10, 0).InOwner(200),
            Make.Line(5, 10, 0, 0, 0).InOwner(200),
            Make.Line(6, 0, 0, 10, 0).InOwner(300)
        });

        Assert.Equal(2, groups.Count);
        Assert.Contains(groups, g => g.Keep.Handle.Value == 1 && g.Duplicates.Count == 1);
        Assert.Contains(groups, g => g.Keep.Handle.Value == 3 && g.Duplicates.Count == 2);
        Assert.All(groups, g => Assert.All(g.Duplicates, d => Assert.Equal(g.Keep.Owner, d.Owner)));
    }

    [Fact]
    public void Ignoring_layer_does_not_cross_owners()
    {
        var groups = Finders.Create(ignore: ComparedProperty.Layer).Find(new[]
        {
            Make.Line(1, 0, 0, 10, 0, "A").InOwner(100),
            Make.Line(2, 0, 0, 10, 0, "B").InOwner(200)
        });

        Assert.Empty(groups);
    }
}
