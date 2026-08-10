/// <summary>
/// The date/time half of <see cref="NumberFormat"/>.
///
/// The one genuine ambiguity in the format language lives here: <c>m</c> is BOTH the month token and
/// the minute token, and which it is depends on position — a month unless it directly follows an
/// hour token or directly precedes a seconds token. <c>mm/dd/yy</c> is a date; <c>hh:mm:ss</c> holds
/// a minute in the middle; and <c>m/d/yy h:mm</c> contains one of each.
/// </summary>
static partial class NumberFormat
{
    static readonly string[] shortMonths =
        ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];

    static readonly string[] longMonths =
    [
        "January", "February", "March", "April", "May", "June",
        "July", "August", "September", "October", "November", "December"
    ];

    static readonly string[] shortDays = ["Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat"];

    static readonly string[] longDays =
        ["Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday"];

    static string FormatDateTime(double serial, string body)
    {
        var moment = FromSerial(serial);
        var twelveHour = HasAmPm(body);
        var result = new StringBuilder();

        for (var i = 0; i < body.Length; i++)
        {
            var ch = body[i];

            switch (ch)
            {
                case '"':
                    var close = body.IndexOf('"', i + 1);
                    if (close < 0)
                    {
                        return result.ToString();
                    }

                    result.Append(body[(i + 1)..close]);
                    i = close;
                    continue;

                case '\\' when i + 1 < body.Length:
                    result.Append(body[++i]);
                    continue;

                case '_' when i + 1 < body.Length:
                    result.Append(' ');
                    i++;
                    continue;

                case '*' when i + 1 < body.Length:
                    i++;
                    continue;

                case '[':
                    var end = body.IndexOf(']', i);
                    if (end < 0)
                    {
                        return result.ToString();
                    }

                    // [h] / [mm] / [s] are elapsed totals rather than clock components, so they are
                    // not wrapped: 30 hours stays 30, not 6.
                    result.Append(Elapsed(body[(i + 1)..end], serial));
                    i = end;
                    continue;
            }

            var run = RunLength(body, i, out var token);
            switch (token)
            {
                case 'y':
                    result.Append(run <= 2
                        ? (moment.Year % 100).ToString("D2", CultureInfo.InvariantCulture)
                        : moment.Year.ToString("D4", CultureInfo.InvariantCulture));
                    break;

                case 'm':
                    result.Append(IsMinute(body, i, run)
                        ? moment.Minute.ToString(run >= 2 ? "D2" : "D1", CultureInfo.InvariantCulture)
                        : Month(moment.Month, run));
                    break;

                case 'd':
                    result.Append(run switch
                    {
                        1 => moment.Day.ToString(CultureInfo.InvariantCulture),
                        2 => moment.Day.ToString("D2", CultureInfo.InvariantCulture),
                        3 => shortDays[(int) moment.DayOfWeek],
                        _ => longDays[(int) moment.DayOfWeek]
                    });
                    break;

                case 'h':
                    var hour = twelveHour ? ToTwelveHour(moment.Hour) : moment.Hour;
                    result.Append(hour.ToString(run >= 2 ? "D2" : "D1", CultureInfo.InvariantCulture));
                    break;

                case 's':
                    result.Append(moment.Second.ToString(run >= 2 ? "D2" : "D1", CultureInfo.InvariantCulture));
                    break;

                case 'a':
                    result.Append(moment.Hour < 12 ? "AM" : "PM");
                    break;

                default:
                    result.Append(ch);
                    run = 1;
                    break;
            }

            i += run - 1;
        }

        return result.ToString();
    }

    /// <summary>
    /// Converts a serial to a moment, reproducing the 1900 leap-year bug: Excel counts a
    /// 29 February 1900 that did not exist, so serials from 61 on are one day ahead of a true day
    /// count. Subtracting a day for those lands every real date correctly.
    /// </summary>
    static DateTime FromSerial(double serial)
    {
        var days = Math.Floor(serial);
        var fraction = serial - days;

        // Serials at or below 59 predate the phantom 29 February and so count from 1899-12-31;
        // 60 onward absorb it and count from 1899-12-30. Serial 1 is 1900-01-01 either way, and
        // every real date from 1900-03-01 lands correctly.
        var origin = days < 60 ? epoch.AddDays(1) : epoch;
        return origin.AddDays(days).AddSeconds(Math.Round(fraction * 86400));
    }

    static string Elapsed(string token, double serial)
    {
        var lower = token.ToLowerInvariant();
        var digits = lower.Length;
        var totalSeconds = Math.Round(serial * 86400);

        var value = lower[0] switch
        {
            'h' => Math.Floor(totalSeconds / 3600),
            'm' => Math.Floor(totalSeconds / 60),
            's' => totalSeconds,
            _ => 0
        };

        return ((long) value).ToString(new('0', Math.Max(1, digits)), CultureInfo.InvariantCulture);
    }

    static string Month(int month, int run) =>
        run switch
        {
            1 => month.ToString(CultureInfo.InvariantCulture),
            2 => month.ToString("D2", CultureInfo.InvariantCulture),
            3 => shortMonths[month - 1],
            4 => longMonths[month - 1],
            _ => longMonths[month - 1][..1]
        };

    /// <summary>
    /// Whether an <c>m</c> run is a minute rather than a month: true when the nearest meaningful
    /// token before it is an hour, or the nearest after it is a second.
    /// </summary>
    static bool IsMinute(string body, int index, int run)
    {
        for (var i = index - 1; i >= 0; i--)
        {
            var ch = char.ToLowerInvariant(body[i]);

            // An elapsed bracket counts as the hour it precedes: "[h]:mm" holds a minute.
            if (ch == ']')
            {
                var open = body.LastIndexOf('[', i);
                if (open < 0)
                {
                    break;
                }

                if (body[(open + 1)..i].ToLowerInvariant().StartsWith('h'))
                {
                    return true;
                }

                i = open;
                continue;
            }

            if (ch == 'h')
            {
                return true;
            }

            if (char.IsAsciiLetter(ch) || ch is not (':' or ' ' or '.'))
            {
                break;
            }
        }

        for (var i = index + run; i < body.Length; i++)
        {
            var ch = char.ToLowerInvariant(body[i]);
            if (ch == 's')
            {
                return true;
            }

            if (char.IsAsciiLetter(ch))
            {
                break;
            }

            if (ch is not (':' or ' ' or '.'))
            {
                break;
            }
        }

        return false;
    }

    static bool HasAmPm(string body) =>
        body.Contains("AM/PM", StringComparison.OrdinalIgnoreCase) ||
        body.Contains("A/P", StringComparison.OrdinalIgnoreCase);

    static int ToTwelveHour(int hour)
    {
        var wrapped = hour % 12;
        return wrapped == 0 ? 12 : wrapped;
    }

    /// <summary>
    /// Length of the run of the same token character starting at <paramref name="index"/>, with
    /// <c>AM/PM</c> collapsed to a single 'a' token.
    /// </summary>
    static int RunLength(string body, int index, out char token)
    {
        var ch = char.ToLowerInvariant(body[index]);
        if (ch is 'a' && body.Length - index >= 5 &&
            body[index..(index + 5)].Equals("AM/PM", StringComparison.OrdinalIgnoreCase))
        {
            token = 'a';
            return 5;
        }

        if (ch is 'a' && body.Length - index >= 3 &&
            body[index..(index + 3)].Equals("A/P", StringComparison.OrdinalIgnoreCase))
        {
            token = 'a';
            return 3;
        }

        if (ch is not ('y' or 'm' or 'd' or 'h' or 's'))
        {
            token = ch;
            return 1;
        }

        token = ch;
        var run = 1;
        while (index + run < body.Length && char.ToLowerInvariant(body[index + run]) == ch)
        {
            run++;
        }

        return run;
    }
}
