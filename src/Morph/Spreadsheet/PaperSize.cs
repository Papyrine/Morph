/// <summary>
/// Paper dimensions for the <c>paperSize</c> codes a worksheet's <c>pageSetup</c> uses (ECMA-376
/// §18.18.50, which follows the Windows <c>DMPAPER_*</c> numbering).
///
/// Only the sizes a real workbook picks are listed. A sheet that names none — 72 of the corpus's 77
/// — takes the region default, which the test harness pins to A4 through
/// <c>DefaultPageSize</c> exactly as the DOCX path does.
/// </summary>
static class PaperSize
{
    const double pointsPerInch = 72.0;
    const double pointsPerMillimetre = 72.0 / 25.4;

    /// <summary>Portrait dimensions in points for a paper code, defaulting to the region's paper.</summary>
    public static (double Width, double Height) Resolve(uint? code) =>
        code switch
        {
            1 or 2 => (8.5 * pointsPerInch, 11 * pointsPerInch),      // Letter
            3 => (11 * pointsPerInch, 17 * pointsPerInch),            // Tabloid
            5 => (8.5 * pointsPerInch, 14 * pointsPerInch),           // Legal
            7 => (5.5 * pointsPerInch, 8.5 * pointsPerInch),          // Statement
            8 => (297 * pointsPerMillimetre, 420 * pointsPerMillimetre),  // A3
            9 or 10 => (210 * pointsPerMillimetre, 297 * pointsPerMillimetre), // A4
            11 => (148 * pointsPerMillimetre, 210 * pointsPerMillimetre), // A5
            13 => (182 * pointsPerMillimetre, 257 * pointsPerMillimetre), // B5
            _ => (DefaultPageSize.WidthPoints, DefaultPageSize.HeightPoints)
        };
}
