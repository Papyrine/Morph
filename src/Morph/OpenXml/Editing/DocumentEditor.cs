using OoxmlParagraphProperties = DocumentFormat.OpenXml.Wordprocessing.ParagraphProperties;
using OoxmlRun = DocumentFormat.OpenXml.Wordprocessing.Run;
using OoxmlRunProperties = DocumentFormat.OpenXml.Wordprocessing.RunProperties;
using Cell = ParagraphContent.Cell;
using Unit = ParagraphContent.Unit;

/// <summary>
/// The edits made to a document's text: a paragraph rewritten, split or joined to its neighbour, text
/// formatted, aligned or deleted. Each takes a document's bytes and returns the edited document's; the
/// input is never touched, which is what makes undo a matter of keeping the old array.
///
/// A paragraph is rewritten from what it now reads as rather than from the keys that were pressed
/// (<see cref="Rewrite"/>): what differs is worked out against the markup (<see cref="TextDiff"/>), and
/// only that is changed, so everything the editor never showed — bookmarks, comment anchors, the
/// spelling of a run's properties — stays as it was. With <see cref="EditOptions.Track"/> the same
/// differences are written as tracked changes instead, the way Word records them.
///
/// Paragraphs are named by ordinal and places by <see cref="SourcePosition"/>, both as
/// <see cref="DocumentOutline"/> read them from the same bytes.
/// </summary>
static class DocumentEditor
{
    const string wordNamespace = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    const string placeholderStyle = "PlaceholderText";

    /// <summary>
    /// Makes the paragraph read as <paramref name="content"/> does — as several paragraphs, where it
    /// was split — and then joins it to a neighbour if asked.
    /// </summary>
    public static EditResult Rewrite(byte[] docx, int paragraph, IReadOnlyList<NewParagraph> content, EditOptions options, JoinDirection join = JoinDirection.None) =>
        Edit(
            docx,
            options,
            edit =>
            {
                var rewritten = edit.Rewrite(paragraph, content);
                if (join == JoinDirection.Next)
                {
                    return edit.Join(rewritten[^1]);
                }

                if (join == JoinDirection.Previous &&
                    PreviousBlock(rewritten[0]) is Paragraph previous)
                {
                    return edit.Join(previous);
                }

                return (rewritten[0], 0);
            });

    /// <summary>
    /// Joins a paragraph to the one after it: the break between them goes — or, tracked, is marked
    /// as deleted. The place returned is where the two met.
    /// </summary>
    public static EditResult Join(byte[] docx, int paragraph, EditOptions options) =>
        Edit(docx, options, edit => edit.Join(edit.Find(paragraph)));

    /// <summary>Sets character formatting on the text between two places.</summary>
    public static byte[] Format(byte[] docx, SourcePosition start, SourcePosition end, RunFormat format, EditOptions options) =>
        Edit(
            docx,
            options,
            edit =>
            {
                edit.Format(start, end, format);
                return null;
            }).Document;

    /// <summary>Aligns paragraphs.</summary>
    public static byte[] Align(byte[] docx, IReadOnlyList<int> paragraphs, TextAlignment alignment, EditOptions options) =>
        Edit(
            docx,
            options,
            edit =>
            {
                foreach (var paragraph in paragraphs.Distinct().Select(edit.Find).ToList())
                {
                    edit.Align(paragraph, alignment);
                }

                return null;
            }).Document;

    /// <summary>
    /// Deletes the text between two places. Across paragraphs, the paragraphs between go with it and
    /// the first is joined to the last; they have to follow one another in one container.
    /// </summary>
    public static EditResult Delete(byte[] docx, SourcePosition start, SourcePosition end, EditOptions options) =>
        Edit(docx, options, edit => edit.Delete(start, end));

    /// <summary>Turns the document's own change tracking on or off (<c>w:trackRevisions</c>).</summary>
    public static byte[] SetTracking(byte[] docx, bool on)
    {
        using var stream = new MemoryStream();
        stream.Write(docx);
        stream.Position = 0;
        using (var document = WordprocessingDocument.Open(stream, true))
        {
            var main = MainPart(document);
            var part = main.DocumentSettingsPart ?? main.AddNewPart<DocumentSettingsPart>();
            var settings = part.Settings;
            if (settings == null)
            {
                settings = new();
                part.Settings = settings;
            }

            settings.RemoveAllChildren<TrackRevisions>();
            if (on)
            {
                settings.AddChild(new TrackRevisions());
            }
        }

        return stream.ToArray();
    }

    static EditResult Edit(byte[] docx, EditOptions options, Func<Editing, (Paragraph Paragraph, int Offset)?> edit)
    {
        using var stream = new MemoryStream();
        stream.Write(docx);
        stream.Position = 0;
        var ordinal = -1;
        var offset = 0;
        using (var document = WordprocessingDocument.Open(stream, true))
        {
            var editing = new Editing(document, options);
            if (edit(editing) is { } place)
            {
                ordinal = MainPart(document).Document!.Descendants<Paragraph>().TakeWhile(_ => !ReferenceEquals(_, place.Paragraph)).Count();
                offset = place.Offset;
            }
        }

        return new(stream.ToArray(), ordinal, offset);
    }

    static MainDocumentPart MainPart(WordprocessingDocument document)
    {
        if (document.MainDocumentPart is {Document: not null} main)
        {
            return main;
        }

        throw new InvalidOperationException("Document has no main part");
    }

    /// <summary>The block after a paragraph, looking past the range marks that can sit between blocks.</summary>
    public static OpenXmlElement? NextBlock(OpenXmlElement element)
    {
        var next = element.NextSibling();
        while (next != null && IsRangeMark(next))
        {
            next = next.NextSibling();
        }

        return next;
    }

    /// <summary>The block before a paragraph, looking past the range marks that can sit between blocks.</summary>
    public static OpenXmlElement? PreviousBlock(OpenXmlElement element)
    {
        var previous = element.PreviousSibling();
        while (previous != null && IsRangeMark(previous))
        {
            previous = previous.PreviousSibling();
        }

        return previous;
    }

    static bool IsRangeMark(OpenXmlElement element) =>
        element is
            BookmarkStart or BookmarkEnd or
            CommentRangeStart or CommentRangeEnd or
            MoveFromRangeStart or MoveFromRangeEnd or MoveToRangeStart or MoveToRangeEnd or
            PermStart or PermEnd or
            ProofError;

