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
