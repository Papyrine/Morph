/// <summary>
/// Tests for InkParser.ScaleStrokesToCanvas functionality.
/// </summary>
public class InkParserScalingTests
{
    [Test]
    public async Task ScaleStrokesToCanvas_EmptyStrokes_DoesNothing()
    {
        var strokes = new List<InkStroke>();

        InkParser.ScaleStrokesToCanvas(strokes, 100, 100);

        await Assert.That(strokes).IsEmpty();
    }

    [Test]
    public async Task ScaleStrokesToCanvas_SingleStroke_ScalesToFitCanvas()
    {
        var strokes = new List<InkStroke>
        {
            new()
            {
                Points = new List<InkPoint>
                {
                    new() { X = 0, Y = 0 },
                    new() { X = 200, Y = 100 }
                }
            }
        };

        InkParser.ScaleStrokesToCanvas(strokes, 100, 50);

        // Original: 200x100, Canvas: 100x50
        // Scale factor = min(100/200, 50/100) = min(0.5, 0.5) = 0.5
        // Points should be scaled to (0,0) and (100,50)
        await Assert.That(strokes[0].Points[0].X).IsEqualTo(0);
        await Assert.That(strokes[0].Points[0].Y).IsEqualTo(0);
        await Assert.That(strokes[0].Points[1].X).IsEqualTo(100);
        await Assert.That(strokes[0].Points[1].Y).IsEqualTo(50);
    }

    [Test]
    public async Task ScaleStrokesToCanvas_PreservesAspectRatio()
    {
        var strokes = new List<InkStroke>
        {
            new()
            {
                Points = new List<InkPoint>
                {
                    new() { X = 0, Y = 0 },
                    new() { X = 100, Y = 100 }
                }
            }
        };

        // Canvas is wider than tall - should scale to fit height
        InkParser.ScaleStrokesToCanvas(strokes, 200, 50);

        // Original: 100x100, Canvas: 200x50
        // Scale factor = min(200/100, 50/100) = min(2, 0.5) = 0.5
        // Points should be scaled to (0,0) and (50,50)
        await Assert.That(strokes[0].Points[1].X).IsEqualTo(50);
        await Assert.That(strokes[0].Points[1].Y).IsEqualTo(50);
    }

    [Test]
    public async Task ScaleStrokesToCanvas_TranslatesToOrigin()
    {
        var strokes = new List<InkStroke>
        {
            new()
            {
                Points = new List<InkPoint>
                {
                    new() { X = 100, Y = 50 },
                    new() { X = 200, Y = 150 }
                }
            }
        };

        InkParser.ScaleStrokesToCanvas(strokes, 100, 100);

        // Original bounding box: (100,50) to (200,150) = 100x100
        // After translation to origin: (0,0) to (100,100)
        // Scale factor = min(100/100, 100/100) = 1.0
        await Assert.That(strokes[0].Points[0].X).IsEqualTo(0);
        await Assert.That(strokes[0].Points[0].Y).IsEqualTo(0);
        await Assert.That(strokes[0].Points[1].X).IsEqualTo(100);
        await Assert.That(strokes[0].Points[1].Y).IsEqualTo(100);
    }

    [Test]
    public async Task ScaleStrokesToCanvas_MultipleStrokes_ScalesTogether()
    {
        var strokes = new List<InkStroke>
        {
            new()
            {
                Points = new List<InkPoint>
                {
                    new() { X = 0, Y = 0 },
                    new() { X = 50, Y = 50 }
                }
            },
            new()
            {
                Points = new List<InkPoint>
                {
                    new() { X = 50, Y = 50 },
                    new() { X = 100, Y = 100 }
                }
            }
        };

        InkParser.ScaleStrokesToCanvas(strokes, 200, 200);

        // Original bounding box across both strokes: (0,0) to (100,100)
        // Scale factor = min(200/100, 200/100) = 2.0
        await Assert.That(strokes[0].Points[0].X).IsEqualTo(0);
        await Assert.That(strokes[0].Points[0].Y).IsEqualTo(0);
        await Assert.That(strokes[0].Points[1].X).IsEqualTo(100);
        await Assert.That(strokes[0].Points[1].Y).IsEqualTo(100);
        await Assert.That(strokes[1].Points[0].X).IsEqualTo(100);
        await Assert.That(strokes[1].Points[0].Y).IsEqualTo(100);
        await Assert.That(strokes[1].Points[1].X).IsEqualTo(200);
        await Assert.That(strokes[1].Points[1].Y).IsEqualTo(200);
    }

