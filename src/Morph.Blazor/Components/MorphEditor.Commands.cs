namespace Morph;

// The editor's commands, in one table: every button of the ribbon, the verbs of its menus, Word's keys and
// the buttons the script handles itself. Each says when it can be used, whether it is on, and what it does
// (EditorCommand), asked of one EditorState, so a button and its key cannot disagree; the ribbon's markup
// draws each from its entry (Tool, MenuItem) and holds no rule of its own.
//
// A menu's choices — the margins, the colours, the wrap styles — are not commands: they are what a
// command's menu offers, and can be chosen only while it is open.
public partial class MorphEditor
{
    // How a command's button is drawn: large, its label under its icon (and a caret when it opens a
    // menu); small, its label beside its icon; an icon alone, named by its label; or a group's launcher.
    enum ToolLook
    {
        Large,
        Small,
        Icon,
        Launcher
    }

    // What the ribbon was drawn from: worked out at the start of each render.
    EditorState state = new();

    // Where the reader is, for the commands to be decided on.
    EditorState State() =>
        new()
        {
            IsWord = IsWord,
            CanWrite = CanWrite,
            Revising = revising,
            InTable = InTable,
            InLink = InLink,
            List = CurrentList,
            Subscript = IsSubscript,
            Superscript = IsSuperscript,
            PictureSelected = PictureSelected,
            HasContents = outline.HasContents,
            Tracking = Tracking,
            ForcesTracking = outline.ForcesTracking,
            CanComment = CanComment,
            CanResolve = CanResolve,
            HasChanges = review.Changes.Count > 0,
            OffersReview = ShowReview,
            Painting = Painting,
            MarksShown = marksShown,
            RulerShown = rulerShown,
            PagesShown = sidebarOpen,
            ReviewShown = reviewOpen,
            FindShown = findOpen && !replaceOpen,
            ReplaceShown = ReplaceShown,
            Presenting = presenting
        };

    // A command run from the ribbon, a menu or a key: only while it can be.
    Task RunCommandAsync(EditorCommand command)
    {
        var now = State();
        if (!command.Visible(now) ||
            !command.Enabled(now))
        {
            return Task.CompletedTask;
        }

        return command.Run(this);
    }

    // A command chosen from a menu, which closes first, so the pages are what the reader sees while it runs.
    Task ChooseAsync(EditorCommand command)
    {
        menu = null;
        return RunCommandAsync(command);
    }

    // A command the script names: a key's, or a button's it handles itself when no paragraph is open.
    internal override Task CommandAsync(string name)
    {
        if (Commands.Named(name) is not { } command)
        {
            return Task.CompletedTask;
        }

        return RunCommandAsync(command);
    }

    bool Off(EditorCommand command) =>
        !command.Enabled(state);

    bool Shown(EditorCommand command) =>
        command.Visible(state);

    string? PressedOf(EditorCommand command)
    {
        if (command.Checked(state) is { } on)
        {
            return Pressed(on);
        }

        return null;
    }

    // A button the script handles has no click here: an empty callback adds no handler.
    EventCallback ClickOf(EditorCommand command)
    {
        if (command.Handler != CommandHandler.Component)
        {
            return default;
        }

        return EventCallback.Factory.Create(this, () => RunCommandAsync(command));
    }

    // A large or small button names itself in its text, and has a tooltip only where it has more to say.
    static string? TitleOf(EditorCommand command, ToolLook look)
    {
        if (look is ToolLook.Large or ToolLook.Small &&
            command.Tip is null &&
            command.Key is null)
        {
            return null;
        }

        return command.Title;
    }

    static string ToolClass(ToolLook look, string? classes)
    {
        var name = look switch
        {
            ToolLook.Large => "editor-tool editor-large",
            ToolLook.Small => "editor-tool",
            ToolLook.Icon => "viewer-button",
            _ => "editor-launcher"
        };
        if (classes is null)
        {
            return name;
        }

        return $"{name} {classes}";
    }

    internal static class Commands
    {
        // What a command the script carries out does when .NET is handed it.
        static readonly Func<MorphEditor, Task> nothing = _ => Task.CompletedTask;

