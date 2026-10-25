// SendInput returns before Word has processed anything. A step is settled when two COM reads 30 ms apart
// agree on the selection and the document length, after a minimum that gives AutoFormat and AutoCorrect
// (which run after the triggering character) their chance.
enum SettleProfile
{
    Key,
    Type,
    Mouse,
    Dialog
}

static class Settle
{
    static readonly Dictionary<SettleProfile, (int Minimum, int Cap)> timings = new()
    {
        [SettleProfile.Key] = (40, 500),
        [SettleProfile.Type] = (90, 1500),
        [SettleProfile.Mouse] = (70, 700),
        [SettleProfile.Dialog] = (200, 2000)
    };

    public static bool WaitFor(ProbeDocument probe, SettleProfile profile)
    {
        var (minimum, cap) = timings[profile];
        var watch = Stopwatch.StartNew();
        Thread.Sleep(minimum);
        var previous = Sample(probe);
        while (watch.ElapsedMilliseconds < cap)
        {
            Thread.Sleep(30);
            var current = Sample(probe);
            if (current != null &&
                current == previous)
            {
                return true;
            }

            previous = current;
        }

        return false;
    }

    static string? Sample(ProbeDocument probe)
    {
        try
        {
            var selection = probe.Selection;
            return $"{selection.Start}:{selection.End}:{(int) selection.Type}:{probe.Document.Content.End}";
        }
        catch (COMException)
        {
            return null;
        }
    }
}
