/// <summary>
/// Traces the text on laid-out pages back to the runs it was parsed from, so a selection on a page can
/// become a comment's range and a comment's range a highlight on a page.
///
/// The layout engine keeps no such link, and is not asked to: it measures and paints for every converter,
/// and only the viewer wants to know where a word came from. What it does keep is each line's paragraph,
/// and its rule for building a line's text from that paragraph's runs is simple enough to follow
/// backwards — the runs' text in order (upper-cased under <c>w:caps</c>, a no-break space drawn as a
/// space), less the spaces a wrap or a justified line swallowed and the breaks that ended a line. So the
/// placed text is the paragraph's text with whitespace missing, and walking the two together says which
/// run every placed character belongs to.
///
/// Anything else is a paragraph this cannot follow — a drop cap's restructured runs, say — and it is
/// left unmapped from the point the two part ways rather than guessed at: a comment anchored a word off
/// is worse than a place that cannot be commented on.
/// </summary>
sealed class SourceIndex
{
    // Float noise only: a marker stands a hanging indent left of its line, a line number further still.
    const float outdentTolerance = 0.05f;

    readonly Dictionary<PlacedLine, IReadOnlyList<RunPiece>?[]> lines = new(ReferenceEqualityComparer.Instance);

    SourceIndex()
    {
    }

    /// <summary>How many placed lines of stamped paragraphs were followed to the end.</summary>
    public int Followed { get; private set; }

    /// <summary>
    /// How many were not: the lines of a paragraph from the one whose text stopped matching its runs.
    /// </summary>
    public int Lost { get; private set; }

    /// <summary>The index of a laid-out document, or null when its runs carry no sources to index.</summary>
    public static SourceIndex? Build(LaidOutDocument document)
    {
        var index = new SourceIndex();
        var cursors = new Dictionary<ParagraphElement, Cursor?>(ReferenceEqualityComparer.Instance);
        foreach (var page in document.Pages)
        {
            index.Walk(page.Items, cursors);
        }

        if (index.lines.Count == 0)
        {
            return null;
        }

        return index;
    }

    /// <summary>
    /// Where the text of a placed run came from, as pieces of it in order; null when nothing is known.
    /// A run usually is one piece. It is several when the engine drew neighbouring runs of one
    /// formatting as one, and has gaps where a character came from a run the parser synthesised.
    /// </summary>
    public IReadOnlyList<RunPiece>? Pieces(PlacedLine line, int runIndex)
    {
        if (lines.TryGetValue(line, out var runs) &&
            runIndex < runs.Length)
        {
            return runs[runIndex];
        }

        return null;
    }

    void Walk(IReadOnlyList<PlacedItem> items, Dictionary<ParagraphElement, Cursor?> cursors)
    {
        foreach (var item in items)
        {
            switch (item)
            {
                case PlacedLine line:
                    Align(line, cursors);
                    break;
                case PlacedTableRow row:
                    foreach (var cell in row.Cells)
                    {
                        Walk(cell.Floats, cursors);
                        Walk(cell.Content, cursors);
                    }

                    break;
                case PlacedRotatedGroup group:
                    Walk(group.Items, cursors);
                    break;
            }
        }
    }

