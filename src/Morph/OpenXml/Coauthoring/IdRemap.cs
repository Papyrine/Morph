using static MergeNames;

/// <summary>
/// Ids both edits made afresh. The editor gives a new list, note, comment, bookmark, picture or
/// relationship the next free number in the file it edits, and a new part the next free name, so two
/// people editing at once give theirs the same one. Before the merge, mine's are given others and every
/// reference to them in mine follows; theirs are never touched, so nobody's open paragraph changes under
/// them. Ids both made for the same thing (two equal list definitions, one relationship to one part)
/// are left as they are, to merge as one.
///
/// A tracked change's <c>w:id</c> is not remapped: the editor finds revisions by their place, not their
/// id, and Word opens a file in which two share one.
/// </summary>
static class IdRemap
{
    /// <summary>Remaps mine's, and says whether it remapped any.</summary>
    public static bool Remap(PackageParts @base, PackageParts mine, PackageParts theirs)
    {
        var remapped = Parts(@base, mine, theirs);
        remapped |= RelationshipIds(@base, mine, theirs);
        remapped |= Numbering(@base, mine, theirs);
        remapped |= Notes(@base, mine, theirs, "footnotes", "footnote", "footnoteReference");
        remapped |= Notes(@base, mine, theirs, "endnotes", "endnote", "endnoteReference");
        remapped |= Comments(@base, mine, theirs);
        remapped |= Bookmarks(@base, mine, theirs);
        remapped |= DrawingIds(@base, mine, theirs);
        remapped |= Paragraphs(@base, mine, theirs);
        remapped |= DurableIds(@base, mine, theirs);
        return remapped;
    }

    // A part both made under one name, with other content: mine's takes the next free name. Parts a
    // document has one of are merged instead, and relationships parts go with their part.
    static bool Parts(PackageParts @base, PackageParts mine, PackageParts theirs)
    {
        var renamed = false;
        foreach (var name in mine.Names.ToList())
        {
            if (@base.Has(name) ||
                !theirs.Has(name) ||
                name == "[Content_Types].xml" ||
                name.EndsWith(".rels", StringComparison.OrdinalIgnoreCase) ||
                mine.Bytes(name).AsSpan().SequenceEqual(theirs.Bytes(name)) ||
                mine.Root(name) is { } root && SingleInstance.Contains(root.Name))
            {
                continue;
            }

            Rename(mine, name, FreshName(name, @base, mine, theirs));
            renamed = true;
        }

        return renamed;
    }

    static void Rename(PackageParts mine, string from, string to)
    {
        mine.Rename(from, to);
        var relationshipsOfPart = PackageParts.RelationshipsOf(from);
        if (mine.Has(relationshipsOfPart))
        {
            mine.Rename(relationshipsOfPart, PackageParts.RelationshipsOf(to));
        }

        foreach (var relationships in RelationshipsParts(mine))
        {
            var changed = false;
            foreach (var relationship in mine.Root(relationships)!.Elements(Relationships + "Relationship"))
            {
                if (string.Equals(PackageParts.Target(relationships, relationship), from, StringComparison.OrdinalIgnoreCase))
                {
                    relationship.SetAttributeValue("Target", PackageParts.RelativeTarget(relationships, to));
                    changed = true;
                }
            }

            if (changed)
            {
                mine.Changed(relationships);
            }
        }

        if (mine.Root("[Content_Types].xml") is { } types)
        {
            foreach (var entry in types.Elements(ContentTypes + "Override"))
            {
                if (string.Equals((string?) entry.Attribute("PartName"), "/" + from, StringComparison.OrdinalIgnoreCase))
                {
                    entry.SetAttributeValue("PartName", "/" + to);
                    mine.Changed("[Content_Types].xml");
                }
            }
        }
    }

