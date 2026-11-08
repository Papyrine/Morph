/// <summary>
/// How the reader's browser spells a date (<c>readerDates</c> in morph-viewer.js): its short date, long
/// date, year and month and short time as .NET custom formats, and its month and day names, January
/// and Sunday first. A WebAssembly app published with <c>InvariantGlobalization</c>, as Morph.Web is,
/// has no culture data of its own, and the browser is the one place that knows the reader's.
/// </summary>
sealed record ReaderDates(
    string ShortDate,
    string LongDate,
    string YearMonth,
    string ShortTime,
    string[] Months,
    string[] ShortMonths,
    string[] Days,
    string[] ShortDays)
{
    /// <summary>
    /// A culture that spells dates as the browser does: the invariant culture's, with these patterns
    /// and names. Its name stays the invariant's, so it is told apart by reference, not by name.
    /// Null when the browser handed back something short.
    /// </summary>
    public CultureInfo? ToCulture()
    {
        if (Months is not { Length: 12 } ||
            ShortMonths is not { Length: 12 } ||
            Days is not { Length: 7 } ||
            ShortDays is not { Length: 7 })
        {
            return null;
        }

        var culture = (CultureInfo) CultureInfo.InvariantCulture.Clone();
        var formats = culture.DateTimeFormat;
        // The month lists take a thirteenth month, empty in a twelve-month calendar.
        formats.MonthNames = [.. Months, ""];
        formats.MonthGenitiveNames = [.. Months, ""];
        formats.AbbreviatedMonthNames = [.. ShortMonths, ""];
        formats.AbbreviatedMonthGenitiveNames = [.. ShortMonths, ""];
        formats.DayNames = Days;
        formats.AbbreviatedDayNames = ShortDays;
        // The patterns write their separators in as themselves, so the culture's stay / and :.
        Set(ShortDate, _ => formats.ShortDatePattern = _);
        Set(LongDate, _ => formats.LongDatePattern = _);
        Set(YearMonth, _ => formats.YearMonthPattern = _);
        Set(ShortTime, _ => formats.ShortTimePattern = _);
        return culture;

        static void Set(string pattern, Action<string> set)
        {
            if (pattern.Length > 0)
            {
                set(pattern);
            }
        }
    }
}