    // A place between two nodes: before Before, or at the end of Parent.
    readonly record struct Point(OpenXmlElement Parent, OpenXmlElement? Before)
    {
        public static Point Ahead(OpenXmlElement node) =>
            new(node.Parent!, node);

        public static Point Behind(OpenXmlElement node) =>
            new(node.Parent!, node.NextSibling());

        public static Point EndOf(OpenXmlElement parent) =>
            new(parent, null);

        public void Insert(OpenXmlElement node)
        {
            if (Before == null)
            {
                Parent.AppendChild(node);
            }
            else
            {
                Parent.InsertBefore(node, Before);
            }
        }
    }

    // A character of the text as it is to read, with the formatting it is to have; or the break
    // between two paragraphs.
    readonly record struct Fresh(char Character, int Unit, RunFormat Format, bool Mark);

    // A character of the text as it was, and the unit it was a character of.
    readonly record struct Old(Cell Cell, int Unit);

    // What stands around an insertion: the character before it, the first of those it replaces,
    // the character after it — and, where the stretch has no text at all, the things that are not
    // text either side. Any of them can be missing.
    readonly record struct Beside(Old? Left, Old? Gone, Old? Right, Unit? Behind, Unit? Ahead)
    {
        // Places an insertion can go, in the order they stand in.
        public const int AtLeft = 0;
        public const int AtGone = 1;
        public const int AtRight = 2;
        public const int Nowhere = 3;

        // Beside the neighbour the text is formatted as; like none of them, beside the first there is.
        public int Nearest(Fresh cell)
        {
            if (!cell.Mark)
            {
                if (Right is { } right && right.Unit == cell.Unit)
                {
                    return AtRight;
                }

                if (Gone is { } gone && gone.Unit == cell.Unit)
                {
                    return AtGone;
                }
            }

            if (Left != null)
            {
                return AtLeft;
            }

            if (Gone != null)
            {
                return AtGone;
            }

            if (Right != null)
            {
                return AtRight;
            }

            return Nowhere;
        }
    }

    enum StepKind
    {
        Keep,
        Delete,
        Insert
    }

    // One step from the old text to the new: a character kept (Old and New), one that went (Old), or
    // one that came (New, ahead of the old character Old).
    readonly record struct Step(StepKind Kind, int Old, int New);

    sealed class Editing(WordprocessingDocument document, EditOptions options)
    {
        readonly MainDocumentPart main = MainPart(document);

        // Where each character still to be dealt with is now, by the element it is in. Dividing a
        // run, typing into one and deleting from one all move the characters after the place, and
        // every one of those goes through here to say so.
        readonly Dictionary<OpenXmlElement, List<Cell>> places = new(ReferenceEqualityComparer.Instance);
        long nextRevision = -1;

        public Paragraph Find(int ordinal)
        {
            if (ordinal >= 0 &&
                main.Document!.Descendants<Paragraph>().ElementAtOrDefault(ordinal) is { } paragraph)
            {
                return paragraph;
            }

            throw new InvalidOperationException($"The document has no paragraph {ordinal}.");
        }

        List<Unit> Units(Paragraph paragraph)
        {
            foreach (var (candidate, units) in ParagraphContent.ReadAll(main.Document!))
            {
                if (ReferenceEquals(candidate, paragraph))
                {
                    return units;
                }
            }

            throw new InvalidOperationException("The paragraph is not part of the document.");
        }

        void Follow(IEnumerable<Unit> units)
        {
            foreach (var unit in units)
            {
                foreach (var cell in unit.Cells)
                {
                    Placed(cell.Child).Add(cell);
                }
            }
        }

        List<Cell> Placed(OpenXmlElement child)
        {
            if (!places.TryGetValue(child, out var cells))
            {
                places[child] = cells = [];
            }

            return cells;
        }

        // Rewriting

        public List<Paragraph> Rewrite(int ordinal, IReadOnlyList<NewParagraph> content)
        {
            if (content.Count == 0)
            {
                throw new InvalidOperationException("A paragraph cannot be rewritten as nothing.");
            }

            var paragraph = Find(ordinal);
            var units = Units(paragraph);
            Follow(units);

            // The text between the things that are not text is edited a stretch at a time: those
            // things stay where they are, and in their order, so stretch n of the new text is what
            // stretch n of the old has become.
            var before = new List<List<Old>> {new()};
            var bounds = new List<Unit>();
            for (var index = 0; index < units.Count; index++)
            {
                var unit = units[index];
                if (unit.Editable)
                {
                    before[^1].AddRange(unit.Cells.Select(_ => new Old(_, index)));
                }
                else
                {
                    bounds.Add(unit);
                    before.Add([]);
                }
            }

            const string fixedInPlace = "The paragraph's pictures, fields and notes have to stay as they are, and in their order.";
            var after = new List<List<Fresh>> {new()};
            for (var index = 0; index < content.Count; index++)
            {
                if (index > 0)
                {
                    after[^1].Add(new('\r', -1, default, true));
                }

                foreach (var item in content[index].Items)
                {
                    if (item.Text == null)
                    {
                        if (after.Count > bounds.Count ||
                            item.Unit < 0 ||
                            item.Unit >= units.Count ||
                            !ReferenceEquals(units[item.Unit], bounds[after.Count - 1]))
                        {
                            throw new InvalidOperationException(fixedInPlace);
                        }

                        after.Add([]);
                        continue;
                    }

                    if (item.Unit >= units.Count ||
                        (item.Unit >= 0 && !units[item.Unit].Editable))
                    {
                        throw new InvalidOperationException($"The paragraph has no text unit {item.Unit}.");
                    }

                    foreach (var character in Clean(item.Text))
                    {
                        after[^1].Add(new(character, Math.Max(item.Unit, -1), item.Format, false));
                    }
                }
            }

            if (after.Count != before.Count)
            {
                throw new InvalidOperationException(fixedInPlace);
            }

            // Last stretch first, and within a stretch last change first: a change moves only what
            // comes after it, and what comes after it has been dealt with.
            var results = new List<Paragraph> {paragraph};
            for (var index = before.Count - 1; index >= 0; index--)
            {
                var ahead = index < bounds.Count ? bounds[index] : null;
                var behind = index > 0 ? bounds[index - 1] : null;
                Stretch(paragraph, units, before[index], after[index], behind, ahead, results);
            }

            for (var index = 0; index < content.Count && index < results.Count; index++)
            {
                if (content[index].Alignment is { } alignment)
                {
                    Align(results[index], alignment);
                }
            }

            if (results.Count > 1)
            {
                FollowOn(results[^1]);
            }

            foreach (var result in results)
            {
                Tidy(result);
            }

            return results;
        }

