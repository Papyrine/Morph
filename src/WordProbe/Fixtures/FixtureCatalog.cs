// The fixture documents, built once and committed. Texts avoid the markdown linter's banned words so the
// notation can be quoted anywhere. Geometry fixtures use a 24pt face and 0.5" cell margins: a glyph half
// or a cell edge is then many pixels wide (CLAUDE.md's "amplify the variable").
record Fixture(string Name, string Summary, Action<FixtureBuilder> Build);

static class FixtureCatalog
{
    const string fox = "The quick brown fox jumps over the lazy dog.";

    public static IReadOnlyList<Fixture> All { get; } =
    [
        new("plain", "One Normal paragraph: the fox sentence.", _ => _.Paragraph(fox)),
        new("three-paragraphs", "Three Normal paragraphs; the second is centred with one bold word (EditFixtures.Paragraphs).", _ =>
        {
            _.Paragraph("First paragraph here.");
            _.Paragraph("Second bold one.", alignment: Word.WdParagraphAlignment.wdAlignParagraphCenter);
            _.Bold(2, "bold");
            _.Paragraph("Third and last.");
        }),
        new("words", "Word-boundary material at 24pt: double space, hyphen, apostrophe, numbers with punctuation.", _ =>
        {
            _.Paragraph("alpha beta,  gamma-delta 3.14 don't (eps) 1,000 10:30 end.");
            _.Size(24);
        }),
        new("sentences", "Two paragraphs of short sentences with an abbreviation and a question.", _ =>
        {
            _.Paragraph("One is first. Two is next? Three uses e.g. four here. Five ends it.");
            _.Paragraph("Second line starts. It ends here.");
        }),
        new("wrap", "One paragraph at 24pt that wraps to three lines.", _ =>
        {
            _.Paragraph("Alpha beta gamma delta epsilon zeta eta theta iota kappa lambda mu nu xi omicron pi rho sigma tau upsilon phi chi psi omega alpha beta gamma delta epsilon zeta.");
            _.Size(24);
        }),
        new("long-paragraph", "Three paragraphs; the middle one wraps to several lines at the default size.", _ =>
        {
            _.Paragraph("Short first.");
            _.Paragraph("A long second paragraph that runs on past the right margin so that it wraps to a second and then a third line of text on the page, giving Up and Down keys lines to move between before it finally ends here.");
            _.Paragraph("Short third.");
        }),
        new("runs", "One paragraph with bold, italic, underlined and larger runs.", _ =>
        {
            _.Paragraph("Plain bold italic under big end.");
            _.Bold(1, "bold");
            _.Italic(1, "italic");
            _.Underline(1, "under");
            _.Font(1, "big", "Arial", 20);
        }),
        new("mixed", "Two paragraphs: bold + italic + plain runs, then two fonts at two sizes.", _ =>
        {
            _.Paragraph("Strong soft plain words.");
            _.Bold(1, "Strong");
            _.Italic(1, "soft");
            _.Paragraph("Serif face sans face.");
            _.Font(2, "Serif face", "Times New Roman", 14);
            _.Font(2, "sans face", "Arial", 10);
        }),
        new("heading-body", "Heading 1, then a Normal paragraph.", _ =>
        {
            _.Paragraph("Title here", Word.WdBuiltinStyle.wdStyleHeading1);
            _.Paragraph("Body text follows.");
        }),
        new("headings", "Heading 1, Heading 2, Heading 3, Normal.", _ =>
        {
            _.Paragraph("Level one", Word.WdBuiltinStyle.wdStyleHeading1);
            _.Paragraph("Level two", Word.WdBuiltinStyle.wdStyleHeading2);
            _.Paragraph("Level three", Word.WdBuiltinStyle.wdStyleHeading3);
            _.Paragraph("Body under them.");
        }),
        new("kinds", "One paragraph of each kind: Normal, Heading 1, bullet, centred, bordered, indented, empty, keep-with-next.", _ =>
        {
            _.Paragraph("Normal kind.");
            _.Paragraph("Heading kind", Word.WdBuiltinStyle.wdStyleHeading1);
            _.Paragraph("Bullet kind.");
            _.Bullets(3, 3);
            _.Paragraph("Centred kind.", alignment: Word.WdParagraphAlignment.wdAlignParagraphCenter);
            var bordered = _.Paragraph("Bordered kind.");
            bordered.Borders.Enable = 1;
            var indented = _.Paragraph("Indented kind.");
            indented.LeftIndent = 36;
            _.Empty();
            var keep = _.Paragraph("Keep with next kind.");
            keep.KeepWithNext = -1;
            _.Paragraph("Last plain kind.");
        }),
        new("merge-pairs", "Six paragraphs that each carry ONE property (centred, indented, bordered, spaced after, Heading 1, bulleted), each followed by a plain paragraph, so a merge across any boundary differs in one thing only.", _ =>
        {
            _.Paragraph("Centred one.", alignment: Word.WdParagraphAlignment.wdAlignParagraphCenter);
            _.Paragraph("Plain two.");
            var indented = _.Paragraph("Indented three.");
            indented.LeftIndent = 36;
            _.Paragraph("Plain four.");
            var bordered = _.Paragraph("Bordered five.");
            bordered.Borders.Enable = 1;
            _.Paragraph("Plain six.");
            var spaced = _.Paragraph("Spaced seven.");
            spaced.SpaceAfter = 24;
            _.Paragraph("Plain eight.");
            _.Paragraph("Heading nine", Word.WdBuiltinStyle.wdStyleHeading1);
            _.Paragraph("Plain ten.");
            _.Paragraph("Bullet eleven.");
            _.Bullets(11, 11);
            _.Paragraph("Plain twelve.");
        }),
        new("merge-lists", "The list merges that E-60 and L-20 left open, one pair at a time with a plain paragraph between: two bullets differing in indent only, a bullet before a centred paragraph, a level-1 bullet before a level-2 one, a centred paragraph before a bullet.", _ =>
        {
            _.Paragraph("Bullet one.");
            var deeper = _.Paragraph("Bullet two deeper.");
            _.Bullets(1, 2);
            deeper.LeftIndent = 72;
            _.Paragraph("Plain three.");
            _.Paragraph("Bullet four.");
            _.Bullets(4, 4);
            _.Paragraph("Centred five.", alignment: Word.WdParagraphAlignment.wdAlignParagraphCenter);
            _.Paragraph("Plain six.");
            _.Paragraph("Bullet seven.");
            _.Paragraph("Bullet eight nested.");
            _.Bullets(7, 8);
            _.Indent(8, 1);
            _.Paragraph("Plain nine.");
            _.Paragraph("Centred ten.", alignment: Word.WdParagraphAlignment.wdAlignParagraphCenter);
            _.Paragraph("Bullet eleven.");
            _.Bullets(11, 11);
            _.Paragraph("Plain twelve.");
        }),
        new("empty-paragraphs", "Above, two empty paragraphs, Below.", _ =>
        {
            _.Paragraph("Above");
            _.Empty();
            _.Empty();
            _.Paragraph("Below");
        }),
        new("bullets", "Three bulleted items.", _ =>
        {
            _.Paragraph("Alpha");
            _.Paragraph("Beta");
            _.Paragraph("Gamma");
            _.Bullets(1, 3);
        }),
        new("bullets-nested", "Three bulleted items, the second at level 2.", _ =>
        {
            _.Paragraph("Alpha");
            _.Paragraph("Beta");
            _.Paragraph("Gamma");
            _.Bullets(1, 3);
            _.Indent(2, 1);
        }),
        new("numbers", "Three numbered items.", _ =>
        {
            _.Paragraph("One");
            _.Paragraph("Two");
            _.Paragraph("Three");
            _.Numbers(1, 3);
        }),
        new("numbers-nested", "Three numbered items, the third at level 2, then a Normal paragraph.", _ =>
        {
            _.Paragraph("One");
            _.Paragraph("Two");
            _.Paragraph("Three");
            _.Numbers(1, 3);
            _.Indent(3, 1);
            _.Paragraph("After the list.");
        }),
        new("numbers-two-lists", "A numbered list, a Normal paragraph, a second numbered list.", _ =>
        {
            _.Paragraph("One");
            _.Paragraph("Two");
            _.Numbers(1, 2);
            _.Paragraph("Between the lists.");
            _.Paragraph("Alpha");
            _.Paragraph("Beta");
            _.Numbers(4, 5);
        }),
        new("numbered-heading", "A numbered Heading 1, a Heading 2 under it, Normal.", _ =>
        {
            _.Paragraph("Numbered title", Word.WdBuiltinStyle.wdStyleHeading1);
            _.Numbers(1, 1);
            _.Paragraph("Sub title", Word.WdBuiltinStyle.wdStyleHeading2);
            _.Paragraph("Body text.");
        }),
        new("tabs-breaks", "A paragraph with a tab, one with a manual line break, one with two spaces.", _ =>
        {
            _.Paragraph("Left\tright side");
            _.Paragraph("Line one line two");
            FixtureBuilder.LineBreak(_.After(2, "Line one "));
            _.Paragraph("Two  spaces here.");
        }),
        new("table-2x2", "Paragraph, a 2x2 table (One Two / Three Four) with 0.5in cell margins, paragraph.", _ =>
        {
            _.Paragraph("Before the table.");
            _.Table(2, 2, new string?[,] {{"One", "Two"}, {"Three", "Four"}}, 36);
            _.Paragraph("After the table.");
        }),
        new("table-2x2-empty", "Paragraph, an empty 2x2 table, paragraph.", _ =>
        {
            _.Paragraph("Before the table.");
            _.Table(2, 2, null, 36);
            _.Paragraph("After the table.");
        }),
        new("table-3x3", "Paragraph, a 3x3 table with one word per cell, paragraph.", _ =>
        {
            _.Paragraph("Before the table.");
            _.Table(3, 3, new string?[,] {{"a1", "b1", "c1"}, {"a2", "b2", "c2"}, {"a3", "b3", "c3"}}, 36);
            _.Paragraph("After the table.");
        }),
        new("table-merged", "A 3x3 table with (1,1)-(1,2) merged across and (2,3)-(3,3) merged down.", _ =>
        {
            _.Paragraph("Before the table.");
            var table = _.Table(3, 3, new string?[,] {{"a1", "b1", "c1"}, {"a2", "b2", "c2"}, {"a3", "b3", "c3"}}, 36);
            table.Cell(1, 1).Merge(table.Cell(1, 2));
            table.Cell(2, 3).Merge(table.Cell(3, 3));
            _.Paragraph("After the table.");
        }),
        new("table-nested", "A 2x2 table whose (1,2) holds a 2x2 inner table and a paragraph after it.", _ =>
        {
            _.Paragraph("Before the table.");
            var table = _.Table(2, 2, new string?[,] {{"a1", null}, {"a2", "b2"}}, 18);
            var cell = table.Cell(1, 2).Range;
            cell.Text = "inner after";
            var innerAnchor = _.Document.Range(cell.Start, cell.Start);
            var inner = _.Document.Tables.Add(innerAnchor, 2, 2, Word.WdDefaultTableBehavior.wdWord9TableBehavior, Word.WdAutoFitBehavior.wdAutoFitFixed);
            inner.Borders.Enable = 1;
            inner.Cell(1, 1).Range.Text = "i1";
            inner.Cell(1, 2).Range.Text = "i2";
            inner.Cell(2, 1).Range.Text = "i3";
            inner.Cell(2, 2).Range.Text = "i4";
            _.Paragraph("After the table.");
        }),
        new("table-at-start", "A 2x2 table as the first thing in the document, then a paragraph.", _ =>
        {
            var first = _.Document.Paragraphs[1].Range;
            var table = _.Document.Tables.Add(first, 2, 2, Word.WdDefaultTableBehavior.wdWord9TableBehavior, Word.WdAutoFitBehavior.wdAutoFitFixed);
            table.Borders.Enable = 1;
            table.Cell(1, 1).Range.Text = "One";
            table.Cell(1, 2).Range.Text = "Two";
            table.Cell(2, 1).Range.Text = "Three";
            table.Cell(2, 2).Range.Text = "Four";
            _.Paragraph("After the table.");
        }),
        new("table-at-end", "A paragraph, then a 2x2 table followed only by the final paragraph mark.", _ =>
        {
            _.Paragraph("Before the table.");
            _.Table(2, 2, new string?[,] {{"One", "Two"}, {"Three", "Four"}}, 36);
        }),
        new("tables-adjacent", "Two 2x2 tables with one empty paragraph between them.", _ =>
        {
            _.Paragraph("Before the tables.");
            _.Table(2, 2, new string?[,] {{"a1", "b1"}, {"a2", "b2"}}, 18);
            _.Empty();
            _.Table(2, 2, new string?[,] {{"c1", "d1"}, {"c2", "d2"}}, 18);
            _.Paragraph("After the tables.");
        }),
        new("table-multi", "A 2x2 table whose (1,1) has two paragraphs and whose (2,2) holds a two-item bullet list.", _ =>
        {
            _.Paragraph("Before the table.");
            var table = _.Table(2, 2, new string?[,] {{"first\rsecond", "b1"}, {"a2", "item one\ritem two"}}, 36);
            table.Cell(2, 2).Range.ListFormat.ApplyBulletDefault();
            _.Paragraph("After the table.");
        }),
        new("table-1x1", "Paragraph, a 1x1 table, paragraph.", _ =>
        {
            _.Paragraph("Before the table.");
            _.Table(1, 1, new string?[,] {{"Only"}}, 36);
            _.Paragraph("After the table.");
        }),
        new("list-in-cell", "A 2x2 table whose (1,1) holds a two-item bullet list.", _ =>
        {
            _.Paragraph("Before the table.");
            var table = _.Table(2, 2, new string?[,] {{"item one\ritem two", "b1"}, {"a2", "b2"}}, 36);
            table.Cell(1, 1).Range.ListFormat.ApplyBulletDefault();
            _.Paragraph("After the table.");
        }),
        new("image-inline", "Text, an inline picture between two words, text.", _ =>
        {
            _.Paragraph("Text before  after end.");
            _.InlinePicture(_.After(1, "before "));
            _.Paragraph("Second paragraph.");
        }),
        new("image-edges", "A paragraph starting with an inline picture and one ending with one.", _ =>
        {
            _.Paragraph(" starts with a picture.");
            _.InlinePicture(_.Before(1, " starts"));
            _.Paragraph("Ends with a picture ");
            _.InlinePicture(_.EndOf(2));
        }),
        new("image-floating", "Three paragraphs and a square-wrapped floating picture anchored to the second.", _ =>
        {
            _.Paragraph("First paragraph of text here.");
            _.Paragraph("Second paragraph carries the anchor of the picture.");
            _.Paragraph("Third paragraph after it.");
            _.FloatingPicture(2);
        }),
        new("text-box", "Three paragraphs and a text box (two paragraphs) anchored to the first.", _ =>
        {
            _.Paragraph("First paragraph of text here.");
            _.Paragraph("Second paragraph of text here.");
            _.Paragraph("Third paragraph of text here.");
            _.TextBox(1, "Box line one\rBox line two");
        }),
        new("shape", "A paragraph and a rectangle shape with no text anchored to it.", _ =>
        {
            _.Paragraph("Paragraph beside a shape.");
            _.Paragraph("Second paragraph.");
            _.Rectangle(1);
        }),
        new("hyperlink", "A sentence with a bold hyperlink in the middle.", _ =>
        {
            _.Paragraph("See the site today.");
            _.Hyperlink(1, "the site", "https://example.com/");
            _.Bold(1, "the site");
            _.Paragraph("Second paragraph.");
        }),
        new("hyperlink-internal", "The hyperlink sentence again, the link pointing at a bookmark in the second paragraph, so a key that follows it moves the caret there instead of opening a browser.", _ =>
        {
            _.Paragraph("See the site today.");
            _.Paragraph("Target paragraph here.");
            _.Bookmark("target", _.Find(2, "Target"));
            _.InternalHyperlink(1, "the site", "target");
            _.Bold(1, "the site");
        }),
        new("field-page", "A paragraph reading 'Page N of M' from PAGE and NUMPAGES fields, then a DATE paragraph.", _ =>
        {
            _.Paragraph("Page  of  end.");
            _.Field(_.After(1, "Page "), Word.WdFieldType.wdFieldPage);
            _.Field(_.Before(1, " end."), Word.WdFieldType.wdFieldNumPages);
            _.Paragraph("Date:  end.");
            _.Field(_.After(2, "Date: "), Word.WdFieldType.wdFieldDate, "\\@ \"d/MM/yyyy\"");
            _.Bold(2, "Date:");
        }),
        new("contents", "Three headings and an automatic table of contents.", _ =>
        {
            _.Paragraph("Alpha heading", Word.WdBuiltinStyle.wdStyleHeading1);
            _.Paragraph("Alpha body text.");
            _.Paragraph("Beta heading", Word.WdBuiltinStyle.wdStyleHeading1);
            _.Paragraph("Beta body text.");
            var first = _.Document.Paragraphs[1].Range;
            first.InsertParagraphBefore();
            var anchor = _.Document.Paragraphs[1].Range;
            _.Document.TablesOfContents.Add(anchor, true, 1, 3);
        }),
        new("page-break", "Two paragraphs with a page break between them.", _ =>
        {
            _.Paragraph("Before the break.");
            _.Paragraph("After the break.");
            FixtureBuilder.PageBreak(_.StartOf(2));
        }),
        new("section-break", "Two sections, next-page break, the second landscape.", _ =>
        {
            _.Paragraph("Section one text.");
            _.Paragraph("Section two text.");
            FixtureBuilder.SectionBreak(_.StartOf(2));
            _.Document.Sections[2].PageSetup.Orientation = Word.WdOrientation.wdOrientLandscape;
        }),
        new("header-footer", "A body paragraph, a header with text and a footer with a PAGE field.", _ =>
        {
            _.Paragraph("Body paragraph.");
            _.Paragraph("Second body paragraph.");
            _.Header("Header text");
            var footer = _.Document.Sections[1].Footers[Word.WdHeaderFooterIndex.wdHeaderFooterPrimary].Range;
            footer.Text = "Page ";
            footer.SetRange(footer.End - 1, footer.End - 1);
            _.Document.Fields.Add(footer, Word.WdFieldType.wdFieldPage, Type.Missing, false);
        }),
        new("footnote", "A sentence with a footnote reference after one word, then a second paragraph; an endnote on it.", _ =>
        {
            _.Paragraph("Text more words.");
            _.Footnote(_.After(1, "Text"), "Note text.");
            _.Paragraph("Second with an end note.");
            _.Endnote(_.EndOf(2), "End note text.");
        }),
        new("comment", "The fox sentence with a comment on 'brown fox'.", _ =>
        {
            _.Paragraph(fox);
            _.Comment(_.Find(1, "brown fox"), "A comment.");
            _.Paragraph("Second paragraph.");
        }),
        new("bookmark", "The fox sentence with a bookmark on 'brown fox'.", _ =>
        {
            _.Paragraph(fox);
            _.Bookmark("mark", _.Find(1, "brown fox"));
            _.Paragraph("Second paragraph.");
        }),
        new("content-control", "Rich text, plain text, checkbox and dropdown content controls, one per paragraph.", _ =>
        {
            _.Paragraph("Rich: content here end.");
            _.ContentControl(_.Find(1, "content here"));
            _.Paragraph("Plain: plain here end.");
            _.ContentControl(_.Find(2, "plain here"), Word.WdContentControlType.wdContentControlText);
            _.Paragraph("Check:  end.");
            _.ContentControl(_.After(3, "Check: "), Word.WdContentControlType.wdContentControlCheckBox);
            _.Paragraph("Pick: choice end.");
            var dropdown = _.ContentControl(_.Find(4, "choice"), Word.WdContentControlType.wdContentControlDropdownList);
            dropdown.DropdownListEntries.Add("First", "First");
            dropdown.DropdownListEntries.Add("Second", "Second");
        }),
        new("tracked", "A sentence with another author's tracked insertion and deletion.", _ =>
        {
            _.Paragraph("Kept gone text stays.");
            _.Tracked(builder =>
            {
                builder.Find(1, "gone ").Delete();
                builder.After(1, "Kept ").InsertAfter("added ");
            });
            _.Paragraph("Second paragraph.");
        }),
        new("special", "Non-breaking space, soft hyphen, hidden text and an em dash.", _ =>
        {
            _.Paragraph("a b soft­hyphen hidden words dash—end.");
            _.Find(1, "hidden").Font.Hidden = 1;
        }),
        new("columns", "Three paragraphs in a two-column section.", _ =>
        {
            _.Paragraph("Column text one with enough words to fill a few lines of a narrow column so the break lands inside it.");
            _.Paragraph("Column text two with enough words to fill a few more lines of a narrow column and run on further.");
            _.Paragraph("Column text three closes the section with a final short line.");
            _.Columns(2);
        }),
        new("three-pages", "Thirty paragraphs with page breaks after the tenth and twentieth.", _ =>
        {
            for (var i = 1; i <= 30; i++)
            {
                _.Paragraph($"Paragraph {i} of thirty on one of three pages.");
            }

            FixtureBuilder.PageBreak(_.StartOf(11));
            FixtureBuilder.PageBreak(_.StartOf(22));
        }),
        new("indents", "Paragraphs with a left indent, a first-line indent, a hanging indent and space after.", _ =>
        {
            var left = _.Paragraph("Left indented paragraph.");
            left.LeftIndent = 36;
            var first = _.Paragraph("First line indented paragraph.");
            first.FirstLineIndent = 36;
            var hanging = _.Paragraph("Hanging indented paragraph.");
            hanging.LeftIndent = 36;
            hanging.FirstLineIndent = -36;
            var spaced = _.Paragraph("Space after paragraph.");
            spaced.SpaceAfter = 24;
            _.Paragraph("Plain closing paragraph.");
        }),
        new("sizes", "One paragraph per starting font size for the size ladders.", _ =>
        {
            float[] sizes = [1, 5, 7, 8, 9, 10, 10.5f, 11, 12, 13, 14, 16, 18, 20, 22, 24, 26, 28, 36, 48, 72, 80, 100];
            for (var i = 0; i < sizes.Length; i++)
            {
                var paragraph = _.Paragraph($"Size {sizes[i].ToString(CultureInfo.InvariantCulture)}");
                paragraph.Range.Font.Size = sizes[i];
            }
        })
    ];

