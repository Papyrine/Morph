using A = DocumentFormat.OpenXml.Drawing;

/// <summary>
/// Converts a DrawingML text body (<c>p:txBody</c>, <c>a:txBody</c> or <c>dsp:txBody</c>) into the
/// shared paragraph model.
///
/// Every property resolves through a <see cref="TextStyleChain"/> rather than off the run alone,
/// because slide text is overwhelmingly inherited: a corpus run typically declares only
/// <c>lang</c> and <c>dirty</c>, taking its size, font and colour from the layout or master.
/// </summary>
sealed class DrawingTextParser(ThemeColors? themeColors, ThemeFonts? themeFonts, string defaultFont)
{
    /// <summary>DrawingML font sizes are hundredths of a point.</summary>
    const double hundredthsPerPoint = 100.0;

    /// <summary>DrawingML percentages (line spacing, autofit scale) are thousandths of a percent.</summary>
    const double thousandthsPerPercent = 100000.0;

    /// <summary>
    /// Parses every <c>a:p</c> in the body. <paramref name="fontScale"/> is the
    /// <c>a:normAutofit/@fontScale</c> shrink PowerPoint bakes in when text overflows its box,
    /// applied to every resolved size (1.0 when the body does not autofit).
    /// </summary>
    public List<DocumentElement> Parse(OpenXmlElement body, TextStyleChain chain, double fontScale = 1)
    {
        var elements = new List<DocumentElement>();
        foreach (var paragraph in body.Elements<A.Paragraph>())
        {
            elements.Add(ParseParagraph(paragraph, chain, fontScale));
        }

        return elements;
    }

    ParagraphElement ParseParagraph(A.Paragraph paragraph, TextStyleChain chain, double fontScale)
    {
        var properties = paragraph.ParagraphProperties;
        var level = properties?.Level?.Value ?? 0;

        // The marker is modelled the way the DOCX path models a list marker — as numbering on the
        // paragraph — so the exporters reconstruct list nesting and the measurer hangs text off it.
        var numbering = ResolveBullet(properties, chain, level);
        var runs = new List<Run>();

        foreach (var child in paragraph.ChildElements)
        {
            switch (child)
            {
                case A.Run run:
                    runs.Add(ParseRun(run, chain, level, fontScale));
                    break;

                // a:br is a hard line break inside the paragraph.
                case A.Break:
                    runs.Add(new()
                    {
                        Text = "\n",
                        Properties = ResolveRunProperties(null, chain, level, fontScale)
                    });
                    break;

                // A field (slide number, date) carries its last-computed value as a:t. Slide numbers
                // are re-evaluated at layout through Run.PageField; anything else keeps the cached text.
                case A.Field field:
                    runs.Add(ParseField(field, chain, level, fontScale));
                    break;
            }
        }

        return new()
        {
            Runs = runs,
            Properties = ParseParagraphProperties(paragraph, chain, level, numbering, fontScale)
        };
    }

    Run ParseRun(A.Run run, TextStyleChain chain, int level, double fontScale) =>
        new()
        {
            Text = run.Text?.Text ?? string.Empty,
            Properties = ResolveRunProperties(run.RunProperties, chain, level, fontScale),
            HyperlinkUrl = null
        };

    Run ParseField(A.Field field, TextStyleChain chain, int level, double fontScale)
    {
        var properties = ResolveRunProperties(field.RunProperties, chain, level, fontScale);
        // a:fld/@type "slidenum" is PowerPoint's PAGE: the cached a:t is right for the slide it was
        // computed on and wrong everywhere else, so it re-evaluates per page like the DOCX field.
        return new()
        {
            Text = field.Text?.Text ?? string.Empty,
            Properties = properties,
            PageField = field.Type?.Value is "slidenum" ? PageFieldKind.Page : PageFieldKind.None
        };
    }

