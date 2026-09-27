/// <summary>
/// What changed between two texts, as the stretches that differ. An editor hands back a paragraph as
/// it now reads, not the keys that were pressed, so which characters are new and which went is worked
/// out here — the fewest that account for the difference (Myers), then tidied so that a change reads
/// as the edit a person made rather than as the letters two words happen to share.
/// </summary>
static class TextDiff
{
    // Past this many cells the search for the fewest changes costs more than it is worth, and what
    // lies between the text's common start and its common end is taken as replaced whole.
    const long searchLimit = 4_000_000;

    const int maximumRounds = 1500;

    // Characters left standing between two changes are part of the change unless there are more of
    // them than this: "quick" rewritten as "quiet" is one word replaced, not "ck" swapped for "et".
    const int minimumKept = 3;

    /// <summary>
    /// The stretches of <paramref name="before"/> that were replaced and what replaced each in
    /// <paramref name="after"/>, in order. Either length of a hunk can be zero: an insertion replaces
    /// nothing, and a deletion is replaced by nothing.
    ///
    /// Given a kind for each character of each text, a character is only the same as one of its
    /// own kind. Two spaces are one space to a search by character, and which of them was typed
    /// and which was there is then a toss-up; the run each belongs to settles it.
    /// </summary>
    public static List<TextHunk> Compare(string before, string after, IReadOnlyList<int>? beforeKinds = null, IReadOnlyList<int>? afterKinds = null)
    {
        var was = Symbols(before, beforeKinds);
        var now = Symbols(after, afterKinds);
        var start = 0;
        var limit = Math.Min(was.Length, now.Length);
        while (start < limit && was[start] == now[start])
        {
            start++;
        }

        var endBefore = was.Length;
        var endAfter = now.Length;
        while (endBefore > start && endAfter > start && was[endBefore - 1] == now[endAfter - 1])
        {
            endBefore--;
            endAfter--;
        }

        var hunks = new List<TextHunk>();
        if (endBefore == start && endAfter == start)
        {
            return hunks;
        }

        var lengthBefore = endBefore - start;
        var lengthAfter = endAfter - start;
        if (lengthBefore == 0 ||
            lengthAfter == 0 ||
            (long) lengthBefore * lengthAfter > searchLimit)
        {
            hunks.Add(new(start, lengthBefore, start, lengthAfter));
        }
        else
        {
            Search(was.AsSpan(start, lengthBefore), now.AsSpan(start, lengthAfter), start, hunks);
        }

        return Tidy(hunks, before, after);
    }

    // A character and its kind as one number.
    static long[] Symbols(string text, IReadOnlyList<int>? kinds)
    {
        var symbols = new long[text.Length];
        for (var index = 0; index < text.Length; index++)
        {
            symbols[index] = text[index];
            if (kinds != null)
            {
                symbols[index] |= (long) (kinds[index] + 1) << 16;
            }
        }

        return symbols;
    }

    // Myers' O(ND) search, keeping each round's furthest-reaching paths to walk back along. A round is
    // one more character changed; texts further apart than maximumRounds are not worth the search.
    static void Search(ReadOnlySpan<long> before, ReadOnlySpan<long> after, int offset, List<TextHunk> hunks)
    {
        var lengthBefore = before.Length;
        var lengthAfter = after.Length;
        var maximum = Math.Min(lengthBefore + lengthAfter, maximumRounds);
        var reach = new int[2 * maximum + 2];
        var rounds = new List<int[]>();
        var found = -1;
        for (var round = 0; round <= maximum && found < 0; round++)
        {
            // What the round starts from, on the diagonals it can reach.
            var snapshot = new int[2 * round + 1];
            Array.Copy(reach, maximum - round, snapshot, 0, snapshot.Length);
            rounds.Add(snapshot);
            for (var diagonal = -round; diagonal <= round; diagonal += 2)
            {
                int x;
                if (diagonal == -round ||
                    (diagonal != round && reach[maximum + diagonal - 1] < reach[maximum + diagonal + 1]))
                {
                    x = reach[maximum + diagonal + 1];
                }
                else
                {
                    x = reach[maximum + diagonal - 1] + 1;
                }

                var y = x - diagonal;
                while (x < lengthBefore && y < lengthAfter && before[x] == after[y])
                {
                    x++;
                    y++;
                }

                reach[maximum + diagonal] = x;
                if (x >= lengthBefore && y >= lengthAfter)
                {
                    found = round;
                    break;
                }
            }
        }

        if (found < 0)
        {
            hunks.Add(new(offset, lengthBefore, offset, lengthAfter));
            return;
        }

        // Back from the end, a round at a time: each round took one step off its diagonal — a
        // character deleted or a character inserted — and then ran along it.
        var steps = new List<(int X, int Y, bool Deleted)>();
        var atX = lengthBefore;
        var atY = lengthAfter;
        for (var round = found; round > 0; round--)
        {
            var previous = rounds[round];
            var diagonal = atX - atY;
            int from;
            if (diagonal == -round ||
                (diagonal != round && previous[round + diagonal - 1] < previous[round + diagonal + 1]))
            {
                from = diagonal + 1;
            }
            else
            {
                from = diagonal - 1;
            }

            var fromX = previous[round + from];
            var fromY = fromX - from;
            steps.Add((fromX, fromY, from == diagonal - 1));
            atX = fromX;
            atY = fromY;
        }

        steps.Reverse();
        foreach (var (x, y, deleted) in steps)
        {
            var hunk = deleted ? new TextHunk(offset + x, 1, offset + y, 0) : new TextHunk(offset + x, 0, offset + y, 1);
            if (hunks.Count > 0 &&
                hunks[^1] is var last &&
                last.OldStart + last.OldLength == hunk.OldStart &&
                last.NewStart + last.NewLength == hunk.NewStart)
            {
                hunks[^1] = new(last.OldStart, last.OldLength + hunk.OldLength, last.NewStart, last.NewLength + hunk.NewLength);
            }
            else
            {
                hunks.Add(hunk);
            }
        }
    }

