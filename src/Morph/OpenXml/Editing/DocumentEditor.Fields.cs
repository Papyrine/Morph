using OoxmlParagraphProperties = DocumentFormat.OpenXml.Wordprocessing.ParagraphProperties;
using OoxmlRun = DocumentFormat.OpenXml.Wordprocessing.Run;
using OoxmlRunProperties = DocumentFormat.OpenXml.Wordprocessing.RunProperties;
using OoxmlFieldCode = DocumentFormat.OpenXml.Wordprocessing.FieldCode;
using OoxmlTabStop = DocumentFormat.OpenXml.Wordprocessing.TabStop;

// Fields put in at the caret — the page's number, the number of pages, the date — and a table of
// contents. Word-probed 2026-10-23 (Word 365 over COM, a blank document with two headings):
//
//   - Insert > Page Number > Current Position > Plain Number is a complex field
//     (begin, " PAGE   \* MERGEFORMAT ", separate, a run holding "1" in w:noProof, end), each part a
//     run of its own. Number of pages is the same field with NUMPAGES.
//   - Insert > Date and Time with Update automatically is " DATE \@ "d MMMM yyyy" " (the chosen
//     format), its result the date as that format writes it, in w:noProof. Unticked, it is plain text.
//   - References > Table of Contents > Automatic Table 1 is a block content control
//     (w:docPartObj, gallery "Table of Contents", w:docPartUnique) holding a "Contents" paragraph in
//     TOC Heading, then one paragraph per heading in TOC 1, 2, 3 by its outline level, each with a
//     right tab, dot leader, at the text's width less 10 twips (9016 on A4 with inch margins). The
//     field " TOC \o "1-3" \h \z \u " begins in the first entry's paragraph and ends in a paragraph
//     of its own after the last, whose run is bold. Each entry is a w:hyperlink to a bookmark,
//     history on: the heading's text in the Hyperlink style, a tab, and
//     " PAGEREF _Toc243668740 \h " with the page's number as its result — every run in w:noProof,
//     those after the text w:webHidden too. Each heading is wrapped in its bookmark,
//     "_Toc" and nine digits, the start before its first run and the end after its last.
//
// What page a heading is on is the layout's to say, which the editor knows and this does not: the
// table is written with every page as 1, and SetContentsPages fills them in afterwards.
static partial class DocumentEditor
{
    const string contentsGallery = "Table of Contents";
    const string contentsInstruction = " TOC \\o \"1-3\" \\h \\z \\u ";

    // Word's own text where there is no heading to list. Not probed: the wording is Word's, the
    // formatting the bold run that ends the field.
    const string noEntries = "No table of contents entries found.";

    /// <summary>
    /// Puts the number of the page — or, <paramref name="total"/>, the number of pages — at a place, as
    /// a field. The place returned is after it.
    /// </summary>
    public static EditResult InsertPageNumber(byte[] docx, int paragraph, int offset, bool total, EditOptions options) =>
        Edit(docx, options, edit => edit.Field(paragraph, offset, total ? " NUMPAGES   \\* MERGEFORMAT " : " PAGE   \\* MERGEFORMAT ", "1"));

    /// <summary>
    /// Puts a date at a place, written in a format of Word's Date and Time dialog (<c>d MMMM yyyy</c>):
    /// as a DATE field when it is to <paramref name="update"/>, otherwise as text. The place returned
    /// is after it.
    /// </summary>
    public static EditResult InsertDate(byte[] docx, int paragraph, int offset, string format, DateTime date, bool update, EditOptions options)
    {
        var text = date.ToString(format, CultureInfo.InvariantCulture);
        if (update)
        {
            return Edit(docx, options, edit => edit.Field(paragraph, offset, $" DATE \\@ \"{format}\" ", text));
        }

        return Edit(docx, options, edit => edit.Text(paragraph, offset, text));
    }

