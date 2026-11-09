using OoxmlRun = DocumentFormat.OpenXml.Wordprocessing.Run;

/// <summary>
/// A run: its properties, revisions, hints, text effects and content controls.
/// </summary>
sealed partial class DocumentParser
{
    /// <summary>
    /// Reads a <c>w:themeShade</c>-style hex byte, or null when absent or malformed.
    /// </summary>
    static byte? ThemeByte(StringValue? value)
    {
        if (value?.HasValue == true &&
            byte.TryParse(value.Value, NumberStyles.HexNumber, null, out var parsed))
        {
            return parsed;
        }

        return null;
    }

    /// <summary>
    /// Resolves a <c>w:shd</c>'s <c>w:themeFill</c> against the document theme, honouring the
    /// <c>w:themeFillShade</c> / <c>w:themeFillTint</c> bytes that go with it.
    /// </summary>
    /// <remarks>
    /// Run shading, paragraph shading and both inline-background paths read it, so a tint of an
    /// accent never paints as the flat accent (<c>_probe_themeshade</c>, twelve shaded bands). It is
    /// <c>w:color</c>'s reading of its shade and tint bytes, for the fill.
    /// </remarks>
    string? ResolveThemeFill(Shading shadingElement)
    {
        var themeFill = shadingElement.ThemeFill?.Value;
        if (themeFill == null || currentThemeColors == null)
        {
            return null;
        }

        return currentThemeColors.ResolveColor(
            ((IEnumValue) themeFill).Value,
            ThemeByte(shadingElement.ThemeFillShade),
            ThemeByte(shadingElement.ThemeFillTint));
    }

    // Resolves a w:color element to a 6-hex string: a theme colour (w:themeColor with optional
    // w:themeShade/w:themeTint) via the document's colour scheme, else the direct w:val. Returns
    // null for "auto" or an unresolved/absent value.
    string? ResolveRunColor(Color colorElement)
    {
        var themeColor = colorElement.ThemeColor?.Value;
        if (themeColor != null &&
            currentThemeColors != null)
        {
            var shade = ThemeByte(colorElement.ThemeShade);
            var tint = ThemeByte(colorElement.ThemeTint);

            var resolved = currentThemeColors.ResolveColor(((IEnumValue) themeColor).Value, shade, tint);
            if (resolved != null)
            {
                return resolved;
            }
        }

        if (colorElement.Val?.HasValue == true &&
            colorElement.Val.Value != "auto")
        {
            return colorElement.Val.Value;
        }

        return null;
    }

    // Maps the named-colour palette of <w:highlight w:val="..."/> to RGB hex strings.
    // Word's highlight pen uses a fixed palette (not arbitrary RGB), so mapping is
    // deterministic and fixed by the spec — the colours match the highlighter swatches
    // in Word's Home → Text Highlight Color picker.
    static string? HighlightToHex(HighlightColorValues value)
    {
        if (value == HighlightColorValues.Yellow) return "FFFF00";
        if (value == HighlightColorValues.Green) return "00FF00";
        if (value == HighlightColorValues.Cyan) return "00FFFF";
        if (value == HighlightColorValues.Magenta) return "FF00FF";
        if (value == HighlightColorValues.Blue) return "0000FF";
        if (value == HighlightColorValues.Red) return "FF0000";
        if (value == HighlightColorValues.DarkBlue) return "000080";
        if (value == HighlightColorValues.DarkCyan) return "008080";
        if (value == HighlightColorValues.DarkGreen) return "008000";
        if (value == HighlightColorValues.DarkMagenta) return "800080";
        if (value == HighlightColorValues.DarkRed) return "800000";
        if (value == HighlightColorValues.DarkYellow) return "808000";
        if (value == HighlightColorValues.DarkGray) return "808080";
        if (value == HighlightColorValues.LightGray) return "C0C0C0";
        if (value == HighlightColorValues.Black) return "000000";
        if (value == HighlightColorValues.White) return "FFFFFF";
        return null;
    }

    // w:w as a percent, within the 1-600 the schema allows; null when absent or unreadable.
    static double? CharacterScaleOf(CharacterScale? scale)
    {
        if (scale?.Val?.Value is not { } percent || percent <= 0)
        {
            return null;
        }

        return Math.Clamp(percent, 1, 600);
    }

    // w:u/@w:val as the style it draws in (TextDecorations); a value Word has and Morph does not, or
    // none at all, draws single.
    static UnderlineStyle UnderlineStyleOf(Underline underline)
    {
        if (underline.Val?.Value is { } value &&
            underlineStyles.TryGetValue(value, out var style))
        {
            return style;
        }

        return UnderlineStyle.Single;
    }

    static readonly Dictionary<UnderlineValues, UnderlineStyle> underlineStyles = new()
    {
        [UnderlineValues.Words] = UnderlineStyle.Words,
        [UnderlineValues.Double] = UnderlineStyle.Double,
        [UnderlineValues.Thick] = UnderlineStyle.Thick,
        [UnderlineValues.Dotted] = UnderlineStyle.Dotted,
        [UnderlineValues.DottedHeavy] = UnderlineStyle.DottedHeavy,
        [UnderlineValues.Dash] = UnderlineStyle.Dash,
        [UnderlineValues.DashedHeavy] = UnderlineStyle.DashedHeavy,
        [UnderlineValues.DashLong] = UnderlineStyle.DashLong,
        [UnderlineValues.DashLongHeavy] = UnderlineStyle.DashLongHeavy,
        [UnderlineValues.DotDash] = UnderlineStyle.DotDash,
        [UnderlineValues.DashDotHeavy] = UnderlineStyle.DashDotHeavy,
        [UnderlineValues.DotDotDash] = UnderlineStyle.DotDotDash,
        [UnderlineValues.DashDotDotHeavy] = UnderlineStyle.DashDotDotHeavy,
        [UnderlineValues.Wave] = UnderlineStyle.Wave,
        [UnderlineValues.WavyHeavy] = UnderlineStyle.WavyHeavy,
        [UnderlineValues.WavyDouble] = UnderlineStyle.WavyDouble
    };

    // w:textAlignment as the vertical placement of a line's smaller runs; null when absent. Auto is the
    // baseline for horizontal text.
    static FontAlignment? FontAlignmentOf(DocumentFormat.OpenXml.Wordprocessing.TextAlignment? alignment)
    {
        var value = alignment?.Val?.Value;
        if (value == null)
        {
            return null;
        }

        if (value == VerticalTextAlignmentValues.Top)
        {
            return FontAlignment.Top;
        }

        if (value == VerticalTextAlignmentValues.Center)
        {
            return FontAlignment.Center;
        }

        if (value == VerticalTextAlignmentValues.Bottom)
        {
            return FontAlignment.Bottom;
        }

        return FontAlignment.Baseline;
    }

