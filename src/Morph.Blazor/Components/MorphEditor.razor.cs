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
    const string columnsMenu = "columns";
    const string bulletsMenu = "bullets";
    const string numberingMenu = "numbering";
    const string paragraphBordersMenu = "paragraph-borders";
    const string paragraphShadingMenu = "paragraph-shading";
    const string wrapMenu = "wrap";
    const string bordersMenu = "borders";
    const string shadingMenu = "shading";
    const string symbolMenu = "symbol";

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
            if (InTable &&
                IsWord)
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

    // Each tab names the panel it shows, and the panel the tab it is shown by, as the dialogs' titles
    // are named, by fixed ids.
    const string tabPanelId = "editor-tabpanel";

    static string TabId(string name) =>
        $"editor-tab-{name.ToLowerInvariant()}";

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
    List<StyleChoice> GalleryChoices
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
        if (placeMenu &&
            menu == contextMenu)
        {
            placeMenu = false;
            await PlaceMenuAsync();
        }

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

    // Choosing a tab closes whatever menu is open, the File menu among them, as Word's ribbon does. The
    // tab's mousedown is cancelled to keep the caret, so the outside-click that closes a menu never saw it.
    void SelectTab(string name)
    {
        tab = name;
        if (menu is not null)
        {
            CloseMenu();
        }
    }

    void ToggleMenu(string name)
    {
        menu = menu == name ? null : name;
        openSubmenu = null;
    }

    void CloseMenu()
    {
        menu = null;
        openSubmenu = null;
    }

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

    // Cancel, Escape or a press beside the dialog: it closes, and the reader is back where they were
    // when it opened — the paragraph open again, or the text selected again — as OK leaves them, rather
    // than with the focus nowhere and nothing to type in. Opening the dialog handed the paragraph in.
    Task CancelDialogAsync()
    {
        if (dialog is null)
        {
            return Task.CompletedTask;
        }

        CloseDialog();
        StateHasChanged();
        return ReturnToDocumentAsync();
    }

    // What Escape closes at the document (OnMenuEscape) is a dialog, with no menu over it.
    internal override bool EscapeCancelsDialog =>
        menu is null &&
        dialog is not null;

    Task OnDialogKeyDown(KeyboardEventArgs args)
    {
        if (args.Key == "Escape")
        {
            return CancelDialogAsync();
        }

        return Task.CompletedTask;
    }

    // Menus close before what was chosen runs, so the pages are what the reader sees while it does.

    Task StyleFromMenuAsync(string styleId)
    {
        menu = null;
        return ApplyStyleAsync(styleId);
    }

    Task ListStyleFromMenuAsync(ListStyle style)
    {
        menu = null;
        return SetListStyleAsync(style);
    }

    Task TableFromMenuAsync((int Rows, int Columns) size)
    {
        menu = null;
        return InsertTableAsync(size.Rows, size.Columns);
    }

    // Insert > Table's grid, a component of its own (TableSizePicker), which an internal type cannot be named
    // by a tag for: markup finds only public components.
    RenderFragment TableGridPicker =>
        builder =>
        {
            builder.OpenComponent<TableSizePicker>(0);
            builder.AddComponentParameter(1, nameof(TableSizePicker.Rows), gridRows);
            builder.AddComponentParameter(2, nameof(TableSizePicker.Columns), gridColumns);
            builder.AddComponentParameter(3, nameof(TableSizePicker.OnChoose), EventCallback.Factory.Create<(int Rows, int Columns)>(this, TableFromMenuAsync));
            builder.CloseComponent();
        };

    // Layout

    static readonly string[] marginSides = ["Top", "Bottom", "Left", "Right"];

    readonly Dictionary<string, string> customMargins = new();

    // The margins as the dialog opened on them: each side's twips, and the centimetres they were shown as.
    readonly Dictionary<string, (int Twips, string Shown)> openedMargins = new();

    // Twips as centimetres, to a tenth.
    static string Centimetres(int twips) =>
        (twips * 2.54 / 1440).ToString("0.##", CultureInfo.InvariantCulture) + " cm";

    // Layout > Columns' equal choices, as Word's menu names them.
    internal static readonly (int Count, string Name)[] ColumnChoices = [(1, "One"), (2, "Two"), (3, "Three")];

    void OpenMargins()
    {
        menu = null;
        var current = CurrentPage ?? new PageSetup(11906, 16838, 1440, 1440, 1440, 1440, false);
        foreach (var (side, twips) in (ReadOnlySpan<(string, int)>) [("Top", current.Top), ("Bottom", current.Bottom), ("Left", current.Left), ("Right", current.Right)])
        {
            var shown = (twips * 2.54 / 1440).ToString("0.##", CultureInfo.InvariantCulture);
            customMargins[side] = shown;
            openedMargins[side] = (twips, shown);
        }

        dialog = marginsDialog;
    }

    // The margins typed, in twips; null while one of them is not a size. A side left as it was shown
    // keeps its own twips: two decimal places of a centimetre are not a twip's worth, so 1080 twips,
    // shown as 1.9 cm, would come back 1077 and a preset would no longer be the margins it is.
    internal (int Top, int Right, int Bottom, int Left)? CustomMargins()
    {
        var twips = new Dictionary<string, int>();
        foreach (var side in marginSides)
        {
            var text = customMargins.GetValueOrDefault(side)?.Trim();
            if (openedMargins.TryGetValue(side, out var opened) &&
                text == opened.Shown)
            {
                twips[side] = opened.Twips;
                continue;
            }

            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var centimetres) ||
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

        await OnDialogKeyDown(args);
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

    // Home > Borders' menu, in Word's order.
    internal static readonly (ParagraphBorder Border, string Name)[] ParagraphBorders =
    [
        (ParagraphBorder.Bottom, "Bottom border"),
        (ParagraphBorder.Top, "Top border"),
        (ParagraphBorder.Left, "Left border"),
        (ParagraphBorder.Right, "Right border"),
        (ParagraphBorder.None, "No border"),
        (ParagraphBorder.All, "All borders"),
        (ParagraphBorder.Outside, "Outside borders"),
        (ParagraphBorder.Inside, "Inside horizontal border")
    ];

    // Layout > Wrap Text's choices, in Word's order.
    internal static readonly (PictureWrap Wrap, string Name)[] WrapChoices =
    [
        (PictureWrap.Inline, "In line with text"),
        (PictureWrap.Square, "Square"),
        (PictureWrap.Tight, "Tight"),
        (PictureWrap.TopAndBottom, "Top and bottom"),
        (PictureWrap.BehindText, "Behind text"),
        (PictureWrap.InFrontOfText, "In front of text")
    ];

    Task ParagraphShadingFromMenuAsync(string? fill)
    {
        menu = null;
        return ShadeParagraphsAsync(fill);
    }

    Task ShadingFromMenuAsync(string? fill)
    {
        menu = null;
        return ShadeCellsAsync(fill);
    }

    // Table > Width: the caret's cell's width as the pages drew it, in centimetres, until a width is
    // typed for that paragraph's cell — and again once the caret moves on, as Word's box follows it.
    // What can be typed is Word's range for a column, 0.1 to 55.87 cm (22 inches).
    const double leastColumn = 0.1;
    const double widestColumn = 55.87;

    int? columnWidthAt;

    string ColumnWidthShown
    {
        get
        {
            if (columnWidthAt is { } typedAt &&
                typedAt == CurrentOrdinal)
            {
                return columnWidth;
            }

            if (CurrentCellWidth is not { } points)
            {
                return "";
            }

            return (points / pointsPerCentimetre).ToString("0.##", CultureInfo.InvariantCulture);
        }
    }

    async Task OnColumnWidthAsync(ChangeEventArgs args)
    {
        columnWidth = args.Value?.ToString()?.Trim() ?? "";
        columnWidthAt = CurrentOrdinal;
        if (!double.TryParse(columnWidth, NumberStyles.Float, CultureInfo.InvariantCulture, out var centimetres) ||
            !double.IsFinite(centimetres))
        {
            editNote = "Type the column's width in centimetres, as a number.";
            return;
        }

        // The box shows the cell's width again once the edit has laid the pages out; a width out of
        // range is said after the edit, which clears the note as it starts.
        columnWidthAt = null;
        var clamped = Math.Clamp(centimetres, leastColumn, widestColumn);
        await SetColumnWidthAsync(clamped);
        if (clamped != centimetres)
        {
            editNote = $"A column is {Number(leastColumn)} to {Number(widestColumn)} cm wide.";
        }
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

        await OnDialogKeyDown(args);
    }

    Task FontFromMenuAsync(string font)
    {
        menu = null;
        return SetFontAsync(font);
    }

    // The ribbon's Font and Font size boxes take a name or a size typed into them, as Word's do: set
    // when Enter is pressed or the box is left, which is when a browser says the box has changed.
    Task OnFontTypedAsync(ChangeEventArgs args)
    {
        var font = (args.Value as string ?? "").Trim();
        if (font.Length == 0 ||
            Off(Commands.Font))
        {
            return Task.CompletedTask;
        }

        menu = null;
        return SetFontAsync(font);
    }

    Task OnSizeTypedAsync(ChangeEventArgs args)
    {
        var text = (args.Value as string ?? "").Trim();
        if (Off(Commands.FontSize))
        {
            return Task.CompletedTask;
        }

        // Word's sizes are 1 to 1638 points, in halves.
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var size) ||
            size is < 1 or > 1638)
        {
            editNote = "A font size is a number from 1 to 1638.";
            return Task.CompletedTask;
        }

        menu = null;
        return SetSizeAsync(Math.Round(size * 2) / 2);
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
        waitingOpen = null;
        if (Dirty)
        {
            dialog = discardDialog;
            return Task.CompletedTask;
        }

        return NewDocumentAsync();
    }

    // Another file opened over edits not saved asks as New does, and opens once answered.
    Func<Task>? waitingOpen;

    internal override bool AskBeforeOpen(Func<Task> open)
    {
        if (!Dirty)
        {
            return false;
        }

        menu = null;
        waitingOpen = open;
        dialog = discardDialog;
        return true;
    }

    // What the discard dialog goes on to: the file waiting to open, or a new document.
    Task ContinueAfterDiscardAsync()
    {
        var open = waitingOpen;
        waitingOpen = null;
        return open?.Invoke() ?? NewDocumentAsync();
    }

    async Task SaveThenNewAsync()
    {
        dialog = null;
        await SaveAsync();
        if (Dirty)
        {
            return;
        }

        await ContinueAfterDiscardAsync();
    }

    async Task DiscardThenNewAsync()
    {
        dialog = null;
        await DropDraftAsync();
        await ContinueAfterDiscardAsync();
    }

    // A command the script carries out, pressed where it cannot: it says why.
    Task NoteAsync(string note)
    {
        editNote = note;
        return Task.CompletedTask;
    }

    // Insert > Symbol: the symbols Word's gallery opens on, and the few more a document most often wants,
    // each put in at the caret as text in the look around it.
    internal static readonly (string Symbol, string Name)[] Symbols =
    [
        ("€", "Euro sign"), ("£", "Pound sign"), ("¥", "Yen sign"), ("©", "Copyright sign"), ("®", "Registered sign"),
        ("™", "Trade mark sign"), ("±", "Plus-minus sign"), ("≠", "Not equal to"), ("≤", "Less-than or equal to"), ("≥", "Greater-than or equal to"),
        ("÷", "Division sign"), ("×", "Multiplication sign"), ("∞", "Infinity"), ("µ", "Micro sign"), ("α", "Greek small letter alpha"),
        ("β", "Greek small letter beta"), ("π", "Greek small letter pi"), ("Ω", "Greek capital letter omega"), ("∑", "N-ary summation"), ("°", "Degree sign"),
        ("§", "Section sign"), ("¶", "Pilcrow sign"), ("•", "Bullet"), ("…", "Horizontal ellipsis"), ("–", "En dash"),
        ("—", "Em dash"), ("←", "Leftwards arrow"), ("→", "Rightwards arrow"), ("✓", "Check mark"), ("☺", "White smiling face")
    ];

    Task SymbolFromMenuAsync(string symbol)
    {
        menu = null;
        return PasteAsync("", symbol);
    }

    // References > Next and Previous Footnote: the caret to the next or the last note's mark in the body,
    // before it, as Word's moves it; from the last mark onwards Next does nothing, as Word's does.
    async Task StepNoteAsync(bool forward)
    {
        if (await RequireAsync() is not { } target)
        {
            return;
        }

        var marks = outline.Paragraphs
            .Where(_ => _.Part == ReviewPart.Body)
            .SelectMany(Marks)
            .ToList();
        var caret = (target.Paragraph, target.Offset);
        var index = forward
            ? marks.FindIndex(_ => _.CompareTo(caret) > 0)
            : marks.FindLastIndex(_ => _.CompareTo(caret) < 0);
        if (index < 0)
        {
            if (marks.Count == 0)
            {
                editNote = "The document has no footnotes or endnotes.";
            }

            // The paragraph was handed in to find where the caret was: it opens again there.
            if (target.Typing)
            {
                await ResumeAsync(target.Paragraph, target.Offset);
            }

            return;
        }

        await ResumeAsync(marks[index].Paragraph, marks[index].Offset);

        // Where a paragraph's note marks are, in its places.
        static IEnumerable<(int Paragraph, int Offset)> Marks(EditParagraph paragraph)
        {
            var at = 0;
            foreach (var unit in paragraph.Units)
            {
                if (unit.Kind == EditUnitKind.Note)
                {
                    yield return (paragraph.Ordinal, at);
                }

                at += unit.Editable ? unit.Text.Length : 1;
            }
        }
    }

    // Layout > Paragraph's boxes: what the caret's paragraph has, shown as the Paragraph dialog shows it,
    // and set as the dialog sets it when a new value is typed in.
    string ParagraphValue(EditorCommand box)
    {
        if (CurrentProperties is not { } properties)
        {
            return "0";
        }

        if (box == Commands.LeftIndent)
        {
            return Number(properties.LeftIndentPoints / pointsPerCentimetre);
        }

        if (box == Commands.RightIndent)
        {
            return Number(properties.RightIndentPoints / pointsPerCentimetre);
        }

        if (box == Commands.SpacingBefore)
        {
            return Number(properties.SpacingBeforePoints);
        }

        return Number(properties.SpacingAfterPoints);
    }

    async Task OnParagraphBoxAsync(EditorCommand box, ChangeEventArgs args)
    {
        if (Off(box))
        {
            return;
        }

        var text = args.Value?.ToString() ?? "";
        var indent = box == Commands.LeftIndent || box == Commands.RightIndent;
        if (Parse(text) is not { } value ||
            (!indent && value < 0))
        {
            editNote = indent ? "Type the indent in centimetres, as a number." : "Type the spacing in points, as a number not below 0.";
            return;
        }

        var format = new ParagraphFormat();
        if (box == Commands.LeftIndent)
        {
            format = format with
            {
                LeftPoints = value * pointsPerCentimetre
            };
        }
        else if (box == Commands.RightIndent)
        {
            format = format with
            {
                RightPoints = value * pointsPerCentimetre
            };
        }
        else if (box == Commands.SpacingBefore)
        {
            format = format with
            {
                BeforePoints = value
            };
        }
        else
        {
            format = format with
            {
                AfterPoints = value
            };
        }

        if (await RequireAsync() is not { } target)
        {
            return;
        }

        await EditParagraphsAsync(
            target,
            (_, options) => DocumentEditor.FormatParagraphs(_, target.Paragraphs, format, options));
    }

    // Links

    async Task OpenLinkAsync()
    {
        if (!await BeginLinkAsync())
        {
            return;
        }

        // Text selected in a link edits it, from the address it has.
        linkAddress = ExistingLink ?? "https://";
        linkText = "";
        dialog = linkDialog;
        // In a link with nothing selected, the dialog only removes it; elsewhere it asks for the address,
        // and with nothing selected for the text to display too.
        focusLink = LinkAsksAddress;
    }

    // What the link dialog asks for: an address, unless the caret is in a link with nothing selected,
    // and the text to display when nothing is selected.
    bool LinkAsksAddress => linkTarget is {HasSelection: true} || !LinkInLink;

    bool LinkAsksText => linkTarget is not {HasSelection: true} && !LinkInLink;

    string linkText = "";

    void OnLinkTextInput(ChangeEventArgs args) =>
        linkText = args.Value as string ?? "";

    void OnLinkInput(ChangeEventArgs args) =>
        linkAddress = args.Value as string ?? "";

    Task InsertLinkAsync()
    {
        dialog = null;
        if (LinkAsksText)
        {
            return LinkTextAsync(linkText, linkAddress.Trim());
        }

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

    Task AddBookmarkFromDialogAsync()
    {
        dialog = null;
        if (bookmarkTarget is { } target &&
            DocumentEditor.IsBookmarkName(bookmarkName))
        {
            return AddBookmarkAsync(target, bookmarkName);
        }

        return Task.CompletedTask;
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
            return CancelDialogAsync();
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
            return CancelDialogAsync();
        }

        if (args.Key == "Enter" &&
            LinkAsksAddress &&
            linkAddress.Trim().Length > 0 &&
            !(LinkAsksText && linkText.Length == 0))
        {
            return InsertLinkAsync();
        }

        return Task.CompletedTask;
    }
}
