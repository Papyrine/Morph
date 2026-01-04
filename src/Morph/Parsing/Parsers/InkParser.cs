namespace WordRender;

/// <summary>
/// Parses ink/handwriting content from Word documents.
/// </summary>
public static class InkParser
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
            .FirstOrDefault(e => e.LocalName == "contentPart" &&
                                 e.GetAttributes().Any(a => a.LocalName is "id" or "embed"));

        if (contentPart == null)
        {
            return null;
        }

        // Get the relationship ID
        var relIdAttr = contentPart.GetAttributes()
            .FirstOrDefault(a => a is {LocalName: "id", Prefix: "r"});

        if (relIdAttr.Value == null)
        {
            return null;
        }

        // Get the ink part
        var inkPart = mainPart.GetPartById(relIdAttr.Value);

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
                                if (value.StartsWith('#') && value.Length == 7)
                                {
                                    color = value.Substring(1);
                                }

                                break;
                            case "width":
                                // Width is typically in cm, convert to points (1cm = 28.35pt)
                                if (double.TryParse(value.Replace("cm", "").Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var w))
                                {
                                    width = w * 28.35;
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

                if (brushRef != null && brushes.TryGetValue(brushRef, out var brush))
                {
                    strokeColor = brush.color;
                    strokeWidth = brush.width;
                    strokeTransparency = brush.transparency;
                    isHighlighter = brush.isHighlighter;
                }

                // Parse trace points
                // Format: "x1 y1, x2 y2, x3 y3" or "x1 y1 x2 y2 x3 y3"
                var points = ParseTracePoints(traceData, canvasWidth, canvasHeight);
                if (points.Count < 2)
                {
                    continue;
                }

                strokes.Add(new()
                {
                    Points = points,
                    ColorHex = strokeColor,
                    WidthPoints = strokeWidth,
                    Transparency = strokeTransparency,
                    IsHighlighter = isHighlighter
                });
            }
        }

        return strokes;
    }

    /// <summary>
    /// Parses trace point data from InkML trace element.
    /// </summary>
    static List<InkPoint> ParseTracePoints(string traceData, double canvasWidth, double canvasHeight)
    {
        var points = new List<InkPoint>();

        // InkML trace data can be in various formats:
        // "x1 y1, x2 y2, x3 y3" (comma-separated points)
        // "x1 y1 x2 y2 x3 y3" (space-separated values)
        // "'x1 y1 'x2 y2" (with modifiers like ' for relative or * for velocity)

        // Split by comma first, then by space
        var segments = traceData.Split([','], StringSplitOptions.RemoveEmptyEntries);

        foreach (var segment in segments)
        {
            var values = segment.Trim().Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);

            // Process pairs of values (x, y)
            for (var i = 0; i + 1 < values.Length; i += 2)
            {
                var xStr = values[i].TrimStart('\'', '*', '!', '?');
                var yStr = values[i + 1].TrimStart('\'', '*', '!', '?');

                if (double.TryParse(xStr, NumberStyles.Float, CultureInfo.InvariantCulture, out var x) &&
                    double.TryParse(yStr, NumberStyles.Float, CultureInfo.InvariantCulture, out var y))
                {
                    // InkML coordinates are typically in himetric units (0.01mm)
                    // Convert to points: 1 himetric = 0.01mm, 1 point = 0.3528mm
                    // So: points = himetric * 0.01 / 0.3528 = himetric * 0.02835
                    var xPt = x * 0.02835;
                    var yPt = y * 0.02835;

                    // Handle relative coordinates (prefixed with ')
                    if (values[i].StartsWith('\'') && points.Count > 0)
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
        }

        return points;
    }
}
