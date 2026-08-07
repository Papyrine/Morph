using System.Text.RegularExpressions;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Wordprocessing;

/// <summary>
/// Audits the OpenXml SDK's catalogue of WordprocessingML element types against the tags
/// referenced in <c>docs/word-features.md</c> and <c>src/missingTags.md</c>. The verified
/// snapshot shows three lists:
///
///   1. SDK tags not mentioned in either doc — likely coverage gaps to triage.
///   2. Tags mentioned in docs but unknown to the SDK — typos / invented names / extension
///      namespaces the SDK doesn't model (a14:, c16r3:, int2: …).
///   3. SDK namespaces present in the catalogue (informational — sanity-check the prefix map).
///
/// The test always passes; treat the snapshot diff as the audit output. When you intentionally
/// change either doc, accept the new baseline via the usual Verify-promote workflow.
/// </summary>
public class TagCoverageAuditTests
{
    /// <summary>
    /// XML namespaces that surface in DOCX wordprocessing parts. Anything outside this set
    /// (DrawingML for spreadsheets, PowerPoint, etc.) is irrelevant for Morph.
    /// </summary>
    static readonly HashSet<string> relevantNamespaces =
    [
        with(StringComparer.Ordinal),
        // w
        "http://schemas.openxmlformats.org/wordprocessingml/2006/main",
        // w14
        "http://schemas.microsoft.com/office/word/2010/wordml",
        // w15
        "http://schemas.microsoft.com/office/word/2012/wordml",
        // w16
        "http://schemas.microsoft.com/office/word/2018/wordml",
        // m
        "http://schemas.openxmlformats.org/officeDocument/2006/math",
        // a
        "http://schemas.openxmlformats.org/drawingml/2006/main",
        // a14
        "http://schemas.microsoft.com/office/drawing/2010/main",
        // a16
        "http://schemas.microsoft.com/office/drawing/2012/main",
        // wp
        "http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing",
        // wp14
        "http://schemas.microsoft.com/office/word/2010/wordprocessingDrawing",
        // pic
        "http://schemas.openxmlformats.org/drawingml/2006/picture",
        // mc
        "http://schemas.openxmlformats.org/markup-compatibility/2006",
        // wps
        "http://schemas.microsoft.com/office/word/2010/wordprocessingShape",
        // wpg
        "http://schemas.microsoft.com/office/word/2010/wordprocessingGroup",
        // wpi
        "http://schemas.microsoft.com/office/word/2010/wordprocessingInk",
        // wpc
        "http://schemas.microsoft.com/office/word/2010/wordprocessingCanvas",
        // cx
        "http://schemas.microsoft.com/office/drawing/2014/chartex",
        // cdr
        "http://schemas.openxmlformats.org/officeDocument/2006/chartDrawing",
        // cdr
        "http://schemas.openxmlformats.org/drawingml/2006/chartDrawing",
        // dgm
        "http://schemas.openxmlformats.org/drawingml/2006/diagram",
        // c
        "http://schemas.openxmlformats.org/drawingml/2006/chart",
        // v
        "urn:schemas-microsoft-com:vml",
        // o
        "urn:schemas-microsoft-com:office:office",
        // w10
        "urn:schemas-microsoft-com:office:word",
        // r
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships"
    ];

