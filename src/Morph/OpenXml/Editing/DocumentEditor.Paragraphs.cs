using OoxmlParagraphProperties = DocumentFormat.OpenXml.Wordprocessing.ParagraphProperties;
using OoxmlRun = DocumentFormat.OpenXml.Wordprocessing.Run;
using OoxmlRunProperties = DocumentFormat.OpenXml.Wordprocessing.RunProperties;

// What a paragraph is, rather than what it says: its style, its place in a list, its indent and its
// spacing. Each is a change to the paragraph's properties, so tracked, each is recorded the way
// alignment is (w:pPrChange) and settled in the review pane like it.
static partial class DocumentEditor
{
    /// <summary>
    /// Sets paragraphs' style, as Word's style gallery does: the paragraphs' own paragraph formatting
    /// goes, and so does character formatting that covers a whole paragraph. A style the document does
    /// not define, and Word builds in, is added to it (<see cref="BuiltInStyles"/>).
    /// </summary>
    public static byte[] SetStyle(byte[] docx, IReadOnlyList<int> paragraphs, string styleId, EditOptions options) =>
        Edit(
            docx,
            options,
            edit =>
            {
                edit.Define(styleId);
                foreach (var paragraph in edit.FindAll(paragraphs))
                {
                    edit.Restyle(paragraph, styleId);
                }

                return null;
            }).Document;

    /// <summary>
    /// Puts paragraphs in a list of a kind, or takes them out of the one they are in. A list starts
    /// where none of that kind is next to the paragraphs, and otherwise they join it.
    /// </summary>
    public static byte[] SetList(byte[] docx, IReadOnlyList<int> paragraphs, ListKind kind, EditOptions options) =>
        Edit(
            docx,
            options,
            edit =>
            {
                edit.List(edit.FindAll(paragraphs), kind);
                return null;
            }).Document;

    /// <summary>
    /// Moves paragraphs in by <paramref name="steps"/> (out, when negative): a list's paragraph a level
    /// of the list, any other its left indent from one default tab stop to the next.
    /// </summary>
    public static byte[] Indent(byte[] docx, IReadOnlyList<int> paragraphs, int steps, EditOptions options) =>
        Edit(
            docx,
            options,
            edit =>
            {
                foreach (var paragraph in edit.FindAll(paragraphs))
                {
                    edit.Indent(paragraph, steps);
                }

                return null;
            }).Document;

    /// <summary>Sets the spacing of paragraphs' lines, and the space before and after them.</summary>
    public static byte[] Space(byte[] docx, IReadOnlyList<int> paragraphs, ParagraphSpacing spacing, EditOptions options) =>
        Edit(
            docx,
            options,
            edit =>
            {
                foreach (var paragraph in edit.FindAll(paragraphs))
                {
                    edit.Space(paragraph, spacing);
                }

                return null;
            }).Document;

    /// <summary>The kind of list a paragraph of a document is in.</summary>
    public static ListKind ListOf(byte[] docx, int paragraph)
    {
        using var stream = new MemoryStream(docx, writable: false);
        using var document = WordprocessingDocument.Open(stream, false);
        var editing = new Editing(document, new("", DateTimeOffset.MinValue, false));
        return editing.KindOf(editing.Find(paragraph));
    }

    const int defaultTabStop = 720;
    const int maxLevel = 8;

    sealed partial class Editing
    {
        public List<Paragraph> FindAll(IReadOnlyList<int> ordinals)
        {
            var all = main.Document!.Descendants<Paragraph>().ToList();
            var found = new List<Paragraph>();
            foreach (var ordinal in ordinals.Distinct().Order())
            {
                if (ordinal < 0 ||
                    ordinal >= all.Count)
                {
                    throw new InvalidOperationException($"The document has no paragraph {ordinal}.");
                }

                found.Add(all[ordinal]);
            }

            return found;
        }

        // Styles

        Styles StylesRoot()
        {
            var part = main.StyleDefinitionsPart ?? main.AddNewPart<StyleDefinitionsPart>();
            part.Styles ??= new();
            return part.Styles;
        }

        Style? StyleOf(string styleId) =>
            main.StyleDefinitionsPart?.Styles?.Elements<Style>().FirstOrDefault(_ => _.StyleId?.Value == styleId);

        // The style a paragraph naming none is in.
        string DefaultStyle =>
            DefaultOf(StyleValues.Paragraph) ?? "Normal";

