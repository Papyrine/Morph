/// <summary>
/// Turns the layout and painting loops' page counts into <see cref="ExportProgress"/> reports for an
/// options record's <c>OnProgress</c>. Shared by the PDF renderer and both raster backends so every
/// paginated export reports the same sequence.
/// </summary>
static class ProgressReports
{
    // Fragmenter.Layout's onPage, reporting each page laid out with no total; null when nobody listens,
    // so the flow pays nothing.
    public static Action<int>? Layout(Action<ExportProgress>? onProgress)
    {
        if (onProgress == null)
        {
            return null;
        }

        return _ => onProgress(new(ExportStage.Layout, _, null));
    }

    // Reports each page painted as page x of total; null when nobody listens.
    public static Painting? Paint(Action<ExportProgress>? onProgress, int total)
    {
        if (onProgress == null)
        {
            return null;
        }

        return new(onProgress, total);
    }

    /// <summary>
    /// Counts pages as they are painted. Safe to call from several painting threads: the count and the
    /// call are taken under one lock, so the host's callback never runs twice at once and the page
    /// numbers it sees only rise.
    /// </summary>
    public sealed class Painting(Action<ExportProgress> onProgress, int total)
    {
        readonly System.Threading.Lock gate = new();
        int painted;

        public void Painted()
        {
            lock (gate)
            {
                painted++;
                onProgress(new(ExportStage.Paint, painted, total));
            }
        }
    }
}
