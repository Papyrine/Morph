/// <summary>
/// Builds the synthetic DOCX inputs for the render/parse benchmarks. The scenario corpus under
/// src/Tests/Inputs is nearly all single-page, so per-page costs (header/watermark image decode,
/// per-fragment font resolution) would be invisible there; these documents are multi-page by
/// construction. Generated in-memory on first use — nothing is written to disk and nothing is
/// picked up by the scenario test suite.
/// </summary>
static class BenchmarkDocs
{
    static byte[]? headerLogoRaster;
    static byte[]? headerLogoSvg;
    static byte[]? pictureWatermark;
    static byte[]? repeatedImage;
    static byte[]? toc;

    /// <summary>12 pages of text with a raster PNG logo in the default header.</summary>
    public static byte[] HeaderLogoRaster => headerLogoRaster ??= BuildHeaderLogoDoc(useSvg: false);

    /// <summary>12 pages of text with an SVG logo (PNG fallback) in the default header.</summary>
    public static byte[] HeaderLogoSvg => headerLogoSvg ??= BuildHeaderLogoDoc(useSvg: true);

    /// <summary>12 pages of text with a VML picture watermark (gain/blacklevel washout).</summary>
    public static byte[] PictureWatermark => pictureWatermark ??= BuildPictureWatermarkDoc();

    /// <summary>6 pages, 10 inline images per page, all referencing the same image part.</summary>
    public static byte[] RepeatedImage => repeatedImage ??= BuildRepeatedImageDoc();

    /// <summary>
    /// TOC-shaped document: 300 hyperlinked entries with dot-leader right tabs plus 150
    /// default-tab lines, against a styles.xml carrying 200 character styles. Exercises the
    /// per-run style scan, hyperlink relationship resolution and tab measurement paths.
    /// </summary>
    public static byte[] Toc => toc ??= BuildTocDoc();

    const string contentTypesPath = "[Content_Types].xml";
    const string relsNs = "http://schemas.openxmlformats.org/package/2006/relationships";
    const string imageRelType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/image";
    const string documentNamespaces =
        "xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\" " +
        "xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\" " +
        "xmlns:wp=\"http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing\" " +
        "xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\" " +
        "xmlns:pic=\"http://schemas.openxmlformats.org/drawingml/2006/picture\" " +
        "xmlns:asvg=\"http://schemas.microsoft.com/office/drawing/2016/SVG/main\" " +
        "xmlns:v=\"urn:schemas-microsoft-com:vml\" " +
        "xmlns:o=\"urn:schemas-microsoft-com:office:office\" " +
        "xmlns:w10=\"urn:schemas-microsoft-com:office:word\"";

    const string packageRels =
        $"""<?xml version="1.0" encoding="utf-8"?><Relationships xmlns="{relsNs}"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="/word/document.xml"/></Relationships>""";

    static string ContentTypes(bool header, bool styles, bool svg)
    {
        var builder = new StringBuilder();
        builder.Append("""<?xml version="1.0" encoding="utf-8"?><Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">""");
        builder.Append("""<Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>""");
        builder.Append("""<Default Extension="png" ContentType="image/png"/>""");
        if (svg)
        {
            builder.Append("""<Default Extension="svg" ContentType="image/svg+xml"/>""");
        }

        builder.Append("""<Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>""");
        if (header)
        {
            builder.Append("""<Override PartName="/word/header1.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.header+xml"/>""");
        }

        if (styles)
        {
            builder.Append("""<Override PartName="/word/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml"/>""");
        }

        builder.Append("</Types>");
        return builder.ToString();
    }

