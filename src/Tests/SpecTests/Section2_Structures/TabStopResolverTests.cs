public class TabStopResolverTests
{
    // === Left-aligned explicit stops ===

    [Test]
    public async Task Left_SnapsToFirstStopBeyondCursor()
    {
        var stops = new List<TabStop>
        {
            new() { PositionPoints = 72, Alignment = TabAlignment.Left },
            new() { PositionPoints = 216, Alignment = TabAlignment.Left }
        };

        var (dest, stop) = TabStopResolver.Resolve(cursorX: 30, followingWidth: 40, stops, defaultTabStopPoints: 36, leftIndentPoints: 0);

        await Assert.That(dest).IsEqualTo(72.0);
        await Assert.That(stop).IsNotNull();
        await Assert.That(stop!.PositionPoints).IsEqualTo(72.0);
    }

    [Test]
    public async Task Left_SkipsStopsAtOrBehindCursor()
    {
        var stops = new List<TabStop>
        {
            new() { PositionPoints = 72, Alignment = TabAlignment.Left },
            new() { PositionPoints = 216, Alignment = TabAlignment.Left }
        };

        var (dest, _) = TabStopResolver.Resolve(cursorX: 100, followingWidth: 0, stops, defaultTabStopPoints: 36, leftIndentPoints: 0);

        await Assert.That(dest).IsEqualTo(216.0);
    }

    // === Right-aligned explicit stops ===

    [Test]
    public async Task Right_DestinationSubtractsFollowingWidth()
    {
        var stops = new List<TabStop>
        {
            new() { PositionPoints = 432, Alignment = TabAlignment.Right, Leader = TabLeader.Dot }
        };

        var (dest, stop) = TabStopResolver.Resolve(cursorX: 100, followingWidth: 20, stops, defaultTabStopPoints: 36, leftIndentPoints: 0);

        await Assert.That(dest).IsEqualTo(412.0);
        await Assert.That(stop!.Leader).IsEqualTo(TabLeader.Dot);
    }

    [Test]
    public async Task Right_SkipsToNextStopIfDestinationBehindCursor()
    {
        var stops = new List<TabStop>
        {
            new() { PositionPoints = 100, Alignment = TabAlignment.Right },
            new() { PositionPoints = 400, Alignment = TabAlignment.Right }
        };

        // followingWidth=60 at stop 100 → dest 40 (behind cursor 50) → try next stop.
        var (dest, stop) = TabStopResolver.Resolve(cursorX: 50, followingWidth: 60, stops, defaultTabStopPoints: 36, leftIndentPoints: 0);

        await Assert.That(dest).IsEqualTo(340.0);
        await Assert.That(stop!.PositionPoints).IsEqualTo(400.0);
    }

    // === Center-aligned explicit stops ===

    [Test]
    public async Task Center_DestinationSubtractsHalfFollowingWidth()
    {
        var stops = new List<TabStop>
        {
            new() { PositionPoints = 234, Alignment = TabAlignment.Center }
        };

        var (dest, _) = TabStopResolver.Resolve(cursorX: 50, followingWidth: 60, stops, defaultTabStopPoints: 36, leftIndentPoints: 0);

        await Assert.That(dest).IsEqualTo(204.0);
    }

    // === Default tab stops ===

    [Test]
    public async Task Default_SnapsToNextMultiplePastCursor_WithNoExplicitStops()
    {
        var (dest, stop) = TabStopResolver.Resolve(cursorX: 10, followingWidth: 0, [], defaultTabStopPoints: 36, leftIndentPoints: 0);

        await Assert.That(dest).IsEqualTo(36.0);
        await Assert.That(stop).IsNull();
    }

    [Test]
    public async Task Default_SnapsRelativeToLeftIndent()
    {
        // leftIndent=50, defaultTab=36 → stops at 86, 122, 158, ...
        var (dest, _) = TabStopResolver.Resolve(cursorX: 60, followingWidth: 0, [], defaultTabStopPoints: 36, leftIndentPoints: 50);

        await Assert.That(dest).IsEqualTo(86.0);
    }

    [Test]
    public async Task Default_KicksInPastLastExplicitStop()
    {
        var stops = new List<TabStop>
        {
            new() { PositionPoints = 144, Alignment = TabAlignment.Left }
        };

        // Cursor past last explicit stop at 150 → next default multiple past 144 is 180, 216, ...
        var (dest, stop) = TabStopResolver.Resolve(cursorX: 150, followingWidth: 0, stops, defaultTabStopPoints: 36, leftIndentPoints: 0);

        await Assert.That(dest).IsEqualTo(180.0);
        await Assert.That(stop).IsNull();
    }

    [Test]
    public async Task Default_DoesNotKickInBeforeLastExplicitStop()
    {
        var stops = new List<TabStop>
        {
            new() { PositionPoints = 200, Alignment = TabAlignment.Left }
        };

        // Cursor at 50: even though a default-tab multiple (72) is closer, the explicit stop wins.
        var (dest, stop) = TabStopResolver.Resolve(cursorX: 50, followingWidth: 0, stops, defaultTabStopPoints: 36, leftIndentPoints: 0);

        await Assert.That(dest).IsEqualTo(200.0);
        await Assert.That(stop).IsNotNull();
    }

    // === Edge cases ===

    [Test]
    public async Task Collapses_WhenDefaultTabStopIsZero()
    {
        var (dest, stop) = TabStopResolver.Resolve(cursorX: 50, followingWidth: 0, [], defaultTabStopPoints: 0, leftIndentPoints: 0);

        await Assert.That(dest).IsEqualTo(50.0);
        await Assert.That(stop).IsNull();
    }

    [Test]
    public async Task DecimalFallsBackToRightWhenNoPrefix()
    {
        // No decimalPrefixWidth supplied → behaves like Right (matches Word's fallback when
        // the following text has no decimal point).
        var stops = new List<TabStop>
        {
            new() { PositionPoints = 144, Alignment = TabAlignment.Decimal }
        };

        var (dest, _) = TabStopResolver.Resolve(cursorX: 20, followingWidth: 50, stops, defaultTabStopPoints: 36, leftIndentPoints: 0);

        await Assert.That(dest).IsEqualTo(94.0);
    }

    // === Clamping past availableEndX ===

    [Test]
    public async Task Right_ClampsToAvailableEndX_WhenStopIsPastIt()
    {
        // TOC1 case: style declares a right tab at 540pt for full-page width, but the paragraph
        // lives in a 250pt-wide cell. The page number must still right-align at the cell edge.
        var stops = new List<TabStop>
        {
            new() { PositionPoints = 540, Alignment = TabAlignment.Right, Leader = TabLeader.Dot }
        };

        var (dest, stop) = TabStopResolver.Resolve(
            cursorX: 30, followingWidth: 8, stops,
            defaultTabStopPoints: 36, leftIndentPoints: 0,
            availableEndX: 250);

        await Assert.That(dest).IsEqualTo(242.0);
        await Assert.That(stop!.Leader).IsEqualTo(TabLeader.Dot);
    }

    [Test]
    public async Task Right_DoesNotClamp_WhenStopFitsInsideAvailableEndX()
    {
        var stops = new List<TabStop>
        {
            new() { PositionPoints = 200, Alignment = TabAlignment.Right, Leader = TabLeader.Dot }
        };

        var (dest, _) = TabStopResolver.Resolve(
            cursorX: 30, followingWidth: 8, stops,
            defaultTabStopPoints: 36, leftIndentPoints: 0,
            availableEndX: 250);

        await Assert.That(dest).IsEqualTo(192.0);
    }

    [Test]
    public async Task Center_ClampsToAvailableEndX_WhenStopIsPastIt()
    {
        var stops = new List<TabStop>
        {
            new() { PositionPoints = 540, Alignment = TabAlignment.Center }
        };

        var (dest, _) = TabStopResolver.Resolve(
            cursorX: 30, followingWidth: 60, stops,
            defaultTabStopPoints: 36, leftIndentPoints: 0,
            availableEndX: 250);

        await Assert.That(dest).IsEqualTo(220.0);
    }

    [Test]
    public async Task Left_DoesNotClampToAvailableEndX()
    {
        // Left tabs past the available area collapse via the cursor check; the renderer drops them
        // separately. We just assert the resolver does not silently relocate them.
        var stops = new List<TabStop>
        {
            new() { PositionPoints = 540, Alignment = TabAlignment.Left }
        };

        var (dest, stop) = TabStopResolver.Resolve(
            cursorX: 30, followingWidth: 8, stops,
            defaultTabStopPoints: 36, leftIndentPoints: 0,
            availableEndX: 250);

        await Assert.That(dest).IsEqualTo(540.0);
        await Assert.That(stop!.Alignment).IsEqualTo(TabAlignment.Left);
    }

    [Test]
    public async Task Decimal_AlignsDecimalPointAtStop()
    {
        // followingWidth=50 (whole "12.50"), decimalPrefixWidth=20 (just "12") → decimal lands at 144,
        // so destination = 144 - 20 = 124.
        var stops = new List<TabStop>
        {
            new() { PositionPoints = 144, Alignment = TabAlignment.Decimal }
        };

        var (dest, stop) = TabStopResolver.Resolve(
            cursorX: 20, followingWidth: 50, stops,
            defaultTabStopPoints: 36, leftIndentPoints: 0,
            decimalPrefixWidth: 20);

        await Assert.That(dest).IsEqualTo(124.0);
        await Assert.That(stop!.PositionPoints).IsEqualTo(144.0);
    }
}
