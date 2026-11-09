using A = DocumentFormat.OpenXml.Drawing;

/// <summary>
/// Accumulated transform from nested groups: an affine map from a child's local EMU
/// coordinates into the anchor's coordinate space. The linear part carries the composed
/// scales AND nested-group rotations (each applied about its own group's centre, per
/// DrawingML); <see cref="MapRectangle"/> folds a child's axis-aligned box through it.
/// </summary>
struct AccumulatedTransform
{
    public double M11;
    public double M12;
    public double M21;

    public double M22;

    // Accumulated translation in EMUs
    public double OffsetX;
    public double OffsetY;

    public static AccumulatedTransform Identity =>
        new()
        {
            M11 = 1,
            M22 = 1
        };

    /// <summary>
    /// Maps a child-space axis-aligned rectangle through the accumulated transform. The centre
    /// maps exactly; the size scales by the basis-vector lengths; the returned rotation
    /// (clockwise degrees, the model's convention) is the angle the composed nested-group
    /// rotations turn the child by — renderers re-apply it about the returned box's centre,
    /// which reproduces DrawingML's rotate-about-group-centre placement. An anisotropic outer
    /// scale over a rotated inner group would be a skew; that residual is dropped.
    /// A negative determinant (an odd number of ancestor flips, composed when the transform
    /// was built with flips) decomposes canonically as rotation + vertical flip: any mirrored
    /// 2×2 similarity is R(θ)·diag(s, −s), with θ read off the first basis column as usual.
    /// </summary>
    public (double X, double Y, double Width, double Height, double RotationDegrees, bool FlipVertical) MapRectangle(double x, double y, double width, double height)
    {
        var centerX = x + width / 2;
        var centerY = y + height / 2;
        var mappedCenterX = OffsetX + M11 * centerX + M12 * centerY;
        var mappedCenterY = OffsetY + M21 * centerX + M22 * centerY;
        var scaleX = Math.Sqrt(M11 * M11 + M21 * M21);
        var scaleY = Math.Sqrt(M12 * M12 + M22 * M22);
        var mappedWidth = width * scaleX;
        var mappedHeight = height * scaleY;
        var rotationDegrees = Math.Atan2(M21, M11) * (180 / Math.PI);
        var flipVertical = M11 * M22 - M12 * M21 < 0;
        return (mappedCenterX - mappedWidth / 2, mappedCenterY - mappedHeight / 2, mappedWidth, mappedHeight, rotationDegrees, flipVertical);
    }

