#:package DocumentFormat.OpenXml@3.3.0
using System.IO;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using M = DocumentFormat.OpenXml.Math;

var source = args[0];
var target = args[1];
File.Copy(source, target, overwrite: true);

using var doc = WordprocessingDocument.Open(target, isEditable: true);
var mainPart = doc.MainDocumentPart!;
var body = mainPart.Document.Body!;
foreach (var p in body.Elements<Paragraph>().ToList()) p.Remove();

var sectPr = body.Elements<SectionProperties>().Last();
sectPr.Remove();

// 1. Footnote definition + reference.
var footnotesPart = mainPart.AddNewPart<FootnotesPart>();
var footnotes = new Footnotes();
// Required separator entries.
footnotes.Append(new Footnote { Id = -1, Type = FootnoteEndnoteValues.Separator });
footnotes.Append(new Footnote { Id = 0, Type = FootnoteEndnoteValues.ContinuationSeparator });
var fn = new Footnote { Id = 1 };
fn.Append(new Paragraph(new Run(new Text("This is a footnote."))));
footnotes.Append(fn);
footnotesPart.Footnotes = footnotes;
footnotesPart.Footnotes.Save();

// 2. Endnote definition.
var endnotesPart = mainPart.AddNewPart<EndnotesPart>();
var endnotes = new Endnotes();
endnotes.Append(new Endnote { Id = -1, Type = FootnoteEndnoteValues.Separator });
endnotes.Append(new Endnote { Id = 0, Type = FootnoteEndnoteValues.ContinuationSeparator });
var en = new Endnote { Id = 1 };
en.Append(new Paragraph(new Run(new Text("This is an endnote."))));
endnotes.Append(en);
endnotesPart.Endnotes = endnotes;
endnotesPart.Endnotes.Save();

// 3. Body paragraphs with references and an OLE placeholder.
var pFn = new Paragraph();
pFn.Append(new Run(new Text("Footnote ref ")));
pFn.Append(new Run(new FootnoteReference { Id = 1 }));
body.Append(pFn);

var pEn = new Paragraph();
pEn.Append(new Run(new Text("Endnote ref ")));
pEn.Append(new Run(new EndnoteReference { Id = 1 }));
body.Append(pEn);

// 4. Math equation.
var pMath = new Paragraph();
var oMathPara = new M.Paragraph();
var oMath = new M.OfficeMath();
var mr = new M.Run();
mr.Append(new M.Text("x"));
oMath.Append(mr);
oMathPara.Append(oMath);
pMath.Append(oMathPara);
body.Append(pMath);

body.Append(sectPr);
mainPart.Document.Save();
Console.WriteLine($"Updated {target}");
