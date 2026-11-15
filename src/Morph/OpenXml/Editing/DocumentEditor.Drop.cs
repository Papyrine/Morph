using Wp = DocumentFormat.OpenXml.Drawing.Wordprocessing;
using OoxmlRun = DocumentFormat.OpenXml.Wordprocessing.Run;
using OoxmlRunProperties = DocumentFormat.OpenXml.Wordprocessing.RunProperties;
using Cell = ParagraphContent.Cell;
using Unit = ParagraphContent.Unit;

// Drag and drop: text selected — with what is not text in it, a picture, a field, a note's reference —
// dragged to another place and dropped, moved there or, with Ctrl held, copied. What Word does was
// probed with real mouse drags (WordProbe M-80 to M-88c, C-22, M-62; src/word-behavior.md):
//
//   - What is dropped keeps its own formatting, and is left selected where it went (M-80).
//   - Dropped inside a word, text lands at the word's start ("beta" in "ga|mma" goes before "gamma"),
//     or past the word and its spaces once the drop is two letters or more nearer the word's end than
//     its start ("Thir|d and" before "and") (M-80 to M-88c; Snapped). A picture dropped there is not
//     moved off the place (M-62).
//   - Dropped on itself or at either of its ends, nothing happens and the caret goes to the drop (M-83).
//   - Smart cut and paste, on by default: a whole word moved away takes a space with it, as Cut does
//     (smartCut in morph-edit.js), and where it lands it is spaced from its neighbours — a space before
//     it after a word or a sentence's punctuation, and one after it before a word when it ends in one
//     (M-80, M-81, M-85). The space after it is selected with it; the one before it is not.
//   - A selection that takes in a paragraph's mark moves its text alone, joining the paragraph it is
//     dropped in; the paragraph it leaves goes if the selection took all of it (M-81). Across several
//     marks the paragraphs between go, as a deletion takes them (DocumentEditor.Delete), and each
//     mark but the last dropped divides the paragraph dropped into.
//   - Tracked, what moved is struck where it was and inserted where it went (M-80, M-84, M-62). Word
//     records a whole paragraph's text moved as a move (w:moveFrom / w:moveTo) instead, which Morph's
//     review pane cannot yet settle: it is written as a deletion and an insertion here, which
//     accepting and rejecting settle the same way.
//
// What could not be probed is refused rather than guessed at: a drop into another story (a header,
// a note) and a selection that is not one run of paragraphs of one cell or of the body.
static partial class DocumentEditor
{
    const char objectMark = '￼';