        /// <summary>
        /// Makes sure the document defines a style, adding Word's own definition of one it builds in,
        /// and what that names in turn. A style neither defines is left to be named all the same, as
        /// Word leaves a style it cannot find.
        /// </summary>
        public void Define(string styleId)
        {
            if (StyleOf(styleId) != null ||
                BuiltInStyles.Definition(styleId) is not { } style)
            {
                return;
            }

            // Normal, Default Paragraph Font and Normal Table are what a document's styles default
            // to. One that defaults to a style of another name has its own, and a second would be a
            // second default.
            if (style.Default?.Value == true &&
                DefaultOf(style.Type?.Value) != null)
            {
                return;
            }

            StylesRoot().AppendChild(style);
            var references = new (OpenXmlElement? Element, StringValue? Name)[]
            {
                (style.BasedOn, style.BasedOn?.Val),
                (style.NextParagraphStyle, style.NextParagraphStyle?.Val),
                (style.LinkedStyle, style.LinkedStyle?.Val)
            };
            foreach (var (element, name) in references)
            {
                if (element == null ||
                    name?.Value is not { } other)
                {
                    continue;
                }

                Define(other);
                if (StyleOf(other) != null)
                {
                    continue;
                }

                if (BuiltInStyles.Definition(other) is {Default.Value: true} defaulted &&
                    DefaultOf(defaulted.Type?.Value) is { } standing)
                {
                    name.Value = standing;
                    continue;
                }

                element.Remove();
            }
        }

        // The id of the style of a type that the document's text of that type is in when it names none.
        string? DefaultOf(StyleValues? type) =>
            main.StyleDefinitionsPart?.Styles?.Elements<Style>()
                .FirstOrDefault(_ => _.Type?.Value == type && _.Default?.Value == true)?.StyleId?.Value;

        public void Restyle(Paragraph paragraph, string styleId)
        {
            var properties = Reformat(paragraph);
            foreach (var child in properties.ChildElements.ToList())
            {
                if (child is not (ParagraphMarkRunProperties or SectionProperties or ParagraphPropertiesChange))
                {
                    child.Remove();
                }
            }

            if (styleId != DefaultStyle)
            {
                properties.ParagraphStyleId = new()
                {
                    Val = styleId
                };
            }

            Unformat(paragraph);
            Unclutter(paragraph);
        }

        // Character formatting set on the whole of a paragraph's text says how the paragraph was to
        // look, which a style now says instead. Formatting set on part of it is emphasis, and stays.
        //
        // Word-probed 2026-10-18: Heading 2 applied to a paragraph bold throughout leaves it bold only
        // as Heading 2 is; Heading 1 applied to one with its first word bold leaves that word bold.
        void Unformat(Paragraph paragraph)
        {
            var runs = ParagraphContent.Runs(paragraph)
                .Where(_ => _.Deletion == null && SourceRuns.Length(_.Run) > 0)
                .Select(_ => _.Run)
                .ToList();
            if (runs.Count == 0)
            {
                return;
            }

            foreach (var child in runs[0].RunProperties?.ChildElements.ToList() ?? [])
            {
                if (child is RunStyle or RunPropertiesChange or Inserted or Deleted or MoveFrom or MoveTo)
                {
                    continue;
                }

                var xml = child.OuterXml;
                if (!runs.All(_ => _.RunProperties?.ChildElements.Any(property => property.OuterXml == xml) == true))
                {
                    continue;
                }

                foreach (var run in runs)
                {
                    var properties = Rerun(run);
                    foreach (var property in properties.ChildElements.Where(_ => _.OuterXml == xml).ToList())
                    {
                        property.Remove();
                    }

                    if (!properties.HasChildren)
                    {
                        properties.Remove();
                    }
                }
            }
        }

        // An edit that left a paragraph's properties empty leaves no trace of them.
        static void Unclutter(Paragraph paragraph)
        {
            if (paragraph.ParagraphProperties is {HasChildren: false} properties)
            {
                properties.Remove();
            }
        }

        // The style a paragraph is in, and those it is based on, nearest first.
        IEnumerable<Style> Chain(string? styleId)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var id = styleId ?? DefaultStyle;
            while (seen.Add(id) &&
                   StyleOf(id) is { } style)
            {
                yield return style;
                if (style.BasedOn?.Val?.Value is not { } basedOn)
                {
                    break;
                }

                id = basedOn;
            }
        }

        // Lists

