using A = DocumentFormat.OpenXml.Drawing;

/// <summary>
/// The styles part: what each style declares up its basedOn chain, the document defaults, and the style table itself.
/// </summary>
sealed partial class DocumentParser
{
    /// <summary>
    /// The ascii face a <c>w:rFonts</c> names: a theme reference (<c>w:asciiTheme</c>) resolved against
    /// the document's font scheme, else the literal <c>w:ascii</c>. Null when it names neither — which
    /// includes the common shape of an rFonts carrying only <c>w:eastAsiaTheme</c>/<c>w:cstheme</c>, and
    /// means "inherit", not "use the default face".
    /// </summary>
    string? ResolveRunFontFamily(RunFonts? fonts)
    {
        if (fonts == null)
        {
            return null;
        }

        if (fonts.AsciiTheme?.HasValue == true &&
            currentThemeFonts != null)
        {
            var themeValue = ((IEnumValue) fonts.AsciiTheme.Value).Value;
            var resolved = currentThemeFonts.ResolveFont(themeValue);
            if (resolved != null)
            {
                return resolved;
            }
        }

        if (fonts.Ascii?.HasValue == true)
        {
            return fonts.Ascii.Value;
        }

        return null;
    }

    string? ResolveDocDefaultFont(MainDocumentPart mainPart)
    {
        var rPrDefault = mainPart.StyleDefinitionsPart?.Styles?.DocDefaults?.RunPropertiesDefault?.RunPropertiesBaseStyle;
        return ResolveRunFontFamily(rPrDefault?.GetFirstChild<RunFonts>());
    }

    static double? ResolveDocDefaultFontSizePoints(MainDocumentPart mainPart)
    {
        // No styles part, or styles with no docDefaults at all: the document declares no
        // document-wide defaults, so Word supplies normal.dotm's built-in size (null -> 12pt).
        var docDefaults = mainPart.StyleDefinitionsPart?.Styles?.DocDefaults;
        if (docDefaults == null)
        {
            return null;
        }

        // docDefaults IS present: an absent w:sz is the spec's 10pt, not the built-in.
        var size = docDefaults.RunPropertiesDefault?.RunPropertiesBaseStyle?.GetFirstChild<FontSize>();
        if (size?.Val?.HasValue != true)
        {
            return specDefaultFontSizePoints;
        }

        return OoxmlUnits.FontSizeHalfPointsToPoints(size.Val.Value) ?? specDefaultFontSizePoints;
    }

    // The document-wide kerning threshold, Word-probed with the _probe_kern_* fixtures (todo #43):
    // a document with NO docDefaults kerns by default (Word's built-in Normal - measured kerned at
    // 10/12/24pt in a bare package), docDefaults WITHOUT w:kern disable it (the spec default 0),
    // and a docDefaults w:kern enables it at its half-point threshold.
    //
    // Above that, w:kern cascades like any run property (_probe_kerncascade, 2026-10-02: 52 cases,
    // compatibility modes 12 and 15, read off the XPS): table style, paragraph style chain,
    // character style, the run's own w:rPr. A rung that declares it wins outright, w:val="0"
    // included, and the size test is the resolved threshold's (a style declaring w:val="80" kerns
    // its 48pt text and not its 24pt text).
    static double ResolveDocDefaultKerningMinPoints(MainDocumentPart mainPart)
    {
        var docDefaults = mainPart.StyleDefinitionsPart?.Styles?.DocDefaults;
        if (docDefaults == null)
        {
            return builtInKerningMinPoints;
        }

        return DeclaredRunProperties.KerningMinPoints(docDefaults.RunPropertiesDefault?.RunPropertiesBaseStyle) ?? 0;
    }