        // What a Table tab command asks: the caret in a table, as well as a document to write.
        public static bool InATable(EditorState state) =>
            state is
            {
                Editable: true,
                InTable: true
            };

        // Home

        public static readonly EditorCommand Undo =
            new("undo", "Undo", _ => _.UndoAsync())
            {
                Icon = Lucide.Undo2,
                Key = "Ctrl+Z",
                Place = "Home › Undo",
                Handler = CommandHandler.Script
            };

        public static readonly EditorCommand Redo =
            new("redo", "Redo", _ => _.RedoAsync())
            {
                Icon = Lucide.Redo2,
                Key = "Ctrl+Y",
                Place = "Home › Undo",
                Handler = CommandHandler.Script
            };

        // The clipboard is the script's alone: cut on the pages reaches .NET as a delete.
        public static readonly EditorCommand Paste =
            new("paste", "Paste", nothing)
            {
                Icon = Lucide.ClipboardPaste,
                Key = "Ctrl+V",
                Place = "Home › Clipboard",
                Handler = CommandHandler.Script
            };

        public static readonly EditorCommand Cut =
            new("cut", "Cut", nothing)
            {
                Icon = Lucide.Scissors,
                Key = "Ctrl+X",
                Place = "Home › Clipboard",
                Handler = CommandHandler.Script
            };

        // Copying only reads, as Word lets a read-only document be copied from.
        public static readonly EditorCommand Copy =
            new("copy", "Copy", nothing)
            {
                Icon = Lucide.Copy,
                Key = "Ctrl+C",
                Place = "Home › Clipboard",
                Handler = CommandHandler.Script,
                Enabled = _ => true
            };

        public static readonly EditorCommand FormatPainter =
            new("format-painter", "Format Painter", _ => _.FormatPainterAsync())
            {
                Icon = Lucide.Paintbrush,
                Tip = "Format Painter: pick up the formatting at the caret, then select text to paint it",
                Place = "Home › Clipboard",
                Checked = _ => _.Painting
            };

        public static readonly EditorCommand Font = new("font", "Font", _ => _.ToggleMenu(fontMenu))
        {
            Place = "Home › Font",
            Menu = fontMenu,
            Opens = "listbox"
        };

        public static readonly EditorCommand FontSize =
            new("font-size", "Font size", _ => _.ToggleMenu(sizeMenu))
            {
                Place = "Home › Font",
                Menu = sizeMenu,
                Opens = "listbox"
            };

        public static readonly EditorCommand Grow =
            new("grow", "Grow font", _ => _.GrowAsync(1))
            {
                Icon = Lucide.AArrowUp,
                Key = "Ctrl+]",
                Place = "Home › Font"
            };

        public static readonly EditorCommand Shrink =
            new("shrink", "Shrink font", _ => _.GrowAsync(-1))
            {
                Icon = Lucide.AArrowDown,
                Key = "Ctrl+[",
                Place = "Home › Font"
            };

        public static readonly EditorCommand ClearFormatting =
            new("clear-formatting", "Clear formatting", _ => _.ClearFormattingAsync())
            {
                Icon = Lucide.RemoveFormatting,
                Key = "Ctrl+Space",
                Place = "Home › Font"
            };

        public static readonly EditorCommand Bold =
            new("bold", "Bold", _ => _.FormatAsync(properties => properties.Bold, on => new(Bold: on)))
            {
                Icon = Lucide.Bold,
                Key = "Ctrl+B",
                Place = "Home › Font",
                Handler = CommandHandler.Script
            };

        public static readonly EditorCommand Italic =
            new("italic", "Italic", _ => _.FormatAsync(properties => properties.Italic, on => new(Italic: on)))
            {
                Icon = Lucide.Italic,
                Key = "Ctrl+I",
                Place = "Home › Font",
                Handler = CommandHandler.Script
            };

        public static readonly EditorCommand Underline =
            new("underline", "Underline", _ => _.FormatAsync(properties => properties.Underline, on => new(Underline: on)))
            {
                Icon = Lucide.Underline,
                Key = "Ctrl+U",
                Place = "Home › Font",
                Handler = CommandHandler.Script
            };

        public static readonly EditorCommand Strike =
            new("strike", "Strikethrough", _ => _.FormatAsync(properties => properties.Strikethrough, on => new(Strike: on)))
            {
                Icon = Lucide.Strikethrough,
                Place = "Home › Font",
                Handler = CommandHandler.Script
            };

