/// <summary>
/// The viewer's find: every occurrence of a query across a document's pages, as offsets into each page's
/// <see cref="Morph.PageTextLayer.Text"/> — the text the page's layer holds in its DOM, so morph-text.js
/// can turn an offset straight into a highlight range.
///
/// Whitespace is loose, the way a browser's find treats rendered text: any run of spaces, tabs and line
/// breaks in the page matches any run in the query. That is what lets a phrase match across a soft wrap,
/// a paragraph's line break or a table cell. A match never spans pages (PDF.js's rule too).
/// </summary>
sealed class TextSearch(IEnumerable<string> pageTexts)
{
    // Past this the count reads "10000+" and the rest is not worth materialising.
    internal const int MaxMatches = 10_000;

    readonly Page[] pages = pageTexts.Select(Collapse).ToArray();

    public IReadOnlyList<TextMatch> Find(string query, bool matchCase)
    {
        var needle = Collapse(query.Trim()).Text;
        var matches = new List<TextMatch>();
        if (needle.Length == 0)
        {
            return matches;
        }

        var comparison = matchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        for (var pageIndex = 0; pageIndex < pages.Length; pageIndex++)
        {
            var page = pages[pageIndex];
            var from = 0;
            while (from <= page.Text.Length - needle.Length)
            {
                var found = page.Text.IndexOf(needle, from, comparison);
                if (found < 0)
                {
                    break;
                }

                // The needle neither starts nor ends in whitespace, so both ends land on real characters
                // of the original text; a collapsed run in the middle spans its original extent.
                var start = page.Map[found];
                var end = page.Map[found + needle.Length - 1] + 1;
                matches.Add(new(pageIndex, start, end - start));
                if (matches.Count == MaxMatches)
                {
                    return matches;
                }

                from = found + needle.Length;
            }
        }

        return matches;
    }

    // The text with every whitespace run reduced to one space, plus where each kept character came from.
    static Page Collapse(string text)
    {
        var builder = new StringBuilder(text.Length);
        var map = new List<int>(text.Length);
        var inWhitespace = false;
        for (var index = 0; index < text.Length; index++)
        {
            var ch = text[index];
            if (char.IsWhiteSpace(ch))
            {
                if (inWhitespace)
                {
                    continue;
                }

                inWhitespace = true;
                builder.Append(' ');
            }
            else
            {
                inWhitespace = false;
                builder.Append(ch);
            }

            map.Add(index);
        }

        return new(builder.ToString(), [.. map]);
    }

    readonly record struct Page(string Text, int[] Map);
}
