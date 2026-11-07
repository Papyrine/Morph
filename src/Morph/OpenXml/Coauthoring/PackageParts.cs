using System.Xml.Linq;

/// <summary>
/// A package's entries for the merge: the bytes of each, and the XML of those it reads or rewrites,
/// parsed once and written back only if changed. Names are the zip's, without a leading slash.
/// </summary>
sealed class PackageParts
{
    readonly OrderedDictionary<string, byte[]> entries;
    readonly Dictionary<string, XDocument?> parsed = new(StringComparer.Ordinal);
    readonly HashSet<string> changed = new(StringComparer.Ordinal);

    public PackageParts(byte[] docx) =>
        entries = PackageEntries.Read(docx);

    public IEnumerable<string> Names => entries.Keys;

    public bool Has(string name) =>
        entries.ContainsKey(name);

    /// <summary>The entry's bytes, written again from its XML if that was changed; null if there is none.</summary>
    public byte[]? Bytes(string name)
    {
        if (changed.Contains(name) &&
            parsed.TryGetValue(name, out var xml) &&
            xml != null)
        {
            entries[name] = Serialize(xml);
            changed.Remove(name);
        }

        return entries.GetValueOrDefault(name);
    }

    /// <summary>The entry's XML, or null where there is no entry or it is not XML.</summary>
    public XDocument? Xml(string name)
    {
        if (parsed.TryGetValue(name, out var xml))
        {
            return xml;
        }

        xml = null;
        if (entries.TryGetValue(name, out var bytes) &&
            IsXmlName(name))
        {
            xml = Parse(bytes);
        }

        parsed[name] = xml;
        return xml;
    }

    /// <summary>The root of the entry's XML, or null.</summary>
    public XElement? Root(string name) =>
        Xml(name)?.Root;

    /// <summary>Marks the entry's XML as changed, to be written again.</summary>
    public void Changed(string name) =>
        changed.Add(name);

    /// <summary>The names of the parts whose root is one of <paramref name="roots"/>.</summary>
    public IEnumerable<string> WithRoot(IReadOnlySet<XName> roots) =>
        Names.Where(_ => Root(_) is { } root && roots.Contains(root.Name)).ToList();

    /// <summary>The name of the part whose root is <paramref name="root"/>, if there is one.</summary>
    public string? WithRoot(XName root) =>
        Names.FirstOrDefault(_ => Root(_)?.Name == root);

    /// <summary>Gives an entry another name, keeping its place.</summary>
    public void Rename(string from, string to)
    {
        var bytes = Bytes(from)!;
        var index = entries.IndexOf(from);
        entries.RemoveAt(index);
        entries.Insert(index, to, bytes);
        if (parsed.Remove(from, out var xml))
        {
            parsed[to] = xml;
        }
    }

    public static bool IsXmlName(string name) =>
        name.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) ||
        name.EndsWith(".rels", StringComparison.OrdinalIgnoreCase);

    public static XDocument? Parse(byte[] bytes)
    {
        try
        {
            using var stream = new MemoryStream(bytes, writable: false);
            return XDocument.Load(stream, LoadOptions.PreserveWhitespace);
        }
        catch (XmlException)
        {
            return null;
        }
    }

    public static byte[] Serialize(XDocument xml)
    {
        using var stream = new MemoryStream();
        var settings = new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(false),
            Indent = false
        };
        using (var writer = XmlWriter.Create(stream, settings))
        {
            xml.Save(writer);
        }

        return stream.ToArray();
    }

    /// <summary>The part a relationship's target names, from the relationships part it is in.</summary>
    public static string? Target(string relationshipsPart, XElement relationship)
    {
        if ((string?) relationship.Attribute("TargetMode") == "External" ||
            (string?) relationship.Attribute("Target") is not { } target)
        {
            return null;
        }

        if (target.StartsWith('/'))
        {
            return Normalize(target[1..]);
        }

        return Normalize(SourceFolder(relationshipsPart) + target);
    }

    /// <summary>The target to write in <paramref name="relationshipsPart"/> for <paramref name="part"/>.</summary>
    public static string RelativeTarget(string relationshipsPart, string part)
    {
        var folder = SourceFolder(relationshipsPart);
        if (folder.Length > 0 &&
            part.StartsWith(folder, StringComparison.OrdinalIgnoreCase))
        {
            return part[folder.Length..];
        }

        return "/" + part;
    }

    /// <summary>The part a relationships part belongs to, or "" for the package's own.</summary>
    public static string Source(string relationshipsPart)
    {
        // word/_rels/document.xml.rels belongs to word/document.xml, _rels/.rels to the package.
        var slash = relationshipsPart.LastIndexOf("_rels/", StringComparison.Ordinal);
        var folder = relationshipsPart[..slash];
        var file = relationshipsPart[(slash + "_rels/".Length)..];
        return folder + file[..^".rels".Length];
    }

    /// <summary>The relationships part of <paramref name="part"/>.</summary>
    public static string RelationshipsOf(string part)
    {
        var slash = part.LastIndexOf('/');
        return part[..(slash + 1)] + "_rels/" + part[(slash + 1)..] + ".rels";
    }

    static string SourceFolder(string relationshipsPart)
    {
        var source = Source(relationshipsPart);
        var slash = source.LastIndexOf('/');
        return source[..(slash + 1)];
    }

    static string Normalize(string path)
    {
        var parts = new List<string>();
        foreach (var segment in path.Split('/'))
        {
            if (segment == "..")
            {
                if (parts.Count > 0)
                {
                    parts.RemoveAt(parts.Count - 1);
                }

                continue;
            }

            if (segment is "." or "")
            {
                continue;
            }

            parts.Add(segment);
        }

        return string.Join('/', parts);
    }
}
