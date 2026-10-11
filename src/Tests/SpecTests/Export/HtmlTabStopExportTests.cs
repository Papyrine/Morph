/// <summary>
/// Tests for which declared stop a tab goes to in the HTML export. A tab advances to the first
/// stop PAST the pen, so a stop the text before it has already passed is skipped; the export
/// measures nothing, so only a stop the text has certainly passed counts as passed.
/// </summary>
public class HtmlTabStopExportTests
{
    static ParagraphElement Tabbed(TabStop[] stops, params string[] segments)
    {
        var runs = new List<Run>();
        foreach (var segment in segments)
        {
            if (runs.Count > 0)
            {
                runs.Add(new() {IsTab = true, Text = "\t"});
            }

            runs.Add(TextRun(segment, fontSize: 10));
        }

        return new()
        {
            Runs = runs,
            Properties = new()
            {
                SpacingAfterPoints = 8,
                TabStops = stops
            }
        };
    }

    static TabStop Left(double position) =>
        new() {PositionPoints = position, Alignment = TabAlignment.Left};

    static string Export(params DocumentElement[] elements) =>
        HtmlExporter.Export(Doc(elements), new() {EmitDocument = false});

    /// <summary>
    /// resumes/11 sets three columns on stops at 36, 216 and 396pt, and its first entry is wider
    /// than 36pt, so Word's tab goes on to 216. Paired with the first stop declared, the second
    /// column started wherever the first entry ended.
    /// </summary>
    [Test]
    public async Task StopTheTextHasPassed_IsSkipped()
    {
        var html = Export(Tabbed([Left(36), Left(216), Left(396)], "chanchals@example.com", "(718) 555-0100", "LinkedIn profile"));

        await Assert.That(html).Contains("<span style=\"min-width: 216pt\">");
        await Assert.That(html).Contains("<span style=\"min-width: 180pt\">");
        await Assert.That(html).DoesNotContain("min-width: 36pt");
    }

    /// <summary>A stop the text may or may not have reached is still the one the tab goes to.</summary>
    [Test]
    public async Task StopTheTextMayNotHaveReached_IsTaken()
    {
        var html = Export(Tabbed([Left(36), Left(216)], "Name", "value"));

        await Assert.That(html).Contains("<span style=\"min-width: 36pt\">");
    }

    /// <summary>A tab with no stop left to go to puts the paragraph back on the plain path.</summary>
    [Test]
    public async Task TabPastEveryStop_FallsBackToASpace()
    {
        var html = Export(Tabbed([Left(36)], "a line of text long enough to be past its only stop", "after"));

        await Assert.That(html).DoesNotContain("display: flex");
        await Assert.That(html).Contains("stop</span> <span");
    }
}
