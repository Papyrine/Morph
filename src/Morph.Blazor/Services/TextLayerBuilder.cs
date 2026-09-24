using System.Buffers;
using System.Text.Encodings.Web;
using System.Text.Json;
using Morph;

/// <summary>
/// Reads one laid-out page's text back out of the layout tree for the browser's selectable text layer —
/// the PDF.js technique: the page stays a picture, and a transparent copy of every run of text sits over
/// it at the exact spot the painter drew it, so the browser can select, copy and search it.
///
/// One walk feeds two writers that must agree character for character: the compact JSON morph-text.js
/// builds the layer from, and the plain text that layer yields when copied. The viewer's find box
/// searches that plain text, so a match's offsets land on the same characters in the DOM.
///
/// <para>The JSON (<c>v</c> 1). Lengths are points, rounded to 0.01; a frame's children are in the
/// frame's own coordinates.</para>
/// <code>
/// {"v":1,"w":595.28,"h":841.89,"i":[
///   {"x":72,"y":80,"h":14.6,"b":91.4,"e":1,"s":[{"x":72,"w":31.2,"t":"Hello","z":11,"f":1}]},
///   {"k":"r","x":…,"y":…,"w":…,"h":…,"r":-90,"i":[…]},     a rotation frame (nests)
///   {"k":"c","x":…,"y":…,"w":…,"h":…,"cx":1,"i":[…]},      a clip frame (cx 0: vertical only)
///   {"k":"b","x":…,"y":…,"w":…,"h":…,"t":"…","e":2}]}       a warped WordArt box
/// </code>
/// A line carries its box top <c>y</c>, height <c>h</c> and baseline <c>b</c>; its spans their left
/// <c>x</c>, width <c>w</c>, text <c>t</c>, drawn size <c>z</c>, flags <c>f</c> (1 bold, 2 italic, 4 a
/// separator synthesised here rather than drawn) and, for a superscript or subscript, the baseline shift
/// <c>d</c>. <c>e</c> is what follows the line when copied: 0 nothing, 1 a space, 2 a line break, 3 a tab.
/// </summary>
static class TextLayerBuilder
{
    // Runs on one line touch exactly when they are contiguous — both sides sit on the measurer's 120-dpi
    // pen grid — so any real gap is a removed space or a tab. This only absorbs float noise.
    const float minimumGap = 0.25f;

