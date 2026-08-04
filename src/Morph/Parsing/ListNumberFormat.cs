/// <summary>
/// The counter style of a numbered-list level (<c>w:numFmt</c>), retained so the exporters can
/// reproduce Word's roman/letter markers instead of collapsing every ordered list to decimal.
/// <see cref="Bullet"/> covers unordered levels; number formats Morph does not map explicitly
/// (ordinal text, hex, decimal-enclosed, …) arrive as <see cref="Decimal"/>.
/// </summary>
enum ListNumberFormat
{
    Bullet,
    Decimal,
    UpperRoman,
    LowerRoman,
    UpperLetter,
    LowerLetter,
}
