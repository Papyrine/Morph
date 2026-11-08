using Wp = DocumentFormat.OpenXml.Drawing.Wordprocessing;
using OoxmlFieldCode = DocumentFormat.OpenXml.Wordprocessing.FieldCode;
using OoxmlRun = DocumentFormat.OpenXml.Wordprocessing.Run;
using OoxmlRunProperties = DocumentFormat.OpenXml.Wordprocessing.RunProperties;
using Unit = ParagraphContent.Unit;

// What is not text, moved past another of its kind: a picture, a break, a field or a note's reference
// dragged in the open paragraph over a neighbour that is not text either. Rewrite edits the text a
// stretch at a time between such things, so the ones that keep their order still divide it — the
// longest run of them in their old order — and each of the others is moved: written into the new text
// as a mark, which the text's own diff then places among the characters (inside the insertion, when
// tracked), and put in its mark's place.
//
// Untracked, it is taken from where it was and put down whole, a note's reference keeping its note.
// Tracked, it is struck where it was and a copy goes in as an insertion, so rejecting the move puts it
// back; a picture's copy takes a drawing id of its own, and a simple field goes in as the complex field
// it is, since only runs can be inserted. A note's reference is not moved under tracking: both places
// would refer to one note.
static partial class DocumentEditor
{
    // The first of the marks: Unicode's noncharacters, which no document holds.
    const char firstMark = '﷐';
    const int markCount = 32;

    sealed partial class Editing
    {
        // The atoms, by unit, that keep their order: the subsequence of `order` that rises and weighs
        // most, where one that cannot be moved outweighs every one that can — so text a tracked change
        // deleted stays put and what is dragged past it moves.
        HashSet<int> InOrder(List<int> order, List<Unit> units)
        {
            if (order.Count == 0)
            {
                return [];
            }

            long Weight(int unit) =>
                Movable(units[unit]) ? 1 : order.Count + 1;

            var total = new long[order.Count];
            var previous = new int[order.Count];
            var best = 0;
            for (var i = 0; i < order.Count; i++)
            {
                total[i] = Weight(order[i]);
                previous[i] = -1;
                for (var j = 0; j < i; j++)
                {
                    if (order[j] < order[i] &&
                        total[j] + Weight(order[i]) > total[i])
                    {
                        total[i] = total[j] + Weight(order[i]);
                        previous[i] = j;
                    }
                }

                if (total[i] > total[best])
                {
                    best = i;
                }
            }

            var staying = new HashSet<int>();
            for (var at = best; at >= 0; at = previous[at])
            {
                staying.Add(order[at]);
            }

            return staying;
        }

        // Whether a unit can be moved at all, and moved the way this edit writes it.
        bool Movable(Unit unit) =>
            Removable(unit) &&
            !(options.Track &&
              unit.Kind == EditUnitKind.Note) &&
            (unit.First is SimpleField ||
             (unit.First.Parent is OoxmlRun &&
              unit.Last.Parent is OoxmlRun));

        // A copy of what a unit is made of, to put down at its new place, divided off from what is
        // either side of it first.
        List<OpenXmlElement> Lift(Unit unit)
        {
            if (unit.First is SimpleField simple)
            {
                return options.Track
                    ? [.. Complex(simple)]
                    : [simple.CloneNode(true)];
            }

            var copies = RunsOf(unit)
                .Select(_ => _.CloneNode(true))
                .ToList();
            if (options.Track)
            {
                // Both stand in the file until the move is accepted or rejected.
                foreach (var properties in copies.SelectMany(_ => _.Descendants<Wp.DocProperties>()))
                {
                    properties.Id = NextDrawing();
                }
            }

            return copies;
        }

        // A simple field as the complex one it stands for: its code between a beginning and a
        // separator, its result after.
        static IEnumerable<OpenXmlElement> Complex(SimpleField simple)
        {
            var result = simple.Elements<OoxmlRun>().Select(_ => (OoxmlRun) _.CloneNode(true)).ToList();
            var look = result.FirstOrDefault()?.RunProperties;

            OoxmlRun Holding(OpenXmlElement child)
            {
                var run = new OoxmlRun();
                if (look != null)
                {
                    run.AppendChild((OoxmlRunProperties) look.CloneNode(true));
                }

                run.AppendChild(child);
                return run;
            }

            yield return Holding(new FieldChar {FieldCharType = FieldCharValues.Begin});
            yield return Holding(
                new OoxmlFieldCode(simple.Instruction?.Value ?? "")
                {
                    Space = SpaceProcessingModeValues.Preserve
                });
            yield return Holding(new FieldChar {FieldCharType = FieldCharValues.Separate});
            foreach (var run in result)
            {
                yield return run;
            }

            yield return Holding(new FieldChar {FieldCharType = FieldCharValues.End});
        }

        // Takes a unit from where it was: untracked, its runs go (a note's reference without its note,
        // which goes with the copy); tracked, they are struck, as a deletion strikes them.
        void TakeOut(Unit unit)
        {
            if (options.Track)
            {
                Drop(unit);
                return;
            }

            IEnumerable<OpenXmlElement> held = unit.First is SimpleField simple ? [simple] : RunsOf(unit);
            foreach (var element in held.ToList())
            {
                var holder = element.Parent;
                element.Remove();
                if (holder != null)
                {
                    Vacate(holder);
                }
            }
        }

        // Puts what was lifted where its mark is in the paragraphs the rewrite left, and the mark goes.
        void PutDown(IReadOnlyList<Paragraph> paragraphs, char mark, List<OpenXmlElement> lifted)
        {
            foreach (var text in paragraphs.SelectMany(_ => _.Descendants<Text>()))
            {
                var at = Raw(text).IndexOf(mark);
                if (at < 0 ||
                    text.Parent is not OoxmlRun run)
                {
                    continue;
                }

                // The mark divided off into a run of its own, which what was lifted takes the place of.
                var marked = (OoxmlRun) Ahead(run, text, at).Before!;
                var first = marked.ChildElements.First(_ => _ is not OoxmlRunProperties);
                Ahead(marked, first, 1);
                foreach (var element in lifted)
                {
                    marked.InsertBeforeSelf(element);
                }

                marked.Remove();
                return;
            }

            throw new InvalidOperationException("A moved picture, field or note lost its place.");
        }
    }
}
