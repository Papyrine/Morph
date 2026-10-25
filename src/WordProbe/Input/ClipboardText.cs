// The user's clipboard text is kept across the run and put back at the end; a case that needs the
// clipboard sets it explicitly.
static class ClipboardText
{
    public static string? Read()
    {
        if (!NativeMethods.IsClipboardFormatAvailable(NativeMethods.ClipboardUnicodeText) ||
            !NativeMethods.OpenClipboard(IntPtr.Zero))
        {
            return null;
        }

        try
        {
            var handle = NativeMethods.GetClipboardData(NativeMethods.ClipboardUnicodeText);
            if (handle == IntPtr.Zero)
            {
                return null;
            }

            var pointer = NativeMethods.GlobalLock(handle);
            try
            {
                return Marshal.PtrToStringUni(pointer);
            }
            finally
            {
                NativeMethods.GlobalUnlock(handle);
            }
        }
        finally
        {
            NativeMethods.CloseClipboard();
        }
    }

    public static void Write(string text)
    {
        if (!NativeMethods.OpenClipboard(IntPtr.Zero))
        {
            Thread.Sleep(100);
            if (!NativeMethods.OpenClipboard(IntPtr.Zero))
            {
                throw new InvalidOperationException("The clipboard could not be opened.");
            }
        }

        try
        {
            NativeMethods.EmptyClipboard();
            var bytes = (text.Length + 1) * 2;
            var handle = NativeMethods.GlobalAlloc(NativeMethods.GlobalMoveable, (UIntPtr) bytes);
            var pointer = NativeMethods.GlobalLock(handle);
            try
            {
                var buffer = Encoding.Unicode.GetBytes(text + "\0");
                Marshal.Copy(buffer, 0, pointer, buffer.Length);
            }
            finally
            {
                NativeMethods.GlobalUnlock(handle);
            }

            NativeMethods.SetClipboardData(NativeMethods.ClipboardUnicodeText, handle);
        }
        finally
        {
            NativeMethods.CloseClipboard();
        }
    }

    public static void Clear()
    {
        if (NativeMethods.OpenClipboard(IntPtr.Zero))
        {
            NativeMethods.EmptyClipboard();
            NativeMethods.CloseClipboard();
        }
    }
}
