namespace CleanTool.Core.Model;

/// <summary>
/// Display properties of an object. Values are the platform's raw strings
/// (e.g. Color: "BYLAYER", "1", "255,0,0"); Core only compares them and never interprets their meaning.
/// </summary>
public sealed record DisplayProperties(
    string Layer,
    string Color,
    string Linetype,
    double LinetypeScale,
    string Lineweight,
    string Transparency)
{
    /// <summary>An object on the given layer with every other property set to ByLayer.</summary>
    public static DisplayProperties OnLayer(string layer) =>
        new(layer, "BYLAYER", "BYLAYER", 1.0, "BYLAYER", "BYLAYER");

    /// <summary>
    /// The effective (displayed) properties of an object inside a block, following AutoCAD's rules:
    /// an object on layer "0" takes the insert's layer, and ByBlock properties take the insert's values.
    /// For nested blocks the insert's properties must already be resolved against its own parent.
    /// </summary>
    /// <param name="insert">Resolved properties of the block reference that displays this object.</param>
    public DisplayProperties ResolveInBlock(DisplayProperties insert) => new(
        Layer == "0" ? insert.Layer : Layer,
        IsByBlock(Color) ? insert.Color : Color,
        IsByBlock(Linetype) ? insert.Linetype : Linetype,
        LinetypeScale,
        IsByBlock(Lineweight) ? insert.Lineweight : Lineweight,
        IsByBlock(Transparency) ? insert.Transparency : Transparency);

    private static bool IsByBlock(string value) => string.Equals(value, "BYBLOCK", StringComparison.OrdinalIgnoreCase);
}

/// <summary>Properties that can be compared or ignored, like OVERKILL's "Ignore object property" options.</summary>
[Flags]
public enum ComparedProperty
{
    None = 0,
    Layer = 1 << 0,
    Color = 1 << 1,
    Linetype = 1 << 2,
    LinetypeScale = 1 << 3,
    Lineweight = 1 << 4,
    Transparency = 1 << 5,

    All = Layer | Color | Linetype | LinetypeScale | Lineweight | Transparency
}
