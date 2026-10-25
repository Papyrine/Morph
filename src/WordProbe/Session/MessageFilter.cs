// Word rejects COM calls (RPC_E_SERVERCALL_RETRYLATER) while a modal dialog is up or its UI thread is
// mid-command. A registered message filter is what turns that into a retry with a deadline: the STA
// thread retries for a while, asks the dialog watchdog to dismiss whatever is up, then gives up and the
// caller sees a COMException.
[ComImport]
[Guid("00000016-0000-0000-C000-000000000046")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IMessageFilter
{
    [PreserveSig]
    int HandleInComingCall(int callType, IntPtr taskCaller, int tickCount, IntPtr interfaceInfo);

    [PreserveSig]
    int RetryRejectedCall(IntPtr taskCallee, int tickCount, int rejectType);

    [PreserveSig]
    int MessagePending(IntPtr taskCallee, int tickCount, int pendingType);
}

sealed class MessageFilter : IMessageFilter, IDisposable
{
    const int serverCallIsHandled = 0;
    const int serverCallRetryLater = 2;
    const int pendingMessageWaitDefaultProcess = 2;

    readonly DialogWatchdog? watchdog;
    readonly int giveUpAfterMilliseconds;
    readonly int dismissAfterMilliseconds;
    readonly Func<bool> dismissAllowed;
    IMessageFilter? previous;

    MessageFilter(DialogWatchdog? watchdog, TimeSpan giveUpAfter, TimeSpan dismissAfter, Func<bool> dismissAllowed)
    {
        this.watchdog = watchdog;
        giveUpAfterMilliseconds = (int) giveUpAfter.TotalMilliseconds;
        dismissAfterMilliseconds = (int) dismissAfter.TotalMilliseconds;
        this.dismissAllowed = dismissAllowed;
    }

    [DllImport("ole32.dll")]
    static extern int CoRegisterMessageFilter(IMessageFilter? newFilter, out IMessageFilter? oldFilter);

    public static MessageFilter Register(DialogWatchdog? watchdog, TimeSpan giveUpAfter, TimeSpan dismissAfter, Func<bool>? dismissAllowed = null)
    {
        var filter = new MessageFilter(watchdog, giveUpAfter, dismissAfter, dismissAllowed ?? (() => true));
        var result = CoRegisterMessageFilter(filter, out filter.previous);
        if (result != 0)
        {
            throw new InvalidOperationException($"CoRegisterMessageFilter failed with 0x{result:X8}; Main must run on an STA thread.");
        }

        return filter;
    }

    public int HandleInComingCall(int callType, IntPtr taskCaller, int tickCount, IntPtr interfaceInfo) =>
        serverCallIsHandled;

    public int RetryRejectedCall(IntPtr taskCallee, int tickCount, int rejectType)
    {
        if (rejectType != serverCallRetryLater ||
            tickCount >= giveUpAfterMilliseconds)
        {
            return -1;
        }

        if (tickCount >= dismissAfterMilliseconds &&
            dismissAllowed())
        {
            watchdog?.TryDismiss();
        }

        return 100;
    }

    public int MessagePending(IntPtr taskCallee, int tickCount, int pendingType) =>
        pendingMessageWaitDefaultProcess;

    public void Dispose()
    {
        CoRegisterMessageFilter(previous, out _);
        previous = null;
    }
}
