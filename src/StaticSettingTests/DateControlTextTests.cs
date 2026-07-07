using System.Globalization;

/// <summary>
/// Guards that date content controls render deterministically regardless of the host machine's
/// culture. Lives in the single-threaded StaticSettingTests project because it mutates the
/// process-wide <see cref="CultureInfo.CurrentCulture"/>; running in parallel would let the
/// forced culture leak into other tests.
/// </summary>
public class DateControlTextTests
{
    static readonly DateTime sample = new(2026, 7, 6);

    // Two cultures whose short-date formats differ sharply from each other and from ISO:
    // de-DE is dd.MM.yyyy, en-US is M/d/yyyy. If the resolver leaked CurrentCulture (as the old
    // ToShortDateString did), the fallback output would differ between them.
    static readonly CultureInfo german = CultureInfo.GetCultureInfo("de-DE");
    static readonly CultureInfo american = CultureInfo.GetCultureInfo("en-US");

    [Test]
    public async Task Fallback_IsIdenticalAcrossCultures()
    {
        var control = new ContentControlElement
        {
            ControlType = ContentControlType.Date,
            DateValue = sample,
            DateFormat = "yyyy-MM-dd"
        };

        var underGerman = ResolveUnder(german, control);
        var underAmerican = ResolveUnder(american, control);

        await Assert.That(underGerman).IsEqualTo(underAmerican);
        await Assert.That(underGerman).IsEqualTo("2026-07-06");
    }

    [Test]
    public async Task Fallback_HonoursDeclaredFormat_Invariantly()
    {
        var control = new ContentControlElement
        {
            ControlType = ContentControlType.Date,
            DateValue = sample,
            DateFormat = "dd MMM yyyy"
        };

        // "MMM" resolves to the invariant English abbreviation ("Jul") under any thread culture.
        await Assert.That(ResolveUnder(german, control)).IsEqualTo("06 Jul 2026");
        await Assert.That(ResolveUnder(american, control)).IsEqualTo("06 Jul 2026");
    }

    [Test]
    public async Task Fallback_MissingFormat_UsesIsoDefault()
    {
        var control = new ContentControlElement
        {
            ControlType = ContentControlType.Date,
            DateValue = sample
        };

        await Assert.That(ResolveUnder(german, control)).IsEqualTo("2026-07-06");
    }

    [Test]
    public async Task Fallback_InvalidFormat_DoesNotThrow()
    {
        // Word's w:dateFormat is not guaranteed to be a valid .NET format string; "j" is an
        // unknown single-character (standard) specifier and throws FormatException when applied.
        var control = new ContentControlElement
        {
            ControlType = ContentControlType.Date,
            DateValue = sample,
            DateFormat = "j"
        };

        await Assert.That(ResolveUnder(american, control)).IsEqualTo("2026-07-06");
    }

    [Test]
    public async Task RunText_IsReturnedVerbatim_RegardlessOfCulture()
    {
        // Word displays the control's run text verbatim; the resolver must prefer it over the
        // canonical fullDate even when a (differently formatted) DateValue is also present.
        var control = new ContentControlElement
        {
            ControlType = ContentControlType.Date,
            Content = "6 July 2026",
            DateValue = sample,
            DateFormat = "yyyy-MM-dd"
        };

        await Assert.That(ResolveUnder(german, control)).IsEqualTo("6 July 2026");
        await Assert.That(ResolveUnder(american, control)).IsEqualTo("6 July 2026");
    }

    [Test]
    public async Task Empty_ReturnsPlaceholder()
    {
        var control = new ContentControlElement
        {
            ControlType = ContentControlType.Date,
            PlaceholderText = "Click to enter a date."
        };

        await Assert.That(ResolveUnder(german, control)).IsEqualTo("Click to enter a date.");
    }

    static string ResolveUnder(CultureInfo culture, ContentControlElement control)
    {
        var original = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = culture;
        try
        {
            return DateControlText.Resolve(control);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }
}
