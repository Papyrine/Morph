/// <summary>
/// Drop-shadow effect behind text (w14:shadow). Captures the parameters that
/// affect the visual offset and softness; <c>sx</c>/<c>sy</c>/<c>kx</c>/<c>ky</c>
/// scale-and-skew transforms from the OOXML are not modelled.
/// </summary>
sealed record TextShadow
{
    /// <summary>Shadow colour as 6-digit hex.</summary>
    public required string ColorHex { get; init; }

    /// <summary>Distance from text in points.</summary>
    public required double DistancePoints { get; init; }

    /// <summary>Direction in degrees (0 = right, 90 = down — Word's convention).</summary>
    public required double DirectionDegrees { get; init; }

    /// <summary>Blur radius in points (0 means crisp shadow).</summary>
    public required double BlurPoints { get; init; }

    /// <summary>Alpha 0–100 (100 = fully opaque).</summary>
    public required int AlphaPercent { get; init; }
}
