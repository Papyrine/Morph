using OoxmlEndnote = DocumentFormat.OpenXml.Wordprocessing.Endnote;
using OoxmlFieldCode = DocumentFormat.OpenXml.Wordprocessing.FieldCode;
using OoxmlFootnote = DocumentFormat.OpenXml.Wordprocessing.Footnote;
using OoxmlRun = DocumentFormat.OpenXml.Wordprocessing.Run;
using Unit = ParagraphContent.Unit;

// What is not text, deleted: a picture, a page or column break, a field, a note's reference. The
// editor shows each as one place, and the reader deletes it as one: Backspace beside it, or a
// selection that takes it in. Text a tracked change deleted is not among them — that is settled in
// the review pane — and nor is a field that began in an earlier paragraph, whose end is in this one.
//
// Untracked, the runs that held it go, and a note's reference takes its note with it, as Word's does.
// Tracked, they are marked deleted (w:del), their text and field code struck out, and the note stays
// until the deletion is accepted.
static partial class DocumentEditor
{
    sealed partial class Editing
    {
        static bool Removable(Unit unit) =>
            unit.Kind switch
            {
                EditUnitKind.Drawing or EditUnitKind.Break or EditUnitKind.Note => true,
                EditUnitKind.Field => unit.First is SimpleField ||
                                      (unit.First is FieldChar {FieldCharType.Value: var begin} &&
                                       begin == FieldCharValues.Begin &&
                                       unit.Last is FieldChar {FieldCharType.Value: var end} &&
                                       end == FieldCharValues.End),
                _ => false
            };

        // The things between two places of a chain of paragraphs that are not text and can go.
        List<Unit> Atoms(List<Paragraph> chain, SourcePosition start, SourcePosition end)
        {
            if (end.Run < start.Run ||
                (end.Run == start.Run && end.Offset < start.Offset))
            {
                (start, end) = (end, start);
            }

            var ordinals = SourceRuns.Index(main.Document!);
            var found = new List<Unit>();
            foreach (var paragraph in chain)
            {
                foreach (var unit in Units(paragraph))
                {
                    if (unit.Editable ||
                        !Removable(unit) ||
                        !ordinals.TryGetValue(unit.Run, out var run))
                    {
                        continue;
                    }

                    var after = run > start.Run || (run == start.Run && unit.Start >= start.Offset);
                    var ahead = run < end.Run || (run == end.Run && unit.Start < end.Offset);
                    if (after && ahead)
                    {
                        found.Add(unit);
                    }
                }
            }

            return found;
        }

        void Drop(Unit unit)
        {
            if (unit.First is SimpleField simple)
            {
                if (options.Track)
                {
                    foreach (var run in simple.Descendants<OoxmlRun>().ToList())
                    {
                        Strike(run);
                    }

                    return;
                }

                var holder = simple.Parent;
                simple.Remove();
                if (holder != null)
                {
                    Vacate(holder);
                }

                return;
            }

            if (unit.First.Parent is not OoxmlRun first ||
                unit.Last.Parent is not OoxmlRun)
            {
                return;
            }

            // The runs that hold it, divided off from what is either side of it.
            Ahead(first, unit.First, 0);
            Ahead((OoxmlRun) unit.Last.Parent!, unit.Last, int.MaxValue);
            var opening = (OoxmlRun) unit.First.Parent!;
            var closing = (OoxmlRun) unit.Last.Parent!;
            var runs = new List<OoxmlRun>();
            var inside = false;
            foreach (var run in opening.Ancestors<Paragraph>().First().Descendants<OoxmlRun>())
            {
                inside |= ReferenceEquals(run, opening);
                if (inside)
                {
                    runs.Add(run);
                }

                if (ReferenceEquals(run, closing))
                {
                    break;
                }
            }

            if (!options.Track &&
                unit.Kind == EditUnitKind.Note)
            {
                Unnote(unit.First);
            }

            foreach (var run in runs)
            {
                if (options.Track)
                {
                    Strike(run);
                    continue;
                }

                var holder = run.Parent;
                run.Remove();
                if (holder != null)
                {
                    Vacate(holder);
                }
            }
        }

        // A run marked deleted, its text and field code struck out. One the same author inserted
        // is simply taken back out.
        void Strike(OoxmlRun run)
        {
            if (Insertion(run) is { } insertion &&
                IsOwn(insertion))
            {
                var holder = run.Parent;
                run.Remove();
                if (holder != null)
                {
                    Vacate(holder);
                }

                return;
            }

            if (run.Parent is DeletedRun)
            {
                return;
            }

            foreach (var text in run.Elements<Text>().ToList())
            {
                run.ReplaceChild(
                    new DeletedText(Raw(text))
                    {
                        Space = SpaceProcessingModeValues.Preserve
                    },
                    text);
            }

            foreach (var code in run.Elements<OoxmlFieldCode>().ToList())
            {
                run.ReplaceChild(
                    new DeletedFieldCode(code.Text)
                    {
                        Space = SpaceProcessingModeValues.Preserve
                    },
                    code);
            }

            var deletion = Stamp(new DeletedRun());
            run.InsertBeforeSelf(deletion);
            run.Remove();
            deletion.AppendChild(run);
        }

        // A note goes with its reference, as Word's does.
        void Unnote(OpenXmlElement reference)
        {
            switch (reference)
            {
                case FootnoteReference {Id.Value: var id}:
                    main.FootnotesPart?.Footnotes?.Elements<OoxmlFootnote>().FirstOrDefault(_ => _.Id?.Value == id)?.Remove();
                    break;
                case EndnoteReference {Id.Value: var id}:
                    main.EndnotesPart?.Endnotes?.Elements<OoxmlEndnote>().FirstOrDefault(_ => _.Id?.Value == id)?.Remove();
                    break;
            }
        }
    }
}
