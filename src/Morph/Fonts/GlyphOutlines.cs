using System.Buffers.Binary;

/// <summary>
/// A TrueType face's glyph outlines (<c>glyf</c>/<c>loca</c>), read without a backend, for the
/// exports that draw text as shapes: the HTML export's envelope WordArt, whose letters are bent
/// as the raster painters bend them (<see cref="WordArtEnvelope"/>). Quadratic curves are cut
/// into chords, since the warp wants a dense polyline rather than a curve. A face with CFF
/// outlines (<c>OTTO</c>) has no <c>glyf</c> and reads as null.
/// </summary>
sealed class GlyphOutlines
{
    // 'glyf'
    const uint glyfTag = 0x676C7966;
    // 'loca'
    const uint locaTag = 0x6C6F6361;
    // 'head'
    const uint headTag = 0x68656164;

    // A quadratic curve is cut into this many chords, as the Skia drawer's Flatten cuts one.
    const int curveSteps = 12;

    // How deep composite glyphs may nest before the rest are dropped, against a malformed font
    // whose components refer back to themselves.
    const int maxDepth = 8;

    readonly FontMetrics metrics;
    readonly byte[] glyphData;
    readonly uint[] glyphOffsets;

    GlyphOutlines(FontMetrics metrics, byte[] glyphData, uint[] glyphOffsets)
    {
        this.metrics = metrics;
        this.glyphData = glyphData;
        this.glyphOffsets = glyphOffsets;
    }

    /// <summary>Reads face <paramref name="faceIndex"/> of the font in <paramref name="stream"/>; null when it has no TrueType outlines.</summary>
    public static GlyphOutlines? Read(Stream stream, int faceIndex = 0)
    {
        if (FontMetricsReader.Read(stream, faceIndex) is not { } metrics ||
            FontMetricsReader.ReadDirectory(stream, faceIndex) is not { } tables ||
            !tables.TryGetValue(glyfTag, out var glyf) ||
            !tables.TryGetValue(locaTag, out var loca) ||
            !tables.TryGetValue(headTag, out var head))
        {
            return null;
        }

        // head: indexToLocFormat is an int16 at offset 50.
        var headBytes = new byte[54];
        var glyphData = new byte[glyf.Length];
        var locaBytes = new byte[loca.Length];
        if (!FontMetricsReader.TryReadExactAt(stream, head.Offset, headBytes) ||
            !FontMetricsReader.TryReadExactAt(stream, glyf.Offset, glyphData) ||
            !FontMetricsReader.TryReadExactAt(stream, loca.Offset, locaBytes))
        {
            return null;
        }

        var longOffsets = BinaryPrimitives.ReadInt16BigEndian(headBytes.AsSpan(50, 2)) != 0;
        var count = longOffsets ? locaBytes.Length / 4 : locaBytes.Length / 2;
        var offsets = new uint[count];
        for (var index = 0; index < count; index++)
        {
            offsets[index] = longOffsets
                ? BinaryPrimitives.ReadUInt32BigEndian(locaBytes.AsSpan(index * 4, 4))
                : BinaryPrimitives.ReadUInt16BigEndian(locaBytes.AsSpan(index * 2, 2)) * 2u;
        }

        return new(metrics, glyphData, offsets);
    }