        public static readonly EditorCommand Subscript =
            new("subscript", "Subscript", _ => _.ToggleSubscriptAsync())
            {
                Icon = Lucide.Subscript,
                Key = "Ctrl+=",
                Place = "Home › Font",
                Checked = _ => _.Subscript
            };

        public static readonly EditorCommand Superscript =
            new("superscript", "Superscript", _ => _.ToggleSuperscriptAsync())
            {
                Icon = Lucide.Superscript,
                Key = "Ctrl+Shift+=",
                Place = "Home › Font",
                Checked = _ => _.Superscript
            };

        public static readonly EditorCommand Highlight =
            new("highlight", "Highlight", _ => _.SetHighlightAsync(_.LastHighlight))
            {
                Icon = Lucide.Highlighter,
                Place = "Home › Font"
            };

        public static readonly EditorCommand Highlights =
            new("highlights", "More highlight colours", _ => _.ToggleMenu(highlightMenu))
            {
                Icon = Lucide.ChevronDown,
                Place = "Home › Font",
                Menu = highlightMenu,
                Opens = "dialog"
            };

        public static readonly EditorCommand FontColour =
            new("font-colour", "Font colour", _ => _.SetColorAsync(_.LastColor))
            {
                Icon = Lucide.Baseline,
                Place = "Home › Font"
            };

        public static readonly EditorCommand FontColours =
            new("font-colours", "More font colours", _ => _.ToggleMenu(colorMenu))
            {
                Icon = Lucide.ChevronDown,
                Place = "Home › Font",
                Menu = colorMenu,
                Opens = "dialog"
            };

        public static readonly EditorCommand FontDialog =
            new("font-dialog", "Font settings", _ => _.OpenFontDialogAsync())
            {
                Icon = Lucide.SquareArrowDownRight,
                Place = "Home › Font",
                Opens = "dialog"
            };

        public static readonly EditorCommand Bullets =
            new("bullets", "Bullets", _ => _.ToggleListAsync(ListKind.Bullet))
            {
                Icon = Lucide.List,
                Key = "Ctrl+Shift+L",
                Place = "Home › Paragraph",
                Checked = _ => _.List == ListKind.Bullet
            };

        public static readonly EditorCommand BulletLibrary =
            new("bullet-library", "Bullet library", _ => _.ToggleMenu(bulletsMenu))
            {
                Icon = Lucide.ChevronDown,
                Place = "Home › Paragraph",
                Menu = bulletsMenu,
                Opens = "menu"
            };

        public static readonly EditorCommand Numbering =
            new("numbering", "Numbering", _ => _.ToggleListAsync(ListKind.Number))
            {
                Icon = Lucide.ListOrdered,
                Place = "Home › Paragraph",
                Checked = _ => _.List == ListKind.Number
            };

        public static readonly EditorCommand NumberingLibrary =
            new("numbering-library", "Numbering library", _ => _.ToggleMenu(numberingMenu))
            {
                Icon = Lucide.ChevronDown,
                Place = "Home › Paragraph",
                Menu = numberingMenu,
                Opens = "menu"
            };

        public static readonly EditorCommand RestartNumbering =
            new("restart-numbering", "Restart at 1", _ => _.RestartNumberingAsync(true))
            {
                Place = "Home › Paragraph",
                Enabled = _ => _ is
                {
                    Editable: true,
                    List: ListKind.Number
                }
            };

        public static readonly EditorCommand ContinueNumbering =
            new("continue-numbering", "Continue numbering", _ => _.RestartNumberingAsync(false))
            {
                Place = "Home › Paragraph",
                Enabled = _ => _ is
                {
                    Editable: true,
                    List: ListKind.Number
                }
            };

        public static readonly EditorCommand DecreaseIndent =
            new("decrease-indent", "Decrease indent", _ => _.IndentAsync(-1))
            {
                Icon = Lucide.IndentDecrease,
                Place = "Home › Paragraph"
            };

        public static readonly EditorCommand IncreaseIndent =
            new("increase-indent", "Increase indent", _ => _.IndentAsync(1))
            {
                Icon = Lucide.IndentIncrease,
                Place = "Home › Paragraph"
            };