        void Stretch(Paragraph paragraph, List<Unit> units, List<Old> old, List<Fresh> fresh, Unit? behind, Unit? ahead, List<Paragraph> results)
        {
            var steps = Steps(units, old, fresh);
            var index = steps.Count - 1;
            while (index >= 0)
            {
                var last = index;
                var kind = steps[index].Kind;
                while (index >= 0 && steps[index].Kind == kind)
                {
                    index--;
                }

                var first = index + 1;
                switch (kind)
                {
                    case StepKind.Insert:
                        var cells = new List<Fresh>();
                        for (var step = first; step <= last; step++)
                        {
                            cells.Add(fresh[steps[step].New]);
                        }

                        var beside = new Beside(Left(old, steps, first), Gone(old, steps, first), Right(old, steps, last), behind, ahead);
                        Insert(paragraph, units, beside, cells, results);
                        break;

                    case StepKind.Delete:
                        Remove(old.GetRange(steps[first].Old, last - first + 1).Select(_ => _.Cell).ToList());
                        break;

                    default:
                        Reformat(old, fresh, steps, first, last);
                        break;
                }
            }
        }

        // What an insertion stands after. Tracked, that is the character before it, deleted or not:
        // a deletion stays in the file, and what replaces it follows it. Untracked, what was deleted
        // is about to be gone, and the insertion stands after the last character that stays.
        Old? Left(List<Old> old, List<Step> steps, int first)
        {
            for (var step = first - 1; step >= 0; step--)
            {
                if (steps[step].Kind == StepKind.Keep ||
                    (options.Track && steps[step].Kind == StepKind.Delete))
                {
                    return old[steps[step].Old];
                }
            }

            return null;
        }

        // The first of the characters an insertion replaces, which are still there when it is made
        // and gone straight after: untracked, what takes their place goes where they were — in the
        // link they were in, in the content control. Tracked, they stay, and Left is the last of them.
        Old? Gone(List<Old> old, List<Step> steps, int first)
        {
            if (options.Track)
            {
                return null;
            }

            Old? gone = null;
            for (var step = first - 1; step >= 0 && steps[step].Kind == StepKind.Delete; step--)
            {
                gone = old[steps[step].Old];
            }

            return gone;
        }

        // What an insertion stands before: the next character that stays. One that went has been
        // dealt with already, and is no longer where it was read.
        static Old? Right(List<Old> old, List<Step> steps, int last)
        {
            for (var step = last + 1; step < steps.Count; step++)
            {
                if (steps[step].Kind == StepKind.Keep)
                {
                    return old[steps[step].Old];
                }
            }

            return null;
        }

        List<Step> Steps(List<Unit> units, List<Old> old, List<Fresh> fresh)
        {
            var was = Text(old.Select(_ => _.Cell));
            var now = string.Concat(fresh.Select(_ => _.Character));

            // A character is what it was only in the run it was in: the editor says which run each
            // stretch of text is like, and that is what tells a space typed from a space kept.
            var hunks = TextDiff.Compare(was, now, old.Select(_ => _.Unit).ToList(), fresh.Select(_ => _.Unit).ToList());
            var steps = new List<Step>();
            var at = 0;
            var to = 0;
            foreach (var hunk in hunks)
            {
                Kept(hunk.OldStart - at);
                Changed(hunk.OldLength, hunk.NewLength);
            }

            Kept(was.Length - at);
            return steps;

            void Kept(int count)
            {
                for (var index = 0; index < count; index++)
                {
                    steps.Add(new(StepKind.Keep, at++, to++));
                }
            }

            // What went, then what came. Tracked, a character that went from one run and came back
            // the same in another formatted just like it is left where it is: a paragraph often
            // ends in a space that is a run of its own, and a reviewer has no use for being told
            // that a space was deleted and a space typed. Untracked there is nothing to tell, and
            // the character goes to the run the editor said, which may be inside a link or a
            // comment's range where the other was not.
            void Changed(int gone, int come)
            {
                var first = 0;
                while (first < gone &&
                       first < come &&
                       Same(at + first, to + first))
                {
                    first++;
                }

                var last = 0;
                while (last < gone - first &&
                       last < come - first &&
                       Same(at + gone - 1 - last, to + come - 1 - last))
                {
                    last++;
                }

                Kept(first);
                for (var index = first; index < gone - last; index++)
                {
                    steps.Add(new(StepKind.Delete, at++, to));
                }

                for (var index = first; index < come - last; index++)
                {
                    steps.Add(new(StepKind.Insert, at, to++));
                }

                Kept(last);
            }

            bool Same(int before, int after) =>
                options.Track &&
                was[before] == now[after] &&
                fresh[after].Format.IsEmpty &&
                old[before].Unit != fresh[after].Unit &&
                Alike(units, old[before].Unit, fresh[after].Unit);
        }

        static bool Alike(List<Unit> units, int first, int second)
        {
            if (first < 0 ||
                second < 0)
            {
                return false;
            }

            return Formatting(units[first].Run) == Formatting(units[second].Run);
        }

        // A run's properties as written, less the record of what they used to be.
        static string Formatting(OoxmlRun run)
        {
            if (run.RunProperties is not { } properties)
            {
                return "";
            }

            return string.Concat(properties.ChildElements.Where(_ => _ is not RunPropertiesChange).Select(_ => _.OuterXml));
        }

        static string Text(IEnumerable<Cell> cells)
        {
            var builder = new StringBuilder();
            OpenXmlElement? child = null;
            var value = "";
            foreach (var cell in cells)
            {
                if (!ReferenceEquals(cell.Child, child))
                {
                    child = cell.Child;
                    value = ParagraphContent.Value(child);
                }

                builder.Append(value[cell.Index]);
            }

            return builder.ToString();
        }

        // Inserting

