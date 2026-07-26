/// <summary>
/// One run of text to paint on a <see cref="PlacedLine"/>: its <see cref="Text"/>, the
/// <see cref="RunProperties"/> a painter resolves for font, colour and decoration, its absolute
/// <see cref="X"/> (points from the page's left) and its <see cref="Width"/> (points). A mixed-format
/// line is several runs at their own X; a uniform line is one. The width spans the run's text so a
/// painter can stroke an underline or strike and fill a highlight across it; per-glyph advances (so glyph
/// positions come from the metric model rather than the painter's own font library) are a later slice.
/// </summary>
readonly record struct PlacedRun(float X, float Width, string Text, RunProperties Properties);