    [Test]
    public async Task ScaleStrokesToCanvas_PreservesStrokeProperties()
    {
        var strokes = new List<InkStroke>
        {
            new()
            {
                Points = new List<InkPoint>
                {
                    new() { X = 0, Y = 0 },
                    new() { X = 100, Y = 100 }
                },
                ColorHex = "FF0000",
                WidthPoints = 2.5,
                Transparency = 128,
                PenTip = InkPenTip.Rectangle,
                IsHighlighter = true
            }
        };

        InkParser.ScaleStrokesToCanvas(strokes, 50, 50);

        await Assert.That(strokes[0].ColorHex).IsEqualTo("FF0000");
        await Assert.That(strokes[0].WidthPoints).IsEqualTo(2.5);
        await Assert.That(strokes[0].Transparency).IsEqualTo((byte)128);
        await Assert.That(strokes[0].PenTip).IsEqualTo(InkPenTip.Rectangle);
        await Assert.That(strokes[0].IsHighlighter).IsTrue();
    }

    [Test]
    public async Task ScaleStrokesToCanvas_PreservesPressure()
    {
        var strokes = new List<InkStroke>
        {
            new()
            {
                Points = new List<InkPoint>
                {
                    new() { X = 0, Y = 0, Pressure = 0.5 },
                    new() { X = 100, Y = 100, Pressure = 1.0 }
                }
            }
        };

        InkParser.ScaleStrokesToCanvas(strokes, 50, 50);

        await Assert.That(strokes[0].Points[0].Pressure).IsEqualTo(0.5);
        await Assert.That(strokes[0].Points[1].Pressure).IsEqualTo(1.0);
    }

    [Test]
    public async Task ScaleStrokesToCanvas_ZeroWidthInk_HandledGracefully()
    {
        // Vertical line (zero width)
        var strokes = new List<InkStroke>
        {
            new()
            {
                Points = new List<InkPoint>
                {
                    new() { X = 50, Y = 0 },
                    new() { X = 50, Y = 100 }
                }
            }
        };

        InkParser.ScaleStrokesToCanvas(strokes, 100, 50);

        // Should scale Y to fit canvas height, X stays at 0 (translated from 50)
        await Assert.That(strokes[0].Points[0].X).IsEqualTo(0);
        await Assert.That(strokes[0].Points[0].Y).IsEqualTo(0);
        await Assert.That(strokes[0].Points[1].X).IsEqualTo(0);
        await Assert.That(strokes[0].Points[1].Y).IsEqualTo(50);
    }

    [Test]
    public async Task ScaleStrokesToCanvas_ZeroHeightInk_HandledGracefully()
    {
        // Horizontal line (zero height)
        var strokes = new List<InkStroke>
        {
            new()
            {
                Points = new List<InkPoint>
                {
                    new() { X = 0, Y = 50 },
                    new() { X = 100, Y = 50 }
                }
            }
        };

        InkParser.ScaleStrokesToCanvas(strokes, 50, 100);

        // Should scale X to fit canvas width, Y stays at 0 (translated from 50)
        await Assert.That(strokes[0].Points[0].X).IsEqualTo(0);
        await Assert.That(strokes[0].Points[0].Y).IsEqualTo(0);
        await Assert.That(strokes[0].Points[1].X).IsEqualTo(50);
        await Assert.That(strokes[0].Points[1].Y).IsEqualTo(0);
    }

    [Test]
    public async Task ScaleStrokesToCanvas_SinglePoint_NoScaling()
    {
        // Single point has zero extent
        var strokes = new List<InkStroke>
        {
            new()
            {
                Points = new List<InkPoint>
                {
                    new() { X = 50, Y = 50 }
                }
            }
        };

        InkParser.ScaleStrokesToCanvas(strokes, 100, 100);

        // With zero extent, no scaling should be applied
        // Point should remain at original position (no translation since min=max)
        await Assert.That(strokes[0].Points[0].X).IsEqualTo(50);
        await Assert.That(strokes[0].Points[0].Y).IsEqualTo(50);
    }
}
