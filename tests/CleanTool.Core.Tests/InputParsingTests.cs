using CleanTool.Core.Geometry;
using CleanTool.Core.Operations.Layers;

namespace CleanTool.Core.Tests;

/// <summary>Parsing of text input coming from the dialog (the dialog uses these Core methods).</summary>
public class InputParsingTests
{
    [Theory]
    [InlineData("0.001", 0.001)]
    [InlineData("0,001", 0.001)]   // decimal comma (e.g. Turkish locale)
    [InlineData(" 1e-6 ", 1e-6)]
    [InlineData("5", 5)]
    public void Tolerance_accepts_valid_input(string text, double expected)
    {
        Assert.True(Tolerance.TryParse(text, out var tolerance));
        Assert.Equal(expected, tolerance.Value, 12);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("abc")]
    [InlineData("0")]
    [InlineData("-0.1")]
    [InlineData("NaN")]
    public void Tolerance_rejects_invalid_input(string? text)
    {
        Assert.False(Tolerance.TryParse(text, out _));
    }

    [Fact]
    public void Tolerance_round_trips_through_text_regardless_of_culture()
    {
        var original = new Tolerance(0.0005);

        Assert.True(Tolerance.TryParse(original.ToString(), out var parsed));
        Assert.Equal(original.Value, parsed.Value);
    }

    [Fact]
    public void Patterns_are_split_trimmed_and_deduplicated()
    {
        var patterns = LayerPurgeOptions.ParsePatterns(" A-* ; TEMP?;;a-*\nXREF*\r\n");

        Assert.Equal(new[] { "A-*", "TEMP?", "XREF*" }, patterns);
    }

    [Fact]
    public void Comma_is_part_of_a_layer_name_not_a_separator()
    {
        Assert.Equal(new[] { "WALL,EXT" }, LayerPurgeOptions.ParsePatterns("WALL,EXT"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ; ; ")]
    public void Empty_pattern_text_gives_no_patterns(string? text)
    {
        Assert.Empty(LayerPurgeOptions.ParsePatterns(text));
    }

    [Fact]
    public void Patterns_round_trip_through_text()
    {
        var text = LayerPurgeOptions.FormatPatterns(new[] { "A-*", "TEMP?" });

        Assert.Equal(new[] { "A-*", "TEMP?" }, LayerPurgeOptions.ParsePatterns(text));
    }
}