        void Insert(Paragraph paragraph, List<Unit> units, Beside beside, List<Fresh> cells, List<Paragraph> results)
        {
            // The text in runs' worths, a paragraph's break a piece of its own.
            var pieces = new List<List<Fresh>>();
            foreach (var cell in cells)
            {
                if (pieces.Count > 0 &&
                    !cell.Mark &&
                    pieces[^1][^1] is {Mark: false} last &&
                    last.Unit == cell.Unit &&
                    last.Format == cell.Format)
                {
                    pieces[^1].Add(cell);
                }
                else
                {
                    pieces.Add([cell]);
                }
            }

            // Text typed into a run is that run's text: no new run is made for it. The last of
            // what was typed can be the start of the run that follows, and the first of it the end
            // of the run before.
            var start = 0;
            var end = pieces.Count;
            Point? point = null;
            var reach = int.MaxValue;
            var intoLeft = beside.Left is { } before && Joins(pieces[0], before);
            if (beside.Right is { } after &&
                Joins(pieces[^1], after) &&
                !(intoLeft && end == 1))
            {
                var (child, index) = Into(after.Cell.Run, after.Cell.Child, after.Cell.Index, pieces[^1]);
                end--;
                if (end > 0)
                {
                    point = Ahead((OoxmlRun) child.Parent!, child, index);
                    reach = Beside.AtRight;
                }
            }

            if (intoLeft)
            {
                start = 1;
            }

            // The rest, last first, each ahead of the one placed before it. A piece goes beside
            // the neighbour it is formatted as — which is how it lands inside the right link — and
            // since the neighbours stand in order, that can only ever move the place leftwards.
            for (var index = end - 1; index >= start; index--)
            {
                var piece = pieces[index];
                var wanted = beside.Nearest(piece[0]);
                if (point == null ||
                    wanted < reach)
                {
                    point = Place(paragraph, beside, wanted);
                    reach = wanted;
                }

                if (piece[0].Mark)
                {
                    var first = Split(point.Value);
                    results.Insert(0, first);
                    point = Point.EndOf(first);
                    reach = int.MinValue;
                    continue;
                }

                point = Add(point.Value, Run(paragraph, units, piece));
            }

            if (intoLeft)
            {
                var cell = beside.Left!.Value.Cell;
                Into(cell.Run, cell.Child, cell.Index + 1, pieces[0]);
            }
        }

        // Whether text can simply join a run: it is formatted as the run is, and — tracked — the
        // run is an insertion of the same author's, which the text then is part of.
        bool Joins(List<Fresh> piece, Old neighbour)
        {
            if (piece[0].Mark ||
                piece[0].Unit != neighbour.Unit ||
                !piece[0].Format.IsEmpty)
            {
                return false;
            }

            if (!options.Track)
            {
                return true;
            }

            return Insertion(neighbour.Cell.Run) is { } insertion && IsOwn(insertion);
        }

        // Where inserted text goes when no run takes it in.
        Point Place(Paragraph paragraph, Beside beside, int where)
        {
            switch (where)
            {
                case Beside.AtLeft:
                    return After(beside.Left!.Value.Cell);
                case Beside.AtGone:
                    var gone = beside.Gone!.Value.Cell;
                    return Ahead(gone.Run, gone.Child, gone.Index);
                case Beside.AtRight:
                    var right = beside.Right!.Value.Cell;
                    return Ahead(right.Run, right.Child, right.Index);
            }

            // A stretch with no text in it: between two things that are not text, or a paragraph
            // with nothing in it at all.
            if (beside.Ahead != null)
            {
                return Before(beside.Ahead);
            }

            if (beside.Behind != null)
            {
                return After(beside.Behind);
            }

            return Point.EndOf(paragraph);
        }

        Point After(Cell cell) =>
            Ahead(cell.Run, cell.Child, cell.Index + 1);

        Point Before(Unit unit)
        {
            if (unit.First.Parent is OoxmlRun run)
            {
                return Ahead(run, unit.First, 0);
            }

            return Point.Ahead(unit.First);
        }

        Point After(Unit unit)
        {
            if (unit.Last.Parent is OoxmlRun run)
            {
                return Ahead(run, unit.Last, int.MaxValue);
            }

            return Point.Behind(unit.Last);
        }

        // Divides a run so that one begins at a character of one of its children, and gives the
        // place that run starts at.
        Point Ahead(OoxmlRun run, OpenXmlElement child, int index)
        {
            var length = ParagraphContent.Value(child).Length;
            var children = run.ChildElements.Where(_ => _ is not OoxmlRunProperties).ToList();
            var position = children.FindIndex(_ => ReferenceEquals(_, child));
            if (position < 0)
            {
                return Point.Behind(run);
            }

            if (index >= length)
            {
                position++;
                index = 0;
            }

            if (position == 0 &&
                index == 0)
            {
                return Point.Ahead(run);
            }

            if (position >= children.Count)
            {
                return Point.Behind(run);
            }

            var tail = (OoxmlRun) run.CloneNode(false);
            if (run.RunProperties is { } properties)
            {
                tail.AppendChild(properties.CloneNode(true));
            }

            for (var moved = position; moved < children.Count; moved++)
            {
                var node = children[moved];
                if (moved == position &&
                    index > 0)
                {
                    // Only text is divided: everything else is one character long.
                    tail.AppendChild(Halve((TextType) node, index));
                    continue;
                }

                node.Remove();
                tail.AppendChild(node);
            }

            foreach (var node in tail.ChildElements)
            {
                if (places.TryGetValue(node, out var cells))
                {
                    foreach (var cell in cells)
                    {
                        cell.Run = tail;
                    }
                }
            }

            run.InsertAfterSelf(tail);
            return Point.Ahead(tail);
        }

        // Divides a text in two, and gives the second half: an element yet to be put anywhere.
        TextType Halve(TextType text, int index)
        {
            var whole = Raw(text);
            var rest = (TextType) text.CloneNode(false);
            rest.Text = whole[index..];
            text.Text = whole[..index];
            if (places.TryGetValue(text, out var cells))
            {
                foreach (var cell in cells.Where(_ => _.Index >= index).ToList())
                {
                    cells.Remove(cell);
                    cell.Child = rest;
                    cell.Index -= index;
                    Placed(rest).Add(cell);
                }
            }

            return rest;
        }

        // Text written without xml:space="preserve" is read without its edge spaces. Writing it as
        // it is read makes a character's place in the text its place in the element.
        static string Raw(TextType text)
        {
            if (text.Space?.Value != SpaceProcessingModeValues.Preserve)
            {
                text.Text = SourceRuns.EffectiveText(text.Text, text.Space?.Value);
                text.Space = SpaceProcessingModeValues.Preserve;
            }

            return text.Text;
        }

        // Puts text into a run that is already there, ahead of a character of one of its children,
        // and says where what it put begins.
        (OpenXmlElement Child, int Index) Into(OoxmlRun run, OpenXmlElement child, int index, List<Fresh> cells)
        {
            OpenXmlElement? ahead;
            if (child is TextType text &&
                index > 0 &&
                index < Raw(text).Length)
            {
                var rest = Halve(text, index);
                text.InsertAfterSelf(rest);
                ahead = rest;
            }
            else if (index > 0)
            {
                ahead = child.NextSibling();
            }
            else
            {
                ahead = child;
            }

            (OpenXmlElement Child, int Index)? begins = null;
            foreach (var node in Content(string.Concat(cells.Select(_ => _.Character))))
            {
                if (ahead == null)
                {
                    run.AppendChild(node);
                }
                else
                {
                    run.InsertBefore(node, ahead);
                }

                var placed = Absorb(node);
                begins ??= placed;
            }

            Started(run);
            return begins ?? (child, index);
        }

