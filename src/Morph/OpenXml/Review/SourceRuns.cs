using OoxmlRun = DocumentFormat.OpenXml.Wordprocessing.Run;

/// <summary>
/// The coordinate system a <see cref="RunSource"/> is written in, shared by the two sides that must agree
/// on it: the parser, which stamps each model run with where its text came from, and the review code,
/// which reads comment and revision anchors out of the markup and splits a <c>w:r</c> where a new comment
/// begins.
///
/// A run's ordinal is its index among the part's <c>w:r</c> elements in document order. A position
/// within a run counts only what the parser turns into text, in child order: each character of a
/// <c>w:t</c> or <c>w:delText</c> (after the whitespace the parser trims), and one each for a tab, a soft
/// or no-break hyphen and a note reference. Everything else in a run — its properties, a break, a
/// drawing, field plumbing — takes no position, so the count does not depend on where the run sits.
/// </summary>
static class SourceRuns
{
    /// <summary>Every <c>w:r</c> under <paramref name="root"/>, numbered in document order.</summary>
    public static Dictionary<OoxmlRun, int> Index(OpenXmlElement root)
    {
        var ordinals = new Dictionary<OoxmlRun, int>(ReferenceEqualityComparer.Instance);
        foreach (var run in root.Descendants<OoxmlRun>())
        {
            ordinals[run] = ordinals.Count;
        }

        return ordinals;
    }

    /// <summary>How many positions a run's child takes.</summary>
    public static int Length(OpenXmlElement child) =>
        child switch
        {
            Text text => EffectiveText(text.Text, text.Space?.Value).Length,
            DeletedText text => EffectiveText(text.Text, text.Space?.Value).Length,
            SoftHyphen or NoBreakHyphen or TabChar or DocumentFormat.OpenXml.Wordprocessing.PositionalTab => 1,
            FootnoteReference {Id: not null} => 1,
            EndnoteReference {Id: not null} => 1,
            _ => 0
        };

    /// <summary>
    /// A run's text as the parser reads it, a character per position — a tab and a note reference each
    /// stand as one character, so an offset into this string is a position in the run.
    /// </summary>
    public static string Text(OoxmlRun run)
    {
        var builder = new StringBuilder();
        foreach (var child in run.ChildElements)
        {
            switch (child)
            {
                case Text text:
                    builder.Append(EffectiveText(text.Text, text.Space?.Value));
                    break;
                case DeletedText text:
                    builder.Append(EffectiveText(text.Text, text.Space?.Value));
                    break;
                case SoftHyphen:
                    builder.Append('­');
                    break;
                case NoBreakHyphen:
                    builder.Append('‑');
                    break;
                case TabChar or DocumentFormat.OpenXml.Wordprocessing.PositionalTab:
                    builder.Append('\t');
                    break;
                case FootnoteReference {Id: not null} or EndnoteReference {Id: not null}:
                    builder.Append('￼');
                    break;
            }
        }

        return builder.ToString();
    }

    /// <summary>How many positions a run takes.</summary>
    public static int Length(OoxmlRun run)
    {
        var length = 0;
        foreach (var child in run.ChildElements)
        {
            length += Length(child);
        }

        return length;
    }

    /// <summary>
    /// ECMA-376 whitespace handling: a <c>w:t</c> (or <c>w:delText</c>) without
    /// <c>xml:space="preserve"</c> sheds its XML edge whitespace — space, tab, CR, LF, but NOT the
    /// no-break space, which is content. document_capture/01 authors "Footnote ref " unpreserved and Word
    /// sets the reference mark flush after "ref". Word's own writer stamps preserve wherever an edge
    /// space is real, so for Word-authored packages this is a no-op; only hand-authored XML hits it.
    /// </summary>
    public static string EffectiveText(string text, SpaceProcessingModeValues? space)
    {
        if (space == SpaceProcessingModeValues.Preserve)
        {
            return text;
        }

        return text.Trim(' ', '\t', '\r', '\n');
    }
}
