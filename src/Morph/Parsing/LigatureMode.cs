/// <summary>
/// OpenType ligature mode (w14:ligatures). Word 2010+ extension.
/// </summary>
[Flags]
enum LigatureMode
{
    None = 0,
    Standard = 1 << 0,
    Contextual = 1 << 1,
    Historical = 1 << 2,
    Discretional = 1 << 3,
    All = Standard | Contextual | Historical | Discretional
}
