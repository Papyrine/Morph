namespace Morph.Web.Services;

/// <summary>The Office formats this app can read. Every one of them converts to every <see cref="OutputFormat"/>.</summary>
public enum InputFormat
{
    /// <summary>Word document (.docx).</summary>
    Docx,

    /// <summary>Excel workbook (.xlsx).</summary>
    Xlsx,

    /// <summary>PowerPoint presentation (.pptx).</summary>
    Pptx,
}
