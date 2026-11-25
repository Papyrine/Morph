namespace Morph;

/// <summary>
/// A lock operation's answer: its status, and the lock the file has when it is not the one asked for.
/// </summary>
/// <param name="Status">The HTTP status the host answered: 200 for done.</param>
/// <param name="Lock">The lock the file has, on a 409 (X-WOPI-Lock); none when it has no lock at all.</param>
/// <param name="Reason">Why the host refused, on a 409 (X-WOPI-LockFailureReason), which the reader is shown.</param>
public readonly record struct WopiReply(int Status, string? Lock, string? Reason)
{
    /// <summary>Whether the host did it.</summary>
    public bool Succeeded => Status == 200;

    /// <summary>A 409 with no lock: the file is not locked at all, as after a lock expired.</summary>
    public bool Unlocked =>
        Status == 409 &&
        string.IsNullOrEmpty(Lock);
}