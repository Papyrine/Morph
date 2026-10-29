/// <summary>
/// What a <c>w:sym</c> character is, for when its symbol font is not there to draw it.
///
/// A symbol font keys its glyphs by a single byte, which the font's Windows Symbol <c>cmap</c> (3,0)
/// places in the F000–F0FF private-use block: Wingdings' ☑ is <c>0xFE</c>, stored as <c>F0FE</c>. That
/// code means nothing outside the font, so where the font is missing the character is drawn as its
/// Unicode equivalent instead — Wingdings' and Symbol's own encodings, read off the faces Windows ships
/// (2026-10-29: each glyph rendered through the symbol <c>cmap</c> and matched by eye to its codepoint).
/// Only those two are mapped; Wingdings 2 and 3 and Webdings encode the same bytes differently.
/// </summary>
static class SymbolFonts
{
    /// <summary>The embedded face a mapped symbol is drawn from when its own font is missing.</summary>
    public const string Fallback = "Morph Bullets";

    static readonly Dictionary<int, char> wingdings = new()
    {
        [0x4A] = '☺',
        [0x4C] = '☹',
        [0x6C] = '●',
        [0x6E] = '■',
        // A white square in the font, and how Word's symbol dialog offers an empty checkbox — so
        // the ballot box (☐) rather than U+25A1, which a checkbox reader would not take for one.
        [0x6F] = '☐',
        [0x71] = '❑',
        [0x72] = '❒',
        [0x75] = '◆',
        [0x76] = '❖',
        [0x77] = '⬥',
        [0x78] = '☒',
        [0x9F] = '•',
        [0xA1] = '○',
        [0xA7] = '▪',
        [0xA8] = '☐',
        [0xD8] = '➢',
        [0xE8] = '➔',
        [0xF0] = '⇨',
        [0xFB] = '✗',
        [0xFC] = '✓',
        [0xFD] = '☒',
        [0xFE] = '☑'
    };

    // Symbol's encoding. Its printable ASCII positions not listed here are ASCII itself.
    static readonly Dictionary<int, char> symbol = new()
    {
        [0x22] = '∀',
        [0x24] = '∃',
        [0x27] = '∋',
        [0x2A] = '∗',
        [0x2D] = '−',
        [0x40] = '≅',
        [0x41] = 'Α',
        [0x42] = 'Β',
        [0x43] = 'Χ',
        [0x44] = 'Δ',
        [0x45] = 'Ε',
        [0x46] = 'Φ',
        [0x47] = 'Γ',
        [0x48] = 'Η',
        [0x49] = 'Ι',
        [0x4A] = 'ϑ',
        [0x4B] = 'Κ',
        [0x4C] = 'Λ',
        [0x4D] = 'Μ',
        [0x4E] = 'Ν',
        [0x4F] = 'Ο',
        [0x50] = 'Π',
        [0x51] = 'Θ',
        [0x52] = 'Ρ',
        [0x53] = 'Σ',
        [0x54] = 'Τ',
        [0x55] = 'Υ',
        [0x56] = 'ς',
        [0x57] = 'Ω',
        [0x58] = 'Ξ',
        [0x59] = 'Ψ',
        [0x5A] = 'Ζ',
        [0x5C] = '∴',
        [0x5E] = '⊥',
        [0x61] = 'α',
        [0x62] = 'β',
        [0x63] = 'χ',
        [0x64] = 'δ',
        [0x65] = 'ε',
        [0x66] = 'φ',
        [0x67] = 'γ',
        [0x68] = 'η',
        [0x69] = 'ι',
        [0x6A] = 'ϕ',
        [0x6B] = 'κ',
        [0x6C] = 'λ',
        [0x6D] = 'μ',
        [0x6E] = 'ν',
        [0x6F] = 'ο',
        [0x70] = 'π',
        [0x71] = 'θ',
        [0x72] = 'ρ',
        [0x73] = 'σ',
        [0x74] = 'τ',
        [0x75] = 'υ',
        [0x76] = 'ϖ',
        [0x77] = 'ω',
        [0x78] = 'ξ',
        [0x79] = 'ψ',
        [0x7A] = 'ζ',
        [0x7E] = '∼',
        [0xA1] = 'ϒ',
        [0xA2] = '′',
        [0xA3] = '≤',
        [0xA4] = '⁄',
        [0xA5] = '∞',
        [0xA6] = 'ƒ',
        [0xA7] = '♣',
        [0xA8] = '♦',
        [0xA9] = '♥',
        [0xAA] = '♠',
        [0xAB] = '↔',
        [0xAC] = '←',
        [0xAD] = '↑',
        [0xAE] = '→',
        [0xAF] = '↓',
        [0xB0] = '°',
        [0xB1] = '±',
        [0xB2] = '″',
        [0xB3] = '≥',
        [0xB4] = '×',
        [0xB5] = '∝',
        [0xB6] = '∂',
        [0xB7] = '•',
        [0xB8] = '÷',
        [0xB9] = '≠',
        [0xBA] = '≡',
        [0xBB] = '≈',
        [0xBC] = '…',
        [0xC0] = 'ℵ',
        [0xC1] = 'ℑ',
        [0xC2] = 'ℜ',
        [0xC3] = '℘',
        [0xC4] = '⊗',
        [0xC5] = '⊕',
        [0xC6] = '∅',
        [0xC7] = '∩',
        [0xC8] = '∪',
        [0xC9] = '⊃',
        [0xCA] = '⊇',
        [0xCB] = '⊄',
        [0xCC] = '⊂',
        [0xCD] = '⊆',
        [0xCE] = '∈',
        [0xCF] = '∉',
        [0xD0] = '∠',
        [0xD1] = '∇',
        [0xD2] = '®',
        [0xD3] = '©',
        [0xD4] = '™',
        [0xD5] = '∏',
        [0xD6] = '√',
        [0xD7] = '⋅',
        [0xD8] = '¬',
        [0xD9] = '∧',
        [0xDA] = '∨',
        [0xDB] = '⇔',
        [0xDC] = '⇐',
        [0xDD] = '⇑',
        [0xDE] = '⇒',
        [0xDF] = '⇓',
        [0xE0] = '◊',
        [0xE1] = '〈',
        [0xE5] = '∑',
        [0xF1] = '〉',
        [0xF2] = '∫'
    };

