/// <summary>
/// The rules of an SVG document's <c>style</c> elements, for the one kind of selector Office and
/// the drawing tools that feed it write: an element name, an <c>#id</c>, one or more
/// <c>.class</c> names, or those run together (<c>path.accent</c>), in comma-separated lists. An
/// Office icon keeps each theme colour in a rule like <c>.MsftOfcThm_Accent1_Fill_v2 { fill:#003780 }</c>
/// and repeats it on the element as a <c>fill</c> attribute — and when the two disagree the rule is
/// the one Office draws (the attribute is the colour before the theme was applied), which is what
/// the cascade says too: a style sheet outranks a presentation attribute.
///
/// Anything a rule of that shape cannot say — a combinator, a pseudo-class, an attribute selector,
/// an at-rule, <c>!important</c> — makes <see cref="TryParse"/> fail, and the reader gives the
/// document up rather than draw it with some of its styling missing.
/// </summary>
sealed class SvgStyleSheet
{
    sealed record Rule(string? Element, string? Id, string[] Classes, Dictionary<string, string> Declarations)
    {
        // An id outranks any number of classes, and a class any element name.
        public int Specificity => (Id == null ? 0 : 10000) + Classes.Length * 100 + (Element == null ? 0 : 1);
    }

    List<Rule> rules = [];

    /// <summary>
    /// Reads the text of every <c>style</c> element, in document order. False, naming what stood in
    /// the way, for anything beyond the simple rules above.
    /// </summary>
    public static bool TryParse(IEnumerable<string> sheets, [NotNullWhen(true)] out SvgStyleSheet? styleSheet, [NotNullWhen(false)] out string? unsupported)
    {
        var parsed = new SvgStyleSheet();
        foreach (var sheet in sheets)
        {
            unsupported = parsed.Read(StripComments(sheet));
            if (unsupported != null)
            {
                styleSheet = null;
                return false;
            }
        }

        // The cascade: a more specific rule over a less specific one, and a later rule over an
        // earlier one of the same weight — which the stable sort leaves in the order they came.
        // Sorted once, so applying is a walk in order.
        parsed.rules = parsed.rules.OrderBy(_ => _.Specificity).ToList();
        styleSheet = parsed;
        unsupported = null;
        return true;
    }

    // Null when the whole sheet was taken; otherwise what in it is beyond these rules.
    string? Read(string text)
    {
        if (text.Contains('@'))
        {
            return "an at-rule in a style sheet";
        }

        var position = 0;
        while (true)
        {
            var open = text.IndexOf('{', position);
            if (open < 0)
            {
                return string.IsNullOrWhiteSpace(text[position..]) ? null : "a style sheet that does not parse";
            }

            var close = text.IndexOf('}', open);
            if (close < 0 || text.IndexOf('{', open + 1, close - open - 1) >= 0)
            {
                return "a style sheet that does not parse";
            }

            var declarations = new Dictionary<string, string>(StringComparer.Ordinal);
            if (!TryParseDeclarations(text[(open + 1)..close], declarations))
            {
                return "!important in a style sheet";
            }

            foreach (var selector in text[position..open].Split(','))
            {
                if (!TryParseSelector(selector.Trim(), declarations, out var rule))
                {
                    return $"the style selector '{selector.Trim()}'";
                }

                rules.Add(rule);
            }

            position = close + 1;
        }
    }

    /// <summary>
    /// Lays every rule that matches an element over <paramref name="properties"/>, weakest first,
    /// so the strongest is what is left standing.
    /// </summary>
    public void Apply(string element, string? id, string[] classes, Dictionary<string, string> properties)
    {
        foreach (var rule in rules)
        {
            if ((rule.Element != null && rule.Element != element) ||
                (rule.Id != null && rule.Id != id))
            {
                continue;
            }

            var matches = true;
            foreach (var name in rule.Classes)
            {
                if (Array.IndexOf(classes, name) < 0)
                {
                    matches = false;
                    break;
                }
            }

            if (!matches)
            {
                continue;
            }

            foreach (var (property, value) in rule.Declarations)
            {
                properties[property] = value;
            }
        }
    }

    /// <summary>
    /// Reads a block of <c>name: value;</c> declarations — a rule's body, or a <c>style</c>
    /// attribute — over <paramref name="properties"/>. Property names are lower-cased, since CSS
    /// reads them without regard to case. False when a declaration is marked <c>!important</c>,
    /// which would have to outrank declarations the plain cascade puts above it.
    /// </summary>
    public static bool TryParseDeclarations(string text, Dictionary<string, string> properties)
    {
        foreach (var declaration in text.Split(';'))
        {
            var colon = declaration.IndexOf(':');
            if (colon < 0)
            {
                continue;
            }

            var value = declaration[(colon + 1)..].Trim();
            if (value.Contains('!'))
            {
                return false;
            }

            properties[declaration[..colon].Trim().ToLowerInvariant()] = value;
        }

        return true;
    }

    static bool TryParseSelector(string selector, Dictionary<string, string> declarations, [NotNullWhen(true)] out Rule? rule)
    {
        rule = null;
        string? element = null;
        string? id = null;
        var classes = new List<string>();
        var position = 0;
        if (selector.Length == 0)
        {
            return false;
        }

        if (selector[0] == '*')
        {
            position = 1;
        }
        else if (IsNameCharacter(selector[0]))
        {
            element = ReadName(selector, ref position);
        }

        while (position < selector.Length)
        {
            var marker = selector[position];
            position++;
            if (marker is not ('.' or '#') || position >= selector.Length || !IsNameCharacter(selector[position]))
            {
                return false;
            }

            var name = ReadName(selector, ref position);
            if (marker == '.')
            {
                classes.Add(name);
            }
            else if (id == null)
            {
                id = name;
            }
            else
            {
                return false;
            }
        }

        rule = new(element, id, classes.ToArray(), declarations);
        return true;
    }

    static string ReadName(string selector, ref int position)
    {
        var start = position;
        while (position < selector.Length && IsNameCharacter(selector[position]))
        {
            position++;
        }

        return selector[start..position];
    }

    static bool IsNameCharacter(char character) =>
        char.IsAsciiLetterOrDigit(character) || character is '-' or '_' || character > 0x7F;

    // Comments, and the <!-- --> a style sheet written for old browsers is wrapped in.
    static string StripComments(string sheet)
    {
        var text = new StringBuilder(sheet.Length);
        var position = 0;
        while (position < sheet.Length)
        {
            var open = sheet.IndexOf("/*", position, StringComparison.Ordinal);
            if (open < 0)
            {
                text.Append(sheet, position, sheet.Length - position);
                break;
            }

            text.Append(sheet, position, open - position);
            var close = sheet.IndexOf("*/", open + 2, StringComparison.Ordinal);
            if (close < 0)
            {
                break;
            }

            position = close + 2;
        }

        return text.Replace("<!--", "").Replace("-->", "").ToString();
    }
}
