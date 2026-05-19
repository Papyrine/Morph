/// <summary>
/// Tests for InkML elements (pen/handwriting input).
/// </summary>
public class InkElementTests
{
    [Test]
    public async Task InkElement_RequiredProperties_CanBeSet()
    {
        var element = new InkElement
        {
            WidthPoints = 200,
            HeightPoints = 100,
            Strokes = new List<InkStroke>()
        };

        await Assert.That(element.WidthPoints).IsEqualTo(200);
        await Assert.That(element.HeightPoints).IsEqualTo(100);
        await Assert.That(element.Strokes).IsNotNull();
    }

    [Test]
    public async Task InkElement_WithStrokes_ContainsStrokes()
    {
        var strokes = new List<InkStroke>
        {
            new()
            {
                Points = new List<InkPoint>
                {
                    new() { X = 0, Y = 0 },
                    new() { X = 100, Y = 50 }
                }
            }
        };

        var element = new InkElement
        {
            WidthPoints = 200,
            HeightPoints = 100,
            Strokes = strokes
        };

        await Assert.That(element.Strokes.Count).IsEqualTo(1);
        await Assert.That(element.Strokes[0].Points.Count).IsEqualTo(2);
    }

    [Test]
    public async Task InkStroke_DefaultValues_AreCorrect()
    {
        var stroke = new InkStroke
        {
            Points = new List<InkPoint> { new() { X = 0, Y = 0 } }
        };

        await Assert.That(stroke.ColorHex).IsEqualTo("000000");
        await Assert.That(stroke.WidthPoints).IsEqualTo(1.5);
        await Assert.That(stroke.Transparency).IsEqualTo((byte)0);
        await Assert.That(stroke.PenTip).IsEqualTo(InkPenTip.Ellipse);
        await Assert.That(stroke.IsHighlighter).IsFalse();
    }

    [Test]
    public async Task InkStroke_ColorHex_CanBeSet()
    {
        var stroke = new InkStroke
        {
            Points = new List<InkPoint> { new() { X = 0, Y = 0 } },
            ColorHex = "FF0000"
        };

        await Assert.That(stroke.ColorHex).IsEqualTo("FF0000");
    }

    [Test]
    [Arguments(0.5)]
    [Arguments(1.0)]
    [Arguments(1.5)]
    [Arguments(2.0)]
    [Arguments(5.0)]
    public async Task InkStroke_WidthPoints_CanBeSet(double width)
    {
        var stroke = new InkStroke
        {
            Points = new List<InkPoint> { new() { X = 0, Y = 0 } },
            WidthPoints = width
        };

        await Assert.That(stroke.WidthPoints).IsEqualTo(width);
    }

    [Test]
    public async Task InkStroke_PenTipRectangle_CanBeSet()
    {
        var stroke = new InkStroke
        {
            Points = new List<InkPoint> { new() { X = 0, Y = 0 } },
            PenTip = InkPenTip.Rectangle
        };

        await Assert.That(stroke.PenTip).IsEqualTo(InkPenTip.Rectangle);
    }

    [Test]
    public async Task InkStroke_IsHighlighter_CanBeTrue()
    {
        var stroke = new InkStroke
        {
            Points = new List<InkPoint> { new() { X = 0, Y = 0 } },
            IsHighlighter = true
        };

        await Assert.That(stroke.IsHighlighter).IsTrue();
    }

    [Test]
    public async Task InkPoint_RequiredProperties_CanBeSet()
    {
        var point = new InkPoint { X = 50.5, Y = 100.25 };

        await Assert.That(point.X).IsEqualTo(50.5);
        await Assert.That(point.Y).IsEqualTo(100.25);
    }

    [Test]
    public async Task InkPoint_Pressure_IsOptional()
    {
        var point = new InkPoint { X = 0, Y = 0 };
        await Assert.That(point.Pressure).IsNull();
    }

    [Test]
    public async Task InkPoint_Pressure_CanBeSet()
    {
        var point = new InkPoint { X = 0, Y = 0, Pressure = 0.75 };
        await Assert.That(point.Pressure).IsEqualTo(0.75);
    }

    [Test]
    public async Task InkElement_MultipleStrokes_Supported()
    {
        var element = new InkElement
        {
            WidthPoints = 300,
            HeightPoints = 200,
            Strokes = new List<InkStroke>
            {
                new()
                {
                    Points = new List<InkPoint>
                    {
                        new() { X = 0, Y = 0 },
                        new() { X = 100, Y = 100 }
                    },
                    ColorHex = "FF0000"
                },
                new()
                {
                    Points = new List<InkPoint>
                    {
                        new() { X = 0, Y = 100 },
                        new() { X = 100, Y = 0 }
                    },
                    ColorHex = "0000FF"
                }
            }
        };

        await Assert.That(element.Strokes.Count).IsEqualTo(2);
        await Assert.That(element.Strokes[0].ColorHex).IsEqualTo("FF0000");
        await Assert.That(element.Strokes[1].ColorHex).IsEqualTo("0000FF");
    }
}