    /// <summary>
    /// Moves, or with <paramref name="copy"/> copies, what is between two places — from
    /// <paramref name="fromOffset"/> in paragraph <paramref name="fromParagraph"/> to
    /// <paramref name="toOffset"/> in <paramref name="toParagraph"/>, counted as
    /// <see cref="EditParagraph.Length"/> counts — to <paramref name="atOffset"/> in
    /// <paramref name="atParagraph"/>, as a drag and drop does.
    /// </summary>
    public static DropResult Drop(byte[] docx, int fromParagraph, int fromOffset, int toParagraph, int toOffset, int atParagraph, int atOffset, bool copy, EditOptions options)
    {
        var paragraphs = DocumentOutline.Read(docx).Paragraphs;
        if (Before(toParagraph, toOffset, fromParagraph, fromOffset))
        {
            (fromParagraph, fromOffset, toParagraph, toOffset) = (toParagraph, toOffset, fromParagraph, fromOffset);
        }

        DropResult Unchanged(int paragraph, int offset) =>
            new(docx, paragraph, offset, paragraph, offset);

        if (fromParagraph < 0 ||
            toParagraph >= paragraphs.Count ||
            atParagraph < 0 ||
            atParagraph >= paragraphs.Count)
        {
            return Unchanged(Math.Clamp(atParagraph, 0, Math.Max(paragraphs.Count - 1, 0)), Math.Max(atOffset, 0));
        }

        fromOffset = Math.Clamp(fromOffset, 0, paragraphs[fromParagraph].Length);
        toOffset = Math.Clamp(toOffset, 0, paragraphs[toParagraph].Length);
        atOffset = Math.Clamp(atOffset, 0, paragraphs[atParagraph].Length);

        // A selection that ends at a paragraph's start took the mark before it, but nothing of that
        // paragraph: the text that moves ends with the paragraph before.
        var lastText = toParagraph;
        var lastOffset = toOffset;
        if (toParagraph > fromParagraph &&
            toOffset == 0)
        {
            lastText = toParagraph - 1;
            lastOffset = paragraphs[lastText].Length;
        }

        var dragged = Dragged(paragraphs, fromParagraph, fromOffset, lastText, lastOffset);
        if (!Followed(paragraphs, fromParagraph, toParagraph) ||
            paragraphs[fromParagraph].Root != paragraphs[atParagraph].Root ||
            dragged.Replace("\n", "").Length == 0)
        {
            return Unchanged(atParagraph, atOffset);
        }

        var at = Reading(paragraphs[atParagraph]);
        if (IsWord(dragged[0]))
        {
            atOffset = Snapped(at, atOffset);
        }

        if (!Before(atParagraph, atOffset, fromParagraph, fromOffset) &&
            !Before(toParagraph, toOffset, atParagraph, atOffset))
        {
            return Unchanged(atParagraph, atOffset);
        }

        // Smart cut: a whole word moved away takes a space with it.
        var cutFrom = fromOffset;
        var cutTo = lastOffset;
        if (!copy &&
            fromParagraph == lastText)
        {
            (cutFrom, cutTo) = SmartCut(Reading(paragraphs[fromParagraph]), fromOffset, lastOffset);
        }

        // Smart paste: the neighbours it lands between, as they stand once it has gone from beside them.
        var cut = !copy && atParagraph == fromParagraph && fromParagraph == lastText;
        char? previous = null;
        for (var index = atOffset - 1; index >= 0; index--)
        {
            if (!(cut && index >= cutFrom && index < cutTo))
            {
                previous = at[index];
                break;
            }
        }

        char? next = null;
        for (var index = atOffset; index < at.Length; index++)
        {
            if (!(cut && index >= cutFrom && index < cutTo))
            {
                next = at[index];
                break;
            }
        }

        var text = dragged.TrimEnd('\n');
        var leading = IsWord(text[0]) &&
                      previous is { } before &&
                      (IsWord(before) || ".,;:!?".Contains(before));
        var trailing = IsWord(text[^1]) &&
                       next is { } after &&
                       IsWord(after);

        // Tracked, a whole paragraph's text moved out of every table is recorded as a move, as Word records
        // it (M-81, M-85); anything less, or a cell's, as a deletion and an insertion (M-80, M-84).
        var move = !copy &&
                   options.Track &&
                   fromOffset == 0 &&
                   lastOffset == paragraphs[lastText].Length &&
                   paragraphs[fromParagraph].Cell == null;

        var placed = (FirstParagraph: atParagraph, Start: atOffset, LastParagraph: atParagraph, End: atOffset);
        var edited = DocumentReading.Edit(
            docx,
            document =>
            {
                var editing = new Editing(document, options);
                var (first, start, last, end) = editing.Drop(
                    new(fromParagraph, fromOffset, lastText, lastOffset, cutFrom, cutTo, toParagraph, toOffset, atParagraph, atOffset),
                    copy,
                    move,
                    leading,
                    trailing);
                var all = SourceRuns.Stories(document).SelectMany(_ => _.Descendants<Paragraph>()).ToList();
                placed = (all.FindIndex(_ => ReferenceEquals(_, first)), start, all.FindIndex(_ => ReferenceEquals(_, last)), end);
            });

        return new(edited, placed.FirstParagraph, placed.Start, placed.LastParagraph, placed.End);
    }

    static bool Before(int paragraph, int offset, int otherParagraph, int otherOffset) =>
        paragraph < otherParagraph ||
        (paragraph == otherParagraph &&
         offset < otherOffset);