    // word/media/image3.png becomes the first of image4.png, image5.png… none of the three has.
    static string FreshName(string name, params PackageParts[] packages)
    {
        var slash = name.LastIndexOf('/');
        var folder = name[..(slash + 1)];
        var file = name[(slash + 1)..];
        var dot = file.LastIndexOf('.');
        var extension = dot < 0 ? "" : file[dot..];
        var stem = file.AsSpan(0, dot < 0 ? file.Length : dot).TrimEnd("0123456789");
        for (var number = 1; ; number++)
        {
            var fresh = $"{folder}{stem}{number}{extension}";
            if (packages.All(_ => !_.Names.Any(existing => string.Equals(existing, fresh, StringComparison.OrdinalIgnoreCase))))
            {
                return fresh;
            }
        }
    }

    static List<string> RelationshipsParts(PackageParts package) =>
        [.. package.Names.Where(_ => _.EndsWith(".rels", StringComparison.OrdinalIgnoreCase) && package.Root(_) != null)];

    // A relationship mine made under an id theirs made another under takes a fresh one; one mine made to
    // the same part theirs made one to (each adding the styles, say) takes theirs' id.
    static bool RelationshipIds(PackageParts @base, PackageParts mine, PackageParts theirs)
    {
        var remapped = false;
        foreach (var relationships in RelationshipsParts(mine))
        {
            if (theirs.Root(relationships) is not { } others)
            {
                continue;
            }

            var ours = mine.Root(relationships)!;
            var original = Relations(@base.Root(relationships)).Select(Id).ToHashSet(StringComparer.Ordinal);
            var theirsNew = Relations(others).Where(_ => !original.Contains(Id(_))).ToList();
            var used = Relations(ours).Concat(Relations(others)).Concat(Relations(@base.Root(relationships)))
                .Select(Id)
                .ToHashSet(StringComparer.Ordinal);
            var renames = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var relationship in Relations(ours))
            {
                var id = Id(relationship);
                if (original.Contains(id))
                {
                    continue;
                }

                var same = theirsNew.FirstOrDefault(_ => Same(relationships, _, relationship));
                if (same != null)
                {
                    if (Id(same) != id)
                    {
                        renames[id] = Id(same);
                    }

                    continue;
                }

                if (theirsNew.Any(_ => Id(_) == id))
                {
                    var fresh = FreshRelationshipId(used);
                    used.Add(fresh);
                    renames[id] = fresh;
                }
            }

            if (renames.Count == 0)
            {
                continue;
            }

            foreach (var relationship in Relations(ours))
            {
                if (renames.TryGetValue(Id(relationship), out var to))
                {
                    relationship.SetAttributeValue("Id", to);
                }
            }

            mine.Changed(relationships);
            var source = PackageParts.Source(relationships);
            if (source.Length > 0 &&
                mine.Root(source) is { } root)
            {
                foreach (var attribute in root.DescendantsAndSelf().Attributes())
                {
                    if (attribute.Name.Namespace == R &&
                        renames.TryGetValue(attribute.Value, out var to))
                    {
                        attribute.Value = to;
                    }
                }

                mine.Changed(source);
            }

            remapped = true;
        }

        return remapped;