    static byte[] Zip(List<(string Path, byte[] Content)> parts)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (path, content) in parts)
            {
                var entry = archive.CreateEntry(path, CompressionLevel.Fastest);
                using var entryStream = entry.Open();
                entryStream.Write(content);
            }
        }

        return stream.ToArray();
    }

    static byte[] Utf8(string text) => Encoding.UTF8.GetBytes(text);

    // ---- body text ----

    static readonly string[] wordBank =
    [
        "revenue", "throughput", "allocation", "pipeline", "rendering", "measurement", "baseline",
        "layout", "cadence", "quarterly", "projection", "typography", "resolution", "document",
        "watermark", "benchmark", "converter", "paragraph", "fragment", "resolver"
    ];

    static string Sentence(int seed, int words)
    {
        var builder = new StringBuilder(words * 10);
        for (var i = 0; i < words; i++)
        {
            if (i > 0)
            {
                builder.Append(' ');
            }

            builder.Append(wordBank[(seed * 7 + i * 3) % wordBank.Length]);
        }

        builder[0] = char.ToUpperInvariant(builder[0]);
        return builder.Append('.').ToString();
    }

    static void AppendTextPages(StringBuilder body, int pages, int paragraphsPerPage)
    {
        for (var page = 0; page < pages; page++)
        {
            for (var index = 0; index < paragraphsPerPage; index++)
            {
                var seed = page * paragraphsPerPage + index;
                body.Append("<w:p>");
                if (index % 3 == 0)
                {
                    body.Append($"""<w:r><w:rPr><w:b/><w:rFonts w:ascii="Aptos" w:hAnsi="Aptos"/></w:rPr><w:t xml:space="preserve">Item {seed}: </w:t></w:r>""");
                }

                body.Append($"""<w:r><w:t xml:space="preserve">{Sentence(seed, 12 + seed % 5)}</w:t></w:r></w:p>""");
            }

            if (page < pages - 1)
            {
                body.Append("""<w:p><w:r><w:br w:type="page"/></w:r></w:p>""");
            }
        }
    }

    static string SectionProperties(string? headerRelId)
    {
        var headerReference = headerRelId == null
            ? ""
            : $"""<w:headerReference w:type="default" r:id="{headerRelId}"/>""";
        return $"""<w:sectPr>{headerReference}<w:pgSz w:w="12240" w:h="15840"/><w:pgMar w:top="1440" w:right="1440" w:bottom="1440" w:left="1440" w:header="720" w:footer="720" w:gutter="0"/></w:sectPr>""";
    }

    // ---- drawings ----

    static string InlineImage(int id, string relId, long cx, long cy, string? svgRelId = null)
    {
        var svgExtension = svgRelId == null
            ? ""
            : $$"""<a:extLst><a:ext uri="{96DAC541-7B7A-43D3-8B79-37D633B846F1}"><asvg:svgBlip r:embed="{{svgRelId}}"/></a:ext></a:extLst>""";
        return
            $"""<w:drawing><wp:inline distT="0" distB="0" distL="0" distR="0"><wp:extent cx="{cx}" cy="{cy}"/><wp:effectExtent l="0" t="0" r="0" b="0"/><wp:docPr id="{id}" name="Image {id}"/><wp:cNvGraphicFramePr><a:graphicFrameLocks noChangeAspect="1"/></wp:cNvGraphicFramePr>""" +
            $"""<a:graphic><a:graphicData uri="http://schemas.openxmlformats.org/drawingml/2006/picture"><pic:pic><pic:nvPicPr><pic:cNvPr id="0" name="image{id}"/><pic:cNvPicPr/></pic:nvPicPr>""" +
            $"""<pic:blipFill><a:blip r:embed="{relId}">{svgExtension}</a:blip><a:stretch><a:fillRect/></a:stretch></pic:blipFill>""" +
            $"""<pic:spPr><a:xfrm><a:off x="0" y="0"/><a:ext cx="{cx}" cy="{cy}"/></a:xfrm><a:prstGeom prst="rect"><a:avLst/></a:prstGeom></pic:spPr></pic:pic></a:graphicData></a:graphic></wp:inline></w:drawing>""";
    }

    // VML picture watermark matching the shape Word emits (and DocumentParser.ExtractWatermarks
    // expects): a v:shape whose id carries the WordPictureWatermark marker, with washout
    // gain/blacklevel on v:imagedata. Boilerplate copied from a Word-authored document.
    static string WatermarkPict(string relId) =>
        """<w:pict><v:shapetype id="_x0000_t75" coordsize="21600,21600" o:spt="75" o:preferrelative="t" path="m@4@5l@4@11@9@11@9@5xe" filled="f" stroked="f">""" +
        """<v:stroke joinstyle="miter"/><v:formulas><v:f eqn="if lineDrawn pixelLineWidth 0"/><v:f eqn="sum @0 1 0"/><v:f eqn="sum 0 0 @1"/><v:f eqn="prod @2 1 2"/><v:f eqn="prod @3 21600 pixelWidth"/><v:f eqn="prod @3 21600 pixelHeight"/><v:f eqn="sum @0 0 1"/><v:f eqn="prod @6 1 2"/><v:f eqn="prod @7 21600 pixelWidth"/><v:f eqn="sum @8 21600 0"/><v:f eqn="prod @7 21600 pixelHeight"/><v:f eqn="sum @10 21600 0"/></v:formulas>""" +
        """<v:path o:extrusionok="f" gradientshapeok="t" o:connecttype="rect"/><o:lock v:ext="edit" aspectratio="t"/></v:shapetype>""" +
        """<v:shape id="WordPictureWatermark1" o:spid="_x0000_s1026" type="#_x0000_t75" style="position:absolute;margin-left:0;margin-top:0;width:612pt;height:11in;z-index:-251653120;mso-position-horizontal:center;mso-position-horizontal-relative:margin;mso-position-vertical:center;mso-position-vertical-relative:margin" o:allowincell="f">""" +
        $"""<v:imagedata r:id="{relId}" o:title="benchmark-watermark" gain="19661f" blacklevel="22938f"/><w10:wrap anchorx="margin" anchory="margin"/></v:shape></w:pict>""";

    // ---- images ----

    static byte[] BuildLogoPng()
    {
        using var bitmap = new SKBitmap(300, 80);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(new(240, 244, 252));
        using var paint = new SKPaint();
        paint.IsAntialias = true;
        paint.Color = new(0x33, 0x66, 0x99);
        canvas.DrawRoundRect(new(8, 8, 120, 72), 12, 12, paint);
        paint.Color = new(0xE8, 0x9C, 0x2E);
        canvas.DrawCircle(160, 40, 28, paint);
        paint.Color = new(0x2E, 0x7D, 0x32);
        canvas.DrawRect(new(200, 16, 288, 64), paint);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 90);
        return data.ToArray();
    }

    static byte[] BuildIconPng()
    {
        using var bitmap = new SKBitmap(64, 64);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(new(255, 255, 255));
        using var paint = new SKPaint();
        paint.IsAntialias = true;
        paint.Color = new(0xC6, 0x28, 0x28);
        canvas.DrawCircle(32, 32, 26, paint);
        paint.Color = new(0xFF, 0xF9, 0xC4);
        canvas.DrawCircle(32, 32, 12, paint);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 90);
        return data.ToArray();
    }

    static byte[] BuildWatermarkPng()
    {
        using var bitmap = new SKBitmap(1200, 1600);
        using var canvas = new SKCanvas(bitmap);
        using var paint = new SKPaint();
        using var shader = SKShader.CreateLinearGradient(
            new(0, 0),
            new(1200, 1600),
            [new(0x1A, 0x23, 0x7E), new(0x00, 0x83, 0x8F), new(0xF9, 0xA8, 0x25)],
            SKShaderTileMode.Clamp);
        paint.Shader = shader;
        canvas.DrawRect(new(0, 0, 1200, 1600), paint);
        paint.Shader = null;
        paint.IsAntialias = true;
        paint.Color = new(255, 255, 255, 90);
        for (var i = 0; i < 12; i++)
        {
            canvas.DrawCircle(100 + i * 95, 200 + i % 4 * 350, 130, paint);
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 90);
        return data.ToArray();
    }

    static string BuildLogoSvg()
    {
        var builder = new StringBuilder();
        builder.Append("""<?xml version="1.0" encoding="UTF-8"?>""");
        builder.Append("""<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 240 240" width="240" height="240">""");
        // A <style> element plus class attributes so SvgPreprocessor.StripStyleAndClass has
        // representative work to do per load.
        builder.Append("<style>.accent{fill:#336699;}.muted{fill:#88aacc;}</style>");
        for (var i = 0; i < 20; i++)
        {
            builder.Append($"""<circle class="accent" cx="{20 + i * 10}" cy="{30 + i % 5 * 40}" r="9"/>""");
            builder.Append($"""<rect class="muted" x="{10 + i * 11}" y="{120 + i % 4 * 25}" width="14" height="14" rx="3"/>""");
            builder.Append($"""<path fill="#e89c2e" d="M{12 + i * 11} 220 l8 -14 l8 14 z"/>""");
        }

        builder.Append("</svg>");
        return builder.ToString();
    }

    // ---- documents ----

    static byte[] BuildHeaderLogoDoc(bool useSvg)
    {
        var body = new StringBuilder();
        AppendTextPages(body, pages: 12, paragraphsPerPage: 12);
        body.Append(SectionProperties("rHdr"));

        // Sized to fit the header band (0.5" header offset, 1" body margin → keep under 0.4" tall)
        // so the logo doesn't overlap body text.
        var headerImage = useSvg
            ? InlineImage(1, "rLogoFallback", 365760, 365760, svgRelId: "rLogoSvg")
            : InlineImage(1, "rLogo", 1371600, 365760);
        var header =
            $"""<?xml version="1.0" encoding="utf-8"?><w:hdr {documentNamespaces}><w:p><w:r>{headerImage}</w:r><w:r><w:t xml:space="preserve"> Morph benchmark report</w:t></w:r></w:p></w:hdr>""";

        var headerRels = useSvg
            ? $"""<?xml version="1.0" encoding="utf-8"?><Relationships xmlns="{relsNs}"><Relationship Id="rLogoSvg" Type="{imageRelType}" Target="media/logo.svg"/><Relationship Id="rLogoFallback" Type="{imageRelType}" Target="media/logo.png"/></Relationships>"""
            : $"""<?xml version="1.0" encoding="utf-8"?><Relationships xmlns="{relsNs}"><Relationship Id="rLogo" Type="{imageRelType}" Target="media/logo.png"/></Relationships>""";

        var parts = new List<(string, byte[])>
        {
            (contentTypesPath, Utf8(ContentTypes(header: true, styles: false, svg: useSvg))),
            ("_rels/.rels", Utf8(packageRels)),
            ("word/document.xml", Utf8($"""<?xml version="1.0" encoding="utf-8"?><w:document {documentNamespaces}><w:body>{body}</w:body></w:document>""")),
            ("word/_rels/document.xml.rels", Utf8($"""<?xml version="1.0" encoding="utf-8"?><Relationships xmlns="{relsNs}"><Relationship Id="rHdr" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/header" Target="header1.xml"/></Relationships>""")),
            ("word/header1.xml", Utf8(header)),
            ("word/_rels/header1.xml.rels", Utf8(headerRels)),
            ("word/media/logo.png", BuildLogoPng())
        };
        if (useSvg)
        {
            parts.Add(("word/media/logo.svg", Utf8(BuildLogoSvg())));
        }

        return Zip(parts);
    }

    static byte[] BuildPictureWatermarkDoc()
    {
        var body = new StringBuilder();
        AppendTextPages(body, pages: 12, paragraphsPerPage: 12);
        body.Append(SectionProperties("rHdr"));

        var header =
            $"""<?xml version="1.0" encoding="utf-8"?><w:hdr {documentNamespaces}><w:p><w:r>{WatermarkPict("rWm")}</w:r></w:p></w:hdr>""";

        return Zip(
        [
            (contentTypesPath, Utf8(ContentTypes(header: true, styles: false, svg: false))),
            ("_rels/.rels", Utf8(packageRels)),
            ("word/document.xml", Utf8($"""<?xml version="1.0" encoding="utf-8"?><w:document {documentNamespaces}><w:body>{body}</w:body></w:document>""")),
            ("word/_rels/document.xml.rels", Utf8($"""<?xml version="1.0" encoding="utf-8"?><Relationships xmlns="{relsNs}"><Relationship Id="rHdr" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/header" Target="header1.xml"/></Relationships>""")),
            ("word/header1.xml", Utf8(header)),
            ("word/_rels/header1.xml.rels", Utf8($"""<?xml version="1.0" encoding="utf-8"?><Relationships xmlns="{relsNs}"><Relationship Id="rWm" Type="{imageRelType}" Target="media/watermark.png"/></Relationships>""")),
            ("word/media/watermark.png", BuildWatermarkPng())
        ]);
    }

    static byte[] BuildRepeatedImageDoc()
    {
        var body = new StringBuilder();
        for (var page = 0; page < 6; page++)
        {
            for (var index = 0; index < 10; index++)
            {
                var id = page * 10 + index + 1;
                body.Append($"""<w:p><w:r>{InlineImage(id, "rImg", 457200, 457200)}</w:r><w:r><w:t xml:space="preserve"> Repeated icon {id}</w:t></w:r></w:p>""");
            }

            if (page < 5)
            {
                body.Append("""<w:p><w:r><w:br w:type="page"/></w:r></w:p>""");
            }
        }

        body.Append(SectionProperties(null));

        return Zip(
        [
            (contentTypesPath, Utf8(ContentTypes(header: false, styles: false, svg: false))),
            ("_rels/.rels", Utf8(packageRels)),
            ("word/document.xml", Utf8($"""<?xml version="1.0" encoding="utf-8"?><w:document {documentNamespaces}><w:body>{body}</w:body></w:document>""")),
            ("word/_rels/document.xml.rels", Utf8($"""<?xml version="1.0" encoding="utf-8"?><Relationships xmlns="{relsNs}"><Relationship Id="rImg" Type="{imageRelType}" Target="media/icon.png"/></Relationships>""")),
            ("word/media/icon.png", BuildIconPng())
        ]);
    }

    static byte[] BuildTocDoc()
    {
        const int entries = 300;
        const int plainTabLines = 150;

        var body = new StringBuilder();
        var rels = new StringBuilder();
        rels.Append($"""<?xml version="1.0" encoding="utf-8"?><Relationships xmlns="{relsNs}">""");
        rels.Append("""<Relationship Id="rStyles" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/>""");

        for (var i = 0; i < entries; i++)
        {
            rels.Append($"""<Relationship Id="lnk{i}" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/hyperlink" Target="https://example.com/section/{i}" TargetMode="External"/>""");
            body.Append($"""<w:p><w:pPr><w:tabs><w:tab w:val="right" w:leader="dot" w:pos="9350"/></w:tabs></w:pPr><w:hyperlink r:id="lnk{i}" w:history="1"><w:r><w:rPr><w:rStyle w:val="Hyperlink"/></w:rPr><w:t xml:space="preserve">Section {i}: {Sentence(i, 6)}</w:t></w:r></w:hyperlink><w:r><w:tab/></w:r><w:r><w:t>{i + 1}</w:t></w:r></w:p>""");
        }

        for (var i = 0; i < plainTabLines; i++)
        {
            body.Append($"""<w:p><w:r><w:t xml:space="preserve">Field {i}</w:t></w:r><w:r><w:tab/></w:r><w:r><w:t xml:space="preserve">{Sentence(i + entries, 5)}</w:t></w:r></w:p>""");
        }

        body.Append(SectionProperties(null));
        rels.Append("</Relationships>");

        // 200 filler character styles ahead of Hyperlink so a linear styles.xml scan pays a
        // representative cost for every styled run.
        var styles = new StringBuilder();
        styles.Append("""<?xml version="1.0" encoding="utf-8"?><w:styles xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">""");
        for (var i = 0; i < 200; i++)
        {
            styles.Append($"""<w:style w:type="character" w:styleId="Filler{i}"><w:name w:val="Filler {i}"/><w:rPr><w:color w:val="3355{i % 100:00}"/></w:rPr></w:style>""");
        }

        styles.Append("""<w:style w:type="character" w:styleId="Hyperlink"><w:name w:val="Hyperlink"/><w:rPr><w:color w:val="0563C1"/><w:u w:val="single"/></w:rPr></w:style>""");
        styles.Append("</w:styles>");

        return Zip(
        [
            (contentTypesPath, Utf8(ContentTypes(header: false, styles: true, svg: false))),
            ("_rels/.rels", Utf8(packageRels)),
            ("word/document.xml", Utf8($"""<?xml version="1.0" encoding="utf-8"?><w:document {documentNamespaces}><w:body>{body}</w:body></w:document>""")),
            ("word/_rels/document.xml.rels", Utf8(rels.ToString())),
            ("word/styles.xml", Utf8(styles.ToString()))
        ]);
    }
}
