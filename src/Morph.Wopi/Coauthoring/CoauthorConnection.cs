using System.Threading.Channels;

/// <summary>
/// One editor or viewer in a coauthoring session: a page that joined it with its own token. Its id is the
/// secret its requests name it by, handed out only to a token the host accepted.
/// </summary>
sealed class CoauthorConnection(string id, string userId, string userName, string token, bool canWrite, int color)
{
    public string Id => id;

    public string UserId => userId;

    public string UserName => userName;

    public string Token => token;

    /// <summary>Whether its edits are taken: the host lets the user write, and the document can be edited.</summary>
    public bool CanWrite => canWrite;

    /// <summary>The colour its presence is shown in, an index the session hands out.</summary>
    public int Color => color;

    /// <summary>The paragraph it has open, by id, which nobody else may change meanwhile.</summary>
    public string? Paragraph { get; set; }

    /// <summary>The version it last said it has applied.</summary>
    public long Version { get; set; }

    /// <summary>The session's state, the latest only, for its event stream to send.</summary>
    public Channel<string> Events { get; } = Channel.CreateBounded<string>(
        new BoundedChannelOptions(1)
        {
            FullMode = BoundedChannelFullMode.DropOldest
        });

    /// <summary>How many event streams it has open: none for a while, and it has gone.</summary>
    public int Streams { get; set; }

    /// <summary>Counts down to its leaving, once its last event stream closed.</summary>
    public ITimer? Leaving { get; set; }
}
