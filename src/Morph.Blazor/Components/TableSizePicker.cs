using Microsoft.AspNetCore.Components.Rendering;

/// <summary>
/// Insert > Table's grid: a cell per size up to <see cref="Rows"/> by <see cref="Columns"/>, lit up to
/// the one under the pointer or the focus, and the size it would make named over it. A component of its
/// own so that the pointer crossing a cell redraws the grid and nothing else: in the editor, that was
/// the whole editor and its ribbon, eighty times over.
/// </summary>
sealed class TableSizePicker : ComponentBase
{
    (int Rows, int Columns) hover = (1, 1);
    bool moved;

    [Parameter, EditorRequired]
    public int Rows { get; set; }

    [Parameter, EditorRequired]
    public int Columns { get; set; }

    /// <summary>What a cell chosen makes: a table of its rows and columns.</summary>
    [Parameter]
    public EventCallback<(int Rows, int Columns)> OnChoose { get; set; }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "div");
        builder.AddAttribute(1, "class", "editor-menu-heading");
        builder.AddContent(2, $"{hover.Rows} × {hover.Columns} table");
        builder.CloseElement();

        builder.OpenElement(3, "div");
        builder.AddAttribute(4, "class", "editor-grid");
        builder.AddAttribute(5, "style", $"--columns: {Columns}");
        for (var row = 1; row <= Rows; row++)
        {
            for (var column = 1; column <= Columns; column++)
            {
                var size = (row, column);
                builder.OpenElement(6, "button");
                builder.SetKey(size);
                builder.AddAttribute(7, "type", "button");
                builder.AddAttribute(8, "class", "editor-grid-cell");
                builder.AddAttribute(9, "aria-label", $"{row} by {column}");
                if (row <= hover.Rows &&
                    column <= hover.Columns)
                {
                    builder.AddAttribute(10, "data-lit", "true");
                }

                builder.AddAttribute(11, "onmouseover", EventCallback.Factory.Create(this, () => Light(size)));
                builder.AddAttribute(12, "onfocus", EventCallback.Factory.Create(this, () => Light(size)));
                builder.AddEventPreventDefaultAttribute(13, "onmousedown", true);
                builder.AddAttribute(14, "onclick", EventCallback.Factory.Create(this, () => OnChoose.InvokeAsync(size)));
                builder.CloseElement();
            }
        }

        builder.CloseElement();
    }

    // The pointer or the focus coming to the cell already lit draws nothing; nor does the editor drawing
    // itself again, since the grid it holds never changes.
    void Light((int Rows, int Columns) size)
    {
        moved = hover != size;
        hover = size;
    }

    protected override bool ShouldRender()
    {
        var render = moved;
        moved = false;
        return render;
    }
}
