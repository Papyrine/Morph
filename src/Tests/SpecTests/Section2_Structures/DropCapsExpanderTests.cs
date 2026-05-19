public class DropCapsExpanderTests
{
    [Test]
    public async Task PassesThroughWhenNoDropCap()
    {
        var runs = new List<Run>
        {
            new() {Text = "Hello", Properties = new() {FontSizePoints = 12}}
        };
        var props = new ParagraphProperties();

        var result = DropCapsExpander.Expand(runs, props);

        await Assert.That(result).IsSameReferenceAs(runs);
    }

    [Test]
    public async Task PassesThroughWhenDropCapLinesIsOneOrLess()
    {
        var runs = new List<Run>
        {
            new() {Text = "Hello", Properties = new() {FontSizePoints = 12}}
        };
        var props = new ParagraphProperties {DropCap = DropCapPosition.Drop, DropCapLines = 1};

        var result = DropCapsExpander.Expand(runs, props);

        await Assert.That(result).IsSameReferenceAs(runs);
    }

    [Test]
    public async Task ExpandsFirstCharIntoLargeCap()
    {
        var runs = new List<Run>
        {
            new() {Text = "Hello world", Properties = new() {FontSizePoints = 11}}
        };
        var props = new ParagraphProperties {DropCap = DropCapPosition.Drop, DropCapLines = 3};

        var result = DropCapsExpander.Expand(runs, props);

        // Cap "H" at 33pt, line break, then "ello world"
        await Assert.That(result.Count).IsEqualTo(3);
        await Assert.That(result[0].Text).IsEqualTo("H");
        await Assert.That(result[0].Properties.FontSizePoints).IsEqualTo(33);
        await Assert.That(result[1].Text).IsEqualTo("\n");
        await Assert.That(result[2].Text).IsEqualTo("ello world");
        await Assert.That(result[2].Properties.FontSizePoints).IsEqualTo(11);
    }

    [Test]
    public async Task PreservesOtherRunPropertiesOnCap()
    {
        var runs = new List<Run>
        {
            new()
            {
                Text = "Once upon a time",
                Properties = new() {FontSizePoints = 12, Bold = true, ColorHex = "AA0000"}
            }
        };
        var props = new ParagraphProperties {DropCap = DropCapPosition.Drop, DropCapLines = 2};

        var result = DropCapsExpander.Expand(runs, props);

        await Assert.That(result[0].Properties.Bold).IsTrue();
        await Assert.That(result[0].Properties.ColorHex).IsEqualTo("AA0000");
        await Assert.That(result[0].Properties.FontSizePoints).IsEqualTo(24);
    }

    [Test]
    public async Task SkipsLeadingTabAndImageRunsToFindFirstTextRun()
    {
        var runs = new List<Run>
        {
            new() {Text = "\t", IsTab = true, Properties = new() {FontSizePoints = 11}},
            new() {Text = "", InlineImageData = [1, 2], Properties = new() {FontSizePoints = 11}},
            new() {Text = "Real text", Properties = new() {FontSizePoints = 11}}
        };
        var props = new ParagraphProperties {DropCap = DropCapPosition.Drop, DropCapLines = 2};

        var result = DropCapsExpander.Expand(runs, props);

        // Tab + image preserved, then cap "R" + line break + "eal text"
        await Assert.That(result.Count).IsEqualTo(5);
        await Assert.That(result[0].IsTab).IsTrue();
        await Assert.That(result[1].InlineImageData).IsNotNull();
        await Assert.That(result[2].Text).IsEqualTo("R");
        await Assert.That(result[2].Properties.FontSizePoints).IsEqualTo(22);
        await Assert.That(result[3].Text).IsEqualTo("\n");
        await Assert.That(result[4].Text).IsEqualTo("eal text");
    }

    [Test]
    public async Task ReturnsRunsWhenNoTextRunFound()
    {
        var runs = new List<Run>
        {
            new() {Text = "\t", IsTab = true, Properties = new()},
            new() {Text = "", Properties = new()}
        };
        var props = new ParagraphProperties {DropCap = DropCapPosition.Drop, DropCapLines = 3};

        var result = DropCapsExpander.Expand(runs, props);

        await Assert.That(result).IsSameReferenceAs(runs);
    }

    [Test]
    public async Task EmptyRunListPassesThrough()
    {
        var runs = new List<Run>();
        var props = new ParagraphProperties {DropCap = DropCapPosition.Drop, DropCapLines = 3};

        var result = DropCapsExpander.Expand(runs, props);

        await Assert.That(result).IsSameReferenceAs(runs);
    }
}
