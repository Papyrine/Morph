namespace Morph;

/// <summary>
/// Reads a message with MsgReader: an Outlook <c>.msg</c>, a compound file, through
/// <see cref="Storage.Message"/>; anything else as MIME through <see cref="Message"/>. A picture the body
/// shows by <c>cid:</c> is inlined into it as a <c>data:</c> URI and left out of the attachments; the
/// attachments listed are the files the reader would save beside the message.
/// </summary>
static class EmailReader
{
    // The signature of a compound file, which an Outlook .msg is.
    static readonly byte[] compoundFile = [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1];

    public static EmailMessage Read(Stream stream)
    {
        var bytes = ToBytes(stream);
        if (bytes.AsSpan().StartsWith(compoundFile))
        {
            return ReadOutlook(bytes);
        }

        return ReadMime(bytes);
    }

    static byte[] ToBytes(Stream stream)
    {
        if (stream is MemoryStream memory &&
            memory.TryGetBuffer(out var buffer) &&
            buffer.Offset == 0 &&
            buffer.Count == buffer.Array!.Length)
        {
            return buffer.Array;
        }

        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    }

    static EmailMessage ReadOutlook(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        using var message = new Storage.Message(stream);
        var pictures = new Dictionary<string, (string Type, byte[] Data)>(StringComparer.OrdinalIgnoreCase);
        var files = new List<(string Name, string? ContentId)>();
        foreach (var attachment in message.Attachments)
        {
            switch (attachment)
            {
                case Storage.Attachment file:
                    // A picture the body shows is hidden from the attachment list by Outlook, and is still
                    // the picture.
                    if (file is {ContentId: { Length: > 0 } contentId, Data: not null})
                    {
                        pictures[contentId.Trim('<', '>')] = (file.MimeType ?? "application/octet-stream", file.Data);
                    }

                    if (!file.Hidden)
                    {
                        files.Add((file.FileName, file.ContentId));
                    }

                    break;
                case Storage.Message embedded:
                    files.Add((embedded.FileName ?? $"{embedded.Subject}.msg", null));
                    break;
            }
        }

        var (body, shown) = Body(message.BodyHtml, message.BodyText, pictures);
        var recipients = message.Recipients;
        return new(
            message.Subject,
            Format(message.Sender?.DisplayName, message.Sender?.Email),
            // Stored in UTC; the reader gives it in the machine's zone
            message.SentOn?.ToUniversalTime(),
            Recipients(recipients, RecipientType.To),
            Recipients(recipients, RecipientType.Cc),
            Recipients(recipients, RecipientType.Bcc),
            Attachments(files, shown),
            body);
    }

    static List<string> Recipients(List<Storage.Recipient> recipients, RecipientType type) =>
        recipients
            .Where(_ => _.Type == type)
            .Select(_ => Format(_.DisplayName, _.Email))
            .Where(_ => _ != null)
            .Select(_ => _!)
            .ToList();

    static EmailMessage ReadMime(byte[] bytes)
    {
        var message = new Message(bytes);
        var headers = message.Headers;
        var pictures = new Dictionary<string, (string Type, byte[] Data)>(StringComparer.OrdinalIgnoreCase);
        var files = new List<(string Name, string? ContentId)>();
        foreach (var part in message.Attachments ?? [])
        {
            if (part.ContentId is { Length: > 0 } contentId)
            {
                pictures[contentId.Trim('<', '>')] = (part.ContentType?.MediaType ?? "application/octet-stream", part.Body);
            }

            files.Add((part.FileName, part.ContentId));
        }

        var (body, shown) = Body(message.HtmlBody?.GetBodyAsText(), message.TextBody?.GetBodyAsText(), pictures);
        return new(
            headers.Subject,
            Format(headers.From),
            Date(headers),
            Addresses(headers.To),
            Addresses(headers.Cc),
            Addresses(headers.Bcc),
            Attachments(files, shown),
            body);
    }

    // The header's own value keeps the sender's offset; the parsed DateSent is UTC.
    static DateTimeOffset? Date(MessageHeader headers)
    {
        if (!string.IsNullOrWhiteSpace(headers.Date) &&
            DateTimeOffset.TryParse(headers.Date, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            return date;
        }

        if (headers.DateSent == DateTimeOffset.MinValue)
        {
            return null;
        }

        return headers.DateSent;
    }

    static List<string> Addresses(List<RfcMailAddress>? addresses) =>
        (addresses ?? [])
        .Select(Format)
        .Where(_ => _ != null)
        .Select(_ => _!)
        .ToList();

    static string? Format(RfcMailAddress? address)
    {
        if (address == null)
        {
            return null;
        }

        return Format(address.DisplayName, address.Address) ?? address.Raw;
    }

    static string? Format(string? name, string? mailbox)
    {
        name = name?.Trim();
        mailbox = mailbox?.Trim();
        if (!string.IsNullOrEmpty(name) &&
            !string.IsNullOrEmpty(mailbox) &&
            !name.Equals(mailbox, StringComparison.OrdinalIgnoreCase))
        {
            return $"{name} <{mailbox}>";
        }

        if (!string.IsNullOrEmpty(mailbox))
        {
            return mailbox;
        }

        if (!string.IsNullOrEmpty(name))
        {
            return name;
        }

        return null;
    }

    // The files that are not pictures the body shows.
    static List<string> Attachments(List<(string Name, string? ContentId)> files, HashSet<string> shown) =>
        files
            .Where(_ => _.ContentId == null || !shown.Contains(_.ContentId.Trim('<', '>')))
            .Select(_ => _.Name)
            .Where(_ => !string.IsNullOrWhiteSpace(_))
            .ToList();

    /// <summary>
    /// The body as an HTML fragment: the HTML with every <c>cid:</c> picture it shows inlined, or the text
    /// in a <c>pre</c>. Also which content ids the body shows.
    /// </summary>
    static (string Html, HashSet<string> Shown) Body(string? html, string? text, Dictionary<string, (string Type, byte[] Data)> pictures)
    {
        var shown = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(html))
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return ("", shown);
            }

            return ($"<pre>{WebUtility.HtmlEncode(text)}</pre>", shown);
        }

        var parser = new HtmlParser();
        using var document = parser.ParseDocument(html);
        foreach (var image in document.QuerySelectorAll("img"))
        {
            var source = image.GetAttribute("src");
            if (source == null ||
                !source.StartsWith("cid:", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var contentId = Uri.UnescapeDataString(source[4..]).Trim('<', '>');
            if (pictures.TryGetValue(contentId, out var picture))
            {
                image.SetAttribute("src", $"data:{picture.Type};base64,{Convert.ToBase64String(picture.Data)}");
                shown.Add(contentId);
            }
            else
            {
                // A picture the message does not carry: the alt text, if any, is all that can be shown.
                image.Remove();
            }
        }

        return (document.Body!.InnerHtml, shown);
    }
}
