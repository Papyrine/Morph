/// <summary>
/// Parses ink/handwriting content from Word documents.
/// </summary>
static class InkParser
{
    /// <summary>
    /// Parses a Drawing element to extract ink content.
    /// </summary>
    public static InkElement? ParseInk(Drawing drawing, MainDocumentPart mainPart)
    {
        var dimensions = drawing.GetDimensions();
        if (dimensions == null)
        {
            return null;
        }

        var (widthPoints, heightPoints) = dimensions.Value;

        // Look for contentPart element which references ink content
        // contentPart is in the a14 namespace (Office 2010 Drawing)
        var contentPart = drawing.Descendants()
            .FirstOrDefault(_ => _.LocalName == "contentPart" &&
                                 _.GetAttributes().Any(attribute => attribute.LocalName is "id" or "embed"));

        if (contentPart == null)
        {
            return null;
        }

        // Get the relationship ID
        var relIdAttribute = contentPart.GetAttributes()
            .FirstOrDefault(_ => _ is {LocalName: "id", Prefix: "r"});

        if (relIdAttribute.Value == null)
        {
            return null;
        }

        // Get the ink part
        var inkPart = mainPart.GetPartById(relIdAttribute.Value);

        // Read the InkML content
        using var stream = inkPart.GetStream();
        var inkXml = new XmlDocument();
        inkXml.Load(stream);

        var strokes = ParseInkML(inkXml, widthPoints, heightPoints);
        if (strokes.Count == 0)
        {
            return null;
        }

        return new()
        {
            WidthPoints = widthPoints,
            HeightPoints = heightPoints,
            Strokes = strokes
        };
    }

    /// <summary>
    /// Parses InkML XML to extract strokes.
    /// </summary>
    static List<InkStroke> ParseInkML(XmlDocument inkXml, double canvasWidth, double canvasHeight)
    {
        var strokes = new List<InkStroke>();
        var nsMgr = new XmlNamespaceManager(inkXml.NameTable);
        nsMgr.AddNamespace("inkml", "http://www.w3.org/2003/InkML");

        // Parse brush definitions for colors and widths
        var brushes = new Dictionary<string, (string color, double width, byte transparency, bool isHighlighter)>();
        var brushNodes = inkXml.SelectNodes("//inkml:brush", nsMgr);
        if (brushNodes != null)
        {
            foreach (XmlNode brushNode in brushNodes)
            {
                var brushId = brushNode.Attributes?["xml:id"]?.Value;
                if (brushId == null)
                {
                    continue;
                }

                var color = "000000";
                var width = 1.5;
                byte transparency = 0;
                var isHighlighter = false;

                var brushProps = brushNode.SelectNodes("inkml:brushProperty", nsMgr);
                if (brushProps != null)
                {
                    foreach (XmlNode prop in brushProps)
                    {
                        var name = prop.Attributes?["name"]?.Value;
                        var value = prop.Attributes?["value"]?.Value;
                        if (name == null || value == null)
                        {
                            continue;
                        }

                        switch (name)
                        {
                            case "color":
                                // Color can be #RRGGBB format
                                if (value.StartsWith('#') &&
                                    value.Length == 7)
                                {
                                    color = value[1..];
                                }

                                break;
                            case "width":
                                // Width is typically in cm, convert to points (1cm = 28.35pt)
                                var widthSpan = value.AsSpan().Trim();
                                if (widthSpan.EndsWith("cm", StringComparison.Ordinal))
                                {
                                    widthSpan = widthSpan[..^2].TrimEnd();
                                }
                                if (double.TryParse(widthSpan, NumberStyles.Float, CultureInfo.InvariantCulture, out var widthCm))
                                {
                                    width = widthCm * 28.35;
                                }

                                break;
                            case "transparency":
                                if (int.TryParse(value, out var t))
                                {
                                    transparency = (byte) Math.Clamp(t, 0, 255);
                                }

                                break;
                            case "tip":
                                // Highlighters often use rectangle tip
                                isHighlighter = value == "rectangle";
                                break;
                        }
                    }
                }

                // Check for highlighter based on high transparency
                if (transparency > 100)
                {
                    isHighlighter = true;
                }

                brushes[brushId] = (color, width, transparency, isHighlighter);
            }
        }

        // Parse trace elements (strokes)
        var traceNodes = inkXml.SelectNodes("//inkml:trace", nsMgr);
        if (traceNodes != null)
        {
            foreach (XmlNode traceNode in traceNodes)
            {
                var brushRef = traceNode.Attributes?["brushRef"]?.Value.TrimStart('#');
                var traceData = traceNode.InnerText.Trim();

                if (string.IsNullOrEmpty(traceData))
                {
                    continue;
                }

                // Get brush properties
                var strokeColor = "000000";
                var strokeWidth = 1.5;
                byte strokeTransparency = 0;
                var isHighlighter = false;

                if (brushRef != null &&
                    brushes.TryGetValue(brushRef, out var brush))
                {
                    strokeColor = brush.color;
                    strokeWidth = brush.width;
                    strokeTransparency = brush.transparency;
                    isHighlighter = brush.isHighlighter;
                }

                // Parse trace points
                // Format: "x1 y1, x2 y2, x3 y3" or "x1 y1 x2 y2 x3 y3"
                var points = ParseTracePoints(traceData);
                if (points.Count < 2)
                {
                    continue;
                }

                strokes.Add(
                    new()
                    {
                        Points = points,
                        ColorHex = strokeColor,
                        WidthPoints = strokeWidth,
                        Transparency = strokeTransparency,
                        IsHighlighter = isHighlighter
                    });
            }
        }

        // Scale all stroke points to fit within the canvas bounds
        ScaleStrokesToCanvas(strokes, canvasWidth, canvasHeight);

        return strokes;
    }