        // Only shows: a read-only document's marks can be shown too.
        public static readonly EditorCommand Marks =
            new("marks", "Show/Hide formatting marks", _ => _.ToggleMarksAsync())
            {
                Icon = Lucide.Pilcrow,
                Tip = "Show/Hide ¶",
                Key = "Ctrl+Shift+8",
                Place = "Home › Paragraph",
                Enabled = _ => true,
                Checked = _ => _.MarksShown
            };

        public static readonly EditorCommand AlignLeft =
            new("align-left", "Align left", _ => _.AlignAsync(TextAlignment.Left))
            {
                Icon = Lucide.TextAlignStart,
                Key = "Ctrl+L",
                Place = "Home › Paragraph",
                Handler = CommandHandler.Script
            };

        public static readonly EditorCommand AlignCenter =
            new("align-center", "Centre", _ => _.AlignAsync(TextAlignment.Center))
            {
                Icon = Lucide.TextAlignCenter,
                Key = "Ctrl+E",
                Place = "Home › Paragraph",
                Handler = CommandHandler.Script
            };

        public static readonly EditorCommand AlignRight =
            new("align-right", "Align right", _ => _.AlignAsync(TextAlignment.Right))
            {
                Icon = Lucide.TextAlignEnd,
                Key = "Ctrl+R",
                Place = "Home › Paragraph",
                Handler = CommandHandler.Script
            };

        public static readonly EditorCommand Justify =
            new("align-justify", "Justify", _ => _.AlignAsync(TextAlignment.Justify))
            {
                Icon = Lucide.TextAlignJustify,
                Key = "Ctrl+J",
                Place = "Home › Paragraph",
                Handler = CommandHandler.Script
            };

        public static readonly EditorCommand Spacing =
            new("spacing", "Line and paragraph spacing", _ => _.ToggleMenu(spacingMenu))
            {
                Icon = Lucide.ListChevronsUpDown,
                Place = "Home › Paragraph",
                Menu = spacingMenu,
                Opens = "menu"
            };

        public static readonly EditorCommand ParagraphShading = new("paragraph-shading", "Paragraph shading", _ => _.ShadeParagraphsAsync(_.LastShading))
        {
            Icon = Lucide.PaintBucket,
            Place = "Home › Paragraph"
        };

        public static readonly EditorCommand ParagraphShadings = new("paragraph-shadings", "More paragraph shading colours", _ => _.ToggleMenu(paragraphShadingMenu))
        {
            Icon = Lucide.ChevronDown,
            Place = "Home › Paragraph",
            Menu = paragraphShadingMenu,
            Opens = "dialog"
        };

        public static readonly EditorCommand ParagraphBorders =
            new("paragraph-borders", "Paragraph borders", _ => _.BorderParagraphsAsync(_.LastParagraphBorder))
            {
                Icon = Lucide.SquareDashedBottom,
                Place = "Home › Paragraph"
            };

        public static readonly EditorCommand MoreParagraphBorders =
            new("more-paragraph-borders", "More paragraph borders", _ => _.ToggleMenu(paragraphBordersMenu))
            {
                Icon = Lucide.ChevronDown,
                Tip = "More borders",
                Place = "Home › Paragraph",
                Menu = paragraphBordersMenu,
                Opens = "menu"
            };

        public static readonly EditorCommand ParagraphDialog =
            new("paragraph-dialog", "Paragraph settings", _ => _.OpenParagraphDialogAsync())
            {
                Icon = Lucide.SquareArrowDownRight,
                Place = "Home › Paragraph",
                Opens = "dialog"
            };

        public static readonly EditorCommand Styles =
            new("styles", "All styles", _ => _.ToggleMenu(stylesMenu))
            {
                Icon = Lucide.PanelBottomOpen,
                Place = "Home › Styles",
                Menu = stylesMenu,
                Opens = "listbox"
            };

        public static readonly EditorCommand Find =
            new("find", "Find", _ => _.ToggleFindAsync())
            {
                Icon = Lucide.Search,
                Key = "Ctrl+F",
                Place = "Home › Editing",
                Enabled = _ => true,
                Checked = _ => _.FindShown
            };

