using A = DocumentFormat.OpenXml.Drawing;
using DW = DocumentFormat.OpenXml.Drawing.Wordprocessing;
using OoxmlParagraphProperties = DocumentFormat.OpenXml.Wordprocessing.ParagraphProperties;
using OoxmlRun = DocumentFormat.OpenXml.Wordprocessing.Run;
using OoxmlFieldCode = DocumentFormat.OpenXml.Wordprocessing.FieldCode;
using OoxmlTabStop = DocumentFormat.OpenXml.Wordprocessing.TabStop;
using OoxmlTabs = DocumentFormat.OpenXml.Wordprocessing.Tabs;
using WPG = DocumentFormat.OpenXml.Office2010.Word.DrawingGroup;
using WPS = DocumentFormat.OpenXml.Office2010.Word.DrawingShape;

/// <summary>
/// The body walk and a paragraph: its properties, tabs, numbering state and fields.
/// </summary>
sealed partial class DocumentParser
{
    NumberingInfo? GetNumberingInfo(OoxmlParagraphProperties? paraProps, string? styleId)
    {
        if (numberingDefinitions == null ||
            numberingDefinitions.Count == 0)
        {
            return null;
        }

        var numId = 0;
        var ilvl = 0;

        // First check for direct numPr on paragraph
        var numPr = paraProps?.GetFirstChild<NumberingProperties>();
        if (numPr != null)
        {
            numId = numPr.NumberingId?.Val?.Value ?? 0;
            ilvl = numPr.NumberingLevelReference?.Val?.Value ?? 0;
        }
        // Fall back to style numbering
        else if (styleId != null &&
                 styleNumbering != null &&
                 styleNumbering.TryGetValue(styleId, out var styleNumInfo))
        {
            numId = styleNumInfo.numId;
            ilvl = styleNumInfo.ilvl;
        }

        if (numId == 0)
        {
            return null;
        }

        if (!numberingDefinitions.TryGetValue(numId, out var levels))
        {
            return null;
        }

        if (!levels.TryGetValue(ilvl, out var levelDef))
        {
            return null;
        }

        string text;
        string? levelNumber = null;
        if (levelDef.IsBullet)
        {
            // For bullets, the level text IS the bullet character. Word stores it in
            // the level's font's Private Use Area encoding - the same PUA codepoint
            // means a *different glyph* in Symbol vs Wingdings vs Wingdings 2 (e.g.
            // U+F0A7 is a club suit in Symbol but a small filled square in Wingdings).
            // Map to Unicode based on the level's declared font, not just the codepoint.
            text = NumberingParser.MapBulletPuaToUnicode(levelDef.LevelText, levelDef.FontFamily);

            if (string.IsNullOrEmpty(text))
            {
                text = "•";
            }
        }
        else
        {
            // The counter is shared across every numId referencing the same abstract; a numId's
            // w:startOverride resets it once, at that numId's first use (Word's per-table restart).
            var abstractId = numberingAbstractIds.GetValueOrDefault(numId, numId);
            var counterKey = (abstractId, ilvl);
            int counter;
            if (numberingStartOverrides.Contains((numId, ilvl)) &&
                appliedStartOverrides.Add((numId, ilvl)))
            {
                counter = levelDef.StartNumber;
            }
            else if (!numberingCounters.TryGetValue(counterKey, out counter))
            {
                counter = levelDef.StartNumber;
            }
            else
            {
                counter++;
            }

            numberingCounters[counterKey] = counter;
            levelNumber = NumberingParser.FormatNumber(counter, levelDef.NumberFormat);

            // OOXML default: incrementing level N restarts every deeper level of the
            // same numId, so e.g. a fresh "II." re-starts its child a/b counter.
            // <w:lvlRestart w:val="0"/> on a deeper level opts out; <w:lvlRestart w:val="K"/>
            // means that level only restarts when a level <= K-1 increments, so any
            // deeper level whose lvlRestart-1 is less than ilvl stays put.
            ResetDeeperCounters(abstractId, ilvl, levels);

            // Replace %N placeholders with formatted counter values
            text = levelDef.LevelText;
            for (var lvl = 0; lvl <= ilvl; lvl++)
            {
                var placeholder = $"%{lvl + 1}";
                if (!text.Contains(placeholder))
                {
                    continue;
                }

                int lvlCounter;
                if (lvl == ilvl)
                {
                    lvlCounter = counter;
                }
                else
                {
                    // For parent levels, use the current counter (or start if not yet seen)
                    var parentKey = (abstractId, lvl);
                    lvlCounter = numberingCounters.GetValueOrDefault(parentKey, levelDef.StartNumber);
                }

                var numFmt = levelDef.NumberFormat;
                // Try to get the actual format for parent levels
                if (lvl != ilvl &&
                    levels.TryGetValue(lvl, out var parentLevelDef))
                {
                    numFmt = parentLevelDef.NumberFormat;
                }

                text = text.Replace(placeholder, NumberingParser.FormatNumber(lvlCounter, numFmt));
            }
        }

        return new()
        {
            Text = text,
            LevelNumber = levelNumber,
            Level = ilvl,
            FontFamily = levelDef.FontFamily,
            FontSizePoints = levelDef.FontSizePoints,
            ColorHex = levelDef.ColorHex,
            IndentPoints = levelDef.LeftIndentPoints,
            HangingIndentPoints = levelDef.HangingIndentPoints,
            MarkerRightAligned = levelDef.RightAligned,
            Suffix = levelDef.Suffix,
            Format = levelDef.IsBullet ? ListNumberFormat.Bullet : NumberingParser.MapNumberFormat(levelDef.NumberFormat)
        };
    }

    void ResetDeeperCounters(int abstractId, int incrementedIlvl, Dictionary<int, NumberingLevelDefinition> levels)
    {
        foreach (var (deeperIlvl, deeperLevel) in levels)
        {
            if (deeperIlvl <= incrementedIlvl)
            {
                continue;
            }

            // lvlRestart=0 means "never restart" - the counter persists across parent changes.
            if (deeperLevel.LevelRestart == 0)
            {
                continue;
            }

            // lvlRestart=K means "restart when an item at level <= K-1 increments".
            // If our incrementedIlvl is deeper than K-1, this level keeps its value.
            if (deeperLevel.LevelRestart is { } restart and > 0 &&
                incrementedIlvl > restart - 1)
            {
                continue;
            }

            numberingCounters.Remove((abstractId, deeperIlvl));
        }
    }

    // w:spacing/@w:beforeLines or @w:afterLines in points: hundredths of a line, a line being the
    // document grid's pitch, or 12pt off a grid. Word-probed (_probe_grid_*, 2026-10-21): a positive
    // value outranks the twips value beside it, and 0 is ignored — agendas-minutes/17's
    // afterLines="0" beside after="40" keeps its 2pt.
    double? LineUnitsToPoints(Int32Value? lines)
    {
        if (lines?.Value is not > 0)
        {
            return null;
        }

        var line = documentGridPitchPoints > 0 ? documentGridPitchPoints : 12;
        return lines.Value / 100.0 * line;
    }

    /// <summary>
    /// Drops page breaks synthesized from <c>w:lastRenderedPageBreak</c> hints when they land on
    /// a boundary the flow already breaks at — right after an explicit page break or a section
    /// break that starts a new page. Word writes the hint into the first run of each rendered
    /// page, so the marker that follows such a boundary records the SAME page start; honouring
    /// it manufactured a blank page Word never produced (business-plans/13: the cover's nextPage
    /// sectPr is immediately followed by the TOC page's hint). Floating/behind-text elements
    /// between them don't advance the flow, so they are looked past.
    /// </summary>
    static void RemoveRedundantPaginationHintBreaks(List<DocumentElement> elements)
    {
        for (var i = elements.Count - 1; i >= 0; i--)
        {
            if (elements[i] is not PageBreakElement {FromPaginationHint: true})
            {
                continue;
            }

            for (var j = i - 1; j >= 0; j--)
            {
                var previous = elements[j];
                if (previous is FloatingImageElement or FloatingShapeElement or FloatingTextBoxElement or FloatingWordArtElement)
                {
                    continue;
                }

                if (previous is PageBreakElement
                    or SectionBreakElement {BreakType: SectionBreakType.NextPage or SectionBreakType.EvenPage or SectionBreakType.OddPage})
                {
                    elements.RemoveAt(i);
                }

                break;
            }
        }
    }

    List<DocumentElement> ParseElements(Body body, MainDocumentPart mainPart)
    {
        var elements = new List<DocumentElement>();

        foreach (var element in body.ChildElements)
        {
            switch (element)
            {
                case Paragraph para:
                    var parsedElements = ParseParagraph(para, mainPart);
                    elements.AddRange(parsedElements);
                    break;

                case Table table:
                    var parsedTable = ParseTable(table, mainPart);
                    if (parsedTable != null)
                    {
                        elements.Add(parsedTable);
                    }

                    break;

                case AltChunk altChunk:
                    var altChunkElements = ParseAltChunk(altChunk, mainPart);
                    elements.AddRange(altChunkElements);
                    break;

                case SdtBlock sdtBlock:
                    // Block-level content control at document body level - extract and parse its content
                    foreach (var sdtChild in sdtBlock.SdtContentBlock?.ChildElements ?? [])
                    {
                        if (sdtChild is Paragraph sdtPara)
                        {
                            elements.AddRange(ParseParagraph(sdtPara, mainPart));
                        }
                        else if (sdtChild is Table sdtTable)
                        {
                            var parsedSdtTable = ParseTable(sdtTable, mainPart);
                            if (parsedSdtTable != null)
                            {
                                elements.Add(parsedSdtTable);
                            }
                        }
                    }

                    break;
            }
        }

        RemoveRedundantPaginationHintBreaks(elements);

        return elements;
    }

    List<DocumentElement> ParseAltChunk(AltChunk altChunk, MainDocumentPart mainPart)
    {
        if (altChunk.Id?.Value == null)
        {
            return [];
        }

        var part = mainPart.PartOrNull(altChunk.Id.Value);
        if (part is AlternativeFormatImportPart altPart)
        {
            using var stream = altPart.GetStream(FileMode.Open, FileAccess.Read);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            var html = reader.ReadToEnd();

            // Body text keeps the browser-default serif Word imports AltChunk paragraphs in, while
            // table cells and list items take the host document's default — Word lays those out in
            // the destination Normal, not the serif. Verified by Word probe (see HtmlParser).
            return HtmlParser.Parse(html, "Times New Roman", effectiveDefaultFont);
        }

        return [];
    }

    static void SortFloatingBatchesByZ(List<DocumentElement> elements)
    {
        var start = -1;
        for (var i = 0; i <= elements.Count; i++)
        {
            var isFloating = i < elements.Count &&
                             elements[i] is FloatingShapeElement or FloatingImageElement or FloatingTextBoxElement or FloatingWordArtElement;
            if (isFloating)
            {
                if (start < 0)
                {
                    start = i;
                }

                continue;
            }

            if (start >= 0 &&
                i - start > 1)
            {
                var sorted = elements.GetRange(start, i - start)
                    .OrderBy(FloatingZOrder)
                    .ToList();
                for (var j = 0; j < sorted.Count; j++)
                {
                    elements[start + j] = sorted[j];
                }
            }

            start = -1;
        }
    }

    static uint FloatingZOrder(DocumentElement element) => element switch
    {
        FloatingShapeElement shape => shape.RelativeHeight,
        FloatingImageElement image => image.RelativeHeight,
        FloatingTextBoxElement textBox => textBox.RelativeHeight,
        FloatingWordArtElement wordArt => wordArt.RelativeHeight,
        _ => 0
    };

    /// <summary>
    /// Cascades a tblStylePr-derived run colour onto the runs in a freshly-parsed cell.
    /// Only fills in runs that don't carry an explicit <c>w:color</c> — explicit run colour
    /// always wins. Nested tables are skipped because they run their own conditional cascade.
    /// </summary>
    static void ApplyDefaultRunColor(List<DocumentElement> cellContent, string color)
    {
        for (var i = 0; i < cellContent.Count; i++)
        {
            if (cellContent[i] is ParagraphElement paragraph)
            {
                cellContent[i] = ApplyDefaultRunColorToParagraph(paragraph, color);
            }
        }
    }