    static LigatureMode ParseLigatureMode(DocumentFormat.OpenXml.Office2010.Word.Ligatures? element)
    {
        if (element?.Val?.Value is not { } val)
        {
            return LigatureMode.Standard;
        }

        var v = DocumentFormat.OpenXml.Office2010.Word.LigaturesValues.None;
        if (val == v) return LigatureMode.None;
        if (val == DocumentFormat.OpenXml.Office2010.Word.LigaturesValues.Standard) return LigatureMode.Standard;
        if (val == DocumentFormat.OpenXml.Office2010.Word.LigaturesValues.Contextual) return LigatureMode.Contextual;
        if (val == DocumentFormat.OpenXml.Office2010.Word.LigaturesValues.Historical) return LigatureMode.Historical;
        if (val == DocumentFormat.OpenXml.Office2010.Word.LigaturesValues.Discretional) return LigatureMode.Discretional;
        if (val == DocumentFormat.OpenXml.Office2010.Word.LigaturesValues.StandardContextual) return LigatureMode.Standard | LigatureMode.Contextual;
        if (val == DocumentFormat.OpenXml.Office2010.Word.LigaturesValues.StandardHistorical) return LigatureMode.Standard | LigatureMode.Historical;
        if (val == DocumentFormat.OpenXml.Office2010.Word.LigaturesValues.StandardDiscretional) return LigatureMode.Standard | LigatureMode.Discretional;
        if (val == DocumentFormat.OpenXml.Office2010.Word.LigaturesValues.ContextualHistorical) return LigatureMode.Contextual | LigatureMode.Historical;
        if (val == DocumentFormat.OpenXml.Office2010.Word.LigaturesValues.ContextualDiscretional) return LigatureMode.Contextual | LigatureMode.Discretional;
        if (val == DocumentFormat.OpenXml.Office2010.Word.LigaturesValues.HistoricalDiscretional) return LigatureMode.Historical | LigatureMode.Discretional;
        if (val == DocumentFormat.OpenXml.Office2010.Word.LigaturesValues.StandardContextualHistorical) return LigatureMode.Standard | LigatureMode.Contextual | LigatureMode.Historical;
        if (val == DocumentFormat.OpenXml.Office2010.Word.LigaturesValues.StandardContextualDiscretional) return LigatureMode.Standard | LigatureMode.Contextual | LigatureMode.Discretional;
        if (val == DocumentFormat.OpenXml.Office2010.Word.LigaturesValues.StandardHistoricalDiscretional) return LigatureMode.Standard | LigatureMode.Historical | LigatureMode.Discretional;
        if (val == DocumentFormat.OpenXml.Office2010.Word.LigaturesValues.ContextualHistoricalDiscretional) return LigatureMode.Contextual | LigatureMode.Historical | LigatureMode.Discretional;
        if (val == DocumentFormat.OpenXml.Office2010.Word.LigaturesValues.All) return LigatureMode.All;
        return LigatureMode.Standard;
    }

    /// <summary>
    /// Checks if an SdtRun is a specific content control type that should be rendered as a ContentControlElement.
    /// Returns true for checkboxes, combo boxes, dropdowns, date pickers, and plain text controls.
    /// Returns false for generic rich text containers that should just have their runs extracted.
    /// </summary>
    static bool IsContentControlType(SdtRun sdtRun)
    {
        var props = sdtRun.SdtProperties;
        if (props == null)
        {
            return false;
        }

        // A picture control renders an image the cached runs cannot carry — always the control path.
        if (props.GetFirstChild<SdtContentPicture>() != null)
        {
            return true;
        }

        // Every other inline control — checkbox (w14:checkbox), combo box, dropdown, date, plain
        // text — prints as its cached content, laid out inline with the rest of the paragraph:
        // Word's print output for content_control_inline is plain "☒ Yes", "Medium", "2025-06-15"
        // with no box chrome, on the label's own line. So once the control HOLDS runs, those runs
        // are the value and flow inline. Only an EMPTY control takes the block-control path, which
        // can render what the cached content genuinely cannot supply — the checkbox glyph from its
        // checked state, a date formatted from w:fullDate, or the grayed placeholder.
        if (props.Descendants().Any(_ => _.LocalName == "checkbox") ||
            props.GetFirstChild<SdtContentComboBox>() != null ||
            props.GetFirstChild<SdtContentDropDownList>() != null ||
            props.GetFirstChild<SdtContentDate>() != null ||
            props.GetFirstChild<SdtContentText>() != null)
        {
            return sdtRun.SdtContentRun?.Descendants<OoxmlRun>().All(_ => _.InnerText.Length == 0) ?? true;
        }

        return false;
    }

    /// <summary>
    /// Parses a content control (SdtRun) to extract form control information.
    /// </summary>
    ContentControlElement? ParseSdtRun(SdtRun sdtRun, MainDocumentPart mainPart, string? paragraphStyleId = null)
    {
        var props = sdtRun.SdtProperties;
        if (props == null)
        {
            return null;
        }

        // Determine control type
        var controlType = ContentControlType.RichText;
        string? tag = null;
        string? title = null;
        string? placeholder = null;
        bool? isChecked = null;
        List<string>? listItems = null;
        DateTime? dateValue = null;
        string? dateFormat = null;

        // Get tag and title
        var tagElement = props.GetFirstChild<Tag>();
        if (tagElement?.Val?.HasValue == true)
        {
            tag = tagElement.Val.Value;
        }

        var aliasElement = props.GetFirstChild<SdtAlias>();
        if (aliasElement?.Val?.HasValue == true)
        {
            title = aliasElement.Val.Value;
        }

        // Check for specific control types using Office 2010 Word namespace
        var checkbox14 = props.Descendants()
            .FirstOrDefault(_ => _.LocalName == "checkbox");
        if (checkbox14 != null)
        {
            controlType = ContentControlType.CheckBox;
            var checkedElement = checkbox14.Descendants()
                .FirstOrDefault(_ => _.LocalName == "checked");
            var checkedVal = checkedElement?.AttributeValue("val");
            isChecked = checkedVal is "1" or "true";
        }
        else if (props.GetFirstChild<SdtContentComboBox>() != null)
        {
            controlType = ContentControlType.ComboBox;
            var combo = props.GetFirstChild<SdtContentComboBox>();
            listItems = combo?.Elements<ListItem>()
                .Select(li => li.DisplayText?.Value ?? li.Value?.Value ?? "")
                .ToList();
        }
        else if (props.GetFirstChild<SdtContentDropDownList>() != null)
        {
            controlType = ContentControlType.DropDownList;
            var dropdown = props.GetFirstChild<SdtContentDropDownList>();
            listItems = dropdown?.Elements<ListItem>()
                .Select(li => li.DisplayText?.Value ?? li.Value?.Value ?? "")
                .ToList();
        }
        else if (props.GetFirstChild<SdtContentDate>() != null)
        {
            controlType = ContentControlType.Date;
            var dateControl = props.GetFirstChild<SdtContentDate>();
            var fullDateVal = dateControl?.FullDate?.Value;
            if (fullDateVal.HasValue)
            {
                dateValue = fullDateVal.Value;
            }

            // Capture w:dateFormat for the empty-run-text fallback. Word displays the run text
            // verbatim, so this is only used when the control carries a fullDate but no content.
            dateFormat = dateControl?.GetFirstChild<DateFormat>()?.Val?.Value;
        }
        else if (props.GetFirstChild<SdtContentText>() != null)
        {
            controlType = ContentControlType.PlainText;
        }
        else if (props.GetFirstChild<SdtContentPicture>() != null)
        {
            controlType = ContentControlType.Picture;
        }

        // Get placeholder text
        var placeholderElement = props.GetFirstChild<SdtPlaceholder>();
        if (placeholderElement != null)
        {
            var docPartElement = placeholderElement.GetFirstChild<DocPartGallery>();
            placeholder = docPartElement?.Val?.Value;
        }

        // Get content - extract styled runs to preserve formatting
        var content = "";
        var styledRuns = new List<Run>();
        var sdtContent = sdtRun.SdtContentRun;
        if (sdtContent != null)
        {
            // Parse each run with full styling, inheriting from paragraph style
            foreach (var run in sdtContent.Descendants<OoxmlRun>())
            {
                // Check for line breaks within the run. The run may also contain text after
                // the break (e.g. <w:r><w:br/><w:t>Sharma</w:t></w:r>) — emit a newline run
                // for the break, then fall through to also parse the text content so neither
                // half of the run is dropped.
                var breakElement = run.GetFirstChild<Break>();
                if (breakElement != null &&
                    breakElement.Type?.Value != BreakValues.Page &&
                    breakElement.Type?.Value != BreakValues.Column)
                {
                    var runProps = ParseRunProperties(run.RunProperties, mainPart);
                    styledRuns.Add(
                        new()
                        {
                            Text = "\n",
                            Properties = runProps
                        });
                }

                styledRuns.AddRange(ParseRun(run, mainPart, paragraphStyleId));
            }

            // Also build plain text content for backward compatibility
            content = string.Concat(styledRuns.Select(_ => _.Text));
        }

        return new()
        {
            ControlType = controlType,
            Tag = tag,
            Title = title,
            PlaceholderText = placeholder,
            Content = content,
            Runs = styledRuns.Count > 0 ? styledRuns : null,
            Checked = isChecked,
            ListItems = listItems,
            DateValue = dateValue,
            DateFormat = dateFormat,
            // Default width
            WidthPoints = 100
        };
    }