        // Opens the bar a replacement is typed in; replacing is the edit.
        public static readonly EditorCommand Replace =
            new("replace", "Replace", _ => _.OpenReplaceAsync())
            {
                Icon = Lucide.Replace,
                Key = "Ctrl+H",
                Place = "Home › Editing",
                Enabled = _ => _.CanWrite,
                Checked = _ => _.ReplaceShown
            };

        // Insert

        public static readonly EditorCommand PageBreak =
            new("page-break", "Page break", _ => _.InsertPageBreakAsync())
            {
                Icon = Lucide.SeparatorHorizontal,
                Place = "Insert › Pages"
            };

        public static readonly EditorCommand Table =
            new("table", "Table", _ => _.ToggleMenu(tableMenu))
            {
                Icon = Lucide.Table,
                Place = "Insert › Tables",
                Menu = tableMenu,
                Opens = "dialog"
            };

        // The picker opens from the press itself, which a browser asks of a file input: this only takes
        // where the picture is to go.
        public static readonly EditorCommand Picture =
            new("picture", "Picture", _ => _.BeginPicture())
            {
                Icon = Lucide.ImagePlus,
                Place = "Insert › Illustrations"
            };

        public static readonly EditorCommand Link = new("link", "Link", _ => _.OpenLinkAsync())
        {
            Icon = Lucide.Link,
            Key = "Ctrl+K",
            Place = "Insert › Links",
            Opens = "dialog"
        };

        public static readonly EditorCommand RemoveLink =
            new("remove-link", "Remove link", _ => _.UnlinkAsync())
            {
                Icon = Lucide.Unlink,
                Place = "Insert › Links",
                Enabled = _ => _ is
                {
                    Editable: true,
                    InLink: true
                }
            };

        public static readonly EditorCommand Bookmark =
            new("bookmark", "Bookmark", _ => _.OpenBookmarkAsync())
            {
                Icon = Lucide.Bookmark,
                Place = "Insert › Links",
                Opens = "dialog"
            };

        public static readonly EditorCommand Header =
            new("header", "Header", _ => _.EditHeaderAsync(false))
            {
                Icon = Lucide.PanelTop,
                Place = "Insert › Header and footer"
            };

        public static readonly EditorCommand Footer =
            new("footer", "Footer", _ => _.EditHeaderAsync(true))
            {
                Icon = Lucide.PanelBottom,
                Place = "Insert › Header and footer"
            };

        public static readonly EditorCommand PageNumber =
            new("page-number", "Page number", _ => _.ToggleMenu(pageNumberMenu))
            {
                Icon = Lucide.Hash,
                Place = "Insert › Header and footer",
                Menu = pageNumberMenu,
                Opens = "menu"
            };

        public static readonly EditorCommand Date =
            new("date", "Date & time", _ => _.OpenDateAsync())
            {
                Icon = Lucide.CalendarDays,
                Place = "Insert › Text",
                Opens = "dialog"
            };

        // Layout

        public static readonly EditorCommand Margins =
            new("margins", "Margins", _ => _.ToggleMenu(marginsMenu))
            {
                Icon = Lucide.Scan,
                Place = "Layout › Page setup",
                Menu = marginsMenu,
                Opens = "menu"
            };

        public static readonly EditorCommand Orientation =
            new("orientation", "Orientation", _ => _.ToggleMenu(orientationMenu))
            {
                Icon = Lucide.RectangleVertical,
                Place = "Layout › Page setup",
                Menu = orientationMenu,
                Opens = "menu"
            };

        public static readonly EditorCommand PaperSize =
            new("paper-size", "Size", _ => _.ToggleMenu(sizeOfPageMenu))
            {
                Icon = Lucide.File,
                Place = "Layout › Page setup",
                Menu = sizeOfPageMenu,
                Opens = "menu"
            };

        public static readonly EditorCommand Columns =
            new("columns", "Columns", _ => _.ToggleMenu(columnsMenu))
            {
                Icon = Lucide.Columns2,
                Place = "Layout › Page setup",
                Menu = columnsMenu,
                Opens = "menu"
            };