    // A paragraph's text place by place: a character of text for each of its own, and an object
    // mark for each thing that is not text.
    static string Reading(EditParagraph paragraph)
    {
        var builder = new StringBuilder();
        foreach (var unit in paragraph.Units)
        {
            if (unit.Editable)
            {
                builder.Append(unit.Text);
                continue;
            }

            builder.Append(objectMark);
        }

        return builder.ToString();
    }

    // What a selection reads, a line to each paragraph.
    static string Dragged(IReadOnlyList<EditParagraph> paragraphs, int from, int fromOffset, int to, int toOffset)
    {
        var builder = new StringBuilder();
        for (var ordinal = from; ordinal <= to; ordinal++)
        {
            var reading = Reading(paragraphs[ordinal]);
            var start = 0;
            if (ordinal == from)
            {
                start = fromOffset;
            }

            var end = reading.Length;
            if (ordinal == to)
            {
                end = toOffset;
            }

            if (ordinal > from)
            {
                builder.Append('\n');
            }

            if (end > start)
            {
                builder.Append(reading, start, end - start);
            }
        }

        return builder.ToString();
    }

    // Whether paragraphs follow one another in one story, in one cell or out of every table: what a
    // deletion can take out (Editing.Delete).
    static bool Followed(IReadOnlyList<EditParagraph> paragraphs, int from, int to)
    {
        var at = from;
        while (at != to)
        {
            if (paragraphs[at].NextInStory is not { } next ||
                next > to ||
                paragraphs[next].Cell != paragraphs[from].Cell)
            {
                return false;
            }

            at = next;
        }

        return true;
    }

    static bool IsWord(char character) =>
        char.IsLetterOrDigit(character) ||
        character is '_' or '\'' or '’';

    // A drop inside a word goes to the word's start, or past the word and the spaces after it once the
    // drop is two letters or more nearer its end than its start: what is dropped does not matter, and a
    // text box's words go as the body's. Eighteen drops across six words settle it (M-80 to M-88c):
    // "paragraph" took letters one to five in to its start and six to eight past it; "Thir|d" and
    // "lin|e" went past, "Bo|x" (two in, one to go) and "li|ne" (two and two) to the start — so it is
    // neither the nearer end nor a fraction of the word, and the drop's x was where the harness pressed.
    static int Snapped(string text, int offset)
    {
        if (offset <= 0 ||
            offset >= text.Length ||
            !IsWord(text[offset - 1]) ||
            !IsWord(text[offset]))
        {
            return offset;
        }

        var start = offset;
        while (start > 0 &&
               IsWord(text[start - 1]))
        {
            start--;
        }

        var end = offset;
        while (end < text.Length &&
               IsWord(text[end]))
        {
            end++;
        }

        if (offset - start < end - offset + 2)
        {
            return start;
        }

        while (end < text.Length &&
               text[end] == ' ')
        {
            end++;
        }

        return end;
    }

    // Word's smart cut, as the box's own (smartCut in morph-edit.js): whole words taken away take a
    // space with them rather than leave two, or one before punctuation — the space after them where one
    // space and then more text follow, else the space before them where a space or punctuation
    // follows; nothing for part of a word, or for a selection that already ends in a space.
    static (int From, int To) SmartCut(string text, int from, int to)
    {
        if (to <= from ||
            !IsWord(text[from]) ||
            char.IsWhiteSpace(text[to - 1]) ||
            (from > 0 && IsWord(text[from - 1])) ||
            (to < text.Length && IsWord(text[to])))
        {
            return (from, to);
        }

        var after = to < text.Length ? text[to] : (char?) null;
        if (after == ' ' &&
            to + 1 < text.Length &&
            !char.IsWhiteSpace(text[to + 1]) &&
            !",.;:!?".Contains(text[to + 1]))
        {
            return (from, to + 1);
        }

        if (from > 0 &&
            text[from - 1] == ' ' &&
            (after == ' ' || (after is { } mark && ",.;:!?".Contains(mark))))
        {
            return (from - 1, to);
        }

        return (from, to);
    }

