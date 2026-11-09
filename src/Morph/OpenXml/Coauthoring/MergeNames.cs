/// <summary>The namespaces and names the coauthoring merge reads parts by.</summary>
static class MergeNames
{
    public static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    public static readonly XNamespace W14 = "http://schemas.microsoft.com/office/word/2010/wordml";
    public static readonly XNamespace W15 = "http://schemas.microsoft.com/office/word/2012/wordml";
    public static readonly XNamespace W16Cid = "http://schemas.microsoft.com/office/word/2016/wordml/cid";
    public static readonly XNamespace W16Cex = "http://schemas.microsoft.com/office/word/2018/wordml/cex";
    public static readonly XNamespace Mc = "http://schemas.openxmlformats.org/markup-compatibility/2006";
    public static readonly XNamespace R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    public static readonly XNamespace Wp = "http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing";
    public static readonly XNamespace Relationships = "http://schemas.openxmlformats.org/package/2006/relationships";
    public static readonly XNamespace ContentTypes = "http://schemas.openxmlformats.org/package/2006/content-types";

    public static readonly XName ParaId = W14 + "paraId";
    public static readonly XName Id = W + "id";
    public static readonly XName Val = W + "val";
    public static readonly XName ParagraphTag = W + "p";
    public static readonly XName RowTag = W + "tr";
    public static readonly XName Fallback = Mc + "Fallback";

    /// <summary>The roots of the parts whose paragraphs the editor edits, and the comments'.</summary>
    public static readonly HashSet<XName> Stories =
    [
        W + "document",
        W + "hdr",
        W + "ftr",
        W + "footnotes",
        W + "endnotes",
        W + "comments"
    ];

    /// <summary>
    /// The roots of the parts a document has at most one of: two edits that each made one made the same
    /// part, which is merged, where two headers made at once are two parts.
    /// </summary>
    public static readonly HashSet<XName> SingleInstance =
    [
        W + "styles",
        W + "numbering",
        W + "settings",
        W + "comments",
        W + "footnotes",
        W + "endnotes",
        W + "fonts",
        W + "webSettings",
        W15 + "commentsEx",
        W15 + "people",
        W16Cid + "commentsIds",
        W16Cex + "commentsExtensible"
    ];

    /// <summary>The paragraphs and rows under an element, in document order, past mc:Fallback's copies.</summary>
    public static IEnumerable<XElement> Blocks(XElement element)
    {
        foreach (var child in element.Elements())
        {
            if (child.Name == Fallback)
            {
                continue;
            }

            if (child.Name == ParagraphTag ||
                child.Name == RowTag)
            {
                yield return child;
            }

            foreach (var nested in Blocks(child))
            {
                yield return nested;
            }
        }
    }

    /// <summary>The paragraphs under an element, by their id, the first of any two that share one.</summary>
    public static Dictionary<string, XElement> ParagraphsById(XElement element)
    {
        var paragraphs = new Dictionary<string, XElement>(StringComparer.OrdinalIgnoreCase);
        foreach (var block in Blocks(element))
        {
            if (block.Name == ParagraphTag &&
                (string?) block.Attribute(ParaId) is { } id)
            {
                paragraphs.TryAdd(id, block);
            }
        }

        return paragraphs;
    }
}
