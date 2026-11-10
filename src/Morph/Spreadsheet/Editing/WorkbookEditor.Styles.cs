using S = DocumentFormat.OpenXml.Spreadsheet;

static partial class WorkbookEditor
{
    /// <summary>Formats a range's cells; nothing a formula reads changes, so nothing is worked out again.</summary>
    public static byte[] Format(byte[] xlsx, int sheet, SheetRange range, CellFormatChange change) =>
        Edit(xlsx, null, _ =>
        {
            var worksheet = Worksheet(_, sheet);
            var styles = new StyleBook(_.WorkbookPart!);
            foreach (var (row, column) in Places(worksheet, range))
            {
                var cell = CellAt(worksheet, row, column);
                var edges = change.Borders switch
                {
                    "all" => Edges.All,
                    "outside" => OutsideEdges(range, row, column),
                    "none" => Edges.None,
                    _ => (Edges?) null
                };
                cell.StyleIndex = Index(styles.Changed(cell.StyleIndex?.Value ?? 0, change, edges));
            }

            styles.Save();
        });

    // A style index as the file writes it: none for the default.
    static UInt32Value? Index(uint style)
    {
        if (style == 0)
        {
            return null;
        }

        return style;
    }

    // The places a format goes: every cell of a range of a size to fill, and only the cells there are
    // of a whole column or row (which Excel would format by the column's or row's own style instead).
    static IEnumerable<(int Row, int Column)> Places(S.Worksheet worksheet, SheetRange range)
    {
        if ((long) range.RowCount * range.ColumnCount <= 50_000)
        {
            for (var row = range.FirstRow; row <= range.LastRow; row++)
            {
                for (var column = range.FirstColumn; column <= range.LastColumn; column++)
                {
                    yield return (row, column);
                }
            }

            yield break;
        }

        foreach (var cell in CellsIn(worksheet, range).ToList())
        {
            yield return (CellReference.RowOf(cell.CellReference?.Value), CellReference.ColumnOf(cell.CellReference?.Value));
        }
    }

    [Flags]
    enum Edges
    {
        None = 0,
        Left = 1,
        Right = 2,
        Top = 4,
        Bottom = 8,
        All = Left | Right | Top | Bottom,

        /// <summary>The edges not named are kept as the cell has them, rather than taken away.</summary>
        Keep = 16
    }

    static Edges OutsideEdges(SheetRange range, int row, int column)
    {
        var edges = Edges.None;
        if (column == range.FirstColumn)
        {
            edges |= Edges.Left;
        }

        if (column == range.LastColumn)
        {
            edges |= Edges.Right;
        }

        if (row == range.FirstRow)
        {
            edges |= Edges.Top;
        }

        if (row == range.LastRow)
        {
            edges |= Edges.Bottom;
        }

        // The edges a cell inside has already are its own, kept.
        return edges | Edges.Keep;
    }

    /// <summary>
    /// The workbook's styles, as an edit changes them: a cell's look is changed by making the cell
    /// format (<c>xf</c>) it would have — its font, fill or border made too where they change — and
    /// pointing the cell at it, an identical one already in the file used rather than added again.
    /// </summary>
    sealed class StyleBook
    {
        readonly S.Stylesheet stylesheet;
        readonly Dictionary<(uint, CellFormatChange, Edges?), uint> made = [];
        Dictionary<string, uint>? formats;
        Dictionary<string, uint>? fonts;
        Dictionary<string, uint>? fills;
        Dictionary<string, uint>? borders;

        public StyleBook(WorkbookPart workbookPart)
        {
            var part = workbookPart.WorkbookStylesPart ?? workbookPart.AddNewPart<WorkbookStylesPart>();
            part.Stylesheet ??= Minimal();
            stylesheet = part.Stylesheet;
            stylesheet.Fonts ??= new(new S.Font(new S.FontSize { Val = 11 }, new S.FontName { Val = "Calibri" }));
            stylesheet.Fills ??= new(
                new S.Fill(new S.PatternFill { PatternType = S.PatternValues.None }),
                new S.Fill(new S.PatternFill { PatternType = S.PatternValues.Gray125 }));
            stylesheet.Borders ??= new(new S.Border(new S.LeftBorder(), new S.RightBorder(), new S.TopBorder(), new S.BottomBorder(), new S.DiagonalBorder()));
            stylesheet.CellFormats ??= new(new S.CellFormat { NumberFormatId = 0, FontId = 0, FillId = 0, BorderId = 0 });
        }

        // The least stylesheet Excel opens: one font, the two fills it reserves, one border, one format.
        static S.Stylesheet Minimal() =>
            new(
                new S.Fonts(new S.Font(new S.FontSize { Val = 11 }, new S.FontName { Val = "Calibri" })),
                new S.Fills(
                    new S.Fill(new S.PatternFill { PatternType = S.PatternValues.None }),
                    new S.Fill(new S.PatternFill { PatternType = S.PatternValues.Gray125 })),
                new S.Borders(new S.Border(new S.LeftBorder(), new S.RightBorder(), new S.TopBorder(), new S.BottomBorder(), new S.DiagonalBorder())),
                new S.CellStyleFormats(new S.CellFormat { NumberFormatId = 0, FontId = 0, FillId = 0, BorderId = 0 }),
                new S.CellFormats(new S.CellFormat { NumberFormatId = 0, FontId = 0, FillId = 0, BorderId = 0, FormatId = 0 }),
                new S.CellStyles(new S.CellStyle { Name = "Normal", FormatId = 0, BuiltinId = 0 }));