    // Unicode characters for hyphenation
    // Soft hyphen (optional break point)
    const string softHyphen = "\u00AD";

    // Non-breaking hyphen
    const string nonBreakingHyphen = "\u2011";

    string? ResolveHyperlinkUrl(Hyperlink hyperlink, MainDocumentPart mainPart)
    {
        // External links carry an r:id pointing at a HyperlinkRelationship; internal links to a
        // bookmark carry a w:anchor. Combine both: an external target may also include a sub-anchor.
        string? target = null;
        if (hyperlink.Id?.Value is { } relationshipId)
        {
            if (hyperlinkUrlsByRelId == null)
            {
                hyperlinkUrlsByRelId = new(StringComparer.Ordinal);
                foreach (var relationship in mainPart.HyperlinkRelationships)
                {
                    hyperlinkUrlsByRelId.TryAdd(
                        relationship.Id,
                        relationship.Uri.IsAbsoluteUri ? relationship.Uri.AbsoluteUri : relationship.Uri.OriginalString);
                }
            }

            hyperlinkUrlsByRelId.TryGetValue(relationshipId, out target);
        }

        if (hyperlink.Anchor?.Value is {Length: > 0} anchor)
        {
            if (target == null)
            {
                return $"#{anchor}";
            }

            return $"{target}#{anchor}";
        }

        return target;
    }

    // Word's revision colour for a single author, sampled from its own render of
    // tracked_changes/01 at 150 DPI: (209, 52, 56). Word cycles a palette per author; only the
    // first is modelled, which covers every corpus document (one carries tracked changes at all).
    const string revisionColorHex = "D13438";

    // Which tracked-change mark a run carries, if any.
    enum RevisionMark
    {
        None,
        Inserted,
        Deleted
    }

    // The mark a run takes is read off the markup around it, not off how the parser came to it: runs are
    // reached through a content control, a hyperlink or a field as often as through the paragraph, and
    // a revision wraps them just the same inside any of those. The nearest revision is the one that
    // counts — text one reviewer inserted and another deleted is drawn deleted. A text box's runs stop
    // at their own paragraph, so a box anchored in revised text is not itself revised.
    static RevisionMark RevisionOf(OoxmlRun run)
    {
        for (var parent = run.Parent; parent != null && parent is not Paragraph; parent = parent.Parent)
        {
            switch (parent)
            {
                case DeletedRun or MoveFromRun:
                    return RevisionMark.Deleted;
                case InsertedRun or MoveToRun:
                    return RevisionMark.Inserted;
            }
        }

        return RevisionMark.None;
    }

    // The runs inside a revision or a hyperlink, through any revisions nested in it: a deletion inside
    // an insertion, either inside a link. (The schema puts a revision inside a link, never around one.)
    static IEnumerable<OoxmlRun> RunsWithin(OpenXmlElement container)
    {
        foreach (var child in container.ChildElements)
        {
            switch (child)
            {
                case OoxmlRun run:
                    yield return run;
                    break;
                case InsertedRun or DeletedRun or MoveToRun or MoveFromRun:
                    foreach (var nested in RunsWithin(child))
                    {
                        yield return nested;
                    }

                    break;
            }
        }
    }

    RunProperties ApplyComment(RunProperties properties, OoxmlRun run)
    {
        if (commentedRuns != null &&
            commentedRuns.TryGetValue(run, out var id))
        {
            return properties with
            {
                CommentId = id
            };
        }

        return properties;
    }

    // A run whose formatting a tracked change altered (w:rPrChange) keeps its new look, uncoloured,
    // and takes Word's change bar beside its line as an inserted or deleted run does.
    static RunProperties ApplyFormatRevision(RunProperties properties, OoxmlRun run) =>
        !properties.IsRevisionMark &&
        run.RunProperties?.GetFirstChild<RunPropertiesChange>() != null
            ? properties with
            {
                IsRevisionMark = true
            }
            : properties;

    // An insertion is underlined and a deletion struck through, both recoloured. Layered over the
    // run's own properties so its font, size and weight survive.
    static RunProperties ApplyRevisionMark(RunProperties properties, RevisionMark mark) =>
        mark switch
        {
            RevisionMark.Inserted => properties with
            {
                ColorHex = revisionColorHex,
                Underline = true,
                IsRevisionMark = true
            },
            RevisionMark.Deleted => properties with
            {
                ColorHex = revisionColorHex,
                Strikethrough = true,
                IsRevisionMark = true
            },
            _ => properties
        };

    /// <summary>
    /// The East Asian face a <c>w:rFonts</c> names — <c>w:eastAsia</c> directly, or the theme slot
    /// <c>w:eastAsiaTheme</c> points at. Null when it names neither.
    /// </summary>
    string? EastAsiaFontFamily(RunFonts? fonts)
    {
        if (fonts == null)
        {
            return null;
        }

        if (fonts.EastAsiaTheme?.HasValue == true &&
            currentThemeFonts != null)
        {
            var themeValue = ((IEnumValue) fonts.EastAsiaTheme.Value).Value;
            if (currentThemeFonts.ResolveFont(themeValue) is { } themed)
            {
                return themed;
            }
        }

        if (fonts.EastAsia?.HasValue == true)
        {
            return fonts.EastAsia.Value;
        }

        return null;
    }

    /// <summary>
    /// <c>w:rFonts/@w:hint="eastAsia"</c> (ECMA-376 §17.3.2.26): characters whose script is
    /// ambiguous — the general punctuation, symbol, arrow, box-drawing, geometric-shape and dingbat
    /// blocks from U+2010 to U+2FFF, the CJK blocks above U+3000, and a handful of Latin-1
    /// punctuation — draw in the run's East Asian face rather than its ASCII/high-ANSI one. A run
    /// made only of such characters is switched here; a run mixing them with Latin text keeps its
    /// Latin face, since the model carries one face per run.
    ///
    /// Word-read on wedding/10, whose two checkbox runs carry <c>hint="eastAsia"</c> with an
    /// <c>eastAsia="MS Gothic"</c> and no ASCII face and render Word's MS Gothic ☐ (U+2610) like
    /// the 134 rows that name MS Gothic outright; and on wedding/04, whose checkbox cells carry the
    /// bare hint and take the docDefaults <c>eastAsia="MS Mincho"</c>.
    /// </summary>
    RunProperties ApplyEastAsiaHint(RunProperties properties, RunFonts? fonts, string text)
    {
        if (fonts?.Hint?.Value != FontTypeHintValues.EastAsia || text.Length == 0)
        {
            return properties;
        }

        var hinted = false;
        foreach (var ch in text)
        {
            if (ch == ' ')
            {
                continue;
            }

            if (!IsEastAsiaHinted(ch))
            {
                return properties;
            }

            hinted = true;
        }

        if (!hinted)
        {
            return properties;
        }

        var family = EastAsiaFontFamily(fonts) ?? defaultEastAsiaFontFamily;
        if (family == null || string.Equals(family, properties.FontFamily, StringComparison.OrdinalIgnoreCase))
        {
            return properties;
        }

        return properties with
        {
            FontFamily = family
        };
    }

    static bool IsEastAsiaHinted(char ch) =>
        ch is >= '\u3000' or >= '\u2010' and <= '\u2FFF' or '\u00A1' or '\u00A4' or '\u00A7' or '\u00A8' or '\u00AA' or '\u00AD' or '\u00AF'
            or >= '\u00B0' and <= '\u00B4' or >= '\u00B6' and <= '\u00BA' or >= '\u00BC' and <= '\u00BF'
            or '\u00D7' or '\u00F7';

