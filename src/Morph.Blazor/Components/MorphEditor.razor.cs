namespace Morph;

/// <summary>
/// A word processor for Word documents, in the browser: a File menu and a ribbon over the pages, as in
/// Word. The text is typed where it stands on the page; the ribbon sets a paragraph's style, list,
/// indent, alignment and spacing, the text's bold, italic, underline and strikethrough, puts in page
/// breaks, tables, pictures and links, adds and deletes a table's rows and columns, finds and replaces,
/// tracks changes, and takes comments. File starts a new document, opens one, saves a copy, exports
/// it as PDF, HTML, Markdown, plain text or PNG, and prints it.
///
/// Built on <see cref="MorphViewer"/>, whose parameters it takes: every edit is made to the file and the
/// file laid out again, so the pages always show exactly what Save saves. An Excel workbook or a
/// PowerPoint deck opens to be read; only a Word document is edited.
/// </summary>
public partial class MorphEditor
{
    const string fileMenu = "file";
    const string stylesMenu = "styles";
    const string spacingMenu = "spacing";
    const string tableMenu = "table";
    const string fontMenu = "font";
    const string sizeMenu = "size";
    const string colorMenu = "color";
    const string highlightMenu = "highlight";

    const string automatic = "auto";
    const string noHighlight = "none";

    const string home = "Home";
    const string insert = "Insert";
    const string table = "Table";
    const string reviewTab = "Review";
    const string view = "View";

    const string linkDialog = "link";
    const string countDialog = "count";
    const string discardDialog = "discard";

    const int gridRows = 8;
    const int gridColumns = 10;

    string tab = home;
    string? menu;
    string? dialog;
    string linkAddress = "";
    ElementReference linkInput;
    bool focusLink;
    (int Rows, int Columns) hover = (1, 1);

    internal override bool Editing => true;

    string EditorClass
    {
        get
        {
            if (Class is {Length: > 0} extra)
            {
                return $"viewer editor {extra}";
            }

            return "viewer editor";
        }
    }

    // The Table tab is there while the caret is in a table, as Word's Table Layout tab is.
    IEnumerable<string> Tabs
    {
        get
        {
            yield return home;
            yield return insert;
            if (InTable && IsWord)
            {
                yield return table;
            }

            yield return reviewTab;
            yield return view;
        }
    }

    string WordsLabel
    {
        get
        {
            var words = Count().Words;
            if (CountSelected() is { } selected)
            {
                return $"{Number(selected.Words)} of {Number(words)} words";
            }

            return $"{Number(words)} {(words == 1 ? "word" : "words")}";
        }
    }

    string GridLabel =>
        $"{hover.Rows} × {hover.Columns} table";

    // A Table tab chosen and then left behind by the caret leaving the table is Home again.
    string ShownTab
    {
        get
        {
            if (tab == table &&
                !InTable)
            {
                return home;
            }

            return tab;
        }
    }

    static string Number(int value) =>
        value.ToString("N0", CultureInfo.InvariantCulture);

    static string Pressed(bool on)
    {
        if (on)
        {
            return "true";
        }

        return "false";
    }

    string Expanded(string name) =>
        Pressed(menu == name);

    // A style's name drawn the way its text is, as Word's gallery draws it, at a size the menu holds.
    static string StylePreview(StyleChoice style)
    {
        var size = Math.Clamp(style.SizePoints, 9, 20).ToString("0.#", CultureInfo.InvariantCulture);
        var css = $"font-size: {size}pt;";
        if (style.Bold)
        {
            css += " font-weight: 700;";
        }

        if (style.Italic)
        {
            css += " font-style: italic;";
        }

        if (style.ColorHex is { } color)
        {
            css += $" color: #{color};";
        }

        return css;
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        await base.OnAfterRenderAsync(firstRender);
        if (firstRender)
        {
            await FindDraftAsync();
        }

        if (focusLink &&
            dialog == linkDialog)
        {
            focusLink = false;
            await linkInput.FocusAsync();
        }
    }

    void ToggleMenu(string name)
    {
        menu = menu == name ? null : name;
        hover = (1, 1);
    }

