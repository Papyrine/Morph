/// <summary>
/// Text wrapping type for floating elements.
/// </summary>
enum WrapType
{
    /// <summary>No wrapping - image floats over/under text.</summary>
    None,
    /// <summary>Text wraps in a square around the image.</summary>
    Square,
    /// <summary>Text wraps tightly around the image outline.</summary>
    Tight,
    /// <summary>Text wraps through the image.</summary>
    Through,
    /// <summary>Text appears above and below but not beside.</summary>
    TopAndBottom
}

/// <summary>
/// Which side(s) of a wrapped float the text may flow on (wp @wrapText).
/// </summary>
enum WrapTextSide
{
    /// <summary>Text flows on both sides of the float.</summary>
    BothSides,
    /// <summary>Text flows only on the left side of the float.</summary>
    Left,
    /// <summary>Text flows only on the right side of the float.</summary>
    Right,
    /// <summary>Text flows on whichever side is wider.</summary>
    Largest
}