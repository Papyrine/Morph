[assembly: ParallelLimiter<SingleThreaded>]

/// <summary>
/// Forces tests to run one at a time. The tests in this project mutate process-wide
/// static settings on <see cref="DefaultFontSettings"/>, so parallel execution would
/// let one test's mutation leak into another.
/// </summary>
public class SingleThreaded : IParallelLimit
{
    public int Limit => 1;
}