        static string Id(XElement relationship) =>
            (string?) relationship.Attribute("Id") ?? "";
    }

    static IEnumerable<XElement> Relations(XElement? root) =>
        root?.Elements(Relationships + "Relationship") ?? [];

    static bool Same(string relationships, XElement first, XElement second) =>
        (string?) first.Attribute("Type") == (string?) second.Attribute("Type") &&
        (string?) first.Attribute("TargetMode") == (string?) second.Attribute("TargetMode") &&
        string.Equals(PackageParts.Target(relationships, first) ?? (string?) first.Attribute("Target"),
            PackageParts.Target(relationships, second) ?? (string?) second.Attribute("Target"),
            StringComparison.OrdinalIgnoreCase);

    static string FreshRelationshipId(HashSet<string> used)
    {
        var highest = used
            .Where(_ => _.StartsWith("rId", StringComparison.Ordinal))
            .Select(_ => Number(_[3..]))
            .DefaultIfEmpty(0)
            .Max();
        return $"rId{highest + 1}";
    }

    // The number an id is, or 0 where it is not one.
    static int Number(string id)
    {
        if (int.TryParse(id, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var number))
        {
            return number;
        }

        return 0;
    }

    // A list definition or a list mine made under the number theirs made another under takes the next
    // free number, and its references in mine follow. A renumbered definition gets a new nsid too, which
    // Word tells lists apart by.
    static bool Numbering(PackageParts @base, PackageParts mine, PackageParts theirs)
    {
        if (mine.WithRoot(W + "numbering") is not { } name ||
            theirs.Root(name) is not { } others ||
            others.Name != W + "numbering")
        {
            return false;
        }

        var ours = mine.Root(name)!;
        var original = @base.Root(name);

        // The definitions first: a list is told apart from theirs by the definition it points at.
        var abstracts = Collisions(original, ours, others, W + "abstractNum", W + "abstractNumId");
        foreach (var definition in ours.Elements(W + "abstractNum"))
        {
            if (abstracts.TryGetValue((string?) definition.Attribute(W + "abstractNumId") ?? "", out var to))
            {
                definition.SetAttributeValue(W + "abstractNumId", to);
                definition.Element(W + "nsid")?.SetAttributeValue(Val, Random.Shared.Next(1, int.MaxValue).ToString("X8", CultureInfo.InvariantCulture));
            }
        }

        foreach (var list in ours.Elements(W + "num"))
        {
            if (list.Element(W + "abstractNumId") is { } reference &&
                abstracts.TryGetValue((string?) reference.Attribute(Val) ?? "", out var definition))
            {
                reference.SetAttributeValue(Val, definition);
            }
        }

        var lists = Collisions(original, ours, others, W + "num", W + "numId");
        foreach (var list in ours.Elements(W + "num"))
        {
            if (lists.TryGetValue((string?) list.Attribute(W + "numId") ?? "", out var to))
            {
                list.SetAttributeValue(W + "numId", to);
            }
        }

        if (abstracts.Count == 0 &&
            lists.Count == 0)
        {
            return false;
        }

        mine.Changed(name);
        if (lists.Count > 0)
        {
            var parts = mine.WithRoot(Stories).Append(mine.WithRoot(W + "styles")).OfType<string>();
            Rewrite(mine, parts, W + "numId", Val, lists);
        }

        return true;
    }

    // A footnote or endnote mine made under the number theirs made another under takes the next free one.
    static bool Notes(PackageParts @base, PackageParts mine, PackageParts theirs, string root, string note, string reference)
    {
        if (mine.WithRoot(W + root) is not { } name ||
            theirs.Root(name) is not { } others ||
            others.Name != W + root)
        {
            return false;
        }

        var renames = Collisions(@base.Root(name), mine.Root(name)!, others, W + note, Id);
        if (renames.Count == 0)
        {
            return false;
        }

        Rewrite(mine, [name], W + note, Id, renames);
        Rewrite(mine, mine.WithRoot(Stories), W + reference, Id, renames);
        return true;
    }

    // A comment mine made under the number theirs made another under takes the next free one, past the
    // range marks too, as the review numbers them.
    static bool Comments(PackageParts @base, PackageParts mine, PackageParts theirs)
    {
        if (mine.WithRoot(W + "comments") is not { } name ||
            theirs.Root(name) is not { } others ||
            others.Name != W + "comments")
        {
            return false;
        }

        var ours = mine.Root(name)!;
        var original = @base.Root(name);
        var marks = new[] { "commentRangeStart", "commentRangeEnd", "commentReference" };
        var used = new[] { @base, mine, theirs }
            .SelectMany(_ => _.WithRoot(Stories).Select(_.Root))
            .OfType<XElement>()
            .SelectMany(_ => _.Descendants())
            .Where(_ => _.Name == W + "comment" || marks.Any(mark => _.Name == W + mark))
            .Select(_ => (string?) _.Attribute(Id))
            .OfType<string>();
        var renames = Collisions(original, ours, others, W + "comment", Id, used);
        if (renames.Count == 0)
        {
            return false;
        }

        Rewrite(mine, [name], W + "comment", Id, renames);
        foreach (var mark in marks)
        {
            Rewrite(mine, mine.WithRoot(Stories), W + mark, Id, renames);
        }

        return true;
    }

    // A bookmark mine made under the number theirs made another under takes the next free one, at both
    // its ends.
    static bool Bookmarks(PackageParts @base, PackageParts mine, PackageParts theirs)
    {
        var renames = Collisions(
            AllIds(@base, W + "bookmarkStart", Id),
            AllIds(mine, W + "bookmarkStart", Id),
            AllIds(theirs, W + "bookmarkStart", Id),
            Numbered);
        if (renames.Count == 0)
        {
            return false;
        }

        Rewrite(mine, mine.WithRoot(Stories), W + "bookmarkStart", Id, renames);
        Rewrite(mine, mine.WithRoot(Stories), W + "bookmarkEnd", Id, renames);
        return true;
    }

    // A picture mine put in under the drawing id theirs put another in under takes the next free one.
    static bool DrawingIds(PackageParts @base, PackageParts mine, PackageParts theirs)
    {
        var renames = Collisions(
            AllIds(@base, Wp + "docPr", "id"),
            AllIds(mine, Wp + "docPr", "id"),
            AllIds(theirs, Wp + "docPr", "id"),
            Numbered);
        if (renames.Count == 0)
        {
            return false;
        }

        Rewrite(mine, mine.WithRoot(Stories), Wp + "docPr", "id", renames);
        return true;
    }

    // A paragraph mine made under the id theirs made another under takes a fresh random one, and the
    // comment threads that name it by that id follow.
    static bool Paragraphs(PackageParts @base, PackageParts mine, PackageParts theirs)
    {
        var renames = Collisions(
            AllIds(@base, W + "p", ParaId).Concat(AllIds(@base, W + "tr", ParaId)),
            AllIds(mine, W + "p", ParaId).Concat(AllIds(mine, W + "tr", ParaId)),
            AllIds(theirs, W + "p", ParaId).Concat(AllIds(theirs, W + "tr", ParaId)),
            RandomHex);
        if (renames.Count == 0)
        {
            return false;
        }

        Rewrite(mine, mine.WithRoot(Stories), W + "p", ParaId, renames);
        Rewrite(mine, mine.WithRoot(Stories), W + "tr", ParaId, renames);
        if (mine.WithRoot(W15 + "commentsEx") is { } extended)
        {
            Rewrite(mine, [extended], W15 + "commentEx", W15 + "paraId", renames);
            Rewrite(mine, [extended], W15 + "commentEx", W15 + "paraIdParent", renames);
        }

        if (mine.WithRoot(W16Cid + "commentsIds") is { } ids)
        {
            Rewrite(mine, [ids], W16Cid + "commentId", W16Cid + "paraId", renames);
        }

        return true;
    }

    // A comment's durable id mine made that theirs made for another takes a fresh random one.
    static bool DurableIds(PackageParts @base, PackageParts mine, PackageParts theirs)
    {
        var renames = Collisions(
            AllIds(@base, W16Cid + "commentId", W16Cid + "durableId"),
            AllIds(mine, W16Cid + "commentId", W16Cid + "durableId"),
            AllIds(theirs, W16Cid + "commentId", W16Cid + "durableId"),
            RandomHex);
        if (renames.Count == 0)
        {
            return false;
        }

        if (mine.WithRoot(W16Cid + "commentsIds") is { } ids)
        {
            Rewrite(mine, [ids], W16Cid + "commentId", W16Cid + "durableId", renames);
        }

        if (mine.WithRoot(W16Cex + "commentsExtensible") is { } extensible)
        {
            Rewrite(mine, [extensible], W16Cex + "commentExtensible", W16Cex + "durableId", renames);
        }

        return true;
    }

    // The ids of the elements named, in every part that holds paragraphs.
    static List<string> AllIds(PackageParts package, XName element, XName attribute)
    {
        var parts = package.WithRoot(Stories)
            .Concat(package.WithRoot(new HashSet<XName> { W16Cid + "commentsIds" }));
        return
        [
            .. parts
                .Select(package.Root)
                .OfType<XElement>()
                .SelectMany(_ => _.Descendants(element))
                .Select(_ => (string?) _.Attribute(attribute))
                .OfType<string>()
        ];
    }

    // Ids both made, each mapped to a fresh one.
    static Dictionary<string, string> Collisions(IEnumerable<string> original, IEnumerable<string> ours, IEnumerable<string> others, Func<HashSet<string>, string> fresh)
    {
        var before = original.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var mineIds = ours.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var theirsIds = others.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var theirsNew = theirsIds.Where(_ => !before.Contains(_)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var used = before.Concat(mineIds).Concat(theirsIds).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var renames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var id in mineIds)
        {
            if (before.Contains(id) ||
                !theirsNew.Contains(id))
            {
                continue;
            }

            var to = fresh(used);
            used.Add(to);
            renames[id] = to;
        }

        return renames;
    }

    // Children both made under one id with other content, each mapped to the next free number.
    static Dictionary<string, string> Collisions(XElement? original, XElement ours, XElement others, XName element, XName attribute, IEnumerable<string>? alsoUsed = null)
    {
        var theirs = others.Elements(element)
            .Where(_ => (string?) _.Attribute(attribute) != null)
            .GroupBy(_ => (string) _.Attribute(attribute)!)
            .ToDictionary(_ => _.Key, _ => _.First());
        var before = (original?.Elements(element) ?? [])
            .Select(_ => (string?) _.Attribute(attribute))
            .OfType<string>()
            .ToHashSet();
        var used = before
            .Concat(theirs.Keys)
            .Concat(ours.Elements(element).Select(_ => (string?) _.Attribute(attribute)).OfType<string>())
            .Concat(alsoUsed ?? [])
            .ToHashSet();
        var renames = new Dictionary<string, string>();
        foreach (var child in ours.Elements(element))
        {
            if ((string?) child.Attribute(attribute) is not { } id ||
                before.Contains(id) ||
                !theirs.TryGetValue(id, out var other) ||
                XNode.DeepEquals(child, other))
            {
                continue;
            }

            var to = Numbered(used);
            used.Add(to);
            renames[id] = to;
        }

        return renames;
    }

    static string Numbered(HashSet<string> used)
    {
        var highest = used
            .Select(Number)
            .DefaultIfEmpty(0)
            .Max();
        return (highest + 1).ToString(CultureInfo.InvariantCulture);
    }

    static string RandomHex(HashSet<string> used)
    {
        while (true)
        {
            var id = Random.Shared.Next(1, int.MaxValue).ToString("X8", CultureInfo.InvariantCulture);
            if (!used.Contains(id))
            {
                return id;
            }
        }
    }

    // Sets the attribute of every element of that name in the parts that has a renamed value.
    static void Rewrite(PackageParts package, IEnumerable<string> parts, XName element, XName attribute, Dictionary<string, string> renames)
    {
        foreach (var part in parts.ToList())
        {
            if (package.Root(part) is not { } root)
            {
                continue;
            }

            var changed = false;
            foreach (var found in root.DescendantsAndSelf(element))
            {
                if ((string?) found.Attribute(attribute) is { } value &&
                    renames.TryGetValue(value, out var to))
                {
                    found.SetAttributeValue(attribute, to);
                    changed = true;
                }
            }

            if (changed)
            {
                package.Changed(part);
            }
        }
    }
}