    /// <summary>Reads the font at <paramref name="path"/>.</summary>
    public static GlyphOutlines? Read(string path, int faceIndex = 0)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Read(stream, faceIndex);
    }

    /// <summary>
    /// The outline of <paramref name="text"/> set in one line at <paramref name="sizePoints"/>, as
    /// closed polylines in points with y down and the baseline at 0, each glyph advanced by the
    /// face's own advance and nothing more (no kerning), as a backend's text path is.
    /// </summary>
    public List<IReadOnlyList<(float X, float Y)>> TextContours(string text, float sizePoints)
    {
        var scale = sizePoints / metrics.UnitsPerEm;
        var contours = new List<IReadOnlyList<(float X, float Y)>>();
        var pen = 0f;
        foreach (var rune in text.EnumerateRunes())
        {
            var glyph = metrics.GlyphId(rune.Value);
            var origin = pen;
            AppendGlyph(glyph, 1, 0, 0, 1, 0, 0, 0, (x, y) => (origin + x * scale, -y * scale), contours);
            pen += metrics.GlyphAdvanceUnits(glyph) * scale;
        }

        return contours;
    }

    // Appends a glyph's contours, each point taken through the 2x2 matrix and offset a composite
    // places its component with, then through `place`.
    void AppendGlyph(ushort glyph, float xx, float xy, float yx, float yy, float dx, float dy, int depth, Func<float, float, (float X, float Y)> place, List<IReadOnlyList<(float X, float Y)>> contours)
    {
        if (depth > maxDepth ||
            glyph + 1 >= glyphOffsets.Length)
        {
            return;
        }

        var start = (int) glyphOffsets[glyph];
        var end = (int) glyphOffsets[glyph + 1];
        if (end <= start ||
            end > glyphData.Length ||
            end - start < 10)
        {
            return;
        }

        var data = glyphData.AsSpan(start, end - start);
        var contourCount = BinaryPrimitives.ReadInt16BigEndian(data);
        (float X, float Y) Transform(float x, float y) => place(x * xx + y * yx + dx, x * xy + y * yy + dy);
        if (contourCount >= 0)
        {
            AppendSimple(data, contourCount, Transform, contours);
        }
        else
        {
            AppendComposite(data, xx, xy, yx, yy, dx, dy, depth, place, contours);
        }
    }

    static void AppendSimple(ReadOnlySpan<byte> data, int contourCount, Func<float, float, (float X, float Y)> place, List<IReadOnlyList<(float X, float Y)>> contours)
    {
        var position = 10;
        if (data.Length < position + contourCount * 2 + 2)
        {
            return;
        }

        var ends = new int[contourCount];
        for (var index = 0; index < contourCount; index++)
        {
            ends[index] = BinaryPrimitives.ReadUInt16BigEndian(data[(position + index * 2)..]);
        }

        position += contourCount * 2;
        var pointCount = contourCount == 0 ? 0 : ends[^1] + 1;
        var instructionLength = BinaryPrimitives.ReadUInt16BigEndian(data[position..]);
        position += 2 + instructionLength;

        var flags = new byte[pointCount];
        for (var index = 0; index < pointCount;)
        {
            if (position >= data.Length)
            {
                return;
            }

            var flag = data[position++];
            flags[index++] = flag;
            if ((flag & 0x08) != 0 &&
                position < data.Length)
            {
                var repeat = data[position++];
                for (var copy = 0; copy < repeat && index < pointCount; copy++)
                {
                    flags[index++] = flag;
                }
            }
        }

        var xs = new int[pointCount];
        var ys = new int[pointCount];
        if (!ReadCoordinates(data, ref position, flags, xs, 0x02, 0x10) ||
            !ReadCoordinates(data, ref position, flags, ys, 0x04, 0x20))
        {
            return;
        }

        var first = 0;
        foreach (var last in ends)
        {
            if (last >= pointCount ||
                last < first)
            {
                return;
            }

            if (Contour(xs, ys, flags, first, last, place) is {Count: > 1} contour)
            {
                contours.Add(contour);
            }

            first = last + 1;
        }
    }

    // Reads one axis of a simple glyph's points, which are deltas: a short one a byte whose sign
    // is the flag's same-or-positive bit, a long one a signed word, or none at all when the flag
    // says the value repeats.
    static bool ReadCoordinates(ReadOnlySpan<byte> data, ref int position, byte[] flags, int[] values, int shortBit, int sameBit)
    {
        var value = 0;
        for (var index = 0; index < flags.Length; index++)
        {
            var flag = flags[index];
            if ((flag & shortBit) != 0)
            {
                if (position >= data.Length)
                {
                    return false;
                }

                var delta = data[position++];
                value += (flag & sameBit) != 0 ? delta : -delta;
            }
            else if ((flag & sameBit) == 0)
            {
                if (position + 2 > data.Length)
                {
                    return false;
                }

                value += BinaryPrimitives.ReadInt16BigEndian(data[position..]);
                position += 2;
            }

            values[index] = value;
        }

        return true;
    }

    // One contour as a polyline: between two off-curve points stands an implied on-curve point
    // halfway, and each off-curve point is the control of a quadratic between its neighbours.
    static List<(float X, float Y)> Contour(int[] xs, int[] ys, byte[] flags, int first, int last, Func<float, float, (float X, float Y)> place)
    {
        var count = last - first + 1;
        bool OnCurve(int index) => (flags[first + (index % count + count) % count] & 0x01) != 0;
        (float X, float Y) At(int index)
        {
            var point = first + (index % count + count) % count;
            return (xs[point], ys[point]);
        }

        // Start on an on-curve point, or on the implied one before the first point when none is.
        var startIndex = 0;
        while (startIndex < count &&
               !OnCurve(startIndex))
        {
            startIndex++;
        }

        (float X, float Y) current;
        if (startIndex == count)
        {
            var (firstX, firstY) = At(0);
            var (lastX, lastY) = At(-1);
            current = ((firstX + lastX) / 2, (firstY + lastY) / 2);
            startIndex = 0;
        }
        else
        {
            current = At(startIndex);
            startIndex++;
        }

        // The walk ends back on an on-curve start; a contour of off-curve points alone ends on a
        // control and closes to the implied start.
        var start = current;
        var polyline = new List<(float X, float Y)> {place(current.X, current.Y)};
        (float X, float Y)? control = null;
        for (var step = 0; step < count; step++)
        {
            var index = startIndex + step;
            var point = At(index);
            if (OnCurve(index))
            {
                if (control is { } pending)
                {
                    AppendQuad(polyline, current, pending, point, place);
                    control = null;
                }
                else
                {
                    polyline.Add(place(point.X, point.Y));
                }

                current = point;
            }
            else
            {
                if (control is { } pending)
                {
                    var middle = ((pending.X + point.X) / 2, (pending.Y + point.Y) / 2);
                    AppendQuad(polyline, current, pending, middle, place);
                    current = middle;
                }

                control = point;
            }
        }

        if (control is { } closing)
        {
            AppendQuad(polyline, current, closing, start, place);
        }

        return polyline;
    }

    static void AppendQuad(List<(float X, float Y)> polyline, (float X, float Y) from, (float X, float Y) control, (float X, float Y) to, Func<float, float, (float X, float Y)> place)
    {
        for (var step = 1; step <= curveSteps; step++)
        {
            var along = (float) step / curveSteps;
            var rest = 1 - along;
            var x = rest * rest * from.X + 2 * rest * along * control.X + along * along * to.X;
            var y = rest * rest * from.Y + 2 * rest * along * control.Y + along * along * to.Y;
            polyline.Add(place(x, y));
        }
    }

    void AppendComposite(ReadOnlySpan<byte> data, float xx, float xy, float yx, float yy, float dx, float dy, int depth, Func<float, float, (float X, float Y)> place, List<IReadOnlyList<(float X, float Y)>> contours)
    {
        var position = 10;
        while (position + 4 <= data.Length)
        {
            var flags = BinaryPrimitives.ReadUInt16BigEndian(data[position..]);
            var component = BinaryPrimitives.ReadUInt16BigEndian(data[(position + 2)..]);
            position += 4;

            float argument1;
            float argument2;
            if ((flags & 0x0001) != 0)
            {
                if (position + 4 > data.Length)
                {
                    return;
                }

                argument1 = BinaryPrimitives.ReadInt16BigEndian(data[position..]);
                argument2 = BinaryPrimitives.ReadInt16BigEndian(data[(position + 2)..]);
                position += 4;
            }
            else
            {
                if (position + 2 > data.Length)
                {
                    return;
                }

                argument1 = (sbyte) data[position];
                argument2 = (sbyte) data[position + 1];
                position += 2;
            }

            float scaleXX = 1, scaleXY = 0, scaleYX = 0, scaleYY = 1;
            if ((flags & 0x0008) != 0)
            {
                scaleXX = scaleYY = F2Dot14(data, ref position);
            }
            else if ((flags & 0x0040) != 0)
            {
                scaleXX = F2Dot14(data, ref position);
                scaleYY = F2Dot14(data, ref position);
            }
            else if ((flags & 0x0080) != 0)
            {
                scaleXX = F2Dot14(data, ref position);
                scaleXY = F2Dot14(data, ref position);
                scaleYX = F2Dot14(data, ref position);
                scaleYY = F2Dot14(data, ref position);
            }

            // Offsets given as point numbers to match (ARGS_ARE_XY_VALUES clear) are rare in
            // Latin faces and are taken as no offset.
            var offsetX = (flags & 0x0002) != 0 ? argument1 : 0;
            var offsetY = (flags & 0x0002) != 0 ? argument2 : 0;

            // The component's own matrix first, then its offset, then the parent's matrix and
            // offset.
            AppendGlyph(
                component,
                scaleXX * xx + scaleXY * yx,
                scaleXX * xy + scaleXY * yy,
                scaleYX * xx + scaleYY * yx,
                scaleYX * xy + scaleYY * yy,
                offsetX * xx + offsetY * yx + dx,
                offsetX * xy + offsetY * yy + dy,
                depth + 1,
                place,
                contours);

            if ((flags & 0x0020) == 0)
            {
                return;
            }
        }
    }

    static float F2Dot14(ReadOnlySpan<byte> data, ref int position)
    {
        if (position + 2 > data.Length)
        {
            return 1;
        }

        var value = BinaryPrimitives.ReadInt16BigEndian(data[position..]) / 16384f;
        position += 2;
        return value;
    }
}
