namespace Orrery.Core.Design;

/// <summary>Understands the dates people actually type: "tomorrow", "friday", "dec 10", "12/10", "in 3 days".</summary>
public static class DateParser
{
    private static readonly string[] Months =
        ["jan", "feb", "mar", "apr", "may", "jun", "jul", "aug", "sep", "oct", "nov", "dec"];
    private static readonly string[] Days = ["sun", "mon", "tue", "wed", "thu", "fri", "sat"];

    /// <summary>The pattern that matches any phrase <see cref="TryParse"/> accepts, for pulling one out of a longer line.</summary>
    public const string Pattern =
        @"today|tomorrow|(?:sun|mon|tue|wed|thu|fri|sat)[a-z]*|(?:jan|feb|mar|apr|may|jun|jul|aug|sep|oct|nov|dec)[a-z]*\.? \d{1,2}|\d{1,2} (?:jan|feb|mar|apr|may|jun|jul|aug|sep|oct|nov|dec)[a-z]*|\d{1,2}/\d{1,2}|in \d+ ?(?:days?|d|weeks?|w)";

    public static bool TryParse(string text, DateOnly today, out DateOnly date)
    {
        date = default;
        var t = text.Trim().ToLowerInvariant().TrimEnd('.');
        if (t.Length == 0)
            return false;

        if (t == "today") { date = today; return true; }
        if (t == "tomorrow") { date = today.AddDays(1); return true; }

        var rel = System.Text.RegularExpressions.Regex.Match(t, @"^in (\d+) ?(days?|d|weeks?|w)$");
        if (rel.Success)
        {
            var n = int.Parse(rel.Groups[1].Value);
            date = today.AddDays(rel.Groups[2].Value.StartsWith('w') ? n * 7 : n);
            return true;
        }

        var weekday = Array.FindIndex(Days, d => t.StartsWith(d, StringComparison.Ordinal) && t.Length >= 3 && t.All(char.IsLetter));
        if (weekday >= 0)
        {
            var ahead = ((weekday - (int)today.DayOfWeek) + 7) % 7;
            date = today.AddDays(ahead == 0 ? 7 : ahead);
            return true;
        }

        var slash = System.Text.RegularExpressions.Regex.Match(t, @"^(\d{1,2})/(\d{1,2})$");
        if (slash.Success)
        {
            var a = int.Parse(slash.Groups[1].Value);
            var b = int.Parse(slash.Groups[2].Value);
            var (month, day) = a <= 12 ? (a, b) : (b, a);
            return Build(today, month, day, out date);
        }

        var named = System.Text.RegularExpressions.Regex.Match(t, @"^([a-z]+)\.? (\d{1,2})$");
        if (!named.Success)
        {
            var flipped = System.Text.RegularExpressions.Regex.Match(t, @"^(\d{1,2}) ([a-z]+)$");
            if (flipped.Success)
                named = System.Text.RegularExpressions.Regex.Match($"{flipped.Groups[2].Value} {flipped.Groups[1].Value}", @"^([a-z]+) (\d{1,2})$");
        }
        if (named.Success)
        {
            var m = Array.FindIndex(Months, x => named.Groups[1].Value.StartsWith(x, StringComparison.Ordinal));
            if (m >= 0)
                return Build(today, m + 1, int.Parse(named.Groups[2].Value), out date);
        }
        return false;
    }

    private static bool Build(DateOnly today, int month, int day, out DateOnly date)
    {
        date = default;
        if (month is < 1 or > 12 || day < 1 || day > DateTime.DaysInMonth(today.Year, month))
            return false;
        date = new DateOnly(today.Year, month, day);
        if (date < today)
        {
            if (day > DateTime.DaysInMonth(today.Year + 1, month))
                return false;
            date = new DateOnly(today.Year + 1, month, day);
        }
        return true;
    }
}
