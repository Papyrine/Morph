// Each case's markup diff is a file of its own under Results/markup, named for the case: its id with every
// run of characters a file name cannot carry made "_". Ids that differ only in those characters, or only in
// case (the file system ignores it), would share a file, each case overwriting the one before (A-11's
// leaders "* ", "- " and "> " all came out as A-11_leader_), so those names take a short hash of the
// whole id as well. A name no other case shares stays readable as it was.
static class MarkupFiles
{
    static readonly Lazy<HashSet<string>> shared = new(() =>
        Catalog.Families
            .SelectMany(_ => _.Scenarios())
            .SelectMany(CaseExpander.Expand)
            .GroupBy(_ => Readable(_.Id), StringComparer.OrdinalIgnoreCase)
            .Where(_ => _.Count() > 1)
            .Select(_ => _.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase));

    /// <summary>The diff's path under Results, as a result records it.</summary>
    public static string PathOf(string caseId) =>
        $"markup/{NameOf(caseId)}";

    static string NameOf(string caseId)
    {
        var name = Readable(caseId);
        if (!shared.Value.Contains(name))
        {
            return $"{name}.diff";
        }

        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(caseId)))[..8];
        return $"{name}_{hash}.diff";
    }

    static string Readable(string caseId) =>
        Regex.Replace(caseId, "[^A-Za-z0-9._-]+", "_");

    /// <summary>Writes the case's diff, or deletes the one an earlier run left when this one changed nothing.</summary>
    public static string? Write(string caseId, string? diff)
    {
        var path = Path.Combine(Paths.Results, PathOf(caseId));
        if (diff == null)
        {
            File.Delete(path);
            return null;
        }

        Directory.CreateDirectory(Paths.Markup);
        File.WriteAllText(path, diff);
        return PathOf(caseId);
    }

    /// <summary>Deletes every diff no recorded case names: one a renamed or a removed case left behind.</summary>
    public static void Prune()
    {
        if (!Directory.Exists(Paths.Markup))
        {
            return;
        }

        var named = ResultStore.LoadAll()
            .SelectMany(_ => _.Cases.Values)
            .Select(_ => _.Observation?.MarkupDiff)
            .OfType<string>()
            .Select(_ => Path.GetFullPath(Path.Combine(Paths.Results, _)))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var pruned = 0;
        foreach (var file in Directory.EnumerateFiles(Paths.Markup, "*.diff"))
        {
            if (!named.Contains(Path.GetFullPath(file)))
            {
                File.Delete(file);
                pruned++;
            }
        }

        if (pruned > 0)
        {
            Log.Info($"Pruned {pruned} markup diffs no case names.");
        }
    }
}