        // Text beside text in one run is one text. Of two, the earlier element is the one that
        // stays. Gives where the text of the node now begins.
        (OpenXmlElement Child, int Index) Absorb(OpenXmlElement node)
        {
            if (node is not Text added)
            {
                return (node, 0);
            }

            var kept = added;
            var begins = 0;
            if (added.PreviousSibling() is Text previous)
            {
                begins = Raw(previous).Length;
                previous.Text += added.Text;
                added.Remove();
                kept = previous;
            }

            if (kept.NextSibling() is Text next)
            {
                Swallow(kept, next);
            }

            return (kept, begins);
        }

        void Swallow(Text kept, Text next)
        {
            var length = Raw(kept).Length;
            kept.Text += Raw(next);
            if (places.Remove(next, out var cells))
            {
                foreach (var cell in cells)
                {
                    cell.Child = kept;
                    cell.Index += length;
                    Placed(kept).Add(cell);
                }
            }

            next.Remove();
        }

        // A run of new text, formatted as the unit it is said to be like — or, in a paragraph with
        // no text to be like, as the paragraph's mark is.
        static OoxmlRun Run(Paragraph paragraph, List<Unit> units, List<Fresh> cells)
        {
            var run = new OoxmlRun();
            var like = cells[0].Unit >= 0 ? units[cells[0].Unit].Run : null;
            OpenXmlElement? source = like?.RunProperties;
            if (like == null)
            {
                source = paragraph.ParagraphProperties?.ParagraphMarkRunProperties;
            }

            if (source != null)
            {
                var properties = new OoxmlRunProperties();
                foreach (var child in source.ChildElements)
                {
                    if (child is RunPropertiesChange or ParagraphMarkRunPropertiesChange ||
                        RevisionElements.IsRevision(child))
                    {
                        continue;
                    }

                    properties.AppendChild(child.CloneNode(true));
                }

                if (properties.HasChildren)
                {
                    run.AppendChild(properties);
                }
            }

            if (like != null)
            {
                Started(like, run);
            }

            Set(run, cells[0].Format);
            foreach (var node in Content(string.Concat(cells.Select(_ => _.Character))))
            {
                run.AppendChild(node);
            }

            return run;
        }

        // Text as the children of a run: a tab, a line break and the two hyphens are elements.
        static List<OpenXmlElement> Content(string value)
        {
            var nodes = new List<OpenXmlElement>();
            var text = new StringBuilder();
            foreach (var character in value)
            {
                OpenXmlElement? node = character switch
                {
                    '\t' => new TabChar(),
                    '\n' => new Break(),
                    '­' => new SoftHyphen(),
                    '‑' => new NoBreakHyphen(),
                    _ => null
                };
                if (node == null)
                {
                    text.Append(character);
                    continue;
                }

                Flush();
                nodes.Add(node);
            }

            Flush();
            return nodes;

            void Flush()
            {
                if (text.Length == 0)
                {
                    return;
                }

                nodes.Add(
                    new Text(text.ToString())
                    {
                        Space = SpaceProcessingModeValues.Preserve
                    });
                text.Clear();
            }
        }

        // What XML cannot hold is dropped, and a line's end is one character however it was typed.
        // Only a carriage return is taken for a line's end: the separators Unicode has for the
        // purpose (U+2028 among them) are characters a document can hold, and one that does has to
        // come back as it was.
        static string Clean(string text)
        {
            var builder = new StringBuilder(text.Length);
            foreach (var character in text.Replace("\r\n", "\n").Replace('\r', '\n'))
            {
                if (character is '\t' or '\n' ||
                    (character >= ' ' && character is not ('￾' or '￿')))
                {
                    builder.Append(character);
                }
            }

            return builder.ToString();
        }

        // Adds a run at a place, as an insertion when changes are tracked, and gives the place
        // ahead of what was added.
        Point Add(Point point, OoxmlRun run)
        {
            if (!options.Track)
            {
                point.Insert(run);
                return Point.Ahead(run);
            }

            if (point.Parent is InsertedRun or MoveToRun)
            {
                if (IsOwn(point.Parent))
                {
                    point.Insert(run);
                    return Point.Ahead(run);
                }

                // In the middle of someone else's insertion, which is divided to make room.
                point = Divide(point);
            }

            var insertion = Stamp(new InsertedRun());
            insertion.AppendChild(run);
            point.Insert(insertion);
            return Point.Ahead(insertion);
        }

        // Divides what a place is in — a link, a content control, a revision — so that the place is
        // between its two halves.
        Point Divide(Point point)
        {
            var wrapper = point.Parent;
            if (point.Before == null)
            {
                return Point.Behind(wrapper);
            }

            var content = wrapper.ChildElements.Where(_ => !IsProperties(_)).ToList();
            if (content.Count == 0 ||
                ReferenceEquals(content[0], point.Before))
            {
                return Point.Ahead(wrapper);
            }

            var half = wrapper.CloneNode(false);
            foreach (var child in wrapper.ChildElements.ToList())
            {
                if (IsProperties(child))
                {
                    var copy = child.CloneNode(true);

                    // A content control's id is its own.
                    copy.RemoveAllChildren<SdtId>();
                    half.AppendChild(copy);
                }
            }

            var moving = false;
            foreach (var child in wrapper.ChildElements.ToList())
            {
                moving |= ReferenceEquals(child, point.Before);
                if (moving)
                {
                    child.Remove();
                    half.AppendChild(child);
                }
            }

            if (RevisionElements.IsRevision(half))
            {
                // The second half of a revision is a revision of its own, by the same hand.
                half.SetAttribute(new("w", "id", wordNamespace, NextRevision()));
            }

            wrapper.InsertAfterSelf(half);
            return Point.Ahead(half);
        }

        static bool IsProperties(OpenXmlElement element) =>
            element is SdtProperties or SdtEndCharProperties;

