namespace Morph;

/// <summary>
/// What the document says of a message, the same whichever file it came from: the addresses already
/// written out (<c>Name &lt;mailbox&gt;</c>), the attachments by name, and the body as an HTML fragment
/// with the pictures it shows inlined.
/// </summary>
sealed record EmailMessage(
    string? Subject,
    string? From,
    DateTimeOffset? Date,
    List<string> To,
    List<string> Cc,
    List<string> Bcc,
    List<string> Attachments,
    string Body);
