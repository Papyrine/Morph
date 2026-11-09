/// <summary>
/// Flattens Office Math (<c>m:oMath</c>) into runs of text, the way Word's own linear format
/// reads it.
/// </summary>
static class MathParser
{
    public static void AppendMathText(List<Run> runs, OpenXmlElement mathElement)
    {
        // Math text inherits the surrounding run's font size when available so equations don't
        // collapse to the 11pt default, but sets in Cambria — Word's math face is Cambria Math
        // regardless of the body font, and plain Cambria gives the same serif letterforms at a
        // normal line box (Cambria Math's own vertical metrics are sized for stretchy operators
        // and inflated every math line ~55px when tried). Variables render italic by default —
        // Word's convention; numbers and operators stay upright (handled inside EmitMathRun).
        var context = runs.Count > 0 ? runs[^1].Properties : new();
        WalkMath(mathElement, runs, context with
        {
            Italic = true,
            FontFamily = "Cambria"
        });
    }

    static void WalkMath(OpenXmlElement element, List<Run> runs, RunProperties props)
    {
        foreach (var child in element.ChildElements)
        {
            switch (child)
            {
                case DocumentFormat.OpenXml.Math.Run mathRun:
                    EmitMathRun(mathRun, runs, props);
                    break;

                case DocumentFormat.OpenXml.Math.Subscript sSub:
                {
                    var b = sSub.GetFirstChild<DocumentFormat.OpenXml.Math.Base>();
                    if (b != null) WalkMath(b, runs, props);
                    var sub = sSub.GetFirstChild<DocumentFormat.OpenXml.Math.SubArgument>();
                    if (sub != null)
                        WalkMath(sub, runs, props with
                        {
                            VerticalAlignment = VerticalRunAlignment.Subscript
                        });
                    break;
                }

                case DocumentFormat.OpenXml.Math.Superscript sSup:
                {
                    var b = sSup.GetFirstChild<DocumentFormat.OpenXml.Math.Base>();
                    if (b != null) WalkMath(b, runs, props);
                    var sup = sSup.GetFirstChild<DocumentFormat.OpenXml.Math.SuperArgument>();
                    if (sup != null)
                        WalkMath(sup, runs, props with
                        {
                            VerticalAlignment = VerticalRunAlignment.Superscript
                        });
                    break;
                }

                case DocumentFormat.OpenXml.Math.SubSuperscript sSubSup:
                {
                    var b = sSubSup.GetFirstChild<DocumentFormat.OpenXml.Math.Base>();
                    if (b != null) WalkMath(b, runs, props);
                    var sub = sSubSup.GetFirstChild<DocumentFormat.OpenXml.Math.SubArgument>();
                    if (sub != null)
                        WalkMath(sub, runs, props with
                        {
                            VerticalAlignment = VerticalRunAlignment.Subscript
                        });
                    var sup = sSubSup.GetFirstChild<DocumentFormat.OpenXml.Math.SuperArgument>();
                    if (sup != null)
                        WalkMath(sup, runs, props with
                        {
                            VerticalAlignment = VerticalRunAlignment.Superscript
                        });
                    break;
                }

                case DocumentFormat.OpenXml.Math.Fraction frac when BuiltUp(frac):
                {
                    // A bar (or bar-less) fraction is built up: its levels stacked about the math axis,
                    // at Cambria Math's script scale of 73% (_probe_math: 8.4pt digits for 12pt math,
                    // 17.4 for 24, on Word's 120-dpi grid; its widths are the unrounded 8.76 and 17.52).
                    var numerator = new List<Run>();
                    var denominator = new List<Run>();
                    if (frac.GetFirstChild<DocumentFormat.OpenXml.Math.Numerator>() is { } numeratorElement)
                    {
                        WalkMath(numeratorElement, numerator, props);
                    }

                    if (frac.GetFirstChild<DocumentFormat.OpenXml.Math.Denominator>() is { } denominatorElement)
                    {
                        WalkMath(denominatorElement, denominator, props);
                    }

                    var size = numerator.Concat(denominator).FirstOrDefault()?.Properties.FontSizePoints ?? props.FontSizePoints;
                    var hasBar = frac.FractionProperties?.FractionType?.Val?.Value != DocumentFormat.OpenXml.Math.FractionTypeValues.NoBar;
                    runs.Add(
                        new()
                        {
                            Text = "",
                            Properties = props,
                            InlineFraction = new(ScriptLevel(numerator), ScriptLevel(denominator), size, hasBar)
                        });
                    break;
                }

                case DocumentFormat.OpenXml.Math.Fraction frac:
                {
                    // A linear or skewed fraction stays inline: numerator "/" denominator.
                    var num = frac.GetFirstChild<DocumentFormat.OpenXml.Math.Numerator>();
                    if (num != null)
                    {
                        WalkMath(num, runs, props);
                    }

                    runs.Add(
                        new()
                        {
                            Text = "/",
                            Properties = props with
                            {
                                Italic = false
                            }
                        });
                    var den = frac.GetFirstChild<DocumentFormat.OpenXml.Math.Denominator>();
                    if (den != null)
                    {
                        WalkMath(den, runs, props);
                    }

                    break;
                }

                default:
                    // Recurse into other math composites (m:e, m:rad, m:nary, m:func, etc.)
                    // so their inner runs still surface as plain text.
                    if (child is OpenXmlCompositeElement composite)
                    {
                        WalkMath(composite, runs, props);
                    }

                    break;
            }
        }
    }

    static void EmitMathRun(DocumentFormat.OpenXml.Math.Run mathRun, List<Run> runs, RunProperties props)
    {
        foreach (var mathText in mathRun.Elements<DocumentFormat.OpenXml.Math.Text>())
        {
            var text = mathText.Text;
            if (string.IsNullOrEmpty(text))
            {
                continue;
            }

            // Math italic only applies to alphabetic variables; digits and operators stay upright.
            // Word also spaces binary operators and relations — "a²+b²=c²" prints as
            // "a² + b² = c²" — which OMML leaves implicit, so a bare operator token gains the
            // padding here.
            var isVariable = text.All(char.IsLetter);
            if (text.Length == 1 &&
                "+−-=<>×÷±≤≥≠".Contains(text[0]))
            {
                text = $" {text} ";
            }

            runs.Add(
                new()
                {
                    Text = text,
                    Properties = isVariable
                        ? props
                        : props with
                        {
                            Italic = false
                        }
                });
        }
    }

    // A fraction is built up unless its m:fPr/m:type says it is set on the line (lin) or skewed (skw).
    static bool BuiltUp(DocumentFormat.OpenXml.Math.Fraction fraction) =>
        fraction.FractionProperties?.FractionType?.Val?.Value is not {} type ||
        type == DocumentFormat.OpenXml.Math.FractionTypeValues.Bar ||
        type == DocumentFormat.OpenXml.Math.FractionTypeValues.NoBar;

    // A fraction's level in Cambria Math's script size, 73% of the math it stands in.
    static List<Run> ScriptLevel(List<Run> runs) =>
        [.. runs.Select(_ => _.WithProperties(_.Properties with
        {
            FontSizePoints = _.Properties.FontSizePoints * 0.73
        }))];
}
