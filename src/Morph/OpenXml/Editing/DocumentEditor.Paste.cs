// Pasting paragraphs read from the clipboard (PastedHtml) into the paragraph the caret is in. The text
// goes in as typing does — through Rewrite, which splits the paragraph where the paste does and tracks
// what it adds — and then each paragraph the paste made whole is given what it was: a heading, a list
// and its level, an alignment. A pasted paragraph that lands among the paragraph's own text is part of
// that paragraph, and keeps its look, as in Word. Last come the links.
static partial class DocumentEditor
{
    /// <summary>
    /// Pastes paragraphs at a place in a paragraph — <paramref name="offset"/> places in, counted as
    /// <see cref="EditParagraph.Length"/> counts. The place returned is the end of what was pasted.
    /// </summary>
    public static EditResult Paste(byte[] docx, int paragraph, int offset, IReadOnlyList<PastedParagraph> content, EditOptions options)
    {
        if (content.Count == 0)
        {
            return new(docx, paragraph, offset);
        }

        // The paragraph's own content, either side of the place, and what pasted text is formatted
        // as: the text before the place, or failing that the text after it.
        var target = DocumentOutline.Read(docx).Paragraphs[paragraph];
        var before = new List<NewItem>();
        var after = new List<NewItem>();
        var like = -1;
        int? next = null;
        var place = 0;
        for (var index = 0; index < target.Units.Count; index++)
        {
            var unit = target.Units[index];
            if (!unit.Editable)
            {
                (place < offset ? before : after).Add(new(index, null));
                place++;
                continue;
            }

            var split = Math.Clamp(offset - place, 0, unit.Text.Length);
            if (split > 0)
            {
                before.Add(new(index, unit.Text[..split]));
                like = index;
            }

            if (split < unit.Text.Length)
            {
                after.Add(new(index, unit.Text[split..]));
                next ??= index;
            }

            place += unit.Text.Length;
        }

        if (like < 0)
        {
            like = next ?? -1;
        }

        var last = content.Count - 1;
        var paragraphs = new List<NewParagraph>();
        for (var index = 0; index <= last; index++)
        {
            var items = new List<NewItem>();
            if (index == 0)
            {
                items.AddRange(before);
            }

            foreach (var run in content[index].Runs)
            {
                items.Add(new(like, run.Text, run.Format));
            }

            if (index == last)
            {
                items.AddRange(after);
            }

            paragraphs.Add(new(items));
        }

        var bytes = Rewrite(docx, paragraph, paragraphs, options).Document;

        // A paragraph is the paste's own when none of the paragraph pasted into is in it.
        var owned = Enumerable.Range(0, content.Count)
            .Where(_ => (_ > 0 || before.Count == 0) && (_ < last || after.Count == 0))
            .ToList();
        if (owned.Count > 0)
        {
            bytes = Edit(
                bytes,
                options,
                edit =>
                {
                    edit.Shape(paragraph, content, owned);
                    return null;
                }).Document;
        }

        bytes = Links(bytes, paragraph, offset, content, options);
        var end = content[last].Text.Length;
        if (last == 0)
        {
            end += offset;
        }

        return new(bytes, paragraph + last, end);
    }

    // Each stretch of pasted text that links somewhere, made a link: from the end of the paragraph's
    // own text before the place, in the first pasted paragraph, and from the start in the others.
    static byte[] Links(byte[] docx, int paragraph, int offset, IReadOnlyList<PastedParagraph> content, EditOptions options)
    {
        var links = new List<(int Paragraph, int From, int To, string Address)>();
        for (var index = 0; index < content.Count; index++)
        {
            var at = 0;
            if (index == 0)
            {
                at = offset;
            }

            foreach (var run in content[index].Runs)
            {
                var from = at;
                at += run.Text.Length;
                if (run.Link is not { } address)
                {
                    continue;
                }

                // Runs that link to the same place one after another are one link.
                if (links.Count > 0 &&
                    links[^1] is var (linkParagraph, linkFrom, linkTo, linkAddress) &&
                    linkParagraph == paragraph + index &&
                    linkTo == from &&
                    linkAddress == address)
                {
                    links[^1] = (linkParagraph, linkFrom, at, address);
                    continue;
                }

                links.Add((paragraph + index, from, at, address));
            }
        }

        foreach (var (ordinal, from, to, address) in links)
        {
            var read = DocumentOutline.Read(docx).Paragraphs[ordinal];
            if (read.PositionAt(from, ending: false) is { } start &&
                read.PositionAt(to, ending: true) is { } end)
            {
                docx = Link(docx, start, end, address, options);
            }
        }

        return docx;
    }

    sealed partial class Editing
    {
        // The paragraphs the paste made whole, made what they were: in their style or else the
        // document's default, out of the list the paragraph pasted into was in, in their own list,
        // at their level and alignment.
        public void Shape(int first, IReadOnlyList<PastedParagraph> content, List<int> owned)
        {
            foreach (var index in owned)
            {
                var pasted = content[index];
                var paragraph = Find(first + index);
                if (pasted.Style is { } style)
                {
                    Define(style);
                }

                var properties = Reformat(paragraph);
                foreach (var child in properties.ChildElements.ToList())
                {
                    if (child is not (ParagraphMarkRunProperties or SectionProperties or ParagraphPropertiesChange))
                    {
                        child.Remove();
                    }
                }

                if (pasted.Style is { } named &&
                    named != DefaultStyle &&
                    StyleOf(named) != null)
                {
                    properties.ParagraphStyleId = new()
                    {
                        Val = named
                    };
                }

                Unclutter(paragraph);
            }

            // Items one after another in a list of one kind are one list.
            var group = new List<int>();
            for (var position = 0; position <= owned.Count; position++)
            {
                if (position < owned.Count &&
                    content[owned[position]].List is not ListKind.None and var kind &&
                    (group.Count == 0 ||
                     (owned[position] == group[^1] + 1 && content[group[^1]].List == kind)))
                {
                    group.Add(owned[position]);
                    continue;
                }

                if (group.Count > 0)
                {
                    List(group.Select(_ => Find(first + _)).ToList(), content[group[0]].List);
                    foreach (var index in group)
                    {
                        Indent(Find(first + index), content[index].Level);
                    }

                    group.Clear();
                }

                if (position < owned.Count &&
                    content[owned[position]].List != ListKind.None)
                {
                    group.Add(owned[position]);
                }
            }

            foreach (var index in owned)
            {
                if (content[index].Alignment is { } alignment)
                {
                    Align(Find(first + index), alignment);
                }
            }
        }
    }
}
