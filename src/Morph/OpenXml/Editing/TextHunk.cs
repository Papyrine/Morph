/// <summary>
/// One difference between two texts: <see cref="OldLength"/> characters of the first from
/// <see cref="OldStart"/> were replaced by <see cref="NewLength"/> of the second from
/// <see cref="NewStart"/>.
/// </summary>
readonly record struct TextHunk(int OldStart, int OldLength, int NewStart, int NewLength);
