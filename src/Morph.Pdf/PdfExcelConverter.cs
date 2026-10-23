namespace Morph;

/// <summary>
/// Converts Excel workbooks to vector-text PDF using Krilla. For parse-once / export-many
/// workflows, prefer <see cref="ExcelDocument"/> plus its <c>ExportToPdf</c> extension.
/// </summary>
public sealed class PdfExcelConverter
{
    /// <summary>Converts an XLSX file to a PDF byte array.</summary>
    public static byte[] ConvertToPdf(string xlsxPath, PdfExportOptions? options = null) =>
        ConvertToPdf(xlsxPath, options, default);

    /// <summary>Converts an XLSX file to a PDF byte array, stopping when <paramref name="cancel"/> is cancelled.</summary>
    public static byte[] ConvertToPdf(string xlsxPath, PdfExportOptions? options, Cancel cancel)
    {
        using var stream = File.OpenRead(xlsxPath);
        return ConvertToPdf(stream, options, cancel);
    }

    /// <summary>Converts an XLSX stream to a PDF byte array.</summary>
    public static byte[] ConvertToPdf(Stream xlsxStream, PdfExportOptions? options = null) =>
        ConvertToPdf(xlsxStream, options, default);

    /// <summary>Converts an XLSX stream to a PDF byte array, stopping when <paramref name="cancel"/> is cancelled.</summary>
    public static byte[] ConvertToPdf(Stream xlsxStream, PdfExportOptions? options, Cancel cancel)
    {
        cancel.ThrowIfCancellationRequested();
        return PdfRenderer.Render(ExcelConverter.Parse(xlsxStream, options), options, cancel);
    }

    /// <summary>Converts an XLSX file to a PDF written to <paramref name="outputPdfPath"/>.</summary>
    public static void ConvertToPdf(string xlsxPath, string outputPdfPath, PdfExportOptions? options = null, Cancel cancel = default) =>
        File.WriteAllBytes(outputPdfPath, ConvertToPdf(xlsxPath, options, cancel));

    /// <summary>Converts an XLSX stream to a PDF written to <paramref name="output"/>.</summary>
    public static void ConvertToPdf(Stream xlsxStream, Stream output, PdfExportOptions? options = null, Cancel cancel = default)
    {
        var pdf = ConvertToPdf(xlsxStream, options, cancel);
        output.Write(pdf, 0, pdf.Length);
    }
}