        public static readonly EditorCommand Breaks =
            new("breaks", "Breaks", _ => _.ToggleMenu(breaksMenu))
            {
                Icon = Lucide.SeparatorHorizontal,
                Place = "Layout › Page setup",
                Menu = breaksMenu,
                Opens = "menu"
            };

        public static readonly EditorCommand Wrap =
            new("wrap-text", "Wrap text", _ => _.ToggleMenu(wrapMenu))
            {
                Icon = Lucide.WrapText,
                Tip = "Wrap text: how the selected picture stands with the text",
                Place = "Layout › Arrange",
                Menu = wrapMenu,
                Opens = "menu",
                Enabled = _ => _ is
                {
                    Editable: true,
                    PictureSelected: true
                }
            };

        // References

        public static readonly EditorCommand Contents =
            new("contents", "Table of contents", _ => _.InsertContentsAsync())
            {
                Icon = Lucide.TableOfContents,
                Place = "References › Table of contents"
            };

        public static readonly EditorCommand UpdateContents =
            new("update-contents", "Update table", _ => _.UpdateContentsAsync())
            {
                Icon = Lucide.RefreshCw,
                Place = "References › Table of contents",
                Enabled = _ => _ is
                {
                    Editable: true,
                    HasContents: true
                }
            };

        public static readonly EditorCommand Footnote =
            new("footnote", "Footnote", _ => _.InsertFootnoteAsync())
            {
                Icon = Lucide.Superscript,
                Place = "References › Footnotes"
            };

        public static readonly EditorCommand Endnote =
            new("endnote", "Endnote", _ => _.InsertEndnoteAsync())
            {
                Icon = Lucide.FileDown,
                Place = "References › Footnotes"
            };

        // Table, the contextual tab

        public static readonly EditorCommand InsertRowAbove =
            TableCommand("insert-row-above", "Insert above", TableEdit.InsertRowAbove, Lucide.BetweenHorizontalStart, "Rows and columns");

        public static readonly EditorCommand InsertRowBelow =
            TableCommand("insert-row-below", "Insert below", TableEdit.InsertRowBelow, Lucide.BetweenHorizontalEnd, "Rows and columns");

        public static readonly EditorCommand InsertColumnLeft =
            TableCommand("insert-column-left", "Insert left", TableEdit.InsertColumnLeft, Lucide.BetweenVerticalStart, "Rows and columns");

        public static readonly EditorCommand InsertColumnRight =
            TableCommand("insert-column-right", "Insert right", TableEdit.InsertColumnRight, Lucide.BetweenVerticalEnd, "Rows and columns");

        public static readonly EditorCommand DeleteRow =
            TableCommand("delete-row", "Delete row", TableEdit.DeleteRow, Lucide.Rows2, "Delete");

        public static readonly EditorCommand DeleteColumn =
            TableCommand("delete-column", "Delete column", TableEdit.DeleteColumn, Lucide.Columns2, "Delete");

        public static readonly EditorCommand DeleteTable =
            TableCommand("delete-table", "Delete table", TableEdit.DeleteTable, Lucide.Trash, "Delete");

        public static readonly EditorCommand MergeCells =
            new("merge-cells", "Merge cells", _ => _.MergeCellsAsync())
            {
                Icon = Lucide.TableCellsMerge,
                Place = "Table › Merge",
                Enabled = InATable
            };

        public static readonly EditorCommand SplitCells =
            new("split-cells", "Split cells", _ => _.OpenSplitAsync())
            {
                Icon = Lucide.TableCellsSplit,
                Place = "Table › Merge",
                Opens = "dialog",
                Enabled = InATable
            };

        public static readonly EditorCommand DistributeColumns =
            new("distribute-columns", "Distribute columns", _ => _.DistributeColumnsAsync())
            {
                Icon = Lucide.Columns3,
                Place = "Table › Cell size",
                Enabled = InATable
            };

        public static readonly EditorCommand AutoFit =
            new("autofit", "AutoFit", _ => _.ToggleMenu(fitMenu))
            {
                Icon = Lucide.Expand,
                Place = "Table › Cell size",
                Menu = fitMenu,
                Opens = "menu",
                Enabled = InATable
            };

