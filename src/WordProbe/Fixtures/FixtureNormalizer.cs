// Makes a saved fixture byte-stable: Word's revision ids, paragraph ids, document ids, proofing marks and
// rendering hints come out, authors and dates are fixed, and the package is rezipped in a fixed order
// with fixed timestamps. The content Word wrote is otherwise untouched.
using System.IO.Compression;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

static class FixtureNormalizer
{
    static readonly string[] revisionAttributes =
    [
        "rsidR", "rsidRPr", "rsidRDefault", "rsidP", "rsidSect", "rsidDel", "rsidTr"
    ];

    public static void Normalize(string path)
    {
        using (var package = WordprocessingDocument.Open(path, true))
        {
            var main = package.MainDocumentPart!;
            foreach (var part in Parts(main))
            {
                var root = part.RootElement;
                if (root == null)
                {
                    continue;
                }

                Strip(root);
                root.Save();
            }

            var settings = main.DocumentSettingsPart?.Settings;
            if (settings != null)
            {
                settings.RemoveAllChildren<Rsids>();
                foreach (var child in settings.ChildElements.Where(_ => _.LocalName is "docId" or "chartTrackingRefBased").ToList())
                {
                    child.Remove();
                }

                settings.Save();
            }

            var core = package.PackageProperties;
            core.Creator = "Probe";
            core.LastModifiedBy = "Probe";
            core.Created = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            core.Modified = core.Created;
            core.Revision = "1";
            core.Title = null;
        }

        Rezip(path);
    }

    static IEnumerable<OpenXmlPart> Parts(MainDocumentPart main)
    {
        yield return main;
        foreach (var part in main.HeaderParts)
        {
            yield return part;
        }

        foreach (var part in main.FooterParts)
        {
            yield return part;
        }

        if (main.FootnotesPart != null)
        {
            yield return main.FootnotesPart;
        }

        if (main.EndnotesPart != null)
        {
            yield return main.EndnotesPart;
        }

        if (main.WordprocessingCommentsPart != null)
        {
            yield return main.WordprocessingCommentsPart;
        }

        if (main.StyleDefinitionsPart != null)
        {
            yield return main.StyleDefinitionsPart;
        }

        if (main.NumberingDefinitionsPart != null)
        {
            yield return main.NumberingDefinitionsPart;
        }
    }

    static void Strip(OpenXmlElement root)
    {
        foreach (var element in root.Descendants().ToList())
        {
            foreach (var name in revisionAttributes)
            {
                if (element.GetAttributes().Any(_ => _.LocalName == name))
                {
                    element.RemoveAttribute(name, "http://schemas.openxmlformats.org/wordprocessingml/2006/main");
                }
            }

            foreach (var attribute in element.GetAttributes().Where(_ => _.LocalName is "paraId" or "textId").ToList())
            {
                element.RemoveAttribute(attribute.LocalName, attribute.NamespaceUri);
            }

            switch (element)
            {
                case ProofError:
                case LastRenderedPageBreak:
                    element.Remove();
                    break;
                case BookmarkStart {Name.Value: "_GoBack"} start:
                {
                    var id = start.Id?.Value;
                    foreach (var end in root.Descendants<BookmarkEnd>().Where(_ => _.Id?.Value == id).ToList())
                    {
                        end.Remove();
                    }

                    start.Remove();
                    break;
                }
                case Inserted inserted:
                    FixRevision(inserted);
                    break;
                case Deleted deleted:
                    FixRevision(deleted);
                    break;
                case InsertedRun insertedRun:
                    FixRevision(insertedRun);
                    break;
                case DeletedRun deletedRun:
                    FixRevision(deletedRun);
                    break;
                case Comment comment:
                    comment.Date = new(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
                    comment.Initials = "OT";
                    break;
            }
        }
    }

    static void FixRevision(OpenXmlElement element)
    {
        var date = element.GetAttributes().FirstOrDefault(_ => _.LocalName == "date");
        if (date.LocalName != null)
        {
            element.SetAttribute(new(date.LocalName, date.NamespaceUri, "2026-01-01T00:00:00Z"));
        }
    }

    static void Rezip(string path)
    {
        var entries = new SortedDictionary<string, byte[]>(StringComparer.Ordinal);
        using (var archive = ZipFile.OpenRead(path))
        {
            foreach (var entry in archive.Entries)
            {
                using var stream = entry.Open();
                using var memory = new MemoryStream();
                stream.CopyTo(memory);
                entries[entry.FullName] = memory.ToArray();
            }
        }

        var temporary = path + ".tmp";
        using (var output = new FileStream(temporary, FileMode.Create))
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create))
        {
            // [Content_Types].xml first, as Word writes it, then the rest in name order.
            foreach (var (name, bytes) in entries.OrderBy(_ => _.Key == "[Content_Types].xml" ? 0 : 1).ThenBy(_ => _.Key, StringComparer.Ordinal))
            {
                var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
                entry.LastWriteTime = new(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);
                using var stream = entry.Open();
                stream.Write(bytes);
            }
        }

        // Word (or an indexer) can hold the file for a moment after Close; retry before giving up.
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                File.Move(temporary, path, overwrite: true);
                return;
            }
            catch (IOException) when (attempt < 10)
            {
                Thread.Sleep(300);
            }
            catch (UnauthorizedAccessException) when (attempt < 10)
            {
                Thread.Sleep(300);
            }
        }
    }
}
