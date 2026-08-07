/// <summary>
/// Tests for the sign of w:pgMar/@w:top, which decides whether a header taller than the top margin
/// pushes the body down or is allowed to overlap it (ECMA-376 §17.6.11).
/// </summary>
public class HeaderSpaceReservationTests
{
    static PageSettings Parse(params string[] scenario)
    {
        var segments = new[] {ProjectFiles.ProjectDirectory, "Inputs"}
            .Concat(scenario)
            .Append("input.docx")
            .ToArray();
        return new DocumentParser().Parse(Path.Combine(segments)).PageSettings;
    }

    /// <summary>
    /// The ordinary case: a positive w:top is a MINIMUM, so an overflowing header moves the body.
    /// </summary>
    [Test]
    public async Task PositiveTopMargin_IsNotAbsolute()
    {
        var settings = Parse("nonstandard_main_part_name");

        // w:top=1440 twips
        await Assert.That(settings.MarginTop).IsEqualTo(72);
        await Assert.That(settings.TopMarginIsAbsolute).IsFalse();
    }

    /// <summary>
    /// A NEGATIVE w:top means |val| is the absolute distance from the top of the page to the top
    /// of the body, so the header overlaps rather than displacing it. The magnitude is still what
    /// the body is laid out against — only the push is suppressed.
    /// </summary>
    [Test]
    public async Task NegativeTopMargin_IsAbsoluteAndKeepsItsMagnitude()
    {
        var settings = Parse("agendas-minutes", "11");

        // w:top=-265 twips
        await Assert.That(settings.MarginTop).IsEqualTo(13.25);
        await Assert.That(settings.TopMarginIsAbsolute).IsTrue();
    }

    /// <summary>A document with no explicit w:pgMar keeps the 1in default and is not absolute.</summary>
    [Test]
    public async Task DefaultPageSettings_AreNotAbsolute() =>
        await Assert.That(new PageSettings().TopMarginIsAbsolute).IsFalse();
}