    void Align(PlacedLine line, Dictionary<ParagraphElement, Cursor?> cursors)
    {
        var paragraph = line.Paragraph;
        if (!cursors.TryGetValue(paragraph, out var cursor))
        {
            // Most paragraphs of most documents: nothing stamped, nothing to follow.
            cursor = paragraph.Runs.Any(_ => _.Source != null) ? new Cursor(paragraph) : null;
            cursors[paragraph] = cursor;
        }

        if (cursor == null)
        {
            return;
        }

        // A paragraph is laid out again wherever it repeats — a header row on every page it heads —
        // and each time from its first line.
        if (line.LineIndex == 0)
        {
            cursor.Restart();
        }
        else if (line.LineIndex != cursor.NextLine)
        {
            cursor.Lost = true;
        }

        cursor.NextLine = line.LineIndex + 1;
        if (cursor.Lost)
        {
            Lost++;
            return;
        }

        var pieces = new IReadOnlyList<RunPiece>?[line.Runs.Count];
        var mapped = false;

        // A line can open with runs that are no part of its paragraph: a list's marker, and a line
        // number in the margin. Both are set out to the left of the line, where its own text never is.
        var first = 0;
        while (first < line.Runs.Count &&
               line.Runs[first].X < line.X - outdentTolerance)
        {
            first++;
        }

        for (var index = first; index < line.Runs.Count && !cursor.Lost; index++)
        {
            var run = line.Runs[index];
            if (run.Leader != TabLeader.None ||
                string.IsNullOrEmpty(run.Text))
            {
                continue;
            }

            pieces[index] = cursor.Follow(run.Text);
            mapped |= pieces[index] is {Count: > 0};
        }

        if (mapped)
        {
            lines[line] = pieces;
        }

        if (cursor.Lost)
        {
            Lost++;
        }
        else
        {
            Followed++;
        }
    }

    // A place in a paragraph's text, moved along as its lines are followed.
    sealed class Cursor(ParagraphElement paragraph)
    {
        readonly IReadOnlyList<Run> runs = paragraph.Runs;
        readonly string?[] texts = paragraph.Runs.Select(Drawn).ToArray();
        int run;
        int character;

        public int NextLine { get; set; }

        public bool Lost { get; set; }

        public void Restart()
        {
            run = 0;
            character = 0;
            NextLine = 0;
            Lost = false;
        }

        // The text the measurer lays out for a run, or null for a run it lays none for — the mirror of
        // CanonicalParagraphMeasurer.Flatten.
        static string? Drawn(Run run)
        {
            if (run.InlineImageData is {Length: > 0} ||
                run.InlineImageRasterFallbackData is {Length: > 0} ||
                run.InlineShapeGroup != null ||
                run.IsTab ||
                run.Properties.Hidden ||
                string.IsNullOrEmpty(run.Text))
            {
                return null;
            }

            var text = run.Text;
            if (run.Properties.AllCaps)
            {
                text = text.ToUpperInvariant();
            }

            return text.Replace(' ', ' ');
        }

        public IReadOnlyList<RunPiece>? Follow(string placed)
        {
            List<RunPiece>? pieces = null;
            for (var index = 0; index < placed.Length; index++)
            {
                if (!Find(placed[index]))
                {
                    Lost = true;
                    return pieces;
                }

                // Find left the cursor just past the character it matched.
                var at = character - 1;
                if (runs[run].Source is not { } source)
                {
                    continue;
                }

                var position = source.Atomic ? source : source with {Start = source.Start + at};
                pieces ??= [];
                if (pieces.Count > 0 &&
                    Continues(pieces[^1], index, position))
                {
                    pieces[^1] = pieces[^1] with {Length = pieces[^1].Length + 1};
                }
                else
                {
                    pieces.Add(new(index, 1, position));
                }
            }

            return pieces;
        }

        static bool Continues(RunPiece piece, int index, RunSource position)
        {
            if (piece.Start + piece.Length != index ||
                piece.Source.Run != position.Run ||
                piece.Source.Atomic != position.Atomic)
            {
                return false;
            }

            if (position.Atomic)
            {
                return piece.Source.Start == position.Start;
            }

            return piece.Source.Start + piece.Length == position.Start;
        }

        // Moves to the next character of the paragraph that is the placed one, passing only over
        // whitespace the layout may have dropped.
        bool Find(char placed)
        {
            while (run < texts.Length)
            {
                var text = texts[run];
                if (text == null ||
                    character >= text.Length)
                {
                    run++;
                    character = 0;
                    continue;
                }

                var candidate = text[character];
                if (candidate == placed)
                {
                    character++;
                    return true;
                }

                if (candidate is not (' ' or '\n'))
                {
                    return false;
                }

                character++;
            }

            return false;
        }
    }
}

/// <summary>
/// <see cref="Length"/> characters of a placed run's text from <see cref="Start"/>, and where the first
/// of them came from.
/// </summary>
readonly record struct RunPiece(int Start, int Length, RunSource Source);