    List<Run> ParseRun(OoxmlRun run, MainDocumentPart mainPart, string? paragraphStyleId = null, string? hyperlinkUrl = null, bool emitLineBreaks = false)
    {
        var result = new List<Run>();
        RunProperties? properties = null;

        RunProperties GetProperties() =>
            properties ??= ApplyComment(
                ApplyFormatRevision(
                    ApplyRevisionMark(
                        ParseRunProperties(run.RunProperties, mainPart, paragraphStyleId),
                        RevisionOf(run)),
                    run),
                run);

        // w:vanish / w:specVanish — drop hidden runs at parse time so they don't enter
        // measurement or rendering. Cheaper than filtering at every render call site.
        if (GetProperties().Hidden)
        {
            return result;
        }

        // Walk children in order so w:tab splits the run into separate model Runs (text, tab, text, ...)
        var textBuilder = new StringBuilder();

        // Where each model run's text sits in this w:r, for Run.Source. position counts what the
        // children walked so far took (SourceRuns.Length, the count the review code splits a run by),
        // and textStart is where the text being gathered began. Everything that takes a position
        // either joins the gathered text or flushes it first, so a model run's characters are always
        // consecutive positions.
        var sourceRun = 0;
        var stamped = runOrdinals?.TryGetValue(run, out sourceRun) == true;
        var position = 0;
        var textStart = 0;

        RunSource? SourceAt(int start, bool atomic = false)
        {
            if (!stamped)
            {
                return null;
            }

            return new RunSource(sourceRun, start, atomic);
        }

        void GatherText(string text)
        {
            if (textBuilder.Length == 0)
            {
                textStart = position;
            }

            textBuilder.Append(text);
        }

        void FlushText()
        {
            if (textBuilder.Length == 0)
            {
                return;
            }

            var text = textBuilder.ToString();
            result.Add(
                new()
                {
                    Text = text,
                    Properties = ApplyEastAsiaHint(GetProperties(), run.RunProperties?.GetFirstChild<RunFonts>(), text),
                    HyperlinkUrl = hyperlinkUrl,
                    Source = SourceAt(textStart)
                });
            textBuilder.Clear();
        }

        foreach (var child in run.ChildElements)
        {
            switch (child)
            {
                case Text textElement:
                    GatherText(SourceRuns.EffectiveText(textElement.Text, textElement.Space?.Value).ReplaceSeparatorsWithSpace());
                    break;
                // w:delText carries the text of a tracked deletion: a sibling type of w:t, not a subclass.
                case DeletedText deletedText:
                    GatherText(SourceRuns.EffectiveText(deletedText.Text, deletedText.Space?.Value).ReplaceSeparatorsWithSpace());
                    break;
                case SoftHyphen:
                    GatherText(softHyphen);
                    break;
                case NoBreakHyphen:
                    GatherText(nonBreakingHyphen);
                    break;
                // A w:sym — Word's Insert Symbol, and how a generated checkbox is often written
                // (Wingdings F0FE ☑, F06F ☐). A run of its own in the named font, which layout draws
                // from that font or, where it is missing, from the character's Unicode equivalent.
                case SymbolChar symbolChar when ParseSymbol(symbolChar, GetProperties().FontFamily) is { } symbol:
                    FlushText();
                    result.Add(
                        new()
                        {
                            Text = SymbolFonts.Text(symbol.Font, symbol.Code),
                            Properties = GetProperties() with
                            {
                                FontFamily = symbol.Font
                            },
                            Symbol = symbol,
                            HyperlinkUrl = hyperlinkUrl
                        });
                    break;
                case TabChar:
                    FlushText();
                    result.Add(
                        new()
                        {
                            Text = "\t",
                            Properties = GetProperties(),
                            IsTab = true,
                            HyperlinkUrl = hyperlinkUrl,
                            Source = SourceAt(position)
                        });
                    break;
                // w:ptab — an absolute position tab. Carried through as a tab run with the position
                // attached, since where it lands depends on the measure the paragraph ends up in.
                case DocumentFormat.OpenXml.Wordprocessing.PositionalTab positionalTab:
                    FlushText();
                    result.Add(
                        new()
                        {
                            Text = "\t",
                            Properties = GetProperties(),
                            IsTab = true,
                            PositionalTab = new()
                            {
                                Alignment = MapPositionalTabAlignment(positionalTab.Alignment?.InnerText),
                                Leader = MapTabLeader(positionalTab.Leader?.InnerText),
                                RelativeTo = MapPositionalTabBase(positionalTab.RelativeTo?.InnerText)
                            },
                            HyperlinkUrl = hyperlinkUrl,
                            Source = SourceAt(position)
                        });
                    break;
                // A w:footnoteReference / w:endnoteReference marks where a note is cited. The note
                // body lives in footnotes.xml / endnotes.xml (ExtractFootnotes / ExtractEndnotes);
                // here we only record the citation position as an empty marker run so the text
                // exporters can emit a reference and a trailing notes section.
                case FootnoteReference { Id.Value: { } footnoteId }:
                    FlushText();
                    var footnoteKey = footnoteId.ToString();
                    if (!footnoteCitationNumbers.TryGetValue(footnoteKey, out var footnoteNumber))
                    {
                        footnoteNumber = ++footnotesInSection;
                        footnoteCitationNumbers[footnoteKey] = footnoteNumber;
                    }

                    result.Add(
                        new()
                        {
                            // The visible reference mark: the citation ordinal in the document's
                            // footnote number format, superscripted - the raster/PDF backends draw
                            // the run as-is. The exporters branch on FootnoteReferenceId before
                            // Text, so they keep their own markers.
                            Text = NumberingParser.FormatNumber(footnoteNumber, footnoteNumberFormat),
                            Properties = GetProperties() with
                            {
                                VerticalAlignment = VerticalRunAlignment.Superscript
                            },
                            HyperlinkUrl = hyperlinkUrl,
                            FootnoteReferenceId = footnoteKey,
                            Source = SourceAt(position, atomic: true)
                        });
                    break;
                case EndnoteReference { Id.Value: { } endnoteId }:
                    FlushText();
                    var endnoteKey = endnoteId.ToString();
                    if (!endnoteCitationNumbers.TryGetValue(endnoteKey, out var endnoteNumber))
                    {
                        endnoteNumber = ++endnotesInSection;
                        endnoteCitationNumbers[endnoteKey] = endnoteNumber;
                    }

                    result.Add(
                        new()
                        {
                            Text = NumberingParser.FormatNumber(endnoteNumber, endnoteNumberFormat),
                            Properties = GetProperties() with
                            {
                                VerticalAlignment = VerticalRunAlignment.Superscript
                            },
                            HyperlinkUrl = hyperlinkUrl,
                            EndnoteReferenceId = endnoteKey,
                            Source = SourceAt(position, atomic: true)
                        });
                    break;
                // A w:footnoteRef / w:endnoteRef inside a note's own body draws the note's number — the
                // citation ordinal in the document's format — in the run's formatting (Word's
                // FootnoteReference style makes it superscript; nothing is forced here, and a note
                // authored without the run shows no number, as document_capture/01's reference does).
                case FootnoteReferenceMark when noteReferenceText != null:
                case EndnoteReferenceMark when noteReferenceText != null:
                    FlushText();
                    result.Add(
                        new()
                        {
                            Text = noteReferenceText,
                            Properties = GetProperties(),
                            HyperlinkUrl = hyperlinkUrl
                        });
                    break;
                // A <w:br/> inside a run becomes a newline at its document position, so text on
                // either side of it (e.g. <w:r><w:br/><w:t>An</w:t></w:r>) survives in order.
                // Opt-in: callers that emit their own break runs (the SDT paths) leave this off.
                // Page/column breaks are structural and stay with the caller's paragraph splitting.
                case Break breakChild when emitLineBreaks &&
                                           breakChild.Type?.Value != BreakValues.Page &&
                                           breakChild.Type?.Value != BreakValues.Column:
                    FlushText();
                    result.Add(
                        new()
                        {
                            Text = "\n",
                            Properties = GetProperties(),
                            HyperlinkUrl = hyperlinkUrl,
                            Source = SourceAt(position)
                        });
                    break;
            }

            position += SourceRuns.Length(child);
        }

        FlushText();
        return result;
    }