    static ParagraphElement ApplyDefaultRunColorToParagraph(ParagraphElement paragraph, string color)
    {
        Run[]? rebuilt = null;
        for (var i = 0; i < paragraph.Runs.Count; i++)
        {
            var run = paragraph.Runs[i];
            if (run.Properties.ColorHex != null)
            {
                continue;
            }

            rebuilt ??= [.. paragraph.Runs];
            // WithProperties preserves every member: hyperlink targets, note ids, page-field markers.
            rebuilt[i] = run.WithProperties(
                run.Properties with
                {
                    ColorHex = color
                });
        }

        if (rebuilt == null)
        {
            return paragraph;
        }

        return new()
        {
            Runs = rebuilt,
            Properties = paragraph.Properties
        };
    }

    BorderEdge ParseBorderEdge(BorderType? border) =>
        BorderParser.ParseBorderEdge(border, currentThemeColors);

    // Per-field parse state for the PAGE/NUMPAGES/SECTIONPAGES tracker (see HandleFieldControlRun
    // in ParseParagraph). A stack of these follows the fldChar begin…separate…end nesting so a
    // page field wrapped in another field still resolves.
    sealed class FieldParseState
    {
        public readonly StringBuilder Instruction = new();
        public bool CollectingResult;
        public PageFieldKind Kind;
        public string? NumberFormat;
        public string? Bookmark;
        public readonly StringBuilder ResultText = new();
        public RunProperties? ResultProperties;
        public RunProperties? SeparateProperties;
    }

    /// <summary>
    /// Classifies a field instruction as a page-numbering field and extracts its <c>\*</c> numeric
    /// format switch. Returns <see cref="PageFieldKind.None"/> for everything else.
    /// </summary>
    static (PageFieldKind Kind, string? NumberFormat, string? Bookmark) ClassifyPageField(string? instruction)
    {
        if (instruction == null)
        {
            return (PageFieldKind.None, null, null);
        }

        var span = instruction.AsSpan().Trim();
        if (span.IsEmpty)
        {
            return (PageFieldKind.None, null, null);
        }

        var space = span.IndexOfAny(' ', '\t');
        var keyword = (space < 0 ? span : span[..space]).ToString().ToUpperInvariant();
        var kind = keyword switch
        {
            "PAGE" => PageFieldKind.Page,
            "NUMPAGES" => PageFieldKind.NumberOfPages,
            "SECTIONPAGES" => PageFieldKind.SectionPages,
            "PAGEREF" => PageFieldKind.PageReference,
            _ => PageFieldKind.None
        };

        // A PAGEREF names its bookmark first. One that asks for "above"/"below" (\p) reads as text,
        // which the layout does not write, so it keeps its cached result.
        string? bookmark = null;
        if (kind == PageFieldKind.PageReference)
        {
            var parts = span.ToString().Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2 ||
                parts[1].StartsWith('\\') ||
                parts.Contains("\\p"))
            {
                return (PageFieldKind.None, null, null);
            }

            bookmark = parts[1].Trim('"');
        }

        if (kind == PageFieldKind.None)
        {
            return (PageFieldKind.None, null, null);
        }

        // A "\* <format>" switch overrides the default decimal rendering. MERGEFORMAT / CHARFORMAT
        // are formatting-preservation switches, not numeric formats, so they leave the default.
        string? numberFormat = null;
        var star = instruction.IndexOf("\\*", StringComparison.Ordinal);
        if (star >= 0)
        {
            var rest = instruction.AsSpan(star + 2).TrimStart();
            var end = rest.IndexOfAny(' ', '\t');
            var token = (end < 0 ? rest : rest[..end]).ToString();
            if (token is "roman" or "Roman" or "ROMAN"
                or "alphabetic" or "Alphabetic" or "ALPHABETIC"
                or "arabic" or "Arabic" or "ARABIC")
            {
                numberFormat = token;
            }
        }