    /// <summary>
    /// Copies the floating picture at a place, as Ctrl+drag does (WordProbe I-15, Word's Duplicate): the
    /// copy is anchored where the picture is, ahead of it, and stands moved by the drag, over every other
    /// picture; the picture itself stays. Tracked, the copy is an insertion, as Word's is.
    /// </summary>
    public static byte[] DuplicatePicture(byte[] docx, int paragraph, int offset, double rightPoints, double downPoints, EditOptions options) =>
        Edit(
            docx,
            options,
            edit =>
            {
                edit.Duplicate(paragraph, offset);
                edit.Nudge(paragraph, offset, rightPoints, downPoints);
                return null;
            }).Document;

    // Where a drop's text comes from and goes, in the paragraphs of the file as it was: the text from
    // From to Last (the selection less a mark it ends with); To, where the selection ends, past that
    // mark; and the drop.
    readonly record struct DropPlace(int From, int FromOffset, int Last, int LastOffset, int CutFrom, int CutTo, int To, int ToOffset, int At, int AtOffset);

    sealed partial class Editing
    {
        // What a drop lifts from one paragraph: characters of text, or one thing that is not text.
        sealed record Piece(int Paragraph, List<Cell>? Cells, Unit? Atom)
        {
            public List<OpenXmlElement> Elements { get; } = [];
        }