        // Ends a paragraph at a place: what came before the place becomes a paragraph of its own,
        // ahead of this one, formatted as this one is. This one keeps its mark and what hangs off it.
        Paragraph Split(Point point)
        {
            while (point.Parent is not Paragraph)
            {
                point = Divide(point);
            }

            var paragraph = (Paragraph) point.Parent;
            var first = new Paragraph();
            if (paragraph.ParagraphProperties is { } properties)
            {
                var copy = (OoxmlParagraphProperties) properties.CloneNode(true);
                copy.RemoveAllChildren<SectionProperties>();
                copy.RemoveAllChildren<ParagraphPropertiesChange>();
                if (copy.ParagraphMarkRunProperties is { } mark)
                {
                    Unrevise(mark);
                    if (!mark.HasChildren)
                    {
                        mark.Remove();
                    }
                }

                if (copy.HasChildren)
                {
                    first.AppendChild(copy);
                }
            }

            foreach (var child in paragraph.ChildElements.ToList())
            {
                if (ReferenceEquals(child, point.Before))
                {
                    break;
                }

                if (child is OoxmlParagraphProperties)
                {
                    continue;
                }

                child.Remove();
                first.AppendChild(child);
            }

            paragraph.InsertBeforeSelf(first);
            if (options.Track)
            {
                Mark(first).PrependChild(Stamp(new Inserted()));
            }

            return first;
        }

        static void Unrevise(OpenXmlElement mark)
        {
            foreach (var revision in mark.ChildElements.Where(_ => _ is Inserted or Deleted or MoveFrom or MoveTo).ToList())
            {
                revision.Remove();
            }
        }

        static ParagraphMarkRunProperties Mark(Paragraph paragraph)
        {
            var properties = paragraph.ParagraphProperties;
            if (properties == null)
            {
                properties = new();
                paragraph.PrependChild(properties);
            }

            var mark = properties.ParagraphMarkRunProperties;
            if (mark == null)
            {
                mark = new();
                properties.AddChild(mark);
            }

            return mark;
        }

        // Pressing Enter at the end of a heading starts body text, not another heading: a style
        // names the style of the paragraph that follows it (w:next). Tracked, the break is a change
        // to settle and the paragraph keeps its style, which is what rejecting the break gives back.
        //
        // Word-probed 2026-09-27: Enter at the end of a centred Heading 1 whose style names Normal
        // next gives an empty Normal paragraph, left-aligned — the heading's own alignment does not
        // follow it. Enter in the middle of the heading, or at its start, gives two centred headings.
        void FollowOn(Paragraph paragraph)
        {
            if (options.Track ||
                ParagraphContent.Runs(paragraph).Any() ||
                paragraph.ParagraphProperties is not { } properties ||
                properties.ParagraphStyleId?.Val?.Value is not { } styleId ||
                main.StyleDefinitionsPart?.Styles?.Elements<Style>().FirstOrDefault(_ => _.StyleId?.Value == styleId) is not { } style ||
                style.NextParagraphStyle?.Val?.Value is not { } next ||
                next == styleId)
            {
                return;
            }

            foreach (var child in properties.ChildElements.ToList())
            {
                if (child is not (SectionProperties or ParagraphMarkRunProperties))
                {
                    child.Remove();
                }
            }

            properties.ParagraphStyleId = new()
            {
                Val = next
            };
        }

        // Deleting

        void Remove(List<Cell> cells)
        {
            var end = cells.Count;
            for (var index = cells.Count - 1; index >= 0; index--)
            {
                if (index > 0 &&
                    ReferenceEquals(cells[index - 1].Run, cells[index].Run))
                {
                    continue;
                }

                Remove(cells[index].Run, cells.GetRange(index, end - index));
                end = index;
            }
        }

        // The characters are one run's, and follow one another.
        void Remove(OoxmlRun run, List<Cell> cells)
        {
            var insertion = Insertion(run);
            if (!options.Track ||
                (insertion != null && IsOwn(insertion)))
            {
                Erase(cells);
                return;
            }

            var deleted = Isolate(cells);
            foreach (var text in deleted.Elements<Text>().ToList())
            {
                var struck = new DeletedText(Raw(text))
                {
                    Space = SpaceProcessingModeValues.Preserve
                };
                deleted.ReplaceChild(struck, text);
                if (places.Remove(text, out var moved))
                {
                    foreach (var cell in moved)
                    {
                        cell.Child = struck;
                    }

                    places[struck] = moved;
                }
            }

            var deletion = Stamp(new DeletedRun());
            deleted.InsertBeforeSelf(deletion);
            deleted.Remove();
            deletion.AppendChild(deleted);
        }

        void Erase(List<Cell> cells)
        {
            var run = cells[0].Run;
            for (var index = cells.Count - 1; index >= 0; index--)
            {
                var cell = cells[index];
                var placed = Placed(cell.Child);
                placed.Remove(cell);
                if (cell.Child is not TextType text)
                {
                    // Once a tab or a break between two texts is gone, they are one text.
                    var previous = cell.Child.PreviousSibling();
                    var next = cell.Child.NextSibling();
                    cell.Child.Remove();
                    places.Remove(cell.Child);
                    if (previous is Text kept &&
                        next is Text following)
                    {
                        Swallow(kept, following);
                    }

                    continue;
                }

                text.Text = Raw(text).Remove(cell.Index, 1);
                foreach (var other in placed)
                {
                    if (other.Index > cell.Index)
                    {
                        other.Index--;
                    }
                }

                if (text.Text.Length == 0)
                {
                    text.Remove();
                    places.Remove(text);
                }
            }

            Vacate(run);
        }

        // A run with nothing left in it goes, and so does what wrapped only it.
        static void Vacate(OpenXmlElement element)
        {
            if (element is Paragraph ||
                element.ChildElements.Any(_ => _ is not (OoxmlRunProperties or SdtProperties or SdtEndCharProperties)))
            {
                return;
            }

            var parent = element.Parent;
            element.Remove();
            if (parent != null)
            {
                Vacate(parent);
            }
        }

        // The run that holds exactly these characters, divided off from the text either side.
        OoxmlRun Isolate(List<Cell> cells)
        {
            var last = cells[^1];
            Ahead(last.Run, last.Child, last.Index + 1);
            var first = cells[0];
            Ahead(first.Run, first.Child, first.Index);
            return first.Run;
        }

        // Formatting

        void Reformat(List<Old> old, List<Fresh> fresh, List<Step> steps, int first, int last)
        {
            var end = last + 1;
            for (var index = last; index >= first; index--)
            {
                if (index > first &&
                    ReferenceEquals(old[steps[index - 1].Old].Cell.Run, old[steps[index].Old].Cell.Run) &&
                    fresh[steps[index - 1].New].Format == fresh[steps[index].New].Format)
                {
                    continue;
                }

                var format = fresh[steps[index].New].Format;
                if (!format.IsEmpty)
                {
                    var cells = new List<Cell>();
                    for (var step = index; step < end; step++)
                    {
                        cells.Add(old[steps[step].Old].Cell);
                    }

                    Change(Isolate(cells), format);
                }

                end = index;
            }
        }

