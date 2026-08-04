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
        AppendSection(paragraphs, "Footnotes", footnotes.Select(_ => _.Text).ToList());
        AppendSection(paragraphs, "Endnotes", endnotes.Select(_ => _.Text).ToList());
        return paragraphs;
    }

    static void AppendSection(List<ParagraphElement> paragraphs, string heading, List<string> entries)
    {
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
            paragraphs.Add(new()
            {
                Runs =
                [
                    // Sequential display number, matching the citation marks (footnotes.xml
                    // ids start at 2; Word shows 1, 2, 3...).
                    new()
                    {
                        Text = $"{noteIndex + 1}. ",
                        Properties = new()
                        {
                            Bold = true,
                            FontSizePoints = 10
                        }
                    },
                    new()
                    {
                        Text = entries[noteIndex],
                        Properties = new()
                        {
                            FontSizePoints = 10
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
