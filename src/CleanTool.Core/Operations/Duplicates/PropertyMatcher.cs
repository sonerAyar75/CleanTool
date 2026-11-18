using CleanTool.Core.Model;

namespace CleanTool.Core.Operations.Duplicates;

/// <summary>
/// Are the display properties of two objects the same, apart from the ignored ones?
/// Values are compared raw: a ByLayer color on a red layer and an explicit "red" are different
/// (OVERKILL behaves the same way, which keeps results predictable).
/// </summary>
public static class PropertyMatcher
{
    private const double ScaleTolerance = 1e-9;

    public static bool Matches(DisplayProperties a, DisplayProperties b, ComparedProperty ignored) =>
        SameText(a.Layer, b.Layer, ignored, ComparedProperty.Layer)
        && SameText(a.Color, b.Color, ignored, ComparedProperty.Color)
        && SameText(a.Linetype, b.Linetype, ignored, ComparedProperty.Linetype)
        && (ignored.HasFlag(ComparedProperty.LinetypeScale) || Math.Abs(a.LinetypeScale - b.LinetypeScale) <= ScaleTolerance)
        && SameText(a.Lineweight, b.Lineweight, ignored, ComparedProperty.Lineweight)
        && SameText(a.Transparency, b.Transparency, ignored, ComparedProperty.Transparency);

    private static bool SameText(string a, string b, ComparedProperty ignored, ComparedProperty property) =>
        ignored.HasFlag(property) || string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
