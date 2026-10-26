// One line per body paragraph, one per table row, trailing lines for shapes and other stories, then a
// summary line. Extends EditFixtures.Read / EditDocuments.Shape (runs `[b:bold]`, `{+…+}` `{-…-}`,
// suffixes ` (center)` ` #Heading1` ` •1.0`, `<[link]>`, `«`/`|`/`»` field marks, `▣`, `¶`) with:
//
//   ‸          caret; inside a selection it marks the active end: ⟦abc‸⟧ end-active, ⟦‸abc⟧ start-active
//   ⟦ … ⟧      the selection
//   ¶          a paragraph mark, shown only when it is inside the selection
//   \t \n      tab, line break;  ⤓ page break;  § section break;  ⍽ non-breaking space
//   ▣          inline picture;  ⚓n floating picture's anchor (details on a trailing SHPn line)
//   †n ‡n      footnote / endnote reference;  ⟪…⟫n a comment's scope and reference
//   ║ … ¤      table cells and their end-of-cell marks; the row ends with its end-of-row ¤
//   ⇥L,F       left and first-line indent in points, when either is non-zero
static class StateNotation
{
    public const char Caret = '‸';
    public const char SelectionOpen = '⟦';
    public const char SelectionClose = '⟧';

    public static List<string> Lines(Observation observation)
    {
        var lines = new List<string>();
        var selection = observation.Selection;
        var inMain = selection.StoryType == nameof(Word.WdStoryType.wdMainTextStory);
        var mainSelection = inMain ? selection : null;
        Render(observation.Paragraphs, observation, mainSelection, "", lines);
        foreach (var shape in observation.Shapes)
        {
            var line = $"SHP{shape.Index} ⌖P{shape.AnchorParagraph} {shape.Kind} {Round(shape.Width)}x{Round(shape.Height)}pt {shape.Wrap} @{Round(shape.Left)},{Round(shape.Top)}";
            if (shape.Selected)
            {
                line += " ⟦selected⟧";
            }

            lines.Add(line);
        }

        if (observation.Story != null)
        {
            var prefix = observation.Story.StoryType switch
            {
                nameof(Word.WdStoryType.wdPrimaryHeaderStory) => "HDR: ",
                nameof(Word.WdStoryType.wdPrimaryFooterStory) => "FTR: ",
                nameof(Word.WdStoryType.wdFootnotesStory) => "FN: ",
                nameof(Word.WdStoryType.wdEndnotesStory) => "EN: ",
                nameof(Word.WdStoryType.wdCommentsStory) => "CMT: ",
                nameof(Word.WdStoryType.wdTextFrameStory) => "TB: ",
                _ => observation.Story.StoryType + ": "
            };
            Render(observation.Story.Paragraphs, observation, inMain ? null : selection, prefix, lines);
        }

        lines.Add(Summary(observation));
        return lines;
    }

    static string Round(float value) =>
        Math.Round(value, 1).ToString(CultureInfo.InvariantCulture);

    // A tracked move is two revisions Word keeps apart from a plain deletion and insertion (wdRevisionMovedFrom
    // / wdRevisionMovedTo); the brackets say which, since the editor writes w:ins / w:del and never a move.
    // Every other kind (Property, ParagraphProperty, Style, ParagraphNumber, …) is a formatting change: the
    // text stays and only its look or its paragraph's properties are tracked.
    static string RevisionOpen(string type) =>
        type switch
        {
            "Insert" => "{+",
            "Delete" => "{-",
            "MovedFrom" => "{<",
            "MovedTo" => "{>",
            _ => "{~"
        };

    static string RevisionClose(string type) =>
        type switch
        {
            "Insert" => "+}",
            "Delete" => "-}",
            "MovedFrom" => "<}",
            "MovedTo" => ">}",
            _ => "~}"
        };