    static readonly JsonWriterOptions writerOptions = new()
    {
        // The JSON only ever reaches JSON.parse, never an HTML document, so non-ASCII text can travel
        // unescaped rather than tripling in size.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static PageTextLayer Build(LaidOutPage page)
    {
        var nodes = Container(ReadingOrder(page));

        var buffer = new ArrayBufferWriter<byte>();
        using (var json = new Utf8JsonWriter(buffer, writerOptions))
        {
            json.WriteStartObject();
            json.WriteNumber("v", 1);
            WriteNumber(json, "w", page.Settings.WidthPoints);
            WriteNumber(json, "h", page.Settings.HeightPoints);
            json.WriteStartArray("i");
            foreach (var node in nodes)
            {
                Write(json, node, 0, 0);
            }

            json.WriteEndArray();
            json.WriteEndObject();
        }

        var text = new StringBuilder();
        foreach (var node in nodes)
        {
            AppendText(text, node);
        }

        return new(Encoding.UTF8.GetString(buffer.WrittenSpan), text.ToString());
    }

    // Paint order puts the footer band BEFORE the body (Fragmenter assembles background, footer images,
    // header band, footer band, floats, body), and nothing on an item says which band it came from. Its
    // position does: the footer sits in the bottom margin. Moving those items last makes a copied page
    // read header, body, footer.
    static List<PlacedItem> ReadingOrder(LaidOutPage page)
    {
        var footerTop = page.Settings.HeightPoints - page.Settings.MarginBottom;
        var body = new List<PlacedItem>(page.Items.Count);
        var footer = new List<PlacedItem>();
        foreach (var item in page.Items)
        {
            (item.Y >= footerTop ? footer : body).Add(item);
        }

        body.AddRange(footer);
        return body;
    }

    // One container's items (the page, a cell, a rotated group), with each line's ending assigned from
    // paragraph continuity. Lines that came out of table cells arrive with their ending already set.
    static List<Node> Container(IEnumerable<PlacedItem> items)
    {
        var nodes = new List<Node>();
        foreach (var item in items)
        {
            switch (item)
            {
                case PlacedLine line:
                    nodes.Add(Line(line));
                    break;
                case PlacedTableRow row:
                    Row(row, nodes);
                    break;
                case PlacedRotatedGroup group:
                    nodes.Add(new FrameNode('r', group.X, group.Y, group.Width, group.Height, group.RotationDegrees, true, Container(group.Items)));
                    break;
                case PlacedWordArt wordArt when wordArt.Visual.Text.Length > 0:
                    // A warp draws as one figure with no line geometry; the whole box stands in for its text.
                    nodes.Add(new BoxNode(wordArt.X, wordArt.Y, wordArt.Width, wordArt.Height, wordArt.Visual.Text));
                    break;
                // PlacedImage, PlacedShape, PlacedShading, PlacedBorder: nothing to read.
            }
        }

        AssignParagraphEnds(nodes);
        return nodes;
    }

    static LineNode Line(PlacedLine line)
    {
        var node = new LineNode(line.X, line.Y, line.Height, line.Baseline, line.Paragraph, line.LineIndex);

        // A plain tab leaves no run, only a gap — as does a justified line's removed space. A paragraph
        // with no tab in it can only mean the space; one with tabs means a tab where the gap is wide,
        // unless the line is justified, where wide gaps are the norm.
        var paragraph = line.Paragraph;
        var tabsPossible = paragraph.Runs.Any(_ => _.IsTab) && paragraph.Properties.Alignment != TextAlignment.Justify;

        SpanNode? previous = null;
        var leaderPending = false;
        foreach (var run in line.Runs)
        {
            // A tab leader is dots (or a rule) filling a tab's gap: the gap reads as the tab it is.
            if (run.Leader != TabLeader.None)
            {
                leaderPending = true;
                continue;
            }

            if (string.IsNullOrEmpty(run.Text))
            {
                continue;
            }

            var text = NormaliseSymbols(run.Text);
            if (previous is { } before)
            {
                var gapStart = before.X + before.Width;
                var gap = run.X - gapStart;
                if (leaderPending ||
                    (gap > minimumGap &&
                     !EndsWithWhitespace(before.Text) &&
                     !char.IsWhiteSpace(text[0]) &&
                     !ImageCovers(line.Images, gapStart, run.X)))
                {
                    var tab = leaderPending || (tabsPossible && gap > before.Size / 2);
                    node.Spans.Add(new(tab ? "\t" : " ", gapStart, Math.Max(gap, 0), before.Size, before.Bold, before.Italic, true, 0));
                }
            }

            leaderPending = false;
            var properties = run.Properties;
            var span = new SpanNode(
                text,
                run.X,
                run.Width,
                (float) VerticalRunPosition.RenderSizePoints(properties),
                properties.Bold,
                properties.Italic,
                false,
                run.BaselineShift);
            node.Spans.Add(span);
            previous = span;
        }

        return node;
    }

    // A row's cells in order. Each cell's text ends with a tab — the last one with a line break — so a
    // copied table pastes into a spreadsheet as cells. An empty cell still contributes its tab, or every
    // later column would shift left by one.
    static void Row(PlacedTableRow row, List<Node> nodes)
    {
        for (var index = 0; index < row.Cells.Count; index++)
        {
            var cell = row.Cells[index];
            var end = index == row.Cells.Count - 1 ? LineEnd.Break : LineEnd.Tab;

            // A cell's floats are painted outside its clip, so they stay outside the clip frame too.
            nodes.AddRange(Container(cell.Floats));

            var content = Container(cell.Content);
            if (LastEnded(content) is { } last)
            {
                last.End = end;
            }
            else
            {
                content.Add(new LineNode(cell.X, cell.Y, cell.Height, cell.Y + cell.Height, null, 0)
                {
                    End = end
                });
            }

            if (cell.ClipContent)
            {
                // The painter's clip: the box widened by its spill, or — for a Word exact-height row —
                // vertical only, since Word lets ink overhang a cell sideways.
                var clipX = cell.ClipHorizontally ? cell.X - cell.ClipSpillLeft : cell.X;
                var clipWidth = cell.ClipHorizontally ? cell.Width + cell.ClipSpillLeft + cell.ClipSpillRight : cell.Width;
                nodes.Add(new FrameNode('c', clipX, cell.Y, clipWidth, cell.Height, 0, cell.ClipHorizontally, content));
            }
            else
            {
                nodes.AddRange(content);
            }
        }
    }

    static void AssignParagraphEnds(List<Node> nodes)
    {
        for (var index = 0; index < nodes.Count; index++)
        {
            switch (nodes[index])
            {
                case LineNode { End: null } line:
                    var next = NextLine(nodes, index + 1);
                    var continues = next is { End: null } &&
                                    line.Paragraph is not null &&
                                    ReferenceEquals(next.Paragraph, line.Paragraph) &&
                                    next.LineIndex == line.LineIndex + 1;
                    line.End = continues ? SoftWrapEnd(line) : LineEnd.Break;
                    break;
                case BoxNode { End: null } box:
                    box.End = LineEnd.Break;
                    break;
            }
        }
    }

    static LineNode? NextLine(List<Node> nodes, int start)
    {
        for (var index = start; index < nodes.Count; index++)
        {
            if (nodes[index] is LineNode line)
            {
                return line;
            }
        }

        return null;
    }

    // A wrap inside a paragraph drops the space the break replaced; put it back so the copied words don't
    // run together — unless the line already ends in whitespace, or in a hyphen the word broke at.
    static LineEnd SoftWrapEnd(LineNode line)
    {
        if (line.Spans.Count == 0)
        {
            return LineEnd.Space;
        }

        var text = line.Spans[^1].Text;
        var last = text[^1];
        return char.IsWhiteSpace(last) || last is '-' or '‐' or '­'
            ? LineEnd.None
            : LineEnd.Space;
    }

    static EndedNode? LastEnded(List<Node> nodes)
    {
        for (var index = nodes.Count - 1; index >= 0; index--)
        {
            switch (nodes[index])
            {
                case EndedNode ended:
                    return ended;
                case FrameNode frame when LastEnded(frame.Children) is { } nested:
                    return nested;
            }
        }

        return null;
    }

    static bool ImageCovers(IReadOnlyList<PlacedImage> images, float gapStart, float gapEnd)
    {
        foreach (var image in images)
        {
            if (image.X < gapEnd && image.X + image.Width > gapStart)
            {
                return true;
            }
        }

        return false;
    }

    static bool EndsWithWhitespace(string text) =>
        char.IsWhiteSpace(text[^1]);

    // Symbol-font list markers (Wingdings, Symbol) arrive as private-use code points, which copy as
    // nothing readable. They are bullets on the page, so they become a bullet in the text.
    static string NormaliseSymbols(string text)
    {
        if (!text.Any(IsPrivateUse))
        {
            return text;
        }

        var builder = new StringBuilder(text.Length);
        foreach (var ch in text)
        {
            builder.Append(IsPrivateUse(ch) ? '•' : ch);
        }

        return builder.ToString();
    }

    static bool IsPrivateUse(char ch) =>
        ch is >= '' and <= '';

    static void Write(Utf8JsonWriter json, Node node, float originX, float originY)
    {
        json.WriteStartObject();
        switch (node)
        {
            case LineNode line:
                WriteNumber(json, "x", line.X - originX);
                WriteNumber(json, "y", line.Y - originY);
                WriteNumber(json, "h", line.Height);
                WriteNumber(json, "b", line.Baseline - originY);
                json.WriteNumber("e", (int) (line.End ?? LineEnd.Break));
                json.WriteStartArray("s");
                foreach (var span in line.Spans)
                {
                    json.WriteStartObject();
                    WriteNumber(json, "x", span.X - originX);
                    WriteNumber(json, "w", span.Width);
                    json.WriteString("t", span.Text);
                    WriteNumber(json, "z", span.Size);
                    var flags = (span.Bold ? 1 : 0) | (span.Italic ? 2 : 0) | (span.Separator ? 4 : 0);
                    if (flags != 0)
                    {
                        json.WriteNumber("f", flags);
                    }

                    if (span.Shift != 0)
                    {
                        WriteNumber(json, "d", span.Shift);
                    }

                    json.WriteEndObject();
                }

                json.WriteEndArray();
                break;
            case BoxNode box:
                json.WriteString("k", "b");
                WriteBox(json, box.X - originX, box.Y - originY, box.Width, box.Height);
                json.WriteString("t", box.Text);
                json.WriteNumber("e", (int) (box.End ?? LineEnd.Break));
                break;
            case FrameNode frame:
                json.WriteString("k", frame.Kind == 'r' ? "r" : "c");
                WriteBox(json, frame.X - originX, frame.Y - originY, frame.Width, frame.Height);
                if (frame.Kind == 'r')
                {
                    WriteNumber(json, "r", frame.Rotation);
                }
                else
                {
                    json.WriteNumber("cx", frame.ClipHorizontally ? 1 : 0);
                }

                json.WriteStartArray("i");
                foreach (var child in frame.Children)
                {
                    Write(json, child, frame.X, frame.Y);
                }

                json.WriteEndArray();
                break;
        }

        json.WriteEndObject();
    }

    static void WriteBox(Utf8JsonWriter json, double x, double y, double width, double height)
    {
        WriteNumber(json, "x", x);
        WriteNumber(json, "y", y);
        WriteNumber(json, "w", width);
        WriteNumber(json, "h", height);
    }

    static void WriteNumber(Utf8JsonWriter json, string name, double value) =>
        json.WriteNumber(name, Math.Round(value, 2));

    // The text the layer's DOM will hold, in DOM order: each span's text (a synthesised separator
    // included), then the line's ending. morph-text.js renders a tab separator as a space glyph but copies
    // it as a tab, so the two agree in both content and length.
    static void AppendText(StringBuilder text, Node node)
    {
        switch (node)
        {
            case LineNode line:
                foreach (var span in line.Spans)
                {
                    text.Append(span.Text);
                }

                AppendEnd(text, line.End);
                break;
            case BoxNode box:
                text.Append(box.Text);
                AppendEnd(text, box.End);
                break;
            case FrameNode frame:
                foreach (var child in frame.Children)
                {
                    AppendText(text, child);
                }

                break;
        }
    }

    static void AppendEnd(StringBuilder text, LineEnd? end)
    {
        switch (end ?? LineEnd.Break)
        {
            case LineEnd.Space:
                text.Append(' ');
                break;
            case LineEnd.Tab:
                text.Append('\t');
                break;
            case LineEnd.Break:
                text.Append('\n');
                break;
        }
    }

    enum LineEnd
    {
        None = 0,
        Space = 1,
        Break = 2,
        Tab = 3
    }

    abstract class Node;

    abstract class EndedNode : Node
    {
        public LineEnd? End { get; set; }
    }

    sealed class LineNode(float x, float y, float height, float baseline, ParagraphElement? paragraph, int lineIndex) : EndedNode
    {
        public float X { get; } = x;
        public float Y { get; } = y;
        public float Height { get; } = height;
        public float Baseline { get; } = baseline;

        // Null for a synthesised empty-cell line, which continues no paragraph.
        public ParagraphElement? Paragraph { get; } = paragraph;
        public int LineIndex { get; } = lineIndex;
        public List<SpanNode> Spans { get; } = [];
    }

    sealed class BoxNode(float x, float y, float width, float height, string text) : EndedNode
    {
        public float X { get; } = x;
        public float Y { get; } = y;
        public float Width { get; } = width;
        public float Height { get; } = height;
        public string Text { get; } = text;
    }

    sealed class FrameNode(char kind, float x, float y, float width, float height, double rotation, bool clipHorizontally, List<Node> children) : Node
    {
        public char Kind { get; } = kind;
        public float X { get; } = x;
        public float Y { get; } = y;
        public float Width { get; } = width;
        public float Height { get; } = height;
        public double Rotation { get; } = rotation;
        public bool ClipHorizontally { get; } = clipHorizontally;
        public List<Node> Children { get; } = children;
    }

    readonly record struct SpanNode(string Text, float X, float Width, float Size, bool Bold, bool Italic, bool Separator, float Shift);
}