        return (kind, numberFormat, bookmark);
    }

    /// <summary>
    /// Whether a mid-paragraph flush would emit anything worth calling a paragraph.
    ///
    /// Anchored art splits a paragraph's runs so the art keeps its place in document order. Runs made
    /// only of tabs are not a paragraph though: flushed, the tab is stranded from the text it advances
    /// (a header marking centred by a leading <c>w:tab</c>, an anchored rule between them, lands at the
    /// left margin under a phantom blank line). Holding them back costs no ordering, since the art they
    /// surround is out of flow anyway.
    /// </summary>
    static bool IsTabOnly(List<Run> runs)
    {
        foreach (var run in runs)
        {
            if (!run.IsTab)
            {
                return false;
            }
        }

        return true;
    }

    List<DocumentElement> ParseParagraph(Paragraph para, MainDocumentPart mainPart)
    {
        var result = new List<DocumentElement>();

        var paraProps = para.ParagraphProperties;
        var sectionProps = paraProps?.GetFirstChild<SectionProperties>();
        SectionBreakElement? sectionBreak = null;
        if (sectionProps != null)
        {
            sectionBreak = ParseSectionBreak(sectionProps);
        }

        var runs = new List<Run>();

        var paragraphStyleId = paraProps?.ParagraphStyleId?.Val?.Value;
        var inScopedPart = para.Ancestors<DocumentFormat.OpenXml.Wordprocessing.TableCell>().Any() ||
                           para.Ancestors<Header>().Any() ||
                           para.Ancestors<Footer>().Any();
        var props = ParseParagraphProperties(paraProps, mainPart, paragraphStyleId, omitParagraphMark: inScopedPart, paragraph: para);

        // --- PAGE / NUMPAGES / SECTIONPAGES field tracking ---
        // Word emits these as complex fields: fldChar begin → instrText → fldChar separate →
        // cached-result run(s) → fldChar end. The cached result is a fixed string (wrong on every
        // page but the one Word computed it for), so we collapse each page field's result into a
        // single Run tagged with its PageFieldKind; the renderers substitute the live value. The
        // tracker is shared by the run loops below (plain runs and inline SDT content).
        var fieldStack = new Stack<FieldParseState>();

        // Handles a field-control run (fldChar begin/separate/end or instrText). Returns true when
        // the run was consumed as field plumbing (it carries no visible text). On a page field's
        // fldChar end it appends the single tagged result run to <paramref name="runs"/>.
        bool HandleFieldControlRun(OoxmlRun controlRun)
        {
            var fieldChar = controlRun.GetFirstChild<FieldChar>();
            if (fieldChar != null)
            {
                var type = fieldChar.FieldCharType?.Value;
                if (type == FieldCharValues.Begin)
                {
                    fieldStack.Push(new());
                }
                else if (type == FieldCharValues.Separate &&
                         fieldStack.Count > 0)
                {
                    var state = fieldStack.Peek();
                    (state.Kind, state.NumberFormat, state.Bookmark) = ClassifyPageField(state.Instruction.ToString());
                    if (state.Kind != PageFieldKind.None)
                    {
                        state.CollectingResult = true;
                        state.SeparateProperties = ParseRunProperties(controlRun.RunProperties, mainPart, paragraphStyleId);
                    }
                }
                else if (type == FieldCharValues.End &&
                         fieldStack.Count > 0)
                {
                    var state = fieldStack.Pop();
                    if (state.Kind != PageFieldKind.None)
                    {
                        runs.Add(
                            new()
                            {
                                Text = state.ResultText.ToString(),
                                Properties = state.ResultProperties
                                             ?? state.SeparateProperties
                                             ?? ParseRunProperties(null, mainPart, paragraphStyleId),
                                PageField = state.Kind,
                                PageFieldNumberFormat = state.NumberFormat,
                                PageFieldBookmark = state.Bookmark
                            });

                        if (state.Kind is PageFieldKind.NumberOfPages or PageFieldKind.SectionPages)
                        {
                            requiresTotalPageCount = true;
                        }
                    }
                }

                return true;
            }

            var instruction = controlRun.GetFirstChild<OoxmlFieldCode>();
            if (instruction != null &&
                fieldStack.Count > 0)
            {
                fieldStack.Peek().Instruction.Append(instruction.Text);
                return true;
            }

            return false;
        }

        // While a page field is between its separate and end, capture the cached result text (kept
        // for the exporters) and its formatting, and report that the run must not be emitted here —
        // HandleFieldControlRun emits one tagged run at the field's end instead.
        bool CaptureSuppressedFieldResult(OoxmlRun resultRun)
        {
            if (fieldStack.Count == 0 || !fieldStack.Peek().CollectingResult)
            {
                return false;
            }

            var state = fieldStack.Peek();
            foreach (var text in resultRun.Descendants<Text>())
            {
                state.ResultText.Append(text.Text.ReplaceSeparatorsWithSpace());
            }

            state.ResultProperties ??= ParseRunProperties(resultRun.RunProperties, mainPart, paragraphStyleId);
            return true;
        }

        // Check for numbering (direct on paragraph or from style)
        var numberingInfo = GetNumberingInfo(paraProps, paragraphStyleId);
        if (numberingInfo != null)
        {
            // OOXML indent cascade for a numbered paragraph. Each component (left / hanging)
            // resolves independently — a layer may set only one of them and inherit the rest —
            // and the numbering level's <w:ind> sits at a different layer depending on how the
            // numbering was attached:
            //   - numPr directly on the paragraph: the level's indent behaves like direct
            //     formatting and overrides the style's (dot_points: ListParagraph's flat 720
            //     loses to the level's 720..4320 progression, matching Word).
            //   - numPr inherited from the style: the style definition is self-consistent —
            //     its own <w:ind> deliberately overrides the level it references
            //     (agendas-minutes/17: the Bullets style tightens the level's 1800 to 360).
            // Direct paragraph <w:ind> wins over both.
            ParagraphProperties? styleDefaults = null;
            if (paragraphStyleId != null &&
                styleParagraphProperties != null)
            {
                styleParagraphProperties.TryGetValue(paragraphStyleId, out styleDefaults);
            }

            var directInd = paraProps?.GetFirstChild<Indentation>();
            var directHasLeft = directInd?.Left?.HasValue == true;
            var directHasHanging = directInd?.Hanging?.HasValue == true;

            // Within this block the numbering resolved, so a present direct numPr is what
            // supplied it (a direct numPr with numId 0 removes numbering entirely and never
            // reaches here).
            var numberingIsDirect = paraProps?.GetFirstChild<NumberingProperties>() != null;

            // A layer "has" the value when it's non-zero - we can't distinguish "explicitly 0"
            // from "default 0" without re-parsing, but in practice any zero indent means the
            // layer didn't set one and the next should fill in.
            var (styleIndentsLeft, styleIndentsHanging) = paragraphStyleId == null ? (true, true) : StyleIndentBeforeList(paragraphStyleId);
            var styleHasLeft = styleIndentsLeft && (styleDefaults?.LeftIndentPoints ?? 0) > 0;
            var styleHasHanging = styleIndentsHanging && (styleDefaults?.HangingIndentPoints ?? 0) > 0;
            var numberingHasLeft = numberingInfo.IndentPoints > 0;
            var numberingHasHanging = numberingInfo.HangingIndentPoints > 0;

            double leftIndent;
            double hangingIndent;
            if (numberingIsDirect)
            {
                leftIndent = directHasLeft ? props.LeftIndentPoints
                    : numberingHasLeft ? numberingInfo.IndentPoints
                    : styleDefaults?.LeftIndentPoints ?? 0;
                hangingIndent = directHasHanging ? props.HangingIndentPoints
                    : numberingHasHanging ? numberingInfo.HangingIndentPoints
                    : styleDefaults?.HangingIndentPoints ?? 0;
            }
            else
            {
                leftIndent = directHasLeft ? props.LeftIndentPoints
                    : styleHasLeft ? styleDefaults!.LeftIndentPoints
                    : numberingInfo.IndentPoints;
                hangingIndent = directHasHanging ? props.HangingIndentPoints
                    : styleHasHanging ? styleDefaults!.HangingIndentPoints
                    : numberingInfo.HangingIndentPoints;
            }

            props = props with
            {
                Numbering = numberingInfo,
                LeftIndentPoints = leftIndent,
                HangingIndentPoints = hangingIndent
            };
        }

        foreach (var child in para.ChildElements)
        {
            switch (child)
            {
                case SdtRun sdtRun:
                    if (IsContentControlType(sdtRun))
                    {
                        // Emit current paragraph content before the content control
                        if (runs.Count > 0)
                        {
                            result.Add(
                                new ParagraphElement
                                {
                                    Runs = new List<Run>(runs),
                                    Properties = props
                                });
                            runs.Clear();
                        }

                        var contentControl = ParseSdtRun(sdtRun, mainPart, paragraphStyleId);
                        if (contentControl != null)
                        {
                            result.Add(contentControl);
                        }

                        break;
                    }

                    // SdtRun is an inline (run-level) content control - extract its runs inline
                    var sdtRunContent = sdtRun.SdtContentRun;
                    if (sdtRunContent != null)
                    {
                        foreach (var sdtChildRun in sdtRunContent.Descendants<OoxmlRun>())
                        {
                            // Page-number fields (Word wraps them in a "Page Numbers" content
                            // control) resolve the same way as in the plain run loop.
                            if (HandleFieldControlRun(sdtChildRun) || CaptureSuppressedFieldResult(sdtChildRun))
                            {
                                continue;
                            }

                            var breakElement = sdtChildRun.GetFirstChild<Break>();
                            if (breakElement != null)
                            {
                                var breakType = breakElement.Type?.Value;
                                if (breakType == BreakValues.Page)
                                {
                                    if (runs.Count > 0)
                                    {
                                        result.Add(
                                            new ParagraphElement
                                            {
                                                Runs = new List<Run>(runs),
                                                Properties = props
                                            });
                                        runs.Clear();
                                    }

                                    result.Add(new PageBreakElement());
                                    continue;
                                }

                                if (breakType == BreakValues.Column)
                                {
                                    if (runs.Count > 0)
                                    {
                                        result.Add(
                                            new ParagraphElement
                                            {
                                                Runs = new List<Run>(runs),
                                                Properties = props
                                            });
                                        runs.Clear();
                                    }

                                    result.Add(new ColumnBreakElement());
                                    continue;
                                }

                                // Line break - add newline character. Don't `continue` — the
                                // run may also have <w:t> text after the break (e.g.
                                // <w:r><w:br/><w:t>Sharma</w:t></w:r>) and we still need to
                                // parse it so neither half of the run is dropped.
                                var runProps = ParseRunProperties(sdtChildRun.RunProperties, mainPart);
                                runs.Add(
                                    new()
                                    {
                                        Text = "\n",
                                        Properties = runProps
                                    });
                            }

                            // Drawings (images/icons) within the SdtRun child, collected once for
                            // the drawing loop and the text-content guard.
                            List<Drawing>? sdtRunDrawings = null;
                            foreach (var drawing in sdtChildRun.Descendants<Drawing>())
                            {
                                (sdtRunDrawings ??= []).Add(drawing);
                            }

                            foreach (var drawing in sdtRunDrawings ?? (IReadOnlyList<Drawing>) [])
                            {
                                var imageElements = ParseDrawingElements(drawing, mainPart);
                                if (imageElements.Count > 0)
                                {
                                    // Emit current paragraph content before the images
                                    if (runs.Count > 0)
                                    {
                                        result.Add(
                                            new ParagraphElement
                                            {
                                                Runs = new List<Run>(runs),
                                                Properties = props
                                            });
                                        runs.Clear();
                                    }

                                    result.AddRange(imageElements);
                                }
                            }

                            // Parse the run for text content (skip if it only contains a drawing)
                            if (sdtRunDrawings == null)
                            {
                                runs.AddRange(ParseRun(sdtChildRun, mainPart, paragraphStyleId));
                            }
                        }
                    }

                    break;

                case SdtCell sdtCell:
                    // Cell-level content control - extract runs from its content
                    foreach (var sdtCellRun in sdtCell.Descendants<OoxmlRun>())
                    {
                        var cellBreakElement = sdtCellRun.GetFirstChild<Break>();
                        if (cellBreakElement != null)
                        {
                            var breakType = cellBreakElement.Type?.Value;
                            if (breakType == BreakValues.Page)
                            {
                                if (runs.Count > 0)
                                {
                                    result.Add(
                                        new ParagraphElement
                                        {
                                            Runs = new List<Run>(runs),
                                            Properties = props
                                        });
                                    runs.Clear();
                                }

                                result.Add(new PageBreakElement());
                                continue;
                            }

                            if (breakType == BreakValues.Column)
                            {
                                if (runs.Count > 0)
                                {
                                    result.Add(
                                        new ParagraphElement
                                        {
                                            Runs = new List<Run>(runs),
                                            Properties = props
                                        });
                                    runs.Clear();
                                }

                                result.Add(new ColumnBreakElement());
                                continue;
                            }

                            var runProps = ParseRunProperties(sdtCellRun.RunProperties, mainPart);
                            runs.Add(
                                new()
                                {
                                    Text = "\n",
                                    Properties = runProps
                                });
                            continue;
                        }

                        runs.AddRange(ParseRun(sdtCellRun, mainPart, paragraphStyleId));
                    }

                    break;

                case SdtBlock sdtBlock:
                    // Block-level content control - extract runs from its content
                    foreach (var sdtBlockRun in sdtBlock.Descendants<OoxmlRun>())
                    {
                        var blockBreakElement = sdtBlockRun.GetFirstChild<Break>();
                        if (blockBreakElement != null)
                        {
                            var breakType = blockBreakElement.Type?.Value;
                            if (breakType == BreakValues.Page)
                            {
                                if (runs.Count > 0)
                                {
                                    result.Add(
                                        new ParagraphElement
                                        {
                                            Runs = new List<Run>(runs),
                                            Properties = props
                                        });
                                    runs.Clear();
                                }

                                result.Add(new PageBreakElement());
                                continue;
                            }

                            if (breakType == BreakValues.Column)
                            {
                                if (runs.Count > 0)
                                {
                                    result.Add(
                                        new ParagraphElement
                                        {
                                            Runs = new List<Run>(runs),
                                            Properties = props
                                        });
                                    runs.Clear();
                                }

                                result.Add(new ColumnBreakElement());
                                continue;
                            }

                            var runProps = ParseRunProperties(sdtBlockRun.RunProperties, mainPart);
                            runs.Add(
                                new()
                                {
                                    Text = "\n",
                                    Properties = runProps
                                });
                            continue;
                        }

                        runs.AddRange(ParseRun(sdtBlockRun, mainPart, paragraphStyleId));
                    }

                    break;

                case SimpleField simpleField:
                    // w:fldSimple wraps the cached result runs of a legacy field. A PAGE/NUMPAGES/
                    // SECTIONPAGES field collapses to one tagged run (cached text kept for the
                    // exporters; the renderers substitute the live value); anything else renders
                    // its cached result inline. The instruction is also captured on FieldCodes.
                    var (simpleKind, simpleFormat, simpleBookmark) = ClassifyPageField(simpleField.Instruction?.Value);
                    if (simpleKind != PageFieldKind.None)
                    {
                        var firstResultRun = simpleField.Descendants<OoxmlRun>().FirstOrDefault();
                        var cachedText = string.Concat(simpleField.Descendants<Text>().Select(_ => _.Text));
                        runs.Add(
                            new()
                            {
                                Text = cachedText,
                                Properties = ParseRunProperties(firstResultRun?.RunProperties, mainPart, paragraphStyleId),
                                PageField = simpleKind,
                                PageFieldNumberFormat = simpleFormat,
                                PageFieldBookmark = simpleBookmark
                            });

                        if (simpleKind is PageFieldKind.NumberOfPages or PageFieldKind.SectionPages)
                        {
                            requiresTotalPageCount = true;
                        }
                    }
                    else
                    {
                        foreach (var fieldRun in simpleField.Descendants<OoxmlRun>())
                        {
                            runs.AddRange(ParseRun(fieldRun, mainPart, paragraphStyleId));
                        }
                    }

                    break;

                case Hyperlink hyperlink:
                    // w:hyperlink wraps runs that point at an external URL or internal anchor. The
                    // visible content is the inner runs; the link target is captured on each run so
                    // the HTML/Markdown exporters can emit links (raster rendering ignores it).
                    var hyperlinkUrl = ResolveHyperlinkUrl(hyperlink, mainPart);
                    foreach (var hlRun in RunsWithin(hyperlink))
                    {
                        runs.AddRange(ParseRun(hlRun, mainPart, paragraphStyleId, hyperlinkUrl));
                    }

                    break;

                // Tracked changes render as Word PRINTS them, which is the All Markup view: an
                // insertion underlined and a deletion struck through, both in the revision colour
                // (tracked_changes/01). The revision metadata stays on ParsedDocument.TrackedChanges.
                //
                // A move is a deletion where the text was and an insertion where it went, and is
                // drawn as the two. (Word's own markup sets a move apart in green with doubled
                // rules; that distinction is not modelled.) Which mark a run takes is read off the
                // markup around it — see RevisionOf.
                case InsertedRun or DeletedRun or MoveToRun or MoveFromRun:
                    foreach (var revisedRun in RunsWithin(child))
                    {
                        runs.AddRange(ParseRun(revisedRun, mainPart, paragraphStyleId));
                    }

                    break;

                case DocumentFormat.OpenXml.Math.OfficeMath inlineMath:
                    // Office Math (OMML) — render the textual content of m:t descendants as a fallback.
                    // Proper math layout (radicals, fractions, sub/superscripts, big operators) is
                    // not modelled, so symbols and structure are lost; equations like "a²+b²=c²"
                    // come through as "a2+b2=c2" but at least don't disappear from the page.
                    MathParser.AppendMathText(runs, inlineMath);
                    break;

                case DocumentFormat.OpenXml.Math.Paragraph mathPara:
                    MathParser.AppendMathText(runs, mathPara);

                    // m:oMathPara is display math: Word centres the equation block — OMML's
                    // m:oMathParaPr/m:jc defaults to centerGroup. A w:jc on the paragraph itself
                    // still wins; an explicit m:jc left/right is honoured.
                    if (paraProps?.GetFirstChild<Justification>() == null)
                    {
                        var mathJustification = mathPara.ParagraphProperties?
                            .GetFirstChild<DocumentFormat.OpenXml.Math.Justification>()?.Val?.Value;
                        var mathAlignment = TextAlignment.Center;
                        if (mathJustification != null)
                        {
                            if (mathJustification == DocumentFormat.OpenXml.Math.JustificationValues.Left)
                            {
                                mathAlignment = TextAlignment.Left;
                            }
                            else if (mathJustification == DocumentFormat.OpenXml.Math.JustificationValues.Right)
                            {
                                mathAlignment = TextAlignment.Right;
                            }
                        }

                        props = props with
                        {
                            Alignment = mathAlignment
                        };
                    }

                    break;

                case OoxmlRun run:
                    // Check for legacy form fields (FieldChar with FormFieldData)
                    var formField = FieldParser.ParseFormField(run);
                    if (formField is CheckBoxFormFieldElement checkBoxField)
                    {
                        // A FORMCHECKBOX is an INLINE glyph in Word — "☐ Option 1" flows on one
                        // line — so it becomes a run (☐/☒ live in the embedded Morph Bullets
                        // face), not a block widget. ffData's size is half-points; auto-sized
                        // boxes track the surrounding run size.
                        var checkBoxProperties = ParseRunProperties(run.RunProperties, mainPart, paragraphStyleId) with
                        {
                            FontFamily = "Morph Bullets"
                        };
                        if (checkBoxField.SizePoints > 0)
                        {
                            checkBoxProperties = checkBoxProperties with
                            {
                                FontSizePoints = checkBoxField.SizePoints
                            };
                        }

                        runs.Add(
                            new()
                            {
                                Text = checkBoxField.Checked ? "☒" : "☐",
                                Properties = checkBoxProperties
                            });
                        break;
                    }

                    // Text-input and dropdown legacy fields deliberately DON'T emit their block
                    // widgets: Word's print layout renders just the field's cached result text
                    // inline ("Name: Enter your name" on one line, form_text_fields/form_dropdowns
                    // expected renders), and that cached text lives in the ordinary runs between
                    // the field's separate/end — falling through to the field plumbing below lets
                    // it flow exactly like Word.

                    // PAGE/NUMPAGES/SECTIONPAGES field plumbing (fldChar/instrText) carries no text
                    // and is consumed here; the field's cached result is captured and re-emitted as
                    // a single tagged run at the field's end (see the tracker above).
                    if (HandleFieldControlRun(run) || CaptureSuppressedFieldResult(run))
                    {
                        break;
                    }

                    // One walk over the run's subtree collects everything the branches below need.
                    List<Drawing>? runDrawings = null;
                    var hasPageOrColumnBreak = false;
                    foreach (var descendant in run.Descendants())
                    {
                        if (descendant is Drawing descendantDrawing)
                        {
                            (runDrawings ??= []).Add(descendantDrawing);
                        }
                        else if (descendant is Break descendantBreak &&
                                 (descendantBreak.Type?.Value == BreakValues.Page || descendantBreak.Type?.Value == BreakValues.Column))
                        {
                            hasPageOrColumnBreak = true;
                        }
                    }

                    // The text gathered ahead of a drawing is closed off as a paragraph of its own only
                    // when the drawing takes a place in the flow (an inline WordArt box, ink). Floating
                    // art is lifted out of the line: the text either side of its anchor is still ONE
                    // paragraph on one line (brochures/06's second page opens on a 4pt paragraph
                    // holding a space, an anchored group and another space).
                    void EndParagraphAhead(IReadOnlyList<DocumentElement> drawn)
                    {
                        if (runs.Count == 0 ||
                            drawn.All(_ => _ is FloatingShapeElement or FloatingImageElement or FloatingTextBoxElement or FloatingWordArtElement))
                        {
                            return;
                        }

                        result.Add(
                            new ParagraphElement
                            {
                                Runs = new List<Run>(runs),
                                Properties = props
                            });
                        runs.Clear();
                    }

                    // Check for drawings (images/icons/WordArt/ink/shapes) within run
                    foreach (var drawing in runDrawings ?? (IReadOnlyList<Drawing>) [])
                    {
                        // Try background shapes first (solid fill behind text)
                        // May return multiple shapes when a WordprocessingGroup contains multiple non-decorative shapes
                        var drawingChildSources = new Dictionary<DocumentElement, OpenXmlElement>();
                        var shapeElements = ShapeParser.ParseBackgroundShapes(drawing, currentThemeColors, mainPart, GetPartBytes, drawingChildSources, anchorAlignmentPage);

                        // Check if there's a group - groups may contain text boxes and images even without shapes
                        var hasGroup = drawing.Descendants<WPG.WordprocessingGroup>().Any();

                        // Inline shape groups (no behindDoc anchor, just <wp:inline> with a wpg:wgp inside)
                        // need to flow with the surrounding text rather than render as floating block content.
                        // Skip the group-as-block branch for inline drawings so the inline-image path catches them.
                        var isInlineGroup = hasGroup &&
                                            drawing.Descendants().Any(_ => _.LocalName == "inline");

                        // A FRONT-of-text anchored drawing whose art is a blip-filled, solid or line
                        // text-free wsp reaches neither ShapeParser (behindDoc-only) nor the
                        // pic-based image path, so it goes through the walk (newsletters/08's cover
                        // photo, resumes/10's accent circle, cards/01's dashed fold guides). For
                        // behindDoc standalones ShapeParser already emitted a copy and the
                        // position/size dedup below eats the walk's twin. Text-CARRYING solid shapes
                        // stay on the ParseTextBox path further down.
                        var hasAnchoredFillShape =
                            drawing.GetFirstChild<DW.Anchor>() != null &&
                            drawing.Descendants<WPS.WordprocessingShape>().Any(shape =>
                                shape.GetFirstChild<WPS.ShapeProperties>() is { } shapeProperties &&
                                (shapeProperties.GetFirstChild<A.BlipFill>() != null ||
                                 ((shapeProperties.GetFirstChild<A.SolidFill>() != null ||
                                   ShapeParser.IsLineShape(shapeProperties)) &&
                                  TextBoxHasNoText(shape))));

                        if ((shapeElements.Count > 0 || hasGroup || hasAnchoredFillShape) &&
                            !isInlineGroup)
                        {
                            // For grouped drawings anchored inside table cells the walk below owns the
                            // children: its nested-group transform math is right, while ShapeParser's
                            // flat anchor-extent scaling emits them oversized at group-local positions
                            // (business/05's corner tiles, 2.4x) that the size-sensitive dedup cannot
                            // pair. Body-level groups keep the dual-parser + dedup: there ShapeParser's
                            // flat scaling is right (agendas-minutes/02, newsletters/01/12, wedding/11).
                            var cellGroup = hasGroup &&
                                            para.Ancestors<DocumentFormat.OpenXml.Wordprocessing.TableCell>().Any();

                            // ShapeParser's flat anchor-extent scaling is only meaningful when the
                            // group's nesting is identity (chOff==off, chExt==ext, no rotation) —
                            // Word's usual pattern, where a child's raw EMU coordinates already
                            // live in the root space. A NON-identity nested grpSp rescales or
                            // rotates its children (labels/16's bear sub-groups scale 1.55×), and
                            // SP's per-child flat math puts them at garbage positions that the
                            // size-sensitive dedup can't match — the walk's composed nested
                            // transform is the only correct owner there, exactly as for cell
                            // groups.
                            var nonIdentityNesting = hasGroup &&
                                                     HasNonIdentityNestedGroup(drawing);
                            var walkOwnsChildren = cellGroup || nonIdentityNesting;

                            // Also check for images in the same drawing/group (e.g., decorative overlays, SVG backgrounds)
                            var childSources = drawingChildSources;
                            var overlayImages = ParseDrawingElements(drawing, mainPart, childSources);

                            // Parse text boxes and solid fill shapes inside the shapes/group (they contain the actual content).
                            // Skip any solid-fill FloatingShapeElement whose position/size matches one
                            // already emitted by ShapeParser above — both parsers traverse the same wgp
                            // group and would otherwise produce duplicates.
                            var shapesFromDrawing = ParseAllShapesFromDrawing(drawing, mainPart, childSources);
                            var walkSurvivors = new List<DocumentElement>();
                            foreach (var shape in shapesFromDrawing)
                            {
                                // The position/size dedup only applies when ShapeParser's output was
                                // actually emitted — for cell-anchored groups it is skipped above,
                                // and deduping against it here would silently eat the only copy
                                // (labels/14's navy card bodies matched ShapeParser's twins).
                                if (!walkOwnsChildren &&
                                    shape is FloatingShapeElement fse &&
                                    shapeElements.Any(existing =>
                                        existing is FloatingShapeElement e &&
                                        Math.Abs(e.HorizontalPositionPoints - fse.HorizontalPositionPoints) < 0.5 &&
                                        Math.Abs(e.VerticalPositionPoints - fse.VerticalPositionPoints) < 0.5 &&
                                        Math.Abs(e.WidthPoints - fse.WidthPoints) < 0.5 &&
                                        Math.Abs(e.HeightPoints - fse.HeightPoints) < 0.5))
                                {
                                    continue;
                                }

                                walkSurvivors.Add(shape);
                            }

                            // Word paints a group's children back-to-front in DOCUMENT order, but the
                            // two sweeps above emit all pictures then all shapes/text boxes — labels/06's
                            // "ADMIT ONE" captions sank under their ticket-body pictures while
                            // labels/11's white base boxes floated over its purple brush art (each
                            // needs the opposite phase order, so no concatenation is ever right).
                            // Re-interleave by each element's source child position within the wgp.
                            // OrderBy is stable, and elements without a mapped source (txbx-nested
                            // pictures) keep their relative order at the end.
                            var groupForOrder = drawing.Descendants<WPG.WordprocessingGroup>().FirstOrDefault();
                            // The walk parses blip-filled (image-textured) shapes with the same
                            // composed transforms as everything else, so walk-owned groups
                            // suppress ShapeParser's output entirely — keeping its image shapes
                            // alongside the walk's would double-draw them, because the
                            // position/size dedup above is skipped for walk-owned groups.
                            var groupChildren = (walkOwnsChildren
                                    ? []
                                    : shapeElements.Cast<DocumentElement>())
                                .Concat(overlayImages)
                                .Concat(walkSurvivors);
                            if (groupForOrder != null)
                            {
                                var documentOrder = GroupDrawables(groupForOrder)
                                    .Select((node, index) => (Node: node, Index: index))
                                    .ToDictionary(_ => _.Node, _ => _.Index);
                                groupChildren = groupChildren.OrderBy(_ =>
                                {
                                    if (childSources.TryGetValue(_, out var source) &&
                                        documentOrder.TryGetValue(source, out var ordinal))
                                    {
                                        return ordinal;
                                    }

                                    return int.MaxValue;
                                });
                            }

                            var drawn = groupChildren.ToList();
                            if (!IsTabOnly(runs))
                            {
                                EndParagraphAhead(drawn);
                            }

                            result.AddRange(drawn);

                            continue;
                        }

                        // Try ink first, then WordArt, then fall back to image
                        var inkElement = InkParser.ParseInk(drawing, mainPart);
                        if (inkElement != null)
                        {
                            // Emit current paragraph content before the ink
                            if (runs.Count > 0)
                            {
                                result.Add(
                                    new ParagraphElement
                                    {
                                        Runs = new List<Run>(runs),
                                        Properties = props
                                    });
                                runs.Clear();
                            }

                            result.Add(inkElement);
                        }
                        else
                        {
                            var wordArtElement = ParseWordArt(
                                drawing,
                                mainPart,
                                props.Alignment,
                                spacingBefore: runs.Count > 0 ? 0 : props.SpacingBeforePoints,
                                spacingAfter: props.SpacingAfterPoints,
                                hostParagraph: runs.Count > 0 ? null : props);
                            if (wordArtElement != null)
                            {
                                EndParagraphAhead([wordArtElement]);
                                result.Add(wordArtElement);
                            }
                            else
                            {
                                // Try text box (positioned text without WordArt transform)
                                var textBoxElement = ParseTextBox(drawing, mainPart);
                                if (textBoxElement != null)
                                {
                                    EndParagraphAhead([textBoxElement]);
                                    result.Add(textBoxElement);
                                }
                                else
                                {
                                    // Check if this is an inline image (wp:inline) - should flow with text
                                    var isInline = drawing.Descendants().Any(_ => _.LocalName == "inline");

                                    if (isInline)
                                    {
                                        // The picture's run is set in the run's own font like any other:
                                        // what a line keeps under a picture, and the pitch of a line a
                                        // thin rule stands alone on, are that font's (resumes/09's sidebar
                                        // rules: a 0.7pt rectangle in a 9.5pt heading style).
                                        var inlineRun = TryParseInlineImageRun(drawing, mainPart, ParseRunProperties(run.RunProperties, mainPart, paragraphStyleId));
                                        if (inlineRun != null)
                                        {
                                            runs.Add(inlineRun);
                                        }
                                    }
                                    else
                                    {
                                        // Anchored/floating images are block elements
                                        var imageElements = ParseDrawingElements(drawing, mainPart);
                                        if (imageElements.Count > 0)
                                        {
                                            EndParagraphAhead(imageElements);
                                            result.AddRange(imageElements);
                                        }
                                    }
                                }
                            }
                        }
                    }

                    // An embedded OLE object (w:object) is drawn from the preview picture it stores, in
                    // the line or floating as its VML shape says (OlePreviewParser).
                    foreach (var oleObject in OlePreviewParser.Objects(run))
                    {
                        if (OlePreviewParser.Read(oleObject, ResolveHostPart(oleObject, mainPart), GetPartBytes, anchorAlignmentPage) is not { } preview)
                        {
                            continue;
                        }

                        if (preview.Float is { } placement)
                        {
                            result.Add(
                                new FloatingImageElement
                                {
                                    ImageData = preview.Data,
                                    ContentType = preview.ContentType,
                                    Metafile = preview.Metafile,
                                    Description = preview.Description,
                                    WidthPoints = preview.WidthPoints,
                                    HeightPoints = preview.HeightPoints,
                                    HorizontalPositionPoints = placement.HorizontalPositionPoints,
                                    VerticalPositionPoints = placement.VerticalPositionPoints,
                                    HorizontalAnchor = placement.HorizontalAnchor,
                                    VerticalAnchor = placement.VerticalAnchor,
                                    BehindText = placement.BehindText,
                                    RelativeHeight = placement.RelativeHeight,
                                    WrapType = placement.WrapType,
                                    WrapTextSide = placement.WrapTextSide,
                                    WrapDistanceLeftPoints = placement.WrapDistanceLeftPoints,
                                    WrapDistanceTopPoints = placement.WrapDistanceTopPoints,
                                    WrapDistanceRightPoints = placement.WrapDistanceRightPoints,
                                    WrapDistanceBottomPoints = placement.WrapDistanceBottomPoints
                                });
                            continue;
                        }

                        runs.Add(
                            new()
                            {
                                Text = "",
                                Properties = ParseRunProperties(run.RunProperties, mainPart, paragraphStyleId),
                                InlineImageData = preview.Data,
                                InlineImageContentType = preview.ContentType,
                                InlineMetafile = preview.Metafile,
                                InlineImageDescription = preview.Description,
                                InlineImageWidthPoints = preview.WidthPoints,
                                InlineImageHeightPoints = preview.HeightPoints
                            });
                    }

                    // Check for breaks within run
                    foreach (var runChild in run.ChildElements)
                    {
                        if (runChild is Break breakElement)
                        {
                            var breakType = breakElement.Type?.Value;

                            if (breakType == BreakValues.Page)
                            {
                                // Emit current paragraph content before the break
                                if (runs.Count > 0)
                                {
                                    result.Add(
                                        new ParagraphElement
                                        {
                                            Runs = new List<Run>(runs),
                                            Properties = props
                                        });
                                    runs.Clear();
                                }

                                result.Add(new PageBreakElement());
                            }
                            else if (breakType == BreakValues.Column)
                            {
                                // Emit current paragraph content before the break
                                if (runs.Count > 0)
                                {
                                    result.Add(
                                        new ParagraphElement
                                        {
                                            Runs = new List<Run>(runs),
                                            Properties = props
                                        });
                                    runs.Clear();
                                }

                                result.Add(new ColumnBreakElement());
                            }
                            // Line breaks (no type or TextWrapping) are emitted in document order
                            // by ParseRun below (emitLineBreaks), so the text on both sides of the
                            // break is preserved — nothing to do here.
                        }
                        else if (lastRenderedPageBreakCount >= 20 &&
                                 runChild is LastRenderedPageBreak &&
                                 !run.Descendants<Text>().Any() &&
                                 !run.Descendants<Break>().Any())
                        {
                            // Word caches pagination using lastRenderedPageBreak. Only treat it as a page boundary hint
                            // when the document has lots of these markers (i.e., likely reflects full-document pagination).
                            if (result.LastOrDefault() is not PageBreakElement)
                            {
                                if (runs.Count > 0)
                                {
                                    result.Add(
                                        new ParagraphElement
                                        {
                                            Runs = new List<Run>(runs),
                                            Properties = props
                                        });
                                    runs.Clear();
                                }

                                result.Add(new PageBreakElement {FromPaginationHint = true});
                            }
                        }
                        else if (runChild is Text)
                        {
                            // Regular text - will be handled by ParseRun
                        }
                    }

                    // Parse the run normally (this handles text content, with line breaks emitted as
                    // "\n" runs in document order). Skip if the run only contains a drawing (no text).
                    if (runDrawings == null)
                    {
                        var parsedRuns = ParseRun(run, mainPart, paragraphStyleId, emitLineBreaks: true);
                        // A page/column break splits the paragraph above, so its run's content is
                        // intentionally not appended here; line-break-only (or break-free) runs are.
                        if (parsedRuns.Count > 0 &&
                            !hasPageOrColumnBreak)
                        {
                            runs.AddRange(parsedRuns);
                        }
                    }

                    break;
            }
        }

        // Floating art — behind-text or in-front; a shape, picture, text box or WordArt — is lifted
        // out of the flow, so a paragraph that produced only that is still an empty paragraph whose
        // mark takes a line, whether or not the shape parser understood the drawing (resumes/10's
        // accent circles, business-plans/13's cover image and shape, cards/02's ticket text box).
        var onlyFloatingArt = result.Count > 0 &&
                              result.All(_ => _ is FloatingShapeElement or FloatingImageElement or FloatingTextBoxElement or FloatingWordArtElement);

        // Add remaining content
        if (runs.Count == 0 &&
            (result.Count == 0 ||
             (onlyFloatingArt &&
              props.SpacingAfterPoints <= 0)))
        {
            // Empty paragraph - still counts for spacing
            // Keep runs empty so the renderer can avoid creating spurious extra pages at document end.
            // A paragraph whose only purpose is carrying the sectPr consumes no line number, and for a
            // CONTINUOUS break draws no line either — only its spacing survives (Word-probed; see
            // ParagraphElement.IsSectionBreakMark).
            //
            // Restricted to continuous breaks because that is what the probe measured. A next-page
            // mark ends the outgoing page, where its line decides whether the content before it fits —
            // unprobed (it moved sample.docx from 6 pages to 5 with no Word reference to settle it).
            result.Add(
                new ParagraphElement
                {
                    Runs = [],
                    Properties = sectionBreak != null
                        ? props with
                        {
                            SuppressLineNumbers = true
                        }
                        : props,
                    IsSectionBreakMark = sectionBreak is {BreakType: SectionBreakType.Continuous}
                });
        }
        else if (runs.Count > 0)
        {
            result.Add(
                new ParagraphElement
                {
                    Runs = runs,
                    Properties = props
                });
        }
        else if (props.SpacingAfterPoints > 0 &&
                 onlyFloatingArt)
        {
            // Paragraph contained only behind-text decorative shapes (a "background
            // placeholder" pattern) but carries explicit spacing-after — Word honours
            // that spacing so the following block sits the correct distance below.
            // Emit a marker paragraph (IsAnchorOnlyMark), laid out with a line like any mark,
            // that keeps its spacing-before too (Word's XPS of agendas-minutes/14, 2026-10-27:
            // its 3pt w:before stands above the first baseline).
            result.Add(
                new ParagraphElement
                {
                    Runs = [],
                    Properties = props,
                    IsAnchorOnlyMark = true
                });
        }
        else if (result.Count > 0 &&
                 result[^1] is ColumnBreakElement)
        {
            // A paragraph that ENDS at a column break still has its mark, and Word places it at
            // the top of the NEXT column as a line of its own — the following paragraph starts one
            // line (plus this paragraph's after-spacing) further down (column_breaks, mixed_breaks).
            // Deliberately NOT done for page breaks: the page-break mark interacts with FinishPage's
            // blank-page rules (see the sectPr note above) and needs its own adjudication.
            result.Add(
                new ParagraphElement
                {
                    Runs = [],
                    Properties = props
                });
        }

        // Add section break after paragraph content
        if (sectionBreak != null)
        {
            result.Add(sectionBreak);
        }

        // Wire this paragraph's floats to their anchor: every float parsed by this call belongs to
        // THIS paragraph, whatever side of the flush ordering it landed on, and a Word probe (2026-08-05,
        // _probe_anchor_ref) pins the reference to the paragraph's PRE-SPACING top — an offset-0
        // paragraph-anchored shape on a 60pt-before paragraph sits at the previous paragraph's bottom
        // edge, not at the line top. Multiple fragments in one call (splits around breaks) anchor to the
        // FIRST — the paragraph's start. A call that produced no paragraph leaves the anchor null and
        // the layout falls back to the flow cursor, which is the same position.
        var anchorParagraph = result.OfType<ParagraphElement>().FirstOrDefault();
        if (anchorParagraph != null)
        {
            foreach (var element in result)
            {
                switch (element)
                {
                    case FloatingImageElement {AnchorParagraph: null} image:
                        image.AnchorParagraph = anchorParagraph;
                        break;
                    case FloatingShapeElement {AnchorParagraph: null} shape:
                        shape.AnchorParagraph = anchorParagraph;
                        break;
                    case FloatingTextBoxElement {AnchorParagraph: null} textBox:
                        textBox.AnchorParagraph = anchorParagraph;
                        break;
                    case FloatingChartElement {AnchorParagraph: null} chart:
                        chart.AnchorParagraph = anchorParagraph;
                        break;
                }
            }
        }

        // Automatic text turns white over a dark PARAGRAPH shading, the same contrast rule cell shading
        // gets (see IsDarkForAutomaticText for the Word-probed formula and threshold). Applied here, over
        // every paragraph this method built, because the runs are flushed into ParagraphElements at a
        // dozen points above and each would otherwise need its own call. Runs still lacking a colour are
        // the automatic ones — an explicit w:color has already filled ColorHex in and wins. Run-level
        // w:shd is resolved in ParseRunProperties instead, where the run's own fill is in hand.
        for (var i = 0; i < result.Count; i++)
        {
            if (result[i] is ParagraphElement {Properties.BackgroundColorHex: { } fill} shaded &&
                IsDarkForAutomaticText(fill))
            {
                result[i] = ApplyDefaultRunColorToParagraph(shaded, "FFFFFF");
            }
        }

        // Where Word's change bars stand for a revision of the w:p itself: its properties' beside its
        // first line, its mark's beside its last.
        var revisedProperties = paraProps?.GetFirstChild<ParagraphPropertiesChange>() != null ||
                                paraProps?.NumberingProperties?.GetFirstChild<NumberingChange>() != null;
        var revisedMark = paraProps?.ParagraphMarkRunProperties?.ChildElements.Any(_ => _ is Inserted or Deleted or MoveFrom or MoveTo or ParagraphMarkRunPropertiesChange) == true;
        if (revisedProperties &&
            result.OfType<ParagraphElement>().FirstOrDefault() is { } firstBuilt)
        {
            firstBuilt.PropertiesRevised = true;
        }

        if (revisedMark &&
            result.OfType<ParagraphElement>().LastOrDefault() is { } lastBuilt)
        {
            lastBuilt.MarkRevised = true;
        }

        // Last, once every paragraph this call builds has its final form: a w:p split around a break
        // is several paragraphs here and one in the file.
        if (paragraphSources?.TryGetValue(para, out var source) == true)
        {
            foreach (var element in result)
            {
                if (element is ParagraphElement built)
                {
                    built.Source = source;
                }
            }
        }

        // A bookmark is on the page its w:p begins on, so it is tied to the first paragraph that w:p
        // was read as (a break can make a w:p more paragraphs, or none).
        if (bookmarkParagraphs?.ContainsKey(para) == true)
        {
            bookmarkParagraphs[para] = result.OfType<ParagraphElement>().FirstOrDefault();
        }

        return result;
    }

    static IReadOnlyList<TabStop> ParseTabs(OpenXmlCompositeElement? props, IReadOnlyList<TabStop> inheritedTabs)
    {
        var tabsEl = props?.GetFirstChild<OoxmlTabs>();
        if (tabsEl == null)
        {
            return inheritedTabs;
        }

        // Merge inherited + inline. Inline w:val="clear" removes inherited stops at that position.
        var map = new Dictionary<double, TabStop>();
        foreach (var inherited in inheritedTabs)
        {
            map[inherited.PositionPoints] = inherited;
        }

        foreach (var tabStop in tabsEl.Elements<OoxmlTabStop>())
        {
            if (tabStop.Position?.HasValue != true)
            {
                continue;
            }

            var positionPoints = tabStop.Position.Value / OoxmlUnits.TwipsPerPoint;
            var alignment = MapTabAlignment(tabStop.Val?.InnerText);
            if (alignment == TabAlignment.Clear)
            {
                map.Remove(positionPoints);
                continue;
            }

            map[positionPoints] = new()
            {
                PositionPoints = positionPoints,
                Alignment = alignment,
                Leader = MapTabLeader(tabStop.Leader?.InnerText)
            };
        }

        return [.. map.Values.OrderBy(_ => _.PositionPoints)];
    }

    static TabAlignment MapTabAlignment(string? val) =>
        val switch
        {
            "clear" => TabAlignment.Clear,
            "center" => TabAlignment.Center,
            "right" or "end" => TabAlignment.Right,
            "decimal" => TabAlignment.Decimal,
            "bar" => TabAlignment.Bar,
            _ => TabAlignment.Left
        };

    // w:ptab's alignment vocabulary is narrower than w:tab's — left / center / right only.
    static TabAlignment MapPositionalTabAlignment(string? val) =>
        val switch
        {
            "center" => TabAlignment.Center,
            "right" => TabAlignment.Right,
            _ => TabAlignment.Left
        };

    static PositionalTabBase MapPositionalTabBase(string? val) =>
        val switch
        {
            "indent" => PositionalTabBase.Indent,
            "page" => PositionalTabBase.Page,
            _ => PositionalTabBase.Margin
        };

    static TabLeader MapTabLeader(string? val) =>
        val switch
        {
            "dot" => TabLeader.Dot,
            "hyphen" => TabLeader.Hyphen,
            "underscore" => TabLeader.Underscore,
            "middleDot" => TabLeader.MiddleDot,
            "heavy" => TabLeader.Heavy,
            _ => TabLeader.None
        };

    /// <summary>The space Word's "Auto" paragraph spacing stands for.</summary>
    const double autoSpacingPoints = 14;

    /// <summary>
    /// What a paragraph's space before and after come to when either is Word's "Auto"
    /// (<c>w:beforeAutospacing</c> / <c>w:afterAutospacing</c>, what the HTML importer writes and
    /// what Word's own Normal (Web) and several template Title styles carry). The amount declared
    /// beside the flag is not used. Auto is 14pt, and nothing at all where the paragraph has no
    /// neighbour to be set off from: before the first paragraph of a table cell, after the last one,
    /// and between two consecutive items of one list. It then collapses against the neighbour's
    /// spacing like any other.
    ///
    /// <para>Word-probed 2026-10-11 (<c>_probe_autospace_a</c> / <c>_c</c>, compatibility modes 15
    /// and 12, identical: one-line paragraphs of exact 20pt lines, baselines read off the XPS). Auto
    /// before with 5pt declared opens 14.2pt of space, and with 40pt declared 13.6; auto after
    /// 14.2; an auto after against an auto before leaves 14.2, not 28. Three list items, auto on
    /// both sides, stand 20.4 and 19.8 apart with 13.6 above the first and 14.2 under the last. A
    /// cell's only paragraph, auto on both sides, sits on the baseline of the plain paragraph in the
    /// cell beside it and adds nothing under itself; in a three-paragraph cell the first takes no
    /// auto before, the last no auto after, and the middle one 14.2 either side. Unmodelled: a 30pt
    /// paragraph on single line spacing took 19.5 before and left 5.6 after.</para>
    ///
    /// <para>The compatibility setting <c>w:doNotUseHTMLParagraphAutoSpacing</c> switches all of
    /// that off: the declared amounts stand, in cells and lists too (<c>_probe_autospace_b</c>: 5pt
    /// before reads 4.8 at a cell's top and 5.4 under its last line). Under it two auto spacings that
    /// meet are ADDED rather than collapsed (5 and 5 leave 10), which is not modelled either.</para>
    /// </summary>
    (double Before, double After) ResolveAutoSpacing(Paragraph? paragraph, double before, double after, bool beforeAuto, bool afterAuto)
    {
        if (usesFixedAutoSpacing)
        {
            return (before, after);
        }

        if (beforeAuto)
        {
            if (paragraph != null &&
                (IsCellEdge(paragraph, first: true) ||
                 SharesListWith(paragraph, PreviousBlock(paragraph))))
            {
                before = 0;
            }
            else
            {
                before = autoSpacingPoints;
            }
        }

        if (afterAuto)
        {
            after = paragraph != null &&
                    (IsCellEdge(paragraph, first: false) ||
                     SharesListWith(paragraph, NextBlock(paragraph)))
                ? 0
                : autoSpacingPoints;
        }

        return (before, after);

        static OpenXmlElement? PreviousBlock(Paragraph paragraph)
        {
            for (var sibling = paragraph.PreviousSibling(); sibling != null; sibling = sibling.PreviousSibling())
            {
                if (sibling is Paragraph or Table or SdtBlock)
                {
                    return sibling;
                }
            }

            return null;
        }

        static OpenXmlElement? NextBlock(Paragraph paragraph)
        {
            for (var sibling = paragraph.NextSibling(); sibling != null; sibling = sibling.NextSibling())
            {
                if (sibling is Paragraph or Table or SdtBlock)
                {
                    return sibling;
                }
            }

            return null;
        }

        // The first (or last) block of a table cell, a content control around it looked through.
        static bool IsCellEdge(Paragraph paragraph, bool first)
        {
            OpenXmlElement block = paragraph;
            while (true)
            {
                var neighbour = first ? block.PreviousSibling() : block.NextSibling();
                while (neighbour != null &&
                       neighbour is not (Paragraph or Table or SdtBlock))
                {
                    neighbour = first ? neighbour.PreviousSibling() : neighbour.NextSibling();
                }

                if (neighbour != null)
                {
                    return false;
                }

                switch (block.Parent)
                {
                    case DocumentFormat.OpenXml.Wordprocessing.TableCell:
                        return true;
                    case SdtContentBlock {Parent: SdtBlock control}:
                        block = control;
                        continue;
                    default:
                        return false;
                }
            }
        }

        bool SharesListWith(Paragraph paragraph, OpenXmlElement? neighbour) =>
            neighbour is Paragraph other &&
            ListOf(paragraph) is { } list &&
            ListOf(other) == list;

        // The list a paragraph is an item of: its own w:numPr, or its style's. A numId of 0 takes
        // the paragraph out of any list its style puts it in.
        int? ListOf(Paragraph paragraph)
        {
            var properties = paragraph.ParagraphProperties;
            if (properties?.NumberingProperties?.NumberingId?.Val?.Value is { } direct)
            {
                return direct == 0 ? null : direct;
            }

            var style = properties?.ParagraphStyleId?.Val?.Value ?? defaultParagraphStyleId;
            if (style != null &&
                styleNumbering != null &&
                styleNumbering.TryGetValue(style, out var inherited) &&
                inherited.numId != 0)
            {
                return inherited.numId;
            }

            return null;
        }
    }

    ParagraphProperties ParseParagraphProperties(OoxmlParagraphProperties? props, MainDocumentPart mainPart, string? styleId = null, bool omitParagraphMark = false, Paragraph? paragraph = null)
    {
        // Get style defaults if available. Paragraphs without an explicit w:pStyle still
        // inherit the document's default paragraph style (the one with w:default="1",
        // typically Normal) — without this, properties like ind=0 from Normal don't
        // override the indent inherited from pPrDefault.
        ParagraphProperties? styleDefaults = null;
        var effectiveStyleId = styleId ?? defaultParagraphStyleId;
        if (styleParagraphProperties != null &&
            effectiveStyleId != null)
        {
            styleParagraphProperties.TryGetValue(effectiveStyleId, out styleDefaults);
        }

        // Resolve the paragraph mark's run formatting (w:pPr/w:rPr over the paragraph style
        // chain) the same way a run without direct formatting resolves. Word derives an empty
        // paragraph's line height from the mark, not from the first run or a fixed default.
        // Table cells and headers/footers are excluded for now: cell empty-mark heights are
        // entangled with exact-row clipping and Word's end-of-cell mark collapse, and a footer
        // empty changes the content box of every page — both need their own rules first
        // (see page_counts.md, pass 4).
        var paragraphMark = omitParagraphMark
            ? null
            : ParseRunProperties(props?.ParagraphMarkRunProperties, mainPart, styleId);

        // Start with style defaults or document defaults (pPrDefault)
        var alignment = styleDefaults?.Alignment ?? defaultAlignment;
        var spacingBefore = styleDefaults?.SpacingBeforePoints ?? defaultSpacingBeforePoints;
        var spacingAfter = styleDefaults?.SpacingAfterPoints ?? defaultSpacingAfterPoints;
        var spacingBeforeAuto = styleDefaults?.SpacingBeforeAuto ?? false;
        var spacingAfterAuto = styleDefaults?.SpacingAfterAuto ?? false;
        // When no style applies (bare docx without styles.xml), Word falls back to its built-in
        // Normal style — see defaultLineSpacingMultiplier, which carries that value only when the
        // document declares no styles.xml or no docDefaults, and single spacing otherwise.
        var lineSpacingMultiplier = styleDefaults?.LineSpacingMultiplier
                                    ?? docDefaultLineSpacingMultiplier
                                    ?? defaultLineSpacingMultiplier;
        var lineSpacingPoints = styleDefaults?.LineSpacingPoints ?? 0;
        var lineSpacingRule = styleDefaults?.LineSpacingRule ?? LineSpacingRule.Auto;
        var firstLineIndent = styleDefaults?.FirstLineIndentPoints ?? 0;
        var leftIndent = styleDefaults?.LeftIndentPoints ?? defaultLeftIndentPoints;
        var rightIndent = styleDefaults?.RightIndentPoints ?? defaultRightIndentPoints;
        var hangingIndent = styleDefaults?.HangingIndentPoints ?? 0;
        var contextualSpacing = styleDefaults?.ContextualSpacing ?? false;
        var suppressLineNumbers = false;

        // A direct w:numPr/w:numId="0" switches the style's numbering off, and the style's numbering
        // indent goes with it (styleIndentBound) — a paragraph's own w:ind below still overrides.
        var directNumbering = props?.GetFirstChild<NumberingProperties>();
        if (directNumbering != null &&
            (directNumbering.NumberingId?.Val?.Value ?? 0) == 0 &&
            effectiveStyleId != null &&
            styleIndentBound.Contains(effectiveStyleId) &&
            styleUnboundIndents.TryGetValue(effectiveStyleId, out var unboundIndent))
        {
            (leftIndent, firstLineIndent, hangingIndent, rightIndent) = unboundIndent;
        }

        var suppressAutoHyphens = false;

        // Table-style step of the cascade. The values above resolved the paragraph style chain
        // over the DOCUMENT defaults; inside a table, the table style's own w:pPr sits between
        // those two, so it wins wherever the style chain declared nothing. Word-verified on
        // resumes/07, whose TableGrid declares w:after="0" w:line="240": sweeping the document's
        // w:after leaves its cell rows untouched (see docs/word-features.md, Table Style
        // Paragraph Properties).
        if (currentTableStyleId is { } tableStyleId &&
            tableStyleSpacing.TryGetValue(tableStyleId, out var fromTableStyle) &&
            fromTableStyle.DeclaresAnything)
        {
            // effectiveStyleId, not styleId: a paragraph with no explicit w:pStyle still uses the
            // document's default paragraph style, and that style's own declarations outrank the
            // table style. business-plans/02 is the case — its Normal declares w:line="336" (1.4),
            // which Word keeps inside TableGrid tables even though the table style says 240.
            var declared = effectiveStyleId != null &&
                           paragraphStyleDeclaredSpacing.TryGetValue(effectiveStyleId, out var d)
                ? d
                : new();

            if (declared.SpacingBeforePoints == null &&
                fromTableStyle.SpacingBeforePoints is { } tsBefore)
            {
                spacingBefore = tsBefore;
            }

            if (declared.SpacingAfterPoints == null &&
                fromTableStyle.SpacingAfterPoints is { } tsAfter)
            {
                spacingAfter = tsAfter;
            }

            if (declared.LineSpacingRule == null &&
                fromTableStyle.LineSpacingRule is { } tsRule)
            {
                lineSpacingRule = tsRule;
                lineSpacingMultiplier = fromTableStyle.LineSpacingMultiplier ?? lineSpacingMultiplier;
                lineSpacingPoints = fromTableStyle.LineSpacingPoints ?? lineSpacingPoints;
            }

            if (declared.LeftIndentPoints == null &&
                fromTableStyle.LeftIndentPoints is { } tsLeft)
            {
                leftIndent = tsLeft;
            }

            if (declared.RightIndentPoints == null &&
                fromTableStyle.RightIndentPoints is { } tsRight)
            {
                rightIndent = tsRight;
            }
        }

        // Pagination properties - get from style defaults
        var keepLines = styleDefaults?.KeepLines ?? false;
        var keepNext = styleDefaults?.KeepNext ?? false;
        // Default is true per OpenXML spec
        var widowControl = styleDefaults?.WidowControl ?? true;
        var pageBreakBefore = styleDefaults?.PageBreakBefore ?? false;
        var backgroundColor = styleDefaults?.BackgroundColorHex;

        // If no inline properties, return style defaults. The mark still sizes its line by the run
        // ladder (see the paragraphMarkFontSize block below): a bare <w:p/> spacer in a table cell has no
        // resolved mark (the cell path omits it) and no run (letters/13's Posterama 11 spacers).
        if (props == null)
        {
            var bareMark = ParseRunProperties(null, mainPart, styleId);
            return new()
            {
                Alignment = alignment,
                ParagraphMarkFontSizePoints = bareMark.FontSizePoints,
                ParagraphMarkFontFamily = bareMark.FontFamily,
                SpacingBeforePoints = spacingBefore,
                SpacingAfterPoints = spacingAfter,
                LineSpacingMultiplier = lineSpacingMultiplier,
                LineSpacingPoints = lineSpacingPoints,
                LineSpacingRule = lineSpacingRule,
                FirstLineIndentPoints = firstLineIndent,
                LeftIndentPoints = leftIndent,
                RightIndentPoints = rightIndent,
                HangingIndentPoints = hangingIndent,
                ContextualSpacing = contextualSpacing,
                SuppressLineNumbers = suppressLineNumbers,
                SuppressAutoHyphens = suppressAutoHyphens,
                AutoHyphenation = AutoHyphenationFor(suppressAutoHyphens),
                KeepLines = keepLines,
                KeepNext = keepNext,
                WidowControl = widowControl,
                PageBreakBefore = pageBreakBefore,
                ParagraphMarkRunProperties = paragraphMark,
                BackgroundColorHex = backgroundColor,
                StyleId = styleId,
                ResolvedStyleId = effectiveStyleId,
                InDefaultStyle = effectiveStyleId == defaultParagraphStyleId,
                Borders = styleDefaults?.Borders,
                BorderTopSpacePoints = styleDefaults?.BorderTopSpacePoints ?? 0,
                BorderBottomSpacePoints = styleDefaults?.BorderBottomSpacePoints ?? 0,
                BorderLeftSpacePoints = styleDefaults?.BorderLeftSpacePoints ?? 0,
                BorderRightSpacePoints = styleDefaults?.BorderRightSpacePoints ?? 0,
                BorderBetween = styleDefaults?.BorderBetween ?? BorderEdge.None,
                BorderBetweenSpacePoints = styleDefaults?.BorderBetweenSpacePoints ?? 0,
                TabStops = styleDefaults?.TabStops ?? [],
                DefaultTabStopPoints = defaultTabStopPoints
            };
        }

        // Override with inline properties, read in one pass over the children; ??= keeps
        // GetFirstChild's first-in-document-order semantics on duplicates.
        Justification? justification = null;
        SpacingBetweenLines? spacing = null;
        Indentation? indentation = null;
        SuppressLineNumbers? suppressLineNumbersElement = null;
        SuppressAutoHyphens? suppressAutoHyphensElement = null;
        ContextualSpacing? contextualSpacingElement = null;
        KeepLines? keepLinesElement = null;
        KeepNext? keepNextElement = null;
        WidowControl? widowControlEl = null;
        PageBreakBefore? pageBreakBeforeElement = null;
        MirrorIndents? mirrorIndentsElement = null;
        Shading? shadingElement = null;
        ParagraphBorders? pBdr = null;
        BiDi? bidi = null;
        FrameProperties? framePr = null;

        foreach (var child in props.ChildElements)
        {
            switch (child)
            {
                case Justification element:
                    justification ??= element;
                    break;
                case SpacingBetweenLines element:
                    spacing ??= element;
                    break;
                case Indentation element:
                    indentation ??= element;
                    break;
                case SuppressLineNumbers element:
                    suppressLineNumbersElement ??= element;
                    break;
                case SuppressAutoHyphens element:
                    suppressAutoHyphensElement ??= element;
                    break;
                case ContextualSpacing element:
                    contextualSpacingElement ??= element;
                    break;
                case KeepLines element:
                    keepLinesElement ??= element;
                    break;
                case KeepNext element:
                    keepNextElement ??= element;
                    break;
                case WidowControl element:
                    widowControlEl ??= element;
                    break;
                case PageBreakBefore element:
                    pageBreakBeforeElement ??= element;
                    break;
                case MirrorIndents element:
                    mirrorIndentsElement ??= element;
                    break;
                case Shading element:
                    shadingElement ??= element;
                    break;
                case ParagraphBorders element:
                    pBdr ??= element;
                    break;
                case BiDi element:
                    bidi ??= element;
                    break;
                case FrameProperties element:
                    framePr ??= element;
                    break;
            }
        }

        if (justification?.Val?.HasValue == true)
        {
            var justVal = justification.Val.Value;
            if (justVal == JustificationValues.Center)
            {
                alignment = TextAlignment.Center;
            }
            else if (justVal == JustificationValues.Right)
            {
                alignment = TextAlignment.Right;
            }
            else if (justVal == JustificationValues.Both || justVal == JustificationValues.Distribute)
            {
                alignment = TextAlignment.Justify;
            }
            else
            {
                alignment = TextAlignment.Left;
            }
        }

        if (spacing != null)
        {
            spacingBefore = spacing.Before.TwipsToPoints() ?? spacingBefore;
            spacingBefore = LineUnitsToPoints(spacing.BeforeLines) ?? spacingBefore;

            spacingAfter = spacing.After.TwipsToPoints() ?? spacingAfter;
            spacingAfter = LineUnitsToPoints(spacing.AfterLines) ?? spacingAfter;
            spacingBeforeAuto = spacing.BeforeAutoSpacing?.Value ?? spacingBeforeAuto;
            spacingAfterAuto = spacing.AfterAutoSpacing?.Value ?? spacingAfterAuto;

            if (spacing.Line?.HasValue == true)
            {
                var ruleValue = spacing.LineRule?.Value ?? LineSpacingRuleValues.Auto;

                if (ruleValue == LineSpacingRuleValues.Auto)
                {
                    // Line spacing in 240ths of a line
                    lineSpacingMultiplier = double.Parse(spacing.Line.Value!) / 240.0;
                    lineSpacingRule = LineSpacingRule.Auto;
                }
                else if (ruleValue == LineSpacingRuleValues.Exact)
                {
                    // Line spacing in twips (1/20 of a point)
                    lineSpacingPoints = double.Parse(spacing.Line.Value!) / OoxmlUnits.TwipsPerPoint;
                    lineSpacingRule = LineSpacingRule.Exactly;
                }
                else if (ruleValue == LineSpacingRuleValues.AtLeast)
                {
                    // Line spacing in twips (1/20 of a point)
                    lineSpacingPoints = double.Parse(spacing.Line.Value!) / OoxmlUnits.TwipsPerPoint;
                    lineSpacingRule = LineSpacingRule.AtLeast;
                }
            }
        }

        if (indentation != null)
        {
            firstLineIndent = indentation.FirstLine.TwipsToPoints() ?? firstLineIndent;

            leftIndent = indentation.Left.TwipsToPoints() ?? leftIndent;

            rightIndent = indentation.Right.TwipsToPoints() ?? rightIndent;

            hangingIndent = indentation.Hanging.TwipsToPoints() ?? hangingIndent;
        }

        suppressLineNumbers = suppressLineNumbersElement != null;
        suppressAutoHyphens = suppressAutoHyphensElement != null;

        // Contextual spacing collapses space between paragraphs with matching styles
        if (contextualSpacingElement != null)
        {
            contextualSpacing = true;
        }

        if (keepLinesElement != null)
        {
            keepLines = true;
        }

        if (keepNextElement != null)
        {
            keepNext = true;
        }

        // w:widowControl present without a w:val is on; w:val="0"/"false" turns it off.
        if (widowControlEl != null)
        {
            var valAttribute = widowControlEl.Val;
            if (valAttribute is {HasValue: true})
            {
                widowControl = valAttribute.Value;
            }
            else
            {
                widowControl = true;
            }
        }

        // Inline w:pageBreakBefore is an on/off toggle over the style value: bare presence turns it
        // on, w:val="0" turns a style-inherited break OFF (Word-probed, _probe_bp10_pbboff).
        if (pageBreakBeforeElement != null)
        {
            pageBreakBefore = pageBreakBeforeElement.Val?.Value ?? true;
        }

        var mirrorIndents = (styleDefaults?.MirrorIndents ?? false) || mirrorIndentsElement != null;
        var snapToGrid = props.GetFirstChild<SnapToGrid>() is { } snapElement ? snapElement.IsOn() : styleDefaults?.SnapToGrid ?? true;
        var fontAlignment = FontAlignmentOf(props.GetFirstChild<DocumentFormat.OpenXml.Wordprocessing.TextAlignment>()) ?? styleDefaults?.FontAlignment ?? FontAlignment.Baseline;

        // A mirror paragraph keeps its declared indents in the model. Which side each indent lands
        // on is a PAGE-PARITY question the Fragmenter answers at placement (Word-measured,
        // _probe_mirror2 sweeping hanging/firstLine on both parities): even pages render as
        // declared; odd pages mirror the box.

        if (shadingElement != null)
        {
            var inlineBgColor = ResolveThemeFill(shadingElement);

            if (inlineBgColor == null &&
                shadingElement.Fill?.HasValue == true &&
                shadingElement.Fill.Value != "auto" &&
                shadingElement.Fill.Value != "none")
            {
                inlineBgColor = shadingElement.Fill.Value;
            }

            if (inlineBgColor != null)
            {
                backgroundColor = inlineBgColor;
            }
        }

        var borders = styleDefaults?.Borders;
        var borderTopSpace = styleDefaults?.BorderTopSpacePoints ?? 0;
        var borderBottomSpace = styleDefaults?.BorderBottomSpacePoints ?? 0;
        var borderLeftSpace = styleDefaults?.BorderLeftSpacePoints ?? 0;
        var borderRightSpace = styleDefaults?.BorderRightSpacePoints ?? 0;
        var borderBetween = styleDefaults?.BorderBetween ?? BorderEdge.None;
        var borderBetweenSpace = styleDefaults?.BorderBetweenSpacePoints ?? 0;
        if (pBdr != null)
        {
            var topBorder = pBdr.GetFirstChild<TopBorder>();
            var rightBorder = pBdr.GetFirstChild<RightBorder>();
            var bottomBorder = pBdr.GetFirstChild<BottomBorder>();
            var leftBorder = pBdr.GetFirstChild<LeftBorder>();
            var betweenBorder = pBdr.GetFirstChild<BetweenBorder>();
            borders = new()
            {
                Top = ParseBorderEdge(topBorder),
                Right = ParseBorderEdge(rightBorder),
                Bottom = ParseBorderEdge(bottomBorder),
                Left = ParseBorderEdge(leftBorder)
            };
            borderTopSpace = ParseBorderSpace(topBorder);
            borderRightSpace = ParseBorderSpace(rightBorder);
            borderBottomSpace = ParseBorderSpace(bottomBorder);
            borderLeftSpace = ParseBorderSpace(leftBorder);
            borderBetween = ParseBorderEdge(betweenBorder);
            borderBetweenSpace = ParseBorderSpace(betweenBorder);
        }

        // The paragraph mark's size and face, which an empty paragraph's line is measured by (inside a
        // cell, the whole line height: the cell path omits ParagraphMarkRunProperties). Its own w:sz /
        // w:rFonts, else the ladder a run with no rPr climbs (ParseRunProperties): document defaults, a
        // styled table's rung, the paragraph style chain, and the defaults for an undefined style id.
        // business-plans/15's cost table (Univers marks with no w:sz, 9pt by the chain), business-plans/13
        // (marks whose w:rFonts names no w:ascii) and COMPASS's stocktake summary (an undefined TBLText
        // style, 9pt from the table style; Word's layout over COM, 2026-09-30) are the cases.
        double? paragraphMarkFontSize;
        string? paragraphMarkFontFamily;
        var paragraphMarkRunProps = props.ParagraphMarkRunProperties;
        var markLadder = ParseRunProperties(null, mainPart, styleId);
        if (paragraphMarkRunProps != null)
        {
            var fontSize = paragraphMarkRunProps.GetFirstChild<FontSize>();
            paragraphMarkFontSize = OoxmlUnits.FontSizeHalfPointsToPoints(fontSize?.Val?.Value) ?? markLadder.FontSizePoints;

            var markFonts = paragraphMarkRunProps.GetFirstChild<RunFonts>();
            paragraphMarkFontFamily = ResolveRunFontFamily(markFonts) ?? markLadder.FontFamily;
        }
        else
        {
            // A mark with no rPr at all — the bare <w:p/> spacer — still sizes its line by the ladder
            // (letters/13's empty paragraphs under a Posterama 11 docDefaults, a 14.63pt pitch).
            paragraphMarkFontSize = markLadder.FontSizePoints;
            paragraphMarkFontFamily = markLadder.FontFamily;
        }

        // RTL paragraph (w:bidi)
        var paraRtl = false;
        if (bidi != null)
        {
            paraRtl = bidi.IsOn();
        }

        // Parse drop cap (w:framePr/w:dropCap, w:framePr/w:lines).
        // The OOXML SDK exposes DropCap as a typed EnumValue, but reading via the raw attribute
        // is more reliable across SDK versions.
        var dropCap = DropCapPosition.None;
        var dropCapLines = 0;
        var frame = styleDefaults?.Frame;
        if (framePr != null)
        {
            var attributes = framePr.GetAttributes();
            var dropCapAttribute = attributes.AttributeValue("dropCap");

            // Word only honours a drop cap whose framePr also anchors the frame — its own
            // authoring always writes w:wrap/w:hAnchor/w:vAnchor beside w:dropCap. A bare
            // dropCap+lines (feature_capture/01) renders as a plain normal-size line in Word's
            // reference.
            var frameAnchored = attributes.AttributeValue("wrap") != null ||
                                attributes.AttributeValue("hAnchor") != null ||
                                attributes.AttributeValue("vAnchor") != null;
            if (frameAnchored &&
                string.Equals(dropCapAttribute, "drop", StringComparison.OrdinalIgnoreCase))
            {
                dropCap = DropCapPosition.Drop;
            }
            else if (frameAnchored &&
                     string.Equals(dropCapAttribute, "margin", StringComparison.OrdinalIgnoreCase))
            {
                dropCap = DropCapPosition.Margin;
            }

            if (int.TryParse(attributes.AttributeValue("lines"), out var parsedLines))
            {
                dropCapLines = parsedLines;
            }

            // Frame positioning. When the paragraph's style carries a frame, its positioning is
            // authoritative: Word's editor re-emits a direct framePr full of neutral defaults
            // (xAlign="left", yAlign="inline", vAnchor="margin") whenever the framed paragraph is
            // touched, and those defaults must not clobber the style's real placement (observed
            // across the agendas / letters / labels templates, where the styled frame's xAlign and
            // anchors are what Word actually renders). So prefer the style frame; only parse the
            // direct framePr as the frame when the style has none.
            frame = styleDefaults?.Frame ?? ParseParagraphFrame(framePr);
        }

        if (spacingBeforeAuto || spacingAfterAuto)
        {
            (spacingBefore, spacingAfter) = ResolveAutoSpacing(paragraph, spacingBefore, spacingAfter, spacingBeforeAuto, spacingAfterAuto);
        }

        return new()
        {
            Alignment = alignment,
            SpacingBeforePoints = spacingBefore,
            SpacingAfterPoints = spacingAfter,
            SpacingBeforeAuto = spacingBeforeAuto,
            SpacingAfterAuto = spacingAfterAuto,
            LineSpacingMultiplier = lineSpacingMultiplier,
            LineSpacingPoints = lineSpacingPoints,
            LineSpacingRule = lineSpacingRule,
            FirstLineIndentPoints = firstLineIndent,
            LeftIndentPoints = leftIndent,
            RightIndentPoints = rightIndent,
            HangingIndentPoints = hangingIndent,
            SuppressLineNumbers = suppressLineNumbers,
            SuppressAutoHyphens = suppressAutoHyphens,
            AutoHyphenation = AutoHyphenationFor(suppressAutoHyphens),
            ContextualSpacing = contextualSpacing,
            KeepLines = keepLines,
            KeepNext = keepNext,
            WidowControl = widowControl,
            PageBreakBefore = pageBreakBefore,
            ParagraphMarkFontSizePoints = paragraphMarkFontSize,
            ParagraphMarkFontFamily = paragraphMarkFontFamily,
            ParagraphMarkRunProperties = paragraphMark,
            BackgroundColorHex = backgroundColor,
            StyleId = styleId,
            ResolvedStyleId = effectiveStyleId,
            InDefaultStyle = effectiveStyleId == defaultParagraphStyleId,
            Borders = borders,
            BorderTopSpacePoints = borderTopSpace,
            BorderBottomSpacePoints = borderBottomSpace,
            BorderLeftSpacePoints = borderLeftSpace,
            BorderRightSpacePoints = borderRightSpace,
            BorderBetween = borderBetween,
            BorderBetweenSpacePoints = borderBetweenSpace,
            TabStops = ParseTabs(props, styleDefaults?.TabStops ?? []),
            DefaultTabStopPoints = defaultTabStopPoints,
            DropCap = dropCap,
            DropCapLines = dropCapLines,
            Frame = frame,
            IsRightToLeft = paraRtl,
            MirrorIndents = mirrorIndents,
            SnapToGrid = snapToGrid,
            FontAlignment = fontAlignment,
            GridLinePitchPoints = snapToGrid &&
                                  SnapsToGrid(paragraph) ? documentGridPitchPoints : 0
        };
    }

    // Whether a paragraph in this place snaps to the document grid. Word-probed (_probe_grid_table /
    // _probe_grid_table_adjust, 2026-10-21, 36pt grid): body paragraphs do, a header's do not, and a
    // table cell's do only under w:adjustLineHeightInTable.
    bool SnapsToGrid(Paragraph? paragraph)
    {
        if (documentGridPitchPoints <= 0 ||
            paragraph == null)
        {
            return false;
        }

        foreach (var ancestor in paragraph.Ancestors())
        {
            switch (ancestor)
            {
                case Header:
                case Footer:
                case DocumentFormat.OpenXml.Wordprocessing.Footnote:
                case DocumentFormat.OpenXml.Wordprocessing.Endnote:
                    return false;
                case DocumentFormat.OpenXml.Wordprocessing.TableCell when !adjustLineHeightInTable:
                    return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Parses the positioning subset of a <c>w:framePr</c> (anchors, alignment, explicit offset,
    /// and size) into a <see cref="ParagraphFrame"/>, layering present attributes over
    /// <paramref name="baseFrame"/> (the style's frame). Returns null when neither the element nor
    /// the base carries any positioning signal — i.e. a drop-cap-only frame — so drop-cap handling
    /// is left untouched.
    /// </summary>
    static ParagraphFrame? ParseParagraphFrame(FrameProperties framePr, ParagraphFrame? baseFrame = null)
    {
        var attributes = framePr.GetAttributes();

        var hAnchorRaw = attributes.AttributeValue("hAnchor");
        var vAnchorRaw = attributes.AttributeValue("vAnchor");
        var xAlignRaw = attributes.AttributeValue("xAlign");
        var yAlignRaw = attributes.AttributeValue("yAlign");
        var xRaw = attributes.AttributeValue("x");
        var yRaw = attributes.AttributeValue("y");
        var widthRaw = attributes.AttributeValue("w");
        var heightRaw = attributes.AttributeValue("h");

        // Only treat this as a positioning frame when this element or the inherited style frame
        // carries a positioning attribute. A bare drop-cap frame (just dropCap/lines) with no
        // inherited frame must NOT become a ParagraphFrame.
        var hasPositioning = hAnchorRaw != null || vAnchorRaw != null ||
                             xAlignRaw != null || yAlignRaw != null ||
                             xRaw != null || yRaw != null ||
                             widthRaw != null || heightRaw != null;
        if (!hasPositioning &&
            baseFrame == null)
        {
            return null;
        }

        return new()
        {
            HorizontalAnchor = hAnchorRaw switch
            {
                "page" => HorizontalAnchor.Page,
                "margin" => HorizontalAnchor.Margin,
                // "text" anchors to the text column's leading edge.
                "text" or "column" => HorizontalAnchor.Column,
                _ => baseFrame?.HorizontalAnchor ?? HorizontalAnchor.Column
            },
            VerticalAnchor = vAnchorRaw switch
            {
                "page" => VerticalAnchor.Page,
                "text" => VerticalAnchor.Paragraph,
                "margin" => VerticalAnchor.Margin,
                _ => baseFrame?.VerticalAnchor ?? VerticalAnchor.Margin
            },
            HorizontalAlignment = xAlignRaw switch
            {
                "left" or "inside" => FrameHorizontalAlignment.Left,
                "center" => FrameHorizontalAlignment.Center,
                "right" or "outside" => FrameHorizontalAlignment.Right,
                _ => baseFrame?.HorizontalAlignment ?? FrameHorizontalAlignment.None
            },
            VerticalAlignment = yAlignRaw switch
            {
                "top" or "inside" => FrameVerticalAlignment.Top,
                "center" => FrameVerticalAlignment.Center,
                "bottom" or "outside" => FrameVerticalAlignment.Bottom,
                "inline" => FrameVerticalAlignment.Inline,
                _ => baseFrame?.VerticalAlignment ?? FrameVerticalAlignment.None
            },
            XPoints = int.TryParse(xRaw, out var xTwips) ? xTwips / OoxmlUnits.TwipsPerPoint : baseFrame?.XPoints ?? 0,
            YPoints = int.TryParse(yRaw, out var yTwips) ? yTwips / OoxmlUnits.TwipsPerPoint : baseFrame?.YPoints ?? 0,
            WidthPoints = int.TryParse(widthRaw, out var widthTwips) &&
                          widthTwips > 0 ? widthTwips / OoxmlUnits.TwipsPerPoint : baseFrame?.WidthPoints,
            HeightPoints = int.TryParse(heightRaw, out var heightTwips) &&
                           heightTwips > 0 ? heightTwips / OoxmlUnits.TwipsPerPoint : baseFrame?.HeightPoints
        };
    }
}
