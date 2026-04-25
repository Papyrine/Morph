#:package DocumentFormat.OpenXml@3.3.0
using System.IO;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

var source = args[0];
var target = args[1];
File.Copy(source, target, overwrite: true);

using var doc = WordprocessingDocument.Open(target, isEditable: true);
var mainPart = doc.MainDocumentPart!;
var body = mainPart.Document.Body!;
foreach (var p in body.Elements<Paragraph>().ToList()) p.Remove();

// Pin Aptos as the default font.
var stylesPart = mainPart.StyleDefinitionsPart ?? mainPart.AddNewPart<StyleDefinitionsPart>();
var styles = stylesPart.Styles ??= new Styles();
var docDefaults = styles.DocDefaults ??= new DocDefaults();
var rPrDefault = docDefaults.RunPropertiesDefault ??= new RunPropertiesDefault();
rPrDefault.RunPropertiesBaseStyle = new RunPropertiesBaseStyle(
    new RunFonts { Ascii = "Aptos", HighAnsi = "Aptos", ComplexScript = "Aptos" });
stylesPart.Styles.Save();

var sectPr = body.Elements<SectionProperties>().Last();
sectPr.Remove();

// Each paragraph has a tab character followed by a numeric value with decimal point.
// The tab stop is set at 3.5 inches (5040 twips) with Decimal alignment, so the decimal
// points should align vertically across all rows.
Paragraph MakeRow(string label, string value)
{
    var p = new Paragraph();
    var pPr = new ParagraphProperties();
    var tabs = new Tabs();
    tabs.Append(new TabStop { Val = TabStopValues.Decimal, Position = 5040 });
    pPr.Append(tabs);
    p.Append(pPr);
    p.Append(new Run(new Text(label) { Space = SpaceProcessingModeValues.Preserve }));
    p.Append(new Run(new TabChar()));
    p.Append(new Run(new Text(value)));
    return p;
}

body.Append(MakeRow("Apples", "12.50"));
body.Append(MakeRow("Bananas", "3.14"));
body.Append(MakeRow("Cherries", "100.99"));
body.Append(MakeRow("Dates", "0.05"));

body.Append(sectPr);
mainPart.Document.Save();
Console.WriteLine($"Updated {target}");
