/// <summary>
/// Tests for numbered list counter tracking, number formatting,
/// start overrides, and list restart behavior.
/// </summary>
public class NumberingTests
{
    static string InputsDir => Path.Combine(ProjectFiles.ProjectDirectory, "Inputs");

    static ParsedDocument Parse(string scenario)
    {
        var parser = new DocumentParser();
        using var stream = File.OpenRead(Path.Combine(InputsDir, scenario, "input.docx"));
        return parser.Parse(stream);
    }

    static List<ParagraphElement> GetNumberedParagraphs(ParsedDocument doc) =>
        doc.Elements
            .OfType<ParagraphElement>()
            .Where(_ => _.Properties.Numbering != null)
            .ToList();

    // === Counter tracking ===

    [Test]
    public async Task NumberedList_CountersIncrementSequentially()
    {
        var doc = Parse("numbered_list_tracking");
        var numbered = GetNumberedParagraphs(doc);

        // First list: items 1-5
        await Assert.That(numbered[0].Properties.Numbering!.Text).IsEqualTo("1.");
        await Assert.That(numbered[1].Properties.Numbering!.Text).IsEqualTo("2.");
        await Assert.That(numbered[2].Properties.Numbering!.Text).IsEqualTo("3.");
        await Assert.That(numbered[3].Properties.Numbering!.Text).IsEqualTo("4.");
        await Assert.That(numbered[4].Properties.Numbering!.Text).IsEqualTo("5.");
    }

    [Test]
    public async Task NumberedList_ContinuesAfterInterruption()
    {
        var doc = Parse("numbered_list_tracking");
        var numbered = GetNumberedParagraphs(doc);

        // After a non-list paragraph, same numId continues: 6, 7
        await Assert.That(numbered[5].Properties.Numbering!.Text).IsEqualTo("6.");
        await Assert.That(numbered[6].Properties.Numbering!.Text).IsEqualTo("7.");
    }

    [Test]
    public async Task NumberedList_DifferentNumId_RestartsCounter()
    {
        var doc = Parse("numbered_list_tracking");
        var numbered = GetNumberedParagraphs(doc);

        // Second list (different numId): restarts at 1
        await Assert.That(numbered[7].Properties.Numbering!.Text).IsEqualTo("1.");
        await Assert.That(numbered[8].Properties.Numbering!.Text).IsEqualTo("2.");
        await Assert.That(numbered[9].Properties.Numbering!.Text).IsEqualTo("3.");
    }

    // === Start override ===

    [Test]
    public async Task StartOverride_RestartsAtOne()
    {
        var doc = Parse("numbered_list_restart");
        var numbered = GetNumberedParagraphs(doc);

        // List A (numId=1, no override): 1, 2, 3
        await Assert.That(numbered[0].Properties.Numbering!.Text).IsEqualTo("1.");
        await Assert.That(numbered[1].Properties.Numbering!.Text).IsEqualTo("2.");
        await Assert.That(numbered[2].Properties.Numbering!.Text).IsEqualTo("3.");

        // List B (numId=2, startOverride=1, same abstractNum): restarts at 1
        await Assert.That(numbered[3].Properties.Numbering!.Text).IsEqualTo("1.");
        await Assert.That(numbered[4].Properties.Numbering!.Text).IsEqualTo("2.");
        await Assert.That(numbered[5].Properties.Numbering!.Text).IsEqualTo("3.");
    }

    [Test]
    public async Task StartOverride_CustomStartValue()
    {
        var doc = Parse("numbered_list_restart");
        var numbered = GetNumberedParagraphs(doc);

        // List C (numId=3, startOverride=10): starts at 10
        await Assert.That(numbered[6].Properties.Numbering!.Text).IsEqualTo("10.");
        await Assert.That(numbered[7].Properties.Numbering!.Text).IsEqualTo("11.");
        await Assert.That(numbered[8].Properties.Numbering!.Text).IsEqualTo("12.");
    }

    // === Bullet lists (no counter tracking) ===

    [Test]
    public async Task BulletList_UsesStaticBulletCharacter()
    {
        var doc = Parse("bullet_list");
        var numbered = GetNumberedParagraphs(doc);

        await Assert.That(numbered.Count).IsGreaterThan(0);
        // All bullets use the same character
        foreach (var para in numbered)
        {
            await Assert.That(para.Properties.Numbering!.Text).IsEqualTo("•");
        }
    }

    // === Numbering info properties ===

    [Test]
    public async Task NumberingInfo_HasIndentation()
    {
        var doc = Parse("numbered_list_tracking");
        var numbered = GetNumberedParagraphs(doc);

        var info = numbered[0].Properties.Numbering!;
        await Assert.That(info.IndentPoints).IsGreaterThan(0);
        await Assert.That(info.HangingIndentPoints).IsGreaterThan(0);
    }