        public (Paragraph? First, int Start, Paragraph? Last, int End) Drop(DropPlace place, bool copy, bool move, bool leading, bool trailing)
        {
            var moving = !copy;
            var all = AllParagraphs().ToList();
            if (place.To >= all.Count ||
                place.At >= all.Count)
            {
                throw new InvalidOperationException("The document has no such paragraph.");
            }

            // Text boxes' paragraphs are numbered among those of the paragraph that anchors them, and
            // are none of the selection's.
            var story = all[place.From].Parent;
            var ordinals = Enumerable.Range(place.From, place.To - place.From + 1)
                .Where(_ => ReferenceEquals(all[_].Parent, story))
                .ToList();
            var chain = ordinals.Select(_ => all[_]).ToList();
            var destination = all[place.At];

            // What moves, found before anything is divided, a paragraph at a time.
            var pieces = new List<Piece>();
            var spare = new List<Cell>();
            for (var index = 0; index < chain.Count; index++)
            {
                var ordinal = ordinals[index];
                if (ordinal > place.Last)
                {
                    break;
                }

                var start = 0;
                if (ordinal == place.From)
                {
                    start = place.FromOffset;
                }

                var end = int.MaxValue;
                if (ordinal == place.Last)
                {
                    end = place.LastOffset;
                }

                var units = Units(chain[index]);
                Follow(units);
                var at = 0;
                foreach (var unit in units)
                {
                    if (unit.Editable)
                    {
                        var cells = new List<Cell>();
                        for (var cell = 0; cell < unit.Cells.Count; cell++)
                        {
                            if (at + cell >= start &&
                                at + cell < end)
                            {
                                cells.Add(unit.Cells[cell]);
                            }
                        }

                        if (cells.Count > 0)
                        {
                            pieces.Add(new(index, cells, null));
                        }

                        // The space smart cut takes away with the text, which does not go with it.
                        if (moving &&
                            ordinal == place.From)
                        {
                            for (var cell = 0; cell < unit.Cells.Count; cell++)
                            {
                                var position = at + cell;
                                if (position >= place.CutFrom &&
                                    position < place.CutTo &&
                                    (position < start || position >= end))
                                {
                                    spare.Add(unit.Cells[cell]);
                                }
                            }
                        }

                        at += unit.Cells.Count;
                        continue;
                    }

                    // What is not text and can be taken goes along; text a tracked change deleted stays
                    // to be settled, and so does a note's reference wherever two would refer to one note.
                    if (at >= start &&
                        at < end &&
                        Removable(unit) &&
                        !(unit.Kind == EditUnitKind.Note &&
                          (copy || options.Track)))
                    {
                        pieces.Add(new(index, null, unit));
                    }

                    at++;
                }
            }

            // Each piece divided off into runs of its own.
            foreach (var piece in pieces)
            {
                if (piece.Cells is { } cells)
                {
                    piece.Elements.Add(Isolate(cells));
                    continue;
                }

                var atom = piece.Atom!;
                if (atom.First is SimpleField simple)
                {
                    piece.Elements.Add(simple);
                    continue;
                }

                piece.Elements.AddRange(RunsOf(atom));
            }

            // A move is written round runs that stand in no revision of their own.
            move &= pieces.All(
                _ => _.Elements.All(
                    element => element is OoxmlRun &&
                               !element.Ancestors().Any(ancestor => ancestor is InsertedRun or MoveToRun or DeletedRun or MoveFromRun)));

            // Where it goes, held by a mark that stays put whatever is taken from around it.
            var point = At(destination, place.AtOffset);
            while (point.Parent is Hyperlink or InsertedRun or MoveToRun or DeletedRun or MoveFromRun or SdtRun)
            {
                point = Divide(point);
            }

            var holder = new BookmarkStart
            {
                Name = "_MorphDrop",
                Id = "-1"
            };
            point.Insert(holder);

            // What goes in: the elements themselves when they move and nothing is tracked, else copies.
            // A link's text keeps its link.
            var relocating = moving && !options.Track;
            var groups = new List<List<OpenXmlElement>>();
            var vacated = new List<OpenXmlElement>();
            var copies = new List<OpenXmlElement>();
            var group = -1;
            OpenXmlElement? link = null;
            Hyperlink? linkCopy = null;
            foreach (var piece in pieces)
            {
                while (group < piece.Paragraph)
                {
                    groups.Add([]);
                    group++;
                    link = null;
                }

                foreach (var element in piece.Elements)
                {
                    var parent = element.Parent;
                    OpenXmlElement content;
                    if (relocating)
                    {
                        element.Remove();
                        if (parent != null)
                        {
                            vacated.Add(parent);
                        }

                        content = element;
                    }
                    else
                    {
                        content = element.CloneNode(true);
                        copies.Add(content);
                    }

                    if (parent is Hyperlink hyperlink)
                    {
                        if (!ReferenceEquals(link, hyperlink) ||
                            linkCopy == null)
                        {
                            link = hyperlink;
                            linkCopy = (Hyperlink) hyperlink.CloneNode(false);
                            groups[group].Add(linkCopy);
                        }

                        linkCopy.AppendChild(content);
                        continue;
                    }

                    link = null;
                    groups[group].Add(content);
                }
            }

            while (groups.Count < ordinals.Count(_ => _ <= place.Last))
            {
                groups.Add([]);
            }

            var lifted = groups.SelectMany(_ => _).ToList();
            if (lifted.Count == 0)
            {
                holder.Remove();
                return (destination, place.AtOffset, destination, place.AtOffset);
            }

            // Smart paste's spaces, in the look of the text beside them.
            OpenXmlElement? before = null;
            if (leading)
            {
                before = Space(lifted[0]);
            }

            OpenXmlElement? after = null;
            if (trailing)
            {
                after = Space(lifted[^1]);
            }

            var inserted = new List<OpenXmlElement>();

            void Put(OpenXmlElement node, bool moved)
            {
                foreach (var made in Tracked(node, moved))
                {
                    holder.InsertBeforeSelf(made);
                    inserted.Add(made);
                }
            }

            // Smart paste's spaces are insertions of their own, even in a move (M-81, M-85).
            if (before != null)
            {
                Put(before, false);
            }

            var content0 = inserted.Count;
            for (var index = 0; index < groups.Count; index++)
            {
                if (index > 0)
                {
                    Split(Point.Ahead(holder));
                }

                foreach (var node in groups[index])
                {
                    Put(node, move);
                }
            }

            var contentEnd = inserted.Count - 1;
            if (after != null)
            {
                Put(after, false);
            }

            // A move's two halves, tied by the name on the range round each.
            var moveName = "";
            if (move)
            {
                moveName = $"move{NextRevision()}";
                var start = Stamp(new MoveToRangeStart());
                start.SetAttribute(new("w", "name", wordNamespace, moveName));
                inserted[content0].InsertBeforeSelf(start);
                inserted[contentEnd].InsertAfterSelf(
                    new MoveToRangeEnd
                    {
                        Id = RevisionElements.Id(start)
                    });
            }

            // A picture's copy is a drawing of its own.
            foreach (var properties in copies.SelectMany(_ => _.Descendants<Wp.DocProperties>()))
            {
                properties.Id = NextDrawing();
            }

            // Taken from where it was: gone, or struck when tracked.
            if (moving)
            {
                if (relocating)
                {
                    foreach (var parent in vacated)
                    {
                        if (parent.Parent != null)
                        {
                            Vacate(parent);
                        }
                    }
                }
                else if (move)
                {
                    // The text stays where it was, as the place it moved from.
                    var runs = pieces.SelectMany(_ => _.Elements).ToList();
                    var wrappers = new List<OpenXmlElement>();
                    foreach (var run in runs)
                    {
                        var wrapper = Stamp(new MoveFromRun());
                        run.InsertBeforeSelf(wrapper);
                        run.Remove();
                        wrapper.AppendChild(run);
                        wrappers.Add(wrapper);
                    }

                    var start = Stamp(new MoveFromRangeStart());
                    start.SetAttribute(new("w", "name", wordNamespace, moveName));
                    wrappers[0].InsertBeforeSelf(start);
                    wrappers[^1].InsertAfterSelf(
                        new MoveFromRangeEnd
                        {
                            Id = RevisionElements.Id(start)
                        });
                }
                else
                {
                    foreach (var piece in pieces.AsEnumerable().Reverse())
                    {
                        if (piece.Cells is { } cells)
                        {
                            Remove(cells[0].Run, cells);
                            continue;
                        }

                        Drop(piece.Atom!);
                    }
                }

                // Each space by itself: one may stand either side of the text.
                foreach (var cell in spare.AsEnumerable().Reverse())
                {
                    Remove(cell.Run, [cell]);
                }

                Unmark(chain, place);
            }

            holder.Remove();
            foreach (var paragraph in chain.Append(destination))
            {
                if (paragraph.Parent != null)
                {
                    Tidy(paragraph);
                }
            }

            var first = inserted[content0];
            var last = inserted[^1];
            var firstParagraph = first.Ancestors<Paragraph>().First();
            var lastParagraph = last.Ancestors<Paragraph>().First();
            var startOffset = Extent(firstParagraph, first).Start;
            var endOffset = Extent(lastParagraph, last).End;
            return (firstParagraph, startOffset, lastParagraph, endOffset);
        }

