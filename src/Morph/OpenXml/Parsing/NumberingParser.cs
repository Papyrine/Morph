using A = DocumentFormat.OpenXml.Drawing;

/// <summary>
/// Reads the numbering part into <see cref="NumberingLevelDefinition"/>s and formats list
/// numbers (decimal, roman, letter, …). The per-paragraph counters stay in
/// <see cref="DocumentParser"/>, since they are state of one parse.
/// </summary>
static class NumberingParser
{
    public static Dictionary<int, Dictionary<int, NumberingLevelDefinition>> ExtractNumberingDefinitions(MainDocumentPart mainPart, Dictionary<int, int> numIdToAbstract, HashSet<(int numId, int ilvl)> startOverrides)
    {
        var result = new Dictionary<int, Dictionary<int, NumberingLevelDefinition>>();
        var numberingPart = mainPart.NumberingDefinitionsPart;
        if (numberingPart?.Numbering == null)
        {
            return result;
        }

        var numbering = numberingPart.Numbering;

        // First, collect abstract numbering definitions (abstractNumId -> levels)
        var abstractNums = new Dictionary<int, Dictionary<int, NumberingLevelDefinition>>();
        foreach (var abstractNum in numbering.Elements<AbstractNum>())
        {
            var abstractNumId = abstractNum.AbstractNumberId?.Value ?? 0;
            var levels = new Dictionary<int, NumberingLevelDefinition>();

            foreach (var level in abstractNum.Elements<Level>())
            {
                var ilvl = level.LevelIndex?.Value ?? 0;
                var levelText = level.LevelText?.Val?.Value ?? "";
                var numFmt = level.NumberingFormat?.Val?.Value;

                var isBullet = numFmt == NumberFormatValues.Bullet;

                string? fontFamily = null;
                double? fontSizePoints = null;
                string? markerColorHex = null;
                var runProps = level.NumberingSymbolRunProperties;
                if (runProps != null)
                {
                    // The level's own label size (w:lvl/w:rPr/w:sz, half-points).
                    if (runProps.GetFirstChild<FontSize>()?.Val?.Value is { } halfPoints &&
                        double.TryParse(halfPoints, NumberStyles.Float, CultureInfo.InvariantCulture, out var labelHalfPoints) &&
                        labelHalfPoints > 0)
                    {
                        fontSizePoints = labelHalfPoints / 2;
                    }

                    var fonts = runProps.GetFirstChild<RunFonts>();
                    if (fonts?.Ascii?.HasValue == true)
                    {
                        fontFamily = fonts.Ascii.Value;
                    }
                    else if (fonts?.HighAnsi?.HasValue == true)
                    {
                        fontFamily = fonts.HighAnsi.Value;
                    }

                    // The level's own marker colour (w:lvl/w:rPr/w:color). Direct hex only:
                    // "auto" keeps the paragraph fallback.
                    var levelColor = runProps.GetFirstChild<Color>()?.Val?.Value;
                    if (levelColor != null &&
                        !levelColor.Equals("auto", StringComparison.OrdinalIgnoreCase))
                    {
                        markerColorHex = levelColor;
                    }
                }

                double leftIndent = 0;
                double hangingIndent = 0;
                var pPr = level.GetFirstChild<PreviousParagraphProperties>();
                var indentation = pPr?.GetFirstChild<Indentation>() ?? level.GetFirstChild<Indentation>();
                if (indentation != null)
                {
                    leftIndent = indentation.Left.TwipsToPoints() ?? leftIndent;

                    hangingIndent = indentation.Hanging.TwipsToPoints() ?? hangingIndent;
                }

                var startNumber = level.StartNumberingValue?.Val?.Value ?? 1;
                var lvlRestart = level.LevelRestart?.Val?.Value;
                var rightAligned = level.LevelJustification?.Val?.Value == LevelJustificationValues.Right;

                levels[ilvl] = new()
                {
                    LevelText = levelText,
                    FontFamily = fontFamily,
                    FontSizePoints = fontSizePoints,
                    ColorHex = markerColorHex,
                    LeftIndentPoints = leftIndent,
                    HangingIndentPoints = hangingIndent,
                    IsBullet = isBullet,
                    StartNumber = startNumber,
                    NumberFormat = numFmt ?? NumberFormatValues.Decimal,
                    LevelRestart = lvlRestart,
                    Suffix = MarkerSuffix(level),
                    RightAligned = rightAligned
                };
            }

            abstractNums[abstractNumId] = levels;
        }

        // Now map numId to abstractNumId, applying any level overrides
        foreach (var numInstance in numbering.Elements<NumberingInstance>())
        {
            var numId = numInstance.NumberID?.Value ?? 0;
            var abstractNumIdRef = numInstance.AbstractNumId?.Val?.Value ?? 0;

            if (!abstractNums.TryGetValue(abstractNumIdRef, out var abstractLevels))
            {
                continue;
            }

            numIdToAbstract[numId] = abstractNumIdRef;

            // Check for level overrides (w:lvlOverride with w:startOverride)
            var overrides = numInstance.Elements<LevelOverride>().ToList();
            if (overrides.Count == 0)
            {
                result[numId] = abstractLevels;
                continue;
            }

            // Clone levels and apply overrides
            var levels = new Dictionary<int, NumberingLevelDefinition>(abstractLevels);
            foreach (var lvlOverride in overrides)
            {
                var overrideIlvl = lvlOverride.LevelIndex?.Value ?? 0;
                var startOverride = lvlOverride.StartOverrideNumberingValue?.Val?.Value;

                if (startOverride != null &&
                    levels.TryGetValue(overrideIlvl, out var baseDef))
                {
                    startOverrides.Add((numId, overrideIlvl));
                    levels[overrideIlvl] = new()
                    {
                        LevelText = baseDef.LevelText,
                        ColorHex = baseDef.ColorHex,
                        FontFamily = baseDef.FontFamily,
                        FontSizePoints = baseDef.FontSizePoints,
                        LeftIndentPoints = baseDef.LeftIndentPoints,
                        HangingIndentPoints = baseDef.HangingIndentPoints,
                        IsBullet = baseDef.IsBullet,
                        StartNumber = startOverride.Value,
                        NumberFormat = baseDef.NumberFormat,
                        RightAligned = baseDef.RightAligned,
                        Suffix = baseDef.Suffix
                    };
                }
            }

            result[numId] = levels;
        }

        return result;
    }

