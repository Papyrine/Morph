/// <summary>
/// A <c>w:sym</c>: the character <see cref="Code"/> (<c>w:char</c>) drawn from <see cref="Font"/>
/// (<c>w:font</c>). A symbol font such as Wingdings keys its glyphs in the F000–F0FF private-use block
/// (the <c>cmap</c>'s Windows Symbol (3,0) subtable), and <c>w:char</c> names a glyph by that code — or
/// by its low byte, which stands for the same glyph. The run's <see cref="global::Run.Text"/> carries the
/// character's Unicode equivalent where <see cref="SymbolFonts.ToUnicode"/> knows one, so the text
/// exporters write something a reader without the font can read; layout draws from the font itself
/// when it is there (<c>CanonicalParagraphMeasurer.SymbolText</c>).
/// </summary>
sealed record RunSymbol(string Font, char Code);
