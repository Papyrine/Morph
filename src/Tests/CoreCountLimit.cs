[assembly: ParallelLimiter<CoreCountLimit>]

/// <summary>
/// Caps concurrent tests at the core count. TUnit's auto-detected default is four times that,
/// which measurably oversubscribes this suite: its heavy tests are page rasterisation and
/// out-of-process Chromium screenshots, so 48 in flight on 12 cores spends the extra slots
/// contending for CPU and memory rather than covering latency.
///
/// Measured on a 12-core host, full suite, wall clock. Back-to-back sweep:
///
///     limit 12  3m05s      limit 20  3m05s      limit 48 (default)  3m48s
///     limit 16  3m08s      limit 24  3m12s
///
/// Anything from 1x to 2x cores is flat and the default is the outlier. Absolute times drift
/// a lot with machine conditions — the same configuration measured anywhere from 3m05s to
/// 3m47s across one session — so only compare runs made back to back. A later control pair
/// put the default at 3m57s against 3m37s, i.e. the same direction at 9% rather than 23%.
/// Take the win as real but its size as anywhere in that range.
///
/// This sets the low end of the flat band, where memory pressure is lowest — with 48 in
/// flight the run held dozens of multi-megabyte page bitmaps and Chromium instances at once.
///
/// The floor keeps a small CI runner from serialising down to its own core count, since a
/// meaningful share of the work happens in Playwright's browser processes rather than in
/// these threads. Only the 12-core numbers above are measured; the floor is a judgement call.
/// </summary>
public class CoreCountLimit : IParallelLimit
{
    public int Limit { get; } = Math.Max(8, Environment.ProcessorCount);
}
