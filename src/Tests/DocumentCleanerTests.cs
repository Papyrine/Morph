/// <summary>
/// Covers <see cref="DocumentCleaner"/>. The parts it removes are unreachable from
/// <c>word/document.xml</c> content, so the load-bearing assertion in most of these tests is not
/// "the part is gone" but "the exported HTML is byte-identical afterwards" — if a removal ever
/// starts changing what Morph renders, that is the assertion that catches it.
/// Every removable part is grafted onto a clean corpus copy rather than borrowed from a corpus
/// document that happens to carry one. That is not incidental: <see cref="InputDocxUnusedPartsTests"/>
/// forbids the corpus from carrying any of them, so a fixture that borrowed would be asserting
/// against state another test guarantees cannot exist.
/// </summary>
public class DocumentCleanerTests
{
    /// <summary>Any corpus input serves — the fixture supplies the parts under test itself.</summary>
    const string fixtureScenario = "cover-letters/08";

    [Test]
    public async Task RemovesThumbnailPartRelationshipAndContentType()
    {
        using var fixture = Fixture.Loaded();

        await Assert.That(DocumentCleaner.Find(fixture.Path))
            .IsEqualTo(DocumentParts.Thumbnail | DocumentParts.Glossary | DocumentParts.CustomXml);

        var removed = DocumentCleaner.Remove(fixture.Path, DocumentParts.Thumbnail);

        await Assert.That(removed).IsEqualTo(DocumentParts.Thumbnail);
        await Assert.That(fixture.PartNames()).DoesNotContain("docProps/thumbnail.emf");
        await Assert.That(fixture.Text("_rels/.rels")).DoesNotContain("metadata/thumbnail");
        await Assert.That(fixture.Text("[Content_Types].xml")).DoesNotContain("\"emf\"");
    }

    [Test]
    public async Task RemovesGlossaryAndCustomXmlWithTheirRelationships()
    {
        using var fixture = Fixture.Loaded();

        var removed = DocumentCleaner.Remove(fixture.Path);

        await Assert.That(removed).IsEqualTo(DocumentParts.Thumbnail | DocumentParts.Glossary | DocumentParts.CustomXml);
        await Assert.That(fixture.PartNames().Where(_ => _.StartsWith("word/glossary/"))).IsEmpty();
        await Assert.That(fixture.PartNames().Where(_ => _.StartsWith("customXml/"))).IsEmpty();

        // the relationships that pointed at them must go too, or the package has dangling targets.
        // The customXml relationship is declared as "../customXml/item1.xml", so dropping it also
        // exercises the parent-segment path resolution.
        var relationships = fixture.Text("word/_rels/document.xml.rels");
        await Assert.That(relationships).DoesNotContain("glossaryDocument");
        await Assert.That(relationships).DoesNotContain("customXml");

        // as must their content-type Overrides
        var contentTypes = fixture.Text("[Content_Types].xml");
        await Assert.That(contentTypes).DoesNotContain("/word/glossary/");
        await Assert.That(contentTypes).DoesNotContain("/customXml/");
    }

    [Test]
    public async Task RemovesOnlyTheSelectedParts()
    {
        using var fixture = Fixture.Loaded();

        var removed = DocumentCleaner.Remove(fixture.Path, DocumentParts.Glossary);

        await Assert.That(removed).IsEqualTo(DocumentParts.Glossary);
        await Assert.That(fixture.PartNames().Where(_ => _.StartsWith("word/glossary/"))).IsEmpty();
        await Assert.That(fixture.PartNames().Where(_ => _.StartsWith("customXml/"))).IsNotEmpty();
        await Assert.That(fixture.PartNames()).Contains("docProps/thumbnail.emf");
        await Assert.That(fixture.Text("word/_rels/document.xml.rels")).Contains("customXml");
    }

    [Test]
    public async Task ReportsNothingForAPackageThatCarriesNone()
    {
        using var fixture = Fixture.Clean();

        await Assert.That(DocumentCleaner.Find(fixture.Path)).IsEqualTo(DocumentParts.None);
    }

    [Test]
    public async Task LeavesAPackageWithNothingToRemoveByteIdentical()
    {
        using var fixture = Fixture.Clean();
        var original = await File.ReadAllBytesAsync(fixture.Path);

        var removed = DocumentCleaner.Remove(fixture.Path);

        await Assert.That(removed).IsEqualTo(DocumentParts.None);
        await Assert.That(await File.ReadAllBytesAsync(fixture.Path)).IsEquivalentTo(original);
    }

    [Test]
    public async Task PreservesRenderedOutput()
    {
        using var fixture = Fixture.Loaded();

        var before = DocumentConverter.ConvertToHtml(fixture.Path);
        DocumentCleaner.Remove(fixture.Path);
        var after = DocumentConverter.ConvertToHtml(fixture.Path);

        await Assert.That(after).IsEqualTo(before);
    }

    /// <summary>
    /// A throwaway copy of <see cref="fixtureScenario"/> on disk, with helpers to read back the
    /// parts of the rewritten package.
    /// </summary>
    sealed class Fixture : IDisposable
    {
        const string relationshipTypes = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

