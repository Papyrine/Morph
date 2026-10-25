// The ribbon's state where a case left the selection: which of the controls Morph's editor has a command
// for are greyed out, and which toggles are pressed. Read through
// CommandBars.GetEnabledMso / GetPressedMso, which answer for the ribbon as drawn.
// Word updates the ribbon when it goes idle, so the read waits for that first. GetVisibleMso is not read:
// for a contextual tab (TabTableToolsLayout) it answered true wherever the caret was (2026-10-25), so it
// says whether the tab is customised away, not whether it shows. Galleries (Bullets, Underline) and
// split buttons do not answer GetPressedMso at all, so only true toggles can read as pressed.
static class RibbonReader
{
    /// <summary>idMso → the name of Morph's EditorCommand (MorphEditor.Commands.cs) it stands for.</summary>
    public static IReadOnlyList<(string IdMso, string Command)> Controls { get; } =
    [
        ("Undo", "undo"),
        ("Redo", "redo"),
        ("Paste", "paste"),
        ("Cut", "cut"),
        ("Copy", "copy"),
        ("FormatPainter", "format-painter"),
        ("Font", "font"),
        ("FontSize", "font-size"),
        ("FontSizeIncreaseWord", "grow"),
        ("FontSizeDecreaseWord", "shrink"),
        ("ClearFormatting", "clear-formatting"),
        ("Bold", "bold"),
        ("Italic", "italic"),
        ("UnderlineGallery", "underline"),
        ("Strikethrough", "strike"),
        ("Subscript", "subscript"),
        ("Superscript", "superscript"),
        ("TextHighlightColorPicker", "highlight"),
        ("FontColorPicker", "font-colour"),
        ("FontDialog", "font-dialog"),
        ("BulletsGalleryWord", "bullets"),
        ("NumberingGalleryWord", "numbering"),
        ("IndentDecreaseWord", "decrease-indent"),
        ("IndentIncreaseWord", "increase-indent"),
        ("ParagraphMarks", "marks"),
        ("AlignLeft", "align-left"),
        ("AlignCenter", "align-center"),
        ("AlignRight", "align-right"),
        ("AlignJustify", "align-justify"),
        ("LineSpacingGallery", "spacing"),
        ("ShadingColorPicker", "paragraph-shading"),
        ("BordersSelectionGallery", "paragraph-borders"),
        ("ParagraphDialog", "paragraph-dialog"),
        ("QuickStylesGallery", "styles"),
        ("FindDialog", "find"),
        ("ReplaceDialog", "replace"),
        ("PageBreakInsertWord", "page-break"),
        ("TableInsertGallery", "table"),
        ("PictureInsertFromFile", "picture"),
        ("HyperlinkInsert", "link"),
        ("BookmarkInsert", "bookmark"),
        ("HeaderInsertGallery", "header"),
        ("FooterInsertGallery", "footer"),
        ("HeaderFooterPageNumberInsert", "page-number"),
        ("DateAndTimeInsert", "date"),
        ("PageMarginsGallery", "margins"),
        ("PageOrientationGallery", "orientation"),
        ("PageSizeGallery", "paper-size"),
        ("TableColumnsGallery", "columns"),
        ("BreaksGallery", "breaks"),
        ("TextWrapGallery", "wrap-text"),
        ("TableOfContentsGallery", "contents"),
        ("TableOfContentsUpdate", "update-contents"),
        ("FootnoteInsert", "footnote"),
        ("EndnoteInsertWord", "endnote"),
        ("TableRowsInsertAboveWord", "insert-row-above"),
        ("TableRowsInsertBelowWord", "insert-row-below"),
        ("TableColumnsInsertLeft", "insert-column-left"),
        ("TableColumnsInsertRight", "insert-column-right"),
        ("TableRowsDelete", "delete-row"),
        ("TableColumnsDelete", "delete-column"),
        ("TableDelete", "delete-table"),
        ("MergeCells", "merge-cells"),
        ("SplitCells", "split-cells"),
        ("TableColumnsDistribute", "distribute-columns"),
        ("TableAutoFitMenu", "autofit"),
        ("WordCount", "word-count"),
        ("ReviewNewComment", "new-comment"),
        ("ReviewReviewingPane", "review-pane"),
        ("ReviewTrackChanges", "track"),
        ("ReviewAcceptAllChangesInDocument", "accept-all"),
        ("ReviewRejectAllChangesInDocument", "reject-all"),
        ("Thumbnails", "pages"),
        ("ViewRulerWord", "ruler")
    ];

    public static RibbonState Read(Word.Application application)
    {
        Thread.Sleep(400);
        dynamic bars = ((dynamic) application).CommandBars;
        var disabled = new List<string>();
        var pressed = new List<string>();
        var unreadable = new List<string>();
        foreach (var (id, _) in Controls)
        {
            try
            {
                bool enabled = bars.GetEnabledMso(id);
                if (!enabled)
                {
                    disabled.Add(id);
                }
            }
            catch (Exception)
            {
                unreadable.Add(id);
                continue;
            }

            // Only toggles answer GetPressedMso; every other kind throws, which is not worth recording.
            try
            {
                bool down = bars.GetPressedMso(id);
                if (down)
                {
                    pressed.Add(id);
                }
            }
            catch (Exception)
            {
            }
        }

        return new(disabled, pressed, unreadable);
    }

    public static string Summary(RibbonState ribbon)
    {
        var parts = new List<string>
        {
            ribbon.Disabled.Count == 0 ? "greyed: none" : $"greyed: {string.Join(", ", ribbon.Disabled)}",
            ribbon.Pressed.Count == 0 ? "pressed: none" : $"pressed: {string.Join(", ", ribbon.Pressed)}"
        };
        if (ribbon.Unreadable.Count > 0)
        {
            parts.Add($"unreadable: {string.Join(", ", ribbon.Unreadable)}");
        }

        return "Ribbon " + string.Join(" · ", parts);
    }
}
