using Autodesk.AutoCAD.DatabaseServices;
using CleanTool.Core.Model;
using AcColor = Autodesk.AutoCAD.Colors.Color;
using AcTransparency = Autodesk.AutoCAD.Colors.Transparency;

namespace CleanTool.AcadTool.ShapeReading;

/// <summary>Converts an AutoCAD object's display properties into strings that Core can compare.</summary>
internal static class PropertyReader
{
    /// <summary>Reads layer, color, linetype, linetype scale, lineweight and transparency.</summary>
    public static DisplayProperties Read(Entity entity) => new(
        entity.Layer,
        ColorKey(entity.Color),
        entity.Linetype,
        entity.LinetypeScale,
        entity.LineWeight.ToString(),
        TransparencyKey(entity.Transparency));

    private static string ColorKey(AcColor color)
    {
        if (color.IsByLayer) return "BYLAYER";
        if (color.IsByBlock) return "BYBLOCK";
        if (color.IsByAci) return color.ColorIndex.ToString();
        return $"{color.Red},{color.Green},{color.Blue}";
    }

    private static string TransparencyKey(AcTransparency transparency)
    {
        if (transparency.IsByLayer) return "BYLAYER";
        if (transparency.IsByBlock) return "BYBLOCK";
        return transparency.Alpha.ToString();
    }
}
