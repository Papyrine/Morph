// The body's WordprocessingML, for the cases that ask for a markup diff, and a small line diff.
static class Markup
{
    public static string Body(Word.Document document)
    {
        var flat = document.Content.WordOpenXML;
        try
        {
            var xml = System.Xml.Linq.XDocument.Parse(flat);
            System.Xml.Linq.XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
            var body = xml.Descendants(w + "body").FirstOrDefault();
            if (body == null)
            {
                return flat;
            }

            var settings = new System.Xml.XmlWriterSettings
            {
                Indent = true,
                OmitXmlDeclaration = true,
                NewLineChars = "\n"
            };
            var builder = new StringBuilder();
            using var writer = System.Xml.XmlWriter.Create(builder, settings);
            body.WriteTo(writer);
            writer.Flush();
            return Regex.Replace(
                builder.ToString(),
                """
                \s+w:rsid\w*="[^"]*"
                """, "");
        }
        catch (Exception)
        {
            return flat;
        }
    }
}
