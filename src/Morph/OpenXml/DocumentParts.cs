namespace Morph;

/// <summary>
/// Parts of a DOCX package that hold no information Morph renders, selectable for removal
/// by <see cref="DocumentCleaner"/>. None of these are reachable from <c>word/document.xml</c>
/// content, so dropping them cannot change the rendered output of any format.
/// </summary>
[Flags]
public enum DocumentParts
{
    /// <summary>Nothing.</summary>
    None = 0,

    /// <summary>
    /// The package preview picture (<c>docProps/thumbnail.emf</c> and friends), written by Word
    /// when "Save Thumbnails" is on and shown by Explorer. Frequently the single largest part in
    /// a template — routinely more than 90% of the file.
    /// </summary>
    Thumbnail = 1 << 0,

    /// <summary>
    /// The glossary document (<c>word/glossary/</c>), which stores building blocks and Quick Parts.
    /// Only consumed by Word's insert-a-building-block UI, never by document body content.
    /// </summary>
    Glossary = 1 << 1,

    /// <summary>
    /// Custom XML data islands (<c>customXml/</c>) and their property parts. Content controls may
    /// carry a <c>w:dataBinding</c> to one of these, but the bound value is also cached inline in
    /// <c>word/document.xml</c>, which is what every reader — including Word, until it refreshes —
    /// displays. Removing them can therefore change what Word shows if the island and the cache
    /// have drifted apart.
    /// </summary>
    CustomXml = 1 << 2,

    /// <summary>
    /// The revision-author list (<c>word/people.xml</c>), which maps tracked-change author IDs to
    /// display names and presence information.
    /// </summary>
    RevisionAuthors = 1 << 3,

    /// <summary>Every part this enum can describe.</summary>
    All = Thumbnail | Glossary | CustomXml | RevisionAuthors,
}
