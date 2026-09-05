/// <summary>
/// Builds the footnote/endnote appendix as synthesized flow paragraphs. Word draws footnotes at the
/// bottom of the page where the reference appears; that needs page-level reservation in the layout
/// pass (not currently wired). Until then the notes are listed at document end so the content isn't
/// lost. One builder serves every pagination path — the layout engine appends these to the element
/// flow, and the production page renderers render them after the body — so the appendix is identical
/// by construction everywhere.
/// </summary>
static class NotesAppendix
{
    /// <summary>
    /// The document's elements followed by its notes appendix; the original list when the document
    /// has no user-authored notes.
    /// </summary>
    public static IReadOnlyList<DocumentElement> AppendTo(ParsedDocument document)
    {
        var appendix = BuildElements(document);
        return appendix.Count == 0 ? document.Elements : [.. document.Elements, .. appendix];
    }

    /// <summary>
    /// The appendix paragraphs — a "Footnotes" heading and one numbered paragraph per note, then the
    /// same for endnotes. Empty when the document has no user-authored notes.
    /// </summary>
    public static IReadOnlyList<ParagraphElement> BuildElements(ParsedDocument document)
    {
        // Footnote/endnote ids 0 and -1 are Word's "separator" / "continuation separator" stubs —
        // skip them so the appendix only contains user-authored notes.
        var footnotes = document.Footnotes
            .Where(_ => _.Id != "0" && _.Id != "-1" && !string.IsNullOrWhiteSpace(_.Text))
            .ToList();
        var endnotes = document.Endnotes
            .Where(_ => _.Id != "0" && _.Id != "-1" && !string.IsNullOrWhiteSpace(_.Text))
            .ToList();

        if (footnotes.Count == 0 && endnotes.Count == 0)
        {
            return [];
        }

        var paragraphs = new List<ParagraphElement>();
        AppendSection(paragraphs, "Footnotes", footnotes.Select(_ => _.Text).ToList(), ListNumberFormat.Decimal, document.FootnoteTextSizePoints);
        AppendSection(paragraphs, "Endnotes", endnotes.Select(_ => _.Text).ToList(), document.EndnoteNumberFormat, document.EndnoteTextSizePoints);
        return paragraphs;
    }

    /// <summary>
    /// The display ordinal in the section's counter style — endnotes default to lowercase roman
    /// (matching the reference marks the parser emits), footnotes to decimal.
    /// </summary>
    static string FormatOrdinal(int number, ListNumberFormat format)
    {
        switch (format)
        {
            case ListNumberFormat.UpperRoman:
                return ToRoman(number);
            case ListNumberFormat.LowerRoman:
                return ToRoman(number).ToLowerInvariant();
            case ListNumberFormat.UpperLetter:
                return ToLetter(number);
            case ListNumberFormat.LowerLetter:
                return ToLetter(number).ToLowerInvariant();
            default:
                return number.ToString();
        }
    }

    static string ToRoman(int number)
    {
        if (number is <= 0 or > 3999)
        {
            return number.ToString();
        }

        var values = (int[]) [1000, 900, 500, 400, 100, 90, 50, 40, 10, 9, 5, 4, 1];
        var symbols = (string[]) ["M", "CM", "D", "CD", "C", "XC", "L", "XL", "X", "IX", "V", "IV", "I"];
        var builder = new StringBuilder();
        for (var i = 0; i < values.Length; i++)
        {
            while (number >= values[i])
            {
                builder.Append(symbols[i]);
                number -= values[i];
            }
        }

        return builder.ToString();
    }

    static string ToLetter(int number)
    {
        if (number <= 0)
        {
            return number.ToString();
        }

        // 1 => A, 26 => Z, 27 => AA (Word repeats the letter: 27 is AA, 53 is AAA at 26-cycle).
        var cycle = (number - 1) / 26;
        var letter = (char) ('A' + (number - 1) % 26);
        return new(letter, cycle + 1);
    }

    // Word sets a note body in the document's FootnoteText / EndnoteText style; its built-in styles are
    // 10pt, which is the fallback when the styles part defines neither.
    const double builtInNoteSizePoints = 10;

    static void AppendSection(List<ParagraphElement> paragraphs, string heading, List<string> entries, ListNumberFormat format, double? noteSizePoints)
    {
        var noteSize = noteSizePoints ?? builtInNoteSizePoints;
        if (entries.Count == 0)
        {
            return;
        }

        paragraphs.Add(new()
        {
            Runs =
            [
                new()
                {
                    Text = heading,
                    Properties = new()
                    {
                        Bold = true,
                        FontSizePoints = 12
                    }
                }
            ],
            Properties = new()
            {
                SpacingBeforePoints = 12,
                SpacingAfterPoints = 6
            }
        });

        for (var noteIndex = 0; noteIndex < entries.Count; noteIndex++)
        {
            paragraphs.Add(
                new()
                {
                    Runs =
                    [
                        // Sequential display number, matching the citation marks (footnotes.xml
                        // ids start at 2; Word shows 1, 2, 3... for footnotes and i, ii, iii...
                        // for default-format endnotes).
                        new()
                        {
                            Text = $"{FormatOrdinal(noteIndex + 1, format)}. ",
                            Properties = new()
                            {
                                Bold = true,
                                FontSizePoints = noteSize
                            }
                        },
                        new()
                        {
                            Text = entries[noteIndex],
                            Properties = new()
                            {
                                FontSizePoints = noteSize
                            }
                        }
                    ],
                    Properties = new()
                    {
                        SpacingAfterPoints = 4
                    }
                });
        }
    }
}