    // w:char is a hex code (F0FE, or its low byte FE); w:font falls back to the run's own face. A code
    // that is not hex, or is no single UTF-16 character, names nothing a run can carry and is left out.
    static RunSymbol? ParseSymbol(SymbolChar symbolChar, string runFont)
    {
        if (!int.TryParse(symbolChar.Char?.Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var code) ||
            code is <= 0 or > 0xFFFF or >= 0xD800 and <= 0xDFFF)
        {
            return null;
        }

        var font = symbolChar.Font?.Value;
        if (string.IsNullOrWhiteSpace(font))
        {
            font = runFont;
        }

        return new(font, (char) code);
    }

    // props is any rPr-shaped element: a run's w:rPr (OoxmlRunProperties) or a paragraph
    // mark's w:pPr/w:rPr (ParagraphMarkRunProperties) — both carry the same children.
    RunProperties ParseRunProperties(OpenXmlElement? props, MainDocumentPart mainPart, string? paragraphStyleId = null)
    {
        // Start with defaults from paragraph style if available
        // If no explicit style, default to "Normal" which is the implicit default style in Word
        RunProperties? styleDefaults = null;
        if (styleRunProperties != null)
        {
            var styleId = paragraphStyleId ?? "Normal";
            styleRunProperties.TryGetValue(styleId, out styleDefaults);
        }

        // If no inline properties, return style defaults or empty properties.
        // When neither is present, anchor the font to the document default rather than the
        // RunProperties record's static default (which is Georgia, the cross-platform fallback).
        //
        // A run inside a styled table skips this shortcut even with no rPr of its own: the table style's
        // rung still has to be layered in below, and taking the style defaults verbatim here would drop it.
        if (props == null &&
            tableStyleRunDefaults == null)
        {
            if (styleDefaults != null)
            {
                // The style-chain sentinel for w:color w:val="auto" must not escape into the model,
                // and a colourless chain still resolves to the contextual automatic colour (white on
                // a dark w:background). Styles absorb defaultRunColorHex when they are built, so a
                // null here means the whole chain is colourless.
                var styleColor = styleDefaults.ColorHex == automaticColorSentinel
                    ? automaticRunColorHex
                    : styleDefaults.ColorHex ?? automaticRunColorHex;
                if (styleColor == styleDefaults.ColorHex)
                {
                    return styleDefaults;
                }

                return styleDefaults with
                {
                    ColorHex = styleColor
                };
            }

            return new()
            {
                FontFamily = effectiveDefaultFont,
                FontSizePoints = effectiveDefaultFontSizePoints,
                KerningMinFontSizePoints = effectiveDefaultKerningMinPoints,
                ColorHex = defaultRunColorHex ?? automaticRunColorHex
            };
        }

        // Start with style defaults or built-in defaults
        var fontFamily = styleDefaults?.FontFamily ?? effectiveDefaultFont;
        var fontSize = styleDefaults?.FontSizePoints ?? effectiveDefaultFontSizePoints;
        var bold = styleDefaults?.Bold ?? false;
        var italic = styleDefaults?.Italic ?? false;
        var underline = styleDefaults?.Underline ?? false;
        var underlineStyle = styleDefaults?.UnderlineStyle ?? UnderlineStyle.Single;
        var strikethrough = styleDefaults?.Strikethrough ?? false;
        var doubleStrikethrough = styleDefaults?.DoubleStrikethrough ?? false;
        var allCaps = styleDefaults?.AllCaps ?? false;
        var smallCaps = styleDefaults?.SmallCaps ?? false;
        var color = styleDefaults?.ColorHex ?? defaultRunColorHex ?? automaticRunColorHex;
        var kerningMinFontSize = styleDefaults?.KerningMinFontSizePoints ?? effectiveDefaultKerningMinPoints;

        // Inside a styled table the ladder has an extra rung: document defaults, then the table style
        // (whole-table rPr with the cell's conditional region over it), then the paragraph style, then
        // the direct formatting applied below (ECMA-376 §17.7.2). Because styleDefaults has already
        // folded the document defaults in, a value is only the table style's to take when the paragraph
        // chain did not declare it — which is what styleDeclaredRunProperties records.
        //
        // Toggles do not layer, they XOR across the two style rungs (§17.7.3) — Word-probed: a firstRow
        // region declaring w:b renders NOT bold under a paragraph style that also declares w:b, and bold
        // under one declaring w:b w:val="0". Direct formatting still overrides outright, further down.
        if (tableStyleRunDefaults is { } tableRun)
        {
            DeclaredRunProperties? paragraphDeclared = null;
            styleDeclaredRunProperties?.TryGetValue(paragraphStyleId ?? "Normal", out paragraphDeclared);

            fontFamily = paragraphDeclared?.FontFamily ?? tableRun.FontFamily ?? fontFamily;
            fontSize = paragraphDeclared?.FontSizePoints ?? tableRun.FontSizePoints ?? fontSize;
            color = paragraphDeclared?.ColorHex ?? tableRun.ColorHex ?? color;
            bold = DeclaredRunProperties.ToggleAcross(tableRun.Bold, paragraphDeclared?.Bold);
            italic = DeclaredRunProperties.ToggleAcross(tableRun.Italic, paragraphDeclared?.Italic);
            strikethrough = DeclaredRunProperties.ToggleAcross(tableRun.Strikethrough, paragraphDeclared?.Strikethrough);
            allCaps = DeclaredRunProperties.ToggleAcross(tableRun.AllCaps, paragraphDeclared?.AllCaps);
            smallCaps = DeclaredRunProperties.ToggleAcross(tableRun.SmallCaps, paragraphDeclared?.SmallCaps);
            underline = paragraphDeclared?.Underline ?? tableRun.Underline ?? underline;
            kerningMinFontSize = paragraphDeclared?.KerningMinFontSizePoints ?? tableRun.KerningMinFontSizePoints ?? kerningMinFontSize;
        }

        if (color == automaticColorSentinel)
        {
            color = automaticRunColorHex;
        }

        var backgroundColor = styleDefaults?.BackgroundColorHex;
        var verticalAlignment = styleDefaults?.VerticalAlignment ?? VerticalRunAlignment.Baseline;
        var characterSpacing = styleDefaults?.CharacterSpacingPoints ?? 0.0;
        var characterScale = styleDefaults?.CharacterScalePercent ?? 100;

        // Override with inline properties, read in one pass over the children; ??= keeps
        // GetFirstChild's first-in-document-order semantics on duplicate children.
        RunFonts? runFonts = null;
        FontSize? fontSizeElement = null;
        Bold? boldElement = null;
        Italic? italicElement = null;
        Underline? underlineElement = null;
        Strike? strikeElement = null;
        DoubleStrike? doubleStrikeElement = null;
        Caps? capsElement = null;
        SmallCaps? smallCapsElement = null;
        Spacing? spacingElement = null;
        CharacterScale? characterScaleElement = null;
        Kern? kernElement = null;
        DocumentFormat.OpenXml.Office2010.Word.Ligatures? ligaturesElement = null;
        RightToLeftText? rtlElement = null;
        DocumentFormat.OpenXml.Office2010.Word.TextOutlineEffect? outlineEffectElement = null;
        DocumentFormat.OpenXml.Office2010.Word.Shadow? shadowElement = null;
        DocumentFormat.OpenXml.Office2010.Word.Glow? glowElement = null;
        DocumentFormat.OpenXml.Office2010.Word.Reflection? reflectionElement = null;
        VerticalTextAlignment? vertAlignElement = null;
        Color? colorElement = null;
        Shading? shadingElement = null;
        Highlight? highlightElement = null;
        RunStyle? runStyleElement = null;
        Vanish? vanishElement = null;
        SpecVanish? specVanishElement = null;
        Position? positionElement = null;
        Border? bdrElement = null;
        Emboss? embossElement = null;
        Imprint? imprintElement = null;
        Outline? outlineElement = null;

        // props is null for a run inside a styled table that carries no rPr of its own — it still has to
        // reach the layering above, so the direct-formatting sweep simply finds nothing.
        foreach (var child in (IEnumerable<OpenXmlElement>?) props?.ChildElements ?? [])
        {
            switch (child)
            {
                case RunFonts element:
                    runFonts ??= element;
                    break;
                case FontSize element:
                    fontSizeElement ??= element;
                    break;
                case Bold element:
                    boldElement ??= element;
                    break;
                case Italic element:
                    italicElement ??= element;
                    break;
                case Underline element:
                    underlineElement ??= element;
                    break;
                case Strike element:
                    strikeElement ??= element;
                    break;
                case DoubleStrike element:
                    doubleStrikeElement ??= element;
                    break;
                case Caps element:
                    capsElement ??= element;
                    break;
                case SmallCaps element:
                    smallCapsElement ??= element;
                    break;
                case Spacing element:
                    spacingElement ??= element;
                    break;
                case CharacterScale element:
                    characterScaleElement ??= element;
                    break;
                case Kern element:
                    kernElement ??= element;
                    break;
                case DocumentFormat.OpenXml.Office2010.Word.Ligatures element:
                    ligaturesElement ??= element;
                    break;
                case RightToLeftText element:
                    rtlElement ??= element;
                    break;
                case DocumentFormat.OpenXml.Office2010.Word.TextOutlineEffect element:
                    outlineEffectElement ??= element;
                    break;
                case DocumentFormat.OpenXml.Office2010.Word.Shadow element:
                    shadowElement ??= element;
                    break;
                case DocumentFormat.OpenXml.Office2010.Word.Glow element:
                    glowElement ??= element;
                    break;
                case DocumentFormat.OpenXml.Office2010.Word.Reflection element:
                    reflectionElement ??= element;
                    break;
                case VerticalTextAlignment element:
                    vertAlignElement ??= element;
                    break;
                case Color element:
                    colorElement ??= element;
                    break;
                case Shading element:
                    shadingElement ??= element;
                    break;
                case Highlight element:
                    highlightElement ??= element;
                    break;
                case RunStyle element:
                    runStyleElement ??= element;
                    break;
                case Vanish element:
                    vanishElement ??= element;
                    break;
                case SpecVanish element:
                    specVanishElement ??= element;
                    break;
                case Position element:
                    positionElement ??= element;
                    break;
                case Border element:
                    bdrElement ??= element;
                    break;
                case Emboss element:
                    embossElement ??= element;
                    break;
                case Imprint element:
                    imprintElement ??= element;
                    break;
                case Outline element:
                    outlineElement ??= element;
                    break;
            }
        }

        if (runFonts != null)
        {
            if (runFonts.AsciiTheme?.HasValue == true &&
                currentThemeFonts != null)
            {
                var themeValue = ((IEnumValue) runFonts.AsciiTheme.Value).Value;
                var resolvedFont = currentThemeFonts.ResolveFont(themeValue);
                if (resolvedFont != null)
                {
                    fontFamily = resolvedFont;
                }
            }
            else if (runFonts.Ascii?.HasValue == true)
            {
                fontFamily = runFonts.Ascii.Value!;
            }
        }

        if (OoxmlUnits.FontSizeHalfPointsToPoints(fontSizeElement?.Val?.Value) is { } declaredSize)
        {
            fontSize = declaredSize;
        }

        if (boldElement != null)
        {
            bold = boldElement.IsOn();
        }

        if (italicElement != null)
        {
            italic = italicElement.IsOn();
        }

        string? underlineColorHex = null;
        if (underlineElement != null &&
            underlineElement.Val?.Value != UnderlineValues.None)
        {
            underline = true;
            underlineStyle = UnderlineStyleOf(underlineElement);

            // w:u/@w:color — the rule's own colour. "auto" (and absence) means the text colour,
            // which a null carries.
            var underlineColor = underlineElement.Color?.Value;
            if (!string.IsNullOrEmpty(underlineColor) &&
                !underlineColor.Equals("auto", StringComparison.OrdinalIgnoreCase))
            {
                underlineColorHex = underlineColor;
            }
        }

        if (strikeElement != null)
        {
            strikethrough = strikeElement.IsOn();
        }

        if (doubleStrikeElement != null)
        {
            doubleStrikethrough = doubleStrikeElement.IsOn();
        }

        if (capsElement != null)
        {
            allCaps = capsElement.IsOn();
        }

        if (smallCapsElement != null)
        {
            smallCaps = smallCapsElement.IsOn();
        }

        // Character spacing (w:spacing in rPr — extra space between characters, in twips)
        if (spacingElement?.Val?.HasValue == true)
        {
            characterSpacing = spacingElement.Val.Value / OoxmlUnits.TwipsPerPoint;
        }

        if (CharacterScaleOf(characterScaleElement) is { } directScale)
        {
            characterScale = directScale;
        }

        // Kerning threshold (w:kern in rPr — half-points; 0 disables). An absent inline kern leaves
        // what the style ladder resolved above: the paragraph style's chain, the table style, or
        // the document default (ResolveDocDefaultKerningMinPoints).
        if (kernElement?.Val?.HasValue == true)
        {
            kerningMinFontSize = kernElement.Val.Value.HalfPointsToPoints();
        }

        // Ligature mode (w14:ligatures in rPr — Word 2010+ extension). Word sets standard ligatures
        // only in compatibility mode 15, where a run that says nothing has them, and none at all in
        // modes 14 and older whatever the run asks: _probe_liga (twenty 24pt Calibri f's and " xx",
        // the measure stepped 1pt at a time) wraps " xx" below 165pt with standard or no
        // w14:ligatures in mode 15, below 169pt with none or contextual-only, and below 169pt for
        // every value in modes 12 and 14 (2026-10-30).
        var ligatures = compatibilityMode >= 15 ? ParseLigatureMode(ligaturesElement) : LigatureMode.None;

        var runRtl = false;
        if (rtlElement != null)
        {
            runRtl = rtlElement.IsOn();
        }

        // w14 text effects (parameters captured for outline/shadow/glow; reflection is presence-only)
        // A run's own effect replaces its style's, an inert one included; one it does not declare
        // is its style's.
        var outline = outlineEffectElement != null ? ParseTextOutline(outlineEffectElement, color) : styleDefaults?.Outline;
        var shadow = shadowElement != null ? ParseTextShadow(shadowElement) : styleDefaults?.Shadow;
        var glow = glowElement != null ? ParseTextGlow(glowElement) : styleDefaults?.Glow;
        var hasReflection = !IsInertEffect(reflectionElement);

        // Vertical alignment (subscript/superscript)
        if (vertAlignElement?.Val?.HasValue == true)
        {
            var vertAlignVal = vertAlignElement.Val.Value;
            if (vertAlignVal == VerticalPositionValues.Superscript)
            {
                verticalAlignment = VerticalRunAlignment.Superscript;
            }
            else if (vertAlignVal == VerticalPositionValues.Subscript)
            {
                verticalAlignment = VerticalRunAlignment.Subscript;
            }
            else
            {
                verticalAlignment = VerticalRunAlignment.Baseline;
            }
        }

        // An explicit w:color on the run overrides any inherited (style / docDefaults) colour,
        // w:val="auto" included: "automatic" opts back out to Word's contrast colour (black on a
        // light page, white on a dark w:background), so it RESETS the inherited colour — a card
        // template's white docDefaults must not leak onto it. ResolveRunColor returns null for
        // auto / unresolved.
        if (colorElement != null)
        {
            color = ResolveRunColor(colorElement) ?? automaticRunColorHex;
        }

        if (shadingElement != null)
        {
            string? inlineBgColor;
            inlineBgColor = ResolveThemeFill(shadingElement);

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

        // w:highlight — Word's highlighter pen, distinct from w:shd. Values are a fixed
        // palette of named colors (yellow, green, cyan, ...). Mapped to BackgroundColorHex
        // so the same renderer path that handles shading paints the highlight.
        if (highlightElement?.Val?.HasValue == true)
        {
            var highlightHex = HighlightToHex(highlightElement.Val.Value);
            if (highlightHex != null)
            {
                backgroundColor = highlightHex;
            }
        }

        // A character style (w:rStyle) applies only the properties it EXPLICITLY defines, not
        // inherited defaults: "Shade" (background colour only) must not override Heading1's size.
        var runStyleId = runStyleElement?.Val?.Value;

        // Word's print layout suppresses the Hyperlink character style inside TOC entries: a TOC
        // built with \h stamps rStyle=Hyperlink on every entry run, but Word renders them in the
        // TOC N paragraph style's formatting — no underline, no hyperlink colour
        // (business-plans/12/13/15, verified against Word's own renders). The links stay
        // functional; only the character style's visuals are dropped.
        if (runStyleId == "Hyperlink" &&
            paragraphStyleId != null &&
            paragraphStyleId.StartsWith("TOC", StringComparison.OrdinalIgnoreCase))
        {
            runStyleId = null;
        }

        if (runStyleId != null &&
            styleRunProperties != null && styleRunProperties.TryGetValue(runStyleId, out var runStyleProps))
        {
            var originalStyle = LookupStyle(mainPart, runStyleId);
            var originalRPr = originalStyle?.StyleRunProperties;

            if (runFonts == null &&
                originalRPr?.GetFirstChild<RunFonts>() != null)
            {
                fontFamily = runStyleProps.FontFamily;
            }

            if (fontSizeElement == null &&
                originalRPr?.GetFirstChild<FontSize>() != null)
            {
                fontSize = runStyleProps.FontSizePoints;
            }

            // w:b, w:i, w:strike, w:caps and w:smallCaps are TOGGLE properties (ECMA-376
            // §17.7.3): declared ON in a character style they FLIP the paragraph-chain state
            // rather than asserting it — resumes/18's "NotBold" style is a bare <w:b/> applied
            // inside bold Heading2 rows, and Word renders those runs regular. An explicit
            // w:val="0" is not a toggle; it forces the property off.
            if (boldElement == null &&
                originalRPr?.GetFirstChild<Bold>() is { } styleBold)
            {
                bold = styleBold.IsOn() ? !bold : false;
            }

            if (italicElement == null &&
                originalRPr?.GetFirstChild<Italic>() is { } styleItalic)
            {
                italic = styleItalic.IsOn() ? !italic : false;
            }

            if (underlineElement == null &&
                originalRPr?.GetFirstChild<Underline>() != null)
            {
                underline = runStyleProps.Underline;
                underlineStyle = runStyleProps.UnderlineStyle;
            }

            if (strikeElement == null &&
                originalRPr?.GetFirstChild<Strike>() is { } styleStrike)
            {
                strikethrough = styleStrike.IsOn() ? !strikethrough : false;
            }

            if (doubleStrikeElement == null &&
                originalRPr?.GetFirstChild<DoubleStrike>() is { } styleDoubleStrike)
            {
                doubleStrikethrough = styleDoubleStrike.IsOn() ? !doubleStrikethrough : false;
            }

            if (capsElement == null &&
                originalRPr?.GetFirstChild<Caps>() is { } styleCaps)
            {
                allCaps = styleCaps.IsOn() ? !allCaps : false;
            }

            if (smallCapsElement == null &&
                originalRPr?.GetFirstChild<SmallCaps>() is { } styleSmallCaps)
            {
                smallCaps = styleSmallCaps.IsOn() ? !smallCaps : false;
            }

            if (spacingElement == null &&
                originalRPr?.GetFirstChild<Spacing>() != null)
            {
                characterSpacing = runStyleProps.CharacterSpacingPoints;
            }

            if (characterScaleElement == null &&
                originalRPr?.GetFirstChild<CharacterScale>() != null)
            {
                characterScale = runStyleProps.CharacterScalePercent;
            }

            // A character style's w:kern outranks the paragraph style's and yields to the run's own.
            if (kernElement == null &&
                DeclaredRunProperties.KerningMinPoints(originalRPr) != null)
            {
                kerningMinFontSize = runStyleProps.KerningMinFontSizePoints;
            }

            if (colorElement == null &&
                originalRPr?.GetFirstChild<Color>() != null)
            {
                color = runStyleProps.ColorHex == automaticColorSentinel
                    ? automaticRunColorHex
                    : runStyleProps.ColorHex;
            }

            if (shadingElement == null &&
                originalRPr?.GetFirstChild<Shading>() != null)
            {
                backgroundColor = runStyleProps.BackgroundColorHex;
            }

            // A raise the character style takes from the style it is based on is its own as much as one
            // it declares (a style based on Footnote Reference is superscript too).
            if (vertAlignElement == null &&
                DeclaresUpChain(mainPart, originalStyle, _ => _.GetFirstChild<VerticalTextAlignment>() != null))
            {
                verticalAlignment = runStyleProps.VerticalAlignment;
            }
        }

        // w:vanish / w:specVanish — hidden text. Either form skips the run during layout.
        // w:webHidden is intentionally not consumed: it hides only in web view, and Morph
        // renders for print/image so the runs stay visible (parsed-and-discarded).
        var hidden = styleDefaults?.Hidden ?? false;
        if (vanishElement != null)
        {
            hidden = vanishElement.IsOn();
        }

        if (specVanishElement != null)
        {
            hidden = true;
        }

        // w:position — baseline shift in half-points (positive = up, negative = down).
        // Distinct from w:vertAlign which also resizes the glyph.
        var baselineShift = styleDefaults?.BaselineShiftPoints ?? 0.0;
        if (positionElement?.Val?.HasValue == true &&
            double.TryParse(positionElement.Val.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var posHalfPts))
        {
            baselineShift = posHalfPts.HalfPointsToPoints();
        }

        // w:bdr — per-run border, modelled as a single BorderEdge applied around the run's
        // measured box. Reuses ParseBorderEdge so dotted/dashed/single all flow through.
        var runBorder = styleDefaults?.Border;
        if (bdrElement != null)
        {
            var parsed = ParseBorderEdge(bdrElement);
            runBorder = parsed.IsVisible ? parsed : null;
        }

        // w:emboss / w:imprint / w:outline — three glyph-fill modifiers. Each is a presence
        // toggle (val="0" / val="false" turns it back off).
        var emboss = styleDefaults?.Emboss ?? false;
        if (embossElement != null)
        {
            emboss = embossElement.IsOn();
        }

        var imprint = styleDefaults?.Imprint ?? false;
        if (imprintElement != null)
        {
            imprint = imprintElement.IsOn();
        }

        var outlineOnly = styleDefaults?.OutlineOnly ?? false;
        if (outlineElement != null)
        {
            outlineOnly = outlineElement.IsOn();
        }

        // w:effect — animated text (blinkBackground, sparkle, etc.). Pure animation, so we
        // render the underlying text plain (parsed-and-discarded).

        // Automatic text turns white over a dark RUN shading (w:rPr/w:shd or a w:highlight), the same
        // contrast rule cell and paragraph shading get. Resolved here rather than in a post-pass because
        // the run's own fill is only in hand at this point; a null colour is what "automatic" leaves
        // behind, so an explicit w:color is never flipped.
        if (color == null &&
            backgroundColor != null &&
            IsDarkForAutomaticText(backgroundColor))
        {
            color = "FFFFFF";
        }

        return new()
        {
            FontFamily = fontFamily,
            FontSizePoints = fontSize,
            Bold = bold,
            Italic = italic,
            Underline = underline,
            UnderlineColorHex = underlineColorHex,
            UnderlineStyle = underlineStyle,
            DoubleStrikethrough = doubleStrikethrough,
            DirectUnderline = underlineElement?.Val?.InnerText,
            DirectDoubleStrikethrough = doubleStrikeElement?.IsOn() == true ? true : null,
            Strikethrough = strikethrough,
            AllCaps = allCaps,
            SmallCaps = smallCaps,
            ColorHex = color,
            BackgroundColorHex = backgroundColor,
            CharacterSpacingPoints = characterSpacing,
            CharacterScalePercent = characterScale,
            VerticalAlignment = verticalAlignment,
            KerningMinFontSizePoints = kerningMinFontSize,
            Ligatures = ligatures,
            IsRightToLeft = runRtl,
            Outline = outline,
            Shadow = shadow,
            Glow = glow,
            HasReflection = hasReflection,
            Hidden = hidden,
            BaselineShiftPoints = baselineShift,
            Border = runBorder,
            Emboss = emboss,
            Imprint = imprint,
            OutlineOnly = outlineOnly
        };
    }

    // A w14 text effect declared with neither attributes nor children carries no effect, and Word
    // renders the run plain. feature_capture/01 is the case: its heading declares
    // <w14:shadow/> <w14:textOutline/> <w14:glow/> <w14:reflection/> all empty, and Word draws
    // "All features" as plain small caps.
    static bool IsInertEffect([NotNullWhen(false)] OpenXmlElement? element) =>
        element is null or
        {
            HasAttributes: false,
            HasChildren: false
        };

    TextOutline? ParseTextOutline(
        DocumentFormat.OpenXml.Office2010.Word.TextOutlineEffect? element,
        string? fillColor)
    {
        // An outline strokes only with a fill: w14:noFill, or no fill at all, draws nothing
        // (agendas-minutes/08's Title declares a noFill outline).
        if (IsInertEffect(element) ||
            !element.ChildElements.Any(_ => _.LocalName is "solidFill" or "gradFill"))
        {
            return null;
        }

        // LineWidth is in EMU; default to a thin stroke when absent.
        var widthEmu = element.LineWidth?.Value ?? (long) (0.75 * OoxmlUnits.EmusPerPoint);
        var widthPoints = widthEmu / OoxmlUnits.EmusPerPoint;

        var color = ReadW14SolidFillColor(element) ?? fillColor ?? "000000";
        return new()
        {
            ColorHex = color,
            WidthPoints = widthPoints
        };
    }

    TextShadow? ParseTextShadow(DocumentFormat.OpenXml.Office2010.Word.Shadow? element)
    {
        if (IsInertEffect(element))
        {
            return null;
        }

        // Word's defaults when attributes absent: 4pt distance, 45deg, 4pt blur, and with no colour a
        // half-transparent black. A colour declared without w14:alpha is opaque, as any OOXML colour is:
        // menus/07's Title shadow draws solid mint in Word.
        var blurPoints = (element.BlurRadius?.Value ?? (long) (4 * OoxmlUnits.EmusPerPoint)) / OoxmlUnits.EmusPerPoint;
        var distancePoints = (element.DistanceFromText?.Value ?? (long) (4 * OoxmlUnits.EmusPerPoint)) / OoxmlUnits.EmusPerPoint;
        // DirectionAngle is in 60000ths of a degree; OOXML 0deg = right, 90deg = down.
        var directionDegrees = (element.DirectionAngle?.Value ?? 2700000) / 60000.0;

        var (color, alpha) = ReadW14ColorWithAlpha(element.RgbColorModelHex, element.SchemeColor);
        return new()
        {
            ColorHex = color ?? "000000",
            BlurPoints = blurPoints,
            DistancePoints = distancePoints,
            DirectionDegrees = directionDegrees,
            AlphaPercent = alpha ?? (color == null ? 50 : 100)
        };
    }

    TextGlow? ParseTextGlow(DocumentFormat.OpenXml.Office2010.Word.Glow? element)
    {
        if (IsInertEffect(element))
        {
            return null;
        }

        var radiusPoints = (element.GlowRadius?.Value ?? (long) (4 * OoxmlUnits.EmusPerPoint)) / OoxmlUnits.EmusPerPoint;
        var (color, alpha) = ReadW14ColorWithAlpha(element.RgbColorModelHex, element.SchemeColor);
        return new()
        {
            ColorHex = color ?? "FFFF00",
            RadiusPoints = radiusPoints,
            AlphaPercent = alpha ?? 60
        };
    }

    // A w14 effect a style's rPr declares. The SDK types them in a run's rPr but leaves them unknown
    // elements under a style's, so the typed element is built again from the markup.
    static T? W14Effect<T>(OpenXmlElement runProperties, string localName, Func<string, T> fromXml)
        where T : OpenXmlElement
    {
        foreach (var child in runProperties.ChildElements)
        {
            if (child is T typed)
            {
                return typed;
            }

            if (child.LocalName == localName &&
                child.NamespaceUri == "http://schemas.microsoft.com/office/word/2010/wordml")
            {
                return fromXml(child.OuterXml);
            }
        }

        return null;
    }

    // The colour of a w14 effect's solidFill: its srgbClr, or its schemeClr resolved against the theme.
    // Null when it has neither.
    string? ReadW14SolidFillColor(OpenXmlElement element)
    {
        foreach (var child in element.Descendants())
        {
            if (child is DocumentFormat.OpenXml.Office2010.Word.RgbColorModelHex {Val.HasValue: true} rgb)
            {
                return rgb.Val.Value;
            }

            if (child is DocumentFormat.OpenXml.Office2010.Word.SchemeColor scheme &&
                ResolveW14Scheme(scheme) is { } resolved)
            {
                return resolved;
            }
        }

        return null;
    }

    // A w14:schemeClr against the document theme, with its luminance and saturation transforms, which
    // are DrawingML's under the w14 namespace. menus/07's Title outline is accent4 at lumMod 25% and its
    // shadow accent4 at lumMod 90%.
    string? ResolveW14Scheme(DocumentFormat.OpenXml.Office2010.Word.SchemeColor scheme)
    {
        if (currentThemeColors == null ||
            scheme.GetAttributes().AttributeValue("val") is not { } name)
        {
            return null;
        }

        List<ColorTransform>? operations = null;
        foreach (var child in scheme.ChildElements)
        {
            ColorTransformKind? kind = child.LocalName switch
            {
                "shade" => ColorTransformKind.Shade,
                "tint" => ColorTransformKind.Tint,
                "lumMod" => ColorTransformKind.LumMod,
                "lumOff" => ColorTransformKind.LumOff,
                "satMod" => ColorTransformKind.SatMod,
                "satOff" => ColorTransformKind.SatOff,
                _ => null
            };
            if (kind is not { } known ||
                child.GetAttributes().AttributeValue("val") is not { } raw ||
                !int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            {
                continue;
            }

            operations ??= [];
            operations.Add(ColorTransform.FromOoxml(known, value));
        }

        return currentThemeColors.ResolveColor(
            name,
            new()
            {
                Operations = operations ?? []
            });
    }

    (string? Color, int? AlphaPercent) ReadW14ColorWithAlpha(
        DocumentFormat.OpenXml.Office2010.Word.RgbColorModelHex? rgb,
        DocumentFormat.OpenXml.Office2010.Word.SchemeColor? scheme)
    {
        if (rgb?.Val?.HasValue == true)
        {
            return (rgb.Val.Value, ReadW14Alpha(rgb));
        }

        if (scheme != null)
        {
            return (ResolveW14Scheme(scheme), ReadW14Alpha(scheme));
        }

        return (null, null);
    }

    static int? ReadW14Alpha(OpenXmlElement parent)
    {
        var alpha = parent.GetFirstChild<DocumentFormat.OpenXml.Office2010.Word.Alpha>();
        if (alpha?.Val?.HasValue == true)
        {
            // w14:alpha is in 1000ths of a percent (100000 = 100%).
            return alpha.Val.Value / 1000;
        }

        return null;
    }
}
