using S = DocumentFormat.OpenXml.Spreadsheet;

/// <summary>
/// A worksheet's printed page header and footer (<c>headerFooter</c>, ECMA-376 §18.3.1.46) as the
/// section's header and footer bands. Each is a string of up to three parts — <c>&amp;L</c>, <c>&amp;C</c>
/// and <c>&amp;R</c> — set on one line across the page between its margins: one paragraph, the centre
/// part at a centre tab stop and the right part at a right one. The text is the Normal style's font and
/// colour (invoice-accessibility-guide's footer is its accent-coloured body face), at the sheet's print
/// scale unless <c>scaleWithDoc="0"</c>: Excel shrinks the header with a sheet fitted to its page, and
/// simple-list's "To Do List" stands at the 60% its grid is printed at.
/// <c>&amp;P</c> and <c>&amp;N</c> are the page number and the page count, numbered through the whole
/// print as Excel numbers a workbook printed entire (invoice-accessibility-guide's "Page 2 of 10" on
/// its second sheet's page); <c>&amp;A</c> is the sheet's name. Codes that set the text's look
/// (<c>&amp;"font"</c>, a size, <c>&amp;B</c>, <c>&amp;K</c> and the rest) are read past rather than honoured,
/// and the date, time, file and picture codes print nothing, so a render does not depend on when or
/// where it is made.
/// </summary>
static class SheetHeaderFooter
{
    public static PageSettings Apply(PageSettings settings, S.Worksheet worksheet, string sheetName, RunProperties font, double scale)
    {
        if (worksheet.GetFirstChild<S.HeaderFooter>() is not { } headerFooter)
        {
            return settings;
        }

        if (headerFooter.ScaleWithDoc?.Value != false)
        {
            font = font with
            {
                FontSizePoints = font.FontSizePoints * scale
            };
        }

        var width = settings.WidthPoints - settings.MarginLeft - settings.MarginRight;
        HeaderFooterContent? Band(string? text) =>
            string.IsNullOrEmpty(text) ? null : Content(text, sheetName, font, width);

        var differentFirst = headerFooter.DifferentFirst?.Value == true;
        var differentOddEven = headerFooter.DifferentOddEven?.Value == true;
        return settings with
        {
            DifferentFirstPage = differentFirst,
            Bands = new()
            {
                Header = Band(headerFooter.OddHeader?.Text),
                Footer = Band(headerFooter.OddFooter?.Text),
                FirstPageHeader = differentFirst ? Band(headerFooter.FirstHeader?.Text) : null,
                FirstPageFooter = differentFirst ? Band(headerFooter.FirstFooter?.Text) : null,
                EvenPageHeader = differentOddEven ? Band(headerFooter.EvenHeader?.Text) : null,
                EvenPageFooter = differentOddEven ? Band(headerFooter.EvenFooter?.Text) : null,
                EvenAndOddHeaders = differentOddEven
            }
        };
    }

    static HeaderFooterContent? Content(string text, string sheetName, RunProperties properties, double width)
    {
        var parts = Parts(text, sheetName);
        if (parts.All(_ => _.Count == 0))
        {
            return null;
        }

        var (left, centre, right) = (parts[0], parts[1], parts[2]);

        // One part alone is aligned as a paragraph; more than one stand at tab stops on one line.
        var alone = (left.Count > 0 ? 1 : 0) + (centre.Count > 0 ? 1 : 0) + (right.Count > 0 ? 1 : 0) == 1;
        List<Run> runs = [];
        ParagraphProperties paragraph;
        if (alone)
        {
            runs.AddRange(Runs(left.Count > 0 ? left : centre.Count > 0 ? centre : right, properties));
            paragraph = new()
            {
                Alignment = centre.Count > 0 ? TextAlignment.Center : right.Count > 0 ? TextAlignment.Right : TextAlignment.Left
            };
        }
        else
        {
            runs.AddRange(Runs(left, properties));
            runs.Add(Tab(properties));
            runs.AddRange(Runs(centre, properties));
            runs.Add(Tab(properties));
            runs.AddRange(Runs(right, properties));
            paragraph = new()
            {
                TabStops =
                [
                    new()
                    {
                        PositionPoints = width / 2,
                        Alignment = TabAlignment.Center
                    },
                    new()
                    {
                        PositionPoints = width,
                        Alignment = TabAlignment.Right
                    }
                ]
            };
        }

        return new()
        {
            Elements =
            [
                new ParagraphElement
                {
                    Runs = runs,
                    Properties = paragraph
                }
            ]
        };
    }

    static Run Tab(RunProperties properties) =>
        new()
        {
            Text = "\t",
            IsTab = true,
            Properties = properties
        };

    static IEnumerable<Run> Runs(List<(string Text, PageFieldKind Field)> part, RunProperties properties) =>
        part.Select(_ => new Run
        {
            Text = _.Text,
            Properties = properties,
            PageField = _.Field
        });

    /// <summary>
    /// The left, centre and right parts, each as its text and page fields in order. Text before any
    /// section code is the centre part's, as Excel reads it.
    /// </summary>
    internal static List<List<(string Text, PageFieldKind Field)>> Parts(string text, string sheetName)
    {
        List<List<(string Text, PageFieldKind Field)>> parts = [[], [], []];
        var current = 1;
        var literal = new StringBuilder();

        void Flush()
        {
            if (literal.Length > 0)
            {
                parts[current].Add((literal.ToString(), PageFieldKind.None));
                literal.Clear();
            }
        }

        for (var index = 0; index < text.Length; index++)
        {
            var ch = text[index];
            if (ch != '&' ||
                index + 1 == text.Length)
            {
                literal.Append(ch);
                continue;
            }

            var code = text[++index];
            switch (char.ToUpperInvariant(code))
            {
                case '&':
                    literal.Append('&');
                    break;
                case 'L':
                    Flush();
                    current = 0;
                    break;
                case 'C':
                    Flush();
                    current = 1;
                    break;
                case 'R':
                    Flush();
                    current = 2;
                    break;
                case 'P':
                    Flush();
                    parts[current].Add(("1", PageFieldKind.Page));
                    break;
                case 'N':
                    Flush();
                    parts[current].Add(("1", PageFieldKind.NumberOfPages));
                    break;
                case 'A':
                    literal.Append(sheetName);
                    break;
                case '"':
                    // &"font,style": the closing quote ends it.
                    var close = text.IndexOf('"', index + 1);
                    index = close < 0 ? text.Length - 1 : close;
                    break;
                case 'K':
                    // &K and a colour: six hex digits, or a theme colour's "TTSNNN".
                    index = Math.Min(index + 6, text.Length - 1);
                    break;
                default:
                    // A size (&12) runs to its last digit; any other code is one letter.
                    while (char.IsAsciiDigit(code) &&
                           index + 1 < text.Length &&
                           char.IsAsciiDigit(text[index + 1]))
                    {
                        index++;
                    }

                    break;
            }
        }

        Flush();
        return parts;
    }
}
