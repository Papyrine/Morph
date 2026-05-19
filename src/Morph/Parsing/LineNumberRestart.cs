/// <summary>
/// Specifies when line numbering should restart.
/// </summary>
enum LineNumberRestart
{
    /// <summary>Line numbers restart at the beginning of each page.</summary>
    NewPage,

    /// <summary>Line numbers restart at the beginning of each section.</summary>
    NewSection,

    /// <summary>Line numbers are continuous throughout the document.</summary>
    Continuous
}