    /// <summary>
    /// Scales all stroke points to fit within the canvas dimensions.
    /// Translates points to origin (0,0) and scales uniformly to fit within the canvas while preserving aspect ratio.
    /// </summary>
    /// <param name="strokes">The strokes to scale (modified in place).</param>
    /// <param name="canvasWidth">Target canvas width in points.</param>
    /// <param name="canvasHeight">Target canvas height in points.</param>
    public static void ScaleStrokesToCanvas(List<InkStroke> strokes, double canvasWidth, double canvasHeight)
    {
        if (strokes.Count == 0)
        {
            return;
        }

        // Find bounding box of all points across all strokes (single pass)
        var minX = double.MaxValue;
        var maxX = double.MinValue;
        var minY = double.MaxValue;
        var maxY = double.MinValue;
        var hasPoints = false;

        foreach (var stroke in strokes)
        {
            foreach (var point in stroke.Points)
            {
                hasPoints = true;
                if (point.X < minX)
                {
                    minX = point.X;
                }

                if (point.X > maxX)
                {
                    maxX = point.X;
                }

                if (point.Y < minY)
                {
                    minY = point.Y;
                }

                if (point.Y > maxY)
                {
                    maxY = point.Y;
                }
            }
        }

        if (!hasPoints)
        {
            return;
        }

        var rawWidth = maxX - minX;
        var rawHeight = maxY - minY;

        // If ink has no extent, nothing to scale
        if (rawWidth <= 0 &&
            rawHeight <= 0)
        {
            return;
        }

        // Calculate scale factor to fit within canvas while preserving aspect ratio
        var scaleX = rawWidth > 0 ? canvasWidth / rawWidth : 1.0;
        var scaleY = rawHeight > 0 ? canvasHeight / rawHeight : 1.0;
        var scale = Math.Min(scaleX, scaleY);

        // Scale and translate all points (translate to origin first, then scale)
        for (var i = 0; i < strokes.Count; i++)
        {
            var stroke = strokes[i];
            var scaledPoints = new List<InkPoint>(stroke.Points.Count);

            foreach (var point in stroke.Points)
            {
                scaledPoints.Add(
                    new()
                    {
                        X = (point.X - minX) * scale,
                        Y = (point.Y - minY) * scale,
                        Pressure = point.Pressure
                    });
            }

            // Replace stroke with scaled points
            strokes[i] = new()
            {
                Points = scaledPoints,
                ColorHex = stroke.ColorHex,
                WidthPoints = stroke.WidthPoints,
                Transparency = stroke.Transparency,
                PenTip = stroke.PenTip,
                IsHighlighter = stroke.IsHighlighter
            };
        }
    }

    /// <summary>
    /// Parses trace point data from InkML trace element.
    /// </summary>
    internal static List<InkPoint> ParseTracePoints(string traceData)
    {
        var points = new List<InkPoint>();

        // InkML trace data can be in various formats:
        // "x1 y1, x2 y2, x3 y3" (comma-separated points)
        // "x1 y1 x2 y2 x3 y3" (space-separated values)
        // "'x1 y1 'x2 y2" (with modifiers like ' for relative or * for velocity)

        var trace = traceData.AsSpan();
        foreach (var segmentRange in trace.Split(','))
        {
            var segment = trace[segmentRange].Trim();
            if (segment.IsEmpty)
            {
                continue;
            }

            // Process pairs of (x, y) tokens
            ReadOnlySpan<char> firstToken = default;
            var haveFirst = false;
            foreach (var tokenRange in segment.SplitAny(" \t"))
            {
                var token = segment[tokenRange];
                if (token.IsEmpty)
                {
                    continue;
                }

                if (!haveFirst)
                {
                    firstToken = token;
                    haveFirst = true;
                    continue;
                }

                var prefixChars = "'*!?";
                var xText = firstToken.TrimStart(prefixChars);
                var yText = token.TrimStart(prefixChars);
                haveFirst = false;

                if (!double.TryParse(xText, NumberStyles.Float, CultureInfo.InvariantCulture, out var x) ||
                    !double.TryParse(yText, NumberStyles.Float, CultureInfo.InvariantCulture, out var y))
                {
                    continue;
                }

                // InkML coordinates are typically in himetric units (0.01mm)
                // Convert to points: 1 himetric = 0.01mm, 1 point = 0.3528mm
                // So: points = himetric * 0.01 / 0.3528 = himetric * 0.02835
                var xPt = x * 0.02835;
                var yPt = y * 0.02835;

                // Handle relative coordinates (prefixed with ')
                if (firstToken[0] == '\'' &&
                    points.Count > 0)
                {
                    var lastPoint = points[^1];
                    xPt = lastPoint.X + xPt;
                    yPt = lastPoint.Y + yPt;
                }

                points.Add(
                    new()
                    {
                        X = xPt,
                        Y = yPt
                    });
            }
        }

        return points;
    }
}
