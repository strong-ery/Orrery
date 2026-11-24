using System.Globalization;
using System.Text.RegularExpressions;

namespace Orrery.Core.Engine;

/// <summary>
/// Reads a step as typed: "Write up notes 30m" is a step called "Write up notes" that takes half an hour.
/// Only a time at the very end counts, so "Call mom at 5" is left alone.
/// </summary>
public static partial class StepText
{
    [GeneratedRegex(@"^(?<title>.*?\S)\s+~?(?:(?<h>\d+(?:\.\d+)?)\s*(?:h|hr|hrs|hour|hours)(?:\s*(?<m2>\d+)\s*(?:m|min|mins)?)?|(?<m>\d+)\s*(?:m|min|mins|minutes?))$",
        RegexOptions.IgnoreCase)]
    private static partial Regex Trailing();

    public static (string Title, int? Minutes) Parse(string text)
    {
        var s = text.Trim();
        if (Trailing().Match(s) is not { Success: true } m)
            return (s, null);
        double minutes;
        if (m.Groups["h"].Success)
        {
            minutes = double.Parse(m.Groups["h"].Value, CultureInfo.InvariantCulture) * 60;
            if (m.Groups["m2"].Success)
                minutes += int.Parse(m.Groups["m2"].Value, CultureInfo.InvariantCulture);
        }
        else
            minutes = int.Parse(m.Groups["m"].Value, CultureInfo.InvariantCulture);
        var rounded = (int)Math.Round(minutes);
        return rounded is > 0 and <= 24 * 60 ? (m.Groups["title"].Value, rounded) : (s, null);
    }
}