        /// <summary>The number format code a style shows its cells' values in.</summary>
        public string? FormatCode(uint style)
        {
            var format = stylesheet.CellFormats?.Elements<S.CellFormat>().ElementAtOrDefault((int) style);
            var id = format?.NumberFormatId?.Value ?? 0;
            if (BuiltinNumberFormats.Code((int) id) is { } builtin)
            {
                return builtin;
            }

            return stylesheet.NumberingFormats?
                .Elements<S.NumberingFormat>()
                .FirstOrDefault(_ => _.NumberFormatId?.Value == id)?
                .FormatCode?.Value;
        }

        /// <summary>The style a cell of a style has once the change is made to it.</summary>
        public uint Changed(uint style, CellFormatChange change, Edges? edges = null)
        {
            if (made.TryGetValue((style, change, edges), out var known))
            {
                return known;
            }

            var formatsList = stylesheet.CellFormats!.Elements<S.CellFormat>().ToList();
            var source = formatsList.ElementAtOrDefault((int) style) ?? formatsList[0];
            var format = (S.CellFormat) source.CloneNode(true);

            if (change.Bold != null ||
                change.Italic != null ||
                change.Underline != null ||
                change.Strikethrough != null ||
                change.FontFamily != null ||
                change.FontSizePoints != null ||
                change.ColorHex != null)
            {
                format.FontId = Font(format.FontId?.Value ?? 0, change);
                format.ApplyFont = true;
            }

            if (change.FillHex != null)
            {
                format.FillId = Fill(change.FillHex);
                format.ApplyFill = true;
            }

            if (edges is { } wanted)
            {
                format.BorderId = Border(format.BorderId?.Value ?? 0, wanted);
                format.ApplyBorder = true;
            }

            if (change.Horizontal != null ||
                change.Vertical != null ||
                change.Wrap != null)
            {
                var alignment = format.Alignment ?? new S.Alignment();
                if (change.Horizontal is { } horizontal)
                {
                    alignment.Horizontal = horizontal switch
                    {
                        "left" => S.HorizontalAlignmentValues.Left,
                        "center" => S.HorizontalAlignmentValues.Center,
                        "right" => S.HorizontalAlignmentValues.Right,
                        "justify" => S.HorizontalAlignmentValues.Justify,
                        _ => null
                    };
                }

                if (change.Vertical is { } vertical)
                {
                    alignment.Vertical = vertical switch
                    {
                        "top" => S.VerticalAlignmentValues.Top,
                        "center" => S.VerticalAlignmentValues.Center,
                        _ => null
                    };
                }

                if (change.Wrap == true)
                {
                    alignment.WrapText = true;
                }
                else if (change.Wrap == false)
                {
                    alignment.WrapText = null;
                }

                format.Alignment = alignment.HasAttributes ? alignment : null;
                format.ApplyAlignment = true;
            }

            if (change.NumberFormat is { } code)
            {
                format.NumberFormatId = NumberFormat(code);
                format.ApplyNumberFormat = true;
            }

            var index = Add(stylesheet.CellFormats!, format, ref formats);
            made[(style, change, edges)] = index;
            return index;
        }

        uint Font(uint fontId, CellFormatChange change)
        {
            var list = stylesheet.Fonts!.Elements<S.Font>().ToList();
            var font = (S.Font) (list.ElementAtOrDefault((int) fontId) ?? list[0]).CloneNode(true);
            Toggle<S.Bold>(font, change.Bold);
            Toggle<S.Italic>(font, change.Italic);
            Toggle<S.Strike>(font, change.Strikethrough);
            if (change.Underline is { } underline)
            {
                font.RemoveAllChildren<S.Underline>();
                if (underline)
                {
                    font.AppendChild(new S.Underline());
                }
            }

            if (change.FontSizePoints is { } size)
            {
                font.RemoveAllChildren<S.FontSize>();
                font.AppendChild(new S.FontSize { Val = size });
            }

            if (change.ColorHex is { } color)
            {
                font.RemoveAllChildren<S.Color>();
                if (color.Length == 6)
                {
                    font.AppendChild(new S.Color { Rgb = "FF" + color.ToUpperInvariant() });
                }
            }

            if (change.FontFamily is { } family)
            {
                font.RemoveAllChildren<S.FontName>();
                // A theme's font (scheme minor or major) wins over the name, so it goes.
                font.RemoveAllChildren<S.FontScheme>();
                font.RemoveAllChildren<S.FontFamilyNumbering>();
                font.AppendChild(new S.FontName { Val = family });
            }

            Order(font);
            return Add(stylesheet.Fonts!, font, ref fonts);
        }

