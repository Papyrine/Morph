// Bookmarks: a name for a place, or for some text, which a link to #name goes to. Word-probed
// 2026-10-21 (Word 365 over COM, Bookmarks.Add over a range and over an empty place):
//
//   - <w:bookmarkStart w:id="0" w:name="Target"/> stands before the text's first run, and
//     <w:bookmarkEnd w:id="0"/> after its last; ids count from 0. A bookmark of a place is the two
//     together.
//   - Word's Bookmark dialog takes a name that starts with a letter and holds letters, digits and
//     underscores, at most 40 of them; a name already used moves to the new place. Names starting with
//     an underscore are Word's own (hidden bookmarks, such as a table of contents' _Toc…).
static partial class DocumentEditor
{
    /// <summary>Whether Word's Bookmark dialog takes a name.</summary>
    public static bool IsBookmarkName(string name) =>
        BookmarkName.IsMatch(name);

    [GeneratedRegex(@"^\p{L}[\p{L}\p{Nd}_]{0,39}$")]
    private static partial Regex BookmarkName { get; }

    /// <summary>
    /// Names a place, or the text between two places of the body, as a bookmark; a bookmark of the
    /// name already is moved. The place returned is the caret's.
    /// </summary>
    public static EditResult AddBookmark(byte[] docx, int paragraph, int offset, SourcePosition? start, SourcePosition? end, string name, EditOptions options)
    {
        if (!IsBookmarkName(name))
        {
            throw new InvalidOperationException("A bookmark's name starts with a letter, and holds only letters, numbers and underscores, at most 40.");
        }

        return Edit(docx, options, edit => edit.Bookmark(paragraph, offset, start, end, name));
    }

    /// <summary>Removes a bookmark, leaving its text.</summary>
    public static byte[] DeleteBookmark(byte[] docx, string name, EditOptions options) =>
        Edit(
            docx,
            options,
            edit =>
            {
                edit.Unbookmark(name);
                return null;
            }).Document;

    sealed partial class Editing
    {
        public (Paragraph Paragraph, int Offset) Bookmark(int ordinal, int offset, SourcePosition? start, SourcePosition? end, string name)
        {
            Unbookmark(name);
            var ids = Stories()
                .SelectMany(_ => _.Descendants<BookmarkStart>())
                .Select(_ => int.TryParse(_.Id?.Value, out var id) ? id : -1)
                .DefaultIfEmpty(-1)
                .Max() + 1;
            var id = ids.ToString(CultureInfo.InvariantCulture);
            var opening = new BookmarkStart
            {
                Id = id,
                Name = name
            };
            var closing = new BookmarkEnd
            {
                Id = id
            };

            var paragraph = Find(ordinal);
            if (start is { } from &&
                end is { } to &&
                from != to &&
                Runs(from, to) is { Count: > 0 } runs)
            {
                runs[0].InsertBeforeSelf(opening);
                runs[^1].InsertAfterSelf(closing);
                return (paragraph, offset);
            }

            var point = At(paragraph, offset);
            point.Insert(opening);
            point.Insert(closing);
            return (paragraph, offset);
        }

        public void Unbookmark(string name)
        {
            foreach (var root in Stories())
            {
                foreach (var opening in root.Descendants<BookmarkStart>().Where(_ => _.Name?.Value == name).ToList())
                {
                    var id = opening.Id?.Value;
                    opening.Remove();
                    foreach (var closing in root.Descendants<BookmarkEnd>().Where(_ => _.Id?.Value == id).ToList())
                    {
                        closing.Remove();
                    }
                }
            }
        }
    }
}
