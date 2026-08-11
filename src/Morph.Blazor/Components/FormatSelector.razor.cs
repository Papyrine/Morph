namespace Morph;

/// <summary>A labelled dropdown of output formats, bindable through <c>@bind-Selected</c>.</summary>
public partial class FormatSelector
{
    /// <summary>Text of the <c>&lt;label&gt;</c>; also seeds the control's id.</summary>
    [Parameter]
    public string Label { get; set; } = "Format";

    /// <summary>The formats to offer, in display order.</summary>
    [Parameter]
    public IReadOnlyList<FormatInfo> Formats { get; set; } = [];

    /// <summary>The currently selected format.</summary>
    [Parameter]
    public OutputFormat Selected { get; set; }

    /// <summary>Raised when the user picks a different format.</summary>
    [Parameter]
    public EventCallback<OutputFormat> SelectedChanged { get; set; }

    Task OnChanged(ChangeEventArgs args)
    {
        var format = Enum.Parse<OutputFormat>((string) args.Value!);
        return SelectedChanged.InvokeAsync(format);
    }
}
