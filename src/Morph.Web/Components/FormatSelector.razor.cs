namespace Morph.Web.Components;

public partial class FormatSelector
{
    [Parameter]
    public string Label { get; set; } = "Format";

    [Parameter]
    public IReadOnlyList<FormatInfo> Formats { get; set; } = [];

    [Parameter]
    public OutputFormat Selected { get; set; }

    [Parameter]
    public EventCallback<OutputFormat> SelectedChanged { get; set; }

    Task OnChanged(ChangeEventArgs args)
    {
        var format = Enum.Parse<OutputFormat>((string) args.Value!);
        return SelectedChanged.InvokeAsync(format);
    }
}
