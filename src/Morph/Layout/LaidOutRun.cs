/// <summary>
/// One run segment on a wrapped line: the <see cref="Text"/> from a single source run that falls on the
/// line, its <see cref="RunProperties"/> (font, colour, decoration), its <see cref="X"/> offset from the
/// line's left edge and its <see cref="Width"/>, in points. A mixed-format line ("plain <b>bold</b>
/// plain") becomes several of these; a uniform line is one. The offset is the canonical pen position at
/// the segment's start, so a painter draws each segment's font at the right place without re-measuring.
/// A tab-leader filler carries an empty <see cref="Text"/> and a non-<c>None</c> <see cref="Leader"/>: a
/// painter fills its span with the leader (tiled dots/hyphens, or a baseline rule for underscore).
/// </summary>
readonly record struct LaidOutRun(float X, float Width, string Text, RunProperties Properties, TabLeader Leader = TabLeader.None);