    public static Fixture Get(string name) =>
        All.FirstOrDefault(_ => _.Name == name) ?? throw new ArgumentException($"Unknown fixture '{name}'.");

    public static void BuildAll(bool force)
    {
        Directory.CreateDirectory(Paths.Fixtures);
        EnsurePicture();
        var pending = All.Where(_ => force || !File.Exists(Path.Combine(Paths.Fixtures, $"{_.Name}.docx"))).ToList();
        if (pending.Count == 0)
        {
            Log.Info("All fixtures present.");
            return;
        }

        Word.Application? application = null;
        MessageFilter? filter = null;
        try
        {
            filter = MessageFilter.Register(null, TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(5));
            // Visible: shapes' text frames (and some layout-dependent members) fail in a hidden window.
            application = new Word.Application
            {
                Visible = true,
                DisplayAlerts = Word.WdAlertLevel.wdAlertsNone
            };
            var previousName = application.UserName;
            application.UserName = "Probe";
            foreach (var fixture in pending)
            {
                Build(application, fixture);
            }

            application.UserName = previousName;
        }
        finally
        {
            if (application != null)
            {
                Com.Try(() =>
                {
                    while (application.Documents.Count > 0)
                    {
                        application.Documents[1].Close(Word.WdSaveOptions.wdDoNotSaveChanges);
                    }

                    application.NormalTemplate.Saved = true;
                    application.Quit(Word.WdSaveOptions.wdDoNotSaveChanges);
                    return true;
                });
            }

            Com.Release(ref application);
            filter?.Dispose();
        }
    }