        public static readonly EditorCommand TableBorders =
            new("table-borders", "Borders", _ => _.ToggleMenu(bordersMenu))
            {
                Icon = Lucide.Grid2x2,
                Place = "Table › Borders and shading",
                Menu = bordersMenu,
                Opens = "menu",
                Enabled = InATable
            };

        public static readonly EditorCommand CellShading =
            new("cell-shading", "Shading", _ => _.ShadeCellsAsync(_.LastShading))
            {
                Icon = Lucide.PaintBucket,
                Place = "Table › Borders and shading",
                Enabled = InATable
            };

        public static readonly EditorCommand CellShadings =
            new("cell-shadings", "More shading colours", _ => _.ToggleMenu(shadingMenu))
            {
                Icon = Lucide.ChevronDown,
                Place = "Table › Borders and shading",
                Menu = shadingMenu,
                Opens = "dialog",
                Enabled = InATable
            };

        // Review

        public static readonly EditorCommand WordCount =
            new("word-count", "Word count", _ => _.dialog = countDialog)
            {
                Icon = Lucide.WholeWord,
                Place = "Review › Proofing",
                Opens = "dialog",
                Enabled = _ => true
            };

        public static readonly EditorCommand NewComment =
            new("new-comment", "New comment", _ => _.CommentAsync())
            {
                Icon = Lucide.MessageSquarePlus,
                Place = "Review › Comments",
                Enabled = _ => _ is
                {
                    CanComment: true,
                    Revising: false
                },
                Visible = _ => _.OffersReview
            };

        public static readonly EditorCommand ReviewPane =
            new("review-pane", "Comments and changes", _ => _.ToggleReviewAsync())
            {
                Icon = Lucide.MessageSquare,
                Place = "Review › Comments",
                Enabled = _ => true,
                Visible = _ => _.OffersReview,
                Checked = _ => _.ReviewShown
            };

        public static readonly EditorCommand Track =
            new("track", "Track changes", _ => _.SetTrackingAsync(!_.outline.Tracking))
            {
                Icon = Lucide.FilePenLine,
                Place = "Review › Tracking",
                Handler = CommandHandler.Script,
                Enabled = _ => _ is
                {
                    Editable: true,
                    ForcesTracking: false
                },
                Checked = _ => _.Tracking
            };

        public static readonly EditorCommand AcceptAll =
            new("accept-all", "Accept all", _ => _.ResolveAllAsync(true))
            {
                Icon = Lucide.CheckCheck,
                Place = "Review › Changes",
                Enabled = _ => _ is
                {
                    HasChanges: true,
                    Revising: false
                },
                Visible = _ => _ is
                {
                    OffersReview: true,
                    CanResolve: true
                }
            };

        public static readonly EditorCommand RejectAll =
            new("reject-all", "Reject all", _ => _.ResolveAllAsync(false))
            {
                Icon = Lucide.X,
                Place = "Review › Changes",
                Enabled = _ => _ is
                {
                    HasChanges: true,
                    Revising: false
                },
                Visible = _ => _ is
                {
                    OffersReview: true,
                    CanResolve: true
                }
            };

        // View: these only show, so they are there for any document.

        public static readonly EditorCommand Pages =
            new("pages", "Pages", _ => _.ToggleSidebarAsync())
            {
                Icon = Lucide.PanelLeft,
                Place = "View › Show",
                Enabled = _ => true,
                Checked = _ => _.PagesShown
            };

        public static readonly EditorCommand Ruler =
            new("ruler", "Ruler", _ => _.ToggleRuler())
            {
                Icon = Lucide.Ruler,
                Place = "View › Show",
                Enabled = _ => true,
                Visible = _ => _.IsWord,
                Checked = _ => _.RulerShown
            };

        public static readonly EditorCommand Present =
            new("presentation", "Present", nothing)
            {
                Icon = Lucide.Presentation,
                Place = "View › Present",
                Handler = CommandHandler.Viewer,
                Enabled = _ => true,
                Checked = _ => _.Presenting
            };

        // Keys alone: Word's for a list's paragraph and a heading (onParagraphKey in morph-edit.js), its
        // headings' and line spacings', a picture's Reset size, and Delete on text selected across the pages.

        public static readonly EditorCommand ListLevelIn =
            new("indent", "Increase list level", _ => _.TabInListAsync(1))
            {
                Key = "Tab"
            };

