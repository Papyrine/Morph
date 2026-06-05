/// <summary>
/// Mirror of the <c>w:cnfStyle</c> flag set used to mark which conditional
/// regions of a table style apply to a row, cell, or paragraph. Word stores
/// these as a 12-bit string and as named attributes; we read the named
/// attributes since they're authoritative when present.
/// </summary>
[Flags]
enum ConditionalFormatFlags
{
    None = 0,
    FirstRow = 1 << 0,
    LastRow = 1 << 1,
    FirstColumn = 1 << 2,
    LastColumn = 1 << 3,
    OddVBand = 1 << 4,
    EvenVBand = 1 << 5,
    OddHBand = 1 << 6,
    EvenHBand = 1 << 7,
    FirstRowFirstColumn = 1 << 8,
    FirstRowLastColumn = 1 << 9,
    LastRowFirstColumn = 1 << 10,
    LastRowLastColumn = 1 << 11,
}
