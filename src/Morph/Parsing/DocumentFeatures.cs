/// <summary>
/// Presence flags for advanced OOXML features that the renderer doesn't yet draw but that
/// consumers may want to detect (e.g. to fall back to Word for documents that depend on them).
/// </summary>
sealed record DocumentFeatures
{
    /// <summary>The body contains at least one chart (<c>c:chartSpace</c> reference).</summary>
    public bool HasCharts { get; init; }

    /// <summary>The body contains at least one SmartArt diagram (<c>a:graphicData</c> with the SmartArt URI).</summary>
    public bool HasSmartArt { get; init; }

    /// <summary>The body or headers contain Office Math (<c>m:oMath</c> / <c>m:oMathPara</c>).</summary>
    public bool HasMath { get; init; }

    /// <summary>The headers contain a likely watermark shape (text effect with rotated/semi-transparent text).</summary>
    public bool HasWatermarks { get; init; }

    /// <summary>At least one shape uses gradient fill (<c>a:gradFill</c>).</summary>
    public bool HasGradientFills { get; init; }

    /// <summary>At least one shape uses custom Bezier path geometry (<c>a:custGeom</c>).</summary>
    public bool HasBezierShapes { get; init; }

    /// <summary>At least one shape uses 3D effects (<c>a:sp3d</c> or <c>a:scene3d</c>).</summary>
    public bool Has3dEffects { get; init; }

    /// <summary>At least one connector shape (<c>wps:wsp</c> with connection geometry, or <c>a:cxnSp</c>).</summary>
    public bool HasConnectors { get; init; }

    /// <summary>At least one image uses a duotone or other recolor effect (<c>a:duotone</c>, <c>a:lum</c>, <c>a:clrChange</c>).</summary>
    public bool HasDuotoneEffects { get; init; }
}