    static void Render(List<ParagraphState> paragraphs, Observation observation, SelectionState? selection, string prefix, List<string> lines)
    {
        var i = 0;
        while (i < paragraphs.Count)
        {
            var paragraph = paragraphs[i];
            if (paragraph.Table == 0)
            {
                lines.Add(prefix + Paragraph(paragraph, observation, selection, isCellContent: false));
                i++;
                continue;
            }

            // Gather the row: consecutive paragraphs of the same top-level table and row.
            var table = observation.Tables.FirstOrDefault(_ => _.Index == paragraph.Table);
            var rowCells = table?.Cells.Where(_ => _.Row == paragraph.Row).OrderBy(_ => _.Column).ToList() ?? [];
            var rowParagraphs = new List<ParagraphState>();
            while (i < paragraphs.Count &&
                   paragraphs[i].Table == paragraph.Table &&
                   paragraphs[i].Row == paragraph.Row)
            {
                rowParagraphs.Add(paragraphs[i]);
                i++;
            }

            var builder = new StringBuilder();
            builder.Append(prefix).Append('T').Append(paragraph.Table).Append(" R").Append(paragraph.Row).Append(' ');
            foreach (var cell in rowCells)
            {
                builder.Append('║');
                var cellParagraphs = rowParagraphs.Where(_ => _.Start >= cell.Start && _.Start < cell.End).ToList();
                for (var k = 0; k < cellParagraphs.Count; k++)
                {
                    if (k > 0)
                    {
                        builder.Append(" ¶ ");
                    }

                    if (cellParagraphs[k].NestingLevel > 1)
                    {
                        builder.Append('⊞');
                    }

                    builder.Append(Paragraph(cellParagraphs[k], observation, selection, isCellContent: true));
                }
            }

            builder.Append('║');
            var rowEnd = rowCells.Count > 0 ? rowCells[0].RowEnd : -1;
            if (selection != null &&
                rowEnd >= 0)
            {
                if (!selection.IsCollapsed && selection.Start <= rowEnd && selection.End > rowEnd)
                {
                    builder.Append(selection.Start == rowEnd ? "⟦" : "");
                }

                if (selection.IsCollapsed && selection.Start == rowEnd)
                {
                    builder.Append(Caret);
                }
            }

            builder.Append('¤');
            if (selection is
                {
                    IsCollapsed: false
                } &&
                rowEnd >= 0 &&
                selection.End == rowEnd + 1)
            {
                if (!selection.StartIsActive)
                {
                    builder.Append(Caret);
                }

                builder.Append(SelectionClose);
            }

            lines.Add(builder.ToString());
        }
    }