    Dictionary<string, RunProperties> ExtractStyleRunProperties(MainDocumentPart mainPart)
    {
        var styleProps = new Dictionary<string, RunProperties>(StringComparer.OrdinalIgnoreCase);
        var declaredProps = new Dictionary<string, DeclaredRunProperties>(StringComparer.OrdinalIgnoreCase);
        styleDeclaredRunProperties = declaredProps;

        // What this style's chain declares in its own right, for the table-style cascade. Kept beside the
        // resolved values rather than derived from them, since those have docDefaults folded in.
        void RecordDeclared(string styleId, string? basedOnId, OpenXmlElement? ownRunProps)
        {
            DeclaredRunProperties? inherited = null;
            if (basedOnId != null)
            {
                declaredProps.TryGetValue(basedOnId, out inherited);
            }

            var own = DeclaredRunProperties.Read(ownRunProps);
            var declared = inherited == null ? own : inherited.Layer(own);
            if (declared != null)
            {
                declaredProps[styleId] = declared;
            }
        }

        var stylesPart = mainPart.StyleDefinitionsPart;
        if (stylesPart?.Styles == null)
        {
            return styleProps;
        }

        // Extract docDefaults run properties as the base defaults. The size is already resolved
        // from docDefaults (see effectiveDefaultFontSizePoints), so it needs no re-parse below.
        var defaultFontFamily = defaultFont ?? builtInDefaultFontFamily;
        var defaultFontSize = effectiveDefaultFontSizePoints;

        var docDefaults = stylesPart.Styles.DocDefaults;
        if (docDefaults?.RunPropertiesDefault?.RunPropertiesBaseStyle != null)
        {
            var rPrDefault = docDefaults.RunPropertiesDefault.RunPropertiesBaseStyle;

            var defaultFonts = rPrDefault.GetFirstChild<RunFonts>();
            if (defaultFonts != null)
            {
                defaultEastAsiaFontFamily = EastAsiaFontFamily(defaultFonts);

                if (defaultFonts.AsciiTheme?.HasValue == true &&
                    currentThemeFonts != null)
                {
                    var themeValue = ((IEnumValue) defaultFonts.AsciiTheme.Value).Value;
                    var resolvedFont = currentThemeFonts.ResolveFont(themeValue);
                    if (resolvedFont != null)
                    {
                        defaultFontFamily = resolvedFont;
                    }
                }
                else if (defaultFonts.Ascii?.HasValue == true)
                {
                    defaultFontFamily = defaultFonts.Ascii.Value!;
                }
            }

            // Text colour from docDefaults — the base of the run-colour cascade. Many templates set
            // their body colour here as a theme colour (e.g. text2 #44546a) rather than on every run.
            // A WHITE default applies too: dark-board templates (menus/07/08, inline_group_crop)
            // declare FFFFFF background1 and Word paints fall-through runs white. What keeps the
            // matching card/invitation templates readable is not a white filter here but the style
            // chain: their Normal declares w:color w:val="auto", which RESETS the cascade to
            // automatic (see automaticColorSentinel) — cards/01/05/15 and wedding/03 pair the same
            // white default with an auto Normal, and their body text stays black.
            var defaultColorElement = rPrDefault.GetFirstChild<Color>();
            if (defaultColorElement != null)
            {
                defaultRunColorHex = ResolveRunColor(defaultColorElement);
            }
        }

        var styles = stylesPart.Styles.Elements<Style>().ToList();
        var processed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var existingStyleIds = new HashSet<string>(
            styles
                .Select(_ => _.StyleId?.Value)
                .Where(_ => _ != null)!,
            StringComparer.OrdinalIgnoreCase);

        // Process styles with proper inheritance - may need multiple passes
        // to handle chains like: Title -> Normal -> (base)
        int lastCount;
        do
        {
            lastCount = processed.Count;
            foreach (var style in styles)
            {
                var styleId = style.StyleId?.Value;
                if (styleId == null ||
                    processed.Contains(styleId))
                {
                    continue;
                }

                var basedOnId = style.BasedOn?.Val?.Value;
                // Wait for the base style only if it exists in this document; a basedOn naming a
                // missing style is processed without waiting.
                if (basedOnId != null &&
                    existingStyleIds.Contains(basedOnId) &&
                    !processed.Contains(basedOnId))
                {
                    continue;
                }

                RunProperties? baseProps = null;
                if (basedOnId != null)
                {
                    styleProps.TryGetValue(basedOnId, out baseProps);
                }

                var runProps = style.StyleRunProperties;

                var fontFamily = baseProps?.FontFamily ?? defaultFontFamily;
                var fontSize = baseProps?.FontSizePoints ?? defaultFontSize;
                var bold = baseProps?.Bold ?? false;
                var italic = baseProps?.Italic ?? false;
                var underline = baseProps?.Underline ?? false;
                var underlineStyle = baseProps?.UnderlineStyle ?? UnderlineStyle.Single;
                var strikethrough = baseProps?.Strikethrough ?? false;
                var doubleStrikethrough = baseProps?.DoubleStrikethrough ?? false;
                var allCaps = baseProps?.AllCaps ?? false;
                var smallCaps = baseProps?.SmallCaps ?? false;
                var color = baseProps?.ColorHex ?? defaultRunColorHex;
                var backgroundColor = baseProps?.BackgroundColorHex;
                var characterSpacing = baseProps?.CharacterSpacingPoints ?? 0.0;
                var characterScale = baseProps?.CharacterScalePercent ?? 100;

                // Superscript and subscript come down the chain like the rest: a note's own number
                // in Footnote Reference takes its raise from the style.
                var verticalAlignment = baseProps?.VerticalAlignment ?? VerticalRunAlignment.Baseline;

                // So does the kerning threshold: most body text carries no w:rPr and takes it from a style.
                var kerningMinFontSize = baseProps?.KerningMinFontSizePoints ?? effectiveDefaultKerningMinPoints;

                // The w14 text effects come down the chain too: menus/07's Title declares its outline
                // and shadow on the style, and its title run carries no rPr of its own.
                var outline = baseProps?.Outline;
                var shadow = baseProps?.Shadow;
                var glow = baseProps?.Glow;

                RecordDeclared(styleId, basedOnId, runProps);

                // If no run properties, still save inherited properties
                if (runProps == null)
                {
                    styleProps[styleId] = new()
                    {
                        FontFamily = fontFamily,
                        FontSizePoints = fontSize,
                        Bold = bold,
                        Italic = italic,
                        Underline = underline,
                        UnderlineStyle = underlineStyle,
                        Strikethrough = strikethrough,
                        DoubleStrikethrough = doubleStrikethrough,
                        AllCaps = allCaps,
                        SmallCaps = smallCaps,
                        ColorHex = color,
                        BackgroundColorHex = backgroundColor,
                        CharacterSpacingPoints = characterSpacing,
                        CharacterScalePercent = characterScale,
                        VerticalAlignment = verticalAlignment,
                        KerningMinFontSizePoints = kerningMinFontSize,
                        Outline = outline,
                        Shadow = shadow,
                        Glow = glow
                    };
                    processed.Add(styleId);
                    continue;
                }

                var runFonts = runProps.GetFirstChild<RunFonts>();
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

                var fontSizeElement = runProps.GetFirstChild<FontSize>();
                if (OoxmlUnits.FontSizeHalfPointsToPoints(fontSizeElement?.Val?.Value) is { } declaredSize)
                {
                    fontSize = declaredSize;
                }

                var boldElement = runProps.GetFirstChild<Bold>();
                if (boldElement != null)
                {
                    bold = boldElement.IsOn();
                }

                var italicElement = runProps.GetFirstChild<Italic>();
                if (italicElement != null)
                {
                    italic = italicElement.IsOn();
                }

                var underlineElement = runProps.GetFirstChild<Underline>();
                if (underlineElement != null &&
                    underlineElement.Val?.Value != UnderlineValues.None)
                {
                    underline = true;
                    underlineStyle = UnderlineStyleOf(underlineElement);
                }

                var strikeElement = runProps.GetFirstChild<Strike>();
                if (strikeElement != null)
                {
                    strikethrough = strikeElement.IsOn();
                }

                var doubleStrikeElement = runProps.GetFirstChild<DoubleStrike>();
                if (doubleStrikeElement != null)
                {
                    doubleStrikethrough = doubleStrikeElement.IsOn();
                }

                var capsElement = runProps.GetFirstChild<Caps>();
                if (capsElement != null)
                {
                    allCaps = capsElement.IsOn();
                }

                var smallCapsElement = runProps.GetFirstChild<SmallCaps>();
                if (smallCapsElement != null)
                {
                    smallCaps = smallCapsElement.IsOn();
                }

                var spacingElement = runProps.GetFirstChild<Spacing>();
                if (spacingElement?.Val?.HasValue == true)
                {
                    characterSpacing = spacingElement.Val.Value / OoxmlUnits.TwipsPerPoint;
                }

                // Character scale (w:w in rPr), a percent of the glyphs' width.
                if (CharacterScaleOf(runProps.GetFirstChild<CharacterScale>()) is { } styleScale)
                {
                    characterScale = styleScale;
                }

                if (runProps.GetFirstChild<VerticalTextAlignment>()?.Val?.Value is { } styleVertical)
                {
                    verticalAlignment = styleVertical == VerticalPositionValues.Superscript
                        ? VerticalRunAlignment.Superscript
                        : styleVertical == VerticalPositionValues.Subscript
                            ? VerticalRunAlignment.Subscript
                            : VerticalRunAlignment.Baseline;
                }

                // Kerning threshold (w:kern in rPr); w:val="0" switches it off for this style.
                if (DeclaredRunProperties.KerningMinPoints(runProps) is { } declaredKerning)
                {
                    kerningMinFontSize = declaredKerning;
                }

                // Colour — mirror the run-level rule: an explicit w:color on the style overrides
                // whatever was inherited (basedOn chain / docDefaults), and w:val="auto" RESETS the
                // inherited colour to automatic rather than falling through to it (a card template's
                // white docDefaults must not leak through Normal's explicit auto). The sentinel
                // survives basedOn inheritance and is converted to automaticRunColorHex at the run
                // resolution sites.
                var colorElement = runProps.GetFirstChild<Color>();
                if (colorElement != null)
                {
                    color = ResolveRunColor(colorElement) ?? automaticColorSentinel;
                }

                var shadingElement = runProps.GetFirstChild<Shading>();
                if (shadingElement != null)
                {
                    backgroundColor = ResolveThemeFill(shadingElement);

                    if (backgroundColor == null &&
                        shadingElement.Fill?.HasValue == true &&
                        shadingElement.Fill.Value != "auto" &&
                        shadingElement.Fill.Value != "none")
                    {
                        backgroundColor = shadingElement.Fill.Value;
                    }
                }

                // A style's own effect replaces the inherited one, an inert one (w14:noFill) included.
                if (W14Effect(runProps, "textOutline", _ => new DocumentFormat.OpenXml.Office2010.Word.TextOutlineEffect(_)) is { } outlineElement)
                {
                    outline = ParseTextOutline(outlineElement, color);
                }

                if (W14Effect(runProps, "shadow", _ => new DocumentFormat.OpenXml.Office2010.Word.Shadow(_)) is { } shadowElement)
                {
                    shadow = ParseTextShadow(shadowElement);
                }

                if (W14Effect(runProps, "glow", _ => new DocumentFormat.OpenXml.Office2010.Word.Glow(_)) is { } glowElement)
                {
                    glow = ParseTextGlow(glowElement);
                }

                styleProps[styleId] = new()
                {
                    FontFamily = fontFamily,
                    FontSizePoints = fontSize,
                    Bold = bold,
                    Italic = italic,
                    Underline = underline,
                    UnderlineStyle = underlineStyle,
                    Strikethrough = strikethrough,
                    DoubleStrikethrough = doubleStrikethrough,
                    AllCaps = allCaps,
                    SmallCaps = smallCaps,
                    ColorHex = color,
                    BackgroundColorHex = backgroundColor,
                    CharacterSpacingPoints = characterSpacing,
                    CharacterScalePercent = characterScale,
                    VerticalAlignment = verticalAlignment,
                    KerningMinFontSizePoints = kerningMinFontSize,
                    Outline = outline,
                    Shadow = shadow,
                    Glow = glow
                };
                processed.Add(styleId);
            }
        } while (processed.Count > lastCount);

        return styleProps;
    }

