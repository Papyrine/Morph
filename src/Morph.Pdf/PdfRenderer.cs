/// <summary>
/// Renders a parsed document to a PDF byte array using PdfSharp. Shared entry point for the
/// DOCX → PDF and HTML → PDF public converters. Output is made byte-reproducible (see
/// <see cref="MakeDeterministic"/> / <see cref="Normalize"/>) so it can be snapshot-tested.
/// </summary>
static class PdfRenderer
{
    public static byte[] Render(ParsedDocument document, PdfExportOptions? options)
    {
        options ??= new();

        var totalPageCount = CountPagesIfRequired(document, options);

        var context = new PdfRenderContext(
            document.PageSettings,
            document.Compatibility,
            options.FontWidthScale,
            options.FontFallback,
            options.FontDirectory)
        {
            TotalPageCount = totalPageCount
        };

        var renderer = new PdfPageRenderer(context)
        {
            OnWarning = options.OnWarning,
            Pages = options.Pages,
            RasterizeWordArt = options.RasterizeWordArt
        };
        renderer.RenderDocument(document);

        MakeDeterministic(context.Document);

        if (options.Pages is { } range)
        {
            TrimPages(context.Document, range);
        }

        using var stream = new MemoryStream();
        context.Document.Save(stream, closeStream: false);
        context.DisposeImages();
        return Normalize(stream.ToArray());
    }

    // A NUMPAGES/SECTIONPAGES field needs the final page total, which is only known after the
    // document is laid out. Build a throwaway document first to count pages so the real render can
    // substitute the total. Documents without such a field render once. Any page range is applied
    // only to the real render, so the count reflects the whole document (matching Word's NUMPAGES).
    static int CountPagesIfRequired(ParsedDocument document, PdfExportOptions options)
    {
        if (!document.RequiresTotalPageCount)
        {
            return 0;
        }

        var context = new PdfRenderContext(
            document.PageSettings,
            document.Compatibility,
            options.FontWidthScale,
            options.FontFallback,
            options.FontDirectory);
        // No OnWarning here — the real render reports warnings; forwarding them from the counting
        // pass too would emit every warning twice. RasterizeWordArt must match the real render so
        // WordArt reserves the same height in both passes and pagination stays consistent.
        var renderer = new PdfPageRenderer(context)
        {
            RasterizeWordArt = options.RasterizeWordArt
        };
        var total = renderer.RenderDocument(document);
        context.DisposeImages();
        return total;
    }

    /// <summary>
    /// Drops any page outside <paramref name="range"/> (1-based, inclusive). The page numbers in
    /// the PDF reset to 1..N over the kept pages.
    /// </summary>
    static void TrimPages(PdfDocument document, PageRange range)
    {
        var total = document.PageCount;
        var keepFrom = Math.Max(1, range.Start);
        var keepTo = Math.Min(total, range.End);

        for (var index = total; index >= 1; index--)
        {
            if (index < keepFrom || index > keepTo)
            {
                document.Pages.RemoveAt(index - 1);
            }
        }
    }

