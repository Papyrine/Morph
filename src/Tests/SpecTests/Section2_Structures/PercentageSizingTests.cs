using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Drawing.Wordprocessing;
using DocumentFormat.OpenXml.Office2010.Word.Drawing;

/// <summary>
/// Covers <c>wp14:pctWidth</c> / <c>wp14:pctHeight</c> + <c>wp14:sizeRelH</c> /
/// <c>wp14:sizeRelV</c> percentage sizing on anchored drawings.
///
/// Values are stored ×1000 of the percentage (50000 = 50% = 0.5). A literal zero in
/// the doc means "no percentage sizing in effect" — Word emits a placeholder even when
/// the explicit <c>wp:extent</c> is the source of truth — and Morph treats that as null
/// so the renderer keeps the EMU extent.
/// </summary>
public class PercentageSizingTests
{
    [Test]
    public async Task ParsePositioning_ReadsHalfPagePercentSizing()
    {
        // Hand-built anchor: 50% of page width, 25% of margin height.
        var anchor = BuildAnchor(
            sizeRelH: BuildSizeRelH("page", widthThousandths: 50_000),
            sizeRelV: BuildSizeRelV("margin", heightThousandths: 25_000));

        var positioning = anchor.ParsePositioning();

        await Assert.That(positioning.WidthPercent).IsEqualTo(0.5);
        await Assert.That(positioning.WidthRelativeFrom).IsEqualTo(SizeRelativeFrom.Page);
        await Assert.That(positioning.HeightPercent).IsEqualTo(0.25);
        await Assert.That(positioning.HeightRelativeFrom).IsEqualTo(SizeRelativeFrom.Margin);
    }

    [Test]
    public async Task ParsePositioning_ZeroPlaceholder_BecomesNull()
    {
        // Word emits <pctWidth>0</pctWidth> as a placeholder even when the explicit
        // wp:extent is authoritative. Morph collapses zero to null so the renderer
        // doesn't multiply the page by 0.
        var anchor = BuildAnchor(
            sizeRelH: BuildSizeRelH("page", widthThousandths: 0),
            sizeRelV: BuildSizeRelV("page", heightThousandths: 0));

        var positioning = anchor.ParsePositioning();

        await Assert.That(positioning.WidthPercent).IsNull();
        await Assert.That(positioning.HeightPercent).IsNull();
    }

    [Test]
    public async Task ParsePositioning_NoSizeRel_LeavesPercentNull()
    {
        var anchor = BuildAnchor();

        var positioning = anchor.ParsePositioning();

        await Assert.That(positioning.WidthPercent).IsNull();
        await Assert.That(positioning.HeightPercent).IsNull();
        // Defaults to Margin so the renderer has a sensible reference if a percent is
        // supplied later via a property setter.
        await Assert.That(positioning.WidthRelativeFrom).IsEqualTo(SizeRelativeFrom.Margin);
    }

    [Test]
    public async Task ParsePositioning_RelativeFromMargin_VariantsCollapse()
    {
        // leftMargin / rightMargin / insideMargin / outsideMargin all collapse to Margin
        // because Morph doesn't model mirror-margin layouts at the renderer level yet.
        foreach (var variant in new[] { "leftMargin", "rightMargin", "insideMargin", "outsideMargin" })
        {
            var anchor = BuildAnchor(
                sizeRelH: BuildSizeRelH(variant, widthThousandths: 50_000));

            var positioning = anchor.ParsePositioning();

            await Assert.That(positioning.WidthRelativeFrom).IsEqualTo(SizeRelativeFrom.Margin);
        }
    }

    [Test]
    public async Task FloatingImage_DefaultsToNullPercent_AndMarginRelativeFrom()
    {
        var image = new FloatingImageElement
        {
            ImageData = [],
            WidthPoints = 100,
            HeightPoints = 50
        };

        await Assert.That(image.WidthPercent).IsNull();
        await Assert.That(image.HeightPercent).IsNull();
        await Assert.That(image.WidthRelativeFrom).IsEqualTo(SizeRelativeFrom.Margin);
        await Assert.That(image.HeightRelativeFrom).IsEqualTo(SizeRelativeFrom.Margin);
    }

    static Anchor BuildAnchor(OpenXmlElement? sizeRelH = null, OpenXmlElement? sizeRelV = null)
    {
        var anchor = new Anchor
        {
            BehindDoc = false,
            DistanceFromTop = 0u,
            DistanceFromBottom = 0u,
            DistanceFromLeft = 0u,
            DistanceFromRight = 0u,
            SimplePos = false,
            LayoutInCell = true,
            AllowOverlap = true,
            RelativeHeight = 0u,
            EditId = "00000000",
            AnchorId = "00000000"
        };

        if (sizeRelH != null) anchor.AppendChild(sizeRelH);
        if (sizeRelV != null) anchor.AppendChild(sizeRelV);
        return anchor;
    }

    static RelativeWidth BuildSizeRelH(string relativeFrom, long widthThousandths)
    {
        var sizeRelH = new RelativeWidth
        {
            ObjectId = relativeFrom switch
            {
                "page" => SizeRelativeHorizontallyValues.Page,
                "leftMargin" => SizeRelativeHorizontallyValues.LeftMargin,
                "rightMargin" => SizeRelativeHorizontallyValues.RightMargin,
                "insideMargin" => SizeRelativeHorizontallyValues.InsideMargin,
                "outsideMargin" => SizeRelativeHorizontallyValues.OutsideMargin,
                _ => SizeRelativeHorizontallyValues.Margin
            }
        };
        sizeRelH.AppendChild(new PercentageWidth { Text = widthThousandths.ToString() });
        return sizeRelH;
    }

    static RelativeHeight BuildSizeRelV(string relativeFrom, long heightThousandths)
    {
        var sizeRelV = new RelativeHeight
        {
            RelativeFrom = relativeFrom switch
            {
                "page" => SizeRelativeVerticallyValues.Page,
                "topMargin" => SizeRelativeVerticallyValues.TopMargin,
                "bottomMargin" => SizeRelativeVerticallyValues.BottomMargin,
                "insideMargin" => SizeRelativeVerticallyValues.InsideMargin,
                "outsideMargin" => SizeRelativeVerticallyValues.OutsideMargin,
                _ => SizeRelativeVerticallyValues.Margin
            }
        };
        sizeRelV.AppendChild(new PercentageHeight { Text = heightThousandths.ToString() });
        return sizeRelV;
    }
}