    /// <summary>
    /// Resolves a run's effective properties: its own <c>a:rPr</c> first, then each cascade source's
    /// <c>a:defRPr</c> for the paragraph's level.
    /// </summary>
    RunProperties ResolveRunProperties(A.TextCharacterPropertiesType? own, TextStyleChain chain, int level, double fontScale)
    {
        // Own properties first, then the inherited defaults in priority order. Treating them as one
        // ordered list keeps every property on the same "first declaration wins" rule.
        var sources = new List<A.TextCharacterPropertiesType>();
        if (own != null)
        {
            sources.Add(own);
        }

        sources.AddRange(chain.DefaultRunProperties(level));

        var sizeHundredths = First(sources, _ => _.FontSize?.Value);
        var fontSize = sizeHundredths is { } hundredths
            ? hundredths / hundredthsPerPoint * fontScale
            : 18 * fontScale;

        return new()
        {
            FontFamily = ResolveFontFamily(sources),
            FontSizePoints = fontSize,
            Bold = First(sources, _ => _.Bold?.Value) ?? false,
            Italic = First(sources, _ => _.Italic?.Value) ?? false,
            Underline = First(sources, _ => _.Underline?.Value) is { } underline &&
                        underline != A.TextUnderlineValues.None,
            Strikethrough = First(sources, _ => _.Strike?.Value) is { } strike &&
                            strike != A.TextStrikeValues.NoStrike,
            AllCaps = First(sources, _ => _.Capital?.Value) == A.TextCapsValues.All,
            SmallCaps = First(sources, _ => _.Capital?.Value) == A.TextCapsValues.Small,
            ColorHex = ResolveColor(sources),
            // a:rPr/@spc is hundredths of a point, like the size.
            CharacterSpacingPoints = First(sources, _ => _.Spacing?.Value) / hundredthsPerPoint ?? 0
        };
    }

    /// <summary>
    /// The latin typeface, resolving the theme references PowerPoint writes for almost every run:
    /// <c>+mn-lt</c> is the theme's minor (body) font and <c>+mj-lt</c> the major (heading) one.
    /// </summary>
    string ResolveFontFamily(List<A.TextCharacterPropertiesType> sources)
    {
        var typeface = First(sources, _ => _.GetFirstChild<A.LatinFont>()?.Typeface?.Value);
        return typeface switch
        {
            null or "" => themeFonts?.MinorFont ?? defaultFont,
            "+mn-lt" => themeFonts?.MinorFont ?? defaultFont,
            "+mj-lt" => themeFonts?.MajorFont ?? defaultFont,
            _ => typeface
        };
    }

    string? ResolveColor(List<A.TextCharacterPropertiesType> sources)
    {
        foreach (var source in sources)
        {
            if (source.GetFirstChild<A.SolidFill>() is { } solidFill)
            {
                return ShapeParser.ExtractSolidFillColor(solidFill, themeColors);
            }
        }

        return null;
    }

    static ParagraphProperties ParseParagraphProperties(
        A.Paragraph paragraph,
        TextStyleChain chain,
        int level,
        NumberingInfo? numbering,
        double fontScale)
    {
        var properties = paragraph.ParagraphProperties;
        var alignment = properties?.Alignment?.Value
                        ?? chain.ResolveValue(level, _ => _.Alignment?.Value);

        // marL/indent are EMU-free: DrawingML indents are already in EMU-like 1/12700 pt units.
        var leftIndent = properties?.LeftMargin?.Value
                         ?? chain.ResolveValue(level, _ => _.LeftMargin?.Value);
        var firstLine = properties?.Indent?.Value
                        ?? chain.ResolveValue(level, _ => _.Indent?.Value);

        var lineSpacing = ResolveSpacing(properties?.LineSpacing, chain, level, _ => _.LineSpacing);
        var before = ResolveSpacing(properties?.SpaceBefore, chain, level, _ => _.SpaceBefore);
        var after = ResolveSpacing(properties?.SpaceAfter, chain, level, _ => _.SpaceAfter);

        // A hanging indent in DrawingML is a NEGATIVE a:indent against a positive marL — the marker
        // sits at marL+indent and the text at marL. Morph models the outdent as HangingIndentPoints.
        var leftPoints = EmuToPoints(leftIndent ?? 0);
        var firstLinePoints = EmuToPoints(firstLine ?? 0);

        return new()
        {
            Alignment = MapAlignment(alignment),
            LeftIndentPoints = leftPoints,
            FirstLineIndentPoints = firstLinePoints > 0 ? firstLinePoints : 0,
            HangingIndentPoints = firstLinePoints < 0 ? -firstLinePoints : 0,
            SpacingBeforePoints = before.Points ?? 0,
            SpacingAfterPoints = after.Points ?? 0,
            LineSpacingMultiplier = lineSpacing.Percent ?? 1.0,
            LineSpacingPoints = lineSpacing.Points ?? 0,
            LineSpacingRule = lineSpacing.Points is > 0 ? LineSpacingRule.Exactly : LineSpacingRule.Auto,
            Numbering = numbering,
            // Slides never reflow, so paragraph-level pagination controls are meaningless here.
            WidowControl = false,
            ParagraphMarkFontSizePoints = ResolveMarkSize(paragraph, chain, level, fontScale)
        };
    }

