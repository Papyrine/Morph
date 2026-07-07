/// <summary>
/// Resolves the text drawn for a date content control (<c>w:sdt</c> with <c>w:date</c>).
///
/// Word displays the run text held inside the control verbatim, so that text is preferred —
/// this both matches Word and keeps the render independent of the host machine's culture. Only
/// when the control has no run text (it carries a <c>w:fullDate</c> but empty content) is the
/// date formatted, and that always uses the declared <c>w:dateFormat</c> under the invariant
/// culture. It never uses <see cref="CultureInfo.CurrentCulture"/> (as <c>ToShortDateString</c>
/// would), which would make page output depend on the machine running the render.
/// </summary>
static class DateControlText
{
    const string fallbackFormat = "yyyy-MM-dd";

    public static string Resolve(ContentControlElement control)
    {
        if (!string.IsNullOrEmpty(control.Content))
        {
            return control.Content;
        }

        if (control.DateValue is { } date)
        {
            var format = string.IsNullOrEmpty(control.DateFormat) ? fallbackFormat : control.DateFormat!;
            try
            {
                return date.ToString(format, CultureInfo.InvariantCulture);
            }
            catch (FormatException)
            {
                // Word's w:dateFormat strings are not guaranteed to be valid .NET custom format
                // specifiers; fall back to a safe invariant format rather than throwing.
                return date.ToString(fallbackFormat, CultureInfo.InvariantCulture);
            }
        }

        return control.PlaceholderText ?? "";
    }
}