    // A PDF's CreationDate/ModDate (stamped with DateTime.Now) and trailer /ID (a fresh GUID) vary
    // per save, so identical input produces different bytes. Pin them to fixed values.
    static readonly DateTime fixedTimestamp = new(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    const string fixedDocumentId = "MorphDeterminist";

    static void MakeDeterministic(PdfDocument document)
    {
        document.Info.CreationDate = fixedTimestamp;
        document.Info.ModificationDate = fixedTimestamp;
        document.Internals.FirstDocumentID = fixedDocumentId;
        document.Internals.SecondDocumentID = fixedDocumentId;
    }

    // PdfSharp prefixes each embedded font subset with a random 6-uppercase-letter tag (e.g.
    // "YZGTLG+Aptos") generated from a GUID, and writes random XMP DocumentID/InstanceID UUIDs —
    // neither has an override hook, and they're the last sources of per-save variance. Remap subset
    // tags to deterministic ones (AAAAAA, AAAAAB, … by first appearance) and pin the UUIDs. Both
    // patterns are tightly anchored so the binary (FlateDecode) streams are never touched. Every
    // replacement is the same length as what it replaces, so the buffer is patched in place —
    // this used to round-trip the whole PDF through a Latin1 string and two regex passes
    // (~5× the file size in transient allocations).
    static readonly byte[] baseFontMarker = "/BaseFont/"u8.ToArray();
    static readonly byte[] fontNameMarker = "/FontName/"u8.ToArray();
    static readonly byte[] uuidMarker = "uuid:"u8.ToArray();
    static readonly byte[] fixedUuid = "uuid:00000000-0000-0000-0000-000000000000"u8.ToArray();

    static byte[] Normalize(byte[] pdf)
    {
        PatchSubsetTags(pdf);
        PatchXmpUuids(pdf);
        return pdf;
    }

    static void PatchSubsetTags(byte[] pdf)
    {
        var map = new Dictionary<string, string>();
        var index = 0;
        while (index < pdf.Length)
        {
            var marker = MatchesAt(pdf, index, baseFontMarker) ? baseFontMarker
                : MatchesAt(pdf, index, fontNameMarker) ? fontNameMarker
                : null;
            if (marker == null)
            {
                index++;
                continue;
            }

            // A match needs six uppercase letters and a '+' right after the marker; anything
            // else means the regex this replaces would have kept scanning from the next byte.
            var tagStart = index + marker.Length;
            if (tagStart + 7 > pdf.Length || pdf[tagStart + 6] != (byte) '+' || !IsUppercaseTag(pdf, tagStart))
            {
                index++;
                continue;
            }

            var original = Encoding.ASCII.GetString(pdf, tagStart, 6);
            if (!map.TryGetValue(original, out var replacement))
            {
                replacement = DeterministicTag(map.Count);
                map[original] = replacement;
            }

            for (var offset = 0; offset < 6; offset++)
            {
                pdf[tagStart + offset] = (byte) replacement[offset];
            }

            index = tagStart + 7;
        }
    }

    static bool IsUppercaseTag(byte[] pdf, int start)
    {
        for (var offset = 0; offset < 6; offset++)
        {
            if (pdf[start + offset] is < (byte) 'A' or > (byte) 'Z')
            {
                return false;
            }
        }

        return true;
    }

    static void PatchXmpUuids(byte[] pdf)
    {
        var index = 0;
        while (index <= pdf.Length - fixedUuid.Length)
        {
            if (!MatchesAt(pdf, index, uuidMarker) || !IsUuidBody(pdf, index + uuidMarker.Length))
            {
                index++;
                continue;
            }

            fixedUuid.CopyTo(pdf, index);
            index += fixedUuid.Length;
        }
    }

    // 8-4-4-4-12 hex groups separated by dashes, immediately after "uuid:".
    static bool IsUuidBody(byte[] pdf, int start)
    {
        for (var offset = 0; offset < 36; offset++)
        {
            var value = pdf[start + offset];
            if (offset is 8 or 13 or 18 or 23)
            {
                if (value != (byte) '-')
                {
                    return false;
                }
            }
            else if (!char.IsAsciiHexDigit((char) value))
            {
                return false;
            }
        }

        return true;
    }

    static bool MatchesAt(byte[] pdf, int index, byte[] pattern)
    {
        if (index + pattern.Length > pdf.Length)
        {
            return false;
        }

        for (var offset = 0; offset < pattern.Length; offset++)
        {
            if (pdf[index + offset] != pattern[offset])
            {
                return false;
            }
        }

        return true;
    }

    static string DeterministicTag(int index)
    {
        var tag = new char[6];
        for (var position = 5; position >= 0; position--)
        {
            tag[position] = (char) ('A' + index % 26);
            index /= 26;
        }

        return new(tag);
    }
}
