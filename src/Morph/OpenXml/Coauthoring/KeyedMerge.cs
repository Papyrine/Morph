using System.Xml.Linq;
using static MergeNames;

/// <summary>
/// The three-way merge of an XML part two edits changed from the same base. An element only one side
/// changed is that side's; one both changed alike is either; one both changed otherwise is merged child
/// by child where it is a container (the body, a table, a row, a cell, a content control, the styles or
/// the relationships), and is a <see cref="MergeConflict"/> where it is not (a paragraph, a style, a
/// relationship).
///
/// Children are matched across the three by a key: a paragraph and a row by their <c>w14:paraId</c>
/// (<see cref="ParagraphIds"/>), a note or a comment by its id, a style by its styleId, a relationship
/// by its Id. A table, a cell or a content control has no key of its own and is matched by the
/// paragraphs and rows it holds; anything else is matched by its name and its place among its
/// like-named siblings. Theirs gives the order: mine's deletions come out of it where theirs left the
/// element alone, and mine's insertions go in before the element that follows them in mine, or after
/// the one before them — so a paragraph Enter split off ahead of the one it was split from stays ahead
/// of it, and a paragraph added at the end goes before the body's last <c>w:sectPr</c>.
/// </summary>
static class KeyedMerge
{
    static readonly Dictionary<XName, XName> keys = new()
    {
        [ParagraphTag] = ParaId,
        [RowTag] = ParaId,
        [W + "footnote"] = Id,
        [W + "endnote"] = Id,
        [W + "comment"] = Id,
        [W + "style"] = W + "styleId",
        [W + "abstractNum"] = W + "abstractNumId",
        [W + "num"] = W + "numId",
        [W + "font"] = W + "name",
        [W + "bookmarkStart"] = Id,
        [W + "bookmarkEnd"] = Id,
        [W + "commentRangeStart"] = Id,
        [W + "commentRangeEnd"] = Id,
        [W + "permStart"] = Id,
        [W + "permEnd"] = Id,
        [Relationships + "Relationship"] = "Id",
        [ContentTypes + "Override"] = "PartName",
        [ContentTypes + "Default"] = "Extension",
        [W15 + "commentEx"] = W15 + "paraId",
        [W15 + "person"] = W15 + "author",
        [W16Cid + "commentId"] = W16Cid + "paraId",
        [W16Cex + "commentExtensible"] = W16Cex + "durableId"
    };

    // Matched by the paragraphs and rows they hold.
    static readonly HashSet<XName> held =
    [
        W + "tbl",
        W + "tc",
        W + "sdt",
        W + "sdtContent",
        W + "customXml"
    ];

    // Merged child by child when both sides changed them.
    static readonly HashSet<XName> containers =
    [
        W + "document",
        W + "body",
        W + "hdr",
        W + "ftr",
        W + "footnotes",
        W + "endnotes",
        W + "footnote",
        W + "endnote",
        W + "comments",
        W + "comment",
        W + "tbl",
        W + "tr",
        W + "tc",
        W + "sdt",
        W + "sdtContent",
        W + "customXml",
        W + "styles",
        W + "numbering",
        W + "settings",
        W + "fonts",
        Relationships + "Relationships",
        ContentTypes + "Types",
        W15 + "commentsEx",
        W15 + "people",
        W16Cid + "commentsIds",
        W16Cex + "commentsExtensible"
    ];

    /// <summary>
    /// Merges the roots of a part. <paramref name="base"/> is null for a part both edits made, which is
    /// merged against an empty root of its kind.
    /// </summary>
    public static XElement Merge(XElement? @base, XElement mine, XElement theirs) =>
        Element(@base ?? new XElement(mine.Name), mine, theirs);

    static XElement Element(XElement @base, XElement mine, XElement theirs)
    {
        if (XNode.DeepEquals(mine, @base))
        {
            return theirs;
        }

        if (XNode.DeepEquals(theirs, @base) ||
            XNode.DeepEquals(mine, theirs))
        {
            return mine;
        }

        if (!IsContainer(mine) ||
            !IsContainer(theirs) ||
            !IsContainer(@base))
        {
            throw Conflict(mine);
        }

        return new(mine.Name, Attributes(@base, mine, theirs), Children(@base, mine, theirs));
    }

    // A container holds elements, and text only as the whitespace between them.
    static bool IsContainer(XElement element) =>
        containers.Contains(element.Name) &&
        element.Nodes().All(_ => _ is XElement || _ is XText text && string.IsNullOrWhiteSpace(text.Value));

    static MergeConflict Conflict(XElement element)
    {
        var paragraph = element.AncestorsAndSelf(ParagraphTag).LastOrDefault();
        var id = (string?) paragraph?.Attribute(ParaId);
        if (id != null)
        {
            return new("Both edits changed the same paragraph.", id);
        }

        return new($"Both edits changed the same {element.Name.LocalName}.");
    }

    static List<XAttribute> Attributes(XElement @base, XElement mine, XElement theirs)
    {
        var merged = new List<XAttribute>();
        var names = theirs.Attributes()
            .Concat(mine.Attributes())
            .Select(_ => _.Name)
            .Distinct();
        foreach (var name in names)
        {
            var original = (string?) @base.Attribute(name);
            var ours = (string?) mine.Attribute(name);
            var others = (string?) theirs.Attribute(name);
            string? value;
            if (ours == original)
            {
                value = others;
            }
            else if (others == original ||
                     ours == others)
            {
                value = ours;
            }
            else if (name == Mc + "Ignorable")
            {
                value = string.Join(' ', $"{others} {ours}".Split(' ', StringSplitOptions.RemoveEmptyEntries).Distinct());
            }
            else
            {
                throw new MergeConflict($"Both edits changed {mine.Name.LocalName}'s {name.LocalName}.");
            }

            if (value != null)
            {
                merged.Add(new(name, value));
            }
        }

        return merged;
    }

