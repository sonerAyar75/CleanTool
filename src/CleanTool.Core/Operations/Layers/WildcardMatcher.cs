using System.Text.RegularExpressions;

namespace CleanTool.Core.Operations.Layers;

/// <summary>Simple AutoCAD-style wildcard matching: * (any sequence), ? (one character). Case insensitive.</summary>
public static class WildcardMatcher
{
    /// <summary>Does <paramref name="input"/> match the wildcard <paramref name="pattern"/>?</summary>
    /// <param name="input">Text to test, e.g. a layer or block name.</param>
    /// <param name="pattern">Pattern with * and ? wildcards; other characters match literally.</param>
    public static bool IsMatch(string input, string pattern)
    {
        var regex = "^" + Regex.Escape(pattern).Replace(@"\*", ".*").Replace(@"\?", ".") + "$";
        return Regex.IsMatch(input, regex, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }
}