    /// <summary>
    /// Puts Word's Automatic Table 1 at a place: the paragraph is divided there and the table stands
    /// between the halves, listing the body's headings of levels 1 to 3. The place returned is the
    /// paragraph after it.
    /// </summary>
    public static EditResult InsertContents(byte[] docx, int paragraph, int offset, EditOptions options) =>
        Edit(docx, options, edit => edit.Contents(paragraph, offset));

    /// <summary>
    /// Lists the headings anew in every table of contents in the body, as Word's Update Table (Update
    /// entire table) does: what was typed into a table is lost with the rest. Its pages are 1 until
    /// <see cref="SetContentsPages"/> says otherwise.
    /// </summary>
    public static byte[] UpdateContents(byte[] docx, EditOptions options) =>
        Edit(
            docx,
            options,
            edit =>
            {
                edit.UpdateContents();
                return null;
            }).Document;

    /// <summary>
    /// Writes the page each table of contents entry's heading is on into the entry:
    /// <paramref name="pageOf"/> takes a paragraph's ordinal and gives its page's number, or null for
    /// one on no page. Untracked: Word does not track a field's result changing.
    /// </summary>
    public static byte[] SetContentsPages(byte[] docx, Func<int, int?> pageOf) =>
        Edit(
            docx,
            new("", DateTimeOffset.UnixEpoch, Track: false),
            edit =>
            {
                edit.ContentsPages(pageOf);
                return null;
            }).Document;

    /// <summary>Whether the body has a table of contents to update.</summary>
    public static bool HasContents(byte[] docx)
    {
        using var stream = new MemoryStream(docx, false);
        using var document = WordprocessingDocument.Open(stream, false);
        return document.MainDocumentPart?.Document?.Body is { } body &&
               body.Descendants<OoxmlFieldCode>().Any(IsContents);
    }

    /// <summary>Whether a paragraph holds a table of contents' field code: where one begins.</summary>
    public static bool HasContents(Paragraph paragraph) =>
        paragraph.Descendants<OoxmlFieldCode>().Any(IsContents);

    static bool IsContents(OoxmlFieldCode code) =>
        code.Text.TrimStart().StartsWith("TOC ", StringComparison.Ordinal);

    sealed partial class Editing
    {
        // A field at a place, its result what it shows until the layout works it out.
        public (Paragraph Paragraph, int Offset) Field(int ordinal, int offset, string instruction, string result)
        {
            var paragraph = Find(ordinal);
            Put(paragraph, offset, FieldRuns(instruction, result));
            return (paragraph, offset + 1);
        }

        // Text at a place, as a run of its own.
        public (Paragraph Paragraph, int Offset) Text(int ordinal, int offset, string text)
        {
            var paragraph = Find(ordinal);
            Put(paragraph, offset, [new(new Text(text) {Space = SpaceProcessingModeValues.Preserve})]);
            return (paragraph, offset + text.Length);
        }

        // Runs at a place, outside any link: tracked, as one insertion.
        void Put(Paragraph paragraph, int offset, IReadOnlyList<OoxmlRun> runs)
        {
            var point = At(paragraph, offset);
            while (point.Parent is Hyperlink)
            {
                point = Divide(point);
            }

            if (!options.Track)
            {
                foreach (var run in runs)
                {
                    point.Insert(run);
                }

                return;
            }

            if (point.Parent is InsertedRun or MoveToRun)
            {
                if (IsOwn(point.Parent))
                {
                    foreach (var run in runs)
                    {
                        point.Insert(run);
                    }

                    return;
                }

                point = Divide(point);
            }

            var insertion = Stamp(new InsertedRun());
            insertion.Append(runs);
            point.Insert(insertion);
        }

        static List<OoxmlRun> FieldRuns(string instruction, string result) =>
        [
            new(new FieldChar {FieldCharType = FieldCharValues.Begin}),
            new(new OoxmlFieldCode(instruction) {Space = SpaceProcessingModeValues.Preserve}),
            new(new FieldChar {FieldCharType = FieldCharValues.Separate}),
            new(new OoxmlRunProperties(new NoProof()), new Text(result) {Space = SpaceProcessingModeValues.Preserve}),
            new(new FieldChar {FieldCharType = FieldCharValues.End})
        ];

