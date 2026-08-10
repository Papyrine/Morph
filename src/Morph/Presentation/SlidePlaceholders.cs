using A = DocumentFormat.OpenXml.Drawing;
using P = DocumentFormat.OpenXml.Presentation;

/// <summary>
/// Resolves a slide shape's inherited state up the slide → slideLayout → slideMaster chain.
///
/// Inheritance is not an optimisation to skip: measured across the 40-deck corpus, 2650 of 3092
/// shapes on slides are placeholders and <b>939 carry no <c>a:xfrm</c> at all</b>, so without this
/// walk nearly a third of every deck has no geometry.
///
/// PowerPoint matches a slide placeholder to its layout counterpart on the <c>idx</c> attribute when
/// both declare one, and on <c>type</c> otherwise. Two wrinkles the corpus exercises:
/// <list type="bullet">
/// <item>A missing <c>type</c> means <c>body</c> (ECMA-376 §19.3.1.36 default), which is why a bare
/// <c>&lt;p:ph idx="1"/&gt;</c> resolves at all.</item>
/// <item><c>title</c> and <c>ctrTitle</c> are the same slot: a layout may declare either and a slide
/// may reference the other.</item>
/// </list>
/// </summary>
sealed class SlidePlaceholders
{
    readonly Dictionary<(string Type, uint Index), P.Shape> byTypeAndIndex = [];
    readonly Dictionary<string, P.Shape> byType = [];
    readonly Dictionary<uint, P.Shape> byIndex = [];
    readonly SlidePlaceholders? parent;

    SlidePlaceholders(OpenXmlElement? shapeTree, SlidePlaceholders? parent)
    {
        this.parent = parent;
        if (shapeTree == null)
        {
            return;
        }

        foreach (var shape in shapeTree.Descendants<P.Shape>())
        {
            var placeholder = shape.NonVisualShapeProperties?
                .ApplicationNonVisualDrawingProperties?
                .GetFirstChild<P.PlaceholderShape>();
            if (placeholder == null)
            {
                continue;
            }

            var type = TypeOf(placeholder);
            var index = placeholder.Index?.Value;

            if (index is { } declaredIndex)
            {
                byTypeAndIndex.TryAdd((type, declaredIndex), shape);
                byIndex.TryAdd(declaredIndex, shape);
            }

            byType.TryAdd(type, shape);
        }
    }

    /// <summary>Builds the layout-then-master chain a slide inherits from.</summary>
    public static SlidePlaceholders For(SlidePart slidePart)
    {
        var layoutPart = slidePart.SlideLayoutPart;
        var masterTree = layoutPart?.SlideMasterPart?.SlideMaster?.CommonSlideData?.ShapeTree;
        var master = new SlidePlaceholders(masterTree, null);
        return new(layoutPart?.SlideLayout?.CommonSlideData?.ShapeTree, master);
    }

    /// <summary>
    /// The layout (or, failing that, master) shape a slide placeholder inherits from. Null when the
    /// shape is not a placeholder or nothing up the chain matches.
    /// </summary>
    /// <summary>
    /// Every shape a slide placeholder inherits from, nearest first: the layout's match, then the
    /// master's. Both matter — a layout routinely overrides geometry while leaving the master to
    /// supply text style — so the text cascade needs the whole chain, not just the nearest hit.
    /// </summary>
    public IEnumerable<P.Shape> Matches(P.PlaceholderShape? placeholder)
    {
        if (placeholder == null)
        {
            yield break;
        }

        if (MatchHere(placeholder) is { } here)
        {
            yield return here;
        }

        if (parent == null)
        {
            yield break;
        }

        foreach (var inherited in parent.Matches(placeholder))
        {
            yield return inherited;
        }
    }

    public P.Shape? Match(P.PlaceholderShape? placeholder) => Matches(placeholder).FirstOrDefault();

    P.Shape? MatchHere(P.PlaceholderShape? placeholder)
    {
        if (placeholder == null)
        {
            return null;
        }

        var type = TypeOf(placeholder);
        var index = placeholder.Index?.Value;

        if (index is { } declaredIndex &&
            byTypeAndIndex.TryGetValue((type, declaredIndex), out var exact))
        {
            return exact;
        }

        if (byType.TryGetValue(type, out var typeMatch))
        {
            return typeMatch;
        }

        // An idx-only reference against a layout slot that declared a different type — the corpus's
        // content placeholders (`<p:ph idx="1"/>` against `<p:ph type="body" idx="1"/>`) are already
        // covered by the type default, so this is the last resort before deferring to the master.
        if (index is { } fallbackIndex && byIndex.TryGetValue(fallbackIndex, out var indexMatch))
        {
            return indexMatch;
        }

        return parent?.Match(placeholder);
    }

    /// <summary>
    /// The shape's own transform, or the first one inherited from its placeholder chain. Null when
    /// neither the shape nor anything it inherits from declares geometry.
    /// </summary>
    public A.Transform2D? ResolveTransform(P.Shape shape) =>
        ResolveTransform(shape.ShapeProperties?.Transform2D, Placeholder(shape));

    /// <summary>
    /// A declared transform, or the first one up the placeholder chain. Pictures and graphic frames
    /// need this as much as shapes do: a picture placeholder routinely carries no <c>a:xfrm</c> of
    /// its own and takes its whole frame from the layout.
    /// </summary>
    public A.Transform2D? ResolveTransform(A.Transform2D? own, P.PlaceholderShape? placeholder)
    {
        if (own != null)
        {
            return own;
        }

        foreach (var inherited in Matches(placeholder))
        {
            if (inherited.ShapeProperties?.Transform2D is { } transform)
            {
                return transform;
            }
        }

        return null;
    }

    /// <summary>The placeholder declaration on a shape, or null when it is a free-standing shape.</summary>
    public static P.PlaceholderShape? Placeholder(P.Shape shape) =>
        shape.NonVisualShapeProperties?
            .ApplicationNonVisualDrawingProperties?
            .GetFirstChild<P.PlaceholderShape>();

    /// <summary>
    /// A placeholder's type as the XML spells it ("title", "ctrTitle", "sldNum"), normalised so an
    /// absent type reads as "body" (ECMA-376 §19.3.1.36) and ctrTitle collapses onto title, which is
    /// the same slot.
    ///
    /// The XML token has to be pulled out through <see cref="IEnumValue"/>: the generated enum's
    /// <c>ToString</c> gives the .NET member name ("Title", "CenteredTitle"), not the token, so
    /// comparing against it silently matches nothing. <c>ShapeParser.ExtractSolidFillColor</c> hit
    /// the same trap with scheme colours ("tx2" vs "Text2").
    /// </summary>
    public static string TypeOf(P.PlaceholderShape? placeholder)
    {
        var token = placeholder?.Type?.Value is { } value ? ((IEnumValue) value).Value : null;
        return token switch
        {
            null or "" => "body",
            "ctrTitle" => "title",
            _ => token
        };
    }
}
