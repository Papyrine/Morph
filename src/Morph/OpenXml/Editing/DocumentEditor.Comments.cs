using OoxmlRun = DocumentFormat.OpenXml.Wordprocessing.Run;

// A comment whose whole anchor is deleted goes with it, as Word's does (WordProbe H-17, R-H-4: the
// anchor "quick" selected and deleted leaves "The brown fox…" and no comment). Backspace at the anchor's
// end only shrinks it, and the comment stays while a character of its anchor does. Tracked, the anchor's
// deletion is struck, the anchor stays, and so does the comment.
static partial class DocumentEditor
{
    sealed partial class Editing
    {
        // Runs an edit that can delete text, and deletes each comment it left nothing to anchor to.
        public T Unanchoring<T>(Func<T> edit)
        {
            // Read through the part's presence alone: a part whose DOM is loaded is written out again.
            if (options.Track ||
                main.WordprocessingCommentsPart == null)
            {
                return edit();
            }

            var before = Anchored();
            var result = edit();
            if (before.Count == 0)
            {
                return result;
            }

            var after = Anchored();
            foreach (var id in before.Where(_ => !after.Contains(_)))
            {
                ReviewEditor.DeleteComment(document, id);
            }

            return result;
        }

        // The comments whose anchors hold something: text, a picture, a note's reference — anything
        // between the anchor's start and its end that takes a place of its own.
        HashSet<string> Anchored()
        {
            var found = new HashSet<string>(StringComparer.Ordinal);
            foreach (var root in Stories())
            {
                var open = new HashSet<string>(StringComparer.Ordinal);
                foreach (var element in root.Descendants())
                {
                    switch (element)
                    {
                        case CommentRangeStart {Id.Value: { } id}:
                            open.Add(id);
                            break;
                        case CommentRangeEnd {Id.Value: { } id}:
                            open.Remove(id);
                            break;
                        default:
                            if (open.Count > 0 &&
                                element.Parent is OoxmlRun &&
                                (SourceRuns.Length(element) > 0 ||
                                 element is Drawing or DocumentFormat.OpenXml.Wordprocessing.Picture or DocumentFormat.OpenXml.Wordprocessing.EmbeddedObject))
                            {
                                found.UnionWith(open);
                            }

                            break;
                    }
                }
            }

            return found;
        }
    }
}
