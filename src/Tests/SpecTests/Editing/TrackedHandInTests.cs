using static EditDocuments;
using static ReviewDocuments;

// The open paragraph, under tracking, says what it struck and what was typed (NewItem.Struck,
// NewItem.Typed), and each change goes into the file exactly where the reader made it — as Word records
// the keys (WordProbe R-R-1, R-R-2, R-13) — rather than where a comparison of the two texts puts it.
// Unit 0 is "Third and last.".
public class TrackedHandInTests
{
    static byte[] Third() =>
        Build(P(R("Third and last.")));

    static NewItem Struck(int unit, string text) =>
        new(unit, text, Struck: true);

    static NewItem Typed(int unit, string text) =>
        new(unit, text, Typed: true);

    // Typed beside a deletion, ahead of it, it stays ahead of it. Compared as texts, "last." made
    // "first." would be "la" replaced by "fir", behind the deletion.
    [Test]
    public async Task TypedAheadOfStruckText_StaysAheadOfTheDeletion()
    {
        var edited = Rewrite(Third(), 0, Tracked, Reads(T(0, "Third and "), Typed(0, "first."), Struck(0, "last.")));

        await Assert.That(Shape(edited)).IsEqualTo("[Third and ]{+[first.]+}{-[last.]-} ¶");
    }

    // A selection typed over: struck, and what replaces it after it (WordProbe R-05).
    [Test]
    public async Task StruckThenTyped_TheReplacementFollowsTheDeletion()
    {
        var edited = Rewrite(Third(), 0, Tracked, Reads(T(0, "Third "), Struck(0, "and "), Typed(0, "or "), T(0, "last.")));

        await Assert.That(Shape(edited)).IsEqualTo("[Third ]{-[and ]-}{+[or ]+}[last.] ¶");
    }

    // Typed ahead of a deletion with text after it: still ahead of the deletion.
    [Test]
    public async Task TypedAheadOfStruckText_InTheMiddle_StaysAheadOfIt()
    {
        var edited = Rewrite(Third(), 0, Tracked, Reads(T(0, "Third "), Typed(0, "or "), Struck(0, "and "), T(0, "last.")));

        await Assert.That(Shape(edited)).IsEqualTo("[Third ]{+[or ]+}{-[and ]-}[last.] ¶");
    }

    // Two letters struck one apart are two deletions, as the reader made them: compared as texts, the
    // two would join into "hir" replaced by "i".
    [Test]
    public async Task TwoDeletionsCloseTogether_StayTwo()
    {
        var edited = Rewrite(
            Third(),
            0,
            Tracked,
            Reads(T(0, "T"), Struck(0, "h"), T(0, "i"), Struck(0, "r"), T(0, "d and last.")));

        await Assert.That(Shape(edited)).IsEqualTo("[T]{-[h]-}[i]{-[r]-}[d and last.] ¶");
    }

    [Test]
    public async Task TwoInsertionsCloseTogether_StayTwo()
    {
        var edited = Rewrite(
            Third(),
            0,
            Tracked,
            Reads(T(0, "Thi"), Typed(0, "x"), T(0, "rd"), Typed(0, "y"), T(0, " and last.")));

        await Assert.That(Shape(edited)).IsEqualTo("[Thi]{+[x]+}[rd]{+[y]+}[ and last.] ¶");
    }

    // A letter struck and the same letter typed again is a deletion and an insertion, never the letter
    // kept: what was typed is not what was there.
    [Test]
    public async Task TheSameLetterStruckAndTyped_IsADeletionAndAnInsertion()
    {
        var edited = Rewrite(Third(), 0, Tracked, Reads(T(0, "Third and "), Typed(0, "l"), Struck(0, "l"), T(0, "ast.")));

        await Assert.That(Shape(edited)).IsEqualTo("[Third and ]{+[l]+}{-[l]-}[ast.] ¶");
    }

    // What the editor says is not the old text — a letter changed without being said to be — and the
    // comparison works it out; what was struck is still deleted, never put in again.
    [Test]
    public async Task WhatItSaysIsNotTheOldText_IsComparedAndTheStruckTextStillGoes()
    {
        var edited = Rewrite(Third(), 0, Tracked, Reads(T(0, "Thxrd and "), Struck(0, "last.")));

        await Assert.That(Shape(edited)).Contains("{-[last.]-}");
        await Assert.That(Shape(edited)).DoesNotContain("{+[last.");
        await Assert.That(Describe(edited)).IsEqualTo("{-Third-}{+Thxrd+} and {-last.-}¶");
    }

    // Untracked, the struck text goes and the typed stays.
    [Test]
    public async Task Untracked_TheStruckTextGoes()
    {
        var edited = Rewrite(Third(), 0, Plain, Reads(T(0, "Third and "), Typed(0, "first."), Struck(0, "last.")));

        await Assert.That(Shape(edited)).IsEqualTo("[Third and first.] ¶");
    }
}