    // An empty a:p still occupies a line, and its height comes from a:endParaRPr — the only place a
    // runless paragraph declares a size.
    static double? ResolveMarkSize(A.Paragraph paragraph, TextStyleChain chain, int level, double fontScale)
    {
        var endProperties = paragraph.GetFirstChild<A.EndParagraphRunProperties>();
        var sources = new List<A.TextCharacterPropertiesType>();
        if (endProperties != null)
        {
            sources.Add(endProperties);
        }

        sources.AddRange(chain.DefaultRunProperties(level));
        return First(sources, _ => _.FontSize?.Value) / hundredthsPerPoint * fontScale;
    }

    /// <summary>
    /// A DrawingML spacing slot carries EITHER a percentage (<c>a:spcPct</c>) or an absolute value
    /// (<c>a:spcPts</c>), both in thousandths. Line spacing uses the percentage as a multiplier;
    /// before/after spacing uses points.
    /// </summary>
    static (double? Percent, double? Points) ResolveSpacing(
        OpenXmlElement? own,
        TextStyleChain chain,
        int level,
        Func<A.TextParagraphPropertiesType, OpenXmlElement?> pick)
    {
        var element = own ?? chain.Resolve(level, pick);
        if (element == null)
        {
            return (null, null);
        }

        if (element.GetFirstChild<A.SpacingPercent>()?.Val?.Value is { } percent)
        {
            return (percent / thousandthsPerPercent, null);
        }

        if (element.GetFirstChild<A.SpacingPoints>()?.Val?.Value is { } points)
        {
            return (null, points / hundredthsPerPoint);
        }

        return (null, null);
    }

    /// <summary>
    /// The paragraph's bullet, resolved through the same cascade. <c>a:buNone</c> anywhere in the
    /// chain suppresses inheritance, which is how a title placeholder stays unbulleted under a
    /// master whose body style bullets every level.
    /// </summary>
    NumberingInfo? ResolveBullet(A.ParagraphProperties? properties, TextStyleChain chain, int level)
    {
        var sources = new List<OpenXmlElement>();
        if (properties != null)
        {
            sources.Add(properties);
        }

        sources.AddRange(chain.LevelProperties(level));

        foreach (var source in sources)
        {
            if (source.GetFirstChild<A.NoBullet>() != null)
            {
                return null;
            }

            if (source.GetFirstChild<A.CharacterBullet>()?.Char?.Value is { } character)
            {
                return new()
                {
                    Text = character,
                    Level = level,
                    FontFamily = source.GetFirstChild<A.BulletFont>()?.Typeface?.Value,
                    ColorHex = source.GetFirstChild<A.BulletColor>()?.GetFirstChild<A.SolidFill>() is { } fill
                        ? ShapeParser.ExtractSolidFillColor(fill, themeColors)
                        : null
                };
            }

            if (source.GetFirstChild<A.AutoNumberedBullet>() != null)
            {
                // Autonumbered levels need a counter the parser does not yet carry across slides;
                // the marker renders as the level's ordinal placeholder until that lands.
                return new()
                {
                    Text = "1.",
                    Level = level,
                    FontFamily = source.GetFirstChild<A.BulletFont>()?.Typeface?.Value
                };
            }
        }

        return null;
    }

    static TextAlignment MapAlignment(A.TextAlignmentTypeValues? alignment)
    {
        if (alignment == null)
        {
            return TextAlignment.Left;
        }

        if (alignment == A.TextAlignmentTypeValues.Center)
        {
            return TextAlignment.Center;
        }

        if (alignment == A.TextAlignmentTypeValues.Right)
        {
            return TextAlignment.Right;
        }

        if (alignment == A.TextAlignmentTypeValues.Justified ||
            alignment == A.TextAlignmentTypeValues.JustifiedLow ||
            alignment == A.TextAlignmentTypeValues.Distributed)
        {
            return TextAlignment.Justify;
        }

        return TextAlignment.Left;
    }

    static double EmuToPoints(int emu) => emu / 12700.0;

    static T? First<T>(List<A.TextCharacterPropertiesType> sources, Func<A.TextCharacterPropertiesType, T?> pick)
        where T : struct
    {
        foreach (var source in sources)
        {
            if (pick(source) is { } value)
            {
                return value;
            }
        }

        return null;
    }

    static string? First(List<A.TextCharacterPropertiesType> sources, Func<A.TextCharacterPropertiesType, string?> pick)
    {
        foreach (var source in sources)
        {
            if (pick(source) is { Length: > 0 } value)
            {
                return value;
            }
        }

        return null;
    }
}
