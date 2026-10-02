/// <summary>
/// Tests the <see cref="IParagraphMeasurer"/> surface over the canonical measurer
/// (<see cref="CanonicalParagraphMeasurer"/>, step 1 of <c>docs/layout-engine.md</c>): its
/// multi-run wrapping, per-line heights and before/after spacing, resolving fonts from the bundled
/// directory the same way the renderers do.
/// </summary>
public class CanonicalParagraphMeasurerTests
{
    static readonly string fontsDirectory = Path.GetFullPath(Path.Combine(ProjectFiles.ProjectDirectory, "..", "Fonts"));

    static readonly FontFileCache fonts = new(
        FontCacheLoader.EnumerateFontFilesInDirectory(fontsDirectory, recursive: true),
        OpenTypeReader.ReadFaces);

    static readonly CanonicalParagraphMeasurer measurer = new(Resolve);

    static FontMetrics? Resolve(string family, bool bold, bool italic)
    {
        var candidates = FontHelpers.GetCandidateNames(family, bold);
        if (!fonts.TryGet(candidates, out var faces) || faces.Length == 0)
        {
            return null;
        }

        var weight = FontHelpers.ResolveTargetWeight(family, bold);
        var best = faces.OrderBy(_ => FontHelpers.ScoreFace(_, weight, italic)).First();
        return FontMetricsReader.Read(best.Path, best.Index);
    }

    static Run Run(string text, string family, double size, bool bold = false, bool italic = false) =>
        new()
        {
            Text = text,
            Properties = new()
            {
                FontFamily = family,
                FontSizePoints = size,
                Bold = bold,
                Italic = italic
            }
        };

    static ParagraphElement Para(params Run[] runs) => new()
    {
        Runs = runs,
        Properties = new()
    };

    [Test]
    public async Task Uniform_paragraph_line_count_matches_the_single_font_measurer()
    {
        const string text = "The quick brown fox jumps over the lazy dog and then runs off into the field once again";
        var aptos = FontMetricsReader.Read(Path.Combine(fontsDirectory, "Aptos_400.ttf"))!;

        var expected = CanonicalTextMeasurer.WrapLines(aptos, text, 11, 200).Count;
        var lines = measurer.LayoutParagraphForMeasurement(Para(Run(text, "Aptos", 11)), 200);

        await Assert.That(lines.Count).IsEqualTo(expected);
        // Auto line spacing at the default 1.08 multiplier over the hhea pitch.
        await Assert.That(lines[0]).IsEqualTo((float) (aptos.LinePitchPoints(11) * 1.08)).Within(0.02f);
    }

    [Test]
    public async Task A_word_split_across_runs_is_not_broken()
    {
        // "Hello" as Hel (regular) + lo (bold): one word spanning two runs.
        var paragraph = Para(Run("Hel", "Aptos", 11), Run("lo", "Aptos", 11, bold: true));
        var natural = measurer.MeasureParagraphNaturalWidth(paragraph, 10000);
        await Assert.That(natural > 0).IsTrue();

        // At a measure that fits the whole word but nothing more, it stays one line — never split mid-word.
        var lines = measurer.LayoutParagraphForMeasurement(paragraph, natural + 2);
        await Assert.That(lines.Count).IsEqualTo(1);
    }

    // An autofit column is sized to its text's natural width plus the cell's chrome, and the cell
    // then takes the chrome back off — so the measure the text is wrapped at has been through float
    // arithmetic, and can land a few millionths of a point under the width it was read from. The
    // text still has to fit on the one line that was measured.
    [Test]
    public async Task A_line_fits_the_measure_taken_from_its_own_width()
    {
        var paragraph = Para(Run("Top margin emphasis", "Calibri", 12));
        var natural = measurer.MeasureParagraphNaturalWidth(paragraph, float.MaxValue / 4);

        // Every chrome up to 40pt, in the twentieths of a point that margins and borders come in.
        var wrapped = new List<float>();
        for (var twentieths = 0; twentieths <= 800; twentieths++)
        {
            var chrome = twentieths / 20f;
            var column = natural + chrome;
            if (measurer.LayoutParagraphForMeasurement(paragraph, column - chrome).Count != 1)
            {
                wrapped.Add(chrome);
            }
        }

        await Assert.That(wrapped).IsEmpty();
    }