        public required string Path { get; init; }

        public void Dispose() =>
            File.Delete(Path);

        /// <summary>A copy exactly as it sits in the corpus — carrying no removable part at all.</summary>
        public static Fixture Clean()
        {
            var source = System.IO.Path.Combine(ProjectFiles.ProjectDirectory, "Inputs", fixtureScenario, "input.docx");
            var target = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"morph-cleaner-{Guid.NewGuid():N}.docx");
            File.Copy(source, target);
            return new()
            {
                Path = target
            };
        }

        /// <summary>
        /// A copy carrying one of every removable part, grafted in the shape Word writes them:
        /// each part, the relationship that reaches it, and its content-type declaration.
        /// <c>word/people.xml</c> is left out — nothing in the corpus has ever carried one, so
        /// there is no authentic shape to copy.
        /// </summary>
        public static Fixture Loaded()
        {
            var fixture = Clean();
            using var package = ZipFile.Open(fixture.Path, ZipArchiveMode.Update);

            AddThumbnail(package);
            AddGlossary(package);
            AddCustomXml(package);

            return fixture;
        }

        static void AddThumbnail(ZipArchive package)
        {
            Write(package, "docProps/thumbnail.emf", "not really an EMF, but nothing reads it");

            Relate(package, "_rels/.rels", "rIdThumbnail",
                "http://schemas.openxmlformats.org/package/2006/relationships/metadata/thumbnail",
                "/docProps/thumbnail.emf");

            Declare(package, new("Default",
                new XAttribute("Extension", "emf"),
                new XAttribute("ContentType", "image/x-emf")));
        }

        static void AddGlossary(ZipArchive package)
        {
            Write(package, "word/glossary/document.xml",
                """<?xml version="1.0" encoding="UTF-8" standalone="yes"?><w:glossaryDocument xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:docParts /></w:glossaryDocument>""");

            Relate(package, "word/_rels/document.xml.rels", "rIdGlossary",
                $"{relationshipTypes}/glossaryDocument",
                "glossary/document.xml");

            Declare(package, new("Override",
                new XAttribute("PartName", "/word/glossary/document.xml"),
                new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.wordprocessingml.document.glossary+xml")));
        }

        static void AddCustomXml(ZipArchive package)
        {
            Write(package, "customXml/item1.xml", """<?xml version="1.0" encoding="UTF-8" standalone="yes"?><root><value>bound</value></root>""");
            Write(package, "customXml/itemProps1.xml",
                """<?xml version="1.0" encoding="UTF-8" standalone="yes"?><ds:datastoreItem xmlns:ds="http://schemas.openxmlformats.org/officeDocument/2006/customXml" ds:itemID="{6C3C8BC8-F283-45AE-878A-BAB7291924A1}"><ds:schemaRefs /></ds:datastoreItem>""");

            Write(package, "customXml/_rels/item1.xml.rels",
                $"""<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rIdProps" Type="{relationshipTypes}/customXmlProps" Target="itemProps1.xml" /></Relationships>""");

            // declared with a parent segment, exactly as Word writes it from word/
            Relate(package, "word/_rels/document.xml.rels", "rIdCustomXml",
                $"{relationshipTypes}/customXml",
                "../customXml/item1.xml");

            Declare(package, new("Override",
                new XAttribute("PartName", "/customXml/itemProps1.xml"),
                new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.customXmlProperties+xml")));
        }

        static void Write(ZipArchive package, string partName, string content)
        {
            using var stream = package.CreateEntry(partName).Open();
            stream.Write(Encoding.UTF8.GetBytes(content));
        }

        static void Relate(ZipArchive package, string relsPartName, string id, string type, string target) =>
            Edit(package, relsPartName, document =>
                document.Root!.Add(
                    new XElement(
                        document.Root.Name.Namespace + "Relationship",
                        new XAttribute("Id", id),
                        new XAttribute("Type", type),
                        new XAttribute("Target", target))));

        static void Declare(ZipArchive package, XElement declaration) =>
            Edit(package, "[Content_Types].xml", document =>
                document.Root!.AddFirst(
                    new XElement(document.Root.Name.Namespace + declaration.Name.LocalName, declaration.Attributes())));

        static void Edit(ZipArchive package, string partName, Action<XDocument> edit)
        {
            var entry = package.GetEntry(partName)!;

            XDocument document;
            using (var content = entry.Open())
            {
                document = XDocument.Load(content);
            }

            edit(document);

            using var replacement = entry.Open();
            replacement.SetLength(0);
            document.Save(replacement);
        }

        public IReadOnlyList<string> PartNames()
        {
            using var package = ZipFile.OpenRead(Path);
            return package.Entries.Select(_ => _.FullName).ToList();
        }

        public string Text(string partName)
        {
            using var package = ZipFile.OpenRead(Path);
            using var content = package.GetEntry(partName)!.Open();
            using var reader = new StreamReader(content);
            return reader.ReadToEnd();
        }
    }
}
