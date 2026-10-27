using System.IO.Compression;

/// <summary>
/// What <see cref="MetafileReader"/> plays out of a metafile: the EMF preview a legacy MS Graph chart
/// leaves in a deck (ole-chart-preview), and a WMF built record by record here, since the corpus holds
/// none.
/// </summary>
public class MetafileReaderTests
{
    static readonly string deck = Path.Combine(ScenarioInputs.Root(ScenarioFormat.PowerPoint), "ole-chart-preview", "input.pptx");

    static byte[] Preview()
    {
        using var archive = ZipFile.OpenRead(deck);
        using var stream = archive.GetEntry("ppt/media/image2.emf")!.Open();
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }

    [Test]
    public async Task The_chart_preview_plays_into_its_bars_and_labels()
    {
        var drawing = MetafileReader.Read(Preview())!;
        var texts = drawing.Items.OfType<MetafileText>().Select(_ => _.Text).ToList();

        // Every label MS Graph drew: the categories, the series in the legend, the axis's ticks.
        await Assert.That(texts).Contains("Row 1");
        await Assert.That(texts).Contains("Row 4");
        await Assert.That(texts).Contains("Column 3");
        await Assert.That(texts).Contains("12");

        // Its three series' bars are filled in their own colours.
        var fills = drawing.Items.OfType<MetafilePath>().Select(_ => _.FillHex).OfType<string>().ToHashSet();
        await Assert.That(fills).Contains("004586");
        await Assert.That(fills).Contains("FF420E");
        await Assert.That(fills).Contains("FFD320");
    }

    [Test]
    public async Task The_chart_preview_lies_inside_its_frame()
    {
        var drawing = MetafileReader.Read(Preview())!;
        var points = drawing.Items.OfType<MetafilePath>().SelectMany(_ => _.Figures).SelectMany(_ => _).ToList();

        await Assert.That(points.Min(_ => _.X)).IsGreaterThanOrEqualTo(-0.01);
        await Assert.That(points.Max(_ => _.X)).IsLessThanOrEqualTo(1.01);
        await Assert.That(points.Min(_ => _.Y)).IsGreaterThanOrEqualTo(-0.01);
        await Assert.That(points.Max(_ => _.Y)).IsLessThanOrEqualTo(1.01);
    }

    // A placeable WMF on a 1000 x 500 box: a red square with a blue pen, and "Hi" centred on a baseline.
    internal static byte[] Wmf()
    {
        var records = new List<byte[]>
        {
            Record(0x02FA, Words(0, 10, 0), Color(0x0000FF)),
            Record(0x02FC, Words(0), Color(0xFF0000), Words(0)),
            Record(0x012D, Words(0)),
            Record(0x012D, Words(1)),
            Record(0x0324, Words(4, 100, 100, 300, 100, 300, 300, 100, 300)),
            Record(0x012E, Words(6 | 24)),
            Record(0x0209, Color(0x00FF00)),
            Record(0x0521, Words(2), "Hi"u8.ToArray(), Words(250, 500)),
            Record(0x0000)
        };

        var body = records.SelectMany(_ => _).ToArray();
        var placeable = new List<byte>();
        placeable.AddRange(BitConverter.GetBytes(0x9AC6CDD7u));
        placeable.AddRange(Words(0, 0, 0, 1000, 500, 1440));
        placeable.AddRange(BitConverter.GetBytes(0u));
        placeable.AddRange(Words(0));
        var header = new List<byte>();
        header.AddRange(Words(1, 9, 0x0300));
        header.AddRange(BitConverter.GetBytes((uint) (9 + body.Length / 2)));
        header.AddRange(Words(2));
        header.AddRange(BitConverter.GetBytes(0u));
        header.AddRange(Words(0));
        return [.. placeable, .. header, .. body];

        static byte[] Words(params int[] values) =>
            [.. values.SelectMany(_ => BitConverter.GetBytes((short) _))];

        // A COLORREF from 0xRRGGBB.
        static byte[] Color(int rgb) =>
            BitConverter.GetBytes((uint) (((rgb & 0xFF) << 16) | (rgb & 0xFF00) | ((rgb >> 16) & 0xFF)));

        static byte[] Record(int function, params byte[][] parameters)
        {
            var body = parameters.SelectMany(_ => _).ToArray();
            return [.. BitConverter.GetBytes((uint) (3 + body.Length / 2)), .. BitConverter.GetBytes((ushort) function), .. body];
        }
    }

    [Test]
    public async Task A_wmf_plays_its_shapes_and_text_in_its_placeable_box()
    {
        var drawing = MetafileReader.Read(Wmf())!;
        var square = drawing.Items.OfType<MetafilePath>().Single();
        var text = drawing.Items.OfType<MetafileText>().Single();

        await Assert.That(square.FillHex).IsEqualTo("FF0000");
        await Assert.That(square.LineHex).IsEqualTo("0000FF");
        await Assert.That(square.Figures[0][0]).IsEqualTo((0.1, 0.2));
        await Assert.That(square.Figures[0][2]).IsEqualTo((0.3, 0.6));

        await Assert.That(text.Text).IsEqualTo("Hi");
        await Assert.That(text.ColorHex).IsEqualTo("00FF00");
        await Assert.That((text.X, text.Y)).IsEqualTo((0.5, 0.5));
        await Assert.That(text.Horizontal).IsEqualTo(MetafileTextAlign.Centre);
        await Assert.That(text.Vertical).IsEqualTo(MetafileTextBaseline.Baseline);
    }

    [Test]
    public async Task Neither_format_is_no_drawing() =>
        await Assert.That(MetafileReader.Read([1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20])).IsNull();
}
