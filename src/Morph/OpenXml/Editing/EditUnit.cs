/// <summary>
/// A stretch of one paragraph, as <see cref="DocumentOutline"/> reads it for an editor. A
/// <see cref="EditUnitKind.Text"/> unit is <see cref="Text"/> at positions <see cref="Start"/> onwards of
/// the <c>w:r</c> with ordinal <see cref="Run"/>, in the coordinates of <see cref="SourceRuns"/>. Every
/// other kind is one thing that stays as it is while the text around it is edited; its
/// <see cref="Text"/> is what it shows, if it shows text.
/// </summary>
sealed record EditUnit(EditUnitKind Kind, int Run, int Start, string Text)
{
    /// <summary>The size of a drawing that sits in the line, in points; zero for one that floats.</summary>
    public double WidthPoints { get; init; }

    /// <inheritdoc cref="WidthPoints"/>
    public double HeightPoints { get; init; }

    public bool Editable => Kind == EditUnitKind.Text;
}
