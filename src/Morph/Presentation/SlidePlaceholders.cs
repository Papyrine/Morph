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

    const uint orphanIndex = uint.MaxValue;

    P.Shape? MatchHere(P.PlaceholderShape? placeholder)
    {
        if (placeholder == null)
        {
            return null;
        }

        var type = TypeOf(placeholder);
        var index = placeholder.Index?.Value;

        // idx 4294967295 is PowerPoint's orphan: a placeholder whose layout slot was deleted. It
        // matches nothing on the layout and takes only the master's slot of its type. gradient's
        // agenda body is one; matched by type to its layout's first body slot, a 14pt caption, its
        // topics came out half the 28pt the master's body gives them.
        if (index == orphanIndex &&
            parent != null)
        {
            return null;
        }

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
        if (index is { } fallbackIndex &&
            byIndex.TryGetValue(fallbackIndex, out var indexMatch))
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

    /// <summary>
    /// Where the shape's text stands up and down its box: its own <c>a:bodyPr/@anchor</c>, or the first
    /// one up the placeholder chain — a title takes the bottom or centre its layout or master gives it.
    /// </summary>
    public A.TextAnchoringTypeValues? ResolveTextAnchor(P.Shape shape)
    {
        if (shape.TextBody?.BodyProperties?.Anchor?.Value is { } own)
        {
            return own;
        }

        foreach (var inherited in Matches(Placeholder(shape)))
        {
            if (inherited.TextBody?.BodyProperties?.Anchor?.Value is { } anchor)
            {
                return anchor;
            }
        }

        return null;
    }

    /// <summary>
    /// A value of the shape's <c>a:bodyPr</c>, or of the first one up the placeholder chain that
    /// declares it.
    /// </summary>
    public T? ResolveBody<T>(P.Shape shape, Func<A.BodyProperties, T?> read)
        where T : struct
    {
        if (shape.TextBody?.BodyProperties is { } own &&
            read(own) is { } value)
        {
            return value;
        }

        foreach (var inherited in Matches(Placeholder(shape)))
        {
            if (inherited.TextBody?.BodyProperties is { } properties &&
                read(properties) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    /// <summary>
    /// The <c>p:spPr</c> that declares the shape's fill: its own when it names one, else the first
    /// up the placeholder chain that does. fashion-infographics-poster's "$2Bn" label is a body
    /// placeholder whose slide copy declares only where it stands; its accent fill is the layout's.
    /// </summary>
    public OpenXmlElement? ResolveFillSource(P.Shape shape) =>
        ResolveShapeProperties(shape, HasFill);

    /// <summary>The <c>p:spPr</c> that declares the shape's outline (<c>a:ln</c>), as for the fill.</summary>
    public OpenXmlElement? ResolveOutlineSource(P.Shape shape) =>
        ResolveShapeProperties(shape, _ => _.GetFirstChild<A.Outline>() != null);

    /// <summary>
    /// The <c>p:spPr</c> that declares the shape's geometry (<c>a:prstGeom</c> or <c>a:custGeom</c>),
    /// as for the fill. sketchlines' title is a blob its layout draws; the slide's copy declares none.
    /// </summary>
    public OpenXmlElement? ResolveGeometrySource(P.Shape shape) =>
        ResolveShapeProperties(shape, _ => _.GetFirstChild<A.PresetGeometry>() != null || _.GetFirstChild<A.CustomGeometry>() != null);

    /// <summary>The <c>p:spPr</c> that declares the shape's effects (<c>a:effectLst</c>), as for the fill.</summary>
    public OpenXmlElement? ResolveEffectSource(P.Shape shape) =>
        ResolveShapeProperties(shape, _ => _.GetFirstChild<A.EffectList>() != null);

    OpenXmlElement? ResolveShapeProperties(P.Shape shape, Func<OpenXmlElement, bool> declares)
    {
        if (shape.ShapeProperties is { } own &&
            declares(own))
        {
            return own;
        }

        if (Placeholder(shape) is { } placeholder)
        {
            foreach (var inherited in Matches(placeholder))
            {
                if (inherited.ShapeProperties is { } properties &&
                    declares(properties))
                {
                    return properties;
                }
            }
        }

        return shape.ShapeProperties;
    }

    static bool HasFill(OpenXmlElement properties) =>
        properties.GetFirstChild<A.NoFill>() != null ||
        properties.GetFirstChild<A.SolidFill>() != null ||
        properties.GetFirstChild<A.GradientFill>() != null ||
        properties.GetFirstChild<A.BlipFill>() != null ||
        properties.GetFirstChild<A.PatternFill>() != null ||
        properties.GetFirstChild<A.GroupFill>() != null;

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