    [Test]
    public async Task AuditSdkTagsAgainstDocs()
    {
        var sdkElements = CollectSdkElementTags();
        var sdkAttributes = CollectSdkAttributeTags();
        var docTags = CollectDocumentedTags();

        // Attributes the docs mention (e.g. `w:val`, `w:firstLine`) aren't a coverage gap —
        // they're already part of the elements that own them. Subtract them out before
        // flagging an "unknown to SDK" entry so the report focuses on real typos / extension
        // namespaces.
        var unknownToSdk = docTags
            .Except(sdkElements, StringComparer.Ordinal)
            .Except(sdkAttributes, StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();

        var notInDocs = sdkElements
            .Except(docTags, StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();

        var prefixesInSdk = sdkElements.Select(_ => _.Split(':', 2)[0]).Distinct().Order(StringComparer.Ordinal).ToList();

        var report = new StringBuilder();
        report.AppendLine("# OOXML Tag Coverage Audit");
        report.AppendLine();
        report.AppendLine($"SDK element types catalogued: {sdkElements.Count}");
        report.AppendLine($"SDK attribute names catalogued: {sdkAttributes.Count}");
        report.AppendLine($"Tags mentioned in docs: {docTags.Count}");
        report.AppendLine($"SDK namespaces seen: {string.Join(", ", prefixesInSdk)}");
        report.AppendLine();

        report.AppendLine($"## SDK tags missing from both word-features.md and missingTags.md ({notInDocs.Count})");
        report.AppendLine();
        report.AppendLine("These elements are recognised by the OpenXml SDK but not documented anywhere — either coverage gaps to add to `missingTags.md`, or genuinely uninteresting plumbing tags. Triage and either document or explicitly accept.");
        report.AppendLine();
        foreach (var tag in notInDocs)
        {
            report.AppendLine($"- `{tag}`");
        }

        report.AppendLine();
        report.AppendLine($"## Tags mentioned in docs but unknown to the SDK ({unknownToSdk.Count})");
        report.AppendLine();
        report.AppendLine("These are typos, invented names, or extension-namespace tags the SDK doesn't model (e.g. `a14:`, `c16r3:`, `int2:`, `adec:`, `ask:`). Worth a manual scan for typos. Attributes already known to the SDK are filtered out.");
        report.AppendLine();
        foreach (var tag in unknownToSdk)
        {
            report.AppendLine($"- `{tag}`");
        }

        await Verify(report.ToString())
            .UseDirectory("Audit");
    }

    /// <summary>
    /// Walks every concrete <see cref="OpenXmlElement"/> subtype in the SDK assembly,
    /// instantiates it, and records its <c>{prefix}:{localName}</c>. Filters to namespaces
    /// relevant to wordprocessing documents.
    /// </summary>
    static HashSet<string> CollectSdkElementTags()
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        var elementBase = typeof(OpenXmlElement);

        foreach (var type in typeof(Body).Assembly.GetTypes())
        {
            if (type.IsAbstract || !elementBase.IsAssignableFrom(type))
            {
                continue;
            }

            // Skip the SDK's internal placeholder for unknown elements.
            if (type == typeof(OpenXmlUnknownElement) || type == typeof(OpenXmlMiscNode))
            {
                continue;
            }

            var ctor = type.GetConstructor(BindingFlags.Public | BindingFlags.Instance, Type.EmptyTypes);
            if (ctor == null)
            {
                continue;
            }

            OpenXmlElement instance;
            try
            {
                instance = (OpenXmlElement) ctor.Invoke(null);
            }
            catch
            {
                continue;
            }

            if (string.IsNullOrEmpty(instance.LocalName) ||
                string.IsNullOrEmpty(instance.Prefix))
            {
                continue;
            }

            if (!relevantNamespaces.Contains(instance.NamespaceUri))
            {
                continue;
            }

            result.Add($"{instance.Prefix}:{instance.LocalName}");
        }

        return result;
    }

    /// <summary>
    /// Walks every concrete <see cref="OpenXmlElement"/> subtype and reflects each property
    /// for the SDK's <c>SchemaAttrAttribute</c> custom attribute, recording every modelled
    /// XML attribute as <c>{prefix}:{localName}</c>. Lets the audit subtract attributes from
    /// the "unknown to SDK" list so the report only flags real typos.
    /// </summary>
    static HashSet<string> CollectSdkAttributeTags()
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        var elementBase = typeof(OpenXmlElement);

        foreach (var type in typeof(Body).Assembly.GetTypes())
        {
            if (type.IsAbstract ||
                !elementBase.IsAssignableFrom(type))
            {
                continue;
            }

            var ctor = type.GetConstructor(BindingFlags.Public | BindingFlags.Instance, Type.EmptyTypes);
            if (ctor == null)
            {
                continue;
            }

            OpenXmlElement instance;
            try
            {
                instance = (OpenXmlElement) ctor.Invoke(null);
            }
            catch
            {
                continue;
            }

            // ElementMetadata exposes the schema info for both elements and their attributes;
            // the schema-attribute records carry a QName whose Namespace + Name we want.
            var metadataProp = type.GetProperty("Metadata", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.FlattenHierarchy);
            object? metadata;
            try
            {
                metadata = metadataProp?.GetValue(instance);
            }
            catch
            {
                // Some types throw during metadata initialization; skip them.
                continue;
            }

            if (metadata == null)
            {
                continue;
            }

            var attributesProp = metadata.GetType().GetProperty("Attributes");
            if (attributesProp?.GetValue(metadata) is not IEnumerable attributes)
            {
                continue;
            }

            foreach (var attribute in attributes)
            {
                if (attribute == null)
                {
                    continue;
                }

                var qNameProp = attribute.GetType().GetProperty("QName");
                var qName = qNameProp?.GetValue(attribute);
                if (qName == null)
                {
                    continue;
                }

                var qNameType = qName.GetType();
                var ns = qNameType.GetProperty("Namespace")?.GetValue(qName)?.ToString();
                var name = qNameType.GetProperty("Name")?.GetValue(qName) as string;
                if (string.IsNullOrEmpty(ns) || string.IsNullOrEmpty(name))
                {
                    continue;
                }

                // Empty namespace = unqualified attribute (the common case for OOXML attributes
                // that share the parent element's namespace). Map to the parent element's prefix.
                var prefix = ns.Length == 0
                    ? relevantNamespaces.Contains(instance.NamespaceUri) ? NamespaceToPrefix(instance.NamespaceUri) : null
                    : relevantNamespaces.Contains(ns) ? NamespaceToPrefix(ns) : null;

                if (prefix != null)
                {
                    result.Add($"{prefix}:{name}");
                }
            }
        }

        return result;
    }

