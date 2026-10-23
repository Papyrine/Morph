/// <summary>
/// The lists Word's Bullets and Numbering buttons start: nine levels each, half an inch deeper a
/// level, the bullets cycling through a disc, a circle and a square, the numbers through 1., a. and
/// i. Read off a corpus document's numbering part (agendas-minutes/01, agendas-minutes/02), which is
/// what Word writes for them; the per-level template codes (<c>w:tplc</c>) are left out, being Word's
/// own bookkeeping for its gallery.
/// </summary>
static class ListTemplates
{
    // The disc, circle and square, each in the font it is a character of.
    static readonly (string Text, string Font)[] bullets =
    [
        ("", "Symbol"),
        ("o", "Courier New"),
        ("", "Wingdings")
    ];

    static readonly (NumberFormatValues Format, bool Right)[] numbers =
    [
        (NumberFormatValues.Decimal, false),
        (NumberFormatValues.LowerLetter, false),
        (NumberFormatValues.LowerRoman, true)
    ];

    /// <summary>
    /// Home > Bullets' and Numbering's libraries, in the order Word shows them. Word-probed 2026-10-23
    /// (each gallery template applied over COM, numbering.xml read back): an item differs from the
    /// first only in its first level — its format, text, justification, hanging indent and bullet
    /// font — and every deeper level is the same o, square and disc, or a., i. and 1. The library's
    /// fourth bullet, a picture, is left out.
    /// </summary>
    public static IReadOnlyList<ListStyle> Bullets { get; } =
    [
        new("Disc", ListKind.Bullet, NumberFormatValues.Bullet, "", "Symbol", false, 360, "●"),
        new("Circle", ListKind.Bullet, NumberFormatValues.Bullet, "o", "Courier New", false, 360, "○") {ComplexScript = true},
        new("Square", ListKind.Bullet, NumberFormatValues.Bullet, "", "Wingdings", false, 360, "■"),
        new("Diamonds", ListKind.Bullet, NumberFormatValues.Bullet, "", "Wingdings", false, 360, "❖"),
        new("Arrow", ListKind.Bullet, NumberFormatValues.Bullet, "", "Wingdings", false, 360, "➢"),
        new("Check mark", ListKind.Bullet, NumberFormatValues.Bullet, "", "Wingdings", false, 360, "✓")
    ];

    /// <inheritdoc cref="Bullets"/>
    public static IReadOnlyList<ListStyle> Numbers { get; } =
    [
        new("Numbers", ListKind.Number, NumberFormatValues.Decimal, "%1.", null, false, 360, "1. 2. 3."),
        new("Numbers with parentheses", ListKind.Number, NumberFormatValues.Decimal, "%1)", null, false, 360, "1) 2) 3)"),
        new("Upper-case roman", ListKind.Number, NumberFormatValues.UpperRoman, "%1.", null, true, 180, "I. II. III."),
        new("Upper-case letters", ListKind.Number, NumberFormatValues.UpperLetter, "%1.", null, false, 360, "A. B. C."),
        new("Letters with parentheses", ListKind.Number, NumberFormatValues.LowerLetter, "%1)", null, false, 360, "a) b) c)"),
        new("Lower-case letters", ListKind.Number, NumberFormatValues.LowerLetter, "%1.", null, false, 360, "a. b. c."),
        new("Lower-case roman", ListKind.Number, NumberFormatValues.LowerRoman, "%1.", null, true, 360, "i. ii. iii.")
    ];

    /// <summary>
    /// A list's definition, numbered as the document's next and identified by <paramref name="nsid"/>;
    /// its first level <paramref name="first"/>'s, where one is given.
    /// </summary>
    public static AbstractNum Create(ListKind kind, int id, int nsid, ListStyle? first)
    {
        var definition = Create(kind, id, nsid);
        if (first != null)
        {
            Restyle(definition, first);
        }

        return definition;
    }

