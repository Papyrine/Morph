/// <summary>
/// One run of text to paint on a <see cref="PlacedLine"/>: its <see cref="Text"/>, the
/// <see cref="RunProperties"/> a painter resolves for font, colour and decoration, and its absolute
/// <see cref="X"/> (points from the page's left). The first painter slice places a single run per line
/// (the line's dominant font); a later slice splits a mixed-format line into several runs at their own
/// X, and carries the canonical per-glyph advances so glyph positions come from the metric model rather
/// than the painter's own font library.
/// </summary>
readonly record struct PlacedRun(float X, string Text, RunProperties Properties);
