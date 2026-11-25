/// <summary>
/// Keeps a text box's two copies of its text as one. Word writes a text box twice: the
/// <c>wps:txbx</c> in <c>mc:Choice</c> that Word 2010 and later read, and the VML <c>v:textbox</c> in
/// <c>mc:Fallback</c> for readers older than that, the same paragraphs with the same ids
/// (<see cref="ParagraphIds"/>). An edit writes the Choice's, which is the one laid out, edited and
/// opened again; once it is done the Fallback's is made a copy of it, as Word writes the copy again
/// whenever it saves. Only the parts the edit loaded are looked at, so a part the edit never touched is
/// not written out again.
/// </summary>
static class TextBoxFallbacks
{
    public static void Follow(WordprocessingDocument document)
    {
        if (document.MainDocumentPart is not { } main)
        {
            return;
        }

        IEnumerable<OpenXmlPart> parts =
        [
            main,
            .. main.HeaderParts,
            .. main.FooterParts,
            .. new OpenXmlPart?[] {main.FootnotesPart, main.EndnotesPart}.OfType<OpenXmlPart>()
        ];
        foreach (var part in parts)
        {
            if (part is
                {
                    IsRootElementLoaded: true,
                    RootElement: { } root
                })
            {
                Follow(root);
            }
        }
    }

    static void Follow(OpenXmlElement root)
    {
        foreach (var alternate in root.Descendants<AlternateContent>().ToList())
        {
            // A box inside another's text is followed with the outer one's copy.
            if (alternate.Ancestors<AlternateContentFallback>().Any())
            {
                continue;
            }

            var choice = alternate.GetFirstChild<AlternateContentChoice>()?.Descendants<TextBoxContent>().FirstOrDefault();
            var fallback = alternate.GetFirstChild<AlternateContentFallback>()?.Descendants<TextBoxContent>().FirstOrDefault();
            if (choice == null ||
                fallback == null ||
                string.Equals(choice.InnerXml, fallback.InnerXml, StringComparison.Ordinal))
            {
                continue;
            }

            fallback.RemoveAllChildren();
            foreach (var child in choice.ChildElements)
            {
                fallback.AppendChild(child.CloneNode(true));
            }
        }
    }
}