        // The marks a moved selection took in: the paragraphs wholly inside it go, as a deletion takes
        // them, and so does the first when all of it went; tracked, the marks are struck instead.
        void Unmark(List<Paragraph> chain, DropPlace place)
        {
            var marks = chain.Count - 1;
            if (marks <= 0)
            {
                return;
            }

            if (options.Track)
            {
                for (var index = marks - 1; index >= 0; index--)
                {
                    Join(chain[index]);
                }

                return;
            }

            var last = chain[marks];
            var whole = place.FromOffset == 0 &&
                        place.ToOffset == 0;
            for (var index = marks - 1; index >= 0; index--)
            {
                if (index == 0 &&
                    !whole)
                {
                    break;
                }

                // What is left that is not text — a bookmark's end, a comment's anchor — stays, at the
                // start of the paragraph after.
                var paragraph = chain[index];
                var keeping = index == 0 ? last : chain[0];
                var leftover = paragraph.ChildElements.Where(_ => _ is not DocumentFormat.OpenXml.Wordprocessing.ParagraphProperties).ToList();
                foreach (var child in leftover.AsEnumerable().Reverse())
                {
                    child.Remove();
                    if (index == 0)
                    {
                        var properties = keeping.GetFirstChild<DocumentFormat.OpenXml.Wordprocessing.ParagraphProperties>();
                        if (properties != null)
                        {
                            properties.InsertAfterSelf(child);
                            continue;
                        }

                        keeping.PrependChild(child);
                        continue;
                    }

                    keeping.AppendChild(child);
                }

                paragraph.Remove();
            }
        }