    // Word's layout kerns a pair that straddles a space like any other. Probed on autofit columns
    // (_probe_spacekern, 2026-10-02): twenty "A" set a space apart in 12pt Arial, whose A+space and
    // space+A pairs are -113 units each, make a 331.4px column with kerning on and a 372.4px one
    // with it off. The wrap measures word by word, so the pair has to be carried between the pieces.
    [Test]
    public async Task Kerning_reaches_across_a_space()
    {
        var text = string.Join(' ', Enumerable.Repeat("A", 20));
        ParagraphElement Spaced(double kerningThreshold) =>
            Para(new Run
            {
                Text = text,
                Properties = new()
                {
                    FontFamily = "Arial",
                    FontSizePoints = 12,
                    KerningMinFontSizePoints = kerningThreshold
                }
            });

        var kernedPixels = measurer.MeasureParagraphNaturalWidth(Spaced(1), 10000) * 120.0 / 72;
        var unkernedPixels = measurer.MeasureParagraphNaturalWidth(Spaced(0), 10000) * 120.0 / 72;

        await Assert.That(unkernedPixels).IsEqualTo(372.4).Within(1);
        await Assert.That(kernedPixels).IsEqualTo(331.4).Within(2);
    }

    [Test]
    public async Task Empty_paragraph_is_one_mark_line_with_no_after_spacing()
    {
        var empty = new ParagraphElement
        {
            Runs =
            [
                new()
                {
                    Text = "",
                    Properties = new()
                    {
                        FontFamily = "Aptos",
                        FontSizePoints = 11
                    }
                }
            ],
            Properties = new()
            {
                SpacingAfterPoints = 12
            }
        };

        var lines = measurer.LayoutParagraphForMeasurement(empty, 200);
        await Assert.That(lines.Count).IsEqualTo(1);
        await Assert.That(lines[0] > 0).IsTrue();

        // The 12pt after-spacing is dropped for an empty (spacer) paragraph — height is just the mark line.
        await Assert.That(measurer.MeasureParagraphHeightWithWidth(empty, 200)).IsEqualTo(lines[0]).Within(0.01f);
    }

    [Test]
    public async Task Empty_paragraph_line_is_sized_by_the_mark_not_a_phantom_run()
    {
        // A blank spacer paragraph parked over a zero-length 11pt run whose font differs from the 8pt
        // paragraph mark (resumes/11's contact-block spacers, a deleted-text artefact). Word sizes the
        // line by the mark, not the phantom run — matching PdfTextEngine.EmptyLineHeight.
        var overPhantomRun = new ParagraphElement
        {
            Runs =
            [
                new()
                {
                    Text = "",
                    Properties = new()
                    {
                        FontFamily = "Aptos",
                        FontSizePoints = 11
                    }
                }
            ],
            Properties = new()
            {
                ParagraphMarkRunProperties = new()
                {
                    FontFamily = "Aptos",
                    FontSizePoints = 8
                }
            }
        };

        // The same 8pt mark with no phantom run — the reference for "sized by the mark".
        var markOnly = new ParagraphElement
        {
            Runs = [],
            Properties = new()
            {
                ParagraphMarkRunProperties = new()
                {
                    FontFamily = "Aptos",
                    FontSizePoints = 8
                }
            }
        };

        // A larger 11pt mark — the phantom run's size, which the line must NOT take.
        var largerMark = new ParagraphElement
        {
            Runs = [],
            Properties = new()
            {
                ParagraphMarkRunProperties = new()
                {
                    FontFamily = "Aptos",
                    FontSizePoints = 11
                }
            }
        };

        var height = measurer.LayoutParagraphForMeasurement(overPhantomRun, 200)[0];
        await Assert.That(height).IsEqualTo(measurer.LayoutParagraphForMeasurement(markOnly, 200)[0]).Within(0.01f);
        await Assert.That(height).IsLessThan(measurer.LayoutParagraphForMeasurement(largerMark, 200)[0]);
    }

    [Test]
    public async Task Height_includes_before_and_after_for_a_normal_paragraph()
    {
        var paragraph = new ParagraphElement
        {
            Runs = [Run("A single short line.", "Aptos", 11)],
            Properties = new()
            {
                SpacingBeforePoints = 6,
                SpacingAfterPoints = 8
            }
        };

        var lines = measurer.LayoutParagraphForMeasurement(paragraph, 500);
        await Assert.That(lines.Count).IsEqualTo(1);
        await Assert.That(measurer.MeasureParagraphHeightWithWidth(paragraph, 500)).IsEqualTo(6f + lines[0] + 8f).Within(0.02f);
    }
}
