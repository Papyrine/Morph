namespace Morph;

/// <summary>
/// The option editor for one <see cref="OutputFormat"/>. Named <c>…Panel</c> rather than
/// <c>ExportOptions</c> because Morph's own <see cref="ExportOptions"/> record shares this namespace.
/// </summary>
public partial class ExportOptionsPanel
{
    /// <summary>The format whose options to edit.</summary>
    [Parameter]
    public OutputFormat Target { get; set; }

    /// <summary>The image settings the PNG editor mutates in place.</summary>
    [Parameter]
    public ImageSettings Image { get; set; } = new();
}
