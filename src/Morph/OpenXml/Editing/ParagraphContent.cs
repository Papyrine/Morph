using DocumentFormat.OpenXml.Drawing.Wordprocessing;
using OoxmlParagraphProperties = DocumentFormat.OpenXml.Wordprocessing.ParagraphProperties;
using OoxmlRun = DocumentFormat.OpenXml.Wordprocessing.Run;

/// <summary>
/// What a paragraph holds, read off the markup in the order it holds it, for editing: the text that can
/// be edited, character by character, and between the stretches of it the things that are left as
/// they are — a picture, a field, a note's reference, text that a tracked change has deleted.
///
/// <see cref="DocumentOutline"/> reads a document this way for the viewer, and
/// <see cref="DocumentEditor"/> reads it again when the edit arrives, so the two agree on what unit 3
/// of paragraph 12 is. Positions are those of <see cref="SourceRuns"/>.
/// </summary>
static class ParagraphContent
{
    /// <summary>
    /// One character of editable text: where it is in the markup. An edit that divides a run, or puts
    /// text into one, moves the characters after it; <see cref="DocumentEditor"/> keeps each cell's
    /// place current as it goes, which is why a cell is an object and not a value.
    /// </summary>
    public sealed class Cell(OoxmlRun run, OpenXmlElement child, int index, int position)
    {
        /// <summary>The run it is in.</summary>
        public OoxmlRun Run { get; set; } = run;

        /// <summary>The child of the run it is, or is a character of.</summary>
        public OpenXmlElement Child { get; set; } = child;

        /// <summary>Which character of <see cref="Child"/>; 0 for a tab or a break.</summary>
        public int Index { get; set; } = index;

        /// <summary>Its position in the run it was read from, as <see cref="SourceRuns"/> counts.</summary>
        public int Position { get; } = position;

        /// <summary>The run it was read from.</summary>
        public OoxmlRun Source { get; } = run;
    }

    /// <summary>One unit of a paragraph, with the elements it was read from.</summary>
    public sealed class Unit(EditUnitKind kind, OoxmlRun run, int start)
    {
        public EditUnitKind Kind { get; } = kind;

        /// <summary>The run the unit begins in.</summary>
        public OoxmlRun Run { get; } = run;

        /// <summary>The position in <see cref="Run"/> it begins at.</summary>
        public int Start { get; } = start;

        public StringBuilder Text { get; } = new();

        /// <summary>The characters of a text unit; empty for any other kind.</summary>
        public List<Cell> Cells { get; } = [];

        /// <summary>The node the unit begins with: a run's child, or what wraps the run.</summary>
        public required OpenXmlElement First { get; init; }

        /// <summary>The node it ends with. A field can end runs after the one it began in.</summary>
        public required OpenXmlElement Last { get; set; }

        public double WidthPoints { get; init; }

        public double HeightPoints { get; init; }

        public bool Editable => Kind == EditUnitKind.Text;
    }

    /// <summary>Every paragraph of the part in document order, with its units.</summary>
    public static List<(Paragraph Paragraph, List<Unit> Units)> ReadAll(OpenXmlElement root)
    {
        // A field can begin in one paragraph and end in a later one — a table of contents does — so
        // how deep in fields the text is carries over from paragraph to paragraph. It carries within
        // a story: the text in a text box is no part of the field its anchor sits in.
        var fields = new Dictionary<OpenXmlElement, Stack<bool>>(ReferenceEqualityComparer.Instance);
        var paragraphs = new List<(Paragraph, List<Unit>)>();
        foreach (var paragraph in root.Descendants<Paragraph>())
        {
            var story = Story(paragraph, root);
            if (!fields.TryGetValue(story, out var open))
            {
                fields[story] = open = new();
            }

            var reader = new Reader(open);
            reader.Read(paragraph);
            paragraphs.Add((paragraph, reader.Units));
        }

        return paragraphs;
    }

    static OpenXmlElement Story(OpenXmlElement paragraph, OpenXmlElement root)
    {
        foreach (var ancestor in paragraph.Ancestors())
        {
            if (ancestor is TextBoxContent)
            {
                return ancestor;
            }
        }

        return root;
    }

    /// <summary>
    /// The runs of a paragraph's own text, through whatever wraps them — a link, a content control, a
    /// revision — with whether a tracked deletion or a simple field does. The paragraphs of a text box
    /// anchored in one of those runs are paragraphs of their own, and none of this one's.
    /// </summary>
    public static IEnumerable<(OoxmlRun Run, OpenXmlElement? Deletion, SimpleField? Field)> Runs(Paragraph paragraph) =>
        Runs(paragraph, null, null);

    static IEnumerable<(OoxmlRun Run, OpenXmlElement? Deletion, SimpleField? Field)> Runs(OpenXmlElement container, OpenXmlElement? deletion, SimpleField? field)
    {
        foreach (var child in container.ChildElements)
        {
            switch (child)
            {
                case OoxmlRun run:
                    yield return (run, deletion, field);
                    break;
                case OoxmlParagraphProperties:
                    break;
                case DeletedRun or MoveFromRun:
                    foreach (var inner in Runs(child, deletion ?? child, field))
                    {
                        yield return inner;
                    }

                    break;
                case SimpleField simple:
                    foreach (var inner in Runs(child, deletion, field ?? simple))
                    {
                        yield return inner;
                    }

                    break;
                default:
                    foreach (var inner in Runs(child, deletion, field))
                    {
                        yield return inner;
                    }

                    break;
            }
        }
    }

