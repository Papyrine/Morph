// Home > Bullets' and Numbering's libraries, and numbering restarted or continued. Word-probed
// 2026-10-23 (Word 365 over COM, gallery templates applied; see ListTemplates.Bullets):
//
//   - A library item applied to paragraphs not in a list starts a list whose first level is the
//     item's; applied to a list's paragraph, it changes the list, every paragraph of it.
//   - Restart at 1 (applied from this point forward) moves the paragraph, and the list's paragraphs
//     after it, to a new w:num over a copy of the list's definition — not a w:lvlOverride.
//   - Continue Numbering puts them back in the list of the same kind before them.
static partial class DocumentEditor
{
    /// <summary>
    /// Puts paragraphs in a list of a library item's look: a list a paragraph is in already takes the
    /// look, and the others join a list of that look beside them or start one.
    /// </summary>
    public static byte[] SetListStyle(byte[] docx, IReadOnlyList<int> paragraphs, ListStyle style, EditOptions options) =>
        Edit(
            docx,
            options,
            edit =>
            {
                edit.ListStyle(edit.FindAll(paragraphs), style);
                return null;
            }).Document;

    /// <summary>
    /// Numbers the list from the paragraph again at 1: it and the list's paragraphs after it are a new
    /// list, numbered as the old one was.
    /// </summary>
    public static byte[] RestartNumbering(byte[] docx, int paragraph, EditOptions options) =>
        Edit(
            docx,
            options,
            edit =>
            {
                edit.Restart(edit.Find(paragraph));
                return null;
            }).Document;

    /// <summary>
    /// Numbers the list from the paragraph on from the list of the same kind before it: it and the
    /// list's paragraphs after it join that list. Unchanged when there is none.
    /// </summary>
    public static byte[] ContinueNumbering(byte[] docx, int paragraph, EditOptions options) =>
        Edit(
            docx,
            options,
            edit =>
            {
                edit.Continue(edit.Find(paragraph));
                return null;
            }).Document;

    /// <summary>The library item a paragraph's list looks like, or null for none of them.</summary>
    public static ListStyle? ListStyleOf(byte[] docx, int paragraph)
    {
        using var stream = new MemoryStream(docx, false);
        using var document = WordprocessingDocument.Open(stream, false);
        var editing = new Editing(document, new("", DateTimeOffset.MinValue, false));
        return editing.StyleOfList(editing.Find(paragraph));
    }

    sealed partial class Editing
    {
        public void ListStyle(List<Paragraph> paragraphs, ListStyle style)
        {
            var unlisted = new List<Paragraph>();
            var restyled = new HashSet<int>();
            foreach (var paragraph in paragraphs)
            {
                if (Numbering(paragraph) is {Id: > 0} numbering &&
                    Definition(numbering.Id) is { } definition)
                {
                    if (restyled.Add(numbering.Id))
                    {
                        ListTemplates.Restyle(definition, style);
                    }

                    continue;
                }

                unlisted.Add(paragraph);
            }

            if (unlisted.Count == 0)
            {
                return;
            }

            var list = Alike(PreviousBlock(unlisted[0]), style) ??
                       Alike(NextBlock(unlisted[^1]), style) ??
                       NewList((id, nsid) => ListTemplates.Create(style.Kind, id, nsid, style));
            Assign(unlisted, list);
        }

        // The list a block beside paragraphs is in, when it looks like the item.
        int? Alike(OpenXmlElement? block, ListStyle style)
        {
            if (block is Paragraph paragraph &&
                Numbering(paragraph) is {Id: > 0} numbering &&
                ListTemplates.Looks(Definition(numbering.Id), style))
            {
                return numbering.Id;
            }

            return null;
        }

        public ListStyle? StyleOfList(Paragraph paragraph)
        {
            if (Numbering(paragraph) is not {Id: > 0} numbering ||
                Definition(numbering.Id) is not { } definition)
            {
                return null;
            }

            return ListTemplates.Bullets.Concat(ListTemplates.Numbers).FirstOrDefault(_ => ListTemplates.Looks(definition, _));
        }

        public void Restart(Paragraph paragraph)
        {
            if (Numbering(paragraph) is not {Id: > 0} numbering ||
                Definition(numbering.Id) is not { } definition)
            {
                return;
            }

            var list = NewList((id, nsid) =>
            {
                var copy = (AbstractNum) definition.CloneNode(true);
                copy.AbstractNumberId = id;
                copy.Nsid = new()
                {
                    Val = nsid.ToString("X8", CultureInfo.InvariantCulture)
                };
                return copy;
            });
            Assign(OnwardIn(paragraph, numbering.Id), list);
        }

        public void Continue(Paragraph paragraph)
        {
            if (Numbering(paragraph) is not {Id: > 0} numbering)
            {
                return;
            }

            var kind = KindOf(paragraph);
            var body = main.Document!.Body!.Descendants<Paragraph>().ToList();
            for (var index = body.IndexOf(paragraph) - 1; index >= 0; index--)
            {
                if (Numbering(body[index]) is {Id: > 0} before &&
                    before.Id != numbering.Id &&
                    KindOf(body[index]) == kind)
                {
                    Assign(OnwardIn(paragraph, numbering.Id), before.Id);
                    return;
                }
            }
        }

        // A paragraph and the body's paragraphs after it in the same list.
        List<Paragraph> OnwardIn(Paragraph paragraph, int list)
        {
            var body = main.Document!.Body!.Descendants<Paragraph>().ToList();
            var start = body.IndexOf(paragraph);
            if (start < 0)
            {
                return [paragraph];
            }

            return
            [
                .. body.Skip(start)
                    .Where(_ => ReferenceEquals(_, paragraph) ||
                                Numbering(_) is {Id: var id} &&
                                id == list)
            ];
        }
    }
}
