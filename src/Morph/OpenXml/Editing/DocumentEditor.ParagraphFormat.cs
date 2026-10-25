using OoxmlParagraphProperties = DocumentFormat.OpenXml.Wordprocessing.ParagraphProperties;

// Word's Paragraph dialog: alignment, indents, spacing, and how a paragraph breaks across pages.
// Word-probed 2026-10-21 (Word 365 over COM, ParagraphFormat set on one paragraph):
//
//   <w:keepNext/><w:keepLines/><w:pageBreakBefore/><w:widowControl w:val="0"/>
//   <w:spacing w:before="120" w:after="240" w:line="400" w:lineRule="exact"/>
//   <w:ind w:left="720" w:right="360" w:hanging="360"/>
//
// in twentieths of a point. A switch is written where it differs from what the paragraph's style
// gives — widow control, on in Word unless a style says otherwise, is written only to turn it off.
static partial class DocumentEditor
{
    /// <summary>Sets what Word's Paragraph dialog sets on paragraphs.</summary>
    public static byte[] FormatParagraphs(byte[] docx, IReadOnlyList<int> paragraphs, ParagraphFormat format, EditOptions options) =>
        Edit(
            docx,
            options,
            edit =>
            {
                foreach (var paragraph in edit.FindAll(paragraphs))
                {
                    edit.FormatParagraph(paragraph, format);
                }

                return null;
            }).Document;

    sealed partial class Editing
    {
        public void FormatParagraph(Paragraph paragraph, ParagraphFormat format)
        {
            if (format == default)
            {
                return;
            }

            if (format.Alignment is { } alignment)
            {
                Align(paragraph, alignment);
            }

            var properties = Reformat(paragraph);
            Indent(properties, format);
            Space(properties, format);
            Switch<KeepNext>(paragraph, properties, format.KeepNext, false, (_, value) => _.KeepNext = value);
            Switch<KeepLines>(paragraph, properties, format.KeepLines, false, (_, value) => _.KeepLines = value);
            Switch<PageBreakBefore>(paragraph, properties, format.PageBreakBefore, false, (_, value) => _.PageBreakBefore = value);
            Switch<WidowControl>(paragraph, properties, format.WidowControl, true, (_, value) => _.WidowControl = value);
        }

        static void Indent(OoxmlParagraphProperties properties, ParagraphFormat format)
        {
            if (format is { LeftPoints: null, RightPoints: null, FirstLinePoints: null })
            {
                return;
            }

            var indentation = properties.Indentation ?? new Indentation();
            if (format.LeftPoints is { } left)
            {
                indentation.Left = Twips(left);
                indentation.Start = null;
                indentation.LeftChars = null;
                indentation.StartCharacters = null;
            }

            if (format.RightPoints is { } right)
            {
                indentation.Right = Twips(right);
                indentation.End = null;
                indentation.RightChars = null;
                indentation.EndCharacters = null;
            }

            if (format.FirstLinePoints is { } first)
            {
                indentation.FirstLineChars = null;
                indentation.HangingChars = null;
                if (first < 0)
                {
                    indentation.FirstLine = null;
                    indentation.Hanging = Twips(-first);
                }
                else
                {
                    indentation.Hanging = null;
                    indentation.FirstLine = Twips(first);
                }
            }

            properties.Indentation = indentation;
        }

        static void Space(OoxmlParagraphProperties properties, ParagraphFormat format)
        {
            if (format is { BeforePoints: null, AfterPoints: null, LineRule: null })
            {
                return;
            }

            var lines = properties.SpacingBetweenLines ?? new SpacingBetweenLines();
            if (format.BeforePoints is { } before)
            {
                lines.Before = Twips(before);
                lines.BeforeLines = null;
                lines.BeforeAutoSpacing = null;
            }

            if (format.AfterPoints is { } after)
            {
                lines.After = Twips(after);
                lines.AfterLines = null;
                lines.AfterAutoSpacing = null;
            }

            if (format is { LineRule: { } rule, LineValue: { } value })
            {
                if (rule == LineSpacingRule.Auto)
                {
                    lines.Line = ((int)Math.Round(value * 240)).ToString(CultureInfo.InvariantCulture);
                    lines.LineRule = LineSpacingRuleValues.Auto;
                }
                else
                {
                    lines.Line = Twips(value);
                    lines.LineRule = rule == LineSpacingRule.Exactly ? LineSpacingRuleValues.Exact : LineSpacingRuleValues.AtLeast;
                }
            }

            properties.SpacingBetweenLines = lines;
        }

        static string Twips(double points) =>
            ((int)Math.Round(points * 20)).ToString(CultureInfo.InvariantCulture);

        // A switch the paragraph says only where its style says otherwise.
        void Switch<T>(Paragraph paragraph, OoxmlParagraphProperties properties, bool? wanted, bool unsaid, Action<OoxmlParagraphProperties, T?> write)
            where T : OnOffType, new()
        {
            if (wanted is not { } on)
            {
                return;
            }

            write(properties, null);
            if (StyleSays<T>(paragraph) ?? unsaid)
            {
                // Off as Word spells it: w:val="0".
                if (!on)
                {
                    write(
                        properties,
                        new()
                        {
                            Val = Literal<OnOffValue>("0")
                        });
                }

                return;
            }

            if (on)
            {
                write(properties, Toggle<T>(true));
            }
        }

        // What the paragraph's style, through the styles it is based on and then the document's
        // defaults, says of a switch; null where none says.
        bool? StyleSays<T>(Paragraph paragraph)
            where T : OnOffType
        {
            var styles = main.StyleDefinitionsPart?.Styles;
            var id = paragraph.ParagraphProperties?.ParagraphStyleId?.Val?.Value ??
                     styles?.Elements<Style>()
                         .FirstOrDefault(_ => _.Type?.Value == StyleValues.Paragraph &&
                                              _.Default?.Value == true)?.StyleId?.Value;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            while (id != null &&
                   seen.Add(id) &&
                   styles?.Elements<Style>()
                       .FirstOrDefault(_ => _.StyleId?.Value == id) is { } style)
            {
                if (style.StyleParagraphProperties is { } own &&
                    own.Elements<T>().FirstOrDefault() is { } said)
                {
                    return said.Val?.Value ?? true;
                }

                id = style.BasedOn?.Val?.Value;
            }

            var defaults = styles?.DocDefaults?
                .ParagraphPropertiesDefault?
                .ParagraphPropertiesBaseStyle;
            if (defaults?.Elements<T>().FirstOrDefault() is { } given)
            {
                return given.Val?.Value ?? true;
            }

            return null;
        }
    }
}
