using System.Globalization;
using System.Text.RegularExpressions;

namespace PD.Core.Model;

/// <summary>Formats and parses architectural feet-inches, e.g. 14'-1".</summary>
public static partial class FeetInches
{
    public static string Format(double feet)
    {
        var totalIn = (int)Math.Round(Math.Abs(feet) * 12.0);
        var sign = feet < 0 && totalIn > 0 ? "-" : "";
        return $"{sign}{totalIn / 12}'-{totalIn % 12}\"";
    }

    /// <summary>Accepts 14'-1", 14' 1", 14', 1" or a decimal number of feet.</summary>
    public static bool TryParse(string? text, out double feet)
    {
        feet = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var s = text.Trim();
        if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out feet)) return true;
        var m = FtIn().Match(s);
        if (!m.Success || (!m.Groups["ft"].Success && !m.Groups["in"].Success)) return false;
        var ft = m.Groups["ft"].Success ? double.Parse(m.Groups["ft"].Value, CultureInfo.InvariantCulture) : 0;
        var inch = m.Groups["in"].Success ? double.Parse(m.Groups["in"].Value, CultureInfo.InvariantCulture) : 0;
        var neg = m.Groups["neg"].Success;
        feet = (neg ? -1 : 1) * (Math.Abs(ft) + inch / 12.0);
        return true;
    }

    [GeneratedRegex("^(?<neg>-)?\\s*(?:(?<ft>\\d+(?:\\.\\d+)?)\\s*')?\\s*-?\\s*(?:(?<in>\\d+(?:\\.\\d+)?)\\s*\")?$")]
    private static partial Regex FtIn();
}
