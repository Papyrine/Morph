namespace Morph;

/// <summary>
/// Reads an email, <c>.eml</c> or Outlook <c>.msg</c>, as a document: a header block with the sender, the
/// date, the recipients, the subject and the names of the attachments, then the body. The body is the
/// message's HTML (its text when it has none), the pictures it shows by <c>cid:</c> inlined, and the
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
        var email = EmailReader.Read(emailStream);
        cancel.ThrowIfCancellationRequested();
        return ToHtml(email, culture ?? CultureInfo.InvariantCulture);
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

    static string ToHtml(EmailMessage email, CultureInfo culture)
    {
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
        AppendLine(lines, "From", email.From);
        AppendLine(lines, "Date", FormatDate(email.Date, culture));
        AppendLine(lines, "To", Join(email.To));
        AppendLine(lines, "Cc", Join(email.Cc));
        AppendLine(lines, "Bcc", Join(email.Bcc));
        AppendLine(lines, "Attachments", Join(email.Attachments));
        builder.AppendJoin("<br>", lines);
        // No rule under the block: the .docx import writes one as a row of dashes.
        builder.Append("</p>");
        builder.Append(email.Body);
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

    static string? Join(List<string> values)
    {
        if (values.Count == 0)
        {
            return null;
        }

        return string.Join(", ", values);
    }

    static string? FormatDate(DateTimeOffset? date, CultureInfo culture)
    {
        if (date == null)
        {
            return null;
        }

        return $"{date.Value.ToString("f", culture)} {date.Value.ToString("zzz", culture)}";
    }
}