    sealed class Reader(Stack<bool> fields)
    {
        // The field that is open, and the text being gathered, if any.
        Unit? field;
        Unit? text;

        public List<Unit> Units { get; } = [];

        public void Read(Paragraph paragraph)
        {
            foreach (var (run, deletion, simple) in Runs(paragraph))
            {
                text = null;
                if (fields.Count > 0)
                {
                    // In a field that began in an earlier run, or an earlier paragraph.
                    Continue(run, run);
                    Inside(run);
                }
                else if (deletion != null)
                {
                    Whole(EditUnitKind.Deleted, run, deletion);
                }
                else if (simple != null)
                {
                    Whole(EditUnitKind.Field, run, simple);
                }
                else
                {
                    Inside(run);
                }
            }
        }

        // A run that is one thing from end to end, with what wraps it. Runs wrapped together are one unit.
        void Whole(EditUnitKind kind, OoxmlRun run, OpenXmlElement wrapper)
        {
            if (Units.Count > 0 &&
                Units[^1] is var last &&
                last.Kind == kind &&
                ReferenceEquals(last.First, wrapper))
            {
                last.Text.Append(SourceRuns.Text(run));
                return;
            }

            var unit = new Unit(kind, run, 0)
            {
                First = wrapper,
                Last = wrapper
            };
            unit.Text.Append(SourceRuns.Text(run));
            Units.Add(unit);
        }

        void Continue(OoxmlRun run, OpenXmlElement node)
        {
            if (field != null)
            {
                field.Last = node;
                return;
            }

            field = new(EditUnitKind.Field, run, 0)
            {
                First = node,
                Last = node
            };
            Units.Add(field);
        }

        void Inside(OoxmlRun run)
        {
            var position = 0;
            foreach (var child in run.ChildElements)
            {
                var length = SourceRuns.Length(child);
                switch (child)
                {
                    case FieldChar mark:
                        Field(run, mark, position);
                        break;
                    case Drawing or Picture or DocumentFormat.OpenXml.Wordprocessing.EmbeddedObject or AlternateContent when fields.Count == 0:
                        Fixed(EditUnitKind.Drawing, run, child, position, "");
                        break;
                    case Break when length == 0 && fields.Count == 0:
                        Fixed(EditUnitKind.Break, run, child, position, "");
                        break;
                    case FootnoteReference or EndnoteReference when length > 0 && fields.Count == 0:
                        Fixed(EditUnitKind.Note, run, child, position, "");
                        break;
                    default:
                        if (length > 0)
                        {
                            Characters(run, child, position);
                        }

                        break;
                }

                position += length;
            }

            text = null;
        }

        void Field(OoxmlRun run, FieldChar mark, int position)
        {
            var type = mark.FieldCharType?.Value;
            if (type == FieldCharValues.Begin)
            {
                if (fields.Count == 0)
                {
                    text = null;
                    field = new(EditUnitKind.Field, run, position)
                    {
                        First = mark,
                        Last = mark
                    };
                    Units.Add(field);
                }

                fields.Push(false);
                return;
            }

            if (fields.Count == 0)
            {
                return;
            }

            if (type == FieldCharValues.Separate)
            {
                fields.Pop();
                fields.Push(true);
                return;
            }

            if (type != FieldCharValues.End)
            {
                return;
            }

            fields.Pop();
            Continue(run, mark);
            if (fields.Count == 0)
            {
                field = null;
            }
        }

        void Fixed(EditUnitKind kind, OoxmlRun run, OpenXmlElement child, int position, string shown)
        {
            text = null;
            var (width, height) = Size(child);
            var unit = new Unit(kind, run, position)
            {
                First = child,
                Last = child,
                WidthPoints = width,
                HeightPoints = height
            };
            unit.Text.Append(shown);
            Units.Add(unit);
        }

        void Characters(OoxmlRun run, OpenXmlElement child, int position)
        {
            var value = Value(child);
            if (fields.Count > 0)
            {
                Continue(run, child);

                // A field shows its result, which follows its code.
                if (fields.Peek())
                {
                    field!.Text.Append(value);
                }

                return;
            }

            if (text == null)
            {
                text = new(EditUnitKind.Text, run, position)
                {
                    First = child,
                    Last = child
                };
                Units.Add(text);
            }

            text.Last = child;
            text.Text.Append(value);
            for (var index = 0; index < value.Length; index++)
            {
                text.Cells.Add(new(run, child, index, position + index));
            }
        }
    }

    /// <summary>The text of a run's child that takes positions, a character to a position.</summary>
    public static string Value(OpenXmlElement child) =>
        child switch
        {
            Text text => SourceRuns.EffectiveText(text.Text, text.Space?.Value),
            DeletedText text => SourceRuns.EffectiveText(text.Text, text.Space?.Value),
            SoftHyphen => "­",
            NoBreakHyphen => "‑",
            TabChar or DocumentFormat.OpenXml.Wordprocessing.PositionalTab => "\t",
            Break => "\n",
            _ => "￼"
        };

    // A drawing in the line takes the room its extent says; one that floats takes none.
    static (double Width, double Height) Size(OpenXmlElement child)
    {
        if (child.Descendants<Inline>().FirstOrDefault()?.Extent is {Cx.Value: var width, Cy.Value: var height})
        {
            return (width / 12700d, height / 12700d);
        }

        return (0, 0);
    }
}