    static string Paragraph(ParagraphState paragraph, Observation observation, SelectionState? selection, bool isCellContent)
    {
        var builder = new StringBuilder();
        var text = paragraph.Text;
        var start = paragraph.Start;
        var end = paragraph.End;
        var runs = paragraph.Runs;
        var fields = observation.Fields.Where(_ => _.Begin >= start && _.Begin < end).ToList();
        var revisions = observation.Revisions.Where(_ => _.Start < end && _.End > start).ToList();
        var comments = observation.Comments;
        var hyperlinkCodes = fields.Where(_ => _.TypeName == "Hyperlink").ToList();
        var mismatch = paragraph.LengthMismatch;

        // Run boundaries also fall at field marks so code and result get their own brackets.
        var boundaries = new SortedSet<int>(runs.Select(_ => _.Start));
        foreach (var field in fields)
        {
            boundaries.Add(field.Begin);
            boundaries.Add(field.End + 1);
            if (field.TypeName != "Hyperlink")
            {
                boundaries.Add(field.Begin + 1);
                boundaries.Add(field.Separator);
                boundaries.Add(field.Separator + 1);
                boundaries.Add(field.End);
            }
        }

        foreach (var revision in revisions)
        {
            boundaries.Add(revision.Start);
            boundaries.Add(revision.End);
        }

        boundaries.RemoveWhere(_ => _ < start || _ >= end);
        var runOpen = false;
        var selectionOpen = false;
        if (selection != null &&
            !selection.IsCollapsed &&
            selection.Start < start &&
            selection.End > start)
        {
            // The selection began in an earlier paragraph: this line opens inside it.
            builder.Append(SelectionOpen);
            selectionOpen = true;
        }

        for (var p = start; p < end; p++)
        {
            var textIndex = p - start;
            var character = mismatch ? (textIndex < text.Length ? text[textIndex] : '\0') : text[textIndex];
            var isMark = p == end - 1 && character is '\r' or '\a';

            // Openers.
            foreach (var revision in revisions.Where(_ => _.Start == p))
            {
                if (runOpen)
                {
                    builder.Append(']');
                    runOpen = false;
                }

                builder.Append(RevisionOpen(revision.Type));
            }

            foreach (var comment in comments.Where(_ => _.ScopeStart == p && _.ScopeEnd > p))
            {
                builder.Append('⟪');
            }

            var hyperlinkCode = hyperlinkCodes.FirstOrDefault(_ => p >= _.Begin && p <= _.Separator);
            var hyperlinkEdge = hyperlinkCodes.Any(_ => p == _.Separator + 1 || p == _.End);
            if (boundaries.Contains(p) && !isMark && !hyperlinkEdge)
            {
                if (runOpen)
                {
                    builder.Append(']');
                }

                if (hyperlinkCode == null)
                {
                    var run = runs.LastOrDefault(_ => _.Start <= p) ?? runs[0];
                    builder.Append('[');
                    if (run.Marks.Length > 0)
                    {
                        builder.Append(run.Marks).Append(':');
                    }

                    runOpen = true;
                }
                else
                {
                    runOpen = false;
                }
            }

            if (selection != null)
            {
                if (!selection.IsCollapsed && selection.Start == p)
                {
                    builder.Append(SelectionOpen);
                    selectionOpen = true;
                    if (selection.StartIsActive)
                    {
                        builder.Append(Caret);
                    }
                }

                if (selection.IsCollapsed && selection.Start == p)
                {
                    builder.Append(Caret);
                }
            }

            // The character.
            if (hyperlinkCode != null)
            {
                if (p == hyperlinkCode.Begin)
                {
                    builder.Append('<');
                }

                if (p == hyperlinkCode.Separator)
                {
                    var display = runs.LastOrDefault(_ => _.Start <= p + 1) ?? runs[0];
                    builder.Append('[');
                    if (display.Marks.Length > 0)
                    {
                        builder.Append(display.Marks).Append(':');
                    }

                    runOpen = true;
                }
            }
            else if (hyperlinkCodes.Any(_ => _.End == p))
            {
                if (runOpen)
                {
                    builder.Append(']');
                    runOpen = false;
                }

                builder.Append('>');
            }
            else
            {
                builder.Append(Render(character, p, paragraph, observation, selection, isMark));
            }

            // Closers.
            if (selection != null &&
                !selection.IsCollapsed &&
                selection.End == p + 1 &&
                selectionOpen)
            {
                if (!selection.StartIsActive)
                {
                    builder.Append(Caret);
                }

                builder.Append(SelectionClose);
                selectionOpen = false;
            }

            foreach (var comment in comments.Where(_ => _.ScopeEnd == p + 1 && _.ScopeStart < p + 1))
            {
                builder.Append('⟫').Append(comment.Index);
            }

            foreach (var revision in revisions.Where(_ => _.End == p + 1))
            {
                if (runOpen)
                {
                    builder.Append(']');
                    runOpen = false;
                }

                builder.Append(RevisionClose(revision.Type));
            }
        }

        if (selection != null &&
            !selection.IsCollapsed &&
            selectionOpen &&
            selection.End >= end)
        {
            // The selection runs past this paragraph; close the bracket on this line.
            if (selection.End == end && !selection.StartIsActive)
            {
                builder.Append(Caret);
            }

            builder.Append(SelectionClose);
        }

        if (runOpen)
        {
            builder.Append(']');
        }

        if (selection != null &&
            selection.IsCollapsed &&
            selection.Start == end &&
            !isCellContent &&
            paragraph.Index == observation.Paragraphs.Count &&
            observation.Paragraphs.Count > 0 &&
            observation.Paragraphs[^1].End == end)
        {
            // A caret at the very end of the story (after the final mark) is drawn after the last paragraph.
            builder.Append(Caret);
        }

        var line = builder.ToString();
        if (!isCellContent || true)
        {
            line += Suffixes(paragraph);
        }

        return line;
    }