    // The five counter styles CSS list-style-type can name directly; everything else (ordinal,
    // cardinal text, hex, decimal-enclosed variants) has no faithful CSS equivalent and falls
    // through to Decimal, which is also the browser default.
    public static ListNumberFormat MapNumberFormat(NumberFormatValues format)
    {
        if (format == NumberFormatValues.UpperRoman)
        {
            return ListNumberFormat.UpperRoman;
        }

        if (format == NumberFormatValues.LowerRoman)
        {
            return ListNumberFormat.LowerRoman;
        }

        if (format == NumberFormatValues.UpperLetter)
        {
            return ListNumberFormat.UpperLetter;
        }

        if (format == NumberFormatValues.LowerLetter)
        {
            return ListNumberFormat.LowerLetter;
        }

        return ListNumberFormat.Decimal;
    }

    public static string FormatNumber(int number, NumberFormatValues format)
    {
        if (format == NumberFormatValues.UpperRoman)
        {
            return ToRoman(number);
        }

        if (format == NumberFormatValues.LowerRoman)
        {
            return ToRoman(number).ToLowerInvariant();
        }

        if (format == NumberFormatValues.UpperLetter)
        {
            return ToLetter(number);
        }

        if (format == NumberFormatValues.LowerLetter)
        {
            return ToLetter(number).ToLowerInvariant();
        }

        return number.ToString();
    }

    static string ToLetter(int number)
    {
        if (number <= 0)
        {
            return number.ToString();
        }

        // 1=A, 2=B, ..., 26=Z, 27=AA, 28=AB, ...
        var result = new StringBuilder();
        while (number > 0)
        {
            number--;
            result.Insert(0, (char) ('A' + number % 26));
            number /= 26;
        }

        return result.ToString();
    }

