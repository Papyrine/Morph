/// <summary>
/// Which line of a glyph box stays fixed while the envelope scales it vertically.
/// </summary>
enum EnvelopeAnchor
{
    /// <summary>Bottom stays put and the top shrinks toward it (Fade, Triangle, CanUp).</summary>
    Baseline,

    /// <summary>Both edges move symmetrically about the centre (Inflate, Deflate).</summary>
    Centre,

    /// <summary>Top stays put and the bottom moves away from it (CanDown).</summary>
    Top
}

/// <summary>
/// The geometry behind the WordArt envelope warps, shared by the Skia and ImageSharp drawers.
/// </summary>
/// <remarks>
/// <para>
/// Pure float arithmetic with no backend types in the signatures, which is what lets both drawers
/// call it: the two carried verbatim copies of <see cref="WarpPoint"/> and <see cref="At"/> and of
/// the <see cref="ScaleY"/> / <see cref="AnchorFor"/> tables, differing only in whether a point was
/// an <c>SKPoint</c> or a <c>PointF</c>.
/// </para>
/// <para>
/// Worth sharing for more than the line count. The two backends have to agree pixel-for-pixel —
/// the scenario suite compares them independently against the same Word reference — so a warp
/// constant that drifted in one copy would show up as a fidelity regression in one backend and be
/// read as a rasterisation difference.
/// </para>
/// </remarks>
static class WordArtEnvelope
{
    /// <summary>
    /// Edge text keeps this fraction of the bounding-box height, so glyphs at the ends of the word
    /// stay readable instead of collapsing to a line.
    /// </summary>
    const float minRatio = 0.55f;

    /// <summary>
    /// Maps a point on the laid-out text into the warped envelope.
    /// </summary>
    public static (float X, float Y) WarpPoint(
        (float X, float Y) point,
        float totalWidth,
        float glyphsTop,
        float glyphsHeight,
        float x, float y, float width, float height,
        WordArtTransform transform)
    {
        var t = Math.Clamp(point.X / totalWidth, 0f, 1f);
        var newX = x + t * width;
        var (top, bottom) = At(t, transform, y, height);
        var normY = (point.Y - glyphsTop) / glyphsHeight;
        var newY = top + normY * (bottom - top);
        return (newX, newY);
    }

    /// <summary>
    /// Returns the (top Y, bottom Y) envelope curve at normalised text position t ∈ [0, 1] for the
    /// given warp.
    /// </summary>
    public static (float top, float bottom) At(float t, WordArtTransform transform, float bboxTop, float bboxHeight)
    {
        var sinT = (float) Math.Sin(Math.PI * t);
        var bboxBottom = bboxTop + bboxHeight;
        var bboxCentre = bboxTop + bboxHeight / 2f;

        switch (transform)
        {
            case WordArtTransform.Inflate:
            {
                var h = bboxHeight * (minRatio + (1 - minRatio) * sinT);
                return (bboxCentre - h / 2f, bboxCentre + h / 2f);
            }
            case WordArtTransform.Deflate:
            {
                var h = bboxHeight * (1f - (1 - minRatio) * sinT);
                return (bboxCentre - h / 2f, bboxCentre + h / 2f);
            }
            case WordArtTransform.CanUp:
            {
                var h = bboxHeight * (minRatio + (1 - minRatio) * sinT);
                return (bboxBottom - h, bboxBottom);
            }
            case WordArtTransform.CanDown:
            {
                var h = bboxHeight * (minRatio + (1 - minRatio) * sinT);
                return (bboxTop, bboxTop + h);
            }
            default:
                return (bboxTop, bboxBottom);
        }
    }

    /// <summary>
    /// The per-glyph vertical scale factor for normalised position t ∈ [0, 1] along the text, or
    /// null when the warp is not one of the per-glyph envelopes.
    /// </summary>
    /// <remarks>
    /// Inflate/CanUp/CanDown peak ~1.4x in the middle (sin curve, amplitude 0.4); Deflate floors at
    /// 0.7 in the middle so glyphs stay readable. Per-glyph rendering is slower than a single
    /// DrawText, so a caller tries the path-based warps first.
    /// </remarks>
    public static Func<float, float>? ScaleY(WordArtTransform transform) =>
        transform switch
        {
            WordArtTransform.FadeRight => t => 1f - 0.65f * t,
            WordArtTransform.FadeLeft => t => 0.35f + 0.65f * t,
            WordArtTransform.Triangle => t => 0.35f + 0.65f * (1f - Math.Abs(2f * t - 1f)),
            WordArtTransform.Inflate => t => 1f + 0.5f * (float) Math.Sin(Math.PI * t),
            WordArtTransform.Deflate => t => 1f - 0.45f * (float) Math.Sin(Math.PI * t),
            WordArtTransform.CanUp => t => 1f + 0.5f * (float) Math.Sin(Math.PI * t),
            WordArtTransform.CanDown => t => 1f + 0.5f * (float) Math.Sin(Math.PI * t),
            _ => null
        };

    /// <summary>Which line stays fixed under the <see cref="ScaleY"/> factor.</summary>
    public static EnvelopeAnchor AnchorFor(WordArtTransform transform) =>
        transform switch
        {
            WordArtTransform.Inflate or WordArtTransform.Deflate => EnvelopeAnchor.Centre,
            WordArtTransform.CanDown => EnvelopeAnchor.Top,
            _ => EnvelopeAnchor.Baseline
        };
}
