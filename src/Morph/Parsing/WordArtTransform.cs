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
    CanDown
}