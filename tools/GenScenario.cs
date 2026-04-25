#:package DocumentFormat.OpenXml@3.3.0
using System.IO;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Office2010.Word;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using W14 = DocumentFormat.OpenXml.Office2010.Word;

var source = args[0];
var target = args[1];
File.Copy(source, target, overwrite: true);

using var doc = WordprocessingDocument.Open(target, isEditable: true);
var body = doc.MainDocumentPart!.Document.Body!;
foreach (var p in body.Elements<Paragraph>().ToList()) p.Remove();

var sectPr = body.Elements<SectionProperties>().Last();
sectPr.Remove();

// Paragraph 1: kern=24, smallCaps, ligatures=none, w14:shadow, w:rtl on a single run.
var p1 = new Paragraph();
var p1Pr = new ParagraphProperties();
p1Pr.Append(new BiDi()); // paragraph reads RTL
p1.Append(p1Pr);

var rPr = new RunProperties();
rPr.Append(new Kern { Val = 24U });
rPr.Append(new SmallCaps());
rPr.Append(new W14.Ligatures { Val = LigaturesValues.None });
rPr.Append(new W14.Shadow());
rPr.Append(new W14.TextOutlineEffect());
rPr.Append(new W14.Glow());
rPr.Append(new W14.Reflection());
rPr.Append(new RightToLeftText());

var run = new Run();
run.Append(rPr);
run.Append(new Text("All features"));
p1.Append(run);
body.Append(p1);

// Paragraph 2: drop cap.
var p2 = new Paragraph();
var p2Pr = new ParagraphProperties();
var framePr = new FrameProperties { Lines = 3 };
framePr.SetAttribute(new OpenXmlAttribute("w", "dropCap", "http://schemas.openxmlformats.org/wordprocessingml/2006/main", "drop"));
p2Pr.Append(framePr);
p2.Append(p2Pr);
p2.Append(new Run(new Text("Drop cap paragraph")));
body.Append(p2);

// Table: tblLayout=fixed, header row, cell with textDirection=btLr.
var table = new Table();
var tblPr = new TableProperties();
tblPr.Append(new TableWidth { Type = TableWidthUnitValues.Dxa, Width = "3000" });
tblPr.Append(new TableLayout { Type = TableLayoutValues.Fixed });
table.Append(tblPr);

var grid = new TableGrid();
grid.Append(new GridColumn { Width = "1500" });
grid.Append(new GridColumn { Width = "1500" });
table.Append(grid);

var headerRow = new TableRow();
var trPr = new TableRowProperties();
trPr.Append(new TableHeader());
headerRow.Append(trPr);
var headerCell = new TableCell();
var hCellPr = new TableCellProperties();
hCellPr.Append(new TableCellWidth { Type = TableWidthUnitValues.Dxa, Width = "1500" });
hCellPr.Append(new TextDirection { Val = TextDirectionValues.BottomToTopLeftToRight });
headerCell.Append(hCellPr);
headerCell.Append(new Paragraph(new Run(new Text("Header"))));
headerRow.Append(headerCell);
var headerCell2 = new TableCell();
headerCell2.Append(new TableCellProperties(new TableCellWidth { Type = TableWidthUnitValues.Dxa, Width = "1500" }));
headerCell2.Append(new Paragraph(new Run(new Text("Header2"))));
headerRow.Append(headerCell2);
table.Append(headerRow);

var dataRow = new TableRow();
var dataCell = new TableCell();
dataCell.Append(new TableCellProperties(new TableCellWidth { Type = TableWidthUnitValues.Dxa, Width = "1500" }));
dataCell.Append(new Paragraph(new Run(new Text("Data"))));
dataRow.Append(dataCell);
var dataCell2 = new TableCell();
dataCell2.Append(new TableCellProperties(new TableCellWidth { Type = TableWidthUnitValues.Dxa, Width = "1500" }));
dataCell2.Append(new Paragraph(new Run(new Text("Data2"))));
dataRow.Append(dataCell2);
table.Append(dataRow);

body.Append(table);

body.Append(sectPr);

doc.MainDocumentPart.Document.Save();
Console.WriteLine($"Updated {target}");
