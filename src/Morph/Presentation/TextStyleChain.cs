using A = DocumentFormat.OpenXml.Drawing;

/// <summary>
/// The ordered list of DrawingML list-style sources a slide paragraph resolves its properties
/// through, most specific first:
/// <list type="number">
/// <item>the shape's own <c>a:lstStyle</c> on the slide</item>
/// <item>the matching layout placeholder's <c>a:lstStyle</c></item>
/// <item>the master placeholder's <c>a:lstStyle</c></item>
/// <item>the master's <c>p:txStyles</c> list for the placeholder's kind (title / body / other)</item>
/// <item>the presentation's <c>p:defaultTextStyle</c></item>
/// </list>
/// Each source holds up to nine <c>a:lvlNpPr</c> children; a paragraph at <c>a:pPr/@lvl</c> N reads
/// the N+1'th of each in turn and takes the first source that declares the property being resolved.
/// Sources are stored rather than flattened because resolution is per-property, not per-source: a
/// paragraph routinely takes its alignment from the layout and its font size from the master.
/// </summary>
sealed class TextStyleChain
{
    readonly IReadOnlyList<OpenXmlElement> sources;

    public TextStyleChain(IEnumerable<OpenXmlElement?> sources) =>
        this.sources = sources.Where(_ => _ != null).ToArray()!;

    /// <summary>The level properties each source declares for <paramref name="level"/>, in priority order.</summary>
    public IEnumerable<A.TextParagraphPropertiesType> LevelProperties(int level)
    {
        // The nine level elements are distinct generated types (a:lvl1pPr … a:lvl9pPr) sharing one
        // base, so match on the element name rather than assuming they are present or in order.
        var name = $"lvl{Math.Clamp(level, 0, 8) + 1}pPr";
        foreach (var source in sources)
        {
            var match = source.Elements<A.TextParagraphPropertiesType>()
                .FirstOrDefault(_ => _.LocalName == name);
            if (match != null)
            {
                yield return match;
            }
        }
    }

    /// <summary>
    /// Resolves a paragraph-level property: the first source at <paramref name="level"/> that
    /// declares it wins. <paramref name="pick"/> returns null when its source is silent.
    /// </summary>
    public T? Resolve<T>(int level, Func<A.TextParagraphPropertiesType, T?> pick)
        where T : class
    {
        foreach (var properties in LevelProperties(level))
        {
            if (pick(properties) is { } value)
            {
                return value;
            }
        }

        return null;
    }

    /// <summary>Value-typed counterpart to <c>Resolve</c>.</summary>
    public T? ResolveValue<T>(int level, Func<A.TextParagraphPropertiesType, T?> pick)
        where T : struct
    {
        foreach (var properties in LevelProperties(level))
        {
            if (pick(properties) is { } value)
            {
                return value;
            }
        }

        return null;
    }

    /// <summary>The inherited default run properties for <paramref name="level"/>, in priority order.</summary>
    public IEnumerable<A.DefaultRunProperties> DefaultRunProperties(int level)
    {
        foreach (var properties in LevelProperties(level))
        {
            if (properties.GetFirstChild<A.DefaultRunProperties>() is { } defaults)
            {
                yield return defaults;
            }
        }
    }
}