        public void Format(SourcePosition start, SourcePosition end, RunFormat format)
        {
            if (format.IsEmpty)
            {
                return;
            }

            foreach (var cells in Between(start, end).AsEnumerable().Reverse())
            {
                Change(Isolate(cells), format);
            }
        }

        // The editable characters between two places, a unit's at a time, in document order.
        List<List<Cell>> Between(SourcePosition start, SourcePosition end)
        {
            if (end.Run < start.Run ||
                (end.Run == start.Run && end.Offset < start.Offset))
            {
                (start, end) = (end, start);
            }

            var ordinals = SourceRuns.Index(main.Document!);
            var found = new List<(int Run, List<Cell> Cells)>();
            foreach (var (_, units) in ParagraphContent.ReadAll(main.Document!))
            {
                foreach (var unit in units)
                {
                    if (!unit.Editable)
                    {
                        continue;
                    }

                    var ordinal = ordinals[unit.Run];
                    if (ordinal < start.Run ||
                        ordinal > end.Run)
                    {
                        continue;
                    }

                    var cells = unit.Cells
                        .Where(_ => (ordinal > start.Run || _.Position >= start.Offset) &&
                                    (ordinal < end.Run || _.Position < end.Offset))
                        .ToList();
                    if (cells.Count > 0)
                    {
                        found.Add((ordinal, cells));
                        Follow([unit]);
                    }
                }
            }

            return found
                .OrderBy(_ => _.Run)
                .ThenBy(_ => _.Cells[0].Position)
                .Select(_ => _.Cells)
                .ToList();
        }

        // Tracked, the run keeps a record of the properties it had: once, however often it is changed.
        void Change(OoxmlRun run, RunFormat format)
        {
            if (options.Track &&
                !(Insertion(run) is { } insertion && IsOwn(insertion)) &&
                run.RunProperties?.GetFirstChild<RunPropertiesChange>() == null)
            {
                var previous = new PreviousRunProperties();
                foreach (var child in run.RunProperties?.ChildElements ?? [])
                {
                    previous.AppendChild(child.CloneNode(true));
                }

                var change = Stamp(new RunPropertiesChange());
                change.AppendChild(previous);
                Properties(run).AppendChild(change);
            }

            Set(run, format);
        }

        static OoxmlRunProperties Properties(OoxmlRun run)
        {
            var properties = run.RunProperties;
            if (properties == null)
            {
                properties = new();
                run.PrependChild(properties);
            }

            return properties;
        }

        // Set outright, on or off: left out, a property would be whatever the run's style makes it.
        static void Set(OoxmlRun run, RunFormat format)
        {
            if (format.IsEmpty)
            {
                return;
            }

            var properties = Properties(run);
            if (format.Bold is { } bold)
            {
                properties.Bold = Toggle<Bold>(bold);
                properties.BoldComplexScript = Toggle<BoldComplexScript>(bold);
            }

            if (format.Italic is { } italic)
            {
                properties.Italic = Toggle<Italic>(italic);
                properties.ItalicComplexScript = Toggle<ItalicComplexScript>(italic);
            }

            if (format.Strike is { } strike)
            {
                properties.DoubleStrike = null;
                properties.Strike = Toggle<Strike>(strike);
            }

            if (format.Underline is { } underline)
            {
                properties.Underline = new()
                {
                    Val = underline ? UnderlineValues.Single : UnderlineValues.None
                };
            }
        }

        // On is the element alone, as Word writes it; off has to say so.
        static T Toggle<T>(bool on)
            where T : OnOffType, new()
        {
            var toggle = new T();
            if (!on)
            {
                toggle.Val = false;
            }

            return toggle;
        }

        public void Align(Paragraph paragraph, TextAlignment alignment)
        {
            var properties = paragraph.ParagraphProperties;
            if (properties == null)
            {
                properties = new();
                paragraph.PrependChild(properties);
            }

            if (options.Track &&
                properties.GetFirstChild<ParagraphPropertiesChange>() == null)
            {
                var previous = new ParagraphPropertiesExtended();
                foreach (var child in properties.ChildElements)
                {
                    if (child is not (ParagraphMarkRunProperties or SectionProperties))
                    {
                        previous.AppendChild(child.CloneNode(true));
                    }
                }

                var change = Stamp(new ParagraphPropertiesChange());
                change.AppendChild(previous);
                properties.AppendChild(change);
            }

            properties.Justification = new()
            {
                Val = alignment switch
                {
                    TextAlignment.Center => JustificationValues.Center,
                    TextAlignment.Right => JustificationValues.Right,
                    TextAlignment.Justify => JustificationValues.Both,
                    _ => JustificationValues.Left
                }
            };
        }

        // Joining

        public (Paragraph Paragraph, int Offset) Join(Paragraph first)
        {
            var offset = Length(Units(first));
            if (NextBlock(first) is not Paragraph next)
            {
                return (first, offset);
            }

            var mark = first.ParagraphProperties?.ParagraphMarkRunProperties;
            var inserted = mark?.ChildElements.FirstOrDefault(_ => _ is Inserted or MoveTo);
            if (options.Track &&
                !(inserted != null && IsOwn(inserted)))
            {
                // The break stays in the file, struck out. Until the deletion is settled the two
                // are still two paragraphs.
                if (mark?.ChildElements.Any(_ => _ is Deleted or MoveFrom) != true)
                {
                    Mark(first).PrependChild(Stamp(new Deleted()));
                }

                return (first, offset);
            }

            if (Merge(first, next))
            {
                return (first, offset);
            }

            return (next, 0);
        }

