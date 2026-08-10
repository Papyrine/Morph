namespace Morph;

/// <summary>
/// Converts Excel workbooks to vector-text PDF using PdfSharp. For parse-once / export-many
/// workflows, prefer <see cref="ExcelDocument"/> plus its <c>ExportToPdf</c> extension.
/// </summary>
public sealed class PdfExcelConverter
{
    /// <summary>Converts an XLSX file to a PDF byte array.</summary>
    public static byte[] ConvertToPdf(string xlsxPath, PdfExportOptions? options = null)
    {
        using var stream = File.OpenRead(xlsxPath);
        return ConvertToPdf(stream, options);
    }

    /// <summary>Converts an XLSX stream to a PDF byte array.</summary>
    public static byte[] ConvertToPdf(Stream xlsxStream, PdfExportOptions? options = null) =>
        PdfRenderer.Render(ExcelConverter.Parse(xlsxStream, options), options);

    /// <summary>Converts an XLSX file to a PDF written to <paramref name="outputPdfPath"/>.</summary>
    public static void ConvertToPdf(string xlsxPath, string outputPdfPath, PdfExportOptions? options = null) =>
        File.WriteAllBytes(outputPdfPath, ConvertToPdf(xlsxPath, options));

    /// <summary>Converts an XLSX stream to a PDF written to <paramref name="output"/>.</summary>
    public static void ConvertToPdf(Stream xlsxStream, Stream output, PdfExportOptions? options = null)
    {
        var pdf = ConvertToPdf(xlsxStream, options);
        output.Write(pdf, 0, pdf.Length);
    }
}