        // Tables of contents

        public (Paragraph Paragraph, int Offset) Contents(int ordinal, int offset)
        {
            var paragraph = Find(ordinal);
            if (!ReferenceEquals(PartOf(paragraph), main))
            {
                throw new InvalidOperationException("A table of contents goes in the body of the document.");
            }

            if (offset > 0)
            {
                Split(At(paragraph, offset));
            }

            Define("TOCHeading");
            var heading = new Paragraph(
                new OoxmlParagraphProperties(new ParagraphStyleId {Val = "TOCHeading"}),
                new OoxmlRun(new Text("Contents")));
            var ids = Stories().SelectMany(_ => _.Descendants<SdtId>()).Select(_ => _.Val?.Value ?? 0).DefaultIfEmpty(0).Max();
            var block = new SdtBlock(
                new SdtProperties(
                    new SdtId {Val = ids + 1},
                    new SdtContentDocPartObject(new DocPartGallery {Val = contentsGallery}, new DocPartUnique())),
                new SdtContentBlock(heading));
            paragraph.InsertBeforeSelf(block);

            var content = block.SdtContentBlock!;
            foreach (var entry in Entries(contentsInstruction, TabStop(paragraph)))
            {
                content.AppendChild(entry);
            }

            Track(content);
            return (paragraph, 0);
        }

        public void UpdateContents()
        {
            foreach (var code in main.Document!.Body!.Descendants<OoxmlFieldCode>().Where(IsContents).ToList())
            {
                if (code.Ancestors<Paragraph>().FirstOrDefault() is not { } first ||
                    FieldParagraphs(code) is not { Count: > 0 } old)
                {
                    continue;
                }

                var entries = Entries(code.Text, TabStop(first), old);
                foreach (var entry in entries)
                {
                    first.InsertBeforeSelf(entry);
                }

                foreach (var gone in old)
                {
                    gone.Remove();
                }

                foreach (var entry in entries)
                {
                    Track(entry);
                }
            }
        }

        public void ContentsPages(Func<int, int?> pageOf)
        {
            var all = AllParagraphs().ToList();
            var ordinals = new Dictionary<Paragraph, int>(ReferenceEqualityComparer.Instance);
            for (var index = 0; index < all.Count; index++)
            {
                ordinals[all[index]] = index;
            }

            var marks = main.Document!.Body!.Descendants<BookmarkStart>()
                .Where(_ => _.Name?.Value is { } name &&
                            name.StartsWith("_Toc", StringComparison.Ordinal))
                .GroupBy(_ => _.Name!.Value!, StringComparer.Ordinal)
                .ToDictionary(_ => _.Key, _ => _.First(), StringComparer.Ordinal);
            var contents = main.Document.Body.Descendants<OoxmlFieldCode>().Where(IsContents).SelectMany(FieldParagraphs).ToHashSet(ReferenceEqualityComparer.Instance);
            foreach (var code in main.Document.Body.Descendants<OoxmlFieldCode>().ToList())
            {
                if (PageReference(code.Text) is not { } name ||
                    !marks.TryGetValue(name, out var mark) ||
                    code.Ancestors<Paragraph>().FirstOrDefault() is not { } entry ||
                    !contents.Contains(entry) ||
                    Holder(mark) is not { } heading ||
                    !ordinals.TryGetValue(heading, out var ordinal) ||
                    pageOf(ordinal) is not { } page)
                {
                    continue;
                }

                SetResult(code, page.ToString(CultureInfo.InvariantCulture));
            }
        }

        // The paragraph a bookmark marks: the one it stands in, or the one after it when it stands
        // between blocks.
        static Paragraph? Holder(BookmarkStart mark) =>
            mark.Ancestors<Paragraph>().FirstOrDefault() ?? NextBlock(mark) as Paragraph;

