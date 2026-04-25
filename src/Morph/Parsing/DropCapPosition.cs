/// <summary>
/// Position of a drop cap (w:framePr/w:dropCap). The first character of the paragraph
/// is rendered larger and either inset within the text body (Drop) or floated into the
/// margin (Margin).
/// </summary>
enum DropCapPosition
{
    None,
    Drop,
    Margin
}