    static List<TextHunk> Tidy(List<TextHunk> hunks, string before, string after)
    {
        // Joined first, so that a letter deleted and a letter typed two letters on are seen as the
        // replacement they are; then grown to whole words, which can bring two changes together.
        var tidied = Joined(hunks);
        for (var index = 0; index < tidied.Count; index++)
        {
            // A hunk grows over what was kept, no further: to where the hunk before it ends, and
            // to where the one after it starts.
            var floor = index > 0 ? tidied[index - 1].OldStart + tidied[index - 1].OldLength : 0;
            var ceiling = index + 1 < tidied.Count ? tidied[index + 1].OldStart : before.Length;
            tidied[index] = Word(tidied[index], before, after, floor, ceiling);
        }

        tidied = Joined(tidied);
        for (var index = 0; index < tidied.Count; index++)
        {
            tidied[index] = Whole(tidied[index], before, after);
        }

        return tidied;
    }

    static List<TextHunk> Joined(List<TextHunk> hunks)
    {
        var joined = new List<TextHunk>();
        foreach (var hunk in hunks)
        {
            if (joined.Count > 0 &&
                joined[^1] is var last &&
                hunk.OldStart - (last.OldStart + last.OldLength) < minimumKept)
            {
                joined[^1] = new(
                    last.OldStart,
                    hunk.OldStart + hunk.OldLength - last.OldStart,
                    last.NewStart,
                    hunk.NewStart + hunk.NewLength - last.NewStart);
            }
            else
            {
                joined.Add(hunk);
            }
        }

        return joined;
    }

    // Text replaced within a word is the word replaced: "lazy" made "sleepy" keeps its y, and "teh"
    // made "the" its t, but nobody rewrote three letters of four. Text only typed, or only deleted,
    // is left as it is — a letter added to a word is a letter added.
    static TextHunk Word(TextHunk hunk, string before, string after, int floor, int ceiling)
    {
        var (oldStart, oldLength, newStart, newLength) = hunk;
        if (oldLength == 0 ||
            newLength == 0)
        {
            return hunk;
        }

        while (oldStart > floor &&
               IsWord(before[oldStart - 1]) &&
               (IsWord(before[oldStart]) || IsWord(after[newStart])))
        {
            oldStart--;
            newStart--;
            oldLength++;
            newLength++;
        }

        while (oldStart + oldLength < ceiling &&
               IsWord(before[oldStart + oldLength]) &&
               (IsWord(before[oldStart + oldLength - 1]) || IsWord(after[newStart + newLength - 1])))
        {
            oldLength++;
            newLength++;
        }

        return new(oldStart, oldLength, newStart, newLength);
    }

    static bool IsWord(char character) =>
        char.IsLetterOrDigit(character) ||
        character is '\'' or '\u2019';

    // A change never parts the two halves of one character: two emoji that share their first half
    // differ, to a search by UTF-16 unit, only in their second.
    static TextHunk Whole(TextHunk hunk, string before, string after)
    {
        var (oldStart, oldLength, newStart, newLength) = hunk;
        if (oldStart > 0 &&
            char.IsHighSurrogate(before[oldStart - 1]) &&
            newStart > 0 &&
            char.IsHighSurrogate(after[newStart - 1]))
        {
            oldStart--;
            oldLength++;
            newStart--;
            newLength++;
        }

        var oldEnd = oldStart + oldLength;
        var newEnd = newStart + newLength;
        if (oldEnd < before.Length &&
            char.IsLowSurrogate(before[oldEnd]) &&
            newEnd < after.Length &&
            char.IsLowSurrogate(after[newEnd]))
        {
            oldLength++;
            newLength++;
        }

        return new(oldStart, oldLength, newStart, newLength);
    }
}