    static string Render(char character, int position, ParagraphState paragraph, Observation observation, SelectionState? selection, bool isMark)
    {
        if (observation.InlinePictures.Contains(position))
        {
            return "▣";
        }

        // A symbol-font character (Ctrl+Shift+Q) comes back in the private-use range F000–F0FF; its low byte
        // is the key that was typed, which is what the line should show (the run's marks name the font).
        if (character is >= '' and <= '')
        {
            return ((char) (character - 0xF000)).ToString();
        }

        switch (character)
        {
            case '\r':
                if (isMark)
                {
                    return selection != null && !selection.IsCollapsed && selection.Start <= position && selection.End > position ? "¶" : "";
                }

                return "\\r";
            case '\a':
                return "¤";
            case '\t':
                return "\\t";
            case '\v':
                return "\\n";
            case '\f':
                return observation.SectionEnds.Contains(position) ? "§" : "⤓";
            case '\u0001':
                return "▣";
            case '\u0008':
            {
                var shape = observation.Shapes.FirstOrDefault(_ => _.Anchor == position);
                return shape == null ? "⚓" : $"⚓{shape.Index}";
            }
            case '\u0002':
            {
                var note = observation.Notes.FirstOrDefault(_ => _.Reference == position);
                if (note == null)
                {
                    return "†";
                }

                return (note.Kind == "footnote" ? "†" : "‡") + note.Index;
            }
            case '\u0005':
                return "";
            case '\u0013':
                return "«";
            case '\u0014':
                return "|";
            case '\u0015':
                return "»";
            case ' ':
                return "⍽";
            case '­':
            case '\u001F':
                return "⁃";
            case '\u001E':
                return "‑";
            case '\0':
            {
                // A position Word's text leaves out: a field mark, a floating anchor, a content control edge, or unknown.
                var field = observation.Fields.FirstOrDefault(_ => _.Begin == position || _.Separator == position || _.End == position);
                if (field != null)
                {
                    return field.Begin == position ? "«" : field.Separator == position ? "|" : "»";
                }

                var shape = observation.Shapes.FirstOrDefault(_ => _.Anchor == position);
                if (shape != null)
                {
                    return $"⚓{shape.Index}";
                }

                if (observation.ContentControls.Any(_ => _.Start - 1 == position))
                {
                    return "(|";
                }

                if (observation.ContentControls.Any(_ => _.End == position))
                {
                    return "|)";
                }

                return "";
            }
            case '[':
            case ']':
                return "\\" + character;
            default:
                if (char.IsControl(character))
                {
                    return $"\\u{(int) character:X4}";
                }

                return character.ToString();
        }
    }

    static string Suffixes(ParagraphState paragraph)
    {
        var builder = new StringBuilder();
        if (paragraph.Alignment.Length > 0)
        {
            builder.Append(" (").Append(paragraph.Alignment).Append(')');
        }

        if (paragraph.Style.Length > 0 &&
            paragraph.Style != "Normal")
        {
            builder.Append(" #").Append(paragraph.Style);
        }

        if (paragraph.ListLevel != null)
        {
            builder.Append(" •").Append(paragraph.ListIndex ?? 0).Append('.').Append(paragraph.ListLevel);
            if (!string.IsNullOrEmpty(paragraph.ListString))
            {
                builder.Append(' ').Append(ListGlyph(paragraph.ListString));
            }
        }

        if (Math.Abs(paragraph.LeftIndent) > 0.05f ||
            Math.Abs(paragraph.FirstLineIndent) > 0.05f)
        {
            builder.Append(" ⇥").Append(Math.Round(paragraph.LeftIndent, 1).ToString(CultureInfo.InvariantCulture)).Append(',').Append(Math.Round(paragraph.FirstLineIndent, 1).ToString(CultureInfo.InvariantCulture));
        }

        // Space before and after, a border and keep-with-next are the properties the merge grids (E-41b, E-60b)
        // vary one at a time, so the survivor has to show them. Normal and the heading styles carry their own
        // spacing, so the spacing shows only where it is not the style's: a style's own value is left unsaid by
        // comparing with what Normal gives (0 before, 8 after in this template).
        // A table cell's paragraphs take their spacing from the table style (0 after), so the suffix would
        // mark every cell; the spacing is shown for body paragraphs only.
        var body = paragraph.Style == "Normal" && paragraph.Table == 0;
        if (Math.Abs(paragraph.SpaceBefore) > 0.05f && body)
        {
            builder.Append(" ↑").Append(Math.Round(paragraph.SpaceBefore, 1).ToString(CultureInfo.InvariantCulture));
        }

        if (Math.Abs(paragraph.SpaceAfter - 8) > 0.05f && body)
        {
            builder.Append(" ↓").Append(Math.Round(paragraph.SpaceAfter, 1).ToString(CultureInfo.InvariantCulture));
        }

        if (paragraph.Bordered)
        {
            builder.Append(" ▭");
        }

        if (paragraph.KeepWithNext && paragraph.Style == "Normal")
        {
            builder.Append(" ⇊");
        }

        return builder.ToString();
    }

    static string ListGlyph(string listString)
    {
        var builder = new StringBuilder();
        foreach (var character in listString)
        {
            builder.Append(character switch
            {
                '' => '●',
                '' => '▪',
                '' => '❖',
                '' => '➢',
                '' => '✓',
                'o' when listString.Length == 1 => '○',
                >= '' and <= '' => '•',
                _ => character
            });
        }

        return builder.ToString();
    }

