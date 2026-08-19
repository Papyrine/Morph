/// <summary>
/// Run-level text properties.
/// </summary>
sealed record RunProperties
{
    public string FontFamily { get; init; } = DefaultFontSettings.DefaultFont;
    public double FontSizePoints { get; init; } = 11;
    public bool Bold { get; init; }
    public bool Italic { get; init; }
    public bool Underline { get; init; }

    /// <summary>
    /// Underline colour from <c>w:u/@w:color</c> (hex, no #). Null (or "auto" in the source)
    /// means the run's own text colour, which is Word's default.
    /// </summary>
    public string? UnderlineColorHex { get; init; }

    /// <summary>Whether <c>w:u/@w:val</c> is <c>double</c> — two thin rules instead of one.</summary>
    public bool DoubleUnderline { get; init; }

    /// <summary>
    /// Whether the run carries a tracked change (insertion or deletion). Word's markup view
    /// draws a vertical change bar in the left margin beside any line containing one.
    /// </summary>
    public bool IsRevisionMark { get; init; }
    public bool Strikethrough { get; init; }
    public bool AllCaps { get; init; }
    public bool SmallCaps { get; init; }
    // null = black
    public string? ColorHex { get; init; }

    /// <summary>
    /// Background/shading color for text (from w:shd element).
    /// </summary>
    public string? BackgroundColorHex { get; init; }

    /// <summary>
    /// Extra spacing between characters in points (from w:spacing in rPr).
    /// Positive values expand, negative values condense.
    /// </summary>
    public double CharacterSpacingPoints { get; init; }

    /// <summary>
    /// Vertical alignment for subscript/superscript text.
    /// </summary>
    public VerticalRunAlignment VerticalAlignment { get; init; } = VerticalRunAlignment.Baseline;

    /// <summary>
    /// Minimum font size in points at which Word applies pair kerning (w:kern).
    /// Zero means "no explicit threshold". The renderer relies on the platform shaper for
    /// the actual kerning values; this field is captured for downstream inspection.
    /// </summary>
    public double KerningMinFontSizePoints { get; init; }

    /// <summary>
    /// OpenType ligature mode (w14:ligatures). Word 2010+ extension. Renderer relies on the
    /// platform shaper for actual ligature substitution; this field is captured for inspection.
    /// </summary>
    public LigatureMode Ligatures { get; init; } = LigatureMode.Standard;

    /// <summary>
    /// Whether this run reads right-to-left (w:rtl). Renderer does not yet reverse text order.
    /// </summary>
    public bool IsRightToLeft { get; init; }

    /// <summary>Stroke around glyph outlines (w14:textOutline). Null = no outline.</summary>
    public TextOutline? Outline { get; init; }

    /// <summary>Drop-shadow behind text (w14:shadow). Null = no shadow.</summary>
    public TextShadow? Shadow { get; init; }

    /// <summary>Soft halo around text (w14:glow). Null = no glow.</summary>
    public TextGlow? Glow { get; init; }

    /// <summary>Mirrored reflection below text (w14:reflection). Presence-only — full
    /// parameter set (alpha gradient, distance, blur, skew) is not modelled.</summary>
    public bool HasReflection { get; init; }

    /// <summary>
    /// <c>w:vanish</c> / <c>w:specVanish</c> — when true, the run is hidden text and is
    /// skipped during layout and rendering. Word's <c>w:showHiddenText</c> setting could
    /// override this; Morph always honours hidden as the user-visible default.
    /// </summary>
    public bool Hidden { get; init; }

    /// <summary>
    /// Baseline shift in points (<c>w:position</c>; positive moves text up, negative down).
    /// Distinct from <see cref="VerticalAlignment"/>'s super/sub which also resizes.
    /// </summary>
    public double BaselineShiftPoints { get; init; }

    /// <summary>
    /// Per-run border (<c>w:bdr</c>). Drawn as a rectangle around the run's measured box.
    /// Null = no run border.
    /// </summary>
    public BorderEdge? Border { get; init; }

    /// <summary>
    /// <c>w:emboss</c> — 3D emboss effect (lighter glyph offset down-right of the main
    /// glyph). Approximated as a tonal effect; the renderer still uses the run's colour.
    /// </summary>
    public bool Emboss { get; init; }

    /// <summary>
    /// <c>w:imprint</c> — engrave (inverse emboss; darker glyph offset up-left).
    /// </summary>
    public bool Imprint { get; init; }

    /// <summary>
    /// <c>w:outline</c> — stroke-only text (no fill). When true, the renderer draws each
    /// glyph's outline using the run's colour rather than a filled fill.
    /// </summary>
    public bool OutlineOnly { get; init; }

    /// <summary>
    /// Bitmask view of which Word 2010+ text effects are present on the run, derived from
    /// <see cref="Outline"/>, <see cref="Shadow"/>, <see cref="Glow"/> and <see cref="HasReflection"/>.
    /// </summary>
    public TextEffects Effects =>
        (Shadow != null ? TextEffects.Shadow : TextEffects.None) |
        (Outline != null ? TextEffects.Outline : TextEffects.None) |
        (Glow != null ? TextEffects.Glow : TextEffects.None) |
        (HasReflection ? TextEffects.Reflection : TextEffects.None);
}