        // The first paragraph takes in the second, and stays what it was: its style, its alignment,
        // its place in a list. An empty one has nothing to stay: it goes, and the second stands as
        // it is. Says whether the first is the one left standing.
        //
        // Word-probed 2026-09-27 (Word 16 over COM; Delete at the end of the first paragraph,
        // Backspace at the start of the second, and the mark selected and deleted all gave the
        // same): a centred "first " joined to a right-aligned "second" is centred, and a Heading 1
        // joined to body text is a Heading 1; an empty centred paragraph joined to a right-aligned
        // "second" is right-aligned. This is what someone editing sees, and it is NOT what settling
        // a tracked deletion of the same mark gives — there the second paragraph's formatting
        // stands (ReviewEditor.MergeWithNext, probed the same day).
        //
        // Whichever stands, the mark that went was the first's. What hung off it goes with it — a
        // revision of the mark, a section's end — and what hung off the second's is what is left.
        static bool Merge(Paragraph first, Paragraph next)
        {
            if (!ParagraphContent.Runs(first).Any())
            {
                var after = (OpenXmlElement?) next.ParagraphProperties;
                foreach (var child in first.ChildElements.Where(_ => _ is not OoxmlParagraphProperties).ToList())
                {
                    child.Remove();
                    if (after == null)
                    {
                        next.PrependChild(child);
                    }
                    else
                    {
                        next.InsertAfter(child, after);
                    }

                    after = child;
                }

                first.Remove();
                return false;
            }

            foreach (var child in next.ChildElements.ToList())
            {
                if (child is OoxmlParagraphProperties)
                {
                    continue;
                }

                child.Remove();
                first.AppendChild(child);
            }

            var properties = first.ParagraphProperties;
            properties?.RemoveAllChildren<SectionProperties>();
            if (properties?.ParagraphMarkRunProperties is { } mark)
            {
                Unrevise(mark);
                if (!mark.HasChildren)
                {
                    mark.Remove();
                }
            }

            var others = next.ParagraphProperties;
            var revisions = others?.ParagraphMarkRunProperties?.ChildElements.Where(_ => _ is Inserted or Deleted or MoveFrom or MoveTo).ToList() ?? [];
            var section = others?.GetFirstChild<SectionProperties>();
            if (revisions.Count > 0)
            {
                var standing = Mark(first);
                for (var index = revisions.Count - 1; index >= 0; index--)
                {
                    revisions[index].Remove();
                    standing.PrependChild(revisions[index]);
                }
            }

            if (section != null)
            {
                section.Remove();
                if (first.ParagraphProperties == null)
                {
                    first.PrependChild(new OoxmlParagraphProperties());
                }

                first.ParagraphProperties!.AddChild(section);
            }

            if (first.ParagraphProperties is {HasChildren: false} empty)
            {
                empty.Remove();
            }

            next.Remove();
            return true;
        }

        static int Length(List<Unit> units)
        {
            var length = 0;
            foreach (var unit in units)
            {
                length += unit.Editable ? unit.Cells.Count : 1;
            }

            return length;
        }

        public (Paragraph Paragraph, int Offset)? Delete(SourcePosition start, SourcePosition end)
        {
            var stretches = Between(start, end);
            if (stretches.Count == 0)
            {
                return null;
            }

            var first = stretches[0][0].Run.Ancestors<Paragraph>().First();
            var last = stretches[^1][0].Run.Ancestors<Paragraph>().First();
            var chain = new List<Paragraph> {first};
            while (!ReferenceEquals(chain[^1], last))
            {
                if (NextBlock(chain[^1]) is not Paragraph next ||
                    chain.Count > 10_000)
                {
                    throw new InvalidOperationException("Text can be deleted across paragraphs only where they follow one another: not across a table, or into one.");
                }

                chain.Add(next);
            }

            // Where the text left standing ahead of the deletion ends.
            var offset = 0;
            var target = stretches[0][0];
            foreach (var unit in Units(first))
            {
                if (unit.Editable &&
                    ReferenceEquals(unit.Run, target.Source) &&
                    unit.Cells.Any(_ => _.Position == target.Position))
                {
                    offset += unit.Cells.Count(_ => _.Position < target.Position);
                    break;
                }

                offset += unit.Editable ? unit.Cells.Count : 1;
            }

            foreach (var cells in stretches.AsEnumerable().Reverse())
            {
                Remove(cells[0].Run, cells);
            }

            var standing = chain[^1];
            for (var index = chain.Count - 2; index >= 0; index--)
            {
                // Tracked, the paragraphs stay and their breaks are struck out.
                var joined = Join(chain[index]).Paragraph;
                if (!options.Track)
                {
                    standing = joined;
                }
            }

            if (options.Track)
            {
                standing = first;
            }

            foreach (var paragraph in chain)
            {
                if (paragraph.Parent != null)
                {
                    Tidy(paragraph);
                }
            }

            return (standing, offset);
        }

        // Housekeeping

        // What Word marked as misspelt is no longer what is written there.
        static void Tidy(Paragraph paragraph)
        {
            foreach (var error in paragraph.Descendants<ProofError>().ToList())
            {
                if (ReferenceEquals(error.Ancestors<Paragraph>().FirstOrDefault(), paragraph))
                {
                    error.Remove();
                }
            }
        }

        // A content control showing its prompt — "Click here to enter text" — stops showing it once
        // something is typed there, and what is typed is not set in the prompt's grey.
        static void Started(OoxmlRun like, OoxmlRun? added = null)
        {
            var control = like.Ancestors<SdtElement>().FirstOrDefault(_ => _.SdtProperties?.GetFirstChild<ShowingPlaceholder>() != null);
            if (control == null)
            {
                return;
            }

            control.SdtProperties!.RemoveAllChildren<ShowingPlaceholder>();
            foreach (var run in control.Descendants<OoxmlRun>().Append(added).OfType<OoxmlRun>())
            {
                if (run.RunProperties is {RunStyle: {Val.Value: placeholderStyle} style} properties)
                {
                    style.Remove();
                    if (!properties.HasChildren)
                    {
                        properties.Remove();
                    }
                }
            }
        }

        // Tracked changes

        static OpenXmlElement? Insertion(OoxmlRun run)
        {
            for (var parent = run.Parent; parent != null && parent is not Paragraph; parent = parent.Parent)
            {
                if (parent is InsertedRun or MoveToRun)
                {
                    return parent;
                }
            }

            return null;
        }

        bool IsOwn(OpenXmlElement revision) =>
            string.Equals(RevisionElements.Author(revision), options.Author, StringComparison.Ordinal);

        T Stamp<T>(T revision)
            where T : OpenXmlElement
        {
            revision.SetAttribute(new("w", "id", wordNamespace, NextRevision()));
            revision.SetAttribute(new("w", "author", wordNamespace, options.Author));
            revision.SetAttribute(new("w", "date", wordNamespace, RevisionElements.Stamp(options.Date)));
            return revision;
        }

        // One more than the highest id any revision in the document has.
        string NextRevision()
        {
            if (nextRevision < 0)
            {
                foreach (var (_, root) in RevisionElements.Roots(document))
                {
                    foreach (var revision in RevisionElements.Enumerate(root))
                    {
                        if (long.TryParse(RevisionElements.Id(revision), NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
                        {
                            nextRevision = Math.Max(nextRevision, id);
                        }
                    }
                }

                nextRevision++;
            }

            return (nextRevision++).ToString(CultureInfo.InvariantCulture);
        }
    }
}
