/// <summary>
/// WordArt text transform/warp presets.
/// </summary>
enum WordArtTransform
{
    /// <summary>No transform applied.</summary>
    None,

    /// <summary>Text follows an arc path upward.</summary>
    ArchUp,

    /// <summary>Text follows an arc path downward.</summary>
    ArchDown,

    /// <summary>Text arranged in a circle.</summary>
    Circle,

    /// <summary>Text with wave effect.</summary>
    Wave,

    /// <summary>Text with chevron pointing up.</summary>
    ChevronUp,

    /// <summary>Text with chevron pointing down.</summary>
    ChevronDown,

    /// <summary>Text slanted upward.</summary>
    SlantUp,

    /// <summary>Text slanted downward.</summary>
    SlantDown,

    /// <summary>Text in a triangle shape.</summary>
    Triangle,

    /// <summary>Text with fade effect to right.</summary>
    FadeRight,

    /// <summary>Text with fade effect to left.</summary>
    FadeLeft,

    /// <summary>Text inflated — top bulges up and bottom bulges down.</summary>
    Inflate,

    /// <summary>Text deflated — top dips down and bottom dips up (pinched centre).</summary>
    Deflate,

    /// <summary>Text in a "can" shape with the top bulged up and bottom flat.</summary>
    CanUp,

    /// <summary>Text in a "can" shape with the bottom bulged down and top flat.</summary>
    CanDown,

    /// <summary>Text stretched to fill its rect, with no bend (<c>textPlain</c>).</summary>
    Plain,

    /// <summary>Text in a stop-sign octagon: both edges cut in at the ends (<c>textStop</c>).</summary>
    Stop,

    /// <summary>Text in a triangle pointing down (<c>textTriangleInverted</c>).</summary>
    TriangleInverted,

    /// <summary>The wave mirrored: it dips first and rises second (<c>textWave2</c>).</summary>
    WaveInverted,

    /// <summary>Two waves end to end (<c>textDoubleWave1</c>).</summary>
    DoubleWave,

    /// <summary>Two mirrored waves end to end (<c>textWave4</c>).</summary>
    DoubleWaveInverted,

    /// <summary>Only the top edge bulges up (<c>textInflateTop</c>).</summary>
    InflateTop,

    /// <summary>Only the bottom edge bulges down (<c>textInflateBottom</c>).</summary>
    InflateBottom,

    /// <summary>Only the top edge dips (<c>textDeflateTop</c>).</summary>
    DeflateTop,

    /// <summary>Only the bottom edge rises (<c>textDeflateBottom</c>).</summary>
    DeflateBottom,

    /// <summary>Text narrowing toward its top edge (<c>textFadeUp</c>).</summary>
    FadeUp,

    /// <summary>Text narrowing toward its bottom edge (<c>textFadeDown</c>).</summary>
    FadeDown,

    /// <summary>Text curving up to the right (<c>textCurveUp</c>).</summary>
    CurveUp,

    /// <summary>Text curving down to the right (<c>textCurveDown</c>).</summary>
    CurveDown,

    /// <summary>Text rising to the right and growing shorter (<c>textCascadeUp</c>).</summary>
    CascadeUp,

    /// <summary>Text falling to the right and growing shorter (<c>textCascadeDown</c>).</summary>
    CascadeDown
}