        // A run holding one space, formatted as the text beside it.
        static OoxmlRun Space(OpenXmlElement beside)
        {
            var run = new OoxmlRun();
            var like = beside as OoxmlRun ?? beside.Descendants<OoxmlRun>().FirstOrDefault();
            if (like?.RunProperties is { } properties)
            {
                var copy = (OoxmlRunProperties) properties.CloneNode(true);
                copy.RemoveAllChildren<RunPropertiesChange>();
                if (copy.HasChildren)
                {
                    run.AppendChild(copy);
                }
            }

            run.AppendChild(
                new Text(" ")
                {
                    Space = SpaceProcessingModeValues.Preserve
                });
            return run;
        }

        // A tracked insertion's wrapper, or a move's where it went to.
        OpenXmlElement Wrapper(bool moved)
        {
            if (moved)
            {
                return Stamp(new MoveToRun());
            }

            return Stamp(new InsertedRun());
        }

        // What a dropped node goes in as: itself, or tracked, an insertion (or the place a move went to) —
        // a link's runs each wrapped inside it, and a simple field as the complex field it is, since only
        // runs can be.
        IEnumerable<OpenXmlElement> Tracked(OpenXmlElement node, bool moved = false)
        {
            if (!options.Track)
            {
                return [node];
            }

            if (node is SimpleField simple)
            {
                var insertion = Wrapper(moved);
                foreach (var run in Complex(simple))
                {
                    insertion.AppendChild(run);
                }

                return [insertion];
            }

            if (node is OoxmlRun)
            {
                var insertion = Wrapper(moved);
                insertion.AppendChild(node);
                return [insertion];
            }

            foreach (var run in node.Elements<OoxmlRun>().ToList())
            {
                var insertion = Wrapper(moved);
                run.InsertBeforeSelf(insertion);
                run.Remove();
                insertion.AppendChild(run);
            }

            return [node];
        }

        // The places of a paragraph that a node holds: from the first to just past the last.
        (int Start, int End) Extent(Paragraph paragraph, OpenXmlElement node)
        {
            bool Within(OpenXmlElement element) =>
                ReferenceEquals(element, node) ||
                element.Ancestors().Any(_ => ReferenceEquals(_, node));

            int? start = null;
            var end = 0;
            var at = 0;
            foreach (var unit in Units(paragraph))
            {
                if (unit.Editable)
                {
                    foreach (var cell in unit.Cells)
                    {
                        if (Within(cell.Child))
                        {
                            start ??= at;
                            end = at + 1;
                        }

                        at++;
                    }

                    continue;
                }

                if (Within(unit.First))
                {
                    start ??= at;
                    end = at + 1;
                }

                at++;
            }

            return (start ?? 0, Math.Max(end, start ?? 0));
        }

        // A copy of the floating picture at a place, anchored just ahead of it, drawn over every other.
        public void Duplicate(int ordinal, int offset)
        {
            var at = 0;
            foreach (var unit in Units(Find(ordinal)))
            {
                if (at == offset &&
                    unit.Kind == EditUnitKind.Drawing &&
                    unit.First.Parent is OoxmlRun &&
                    unit.Last.Parent is OoxmlRun)
                {
                    var runs = RunsOf(unit);
                    var height = Stories().SelectMany(_ => _.Descendants<Wp.Anchor>()).Select(_ => _.RelativeHeight?.Value ?? 0).DefaultIfEmpty(251658239u).Max();
                    foreach (var run in runs)
                    {
                        var copy = (OoxmlRun) run.CloneNode(true);
                        foreach (var made in Tracked(copy, false))
                        {
                            runs[0].InsertBeforeSelf(made);
                        }

                        foreach (var properties in copy.Descendants<Wp.DocProperties>())
                        {
                            properties.Id = NextDrawing();
                            properties.Name = $"Picture {properties.Id.Value}";
                        }

                        foreach (var anchor in copy.Descendants<Wp.Anchor>())
                        {
                            anchor.RelativeHeight = height + 1;
                        }
                    }

                    return;
                }

                at += Span(unit);
                if (at > offset)
                {
                    break;
                }
            }

            throw new InvalidOperationException("There is no picture there.");
        }
    }
}
