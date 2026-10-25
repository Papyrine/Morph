// COM hygiene: releasing the RCWs the harness caches, and classifying the failures Word returns while
// it is busy or showing a dialog.
static class Com
{
    public const int CallRejected = unchecked((int) 0x80010001);
    public const int RetryLater = unchecked((int) 0x8001010A);
    public const int ServerDisconnected = unchecked((int) 0x80010108);
    public const int ServerNotAvailable = unchecked((int) 0x800706BA);

    public static void Release<T>(ref T? target)
        where T : class
    {
        if (target != null &&
            Marshal.IsComObject(target))
        {
            Marshal.FinalReleaseComObject(target);
        }

        target = null;
    }

    public static bool IsBusy(COMException exception) =>
        exception.HResult is CallRejected or RetryLater;

    public static bool IsGone(COMException exception) =>
        exception.HResult is ServerDisconnected or ServerNotAvailable;

    /// <summary>A COM read that may legitimately fail (a property that throws outside its context).</summary>
    public static T? Try<T>(Func<T> read)
    {
        try
        {
            return read();
        }
        catch (COMException)
        {
            return default;
        }
        catch (InvalidCastException)
        {
            return default;
        }
    }
}

sealed class ForegroundLostException(string message) : Exception(message);

sealed class WordBusyException(string message, Exception inner) : Exception(message, inner);

sealed class NotApplicableException(string message) : Exception(message);