        static string? PageReference(string instruction)
        {
            var parts = instruction.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts is ["PAGEREF", var name, ..])
            {
                return name;
            }

            return null;
        }

        // Makes a field's result read as text: its first run of text between separate and end takes
        // it, and any other text there goes.
        static void SetResult(OoxmlFieldCode code, string text)
        {
            var run = code.Parent;
            var separated = false;
            var written = false;
            for (var current = run?.NextSibling(); current != null; current = current.NextSibling())
            {
                if (current is not OoxmlRun each)
                {
                    continue;
                }

                if (each.GetFirstChild<FieldChar>() is { FieldCharType.Value: var type })
                {
                    if (type == FieldCharValues.Separate)
                    {
                        separated = true;
                        continue;
                    }

                    if (type == FieldCharValues.End)
                    {
                        if (!written &&
                            separated)
                        {
                            each.InsertBeforeSelf(new OoxmlRun(
                                new OoxmlRunProperties(new NoProof(), new WebHidden()),
                                new Text(text)));
                        }

                        return;
                    }

                    // A field inside this one's result is not this one's.
                    return;
                }

                if (!separated)
                {
                    continue;
                }

                foreach (var gone in each.Elements<Text>().ToList())
                {
                    if (written)
                    {
                        gone.Remove();
                    }
                    else
                    {
                        gone.Text = text;
                        written = true;
                    }
                }
            }
        }

        // The paragraphs a field spans, from the one it begins in to the one it ends in, all in one
        // container; empty when its end is not found there.
        static List<Paragraph> FieldParagraphs(OoxmlFieldCode code)
        {
            if (code.Ancestors<Paragraph>().FirstOrDefault() is not { } first)
            {
                return [];
            }

            var paragraphs = new List<Paragraph>();
            var depth = 0;
            var begun = false;
            for (OpenXmlElement? block = first; block != null; block = NextBlock(block))
            {
                if (block is not Paragraph paragraph)
                {
                    return [];
                }

                paragraphs.Add(paragraph);
                foreach (var mark in paragraph.Descendants<FieldChar>())
                {
                    if (!begun)
                    {
                        // Counting from this field's own begin, the one ahead of its code.
                        if (mark.Parent is { } holder &&
                            code.Parent is { } coded &&
                            holder.IsBefore(coded) &&
                            mark.FieldCharType?.Value == FieldCharValues.Begin &&
                            NextRunOf(holder) == coded)
                        {
                            begun = true;
                            depth = 1;
                        }

                        continue;
                    }

                    if (mark.FieldCharType?.Value == FieldCharValues.Begin)
                    {
                        depth++;
                    }
                    else if (mark.FieldCharType?.Value == FieldCharValues.End &&
                             --depth == 0)
                    {
                        return paragraphs;
                    }
                }
            }

            return [];
        }

        static OpenXmlElement? NextRunOf(OpenXmlElement run)
        {
            var next = run.NextSibling();
            while (next != null &&
                   next is not OoxmlRun)
            {
                next = next.NextSibling();
            }

            return next;
        }

        // Where an entry's page number stands: the text's right edge, less Word's 10 twips.
        int TabStop(Paragraph paragraph) =>
            TextWidth(paragraph) - 10;

