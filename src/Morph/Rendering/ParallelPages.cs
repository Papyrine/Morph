/// <summary>
/// Paints the pages of a laid-out document on several threads at once and hands them to the caller in
/// page order (<see cref="ImageExportOptions.MaxDegreeOfParallelism"/>).
///
/// Painting is the cheap half to split: the tree is already paginated, so a page's pixels depend on
/// nothing but its own items. What is not shareable is the render context — its font, paint and image
/// caches are plain dictionaries, and the reusable paints it hands out are mutated per draw — so each
/// worker creates its own and disposes it when done. Pages are encoded into memory as they finish and
/// delivered in order once all are painted; the serial path still streams one page at a time.
/// </summary>
static class ParallelPages
{
    public static void Paint<TContext>(
        IReadOnlyList<LaidOutPage> pages,
        int degreeOfParallelism,
        Func<TContext> createContext,
        Action<TContext, LaidOutPage, Action<Action<Stream>>> paintPage,
        Action<Action<Stream>> pageCallback)
        where TContext : IDisposable
    {
        var encoded = new byte[pages.Count][];
        Parallel.For(
            0,
            pages.Count,
            new ParallelOptions { MaxDegreeOfParallelism = degreeOfParallelism },
            createContext,
            (index, _, context) =>
            {
                paintPage(
                    context,
                    pages[index],
                    write =>
                    {
                        using var buffer = new MemoryStream();
                        write(buffer);
                        encoded[index] = buffer.ToArray();
                    });
                return context;
            },
            _ => _.Dispose());

        foreach (var page in encoded)
        {
            // A page the painter chose not to emit leaves its slot empty.
            if (page != null)
            {
                pageCallback(_ => _.Write(page));
            }
        }
    }
}