    /// <summary>
    /// Reads what a single style's own <c>w:pPr</c> declares, leaving anything absent null.
    /// </summary>
    StyleParagraphSpacing ReadDeclaredSpacing(StyleParagraphProperties? pPr)
    {
        if (pPr == null)
        {
            return new();
        }

        double? before = null, after = null, multiplier = null, points = null;
        LineSpacingRule? rule = null;

        var spacing = pPr.GetFirstChild<SpacingBetweenLines>();
        if (spacing != null)
        {
            before = spacing.Before.TwipsToPoints() ?? before;
            before = LineUnitsToPoints(spacing.BeforeLines) ?? before;

            after = spacing.After.TwipsToPoints() ?? after;
            after = LineUnitsToPoints(spacing.AfterLines) ?? after;

            if (spacing.Line?.HasValue == true)
            {
                var ruleValue = spacing.LineRule?.Value ?? LineSpacingRuleValues.Auto;
                if (ruleValue == LineSpacingRuleValues.Auto)
                {
                    multiplier = double.Parse(spacing.Line.Value!) / 240.0;
                    rule = LineSpacingRule.Auto;
                }
                else if (ruleValue == LineSpacingRuleValues.Exact)
                {
                    points = double.Parse(spacing.Line.Value!) / OoxmlUnits.TwipsPerPoint;
                    rule = LineSpacingRule.Exactly;
                }
                else if (ruleValue == LineSpacingRuleValues.AtLeast)
                {
                    points = double.Parse(spacing.Line.Value!) / OoxmlUnits.TwipsPerPoint;
                    rule = LineSpacingRule.AtLeast;
                }
            }
        }

        double? left = null, right = null;
        var indentation = pPr.GetFirstChild<Indentation>();
        if (indentation != null)
        {
            left = indentation.Left.TwipsToPoints() ?? left;

            right = indentation.Right.TwipsToPoints() ?? right;
        }

        return new()
        {
            SpacingBeforePoints = before,
            SpacingAfterPoints = after,
            LineSpacingMultiplier = multiplier,
            LineSpacingPoints = points,
            LineSpacingRule = rule,
            LeftIndentPoints = left,
            RightIndentPoints = right
        };
    }

    /// <summary>
    /// Declared spacing per style id, resolved through <c>w:basedOn</c>, for the requested style
    /// type. Table styles feed the table-style cascade step; paragraph styles say where that step
    /// is allowed to apply (a paragraph style that declares a value outranks the table style).
    /// </summary>
    Dictionary<string, StyleParagraphSpacing> ExtractDeclaredSpacing(
        MainDocumentPart mainPart,
        StyleValues styleType)
    {
        var result = new Dictionary<string, StyleParagraphSpacing>(StringComparer.OrdinalIgnoreCase);
        var styles = mainPart.StyleDefinitionsPart?.Styles;
        if (styles == null)
        {
            return result;
        }

        var byId = new Dictionary<string, Style>(StringComparer.OrdinalIgnoreCase);
        foreach (var style in styles.Elements<Style>())
        {
            if (style.Type?.Value == styleType &&
                style.StyleId?.Value is { } id)
            {
                byId[id] = style;
            }
        }

        foreach (var (id, _) in byId)
        {
            // Walk to the root of the basedOn chain, then layer back down so a child's own
            // declarations win. The visited set guards the malformed-cycle case.
            var chain = new List<Style>();
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var currentId = id;
            while (visited.Add(currentId) &&
                   byId.TryGetValue(currentId, out var style))
            {
                chain.Add(style);
                if (style.BasedOn?.Val?.Value is not { } baseId)
                {
                    break;
                }

                currentId = baseId;
            }

            var resolved = new StyleParagraphSpacing();
            for (var i = chain.Count - 1; i >= 0; i--)
            {
                resolved = resolved.Layer(ReadDeclaredSpacing(chain[i].StyleParagraphProperties));
            }

            result[id] = resolved;
        }

        return result;
    }