        static void Toggle<T>(S.Font font, bool? on)
            where T : OpenXmlElement, new()
        {
            if (on is not { } value)
            {
                return;
            }

            font.RemoveAllChildren<T>();
            if (value)
            {
                font.AppendChild(new T());
            }
        }

        // The order Excel writes a font's parts in, which it reads in any order but writes in this one.
        static readonly string[] fontOrder = ["b", "i", "strike", "condense", "extend", "outline", "shadow", "u", "vertAlign", "sz", "color", "name", "family", "charset", "scheme"];

        static void Order(S.Font font)
        {
            var children = font.ChildElements.ToList();
            foreach (var child in children)
            {
                child.Remove();
            }

            foreach (var child in children.OrderBy(FontRank))
            {
                font.AppendChild(child);
            }
        }

        static int FontRank(OpenXmlElement child)
        {
            var at = Array.IndexOf(fontOrder, child.LocalName);
            if (at < 0)
            {
                return fontOrder.Length;
            }

            return at;
        }

        uint Fill(string hex)
        {
            if (hex.Length == 0)
            {
                return 0;
            }

            var fill = new S.Fill(new S.PatternFill(
                new S.ForegroundColor { Rgb = "FF" + hex.ToUpperInvariant() },
                new S.BackgroundColor { Indexed = 64 })
            {
                PatternType = S.PatternValues.Solid
            });
            return Add(stylesheet.Fills!, fill, ref fills);
        }

        uint Border(uint borderId, Edges edges)
        {
            if (edges == Edges.None)
            {
                return 0;
            }

            var list = stylesheet.Borders!.Elements<S.Border>().ToList();
            var keep = edges.HasFlag(Edges.Keep);
            var existing = list.ElementAtOrDefault((int) borderId) ?? list[0];
            var border = new S.Border(
                Edge<S.LeftBorder>(existing.LeftBorder, keep, edges.HasFlag(Edges.Left)),
                Edge<S.RightBorder>(existing.RightBorder, keep, edges.HasFlag(Edges.Right)),
                Edge<S.TopBorder>(existing.TopBorder, keep, edges.HasFlag(Edges.Top)),
                Edge<S.BottomBorder>(existing.BottomBorder, keep, edges.HasFlag(Edges.Bottom)),
                new S.DiagonalBorder());
            return Add(stylesheet.Borders!, border, ref borders);
        }

        static T Edge<T>(S.BorderPropertiesType? existing, bool keep, bool thin)
            where T : S.BorderPropertiesType, new()
        {
            if (thin)
            {
                return new()
                {
                    Style = S.BorderStyleValues.Thin,
                    Color = new()
                    {
                        Auto = true
                    }
                };
            }

            if (keep &&
                existing != null)
            {
                var copy = new T
                {
                    Style = existing.Style
                };
                if (existing.Color?.CloneNode(true) is S.Color color)
                {
                    copy.Color = color;
                }

                return copy;
            }

            return new();
        }

        uint NumberFormat(string code)
        {
            if (BuiltinNumberFormats.Id(code) is { } builtin)
            {
                return (uint) builtin;
            }

            var numbering = stylesheet.NumberingFormats;
            if (numbering == null)
            {
                numbering = new();
                stylesheet.InsertAt(numbering, 0);
            }

            foreach (var each in numbering.Elements<S.NumberingFormat>())
            {
                if (each.FormatCode?.Value == code &&
                    each.NumberFormatId?.Value is { } id)
                {
                    return id;
                }
            }

            var next = Math.Max(163u, numbering.Elements<S.NumberingFormat>().Select(_ => _.NumberFormatId?.Value ?? 0).DefaultIfEmpty(0u).Max()) + 1;
            numbering.AppendChild(new S.NumberingFormat { NumberFormatId = next, FormatCode = code });
            numbering.Count = (uint) numbering.Elements<S.NumberingFormat>().Count();
            return next;
        }

        // An element added to a list, or the identical one the list holds already.
        static uint Add(OpenXmlCompositeElement list, OpenXmlElement element, ref Dictionary<string, uint>? known)
        {
            if (known == null)
            {
                known = new(StringComparer.Ordinal);
                var index = 0u;
                foreach (var child in list.ChildElements.Where(_ => _.GetType() == element.GetType()))
                {
                    known.TryAdd(child.OuterXml, index++);
                }
            }

            var xml = element.OuterXml;
            if (known.TryGetValue(xml, out var found))
            {
                return found;
            }

            var count = (uint) list.ChildElements.Count(_ => _.GetType() == element.GetType());
            list.AppendChild(element);
            known[xml] = count;
            return count;
        }

        /// <summary>Each list's count kept true.</summary>
        public void Save()
        {
            stylesheet.Fonts!.Count = (uint) stylesheet.Fonts.Elements<S.Font>().Count();
            stylesheet.Fills!.Count = (uint) stylesheet.Fills.Elements<S.Fill>().Count();
            stylesheet.Borders!.Count = (uint) stylesheet.Borders.Elements<S.Border>().Count();
            stylesheet.CellFormats!.Count = (uint) stylesheet.CellFormats.Elements<S.CellFormat>().Count();
        }
    }
}
