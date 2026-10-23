using DW = DocumentFormat.OpenXml.Drawing.Wordprocessing;
using OoxmlEmbeddedObject = DocumentFormat.OpenXml.Wordprocessing.EmbeddedObject;
using OoxmlRun = DocumentFormat.OpenXml.Wordprocessing.Run;

/// <summary>
/// What <see cref="DocumentParser"/> reads off the body beside its block structure — the section
/// breaks, the fields, the tracked changes, the comment and bookmark anchors, the embedded objects and
/// the feature flags — gathered in ONE walk of it. Each was a whole-body walk of its own, a dozen in
/// all, which together cost several times the one walk that replaces them.
/// The parser never restructures the body, so a walk at the start sees what each of them saw.
/// </summary>
sealed class BodyScan
{
    public int LastRenderedPageBreaks { get; }
    public bool HasAnchors { get; private set; }
    public List<Paragraph> Paragraphs { get; } = [];
    public List<OoxmlRun> Runs { get; } = [];
    public List<SectionProperties> Sections { get; } = [];
    public List<SimpleField> SimpleFields { get; } = [];
    public List<InsertedRun> Insertions { get; } = [];
    public List<DeletedRun> Deletions { get; } = [];
    public List<CommentRangeStart> CommentStarts { get; } = [];
    public List<BookmarkStart> BookmarkStarts { get; } = [];
    public List<OoxmlEmbeddedObject> EmbeddedObjects { get; } = [];

    bool hasCharts;
    bool hasSmartArt;
    bool hasMath;
    bool hasGradients;
    bool hasBezier;
    bool has3d;
    bool hasConnectors;
    bool hasDuotone;

    Dictionary<Paragraph, int>? paragraphOrdinals;

    public BodyScan(Body body)
    {
        foreach (var element in body.Descendants())
        {
            switch (element)
            {
                case Paragraph paragraph:
                    Paragraphs.Add(paragraph);
                    break;
                case OoxmlRun run:
                    Runs.Add(run);
                    break;
                case LastRenderedPageBreak:
                    LastRenderedPageBreaks++;
                    break;
                case SectionProperties section:
                    Sections.Add(section);
                    break;
                case SimpleField field:
                    SimpleFields.Add(field);
                    break;
                case InsertedRun inserted:
                    Insertions.Add(inserted);
                    break;
                case DeletedRun deleted:
                    Deletions.Add(deleted);
                    break;
                case CommentRangeStart start:
                    CommentStarts.Add(start);
                    break;
                case BookmarkStart start:
                    BookmarkStarts.Add(start);
                    break;
                case OoxmlEmbeddedObject embedded:
                    EmbeddedObjects.Add(embedded);
                    break;
                case DW.Anchor:
                    HasAnchors = true;
                    break;
                default:
                    Detect(element);
                    break;
            }
        }
    }

    // The paragraph ordinals that bookmarks and comments anchor to, built the first time one asks.
    public Dictionary<Paragraph, int> ParagraphOrdinals()
    {
        if (paragraphOrdinals == null)
        {
            paragraphOrdinals = [with(Paragraphs.Count)];
            for (var ordinal = 0; ordinal < Paragraphs.Count; ordinal++)
            {
                paragraphOrdinals[Paragraphs[ordinal]] = ordinal;
            }
        }

        return paragraphOrdinals;
    }

    // hasWatermark is determined from the header parts, extracted alongside the watermark list.
    public DocumentFeatures Features(bool hasWatermark) =>
        new()
        {
            HasCharts = hasCharts,
            HasSmartArt = hasSmartArt,
            HasMath = hasMath,
            HasWatermarks = hasWatermark,
            HasGradientFills = hasGradients,
            HasBezierShapes = hasBezier,
            Has3dEffects = has3d,
            HasConnectors = hasConnectors,
            HasDuotoneEffects = hasDuotone
        };

    void Detect(OpenXmlElement element)
    {
        switch (element.LocalName)
        {
            case "oMath":
            case "oMathPara":
                hasMath = true;
                break;
            case "graphicData":
                var uri = element.AttributeValue("uri");
                if (uri == "http://schemas.openxmlformats.org/drawingml/2006/chart")
                {
                    hasCharts = true;
                }
                else if (uri == "http://schemas.openxmlformats.org/drawingml/2006/diagram")
                {
                    hasSmartArt = true;
                }

                break;
            case "gradFill":
                hasGradients = true;
                break;
            case "custGeom":
                hasBezier = true;
                break;
            case "sp3d":
            case "scene3d":
                has3d = true;
                break;
            case "cxnSp":
                hasConnectors = true;
                break;
            case "duotone":
            case "clrChange":
                hasDuotone = true;
                break;
        }
    }
}
