using System.Xml.Linq;
using static MergeNames;

/// <summary>What became of an edit merged with the others': kept as it was, kept rebased, or refused.</summary>
/// <param name="Document">The merged file; null when refused.</param>
/// <param name="Rebased">Whether the merged file differs from mine: theirs changed meanwhile, or mine's ids were remapped.</param>
/// <param name="Reason">Why it was refused.</param>
/// <param name="Paragraph">The id of the paragraph in the way, where there is one.</param>
/// <param name="Locked">Whether it was refused for a paragraph someone else has open.</param>
sealed record MergeOutcome(byte[]? Document, bool Rebased, string? Reason = null, string? Paragraph = null, bool Locked = false)
{
    public bool Refused => Document == null;

    public static MergeOutcome Kept(byte[] document) =>
        new(document, false);

    public static MergeOutcome Refuse(string reason, string? paragraph = null, bool locked = false) =>
        new(null, false, reason, paragraph, locked);
}

/// <summary>
/// The three-way merge of two edits made to one version of a Word document: <c>mine</c>, an edit one
/// person made, and <c>theirs</c>, the document as the others' edits have left it since. Coauthoring
/// merges every edit committed against a version that is no longer the latest, and the editor merges to
/// undo an edit others have edited around.
///
/// Part by part: a part only one side changed is that side's, and a part both changed is merged by
/// <see cref="KeyedMerge"/>, paragraph by paragraph. Ids both sides made afresh are remapped in mine
/// first (<see cref="IdRemap"/>). An edit is taken whole or refused whole: one that changed a paragraph
/// theirs changed too, or deleted one theirs changed, or changed a paragraph someone else has open, is
/// refused, since an edit that changed two paragraphs (Enter, a join) is only right with both.
/// </summary>
static class DocumentMerge
{
    public static MergeOutcome Merge(byte[] @base, byte[] mine, byte[] theirs, IReadOnlySet<string>? locked = null)
    {
        try
        {
            var original = new PackageParts(@base);
            var ours = new PackageParts(mine);
            if (locked is {Count: > 0} &&
                Changed(original, ours).FirstOrDefault(locked.Contains) is { } held)
            {
                return MergeOutcome.Refuse("Someone else is editing that paragraph.", held, locked: true);
            }

            if (ReferenceEquals(@base, theirs))
            {
                return MergeOutcome.Kept(mine);
            }

            var others = new PackageParts(theirs);
            if (Same(original, others))
            {
                return MergeOutcome.Kept(mine);
            }

            IdRemap.Remap(original, ours, others);
            var entries = new OrderedDictionary<string, byte[]>(StringComparer.Ordinal);
            foreach (var name in others.Names.Concat(ours.Names).Distinct(StringComparer.Ordinal).ToList())
            {
                if (Entry(name, original, ours, others) is { } merged)
                {
                    entries[name] = merged;
                }
            }

            ContentTypes(entries);
            var document = PackageEntries.Write(entries);
            Open(document);
            return new(document, true);
        }
        catch (MergeConflict conflict)
        {
            return MergeOutcome.Refuse(conflict.Message, conflict.Paragraph);
        }
        catch (Exception exception) when (exception is InvalidDataException or XmlException or OpenXmlPackageException or InvalidOperationException)
        {
            return MergeOutcome.Refuse($"The edits could not be merged: {exception.Message}");
        }
    }

    /// <summary>
    /// The ids of the paragraphs <paramref name="mine"/> changed or deleted from <paramref name="base"/>,
    /// in every part that holds paragraphs: what the paragraphs others have open are checked against.
    /// </summary>
    public static IEnumerable<string> Changed(byte[] @base, byte[] mine) =>
        Changed(new PackageParts(@base), new PackageParts(mine));

    static IEnumerable<string> Changed(PackageParts @base, PackageParts mine)
    {
        foreach (var name in @base.WithRoot(Stories))
        {
            var before = ParagraphsById(@base.Root(name)!);
            var after = mine.Root(name) is { } root ? ParagraphsById(root) : [];
            foreach (var (id, paragraph) in before)
            {
                if (!after.TryGetValue(id, out var edited) ||
                    !XNode.DeepEquals(paragraph, edited))
                {
                    yield return id;
                }
            }
        }
    }

    static bool Same(PackageParts first, PackageParts second) =>
        first.Names.Count() == second.Names.Count() &&
        first.Names.All(_ => second.Has(_) && first.Bytes(_).AsSpan().SequenceEqual(second.Bytes(_)));

    // An entry as merged: the side that changed it, or both merged; null where it is gone.
    static byte[]? Entry(string name, PackageParts @base, PackageParts mine, PackageParts theirs)
    {
        var original = @base.Bytes(name);
        var ours = mine.Bytes(name);
        var others = theirs.Bytes(name);
        if (Equal(ours, original))
        {
            return others;
        }

        if (Equal(others, original) ||
            Equal(ours, others))
        {
            return ours;
        }

        if (ours == null ||
            others == null)
        {
            // One deleted the part, the other changed it.
            throw new MergeConflict($"One edit removed {name}, which the other changed.");
        }

        if (mine.Xml(name) is not { Root: { } mineRoot } ||
            theirs.Xml(name) is not { Root: { } theirsRoot } xml)
        {
            throw new MergeConflict($"Both edits changed {name}.");
        }

        var merged = KeyedMerge.Merge(@base.Root(name), mineRoot, theirsRoot);
        return PackageParts.Serialize(new(xml.Declaration, merged));
    }

    static bool Equal(byte[]? first, byte[]? second)
    {
        if (first == null ||
            second == null)
        {
            return first == second;
        }

        return first.AsSpan().SequenceEqual(second);
    }

    // The content types say nothing of a part the merge left out.
    static void ContentTypes(OrderedDictionary<string, byte[]> entries)
    {
        const string name = "[Content_Types].xml";
        if (!entries.TryGetValue(name, out var bytes) ||
            PackageParts.Parse(bytes) is not { Root: { } root } xml)
        {
            return;
        }

        var parts = entries.Keys.Select(_ => "/" + _).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var dangling = root.Elements(MergeNames.ContentTypes + "Override")
            .Where(_ => !parts.Contains((string?) _.Attribute("PartName") ?? ""))
            .ToList();
        if (dangling.Count == 0)
        {
            return;
        }

        dangling.ForEach(_ => _.Remove());
        entries[name] = PackageParts.Serialize(xml);
    }

    // The merged file opens, and its body reads: a merge that broke the package is refused rather than
    // handed to everyone.
    static void Open(byte[] document)
    {
        using var stream = new MemoryStream(document, writable: false);
        using var package = WordprocessingDocument.Open(stream, false);
        _ = package.MainDocumentPart?.Document?.Body ?? throw new InvalidDataException("The merged file has no body.");
    }
}
