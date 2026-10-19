/// <summary>
/// A stretch of pasted text with one look: its emphasis (<see cref="Format"/> sets only what the source
/// turned on, the rest following the text it is pasted into) and the address it links to, if any.
/// </summary>
readonly record struct PastedRun(string Text, RunFormat Format = default, string? Link = null);
