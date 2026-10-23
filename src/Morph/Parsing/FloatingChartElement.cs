/// <summary>
/// A chart placed over the page (an Excel drawing's <c>xdr:graphicFrame</c>, a slide's
/// <c>p:graphicFrame</c>). Its box is fixed at parse; what is drawn in it is composed at layout
/// (<see cref="ChartComposer"/>), where its labels can be measured.
/// </summary>
sealed class FloatingChartElement : DocumentElement
{
    public required ChartModel Chart { get; init; }

    public required double WidthPoints { get; init; }

    public required double HeightPoints { get; init; }

    public double HorizontalPositionPoints { get; init; }

    public double VerticalPositionPoints { get; init; }

    public HorizontalAnchor HorizontalAnchor { get; init; } = HorizontalAnchor.Margin;

    public VerticalAnchor VerticalAnchor { get; init; } = VerticalAnchor.Paragraph;

    /// <summary>As <see cref="FloatingShapeElement.AnchorParagraph"/>.</summary>
    public ParagraphElement? AnchorParagraph { get; set; }

    public uint RelativeHeight { get; init; }

    /// <summary>
    /// How much the chart's text, rules and spacing shrink with the sheet it is printed on (a sheet's
    /// print scale or fit to page); 1 elsewhere. The box is already in printed points.
    /// </summary>
    public double Scale { get; init; } = 1;
}