        public void List(List<Paragraph> paragraphs, ListKind kind)
        {
            if (paragraphs.Count == 0)
            {
                return;
            }

            if (kind == ListKind.None)
            {
                foreach (var paragraph in paragraphs)
                {
                    Unlist(paragraph);
                }

                return;
            }

            var list = Neighbour(PreviousBlock(paragraphs[0]), kind) ??
                       Neighbour(NextBlock(paragraphs[^1]), kind) ??
                       NewList(kind);
            foreach (var paragraph in paragraphs)
            {
                var level = 0;
                if (Numbering(paragraph) is {Id: > 0} numbering)
                {
                    level = numbering.Level;
                }

                var properties = Reformat(paragraph);
                properties.NumberingProperties = new(
                    new NumberingLevelReference
                    {
                        Val = level
                    },
                    new NumberingId
                    {
                        Val = list
                    });

                // Word sets a list in List Paragraph, unless the paragraph is in a style of its own.
                if (properties.ParagraphStyleId?.Val?.Value is not { } styleId ||
                    styleId == DefaultStyle)
                {
                    Define("ListParagraph");
                    properties.ParagraphStyleId = new()
                    {
                        Val = "ListParagraph"
                    };
                }
            }
        }

        // Word-probed 2026-10-18: a bulleted paragraph's numbers removed, it is in Normal again — not
        // List Paragraph, which would leave it indented as though it were still listed.
        void Unlist(Paragraph paragraph)
        {
            if (Numbering(paragraph) is not {Id: > 0})
            {
                return;
            }

            var properties = Reformat(paragraph);
            properties.NumberingProperties = null;
            if (properties.ParagraphStyleId?.Val?.Value == "ListParagraph")
            {
                properties.ParagraphStyleId = null;
            }

            // A list the paragraph's style puts it in is left by saying so.
            if (Numbering(paragraph) is {Id: > 0})
            {
                properties.NumberingProperties = new(
                    new NumberingId
                    {
                        Val = 0
                    });
            }

            Unclutter(paragraph);
        }

        // The list a block next to the paragraphs is in, if it is a paragraph in a list of the kind.
        int? Neighbour(OpenXmlElement? block, ListKind kind)
        {
            if (block is Paragraph paragraph &&
                Numbering(paragraph) is {Id: > 0} numbering &&
                KindOf(paragraph) == kind)
            {
                return numbering.Id;
            }

            return null;
        }

        public ListKind KindOf(Paragraph paragraph)
        {
            if (Numbering(paragraph) is not {Id: > 0} numbering ||
                Definition(numbering.Id) is not { } definition)
            {
                return ListKind.None;
            }

            var level = definition.Elements<Level>().FirstOrDefault(_ => _.LevelIndex?.Value == numbering.Level) ??
                        definition.Elements<Level>().FirstOrDefault();
            var format = level?.NumberingFormat?.Val;
            if (format == null)
            {
                return ListKind.Number;
            }

            if (format.Value == NumberFormatValues.Bullet)
            {
                return ListKind.Bullet;
            }

            if (format.Value == NumberFormatValues.None)
            {
                return ListKind.None;
            }

            return ListKind.Number;
        }

        // The list a paragraph is in, and its level in it: its own numbering, or its style's.
        (int Id, int Level)? Numbering(Paragraph paragraph)
        {
            var own = paragraph.ParagraphProperties?.NumberingProperties;
            var id = own?.NumberingId?.Val?.Value;
            var level = own?.NumberingLevelReference?.Val?.Value;
            foreach (var style in Chain(paragraph.ParagraphProperties?.ParagraphStyleId?.Val?.Value))
            {
                var inherited = style.StyleParagraphProperties?.NumberingProperties;
                id ??= inherited?.NumberingId?.Val?.Value;
                level ??= inherited?.NumberingLevelReference?.Val?.Value;
            }

            if (id is not { } list)
            {
                return null;
            }

            return (list, level ?? 0);
        }

        AbstractNum? Definition(int numberingId)
        {
            var numbering = main.NumberingDefinitionsPart?.Numbering;
            var instance = numbering?.Elements<NumberingInstance>().FirstOrDefault(_ => _.NumberID?.Value == numberingId);
            if (instance?.AbstractNumId?.Val?.Value is not { } abstractId)
            {
                return null;
            }

            return numbering!.Elements<AbstractNum>().FirstOrDefault(_ => _.AbstractNumberId?.Value == abstractId);
        }