    Dictionary<string, ParagraphProperties> ExtractStyleParagraphProperties(MainDocumentPart mainPart)
    {
        var styleProps = new Dictionary<string, ParagraphProperties>(StringComparer.OrdinalIgnoreCase);
        styleIndentBound.Clear();
        styleUnboundIndents.Clear();

        var stylesPart = mainPart.StyleDefinitionsPart;
        if (stylesPart?.Styles == null)
        {
            return styleProps;
        }

        var styles = stylesPart.Styles.Elements<Style>().ToList();
        var processed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var existingStyleIds = new HashSet<string>(
            styles.Select(_ => _.StyleId?.Value).Where(id => id != null)!,
            StringComparer.OrdinalIgnoreCase);

        // Capture the default paragraph style id (typically "Normal") so unstyled paragraphs
        // can layer its properties on top of pPrDefault — Word applies it implicitly.
        foreach (var style in styles)
        {
            if (style.Type?.Value == StyleValues.Paragraph &&
                style.Default?.Value == true &&
                style.StyleId?.Value is { } defaultPStyleId)
            {
                defaultParagraphStyleId = defaultPStyleId;
                break;
            }
        }

        // Process styles with proper inheritance - may need multiple passes
        // to handle chains like: Title -> Normal -> (base)
        int lastCount;
        do
        {
            lastCount = processed.Count;
            foreach (var style in styles)
            {
                var styleId = style.StyleId?.Value;
                if (styleId == null || processed.Contains(styleId))
                {
                    continue;
                }

                var basedOnId = style.BasedOn?.Val?.Value;
                // Wait for the base style only if it exists in this document; a basedOn naming a
                // missing style is processed without waiting.
                if (basedOnId != null &&
                    existingStyleIds.Contains(basedOnId) &&
                    !processed.Contains(basedOnId))
                {
                    continue;
                }

                ParagraphProperties? baseProps = null;
                if (basedOnId != null)
                {
                    styleProps.TryGetValue(basedOnId, out baseProps);
                }

                // Whether the base's indentation is its numbering's, and what it falls back to without
                // the numbering (see styleIndentBound).
                var baseIndentBound = basedOnId != null &&
                                      styleIndentBound.Contains(basedOnId);
                var baseUnboundIndent = basedOnId != null &&
                                        styleUnboundIndents.TryGetValue(basedOnId, out var inheritedUnbound)
                    ? inheritedUnbound
                    : (defaultLeftIndentPoints, 0d, 0d, defaultRightIndentPoints);

                var paraProps = style.StyleParagraphProperties;

                // Start with base style properties or document defaults (pPrDefault)
                var alignment = baseProps?.Alignment ?? defaultAlignment;
                var spacingBefore = baseProps?.SpacingBeforePoints ?? defaultSpacingBeforePoints;
                var spacingAfter = baseProps?.SpacingAfterPoints ?? defaultSpacingAfterPoints;
                var spacingBeforeAuto = baseProps?.SpacingBeforeAuto ?? false;
                var spacingAfterAuto = baseProps?.SpacingAfterAuto ?? false;
                // An absent w:spacing/@w:line is SINGLE, the font's own line box (Word probe
                // 2026-08-04, resumes/01 and agendas-minutes/15: inherited pitch == explicit
                // w:line="240" == the hhea box, for Calibri Light, Segoe UI and Aptos alike).
                // Where Word's BUILT-IN paragraph defaults are in force (see
                // ExtractDefaultParagraphProperties) they stand under every style as a declared
                // pPrDefault would (_probe_styleline_a/b/c/e, 2026-10-11: a style based on an
                // undefined Normal, one with no basedOn, one declaring only w:after and one declaring
                // nothing each keep the 278/240 pitch, 17.1pt at 12pt Calibri against 14.7 single;
                // complex_spacing's CustomStyle1).
                var lineSpacingMultiplier = baseProps?.LineSpacingMultiplier
                                            ?? docDefaultLineSpacingMultiplier
                                            ?? defaultLineSpacingMultiplier;
                var lineSpacingPoints = baseProps?.LineSpacingPoints ?? 0;
                var lineSpacingRule = baseProps?.LineSpacingRule ?? LineSpacingRule.Auto;
                var firstLineIndent = baseProps?.FirstLineIndentPoints ?? 0;
                var leftIndent = baseProps?.LeftIndentPoints ?? defaultLeftIndentPoints;
                var rightIndent = baseProps?.RightIndentPoints ?? defaultRightIndentPoints;
                var hangingIndent = baseProps?.HangingIndentPoints ?? 0;
                var contextualSpacing = baseProps?.ContextualSpacing ?? false;

                // Pagination properties from base style. pageBreakBefore inherits like the others
                // (_probe_bp10_nointro/_nokeep, 2026-08-07): a Heading1 whose only pageBreakBefore
                // is the STYLE's breaks the page, and an inline w:val="0" suppresses it
                // (_probe_bp10_pbboff); business-plans/10's "Campaign Sign-off" page 5.
                var keepLines = baseProps?.KeepLines ?? false;
                var keepNext = baseProps?.KeepNext ?? false;
                var widowControl = baseProps?.WidowControl ?? true;
                var pageBreakBefore = baseProps?.PageBreakBefore ?? false;
                var backgroundColor = baseProps?.BackgroundColorHex;

                // If no paragraph properties, still save inherited properties
                if (paraProps == null)
                {
                    if (baseIndentBound)
                    {
                        styleIndentBound.Add(styleId);
                    }

                    styleUnboundIndents[styleId] = baseUnboundIndent;
                    styleProps[styleId] = new()
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
                        ContextualSpacing = contextualSpacing,
                        KeepLines = keepLines,
                        KeepNext = keepNext,
                        WidowControl = widowControl,
                        BackgroundColorHex = backgroundColor,
                        TabStops = baseProps?.TabStops ?? [],
                        DefaultTabStopPoints = defaultTabStopPoints,
                        Frame = baseProps?.Frame,
                        PageBreakBefore = pageBreakBefore,
                        SnapToGrid = baseProps?.SnapToGrid ?? true,
                        FontAlignment = baseProps?.FontAlignment ?? FontAlignment.Baseline
                    };
                    processed.Add(styleId);
                    continue;
                }

                // Text-frame positioning (w:framePr). A style's own framePr replaces any inherited
                // one; otherwise the base style's frame carries through.
                var frame = baseProps?.Frame;
                var styleFramePr = paraProps.GetFirstChild<FrameProperties>();
                if (styleFramePr != null)
                {
                    frame = ParseParagraphFrame(styleFramePr);
                }

                var justification = paraProps.GetFirstChild<Justification>();
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

                var spacing = paraProps.GetFirstChild<SpacingBetweenLines>();
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

                // A style that switches numbering off (w:numPr with numId 0, or with no numId at all)
                // drops the indent its BASE declared beside live numbering (styleIndentBound), and
                // then applies its own w:ind like any other style. Word-probed (_probe_numoff, XPS,
                // base numbered with left=1440 hanging=720): numId=0 and no w:ind sets both lines
                // of a wrapped paragraph at the margin; numId=0 and left=2160 starts at 108pt with
                // no hanging; left=2160 hanging=720 starts at 72pt, its second line at 108pt; a
                // style based on Normal with numId=0, or only an w:ilvl, and left=1440 starts at
                // 72pt. business-plans/08's Title is the first case, resumes/07's "College info"
                // (basedOn List Bullet, numId=0, left=360) the second.
                var styleNumPr = paraProps.GetFirstChild<NumberingProperties>();
                var hasOrphanedNumPr = styleNumPr != null &&
                                       (styleNumPr.NumberingId?.Val?.Value ?? 0) == 0;
                var activeNumbering = styleNumPr != null &&
                                      !hasOrphanedNumPr;
                if (hasOrphanedNumPr &&
                    baseIndentBound)
                {
                    (leftIndent, firstLineIndent, hangingIndent, rightIndent) = baseUnboundIndent;
                }

                var indentation = paraProps.GetFirstChild<Indentation>();
                var ownIndentation = indentation != null;
                if (ownIndentation)
                {
                    firstLineIndent = indentation!.FirstLine.TwipsToPoints() ?? firstLineIndent;

                    leftIndent = indentation.Left.TwipsToPoints() ?? leftIndent;

                    rightIndent = indentation.Right.TwipsToPoints() ?? rightIndent;

                    hangingIndent = indentation.Hanging.TwipsToPoints() ?? hangingIndent;
                }

                if (activeNumbering ||
                    (baseIndentBound &&
                     !hasOrphanedNumPr &&
                     !ownIndentation))
                {
                    styleIndentBound.Add(styleId);
                }

                styleUnboundIndents[styleId] = activeNumbering || !ownIndentation
                    ? baseUnboundIndent
                    : (leftIndent, firstLineIndent, hangingIndent, rightIndent);

                if (paraProps.GetFirstChild<ContextualSpacing>() != null)
                {
                    contextualSpacing = true;
                }

                if (paraProps.GetFirstChild<KeepLines>() != null)
                {
                    keepLines = true;
                }

                var stylePageBreakBefore = paraProps.GetFirstChild<PageBreakBefore>();
                if (stylePageBreakBefore != null)
                {
                    pageBreakBefore = stylePageBreakBefore.Val?.Value ?? true;
                }

                if (paraProps.GetFirstChild<KeepNext>() != null)
                {
                    keepNext = true;
                }

                var widowControlEl = paraProps.GetFirstChild<WidowControl>();
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

                var shadingElement = paraProps.GetFirstChild<Shading>();
                if (shadingElement != null)
                {
                    backgroundColor = ResolveThemeFill(shadingElement);

                    if (backgroundColor == null &&
                        shadingElement.Fill?.HasValue == true &&
                        shadingElement.Fill.Value != "auto" &&
                        shadingElement.Fill.Value != "none")
                    {
                        backgroundColor = shadingElement.Fill.Value;
                    }
                }

                var borders = baseProps?.Borders;
                var borderTopSpace = baseProps?.BorderTopSpacePoints ?? 0;
                var borderBottomSpace = baseProps?.BorderBottomSpacePoints ?? 0;
                var borderLeftSpace = baseProps?.BorderLeftSpacePoints ?? 0;
                var borderRightSpace = baseProps?.BorderRightSpacePoints ?? 0;
                var borderBetween = baseProps?.BorderBetween ?? BorderEdge.None;
                var borderBetweenSpace = baseProps?.BorderBetweenSpacePoints ?? 0;
                var pBdr = paraProps.GetFirstChild<ParagraphBorders>();
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

                styleProps[styleId] = new()
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
                    ContextualSpacing = contextualSpacing,
                    KeepLines = keepLines,
                    KeepNext = keepNext,
                    WidowControl = widowControl,
                    BackgroundColorHex = backgroundColor,
                    Borders = borders,
                    BorderTopSpacePoints = borderTopSpace,
                    BorderBottomSpacePoints = borderBottomSpace,
                    BorderLeftSpacePoints = borderLeftSpace,
                    BorderRightSpacePoints = borderRightSpace,
                    BorderBetween = borderBetween,
                    BorderBetweenSpacePoints = borderBetweenSpace,
                    TabStops = ParseTabs(paraProps, baseProps?.TabStops ?? []),
                    DefaultTabStopPoints = defaultTabStopPoints,
                    Frame = frame,
                    PageBreakBefore = pageBreakBefore,
                    SnapToGrid = paraProps.GetFirstChild<SnapToGrid>() is { } styleSnap ? styleSnap.IsOn() : baseProps?.SnapToGrid ?? true,
                    FontAlignment = FontAlignmentOf(paraProps.GetFirstChild<DocumentFormat.OpenXml.Wordprocessing.TextAlignment>()) ?? baseProps?.FontAlignment ?? FontAlignment.Baseline
                };
                processed.Add(styleId);
            }
        } while (processed.Count > lastCount);

        return styleProps;
    }

    static Dictionary<string, (bool Left, bool Hanging, string? BasedOn)> ExtractStyleOwnIndents(MainDocumentPart mainPart)
    {
        var result = new Dictionary<string, (bool Left, bool Hanging, string? BasedOn)>(StringComparer.OrdinalIgnoreCase);
        foreach (var style in mainPart.StyleDefinitionsPart?.Styles?.Elements<Style>() ?? [])
        {
            if (style.StyleId?.Value is not { } id ||
                style.Type?.Value != StyleValues.Paragraph)
            {
                continue;
            }

            var indentation = style.StyleParagraphProperties?.GetFirstChild<Indentation>();
            result.TryAdd(
                id,
                (indentation?.Left?.HasValue == true || indentation?.Start?.HasValue == true,
                    indentation?.Hanging?.HasValue == true,
                    style.BasedOn?.Val?.Value));
        }

        return result;
    }

    // Whether a style-numbered paragraph's style indent comes from a style at or above the one that puts
    // it in the list, which then outranks the list level's own (agendas-minutes/17's Bullets tightens its
    // level's 1800 to 360). An indent from a base BELOW that style is outranked by the level, as the
    // level stands between the two: resumes/19's Skills Bullets is based on Bullets Skills, which names
    // the list (level 288 left, 288 hanging), based on Body Contact Info, which indents 14 twips; Word
    // sets the items at the level's 288, with their bullets, where taking the 14 hung the bullets off
    // the cell.
    (bool Left, bool Hanging) StyleIndentBeforeList(string styleId)
    {
        if (styleOwnIndents == null ||
            styleNumberingOwners == null)
        {
            return (true, true);
        }

        var left = false;
        var hanging = false;
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var current = (string?) styleId;
        while (current != null &&
               visited.Add(current) &&
               styleOwnIndents.TryGetValue(current, out var own))
        {
            left |= own.Left;
            hanging |= own.Hanging;
            if (styleNumberingOwners.Contains(current))
            {
                return (left, hanging);
            }

            current = own.BasedOn;
        }

        return (true, true);
    }

    static (Dictionary<string, (int numId, int ilvl)> Numbering, HashSet<string> Owners) ExtractStyleNumbering(MainDocumentPart mainPart)
    {
        var result = new Dictionary<string, (int numId, int ilvl)>(StringComparer.OrdinalIgnoreCase);

        // Method 1: Extract from numbering.xml pStyle links (numbering definitions that link TO styles)
        var numberingPart = mainPart.NumberingDefinitionsPart;
        if (numberingPart?.Numbering != null)
        {
            var numbering = numberingPart.Numbering;

            // Build abstractNumId -> List of (ilvl, styleId)
            var abstractStyleLinks = new Dictionary<int, List<(int ilvl, string styleId)>>();
            foreach (var abstractNum in numbering.Elements<AbstractNum>())
            {
                var abstractNumId = abstractNum.AbstractNumberId?.Value ?? 0;
                foreach (var level in abstractNum.Elements<Level>())
                {
                    var pStyle = level.GetFirstChild<ParagraphStyleIdInLevel>();
                    if (pStyle?.Val?.Value != null)
                    {
                        if (!abstractStyleLinks.TryGetValue(abstractNumId, out var value))
                        {
                            value = new List<(int, string)>();
                            abstractStyleLinks[abstractNumId] = value;
                        }

                        var ilvl = level.LevelIndex?.Value ?? 0;
                        value.Add((ilvl, pStyle.Val.Value));
                    }
                }
            }

            // Map numId -> abstractNumId, then look up style links
            foreach (var numInstance in numbering.Elements<NumberingInstance>())
            {
                var numId = numInstance.NumberID?.Value ?? 0;
                var abstractNumIdRef = numInstance.AbstractNumId?.Val?.Value ?? 0;

                if (abstractStyleLinks.TryGetValue(abstractNumIdRef, out var styleLinks))
                {
                    foreach (var (ilvl, styleId) in styleLinks)
                    {
                        result.TryAdd(styleId, (numId, ilvl));
                    }
                }
            }
        }

        // Method 2: Extract from styles that have numPr directly
        var declaredNone = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var stylesPart = mainPart.StyleDefinitionsPart;
        if (stylesPart?.Styles == null)
        {
            return (result, new(result.Keys, StringComparer.OrdinalIgnoreCase));
        }

        foreach (var style in stylesPart.Styles.Elements<Style>())
        {
            var styleId = style.StyleId?.Value;
            if (styleId == null)
            {
                continue;
            }

            var pPr = style.StyleParagraphProperties;
            if (pPr == null)
            {
                continue;
            }

            var numPr = pPr.GetFirstChild<NumberingProperties>();
            if (numPr == null)
            {
                continue;
            }

            var numId = numPr.NumberingId?.Val?.Value ?? 0;
            var ilvl = numPr.NumberingLevelReference?.Val?.Value ?? 0;

            if (numId > 0)
            {
                result[styleId] = (numId, ilvl);
            }
            else if (numPr.NumberingId != null)
            {
                declaredNone.Add(styleId);
            }
        }

        var owners = new HashSet<string>(result.Keys, StringComparer.OrdinalIgnoreCase);

        // A style that declares no list takes its base style's, down the w:basedOn chain; one that
        // declares numId 0 stops it. resumes/19's Skills Bullets is based on Bullets Skills, whose
        // numPr is the list, and its items were drawn without bullets.
        var paragraphStyles = stylesPart.Styles.Elements<Style>()
            .Where(_ => _.StyleId?.Value != null)
            .GroupBy(_ => _.StyleId!.Value!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(_ => _.Key, _ => _.First(), StringComparer.OrdinalIgnoreCase);
        foreach (var styleId in paragraphStyles.Keys)
        {
            if (result.ContainsKey(styleId) ||
                declaredNone.Contains(styleId))
            {
                continue;
            }

            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {styleId};
            var baseId = paragraphStyles[styleId].BasedOn?.Val?.Value;
            while (baseId != null &&
                   visited.Add(baseId) &&
                   !declaredNone.Contains(baseId))
            {
                if (result.TryGetValue(baseId, out var inherited))
                {
                    result[styleId] = inherited;
                    break;
                }

                baseId = paragraphStyles.TryGetValue(baseId, out var baseStyle) ? baseStyle.BasedOn?.Val?.Value : null;
            }
        }

        return (result, owners);
    }

    Dictionary<string, TableStyleBorderInfo> ExtractTableStyleBorders(MainDocumentPart mainPart)
    {
        var result = new Dictionary<string, TableStyleBorderInfo>(StringComparer.OrdinalIgnoreCase);

        var stylesPart = mainPart.StyleDefinitionsPart;
        if (stylesPart?.Styles == null)
        {
            return result;
        }

        // Index table styles by ID so we can resolve inherited (w:basedOn) cell margins.
        var tableStylesById = new Dictionary<string, Style>(StringComparer.OrdinalIgnoreCase);
        foreach (var style in stylesPart.Styles.Elements<Style>())
        {
            if (style.Type?.Value == StyleValues.Table &&
                style.StyleId?.Value is { } id)
            {
                tableStylesById[id] = style;
            }
        }

        foreach (var style in stylesPart.Styles.Elements<Style>())
        {
            var styleId = style.StyleId?.Value;
            if (styleId == null)
            {
                continue;
            }

            if (style.Type?.Value != StyleValues.Table)
            {
                continue;
            }

            if (style.Default?.Value == true)
            {
                defaultTableStyleId = styleId;
            }

            var tblPr = style.StyleTableProperties;

            // Borders come from the w:basedOn chain, per side, like the cell padding below — a house
            // style that adds only conditional formatting on top of a base carrying the grid would
            // otherwise render with no rules at all.
            var (cellBorders, insideH, insideV) = TableStyleParser.ResolveStyleBorders(style, tableStylesById, currentThemeColors);
            var colBandSize = 1;
            var rowBandSize = 1;

            if (tblPr != null)
            {
                var colBand = tblPr.GetFirstChild<TableStyleColumnBandSize>();
                if (colBand?.Val?.HasValue == true)
                {
                    colBandSize = colBand.Val.Value;
                }

                var rowBand = tblPr.GetFirstChild<TableStyleRowBandSize>();
                if (rowBand?.Val?.HasValue == true)
                {
                    rowBandSize = rowBand.Val.Value;
                }
            }

            // w:tblCellSpacing on the style-level tblPr propagates to every table that
            // references the style (TableWeb1/2/3 etc. all use this for HTML-style detached borders).
            var styleCellSpacing = ReadTableCellSpacing(tblPr);

            // Whole-table cell shading lives on the style's StyleTableCellProperties (the
            // <w:tcPr> that's a direct child of <w:style>, distinct from
            // StyleTableProperties which holds <w:tblPr>). All cells inherit this fill
            // unless a more-specific conditional region or an explicit cell w:shd overrides.
            var wholeTableShading = BorderParser.ReadShadingFill(style.StyleTableCellProperties?.GetFirstChild<Shading>());

            // Whole-table cell vAlign also lives on StyleTableCellProperties — Word's
            // built-in form-style tables (FormTable / TableGrid) put vAlign="center" here
            // so every data row's content sits centred between the row's top and bottom borders.
            CellVerticalAlignment? styleVerticalAlignment = null;
            var styleVAlign = style.StyleTableCellProperties?.GetFirstChild<TableCellVerticalAlignment>();
            if (styleVAlign?.Val?.HasValue == true)
            {
                var vAlignVal = styleVAlign.Val.Value;
                if (vAlignVal == TableVerticalAlignmentValues.Center)
                {
                    styleVerticalAlignment = CellVerticalAlignment.Center;
                }
                else if (vAlignVal == TableVerticalAlignmentValues.Bottom)
                {
                    styleVerticalAlignment = CellVerticalAlignment.Bottom;
                }
                else
                {
                    styleVerticalAlignment = CellVerticalAlignment.Top;
                }
            }

            var conditionals = TableStyleParser.ResolveStyleConditionals(style, tableStylesById, currentThemeColors);

            // Resolve default cell padding by walking the w:basedOn chain — Word's built-in
            // TableGrid inherits its tblCellMar from TableNormal, so a table that names
            // TableGrid would otherwise lose the 108-twip start/end padding.
            var styleCellPadding = TableStyleParser.ResolveStyleCellPadding(style, tableStylesById);
            var styleIndent = TableStyleParser.ResolveStyleIndent(style, tableStylesById);
            var styleCannotSplit = TableStyleParser.ResolveStyleCannotSplit(style, tableStylesById);

            // A style that declares nothing but run properties still hands them to its cells: probed
            // with a table style carrying only w:rPr/w:kern, whose text Word kerns or not as the
            // style says (_probe_kerncascade, A10 / A12 / B8).
            var styleRun = TableStyleParser.ResolveStyleRunProperties(style, tableStylesById);

            var styleCellMargin = TableStyleParser.ResolveStyleCellMargin(style, tableStylesById);

            if (cellBorders.HasAnyBorder || insideH.IsVisible || insideV.IsVisible || wholeTableShading != null || conditionals != null || styleCellSpacing > 0 || styleVerticalAlignment != null || styleCellPadding != null || styleRun != null || styleIndent != null || styleCannotSplit || styleCellMargin != null)
            {
                result[styleId] = new(cellBorders, insideH, insideV, wholeTableShading, rowBandSize, colBandSize, styleCellSpacing, conditionals, styleVerticalAlignment, styleCellPadding, styleRun, styleIndent, styleCannotSplit, styleCellMargin);
            }
        }

        return result;
    }

    void ExtractDefaultParagraphProperties(MainDocumentPart mainPart)
    {
        // Cleared per parse so a reused parser instance can't carry one document's default into
        // the next; every path below either assigns it or leaves the document without one.
        docDefaultLineSpacingMultiplier = null;

        var stylesPart = mainPart.StyleDefinitionsPart;

        // No styles.xml, or styles with no docDefaults element at all: the document declares
        // no document-wide defaults, so Word supplies its normal.dotm built-in 8pt-after.
        // Minimal hand-built docx (multiple_pages, table_multipage, header_footer) have no
        // styles.xml and rely on this.
        if (stylesPart?.Styles == null)
        {
            defaultSpacingAfterPoints = builtInSpacingAfterPoints;
            defaultLineSpacingMultiplier = builtInLineSpacingMultiplier;
            return;
        }

        var docDefaults = stylesPart.Styles.DocDefaults;
        if (docDefaults == null)
        {
            defaultSpacingAfterPoints = builtInSpacingAfterPoints;
            defaultLineSpacingMultiplier = builtInLineSpacingMultiplier;
            return;
        }

        // docDefaults IS present but declares NO pPrDefault element at all: nothing has
        // replaced Word's BUILT-IN paragraph defaults (8pt-after, line=278), so every paragraph
        // still starts from them. Measured against Word's references: even_odd_headers/02's
        // footer line and decimal_tabs/01's rows each sit exactly the built-in spacing below a
        // zero-spacing render (row pitch 46.5px vs the 25.5px bare line at 150 DPI). An EMPTY
        // <w:pPrDefault/> is different — it is an explicit "no paragraph defaults" and Word
        // reads it as zero: wordart declares one and its reference is tight.
        //
        // Whether the package defines a default paragraph style makes no difference
        // (_probe_styleline_a to _e, 2026-10-11, XPS): with an rPrDefault and no pPrDefault, an
        // unstyled paragraph is pitched 17.4pt and followed by 8pt whether Normal is defined (and
        // empty) or not, against 14.7pt and nothing under an empty pPrDefault. resumes/13,
        // cover-letters/03, letters/09 and wedding/05 are tight because their pPrDefault is EMPTY.
        if (docDefaults.ParagraphPropertiesDefault == null)
        {
            defaultSpacingAfterPoints = builtInSpacingAfterPoints;
            defaultLineSpacingMultiplier = builtInLineSpacingMultiplier;
        }

        // docDefaults IS present but carries no paragraph defaults beyond the cases above:
        // the document explicitly configured its document-wide defaults and omitted
        // paragraph spacing, which Word reads as zero — not the 8pt built-in.
        var pPrDefault = docDefaults.ParagraphPropertiesDefault;
        if (pPrDefault?.ParagraphPropertiesBaseStyle == null)
        {
            return;
        }

        var pPr = pPrDefault.ParagraphPropertiesBaseStyle;

        var spacing = pPr.SpacingBetweenLines;
        if (spacing != null)
        {
            defaultSpacingAfterPoints = spacing.After.TwipsToPoints() ?? defaultSpacingAfterPoints;
            defaultSpacingAfterPoints = LineUnitsToPoints(spacing.AfterLines) ?? defaultSpacingAfterPoints;

            defaultSpacingBeforePoints = spacing.Before.TwipsToPoints() ?? defaultSpacingBeforePoints;
            defaultSpacingBeforePoints = LineUnitsToPoints(spacing.BeforeLines) ?? defaultSpacingBeforePoints;

            // w:line is a multiplier only under lineRule="auto" (absent defaults to auto); under
            // "exact"/"atLeast" it is a twip measurement and belongs to the per-paragraph path.
            if (spacing.Line?.HasValue == true &&
                (spacing.LineRule?.Value ?? LineSpacingRuleValues.Auto) == LineSpacingRuleValues.Auto)
            {
                docDefaultLineSpacingMultiplier = double.Parse(spacing.Line.Value!) / 240.0;
            }
        }

        var indentation = pPr.Indentation;
        if (indentation != null)
        {
            defaultLeftIndentPoints = indentation.Left.TwipsToPoints() ?? defaultLeftIndentPoints;

            defaultRightIndentPoints = indentation.Right.TwipsToPoints() ?? defaultRightIndentPoints;
        }

        // Alignment default (w:jc) — the base of the alignment cascade.
        var justification = pPr.Justification;
        if (justification?.Val?.HasValue == true)
        {
            var justVal = justification.Val.Value;
            if (justVal == JustificationValues.Center)
            {
                defaultAlignment = TextAlignment.Center;
            }
            else if (justVal == JustificationValues.Right)
            {
                defaultAlignment = TextAlignment.Right;
            }
            else if (justVal == JustificationValues.Both || justVal == JustificationValues.Distribute)
            {
                defaultAlignment = TextAlignment.Justify;
            }
        }
    }

    // A styles part that defines one styleId twice: Word keeps the LAST definition and nothing of the
    // ones before it. Word-probed 2026-10-11 (_probe_headname_a to _f) on complex_tables' own styles
    // part, which declares Heading1 and Heading2 first as blue 16pt and 13pt, and again at its end as
    // bold black 18pt and 14pt with space above and below: Word set the headings at 18.025 and 13.8pt
    // on the second definitions' spacing, and adding italics, centring and an indent to the FIRST
    // definition moved nothing. Every reader of the styles walks the part in order and takes the
    // first match, so the earlier definitions are dropped from the tree before anything reads it.
    static void DropRedefinedStyles(MainDocumentPart mainPart, List<Action>? restore)
    {
        if (mainPart.StyleDefinitionsPart?.Styles is not { } styles)
        {
            return;
        }

        var last = new Dictionary<string, Style>(StringComparer.Ordinal);
        List<Style>? redefined = null;
        foreach (var style in styles.Elements<Style>())
        {
            if (style.StyleId?.Value is not { } styleId)
            {
                continue;
            }

            if (last.TryGetValue(styleId, out var earlier))
            {
                (redefined ??= []).Add(earlier);
            }

            last[styleId] = style;
        }

        if (redefined == null)
        {
            return;
        }

        foreach (var style in redefined)
        {
            if (restore != null &&
                style.Parent is { } parent)
            {
                var previous = style.PreviousSibling();
                restore.Add(() =>
                {
                    if (previous == null)
                    {
                        parent.PrependChild(style);
                    }
                    else
                    {
                        parent.InsertAfter(style, previous);
                    }
                });
            }

            style.Remove();
        }
    }

    // Percentage-sized anchors take their size from the page of the section they stand in: a section
    // ends at the paragraph carrying its w:sectPr, and the body's own w:sectPr is the last section's.
    void FoldRelativeSizes(Body body, PageSettings firstSection)
    {
        // Walked from the end, so that each element is under the section properties that close it.
        var page = body.GetFirstChild<SectionProperties>() is { } last ? ExtractPageSettings(last) : firstSection;
        for (var index = body.ChildElements.Count - 1; index >= 0; index--)
        {
            var child = body.ChildElements[index];
            if (child is Paragraph {ParagraphProperties.SectionProperties: { } section})
            {
                page = ExtractPageSettings(section);
            }

            child.FoldRelativeSizes(page, restore);
        }
    }

    // Whether a style, or one it is based on, declares something in its own run properties. Bounded, so a
    // chain that loops back on itself ends.
    bool DeclaresUpChain(MainDocumentPart mainPart, Style? style, Func<StyleRunProperties, bool> declares)
    {
        for (var depth = 0; style != null && depth < 16; depth++)
        {
            if (style.StyleRunProperties is { } properties &&
                declares(properties))
            {
                return true;
            }

            style = style.BasedOn?.Val?.Value is { } basedOn ? LookupStyle(mainPart, basedOn) : null;
        }

        return false;
    }

    // First wins on duplicate style ids.
    Style? LookupStyle(MainDocumentPart mainPart, string styleId)
    {
        if (stylesById == null)
        {
            stylesById = new(StringComparer.Ordinal);
            var styles = mainPart.StyleDefinitionsPart?.Styles;
            if (styles != null)
            {
                foreach (var style in styles.Elements<Style>())
                {
                    if (style.StyleId?.Value is { } id)
                    {
                        stylesById.TryAdd(id, style);
                    }
                }
            }
        }

        if (stylesById.TryGetValue(styleId, out var match))
        {
            return match;
        }

        return null;
    }
}