    [Test]
    public async Task NumberingInfo_NonNumberedParagraphs_HaveNoNumbering()
    {
        var doc = Parse("numbered_list_tracking");
        var nonNumbered = doc.Elements
            .OfType<ParagraphElement>()
            .Where(_ => _.Properties.Numbering == null &&
                        _.Runs.Count > 0)
            .ToList();

        await Assert.That(nonNumbered.Count).IsGreaterThan(0);
    }

    // === Existing numbered_list scenario ===

    [Test]
    public async Task NumberedList_OriginalScenario_HasCorrectCounters()
    {
        var doc = Parse("numbered_list");
        var numbered = GetNumberedParagraphs(doc);

        await Assert.That(numbered.Count).IsGreaterThanOrEqualTo(4);
        await Assert.That(numbered[0].Properties.Numbering!.Text).IsEqualTo("1.");
        await Assert.That(numbered[1].Properties.Numbering!.Text).IsEqualTo("2.");
        await Assert.That(numbered[2].Properties.Numbering!.Text).IsEqualTo("3.");
        await Assert.That(numbered[3].Properties.Numbering!.Text).IsEqualTo("4.");
    }

    // === Nested list scenario ===

    [Test]
    public async Task NestedList_BulletsAtMultipleLevels()
    {
        var doc = Parse("nested_list");
        var numbered = GetNumberedParagraphs(doc);

        await Assert.That(numbered.Count).IsGreaterThan(0);
        // All should have numbering info with valid text
        foreach (var para in numbered)
        {
            await Assert.That(para.Properties.Numbering!.Text).IsNotNull();
            await Assert.That(para.Properties.Numbering!.Text.Length).IsGreaterThan(0);
        }
    }

    // === Deep nested list ===

    [Test]
    public async Task DeepNestedList_HasMultipleLevels()
    {
        var doc = Parse("deep_nested_list");
        var numbered = GetNumberedParagraphs(doc);

        await Assert.That(numbered.Count).IsGreaterThan(1);

        // Multiple different bullet characters across levels
        var bulletTexts = numbered
            .Select(_ => _.Properties.Numbering!.Text)
            .Distinct()
            .ToList();

        await Assert.That(bulletTexts.Count).IsGreaterThan(1);
    }

    // === Style-cascade for indent on numbered paragraph ===

    [Test]
    public async Task BulletParagraph_StyleIndentBeatsNumberingLevelIndent()
    {
        // agendas-minutes/07 uses ListBullet style with <w:ind w:left="432" w:hanging="288">
        // (= 21.6pt / 14.4pt) and a numbering level with <w:ind w:left="720" w:hanging="360">
        // (= 36pt / 18pt). Per Word's actual cascade, the style indent wins because the
        // numbering level's pPr is treated as a low-priority default.
        var doc = Parse("agendas-minutes/07");

        static ParagraphElement? Find(IEnumerable<DocumentElement> elems)
        {
            foreach (var e in elems)
            {
                if (e is ParagraphElement p && string.Concat(p.Runs.Select(r => r.Text)).Contains("Membership"))
                {
                    return p;
                }
                if (e is TableElement t)
                {
                    foreach (var row in t.Rows)
                    foreach (var cell in row.Cells)
                    {
                        var found = Find(cell.Content);
                        if (found != null) return found;
                    }
                }
            }
            return null;
        }

        var p = Find(doc.Elements);
        await Assert.That(p).IsNotNull();
        await Assert.That(p!.Properties.LeftIndentPoints).IsEqualTo(21.6);
        await Assert.That(p.Properties.HangingIndentPoints).IsEqualTo(14.4);
    }

    // === Multi-level restart (OOXML w:lvlRestart default behaviour) ===

    [Test]
    public async Task MultiLevel_ChildCounterRestartsWhenParentIncrements()
    {
        // agendas-minutes/04 uses a Heading2/Heading3 multi-level list (numId=1,
        // ilvl=0/1 — upperRoman / lowerLetter) with no <w:lvlRestart>, so each
        // Roman section should start its child letter sequence at "a." again.
        var doc = Parse("agendas-minutes/04");
        var numbered = GetNumberedParagraphs(doc);

        var texts = numbered.Select(_ => _.Properties.Numbering!.Text).ToList();

        await Assert.That(texts).IsEquivalentTo(new List<string>
        {
            "I.",  "a.", "b.",  // Introductions
            "II.", "a.", "b.",  // New business
            "III.","a.",        // Old business
            "IV.", "a."         // Conclusion
        });
    }
}