        public static readonly EditorCommand ListLevelOut =
            new("outdent", "Decrease list level", _ => _.TabInListAsync(-1))
            {
                Key = "Shift+Tab"
            };

        public static readonly EditorCommand EndList =
            new("end-list", "End the list", _ => _.EndListAsync());

        public static readonly EditorCommand Unindent =
            new("unindent", "Take off the number or a step of indent", _ => _.UnindentAsync());

        public static readonly EditorCommand FollowOn =
            new("follow-on", "Go on in the style that follows", _ => _.FollowOnAsync());

        public static readonly EditorCommand ResetPicture =
            new("reset-picture", "Reset size", _ => _.ResetPictureAsync());

        public static readonly EditorCommand Heading1 =
            Heading(1);

        public static readonly EditorCommand Heading2 =
            Heading(2);

        public static readonly EditorCommand Heading3 =
            Heading(3);

        public static readonly EditorCommand SingleSpacing =
            new("line-single", "Single line spacing", _ => _.SpaceAsync(new(Line: 1)))
            {
                Key = "Ctrl+1"
            };

        public static readonly EditorCommand DoubleSpacing =
            new("line-double", "Double line spacing", _ => _.SpaceAsync(new(Line: 2)))
            {
                Key = "Ctrl+2"
            };

        public static readonly EditorCommand OneAndAHalfSpacing =
            new("line-one-and-half", "1.5 line spacing", _ => _.SpaceAsync(new(Line: 1.5)))
            {
                Key = "Ctrl+5"
            };

        public static readonly EditorCommand Delete =
            new("delete", "Delete", _ => _.DeleteAsync())
            {
                Key = "Delete"
            };

        // Every command, by the name the script hands in. Last in the file, so that every field above is
        // set by the time it is.
        static readonly Dictionary<string, EditorCommand> all =
            new[]
            {
                Undo, Redo, Paste, Cut, Copy, FormatPainter, Font, FontSize, Grow, Shrink, ClearFormatting, Bold, Italic,
                Underline, Strike, Subscript, Superscript, Highlight, Highlights, FontColour, FontColours, FontDialog,
                Bullets, BulletLibrary, Numbering, NumberingLibrary, RestartNumbering, ContinueNumbering, DecreaseIndent,
                IncreaseIndent, Marks, AlignLeft, AlignCenter, AlignRight, Justify, Spacing, ParagraphShading,
                ParagraphShadings, ParagraphBorders, MoreParagraphBorders, ParagraphDialog, Styles, Find, Replace,
                PageBreak, Table, Picture, Link, RemoveLink, Bookmark, Header, Footer, PageNumber, Date, Margins,
                Orientation, PaperSize, Columns, Breaks, Wrap, Contents, UpdateContents, Footnote, Endnote,
                InsertRowAbove, InsertRowBelow, InsertColumnLeft, InsertColumnRight, DeleteRow, DeleteColumn,
                DeleteTable, MergeCells, SplitCells, DistributeColumns, AutoFit, TableBorders, CellShading, CellShadings,
                WordCount, NewComment, ReviewPane, Track, AcceptAll, RejectAll, Pages, Ruler, Present, ListLevelIn,
                ListLevelOut, EndList, Unindent, FollowOn, ResetPicture, Heading1, Heading2, Heading3, SingleSpacing,
                DoubleSpacing, OneAndAHalfSpacing, Delete
            }.ToDictionary(_ => _.Name, StringComparer.Ordinal);

        public static IReadOnlyCollection<EditorCommand> All =>
            all.Values;

        public static EditorCommand? Named(string name) =>
            all.GetValueOrDefault(name);

        static EditorCommand TableCommand(string name, string label, TableEdit change, Icon icon, string group) =>
            new(name, label, _ => _.EditTableAsync(change))
            {
                Icon = icon,
                Place = $"Table › {group}",
                Enabled = InATable
            };

        // Word's Ctrl+Alt+1 to 3.
        static EditorCommand Heading(int level) =>
            new(
                $"heading{level}",
                $"Heading {level}",
                _ => _.ApplyStyleAsync($"Heading{level}"))
            {
                Key = $"Ctrl+Alt+{level}"
            };
    }
}
