using System.Text.RegularExpressions;

/// <summary>
/// Formats a cell value the way Excel would, from an ECMA-376 §18.8.30 format code.
///
/// A spreadsheet stores numbers, not the text a reader sees: <c>44927</c> renders as
/// <c>1/1/2023</c>, <c>-1234.5</c> as <c>$(1,234.50)</c> in red, and the same serial can be a date,
/// a currency or a phone number depending only on the code attached to it. Nothing else in Morph
/// needs this, because no other input format separates value from presentation this way.
///
/// A code is up to four sections separated by <c>;</c> — positive, negative, zero, text — and each
/// may carry a bracketed condition (<c>[&lt;=9999999]</c>) or colour (<c>[Red]</c>). Section choice
/// is the first thing resolved, because it decides whether the value is even shown as a number.
/// </summary>
static partial class NumberFormat
{
    /// <summary>
    /// Excel's day 0. Serial 1 is 1900-01-01, and the epoch sits at 1899-12-30 rather than 12-31
    /// because Excel deliberately reproduces Lotus 1-2-3's belief that 1900 was a leap year: serial
    /// 60 is a 29 February that never existed. Dates from 1900-03-01 on — every date the corpus
    /// contains — land correctly by counting from two days early, which is why the bug is
    /// reproduced rather than corrected.
    /// </summary>
    static readonly DateTime epoch = new(1899, 12, 30);

    /// <summary>Formats <paramref name="value"/>, or returns text unchanged when the cell holds a string.</summary>
    public static FormattedValue Format(double value, string? formatCode) =>
        FormatCore(value, string.IsNullOrEmpty(formatCode) ? "General" : formatCode);

    /// <summary>Formats a text cell: only the fourth section, if present, applies.</summary>
    public static FormattedValue FormatText(string text, string? formatCode)
    {
        if (string.IsNullOrEmpty(formatCode))
        {
            return new(text, null);
        }

        var sections = Split(formatCode);
        if (sections.Count < 4)
        {
            return new(text, null);
        }

        var section = sections[3];
        return new(ApplyTextSection(section.Body, text), section.ColorHex);
    }

    static FormattedValue FormatCore(double value, string formatCode)
    {
        var sections = Split(formatCode);
        var (section, negated) = Select(sections, value);
        if (section == null)
        {
            return new(General(value), null);
        }

        var body = section.Value.Body;
        if (body.Length == 0)
        {
            // An empty section is Excel's way of hiding a value — "0.00;;" shows nothing for zero.
            return new(string.Empty, section.Value.ColorHex);
        }

        if (IsGeneral(body))
        {
            return new(General(negated ? Math.Abs(value) : value), section.Value.ColorHex);
        }

        var magnitude = negated ? Math.Abs(value) : value;
        var text = IsDateTime(body) ? FormatDateTime(magnitude, body) : FormatNumber(magnitude, body);
        return new(text, section.Value.ColorHex);
    }

    /// <summary>
    /// Picks the section for a value. Without conditions the order is positive / negative / zero,
    /// and a negative rendered by the SECOND section has its sign consumed by that section (the code
    /// supplies its own minus or parentheses) — hence the <c>negated</c> flag. With explicit
    /// conditions the sections are tested in order and the last acts as the else branch.
    /// </summary>
    static (Section? Section, bool Negated) Select(IReadOnlyList<Section> sections, double value)
    {
        if (sections.Count == 0)
        {
            return (null, false);
        }

        if (sections.Any(_ => _.Condition != null))
        {
            foreach (var candidate in sections)
            {
                if (candidate.Condition == null)
                {
                    // The first unconditional section is the else branch.
                    return (candidate, value < 0 && sections.Count > 1);
                }

                if (candidate.Condition.Value.Matches(value))
                {
                    return (candidate, false);
                }
            }

            return (sections[^1], false);
        }

        if (value < 0 && sections.Count > 1)
        {
            return (sections[1], true);
        }

        if (value == 0 && sections.Count > 2)
        {
            return (sections[2], false);
        }

        return (sections[0], false);
    }

    /// <summary>
    /// Splits a code into sections on unquoted semicolons, lifting each section's leading
    /// <c>[condition]</c> and <c>[Color]</c> brackets out of the body.
    /// </summary>
    static List<Section> Split(string formatCode)
    {
        var sections = new List<Section>();
        var current = new StringBuilder();
        var quoted = false;

        for (var i = 0; i < formatCode.Length; i++)
        {
            var ch = formatCode[i];
            if (ch == '"')
            {
                quoted = !quoted;
                current.Append(ch);
                continue;
            }

            if (ch == '\\' && i + 1 < formatCode.Length)
            {
                current.Append(ch).Append(formatCode[++i]);
                continue;
            }

            if (ch == ';' && !quoted)
            {
                sections.Add(Section.Parse(current.ToString()));
                current.Clear();
                continue;
            }

            current.Append(ch);
        }

        sections.Add(Section.Parse(current.ToString()));
        return sections;
    }

