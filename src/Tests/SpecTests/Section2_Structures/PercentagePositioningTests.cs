using DocumentFormat.OpenXml.Drawing.Wordprocessing;

/// <summary>
/// Covers <c>wp14:pctPosHOffset</c> / <c>wp14:pctPosVOffset</c> percentage *positioning* on
/// anchored drawings. Values are stored ×1000 of the percentage (50000 = 50% = 0.5).
///
/// The percentage replaces the EMU-based <c>wp:posOffset</c> when present and is resolved at
/// render time against the page (when <c>relativeFrom="page"</c>) or the content area
/// (everything else, since Morph collapses leftMargin/rightMargin/insideMargin/outsideMargin
/// down to the content rectangle).
/// </summary>
public class PercentagePositioningTests
{
    const string wp = "http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing";
    const string wp14 = "http://schemas.microsoft.com/office/word/2010/wordprocessingDrawing";
    const string mc = "http://schemas.openxmlformats.org/markup-compatibility/2006";

    [Test]
    public async Task ParsePositioning_ReadsPctPosHOffset_AsFraction()
    {
        var anchor = LoadAnchor(
            $"""
            <wp:anchor xmlns:wp="{wp}" xmlns:wp14="{wp14}" behindDoc="0" simplePos="0" relativeHeight="0" allowOverlap="1" layoutInCell="1" distT="0" distB="0" distL="0" distR="0" wp14:editId="0" wp14:anchorId="0">
              <wp:positionH relativeFrom="page">
                <wp14:pctPosHOffset>50000</wp14:pctPosHOffset>
              </wp:positionH>
              <wp:positionV relativeFrom="margin">
                <wp14:pctPosVOffset>25000</wp14:pctPosVOffset>
              </wp:positionV>
            </wp:anchor>
            """);

        var positioning = anchor.ParsePositioning();

        await Assert.That(positioning.HorizontalPositionPercent).IsEqualTo(0.5);
        await Assert.That(positioning.HorizontalAnchor).IsEqualTo(HorizontalAnchor.Page);
        await Assert.That(positioning.VerticalPositionPercent).IsEqualTo(0.25);
        await Assert.That(positioning.VerticalAnchor).IsEqualTo(VerticalAnchor.Margin);
    }

    [Test]
    public async Task ParsePositioning_ZeroPctOffset_BecomesNull()
    {
        // A zero <pctPosHOffset> coexisting with an explicit <posOffset> means "this offset is
        // EMU-based, the percent placeholder is just zeroed out". Morph treats zero as null so
        // the EMU value wins.
        var anchor = LoadAnchor(
            $"""
            <wp:anchor xmlns:wp="{wp}" xmlns:wp14="{wp14}" behindDoc="0" simplePos="0" relativeHeight="0" allowOverlap="1" layoutInCell="1" distT="0" distB="0" distL="0" distR="0" wp14:editId="0" wp14:anchorId="0">
              <wp:positionH relativeFrom="page">
                <wp14:pctPosHOffset>0</wp14:pctPosHOffset>
                <wp:posOffset>914400</wp:posOffset>
              </wp:positionH>
            </wp:anchor>
            """);

        var positioning = anchor.ParsePositioning();

        await Assert.That(positioning.HorizontalPositionPercent).IsNull();
        // 914400 EMU = 72 pt = 1 inch.
        await Assert.That(positioning.HorizontalPositionPoints).IsEqualTo(72);
    }

    [Test]
    public async Task ParsePositioning_AlternateContent_PrefersWp14Choice()
    {
        // Real-world layout: positionH wrapped in mc:AlternateContent with a wp14 Choice
        // (percent) and an EMU Fallback. Morph understands wp14, so it must pick the Choice.
        var anchor = LoadAnchor(
            $"""
            <wp:anchor xmlns:wp="{wp}" xmlns:wp14="{wp14}" xmlns:mc="{mc}" behindDoc="0" simplePos="0" relativeHeight="0" allowOverlap="1" layoutInCell="1" distT="0" distB="0" distL="0" distR="0" wp14:editId="0" wp14:anchorId="0">
              <mc:AlternateContent>
                <mc:Choice Requires="wp14">
                  <wp:positionH relativeFrom="page">
                    <wp14:pctPosHOffset>50000</wp14:pctPosHOffset>
                  </wp:positionH>
                </mc:Choice>
                <mc:Fallback>
                  <wp:positionH relativeFrom="page">
                    <wp:posOffset>3886200</wp:posOffset>
                  </wp:positionH>
                </mc:Fallback>
              </mc:AlternateContent>
            </wp:anchor>
            """);

        var positioning = anchor.ParsePositioning();

        await Assert.That(positioning.HorizontalPositionPercent).IsEqualTo(0.5);
        // No EMU offset surfaces — the Choice branch carries only the percent.
        await Assert.That(positioning.HorizontalPositionPoints).IsEqualTo(0);
    }

    [Test]
    public async Task ParsePositioning_AlternateContent_FallsBackWhenChoiceUnknown()
    {
        // If Choice requires an unknown namespace, Morph must fall back to mc:Fallback so it
        // still gets an offset (the EMU one) rather than dropping the position entirely.
        var anchor = LoadAnchor(
            $"""
            <wp:anchor xmlns:wp="{wp}" xmlns:wp14="{wp14}" xmlns:mc="{mc}" behindDoc="0" simplePos="0" relativeHeight="0" allowOverlap="1" layoutInCell="1" distT="0" distB="0" distL="0" distR="0" wp14:editId="0" wp14:anchorId="0">
              <mc:AlternateContent>
                <mc:Choice Requires="someUnknownExt">
                  <wp:positionH relativeFrom="page">
                    <wp:posOffset>0</wp:posOffset>
                  </wp:positionH>
                </mc:Choice>
                <mc:Fallback>
                  <wp:positionH relativeFrom="page">
                    <wp:posOffset>914400</wp:posOffset>
                  </wp:positionH>
                </mc:Fallback>
              </mc:AlternateContent>
            </wp:anchor>
            """);

        var positioning = anchor.ParsePositioning();

        await Assert.That(positioning.HorizontalPositionPoints).IsEqualTo(72);
        await Assert.That(positioning.HorizontalPositionPercent).IsNull();
    }

    [Test]
    public async Task FloatingImage_DefaultsToNullPercent()
    {
        var image = new FloatingImageElement
        {
            ImageData = [],
            WidthPoints = 100,
            HeightPoints = 50
        };

        await Assert.That(image.HorizontalPositionPercent).IsNull();
        await Assert.That(image.VerticalPositionPercent).IsNull();
    }

    static Anchor LoadAnchor(string xml) => new(xml);
}
