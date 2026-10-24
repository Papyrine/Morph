namespace Morph;

/// <summary>
/// A lock operation's answer: its status, and the lock the file has when it is not the one asked for.
/// </summary>
readonly record struct WopiReply(int Status, string? Lock, string? Reason)
{
    public bool Succeeded => Status == 200;

    /// <summary>A 409 with no lock: the file is not locked at all, as after a lock expired.</summary>
    public bool Unlocked =>
        Status == 409 &&
        string.IsNullOrEmpty(Lock);
}