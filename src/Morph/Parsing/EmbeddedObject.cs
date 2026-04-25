/// <summary>
/// An embedded OLE object reference (w:object / o:OLEObject). Currently captured as a
/// placeholder — the embedded payload itself is not rendered.
/// </summary>
sealed record EmbeddedObject
{
    /// <summary>The ProgID of the embedded object (e.g. <c>Excel.Sheet.12</c>) when known.</summary>
    public string? ProgId { get; init; }

    /// <summary>The relationship id of the embedded payload (r:id), when present.</summary>
    public string? RelationshipId { get; init; }
}