    static string? NamespaceToPrefix(string ns) => ns switch
    {
        "http://schemas.openxmlformats.org/wordprocessingml/2006/main" => "w",
        "http://schemas.microsoft.com/office/word/2010/wordml" => "w14",
        "http://schemas.microsoft.com/office/word/2012/wordml" => "w15",
        "http://schemas.microsoft.com/office/word/2018/wordml" => "w16",
        "http://schemas.openxmlformats.org/officeDocument/2006/math" => "m",
        "http://schemas.openxmlformats.org/drawingml/2006/main" => "a",
        "http://schemas.microsoft.com/office/drawing/2010/main" => "a14",
        "http://schemas.microsoft.com/office/drawing/2012/main" => "a16",
        "http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing" => "wp",
        "http://schemas.microsoft.com/office/word/2010/wordprocessingDrawing" => "wp14",
        "http://schemas.openxmlformats.org/drawingml/2006/picture" => "pic",
        "http://schemas.openxmlformats.org/markup-compatibility/2006" => "mc",
        "http://schemas.microsoft.com/office/word/2010/wordprocessingShape" => "wps",
        "http://schemas.microsoft.com/office/word/2010/wordprocessingGroup" => "wpg",
        "http://schemas.microsoft.com/office/word/2010/wordprocessingInk" => "wpi",
        "http://schemas.microsoft.com/office/word/2010/wordprocessingCanvas" => "wpc",
        "http://schemas.openxmlformats.org/drawingml/2006/diagram" => "dgm",
        "http://schemas.openxmlformats.org/drawingml/2006/chart" => "c",
        "http://schemas.microsoft.com/office/drawing/2014/chartex" => "cx",
        "urn:schemas-microsoft-com:vml" => "v",
        "urn:schemas-microsoft-com:office:office" => "o",
        "urn:schemas-microsoft-com:office:word" => "w10",
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships" => "r",
        _ => null
    };

    /// <summary>
    /// Reads <c>docs/word-features.md</c> and <c>src/missingTags.md</c> and extracts every
    /// <c>prefix:localName</c> token that uses a known wordprocessing prefix.
    /// </summary>
    static HashSet<string> CollectDocumentedTags()
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        var paths = new[]
        {
            Path.GetFullPath(Path.Combine(ProjectFiles.SolutionDirectory, "..", "docs", "word-features.md")),
            Path.Combine(ProjectFiles.SolutionDirectory, "missingTags.md"),
        };

        // Match prefix:localName. Constrain prefixes to the known wordprocessing-doc set so we
        // don't pick up unrelated `xml:lang` or schema URLs.
        var pattern = new Regex(@"\b(w|w10|w14|w15|w16|wp|wp14|wpg|wps|pic|m|a|a14|a16|mc|dgm|c|cs|v|o|r|ink|adec|asvg|c14|c16|c16r3|thm15|w16cid|w16cur|w16sdtdh|w16se|wopx|w16ex|x14|w16hdp):([a-zA-Z][a-zA-Z0-9]*)\b");

        foreach (var path in paths)
        {
            if (!File.Exists(path))
            {
                continue;
            }

            var text = File.ReadAllText(path);
            foreach (Match match in pattern.Matches(text))
            {
                result.Add($"{match.Groups[1].Value}:{match.Groups[2].Value}");
            }
        }

        return result;
    }
}