    void CloseMenu() =>
        menu = null;

    void OnMenuKeyDown(KeyboardEventArgs args)
    {
        if (args.Key == "Escape")
        {
            menu = null;
        }
    }

    void CloseDialog()
    {
        dialog = null;
        linkTarget = null;
    }

    void OnDialogKeyDown(KeyboardEventArgs args)
    {
        if (args.Key == "Escape")
        {
            CloseDialog();
        }
    }

    // Menus close before what was chosen runs, so the pages are what the reader sees while it does.

    Task StyleFromMenuAsync(string styleId)
    {
        menu = null;
        return ApplyStyleAsync(styleId);
    }

    Task SpacingFromMenuAsync(ParagraphSpacing spacing)
    {
        menu = null;
        return SpaceAsync(spacing);
    }

    Task SpaceBeforeFromMenuAsync()
    {
        menu = null;
        return ToggleSpaceBeforeAsync();
    }

    Task SpaceAfterFromMenuAsync()
    {
        menu = null;
        return ToggleSpaceAfterAsync();
    }

    Task TableFromMenuAsync(int rows, int columns)
    {
        menu = null;
        return InsertTableAsync(rows, columns);
    }

    Task FontFromMenuAsync(string font)
    {
        menu = null;
        return SetFontAsync(font);
    }

    Task SizeFromMenuAsync(double size)
    {
        menu = null;
        return SetSizeAsync(size);
    }

    Task ColorFromMenuAsync(string color)
    {
        menu = null;
        return SetColorAsync(color);
    }

    Task HighlightFromMenuAsync(string highlight)
    {
        menu = null;
        return SetHighlightAsync(highlight);
    }

    static string SizeLabel(double size) =>
        size.ToString("0.#", CultureInfo.InvariantCulture);

    static string HighlightHex(string highlight)
    {
        foreach (var (value, hex, _) in Highlights)
        {
            if (value == highlight)
            {
                return hex;
            }
        }

        return "FFFF00";
    }

    Task SaveFromMenuAsync()
    {
        menu = null;
        return DownloadAsync();
    }

    Task ExportFromMenuAsync(OutputFormat format)
    {
        menu = null;
        return ExportAsync(format);
    }

    Task PrintFromMenuAsync()
    {
        menu = null;
        return PrintAsync();
    }

    Task OpenFromMenuAsync(InputFileChangeEventArgs args)
    {
        menu = null;
        return OnFileSelectedAsync(args);
    }

    // A new document

    Task NewAsync()
    {
        menu = null;
        if (Dirty)
        {
            dialog = discardDialog;
            return Task.CompletedTask;
        }

        return NewDocumentAsync();
    }

    async Task SaveThenNewAsync()
    {
        dialog = null;
        await DownloadAsync();
        await NewDocumentAsync();
    }

    async Task DiscardThenNewAsync()
    {
        dialog = null;
        await DropDraftAsync();
        await NewDocumentAsync();
    }

    // Links

    internal override Task KeyAsync(string name)
    {
        if (name == "link")
        {
            return OpenLinkAsync();
        }

        return base.KeyAsync(name);
    }

    async Task OpenLinkAsync()
    {
        if (!await BeginLinkAsync())
        {
            return;
        }

        linkAddress = "https://";
        dialog = linkDialog;
        focusLink = true;
    }

    void OnLinkInput(ChangeEventArgs args) =>
        linkAddress = args.Value as string ?? "";

    Task InsertLinkAsync()
    {
        dialog = null;
        return LinkAsync(linkAddress.Trim());
    }

    Task RemoveLinkAsync()
    {
        dialog = null;
        return UnlinkAsync();
    }

    async Task OnLinkKeyDownAsync(KeyboardEventArgs args)
    {
        if (args.Key == "Escape")
        {
            CloseDialog();
            return;
        }

        if (args.Key == "Enter" &&
            linkTarget is {HasSelection: true} &&
            linkAddress.Trim().Length > 0)
        {
            await InsertLinkAsync();
        }
    }
}