    static bool IsGeneral(string body) =>
        body.Trim().Equals("General", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Whether a section is a date/time code rather than a numeric one.
    ///
    /// Any unquoted date token settles it, <c>m</c> included: a numeric code is built from
    /// <c>#</c>, <c>0</c>, <c>?</c>, <c>.</c>, <c>,</c>, <c>%</c> and <c>E</c>, so a bare letter
    /// cannot appear in one. Everything a numeric code could legitimately contain — quoted literals,
    /// escapes, width and fill markers, and bracketed colour/condition/locale groups — is skipped
    /// before the test, which is what keeps <c>[$$-409]#,##0.00</c> numeric despite its letters.
    /// </summary>
    static bool IsDateTime(string body)
    {
        for (var i = 0; i < body.Length; i++)
        {
            var ch = body[i];

            if (ch == '"')
            {
                var quote = body.IndexOf('"', i + 1);
                i = quote < 0 ? body.Length : quote;
                continue;
            }

            if (ch is
                    '\\' or
                    '_' or
                    '*' &&
                i + 1 < body.Length)
            {
                i++;
                continue;
            }

            if (ch == '[')
            {
                var close = body.IndexOf(']', i);
                if (close < 0)
                {
                    break;
                }

                // An elapsed-time bracket is the one bracket group that IS a date token.
                var inner = body[(i + 1)..close];
                if (inner.Length > 0 && inner.All(_ => _ is 'h' or 'm' or 's' or 'H' or 'M' or 'S'))
                {
                    return true;
                }

                i = close;
                continue;
            }

            if (char.ToLowerInvariant(ch) is 'y' or 'm' or 'd' or 'h' or 's')
            {
                return true;
            }
        }

        return false;
    }

    static string General(double value)
    {
        if (value == Math.Floor(value) && Math.Abs(value) < 1e15)
        {
            return ((long) value).ToString(CultureInfo.InvariantCulture);
        }

        // Excel's General shows up to 11 significant digits, trimming trailing zeros.
        var text = value.ToString("G11", CultureInfo.InvariantCulture);
        return text.Contains('E', StringComparison.Ordinal)
            ? value.ToString("G6", CultureInfo.InvariantCulture).Replace("E+", "E+", StringComparison.Ordinal)
            : text;
    }

    static string ApplyTextSection(string body, string text)
    {
        var result = new StringBuilder();
        for (var i = 0; i < body.Length; i++)
        {
            var ch = body[i];
            switch (ch)
            {
                case '@':
                    result.Append(text);
                    break;
                case '"':
                    var close = body.IndexOf('"', i + 1);
                    if (close < 0)
                    {
                        return result.ToString();
                    }

                    result.Append(body[(i + 1)..close]);
                    i = close;
                    break;
                case '\\' when i + 1 < body.Length:
                    result.Append(body[++i]);
                    break;
                case '_' when i + 1 < body.Length:
                    result.Append(' ');
                    i++;
                    break;
                case '*' when i + 1 < body.Length:
                    i++;
                    break;
                case '[':
                    var end = body.IndexOf(']', i);
                    i = end < 0 ? body.Length : end;
                    break;
                default:
                    result.Append(ch);
                    break;
            }
        }

        return result.ToString();
    }

    [GeneratedRegex(@"^\[(?<op><=|>=|<>|<|>|=)(?<value>-?[0-9.]+)\]", RegexOptions.ExplicitCapture)]
    private static partial Regex ConditionPattern { get; }

    [GeneratedRegex("^\\[(?<name>Red|Black|White|Blue|Green|Magenta|Yellow|Cyan|Color\\s*\\d+)\\]", RegexOptions.IgnoreCase | RegexOptions.ExplicitCapture)]
    private static partial Regex ColorPattern { get; }

    readonly record struct Condition(string Operator, double Value)
    {
        public bool Matches(double candidate) =>
            Operator switch
            {
                "<" => candidate < Value,
                "<=" => candidate <= Value,
                ">" => candidate > Value,
                ">=" => candidate >= Value,
                "<>" => Math.Abs(candidate - Value) > double.Epsilon,
                _ => Math.Abs(candidate - Value) <= double.Epsilon
            };
    }

    readonly record struct Section(string Body, string? ColorHex, Condition? Condition)
    {
        /// <summary>
        /// Lifts the leading bracket groups off a section. Only colour and condition are consumed —
        /// a locale group such as <c>[$$-409]</c> stays in the body, because it carries the currency
        /// symbol that has to be emitted.
        /// </summary>
        public static Section Parse(string raw)
        {
            string? color = null;
            Condition? condition = null;
            var body = raw;

            while (body.StartsWith('['))
            {
                var colorMatch = ColorPattern.Match(body);
                if (colorMatch.Success)
                {
                    color ??= ResolveColor(colorMatch.Groups["name"].Value);
                    body = body[colorMatch.Length..];
                    continue;
                }

                var conditionMatch = ConditionPattern.Match(body);
                if (conditionMatch.Success)
                {
                    condition ??= new(
                        conditionMatch.Groups["op"].Value,
                        double.Parse(conditionMatch.Groups["value"].Value, CultureInfo.InvariantCulture));
                    body = body[conditionMatch.Length..];
                    continue;
                }

                break;
            }

            return new(body, color, condition);
        }

        static string? ResolveColor(string name) =>
            name.ToLowerInvariant() switch
            {
                "red" => "FF0000",
                "black" => "000000",
                "white" => "FFFFFF",
                "blue" => "0000FF",
                "green" => "00B050",
                "magenta" => "FF00FF",
                "yellow" => "FFFF00",
                "cyan" => "00FFFF",
                _ => null
            };
    }
}

/// <summary>
/// A formatted cell value and the colour its format code asked for, which is a presentation choice
/// the code makes rather than anything the cell's own style declares — <c>[Red]</c> on the negative
/// section is how a spreadsheet shows losses in red.
/// </summary>
readonly record struct FormattedValue(string Text, string? ColorHex);