        int NewList(ListKind kind)
        {
            var part = main.NumberingDefinitionsPart ?? main.AddNewPart<NumberingDefinitionsPart>();
            part.Numbering ??= new();
            var numbering = part.Numbering;
            var definitions = numbering.Elements<AbstractNum>().ToList();
            var instances = numbering.Elements<NumberingInstance>().ToList();
            var abstractId = definitions.Select(_ => _.AbstractNumberId?.Value ?? 0).DefaultIfEmpty(-1).Max() + 1;
            var id = instances.Select(_ => _.NumberID?.Value ?? 0).DefaultIfEmpty(0).Max() + 1;

            // A list's nsid tells it from every other the document has; Word draws one at random.
            // Derived from its ids instead, so the same edit makes the same file.
            var taken = definitions
                .Select(_ => _.Nsid?.Val?.Value)
                .OfType<string>()
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var nsid = unchecked((int) (0x4D4F0000u + (uint) abstractId * 0x9E37u));
            while (taken.Contains(nsid.ToString("X8", CultureInfo.InvariantCulture)))
            {
                nsid++;
            }

            var definition = ListTemplates.Create(kind, abstractId, nsid);
            if (definitions.Count > 0)
            {
                definitions[^1].InsertAfterSelf(definition);
            }
            else
            {
                numbering.PrependChild(definition);
            }

            var instance = new NumberingInstance(
                new AbstractNumId
                {
                    Val = abstractId
                })
            {
                NumberID = id
            };
            if (instances.Count > 0)
            {
                instances[^1].InsertAfterSelf(instance);
            }
            else
            {
                definition.InsertAfterSelf(instance);
            }

            return id;
        }

        // Indenting

        public void Indent(Paragraph paragraph, int steps)
        {
            if (steps == 0)
            {
                return;
            }

            if (Numbering(paragraph) is {Id: > 0} numbering)
            {
                var level = Math.Clamp(numbering.Level + steps, 0, maxLevel);
                if (level == numbering.Level)
                {
                    return;
                }

                var own = paragraph.ParagraphProperties?.NumberingProperties?.NumberingId?.Val?.Value;
                var properties = Reformat(paragraph);
                properties.NumberingProperties = new(
                    new NumberingLevelReference
                    {
                        Val = level
                    });
                if (own is { } id)
                {
                    properties.NumberingProperties.AppendChild(new NumberingId
                    {
                        Val = id
                    });
                }

                return;
            }

            // From one tab stop to the next, as Word's Increase Indent goes: an indent between two
            // stops goes to the next one, not a whole stop further.
            var stop = TabStop();
            var left = Left(paragraph);
            int target;
            if (steps > 0)
            {
                target = (left / stop + steps) * stop;
            }
            else
            {
                target = Math.Max(0, ((left + stop - 1) / stop + steps) * stop);
            }

            if (target == left)
            {
                return;
            }

            var reformatted = Reformat(paragraph);
            var indentation = reformatted.Indentation ?? new Indentation();
            var value = target.ToString(CultureInfo.InvariantCulture);
            if (indentation.Start != null)
            {
                indentation.Start = value;
            }
            else
            {
                indentation.Left = value;
            }

            reformatted.Indentation = indentation;
        }

        int TabStop()
        {
            if (main.DocumentSettingsPart?.Settings?.GetFirstChild<DefaultTabStop>()?.Val?.Value is > 0 and var stop)
            {
                return stop;
            }

            return defaultTabStop;
        }

        // The left indent a paragraph has, in twips: its own, its style's, or the document's.
        int Left(Paragraph paragraph)
        {
            if (Twips(paragraph.ParagraphProperties?.Indentation) is { } own)
            {
                return own;
            }

            foreach (var style in Chain(paragraph.ParagraphProperties?.ParagraphStyleId?.Val?.Value))
            {
                if (Twips(style.StyleParagraphProperties?.Indentation) is { } styled)
                {
                    return styled;
                }
            }

            var defaults = main.StyleDefinitionsPart?.Styles?.DocDefaults?.ParagraphPropertiesDefault?.ParagraphPropertiesBaseStyle;
            return Twips(defaults?.Indentation) ?? 0;

            static int? Twips(Indentation? indentation)
            {
                var value = indentation?.Left?.Value ?? indentation?.Start?.Value;
                if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var twips))
                {
                    return twips;
                }

                return null;
            }
        }

        // Spacing

        public void Space(Paragraph paragraph, ParagraphSpacing spacing)
        {
            if (spacing == default)
            {
                return;
            }

            var properties = Reformat(paragraph);
            var lines = properties.SpacingBetweenLines ?? new SpacingBetweenLines();
            if (spacing.Line is { } line)
            {
                lines.Line = Twentieths(line * 240);
                lines.LineRule = LineSpacingRuleValues.Auto;
            }

            if (spacing.BeforePoints is { } before)
            {
                lines.Before = Twentieths(before * 20);
                lines.BeforeLines = null;
                lines.BeforeAutoSpacing = null;
            }

            if (spacing.AfterPoints is { } after)
            {
                lines.After = Twentieths(after * 20);
                lines.AfterLines = null;
                lines.AfterAutoSpacing = null;
            }

            properties.SpacingBetweenLines = lines;
        }

        static string Twentieths(double value) =>
            ((int) Math.Round(value)).ToString(CultureInfo.InvariantCulture);
    }
}