        // A table's paragraphs, from the one its field begins in to the one it ends in. The headings
        // listed are the body's at levels the instruction's \o names (1 to 3 unless it says), by their
        // style's outline level or their own; those in the paragraphs a table replaces are not.
        List<Paragraph> Entries(string instruction, int tab, IReadOnlyCollection<Paragraph>? replacing = null)
        {
            var (from, to) = Levels(instruction);
            var linked = instruction.Contains("\\h", StringComparison.Ordinal);
            var skip = new HashSet<Paragraph>(replacing ?? [], ReferenceEqualityComparer.Instance);
            foreach (var code in main.Document!.Body!.Descendants<OoxmlFieldCode>().Where(IsContents))
            {
                skip.UnionWith(FieldParagraphs(code));
            }

            var headings = new List<(Paragraph Heading, int Level, string Text)>();
            foreach (var paragraph in main.Document.Body.Descendants<Paragraph>())
            {
                if (skip.Contains(paragraph) ||
                    OutlineLevel(paragraph) is not { } level ||
                    level + 1 < from ||
                    level + 1 > to ||
                    HeadingText(paragraph) is not { Length: > 0 } text)
                {
                    continue;
                }

                headings.Add((paragraph, level + 1, text));
            }

            var entries = new List<Paragraph>();
            if (headings.Count == 0)
            {
                var none = new Paragraph();
                none.Append(FieldStart(instruction));
                none.AppendChild(new OoxmlRun(new OoxmlRunProperties(new Bold(), new BoldComplexScript(), new NoProof()), new Text(noEntries)));
                none.AppendChild(new OoxmlRun(new OoxmlRunProperties(new Bold(), new BoldComplexScript(), new NoProof()), new FieldChar {FieldCharType = FieldCharValues.End}));
                entries.Add(none);
                return entries;
            }

            foreach (var (heading, level, text) in headings)
            {
                var name = Bookmarked(heading);
                var style = $"TOC{level}";
                Define(style);
                if (linked)
                {
                    Define("Hyperlink");
                }

                var entry = new Paragraph(new OoxmlParagraphProperties(
                    new ParagraphStyleId {Val = style},
                    new Tabs(new OoxmlTabStop {Val = TabStopValues.Right, Leader = TabStopLeaderCharValues.Dot, Position = tab}),
                    new ParagraphMarkRunProperties(new NoProof())));
                if (entries.Count == 0)
                {
                    entry.Append(FieldStart(instruction));
                }

                List<OpenXmlElement> runs =
                [
                    new OoxmlRun(linked ? new OoxmlRunProperties(new RunStyle {Val = "Hyperlink"}, new NoProof()) : new OoxmlRunProperties(new NoProof()), new Text(text) {Space = SpaceProcessingModeValues.Preserve}),
                    Hidden(new TabChar()),
                    Hidden(new FieldChar {FieldCharType = FieldCharValues.Begin}),
                    Hidden(new OoxmlFieldCode($" PAGEREF {name} \\h ") {Space = SpaceProcessingModeValues.Preserve}),
                    Hidden(),
                    Hidden(new FieldChar {FieldCharType = FieldCharValues.Separate}),
                    Hidden(new Text("1")),
                    Hidden(new FieldChar {FieldCharType = FieldCharValues.End})
                ];
                if (linked)
                {
                    entry.AppendChild(new Hyperlink(runs) {Anchor = name, History = Literal<OnOffValue>("1")});
                }
                else
                {
                    entry.Append(runs);
                }

                entries.Add(entry);
            }

            entries.Add(new Paragraph(new OoxmlRun(new OoxmlRunProperties(new Bold(), new BoldComplexScript(), new NoProof()), new FieldChar {FieldCharType = FieldCharValues.End})));
            return entries;

            static OoxmlRun Hidden(params OpenXmlElement[] content) =>
                new([new OoxmlRunProperties(new NoProof(), new WebHidden()), .. content]);
        }

        static IEnumerable<OoxmlRun> FieldStart(string instruction) =>
        [
            new(new FieldChar {FieldCharType = FieldCharValues.Begin}),
            new(new OoxmlFieldCode(instruction)
            {
                Space = SpaceProcessingModeValues.Preserve
            }),
            new(new FieldChar {FieldCharType = FieldCharValues.Separate})
        ];

