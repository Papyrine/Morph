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