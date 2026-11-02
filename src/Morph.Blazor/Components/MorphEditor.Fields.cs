namespace Morph;

// Insert > Page Number and Date and Time, and References > Table of Contents and Update Table.
public partial class MorphEditor
{
    const string pageNumberMenu = "page-number";
    const string dateDialog = "date";

    // Word's dialog opens with Update automatically off, on the format chosen last.
    string? dateFormat;
    bool dateUpdates;

    // The date each format is shown with: the reader's, when the dialog opened.
    DateTime dateShown;

    // The formats the dialog offers, in the reader's culture: its own short and long dates first, as
    // Word lists the language's, then the rest of Word's list. A runtime with no culture data (the
    // invariant culture, as a WebAssembly app built with InvariantGlobalization has) offers Word's
    // list alone.
    internal static IReadOnlyList<string> DateChoices => DateFormatsFor(CultureInfo.CurrentCulture);

    internal static IReadOnlyList<string> DateFormatsFor(CultureInfo culture)
    {
        if (culture.Name.Length == 0)
        {
            return DateFormats;
        }

        var formats = culture.DateTimeFormat;
        string[] own =
        [
            Spelt(formats.ShortDatePattern),
            Spelt(formats.LongDatePattern),
            Spelt(formats.YearMonthPattern),
            Spelt(formats.ShortTimePattern)
        ];
        return
        [
            .. own
                .Where(Writable)
                .Concat(DateFormats)
                .Distinct(StringComparer.Ordinal)
        ];

        // The culture's separators written in, since Word's switch takes a / or a : as itself.
        string Spelt(string pattern) =>
            pattern
                .Replace("/", formats.DateSeparator, StringComparison.Ordinal)
                .Replace(":", formats.TimeSeparator, StringComparison.Ordinal);

        // What Word's \@ switch spells as .NET does: not an AM/PM designator, an era or a time zone.
        static bool Writable(string pattern) =>
            pattern.Length > 0 &&
            pattern.IndexOfAny(['t', 'g', 'z', 'K', 'f', 'F']) < 0;
    }

    string ChosenDate
    {
        get
        {
            var choices = DateChoices;
            if (dateFormat is { } chosen &&
                choices.Contains(chosen))
            {
                return chosen;
            }

            return choices[Math.Min(2, choices.Count - 1)];
        }
    }

    async Task OpenDateAsync()
    {
        dateShown = await ReaderDateAsync();
        dateFormat = ChosenDate;
        dialog = dateDialog;
    }

    string DateLabel(string format) =>
        dateShown.ToString(DocumentEditor.Literal(format), CultureInfo.CurrentCulture);

    Task DateFromDialogAsync()
    {
        dialog = null;
        return InsertDateAsync(ChosenDate, dateUpdates);
    }

    async Task OnDateKeyDownAsync(KeyboardEventArgs args)
    {
        if (args.Key == "Enter")
        {
            await DateFromDialogAsync();
            return;
        }

        OnDialogKeyDown(args);
    }
}