        // The levels \o "1-3" names; Word's 1 to 9 without it, 1 to 3 being what Automatic Table writes.
        static (int From, int To) Levels(string instruction)
        {
            var match = OutlineLevels.Match(instruction);
            if (match.Success)
            {
                return (int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture), int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture));
            }

            return (1, 9);
        }

        // A paragraph's outline level, 0 for a level-1 heading: its own, or its style's, or what
        // that is based on. Null for body text (level 9 is body text too).
        int? OutlineLevel(Paragraph paragraph)
        {
            var level = paragraph.ParagraphProperties?.OutlineLevel?.Val?.Value;
            if (level == null)
            {
                foreach (var style in Chain(paragraph.ParagraphProperties?.ParagraphStyleId?.Val?.Value))
                {
                    if (style.StyleParagraphProperties?.OutlineLevel?.Val?.Value is { } styled)
                    {
                        level = styled;
                        break;
                    }
                }
            }

            if (level is null or >= 9)
            {
                return null;
            }

            return level;
        }

        // A heading's text as it reads: what is deleted and what is a field's code left out.
        static string HeadingText(Paragraph paragraph)
        {
            var builder = new StringBuilder();
            foreach (var run in paragraph.Descendants<OoxmlRun>())
            {
                if (run.Ancestors<DeletedRun>().Any())
                {
                    continue;
                }

                foreach (var child in run.ChildElements)
                {
                    if (child is Text text)
                    {
                        builder.Append(text.Text);
                    }
                    else if (child is TabChar)
                    {
                        builder.Append(' ');
                    }
                }
            }

            return builder.ToString().Trim();
        }

        // The _Toc bookmark round a heading: the one it has, or a new one round its runs.
        string Bookmarked(Paragraph heading)
        {
            if (heading.Elements<BookmarkStart>().FirstOrDefault(_ => _.Name?.Value?.StartsWith("_Toc", StringComparison.Ordinal) == true) is {Name.Value: { } existing})
            {
                return existing;
            }

            var numbers = Stories()
                .SelectMany(_ => _.Descendants<BookmarkStart>())
                .Select(_ => _.Name?.Value)
                .Where(_ => _ != null &&
                            _.StartsWith("_Toc", StringComparison.Ordinal))
                .Select(_ => long.TryParse(_![4..], NumberStyles.None, CultureInfo.InvariantCulture, out var number) ? number : 0)
                .DefaultIfEmpty(200000000)
                .Max();
            var name = $"_Toc{Math.Max(numbers, 200000000) + 1}";
            var id = Stories()
                .SelectMany(_ => _.Descendants<BookmarkStart>())
                .Select(_ => int.TryParse(_.Id?.Value, out var value) ? value : -1)
                .DefaultIfEmpty(-1)
                .Max() + 1;
            var key = id.ToString(CultureInfo.InvariantCulture);
            var content = heading.ChildElements.Where(_ => _ is not OoxmlParagraphProperties).ToList();
            var start = new BookmarkStart {Id = key, Name = name};
            var end = new BookmarkEnd {Id = key};
            if (content.Count == 0)
            {
                heading.Append(start, end);
            }
            else
            {
                content[0].InsertBeforeSelf(start);
                content[^1].InsertAfterSelf(end);
            }

            return name;
        }

        // Tracked, what was just written is an insertion: its runs, and its paragraphs' marks.
        void Track(OpenXmlElement written)
        {
            if (!options.Track)
            {
                return;
            }

            var paragraphs = written as Paragraph is { } single ? [single] : written.Descendants<Paragraph>().ToList();
            foreach (var paragraph in paragraphs)
            {
                foreach (var run in paragraph.Descendants<OoxmlRun>().ToList())
                {
                    if (run.Ancestors<InsertedRun>().Any())
                    {
                        continue;
                    }

                    var insertion = Stamp(new InsertedRun());
                    run.InsertBeforeSelf(insertion);
                    run.Remove();
                    insertion.AppendChild(run);
                }

                Mark(paragraph).PrependChild(Stamp(new Inserted()));
            }
        }
    }

    [GeneratedRegex("""
                    \\o\s+"(\d)-(\d)"
                    """)]
    private static partial Regex OutlineLevels { get; }
}
