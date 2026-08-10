/// <summary>
/// Spec tests for the ECMA-376 number-format engine.
///
/// The expectations are Excel's own output, and the cases are drawn from the format codes the
/// spreadsheet corpus actually contains — currency, accounting, dates, percent and the conditional
/// phone-number pattern — plus the language features those rest on. This runs without any rendering,
/// which is why it is the first thing built: a wrong number here is invisible in a page comparison,
/// showing up only as slightly-wrong text inside an otherwise correct-looking grid.
/// </summary>
public class NumberFormatTests
{
    public static IEnumerable<(string Code, double Value, string Expected)> NumericCases() =>
    [
        // General
        ("General", 1234.5, "1234.5"),
        ("General", 42, "42"),
        ("General", -7, "-7"),

        // Builtin shapes
        ("0", 42.6, "43"),
        ("0", -42.6, "-43"),
        ("0.00", 3.14159, "3.14"),
        ("#,##0", 1234567, "1,234,567"),
        ("#,##0.00", 1234.5, "1,234.50"),
        ("0%", 0.42, "42%"),
        ("0.00%", 0.4256, "42.56%"),

        // A '#' before the point drops a lone leading zero; a '0' keeps it.
        ("#.##", 0.5, ".5"),
        ("0.##", 0.5, "0.5"),

        // Zero padding
        ("00000", 42, "00042"),

        // The corpus's dominant currency code, and its bracketed-locale variant.
        ("\"$\"#,##0.00", 1234.5, "$1,234.50"),
        ("\"$\"#,##0.00", -1234.5, "-$1,234.50"),
        ("[$$-409]#,##0.00", 99.9, "$99.90"),

        // Negative section supplies its own parentheses, so no automatic minus is added.
        ("\"$\"#,##0.00_);(\"$\"#,##0.00)", -1234.5, "($1,234.50)"),
        ("\"$\"#,##0.00_);(\"$\"#,##0.00)", 1234.5, "$1,234.50 "),

        // Zero section
        ("0.00;-0.00;\"zero\"", 0, "zero"),

        // An empty section hides the value.
        ("0.00;;", 0, ""),

        // Trailing comma scales by a thousand.
        ("#,##0,", 1234567, "1,235"),

        // The conditional phone-number pattern the corpus uses.
        ("[<=9999999]###\\-####;\\(###\\)\\ ###\\-####", 5551234, "555-1234"),
        ("[<=9999999]###\\-####;\\(###\\)\\ ###\\-####", 2065551234, "(206) 555-1234")
    ];

    [Test]
    [MethodDataSource(nameof(NumericCases))]
    public async Task Numeric(string code, double value, string expected) =>
        await Assert.That(NumberFormat.Format(value, code).Text).IsEqualTo(expected);

    public static IEnumerable<(string Code, double Serial, string Expected)> DateCases() =>
    [
        // Serial 44927 is 2023-01-01; 1 is 1900-01-01.
        ("m/d/yyyy", 44927, "1/1/2023"),
        ("mm/dd/yy", 44927, "01/01/23"),
        ("m/d/yy", 45000, "3/15/23"),
        ("d-mmm-yy", 44927, "1-Jan-23"),
        ("mmmm d, yyyy", 44927, "January 1, 2023"),
        ("mmm", 44927, "Jan"),
        ("mmmmm", 44927, "J"),
        ("dddd", 44927, "Sunday"),
        ("ddd", 44927, "Sun"),
        ("m/d/yyyy", 1, "1/1/1900"),

        // 'm' is a minute next to an hour or a second, a month otherwise. 0.5 is noon.
        ("h:mm", 0.5, "12:00"),
        ("hh:mm:ss", 0.5, "12:00:00"),
        ("h:mm AM/PM", 0.5, "12:00 PM"),
        ("h:mm AM/PM", 0.25, "6:00 AM"),

        // Elapsed totals do not wrap: 1.5 days is 36 hours, not 12.
        ("[h]:mm", 1.5, "36:00")
    ];

    [Test]
    [MethodDataSource(nameof(DateCases))]
    public async Task Dates(string code, double serial, string expected) =>
        await Assert.That(NumberFormat.Format(serial, code).Text).IsEqualTo(expected);

    [Test]
    public async Task RedNegativeCarriesItsColour()
    {
        var negative = NumberFormat.Format(-5, "\"$\"#,##0.00_);[Red](\"$\"#,##0.00)");
        await Assert.That(negative.ColorHex).IsEqualTo("FF0000");

        var positive = NumberFormat.Format(5, "\"$\"#,##0.00_);[Red](\"$\"#,##0.00)");
        await Assert.That(positive.ColorHex).IsNull();
    }

    [Test]
    public async Task TextUsesTheFourthSectionOnly()
    {
        const string accounting = "_(* #,##0_);_(* (#,##0);_(* \"-\"_);_(@_)";
        await Assert.That(NumberFormat.FormatText("hello", accounting).Text).IsEqualTo(" hello ");

        // With fewer than four sections a text cell passes through untouched.
        await Assert.That(NumberFormat.FormatText("hello", "#,##0").Text).IsEqualTo("hello");
    }

    [Test]
    public async Task AccountingDashesZeroAndPadsWidth()
    {
        const string accounting = "_(\"$\"* #,##0.00_);_(\"$\"* (#,##0.00);_(\"$\"* \"-\"??_);_(@_)";
        await Assert.That(NumberFormat.Format(0, accounting).Text).Contains("-");
        await Assert.That(NumberFormat.Format(1234.5, accounting).Text).Contains("1,234.50");
        await Assert.That(NumberFormat.Format(-1234.5, accounting).Text).Contains("(1,234.50)");
    }

    [Test]
    [Arguments(14, "m/d/yyyy")]
    [Arguments(9, "0%")]
    [Arguments(44, "_(\"$\"* #,##0.00_);_(\"$\"* (#,##0.00);_(\"$\"* \"-\"??_);_(@_)")]
    [Arguments(49, "@")]
    public async Task BuiltinIdsResolve(int id, string expected) =>
        await Assert.That(BuiltinNumberFormats.Code(id)).IsEqualTo(expected);

    [Test]
    public async Task CustomIdsAreNotBuiltin() =>
        await Assert.That(BuiltinNumberFormats.Code(164)).IsNull();
}