    /// <summary>The paragraph (or cell) holding the selection's active end, in the notation, for a step trace.</summary>
    public static string CaretLine(ProbeDocument probe, SelectionState selection)
    {
        try
        {
            var at = selection.StartIsActive ? selection.Start : Math.Max(selection.Start, selection.End - 1);
            var storyType = Enum.Parse<Word.WdStoryType>(selection.StoryType);
            var story = storyType == Word.WdStoryType.wdMainTextStory ? probe.Document.Content : probe.Document.StoryRanges[storyType];
            var range = story.Duplicate;
            range.SetRange(at, at);
            var paragraph = range.Paragraphs[1];
            var paragraphRange = paragraph.Range;
            var inTable = Com.Try(() => (bool) paragraphRange.Information[Word.WdInformation.wdWithInTable]);
            var text = ProbeDocument.StoryText(paragraphRange, inTable);
            var start = paragraphRange.Start;
            var end = paragraphRange.End;
            var state = new ParagraphState(0, start, end, text, [new(start, end, "")], "", "", 0, 0, null, null, null, 0, 0, 0, 0, text.Length != end - start);
            // Fields, pictures and section ends of this paragraph only, so the trace line reads like the final one.
            var fields = new List<FieldState>();
            foreach (Word.Field field in paragraphRange.Fields)
            {
                var code = field.Code;
                var result = Com.Try(() => field.Result);
                fields.Add(new((int) field.Type, field.Type.ToString().Replace("wdField", ""), code.Start - 1, code.End, result?.End ?? code.End, (code.Text ?? "").Trim()));
            }

            var pictures = new List<int>();
            foreach (Word.InlineShape inlineShape in paragraphRange.InlineShapes)
            {
                pictures.Add(inlineShape.Range.Start);
            }

            var sectionEnds = new List<int>();
            if (text.Contains('\f'))
            {
                var count = probe.Document.Sections.Count;
                for (var i = 1; i < count; i++)
                {
                    sectionEnds.Add(probe.Document.Sections[i].Range.End - 1);
                }
            }

            var empty = new Observation(selection, [], [], [], fields, [], [], [], [], pictures, sectionEnds, [], "off", false, null, null, [], null, []);
            var line = Paragraph(state, empty, selection, isCellContent: false);
            if (selection.Start < start ||
                selection.End > end)
            {
                line += " …";
            }

            return line;
        }
        catch (COMException)
        {
            return "?";
        }
    }

    public static string Summary(Observation observation)
    {
        var selection = observation.Selection;
        var parts = new List<string>();
        var active = selection.IsCollapsed ? "" : selection.StartIsActive ? " start-active" : " end-active";
        parts.Add($"Selection: {selection.Type} {selection.Start}–{selection.End}{active}");
        if (selection.StoryType != nameof(Word.WdStoryType.wdMainTextStory))
        {
            parts.Add($"Story {selection.StoryType.Replace("wd", "").Replace("Story", "")}");
        }

        if (selection.InTable)
        {
            var cells = $"Cells {selection.Cells}";
            if (selection.StartRow != null)
            {
                cells += $" (R{selection.StartRow}C{selection.StartColumn}–R{selection.EndRow}C{selection.EndColumn})";
            }

            if (selection.AtEndOfRowMarker)
            {
                cells += " at-row-end";
            }

            parts.Add(cells);
        }

        if (selection.ExtendMode)
        {
            parts.Add("extend-mode");
        }

        if (selection.ColumnSelectMode)
        {
            parts.Add("column-mode");
        }

        if (selection.Overtype)
        {
            parts.Add("overtype");
        }

        if (selection.InlineShapes > 0)
        {
            parts.Add($"InlineShapes {selection.InlineShapes}");
        }

        if (selection.Shapes > 0)
        {
            parts.Add($"Shapes {selection.Shapes}");
        }

        if (observation.Tracking)
        {
            parts.Add($"Tracking · Revisions {observation.Revisions.Count}");
        }
        else if (observation.Revisions.Count > 0)
        {
            parts.Add($"Revisions {observation.Revisions.Count}");
        }

        if (observation.UndoSource != "off")
        {
            parts.Add(observation.Undo.Count == 0 ? "Undo —" : observation.UndoSource == "control128" ? $"Undo: {string.Join(", ", observation.Undo)}" : $"Undo depth {observation.Undo.Count}");
        }

        if (observation.Dialog != null)
        {
            var dialogText = string.IsNullOrEmpty(observation.Dialog.Text) ? "" : $": {observation.Dialog.Text}";
            parts.Add($"Dialog \"{observation.Dialog.Title}\" ({observation.Dialog.ClassName}){dialogText}");
        }

        foreach (var hotspot in observation.Hotspots)
        {
            parts.Add($"{hotspot.Step} → {hotspot.Under}, {hotspot.Cursor}");
        }

        return string.Join(" · ", parts);
    }
}