    /// <summary>
    /// Calculates the accumulated transform for an element by walking up through ancestor grpSp
    /// groups. Each group maps a child point p to c + R·(off + (p − chOff)·scale − c): the child
    /// space is scaled into the group's box, then the box rotates about its own centre c by the
    /// group's <c>a:xfrm/@rot</c>. Groups compose innermost-out; the root wgp's child space is
    /// applied last via the rootChOff/rootScale parameters.
    /// </summary>
    public static AccumulatedTransform Of(OpenXmlElement element, long rootChOffX, long rootChOffY, double rootScaleX, double rootScaleY, bool composeFlips = false)
    {
        var transform = AccumulatedTransform.Identity;

        var current = element.Parent;
        while (current != null)
        {
            // Check if this is a grpSp element (DrawingML group, not wgp which is already handled)
            if (current.LocalName == "grpSp")
            {
                var grpSpPr = current.Elements().FirstOrDefault(_ => _.LocalName == "grpSpPr");
                var xfrm = grpSpPr?.Elements().FirstOrDefault(_ => _.LocalName == "xfrm");

                if (xfrm != null)
                {
                    long offX = 0, offY = 0, extCx = 1, extCy = 1, chOffX = 0, chOffY = 0, chExtCx = 1, chExtCy = 1;

                    var off = xfrm.Elements().FirstOrDefault(_ => _.LocalName == "off");
                    var ext = xfrm.Elements().FirstOrDefault(_ => _.LocalName == "ext");
                    var chOff = xfrm.Elements().FirstOrDefault(_ => _.LocalName == "chOff");
                    var chExt = xfrm.Elements().FirstOrDefault(_ => _.LocalName == "chExt");

                    if (off != null)
                    {
                        var attributes = off.GetAttributes();
                        long.TryParse(attributes.AttributeValue("x"), out offX);
                        long.TryParse(attributes.AttributeValue("y"), out offY);
                    }

                    if (ext != null)
                    {
                        var attributes = ext.GetAttributes();
                        long.TryParse(attributes.AttributeValue("cx"), out extCx);
                        long.TryParse(attributes.AttributeValue("cy"), out extCy);
                    }

                    if (chOff != null)
                    {
                        var attributes = chOff.GetAttributes();
                        long.TryParse(attributes.AttributeValue("x"), out chOffX);
                        long.TryParse(attributes.AttributeValue("y"), out chOffY);
                    }

                    if (chExt != null)
                    {
                        var attributes = chExt.GetAttributes();
                        long.TryParse(attributes.AttributeValue("cx"), out chExtCx);
                        long.TryParse(attributes.AttributeValue("cy"), out chExtCy);
                    }

                    if (extCx <= 0)
                    {
                        extCx = 1;
                    }

                    if (extCy <= 0)
                    {
                        extCy = 1;
                    }

                    if (chExtCx <= 0)
                    {
                        chExtCx = 1;
                    }

                    if (chExtCy <= 0)
                    {
                        chExtCy = 1;
                    }

                    var scaleX = (double) extCx / chExtCx;
                    var scaleY = (double) extCy / chExtCy;

                    // a:xfrm/@rot is clockwise 60,000ths of a degree; in Y-down page coordinates
                    // the clockwise rotation matrix is [[cos, −sin], [sin, cos]].
                    double rotation = 0;
                    if (xfrm.GetAttributes().AttributeValue("rot") is { } rotValue &&
                        long.TryParse(rotValue, out var rot60000ths))
                    {
                        rotation = rot60000ths / 60000.0 * Math.PI / 180;
                    }

                    // a:xfrm/@flipH/@flipV mirror the group's content about its own centre
                    // BEFORE the group's rotation turns it: local = R · F · diag(scale).
                    // Composed only when the caller consumes flips (shape/picture geometry);
                    // text boxes keep the flip-free transform — Word mirrors box geometry,
                    // never the text itself.
                    var xfrmAttributes = xfrm.GetAttributes();
                    var mirrorX = composeFlips &&
                                  xfrmAttributes.AttributeValue("flipH") is "1" or "true" ? -1d : 1d;
                    var mirrorY = composeFlips &&
                                  xfrmAttributes.AttributeValue("flipV") is "1" or "true" ? -1d : 1d;

                    var cos = Math.Cos(rotation);
                    var sin = Math.Sin(rotation);
                    var rf11 = cos * mirrorX;
                    var rf12 = -sin * mirrorY;
                    var rf21 = sin * mirrorX;
                    var rf22 = cos * mirrorY;
                    var localM11 = rf11 * scaleX;
                    var localM12 = rf12 * scaleY;
                    var localM21 = rf21 * scaleX;
                    var localM22 = rf22 * scaleY;
                    var centerX = offX + extCx / 2.0;
                    var centerY = offY + extCy / 2.0;
                    var baseX = offX - chOffX * scaleX - centerX;
                    var baseY = offY - chOffY * scaleY - centerY;
                    var localOffsetX = centerX + rf11 * baseX + rf12 * baseY;
                    var localOffsetY = centerY + rf21 * baseX + rf22 * baseY;

                    // This ancestor wraps everything accumulated so far: new = local ∘ accumulated.
                    transform = new()
                    {
                        M11 = localM11 * transform.M11 + localM12 * transform.M21,
                        M12 = localM11 * transform.M12 + localM12 * transform.M22,
                        M21 = localM21 * transform.M11 + localM22 * transform.M21,
                        M22 = localM21 * transform.M12 + localM22 * transform.M22,
                        OffsetX = localM11 * transform.OffsetX + localM12 * transform.OffsetY + localOffsetX,
                        OffsetY = localM21 * transform.OffsetX + localM22 * transform.OffsetY + localOffsetY
                    };
                }
            }
            else if (current.LocalName == "wgp")
            {
                // Stop at the root WordprocessingGroup - its transform is applied separately via rootScaleX/Y
                break;
            }

            current = current.Parent;
        }

        // Apply root wgp transform
        return new()
        {
            M11 = rootScaleX * transform.M11,
            M12 = rootScaleX * transform.M12,
            M21 = rootScaleY * transform.M21,
            M22 = rootScaleY * transform.M22,
            OffsetX = (transform.OffsetX - rootChOffX) * rootScaleX,
            OffsetY = (transform.OffsetY - rootChOffY) * rootScaleY
        };
    }
}
