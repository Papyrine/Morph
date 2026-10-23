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
    const string fitMenu = "fit";
    const string marginsMenu = "margins";
    const string orientationMenu = "orientation";
    const string sizeOfPageMenu = "paper";
    const string breaksMenu = "breaks";
    const string bordersMenu = "borders";
    const string shadingMenu = "shading";

    const string automatic = "auto";
    const string noHighlight = "none";

    const string home = "Home";
    const string insert = "Insert";
    const string layout = "Layout";
    const string references = "References";
    const string table = "Table";
    const string reviewTab = "Review";
    const string view = "View";

    const string linkDialog = "link";
    const string countDialog = "count";
    const string discardDialog = "discard";
    const string splitDialog = "split";
    const string marginsDialog = "margins";
    const string bookmarkDialog = "bookmark";

    const int galleryRows = 2;
    const int galleryColumns = 4;

    const int gridRows = 8;
    const int gridColumns = 10;

    string tab = home;
    string? menu;
    string? dialog;
    string linkAddress = "";
    ElementReference linkInput;
    bool focusLink;
    (int Rows, int Columns) hover = (1, 1);
    int galleryStart;
    string? galleryStyle;

    /// <summary>
    /// Whether the File menu offers a new blank document. Default true; a host that opened one file for
    /// editing, as <see cref="MorphWopi"/> does, turns it off.
    /// </summary>
    [Parameter]
    public bool ShowNew { get; set; } = true;

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
            if (IsWord)
            {
                yield return layout;
                yield return references;
            }

            yield return reviewTab;
            yield return view;
            // A contextual tab, after the others, as Word's Table Layout is.
            if (InTable && IsWord)
            {
                yield return table;
            }
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

    // The style gallery: the styles a document puts in it (and the caret's, wherever it is from), two
    // rows at a time. It turns to the row the caret's style is on whenever that style changes, and is
    // otherwise left where the reader paged it.
    IReadOnlyList<StyleChoice> GalleryChoices
    {
        get
        {
            var choices = Styles.Where(_ => _.Gallery).ToList();
            if (choices.Count == 0)
            {
                choices = [.. Styles];
            }

            if (choices.All(_ => _.Id != CurrentStyle) &&
                Styles.FirstOrDefault(_ => _.Id == CurrentStyle) is { } current)
            {
                choices.Insert(0, current);
            }

            return choices;
        }
    }

    IReadOnlyList<StyleChoice> GalleryShown
    {
        get
        {
            var choices = GalleryChoices;
            if (galleryStyle != CurrentStyle)
            {
                galleryStyle = CurrentStyle;
                var index = choices.ToList().FindIndex(_ => _.Id == CurrentStyle);
                if (index >= 0 &&
                    (index < galleryStart || index >= galleryStart + galleryRows * galleryColumns))
                {
                    galleryStart = index / galleryColumns * galleryColumns;
                }
            }

            return [.. choices.Skip(galleryStart).Take(galleryRows * galleryColumns)];
        }
    }

    bool CanPageGallery(int rows)
    {
        if (rows < 0)
        {
            return galleryStart > 0;
        }

        return galleryStart + galleryRows * galleryColumns < GalleryChoices.Count;
    }

    void PageGallery(int rows) =>
        galleryStart = Math.Max(0, galleryStart + rows * galleryColumns);

    // A style in the gallery: drawn as its text is, at a size a tile holds.
    static string GalleryPreview(StyleChoice style) =>
        StylePreview(style, 13);

    // A style's name drawn the way its text is, as Word's gallery draws it, at a size the menu holds.
    static string StylePreview(StyleChoice style, double largest = 20)
    {
        var size = Math.Clamp(style.SizePoints, 9, largest).ToString("0.#", CultureInfo.InvariantCulture);
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
        await SyncRulerAsync();
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

    // Layout

    static readonly string[] marginSides = ["Top", "Bottom", "Left", "Right"];

    readonly Dictionary<string, string> customMargins = new();

    // Twips as centimetres, to a tenth.
    static string Centimetres(int twips) =>
        (twips * 2.54 / 1440).ToString("0.##", CultureInfo.InvariantCulture) + " cm";

    Task MarginsFromMenuAsync((int Top, int Right, int Bottom, int Left) margins)
    {
        menu = null;
        return SetMarginsAsync(margins.Top, margins.Right, margins.Bottom, margins.Left);
    }

    Task OrientationFromMenuAsync(bool landscape)
    {
        menu = null;
        return SetOrientationAsync(landscape);
    }

    Task PaperFromMenuAsync((int Width, int Height) paper)
    {
        menu = null;
        return SetPaperSizeAsync(paper.Width, paper.Height);
    }

    Task PageBreakFromMenuAsync()
    {
        menu = null;
        return InsertPageBreakAsync();
    }

    Task SectionBreakFromMenuAsync(bool continuous)
    {
        menu = null;
        return InsertSectionBreakAsync(continuous);
    }

    void OpenMargins()
    {
        menu = null;
        var current = CurrentPage ?? new PageSetup(11906, 16838, 1440, 1440, 1440, 1440, false);
        customMargins["Top"] = Number(current.Top);
        customMargins["Bottom"] = Number(current.Bottom);
        customMargins["Left"] = Number(current.Left);
        customMargins["Right"] = Number(current.Right);
        dialog = marginsDialog;

        static string Number(int twips) =>
            (twips * 2.54 / 1440).ToString("0.##", CultureInfo.InvariantCulture);
    }

    // The margins typed, in twips; null while one of them is not a size.
    (int Top, int Right, int Bottom, int Left)? CustomMargins()
    {
        var twips = new Dictionary<string, int>();
        foreach (var side in marginSides)
        {
            if (!double.TryParse(customMargins.GetValueOrDefault(side), NumberStyles.Float, CultureInfo.InvariantCulture, out var centimetres) ||
                centimetres is < 0 or > 50)
            {
                return null;
            }

            twips[side] = (int) Math.Round(centimetres * 1440 / 2.54);
        }

        return (twips["Top"], twips["Right"], twips["Bottom"], twips["Left"]);
    }

    Task MarginsFromDialogAsync()
    {
        if (CustomMargins() is not { } margins)
        {
            return Task.CompletedTask;
        }

        dialog = null;
        return SetMarginsAsync(margins.Top, margins.Right, margins.Bottom, margins.Left);
    }

    async Task OnMarginsKeyDownAsync(KeyboardEventArgs args)
    {
        if (args.Key == "Enter")
        {
            await MarginsFromDialogAsync();
            return;
        }

        OnDialogKeyDown(args);
    }

    // Tables

    // The lighter shades of Word's theme colours, which is what cells are mostly shaded in, and its
    // standard colours.
    internal static readonly (string Hex, string Name)[] ShadingColors =
    [
        ("F2F2F2", "White, darker 5%"), ("D9D9D9", "White, darker 15%"), ("BFBFBF", "White, darker 25%"), ("DAE9F8", "Blue, lighter 80%"), ("C1F0C8", "Green, lighter 80%"),
        ("FBE2D5", "Orange, lighter 80%"), ("F2CEEF", "Purple, lighter 80%"), ("FFFF00", "Yellow"), ("92D050", "Light green"), ("00B0F0", "Light blue"),
        ("FFC000", "Orange"), ("FF0000", "Red"), ("0070C0", "Blue"), ("7030A0", "Purple"), ("000000", "Black")
    ];

    string columnWidth = "";
    int splitColumns = 2;
    int splitRows = 1;

    Task FitFromMenuAsync(TableFit fit)
    {
        menu = null;
        return FitTableAsync(fit);
    }

    Task BordersFromMenuAsync(BorderPreset preset)
    {
        menu = null;
        return SetTableBordersAsync(preset);
    }

    Task ShadingFromMenuAsync(string? fill)
    {
        menu = null;
        return ShadeCellsAsync(fill);
    }

    Task OnColumnWidthAsync(ChangeEventArgs args)
    {
        columnWidth = args.Value?.ToString() ?? "";
        if (!double.TryParse(columnWidth, NumberStyles.Float, CultureInfo.InvariantCulture, out var centimetres))
        {
            return Task.CompletedTask;
        }

        return SetColumnWidthAsync(centimetres);
    }

    Task OpenSplitAsync()
    {
        menu = null;
        splitColumns = 2;
        splitRows = 1;
        dialog = splitDialog;
        return Task.CompletedTask;
    }

    static int Count(ChangeEventArgs args, int fallback)
    {
        if (int.TryParse(args.Value?.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var count))
        {
            return Math.Clamp(count, 1, 63);
        }

        return fallback;
    }

    Task SplitFromDialogAsync()
    {
        dialog = null;
        return SplitCellAsync(splitColumns, splitRows);
    }

    async Task OnSplitKeyDownAsync(KeyboardEventArgs args)
    {
        if (args.Key == "Enter" &&
            splitColumns * splitRows > 1)
        {
            await SplitFromDialogAsync();
            return;
        }

        OnDialogKeyDown(args);
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

    Task SaveInPlaceFromMenuAsync()
    {
        menu = null;
        return SaveAsync();
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
        await SaveAsync();
        if (Dirty)
        {
            return;
        }

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
        // The address is asked for only of a selection: in a link with none, the dialog only removes it.
        focusLink = linkTarget is {HasSelection: true};
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

    // Word's Bookmark dialog: a name for the caret's place or the text selected; one of the document's
    // chosen from the list is deleted or gone to.
    string bookmarkName = "";
    EditTarget? bookmarkTarget;

    async Task OpenBookmarkAsync()
    {
        if (await RequireAsync() is not { } target)
        {
            return;
        }

        bookmarkTarget = target;
        bookmarkName = "";
        dialog = bookmarkDialog;
    }

    bool BookmarkExists =>
        Bookmarks.Any(_ => _.Name == bookmarkName);

    void OnBookmarkInput(ChangeEventArgs args) =>
        bookmarkName = (args.Value as string ?? "").Trim();

    async Task AddBookmarkFromDialogAsync()
    {
        dialog = null;
        if (bookmarkTarget is { } target &&
            DocumentEditor.IsBookmarkName(bookmarkName))
        {
            await AddBookmarkAsync(target, bookmarkName);
        }
    }

    async Task DeleteBookmarkFromDialogAsync()
    {
        if (bookmarkTarget is { } target &&
            BookmarkExists)
        {
            await DeleteBookmarkAsync(target, bookmarkName);
            bookmarkName = "";
        }
    }

    Task GoToBookmarkFromDialogAsync()
    {
        dialog = null;
        return GoToBookmarkAsync(bookmarkName);
    }

    Task OnBookmarkKeyDownAsync(KeyboardEventArgs args)
    {
        if (args.Key == "Escape")
        {
            CloseDialog();
            return Task.CompletedTask;
        }

        if (args.Key == "Enter" &&
            DocumentEditor.IsBookmarkName(bookmarkName))
        {
            return AddBookmarkFromDialogAsync();
        }

        return Task.CompletedTask;
    }

    Task OnLinkKeyDownAsync(KeyboardEventArgs args)
    {
        if (args.Key == "Escape")
        {
            CloseDialog();
            return Task.CompletedTask;
        }

        if (args.Key == "Enter" &&
            linkTarget is {HasSelection: true} &&
            linkAddress.Trim().Length > 0)
        {
            return InsertLinkAsync();
        }

        return Task.CompletedTask;
    }
}
