/// <summary>
/// Reads a <c>path</c> element's <c>d</c> attribute into figures of lines and cubics. Every command
/// is taken — both cases of M, Z, L, H, V, C, S, Q, T and A — with the grammar's shorthands: a
/// command letter stands until the next one, extra pairs after a move are lines, and an arc's two
/// flags may run into each other and the number after them.
/// </summary>
static class SvgPathData
{
    /// <summary>
    /// The figures the data draws into <paramref name="builder"/>. False when the data is not path
    /// data all the way through; a reader then gives the document up rather than draw the part
    /// before the error, since the picture's raster twin is the surer rendering of it.
    /// </summary>
    public static bool TryParse(string data, SvgFigureBuilder builder)
    {
        var text = data.AsSpan();
        var position = 0;
        var command = '\0';

        // What S and T reflect: the last control point, when the command before was a curve of
        // their own kind, and the current point otherwise.
        var lastCubicX = 0d;
        var lastCubicY = 0d;
        var lastQuadraticX = 0d;
        var lastQuadraticY = 0d;
        var previous = '\0';

        SvgNumbers.SkipSpace(text, ref position);
        while (position < text.Length)
        {
            if (char.IsAsciiLetter(text[position]))
            {
                // Path data opens with a move; anything else has no point to draw from.
                if (command == '\0' && text[position] is not ('M' or 'm'))
                {
                    return false;
                }

                command = text[position];
                position++;
                if (command is 'Z' or 'z')
                {
                    builder.Close();
                    previous = command;
                    SvgNumbers.SkipSpace(text, ref position);
                    continue;
                }
            }
            else if (command is '\0' or 'Z' or 'z')
            {
                // Numbers with no command before them, or after a close.
                return false;
            }

            var relative = char.IsAsciiLetterLower(command);
            var originX = relative ? builder.X : 0;
            var originY = relative ? builder.Y : 0;
            switch (char.ToUpperInvariant(command))
            {
                case 'M':
                {
                    if (!Number(text, ref position, out var x) || !Number(text, ref position, out var y))
                    {
                        return false;
                    }

                    builder.MoveTo(originX + x, originY + y);

                    // Pairs after the first are lines, relative when the move was.
                    command = relative ? 'l' : 'L';
                    break;
                }
                case 'L':
                {
                    if (!Number(text, ref position, out var x) || !Number(text, ref position, out var y))
                    {
                        return false;
                    }

                    builder.LineTo(originX + x, originY + y);
                    break;
                }
                case 'H':
                {
                    if (!Number(text, ref position, out var x))
                    {
                        return false;
                    }

                    builder.LineTo(originX + x, builder.Y);
                    break;
                }
                case 'V':
                {
                    if (!Number(text, ref position, out var y))
                    {
                        return false;
                    }

                    builder.LineTo(builder.X, originY + y);
                    break;
                }
                case 'C':
                {
                    if (!Number(text, ref position, out var control1X) || !Number(text, ref position, out var control1Y) ||
                        !Number(text, ref position, out var control2X) || !Number(text, ref position, out var control2Y) ||
                        !Number(text, ref position, out var x) || !Number(text, ref position, out var y))
                    {
                        return false;
                    }

                    lastCubicX = originX + control2X;
                    lastCubicY = originY + control2Y;
                    builder.CurveTo(originX + control1X, originY + control1Y, lastCubicX, lastCubicY, originX + x, originY + y);
                    break;
                }
                case 'S':
                {
                    if (!Number(text, ref position, out var control2X) || !Number(text, ref position, out var control2Y) ||
                        !Number(text, ref position, out var x) || !Number(text, ref position, out var y))
                    {
                        return false;
                    }

                    var smooth = char.ToUpperInvariant(previous) is 'C' or 'S';
                    var control1X = smooth ? 2 * builder.X - lastCubicX : builder.X;
                    var control1Y = smooth ? 2 * builder.Y - lastCubicY : builder.Y;
                    lastCubicX = originX + control2X;
                    lastCubicY = originY + control2Y;
                    builder.CurveTo(control1X, control1Y, lastCubicX, lastCubicY, originX + x, originY + y);
                    break;
                }
                case 'Q':
                {
                    if (!Number(text, ref position, out var controlX) || !Number(text, ref position, out var controlY) ||
                        !Number(text, ref position, out var x) || !Number(text, ref position, out var y))
                    {
                        return false;
                    }

                    lastQuadraticX = originX + controlX;
                    lastQuadraticY = originY + controlY;
                    builder.QuadraticTo(lastQuadraticX, lastQuadraticY, originX + x, originY + y);
                    break;
                }
                case 'T':
                {
                    if (!Number(text, ref position, out var x) || !Number(text, ref position, out var y))
                    {
                        return false;
                    }

                    var smooth = char.ToUpperInvariant(previous) is 'Q' or 'T';
                    lastQuadraticX = smooth ? 2 * builder.X - lastQuadraticX : builder.X;
                    lastQuadraticY = smooth ? 2 * builder.Y - lastQuadraticY : builder.Y;
                    builder.QuadraticTo(lastQuadraticX, lastQuadraticY, originX + x, originY + y);
                    break;
                }
                case 'A':
                {
                    if (!Number(text, ref position, out var radiusX) || !Number(text, ref position, out var radiusY) ||
                        !Number(text, ref position, out var rotation) ||
                        !Flag(text, ref position, out var largeArc) || !Flag(text, ref position, out var sweep) ||
                        !Number(text, ref position, out var x) || !Number(text, ref position, out var y))
                    {
                        return false;
                    }

                    builder.ArcTo(radiusX, radiusY, rotation, largeArc, sweep, originX + x, originY + y);
                    break;
                }
                default:
                    return false;
            }

            // The move has already turned itself into the line its further pairs are, and a line is
            // no curve for S or T to carry on from either way.
            previous = command;
            SvgNumbers.SkipSeparator(text, ref position);
        }

        return true;
    }

    static bool Number(CharSpan text, ref int position, out double value)
    {
        SvgNumbers.SkipSeparator(text, ref position);
        return SvgNumbers.TryRead(text, ref position, out value);
    }

    // An arc flag is a single 0 or 1 and needs nothing after it: "a1 1 0 01 2 2" has a large-arc
    // flag of 0 and a sweep flag of 1.
    static bool Flag(CharSpan text, ref int position, out bool value)
    {
        SvgNumbers.SkipSeparator(text, ref position);
        value = false;
        if (position >= text.Length || text[position] is not ('0' or '1'))
        {
            return false;
        }

        value = text[position] == '1';
        position++;
        return true;
    }
}
