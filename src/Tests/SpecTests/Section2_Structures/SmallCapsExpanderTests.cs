public class SmallCapsExpanderTests
{
    [Test]
    public async Task PassesThroughWhenNoSmallCapsRunsPresent()
    {
        var runs = new List<Run>
        {
            new() { Text = "Hello", Properties = new() { FontSizePoints = 12 } },
            new() { Text = " world", Properties = new() { FontSizePoints = 14 } }
        };

        var result = SmallCapsExpander.Expand(runs);

        await Assert.That(result).IsSameReferenceAs(runs);
    }

    [Test]
    public async Task SplitsLowercaseAndUppercaseSegments()
    {
        var runs = new List<Run>
        {
            new()
            {
                Text = "Hello World",
                Properties = new() { FontSizePoints = 10, SmallCaps = true }
            }
        };

        var result = SmallCapsExpander.Expand(runs);

        // "Hello World" → "H" (10pt) | "ELLO" (8pt) | " W" (10pt) | "ORLD" (8pt)
        await Assert.That(result.Count).IsEqualTo(4);

        await Assert.That(result[0].Text).IsEqualTo("H");
        await Assert.That(result[0].Properties.FontSizePoints).IsEqualTo(10);
        await Assert.That(result[0].Properties.SmallCaps).IsFalse();

        await Assert.That(result[1].Text).IsEqualTo("ELLO");
        await Assert.That(result[1].Properties.FontSizePoints).IsEqualTo(8);
        await Assert.That(result[1].Properties.SmallCaps).IsFalse();

        await Assert.That(result[2].Text).IsEqualTo(" W");
        await Assert.That(result[2].Properties.FontSizePoints).IsEqualTo(10);

        await Assert.That(result[3].Text).IsEqualTo("ORLD");
        await Assert.That(result[3].Properties.FontSizePoints).IsEqualTo(8);
    }

    [Test]
    public async Task GroupsNonLetterCharactersWithUppercaseRun()
    {
        var runs = new List<Run>
        {
            new()
            {
                Text = "ABC 123",
                Properties = new() { FontSizePoints = 10, SmallCaps = true }
            }
        };

        var result = SmallCapsExpander.Expand(runs);

        await Assert.That(result.Count).IsEqualTo(1);
        await Assert.That(result[0].Text).IsEqualTo("ABC 123");
        await Assert.That(result[0].Properties.FontSizePoints).IsEqualTo(10);
    }

    [Test]
    public async Task AllLowercaseProducesSingleScaledUppercaseSegment()
    {
        var runs = new List<Run>
        {
            new()
            {
                Text = "hello",
                Properties = new() { FontSizePoints = 10, SmallCaps = true }
            }
        };

        var result = SmallCapsExpander.Expand(runs);

        await Assert.That(result.Count).IsEqualTo(1);
        await Assert.That(result[0].Text).IsEqualTo("HELLO");
        await Assert.That(result[0].Properties.FontSizePoints).IsEqualTo(8);
    }

    [Test]
    public async Task PreservesOtherRunPropertiesAcrossSplits()
    {
        var runs = new List<Run>
        {
            new()
            {
                Text = "Ab",
                Properties = new()
                {
                    FontSizePoints = 10,
                    SmallCaps = true,
                    Bold = true,
                    ColorHex = "FF0000"
                }
            }
        };

        var result = SmallCapsExpander.Expand(runs);

        await Assert.That(result.Count).IsEqualTo(2);
        await Assert.That(result[0].Properties.Bold).IsTrue();
        await Assert.That(result[0].Properties.ColorHex).IsEqualTo("FF0000");
        await Assert.That(result[1].Properties.Bold).IsTrue();
        await Assert.That(result[1].Properties.ColorHex).IsEqualTo("FF0000");
    }

    [Test]
    public async Task LeavesTabAndImageRunsUntouched()
    {
        var runs = new List<Run>
        {
            new()
            {
                Text = "\t",
                IsTab = true,
                Properties = new() { FontSizePoints = 10, SmallCaps = true }
            },
            new()
            {
                Text = "",
                InlineImageData = [1, 2, 3],
                Properties = new() { FontSizePoints = 10, SmallCaps = true }
            }
        };

        var result = SmallCapsExpander.Expand(runs);

        await Assert.That(result.Count).IsEqualTo(2);
        await Assert.That(result[0].IsTab).IsTrue();
        await Assert.That(result[1].InlineImageData).IsNotNull();
    }

    [Test]
    public async Task EmptyTextRunPassesThrough()
    {
        var runs = new List<Run>
        {
            new()
            {
                Text = "",
                Properties = new() { FontSizePoints = 10, SmallCaps = true }
            }
        };

        var result = SmallCapsExpander.Expand(runs);

        await Assert.That(result.Count).IsEqualTo(1);
        await Assert.That(result[0].Text).IsEqualTo("");
    }
}