    static List<XElement> Children(XElement @base, XElement mine, XElement theirs)
    {
        var baseKeys = Keys(@base, null, "b");
        var mineKeys = Keys(mine, baseKeys, "m");
        var theirsKeys = Keys(theirs, baseKeys, "t");
        var baseMap = baseKeys.ToDictionary(_ => _.Key, _ => _.Element);
        var mineMap = mineKeys.ToDictionary(_ => _.Key, _ => _.Element);
        var theirsMap = theirsKeys.ToDictionary(_ => _.Key, _ => _.Element);

        // Theirs gives the order, each element as merged.
        var merged = new List<(string Key, XElement Element)>();
        foreach (var (key, other) in theirsKeys)
        {
            var original = baseMap.GetValueOrDefault(key);
            var ours = mineMap.GetValueOrDefault(key);
            if (original == null)
            {
                // Made by theirs, or by both.
                if (ours == null ||
                    XNode.DeepEquals(ours, other))
                {
                    merged.Add((key, other));
                    continue;
                }

                merged.Add((key, Element(new(ours.Name), ours, other)));
                continue;
            }

            if (ours == null)
            {
                // Deleted by mine: gone, unless theirs changed it.
                if (XNode.DeepEquals(other, original))
                {
                    continue;
                }

                throw Conflict(other);
            }

            merged.Add((key, Element(original, ours, other)));
        }

        // Deleted by theirs: gone, unless mine changed it.
        foreach (var (key, original) in baseKeys)
        {
            if (!theirsMap.ContainsKey(key) &&
                mineMap.TryGetValue(key, out var ours) &&
                !XNode.DeepEquals(ours, original))
            {
                throw Conflict(ours);
            }
        }

        // Made by mine: before what follows it in mine, else after what comes before it.
        for (var index = 0; index < mineKeys.Count; index++)
        {
            var (key, ours) = mineKeys[index];
            if (baseMap.ContainsKey(key) ||
                theirsMap.ContainsKey(key))
            {
                continue;
            }

            merged.Insert(Place(merged, mineKeys, index), (key, ours));
        }

        return [.. merged.Select(_ => _.Element)];
    }

    // Where an element mine made goes in the merged children.
    static int Place(List<(string Key, XElement Element)> merged, List<(string Key, XElement Element)> mine, int index)
    {
        for (var after = index + 1; after < mine.Count; after++)
        {
            var found = merged.FindIndex(_ => _.Key == mine[after].Key);
            if (found >= 0)
            {
                return found;
            }
        }

        for (var before = index - 1; before >= 0; before--)
        {
            var found = merged.FindIndex(_ => _.Key == mine[before].Key);
            if (found >= 0)
            {
                return found + 1;
            }
        }

        return merged.Count;
    }

    // The children of an element by key. A table, cell or content control of mine or theirs takes the key
    // of the base's that holds most of the same paragraphs and rows; one that holds none of the base's is
    // new, and one that holds no paragraph with an id at all is matched by its place, as anything without
    // a key is.
    static List<(string Key, XElement Element)> Keys(XElement parent, List<(string Key, XElement Element)>? @base, string side)
    {
        var keyed = new List<(string Key, XElement Element)>();
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        var claimed = new HashSet<string>(StringComparer.Ordinal);
        foreach (var child in parent.Elements())
        {
            var key = Key(child, @base, side, keyed.Count, claimed);
            var count = counts.GetValueOrDefault(key);
            counts[key] = count + 1;
            if (count > 0 ||
                key.EndsWith('#'))
            {
                key += count.ToString(CultureInfo.InvariantCulture);
            }

            keyed.Add((key, child));
        }

        return keyed;
    }

    static string Key(XElement child, List<(string Key, XElement Element)>? @base, string side, int index, HashSet<string> claimed)
    {
        if (keys.TryGetValue(child.Name, out var attribute) &&
            (string?) child.Attribute(attribute) is { } value)
        {
            // Part names and extensions are not case-sensitive.
            if (child.Name.Namespace == ContentTypes)
            {
                value = value.ToLowerInvariant();
            }

            return $"{child.Name}|{value}";
        }

        if (!held.Contains(child.Name))
        {
            return $"{child.Name}#";
        }

        var ids = HeldIds(child);
        if (ids.Count == 0)
        {
            return $"{child.Name}#";
        }

        if (@base == null)
        {
            return $"{child.Name}|{side}{index}";
        }

        string? best = null;
        var most = 0;
        foreach (var (key, original) in @base)
        {
            if (original.Name != child.Name ||
                claimed.Contains(key))
            {
                continue;
            }

            var shared = HeldIds(original).Count(ids.Contains);
            if (shared > most)
            {
                most = shared;
                best = key;
            }
        }

        if (best == null)
        {
            return $"{child.Name}|{side}{index}";
        }

        claimed.Add(best);
        return best;
    }

    static HashSet<string> HeldIds(XElement element) =>
        Blocks(element)
            .Select(_ => (string?) _.Attribute(ParaId))
            .OfType<string>()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
}
