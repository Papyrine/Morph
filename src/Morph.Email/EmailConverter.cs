namespace Morph;

/// <summary>
/// Reads an email, <c>.eml</c> or Outlook <c>.msg</c>, as a document: a header block with the sender, the
/// date, the recipients, the subject and the names of the attachments, then the body. The body is the
/// message's HTML, its <c>cid:</c> images inlined and remote resources dropped (OfficeIMO.Email), and the
/// document is a <c>.docx</c> (OpenXmlHtml), which Morph lays out with the inline styles and images an
/// email body relies on. <see cref="PdfEmailConverter"/> renders it to PDF.
/// </summary>
public static class EmailConverter
{
    /// <summary>The email as an HTML document: the header block, then the body.</summary>
    public static string ConvertToHtml(string emailPath, CultureInfo? culture = null, Cancel cancel = default)
    {
        using var stream = File.OpenRead(emailPath);
        return ConvertToHtml(stream, culture, cancel);
    }

    /// <summary>
    /// The email as an HTML document: the header block, then the body. The date is written in
    /// <paramref name="culture"/>, the invariant culture when null.
    /// </summary>
    public static string ConvertToHtml(Stream emailStream, CultureInfo? culture = null, Cancel cancel = default)
    {
        cancel.ThrowIfCancellationRequested();
        var email = EmailDocument.Load(emailStream, null, cancel);
        return ToHtml(email, culture ?? CultureInfo.InvariantCulture, cancel);
    }

    /// <summary>The email as a Word document, written to <paramref name="output"/>.</summary>
    public static void ConvertToDocx(Stream emailStream, Stream output, CultureInfo? culture = null, Cancel cancel = default)
    {
        using var docx = ToDocx(emailStream, culture, cancel);
        docx.CopyTo(output);
    }

    internal static MemoryStream ToDocx(Stream emailStream, CultureInfo? culture, Cancel cancel)
    {
        var html = ConvertToHtml(emailStream, culture, cancel);
        cancel.ThrowIfCancellationRequested();
        var docx = new MemoryStream();
        // The default settings take data: images and fetch neither local nor remote ones.
        WordHtmlConverter.ConvertToDocx(html, docx);
        docx.Position = 0;
        return docx;
    }

    static string ToHtml(EmailDocument email, CultureInfo culture, Cancel cancel)
    {
        // The exporter's page is the body sanitized, its cid: images as data: URIs, under a header of its
        // own. That header is replaced by the block below, which lists the attachments too.
        var exported = EmailHtmlExporter.ExportContent(email, cancel).Html;
        var parser = new HtmlParser();
        using var page = parser.ParseDocument(exported);
        var body = page.Body!;
        if (body.QuerySelector("header") is { } header)
        {
            if (header.NextElementSibling is { } rule &&
                rule.TagName.Equals("hr", StringComparison.OrdinalIgnoreCase))
            {
                rule.Remove();
            }

            header.Remove();
        }

        var subject = email.Subject;
        if (string.IsNullOrWhiteSpace(subject))
        {
            subject = "(no subject)";
        }

        var builder = new StringBuilder();
        builder.Append("<!doctype html><html><head><meta charset=\"utf-8\"><title>");
        builder.Append(WebUtility.HtmlEncode(subject));
        builder.Append("</title></head><body><h1>");
        builder.Append(WebUtility.HtmlEncode(subject));
        builder.Append("</h1><p>");
        var lines = new List<string>();
        AppendLine(lines, "From", Format(email.From ?? email.Sender));
        AppendLine(lines, "Date", FormatDate(email.Date, culture));
        AppendLine(lines, "To", Recipients(email, EmailRecipientKind.To));
        AppendLine(lines, "Cc", Recipients(email, EmailRecipientKind.Cc));
        AppendLine(lines, "Bcc", Recipients(email, EmailRecipientKind.Bcc));
        AppendLine(lines, "Attachments", Attachments(email));
        builder.AppendJoin("<br>", lines);
        // No rule under the block: the .docx import writes one as a row of dashes.
        builder.Append("</p>");
        builder.Append(body.InnerHtml);
        builder.Append("</body></html>");
        return builder.ToString();
    }

    static void AppendLine(List<string> lines, string label, string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return;
        }

        lines.Add($"<b>{label}:</b> {WebUtility.HtmlEncode(value)}");
    }

    static string? FormatDate(DateTimeOffset? date, CultureInfo culture)
    {
        if (date == null)
        {
            return null;
        }

        return $"{date.Value.ToString("f", culture)} {date.Value.ToString("zzz", culture)}";
    }

    static string? Recipients(EmailDocument email, EmailRecipientKind kind)
    {
        var addresses = email.Recipients
            .Where(_ => _.Kind == kind)
            .Select(_ => Format(_.Address))
            .Where(_ => !string.IsNullOrEmpty(_))
            .ToList();
        if (addresses.Count == 0)
        {
            return null;
        }

        return string.Join(", ", addresses);
    }

    // The files the reader would save beside the message: not the images the body shows in place.
    static string? Attachments(EmailDocument email)
    {
        var names = email.Attachments
            .Where(_ => !_.IsInline && !_.IsHidden)
            .Select(_ => _.FileName)
            .Where(_ => !string.IsNullOrWhiteSpace(_))
            .ToList();
        if (names.Count == 0)
        {
            return null;
        }

        return string.Join(", ", names);
    }

    static string? Format(EmailAddress? address)
    {
        if (address == null)
        {
            return null;
        }

        var name = address.DisplayName?.Trim();
        var mailbox = address.Address?.Trim();
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

        return address.RawValue;
    }
}
