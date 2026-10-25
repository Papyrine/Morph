// The undo list's entry names, most recent first. Word's legacy command bars still carry the Undo
// dropdown (control id 128), whose list is the stack with Word's own names ("Typing", "Clear", "Bold").
// If that control answers nothing in this Word, the depth is counted by undoing and redoing instead.
static class UndoStack
{
    static bool warned;

    public static (List<string> Names, string Source) Read(ProbeDocument probe)
    {
        // The control answers reliably, but a single read can fail while Word is still settling; the
        // fallback counts by undoing and redoing, and the next observation tries the control again.
        try
        {
            var names = ReadControl(probe.Document.Application);
            if (names != null)
            {
                return (names, "control128");
            }
        }
        catch (Exception exception)
        {
            var inner = exception is TargetInvocationException
            {
                InnerException: { } wrapped
            } ? wrapped : exception;
            Log.Detail($"Undo control unavailable this time: {inner.GetType().Name}: {inner.Message}");
        }

        if (!warned)
        {
            warned = true;
            Log.Warn("Undo entry names were unavailable from the command bar control for a case; its depth was counted instead.");
        }

        return (CountByUndoing(probe.Document), "undo-redo-count");
    }

    static List<string>? ReadControl(Word.Application application)
    {
        var bars = ((dynamic) application).CommandBars;
        // Word 365 (smoke-tested 2026-10-25): the visible QAT control with id 128 is a plain button (type 1); the
        // hidden one on the legacy Standard bar is the split dropdown (type 6) that carries the list.
        var strategies = new (string Name, Func<object?> Find)[]
        {
            ("FindControl(Id: 128, Visible: false)", () => bars.FindControl(Id: 128, Visible: false)),
            ("FindControl(Id: 128)", () => bars.FindControl(Id: 128)),
            ("FindControl(Type: 6, Id: 128)", () => bars.FindControl(Type: 6, Id: 128))
        };
        foreach (var (name, find) in strategies)
        {
            try
            {
                dynamic? control = find();
                if (control == null)
                {
                    Log.Detail($"Undo control: {name} found nothing");
                    continue;
                }

                int type = control.Type;
                try
                {
                    int count = control.ListCount;
                    var names = new List<string>();
                    for (var i = 1; i <= count; i++)
                    {
                        string entry = control.List[i];
                        names.Add(entry);
                    }

                    Log.Detail($"Undo control: {name} is type {type} with {count} entries");
                    return names;
                }
                catch (Exception exception)
                {
                    Log.Detail($"Undo control: {name} is type {type} without a list ({exception.GetType().Name})");
                }
            }
            catch (Exception exception)
            {
                var inner = exception is TargetInvocationException
                {
                    InnerException: { } wrapped
                } ? wrapped : exception;
                Log.Detail($"Undo control: {name} failed: {inner.GetType().Name}: {inner.Message}");
            }
        }

        return null;
    }

    static List<string> CountByUndoing(Word.Document document)
    {
        var depth = 0;
        while (depth < 200 &&
               document.Undo())
        {
            depth++;
        }

        if (depth > 0)
        {
            document.Redo(depth);
        }

        return Enumerable.Repeat("?", depth).ToList();
    }

    public static void Reset() =>
        warned = false;
}