    static string ToRoman(int number)
    {
        if (number is <= 0 or > 3999)
        {
            return number.ToString();
        }

        ReadOnlySpan<(int value, string numeral)> romanNumerals =
        [
            (1000, "M"),
            (900, "CM"),
            (500, "D"),
            (400, "CD"),
            (100, "C"),
            (90, "XC"),
            (50, "L"),
            (40, "XL"),
            (10, "X"),
            (9, "IX"),
            (5, "V"),
            (4, "IV"),
            (1, "I")
        ];

        var result = new StringBuilder();
        foreach (var (value, numeral) in romanNumerals)
        {
            while (number >= value)
            {
                result.Append(numeral);
                number -= value;
            }
        }

        return result.ToString();
    }

    // w:lvl/w:suff — what follows a list marker; absent is a tab (ECMA-376 §17.9.29).
    static ListMarkerSuffix MarkerSuffix(Level level)
    {
        var suffix = level.LevelSuffix?.Val?.Value;
        if (suffix == LevelSuffixValues.Space)
        {
            return ListMarkerSuffix.Space;
        }

        if (suffix == LevelSuffixValues.Nothing)
        {
            return ListMarkerSuffix.Nothing;
        }

        return ListMarkerSuffix.Tab;
    }

    /// <summary>
    /// Maps a single Private Use Area bullet character to a Unicode glyph that
    /// <c>Bullets.ttf</c> (and most system fonts) can actually render.
    /// </summary>
    /// <remarks>
    /// Word stores bullet glyphs as PUA codepoints in the level's declared font - the
    /// renderer must therefore use that font, but in our pipeline bullets are drawn from
    /// the bundled <c>Morph Bullets</c> subset which only carries Unicode codepoints. The
    /// same PUA codepoint maps to a different glyph per font, so the lookup branches on
    /// <paramref name="fontFamily"/> first. Multi-character or non-PUA level texts are
    /// returned unchanged - those are e.g. the literal lowercase "o" Word emits at the
    /// second bullet level (font = Courier New).
    /// </remarks>
    public static string MapBulletPuaToUnicode(string levelText, string? fontFamily)
    {
        if (string.IsNullOrEmpty(levelText) ||
            levelText.Length != 1)
        {
            return levelText;
        }

        var c = levelText[0];
        if (c is < '' or > '')
        {
            return levelText;
        }

        // Wingdings and its siblings - codepoints reflect the standard Wingdings encoding
        // shifted into the F000 PUA block (so 0xA7 -> U+F0A7, etc.).
        if (fontFamily != null &&
            fontFamily.StartsWith("Wingdings", StringComparison.OrdinalIgnoreCase))
        {
            return c switch
            {
                // Wingdings 0x76 - small filled square
                '' => "▪",
                // Wingdings 0xA7 - black small square (Word's default bullet at ilvl 2)
                '' => "▪",
                // Wingdings 0xA8 - white square, which is how a Word template draws an empty
                // checkbox. U+2610 rather than U+25A2 white square: Bullets.ttf carries the
                // ballot box and not the square, so the closer codepoint rendered as nothing.
                '' => "☐",
                // Wingdings 0xFE - ballot box with check, the checked half of that pair (the font
                // draws a tick in the box, not a cross: that is 0xFD, and 0x78)
                '' => "☑",
                // Wingdings 0xFC - check mark
                '' => "✓",
                // Wingdings 0xFB - ballot X
                '' => "✗",
                // Wingdings 0xD8 - black right-pointing pointer
                '' => "►",
                // Default Wingdings bullet to a small square
                _ => "▪"
            };
        }

        // Symbol font (and unknown/unspecified fonts - Symbol is by far the most common
        // bullet font in Word templates, so it's the safest default).
        return c switch
        {
            // Symbol 0xB7 - bullet operator
            '' => "•",
            // Symbol 0x6C - filled circle
            '' => "●",
            // Symbol 0xA8 - hollow circle
            '' => "○",
            // Symbol 0xD8 - diamond
            '' => "◆",
            _ => "•"
        };
    }
}
