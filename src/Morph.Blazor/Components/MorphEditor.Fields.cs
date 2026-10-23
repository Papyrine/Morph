namespace Morph;

// Insert > Page Number and Date and Time, and References > Table of Contents and Update Table.
public partial class MorphEditor
{
    const string pageNumberMenu = "page-number";
    const string dateDialog = "date";

    string dateFormat = DateFormats[2];
    bool dateUpdates = true;

    // The date each format is shown with: the reader's, when the dialog opened.
    DateTime dateShown;

    async Task OpenDateAsync()
    {
        dateShown = await ReaderDateAsync();
        dialog = dateDialog;
    }

    string DateLabel(string format) =>
        dateShown.ToString(format, CultureInfo.InvariantCulture);

    Task DateFromDialogAsync()
    {
        dialog = null;
        return InsertDateAsync(dateFormat, dateUpdates);
    }
}
