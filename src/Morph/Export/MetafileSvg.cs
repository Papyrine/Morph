/// <summary>
/// A Windows metafile picture (EMF or WMF) as an SVG document a browser can draw. No browser draws a
/// metafile, so an exported <c>&lt;img&gt;</c> of one showed only its alt text: letters/10's signature,
/// an EMF of a single run of script text, read "Image of signature". The drawing is
/// <see cref="MetafileReader"/>'s, in the unit square of the frame, scaled here to the frame's own size.
/// </summary>
static class MetafileSvg
{
    public static bool IsMetafile(string? contentType) =>
        contentType is "image/x-emf" or "image/emf" or "image/x-wmf" or "image/wmf";

    /// <summary>The SVG for <paramref name="data"/> drawn at the frame's size, or null when the
    /// reader takes nothing from it.</summary>
    public static string? Write(byte[] data, double widthPoints, double heightPoints)
    {
        if (widthPoints <= 0 ||
            heightPoints <= 0 ||
            MetafileReader.Read(data) is not {Items.Count: > 0} drawing)
        {
            return null;
        }

        var builder = new StringBuilder();
        builder.Append("<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 ")
            .Append(Number(widthPoints)).Append(' ').Append(Number(heightPoints))
            .Append("\" preserveAspectRatio=\"none\" width=\"").Append(Number(widthPoints)).Append("pt\" height=\"")
            .Append(Number(heightPoints)).Append("pt\">");
        foreach (var item in drawing.Items)
        {
            switch (item)
            {
                case MetafilePath path:
                    AppendPath(builder, path, widthPoints, heightPoints);
                    break;
                case MetafileText text:
                    AppendText(builder, text, widthPoints, heightPoints);
                    break;
            }
        }

        builder.Append("</svg>");
        return builder.ToString();
    }

    static void AppendPath(StringBuilder builder, MetafilePath path, double width, double height)
    {
        builder.Append("<path d=\"");
        foreach (var figure in path.Figures)
        {
            for (var index = 0; index < figure.Count; index++)
            {
                builder.Append(index == 0 ? 'M' : 'L')
                    .Append(Number(figure[index].X * width)).Append(' ')
                    .Append(Number(figure[index].Y * height)).Append(' ');
            }

            if (path.Closed)
            {
                builder.Append("Z ");
            }
        }

        builder.Append("\" fill=\"").Append(path.FillHex is { } fill ? $"#{fill}" : "none").Append('"');
        if (path.LineHex is { } line)
        {
            builder.Append(" stroke=\"#").Append(line).Append("\" stroke-width=\"")
                .Append(Number(Math.Max(path.LineWidth * height, 0.25))).Append('"');
        }

        builder.Append("/>");
    }

    static void AppendText(StringBuilder builder, MetafileText text, double width, double height)
    {
        var x = text.X * width;
        var y = text.Y * height;
        builder.Append("<text x=\"").Append(Number(x)).Append("\" y=\"").Append(Number(y))
            .Append("\" font-family=\"").Append(WebUtility.HtmlEncode(text.FontFamily))
            .Append("\" font-size=\"").Append(Number(text.Size * height))
            .Append("\" fill=\"#").Append(text.ColorHex).Append('"');
        if (text.Bold)
        {
            builder.Append(" font-weight=\"bold\"");
        }

        if (text.Italic)
        {
            builder.Append(" font-style=\"italic\"");
        }

        if (text.Underline)
        {
            builder.Append(" text-decoration=\"underline\"");
        }

        var anchor = text.Horizontal switch
        {
            MetafileTextAlign.Centre => "middle",
            MetafileTextAlign.Right => "end",
            _ => "start"
        };
        var baseline = text.Vertical switch
        {
            MetafileTextBaseline.Top => "text-before-edge",
            MetafileTextBaseline.Bottom => "text-after-edge",
            _ => "alphabetic"
        };
        builder.Append(" text-anchor=\"").Append(anchor).Append("\" dominant-baseline=\"").Append(baseline).Append('"');
        if (Math.Abs(text.Degrees) > 0.01)
        {
            // The escapement turns anticlockwise; SVG's rotate turns clockwise.
            builder.Append(" transform=\"rotate(").Append(Number(-text.Degrees)).Append(' ')
                .Append(Number(x)).Append(' ').Append(Number(y)).Append(")\"");
        }

        builder.Append('>').Append(WebUtility.HtmlEncode(text.Text)).Append("</text>");
    }

    static string Number(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
}
