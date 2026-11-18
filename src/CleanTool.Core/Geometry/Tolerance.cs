using System.Globalization;

namespace CleanTool.Core.Geometry;

/// <summary>
/// A positive tolerance value. Invalid values (zero, negative, NaN, infinity) are rejected.
/// When default(Tolerance) is used, <see cref="DefaultValue"/> applies.
/// </summary>
public readonly struct Tolerance
{
    /// <summary>Tolerance used when none is given: 1e-6 drawing units.</summary>
    public const double DefaultValue = 1e-6;

    private readonly double _value;

    /// <summary>Creates a tolerance.</summary>
    /// <param name="value">A positive, finite value.</param>
    /// <exception cref="ArgumentOutOfRangeException">The value is zero, negative, NaN or infinite.</exception>
    public Tolerance(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value) || value <= 0)
            throw new ArgumentOutOfRangeException(nameof(value), value, "Tolerance must be a positive, finite number.");
        _value = value;
    }

    /// <summary>The tolerance value (always positive).</summary>
    public double Value => _value > 0 ? _value : DefaultValue;

    /// <summary>A tolerance of <see cref="DefaultValue"/>.</summary>
    public static Tolerance Default => new(DefaultValue);

    /// <summary>Is the value equal to zero within the tolerance?</summary>
    public bool IsZero(double value) => Math.Abs(value) <= Value;

    /// <summary>Are the two values equal within the tolerance?</summary>
    public bool AreEqual(double a, double b) => Math.Abs(a - b) <= Value;

    /// <summary>
    /// Parses user input. Both "0.001" and "0,001" are accepted (for locales that use a decimal comma).
    /// Returns false for empty, non-numeric or non-positive input.
    /// </summary>
    public static bool TryParse(string? text, out Tolerance tolerance)
    {
        tolerance = Default;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var normalized = text.Trim().Replace(',', '.');
        if (!double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)) return false;
        if (double.IsNaN(value) || double.IsInfinity(value) || value <= 0) return false;

        tolerance = new Tolerance(value);
        return true;
    }

    public override string ToString() => Value.ToString("G", CultureInfo.InvariantCulture);
}
