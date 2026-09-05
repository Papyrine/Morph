using A = DocumentFormat.OpenXml.Drawing;

public class GradientFillParseTests
{
    static A.GradientFill BuildGradient(double angleDegrees, params (int Position, string Color)[] stops)
    {
        var gradFill = new A.GradientFill();
        var stopList = new A.GradientStopList();
        foreach (var (pos, color) in stops)
        {
            stopList.AppendChild(new A.GradientStop
            {
                Position = pos,
                RgbColorModelHex = new()
                {
                    Val = color
                }
            });
        }

        gradFill.AppendChild(stopList);
        gradFill.AppendChild(new A.LinearGradientFill
        {
            Angle = (int) (angleDegrees * 60000)
        });
        return gradFill;
    }

    [Test]
    public async Task ReadsTheStopAlphaAndDefaultsItToOpaque()
    {
        var gradFill = BuildGradient(90, (0, "FF0000"), (100000, "0000FF"));
        var stops = gradFill.GetFirstChild<A.GradientStopList>()!.Elements<A.GradientStop>().ToList();
        stops[0].RgbColorModelHex!.AppendChild(new A.Alpha { Val = 30000 });

        var result = ShapeParser.ExtractGradientFill(gradFill, null);

        await Assert.That(result!.StartAlpha).IsEqualTo(0.3).Within(0.0001);
        await Assert.That(result.EndAlpha).IsEqualTo(1.0);
    }

    [Test]
    public async Task ExtractsTwoStopLinearGradient()
    {
        var gradFill = BuildGradient(90, (0, "FF0000"), (100000, "0000FF"));

        var result = ShapeParser.ExtractGradientFill(gradFill, null);

        await Assert.That(result).IsNotNull();
        await Assert.That(result!.StartColorHex).IsEqualTo("FF0000");
        await Assert.That(result.EndColorHex).IsEqualTo("0000FF");
        await Assert.That(result.DirectionDegrees).IsEqualTo(90.0);
    }

    [Test]
    public async Task FlattensMultiStopGradientToFirstAndLastByPosition()
    {
        // Out-of-order stops should still resolve to lowest/highest position pair.
        var gradFill = BuildGradient(0,
            (50000, "00FF00"),
            (100000, "0000FF"),
            (0, "FF0000"));

        var result = ShapeParser.ExtractGradientFill(gradFill, null);

        await Assert.That(result).IsNotNull();
        await Assert.That(result!.StartColorHex).IsEqualTo("FF0000");
        await Assert.That(result.EndColorHex).IsEqualTo("0000FF");
    }

    [Test]
    public async Task DefaultsAngleToZeroWhenLinearElementMissing()
    {
        var gradFill = new A.GradientFill();
        var stopList = new A.GradientStopList();
        stopList.AppendChild(new A.GradientStop
        {
            Position = 0,
            RgbColorModelHex =
                new()
                {
                    Val = "AAAAAA"
                }
        });
        stopList.AppendChild(new A.GradientStop
        {
            Position = 100000,
            RgbColorModelHex =
                new()
                {
                    Val = "BBBBBB"
                }
        });
        gradFill.AppendChild(stopList);

        var result = ShapeParser.ExtractGradientFill(gradFill, null);

        await Assert.That(result).IsNotNull();
        await Assert.That(result!.DirectionDegrees).IsEqualTo(0.0);
    }

    [Test]
    public async Task ReturnsNullWhenNoStopList()
    {
        var gradFill = new A.GradientFill();
        gradFill.AppendChild(new A.LinearGradientFill
        {
            Angle = 0
        });

        var result = ShapeParser.ExtractGradientFill(gradFill, null);

        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task ReturnsNullWhenFewerThanTwoStops()
    {
        var gradFill = new A.GradientFill();
        var stopList = new A.GradientStopList();
        stopList.AppendChild(new A.GradientStop
        {
            Position = 0,
            RgbColorModelHex =
                new()
                {
                    Val = "FF0000"
                }
        });
        gradFill.AppendChild(stopList);

        var result = ShapeParser.ExtractGradientFill(gradFill, null);

        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task ReturnsNullWhenStopMissingPosition()
    {
        // Stops without explicit Position attribute are skipped; if that leaves <2 stops, gradient fails.
        var gradFill = new A.GradientFill();
        var stopList = new A.GradientStopList();
        stopList.AppendChild(new A.GradientStop
        {
            RgbColorModelHex =
                new()
                {
                    Val = "FF0000"
                }
        });
        stopList.AppendChild(
            new A.GradientStop
            {
                Position = 100000,
                RgbColorModelHex = new()
                {
                    Val = "0000FF"
                }
            });
        gradFill.AppendChild(stopList);

        var result = ShapeParser.ExtractGradientFill(gradFill, null);

        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task AngleIsConvertedFromSixtyThousandthsOfDegrees()
    {
        // OOXML stores `ang` in 60000ths of a degree; 5400000 = 90°.
        var gradFill = BuildGradient(135, (0, "112233"), (100000, "445566"));

        var result = ShapeParser.ExtractGradientFill(gradFill, null);

        await Assert.That(result!.DirectionDegrees).IsEqualTo(135.0);
    }
}
