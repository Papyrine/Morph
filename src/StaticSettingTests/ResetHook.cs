/// <summary>
/// Resets <see cref="DefaultFontSettings"/> (including the internal renderOccurred flag)
/// between every test in this assembly so each test starts from a clean default state.
/// Pairs with the assembly-level <c>ParallelLimiter&lt;SingleThreaded&gt;</c> — since tests
/// mutate process-wide static state, they must run one at a time.
/// </summary>
public static class ResetHook
{
    [BeforeEvery(Test)]
    public static void Reset() => DefaultFontSettings.ResetToDefault();
}
