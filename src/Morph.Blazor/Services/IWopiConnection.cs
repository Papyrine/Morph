namespace Morph;

/// <summary>
/// The host <see cref="MorphWopi"/> opens a file from and saves it back to: the WOPI operations it asks
/// ([MS-WOPI] 3.3.5), answered as a WOPI host answers them. Without one, MorphWopi asks them of Morph.Wopi's
/// proxy, for the file the launch in its page names; a host the page can reach itself, as one whose own API
/// takes the user's cookies, passes one of its own as <see cref="MorphWopi.Connection"/>.
/// </summary>
public interface IWopiConnection
{
    /// <summary>The file's name, what the user may do with it, and the host's links (CheckFileInfo).</summary>
    /// <exception cref="HttpRequestException">
    /// The host refused or could not be reached; MorphWopi shows the exception's message in place of the file.
    /// </exception>
    Task<WopiFileInfo> CheckFileInfoAsync(Cancel cancel = default);

    /// <summary>The file's content (GetFile).</summary>
    /// <exception cref="HttpRequestException">
    /// The host refused or could not be reached; MorphWopi shows the exception's message in place of the file.
    /// </exception>
    Task<byte[]> GetFileAsync(Cancel cancel = default);

    /// <summary>
    /// Locks the file for the page (Lock), with <paramref name="id"/> as the lock. Anything but a 200 opens the
    /// file read only, and a 409's <see cref="WopiReply.Reason"/> says why over the editor.
    /// </summary>
    Task<WopiReply> LockAsync(string id, Cancel cancel = default);

    /// <summary>
    /// Keeps the lock (RefreshLock), every ten minutes while the file is open. A 409 with no lock says it has
    /// lapsed, and MorphWopi takes it again.
    /// </summary>
    Task<WopiReply> RefreshLockAsync(string id, Cancel cancel = default);

    /// <summary>Lets the lock go (Unlock), as the editor is disposed.</summary>
    Task<WopiReply> UnlockAsync(string id, Cancel cancel = default);

    /// <summary>
    /// Saves <paramref name="bytes"/> over the file, under the lock (PutFile). A 409 with no lock says it has
    /// lapsed: MorphWopi takes it again and saves once more.
    /// </summary>
    Task<WopiReply> PutFileAsync(byte[] bytes, string id, Cancel cancel = default);

    /// <summary>
    /// What lets the lock go as the page goes away, when there is no time left to ask .NET: a request the
    /// browser sends on <c>pagehide</c>, or null for none. Asked again each time the lock is kept, so a request
    /// that carries something that expires, as a request digest does, is current when it is sent.
    /// </summary>
    Task<WopiLeave?> LeaveAsync(string id, Cancel cancel = default);
}