    /// <summary>
    /// Whether <paramref name="font"/> is one whose characters are glyph slots rather than text, so its
    /// low byte read as Latin-1 would draw the wrong thing.
    /// </summary>
    public static bool IsSymbolFont(string font) =>
        font.Equals("Symbol", StringComparison.OrdinalIgnoreCase) ||
        font.StartsWith("Wingdings", StringComparison.OrdinalIgnoreCase) ||
        font.Equals("Webdings", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The code a symbol font's (3,0) <c>cmap</c> keys <paramref name="code"/> under: a single byte is
    /// shifted into the F000 block, anything else is itself.
    /// </summary>
    public static char FontCode(char code)
    {
        if (code < 0x100)
        {
            return (char) (code + 0xF000);
        }

        return code;
    }

    /// <summary>The byte a code in the F000–F0FF block stands for; any other code is itself.</summary>
    public static char LowByte(char code)
    {
        if (code is >= '' and <= '')
        {
            return (char) (code - 0xF000);
        }

        return code;
    }

    /// <summary>
    /// The Unicode character <paramref name="code"/> draws as in <paramref name="font"/>, or null when the
    /// font is not one mapped here or the code is not one of its mapped characters.
    /// </summary>
    public static char? ToUnicode(string font, char code)
    {
        var slot = LowByte(code);
        if (font.Equals("Wingdings", StringComparison.OrdinalIgnoreCase))
        {
            if (wingdings.TryGetValue(slot, out var glyph))
            {
                return glyph;
            }

            return null;
        }

        if (font.Equals("Symbol", StringComparison.OrdinalIgnoreCase))
        {
            if (symbol.TryGetValue(slot, out var glyph))
            {
                return glyph;
            }

            if (slot is >= ' ' and <= '~')
            {
                return slot;
            }
        }

        return null;
    }

    /// <summary>
    /// The text a <c>w:sym</c> carries in the model: its Unicode equivalent where one is known, the
    /// character itself in the font's own block for a symbol font that has none, and for any other font
    /// the character the code names (<c>F041</c> in Arial is "A").
    /// </summary>
    public static string Text(string font, char code)
    {
        if (ToUnicode(font, code) is { } unicode)
        {
            return unicode.ToString();
        }

        if (IsSymbolFont(font))
        {
            return FontCode(code).ToString();
        }

        return LowByte(code).ToString();
    }
}