    /// <summary>Gives a list's first level a library item's look; its deeper levels stay as they are.</summary>
    public static void Restyle(AbstractNum definition, ListStyle style)
    {
        var level = new Level
        {
            LevelIndex = 0
        };
        level.AppendChild(new StartNumberingValue
        {
            Val = 1
        });
        level.AppendChild(new NumberingFormat
        {
            Val = style.Format
        });
        level.AppendChild(new LevelText
        {
            Val = style.Text
        });
        level.AppendChild(new LevelJustification
        {
            Val = style.Right ? LevelJustificationValues.Right : LevelJustificationValues.Left
        });
        level.AppendChild(new PreviousParagraphProperties(new Indentation
        {
            Left = "720",
            Hanging = style.Hanging.ToString(CultureInfo.InvariantCulture)
        }));
        if (style.Font is { } font)
        {
            level.AppendChild(new NumberingSymbolRunProperties(new RunFonts
            {
                Ascii = font,
                HighAnsi = font,
                ComplexScript = style.ComplexScript ? font : null,
                Hint = FontTypeHintValues.Default
            }));
        }

        if (definition.Elements<Level>().FirstOrDefault(_ => _.LevelIndex?.Value == 0) is { } old)
        {
            old.InsertBeforeSelf(level);
            old.Remove();
        }
        else
        {
            definition.AppendChild(level);
        }
    }

    /// <summary>Whether a list's first level has a library item's look.</summary>
    public static bool Looks(AbstractNum? definition, ListStyle style)
    {
        var level = definition?.Elements<Level>().FirstOrDefault(_ => _.LevelIndex?.Value == 0);
        return level?.NumberingFormat?.Val?.Value == style.Format &&
               level.LevelText?.Val?.Value == style.Text &&
               (style.Font == null || level.NumberingSymbolRunProperties?.RunFonts?.Ascii?.Value == style.Font);
    }

    /// <summary>A list's definition, numbered as the document's next and identified by <paramref name="nsid"/>.</summary>
    public static AbstractNum Create(ListKind kind, int id, int nsid)
    {
        var definition = new AbstractNum
        {
            AbstractNumberId = id
        };
        definition.AppendChild(new Nsid
        {
            Val = nsid.ToString("X8", CultureInfo.InvariantCulture)
        });
        definition.AppendChild(new MultiLevelType
        {
            Val = MultiLevelValues.HybridMultilevel
        });
        for (var level = 0; level < 9; level++)
        {
            definition.AppendChild(Level(kind, level));
        }

        return definition;
    }

    static Level Level(ListKind kind, int level)
    {
        var left = 720 * (level + 1);
        var element = new Level
        {
            LevelIndex = level
        };
        element.AppendChild(new StartNumberingValue
        {
            Val = 1
        });
        var indentation = new Indentation
        {
            Left = left.ToString(CultureInfo.InvariantCulture),
            Hanging = "360"
        };
        if (kind == ListKind.Bullet)
        {
            var (text, font) = bullets[level % bullets.Length];
            element.AppendChild(new NumberingFormat
            {
                Val = NumberFormatValues.Bullet
            });
            element.AppendChild(new LevelText
            {
                Val = text
            });
            element.AppendChild(new LevelJustification
            {
                Val = LevelJustificationValues.Left
            });
            element.AppendChild(new PreviousParagraphProperties(indentation));
            element.AppendChild(new NumberingSymbolRunProperties(new RunFonts
            {
                Ascii = font,
                HighAnsi = font,
                Hint = FontTypeHintValues.Default
            }));
            return element;
        }

        var (format, right) = numbers[level % numbers.Length];
        element.AppendChild(new NumberingFormat
        {
            Val = format
        });
        element.AppendChild(new LevelText
        {
            Val = $"%{level + 1}."
        });
        element.AppendChild(new LevelJustification
        {
            Val = right ? LevelJustificationValues.Right : LevelJustificationValues.Left
        });

        // A roman numeral hangs from its right edge, the narrower distance from the text.
        if (right)
        {
            indentation.Hanging = "180";
        }

        element.AppendChild(new PreviousParagraphProperties(indentation));
        return element;
    }
}
