/// <summary>
/// How a bold run is emboldened when the face it resolved is not a bold one. The layout engine decides
/// it from the face (<see cref="BoldSimulation.For"/>) and the painters draw it.
/// </summary>
enum SyntheticBold
{
    /// <summary>The run is not bold, or its face is.</summary>
    None,

    /// <summary>
    /// Word's bold simulation: the family has no bold face, so the regular one is drawn with thicker
    /// strokes and each glyph a layout pixel wider.
    /// </summary>
    Simulated,

    /// <summary>
    /// The face is a variable font, whose bold Word draws as a real instance of its weight axis, at the
    /// regular instance's advances. No painter can instance the axis, so the regular outlines are
    /// thickened to stand in for it.
    /// </summary>
    VariableInstance
}
