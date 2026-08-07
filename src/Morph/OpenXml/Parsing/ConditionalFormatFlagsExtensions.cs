static class ConditionalFormatFlagsExtensions
{
    public const ConditionalFormatFlags AllConditions =
        ConditionalFormatFlags.FirstRow |
        ConditionalFormatFlags.LastRow |
        ConditionalFormatFlags.FirstColumn |
        ConditionalFormatFlags.LastColumn |
        ConditionalFormatFlags.OddVBand |
        ConditionalFormatFlags.EvenVBand |
        ConditionalFormatFlags.OddHBand |
        ConditionalFormatFlags.EvenHBand |
        ConditionalFormatFlags.FirstRowFirstColumn |
        ConditionalFormatFlags.FirstRowLastColumn |
        ConditionalFormatFlags.LastRowFirstColumn |
        ConditionalFormatFlags.LastRowLastColumn;
}
