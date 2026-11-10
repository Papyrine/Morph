/// <summary>The special items of a structured reference.</summary>
[Flags]
enum StructuredItems
{
    None = 0,
    All = 1,
    Data = 2,
    Headers = 4,
    Totals = 8,
    ThisRow = 16
}

/// <summary>
/// What a structured reference's brackets say: which of a table's rows — <c>#All</c>, <c>#Data</c>,
/// <c>#Headers</c>, <c>#Totals</c>, <c>#This Row</c> (written <c>@</c> too) — and which of its columns,
/// one or a span of them, or every column when none is named. <c>Table1[]</c> and <c>Table1[Price]</c>
/// are its data; <c>Table1[[#This Row],[Price]]</c> one cell.
/// </summary>
sealed record StructuredSpecifier(StructuredItems Items, string? FirstColumn, string? LastColumn)
{
    public static StructuredSpecifier Parse(string bracketed)
    {
        if (bracketed.Length < 2 ||
            bracketed[0] != '[' ||
            bracketed[^1] != ']')
        {
            throw new FormulaSyntaxException("A table's reference is not understood.", 0);
        }

        // Not trimmed: a column's name may end in a space, as probate-inventory's [Balance ] does.
        var inner = bracketed[1..^1];
        var items = StructuredItems.None;
        if (inner.AsSpan().TrimStart().StartsWith('@'))
        {
            items = StructuredItems.ThisRow;
            inner = inner.TrimStart()[1..];
        }

        // Excel lets spaces stand around the parts: [ [#This Row], [Price] ].
        if (inner.AsSpan().TrimStart().StartsWith('['))
        {
            inner = inner.Trim();
        }

        if (string.IsNullOrWhiteSpace(inner))
        {
            return new(Rows(items), null, null);
        }

        // [Price] or [@Price]: one column, its name as written (but for ' escapes).
        if (!inner.StartsWith('['))
        {
            if (inner.StartsWith('#'))
            {
                return new(Rows(items | Item(inner)), null, null);
            }

            return new(Rows(items), Unescape(inner), Unescape(inner));
        }

        string? first = null;
        string? last = null;
        foreach (var part in Parts(inner))
        {
            var span = SplitSpan(part);
            foreach (var each in span)
            {
                var name = each.Trim();
                if (name.Length < 2 ||
                    name[0] != '[' ||
                    name[^1] != ']')
                {
                    throw new FormulaSyntaxException("A table's reference is not understood.", 0);
                }

                name = name[1..^1];
                if (name.StartsWith('#'))
                {
                    items |= Item(name);
                    continue;
                }

                var column = Unescape(name);
                first ??= column;
                last = column;
            }
        }

        return new(Rows(items), first, last);
    }

    // No rows named is the data's, as Excel reads Table1[Price].
    static StructuredItems Rows(StructuredItems items)
    {
        if (items == StructuredItems.None)
        {
            return StructuredItems.Data;
        }

        return items;
    }

    static StructuredItems Item(string name)
    {
        var trimmed = name.AsSpan().Trim();
        if (trimmed.Equals("#ALL", StringComparison.OrdinalIgnoreCase))
        {
            return StructuredItems.All;
        }

        if (trimmed.Equals("#DATA", StringComparison.OrdinalIgnoreCase))
        {
            return StructuredItems.Data;
        }

        if (trimmed.Equals("#HEADERS", StringComparison.OrdinalIgnoreCase))
        {
            return StructuredItems.Headers;
        }

        if (trimmed.Equals("#TOTALS", StringComparison.OrdinalIgnoreCase))
        {
            return StructuredItems.Totals;
        }

        if (trimmed.Equals("#THIS ROW", StringComparison.OrdinalIgnoreCase))
        {
            return StructuredItems.ThisRow;
        }

        throw new FormulaSyntaxException($"'{name}' is not one of a table's items.", 0);
    }

    // The comma-separated parts at the outer depth, each [x] or [x]:[y].
    static IEnumerable<string> Parts(string inner)
    {
        var depth = 0;
        var start = 0;
        for (var index = 0; index < inner.Length; index++)
        {
            var ch = inner[index];
            if (ch == '\'')
            {
                index++;
                continue;
            }

            if (ch == '[')
            {
                depth++;
            }
            else if (ch == ']')
            {
                depth--;
            }
            else if (ch == ',' &&
                     depth == 0)
            {
                yield return inner[start..index];
                start = index + 1;
            }
        }

        yield return inner[start..];
    }

    static IEnumerable<string> SplitSpan(string part)
    {
        var depth = 0;
        for (var index = 0; index < part.Length; index++)
        {
            var ch = part[index];
            if (ch == '\'')
            {
                index++;
                continue;
            }

            if (ch == '[')
            {
                depth++;
            }
            else if (ch == ']')
            {
                depth--;
            }
            else if (ch == ':' &&
                     depth == 0)
            {
                return [part[..index], part[(index + 1)..]];
            }
        }

        return [part];
    }

    // ' escapes the character after it: [BIN '#] is the column "BIN #".
    static string Unescape(string name)
    {
        if (!name.Contains('\''))
        {
            return name;
        }

        var builder = new StringBuilder(name.Length);
        for (var index = 0; index < name.Length; index++)
        {
            if (name[index] == '\'' &&
                index + 1 < name.Length)
            {
                index++;
            }

            builder.Append(name[index]);
        }

        return builder.ToString();
    }
}