    static void Build(Word.Application application, Fixture fixture)
    {
        Log.Info($"Fixture {fixture.Name}");
        var path = Path.Combine(Paths.Fixtures, $"{fixture.Name}.docx");
        Directory.CreateDirectory(Paths.Temp);
        var blank = Path.Combine(Paths.Temp, "blank.docx");
        File.Copy(Paths.BlankDocument, blank, overwrite: true);
        var document = application.Documents.Add(blank, false, Word.WdNewDocumentType.wdNewBlankDocument, true);
        try
        {
            var builder = new FixtureBuilder(document);
            fixture.Build(builder);
            if (File.Exists(path))
            {
                File.Delete(path);
            }

            document.SaveAs2(path, Word.WdSaveFormat.wdFormatXMLDocument);
        }
        finally
        {
            document.Close(Word.WdSaveOptions.wdDoNotSaveChanges);
        }

        FixtureNormalizer.Normalize(path);
    }

    static void EnsurePicture()
    {
        var path = FixtureBuilder.PicturePath;
        if (File.Exists(path))
        {
            return;
        }

        // A 96x96 flat colour PNG written by hand: signature, IHDR, one IDAT of a stored deflate stream, IEND.
        File.WriteAllBytes(path, FlatPng.Create(96, 96, 0x2E, 0x7D, 0xC9));
    }
}
