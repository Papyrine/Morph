/// <summary>
/// The format codes for builtin <c>numFmtId</c> values, which a workbook references by number
/// without ever writing out (ECMA-376 §18.8.30, table of implied formats). Ids 164 and above are
/// custom and carry their own <c>formatCode</c> in <c>styles.xml</c>.
///
/// Every id the corpus actually uses is covered: 14 (dates) leads by a wide margin, then the
/// currency and percent group (7, 9, 10) and accounting (37, 41-44).
///
/// The locale-dependent ids (14-22) are given their en-US forms. Excel resolves those against the
/// system locale, but the test harness pins culture (see the Tests ModuleInitializer) and the corpus
/// is en-US authored, so a fixed table is both correct here and reproducible.
/// </summary>
static class BuiltinNumberFormats
{
    static readonly Dictionary<int, string> codes = new()
    {
        [0] = "General",
        [1] = "0",
        [2] = "0.00",
        [3] = "#,##0",
        [4] = "#,##0.00",
        [5] = "\"$\"#,##0_);(\"$\"#,##0)",
        [6] = "\"$\"#,##0_);[Red](\"$\"#,##0)",
        [7] = "\"$\"#,##0.00_);(\"$\"#,##0.00)",
        [8] = "\"$\"#,##0.00_);[Red](\"$\"#,##0.00)",
        [9] = "0%",
        [10] = "0.00%",
        [11] = "0.00E+00",
        [12] = "# ?/?",
        [13] = "# ??/??",
        [14] = "m/d/yyyy",
        [15] = "d-mmm-yy",
        [16] = "d-mmm",
        [17] = "mmm-yy",
        [18] = "h:mm AM/PM",
        [19] = "h:mm:ss AM/PM",
        [20] = "h:mm",
        [21] = "h:mm:ss",
        [22] = "m/d/yyyy h:mm",
        [37] = "#,##0_);(#,##0)",
        [38] = "#,##0_);[Red](#,##0)",
        [39] = "#,##0.00_);(#,##0.00)",
        [40] = "#,##0.00_);[Red](#,##0.00)",
        [41] = "_(* #,##0_);_(* (#,##0);_(* \"-\"_);_(@_)",
        [42] = "_(\"$\"* #,##0_);_(\"$\"* (#,##0);_(\"$\"* \"-\"_);_(@_)",
        [43] = "_(* #,##0.00_);_(* (#,##0.00);_(* \"-\"??_);_(@_)",
        [44] = "_(\"$\"* #,##0.00_);_(\"$\"* (#,##0.00);_(\"$\"* \"-\"??_);_(@_)",
        [45] = "mm:ss",
        [46] = "[h]:mm:ss",
        [47] = "mm:ss.0",
        [48] = "##0.0E+0",
        [49] = "@"
    };

    /// <summary>
    /// The code for a builtin id, or null when the id is custom (164+) or one of the reserved gaps
    /// (23-36) that no format is defined for.
    /// </summary>
    public static string? Code(int id) => codes.GetValueOrDefault(id);

    /// <summary>
    /// Whether an id is one of the date/time builtins. Useful before a format code is even resolved —
    /// a cell carrying id 14 holds a date serial whatever else is known about it.
    /// </summary>
    public static bool IsDate(int id) => id is
        >= 14 and <= 22 or
        >= 45 and <= 47;
}
